using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Actual Game-view HUD captures at successive earned tool states and two window sizes.</summary>
    [InitializeOnLoad]
    public static class CareerHudPlayReview
    {
        const string Key = "AirCity.CareerHudPlayReview.";
        const string Root = "Captures/NewGame/CareerHud/";
        const string WindowTitle = "Career HUD review";
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static double next;
        static CareerGame game;
        static Mouse reviewMouse;
        static Vector2 target;
        static readonly List<Shot> shots = new List<Shot>();
        [Serializable] sealed class Shot { public string name, activeTool; public int width, height, unlockedTools; }
        [Serializable] sealed class Result { public string result, utc, unityVersion; public List<Shot> screenshots; }

        static CareerHudPlayReview()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update;
        }

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Root); Directory.CreateDirectory("Logs");
                SessionState.SetInt(Key + "Background", (int)InputSystem.settings.backgroundBehavior);
                SessionState.SetInt(Key + "EditorInput", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
                SessionState.SetInt(Key + "OriginalMouse", Mouse.current != null ? Mouse.current.deviceId : -1);
                NewGameSetup.UpdateWorldAssets();
                foreach (string path in Directory.GetFiles(Root, "*.png")) File.Delete(path);
                var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
                var window = (EditorWindow)ScriptableObject.CreateInstance(type);
                window.titleContent = new GUIContent(WindowTitle);
                window.Show(); Resize(1440f, 900f);
                SessionState.SetBool(Key + "Running", true); SessionState.SetInt(Key + "Stage", 0);
                SessionState.SetFloat(Key + "Started", (float)EditorApplication.timeSinceStartup);
                SessionState.SetString(Key + "Failure", "");
                EditorApplication.update -= Update; EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { Debug.LogException(e); Finish(false); }
        }

        static void Update()
        {
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage >= 7)
            {
                if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) Finish(stage == 7);
                return;
            }
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0f) > 150f)
                    throw new InvalidOperationException("The career HUD review timed out.");
                if (!EditorApplication.isPlaying) return;
                game = Object.FindAnyObjectByType<CareerGame>();
                if (game == null || game.State == null) return;
                var tool = game.GetComponent<CareerBuildTool>();
                if (tool == null || game.GetComponent<CareerHud>() == null || !game.GetComponent<CareerHud>().enabled)
                    throw new InvalidOperationException("The live career tools and HUD must remain enabled for review.");
                if (stage == 0)
                {
                    var field = typeof(CareerGame).GetField("store", PrivateInstance);
                    if (field == null) throw new MissingFieldException(nameof(CareerGame), "store");
                    field.SetValue(game, new CareerSaveStore(Path.GetFullPath("Logs/CareerHudPlayReview")));
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    reviewMouse = InputSystem.AddDevice<Mouse>("Career HUD review mouse");
                    SessionState.SetInt(Key + "ReviewMouse", reviewMouse.deviceId);
                    InputSystem.onBeforeUpdate += FeedInput;
                    shots.Clear();
                    Initialize(CareerState.New(42)); target = game.State.world.sites[0].centre;
                    Resize(1440f, 900f); Go(1); return;
                }
                if (EditorApplication.timeSinceStartup < next) return;
                switch (stage)
                {
                    case 1:
                        if (!Capture("01-site-selection", -1)) break;
                        var bought = CareerState.New(42);
                        if (!bought.Build(out string why, 0)) throw new InvalidOperationException(why);
                        Initialize(bought); target = bought.Site.centre; Go(2); break;
                    case 2:
                        if (!Capture("02-land-owned", 2)) break;
                        var state = game.State;
                        target = state.world.trees.Where(t => !t.removed && state.Site.Parcel.Contains(t.position))
                            .OrderBy(t => (t.position - state.Site.centre).sqrMagnitude).First().position;
                        if (!CareerConstruction.Clear(state, target, 45f, out string clearProblem)) throw new InvalidOperationException(clearProblem);
                        Initialize(state); tool.Choose(CareerTool.ClearTrees); tool.Radius = 45f;
                        // Show a live brush over remaining woodland, beside the patch we just cleared manually.
                        target = state.world.trees.Where(t => !t.removed && state.Site.Parcel.Contains(t.position))
                            .OrderBy(t => (t.position - state.Site.centre).sqrMagnitude).First().position;
                        Go(3); break;
                    case 3:
                        if (!Capture("03-clearing", 3)) break;
                        tool.Choose(CareerTool.Level); tool.Radius = 35f;
                        target = game.State.world.trees.Where(t => t.removed && game.State.Site.Parcel.Contains(t.position))
                            .OrderBy(t => (t.position - game.State.Site.centre).sqrMagnitude).First().position;
                        Go(4); break;
                    case 4:
                        if (!Capture("04-level-ground", 3)) break;
                        Initialize(PlayerConstructionReview.Open(42)); tool.Choose(CareerTool.Select);
                        target = game.State.Site.centre; Go(5); break;
                    case 5:
                        if (!Capture("05-all-tools", 8)) break;
                        Resize(800f, 650f); tool.Choose(CareerTool.ClearTrees); tool.Radius = 32f;
                        target = game.State.world.trees.Where(t => !t.removed && game.State.Site.Parcel.Contains(t.position))
                            .OrderBy(t => (t.position - game.State.Site.centre).sqrMagnitude).First().position;
                        game.view.pivot = new Vector3(target.x, game.State.world.Height(target), target.y); game.view.Apply();
                        Go(6, 1.5f); break;
                    case 6:
                        if (!Capture("06-narrow-clearing", 8)) break;
                        if (shots[shots.Count - 1].width >= shots[shots.Count - 2].width)
                            throw new InvalidOperationException("The narrow Game view did not resize its actual render target.");
                        var result = new Result { result = "Passed: live career HUD, earned tools, clearing/levelling controls and narrow viewport.",
                            utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, screenshots = shots };
                        File.WriteAllText("Logs/career-hud-play-review.json", JsonUtility.ToJson(result, true));
                        File.WriteAllText("Logs/career-hud-play-review.txt", result.result + "\n");
                        RestoreInput(); SessionState.SetInt(Key + "Stage", 7); EditorApplication.ExitPlaymode(); break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e); SessionState.SetString(Key + "Failure", e.ToString());
                RestoreInput(); SessionState.SetInt(Key + "Stage", 8);
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else Finish(false);
            }
        }

        static void Initialize(CareerState state) { game.Initialize(state, false); game.Paused = true; }
        static void Go(int stage, float settle = 1f) { SessionState.SetInt(Key + "Stage", stage); next = EditorApplication.timeSinceStartup + settle; }
        static EditorWindow Window()
        {
            var window = FindWindow();
            if (window == null) throw new InvalidOperationException("The floating career review Game view was closed.");
            return window;
        }
        static EditorWindow FindWindow()
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
            return Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(w => w.GetType() == type && w.titleContent.text == WindowTitle);
        }
        static void Resize(float width, float height)
        {
            var window = Window(); window.maximized = false;
            var freeAspect = window.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (freeAspect == null || !freeAspect.CanWrite) throw new MissingMemberException("GameView.selectedSizeIndex");
            freeAspect.SetValue(window, 0); // Free Aspect follows the real window instead of a fixed 1920x1080 render size.
            window.position = new Rect(30f, 30f, width, height); window.Show();
            window.titleContent = new GUIContent(WindowTitle); window.Focus(); window.Repaint();
        }
        static void FeedInput()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || reviewMouse == null || game == null || game.valley.Terrain == null) return;
            var point = game.view.GetComponent<Camera>().WorldToScreenPoint(new Vector3(target.x, game.valley.DisplayHeight(target), target.y));
            reviewMouse.MakeCurrent(); InputSystem.QueueStateEvent(reviewMouse, new MouseState { position = new Vector2(point.x, point.y) });
        }
        static bool Capture(string name, int expectedTools)
        {
            string path = Root + name + ".png";
            if (!File.Exists(path)) { ScreenCapture.CaptureScreenshot(path); next = EditorApplication.timeSinceStartup + 1f; return false; }
            int count = game.State.Site == null ? 0 : Enum.GetValues(typeof(CareerTool)).Cast<CareerTool>().Count(game.State.layout.Unlocked);
            if (expectedTools >= 0 && count != expectedTools)
                throw new InvalidOperationException(name + " expected " + expectedTools + " earned tools, found " + count + ".");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24) throw new InvalidOperationException("The Game-view screenshot is incomplete: " + path);
            int width = BigEndian(bytes, 16), height = BigEndian(bytes, 20);
            if (width < 300 || height < 300) throw new InvalidOperationException("The Game-view screenshot has an invalid size: " + path);
            shots.Add(new Shot { name = name, width = width, height = height, unlockedTools = count,
                activeTool = game.GetComponent<CareerBuildTool>().Tool.ToString() });
            return true;
        }
        static int BigEndian(byte[] bytes, int at) => bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3];
        static void RestoreInput()
        {
            InputSystem.onBeforeUpdate -= FeedInput;
            var mouse = reviewMouse ?? InputSystem.GetDeviceById(SessionState.GetInt(Key + "ReviewMouse", -1)) as Mouse;
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            reviewMouse = null;
            var original = InputSystem.GetDeviceById(SessionState.GetInt(Key + "OriginalMouse", -1)) as Mouse;
            if (original != null) original.MakeCurrent();
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt(Key + "Background", 0);
            InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Key + "EditorInput", 0);
        }
        static void Finish(bool success)
        {
            RestoreInput(); SessionState.SetBool(Key + "Running", false); EditorApplication.update -= Update;
            var window = FindWindow();
            if (window != null) window.Close();
            if (!success) File.WriteAllText("Logs/career-hud-play-review.txt", "Failed: " + SessionState.GetString(Key + "Failure", "Review startup failed.") + "\n");
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
