using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.Vehicles;
using AirCity.World;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public sealed class SharedGameTests
    {
        readonly List<Object> spawned = new List<Object>();
        [TearDown] public void Cleanup() { foreach (var o in spawned) if (o != null) Object.DestroyImmediate(o); spawned.Clear(); }

        static PublicRoadNetwork Road(out int from, out int segment)
        {
            var net = new PublicRoadNetwork();
            int a = net.AddNode(new Vector3(-100, 0, 0)), b = net.AddNode(new Vector3(100, 0, 0));
            segment = net.AddSegment(a, b, PublicRoadStyle.Urban, RoadStructure.Ground).id;
            from = net.AddNode(new Vector3(0, 0, -100)); return net;
        }

        [TestCase(GameMode.Career)] [TestCase(GameMode.Sandbox)]
        public void SameRoadTransactionBuildsATJunctionAndOnlyCareerPays(GameMode mode)
        {
            var net = Road(out int from, out int segment);
            var state = new CareerState { mode = mode, funds = 100000 };
            string before = JsonUtility.ToJson(net);
            Assert.That(PublicRoadConstruction.Build(state, net, from, Vector3.zero, -1, segment, PublicRoadStyle.Urban, true,
                out var result, out int price, out string problem), Is.True, problem);
            Assert.That(JsonUtility.ToJson(net), Is.EqualTo(before), "The input graph is never mutated.");
            Assert.That(result.segments.Count, Is.EqualTo(3));
            Assert.That(result.Validate(out problem), Is.True, problem);
            Assert.That(state.funds, Is.EqualTo(mode == GameMode.Career ? 100000 - price : 100000));
        }

        [Test]
        public void PreviewAndUnaffordableRoadDoNotSplitTheLiveRoadOrSpendMoney()
        {
            var net = Road(out int from, out int segment); string before = JsonUtility.ToJson(net);
            var state = new CareerState { funds = 100000 };
            Assert.That(PublicRoadConstruction.Build(state, net, from, Vector3.zero, -1, segment, PublicRoadStyle.Urban, false,
                out var result, out _, out var problem), Is.True, problem);
            Assert.That(result, Is.SameAs(net)); Assert.That(state.funds, Is.EqualTo(100000));
            state.funds = 0;
            Assert.That(PublicRoadConstruction.Build(state, net, from, Vector3.zero, -1, segment, PublicRoadStyle.Urban, true,
                out result, out _, out problem), Is.False);
            Assert.That(JsonUtility.ToJson(net), Is.EqualTo(before)); Assert.That(state.funds, Is.Zero);
            state.mode = GameMode.Sandbox;
            Assert.That(PublicRoadConstruction.Build(state, net, from, Vector3.zero, -1, segment, PublicRoadStyle.Urban, true,
                out result, out _, out problem), Is.True, problem);
        }

        [TestCase(30f)] [TestCase(130f)]
        public void SnappedStreetJoinsTheEngineeredProfileWithoutMovingTheExistingRoad(float naturalHeight)
        {
            var world = new ValleyWorld { heights = new float[ValleyWorld.Resolution * ValleyWorld.Resolution] };
            for (int i = 0; i < world.heights.Length; i++) world.heights[i] = naturalHeight;
            var state = new CareerState { world = world, funds = 100000 };
            var graph = new PublicRoadNetwork();
            int a = graph.AddNode(new Vector3(-100f, 76f, 0f)), b = graph.AddNode(new Vector3(100f, 84f, 0f));
            int segment = graph.AddSegment(a, b, PublicRoadStyle.Rural, RoadStructure.Ground).id;
            int from = graph.AddNode(new Vector3(0f, 80f, -120f));
            string before = JsonUtility.ToJson(graph);
            var rawTerrainEnd = new Vector3(0f, naturalHeight, 0f);
            Assert.IsTrue(PublicRoadConstruction.Build(state, graph, from, rawTerrainEnd, -1, segment,
                PublicRoadStyle.Urban, false, out var preview, out int quoted, out string why), why);
            Assert.AreSame(graph, preview); Assert.AreEqual(before, JsonUtility.ToJson(graph));
            Assert.IsTrue(PublicRoadConstruction.Build(state, graph, from, rawTerrainEnd, -1, segment,
                PublicRoadStyle.Urban, true, out var built, out int charged, out why), why);
            Assert.AreEqual(quoted, charged); Assert.AreEqual(100000 - charged, state.funds);
            var junction = built.nodes.Single(n => built.At(n.id).Count() == 3);
            Assert.AreEqual(new Vector3(0f, 80f, 0f), junction.position);
            Assert.AreEqual(graph.Node(a).position, built.Node(a).position); Assert.AreEqual(graph.Node(b).position, built.Node(b).position);
            var oldRoad = built.segments.Where(e => e.style == PublicRoadStyle.Rural).ToArray(); Assert.AreEqual(2, oldRoad.Length);
            foreach (var edge in oldRoad)
                foreach (int id in new[] { edge.a, edge.b })
                {
                    var p = built.Node(id).position;
                    Assert.AreEqual(76f + (p.x + 100f) * .04f, p.y, .0001f,
                        "Joining an airport street must retain the existing road's vertical lane profile.");
                }
            Assert.IsTrue(built.Validate(out why), why); Assert.AreEqual(before, JsonUtility.ToJson(graph));
        }

        [TestCase(GameMode.Career)] [TestCase(GameMode.Sandbox)]
        public void InvalidRoadCannotChangeEitherMode(GameMode mode)
        {
            var net = Road(out int from, out int segment); string before = JsonUtility.ToJson(net);
            var state = new CareerState { mode = mode, funds = 100000 };
            Assert.That(PublicRoadConstruction.Build(state, net, from, new Vector3(float.NaN, 0, 0), -1, segment, PublicRoadStyle.Urban, true,
                out _, out _, out _), Is.False);
            Assert.That(JsonUtility.ToJson(net), Is.EqualTo(before)); Assert.That(state.funds, Is.EqualTo(100000));
        }

        [Test]
        public void CompleteAirportSaveRemapsRunwayAndDepotReferencesAndPreservesBuildings()
        {
            var root = new GameObject("Full airport save test"); root.SetActive(false); spawned.Add(root);
            var airport = root.AddComponent<Airport>(); airport.groundSize = 0;
            var runway = airport.AddRunway(new Vector3(-500, 0, 0), new Vector3(500, 0, 0), PavementSurface.Grass, 18);
            var net = airport.Taxiways;
            int a = net.AddNode(Vector3.zero, runway), b = net.AddNode(new Vector3(0, 0, -150));
            net.Connect(a, b, 12, PavementSurface.Asphalt);
            airport.terminals.Add(Terminal.Polygon(new[] { new Vector2(100, -200), new Vector2(100, -250), new Vector2(200, -250), new Vector2(200, -200) }, 3));
            airport.aprons.Add(ApronArea.Rect(new Vector2(0, -300), new Vector2(200, 100), 15, PavementSurface.Concrete));
            var hallObject = new GameObject("Baggage hall"); hallObject.transform.SetParent(root.transform, false);
            var hall = hallObject.AddComponent<BaggageDepot>(); hall.hiredWorkers = 7;
            var teamObject = new GameObject("Saved team"); teamObject.transform.SetParent(hallObject.transform, false);
            var team = teamObject.AddComponent<BaggageStation>(); team.depot = hall; team.cartCount = 3;
            var save = AirportSaveData.Capture(airport, GameAssetCatalog.Load());
            string json = JsonUtility.ToJson(save);
            var detached = JsonUtility.FromJson<AirportSaveData>(json);
            Assert.That(detached.Validate(out var problem), Is.True, problem);
            var restored = detached.Restore(null, GameAssetCatalog.Load()); spawned.Add(restored.gameObject);
            Assert.That(restored.Runways.Count, Is.EqualTo(1));
            Assert.That(restored.Taxiways.nodes.Single(n => n.runway != null).runway, Is.SameAs(restored.Runways[0]));
            Assert.That(restored.terminals.Single().floors, Is.EqualTo(3)); Assert.That(restored.aprons.Count, Is.EqualTo(1));
            var savedTeam = restored.GetComponentInChildren<BaggageStation>();
            Assert.That(savedTeam.depot, Is.SameAs(restored.GetComponentInChildren<BaggageDepot>()));
            Assert.That(savedTeam.depot.hiredWorkers, Is.EqualTo(7)); Assert.That(savedTeam.cartCount, Is.EqualTo(3));
            Assert.That(savedTeam.transform.parent, Is.EqualTo(savedTeam.depot.transform));
            hallObject.SetActive(false);
            var afterDelete = AirportSaveData.Capture(airport, GameAssetCatalog.Load());
            Assert.That(afterDelete.parts.Any(p => p.kind == 4 || p.kind == 5), Is.False, "Pending destroyed halls and their teams must not reappear on reload.");
        }

        [Test]
        public void AuthoredSandboxAirportCanUseTheSamePortableSave()
        {
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity", OpenSceneMode.Additive);
            try
            {
                var airport = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Airport>()).First();
                var data = AirportSaveData.Capture(airport, GameAssetCatalog.Load());
                Assert.That(data.Validate(out string problem), Is.True, problem);
                var restored = data.Restore(null, GameAssetCatalog.Load()); spawned.Add(restored.gameObject);
                Assert.That(restored.Runways.Count, Is.EqualTo(airport.Runways.Count));
                Assert.That(restored.Taxiways.stands.Count, Is.EqualTo(airport.Taxiways.stands.Count));
                Assert.That(restored.terminals.Count, Is.EqualTo(airport.terminals.Count));
                Assert.That(restored.GetComponentsInChildren<BaggageDepot>().Length, Is.EqualTo(airport.GetComponentsInChildren<BaggageDepot>().Length));
            }
            finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(active); }
        }

        [Test]
        public void CorruptComponentReferenceIsRejectedBeforeInstantiation()
        {
            var data = new AirportSaveData();
            data.nodes.Add(new AirportSaveData.Node { parent = -1, scale = Vector3.one, rotation = Quaternion.identity });
            data.parts.Add(new AirportSaveData.Part { node = 0, kind = 0, json = "{\"concrete\":{\"instanceID\":1}}" });
            Assert.That(data.Validate(out _), Is.False);
            Assert.Throws<ArgumentException>(() => data.Restore(null, null));
            data.references.Add("asset:missing:1");
            Assert.That(data.AssetsAvailable(null, out var problem), Is.False);
            StringAssert.Contains("Missing saved airport asset", problem);
        }

        [Test]
        public void PlayerAuthoredBridgeAppearanceIsEmbeddedAndOwnedByTheRestoredAirport()
        {
            var root = new GameObject("Embedded bridge finish"); root.SetActive(false); spawned.Add(root);
            var airport = root.AddComponent<Airport>(); airport.groundSize = 0;
            var appearance = ScriptableObject.CreateInstance<JetBridgeAppearance>(); spawned.Add(appearance);
            appearance.name = "Player finish"; appearance.glazing = Color.cyan;
            var stand = new Stand(); stand.bridges.Add(new JetBridge { appearance = appearance });
            airport.Taxiways.stands.Add(stand);
            var data = AirportSaveData.Capture(airport, GameAssetCatalog.Load());
            Assert.That(data.appearances.Count, Is.EqualTo(1));
            var loaded = JsonUtility.FromJson<AirportSaveData>(JsonUtility.ToJson(data)).Restore(null, GameAssetCatalog.Load()); spawned.Add(loaded.gameObject);
            var finish = loaded.Taxiways.stands.Single().bridges.Single().appearance;
            Assert.That(finish, Is.Not.SameAs(appearance)); Assert.That(finish.glazing, Is.EqualTo(Color.cyan));
            Assert.That(loaded.GetComponent<AirportOwnedAssets>().assets, Does.Contain(finish));
        }

        [Test]
        public void ModeSwitchKeepsAirportRoadGraphAndCameraAndDoesNotGrantCareerUnlocks()
        {
            var scene = SceneManager.GetActiveScene(); var before = new HashSet<GameObject>(scene.GetRootGameObjects());
            try
            {
                var game = NewGameSetup.Build(); var state = CareerState.New(42);
                Assert.That(state.Build(out var problem, 0), Is.True, problem);
                game.Initialize(state, false);
                Assert.That(game.BuildTools.Surface, Is.EqualTo(PavementSurface.Grass));
                Assert.That(game.BuildTools.StandCode, Is.EqualTo(SizeCode.A));
                Assert.That(game.BuildTools.StandExit, Is.EqualTo(StandExit.TaxiOut));
                var airport = game.Airport; var roads = game.Streets.network; var pivot = game.view.pivot; int unlocked = state.layout.unlocked;
                Assert.That(game.ToolProblem(DevTool.Street), Is.Not.Null);
                game.SetMode(GameMode.Sandbox);
                Assert.That(game.ToolProblem(DevTool.Street), Is.Null);
                game.SetMode(GameMode.Career);
                Assert.That(game.Airport, Is.SameAs(airport)); Assert.That(game.Streets.network, Is.SameAs(roads));
                Assert.That(game.view.pivot, Is.EqualTo(pivot)); Assert.That(state.layout.unlocked, Is.EqualTo(unlocked));
                Assert.That(game.ToolProblem(DevTool.Street), Is.Not.Null);
                game.BuildTools.SetTimeScale(20);
                Assert.That(game.Speed, Is.EqualTo(20)); Assert.That(game.Paused, Is.False);
                game.BuildTools.GetComponent<DevToolbar>().GateDesigner.Open(game.Airport, null);
                game.BuildTools.GetComponent<DevToolbar>().GateDesigner.SetPaused(true);
                Assert.That(game.Paused, Is.True);
                game.BuildTools.GetComponent<DevToolbar>().GateDesigner.Close();
                Assert.That(game.Paused, Is.False);
                Assert.That(game.Airport.externallySimulated, Is.True);
                float clock = game.Airport.Traffic.Clock; game.Airport.timeScale = 20f;
                game.Airport.SendMessage("Update");
                Assert.That(game.Airport.Traffic.Clock, Is.EqualTo(clock), "The airport must not step a second time outside the session.");
                var accounts = game.Airport.Deicing.Accounts; int balance = state.funds;
                Assert.That(accounts.CanAfford(balance + 1), Is.False);
                accounts.sharedExpense(.4); accounts.sharedExpense(.8);
                Assert.That(state.funds, Is.EqualTo(balance - 1)); Assert.That(state.expenseRemainder, Is.EqualTo(.2).Within(.000001));
                game.SetMode(GameMode.Sandbox);
                Assert.That(accounts.CanAfford(balance + 1), Is.True);
                accounts.sharedExpense(1000);
                Assert.That(state.funds, Is.EqualTo(balance - 1));
                var captured = CareerSnapshot.Capture(state);
                Assert.That(captured.mode, Is.EqualTo(GameMode.Sandbox)); Assert.That(captured.expenseRemainder, Is.EqualTo(state.expenseRemainder));
            }
            finally { foreach (var go in scene.GetRootGameObjects()) if (!before.Contains(go)) Object.DestroyImmediate(go); }
        }
    }
}
