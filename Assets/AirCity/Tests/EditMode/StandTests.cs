using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    /// <summary>Aircraft stands and de-icing bays: size rules, clearances, tiling, taxi-out swings, and staying attached
    /// to their taxiway.</summary>
    public class StandTests
    {
        readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
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

        /// <summary>An east-west taxiway from x = -600 to 600 on z = 0.</summary>
        static TaxiwayNetwork Straight(Airport airport, float width = 15f, bool lit = false)
        {
            var net = airport.Taxiways;
            net.Connect(net.AddNode(new Vector3(-600f, 0f, 0f)), net.AddNode(new Vector3(600f, 0f, 0f)), width,
                PavementSurface.Asphalt, lit);
            return net;
        }

        static float Bearing(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

        [Test]
        public void Roads_TouchStandsAtTheirHeadsButNeverCrossThem()
        {
            var airport = NewAirport();
            var net = Straight(airport);
            var st = net.AddStand(StandKind.Parking, Vector3.zero, 180f, SizeCode.C, PavementSurface.Concrete);
            net.Rebuild();
            var l = net.Layout(st);
            float hw = RunwayRules.RoadWidth * 0.5f;
            var head = l.At(0f, l.far) + l.axis * hw;       // a road centreline with its edge on the stand's head
            Assert.IsEmpty(net.RoadAccess(l), "no road yet: vehicles come along the taxiway");
            Assert.AreSame(st, net.StandAcross(new Vector2(0f, -10f), new Vector2(0f, -200f), RunwayRules.RoadWidth), "through it");
            Assert.IsNull(net.StandAcross(head - Vector2.right * 100f, head + Vector2.right * 100f, RunwayRules.RoadWidth),
                "along its head is fine");

            net.Connect(net.AddNode(new Vector3(head.x - 100f, 0f, head.y)), net.AddNode(new Vector3(head.x + 100f, 0f, head.y)),
                RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            net.Rebuild();
            var access = net.RoadAccess(l);
            Assert.AreEqual(1, access.Count);
            Assert.AreEqual(0, access[0].side, "at its head");
            Assert.AreEqual(l.x1 - l.x0, access[0].length, 1f, "all the way along it");
            Assert.AreEqual(hw, Vector2.Distance(access[0].onPad, access[0].onRoad), 0.1f);

            // the next stand along touches the road too; a code E stand, longer, would run out over it
            var next = net.AddStand(StandKind.Parking, new Vector3(l.x1 - l.x0, 0f, 0f), 180f, SizeCode.C, PavementSurface.Concrete);
            Assert.IsTrue(net.Fits(net.Layout(next)));
            Assert.AreEqual(0, net.RoadAccess(net.Layout(next)).Single().side);
            var taxiway = net.edges.First(e => !e.IsRoad);
            var big = StandLayout.Of(new Stand { kind = StandKind.Parking, code = SizeCode.E, surface = PavementSurface.Concrete,
                position = new Vector3(-100f, 0f, 0f), heading = 180f }, taxiway, net.At(taxiway.a), net.At(taxiway.b));
            Assert.IsNotNull(big);
            Assert.IsFalse(net.Fits(big), "a stand can't be put over a road");

            // a road coming to the stand's side end-on: a way in there too
            var side = l.At(l.x0, (l.near0 + l.far) * 0.5f);
            var outward = (side - l.At(0f, (l.near0 + l.far) * 0.5f)).normalized;
            net.Connect(net.AddNode(new Vector3(side.x + outward.x * 60f, 0f, side.y + outward.y * 60f)),
                net.AddNode(new Vector3(side.x + outward.x * 0.5f, 0f, side.y + outward.y * 0.5f)),
                RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            Assert.IsTrue(net.RoadAccess(l).Any(a => a.side != 0), "touching its side");
        }

        [Test]
        public void Rules_StandsTakeAircraftUpToTheirCode()
        {
            var cessna = Type("Cessna172");
            var jet = Type("Narrowbody");
            Assert.AreEqual('A', RunwayRules.TaxiwayCode(7.5f));
            Assert.AreEqual('C', RunwayRules.TaxiwayCode(15f));
            Assert.AreEqual('E', RunwayRules.TaxiwayCode(23f));
            Assert.AreEqual(StandProblem.None, RunwayRules.CheckStand(jet, 'C', PavementSurface.Concrete, 15f, PavementSurface.Asphalt));
            Assert.AreEqual(StandProblem.None, RunwayRules.CheckStand(jet, 'E', PavementSurface.Concrete, 23f, PavementSurface.Asphalt),
                "a bigger stand takes it too");
            Assert.AreEqual(StandProblem.WingspanTooBig, RunwayRules.CheckStand(jet, 'B', PavementSurface.Concrete, 15f, PavementSurface.Asphalt));
            Assert.AreEqual(StandProblem.TaxiwayTooSmall, RunwayRules.CheckStand(jet, 'C', PavementSurface.Concrete, 10.5f, PavementSurface.Asphalt));
            Assert.AreEqual(StandProblem.NeedsPavement, RunwayRules.CheckStand(jet, 'C', PavementSurface.Grass, 15f, PavementSurface.Asphalt));
            Assert.AreEqual(StandProblem.None, RunwayRules.CheckStand(cessna, 'A', PavementSurface.Grass, 10.5f, PavementSurface.Grass));
            Assert.IsTrue(RunwayRules.StandSurfaceOk('B', PavementSurface.Grass));
            Assert.IsFalse(RunwayRules.StandSurfaceOk('C', PavementSurface.Grass), "grass stands are for light aircraft");
            Assert.Less(RunwayRules.TaxilaneClearance(7.5f), RunwayRules.TaxilaneClearance(23f));

            var airport = NewAirport();
            var net = Straight(airport);
            var a = net.AddStand(StandKind.Parking, new Vector3(0f, 0f, 0f), 0f, SizeCode.A, PavementSurface.Asphalt);
            var c = net.AddStand(StandKind.Parking, new Vector3(100f, 0f, 0f), 0f, SizeCode.C, PavementSurface.Asphalt);
            CollectionAssert.AreEquivalent(new[] { a, c }, net.StandsFor(cessna));
            CollectionAssert.AreEquivalent(new[] { c }, net.StandsFor(jet));
            Assert.AreEqual(1, a.number);
            Assert.AreEqual(2, c.number);

            // a runway north of them: the jet stand's pad reaches it, the light aircraft's doesn't
            airport.AddRunway(new Vector3(-500f, 0f, 80f), new Vector3(500f, 0f, 80f), PavementSurface.Asphalt, 30f);
            Assert.IsFalse(net.Fits(net.Layout(c)), "stands stay off runways");
            Assert.IsTrue(net.Fits(net.Layout(a)));
        }

        [Test]
        public void PushBackStands_KeepClearOfTheTaxiwayAtEveryAngle_AndTile()
        {
            var airport = NewAirport();
            var net = Straight(airport);
            float clearance = RunwayRules.TaxilaneClearance(15f);
            float span = RunwayRules.MaxWingspan('C'), length = RunwayRules.StandLength('C');
            float x = -500f;
            foreach (float angle in new[] { 90f, 60f, 45f, 30f })
                foreach (int side in new[] { 1, -1 })
                {
                    // the stand's axis turned `angle` from east, to the north (side 1) or south
                    float bearing = side > 0 ? 90f - angle : 90f + angle;
                    var first = net.AddStand(StandKind.Parking, new Vector3(x, 0f, 0f), bearing, SizeCode.C, PavementSurface.Concrete);
                    Assert.IsNotNull(first, $"{angle} deg");
                    var l = net.Layout(first);
                    Assert.AreEqual((float)side, Mathf.Sign(l.side.y), "on the side it points to");

                    // the parked design aircraft: no corner nearer the taxiway centreline than the clearance
                    var right = l.Right;
                    float nearest = float.MaxValue;
                    foreach (float sx in new[] { -0.5f, 0.5f })
                        foreach (float ss in new[] { l.tailS, l.noseS })
                            nearest = Mathf.Min(nearest, Mathf.Abs((l.entry + l.axis * ss + right * (sx * span)).y));
                    Assert.AreEqual(clearance, nearest, 0.05f, $"{angle} deg: tail corner on the clearance line");
                    Assert.AreEqual(length, l.noseS - l.tailS, 0.01f);
                    Assert.Greater(Vector2.Dot(l.stop - l.entry, l.axis), l.tailS, "the stop bar is on the stand");

                    // the next stand along at the same angle shares its side line: it fits; 5 m closer, it doesn't
                    float pitch = (l.x1 - l.x0) / l.sin;
                    var probe = new Stand { kind = StandKind.Parking, code = SizeCode.C, heading = bearing, position = new Vector3(x + pitch, 0f, 0f) };
                    var next = StandLayout.Of(probe, l.edge, new Vector2(-600f, 0f), new Vector2(600f, 0f));
                    Assert.IsTrue(net.Fits(next), $"{angle} deg: neighbours side by side fit");
                    probe.position.x -= 5f;
                    Assert.IsFalse(net.Fits(StandLayout.Of(probe, l.edge, new Vector2(-600f, 0f), new Vector2(600f, 0f))), "overlapping doesn't");
                    x += pitch + 80f;
                }
            Assert.IsNull(net.AddStand(StandKind.Parking, new Vector3(550f, 0f, 0f), 80f, SizeCode.C, PavementSurface.Concrete),
                "too shallow an angle");
            Assert.IsNull(net.AddStand(StandKind.Parking, new Vector3(0f, 0f, 30f), 0f, SizeCode.C, PavementSurface.Concrete),
                "not on a taxiway");
            net.Rebuild();
            AirfieldTests.CheckMesh(net.SurfaceMesh, "taxiway and stand surface");
            AirfieldTests.CheckMesh(net.PaintMesh, "taxiway and stand paint");
        }

        [Test]
        public void TaxiOutStands_HoldTheirSwing()
        {
            var airport = NewAirport();
            var net = Straight(airport);
            float clearance = RunwayRules.TaxilaneClearance(15f);
            foreach (float bearing in new[] { 0f, 45f, 135f, 300f })
            {
                net.stands.Clear();
                var pushed = StandLayout.Of(new Stand { code = SizeCode.C, heading = bearing }, net.edges[0],
                    new Vector2(-600f, 0f), new Vector2(600f, 0f));
                var st = net.AddStand(StandKind.Parking, Vector3.zero, bearing, SizeCode.C, PavementSurface.Concrete, StandExit.TaxiOut);
                var l = net.Layout(st);
                Assert.IsTrue(l.TaxiOut);
                Assert.Greater(Vector2.Dot(l.nose, -l.axis), 0.99f, "parked nose out");
                Assert.Greater(l.x1 - l.x0, pushed.x1 - pushed.x0, $"{bearing}: wider than a push-back stand");

                var path = l.LeadIn(1f);
                Assert.Less(Mathf.Abs(path[0].point.y), 0.01f, "the lead-in leaves the taxiway centreline");
                Assert.Greater(Vector2.Dot(path[0].heading, l.inAlong), 0.99f, "along the taxiway");
                var end = path[path.Count - 1];
                Assert.Less(Vector2.Distance(end.point, l.stop), 0.01f, "and ends at the stop bar");
                Assert.Greater(Vector2.Dot(end.heading, -l.axis), 0.99f, "heading for the taxiway");
                Assert.Greater(l.uTurnS, Vector2.Dot(l.stop - l.entry, l.axis), "lined up before the stop bar");
                for (int i = 1; i < path.Count; i++)
                    Assert.Less(Vector2.Distance(path[i].point, path[i - 1].point), 1.01f, "one continuous line");
                foreach (var (p, _) in path)
                    if (Mathf.Abs(p.y) > 7.5f) Assert.IsTrue(l.Contains(p), $"{bearing}: the track at {p} is on the pad");

                // the wingtips stay on the pad wherever they're out of the taxilane's way
                float wing = RunwayRules.MaxWingspan('C') * 0.5f;
                float setback = RunwayRules.NoseGearSetback * RunwayRules.StandLength('C'), wingBack = 0.45f * RunwayRules.StandLength('C');
                foreach (var (p, h) in path)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var tip = p + h * (setback - wingBack) + new Vector2(h.y, -h.x) * (side * wing);
                        if (Mathf.Abs(tip.y) < clearance) continue;
                        Assert.IsTrue(l.Contains(tip), $"{bearing}: wingtip at {tip} on the pad");
                    }
            }
            net.Rebuild();
            AirfieldTests.CheckMesh(net.SurfaceMesh, "taxi-out stand surface");
        }

        [Test]
        public void DeIcingBays_RunAlongTheTaxiway()
        {
            var airport = NewAirport();
            var net = Straight(airport, 15f, true);
            net.Rebuild();
            int lights = net.LightCount, paint = net.PaintMesh.vertexCount;
            Assert.IsNull(net.AddStand(StandKind.DeIcing, Vector3.zero, 0f, SizeCode.C, PavementSurface.Concrete), "across the taxiway");
            var bay = net.AddStand(StandKind.DeIcing, Vector3.zero, 90f, SizeCode.C, PavementSurface.Grass);
            Assert.IsNotNull(bay);
            Assert.AreEqual(PavementSurface.Concrete, bay.surface, "bays are paved");
            Assert.AreEqual("D1", bay.Label);
            var l = net.Layout(bay);
            float span = RunwayRules.MaxWingspan('C'), clr = RunwayRules.StandClearance('C');
            Assert.AreEqual(span + 2f * clr + 2f * RunwayRules.DeIcingVehicleLane, l.x1 - l.x0, 0.01f, "trucks either side");
            Assert.AreEqual(0f, l.stop.y, 0.01f, "stops on the centreline");
            Assert.Greater(l.stop.x, 0f, "nose towards the way it's heading");
            Assert.IsTrue(l.Contains(new Vector2(0f, 25f)) && l.Contains(new Vector2(0f, -25f)), "both sides");

            var probe = new Stand { kind = StandKind.DeIcing, code = SizeCode.C, heading = 90f, position = new Vector3(40f, 0f, 0f) };
            Assert.IsFalse(net.Fits(StandLayout.Of(probe, l.edge, new Vector2(-600f, 0f), new Vector2(600f, 0f))), "bays don't overlap");
            net.Rebuild();
            Assert.Less(net.LightCount, lights, "no edge lights in the bay");
            Assert.Greater(net.PaintMesh.vertexCount, paint, "painted");
            AirfieldTests.CheckMesh(net.SurfaceMesh, "de-icing bay surface");
        }

        [Test]
        public void Stands_StayOnTheirTaxiway_AndStayPutWhenItGoes()
        {
            var airport = NewAirport();
            var net = Straight(airport, 15f, true);
            net.Rebuild();
            int verges = net.VergeMesh.vertexCount;
            var stand = net.AddStand(StandKind.Parking, new Vector3(100f, 0f, 0f), 0f, SizeCode.C, PavementSurface.Concrete);
            net.Rebuild();
            Assert.Greater(net.VergeMesh.vertexCount, verges, "the verge on the stand's side is split round it");

            // a taxiway crossing it splits the section the stand opens onto: it stays where it was
            net.Connect(net.AddNode(new Vector3(0f, 0f, -300f)), net.AddNode(new Vector3(0f, 0f, 300f)), 15f, PavementSurface.Asphalt);
            var l = net.Layout(stand);
            Assert.IsNotNull(l, "still on a taxiway");
            Assert.AreEqual(100f, l.entry.x, 0.01f);
            Assert.IsTrue(net.TryPickStand(new Vector2(100f, 60f), out var picked) && picked == stand, "picked on its pad");
            Assert.IsFalse(net.TryPickStand(new Vector2(100f, -60f), out _), "not across the taxiway");

            // take the crossing away: the sections rejoin and the stand's still there; take its taxiway: it stays put,
            // no longer connected, and a taxiway laid along its front again connects it
            net.RemoveEdges(net.edges.Where(e => Mathf.Abs(net.At(e.a).x) < 1f && Mathf.Abs(net.At(e.b).x) < 1f).ToList());
            Assert.AreEqual(1, net.stands.Count);
            var pad = net.Layout(stand).Pad;
            net.RemoveEdges(net.edges.Where(e => Mathf.Abs(net.At(e.a).y) < 1f && Mathf.Abs(net.At(e.b).y) < 1f && net.At(e.b).x > 0f).ToList());
            Assert.AreEqual(1, net.stands.Count, "it stays when its taxiway goes");
            var loose = net.Layout(stand);
            Assert.IsNull(loose.edge, "no longer connected");
            for (int i = 0; i < 4; i++) Assert.Less(Vector2.Distance(pad[i], loose.Pad[i]), 0.01f, "and doesn't move");
            Assert.AreEqual(StandProblem.NotConnected, net.CheckStand(stand, Type("Narrowbody")));
            net.Connect(net.AddNode(new Vector3(50f, 0f, 0f)), net.AddNode(new Vector3(400f, 0f, 0f)), 15f, PavementSurface.Asphalt);
            Assert.IsNotNull(net.Layout(stand).edge, "connected again");
            Assert.AreEqual(StandProblem.None, net.CheckStand(stand, Type("Narrowbody")));
        }

        [Test]
        public void Stands_BuiltFirstThenConnectedByATaxiwayAlongTheirFront()
        {
            var airport = NewAirport();
            var net = airport.Taxiways;
            // a code C stand standing free, its centreline due north, its front line (the taxilane to come) running east
            var st = new Stand { kind = StandKind.Parking, code = SizeCode.C, surface = PavementSurface.Concrete,
                position = new Vector3(100f, 0f, 0f), heading = 0f };
            st.Frame(Vector2.right, 0f);
            Assert.IsNotNull(net.Add(st));
            net.Rebuild();
            var l = net.Layout(st);
            Assert.IsNull(l.edge);
            Assert.AreEqual(RunwayRules.TaxiwayWidthFor('C') * 0.5f, l.halfWidth, 0.01f, "laid out for its code's taxilane");
            Assert.IsTrue(net.Fits(l));
            Assert.Greater(net.SurfaceMesh.vertexCount, 0, "its pad is built with no taxiway");
            Assert.AreEqual(StandProblem.NotConnected, net.CheckStand(st, Type("Narrowbody")));
            Assert.IsEmpty(net.StandsFor(Type("Narrowbody")));

            // a taxiway across its pad isn't allowed; one along its front line connects it
            Assert.AreSame(st, net.StandAcross(new Vector2(100f, -50f), new Vector2(100f, 100f), 15f, road: false));
            Assert.IsNull(net.StandAcross(new Vector2(-200f, 0f), new Vector2(400f, 0f), 15f, road: false));
            var stop = l.stop;
            float tail = l.tailS, far = l.far, clearance = l.clearance;
            var parallel = new Vector2(0f, 0.4f);   // within OnCentreline of it
            net.Connect(net.AddNode(new Vector3(-200f, 0f, parallel.y)), net.AddNode(new Vector3(400f, 0f, parallel.y)), 23f,
                PavementSurface.Asphalt);
            l = net.Layout(st);
            Assert.IsNotNull(l.edge, "connected");
            Assert.AreEqual(11.5f, l.halfWidth, 0.01f, "its pad runs in under the taxiway");
            Assert.Less(Vector2.Distance(stop, l.stop), 0.01f, "connecting it doesn't move it");
            Assert.AreEqual(tail, l.tailS, 0.01f);
            Assert.AreEqual(far, l.far, 0.01f);
            Assert.AreEqual(clearance, l.clearance, 0.01f, "it keeps its own taxilane's clearance");
            Assert.AreEqual(StandProblem.None, net.CheckStand(st, Type("Narrowbody")));
            Assert.AreEqual(1, net.StandsFor(Type("Narrowbody")).Count);
            // the 23 m (code E) taxiway now takes only aircraft the stand's code C taxilane clears
            Assert.AreEqual('C', net.LaneLimits()[l.edge], "bigger aircraft can't pass it");

            // a taxiway at an angle to the front line doesn't connect it
            var other = new Stand { kind = StandKind.Parking, code = SizeCode.C, surface = PavementSurface.Concrete,
                position = new Vector3(-300f, 0f, 300f), heading = 0f };
            other.Frame(Vector2.right, 0f);
            net.Add(other);
            net.Connect(net.AddNode(new Vector3(-400f, 0f, 290f)), net.AddNode(new Vector3(-200f, 0f, 310f)), 15f, PavementSurface.Asphalt);
            Assert.IsNull(net.Layout(other).edge, "6 degrees off its front line");
        }

        [Test]
        public void Stands_SavedBeforeFrontLines_TakeTheirTaxiways()
        {
            var airport = NewAirport();
            var net = Straight(airport, 23f);
            // as saved before stands had a front line: a point on the taxiway and a heading, nothing else
            var old = JsonUtility.FromJson<Stand>(
                "{\"number\":4,\"kind\":0,\"code\":2,\"surface\":2,\"position\":{\"x\":100,\"y\":0,\"z\":0.3},\"heading\":30}");
            Assert.IsFalse(old.Framed);
            net.stands.Add(old);
            var l = net.Layout(old);
            Assert.IsNotNull(l.edge, "connected to the taxiway it was saved on");
            Assert.IsTrue(old.Framed);
            Assert.AreEqual(0f, old.position.z, 1e-4f, "onto its centreline");
            Assert.AreEqual(11.5f, l.halfWidth, 0.01f);
            Assert.AreEqual(60f, Vector2.Angle(l.along, l.axis), 0.01f, "60 degrees to it, leaning east");
        }

        [Test]
        public void Stands_TakeAnyAngleEitherWay()
        {
            var airport = NewAirport();
            var net = Straight(airport);
            // north of an east-west taxiway: 90 is square on, under 90 leans east, over 90 west
            foreach (float angle in new[] { 25f, 37f, 90f, 113f, 155f })
            {
                net.stands.Clear();
                float rad = angle * Mathf.Deg2Rad;
                var axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                var st = net.AddStand(StandKind.Parking, Vector3.zero, Bearing(axis), SizeCode.C, PavementSurface.Concrete);
                Assert.IsNotNull(st, $"{angle} degrees");
                var l = net.Layout(st);
                Assert.AreEqual(angle, Vector2.Angle(l.along, l.axis), 0.01f, $"{angle} degrees to the taxiway");
                Assert.IsTrue(net.Fits(l), $"{angle} degrees fits");
            }
            Assert.IsNull(net.AddStand(StandKind.Parking, Vector3.zero, Bearing(new Vector2(1f, 0.3f)), SizeCode.C,
                PavementSurface.Concrete), "too shallow");
        }

        [Test]
        public void SandboxStands_FitAndTakeTheirAircraft()
        {
            var airport = AirfieldSetup.BuildSandboxAirport();
            spawned.Add(airport.gameObject);
            var net = airport.Taxiways;
            Assert.AreEqual(net.stands.Count, net.Layouts().Count, "every stand has its taxiway");
            Assert.GreaterOrEqual(net.stands.Count(s => s.kind == StandKind.Parking), 10);
            Assert.AreEqual(2, net.stands.Count(s => s.DeIcing));
            foreach (var l in net.Layouts())
                Assert.IsTrue(net.Fits(l), $"{l.stand.Name} overlaps another");
            Assert.Greater(net.StandsFor(Type("Narrowbody")).Count, 5);
            var cessna = net.StandsFor(Type("Cessna172"));
            Assert.Greater(cessna.Count, 3, "the GA ramp takes Cessnas");
            Assert.IsEmpty(cessna.Intersect(net.StandsFor(Type("Narrowbody"))), "the jet stands turn small aircraft away");
            Assert.AreEqual(2, net.StandsFor(Type("Narrowbody"), StandKind.DeIcing).Count);
            var park = net.Layout(net.StandsFor(Type("Narrowbody"))[0]).ParkPosition(Type("Narrowbody"));
            Assert.IsTrue(float.IsFinite(park.x) && float.IsFinite(park.y));
            Debug.Log($"sandbox stands: {string.Join(", ", net.stands.Select(s => $"{s.Name} {s.Code}"))}");
        }
    }
}
