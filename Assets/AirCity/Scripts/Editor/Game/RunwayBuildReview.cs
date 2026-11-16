using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Dev;
using AirCity.Airfield;
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
    /// <summary>Actual two-click shared runway placement and map prices on a controlled hill. Use an isolated non-batch editor.
    /// Captures the GameView's own IMGUI-inclusive render target, without waiting on ScreenCapture's end-of-frame queue.</summary>
    [InitializeOnLoad]
    public static class RunwayBuildReview
    {
        const string Key = "AirCity.RunwayBuildReview.";
        const string Folder = "Captures/NewGame/RunwayBuild/";
        const string SaveFolder = "Logs/RunwayBuildReview";
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        const int SavedFunds = 1000000;
        static CareerGame game;
        static CareerHud hud;
        static EditorWindow window;
        static Mouse mouse;
        static Keyboard keyboard;
        static Vector2 pointer;
        static bool pressed, releasePending;
        static double next, releaseAt, actionDeadline;
        static int width, height, layoutVersion;
        static readonly Vector2 Start = new Vector2(-350f, 0f), End = new Vector2(350f, 0f);
        static DevAirfieldTool tool;
        static float[] originalHeights;
        static int quotedPrice, gradingPrice;
        static float earthwork;
        static Runway built;
        static readonly List<Shot> shots = new List<Shot>();
        [Serializable] sealed class Shot { public string name; public int width, height, price, gradingPrice; public float slope; }
        [Serializable] sealed class Result
        {
            public string result, unityVersion, utc, fixture;
            public int price, gradingPrice;
            public float earthwork, grade, worldStart, worldEnd, airportDatum;
            public bool previewUnchanged, terrainSupportsPavement, naturalEdges, savedProfileVerified;
            public List<Shot> screenshots;
        }
        [Serializable] sealed class SavedGameView { public string id; public int size; }
        [Serializable] sealed class GameViewSetup { public SavedGameView[] views; }
        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class Setup { public SavedScene[] scenes; }

        static RunwayBuildReview()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update;
        }

        [MenuItem("AirCity/Game/Review Runway build")]
        public static void Run() => Begin(false);
        public static void RunFromCommandLine() => Begin(true);

        static void Begin(bool exitEditor)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before running the runway build review.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save modified scenes before running the runway build review.");
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
                File.WriteAllText("Logs/runway-build-review.txt", "Running actual shared-runway placement on a controlled 12% hill with crooked bumps and crossfall.\n");
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
                    File.WriteAllText("Logs/runway-build-review.txt", "Failed to start: " + e + "\n");
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
                    throw new TimeoutException("Runway build review timed out; use a graphics-enabled isolated editor.");
                if (!EditorApplication.isPlaying) return;
                EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
                if (window != null) window.Repaint();
                if (releasePending && EditorApplication.timeSinceStartup >= releaseAt)
                { pressed = false; SendMouse(EventType.MouseUp); releasePending = false; }
                if (EditorApplication.timeSinceStartup < next) return;
                if (stage == 0) { if (Initialize()) Go(1, 3f); return; }
                switch (stage)
                {
                    case 1: Click(WorldPoint(Start)); Go(2); break;
                    case 2:
                        Require(tool.PlacingRunway != null, "The first actual map click did not start the shared runway tool.");
                        MovePointer(End); Go(3, 1f); break;
                    case 3:
                        CheckPreview();
                        if (!Capture("01-controlled-hill-preview-price-1440")) return;
                        quotedPrice = tool.RunwayPrice; gradingPrice = tool.RunwayGradingPrice; earthwork = tool.RunwayEarthwork;
                        Click(WorldPoint(End)); Go(6, 1f); break;
                    case 6:
                        Require(tool.PlacingRunway == null, "The second map click did not commit the quoted runway: " + tool.RunwayProblem);
                        built = game.Airport.Runways.SingleOrDefault(r => (r.gameObject.hideFlags & HideFlags.DontSave) == 0);
                        Require(built != null, "The committed runway is missing from the shared airport.");
                        Require(game.State.funds == SavedFunds - quotedPrice, "The shared commit did not charge exactly the visible construction-plus-grading quote.");
                        CheckBuiltProfile();
                        Resize(680, 650); FocusHill(); pointer = new Vector2(50f, 160f); Go(7, 1f); break;
                    case 7:
                        if (!Capture("02-controlled-hill-runway-built-680")) return;
                        Resize(1440, 900); FocusHill(); Go(8, 1f); break;
                    case 8:
                        if (!Capture("03-controlled-hill-runway-built-1440")) return;
                        game.BuildTools.SetTool(DevTool.Select);
                        game.view.FocusPoint(new Vector3(0f, game.State.world.Height(Vector2.zero), 0f), 700f, 18f, 0f);
                        Go(9, 1f); break;
                    case 9:
                        if (!Capture("04-controlled-hill-low-angle-profile")) return;
                        VerifySave();
                        var result = new Result { result = "Passed: actual two-click shared-runway preview and paid commit on a controlled 12% hill; crooked ground graded, no grass-strip meshes, exact funds, supported pavement and portable profile save/restore.",
                            fixture = "Controlled synthetic hill: west threshold 100m, east 184m over700m, +6m sinusoidal bumps and5%crossfall. Not a real geographic location.",
                            unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"), price = quotedPrice, gradingPrice = gradingPrice,
                            earthwork = earthwork, grade = built.Grade, worldStart = game.Airport.transform.position.y + built.start.y,
                            worldEnd = game.Airport.transform.position.y + built.end.y, airportDatum = game.Airport.transform.position.y,
                            previewUnchanged = true, terrainSupportsPavement = true, naturalEdges = true, savedProfileVerified = true, screenshots = shots };
                        File.WriteAllText("Logs/runway-build-review.json", JsonUtility.ToJson(result, true));
                        File.AppendAllText("Logs/runway-build-review.txt", result.result + "\n");
                        SessionState.SetInt(Key + "Stage", 100); RestoreInput(); EditorApplication.ExitPlaymode(); break;
                }
            }
            catch (Exception e) { Fail(e); }
        }

        static bool Initialize()
        {
            game = Object.FindAnyObjectByType<CareerGame>();
            if (game == null || game.State == null) { next = EditorApplication.timeSinceStartup + .2f; return false; }
            hud = game.GetComponent<CareerHud>();
            Require(hud != null && hud.enabled, "The production map-price HUD must be enabled.");
            typeof(CareerGame).GetField("store", Instance).SetValue(game, new CareerSaveStore(Path.GetFullPath(SaveFolder)));
            var state = CareerState.New(19770616, preset: RegionPreset.Woodland);
            var world = state.world; world.trees.Clear(); world.hasOcean = world.hasRiver = false; world.waterHeights = null;
            world.landscapeSource = "Controlled hill UI fixture"; world.hasCoordinates = false;
            world.latitude = world.longitude = 0d; world.elevationSha256 = world.coverSha256 = null;
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                float px = x * world.Spacing - ValleyWorld.Size * .5f, pz = z * world.Spacing - ValleyWorld.Size * .5f;
                int i = z * ValleyWorld.Resolution + x;
                world.heights[i] = FixtureHeight(px, pz); world.landCover[i] = 50;
            }
            world.publicRoad = new List<Vector2> { new Vector2(2000f, -3200f), new Vector2(2000f, 3200f) };
            world.roadHeights = world.publicRoad.Select(p => FixtureHeight(p.x, p.y)).ToArray();
            world.settlements = new List<Vector2> { new Vector2(2300f, -2600f), new Vector2(2300f, 2600f) };
            foreach (var site in world.sites) { site.centre = Vector2.zero; site.elevation = 100f; site.name = "Controlled hill"; }
            state.publicRoads = PublicRoadNetwork.FromWorld(world);
            Require(state.StartRegion(out string why), why); state.funds = SavedFunds;
            Require(state.Validate(out why), "Controlled fixture is invalid: " + why);
            game.Initialize(state, false); game.Paused = true;
            tool = game.BuildTools; Require(game.SharedTools && tool != null, "The actual shared build tool must be active.");
            tool.SetTool(DevTool.Runway); tool.SetSurface(PavementSurface.Asphalt); tool.SetWidth(18f);
            Require(tool.NewWidth == 18f && tool.Surface == PavementSurface.Asphalt, "The actual runway tool did not accept its asphalt18m setup.");
            originalHeights = (float[])world.heights.Clone();
            DevGui.Visible = true; shots.Clear();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("Runway build review mouse"); SessionState.SetInt(Key + "Mouse", mouse.deviceId);
            keyboard = InputSystem.AddDevice<Keyboard>("Runway build review keyboard"); SessionState.SetInt(Key + "Keyboard", keyboard.deviceId);
            InputSystem.onBeforeUpdate -= FeedInput; InputSystem.onBeforeUpdate += FeedInput;
            SessionState.SetString(Key + "GameViews", JsonUtility.ToJson(new GameViewSetup { views = GameViews()
                .Select(v => new SavedGameView { id = v.GetEntityId().ToString(), size = (int)v.GetType().GetProperty("selectedSizeIndex", Instance).GetValue(v) }).ToArray() }));
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
            window = (EditorWindow)ScriptableObject.CreateInstance(type); window.titleContent = new GUIContent("Runway build review"); window.Show();
            SessionState.SetString(Key + "Window", window.GetEntityId().ToString());
            pointer = new Vector2(0f, 0f); Resize(1440, 900); FocusHill();
            return true;
        }
        static float FixtureHeight(float x, float z)
        {
            float along = Mathf.Clamp(x, -500f, 500f);
            return 100f + (along + 350f) * .12f + Mathf.Sin((x + 350f) * Mathf.PI * 4f / 700f) * 6f *
                Mathf.Clamp01((700f - Mathf.Abs(x)) / 200f) + .05f * Mathf.Clamp(z, -100f, 100f);
        }
        static void FocusHill() => game.view.FocusPoint(new Vector3(0f, game.State.world.Height(Vector2.zero), 0f), 1000f, 55f, 0f);
        static Vector2 WorldPoint(Vector2 point)
        {
            var viewport = game.view.GetComponent<Camera>().WorldToViewportPoint(new Vector3(point.x, game.valley.DisplayHeight(point), point.y));
            return new Vector2(viewport.x * width, (1f - viewport.y) * height);
        }
        static void MovePointer(Vector2 point) { pointer = WorldPoint(point); SendMouse(EventType.MouseMove); }
        static void CheckPreview()
        {
            var r = tool.PlacingRunway;
            Require(r != null && string.IsNullOrEmpty(tool.RunwayProblem), "The shared runway preview was refused: " + tool.RunwayProblem);
            Require(tool.RunwayPrice > 14000 && tool.RunwayGradingPrice > 0 && tool.RunwayEarthwork > 1f, "The crooked-hill preview did not include a paid grading quote.");
            Require(game.State.funds == SavedFunds && originalHeights.SequenceEqual(game.State.world.heights), "Merely previewing the runway changed funds or terrain.");
            Require(Mathf.Abs(r.Length - 700f) < .1f, "The map preview did not reach the desired second threshold: " + r.Length);
            Require(Mathf.Abs(r.Airport.transform.position.y + r.start.y - 100f) < .04f && Mathf.Abs(r.Airport.transform.position.y + r.end.y - 184f) < .04f,
                "The actual preview did not follow the natural hill endpoints.");
            var rect = (Rect)typeof(CareerHud).GetField("worldQuoteRect", Instance).GetValue(hud);
            Require(rect.width > 0f && rect.x >= 0f && rect.xMax <= width + 1f && rect.y >= 0f && rect.yMax <= height + 1f, "The real runway map quote escaped its viewport.");
            File.AppendAllText("Logs/runway-build-review.txt", $"Preview{width}x{height}: length={r.Length},worldY={r.Airport.transform.position.y+r.start.y}/{r.Airport.transform.position.y+r.end.y},price={tool.RunwayPrice},grading={tool.RunwayGradingPrice},volume={tool.RunwayEarthwork}.\n");
        }
        static void CheckBuiltProfile()
        {
            Require(Mathf.Abs(game.Airport.transform.position.y - 184f) < .04f, "The first airport did not adopt the upper plateau as its datum.");
            Require(Mathf.Abs(built.start.y + 84f) < .04f && Mathf.Abs(built.end.y) < .04f && Mathf.Abs(built.Grade - .12f) < .0001f,
                "The committed profile was flattened or lost its signed local threshold elevations.");
            Require(built.naturalEdges && (built.VergeMesh == null || built.VergeMesh.vertexCount == 0), "The new runway still builds the broad grass-strip meshes.");
            Require(game.Airport.groundSize == 0f, "A floating flat airport ground pad remains beneath the natural runway.");
            bool changed = !originalHeights.SequenceEqual(game.State.world.heights);
            Require(changed, "The crooked ground was not graded as part of the paid placement.");
            for (int i = 0; i <= 20; i++)
            foreach (float across in new[] { -8f, 0f, 8f })
            {
                var p = Vector2.Lerp(Start, End, i / 20f) + Vector2.up * across;
                float pavement = game.Airport.transform.position.y + built.ElevationAt(p);
                Require(Mathf.Abs(game.valley.DisplayHeight(p) - pavement) < .035f, "Pavement is floating or buried above the paid terrain profile at " + p);
            }
        }
        static void VerifySave()
        {
            var catalog = GameAssetCatalog.Load();
            game.State.airport = AirportSaveData.Capture(game.Airport, catalog);
            var store = new CareerSaveStore(Path.GetFullPath(SaveFolder));
            Require(store.Save(game.State, out string why), "Could not save the actual UI runway: " + why);
            Require(store.Load(out var loaded, out why), "Could not load the actual UI runway: " + why);
            Require(loaded.world.heights.SequenceEqual(game.State.world.heights), "The saved sloped runway terrain changed.");
            var host = new GameObject("Restored controlled hill review airport");
            try
            {
                var airport = loaded.airport.Restore(host.transform, catalog); var runway = airport.Runways.Single();
                Require(Mathf.Abs(airport.transform.position.y - 184f) < .04f && Mathf.Abs(runway.start.y + 84f) < .04f && Mathf.Abs(runway.end.y) < .04f,
                    "Portable airport restore lost the world/local sloped threshold profile.");
                Require(runway.naturalEdges, "Portable airport restore rebuilt the grass strips.");
            }
            finally { Object.Destroy(host); }
        }
        static float U(float value) => DevGui.U(value);
        static void Go(int stage, float settle = .7f)
        {
            SessionState.SetInt(Key + "Stage", stage); next = EditorApplication.timeSinceStartup + settle;
            File.AppendAllText("Logs/runway-build-review.txt", "Stage " + stage + "\n");
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
            File.AppendAllText("Logs/runway-build-review.txt", $"Click GUI {guiPoint}, target {width}x{height}, editor Screen {Screen.width}x{Screen.height}\n");
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
            var size = Activator.CreateInstance(sizeType, Instance, null, new[] { fixedKind, (object)wantedWidth, wantedHeight, "Runway build review temporary" }, null);
            int index = (int)group.GetType().GetMethod("GetTotalCount", Instance).Invoke(group, null);
            group.GetType().GetMethod("AddCustomSize", Instance).Invoke(group, new[] { size }); SessionState.SetInt(Key + "CustomSize", index);
            // Each GameView renders OnGUI against its own target size. Keep every local view at the review size so
            // a background GameView cannot replace RegionBrowser's last production hit rectangles with stale ones.
            foreach (var view in GameViews())
            { view.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(view, index); view.Repaint(); }
            // EditorApplication.update runs outside GameView's temporary aspect scope. Keep this
            // isolated review camera explicit so projected clicks match the fixed render target.
            if (game != null && game.view != null) game.view.GetComponent<Camera>().aspect = width / (float)height;
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
                File.AppendAllText("Logs/runway-build-review.txt", $"{name}: divider top={topDivider:0.000}, bottom={bottomDivider:0.000}\n");
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
                shots.Add(new Shot { name = name, width = target.width, height = target.height, price = tool.RunwayPrice, gradingPrice = tool.RunwayGradingPrice, slope = (tool.PlacingRunway != null ? tool.PlacingRunway : built)?.Grade ?? 0f });
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
            Directory.CreateDirectory("Logs"); File.AppendAllText("Logs/runway-build-review.txt", "Failed: " + exception + "\n");
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
                File.AppendAllText("Logs/runway-build-review.txt", "Cleanup failed: " + cleanup + "\n");
            }
            finally
            {
                Application.runInBackground = SessionState.GetBool(Key + "Background", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "Paused", false); DevGui.Visible = SessionState.GetBool(Key + "HudVisible", true);
                if (exit) EditorApplication.Exit(success ? 0 : 1);
                else Debug.Log("RunwayBuildReview: " + (success ? "passed" : "failed") + "; see Logs/runway-build-review.txt");
            }
        }
    }
}
