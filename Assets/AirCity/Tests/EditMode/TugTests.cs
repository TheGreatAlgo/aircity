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
    /// <summary>The towbarless tugs and push-backs: the two sizes and what they take, the way off a stand, and a tug
    /// coming from its depot, pushing an aircraft back and going home without touching anything.</summary>
    public class TugTests
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
        /// A runway along z = 0, a parallel taxiway at z = -200 joined to it at both ends, a service road from a depot
        /// south-west of it, and push-back stands (code C) on the taxiway's south side at x = 0, 40.5 and 81.
        /// </summary>
        (Airport airport, MaintenanceDepot depot, List<StandLayout> stands) TugAirport(int smallTugs, int largeTugs)
        {
            AirfieldSetup.CreateMaterials();
            var go = new GameObject("Test airport");
            go.SetActive(false);
            var airport = go.AddComponent<Airport>();
            AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 0f;
            go.SetActive(true);
            spawned.Add(go);

            var r = airport.AddRunway(new Vector3(-1000f, 0f, 0f), new Vector3(1000f, 0f, 0f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0f, z), on);
            int w = Node(-700f, 0f, r), a = Node(-700f, -200f), b = Node(700f, -200f), e = Node(700f, 0f, r);
            foreach (var (x, y) in new[] { (w, a), (a, b), (b, e) }) net.Connect(x, y, 15f, PavementSurface.Asphalt);
            int p = net.SplitEdge(net.edges.First(x => x.a == a && x.b == b), new Vector3(-300f, 0f, -200f));
            net.Connect(Node(-300f, -312f), p, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            airport.aprons.Add(ApronArea.Rect(new Vector2(-300f, -312f), new Vector2(56f, 36f), 0f, PavementSurface.Concrete));
            var stands = new List<StandLayout>();
            foreach (float x in new[] { 0f, 40.5f, 81f })
            {
                var st = net.AddStand(StandKind.Parking, new Vector3(x, 0f, -200f), 180f, SizeCode.C, PavementSurface.Concrete);
                Assert.IsNotNull(st);
                stands.Add(net.Layout(st));
            }
            airport.BuildGround();
            net.Rebuild();

            var depot = airport.AddDepot(new Vector2(-300f, -330f), Vector2.up);
            depot.SetAssets(VehicleSetup.Type("SnowPlough"), VehicleSetup.Type("Mower"), VehicleSetup.Type("Depot"), null, null, null);
            depot.SetTugAssets(VehicleSetup.Type("TugSmall"), VehicleSetup.Type("TugLarge"), null, null);
            Assert.IsNotNull(depot.smallTugType, "TugSmall.vehicle not imported");
            depot.autoDispatch = false;
            depot.SetFleet(0, 0);
            depot.SetTugs(smallTugs, largeTugs);
            return (airport, depot, stands);
        }

        AirCity.Aircraft.Aircraft Place(string model, Vector2 at, Vector2 facing)
        {
            var prefab = AircraftSetup.Prefab(Type(model));
            Assert.IsNotNull(prefab, $"{model} prefab");
            var go = Object.Instantiate(prefab, new Vector3(at.x, 0f, at.y), Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y)));
            spawned.Add(go);
            return go.GetComponent<AirCity.Aircraft.Aircraft>();
        }

        AirCity.Aircraft.Aircraft ParkOn(StandLayout l, string model) => Place(model, l.ParkPosition(Type(model)), l.nose);

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        static AirportMaintenance.Box BoxOf(AirCity.Aircraft.Aircraft a, float inflate = 0f) =>
            AirportMaintenance.Box.Of(XZ(a.transform.position), XZ(a.transform.forward).normalized, a.type.bounds, inflate);

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

        [Test]
        public void Tugs_TwoSizesWithACradleOnTheRearAxle()
        {
            var small = VehicleSetup.Type("TugSmall");
            var large = VehicleSetup.Type("TugLarge");
            Assert.IsNotNull(small);
            Assert.IsNotNull(large);
            var cessna = Type("Cessna172");
            var jet = Type("Narrowbody");
            foreach (var t in new[] { small, large })
            {
                Assert.AreEqual(VehicleKind.Tug, t.kind);
                Assert.IsTrue(t.IsTug && !t.IsBuilding);
                Assert.Less(t.cradle.magnitude, 0.01f, $"{t.model}: the nose wheel stands on the rear axle line");
                Assert.Greater(t.liftHeight, 0.1f);
                Assert.Greater(t.cradleOpening, 1f, "room for a twin nose wheel");
                Assert.AreEqual("Cradle", t.liftPart);
                Assert.AreEqual(4, t.wheels.Length);
                Assert.AreEqual(2, t.wheels.Count(w => w.steerable));
                Assert.IsTrue(t.lights.Any(l => l.type == VehicleLightType.Beacon));
                Assert.IsTrue(t.lights.Any(l => l.type == VehicleLightType.Work && l.direction.z < 0f), "work lights on the cradle");
                Assert.Less(t.workSpeed, 1.5f, "pushes back at walking pace");
                Assert.IsTrue(t.CanTow(jet), $"{t.model} moves an A320-size jet");
                Assert.IsFalse(t.CanTow(cessna), "light aircraft are pushed by hand");
            }
            Assert.That(small.Length, Is.InRange(5.5f, 6.5f));
            Assert.That(large.Length, Is.InRange(9.5f, 10.5f));
            Assert.Greater(large.Width, small.Width + 1f);
            Assert.Greater(large.maxTowMass, 500000f, "an A380");
            Assert.LessOrEqual(small.maxTowMass, 100000f);
            Assert.Less(small.bounds.max.y, 2.05f, "under an A320's nose (its belly is 1.9 m up at the nose wheel, 2.4 m a metre from the tip)");

            foreach (var model in new[] { "TugSmall", "TugLarge" })
            {
                var prefab = VehicleSetup.Prefab(model);
                Assert.IsNotNull(prefab, $"{model}.prefab");
                var v = prefab.GetComponent<Vehicle>();
                Assert.IsNotNull(v);
                Assert.AreEqual(4, v.wheels.Length, $"{model}: wheels found by name");
                Assert.IsNotNull(v.liftPart, $"{model}: the cradle found");
                Assert.IsNotNull(prefab.GetComponent<LODGroup>());
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
                    Assert.AreEqual(1, r.sharedMaterials.Length, $"{model}/{r.name}: one material");
            }
        }

        [Test]
        public void PushBack_StraightBackThenRoundOntoTheTaxiwayFacingTheRunway()
        {
            var (airport, _, stands) = TugAirport(0, 0);
            var net = airport.Taxiways;
            var jet = Type("Narrowbody");
            // a stand at 60 degrees on the north side, leaning east
            var lean = net.AddStand(StandKind.Parking, new Vector3(300f, 0f, -200f), 30f, SizeCode.C, PavementSurface.Concrete);
            net.Rebuild();
            foreach (var l in new[] { stands[1], net.Layout(lean) })
            {
                var start = l.ParkPosition(jet);
                var path = PushBackPath.Plan(net, jet, start, l.nose, false, out string problem);
                Assert.IsNotNull(path, problem);
                Assert.AreEqual(0f, Vector2.Distance(path.start, start), 1e-3f);
                Assert.AreEqual(-200f, path.End.y, 0.3f, $"{l.stand.Name}: ends on the taxiway's centreline");
                // the runway is nearer east (via node b): both face east; the angled one turns the short way, 60 degrees
                Assert.Greater(path.endFacing.x, 0.999f, $"{l.stand.Name}: faces east, to the runway");
                Assert.Less(Vector2.Distance(path.NoseEnd, path.End + Vector2.right * jet.Wheelbase), 0.3f, "nose wheel straight");
                // the steering is wound on and off: never beyond the limit, never a jump
                float last = path.SteeringAt(0f), max = 0f;
                for (float d = 0.5f; d <= path.Length; d += 0.5f)
                {
                    float steer = path.SteeringAt(d);
                    Assert.Less(Mathf.Abs(steer - last), 4f, $"{l.stand.Name}: smooth steering at {d:0.0} m");
                    max = Mathf.Max(max, Mathf.Abs(steer));
                    last = steer;
                }
                Assert.That(max, Is.InRange(15f, jet.maxSteeringAngle), "turns, within the nose gear's limit");
                Assert.AreEqual(0f, path.SteeringAt(path.Length), 1f, "straight at the end");
                // the aircraft goes backwards: its main gear follows behind the nose wheel
                path.Sample(path.Length * 0.5f, out var p, out var heading, out _, out _);
                path.Sample(path.Length * 0.5f + 1f, out var p2, out _, out _, out _);
                Assert.Less(Vector2.Dot(p2 - p, heading), 0f, "rolling backwards");
            }

            var other = PushBackPath.Plan(net, jet, stands[1].ParkPosition(jet), stands[1].nose, true, out _);
            Assert.Less(other.endFacing.x, -0.999f, "the other way: west");
        }

        [Test]
        public void PushBack_TugComesFromTheDepotPushesAndGoesHome()
        {
            var (airport, depot, stands) = TugAirport(1, 1);
            var jet = Type("Narrowbody");
            var west = ParkOn(stands[0], "Narrowbody");
            var mid = ParkOn(stands[1], "Narrowbody");
            var east = ParkOn(stands[2], "Narrowbody");
            var job = airport.Maintenance.PushBack(mid, out string problem);
            Assert.IsNotNull(job, problem);
            var tug = job.Tug;
            Assert.IsNotNull(tug, "a tug is sent at once");
            Assert.AreEqual("TugSmall", tug.type.model, "the smallest tug that can move it");
            Assert.AreSame(job, airport.Maintenance.TowOf(mid));
            Assert.IsNull(airport.Maintenance.PushBack(mid, out problem));
            StringAssert.Contains("already", problem);

            var neighbours = new[] { BoxOf(west), BoxOf(east) };
            var start = XZ(mid.transform.position);
            // the jet's wings, engines and tail: everything more than 3.5 m behind its nose wheel
            var b = jet.bounds;
            var aft = new Bounds();
            aft.SetMinMax(new Vector3(b.min.x, 0f, b.min.z), new Vector3(b.max.x, 1f, jet.Wheelbase - 3.5f));
            float held = 0f, worst = 0f;
            var states = new HashSet<TowState>();
            float took = RunUntil(airport, () => job.State == TowState.Done && tug.state == VehicleTeam.State.Parked, 3600f, () =>
            {
                states.Add(job.State);
                var p = tug.PositionOf(0);
                var me = AirportMaintenance.Box.Of(p, tug.FacingOf(0), tug.type.bounds, 0f);
                foreach (var n in neighbours) Assert.IsFalse(me.Overlaps(n), $"the tug keeps off the jets next door ({tug.Describe()})");
                var jetBox = AirportMaintenance.Box.Of(XZ(mid.transform.position), XZ(mid.transform.forward).normalized, aft, 0f);
                Assert.IsFalse(me.Overlaps(jetBox), $"and off its own jet's wings ({job.State}, {tug.Describe()})");
                if (job.State >= TowState.Lifting && job.State <= TowState.Lowering)
                {
                    // the nose wheel stays in the cradle
                    var noseGear = XZ(mid.transform.TransformPoint(new Vector3(0f, 0f, jet.Wheelbase)));
                    worst = Mathf.Max(worst, Vector2.Distance(noseGear, p));
                    held += 1f;
                }
                if (job.State == TowState.Pushing)
                    Assert.LessOrEqual(Mathf.Abs(mid.GetComponent<AircraftGroundMotion>().steering), 1f);
            });
            Debug.Log($"push-back: {took:0} s, nose wheel held {held:0} s, at most {worst:0.00} m off the cradle");
            Assert.AreEqual(TowState.Done, job.State, tug.Describe());
            Assert.AreEqual(VehicleTeam.State.Parked, tug.state, $"the tug is home ({tug.Describe()})");
            CollectionAssert.IsSubsetOf(new[] { TowState.Coming, TowState.Lifting, TowState.Pushing, TowState.Lowering }, states);
            Assert.Less(worst, 0.2f, "the nose wheel stays in the cradle");
            Assert.Greater(held, 40f, "lift, push, lower");
            Assert.Less(took, 1800f, "within half an hour");

            var end = XZ(mid.transform.position);
            Assert.AreEqual(-200f, end.y, 0.5f, "on the taxiway's centreline");
            Assert.Greater(Mathf.Abs(mid.transform.forward.x), 0.99f, "facing along it");
            Assert.Less(Mathf.Abs(mid.transform.forward.y), 0.01f, "the nose back down");
            Assert.IsFalse(mid.Moving);
            Assert.Greater(Vector2.Distance(start, end), 40f);
            Assert.IsNull(airport.Maintenance.TowOf(mid));
            Assert.AreEqual(XZ(west.transform.position), stands[0].ParkPosition(jet), "the others haven't moved");
            Assert.AreEqual(0, airport.Maintenance.Tows.Count, "done with");
        }

        [Test]
        public void PushBack_TugComesAlongTheRoadAtTheStandsHeads()
        {
            var (airport, _, stands) = TugAirport(1, 0);
            var net = airport.Taxiways;
            // a road along the three stands' heads, on from the depot's road
            var first = stands[0];
            var last = stands[2];
            float headZ = first.At(0f, first.far).y + first.axis.y * RunwayRules.RoadWidth * 0.5f;
            float east = Mathf.Max(last.At(last.x0, last.far).x, last.At(last.x1, last.far).x);
            int join = net.SplitEdge(net.edges.First(e => e.IsRoad), new Vector3(-300f, 0f, headZ));
            net.Connect(join, net.AddNode(new Vector3(east, 0f, headZ)), RunwayRules.RoadWidth, PavementSurface.Asphalt, false,
                RoadClass.Service);
            net.Rebuild();
            foreach (var l in stands) Assert.AreEqual(0, net.RoadAccess(l).First().side, $"{l.stand.Name}: the road at its head");

            var west = ParkOn(stands[0], "Narrowbody");
            var mid = ParkOn(stands[1], "Narrowbody");
            var eastJet = ParkOn(stands[2], "Narrowbody");
            var job = airport.Maintenance.PushBack(mid, out string problem);
            Assert.IsNotNull(job, problem);
            var tug = job.Tug;
            var neighbours = new[] { BoxOf(west), BoxOf(eastJet) };
            var beside = new Vector2(stands[1].entry.x, headZ);
            float nearest = float.MaxValue;
            bool onTaxiway = false;
            RunUntil(airport, () => job.State == TowState.Done && tug.state == VehicleTeam.State.Parked, 3600f, () =>
            {
                var p = tug.PositionOf(0);
                var me = AirportMaintenance.Box.Of(p, tug.FacingOf(0), tug.type.bounds, 0f);
                foreach (var n in neighbours) Assert.IsFalse(me.Overlaps(n), $"the tug keeps off the jets next door ({tug.Describe()})");
                if (job.State > TowState.Coming) return;
                nearest = Mathf.Min(nearest, Vector2.Distance(p, beside));
                onTaxiway |= Mathf.Abs(p.y + 200f) < 7.5f;
            });
            Assert.AreEqual(TowState.Done, job.State, tug.Describe());
            Assert.AreEqual(VehicleTeam.State.Parked, tug.state, $"the tug is home ({tug.Describe()})");
            Assert.Less(nearest, 8f, "it came along the road behind the stand");
            Assert.IsFalse(onTaxiway, "not along the taxiway");
            Assert.AreEqual(-200f, mid.transform.position.z, 0.5f, "pushed back onto the taxiway");
        }

        [Test]
        public void PushBack_TwoAtOnceGoToTheSmallTugThenTheLargeOne()
        {
            // west and east, so neither boxes the other's tug in (aircraft don't taxi away yet)
            var (airport, depot, stands) = TugAirport(1, 1);
            var net = airport.Taxiways;
            var far = net.AddStand(StandKind.Parking, new Vector3(300f, 0f, -200f), 180f, SizeCode.C, PavementSurface.Concrete);
            net.Rebuild();
            var a = ParkOn(stands[0], "Narrowbody");
            var b = ParkOn(net.Layout(far), "Narrowbody");
            var first = airport.Maintenance.PushBack(a, out string problem, otherWay: true);
            Assert.IsNotNull(first, problem);
            Assert.Less(first.path.endFacing.x, -0.99f, "the first faces west, towards the depot");
            var second = airport.Maintenance.PushBack(b, out problem);
            Assert.IsNotNull(second, problem);
            Assert.AreEqual("TugSmall", first.Tug.type.model);
            Assert.AreEqual("TugLarge", second.Tug.type.model, "the small one is busy: the large one goes");
            var tugs = depot.Teams.Where(t => t.type.IsTug).ToList();
            RunUntil(airport, () => first.State == TowState.Done && second.State == TowState.Done &&
                                    tugs.All(t => t.state == VehicleTeam.State.Parked), 3600f, () =>
            {
                // the tugs keep apart, and each keeps off the other's jet
                var boxes = tugs.Select(t => AirportMaintenance.Box.Of(t.PositionOf(0), t.FacingOf(0), t.type.bounds, 0f)).ToList();
                if (!tugs.Any(t => t.state == VehicleTeam.State.Parked))
                    Assert.IsFalse(boxes[0].Overlaps(boxes[1]), $"tugs apart: {string.Join("; ", tugs.Select(t => t.Describe() + " @" + t.PositionOf(0)))}");
            });
            foreach (var job in new[] { first, second })
                Assert.AreEqual(TowState.Done, job.State, string.Join("; ", tugs.Select(t => t.Describe())));
            Assert.IsTrue(tugs.All(t => t.state == VehicleTeam.State.Parked), string.Join("; ", tugs.Select(t => t.Describe())));
            foreach (var j in new[] { a, b })
                Assert.AreEqual(-200f, j.transform.position.z, 0.5f, $"{j.name} on the taxiway");
            var outlineB = AirportMaintenance.Outline(XZ(b.transform.position), XZ(b.transform.forward), b.type, 0f);
            Assert.IsFalse(AirportMaintenance.Outline(XZ(a.transform.position), XZ(a.transform.forward), a.type, 0f)
                .Any(x => outlineB.Any(y => x.Overlaps(y))), "clear of each other");
        }

        [Test]
        public void PushBack_SaysWhyNot()
        {
            var (airport, depot, stands) = TugAirport(1, 0);
            var net = airport.Taxiways;
            var light = net.AddStand(StandKind.Parking, new Vector3(250f, 0f, -200f), 180f, SizeCode.A, PavementSurface.Asphalt);
            var taxiOut = net.AddStand(StandKind.Parking, new Vector3(400f, 0f, -200f), 180f, SizeCode.C, PavementSurface.Concrete,
                StandExit.TaxiOut);
            net.Rebuild();
            var work = airport.Maintenance;

            var cessna = ParkOn(net.Layout(light), "Cessna172");
            Assert.IsNull(work.PushBack(cessna, out string problem));
            StringAssert.Contains("by hand", problem);

            var nose = ParkOn(net.Layout(taxiOut), "Narrowbody");
            Assert.IsNull(work.PushBack(nose, out problem));
            StringAssert.Contains("taxi-out", problem);

            var loose = Place("Narrowbody", new Vector2(0f, -600f), Vector2.up);
            Assert.IsNull(work.PushBack(loose, out problem));
            StringAssert.Contains("isn't on a stand", problem);

            // a jet standing on the taxiway behind the stand
            var jet = ParkOn(stands[1], "Narrowbody");
            var inTheWay = Place("Narrowbody", new Vector2(30f, -200f), Vector2.right);
            Assert.IsNull(work.PushBack(jet, out problem));
            StringAssert.Contains("in the way", problem);
            Object.DestroyImmediate(inTheWay.gameObject);
            Assert.IsNotNull(work.PushBack(jet, out problem), problem);

            // no tugs at all
            depot.SetTugs(0, 0);
            var other = ParkOn(stands[2], "Narrowbody");
            Assert.IsNull(work.PushBack(other, out problem));
            StringAssert.Contains("no tugs", problem);
        }

        [Test]
        public void PushBack_TheAircraftGoingAwaySendsTheTugHome()
        {
            var (airport, _, stands) = TugAirport(1, 0);
            var jet = ParkOn(stands[1], "Narrowbody");
            var job = airport.Maintenance.PushBack(jet, out string problem);
            Assert.IsNotNull(job, problem);
            var tug = job.Tug;
            RunUntil(airport, () => job.State == TowState.Pushing, 1800f);
            Assert.AreEqual(TowState.Pushing, job.State, tug.Describe());
            RunUntil(airport, () => false, 10f);
            Object.DestroyImmediate(jet.gameObject);
            RunUntil(airport, () => tug.state == VehicleTeam.State.Parked, 1800f);
            Assert.AreEqual(VehicleTeam.State.Parked, tug.state, tug.Describe());
            Assert.IsNull(tug.Tow);
            Assert.AreEqual(0, airport.Maintenance.Tows.Count);
        }
    }
}
