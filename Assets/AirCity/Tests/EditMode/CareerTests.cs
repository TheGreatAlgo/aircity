using System;
using System.IO;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Game
{
    public class CareerTests
    {
        string directory;
        [SetUp] public void Setup() { directory = Path.Combine(Path.GetTempPath(), "AirCityCareer-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test]
        public void SeedReproducesTheRegionWithoutChangingUnityRandom()
        {
            var randomState = UnityEngine.Random.state;
            var first = ValleyWorld.Generate(42); var second = ValleyWorld.Generate(42); var other = ValleyWorld.Generate(43);
            CollectionAssert.AreEqual(first.heights, second.heights);
            Assert.AreEqual(first.trees.Count, second.trees.Count);
            Assert.AreEqual(first.trees[100].position, second.trees[100].position);
            CollectionAssert.AreEqual(first.heights, other.heights, "Internal seeds cannot change the selected geographic footprint.");
            Assert.AreNotEqual(first.trees[100].position, other.trees[100].position);
            Assert.AreEqual(randomState, UnityEngine.Random.state);
        }

        [TestCase(0)] [TestCase(-1)] [TestCase(int.MinValue)] [TestCase(int.MaxValue)] [TestCase(19770616)]
        public void EveryStartingPocketIsAffordableAndHasClearLocalApproaches(int seed)
        {
            var generated = ValleyWorld.GenerateLegacy(seed);
            Assert.IsTrue(generated.Validate(out string why), why);
            for (int index = 0; index < 3; index++)
            {
                var state = new CareerState { world = JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(generated)) };
                Assert.IsTrue(state.Build(out why, index), why);
                while (!state.IsOpen)
                {
                    int before = state.funds, price = state.Quote().cost;
                    Assert.IsTrue(state.Build(out why), why);
                    Assert.AreEqual(before - price, state.funds);
                }
                Assert.Greater(state.funds, CareerState.CharterCost + 5000);
                var site = state.Site;
                Assert.AreEqual(0, state.world.TreeCount(site.Preparation));
                for (float x = -400f; x <= 400f; x += 25f)
                    Assert.That(state.world.Height(site.centre + new Vector2(x, -65f)), Is.EqualTo(site.elevation).Within(.001f));
                for (float x = -550f; x <= 550f; x += 50f)
                {
                    float altitude = site.elevation + Mathf.Max(0f, Mathf.Abs(x) - 350f) * Mathf.Tan(3f * Mathf.Deg2Rad);
                    Assert.LessOrEqual(state.world.Height(site.centre + new Vector2(x, 0f)), altitude + 3f);
                }
                Assert.IsTrue(state.Validate(out why), why);
            }
        }

        [Test]
        public void UnaffordableWorkAndPrematureFlightsDoNotMutateTheCareer()
        {
            var state = CareerState.New(17, false);
            Assert.IsFalse(state.BeginCharter(out _)); Assert.IsFalse(state.Build(out _, -1));
            Assert.AreEqual(CareerState.StartingFunds, state.funds);
            state.funds = 1;
            var before = JsonUtility.ToJson(state);
            Assert.IsFalse(state.Build(out _, 0)); Assert.AreEqual(before, JsonUtility.ToJson(state));
        }

        [Test]
        public void EarthworkQuoteIncludesTheBlendedEdgesAndClearingMatchesItsQuote()
        {
            var state = CareerState.New(19770616, false); state.Build(out _, 0);
            int remaining = state.world.trees.FindAll(t => !t.removed).Count;
            int quotedTrees = state.Quote().trees;
            state.Build(out _);
            Assert.AreEqual(quotedTrees, remaining - state.world.trees.FindAll(t => !t.removed).Count);
            var before = (float[])state.world.heights.Clone();
            float quote = state.Quote().earthwork; state.Build(out _);
            double actual = 0;
            for (int i = 0; i < before.Length; i++) actual += Math.Abs(before[i] - state.world.heights[i]) * state.world.Spacing * state.world.Spacing;
            Assert.That(actual, Is.EqualTo(quote).Within(quote * .0001f));
            Assert.AreEqual(0, state.world.TreeCount(state.Site.Clearing));
        }

        [Test]
        public void CharterPaysOnceAndUnlocksAnAffordableExtension()
        {
            var state = OpenCareer(); int money = state.funds;
            Assert.IsFalse(state.CompleteCharter());
            Assert.IsTrue(state.BeginCharter(out _)); Assert.AreEqual(money - CareerState.CharterCost, state.funds);
            Assert.IsFalse(state.BeginCharter(out _)); Assert.IsFalse(state.Build(out _));
            Assert.IsTrue(state.CompleteCharter()); Assert.IsFalse(state.CompleteCharter());
            Assert.AreEqual(money + CareerState.CharterFee - CareerState.CharterCost, state.funds);
            Assert.AreEqual(CareerStep.Expansion, state.Step);
            Assert.IsTrue(state.Build(out string why), why); Assert.AreEqual(1000f, state.RunwayLength);
            Assert.AreEqual(CareerStep.Operating, state.Step);
            Assert.IsTrue(state.BeginCharter(out _)); state.CancelCharter();
            int refunded = state.funds; state.CancelCharter(); Assert.AreEqual(refunded, state.funds);
        }

        [Test]
        public void SaveRetainsTerrainEditsAndInFlightProgress()
        {
            var state = OpenCareer(); state.BeginCharter(out _); state.charterElapsed = 62.5f; state.operatingSeconds = 62.5f;
            var store = new CareerSaveStore(directory);
            Assert.IsTrue(store.Save(state, out string why), why);
            Assert.IsTrue(store.Load(out var loaded, out why), why);
            Assert.AreEqual(JsonUtility.ToJson(state), JsonUtility.ToJson(loaded));
            Assert.IsTrue(loaded.charterActive); Assert.AreEqual(62.5f, loaded.charterElapsed);
        }

        [Test]
        public void CorruptLatestSaveRecoversBackupAndRejectsInvalidWrites()
        {
            var state = OpenCareer(); var store = new CareerSaveStore(directory);
            Assert.IsTrue(store.Save(state, out _)); int funds = state.funds;
            state.BeginCharter(out _); Assert.IsTrue(store.Save(state, out _));
            File.WriteAllText(Path.Combine(directory, "career.json"), "{broken");
            Assert.IsTrue(store.Load(out var recovered, out string why), why);
            Assert.AreEqual(funds, recovered.funds); StringAssert.Contains("Recovered", why);
            state.world.heights[0] = float.NaN;
            Assert.IsFalse(store.Save(state, out _));
            Assert.IsTrue(store.Load(out recovered, out _)); Assert.AreEqual(funds, recovered.funds);
        }

        [Test]
        public void IncompleteOrFutureSaveIsRejectedWithoutReplacingTheSlot()
        {
            var store = new CareerSaveStore(directory); var state = OpenCareer();
            Assert.IsTrue(store.Save(state, out _));
            state.saveVersion++; Assert.IsFalse(store.Save(state, out _));
            Assert.IsTrue(store.Load(out var recovered, out _)); Assert.AreEqual(CareerState.SaveVersion, recovered.saveVersion);
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "career.json"), "{}");
            Assert.IsFalse(store.Load(out _, out _));
        }

        static CareerState OpenCareer()
        {
            var state = CareerState.New(19770616, false); Assert.IsTrue(state.Build(out _, 0));
            while (!state.IsOpen) Assert.IsTrue(state.Build(out string why), why);
            return state;
        }
    }
}
