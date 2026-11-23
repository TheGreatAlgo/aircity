using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Game
{
    /// <summary>Repeatable real-region forest captures and CPU work measurements in an isolated validation project.</summary>
    public static class ForestDensityReview
    {
        [Serializable] sealed class Review
        {
            public string label, graphicsDevice, graphicsApi;
            public List<RegionResult> regions = new List<RegionResult>();
        }
        [Serializable] sealed class RegionResult
        {
            public string preset, source;
            public int trees, removedInBrush, groundCoverInstances;
            public int clearCandidateTrees, clearRendererCheckedTrees;
            public bool groundCoverPreservedByClearing;
            public Vector2 forestTarget, groundFeatureTarget;
            public bool hasGroundFeature;
            public double generateMilliseconds, initializeMilliseconds, rebuildMilliseconds, firstQueryMilliseconds, clearMilliseconds, clearPresentationMilliseconds;
            public List<Count> treeStyles = new List<Count>(), groundCoverKinds = new List<Count>();
            public List<Shot> shots = new List<Shot>();
        }
        [Serializable] sealed class Count { public string name; public int count; }
        [Serializable] sealed class Shot
        {
            public string file;
            public int treeDrawCalls, submittedInstances, highDetailInstances, mediumDetailInstances, distantInstances;
            public int groundCoverDrawCalls, groundCoverVisibleInstances;
            public long submittedTriangles, groundCoverTriangles;
            public Vector3 cameraPosition;
            public float cameraFieldOfView, cameraAspect;
            public bool baselineCameraMatched;
            public double captureMilliseconds;
        }

        public static void RunFromCommandLine()
        {
            try
            {
                string label = Argument("-forestReviewLabel") ?? "current";
                if (label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid review label.");
                string root = "Captures/NewGame/ForestDensity/" + label + "/";
                string baselinePath = Argument("-forestReviewBaseline") ??
                    (label.StartsWith("biome-") ? "Logs/forest-density-biome-before.json" : "Logs/forest-density-before.json");
                var baseline = label != "before" && File.Exists(baselinePath) ? JsonUtility.FromJson<Review>(File.ReadAllText(baselinePath)) : null;
                Directory.CreateDirectory(root); Directory.CreateDirectory("Logs");
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                var game = UnityEngine.Object.FindAnyObjectByType<CareerGame>();
                if (game == null) throw new InvalidOperationException("New Game scene has no game host.");
                var review = new Review { label = label, graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString() };
                foreach (var preset in new[] { RegionPreset.Woodland, RegionPreset.Mountains, RegionPreset.Tropical, RegionPreset.Desert })
                {
                    int seed = preset == RegionPreset.Woodland ? 19770616 : preset == RegionPreset.Mountains ? 873241 : preset == RegionPreset.Tropical ? 445090 : 692835;
                    var timer = Stopwatch.StartNew();
                    var state = CareerState.New(seed, preset: preset);
                    var region = new RegionResult { preset = preset.ToString(), source = state.world.landscapeSource,
                        trees = state.world.trees.Count, generateMilliseconds = timer.Elapsed.TotalMilliseconds };
                    if (!state.world.Validate(out string problem)) throw new InvalidOperationException(preset + ": " + problem);
                    timer.Restart(); game.Initialize(state, false); region.initializeMilliseconds = timer.Elapsed.TotalMilliseconds;
                    var forest = game.GetComponentInChildren<ForestRenderer>();
                    if (forest == null || forest.TreeCount != state.world.trees.Count) throw new InvalidOperationException("The full saved forest was not instanced.");
                    var groundCover = game.GetComponentInChildren<GroundCoverRenderer>();
                    region.groundCoverInstances = groundCover != null ? groundCover.InstanceCount : 0;
                    if (groundCover != null)
                        foreach (var kind in groundCover.Instances.GroupBy(item => item.kind))
                            region.groundCoverKinds.Add(new Count { name = kind.Key.ToString(), count = kind.Count() });
                    ReadTreeStyles(forest, region);
                    timer.Restart(); forest.Rebuild(state.world, game.valley.DisplayHeight); region.rebuildMilliseconds = timer.Elapsed.TotalMilliseconds;
                    var baselineRegion = baseline?.regions.Find(item => item.preset == region.preset);
                    region.forestTarget = baselineRegion != null ? baselineRegion.forestTarget : ForestTarget(state.world);
                    if (groundCover != null && (preset == RegionPreset.Woodland || preset == RegionPreset.Tropical))
                    {
                        region.hasGroundFeature = baselineRegion != null && baselineRegion.hasGroundFeature;
                        region.groundFeatureTarget = region.hasGroundFeature ? baselineRegion.groundFeatureTarget : GroundFeatureTarget(state.world, groundCover, region.forestTarget, out region.hasGroundFeature);
                    }
                    game.valley.SetRoadClock(45f);
                    var camera = game.view.GetComponent<Camera>();
                    game.view.Region(); Capture(game, forest, camera, region, baselineRegion, root, preset + "-overview");
                    var point = region.forestTarget;
                    var target = new Vector3(point.x, game.valley.DisplayHeight(point) + 9f, point.y);
                    game.view.FocusPoint(target, 650f, 49f, -35f);
                    Capture(game, forest, camera, region, baselineRegion, root, preset + "-forest-mid");
                    if (preset == RegionPreset.Woodland || preset == RegionPreset.Tropical)
                    {
                        game.view.FocusPoint(target, 145f, 32f, -35f);
                        Capture(game, forest, camera, region, baselineRegion, root, preset + "-forest-close");
                        game.view.FocusPoint(target, 1200f, 80f, 0f);
                        Capture(game, forest, camera, region, baselineRegion, root, preset + "-canopy-overhead");
                        if (region.hasGroundFeature)
                        {
                            var feature = region.groundFeatureTarget;
                            game.view.FocusPoint(new Vector3(feature.x, game.valley.DisplayHeight(feature) + 1.5f, feature.y), 72f, 34f, -35f);
                            Capture(game, forest, camera, region, baselineRegion, root, preset + "-natural-ground-feature");
                        }
                    }
                    if (preset == RegionPreset.Tropical)
                    {
                        // The same narrow real reef island used by the landscape coast review, north-up.
                        var motu = new Vector2(1162.5f, -2100f);
                        game.view.FocusPoint(new Vector3(motu.x, game.valley.DisplayHeight(motu) + 3f, motu.y), 235f, 43f, -35f);
                        Capture(game, forest, camera, region, baselineRegion, root, "Tropical-low-motu");
                    }
                    if (preset == RegionPreset.Woodland || preset == RegionPreset.Tropical)
                    {
                        state.selectedSite = 0; state.mode = GameMode.Sandbox;
                        var oldGroundCover = groundCover?.Instances;
                        timer.Restart(); CareerConstruction.Trees(state, point, 60f); region.firstQueryMilliseconds = timer.Elapsed.TotalMilliseconds;
                        var changedBounds = new Rect(point.x - 60f, point.y - 60f, 120f, 120f);
                        var query = typeof(ValleyWorld).Assembly.GetType("AirCity.World.ForestQuery");
                        if (query?.GetMethod("Candidates")?.Invoke(null, new object[] { state.world, changedBounds }) is IEnumerable<int> candidates)
                            region.clearCandidateTrees = candidates.Count();
                        timer.Restart();
                        if (!CareerConstruction.Clear(state, point, 60f, out problem)) throw new InvalidOperationException("Forest clearing review: " + problem);
                        region.clearMilliseconds = timer.Elapsed.TotalMilliseconds;
                        region.removedInBrush = state.world.trees.Count(tree => tree.removed);
                        timer.Restart(); game.PlayerChanged(CareerTool.ClearTrees, changedBounds); region.clearPresentationMilliseconds = timer.Elapsed.TotalMilliseconds;
                        region.clearRendererCheckedTrees = Metric(forest, "LastRemovalCheckedTrees");
                        if (forest.TreeCount != state.world.trees.Count - region.removedInBrush)
                            throw new InvalidOperationException("The clearing presentation retained removed roots or removed surviving trees.");
                        region.groundCoverPreservedByClearing = groundCover == null || ReferenceEquals(oldGroundCover, groundCover.Instances);
                        if (!region.groundCoverPreservedByClearing) throw new InvalidOperationException("Tree clearing rebuilt ground-cover habitat.");
                        game.view.FocusPoint(target, 400f, 56f, -35f);
                        Capture(game, forest, camera, region, baselineRegion, root, preset + "-cleared-brush");
                        var groundTarget = new Vector3(point.x, game.valley.DisplayHeight(point) + 1.5f, point.y);
                        game.view.FocusPoint(groundTarget, 92f, 26f, -35f);
                        Capture(game, forest, camera, region, baselineRegion, root, preset + "-ground-depth");
                        if (label == "biome-after" && region.hasGroundFeature)
                        {
                            // A second tree-only clearing exposes an actual generated rock cluster. Keep this
                            // labelled as a showcase rather than treating its changed camera as a matched pair.
                            var feature = region.groundFeatureTarget;
                            if (CareerConstruction.Clear(state, feature, 38f, out _, -1))
                                game.PlayerChanged(CareerTool.ClearTrees, new Rect(feature.x - 38f, feature.y - 38f, 76f, 76f));
                            game.view.FocusPoint(new Vector3(feature.x, game.valley.DisplayHeight(feature) + 1.5f, feature.y), 95f, 62f, -35f);
                            Capture(game, forest, camera, region, null, root, preset + "-rock-cluster-showcase");
                        }
                    }
                    review.regions.Add(region);
                    File.WriteAllText("Logs/forest-density-" + label + ".json", JsonUtility.ToJson(review, true));
                }
                UnityEngine.Debug.Log("Forest density review completed: " + Path.GetFullPath(root));
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        static void Capture(CareerGame game, ForestRenderer forest, Camera camera, RegionResult region, RegionResult baselineRegion, string root, string name)
        {
            string file = root + name + ".png";
            var timer = Stopwatch.StartNew();
            // Do not inherit the editor Game view's aspect ratio when comparing isolated runs.
            camera.aspect = 1.6f;
            try { Dev.SceneCapture.Capture(camera, file, 1600, 1000); }
            finally { camera.ResetAspect(); }
            var shot = new Shot { file = file, treeDrawCalls = forest.LastDrawCalls,
                submittedInstances = Metric(forest, "LastSubmittedTrees"), highDetailInstances = Metric(forest, "LastFullDetailTrees"),
                mediumDetailInstances = Metric(forest, "LastMediumDetailTrees"), distantInstances = Metric(forest, "LastImpostorTrees"),
                submittedTriangles = typeof(ForestRenderer).GetProperty("LastSubmittedTriangles")?.GetValue(forest) is long triangles ? triangles : -1,
                cameraPosition = camera.transform.position, cameraFieldOfView = camera.fieldOfView, cameraAspect = 1.6f,
                captureMilliseconds = timer.Elapsed.TotalMilliseconds };
            var cover = game.GetComponentInChildren<GroundCoverRenderer>();
            if (cover != null)
            {
                shot.groundCoverDrawCalls = cover.LastDrawCalls; shot.groundCoverVisibleInstances = cover.LastVisibleInstances;
                shot.groundCoverTriangles = typeof(GroundCoverRenderer).GetProperty("LastSubmittedTriangles")?.GetValue(cover) is long coverTriangles ? coverTriangles : -1;
            }
            var baseline = baselineRegion?.shots.Find(item => Path.GetFileNameWithoutExtension(item.file) == name);
            if (baseline != null)
            {
                if (Vector3.Distance(shot.cameraPosition, baseline.cameraPosition) > .001f ||
                    baseline.cameraFieldOfView > 0f && Mathf.Abs(shot.cameraFieldOfView - baseline.cameraFieldOfView) > .001f ||
                    baseline.cameraAspect > 0f && Mathf.Abs(shot.cameraAspect - baseline.cameraAspect) > .001f)
                    throw new InvalidOperationException("Before/after forest camera changed for " + name);
                shot.baselineCameraMatched = true;
            }
            if (shot.submittedInstances < 0) ReadLegacyMetrics(forest, camera, shot);
            region.shots.Add(shot);
        }

        static void ReadTreeStyles(ForestRenderer forest, RegionResult region)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            if (!(typeof(ForestRenderer).GetField("batches", flags)?.GetValue(forest) is System.Collections.IEnumerable batches) ||
                !(typeof(ForestRenderer).GetField("activeLibrary", flags)?.GetValue(forest) is TreeLibrary library)) return;
            var counts = new int[library.variants.Length];
            foreach (var batch in batches)
            {
                var type = batch.GetType();
                int variant = (int)type.GetField("variant", flags).GetValue(batch);
                counts[variant] += ((Matrix4x4[])type.GetField("matrices", flags).GetValue(batch)).Length;
            }
            for (int i = 0; i < counts.Length; i++)
                region.treeStyles.Add(new Count { name = library.variants[i].name, count = counts[i] });
        }

        static Vector2 GroundFeatureTarget(ValleyWorld world, GroundCoverRenderer cover, Vector2 forestTarget, out bool found)
        {
            found = false; float best = float.NegativeInfinity; Vector2 point = default;
            foreach (var item in cover.Instances.Where(item => item.kind == GroundCoverKind.Boulder).OrderByDescending(item => item.scale).Take(400))
            {
                var p = item.position;
                int x = Mathf.Clamp(Mathf.RoundToInt((p.x + ValleyWorld.Size * .5f) / world.Spacing), 0, ValleyWorld.Resolution - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt((p.y + ValleyWorld.Size * .5f) / world.Spacing), 0, ValleyWorld.Resolution - 1);
                byte habitat = world.landCover[z * ValleyWorld.Resolution + x];
                if (habitat != 10 && habitat != 20 && habitat != 30 || world.IsWater(p)) continue;
                float relief = Mathf.Abs(world.Height(p + Vector2.right * 15f) - world.Height(p - Vector2.right * 15f)) +
                    Mathf.Abs(world.Height(p + Vector2.up * 15f) - world.Height(p - Vector2.up * 15f));
                float score = item.scale * 8f - Vector2.Distance(p, forestTarget) * .004f - relief * .5f;
                if (score <= best) continue;
                best = score; point = p; found = true;
            }
            return point;
        }

        // Allows the same harness to measure the previous 800 m renderer in an untouched baseline clone.
        static void ReadLegacyMetrics(ForestRenderer forest, Camera camera, Shot shot)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            if (!(typeof(ForestRenderer).GetField("batches", flags)?.GetValue(forest) is System.Collections.IEnumerable batches) ||
                !(typeof(ForestRenderer).GetField("activeLibrary", flags)?.GetValue(forest) is TreeLibrary library)) return;
            var projection = Matrix4x4.Perspective(camera.fieldOfView, 1.6f, camera.nearClipPlane, camera.farClipPlane);
            var planes = GeometryUtility.CalculateFrustumPlanes(projection * camera.worldToCameraMatrix);
            shot.submittedInstances = shot.highDetailInstances = shot.mediumDetailInstances = shot.distantInstances = 0;
            shot.submittedTriangles = 0;
            int drawCalls = 0;
            foreach (var batch in batches)
            {
                var type = batch.GetType();
                var bounds = (Bounds)type.GetField("bounds", flags).GetValue(batch);
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                float distance = Vector3.Distance(camera.transform.position, bounds.ClosestPoint(camera.transform.position));
                int level = distance < 250f ? 0 : distance < 850f ? 1 : 2;
                int count = ((Matrix4x4[])type.GetField("matrices", flags).GetValue(batch)).Length;
                int variant = (int)type.GetField("variant", flags).GetValue(batch);
                var mesh = library.variants[variant].levels[level];
                shot.submittedInstances += count;
                if (level == 0) shot.highDetailInstances += count;
                else if (level == 1) shot.mediumDetailInstances += count;
                else shot.distantInstances += count;
                shot.submittedTriangles += ((long)mesh.GetIndexCount(0) + (long)mesh.GetIndexCount(1)) / 3 * count;
                if (mesh.GetIndexCount(0) > 0) drawCalls++;
                if (mesh.GetIndexCount(1) > 0) drawCalls++;
            }
            if (drawCalls != shot.treeDrawCalls) throw new InvalidOperationException($"Baseline forest frustum mismatch: rendered {shot.treeDrawCalls}, measured {drawCalls}.");
        }

        // A mapped, reasonably gentle interior forest patch. This depends on geography, never on the candidate scatter.
        internal static Vector2 ForestTarget(ValleyWorld world)
        {
            Vector2 best = Vector2.zero; float bestScore = float.NegativeInfinity;
            for (int z = 48; z < ValleyWorld.Resolution - 48; z += 8)
            for (int x = 48; x < ValleyWorld.Resolution - 48; x += 8)
            {
                int index = z * ValleyWorld.Resolution + x;
                if (world.landCover[index] != 10 && world.landCover[index] != 95) continue;
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                if (world.IsWater(p)) continue;
                if (world.preset == RegionPreset.Mountains && world.Height(p) > 340f) continue;
                float slope = Mathf.Abs(world.Height(p + Vector2.right * 40f) - world.Height(p - Vector2.right * 40f)) +
                    Mathf.Abs(world.Height(p + Vector2.up * 40f) - world.Height(p - Vector2.up * 40f));
                float score = -slope * 1.5f - p.magnitude * .001f;
                for (int dz = -16; dz <= 16; dz += 4)
                for (int dx = -16; dx <= 16; dx += 4)
                {
                    byte cover = world.landCover[index + dz * ValleyWorld.Resolution + dx];
                    if (cover == 10 || cover == 95) score += 2f;
                }
                foreach (var town in world.settlements) if ((town - p).sqrMagnitude < 300f * 300f) score -= 300f;
                for (int i = 1; i < world.publicRoad.Count; i++)
                    if (CareerConstruction.Distance(p, world.publicRoad[i - 1], world.publicRoad[i]) < 250f) { score -= 300f; break; }
                if (score <= bestScore) continue;
                bestScore = score; best = p;
            }
            if (bestScore == float.NegativeInfinity && world.trees.Count > 0) best = world.trees[world.trees.Count / 2].position;
            return best;
        }

        static int Metric(ForestRenderer forest, string name) => typeof(ForestRenderer).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(forest) is int value ? value : -1;
        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
