using System;
using System.Collections;
using System.Collections.Generic;
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
    public class BaggageTurnaroundTests
    {
        BaggageTurnaroundDemo demo;

        [SetUp]
        public void Load()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath, OpenSceneMode.Single);
            demo = Object.FindFirstObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = false; demo.autoDeparture = false;
        }

        [TearDown]
        public void CloseReviewScene()
        {
            if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        void Initialize() { demo.Initialize(); Assert.That(demo.Problem, Is.Null); }

        void Until(Func<bool> done, float seconds = 1800f)
        {
            for (int i = 0; i < seconds * 4f && !done(); i++)
            {
                demo.airport.Simulate(0.25f); demo.Observe(); Conserved();
            }
            Assert.That(done(), Is.True, $"{demo.Job?.State}: {demo.Job?.Waiting}, {demo.Problem}; {demo.Departure?.Status}");
        }

        void Until(BaggageTurnaroundState state) => Until(() => demo.Job.State == state);

        void Conserved()
        {
            var ledger = demo.airport.Baggage.Ledger;
            var stores = new HashSet<BaggageStore>();
            int count = 0; float weight = 0f;
            foreach (var batch in ledger.Batches)
            { Assert.That(batch.Location, Is.Not.Null); stores.Add(batch.Location); count += batch.Count; weight += batch.Weight; }
            int stored = 0; float storedWeight = 0f;
            foreach (var store in stores) { stored += store.Count; storedWeight += store.Weight; }
            Assert.That(count, Is.EqualTo(ledger.Count)); Assert.That(stored, Is.EqualTo(count));
            Assert.That(weight, Is.EqualTo(ledger.Weight).Within(0.001f)); Assert.That(storedWeight, Is.EqualTo(weight).Within(0.001f));
        }

        [Test]
        public void Turnaround_UnloadsIncomingLoadsDepartureAndReturnsAllEquipment()
        {
            Initialize(); Until(BaggageTurnaroundState.LoadingHold);
            Assert.That(demo.aircraft.CargoDoors.IsOpen(demo.Job.HoldConfiguration.cargoPoint), Is.True);
            Assert.That(demo.Job.Fleet.Arrivals.Count, Is.EqualTo(30));
            Assert.That(demo.Job.Fleet.Cart.Count, Is.EqualTo(60));
            Assert.That(demo.airport.Baggage.Reserved(demo.Job.Stand), Is.True);
            Assert.That(demo.Job.DepartureReady, Is.False);
            Until(() => demo.Job.Finished);
            Assert.That(demo.Job.DepartureReady, Is.True);
            Assert.That(demo.aircraft.CargoDoors.AllClosed, Is.True);
            Assert.That(demo.Job.Hold.Count, Is.EqualTo(60)); Assert.That(demo.Job.Hold.Weight, Is.EqualTo(900f));
            Assert.That(demo.Job.Fleet.Arrivals.Weight, Is.EqualTo(450f)); Assert.That(demo.Job.Fleet.Cart.Count, Is.Zero);
            Assert.That(demo.Job.Fleet.Equipment.Owner, Is.Null);
            Assert.That(demo.airport.Baggage.Reserved(demo.Job.Stand), Is.False);
            Assert.That(Vector2.Distance(demo.Job.Fleet.LoaderPose.position, demo.Job.Plan.loaderPark.position), Is.LessThan(0.01f));
            StandEquipmentLayoutTests.AssertParked(demo.Job.Fleet.Train,StandEquipmentLayout.For(demo.station).trainBay);
            Assert.That(demo.Job.BagTravelSeconds,Is.Zero,"finished visits have no active crew transfer");
            Assert.That(demo.Job.TransferInterval,Is.EqualTo(demo.station.secondsPerBag));
            Assert.That(demo.Job.Fleet.TrainSpeed,Is.Zero,"parked equipment stops its wheels and lights");
            Assert.That(demo.Job.Fleet.LoaderSpeed,Is.Zero);
        }

        [Test]
        public void Traffic_DepartureWaitsBeforeTugDispatchUntilBaggageEquipmentReturns()
        {
            demo.autoDeparture = true; Initialize(); Until(BaggageTurnaroundState.InboundDrive);
            demo.airport.Simulate(1f); demo.Observe();
            Assert.That(demo.Departure, Is.Not.Null); Assert.That(demo.Departure.Phase, Is.EqualTo(FlightPhase.Ready));
            Assert.That(demo.Departure.Waiting, Does.Contain("baggage"));
            Assert.That(demo.ObservedDepartureHold, Is.True);
            Until(() => demo.Job.DepartureReady && demo.Departure.Phase != FlightPhase.Ready);
            Assert.That(demo.Job.Fleet.Equipment.Owner, Is.Null);
            Until(() => demo.Departure.Phase == FlightPhase.Taxi, 400f);
        }

        [Test]
        public void BridgeAndBaggage_MustBothClearBeforePushback()
        {
            var stand = demo.airport.Taxiways.stands.Find(s => s.id == demo.standId);
            var layout = demo.airport.Taxiways.Layout(stand);
            float x0 = float.MaxValue, x1 = float.MinValue, front = float.MinValue;
            foreach (var p in layout.Pad) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); front = Mathf.Max(front, p.y); }
            var terminal = demo.airport.AddTerminal(new[] { new Vector2(x0, front + 12f), new Vector2(x1, front + 12f),
                new Vector2(x1, front + 42f), new Vector2(x0, front + 42f) });
            Assert.That(terminal, Is.Not.Null);
            Assert.That(demo.airport.Bridges.SetBridges(stand, 1), Is.EqualTo(1));
            for (int i = 0; i < 200 && demo.airport.Bridges.Clear(demo.aircraft); i++) demo.airport.Simulate(1f);
            Assert.That(demo.airport.Bridges.Clear(demo.aircraft), Is.False);
            Assert.IsFalse(StandEquipmentLayout.For(demo.station).Valid,"the compact bridge fixture has no room for both staging exit turns");
            demo.station.parkAtStand=false; demo.airport.Baggage.RefreshParking(demo.station);
            Initialize(); demo.autoDeparture = true;
            Until(() => demo.Job.Acquired, 15f);
            Until(() => demo.Job.DepartureReady && demo.Departure != null && demo.Departure.Phase != FlightPhase.Ready);
            Assert.That(demo.airport.Bridges.Clear(demo.aircraft), Is.True);
            Assert.That(demo.Job.Fleet.Equipment.Owner, Is.Null);
        }

        [Test]
        public void Arrival_QueuesHandlingUntilAircraftActuallyParks()
        {
            demo.startWithArrival = true; Initialize();
            demo.airport.Simulate(1f);
            Assert.That(demo.Job.Acquired, Is.False);
            Assert.That(demo.Job.Hold.Count, Is.EqualTo(30));
            Until(() => demo.Job.Finished, 3000f);
            Assert.That(demo.Job.DepartureReady, Is.True);
        }

        [Test]
        public void EquipmentBusy_LeavesBaggageInPlaceAndResumesWhenReleased()
        {
            Initialize(); var owner = new object();
            Assert.That(demo.Job.Fleet.Equipment.Reserve(owner, out _), Is.True);
            demo.airport.Simulate(5f);
            Assert.That(demo.Job.Acquired, Is.False); Assert.That(demo.Job.Hold.Count, Is.EqualTo(30));
            Assert.That(demo.Job.Fleet.Dispatch.Count, Is.EqualTo(60));
            demo.Job.Fleet.Equipment.Release(owner); Until(() => demo.Job.Finished);
            Assert.That(demo.Job.DepartureReady, Is.True);
        }

        [Test]
        public void BlockedAccess_WaitsWithReservedEquipmentAndConservedBaggage()
        {
            Initialize(); Until(BaggageTurnaroundState.InboundDrive);
            demo.station.accessBlocked = true; demo.airport.Simulate(40f);
            Assert.That(demo.Job.Finished, Is.False); Assert.That(demo.Job.Waiting, Does.Contain("blocked"));
            Assert.That(demo.Job.Fleet.Equipment.Owner, Is.SameAs(demo.Job)); Conserved();
            demo.station.accessBlocked = false; Until(() => demo.Job.Finished);
            Assert.That(demo.Job.DepartureReady, Is.True);
        }

        [TestCase(BaggageTurnaroundState.ParkingTrain)]
        [TestCase(BaggageTurnaroundState.LoaderApproach)]
        [TestCase(BaggageTurnaroundState.Docking)]
        [TestCase(BaggageTurnaroundState.Undocking)]
        [TestCase(BaggageTurnaroundState.InboundDrive)]
        [TestCase(BaggageTurnaroundState.UnloadingHold)]
        [TestCase(BaggageTurnaroundState.ArrivalsDrive)]
        [TestCase(BaggageTurnaroundState.CollectingDepartures)]
        [TestCase(BaggageTurnaroundState.OutboundDrive)]
        [TestCase(BaggageTurnaroundState.LoadingHold)]
        [TestCase(BaggageTurnaroundState.Withdrawing)]
        public void Cancellation_RetainsRecordsAndWithdrawsEquipment(BaggageTurnaroundState state)
        {
            Initialize(); Until(state); demo.Job.Cancel();
            Until(() => demo.Job.Finished);
            Assert.That(demo.Job.State, Is.EqualTo(BaggageTurnaroundState.Cancelled));
            Assert.That(demo.aircraft.CargoDoors.AllClosed, Is.True);
            Assert.That(demo.Job.DepartureReady, Is.False); Assert.That(demo.Job.Fleet.Cart.Count, Is.Zero);
            Assert.That(demo.Job.Fleet.Equipment.Owner, Is.Null);
            Assert.That(demo.airport.Baggage.DepartureBlocker(demo.aircraft), Is.Not.Null);
        }

        [Test]
        public void StandRemoval_CancelsWithoutDroppingBatchesOrLeakingReservations()
        {
            Initialize(); Until(BaggageTurnaroundState.UnloadingHold);
            demo.airport.Taxiways.stands.Remove(demo.Job.Stand); demo.airport.Taxiways.Rebuild();
            Until(() => demo.Job.Finished);
            Assert.That(demo.Job.State, Is.EqualTo(BaggageTurnaroundState.Cancelled));
            Assert.That(demo.Job.Fleet.Equipment.Owner, Is.Null);
            Assert.That(demo.airport.Baggage.Reserved(demo.Job.Stand), Is.False);
            Assert.That(demo.airport.Baggage.Ledger.Count, Is.EqualTo(90));
        }

        [Test]
        public void FullArrivalsBay_WaitsUntilSpaceIsAvailable()
        {
            Initialize(); var ledger = demo.airport.Baggage.Ledger;
            var filler = ledger.Add("arrivals-occupied", "AC099", 990, 14850f, true, demo.Job.Fleet.Arrivals);
            Until(BaggageTurnaroundState.UnloadingArrivals); demo.airport.Simulate(30f);
            Assert.That(demo.Job.Waiting, Does.Contain("full")); Assert.That(demo.Job.Fleet.Cart.Count, Is.EqualTo(20));
            var reclaim = ledger.Store("reclaim", 1000, 20000f);
            Assert.That(ledger.Transfer(filler, demo.Job.Fleet.Arrivals, reclaim, out _), Is.True);
            Until(() => demo.Job.Finished); Assert.That(demo.Job.DepartureReady, Is.True);
        }

        [Test]
        public void ReconfiguredStop_CancelsAndReturnsWithoutLosingBaggage()
        {
            Initialize(); Until(BaggageTurnaroundState.InboundDrive);
            demo.Job.Stand.serviceConfigurations.Add(new AirCity.Airfield.StandServiceConfiguration
                { aircraft = demo.aircraft.type, stopOffset = 5f });
            Until(() => demo.Job.Finished);
            Assert.That(demo.Job.State, Is.EqualTo(BaggageTurnaroundState.Cancelled));
            Assert.That(demo.Job.Fleet.Equipment.Owner, Is.Null);
        }

        [Test]
        public void ManifestMustCloseBeforeEquipmentCanBeDispatched()
        {
            var stand = demo.airport.Taxiways.stands.Find(s => s.id == demo.standId);
            var job = demo.airport.Baggage.Request(demo.aircraft, stand, new BulkHoldConfiguration { aircraft = demo.aircraft.type },
                demo.station, "IN", "OUT", out var why);
            Assert.That(job, Is.Not.Null, why); demo.airport.Simulate(10f);
            Assert.That(job.Acquired, Is.False); Assert.That(demo.airport.Baggage.DepartureBlocker(demo.aircraft), Is.Not.Null);
            Assert.That(job.Seal(out _), Is.True);
            for (int i = 0; i < 2000 && !job.Finished; i++) demo.airport.Simulate(0.25f);
            Assert.That(job.DepartureReady, Is.True, job.Waiting);
        }

        [Test]
        public void CancelledHoldCannotBeHiddenByAnEmptyReplacementTurnaround()
        {
            Initialize(); demo.Job.Cancel(); demo.airport.Simulate(0.1f);
            var replacement = demo.airport.Baggage.Request(demo.aircraft, demo.Job.Stand,
                new BulkHoldConfiguration { aircraft = demo.aircraft.type }, demo.station, "NEW-IN", "NEW-OUT", out var why);
            Assert.That(replacement, Is.Null); Assert.That(why, Does.Contain("recover"));
            Assert.That(demo.airport.Baggage.DepartureBlocker(demo.aircraft), Is.Not.Null);
            Assert.That(demo.Job.Hold.Count, Is.EqualTo(30)); Conserved();
        }

        [Test]
        public void BaggageCannotStartAfterPushbackHasAlreadyBeenDispatched()
        {
            var flight = demo.airport.Traffic.Depart(demo.aircraft, out var why);
            Assert.That(flight, Is.Not.Null, why); demo.airport.Simulate(0.25f);
            Assert.That(flight.Phase, Is.Not.EqualTo(FlightPhase.Ready));
            var stand = demo.airport.Taxiways.stands.Find(s => s.id == demo.standId);
            var job = demo.airport.Baggage.Request(demo.aircraft, stand,
                new BulkHoldConfiguration { aircraft = demo.aircraft.type }, demo.station, "IN", "OUT", out why);
            Assert.That(job, Is.Null); Assert.That(why, Does.Contain("already started departure"));
            Assert.That(demo.airport.Baggage.Jobs.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ReviewScene_StartsAndStepsWithTheAirportInPlayMode()
        {
            yield return new EnterPlayMode();
            var review = Object.FindFirstObjectByType<BaggageTurnaroundDemo>();
            yield return null;
            Assert.That(review.Problem, Is.Null); Assert.That(review.Job, Is.Not.Null);
            review.airport.timeScale = 30f;
            for (int i = 0; i < 300 && !review.Job.Acquired; i++) yield return null;
            Assert.That(review.Job.Acquired, Is.True, review.Job.Waiting);
            Assert.That(Object.FindObjectsByType<AirCity.Characters.RampCrewRig>(FindObjectsSortMode.None).Length,Is.GreaterThanOrEqualTo(2));
            LogAssert.NoUnexpectedReceived();
            yield return new ExitPlayMode();
        }
    }
}
