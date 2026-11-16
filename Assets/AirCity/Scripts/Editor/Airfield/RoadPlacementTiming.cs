using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using AirCity.Airfield;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>
    /// How long placing a road takes, step by step, in the sandbox (an airside road: Connect + Rebuild, and the hover
    /// checks the tool runs every frame) and in the career (a public road: the valley's road geometry, terrain grading,
    /// ground paint, forest and ground cover). Writes Logs/road-placement-timing.txt.
    ///   Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Airfield.RoadPlacementTiming.FromCommandLine
    /// </summary>
    public static class RoadPlacementTiming
    {
        static readonly StringBuilder report = new StringBuilder();

        static double Time(string what, Action action, int repeat = 1)
        {
            action();   // warm (JIT, caches)
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < repeat; i++) action();
            double ms = watch.Elapsed.TotalMilliseconds / repeat;
            report.AppendLine($"{what,-58} {ms,9:0.00} ms");
            return ms;
        }

        static void Call(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, args.Select(a => a.GetType()).ToArray(), null);
            if (m == null) throw new MissingMethodException(target.GetType().Name, method);
            m.Invoke(target, args);
        }

        public static void FromCommandLine()
        {
            try
            {
                Sandbox();
                Career();
                HandsOn();
                System.IO.File.WriteAllText("Logs/road-placement-timing.txt", report.ToString());
                Debug.Log(report.ToString());
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                System.IO.File.WriteAllText("Logs/road-placement-timing.txt", report + "\n" + e);
                EditorApplication.Exit(1);
            }
        }

        static void Sandbox()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var airport = Object.FindAnyObjectByType<Airport>();
            var net = airport.Taxiways;
            report.AppendLine($"== Sandbox: {net.nodes.Count} nodes, {net.edges.Count} edges, {net.stands.Count} stands");
            Time("TaxiwayNetwork.Rebuild (whole airfield)", net.Rebuild, 5);
            for (int i = 0; i < 5; i++) net.Rebuild();
            // a new road between two free points north of everything, then one more section off it
            float minX = net.nodes.Min(n => n.position.x), maxZ = net.nodes.Max(n => n.position.z);
            var a0 = new Vector2(minX, maxZ + 200f);
            var a1 = new Vector2(minX + 120f, maxZ + 200f);
            Time("JoinProblem (hover check, every frame)", () => net.JoinProblem(a0, -1, null, a1, -1, null, RunwayRules.RoadWidth, RoadClass.Service), 50);
            var sw = Stopwatch.StartNew();
            int n0 = net.AddNode(Geo3(a0)), n1 = net.AddNode(Geo3(a1));
            net.Connect(n0, n1, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            report.AppendLine($"{"Connect (one new road)",-58} {sw.Elapsed.TotalMilliseconds,9:0.00} ms");
            var once = Stopwatch.StartNew(); net.Rebuild();
            report.AppendLine($"{"Rebuild after placing a road away from the gates",-58} {once.Elapsed.TotalMilliseconds,9:0.00} ms");
            Time("StandEquipmentLayout.All after a far road", () => { Call(airport, "LayoutChanged"); AirCity.Vehicles.StandEquipmentLayout.All(airport); }, 3);
            Time("  depots BuildingBox", () => { foreach (var d in airport.GetComponentsInChildren<AirCity.Vehicles.BaggageDepot>()) _ = d.BuildingBox; }, 3);
            Time("  terminals Footprints()", () => { foreach (var t in airport.terminals) _ = t.Footprints(); }, 3);
            Time("  Bridges.BaggageExclusions() after a layout change", () => { Call(airport, "LayoutChanged"); airport.Bridges.BaggageExclusions(); }, 3);
            Time("  Bridges.Layouts after a layout change", () => { Call(airport, "LayoutChanged"); _ = airport.Bridges.Layouts; }, 3);
            int bridges = 0; foreach (var (_, list) in airport.Bridges.Layouts) bridges += list.Count;
            report.AppendLine($"  bridges: {bridges}");
            report.AppendLine($"  terminals: {airport.terminals.Count}");
            Time("airport.Pavement (lazy, after a layout change)", () => { Call(airport, "LayoutChanged"); _ = airport.Pavement; }, 3);
            Time("net.Layouts()", () => net.Layouts(), 5);
        }

        static Vector3 Geo3(Vector2 p) => new Vector3(p.x, 0f, p.y);

        static void HandsOn()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/NewGame.unity");
            var game = Object.FindAnyObjectByType<CareerGame>();
            var state = CareerState.New(42);
            if (!state.Build(out string why, 0)) throw new InvalidOperationException(why);
            AirCity.EditorTools.Game.PlayerConstructionReview.PreparePatch(state);
            AirCity.EditorTools.Game.PlayerConstructionReview.Runway(state);
            AirCity.EditorTools.Game.PlayerConstructionReview.Parking(state);
            var sw = Stopwatch.StartNew();
            game.Initialize(state, false);
            report.AppendLine($"\n== Hands-on career (seed 42): runway + taxiway + stand, no road yet");
            report.AppendLine($"{"CareerGame.Initialize",-58} {sw.Elapsed.TotalMilliseconds,9:0.00} ms");
            var start = state.Site.centre + new Vector2(220f, -170f);
            var end = new Vector2(ValleyWorld.RoadX, start.y);
            for (int i = 0; i < state.world.trees.Count; i++)
            {
                var tree = state.world.trees[i];
                if (!tree.removed && CareerConstruction.Distance(tree.position, start, end) < 20f)
                    CareerConstruction.Clear(state, tree.position, 4f, out _, i);
            }
            var route = new[] { start, end };
            Time("road tool hover: CareerConstruction.Road (every frame)", () => CareerConstruction.Road(state, route, out _), 5);
            Time("road tool hover: PublicRoadLayout.TrySnap", () => PublicRoadLayout.TrySnap(state.world, end, 22f, out _), 20);
            Time("  CareerConstruction.BuildRoad (validate, graph, charge) on a copy", () =>
            {
                var copy = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(state));
                var w = Stopwatch.StartNew();
                if (!CareerConstruction.BuildRoad(copy, route, out string problem)) throw new InvalidOperationException(problem);
                report.AppendLine($"{"    (BuildRoad alone)",-58} {w.Elapsed.TotalMilliseconds,9:0.00} ms");
            }, 1);
            var withoutRoad = state.RoadNetwork().Clone();
            sw.Restart();
            if (!game.Construct(route)) throw new InvalidOperationException(game.Message);
            report.AppendLine($"{"game.Construct(road) = placing the access road",-58} {sw.Elapsed.TotalMilliseconds,9:0.00} ms");
            Time("  game.Save(false)", () => game.Save(false), 2);
            var roads = state.RoadNetwork();
            Time("  valley.RefreshRoads, nothing changed", () => game.valley.RefreshRoads(roads), 2);
            Time("  valley.RefreshRoads, the road removed then added (per change)", () => { game.valley.RefreshRoads(withoutRoad); game.valley.RefreshRoads(roads); }, 2);
            Time("  QueueAutosave (snapshot on the main thread)", () => game.QueueAutosave(), 3);
            Time("  PublicRoadGeometry alone", () => new PublicRoadGeometry(roads, game.valley.ruralRoad, game.valley.airportRoad, state.world.Height), 2);
            Time("  RebuildForest alone", () => Call(game.valley, "RebuildForest"), 2);
            Time("  RefreshGroundCover (exclusions unchanged)", () => Call(game, "RefreshGroundCover"), 2);
            Time("PlayerChanged(Taxiway) = placing a taxiway", () => game.PlayerChanged(CareerTool.Taxiway), 2);
            Time("  BuildAirport", () => Call(game, "BuildAirport"), 2);
        }

        static void Career()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/NewGame.unity");
            var game = Object.FindAnyObjectByType<CareerGame>();
            var state = CareerState.New(19770616, false);
            state.Build(out _, 0);
            while (state.Step != CareerStep.AccessRoad) state.Build(out _);
            var start = state.Site.Entrance;
            if (!state.Build(out string why, accessRoute: new[] { start, start + new Vector2(180f, -180f), new Vector2(ValleyWorld.RoadX, start.y - 180f) }))
                throw new InvalidOperationException(why);
            var sw = Stopwatch.StartNew();
            game.Initialize(state, false);
            report.AppendLine($"\n== Career (seed 19770616, original-save path)");
            report.AppendLine($"{"CareerGame.Initialize (whole valley + airport)",-58} {sw.Elapsed.TotalMilliseconds,9:0.00} ms");
            var valley = game.valley;
            var roads = state.RoadNetwork();
            report.AppendLine($"public road graph: {roads.nodes.Count} nodes, {roads.segments.Count} segments");
            Time("valley.BuildAccess (what placing a public road runs)", () => valley.BuildAccess(state.Site, roads), 2);
            Time("  new PublicRoadGeometry", () => new PublicRoadGeometry(roads, valley.ruralRoad, valley.airportRoad, state.world.Height), 3);
            Time("  BuildRoadGeometry (geometry + scene objects + signals)", () => Call(valley, "BuildRoadGeometry"), 3);
            Time("  SetHeights (grade whole 513x513 terrain)", () => Call(valley, "SetHeights"), 2);
            Time("  PaintRoads (whole 1024x1024 ground paint)", () => Call(valley, "PaintRoads"), 2);
            Time("  RebuildForest", () => Call(valley, "RebuildForest"), 2);
            Time("  RebuildGroundCover", () => Call(valley, "RebuildGroundCover"), 2);
            Time("CareerGame.BuildAirport (taxiway/runway/stand placement)", () => Call(game, "BuildAirport"), 2);
        }
    }
}
