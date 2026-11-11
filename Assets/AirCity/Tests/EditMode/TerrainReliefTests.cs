using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class TerrainReliefTests
    {
        [TestCase(42)]
        [TestCase(19770616)]
        public void StarterValleyHasRollingGroundAndSmallerLandformsWithClearApproaches(int seed)
        {
            var world = ValleyWorld.Generate(seed);
            Assert.AreEqual(2, world.generatorVersion);
            var site = world.sites[0];
            float low = float.MaxValue, high = float.MinValue, localForm = 0f;
            for (float z = -140f; z <= 140f; z += 25f)
            for (float x = -275f; x <= 275f; x += 25f)
            {
                var point = site.centre + new Vector2(x, z);
                float h = world.Height(point);
                low = Mathf.Min(low, h); high = Mathf.Max(high, h);
                float neighbours = (world.Height(point + Vector2.right * 25f) + world.Height(point - Vector2.right * 25f)) * .5f;
                localForm = Mathf.Max(localForm, Mathf.Abs(h - neighbours));
            }
            Assert.Greater(high - low, 2f, "An unworked starter site needs visible low mounds and swales.");
            Assert.Greater(localForm, .12f, "Relief must include smaller landforms, not just a tilted plane.");
            foreach (var valley in world.sites)
            for (float x = -3000f; x <= 3000f; x += 50f)
            {
                float approach = valley.elevation + Mathf.Max(0f, Mathf.Abs(x) - 350f) * Mathf.Tan(3f * Mathf.Deg2Rad);
                Assert.LessOrEqual(world.Height(valley.centre + Vector2.right * x), approach + 3f);
            }
        }

        [Test]
        public void LevelBrushFlattensOnlyTheChosenPatchOfNaturalRelief()
        {
            var world = ValleyWorld.Generate(42);
            var site = world.sites[0];
            var point = new Vector2(Mathf.Round(site.centre.x / world.Spacing) * world.Spacing,
                Mathf.Round(site.centre.y / world.Spacing) * world.Spacing);
            const float radius = 90f;
            var before = (float[])world.heights.Clone();
            int trees = world.TreeCount(site.Parcel), innerSamples = 0;
            world.LevelBrush(point, radius, site.elevation, site.Parcel);
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                int index = z * ValleyWorld.Resolution + x;
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                float distance = Vector2.Distance(p, point);
                if (distance < radius * .7f && site.Parcel.Contains(p))
                { Assert.AreEqual(site.elevation, world.heights[index], .00001f); innerSamples++; }
                if (distance >= radius || !site.Parcel.Contains(p)) Assert.AreEqual(before[index], world.heights[index]);
            }
            Assert.Greater(innerSamples, 50);
            Assert.AreEqual(trees, world.TreeCount(site.Parcel), "Grading must never remove trees.");
            Assert.Greater(world.BrushEarthwork(point + Vector2.right * 150f, radius, site.elevation, site.Parcel), 1f);
        }

        [Test]
        public void OriginalHeightSnapshotsRemainValidWithoutRegeneration()
        {
            var world = ValleyWorld.Generate(17);
            world.generatorVersion = 1;
            world.heights[0] = 123.456f;
            var original = (float[])world.heights.Clone();
            var loaded = JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(world));
            Assert.IsTrue(loaded.Validate(out string why), why);
            Assert.AreEqual(1, loaded.generatorVersion);
            CollectionAssert.AreEqual(original, loaded.heights);
            loaded.generatorVersion = ValleyWorld.GeneratorVersion + 1;
            Assert.IsFalse(loaded.Validate(out _));
        }
    }
}
