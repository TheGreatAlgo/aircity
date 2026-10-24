using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    /// <summary>The depots and their teams: taxiway runs turned on the pavement, vehicles parked inside with doors
    /// that roll up, several depots, keeping clear of aircraft, verges, service roads.</summary>
    public class MaintenanceTests
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

        Airport NewAirport()
        {
            AirfieldSetup.CreateMaterials();
            var go = new GameObject("Test airport");
            go.SetActive(false);
            var a = go.AddComponent<Airport>();
            AirfieldSetup.AssignMaterials(a);
            a.groundSize = 0f;
            go.SetActive(true);
            spawned.Add(go);
            return a;
        }

        /// <summary>A depot without visuals (the teams are simulated), doors at `front` facing `outward`.</summary>
        static MaintenanceDepot AddDepot(Airport airport, Vector2 front, Vector2 outward, int ploughs, int mowers)
        {
            var d = airport.AddDepot(front, outward);
            d.SetAssets(VehicleSetup.Type("SnowPlough"), VehicleSetup.Type("Mower"), VehicleSetup.Type("Depot"), null, null, null);
            Assert.IsNotNull(d.ploughType, "SnowPlough.vehicle not imported");
            d.autoDispatch = false;
            d.SetFleet(ploughs, mowers);
            return d;
        }

        /// <summary>Run the airport in 2-second game steps until done() or the time runs out; returns the time taken.</summary>
        static float RunUntil(Airport airport, System.Func<bool> done, float maxSeconds, System.Action each = null)
        {
            float t = 0f;
            while (t < maxSeconds && !done())
            {
                airport.Simulate(2f);
                each?.Invoke();
                t += 2f;
            }
            return t;
        }

        static AircraftType Type(string model) => AircraftSetup.AllTypes().First(x => x.model == model);

        [Test]
        public void StripWork_OneCentredPassWhenTheTeamIsAsWideAsTheStrip()
        {
            // three ploughs (15 m) on a 15 m taxiway: one pass, lanes at +5, 0 and -5
            var w = new StripWork(WorkKind.Plough, 400f, 7.5f, 4f, 5f, 3, false);
            Assert.IsTrue(w.Centred);
            Assert.AreEqual(1, w.Passes);
            Assert.AreEqual(5f, w.LeadOffset(0), 1e-4f, "the lead on the right, the others in echelon to its left");
            w.Advance(0, 200f);
            Assert.IsTrue(w.Worked(190f, 6f), "lead lane");
            Assert.IsFalse(w.Worked(190f, 1f), "the second plough is 16 m behind");
            Assert.IsTrue(w.Worked(180f, 1f));
            Assert.IsTrue(w.Worked(160f, -6f), "the third, 32 m behind");
            Assert.IsFalse(w.Worked(170f, -6f));
            Assert.AreEqual(105f, w.ShaderState.w, 1e-4f, "lane width + the centred flag");
            var back = new StripWork(WorkKind.Plough, 400f, 7.5f, 4f, 5f, 3, true);
            Assert.AreEqual(-5f, back.LeadOffset(0), 1e-4f, "mirrored from the far end");
            back.Advance(0, 200f);
            Assert.IsTrue(back.Worked(210f, -6f) && !back.Worked(190f, -6f));
            Assert.IsFalse(new StripWork(WorkKind.Plough, 400f, 7.5f, 4f, 5f, 2, false).Centred, "two are narrower");
        }

        [Test]
        public void Path_FacingTurnsSmoothlyRoundATurnAndThroughACusp()
        {
            // a 10 m quarter turn, then back the other way round a cusp
            var path = new VehiclePath();
            path.Start(Vector2.zero, 5f);
            path.LineTo(new Vector2(0f, 20f), 5f);
            path.ArcAround(new Vector2(10f, 20f), -Mathf.PI / 2f, 5f);
            path.LineTo(new Vector2(30f, 30f), 5f);
            path.LineTo(new Vector2(20f, 30f), 2f, reverse: true);
            path.Pose(0f, out var last, out _);
            for (float d = 0.1f; d <= path.Length; d += 0.1f)
            {
                path.Pose(d, out var facing, out float curvature);
                Assert.Less(Vector2.Angle(last, facing), 1f, $"no click at {d:0.0} m (a 1.5 m chord turns 8°)");
                last = facing;
                if (d > 22f && d < 34f)
                    Assert.AreEqual(-0.1f, curvature, 0.01f, $"steady steering round the turn at {d:0.0} m");
            }
            Assert.AreEqual(0f, Vector2.Angle(Vector2.right, last), 0.01f, "still facing along the road, backing up");
        }

        /// <summary>A runway, a parallel taxiway joined to it at both ends by bends, a spur from the runway down to
        /// a T on the parallel, and a service road from a depot to the parallel.</summary>
        (Airport airport, Runway runway, MaintenanceDepot depot) TaxiwayAirport(int ploughs)
        {
            var airport = NewAirport();
            var r = airport.AddRunway(new Vector3(-1000f, 0f, 0f), new Vector3(1000f, 0f, 0f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0f, z), on);
            int w = Node(-700f, 0f, r), a = Node(-700f, -200f), b = Node(-200f, -200f), c = Node(200f, -200f), d = Node(700f, -200f), e = Node(700f, 0f, r);
            foreach (var (x, y) in new[] { (w, a), (a, b), (b, c), (c, d), (d, e) }) net.Connect(x, y, 15f, PavementSurface.Asphalt);
            int t = net.SplitEdge(net.edges.First(x => x.a == b && x.b == c), new Vector3(0f, 0f, -200f));
            net.Connect(Node(0f, 0f, r), t, 15f, PavementSurface.Asphalt);
            int p = net.SplitEdge(net.edges.First(x => x.a == a && x.b == b), new Vector3(-300f, 0f, -200f));
            net.Connect(Node(-300f, -312f), p, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, true);
            // a pad in front of the depot, where the road ends
            airport.aprons.Add(new ApronArea { centre = new Vector3(-300f, 0f, -312f), size = new Vector2(56f, 36f), surface = PavementSurface.Concrete });
            airport.BuildGround();
            net.Rebuild();
            var depot = AddDepot(airport, new Vector2(-300f, -330f), Vector2.up, ploughs, 0);
            return (airport, r, depot);
        }

        [Test]
        public void RemovingWhatATeamIsWorking_SendsItHome()
        {
            var (airport, r, depot) = TaxiwayAirport(2);
            var net = airport.Taxiways;
            r.SetSnow(4f);
            Assert.IsTrue(airport.Maintenance.Plough(r));
            var team = depot.Teams[0];
            RunUntil(airport, () => team.state == VehicleTeam.State.Working, 3600f);
            Assert.AreEqual(VehicleTeam.State.Working, team.state, "ploughing the runway");
            airport.RemoveRunway(r);
            RunUntil(airport, () => team.Job == null, 10f);
            Assert.IsNull(team.Job, "gives the job up");
            Assert.AreEqual(VehicleTeam.State.Returning, team.state);
            RunUntil(airport, () => team.state == VehicleTeam.State.Parked, 3600f);
            Assert.AreEqual(VehicleTeam.State.Parked, team.state, "and drives home");

            // the same for a taxiway run, removed while they are on their way to it
            foreach (var e in net.edges) e.snow = 4f;
            var spur = net.edges.First(e => !e.road && net.At(e.a).x == 0f && net.At(e.b).x == 0f);
            Assert.IsTrue(airport.Maintenance.Plough(spur));
            RunUntil(airport, () => team.state == VehicleTeam.State.Driving, 600f);
            Assert.IsTrue(team.Job != null && team.Job.Contains(spur), "on its way to the spur");
            net.RemoveEdges(team.Job.edges.ToList());
            net.Rebuild();
            // once they're all out of the yard
            RunUntil(airport, () => team.Job == null, 300f);
            Assert.IsNull(team.Job, "gives it up");
            RunUntil(airport, () => team.state == VehicleTeam.State.Parked, 3600f);
            Assert.AreEqual(VehicleTeam.State.Parked, team.state, "home again");
        }

        [Test]
        public void Taxiways_PloughedInRunsTurningRoundOnThePavement()
        {
            var (airport, r, depot) = TaxiwayAirport(1);
            var net = airport.Taxiways;
            foreach (var e in net.edges) e.snow = 4f;
            airport.Maintenance.PloughTaxiways();
            var jobs = airport.Maintenance.Queue;
            Assert.AreEqual(3, jobs.Count, "three runs: the parallel taxiway (round its bends), the spur and the road");
            Assert.AreEqual(7, jobs.Max(j => j.edges.Count), "the parallel: entry, three sections split by the spur and the road, exit");

            var team = depot.Teams[0];
            var pavement = airport.Pavement;
            int samples = 0, off = 0, reversing = 0;
            MaintenanceJob seen = null;
            float took = RunUntil(airport, () => net.edges.All(e => e.snow < 0.5f) && team.state == VehicleTeam.State.Parked, 6f * 3600f, () =>
            {
                if (team.Job == null || team.Job == seen) return;
                seen = team.Job;
                var (from, to) = team.WorkSpan;
                var poses = team.PlannedPoses(0, 2f, from, to);
                int o = VehicleManeuver.OffPavement(poses, team.type, pavement);
                Debug.Log($"{seen}: {seen.work.Passes} passes, {team.ReversingTurns} turns with reversing, {o} of {poses.Count * 6} footprint points off the pavement");
                samples += poses.Count * 6;
                off += o;
                reversing += team.ReversingTurns;
            });
            Debug.Log($"one plough, all taxiways and the road: {took / 60f:0} min");
            Assert.IsTrue(net.edges.All(e => e.snow < 0.5f), "all ploughed");
            Assert.AreEqual(VehicleTeam.State.Parked, team.state, "home again");
            Assert.Greater(reversing, 0, "the spur's T end is too tight for a U-turn: the plough backs into the parallel taxiway");
            Assert.Less(off, samples / 200, "a plough's footprint stays on the pavement through the passes and the turns between them");
        }

        [Test]
        public void Depot_VehiclesLiveInsideAndTheDoorsRollUp()
        {
            var (airport, _, depot) = TaxiwayAirport(2);
            var type = depot.ploughType;
            Assert.Greater(depot.DoorCount, 1);
            for (int k = 0; k < 2; k++)
            {
                var rel = depot.Bay(k, type) - depot.Front;
                Assert.Less(Vector2.Dot(rel, depot.Outward) + type.bounds.max.z, 0f, $"plough {k} parks inside, its blade behind the doors");
            }
            Assert.AreEqual(0f, depot.DoorOpenAmount(0), "doors shut");
            var net = airport.Taxiways;
            foreach (var e in net.edges) e.snow = 4f;
            Assert.IsTrue(depot.Plough(net.edges.First(e => !e.road && e.a != e.b && net.Node(e.a).runway != null)));

            var team = depot.Teams[0];
            float Along(Vector2 p) => Vector2.Dot(p - depot.Front, depot.Outward);
            int through = 0, opened = 0;
            float minGap = float.MaxValue;
            RunUntil(airport, () => team.state == VehicleTeam.State.Parked && airport.Maintenance.Queue.Count == 0, 3f * 3600f, () =>
            {
                for (int i = 0; i < team.size; i++)
                {
                    float z = Along(team.PositionOf(i));
                    if (depot.DoorOpenAmount(i) > 0.5f) opened++;
                    // the vehicle is in its doorway: from its rear axle at the door (going out) to its tail clear of it
                    if (!team.ParkedInBay(i) && z > -type.bounds.max.z && z < -type.bounds.min.z)
                    {
                        through++;
                        Assert.GreaterOrEqual(depot.DoorOpenAmount(i), 0.95f, $"plough {i} drives through door {i} only when it's up");
                    }
                }
                if (team.Job != null) minGap = Mathf.Min(minGap, Vector2.Distance(team.PositionOf(0), team.PositionOf(1)));
            });
            Assert.AreEqual(VehicleTeam.State.Parked, team.state, team.Describe());
            Assert.Greater(through, 4, "both ploughs went out and came back through their doors");
            Assert.Greater(opened, 0);
            airport.Simulate(10f);
            Assert.AreEqual(0f, depot.DoorOpenAmount(0), "shut again behind them");
            Assert.AreEqual(0f, depot.DoorOpenAmount(1));
            Assert.Greater(minGap, 6f, "the team keeps its distance");
        }

        [Test]
        public void Depots_JobsGoToTheNearestFreeTeam()
        {
            var airport = NewAirport();
            var west = airport.AddRunway(new Vector3(-2400f, 0f, 0f), new Vector3(-1200f, 0f, 0f), PavementSurface.Asphalt, 30f);
            var east = airport.AddRunway(new Vector3(1200f, 0f, 0f), new Vector3(2400f, 0f, 0f), PavementSurface.Asphalt, 30f);
            var net = airport.Taxiways;
            int wn = net.AddNode(new Vector3(-1800f, 0f, 0f), west), en = net.AddNode(new Vector3(1800f, 0f, 0f), east);
            int w0 = net.AddNode(new Vector3(-1800f, 0f, -200f)), e0 = net.AddNode(new Vector3(1800f, 0f, -200f));
            net.Connect(wn, w0, 15f, PavementSurface.Asphalt);
            net.Connect(w0, e0, 15f, PavementSurface.Asphalt);
            net.Connect(e0, en, 15f, PavementSurface.Asphalt);
            net.Rebuild();
            var dw = AddDepot(airport, new Vector2(-1800f, -320f), Vector2.up, 2, 0);
            var de = AddDepot(airport, new Vector2(1800f, -320f), Vector2.up, 2, 0);
            Assert.AreEqual(2, airport.Depots.Count);
            west.SetSnow(3f);
            east.SetSnow(3f);
            Assert.IsTrue(airport.Maintenance.Plough(east));
            Assert.IsTrue(airport.Maintenance.Plough(west));
            airport.Simulate(2f);
            Assert.AreEqual(west, dw.Teams[0].Job?.runway, "the west depot's team takes the west runway");
            Assert.AreEqual(east, de.Teams[0].Job?.runway, "and the east one the east");
            float took = RunUntil(airport, () => west.snow == 0f && east.snow == 0f, 2f * 3600f);
            Debug.Log($"two depots, two runways at once: {took / 60f:0} min");
            Assert.AreEqual(0f, west.snow);
            Assert.AreEqual(0f, east.snow);
        }

        /// <summary>A runway, and two ways to it from a depot: straight up a taxiway, or round by the east.</summary>
        (Airport airport, Runway runway, MaintenanceDepot depot, int direct) DetourAirport()
        {
            var airport = NewAirport();
            var r = airport.AddRunway(new Vector3(-800f, 0f, 0f), new Vector3(800f, 0f, 0f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int s = net.AddNode(new Vector3(0f, 0f, -380f));
            int direct = net.AddNode(new Vector3(0f, 0f, 0f), r);
            net.Connect(s, direct, 15f, PavementSurface.Asphalt);
            int e0 = net.AddNode(new Vector3(400f, 0f, -380f)), e1 = net.AddNode(new Vector3(400f, 0f, 0f), r);
            net.Connect(s, e0, 15f, PavementSurface.Asphalt);
            net.Connect(e0, e1, 15f, PavementSurface.Asphalt);
            net.Rebuild();
            var depot = AddDepot(airport, new Vector2(0f, -410f), Vector2.up, 1, 0);
            r.SetSnow(3f);
            return (airport, r, depot, direct);
        }

        AirCity.Aircraft.Aircraft Park(string model, Vector3 at, float heading, float speed = 0f)
        {
            var prefab = AircraftSetup.Prefab(Type(model));
            Assert.IsNotNull(prefab, $"{model} prefab");
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(0f, heading, 0f));
            spawned.Add(go);
            var motion = go.GetComponent<AircraftGroundMotion>();
            if (motion != null) motion.groundSpeed = speed;
            return go.GetComponent<AirCity.Aircraft.Aircraft>();
        }

        static AirportMaintenance.Box BoxOf(AirCity.Aircraft.Aircraft a) =>
            AirportMaintenance.Box.Of(new Vector2(a.transform.position.x, a.transform.position.z),
                new Vector2(a.transform.forward.x, a.transform.forward.z).normalized, a.type.bounds, 0f);

        [Test]
        public void Avoidance_RoutesRoundParkedAircraftAndWaitsForMovingOnes()
        {
            // a jet parked across the direct taxiway: the plough goes round by the east
            var (airport, r, depot, _) = DetourAirport();
            var jet = Park("Narrowbody", new Vector3(0f, 0f, -200f), 90f);
            var team = depot.Teams[0];
            Assert.IsTrue(r.StartPloughing());
            airport.Simulate(2f);
            var box = BoxOf(jet);
            var poses = team.PlannedPoses(0, 2f);
            var touch = poses.FindIndex(p => AirportMaintenance.Box.Of(p.p, p.facing, team.type.bounds, 0f).Overlaps(box));
            Assert.AreEqual(-1, touch, $"the route keeps clear of the parked jet (pose {touch} of {poses.Count}: " +
                                       $"{(touch >= 0 ? poses[touch].p.ToString() : "")}; {team.Describe()})");
            RunUntil(airport, () => r.snow == 0f && team.state == VehicleTeam.State.Parked, 2f * 3600f, () =>
                Assert.IsFalse(AirportMaintenance.Box.Of(team.PositionOf(0), team.FacingOf(0), team.type.bounds, 0f).Overlaps(box), "never touches it"));
            Assert.AreEqual(0f, r.snow, "ploughed all the same");

            // a jet taxiing (it doesn't give way): the plough stops short and goes on once it has gone
            Object.DestroyImmediate(jet.gameObject);
            r.SetSnow(3f);
            var moving = Park("Narrowbody", new Vector3(0f, 0f, -200f), 90f, 5f);
            Assert.IsTrue(moving.Moving);
            // only the direct way now
            var net = airport.Taxiways;
            net.RemoveEdge(net.edges.First(e => net.At(e.a).x > 300f || net.At(e.b).x > 300f).id);
            net.Rebuild();
            Assert.IsTrue(r.StartPloughing());
            box = BoxOf(moving);
            float waited = 0f;
            RunUntil(airport, () => waited > 120f, 1800f, () =>
            {
                Assert.IsFalse(AirportMaintenance.Box.Of(team.PositionOf(0), team.FacingOf(0), team.type.bounds, 0f).Overlaps(box),
                    "stops short of the moving jet");
                if (team.SpeedOf(0) < 0.05f && team.state == VehicleTeam.State.Driving && team.PositionOf(0).y > -330f) waited += 2f;
            });
            Assert.Greater(waited, 100f, "waits for it");
            StringAssert.Contains("waiting for aircraft", team.Status);
            Object.DestroyImmediate(moving.gameObject);
            RunUntil(airport, () => r.snow == 0f, 3600f);
            Assert.AreEqual(0f, r.snow, "and gets on with it");
        }

        [Test]
        public void Verges_GrowAndAreMownInStripes()
        {
            var airport = NewAirport();
            var r = airport.AddRunway(new Vector3(-400f, 0f, 0f), new Vector3(400f, 0f, 0f), PavementSurface.Asphalt, 23f);
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(-200f, 0f, -150f));
            net.Connect(a, net.AddNode(new Vector3(-200f, 0f, 0f), r), 15f, PavementSurface.Asphalt, true);
            net.Connect(a, net.AddNode(new Vector3(-200f, 0f, -260f)), RunwayRules.RoadWidth, PavementSurface.Asphalt, true, true);
            net.Rebuild();
            Assert.IsFalse(net.edges.First(e => e.road).lit, "roads are never lit");
            Assert.IsFalse(net.edges.First(e => e.road).HasVerges, "roads have no verges");
            AirfieldTests.CheckMesh(r.VergeMesh, "runway verges");
            AirfieldTests.CheckMesh(net.VergeMesh, "taxiway verges");
            var depot = AddDepot(airport, new Vector2(-200f, -272f), Vector2.up, 0, 1);

            airport.Simulate(24f * 3600f * 10f);
            Assert.That(r.vergeGrass, Is.InRange(14.5f, 15.5f), "a centimetre a day");
            Assert.AreEqual(r.vergeGrass, r.VergeMesh.colors[0].b, 0.01f, "the verges show it");
            depot.autoDispatch = true;
            airport.Simulate(31f);
            var team = depot.Teams[0];
            Assert.IsNotNull(team.Job, "long verges call the mower out");
            Assert.IsTrue(team.Job.verge);
            float took = RunUntil(airport, () => r.vergeGrass < 6f && net.edges.Where(e => e.HasVerges).All(e => e.vergeGrass < 6f), 6f * 3600f);
            Debug.Log($"one mower, 800 m runway verges and a taxiway's: {took / 60f:0} min");
            Assert.AreEqual(RunwayRules.CutGrass, r.vergeGrass, 0.01f);
            Assert.Less(r.VergeMesh.uv4Value().w, 0f, "the mowing stripes stay on the verges");
            Assert.IsTrue(r.Check(Type("Cessna172")).Ok, "mowing verges never closes the runway");
        }

        [Test]
        public void Roads_CarryVehiclesNotAircraft()
        {
            var airport = NewAirport();
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(0f, 0f, 0f)), b = net.AddNode(new Vector3(400f, 0f, 0f));
            int c = net.AddNode(new Vector3(0f, 0f, -30f)), d = net.AddNode(new Vector3(400f, 0f, -30f));
            net.Connect(a, b, 15f, PavementSurface.Asphalt, true);
            foreach (var (x, y) in new[] { (a, c), (c, d), (d, b) }) net.Connect(x, y, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, true);
            // a lit taxiway crossing the road
            net.Connect(net.AddNode(new Vector3(200f, 0f, -120f)), net.AddNode(new Vector3(200f, 0f, -60f)), 15f, PavementSurface.Asphalt, true);
            int crossing = net.AddNode(new Vector3(200f, 0f, 40f));
            net.Connect(net.nodes.First(n => Vector3.Distance(n.position, new Vector3(200f, 0f, -60f)) < 0.5f).id, crossing, 15f, PavementSurface.Asphalt, true);
            net.Rebuild();
            Assert.IsTrue(net.nodes.Any(n => Vector3.Distance(n.position, new Vector3(200f, 0f, -30f)) < 0.5f), "the taxiway splits the road where it crosses");

            var cessna = Type("Cessna172");
            Assert.IsTrue(net.FindVehicleRoute(a, b).Contains(c), "vehicles prefer the road");
            CollectionAssert.AreEqual(new[] { a, net.nodes.First(n => Vector3.Distance(n.position, new Vector3(200f, 0f, 0f)) < 0.5f).id, b },
                net.FindRoute(a, b, cessna), "aircraft keep to the taxiway");
            net.RemoveEdge(net.edges.First(e => !e.road && (e.a == a || e.b == a)).id);
            net.Rebuild();
            Assert.IsNull(net.FindRoute(a, b, cessna), "and never take the road");
            Assert.IsNotNull(net.FindVehicleRoute(a, b));

            var colors = net.PaintMesh.colors;
            Assert.Greater(colors.Count(col => col.b > 0.8f), 100, "white road markings: edge lines, centre dashes, give-way lines");
            AirfieldTests.CheckMesh(net.SurfaceMesh, "taxiway and road surface");
        }
    }
}
