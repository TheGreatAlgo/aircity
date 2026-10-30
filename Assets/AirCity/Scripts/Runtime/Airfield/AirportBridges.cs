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
            var wanted = new JetBridge { door = door, side = side, upper = upper };
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
                placed = HeadLayout.SuggestBridge(h, wanted, others, hs, out _);
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
            // it was placed parking after the others (BridgeLayout.Check), so it goes last
            stand.bridges.Remove(bridge);
            stand.bridges.Add(bridge);
            net.Rebuild();
            return true;
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
                            float k = s.t * s.t * (3f - 2f * s.t);
                            s.pose = BridgePose.Lerp(s.from, s.to, k);
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
                    if (st == null || st.bridges == null || st.bridges.Count == 0) continue;
                    var sl = net.Layout(st);
                    if (sl == null) continue;
                    var head = HeadLayout.Of(sl, airport.terminals);
                    if (head != null)
                    {
                        head.Columns(pavement);
                        heads[st] = head;
                    }
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
