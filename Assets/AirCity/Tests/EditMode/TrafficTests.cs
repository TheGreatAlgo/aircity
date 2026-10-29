using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Vehicles;
using AirCity.Traffic;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    /// <summary>Flights: the taxi route to the runway in use and from the runway to a free stand, holding for the
    /// runway, junctions and each other, the takeoff and the landing.</summary>
    public class TrafficTests
    {
        readonly List<Object> spawned = new List<Object>();
        float glow;

        [SetUp]
        public void Setup()
        {
            glow = AircraftLights.Glow;
            AircraftLights.Glow = 0f;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
            AircraftLights.Glow = glow;
        }

        static AircraftType Type(string model)
        {
            var t = AircraftSetup.AllTypes().FirstOrDefault(x => x.model == model);
            Assert.IsNotNull(t, $"{model}.aircraft not imported");
            return t;
        }

        /// <summary>
        /// A 3 km runway along z = 0 (09/27), a parallel taxiway at z = -200 with entries at both ends and 90° exits
        /// at x = ±500, a depot with tugs on a service road south-west, and push-back stands (code C) on the taxiway's
        /// south side at x = -40.5, 0 and 40.5. With `deadEnd`, also a dead-end taxilane south off the taxiway at
        /// x = 250 with three more push-back stands on its east side (z = -270, -310.5, -351): one way in and out.
        /// </summary>
        (Airport airport, Runway runway, List<StandLayout> stands) TestAirport(float windFrom = 270f, bool deadEnd = false)
        {
            AirfieldSetup.CreateMaterials();
            var go = new GameObject("Test airport");
            go.SetActive(false);
            var airport = go.AddComponent<Airport>();
            AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 0f;
            airport.windFrom = windFrom;
            go.SetActive(true);
            spawned.Add(go);

            var r = airport.AddRunway(new Vector3(-1500f, 0f, 0f), new Vector3(1500f, 0f, 0f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0f, z), on);
            int w = Node(-1450f, 0f, r), a = Node(-1450f, -200f), m1 = Node(-500f, -200f), m2 = Node(500f, -200f),
                b = Node(1450f, -200f), e = Node(1450f, 0f, r);
            foreach (var (x, y) in new[] { (w, a), (a, m1), (m1, m2), (m2, b), (b, e) }) net.Connect(x, y, 15f, PavementSurface.Asphalt);
            net.Connect(Node(-500f, 0f, r), m1, 15f, PavementSurface.Asphalt);
            net.Connect(Node(500f, 0f, r), m2, 15f, PavementSurface.Asphalt);
            int p = net.SplitEdge(net.edges.First(x => (x.a == m1 && x.b == m2) || (x.a == m2 && x.b == m1)), new Vector3(-300f, 0f, -200f));
            net.Connect(Node(-300f, -312f), p, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            airport.aprons.Add(ApronArea.Rect(new Vector2(-300f, -312f), new Vector2(56f, 36f), 0f, PavementSurface.Concrete));
            var stands = new List<StandLayout>();
            foreach (float x in new[] { -40.5f, 0f, 40.5f })
            {
                var st = net.AddStand(StandKind.Parking, new Vector3(x, 0f, -200f), 180f, SizeCode.C, PavementSurface.Concrete);
                Assert.IsNotNull(st);
                stands.Add(net.Layout(st));
            }
            if (deadEnd)
            {
                net.TryNearestEdge(new Vector3(250f, 0f, -200f), 1f, out var along, out _);
                net.Connect(net.SplitEdge(along, new Vector3(250f, 0f, -200f)), Node(250f, -460f), 15f, PavementSurface.Asphalt);
                foreach (float z in new[] { -270f, -310.5f, -351f })
                {
                    var st = net.AddStand(StandKind.Parking, new Vector3(250f, 0f, z), 90f, SizeCode.C, PavementSurface.Concrete);
                    Assert.IsNotNull(st);
                    stands.Add(net.Layout(st));
                }
            }
            airport.BuildGround();
            net.Rebuild();
            r.Rebuild();

            var depot = airport.AddDepot(new Vector2(-300f, -330f), Vector2.up);
            depot.SetAssets(VehicleSetup.Type("SnowPlough"), VehicleSetup.Type("Mower"), VehicleSetup.Type("Depot"), null, null, null);
            depot.SetTugAssets(VehicleSetup.Type("TugSmall"), VehicleSetup.Type("TugLarge"), null, null);
            depot.autoDispatch = false;
            depot.SetFleet(0, 0);
            depot.SetTugs(2, 0);
            return (airport, r, stands);
        }

        AirCity.Aircraft.Aircraft Place(string model, Vector2 at, Vector2 facing)
        {
            var prefab = AircraftSetup.Prefab(Type(model));
            Assert.IsNotNull(prefab, $"{model} prefab");
            var go = Object.Instantiate(prefab, new Vector3(at.x, 0f, at.y), Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y)));
            spawned.Add(go);
            return go.GetComponent<AirCity.Aircraft.Aircraft>();
        }

        AirCity.Aircraft.Aircraft ParkOn(StandLayout l, string model, string name)
        {
            var a = Place(model, l.ParkPosition(Type(model)), l.nose);
            a.name = name;
            return a;
        }

        /// <summary>A brand-new aircraft out of sight, for an arrival.</summary>
        AirCity.Aircraft.Aircraft Spare(string model, string name)
        {
            var a = Place(model, new Vector2(0f, -5000f), Vector2.up);
            a.name = name;
            return a;
        }

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        static float RunUntil(Airport airport, System.Func<bool> done, float maxSeconds, System.Action each = null)
        {
            float t = 0f;
            while (t < maxSeconds && !done())
            {
                airport.Simulate(1f);
                each?.Invoke();
                t += 1f;
            }
            return t;
        }

        /// <summary>What the vehicles out are doing, and where (for failure messages).</summary>
        static string Teams(Airport airport) => string.Join("; ", airport.Depots.SelectMany(d => d.Teams).Where(t => t.Active)
            .Select(t => $"{t.name}: {t.Status} at {t.LeadPosition}, near " + string.Join(", ", AirCity.Aircraft.Aircraft.All
                .Where(a => a != null && Vector2.Distance(XZ(a.transform.position), t.LeadPosition) < 80f)
                .Select(a => $"{a.name} {XZ(a.transform.position)} facing {XZ(a.transform.forward)} {(a.Moving ? "moving" : "still")} y {a.transform.position.y:0}"))));

        /// <summary>No two aircraft on the ground touch.</summary>
        static void AssertApart()
        {
            var ground = AirCity.Aircraft.Aircraft.All.Where(x => x != null && x.gameObject.activeInHierarchy && x.transform.position.y < 1f).ToList();
            for (int i = 0; i < ground.Count; i++)
                for (int k = i + 1; k < ground.Count; k++)
                {
                    var p = AirportMaintenance.Outline(XZ(ground[i].transform.position), XZ(ground[i].transform.forward).normalized, ground[i].type, 0f);
                    var q = AirportMaintenance.Outline(XZ(ground[k].transform.position), XZ(ground[k].transform.forward).normalized, ground[k].type, 0f);
                    Assert.IsFalse(p.Any(x => q.Any(y => x.Overlaps(y))), $"{ground[i].name} and {ground[k].name} touch");
                }
        }

        /// <summary>The nose wheel never leaves the pavement, never turns sharper than its gear can, and never turns
        /// back on itself.</summary>
        static void AssertDriveable(Airport airport, TaxiPath path, AircraftType type, string what, float from = 0f)
        {
            var pave = airport.Pavement;
            Vector2 lastHeading = default;
            // no tighter than the gear turns (degrees of heading per metre of the nose wheel's way)
            float maxTurn = Mathf.Rad2Deg * Mathf.Sin(type.maxSteeringAngle * Mathf.Deg2Rad) / type.Wheelbase + 1f;
            for (float d = from; d <= path.Length; d += 1f)
            {
                path.Pose(d, out var origin, out var heading, out float steer, out _);
                var nose = origin + heading * type.Wheelbase;
                Assert.IsTrue(pave.Contains(nose), $"{what}: nose wheel on the pavement at {d:0} m ({nose})");
                Assert.LessOrEqual(Mathf.Abs(steer), type.maxSteeringAngle + 1f, $"{what}: steering at {d:0} m");
                if (d > from) Assert.Less(Vector2.Angle(lastHeading, heading), maxTurn, $"{what}: smooth at {d:0} m");
                lastHeading = heading;
            }
        }

        [Test]
        public void Wind_PicksWhichWayEachRunwayIsUsed()
        {
            Assert.IsTrue(AirTraffic.FromStart(90f, 90f));
            Assert.IsFalse(AirTraffic.FromStart(90f, 270f));
            Assert.IsTrue(AirTraffic.FromStart(90f, 30f), "60° off the wind: still into it");
            Assert.IsFalse(AirTraffic.FromStart(90f, 200f));

            var (airport, runway, stands) = TestAirport(270f);
            Assert.AreEqual("27", airport.Traffic.InUse(runway));
            var jet = ParkOn(stands[1], "Narrowbody", "Jet");
            var west = airport.Traffic.PreviewDeparture(jet, out string problem);
            Assert.IsNotNull(west, problem);
            Assert.AreSame(runway, west.runway);
            Assert.IsFalse(west.fromStart);
            Assert.Less(west.path.EndHeading.x, -0.995f, "lined up facing west on 27");
            Assert.Greater(west.path.End.x, 1300f, "at the east end: the full length");
            Assert.AreEqual(0f, west.path.End.y, 0.5f, "on the centreline");

            airport.windFrom = 80f;
            Assert.AreEqual("09", airport.Traffic.InUse(runway));
            var east = airport.Traffic.PreviewDeparture(jet, out problem);
            Assert.IsNotNull(east, problem);
            Assert.IsTrue(east.fromStart);
            Assert.Greater(east.path.EndHeading.x, 0.995f, "facing east on 09");
            Assert.Less(east.path.End.x, -1300f, "at the west end");
            Assert.AreEqual(0, airport.Traffic.Flights.Count, "a preview reserves nothing");
        }

        [Test]
        public void Departure_RouteFollowsTheTaxiwaysAndHoldsShortOfTheRunway()
        {
            var (airport, runway, stands) = TestAirport();
            var type = Type("Narrowbody");
            var jet = ParkOn(stands[1], "Narrowbody", "Jet");
            var route = airport.Traffic.PreviewDeparture(jet, out string problem);
            Assert.IsNotNull(route, problem);
            // pushed back facing east, the way to 27: the route starts where the push-back ends
            var push = PushBackPath.Plan(airport.Taxiways, type, stands[1].ParkPosition(type), stands[1].nose, false, out problem);
            Assert.IsNotNull(push, problem);
            Assert.Less(Vector2.Distance(route.path.GearAt(0), push.End), 0.01f);
            Assert.Greater(push.endFacing.x, 0.99f);
            AssertDriveable(airport, route.path, type, "departure");

            // holds: the junction at x = 500 (three ways), then short of 27 at the runway-holding position
            Assert.AreEqual(2, route.holds.Count, string.Join(", ", route.holds.Select(h => $"{h.kind} at {h.at:0}")));
            Assert.AreEqual(HoldKind.Junction, route.holds[0].kind);
            var hold = route.holds[1];
            Assert.AreEqual(HoldKind.Runway, hold.kind);
            Assert.IsTrue(hold.takeoff);
            route.path.Pose(hold.at, out var origin, out var heading, out _, out _);
            float noseTip = (origin + heading * type.bounds.max.z).y;
            Assert.That(noseTip, Is.InRange(-RunwayRules.HoldingDistance(45f) - 3f, -RunwayRules.HoldingDistance(45f)),
                "nose short of the holding position, 75 m from the centreline");
            // the sections it uses, in order, one way each
            Assert.Greater(route.traversals.Count, 2);
            for (int i = 1; i < route.traversals.Count; i++)
                Assert.AreNotSame(route.traversals[i - 1].edge, route.traversals[i].edge);
            float len = 0f;
            foreach (var t in route.traversals) len += t.Span;
            Assert.That(route.path.Length, Is.InRange(len, len + 400f));
        }

        [Test]
        public void Departure_PushedBackTaxisHoldsAndTakesOff()
        {
            var (airport, runway, stands) = TestAirport();
            var jet = ParkOn(stands[1], "Narrowbody", "Jet");
            var flight = airport.Traffic.Depart(jet, out string problem);
            Assert.IsNotNull(flight, problem);
            Assert.AreSame(flight, airport.Traffic.FlightOf(jet));
            Assert.IsNull(airport.Traffic.Depart(jet, out _), "once");
            float wear = runway.wear;
            var phases = new HashSet<FlightPhase>();
            bool towed = false;
            float t = RunUntil(airport, () => flight.Phase >= FlightPhase.Climb, 1800f, () =>
            {
                phases.Add(flight.Phase);
                towed |= airport.Maintenance.TowOf(jet) != null;
            });
            Assert.Less(t, 1800f, $"{flight.Status} after {t:0} s");
            Assert.IsTrue(towed, "a tug pushed it back");
            foreach (var p in new[] { FlightPhase.PushBack, FlightPhase.StartingEngines, FlightPhase.Taxi, FlightPhase.LinedUp, FlightPhase.TakeoffRoll })
                Assert.IsTrue(phases.Contains(p), $"went through {p}");
            Assert.Greater(runway.wear, wear, "a takeoff wears the runway");
            Assert.AreSame(flight, airport.Traffic.OnRunway(runway), "holds the runway until it has climbed away");
            RunUntil(airport, () => airport.Traffic.OnRunway(runway) == null, 120f);
            Assert.Greater(jet.transform.position.y, 50f, "gave the runway back once well up");
            RunUntil(airport, () => flight.Finished, 600f);
            Assert.AreEqual(FlightPhase.Gone, flight.Phase);
            Assert.IsFalse(jet.gameObject.activeSelf, "out of sight");
        }

        [Test]
        public void Arrival_LandsTakesAnExitAndParksOnAFreeStand()
        {
            var (airport, runway, stands) = TestAirport();
            var type = Type("Narrowbody");
            ParkOn(stands[1], "Narrowbody", "Parked");
            var jet = Spare("Narrowbody", "Arrival");
            var flight = airport.Traffic.Arrive(jet, out string problem);
            Assert.IsNotNull(flight, problem);
            var route = flight.Route;
            Assert.AreEqual(FlightPhase.Approach, flight.Phase);
            Assert.AreSame(runway, route.runway);
            Assert.IsFalse(route.fromStart, "landing on 27, into the wind");
            Assert.IsNotNull(route.stand);
            Assert.AreNotSame(stands[1].stand, route.stand.stand, "not the taken stand");
            // on the glide path, east of the runway
            Assert.Greater(jet.transform.position.x, 1500f);
            float glide = Mathf.Atan2(jet.transform.position.y, jet.transform.position.x - (1500f - route.touchdownS)) * Mathf.Rad2Deg;
            Assert.AreEqual(3f, glide, 0.2f, "3° glide path");
            AssertDriveable(airport, route.path, type, "arrival");
            Assert.Greater(route.runwayFree, 0f);

            bool landed = false;
            float t = RunUntil(airport, () => flight.Finished, 1500f, () => landed |= flight.Phase == FlightPhase.Landing);
            Assert.Less(t, 1500f, $"{flight.Status} after {t:0} s");
            Assert.IsTrue(landed);
            Assert.AreEqual(FlightPhase.Parked, flight.Phase);
            var l = route.stand;
            Assert.Less(Vector2.Distance(XZ(jet.transform.position), l.ParkPosition(type)), 1.5f, "on the stand's stop bar");
            Assert.Less(Vector2.Angle(XZ(jet.transform.forward), l.nose), 5f, "nose in");
            Assert.IsNull(airport.Traffic.FlightOf(jet));
            Assert.IsFalse(jet.Moving);
        }

        [Test]
        public void Arrival_NeedsAFreeStand()
        {
            var (airport, _, stands) = TestAirport();
            for (int i = 0; i < stands.Count; i++) ParkOn(stands[i], "Narrowbody", "Parked " + i);
            var jet = Spare("Narrowbody", "Arrival");
            Assert.IsNull(airport.Traffic.Arrive(jet, out string problem));
            StringAssert.Contains("stand", problem);
        }

        [Test]
        public void Stand_NoSmallAircraft_TurnsLightAircraftAway()
        {
            var (airport, _, stands) = TestAirport();
            var net = airport.Taxiways;
            var cessna = Type("Cessna172");
            var jet = Type("Narrowbody");
            Assert.AreEqual(StandProblem.None, net.CheckStand(stands[0].stand, cessna), "a code C stand takes a Cessna");
            foreach (var l in stands) l.stand.noSmallAircraft = true;
            Assert.AreEqual(StandProblem.NoSmallAircraft, net.CheckStand(stands[0].stand, cessna));
            Assert.AreEqual(StandProblem.None, net.CheckStand(stands[0].stand, jet), "jets still");
            Assert.IsFalse(airport.Traffic.CanArrive(cessna, out string problem));
            StringAssert.Contains("stand", problem);
            Assert.IsTrue(airport.Traffic.CanArrive(jet, out problem), problem);
            stands[2].stand.noSmallAircraft = false;
            var f = airport.Traffic.Arrive(Spare("Cessna172", "Cessna"), out problem);
            Assert.IsNotNull(f, problem);
            Assert.AreSame(stands[2].stand, f.Route.stand.stand, "the one stand that takes it");
        }

        [Test]
        public void Arrival_DoesNotTakeAStandADepartureIsStillLeaving()
        {
            var (airport, _, stands) = TestAirport();
            var type = Type("Narrowbody");
            ParkOn(stands[0], "Narrowbody", "Left");
            ParkOn(stands[2], "Narrowbody", "Right");
            var dep = airport.Traffic.Depart(ParkOn(stands[1], "Narrowbody", "Leaving"), out string problem);
            Assert.IsNotNull(dep, problem);
            airport.Simulate(1f);
            Assert.IsFalse(airport.Traffic.CanArrive(type, out problem), "its stand is taken while it's still in front of it");
            float t = RunUntil(airport, () => airport.Traffic.CanArrive(type, out _), 1200f, AssertApart);
            Assert.Less(t, 1200f, dep.Status);
            Assert.AreEqual(FlightPhase.Taxi, dep.Phase, "free once it has taxied off its front");
            var arr = airport.Traffic.Arrive(Spare("Narrowbody", "Arrival"), out problem);
            Assert.IsNotNull(arr, problem);
            Assert.AreSame(stands[1].stand, arr.Route.stand.stand);
            t = RunUntil(airport, () => dep.Phase >= FlightPhase.Climb && arr.Finished, 2400f, AssertApart);
            Assert.Less(t, 2400f, $"{dep}; {arr}");
            Assert.AreEqual(FlightPhase.Parked, arr.Phase);
        }

        [Test]
        public void DeadEndTaxilane_DepartureAndArrivalTakeTurns([Values(0f, 60f, 150f)] float arriveAfter)
        {
            // the stands on the taxiway full, so the arrival goes up the dead-end taxilane the departure comes out of
            var (airport, _, stands) = TestAirport(270f, true);
            for (int i = 0; i < 3; i++) ParkOn(stands[i], "Narrowbody", "Parked " + i);
            var dep = airport.Traffic.Depart(ParkOn(stands[5], "Narrowbody", "Leaving"), out string problem);
            Assert.IsNotNull(dep, problem);
            RunUntil(airport, () => false, arriveAfter, AssertApart);
            var arr = airport.Traffic.Arrive(Spare("Narrowbody", "Arrival"), out problem);
            Assert.IsNotNull(arr, problem);
            Assert.IsTrue(arr.Route.stand.stand == stands[3].stand || arr.Route.stand.stand == stands[4].stand, "up the taxilane");
            float t = RunUntil(airport, () => dep.Phase >= FlightPhase.Climb && arr.Finished, 2400f, AssertApart);
            Assert.Less(t, 2400f, $"{dep}; {arr}; " + Teams(airport));
            Assert.AreEqual(FlightPhase.Parked, arr.Phase);
            Assert.LessOrEqual(arr.GoArounds, 1);
        }

        [Test]
        public void SandboxAirport_EveryParkedJetHasAWayOutAndArrivalsAWayIn([Values(270f, 90f)] float wind)
        {
            var airport = AirfieldSetup.BuildSandboxAirport();
            spawned.Add(airport.gameObject);
            airport.windFrom = wind;
            var jets = airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>();
            Assert.AreEqual(4, jets.Length);
            foreach (var a in jets)
            {
                var route = airport.Traffic.PreviewDeparture(a, out string problem);
                Assert.IsNotNull(route, $"{a.name}: {problem}");
                Assert.AreEqual("09/27", route.runway.Designation, $"{a.name}: the jets' runway");
                Assert.AreEqual(wind == 270f ? "27" : "09", route.RunwayName);
                AssertDriveable(airport, route.path, a.type, a.name, Mathf.Max(route.standTo, 0f));
                Assert.IsTrue(route.holds.Exists(h => h.takeoff), $"{a.name}: holds short before lining up");
                Assert.IsFalse(route.backTrack, $"{a.name}: a jet doesn't turn round on the runway when it can enter at the end");
            }
            // light aircraft on the GA stands: pushed back by hand, or taxiing out. The grass taxiway to them has grown
            // long, which closes it: no way out until it's mowed
            var cessna = Type("Cessna172");
            var ga = airport.Taxiways.Layouts().Where(l => !l.stand.DeIcing && l.Code == 'A' && l.edge != null).ToList();
            Assert.Greater(ga.Count, 2);
            var cutOff = ParkOn(ga.First(l => l.edge.surface == PavementSurface.Grass), "Cessna172", "Cut off");
            Assert.IsNull(airport.Traffic.PreviewDeparture(cutOff, out string closed));
            StringAssert.Contains("no way", closed);
            Object.DestroyImmediate(cutOff.gameObject);
            // (and the grass strip, their only runway: with its grass long a Cessna needs more than its 700 m)
            foreach (var e in airport.Taxiways.edges)
                if (e.surface == PavementSurface.Grass) e.grass = RunwayRules.CutGrass;
            foreach (var r in airport.Runways)
                if (r.surface == PavementSurface.Grass) r.SetGrass(RunwayRules.CutGrass);
            foreach (var l in ga)
            {
                var a = ParkOn(l, "Cessna172", "Cessna on " + l.stand.Name);
                var route = airport.Traffic.PreviewDeparture(a, out string problem);
                Assert.IsNotNull(route, $"{a.name}: {problem}");
                AssertDriveable(airport, route.path, cessna, a.name, Mathf.Max(route.standTo, 0f));
                Object.DestroyImmediate(a.gameObject);
            }
            foreach (var model in new[] { "Narrowbody", "Cessna172" })
            {
                var a = Spare(model, model);
                var flight = airport.Traffic.Arrive(a, out string problem);
                Assert.IsNotNull(flight, $"{model}: {problem}");
                Assert.Greater(Vector2.Dot(XZ(a.transform.forward), new Vector2(Mathf.Sin(wind * Mathf.Deg2Rad), Mathf.Cos(wind * Mathf.Deg2Rad))), 0f,
                    $"{model} lands into the wind");
                AssertDriveable(airport, flight.Route.path, a.type, model + " arrival", 0f);
            }
        }

        [Test]
        public void SandboxAirport_BusyHourClearsWithoutGridlock()
        {
            var airport = AirfieldSetup.BuildSandboxAirport();
            spawned.Add(airport.gameObject);
            var flights = new List<Flight>();
            foreach (var a in airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>())
            {
                var f = airport.Traffic.Depart(a, out string problem);
                Assert.IsNotNull(f, $"{a.name}: {problem}");
                flights.Add(f);
            }
            for (int i = 0; i < 2; i++)
            {
                var f = airport.Traffic.Arrive(Spare("Narrowbody", "Arrival " + i), out string problem);
                Assert.IsNotNull(f, problem);
                flights.Add(f);
            }
            Assert.AreNotSame(flights[4].Route.stand.stand, flights[5].Route.stand.stand, "each to its own stand");
            float maxWait = 0f;
            float t = RunUntil(airport, () => flights.All(f => f.Finished), 3600f, () =>
            {
                var ground = AirCity.Aircraft.Aircraft.All.Where(x => x != null && x.transform.position.y < 1f).ToList();
                for (int i = 0; i < ground.Count; i++)
                    for (int k = i + 1; k < ground.Count; k++)
                    {
                        var p = AirportMaintenance.Outline(XZ(ground[i].transform.position), XZ(ground[i].transform.forward).normalized, ground[i].type, 0f);
                        var q = AirportMaintenance.Outline(XZ(ground[k].transform.position), XZ(ground[k].transform.forward).normalized, ground[k].type, 0f);
                        Assert.IsFalse(p.Any(x => q.Any(y => x.Overlaps(y))), $"{ground[i].name} and {ground[k].name} touch");
                    }
                foreach (var f in flights) maxWait = Mathf.Max(maxWait, f.WaitedFor);
            });
            Assert.Less(t, 3600f, string.Join("; ", flights.Select(f => f.ToString())));
            Assert.AreEqual(4, flights.Count(f => f.Phase == FlightPhase.Gone));
            Assert.AreEqual(2, flights.Count(f => f.Phase == FlightPhase.Parked));
            // departures waiting for a tug don't hold up the arrivals, which land rather than circling
            Assert.LessOrEqual(flights.Sum(f => f.GoArounds), 2);
            Debug.Log($"busy hour: all done in {t:0} s, longest wait {maxWait:0} s, go-arounds {flights.Sum(f => f.GoArounds)}");
        }

        [Test]
        public void Traffic_HoldsForEachOtherAndNeverTouches()
        {
            var (airport, runway, stands) = TestAirport();
            var a = ParkOn(stands[0], "Narrowbody", "First");
            var b = ParkOn(stands[2], "Narrowbody", "Second");
            var first = airport.Traffic.Depart(a, out string problem);
            Assert.IsNotNull(first, problem);
            var second = airport.Traffic.Depart(b, out problem);
            Assert.IsNotNull(second, problem);
            var arrival = airport.Traffic.Arrive(Spare("Narrowbody", "Arrival"), out problem);
            Assert.IsNotNull(arrival, problem);
            var flights = new[] { first, second, arrival };
            var waits = new HashSet<string>();
            int onRunwayAtOnce = 0;
            float t = RunUntil(airport, () => flights.All(f => f.Finished || f.Phase >= FlightPhase.Climb && f.kind == FlightKind.Departure), 3600f, () =>
            {
                foreach (var f in flights)
                    if (f.Waiting != null) waits.Add(f.Waiting);
                // nobody touches anybody on the ground
                var ground = AirCity.Aircraft.Aircraft.All.Where(x => x != null && x.transform.position.y < 1f).ToList();
                for (int i = 0; i < ground.Count; i++)
                    for (int k = i + 1; k < ground.Count; k++)
                    {
                        var p = AirportMaintenance.Outline(XZ(ground[i].transform.position), XZ(ground[i].transform.forward).normalized, ground[i].type, 0f);
                        var q = AirportMaintenance.Outline(XZ(ground[k].transform.position), XZ(ground[k].transform.forward).normalized, ground[k].type, 0f);
                        Assert.IsFalse(p.Any(x => q.Any(y => x.Overlaps(y))), $"{ground[i].name} and {ground[k].name} touch");
                    }
                int on = 0;
                foreach (var f in flights)
                {
                    if (f.Finished || f.Route == null) continue;
                    var pos = f.aircraft.transform.position;
                    if (pos.y < 1f && runway.Contains(pos, -2f)) on++;
                }
                onRunwayAtOnce = Mathf.Max(onRunwayAtOnce, on);
            });
            Assert.Less(t, 3600f, string.Join("; ", flights.Select(f => f.ToString())));
            Assert.AreEqual(1, onRunwayAtOnce, "one at a time on the runway");
            Assert.AreEqual(FlightPhase.Parked, arrival.Phase);
            Assert.IsTrue(waits.Any(w => w.Contains("holding short") || w.Contains("behind") || w.Contains("waiting")),
                "someone waited: " + string.Join(", ", waits));
        }
    }
}
