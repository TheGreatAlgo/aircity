using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Habitat-led, irregular stands with enough stems for overlapping mature crowns.</summary>
    public static class ForestPopulation
    {
        public const int Version = 2;
        public const float Spacing = 8f, TropicalSpacing = 6f;
        struct Candidate { public Vector2 position; public float height; }

        public static void Generate(ValleyWorld world)
        {
            Populate(world, null, false);
            world.forestVersion = Version;
        }

        /// <summary>Append only: saved roots, indices and removal flags never change or regenerate.</summary>
        public static bool Upgrade(ValleyWorld world, Func<Vector2, bool> protectedPoint = null)
        {
            if (world.forestVersion >= Version || world.generatorVersion < 4 ||
                world.landCover == null || world.landCover.Length != ValleyWorld.Resolution * ValleyWorld.Resolution) return false;
            // The second pass changes tropical density only. A woodland already upgraded to version 1
            // keeps every root and receives its new visual tree palette without another population pass.
            if (world.forestVersion == 1 && world.preset != RegionPreset.Tropical)
            { world.forestVersion = Version; return false; }
            Populate(world, protectedPoint, true);
            world.forestVersion = Version;
            return true;
        }

        static void Populate(ValleyWorld world, Func<Vector2, bool> protectedPoint, bool upgrading)
        {
            float spacing = world.preset == RegionPreset.Tropical ? TropicalSpacing : Spacing;
            int cells = Mathf.FloorToInt(ValleyWorld.Size / spacing);
            var blocked = new byte[ValleyWorld.Resolution * ValleyWorld.Resolution];
            // Reserve crowns beside roads and gardens, rather than scattering through pavement and houses.
            for (int i = 1; i < world.publicRoad.Count; i++)
                Block(world.publicRoad[i - 1], world.publicRoad[i], 24f);
            foreach (var town in world.settlements) Block(town, town, 145f);
            if (upgrading && world.disturbance != null && world.disturbance.Length == blocked.Length)
                for (int z = 0; z < ValleyWorld.Resolution; z++)
                for (int x = 0; x < ValleyWorld.Resolution; x++)
                    if (world.disturbance[z * ValleyWorld.Resolution + x] > 0f)
                        for (int dz = -2; dz <= 2; dz++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (nx >= 0 && nz >= 0 && nx < ValleyWorld.Resolution && nz < ValleyWorld.Resolution)
                                blocked[nz * ValleyWorld.Resolution + nx] = 1;
                        }

            var occupied = new byte[cells * cells];
            if (upgrading)
                foreach (var tree in world.trees)
                {
                    // Sparse old clearing strokes may predate disturbance marks. Keep a generous halo
                    // around removed roots as well, including when a whole old parcel was cleared.
                    float radius = tree.removed ? 32f : 4.5f;
                    int x0 = Cell(tree.position.x - radius, spacing, cells), x1 = Cell(tree.position.x + radius, spacing, cells);
                    int z0 = Cell(tree.position.y - radius, spacing, cells), z1 = Cell(tree.position.y + radius, spacing, cells);
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        if ((Position(x, z, world.seed, spacing) - tree.position).sqrMagnitude < radius * radius)
                            occupied[z * cells + x] = 1;
                }

            using var heights = new NativeArray<float>(world.heights, Allocator.TempJob);
            using var cover = new NativeArray<byte>(world.landCover, Allocator.TempJob);
            using var water = world.HasInlandWater ? new NativeArray<float>(world.InlandSurfaceHeights, Allocator.TempJob) : new NativeArray<float>(0, Allocator.TempJob);
            using var clear = new NativeArray<byte>(blocked, Allocator.TempJob);
            using var roots = new NativeArray<byte>(occupied, Allocator.TempJob);
            using var result = new NativeArray<Candidate>(cells * cells, Allocator.TempJob);
            new ScatterJob { seed = world.seed, preset = world.preset, hasOcean = world.HasOcean, seaLevel = world.SeaLevel,
                cells = cells, spacing = spacing, ecologicalSlopeScale = world.preset == RegionPreset.Tropical && world.sourceExtent > 0f && world.elevationScale > 0f ?
                    Mathf.Clamp(ValleyWorld.Size / world.sourceExtent / world.elevationScale, .4f, 1.4f) : 1f,
                heights = heights, cover = cover, water = water, blocked = clear, occupied = roots, result = result }
                .Schedule(result.Length, 256).Complete();
            var additions = new System.Collections.Generic.List<Candidate>();
            foreach (var candidate in result)
            {
                if (candidate.height <= 0f || protectedPoint != null && protectedPoint(candidate.position)) continue;
                additions.Add(candidate);
            }
            int available = Mathf.Max(0, ValleyWorld.MaxTrees - world.trees.Count);
            if (additions.Count > available)
            {
                // Entirely forested downloaded regions can hit the save budget. Sample across the
                // whole footprint instead of filling rows until the northern part is left empty.
                var random = new WorldRandom(world.seed ^ 479137);
                for (int i = 0; i < available; i++)
                {
                    int other = i + Mathf.Min(additions.Count - i - 1, (int)(random.Next() * (additions.Count - i)));
                    var swap = additions[i]; additions[i] = additions[other]; additions[other] = swap;
                }
            }
            for (int i = 0; i < Mathf.Min(available, additions.Count); i++)
                world.trees.Add(new ForestTree { position = additions[i].position, height = additions[i].height });

            void Block(Vector2 a, Vector2 b, float radius)
            {
                // Conservative source-grid mask: no candidate can slip between blocked samples.
                float reach = radius + world.Spacing * .72f;
                int x0 = Grid(Mathf.Min(a.x, b.x) - reach), x1 = Grid(Mathf.Max(a.x, b.x) + reach);
                int z0 = Grid(Mathf.Min(a.y, b.y) - reach), z1 = Grid(Mathf.Max(a.y, b.y) + reach);
                var d = b - a; float length = Mathf.Max(.001f, d.sqrMagnitude);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / length);
                    if ((p - a - d * t).sqrMagnitude < reach * reach) blocked[z * ValleyWorld.Resolution + x] = 1;
                }
            }
        }

        static int Cell(float value, float spacing, int cells) => Mathf.Clamp(Mathf.FloorToInt((value + ValleyWorld.Size * .5f) / spacing), 0, cells - 1);
        static int Grid(float value) => Mathf.Clamp(Mathf.RoundToInt((value + ValleyWorld.Size * .5f) / (ValleyWorld.Size / (ValleyWorld.Resolution - 1))), 0, ValleyWorld.Resolution - 1);
        static Vector2 Position(int x, int z, int seed, float spacing) => new Vector2(
            (x + .5f + (Unit(Hash(x, z, seed ^ 912843)) - .5f) * .8f) * spacing - ValleyWorld.Size * .5f,
            (z + .5f + (Unit(Hash(x, z, seed ^ 291737)) - .5f) * .8f) * spacing - ValleyWorld.Size * .5f);

        [BurstCompile]
        struct ScatterJob : IJobParallelFor
        {
            public int seed, cells;
            public RegionPreset preset;
            public bool hasOcean;
            public float seaLevel, spacing, ecologicalSlopeScale;
            [ReadOnly] public NativeArray<float> heights, water;
            [ReadOnly] public NativeArray<byte> cover, blocked, occupied;
            [WriteOnly] public NativeArray<Candidate> result;
            public void Execute(int index)
            {
                if (occupied[index] != 0) return;
                int x = index % cells, z = index / cells;
                var p = Position(x, z, seed, spacing);
                int grid = Grid(p.y) * ValleyWorld.Resolution + Grid(p.x);
                if (blocked[grid] != 0) return;
                byte habitat = cover[grid];
                float probability = habitat == 10 || habitat == 95 ? .96f : habitat == 20 ? .16f :
                    habitat == 30 ? .025f : habitat == 90 ? .10f : habitat == 60 ? .001f : 0f;
                if (probability <= 0f) return;
                // Broad stands and smaller gaps vary density continuously, without cutting large random
                // holes out of mapped forest. Mapped pasture, crops and towns retain their real openings.
                float stand = Noise(p / 180f, seed ^ 716237), gap = Noise(p / 38f, seed ^ 918127);
                bool tropical = preset == RegionPreset.Tropical;
                probability *= Mathf.Lerp(tropical ? .84f : .68f, 1f, Smooth(.2f, .7f, stand));
                probability *= Mathf.Lerp(tropical ? .6f : .30f, 1f, Smooth(.18f, .42f, gap));
                if (preset == RegionPreset.Desert) probability *= .35f;
                if (Unit(Hash(x, z, seed ^ 712237)) >= probability) return;
                float h = Sample(heights, p);
                // Low reef islands are real wooded land. Keep tropical roots above the wash zone
                // without applying the mainland's two-metre clearance to every narrow motu.
                if (hasOcean && h < seaLevel + (tropical ? .2f : 2f)) return;
                float wet = Sample(water, p, true);
                if (wet > 0f && h < wet + .65f) return;
                float d = ValleyWorld.Size / (ValleyWorld.Resolution - 1);
                float dx = (Sample(heights, p + Vector2.right * d) - Sample(heights, p - Vector2.right * d)) / (2f * d);
                float dz = (Sample(heights, p + Vector2.up * d) - Sample(heights, p - Vector2.up * d)) / (2f * d);
                float slope = Mathf.Sqrt(dx * dx + dz * dz);
                float ecologicalSlope = slope * ecologicalSlopeScale;
                if (slope > (tropical ? 1.65f : .90f) || h > (preset == RegionPreset.Mountains ? 360f : 560f)) return;
                if (Unit(Hash(x, z, seed ^ 519781)) < Smooth(tropical ? .65f : .40f, tropical ? 1.25f : .90f, ecologicalSlope) * (tropical ? .65f : .8f)) return;
                float age = Unit(Hash(x, z, seed ^ 976213));
                float height = preset == RegionPreset.Tropical ? Mathf.Lerp(14f, 27f, age) :
                    preset == RegionPreset.Desert ? Mathf.Lerp(6f, 11f, age) : Mathf.Lerp(18f, 32f, age);
                // A minority of younger trees break up a uniform roof without shrinking every crown.
                if (Unit(Hash(x, z, seed ^ 246191)) < .15f) height *= .65f;
                result[index] = new Candidate { position = p, height = height };
            }
        }

        static float Sample(NativeArray<float> values, Vector2 p, bool wetOnly = false)
        {
            if (values.Length == 0) return 0f;
            const int resolution = ValleyWorld.Resolution;
            float x = Mathf.Clamp((p.x + ValleyWorld.Size * .5f) / ValleyWorld.Size * (resolution - 1), 0f, resolution - 1.001f);
            float z = Mathf.Clamp((p.y + ValleyWorld.Size * .5f) / ValleyWorld.Size * (resolution - 1), 0f, resolution - 1.001f);
            int ix = (int)x, iz = (int)z; float u = x - ix, v = z - iz;
            float a = values[iz * resolution + ix], b = values[iz * resolution + ix + 1];
            float c = values[(iz + 1) * resolution + ix], d = values[(iz + 1) * resolution + ix + 1];
            if (!wetOnly) return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
            float wa = a > 0f ? (1f-u)*(1f-v) : 0f, wb = b > 0f ? u*(1f-v) : 0f;
            float wc = c > 0f ? (1f-u)*v : 0f, wd = d > 0f ? u*v : 0f, weight = wa+wb+wc+wd;
            return weight > .000001f ? (a*wa+b*wb+c*wc+d*wd)/weight : 0f;
        }
        static float Smooth(float a, float b, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, value));
        static uint Hash(int x, int z, int seed)
        {
            uint h = unchecked((uint)x * 374761393u + (uint)z * 668265263u + (uint)seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16);
        }
        static float Unit(uint hash) => (hash >> 8) / 16777216f;
        static float Noise(Vector2 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.y);
            float u = p.x-x, v = p.y-z; u=u*u*(3f-2f*u); v=v*v*(3f-2f*v);
            return Mathf.Lerp(Mathf.Lerp(Unit(Hash(x,z,seed)),Unit(Hash(x+1,z,seed)),u),
                Mathf.Lerp(Unit(Hash(x,z+1,seed)),Unit(Hash(x+1,z+1,seed)),u),v);
        }
    }
}
