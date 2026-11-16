using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AirCity.Tests.EditMode")]

namespace AirCity.World
{
    /// <summary>Real geographic samples: south-first rows, east across, elevations in metres above sea level.</summary>
    [Serializable]
    public sealed class RegionTerrainData
    {
        public const int Resolution = 513;
        public double latitude, longitude;
        public float extent;
        public float[] elevations;
        public byte[] cover;
        public string name;
        public bool hasOcean;
        public string elevationSha256, coverSha256;
    }

    public readonly struct RegionTerrainProgress
    {
        public readonly float fraction;
        public readonly string message;
        public RegionTerrainProgress(float fraction, string message) { this.fraction = fraction; this.message = message; }
    }
}
