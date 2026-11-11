using System;
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
    /// <summary>Captures the real HUD and drives clearing, levelling and a runway drag through player mouse input.</summary>
    [InitializeOnLoad]
    public static class PlayerConstructionPlayReview
    {
        const string Running = "AirCity.PlayerConstructionReview.Running", Stage = "AirCity.PlayerConstructionReview.Stage", Started = "AirCity.PlayerConstructionReview.Started";
        const string Root = "Captures/NewGame/PlayerConstruction/";
        static double next;
        static int living;
        static Vector2 target;
        static UnityEngine.InputSystem.Mouse reviewMouse;
        static CareerGame reviewGame;
        static bool held;
        static InputSettings.BackgroundBehavior background;
        static InputSettings.EditorInputBehaviorInPlayMode editorInput;
        static PlayerConstructionPlayReview() { if (SessionState.GetBool(Running, false)) EditorApplication.update += Update; }
        public static void Run()
        {
            NewGameSetup.UpdateWorldAssets();
            background = InputSystem.settings.backgroundBehavior;
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Directory.CreateDirectory(Root);
            foreach (var path in Directory.GetFiles(Root, "*.png")) File.Delete(path);
            var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            view.position = new Rect(20, 20, 1440, 930); view.Show(); view.Focus();
            SessionState.SetBool(Running, true); SessionState.SetInt(Stage, 0); SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
            EditorApplication.update -= Update; EditorApplication.update += Update; EditorApplication.EnterPlaymode();
        }
        static void Update()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0f) > 150f) throw new Exception("Player construction review timed out.");
                int stage = SessionState.GetInt(Stage, 0);
                if (stage == 18 && !EditorApplication.isPlaying)
                { RestoreInput(); SessionState.SetBool(Running, false); EditorApplication.update -= Update; EditorApplication.Exit(0); return; }
                if (!EditorApplication.isPlaying) return;
                var game = Object.FindAnyObjectByType<CareerGame>();
                if (game == null || game.State == null) return;
                var tool = game.GetComponent<CareerBuildTool>();
                if (stage == 0)
                {
                    typeof(CareerGame).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(game, new CareerSaveStore(Path.GetFullPath("Logs/PlayerConstructionPlayReview")));
                    var state = CareerState.New(42); state.Build(out _, 0); game.Initialize(state); game.Paused = true;
                    reviewMouse = InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("Airport construction review mouse");
                    reviewMouse.MakeCurrent();
                    reviewGame = game; held = false;
                    InputSystem.onBeforeUpdate += FeedInput;
                    target = state.world.trees.First(t => !t.removed && Vector2.Distance(t.position, state.Site.centre + new Vector2(-280f, 25f)) < 40f).position;
                    living = state.world.trees.Count(t => !t.removed); Go(1, 2f); return;
                }
                if (EditorApplication.timeSinceStartup < next) return;
                var c = game.State.Site.centre;
                switch (stage)
                {
                    case 1:
                        if (!Capture("01-start")) break;
                        tool.Choose(CareerTool.ClearTrees); tool.Radius = 45f; Go(2, 1f); break;
                    case 2:
                        if (!Capture("02-clear-preview")) break;
                        SendMouse(game, target, true); Go(3, .3f); break;
                    case 3: SendMouse(game, target, false); Go(4, 1f); break;
                    case 4:
                        int removed = living - game.State.world.trees.Count(t => !t.removed);
                        if (removed < 1 || removed > 100 || !game.State.layout.Unlocked(CareerTool.Level)) throw new Exception("Manual brush removed the wrong trees or failed to unlock terrain.");
                        if (!Capture("03-cleared")) break;
                        tool.Choose(CareerTool.Level); tool.Radius = 35f; Go(5, 1f); break;
                    case 5:
                        if (!Capture("04-level-preview")) break;
                        SendMouse(game, target, true); Go(6, .3f); break;
                    case 6: SendMouse(game, target, false); Go(7, 1f); break;
                    case 7:
                        if (!game.State.layout.Unlocked(CareerTool.Runway) || game.State.layout.earthwork < 1f) throw new Exception("The level brush did not unlock runway placement: " + tool.Status);
                        if (!Capture("05-levelled")) break;
                        PlayerConstructionReview.PreparePatch(game.State); game.valley.RefreshLand();
                        tool.Choose(CareerTool.Runway); target = c + new Vector2(-350f, 25f); Go(8, 1f); break;
                    case 8: SendMouse(game, target, true); Go(9, .3f); break;
                    case 9:
                        target = c + new Vector2(350f, 25f); SendMouse(game, target, true); Go(10, 1f); break;
                    case 10:
                        if (!Capture("06-runway-preview")) break;
                        SendMouse(game, target, false); Go(11, 1f); break;
                    case 11:
                        if (!game.State.HasRunway || Mathf.Abs(game.State.RunwayLength - 700f) > 1f) throw new Exception("The runway drag did not build the chosen thresholds: " + game.Message);
                        tool.Choose(CareerTool.Select); Go(12, .5f); break;
                    case 12:
                        if (!Capture("07-runway-built")) break;
                        PlayerConstructionReview.Parking(game.State); game.PlayerChanged(CareerTool.Stand); Go(13, 1f); break;
                    case 13:
                        if (!Capture("08-tools-unlocked")) break;
                        PlayerConstructionReview.Road(game.State);
                        if (!CareerConstruction.Reception(game.State, c + new Vector2(215f, -135f), 0f, true, out string why)) throw new Exception(why);
                        var completed = game.State; game.Initialize(completed); game.Paused = true;
                        if (!game.Save() || !game.Continue()) throw new Exception(game.Message);
                        game.Paused = true; Go(14, 1f); break;
                    case 14:
                        if (!Capture("09-player-airport")) break;
                        if (!game.AcceptCharter()) throw new Exception(game.Message);
                        game.Advance(1800f);
                        if (game.State.completedCharters != 1) throw new Exception("The manually built airport charter did not finish: " + game.Message);
                        Go(15, 1f); break;
                    case 15:
                        if (!Capture("10-operating")) break;
                        game.view.enabled = false;
                        game.view.transform.position = new Vector3(c.x + 220f, game.State.layout.elevation + 90f, c.y - 350f);
                        game.view.transform.LookAt(new Vector3(c.x + 490f, game.State.layout.elevation, c.y - 80f));
                        Go(17, 1f); break;
                    case 17:
                        if (!Capture("11-ground-detail")) break;
                        File.WriteAllText("Logs/player-construction-play-review.txt", "Passed: actual Game-view UI, manual tree brush, level brush, earned tool unlocks, runway drag, chosen layout, public road, reception, save/load and real charter.\n");
                        Go(18, 0f); EditorApplication.ExitPlaymode(); break;
                }
            }
            catch (Exception e) { RestoreInput(); SessionState.SetBool(Running, false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static Vector2 Screen(CareerGame game, Vector2 p)
        {
            var s = game.view.GetComponent<Camera>().WorldToScreenPoint(new Vector3(p.x, game.valley.DisplayHeight(p), p.y));
            return new Vector2(s.x, s.y);
        }
        static void SendMouse(CareerGame game, Vector2 p, bool down)
        { target = p; held = down; }
        static void FeedInput()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || reviewMouse == null || reviewGame == null) return;
            reviewMouse.MakeCurrent();
            InputSystem.QueueStateEvent(reviewMouse, new MouseState { position = Screen(reviewGame, target), buttons = (ushort)(held ? 1 : 0) });
        }
        static bool Capture(string name)
        {
            string path = Root + name + ".png"; if (File.Exists(path)) return true;
            ScreenCapture.CaptureScreenshot(path); next = EditorApplication.timeSinceStartup + 1f; return false;
        }
        static void Go(int stage, float wait) { SessionState.SetInt(Stage, stage); next = EditorApplication.timeSinceStartup + wait; }
        static void RestoreInput()
        {
            InputSystem.onBeforeUpdate -= FeedInput;
            InputSystem.settings.backgroundBehavior = background;
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        }
    }
}
