using System.Linq;
using AirCity.EditorTools.World;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class PublicRoadTests
    {
        [Test]
        public void AtGradeCrossingCreatesOneJunctionAndConnectsAllFourArms()
        {
            var graph = new PublicRoadNetwork();
            int west = graph.AddNode(new Vector3(-50f, 0f, 0f)), east = graph.AddNode(new Vector3(50f, 0f, 0f));
            graph.AddSegment(west, east, PublicRoadStyle.Urban, RoadStructure.Ground);
            int south = graph.AddNode(new Vector3(0f, 0f, -50f)), north = graph.AddNode(new Vector3(0f, 0f, 50f));
            Assert.IsTrue(graph.Connect(south, north, PublicRoadStyle.Urban, RoadStructure.Ground, out string why), why);
            Assert.AreEqual(5, graph.nodes.Count); Assert.AreEqual(4, graph.segments.Count);
            var junction = graph.nodes.Single(n => graph.At(n.id).Count() == 4);
            Assert.IsTrue(graph.HasRoute(west, north)); Assert.IsTrue(graph.HasRoute(south, east));
            var movements = graph.LaneConnections(junction.id);
            Assert.AreEqual(12, movements.Count); Assert.IsFalse(movements.Any(m => m.incoming == m.outgoing));
            Assert.AreEqual(4, movements.Count(m => m.turn == RoadTurn.Left && m.yieldsToOpposingTraffic));
            Assert.AreEqual(4, movements.Count(m => m.turn == RoadTurn.Right));
            Assert.AreEqual(4, movements.Count(m => m.turn == RoadTurn.Straight));
        }

        [Test]
        public void ElevatedHighwayDoesNotConnectToOrGradeAwayTheRoadBelow()
        {
            var graph = new PublicRoadNetwork();
            int west = graph.AddNode(new Vector3(-60f, 0f, 0f)), east = graph.AddNode(new Vector3(60f, 0f, 0f));
            graph.AddSegment(west, east, PublicRoadStyle.Urban, RoadStructure.Ground);
            int south = graph.AddNode(new Vector3(0f, 8f, -60f)), north = graph.AddNode(new Vector3(0f, 8f, 60f));
            Assert.IsTrue(graph.Connect(south, north, PublicRoadStyle.Highway, RoadStructure.Elevated, out string why), why);
            Assert.AreEqual(4, graph.nodes.Count); Assert.AreEqual(2, graph.segments.Count);
            Assert.IsFalse(graph.HasRoute(west, north)); Assert.IsFalse(graph.HasRoute(south, east));
            var geometry = new PublicRoadGeometry(graph, null, null, _ => 0f);
            try
            {
                geometry.SampleGround(Vector2.zero, out float height, out _, out _); Assert.AreEqual(0f, height);
                Assert.IsTrue(geometry.pavement.vertices.Any(p => p.y > 8f));
                Assert.IsTrue(geometry.pavement.vertices.Any(p => p.y < .3f));
                Assert.Greater(geometry.structures.vertexCount, 0);
            }
            finally { Destroy(geometry); }
        }

        [TestCase(1f)] [TestCase(4f)] [TestCase(6f)]
        public void CrossingWithoutEnoughClearanceIsRefusedBeforeChangingSegments(float height)
        {
            var graph = new PublicRoadNetwork();
            int a = graph.AddNode(new Vector3(-50f, 0f, 0f)), b = graph.AddNode(new Vector3(50f, 0f, 0f));
            graph.AddSegment(a, b, PublicRoadStyle.Urban, RoadStructure.Ground);
            int c = graph.AddNode(new Vector3(0f, height, -50f)), d = graph.AddNode(new Vector3(0f, height, 50f));
            var before = JsonUtility.ToJson(graph);
            Assert.IsFalse(graph.Connect(c, d, PublicRoadStyle.Urban, RoadStructure.Elevated, out _));
            Assert.AreEqual(before, JsonUtility.ToJson(graph));
        }

        [Test]
        public void HighwayAtGradeRequiresAnExplicitRampAndMerge()
        {
            var graph = new PublicRoadNetwork();
            int a = graph.AddNode(new Vector3(-50f, 0f, 0f)), b = graph.AddNode(new Vector3(50f, 0f, 0f));
            graph.AddSegment(a, b, PublicRoadStyle.Highway, RoadStructure.Ground);
            int c = graph.AddNode(new Vector3(0f, 0f, -50f)), d = graph.AddNode(new Vector3(0f, 0f, 50f));
            Assert.IsFalse(graph.Connect(c, d, PublicRoadStyle.Urban, RoadStructure.Ground, out string why));
            StringAssert.Contains("ramp", why);
            var ramp = PublicRoadKitSetup.Layout("HighwayOverpass");
            var start = ramp.nodes.Single(n => n.position.z < -180f);
            var terminal = ramp.nodes.Single(n => n.terminalEntrance == "terminal-departures");
            Assert.IsTrue(ramp.HasRoute(start.id, terminal.id)); Assert.IsFalse(ramp.HasRoute(terminal.id, start.id));
        }

        [Test]
        public void SignalPhasesIncludeAmberAndAllRedAndNeverReleaseConflictingApproaches()
        {
            var graph = PublicRoadKitSetup.Layout("SignalCrossroads");
            var junction = graph.nodes.Single(n => n.control == JunctionControl.TrafficLights);
            var north = graph.At(junction.id).Single(e => PublicRoadSignals.Direction(graph, junction.id, e).y > .9f);
            var south = graph.At(junction.id).Single(e => PublicRoadSignals.Direction(graph, junction.id, e).y < -.9f);
            var west = graph.At(junction.id).Single(e => PublicRoadSignals.Direction(graph, junction.id, e).x < -.9f);
            Assert.AreEqual(PublicRoadSignals.Group(graph, junction.id, north.id), PublicRoadSignals.Group(graph, junction.id, south.id));
            Assert.AreNotEqual(PublicRoadSignals.Group(graph, junction.id, north.id), PublicRoadSignals.Group(graph, junction.id, west.id));
            for (float t = 0f; t < PublicRoadSignals.CycleSeconds * 3f; t += .125f)
                Assert.IsFalse(PublicRoadSignals.Signal(0, t) != RoadSignal.Red && PublicRoadSignals.Signal(1, t) != RoadSignal.Red);
            Assert.AreEqual(RoadSignal.Amber, PublicRoadSignals.Signal(0, 25f));
            Assert.AreEqual(RoadSignal.Red, PublicRoadSignals.Signal(0, 27.5f));
            Assert.AreEqual(RoadSignal.Red, PublicRoadSignals.Signal(1, 27.5f));
            Assert.AreEqual(RoadSignal.Green, PublicRoadSignals.Signal(1, 30f));
        }

        [Test]
        public void CustomAirportRoadQuotesItsLengthAndSavesTheConnectedTerminalRoute()
        {
            var state = CareerState.New(42, false); Assert.IsTrue(state.Build(out _, 0));
            while (state.Step != CareerStep.AccessRoad) Assert.IsTrue(state.Build(out string why), why);
            var entrance = state.Site.Entrance;
            var route = new[] { entrance, entrance + new Vector2(180f, -180f), new Vector2(ValleyWorld.RoadX, entrance.y - 180f) };
            int price = state.Quote(accessRoute: route).cost, funds = state.funds;
            Assert.Greater(price, state.Quote().cost);
            Assert.IsTrue(state.Build(out string problem, accessRoute: route), problem);
            Assert.AreEqual(funds - price, state.funds);
            var resumed = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(state));
            Assert.IsTrue(resumed.Validate(out problem), problem);
            CollectionAssert.AreEqual(route, resumed.accessRoadRoute);
            var graph = resumed.publicRoads;
            var terminal = graph.nodes.Single(n => n.terminalEntrance == "airport-reception");
            var outside = graph.nodes.Where(n => Mathf.Abs(n.position.z) >= 3199f).ToList();
            Assert.IsTrue(outside.All(n => graph.HasRoute(terminal.id, n.id)));
            Assert.AreEqual(1, graph.nodes.Count(n => n.control == JunctionControl.TrafficLights));
        }

        [Test]
        public void AirportRoadCannotCrossTheRunwayAndARefusalDoesNotSpendOrClearAnything()
        {
            var state = CareerState.New(42, false); state.Build(out _, 0);
            while (state.Step != CareerStep.AccessRoad) state.Build(out _);
            var before = JsonUtility.ToJson(state);
            var route = new[] { state.Site.Entrance, state.Site.centre, new Vector2(ValleyWorld.RoadX, state.Site.centre.y) };
            Assert.IsFalse(state.Build(out _, accessRoute: route)); Assert.AreEqual(before, JsonUtility.ToJson(state));
        }

        [Test]
        public void OriginalCareerWithImplicitRoadMigratesToAnExplicitConnectedNetwork()
        {
            var state = CareerState.New(17, false); state.Build(out _, 0);
            while (!state.IsOpen) Assert.IsTrue(state.Build(out string why), why);
            state.publicRoads = null; state.accessRoadRoute = null;
            var restored = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(state));
            Assert.IsTrue(restored.Validate(out string problem), problem);
            var graph = restored.RoadNetwork();
            Assert.IsTrue(graph.Validate(out problem), problem);
            var entrance = graph.nodes.Single(n => n.terminalEntrance == "airport-reception");
            Assert.IsTrue(graph.nodes.Where(n => Mathf.Abs(n.position.z) >= 3199f).All(n => graph.HasRoute(entrance.id, n.id)));
        }

        [Test]
        public void TreeAssetsHaveDistinctSpeciesAndSmallerDistanceLevelsWithRealAlphaFoliage()
        {
            WorldAssetSetup.Ensure(); var library = AssetDatabase.LoadAssetAtPath<TreeLibrary>(WorldAssetSetup.TreesPath);
            Assert.AreEqual(6, library.variants.Length); Assert.IsTrue(library.foliage.enableInstancing);
            Assert.IsTrue(library.foliage.IsKeywordEnabled("_FOLIAGE")); Assert.IsNotNull(library.foliage.GetTexture("_BaseMap"));
            foreach (var tree in library.variants)
            {
                Assert.AreEqual(3, tree.levels.Length);
                Assert.That(tree.levels[0].bounds.size.y, Is.InRange(22f, 28f));
                Assert.Greater(tree.levels[0].triangles.Length, tree.levels[1].triangles.Length);
                Assert.Greater(tree.levels[1].triangles.Length, tree.levels[2].triangles.Length);
                Assert.AreEqual(2, tree.levels[0].subMeshCount);
            }
        }

        [TestCase(42)] [TestCase(19770616)] [TestCase(17)]
        public void CustomJunctionPavementStaysAboveTheInterpolatedRoadbed(int seed)
        {
            var state = CareerState.New(seed, false); state.Build(out _, 0);
            while (state.Step != CareerStep.AccessRoad) state.Build(out _);
            var start = state.Site.Entrance;
            Assert.IsTrue(state.Build(out string why, accessRoute: new[] { start, start + new Vector2(180f, -180f), new Vector2(ValleyWorld.RoadX, start.y - 180f) }), why);
            var geometry = new PublicRoadGeometry(state.publicRoads, null, null, state.world.Height);
            var terrain = new TerrainData { heightmapResolution = ValleyWorld.Resolution, size = new Vector3(ValleyWorld.Size, ValleyWorld.HeightRange, ValleyWorld.Size) };
            try
            {
                var heights = geometry.GradedHeights(state.world); var map = new float[ValleyWorld.Resolution, ValleyWorld.Resolution];
                for (int z = 0; z < ValleyWorld.Resolution; z++)
                for (int x = 0; x < ValleyWorld.Resolution; x++) map[z, x] = heights[z * ValleyWorld.Resolution + x] / ValleyWorld.HeightRange;
                terrain.SetHeights(0, 0, map);
                float minimum = float.MaxValue;
                foreach (var p in geometry.pavement.vertices)
                {
                    float h = terrain.GetInterpolatedHeight((p.x + ValleyWorld.Size * .5f) / ValleyWorld.Size, (p.z + ValleyWorld.Size * .5f) / ValleyWorld.Size);
                    minimum = Mathf.Min(minimum, p.y - h);
                }
                Assert.GreaterOrEqual(minimum, .02f, "Terrain must not cover a ground road near a flattened junction.");
            }
            finally { Object.DestroyImmediate(terrain); Destroy(geometry); }
        }

        static void Destroy(PublicRoadGeometry geometry)
        { foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures }) Object.DestroyImmediate(mesh); }
    }
}
