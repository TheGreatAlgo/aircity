using System;
using System.IO;
using System.Reflection;
using AirCity.Dev;
using AirCity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Actual mouse placement, mode switching and save/reload in the one player scene.</summary>
    [InitializeOnLoad]
    public static class SharedGamePlayReview
    {
        const string Key = "AirCity.SharedGamePlayReview";
        const string Folder = "Captures/SharedGame/";
        static CareerGame game;
        static EditorWindow window;
        static Mouse mouse;
        static Vector2 target;
        static bool pressed;
        static double next;
        static int stage, segments, funds;
        static object airport, roads;
        static Vector3 pivot;
        static SharedGamePlayReview() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Update; }

        public static void Run()
        {
            SharedGameAssets.Ensure();
            Directory.CreateDirectory(Folder); Directory.CreateDirectory("Logs");
            foreach (var path in Directory.GetFiles(Folder, "*.png")) File.Delete(path);
            EditorSceneManager.OpenScene(NewGameSetup.ScenePath);
            SessionState.SetBool(Key, true); SessionState.SetInt(Key + "Stage", 0);
            SessionState.SetFloat(Key + "Start", (float)EditorApplication.timeSinceStartup);
            EditorApplication.update -= Update; EditorApplication.update += Update;
            EditorApplication.EnterPlaymode();
        }

        static void Go(int value) { stage = value; SessionState.SetInt(Key + "Stage", value); next = EditorApplication.timeSinceStartup + 1.2f; }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Update()
        {
            stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage >= 20) { if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(stage == 20); return; }
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Start", 0) > 180) throw new TimeoutException("Shared game review timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
                if (stage == 0)
                {
                    game = Object.FindAnyObjectByType<CareerGame>(); if (game == null || game.State == null) return;
                    var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
                    window = (EditorWindow)ScriptableObject.CreateInstance(type);
                    window.Show(); window.position = new Rect(30, 30, 1440, 900);
                    type.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(window, 0);
                    window.Focus();
                    typeof(CareerGame).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, new CareerSaveStore(Path.GetFullPath("Logs/SharedGamePlayReview")));
                    game.Initialize(PlayerConstructionReview.Open(42), true); game.Paused = true;
                    Require(game.SharedTools, "Shared tools were not attached.");
                    airport = game.Airport; roads = game.Streets.network; pivot = game.view.pivot;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    mouse = InputSystem.AddDevice<Mouse>("Shared game review mouse");
                    InputSystem.onBeforeUpdate += Feed;
                    target = game.State.Site.centre + new Vector2(-150, -220);
                    Go(1); return;
                }
                if (stage == 1)
                {
                    if (!Capture("career")) return;
                    game.SetMode(GameMode.Sandbox);
                    Require(ReferenceEquals(airport, game.Airport) && ReferenceEquals(roads, game.Streets.network) && pivot == game.view.pivot, "Mode change replaced the world or camera.");
                    game.BuildTools.SetTool(DevTool.Street); segments = game.Streets.network.segments.Count; funds = game.State.funds;
                    Go(2); return;
                }
                if (stage == 2) { pressed = true; Go(3); return; }
                if (stage == 3) { pressed = false; target = game.State.Site.centre + new Vector2(100, -220); Go(4); return; }
                if (stage == 4) { pressed = true; Go(5); return; }
                if (stage == 5)
                {
                    pressed = false;
                    Require(game.Streets.network.segments.Count == segments + 1, "Mouse-driven shared Street tool did not build: " + game.BuildTools.StreetProblem);
                    Require(game.State.funds == funds, "Sandbox road charged career funds."); Go(6); return;
                }
                if (stage == 6)
                {
                    if (!Capture("sandbox-street")) return;
                    game.BuildTools.SetTool(DevTool.Runway);
                    target = game.State.Site.centre + new Vector2(-350, 25); Go(9); return;
                }
                if (stage == 9) { pressed = true; Go(10); return; }
                if (stage == 10) { pressed = false; Go(11); return; }
                if (stage == 11) { target = game.State.Site.centre + new Vector2(-400, 25); Go(12); return; }
                if (stage == 12) { pressed = true; Go(14); return; }
                if (stage == 14) { pressed = false; Go(15); return; }
                if (stage == 15)
                {
                    pressed = false;
                    Require(game.BuildTools.PlacingRunway == null && game.Airport.Runways.Count == 1 && Mathf.Abs(game.Airport.Runways[0].Length - 750f) < .1f, "Shared runway extension failed: " + game.BuildTools.RunwayProblem + "; draft=" + (game.BuildTools.PlacingRunway != null) + "; runways=" + game.Airport.Runways.Count + "; length=" + game.Airport.Runways[0].Length + "; " + game.Message);
                    Require(game.State.funds == funds, "Sandbox extension charged career funds.");
                    Go(13); return;
                }
                if (stage == 13)
                {
                    game.BuildTools.SetTool(DevTool.Select);
                    Require(game.Save(), game.Message);
                    Require(game.Continue(), game.Message); game.Paused = true;
                    Require(game.State.mode == GameMode.Sandbox && game.Streets.network.segments.Count == segments + 1, "Mode or roads lost on reload.");
                    Require(game.Airport.Runways.Count == 1 && game.Airport.Taxiways.stands.Count == 1, "Airport layout lost on reload.");
                    Require(Mathf.Abs(game.Airport.Runways[0].Length - 750f) < .1f, "Extended runway lost on reload.");
                    foreach (var node in game.Airport.Taxiways.nodes)
                        if (node.runway != null) Require(node.runway == game.Airport.Runways[0], "Taxiway still refers to the replaced runway.");
                    game.SetMode(GameMode.Career); Go(7); return;
                }
                if (stage == 7)
                {
                    if (!Capture("career-after-reload")) return;
                    window.position = new Rect(30, 30, 900, 700); game.SetMode(GameMode.Sandbox); Go(8); return;
                }
                if (stage == 8)
                {
                    if (!Capture("sandbox-narrow")) return;
                    File.WriteAllText("Logs/shared-game-play-review.txt", "Passed: one scene, same airport/camera on mode change, real mouse Street placement and runway extension, free sandbox construction, full save/reload, taxiway references, career return and narrow HUD.\n");
                    Go(20); EditorApplication.ExitPlaymode();
                }
            }
            catch (Exception e)
            {
                File.WriteAllText("Logs/shared-game-play-review.txt", e.ToString()); Debug.LogException(e);
                Go(21); EditorApplication.ExitPlaymode();
            }
        }
        static void Feed()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || mouse == null || game == null) return;
            var p = game.view.GetComponent<Camera>().WorldToScreenPoint(new Vector3(target.x, game.valley.DisplayHeight(target), target.y));
            mouse.MakeCurrent(); InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(p.x, p.y) }.WithButton(MouseButton.Left, pressed));
        }
        static bool Capture(string name)
        {
            string path = Folder + name + ".png";
            if (!File.Exists(path)) { ScreenCapture.CaptureScreenshot(path); next = EditorApplication.timeSinceStartup + 1f; return false; }
            Require(new FileInfo(path).Length > 1000, "Empty Game-view capture."); return true;
        }
        static void Finish(bool success)
        {
            InputSystem.onBeforeUpdate -= Feed;
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (window != null) window.Close();
            SessionState.SetBool(Key, false); EditorApplication.update -= Update;
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
