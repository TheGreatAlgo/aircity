using System;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Continuous local river/lake levels and banks, derived from saved mapped water samples.</summary>
    public static class RegionInlandShore
    {
        const int SupportRadius = 3;
        const float Diagonal = 1.41421356f;

        /// <summary>Locally condition DEM noise, then extend levels beneath dry banks so mesh edges stay hidden.</summary>
        public static float[] Surface(float[] source, int resolution, bool condition = true)
        {
            if (source == null || source.Length != resolution * resolution)
                throw new ArgumentException("Mapped inland water requires a square source level field.");
            var wet = new float[source.Length]; var scratch = new float[source.Length]; var neighbours = new float[9];
            if (!condition) Array.Copy(source, wet, source.Length);
            else
            {
                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = z * resolution + x;
                    if (source[i] <= 0f) continue;
                    int count = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution) continue;
                        float level = source[nz * resolution + nx];
                        if (level > 0f) neighbours[count++] = level;
                    }
                    Array.Sort(neighbours, 0, count);
                    wet[i] = count % 2 == 0 ? (neighbours[count / 2 - 1] + neighbours[count / 2]) * .5f : neighbours[count / 2];
                }
                // A local filter removes canopy/quantization spikes without fitting one flat level to an entire
                // connected river. Long downhill trends and separate lake altitudes remain independent.
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    for (int z = 0; z < resolution; z++)
                    for (int x = 0; x < resolution; x++)
                    {
                        int i = z * resolution + x;
                        if (source[i] <= 0f) continue;
                        float sum = 0f, weight = 0f;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution) continue;
                            float level = wet[nz * resolution + nx];
                            if (level <= 0f) continue;
                            float w = (dx == 0 ? 2f : 1f) * (dz == 0 ? 2f : 1f);
                            sum += level * w; weight += w;
                        }
                        scratch[i] = sum / weight;
                    }
                    var previous = wet; wet = scratch; scratch = previous;
                }
            }
            var surface = (float[])wet.Clone();
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x;
                if (source[i] > 0f) continue;
                float sum = 0f, weight = 0f;
                for (int dz = -SupportRadius; dz <= SupportRadius; dz++)
                for (int dx = -SupportRadius; dx <= SupportRadius; dx++)
                {
                    int square = dx * dx + dz * dz;
                    if (square == 0 || square > SupportRadius * SupportRadius) continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= resolution || nz >= resolution) continue;
                    float level = wet[nz * resolution + nx];
                    if (level <= 0f) continue;
                    float w = 1f / square; sum += level * w; weight += w;
                }
                if (weight > 0f) surface[i] = sum / weight;
            }
            return surface;
        }

        public static void Shape(ValleyWorld world)
        {
            if (world == null || !world.HasInlandWater) return;
            var surface = world.InlandSurfaceHeights;
            var distance = Distances(world.waterHeights);
            Apply(world, surface, distance, null);
        }

        /// <summary>Upgrade only natural v4/v5 inland banks, preserving raw saved levels and player earthwork.</summary>
        public static bool Upgrade(ValleyWorld world, Func<Vector2, bool> protectedPoint)
        {
            if (world == null || world.generatorVersion < 4 || world.generatorVersion >= 6 || !world.HasInlandWater ||
                world.heights == null || world.heights.Length != world.waterHeights.Length) return false;
            var surface = Surface(world.waterHeights, ValleyWorld.Resolution);
            var distance = Distances(world.waterHeights);
            var protection = Protection(world, protectedPoint, surface);
            Apply(world, surface, distance, protection);
            world.generatorVersion = 6;
            world.SetInlandSurface(surface);
            return true;
        }

        static float[] Distances(float[] source)
        {
            var cover = new byte[source.Length];
            for (int i = 0; i < source.Length; i++) if (source[i] > 0f) cover[i] = 80;
            return RegionCoastline.SignedDistance(cover, ValleyWorld.Resolution);
        }

        static void Apply(ValleyWorld world, float[] surface, float[] distance, float[] protection)
        {
            for (int i = 0; i < world.heights.Length; i++)
            {
                float level = surface[i];
                if (level <= 0f) continue;
                float change = protection == null ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 3f, protection[i]));
                if (change == 0f) continue;
                bool wet = world.waterHeights[i] > 0f;
                float cells = Mathf.Abs(distance[i]), original = world.heights[i], target;
                if (wet)
                {
                    float depth = 1.5f * (float)Math.Tanh(cells * world.Spacing * .18f / 1.5f);
                    target = level - depth;
                }
                else
                {
                    // A local varying level replaces a global sea plane. Keep unmapped dry banks above it,
                    // including places where coarse DEM pixels dip below the neighbouring channel surface.
                    float plateau = Mathf.Max(.9f, original - level);
                    float relief = plateau * (float)Math.Tanh(cells * world.Spacing * .18f / plateau);
                    float restore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.1f, 3f, cells));
                    target = Mathf.Lerp(level + relief, Mathf.Max(original, level + .25f), restore);
                }
                world.heights[i] = Mathf.Lerp(original, Mathf.Clamp(target, 4f, ValleyWorld.HeightRange - 20f), change);
            }
        }

        static float[] Protection(ValleyWorld world, Func<Vector2, bool> protectedPoint, float[] surface)
        {
            const int resolution = ValleyWorld.Resolution;
            var blocked = new bool[world.heights.Length]; bool any = false;
            if (world.disturbance != null && world.disturbance.Length == blocked.Length)
            {
                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    if (world.disturbance[z * resolution + x] <= 0f) continue;
                    any = true;
                    for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx >= 0 && nz >= 0 && nx < resolution && nz < resolution) blocked[nz * resolution + nx] = true;
                    }
                }
            }
            if (protectedPoint != null)
            {
                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = z * resolution + x;
                    if (surface[i] <= 0f || blocked[i]) continue;
                    var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                    if (!protectedPoint(p)) continue;
                    blocked[i] = true; any = true;
                }
            }
            if (!any) return null;
            var result = new float[blocked.Length];
            for (int i = 0; i < result.Length; i++) result[i] = blocked[i] ? 0f : resolution * 2f;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x; float d = result[i];
                if (x > 0) d = Mathf.Min(d, result[i - 1] + 1f);
                if (z > 0)
                {
                    d = Mathf.Min(d, result[i - resolution] + 1f);
                    if (x > 0) d = Mathf.Min(d, result[i - resolution - 1] + Diagonal);
                    if (x + 1 < resolution) d = Mathf.Min(d, result[i - resolution + 1] + Diagonal);
                }
                result[i] = d;
            }
            for (int z = resolution - 1; z >= 0; z--)
            for (int x = resolution - 1; x >= 0; x--)
            {
                int i = z * resolution + x; float d = result[i];
                if (x + 1 < resolution) d = Mathf.Min(d, result[i + 1] + 1f);
                if (z + 1 < resolution)
                {
                    d = Mathf.Min(d, result[i + resolution] + 1f);
                    if (x > 0) d = Mathf.Min(d, result[i + resolution - 1] + Diagonal);
                    if (x + 1 < resolution) d = Mathf.Min(d, result[i + resolution + 1] + Diagonal);
                }
                result[i] = d;
            }
            return result;
        }
    }
}
