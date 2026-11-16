using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class ForestQueryTests
    {
        static ForestTree Tree(float x, float z, float height = 20f)
            => new ForestTree { position = new Vector2(x, z), height = height };

        [Test]
        public void BrushQueryMatchesSavedOrderAndSeesRemovalAppendAndListReplacement()
        {
            var world = new ValleyWorld();
            world.sites.Add(new AirportSite { centre = Vector2.zero });
            world.trees.AddRange(new[] { Tree(112f, 0f), Tree(-12f, 0f), Tree(90f, 0f), Tree(0f, 0f), Tree(2000f, 2000f) });
            var state = new CareerState { world = world, selectedSite = 0, wholeRegion = true };
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, CareerConstruction.Trees(state, Vector2.zero, 90f));
            world.trees[1].removed = true;
            CollectionAssert.AreEqual(new[] { 2, 3 }, CareerConstruction.Trees(state, Vector2.zero, 90f));
            world.trees.Add(Tree(-90f, 0f));
            CollectionAssert.AreEqual(new[] { 2, 3, 5 }, CareerConstruction.Trees(state, Vector2.zero, 90f));
            world.trees = new List<ForestTree> { Tree(1000f, 0f), Tree(0f, -20f) };
            CollectionAssert.AreEqual(new[] { 1 }, CareerConstruction.Trees(state, Vector2.zero, 90f));
            world.trees.Clear();
            Assert.IsEmpty(CareerConstruction.Trees(state, Vector2.zero, 90f));
        }

        [Test]
        public void LevelQueryKeepsItsStrictBoundaryAndReadsCurrentRemovalFlags()
        {
            var world = new ValleyWorld();
            world.trees.Add(Tree(25f, 0f));
            Assert.IsFalse(ForestQuery.AnyWithin(world, Vector2.zero, 25f));
            Assert.IsTrue(ForestQuery.AnyWithin(world, Vector2.zero, 25.01f));
            world.trees[0].removed = true;
            Assert.IsFalse(ForestQuery.AnyWithin(world, Vector2.zero, 25.01f));
            world.trees.Add(Tree(1f, 1f));
            Assert.IsTrue(ForestQuery.AnyWithin(world, Vector2.zero, 25f));
        }

        [Test]
        public void RayPickingMatchesFullScanAtMapEdgesAndShallowCameraAngles()
        {
            var world = new ValleyWorld(); var random = new WorldRandom(719);
            for (int i = 0; i < 5000; i++)
                world.trees.Add(Tree((random.Next() - .5f) * ValleyWorld.Size, (random.Next() - .5f) * ValleyWorld.Size,
                    1f + random.Next() * 39f));
            world.trees.Add(Tree(-3200f, 0f, 40f)); world.trees.Add(Tree(3200f, 0f, 40f));
            float Height(Vector2 p) => 310f + Mathf.Sin(p.x * .002f) * 300f;
            bool Editable(Vector2 p) => p.y < 2700f;
            var origins = new[] { new Vector3(0f, 1000f, -4000f), new Vector3(-4000f, 320f, 0f),
                new Vector3(4000f, 320f, 0f), new Vector3(0f, 710f, 0f), new Vector3(0f, 20f, -3300f) };
            foreach (var origin in origins)
            for (int i = 0; i < 25; i++)
            {
                var tree = world.trees[i == 0 ? world.trees.Count - 2 : i == 1 ? world.trees.Count - 1 : i * 157];
                var aim = new Vector3(tree.position.x, Height(tree.position) + tree.height * .55f, tree.position.y);
                var ray = new Ray(origin, (aim - origin).normalized);
                Assert.AreEqual(FullPick(world, ray, Height, Editable), ForestQuery.Pick(world, ray, Height, Editable),
                    $"Camera {origin}, target {tree.position}");
            }
            // Flags change without rebuilding a spatial index.
            foreach (var tree in world.trees.Where((_, i) => i % 3 == 0)) tree.removed = true;
            var shallow = new Ray(new Vector3(-3400f, 30f, 0f), new Vector3(1f, .001f, 0f).normalized);
            Assert.AreEqual(FullPick(world, shallow, Height, Editable), ForestQuery.Pick(world, shallow, Height, Editable));
        }

        [Test]
        public void PickingEqualDepthTreesKeepsTheLowestSavedIndexAcrossCells()
        {
            var world = new ValleyWorld();
            world.trees.AddRange(new[] { Tree(-10f, 500f), Tree(2f, 0f), Tree(-2f, 0f) });
            var ray = new Ray(new Vector3(0f, 11f, -200f), Vector3.forward);
            Assert.AreEqual(1, ForestQuery.Pick(world, ray, _ => 0f));
            world.trees[1].removed = true;
            Assert.AreEqual(2, ForestQuery.Pick(world, ray, _ => 0f));
        }

        [Test]
        public void DownwardPickingOnlySamplesTreesInNearbyCells()
        {
            var world = new ValleyWorld();
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++) world.trees.Add(Tree(-3175f + x * 50f, -3175f + z * 50f));
            int heightSamples = 0;
            var ray = new Ray(new Vector3(25f, 500f, 25f), Vector3.down);
            int picked = ForestQuery.Pick(world, ray, _ => { heightSamples++; return 40f; });
            Assert.GreaterOrEqual(picked, 0);
            Assert.Less(heightSamples, 100, "Picking must avoid terrain-height sampling for the whole forest.");
            Assert.Less(ForestQuery.Candidates(world, new Rect(-50f, -50f, 100f, 100f)).Count(), 100);
        }

        static int FullPick(ValleyWorld world, Ray ray, Func<Vector2, float> heightAt, Func<Vector2, bool> editable)
        {
            int best = -1; float near = float.MaxValue;
            for (int i = 0; i < world.trees.Count; i++)
            {
                var tree = world.trees[i]; if (tree.removed || !editable(tree.position)) continue;
                var centre = new Vector3(tree.position.x, heightAt(tree.position) + tree.height * .55f, tree.position.y);
                float along = Vector3.Dot(centre - ray.origin, ray.direction); if (along < 0f || along >= near) continue;
                if (Vector3.Distance(ray.GetPoint(along), centre) > tree.height * .35f) continue;
                near = along; best = i;
            }
            return best;
        }
    }
}
