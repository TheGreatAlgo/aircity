using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Saved by number: append new types, never reorder. Dimensions and markings: RoadTypes.</summary>
    public enum PublicRoadStyle { Rural, AirportAccess, Urban, Highway, Ramp, Avenue, Boulevard, OneWay1, OneWay2, OneWay3 }
    public enum RoadStructure { Ground, Elevated, Tunnel }
    public enum RoadBridgeDesign { Automatic, Piers, Arch, Suspension }
    public enum TunnelPortalStyle { Rectangular, Arched }
    public enum JunctionControl { Priority, TrafficLights, Uncontrolled }
    public enum RoadTurn { Straight, Left, Right }
    public enum RoadSignal { Red, Amber, Green }

    [Serializable]
    public sealed class PublicRoadNode
    {
        public int id;
        public Vector3 position;
        public JunctionControl control;
        [Tooltip("Terminal/entrance identifier; roads connect to this node, never directly to the aircraft graph.")]
        public string terminalEntrance;
        [Tooltip("Regional traffic enters here: a map boundary or an island's ferry/harbour connection.")]
        public bool outsideConnection;
        [Tooltip("The control was chosen by hand: roads joining later leave it alone.")]
        public bool controlChosen;
    }

    [Serializable]
    public sealed class PublicRoadSegment
    {
        public int id, a, b;
        public PublicRoadStyle style;
        public RoadStructure structure;
        public RoadBridgeDesign bridgeDesign;
        public TunnelPortalStyle tunnelPortalStyle;
        public AlignmentShape alignment;
        public RoadType Type => RoadTypes.Of(style);
        public int ForwardLanes => Type.forward;
        public int BackwardLanes => Type.backward;
        /// <summary>Kerb to kerb (paved shoulders included).</summary>
        public float Width => Type.Width;
        public float SpeedLimit => Type.speed;
        public PublicRoadSegment Copy() => (PublicRoadSegment)MemberwiseClone();
    }

    public readonly struct RoadLaneConnection
    {
        public readonly int incoming, outgoing, incomingLane, outgoingLane;
        public readonly RoadTurn turn;
        public readonly bool yieldsToOpposingTraffic;
        public RoadLaneConnection(int incoming, int outgoing, int fromLane, int toLane, RoadTurn turn)
        { this.incoming = incoming; this.outgoing = outgoing; incomingLane = fromLane; outgoingLane = toLane; this.turn = turn; yieldsToOpposingTraffic = turn == RoadTurn.Left; }
    }

    /// <summary>Independent, serializable 3D public-road graph. Crossings connect only when their elevations agree.</summary>
    [Serializable]
    public sealed class PublicRoadNetwork
    {
        public const float JunctionHeightTolerance = .35f, CrossingClearance = 6.5f;
        public List<PublicRoadNode> nodes = new List<PublicRoadNode>();
        public List<PublicRoadSegment> segments = new List<PublicRoadSegment>();
        [SerializeField] int nextId = 1;
        public PublicRoadNode Node(int id) => nodes.Find(n => n.id == id);
        public PublicRoadSegment Segment(int id) => segments.Find(e => e.id == id);
        public IEnumerable<PublicRoadSegment> At(int node) => segments.Where(e => e.a == node || e.b == node);
        public static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

        public static PublicRoadNetwork FromWorld(ValleyWorld world)
        {
            var graph = new PublicRoadNetwork();
            int previous = 0;
            bool profile = world.generatorVersion >= 4 && world.roadHeights != null && world.roadHeights.Length == world.publicRoad.Count;
            for (int i = 0; i < world.publicRoad.Count; i++)
            {
                var p = world.publicRoad[i];
                int node = graph.AddNode(new Vector3(p.x, profile ? world.roadHeights[i] : world.Height(p), p.y));
                graph.Node(node).outsideConnection = i == 0 || i == world.publicRoad.Count - 1;
                if (previous != 0) graph.AddSegment(previous, node, PublicRoadStyle.Rural, RoadStructure.Ground);
                previous = node;
            }
            return graph;
        }

        public PublicRoadNetwork Clone()
        {
            var copy = new PublicRoadNetwork { nextId = nextId };
            foreach (var n in nodes) copy.nodes.Add(new PublicRoadNode { id = n.id, position = n.position, control = n.control, terminalEntrance = n.terminalEntrance,
                controlChosen = n.controlChosen, outsideConnection = n.outsideConnection });
            foreach (var e in segments) copy.segments.Add(e.Copy());
            return copy;
        }

        public int AddNode(Vector3 position, string terminalEntrance = null)
        {
            var existing = nodes.Find(n => Vector3.Distance(n.position, position) < .1f);
            if (existing != null) { if (terminalEntrance != null) existing.terminalEntrance = terminalEntrance; return existing.id; }
            var node = new PublicRoadNode { id = nextId++, position = position, terminalEntrance = terminalEntrance };
            nodes.Add(node); return node.id;
        }

        public PublicRoadSegment AddSegment(int a, int b, PublicRoadStyle style, RoadStructure structure,
            RoadBridgeDesign bridgeDesign = RoadBridgeDesign.Automatic, TunnelPortalStyle tunnelPortalStyle = TunnelPortalStyle.Rectangular)
        {
            if (a == b || Node(a) == null || Node(b) == null) throw new ArgumentException("Road endpoints must be two existing nodes.");
            var edge = new PublicRoadSegment { id = nextId++, a = a, b = b, style = style, structure = structure, bridgeDesign = bridgeDesign, tunnelPortalStyle = tunnelPortalStyle };
            segments.Add(edge); return edge;
        }

        [NonSerialized] readonly Dictionary<int,(Vector3 a,Vector3 b,AlignmentShape shape,Alignment3D path)> paths=new Dictionary<int,(Vector3,Vector3,AlignmentShape,Alignment3D)>();
        public Alignment3D Path(PublicRoadSegment edge)
        {
            var a=Node(edge.a).position;var b=Node(edge.b).position;
            if(paths.TryGetValue(edge.id,out var saved) && saved.a==a && saved.b==b && saved.shape.Equals(edge.alignment))return saved.path;
            var path=new Alignment3D(a,b,edge.alignment);paths[edge.id]=(a,b,edge.alignment,path);return path;
        }
        public Vector3 Position(PublicRoadSegment edge, float t) => Path(edge).Position(t);

        public bool TrySnap(Vector3 requested, float radius, out PublicRoadSegment edge, out Vector3 point)
        {
            edge = null; point = requested; float best = radius;
            foreach (var candidate in segments)
            {
                var path=Path(candidate);float t=path.ClosestParameter(XZ(requested));
                var p=path.Position(t); float distance = Vector3.Distance(requested, p);
                if (distance >= best) continue;
                best = distance; edge = candidate; point = p;
            }
            return edge != null;
        }

        public int Split(PublicRoadSegment edge, Vector3 point)
        {
            if (Vector3.Distance(point, Node(edge.a).position) < .1f) return edge.a;
            if (Vector3.Distance(point, Node(edge.b).position) < .1f) return edge.b;
            foreach (int id in new[] { edge.a, edge.b })
            {
                var candidate = Node(id);
                if (!edge.alignment.curved && !edge.alignment.smooth && Vector3.Distance(point, candidate.position) < 16f && At(id).Count() == 2 && candidate.terminalEntrance == null)
                { candidate.position = point; return id; }
            }
            var path=Path(edge);float t=path.ClosestParameter(XZ(point));point=path.Position(t);
            int node = AddNode(point); int b = edge.b; edge.b = node;
            edge.alignment=path.Slice(0,t);AddSegment(node,b,edge.style,edge.structure,edge.bridgeDesign,edge.tunnelPortalStyle).alignment=path.Slice(t,1);return node;
        }

        /// <summary>Connect a segment atomically: ground-level crossings split; overpasses stay separate.</summary>
        public bool Connect(int a,int b,PublicRoadStyle style,RoadStructure structure,out string problem,AlignmentShape alignment=default,Action<IReadOnlyList<int>> created=null)
        {
            // Public callers receive the same atomic graph operation as construction transactions.
            var copy=Clone();
            if(!copy.ConnectDraft(a,b,style,structure,alignment,out problem,out var made))return false;
            nodes=copy.nodes;segments=copy.segments;nextId=copy.nextId;paths.Clear();created?.Invoke(made);return true;
        }
        bool ConnectDraft(int a,int b,PublicRoadStyle style,RoadStructure structure,AlignmentShape alignment,out string problem,out List<int> made)
        {
            made=new List<int>();
            problem="Choose two road nodes at least 1 m apart.";
            if(a==b || Node(a)==null || Node(b)==null || !alignment.IsValid || Vector2.Distance(XZ(Node(a).position),XZ(Node(b).position))<1f)return false;
            Attach(a,style);Attach(b,style);
            var path=new Alignment3D(Node(a).position,Node(b).position,alignment);
            var crossings=new List<(PublicRoadSegment edge,float t,Vector3 position)>();
            foreach(var edge in segments)
            {
                if(edge.a==a || edge.b==a || edge.a==b || edge.b==b)continue;
                var existing=Path(edge);
                foreach(var span in path.Spans())foreach(var other in existing.Spans())
                {
                    if(!Geo.Segments(XZ(span.a),XZ(span.b),XZ(other.a),XZ(other.b),.001f,out var at))continue;
                    float t=path.ClosestParameter(at),u=existing.ClosestParameter(at);
                    if(t<.00001f || t>.99999f || u<.00001f || u>.99999f)continue;
                    if(crossings.Exists(x=>x.edge==edge && Mathf.Abs(x.t-t)*path.Length<.1f))continue;
                    float y=path.Height(t),otherY=existing.Height(u),clearance=Mathf.Abs(y-otherY);
                    if(clearance<=JunctionHeightTolerance)
                    {
                        if(!RoadTypes.Of(style).atGrade || !edge.Type.atGrade)
                        {problem="Highways connect through explicit ramp/merge nodes, rather than an automatic street intersection.";return false;}
                        crossings.Add((edge,t,new Vector3(at.x,(y+otherY)*.5f,at.y)));
                    }
                    else if(clearance<CrossingClearance)
                    {problem=$"An overpass needs {CrossingClearance:0.0} m of separation, including its deck.";return false;}
                }
            }
            var stops=new List<(float t,int node)> {(0,a),(1,b)};
            foreach(var cross in crossings)
            {
                // Earlier crossings may already have split this section; locate the remaining child at this height.
                var edge=segments.Contains(cross.edge)?cross.edge:segments.Find(e=>e.style==cross.edge.style && Path(e).DistanceTo(XZ(cross.position))<.1f && Mathf.Abs(Path(e).Height(Path(e).ClosestParameter(XZ(cross.position)))-cross.position.y)<JunctionHeightTolerance);
                if(edge!=null)stops.Add((cross.t,Split(edge,cross.position)));
            }
            stops.Sort((x,y)=>x.t.CompareTo(y.t));
            for(int i=1;i<stops.Count;i++)if(stops[i-1].node!=stops[i].node)
                {
                    var edge=AddSegment(stops[i-1].node,stops[i].node,style,structure);edge.alignment=path.Slice(stops[i-1].t,stops[i].t);made.Add(edge.id);
                }
            foreach(var stop in stops)Control(stop.node);
            problem=null;return true;
        }

        /// <summary>
        /// Why a new road from a to b would make a poor junction, or null (laid out as Connect would, nothing changed):
        /// it must meet every road at least TaxiwayNetwork.MinJoinAngle off its line, and every section it makes or
        /// shortens must be long enough for the junctions at both its ends (JunctionShape.Needs). Ends are an existing node
        /// (or -1), or a point on `edgeB` (or free). Highways only meet other roads by ramps.
        /// </summary>
        public string JoinProblem(Vector2 a, int nodeA, Vector2 b, int nodeB, PublicRoadSegment edgeB, PublicRoadStyle style)
        {
            var type = RoadTypes.Of(style);
            float min = AirCity.Airfield.TaxiwayNetwork.MinJoinAngle;
            // sections after the change: (from, to, half width, style); removed: split ones
            var sections = new List<(Vector2 p, Vector2 q, float hw, bool fresh, PublicRoadSegment old)>();
            var split = new HashSet<PublicRoadSegment>();
            var cuts = new Dictionary<PublicRoadSegment, List<Vector2>>();
            void Cut(PublicRoadSegment e, Vector2 at)
            {
                if (!cuts.TryGetValue(e, out var list)) cuts[e] = list = new List<Vector2>();
                list.Add(at);
                split.Add(e);
            }
            if (nodeB < 0 && edgeB != null) Cut(edgeB, b);
            var stops = new List<Vector2> { a, b };
            foreach (var e in segments)
            {
                if (e.a == nodeA || e.b == nodeA || e.a == nodeB || e.b == nodeB || e == edgeB) continue;
                var ea = XZ(Node(e.a).position); var eb = XZ(Node(e.b).position);
                if (!Geo.Segments(a, b, ea, eb, .05f, out var at)) continue;
                if (!type.atGrade || !e.Type.atGrade) return "would cross a highway at grade (highways cross on bridges, join by ramps)";
                stops.Add(at);
                Cut(e, at);
            }
            stops.Sort((x, y) => Vector2.Distance(a, x).CompareTo(Vector2.Distance(a, y)));
            for (int i = 0; i + 1 < stops.Count; i++)
                if (Vector2.Distance(stops[i], stops[i + 1]) > .01f) sections.Add((stops[i], stops[i + 1], type.Width * .5f, true, null));
            foreach (var e in segments)
            {
                var ea = XZ(Node(e.a).position); var eb = XZ(Node(e.b).position);
                if (!cuts.TryGetValue(e, out var at)) { sections.Add((ea, eb, e.Width * .5f, false, e)); continue; }
                var points = new List<Vector2>(at) { ea, eb };
                points.Sort((x, y) => Vector2.Distance(ea, x).CompareTo(Vector2.Distance(ea, y)));
                for (int i = 0; i + 1 < points.Count; i++)
                    if (Vector2.Distance(points[i], points[i + 1]) > .01f) sections.Add((points[i], points[i + 1], e.Width * .5f, true, null));
            }
            // judge every point a new or cut section touches
            var points2 = new List<Vector2>();
            foreach (var x in sections) if (x.fresh) { points2.Add(x.p); points2.Add(x.q); }
            var needs = new Dictionary<(Vector2, Vector2, Vector2), float>();
            foreach (var at in points2)
            {
                var arms = new List<(Vector2 dir, float hw, float len, bool fresh, Vector2 p, Vector2 q)>();
                foreach (var x in sections)
                {
                    Vector2 other;
                    if (Vector2.Distance(x.p, at) < .01f) other = x.q; else if (Vector2.Distance(x.q, at) < .01f) other = x.p; else continue;
                    var d = other - at;
                    if (d.sqrMagnitude > 1e-4f) arms.Add((d.normalized, x.hw, d.magnitude, x.fresh, x.p, x.q));
                }
                if (arms.Count < 2) continue;
                arms.Sort((x, y) => Mathf.Atan2(x.dir.y, x.dir.x).CompareTo(Mathf.Atan2(y.dir.y, y.dir.x)));
                for (int i = 0; i < arms.Count; i++)
                {
                    var x = arms[i]; var y = arms[(i + 1) % arms.Count];
                    if (!x.fresh && !y.fresh) continue;
                    float angle = Vector2.Angle(x.dir, y.dir);
                    if (angle < min) return $"would meet a road at {angle:0}° (at least {min:0}°)";
                }
                var need = AirCity.Airfield.Building.JunctionShape.Needs(arms.ConvertAll(x => new AirCity.Airfield.Building.JunctionArm(x.dir, x.hw, x.len, x.len)));
                for (int i = 0; i < arms.Count; i++) needs[(at, arms[i].p, arms[i].q)] = need[i];
            }
            foreach (var x in sections)
            {
                if (!x.fresh) continue;
                needs.TryGetValue((x.p, x.p, x.q), out float na);
                needs.TryGetValue((x.q, x.p, x.q), out float nb);
                if (na + nb > Vector2.Distance(x.p, x.q) + .01f)
                    return "would leave a section too short for the junctions at its ends (put the point on the junction, or further from it)";
            }
            return null;
        }

        /// <summary>A node lying on another road's section (within 10 cm, at the same height) joins it: the section is
        /// split at the node. Not where either road crosses others only by bridge (highways).</summary>
        void Attach(int node, PublicRoadStyle style)
        {
            var n = Node(node);
            if (n == null) return;
            var p = n.position;
            foreach (var edge in segments.ToList())
            {
                if (edge.a == node || edge.b == node) continue;
                var path=Path(edge);float t=path.ClosestParameter(XZ(p));var q=path.Position(t);
                if(t<=1e-4f || t>=1f-1e-4f || Vector2.Distance(XZ(q),XZ(p))>.1f)continue;
                if(Mathf.Abs(q.y-p.y)>JunctionHeightTolerance)continue;
                if(!edge.Type.atGrade || !RoadTypes.Of(style).atGrade)continue;
                int far=edge.b;edge.b=node;edge.alignment=path.Slice(0,t);
                AddSegment(node,far,edge.style,edge.structure,edge.bridgeDesign,edge.tunnelPortalStyle).alignment=path.Slice(t,1);
                Control(node);
                return;
            }
        }

        /// <summary>The control a junction gets unless chosen by hand, as on real roads: traffic lights where a town road
        /// (street, avenue, boulevard, one-way) meets others and traffic arrives from two or more of them; a priority
        /// junction (the road straight through keeps going, the others give way) where only country roads meet, or
        /// where one-way roads just merge or part.</summary>
        public JunctionControl DefaultControl(int node)
        {
            int arms = 0, approaches = 0;
            bool town = false;
            foreach (var e in At(node))
            {
                arms++;
                if ((e.b == node ? e.ForwardLanes : e.BackwardLanes) > 0) approaches++;
                if (e.Type.edge == RoadEdge.Kerb) town = true;
            }
            return arms >= 3 && approaches >= 2 && town ? JunctionControl.TrafficLights : JunctionControl.Priority;
        }

        /// <summary>Give a junction its default control, unless the player chose one.</summary>
        public void Control(int node)
        {
            var n = Node(node);
            if (n != null && !n.controlChosen) n.control = DefaultControl(node);
        }

        /// <summary>Choose a junction's control by hand (it then stays as chosen).</summary>
        public void SetControl(int node, JunctionControl control)
        {
            var n = Node(node);
            if (n == null) return;
            n.control = control;
            n.controlChosen = true;
        }

        /// <summary>Remove a segment, and any node left with nothing; junctions it left get their default control again.</summary>
        public void RemoveSegment(int id)
        {
            var edge = Segment(id);
            if (edge == null) return;
            segments.Remove(edge);
            foreach (int end in new[] { edge.a, edge.b })
            {
                var n = Node(end);
                if (n == null) continue;
                if (!At(end).Any() && n.terminalEntrance == null) nodes.Remove(n);
                else Control(end);
            }
        }

        /// <summary>A long section is bulldozed a piece at a time, however long it was when laid: pieces of equal
        /// length, none longer than this.</summary>
        public const float DeletePieceLength = 40f;

        /// <summary>The piece of a section `length` long that a point `at` metres along it lies in (DeletePieceLength):
        /// where it starts and ends, metres along the section.</summary>
        public static (float from, float to) DeletePiece(float length, float at)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(length / DeletePieceLength - .001f));
            int index = Mathf.Clamp(Mathf.FloorToInt(at / Mathf.Max(length, .001f) * count), 0, count - 1);
            return (length * index / count, length * (index + 1f) / count);
        }

        /// <summary>
        /// Remove the part of a section between two distances along it (from its node a). What is left either side
        /// stays as it was, each now ending in a dead end where the part was; a part reaching a node takes the
        /// section's end there with it, and the whole section is simply removed.
        /// </summary>
        public void RemovePart(int id, float from, float to)
        {
            var edge = Segment(id);
            if (edge == null) return;
            var path = Path(edge);
            float length = path.Length;
            bool head = from > .5f, tail = to < length - .5f;
            if (!head && !tail || to - from < .01f) { if (to - from >= .01f) RemoveSegment(id); return; }
            float t0 = path.ParameterAtDistance(from), t1 = path.ParameterAtDistance(to);
            Vector3 p0 = path.Position(t0), p1 = path.Position(t1);
            AlignmentShape before = path.Slice(0f, t0), after = path.Slice(t1, 1f);
            int a = edge.a, b = edge.b;
            if (tail) AddSegment(AddNode(p1), b, edge.style, edge.structure, edge.bridgeDesign, edge.tunnelPortalStyle).alignment = after;
            if (head) { edge.b = AddNode(p0); edge.alignment = before; }
            else segments.Remove(edge);
            // an end that lost its road: gone if nothing else uses it, otherwise its junction is controlled afresh
            foreach (int end in new[] { head ? -1 : a, tail ? -1 : b })
            {
                var n = Node(end);
                if (n == null) continue;
                if (!At(end).Any() && n.terminalEntrance == null) nodes.Remove(n);
                else Control(end);
            }
        }

        public bool AddAirportAccess(ValleyWorld world, AirportSite site, IList<Vector2> route, out string problem, bool validateLayout = true)
        {
            problem = null;
            if (validateLayout && !PublicRoadLayout.Validate(world, site, route, out problem)) return false;
            var end = route[route.Count - 1];
            // This route is authored in x/z. A road's engineered profile can differ from the natural
            // terrain between its nodes, so join the ground road at its own interpolated elevation.
            PublicRoadSegment trunk = null; Vector3 snapped = default; float best = 5f;
            foreach (var edge in segments)
            {
                if (edge.structure != RoadStructure.Ground) continue;
                var a = Node(edge.a).position; var b = Node(edge.b).position;
                var point = Geo.ClosestOnSegment(end, XZ(a), XZ(b), out float t);
                float distance = Vector2.Distance(end, point);
                if (distance >= best) continue;
                best = distance; trunk = edge; snapped = Vector3.Lerp(a, b, Mathf.Clamp01(t));
            }
            if (trunk == null) { problem = "Finish on an existing ground-level public road."; return false; }
            int junction = Split(trunk, snapped); SetControl(junction, JunctionControl.TrafficLights);   // the airport's own entrance
            int previous = 0;
            for (int i = 0; i < route.Count; i++)
            {
                var p = route[i];
                int node = i == route.Count - 1 ? junction : AddNode(new Vector3(p.x, i == 0 ? site.elevation : world.Height(p), p.y), i == 0 ? "airport-reception" : null);
                if (previous != 0 && !Connect(previous, node, PublicRoadStyle.AirportAccess, RoadStructure.Ground, out problem)) return false;
                previous = node;
            }
            problem = null; return true;
        }

        public bool HasRoute(int from, int to)
        {
            if (Node(from) == null || Node(to) == null) return false;
            var seen = new HashSet<int> { from }; var queue = new Queue<int>(); queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(); if (current == to) return true;
                foreach (var edge in At(current))
                {
                    int next = edge.a == current ? edge.b : edge.a;
                    if ((edge.a == current || edge.BackwardLanes > 0) && seen.Add(next)) queue.Enqueue(next);
                }
            }
            return false;
        }

        /// <summary>
        /// Which lane may go where at a junction, as on a real road: lanes are numbered from the kerb (0) towards the
        /// centre. The kerb lane turns right (and goes straight), the centre lane turns left (and goes straight), lanes
        /// between go straight; with no road straight on, the kerb half turns right and the centre half left; a single
        /// lane makes every move. Each move continues in the matching lane of the road it joins (kerb to kerb, centre to
        /// centre, straight on lane to lane). U-turns are left out.
        /// </summary>
        public List<RoadLaneConnection> LaneConnections(int junction)
        {
            var result = new List<RoadLaneConnection>(); var centre = Node(junction).position;
            foreach (var incoming in At(junction))
            {
                int inLanes = incoming.b == junction ? incoming.ForwardLanes : incoming.BackwardLanes;
                if (inLanes == 0) continue;
                var from=Alignment3D.XZ(Path(incoming).Tangent(incoming.a==junction?0:1)).normalized*(incoming.a==junction?1:-1);
                var exits = new List<(PublicRoadSegment edge, RoadTurn turn, int lanes)>();
                foreach (var outgoing in At(junction))
                {
                    if (outgoing == incoming) continue;
                    int outLanes = outgoing.a == junction ? outgoing.ForwardLanes : outgoing.BackwardLanes;
                    if (outLanes == 0) continue;
                    var to=Alignment3D.XZ(Path(outgoing).Tangent(outgoing.a==junction?0:1)).normalized*(outgoing.a==junction?1:-1);
                    float turnAngle = Vector2.SignedAngle(-from, to);
                    exits.Add((outgoing, Mathf.Abs(turnAngle) < 30f ? RoadTurn.Straight : turnAngle > 0f ? RoadTurn.Left : RoadTurn.Right, outLanes));
                }
                bool straight = exits.Exists(x => x.turn == RoadTurn.Straight);
                for (int lane = 0; lane < inLanes; lane++)
                    foreach (var (outgoing, turn, outLanes) in exits)
                        if (LaneMay(lane, inLanes, turn, straight))
                        {
                            int to = turn == RoadTurn.Right ? 0 : turn == RoadTurn.Left ? outLanes - 1 : Mathf.Min(lane, outLanes - 1);
                            result.Add(new RoadLaneConnection(incoming.id, outgoing.id, lane, to, turn));
                        }
            }
            return result;
        }

        /// <summary>May lane `lane` (0 = kerb lane) of `lanes` make this turn? See LaneConnections.</summary>
        public static bool LaneMay(int lane, int lanes, RoadTurn turn, bool straightExists)
        {
            if (lanes <= 1) return true;
            bool kerb = lane == 0, centre = lane == lanes - 1;
            switch (turn)
            {
                case RoadTurn.Straight: return true;
                case RoadTurn.Right: return straightExists ? kerb : lane < (lanes + 1) / 2;
                default: return straightExists ? centre : lane >= lanes / 2;
            }
        }

        public bool Validate(out string problem)
        {
            problem = "Invalid public-road network.";
            if (nodes == null || segments == null || nodes.Count > 4096 || segments.Count > 8192 || nextId < 1) return false;
            var ids = new HashSet<int>();
            foreach (var n in nodes)
                if (n == null || n.id <= 0 || !ids.Add(n.id) || !ValleyWorld.Finite(n.position.x) || !ValleyWorld.Finite(n.position.y) ||
                    !ValleyWorld.Finite(n.position.z) || Mathf.Abs(n.position.x) > 10000f || Mathf.Abs(n.position.z) > 10000f || n.position.y < -100f || n.position.y > 2000f ||
                    !Enum.IsDefined(typeof(JunctionControl), n.control)) return false;
            var edgeIds = new HashSet<int>();
            foreach (var e in segments)
                if (e == null || e.id <= 0 || !edgeIds.Add(e.id) || e.a == e.b || !ids.Contains(e.a) || !ids.Contains(e.b) ||
                    !Enum.IsDefined(typeof(PublicRoadStyle), e.style) || !Enum.IsDefined(typeof(RoadStructure), e.structure) ||
                    !Enum.IsDefined(typeof(RoadBridgeDesign),e.bridgeDesign) || !Enum.IsDefined(typeof(TunnelPortalStyle),e.tunnelPortalStyle) || !e.alignment.IsValid) return false;
            if (nodes.Any(n => n.id >= nextId) || segments.Any(e => e.id >= nextId)) return false;
            for (int i = 0; i < segments.Count; i++)
            for (int j = i + 1; j < segments.Count; j++)
            {
                var a = segments[i]; var b = segments[j];
                if (a.a == b.a || a.a == b.b || a.b == b.a || a.b == b.b) continue;
                var pa=Path(a);var pb=Path(b);
                foreach(var span in pa.Spans())foreach(var other in pb.Spans())
                {
                    if(!Geo.Segments(XZ(span.a),XZ(span.b),XZ(other.a),XZ(other.b),.001f,out var at))continue;
                    float gap=Mathf.Abs(pa.Height(pa.ClosestParameter(at))-pb.Height(pb.ClosestParameter(at)));
                    if(gap<CrossingClearance)
                    {problem=gap<=JunctionHeightTolerance?"At-grade crossings must share a public junction node.":"The elevated crossing has insufficient clearance.";return false;}
                }
            }
            problem = null; return true;
        }
    }

    /// <summary>Conflict groups for a conventional two-phase junction, with amber and all-red clearance intervals.</summary>
    public static class PublicRoadSignals
    {
        public const float CycleSeconds = 44f;
        public static int Group(PublicRoadNetwork graph, int node, int approach)
        {
            var edges = graph.At(node).ToList();
            if (edges.Count == 0) return 0;
            PublicRoadSegment main = edges[0];
            // The most nearly opposing pair forms the through-road phase; other approaches share the second phase.
            float best = 1f;
            for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
            {
                var u = Direction(graph, node, edges[i]); var v = Direction(graph, node, edges[j]);
                float dot = Vector2.Dot(u, v); if (dot < best) { best = dot; main = edges[i]; }
            }
            return Mathf.Abs(Vector2.Dot(Direction(graph, node, main), Direction(graph, node, graph.Segment(approach)))) > .75f ? 0 : 1;
        }
        public static Vector2 Direction(PublicRoadNetwork graph, int node, PublicRoadSegment edge) =>
            Alignment3D.XZ(graph.Path(edge).Tangent(edge.a==node?0:1)).normalized*(edge.a==node?1:-1);
        public static RoadSignal Signal(int group, float seconds)
        {
            float t = Mathf.Repeat(seconds, CycleSeconds);
            if (group == 0) return t < 24f ? RoadSignal.Green : t < 27f ? RoadSignal.Amber : RoadSignal.Red;
            return t >= 28f && t < 40f ? RoadSignal.Green : t >= 40f && t < 43f ? RoadSignal.Amber : RoadSignal.Red;
        }
    }
}
