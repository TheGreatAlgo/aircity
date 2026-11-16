using System.Collections.Generic;
using System.Linq;
using AirCity.EditorTools.World;
using AirCity.World;
using NUnit.Framework;
using UnityEditor;
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
            world.hasOcean = true; world.seaLevel = 40f;
            for (int i = 0; i < world.heights.Length; i++) world.heights[i] = 40f;
            Assert.IsEmpty(GroundCoverScatter.Generate(world, area: Area));
        }

        [Test]
        public void LowInlandGroundRemainsHabitatUntilAnActualSeaIsPresent()
        {
            var world = Habitat();
            for (int i = 0; i < world.heights.Length; i++) world.heights[i] = 15f;
            Assert.Greater(GroundCoverScatter.Generate(world, area: Area).Length, 1000,
                "An old river's water-height formula must not flood low inland real-world terrain.");
            world.hasOcean = true; world.seaLevel = 40f;
            Assert.IsEmpty(GroundCoverScatter.Generate(world, area: Area));
        }

        [Test]
        public void OutcropsHaveStoneApronsAndWoodlandHasLargeNaturalDebris()
        {
            var world = Habitat();
            var items = GroundCoverScatter.Generate(world, area: Area);
            var rocks = items.Where(p => p.kind == GroundCoverKind.Boulder).ToArray();
            var stones = items.Where(p => p.kind == GroundCoverKind.Stone).ToArray();
            var logs = items.Where(p => p.kind == GroundCoverKind.Log).ToArray();
            Assert.Greater(rocks.Length, 50, "A forest clearing should reveal rocky outcrops, not just tiny grass.");
            Assert.IsTrue(rocks.Any(p => p.scale > 2.4f), "Some outcrops need a silhouette visible from the normal builder camera.");
            Assert.Greater(logs.Length, 2, "Fallen wood should add a different forest-floor silhouette.");
            Assert.IsTrue(stones.Any(s => rocks.Any(b => Vector2.Distance(s.position, b.position) < 9.1f)),
                "Broken stones should gather around larger outcrops.");
            Assert.IsTrue(items.Any(p => p.kind == GroundCoverKind.Shrub && p.scale > 1.6f));
            Assert.IsTrue(items.Any(p => p.kind == GroundCoverKind.Fern && p.scale > 1.5f));
            // The original canopy, including removed roots, determines woodland debris habitat.
            foreach (var log in logs) Assert.Less(log.position.x, 35f);
        }

        [Test]
        public void MappedFieldsAndTownsDoNotFillWithRocksOrForestDebris()
        {
            var world = Habitat();
            world.landCover = new byte[world.heights.Length];
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
                world.landCover[z * ValleyWorld.Resolution + x] = x < 256 ? (byte)10 : z < 256 ? (byte)40 : (byte)50;
            var generated = GroundCoverScatter.Generate(world, area: Area);
            Assert.Greater(generated.Length, 500);
            foreach (var item in generated)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt((item.position.x + ValleyWorld.Size * .5f) / world.Spacing), 0, ValleyWorld.Resolution - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt((item.position.y + ValleyWorld.Size * .5f) / world.Spacing), 0, ValleyWorld.Resolution - 1);
                Assert.AreEqual(10, world.landCover[z * ValleyWorld.Resolution + x]);
            }
        }

        [Test]
        public void SharedModelsFitTheirPlacementFootprintsEvenWhenTiltedAndWidened()
        {
            var library = AssetDatabase.LoadAssetAtPath<GroundCoverLibrary>(GroundCoverSetup.LibraryPath);
            Assert.IsNotNull(library); Assert.GreaterOrEqual(library.generatorVersion, 3);
            Assert.AreEqual(20, library.models.Length);
            foreach (var model in library.models)
            {
                Assert.IsNotNull(model.nearMesh); Assert.IsNotNull(model.farMesh); Assert.IsNotNull(model.material);
                Assert.Greater(model.nearMesh.GetIndexCount(0), 0u);
                Assert.Less(model.farMesh.GetIndexCount(0), model.nearMesh.GetIndexCount(0));
                // The renderer can scale either horizontal axis up by 1/.88. A sphere around every
                // bound corner conservatively includes any slope/yaw, plus shader wind padding.
                var bounds = model.bounds;
                for (int z = -1; z <= 1; z += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int x = -1; x <= 1; x += 2)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    Assert.GreaterOrEqual(GroundCoverScatter.FootprintRadius(model.kind, 1f), corner.magnitude / .88f + .06f,
                        model.name + " needs to reserve its full tilted footprint.");
                }
            }
            foreach (var log in library.models.Where(m => m.kind == GroundCoverKind.Log))
                Assert.AreEqual(0f, log.material.GetFloat("_Wind"), "Fallen trunks must stay grounded.");
        }

        static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude);
            return Vector2.Distance(p, a + d * t);
        }
    }
}
