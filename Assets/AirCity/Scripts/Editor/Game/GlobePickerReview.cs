using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
    /// <summary>Real GameView/IMGUI globe interactions, a cold online terrain request, offline reuse and save preservation.
    /// Run in an isolated graphics-enabled editor; the network request deliberately uses a non-curated French valley.</summary>
    [InitializeOnLoad]
    public static class GlobePickerReview
    {
        const string Key = "AirCity.GlobePickerReview.";
        const string Folder = "Captures/NewGame/GlobePicker/";
        const string SaveFolder = "Logs/GlobePickerReview";
        const string Log = "Logs/globe-picker-review.txt";
        const double Latitude = 47.30, Longitude = .73;
        const float Extent = 8000f;
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        static CareerGame game;
        static CareerHud hud;
        static RegionBrowser browser;
        static EditorWindow window;
        static Mouse mouse;
        static Vector2 pointer, pickedCoordinate;
        static bool pressed, releasePending;
        static double next, releaseAt, actionDeadline, downloadDeadline;
        static int width, height;
        static float beforeLatitude, beforeLongitude;
        static object originalState;
        static CancellationTokenSource cancellation;
        static Task<RegionTerrainData> loading;
        static RegionTerrainData downloaded;
        static ProgressLog networkProgress, cachedProgress;
        static Result result;
        static readonly List<Shot> shots = new List<Shot>();
        [Serializable] sealed class Shot { public string name; public int width, height; public bool browserOpen; }
        [Serializable] sealed class Result
        {
            public string result, unityVersion, utc, elevationSha256, coverSha256, landscapeSource;
            public double latitude, longitude, networkSeconds;
            public int waterSamples, treeCount, terrainResolution;
            public bool sourceHasOcean, worldHasOcean, worldHasInlandWater, cacheVerified, saveVerified;
            public List<string> downloadProgress;
            public List<Shot> screenshots;
        }
        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class Setup { public SavedScene[] scenes; }
        [Serializable] sealed class SavedGameView { public string id; public int size; }
        [Serializable] sealed class GameViewSetup { public SavedGameView[] views; }
        sealed class ProgressLog : IProgress<RegionTerrainProgress>
        {
            public readonly List<string> messages = new List<string>();
            public float lastFraction;
            public void Report(RegionTerrainProgress value)
            {
                Require(!float.IsNaN(value.fraction) && value.fraction >= 0f && value.fraction <= 1f, "Invalid download progress.");
                lastFraction = value.fraction;
                string line = value.fraction.ToString("0.000", CultureInfo.InvariantCulture) + " " + value.message;
                if (messages.Count > 0 && messages[messages.Count - 1] == line) return;
                messages.Add(line); File.AppendAllText(Log, "Terrain: " + line + "\n");
            }
        }

        static GlobePickerReview()
        { if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update; }
        [MenuItem("AirCity/Game/Review Globe Picker (Online)")]
        public static void Run() => Begin(false);
        public static void RunFromCommandLine() => Begin(true);

        static void Begin(bool exitEditor)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before running the globe review.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save modified scenes before running the globe review.");
                Directory.CreateDirectory(Folder); Directory.CreateDirectory(SaveFolder);
                foreach (string file in Directory.GetFiles(Folder, "*.png")) File.Delete(file);
                if (File.Exists("Logs/globe-picker-review.json")) File.Delete("Logs/globe-picker-review.json");
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
                SessionState.SetString(Key + "GameViews", "");
                SessionState.SetString(Key + "CacheBackup", ""); SessionState.SetString(Key + "CachePath", "");
                SessionState.SetBool(Key + "FlipKnown", false);
                File.WriteAllText(Log, "Running real globe UI, cold Loire Valley terrain download, cache and saved-world checks.\n");
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                SessionState.SetBool(Key + "Running", true); SessionState.SetInt(Key + "Stage", 0);
                SessionState.SetFloat(Key + "Started", (float)EditorApplication.timeSinceStartup);
                SessionState.SetString(Key + "Failure", "");
                EditorApplication.isPaused = false; Application.runInBackground = true;
                EditorApplication.update -= Update; EditorApplication.update += Update; EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                if (SessionState.GetBool(Key + "Running", false)) Fail(e);
                else
                {
                    Debug.LogException(e); Directory.CreateDirectory("Logs"); File.WriteAllText(Log, "Failed to start: " + e + "\n");
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
                Require(EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0f) < 600f,
                    "Globe review timed out. Use a graphics-enabled non-batch editor with internet access.");
                if (!EditorApplication.isPlaying) return;
                EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
                if (window != null) window.Repaint();
                if (releasePending && EditorApplication.timeSinceStartup >= releaseAt)
                { SendMouse(EventType.MouseUp); pressed = releasePending = false; }
                if (EditorApplication.timeSinceStartup < next) return;
                if (stage == 0) { if (Initialize()) Go(1, 3f); return; }
                switch (stage)
                {
                    case 1:
                        // The unobscured HUD calibrates the Metal GameView readback orientation for later modal shots.
                        if (!Capture("00-initial-region-1440")) return;
                        browser.Open(); Go(2); break;
                    case 2:
                        if (!Capture("01-globe-1440")) return;
                        Click(CuratedPoint(2)); Go(3); break;
                    case 3:
                        if (!Await(Get<int>("curated") == 2, "The desktop Bora Bora card did not select its actual location.")) return;
                        if (!Capture("02-bora-bora-selected-1440")) return;
                        originalState = game.State; Click(ActionPoint()); Go(4, 1.5f); break;
                    case 4:
                        if (!Await(!ReferenceEquals(originalState, game.State) && !browser.IsOpen, "The desktop offline Preview button did not replace the preview.")) return;
                        Require(game.State.world.preset == RegionPreset.Tropical, "The actual Bora Bora Preview action generated another biome.");
                        if (!Capture("03-bora-bora-region-preview")) return;
                        browser.Open(); Resize(680, 650); Go(5, 1f); break;
                    case 5:
                        RequireBrowserFits();
                        if (!Capture("04-globe-680")) return;
                        Click(CuratedPoint(1)); Go(6); break;
                    case 6:
                        if (!Await(Get<int>("curated") == 1, "The narrow Yosemite card did not select its actual location.")) return;
                        originalState = game.State; Click(ActionPoint()); Go(7, 1.5f); break;
                    case 7:
                        if (!Await(!ReferenceEquals(originalState, game.State) && !browser.IsOpen, "The narrow offline Preview button did not replace the preview.")) return;
                        Require(game.State.world.preset == RegionPreset.Mountains, "The narrow Yosemite Preview action generated another biome.");
                        browser.Open(); Resize(1440, 900); Go(8, 1f); break;
                    case 8:
                        pointer = GlobePoint(new Vector2(.38f, .22f));
                        actionDeadline = EditorApplication.timeSinceStartup + 8f;
                        SendMouse(EventType.ScrollWheel, new Vector2(0f, -2f)); Go(9); break;
                    case 9:
                        if (!Await(Get<float>("zoom") > 1.2f, "Actual globe scroll did not zoom.")) return;
                        beforeLatitude = Get<float>("viewLatitude"); beforeLongitude = Get<float>("viewLongitude");
                        pointer = GlobePoint(new Vector2(.4f, .2f)); pressed = true;
                        SendMouse(EventType.MouseDown); Go(10, .25f); break;
                    case 10:
                        pointer += new Vector2(48f, 27f); SendMouse(EventType.MouseDrag, new Vector2(48f, 27f)); Go(11, .25f); break;
                    case 11:
                        SendMouse(EventType.MouseUp); pressed = false;
                        Require(Mathf.Abs(Get<float>("viewLongitude") - beforeLongitude) > .5f &&
                            Mathf.Abs(Get<float>("viewLatitude") - beforeLatitude) > .5f, "Actual globe drag did not rotate both axes.");
                        var unit = new Vector2(-.28f, .19f);
                        Require(GlobeProjection.TryPick(unit, Get<float>("viewLatitude"), Get<float>("viewLongitude"), out pickedCoordinate), "The planned globe pick was outside Earth.");
                        Click(GlobePoint(unit)); Go(12); break;
                    case 12:
                        if (!Await(Get<int>("curated") == -1 && Math.Abs(Get<double>("latitude") - pickedCoordinate.x) < .001 &&
                            Math.Abs(Get<double>("longitude") - pickedCoordinate.y) < .001, "Actual globe click did not pick its projected coordinates.")) return;
                        if (!Capture("05-globe-rotated-zoomed-picked")) return;
                        PrepareFrance(); Go(13); break;
                    case 13:
                        if (!Capture("06-loire-valley-selection")) return;
                        StartColdDownload(); Go(14, .1f); break;
                    case 14:
                        if (!AwaitDownload()) return;
                        downloaded = loading.GetAwaiter().GetResult();
                        ValidateDownloaded(downloaded);
                        result.networkSeconds = 300f - Math.Max(0d, downloadDeadline - EditorApplication.timeSinceStartup);
                        Require(networkProgress.lastFraction == 1f && networkProgress.messages.Any(s => s.Contains("Terrain ready")) &&
                            !networkProgress.messages.Any(s => s.Contains("Loaded cached terrain")), "The first request did not complete a real cold download.");
                        cachedProgress = new ProgressLog();
                        loading = LocationTerrainLoader.LoadAsync(Latitude, Longitude, Extent, cancellation.Token, cachedProgress);
                        downloadDeadline = EditorApplication.timeSinceStartup + 10f; Go(15, .1f); break;
                    case 15:
                        if (!AwaitDownload()) return;
                        var cached = loading.GetAwaiter().GetResult();
                        Require(cachedProgress.messages.Any(s => s.Contains("Loaded cached terrain")), "Second terrain load did not use the offline cache.");
                        Require(cached.elevationSha256 == downloaded.elevationSha256 && cached.coverSha256 == downloaded.coverSha256 &&
                            cached.elevations.SequenceEqual(downloaded.elevations) && cached.cover.SequenceEqual(downloaded.cover), "Cached terrain changed downloaded samples.");
                        result.cacheVerified = true; VerifySavedWorld(downloaded);
                        // Prepare cache status through the same method used after normal coordinate field editing.
                        typeof(RegionBrowser).GetMethod("CheckCache", Instance).Invoke(browser, null);
                        Require(Get<bool>("cached"), "The globe did not recognize the downloaded custom region as cached.");
                        originalState = game.State; Click(ActionPoint()); Go(16, 2f); break;
                    case 16:
                        if (!Await(!ReferenceEquals(originalState, game.State) && !browser.IsOpen,
                            "The actual custom Preview button did not open the downloaded region. " + Get<string>("error"))) return;
                        RequireCustomWorld(game.State.world); game.Paused = true; game.view.Region(); Go(17, 2f); break;
                    case 17:
                        if (!Capture("07-loire-region-preview-1440")) return;
                        FocusInlandBank(); Go(18, 1.5f); break;
                    case 18:
                        if (!Capture("08-loire-inland-river-bank")) return;
                        browser.Open(); Resize(680, 650); Go(19, 1f); break;
                    case 19:
                        RequireBrowserFits(); Require(Get<bool>("cached"), "Reopening a custom location lost its cached availability.");
                        if (!Capture("09-loire-cached-globe-680")) return;
                        originalState = game.State; Click(ActionPoint()); Go(20, 2f); break;
                    case 20:
                        if (!Await(!ReferenceEquals(originalState, game.State) && !browser.IsOpen,
                            "The narrow custom Preview button did not open its cached region. " + Get<string>("error"))) return;
                        RequireCustomWorld(game.State.world);
                        if (!Capture("10-loire-region-preview-680")) return;
                        Complete(); break;
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
            game.Initialize(CareerState.New(19770616, preset: RegionPreset.Woodland), false); game.Paused = true; game.view.Region();
            browser = (RegionBrowser)typeof(CareerHud).GetField("regionBrowser", Instance).GetValue(hud);
            if (browser == null)
            {
                browser = new RegionBrowser(game);
                typeof(CareerHud).GetField("regionBrowser", Instance).SetValue(hud, browser);
            }
            browser.Close(); DevGui.Visible = true; shots.Clear();
            result = new Result { unityVersion = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"), latitude = Latitude, longitude = Longitude };
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("Globe picker review mouse"); SessionState.SetInt(Key + "Mouse", mouse.deviceId);
            InputSystem.onBeforeUpdate -= FeedInput; InputSystem.onBeforeUpdate += FeedInput;
            SessionState.SetString(Key + "GameViews", JsonUtility.ToJson(new GameViewSetup { views = GameViews().Select(v =>
                new SavedGameView { id = v.GetEntityId().ToString(), size = (int)v.GetType().GetProperty("selectedSizeIndex", Instance).GetValue(v) }).ToArray() }));
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
            window = (EditorWindow)ScriptableObject.CreateInstance(type); window.titleContent = new GUIContent("Globe picker review"); window.Show();
            SessionState.SetString(Key + "Window", window.GetEntityId().ToString());
            Resize(1440, 900); pointer = new Vector2(0f, height); return true;
        }
        static T Get<T>(string field) => (T)typeof(RegionBrowser).GetField(field, Instance).GetValue(browser);
        static void Set(string field, object value) => typeof(RegionBrowser).GetField(field, Instance).SetValue(browser, value);
        static float U(float value) => DevGui.U(value);
        static Rect GlobeRect => Get<Rect>("globeRect");
        static Rect SideRect => Get<Rect>("sideRect");
        static Vector2 CuratedPoint(int index)
        {
            var r = SideRect; bool narrow = width < U(850);
            float cardWidth = narrow ? (r.width - U(8)) * .5f : r.width;
            float cardHeight = U(narrow ? 34 : 52);
            return new Vector2(r.x + (narrow ? index % 2 : 0) * (cardWidth + U(8)) + cardWidth * .5f,
                r.y + U(24) + (narrow ? index / 2 : index) * (cardHeight + U(6)) + cardHeight * .5f);
        }
        static Vector2 ActionPoint() => new Vector2(SideRect.center.x, SideRect.yMax - U(21));
        static Vector2 GlobePoint(Vector2 sphere) => GlobeRect.center + new Vector2(sphere.x * GlobeRect.width, -sphere.y * GlobeRect.height) * (.5f * Get<float>("zoom"));
        static void RequireBrowserFits()
        {
            var r = SideRect; var g = GlobeRect;
            Require(r.width > 100f && g.width > 100f && r.xMin >= 0f && r.xMax <= width && r.yMax <= height && g.yMin >= 0f && g.xMin >= 0f &&
                g.xMax <= width && g.yMax <= r.yMin, "The actual narrow globe or action panel extends outside the GameView.");
            float cardsEnd = r.y + U(24 + 2 * (34 + 6) + 2 + 22 + 18 + 30);
            Require(cardsEnd <= r.yMax - U(38), "Narrow coordinate fields overlap the Preview action.");
        }
        static void PrepareFrance()
        {
            Set("curated", -1); Set("latitude", Latitude); Set("longitude", Longitude);
            Set("latitudeText", Latitude.ToString("0.0000", CultureInfo.InvariantCulture));
            Set("longitudeText", Longitude.ToString("0.0000", CultureInfo.InvariantCulture));
            Set("viewLatitude", (float)Latitude); Set("viewLongitude", (float)Longitude); Set("zoom", 2.6f);
            typeof(RegionBrowser).GetMethod("CheckCache", Instance).Invoke(browser, null);
        }
        static void StartColdDownload()
        {
            string path = (string)typeof(LocationTerrainLoader).GetMethod("CachePath", Static).Invoke(null, new object[] { Latitude, Longitude, Extent });
            SessionState.SetString(Key + "CachePath", path);
            if (File.Exists(path))
            {
                string backup = Path.GetFullPath(Path.Combine(SaveFolder, "prior-loire-cache-" + Guid.NewGuid().ToString("N") + ".terrain"));
                File.Move(path, backup); SessionState.SetString(Key + "CacheBackup", backup);
                File.AppendAllText(Log, "Temporarily preserved an existing location cache for a true cold request.\n");
            }
            Require(!LocationTerrainLoader.HasCached(Latitude, Longitude, Extent), "Cold download still had an existing result cache.");
            cancellation = new CancellationTokenSource(); networkProgress = new ProgressLog();
            downloadDeadline = EditorApplication.timeSinceStartup + 300f;
            loading = LocationTerrainLoader.LoadAsync(Latitude, Longitude, Extent, cancellation.Token, networkProgress);
        }
        static bool AwaitDownload()
        {
            Require(loading != null, "The online task was lost.");
            if (loading.IsCompleted) { loading.GetAwaiter().GetResult(); return true; }
            Require(EditorApplication.timeSinceStartup < downloadDeadline, "Terrain download/cache check exceeded its timeout.");
            next = EditorApplication.timeSinceStartup + .1f; return false;
        }
        static void ValidateDownloaded(RegionTerrainData data)
        {
            Require(data != null && data.latitude == Latitude && data.longitude == Longitude && data.extent == Extent, "Downloaded metadata describes a different location.");
            Require(data.elevations?.Length == RegionTerrainData.Resolution * RegionTerrainData.Resolution && data.cover?.Length == data.elevations.Length,
                "Download did not produce the full geographic sample grid.");
            Require(data.elevations.All(h => !float.IsNaN(h) && !float.IsInfinity(h)), "Downloaded elevations contain invalid samples.");
            Require(data.elevationSha256?.Length == 64 && data.coverSha256?.Length == 64, "Downloaded source hashes are missing.");
            Require(!data.hasOcean, "The inland Loire Valley crop was incorrectly classified as an ocean.");
            Require(data.elevations.Max() - data.elevations.Min() > 2f, "Real elevation data was unexpectedly flat.");
            result.sourceHasOcean = data.hasOcean; result.elevationSha256 = data.elevationSha256; result.coverSha256 = data.coverSha256;
            result.downloadProgress = networkProgress.messages;
            File.AppendAllText(Log, $"Downloaded source range {data.elevations.Min():0.00}–{data.elevations.Max():0.00}m, water cover samples={data.cover.Count(c => c == 80)}, ocean={data.hasOcean}.\n");
        }
        static void VerifySavedWorld(RegionTerrainData data)
        {
            var state = CareerState.NewLocation(data); RequireCustomWorld(state.world);
            Require(state.Validate(out string problem), "Generated custom state failed validation: " + problem);
            var store = new CareerSaveStore(Path.GetFullPath(Path.Combine(SaveFolder, "DownloadedWorld")));
            Require(store.Save(state, out problem), "Could not save the downloaded world: " + problem);
            Require(store.Load(out var loaded, out problem), "Could not load the downloaded world: " + problem);
            RequireCustomWorld(loaded.world);
            Require(loaded.world.heights.SequenceEqual(state.world.heights) && loaded.world.waterHeights.SequenceEqual(state.world.waterHeights) &&
                loaded.world.landCover.SequenceEqual(state.world.landCover) && loaded.world.roadHeights.SequenceEqual(state.world.roadHeights),
                "Saved custom world changed its elevation, water, landcover or graded road samples.");
            Require(loaded.world.elevationScale == state.world.elevationScale && loaded.world.elevationOffset == state.world.elevationOffset &&
                loaded.world.landscapeSource == state.world.landscapeSource && loaded.world.generatorVersion == state.world.generatorVersion,
                "Saved custom world lost its geographic/source transformation metadata.");
            result.saveVerified = true; result.worldHasOcean = state.world.HasOcean; result.worldHasInlandWater = state.world.HasInlandWater;
            result.waterSamples = state.world.waterHeights.Count(h => h > 0f); result.treeCount = state.world.trees.Count; result.landscapeSource = state.world.landscapeSource;
            File.AppendAllText(Log, $"Generated and saved custom world: ocean={state.world.HasOcean}, inland={state.world.HasInlandWater}, wet samples={result.waterSamples}, source={state.world.landscapeSource}.\n");
        }
        static void RequireCustomWorld(ValleyWorld world)
        {
            Require(world.hasCoordinates && Math.Abs(world.latitude - Latitude) < 1e-8 && Math.Abs(world.longitude - Longitude) < 1e-8 &&
                world.sourceExtent == Extent, "Custom preview lost the chosen geographic location.");
            Require(!world.HasOcean && world.HasInlandWater && world.waterHeights.Any(h => h > 0f),
                "The inland custom location did not preserve its mapped river water independently of the ocean.");
        }
        static void FocusInlandBank()
        {
            var world = game.State.world; int n = ValleyWorld.Resolution, at = -1; float best = float.MinValue;
            for (int z = 20; z < n - 20; z++)
            for (int x = 20; x < n - 20; x++)
            {
                int i = z * n + x; if (world.waterHeights[i] <= 0f) continue;
                int wet = 0;
                for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++) if (world.waterHeights[i + dz * n + dx] > 0f) wet++;
                if (wet < 3 || wet > 42) continue;
                float score = wet - new Vector2(x - n * .5f, z - n * .5f).sqrMagnitude * .00005f;
                if (score <= best) continue; best = score; at = i;
            }
            Require(at >= 0, "Downloaded river has no interior bank to inspect.");
            var p = new Vector2((at % n) * world.Spacing - ValleyWorld.Size * .5f, (at / n) * world.Spacing - ValleyWorld.Size * .5f);
            game.view.FocusPoint(new Vector3(p.x, world.InlandWaterHeight(p) + 2f, p.y), 400f, 48f, -35f);
            result.terrainResolution = game.valley.Terrain.terrainData.heightmapResolution;
            File.AppendAllText(Log, $"Inland bank view {p}, water={world.InlandWaterHeight(p):0.00}m, terrain resolution={result.terrainResolution}.\n");
        }
        static void Complete()
        {
            result.result = "Passed: actual 1440/680 globe UI, offline curated previews, scroll/drag/pick, cold geographic terrain download, offline cache, custom previews and saved terrain roundtrip.";
            result.screenshots = shots;
            File.WriteAllText("Logs/globe-picker-review.json", JsonUtility.ToJson(result, true));
            File.AppendAllText(Log, result.result + "\n");
            SessionState.SetInt(Key + "Stage", 100); RestoreInput(); EditorApplication.ExitPlaymode();
        }
        static void Go(int stage, float settle = .7f)
        {
            SessionState.SetInt(Key + "Stage", stage); next = EditorApplication.timeSinceStartup + settle;
            File.AppendAllText(Log, "Stage " + stage + "\n");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static bool Await(bool condition, string message)
        {
            if (condition) return true;
            Require(EditorApplication.timeSinceStartup < actionDeadline, message); next = EditorApplication.timeSinceStartup + .1f; return false;
        }
        static void Click(Vector2 guiPoint)
        {
            pointer = guiPoint; pressed = true; releasePending = true; releaseAt = EditorApplication.timeSinceStartup + .15f;
            actionDeadline = EditorApplication.timeSinceStartup + 20f;
            File.AppendAllText(Log, $"Click GUI {guiPoint}, target {width}x{height}, editor Screen {Screen.width}x{Screen.height}\n");
            window.Focus(); SendMouse(EventType.MouseDown);
        }
        static void SendMouse(EventType type, Vector2 delta = default)
        {
            if (window == null) return;
            var e = new Event { type = type, button = 0, mousePosition = pointer, displayIndex = 0, delta = delta };
            var queue = typeof(EditorGUIUtility).GetMethod("QueueGameViewInputEvent", Static, null, new[] { typeof(Event) }, null);
            if (queue != null) queue.Invoke(null, new object[] { e });
            else
            {
                var offsetProperty = window.GetType().GetProperty("gameMouseOffset", Instance);
                var scaleProperty = window.GetType().GetProperty("gameMouseScale", Instance);
                var offset = offsetProperty != null ? (Vector2)offsetProperty.GetValue(window) : new Vector2(0f, -21f);
                float scale = scaleProperty != null ? (float)scaleProperty.GetValue(window) : 1f;
                e.mousePosition = pointer / scale - offset; window.SendEvent(e);
            }
            window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        }
        static void FeedInput()
        {
            if (mouse == null || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(pointer.x, height - pointer.y) }.WithButton(MouseButton.Left, pressed));
        }
        static object SizeGroup()
        {
            var assembly = typeof(Editor).Assembly; var type = assembly.GetType("UnityEditor.GameViewSizes", true);
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
            var size = Activator.CreateInstance(sizeType, Instance, null, new[] { fixedKind, (object)wantedWidth, wantedHeight, "Globe picker review temporary" }, null);
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
            var previous = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                var pixels = texture.GetPixels32();
                if (!SessionState.GetBool(Key + "FlipKnown", false))
                {
                    float Coverage(bool top)
                    {
                        int boundary = top ? texture.height - Mathf.RoundToInt(U(40)) : Mathf.RoundToInt(U(40)) - 1, best = 0;
                        for (int d = -4; d <= 4; d++)
                        {
                            int y = Mathf.Clamp(boundary + d, 0, texture.height - 1), gold = 0;
                            for (int x = 0; x < texture.width; x++)
                            { var p = pixels[y * texture.width + x]; if (p.r > 180 && p.g > 80 && p.b < 130 && p.r > p.g + 20 && p.g > p.b + 20) gold++; }
                            best = Mathf.Max(best, gold);
                        }
                        return best / (float)texture.width;
                    }
                    float top = Coverage(true), bottom = Coverage(false);
                    Require(Mathf.Max(top, bottom) > .8f, "GameView readback did not contain the real HUD divider needed to calibrate orientation.");
                    SessionState.SetBool(Key + "Flip", bottom > top); SessionState.SetBool(Key + "FlipKnown", true);
                }
                if (SessionState.GetBool(Key + "Flip", false))
                {
                    var flipped = new Color32[pixels.Length];
                    for (int row = 0; row < texture.height; row++) Array.Copy(pixels, row * texture.width, flipped, (texture.height - 1 - row) * texture.width, texture.width);
                    texture.SetPixels32(flipped); texture.Apply();
                }
                Require(pixels.Count(p => p.r > 200 && p.b > 200 && p.g < 60) < pixels.Length / 40, "GameView contains a missing/magenta shader.");
                File.WriteAllBytes(Folder + name + ".png", texture.EncodeToPNG());
                shots.Add(new Shot { name = name, width = target.width, height = target.height, browserOpen = browser.IsOpen });
                File.AppendAllText(Log, $"Captured {name} {target.width}x{target.height}, browser={browser.IsOpen}.\n");
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
            cancellation?.Cancel(); Debug.LogException(exception); Directory.CreateDirectory("Logs"); File.AppendAllText(Log, "Failed: " + exception + "\n");
            SessionState.SetString(Key + "Failure", exception.ToString()); SessionState.SetInt(Key + "Stage", 101); RestoreInput();
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else Finish(false);
        }
        static void Finish(bool success)
        {
            bool exit = SessionState.GetBool(Key + "Exit", false);
            try
            {
                cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; RestoreInput();
                EditorApplication.update -= Update; SessionState.SetBool(Key + "Running", false);
                string backup = SessionState.GetString(Key + "CacheBackup", ""), cachePath = SessionState.GetString(Key + "CachePath", "");
                if (!string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    if (File.Exists(cachePath))
                    { File.Copy(cachePath, Path.Combine(SaveFolder, "downloaded-loire.terrain"), true); File.Delete(cachePath); }
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)); File.Move(backup, cachePath);
                    SessionState.SetString(Key + "CacheBackup", ""); File.AppendAllText(Log, "Restored the pre-existing terrain cache.\n");
                }
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
                    EditorSceneManager.RestoreSceneManagerSetup(scenes.Select(s => new SceneSetup { path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
                }
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception cleanup)
            { success = false; Debug.LogException(cleanup); File.AppendAllText(Log, "Cleanup failed: " + cleanup + "\n"); }
            finally
            {
                Application.runInBackground = SessionState.GetBool(Key + "Background", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "Paused", false); DevGui.Visible = SessionState.GetBool(Key + "HudVisible", true);
                if (exit) EditorApplication.Exit(success ? 0 : 1);
                else Debug.Log("GlobePickerReview: " + (success ? "passed" : "failed") + "; see " + Log);
            }
        }
    }
}
