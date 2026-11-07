using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Traffic;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public class ContainerSandboxTests
    {
        ContainerTurnaroundDemo demo;
        ContainerStation Station => demo.station;
        AirportBaggage Baggage => demo.airport.Baggage;

        [SetUp]
        public void Load()
        {
            EditorSceneManager.OpenScene(ContainerTurnaroundSetup.ScenePath, OpenSceneMode.Single);
            demo = Object.FindFirstObjectByType<ContainerTurnaroundDemo>(); demo.enabled = false;
            Station.automaticHandling = true;
            Station.automaticHold = new ContainerHoldConfiguration { aircraft = demo.aircraft.type };
            Station.standIds.Add(demo.standId);
        }
        [TearDown]
        public void Close()
        { if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }

        ContainerTurnaround Job()
        {
            demo.airport.Simulate(.1f);
            var job = Baggage.ContainerJobOf(demo.aircraft);
            Assert.That(job, Is.Not.Null, Station.Problem); return job;
        }
        void Until(ContainerTurnaround job, Func<bool> done)
        {
            for (float elapsed = 0; elapsed < 4000 && !done(); elapsed += .25f) demo.airport.Simulate(.25f);
            Assert.That(done(), Is.True, $"{job.State}: {job.Waiting}; bulk {Baggage.JobOf(demo.aircraft)?.Waiting}");
            var stores = Baggage.Ledger.Batches.Select(b => b.Location).Distinct().ToArray();
            Assert.That(stores, Has.None.Null);
            Assert.That(stores.Sum(s => s.Count), Is.EqualTo(Baggage.Ledger.Count));
            Assert.That(stores.Sum(s => s.Weight), Is.EqualTo(Baggage.Ledger.Weight).Within(.01f));
            var units = Baggage.Containers.Units.ToArray();
            Assert.That(units.Select(u => u.Location), Has.None.Null);
            Assert.That(units.Select(u => u.Location).Distinct().Sum(b => b.Count), Is.EqualTo(units.Length));
        }
        void ConfigureBulk()
        {
            var bulk = demo.bulkStation;
            bulk.automaticHandling = true; bulk.standIds.Add(demo.standId);
            bulk.automaticHold = new BulkHoldConfiguration { aircraft = demo.aircraft.type, cargoPoint = "Cargo_Bulk" };
            bulk.incomingBags = 20; bulk.outgoingBags = 30; Station.bulkStation = bulk;
        }

        [Test]
        public void AutomaticContainers_AndCompanionBulkFinishBeforeDeparture()
        {
            ConfigureBulk(); var job = Job(); var bulk = Baggage.JobOf(demo.aircraft);
            Assert.That(bulk, Is.Not.Null, demo.bulkStation.Problem);
            Assert.That(bulk.DepartureFlight, Is.EqualTo(job.DepartureFlight));
            Assert.That(demo.airport.Maintenance.PushBack(demo.aircraft, out _), Is.Null);
            var departure = demo.airport.Traffic.Depart(demo.aircraft, out var why);
            Assert.That(departure, Is.Not.Null, why);
            Until(job, () => job.Loaded > 0);
            Assert.That(bulk.Acquired, Is.False); Assert.That(departure.Phase, Is.EqualTo(FlightPhase.Ready));
            Until(job, () => departure.Phase == FlightPhase.Taxi);
            Assert.That(job.DepartureReady && bulk.DepartureReady, Is.True);
            Assert.That(job.Unloaded, Is.EqualTo(2)); Assert.That(job.Loaded, Is.EqualTo(3));
            Assert.That(Baggage.Ledger.Count, Is.EqualTo(380)); Assert.That(Baggage.Ledger.Weight, Is.EqualTo(5700));
            Assert.That(job.Fleet.Job, Is.Null); Assert.That(bulk.Fleet.Equipment.Owner, Is.Null);
        }

        [TestCase(500, 0)]
        [TestCase(0, 500)]
        public void InvalidDemand_CreatesNoPartialManifestAndBlocksDeparture(int incoming, int outgoing)
        {
            Station.incomingBags = incoming; Station.outgoingBags = outgoing;
            Assert.That(Baggage.DepartureBlocker(demo.aircraft), Does.Contain("exceeds"));
            Assert.That(Baggage.ContainerJobs, Is.Empty); Assert.That(Baggage.Containers.Units, Is.Empty);
            Assert.That(Baggage.Ledger.Count, Is.Zero);
            Station.incomingBags = 120; Station.outgoingBags = 210; Job();
            Assert.That(Baggage.ContainerJobs.Count, Is.EqualTo(1));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(0f)]
        public void InvalidWeight_CreatesNoBags(float weight)
        {
            Station.kilogramsPerBag = weight;
            Assert.That(Baggage.DepartureBlocker(demo.aircraft), Is.Not.Null);
            Assert.That(Baggage.ContainerJobs, Is.Empty); Assert.That(Baggage.Ledger.Count, Is.Zero);
        }

        [Test]
        public void AutomaticContainers_RequireAssignedStandAircraftAndUldCompatibility()
        {
            Station.standIds.Clear(); demo.airport.Simulate(.1f);
            Assert.That(Baggage.ContainerJobs, Is.Empty); Assert.That(Baggage.DepartureBlocker(demo.aircraft), Is.Null);
            Station.standIds.Add(demo.standId); Station.automaticHold.aircraft = null;
            demo.airport.Simulate(.1f); Assert.That(Baggage.ContainerJobs, Is.Empty);
            Station.automaticHold.aircraft = demo.aircraft.type; Station.automaticHold.acceptedCode = "AKH";
            Assert.That(Baggage.DepartureBlocker(demo.aircraft), Does.Contain("does not accept"));
            Assert.That(Baggage.Containers.Units, Is.Empty);
        }

        [Test]
        public void ArrivingFlight_ReceivesItsContainerManifestOnlyAfterParking()
        {
            Assert.That(Baggage.ContainerFleet(Station).Train, Is.Not.Null,"equipment is already staged before arrival");
            demo.aircraft.transform.position = new Vector3(0, 0, -5000);
            var arrival = demo.airport.Traffic.Arrive(demo.aircraft, out var why);
            Assert.That(arrival, Is.Not.Null, why);
            demo.airport.Simulate(10);
            Assert.That(Baggage.ContainerJobs, Is.Empty);
            for (int i = 0; i < 6000 && Baggage.ContainerJobOf(demo.aircraft) == null; i++) demo.airport.Simulate(.25f);
            Assert.That(Baggage.ContainerJobOf(demo.aircraft), Is.Not.Null, arrival.Status);
            Assert.That(arrival.Finished, Is.True); Assert.That(Baggage.Ledger.Count, Is.EqualTo(330));
        }

        [Test]
        public void Packing_UsesGrossWeightIncludingTareToReduceLoadSize()
        {
            Station.incomingBags = 0; Station.outgoingBags = 150; Station.kilogramsPerBag = 30;
            var job = Job();
            Assert.That(job.Fleet.Dispatch.Count, Is.EqualTo(4));
            Assert.That(job.Fleet.Dispatch.Units.Max(u => u.Contents.Count), Is.EqualTo(49));
            Assert.That(job.Fleet.Dispatch.Units.All(u => u.GrossWeight <= u.Type.container.maxGross), Is.True);
            Assert.That(Baggage.Ledger.Count, Is.EqualTo(150));
        }

        [Test]
        public void FullDispatch_WaitsBeforeCreatingUnitsAndResumesAfterSpaceIsFreed()
        {
            var fleet = Baggage.ContainerFleet(Station);
            for (int i = 0; i < fleet.Dispatch.Capacity; i++) Baggage.Containers.Create("other-" + i, "OTHER", Station.unitType, 70, fleet.Dispatch).Seal();
            Assert.That(Baggage.DepartureBlocker(demo.aircraft), Does.Contain("dispatch is full"));
            Assert.That(Baggage.ContainerJobs, Is.Empty); Assert.That(Baggage.Ledger.Count, Is.Zero);
            foreach (var unit in fleet.Dispatch.Units.ToArray())
                Assert.That(Baggage.Containers.Transfer(unit, fleet.Dispatch, fleet.EmptyYard, out _), Is.True);
            Job(); Assert.That(Baggage.Ledger.Count, Is.EqualTo(330));
        }

        [Test]
        public void ScreeningRelease_AndReclaimPreserveTheAutomaticManifest()
        {
            Station.outgoingScreened = false; var job = Job();
            Until(job, () => job.Waiting != null && job.Waiting.Contains("screening"));
            Assert.That(job.Fleet.Job, Is.SameAs(job)); Assert.That(job.DepartureReady, Is.False);
            Baggage.ScreenDepartures(job); Until(job, () => job.DepartureReady);
            Baggage.SendToReclaim(Station); Baggage.SendToReclaim(Station);
            Assert.That(job.Fleet.ArrivalsBags.Count, Is.Zero); Assert.That(Baggage.Reclaim.Count, Is.EqualTo(120));
            Assert.That(Baggage.Ledger.Count, Is.EqualTo(330)); Assert.That(job.DepartureReady, Is.True);
        }

        [Test]
        public void CancellationAndRetry_ReusesTheClosedAutomaticManifest()
        {
            var job = Job(); Until(job, () => job.State == ContainerTurnaroundState.MainToFront && job.TransferProgress > .3f);
            job.Cancel(); Until(job, () => job.Finished);
            demo.airport.Simulate(1f);
            Assert.That(Baggage.ContainerJobs.Count, Is.EqualTo(1)); Assert.That(Baggage.Ledger.Count, Is.EqualTo(330));
            Assert.That(Baggage.DepartureBlocker(demo.aircraft), Is.Not.Null);
            Assert.That(job.Retry(out var why), Is.True, why); Until(job, () => job.DepartureReady);
            Assert.That(Baggage.ContainerJobs.Count, Is.EqualTo(1));
        }

        [Test]
        public void NextVisit_CreatesFreshDemandOnlyAfterTheAircraftLeavesItsStand()
        {
            Station.incomingBags = 0; Station.outgoingBags = 10;
            var first = Job(); Until(first, () => first.DepartureReady);
            demo.airport.Simulate(2f); Assert.That(Baggage.ContainerJobs.Count, Is.EqualTo(1));
            var position = demo.aircraft.transform.position;
            demo.aircraft.transform.position += Vector3.right * 200f; demo.airport.Simulate(.1f);
            demo.aircraft.transform.position = position;
            var next = Job(); Assert.That(next, Is.Not.SameAs(first));
            Assert.That(Baggage.ContainerJobs.Count, Is.EqualTo(2)); Assert.That(Baggage.Ledger.Count, Is.EqualTo(20));
            Assert.That(first.Hold.Units.Sum(u => u.Contents.Count), Is.EqualTo(10));
        }

        [Test]
        public void CompanionBulk_MustBeEnabledAndServeTheSameAircraftAndStand()
        {
            ConfigureBulk(); demo.bulkStation.standIds.Clear();
            Assert.That(Baggage.DepartureBlocker(demo.aircraft), Does.Contain("companion bulk"));
            Assert.That(Baggage.ContainerJobs, Is.Empty); Assert.That(Baggage.Ledger.Count, Is.Zero);
            demo.bulkStation.standIds.Add(demo.standId); demo.bulkStation.automaticHandling = false;
            Assert.That(Baggage.DepartureBlocker(demo.aircraft), Does.Contain("companion bulk"));
            Assert.That(Baggage.ContainerJobs, Is.Empty);
            demo.bulkStation.automaticHandling = true; Job();
            Assert.That(Baggage.Jobs.Count, Is.EqualTo(1));
        }

        [Test]
        public void CompletedContainers_DoNotHideAFailedBulkManifest()
        {
            ConfigureBulk(); demo.bulkStation.outgoingBags = 10000;
            Station.incomingBags = 0; Station.outgoingBags = 10;
            var job = Job(); Until(job, () => job.DepartureReady);
            Assert.That(Baggage.Jobs, Is.Empty);
            Assert.That(Baggage.DepartureBlocker(demo.aircraft), Does.Contain("exceeds"));
        }

        [Test]
        public void StationControls_MoveIdleEquipmentLockDuringHandlingAndCreateUnassignedStations()
        {
            var camera = Camera.main; var tool = camera.gameObject.AddComponent<DevAirfieldTool>(); tool.airport = demo.airport;
            var controls = camera.gameObject.AddComponent<DevContainerControls>(); controls.station = Station;
            var fleet = Baggage.ContainerFleet(Station); var parking = Station.loaderParking;
            controls.BeginPlacement(BaggagePlacement.Loader);
            controls.Place(demo.airport.transform.TransformPoint(new Vector3(parking.x, 0, parking.y)));
            controls.Turn(BaggagePlacement.Loader, 90);
            Assert.That(Vector2.Angle(fleet.LoaderPose.facing, Vector2.right), Is.LessThan(.01f));
            controls.Turn(BaggagePlacement.Loader, -90);
            var job = Job(); Assert.That(controls.CanConfigure, Is.False);
            controls.BeginPlacement(BaggagePlacement.Loader); controls.Place(Vector3.zero);
            Assert.That(Station.loaderParking, Is.EqualTo(parking));
            controls.AddStation(); var added = controls.Station;
            Assert.That(added, Is.Not.SameAs(Station)); Assert.That(added.automaticHandling, Is.False);
            Assert.That(added.standIds, Is.Empty); Assert.That(added.unitType, Is.SameAs(Station.unitType));
            Assert.That(added.crewPrefab, Is.SameAs(Station.crewPrefab));
            Assert.That(added.automaticHold.aircraft, Is.SameAs(Station.automaticHold.aircraft));
            Assert.That(Baggage.ContainerFleet(added), Is.Null, "unfinished station setup must not create equipment at the airport origin");
        }

        [Test]
        public void SandboxInstaller_IsIdempotentAndPreservesExistingBulkAndGateLayout()
        {
            EditorSceneManager.OpenScene(AirCity.EditorTools.Aircraft.AircraftSetup.SandboxScene, OpenSceneMode.Single);
            var airport = Object.FindFirstObjectByType<Airport>(); int stands = airport.Taxiways.stands.Count;
            int terminals = airport.terminals.Count, bulkStations = airport.Baggage.Stations.Length;
            bool installed=airport.GetComponentsInChildren<ContainerStation>().Any(s=>s.name=="Sandbox container hall");
            var station = ContainerSandboxSetup.Install(airport);
            Assert.That(ContainerSandboxSetup.Install(airport), Is.SameAs(station));
            Assert.That(airport.Taxiways.stands.Count, Is.EqualTo(stands+(installed ? 0 : 1)));
            Assert.That(airport.terminals.Count, Is.EqualTo(terminals)); Assert.That(airport.Baggage.Stations.Length, Is.EqualTo(bulkStations+(installed ? 0 : 1)));
            Assert.That(station.bulkStation.automaticHold.cargoPoint, Is.EqualTo("Cargo_Bulk"));
            Assert.That(station.standIds.Count, Is.EqualTo(1)); Assert.That(station.automaticHandling, Is.True);
        }

        [UnityTest]
        public IEnumerator MainSandbox_StartsAutomaticContainersAndToolbarInPlayMode()
        {
            const string warning = "Releasing render texture that is set to be RenderTexture.active!";
            bool warned = false;
            void LoadingLog(string message, string stack, LogType type) { if (type == LogType.Warning && message == warning) warned = true; }
            Application.logMessageReceived += LoadingLog;
            try { EditorSceneManager.OpenScene(AirCity.EditorTools.Aircraft.AircraftSetup.SandboxScene, OpenSceneMode.Single); }
            finally { Application.logMessageReceived -= LoadingLog; }
            if (warned) LogAssert.Expect(LogType.Warning, warning);
            ContainerSandboxSetup.Install(Object.FindAnyObjectByType<AirCity.Airfield.Airport>());
            yield return new EnterPlayMode(); yield return null;
            var controls = Object.FindFirstObjectByType<DevContainerControls>(); Assert.That(controls, Is.Not.Null);
            var airport = controls.Station.Airport; airport.timeScale = 30;
            for (int i = 0; i < 100 && airport.Baggage.ContainerJobs.Count == 0; i++) yield return null;
            Assert.That(airport.Baggage.ContainerJobs.Count, Is.GreaterThan(0));
            Assert.That(Object.FindObjectsByType<ContainerEquipmentRig>(FindObjectsInactive.Exclude).Length, Is.GreaterThan(0));
            var folds = (HashSet<string>)typeof(DevToolbar).GetField("folded", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            bool wasFolded = folds.Remove("container-setup");
            yield return null; yield return null; // Draw the complete station setup, including its scroll viewport.
            if (wasFolded) folds.Add("container-setup");
            LogAssert.NoUnexpectedReceived(); yield return new ExitPlayMode();
        }
    }
}
