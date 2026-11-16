using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirCity.EditorTools.World;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public class GroundClearingTests
    {
        [Test]
        public void BrushPricesTreesAndObstaclesTogetherAndCommitsAtomicallyWithoutGrading()
        {
            var state = Session();
            var ground = Obstacles();
            var heights = (float[])state.world.heights.Clone();
            var quote = CareerConstruction.QuoteClear(state, Vector2.zero, 4f, ground);
            Assert.AreEqual(1, quote.Trees); Assert.AreEqual(4, quote.Props);
            // Tree 24, a scale-2 boulder 72, stone 4, log 12, shrub 5.
            Assert.AreEqual(117, quote.Price); Assert.AreEqual(5, quote.Count);
            state.funds = quote.Price - 1;
            string before = JsonUtility.ToJson(state);
            Assert.IsFalse(CareerConstruction.Clear(state, Vector2.zero, 4f, out _, ground: ground));
            Assert.AreEqual(before, JsonUtility.ToJson(state));
            state.funds = quote.Price;
            Assert.IsTrue(CareerConstruction.Clear(state, Vector2.zero, 4f, out string why, ground: ground), why);
            Assert.AreEqual(0, state.funds);
            Assert.IsTrue(state.world.trees[0].removed); Assert.IsFalse(state.world.trees[1].removed);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4 }, state.world.clearedProps);
            Assert.IsFalse(state.world.GroundPropRemoved(5)); Assert.IsFalse(state.world.GroundPropRemoved(6));
            Assert.AreEqual(0f, state.layout.earthwork); CollectionAssert.AreEqual(heights, state.world.heights);
            Assert.IsTrue(state.world.disturbance.All(d => d <= .3f), "Rock removal must not masquerade as earthwork.");
            Assert.AreEqual(0, CareerConstruction.QuoteClear(state, Vector2.zero, 4f, ground).Price);
            Assert.IsFalse(CareerConstruction.Clear(state, Vector2.zero, 4f, out _, ground: ground));
        }

        [Test]
        public void SingleTreeSelectionLeavesUnselectedRocksAndShrubsInPlace()
        {
            var state = Session(); var ground = Obstacles();
            var quote = CareerConstruction.QuoteClear(state, Vector2.zero, 90f, ground, 0);
            Assert.AreEqual(CareerConstruction.TreePrice, quote.Price); Assert.AreEqual(0, quote.Props);
            Assert.IsTrue(CareerConstruction.Clear(state, Vector2.zero, 90f, out string why, 0, ground), why);
            Assert.IsEmpty(state.world.clearedProps); Assert.IsFalse(state.world.trees[1].removed);
        }

        [Test]
        public void RemovedRockSlotsSurviveSnapshotsAndLocalRegenerationWithoutRerollingTheirStoneCluster()
        {
            var world = Habitat();
            var area = new Rect(-320f, -320f, 640f, 640f);
            var original = GroundCoverScatter.Generate(world, area: area);
            Assert.AreEqual(original.Length, original.Select(p => p.id).Distinct().Count());
            var parent = original.First(p => p.kind == GroundCoverKind.Boulder &&
                original.Any(child => child.id == p.id + 1) && original.Any(child => child.id == p.id + 2));
            world.RemoveGroundProps(new[] { parent.id });
            var after = GroundCoverScatter.Generate(world, area: area);
            Assert.IsFalse(after.Any(p => p.id == parent.id));
            Assert.IsTrue(after.Any(p => p.id == parent.id + 1)); Assert.IsTrue(after.Any(p => p.id == parent.id + 2));
            var byId = original.ToDictionary(p => p.id);
            foreach (var item in after) Assert.AreEqual(byId[item.id], item);
            var copy = CareerSnapshot.Capture(new CareerState { world = world });
            Assert.AreNotSame(world.clearedProps, copy.world.clearedProps);
            var loaded = JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(copy.world));
            var restored = GroundCoverScatter.Generate(loaded, area: area);
            CollectionAssert.AreEqual(after.Select(p => p.id), restored.Select(p => p.id));
            var tile = new Rect(-256f, -256f, 256f, 256f);
            var local = GroundCoverScatter.Generate(loaded, area: tile);
            Assert.IsTrue(new HashSet<int>(after.Where(p => tile.Contains(p.position)).Select(p => p.id)).SetEquals(local.Select(p => p.id)));
            foreach (var item in local) Assert.AreEqual(byId[item.id], item);
        }

        [Test]
        public void ImmediateClearRefreshOnlyReplacesTheAffectedPropTile()
        {
            var world = Habitat();
            world.landCover = Enumerable.Repeat((byte)50, world.heights.Length).ToArray();
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                var p = new Vector2(x * world.Spacing - 3200f, z * world.Spacing - 3200f);
                if (new Rect(-256f, -256f, 512f, 512f).Contains(p) || new Rect(800f, 800f, 256f, 256f).Contains(p))
                    world.landCover[z * ValleyWorld.Resolution + x] = 10;
            }
            var root = new GameObject("Saved obstacle clearing regression");
            try
            {
                var renderer = root.AddComponent<GroundCoverRenderer>();
                renderer.library = AssetDatabase.LoadAssetAtPath<GroundCoverLibrary>(GroundCoverSetup.LibraryPath);
                Assert.IsNotNull(renderer.library); renderer.Rebuild(world);
                var collection = renderer.Instances;
                var oldTiles = Tiles(renderer);
                var selected = renderer.Instances.First(p => CareerConstruction.Clearable(p.kind) && p.position.x < 0f);
                int before = renderer.InstanceCount;
                world.RemoveGroundProps(new[] { selected.id });
                renderer.RefreshCleared(world, new Rect(selected.position.x - 4f, selected.position.y - 4f, 8f, 8f));
                Assert.AreSame(collection, renderer.Instances);
                Assert.AreEqual(before - 1, renderer.InstanceCount);
                Assert.IsFalse(renderer.Instances.Any(p => p.id == selected.id));
                var newTiles = Tiles(renderer);
                Assert.AreEqual(1, oldTiles.Where((tile, i) => !ReferenceEquals(tile, newTiles[i])).Count());
                var local = renderer.QueryBounds(new Rect(selected.position.x - 20f, selected.position.y - 20f, 40f, 40f)).ToArray();
                Assert.Less(local.Length, before / 4);
                var remaining = renderer.Instances.Select(p => p.id).ToHashSet();
                renderer.Rebuild(world);
                Assert.AreEqual(before - 1, renderer.InstanceCount);
                Assert.IsTrue(remaining.SetEquals(renderer.Instances.Select(p => p.id)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void InvalidOrDuplicateSavedPropIdentifiersAreRejected()
        {
            var world = CareerState.New(42).world;
            Assert.IsTrue(world.Validate(out string why), why);
            world.clearedProps = new List<int> { 0 }; Assert.IsFalse(world.Validate(out _));
            world.clearedProps = new List<int> { GroundCoverScatter.MaxInstanceId + 1 }; Assert.IsFalse(world.Validate(out _));
            world.clearedProps = new List<int> { 1, 1 }; Assert.IsFalse(world.Validate(out _));
            world.clearedProps = null; Assert.IsTrue(world.Validate(out why), why);
        }

        [Test]
        public void PaidRockRemovalSurvivesADurableCareerSaveAndResume()
        {
            var state = Session();
            var props = GroundCoverScatter.Generate(state.world, area: state.Site.Parcel);
            var rock = props.First(p => p.kind == GroundCoverKind.Boulder);
            Assert.IsTrue(CareerConstruction.Clear(state, rock.position, 4f, out string why, ground: props), why);
            Assert.IsTrue(state.world.GroundPropRemoved(rock.id));
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AirCityRockClear-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new CareerSaveStore(directory);
                Assert.IsTrue(store.Save(state, out why), why);
                Assert.IsTrue(store.Load(out var loaded, out why), why);
                Assert.AreEqual(state.funds, loaded.funds);
                Assert.IsTrue(loaded.world.GroundPropRemoved(rock.id));
                Assert.IsFalse(GroundCoverScatter.Generate(loaded.world, area: loaded.Site.Parcel).Any(p => p.id == rock.id));
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        static object[] Tiles(GroundCoverRenderer renderer) => ((Array)typeof(GroundCoverRenderer)
            .GetField("tiles", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(renderer)).Cast<object>().ToArray();

        static CareerState Session()
        {
            var state = CareerState.New(42); state.selectedSite = 0; state.wholeRegion = state.handsOn = true;
            state.world.trees = new List<ForestTree> {
                new ForestTree { position = Vector2.zero, height = 20f }, new ForestTree { position = new Vector2(20f, 0f), height = 20f } };
            state.layout.Unlock(CareerTool.ClearTrees); state.funds = 10000;
            return state;
        }

        static GroundCoverInstance[] Obstacles() => new[] {
            new GroundCoverInstance { id = 1, kind = GroundCoverKind.Boulder, position = new Vector2(5f, 0f), scale = 2f },
            new GroundCoverInstance { id = 2, kind = GroundCoverKind.Stone, position = new Vector2(2f, 0f), scale = 1f },
            new GroundCoverInstance { id = 3, kind = GroundCoverKind.Log, position = new Vector2(8f, 0f), scale = 1f },
            new GroundCoverInstance { id = 4, kind = GroundCoverKind.Shrub, position = new Vector2(1f, 4f), scale = 1f },
            new GroundCoverInstance { id = 5, kind = GroundCoverKind.Grass, position = Vector2.zero, scale = 1f },
            new GroundCoverInstance { id = 6, kind = GroundCoverKind.Fern, position = Vector2.zero, scale = 1f } };

        static ValleyWorld Habitat()
        {
            var world = new ValleyWorld { seed = 57413, heights = Enumerable.Repeat(80f, ValleyWorld.Resolution * ValleyWorld.Resolution).ToArray() };
            for (int z = -320; z <= 320; z += 24)
            for (int x = -320; x <= 320; x += 24) world.trees.Add(new ForestTree { position = new Vector2(x, z), height = 20f });
            return world;
        }
    }
}
