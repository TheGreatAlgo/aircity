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
        // One stable primary/satellite slot per source cell, independent of model, habitat or tile rebuild.
        public int id;
        public Vector2 position;
        public GroundCoverKind kind;
        public float scale, yaw;
        public uint variation;
    }

    /// <summary>Stable local candidates: clearing one tree never rerolls the surrounding stones or understory.</summary>
    public static class GroundCoverScatter
    {
        public const float Spacing = 8f;
        public const int MaxInstanceId = 800 * 800 * 3;
        public const float MaximumFootprintRadius = 13f;
        const int CanopyResolution = 257;
        struct Candidate { public GroundCoverInstance item; public bool valid, cluster; }

        public static GroundCoverInstance[] Generate(ValleyWorld world, PublicRoadGeometry roads = null,
            IReadOnlyList<GroundCoverExclusion> exclusions = null, Rect? area = null, BuiltSurfaceMap surfaces = null)
        {
            Rect bounds = area ?? new Rect(-ValleyWorld.Size * .5f, -ValleyWorld.Size * .5f, ValleyWorld.Size, ValleyWorld.Size);
            // Include adjacent parent cells: a cluster may reach into this area from just outside it.
            Rect parentBounds = Rect.MinMaxRect(bounds.xMin - 9.5f, bounds.yMin - 9.5f, bounds.xMax + 9.5f, bounds.yMax + 9.5f);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((parentBounds.xMin + ValleyWorld.Size * .5f) / Spacing), 0, 799);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((parentBounds.yMin + ValleyWorld.Size * .5f) / Spacing), 0, 799);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((parentBounds.xMax + ValleyWorld.Size * .5f) / Spacing), 1, 800);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((parentBounds.yMax + ValleyWorld.Size * .5f) / Spacing), 1, 800);
            int width = Mathf.Max(0, x1 - x0), depth = Mathf.Max(0, z1 - z0);
            if (width == 0 || depth == 0) return Array.Empty<GroundCoverInstance>();
            using var heights = new NativeArray<float>(world.heights, Allocator.TempJob);
            using var waterHeights = world.HasInlandWater ? new NativeArray<float>(world.InlandSurfaceHeights, Allocator.TempJob) :
                new NativeArray<float>(0, Allocator.TempJob);
            using var disturbance = world.disturbance != null && world.disturbance.Length == world.heights.Length ?
                new NativeArray<float>(world.disturbance, Allocator.TempJob) : new NativeArray<float>(0, Allocator.TempJob);
            using var landCover = world.landCover != null && world.landCover.Length == world.heights.Length ?
                new NativeArray<byte>(world.landCover, Allocator.TempJob) : new NativeArray<byte>(0, Allocator.TempJob);
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
            new ScatterJob { seed = world.seed, river = world.River, preset = world.preset, hasRiver = world.HasRiver, hasOcean = world.HasOcean, seaLevel = world.SeaLevel, x0 = x0, z0 = z0, width = width, heights = heights,
                waterHeights = waterHeights, disturbance = disturbance, landCover = landCover, canopy = canopy, clear = clearData, candidates = candidates, bounds = parentBounds }
                .Schedule(candidates.Length, 256, habitat).Complete();
            var result = new List<GroundCoverInstance>(candidates.Length / 3);
            var clusterParents = new List<GroundCoverInstance>(candidates.Length / 12);
            foreach (var candidate in candidates)
            {
                if (!candidate.valid) continue;
                if (bounds.Contains(candidate.item.position) && !world.GroundPropRemoved(candidate.item.id)) result.Add(candidate.item);
                if (candidate.cluster) clusterParents.Add(candidate.item);
            }
            using var parentData = new NativeArray<GroundCoverInstance>(clusterParents.ToArray(), Allocator.TempJob);
            using var satellites = new NativeArray<Candidate>(clusterParents.Count * 2, Allocator.TempJob);
            new ClusterJob { parents = parentData, heights = heights, waterHeights = waterHeights, disturbance = disturbance, clear = clearData,
                candidates = satellites, landCover = landCover, seed = world.seed, river = world.River, hasRiver = world.HasRiver, hasOcean = world.HasOcean, seaLevel = world.SeaLevel }.Schedule(satellites.Length, 256).Complete();
            foreach (var satellite in satellites)
                if (satellite.valid && bounds.Contains(satellite.item.position) && !world.GroundPropRemoved(satellite.item.id)) result.Add(satellite.item);
            return surfaces!=null ? surfaces.Exclude(result) : result.ToArray();
        }

        public static float FootprintRadius(GroundCoverKind kind, float scale)
        {
            // Bound the full model corner under nonuniform width and slope rotation, not just its
            // upright ground outline. This keeps a tilted crown/log end out of construction clearances.
            float radius = kind == GroundCoverKind.Boulder ? 3.2f : kind == GroundCoverKind.Stone ? .65f : kind == GroundCoverKind.Log ? 5f :
                kind == GroundCoverKind.Grass ? 2.5f : kind == GroundCoverKind.Fern ? 2f : 2.5f;
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
            [ReadOnly] public NativeArray<float> heights, waterHeights, disturbance, canopy;
            [ReadOnly] public NativeArray<byte> landCover;
            [ReadOnly] public NativeArray<GroundCoverExclusion> clear;
            [WriteOnly] public NativeArray<Candidate> candidates;
            public int seed, x0, z0, width;
            public RiverCourse river;
            public bool hasRiver, hasOcean;
            public float seaLevel;
            public RegionPreset preset;
            public Rect bounds;
            public void Execute(int index)
            {
                int x = index % width + x0, z = index / width + z0;
                uint hash = Hash(x, z, seed);
                var p = new Vector2((x + Unit(hash)) * Spacing - ValleyWorld.Size * .5f,
                    (z + Unit(Hash(x, z, seed ^ 639219))) * Spacing - ValleyWorld.Size * .5f);
                if (!bounds.Contains(p)) return;
                byte habitat = Habitat(landCover, p);
                if (habitat == 50 || habitat == 40 || habitat == 80) return;
                float h = Sample(heights, ValleyWorld.Resolution, p);
                if (hasOcean && h <= seaLevel + 1.5f || hasRiver && (h < river.WaterHeight(p.y) + .6f || Mathf.Abs(p.x - river.X(p.y)) < river.halfWidth)) return;
                float inland = WaterLevel(waterHeights, p);
                if (inland > 0f && h <= inland + .6f) return;
                // Tree work only leaves up to .3 disturbance. Actual grading removes all nature props.
                if (disturbance.Length != 0 && Sample(disturbance, ValleyWorld.Resolution, p, true) > .42f) return;
                float canopyWeight = Sample(canopy, CanopyResolution, p);
                float patch = Noise(p / 85f, seed ^ 239085), broad = Noise(p / 260f, seed ^ 461203);
                float outcrop = Noise(p / 52f, seed ^ 820317);
                float roll = Unit(Hash(x, z, seed ^ 973217));
                const float d = ValleyWorld.Size / (ValleyWorld.Resolution - 1);
                float dx = (Sample(heights, ValleyWorld.Resolution, p + Vector2.right * d) - Sample(heights, ValleyWorld.Resolution, p - Vector2.right * d)) / (2f * d);
                float dz = (Sample(heights, ValleyWorld.Resolution, p + Vector2.up * d) - Sample(heights, ValleyWorld.Resolution, p - Vector2.up * d)) / (2f * d);
                float slope = dx * dx + dz * dz;
                // Geology appears in uneven outcrops, particularly on rocky slopes and bare land.
                // Smaller stones then gather at their feet instead of covering every meadow uniformly.
                float rockWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.51f, .70f, outcrop));
                if (habitat == 60 || slope > .18f) rockWeight = Mathf.Max(rockWeight, .48f);
                float boulderChance = Mathf.Lerp(.005f, .095f, rockWeight);
                float stoneChance = boulderChance + Mathf.Lerp(.018f, .075f, rockWeight);
                GroundCoverKind kind;
                if (roll < boulderChance) kind = GroundCoverKind.Boulder;
                else if (roll < stoneChance) kind = GroundCoverKind.Stone;
                else if (roll < stoneChance + .009f && canopyWeight > .28f && broad > .38f && preset != RegionPreset.Desert) kind = GroundCoverKind.Log;
                else if (roll < stoneChance + .080f && patch > .32f) kind = GroundCoverKind.Shrub;
                else if (roll < stoneChance + .22f && canopyWeight > .28f && patch > .28f) kind = GroundCoverKind.Fern;
                else if (roll < Mathf.Lerp(.68f, .40f, canopyWeight) && patch > .28f) kind = GroundCoverKind.Grass;
                else return;
                if (preset == RegionPreset.Desert && kind != GroundCoverKind.Boulder && kind != GroundCoverKind.Stone &&
                    (kind != GroundCoverKind.Shrub || roll > .04f)) return;
                if (slope > (kind == GroundCoverKind.Boulder || kind == GroundCoverKind.Stone ? 2.25f : kind == GroundCoverKind.Log ? .32f : .65f)) return;
                if (h > 355f && kind != GroundCoverKind.Boulder && kind != GroundCoverKind.Stone) return;
                float size = Unit(Hash(x, z, seed ^ 531931));
                float scale = kind == GroundCoverKind.Boulder ? Mathf.Lerp(.85f, Mathf.Lerp(2f, 4f, rockWeight), size * size) : kind == GroundCoverKind.Stone ?
                    Mathf.Lerp(.8f, 2.8f, size) : kind == GroundCoverKind.Log ? Mathf.Lerp(.65f, 1.35f, size) :
                    kind == GroundCoverKind.Grass ? Mathf.Lerp(.75f, 1.45f, size) :
                    kind == GroundCoverKind.Fern ? Mathf.Lerp(.9f, preset == RegionPreset.Tropical ? 2.3f : 1.8f, size) : Mathf.Lerp(.85f, 2.1f, size);
                float radius = FootprintRadius(kind, scale);
                foreach (var area in clear)
                {
                    var direction = area.b - area.a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - area.a, direction) / Mathf.Max(.0001f, direction.sqrMagnitude));
                    float limit = area.radius + radius;
                    if ((p - (area.a + direction * t)).sqrMagnitude < limit * limit) return;
                }
                candidates[index] = new Candidate { valid = true,
                    cluster = kind == GroundCoverKind.Boulder || kind != GroundCoverKind.Stone && kind != GroundCoverKind.Log &&
                        Unit(Hash(x, z, seed ^ 442913)) < Mathf.Lerp(.20f, .46f, canopyWeight),
                    item = new GroundCoverInstance {
                    id = (z * 800 + x) * 3 + 1,
                    position = p, kind = kind, scale = scale, yaw = Unit(Hash(x, z, seed ^ 345113)) * 360f,
                    variation = Hash(x, z, seed ^ 663997) } };
            }
        }

        [BurstCompile]
        struct ClusterJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroundCoverInstance> parents;
            [ReadOnly] public NativeArray<float> heights, waterHeights, disturbance;
            [ReadOnly] public NativeArray<byte> landCover;
            [ReadOnly] public NativeArray<GroundCoverExclusion> clear;
            [WriteOnly] public NativeArray<Candidate> candidates;
            public int seed;
            public RiverCourse river;
            public bool hasRiver, hasOcean;
            public float seaLevel;
            public void Execute(int index)
            {
                var parent = parents[index / 2]; int satellite = index % 2;
                int key = unchecked((int)parent.variation);
                float angle = Unit(Hash(key, satellite, 732157)) * Mathf.PI * 2f;
                bool rockCluster = parent.kind == GroundCoverKind.Boulder;
                float reach = rockCluster ? Mathf.Lerp(2.3f, 9f, Unit(Hash(key, satellite, 564319))) :
                    Mathf.Lerp(.75f, 3.25f, Unit(Hash(key, satellite, 564319)));
                var p = parent.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
                if (Mathf.Abs(p.x) > ValleyWorld.Size * .5f || Mathf.Abs(p.y) > ValleyWorld.Size * .5f) return;
                byte habitat = Habitat(landCover, p);
                if (habitat == 50 || habitat == 40 || habitat == 80) return;
                float h = Sample(heights, ValleyWorld.Resolution, p);
                if (hasOcean && h <= seaLevel + 1.5f || hasRiver && (h < river.WaterHeight(p.y) + .6f || Mathf.Abs(p.x - river.X(p.y)) < river.halfWidth)) return;
                float inland = WaterLevel(waterHeights, p);
                if (inland > 0f && h <= inland + .6f) return;
                if (disturbance.Length != 0 && Sample(disturbance, ValleyWorld.Resolution, p, true) > .42f) return;
                GroundCoverKind kind = rockCluster ? GroundCoverKind.Stone : parent.kind == GroundCoverKind.Shrub && satellite == 1 ? GroundCoverKind.Grass : parent.kind;
                float scale = rockCluster ? Mathf.Lerp(1.5f, 4f, Unit(Hash(key, satellite, 612497))) :
                    parent.scale * Mathf.Lerp(.5f, .95f, Unit(Hash(key, satellite, 612497)));
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
                if (dx * dx + dz * dz > (rockCluster ? 2.25f : .65f) || h > 355f && !rockCluster) return;
                candidates[index] = new Candidate { valid = true, item = new GroundCoverInstance {
                    id = parent.id + satellite + 1,
                    position = p, kind = kind, scale = scale, yaw = Unit(Hash(key, satellite, 937531)) * 360f,
                    variation = Hash(key, satellite, 296317) } };
            }
        }

        static byte Habitat(NativeArray<byte> cover, Vector2 p)
        {
            if (cover.Length == 0) return 0;
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x + ValleyWorld.Size * .5f) / (ValleyWorld.Size / (ValleyWorld.Resolution - 1))), 0, ValleyWorld.Resolution - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.y + ValleyWorld.Size * .5f) / (ValleyWorld.Size / (ValleyWorld.Resolution - 1))), 0, ValleyWorld.Resolution - 1);
            return cover[z * ValleyWorld.Resolution + x];
        }

        static float WaterLevel(NativeArray<float> values, Vector2 p)
        {
            if (values.Length == 0) return 0f;
            const int resolution = ValleyWorld.Resolution;
            float x = Mathf.Clamp((p.x + ValleyWorld.Size * .5f) / ValleyWorld.Size * (resolution - 1), 0f, resolution - 1.001f);
            float z = Mathf.Clamp((p.y + ValleyWorld.Size * .5f) / ValleyWorld.Size * (resolution - 1), 0f, resolution - 1.001f);
            int ix = (int)x, iz = (int)z; float u = x - ix, v = z - iz;
            float a = values[iz * resolution + ix], b = values[iz * resolution + ix + 1];
            float c = values[(iz + 1) * resolution + ix], d = values[(iz + 1) * resolution + ix + 1];
            float wa = a > 0f ? (1f-u)*(1f-v) : 0f, wb = b > 0f ? u*(1f-v) : 0f;
            float wc = c > 0f ? (1f-u)*v : 0f, wd = d > 0f ? u*v : 0f;
            float weight = wa+wb+wc+wd;
            return weight > .000001f ? (a*wa+b*wb+c*wc+d*wd)/weight : 0f;
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
