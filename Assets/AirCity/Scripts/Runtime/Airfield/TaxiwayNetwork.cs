using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    [Serializable]
    public class TaxiwayNode
    {
        public int id;
        public Vector3 position;
        [Tooltip("The runway this node sits on (a runway entrance, exit or crossing), or none.")]
        public Runway runway;
    }

    [Serializable]
    public class TaxiwayEdge
    {
        public int id, a, b;
        [Tooltip("Paved width (metres). See RunwayRules.TaxiwayWidths.")]
        public float width = 15f;
        public PavementSurface surface = PavementSurface.Asphalt;
        [Tooltip("Blue edge lights (an upgrade, like runway lighting). Dirt and grass can't be lit.")]
        public bool lit;
        [Range(0f, 1f)] public float wear;
        [Tooltip("Lying snow (cm). Builds up while it snows; ploughing clears it.")]
        [Min(0f)] public float snow;
        [Tooltip("Grass length (cm), grass taxiways only. Mowing cuts it.")]
        [Min(0f)] public float grass = RunwayRules.CutGrass;
        [Tooltip("A road rather than a taxiway (aircraft never route on roads). Service roads are airside, for the " +
                 "airport's own vehicles, with white markings and no lights or verges; streets and highways come with " +
                 "road traffic. Across a paved apron a road is only its paint.")]
        public RoadClass road;
        [Tooltip("Grass length (cm) on the verges either side (paved taxiways). Grows; the mowers cut it.")]
        [Min(0f)] public float vergeGrass = RunwayRules.CutGrass;
        /// <summary>The ploughs or mowers assigned to this edge (strip coordinates from node a), or null.</summary>
        [NonSerialized] public StripWork work;
        [NonSerialized] public Vector4 stripes = StripWork.Idle;
        /// <summary>The mowers assigned to its verges, or null.</summary>
        [NonSerialized] public StripWork vergeWork;
        [NonSerialized] public Vector4 vergeStripes = StripWork.Idle;

        public bool IsRoad => road != RoadClass.None;

        /// <summary>Paved taxiways have mown verges; roads and unpaved taxiways don't.</summary>
        public bool HasVerges => !IsRoad && surface.IsPaved();
        public float VergeHalfWidth => RunwayRules.TaxiwayVerge(width);

        internal TaxiwayEdge CopyState(int id, int a, int b) => new TaxiwayEdge
        {
            id = id, a = a, b = b, width = width, surface = surface, lit = lit, wear = wear, snow = snow, grass = grass,
            road = road, vergeGrass = vergeGrass,
        };
    }

    /// <summary>
    /// The airport's taxiways and airside service roads: a graph of straight edges between nodes. The geometry is
    /// generated from it (TaxiwayBuilder): bends become curves sized for the width's aircraft, junctions get
    /// fillets, and edges that meet a runway flare into it with holding-position markings. Roads are edges too
    /// (TaxiwayEdge.road, a RoadClass), drawn with white markings and give-way lines where they cross taxiways, and
    /// under paved aprons, so across one they are only their paint. The same graph
    /// routes aircraft (taxiways only) and the airport's vehicles (roads first, across aprons too). Each edge has
    /// its own wear, snow and grass, and paved taxiways have mown verges; snow (and long grass) closes an edge
    /// until the depots' teams have ploughed (mowed) it. Aircraft stands and de-icing bays open onto taxiways and
    /// are built with them (stands, StandLayout); a taxiway lined with stands is an apron taxilane.
    ///
    ///   int a = net.AddNode(p0), b = net.AddNode(p1);
    ///   net.Connect(a, b, 15f, PavementSurface.Concrete);   // splits crossings, adds runway crossings
    ///   net.Connect(c, d, RunwayRules.RoadWidth, PavementSurface.Asphalt, road: RoadClass.Service);
    ///   net.AddStand(StandKind.Parking, onCentreline, bearing, SizeCode.C, PavementSurface.Concrete);
    ///   net.FindRoute(a, b, type);                           // node ids, or null
    ///
    /// Positions are in the Airport's space.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class TaxiwayNetwork : MonoBehaviour
    {
        public List<TaxiwayNode> nodes = new List<TaxiwayNode>();
        public List<TaxiwayEdge> edges = new List<TaxiwayEdge>();
        public List<Stand> stands = new List<Stand>();
        [SerializeField] int nextId = 1;

        AirfieldPart surfacePart, paintPart, glowPart, fixturePart, vergePart;
        readonly SurfaceState state = new SurfaceState(), vergeState = new SurfaceState();
        List<Owner> owners = new List<Owner>();
        List<TaxiwayEdge> vergeOwners = new List<TaxiwayEdge>();
        bool dirty;

        public Airport Airport => GetComponentInParent<Airport>();

        // ------------------------------------------------------------------ graph

        public TaxiwayNode Node(int id) => nodes.Find(n => n.id == id);
        public TaxiwayEdge Edge(int id) => edges.Find(e => e.id == id);

        public IEnumerable<TaxiwayEdge> EdgesAt(int node)
        {
            foreach (var e in edges)
                if (e.a == node || e.b == node) yield return e;
        }

        public float EdgeLength(TaxiwayEdge e) => Vector2.Distance(Geo.XZ(Node(e.a).position), Geo.XZ(Node(e.b).position));

        /// <summary>A node's ground position (x, z).</summary>
        public Vector2 At(int node) => Geo.XZ(Node(node).position);

        public static int Other(TaxiwayEdge e, int node) => e.a == node ? e.b : e.a;

        public int AddNode(Vector3 position, Runway runway = null)
        {
            if (runway != null) position = runway.ClosestOnCentreline(position);
            var n = new TaxiwayNode { id = nextId++, position = new Vector3(position.x, 0f, position.z), runway = runway };
            nodes.Add(n);
            return n.id;
        }

        /// <summary>The nearest node within radius (metres), or null.</summary>
        public TaxiwayNode NearestNode(Vector3 p, float radius)
        {
            TaxiwayNode best = null;
            float bestD = radius * radius;
            foreach (var n in nodes)
            {
                float d = (Geo.XZ(n.position) - Geo.XZ(p)).sqrMagnitude;
                if (d <= bestD)
                {
                    bestD = d;
                    best = n;
                }
            }
            return best;
        }

        /// <summary>The nearest edge within radius of p, and the closest point on it.</summary>
        public bool TryNearestEdge(Vector3 p, float radius, out TaxiwayEdge edge, out Vector3 point)
        {
            edge = null;
            point = default;
            float bestD = radius;
            foreach (var e in edges)
            {
                var a = Geo.XZ(Node(e.a).position);
                var b = Geo.XZ(Node(e.b).position);
                var c = Geo.ClosestOnSegment(Geo.XZ(p), a, b, out _);
                float d = Vector2.Distance(c, Geo.XZ(p));
                if (d <= bestD)
                {
                    bestD = d;
                    edge = e;
                    point = Geo.X0Z(c);
                }
            }
            return edge != null;
        }

        /// <summary>Split an edge at a point (projected onto it); returns the new node's id.</summary>
        public int SplitEdge(TaxiwayEdge e, Vector3 at)
        {
            var a = Geo.XZ(Node(e.a).position);
            var b = Geo.XZ(Node(e.b).position);
            var c = Geo.ClosestOnSegment(Geo.XZ(at), a, b, out _);
            int mid = AddNode(Geo.X0Z(c));
            edges.Remove(e);
            edges.Add(e.CopyState(nextId++, e.a, mid));
            edges.Add(e.CopyState(nextId++, mid, e.b));
            return mid;
        }

        /// <summary>
        /// Join two nodes with a taxiway (or a service road). Where it crosses existing taxiways or roads both are
        /// split at a shared node, and where it crosses a runway a runway node is added, so the graph always
        /// matches the pavement. Returns the edges made (a crossing makes several).
        /// </summary>
        public List<TaxiwayEdge> Connect(int a, int b, float width, PavementSurface surface, bool lit = false,
            RoadClass road = RoadClass.None)
        {
            var made = new List<TaxiwayEdge>();
            var na = Node(a);
            var nb = Node(b);
            if (na == null || nb == null || a == b) return made;
            var pa = Geo.XZ(na.position);
            var pb = Geo.XZ(nb.position);
            float length = Vector2.Distance(pa, pb);
            if (length < 1f) return made;

            var stops = new List<(float t, int node)> { (0f, a), (1f, b) };
            foreach (var e in edges.ToArray())
            {
                if (e.a == a || e.a == b || e.b == a || e.b == b) continue;
                var ea = Geo.XZ(Node(e.a).position);
                var eb = Geo.XZ(Node(e.b).position);
                if (!Geo.Segments(pa, pb, ea, eb, 0.5f, out var at)) continue;
                stops.Add((Vector2.Distance(pa, at) / length, SplitEdge(e, Geo.X0Z(at))));
            }
            var airport = Airport;
            if (airport != null)
                foreach (var r in airport.Runways)
                {
                    if (na.runway == r || nb.runway == r) continue;
                    var ra = Geo.XZ(r.start);
                    var rb = Geo.XZ(r.end);
                    if (!Geo.Lines(pa, pb - pa, ra, rb - ra, out float t, out float u)) continue;
                    if (t * length < 1f || (1f - t) * length < 1f || u < 0f || u > 1f) continue;
                    stops.Add((t, AddNode(Geo.X0Z(Vector2.Lerp(pa, pb, t)), r)));
                }
            stops.Sort((x, y) => x.t.CompareTo(y.t));
            for (int i = 0; i < stops.Count - 1; i++)
            {
                int from = stops[i].node, to = stops[i + 1].node;
                if (from == to) continue;
                var existing = edges.Find(e => (e.a == from && e.b == to) || (e.a == to && e.b == from));
                if (existing != null)
                {
                    existing.width = width;
                    existing.surface = surface;
                    existing.road = road;
                    existing.lit = lit && surface.IsPaved() && road == RoadClass.None;
                    made.Add(existing);
                    continue;
                }
                var edge = new TaxiwayEdge { id = nextId++, a = from, b = to, width = width, surface = surface, road = road,
                    lit = lit && surface.IsPaved() && road == RoadClass.None };
                edges.Add(edge);
                made.Add(edge);
            }
            return made;
        }

        public void RemoveEdge(int id)
        {
            var e = Edge(id);
            if (e == null) return;
            edges.Remove(e);
            RemoveOrphans();
        }

        public void RemoveNode(int id)
        {
            edges.RemoveAll(e => e.a == id || e.b == id);
            nodes.RemoveAll(n => n.id == id);
            RemoveOrphans();
        }

        /// <summary>Nodes without edges go, and so do stands without a taxiway.</summary>
        // stands stay when their taxiway goes: they are just no longer connected
        void RemoveOrphans() => nodes.RemoveAll(n => !edges.Exists(e => e.a == n.id || e.b == n.id));

        /// <summary>
        /// Remove taxiway / road sections and the nodes left without any, then join up what they had split: where a
        /// plain node is left between two matching sections running straight on, they become one again. The caller
        /// rebuilds.
        /// </summary>
        public void RemoveEdges(IEnumerable<TaxiwayEdge> remove)
        {
            var ends = new HashSet<int>();
            foreach (var e in new List<TaxiwayEdge>(remove))
                if (edges.Remove(e))
                {
                    ends.Add(e.a);
                    ends.Add(e.b);
                }
            RemoveOrphans();
            foreach (int n in ends) Rejoin(n);
        }

        /// <summary>Join the two sections at a plain node (not on a runway) into one, if they run straight on
        /// (within a degree), are alike (width, surface, road, lights) and nobody is working on them.</summary>
        bool Rejoin(int node)
        {
            var n = Node(node);
            if (n == null || n.runway != null) return false;
            var at = new List<TaxiwayEdge>(EdgesAt(node));
            if (at.Count != 2) return false;
            TaxiwayEdge e0 = at[0], e1 = at[1];
            if (e0.road != e1.road || e0.surface != e1.surface || e0.lit != e1.lit || Mathf.Abs(e0.width - e1.width) > 0.01f) return false;
            if (e0.work != null || e1.work != null || e0.vergeWork != null || e1.vergeWork != null) return false;
            int a = Other(e0, node), b = Other(e1, node);
            if (a == b) return false;
            var p = At(node);
            if (Vector2.Angle(p - At(a), At(b) - p) > 1f) return false;
            var joined = e0.CopyState(nextId++, a, b);
            joined.wear = Mathf.Max(e0.wear, e1.wear);
            joined.snow = Mathf.Max(e0.snow, e1.snow);
            joined.grass = Mathf.Max(e0.grass, e1.grass);
            joined.vergeGrass = Mathf.Max(e0.vergeGrass, e1.vergeGrass);
            edges.Remove(e0);
            edges.Remove(e1);
            edges.Add(joined);
            nodes.Remove(n);
            return true;
        }

        /// <summary>The taxiway or road section under a ground point (within its half-width plus margin), the one
        /// with the nearest centreline if several.</summary>
        public bool TryPickEdge(Vector3 p, out TaxiwayEdge edge, float margin = 0f)
        {
            edge = null;
            float best = float.MaxValue;
            var q = Geo.XZ(p);
            foreach (var e in edges)
            {
                float d = Vector2.Distance(Geo.ClosestOnSegment(q, At(e.a), At(e.b), out _), q);
                if (d <= e.width * 0.5f + margin && d < best)
                {
                    best = d;
                    edge = e;
                }
            }
            return edge != null;
        }

        /// <summary>A runway was removed: its nodes become ordinary junctions.</summary>
        public void ForgetRunway(Runway r)
        {
            foreach (var n in nodes)
                if (n.runway == r) n.runway = null;
        }

        // ------------------------------------------------------------------ stands and de-icing bays

        /// <summary>
        /// A parking stand or de-icing bay opening onto the taxiway under `onCentreline` (its front line that taxiway,
        /// laid out for its width): a stand's centreline runs away from the taxiway on `bearing` (degrees clockwise
        /// from north); a bay runs along it, aircraft taxiing through on `bearing`. Doesn't check it fits (see Fits);
        /// null if no taxiway is there or the angle is wrong. Rebuild to see it. For a stand with no taxiway yet, frame
        /// a Stand and use Add.
        /// </summary>
        public Stand AddStand(StandKind kind, Vector3 onCentreline, float bearing, SizeCode code, PavementSurface surface,
            StandExit exit = StandExit.PushBack)
        {
            var stand = new Stand
            {
                kind = kind, code = code, surface = surface, exit = exit,
                position = new Vector3(onCentreline.x, 0f, onCentreline.z), heading = Mathf.Repeat(bearing, 360f),
            };
            return FrameFromTaxiway(stand) ? Add(stand) : null;
        }

        /// <summary>Add a stand framed with its own front line (Stand.Frame), connected or not; numbered with the
        /// lowest number free for its kind. Doesn't check it fits (see Fits); null if it can't be laid out.</summary>
        public Stand Add(Stand stand)
        {
            if (!stand.Framed || StandLayout.Of(stand) == null) return null;
            if (stand.DeIcing && !stand.surface.IsPaved()) stand.surface = PavementSurface.Concrete;
            stand.id = nextId++;
            stand.heading = Mathf.Repeat(stand.heading, 360f);
            stand.position = new Vector3(stand.position.x, 0f, stand.position.z);
            int number = 1;
            while (stands.Exists(x => x.kind == stand.kind && x.number == number)) number++;
            stand.number = number;
            stands.Add(stand);
            return stand;
        }

        /// <summary>Give a stand the front line of the taxiway section it sits on (its position onto that centreline);
        /// false if it sits on none.</summary>
        bool FrameFromTaxiway(Stand stand)
        {
            foreach (var e in edges)
            {
                var na = Node(e.a);
                var nb = Node(e.b);
                if (na == null || nb == null) continue;
                var l = StandLayout.Of(stand, e, Geo.XZ(na.position), Geo.XZ(nb.position));
                if (l == null) continue;
                stand.position = Geo.X0Z(l.entry);
                stand.Frame(l.along, e.width);
                return true;
            }
            return false;
        }

        /// <summary>How far off parallel (sine of the angle) a taxiway may run to a stand's front line and connect it.</summary>
        public const float ConnectTolerance = 0.02f;

        /// <summary>The taxiway section connecting a stand: one running along its front line (within
        /// ConnectTolerance and StandLayout.OnCentreline) past its position; null if none does.</summary>
        public TaxiwayEdge Connection(Stand stand)
        {
            TaxiwayEdge best = null;
            float bestOff = float.MaxValue;
            var p = Geo.XZ(stand.position);
            var f = stand.Front;
            foreach (var e in edges)
            {
                if (e.IsRoad) continue;
                var na = Node(e.a);
                var nb = Node(e.b);
                if (na == null || nb == null) continue;
                var a = Geo.XZ(na.position);
                var d = Geo.XZ(nb.position) - a;
                float len = d.magnitude;
                if (len < 1f) continue;
                var t = d / len;
                if (Mathf.Abs(Geo.Cross(t, f)) > ConnectTolerance) continue;
                float along = Vector2.Dot(p - a, t), off = Mathf.Abs(Geo.Cross(t, p - a));
                if (along < -0.01f || along > len + 0.01f || off > StandLayout.OnCentreline || off >= bestOff) continue;
                bestOff = off;
                best = e;
            }
            return best;
        }

        public void RemoveStand(Stand stand) => stands.Remove(stand);

        /// <summary>Where a stand lies (its taxiway section, pad, stop bar...), or null if no taxiway carries it.</summary>
        public StandLayout Layout(Stand stand)
        {
            // saved before stands had their own front line: take it from the taxiway it was on (else square on)
            if (!stand.Framed && !FrameFromTaxiway(stand))
                stand.Frame(stand.DeIcing ? stand.Axis : Geo.Right(stand.Axis), 0f);
            // connected or not, it stays where it is (laid out for its own taxilane); a taxiway just links it
            var e = Connection(stand);
            var l = e != null ? StandLayout.Of(stand, e, At(e.a), At(e.b)) : null;
            return l ?? StandLayout.Of(stand);
        }

        /// <summary>The layout of every stand that has a taxiway.</summary>
        public List<StandLayout> Layouts()
        {
            var list = new List<StandLayout>();
            foreach (var st in stands)
            {
                var l = Layout(st);
                if (l != null) list.Add(l);
            }
            return list;
        }

        /// <summary>Does a stand's pad stay clear of every other stand's (sharing an edge is fine), off the runways
        /// (pavement and shoulders), off the roads (touching one is fine: RoadAccess) and, for a parking stand, off
        /// every taxiway but those along its front line (StandAcross)?</summary>
        public bool Fits(StandLayout layout)
        {
            if (layout == null) return false;
            foreach (var st in stands)
            {
                if (st == layout.stand) continue;
                var other = Layout(st);
                if (other != null && layout.Overlaps(other)) return false;
            }
            foreach (var e in edges)
            {
                if (!e.IsRoad && (layout.stand.DeIcing || OnFrontLine(layout, At(e.a), At(e.b)))) continue;
                if (layout.Overlaps(RoadCore(At(e.a), At(e.b), e.width))) return false;
            }
            var airport = Airport;
            if (airport != null)
                foreach (var r in airport.Runways)
                {
                    var a = Geo.XZ(r.start);
                    var b = Geo.XZ(r.end);
                    var right = Geo.Right(r.Axis2D) * r.HalfWidthTotal;
                    if (layout.Overlaps(new[] { a + right, b + right, b - right, a - right })) return false;
                }
            return true;
        }

        /// <summary>A road whose pavement comes within this of a pad's edge touches it (RoadAccess).</summary>
        public const float AccessGap = 1f;
        /// <summary>The shortest stretch of a pad's edge a road must touch to be a way in.</summary>
        public const float MinAccess = 3f;

        /// <summary>A road touching a stand's pad: a way in for the airport's vehicles (RoadAccess).</summary>
        public struct StandAccess
        {
            public TaxiwayEdge road;
            /// <summary>The middle of the stretch of the pad's edge the road touches, and the nearest point on the
            /// road's centreline.</summary>
            public Vector2 onPad, onRoad;
            /// <summary>Which edge of the pad: 0 its far end (a parking stand's head), -1 / 1 its x0 / x1 side.</summary>
            public int side;
            /// <summary>How long that stretch is (metres).</summary>
            public float length;
        }

        /// <summary>
        /// The roads touching a stand's pad: along its far end (the head of a parking stand, where the nose points)
        /// or a side, never the edge on its taxiway (a de-icing bay: its sides only, as both its ends are on the
        /// taxiway). The airport's vehicles come in from them and drive about the pad freely. Longest contact first.
        /// </summary>
        public List<StandAccess> RoadAccess(StandLayout l)
        {
            var list = new List<StandAccess>();
            if (l == null) return list;
            var sides = new List<(Vector2 p0, Vector2 p1, int side)>
            {
                (l.At(l.x0, l.near0), l.At(l.x0, l.far), -1),
                (l.At(l.x1, l.near1), l.At(l.x1, l.far), 1),
            };
            if (!l.stand.DeIcing) sides.Add((l.At(l.x0, l.far), l.At(l.x1, l.far), 0));
            const float step = 0.5f;
            foreach (var e in edges)
            {
                if (!e.IsRoad) continue;
                var ra = At(e.a);
                var rb = At(e.b);
                float reach = e.width * 0.5f + AccessGap;
                foreach (var (p0, p1, side) in sides)
                {
                    float len = Vector2.Distance(p0, p1);
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
                    int run = 0, best = 0, bestEnd = 0;
                    for (int i = 0; i <= n; i++)
                    {
                        var q = Vector2.Lerp(p0, p1, i / (float)n);
                        run = Vector2.Distance(Geo.ClosestOnSegment(q, ra, rb, out _), q) <= reach ? run + 1 : 0;
                        if (run <= best) continue;
                        best = run;
                        bestEnd = i;
                    }
                    float length = (best - 1) * len / n;
                    if (best < 2 || length < MinAccess) continue;
                    var mid = Vector2.Lerp(p0, p1, (bestEnd - (best - 1) * 0.5f) / n);
                    list.Add(new StandAccess { road = e, onPad = mid, onRoad = Geo.ClosestOnSegment(mid, ra, rb, out _),
                        side = side, length = length });
                }
            }
            list.Sort((x, y) => y.length.CompareTo(x.length));
            return list;
        }

        /// <summary>The stand a road or taxiway from a to b (this wide) would run across, more than half a metre into
        /// its pad, or null. Roads may touch stands but never cross them; a taxiway may run along a stand's front line
        /// (that connects it) but not across its pad.</summary>
        public Stand StandAcross(Vector2 a, Vector2 b, float width, bool road = true)
        {
            if ((b - a).sqrMagnitude < 0.01f) return null;
            var core = RoadCore(a, b, width);
            foreach (var l in Layouts())
            {
                if (!road && OnFrontLine(l, a, b)) continue;
                if (l.Overlaps(core)) return l.stand;
            }
            return null;
        }

        /// <summary>Does the line from a to b run along a stand's front line (the taxilane that connects it)?</summary>
        public static bool OnFrontLine(StandLayout l, Vector2 a, Vector2 b)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-4f) return false;
            var u = d.normalized;
            return Mathf.Abs(Geo.Cross(u, l.along)) <= ConnectTolerance && Mathf.Abs(Geo.Cross(u, l.entry - a)) <= StandLayout.OnCentreline;
        }

        /// <summary>A road's pavement less half a metre all round (counter-clockwise), for overlap tests that let
        /// it touch.</summary>
        static Vector2[] RoadCore(Vector2 a, Vector2 b, float width)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) d = Vector2.up;
            var u = d.normalized;
            a += u * 0.5f;
            b -= u * 0.5f;
            var right = Geo.Right(u) * Mathf.Max(width * 0.5f - 0.5f, 0.1f);
            return new[] { a + right, b + right, b - right, a - right };
        }

        /// <summary>The stand whose pad is under a ground point (Airport space, x z).</summary>
        public bool TryPickStand(Vector2 point, out Stand stand)
        {
            foreach (var l in Layouts())
                if (l.Contains(point))
                {
                    stand = l.stand;
                    return true;
                }
            stand = null;
            return false;
        }

        /// <summary>Can this stand take an aircraft type: its size, its surface, the taxiway it opens onto, and
        /// whether it takes small aircraft?</summary>
        public StandProblem CheckStand(Stand stand, AircraftType type)
        {
            var l = Layout(stand);
            if (l == null || l.edge == null) return StandProblem.NotConnected;
            if (stand.noSmallAircraft && !stand.DeIcing && type.Light) return StandProblem.NoSmallAircraft;
            float width = l.edge.width;
            if (LaneLimits().TryGetValue(l.edge, out char max)) width = Mathf.Min(width, RunwayRules.TaxiwayWidthFor(max));
            return RunwayRules.CheckStand(type, stand.Code, stand.surface, width, l.edge.surface);
        }

        /// <summary>
        /// The largest size code that may taxi along each taxiway section with stands laid out for a narrower taxilane
        /// than it is (Stand.Lane): their aircraft keep only that taxilane's clearance from the centreline
        /// (RunwayRules.TaxilaneClearance), so only aircraft needing no more may pass. Sections not listed have no limit.
        /// </summary>
        public Dictionary<TaxiwayEdge, char> LaneLimits()
        {
            var map = new Dictionary<TaxiwayEdge, char>();
            foreach (var l in Layouts())
            {
                if (l.edge == null || l.stand.DeIcing) continue;
                char c = RunwayRules.TaxiwayCode(l.stand.Lane);
                if (c >= RunwayRules.TaxiwayCode(l.edge.width)) continue;
                if (!map.TryGetValue(l.edge, out char was) || c < was) map[l.edge] = c;
            }
            return map;
        }

        /// <summary>The parking stands (or de-icing bays) an aircraft type can use.</summary>
        public List<Stand> StandsFor(AircraftType type, StandKind kind = StandKind.Parking) =>
            stands.FindAll(st => st.kind == kind && CheckStand(st, type) == StandProblem.None);

        /// <summary>Where stands cover the sides of each taxiway section (its s from node a, and the side), so the
        /// section leaves out its verges and edge lights there.</summary>
        Dictionary<TaxiwayEdge, List<(float s0, float s1, int side)>> Frontages(List<StandLayout> layouts)
        {
            var map = new Dictionary<TaxiwayEdge, List<(float, float, int)>>();
            foreach (var e in edges)
            {
                if (e.IsRoad) continue;
                var a = At(e.a);
                var b = At(e.b);
                float reach = e.VergeHalfWidth + SurfaceBuilder.Fringe;
                foreach (var l in layouts)
                {
                    if (!l.Frontage(a, b, reach, out float s0, out float s1, out int side)) continue;
                    if (!map.TryGetValue(e, out var list)) map[e] = list = new List<(float, float, int)>();
                    list.Add((s0, s1, side));
                }
            }
            return map;
        }

        /// <summary>Set the lighting upgrade on every paved edge.</summary>
        public void SetLit(bool lit)
        {
            foreach (var e in edges) e.lit = lit && e.surface.IsPaved() && !e.IsRoad;
            Rebuild();
        }

        /// <summary>Wear from an aircraft taxiing along an edge (half a runway movement's).</summary>
        public void AddMovement(TaxiwayEdge e, AircraftType type)
        {
            var airport = Airport;
            var condition = airport != null ? airport.Condition : SurfaceCondition.Dry;
            e.wear = Mathf.Clamp01(e.wear + 0.5f * RunwayRules.WearPerMovement(e.surface, type.maxTakeoffMass, condition));
            RefreshState();
        }

        // ------------------------------------------------------------------ snow and grass

        /// <summary>Is this edge closed by snow, long grass, or the ploughs / mowers at work?</summary>
        public static bool Closed(TaxiwayEdge e) =>
            (e.work != null && e.work.Closing) || RunwayRules.SnowClosed(e.surface, e.snow) || RunwayRules.GrassClosed(e.surface, e.grass);

        /// <summary>Ploughs and mowers work this far beyond an edge's nodes.</summary>
        public const float WorkMargin = 4f;

        /// <summary>A team takes an edge (VehicleTeam): it shows their progress, and closes once they're on it.
        /// Mowing its verges closes nothing.</summary>
        internal void Assign(TaxiwayEdge e, StripWork w, bool verge = false)
        {
            if (verge) e.vergeWork = w;
            else e.work = w;
            RefreshState();
        }

        /// <summary>The team has finished the edge (or given up, done = false).</summary>
        internal void Release(TaxiwayEdge e, StripWork w, bool done, bool verge = false)
        {
            if (verge)
            {
                if (e.vergeWork != w) return;
                if (done)
                {
                    e.vergeGrass = RunwayRules.CutGrass;
                    e.vergeStripes = w.Stripes;
                }
                e.vergeWork = null;
                RefreshState();
                return;
            }
            if (e.work != w) return;
            if (done && w.kind == WorkKind.Plough) e.snow = 0f;
            if (done && w.kind == WorkKind.Mow)
            {
                e.grass = RunwayRules.CutGrass;
                e.stripes = w.Stripes;
            }
            e.work = null;
            RefreshState();
        }

        /// <summary>Advance time (game seconds): snow builds up or melts at snowRate (cm per second, negative =
        /// melting), grass taxiways grow at grassRate and verges at vergeRate.</summary>
        public void Simulate(float seconds, float snowRate, float grassRate = 0f, float vergeRate = 0f)
        {
            foreach (var e in edges)
            {
                e.snow = Mathf.Max(0f, e.snow + snowRate * seconds);
                if (e.surface == PavementSurface.Grass && e.snow < 0.01f) e.grass += grassRate * seconds;
                if (e.HasVerges) e.vergeGrass += vergeRate * seconds;
            }
            RefreshState();
        }

        /// <summary>What a run of generated vertices belongs to: an edge's straight, or a curve / junction
        /// joining edges.</summary>
        internal struct Owner
        {
            public TaxiwayEdge[] edges;
            public bool joint;
        }

        /// <summary>The state of an edge; a curve or junction is as worn as its most worn edge and as clear (or
        /// short) as its clearest (the teams pass through it). Only straights show the work in progress: it is
        /// laid out along the edge.</summary>
        internal static SurfaceValues StateOf(Owner owner)
        {
            if (!owner.joint && owner.edges.Length == 1)
            {
                var e = owner.edges[0];
                return new SurfaceValues { wear = e.wear, snow = e.snow, grass = e.grass, work = e.work != null ? e.work.ShaderState : e.stripes };
            }
            var v = SurfaceValues.Initial;
            if (owner.edges.Length == 0) return v;
            v.snow = v.grass = float.MaxValue;
            foreach (var e in owner.edges)
            {
                v.wear = Mathf.Max(v.wear, e.wear);
                v.snow = Mathf.Min(v.snow, e.snow);
                v.grass = Mathf.Min(v.grass, e.grass);
            }
            return v;
        }

        /// <summary>Push wear, snow, grass and the work in progress to the meshes (no rebuild).</summary>
        public void RefreshState()
        {
            state.Apply(o => o < owners.Count ? StateOf(owners[o]) : SurfaceValues.Initial);
            vergeState.Apply(o => o < vergeOwners.Count ? VergeStateOf(vergeOwners[o]) : SurfaceValues.Initial);
        }

        static SurfaceValues VergeStateOf(TaxiwayEdge e) => new SurfaceValues
        {
            grass = e.vergeGrass,
            work = e.vergeWork != null ? e.vergeWork.ShaderState : e.vergeStripes,
        };

        // ------------------------------------------------------------------ routing

        /// <summary>
        /// Shortest route between two nodes for an aircraft type: only taxiways it fits (width, surface) and that
        /// aren't closed (repair, snow, grass, ploughs / mowers), never service roads, plus runway segments between
        /// runway nodes (back-taxiing, counted as longer so taxiways are preferred). Returns node ids from start to
        /// goal, or null.
        /// </summary>
        public List<int> FindRoute(int from, int to, AircraftType type) => FindRoute(from, to, TaxiOk(type), RunwayOk(type));

        /// <summary>The same to the nearest of several nodes, leaving out the edges `allow` rejects.</summary>
        public List<int> FindRoute(int from, ICollection<int> targets, AircraftType type, Func<TaxiwayEdge, bool> allow)
        {
            if (Node(from) == null || targets.Count == 0) return null;
            var ok = TaxiOk(type);
            return Search(Links(e => allow(e) && ok(e), RunwayOk(type), _ => 1f, 1.5f, 0f), new[] { (from, 0f) }, targets);
        }

        internal Func<TaxiwayEdge, bool> TaxiOk(AircraftType type)
        {
            var limits = LaneLimits();
            char code = type.SizeCode;
            return e => !e.IsRoad && RunwayRules.CanTaxi(type, e.surface, e.width) && e.wear < RunwayRules.ClosedWear && !Closed(e) &&
                        (!limits.TryGetValue(e, out char max) || code <= max);
        }

        static Func<Runway, bool> RunwayOk(AircraftType type) =>
            r => (r.Paved || type.unpavedRunways) && !r.IsPloughing && !r.IsMowing &&
                 !RunwayRules.SnowClosed(r.surface, r.snow) && !RunwayRules.GrassClosed(r.surface, r.grass);

        /// <summary>How much longer than a road vehicles count a metre of taxiway, runway and apron: they keep to
        /// the service roads where there are any, and off the runways.</summary>
        public const float VehicleTaxiwayCost = 1.25f, VehicleRunwayCost = 1.6f, VehicleApronCost = 1.4f;

        /// <summary>Shortest route for the airport's own vehicles between two nodes (see VehicleRoute).</summary>
        public List<int> FindVehicleRoute(int from, int to) =>
            VehicleRoute(new[] { (from, 0f) }, new HashSet<int> { to }, null);

        /// <summary>
        /// The cheapest route for the airport's vehicles from any of the start nodes (each with a cost already
        /// spent getting there) to any target node, over every road, taxiway and runway whatever its state, and
        /// straight across aprons between nodes on the same apron (where the line stays on it); roads are preferred (VehicleTaxiwayCost ..).
        /// Links for which blocked(from, to) is true (a parked aircraft in the way) are left out. Node ids, or
        /// null.
        /// </summary>
        public List<int> VehicleRoute(IList<(int node, float cost)> starts, ICollection<int> targets,
            Func<Vector2, Vector2, bool> blocked)
        {
            var links = Links(e => true, r => true, e => e.IsRoad ? 1f : VehicleTaxiwayCost, VehicleRunwayCost, VehicleApronCost);
            if (blocked != null)
                foreach (var l in links.Values)
                    l.RemoveAll(x => blocked(At(x.from), At(x.node)));
            return Search(links, starts, targets);
        }

        /// <summary>Shortest route over the edges and runways allowed; runway segments cost runwayCost per metre.</summary>
        public List<int> FindRoute(int from, int to, Func<TaxiwayEdge, bool> edgeOk, Func<Runway, bool> runwayOk, float runwayCost = 1.5f)
        {
            if (Node(from) == null || Node(to) == null) return null;
            return Search(Links(edgeOk, runwayOk, _ => 1f, runwayCost, 0f), new[] { (from, 0f) }, new HashSet<int> { to });
        }

        Dictionary<int, List<(int from, int node, float cost)>> Links(Func<TaxiwayEdge, bool> edgeOk, Func<Runway, bool> runwayOk,
            Func<TaxiwayEdge, float> edgeCost, float runwayCost, float apronCost)
        {
            var links = new Dictionary<int, List<(int from, int node, float cost)>>();
            void Link(int x, int y, float cost)
            {
                if (!links.TryGetValue(x, out var l)) links[x] = l = new List<(int, int, float)>();
                l.Add((x, y, cost));
            }
            foreach (var e in edges)
            {
                if (!edgeOk(e)) continue;
                float len = EdgeLength(e) * edgeCost(e);
                Link(e.a, e.b, len);
                Link(e.b, e.a, len);
            }
            var byRunway = new Dictionary<Runway, List<(float s, int id)>>();
            foreach (var n in nodes)
            {
                if (n.runway == null || !runwayOk(n.runway)) continue;
                n.runway.Locate(n.position, out float s, out _);
                if (!byRunway.TryGetValue(n.runway, out var l)) byRunway[n.runway] = l = new List<(float, int)>();
                l.Add((s, n.id));
            }
            foreach (var l in byRunway.Values)
            {
                l.Sort((x, y) => x.s.CompareTo(y.s));
                for (int i = 0; i < l.Count - 1; i++)
                {
                    float cost = (l[i + 1].s - l[i].s) * runwayCost;
                    Link(l[i].id, l[i + 1].id, cost);
                    Link(l[i + 1].id, l[i].id, cost);
                }
            }
            var airport = Airport;
            if (apronCost > 0f && airport != null)
                foreach (var apron in airport.aprons)
                {
                    var on = nodes.FindAll(n => apron.Contains(Geo.XZ(n.position), 3f));
                    for (int i = 0; i < on.Count; i++)
                        for (int k = i + 1; k < on.Count; k++)
                        {
                            if (!apron.Crossable(Geo.XZ(on[i].position), Geo.XZ(on[k].position), 3f)) continue;
                            float cost = Vector2.Distance(Geo.XZ(on[i].position), Geo.XZ(on[k].position)) * apronCost;
                            Link(on[i].id, on[k].id, cost);
                            Link(on[k].id, on[i].id, cost);
                        }
                }
            return links;
        }

        /// <summary>Dijkstra from several starts (with their costs) to the nearest of several targets.</summary>
        List<int> Search(Dictionary<int, List<(int from, int node, float cost)>> links, IList<(int node, float cost)> starts,
            ICollection<int> targets)
        {
            var dist = new Dictionary<int, float>();
            var prev = new Dictionary<int, int>();
            var open = new List<(float d, int node)>();
            foreach (var (n, c) in starts)
            {
                if (Node(n) == null || (dist.TryGetValue(n, out float old) && old <= c)) continue;
                dist[n] = c;
                open.Add((c, n));
            }
            var closed = new HashSet<int>();
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (open[i].d < open[best].d) best = i;
                int cur = open[best].node;
                open.RemoveAt(best);
                if (!closed.Add(cur)) continue;
                if (targets.Contains(cur))
                {
                    var route = new List<int> { cur };
                    while (prev.TryGetValue(route[route.Count - 1], out int p)) route.Add(p);
                    route.Reverse();
                    return route;
                }
                if (!links.TryGetValue(cur, out var next)) continue;
                foreach (var (_, n, cost) in next)
                {
                    float d = dist[cur] + cost;
                    if (dist.TryGetValue(n, out float old) && old <= d) continue;
                    dist[n] = d;
                    prev[n] = cur;
                    open.Add((d, n));
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ runs

        /// <summary>
        /// A run: the longest chain of edges through `seed`, carrying on round bends (a node with just two edges:
        /// the pavement curves there) and straight on (within 50 degrees) through junctions, into edges of the same
        /// width and kind that `include` accepts, as far as it goes both ways. A team works it end to end in one go
        /// and only turns round at its ends. Edges in order, each with whether the run goes along it from a to b.
        /// </summary>
        public List<(TaxiwayEdge edge, bool forward)> Run(TaxiwayEdge seed, Func<TaxiwayEdge, bool> include)
        {
            var run = new List<(TaxiwayEdge edge, bool forward)> { (seed, true) };
            var used = new HashSet<TaxiwayEdge> { seed };
            // onwards from b, then backwards from a
            for (int dir = 0; dir < 2; dir++)
            {
                var cur = seed;
                int node = dir == 0 ? seed.b : seed.a;
                while (true)
                {
                    var into = At(node) - At(Other(cur, node));
                    if (into.sqrMagnitude < 1e-6f) break;
                    into.Normalize();
                    TaxiwayEdge next = null;
                    var nodeData = Node(node);
                    bool bend = nodeData.runway == null && new List<TaxiwayEdge>(EdgesAt(node)).Count == 2;
                    float bestAngle = bend ? 156f : 50f;
                    foreach (var e in EdgesAt(node))
                    {
                        if (used.Contains(e) || e.road != seed.road || Mathf.Abs(e.width - seed.width) > 0.01f || !include(e)) continue;
                        var d = At(Other(e, node)) - At(node);
                        if (d.sqrMagnitude < 1e-6f) continue;
                        float angle = Vector2.Angle(into, d);
                        if (angle < bestAngle)
                        {
                            bestAngle = angle;
                            next = e;
                        }
                    }
                    if (next == null) break;
                    used.Add(next);
                    bool forward = next.a == node;
                    if (dir == 0) run.Add((next, forward));
                    else run.Insert(0, (next, !forward));
                    node = Other(next, node);
                    cur = next;
                }
            }
            return run;
        }

        // ------------------------------------------------------------------ build

        void OnEnable() => Rebuild();

        void OnDisable()
        {
            surfacePart?.Destroy();
            paintPart?.Destroy();
            glowPart?.Destroy();
            fixturePart?.Destroy();
            vergePart?.Destroy();
        }

        void OnValidate() => dirty = true;

        void Update()
        {
            if (!dirty) return;
            dirty = false;
            Rebuild();
        }

        public void Rebuild()
        {
            var airport = Airport;
            if (airport == null || !isActiveAndEnabled) return;
            foreach (var n in nodes)
                if (n.runway != null) n.position = n.runway.ClosestOnCentreline(n.position);

            surfacePart ??= new AirfieldPart(transform, "Surface");
            paintPart ??= new AirfieldPart(transform, "Paint");
            glowPart ??= new AirfieldPart(transform, "LightGlows");
            fixturePart ??= new AirfieldPart(transform, "Fixtures");
            vergePart ??= new AirfieldPart(transform, "Verges");
            var surface = new SurfaceBuilder(Surfaces.Count);
            var paint = new PaintBuilder();
            var lights = new LightBuilder();
            var verges = new SurfaceBuilder(1);
            var layouts = Layouts();
            var builder = new TaxiwayBuilder(this, airport.Runways, Frontages(layouts));
            builder.Build(surface, paint, lights);
            builder.BuildVerges(verges);
            // stands and bays keep their initial state (no owner): like aprons, they're kept clear
            surface.mesh.owner = paint.mesh.owner = -1;
            surface.state = paint.state = SurfaceValues.Initial;
            var standBuilder = new StandBuilder();
            foreach (var l in layouts) standBuilder.Build(l, surface, paint);
            owners = builder.owners;
            vergeOwners = builder.vergeOwners;
            vergePart.Set(verges.mesh.Apply(vergePart.Target, "Taxiway verges"), airport.grassStrip);
            vergeState.Capture(vergePart.Mesh, verges.mesh, null, null);
            airport.LayoutChanged();
            surfacePart.Set(surface.mesh.Apply(surfacePart.Target, "Taxiway surface"),
                airport.SurfaceMaterial(PavementSurface.Dirt), airport.SurfaceMaterial(PavementSurface.Asphalt),
                airport.SurfaceMaterial(PavementSurface.Concrete), airport.SurfaceMaterial(PavementSurface.Grass));
            paintPart.Set(paint.Apply(paintPart.Target, "Taxiway paint"), airport.paint);
            glowPart.Set(lights.ApplyGlows(glowPart.Target, "Taxiway light glows"), airport.lightGlow);
            fixturePart.Set(lights.ApplyFixtures(fixturePart.Target, "Taxiway fixtures"), airport.fixture);
            state.Capture(surfacePart.Mesh, surface.mesh, paintPart.Mesh, paint.mesh);
            RefreshState();
            LightCount = lights.LightCount;
        }

        public int LightCount { get; private set; }
        /// <summary>The generated meshes (for tests and tools; rebuilt on change).</summary>
        public Mesh SurfaceMesh => surfacePart?.Mesh;
        public Mesh PaintMesh => paintPart?.Mesh;
        public Mesh VergeMesh => vergePart?.Mesh;
    }
}
