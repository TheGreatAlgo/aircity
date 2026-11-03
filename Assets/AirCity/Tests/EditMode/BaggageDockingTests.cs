using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEditor;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public class BaggageDockingTests
    {
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [TestCase(false)] [TestCase(true)] [Timeout(600000)]
        public void Planner_DocksAtSillAndReversesOutWithContinuousFacing(bool container)
        {
            EditorSceneManager.OpenScene(container ? ContainerTurnaroundSetup.ScenePath : BaggageTurnaroundSetup.ScenePath);
            BaggageServicePlan plan; string reason;
            if (container)
            {
                var demo = Object.FindFirstObjectByType<ContainerTurnaroundDemo>();
                demo.aircraft.type.TryGetPoint("Cargo_Fwd", out var cargo);
                var stand = demo.airport.Taxiways.stands.Find(s => s.id == demo.standId);
                Assert.That(BaggageServicePlan.Build(demo.airport, demo.aircraft, stand, cargo, new BaggageServiceAccess(demo.station), out plan, out reason), Is.True, reason);
            }
            else
            {
                var demo = Object.FindFirstObjectByType<BaggageTurnaroundDemo>();
                demo.aircraft.type.TryGetPoint("Cargo_Fwd", out var cargo);
                var stand = demo.airport.Taxiways.stands.Find(s => s.id == demo.standId);
                Assert.That(BaggageServicePlan.Build(demo.airport, demo.aircraft, stand, cargo, new BaggageServiceAccess(demo.station), out plan, out reason), Is.True, reason);
            }
            var tip = plan.loaderWork.Point(plan.loaderTip);
            Assert.That(Vector2.Dot(plan.loaderWork.facing, BaggageServicePlan.XZ(plan.inward)), Is.EqualTo(1f).Within(.0001f), "Load square to the fuselage.");
            Assert.That(Vector2.Distance(tip, BaggageServicePlan.XZ(plan.door)), Is.EqualTo(.10f).Within(.005f));
            Assert.That(plan.loaderUndock.Reversing(.1f), Is.True);
            plan.loaderUndock.Pose(.1f, out var facing, out _);
            Assert.That(Vector2.Angle(facing, plan.loaderWork.facing), Is.LessThan(.1f));
            var station = container ? null : Object.FindAnyObjectByType<BaggageTurnaroundDemo>()?.station;
            var loader = station != null ? station.loaderType : Object.FindAnyObjectByType<ContainerTurnaroundDemo>().station.loaderType;
            Assert.That(plan.Clear(plan.loaderWork, loader), Is.True);
            var penetration = new BaggageVehiclePose(plan.loaderWork.position + BaggageServicePlan.XZ(plan.inward), plan.loaderWork.facing);
            Assert.That(plan.Clear(penetration, loader), Is.False, "The docking exception must not permit the nose to pass the sill.");
        }

        [TestCase("Cessna172")] [TestCase("Narrowbody")] [TestCase("Widebody")] [TestCase("DoubleDeck")] [TestCase("Quadjet")]
        public void Aircraft_HaveGeneratedClearancePartsAndUsableHoldDepth(string model)
        {
            var type = AssetDatabase.LoadAssetAtPath<AirCity.Aircraft.AircraftType>($"Assets/AirCity/Aircraft/{model}/{model}.aircraft");
            Assert.That(Array.Exists(type.serviceFootprints, p => p.name == "Fuselage" && p.outline.Length >= 3), Is.True);
            Assert.That(Array.Exists(type.serviceFootprints, p => p.name == "Left wing" && p.outline.Length >= 3), Is.True);
            foreach (var door in type.cargoDoors) Assert.That(door.holdDepth, Is.GreaterThan(.4f));
            if (model == "Narrowbody")
            {
                type.TryGetPoint("Cargo_Fwd", out var point);
                Assert.That(type.bounds.max.z - point.position.z, Is.EqualTo(8.55f).Within(.05f));
                foreach (var door in type.cargoDoors) Assert.That(door.holdDepth, Is.GreaterThan(2f));
            }
        }

        [Test, Timeout(600000)]
        public void WidebodyRearBulk_PlanClearsTheElevatedTail()
        {
            EditorSceneManager.OpenScene(ContainerTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<ContainerTurnaroundDemo>();
            demo.aircraft.type.TryGetPoint("Cargo_Bulk", out var cargo);
            var stand = demo.airport.Taxiways.stands.Find(s => s.id == demo.standId);
            Assert.That(BaggageServicePlan.Build(demo.airport, demo.aircraft, stand, cargo,
                new BaggageServiceAccess(demo.bulkStation), out var plan, out var reason), Is.True, reason);
            Assert.That(plan.Clear(plan.loaderWork, demo.bulkStation.loaderType), Is.True);
            Vector2 Centre(string name)
            {
                foreach (var part in demo.aircraft.type.serviceFootprints)
                {
                    if (part.name != name) continue;
                    var centre = Vector2.zero;
                    foreach (var p in part.outline) centre += p;
                    centre /= part.outline.Length;
                    var world = demo.aircraft.transform.TransformPoint(new Vector3(centre.x, 0f, centre.y));
                    return BaggageServicePlan.XZ(demo.airport.transform.InverseTransformPoint(world));
                }
                Assert.Fail("Missing generated part: " + name); return default;
            }
            var facing = BaggageServicePlan.XZ(demo.airport.transform.InverseTransformDirection(demo.aircraft.transform.forward));
            Assert.That(plan.Clear(new BaggageVehiclePose(Centre("Right wing"), facing), demo.bulkStation.tractorType), Is.True,
                "A low tractor fits beneath the wing away from the engine.");
            Assert.That(plan.Clear(new BaggageVehiclePose(Centre("Right engines"), facing), demo.bulkStation.tractorType), Is.False,
                "Separating the wing height must retain the engine obstacle.");
        }

        [Test, Timeout(600000)]
        public void PausedTransferAndCancellation_FinishCurrentBatchThenRetryWithoutLosingBags()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = false; demo.autoDeparture = false; demo.Initialize();
            Assert.That(demo.Problem, Is.Null);
            demo.airport.Simulate(1f); Assert.That(demo.Job.Acquired, Is.True, demo.Job.Waiting);
            void Until(Func<bool> done)
            {
                for (int i = 0; i < 8000 && !done(); i++) demo.airport.Simulate(.25f);
                Assert.That(done(), Is.True, $"{demo.Job.State}: {demo.Job.Waiting}");
            }
            Until(() => demo.Job.State == BaggageTurnaroundState.LoadingHold && demo.Job.TransferSeconds > demo.Job.BagTravelSeconds * .5f);
            var batch = demo.Job.TransferBatch;
            float elapsed = demo.Job.TransferSeconds;
            demo.station.accessBlocked = true; demo.airport.Simulate(3f);
            Assert.That(demo.Job.TransferSeconds, Is.EqualTo(elapsed));
            Assert.That(batch.Location, Is.SameAs(demo.Job.Fleet.Cart));
            demo.Job.Cancel(); demo.airport.Simulate(2f);
            Assert.That(demo.aircraft.CargoDoors.IsOpen(demo.Job.HoldConfiguration.cargoPoint), Is.True);
            demo.station.accessBlocked = false; Until(() => demo.Job.Finished);
            Assert.That(batch.Location, Is.SameAs(demo.Job.Hold));
            Assert.That(demo.Job.State, Is.EqualTo(BaggageTurnaroundState.Cancelled));
            Assert.That(demo.aircraft.CargoDoors.AllClosed, Is.True);
            Assert.That(demo.Job.Retry(out var why), Is.True, why);
            Until(() => demo.Job.Finished);
            Assert.That(demo.Job.DepartureReady, Is.True);
            Assert.That(demo.Job.Hold.Count, Is.EqualTo(60));
            Assert.That(demo.airport.Baggage.Ledger.Count, Is.EqualTo(90));
            Assert.That(demo.airport.Baggage.Ledger.Weight, Is.EqualTo(1350f));
        }
    }
}
