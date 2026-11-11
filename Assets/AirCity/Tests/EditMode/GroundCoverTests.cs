using System.Collections.Generic;
using System.Linq;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class GroundCoverTests
    {
        static readonly Rect Area = new Rect(-320f, -320f, 640f, 640f);

        static ValleyWorld Habitat()
        {
            var world = new ValleyWorld { seed = 57413, heights = new float[ValleyWorld.Resolution * ValleyWorld.Resolution] };
            for (int i = 0; i < world.heights.Length; i++) world.heights[i] = 80f;
            // A small forest patch beside a meadow: both habitats should have their own understory.
            for (int z = -320; z <= 320; z += 24)
            for (int x = -320; x <= 0; x += 24) world.trees.Add(new ForestTree { position = new Vector2(x, z), height = 20f });
            return world;
        }

        [Test]
        public void LocalCandidatesAreRepeatableAndTreeClearingDoesNotRerollUnderstory()
        {
            var world = Habitat();
            var first = GroundCoverScatter.Generate(world, area: Area);
            Assert.Greater(first.Length, 1000);
            foreach (GroundCoverKind kind in System.Enum.GetValues(typeof(GroundCoverKind)))
                Assert.IsTrue(first.Any(p => p.kind == kind), $"The habitat needs {kind} as well as surface colour.");
            foreach (var tree in world.trees) { tree.removed = true; world.MarkGround(tree.position, 9f, .3f); }
            var cleared = GroundCoverScatter.Generate(world, area: Area);
            Assert.AreEqual(first.Length, cleared.Length);
            for (int i = 0; i < first.Length; i++)
            {
                Assert.AreEqual(first[i].position, cleared[i].position);
                Assert.AreEqual(first[i].kind, cleared[i].kind);
                Assert.AreEqual(first[i].scale, cleared[i].scale);
                Assert.AreEqual(first[i].variation, cleared[i].variation);
            }
        }

        [Test]
        public void EarthworkRemovesGroundPropsWithoutMovingTheUndisturbedSurvivors()
        {
            var world = Habitat();
            var first = GroundCoverScatter.Generate(world, area: Area);
            world.LevelBrush(Vector2.zero, 150f, 80f, Area);
            var after = GroundCoverScatter.Generate(world, area: Area);
            Assert.Less(after.Length, first.Length - 100);
            Assert.IsFalse(after.Any(p => p.position.magnitude < 100f), "A levelled building area must not sprout rocks or plants.");
            var originalByPosition = first.ToDictionary(p => p.position);
            foreach (var survivor in after)
            {
                Assert.IsTrue(originalByPosition.TryGetValue(survivor.position, out var original));
                Assert.AreEqual(original.kind, survivor.kind); Assert.AreEqual(original.scale, survivor.scale);
            }
        }

        [Test]
        public void TreeMarksBesideFractionalEarthworkKeepAllSurvivingPlantsAndClusterSatellites()
        {
            var world = Habitat();
            var untouched = GroundCoverScatter.Generate(world, area: Area);
            // The brush leaves a blend of partial grading samples around its edge.
            world.LevelBrush(Vector2.zero, 160f, 80f, Area);
            var graded = GroundCoverScatter.Generate(world, area: Area);
            Assert.Less(graded.Length, untouched.Length - 100, "Real grading must still remove groundcover.");
            bool hadPartialEarthwork = world.disturbance.Any(d => d > .3f && d < .9f);
            Assert.IsTrue(hadPartialEarthwork);
            // Clear trees densely along and beyond the grading edge. In particular these marks raise
            // neighbouring low samples to .3, which must not combine with earthwork to erase more plants.
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                if (Area.Contains(p)) world.MarkGround(p, 9f, .3f);
            }
            foreach (var tree in world.trees) tree.removed = true;
            var cleared = GroundCoverScatter.Generate(world, area: Area);
            Assert.AreEqual(graded.Length, cleared.Length, "A later rebuild/Continue must retain the same plants as the clear stroke.");
            for (int i = 0; i < graded.Length; i++)
            {
                Assert.AreEqual(graded[i].position, cleared[i].position);
                Assert.AreEqual(graded[i].kind, cleared[i].kind);
                Assert.AreEqual(graded[i].scale, cleared[i].scale);
                Assert.AreEqual(graded[i].yaw, cleared[i].yaw);
                Assert.AreEqual(graded[i].variation, cleared[i].variation);
            }
        }

        [Test]
        public void RoadAndAirportExclusionsAccountForTheWholePropFootprint()
        {
            var world = Habitat(); var graph = new PublicRoadNetwork();
            int a = graph.AddNode(new Vector3(-300f, 80f, 0f)), b = graph.AddNode(new Vector3(300f, 80f, 0f));
            graph.AddSegment(a, b, PublicRoadStyle.Rural, RoadStructure.Ground);
            var geometry = new PublicRoadGeometry(graph, null, null, world.Height);
            var runway = new GroundCoverExclusion(new Vector2(-240f, 140f), new Vector2(240f, 140f), 14f);
            var exclusions = new List<GroundCoverExclusion> { runway };
            try
            {
                var generated = GroundCoverScatter.Generate(world, geometry, exclusions, Area);
                Assert.Greater(generated.Length, 500);
                foreach (var item in generated)
                {
                    float radius = GroundCoverScatter.FootprintRadius(item.kind, item.scale);
                    Assert.GreaterOrEqual(Distance(item.position, new Vector2(-300f, 0f), new Vector2(300f, 0f)), 5.25f + radius - .001f);
                    Assert.GreaterOrEqual(Distance(item.position, runway.a, runway.b), runway.radius + radius - .001f);
                }
            }
            finally
            { foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures }) Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SubareaGenerationKeepsTheSameWorldCandidatesAndWaterStaysClear()
        {
            var world = Habitat();
            var whole = GroundCoverScatter.Generate(world, area: Area);
            var smallArea = new Rect(-123f, -97f, 240f, 185f);
            var small = GroundCoverScatter.Generate(world, area: smallArea);
            var expected = whole.Where(p => smallArea.Contains(p.position)).Select(p => p.position).ToArray();
            CollectionAssert.AreEqual(expected, small.Select(p => p.position).ToArray());
            // Final candidates include local satellites after the original cell candidates. A tiny query
            // around one must find it even though its parent centre is outside the requested area.
            var edgePlant = whole[whole.Length - 1];
            var tinyArea = new Rect(edgePlant.position.x - .05f, edgePlant.position.y - .05f, .1f, .1f);
            var tiny = GroundCoverScatter.Generate(world, area: tinyArea);
            CollectionAssert.Contains(tiny.Select(p => p.position).ToArray(), edgePlant.position);
            for (int i = 0; i < world.heights.Length; i++) world.heights[i] = 40f;
            Assert.IsEmpty(GroundCoverScatter.Generate(world, area: Area));
        }

        static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
            return Vector2.Distance(p, a + d * t);
        }
    }
}
