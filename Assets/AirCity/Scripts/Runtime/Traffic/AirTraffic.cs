using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Traffic
{
    /// <summary>
    /// The airport's air traffic (airport.Traffic): its flights, and the tower that keeps them apart. Runways are used
    /// into the wind (Airport.windFrom). Departures leave their stands (pushed back by a tug, by hand for light
    /// aircraft, or taxiing out), taxi to the runway, hold short and take off; arrivals fly a 3° final, land, take the
    /// first exit they can and taxi to a free stand. Simulated in game seconds from Airport.Simulate.
    ///
    ///   var f = airport.Traffic.Depart(aircraft, out string problem);   // its route: f.Route
    ///   var g = airport.Traffic.Arrive(newAircraft, out problem);       // placed on final
    ///   airport.Traffic.PreviewDeparture(aircraft, out problem);        // the way it would go, nothing reserved
    ///
    /// Keeping them apart:
    ///  - one aircraft at a time on a runway (lining up and taking off, landing until it's off past the holding
    ///    position, crossing), and none onto it while an arrival is close enough to need it;
    ///  - one at a time through a taxiway junction, and never stopping inside one or on a runway;
    ///  - a section of taxiway is used one way at a time: an aircraft waits at the hold before it while another is
    ///    on it (or cleared onto it) the other way, and routes are planned round such sections where they can be.
    ///    Of those waiting for each other's way, the one that has waited longest goes first (once it can);
    ///    An arrival is cleared to land only if nobody on their way still has to come the other way along the stretch
    ///    it will wait on, so two never meet nose to nose (a one-way taxilane works: one way waits while the other goes);
    ///  - each stops short of any aircraft its outline would touch on the way ahead (following another, a parked one
    ///    in the way).
    /// </summary>
    public sealed class AirTraffic
    {
        const float Step = 0.25f;
        /// <summary>Light aircraft (under 5.7 t) are pushed back by hand.</summary>
        const float LightMass = 5700f;
        /// <summary>Arrivals start on final this long before touchdown, and follow each other at least this far apart.</summary>
        const float FinalTime = 110f, ArrivalSpacing = 120f;
        /// <summary>A new arrival leaves this much more behind the one ahead while departures wait for the runway, so
        /// one can take off between them (seconds).</summary>
        const float DepartureGap = 90f;
        /// <summary>A departure doesn't start (push back, taxi out) onto taxiways an arrival this close to touchdown
        /// will need the other way (seconds).</summary>
        const float InboundTime = 150f;
        /// <summary>A departure ready to start gives way to arrivals on final for this long at most (game seconds).</summary>
        const float StartPatience = 90f;
        /// <summary>A flight gives way to others who have waited longer for its way for this long at most (game
        /// seconds): then it goes when its way is clear, so giving way can't hold anyone up for good.</summary>
        const float YieldLimit = 600f;

        readonly Airport airport;
        readonly List<Flight> flights = new List<Flight>();
        readonly Dictionary<int, Flight> junctions = new Dictionary<int, Flight>();
        readonly Dictionary<Runway, Flight> runways = new Dictionary<Runway, Flight>();
        readonly Dictionary<Flight, int> passed = new Dictionary<Flight, int>();
        /// <summary>The vehicle each taxiing aircraft is waiting for (the vehicle goes on, keeping only a tug's
        /// clearance from it: AirportMaintenance).</summary>
        readonly Dictionary<Aircraft.Aircraft, VehicleTeam> waitingFor = new Dictionary<Aircraft.Aircraft, VehicleTeam>();
        /// <summary>Aircraft held at a hold (or their start) for a vehicle on their way (OncomingVehicle).</summary>
        readonly Dictionary<Aircraft.Aircraft, VehicleTeam> heldFor = new Dictionary<Aircraft.Aircraft, VehicleTeam>();
        /// <summary>Taxiing aircraft keep this far behind a vehicle ahead of their nose (metres).</summary>
        const float VehicleGap = 12f;
        /// <summary>Vehicles keep off the track a taxiing aircraft's fuselage will sweep this far ahead of it at most,
        /// as far as it's cleared (metres); before it's cleared on, a moving vehicle in the next this-many metres the
        /// other way (or across) goes first.</summary>
        const float TrackAhead = 200f, OncomingLook = 150f;
        /// <summary>The track is the fuselage grown by this (metres): a little more than the clearance vehicles keep
        /// from a fuselage they pass (AirportMaintenance.TugClearance), so they stop short of it rather than meet the
        /// fuselage on it; a vehicle keeping right on a straight passes outside it, under the wing.</summary>
        const float TrackClearance = 0.6f;
        /// <summary>Vehicles keep this far (metres) from the fuselage's way back off a stand while it's pushed.</summary>
        const float PushClearance = 2f;
        /// <summary>Room for the tug pushing it, ahead of the nose wheel (metres).</summary>
        const float PushTugLength = 7f, PushTugWidth = 3.5f;
        List<(Aircraft.Aircraft who, bool parked, List<AirportMaintenance.Box> shape, Vector2 at)> outlines;

        public AirTraffic(Airport airport) => this.airport = airport;

        public Airport Airport => airport;
        public IReadOnlyList<Flight> Flights => flights;
        /// <summary>Game seconds simulated so far.</summary>
        public float Clock { get; private set; }

        public Flight FlightOf(Aircraft.Aircraft a)
        {
            if (a == null) return null;
            foreach (var f in flights)
                if (f.aircraft == a && !f.Finished) return f;
            return null;
        }

        // ------------------------------------------------------------------ runways in use

        /// <summary>Is a runway used from its start towards its end in this wind (from `windFrom`, degrees)? Aircraft
        /// take off and land into the wind; a wind straight across leaves it the start way.</summary>
        public static bool FromStart(float bearing, float windFrom) => Mathf.Abs(Mathf.DeltaAngle(bearing, windFrom)) <= 90f + 1e-3f;

        public bool UsedFromStart(Runway r) => FromStart(r.Bearing, airport.windFrom);

        /// <summary>The designator of the way a runway is used now ("27").</summary>
        public string InUse(Runway r) => (UsedFromStart(r) ? r.StartDesignator : r.EndDesignator).Trim();

        /// <summary>Can this type take off from (or land on) a runway now, and the length it needs? Like
        /// Runway.Check, but for the one distance.</summary>
        public bool Usable(Runway r, AircraftType type, bool takeoff, out float need, out string problem)
        {
            need = 0f;
            problem = null;
            var c = r.Check(type);
            if (c.problem != RunwayProblem.None && c.problem != RunwayProblem.TooShort)
            {
                problem = c.ToString();
                return false;
            }
            if (r.wear >= RunwayRules.ClosedWear)
            {
                problem = "closed for repair";
                return false;
            }
            if (Airport.IsNight && r.lighting == RunwayLighting.None)
            {
                problem = "unlit at night";
                return false;
            }
            var f = RunwayRules.LengthFactors(r.surface, airport.Condition, r.snow, r.grass);
            if (f == null)
            {
                problem = "closed";
                return false;
            }
            need = takeoff ? type.takeoffDistance * f.Value.x : type.landingDistance * f.Value.y;
            if (r.Length + 0.01f < need)
            {
                problem = $"needs {need:N0} m";
                return false;
            }
            return true;
        }

        List<TaxiPlanner.RunwayChoice> Choices(AircraftType type, bool takeoff, out string problem)
        {
            problem = null;
            var list = new List<TaxiPlanner.RunwayChoice>();
            foreach (var r in airport.Runways)
            {
                if (Usable(r, type, takeoff, out float need, out string why))
                    list.Add(new TaxiPlanner.RunwayChoice { runway = r, fromStart = UsedFromStart(r), need = need });
                else problem ??= $"{r.Designation}: {why}";
            }
            if (list.Count > 0) problem = null;
            else if (problem == null) problem = "the airport has no runway";
            return list;
        }

        // ------------------------------------------------------------------ departures

        /// <summary>
        /// Send an aircraft off: it leaves its stand (or wherever it stands on a taxiway), taxis to the best runway entry
        /// for the wind and takes off. Null, with the reason, if it can't (no runway it can use, no way there, no tug).
        /// </summary>
        public Flight Depart(Aircraft.Aircraft a, out string problem)
        {
            if (a == null || a.type == null)
            {
                problem = "no aircraft";
                return null;
            }
            if (FlightOf(a) != null)
            {
                problem = "it's on its way already";
                return null;
            }
            if (airport.Maintenance.TowOf(a) != null)
            {
                problem = "it's being pushed back";
                return null;
            }
            var route = PlanDeparture(a, null, out var start, out problem);
            if (route == null) return null;
            var f = new Flight(this, FlightKind.Departure, a, route) { start = start };
            flights.Add(f);
            return f;
        }

        /// <summary>The way an aircraft would go if it left now (nothing is reserved), or null with the reason.</summary>
        public TaxiRoute PreviewDeparture(Aircraft.Aircraft a, out string problem)
        {
            if (a == null || a.type == null)
            {
                problem = "no aircraft";
                return null;
            }
            return PlanDeparture(a, null, out _, out problem);
        }

        TaxiRoute PlanDeparture(Aircraft.Aircraft a, Flight self, out TaxiPlanner.Start from, out string problem)
        {
            from = null;
            var type = a.type;
            var choices = Choices(type, true, out problem);
            if (choices.Count == 0) return null;
            var planner = new TaxiPlanner(airport, type, this, self);
            var t = airport.transform;
            var pos = Geo.XZ(t.InverseTransformPoint(a.transform.position));
            var fwd = Geo.XZ(t.InverseTransformDirection(a.transform.forward)).normalized;
            var starts = DepartureStarts(type, pos, fwd, out problem);
            if (starts.Count == 0) return null;
            var route = planner.Departure(starts, choices, out problem, out from);
            if (route != null && from != null)
            {
                route.standTo = from.clearOfStand;
                route.pushBack = from.push;
                route.leaving = StandAt(pos);
            }
            return route;
        }

        /// <summary>The parking stand an aircraft stands on (its main gear, Airport space), or null.</summary>
        StandLayout StandAt(Vector2 pos)
        {
            foreach (var l in airport.Taxiways.Layouts())
                if (!l.stand.DeIcing && l.Contains(pos)) return l;
            return null;
        }

        /// <summary>Where a departure can join the taxiways: pushed back either way off a push-back stand, swinging
        /// either way out of a taxi-out stand, or straight on from where it stands on a taxiway.</summary>
        List<TaxiPlanner.Start> DepartureStarts(AircraftType type, Vector2 pos, Vector2 fwd, out string problem)
        {
            problem = null;
            var list = new List<TaxiPlanner.Start>();
            var net = airport.Taxiways;
            float wb = Mathf.Max(type.Wheelbase, 0.3f);
            var stand = StandAt(pos);
            if (stand != null)
            {
                if (stand.edge == null)
                {
                    problem = $"{stand.stand.Name} isn't connected: no taxiway runs along its front";
                    return list;
                }
                var a = net.At(stand.edge.a);
                var edgeT = (net.At(stand.edge.b) - a).normalized;
                float edgeLen = net.EdgeLength(stand.edge);
                if (!stand.TaxiOut)
                {
                    bool hand = type.maxTakeoffMass < LightMass && !AnyTugFor(type);
                    if (!hand && !AnyTugFor(type))
                    {
                        problem = "no tug here can push it back";
                        return list;
                    }
                    var seen = new List<Vector2>();
                    foreach (bool otherWay in new[] { false, true })
                    {
                        var pb = PushBackPath.Plan(net, type, pos, fwd, otherWay, out string why);
                        if (pb == null)
                        {
                            problem ??= why;
                            continue;
                        }
                        if (seen.Exists(s => Vector2.Dot(s, pb.endFacing) > 0.99f)) continue;
                        seen.Add(pb.endFacing);
                        list.Add(new TaxiPlanner.Start
                        {
                            edge = stand.edge, dir = Vector2.Dot(pb.endFacing, edgeT) > 0f ? +1 : -1, u = Vector2.Dot(pb.NoseEnd - a, edgeT),
                            nose = pb.NoseEnd, heading = pb.endFacing, gear = pb.End, cost = otherWay ? 30f : 0f,
                            pushBack = true, handPush = hand, pushOtherWay = otherWay, push = pb,
                        });
                    }
                    if (list.Count > 0) problem = null;
                    return list;
                }
                // taxi out: straight down the stand centreline, then round onto the taxiway
                if (!Geo.Lines(pos, fwd, stand.entry, stand.along, out float t, out _) || t < wb)
                {
                    problem = $"it isn't facing out of {stand.stand.Name}";
                    return list;
                }
                var corner = pos + fwd * t;
                float u = Vector2.Dot(corner - a, edgeT);
                foreach (var face in new[] { stand.outAlong, stand.inAlong })
                {
                    int dir = Vector2.Dot(face, edgeT) > 0f ? +1 : -1;
                    float room = dir > 0 ? edgeLen - u : u;
                    var s = new TaxiPlanner.Start
                    {
                        edge = stand.edge, dir = dir, u = u, nose = pos + fwd * wb, heading = fwd, gear = pos,
                        cost = face == stand.outAlong ? 0f : 40f,
                    };
                    float radius = Mathf.Max(RunwayRules.StandTurnRadius(stand.Code), wb / Mathf.Sin(Mathf.Clamp(type.maxSteeringAngle, 10f, 85f) * Mathf.Deg2Rad) * 1.05f);
                    s.lay = b =>
                    {
                        b.Limit(Mathf.Min(type.TaxiSpeed * 0.35f, 3f), 0.5f);
                        float cut = b.Via(corner, face, radius, t - wb - 0.5f, 0.45f * room);
                        s.clearOfStand = b.Length + type.Length;
                        b.Limit(type.TaxiSpeed, Flight.TaxiBraking);
                        return cut;
                    };
                    list.Add(s);
                }
                return list;
            }
            // on a taxiway: on the way it faces
            var nose = pos + fwd * wb;
            if (!net.TryNearestEdge(Geo.X0Z(nose), 30f, out var edge, out var point) || edge.IsRoad)
            {
                problem = "it isn't on a stand or a taxiway";
                return list;
            }
            var ea = net.At(edge.a);
            var et = (net.At(edge.b) - ea).normalized;
            var p = Geo.XZ(point);
            foreach (int dir in new[] { +1, -1 })
            {
                if (Vector2.Angle(fwd, et * dir) > 60f) continue;
                var target = p + et * (dir * 6f);
                list.Add(new TaxiPlanner.Start
                {
                    edge = edge, dir = dir, u = Vector2.Dot(p - ea, et), nose = nose, heading = fwd, gear = pos,
                    lay = b =>
                    {
                        b.LineTo(target);
                        return 6f;
                    },
                });
            }
            if (list.Count == 0) problem = "it can't turn round here";
            return list;
        }

        bool AnyTugFor(AircraftType type)
        {
            foreach (var d in airport.Depots)
                if ((d.smallTugs > 0 && d.smallTugType != null && d.smallTugType.CanTow(type)) ||
                    (d.largeTugs > 0 && d.largeTugType != null && d.largeTugType.CanTow(type)))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ arrivals

        /// <summary>
        /// Bring an aircraft in to land: it is put on final for the best runway in use (behind any arrivals already on
        /// it), and its way off the runway to the nearest free stand that takes it is planned. Null, with the reason,
        /// if there is no runway it can land on or no free stand it can reach (the aircraft is left where it was).
        /// </summary>
        public Flight Arrive(Aircraft.Aircraft a, out string problem)
        {
            if (a == null || a.type == null)
            {
                problem = "no aircraft";
                return null;
            }
            var route = PlanArrival(a.type, null, out problem);
            if (route == null) return null;
            var f = new Flight(this, FlightKind.Arrival, a, route) { start = lastStart };
            flights.Add(f);
            f.StartApproach(FinalDistance(f, route.runway));
            return f;
        }

        /// <summary>Could an aircraft of this type come in now (a runway and a free stand)?</summary>
        public bool CanArrive(AircraftType type, out string problem) => PlanArrival(type, null, out problem) != null;

        TaxiPlanner.Start lastStart;

        TaxiRoute PlanArrival(AircraftType type, Flight self, out string problem)
        {
            var choices = Choices(type, false, out problem);
            if (choices.Count == 0) return null;
            var stands = FreeStands(type, self);
            if (stands.Count == 0)
            {
                problem = "no free stand takes it";
                return null;
            }
            TaxiRoute best = null;
            float bestCost = float.MaxValue;
            foreach (var c in choices)
            {
                var planner = new TaxiPlanner(airport, type, this, self);
                float touchdown = TouchdownDistance(type, c.runway);
                var starts = planner.LandingStarts(c.runway, c.fromStart, touchdown, LandingDecel(type, c.runway));
                var route = planner.Arrival(c.runway, c.fromStart, starts, stands, out string why, out var from);
                if (route == null)
                {
                    problem ??= why;
                    continue;
                }
                // a busy runway: arrivals queued for it
                float cost = route.cost;
                foreach (var f in flights)
                    if (f != self && f.kind == FlightKind.Arrival && f.Route.runway == c.runway && f.Phase == FlightPhase.Approach) cost += 400f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = route;
                    lastStart = from;
                }
            }
            if (best != null) problem = null;
            return best;
        }

        /// <summary>Touchdown on the aiming point: a fifth of the landing distance in, up to 300 m.</summary>
        static float TouchdownDistance(AircraftType type, Runway r) => Mathf.Clamp(0.2f * type.landingDistance, 60f, 300f);

        /// <summary>Braking after touchdown: to a stop in about 55% of the landing distance (from 15 m) on this runway
        /// now, within 1.2 and 3 m/s².</summary>
        float LandingDecel(AircraftType type, Runway r)
        {
            var f = RunwayRules.LengthFactors(r.surface, airport.Condition, r.snow, r.grass) ?? Vector2.one;
            float v = type.TouchdownSpeed;
            return Mathf.Clamp(v * v / (2f * 0.55f * type.landingDistance * f.y), 1.2f, 3f);
        }

        /// <summary>The parking stands this type can use that nobody is on, heading for or still leaving (in front of it,
        /// pushed back or taxiing out).</summary>
        List<StandLayout> FreeStands(AircraftType type, Flight self)
        {
            var list = new List<StandLayout>();
            var net = airport.Taxiways;
            var t = airport.transform;
            foreach (var l in net.Layouts())
            {
                if (l.stand.DeIcing || l.edge == null || net.CheckStand(l.stand, type) != StandProblem.None) continue;
                bool taken = false;
                foreach (var a in Aircraft.Aircraft.All)
                {
                    if (a == null || (self != null && a == self.aircraft)) continue;
                    var at = t.InverseTransformPoint(a.transform.position);
                    if (at.y < 3f && l.Contains(new Vector2(at.x, at.z))) taken = true;
                }
                foreach (var f in flights)
                {
                    if (f == self || f.Finished || f.Route == null) continue;
                    if (f.Route.stand != null && f.Route.stand.stand == l.stand) taken = true;
                    if (f.Route.leaving != null && f.Route.leaving.stand == l.stand && !f.ClearOfStand) taken = true;
                }
                if (!taken) list.Add(l);
            }
            return list;
        }

        /// <summary>How far out on final a new arrival starts: FinalTime out, or ArrivalSpacing behind the last one on
        /// this runway (and DepartureGap more while departures are on their way to it).</summary>
        float FinalDistance(Flight f, Runway r)
        {
            float d = f.type.ApproachSpeed * FinalTime;
            bool departing = flights.Exists(g => g.kind == FlightKind.Departure && g.Route != null && g.Route.runway == r && g.OnGround && g.Phase <= FlightPhase.LinedUp);
            float spacing = ArrivalSpacing + (departing ? DepartureGap : 0f);
            foreach (var g in flights)
                if (g != f && g.Route != null && g.Route.runway == r && g.Phase == FlightPhase.Approach)
                    d = Mathf.Max(d, g.ToTouchdown / Mathf.Max(g.type.ApproachSpeed, 1f) * f.type.ApproachSpeed + spacing * f.type.ApproachSpeed);
            return d;
        }

        /// <summary>After a go-around: back onto final behind the others, its way to a stand planned again. False if it
        /// can't come in any more (it flies away).</summary>
        internal bool Requeue(Flight f)
        {
            ReleaseAll(f);
            var route = PlanArrival(f.type, f, out _);
            if (route == null) return false;
            f.Route = route;
            f.start = lastStart;
            passed.Remove(f);
            f.StartApproach(FinalDistance(f, route.runway));
            return true;
        }

        // ------------------------------------------------------------------ simulation

        public void Simulate(float seconds)
        {
            if (flights.Count == 0)
            {
                Clock += seconds;
                return;
            }
            while (seconds > 1e-4f)
            {
                float dt = Mathf.Min(seconds, Step);
                seconds -= dt;
                Clock += dt;
                foreach (var f in flights.ToArray())
                    if (f.aircraft == null) Remove(f);
                outlines = null;
                foreach (var f in flights.ToArray())
                    if (!f.Finished) f.Step(dt);
                flights.RemoveAll(f => f.Finished);
            }
        }

        /// <summary>An arrival has parked: it gives back what it holds.</summary>
        internal void Finish(Flight f) => ReleaseAll(f);

        /// <summary>A departure (or an arrival that couldn't come back) has flown out of sight: it's taken away.</summary>
        internal void Gone(Flight f)
        {
            ReleaseAll(f);
            f.Phase = FlightPhase.Gone;
            if (f.aircraft == null) return;
            f.aircraft.Highlight = AircraftHighlight.None;
            if (Application.isPlaying) Object.Destroy(f.aircraft.gameObject);
            else f.aircraft.gameObject.SetActive(false);
        }

        void Remove(Flight f)
        {
            ReleaseAll(f);
            flights.Remove(f);
        }

        void ReleaseAll(Flight f)
        {
            if (f.aircraft != null)
            {
                waitingFor.Remove(f.aircraft);
                heldFor.Remove(f.aircraft);
            }
            foreach (var k in new List<int>(junctions.Keys))
                if (junctions[k] == f) junctions.Remove(k);
            foreach (var r in new List<Runway>(runways.Keys))
                if (runways[r] == f) runways.Remove(r);
        }

        // ------------------------------------------------------------------ clearances

        bool Active(Flight f) => f != null && !f.Finished && flights.Contains(f);

        /// <summary>
        /// May a flight go on past its hold `index`? Takes the junctions it can't stop between with it (each next
        /// one before it is clear of the last), and for a runway hold the runway: free, and no arrival close enough
        /// to need it before it's done. And no one coming the other way along its route, nor waiting longer to.
        /// Sets hold.waiting when not.
        /// </summary>
        internal bool TryClear(Flight f, int index)
        {
            var h = f.Route.holds[index];
            var chain = Clearable(f, index, true, out string why, out bool opposed);
            if (chain == null)
            {
                h.waiting = why;
                if (opposed) Queue(f, h.at);
                return false;
            }
            f.queuedSince = -1f;
            foreach (var c in chain)
            {
                c.cleared = true;
                c.waiting = null;
                if (c.kind == HoldKind.Junction) junctions[c.node] = f;
            }
            if (h.kind == HoldKind.Runway) runways[h.runway] = f;
            return true;
        }

        /// <summary>The holds f would be cleared past from its hold `index` (TryClear), or null with why not, and
        /// whether that's traffic the other way. `fair`: nor anyone waiting longer for its way the other way who could
        /// go (GiveWay).</summary>
        List<Hold> Clearable(Flight f, int index, bool fair, out string why, out bool opposed)
        {
            why = null;
            opposed = false;
            var holds = f.Route.holds;
            var h = holds[index];
            var chain = new List<Hold> { h };
            if (!h.takeoff)
            {
                float reach = h.release;
                for (int k = index + 1; k < holds.Count && holds[k].kind == HoldKind.Junction && holds[k].at < reach; k++)
                {
                    chain.Add(holds[k]);
                    reach = Mathf.Max(reach, holds[k].release);
                }
            }
            foreach (var c in chain)
                if (c.kind == HoldKind.Junction && junctions.TryGetValue(c.node, out var owner) && owner != f && Active(owner))
                {
                    why = $"waiting for {owner.Name} to clear the junction";
                    return null;
                }
            // one at a time, in the order they come: not while someone is still in the way it's cleared along (to the
            // runway for a takeoff, through the last junction)
            var last = chain[chain.Count - 1];
            var ahead = AircraftBefore(f, h.takeoff ? f.Route.path.Length : Mathf.Max(last.at, last.release));
            if (ahead != null)
            {
                why = $"behind {ahead}";
                return null;
            }
            if (h.kind == HoldKind.Runway && !RunwayFree(f, h, out why)) return null;
            if (!h.takeoff)
            {
                var blocker = RouteBlocker(f, h.at, float.MaxValue);
                if (blocker != null)
                {
                    why = $"waiting for {blocker.Name} to pass";
                    opposed = true;
                    return null;
                }
                var first = fair ? GiveWay(f, h.at) : null;
                if (first != null)
                {
                    why = $"giving way to {first.Name}";
                    opposed = true;
                    return null;
                }
                string vehicle = OncomingVehicle(f, h.at);
                if (vehicle != null)
                {
                    why = $"waiting for {vehicle} to pass";
                    return null;
                }
            }
            return chain;
        }

        /// <summary>Another aircraft on the ground on f's way between where f is and `at` metres along it, or null.</summary>
        string AircraftBefore(Flight f, float at)
        {
            var path = f.Route.path;
            float end = Mathf.Min(at, path.Length);
            if (end <= f.Along + 1f) return null;
            foreach (var (a, parked, shape, pos) in Outlines())
            {
                if (a == f.aircraft) continue;
                if (parked && (end >= f.Route.standFrom || f.Along <= f.Route.standTo)) continue;
                if (!parked && FlightOf(a) == null) continue;
                // close to the way at all?
                path.Pose(f.Along, out var here, out _, out _, out _);
                if ((pos - here).sqrMagnitude > (end - f.Along + a.type.Length + f.type.Length) * (end - f.Along + a.type.Length + f.type.Length)) continue;
                for (float d = f.Along + 2f; d <= end; d += 3f)
                {
                    path.Pose(d, out var p, out var facing, out _, out _);
                    if ((pos - p).sqrMagnitude > (a.type.Length + f.type.Length) * (a.type.Length + f.type.Length)) continue;
                    var me = AirportMaintenance.Outline(p, facing, f.type, 0f)[0];
                    foreach (var o in shape)
                        if (me.Overlaps(o)) return a.name;
                }
            }
            return null;
        }

        /// <summary>Is a runway free for a flight at its holding position: nobody on it, and no arrival due before
        /// it would be done (lined up and gone, or across)?</summary>
        bool RunwayFree(Flight f, Hold h, out string why)
        {
            why = null;
            string name = InUse(h.runway);
            if (runways.TryGetValue(h.runway, out var owner) && owner != f && Active(owner))
            {
                why = $"holding short of {name}: {owner.Name} {(owner.kind == FlightKind.Arrival ? "landing" : owner.Phase >= FlightPhase.LinedUp ? "taking off" : "on the runway")}";
                return false;
            }
            // how long until it's done with the runway: lined up and rolling until lift-off (the ground roll at an even
            // acceleration takes 1.2 x the takeoff distance / VR) and climbing away, or across; and the arrival must
            // have it back before it decides to go around
            float taxi = Mathf.Max(f.type.TaxiSpeed * 0.5f, 3f);
            float need = h.takeoff
                ? (f.Route.path.Length - h.at) / taxi + 3f + 1.2f * f.type.takeoffDistance / Mathf.Max(f.type.RotateSpeed, 1f) + 10f
                : (h.release - h.at) / taxi + 5f;
            need += Flight.DecisionTime;
            foreach (var g in flights)
                if (g != f && g.kind == FlightKind.Arrival && g.Route.runway == h.runway && g.Phase == FlightPhase.Approach &&
                    g.TimeToTouchdown < need)
                {
                    why = $"holding short of {name}: {g.Name} on final";
                    return false;
                }
            return true;
        }

        /// <summary>
        /// Landing clearance: the runway free (and taken), and the way off it to the first place it can stop (its next
        /// hold, or the stand): the junctions and runway crossings before that taken too, and nobody on their way who
        /// still has to come along any of it the other way (they would meet it there, nose to nose, while it waits for
        /// them). When the way off it planned can't be cleared and `lookElsewhere`, another exit (or stand) that can
        /// will do. Why not, when not.
        /// </summary>
        internal bool TryClearLanding(Flight f, bool lookElsewhere, out string why)
        {
            var r = f.Route.runway;
            if (runways.TryGetValue(r, out var owner) && owner != f && Active(owner))
            {
                why = $"{owner.Name} on the runway";
                return false;
            }
            var chain = LandingChain(f, f.Route, out why);
            if (chain == null && lookElsewhere)
            {
                var other = OtherWayOff(f, out var start, out var otherChain);
                if (other != null)
                {
                    f.Route = other;
                    f.start = start;
                    chain = otherChain;
                    why = null;
                }
            }
            if (chain == null) return false;
            runways[r] = f;
            foreach (var c in chain)
            {
                c.cleared = true;
                if (c.kind == HoldKind.Junction) junctions[c.node] = f;
                if (c.kind == HoldKind.Runway) runways[c.runway] = f;
            }
            return true;
        }

        /// <summary>The holds an arrival on this route is cleared past as it lands, or null (with why) if it can't be
        /// cleared now.</summary>
        List<Hold> LandingChain(Flight f, TaxiRoute route, out string why)
        {
            why = null;
            var holds = route.holds;
            var chain = new List<Hold>();
            float reach = route.runwayFree;
            for (int k = 0; k < holds.Count && holds[k].at < reach; k++)
            {
                chain.Add(holds[k]);
                reach = Mathf.Max(reach, holds[k].release);
            }
            foreach (var c in chain)
            {
                if (c.kind == HoldKind.Junction && junctions.TryGetValue(c.node, out var j) && j != f && Active(j))
                {
                    why = $"{j.Name} in the junction off the runway";
                    return null;
                }
                if (c.kind == HoldKind.Runway && runways.TryGetValue(c.runway, out var o) && o != f && Active(o))
                {
                    why = $"{o.Name} on {InUse(c.runway)}";
                    return null;
                }
            }
            float stop = chain.Count < holds.Count ? holds[chain.Count].at : route.path.Length;
            var g = RouteBlocker(f, route, 0f, stop + 1f, false, true);
            if (g != null)
            {
                why = $"{g.Name} coming the other way";
                return null;
            }
            // and room to get off the runway: nobody stopping on the exit where its tail would still be on it
            foreach (var w in flights)
            {
                if (w == f || !w.OnGround || w.Route == null || w.Phase == FlightPhase.Approach) continue;
                float wStop = w.Route.path.Length;
                foreach (var h in w.Route.holds)
                    if (!h.cleared)
                    {
                        wStop = h.at;
                        break;
                    }
                float d = Along(route, w.Route, Mathf.Max(wStop, w.Along));
                if (d >= 0f && d - w.TailBehind < route.runwayFree + 5f)
                {
                    why = $"{w.Name} waiting on the way off the runway";
                    return null;
                }
            }
            return chain;
        }

        /// <summary>Where on `route` (metres along it) a point `d` metres along `other` is, if `other` is on the same
        /// section the same way there; else -1.</summary>
        static float Along(TaxiRoute route, TaxiRoute other, float d)
        {
            foreach (var o in other.traversals)
            {
                if (d < o.dStart - 0.5f || d > o.DEnd + 0.5f) continue;
                float u = o.U(d);
                foreach (var t in route.traversals)
                {
                    if (t.edge != o.edge || t.dir != o.dir) continue;
                    float k = (u - t.uFrom) * t.dir;
                    if (k >= -0.5f && k <= t.Span + 0.5f) return t.dStart + k;
                }
                return -1f;
            }
            return -1f;
        }

        /// <summary>For an arrival whose way off the runway can't be cleared: the cheapest way off by another exit
        /// ahead (each tried on its own) to a stand it could have, that can be; null if there's none.</summary>
        TaxiRoute OtherWayOff(Flight f, out TaxiPlanner.Start start, out List<Hold> chain)
        {
            start = null;
            chain = null;
            var r = f.Route.runway;
            var stands = FreeStands(f.type, f);
            if (stands.Count == 0) return null;
            var planner = new TaxiPlanner(airport, f.type, this, f);
            float decel = f.start != null && f.start.landingDecel > 0f ? f.start.landingDecel : LandingDecel(f.type, r);
            TaxiRoute best = null;
            foreach (var s in planner.LandingStarts(r, f.Route.fromStart, f.Route.touchdownS, decel))
            {
                var route = planner.Arrival(r, f.Route.fromStart, new List<TaxiPlanner.Start> { s }, stands, out _, out var from);
                if (route == null || (best != null && route.cost >= best.cost)) continue;
                var c = LandingChain(f, route, out _);
                if (c == null) continue;
                best = route;
                start = from;
                chain = c;
            }
            return best;
        }

        internal void CancelLanding(Flight f)
        {
            ReleaseAll(f);
            foreach (var h in f.Route.holds) h.cleared = h.released = false;
        }

        internal void Release(Flight f, Hold h)
        {
            h.released = true;
            if (h.kind == HoldKind.Junction && junctions.TryGetValue(h.node, out var o) && o == f) junctions.Remove(h.node);
            if (h.kind == HoldKind.Runway && !h.takeoff) ReleaseRunway(f, h.runway);
        }

        internal void ReleaseRunway(Flight f, Runway r)
        {
            if (r != null && runways.TryGetValue(r, out var o) && o == f) runways.Remove(r);
        }

        /// <summary>The flight (on the way) that holds a runway now, or null.</summary>
        public Flight OnRunway(Runway r) => r != null && runways.TryGetValue(r, out var f) && Active(f) ? f : null;

        // ------------------------------------------------------------------ one way at a time

        /// <summary>
        /// Another flight using a taxiway section on f's route (from..to metres along it) the other way: on it, or
        /// cleared onto it (its tail not yet off it). Starting (a departure about to leave its stand): also one going
        /// the same way close by on the section it joins.
        /// </summary>
        internal Flight RouteBlocker(Flight f, float from, float to, bool starting = false) => RouteBlocker(f, f.Route, from, to, starting, false);

        /// <summary>RouteBlocker for a route f could take. `whole`: against all the others still have to come along,
        /// not only as far as they're cleared.</summary>
        Flight RouteBlocker(Flight f, TaxiRoute route, float from, float to, bool starting, bool whole)
        {
            foreach (var g in flights)
                if (g != f && Blocks(g, f, route, from, to, starting, whole)) return g;
            return null;
        }

        /// <summary>Does g use a section of f's route (from..to metres along it) the other way (RouteBlocker)?</summary>
        bool Blocks(Flight g, Flight f, TaxiRoute route, float from, float to, bool starting, bool whole)
        {
            if (g.Finished || g.Route == null) return false;
            float committed = g.CommittedTo;
            if (committed < 0f) return false;
            var mine = route.traversals;
            float margin = (f.type.Length + g.type.Length) * 0.5f + 10f;
            for (int i = 0; i < mine.Count; i++)
            {
                var t = mine[i];
                if (t.DEnd <= from || t.dStart >= to) continue;
                float lo = Mathf.Min(t.uFrom, t.uTo), hi = Mathf.Max(t.uFrom, t.uTo);
                bool here = true;
                foreach (var o in g.Route.traversals)
                {
                    if (g.TailAlong >= o.DEnd) continue;
                    // the section it's on (or about to be: still leaving its stand) counts though it's cleared
                    // no further than its start
                    bool used = whole || here || o.dStart < committed || o.dStart <= g.Along;
                    here = false;
                    if (o.edge != t.edge || !used) continue;
                    bool against = o.dir != t.dir;
                    if (!against && !(starting && i == 0)) continue;
                    float olo = Mathf.Min(o.uFrom, o.uTo), ohi = Mathf.Max(o.uFrom, o.uTo);
                    // the part of it still ahead of g
                    if (g.Along > o.dStart)
                    {
                        float u = o.U(g.TailAlong);
                        if (o.dir > 0) olo = Mathf.Max(olo, u);
                        else ohi = Mathf.Min(ohi, u);
                    }
                    if (ohi + margin < lo || olo - margin > hi) continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// What keeps a departure from starting (pushing back or taxiing out of its stand): someone using its sections
        /// the other way, or about to (its first section close by, the same way), or waiting longer to (GiveWay); or
        /// an arrival on final within InboundTime that will need them the other way: it goes first, unless the
        /// departure has been waiting StartPatience already (the arrival then waits for it off the runway).
        /// </summary>
        internal string StartBlocker(Flight f)
        {
            var blocker = RouteBlocker(f, 0f, float.MaxValue, true) ?? GiveWay(f, 0f);
            if (blocker == null)
            {
                string vehicle = PushVehicle(f) ?? OncomingVehicle(f, 0f);
                if (vehicle != null) return $"waiting for {vehicle} to pass";
                string aircraft = PushAircraft(f);
                if (aircraft != null) return $"waiting for {aircraft} to pass";
            }
            if (blocker == null && (f.queuedSince < 0f || Clock - f.queuedSince < StartPatience))
                foreach (var g in flights)
                    if (g != f && g.kind == FlightKind.Arrival && g.Phase == FlightPhase.Approach && g.TimeToTouchdown <= InboundTime &&
                        Opposed(f.Route, 0f, g.Route, 0f, 20f))
                    {
                        blocker = g;
                        break;
                    }
            if (blocker == null)
            {
                f.queuedSince = -1f;
                return null;
            }
            Queue(f, 0f);
            return $"waiting for {blocker.Name}";
        }

        /// <summary>f waits, from `at` metres along its way, for traffic the other way (first in, first out).</summary>
        void Queue(Flight f, float at)
        {
            if (f.queuedSince >= 0f && Mathf.Abs(f.queuedAt - at) < 1f) return;
            f.queuedSince = Clock;
            f.queuedAt = at;
        }

        /// <summary>
        /// Someone who has waited longer than f for traffic the other way to clear, who wants some of f's way (from
        /// `from` on) the other way, and whose way f isn't on already: f gives way to them, so the two directions take
        /// turns instead of one stream of aircraft shutting out the other. For YieldLimit at most.
        /// </summary>
        Flight GiveWay(Flight f, float from)
        {
            float mine = f.queuedSince >= 0f ? f.queuedSince : Clock;
            if (Clock - mine >= YieldLimit) return null;
            foreach (var g in flights)
            {
                if (g == f || g.Finished || g.Route == null || g.queuedSince < 0f || g.queuedSince >= mine) continue;
                if (g.AwaitingPush && !g.ReadyToStart) continue;
                if (!Opposed(f.Route, from, g.Route, g.queuedAt, 20f) || Blocks(f, g, g.Route, g.queuedAt, float.MaxValue, g.AwaitingPush, false)) continue;
                return g;
            }
            return null;
        }

        /// <summary>Do two routes use some stretch of a taxiway section (within `margin` metres) in opposite directions,
        /// each from so far along it on?</summary>
        static bool Opposed(TaxiRoute a, float aFrom, TaxiRoute b, float bFrom, float margin)
        {
            foreach (var t in a.traversals)
            {
                if (t.DEnd <= aFrom) continue;
                foreach (var o in b.traversals)
                {
                    if (o.DEnd <= bFrom || o.edge != t.edge || o.dir == t.dir) continue;
                    float lo = Mathf.Min(t.uFrom, t.uTo), hi = Mathf.Max(t.uFrom, t.uTo);
                    float olo = Mathf.Min(o.uFrom, o.uTo), ohi = Mathf.Max(o.uFrom, o.uTo);
                    if (ohi + margin >= lo && olo - margin <= hi) return true;
                }
            }
            return false;
        }

        /// <summary>For planning: someone has reserved this stretch of a section the other way (on their route ahead).</summary>
        internal Flight Conflict(Flight self, AircraftType type, TaxiwayEdge e, int dir, float u0, float u1)
        {
            foreach (var g in flights)
            {
                if (g == self || g.Finished || g.Route == null) continue;
                foreach (var o in g.Route.traversals)
                {
                    if (o.edge != e || o.dir == dir || g.TailAlong >= o.DEnd) continue;
                    float olo = Mathf.Min(o.uFrom, o.uTo), ohi = Mathf.Max(o.uFrom, o.uTo);
                    if (ohi < u0 - 10f || olo > u1 + 10f) continue;
                    return g;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ keeping apart on the ground

        /// <summary>Every aircraft on the ground, its outline (AirportMaintenance.Outline), once a step.</summary>
        List<(Aircraft.Aircraft who, bool parked, List<AirportMaintenance.Box> shape, Vector2 at)> Outlines()
        {
            if (outlines != null) return outlines;
            outlines = new List<(Aircraft.Aircraft, bool, List<AirportMaintenance.Box>, Vector2)>();
            var t = airport.transform;
            foreach (var a in Aircraft.Aircraft.All)
            {
                if (a == null || a.type == null) continue;
                var p = t.InverseTransformPoint(a.transform.position);
                if (p.y > 3f) continue;
                var d = Geo.XZ(t.InverseTransformDirection(a.transform.forward));
                if (d.sqrMagnitude < 1e-6f) continue;
                var f = FlightOf(a);
                bool parked = f == null && !a.Moving && airport.Maintenance.TowOf(a) == null;
                var at = new Vector2(p.x, p.z);
                outlines.Add((a, parked, AirportMaintenance.Outline(at, d.normalized, a.type, parked ? 0.5f : 1.5f), at));
            }
            return outlines;
        }

        /// <summary>
        /// How far along its route a flight can go before its outline touches another aircraft's, or its fuselage
        /// comes within VehicleGap of a vehicle (float.MaxValue if nothing is in the way within its stopping distance
        /// and a margin). Vehicles under a wing don't count (they're low, and give way to aircraft); nor do parked
        /// aircraft beside a stand it is leaving or turning into (the stands are laid out to keep them clear).
        /// </summary>
        internal float FreeAhead(Flight f, float along, float speed, out string who)
        {
            who = null;
            f.behind = null;
            waitingFor.Remove(f.aircraft);
            var path = f.Route.path;
            float look = speed * speed / (2f * Flight.TaxiBraking) + 10f + f.type.Length * 0.2f;
            float end = Mathf.Min(along + look, path.Length);
            var others = Outlines();
            var vehicles = airport.Maintenance.VehicleBoxes(f.aircraft);
            float reach = f.type.Length + 2f * look;
            for (float d = along + 1.5f; d <= end + 1e-3f; d += 2f)
            {
                path.Pose(d, out var p, out var facing, out _, out _);
                List<AirportMaintenance.Box> me = null;
                foreach (var (a, parked, shape, at) in others)
                {
                    if (a == f.aircraft) continue;
                    if (parked && (d >= f.Route.standFrom || d <= f.Route.standTo)) continue;
                    if ((at - p).sqrMagnitude > (reach + a.type.Length) * (reach + a.type.Length)) continue;
                        me ??= AirportMaintenance.Outline(p, facing, f.type, 0f);
                    foreach (var b in me)
                        foreach (var o in shape)
                            if (b.Overlaps(o))
                            {
                                who = a.name;
                                f.behind = FlightOf(a);
                                return d;
                            }
                }
                if (vehicles.Count == 0) continue;
                me ??= AirportMaintenance.Outline(p, facing, f.type, 0f);
                var nose = me[0];
                nose.c += nose.az * (VehicleGap * 0.5f);
                nose.hz += VehicleGap * 0.5f;
                foreach (var v in vehicles)
                {
                    if ((v.c - p).sqrMagnitude > reach * reach || !nose.Overlaps(v)) continue;
                    who = v.what;
                    waitingFor[f.aircraft] = v.team;
                    return d;
                }
            }
            return float.MaxValue;
        }

        /// <summary>
        /// For vehicles (AirportMaintenance keeps them off it): the track each taxiing aircraft's fuselage will sweep
        /// next, as far as it is cleared (up to TrackAhead), as boxes along its way with the way it goes there (zero:
        /// every way); and the vehicle that may drive on it anyway (the tug that pushed it back, getting off its way).
        /// An aircraft being pushed back sweeps the whole push.
        /// </summary>
        public List<(Aircraft.Aircraft who, VehicleTeam exempt, List<(AirportMaintenance.Box box, Vector2 way)> track)> Tracks()
        {
            var list = new List<(Aircraft.Aircraft, VehicleTeam, List<(AirportMaintenance.Box, Vector2)>)>();
            foreach (var f in flights)
            {
                if (f.Finished || f.Route == null || !f.OnGround) continue;
                var push = f.Pushing(out var tug);
                if (push != null)
                {
                    // the fuselage, and the tug under its nose
                    var swept = new List<(AirportMaintenance.Box, Vector2)>();
                    var tugBounds = new Bounds(new Vector3(0f, 1f, f.type.Wheelbase + PushTugLength * 0.5f),
                        new Vector3(PushTugWidth, 2f, PushTugLength));
                    for (float d = 0f; d <= push.Length + 2.9f; d += 3f)
                    {
                        push.Sample(Mathf.Min(d, push.Length), out var p, out var heading, out _, out _);
                        swept.Add((AirportMaintenance.Outline(p, heading, f.type, PushClearance)[0], Vector2.zero));
                        swept.Add((AirportMaintenance.Box.Of(p, heading, tugBounds, PushClearance), Vector2.zero));
                    }
                    list.Add((f.aircraft, tug, swept));
                    continue;
                }
                if (f.Phase != FlightPhase.Taxi && f.Phase != FlightPhase.Landing && f.Phase != FlightPhase.StartingEngines) continue;
                float end = Mathf.Min(f.CommittedTo, f.Along + TrackAhead, f.Route.path.Length);
                if (end <= f.Along + 1f) continue;
                var boxes = new List<(AirportMaintenance.Box, Vector2)>();
                for (float d = f.Along; d <= end; d += 3f)
                {
                    f.Route.path.Pose(d, out var p, out var facing, out _, out _);
                    boxes.Add((AirportMaintenance.Outline(p, facing, f.type, TrackClearance)[0], facing));
                }
                list.Add((f.aircraft, f.PushTug, boxes));
            }
            return list;
        }

        /// <summary>Is a taxiing aircraft stopped for this vehicle (it goes on, off their tracks too)?</summary>
        public bool Blocking(VehicleTeam team) => team != null && (waitingFor.ContainsValue(team) || heldFor.ContainsValue(team));

        /// <summary>A vehicle on the move (not the tug that's pushing it or pushed it back) in the track f's fuselage
        /// would sweep over the next OncomingLook metres from `from`, coming the other way or across: it goes first (it
        /// couldn't stop clear of the track once f is on it). Null if there's none.</summary>
        string OncomingVehicle(Flight f, float from)
        {
            heldFor.Remove(f.aircraft);
            var vehicles = airport.Maintenance.VehicleBoxes(f.aircraft);
            if (vehicles.Count == 0) return null;
            var path = f.Route.path;
            float end = Mathf.Min(from + OncomingLook, path.Length);
            foreach (var v in vehicles)
            {
                if (v.team == f.PushTug || v.team.Velocity(v.index) < 0.3f) continue;
                for (float d = from; d <= end; d += 3f)
                {
                    path.Pose(d, out var p, out var facing, out _, out _);
                    if ((v.c - p).sqrMagnitude > 900f || Vector2.Dot(v.az, facing) > 0.7f) continue;
                    if (!AirportMaintenance.Outline(p, facing, f.type, TrackClearance)[0].Overlaps(v)) continue;
                    // it goes on, under the wings, clear of the fuselage (WaitsFor)
                    heldFor[f.aircraft] = v.team;
                    return v.what;
                }
            }
            return null;
        }

        /// <summary>A flight's aircraft stopped on its way (at a hold, behind another, starting engines): vehicles go
        /// by under its wings, keeping clear of the fuselage.</summary>
        public bool Holding(Aircraft.Aircraft a)
        {
            var f = a != null ? FlightOf(a) : null;
            return f != null && f.Speed < 0.3f && (f.Phase == FlightPhase.Taxi || f.Phase == FlightPhase.StartingEngines);
        }

        /// <summary>Another aircraft on the ground where its push-back would sweep (its fuselage, 3 m clear), or
        /// taxiing into it within the next 60 m, or null.</summary>
        string PushAircraft(Flight f)
        {
            var push = f.Route.pushBack;
            if (push == null) return null;
            var swept = new List<AirportMaintenance.Box>();
            for (float d = 0f; d <= push.Length + 2.9f; d += 3f)
            {
                push.Sample(Mathf.Min(d, push.Length), out var p, out var heading, out _, out _);
                swept.Add(AirportMaintenance.Outline(p, heading, f.type, 3f)[0]);
            }
            foreach (var (a, parked, shape, pos) in Outlines())
            {
                if (a == f.aircraft || parked) continue;
                var g = FlightOf(a);
                if (g == null) continue;
                if (shape.Exists(o => swept.Exists(w => w.Overlaps(o)))) return a.name;
                if (!g.Taxiing || g.Route == null) continue;
                for (float d = g.Along; d <= Mathf.Min(g.Along + 60f, g.Route.path.Length); d += 5f)
                {
                    g.Route.path.Pose(d, out var q, out var facing, out _, out _);
                    var at = AirportMaintenance.Outline(q, facing, g.type, 0f)[0];
                    if (swept.Exists(w => w.Overlaps(at))) return a.name;
                }
            }
            return null;
        }

        /// <summary>A vehicle (not its own tug) on the ground its push-back would sweep (Tracks), or null: it's pushed
        /// once that has gone (it wouldn't get out of the way of the push).</summary>
        string PushVehicle(Flight f)
        {
            var push = f.Route.pushBack;
            if (push == null) return null;
            var tug = f.tow != null ? f.tow.Tug : null;
            var vehicles = airport.Maintenance.VehicleBoxes(f.aircraft);
            if (vehicles.Count == 0) return null;
            var tugBounds = new Bounds(new Vector3(0f, 1f, f.type.Wheelbase + PushTugLength * 0.5f), new Vector3(PushTugWidth, 2f, PushTugLength));
            foreach (var v in vehicles)
            {
                if (v.team == tug) continue;
                for (float d = 0f; d <= push.Length + 2.9f; d += 3f)
                {
                    push.Sample(Mathf.Min(d, push.Length), out var p, out var heading, out _, out _);
                    if ((v.c - p).sqrMagnitude > 1600f) continue;
                    if (AirportMaintenance.Outline(p, heading, f.type, PushClearance)[0].Overlaps(v) ||
                        AirportMaintenance.Box.Of(p, heading, tugBounds, PushClearance).Overlaps(v)) return v.what;
                }
            }
            return null;
        }

        /// <summary>Is this aircraft stopped for this vehicle: taxiing, or waiting to taxi until the tug that pushed it
        /// back is off its way?</summary>
        public bool WaitsFor(Aircraft.Aircraft a, VehicleTeam team)
        {
            if (a == null || team == null) return false;
            var f = FlightOf(a);
            if (f == null) return false;
            return (waitingFor.TryGetValue(a, out var t) && t == team) || (heldFor.TryGetValue(a, out t) && t == team) || f.WaitsForTug(team);
        }

        // ------------------------------------------------------------------ the way changing under it

        /// <summary>Wear on each taxiway section a flight has passed.</summary>
        internal void Passed(Flight f)
        {
            passed.TryGetValue(f, out int n);
            var list = f.Route.traversals;
            var net = airport.Taxiways;
            while (n < list.Count && f.TailAlong >= list[n].DEnd)
            {
                if (net.edges.Contains(list[n].edge)) net.AddMovement(list[n].edge, f.type);
                n++;
            }
            passed[f] = n;
        }

        /// <summary>Are the sections still ahead of it still there, and those it hasn't reached yet open (not closed
        /// for work, snow or long grass)? Before it leaves its stand, every one.</summary>
        internal bool RouteValid(Flight f)
        {
            var net = airport.Taxiways;
            float reached = f.AwaitingPush ? float.MinValue : f.Along + 1f;
            foreach (var t in f.Route.traversals)
            {
                if (t.DEnd <= f.Along) continue;
                if (!net.edges.Contains(t.edge)) return false;
                if (t.dStart > reached && TaxiwayNetwork.Closed(t.edge)) return false;
            }
            return true;
        }

        /// <summary>Is this aircraft on a flight, taxiing (or landing, starting engines on its way)?</summary>
        public bool Taxiing(Aircraft.Aircraft a)
        {
            var f = a != null ? FlightOf(a) : null;
            return f != null && (f.Taxiing || f.Phase == FlightPhase.StartingEngines);
        }

        /// <summary>A departure that hasn't left yet: its way planned again from where it stands (its way closed).</summary>
        internal bool ReplanDeparture(Flight f, out string problem)
        {
            var route = PlanDeparture(f.aircraft, f, out var start, out problem);
            if (route == null) return false;
            ReleaseAll(f);
            f.Route = route;
            f.start = start;
            f.queuedSince = -1f;
            return true;
        }

        /// <summary>
        /// f is stuck behind g, which waits at a hold: in case it waits for what f was cleared for ahead (the runway, a
        /// junction), f gives back the clearances it hasn't reached yet and asks again when it gets there.
        /// </summary>
        internal void GiveBack(Flight f)
        {
            foreach (var h in f.Route.holds)
            {
                if (!h.cleared || h.released || h.at <= f.Along + 0.5f) continue;
                h.cleared = false;
                if (h.kind == HoldKind.Junction && junctions.TryGetValue(h.node, out var j) && j == f) junctions.Remove(h.node);
                if (h.kind == HoldKind.Runway && runways.TryGetValue(h.runway, out var r) && r == f &&
                    !(f.kind == FlightKind.Arrival && h.runway == f.Route.runway && f.Along < f.Route.runwayFree))
                    runways.Remove(h.runway);
            }
        }

        /// <summary>Plan again from where it is on its section (after the layout changed under it).</summary>
        internal bool Replan(Flight f, out string problem)
        {
            problem = "its way has gone";
            var net = airport.Taxiways;
            Traversal? on = null;
            foreach (var t in f.Route.traversals)
                if (f.Along >= t.dStart - 1f && f.Along <= t.DEnd + 1f && net.edges.Contains(t.edge)) on = t;
            if (on == null) return false;
            var tr = on.Value;
            f.Route.path.Pose(f.Along, out var gear, out var facing, out _, out _);
            var nose = gear + facing * Mathf.Max(f.type.Wheelbase, 0.3f);
            var start = new TaxiPlanner.Start
            {
                edge = tr.edge, dir = tr.dir, u = tr.U(f.Along), nose = nose, heading = facing, gear = gear,
                landingDecel = f.start != null ? f.start.landingDecel : 1.5f,
            };
            var planner = new TaxiPlanner(airport, f.type, this, f);
            TaxiRoute route;
            if (f.kind == FlightKind.Departure)
                route = planner.Departure(new List<TaxiPlanner.Start> { start }, Choices(f.type, true, out _), out problem, out _);
            else
            {
                var stands = FreeStands(f.type, f);
                if (f.Route.stand != null && !stands.Contains(f.Route.stand)) stands.Add(f.Route.stand);
                route = planner.Arrival(f.Route.runway, f.Route.fromStart, new List<TaxiPlanner.Start> { start }, stands, out problem, out _);
                if (route != null)
                {
                    route.runwayFree = -1f;
                    route.runway = f.Route.runway;
                }
            }
            if (route == null) return false;
            ReleaseAll(f);
            passed.Remove(f);
            f.queuedSince = -1f;
            f.Route = route;
            f.Along = 0f;
            if (f.Phase == FlightPhase.Landing) f.Phase = FlightPhase.Taxi;
            return true;
        }
    }
}
