using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>A kept-clear capsule in world x/z. A coincident pair of ends describes a circle.</summary>
    [Serializable]
    public struct GroundCoverExclusion
    {
        public Vector2 a, b;
        public float radius;
        public GroundCoverExclusion(Vector2 a, Vector2 b, float radius) { this.a = a; this.b = b; this.radius = radius; }
    }

    public struct GroundCoverInstance
    {
        public Vector2 position;
        public GroundCoverKind kind;
        public float scale, yaw;
        public uint variation;
    }

    /// <summary>Stable local candidates: clearing one tree never rerolls the surrounding stones or understory.</summary>
    public static class GroundCoverScatter
    {
        public const float Spacing = 8f;
        const int CanopyResolution = 257;
        struct Candidate { public GroundCoverInstance item; public bool valid, cluster; }

        public static GroundCoverInstance[] Generate(ValleyWorld world, PublicRoadGeometry roads = null,
            IReadOnlyList<GroundCoverExclusion> exclusions = null, Rect? area = null)
        {
            Rect bounds = area ?? new Rect(-ValleyWorld.Size * .5f, -ValleyWorld.Size * .5f, ValleyWorld.Size, ValleyWorld.Size);
            // Include adjacent parent cells: a cluster may reach into this area from just outside it.
            Rect parentBounds = Rect.MinMaxRect(bounds.xMin - 3.5f, bounds.yMin - 3.5f, bounds.xMax + 3.5f, bounds.yMax + 3.5f);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((parentBounds.xMin + ValleyWorld.Size * .5f) / Spacing), 0, 799);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((parentBounds.yMin + ValleyWorld.Size * .5f) / Spacing), 0, 799);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((parentBounds.xMax + ValleyWorld.Size * .5f) / Spacing), 1, 800);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((parentBounds.yMax + ValleyWorld.Size * .5f) / Spacing), 1, 800);
            int width = Mathf.Max(0, x1 - x0), depth = Mathf.Max(0, z1 - z0);
            if (width == 0 || depth == 0) return Array.Empty<GroundCoverInstance>();
            using var heights = new NativeArray<float>(world.heights, Allocator.TempJob);
            using var disturbance = world.disturbance != null && world.disturbance.Length == world.heights.Length ?
                new NativeArray<float>(world.disturbance, Allocator.TempJob) : new NativeArray<float>(0, Allocator.TempJob);
            using var canopy = new NativeArray<float>(CanopyResolution * CanopyResolution, Allocator.TempJob);
            var trees = new Vector2[world.trees.Count];
            // Even removed trees describe the original habitat. Clearing leaves its smaller plants in place.
            for (int i = 0; i < trees.Length; i++) trees[i] = world.trees[i].position;
            using var treeData = new NativeArray<Vector2>(trees, Allocator.TempJob);
            var keptClear = new List<GroundCoverExclusion>();
            if (roads != null)
                foreach (var road in roads.links)
                    if (road.structure == RoadStructure.Ground) keptClear.Add(new GroundCoverExclusion(road.a, road.b, road.halfWidth + 2f));
            if (exclusions != null) for (int i = 0; i < exclusions.Count; i++) keptClear.Add(exclusions[i]);
            // Keep gardens and access to the generated settlements clear too.
            foreach (var town in world.settlements) keptClear.Add(new GroundCoverExclusion(town, town, 145f));
            using var clearData = new NativeArray<GroundCoverExclusion>(keptClear.ToArray(), Allocator.TempJob);
            using var candidates = new NativeArray<Candidate>(width * depth, Allocator.TempJob);
            var habitat = new CanopyJob { trees = treeData, canopy = canopy }.Schedule();
            new ScatterJob { seed = world.seed, x0 = x0, z0 = z0, width = width, heights = heights,
                disturbance = disturbance, canopy = canopy, clear = clearData, candidates = candidates, bounds = parentBounds }
                .Schedule(candidates.Length, 256, habitat).Complete();
            var result = new List<GroundCoverInstance>(candidates.Length / 3);
            var clusterParents = new List<GroundCoverInstance>(candidates.Length / 12);
            foreach (var candidate in candidates)
            {
                if (!candidate.valid) continue;
                if (bounds.Contains(candidate.item.position)) result.Add(candidate.item);
                if (candidate.cluster) clusterParents.Add(candidate.item);
            }
            using var parentData = new NativeArray<GroundCoverInstance>(clusterParents.ToArray(), Allocator.TempJob);
            using var satellites = new NativeArray<Candidate>(clusterParents.Count * 2, Allocator.TempJob);
            new ClusterJob { parents = parentData, heights = heights, disturbance = disturbance, clear = clearData,
                candidates = satellites, seed = world.seed }.Schedule(satellites.Length, 256).Complete();
            foreach (var satellite in satellites)
                if (satellite.valid && bounds.Contains(satellite.item.position)) result.Add(satellite.item);
            return result.ToArray();
        }

        public static float FootprintRadius(GroundCoverKind kind, float scale)
        {
            float radius = kind == GroundCoverKind.Boulder ? 1.8f : kind == GroundCoverKind.Stone ? .45f :
                kind == GroundCoverKind.Grass ? 1.65f : kind == GroundCoverKind.Fern ? .9f : 1.1f;
            return radius * scale;
        }

        [BurstCompile]
        struct CanopyJob : IJob
        {
            [ReadOnly] public NativeArray<Vector2> trees;
            public NativeArray<float> canopy;
            public void Execute()
            {
                const float spacing = ValleyWorld.Size / (CanopyResolution - 1);
                foreach (var tree in trees)
                {
                    int cx = Mathf.RoundToInt((tree.x + ValleyWorld.Size * .5f) / spacing), cz = Mathf.RoundToInt((tree.y + ValleyWorld.Size * .5f) / spacing);
                    for (int z = Mathf.Max(0, cz - 2); z <= Mathf.Min(CanopyResolution - 1, cz + 2); z++)
                    for (int x = Mathf.Max(0, cx - 2); x <= Mathf.Min(CanopyResolution - 1, cx + 2); x++)
                    {
                        var p = new Vector2(x * spacing - ValleyWorld.Size * .5f, z * spacing - ValleyWorld.Size * .5f);
                        float cover = Mathf.Clamp01(1f - Vector2.Distance(p, tree) / 34f);
                        int index = z * CanopyResolution + x; canopy[index] = Mathf.Max(canopy[index], cover);
                    }
                }
            }
        }

        [BurstCompile]
        struct ScatterJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> heights, disturbance, canopy;
            [ReadOnly] public NativeArray<GroundCoverExclusion> clear;
            [WriteOnly] public NativeArray<Candidate> candidates;
            public int seed, x0, z0, width;
            public Rect bounds;
            public void Execute(int index)
            {
                int x = index % width + x0, z = index / width + z0;
                uint hash = Hash(x, z, seed);
                var p = new Vector2((x + Unit(hash)) * Spacing - ValleyWorld.Size * .5f,
                    (z + Unit(Hash(x, z, seed ^ 639219))) * Spacing - ValleyWorld.Size * .5f);
                if (!bounds.Contains(p)) return;
                float h = Sample(heights, ValleyWorld.Resolution, p);
                if (h < WaterHeight(p.y) + .6f || Mathf.Abs(p.x - RiverX(p.y)) < 60f) return;
                // Tree work only leaves up to .3 disturbance. Actual grading removes all nature props.
                if (disturbance.Length != 0 && Sample(disturbance, ValleyWorld.Resolution, p, true) > .42f) return;
                float canopyWeight = Sample(canopy, CanopyResolution, p);
                float patch = Noise(p / 85f, seed ^ 239085), broad = Noise(p / 260f, seed ^ 461203);
                float roll = Unit(Hash(x, z, seed ^ 973217));
                GroundCoverKind kind;
                if (roll < .0065f && patch > .42f) kind = GroundCoverKind.Boulder;
                else if (roll < .033f && broad > .3f) kind = GroundCoverKind.Stone;
                else if (roll < .077f && patch > .4f) kind = GroundCoverKind.Shrub;
                else if (roll < .17f && canopyWeight > .28f && patch > .33f) kind = GroundCoverKind.Fern;
                else if (roll < Mathf.Lerp(.72f, .38f, canopyWeight) && patch > .28f) kind = GroundCoverKind.Grass;
                else return;
                const float d = ValleyWorld.Size / (ValleyWorld.Resolution - 1);
                float dx = (Sample(heights, ValleyWorld.Resolution, p + Vector2.right * d) - Sample(heights, ValleyWorld.Resolution, p - Vector2.right * d)) / (2f * d);
                float dz = (Sample(heights, ValleyWorld.Resolution, p + Vector2.up * d) - Sample(heights, ValleyWorld.Resolution, p - Vector2.up * d)) / (2f * d);
                if (dx * dx + dz * dz > (kind == GroundCoverKind.Boulder || kind == GroundCoverKind.Stone ? 1.5f : .45f)) return;
                if (h > 355f && kind != GroundCoverKind.Boulder && kind != GroundCoverKind.Stone) return;
                float size = Unit(Hash(x, z, seed ^ 531931));
                float scale = kind == GroundCoverKind.Boulder ? Mathf.Lerp(.65f, 2.3f, size * size) : kind == GroundCoverKind.Stone ?
                    Mathf.Lerp(.6f, 1.8f, size) : kind == GroundCoverKind.Grass ? Mathf.Lerp(.75f, 1.6f, size) : Mathf.Lerp(.6f, 1.5f, size);
                float radius = FootprintRadius(kind, scale);
                foreach (var area in clear)
                {
                    var direction = area.b - area.a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - area.a, direction) / Mathf.Max(.0001f, direction.sqrMagnitude));
                    float limit = area.radius + radius;
                    if ((p - (area.a + direction * t)).sqrMagnitude < limit * limit) return;
                }
                candidates[index] = new Candidate { valid = true,
                    cluster = kind != GroundCoverKind.Boulder && kind != GroundCoverKind.Stone &&
                        Unit(Hash(x, z, seed ^ 442913)) < Mathf.Lerp(.20f, .38f, canopyWeight),
                    item = new GroundCoverInstance {
                    position = p, kind = kind, scale = scale, yaw = Unit(Hash(x, z, seed ^ 345113)) * 360f,
                    variation = Hash(x, z, seed ^ 663997) } };
            }
            float RiverX(float z) => -1530f + Mathf.Sin(z * .0011f + (seed % 97) * .02f) * 130f;
            static float WaterHeight(float z) => 48f + (z + ValleyWorld.Size * .5f) * .0014f;
        }

        [BurstCompile]
        struct ClusterJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroundCoverInstance> parents;
            [ReadOnly] public NativeArray<float> heights, disturbance;
            [ReadOnly] public NativeArray<GroundCoverExclusion> clear;
            [WriteOnly] public NativeArray<Candidate> candidates;
            public int seed;
            public void Execute(int index)
            {
                var parent = parents[index / 2]; int satellite = index % 2;
                int key = unchecked((int)parent.variation);
                float angle = Unit(Hash(key, satellite, 732157)) * Mathf.PI * 2f;
                float reach = Mathf.Lerp(.75f, 3.25f, Unit(Hash(key, satellite, 564319)));
                var p = parent.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
                if (Mathf.Abs(p.x) > ValleyWorld.Size * .5f || Mathf.Abs(p.y) > ValleyWorld.Size * .5f) return;
                float h = Sample(heights, ValleyWorld.Resolution, p);
                float river = -1530f + Mathf.Sin(p.y * .0011f + (seed % 97) * .02f) * 130f;
                if (h < 48.6f + (p.y + ValleyWorld.Size * .5f) * .0014f || Mathf.Abs(p.x - river) < 60f) return;
                if (disturbance.Length != 0 && Sample(disturbance, ValleyWorld.Resolution, p, true) > .42f) return;
                GroundCoverKind kind = parent.kind == GroundCoverKind.Shrub && satellite == 1 ? GroundCoverKind.Grass : parent.kind;
                float scale = parent.scale * Mathf.Lerp(.5f, .95f, Unit(Hash(key, satellite, 612497)));
                float radius = FootprintRadius(kind, scale);
                foreach (var area in clear)
                {
                    var direction = area.b - area.a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - area.a, direction) / Mathf.Max(.0001f, direction.sqrMagnitude));
                    float limit = area.radius + radius;
                    if ((p - (area.a + direction * t)).sqrMagnitude < limit * limit) return;
                }
                const float d = ValleyWorld.Size / (ValleyWorld.Resolution - 1);
                float dx = (Sample(heights, ValleyWorld.Resolution, p + Vector2.right * d) - Sample(heights, ValleyWorld.Resolution, p - Vector2.right * d)) / (2f * d);
                float dz = (Sample(heights, ValleyWorld.Resolution, p + Vector2.up * d) - Sample(heights, ValleyWorld.Resolution, p - Vector2.up * d)) / (2f * d);
                if (dx * dx + dz * dz > .45f || h > 355f) return;
                candidates[index] = new Candidate { valid = true, item = new GroundCoverInstance {
                    position = p, kind = kind, scale = scale, yaw = Unit(Hash(key, satellite, 937531)) * 360f,
                    variation = Hash(key, satellite, 296317) } };
            }
        }

        static float Sample(NativeArray<float> values, int resolution, Vector2 p, bool earthworkOnly = false)
        {
            float x = Mathf.Clamp((p.x + ValleyWorld.Size * .5f) / ValleyWorld.Size * (resolution - 1), 0f, resolution - 1.001f);
            float z = Mathf.Clamp((p.y + ValleyWorld.Size * .5f) / ValleyWorld.Size * (resolution - 1), 0f, resolution - 1.001f);
            int ix = (int)x, iz = (int)z;
            float a = values[iz * resolution + ix], b = values[iz * resolution + ix + 1];
            float c = values[(iz + 1) * resolution + ix], d = values[(iz + 1) * resolution + ix + 1];
            if (earthworkOnly)
            {
                // Tree work contributes at most .3 per saved sample. Discard those samples before blending:
                // adding a tree mark beside a graded sample must not turn surviving undergrowth into earthwork.
                if (a <= .3f) a = 0f; if (b <= .3f) b = 0f;
                if (c <= .3f) c = 0f; if (d <= .3f) d = 0f;
            }
            return Mathf.Lerp(Mathf.Lerp(a, b, x - ix), Mathf.Lerp(c, d, x - ix), z - iz);
        }
        static uint Hash(int x, int z, int seed)
        {
            uint h = unchecked((uint)x * 374761393u + (uint)z * 668265263u + (uint)seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16);
        }
        static float Unit(uint hash) => (hash >> 8) / 16777216f;
        static float Noise(Vector2 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.y);
            float u = p.x - x, v = p.y - z; u = u * u * (3f - 2f * u); v = v * v * (3f - 2f * v);
            return Mathf.Lerp(Mathf.Lerp(Unit(Hash(x, z, seed)), Unit(Hash(x + 1, z, seed)), u),
                Mathf.Lerp(Unit(Hash(x, z + 1, seed)), Unit(Hash(x + 1, z + 1, seed)), u), v);
        }
    }
}
