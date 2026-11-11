using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Exercises the actual road tool with queued mouse clicks in Play mode; uses a review-only save slot.</summary>
    [InitializeOnLoad]
    public static class CareerRoadPlayReview
    {
        const string Running = "AirCity.RoadReview.Running", Stage = "AirCity.RoadReview.Stage", Started = "AirCity.RoadReview.Started";
        const string Root = "Captures/WorldAssets/UI/";
        static double next;
        static CareerRoadPlayReview() { if (SessionState.GetBool(Running, false)) EditorApplication.update += Update; }

        public static void Run()
        {
            NewGameSetup.UpdateWorldAssets();
            Directory.CreateDirectory(Root);
            foreach (var name in new[] { "drawing-road", "connected-road", "signal-junction" }) File.Delete(Root + name + ".png");
            var window = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            window.position = new Rect(20f, 20f, 1440f, 930f); window.Show(); window.Focus();
            SessionState.SetBool(Running, true); SessionState.SetInt(Stage, 0);
            SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
            EditorApplication.update -= Update; EditorApplication.update += Update; EditorApplication.EnterPlaymode();
        }

        static void Update()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0f) > 120f) throw new Exception("Road tool Play review timed out.");
                int stage = SessionState.GetInt(Stage, 0);
                if (stage == 9 && !EditorApplication.isPlaying)
                { SessionState.SetBool(Running, false); EditorApplication.update -= Update; EditorApplication.Exit(0); return; }
                if (!EditorApplication.isPlaying) return;
                var game = Object.FindAnyObjectByType<CareerGame>();
                if (game == null || game.State == null) return;
                var tool = game.GetComponent<CareerRoadTool>();
                if (stage == 0)
                {
                    typeof(CareerGame).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(game, new CareerSaveStore(Path.GetFullPath("Logs/CareerRoadPlayReview")));
                    var state = CareerState.New(42, false); state.Build(out _, 0);
                    while (state.Step != CareerStep.AccessRoad) if (!state.Build(out string why)) throw new Exception(why);
                    game.Initialize(state); game.Paused = true; tool.Begin();
                    Go(1, 2f); return;
                }
                var entrance = game.State.Site.Entrance;
                var point = stage <= 2 ? entrance + new Vector2(180f, -180f) : new Vector2(ValleyWorld.RoadX, entrance.y - 180f);
                var screen = game.view.GetComponent<Camera>().WorldToScreenPoint(new Vector3(point.x, game.valley.DisplayHeight(point), point.y));
                if (stage <= 5 && Mouse.current != null) InputState.Change(Mouse.current.position, new Vector2(screen.x, screen.y));
                if (EditorApplication.timeSinceStartup < next) return;
                switch (stage)
                {
                    case 1: Click(screen, true); Go(2, .3f); break;
                    case 2: Click(screen, false); Go(3, 1f); break;
                    case 3:
                        if (!tool.Active || tool.Price <= game.State.Quote().cost) throw new Exception("The clicked road corner did not update the custom route preview.");
                        if (Capture("drawing-road")) { Click(screen, true); Go(4, .3f); }
                        break;
                    case 4: Click(screen, false); Go(5, 1f); break;
                    case 5:
                        if (tool.Active || !game.State.HasAccess || game.State.accessRoadRoute.Count != 3) throw new Exception("The snapped road click did not build a three-point access route: " + tool.Status);
                        game.State.roadTrafficSeconds = 30f;
                        if (!game.Save() || !game.Continue()) throw new Exception(game.Message);
                        game.Paused = true;
                        if (game.State.accessRoadRoute.Count != 3 || game.State.roadTrafficSeconds != 30f) throw new Exception("Custom road or signal clock changed after reload.");
                        game.view.FrameRoad(entrance, point, game.State.Site.elevation); Go(6, 1f); break;
                    case 6:
                        if (!Capture("connected-road")) break;
                        var junction = game.valley.RoadNetwork.nodes.Single(n => n.control == JunctionControl.TrafficLights);
                        game.view.enabled = false;
                        game.view.transform.position = junction.position + new Vector3(-52f, 33f, -47f);
                        game.view.transform.LookAt(junction.position); Go(7, 1f); break;
                    case 7:
                        if (!Capture("signal-junction")) break;
                        File.WriteAllText("Logs/road-play-review.txt", "Passed: queued player clicks, custom route preview/purchase, terrain rebuild, terminal connection, save/load, signal clock and actual Game-view HUD.\n");
                        Go(9, 0f); EditorApplication.ExitPlaymode(); break;
                }
            }
            catch (Exception e) { SessionState.SetBool(Running, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static bool Capture(string name)
        {
            string path = Root + name + ".png";
            if (File.Exists(path)) return true;
            ScreenCapture.CaptureScreenshot(path); next = EditorApplication.timeSinceStartup + 1f; return false;
        }
        static void Click(Vector3 screen, bool down)
        {
            if (Mouse.current == null) throw new Exception("No mouse device for road tool review.");
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = new Vector2(screen.x, screen.y), buttons = (ushort)(down ? 1 : 0) });
        }
        static void Go(int stage, float wait) { SessionState.SetInt(Stage, stage); next = EditorApplication.timeSinceStartup + wait; }
    }
}
