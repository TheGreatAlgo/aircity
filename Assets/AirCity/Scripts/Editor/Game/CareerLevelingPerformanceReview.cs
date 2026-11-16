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
    /// <summary>Compare full-valley levelling with actual bounded player stamps and release work. Saves stay in Logs.</summary>
    public static class CareerLevelingPerformanceReview
    {
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
            public string utc, unityVersion, measurementNotes;
            public int trees, groundCoverInstances, heightmapResolution, alphamapResolution, surfaceTiles, groundCoverTiles;
            public bool roadsPreserved, savedTerrainMatches;
            public Timing oldStamp, newStamp, oldRelease, newRelease, deferredFrame;
        }
        const int Samples = 3;

        public static void Run()
        {
            try
            {
                NewGameSetup.UpdateWorldAssets();
                var game = Object.FindAnyObjectByType<CareerGame>();
                if (game == null) throw new InvalidOperationException("No career game in the New Game scene.");
                var store = new CareerSaveStore(Path.GetFullPath("Logs/CareerLevelingPerformanceReview"));
                typeof(CareerGame).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, store);
                var state = CareerState.New(42);
                if (!state.Build(out string why, 0)) throw new InvalidOperationException(why);
                var point = state.Site.centre;
                if (!CareerConstruction.Clear(state, point, 90f, out why)) throw new InvalidOperationException(why);
                game.Initialize(state, true);
                var view = game.valley;
                var details = view.GetComponentInChildren<GroundCoverRenderer>();
                const float radius = 45f;
                var bounds = new Rect(point.x - radius, point.y - radius, radius * 2f, radius * 2f);
                int stroke = 0;
                void Level()
                {
                    float target = state.Site.elevation + (++stroke % 2 == 0 ? 1f : 1.5f);
                    if (!CareerConstruction.Level(state, point, radius, target, true, out _, out string problem))
                        throw new InvalidOperationException(problem);
                }
                var result = new Result {
                    utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                    trees = state.world.trees.Count, groundCoverInstances = details.InstanceCount,
                    heightmapResolution = view.Terrain.terrainData.heightmapResolution,
                    alphamapResolution = view.Terrain.terrainData.alphamapResolution,
                    measurementNotes = "Warm editor CPU wall time, one warm-up and three samples. Real paid brush commits at alternating heights. " +
                        "Original stamp/release operations run on the same seeded world. DeferredFrame combines one surface tile and one prop tile. " +
                        "Initialization and disk-worker completion waits are outside measurements; these are not whole-frame timings."
                };
                result.oldStamp = Measure("Original stamp: level + full roads, heightmap and forest rebuild", () => { Level(); view.RefreshLand(false); });
                result.oldRelease = Measure("Original release: full surface and groundcover + queued save", () => { view.RefreshSurface(); game.QueueAutosave(); }, store.FlushPending);
                Mesh road = view.transform.Find("Public road network").GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Asphalt and junctions").sharedMesh;
                result.newStamp = Measure("Player stamp: level + local heightmap and surviving tree heights", () => { Level(); game.PlayerChanged(CareerTool.Level, bounds); });
                result.newRelease = Measure("Player release: queue local surface/props + detached save", () => game.FinishTerrainStroke(CareerTool.Level, bounds), store.FlushPending);
                result.roadsPreserved = road == view.transform.Find("Public road network").GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Asphalt and junctions").sharedMesh;
                result.surfaceTiles = view.PendingSurfaceTiles; result.groundCoverTiles = view.PendingGroundCoverTiles;
                var frames = new List<double>();
                for (int sample = -1; sample < Samples; sample++)
                {
                    view.QueueLevelSurface(bounds);
                    while (view.PendingSurfaceTiles > 0 || view.PendingGroundCoverTiles > 0)
                    {
                        long start = Stopwatch.GetTimestamp();
                        view.ProcessSurfaceUpdates(); view.ProcessGroundCoverUpdates();
                        if (sample >= 0) frames.Add(Milliseconds(start));
                    }
                }
                result.deferredFrame = Summarize("One deferred frame: at most one surface tile and one groundcover tile", frames.ToArray());
                store.FlushPending();
                if (store.TryTakeSaveProblem(out why) || !store.Load(out var saved, out why)) throw new InvalidOperationException(why);
                result.savedTerrainMatches = saved.funds == state.funds && saved.world.heights.SequenceEqual(state.world.heights) &&
                    saved.world.disturbance.SequenceEqual(state.world.disturbance);
                if (!result.roadsPreserved || !result.savedTerrainMatches) throw new InvalidOperationException("Road identity or durable terrain state changed unexpectedly.");
                const string output = "Logs/career-leveling-performance.json";
                File.WriteAllText(output, JsonUtility.ToJson(result, true));
                foreach (var timing in new[] { result.oldStamp, result.newStamp, result.oldRelease, result.newRelease, result.deferredFrame })
                    Debug.Log($"Levelling performance: {timing.name}: median {timing.medianMilliseconds:F2} ms, max {timing.maxMilliseconds:F2} ms");
                Debug.Log($"Levelling results: {Path.GetFullPath(output)}");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static Timing Measure(string name, Action action, Action after = null)
        {
            action(); after?.Invoke();
            var samples = new double[Samples];
            for (int i = 0; i < samples.Length; i++)
            {
                long start = Stopwatch.GetTimestamp(); action(); samples[i] = Milliseconds(start); after?.Invoke();
            }
            return Summarize(name, samples);
        }
        static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        static Timing Summarize(string name, double[] values)
        {
            var sorted = (double[])values.Clone(); Array.Sort(sorted);
            return new Timing { name = name, milliseconds = values, medianMilliseconds = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2],
                maxMilliseconds = sorted.Length == 0 ? 0 : sorted[sorted.Length - 1] };
        }
    }
}
