using System;
using System.Globalization;
using AirCity.World;

namespace AirCity.Game
{
    /// <summary>The calendar and climate share the saved simulation clock; no wall-clock time or global random state.</summary>
    public static class CareerClock
    {
        public const float SimulationSecondsPerDay = 1440f;
        public static readonly DateTime Epoch = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Unspecified);

        public static DateTime Date(float operatingSeconds)
        {
            double seconds = float.IsNaN(operatingSeconds) || operatingSeconds < 0f ? 0d : operatingSeconds * 60d;
            seconds = Math.Min(seconds, (DateTime.MaxValue - Epoch).TotalSeconds - 1d);
            return Epoch.AddSeconds(seconds);
        }

        public static string DateLabel(float operatingSeconds) => Date(operatingSeconds).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        public static string TimeLabel(float operatingSeconds) => Date(operatingSeconds).ToString("HH:mm", CultureInfo.InvariantCulture);
        public static string Season(float operatingSeconds)
        {
            int month = Date(operatingSeconds).Month;
            return month >= 3 && month <= 5 ? "Spring" : month >= 6 && month <= 8 ? "Summer" :
                month >= 9 && month <= 11 ? "Autumn" : "Winter";
        }

        public static float Temperature(ValleyWorld world, float operatingSeconds)
        {
            var date = Date(operatingSeconds);
            float mean = 12f, seasonalRange = 12f, dailyRange = 4f;
            switch (world.preset)
            {
                case RegionPreset.Mountains: mean = 1f; seasonalRange = 11f; dailyRange = 5f; break;
                case RegionPreset.Tropical: mean = 27f; seasonalRange = 2f; dailyRange = 3f; break;
                case RegionPreset.Desert: mean = 28f; seasonalRange = 10f; dailyRange = 9f; break;
            }
            double annual = Math.Cos((date.DayOfYear - 196d) / (DateTime.IsLeapYear(date.Year) ? 366d : 365d) * Math.PI * 2d);
            double daily = Math.Cos((date.TimeOfDay.TotalHours - 15d) / 24d * Math.PI * 2d);
            double phase = unchecked((uint)world.seed) / (double)uint.MaxValue * Math.PI * 2d;
            double weather = Math.Sin((date - Epoch).TotalDays * .7d + phase) * 2d;
            return (float)(mean + annual * seasonalRange + daily * dailyRange + weather);
        }
    }
}
