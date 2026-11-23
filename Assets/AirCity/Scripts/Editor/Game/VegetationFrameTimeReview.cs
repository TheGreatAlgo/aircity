using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Game
{
    /// <summary>Frame time of the forest at fixed cameras, with trees, ground cover and sun shadows switched on and
    /// off, so their cost is a difference of measured milliseconds, not a triangle count. Batch entry:
    /// <c>-executeMethod AirCity.EditorTools.Game.VegetationFrameTimeReview.RunFromCommandLine</c>; writes
    /// <c>Logs/vegetation-frame-time.{json,txt}</c>. With <c>-vegetationReviewBase</c> it instead splits what is left
    /// without vegetation into terrain, water and the pipeline's fixed passes (<c>Logs/base-frame-time.*</c>).
    /// Run it in a clone: nothing else may use the GPU meanwhile.</summary>
    public static class VegetationFrameTimeReview
    {
        [Serializable] sealed class Review
        {
            public string graphicsDevice, graphicsApi, processor;
            public int width, height, frames, rounds;
            public List<View> views = new List<View>();
        }
        [Serializable] sealed class View
        {
            public string name, preset;
            public float cameraRange;
            public int regionTrees, submittedTrees, fullDetailTrees, mediumDetailTrees, distantTrees, treeDrawCalls, distantShadowTrees;
            public long treeTriangles, groundCoverTriangles;
            public int groundCoverDrawCalls, groundCoverInstances;
            public float shadowDistance;
            public List<Sample> samples = new List<Sample>();
        }
        /// <summary>mainThread: C# and culling until Render() returns. frame: N renders, then wait for the GPU once
        /// (what limits the frame rate). synced: wait for the GPU after every render (main thread + GPU, no overlap).</summary>
        [Serializable] sealed class Sample
        {
            public string configuration;
            public bool trees, groundCover, shadows, terrain, water, scene;
            public int width, height;
            public double mainThreadMilliseconds, frameMilliseconds, syncedMilliseconds;
        }

        const int Width = 1920, Height = 1080, Frames = 24, Rounds = 3;

        struct Configuration
        {
            public string name; public bool trees, cover, shadows, quarter, noTerrain, noWater, noScene;
            public Configuration(string name, bool trees, bool cover, bool shadows, bool quarter = false, bool noTerrain = false, bool noWater = false, bool noScene = false)
            { this.name = name; this.trees = trees; this.cover = cover; this.shadows = shadows; this.quarter = quarter; this.noTerrain = noTerrain; this.noWater = noWater; this.noScene = noScene; }
        }
        static readonly Configuration[] Vegetation = {
            new Configuration("everything", true, true, true),
            new Configuration("no trees", false, true, true),
            new Configuration("no ground cover", true, false, true),
            new Configuration("no vegetation", false, false, true),
            new Configuration("everything, no shadows", true, true, false),
            new Configuration("no trees, no shadows", false, true, false),
            new Configuration("no vegetation, no shadows", false, false, false),
            new Configuration("everything, quarter pixels", true, true, true, true),
            new Configuration("no vegetation, quarter pixels", false, false, true, true),
        };
        static readonly Configuration[] Base = {
            new Configuration("no vegetation", false, false, true),
            new Configuration("and no water", false, false, true, noWater: true),
            new Configuration("and no terrain", false, false, true, noTerrain: true),
            new Configuration("and no terrain or water", false, false, true, noTerrain: true, noWater: true),
            new Configuration("nothing drawn (fixed passes)", false, false, true, noScene: true),
            new Configuration("nothing drawn, quarter pixels", false, false, true, quarter: true, noScene: true),
        };

        public static void RunFromCommandLine()
        {
            try
            {
                Directory.CreateDirectory("Logs"); Directory.CreateDirectory("Captures/VegetationFrameTime");
                bool baseScene = Array.IndexOf(Environment.GetCommandLineArgs(), "-vegetationReviewBase") >= 0;
                string output = baseScene ? "Logs/base-frame-time" : "Logs/vegetation-frame-time";
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                var game = UnityEngine.Object.FindAnyObjectByType<CareerGame>();
                if (game == null) throw new InvalidOperationException("New Game scene has no game host.");
                var review = new Review { graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    processor = SystemInfo.processorType, width = Width, height = Height, frames = Frames, rounds = Rounds };
                foreach (var preset in new[] { RegionPreset.Woodland, RegionPreset.Tropical })
                {
                    int seed = preset == RegionPreset.Woodland ? 19770616 : 445090; // the forest density review's regions
                    var state = CareerState.New(seed, preset: preset);
                    game.Initialize(state, false);
                    var forest = game.GetComponentInChildren<ForestRenderer>();
                    var cover = game.GetComponentInChildren<GroundCoverRenderer>();
                    if (forest == null || cover == null) throw new InvalidOperationException("The region has no forest or ground cover renderer.");
                    game.valley.SetRoadClock(45f);
                    var camera = game.view.GetComponent<Camera>();
                    var point = ForestDensityReview.ForestTarget(state.world);
                    var target = new Vector3(point.x, game.valley.DisplayHeight(point) + 9f, point.y);
                    var views = new List<(string name, float range, Action frame)> {
                        ("region overview", 5500f, () => game.view.Region()),
                        ("forest 650 m", 650f, () => game.view.FocusPoint(target, 650f, 49f, -35f)),
                        ("forest 400 m", 400f, () => game.view.FocusPoint(target, 400f, 56f, -35f)),
                        ("forest 145 m", 145f, () => game.view.FocusPoint(target, 145f, 32f, -35f)),
                        ("forest 70 m", 70f, () => game.view.FocusPoint(target, 70f, 26f, -35f)),
                    };
                    if (state.world.sites.Count > 0)
                    { var site = state.world.sites[0]; views.Insert(1, ("airport site", 1150f, () => game.view.Frame(site))); }
                    foreach (var (name, range, frame) in views)
                    {
                        frame();
                        review.views.Add(Measure(game, forest, cover, camera, preset, name, range, state.world.trees.Count, baseScene ? Base : Vegetation));
                        File.WriteAllText(output + ".json", JsonUtility.ToJson(review, true));
                        File.WriteAllText(output + ".txt", Table(review));
                    }
                }
                UnityEngine.Debug.Log("Vegetation frame time review completed.\n" + Table(review));
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        static View Measure(CareerGame game, ForestRenderer forest, GroundCoverRenderer cover, Camera camera, RegionPreset preset, string name, float range, int trees, Configuration[] configurations)
        {
            var sun = RenderSettings.sun;
            if (sun == null) throw new InvalidOperationException("The scene has no sun.");
            var shadowMode = sun.shadows;
            var full = Target(Width, Height); var quarter = Target(Width / 2, Height / 2);
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var view = new View { name = name, preset = preset.ToString(), cameraRange = range, regionTrees = trees };
            var terrain = game.valley.Terrain;
            var water = new List<Renderer>();
            foreach (var surface in game.GetComponentsInChildren<RegionWaterView>()) water.AddRange(surface.GetComponentsInChildren<Renderer>());
            int mask = camera.cullingMask;
            foreach (var c in configurations)
                view.samples.Add(new Sample { configuration = c.name, trees = c.trees, groundCover = c.cover, shadows = c.shadows,
                    terrain = !c.noTerrain && !c.noScene, water = !c.noWater && !c.noScene, scene = !c.noScene,
                    width = c.quarter ? Width / 2 : Width, height = c.quarter ? Height / 2 : Height,
                    mainThreadMilliseconds = double.MaxValue, frameMilliseconds = double.MaxValue, syncedMilliseconds = double.MaxValue });
            var previous = camera.targetTexture;
            try
            {
                // Rounds visit every configuration in turn and keep each one's best time, so heat and background
                // work spread over all of them instead of landing on whichever ran last.
                for (int round = 0; round < Rounds; round++)
                for (int i = 0; i < configurations.Length; i++)
                {
                    var c = configurations[i]; var sample = view.samples[i];
                    forest.enabled = c.trees; cover.enabled = c.cover; sun.shadows = c.shadows ? shadowMode : LightShadows.None;
                    if (terrain != null) terrain.enabled = !c.noTerrain;
                    foreach (var surface in water) surface.enabled = !c.noWater;
                    camera.cullingMask = c.noScene ? 0 : mask;
                    var target = c.quarter ? quarter : full;
                    camera.targetTexture = target; camera.aspect = Width / (float)Height;
                    for (int warm = 0; warm < 3; warm++) camera.Render();
                    Wait(target, probe);
                    var timer = Stopwatch.StartNew();
                    for (int f = 0; f < Frames; f++) camera.Render();
                    double mainThread = timer.Elapsed.TotalMilliseconds / Frames;
                    Wait(target, probe);
                    double frame = timer.Elapsed.TotalMilliseconds / Frames;
                    timer.Restart();
                    for (int f = 0; f < Frames; f++) { camera.Render(); Wait(target, probe); }
                    double synced = timer.Elapsed.TotalMilliseconds / Frames;
                    sample.mainThreadMilliseconds = Math.Min(sample.mainThreadMilliseconds, mainThread);
                    sample.frameMilliseconds = Math.Min(sample.frameMilliseconds, frame);
                    sample.syncedMilliseconds = Math.Min(sample.syncedMilliseconds, synced);
                    if (round == 0 && i == 0)
                    {
                        view.submittedTrees = forest.LastSubmittedTrees; view.fullDetailTrees = forest.LastFullDetailTrees;
                        view.mediumDetailTrees = forest.LastMediumDetailTrees; view.distantTrees = forest.LastImpostorTrees;
                        view.treeDrawCalls = forest.LastDrawCalls; view.treeTriangles = forest.LastSubmittedTriangles;
                        view.distantShadowTrees = forest.DistantShadowTrees;
                        view.groundCoverDrawCalls = cover.LastDrawCalls; view.groundCoverInstances = cover.LastVisibleInstances;
                        view.groundCoverTriangles = cover.LastSubmittedTriangles;
                        view.shadowDistance = AirCity.Rendering.ShadowRange.Distance(camera);
                        Save(quarter, camera, "Captures/VegetationFrameTime/" + preset + "-" + name.Replace(' ', '-') + ".jpg");
                    }
                }
            }
            finally
            {
                forest.enabled = true; cover.enabled = true; sun.shadows = shadowMode;
                if (terrain != null) terrain.enabled = true;
                foreach (var surface in water) surface.enabled = true;
                camera.cullingMask = mask;
                camera.targetTexture = previous; camera.ResetAspect();
                full.Release(); quarter.Release();
                UnityEngine.Object.DestroyImmediate(full); UnityEngine.Object.DestroyImmediate(quarter); UnityEngine.Object.DestroyImmediate(probe);
            }
            return view;
        }

        // The game renders without MSAA (PC_RPAsset), so the target has none either.
        static RenderTexture Target(int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            target.Create(); return target;
        }

        /// <summary>Reading a pixel back returns only when the GPU has finished everything queued for the target.</summary>
        static void Wait(RenderTexture target, Texture2D probe)
        {
            var active = RenderTexture.active; RenderTexture.active = target;
            probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            RenderTexture.active = active;
        }

        static void Save(RenderTexture target, Camera camera, string path)
        {
            var previous = camera.targetTexture; camera.targetTexture = target; camera.Render(); camera.targetTexture = previous;
            var active = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            RenderTexture.active = active;
            File.WriteAllBytes(path, image.EncodeToJPG(80));
            UnityEngine.Object.DestroyImmediate(image);
        }

        static string Table(Review review)
        {
            var text = new StringBuilder();
            text.AppendLine($"{review.graphicsDevice} ({review.graphicsApi}), {review.processor}; {review.width} x {review.height}, best of {review.rounds} x {review.frames} frames");
            text.AppendLine("ms per frame: main = main thread only, frame = N frames then one GPU wait, synced = GPU wait every frame");
            foreach (var view in review.views)
            {
                text.AppendLine();
                text.AppendLine($"{view.preset} / {view.name}: {view.submittedTrees} of {view.regionTrees} trees submitted ({view.fullDetailTrees} full, {view.mediumDetailTrees} medium, {view.distantTrees} cards) in {view.treeDrawCalls} draws, {view.treeTriangles} tree triangles; ground cover {view.groundCoverInstances} instances, {view.groundCoverTriangles} triangles, {view.groundCoverDrawCalls} draws; shadow distance {view.shadowDistance:0} m; {view.distantShadowTrees} shadow crowns built");
                foreach (var sample in view.samples)
                    text.AppendLine($"  {sample.configuration,-32} main {sample.mainThreadMilliseconds,7:0.00}   frame {sample.frameMilliseconds,7:0.00}   synced {sample.syncedMilliseconds,7:0.00}");
            }
            return text.ToString();
        }
    }
}
