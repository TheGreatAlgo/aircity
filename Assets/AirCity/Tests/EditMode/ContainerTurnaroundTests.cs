using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Traffic;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public class ContainerTurnaroundTests
    {
        ContainerTurnaroundDemo demo;
        [SetUp]
        public void Load()
        {
            EditorSceneManager.OpenScene(ContainerTurnaroundSetup.ScenePath, OpenSceneMode.Single);
            demo = Object.FindFirstObjectByType<ContainerTurnaroundDemo>(); demo.autoDeparture = false;
        }
        [TearDown]
        public void Close()
        { if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        ContainerTurnaround Initialize(bool bulk = false)
        {
            if (!bulk) demo.bulkStation = null;
            demo.Initialize(); Assert.That(demo.Problem, Is.Null); return demo.Job;
        }
        void Until(ContainerTurnaround job, Func<bool> condition, float step = .25f)
        {
            for (float time = 0; time < 3500 && !condition(); time += step)
            { demo.airport.Simulate(step); demo.Observe(); }
            Assert.That(condition(), Is.True, $"{job.State}: {job.Waiting}; bulk {demo.Bulk?.State}: {demo.Bulk?.Waiting}; {demo.Problem}");
            Conserved();
        }
        void Conserved()
        {
            var baggage = demo.airport.Baggage; var stores = new HashSet<BaggageStore>();
            int count = 0; float weight = 0;
            foreach (var b in baggage.Ledger.Batches) { Assert.That(b.Location, Is.Not.Null); stores.Add(b.Location); count += b.Count; weight += b.Weight; }
            Assert.That(count, Is.EqualTo(baggage.Ledger.Count)); Assert.That(stores.Sum(s => s.Count), Is.EqualTo(count));
            Assert.That(weight, Is.EqualTo(baggage.Ledger.Weight).Within(.01f)); Assert.That(stores.Sum(s => s.Weight), Is.EqualTo(weight).Within(.01f));
            var bays = new HashSet<ContainerBay>(); int units = 0;
            foreach (var unit in baggage.Containers.Units) { Assert.That(unit.Location, Is.Not.Null); bays.Add(unit.Location); units++; }
            Assert.That(bays.Sum(b => b.Count), Is.EqualTo(units));
        }

        [Test]
        public void ContainerLedger_SealedContentsCannotBeAddedRemovedOrMovedTwice()
        {
            var bags = new BaggageLedger(); var ledger = new ContainerLedger(bags);
            var dispatch = ledger.Bay("dispatch", ContainerBayKind.Dispatch, 4, 6352);
            var dolly = ledger.Bay("dolly", ContainerBayKind.Dolly, 1, 1600);
            var source = bags.Store("make-up", 100, 2000);
            var unit = ledger.Create("AKE-1", "OUT", demo.station.unitType, 100, dispatch);
            var batch = bags.Add("bag-1", "OUT", 60, 900, true, source);
            Assert.That(ledger.Pack(unit, batch, source, out _), Is.True); unit.Seal();
            Assert.That(bags.Transfer(batch, unit.Contents, source, out var why), Is.False); Assert.That(why, Does.Contain("sealed"));
            Assert.Throws<ArgumentException>(() => bags.Add("extra", "OUT", 1, 15, true, unit.Contents));
            Assert.That(ledger.Transfer(unit, dispatch, dolly, out why), Is.True, why);
            Assert.That(ledger.Transfer(unit, dispatch, dolly, out _), Is.False);
            Assert.That(batch.Location, Is.SameAs(unit.Contents)); Assert.That(unit.GrossWeight, Is.EqualTo(995));
            Assert.That(bags.Count, Is.EqualTo(60)); Assert.That(ledger.Transfers, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => ledger.Create("AKE-1", "OUT", demo.station.unitType, 100, dispatch));
        }

        [Test]
        public void ContainerLedger_GrossLimitsIncludeTareAndScreeningBlocksHoldEntry()
        {
            var bags = new BaggageLedger(); var ledger = new ContainerLedger(bags);
            var dispatch = ledger.Bay("dispatch", ContainerBayKind.Dispatch, 4, 6352);
            var hold = ledger.Bay("hold", ContainerBayKind.Hold, 1, 1588, "AKE", "OUT");
            var source = bags.Store("make-up", 200, 4000);
            var unit = ledger.Create("AKE-1", "OUT", demo.station.unitType, 100, dispatch);
            var heavy = bags.Add("heavy", "OUT", 100, 1500, true, source);
            Assert.That(ledger.Pack(unit, heavy, source, out _), Is.False);
            Assert.That(unit.Contents.Count, Is.Zero);
            var batch = bags.Add("unscreened", "OUT", 50, 750, false, source);
            Assert.That(ledger.Pack(unit, batch, source, out _), Is.True); unit.Seal();
            Assert.That(ledger.Transfer(unit, dispatch, hold, out var why), Is.False); Assert.That(why, Does.Contain("screening"));
            bags.Screen(batch); Assert.That(ledger.Transfer(unit, dispatch, hold, out why), Is.True, why);
            Assert.That(unit.GrossWeight, Is.EqualTo(845));
        }

        [Test]
        public void ContainerLedger_RepackingWithinOneBayDoesNotCountItsBagsTwice()
        {
            var bags = new BaggageLedger(); var ledger = new ContainerLedger(bags);
            var dispatch = ledger.Bay("make-up", ContainerBayKind.Dispatch, 2, 1300);
            var first = ledger.Create("AKE-1", "OUT", demo.station.unitType, 100, dispatch);
            var second = ledger.Create("AKE-2", "OUT", demo.station.unitType, 100, dispatch);
            var batch = bags.Add("bags", "OUT", 60, 900, true, first.Contents);
            Assert.That(ledger.Pack(second, batch, first.Contents, out var why), Is.True, why);
            Assert.That(dispatch.Weight, Is.EqualTo(1090)); Assert.That(first.Contents.Count, Is.Zero);
            Assert.That(second.Contents.Count, Is.EqualTo(60)); Assert.That(bags.Count, Is.EqualTo(60));
        }

        [Test]
        public void HoldConfiguration_RequiresExplicitCompatibilityOpeningAndLoaderReach()
        {
            var config = new ContainerHoldConfiguration { aircraft = demo.aircraft.type };
            Assert.That(config.Resolve(demo.station.unitType, demo.station.dollyType, demo.station.loaderType, out _, out var why), Is.True, why);
            config.acceptedCode = "AKH";
            Assert.That(config.Resolve(demo.station.unitType, demo.station.dollyType, demo.station.loaderType, out _, out _), Is.False);
            config.acceptedCode = "AKE"; config.cargoPoint = "Cargo_Bulk";
            Assert.That(config.Resolve(demo.station.unitType, demo.station.dollyType, demo.station.loaderType, out _, out why), Is.False);
            Assert.That(why, Does.Contain("opening"));
            config.cargoPoint = "Cargo_Fwd";
            Assert.That(config.Resolve(demo.station.unitType, VehicleSetup.Type("BaggageCart"), demo.station.loaderType, out _, out _), Is.False);
        }

        [Test]
        public void LoaderPlatforms_StayLevelAndLiftArmsKeepTheirLengthsAcrossTheRange()
        {
            var go = Object.Instantiate(demo.station.loaderPrefab);
            try
            {
                var rig = go.GetComponent<ContainerEquipmentRig>();
                foreach (var heights in new[] { (.485f, 1.8f), (2.75f, 2.75f), (3.7f, 3.7f) })
                {
                    rig.mainHeight = heights.Item1; rig.frontHeight = heights.Item2; rig.stabilisers = 1; rig.Apply();
                    var transforms = go.GetComponentsInChildren<Transform>(true);
                    Assert.That(transforms.Single(t => t.name == "MainPlatform").position.y, Is.EqualTo(heights.Item1).Within(.001f));
                    Assert.That(transforms.Single(t => t.name == "FrontPlatform").position.y, Is.EqualTo(heights.Item2).Within(.001f));
                    foreach (var t in transforms.Where(t => t.name.Contains("PlatformLift_") && !t.name.Contains("LOD")))
                        Assert.That(t.localScale, Is.EqualTo(Vector3.one));
                }
                rig.mainHeight = .508f; rig.bridgeExtended = rig.sideGateOpen = true; rig.Apply();
                var bridge = go.GetComponentsInChildren<Transform>(true).Single(t => t.name == "SideBridge");
                Assert.That(bridge.position.x, Is.EqualTo(-3.76f).Within(.002f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase("ContainerLD3")]
        [TestCase("ContainerDolly")]
        [TestCase("ContainerLoader")]
        public void Equipment_MovingPartsHaveThreeDecreasingLods(string model)
        {
            var prefab = VehicleSetup.Prefab(model); Assert.That(prefab, Is.Not.Null);
            var lod = prefab.GetComponent<LODGroup>(); Assert.That(lod, Is.Not.Null);
            var levels = lod.GetLODs(); Assert.That(levels.Length, Is.EqualTo(3));
            long Triangles(LOD level) => level.renderers.Sum(r => (long)(r.GetComponent<MeshFilter>()?.sharedMesh?.GetIndexCount(0) ?? 0));
            Assert.That(Triangles(levels[1]), Is.LessThan(Triangles(levels[0])));
            Assert.That(Triangles(levels[2]), Is.LessThan(Triangles(levels[1])));
        }

        [TestCase(.05f)]
        [TestCase(.7f)]
        public void Turnaround_UnloadsAndReloadsThroughBothPlatformsWithoutLosingBags(float step)
        {
            var job = Initialize(); Until(job, () => job.DepartureReady, step);
            Assert.That(job.Unloaded, Is.EqualTo(2)); Assert.That(job.Loaded, Is.EqualTo(3));
            Assert.That(demo.aircraft.CargoDoors.AllClosed, Is.True);
            Assert.That(job.Fleet.ArrivalsBags.Count, Is.EqualTo(120)); Assert.That(job.Hold.Count, Is.EqualTo(3));
            Assert.That(job.Fleet.EmptyYard.Count, Is.EqualTo(2)); Assert.That(job.Fleet.Job, Is.Null);
            Assert.That(job.Fleet.Dolly.Count + job.Fleet.Main.Count + job.Fleet.Front.Count, Is.Zero);
            Assert.That(job.Fleet.Stabilisers, Is.Zero); Assert.That(demo.airport.Baggage.Ledger.Count, Is.EqualTo(330));
        }

        [Test]
        public void Transfer_BlockedAccessPausesOneVisibleUnitAndRetainsItsSourceAndLease()
        {
            var job = Initialize(); Until(job, () => job.State == ContainerTurnaroundState.MainToFront && job.TransferProgress > .25f);
            var unit = job.TransferUnit; float progress = job.TransferProgress;
            Assert.That(demo.aircraft.CargoDoors.IsOpen(job.Configuration.cargoPoint), Is.True);
            Assert.That(unit.Location, Is.SameAs(job.Fleet.Main));
            demo.station.accessBlocked = true; demo.airport.Simulate(10);
            Assert.That(job.TransferProgress, Is.EqualTo(progress)); Assert.That(unit.Location, Is.SameAs(job.Fleet.Main));
            Assert.That(job.Fleet.Job, Is.SameAs(job)); Assert.That(job.Waiting, Does.Contain("blocked"));
            demo.station.accessBlocked = false; Until(job, () => job.DepartureReady);
        }

        [TestCase(ContainerTurnaroundState.LoaderApproach)]
        [TestCase(ContainerTurnaroundState.ToStand)]
        [TestCase(ContainerTurnaroundState.FrontToMain)]
        [TestCase(ContainerTurnaroundState.MainToDolly)]
        [TestCase(ContainerTurnaroundState.ToArrivals)]
        [TestCase(ContainerTurnaroundState.DollyToMain)]
        [TestCase(ContainerTurnaroundState.RaiseMain)]
        [TestCase(ContainerTurnaroundState.MainToFront)]
        [TestCase(ContainerTurnaroundState.FrontToHold)]
        public void Cancellation_RecoversPlatformLoadsAndRetryResumesTheSameManifest(ContainerTurnaroundState state)
        {
            var job = Initialize();
            bool TransferState() => state == ContainerTurnaroundState.FrontToMain || state == ContainerTurnaroundState.MainToDolly ||
                state == ContainerTurnaroundState.DollyToMain || state == ContainerTurnaroundState.MainToFront || state == ContainerTurnaroundState.FrontToHold;
            Until(job, () => job.State == state && (!TransferState() || job.TransferProgress > .3f));
            job.Cancel(); Until(job, () => job.Finished);
            Assert.That(demo.aircraft.CargoDoors.AllClosed, Is.True);
            Assert.That(job.State, Is.EqualTo(ContainerTurnaroundState.Cancelled)); Assert.That(job.DepartureReady, Is.False);
            Assert.That(job.Fleet.Dolly.Count + job.Fleet.Main.Count + job.Fleet.Front.Count, Is.Zero);
            Assert.That(job.Fleet.Job, Is.Null); Assert.That(demo.airport.Baggage.DepartureBlocker(demo.aircraft), Is.Not.Null);
            Assert.That(job.Retry(out var why), Is.True, why); Until(job, () => job.DepartureReady);
            Assert.That(demo.airport.Baggage.ContainerJobs.Count, Is.EqualTo(1));
        }

        [TestCase(ContainerTurnaroundState.HoldToFront)]
        [TestCase(ContainerTurnaroundState.FrontToHold)]
        public void AircraftRemoval_DuringHoldTransferRecoversEquipmentAndPreservesBaggage(ContainerTurnaroundState state)
        {
            var job = Initialize();
            Until(job, () => job.State == state && job.TransferProgress > .25f);
            Object.DestroyImmediate(demo.aircraft.gameObject);
            Until(job, () => job.Finished);
            Assert.That(job.State, Is.EqualTo(ContainerTurnaroundState.Cancelled));
            Assert.That(job.Fleet.Job, Is.Null);
            Assert.That(job.Fleet.Dolly.Count + job.Fleet.Main.Count + job.Fleet.Front.Count, Is.Zero);
            Assert.That(job.DepartureReady, Is.False);
        }

        [Test]
        public void FullArrivals_WaitsWithoutDroppingTheContainerOrItsRemainingContents()
        {
            var job = Initialize(); var ledger = demo.airport.Baggage.Ledger;
            var filler = ledger.Add("full-arrivals", "OTHER", 1990, 29850, true, job.Fleet.ArrivalsBags);
            Until(job, () => job.Waiting != null && job.Waiting.Contains("full"));
            Assert.That(job.Fleet.Job, Is.SameAs(job)); Assert.That(job.Fleet.Arrivals.Count, Is.EqualTo(1));
            Assert.That(ledger.Transfer(filler, job.Fleet.ArrivalsBags, demo.airport.Baggage.Reclaim, out _), Is.True);
            Until(job, () => job.DepartureReady);
        }

        [Test]
        public void MissingContainer_WaitsForRecoveryInsteadOfCompletingAnEmptyManifest()
        {
            var job = Initialize(); var containers = demo.airport.Baggage.Containers;
            var unit = job.Fleet.Dispatch.Units.First();
            var exception = containers.Bay("exception", ContainerBayKind.Dispatch, 1, 1600);
            Assert.That(containers.Transfer(unit, job.Fleet.Dispatch, exception, out _), Is.True);
            demo.airport.Simulate(10);
            Assert.That(job.Waiting, Does.Contain("manifest location")); Assert.That(job.DepartureReady, Is.False);
            Assert.That(containers.Transfer(unit, exception, job.Fleet.Dispatch, out _), Is.True);
            Until(job, () => job.DepartureReady);
        }

        [Test]
        public void Departure_WaitsForContainerAndBulkEquipmentBeforeDispatchingATug()
        {
            var job = Initialize(true); demo.autoDeparture = true;
            Until(job, () => job.Loaded > 0);
            Assert.That(demo.Departure, Is.Not.Null); Assert.That(demo.Departure.Phase, Is.EqualTo(FlightPhase.Ready));
            Assert.That(demo.airport.Maintenance.TowOf(demo.aircraft), Is.Null);
            Until(job, () => job.DepartureReady && demo.Bulk.DepartureReady && demo.Departure.Phase == FlightPhase.Taxi);
            Assert.That(demo.airport.Baggage.Ledger.Count, Is.EqualTo(380));
        }

        [UnityTest]
        public IEnumerator Review_StartsAndAnimatesTheRealEquipmentInPlayMode()
        {
            yield return new EnterPlayMode(); yield return null;
            var review = Object.FindFirstObjectByType<ContainerTurnaroundDemo>();
            Assert.That(review.Job, Is.Not.Null); Assert.That(review.Problem, Is.Null);
            review.airport.timeScale = 30;
            for (int i = 0; i < 200 && !review.Job.Acquired; i++) yield return null;
            Assert.That(review.Job.Acquired, Is.True, review.Job.Waiting);
            Assert.That(Object.FindObjectsByType<ContainerEquipmentRig>(FindObjectsInactive.Exclude).Length, Is.GreaterThan(0));
            LogAssert.NoUnexpectedReceived(); yield return new ExitPlayMode();
        }
    }
}
