using System;
using System.Globalization;
using AirCity.World;

namespace AirCity.Game
{
    public readonly struct RegionLocation
    {
        public readonly string name, description;
        public readonly double latitude, longitude;
        public readonly RegionPreset preset;
        public readonly int seed;
        public RegionLocation(string name, string description, double latitude, double longitude, RegionPreset preset, int seed)
        { this.name = name; this.description = description; this.latitude = latitude; this.longitude = longitude; this.preset = preset; this.seed = seed; }
    }

    /// <summary>The bundled real locations. A location fixes the landscape; seeds stay an internal detail.</summary>
    public static class RegionLocations
    {
        public static readonly RegionLocation[] Curated = {
            new RegionLocation("Snoqualmie Valley", "Washington, USA · Woodland", 47.665, -121.928, RegionPreset.Woodland, 19770616),
            new RegionLocation("Yosemite Valley", "California, USA · Mountains", 37.735, -119.597, RegionPreset.Mountains, 873241),
            new RegionLocation("Bora Bora", "French Polynesia · Tropical", -16.503, -151.744, RegionPreset.Tropical, 445090),
            new RegionLocation("Moab", "Utah, USA · Desert", 38.57, -109.55, RegionPreset.Desert, 692835)
        };
        public static RegionLocation ForPreset(RegionPreset preset) => Curated[Math.Min(3, Math.Max(0, (int)preset))];
        public static string Coordinates(double latitude, double longitude) => string.Format(CultureInfo.InvariantCulture,
            "{0:0.0000}°{1}  {2:0.0000}°{3}", Math.Abs(latitude), latitude < 0 ? "S" : "N", Math.Abs(longitude), longitude < 0 ? "W" : "E");
    }
}
