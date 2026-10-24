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
    /// <summary>Runway rules, generated runway / taxiway geometry, taxi routing, and the depot's ploughs and mowers.</summary>
    static class MeshTestExtensions
    {
        /// <summary>The first vertex's uv4 (the work in progress), or zero.</summary>
        public static Vector4 uv4Value(this Mesh m)
        {
            var l = new List<Vector4>();
            m.GetUVs(4, l);
            return l.Count > 0 ? l[0] : Vector4.zero;
        }
    }

    public class AirfieldTests
    {
        readonly List<Object> spawned = new List<Object>();
        float glow;

        [SetUp]
        public void Setup() => glow = AircraftLights.Glow;

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

        /// <summary>A depot (no vehicle visuals: teams are simulated) at the origin facing north.</summary>
        static MaintenanceDepot AddDepot(Airport airport, Vector3 at, int ploughs, int mowers)
        {
            var go = new GameObject("Depot");
            go.SetActive(false);
            go.transform.SetParent(airport.transform, false);
            go.transform.localPosition = at;
            var d = go.AddComponent<MaintenanceDepot>();
            d.ploughType = VehicleSetup.Type("SnowPlough");
            d.mowerType = VehicleSetup.Type("Mower");
            d.buildingType = VehicleSetup.Type("Depot");
            Assert.IsNotNull(d.ploughType, "SnowPlough.vehicle not imported");
            Assert.IsNotNull(d.mowerType, "Mower.vehicle not imported");
            d.autoDispatch = false;
            d.SetFleet(ploughs, mowers);
            go.SetActive(true);
            return d;
        }

        /// <summary>Run the airport in 2-second game steps until done() or the time runs out; returns the time taken.</summary>
        static float RunUntil(Airport airport, System.Func<bool> done, float maxSeconds)
        {
            float t = 0f;
            while (t < maxSeconds && !done())
            {
                airport.Simulate(2f);
                t += 2f;
            }
            return t;
        }

        [Test]
        public void Performance_ComesFromTheAircraftData()
        {
            var cessna = Type("Cessna172");
            var jet = Type("Narrowbody");
            Assert.That(cessna.RunwayLength, Is.InRange(450f, 550f), "C172: ~500 m over a 50 ft obstacle");
            Assert.That(jet.RunwayLength, Is.InRange(1900f, 2300f), "A320: ~2,100 m takeoff field length");
            Assert.IsTrue(cessna.unpavedRunways);
            Assert.IsFalse(jet.unpavedRunways, "jets need pavement");
            Assert.Greater(jet.maxTakeoffMass, 60000f);
        }

        [Test]
        public void Designators_FollowTheHeading()
        {
            Assert.AreEqual(9, RunwayRules.Number(90f));
            Assert.AreEqual(27, RunwayRules.Number(270f));
            Assert.AreEqual(36, RunwayRules.Number(0f), "north is 36, never 0");
            Assert.AreEqual(36, RunwayRules.Number(357f));
            Assert.AreEqual("04", RunwayRules.Designator(4));

            var airport = NewAirport();
            var east = airport.AddRunway(new Vector3(0f, 0f, 0f), new Vector3(1500f, 0f, 0f), PavementSurface.Asphalt, 30f);
            Assert.AreEqual("09/27", east.Designation);
            var diag = airport.AddRunway(new Vector3(0f, 0f, -2000f), new Vector3(0f, 0f, -2000f) + new Vector3(-0.643f, 0f, 0.766f) * 1500f,
                PavementSurface.Asphalt, 30f);
            Assert.AreEqual("32/14", diag.Designation);
        }

        [Test]
        public void ParallelRunways_GetLeftAndRight()
        {
            var airport = NewAirport();
            // landing eastbound (09), north is on the left
            var north = airport.AddRunway(new Vector3(0f, 0f, 400f), new Vector3(2000f, 0f, 400f), PavementSurface.Concrete, 45f);
            // drawn the other way round: its start end is 27
            var south = airport.AddRunway(new Vector3(2000f, 0f, 0f), new Vector3(0f, 0f, 0f), PavementSurface.Concrete, 45f);
            Assert.AreEqual("09L/27R", north.Designation);
            Assert.AreEqual("27L/09R", south.Designation);
            var middle = airport.AddRunway(new Vector3(0f, 0f, 200f), new Vector3(2000f, 0f, 200f), PavementSurface.Concrete, 45f);
            Assert.AreEqual("09C/27C", middle.Designation);
            airport.RemoveRunway(middle);
            Assert.AreEqual("09L/27R", north.Designation);
        }

        [Test]
        public void Qualification_LengthSurfaceWidthWeatherNightWear()
        {
            var cessna = Type("Cessna172");
            var jet = Type("Narrowbody");
            RunwayCheck Check(AircraftType t, PavementSurface s, float length, float width, RunwayLighting l = RunwayLighting.None,
                float wear = 0f, SurfaceCondition c = SurfaceCondition.Dry, bool night = false, float snow = 0f, bool ploughing = false) =>
                RunwayRules.Check(t, s, length, width, l, wear, c, night, snow, ploughing);

            Assert.IsTrue(Check(cessna, PavementSurface.Dirt, 600f, 18f).Ok, "a C172 uses a 600 m strip");
            Assert.AreEqual(RunwayProblem.NeedsPavement, Check(jet, PavementSurface.Dirt, 3000f, 45f).problem);
            Assert.AreEqual(RunwayProblem.TooShort, Check(jet, PavementSurface.Concrete, 2000f, 45f).problem);
            Assert.IsTrue(Check(jet, PavementSurface.Concrete, 2500f, 45f).Ok);
            Assert.IsTrue(Check(jet, PavementSurface.Asphalt, 2500f, 30f).Ok, "code C needs 30 m");
            Assert.AreEqual(RunwayProblem.TooNarrow, Check(jet, PavementSurface.Asphalt, 2500f, 23f).problem);
            // weather: soft wet dirt needs 30% more; wet pavement lengthens the landing roll; a layer of snow
            // lengthens both; deep snow closes the runway until it is ploughed (dirt sooner)
            Assert.AreEqual(RunwayProblem.TooShort, Check(cessna, PavementSurface.Dirt, 600f, 18f, c: SurfaceCondition.Wet).problem);
            var wet = RunwayRules.LengthFactors(PavementSurface.Concrete, SurfaceCondition.Wet).Value;
            Assert.AreEqual(1f, wet.x, "rain doesn't lengthen the takeoff");
            Assert.AreEqual(1.15f, wet.y, 1e-4f, "...but it does the landing roll");
            Assert.Greater(RunwayRules.RequiredLength(jet, PavementSurface.Concrete, SurfaceCondition.Snow, 2f),
                RunwayRules.RequiredLength(jet, PavementSurface.Concrete, SurfaceCondition.Wet), "a layer of snow lengthens both");
            Assert.AreEqual(RunwayProblem.NeedsPloughing, Check(jet, PavementSurface.Concrete, 3500f, 45f, snow: 4f).problem);
            Assert.AreEqual(RunwayProblem.NeedsPloughing, Check(cessna, PavementSurface.Dirt, 2000f, 18f, snow: 1.5f).problem);
            Assert.IsTrue(Check(cessna, PavementSurface.Asphalt, 2000f, 18f, snow: 1.5f).Ok, "paved copes with a little snow");
            Assert.AreEqual(RunwayProblem.Ploughing, Check(jet, PavementSurface.Concrete, 3500f, 45f, ploughing: true).problem);
            // grass: longer runs than dirt when dry, long grass drags, too long closes it
            Assert.IsTrue(Check(cessna, PavementSurface.Grass, 650f, 18f).Ok, "a C172 uses a 650 m grass strip");
            Assert.AreEqual(RunwayProblem.NeedsPavement, Check(jet, PavementSurface.Grass, 3000f, 45f).problem);
            Assert.Greater(RunwayRules.RequiredLength(cessna, PavementSurface.Grass, SurfaceCondition.Dry, 0f, 20f),
                RunwayRules.RequiredLength(cessna, PavementSurface.Grass, SurfaceCondition.Dry), "long grass lengthens the takeoff");
            Assert.AreEqual(RunwayProblem.GrassTooLong,
                RunwayRules.Check(cessna, PavementSurface.Grass, 900f, 18f, RunwayLighting.None, 0f, SurfaceCondition.Dry, false, 0f, false, 30f).problem);
            Assert.AreEqual(RunwayProblem.Mowing,
                RunwayRules.Check(cessna, PavementSurface.Grass, 900f, 18f, RunwayLighting.None, 0f, SurfaceCondition.Dry, false, 0f, false, 8f, true).problem);
            // night needs lighting; worn out closes
            Assert.AreEqual(RunwayProblem.Unlit, Check(jet, PavementSurface.Concrete, 2500f, 45f, night: true).problem);
            Assert.IsTrue(Check(jet, PavementSurface.Concrete, 2500f, 45f, RunwayLighting.Basic, night: true).Ok);
            Assert.AreEqual(RunwayProblem.ClosedForRepair, Check(jet, PavementSurface.Concrete, 2500f, 45f, wear: 1f).problem);
        }

        [Test]
        public void Wear_RatesMatchTheDesignLives()
        {
            var cessna = Type("Cessna172");
            var jet = Type("Narrowbody");
            float Movements(PavementSurface s, AircraftType t, SurfaceCondition c = SurfaceCondition.Dry) =>
                1f / RunwayRules.WearPerMovement(s, t.maxTakeoffMass, c);
            Assert.That(Movements(PavementSurface.Asphalt, jet), Is.InRange(3400f, 4600f));
            Assert.That(Movements(PavementSurface.Concrete, jet), Is.InRange(10000f, 14000f));
            Assert.That(Movements(PavementSurface.Dirt, cessna), Is.InRange(500f, 700f));
            Assert.Less(Movements(PavementSurface.Dirt, cessna, SurfaceCondition.Wet), Movements(PavementSurface.Dirt, cessna) * 0.6f,
                "rain softens dirt");
            Assert.Greater(Movements(PavementSurface.Asphalt, cessna), 100000f, "light aircraft barely wear pavement");

            var airport = NewAirport();
            var r = airport.AddRunway(Vector3.zero, new Vector3(0f, 0f, 1200f), PavementSurface.Asphalt, 30f);
            for (int i = 0; i < 400; i++) r.AddMovement(jet);
            Assert.That(r.wear, Is.InRange(0.08f, 0.12f));
            r.Repair();
            Assert.AreEqual(0f, r.wear);
        }

        /// <summary>A runway with a taxiway each side of it joined to it, and a depot north-west of the taxiway.</summary>
        (Airport airport, Runway runway, int a, int b) SnowAirport(int ploughs)
        {
            var airport = NewAirport();
            var r = airport.AddRunway(new Vector3(-1250f, 0f, 0f), new Vector3(1250f, 0f, 0f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(-400f, 0f, -200f)), b = net.AddNode(new Vector3(400f, 0f, -200f));
            net.Connect(a, b, 15f, PavementSurface.Asphalt);
            net.Connect(a, net.AddNode(new Vector3(-400f, 0f, 0f), r), 15f, PavementSurface.Asphalt);
            net.Rebuild();
            AddDepot(airport, new Vector3(-400f, 0f, -320f), ploughs, 0);
            return (airport, r, a, b);
        }

        [Test]
        public void Snow_BuildsUpClosesAndIsPloughed()
        {
            var jet = Type("Narrowbody");
            var (airport, r, a, b) = SnowAirport(4);
            var net = airport.Taxiways;
            var depot = airport.Depot;
            Assert.IsTrue(r.Check(jet).Ok);

            airport.Condition = SurfaceCondition.Snow;
            airport.Simulate(1.5f * 3600f);   // an hour and a half of heavy snow
            Assert.That(r.snow, Is.InRange(4f, 5f));
            Assert.That(airport.groundSnow, Is.InRange(4f, 5f));
            Assert.AreEqual(RunwayProblem.NeedsPloughing, r.Check(jet).problem);
            Assert.IsNull(net.FindRoute(a, b, jet), "snowed-in taxiways are closed");
            var colors = r.SurfaceMesh.colors;
            Assert.AreEqual(r.snow, colors[0].g, 0.01f, "the surface shows the snow without a rebuild");

            // a team of four drives out and ploughs in echelon: two passes for 30 m each side of the centreline
            Assert.IsTrue(r.StartPloughing());
            Assert.AreEqual(1, depot.Teams.Count);
            airport.Simulate(2f);
            var team = depot.Teams[0];
            Assert.AreEqual(VehicleTeam.State.Driving, team.state);
            Assert.IsNotNull(r.Work);
            Assert.AreEqual(4, r.Work.Passes, "4 ploughs x 5 m lanes = 20 m bands: two each side of a 60 m paved width");
            float there = RunUntil(airport, () => r.IsPloughing, 600f);
            Assert.Less(there, 600f, "the team reaches the runway");
            Assert.AreEqual(RunwayProblem.Ploughing, r.Check(jet).problem);
            RunUntil(airport, () => r.Work != null && r.Work.Progress > 0.25f, 900f);
            var work = r.SurfaceMesh.uv4Value();
            Assert.Greater(work.z, 0f, "the work in progress reaches the shader (band width)");
            Assert.IsTrue(r.Work.Worked(r.Work.Front - r.Work.Direction(r.Work.Pass) * 5f, r.Work.LeadOffset(r.Work.Pass)),
                "cleared just behind the lead");

            float took = RunUntil(airport, () => r.Work == null, 3600f);
            Debug.Log($"four ploughs: {took / 60f:0} min on a 2.5 km x 60 m runway");
            Assert.Less(took, 1500f, "four ploughs clear a 2.5 km runway in under 25 minutes");
            Assert.That(r.snow, Is.LessThan(0.1f), "ploughed");
            Assert.IsTrue(r.Check(jet).Ok, "open again, still snowing");
            Assert.Greater(RunwayRules.LengthFactors(r.surface, airport.Condition, r.snow).Value.y, 1f, "slippery while it snows");

            depot.PloughTaxiways();
            RunUntil(airport, () => net.edges.All(e => e.snow < 0.5f), 3600f);
            CollectionAssert.AreEqual(new[] { a, b }, net.FindRoute(a, b, jet), "taxiways ploughed");
            RunUntil(airport, () => team.state == VehicleTeam.State.Parked, 1800f);
            Assert.AreEqual(VehicleTeam.State.Parked, team.state, "home again: " + team.Describe());

            airport.Condition = SurfaceCondition.Dry;
            airport.Simulate(18f * 3600f);     // what fell while they worked, at 0.5 cm an hour
            Assert.AreEqual(0f, airport.groundSnow, "melted");
        }

        [Test]
        public void Ploughing_OnePloughTakesLongerThanAnEchelon()
        {
            float Clear(int ploughs)
            {
                var (airport, r, _, _) = SnowAirport(ploughs);
                r.SetSnow(4f);
                Assert.IsTrue(r.StartPloughing());
                float t = RunUntil(airport, () => r.snow == 0f, 4f * 3600f);
                Assert.Less(t, 4f * 3600f, $"{ploughs} plough(s) finish");
                Assert.AreEqual(0f, r.snow);
                return t;
            }
            float one = Clear(1), four = Clear(4);
            Debug.Log($"2.5 km x 60 m runway: one plough {one / 60f:0} min, four in echelon {four / 60f:0} min");
            Assert.Greater(one, four * 2.4f, "one plough needs twelve passes, four need four");
        }

        [Test]
        public void StripWork_PassesFromTheCentreOutInEchelon()
        {
            // 45 m runway + 7.5 m shoulders: 30 m each side; two ploughs: 10 m bands, six passes
            var w = new StripWork(WorkKind.Plough, 2000f, 30f, 65f, 5f, 2, false);
            Assert.AreEqual(6, w.Passes);
            Assert.AreEqual(1, w.Direction(0));
            Assert.AreEqual(-1, w.Direction(1));
            Assert.AreEqual(-2.5f, w.LeadOffset(0), 1e-4f, "first pass: left of the centreline, going up the runway");
            Assert.AreEqual(2.5f, w.LeadOffset(1), 1e-4f, "then right of it, coming back");
            Assert.AreEqual(-12.5f, w.LeadOffset(2), 1e-4f, "then the next band out");
            w.Advance(0, 1000f);
            Assert.IsTrue(w.Worked(900f, -2f), "lead lane behind the lead");
            Assert.IsFalse(w.Worked(1100f, -2f), "ahead of it");
            Assert.IsFalse(w.Worked(990f, -7f), "the second plough is 16 m behind the lead");
            Assert.IsTrue(w.Worked(980f, -7f));
            Assert.IsFalse(w.Worked(500f, 3f), "the other side waits for pass 1");
            w.Advance(1, 1500f);
            Assert.IsTrue(w.Worked(500f, -2f), "pass 0 done");
            Assert.IsTrue(w.Worked(1600f, 3f), "pass 1 runs back down");
            Assert.IsFalse(w.Worked(1400f, 3f));
            w.Finish();
            Assert.IsTrue(w.Worked(10f, 29f));
            var v = w.ShaderState;
            Assert.AreEqual(6f, v.x);
            Assert.AreEqual(10f, v.z);
            Assert.AreEqual(5f, v.w);
            // mowing: neighbouring bands in opposite directions, for the stripes, and still a U-turn after each pass
            var mow = new StripWork(WorkKind.Mow, 600f, 13f, 10f, 4.4f, 1, false);
            Assert.AreEqual(6, mow.Passes);
            for (int k = 0; k < mow.Passes - 1; k++)
                Assert.AreEqual(-mow.Direction(k), mow.Direction(k + 1), "U-turn after each pass");
            int DirAt(float x) => Enumerable.Range(0, mow.Passes).Where(k => Mathf.Abs(mow.LeadOffset(k) - x) < 0.01f)
                .Select(mow.Direction).Single();
            Assert.AreEqual(-DirAt(-2.2f), DirAt(-6.6f), "neighbouring bands alternate");
            Assert.AreEqual(-DirAt(2.2f), DirAt(6.6f));
            Assert.AreEqual(-DirAt(-6.6f), DirAt(-11f));
            mow.Advance(2, 300f);
            Assert.IsTrue(mow.Worked(100f, -2f) && mow.Worked(500f, 2f), "both middle bands done");
            Assert.AreEqual(mow.Direction(2) > 0, mow.Worked(250f, mow.LeadOffset(2)));
            var reversed = new StripWork(WorkKind.Mow, 600f, 9f, 10f, 4.4f, 1, true);
            Assert.AreEqual(-1, reversed.Direction(0), "from the far end");
            Assert.Greater(reversed.LeadOffset(0), 0f, "left of the way it's going");
            Assert.Less(reversed.ShaderState.z, 0f);
            Assert.Less(reversed.ShaderState.w, 0f, "mowing");
        }

        [Test]
        public void GrassStrip_GrowsAndIsMowed()
        {
            var cessna = Type("Cessna172");
            var airport = NewAirport();
            var r = airport.AddRunway(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 700f), PavementSurface.Grass, 30f);
            var net = airport.Taxiways;
            int edge = net.AddNode(new Vector3(-150f, 0f, 100f));
            net.Connect(edge, net.AddNode(new Vector3(0f, 0f, 100f), r), 10.5f, PavementSurface.Grass);
            net.Rebuild();
            var depot = AddDepot(airport, new Vector3(-150f, 0f, 20f), 0, 1);
            CheckMesh(r.SurfaceMesh, "grass strip");
            Assert.IsTrue(r.PaintMesh == null || r.PaintMesh.vertexCount == 0, "grass strips have no paint");
            r.SetLighting(RunwayLighting.Basic);
            Assert.AreEqual(RunwayLighting.None, r.lighting, "grass can't be lit");
            Assert.IsTrue(r.Check(cessna).Ok);

            airport.Simulate(24f * 3600f * 21f);     // three weeks
            Assert.That(r.grass, Is.InRange(24f, 27f), "about a centimetre a day");
            Assert.AreEqual(RunwayProblem.GrassTooLong, r.Check(cessna).problem);
            Assert.AreEqual(r.grass, r.SurfaceMesh.colors[0].b, 0.01f, "the shader sees the grass length");

            Assert.IsTrue(r.StartMowing());
            float there = RunUntil(airport, () => r.IsMowing, 900f);
            Assert.Less(there, 900f, "the mower reaches the strip");
            Assert.AreEqual(RunwayProblem.Mowing, r.Check(cessna).problem);
            float took = RunUntil(airport, () => r.Work == null, 3f * 3600f);
            Debug.Log($"700 m x 30 m grass strip, one mower: {took / 60f:0} min");
            Assert.AreEqual(RunwayRules.CutGrass, r.grass, 0.01f);
            Assert.IsTrue(r.Check(cessna).Ok, "open again");
            Assert.Less(r.SurfaceMesh.uv4Value().w, 0f, "the mowing stripes stay in the shader");
        }

        [Test]
        public void Runway_BuildsSurfacePaintAndLights()
        {
            var airport = NewAirport();
            var r = airport.AddRunway(new Vector3(-1400f, 0f, 0f), new Vector3(1400f, 0f, 0f), PavementSurface.Concrete, 45f);
            CheckMesh(r.SurfaceMesh, "surface");
            CheckMesh(r.PaintMesh, "paint");
            Assert.AreEqual(0, r.LightCount, "no lighting bought");
            r.SetLighting(RunwayLighting.Basic);
            int basic = r.LightCount;
            r.SetLighting(RunwayLighting.Precision);
            int precision = r.LightCount;
            Debug.Log($"2,800 m precision runway: {r.SurfaceMesh.vertexCount} surface verts, {r.PaintMesh.vertexCount / 4} paint quads, " +
                      $"{basic} lights basic, {precision} precision");
            // edge lights every 60 m both sides, threshold / end rows, 2 x 4 PAPI
            Assert.That(basic, Is.InRange(2 * 47 + 2 * 15 + 8, 2 * 49 + 2 * 16 + 8));
            Assert.Greater(precision, basic + 2 * 24 * 5, "centreline, touchdown zone and approach lights");
            Assert.Less(precision, 2000, "one runway's lights stay cheap");

            var dirt = airport.AddRunway(new Vector3(0f, 0f, 500f), new Vector3(600f, 0f, 500f), PavementSurface.Dirt, 18f);
            dirt.SetLighting(RunwayLighting.Basic);
            Assert.AreEqual(RunwayLighting.None, dirt.lighting, "dirt can't be lit");
            CheckMesh(dirt.SurfaceMesh, "dirt surface");
            Assert.IsTrue(dirt.PaintMesh == null || dirt.PaintMesh.vertexCount == 0, "dirt strips have no paint");
        }

        [Test]
        public void Taxiways_SplitAtCrossingsAndJoinRunways()
        {
            var airport = NewAirport();
            var r = airport.AddRunway(new Vector3(-1000f, 0f, 300f), new Vector3(1000f, 0f, 300f), PavementSurface.Asphalt, 30f);
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(-500f, 0f, 0f)), b = net.AddNode(new Vector3(500f, 0f, 0f));
            net.Connect(a, b, 15f, PavementSurface.Concrete);
            // a north-south taxiway crossing it and the runway
            int c = net.AddNode(new Vector3(0f, 0f, -200f)), d = net.AddNode(new Vector3(0f, 0f, 500f));
            net.Connect(c, d, 15f, PavementSurface.Concrete, true);
            var cross = net.nodes.Single(n => Vector3.Distance(n.position, Vector3.zero) < 0.5f);
            Assert.AreEqual(4, net.EdgesAt(cross.id).Count(), "taxiways split where they cross");
            var onRunway = net.nodes.Single(n => n.runway == r);
            Assert.AreEqual(300f, onRunway.position.z, 0.01f, "a crossing node on the runway centreline");
            Assert.AreEqual(2 + 2 + 1, net.edges.Count);
            net.Rebuild();
            CheckMesh(net.SurfaceMesh, "taxiway surface");
            CheckMesh(net.PaintMesh, "taxiway paint");
            Assert.Greater(net.LightCount, 20, "the lit taxiway has edge lights");
        }

        [Test]
        public void Bends_MeetTheirStraightsSquare()
        {
            // a 70° bend (not a right angle, where an end chord's error is easiest to miss); 15 m wide, so a
            // 37.5 m radius turned in 2 m steps: an arc end square to its chord sits ~0.2 m off at the edges
            var airport = NewAirport();
            var net = airport.Taxiways;
            var bend = new Vector3(0f, 0f, 0f);
            var outward = new Vector3(Mathf.Cos(70f * Mathf.Deg2Rad), 0f, Mathf.Sin(70f * Mathf.Deg2Rad));
            int a = net.AddNode(new Vector3(-300f, 0f, 0f)), b = net.AddNode(bend), c = net.AddNode(outward * 300f);
            net.Connect(a, b, 15f, PavementSurface.Asphalt, true);
            net.Connect(b, c, 15f, PavementSurface.Asphalt, true);
            net.Rebuild();
            CheckMesh(net.SurfaceMesh, "taxiway surface");

            float cut = RunwayRules.TaxiwayTurnRadius(15f) / Mathf.Tan((180f - 70f) * 0.5f * Mathf.Deg2Rad);
            var verts = net.SurfaceMesh.vertices;
            foreach (var dir in new[] { Vector3.left, outward })
            {
                var side = Vector3.Cross(Vector3.up, dir);
                foreach (float x in new[] { -7.5f, 7.5f })
                {
                    var corner = bend + dir * cut + side * x;
                    int shared = verts.Count(v => new Vector2(v.x - corner.x, v.z - corner.z).magnitude < 0.01f);
                    Assert.GreaterOrEqual(shared, 2, $"the straight and the bend share their corner at {corner}");
                }
            }
        }

        [Test]
        public void TaxiRouting_RespectsWidthAndSurface()
        {
            var airport = NewAirport();
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(0f, 0f, 0f));
            int b = net.AddNode(new Vector3(400f, 0f, 0f));
            int c = net.AddNode(new Vector3(400f, 0f, 400f));
            int d = net.AddNode(new Vector3(0f, 0f, 400f));
            net.Connect(a, b, 7.5f, PavementSurface.Asphalt);     // short way: code A only
            net.Connect(b, c, 15f, PavementSurface.Asphalt);
            net.Connect(a, d, 15f, PavementSurface.Asphalt);      // long way round for a jet
            net.Connect(d, c, 15f, PavementSurface.Asphalt);
            CollectionAssert.AreEqual(new[] { a, b, c }, net.FindRoute(a, c, Type("Cessna172")));
            CollectionAssert.AreEqual(new[] { a, d, c }, net.FindRoute(a, c, Type("Narrowbody")));
            net.Edge(net.EdgesAt(d).First(e => e.a == d && e.b == c || e.a == c && e.b == d).id).surface = PavementSurface.Dirt;
            Assert.IsNull(net.FindRoute(a, c, Type("Narrowbody")), "no route a jet can use");
        }

        [Test]
        public void SandboxAirport_GeometryFacesUpAndIsFinite()
        {
            var airport = AirfieldSetup.BuildSandboxAirport();
            spawned.Add(airport.gameObject);
            Assert.AreEqual(4, airport.Runways.Count);
            Assert.IsNotNull(airport.Depot, "the sandbox has a depot");
            Assert.AreEqual(1, airport.Depot.Teams.Count(t => t.type.kind == VehicleKind.SnowPlough), "three ploughs: one team");
            Assert.IsTrue(airport.Runways.Any(r => r.Designation == "09/27"));
            Assert.IsTrue(airport.Taxiways.nodes.Count(n => n.runway != null && n.runway.surface == PavementSurface.Asphalt) >= 1,
                "the east taxiway crosses runway 13/31");
            foreach (var r in airport.Runways)
            {
                CheckMesh(r.SurfaceMesh, r.name + " surface");
                CheckMesh(r.VergeMesh, r.name + " verges");
                if (r.Paved) CheckMesh(r.PaintMesh, r.name + " paint");
            }
            CheckMesh(airport.Taxiways.SurfaceMesh, "taxiway surface");
            CheckMesh(airport.Taxiways.PaintMesh, "taxiway paint");
            CheckMesh(airport.Taxiways.VergeMesh, "taxiway verges");
            Assert.IsTrue(airport.Taxiways.edges.Any(e => e.road), "a service road to the depot");
            Debug.Log($"sandbox airport: {airport.Runways.Sum(r => r.LightCount) + airport.Taxiways.LightCount} lights, " +
                      $"{airport.Taxiways.SurfaceMesh.vertexCount} taxiway surface verts");
        }

        [Test]
        public void Vehicles_ImportWithLightsWheelsAndWork()
        {
            var plough = VehicleSetup.Type("SnowPlough");
            var mower = VehicleSetup.Type("Mower");
            var depot = VehicleSetup.Type("Depot");
            Assert.IsNotNull(plough);
            Assert.IsNotNull(mower);
            Assert.IsNotNull(depot);
            Assert.AreEqual(VehicleKind.SnowPlough, plough.kind);
            Assert.That(plough.Width, Is.InRange(5.5f, 7f), "the angled blade is wider than the truck");
            Assert.That(plough.Length, Is.InRange(12f, 15f));
            Assert.AreEqual(4, plough.wheels.Length);
            Assert.AreEqual(2, plough.wheels.Count(w => w.steerable));
            Assert.AreEqual(2, plough.lights.Count(l => l.type == VehicleLightType.Beacon));
            Assert.IsTrue(plough.lights.Any(l => l.type == VehicleLightType.Head && l.position.y > 3f), "high plough lights");
            Assert.AreEqual("Broom", plough.spinners.Single().name);
            Assert.Greater(plough.workSpeed, mower.workSpeed);
            Assert.AreEqual(WorkKind.Mow, mower.Work);
            Assert.That(mower.laneWidth, Is.InRange(4f, 5f), "a 15 ft batwing");
            Assert.Greater(depot.lights.Count(l => l.type == VehicleLightType.Flood), 3);

            foreach (var model in new[] { "SnowPlough", "Mower" })
            {
                var prefab = VehicleSetup.Prefab(model);
                Assert.IsNotNull(prefab, $"{model}.prefab");
                var v = prefab.GetComponent<Vehicle>();
                Assert.IsNotNull(v);
                Assert.AreEqual(4, v.wheels.Length, $"{model}: wheels found by name");
                Assert.IsTrue(v.wheels.All(w => w.transform != null));
                Assert.IsNotNull(prefab.GetComponent<LODGroup>(), $"{model}: LODs");
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
                    Assert.AreEqual(1, r.sharedMaterials.Length, $"{model}/{r.name}: one material");
                Assert.Greater(VehicleLights.GlowMesh(v.type).vertexCount, 4 * v.type.lights.Length, "glows plus ground pools");
            }
        }

        [Test]
        public void Picking_AndRemovingTaxiwaysDepotsAndAprons()
        {
            var airport = NewAirport();
            var net = airport.Taxiways;
            // an east-west taxiway crossed by a north-south one: Connect splits both at a shared node
            int w = net.AddNode(new Vector3(-300f, 0f, 0f)), e = net.AddNode(new Vector3(300f, 0f, 0f));
            int n = net.AddNode(new Vector3(0f, 0f, 200f)), s = net.AddNode(new Vector3(0f, 0f, -200f));
            net.Connect(w, e, 15f, PavementSurface.Asphalt);
            net.Connect(n, s, 15f, PavementSurface.Asphalt);
            net.Rebuild();
            Assert.AreEqual(4, net.edges.Count, "split at the crossing");

            Assert.IsTrue(net.TryPickEdge(new Vector3(-100f, 0f, 7f), out var west));
            Assert.IsTrue((west.a == w || west.b == w), "the western half");
            Assert.IsFalse(net.TryPickEdge(new Vector3(-100f, 0f, 9f), out _), "beyond its half-width");
            Assert.IsTrue(net.TryPickEdge(new Vector3(-100f, 0f, 9f), out _, 2f), "within the slack");

            // take away the north-south taxiway: the east-west one is one section again
            net.RemoveEdges(net.edges.Where(x => x.a == n || x.b == n || x.a == s || x.b == s).ToList());
            net.Rebuild();
            Assert.AreEqual(1, net.edges.Count, "rejoined where the crossing was");
            Assert.AreEqual(2, net.nodes.Count, "the crossing node and the far ends are gone");
            Assert.AreEqual(600f, net.EdgeLength(net.edges[0]), 0.01f);
            CheckMesh(net.SurfaceMesh, "taxiway after removing");

            // a bend stays a bend, and sections that differ stay apart
            int c = net.AddNode(new Vector3(300f, 0f, 300f)), d = net.AddNode(new Vector3(600f, 0f, 0f));
            net.Connect(e, c, 15f, PavementSurface.Asphalt);
            net.Connect(e, d, 23f, PavementSurface.Asphalt);
            net.RemoveEdges(net.edges.Where(x => x.a == c || x.b == c).ToList());
            Assert.AreEqual(2, net.edges.Count, "straight on but a different width: not joined");
            net.RemoveEdges(net.edges.Where(x => x.a == d || x.b == d).ToList());
            int bend = net.AddNode(new Vector3(400f, 0f, 200f)), spur = net.AddNode(new Vector3(300f, 0f, -300f));
            net.Connect(e, bend, 15f, PavementSurface.Asphalt);
            net.Connect(e, spur, 15f, PavementSurface.Asphalt);
            net.RemoveEdges(net.edges.Where(x => x.a == spur || x.b == spur).ToList());
            Assert.AreEqual(2, net.edges.Count, "a bend isn't straightened");

            var depot = airport.AddDepot(new Vector2(0f, -400f), Vector2.up);
            Assert.IsTrue(airport.TryPickDepot(new Vector2(0f, -410f), out var hit) && hit == depot, "behind the doors");
            Assert.IsFalse(airport.TryPickDepot(new Vector2(0f, -390f), out _), "in front of them");
            Assert.AreEqual(4, depot.Corners.Length);

            airport.aprons.Add(new ApronArea { centre = new Vector3(0f, 0f, -600f), size = new Vector2(100f, 60f), rotation = 90f });
            airport.BuildGround();
            // size.x is across its axis, size.y along it: turned 90 degrees it is 60 m east-west, 100 m north-south
            Assert.IsTrue(airport.TryPickApron(new Vector2(25f, -645f), out int apron));
            Assert.IsFalse(airport.TryPickApron(new Vector2(35f, -600f), out _));
            var corners = Airport.Corners(airport.aprons[apron]);
            Assert.AreEqual(100f, Vector2.Distance(corners[0], corners[1]), 0.01f, "across");
            Assert.AreEqual(60f, Vector2.Distance(corners[0], corners[3]), 0.01f, "along");
            airport.RemoveApron(apron);
            Assert.AreEqual(0, airport.aprons.Count);
        }

        /// <summary>Finite, non-empty, and every triangle faces up (clockwise seen from above).</summary>
        internal static void CheckMesh(Mesh m, string what)
        {
            Assert.IsNotNull(m, what);
            Assert.Greater(m.vertexCount, 0, what);
            var v = m.vertices;
            Assert.IsTrue(v.All(p => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z)), $"{what}: NaN vertices");
            int flipped = 0, total = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var t = m.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    total++;
                    float y = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y;
                    if (y >= -1e-3f) continue;
                    if (flipped++ < 3) Debug.Log($"{what}: down-facing triangle at {(v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f}");
                }
            }
            Assert.Greater(total, 0, what);
            Assert.AreEqual(0, flipped, $"{what}: {flipped} of {total} triangles face down");
        }
    }
}
