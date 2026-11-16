using System;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Shapes saved coast geometry from the real land mask without erasing narrow reef islands.</summary>
    public static class RegionCoastline
    {
        const float Diagonal = 1.41421356f;

        /// <summary>Run only during generation. Existing saves and player earthwork are never reprocessed.</summary>
        public static void Shape(float[] heights, byte[] cover, int resolution, float spacing, float seaLevel)
        {
            if (heights == null || cover == null || heights.Length != resolution * resolution || cover.Length != heights.Length)
                throw new ArgumentException("Coast geometry requires matching square height and land-cover fields.");
            var distance = SignedDistance(cover, resolution);
            Apply(heights, cover, distance, spacing, seaLevel, null);
        }

        /// <summary>Upgrade natural v4 shoreline only. Saved edits and supplied airport/road footprints stay intact.</summary>
        public static bool Upgrade(ValleyWorld world, Func<Vector2, bool> protectedPoint)
        {
            if (world == null || world.generatorVersion != 4 || !world.HasOcean || world.landCover == null ||
                world.heights == null || world.landCover.Length != ValleyWorld.Resolution * ValleyWorld.Resolution ||
                world.heights.Length != world.landCover.Length) return false;
            const int resolution = ValleyWorld.Resolution;
            var distance = SignedDistance(world.landCover, resolution);
            var blocked = new bool[world.heights.Length]; bool anyBlocked = false;
            if (world.disturbance != null && world.disturbance.Length == blocked.Length)
            {
                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    if (world.disturbance[z * resolution + x] <= 0f) continue;
                    anyBlocked = true;
                    // Even tree clearing is a deliberate player choice; protect the surrounding interpolation halo.
                    for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int sx = x + dx, sz = z + dz;
                        if (sx >= 0 && sz >= 0 && sx < resolution && sz < resolution) blocked[sz * resolution + sx] = true;
                    }
                }
            }
            if (protectedPoint != null)
            {
                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = z * resolution + x;
                    if (blocked[i] || Mathf.Abs(distance[i]) >= 4f) continue;
                    var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                    if (!protectedPoint(p)) continue;
                    blocked[i] = true; anyBlocked = true;
                }
            }
            float[] protectedDistance = null;
            if (anyBlocked)
            {
                protectedDistance = new float[blocked.Length];
                for (int i = 0; i < blocked.Length; i++) protectedDistance[i] = blocked[i] ? 0f : resolution * 2f;
                RelaxDistances(protectedDistance, resolution);
            }
            Apply(world.heights, world.landCover, distance, world.Spacing, world.SeaLevel, protectedDistance);
            world.generatorVersion = ValleyWorld.GeneratorVersion;
            return true;
        }

        static void Apply(float[] heights, byte[] cover, float[] distance, float spacing, float seaLevel, float[] protectedDistance)
        {
            const float profileSlope = .22f;
            for (int i = 0; i < heights.Length; i++)
            {
                float cells = Mathf.Abs(distance[i]);
                if (cells >= 4f) continue;
                float change = protectedDistance == null ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 3f, protectedDistance[i]));
                if (change == 0f) continue;
                float original = heights[i];
                // A smooth normalized ramp keeps sub-grid contour variation even on a low flat motu.
                // A hard min against its original elevation would make every shore vertex the same height
                // again, recreating the rectangular mask. Saturation approaches the original plateau gently.
                float plateau = Mathf.Max(.001f, Mathf.Abs(original - seaLevel));
                float relief = plateau * (float)Math.Tanh(cells * spacing * profileSlope / plateau);
                float shore = seaLevel + (cover[i] == 80 ? -relief : relief);
                float restore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.1f, 4f, cells));
                heights[i] = Mathf.Lerp(original, Mathf.Lerp(shore, original, restore), change);
            }
        }

        /// <summary>Distance in source-grid cells, positive on land. Smoothed zero contours round raster corners.</summary>
        public static float[] SignedDistance(byte[] cover, int resolution)
        {
            if (cover == null || cover.Length != resolution * resolution)
                throw new ArgumentException("Coast distance requires a square land-cover field.");
            var toWater = Distances(cover, resolution, true);
            var toLand = Distances(cover, resolution, false);
            for (int i = 0; i < cover.Length; i++)
                toWater[i] = cover[i] == 80 ? -(toLand[i] - .5f) : toWater[i] - .5f;
            // Three compact Gaussian passes remove stair-step curvature while retaining every source land/water
            // sample's sign. Even one-sample motu and land bridges remain above sea level and connected.
            var smoothed = toLand;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    int index = z * resolution + x;
                    if (Mathf.Abs(toWater[index]) >= 5f) { smoothed[index] = toWater[index]; continue; }
                    float sum = 0f;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sx = Mathf.Clamp(x + dx, 0, resolution - 1), sz = Mathf.Clamp(z + dz, 0, resolution - 1);
                        sum += toWater[sz * resolution + sx] * (dx == 0 ? 2f : 1f) * (dz == 0 ? 2f : 1f);
                    }
                    float value = sum / 16f;
                    smoothed[index] = cover[index] == 80 ? Mathf.Min(-.1f, value) : Mathf.Max(.1f, value);
                }
                var previous = toWater; toWater = smoothed; smoothed = previous;
            }
            return toWater;
        }

        static float[] Distances(byte[] cover, int resolution, bool water)
        {
            var distance = new float[cover.Length];
            float far = resolution * 2f;
            for (int i = 0; i < cover.Length; i++) distance[i] = (cover[i] == 80) == water ? 0f : far;
            RelaxDistances(distance, resolution);
            return distance;
        }

        static void RelaxDistances(float[] distance, int resolution)
        {
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x;
                float d = distance[i];
                if (x > 0) d = Mathf.Min(d, distance[i - 1] + 1f);
                if (z > 0)
                {
                    d = Mathf.Min(d, distance[i - resolution] + 1f);
                    if (x > 0) d = Mathf.Min(d, distance[i - resolution - 1] + Diagonal);
                    if (x + 1 < resolution) d = Mathf.Min(d, distance[i - resolution + 1] + Diagonal);
                }
                distance[i] = d;
            }
            for (int z = resolution - 1; z >= 0; z--)
            for (int x = resolution - 1; x >= 0; x--)
            {
                int i = z * resolution + x;
                float d = distance[i];
                if (x + 1 < resolution) d = Mathf.Min(d, distance[i + 1] + 1f);
                if (z + 1 < resolution)
                {
                    d = Mathf.Min(d, distance[i + resolution] + 1f);
                    if (x > 0) d = Mathf.Min(d, distance[i + resolution - 1] + Diagonal);
                    if (x + 1 < resolution) d = Mathf.Min(d, distance[i + resolution + 1] + Diagonal);
                }
                distance[i] = d;
            }
        }
    }
}
