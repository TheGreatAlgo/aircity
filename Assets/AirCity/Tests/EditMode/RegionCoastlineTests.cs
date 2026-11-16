using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class RegionCoastlineTests
    {
        [Test]
        public void GeometryRoundsCoastalCornersWithoutErasingNarrowMotuOrChangingInteriorLand()
        {
            const int resolution = 65; const float sea = 40f, spacing = 12.5f;
            var cover = new byte[resolution * resolution]; var heights = new float[cover.Length];
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x;
                bool mainland = x >= 20 && x <= 48 && z >= 12 && z <= 52;
                bool motu = x == 9 && z >= 8 && z <= 50 || z == 32 && x >= 9 && x <= 20 || x == 55 && z == 52;
                cover[i] = mainland || motu ? (byte)30 : (byte)80;
                heights[i] = mainland ? sea + 80f : motu ? sea + 1.5f : sea - 25f;
            }
            var original = (float[])heights.Clone();
            RegionCoastline.Shape(heights, cover, resolution, spacing, sea);
            for (int i = 0; i < heights.Length; i++)
                Assert.AreEqual(cover[i] != 80, heights[i] > sea + .15f, "Source land and narrow land bridges must keep their sign.");
            Assert.Less(heights[12 * resolution + 20], heights[25 * resolution + 20], "A convex corner must round towards the shoreline.");
            Assert.AreEqual(original[32 * resolution + 36], heights[32 * resolution + 36]);
            Assert.AreEqual(original[0], heights[0], "Deep water outside the coastal band must retain its bathymetry.");
            Assert.Less(LargestCoastalStep(heights, cover, resolution), spacing * .5f);
            Assert.Greater(LargestCoastalStep(original, cover, resolution), 100f);
        }

        [Test]
        public void RealLagoonShorePreservesMappedLandAndSavedPlayerHeightEdits()
        {
            var world = ValleyWorld.Generate(445090, RegionPreset.Tropical);
            Assert.AreEqual(ValleyWorld.GeneratorVersion, world.generatorVersion);
            int coastalLand = -1, land = 0, wet = 0;
            for (int z = 1; z < ValleyWorld.Resolution - 1; z++)
            for (int x = 1; x < ValleyWorld.Resolution - 1; x++)
            {
                int i = z * ValleyWorld.Resolution + x;
                bool water = world.landCover[i] == 80;
                Assert.AreEqual(!water, world.heights[i] > world.SeaLevel + .15f, "The lagoon, reef islands and main island must retain their source classification.");
                if (water) wet++; else land++;
                if (!water && world.landCover[i - 1] == 80) coastalLand = i;
            }
            Assert.Greater(land, 30000); Assert.Greater(wet, 150000); Assert.GreaterOrEqual(coastalLand, 0);
            Assert.Less(LargestCoastalStep(world.heights, world.landCover, ValleyWorld.Resolution), world.Spacing * .5f);
            var sourceAreaField = new float[world.heights.Length];
            for (int i = 0; i < sourceAreaField.Length; i++) sourceAreaField[i] = world.landCover[i] == 80 ? 0f : 1f;
            float originalArea = SubgridLandArea(sourceAreaField, .5f);
            Assert.AreEqual(originalArea, SubgridLandArea(world.heights, world.SeaLevel), originalArea * .025f,
                "Smoothing may round sub-grid contours but must preserve the real lagoon's land area.");
            world.heights[coastalLand] += 8f; // A player's later earthwork is saved geometry, never regenerated on load.
            var loaded = JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(world));
            CollectionAssert.AreEqual(world.heights, loaded.heights);
            Assert.IsFalse(RegionCoastline.Upgrade(loaded, null));
            Assert.AreEqual(world.heights[coastalLand], loaded.heights[coastalLand]);
        }

        [Test]
        public void ExistingRegionUpgradePreservesEditsInterpolationHaloAndProtectedAirportGround()
        {
            const int resolution = ValleyWorld.Resolution;
            var world = new ValleyWorld { generatorVersion = 4, hasOcean = true,
                heights = new float[resolution * resolution], landCover = new byte[resolution * resolution],
                disturbance = new float[resolution * resolution] };
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x; bool land = x >= 256;
                world.landCover[i] = land ? (byte)30 : (byte)80;
                world.heights[i] = world.SeaLevel + (land ? 1.5f : -18f);
            }
            world.disturbance[120 * resolution + 256] = .3f; // Clearing is protected too, not just grading.
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++) world.heights[(120 + dz) * resolution + 256 + dx] += 4f + dx;
            var protectedAirport = new Rect(-100f, 400f, 200f, 400f);
            var original = (float[])world.heights.Clone();
            var edits = (float[])world.disturbance.Clone();
            Assert.IsTrue(RegionCoastline.Upgrade(world, protectedAirport.Contains));
            Assert.AreEqual(ValleyWorld.GeneratorVersion, world.generatorVersion);
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                if (protectedAirport.Contains(p) || Mathf.Abs(z - 120) <= 2 && Mathf.Abs(x - 256) <= 2)
                    Assert.AreEqual(original[z * resolution + x], world.heights[z * resolution + x]);
            }
            CollectionAssert.AreEqual(edits, world.disturbance);
            Assert.AreNotEqual(original[400 * resolution + 255], world.heights[400 * resolution + 255], "Unworked natural coast should still improve.");
            var upgraded = (float[])world.heights.Clone();
            Assert.IsFalse(RegionCoastline.Upgrade(world, protectedAirport.Contains));
            CollectionAssert.AreEqual(upgraded, world.heights);
            world.generatorVersion = 3;
            Assert.IsFalse(RegionCoastline.Upgrade(world, null));
            CollectionAssert.AreEqual(upgraded, world.heights);
        }

        static float LargestCoastalStep(float[] heights, byte[] cover, int resolution)
        {
            float result = 0f;
            for (int z = 1; z < resolution - 1; z++)
            for (int x = 1; x < resolution - 1; x++)
            {
                int i = z * resolution + x;
                if ((cover[i] == 80) != (cover[i + 1] == 80)) result = Mathf.Max(result, Mathf.Abs(heights[i] - heights[i + 1]));
                if ((cover[i] == 80) != (cover[i + resolution] == 80)) result = Mathf.Max(result, Mathf.Abs(heights[i] - heights[i + resolution]));
            }
            return result;
        }

        static float SubgridLandArea(float[] field, float level)
        {
            const int resolution = ValleyWorld.Resolution;
            int land = 0;
            for (int z = 0; z < resolution - 1; z++)
            for (int x = 0; x < resolution - 1; x++)
            {
                int i = z * resolution + x;
                for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                {
                    float u = dx * .5f + .25f, v = dz * .5f + .25f;
                    float h = Mathf.Lerp(Mathf.Lerp(field[i], field[i + 1], u),
                        Mathf.Lerp(field[i + resolution], field[i + resolution + 1], u), v);
                    if (h > level) land++;
                }
            }
            return land / (4f * (resolution - 1) * (resolution - 1));
        }
    }
}
