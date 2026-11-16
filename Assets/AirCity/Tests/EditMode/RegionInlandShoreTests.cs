using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class RegionInlandShoreTests
    {
        [Test]
        public void LocalSurfaceRemovesRasterSpikesButRetainsRiverFallSeparateLakesAndSavedSamples()
        {
            const int resolution = 65;
            var source = new float[resolution * resolution];
            for (int z = 28; z <= 34; z++)
            for (int x = 4; x <= 60; x++) source[z * resolution + x] = 108f + x * .04f + ((x + z) % 2 == 0 ? .7f : -.7f);
            for (int z = 8; z <= 14; z++)
            for (int x = 8; x <= 14; x++) source[z * resolution + x] = 155f;
            var original = (float[])source.Clone();
            var surface = RegionInlandShore.Surface(source, resolution);
            for (int x = 12; x < 52; x++)
                Assert.Less(Mathf.Abs(surface[31 * resolution + x + 1] - surface[31 * resolution + x]), .1f,
                    "Water should not retain alternating DEM steps between adjacent samples.");
            Assert.Greater(surface[31 * resolution + 52] - surface[31 * resolution + 12], 1.2f,
                "A connected river needs its local downhill profile, rather than one global lake level.");
            Assert.AreEqual(155f, surface[11 * resolution + 11], .0001f);
            Assert.Greater(surface[26 * resolution + 30], 0f, "Water extends beneath a dry bank to hide its outer mesh boundary.");
            Assert.AreEqual(0f, surface[23 * resolution + 30], "The bank support must not flood distant dry ground.");
            CollectionAssert.AreEqual(original, source);
        }

        [Test]
        public void UpgradeRoundsNaturalBanksOnceAndPreservesEarthworkAirportGroundAndOriginalWaterLevels()
        {
            const int resolution = ValleyWorld.Resolution;
            var world = new ValleyWorld { generatorVersion = 5, hasOcean = true,
                heights = new float[resolution * resolution], waterHeights = new float[resolution * resolution],
                disturbance = new float[resolution * resolution] };
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x; bool wet = x >= 252 && x <= 260;
                float level = 108f + z * .01f;
                world.waterHeights[i] = wet ? level : 0f;
                world.heights[i] = level + (wet ? -1.5f : 8f);
            }
            Assert.IsTrue(world.HasOcean && world.HasInlandWater, "An elevated freshwater body can coexist with a coastal ocean.");
            world.disturbance[120 * resolution + 252] = .3f;
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++) world.heights[(120 + dz) * resolution + 252 + dx] += 4f;
            var airport = new Rect(-100f, 400f, 200f, 400f);
            var heights = (float[])world.heights.Clone(); var water = (float[])world.waterHeights.Clone();
            Assert.AreEqual(world.waterHeights[120 * resolution + 256], world.InlandSurfaceHeights[120 * resolution + 256],
                "Old saved wet levels remain exact before their controlled migration; only hidden bank support is derived.");
            Assert.IsTrue(RegionInlandShore.Upgrade(world, airport.Contains));
            Assert.AreEqual(6, world.generatorVersion);
            CollectionAssert.AreEqual(water, world.waterHeights);
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x;
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                if (airport.Contains(p) || Mathf.Abs(z - 120) <= 2 && Mathf.Abs(x - 252) <= 2)
                    Assert.AreEqual(heights[i], world.heights[i]);
            }
            Assert.AreNotEqual(heights[400 * resolution + 251], world.heights[400 * resolution + 251]);
            var upgraded = (float[])world.heights.Clone();
            Assert.IsFalse(RegionInlandShore.Upgrade(world, null));
            CollectionAssert.AreEqual(upgraded, world.heights);
            world.generatorVersion = 3;
            Assert.IsFalse(RegionInlandShore.Upgrade(world, null));
            CollectionAssert.AreEqual(upgraded, world.heights);
        }
    }
}
