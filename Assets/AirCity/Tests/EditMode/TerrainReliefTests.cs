using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class TerrainReliefTests
    {
        [TestCase(42)]
        [TestCase(19770616)]
        public void StarterPocketsHaveRollingGroundAndSmallerLandforms(int seed)
        {
            var world = ValleyWorld.GenerateLegacy(seed);
            Assert.AreEqual(3, world.generatorVersion);
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
            for (float x = -550f; x <= 550f; x += 50f)
            {
                float approach = valley.elevation + Mathf.Max(0f, Mathf.Abs(x) - 350f) * Mathf.Tan(3f * Mathf.Deg2Rad);
                Assert.LessOrEqual(world.Height(valley.centre + Vector2.right * x), approach + 3f);
            }
        }

        [TestCase(RegionPreset.Woodland)]
        [TestCase(RegionPreset.Mountains)]
        [TestCase(RegionPreset.Tropical)]
        [TestCase(RegionPreset.Desert)]
        public void RealLandscapeBiomesHaveValidReliefDryVegetationAndRecordedSources(RegionPreset preset)
        {
            var world = ValleyWorld.Generate(42, preset);
            Assert.IsTrue(world.Validate(out string why), why);
            Assert.AreEqual(preset, world.preset);
            Assert.IsFalse(string.IsNullOrEmpty(world.landscapeSource));
            Assert.Greater(world.sourceExtent, 6000f);
            float low = float.MaxValue, high = float.MinValue;
            foreach (float h in world.heights) { low = Mathf.Min(low,h); high = Mathf.Max(high,h); }
            Assert.Greater(high-low, preset == RegionPreset.Woodland ? 100f : 200f, "Real relief must survive the game-scale height transform.");
            Assert.IsFalse(world.HasRiver, "A synthetic straight river must not cut across real ridges.");
            foreach (var tree in world.trees) Assert.IsFalse(world.IsWater(tree.position), "Trees cannot grow in the ocean or lagoon.");
            if (preset == RegionPreset.Tropical)
            {
                Assert.IsTrue(world.HasOcean); int sea = 0;
                foreach(float h in world.heights) if(h < world.SeaLevel) sea++;
                Assert.That(sea / (float)world.heights.Length, Is.InRange(.25f,.85f));
                Assert.Greater(high, world.SeaLevel + 350f, "Tropical needs a volcanic highland as well as coastline.");
            }
        }

        [Test]
        public void InternalSeedsPreserveNorthUpGeographyAndReproduceSceneryExactly()
        {
            var first = ValleyWorld.Generate(42);
            var different = ValleyWorld.Generate(43);
            var repeated = ValleyWorld.Generate(42);
            CollectionAssert.AreEqual(first.heights, repeated.heights);
            CollectionAssert.AreEqual(first.publicRoad, repeated.publicRoad);
            CollectionAssert.AreEqual(first.heights, different.heights, "An internal seed must never rotate or change the chosen real location.");
            Assert.IsTrue(first.hasCoordinates);
            Assert.AreEqual(first.latitude, different.latitude); Assert.AreEqual(first.longitude, different.longitude);
            CollectionAssert.AreEqual(first.publicRoad, different.publicRoad, "The public road follows the same chosen geography.");
            Assert.AreNotEqual(first.trees[100].position, different.trees[100].position, "Internal variation can change scenery placement.");
            CollectionAssert.AreEqual(first.roadHeights, repeated.roadHeights);
        }

        [Test]
        public void RandomBiomeAndHydrologySurviveSavingWithoutRegeneration()
        {
            var world = ValleyWorld.Generate(19770616, RegionPreset.Random);
            var repeated = ValleyWorld.Generate(19770616, RegionPreset.Random);
            Assert.AreNotEqual(RegionPreset.Random, world.preset);
            Assert.AreEqual(world.preset, repeated.preset);
            CollectionAssert.AreEqual(world.heights, repeated.heights);
            var loaded = JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(world));
            Assert.IsTrue(loaded.Validate(out string why), why);
            Assert.AreEqual(world.preset, loaded.preset);
            foreach (float z in new[] { -3200f, -1200f, 0f, 1700f, 3200f })
            {
                Assert.AreEqual(world.RiverX(z), loaded.RiverX(z));
                Assert.AreEqual(world.RoadAt(z), loaded.RoadAt(z));
            }
            CollectionAssert.AreEqual(world.heights, loaded.heights);
        }

        [Test]
        public void DesertHasSparseTreesAndTropicalKeepsOceanAndVolcanicRelief()
        {
            var desert = ValleyWorld.Generate(42, RegionPreset.Desert);
            var woodland = ValleyWorld.Generate(42, RegionPreset.Woodland);
            var mountains = ValleyWorld.Generate(42, RegionPreset.Mountains);
            var tropical = ValleyWorld.Generate(42, RegionPreset.Tropical);
            Assert.Less(desert.trees.Count, woodland.trees.Count / 20);
            float mountainPeak = 0f, tropicalPeak = 0f;
            foreach (float h in mountains.heights) mountainPeak = Mathf.Max(mountainPeak, h);
            foreach (float h in tropical.heights) tropicalPeak = Mathf.Max(tropicalPeak, h);
            Assert.Greater(mountainPeak, 500f); Assert.Greater(tropicalPeak, 450f);
            Assert.IsTrue(tropical.HasOcean); Assert.IsFalse(desert.HasOcean);
        }

        [Test]
        public void ColoradoWaterKeepsItsMappedElevationAndExcludesRiverbedVegetation()
        {
            var world = ValleyWorld.Generate(692835, RegionPreset.Desert);
            Assert.IsTrue(world.HasInlandWater); Assert.IsFalse(world.HasOcean);
            int wetSamples = 0, checkedQuads = 0, drySamples = 0;
            var surface = world.InlandSurfaceHeights;
            Vector2 riverPoint = default;
            for (int z = 1; z < ValleyWorld.Resolution - 2; z++)
            for (int x = 1; x < ValleyWorld.Resolution - 2; x++)
            {
                int i = z * ValleyWorld.Resolution + x;
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                if (world.waterHeights[i] > 0f)
                {
                    wetSamples++;
                    Assert.AreEqual(80, world.landCover[i]);
                    Assert.That(surface[i] - world.heights[i], Is.InRange(.05f, 1.501f),
                        "Conditioned river levels need a shallow smooth bed, including near the bank.");
                    Assert.IsTrue(world.IsWater(p), "Mapped Colorado samples must contain water.");
                    if (checkedQuads >= 64 || world.waterHeights[i + 1] <= 0f ||
                        world.waterHeights[i + ValleyWorld.Resolution] <= 0f || world.waterHeights[i + ValleyWorld.Resolution + 1] <= 0f) continue;
                    var middle = p + Vector2.one * world.Spacing * .5f;
                    float expected = (surface[i] + surface[i + 1] +
                        surface[i + ValleyWorld.Resolution] + surface[i + ValleyWorld.Resolution + 1]) * .25f;
                    Assert.AreEqual(expected, world.InlandWaterHeight(middle), .001f);
                    Assert.IsTrue(world.IsWater(middle), "The surface must remain continuous between wet samples.");
                    if (checkedQuads++ == 0) riverPoint = middle;
                }
                else if (drySamples < 32 && surface[i] == 0f && surface[i + 1] == 0f &&
                    surface[i + ValleyWorld.Resolution] == 0f && surface[i + ValleyWorld.Resolution + 1] == 0f)
                {
                    var dry = p + Vector2.one * world.Spacing * .5f;
                    Assert.AreEqual(0f, world.InlandWaterHeight(dry)); Assert.IsFalse(world.IsWater(dry)); drySamples++;
                }
            }
            Assert.Greater(wetSamples, 1000); Assert.AreEqual(64, checkedQuads); Assert.AreEqual(32, drySamples);
            var patch = new Rect(riverPoint.x - 60f, riverPoint.y - 60f, 120f, 120f);
            foreach (var prop in GroundCoverScatter.Generate(world, area: patch))
                Assert.IsFalse(world.IsWater(prop.position), "Groundcover and its cluster satellites must stay out of the mapped river.");
        }

        [Test]
        public void InlandWaterSurvivesDetachedSnapshotsAndRejectsCorruptSavedLevels()
        {
            var state = CareerState.New(692835, preset: RegionPreset.Desert);
            Assert.IsTrue(state.StartRegion(out string why), why);
            var copy = CareerSnapshot.Capture(state);
            Assert.AreNotSame(state.world.waterHeights, copy.world.waterHeights);
            var loaded = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(copy));
            Assert.IsTrue(loaded.Validate(out why), why);
            CollectionAssert.AreEqual(state.world.waterHeights, loaded.world.waterHeights);
            CollectionAssert.AreEqual(state.world.heights, loaded.world.heights);
            int wet = System.Array.FindIndex(state.world.waterHeights, value => value > 0f);
            Assert.GreaterOrEqual(wet, 0);
            float original = state.world.waterHeights[wet]; copy.world.waterHeights[wet] += 2f;
            Assert.AreEqual(original, state.world.waterHeights[wet], "A queued snapshot cannot edit live river levels.");
            loaded.world.waterHeights[wet] = float.NaN;
            Assert.IsFalse(loaded.world.Validate(out _));
            loaded.world.waterHeights = new float[3];
            Assert.IsFalse(loaded.world.Validate(out _));
        }

        [Test]
        public void LevelBrushFlattensOnlyTheChosenPatchOfNaturalRelief()
        {
            var world = ValleyWorld.GenerateLegacy(42);
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
            var world = ValleyWorld.GenerateLegacy(17);
            Assert.IsFalse(world.HasInlandWater); Assert.IsNull(world.waterHeights);
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
