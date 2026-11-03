using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    public class BaggageSandboxTests
    {
        BaggageTurnaroundDemo demo;

        [SetUp]
        public void Load()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath, OpenSceneMode.Single);
            demo = Object.FindFirstObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = demo.autoDeparture = false;
        }

        [TearDown]
        public void Close()
        {
            if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        void Configure(int incoming = 230, int outgoing = 240)
        {
            var station = demo.station;
            station.automaticHandling = true;
            station.automaticHold = new BulkHoldConfiguration { aircraft = demo.aircraft.type, bagCapacity = 400, weightCapacity = 6000f };
            station.standIds.Add(demo.standId); station.incomingBags = incoming; station.outgoingBags = outgoing;
        }

        BaggageTurnaround Job()
        {
            demo.airport.Simulate(0.1f);
            var job = demo.airport.Baggage.JobOf(demo.aircraft);
            Assert.That(job, Is.Not.Null, demo.station.Problem);
            return job;
        }

        void Until(BaggageTurnaround job, Func<bool> done, float step = 0.25f)
        {
            var airport = job.Fleet.Station.Airport;
            for (float elapsed = 0f; elapsed < 2400f && !done(); elapsed += step) airport.Simulate(step);
            Assert.That(done(), Is.True, $"{job.State}: {job.Waiting}");
            Conserved(airport);
        }

        void Conserved(AirCity.Airfield.Airport airport = null)
        {
            var ledger = (airport != null ? airport : demo.airport).Baggage.Ledger;
            var stores = new HashSet<BaggageStore>();
            int count = 0; float weight = 0f;
            foreach (var b in ledger.Batches) { Assert.That(b.Location, Is.Not.Null); stores.Add(b.Location); count += b.Count; weight += b.Weight; }
            Assert.That(count, Is.EqualTo(ledger.Count)); Assert.That(stores.Sum(s => s.Count), Is.EqualTo(count));
            Assert.That(weight, Is.EqualTo(ledger.Weight).Within(0.01f)); Assert.That(stores.Sum(s => s.Weight), Is.EqualTo(weight).Within(0.01f));
        }

        [TestCase(0.05f)]
        [TestCase(0.25f)]
        [TestCase(3f)]
        public void AutomaticHandling_MakesOneManifestAndCompletesMultipleTrips(float step)
        {
            Configure(); var job = Job();
            Until(job, () => job.DepartureReady, step);
            Assert.That(job.ArrivalTrips, Is.EqualTo(3)); Assert.That(job.DepartureTrips, Is.EqualTo(3));
            Assert.That(job.Fleet.Arrivals.Count, Is.EqualTo(230)); Assert.That(job.Hold.Count, Is.EqualTo(240));
            Assert.That(job.Fleet.Cart.Count, Is.Zero); Assert.That(job.Fleet.Equipment.Owner, Is.Null);
            demo.airport.Simulate(30f);
            Assert.That(demo.airport.Baggage.Jobs.Count, Is.EqualTo(1));
            Assert.That(demo.airport.Baggage.Ledger.Count, Is.EqualTo(470));
        }

        [Test]
        public void AutomaticHandling_DepartureAndDirectPushbackWaitForAllTrips()
        {
            Configure();
            Assert.That(demo.airport.Maintenance.PushBack(demo.aircraft, out var why), Is.Null);
            Assert.That(why, Does.Contain("baggage"));
            var job = Job();
            var flight = demo.airport.Traffic.Depart(demo.aircraft, out why);
            Assert.That(flight, Is.Not.Null, why);
            Until(job, () => job.State == BaggageTurnaroundState.ReloadDrive);
            Assert.That(flight.Phase, Is.EqualTo(FlightPhase.Ready));
            Assert.That(demo.airport.Maintenance.TowOf(demo.aircraft), Is.Null);
            Until(job, () => flight.Phase == FlightPhase.Taxi);
            Assert.That(job.DepartureReady, Is.True); Assert.That(job.Fleet.Equipment.Owner, Is.Null);
        }

        [Test]
        public void AutomaticHandling_RespectsCartWeightAsWellAsBagCount()
        {
            Configure(150, 150); demo.station.kilogramsPerBag = 30f;
            var job = Job(); Until(job, () => job.DepartureReady);
            Assert.That(job.ArrivalTrips, Is.EqualTo(3)); Assert.That(job.DepartureTrips, Is.EqualTo(3));
            Assert.That(job.Hold.Weight, Is.EqualTo(4500f));
        }

        [TestCase(BaggageTurnaroundState.UnloadingHold)]
        [TestCase(BaggageTurnaroundState.LoadingHold)]
        [TestCase(BaggageTurnaroundState.ReloadDrive)]
        public void CancellationDuringMultiTrip_RecoversTheSameManifest(BaggageTurnaroundState when)
        {
            Configure(); var job = Job();
            Until(job, () => job.State == when && (when != BaggageTurnaroundState.LoadingHold || job.Hold.Count > 0));
            job.Cancel(); Until(job, () => job.Finished);
            Assert.That(job.DepartureReady, Is.False); Assert.That(job.Fleet.Cart.Count, Is.Zero);
            Assert.That(job.Retry(out var why), Is.True, why);
            Until(job, () => job.DepartureReady);
            Assert.That(job.Hold.Count, Is.EqualTo(240)); Assert.That(job.Fleet.Arrivals.Count, Is.EqualTo(230));
            Assert.That(demo.airport.Baggage.Jobs.Count, Is.EqualTo(1));
        }

        [Test]
        public void AutomaticHandling_InvalidDemandCreatesNoPartialManifestAndBlocksDeparture()
        {
            Configure(30, 450);
            var baggage = demo.airport.Baggage;
            Assert.That(baggage.DepartureBlocker(demo.aircraft), Does.Contain("exceeds"));
            Assert.That(baggage.Jobs.Count, Is.Zero); Assert.That(baggage.Ledger.Count, Is.Zero);
            demo.station.outgoingBags = 240;
            var job = Job(); Until(job, () => job.DepartureReady);
        }

        [Test]
        public void AutomaticHandling_DispatchCapacityWaitsBeforeGeneratingAnyBags()
        {
            Configure(); var baggage = demo.airport.Baggage;
            var fleet = baggage.Fleet(demo.station);
            var filler = baggage.Ledger.Add("other-flight", "OTHER", 900, 13500f, true, fleet.Dispatch);
            demo.airport.Simulate(1f);
            Assert.That(baggage.Jobs.Count, Is.Zero); Assert.That(baggage.Ledger.Count, Is.EqualTo(900));
            Assert.That(baggage.DepartureBlocker(demo.aircraft), Does.Contain("dispatch is full"));
            Assert.That(baggage.Ledger.Transfer(filler, fleet.Dispatch, baggage.Reclaim, out _), Is.True);
            var job = Job(); Until(job, () => job.DepartureReady);
        }

        [Test]
        public void AutomaticHandling_RequiresAssignedStandAndCompatibleAircraft()
        {
            Configure(); demo.station.standIds.Clear(); demo.airport.Simulate(1f);
            Assert.That(demo.airport.Baggage.Jobs.Count, Is.Zero);
            demo.station.standIds.Add(demo.standId); demo.station.automaticHold.aircraft = null;
            demo.airport.Simulate(1f); Assert.That(demo.airport.Baggage.Jobs.Count, Is.Zero);
        }

        [Test]
        public void AutomaticHandling_ArrivingFlightGetsItsManifestAfterParking()
        {
            Configure(120, 180);
            demo.aircraft.transform.position = new Vector3(0f, 0f, -5000f);
            var arrival = demo.airport.Traffic.Arrive(demo.aircraft, out var why);
            Assert.That(arrival, Is.Not.Null, why);
            demo.airport.Simulate(10f);
            Assert.That(demo.airport.Baggage.Jobs.Count, Is.Zero);
            for (int i = 0; i < 6000 && demo.airport.Baggage.JobOf(demo.aircraft) == null; i++) demo.airport.Simulate(0.25f);
            var job = demo.airport.Baggage.JobOf(demo.aircraft);
            Assert.That(job, Is.Not.Null, arrival.Status);
            Assert.That(arrival.Finished, Is.True);
            Until(job, () => job.DepartureReady);
            Assert.That(job.Fleet.Arrivals.Count, Is.EqualTo(120)); Assert.That(job.Hold.Count, Is.EqualTo(180));
        }

        [Test]
        public void Reclaim_FreesArrivalsWithoutDeletingOrDuplicatingBaggage()
        {
            Configure(); var job = Job(); Until(job, () => job.DepartureReady);
            var baggage = demo.airport.Baggage;
            baggage.SendToReclaim(demo.station); baggage.SendToReclaim(demo.station);
            Assert.That(job.Fleet.Arrivals.Count, Is.Zero); Assert.That(baggage.Reclaim.Count, Is.EqualTo(230));
            Assert.That(baggage.Ledger.Count, Is.EqualTo(470)); Conserved();
            Assert.That(job.DepartureReady, Is.True, "reclaim must preserve departure clearance");
        }

        [Test]
        public void MissingManifestBatch_WaitsUntilItsRecordedSourceIsRestored()
        {
            Configure(0, 240); var job = Job(); var baggage = demo.airport.Baggage;
            var batch = baggage.Ledger.Batches.First(b => b.Flight == job.DepartureFlight);
            var exception = baggage.Ledger.Store("Exception bay", 100, 2000f);
            Assert.That(baggage.Ledger.Transfer(batch, job.Fleet.Dispatch, exception, out _), Is.True);
            Until(job, () => job.Waiting != null && job.Waiting.Contains("assigned source"));
            Assert.That(job.Fleet.Equipment.Owner, Is.SameAs(job)); Assert.That(job.DepartureReady, Is.False);
            Assert.That(baggage.Ledger.Transfer(batch, exception, job.Fleet.Dispatch, out _), Is.True);
            Until(job, () => job.DepartureReady);
        }

        [Test]
        public void StationPlacement_UpdatesIdleEquipmentAndLocksDuringHandling()
        {
            var camera = Camera.main;
            var tool = camera.gameObject.AddComponent<DevAirfieldTool>(); tool.airport = demo.airport;
            var controls = camera.gameObject.AddComponent<DevBaggageControls>(); controls.station = demo.station;
            var fleet = demo.airport.Baggage.Fleet(demo.station);
            var parking = demo.station.loaderParking + Vector2.right * 5f;
            controls.BeginPlacement(BaggagePlacement.Loader);
            controls.Place(demo.airport.transform.TransformPoint(new Vector3(parking.x, 0f, parking.y)));
            Assert.That(fleet.LoaderPose.position, Is.EqualTo(parking));
            controls.Turn(BaggagePlacement.Loader, 90f);
            Assert.That(Vector2.Angle(fleet.LoaderPose.facing, Vector2.right), Is.LessThan(0.01f));
            Configure(); var job = Job();
            Assert.That(controls.CanConfigure, Is.False);
            controls.BeginPlacement(BaggagePlacement.Dispatch);
            controls.Place(Vector3.zero);
            Assert.That(demo.station.loaderParking, Is.EqualTo(parking));
            job.Cancel(); Until(job, () => job.Finished);
            Assert.That(controls.CanConfigure, Is.True);
        }

        [Test]
        public void SandboxInstaller_PreservesAirportAndCreatesOneUsableStation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var airport = AirCity.EditorTools.Airfield.AirfieldSetup.BuildSandboxAirport();
            var oldStandCount = airport.Taxiways.stands.Count;
            var station = BaggageSandboxSetup.Install(airport);
            Assert.That(BaggageSandboxSetup.Install(airport), Is.SameAs(station));
            Assert.That(airport.Taxiways.stands.Count, Is.EqualTo(oldStandCount + 1));
            var plane = airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>().Single(a => a.name == "Narrowbody baggage turnaround");
            airport.Simulate(0.1f);
            var job = airport.Baggage.JobOf(plane);
            Assert.That(job, Is.Not.Null, station.Problem);
            Until(job, () => job.DepartureReady);
            Assert.That(job.ArrivalTrips, Is.EqualTo(2)); Assert.That(job.DepartureTrips, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SandboxScene_AutomaticHandlingAndToolbarStartInPlayMode()
        {
            // Unity's native editor can warn while replacing its render target during this large scene load.
            // Account for that exact editor-only warning when present; runtime logs remain strict.
            const string renderTargetWarning = "Releasing render texture that is set to be RenderTexture.active!";
            bool warned = false;
            void LoadingLog(string message, string stack, LogType type) { if (type == LogType.Warning && message == renderTargetWarning) warned = true; }
            Application.logMessageReceived += LoadingLog;
            try { EditorSceneManager.OpenScene(AirCity.EditorTools.Aircraft.AircraftSetup.SandboxScene, OpenSceneMode.Single); }
            finally { Application.logMessageReceived -= LoadingLog; }
            if (warned) LogAssert.Expect(LogType.Warning, renderTargetWarning);
            yield return new EnterPlayMode(); yield return null;
            var controls = Object.FindFirstObjectByType<DevBaggageControls>();
            Assert.That(controls, Is.Not.Null);
            var airport = controls.Station.Airport; airport.timeScale = 30f;
            for (int i = 0; i < 100 && !airport.Baggage.Active; i++) yield return null;
            Assert.That(airport.Baggage.Active, Is.True);
            LogAssert.NoUnexpectedReceived();
            yield return new ExitPlayMode();
        }
    }
}
