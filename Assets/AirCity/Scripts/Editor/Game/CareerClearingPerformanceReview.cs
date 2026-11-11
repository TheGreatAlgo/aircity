using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Warm CPU measurements of the release operations. All saves stay under this project's ignored Logs.</summary>
    public static class CareerClearingPerformanceReview
    {
        const int Samples = 3;
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        public sealed class Timing
        {
            public string name;
            public double[] milliseconds;
            public double medianMilliseconds, maxMilliseconds;
        }
        [Serializable]
        public sealed class Result
        {
            public string utc, unityVersion, measurementNotes, saveDirectory;
            public int seed, treeCount, survivingTreeCount, groundCoverCount, alphamapResolution, samples, clearedTrees, localSurfaceTileCount, localSurfacePixels;
            public long saveBytes;
            public bool groundCoverReferencePreserved, savedClearingMatchesState;
            public Timing paintFull1024, uploadFullAlphamap, groundCoverFullRebuild, synchronousSave, oldReleaseFullSurfaceAndSave;
            public Timing saveSnapshotQueue, saveWorkerCompletionWait, playerBrushStamp, playerTreeRelease, localSurfaceTile, localSurfaceDrain;
        }

        public static void Run() => FromCommandLine();

        public static void FromCommandLine()
        {
            try
            {
                string output = Argument("-clearingPerformanceOutput", "Logs/career-clearing-performance.json");
                string saveDirectory = Path.GetFullPath("Logs/CareerClearingPerformanceReview");
                NewGameSetup.UpdateWorldAssets();
                var game = Object.FindAnyObjectByType<CareerGame>();
                if (game == null) throw new InvalidOperationException("The New Game scene contains no career game.");
                var state = CareerState.New(42);
                if (!state.Build(out string problem, 0)) throw new InvalidOperationException(problem);
                // Initialization warms Burst kernels, full terrain painting and shared-instance grouping.
                // Disk writes use only our temporary store, including any release method that calls game.Save.
                var store = new CareerSaveStore(saveDirectory);
                Set(game, "store", store);
                game.Initialize(state, true);
                var valley = game.valley;
                var roads = Get<PublicRoadGeometry>(valley, "roadGeometry");
                var exclusions = Get<List<GroundCoverExclusion>>(valley, "groundExclusions");
                var details = valley.GetComponentInChildren<GroundCoverRenderer>();
                if (details == null) throw new InvalidOperationException("The nature detail renderer was not initialized.");
                var terrain = valley.Terrain.terrainData;
                if (terrain.alphamapResolution != 1024) throw new InvalidOperationException("The review requires the real 1024 terrain control map.");

                float[,,] painted = null;
                var result = new Result {
                    utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                    seed = state.world.seed, treeCount = state.world.trees.Count,
                    survivingTreeCount = state.world.trees.Count(t => !t.removed), groundCoverCount = details.InstanceCount,
                    alphamapResolution = terrain.alphamapResolution, samples = Samples, saveDirectory = saveDirectory,
                    measurementNotes = "Editor CPU wall time in milliseconds; one untimed warm-up plus three samples per operation. " +
                        "Asset setup, shader import, world generation and initial full rebuild are outside measurements. " +
                        "Full baseline operations remain explicit so localized release changes can be compared in the same run."
                };
                result.paintFull1024 = Measure("ValleySurface.Paint(full 1024)", () => painted = ValleySurface.Paint(state.world, roads, 1024));
                result.uploadFullAlphamap = Measure("TerrainData.SetAlphamaps(full) + SetBaseMapDirty", () => {
                    terrain.SetAlphamaps(0, 0, painted); terrain.SetBaseMapDirty();
                });
                result.groundCoverFullRebuild = Measure("GroundCoverRenderer.Rebuild(full valley)", () =>
                    details.Rebuild(state.world, valley.DisplayHeight, roads, exclusions));
                result.synchronousSave = Measure("CareerSaveStore.Save (validate, serialize, fsync, atomic replace)", () => Save(store, state));
                result.oldReleaseFullSurfaceAndSave = Measure("Original release (full paint, upload, cover rebuild, synchronous save)", () => {
                    var weights = ValleySurface.Paint(state.world, roads, 1024);
                    terrain.SetAlphamaps(0, 0, weights); terrain.SetBaseMapDirty();
                    details.Rebuild(state.world, valley.DisplayHeight, roads, exclusions); Save(store, state);
                });
                MeasureQueuedSave(store, state, result);
                MeasurePlayerRelease(game, store, result);
                result.saveBytes = new FileInfo(Path.Combine(saveDirectory, "career.json")).Length;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllText(output, JsonUtility.ToJson(result, true));
                foreach (var timing in new[] { result.paintFull1024, result.uploadFullAlphamap, result.groundCoverFullRebuild,
                    result.synchronousSave, result.oldReleaseFullSurfaceAndSave, result.saveSnapshotQueue, result.saveWorkerCompletionWait,
                    result.playerBrushStamp, result.playerTreeRelease, result.localSurfaceTile, result.localSurfaceDrain })
                    if (timing != null) Debug.Log($"Clearing performance: {timing.name}: median {timing.medianMilliseconds:F2} ms, max {timing.maxMilliseconds:F2} ms");
                Debug.Log($"Clearing performance: {result.treeCount} trees, {result.groundCoverCount} detail instances; results {Path.GetFullPath(output)}");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static Timing Measure(string name, Action operation)
        {
            operation(); // Warm this exact path, including atomic file replacement and destination caches.
            var values = new double[Samples];
            for (int i = 0; i < values.Length; i++)
            {
                long start = Stopwatch.GetTimestamp(); operation();
                values[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            return Summarize(name, values);
        }
        static Timing Summarize(string name, double[] values)
        {
            var sorted = (double[])values.Clone(); Array.Sort(sorted);
            return new Timing { name = name, milliseconds = values, medianMilliseconds = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0,
                maxMilliseconds = sorted.Length > 0 ? sorted[sorted.Length - 1] : 0 };
        }
        static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

        static void MeasureQueuedSave(CareerSaveStore store, CareerState state, Result result)
        {
            var queue = new double[Samples]; var completion = new double[Samples];
            for (int sample = -1; sample < Samples; sample++)
            {
                long start = Stopwatch.GetTimestamp();
                if (!store.QueueSave(state, out string problem)) throw new InvalidOperationException(problem);
                double queueTime = Milliseconds(start);
                start = Stopwatch.GetTimestamp(); store.FlushPending(); double waitTime = Milliseconds(start);
                if (store.TryTakeSaveProblem(out problem)) throw new InvalidOperationException(problem);
                if (sample >= 0) { queue[sample] = queueTime; completion[sample] = waitTime; }
            }
            result.saveSnapshotQueue = Summarize("CareerSaveStore.QueueSave (detached snapshot + queue only)", queue);
            result.saveWorkerCompletionWait = Summarize("FlushPending wait for worker (outside release timing)", completion);
        }

        static void MeasurePlayerRelease(CareerGame game, CareerSaveStore store, Result result)
        {
            var stamp = new double[Samples]; var release = new double[Samples]; var drains = new double[Samples];
            var tiles = new List<double>();
            var details = game.valley.GetComponentInChildren<GroundCoverRenderer>();
            var existingDetails = details.Instances;
            int before = game.State.world.trees.Count(t => !t.removed);
            for (int sample = -1; sample < Samples; sample++)
            {
                // Four separate real strokes, including the warm-up, prevent a no-op repeat from hiding work.
                var hint = game.State.Site.centre + new Vector2(sample * 115f, -120f);
                var tree = game.State.world.trees.Where(t => !t.removed && game.State.Site.Parcel.Contains(t.position))
                    .OrderBy(t => (t.position - hint).sqrMagnitude).First();
                const float radius = 45f;
                var point = tree.position;
                var bounds = new Rect(point.x - radius, point.y - radius, radius * 2f, radius * 2f);
                long start = Stopwatch.GetTimestamp();
                if (!CareerConstruction.Clear(game.State, point, radius, out string problem)) throw new InvalidOperationException(problem);
                game.PlayerChanged(CareerTool.ClearTrees);
                double stampTime = Milliseconds(start);
                start = Stopwatch.GetTimestamp();
                game.FinishTerrainStroke(CareerTool.ClearTrees, bounds);
                double releaseTime = Milliseconds(start);
                start = Stopwatch.GetTimestamp();
                while (game.valley.PendingSurfaceTiles > 0)
                {
                    long tileStart = Stopwatch.GetTimestamp();
                    int pixels = game.valley.ProcessSurfaceUpdates(1);
                    double tileTime = Milliseconds(tileStart);
                    if (sample >= 0) { tiles.Add(tileTime); result.localSurfaceTileCount++; result.localSurfacePixels += pixels; }
                    if (pixels <= 0) throw new InvalidOperationException("Queued terrain work did not make progress.");
                }
                double drainTime = Milliseconds(start);
                if (!ReferenceEquals(existingDetails, details.Instances))
                    throw new InvalidOperationException("Tree-only clearing rebuilt the unrelated ground-cover instance list.");
                // Check durable completion without adding the disk worker's time to the mouse-release sample.
                store.FlushPending();
                if (store.TryTakeSaveProblem(out problem)) throw new InvalidOperationException(problem);
                if (sample >= 0) { stamp[sample] = stampTime; release[sample] = releaseTime; drains[sample] = drainTime; }
            }
            result.clearedTrees = before - game.State.world.trees.Count(t => !t.removed);
            result.groundCoverReferencePreserved = true;
            if (!store.Load(out var saved, out string saveProblem)) throw new InvalidOperationException(saveProblem);
            result.savedClearingMatchesState = saved.world.trees.Count(t => t.removed) == game.State.world.trees.Count(t => t.removed) && saved.funds == game.State.funds;
            if (!result.savedClearingMatchesState) throw new InvalidOperationException("The queued save did not preserve the finished tree strokes.");
            result.playerBrushStamp = Summarize("Real clear brush stamp + PlayerChanged", stamp);
            result.playerTreeRelease = Summarize("Real FinishTerrainStroke(ClearTrees) (queues local paint + autosave)", release);
            result.localSurfaceTile = Summarize("One ProcessSurfaceUpdates(1) terrain tile", tiles.ToArray());
            result.localSurfaceDrain = Summarize("All queued surface tiles combined (normally split across frames)", drains);
        }
        static void Save(CareerSaveStore store, CareerState state)
        {
            if (!store.Save(state, out string problem)) throw new InvalidOperationException(problem);
        }
        static T Get<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }
        static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            field.SetValue(target, value);
        }
        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
    }
}
