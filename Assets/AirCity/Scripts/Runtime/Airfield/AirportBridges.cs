using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    public enum BridgeState
    {
        /// <summary>Retracted, out of the aircraft's way.</summary>
        Parked = 0,
        Docking = 1,
        Docked = 2,
        Retracting = 3,
    }

    /// <summary>
    /// The airport's jet bridges and bridge heads (Stand.head) at work (Airport.Bridges), in game seconds like the
    /// flights (tests run it headless).
    /// A bridge docks once an aircraft has stood still on its stand for a few seconds, if its link reaches a terminal
    /// and it can reach that type's door; it pulls back when the aircraft's departure starts (Flight waits for
    /// Clear before it calls the tug). The Airport's BridgeRig shows them.
    ///
    ///   airport.Bridges.StateOf(bridge)        // Parked / Docking / Docked / Retracting
    ///   airport.Bridges.Clear(aircraft)        // every bridge on its stand parked?
    /// </summary>
    public sealed class AirportBridges
    {
        /// <summary>An aircraft must stand still this long on a stand before the bridges come out (engines spooling down).</summary>
        public const float SettleTime = 5f;
        /// <summary>How close to its parking spot (metres, degrees) an aircraft must stand to count as on the stand.</summary>
        const float ParkReach = 1.5f, ParkTurn = 6f;

        sealed class Bridge
        {
            public BridgeState state;
            public BridgePose from, to, pose;
            public float t;
            public Aircraft.Aircraft docked;
            /// <summary>Not simulated yet: until it is, it is shown docked to an aircraft found parked on its stand (a
            /// scene opened with aircraft on their stands), looked for again each time it is shown.</summary>
            public bool fresh = true;
        }

        sealed class StandWatch
        {
            public Aircraft.Aircraft aircraft;
            public float still;
        }

        readonly Airport airport;
        readonly Dictionary<JetBridge, Bridge> bridges = new Dictionary<JetBridge, Bridge>();
        readonly Dictionary<Stand, StandWatch> watch = new Dictionary<Stand, StandWatch>();
        readonly Dictionary<JetBridge, (int version, BridgePose pose)> parkedPoses = new Dictionary<JetBridge, (int, BridgePose)>();
        int seenVersion = -1;
        List<(StandLayout stand, List<BridgeLayout> bridges)> layouts = new List<(StandLayout, List<BridgeLayout>)>();
        /// <summary>Each stand's head, for the layout version (only stands that have one).</summary>
        readonly Dictionary<Stand, HeadLayout> heads = new Dictionary<Stand, HeadLayout>();
        /// <summary>The roads' and taxiways' pavement, and the taxiways' alone, for the layout version they were made for.</summary>
        int groundVersion = -1;
        List<Vector2[]> pavement = new List<Vector2[]>(), taxiways = new List<Vector2[]>();
        /// <summary>Each bridge's shapes, parked and docked to each type its stand takes, for the layout version.</summary>
        readonly Dictionary<JetBridge, (int version, List<BridgeShape> shapes)> shapes = new Dictionary<JetBridge, (int, List<BridgeShape>)>();

        public AirportBridges(Airport airport) => this.airport = airport;

        /// <summary>Every stand with bridges, laid out (rebuilt after the layout changes).</summary>
        public IReadOnlyList<(StandLayout stand, List<BridgeLayout> bridges)> Layouts
        {
            get
            {
                Refresh();
                return layouts;
            }
        }

        /// <summary>Every stand's bridge head, laid out (rebuilt after the layout changes).</summary>
        public IEnumerable<HeadLayout> Heads
        {
            get
            {
                Refresh();
                return heads.Values;
            }
        }

        /// <summary>A stand's bridge head, laid out, or null if it has none.</summary>
        public HeadLayout HeadOf(Stand stand)
        {
            Refresh();
            return stand != null && heads.TryGetValue(stand, out var h) ? h : null;
        }

        public BridgeState StateOf(JetBridge b) => bridges.TryGetValue(b, out var s) ? s.state : BridgeState.Parked;

        /// <summary>Where a bridge is now (its parked pose if the simulation hasn't seen it yet).</summary>
        public BridgePose PoseOf(BridgeLayout l)
        {
            Refresh();
            var s = Get(l);
            if (s.fresh && s.state == BridgeState.Parked) Seed(l, s);
            return s.pose;
        }

        /// <summary>The aircraft a bridge is docked to (or docking to, or pulling back from), or null.</summary>
        public Aircraft.Aircraft DockedTo(JetBridge b) => bridges.TryGetValue(b, out var s) ? s.docked : null;

        /// <summary>Is every bridge on the aircraft's stand parked (so it can be pushed back)?</summary>
        public bool Clear(Aircraft.Aircraft aircraft)
        {
            foreach (var s in bridges.Values)
                if (s.docked == aircraft && s.state != BridgeState.Parked) return false;
            return true;
        }

        /// <summary>The aircraft types a stand takes (Airport.aircraftTypes): by size code and length, no light aircraft
        /// if it turns them away.</summary>
        public static List<AircraftType> TypesFor(Stand stand, IEnumerable<AircraftType> types)
        {
            var list = new List<AircraftType>();
            if (types == null) return list;
            foreach (var t in types)
                if (t != null && t.SizeCode <= stand.Code && t.Length <= RunwayRules.StandLength(stand.Code) + 0.01f &&
                    !(stand.noSmallAircraft && t.Light))
                    list.Add(t);
            return list;
        }

        /// <summary>The types a stand takes, of the airport's (Airport.aircraftTypes; none listed: the aircraft in the scene).</summary>
        public List<AircraftType> TypesFor(Stand stand)
        {
            if (airport.aircraftTypes != null && airport.aircraftTypes.Count > 0) return TypesFor(stand, airport.aircraftTypes);
            var seen = new HashSet<AircraftType>();
            foreach (var a in Aircraft.Aircraft.All)
                if (a != null && a.type != null) seen.Add(a.type);
            return TypesFor(stand, seen);
        }

        /// <summary>What a stand's bridges are checked against (BridgeSite): its head as it stands now, the other
        /// stands' pads, and their heads and bridges (parked and docked) among them.</summary>
        public BridgeSite Site(Stand stand)
        {
            var site = new BridgeSite { terminals = airport.terminals, types = TypesFor(stand) };
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            if (net == null) return site;
            foreach (var st in net.stands)
            {
                if (st == stand || st == null) continue;
                var l = net.Layout(st);
                if (l != null) site.pads.Add(l.Pad);
            }
            Ground();
            site.pavement = pavement;
            site.taxiways = taxiways;
            site.head = HeadLayout.Of(net.Layout(stand), airport.terminals);
            site.head?.Columns(pavement);
            foreach (var (sl, list) in Layouts)
                if (sl.stand != stand)
                    foreach (var l in list)
                        site.neighbours.AddRange(Shapes(l));
            foreach (var h in heads.Values)
                if (h.stand.stand != stand) site.neighbours.Add(h.Shape);
            return site;
        }

        /// <summary>A bridge's shapes, parked and docked to each type its stand takes that it can dock.</summary>
        List<BridgeShape> Shapes(BridgeLayout l)
        {
            int v = airport.LayoutVersion;
            if (shapes.TryGetValue(l.bridge, out var c) && c.version == v) return c.shapes;
            var list = new List<BridgeShape> { l.Shape(Parked(l)) };
            foreach (var t in TypesFor(l.stand.stand))
            {
                if (t.Light || !BridgeLayout.TryDoor(t, l.bridge, out _)) continue;
                var pose = l.Dock(t, out var problem);
                if (problem == BridgeProblem.None) list.Add(l.Shape(pose));
            }
            shapes[l.bridge] = (v, list);
            return list;
        }

        /// <summary>The roads' and taxiways' pavement as convex quads (made again after the layout changes).</summary>
        void Ground()
        {
            int v = airport.LayoutVersion;
            if (v == groundVersion) return;
            groundVersion = v;
            pavement = new List<Vector2[]>();
            taxiways = new List<Vector2[]>();
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            if (net == null) return;
            foreach (var e in net.edges)
            {
                var a = net.At(e.a);
                var b = net.At(e.b);
                if ((b - a).sqrMagnitude < 0.01f) continue;
                var r = Geo.Right((b - a).normalized) * (e.width * 0.5f);
                var q = new[] { a - r, b - r, b + r, a + r };
                if (Geo.SignedArea(q) < 0f) System.Array.Reverse(q);
                pavement.Add(q);
                if (!e.IsRoad) taxiways.Add(q);
            }
        }

        /// <summary>Every problem a stand's bridge has (BridgeLayout.Check), against its stand's other bridges.</summary>
        public List<(AircraftType type, BridgeProblem problem)> Check(Stand stand, JetBridge bridge)
        {
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            var sl = net != null ? net.Layout(stand) : null;
            if (sl == null) return new List<(AircraftType, BridgeProblem)>();
            var site = Site(stand);
            var others = new List<BridgeLayout>();
            BridgeLayout mine = null;
            foreach (var b in stand.bridges)
            {
                var l = BridgeLayout.Of(b, sl, airport.terminals, site.head);
                others.Add(l);
                if (b == bridge) mine = l;
            }
            return mine == null ? new List<(AircraftType, BridgeProblem)>() : BridgeLayout.Check(mine, others, site);
        }

        /// <summary>What's wrong with a bridge: where it stands (type null), else, if it can dock none of the types the
        /// stand takes, why not for the first that has its door; None if it stands right and serves at least one. (It
        /// needn't serve them all: it stays parked for a type whose door it can't reach.)</summary>
        public BridgeProblem FirstProblem(Stand stand, JetBridge bridge, out AircraftType type)
        {
            type = null;
            var first = BridgeProblem.None;
            AircraftType firstType = null;
            foreach (var (t, p) in Check(stand, bridge))
            {
                if (t == null) return p;
                if (p == BridgeProblem.None) return BridgeProblem.None;
                if (first == BridgeProblem.None && p != BridgeProblem.SmallAircraft && p != BridgeProblem.NoSuchDoor)
                {
                    first = p;
                    firstType = t;
                }
            }
            type = firstType;
            return first;
        }

        /// <summary>
        /// The doors a stand's `count` bridges serve, for the largest type it takes: L1, L2, L3 on a single-deck type; on a
        /// double-deck one L1, then L2 + UL1 (two), then L1 + L2 + UL1 (three), as double-deck gates are built.
        /// </summary>
        public List<JetBridge> PlanDoors(Stand stand, int count)
        {
            AircraftType largest = null;
            foreach (var t in TypesFor(stand))
                if (!t.Light && (largest == null || t.Wingspan > largest.Wingspan)) largest = t;
            var doors = new List<JetBridge>();
            count = Mathf.Clamp(count, 0, JetBridgeRules.MaxPerStand);
            bool upper = largest != null && BridgeLayout.HasUpperDeck(largest);
            if (upper && count == 2)
            {
                doors.Add(new JetBridge { door = 2 });
                doors.Add(new JetBridge { door = 1, upper = true });
            }
            else
                for (int n = 1; n <= count; n++)
                    doors.Add(upper && n == 3 ? new JetBridge { door = 1, upper = true } : new JetBridge { door = n });
            return doors;
        }

        /// <summary>
        /// Give a stand `count` bridges (0 to JetBridgeRules.MaxPerStand), to the doors PlanDoors picks, each placed where it
        /// works for the types it takes: two or three on a shared bridge head where one fits (HeadLayout.Suggest), else
        /// each with its own link (BridgeLayout.Suggest). Returns how many it could place: the ones that found no place
        /// are left off.
        /// </summary>
        public int SetBridges(Stand stand, int count) => SetBridges(stand, PlanDoors(stand, count));

        /// <summary>Give a stand bridges to these doors (each JetBridge's side, deck and number): on a head if two or
        /// more on one side fit on one (and it takes as many as placing them each with its own link would), else placed
        /// in turn where they work, each with its own link; returns how many it could place.</summary>
        public int SetBridges(Stand stand, IList<JetBridge> doors)
        {
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            var sl = net != null ? net.Layout(stand) : null;
            stand.bridges.Clear();
            stand.head = new BridgeHead();
            if (sl != null && doors != null)
            {
                var site = Site(stand);
                var (head, onHead) = HeadLayout.Suggest(sl, doors, site);
                var alone = new List<JetBridge>();
                if (head == null || onHead.Count < doors.Count)
                {
                    var placed = new List<BridgeLayout>();
                    foreach (var door in doors)
                    {
                        var b = BridgeLayout.Suggest(sl, door, placed, site);
                        if (b == null) continue;
                        alone.Add(b);
                        placed.Add(BridgeLayout.Of(b, sl, airport.terminals, null));
                    }
                }
                if (head != null && onHead.Count >= alone.Count)
                {
                    stand.head = head;
                    stand.bridges.AddRange(onHead);
                }
                else stand.bridges.AddRange(alone);
            }
            if (net != null) net.Rebuild();
            return stand.bridges.Count;
        }

        /// <summary>
        /// Point one of a stand's bridges at another door (side, deck, number): it is placed again where it works for
        /// that door, against the stand's other bridges. False, and unchanged, if no place works.
        /// </summary>
        public bool ChangeDoor(Stand stand, JetBridge bridge, DoorSide side, bool upper, int door)
        {
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            var sl = net != null ? net.Layout(stand) : null;
            if (sl == null || !stand.bridges.Contains(bridge)) return false;
            var site = Site(stand);
            var others = new List<BridgeLayout>();
            var wanted = new JetBridge { door = door, side = side, upper = upper, tunnel = bridge.tunnel };
            // on the stand's head if it has one (its upper storey there if this or another bridge on it needs one), else
            // with its own link
            JetBridge placed = null;
            if (site.head != null)
            {
                var h = HeadLayout.Of(stand.head, sl, airport.terminals, upper);
                foreach (var b in stand.bridges)
                    if (b != bridge && b.upper && h.Touches(sl.At(b.root.x, b.root.y))) h.upper = true;
                h.Columns(site.pavement);
                var hs = site.With(h);
                foreach (var b in stand.bridges)
                    if (b != bridge) others.Add(BridgeLayout.Of(b, sl, airport.terminals, h));
                // (a short tunnel first, as on a new head)
                wanted.tunnel = TunnelSize.Short;
                placed = HeadLayout.SuggestBridge(h, wanted, others, hs, out _);
                wanted.tunnel = bridge.tunnel;
                others.Clear();
            }
            if (placed == null)
            {
                foreach (var b in stand.bridges)
                    if (b != bridge) others.Add(BridgeLayout.Of(b, sl, airport.terminals, site.head));
                placed = BridgeLayout.Suggest(sl, wanted, others, site);
            }
            if (placed == null) return false;
            bridge.door = placed.door;
            bridge.side = placed.side;
            bridge.upper = placed.upper;
            bridge.rotunda = placed.rotunda;
            bridge.root = placed.root;
            bridge.tunnel = placed.tunnel;
            // it was placed parking after the others (BridgeLayout.Check), so it goes last
            stand.bridges.Remove(bridge);
            stand.bridges.Add(bridge);
            net.Rebuild();
            return true;
        }

        // ------------------------------------------------------------------ placing by hand (dev tool)

        /// <summary>The layout of a stand (null if it can't be laid out).</summary>
        StandLayout LayoutOf(Stand stand)
        {
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            return net != null && stand != null ? net.Layout(stand) : null;
        }

        void Rebuild() => airport.GetComponentInChildren<TaxiwayNetwork>()?.Rebuild();

        /// <summary>The nearest point of any terminal's wall to a ground point, if it is within `reach` metres (inside a
        /// terminal counts as on its wall), and the wall's outward direction there.</summary>
        public bool OnTerminalWall(Vector2 p, float reach, out Vector2 onWall, out Vector2 outward)
        {
            onWall = p;
            outward = Vector2.up;
            float best = float.MaxValue;
            foreach (var t in airport.terminals)
            {
                if (t == null || t.corners.Count < 3) continue;
                float d = t.ClosestOnWall(p, out var wall, out var o);
                if (t.Contains(p)) d = 0f;
                if (d >= best || d > reach) continue;
                best = d;
                onWall = wall;
                outward = o;
            }
            return best < float.MaxValue;
        }

        /// <summary>The push-back stand a bridge with its rotunda here would serve: the one whose pad it stands on, else
        /// the nearest within 40 m of it. Null if none.</summary>
        public Stand StandFor(Vector2 p)
        {
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            if (net == null) return null;
            Stand best = null;
            float bestD = 40f;
            foreach (var st in net.stands)
            {
                if (st == null || st.DeIcing) continue;
                var l = net.Layout(st);
                if (l == null) continue;
                var pad = l.Pad;
                float d = Geo.Inside(pad, p) ? -1f / (1f + Vector2.Distance(p, l.Centre)) : Geo.DistanceToOutline(pad, p);
                if (d >= bestD) continue;
                bestD = d;
                best = st;
            }
            return best;
        }

        /// <summary>
        /// Move a stand's bridge's rotunda to a ground point. Its link starts from the stand's head if the head's wall is
        /// nearer than any terminal's; else from `keepRoot` (where it left the terminal before, Airport space) if that
        /// still works (on a terminal wall, the link at most JetBridgeRules.MaxLink long and not through a terminal),
        /// else from the nearest terminal wall within reach. Refused, nothing changed, if the rotunda, its link or the
        /// link's columns would stand where the aircraft parks, on a road or taxiway, on another stand or through a
        /// terminal. `problem` is what's wrong where it ends up (or why it was refused), for `type` if it's one type's.
        /// </summary>
        public bool MoveBridge(Stand stand, JetBridge bridge, Vector2 at, Vector2? keepRoot, out BridgeProblem problem, out AircraftType type)
        {
            problem = BridgeProblem.None;
            type = null;
            var sl = LayoutOf(stand);
            if (sl == null || !stand.bridges.Contains(bridge)) return false;
            var was = (bridge.rotunda, bridge.root);
            var root = at;
            float nearest = float.MaxValue;
            foreach (var t in airport.terminals)
            {
                if (t == null || t.corners.Count < 3) continue;
                float d = t.ClosestOnWall(at, out var wall, out _);
                if (d >= nearest || d - JetBridgeRules.RotundaRadius > JetBridgeRules.MaxLink) continue;
                nearest = d;
                root = wall;
            }
            var head = HeadLayout.Of(sl, airport.terminals);
            if (head != null && head.ClosestOnWall(at, out var onHead) < nearest) root = onHead;
            else if (keepRoot is Vector2 k && OnTerminalWall(k, JetBridgeRules.ConnectReach, out _, out _) &&
                     Vector2.Distance(k, at) - JetBridgeRules.RotundaRadius <= JetBridgeRules.MaxLink &&
                     !BridgeLayout.ThroughTerminal(k, at, airport.terminals))
                root = k;
            bridge.rotunda = BridgeLayout.ToStand(sl, at);
            bridge.root = BridgeLayout.ToStand(sl, root);
            problem = FirstProblem(stand, bridge, out type);
            bool ok = problem != BridgeProblem.RotundaInTheWay && problem != BridgeProblem.RotundaOnPavement &&
                      problem != BridgeProblem.ColumnOnPavement && problem != BridgeProblem.LinkInTheWay && problem != BridgeProblem.OnNextStand &&
                      problem != BridgeProblem.TaxiOutStand && problem != BridgeProblem.LinkThroughTerminal;
            if (!ok) (bridge.rotunda, bridge.root) = was;
            Rebuild();
            return ok;
        }

        /// <summary>
        /// Move where a stand's bridge's link leaves the terminal (`bridge` null: the stand's head's main link) to a
        /// point on a terminal's wall (Airport space), the rotunda (or head) staying where it is. Refused, nothing
        /// changed, if the point isn't on a terminal's wall, or the link would be longer than JetBridgeRules.MaxLink, run
        /// through a terminal, over where the aircraft parks or onto another stand, or its columns couldn't keep off the
        /// roads and taxiways. A bridge on the stand's head has no link to the terminal to move.
        /// </summary>
        public bool MoveRoot(Stand stand, JetBridge bridge, Vector2 root, out BridgeProblem problem, out AircraftType type)
        {
            problem = BridgeProblem.None;
            type = null;
            var sl = LayoutOf(stand);
            if (sl == null || !OnTerminalWall(root, JetBridgeRules.ConnectReach, out _, out _)) return false;
            var head = HeadLayout.Of(sl, airport.terminals);
            bool ok;
            if (bridge == null)
            {
                if (head == null) return false;
                var was = stand.head.root;
                stand.head.root = BridgeLayout.ToStand(sl, root);
                var site = Site(stand);
                problem = site.head.Placement(site);
                ok = problem != BridgeProblem.ColumnOnPavement && problem != BridgeProblem.LinkInTheWay && problem != BridgeProblem.OnNextStand &&
                     problem != BridgeProblem.LinkThroughTerminal && problem != BridgeProblem.LinkTooLong;
                if (!ok) stand.head.root = was;
            }
            else
            {
                if (!stand.bridges.Contains(bridge) || BridgeLayout.Of(bridge, sl, airport.terminals, head).head != null) return false;
                var was = bridge.root;
                bridge.root = BridgeLayout.ToStand(sl, root);
                problem = FirstProblem(stand, bridge, out type);
                ok = problem != BridgeProblem.ColumnOnPavement && problem != BridgeProblem.LinkInTheWay && problem != BridgeProblem.OnNextStand &&
                     problem != BridgeProblem.LinkThroughTerminal && problem != BridgeProblem.LinkTooLong;
                if (!ok) bridge.root = was;
            }
            Rebuild();
            return ok;
        }

        /// <summary>The door none of the stand's bridges serves yet whose sill is nearest a ground point along the
        /// aircraft, on the side it's on (a quick guess of what a bridge there would serve, AddBridge), or null.</summary>
        public JetBridge NearestDoor(Stand stand, Vector2 p)
        {
            var sl = LayoutOf(stand);
            if (sl == null) return null;
            var types = TypesFor(stand);
            JetBridge best = null;
            float bestD = float.MaxValue;
            foreach (var door in Doors(stand, types))
            {
                float d = DoorDistance(sl, types, door, p);
                if (d >= bestD) continue;
                bestD = d;
                best = door;
            }
            return best;
        }

        /// <summary>How far a ground point is from a door along the aircraft (the largest type the stand takes with
        /// it), plus a stand's width if it's on the other side; MaxValue if no type has it.</summary>
        static float DoorDistance(StandLayout sl, IList<AircraftType> types, JetBridge door, Vector2 p)
        {
            AircraftType design = null;
            foreach (var t in types)
                if (t != null && !t.Light && BridgeLayout.TryDoor(t, door, out _) && (design == null || t.Wingspan > design.Wingspan)) design = t;
            if (design == null || !BridgeLayout.TryDoor(design, door, out var sp)) return float.MaxValue;
            var sill = BridgeLayout.Sill(sl, design, sp);
            var park = sl.ParkPosition(design);
            var right = Geo.Right(sl.nose);
            float d = Mathf.Abs(Vector2.Dot(p - sill, sl.nose));
            bool sameSide = Mathf.Sign(Vector2.Dot(p - park, right)) == Mathf.Sign(Vector2.Dot(sill - park, right));
            return sameSide ? d : d + 100f;
        }

        /// <summary>Every door (side, deck, number) any of these types has that none of the stand's bridges serves yet.</summary>
        List<JetBridge> Doors(Stand stand, IEnumerable<AircraftType> types)
        {
            var list = new List<JetBridge>();
            foreach (var side in new[] { DoorSide.Left, DoorSide.Right })
                foreach (bool upper in new[] { false, true })
                    for (int n = 1; n <= 6; n++)
                    {
                        bool any = false;
                        foreach (var t in types)
                            any |= t != null && !t.Light && BridgeLayout.TryDoor(t, side, upper, n, out _);
                        bool served = stand.bridges.Exists(b => b != null && b.side == side && b.upper == upper && b.door == n);
                        if (any && !served) list.Add(new JetBridge { side = side, upper = upper, door = n });
                    }
            return list;
        }

        /// <summary>What's wrong with a bridge's rotunda at `rotunda` with its link from `root` (Airport space) on a stand,
        /// whatever door it serves (`door`'s side, deck and number if given, else L1; a short tunnel if it parks
        /// clear with one): BridgeLayout.Placement against the stand's head and bridges. None if nothing.</summary>
        public BridgeProblem WhereProblem(Stand stand, Vector2 rotunda, Vector2 root, JetBridge door)
        {
            var sl = LayoutOf(stand);
            if (sl == null) return BridgeProblem.TaxiOutStand;
            var site = Site(stand);
            var first = BridgeProblem.None;
            foreach (var size in new[] { TunnelSize.Short, TunnelSize.Long })
            {
                var b = new JetBridge
                {
                    door = door?.door ?? 1, side = door?.side ?? DoorSide.Left, upper = door?.upper ?? false, tunnel = size,
                    rotunda = BridgeLayout.ToStand(sl, rotunda), root = BridgeLayout.ToStand(sl, root),
                };
                var l = BridgeLayout.Of(b, sl, airport.terminals, site.head);
                l.Columns(site.pavement);
                var p = l.Placement(site, site.HeadShapes);
                if (p == BridgeProblem.None) return p;
                if (first == BridgeProblem.None) first = p;
            }
            return first;
        }

        /// <summary>
        /// Give a stand another bridge (up to JetBridgeRules.MaxPerStand) with its rotunda at `rotunda` and its link from
        /// `root` (Airport space: a terminal's wall, or the stand's head's wall for a bridge on the head), to the door
        /// nearest it along the aircraft (DoorDistance) that no bridge of the stand serves yet and that it docks at least
        /// one type at, with a short tunnel if that docks as many types as a long one. Placed by hand, it serves the door
        /// it was put beside, even where another door would dock more types. It goes last (parks after the others).
        /// Null, nothing changed, if it can't stand there (`problem`) or docks no type at any door.
        /// </summary>
        public JetBridge AddBridge(Stand stand, Vector2 rotunda, Vector2 root, out BridgeProblem problem, out AircraftType type)
        {
            problem = BridgeProblem.None;
            type = null;
            var sl = LayoutOf(stand);
            if (sl == null || stand.DeIcing) return null;
            if (stand.exit == StandExit.TaxiOut)
            {
                problem = BridgeProblem.TaxiOutStand;
                return null;
            }
            if (stand.bridges.Count >= JetBridgeRules.MaxPerStand) return null;
            var types = TypesFor(stand);
            var site = Site(stand);
            var others = new List<BridgeLayout>();
            foreach (var b in stand.bridges) others.Add(BridgeLayout.Of(b, sl, airport.terminals, site.head));
            JetBridge best = null;
            int bestDocks = 0;
            float bestD = float.MaxValue;
            var firstProblem = BridgeProblem.None;
            AircraftType firstType = null;
            foreach (var door in Doors(stand, types))
                foreach (var size in new[] { TunnelSize.Short, TunnelSize.Long })
                {
                    float near = DoorDistance(sl, types, door, rotunda);
                    var b = new JetBridge
                    {
                        door = door.door, side = door.side, upper = door.upper, tunnel = size,
                        rotunda = BridgeLayout.ToStand(sl, rotunda), root = BridgeLayout.ToStand(sl, root),
                    };
                    var l = BridgeLayout.Of(b, sl, airport.terminals, site.head);
                    int docks = 0;
                    var placement = BridgeProblem.None;
                    foreach (var (t, pr) in BridgeLayout.Check(l, others, site))
                    {
                        if (t == null)
                        {
                            placement = pr;
                            break;
                        }
                        if (pr == BridgeProblem.None) docks++;
                        else if (firstProblem == BridgeProblem.None && pr != BridgeProblem.NoSuchDoor && pr != BridgeProblem.SmallAircraft)
                        {
                            firstProblem = pr;
                            firstType = t;
                        }
                    }
                    if (placement != BridgeProblem.None)
                    {
                        if (problem == BridgeProblem.None) problem = placement;
                        continue;
                    }
                    // the nearest door; as near, the one docking more types (a short tunnel, tried first, if as many)
                    if (docks > 0 && (near < bestD - 0.5f || (near <= bestD + 0.5f && docks > bestDocks)))
                    {
                        best = b;
                        bestDocks = docks;
                        bestD = near;
                    }
                }
            if (best == null)
            {
                // where it stands is wrong for every door, or it stands right but docks nothing
                if (problem == BridgeProblem.None)
                {
                    problem = firstProblem;
                    type = firstType;
                }
                return null;
            }
            problem = BridgeProblem.None;
            stand.bridges.Add(best);
            Rebuild();
            return best;
        }

        /// <summary>Take one bridge off a stand (its head, if it stood on one, stays: RemoveHead takes that).</summary>
        public void RemoveBridge(Stand stand, JetBridge bridge)
        {
            if (stand == null || !stand.bridges.Remove(bridge)) return;
            Rebuild();
        }

        /// <summary>Take a stand's bridge head away, and the bridges standing on it.</summary>
        public void RemoveHead(Stand stand)
        {
            var sl = LayoutOf(stand);
            var head = sl != null ? HeadLayout.Of(sl, airport.terminals) : null;
            if (head == null) return;
            stand.bridges.RemoveAll(b => b != null && head.Touches(sl.At(b.root.x, b.root.y)));
            stand.head = new BridgeHead();
            Rebuild();
        }

        /// <summary>Where each bridge on a stand's head stands along it (metres from its near end and from its far end)
        /// and on which side of it (+1 right): MoveHead takes them along. Empty if the stand has no head.</summary>
        public List<(JetBridge bridge, float fromNear, float fromFar, float side)> HeadStations(Stand stand)
        {
            var list = new List<(JetBridge, float, float, float)>();
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            var sl = net != null ? net.Layout(stand) : null;
            var h = sl != null ? HeadLayout.Of(sl, airport.terminals) : null;
            if (h == null) return list;
            var d = h.Dir;
            foreach (var b in stand.bridges)
            {
                if (b == null || !h.Touches(sl.At(b.root.x, b.root.y))) continue;
                var off = sl.At(b.rotunda.x, b.rotunda.y) - h.a;
                float t = Vector2.Dot(off, d);
                list.Add((b, t, h.Length - t, Vector2.Dot(off, Geo.Right(d)) >= 0f ? 1f : -1f));
            }
            return list;
        }

        /// <summary>
        /// Move a stand's bridge head to run from `near` (where its main link comes in) to `far` (Airport space; kept
        /// JetBridgeRules.MinHead to MaxHead long), its main link following to the nearest terminal wall within
        /// JetBridgeRules.MaxLink (none against it), and its bridges (`stations`, HeadStations before the move) with it,
        /// each on its side of it at its distance from the near end, or from the far end with `keepFar` (the near end
        /// moved). Refused, and nothing changed, if the head, its main link or a bridge's rotunda would stand where an
        /// aircraft the stand takes parks, on a road or taxiway, on another stand's pad or in a terminal. `problem` is
        /// what's wrong where it ends up (or why it was refused), for `type` if it's one type's; None if nothing.
        /// </summary>
        public bool MoveHead(Stand stand, Vector2 near, Vector2 far, IList<(JetBridge bridge, float fromNear, float fromFar, float side)> stations,
            bool keepFar, out BridgeProblem problem, out AircraftType type) =>
            MoveHead(stand, near, far, stations, keepFar, null, out problem, out type);

        /// <summary>MoveHead, its main link kept leaving the terminal at `keepRoot` (Airport space; where it did before)
        /// if that still works: on a terminal's wall, at most JetBridgeRules.MaxLink from the head's near end and not
        /// through a terminal.</summary>
        public bool MoveHead(Stand stand, Vector2 near, Vector2 far, IList<(JetBridge bridge, float fromNear, float fromFar, float side)> stations,
            bool keepFar, Vector2? keepRoot, out BridgeProblem problem, out AircraftType type)
        {
            problem = BridgeProblem.None;
            type = null;
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            var sl = net != null ? net.Layout(stand) : null;
            if (sl == null || stand.head == null || !stand.head.Exists) return false;
            var along = far - near;
            float len = Mathf.Clamp(along.magnitude, JetBridgeRules.MinHead, JetBridgeRules.MaxHead);
            if (along.sqrMagnitude < 1e-4f) along = sl.axis;
            if (keepFar) near = far - along.normalized * len;
            else far = near + along.normalized * len;
            // against a terminal's wall: no main link; else from where it left the terminal before, else from the
            // nearest terminal wall within reach
            var root = near;
            float nearest = float.MaxValue;
            foreach (var t in airport.terminals)
            {
                if (t == null || t.corners.Count < 3) continue;
                float dist = t.ClosestOnWall(near, out var wall, out _);
                if (t.Contains(near)) dist = 0f;
                if (dist >= nearest || dist > JetBridgeRules.MaxLink) continue;
                nearest = dist;
                root = dist <= JetBridgeRules.ConnectReach ? near : wall;
            }
            if (nearest > JetBridgeRules.ConnectReach && keepRoot is Vector2 k && OnTerminalWall(k, JetBridgeRules.ConnectReach, out _, out _) &&
                Vector2.Distance(k, near) <= JetBridgeRules.MaxLink && !BridgeLayout.ThroughTerminal(k, near, airport.terminals))
                root = k;

            return TrySetHead(stand, sl, near, far, root, stations, keepFar, out problem, out type);
        }

        /// <summary>
        /// Give a stand a bridge head by hand: its main link from `root` (on a terminal's wall; the near end against a
        /// wall: no main link) to its near end `near`, the head on to `far` (Airport space; kept JetBridgeRules.MinHead
        /// to MaxHead long). A head the stand has already is replaced, the bridges on it taken along (each at its
        /// distance from the near end). Refused, nothing changed, as MoveHead, and where the main link would be longer
        /// than JetBridgeRules.MaxLink, run through a terminal, or not start on a terminal's wall.
        /// </summary>
        public bool PlaceHead(Stand stand, Vector2 root, Vector2 near, Vector2 far, out BridgeProblem problem, out AircraftType type)
        {
            problem = BridgeProblem.None;
            type = null;
            var sl = LayoutOf(stand);
            if (sl == null || stand.DeIcing) return false;
            if (stand.exit == StandExit.TaxiOut)
            {
                problem = BridgeProblem.TaxiOutStand;
                return false;
            }
            if (!HeadEnds(sl, ref root, near, ref far, out problem)) return false;
            return TrySetHead(stand, sl, near, far, root, HeadStations(stand), false, out problem, out type);
        }

        /// <summary>What's wrong with a head placed so (PlaceHead) on a stand, without placing it: None if nothing.</summary>
        public BridgeProblem HeadProblem(Stand stand, Vector2 root, Vector2 near, Vector2 far)
        {
            var sl = LayoutOf(stand);
            if (sl == null || stand.DeIcing || stand.exit == StandExit.TaxiOut) return BridgeProblem.TaxiOutStand;
            if (!HeadEnds(sl, ref root, near, ref far, out var problem)) return problem;
            var h = HeadLayout.Of(new BridgeHead
            {
                from = BridgeLayout.ToStand(sl, near), to = BridgeLayout.ToStand(sl, far), root = BridgeLayout.ToStand(sl, root),
            }, sl, airport.terminals, false);
            if (HeadLayout.InTerminal(h, airport.terminals)) return BridgeProblem.HeadInTerminal;
            var site = Site(stand);
            return h.Placement(site);
        }

        /// <summary>A hand-placed head's main link and length made right: the near end against a terminal's wall leaves no
        /// main link; else it must start on a wall, at most JetBridgeRules.MaxLink long and not through a terminal. The
        /// far end moved to keep the head MinHead to MaxHead long.</summary>
        bool HeadEnds(StandLayout sl, ref Vector2 root, Vector2 near, ref Vector2 far, out BridgeProblem problem)
        {
            problem = BridgeProblem.None;
            if (OnTerminalWall(near, JetBridgeRules.ConnectReach, out _, out _)) root = near;
            else if (!OnTerminalWall(root, JetBridgeRules.ConnectReach, out _, out _)) problem = BridgeProblem.LinkTooLong;
            else if (Vector2.Distance(root, near) > JetBridgeRules.MaxLink) problem = BridgeProblem.LinkTooLong;
            else if (BridgeLayout.ThroughTerminal(root, near, airport.terminals)) problem = BridgeProblem.LinkThroughTerminal;
            var along = far - near;
            if (along.sqrMagnitude < 1e-4f) along = sl.axis;
            far = near + along.normalized * Mathf.Clamp(along.magnitude, JetBridgeRules.MinHead, JetBridgeRules.MaxHead);
            return problem == BridgeProblem.None;
        }

        /// <summary>Set a stand's head to run from `near` to `far` with its main link from `root` (all Airport space), the
        /// bridges on it (`stations`) taken along; put it all back, and false, if it doesn't stand right (MoveHead).</summary>
        bool TrySetHead(Stand stand, StandLayout sl, Vector2 near, Vector2 far, Vector2 root,
            IList<(JetBridge bridge, float fromNear, float fromFar, float side)> stations, bool keepFar, out BridgeProblem problem, out AircraftType type)
        {
            type = null;
            float len = Vector2.Distance(near, far);
            var was = (stand.head ?? new BridgeHead()).Clone();
            var were = new List<(Vector2 rotunda, Vector2 root)>();
            foreach (var s in stations) were.Add((s.bridge.rotunda, s.bridge.root));
            stand.head = new BridgeHead { from = BridgeLayout.ToStand(sl, near), to = BridgeLayout.ToStand(sl, far), root = BridgeLayout.ToStand(sl, root) };
            var moved = HeadLayout.Of(stand.head, sl, airport.terminals, false);
            float edge = JetBridgeRules.LinkWidth * 0.5f + 0.2f;
            foreach (var (b, fromNear, fromFar, side) in stations)
            {
                float t = Mathf.Clamp(keepFar ? len - fromFar : fromNear, edge, Mathf.Max(len - edge, edge));
                var (rot, stub) = moved.Spot(t, side);
                b.rotunda = BridgeLayout.ToStand(sl, rot);
                b.root = BridgeLayout.ToStand(sl, stub);
            }
            var site = Site(stand);
            problem = site.head != null ? site.head.Placement(site) : BridgeProblem.None;
            if (HeadLayout.InTerminal(moved, airport.terminals)) problem = BridgeProblem.HeadInTerminal;
            bool ok = problem != BridgeProblem.HeadInTheWay && problem != BridgeProblem.LinkInTheWay && problem != BridgeProblem.ColumnOnPavement &&
                      problem != BridgeProblem.OnNextStand && problem != BridgeProblem.OverTaxiway && problem != BridgeProblem.HeadInTerminal &&
                      problem != BridgeProblem.LinkThroughTerminal && problem != BridgeProblem.LinkTooLong;
            foreach (var (b, _, _, _) in stations)
            {
                if (!ok) break;
                var bp = FirstProblem(stand, b, out var bt);
                ok = bp != BridgeProblem.RotundaInTheWay && bp != BridgeProblem.RotundaOnPavement && bp != BridgeProblem.LinkInTheWay &&
                     bp != BridgeProblem.OnNextStand;
                if (problem == BridgeProblem.None || !ok)
                {
                    problem = bp;
                    type = bt;
                }
            }
            if (!ok)
            {
                stand.head = was;
                for (int i = 0; i < stations.Count; i++) (stations[i].bridge.rotunda, stations[i].bridge.root) = were[i];
            }
            Rebuild();
            return ok;
        }

        public void Simulate(float dt)
        {
            Refresh();
            foreach (var (stand, list) in layouts)
            {
                var a = Occupant(stand, dt);
                // departing (a flight has it): pull back
                bool leaving = a != null && airport.Traffic.FlightOf(a) != null;
                foreach (var l in list)
                {
                    var s = Get(l);
                    s.fresh = false;
                    var parked = Parked(l);
                    bool can = a != null && !leaving && l.Connected && watch[stand.stand].still >= SettleTime;
                    var dock = default(BridgePose);
                    if (can)
                    {
                        dock = l.Dock(a.type, out var problem);
                        can = problem == BridgeProblem.None;
                    }
                    switch (s.state)
                    {
                        case BridgeState.Parked:
                            s.pose = parked;
                            s.docked = null;
                            if (can) Move(s, BridgeState.Docking, dock, a);
                            break;
                        case BridgeState.Docked:
                            if (s.docked == null || s.docked != a || leaving) Move(s, BridgeState.Retracting, parked, s.docked);
                            break;
                        case BridgeState.Docking:
                        case BridgeState.Retracting:
                            // an aircraft gone or leaving while docking: turn back from where it is
                            if (s.state == BridgeState.Docking && (s.docked != a || leaving))
                                Move(s, BridgeState.Retracting, parked, s.docked);
                            s.t = Mathf.Min(1f, s.t + dt / JetBridgeRules.DockTime);
                            s.pose = BridgePose.Move(s.from, s.to, s.t);
                            if (s.t >= 1f)
                            {
                                s.state = s.state == BridgeState.Docking ? BridgeState.Docked : BridgeState.Parked;
                                if (s.state == BridgeState.Parked) s.docked = null;
                            }
                            break;
                    }
                }
            }
        }

        static void Move(Bridge s, BridgeState state, BridgePose to, Aircraft.Aircraft a)
        {
            s.state = state;
            s.from = s.pose;
            s.to = to;
            s.t = 0f;
            s.docked = a;
        }

        /// <summary>The aircraft standing still on a stand's parking spot, and how long it has (watch[stand].still).</summary>
        Aircraft.Aircraft Occupant(StandLayout stand, float dt)
        {
            if (!watch.TryGetValue(stand.stand, out var w)) watch[stand.stand] = w = new StandWatch();
            var a = FindParked(stand);
            if (a != w.aircraft)
            {
                w.aircraft = a;
                w.still = 0f;
            }
            else if (a != null) w.still += dt;
            return a;
        }

        Aircraft.Aircraft FindParked(StandLayout stand)
        {
            var t = airport.transform;
            foreach (var a in Aircraft.Aircraft.All)
            {
                if (a == null || a.type == null || a.Moving) continue;
                var p = t.InverseTransformPoint(a.transform.position);
                if (p.y > 1f) continue;
                if (Vector2.Distance(Geo.XZ(p), stand.ParkPosition(a.type)) > ParkReach) continue;
                var f = Geo.XZ(t.InverseTransformDirection(a.transform.forward));
                if (Vector2.Angle(f, stand.nose) > ParkTurn) continue;
                return a;
            }
            return null;
        }

        /// <summary>The bridge's state, made the first time it's seen: already docked to an aircraft standing on its stand
        /// (a scene opened with aircraft parked), else parked.</summary>
        Bridge Get(BridgeLayout l)
        {
            if (bridges.TryGetValue(l.bridge, out var s)) return s;
            s = new Bridge { pose = Parked(l) };
            Seed(l, s);
            bridges[l.bridge] = s;
            return s;
        }

        /// <summary>Docked at once to an aircraft standing on the bridge's stand, if it can reach its door.</summary>
        void Seed(BridgeLayout l, Bridge s)
        {
            var a = FindParked(l.stand);
            if (a == null || !l.Connected || airport.Traffic.FlightOf(a) != null) return;
            var dock = l.Dock(a.type, out var problem);
            if (problem != BridgeProblem.None) return;
            s.state = BridgeState.Docked;
            s.pose = dock;
            s.docked = a;
            if (!watch.ContainsKey(l.stand.stand)) watch[l.stand.stand] = new StandWatch { aircraft = a, still = SettleTime };
        }

        /// <summary>Where a bridge parks: its stand's bridges park in turn, each clear of its head and of those before
        /// it (BridgeLayout.ParkAll).</summary>
        BridgePose Parked(BridgeLayout l)
        {
            int v = airport.LayoutVersion;
            if (parkedPoses.TryGetValue(l.bridge, out var c) && c.version == v) return c.pose;
            var list = new List<BridgeLayout> { l };
            foreach (var (sl, bs) in Layouts)
                if (sl.stand == l.stand.stand)
                {
                    list = bs;
                    break;
                }
            var head = HeadOf(l.stand.stand);
            var poses = BridgeLayout.ParkAll(list, TypesFor(l.stand.stand), head != null ? new[] { head.Shape } : null, out _);
            for (int i = 0; i < list.Count; i++) parkedPoses[list[i].bridge] = (v, poses[i]);
            return parkedPoses.TryGetValue(l.bridge, out c) ? c.pose : poses[0];
        }

        /// <summary>Lay the bridges out again after the layout changed (their links' columns placed off the pavement);
        /// forget bridges that have gone. A docked bridge whose stand or terminal changed under it starts again from
        /// parked.</summary>
        void Refresh()
        {
            int v = airport.LayoutVersion;
            if (v == seenVersion) return;
            seenVersion = v;
            layouts = new List<(StandLayout, List<BridgeLayout>)>();
            heads.Clear();
            var net = airport.GetComponentInChildren<TaxiwayNetwork>();
            var live = new HashSet<JetBridge>();
            Ground();
            if (net != null)
                foreach (var st in net.stands)
                {
                    if (st == null) continue;
                    bool hasHead = st.head != null && st.head.Exists;
                    if ((st.bridges == null || st.bridges.Count == 0) && !hasHead) continue;
                    var sl = net.Layout(st);
                    if (sl == null) continue;
                    // a head stands even with no bridges on it yet (placed by hand, its bridges to come)
                    var head = HeadLayout.Of(sl, airport.terminals);
                    if (head != null)
                    {
                        head.Columns(pavement);
                        heads[st] = head;
                    }
                    if (st.bridges == null || st.bridges.Count == 0) continue;
                    var list = new List<BridgeLayout>();
                    foreach (var b in st.bridges)
                    {
                        if (b == null) continue;
                        var l = BridgeLayout.Of(b, sl, airport.terminals, head);
                        l.Columns(pavement);
                        list.Add(l);
                        live.Add(b);
                    }
                    layouts.Add((sl, list));
                }
            var gone = new List<JetBridge>();
            foreach (var b in bridges.Keys)
                if (!live.Contains(b)) gone.Add(b);
            foreach (var b in gone)
            {
                bridges.Remove(b);
                parkedPoses.Remove(b);
                shapes.Remove(b);
            }
            // poses were worked out for the old layout: settle each bridge where it now belongs
            foreach (var (_, list) in layouts)
                foreach (var l in list)
                {
                    if (!bridges.TryGetValue(l.bridge, out var s)) continue;
                    var problem = BridgeProblem.NoSuchDoor;
                    var dock = s.state == BridgeState.Docked && s.docked != null && l.Connected
                        ? l.Dock(s.docked.type, out problem) : default;
                    if (problem == BridgeProblem.None)
                    {
                        s.pose = dock;
                        continue;
                    }
                    s.state = BridgeState.Parked;
                    s.pose = Parked(l);
                    s.docked = null;
                }
        }
    }
}
