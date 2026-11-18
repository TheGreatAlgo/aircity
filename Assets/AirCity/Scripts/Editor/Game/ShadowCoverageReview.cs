using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>
    /// Sun-shadow coverage review on the real New Game scene and its CareerCamera, in edit mode (no Play mode).
    /// One scene, a fixed set of camera views at increasing zoom: a bridge over a river, a bridge over land, a
    /// building, a parked aircraft and forest all near one pivot. For each view it saves a capture, measures how far
    /// the sun's shadows reach (each pixel is compared with the same frame rendered without sun shadows) and times
    /// frames with and without sun shadows, so the difference is the shadow cost on this machine.
    ///
    /// Unity -batchmode -projectPath &lt;clone&gt; -executeMethod AirCity.EditorTools.Game.ShadowCoverageReview.RunFromCommandLine
    ///       [-shadowReviewLabel before] [-shadowReviewFrames 24]
    /// Writes Captures/NewGame/Shadows/&lt;label&gt;/*.png and Logs/shadow-review-&lt;label&gt;.json.
    /// </summary>
    public static class ShadowCoverageReview
    {
        public const int Width = 1920, Height = 1080;
        public const float Ground = 100f, BridgeLevel = 110f;
        // Around a pivot on the east bank of the fixture's river (x = -1500, water at 99 m, land at 100 m):
        // a bridge across the river, a bridge over land, a three-storey building, a parked aircraft, forest beyond.
        // The views look east-north-east, so the sun (from the south-east) throws shadows to the left of their casters.
        public static readonly Vector3 Pivot = new Vector3(-1430f, Ground, 700f);
        public static readonly Vector2 WaterBridgeA = new Vector2(-1585f, 700f), WaterBridgeB = new Vector2(-1420f, 700f);
        public static readonly Vector2 LandBridgeA = new Vector2(-1385f, 715f), LandBridgeB = new Vector2(-1385f, 800f);
        public static readonly Vector2[] TerminalCorners =
            { new Vector2(-1405f, 630f), new Vector2(-1365f, 630f), new Vector2(-1365f, 668f), new Vector2(-1405f, 668f) };
        public static readonly Vector3 AircraftAt = new Vector3(-1345f, Ground, 690f);
        /// <summary>Kept clear of trees so the structures stay in view close up.</summary>
        static readonly Rect Clearing = Rect.MinMaxRect(-1425f, 615f, -1300f, 820f);

        /// <summary>Close-up pivot beside the land bridge, on the shadow side, for comparison with the owner's photo 4.</summary>
        public static readonly Vector3 CloseUpPivot = new Vector3(-1388f, Ground, 760f);
        [Serializable] public struct View { public string name; public float distance, pitch, yaw; public bool closeUp; }
        public static readonly View[] Views =
        {
            new View { name = "d0045-p22-closeup", distance = 45f, pitch = 22f, yaw = 90f, closeUp = true },
            new View { name = "d0400-p18", distance = 400f, pitch = 18f, yaw = 60f },
            new View { name = "d0150-p55", distance = 150f, pitch = 55f, yaw = 60f },
            new View { name = "d0600-p55", distance = 600f, pitch = 55f, yaw = 60f },
            new View { name = "d1500-p55", distance = 1500f, pitch = 55f, yaw = 60f },
            new View { name = "d4000-p55", distance = 4000f, pitch = 55f, yaw = 60f },
            new View { name = "d7500-p55", distance = 7500f, pitch = 55f, yaw = 60f },
            new View { name = "d1500-p30", distance = 1500f, pitch = 30f, yaw = 60f },
        };

        [Serializable] sealed class Review
        {
            public string label, graphicsDevice, graphicsApi, pipeline, pipelineAtStart, pipelineAtEnd;
            public int trees, forestTrees, frames;
            public bool terminalBuilt, aircraftPlaced;
            /// <summary>Shadow map size forced through ShadowRange.mapResolution (-shadowReviewMap), 0 = default.</summary>
            public int mapResolution;
            public List<Sweep> sweeps = new List<Sweep>();
            public List<Shot> shots = new List<Shot>();
        }
        /// <summary>The camera moved through many frames (pan, zoom, tilt) as the player moves it: how many times the
        /// shadow layout (range or splits) changed, and how many of those went back to the layout of two changes
        /// before (flicker).</summary>
        [Serializable] sealed class Sweep
        {
            public string name;
            public int frames, layoutChanges, flips;
            public float minRange, maxRange;
        }
        [Serializable] sealed class Shot
        {
            public string name, file;
            public float distance, pitch, yaw;
            public Vector3 cameraPosition;
            public float nearClip, farClip;
            /// <summary>Active pipeline's shadow settings while this view rendered.</summary>
            public float shadowDistance, cascadeBorder; public Vector3 cascadeSplits; public int cascades, shadowmapResolution;
            /// <summary>The pipeline asset's shadow settings after the frame: must equal what is on disk.</summary>
            public string pipelineAfterRender;
            /// <summary>ShadowRange's layout for the camera, when it follows the camera.</summary>
            public string shadowRange;
            /// <summary>Flat-ground (y = 100) distances from the camera: bottom-centre and top-centre of the screen (-1 = sky).</summary>
            public float groundBottom, groundTop;
            public float shadowedFraction;
            /// <summary>How dark sun shadow is on water and on land: median brightness of shadowed pixels over the same
            /// pixels without sun shadows (water = teal pixels, blue well above red), and how many pixels each.</summary>
            public float shadowDepthWater = -1f, shadowDepthLand = -1f; public int shadowedWaterPixels, shadowedLandPixels;
            /// <summary>Forest submissions for this view: trees drawn, and draw calls.</summary>
            public int treesDrawn, treeDrawCalls;
            /// <summary>Flat-ground distance of shadowed pixels: 50th, 99th percentile and maximum.</summary>
            public float shadowReachMedian, shadowReach99, shadowReachMax;
            /// <summary>Highest screen row (0 bottom .. 1 top) where at least 0.2% of the row is in sun shadow.</summary>
            public float shadowTopRow;
            /// <summary>Shadowed fraction of ground pixels in ten equal bands of flat-ground distance, bottom to top of screen.</summary>
            public float[] bandShadowed = new float[10];
            public float bandFrom, bandTo;
            public double msShadowsOn, msShadowsOff, msShadowCost;
            /// <summary>Main-thread time of Camera.Render alone (CPU submission), sun shadows on / off.</summary>
            public double msCpuOn, msCpuOff;
        }

        public static void RunFromCommandLine()
        {
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                string label = Argument("-shadowReviewLabel") ?? "before";
                if (label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid review label.");
                int frames = int.TryParse(Argument("-shadowReviewFrames"), out int f) ? Mathf.Clamp(f, 4, 200) : 40;
                if (int.TryParse(Argument("-shadowReviewMap"), out int map)) SetMapResolution(map);
                string root = "Captures/NewGame/Shadows/" + label + "/";
                Directory.CreateDirectory(root); Directory.CreateDirectory("Logs");
                var review = Run(label, root, frames);
                File.WriteAllText("Logs/shadow-review-" + label + ".json", JsonUtility.ToJson(review, true));
                Debug.Log("Shadow coverage review completed: " + Path.GetFullPath(root));
                ShaderUtil.allowAsyncCompilation = async;
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShaderUtil.allowAsyncCompilation = async;
                EditorApplication.Exit(1);
            }
        }

        /// <summary>One shadow setup to time: range on/off, shadow map size (0 = ShadowRange's own), and whether the
        /// forest's merged shadow-only meshes (trees beyond 180 m) cast.</summary>
        [Serializable] public struct Config { public string name; public bool range; public int map; public bool farTrees; }
        public static readonly Config[] CostConfigs =
        {
            new Config { name = "fixed150", range = false, map = 2048, farTrees = false },
            new Config { name = "range2048", range = true, map = 2048, farTrees = false },
            new Config { name = "range2048-trees", range = true, map = 2048, farTrees = true },
            new Config { name = "range4096", range = true, map = 4096, farTrees = false },
            new Config { name = "range4096-trees", range = true, map = 4096, farTrees = true },
        };
        static string[] CostViews = { "d0045-p22-closeup", "d0400-p18", "d0150-p55", "d0600-p55", "d1500-p55", "d4000-p55", "d7500-p55" };
        [Serializable] sealed class CostRun { public Config config; public List<Shot> shots = new List<Shot>(); }
        [Serializable] sealed class CostReview { public string graphicsDevice, graphicsApi, pipelineAtStart, pipelineAtEnd; public int trees; public List<CostRun> runs = new List<CostRun>(); }

        /// <summary>
        /// Same scene and views as <see cref="RunFromCommandLine"/>, every <see cref="CostConfigs"/> entry in one session,
        /// so frame times compare: Logs/shadow-cost.json, captures under Captures/NewGame/Shadows/cost/&lt;config&gt;/.
        /// </summary>
        public static void RunCostFromCommandLine()
        {
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                int frames = int.TryParse(Argument("-shadowReviewFrames"), out int f) ? Mathf.Clamp(f, 4, 200) : 40;
                var review = new Review();
                var (game, camera, sun) = Setup(review);
                // -shadowCostConfigs a,b,c and -shadowCostViews x,y: run a subset (names from CostConfigs / Views).
                var configs = (Argument("-shadowCostConfigs")?.Split(',') is string[] names) ? CostConfigs.Where(c => names.Contains(c.name)).ToArray() : CostConfigs;
                if (Argument("-shadowCostViews")?.Split(',') is string[] views) CostViews = views;
                camera.Render(); // the forest builds its distant shadow meshes on its first draw
                var cost = new CostReview { graphicsDevice = review.graphicsDevice, graphicsApi = review.graphicsApi, pipelineAtStart = PipelineSummary(), trees = review.trees };
                foreach (var config in configs)
                {
                    SetStatic("AirCity.Rendering.ShadowRange", "enabled", config.range);
                    SetStatic("AirCity.Rendering.ShadowRange", "mapResolution", config.map);
                    var farTrees = game.GetComponentInChildren<ForestRenderer>()?.transform.Find("Distant tree shadows");
                    if (farTrees != null) farTrees.gameObject.SetActive(config.farTrees);
                    else if (config.farTrees) Debug.LogWarning("ShadowCoverageReview: this project has no distant tree shadows.");
                    string root = "Captures/NewGame/Shadows/cost/" + config.name + "/"; Directory.CreateDirectory(root);
                    var run = new CostRun { config = config };
                    foreach (var view in Views.Where(v => CostViews.Contains(v.name))) run.shots.Add(Shoot(game, camera, sun, view, root, frames));
                    cost.runs.Add(run);
                }
                SetStatic("AirCity.Rendering.ShadowRange", "enabled", true);
                cost.pipelineAtEnd = PipelineSummary();
                if (cost.pipelineAtEnd != cost.pipelineAtStart) Debug.LogError("ShadowCoverageReview: the pipeline asset's shadow settings changed.");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/shadow-cost.json", JsonUtility.ToJson(cost, true));
                Debug.Log("Shadow cost review completed.");
                ShaderUtil.allowAsyncCompilation = async;
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShaderUtil.allowAsyncCompilation = async;
                EditorApplication.Exit(1);
            }
        }

        static Vector3? pivotOverride;
        [Serializable] sealed class RegionReview { public string pipelineAtStart, pipelineAtEnd; public List<RegionRun> regions = new List<RegionRun>(); }
        [Serializable] sealed class RegionRun { public string preset, config; public int trees, shadowTrees; public Vector3 pivot; public List<Shot> shots = new List<Shot>(); }

        /// <summary>
        /// The same measurements on generated regions (real relief, lakes, mixed forest) rather than the flat
        /// fixture: Woodland and Mountains, four zooms round a forested point. Logs/shadow-regions.json, captures under
        /// Captures/NewGame/Shadows/regions/&lt;preset&gt;-&lt;config&gt;/. The distance bands in each shot assume flat ground and mean little here.
        /// </summary>
        public static void RunRegionsFromCommandLine()
        {
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                int frames = int.TryParse(Argument("-shadowReviewFrames"), out int f) ? Mathf.Clamp(f, 4, 200) : 16;
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                var game = Object.FindAnyObjectByType<CareerGame>();
                if (game == null) throw new InvalidOperationException("New Game scene has no game host.");
                var result = new RegionReview { pipelineAtStart = PipelineSummary() };
                foreach (var (preset, seed) in new[] { (RegionPreset.Woodland, 19770616), (RegionPreset.Mountains, 873241) })
                {
                    var state = CareerState.New(seed, preset: preset);
                    game.Initialize(state, false); game.Paused = true;
                    var camera = game.view.GetComponent<Camera>();
                    var sun = RenderSettings.sun != null ? RenderSettings.sun :
                        Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).First(l => l.type == LightType.Directional);
                    // A forested point: the tree with the most trees within 150 m, out of a spread sample.
                    var trees = state.world.trees; var best = Vector2.zero; int most = -1;
                    for (int i = 0; i < 40 && trees.Count > 0; i++)
                    {
                        var candidate = trees[(int)((i + .5f) / 40f * trees.Count)].position;
                        if (Mathf.Abs(candidate.x) > 2200f || Mathf.Abs(candidate.y) > 2200f) continue;
                        int near = 0; foreach (var tree in trees) if ((tree.position - candidate).sqrMagnitude < 150f * 150f) near++;
                        if (near > most) { most = near; best = candidate; }
                    }
                    var pivot = new Vector3(best.x, game.valley.DisplayHeight(best), best.y);
                    pivotOverride = pivot;
                    camera.Render(); // the forest builds its distant shadow meshes on its first draw
                    var forest = game.GetComponentInChildren<ForestRenderer>();
                    var farTrees = forest?.transform.Find("Distant tree shadows");
                    // fixed = the pipeline asset's own 150 m; range = ShadowRange; trees = plus the forest's distant shadow meshes.
                    foreach (string config in (Argument("-shadowRegionConfigs") ?? "fixed,range,trees").Split(','))
                    {
                        SetStatic("AirCity.Rendering.ShadowRange", "enabled", config != "fixed");
                        if (farTrees != null) farTrees.gameObject.SetActive(config == "trees");
                        string root = "Captures/NewGame/Shadows/regions/" + preset + "-" + config + "/"; Directory.CreateDirectory(root);
                        var run = new RegionRun { preset = preset.ToString(), config = config, trees = trees.Count, pivot = pivot };
                        foreach (var view in new[] {
                            new View { name = "d0300-p35", distance = 300f, pitch = 35f, yaw = -35f },
                            new View { name = "d0650-p49", distance = 650f, pitch = 49f, yaw = -35f },
                            new View { name = "d1500-p55", distance = 1500f, pitch = 55f, yaw = -35f },
                            new View { name = "d4000-p55", distance = 4000f, pitch = 55f, yaw = -35f } })
                            run.shots.Add(Shoot(game, camera, sun, view, root, frames));
                        run.shadowTrees = forest != null && forest.GetType().GetProperty("DistantShadowTrees") is System.Reflection.PropertyInfo count ? (int)count.GetValue(forest) : -1;
                        result.regions.Add(run);
                    }
                    SetStatic("AirCity.Rendering.ShadowRange", "enabled", true);
                    if (farTrees != null) farTrees.gameObject.SetActive(true);
                }
                pivotOverride = null;
                result.pipelineAtEnd = PipelineSummary();
                if (result.pipelineAtEnd != result.pipelineAtStart) Debug.LogError("ShadowCoverageReview: the pipeline asset's shadow settings changed.");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/shadow-regions.json", JsonUtility.ToJson(result, true));
                Debug.Log("Shadow region review completed.");
                ShaderUtil.allowAsyncCompilation = async;
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShaderUtil.allowAsyncCompilation = async;
                EditorApplication.Exit(1);
            }
        }

        static (CareerGame game, Camera camera, Light sun) Setup(Review review)
        {
            EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
            var game = Object.FindAnyObjectByType<CareerGame>();
            if (game == null) throw new InvalidOperationException("New Game scene has no game host.");
            var state = State();
            game.Initialize(state, false); game.Paused = true;
            review.graphicsDevice = SystemInfo.graphicsDeviceName; review.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            review.trees = state.world.trees.Count;
            review.terminalBuilt = game.Airport != null && game.Airport.AddTerminal(TerminalCorners, 3) != null;
            if (game.aircraftPrefab != null)
            {
                var plane = Object.Instantiate(game.aircraftPrefab, AircraftAt, Quaternion.Euler(0f, 200f, 0f));
                plane.name = "Shadow review aircraft"; review.aircraftPlaced = true;
            }
            var camera = game.view.GetComponent<Camera>();
            var sun = RenderSettings.sun != null ? RenderSettings.sun :
                Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun == null) throw new InvalidOperationException("New Game scene has no sun.");
            var forest = game.GetComponentInChildren<ForestRenderer>(); review.forestTrees = forest != null ? forest.TreeCount : -1;
            return (game, camera, sun);
        }

        static Review Run(string label, string root, int frames)
        {
            var review = new Review { label = label, mapResolution = mapResolution, frames = frames };
            var (game, camera, sun) = Setup(review);
            review.pipeline = PipelineName(); review.pipelineAtStart = PipelineSummary();
            foreach (var view in Views) review.shots.Add(Shoot(game, camera, sun, view, root, frames));
            review.sweeps = Sweeps(game, camera);
            review.pipelineAtEnd = PipelineSummary();
            if (review.pipelineAtEnd != review.pipelineAtStart)
                Debug.LogError("ShadowCoverageReview: the pipeline asset's shadow settings changed: " + review.pipelineAtStart + " -> " + review.pipelineAtEnd);
            return review;
        }

        /// <summary>The road-connection water fixture (flat land at 100 m, a river at x = -1500) with two bridges of
        /// its own at 10 m (one across the river, one over land) and forest everywhere west of the airport except on
        /// water, beside roads and in the clearing round the pivot.</summary>
        public static CareerState State()
        {
            var state = RoadConnectionFixture.WaterState();
            var roads = state.publicRoads;
            foreach (var (a, b) in new[] { (WaterBridgeA, WaterBridgeB), (LandBridgeA, LandBridgeB) })
            {
                var from = new Vector3(a.x, BridgeLevel, a.y); var to = new Vector3(b.x, BridgeLevel, b.y);
                var edge = roads.AddSegment(roads.AddNode(from), roads.AddNode(to), PublicRoadStyle.Urban, RoadStructure.Elevated);
                edge.alignment = RoadBuildSettings.Default.Shape(from, to);
            }
            var world = state.world; var random = new System.Random(20261116);
            var lines = roads.segments.Select(s => (XZ(roads.Node(s.a).position), XZ(roads.Node(s.b).position))).ToList();
            for (float x = -3150f; x <= -300f; x += 9f)
            for (float z = -1000f; z <= 3150f; z += 9f)
            {
                var p = new Vector2(x + (float)random.NextDouble() * 6f - 3f, z + (float)random.NextDouble() * 6f - 3f);
                if (Clearing.Contains(p) || RoadConnectionFixture.WaterLand(p) < 99.8f) continue;
                if (lines.Any(l => DistanceToSegment(p, l.Item1, l.Item2) < 16f)) continue;
                world.trees.Add(new ForestTree { position = p, height = 9f + (float)random.NextDouble() * 13f });
            }
            if (!state.Validate(out string why)) throw new InvalidOperationException(why);
            return state;
        }

        static Shot Shoot(CareerGame game, Camera camera, Light sun, View view, string root, int frames)
        {
            game.view.FocusPoint(pivotOverride ?? (view.closeUp ? CloseUpPivot : Pivot), view.distance, view.pitch, view.yaw);
            camera.aspect = (float)Width / Height;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var shadows = sun.shadows;
            try
            {
                camera.targetTexture = rt;
                var shot = new Shot { name = view.name, distance = view.distance, pitch = view.pitch, yaw = view.yaw,
                    cameraPosition = camera.transform.position, nearClip = camera.nearClipPlane, farClip = camera.farClipPlane,
                    file = root + view.name + ".png" };
                var tryGet = typeof(CareerCamera).Assembly.GetType("AirCity.Rendering.ShadowRange")?.GetMethod("TryGet");
                var tryArgs = new object[] { camera, null };
                shot.shadowRange = tryGet != null && (bool)tryGet.Invoke(null, tryArgs) ? tryArgs[1].ToString() : "not following";
                // The values the pipeline holds while this camera renders (code may change them per camera and put
                // them back afterwards), sampled from a handler added after the camera's own.
                bool sampled = false;
                void Sample(UnityEngine.Rendering.ScriptableRenderContext context, Camera rendering)
                { if (rendering == camera && !sampled) { ReadPipeline(shot); sampled = true; } }
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += Sample;
                try { for (int i = 0; i < 3; i++) camera.Render(); }
                finally { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= Sample; }
                shot.pipelineAfterRender = PipelineSummary();
                var forest = game.GetComponentInChildren<ForestRenderer>();
                if (forest != null) { shot.treesDrawn = forest.LastSubmittedTrees; shot.treeDrawCalls = forest.LastDrawCalls; }
                // Lit, unshadowed, lit again, back to back: wind sways trees and ground cover between frames, so a pixel
                // counts as shadowed only when both lit frames are darker than the unshadowed one and agree with each other.
                camera.Render(); var lit = Read(rt);
                sun.shadows = LightShadows.None; camera.Render(); var flat = Read(rt);
                sun.shadows = shadows; camera.Render(); var again = Read(rt);
                File.WriteAllBytes(shot.file, lit.EncodeToPNG());
                Measure(camera, lit, again, flat, shot);
                Object.DestroyImmediate(lit); Object.DestroyImmediate(flat); Object.DestroyImmediate(again);
                // Interleaved rounds so drift (heat, background work) falls on both sides alike.
                var on = new List<double>(); var off = new List<double>(); var cpuOn = new List<double>(); var cpuOff = new List<double>();
                for (int round = 0; round < 4; round++)
                {
                    sun.shadows = shadows; on.Add(FrameMilliseconds(camera, rt, frames / 4 + 1)); cpuOn.Add(lastCpu);
                    sun.shadows = LightShadows.None; off.Add(FrameMilliseconds(camera, rt, frames / 4 + 1)); cpuOff.Add(lastCpu);
                }
                sun.shadows = shadows;
                on.Sort(); off.Sort(); cpuOn.Sort(); cpuOff.Sort();
                shot.msCpuOn = (cpuOn[0] + cpuOn[1]) * .5; shot.msCpuOff = (cpuOff[0] + cpuOff[1]) * .5;
                shot.msShadowsOn = (on[0] + on[1]) * .5; shot.msShadowsOff = (off[0] + off[1]) * .5;
                shot.msShadowCost = shot.msShadowsOn - shot.msShadowsOff;
                Debug.Log($"ShadowCoverageReview {view.name}: shadowDistance {shot.shadowDistance:0} reach99 {shot.shadowReach99:0} m of ground " +
                          $"{shot.groundBottom:0}-{shot.groundTop:0} m, shadowed {shot.shadowedFraction:P2}, frame {shot.msShadowsOn:0.00} ms " +
                          $"(no sun shadows {shot.msShadowsOff:0.00} ms)");
                return shot;
            }
            finally
            {
                sun.shadows = shadows;
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                rt.Release(); Object.DestroyImmediate(rt);
            }
        }

        static int mapResolution;
        /// <summary>Sets ShadowRange.mapResolution when that exists (reflection, so the same harness runs on the
        /// untouched "before" project).</summary>
        static void SetMapResolution(int size)
        {
            if (!SetStatic("AirCity.Rendering.ShadowRange", "mapResolution", size)) return;
            mapResolution = size;
        }

        /// <summary>Sets a private static tuning value of a runtime type (absent in the untouched project: false).</summary>
        static bool SetStatic(string type, string field, object value)
        {
            var info = typeof(CareerCamera).Assembly.GetType(type)?.GetField(field, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (info == null) { Debug.LogWarning($"ShadowCoverageReview: no {type}.{field} in this project."); return false; }
            info.SetValue(null, value); return true;
        }

        /// <summary>Pans, zooms and tilts the game camera frame by frame (no rendering) and counts layout changes.</summary>
        static List<Sweep> Sweeps(CareerGame game, Camera camera)
        {
            var sweeps = new List<Sweep>();
            var range = typeof(CareerCamera).Assembly.GetType("AirCity.Rendering.ShadowRange");
            var tryGet = range?.GetMethod("TryGet");
            if (tryGet == null) return sweeps;
            string Layout()
            {
                var args = new object[] { camera, null };
                if (!(bool)tryGet.Invoke(null, args)) return null;
                var layout = args[1]; var type = layout.GetType();
                float distance = (float)type.GetField("distance").GetValue(layout); var splits = (Vector3)type.GetField("splits").GetValue(layout);
                return distance.ToString("R") + " " + splits.x.ToString("R") + "/" + splits.y.ToString("R") + "/" + splits.z.ToString("R");
            }
            Sweep Run(string name, int frames, Action<float> pose)
            {
                var sweep = new Sweep { name = name, frames = frames, minRange = float.MaxValue };
                var seen = new List<string>();
                for (int i = 0; i < frames; i++)
                {
                    pose(frames == 1 ? 0f : (float)i / (frames - 1));
                    string layout = Layout(); if (layout == null) return sweep;
                    float distance = float.Parse(layout.Split(' ')[0]);
                    sweep.minRange = Mathf.Min(sweep.minRange, distance); sweep.maxRange = Mathf.Max(sweep.maxRange, distance);
                    if (seen.Count > 0 && seen[seen.Count - 1] == layout) continue;
                    if (seen.Count > 0) sweep.layoutChanges++;
                    if (seen.Count >= 2 && seen[seen.Count - 2] == layout) sweep.flips++;
                    seen.Add(layout);
                }
                return sweep;
            }
            Vector3 Ground(Vector3 p) { p.y = game.valley.World.Height(new Vector2(p.x, p.z)); return p; }
            // Pans: 600 frames across 2 km of the fixture (river, banks, forest) at fixed zoom, pitch and heading,
            // with the pivot on the ground as CareerCamera keeps it.
            foreach (float distance in new[] { 150f, 600f, 1500f, 4000f })
                sweeps.Add(Run($"pan-d{distance:0}-p55", 600, t => game.view.FocusPoint(Ground(Pivot + new Vector3(-1000f + 2000f * t, 0f, 300f * Mathf.Sin(t * 6.283f))), distance, 55f, 60f)));
            // Orbit: heading turns a full circle at 600 m.
            sweeps.Add(Run("orbit-d600-p55", 600, t => game.view.FocusPoint(Pivot, 600f, 55f, 60f + 360f * t)));
            // Zoom out 35 -> 7500 m and back in, geometric like the scroll wheel, at pitch 55 and 25.
            foreach (float pitch in new[] { 55f, 25f })
                sweeps.Add(Run($"zoom-p{pitch:0}", 1200, t => game.view.FocusPoint(Pivot, 35f * Mathf.Pow(7500f / 35f, 1f - Mathf.Abs(1f - 2f * t)), pitch, 60f)));
            // Tilt 18 -> 85 -> 18 degrees at 600 m.
            sweeps.Add(Run("tilt-d600", 600, t => game.view.FocusPoint(Pivot, 600f, Mathf.Lerp(18f, 85f, 1f - Mathf.Abs(1f - 2f * t)), 60f)));
            foreach (var sweep in sweeps)
                Debug.Log($"ShadowCoverageReview sweep {sweep.name}: {sweep.layoutChanges} layout changes, {sweep.flips} flips over {sweep.frames} frames, range {sweep.minRange:0}-{sweep.maxRange:0} m");
            return sweeps;
        }

        static Texture2D Read(RenderTexture rt)
        {
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = previous; return tex;
        }

        /// <summary>Lower-quartile milliseconds per frame (robust to other work on the machine), each frame followed
        /// by a one-pixel readback that waits for the GPU.</summary>
        static double lastCpu;
        static double FrameMilliseconds(Camera camera, RenderTexture rt, int frames)
        {
            var cpu = new List<double>();
            var probe = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var previous = RenderTexture.active; var times = new List<double>(); var timer = new Stopwatch();
            try
            {
                for (int i = 0; i < frames + 2; i++)
                {
                    timer.Restart();
                    camera.Render();
                    if (i >= 2) cpu.Add(timer.Elapsed.TotalMilliseconds);
                    RenderTexture.active = rt; probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    timer.Stop();
                    if (i >= 2) times.Add(timer.Elapsed.TotalMilliseconds);
                }
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(probe); }
            cpu.Sort(); lastCpu = cpu[cpu.Count / 4];
            times.Sort(); return times[times.Count / 4];
        }

        static void Measure(Camera camera, Texture2D lit, Texture2D again, Texture2D flat, Shot shot)
        {
            var on = lit.GetPixels32(); var on2 = again.GetPixels32(); var off = flat.GetPixels32();
            shot.groundBottom = GroundDistance(camera, .5f, 0f); shot.groundTop = GroundDistance(camera, .5f, 1f);
            float bandFrom = shot.groundBottom, bandTo = shot.groundTop > 0f ? shot.groundTop : 20000f;
            shot.bandFrom = bandFrom; shot.bandTo = bandTo;
            var reach = new List<float>(); var bandGround = new int[10]; var bandShade = new int[10];
            var waterDepth = new List<float>(); var landDepth = new List<float>();
            int shaded = 0, ground = 0, topRow = -1;
            for (int y = 0; y < Height; y++)
            {
                int rowShade = 0;
                for (int x = 0; x < Width; x += 2)
                {
                    int i = y * Width + x;
                    float t = GroundDistance(camera, (x + .5f) / Width, (y + .5f) / Height);
                    if (t <= 0f) continue;
                    ground++;
                    int band = Mathf.Clamp((int)((t - bandFrom) / Mathf.Max(1f, bandTo - bandFrom) * 10f), 0, 9);
                    bandGround[band]++;
                    float la = Luma(on[i]), la2 = Luma(on2[i]), lb = Luma(off[i]);
                    float darker = Mathf.Max(la, la2);
                    if (lb > .02f && darker < lb * .94f && lb - darker > .01f && Mathf.Abs(la - la2) < .03f)
                    {
                        shaded++; rowShade++; bandShade[band]++; reach.Add(t);
                        bool water = off[i].b > off[i].r + 25 && off[i].g > off[i].r + 15;
                        (water ? waterDepth : landDepth).Add(darker / lb);
                    }
                }
                if (rowShade >= Width / 2 * .002f) topRow = y;
            }
            shot.shadowedFraction = ground > 0 ? (float)shaded / ground : 0f;
            float Median(List<float> values) { if (values.Count == 0) return -1f; values.Sort(); return values[values.Count / 2]; }
            shot.shadowDepthWater = Median(waterDepth); shot.shadowDepthLand = Median(landDepth);
            shot.shadowedWaterPixels = waterDepth.Count; shot.shadowedLandPixels = landDepth.Count;
            shot.shadowTopRow = topRow < 0 ? 0f : (topRow + 1f) / Height;
            for (int i = 0; i < 10; i++) shot.bandShadowed[i] = bandGround[i] > 0 ? (float)bandShade[i] / bandGround[i] : 0f;
            if (reach.Count > 0)
            {
                reach.Sort();
                shot.shadowReachMedian = reach[reach.Count / 2];
                shot.shadowReach99 = reach[Mathf.Min(reach.Count - 1, (int)(reach.Count * .99f))];
                shot.shadowReachMax = reach[reach.Count - 1];
            }
        }

        static float Luma(Color32 c) => (.2126f * c.r + .7152f * c.g + .0722f * c.b) / 255f;

        /// <summary>Distance from the camera along the viewport ray to the plane y = Ground, or -1 for sky.</summary>
        static float GroundDistance(Camera camera, float vx, float vy)
        {
            var ray = camera.ViewportPointToRay(new Vector3(vx, vy, 0f));
            if (ray.direction.y > -1e-4f) return -1f;
            float t = (ray.origin.y - Ground) / -ray.direction.y;
            return t > camera.farClipPlane ? -1f : t;
        }

        static void ReadPipeline(Shot shot)
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (asset == null) return;
            var type = asset.GetType();
            object Get(string name) => type.GetProperty(name)?.GetValue(asset);
            shot.shadowDistance = Get("shadowDistance") is float d ? d : -1f;
            shot.cascades = Get("shadowCascadeCount") is int c ? c : -1;
            shot.cascadeSplits = Get("cascade4Split") is Vector3 s ? s : Vector3.zero;
            shot.cascadeBorder = Get("cascadeBorder") is float border ? border : -1f;
            shot.shadowmapResolution = Get("mainLightShadowmapResolution") is int r ? r : -1;
        }

        static string PipelineSummary()
        {
            var shot = new Shot(); ReadPipeline(shot);
            return $"{shot.shadowDistance} {shot.cascades} {shot.cascadeSplits.x:R}/{shot.cascadeSplits.y:R}/{shot.cascadeSplits.z:R} {shot.cascadeBorder:R} {shot.shadowmapResolution}";
        }

        static string PipelineName()
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            return asset == null ? "none" : asset.name;
        }

        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
