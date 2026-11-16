using System.Linq;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class WoodlandPresetTests
    {
        [Test]
        public void WoodlandIsAnInlandWoodedValleyWithSmallMappedWaterwaysAndBuildableGround()
        {
            var world = ValleyWorld.Generate(19770616, RegionPreset.Woodland);
            StringAssert.Contains("Snoqualmie Valley", world.landscapeSource);
            Assert.IsFalse(world.HasOcean, "The Woodland preset must not contain the former giant marine inlet.");
            Assert.IsTrue(world.HasInlandWater, "The real Snoqualmie river and small lakes must remain visible.");
            float treeCover = world.landCover.Count(c => c == 10) / (float)world.landCover.Length;
            float waterCover = world.landCover.Count(c => c == 80) / (float)world.landCover.Length;
            Assert.Greater(treeCover, .65f); Assert.That(waterCover, Is.InRange(.01f, .04f));
            float relief = world.heights.Max() - world.heights.Min();
            Assert.That(relief, Is.InRange(100f, 220f), "Woodland should retain gentle real relief distinct from the mountain preset.");
            int sampled = 0, gentle = 0;
            for (int z = 1; z < ValleyWorld.Resolution - 1; z += 3)
            for (int x = 1; x < ValleyWorld.Resolution - 1; x += 3)
            {
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                if (world.IsWater(p)) continue;
                float dx = world.Height(p + Vector2.right * world.Spacing) - world.Height(p - Vector2.right * world.Spacing);
                float dz = world.Height(p + Vector2.up * world.Spacing) - world.Height(p - Vector2.up * world.Spacing);
                if (Mathf.Sqrt(dx * dx + dz * dz) / (world.Spacing * 2f) < .04f) gentle++;
                sampled++;
            }
            Assert.Greater(gentle / (float)sampled, .25f, "The river valley needs substantial low-gradient airport land.");
        }

        [Test]
        public void InlandWoodlandRoadsAvoidMappedWaterAndKeepTheirEngineeredGrades()
        {
            var world = ValleyWorld.Generate(19770616, RegionPreset.Woodland);
            Assert.Greater(world.publicRoad.Count, 12);
            for (int i = 1; i < world.publicRoad.Count; i++)
            {
                float grade = Mathf.Abs(world.roadHeights[i] - world.roadHeights[i - 1]) /
                    Vector2.Distance(world.publicRoad[i], world.publicRoad[i - 1]);
                Assert.LessOrEqual(grade, RegionRoadPlanner.MaximumGrade + .00001f);
                for (int step = 0; step <= 10; step++)
                    Assert.IsFalse(world.IsWater(Vector2.Lerp(world.publicRoad[i - 1], world.publicRoad[i], step / 10f)),
                        "A country road must follow usable ground rather than cover the real river.");
            }
        }
    }
}
