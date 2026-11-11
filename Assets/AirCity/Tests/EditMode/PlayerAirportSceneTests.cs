using System.Collections.Generic;
using AirCity.EditorTools.Game;
using AirCity.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirCity.Tests.Game
{
    public class PlayerAirportSceneTests
    {
        [Test]
        public void PlayerPlacedAirportCompletesARealCharterAndResumesAtTheSamePose()
        {
            var scene = SceneManager.GetActiveScene();
            var before = new HashSet<GameObject>(scene.GetRootGameObjects());
            try
            {
                var state = PlayerConstructionReview.Open();
                var game = NewGameSetup.Build(); game.Initialize(state, false);
                Assert.AreEqual(state.layout.runwayA.x - state.Site.centre.x, game.Airport.Runways[0].start.x, .001f);
                Assert.AreEqual(25f, game.Airport.Runways[0].start.z, .001f);
                Assert.IsNotNull(game.Airport.Taxiways.Connection(game.Airport.Taxiways.stands[0]));
                Assert.IsTrue(game.AcceptCharter(), game.Message); game.Advance(20.25f);
                Assert.IsNotNull(game.ActiveFlight);
                var position = game.ActiveFlight.aircraft.transform.position;
                var copy = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(state));
                game.Initialize(copy, false);
                Assert.Less(Vector3.Distance(position, game.ActiveFlight.aircraft.transform.position), .005f);
                for (int i = 0; i < 1800 && copy.charterActive; i++) game.Advance(1f);
                Assert.AreEqual(1, copy.completedCharters, game.Message);
            }
            finally
            {
                foreach (var root in scene.GetRootGameObjects()) if (!before.Contains(root)) Object.DestroyImmediate(root);
            }
        }
    }
}
