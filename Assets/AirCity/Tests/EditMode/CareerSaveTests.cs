using System;
using System.Collections.Generic;
using System.IO;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Game
{
    public class CareerSaveTests
    {
        string directory;
        CareerSaveStore store;
        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "AirCityQueuedSave-" + Guid.NewGuid().ToString("N"));
            store = new CareerSaveStore(directory);
        }
        [TearDown]
        public void Cleanup()
        {
            store.FlushPending();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void SnapshotCopiesEverySerializedFieldAndAllNestedMutableCollections()
        {
            var state = CareerState.New(42);
            state.saveVersion = 1; state.selectedSite = 1; state.completedBuilds = 3; state.funds = 61324;
            state.completedCharters = 2; state.expanded = true; state.charterActive = true;
            state.charterElapsed = 15.25f; state.operatingSeconds = 30.5f; state.roadTrafficSeconds = 71.75f;
            state.world.disturbance = new float[state.world.heights.Length]; state.world.disturbance[0] = .4f;
            state.accessRoadRoute.Add(new Vector2(21f, 34f));
            state.layout = new CareerLayout {
                unlocked = 255, clearedTrees = 11, earthwork = 123.5f, elevation = 92f,
                runwayBuilt = true, standBuilt = true, receptionBuilt = true,
                runwayA = new Vector2(-21f, 33f), runwayB = new Vector2(825f, 44f), stand = new Vector2(51f, 62f),
                standAlong = Vector2.right, reception = new Vector2(73f, 84f), standHeading = 180f, receptionHeading = 25f,
                taxiways = new List<CareerTaxiway> { new CareerTaxiway { points = new List<Vector2> { Vector2.zero, Vector2.one * 50f } } }
            };
            string expected = JsonUtility.ToJson(state);
            var copy = CareerSnapshot.Capture(state);
            Assert.AreEqual(expected, JsonUtility.ToJson(copy), "Every serialized field, including road identifiers, belongs in a snapshot.");
            copy.world.heights[0] += 1f; copy.world.disturbance[0] = .8f;
            copy.world.trees[0].removed = !copy.world.trees[0].removed; copy.world.trees.Add(new ForestTree());
            copy.world.sites[0].name = "Changed"; copy.world.sites.Add(new AirportSite());
            copy.world.publicRoad[0] = Vector2.zero; copy.world.settlements[0] = Vector2.zero;
            copy.publicRoads.nodes[0].position = Vector3.zero;
            copy.publicRoads.segments[0].style = PublicRoadStyle.Highway;
            copy.accessRoadRoute[0] = Vector2.zero;
            copy.layout.taxiways[0].points[0] = Vector2.one; copy.layout.taxiways.Add(new CareerTaxiway());
            copy.layout.receptionHeading += 1f;
            Assert.AreEqual(expected, JsonUtility.ToJson(state), "Editing a snapshot must not mutate the game or another queued snapshot.");
        }

        [Test]
        public void QueueCapturesTheCurrentWorldAndLoadWaitsForItsDurableWrite()
        {
            var state = CareerState.New(42);
            state.world.disturbance = new float[state.world.heights.Length]; state.world.disturbance[0] = .3f;
            string expected = JsonUtility.ToJson(state);
            Assert.IsTrue(store.QueueSave(state, out string why), why);
            // A real game may edit its terrain and tree flags immediately after QueueSave returns.
            state.funds -= 100; state.world.heights[0] = float.NaN;
            state.world.disturbance[0] = 1f; state.world.trees.Clear(); state.layout.clearedTrees = 500;
            Assert.IsTrue(store.Load(out var loaded, out why), why);
            Assert.AreEqual(expected, JsonUtility.ToJson(loaded));
            Assert.IsFalse(store.TryTakeSaveProblem(out why), why);
            Assert.IsFalse(File.Exists(Path.Combine(directory, "career.json.pending")));
        }

        [Test]
        public void RepeatedQueuedSavesAndExplicitSaveKeepTheLatestSnapshotAndBackupInOrder()
        {
            var state = CareerState.New(42);
            for (int i = 0; i < 8; i++)
            {
                state.funds = 90000 - i * 100;
                state.world.trees[i].removed = true;
                Assert.IsTrue(store.QueueSave(state, out string why), why);
            }
            state.funds = 81234;
            Assert.IsTrue(store.Save(state, out string problem), problem);
            string explicitSave = JsonUtility.ToJson(state);
            state.funds = 79876;
            Assert.IsTrue(store.QueueSave(state, out problem), problem);
            Assert.IsTrue(store.Load(out var latest, out problem), problem);
            Assert.AreEqual(79876, latest.funds);
            Assert.AreEqual(explicitSave, File.ReadAllText(Path.Combine(directory, "career.json.backup")));
            Assert.IsFalse(store.TryTakeSaveProblem(out problem), problem);
        }

        [Test]
        public void InvalidQueuedSnapshotReportsFailureOnceAndPreservesTheLastGoodSave()
        {
            var state = CareerState.New(42);
            Assert.IsTrue(store.Save(state, out string problem), problem);
            int savedFunds = state.funds;
            state.world.heights[0] = float.NaN;
            Assert.IsTrue(store.QueueSave(state, out problem), "Queueing copies data; worker validation rejects bad snapshots.");
            Assert.DoesNotThrow(() => store.FlushPending());
            Assert.IsTrue(store.TryTakeSaveProblem(out problem)); Assert.IsNotEmpty(problem);
            Assert.IsFalse(store.TryTakeSaveProblem(out _));
            Assert.IsTrue(store.Load(out var lastGood, out problem), problem);
            Assert.AreEqual(savedFunds, lastGood.funds); Assert.IsFalse(float.IsNaN(lastGood.world.heights[0]));
        }

        [Test]
        public void BackgroundDiskFailureIsReturnedToTheGameWithoutFaultingTheWorker()
        {
            Directory.CreateDirectory(directory);
            string blocked = Path.Combine(directory, "not-a-directory"); File.WriteAllText(blocked, "occupied");
            var blockedStore = new CareerSaveStore(blocked);
            Assert.IsTrue(blockedStore.QueueSave(CareerState.New(42), out string problem), problem);
            Assert.DoesNotThrow(() => blockedStore.FlushPending());
            Assert.IsTrue(blockedStore.TryTakeSaveProblem(out problem)); StringAssert.Contains("Could not save", problem);
            Assert.IsFalse(blockedStore.Exists);
        }
    }
}
