using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Airport-scale soil, meadow and canopy variation. Pixel and canopy work runs in Burst jobs.</summary>
    public static class ValleySurface
    {
        public struct Weights { public float grass, earth, road, dry, rock, litter; }
        struct Cover { public Vector2 point; }
        struct Road { public Vector2 a, b; public float halfWidth; }

        public static float[,,] Paint(ValleyWorld world, PublicRoadGeometry roads, int resolution)
            => PaintRegion(world, roads, resolution, new RectInt(0, 0, resolution, resolution));

        public static RectInt PixelBounds(Rect area, int resolution, float margin = 0f)
        {
            float scale = (resolution - 1) / ValleyWorld.Size;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - margin + ValleyWorld.Size * .5f) * scale), 0, resolution);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - margin + ValleyWorld.Size * .5f) * scale), 0, resolution);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax + margin + ValleyWorld.Size * .5f) * scale) + 1, 0, resolution);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax + margin + ValleyWorld.Size * .5f) * scale) + 1, 0, resolution);
            return new RectInt(x0, z0, Mathf.Max(0, x1 - x0), Mathf.Max(0, z1 - z0));
        }

        /// <summary>Samples the same global field as Paint, but allocates and paints only a dirty rectangle.</summary>
        public static float[,,] PaintRegion(ValleyWorld world, PublicRoadGeometry roads, int resolution, RectInt region)
        {
            region = new RectInt(Mathf.Clamp(region.xMin, 0, resolution), Mathf.Clamp(region.yMin, 0, resolution),
                Mathf.Max(0, Mathf.Min(resolution, region.xMax) - Mathf.Max(0, region.xMin)),
                Mathf.Max(0, Mathf.Min(resolution, region.yMax) - Mathf.Max(0, region.yMin)));
            if (region.width == 0 || region.height == 0) return new float[0, 0, 6];
            using var heights = new NativeArray<float>(world.heights, Allocator.TempJob);
            using var edits = world.disturbance != null && world.disturbance.Length == world.heights.Length ? new NativeArray<float>(world.disturbance, Allocator.TempJob) :
                new NativeArray<float>(world.heights.Length, Allocator.TempJob);
            using var canopy = new NativeArray<float>(world.heights.Length, Allocator.TempJob);
            var treeList = new System.Collections.Generic.List<Cover>();
            float metres = ValleyWorld.Size / (resolution - 1);
            // Canopy values interpolate between height samples. Include their full support beyond this patch.
            float halo = 28f + 2f * world.Spacing;
            var coverBounds = Rect.MinMaxRect(region.xMin * metres - ValleyWorld.Size * .5f - halo,
                region.yMin * metres - ValleyWorld.Size * .5f - halo,
                (region.xMax - 1) * metres - ValleyWorld.Size * .5f + halo,
                (region.yMax - 1) * metres - ValleyWorld.Size * .5f + halo);
            foreach (var tree in world.trees)
                if (!tree.removed && coverBounds.Contains(tree.position)) treeList.Add(new Cover { point = tree.position });
            using var treeData = new NativeArray<Cover>(treeList.ToArray(), Allocator.TempJob);
            var roadList = new System.Collections.Generic.List<Road>();
            foreach (var link in roads.links) if (link.structure == RoadStructure.Ground) roadList.Add(new Road { a = link.a, b = link.b, halfWidth = link.halfWidth });
            using var roadData = new NativeArray<Road>(roadList.ToArray(), Allocator.TempJob);
            using var weights = new NativeArray<Weights>(region.width * region.height, Allocator.TempJob);
            var cover = new CanopyJob { trees = treeData, canopy = canopy }.Schedule();
            new PaintJob { heights = heights, edits = edits, canopy = canopy, roads = roadData, output = weights,
                resolution = resolution, width = region.width, x0 = region.x, z0 = region.y,
                seed = world.seed }.Schedule(weights.Length, 256, cover).Complete();
            var result = new float[region.height, region.width, 6];
            for (int z = 0; z < region.height; z++)
            for (int x = 0; x < region.width; x++)
            {
                var w = weights[z * region.width + x];
                result[z, x, 0] = w.grass; result[z, x, 1] = w.earth; result[z, x, 2] = w.road;
                result[z, x, 3] = w.dry; result[z, x, 4] = w.rock; result[z, x, 5] = w.litter;
            }
            return result;
        }

        [BurstCompile]
        struct CanopyJob : IJob
        {
            [ReadOnly] public NativeArray<Cover> trees;
            public NativeArray<float> canopy;
            public void Execute()
            {
                const int res = ValleyWorld.Resolution;
                float spacing = ValleyWorld.Size / (res - 1);
                foreach (var tree in trees)
                {
                    int cx = Mathf.RoundToInt((tree.point.x + ValleyWorld.Size * .5f) / spacing), cz = Mathf.RoundToInt((tree.point.y + ValleyWorld.Size * .5f) / spacing);
                    for (int z = Mathf.Max(0, cz - 2); z <= Mathf.Min(res - 1, cz + 2); z++)
                    for (int x = Mathf.Max(0, cx - 2); x <= Mathf.Min(res - 1, cx + 2); x++)
                    {
                        var p = new Vector2(x * spacing - ValleyWorld.Size * .5f, z * spacing - ValleyWorld.Size * .5f);
                        float weight = Mathf.Clamp01(1f - Vector2.Distance(p, tree.point) / 28f);
                        canopy[z * res + x] = Mathf.Max(canopy[z * res + x], weight);
                    }
                }
            }
        }
        [BurstCompile]
        struct PaintJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> heights, edits, canopy;
            [ReadOnly] public NativeArray<Road> roads;
            [WriteOnly] public NativeArray<Weights> output;
            public int resolution, seed, width, x0, z0;
            public void Execute(int index)
            {
                var p = new Vector2((index % width + x0) / (float)(resolution - 1) * ValleyWorld.Size - ValleyWorld.Size * .5f,
                    (index / width + z0) / (float)(resolution - 1) * ValleyWorld.Size - ValleyWorld.Size * .5f);
                float h = Sample(heights, p), cut = Sample(edits, p), woods = Sample(canopy, p);
                const float spacing = ValleyWorld.Size / (ValleyWorld.Resolution - 1);
                float dx = (Sample(heights, p + Vector2.right * spacing) - Sample(heights, p - Vector2.right * spacing)) / (spacing * 2f);
                float dz = (Sample(heights, p + Vector2.up * spacing) - Sample(heights, p - Vector2.up * spacing)) / (spacing * 2f);
                float steep = Smooth(10f, 37f, Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg);
                // Smaller irregular transitions keep the landscape from reading as broad painted colour blobs.
                var warp = new Vector2(Noise(p / 157f), Noise(p / 131f + Vector2.one * 73f)) * 38f;
                float broad = Noise((p + warp) / 270f), patches = Noise((p + warp) / 38f + new Vector2(51f, 12f));
                float rock = Mathf.Clamp01(steep * .9f + Smooth(240f, 440f, h) * .65f);
                rock *= Mathf.Lerp(.65f, 1f, patches);
                float riverX = -1530f + Mathf.Sin(p.y * .0011f + (seed % 97) * .02f) * 130f;
                float bank = 1f - Smooth(110f, 180f, Mathf.Abs(p.x - riverX));
                float earth = Mathf.Clamp01(cut * .85f + bank * .65f + Smooth(.62f, .85f, patches) * .25f) * (1f - rock);
                float remaining = 1f - rock - earth;
                float litter = woods * Mathf.Lerp(.62f, .94f, patches) * (1f - cut) * remaining;
                remaining -= litter;
                float dry = Mathf.Clamp01(Smooth(.25f, .8f, broad) * .45f + Smooth(.4f, .8f, patches) * .35f) * remaining;
                float grass = remaining - dry, road = 0f;
                foreach (var link in roads)
                {
                    var d = link.b - link.a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - link.a, d) / Mathf.Max(.001f, d.sqrMagnitude));
                    float distance = Vector2.Distance(p, link.a + d * t);
                    road = Mathf.Max(road, 1f - Smooth(link.halfWidth - .5f, link.halfWidth + 2f, distance));
                }
                float land = 1f - road;
                output[index] = new Weights { grass = grass * land, earth = earth * land, road = road, dry = dry * land, rock = rock * land, litter = litter * land };
            }
            static float Sample(NativeArray<float> field, Vector2 point)
            {
                const int res = ValleyWorld.Resolution;
                float x = Mathf.Clamp((point.x + ValleyWorld.Size * .5f) / ValleyWorld.Size * (res - 1), 0f, res - 1.001f);
                float z = Mathf.Clamp((point.y + ValleyWorld.Size * .5f) / ValleyWorld.Size * (res - 1), 0f, res - 1.001f);
                int ix = (int)x, iz = (int)z;
                return Mathf.Lerp(Mathf.Lerp(field[iz * res + ix], field[iz * res + ix + 1], x - ix),
                    Mathf.Lerp(field[(iz + 1) * res + ix], field[(iz + 1) * res + ix + 1], x - ix), z - iz);
            }
            float Hash(int x, int z)
            {
                uint h = unchecked((uint)x * 374761393u + (uint)z * 668265263u + (uint)seed * 2246822519u);
                h = (h ^ (h >> 13)) * 1274126177u; return (h ^ (h >> 16)) / 4294967295f;
            }
            float Noise(Vector2 p)
            {
                int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.y); float u = Smooth(0f, 1f, p.x - x), v = Smooth(0f, 1f, p.y - z);
                return Mathf.Lerp(Mathf.Lerp(Hash(x, z), Hash(x + 1, z), u), Mathf.Lerp(Hash(x, z + 1), Hash(x + 1, z + 1), u), v);
            }
            static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        }
    }
}
