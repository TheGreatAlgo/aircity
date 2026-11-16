using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirCity.EditorTools.World;
using AirCity.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class ForestRendererTests
    {
        GameObject root;
        ForestRenderer forest;
        Camera camera;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Forest renderer regression");
            forest = root.AddComponent<ForestRenderer>();
            forest.library = AssetDatabase.LoadAssetAtPath<TreeLibrary>(WorldAssetSetup.TreesPath);
            Assert.IsNotNull(forest.library);
            var cameraObject = new GameObject("Forest regression camera"); cameraObject.transform.SetParent(root.transform);
            camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.nearClipPlane = .1f; camera.farClipPlane = 15000f; camera.fieldOfView = 70f; camera.aspect = 1.5f;
        }

        [TearDown] public void TearDown() { Object.DestroyImmediate(root); }

        [Test]
        public void WholeRegionViewSubmitsEachTreeOnceUsingPackedSixTriangleImpostors()
        {
            RequireInstancing();
            var world = Grid(); forest.Rebuild(world, _ => 80f);
            camera.transform.SetPositionAndRotation(new Vector3(0f, 7000f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            forest.DrawForCamera(camera);
            Assert.AreEqual(world.trees.Count, forest.LastSubmittedTrees);
            Assert.AreEqual(world.trees.Count, forest.LastImpostorTrees);
            Assert.AreEqual(0, forest.LastFullDetailTrees + forest.LastMediumDetailTrees);
            Assert.AreEqual(world.trees.Count * 6L, forest.LastSubmittedTriangles);
            int species = Field<TreeLibrary>(forest, "activeLibrary").variants.Length;
            Assert.LessOrEqual(forest.LastDrawCalls, 64 * species,
                "This sparse fixture needs at most one packed buffer per species per 800 m cell, rather than a draw per fine cell.");
            AssertBuffersMatch(world);
        }

        [Test]
        public void CloseCameraKeepsFarTreesCheapWithinTheSameEightHundredMetreCell()
        {
            RequireInstancing();
            var world = new ValleyWorld();
            world.trees.Add(new ForestTree { position = new Vector2(50f, 50f), height = 24f });
            world.trees.Add(new ForestTree { position = new Vector2(700f, 700f), height = 24f });
            forest.Rebuild(world, _ => 0f);
            camera.fieldOfView = 100f;
            camera.transform.SetPositionAndRotation(new Vector3(60f, 45f, -10f), Quaternion.LookRotation(new Vector3(140f, -40f, 600f)));
            forest.DrawForCamera(camera);
            Assert.AreEqual(2, forest.LastSubmittedTrees);
            Assert.AreEqual(1, forest.LastFullDetailTrees);
            Assert.AreEqual(1, forest.LastImpostorTrees, "A nearby tree must not promote the distant part of its coarse cell.");
            camera.cullingMask = 0; forest.DrawForCamera(camera);
            Assert.AreEqual(0, forest.LastDrawCalls); Assert.AreEqual(0, forest.LastSubmittedTrees);
        }

        [Test]
        public void LocalClearingOnlyCompactsNearbyBuffersAndKeepsSurvivorsStableAtEveryDistance()
        {
            var world = Grid(); forest.Rebuild(world, _ => 80f);
            var before = Matrices(DetailBatches()).ToDictionary(p => p.Key, p => p.Value);
            var arrays = DetailBatches().ToDictionary(b => b, b => Field<Matrix4x4[]>(b, "matrices"));
            var centre = new Vector2(-1900f, -1900f);
            int removed = 0;
            foreach (var tree in world.trees)
                if ((tree.position - centre).sqrMagnitude <= 60f * 60f) { tree.removed = true; removed++; }
            forest.RefreshRemoved(world, _ => 80f, new Rect(centre.x - 60f, centre.y - 60f, 120f, 120f));
            Assert.AreEqual(world.trees.Count - removed, forest.TreeCount);
            Assert.Less(forest.LastRemovalCheckedTrees, world.trees.Count / 10, "A clearing stamp must not scan the whole forest.");
            Assert.Greater(arrays.Count(p => ReferenceEquals(p.Value, Field<Matrix4x4[]>(p.Key, "matrices"))), arrays.Count * .95f);
            foreach (var pair in Matrices(DetailBatches())) Assert.AreEqual(before[pair.Key], pair.Value);
            AssertBuffersMatch(world);

            var edited = new Rect(centre.x - 100f, centre.y - 100f, 200f, 200f);
            forest.RefreshHeights(edited, _ => 93f);
            foreach (var pair in Matrices(DetailBatches()))
            {
                var position = world.trees[pair.Key].position;
                Assert.AreEqual(edited.Contains(position) ? 93f : 80f, pair.Value.m13);
            }
            AssertBuffersMatch(world);

            world.trees.Add(new ForestTree { position = new Vector2(110f, 210f), height = 19f });
            forest.RefreshRemoved(world, p => edited.Contains(p) ? 93f : 80f);
            Assert.AreEqual(world.trees.Count - removed, forest.TreeCount, "Appending a saved-world population must refresh the full hierarchy.");
            foreach (var pair in Matrices(DetailBatches()).Where(p => p.Key < before.Count))
            {
                var expected = before[pair.Key]; if (edited.Contains(world.trees[pair.Key].position)) expected.m13 = 93f;
                Assert.AreEqual(expected, pair.Value);
            }
            AssertBuffersMatch(world);
        }

        [Test]
        public void ReplacingTheSavedTreeListWithTheSameCountRebuildsTransforms()
        {
            var world = new ValleyWorld();
            world.trees.Add(new ForestTree { position = new Vector2(10f, 20f), height = 24f });
            forest.Rebuild(world, _ => 80f);
            world.trees = new List<ForestTree> { new ForestTree { position = new Vector2(200f, 300f), height = 19f } };
            forest.RefreshRemoved(world, _ => 91f);
            var matrix = Matrices(DetailBatches()).Single().Value;
            Assert.AreEqual(200f, matrix.m03); Assert.AreEqual(300f, matrix.m23); Assert.AreEqual(91f, matrix.m13);
            AssertBuffersMatch(world);
        }

        void AssertBuffersMatch(ValleyWorld world)
        {
            var detail = Matrices(DetailBatches()).ToDictionary(p => p.Key, p => p.Value);
            var distant = Matrices(Field<IList>(forest, "cells").Cast<object>()
                .SelectMany(c => Field<IList>(c, "distant").Cast<object>())).ToDictionary(p => p.Key, p => p.Value);
            var living = new HashSet<int>(world.trees.Select((tree, index) => (tree, index)).Where(p => !p.tree.removed).Select(p => p.index));
            Assert.AreEqual(living.Count, detail.Count);
            Assert.IsTrue(living.SetEquals(detail.Keys), "Detailed buffers must contain each surviving saved tree exactly once.");
            Assert.AreEqual(detail.Count, distant.Count);
            Assert.IsTrue(living.SetEquals(distant.Keys), "Distant buffers must contain the same saved trees as detailed buffers.");
            foreach (var pair in detail) Assert.AreEqual(pair.Value, distant[pair.Key]);
            foreach (var batch in DetailBatches()) Assert.LessOrEqual(Field<Matrix4x4[]>(batch, "matrices").Length, 500);
        }

        IEnumerable<object> DetailBatches() => Field<IList>(forest, "batches").Cast<object>();
        static IEnumerable<KeyValuePair<int, Matrix4x4>> Matrices(IEnumerable<object> batches)
        {
            foreach (var batch in batches)
            {
                var matrices = Field<Matrix4x4[]>(batch, "matrices"); var indices = Field<int[]>(batch, "treeIndices");
                for (int i = 0; i < indices.Length; i++) yield return new KeyValuePair<int, Matrix4x4>(indices[i], matrices[i]);
            }
        }
        static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(owner);
        static void RequireInstancing() { if (!SystemInfo.supportsInstancing) Assert.Ignore("Graphics instancing is required for the camera submission regression."); }
        static ValleyWorld Grid()
        {
            var world = new ValleyWorld { seed = 1986 };
            for (int z = -2400; z <= 2400; z += 20)
            for (int x = -2400; x <= 2400; x += 20)
                world.trees.Add(new ForestTree { position = new Vector2(x, z), height = 24f + (x + z) % 4 });
            return world;
        }
    }
}
