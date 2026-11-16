using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class RegionRoadTests
    {
        [Test]
        public void RegionalProfileLimitsGradesAcrossAnAbruptTerrainStepAndPersistsItsTrafficEntrances()
        {
            var world = Flat(70f); var route = new List<Vector2>();
            for (int i = 0; i <= 32; i++) route.Add(new Vector2(-1600f + i * 100f, 0f));
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = ValleyWorld.Resolution / 2; x < ValleyWorld.Resolution; x++) world.heights[z * ValleyWorld.Resolution + x] = 300f;
            world.publicRoad = route; world.roadHeights = RegionRoadPlanner.Profile(world, route);
            AssertProfile(world);
            var graph = PublicRoadNetwork.FromWorld(world);
            Assert.AreEqual(2, graph.nodes.Count(n => n.outsideConnection));
            Assert.AreEqual(world.roadHeights[16], graph.nodes[16].position.y);
            var clone = graph.Clone(); Assert.AreEqual(2, clone.nodes.Count(n => n.outsideConnection));
            var loaded = JsonUtility.FromJson<PublicRoadNetwork>(JsonUtility.ToJson(graph));
            CollectionAssert.AreEqual(graph.nodes.Select(n => n.position), loaded.nodes.Select(n => n.position));
            Assert.AreEqual(2, loaded.nodes.Count(n => n.outsideConnection));
        }

        [Test]
        public void IslandRoadFollowsDryCoastalGroundInsteadOfCrossingTheVolcanicPeakOrSea()
        {
            var world = Flat(60f); world.hasOcean = true; world.seaLevel = 40f;
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                float radius = p.magnitude;
                world.heights[z * ValleyWorld.Resolution + x] = radius > 2500f ? 24f : 65f + Mathf.Exp(-radius * radius / 700000f) * 400f;
            }
            world.publicRoad = new List<Vector2> { new Vector2(0f, -3200f), new Vector2(0f, 3200f) };
            RegionRoadPlanner.Plan(world);
            Assert.Greater(world.publicRoad.Count, 8);
            Assert.Greater(Vector2.Distance(world.publicRoad[0], world.publicRoad[world.publicRoad.Count - 1]), 3000f);
            AssertProfile(world);
            foreach (var point in world.publicRoad)
            {
                Assert.IsFalse(world.IsWater(point));
                Assert.Less(world.Height(point), 150f, "The existing regional road should route round the central volcano.");
            }
            for (int i = 1; i < world.publicRoad.Count; i++)
                for (int k = 0; k <= 10; k++) Assert.IsFalse(world.IsWater(Vector2.Lerp(world.publicRoad[i - 1], world.publicRoad[i], k / 10f)));
        }

        [TestCase(0f)] [TestCase(6.25f)]
        public void GroundRoadHasSupportingSoilAndAnEmbankmentAlongAnEightPercentSlope(float roadZ)
        {
            var world = Flat(20f); var graph = new PublicRoadNetwork();
            int a = graph.AddNode(new Vector3(-300f, 56f, roadZ)), b = graph.AddNode(new Vector3(300f, 104f, roadZ));
            graph.AddSegment(a, b, PublicRoadStyle.Rural, RoadStructure.Ground);
            var geometry = new PublicRoadGeometry(graph, null, null, world.Height);
            var terrain = new TerrainData { heightmapResolution = ValleyWorld.Resolution,
                size = new Vector3(ValleyWorld.Size, ValleyWorld.HeightRange, ValleyWorld.Size) };
            try
            {
                var heights = geometry.GradedHeights(world); var map = new float[ValleyWorld.Resolution, ValleyWorld.Resolution];
                for (int z = 0; z < ValleyWorld.Resolution; z++)
                for (int x = 0; x < ValleyWorld.Resolution; x++) map[z, x] = heights[z * ValleyWorld.Resolution + x] / ValleyWorld.HeightRange;
                terrain.SetHeights(0, 0, map);
                foreach (var p in geometry.pavement.vertices)
                {
                    if (Mathf.Abs(p.x) > 270f) continue;
                    float ground = terrain.GetInterpolatedHeight((p.x + ValleyWorld.Size * .5f) / ValleyWorld.Size, (p.z + ValleyWorld.Size * .5f) / ValleyWorld.Size);
                    Assert.That(p.y - ground, Is.InRange(.019f, .151f), "The road must sit on its roadbed, without a trench beneath it.");
                }
                float middle = terrain.GetInterpolatedHeight(.5f, .5f);
                float fillSlope = terrain.GetInterpolatedHeight(.5f, .5f + 70f / ValleyWorld.Size);
                Assert.Greater(middle, 79f); Assert.Less(middle, 81f);
                Assert.That(fillSlope, Is.InRange(40f, 65f), "Filled soil must slope down and meet natural ground.");
                Assert.AreEqual(20f, terrain.GetInterpolatedHeight(.5f, .5f + 250f / ValleyWorld.Size), .01f);
            }
            finally { UnityEngine.Object.DestroyImmediate(terrain); Destroy(geometry); }
        }

        [TestCase(GameMode.Career)] [TestCase(GameMode.Sandbox)]
        public void RefusingAnUnbuildablySteepStreetNeverChargesOrChangesTheRoadGraph(GameMode mode)
        {
            var state = new CareerState { world = Flat(80f), mode = mode, funds = 25000 };
            var graph = new PublicRoadNetwork(); int start = graph.AddNode(new Vector3(0f, 80f, 0f));
            string before = JsonUtility.ToJson(graph);
            Assert.IsFalse(PublicRoadConstruction.Build(state, graph, start, new Vector3(100f, 125f, 0f), -1, -1,
                PublicRoadStyle.Rural, false, out _, out _, out string previewProblem));
            Assert.IsFalse(PublicRoadConstruction.Build(state, graph, start, new Vector3(100f, 125f, 0f), -1, -1,
                PublicRoadStyle.Rural, true, out var result, out _, out string why));
            Assert.AreEqual(previewProblem, why, "The red preview must explain the same refusal as the transaction.");
            StringAssert.Contains("steep", why); Assert.AreEqual(25000, state.funds);
            Assert.AreSame(graph, result); Assert.AreEqual(before, JsonUtility.ToJson(graph));
        }

        [Test]
        public void AFlatGroundStreetCannotCrossAnOceanChannelWithoutABridge()
        {
            var world = Flat(80f); world.hasOcean = true; world.seaLevel = 40f;
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
                if (Mathf.Abs(x * world.Spacing - ValleyWorld.Size * .5f) < 40f) world.heights[z * ValleyWorld.Resolution + x] = 20f;
            var state = new CareerState { world = world, funds = 25000 };
            var graph = new PublicRoadNetwork(); int start = graph.AddNode(new Vector3(-150f, 80f, 0f));
            string before = JsonUtility.ToJson(graph);
            Assert.IsFalse(PublicRoadConstruction.Build(state, graph, start, new Vector3(150f, 80f, 0f), -1, -1,
                PublicRoadStyle.Rural, false, out _, out _, out string previewProblem));
            Assert.IsFalse(PublicRoadConstruction.Build(state, graph, start, new Vector3(150f, 80f, 0f), -1, -1,
                PublicRoadStyle.Rural, true, out _, out _, out string why));
            Assert.AreEqual(previewProblem, why);
            StringAssert.Contains("water", why); Assert.AreEqual(25000, state.funds);
            Assert.AreEqual(before, JsonUtility.ToJson(graph));
        }

        [Test]
        public void AStreetWithADryCentrelineCannotHangItsShoulderOverTheShore()
        {
            var world = Flat(80f); world.hasOcean = true; world.seaLevel = 40f;
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
                if (x * world.Spacing - ValleyWorld.Size * .5f > 0f) world.heights[z * ValleyWorld.Resolution + x] = 20f;
            var state = new CareerState { world = world, funds = 25000 };
            var graph = new PublicRoadNetwork(); int start = graph.AddNode(new Vector3(4f, 80f, -150f));
            Assert.IsFalse(world.IsWater(new Vector2(4f, 0f)), "The centre is still on dry interpolated shoreline terrain.");
            Assert.IsFalse(PublicRoadConstruction.Build(state, graph, start, new Vector3(4f, 80f, 150f), -1, -1,
                PublicRoadStyle.Rural, false, out _, out _, out string why));
            StringAssert.Contains("water", why); Assert.AreEqual(25000, state.funds); Assert.AreEqual(1, graph.nodes.Count);
        }

        static void AssertProfile(ValleyWorld world)
        {
            Assert.AreEqual(world.publicRoad.Count, world.roadHeights.Length);
            for (int i = 1; i < world.publicRoad.Count; i++)
            {
                float grade = Mathf.Abs(world.roadHeights[i] - world.roadHeights[i - 1]) / Vector2.Distance(world.publicRoad[i], world.publicRoad[i - 1]);
                Assert.LessOrEqual(grade, RegionRoadPlanner.MaximumGrade + .00001f);
            }
        }
        static ValleyWorld Flat(float height)
        {
            var world = new ValleyWorld { generatorVersion = 4, heights = new float[ValleyWorld.Resolution * ValleyWorld.Resolution] };
            for (int i = 0; i < world.heights.Length; i++) world.heights[i] = height;
            return world;
        }
        static void Destroy(PublicRoadGeometry geometry)
        { foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures, geometry.kerbMesh, geometry.medianGrass }) UnityEngine.Object.DestroyImmediate(mesh); }
    }
}
