using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Dev;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Actual GameView/OnGUI brush controls, map prices, clearing and one-pass levelling. Use an isolated non-batch editor.
    /// Captures the GameView's own IMGUI-inclusive render target, without waiting on ScreenCapture's end-of-frame queue.</summary>
    [InitializeOnLoad]
    public static class BrushControlsReview
    {
        const string Key = "AirCity.BrushControlsReview.";
        const string Folder = "Captures/NewGame/BrushControls/";
        const string SaveFolder = "Logs/BrushControlsReview";
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        const int SavedFunds = 2500000;
        static CareerGame game;
        static CareerHud hud;
        static EditorWindow window;
        static Mouse mouse;
        static Keyboard keyboard;
        static Vector2 pointer;
        static bool pressed, releasePending;
        static double next, releaseAt, actionDeadline;
        static int width, height, layoutVersion;
        static Vector2 patch, clearedPatch;
        static CareerBuildTool builder;
        static int beforeFunds, quotedClear, quotedLevel, removedTreesBefore, clearTrees, clearProps;
        static int[] selectedProps;
        static float radiusBefore, radiusAfter, holdBefore, holdDelta, typedHeight;
        static float[] levelledHeights;
        static bool repeatFree;
        static Result report;
        static readonly List<Shot> shots = new List<Shot>();
        [Serializable] sealed class Shot { public string name, tool; public int width, height, price; public float radius, targetHeight; }
        [Serializable] sealed class Result
        {
            public string result, unityVersion, utc;
            public int clearedTrees, clearedProps, clearingPrice, levellingPrice;
            public float radiusBefore, radiusAfter, heldHeightDelta;
            public bool secondLevelClickUnchanged, savedClearingVerified;
            public List<Shot> screenshots;
        }
        [Serializable] sealed class SavedGameView { public string id; public int size; }
        [Serializable] sealed class GameViewSetup { public SavedGameView[] views; }
        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class Setup { public SavedScene[] scenes; }

        static BrushControlsReview()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update;
        }

        [MenuItem("AirCity/Game/Review Brush controls")]
        public static void Run() => Begin(false);
        public static void RunFromCommandLine() => Begin(true);

        static void Begin(bool exitEditor)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before running the brush controls review.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save modified scenes before running the brush controls review.");
                Directory.CreateDirectory(Folder); Directory.CreateDirectory(SaveFolder);
                foreach (string file in Directory.GetFiles(Folder, "*.png")) File.Delete(file);
                SessionState.SetString(Key + "Scenes", JsonUtility.ToJson(new Setup { scenes = EditorSceneManager.GetSceneManagerSetup()
                    .Select(s => new SavedScene { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray() }));
                SessionState.SetBool(Key + "Exit", exitEditor);
                SessionState.SetBool(Key + "Background", Application.runInBackground);
                SessionState.SetBool(Key + "Paused", EditorApplication.isPaused);
                SessionState.SetBool(Key + "HudVisible", DevGui.Visible);
                SessionState.SetInt(Key + "InputBackground", (int)InputSystem.settings.backgroundBehavior);
                SessionState.SetInt(Key + "InputEditor", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
                SessionState.SetInt(Key + "OriginalMouse", Mouse.current != null ? Mouse.current.deviceId : -1);
                SessionState.SetInt(Key + "OriginalKeyboard", Keyboard.current != null ? Keyboard.current.deviceId : -1);
                SessionState.SetInt(Key + "CustomSize", -1);
                File.WriteAllText("Logs/brush-controls-review.txt", "Running actual map brush prices, controls and paid terrain transactions.\n");
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                SessionState.SetBool(Key + "Running", true); SessionState.SetInt(Key + "Stage", 0);
                SessionState.SetFloat(Key + "Started", (float)EditorApplication.timeSinceStartup);
                SessionState.SetString(Key + "Failure", "");
                EditorApplication.isPaused = false; Application.runInBackground = true;
                EditorApplication.update -= Update; EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                if (SessionState.GetBool(Key + "Running", false)) Fail(e);
                else
                {
                    Debug.LogException(e); Directory.CreateDirectory("Logs");
                    File.WriteAllText("Logs/brush-controls-review.txt", "Failed to start: " + e + "\n");
                    if (exitEditor) EditorApplication.Exit(1);
                }
            }
        }

        static void Update()
        {
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage >= 100)
            {
                if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) Finish(stage == 100);
                return;
            }
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0f) > 300f)
                    throw new TimeoutException("Brush controls review timed out; use a graphics-enabled non-batch isolated editor.");
                if (!EditorApplication.isPlaying) return;
                EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
                if (window != null) window.Repaint();
                if (releasePending && EditorApplication.timeSinceStartup >= releaseAt)
                { pressed = false; SendMouse(EventType.MouseUp); releasePending = false; }
                if (EditorApplication.timeSinceStartup < next) return;
                if (stage == 0) { if (Initialize()) Go(1, 3f); return; }
                switch (stage)
                {
                    case 1: MoveToPatch(); Go(2); break;
                    case 2:
                        Require(builder.Valid && builder.Price > 0, "The real clearing brush did not show its map price.");
                        if (!Capture("01-clearing-price-1440")) return;
                        CheckRects(); radiusBefore = builder.Radius;
                        var rail = HudRect("radiusSliderRect");
                        Click(new Vector2(rail.x + rail.width * .93f, rail.center.y)); Go(3); break;
                    case 3:
                        radiusAfter = builder.Radius;
                        Require(radiusAfter > radiusBefore + 5f, "Clicking the real radius rail did not update the brush.");
                        MoveToPatch(); Go(4); break;
                    case 4:
                        if (!Capture("02-radius-adjusted-1440")) return;
                        RecordClearQuote(); Click(WorldPoint(patch)); Go(5, 1f); break;
                    case 5:
                        Require(game.State.funds == beforeFunds - quotedClear, "The map clearing click did not charge its exact shown quote.");
                        Require(game.State.world.trees.Count(t => t.removed) == removedTreesBefore + clearTrees, "The map clearing click did not remove its selected trees.");
                        Require(selectedProps.All(game.State.world.GroundPropRemoved), "The map clearing click did not persist its selected rock/wood/shrub removals.");
                        Require(CareerConstruction.QuoteClear(game.State, patch, builder.Radius, game.valley.ClearCandidates(patch, builder.Radius)).Count == 0,
                            "Visible obstacles remained inside the brush after clearing.");
                        if (!Capture("03-cleared-rocks-and-trees-1440")) return;
                        clearedPatch = patch; ChooseBrush(CareerTool.Level); builder.Radius = 45f;
                        typedHeight = Mathf.Round((game.State.world.Height(patch) + 3f) * 10f) / 10f;
                        builder.TargetHeight = typedHeight; MoveToPatch(); Go(6); break;
                    case 6:
                        Require(builder.Valid && builder.Price > 0, "The levelling brush did not show an affordable map price.");
                        if (!Capture("04-level-price-1440")) return;
                        holdBefore = builder.TargetHeight; Hold(HudRect("heightUpRect").center, 1.8f); Go(7, 2.2f); break;
                    case 7:
                        holdDelta = builder.TargetHeight - holdBefore;
                        Require(holdDelta >= 7f && holdDelta < 60f, "Holding the height button did not accelerate safely: " + holdDelta);
                        if (!Capture("05-height-held-1440")) return;
                        Click(HudRect("heightValueRect").center); Go(8, .3f); break;
                    case 8:
                        File.AppendAllText("Logs/brush-controls-review.txt", "Before numeric entry: target=" + builder.TargetHeight + ", field=" + typeof(CareerHud).GetField("heightText", Instance).GetValue(hud) + ", captured=" + game.view.KeyboardCaptured + "\n");
                        SendKey(new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Command });
                        foreach (char c in typedHeight.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                            SendKey(new Event { type = EventType.KeyDown, character = c });
                        SendKey(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return, character = '\n' });
                        Go(9); break;
                    case 9:
                        Require(Mathf.Abs(builder.TargetHeight - typedHeight) < .01f, "The actual numeric height field did not accept the requested height: expected=" + typedHeight + ", actual=" + builder.TargetHeight + ", field=" + typeof(CareerHud).GetField("heightText", Instance).GetValue(hud) + ", captured=" + game.view.KeyboardCaptured);
                        Require(!game.view.KeyboardCaptured, "Committing the height field left world keyboard input captured.");
                        MoveToPatch(); Go(10); break;
                    case 10:
                        Require(builder.Valid && builder.Price > 0, "The numeric target did not produce a live earthwork quote.");
                        if (!Capture("06-height-entered-price-1440")) return;
                        quotedLevel = builder.Price; beforeFunds = game.State.funds;
                        Click(WorldPoint(patch)); Go(11, 1f); break;
                    case 11:
                        Require(game.State.funds == beforeFunds - quotedLevel, "The map level click did not charge its exact displayed earthwork quote.");
                        Require(Mathf.Abs(game.State.world.Height(patch) - typedHeight) < .04f, "One levelling click did not reach the target height.");
                        levelledHeights = (float[])game.State.world.heights.Clone(); beforeFunds = game.State.funds;
                        Go(12); break;
                    case 12: Click(pointer); Go(13); break;
                    case 13:
                        repeatFree = game.State.funds == beforeFunds && levelledHeights.SequenceEqual(game.State.world.heights);
                        Require(repeatFree, "Repeating a completed level click changed terrain or charged additional funds.");
                        if (!Capture("07-level-complete-no-extra-charge-1440")) return;
                        Resize(680, 650); patch = FindPatch(clearedPatch, 300f); FocusPatch();
                        ChooseBrush(CareerTool.ClearTrees); builder.Radius = 50f; MoveToPatch(); Go(14, 1f); break;
                    case 14:
                        Require(builder.Valid && builder.Price > 0, "The narrow clearing view lost the map quote."); CheckRects();
                        if (!Capture("08-clearing-price-680")) return;
                        patch = clearedPatch; FocusPatch(); ChooseBrush(CareerTool.Level); builder.Radius = 45f;
                        builder.TargetHeight = Mathf.Round((game.State.world.Height(patch) + 2f) * 10f) / 10f;
                        MoveToPatch(); Go(15, 1f); break;
                    case 15:
                        Require(builder.Valid && builder.Price > 0, "The narrow level view lost the map quote."); CheckRects();
                        if (!Capture("09-level-price-680")) return;
                        var store = new CareerSaveStore(Path.GetFullPath(SaveFolder)); store.FlushPending();
                        // The game's store owns the queued writes; use its barrier before opening a second reader.
                        ((CareerSaveStore)typeof(CareerGame).GetProperty("Store", Instance).GetValue(game)).FlushPending();
                        Require(store.Load(out var loaded, out string why), "Could not reopen the isolated reviewed clearing: " + why);
                        Require(selectedProps.All(loaded.world.GroundPropRemoved) && loaded.world.heights.SequenceEqual(game.State.world.heights),
                            "The saved game did not preserve obstacle removals and the one-pass terrain result.");
                        report = new Result { result = "Passed: actual 1440/680 brush UI, map prices, radius rail, accelerating held height, numeric entry, tree/rock clearing, exact charges and repeat-free one-pass levelling.",
                            unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"), clearedTrees = clearTrees, clearedProps = clearProps,
                            clearingPrice = quotedClear, levellingPrice = quotedLevel, radiusBefore = radiusBefore, radiusAfter = radiusAfter,
                            heldHeightDelta = holdDelta, secondLevelClickUnchanged = repeatFree, savedClearingVerified = true, screenshots = shots };
                        File.WriteAllText("Logs/brush-controls-review.json", JsonUtility.ToJson(report, true));
                        File.AppendAllText("Logs/brush-controls-review.txt", report.result + "\n");
                        SessionState.SetInt(Key + "Stage", 100); RestoreInput(); EditorApplication.ExitPlaymode(); break;
                }
            }
            catch (Exception e) { Fail(e); }
        }

        static bool Initialize()
        {
            game = Object.FindAnyObjectByType<CareerGame>();
            if (game == null || game.State == null) { next = EditorApplication.timeSinceStartup + .2f; return false; }
            hud = game.GetComponent<CareerHud>(); builder = game.GetComponent<CareerBuildTool>();
            Require(hud != null && hud.enabled && builder != null, "The production brush HUD/tools must be enabled.");
            typeof(CareerGame).GetField("store", Instance).SetValue(game, new CareerSaveStore(Path.GetFullPath(SaveFolder)));
            var state = CareerState.New(19770616, preset: RegionPreset.Woodland);
            Require(state.StartRegion(out string why), why); state.funds = SavedFunds;
            game.Initialize(state, true); game.Paused = true;
            DevGui.Visible = true; shots.Clear();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("Brush controls review mouse"); SessionState.SetInt(Key + "Mouse", mouse.deviceId);
            keyboard = InputSystem.AddDevice<Keyboard>("Brush controls review keyboard"); SessionState.SetInt(Key + "Keyboard", keyboard.deviceId);
            InputSystem.onBeforeUpdate -= FeedInput; InputSystem.onBeforeUpdate += FeedInput;
            SessionState.SetString(Key + "GameViews", JsonUtility.ToJson(new GameViewSetup { views = GameViews()
                .Select(v => new SavedGameView { id = v.GetEntityId().ToString(), size = (int)v.GetType().GetProperty("selectedSizeIndex", Instance).GetValue(v) }).ToArray() }));
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
            window = (EditorWindow)ScriptableObject.CreateInstance(type); window.titleContent = new GUIContent("Brush controls review"); window.Show();
            SessionState.SetString(Key + "Window", window.GetEntityId().ToString());
            pointer = new Vector2(0f, 0f); Resize(1440, 900);
            patch = FindPatch(Vector2.zero, 0f); FocusPatch();
            ChooseBrush(CareerTool.ClearTrees); builder.SingleTree = false; builder.Radius = 70f;
            return true;
        }
        static Vector2 FindPatch(Vector2 avoid, float distance)
        {
            var world = game.State.world;
            var props = game.valley.GetComponentInChildren<GroundCoverRenderer>();
            Require(props != null, "The real ground-detail renderer is missing.");
            foreach (var item in props.Instances.Where(i => i.kind == GroundCoverKind.Boulder)
                .OrderBy(i => i.position.sqrMagnitude))
            {
                var p = item.position;
                if ((p - avoid).sqrMagnitude < distance * distance || Mathf.Abs(p.x) > 2600f || Mathf.Abs(p.y) > 2600f || world.IsWater(p)) continue;
                bool road = false;
                for (int i = 1; i < world.publicRoad.Count; i++)
                    if (CareerConstruction.Distance(p, world.publicRoad[i - 1], world.publicRoad[i]) < 180f) { road = true; break; }
                if (road || world.settlements.Any(t => (t - p).sqrMagnitude < 300f * 300f)) continue;
                float slope = Mathf.Abs(world.Height(p + Vector2.right * 90f) - world.Height(p - Vector2.right * 90f)) +
                    Mathf.Abs(world.Height(p + Vector2.up * 90f) - world.Height(p - Vector2.up * 90f));
                if (slope > 25f || CareerConstruction.Trees(game.State, p, 70f).Count < 12) continue;
                return p;
            }
            throw new InvalidOperationException("Could not find a natural tree-and-rock patch for the actual clearing brush review.");
        }
        static void ChooseBrush(CareerTool tool)
        {
            if (game.SharedTools) game.BuildTools.SetTool(tool == CareerTool.ClearTrees ? DevTool.ClearTrees : DevTool.Level);
            else builder.Choose(tool);
        }
        static void FocusPatch() => game.view.FocusPoint(new Vector3(patch.x, game.valley.DisplayHeight(patch) + 1f, patch.y), 330f, 60f, -35f);
        static Vector2 WorldPoint(Vector2 point)
        {
            var viewport = game.view.GetComponent<Camera>().WorldToViewportPoint(new Vector3(point.x, game.valley.DisplayHeight(point), point.y));
            return new Vector2(viewport.x * width, (1f - viewport.y) * height);
        }
        static void MoveToPatch() { pointer = WorldPoint(patch); SendMouse(EventType.MouseMove); }
        static void RecordClearQuote()
        {
            var candidates = game.valley.ClearCandidates(patch, builder.Radius).ToArray();
            var quote = CareerConstruction.QuoteClear(game.State, patch, builder.Radius, candidates);
            Require(quote.Trees > 0 && quote.Props > 0, "The review patch must require both tree and rock/wood removal.");
            quotedClear = builder.Price;
            Require(quotedClear == quote.Price, "The real map price disagrees with the clearing transaction.");
            clearTrees = quote.Trees; clearProps = quote.Props; beforeFunds = game.State.funds;
            removedTreesBefore = game.State.world.trees.Count(t => t.removed);
            selectedProps = candidates.Where(i => CareerConstruction.Clearable(i.kind) && !game.State.world.GroundPropRemoved(i.id) &&
                (i.position - patch).sqrMagnitude <= Mathf.Pow(builder.Radius + GroundCoverScatter.FootprintRadius(i.kind, i.scale), 2f)).Select(i => i.id).Distinct().ToArray();
            Require(selectedProps.Length == clearProps, "The visible ground-prop selection did not match the transaction quote.");
        }
        static Rect HudRect(string name) => (Rect)typeof(CareerHud).GetField(name, Instance).GetValue(hud);
        static void CheckRects()
        {
            foreach (string name in new[] { "brushTrayRect", "worldQuoteRect" })
            {
                var r = HudRect(name); Require(r.width > 0f && r.height > 0f && r.xMin >= 0f && r.yMin >= 0f && r.xMax <= width + 1f && r.yMax <= height + 1f,
                    name + " escaped the actual " + width + "x" + height + " viewport: " + r);
            }
        }
        static float U(float value) => DevGui.U(value);
        static void Go(int stage, float settle = .7f)
        {
            SessionState.SetInt(Key + "Stage", stage); next = EditorApplication.timeSinceStartup + settle;
            File.AppendAllText("Logs/brush-controls-review.txt", "Stage " + stage + "\n");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static bool Await(bool condition, string message)
        {
            if (condition) return true;
            Require(EditorApplication.timeSinceStartup < actionDeadline, message);
            next = EditorApplication.timeSinceStartup + .1f; return false;
        }
        static void Click(Vector2 guiPoint)
        {
            pointer = guiPoint; pressed = true; releasePending = true; releaseAt = EditorApplication.timeSinceStartup + .15f;
            actionDeadline = EditorApplication.timeSinceStartup + 8f;
            File.AppendAllText("Logs/brush-controls-review.txt", $"Click GUI {guiPoint}, target {width}x{height}, editor Screen {Screen.width}x{Screen.height}\n");
            window.Focus(); SendMouse(EventType.MouseDown);
        }
        static void SendMouse(EventType type)
        {
            if (window == null) return;
            var queue = typeof(EditorGUIUtility).GetMethod("QueueGameViewInputEvent", Static, null, new[] { typeof(Event) }, null);
            if (queue != null)
            {
                queue.Invoke(null, new object[] { new Event { type = type, button = 0, mousePosition = pointer, displayIndex = 0 } });
                window.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); return;
            }
            var offsetProperty = window.GetType().GetProperty("gameMouseOffset", Instance);
            var scaleProperty = window.GetType().GetProperty("gameMouseScale", Instance);
            var offset = offsetProperty != null ? (Vector2)offsetProperty.GetValue(window) : new Vector2(0f, -21f);
            float scale = scaleProperty != null ? (float)scaleProperty.GetValue(window) : 1f;
            window.SendEvent(new Event { type = type, button = 0, mousePosition = pointer / scale - offset });
            window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        }
        static void Hold(Vector2 guiPoint, float seconds)
        {
            pointer = guiPoint; pressed = true; releasePending = true; releaseAt = EditorApplication.timeSinceStartup + seconds;
            actionDeadline = releaseAt + 8f; window.Focus(); SendMouse(EventType.MouseDown);
        }
        static void SendKey(Event e)
        {
            window.SendEvent(e);
            window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        }
        static void FeedInput()
        {
            if (mouse == null || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(pointer.x, height - pointer.y) }
                .WithButton(MouseButton.Left, pressed));
            if (keyboard != null) { keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }
        }

        static object SizeGroup()
        {
            var assembly = typeof(Editor).Assembly;
            var type = assembly.GetType("UnityEditor.GameViewSizes", true);
            var instance = type.GetProperty("instance", Static).GetValue(null);
            var groupType = window != null ? window.GetType().GetProperty("currentSizeGroupType", Static)?.GetValue(null) : null;
            if (groupType == null) groupType = Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeGroupType", true), 0);
            return type.GetMethod("GetGroup", Instance).Invoke(instance, new[] { groupType });
        }
        static EditorWindow[] GameViews() => Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(v => v.GetType().FullName == "UnityEditor.GameView").ToArray();
        static void Resize(int wantedWidth, int wantedHeight)
        {
            width = wantedWidth; height = wantedHeight; var group = SizeGroup(); int previous = SessionState.GetInt(Key + "CustomSize", -1);
            if (previous >= 0)
            {
                foreach (var view in GameViews()) view.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(view, 0);
                group.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(group, new object[] { previous });
            }
            var assembly = typeof(Editor).Assembly; var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            var fixedKind = Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType", true), "FixedResolution");
            var size = Activator.CreateInstance(sizeType, Instance, null, new[] { fixedKind, (object)wantedWidth, wantedHeight, "Brush controls review temporary" }, null);
            int index = (int)group.GetType().GetMethod("GetTotalCount", Instance).Invoke(group, null);
            group.GetType().GetMethod("AddCustomSize", Instance).Invoke(group, new[] { size }); SessionState.SetInt(Key + "CustomSize", index);
            // Each GameView renders OnGUI against its own target size. Keep every local view at the review size so
            // a background GameView cannot replace RegionBrowser's last production hit rectangles with stale ones.
            foreach (var view in GameViews())
            { view.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(view, index); view.Repaint(); }
            window.position = new Rect(30f, 30f, Mathf.Min(1440f, wantedWidth), Mathf.Min(940f, wantedHeight + 42f));
            window.Focus(); window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        }
        static bool Capture(string name)
        {
            var field = window.GetType().GetField("m_RenderTexture", Instance);
            if (field == null) throw new MissingFieldException("GameView", "m_RenderTexture");
            var target = field.GetValue(window) as RenderTexture;
            if (target == null || !target.IsCreated() || target.width != width || target.height != height)
            { window.Repaint(); next = EditorApplication.timeSinceStartup + .2f; return false; }
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                int railHeight = Mathf.RoundToInt(U(width >= U(680) ? 40 : 72));
                var pixels = texture.GetPixels32();
                float DividerCoverage(bool top)
                {
                    int boundary = top ? texture.height - railHeight : railHeight - 1, best = 0;
                    for (int delta = -4; delta <= 4; delta++)
                    {
                        int y = Mathf.Clamp(boundary + delta, 0, texture.height - 1), gold = 0;
                        for (int x = 0; x < texture.width; x++)
                        {
                            var p = pixels[y * texture.width + x];
                            if (p.r > 180 && p.g > 80 && p.b < 130 && p.r > p.g + 20 && p.g > p.b + 20) gold++;
                        }
                        best = Mathf.Max(best, gold);
                    }
                    return best / (float)texture.width;
                }
                float topDivider = DividerCoverage(true), bottomDivider = DividerCoverage(false);
                File.AppendAllText("Logs/brush-controls-review.txt", $"{name}: divider top={topDivider:0.000}, bottom={bottomDivider:0.000}\n");
                Require(Mathf.Max(topDivider, bottomDivider) > .8f,
                    "The GameView render target did not contain the real HUD's full-width amber divider.");
                if (bottomDivider > topDivider)
                {
                    // Match GameView's Metal render-target flip when reading its texture directly.
                    var flipped = new Color32[pixels.Length];
                    for (int row = 0; row < texture.height; row++) Array.Copy(pixels, row * texture.width, flipped,
                        (texture.height - 1 - row) * texture.width, texture.width);
                    texture.SetPixels32(flipped); texture.Apply();
                }
                File.WriteAllBytes(Folder + name + ".png", texture.EncodeToPNG());
                shots.Add(new Shot { name = name, width = target.width, height = target.height, tool = builder.Tool.ToString(), price = builder.Price, radius = builder.Radius, targetHeight = builder.TargetHeight });
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
            return true;
        }
        static void RestoreInput()
        {
            InputSystem.onBeforeUpdate -= FeedInput;
            var reviewMouse = mouse != null ? mouse : InputSystem.GetDeviceById(SessionState.GetInt(Key + "Mouse", -1)) as Mouse;
            if (reviewMouse != null && reviewMouse.added) InputSystem.RemoveDevice(reviewMouse); mouse = null;
            var originalMouse = InputSystem.GetDeviceById(SessionState.GetInt(Key + "OriginalMouse", -1)) as Mouse;
            if (originalMouse != null) originalMouse.MakeCurrent();
            var reviewKeyboard = keyboard != null ? keyboard : InputSystem.GetDeviceById(SessionState.GetInt(Key + "Keyboard", -1)) as Keyboard;
            if (reviewKeyboard != null && reviewKeyboard.added) InputSystem.RemoveDevice(reviewKeyboard); keyboard = null;
            var originalKeyboard = InputSystem.GetDeviceById(SessionState.GetInt(Key + "OriginalKeyboard", -1)) as Keyboard;
            if (originalKeyboard != null) originalKeyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt(Key + "InputBackground", 0);
            InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Key + "InputEditor", 0);
            pressed = releasePending = false;
        }
        static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            Directory.CreateDirectory("Logs"); File.AppendAllText("Logs/brush-controls-review.txt", "Failed: " + exception + "\n");
            SessionState.SetString(Key + "Failure", exception.ToString()); SessionState.SetInt(Key + "Stage", 101);
            RestoreInput();
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else Finish(false);
        }
        static void Finish(bool success)
        {
            bool exit = SessionState.GetBool(Key + "Exit", false);
            try
            {
                RestoreInput(); EditorApplication.update -= Update; SessionState.SetBool(Key + "Running", false);
                if (window == null)
                {
                    string id = SessionState.GetString(Key + "Window", "");
                    window = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetEntityId().ToString() == id);
                }
                object group = null;
                try { group = SizeGroup(); } catch (Exception e) { Debug.LogWarning("Could not inspect temporary GameView size: " + e.Message); }
                string savedViews = SessionState.GetString(Key + "GameViews", "");
                if (!string.IsNullOrEmpty(savedViews))
                    foreach (var savedView in JsonUtility.FromJson<GameViewSetup>(savedViews).views ?? Array.Empty<SavedGameView>())
                    {
                        var existingView = GameViews().FirstOrDefault(v => v.GetEntityId().ToString() == savedView.id);
                        if (existingView != null) existingView.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(existingView, savedView.size);
                    }
                if (window != null) window.Close();
                int size = SessionState.GetInt(Key + "CustomSize", -1);
                if (size >= 0 && group != null) group.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(group, new object[] { size });
                SessionState.SetInt(Key + "CustomSize", -1);
                string setup = SessionState.GetString(Key + "Scenes", "");
                var original = string.IsNullOrEmpty(setup) ? Array.Empty<SavedScene>() : JsonUtility.FromJson<Setup>(setup).scenes ?? Array.Empty<SavedScene>();
                var scenes = original.Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
                if (scenes.Any(s => s.loaded))
                {
                    if (!scenes.Any(s => s.loaded && s.active)) scenes.First(s => s.loaded).active = true;
                    EditorSceneManager.RestoreSceneManagerSetup(scenes.Select(s => new SceneSetup {
                        path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
                }
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception cleanup)
            {
                success = false; Debug.LogException(cleanup);
                File.AppendAllText("Logs/brush-controls-review.txt", "Cleanup failed: " + cleanup + "\n");
            }
            finally
            {
                Application.runInBackground = SessionState.GetBool(Key + "Background", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "Paused", false); DevGui.Visible = SessionState.GetBool(Key + "HudVisible", true);
                if (exit) EditorApplication.Exit(success ? 0 : 1);
                else Debug.Log("BrushControlsReview: " + (success ? "passed" : "failed") + "; see Logs/brush-controls-review.txt");
            }
        }
    }
}
