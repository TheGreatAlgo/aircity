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
    /// <summary>Terminal buildings (curved walls, what they may not stand on) and jet bridges: the model built to the
    /// rules, placing bridges that reach the doors, docking with parked aircraft and pulling back for push-back.</summary>
    public class TerminalBridgeTests
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
        /// A 3 km runway along z = 0, a parallel taxiway at z = -200 with exits at x = ±500, a depot with tugs to the
        /// south-west, push-back stands of a code on the taxiway's south side (code C: at x = -40.5, 0, 40.5; code E:
        /// one at x = 0), and a two-storey terminal from 12 m beyond their heads, 30 m deep, as wide as the stands.
        /// </summary>
        (Airport airport, List<Stand> stands, Terminal terminal) TestAirport(SizeCode code = SizeCode.C, bool terminal = true)
        {
            AirfieldSetup.CreateMaterials();
            var go = new GameObject("Test airport");
            go.SetActive(false);
            var airport = go.AddComponent<Airport>();
            AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 0f;
            airport.aircraftTypes = AircraftSetup.AllTypes().ToList();
            go.SetActive(true);
            spawned.Add(go);

            var r = airport.AddRunway(new Vector3(-1500f, 0f, 0f), new Vector3(1500f, 0f, 0f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0f, z), on);
            int w = Node(-1450f, 0f, r), a = Node(-1450f, -200f), m1 = Node(-500f, -200f), m2 = Node(500f, -200f),
                b = Node(1450f, -200f), e = Node(1450f, 0f, r);
            float width = RunwayRules.TaxiwayWidthFor(code.Letter());
            foreach (var (x, y) in new[] { (w, a), (a, m1), (m1, m2), (m2, b), (b, e) }) net.Connect(x, y, width, PavementSurface.Asphalt);
            net.Connect(Node(-500f, 0f, r), m1, width, PavementSurface.Asphalt);
            net.Connect(Node(500f, 0f, r), m2, width, PavementSurface.Asphalt);
            int p = net.SplitEdge(net.edges.First(x => (x.a == m1 && x.b == m2) || (x.a == m2 && x.b == m1)), new Vector3(-300f, 0f, -200f));
            net.Connect(Node(-300f, -312f), p, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            var stands = new List<Stand>();
            foreach (float x in code == SizeCode.C ? new[] { -40.5f, 0f, 40.5f } : new[] { 0f })
            {
                var st = net.AddStand(StandKind.Parking, new Vector3(x, 0f, -200f), 180f, code, PavementSurface.Concrete);
                Assert.IsNotNull(st);
                st.noSmallAircraft = true;
                stands.Add(st);
            }
            net.Rebuild();
            r.Rebuild();

            Terminal t = null;
            if (terminal)
            {
                var pads = stands.Select(s => net.Layout(s)).ToList();
                float head = pads.Min(l => l.Pad.Min(c => c.y));
                float x0 = pads.Min(l => l.Pad.Min(c => c.x)), x1 = pads.Max(l => l.Pad.Max(c => c.x));
                t = airport.AddTerminal(new[]
                {
                    new Vector2(x0, head - 42f), new Vector2(x1, head - 42f), new Vector2(x1, head - 12f), new Vector2(x0, head - 12f),
                });
                Assert.IsNotNull(t, "terminal");
            }

            var depot = airport.AddDepot(new Vector2(-300f, -330f), Vector2.up);
            depot.SetAssets(VehicleSetup.Type("SnowPlough"), VehicleSetup.Type("Mower"), VehicleSetup.Type("Depot"), null, null, null);
            depot.SetTugAssets(VehicleSetup.Type("TugSmall"), VehicleSetup.Type("TugLarge"), null, null);
            depot.autoDispatch = false;
            depot.SetFleet(0, 0);
            depot.SetTugs(1, 1);
            return (airport, stands, t);
        }

        AirCity.Aircraft.Aircraft ParkOn(Airport airport, Stand stand, string model)
        {
            var l = airport.Taxiways.Layout(stand);
            var at = l.ParkPosition(Type(model));
            var prefab = AircraftSetup.Prefab(Type(model));
            Assert.IsNotNull(prefab, $"{model} prefab");
            var go = Object.Instantiate(prefab, new Vector3(at.x, 0f, at.y), Quaternion.LookRotation(new Vector3(l.nose.x, 0f, l.nose.y)));
            spawned.Add(go);
            return go.GetComponent<AirCity.Aircraft.Aircraft>();
        }

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

        static string Problems(Airport airport, Stand st) => string.Join("; ", st.bridges.Select(b =>
            b.DoorName + ": " + string.Join(", ", airport.Bridges.Check(st, b).Select(c => $"{(c.type != null ? c.type.model : "placement")} {c.problem}"))));

        // ------------------------------------------------------------------ terminals

        [Test]
        public void Terminal_HalfCircleEdge_BowsOutAsAnArc()
        {
            var t = Terminal.Polygon(new[] { new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 40f), new Vector2(0f, 40f) });
            Assert.Greater(Geo2.SignedArea(t.corners), 0f, "stored counter-clockwise");
            // edge 1 runs (40, 0) -> (40, 40): bowing it out by half its length makes a half circle to the east
            t.SetBulge(1, 100f);
            Assert.AreEqual(20f, t.Bulge(1), 1e-4f, "limited to a half circle");
            Assert.AreEqual(1600f + Mathf.PI * 400f * 0.5f, t.Area, 15f, "a square and half a disc");
            var mid = t.EdgeMiddle(1);
            Assert.AreEqual(60f, mid.x, 1e-3f);
            Assert.AreEqual(20f, mid.y, 1e-3f);
            foreach (var p in t.Outline().Where(p => p.x > 40.01f))
                Assert.AreEqual(20f, Vector2.Distance(p, new Vector2(40f, 20f)), 0.01f, "every arc point on the circle");
            Assert.IsTrue(t.Contains(new Vector2(55f, 20f)));
            Assert.IsFalse(t.Contains(new Vector2(55f, 2f)));
            // bowed in instead
            t.SetBulge(1, -10f);
            Assert.IsFalse(t.Contains(new Vector2(35f, 20f)));
        }

        [Test]
        public void Terminal_StaysOffTaxiwaysStandsAndOtherTerminals_AndBuildsWalls()
        {
            var (airport, stands, t) = TestAirport();
            Assert.AreEqual("across a taxiway", airport.TerminalProblem(Terminal.Polygon(new[]
                { new Vector2(-20f, -220f), new Vector2(20f, -220f), new Vector2(20f, -180f), new Vector2(-20f, -180f) })));
            var pad = airport.Taxiways.Layout(stands[1]).Centre;
            StringAssert.StartsWith("on Stand", airport.TerminalProblem(Terminal.Polygon(new[]
                { pad - new Vector2(10f, 10f), pad + new Vector2(10f, -10f), pad + new Vector2(10f, 10f), pad + new Vector2(-10f, 10f) })));
            Assert.AreEqual("overlaps another terminal", airport.TerminalProblem(t.Clone()));
            Assert.AreEqual("too small", airport.TerminalProblem(Terminal.Polygon(new[]
                { new Vector2(0f, -900f), new Vector2(5f, -900f), new Vector2(5f, -895f), new Vector2(0f, -895f) })));

            var mesh = airport.TerminalMesh;
            Assert.IsNotNull(mesh);
            Assert.Greater(mesh.vertexCount, 100);
            // up to the parapet, or the plant rooms on the roof (2.4 m)
            Assert.That(mesh.bounds.max.y, Is.InRange(t.Height - 0.05f, t.RoofHeight + 2.45f));
            // a curved end and another floor: rebuilt, taller
            var changed = t.Clone();
            changed.floors = 3;
            changed.SetBulge(0, 8f);    // landside, away from the stands
            Assert.IsTrue(airport.ChangeTerminal(t, changed), airport.TerminalProblem(changed, t));
            Assert.That(airport.TerminalMesh.bounds.max.y, Is.InRange(3 * Terminal.FloorHeight + Terminal.Parapet - 0.05f,
                3 * Terminal.FloorHeight + 2.45f));
            // bowed onto the stands: refused, unchanged
            var onStands = t.Clone();
            onStands.SetBulge(2, 15f);
            Assert.IsFalse(airport.ChangeTerminal(t, onStands));
            Assert.AreEqual(0f, t.Bulge(2));
        }

        // ------------------------------------------------------------------ the model

        [Test]
        public void Model_MatchesBridgeRules()
        {
            var type = VehicleSetup.Type("JetBridge");
            Assert.IsNotNull(type, "JetBridge.vehicle not imported (ArtSource/Vehicles/build.py -- --model JetBridge)");
            Assert.AreEqual(VehicleKind.JetBridge, type.kind);
            var d = type.bridge;
            Assert.AreEqual(JetBridgeRules.RotundaFloor, d.rotundaFloor, 1e-3f);
            Assert.AreEqual(JetBridgeRules.RotundaRadius, d.rotundaRadius, 1e-3f);
            Assert.AreEqual(JetBridgeRules.TunnelStart, d.tunnelStart, 1e-3f);
            Assert.AreEqual(JetBridgeRules.SectionLength, d.sectionLength, 1e-3f);
            Assert.AreEqual(JetBridgeRules.CabReach, d.cabReach, 1e-3f);
            Assert.AreEqual(JetBridgeRules.CabWidth, d.cabWidth, 1e-3f);
            Assert.AreEqual(JetBridgeRules.DriveFromEnd, d.driveFromEnd, 1e-3f);
            Assert.AreEqual(JetBridgeRules.LinkWidth, d.linkWidth, 1e-3f);
            Assert.AreEqual(JetBridgeRules.YokeBottom, d.yokeBottom, 1e-3f);
            Assert.AreEqual(JetBridgeRules.UpperFloor, d.upperFloor, 1e-3f);
            Assert.AreEqual(JetBridgeRules.CabBottom, d.cabBottom, 1e-3f);
            Assert.AreEqual(JetBridgeRules.CabTop, d.cabTop, 1e-3f);
            Assert.AreEqual(JetBridgeRules.LinkBottom, d.linkBottom, 1e-3f);
            Assert.AreEqual(JetBridgeRules.LinkTop, d.linkTop, 1e-3f);
            Assert.AreEqual(JetBridgeRules.HeadWidth, d.headWidth, 1e-3f);
            Assert.AreEqual(JetBridgeRules.HeadBottom, d.headBottom, 1e-3f);
            Assert.AreEqual(JetBridgeRules.HeadTop, d.headTop, 1e-3f);
            Assert.AreEqual(JetBridgeRules.HeadUpperBottom, d.headUpperBottom, 1e-3f);
            Assert.AreEqual(JetBridgeRules.HeadUpperWider, d.headUpperWider, 1e-3f);
            var prefab = VehicleSetup.Prefab("JetBridge");
            Assert.IsNotNull(prefab, "JetBridge prefab (VehicleSetup.CreateMissing)");
            foreach (var part in new[] { "Rotunda", "RotundaHigh", "Tunnel_A", "Tunnel_B", "Tunnel_C", "Drive", "DriveLegs", "Cab", "Link",
                         "LinkColumnFoot", "LinkColumn", "LinkColumnHead", "Head", "HeadUpper", "HeadEnd", "HeadUpperEnd" })
                Assert.IsNotNull(prefab.transform.Find(part), part);
        }

        // ------------------------------------------------------------------ placing bridges

        [Test]
        public void CodeCStand_OneBridge_DocksTheNarrowbodyFromTheTerminal()
        {
            var (airport, stands, _) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            var b = st.bridges[0];
            Assert.AreEqual(1, b.door);
            var sl = airport.Taxiways.Layout(st);
            var l = BridgeLayout.Of(b, sl, airport.terminals);
            Assert.IsTrue(l.Connected, "its link reaches the terminal");
            Assert.LessOrEqual(l.LinkLength, JetBridgeRules.MaxLink);
            var jet = Type("Narrowbody");
            var pose = l.Dock(jet, out var problem);
            Assert.AreEqual(BridgeProblem.None, problem, Problems(airport, st));
            Assert.That(pose.length, Is.InRange(JetBridgeRules.MinTunnel, JetBridgeRules.MaxTunnel));
            Assert.LessOrEqual(Mathf.Abs(pose.Rise), JetBridgeRules.MaxSlope * pose.Run + 1e-3f);
            Assert.AreEqual(BridgeProblem.None, airport.Bridges.FirstProblem(st, b, out _), Problems(airport, st));
            // the cab ends at the door: bumper DoorGap off the sill, facing the fuselage
            Assert.IsTrue(BridgeLayout.TryDoor(jet, DoorSide.Left, false, 1, out var door));
            var sill = sl.ParkPosition(jet) + Geo2.Right(sl.nose) * door.position.x + sl.nose * door.position.z;
            var bumper = l.rotunda + pose.heading * (JetBridgeRules.TunnelStart + pose.Run) + pose.CabHeading * JetBridgeRules.CabReach;
            Assert.AreEqual(JetBridgeRules.DoorGap, Vector2.Distance(bumper, sill), 0.05f);
            Assert.AreEqual(door.position.y, pose.cabFloor, 1e-3f);
            // parked, it clears the aircraft and the next stand
            Assert.IsTrue(l.Parked(airport.Bridges.TypesFor(st), out _));
            // the light aircraft board by stairs: not this stand's anyway (no small aircraft)
            Assert.IsFalse(airport.Bridges.TypesFor(st).Any(t => t.Light));
        }

        [Test]
        public void CodeEStand_TwoBridges_DockTheWidebodyWithoutCrossing()
        {
            var (airport, stands, _) = TestAirport(SizeCode.E);
            var st = stands[0];
            Assert.AreEqual(2, airport.Bridges.SetBridges(st, 2), Problems(airport, st));
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, st.bridges.Select(b => b.door));
            var sl = airport.Taxiways.Layout(st);
            var wide = Type("Widebody");
            var layouts = st.bridges.Select(b => BridgeLayout.Of(b, sl, airport.terminals)).ToList();
            var poses = new List<BridgePose>();
            foreach (var l in layouts)
            {
                Assert.IsTrue(l.Connected);
                poses.Add(l.Dock(wide, out var problem));
                Assert.AreEqual(BridgeProblem.None, problem, $"{l.bridge.DoorName}: {Problems(airport, st)}");
            }
            Assert.IsFalse(layouts[0].Crosses(poses[0], layouts[1], poses[1]), "the two tunnels don't cross");
            // up to the wide-body's 4.7 m sill
            Assert.Greater(poses[0].cabFloor, JetBridgeRules.RotundaFloor);
        }

        [Test]
        public void CodeFStand_L1AndL2_DockEveryWideBody()
        {
            // the L2 bridge is placed for the largest type (the double-deck), and must still reach the other wide-bodies'
            // L2 just ahead of their swept wing roots (AircraftType.wingOutline, not a span-wide box)
            var (airport, stands, _) = TestAirport(SizeCode.F);
            var st = stands[0];
            var doors = new[] { new JetBridge { door = 1 }, new JetBridge { door = 2 } };
            Assert.AreEqual(2, airport.Bridges.SetBridges(st, doors), Problems(airport, st));
            foreach (var model in new[] { "Widebody", "DoubleDeck", "Quadjet" })
                foreach (var b in st.bridges)
                {
                    var p = airport.Bridges.Check(st, b).Single(c => c.type != null && c.type.model == model).problem;
                    Assert.AreEqual(BridgeProblem.None, p, $"{model} at {b.DoorName}: {Problems(airport, st)}");
                }
        }

        [Test]
        public void WingOutline_KeepsATunnelOffTheWingButNotOffTheDoorAhead()
        {
            var jet = Type("Quadjet");
            var wings = BridgeLayout.Wings(Vector2.zero, Vector2.up, jet);
            Assert.IsNotNull(wings);
            Assert.IsTrue(BridgeLayout.TryDoor(jet, DoorSide.Left, false, 2, out var l2));
            Assert.IsTrue(BridgeLayout.TryDoor(jet, DoorSide.Left, false, 3, out var l3));
            // a tunnel square out from the door, 3 m wide, 8 m long
            Vector2[] Tunnel(AircraftType.ServicePoint d)
            {
                float x = d.position.x, z = d.position.z;
                return new[] { new Vector2(x - 8f, z - 1.5f), new Vector2(x, z - 1.5f), new Vector2(x, z + 1.5f), new Vector2(x - 8f, z + 1.5f) };
            }
            Assert.IsFalse(wings.Any(w => Geo2.Overlap(Tunnel(l2), w)), "L2 is ahead of the wing root");
            Assert.IsTrue(wings.Any(w => Geo2.Overlap(Tunnel(l3), w)), "L3 is over the wing");
        }

        [Test]
        public void RightHandBridge_DocksAtR1_OnTheOtherSide()
        {
            var (airport, stands, _) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(2, airport.Bridges.SetBridges(st, new[] { new JetBridge { door = 1 }, new JetBridge { door = 1, side = DoorSide.Right } }),
                Problems(airport, st));
            var sl = airport.Taxiways.Layout(st);
            var jet = Type("Narrowbody");
            var left = BridgeLayout.Of(st.bridges[0], sl, airport.terminals);
            var right = BridgeLayout.Of(st.bridges[1], sl, airport.terminals);
            Assert.AreEqual("R1", right.bridge.DoorName);
            var lp = left.Dock(jet, out var lProblem);
            var rp = right.Dock(jet, out var rProblem);
            Assert.AreEqual(BridgeProblem.None, lProblem, Problems(airport, st));
            Assert.AreEqual(BridgeProblem.None, rProblem, Problems(airport, st));
            // the two cabs face each other across the fuselage, on either side of the stand's centreline
            Assert.Less(Vector2.Dot(lp.CabHeading, rp.CabHeading), -0.9f);
            var centre = sl.ParkPosition(jet);
            float Side(BridgeLayout l, BridgePose p) => Vector2.Dot(l.rotunda + p.heading * (JetBridgeRules.TunnelStart + p.Run) - centre, Geo2.Right(sl.nose));
            Assert.Less(Side(left, lp), 0f, "L1 is on the aircraft's left");
            Assert.Greater(Side(right, rp), 0f, "R1 is on the aircraft's right");
            Assert.IsFalse(left.Crosses(lp, right, rp));
            Assert.IsFalse(st.head.Exists, "bridges on both sides of the aircraft can't share a head");
        }

        [Test]
        public void UpperDeckBridge_StandsOnARaisedRotunda_AndDocksAtUL1()
        {
            var (airport, stands, _) = TestAirport(SizeCode.F);
            var deck = Type("DoubleDeck");
            var wide = Type("Widebody");
            Assert.IsTrue(BridgeLayout.HasUpperDeck(deck));
            Assert.IsFalse(BridgeLayout.HasUpperDeck(wide));
            Assert.IsTrue(BridgeLayout.TryDoor(deck, DoorSide.Left, true, 1, out var ul1));
            Assert.Greater(ul1.position.y, 7f);
            Assert.IsFalse(BridgeLayout.TryDoor(wide, DoorSide.Left, true, 1, out _), "no upper deck on the wide-body");
            // two bridges: L2 on the main deck, UL1 from the raised rotunda (PlanDoors' choice for a double-deck)
            var plan = airport.Bridges.PlanDoors(stands[0], 2);
            CollectionAssert.AreEqual(new[] { "L2", "UL1" }, plan.Select(b => b.DoorName));
            Assert.AreEqual(2, airport.Bridges.SetBridges(stands[0], plan), Problems(airport, stands[0]));
            var sl = airport.Taxiways.Layout(stands[0]);
            var upper = BridgeLayout.Of(stands[0].bridges.First(b => b.upper), sl, airport.terminals);
            Assert.AreEqual(JetBridgeRules.UpperFloor, upper.bridge.Floor);
            var pose = upper.Dock(deck, out var problem);
            Assert.AreEqual(BridgeProblem.None, problem, Problems(airport, stands[0]));
            Assert.AreEqual(ul1.position.y, pose.cabFloor, 1e-3f);
            Assert.AreEqual(JetBridgeRules.UpperFloor, pose.floor);
            Assert.LessOrEqual(Mathf.Abs(pose.Rise), JetBridgeRules.MaxSlope * pose.Run + 1e-3f, "the raised rotunda keeps it shallow");
            // the main-deck bridge counts its own deck's doors: L2 is the second main-deck door, not UL1
            var l2 = stands[0].bridges.First(b => !b.upper);
            Assert.IsTrue(BridgeLayout.TryDoor(deck, l2, out var l2Door));
            Assert.Less(l2Door.position.y, ul1.position.y - 2f);
            // a type with no upper deck leaves the upper-deck bridge parked
            Assert.AreEqual(BridgeProblem.NoSuchDoor, airport.Bridges.Check(stands[0], upper.bridge).Single(c => c.type == wide).problem);
        }

        [Test]
        public void ThreeBridges_ShareAHead_TheUpperDeckOneLevelWithItsUpperFloor()
        {
            // the double-deck's L1, L2 and UL1 on one head beside the aircraft: a main link from the terminal, a second
            // storey for UL1, every link level, and nothing coming near anything else docked, parked or on the way
            // (JetBridgeRules.Clearance, unless one passes over with Headroom)
            var (airport, stands, _) = TestAirport(SizeCode.F);
            var st = stands[0];
            var deck = Type("DoubleDeck");
            var plan = airport.Bridges.PlanDoors(st, 3);
            CollectionAssert.AreEqual(new[] { "L1", "L2", "UL1" }, plan.Select(b => b.DoorName));
            Assert.AreEqual(3, airport.Bridges.SetBridges(st, plan), Problems(airport, st));
            Assert.IsTrue(st.head.Exists, "the three share a head");
            var head = airport.Bridges.HeadOf(st);
            Assert.IsNotNull(head);
            Assert.IsTrue(head.Connected, "its main link reaches the terminal");
            Assert.IsTrue(head.upper, "a second storey for the upper-deck bridge");
            Assert.That(head.Length, Is.InRange(JetBridgeRules.MinHead, JetBridgeRules.MaxHead));
            Assert.LessOrEqual(head.LinkLength, JetBridgeRules.MaxLink);
            Assert.AreEqual(BridgeProblem.None, head.Placement(airport.Bridges.Site(st)));
            // beside the aircraft, along it
            var sl = airport.Taxiways.Layout(st);
            Assert.Greater(Mathf.Abs(Vector2.Dot(head.Dir, sl.axis)), 0.99f);
            var layouts = st.bridges.Select(b => BridgeLayout.Of(b, sl, airport.terminals)).ToList();
            foreach (var l in layouts)
            {
                Assert.IsNotNull(l.head, $"{l.bridge.DoorName} stands on the head");
                Assert.IsTrue(l.Connected, l.bridge.DoorName);
                Assert.AreEqual(JetBridgeRules.HeadStub, l.LinkLength, 0.05f, $"{l.bridge.DoorName}: a stub link");
                Assert.AreEqual(l.bridge.Floor, l.LinkFloorAt(l.root), 1e-3f, $"{l.bridge.DoorName}'s link is level");
                Assert.AreEqual(BridgeProblem.None, airport.Bridges.Check(st, l.bridge).Single(c => c.type == deck).problem, Problems(airport, st));
            }
            var poses = layouts.Select(l => l.Dock(deck, out _)).ToList();
            for (int i = 0; i < layouts.Count; i++)
            {
                Assert.IsFalse(layouts[i].Shape(poses[i]).Clashes(head.Shape), $"{layouts[i].bridge.DoorName} docked clears the head");
                for (int j = i + 1; j < layouts.Count; j++)
                    Assert.IsFalse(layouts[i].Crosses(poses[i], layouts[j], poses[j]), $"{layouts[i].bridge.DoorName} and {layouts[j].bridge.DoorName} docked");
            }
            // every wide-body docks at each main-deck bridge
            foreach (var model in new[] { "Widebody", "Quadjet" })
                foreach (var l in layouts.Where(l => !l.bridge.upper))
                    Assert.AreEqual(BridgeProblem.None, airport.Bridges.Check(st, l.bridge).Single(c => c.type != null && c.type.model == model).problem,
                        $"{model} at {l.bridge.DoorName}: {Problems(airport, st)}");
        }

        [Test]
        public void LoneUpperDeckBridge_RampsUpFromTheTerminalsFloor()
        {
            // on its own (no head), an upper-deck bridge's link leaves the terminal at the main floor and ramps up
            var (airport, stands, _) = TestAirport(SizeCode.F);
            var st = stands[0];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, new[] { new JetBridge { door = 1, upper = true } }), Problems(airport, st));
            Assert.IsFalse(st.head.Exists);
            var sl = airport.Taxiways.Layout(st);
            var upper = BridgeLayout.Of(st.bridges[0], sl, airport.terminals);
            Assert.IsTrue(upper.Connected);
            Assert.IsNull(upper.head);
            Assert.AreEqual(JetBridgeRules.RotundaFloor, upper.LinkFloorAt(upper.root), 1e-3f, "leaves the terminal at the main floor");
            Assert.AreEqual(JetBridgeRules.UpperFloor, upper.LinkFloorAt(upper.rotunda), 1e-3f, "meets its rotunda's floor");
            Assert.GreaterOrEqual(upper.LinkLength, JetBridgeRules.MinLink(JetBridgeRules.UpperFloor) - 1e-3f);
            Assert.LessOrEqual(upper.LinkSlope, JetBridgeRules.MaxSlope + 1e-4f);
            Assert.AreEqual(BridgeProblem.None, airport.Bridges.Check(st, upper.bridge).Single(c => c.type != null && c.type.model == "DoubleDeck").problem,
                Problems(airport, st));
            // too short a link can't ramp up
            var shortLink = upper.bridge.Clone();
            shortLink.rotunda = shortLink.root + (shortLink.rotunda - shortLink.root).normalized * (JetBridgeRules.RotundaRadius + 20f);
            Assert.AreEqual(BridgeProblem.LinkTooSteep, BridgeLayout.Of(shortLink, sl, airport.terminals).Placement(airport.Bridges.Site(st)));
        }

        [Test]
        public void BridgeHead_ColumnsStandOffRoads_AndStubLinksJoinIt()
        {
            var stand = new StandLayout { stand = new Stand { id = 4 }, entry = Vector2.zero, axis = Vector2.up, nose = Vector2.up };
            // a 30 m head along +y from y = 20, its main link 20 m from a terminal wall at y = 0, with a 7 m road across
            // the head where an evenly spaced pair of columns would stand (y = 35)
            var head = HeadLayout.Of(new BridgeHead { from = new Vector2(0f, 20f), to = new Vector2(0f, 50f), root = Vector2.zero },
                stand, null, false);
            Assert.AreEqual(30f, head.Length, 1e-3f);
            Assert.AreEqual(20f, head.LinkLength, 1e-3f);
            Assert.IsTrue(head.Columns(null));
            Assert.IsNull(head.columns, "evenly spaced, not kept, with no pavement given");
            var even = new HeadLayout { head = head.head, stand = stand, a = head.a, b = head.b, root = head.root };
            Assert.IsTrue(even.Columns(new List<Vector2[]>()));
            Assert.AreEqual(6, even.columns.Count, "three pairs");
            Assert.IsTrue(even.columns.Any(c => Mathf.Abs(c.y - 35f) < 0.5f), "one pair halfway");
            var road = new[] { new Vector2(-50f, 31.5f), new Vector2(50f, 31.5f), new Vector2(50f, 38.5f), new Vector2(-50f, 38.5f) };
            var off = new HeadLayout { head = head.head, stand = stand, a = head.a, b = head.b, root = head.root };
            Assert.IsTrue(off.Columns(new List<Vector2[]> { road }));
            foreach (var c in off.columns)
                Assert.IsTrue(c.y < 31.5f - JetBridgeRules.ColumnFoot || c.y > 38.5f + JetBridgeRules.ColumnFoot, $"column at {c.y:0.0} m is on the road");
            Assert.LessOrEqual(off.columns.Min(c => c.y) - 20f, JetBridgeRules.HeadOverhang + 1e-3f);
            Assert.LessOrEqual(50f - off.columns.Max(c => c.y), JetBridgeRules.HeadOverhang + 1e-3f);
            // a bridge on its side: its stub link starts on the head's wall, is part of the head (no clash), and its
            // tunnel run into the head would clash
            var (rot, root) = off.Spot(15f, 1f);
            Assert.AreEqual(JetBridgeRules.HeadWidth * 0.5f, Vector2.Distance(root, off.a + off.Dir * 15f), 1e-3f);
            var bridge = new JetBridge { rotunda = rot, root = root };
            var l = BridgeLayout.Of(bridge, stand, null, off);
            Assert.AreSame(off, l.head);
            Assert.AreEqual(JetBridgeRules.HeadStub, l.LinkLength, 1e-3f);
            var away = new BridgePose { heading = Geo2.Right(off.Dir), length = 20f, floor = 4f, cabFloor = 4f };
            Assert.IsFalse(l.Shape(away).Clashes(off.Shape), "pointing away, only its stub touches the head");
            var into = away;
            into.heading = -away.heading;
            Assert.IsTrue(l.Shape(into).Clashes(off.Shape), "swung into the head");
        }

        [Test]
        public void Bridges_KeepClearOnOneLevel_AndPassOverOnlyWithHeadroom()
        {
            // a main-deck bridge with its link along +y and its tunnel out along +x
            var main = new BridgeLayout { bridge = new JetBridge(), rotunda = new Vector2(0f, 0f), root = new Vector2(0f, -12f) };
            var mainPose = new BridgePose { heading = Vector2.right, length = 20f, floor = 4f, cabFloor = 4.5f };
            // a tunnel crossing the main bridge's link 6 m from its rotunda: on the raised rotunda it passes over
            BridgeLayout Crossing(bool upper) => new BridgeLayout
            {
                bridge = new JetBridge { upper = upper }, rotunda = new Vector2(-14f, -6f), root = new Vector2(-14f, -60f),
            };
            var high = Crossing(true);
            var highPose = new BridgePose { heading = Vector2.right, length = 22f, floor = JetBridgeRules.UpperFloor, cabFloor = 7.87f };
            Assert.IsFalse(high.Crosses(highPose, main, mainPose), "the upper-deck tunnel passes over the main-deck link");
            Assert.IsFalse(main.Crosses(mainPose, high, highPose));
            var low = Crossing(false);
            var lowPose = highPose;
            lowPose.floor = JetBridgeRules.RotundaFloor;
            lowPose.cabFloor = 4.5f;
            Assert.IsTrue(low.Crosses(lowPose, main, mainPose), "on one level they'd run into each other");
            // pulled lower, the upper tunnel no longer clears the link's roof by Headroom
            var lowered = highPose;
            lowered.floor = lowered.cabFloor = JetBridgeRules.RotundaFloor + JetBridgeRules.LinkTop - JetBridgeRules.TunnelBottom + 0.1f;
            Assert.IsTrue(high.Crosses(lowered, main, mainPose), "less than Headroom over the link");
            // side by side on one level, tunnels running past each other from rotundas at opposite ends: they keep
            // JetBridgeRules.Clearance apart (the cab, a little wider than the tunnel, is the nearest part)
            var level = new BridgePose { heading = Vector2.right, length = 30f, floor = 4f, cabFloor = 4f };
            var west = new BridgeLayout { bridge = new JetBridge(), rotunda = new Vector2(20f, 0f), root = new Vector2(20f, 0f) };
            var westPose = level;
            westPose.heading = Vector2.left;
            BridgeLayout East(float y) => new BridgeLayout { bridge = new JetBridge(), rotunda = new Vector2(-20f, y), root = new Vector2(-20f, y) };
            float nearest = JetBridgeRules.CabWidth * 0.5f + JetBridgeRules.TunnelWidth * 0.5f + 0.05f;
            Assert.IsTrue(East(nearest + JetBridgeRules.Clearance - 0.3f).Crosses(level, west, westPose), "closer than Clearance");
            Assert.IsFalse(East(nearest + JetBridgeRules.Clearance + 0.2f).Crosses(level, west, westPose), "Clearance apart");
        }

        [Test]
        public void LinkColumns_StandOffRoads()
        {
            // a 38 m link from a terminal at y = 0 out along +y: two columns, evenly spaced, the first 12.7 m out
            var l = new BridgeLayout { bridge = new JetBridge(), root = Vector2.zero, rotunda = new Vector2(0f, 38f + JetBridgeRules.RotundaRadius) };
            var even = l.Columns(null);
            Assert.AreEqual(2, even.Count);
            Assert.AreEqual(38f / 3f, even[0].y, 1e-3f);
            // a 7 m service road across it under the first column: that column moves off it, the spans still within LinkSpan
            var road = new[] { new Vector2(-50f, 10f), new Vector2(50f, 10f), new Vector2(50f, 17f), new Vector2(-50f, 17f) };
            var cols = l.Columns(new List<Vector2[]> { road });
            Assert.IsNotNull(cols);
            Assert.AreEqual(2, cols.Count);
            foreach (var c in cols)
                Assert.IsTrue(c.y < 10f - JetBridgeRules.ColumnFoot || c.y > 17f + JetBridgeRules.ColumnFoot, $"column at {c.y:0.0} m is on the road");
            float last = 0f;
            foreach (var c in cols.Append(new Vector2(0f, 38f)))
            {
                Assert.LessOrEqual(c.y - last, JetBridgeRules.LinkSpan + 1e-3f);
                last = c.y;
            }
            // a link running along a road has nowhere to put its columns
            var along = new BridgeLayout { bridge = new JetBridge(), root = Vector2.zero, rotunda = new Vector2(0f, 38f + JetBridgeRules.RotundaRadius) };
            Assert.IsNull(along.Columns(new List<Vector2[]> { new[] { new Vector2(-4f, -5f), new Vector2(4f, -5f), new Vector2(4f, 60f), new Vector2(-4f, 60f) } }));
        }

        [Test]
        public void Bridges_NotOnTaxiOutStands_NorWithoutATerminal()
        {
            var (airport, stands, t) = TestAirport();
            var st = stands[0];
            st.exit = StandExit.TaxiOut;
            Assert.AreEqual(0, airport.Bridges.SetBridges(st, 1), "taxi-out stands take none");
            st.exit = StandExit.PushBack;
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            // the terminal goes: the bridge stays, unconnected
            airport.RemoveTerminal(t);
            var l = BridgeLayout.Of(st.bridges[0], airport.Taxiways.Layout(st), airport.terminals);
            Assert.IsFalse(l.Connected);
        }

        // ------------------------------------------------------------------ at work

        [Test]
        public void Bridge_DocksWithAParkedJet_AndPullsBackBeforeThePushBack()
        {
            var (airport, stands, _) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            var b = st.bridges[0];
            airport.Simulate(1f);
            Assert.AreEqual(BridgeState.Parked, airport.Bridges.StateOf(b));

            var jet = ParkOn(airport, st, "Narrowbody");
            float t = RunUntil(airport, () => airport.Bridges.StateOf(b) == BridgeState.Docked, 200f);
            Assert.AreEqual(BridgeState.Docked, airport.Bridges.StateOf(b), $"after {t:0} s");
            Assert.That(t, Is.InRange(AirportBridges.SettleTime + JetBridgeRules.DockTime - 3f, AirportBridges.SettleTime + JetBridgeRules.DockTime + 3f));
            Assert.AreSame(jet, airport.Bridges.DockedTo(b));
            var l = airport.Bridges.Layouts.SelectMany(x => x.bridges).First(x => x.bridge == b);
            var dock = l.Dock(jet.type, out _);
            Assert.AreEqual(dock.length, airport.Bridges.PoseOf(l).length, 0.01f, "out at the door");
            Assert.IsFalse(airport.Bridges.Clear(jet));

            // departing: the tug isn't sent for until the bridge is back
            var flight = airport.Traffic.Depart(jet, out string problem);
            Assert.IsNotNull(flight, problem);
            bool tugEarly = false;
            float back = RunUntil(airport, () => airport.Maintenance.TowOf(jet) != null, 300f,
                () => tugEarly |= airport.Maintenance.TowOf(jet) != null && airport.Bridges.StateOf(b) != BridgeState.Parked);
            Assert.IsFalse(tugEarly, "a tug was sent while the bridge was out");
            Assert.IsNotNull(airport.Maintenance.TowOf(jet), $"{flight.Status} after {back:0} s");
            Assert.GreaterOrEqual(back, JetBridgeRules.DockTime - 2f, "waited for the bridge");
            Assert.AreEqual(BridgeState.Parked, airport.Bridges.StateOf(b));
        }

        [Test]
        public void Bridge_Unconnected_NeverDocks()
        {
            var (airport, stands, t) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1));
            airport.RemoveTerminal(t);
            ParkOn(airport, st, "Narrowbody");
            RunUntil(airport, () => false, 90f);
            Assert.AreEqual(BridgeState.Parked, airport.Bridges.StateOf(st.bridges[0]));
        }

        /// <summary>Geo is internal to the runtime assembly; the few helpers the tests need.</summary>
        static class Geo2
        {
            public static Vector2 Right(Vector2 d) => new Vector2(d.y, -d.x);

            public static float SignedArea(IList<Vector2> p)
            {
                float a = 0f;
                for (int i = 0; i < p.Count; i++) a += p[i].x * p[(i + 1) % p.Count].y - p[(i + 1) % p.Count].x * p[i].y;
                return a * 0.5f;
            }

            /// <summary>Do two convex polygons (counter-clockwise) overlap? (no separating edge)</summary>
            public static bool Overlap(Vector2[] a, Vector2[] b) => !Separated(a, b) && !Separated(b, a);

            static bool Separated(Vector2[] a, Vector2[] b)
            {
                for (int i = 0; i < a.Length; i++)
                {
                    var n = Right(a[(i + 1) % a.Length] - a[i]);
                    if (b.All(p => Vector2.Dot(p - a[i], n) > 0f)) return true;
                }
                return false;
            }
        }
    }
}
