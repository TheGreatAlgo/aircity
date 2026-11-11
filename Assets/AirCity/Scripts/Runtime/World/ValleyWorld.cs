using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    [Serializable]
    public class ForestTree
    {
        public Vector2 position;
        public float height;
        public bool removed;
    }

    [Serializable]
    public class AirportSite
    {
        public string name, description;
        public Vector2 centre;
        public float elevation;
        public int landPrice;
        public Rect Parcel => new Rect(centre.x - 720f, centre.y - 300f, 1440f, 520f);
        public Rect Preparation => new Rect(centre.x - 430f, centre.y - 205f, 860f, 285f);
        public Rect Expansion => new Rect(centre.x - 590f, centre.y - 205f, 1180f, 285f);
        public Rect Clearing => ValleyWorld.GradingBounds(Preparation);
        public Vector2 Entrance => centre + new Vector2(110f, -155f);
    }

    /// <summary>A saved world snapshot, independent of Unity Terrain and of future generator changes. Metres, x/z.</summary>
    [Serializable]
    public class ValleyWorld
    {
        public const int GeneratorVersion = 2;
        public const int Resolution = 513;
        public const float Size = 6400f, HeightRange = 640f, RoadX = 1720f;
        public int generatorVersion = GeneratorVersion, seed;
        public float[] heights;
        public float[] disturbance;
        public List<ForestTree> trees = new List<ForestTree>();
        public List<AirportSite> sites = new List<AirportSite>();
        public List<Vector2> publicRoad = new List<Vector2>();
        public List<Vector2> settlements = new List<Vector2>();
        public float Spacing => Size / (Resolution - 1);

        public static ValleyWorld Generate(int seed)
        {
            var world = new ValleyWorld { seed = seed, heights = new float[Resolution * Resolution] };
            var rng = new WorldRandom(seed);
            float ox = rng.Next() * 2000f, oz = rng.Next() * 2000f;
            string[] names = { "Pine Meadow", "River Terrace", "Highland Clearing" };
            string[] descriptions = {
                "Gentle ground and dense woodland. A forgiving place to learn.",
                "An open terrace near the river. Less clearing, more earthwork.",
                "A wooded mountain foothill. Higher land with more uneven ground."
            };
            for (int i = 0; i < 3; i++)
                world.sites.Add(new AirportSite {
                    name = names[i], description = descriptions[i],
                    centre = new Vector2(260f + rng.Next() * 160f, -1050f + i * 1150f),
                    elevation = 76f + i * 12f, landPrice = 18000 + i * 2500
                });

            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                var p = new Vector2(x * world.Spacing - Size * 0.5f, z * world.Spacing - Size * 0.5f);
                float noise = Mathf.PerlinNoise(ox + p.x * .001f, oz + p.y * .001f);
                float fine = Mathf.PerlinNoise(ox + p.x * .008f, oz + p.y * .008f) - .5f;
                // Undulating shoulders and smaller hummocks give the lowlands a real surface silhouette.
                // These wavelengths stay above the 12.5 m sample spacing; surface materials handle finer detail.
                float rolling = Mathf.PerlinNoise(ox + 137f + p.x * .0025f, oz + 71f + p.y * .0025f) - .5f;
                float hummocks = Mathf.PerlinNoise(ox + 53f + p.x * .021f, oz + 241f + p.y * .021f) - .5f;
                float channel = Mathf.Abs(Mathf.PerlinNoise(ox + 310f + p.x * .004f, oz + 91f + p.y * .004f) - .5f);
                float swales = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.025f, .11f, channel));
                float mountains = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1400f, 3100f, Mathf.Abs(p.y)));
                float h = 66f + noise * 32f + rolling * 18f + fine * 7f + mountains * (90f + noise * 330f);
                // Broad natural valleys also keep the starter approaches below the glide slope.
                for (int siteIndex = 0; siteIndex < world.sites.Count; siteIndex++)
                {
                    var site = world.sites[siteIndex];
                    float across = Mathf.Abs(p.y - site.centre.y);
                    // A warped valley edge avoids three unnaturally straight, uniform bands through the region.
                    float width = 520f + rolling * 100f;
                    float valley = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(200f, width, across));
                    float raw = rolling * (7f + siteIndex * .5f) + fine * (3.6f + siteIndex * .4f) + hummocks * (1.8f + siteIndex * .2f)
                        - swales * (.85f + siteIndex * .1f);
                    // Keep pronounced shoulder landforms beyond the starter's southern preparation edge and
                    // its blended earthwork margin. The pad still has small relief until the player grades it.
                    float shoulder = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(225f, 400f, across));
                    float limit = Mathf.Lerp(2.75f, 10f + siteIndex * 2f, shoulder);
                    // Smoothly bounded relief leaves an achievable airstrip and a safe 3-degree approach,
                    // without creating a clipped flat pad before the player does any earthwork.
                    float relief = limit * (float)Math.Tanh(raw / limit);
                    float local = site.elevation + relief + shoulder * (fine * 5f + hummocks * 1.5f);
                    h = Mathf.Lerp(h, local, valley);
                }
                float bank = Mathf.Abs(p.x - world.RiverX(p.y));
                float riverBlend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(28f, 125f, bank));
                h = Mathf.Lerp(h, world.WaterHeight(p.y) - 3.5f, riverBlend);
                world.heights[z * Resolution + x] = h;
            }

            for (int i = 0; i <= 64; i++) world.publicRoad.Add(new Vector2(RoadX, -Size * .5f + i * Size / 64f));
            world.settlements.Add(new Vector2(RoadX + 65f, -1800f));
            world.settlements.Add(new Vector2(RoadX + 65f, 1900f));
            for (int i = 0; i < 90000; i++)
            {
                var p = new Vector2((rng.Next() - .5f) * (Size - 60f), (rng.Next() - .5f) * (Size - 60f));
                float h = world.Height(p);
                if (Mathf.Abs(p.x - world.RiverX(p.y)) < 95f || Mathf.Abs(p.x - RoadX) < 18f || h > 400f) continue;
                float grove = Mathf.PerlinNoise(ox + p.x * .0018f, oz + p.y * .0018f);
                if (grove < .34f) continue;
                bool approach = false;
                foreach (var site in world.sites)
                {
                    float dx = Mathf.Abs(p.x - site.centre.x), dz = Mathf.Abs(p.y - site.centre.y);
                    if (dx > 430f && dz < 90f) approach = true;
                    // The river terrace is naturally more open.
                    if (site == world.sites[1] && site.Preparation.Contains(p) && rng.Next() < .7f) approach = true;
                }
                if (approach) continue;
                bool inTown = world.settlements.Exists(t => Vector2.Distance(t, p) < 145f);
                if (!inTown) world.trees.Add(new ForestTree { position = p, height = 14f + rng.Next() * 15f });
            }
            return world;
        }

        public float RiverX(float z) => -1530f + Mathf.Sin(z * .0011f + (seed % 97) * .02f) * 130f;
        public float WaterHeight(float z) => 48f + (z + Size * .5f) * .0014f;

        public float Height(Vector2 p)
        {
            float x = Mathf.Clamp((p.x + Size * .5f) / Spacing, 0f, Resolution - 1.001f);
            float z = Mathf.Clamp((p.y + Size * .5f) / Spacing, 0f, Resolution - 1.001f);
            int ix = (int)x, iz = (int)z;
            return Mathf.Lerp(Mathf.Lerp(heights[iz * Resolution + ix], heights[iz * Resolution + ix + 1], x - ix),
                Mathf.Lerp(heights[(iz + 1) * Resolution + ix], heights[(iz + 1) * Resolution + ix + 1], x - ix), z - iz);
        }

        public int TreeCount(Rect area) => trees.FindAll(t => !t.removed && area.Contains(t.position)).Count;
        public void Clear(Rect area)
        {
            foreach (var tree in trees) if (area.Contains(tree.position)) tree.removed = true;
        }

        public float Earthwork(Rect area, float elevation)
        {
            float volume = 0f;
            ForSamples(GradingBounds(area), (x, z, p) =>
                volume += Mathf.Abs(heights[z * Resolution + x] - elevation) * GradeWeight(area, p) * Spacing * Spacing);
            return volume;
        }

        public static Rect GradingBounds(Rect area)
        {
            const float margin = Size / (Resolution - 1) + 50f;
            return new Rect(area.xMin - margin, area.yMin - margin, area.width + 2f * margin, area.height + 2f * margin);
        }

        float GradeWeight(Rect area, Vector2 p)
        {
            float dx = Mathf.Max(area.xMin - Spacing - p.x, p.x - area.xMax - Spacing, 0f);
            float dz = Mathf.Max(area.yMin - Spacing - p.y, p.y - area.yMax - Spacing, 0f);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Max(dx, dz) / 50f));
        }

        public void Grade(Rect area, float elevation)
        {
            // Include a whole sample beyond the footprint so bilinear interpolation stays level at the edges.
            var blend = GradingBounds(area);
            ForSamples(blend, (x, z, p) => {
                float t = GradeWeight(area, p);
                int index = z * Resolution + x;
                heights[index] = Mathf.Lerp(heights[index], elevation, t);
            });
            Clear(blend);
        }

        public void MarkGround(Vector2 point, float radius, float amount)
        {
            if (disturbance == null || disturbance.Length != heights.Length) disturbance = new float[Resolution * Resolution];
            ForSamples(new Rect(point.x - radius, point.y - radius, radius * 2f, radius * 2f), (x, z, p) => {
                float weight = Mathf.Clamp01(1f - Vector2.Distance(point, p) / radius);
                int i = z * Resolution + x; disturbance[i] = Mathf.Max(disturbance[i], amount * weight);
            });
        }
        public float BrushEarthwork(Vector2 point, float radius, float elevation, Rect land)
        {
            float volume = 0f;
            BrushSamples(point, radius, land, (i, weight) => volume += Mathf.Abs(heights[i] - elevation) * weight * Spacing * Spacing);
            return volume;
        }
        public void LevelBrush(Vector2 point, float radius, float elevation, Rect land)
        {
            if (disturbance == null || disturbance.Length != heights.Length) disturbance = new float[Resolution * Resolution];
            BrushSamples(point, radius, land, (i, weight) => {
                heights[i] = Mathf.Lerp(heights[i], elevation, weight); disturbance[i] = Mathf.Max(disturbance[i], weight);
            });
        }
        void BrushSamples(Vector2 point, float radius, Rect land, Action<int, float> action)
        {
            ForSamples(new Rect(point.x - radius, point.y - radius, radius * 2f, radius * 2f), (x, z, p) => {
                if (!land.Contains(p)) return;
                float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius * .72f, radius, Vector2.Distance(point, p)));
                if (weight > 0f) action(z * Resolution + x, weight);
            });
        }

        void ForSamples(Rect area, Action<int, int, Vector2> action)
        {
            int x0 = Mathf.Clamp(Mathf.CeilToInt((area.xMin + Size * .5f) / Spacing), 0, Resolution - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((area.xMax + Size * .5f) / Spacing), 0, Resolution - 1);
            int z0 = Mathf.Clamp(Mathf.CeilToInt((area.yMin + Size * .5f) / Spacing), 0, Resolution - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((area.yMax + Size * .5f) / Spacing), 0, Resolution - 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++) action(x, z, new Vector2(x * Spacing - Size * .5f, z * Spacing - Size * .5f));
        }

        public bool Validate(out string problem)
        {
            problem = "The saved world is incomplete or uses an unsupported format.";
            if (generatorVersion < 1 || generatorVersion > GeneratorVersion || heights == null || heights.Length != Resolution * Resolution ||
                sites == null || sites.Count != 3 || trees == null || trees.Count > 120000 ||
                publicRoad == null || publicRoad.Count != 65 || settlements == null || settlements.Count != 2) return false;
            foreach (float h in heights) if (!Finite(h) || h < 0f || h > HeightRange) return false;
            if (disturbance != null && disturbance.Length > 0 && (disturbance.Length != heights.Length || Array.Exists(disturbance, d => !Finite(d) || d < 0f || d > 1f))) return false;
            foreach (var site in sites)
                if (site == null || string.IsNullOrWhiteSpace(site.name) || !InWorld(site.centre) || !Finite(site.elevation) ||
                    site.elevation < 10f || site.elevation > 400f || site.landPrice < 0 || site.landPrice > 50000) return false;
            foreach (var tree in trees)
                if (tree == null || !InWorld(tree.position) || !Finite(tree.height) || tree.height < 1f || tree.height > 40f) return false;
            foreach (var p in publicRoad) if (!InWorld(p)) return false;
            foreach (var p in settlements) if (!InWorld(p)) return false;
            problem = null;
            return true;
        }

        public static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static bool InWorld(Vector2 p) => Finite(p.x) && Finite(p.y) && Mathf.Abs(p.x) <= Size * .5f && Mathf.Abs(p.y) <= Size * .5f;
    }

    /// <summary>Versioned local random stream: generation never changes UnityEngine.Random's global state.</summary>
    public struct WorldRandom
    {
        uint state;
        public WorldRandom(int seed) { state = unchecked((uint)seed) ^ 0x9E3779B9u; if (state == 0) state = 1; }
        public float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state >> 8) / 16777216f; }
    }
}
