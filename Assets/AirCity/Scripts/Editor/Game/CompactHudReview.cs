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
    /// <summary>Actual GameView/OnGUI screenshots and Options interaction checks. Run in an isolated non-batch editor.
    /// Captures the GameView's own IMGUI-inclusive render target, without waiting on ScreenCapture's end-of-frame queue.</summary>
    [InitializeOnLoad]
    public static class CompactHudReview
    {
        const string Key = "AirCity.CompactHudReview.";
        const string Folder = "Captures/NewGame/CompactHud/";
        const string SaveFolder = "Logs/CompactHudReview";
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        const int SavedFunds = 482350;
        static CareerGame game;
        static CareerHud hud;
        static EditorWindow window;
        static Mouse mouse;
        static Vector2 pointer;
        static bool pressed, releasePending;
        static double next, releaseAt, actionDeadline;
        static int width, height, layoutVersion;
        static object originalState;
        static readonly List<Shot> shots = new List<Shot>();
        [Serializable] sealed class Shot { public string name; public int width, height; public bool optionsOpen; }
        [Serializable] sealed class Result { public string result, unityVersion, utc; public List<Shot> screenshots; }
        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class Setup { public SavedScene[] scenes; }

        static CompactHudReview()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update;
        }

        [MenuItem("AirCity/Game/Review Compact HUD")]
        public static void Run() => Begin(false);
        public static void RunFromCommandLine() => Begin(true);

        static void Begin(bool exitEditor)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before running the compact HUD review.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save modified scenes before running the compact HUD review.");
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
                SessionState.SetInt(Key + "CustomSize", -1);
                File.WriteAllText("Logs/compact-hud-review.txt", "Running actual compact HUD screenshots and Options checks.\n");
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
                    File.WriteAllText("Logs/compact-hud-review.txt", "Failed to start: " + e + "\n");
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
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0f) > 180f)
                    throw new TimeoutException("Compact HUD review timed out. Run with a graphics-enabled non-batch editor.");
                if (!EditorApplication.isPlaying) return;
                EditorApplication.isPaused = false;
                EditorApplication.QueuePlayerLoopUpdate();
                if (window != null) window.Repaint();
                if (releasePending && EditorApplication.timeSinceStartup >= releaseAt)
                { SendMouse(EventType.MouseUp); pressed = releasePending = false; }
                if (EditorApplication.timeSinceStartup < next) return;
                if (stage == 0) { if (Initialize()) Go(1, 3f); return; }
                switch (stage)
                {
                    case 1:
                        if (!Capture("01-compact-1910")) return;
                        Resize(1440, 900); Go(2); break;
                    case 2:
                        if (!Capture("02-compact-1440")) return;
                        Click(OptionsPoint()); Go(3); break;
                    case 3:
                        if (!Await(MenuOpen, "The Options button did not open its actual IMGUI popup.")) return;
                        if (!Capture("03-options-1440")) return;
                        layoutVersion = game.Airport.LayoutVersion;
                        Click(new Vector2(420f, 210f)); Go(4); break;
                    case 4:
                        if (!Await(!MenuOpen, "Clicking outside Options did not close it.")) return;
                        Require(game.Airport.LayoutVersion == layoutVersion, "The outside-closing click changed the airport.");
                        Resize(680, 650); Go(5); break;
                    case 5:
                        if (!Capture("04-compact-680")) return;
                        Click(OptionsPoint()); Go(6); break;
                    case 6:
                        if (!Await(MenuOpen, "Options did not open at narrow width.")) return;
                        if (!Capture("05-options-680")) return;
                        originalState = game.State;
                        typeof(CareerGame).GetField("persist", Instance).SetValue(game, false); // Keep the known checkpoint stable during the Open assertion.
                        game.State.funds = 19;
                        Click(MenuItemPoint(1)); Go(7, 1.5f); break;
                    case 7:
                        if (!Await(!ReferenceEquals(originalState, game.State), "Open did not restore a saved airport state.")) return;
                        Require(game.State.funds == SavedFunds, "Open did not restore the isolated saved checkpoint's funds.");
                        Require(!MenuOpen, "Open left the Options popup open.");
                        game.Paused = true; game.State.funds = SavedFunds + 1;
                        Click(OptionsPoint()); Go(8); break;
                    case 8:
                        if (!Await(MenuOpen, "Options did not reopen for Save.")) return;
                        Click(MenuItemPoint(0)); Go(9, 1f); break;
                    case 9:
                        if (!Await(!MenuOpen, "Save left the Options popup open.")) return;
                        var store = new CareerSaveStore(Path.GetFullPath(SaveFolder));
                        Require(store.Load(out var loaded, out string why), "Could not inspect isolated Save: " + why);
                        Require(loaded.funds == SavedFunds + 1, "Save did not write the current checkpoint.");
                        if (!Capture("06-after-open-and-save-680")) return;
                        var result = new Result { result = "Passed: actual compact HUD at 1910/1440/680 px, Options popup, outside dismissal, isolated Open and Save.",
                            unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"), screenshots = shots };
                        File.WriteAllText("Logs/compact-hud-review.json", JsonUtility.ToJson(result, true));
                        File.WriteAllText("Logs/compact-hud-review.txt", result.result + "\n");
                        SessionState.SetInt(Key + "Stage", 100); RestoreInput(); EditorApplication.ExitPlaymode(); break;
                }
            }
            catch (Exception e) { Fail(e); }
        }

        static bool Initialize()
        {
            game = Object.FindAnyObjectByType<CareerGame>();
            if (game == null || game.State == null) { next = EditorApplication.timeSinceStartup + .2f; return false; }
            hud = game.GetComponent<CareerHud>(); Require(hud != null && hud.enabled, "The real CareerHud must be enabled.");
            typeof(CareerGame).GetField("store", Instance).SetValue(game, new CareerSaveStore(Path.GetFullPath(SaveFolder)));
            var state = CareerState.New(445090, preset: RegionPreset.Tropical);
            Require(state.StartRegion(out string why), why); state.funds = SavedFunds; state.operatingSeconds = 3003.5f;
            game.Initialize(state, true); game.Paused = true; game.view.Region();
            Require(game.Save(false), game.Message);
            DevGui.Visible = true; shots.Clear();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("Compact HUD review mouse"); SessionState.SetInt(Key + "Mouse", mouse.deviceId);
            InputSystem.onBeforeUpdate -= FeedInput; InputSystem.onBeforeUpdate += FeedInput;
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
            window = (EditorWindow)ScriptableObject.CreateInstance(type); window.titleContent = new GUIContent("Compact HUD review"); window.Show();
            SessionState.SetString(Key + "Window", window.GetEntityId().ToString());
            pointer = new Vector2(0f, height); Resize(1910, 900);
            return true;
        }

        static bool MenuOpen => hud != null && (bool)typeof(CareerHud).GetField("gameMenu", Instance).GetValue(hud);
        static float U(float value) => DevGui.U(value);
        static List<Rect> HitAreas => (List<Rect>)typeof(CareerHud).GetField("hitAreas", Instance).GetValue(hud);
        // Screen.width queried in EditorApplication.update need not belong to the just-rendered GameView.
        // Hit areas come from the production HUD's actual Repaint at the selected target size.
        static Rect OptionsRect
        {
            get
            {
                var found = HitAreas.LastOrDefault(r => Mathf.Abs(r.width - U(80)) < 1f && Mathf.Abs(r.height - U(26)) < 1f && r.x > width - U(120) && r.xMax <= width && r.y < U(80));
                return found.width > 0f ? found : new Rect(width - U(92), U(width < U(680) ? 39 : 7), U(80), U(26));
            }
        }
        static Rect MenuRect
        {
            get
            {
                var found = HitAreas.LastOrDefault(r => Mathf.Abs(r.width - U(232)) < 1f && r.x > width - U(270) && r.xMax <= width && r.height > U(100));
                return found.width > 0f ? found : new Rect(width - U(244), U(width < U(680) ? 78 : 46), U(232), U(178));
            }
        }
        static Vector2 OptionsPoint() => OptionsRect.center;
        static Vector2 MenuItemPoint(int item) => new Vector2(MenuRect.center.x, MenuRect.y + U(21 + item * 32));
        static void Go(int stage, float settle = .7f)
        {
            SessionState.SetInt(Key + "Stage", stage); next = EditorApplication.timeSinceStartup + settle;
            File.AppendAllText("Logs/compact-hud-review.txt", "Stage " + stage + "\n");
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
            File.AppendAllText("Logs/compact-hud-review.txt", $"Click GUI {guiPoint}, target {width}x{height}, editor Screen {Screen.width}x{Screen.height}\n");
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
        static void FeedInput()
        {
            if (mouse == null || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(pointer.x, height - pointer.y) }
                .WithButton(MouseButton.Left, pressed));
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
        static void Resize(int wantedWidth, int wantedHeight)
        {
            width = wantedWidth; height = wantedHeight;
            var group = SizeGroup(); int previous = SessionState.GetInt(Key + "CustomSize", -1);
            if (previous >= 0)
            {
                window.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(window, 0);
                group.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(group, new object[] { previous });
            }
            var assembly = typeof(Editor).Assembly;
            var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            var kindType = assembly.GetType("UnityEditor.GameViewSizeType", true);
            var fixedKind = Enum.Parse(kindType, "FixedResolution");
            var size = Activator.CreateInstance(sizeType, Instance, null,
                new[] { fixedKind, (object)wantedWidth, wantedHeight, "Compact HUD review temporary" }, null);
            int index = (int)group.GetType().GetMethod("GetTotalCount", Instance).Invoke(group, null);
            group.GetType().GetMethod("AddCustomSize", Instance).Invoke(group, new[] { size });
            SessionState.SetInt(Key + "CustomSize", index);
            window.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(window, index);
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
                File.AppendAllText("Logs/compact-hud-review.txt", $"{name}: divider top={topDivider:0.000}, bottom={bottomDivider:0.000}\n");
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
                shots.Add(new Shot { name = name, width = target.width, height = target.height, optionsOpen = MenuOpen });
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
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt(Key + "InputBackground", 0);
            InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Key + "InputEditor", 0);
            pressed = releasePending = false;
        }
        static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/compact-hud-review.txt", "Failed: " + exception + "\n");
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
                File.AppendAllText("Logs/compact-hud-review.txt", "Cleanup failed: " + cleanup + "\n");
            }
            finally
            {
                Application.runInBackground = SessionState.GetBool(Key + "Background", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "Paused", false); DevGui.Visible = SessionState.GetBool(Key + "HudVisible", true);
                if (exit) EditorApplication.Exit(success ? 0 : 1);
                else Debug.Log("CompactHudReview: " + (success ? "passed" : "failed") + "; see Logs/compact-hud-review.txt");
            }
        }
    }
}
