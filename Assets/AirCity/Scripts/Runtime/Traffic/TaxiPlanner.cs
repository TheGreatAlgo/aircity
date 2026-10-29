using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Traffic
{
    /// <summary>
    /// Plans an aircraft's way on the ground over the taxiway graph (TaxiwayNetwork): a shortest-route search that
    /// knows which way the aircraft is going at each junction, so it never turns back on itself or takes a turn
    /// sharper than MaxTurn, and prefers fewer and gentler turns. It uses only the taxiways the aircraft fits
    /// (width, surface, the stands' taxilanes) that aren't closed, never roads, and crosses runways at their
    /// crossings. Sections other traffic has reserved the other way cost more, so it goes round them where it can.
    ///
    /// Departures end lined up on a runway the wind says is in use: at a full-length entry where there is one, or
    /// where the runway left is long enough, or back-tracking along the runway and turning round at its end.
    /// Arrivals start at touchdown, take the first exit they can slow down for (or turn round and come back to one),
    /// and end on the nearest free stand that takes them.
    /// </summary>
    sealed class TaxiPlanner
    {
        /// <summary>The sharpest turn at a junction (degrees).</summary>
        public const float MaxTurn = 135f;
        /// <summary>Lining up onto a runway or turning off it can be sharper.</summary>
        const float MaxRunwayTurn = 150f;
        /// <summary>A 90° turn costs as much as this many metres of taxiing (rising with the square of the angle).</summary>
        const float TurnCost = 30f;
        const float CrossingCost = 60f, UTurnCost = 150f, BackTrackFactor = 3f, ConflictFactor = 4f;
        /// <summary>Anything bigger than a light aircraft turns round on a runway only when there's no other way: it
        /// would rather taxi this much further (metres).</summary>
        const float HeavyUTurnCost = 20000f;
        /// <summary>Each metre an arrival rolls along the runway costs this much: it takes an early exit and taxis on
        /// rather than holding the runway rolling to the exit nearest its stand.</summary>
        const float RolloutCost = 1.5f;
        /// <summary>Each metre of runway left behind at an intersection departure costs this much.</summary>
        const float LostRunwayCost = 0.3f;
        /// <summary>Each size code a stand is bigger than the aircraft costs this many metres.</summary>
        const float BigStandCost = 150f;
        public const float Lateral = 0.8f;
        const float RapidExitAngle = 50f, RapidExitRadius = 550f, RapidExitSpeed = 20f;

        readonly Airport airport;
        readonly TaxiwayNetwork net;
        readonly AircraftType type;
        readonly AirTraffic traffic;
        readonly Flight self;
        readonly Func<TaxiwayEdge, bool> ok;
        readonly Dictionary<int, TaxiwayNode> nodes = new Dictionary<int, TaxiwayNode>();
        readonly List<Link> links = new List<Link>();
        readonly Dictionary<int, List<int>> outOf = new Dictionary<int, List<int>>();
        readonly float wheelbase, noseOverhang, tail, minRadius;

        sealed class Link
        {
            public int from, to;
            public TaxiwayEdge edge;
            public Vector2 dir;
            /// <summary>+1 along the edge from node a, -1 the other way.</summary>
            public int sign;
            public float length, cost;
            /// <summary>Along the edge (from node a): where it starts and ends.</summary>
            public float uFrom, uTo;
            /// <summary>A start (from -1): the start it comes from.</summary>
            public Start start;
        }

        /// <summary>Where the way starts: on a taxiway section facing along it, or (arrivals) at a runway node rolling
        /// along the runway. `lay` lays the way from the aircraft's nose wheel up to there.</summary>
        public sealed class Start
        {
            public TaxiwayEdge edge;
            public int dir;
            public float u;
            /// <summary>Arrivals: the runway node it reaches rolling along `along`.</summary>
            public int node = -1;
            public Vector2 along;
            public float cost;
            public Vector2 nose, heading, gear;
            /// <summary>Lays the way to the start; returns how far past it (along the section) the way already is.</summary>
            public Func<TaxiPath.Builder, float> lay;
            /// <summary>Arrivals: the way on the runway is rolling at landing speed until this node.</summary>
            public bool landing;
            public float landingDecel, touchdownS;
            /// <summary>Departures: the push-back that gets it here, the other way round or not.</summary>
            public bool pushOtherWay;
            public bool pushBack, handPush;
            /// <summary>Departures: the push-back that gets it here.</summary>
            public PushBackPath push;
            /// <summary>Departures from a taxi-out stand: the nose wheel's distance along the way when its tail is out
            /// of the stand.</summary>
            public float clearOfStand = -1f;
        }

        /// <summary>A runway departures may use, the way it's used, and the length a takeoff needs on it now.</summary>
        public struct RunwayChoice
        {
            public Runway runway;
            public bool fromStart;
            public float need;
        }

        sealed class Goal
        {
            public float total;
            public int state = -1;
            // departures
            public RunwayChoice choice;
            public bool backTrack;
            public float turnRadius;
            // arrivals
            public StandLayout stand;
            public int lastLink = -1;
            public float uGoal;
        }

        public TaxiPlanner(Airport airport, AircraftType type, AirTraffic traffic, Flight self)
        {
            this.airport = airport;
            this.type = type;
            this.traffic = traffic;
            this.self = self;
            net = airport.Taxiways;
            ok = net.TaxiOk(type);
            wheelbase = Mathf.Max(type.Wheelbase, 0.3f);
            noseOverhang = Mathf.Max(type.bounds.max.z - wheelbase, 0f);
            tail = Mathf.Max(-type.bounds.min.z, 0f);
            minRadius = wheelbase / Mathf.Sin(Mathf.Clamp(type.maxSteeringAngle, 10f, 85f) * Mathf.Deg2Rad);
            foreach (var n in net.nodes) nodes[n.id] = n;
            foreach (var e in net.edges)
            {
                if (e.IsRoad || !ok(e) || !nodes.ContainsKey(e.a) || !nodes.ContainsKey(e.b)) continue;
                AddLink(e, +1);
                AddLink(e, -1);
            }
        }

        Vector2 At(int node) => Geo.XZ(nodes[node].position);

        /// <summary>Lined up: this far straight along the runway after turning onto it, so the main gear has come
        /// round behind the nose wheel.</summary>
        float Settle => 2.5f * wheelbase + 5f;

        void AddLink(TaxiwayEdge e, int dir)
        {
            var a = At(e.a);
            var b = At(e.b);
            float len = Vector2.Distance(a, b);
            if (len < 0.5f) return;
            var l = new Link
            {
                from = dir > 0 ? e.a : e.b, to = dir > 0 ? e.b : e.a, edge = e, length = len, sign = dir,
                dir = (dir > 0 ? b - a : a - b) / len, uFrom = dir > 0 ? 0f : len, uTo = dir > 0 ? len : 0f,
            };
            l.cost = len * ConflictCost(e, dir, 0f, len);
            links.Add(l);
            if (!outOf.TryGetValue(l.from, out var list)) outOf[l.from] = list = new List<int>();
            list.Add(links.Count - 1);
        }

        /// <summary>Taxiing against traffic that has reserved the section the other way costs more (it would wait).</summary>
        float ConflictCost(TaxiwayEdge e, int dir, float u0, float u1) =>
            traffic != null && traffic.Conflict(self, type, e, dir, u0, u1) != null ? ConflictFactor : 1f;

        static float Turn(float degrees) => TurnCost * (degrees / 90f) * (degrees / 90f);

        static int Dir(Link l) => l.sign;

        /// <summary>A taxiway junction, which aircraft take one at a time: three or more taxiway arms, off any runway.</summary>
        bool Junction(int node, out float radius)
        {
            radius = 0f;
            if (!nodes.TryGetValue(node, out var n) || n.runway != null) return false;
            int arms = 0;
            float hw = 0f, widest = 0f;
            foreach (var e in net.EdgesAt(node))
            {
                if (e.IsRoad) continue;
                arms++;
                hw = Mathf.Max(hw, e.width * 0.5f);
                widest = Mathf.Max(widest, e.width);
            }
            if (arms < 3) return false;
            radius = hw + RunwayRules.MaxWingspan(RunwayRules.TaxiwayCode(widest)) * 0.5f + 3f;
            return true;
        }

        /// <summary>From a runway node along the section to its runway-holding position (metres).</summary>
        static float HoldingDistance(Vector2 dir, Runway r, float length)
        {
            float sin = Mathf.Max(Mathf.Abs(Geo.Cross(dir, r.Axis2D)), 0.15f);
            return Mathf.Min(RunwayRules.HoldingDistance(r.width) / sin, length - 1f);
        }

        /// <summary>The radius to round a corner off with: the taxiway's turn radius (as its painted curves), and wide
        /// rapid-exit curves off a runway at speed.</summary>
        float CornerRadius(float width, float turnDegrees, bool rapid)
        {
            float r = RunwayRules.TaxiwayTurnRadius(width);
            if (rapid && turnDegrees <= RapidExitAngle) r = RapidExitRadius;
            return Mathf.Max(r, minRadius * 1.05f);
        }

        float RunwayUTurnCost => type.Light ? UTurnCost : HeavyUTurnCost;

        /// <summary>The U-turn radius on a runway (0 if the aircraft can't turn round on it).</summary>
        float UTurnRadius(Runway r)
        {
            float hw = r.width * 0.5f;
            float rad = Mathf.Clamp(0.6f * hw, 1.3f * minRadius, 0.95f * hw);
            return 1.3f * minRadius > 0.95f * hw ? 0f : rad;
        }

        // ------------------------------------------------------------------ search

        /// <summary>Dijkstra over (node, the link it came in by), from the starts to the cheapest goal.</summary>
        Goal Search(List<Start> starts, List<RunwayChoice> runways, List<StandLayout> stands)
        {
            var dist = new Dictionary<int, float>();
            var prev = new Dictionary<int, int>();
            var open = new List<(float d, int state)>();
            Goal best = null;

            void Offer(Goal g)
            {
                if (best == null || g.total < best.total) best = g;
            }

            // stand goals by section
            var standsOn = new Dictionary<TaxiwayEdge, List<(StandLayout l, int dir, float u, float cost)>>();
            if (stands != null)
                foreach (var l in stands)
                {
                    if (l.edge == null || !ok(l.edge)) continue;
                    var a = At(l.edge.a);
                    var t = (At(l.edge.b) - a).normalized;
                    float codeCost = Mathf.Max(0, l.stand.Code - type.SizeCode) * BigStandCost;
                    if (!standsOn.TryGetValue(l.edge, out var list)) standsOn[l.edge] = list = new List<(StandLayout, int, float, float)>();
                    if (l.TaxiOut)
                    {
                        // only along the stand's acute side, into its hairpin
                        var lead = l.LeadIn(1f);
                        int dir = Vector2.Dot(l.inAlong, t) > 0f ? +1 : -1;
                        list.Add((l, dir, Vector2.Dot(lead[0].point - a, t), codeCost + 40f));
                    }
                    else
                    {
                        float u = Vector2.Dot(l.entry - a, t);
                        foreach (int dir in new[] { +1, -1 })
                        {
                            float turn = Vector2.Angle(t * dir, l.axis);
                            if (turn > MaxRunwayTurn) continue;
                            list.Add((l, dir, u, codeCost + Turn(turn)));
                        }
                    }
                }

            void StandGoals(int state, Link via, float cost)
            {
                if (via.edge == null || !standsOn.TryGetValue(via.edge, out var list)) return;
                int dir = Dir(via);
                foreach (var (l, gd, u, c) in list)
                {
                    if (gd != dir) continue;
                    float ahead = (u - via.uFrom) * dir;
                    if (ahead < 1f || ahead > via.length + 0.01f) continue;
                    Offer(new Goal { total = cost + ahead + c, state = state, stand = l, uGoal = u });
                }
            }

            // the starts
            foreach (var s in starts)
            {
                if (s.node >= 0)
                {
                    links.Add(new Link { from = -1, to = s.node, dir = s.along, start = s });
                }
                else
                {
                    if (s.edge == null || !nodes.ContainsKey(s.edge.a) || !nodes.ContainsKey(s.edge.b)) continue;
                    float len = Vector2.Distance(At(s.edge.a), At(s.edge.b));
                    var t = (At(s.edge.b) - At(s.edge.a)) / Mathf.Max(len, 1e-3f);
                    float u = Mathf.Clamp(s.u, 0f, len);
                    links.Add(new Link
                    {
                        from = -1, to = s.dir > 0 ? s.edge.b : s.edge.a, edge = s.edge, dir = t * s.dir, sign = s.dir, uFrom = u,
                        uTo = s.dir > 0 ? len : 0f, length = s.dir > 0 ? len - u : u, start = s,
                    });
                    var lk = links[links.Count - 1];
                    lk.cost = lk.length * ConflictCost(s.edge, s.dir, Mathf.Min(lk.uFrom, lk.uTo), Mathf.Max(lk.uFrom, lk.uTo));
                    // a stand ahead on the very section it starts on
                    StandGoals(links.Count - 1, lk, s.cost);
                }
                int i = links.Count - 1;
                float d0 = s.cost + links[i].cost;
                dist[i] = d0;
                open.Add((d0, i));
            }

            var closed = new HashSet<int>();
            while (open.Count > 0)
            {
                int bi = 0;
                for (int k = 1; k < open.Count; k++)
                    if (open[k].d < open[bi].d) bi = k;
                var (d, state) = open[bi];
                open.RemoveAt(bi);
                if (!closed.Add(state)) continue;
                if (best != null && d >= best.total) break;
                var via = links[state];
                int node = via.to;
                var n = nodes[node];

                // departures: line up here?
                if (runways != null && n.runway != null && via.edge != null)
                    foreach (var choice in runways)
                        if (choice.runway == n.runway && TakeoffGoal(via, n, choice, d, out var g))
                        {
                            g.state = state;
                            Offer(g);
                        }

                if (!outOf.TryGetValue(node, out var outs)) continue;
                bool landing = via.start != null && via.start.landing;
                foreach (int next in outs)
                {
                    var l = links[next];
                    if (via.edge != null && l.edge == via.edge) continue;   // no turning back
                    float turn = Vector2.Angle(via.dir, l.dir);
                    bool offRunway = via.edge == null;
                    if (turn > (offRunway ? MaxRunwayTurn : MaxTurn)) continue;
                    if (landing && !CanExit(via.start, n, l, turn)) continue;
                    float cost = d + Turn(turn);
                    if (n.runway != null && !offRunway) cost += CrossingCost;
                    StandGoals(next, l, cost);
                    float nd = cost + l.cost;
                    if (dist.TryGetValue(next, out float old) && old <= nd) continue;
                    dist[next] = nd;
                    prev[next] = state;
                    open.Add((nd, next));
                }
            }

            lastPrev = prev;
            return best;
        }

        Dictionary<int, int> lastPrev;

        /// <summary>Lining up from section `via` at runway node n: straight onto the runway, or back-tracking to its
        /// end and turning round; false if the runway left isn't long enough either way.</summary>
        bool TakeoffGoal(Link via, TaxiwayNode n, RunwayChoice choice, float cost, out Goal goal)
        {
            goal = null;
            var r = choice.runway;
            var T = choice.fromStart ? r.Axis2D : -r.Axis2D;
            r.Locate(n.position, out float s, out _);
            float sT = choice.fromStart ? s : r.Length - s;   // from the threshold
            float hold = HoldingDistance(via.dir, r, via.length);
            float settle = Settle;

            float turn = Vector2.Angle(via.dir, T);
            if (turn <= MaxRunwayTurn)
            {
                float rad = CornerRadius(via.edge.width, turn, false);
                float cut = Mathf.Min(rad * Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad), hold - 2f);
                float lineUp = sT + cut + settle;
                if (r.Length - lineUp >= choice.need)
                {
                    goal = new Goal
                    {
                        total = cost + Turn(turn) + lineUp * LostRunwayCost, choice = choice,
                    };
                    return true;
                }
            }
            float back = Vector2.Angle(via.dir, -T);
            float ur = UTurnRadius(r);
            if (back > MaxRunwayTurn || ur <= 0f) return false;
            float sU = TaxiPath.Builder.UTurnReach(ur) + 3f;
            if (sT <= sU + 1f || r.Length - sU - settle < choice.need) return false;
            goal = new Goal
            {
                total = cost + Turn(back) + (sT - sU) * BackTrackFactor + RunwayUTurnCost + (sU + settle) * LostRunwayCost,
                choice = choice, backTrack = true, turnRadius = ur,
            };
            return true;
        }

        /// <summary>Can a landing aircraft take this exit: a turn it can make at the speed it can slow to by then?</summary>
        bool CanExit(Start s, TaxiwayNode n, Link exit, float turn)
        {
            if (!s.landing) return true;
            if (turn > 120f) return false;
            var r = n.runway;
            if (r == null) return false;
            float sNode = Vector2.Dot(At(n.id) - Geo.XZ(s.nose), s.along);
            float rad = turn <= RapidExitAngle
                ? Mathf.Min(RapidExitRadius, 0.45f * exit.length / Mathf.Max(Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad), 1e-3f))
                : CornerRadius(exit.edge.width, turn, false);
            float vExit = Mathf.Min(RapidExitSpeed, Mathf.Sqrt(Lateral * rad));
            float v0 = type.TouchdownSpeed;
            float cut = rad * Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad);
            float need = Mathf.Max(0f, v0 * v0 - vExit * vExit) / (2f * s.landingDecel) + cut;
            return sNode >= need;
        }

        // ------------------------------------------------------------------ departures

        /// <summary>The departure route from these starts to the best runway entry (null with a reason).</summary>
        public TaxiRoute Departure(List<Start> starts, List<RunwayChoice> runways, out string problem, out Start from)
        {
            problem = null;
            from = null;
            if (runways.Count == 0)
            {
                problem = "no runway is open for it";
                return null;
            }
            var goal = Search(starts, runways, null);
            if (goal == null)
            {
                problem = "no way to a runway it can take off from";
                return null;
            }
            var chain = Chain(goal.state, out from);
            var route = new TaxiRoute { runway = goal.choice.runway, fromStart = goal.choice.fromStart, backTrack = goal.backTrack, cost = goal.total };
            var b = Lay(route, from, chain);
            // onto the runway
            var r = goal.choice.runway;
            var T = goal.choice.fromStart ? r.Axis2D : -r.Axis2D;
            var thr = Geo.XZ(goal.choice.fromStart ? r.start : r.end);
            var last = links[chain[chain.Count - 1]];
            var node = At(last.to);
            float hold = HoldingDistance(last.dir, r, last.length);
            float settle = Settle;
            b.Limit(Mathf.Min(type.TaxiSpeed, 6f), Flight.TaxiBraking);
            if (!goal.backTrack)
            {
                float turn = Vector2.Angle(last.dir, T);
                b.Via(node, T, CornerRadius(last.edge.width, turn, false), hold - 2f, 1000f);
                b.Ahead(settle);
            }
            else
            {
                float turn = Vector2.Angle(last.dir, -T);
                b.Via(node, -T, CornerRadius(last.edge.width, turn, false), hold - 2f, 1000f);
                float sU = TaxiPath.Builder.UTurnReach(goal.turnRadius) + 3f;
                b.Limit(type.TaxiSpeed, Flight.TaxiBraking);
                b.LineTo(thr + T * sU);
                b.Limit(Mathf.Min(type.TaxiSpeed, 6f), Flight.TaxiBraking);
                b.UTurn(goal.turnRadius, Geo.Cross(T, last.dir) < 0f);
                b.Ahead(settle);
            }
            r.Locate(Geo.X0Z(b.At), out float lineUp, out _);
            route.lineUpS = lineUp;
            route.path = b.Build(from.gear, wheelbase, Lateral);
            return route;
        }

        // ------------------------------------------------------------------ arrivals

        /// <summary>
        /// Starts for a landing on runway r (fromStart: landing from its start towards its end), touching down at
        /// touchdownS metres past the threshold and braking at `decel`: rolling on to each runway node ahead (whether
        /// it can turn off there is checked per exit), or stopping, turning round and coming back to one behind.
        /// </summary>
        public List<Start> LandingStarts(Runway r, bool fromStart, float touchdownS, float decel)
        {
            var list = new List<Start>();
            var T = fromStart ? r.Axis2D : -r.Axis2D;
            var thr = Geo.XZ(fromStart ? r.start : r.end);
            var td = thr + T * touchdownS;
            var noseAt = td + T * wheelbase;
            float v0 = type.TouchdownSpeed;
            float ur = UTurnRadius(r);
            // where it could turn round: slowed to taxiing pace
            float sStop = touchdownS + Mathf.Max(0f, v0 * v0 - 9f) / (2f * decel) + 10f;
            bool canTurn = ur > 0f && sStop + TaxiPath.Builder.UTurnReach(ur) <= r.Length - 3f;
            foreach (var n in net.nodes)
            {
                if (n.runway != r) continue;
                float s = Vector2.Dot(Geo.XZ(n.position) - thr, T);
                if (s > touchdownS + wheelbase + 1f)
                {
                    list.Add(new Start
                    {
                        node = n.id, along = T, cost = (s - touchdownS) * RolloutCost, nose = noseAt, heading = T, gear = td,
                        landing = true, landingDecel = decel, touchdownS = touchdownS,
                        lay = b =>
                        {
                            b.Limit(float.PositiveInfinity, decel);
                            return 0f;
                        },
                    });
                }
                if (canTurn && s < sStop - 5f && s > 1f)
                {
                    float back = sStop - s;
                    list.Add(new Start
                    {
                        node = n.id, along = -T, cost = (sStop - touchdownS) * RolloutCost + back * BackTrackFactor + RunwayUTurnCost,
                        nose = noseAt, heading = T, gear = td, landingDecel = decel, touchdownS = touchdownS,
                        lay = b =>
                        {
                            b.Limit(float.PositiveInfinity, decel);
                            b.LineTo(thr + T * sStop);
                            b.Limit(Mathf.Min(type.TaxiSpeed, 5f), Flight.TaxiBraking);
                            b.UTurn(ur);
                            b.Limit(type.TaxiSpeed, Flight.TaxiBraking);
                            return 0f;
                        },
                    });
                }
            }
            return list;
        }

        /// <summary>The arrival route from touchdown to the best of these stands (null with a reason).</summary>
        public TaxiRoute Arrival(Runway r, bool fromStart, List<Start> starts, List<StandLayout> stands, out string problem,
            out Start from)
        {
            problem = null;
            from = null;
            if (stands.Count == 0)
            {
                problem = "no free stand takes it";
                return null;
            }
            if (starts.Count == 0)
            {
                problem = "no way off the runway";
                return null;
            }
            var goal = Search(starts, null, stands);
            if (goal == null)
            {
                problem = "no way from the runway to a free stand";
                return null;
            }
            var chain = Chain(goal.state, out from);
            var route = new TaxiRoute { runway = r, fromStart = fromStart, stand = goal.stand, cost = goal.total, touchdownS = from.touchdownS };
            var b = Lay(route, from, chain, goal.uGoal);
            // into the stand
            var l = goal.stand;
            float slow = Mathf.Min(type.TaxiSpeed * 0.35f, 3f);
            var last = links[chain[chain.Count - 1]];
            if (l.TaxiOut)
            {
                var lead = l.LeadIn(1f);
                b.LineTo(lead[0].point);
                b.Limit(slow, 0.5f);
                for (int i = 1; i < lead.Count; i++) b.LineTo(lead[i].point);
            }
            else
            {
                b.Limit(slow, 0.5f);
                float room = Mathf.Abs(goal.uGoal - last.uFrom) * 0.9f;
                b.Via(l.entry, l.axis, RunwayRules.StandTurnRadius(l.Code), room, 0.7f * Vector2.Distance(l.entry, l.stop));
                b.LineTo(l.stop);
            }
            // the last section ends at the stand
            var tr = route.traversals[route.traversals.Count - 1];
            tr.uTo = goal.uGoal;
            route.traversals[route.traversals.Count - 1] = tr;
            route.standFrom = tr.dStart + Mathf.Abs(goal.uGoal - tr.uFrom) - 2f * l.halfWidth;
            route.path = b.Build(from.gear, wheelbase, Lateral);
            // somewhere to wait off the runway, short of the first junction or runway after it
            if (route.runwayFree > 0f)
            {
                float wait = route.runwayFree + 0.5f;
                bool room = wait < route.path.Length - 1f;
                foreach (var h in route.holds)
                    if (h.at < wait + 0.5f) room = false;
                if (room)
                {
                    route.vacate = new Hold { kind = HoldKind.Route, at = wait, release = wait };
                    route.holds.Add(route.vacate);
                    route.holds.Sort((x, y) => x.at.CompareTo(y.at));
                }
            }
            return route;
        }

        // ------------------------------------------------------------------ laying the way

        /// <summary>The links from the start link to `state`, and the start.</summary>
        List<int> Chain(int state, out Start start)
        {
            var chain = new List<int> { state };
            while (lastPrev.TryGetValue(chain[chain.Count - 1], out int p)) chain.Add(p);
            chain.Reverse();
            start = links[chain[0]].start;
            return chain;
        }

        /// <summary>
        /// Lay the way along the chain of links (the first is the start's), rounding off each node, and note the
        /// sections, the junctions and the runway-holding positions on the way. Ends at the last link's end node
        /// (departures turn onto the runway from there) or partway along it (arrivals turn into the stand).
        /// </summary>
        TaxiPath.Builder Lay(TaxiRoute route, Start start, List<int> chain, float endU = float.NaN)
        {
            var b = new TaxiPath.Builder(start.nose, start.heading);
            float offset = start.lay != null ? start.lay(b) : 0f;
            if (start.node < 0) b.Limit(type.TaxiSpeed, Flight.TaxiBraking);
            Hold crossing = null;

            // up to a runway: its holding position
            void UpTo(Link link, float dStart, bool last)
            {
                var to = nodes[link.to];
                if (to.runway == null) return;
                float line = dStart + link.length - HoldingDistance(link.dir, to.runway, link.length);
                crossing = new Hold
                {
                    kind = HoldKind.Runway, runway = to.runway, at = Mathf.Max(dStart, line - noseOverhang - 1f),
                    takeoff = last && route.stand == null && route.runway == to.runway,
                };
                route.holds.Add(crossing);
            }

            var first = links[chain[0]];
            if (first.edge != null)
            {
                float d0 = b.Length - offset;
                route.traversals.Add(new Traversal { edge = first.edge, dir = Dir(first), uFrom = first.uFrom, uTo = first.uTo, dStart = d0 });
                UpTo(first, d0, chain.Count == 1);
            }
            for (int k = 1; k < chain.Count; k++)
            {
                var prev = links[chain[k - 1]];
                var link = links[chain[k]];
                int node = link.from;
                var p = At(node);
                var n = nodes[node];
                float turn = Vector2.Angle(prev.dir, link.dir);
                bool landing = prev.edge == null && start.landing;
                float width = prev.edge != null ? Mathf.Min(prev.edge.width, link.edge.width) : link.edge.width;
                float maxIn = prev.edge == null ? 1000f : k == 1 ? Mathf.Max(0f, prev.length - offset) * 0.9f : 0.45f * prev.length;
                float maxOut = 0.45f * link.length;
                // the turn into a stand on the last section starts after this corner's curve has ended
                if (k == chain.Count - 1 && !float.IsNaN(endU)) maxOut = Mathf.Min(maxOut, 0.4f * Mathf.Abs(endU - link.uFrom));
                if (n.runway != null && prev.edge != null)
                    maxIn = Mathf.Min(maxIn, HoldingDistance(prev.dir, n.runway, prev.length) - 2f);
                float radius = CornerRadius(width, turn, landing);
                float before = b.Length + Mathf.Max(0f, Vector2.Distance(b.At, p) - Mathf.Min(radius * Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad), Mathf.Min(maxIn, maxOut)));
                float cut;
                if (landing)
                {
                    float rad = turn <= RapidExitAngle ? Mathf.Min(RapidExitRadius, maxOut / Mathf.Max(Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad), 1e-3f)) : radius;
                    float vExit = Mathf.Min(RapidExitSpeed, Mathf.Sqrt(Lateral * rad));
                    b.Limit(float.PositiveInfinity, start.landingDecel);
                    cut = b.Via(p, link.dir, radius, maxIn, maxOut, vExit);
                    b.Limit(type.TaxiSpeed, Flight.TaxiBraking);
                }
                else cut = b.Via(p, link.dir, radius, maxIn, maxOut);
                float dStart = b.Length - cut;

                if (Junction(node, out float zone))
                {
                    float at = (before + b.Length) * 0.5f;
                    route.holds.Add(new Hold
                    {
                        kind = HoldKind.Junction, node = node, zoneRadius = zone,
                        at = at - zone - noseOverhang - 2f, release = at + zone + wheelbase + tail + 2f,
                    });
                }
                // off a runway: the far holding position
                if (n.runway != null)
                {
                    float clear = dStart + HoldingDistance(link.dir, n.runway, link.length) + wheelbase + tail + 2f;
                    if (crossing != null && crossing.runway == n.runway) crossing.release = clear;
                    else if (start.node == node) route.runwayFree = clear;
                    crossing = null;
                }
                route.traversals.Add(new Traversal { edge = link.edge, dir = Dir(link), uFrom = link.uFrom, uTo = link.uTo, dStart = dStart });
                UpTo(link, dStart, k == chain.Count - 1);
            }
            route.holds.Sort((x, y) => x.at.CompareTo(y.at));
            return b;
        }
    }
}
