using System;
using System.Collections.Generic;
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
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Captures the live region chooser, biomes, cliffs and responsive game HUD without saving scenes.</summary>
    [InitializeOnLoad]
    public static class RegionPlayReview
    {
        const string Key = "AirCity.RegionPlayReview.";
        const string Root = "Captures/NewGame/Regions/";
        const string WindowTitle = "Region and HUD review";
        static double next;
        static CareerGame game;
        static Mouse reviewMouse;
        static EditorWindow reviewWindow;
        static readonly List<Shot> shots = new List<Shot>();
        [Serializable] sealed class Shot { public string name, biome; public int seed, width, height, treeCount, treeDrawCalls; public bool wholeRegion; }
        [Serializable] sealed class Result { public string result, utc, unityVersion; public List<Shot> screenshots; }
        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class Setup { public SavedScene[] scenes; }

        static RegionPlayReview()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update;
        }

        [MenuItem("AirCity/Game/Review Regions and HUD")]
        public static void Run() => Begin(false);
        public static void RunFromCommandLine() => Begin(true);

        static void Begin(bool exitEditor)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before starting the region review.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your modified scenes before starting the region review.");
                SessionState.SetString(Key + "SceneSetup", JsonUtility.ToJson(new Setup { scenes = EditorSceneManager.GetSceneManagerSetup()
                    .Select(s => new SavedScene { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray() }));
                SessionState.SetBool(Key + "ExitEditor", exitEditor);
                Directory.CreateDirectory(Root); Directory.CreateDirectory("Logs");
                SessionState.SetInt(Key + "Background", (int)InputSystem.settings.backgroundBehavior);
                SessionState.SetInt(Key + "EditorInput", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
                SessionState.SetInt(Key + "OriginalMouse", Mouse.current != null ? Mouse.current.deviceId : -1);
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                foreach (string path in Directory.GetFiles(Root, "*.png")) File.Delete(path);
                var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
                var window = (EditorWindow)ScriptableObject.CreateInstance(type);
                reviewWindow = window; SessionState.SetString(Key + "WindowId", window.GetEntityId().ToString());
                window.titleContent = new GUIContent(WindowTitle); window.Show(); Resize(1440f, 900f);
                SessionState.SetBool(Key + "Running", true); SessionState.SetInt(Key + "Stage", 0);
                SessionState.SetFloat(Key + "Started", (float)EditorApplication.timeSinceStartup);
                SessionState.SetString(Key + "Failure", "");
                File.WriteAllText("Logs/region-play-review.txt", "Running live region and HUD review.\n");
                File.WriteAllText("Logs/region-render-diagnostics.txt", "Live scenery rendering diagnostics.\n");
                EditorApplication.update -= Update; EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (SessionState.GetBool(Key + "Running", false)) { SessionState.SetString(Key + "Failure", e.ToString()); Finish(false); }
            }
        }

        static void Update()
        {
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage >= 10)
            {
                if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) Finish(stage == 10);
                return;
            }
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0f) > 480f)
                    throw new InvalidOperationException("The region review timed out.");
                if (!EditorApplication.isPlaying) return;
                game = Object.FindAnyObjectByType<CareerGame>();
                if (game == null || game.State == null) return;
                if (game.GetComponent<CareerHud>() == null || !game.GetComponent<CareerHud>().enabled)
                    throw new InvalidOperationException("The live game HUD must remain enabled for review.");
                if (stage == 0)
                {
                    var field = typeof(CareerGame).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field == null) throw new MissingFieldException(nameof(CareerGame), "store");
                    field.SetValue(game, new CareerSaveStore(Path.GetFullPath("Logs/RegionPlayReview")));
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    reviewMouse = InputSystem.AddDevice<Mouse>("Region review mouse");
                    SessionState.SetInt(Key + "ReviewMouse", reviewMouse.deviceId);
                    InputSystem.onBeforeUpdate += FeedInput; shots.Clear();
                    Preview(19770616, RegionPreset.Woodland); Go(1); return;
                }
                if (EditorApplication.timeSinceStartup < next) return;
                switch (stage)
                {
                    case 1:
                        if (!Capture("01-woodland-region")) break;
                        Preview(873241, RegionPreset.Mountains); Go(2); break;
                    case 2:
                        if (!Capture("02-mountain-region")) break;
                        FocusCliff(); Go(3); break;
                    case 3:
                        if (!Capture("03-mountain-cliffs")) break;
                        FocusRoad(); Go(-3); break;
                    case -3:
                        if (!Capture("03b-grounded-road")) break;
                        Preview(445090, RegionPreset.Tropical); Go(4); break;
                    case 4:
                        if (!Capture("04-tropical-region")) break;
                        FocusCoast(); Go(-5); break;
                    case -5:
                        if (!Capture("04b-lagoon-and-harbour")) break;
                        FocusPalm(); Go(-4); break;
                    case -4:
                        if (!Capture("04c-tropical-palms")) break;
                        Preview(692835, RegionPreset.Desert); Go(5); break;
                    case 5:
                        if (!Capture("05-desert-region")) break;
                        Preview(43, RegionPreset.Woodland); Go(6); break;
                    case 6:
                        if (!Capture("06-regenerated-woodland")) break;
                        Preview(99, RegionPreset.Random); Go(7); break;
                    case 7:
                        if (!Capture("07-random-region")) break;
                        var state = CareerState.New(42, preset: RegionPreset.Woodland);
                        if (!state.StartRegion(out string why)) throw new InvalidOperationException(why);
                        if (state.funds != CareerState.StartingFunds || state.BuildableBounds.width < ValleyWorld.Size - 1f)
                            throw new InvalidOperationException("Starting a region must unlock the entire map without a land purchase.");
                        game.Initialize(state, false); game.Paused = true; game.view.Region(); Go(8); break;
                    case 8:
                        if (!Capture("08-region-building-hud")) break;
                        Resize(680f, 650f); Go(9, 1.5f); break;
                    case 9:
                        if (!Capture("09-narrow-hud")) break;
                        if (shots[shots.Count - 1].width >= shots[shots.Count - 2].width)
                            throw new InvalidOperationException("The narrow Game view did not resize its actual render target.");
                        var result = new Result { result = "Passed: four biomes, seed regeneration, cliffs, free whole-region start and responsive live HUD.",
                            utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, screenshots = shots };
                        File.WriteAllText("Logs/region-play-review.json", JsonUtility.ToJson(result, true));
                        File.WriteAllText("Logs/region-play-review.txt", result.result + "\n");
                        RestoreInput(); SessionState.SetInt(Key + "Stage", 10); EditorApplication.ExitPlaymode(); break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e); SessionState.SetString(Key + "Failure", e.ToString()); RestoreInput();
                SessionState.SetInt(Key + "Stage", 11);
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else Finish(false);
            }
        }

        static void Preview(int seed, RegionPreset preset)
        {
            game.NewWorld(seed, preset); game.Paused = true; game.view.Region();
        }
        static void FocusCliff()
        {
            var world = game.State.world;
            float best = 0f; Vector2 target = Vector2.zero;
            for (int z = 20; z < ValleyWorld.Resolution - 20; z += 2)
            for (int x = 20; x < ValleyWorld.Resolution - 20; x += 2)
            {
                int i = z * ValleyWorld.Resolution + x;
                float slope = Mathf.Max(Mathf.Abs(world.heights[i + 1] - world.heights[i - 1]), Mathf.Abs(world.heights[i + ValleyWorld.Resolution] - world.heights[i - ValleyWorld.Resolution]));
                if (slope <= best) continue;
                best = slope; target = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
            }
            game.view.FocusPoint(new Vector3(target.x, world.Height(target), target.y), 550f, 43f, -35f);
        }
        static void FocusRoad()
        {
            var w = game.State.world; int at = w.publicRoad.Count / 2;
            float largestCut = 0f;
            for(int i=2;i<w.publicRoad.Count-2;i++)
            {
                var p=w.publicRoad[i]; if(Mathf.Abs(p.x)>2800f||Mathf.Abs(p.y)>2800f)continue;
                float cut=Mathf.Abs(w.Height(p)-w.roadHeights[i]);
                if(cut>largestCut){largestCut=cut;at=i;}
            }
            var point=w.publicRoad[at];
            game.view.FocusPoint(new Vector3(point.x,game.valley.DisplayHeight(point)+3f,point.y),220f,38f,-35f);
        }
        static void FocusCoast()
        {
            var scenery=game.GetComponentInChildren<RegionSceneryView>();
            var w=game.State.world;
            var point=scenery!=null&&scenery.HasHarbour?scenery.HarbourPosition:w.publicRoad[0];
            game.view.FocusPoint(new Vector3(point.x,w.SeaLevel+5f,point.y),650f,46f,-35f);
        }
        static void FocusPalm()
        {
            var world = game.State.world;
            var tree = world.trees.FirstOrDefault(t => !t.removed && Mathf.Abs(t.position.x) < 2200f && Mathf.Abs(t.position.y) < 2200f);
            if (tree == null) throw new InvalidOperationException("The tropical region has no palms to inspect.");
            game.view.FocusPoint(new Vector3(tree.position.x, world.Height(tree.position) + tree.height * .5f, tree.position.y), 90f, 40f, -35f);
        }
        static void Go(int stage, float settle = 1.2f) { SessionState.SetInt(Key + "Stage", stage); next = EditorApplication.timeSinceStartup + settle; }
        static EditorWindow Window()
        {
            var window = FindWindow();
            if (window == null) throw new InvalidOperationException("The floating region review Game view was closed.");
            return window;
        }
        static EditorWindow FindWindow()
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
            if (reviewWindow != null && reviewWindow.GetType() == type) return reviewWindow;
            string id = SessionState.GetString(Key + "WindowId", "");
            reviewWindow = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == type && w.GetEntityId().ToString() == id)
                ?? Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType() == type && w.titleContent.text == WindowTitle);
            return reviewWindow;
        }
        static void Resize(float width, float height)
        {
            var window = Window(); window.maximized = false;
            var freeAspect = window.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (freeAspect == null || !freeAspect.CanWrite) throw new MissingMemberException("GameView.selectedSizeIndex");
            freeAspect.SetValue(window, 0); window.position = new Rect(30f, 30f, width, height); window.Show();
            window.titleContent = new GUIContent(WindowTitle); window.Focus(); window.Repaint();
        }
        static void FeedInput()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || reviewMouse == null) return;
            reviewMouse.MakeCurrent(); InputSystem.QueueStateEvent(reviewMouse, new MouseState { position = new Vector2(0f, 0f) });
        }
        static bool Capture(string name)
        {
            string path = Root + name + ".png";
            if (!File.Exists(path)) { ScreenCapture.CaptureScreenshot(path); next = EditorApplication.timeSinceStartup + 1f; return false; }
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24) throw new InvalidOperationException("The Game-view screenshot is incomplete: " + path);
            int width = BigEndian(bytes, 16), height = BigEndian(bytes, 20);
            if (width < 300 || height < 300) throw new InvalidOperationException("The Game-view screenshot has an invalid size: " + path);
            var forest = game.GetComponentInChildren<ForestRenderer>();
            if (forest == null) forest = Object.FindAnyObjectByType<ForestRenderer>();
            int count = forest != null ? forest.TreeCount : -1, draws = forest != null ? forest.LastDrawCalls : -1;
            var scenery=game.GetComponentInChildren<RegionSceneryView>();
            File.AppendAllText("Logs/region-render-diagnostics.txt", "Landscape " + game.State.world.landscapeSource +
                ": houses=" + (scenery!=null?scenery.HouseCount:0) + ", harbour=" + (scenery!=null&&scenery.HasHarbour) + "\n");
            string diagnostic = name + ": saved trees=" + game.State.world.trees.Count + ", rendered trees=" + count + ", forest draws=" + draws;
            var active = forest != null ? typeof(ForestRenderer).GetField("activeLibrary", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(forest) as TreeLibrary : null;
            if (active != null && active.variants != null && active.variants.Length > 0)
                diagnostic += ", shader=" + active.foliage.shader.name + ", shader supported=" + active.foliage.shader.isSupported +
                    ", instancing=" + active.foliage.enableInstancing + ", far bark indices=" + active.variants[0].levels[2].GetIndexCount(0) +
                    ", far canopy indices=" + active.variants[0].levels[2].GetIndexCount(1);
            File.AppendAllText("Logs/region-render-diagnostics.txt", diagnostic + "\n");
            shots.Add(new Shot { name = name, width = width, height = height, seed = game.State.world.seed,
                biome = game.State.world.preset.ToString(), wholeRegion = game.State.Site != null && game.State.wholeRegion,
                treeCount = count, treeDrawCalls = draws });
            return true;
        }
        static int BigEndian(byte[] bytes, int at) => bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3];
        static void RestoreInput()
        {
            InputSystem.onBeforeUpdate -= FeedInput;
            var mouse = reviewMouse ?? InputSystem.GetDeviceById(SessionState.GetInt(Key + "ReviewMouse", -1)) as Mouse;
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); reviewMouse = null;
            var original = InputSystem.GetDeviceById(SessionState.GetInt(Key + "OriginalMouse", -1)) as Mouse;
            if (original != null) original.MakeCurrent();
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt(Key + "Background", 0);
            InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Key + "EditorInput", 0);
        }
        static void Finish(bool success)
        {
            RestoreInput(); SessionState.SetBool(Key + "Running", false); EditorApplication.update -= Update;
            var window = FindWindow(); if (window != null) window.Close();
            if (!success) File.WriteAllText("Logs/region-play-review.txt", "Failed: " + SessionState.GetString(Key + "Failure", "Review startup failed.") + "\n");
            string original = SessionState.GetString(Key + "SceneSetup", "");
            if (!string.IsNullOrEmpty(original)) EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<Setup>(original).scenes
                .Select(s => new SceneSetup { path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
            if (SessionState.GetBool(Key + "ExitEditor", false)) EditorApplication.Exit(success ? 0 : 1);
            else Debug.Log("RegionPlayReview: " + (success ? "passed; screenshots in " + Root : "failed; see Logs/region-play-review.txt"));
        }
    }
}
