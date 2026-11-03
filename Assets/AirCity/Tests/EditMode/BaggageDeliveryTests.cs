using System;
using System.Collections;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public class BaggageDeliveryTests
    {
        static VehicleType Type(string model) => AssetDatabase.LoadAssetAtPath<VehicleType>($"Assets/AirCity/Vehicles/{model}/{model}.vehicle");

        sealed class DeliveryCase
        {
            public readonly BaggageLedger ledger = new BaggageLedger();
            public readonly BaggageStore dispatch, cart, hold;
            public readonly BaggageEquipmentSet equipment;
            public readonly BaggageDelivery job;
            public bool blocked;

            public DeliveryCase(int cartCapacity = 100, int holdCapacity = 100, bool screened = true, float handling = 0.1f,
                Func<bool, float, bool> operateDoor = null)
            {
                dispatch = ledger.Store("dispatch", 200, 3000f);
                cart = ledger.Store("cart", cartCapacity, 1800f, "AC102", true);
                hold = ledger.Store("hold", holdCapacity, 1500f, "AC102", true);
                for (int i = 0; i < 3; i++) ledger.Add("batch-" + i, "AC102", 10, 150f, screened, dispatch);
                var tractorType = Type("BaggageTractor"); var cartType = Type("BaggageCart");
                equipment = new BaggageEquipmentSet(tractorType, cartType, Type("BeltLoader"));
                var path = Circuit(out float handoff);
                job = new BaggageDelivery("AC102", ledger, dispatch, cart, hold, equipment,
                    new BaggageTrain(tractorType, cartType, path), handoff, handling, (_, __) => !blocked, operateDoor);
            }

            public void Until(BaggageDeliveryState state)
            {
                for (int i = 0; i < 10000 && job.State != state; i++) job.Step(0.05f);
                Assert.That(job.State, Is.EqualTo(state), job.Waiting);
            }

            public void Conserved()
            {
                Assert.That(dispatch.Count + cart.Count + hold.Count, Is.EqualTo(30));
                Assert.That(dispatch.Weight + cart.Weight + hold.Weight, Is.EqualTo(450f).Within(0.001f));
                Assert.That(ledger.Count, Is.EqualTo(30));
                foreach (var batch in ledger.Batches) Assert.That(batch.Location, Is.Not.Null);
            }
        }

        [Test]
        public void Delivery_WaitsForOpeningAndRetainsEquipmentUntilTheHatchCloses()
        {
            float opened = 0f, closed = 0f;
            var c = new DeliveryCase(operateDoor: (open, dt) =>
            {
                if (open) { opened += dt; return opened >= 8f; }
                closed += dt; return closed >= 8f;
            });
            c.Until(BaggageDeliveryState.Delivering);
            c.job.Step(4f);
            Assert.That(c.hold.Count, Is.Zero);
            Assert.That(c.job.Waiting, Does.Contain("opening"));
            c.Until(BaggageDeliveryState.Returning);
            for (int i = 0; i < 10000 && closed == 0f; i++) c.job.Step(0.05f);
            Assert.That(c.job.Finished, Is.False);
            Assert.That(c.equipment.Owner, Is.SameAs(c.job));
            Assert.That(c.job.Waiting, Does.Contain("closing"));
            c.Until(BaggageDeliveryState.Complete);
            Assert.That(c.job.DepartureReady, Is.True);
            Assert.That(c.equipment.Owner, Is.Null);
            c.Conserved();
        }

        static VehiclePath Circuit(out float handoff)
        {
            var path = new VehiclePath(); path.Start(new Vector2(20f, 0f), 3f);
            path.Through(new[] { new Vector2(20f, 20f), new Vector2(0f, 20f), Vector2.zero }, 6f, 3f);
            handoff = path.Length;
            var back = new VehiclePath(); back.Start(Vector2.zero, 3f);
            back.Through(new[] { new Vector2(0f, -20f), new Vector2(20f, -20f), new Vector2(20f, 0f) }, 6f, 3f);
            path.Append(back);
            return path;
        }

        [Test]
        public void Ledger_RefusesDuplicateIdsStaleTransfersAndForeignStores()
        {
            var ledger = new BaggageLedger();
            var a = ledger.Store("a", 100, 1000f); var b = ledger.Store("b", 100, 1000f);
            var bag = ledger.Add("unique", "AC102", 10, 150f, true, a);
            Assert.Throws<ArgumentException>(() => ledger.Add("unique", "AC102", 1, 10f, true, a));
            Assert.That(ledger.Transfer(bag, a, b, out _), Is.True);
            Assert.That(ledger.Transfer(bag, a, b, out _), Is.False);
            Assert.That(ledger.Transfer(bag, b, new BaggageLedger().Store("foreign", 10, 200f), out _), Is.False);
            Assert.That(bag.Location, Is.SameAs(b)); Assert.That(a.Count, Is.Zero); Assert.That(b.Count, Is.EqualTo(10));
            Assert.That(ledger.Transfers, Is.EqualTo(1)); Assert.That(ledger.Weight, Is.EqualTo(150f));
        }

        [Test]
        public void Ledger_RefusesWrongFlightUnscreenedAndOverweightBaggageAtomically()
        {
            var ledger = new BaggageLedger();
            var source = ledger.Store("source", 100, 2000f);
            var hold = ledger.Store("hold", 10, 100f, "AC102", true);
            var bag = ledger.Add("one", "AC102", 10, 150f, false, source);
            Assert.That(ledger.Transfer(bag, source, hold, out _), Is.False);
            ledger.Screen(bag);
            Assert.That(ledger.Transfer(bag, source, hold, out _), Is.False);
            var other = ledger.Store("other flight", 100, 2000f, "AC103", true);
            Assert.That(ledger.Transfer(bag, source, other, out _), Is.False);
            Assert.That(bag.Location, Is.SameAs(source)); Assert.That(hold.Count, Is.Zero);
            Assert.That(source.Weight, Is.EqualTo(150f));
        }

        [Test]
        public void Delivery_LoadsFlightBaggageAndReleasesEquipmentOnlyAfterReturning()
        {
            var c = new DeliveryCase();
            c.ledger.Add("other-flight", "AC103", 5, 75f, true, c.dispatch);
            c.Until(BaggageDeliveryState.Returning);
            Assert.That(c.hold.Count, Is.EqualTo(30)); Assert.That(c.cart.Count, Is.Zero);
            Assert.That(c.dispatch.Count, Is.EqualTo(5));
            Assert.That(c.equipment.Owner, Is.SameAs(c.job));
            Assert.That(c.job.DepartureReady, Is.False);
            c.Until(BaggageDeliveryState.Complete);
            Assert.That(c.equipment.Owner, Is.Null); Assert.That(c.job.DepartureReady, Is.True);
            Assert.That(c.job.Train.Distance, Is.EqualTo(c.job.Train.Path.Length).Within(0.001f));
            Assert.That(c.ledger.Count, Is.EqualTo(35)); Assert.That(c.ledger.Weight, Is.EqualTo(525f));
        }

        [Test]
        public void Delivery_WaitsForExclusiveEquipmentLease()
        {
            var c = new DeliveryCase(); var other = new object();
            Assert.That(c.equipment.Reserve(other, out _), Is.True);
            c.job.Step(10f);
            Assert.That(c.job.State, Is.EqualTo(BaggageDeliveryState.Waiting));
            Assert.That(c.dispatch.Count, Is.EqualTo(30));
            c.equipment.Release(new object()); Assert.That(c.equipment.Owner, Is.SameAs(other));
            c.equipment.Release(other);
            c.Until(BaggageDeliveryState.Complete); c.Conserved();
        }

        [Test]
        public void Delivery_DoesNotReserveEquipmentForOversizedShipment()
        {
            var c = new DeliveryCase(cartCapacity: 20); c.job.Step(10f);
            Assert.That(c.job.State, Is.EqualTo(BaggageDeliveryState.Waiting));
            Assert.That(c.equipment.Owner, Is.Null); Assert.That(c.dispatch.Count, Is.EqualTo(30)); c.Conserved();
        }

        [Test]
        public void Equipment_RefusesAnAircraftTugInPlaceOfABaggageTractor()
        {
            var set = new BaggageEquipmentSet(Type("TugSmall"), Type("BaggageCart"), Type("BeltLoader"));
            Assert.That(set.Reserve(new object(), out _), Is.False); Assert.That(set.Owner, Is.Null);
        }

        [Test]
        public void Delivery_FullDispatchBayRetainsReturnedBagsAndEquipmentLease()
        {
            var c = new DeliveryCase(); c.Until(BaggageDeliveryState.Outbound);
            var filler = c.ledger.Add("other-dispatch-load", "AC103", 180, 2700f, true, c.dispatch);
            c.job.Cancel(); c.job.Step(150f);
            Assert.That(c.job.Finished, Is.False); Assert.That(c.job.Waiting, Does.Contain("full"));
            Assert.That(c.cart.Count, Is.EqualTo(10)); Assert.That(c.equipment.Owner, Is.SameAs(c.job));
            var overflow = c.ledger.Store("overflow", 200, 3000f);
            Assert.That(c.ledger.Transfer(filler, c.dispatch, overflow, out _), Is.True);
            c.Until(BaggageDeliveryState.Cancelled);
            Assert.That(c.dispatch.Count, Is.EqualTo(30)); Assert.That(c.cart.Count, Is.Zero);
            Assert.That(c.equipment.Owner, Is.Null);
            Assert.That(c.dispatch.Count + overflow.Count, Is.EqualTo(c.ledger.Count));
            Assert.That(c.dispatch.Weight + overflow.Weight, Is.EqualTo(c.ledger.Weight).Within(0.001f));
        }

        [Test]
        public void Delivery_ScreeningExceptionWaitsAndResumesWithoutLosingBaggage()
        {
            var c = new DeliveryCase(screened: false); c.job.Step(5f);
            Assert.That(c.job.State, Is.EqualTo(BaggageDeliveryState.Collecting));
            Assert.That(c.cart.Count, Is.Zero); Assert.That(c.job.Waiting, Does.Contain("screening"));
            foreach (var batch in c.ledger.Batches) c.ledger.Screen(batch);
            c.Until(BaggageDeliveryState.Complete); c.Conserved();
        }

        [Test]
        public void Delivery_FullHoldWaitsWithRemainingBaggageInCart()
        {
            var c = new DeliveryCase(holdCapacity: 20);
            c.Until(BaggageDeliveryState.Delivering); c.job.Step(10f);
            Assert.That(c.hold.Count, Is.EqualTo(20)); Assert.That(c.cart.Count, Is.EqualTo(10));
            Assert.That(c.job.Waiting, Does.Contain("full")); Assert.That(c.equipment.Owner, Is.SameAs(c.job));
            c.Conserved(); c.job.Cancel(); c.Until(BaggageDeliveryState.Cancelled);
            Assert.That(c.dispatch.Count, Is.EqualTo(10)); Assert.That(c.hold.Count, Is.EqualTo(20)); c.Conserved();
        }

        [Test]
        public void Delivery_BlockedAccessStopsAndResumes()
        {
            var c = new DeliveryCase(); c.Until(BaggageDeliveryState.Outbound);
            c.job.Step(1f); float at = c.job.Train.Distance; c.blocked = true; c.job.Step(10f);
            Assert.That(c.job.Train.Distance, Is.EqualTo(at));
            Assert.That(c.job.Waiting, Does.Contain("blocked")); Assert.That(c.equipment.Owner, Is.SameAs(c.job));
            c.Conserved(); c.blocked = false; c.Until(BaggageDeliveryState.Complete); c.Conserved();
        }

        [TestCase(BaggageDeliveryState.Waiting)]
        [TestCase(BaggageDeliveryState.Collecting)]
        [TestCase(BaggageDeliveryState.Outbound)]
        [TestCase(BaggageDeliveryState.Delivering)]
        [TestCase(BaggageDeliveryState.Returning)]
        public void Delivery_CancellationPreservesBaggageAndReturnsEquipment(BaggageDeliveryState at)
        {
            var c = new DeliveryCase(); c.Until(at);
            if (at == BaggageDeliveryState.Collecting) c.job.Step(1.1f);
            if (at == BaggageDeliveryState.Delivering) c.job.Step(1.1f);
            c.job.Cancel(); c.job.Cancel();
            c.Until(BaggageDeliveryState.Cancelled); c.Conserved();
            Assert.That(c.cart.Count, Is.Zero); Assert.That(c.equipment.Owner, Is.Null);
            Assert.That(c.job.DepartureReady, Is.False);
        }

        [Test]
        public void Delivery_GameTimeIsIndependentOfFrameSize()
        {
            var a = new DeliveryCase(handling: 0.8f); var b = new DeliveryCase(handling: 0.8f);
            a.job.Step(40f);
            for (int i = 0; i < 400; i++) b.job.Step(0.1f);
            Assert.That(a.job.State, Is.EqualTo(b.job.State));
            Assert.That(a.job.Train.Distance, Is.EqualTo(b.job.Train.Distance).Within(0.001f));
            Assert.That(a.cart.Count, Is.EqualTo(b.cart.Count)); Assert.That(a.hold.Count, Is.EqualTo(b.hold.Count));
            a.Conserved(); b.Conserved();
        }

        [Test]
        public void Train_HitchesStayConnectedAndCartOfftracksThroughBends()
        {
            var train = new BaggageTrain(Type("BaggageTractor"), Type("BaggageCart"), Circuit(out _));
            float maximumSteering = 0f;
            while (train.Distance < train.Path.Length - 0.001f)
            {
                Assert.That(train.Advance(0.25f, null, out var reason), Is.True, reason);
                Assert.That(Vector2.Distance(train.CartHitch, train.TractorHitch), Is.LessThan(0.0001f));
                maximumSteering = Mathf.Max(maximumSteering, Mathf.Abs(train.CartSteering));
            }
            Assert.That(maximumSteering, Is.InRange(10f, BaggageTrain.SteeringLimit));
        }

        [Test]
        public void Train_HeadlessHitchesMatchTheArticulatedPrefab()
        {
            var tractor = Object.Instantiate(VehicleSetup.Prefab("BaggageTractor")).GetComponent<BaggageEquipmentRig>();
            var cart = Object.Instantiate(VehicleSetup.Prefab("BaggageCart")).GetComponent<BaggageEquipmentRig>();
            try
            {
                var train = new BaggageTrain(tractor.type, cart.type, Circuit(out _));
                while (train.Distance < train.Path.Length - 0.001f)
                {
                    Assert.That(train.Advance(0.5f, null, out _), Is.True);
                    void Pose(BaggageEquipmentRig rig, BaggageVehiclePose p) => rig.transform.SetPositionAndRotation(
                        new Vector3(p.position.x, 0f, p.position.y), Quaternion.Euler(0f, BaggageTrain.Bearing(p.facing), 0f));
                    Pose(tractor, train.Tractor); Pose(cart, train.Cart);
                    cart.cartSteering = train.CartSteering; cart.Apply();
                    Assert.That(Vector3.Distance(tractor.RearHitchWorld, cart.FrontHitchWorld), Is.LessThan(0.002f));
                }
            }
            finally { Object.DestroyImmediate(tractor.gameObject); Object.DestroyImmediate(cart.gameObject); }
        }

        [Test]
        public void Train_ClearanceIncludesCartAndRejectsReverseRoutes()
        {
            var train = new BaggageTrain(Type("BaggageTractor"), Type("BaggageCart"), Circuit(out _));
            Assert.That(BaggageRoute.Validate(train, (_, type) => type.kind != VehicleKind.BaggageCart, out _), Is.False);
            var reverse = new VehiclePath(); reverse.Start(Vector2.zero, 2f); reverse.LineTo(Vector2.up * 10f, 2f, reverse: true);
            Assert.Throws<ArgumentException>(() => new BaggageTrain(Type("BaggageTractor"), Type("BaggageCart"), reverse));
        }

        [Test]
        public void Route_UsesServiceRoadsAndRefusesClosedOrNarrowLinks()
        {
            var go = new GameObject("test roads"); var net = go.AddComponent<TaxiwayNetwork>();
            try
            {
                int a = net.AddNode(Vector3.zero), b = net.AddNode(Vector3.forward * 20f);
                var edge = new TaxiwayEdge { id = 1, a = a, b = b, width = 8f, surface = PavementSurface.Concrete };
                net.edges.Add(edge);
                Assert.That(BaggageRoute.Plan(net, a, b, 6f, 3f, 3f, out _, out _), Is.False);
                edge.road = RoadClass.Service;
                Assert.That(BaggageRoute.Plan(net, a, b, 6f, 3f, 3f, out _, out _), Is.True);
                edge.width = 2f;
                Assert.That(BaggageRoute.Plan(net, a, b, 6f, 3f, 3f, out _, out _), Is.False);
                edge.width = 8f; edge.snow = 100f;
                Assert.That(BaggageRoute.Plan(net, a, b, 6f, 3f, 3f, out _, out _), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Hold_RequiresCargoPointCompatibleLoaderAndReachableSill()
        {
            var aircraft = AssetDatabase.LoadAssetAtPath<AircraftType>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.aircraft");
            var config = new BulkHoldConfiguration { aircraft = aircraft };
            Assert.That(config.Resolve(Type("BeltLoader"), out var point, out _), Is.True);
            Assert.That(point.type, Is.EqualTo(ServicePointType.CargoDoor));
            Assert.That(config.Resolve(Type("BaggageTractor"), out _, out _), Is.False);
            config.cargoPoint = "Door_L1";
            Assert.That(config.Resolve(Type("BeltLoader"), out _, out _), Is.False);
            var tall = Object.Instantiate(aircraft);
            try
            {
                tall.points = new[] { new AircraftType.ServicePoint { name = "Cargo_Fwd", type = ServicePointType.CargoDoor,
                    position = Vector3.up * 10f, normal = Vector3.right } };
                config.aircraft = tall; config.cargoPoint = "Cargo_Fwd";
                Assert.That(config.Resolve(Type("BeltLoader"), out _, out _), Is.False);
            }
            finally { Object.DestroyImmediate(tall); }
        }

        [Test]
        public void ReviewScene_RunsTheRealDeliveryAndMatchesVisibleHitches()
        {
            var scene = EditorSceneManager.OpenScene(BaggageDeliverySetup.ScenePath, OpenSceneMode.Additive);
            try
            {
                BaggageDeliveryDemo demo = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.TryGetComponent(out BaggageDeliveryDemo found)) demo = found;
                Assert.That(demo, Is.Not.Null); demo.Restart(); Assert.That(demo.Problem, Is.Null);
                for (int i = 0; i < 2000 && !demo.Delivery.Finished; i++)
                {
                    demo.Advance(0.2f);
                    Assert.That(Vector3.Distance(demo.tractor.RearHitchWorld, demo.cart.FrontHitchWorld), Is.LessThan(0.002f));
                    Assert.That(demo.Dispatch.Count + demo.Cart.Count + demo.Hold.Count, Is.EqualTo(60));
                }
                Assert.That(demo.Delivery.DepartureReady, Is.True, demo.Delivery.Waiting);
                Assert.That(demo.Hold.Count, Is.EqualTo(60)); Assert.That(demo.Hold.Weight, Is.EqualTo(900f));
                Assert.That(demo.Equipment.Owner, Is.Null);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [UnityTest]
        public IEnumerator ReviewScene_StartsMovesAndPausesInPlayMode()
        {
            EditorSceneManager.OpenScene(BaggageDeliverySetup.ScenePath, OpenSceneMode.Single);
            yield return new EnterPlayMode();
            var demo = Object.FindFirstObjectByType<BaggageDeliveryDemo>();
            Assert.That(demo, Is.Not.Null);
            yield return null; // Start builds the ledger and routes.
            Assert.That(demo.Problem, Is.Null);
            demo.secondsPerBag = 0.01f; demo.gameSpeed = 12f; demo.Restart();
            for (int i = 0; i < 300 && demo.Delivery.Train.Distance < 1f; i++) yield return null;
            Assert.That(demo.Delivery.Train.Distance, Is.GreaterThan(0f));
            demo.paused = true;
            float elapsed = demo.Delivery.Elapsed;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(demo.Delivery.Elapsed, Is.EqualTo(elapsed));
            LogAssert.NoUnexpectedReceived();
            yield return new ExitPlayMode();
        }
    }
}
