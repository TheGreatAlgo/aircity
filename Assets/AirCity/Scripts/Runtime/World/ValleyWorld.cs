using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    public enum RegionPreset { Woodland, Mountains, Tropical, Desert, Random }

    /// <summary>Saved river shape shared by terrain, water, surface paint and Burst scenery jobs.</summary>
    [Serializable]
    public struct RiverCourse
    {
        public float centre, drift, amplitude, phase, frequency, halfWidth, secondary;
        public float X(float z) => centre + z * drift + Mathf.Sin(z * frequency + phase) * amplitude
            + Mathf.Sin(z * frequency * 2.17f + phase * .63f) * amplitude * secondary;
        public float WaterHeight(float z) => 48f + (z + ValleyWorld.Size * .5f) * .0014f;
    }

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
        public const int GeneratorVersion = 6;
        public const int Resolution = 513;
        public const int MaxTrees = 800000;
        public const float Size = 6400f, HeightRange = 640f, RoadX = 1720f;
        public int generatorVersion = GeneratorVersion, seed;
        public int forestVersion;
        public List<int> clearedProps = new List<int>();
        [NonSerialized] List<int> clearedPropSource;
        [NonSerialized] HashSet<int> clearedPropIndex;
        [NonSerialized] int clearedPropCount;
        public RegionPreset preset;
        public RiverCourse river;
        public bool hasOcean, hasRiver;
        public float seaLevel = 40f, elevationScale = 1f, elevationOffset, sourceExtent;
        public string landscapeSource, elevationSha256, coverSha256;
        public bool hasCoordinates;
        public double latitude, longitude;
        public float[] roadHeights, waterHeights;
        [NonSerialized] float[] inlandSurface, inlandSurfaceSource;
        [NonSerialized] bool inlandSurfaceConditioned;
        public byte[] landCover;
        public bool HasOcean => generatorVersion >= 4 && hasOcean;
        public bool HasRiver => generatorVersion < 4 || hasRiver;
        public bool HasInlandWater => generatorVersion >= 4 && waterHeights != null && waterHeights.Length == Resolution * Resolution;
        public float[] InlandSurfaceHeights
        {
            get
            {
                if (!HasInlandWater) return null;
                bool conditioned = generatorVersion >= 6;
                if (inlandSurfaceSource != waterHeights || inlandSurface == null || inlandSurfaceConditioned != conditioned)
                {
                    inlandSurfaceSource = waterHeights; inlandSurfaceConditioned = conditioned;
                    inlandSurface = RegionInlandShore.Surface(waterHeights, Resolution, condition: conditioned);
                }
                return inlandSurface;
            }
        }
        internal void SetInlandSurface(float[] surface)
        { inlandSurfaceSource = waterHeights; inlandSurfaceConditioned = generatorVersion >= 6; inlandSurface = surface; }
        public float SeaLevel => seaLevel;
        public bool IsWater(Vector2 p)
        {
            if (HasOcean && Height(p) <= SeaLevel + .15f ||
                HasRiver && Mathf.Abs(p.x - RiverX(p.y)) < River.halfWidth && Height(p) < WaterHeight(p.y) + .5f) return true;
            float inland = InlandWaterHeight(p);
            return inland > 0f && Height(p) <= inland + .15f;
        }

        /// <summary>Local saved water profile; dry samples do not pull river levels towards zero.</summary>
        public float InlandWaterHeight(Vector2 p)
        {
            if (!HasInlandWater) return 0f;
            var surface = InlandSurfaceHeights;
            float x = Mathf.Clamp((p.x + Size * .5f) / Spacing, 0f, Resolution - 1.001f);
            float z = Mathf.Clamp((p.y + Size * .5f) / Spacing, 0f, Resolution - 1.001f);
            int ix = (int)x, iz = (int)z; float u = x - ix, v = z - iz;
            float a = surface[iz * Resolution + ix], b = surface[iz * Resolution + ix + 1];
            float c = surface[(iz + 1) * Resolution + ix], d = surface[(iz + 1) * Resolution + ix + 1];
            float wa = a > 0f ? (1f - u) * (1f - v) : 0f, wb = b > 0f ? u * (1f - v) : 0f;
            float wc = c > 0f ? (1f - u) * v : 0f, wd = d > 0f ? u * v : 0f;
            float weight = wa + wb + wc + wd;
            return weight > .000001f ? (a * wa + b * wb + c * wc + d * wd) / weight : 0f;
        }
        public float WaterSurfaceAt(Vector2 p)
        {
            float height=Height(p);if(!IsWater(p))return height;
            if(HasOcean)height=Mathf.Max(height,SeaLevel);
            if(HasRiver&&Mathf.Abs(p.x-RiverX(p.y))<River.halfWidth)height=Mathf.Max(height,WaterHeight(p.y));
            return Mathf.Max(height,InlandWaterHeight(p));
        }
        public Rect RegionBounds => new Rect(-Size * .5f, -Size * .5f, Size, Size);
        public string PresetName => preset == RegionPreset.Mountains ? "Alpine Highlands" : preset == RegionPreset.Tropical ? "Volcanic Lagoon" :
            preset == RegionPreset.Desert ? "Desert Canyons" : "Woodland Valley";
        public RiverCourse River => generatorVersion < 3 ? new RiverCourse {
            centre = -1530f, amplitude = 130f, phase = (seed % 97) * .02f, frequency = .0011f, halfWidth = 60f
        } : river;
        public float[] heights;
        public float[] disturbance;
        public List<ForestTree> trees = new List<ForestTree>();
        public List<AirportSite> sites = new List<AirportSite>();
        public List<Vector2> publicRoad = new List<Vector2>();
        public List<Vector2> settlements = new List<Vector2>();
        public float Spacing => Size / (Resolution - 1);

        public static ValleyWorld Generate(int seed, RegionPreset preset = RegionPreset.Woodland)
        {
            if (preset == RegionPreset.Random)
            {
                var random = new WorldRandom(unchecked(seed * 1664525 + 1013904223));
                preset = (RegionPreset)Mathf.Min(3, (int)(random.Next() * 4f));
            }
            return GenerateTerrain(RegionElevation.Data(preset), seed, preset);
        }

        /// <summary>Build the selected real footprint north-up. Seeds affect scenery and roads, never geography.</summary>
        public static ValleyWorld GenerateLocation(RegionTerrainData data)
        {
            ValidateSource(data);
            int seed = unchecked((int)Math.Round(data.latitude * 100000d) * 397 ^ (int)Math.Round(data.longitude * 100000d));
            float low = float.MaxValue, high = float.MinValue; int trees = 0, bare = 0, land = 0;
            for (int i = 0; i < data.elevations.Length; i++)
            {
                low = Mathf.Min(low, data.elevations[i]); high = Mathf.Max(high, data.elevations[i]);
                if (data.cover[i] == 80) continue;
                land++; if (data.cover[i] == 10 || data.cover[i] == 95) trees++;
                if (data.cover[i] == 60) bare++;
            }
            var preset = bare > land * .45f ? RegionPreset.Desert :
                Math.Abs(data.latitude) < 24d && (data.hasOcean || trees > land * .15f) ? RegionPreset.Tropical :
                high - low > 550f ? RegionPreset.Mountains : RegionPreset.Woodland;
            return GenerateTerrain(data, seed, preset);
        }

        static void ValidateSource(RegionTerrainData data)
        {
            if (data == null || data.elevations == null || data.cover == null ||
                data.elevations.Length != Resolution * Resolution || data.cover.Length != data.elevations.Length ||
                double.IsNaN(data.latitude) || double.IsInfinity(data.latitude) || Math.Abs(data.latitude) > 85d ||
                double.IsNaN(data.longitude) || double.IsInfinity(data.longitude) || Math.Abs(data.longitude) > 180d ||
                !Finite(data.extent) || data.extent < 1000f || data.extent > 50000f ||
                Array.Exists(data.elevations, value => !Finite(value) || value < -12000f || value > 10000f))
                throw new ArgumentException("A region needs finite north-up geographic elevation and cover samples.", nameof(data));
        }

        static ValleyWorld GenerateTerrain(RegionTerrainData data, int seed, RegionPreset preset)
        {
            ValidateSource(data);
            var marine = MarineMask(data);
            bool ocean = Array.Exists(marine, value => value);
            float low = float.MaxValue, high = float.MinValue;
            foreach (float value in data.elevations) { low = Mathf.Min(low, value); high = Mathf.Max(high, value); }
            float scale = Mathf.Min(1f, 560f / Mathf.Max(1f, high - (ocean ? 0f : low)));
            float offset = ocean ? 40f : 48f - low * scale;
            var world = new ValleyWorld { seed = seed, preset = preset, hasOcean = ocean, seaLevel = 40f,
                hasCoordinates = true, latitude = data.latitude, longitude = data.longitude,
                elevationScale = scale, elevationOffset = offset, landscapeSource = data.name,
                elevationSha256 = data.elevationSha256, coverSha256 = data.coverSha256,
                sourceExtent = data.extent, heights = new float[Resolution * Resolution], landCover = (byte[])data.cover.Clone(),
                river = new RiverCourse { centre = -2500f, amplitude = 0f, phase = 0f, frequency = .001f, halfWidth = 1f } };
            var coastCover = ocean ? (byte[])world.landCover.Clone() : null;
            for (int i = 0; i < world.heights.Length; i++)
            {
                float h = data.elevations[i], height = h * scale + offset;
                if (marine[i]) height = world.SeaLevel - 1.5f - Mathf.Min(32f, Mathf.Max(0f, -h) * .12f);
                else if (world.landCover[i] == 80)
                {
                    if (world.waterHeights == null) world.waterHeights = new float[Resolution * Resolution];
                    float surface = Mathf.Clamp(height, ocean ? world.SeaLevel + .5f : 5.5f, HeightRange - 20f);
                    world.waterHeights[i] = surface; height = surface - 1.5f;
                    // Inland lakes remain elevated freshwater even in a coastal region.
                    if (coastCover != null) coastCover[i] = 30;
                }
                else if (ocean && h <= .5f) height = world.SeaLevel + 1.5f;
                world.heights[i] = Mathf.Clamp(height, 4f, HeightRange - 20f);
            }
            if (world.HasOcean) RegionCoastline.Shape(world.heights, coastCover, Resolution, world.Spacing, world.SeaLevel);
            if (world.HasInlandWater) RegionInlandShore.Shape(world);
            RegionRoadPlanner.Plan(world);
            world.CreateLandscapeAnchors();
            ForestPopulation.Generate(world);
            return world;
        }

        static bool[] MarineMask(RegionTerrainData data)
        {
            var marine = new bool[data.cover.Length];
            if (!data.hasOcean) return marine;
            var eligible = new bool[marine.Length];
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                int i = z * Resolution + x;
                if (data.cover[i] != 80) continue;
                if (data.elevations[i] <= 2f) { eligible[i] = true; continue; }
                // Mapzen has elevated artefacts over broad real lagoons. Their mapped water interior is
                // more reliable than its DEM. Narrow uphill streams do not have this broad water core.
                bool broad = true;
                for (int dz = -2; dz <= 2 && broad; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx * dx + dz * dz > 4) continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nz >= 0 && nx < Resolution && nz < Resolution && data.cover[nz * Resolution + nx] != 80)
                    { broad = false; break; }
                }
                eligible[i] = broad;
            }
            var pending = new Queue<int>();
            for (int i = 0; i < Resolution; i++)
            {
                Visit(i); Visit((Resolution - 1) * Resolution + i);
                Visit(i * Resolution); Visit(i * Resolution + Resolution - 1);
            }
            while (pending.Count > 0)
            {
                int i = pending.Dequeue(), x = i % Resolution, z = i / Resolution;
                if (x > 0) Visit(i - 1);
                if (x + 1 < Resolution) Visit(i + 1);
                if (z > 0) Visit(i - Resolution);
                if (z + 1 < Resolution) Visit(i + Resolution);
            }
            // A bounded wet-only band covers shallow coast/mouth pixels without propagating marine levels
            // arbitrarily far up a thin connected river. It never crosses or changes a mapped land sample.
            for (int iteration = 0; iteration < 6; iteration++)
            {
                var expanded = (bool[])marine.Clone();
                for (int z = 0; z < Resolution; z++)
                for (int x = 0; x < Resolution; x++)
                {
                    int i = z * Resolution + x;
                    if (marine[i] || data.cover[i] != 80) continue;
                    if (x > 0 && marine[i - 1] || x + 1 < Resolution && marine[i + 1] ||
                        z > 0 && marine[i - Resolution] || z + 1 < Resolution && marine[i + Resolution]) expanded[i] = true;
                }
                marine = expanded;
            }
            // Coarse cover sampling can detach small tidal pools and thin cove tips from the broad lagoon.
            // Only tiny, low coastal remnants qualify; elevated lakes and long upstream reaches remain fresh.
            var visited = new bool[marine.Length]; var pocket = new List<int>();
            for (int start = 0; start < marine.Length; start++)
            {
                if (marine[start] || visited[start] || data.cover[start] != 80) continue;
                pocket.Clear(); pending.Enqueue(start); visited[start] = true;
                float high = float.MinValue;
                while (pending.Count > 0)
                {
                    int i = pending.Dequeue(), x = i % Resolution, z = i / Resolution;
                    pocket.Add(i); high = Mathf.Max(high, data.elevations[i]);
                    if (x > 0) VisitPocket(i - 1);
                    if (x + 1 < Resolution) VisitPocket(i + 1);
                    if (z > 0) VisitPocket(i - Resolution);
                    if (z + 1 < Resolution) VisitPocket(i + Resolution);
                }
                if (pocket.Count > 512 || high > 20f) continue;
                bool coastal = true;
                foreach (int i in pocket)
                {
                    int x = i % Resolution, z = i / Resolution; bool near = false;
                    for (int dz = -12; dz <= 12 && !near; dz++)
                    for (int dx = -12; dx <= 12; dx++)
                    {
                        if (Mathf.Abs(dx) + Mathf.Abs(dz) > 12) continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx >= 0 && nz >= 0 && nx < Resolution && nz < Resolution && marine[nz * Resolution + nx])
                        { near = true; break; }
                    }
                    if (!near) { coastal = false; break; }
                }
                if (coastal) foreach (int i in pocket) marine[i] = true;
            }
            return marine;

            void Visit(int i)
            {
                if (marine[i] || !eligible[i]) return;
                marine[i] = true; pending.Enqueue(i);
            }
            void VisitPocket(int i)
            {
                if (marine[i] || visited[i] || data.cover[i] != 80) return;
                visited[i] = true; pending.Enqueue(i);
            }
        }

        // Original generation remains solely for old construction fixtures; saved games use stored heightfields.
        public static ValleyWorld GenerateLegacy(int seed, RegionPreset preset = RegionPreset.Woodland)
        {
            var rng = new WorldRandom(seed);
            if (preset == RegionPreset.Random) preset = (RegionPreset)Mathf.Min(3, (int)(rng.Next() * 4f));
            var world = new ValleyWorld { generatorVersion = 3, seed = seed, preset = preset, heights = new float[Resolution * Resolution] };
            float ox = rng.Next() * 2000f, oz = rng.Next() * 2000f;
            float side = rng.Next() < .5f ? -1f : 1f;
            world.river = new RiverCourse {
                centre = side * Mathf.Lerp(1450f, 2150f, rng.Next()), drift = Mathf.Lerp(-.09f, .09f, rng.Next()),
                amplitude = Mathf.Lerp(140f, 330f, rng.Next()), frequency = Mathf.Lerp(.0008f, .00165f, rng.Next()),
                phase = rng.Next() * Mathf.PI * 2f, secondary = .27f,
                halfWidth = preset == RegionPreset.Tropical ? 105f : preset == RegionPreset.Desert ? 42f : 65f
            };
            float roadCentre = -side * Mathf.Lerp(1250f, 2200f, rng.Next());
            float roadPhase = rng.Next() * Mathf.PI * 2f, roadBend = Mathf.Lerp(120f, 400f, rng.Next());
            float roadDrift = Mathf.Lerp(-.08f, .08f, rng.Next());
            for (int i = 0; i <= 64; i++)
            {
                float z = -Size * .5f + i * Size / 64f;
                world.publicRoad.Add(new Vector2(roadCentre + z * roadDrift + Mathf.Sin(z * .00075f + roadPhase) * roadBend, z));
            }
            world.settlements.Add(new Vector2(world.RoadAt(-1800f) - side * 65f, -1800f));
            world.settlements.Add(new Vector2(world.RoadAt(1900f) - side * 65f, 1900f));

            // Kept only as tutorial anchors and for original save compatibility. New games own the entire region.
            float valleyShift = Mathf.Lerp(-180f, 180f, rng.Next());
            string[] names = { "Central Valley", "River Terrace", "Highland Clearing" };
            for (int i = 0; i < 3; i++)
                world.sites.Add(new AirportSite {
                    name = names[i], description = "Open region: choose where to build your airport.",
                    centre = new Vector2(Mathf.Lerp(-220f, 220f, rng.Next()), -1050f + i * 1150f + valleyShift),
                    elevation = 76f + i * 12f, landPrice = 18000 + i * 2500
                });

            float cliffSlope = Mathf.Lerp(-.6f, .6f, rng.Next());
            float cliffOffset = Mathf.Lerp(-1600f, 1600f, rng.Next());
            var mesaCentre = new Vector2(Mathf.Lerp(-1800f, 1800f, rng.Next()), Mathf.Lerp(-2100f, 2100f, rng.Next()));
            float mesaRadius = Mathf.Lerp(650f, 1200f, rng.Next());
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                var p = new Vector2(x * world.Spacing - Size * .5f, z * world.Spacing - Size * .5f);
                float noise = Mathf.PerlinNoise(ox + p.x * .001f, oz + p.y * .001f);
                float fine = Mathf.PerlinNoise(ox + p.x * .008f, oz + p.y * .008f) - .5f;
                float rolling = Mathf.PerlinNoise(ox + 137f + p.x * .0025f, oz + 71f + p.y * .0025f) - .5f;
                float hummocks = Mathf.PerlinNoise(ox + 53f + p.x * .021f, oz + 241f + p.y * .021f) - .5f;
                float channel = Mathf.Abs(Mathf.PerlinNoise(ox + 310f + p.x * .004f, oz + 91f + p.y * .004f) - .5f);
                float swales = 1f - Smooth(.025f, .11f, channel);
                float highlands = Smooth(1300f, 3050f, Mathf.Abs(p.y + rolling * 500f));
                float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(ox + p.x * .0017f, oz + p.y * .0017f) * 2f - 1f);
                // Narrow transition bands produce actual steep escarpments, rather than another smooth hill.
                float edge = p.y + p.x * cliffSlope - cliffOffset + rolling * 210f + fine * 32f;
                float escarpment = Smooth(-10f, 10f, edge);
                var mesaDelta = p - mesaCentre;
                float mesaEdge = mesaRadius - new Vector2(mesaDelta.x, mesaDelta.y * 1.3f).magnitude + rolling * 130f;
                float mesa = Smooth(-12f, 12f, mesaEdge);
                float h;
                switch (preset)
                {
                    case RegionPreset.Mountains:
                        h = 66f + noise * 65f + rolling * 30f + fine * 11f + highlands * (70f + ridge * 205f)
                            + escarpment * 150f + mesa * 85f;
                        break;
                    case RegionPreset.Tropical:
                        h = 61f + noise * 35f + rolling * 13f + fine * 5f + highlands * (35f + ridge * 90f)
                            + escarpment * 60f + mesa * 85f;
                        break;
                    case RegionPreset.Desert:
                        float dune = Mathf.Sin(p.x * .007f + p.y * .002f + noise * 9f) * 6f;
                        h = 65f + noise * 35f + rolling * 12f + fine * 3f + dune + highlands * 70f
                            + escarpment * 85f + mesa * 165f;
                        break;
                    default:
                        h = 66f + noise * 32f + rolling * 18f + fine * 7f + highlands * (60f + noise * 160f)
                            + escarpment * 75f + mesa * 100f;
                        break;
                }
                for (int siteIndex = 0; siteIndex < world.sites.Count; siteIndex++)
                {
                    var site = world.sites[siteIndex];
                    float across = Mathf.Abs(p.y - site.centre.y);
                    float width = 520f + rolling * 100f;
                    // Gentle starter pockets are local landforms, never three flattened corridors across the map.
                    float along = Mathf.Abs(p.x - site.centre.x);
                    float valley = (1f - Smooth(200f, width, across)) * (1f - Smooth(600f, 950f, along));
                    float raw = rolling * (7f + siteIndex * .5f) + fine * (3.6f + siteIndex * .4f) + hummocks * (1.8f + siteIndex * .2f)
                        - swales * (.85f + siteIndex * .1f);
                    float shoulder = Smooth(225f, 400f, across);
                    float limit = Mathf.Lerp(2.75f, 10f + siteIndex * 2f, shoulder);
                    float relief = limit * (float)Math.Tanh(raw / limit);
                    h = Mathf.Lerp(h, site.elevation + relief + shoulder * (fine * 5f + hummocks * 1.5f), valley);
                }
                float bank = Mathf.Abs(p.x - world.RiverX(p.y));
                float riverBlend = 1f - Smooth(world.river.halfWidth * .65f, world.river.halfWidth + 70f, bank);
                h = Mathf.Lerp(h, world.WaterHeight(p.y) - 3.5f, riverBlend);
                world.heights[z * Resolution + x] = Mathf.Clamp(h, 10f, HeightRange - 20f);
            }

            int candidates = preset == RegionPreset.Desert ? 4500 : preset == RegionPreset.Mountains ? 72000 :
                preset == RegionPreset.Tropical ? 100000 : 90000;
            for (int i = 0; i < candidates; i++)
            {
                var p = new Vector2((rng.Next() - .5f) * (Size - 60f), (rng.Next() - .5f) * (Size - 60f));
                float h = world.Height(p);
                if (Mathf.Abs(p.x - world.RiverX(p.y)) < world.river.halfWidth + 30f || Mathf.Abs(p.x - world.RoadAt(p.y)) < 22f || h > 400f) continue;
                if (preset == RegionPreset.Desert && Mathf.Abs(p.x - world.RiverX(p.y)) > 380f) continue;
                float grove = Mathf.PerlinNoise(ox + p.x * .0018f, oz + p.y * .0018f);
                if (grove < (preset == RegionPreset.Desert ? .65f : preset == RegionPreset.Tropical ? .29f : .34f)) continue;
                float dx = world.Height(p + Vector2.right * world.Spacing) - world.Height(p - Vector2.right * world.Spacing);
                float dz = world.Height(p + Vector2.up * world.Spacing) - world.Height(p - Vector2.up * world.Spacing);
                if (dx * dx + dz * dz > world.Spacing * world.Spacing * 1.4f) continue;
                bool approach = false;
                foreach (var site in world.sites)
                {
                    float along = Mathf.Abs(p.x - site.centre.x);
                    if (along > 430f && along < 1000f && Mathf.Abs(p.y - site.centre.y) < 90f) approach = true;
                    if (site == world.sites[1] && site.Preparation.Contains(p) && rng.Next() < .7f) approach = true;
                }
                if (approach || world.settlements.Exists(t => Vector2.Distance(t, p) < 145f)) continue;
                // Arid regions have low oasis palms; tropical regions have taller river palms.
                float height = preset == RegionPreset.Desert ? 7f + rng.Next() * 7f : preset == RegionPreset.Tropical ? 12f + rng.Next() * 13f : 14f + rng.Next() * 15f;
                world.trees.Add(new ForestTree { position = p, height = height });
            }
            return world;
        }

        void CreateLandscapeAnchors()
        {
            // Tutorial coordinates are a projection of gentle accessible ground, never a flattened preset parcel.
            var candidates = new List<Vector2>();
            for (int i = 0; i < publicRoad.Count; i += Mathf.Max(1, publicRoad.Count / 24))
            {
                var road = publicRoad[i];
                var tangent = publicRoad[Mathf.Min(publicRoad.Count - 1, i + 1)] - publicRoad[Mathf.Max(0, i - 1)];
                if (tangent.sqrMagnitude < .01f) continue;
                var side = new Vector2(-tangent.y, tangent.x).normalized;
                foreach (float direction in new[] { -1f, 1f })
                {
                    var point = road + side * direction * 100f;
                    if (!RegionBounds.Contains(point) || IsWater(point)) continue;
                    float relief = Mathf.Abs(Height(point + Vector2.right * 35f) - Height(point - Vector2.right * 35f)) +
                        Mathf.Abs(Height(point + Vector2.up * 35f) - Height(point - Vector2.up * 35f));
                    if (relief < 12f) candidates.Add(point);
                }
            }
            if (candidates.Count == 0)
            {
                for (int z = 32; z < Resolution - 32 && candidates.Count == 0; z += 8)
                for (int x = 32; x < Resolution - 32; x += 8)
                {
                    var point = new Vector2(x * Spacing - Size * .5f, z * Spacing - Size * .5f);
                    if (!IsWater(point) && Height(point) < 350f) { candidates.Add(point); break; }
                }
            }
            if (candidates.Count == 0) throw new InvalidOperationException("The region has no accessible land.");
            for (int i = 0; i < 3; i++)
            {
                var centre = candidates[Mathf.Min(candidates.Count - 1, i * candidates.Count / 3)];
                sites.Add(new AirportSite { name = PresetName, description = "Build anywhere in the region.", centre = centre,
                    elevation = Mathf.Clamp(Height(centre), 10f, 400f), landPrice = 0 });
            }
            settlements.Clear();
            for (int town = 0; town < 2; town++)
            {
                float best=float.NegativeInfinity;var centre=candidates[0];
                for(int i=2;i<publicRoad.Count-2;i++)
                {
                    var road=publicRoad[i];
                    if(settlements.Exists(p=>(p-road).sqrMagnitude<850f*850f))continue;
                    var tangent=(publicRoad[i+1]-publicRoad[i-1]).normalized;
                    var side=new Vector2(-tangent.y,tangent.x);int gentle=0;
                    for(int row=-5;row<=5;row+=2)
                    foreach(float direction in new[]{-1f,1f})
                    {
                        var p=road+tangent*(row*23f)+side*(direction*30f);
                        if(!RegionBounds.Contains(p)||IsWater(p))continue;
                        float h=Height(p);
                        float variation=Mathf.Max(Mathf.Abs(Height(p+Vector2.right*10f)-h),Mathf.Abs(Height(p-Vector2.right*10f)-h),
                            Mathf.Abs(Height(p+Vector2.up*10f)-h),Mathf.Abs(Height(p-Vector2.up*10f)-h));
                        if(variation<2f&&(!HasOcean||h>SeaLevel+1f))gentle++;
                    }
                    float score=gentle*1000f-Mathf.Abs(Height(road)-roadHeights[i])*12f;
                    if(score<=best)continue;best=score;centre=road;
                }
                settlements.Add(centre);
            }
        }

        static float Smooth(float a, float b, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, value));
        public float RiverX(float z)
        {
            if (generatorVersion < 3) return -1530f + Mathf.Sin(z * .0011f + (seed % 97) * .02f) * 130f;
            return river.X(z);
        }
        public float WaterHeight(float z) => River.WaterHeight(z);
        public float RoadAt(float z)
        {
            if (publicRoad == null || publicRoad.Count < 2) return RoadX;
            if (generatorVersion >= 4)
            {
                float nearest = float.MaxValue, result = publicRoad[0].x;
                for (int i = 1; i < publicRoad.Count; i++)
                {
                    var a = publicRoad[i-1]; var b = publicRoad[i];
                    float t = Mathf.Abs(b.y-a.y) < .001f ? 0f : Mathf.Clamp01((z-a.y)/(b.y-a.y));
                    float distance = Mathf.Abs(Mathf.Lerp(a.y,b.y,t)-z);
                    if (distance < nearest) { nearest = distance; result = Mathf.Lerp(a.x,b.x,t); }
                }
                return result;
            }
            float sample = Mathf.Clamp((z + Size * .5f) / Size * (publicRoad.Count - 1), 0f, publicRoad.Count - 1.001f);
            int index = (int)sample;
            return Mathf.Lerp(publicRoad[index].x, publicRoad[index + 1].x, sample - index);
        }

        public float Height(Vector2 p)
        {
            float x = Mathf.Clamp((p.x + Size * .5f) / Spacing, 0f, Resolution - 1.001f);
            float z = Mathf.Clamp((p.y + Size * .5f) / Spacing, 0f, Resolution - 1.001f);
            int ix = (int)x, iz = (int)z;
            return Mathf.Lerp(Mathf.Lerp(heights[iz * Resolution + ix], heights[iz * Resolution + ix + 1], x - ix),
                Mathf.Lerp(heights[(iz + 1) * Resolution + ix], heights[(iz + 1) * Resolution + ix + 1], x - ix), z - iz);
        }

        public int TreeCount(Rect area)
        {
            int count = 0;
            foreach (var tree in trees) if (!tree.removed && area.Contains(tree.position)) count++;
            return count;
        }
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
            BrushSamples(point, radius, land, (i, weight) =>
                volume += Mathf.Abs(heights[i] - BrushLevelHeight(heights[i], elevation, radius, weight)) * Spacing * Spacing);
            return volume;
        }
        public void LevelBrush(Vector2 point, float radius, float elevation, Rect land)
        {
            if (disturbance == null || disturbance.Length != heights.Length) disturbance = new float[Resolution * Resolution];
            BrushSamples(point, radius, land, (i, weight) => {
                heights[i] = BrushLevelHeight(heights[i], elevation, radius, weight);
                disturbance[i] = Mathf.Max(disturbance[i], weight);
            });
        }
        float BrushLevelHeight(float current, float target, float radius, float weight)
        {
            if (weight >= .99999f) return target;
            if (weight <= .00001f) return current;
            // An absolute shoulder envelope makes a stamp idempotent. Interpolating from the
            // newly edited height on every click would keep moving/charging for the feathered edge.
            // The flat core reaches the requested elevation immediately; the envelope opens smoothly
            // towards the untouched terrain at the brush edge, independent of its previous edits.
            float shoulder = Mathf.Max(Spacing * .35f, radius * .28f * .35f);
            float allowance = shoulder * (1f - weight) / weight;
            return Mathf.Clamp(current, target - allowance, target + allowance);
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

        /// <summary>Saved natural-prop removals never imply grading, nor remove neighbouring cluster slots.</summary>
        public bool GroundPropRemoved(int id)
        {
            if (clearedProps == null || clearedProps.Count == 0) return false;
            EnsureClearedPropIndex();
            return clearedPropIndex.Contains(id);
        }

        public void RemoveGroundProps(IEnumerable<int> ids)
        {
            clearedProps ??= new List<int>();
            EnsureClearedPropIndex();
            foreach (int id in ids)
                if (id > 0 && id <= GroundCoverScatter.MaxInstanceId && clearedPropIndex.Add(id)) clearedProps.Add(id);
            clearedPropCount = clearedProps.Count;
        }

        void EnsureClearedPropIndex()
        {
            if (clearedPropIndex != null && clearedPropSource == clearedProps && clearedPropCount == (clearedProps?.Count ?? 0)) return;
            clearedPropSource = clearedProps; clearedPropCount = clearedProps?.Count ?? 0;
            clearedPropIndex = clearedProps == null ? new HashSet<int>() : new HashSet<int>(clearedProps);
        }

        public bool Validate(out string problem)
        {
            problem = "The saved world is incomplete or uses an unsupported format.";
            if (clearedProps != null && (clearedProps.Count > GroundCoverScatter.MaxInstanceId ||
                clearedProps.Exists(id => id <= 0 || id > GroundCoverScatter.MaxInstanceId) ||
                new HashSet<int>(clearedProps).Count != clearedProps.Count)) return false;
            if (hasCoordinates && (double.IsNaN(latitude) || double.IsInfinity(latitude) || Math.Abs(latitude) > 85d ||
                double.IsNaN(longitude) || double.IsInfinity(longitude) || Math.Abs(longitude) > 180d ||
                !Finite(sourceExtent) || sourceExtent < 1000f || sourceExtent > 50000f)) return false;
            if (generatorVersion < 1 || generatorVersion > GeneratorVersion || heights == null || heights.Length != Resolution * Resolution ||
                forestVersion < 0 || forestVersion > ForestPopulation.Version ||
                sites == null || sites.Count != 3 || !Enum.IsDefined(typeof(RegionPreset), preset) || preset == RegionPreset.Random || trees == null || trees.Count > MaxTrees ||
                publicRoad == null || (generatorVersion < 4 ? publicRoad.Count != 65 : publicRoad.Count < 2 || publicRoad.Count > 512) || settlements == null || settlements.Count != 2) return false;
            if (generatorVersion >= 3 && HasRiver && (!Finite(river.centre) || Mathf.Abs(river.centre) > 2300f || !Finite(river.drift) || Mathf.Abs(river.drift) > .15f ||
                !Finite(river.amplitude) || river.amplitude < 0f || river.amplitude > 400f || !Finite(river.phase) ||
                !Finite(river.frequency) || river.frequency < .0001f || river.frequency > .01f ||
                !Finite(river.secondary) || river.secondary < 0f || river.secondary > .5f ||
                !Finite(river.halfWidth) || river.halfWidth < 20f || river.halfWidth > 150f)) return false;
            if (generatorVersion >= 4 && (!Finite(seaLevel) || seaLevel < 1f || seaLevel > 100f ||
                roadHeights == null || roadHeights.Length != publicRoad.Count || landCover == null || landCover.Length != heights.Length || Array.Exists(roadHeights, h => !Finite(h) || h < 0f || h > HeightRange))) return false;
            if (waterHeights != null && waterHeights.Length > 0 && (generatorVersion < 4 || waterHeights.Length != heights.Length ||
                Array.Exists(waterHeights, h => !Finite(h) || h < 0f || h > HeightRange))) return false;
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
