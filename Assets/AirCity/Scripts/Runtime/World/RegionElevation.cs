using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Bundled DEM foundations, available offline independently of the optional location downloader.</summary>
    public static class RegionElevation
    {
        [Serializable] sealed class SourceMetadata
        {
            public string location, sha256;
            public double latitude, longitude;
            public float sourceExtentMetres;
            public bool hasOcean;
        }
        static readonly Dictionary<RegionPreset, float[]> cached = new Dictionary<RegionPreset, float[]>();
        static readonly Dictionary<RegionPreset, byte[]> coverCache = new Dictionary<RegionPreset, byte[]>();
        public static string SourceName(RegionPreset preset) => preset switch {
            RegionPreset.Mountains => "Yosemite Valley, California", RegionPreset.Tropical => "Bora Bora, French Polynesia",
            RegionPreset.Desert => "Moab, Utah", _ => "Snoqualmie Valley, Washington" };
        public static float SourceExtent(RegionPreset preset) => preset == RegionPreset.Tropical ? 12800f : 8000f;

        public static RegionTerrainData Data(RegionPreset preset)
        {
            var asset = Resources.Load<TextAsset>("AirCityRegions/" + preset + "Source");
            if (asset == null) throw new InvalidOperationException("Missing bundled geographic metadata for " + preset + ".");
            var metadata = JsonUtility.FromJson<SourceMetadata>(asset.text);
            return new RegionTerrainData { latitude = metadata.latitude, longitude = metadata.longitude,
                extent = metadata.sourceExtentMetres, elevations = Samples(preset), cover = LandCover(preset),
                name = metadata.location, hasOcean = metadata.hasOcean, elevationSha256 = metadata.sha256 };
        }

        public static float[] Samples(RegionPreset preset)
        {
            if (cached.TryGetValue(preset, out var result)) return result;
            var asset = Resources.Load<TextAsset>("AirCityRegions/" + preset);
            if (asset == null) throw new InvalidOperationException("Missing bundled elevation foundation for " + preset + ". Run ArtSource/World/elevation_presets.py.");
            var bytes = asset.bytes;
            int count = ValleyWorld.Resolution * ValleyWorld.Resolution;
            if (bytes.Length != 8 + count * 2 || bytes[0] != 'A' || bytes[1] != 'C' || bytes[2] != 'D' || bytes[3] != 'E' ||
                BitConverter.ToUInt32(bytes, 4) != ValleyWorld.Resolution) throw new InvalidOperationException("Invalid bundled elevation foundation for " + preset + ".");
            result = new float[count];
            for (int i = 0; i < count; i++) result[i] = unchecked((short)(bytes[8 + i * 2] | bytes[9 + i * 2] << 8)) * .25f;
            cached.Add(preset, result); return result;
        }

        public static byte[] LandCover(RegionPreset preset)
        {
            if (coverCache.TryGetValue(preset, out var result)) return result;
            var asset = Resources.Load<TextAsset>("AirCityRegions/" + preset + "Cover");
            if (asset == null || asset.bytes.Length != ValleyWorld.Resolution * ValleyWorld.Resolution)
                throw new InvalidOperationException("Missing bundled WorldCover foundation for " + preset + ". Run ArtSource/World/landcover_presets.py.");
            result = asset.bytes; coverCache.Add(preset,result); return result;
        }

        /// <summary>Seeded rotations/reflections preserve real drainage and landforms. Preset seeds keep north up.</summary>
        public static float Sample(float[] source, int x, int z, int orientation)
        {
            int last = ValleyWorld.Resolution - 1;
            if ((orientation & 4) != 0) x = last - x;
            for (int i = 0; i < (orientation & 3); i++) { int oldX = x; x = z; z = last - oldX; }
            return source[z * ValleyWorld.Resolution + x];
        }
        public static byte Cover(byte[] source, int x, int z, int orientation)
        {
            int last = ValleyWorld.Resolution - 1;
            if ((orientation & 4) != 0) x = last - x;
            for (int i = 0; i < (orientation & 3); i++) { int oldX = x; x = z; z = last - oldX; }
            return source[z * ValleyWorld.Resolution + x];
        }
    }
}
