using AirCity.Aircraft;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>Why a runway can't take an aircraft (the first reason found).</summary>
    public enum RunwayProblem
    {
        None = 0,
        TooShort,
        TooNarrow,
        NeedsPavement,
        /// <summary>Night, and the runway has no lighting.</summary>
        Unlit,
        /// <summary>Too much snow: closed until it is ploughed.</summary>
        NeedsPloughing,
        /// <summary>Closed while the ploughs are out.</summary>
        Ploughing,
        /// <summary>Grass strip: the grass has grown too long, closed until it is mowed.</summary>
        GrassTooLong,
        /// <summary>Closed while the mowers are out.</summary>
        Mowing,
        /// <summary>Worn out: closed until repaired.</summary>
        ClosedForRepair,
    }

    public struct RunwayCheck
    {
        public RunwayProblem problem;
        /// <summary>Length the aircraft needs here, in these conditions (metres).</summary>
        public float requiredLength;
        public float requiredWidth;

        public bool Ok => problem == RunwayProblem.None;

        public override string ToString() => problem switch
        {
            RunwayProblem.None => "OK",
            RunwayProblem.TooShort => $"needs {requiredLength:N0} m",
            RunwayProblem.TooNarrow => $"needs {requiredWidth:0} m wide",
            RunwayProblem.NeedsPavement => "needs a paved runway",
            RunwayProblem.Unlit => "unlit at night",
            RunwayProblem.NeedsPloughing => "snow: needs ploughing",
            RunwayProblem.Ploughing => "closed for ploughing",
            RunwayProblem.GrassTooLong => "grass too long: needs mowing",
            RunwayProblem.Mowing => "closed for mowing",
            _ => "closed for repair",
        };
    }

    /// <summary>
    /// The rules runways and taxiways follow: ICAO widths by aircraft size code, the length an aircraft needs on a
    /// surface in some weather, designators from headings, and wear. Gameplay constants live here so they can be
    /// tuned in one place.
    /// </summary>
    public static class RunwayRules
    {
        /// <summary>Runway width presets (metres); the smallest serves size code A, the largest code F.</summary>
        public static readonly float[] RunwayWidths = { 18f, 23f, 30f, 45f, 60f };

        /// <summary>Taxiway width presets (metres), one per size code A to E.</summary>
        public static readonly float[] TaxiwayWidths = { 7.5f, 10.5f, 15f, 18f, 23f };

        /// <summary>Minimum runway width for an aircraft size code (ICAO Annex 14).</summary>
        public static float RunwayWidthFor(char sizeCode) => sizeCode switch
        {
            'A' => 18f,
            'B' => 23f,
            'C' => 30f,
            'D' or 'E' => 45f,
            _ => 60f,
        };

        /// <summary>Minimum taxiway width for an aircraft size code (ICAO Annex 14).</summary>
        public static float TaxiwayWidthFor(char sizeCode) => sizeCode switch
        {
            'A' => 7.5f,
            'B' => 10.5f,
            'C' => 15f,
            'D' => 18f,
            'E' => 23f,
            _ => 25f,
        };

        /// <summary>Paved shoulder each side of a runway (metres).</summary>
        public static float ShoulderWidth(PavementSurface surface, float width) =>
            !surface.IsPaved() ? 0f : width >= 44f ? 7.5f : width >= 29f ? 4.5f : 0f;

        /// <summary>Centreline radius taxiway curves and junction fillets are built for, from the taxiway width
        /// (about 19 m for code A, 38 m for code C).</summary>
        public static float TaxiwayTurnRadius(float width) => 2.5f * width;

        /// <summary>Distance from the runway centreline to a taxiway's runway-holding position.</summary>
        public static float HoldingDistance(float runwayWidth) => runwayWidth < 25f ? 30f : runwayWidth < 40f ? 60f : 75f;

        // ------------------------------------------------------------------ length

        /// <summary>Takeoff and landing distance factors for a surface, the weather, the snow lying on it (cm) and,
        /// on grass, the grass length (cm). Dirt rolls slower and brakes worse; grass more so, and long grass
        /// drags on the takeoff run; rain, or snow falling on a ploughed runway, lengthens the landing roll; a
        /// layer of snow lengthens both. (UK CAA Safety Sense 7 for grass.) Null = closed until ploughed or
        /// mowed.</summary>
        public static Vector2? LengthFactors(PavementSurface surface, SurfaceCondition condition, float snowCm = 0f,
            float grassCm = CutGrass)
        {
            if (SnowClosed(surface, snowCm) || GrassClosed(surface, grassCm)) return null;
            bool slippery = condition != SurfaceCondition.Dry || snowCm > ContaminatedSnow;
            if (surface == PavementSurface.Dirt)
                return slippery ? new Vector2(1.3f, 1.3f) : new Vector2(1.15f, 1.15f);
            if (surface == PavementSurface.Grass)
            {
                var f = slippery ? new Vector2(1.3f, 1.35f) : new Vector2(1.2f, 1.15f);
                if (grassCm > LongGrass) f.x *= 1.15f;
                return f;
            }
            if (snowCm > ContaminatedSnow) return new Vector2(1.15f, 1.4f);
            return slippery ? new Vector2(1f, 1.15f) : new Vector2(1f, 1f);
        }

        /// <summary>Runway length an aircraft type needs on a surface in some weather with some snow lying and
        /// grass grown (metres); infinity when the runway is closed until ploughed or mowed.</summary>
        public static float RequiredLength(AircraftType type, PavementSurface surface, SurfaceCondition condition,
            float snowCm = 0f, float grassCm = CutGrass)
        {
            var f = LengthFactors(surface, condition, snowCm, grassCm);
            if (f == null) return float.PositiveInfinity;
            return Mathf.Max(type.takeoffDistance * f.Value.x, type.landingDistance * f.Value.y);
        }

        /// <summary>Can this runway take this aircraft type now? Checks pavement, width, ploughs / mowers at work,
        /// snow, grass, length, wear and lighting, in that order.</summary>
        public static RunwayCheck Check(AircraftType type, PavementSurface surface, float length, float width,
            RunwayLighting lighting, float wear, SurfaceCondition condition, bool night, float snowCm = 0f,
            bool ploughing = false, float grassCm = CutGrass, bool mowing = false)
        {
            var check = new RunwayCheck
            {
                requiredLength = RequiredLength(type, surface, condition, snowCm, grassCm),
                requiredWidth = RunwayWidthFor(type.SizeCode),
            };
            if (!surface.IsPaved() && !type.unpavedRunways) check.problem = RunwayProblem.NeedsPavement;
            else if (width + 0.01f < check.requiredWidth) check.problem = RunwayProblem.TooNarrow;
            else if (ploughing) check.problem = RunwayProblem.Ploughing;
            else if (mowing) check.problem = RunwayProblem.Mowing;
            else if (SnowClosed(surface, snowCm)) check.problem = RunwayProblem.NeedsPloughing;
            else if (GrassClosed(surface, grassCm)) check.problem = RunwayProblem.GrassTooLong;
            else if (length + 0.01f < check.requiredLength) check.problem = RunwayProblem.TooShort;
            else if (wear >= ClosedWear) check.problem = RunwayProblem.ClosedForRepair;
            else if (night && lighting == RunwayLighting.None) check.problem = RunwayProblem.Unlit;
            return check;
        }

        /// <summary>Can a taxiway of this surface and width carry this aircraft type (ignoring snow and grass)?</summary>
        public static bool CanTaxi(AircraftType type, PavementSurface surface, float width) =>
            (surface.IsPaved() || type.unpavedRunways) && width + 0.01f >= TaxiwayWidthFor(type.SizeCode);

        // ------------------------------------------------------------------ snow

        /// <summary>Heavy snowfall (cm per hour), and melting with no snow falling (dry / raining).</summary>
        public const float SnowfallRate = 3f, MeltRate = 0.5f, RainMeltRate = 2f;

        /// <summary>Snow (cm) above which a paved runway counts as contaminated: longer takeoff and landing.</summary>
        public const float ContaminatedSnow = 0.3f;

        /// <summary>Snow (cm) at which a paved runway or taxiway closes until ploughed. Dirt and grass are unusable
        /// sooner.</summary>
        public const float ClosingSnow = 3f, UnpavedClosingSnow = 1f;

        /// <summary>Snow (cm) at which the depot sends its ploughs by itself, before a runway closes.</summary>
        public const float PloughingSnow = 1.5f, UnpavedPloughingSnow = 0.6f;

        public static bool SnowClosed(PavementSurface surface, float snowCm) =>
            snowCm >= (surface.IsPaved() ? ClosingSnow : UnpavedClosingSnow);

        /// <summary>Snow worth ploughing (the depot's automatic dispatch).</summary>
        public static bool NeedsPloughing(PavementSurface surface, float snowCm) =>
            snowCm >= (surface.IsPaved() ? PloughingSnow : UnpavedPloughingSnow);

        // ------------------------------------------------------------------ grass

        /// <summary>Grass strips are cut to CutGrass (cm). Above LongGrass the takeoff run lengthens and the depot
        /// sends its mowers; at ClosingGrass the strip closes until it is mowed.</summary>
        public const float CutGrass = 5f, LongGrass = 15f, ClosingGrass = 25f;

        /// <summary>Growth while there's no snow (cm per game hour): about 1 cm a day, so a strip wants mowing
        /// every week and a half.</summary>
        public const float GrassGrowth = 1f / 24f;

        public static bool GrassClosed(PavementSurface surface, float grassCm) =>
            surface == PavementSurface.Grass && grassCm >= ClosingGrass;

        public static bool NeedsMowing(PavementSurface surface, float grassCm) =>
            surface == PavementSurface.Grass && grassCm >= LongGrass;

        // ------------------------------------------------------------------ verges (runway and taxiway strips)

        /// <summary>
        /// Half width (from the centreline) of the graded, mown grass beside a runway: the graded part of its
        /// runway strip (ICAO Annex 14 3.4.8: 75 m for code 3 / 4 runways, 40 m for code 2, 30 m for code 1). By
        /// width: 30 m and wider are code 3 / 4, 23 m code 2. Grass and dirt strips get the code 1 strip.
        /// </summary>
        public static float RunwayVerge(PavementSurface surface, float width) =>
            !surface.IsPaved() ? Mathf.Max(30f, width * 0.5f + 12f) : width >= 29f ? 75f : width >= 22f ? 40f : 30f;

        /// <summary>How far the runway strip's verges run beyond each end of the runway (metres).</summary>
        public static float RunwayVergeOverrun(PavementSurface surface) => surface.IsPaved() ? 60f : 30f;

        /// <summary>Half width (from the centreline) of the graded grass beside a paved taxiway: the graded part of
        /// its taxiway strip (ICAO Annex 14 3.11.3: 10.25 m code A, 11 m B, 12.5 m C, 19 m D, 22 m E), by
        /// width; never less than 4 m of grass each side. Unpaved taxiways and service roads have none.</summary>
        public static float TaxiwayVerge(float width) =>
            width * 0.5f + Mathf.Max(4f, (width < 9f ? 10.25f : width < 12f ? 11f : width < 16f ? 12.5f : width < 20f ? 19f : 22f) - width * 0.5f);

        // ------------------------------------------------------------------ service roads

        /// <summary>Airside service road width: two 3.5 m lanes. Roads carry the airport's vehicles only.</summary>
        public const float RoadWidth = 7f;

        // ------------------------------------------------------------------ designators

        /// <summary>Compass bearing (degrees clockwise from north = +Z) of a direction on the ground.</summary>
        public static float Bearing(Vector3 direction)
        {
            float b = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            return b < 0f ? b + 360f : b;
        }

        /// <summary>Runway number for a landing bearing: tens of degrees, 1..36 (north is 36, not 0).</summary>
        public static int Number(float bearing)
        {
            int n = Mathf.RoundToInt(bearing / 10f) % 36;
            return n == 0 ? 36 : n;
        }

        /// <summary>"09", "27L": ICAO style, always two digits.</summary>
        public static string Designator(int number, char letter = ' ') =>
            number.ToString("00") + (letter == ' ' ? "" : letter.ToString());

        // ------------------------------------------------------------------ wear

        /// <summary>Wear at which a runway or taxiway closes until it is repaired.</summary>
        public const float ClosedWear = 1f;

        /// <summary>Wear above which the condition is reported as poor (time to schedule a repair).</summary>
        public const float PoorWear = 0.7f;

        /// <summary>
        /// Wear one takeoff or landing adds. Pavement damage grows steeply with weight, so light aircraft barely
        /// wear pavement but do wear dirt and turf. Reference: about 4,000 narrowbody movements wear out asphalt,
        /// 12,000 concrete; about 600 light-aircraft movements wear out a dirt strip, 850 a grass strip (half
        /// that when wet).
        /// Taxiing wears taxiways at half this rate.
        /// </summary>
        public static float WearPerMovement(PavementSurface surface, float maxTakeoffMass, SurfaceCondition condition)
        {
            float load = Mathf.Pow(Mathf.Max(maxTakeoffMass, 0f) / 50000f, 1.5f) + 0.02f;
            float rate = surface switch
            {
                PavementSurface.Dirt => 0.071f,
                PavementSurface.Grass => 0.05f,
                PavementSurface.Asphalt => 1.27e-4f,
                _ => 0.42e-4f,
            };
            if (!surface.IsPaved() && condition == SurfaceCondition.Wet) rate *= 2f;
            return rate * load;
        }
    }
}
