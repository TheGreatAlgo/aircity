using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.Traffic;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirCity.Tests.Game
{
    public class CareerSceneTests
    {
        Scene scene;
        HashSet<GameObject> originalRoots;
        CareerGame game;
        [SetUp]
        public void Setup()
        {
            scene = SceneManager.GetActiveScene();
            originalRoots = new HashSet<GameObject>(scene.GetRootGameObjects());
            game = NewGameSetup.Build();
        }
        [TearDown]
        public void Cleanup()
        {
            if (scene.IsValid())
                foreach (var root in scene.GetRootGameObjects())
                    if (!originalRoots.Contains(root)) Object.DestroyImmediate(root);
        }

        [TestCase(0, "approach")]
        [TestCase(0, "boarding")]
        [TestCase(0, "departure")]
        [TestCase(1, "departure")]
        [TestCase(2, "departure")]
        public void RealCharterResumesAtTheSamePoseAndPaysOnlyAfterDeparture(int site, string checkpoint)
        {
            var state = CareerState.New(19770616, false); Assert.IsTrue(state.Build(out _, site));
            while (!state.IsOpen) Assert.IsTrue(state.Build(out string why), why);
            const float idleSeconds = 125.5f;
            state.operatingSeconds = idleSeconds;
            game.Initialize(state, false);
            Assert.IsNotNull(game.Airport.Taxiways.Connection(game.Airport.Taxiways.stands[0]));
            int money = state.funds;
            Assert.IsTrue(game.AcceptCharter(), game.Message);
            if (checkpoint == "approach") game.Advance(20.25f);
            else
            {
                for (int i = 0; i < 1200; i++)
                {
                    game.Advance(.25f);
                    Assert.IsNotNull(game.ActiveFlight, game.Message);
                    if (checkpoint == "boarding" && game.ActiveFlight.Phase == FlightPhase.Parked) break;
                    if (checkpoint == "departure" && game.ActiveFlight.kind == FlightKind.Departure && game.ActiveFlight.Phase == FlightPhase.Taxi) break;
                }
                if (checkpoint == "boarding") { Assert.AreEqual(FlightPhase.Parked, game.ActiveFlight.Phase); game.Advance(7.25f); }
                else { Assert.AreEqual(FlightKind.Departure, game.ActiveFlight.kind); Assert.AreEqual(FlightPhase.Taxi, game.ActiveFlight.Phase); }
            }
            var pose = game.ActiveFlight.aircraft.transform.position;
            var phase = game.ActiveFlight.Phase; var elapsed = state.charterElapsed;
            float operating = state.operatingSeconds, roadClock = state.roadTrafficSeconds;
            var date = CareerClock.Date(operating);
            Assert.AreEqual(idleSeconds + elapsed, operating);
            var resumed = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(CareerSnapshot.Capture(state)));
            game.Initialize(resumed, false);
            Assert.AreEqual(phase, game.ActiveFlight.Phase);
            Assert.That(Vector3.Distance(pose, game.ActiveFlight.aircraft.transform.position), Is.LessThan(.005f));
            Assert.AreEqual(elapsed, resumed.charterElapsed);
            Assert.AreEqual(operating, resumed.operatingSeconds, "Loading must preserve idle time and count the replayed charter only once.");
            Assert.AreEqual(roadClock, resumed.roadTrafficSeconds);
            Assert.AreEqual(date, CareerClock.Date(resumed.operatingSeconds));
            Assert.AreEqual(money - CareerState.CharterCost, resumed.funds);
            for (int i = 0; i < 1800 && resumed.charterActive; i++) game.Advance(1f);
            Assert.AreEqual(1, resumed.completedCharters, game.Message);
            Assert.AreEqual(money + CareerState.CharterFee - CareerState.CharterCost, resumed.funds);
            game.Advance(100f); Assert.AreEqual(1, resumed.completedCharters);
            Assert.IsTrue(resumed.Build(out string problem), problem);
            game.Initialize(resumed, false);
            Assert.AreEqual(1000f, game.Airport.Runways[0].Length);
            Assert.IsTrue(game.AcceptCharter(), game.Message);
            for (int i = 0; i < 1800 && resumed.charterActive; i++) game.Advance(1f);
            Assert.AreEqual(2, resumed.completedCharters, game.Message);
        }
    }
}
