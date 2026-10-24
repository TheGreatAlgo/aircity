namespace AirCity.Airfield
{
    /// <summary>What a runway or taxiway is built of. Dirt and grass suit light aircraft only
    /// (AircraftType.unpavedRunways) and can't be lit; grass grows and has to be mowed. Asphalt and concrete look
    /// different, wear at different rates and cost different amounts.</summary>
    public enum PavementSurface
    {
        Dirt = 0,
        Asphalt = 1,
        Concrete = 2,
        Grass = 3,
    }

    public static class Surfaces
    {
        public const int Count = 4;

        public static bool IsPaved(this PavementSurface s) => s == PavementSurface.Asphalt || s == PavementSurface.Concrete;
    }

    /// <summary>What a maintenance team does to a runway or taxiway: clear its snow or cut its grass.</summary>
    public enum WorkKind
    {
        Plough = 0,
        Mow = 1,
    }

    /// <summary>Runway lighting, bought as an upgrade. Without lighting a runway closes at night.</summary>
    public enum RunwayLighting
    {
        None = 0,
        /// <summary>Edge, threshold / end lights and PAPI (approach slope lights).</summary>
        Basic = 1,
        /// <summary>Basic plus centreline lights and an approach lighting system with sequenced flashers.</summary>
        Precision = 2,
    }

    /// <summary>The weather now, for the whole airfield.</summary>
    public enum SurfaceCondition
    {
        Dry = 0,
        /// <summary>Rain: longer landing rolls, dirt goes soft, lying snow melts faster.</summary>
        Wet = 1,
        /// <summary>Snowing: snow builds up on every runway and taxiway until it is ploughed
        /// (RunwayRules snow constants).</summary>
        Snow = 2,
    }

    /// <summary>Glow behaviours (AirCity/AirfieldLightGlow types).</summary>
    public enum AirfieldLightType
    {
        Steady = 0,
        /// <summary>Precision approach path indicator: white above its angle, red below.</summary>
        Papi = 1,
        /// <summary>Sequenced approach flasher ("the rabbit").</summary>
        Flasher = 2,
    }

    /// <summary>Kinds of airfield surface mesh (uv1.x for AirCity/AirfieldSurface).</summary>
    public enum AirfieldKind
    {
        Apron = 0,
        Runway = 1,
        Taxiway = 2,
        Shoulder = 3,
        Ground = 4,
        /// <summary>Taxiway junction polygons: world-mapped, no slab joints.</summary>
        Junction = 5,
        /// <summary>The graded grass beside a runway or taxiway (its strip): mown, never ploughed. uv0.x runs
        /// outwards from the pavement edge, as if the pavement weren't there (SurfaceBuilder.Verges).</summary>
        Verge = 6,
    }

    /// <summary>
    /// Heights of the airfield layers above the ground (metres). Overlapping pavements are stacked rather than
    /// cut against each other: a taxiway junction runs under the runway, which draws on top. The steps are a few
    /// millimetres, enough for the reversed float depth buffer at any zoom, and all of it stays under the
    /// aircraft contact shadow (3 cm; paint doesn't write depth), so aircraft keep standing at y = 0.
    /// </summary>
    public static class AirfieldHeights
    {
        public const float Ground = 0f;
        /// <summary>Runway verges; taxiway verges sit a little higher, so they win where the two overlap.</summary>
        public const float RunwayVerge = 0.004f, TaxiwayVerge = 0.006f;
        public const float Shoulder = 0.01f;
        public const float Apron = 0.015f;
        public const float Taxiway = 0.02f;
        public const float Runway = 0.024f;
        /// <summary>Crossing runways are stacked this far apart (by Runway.Layer), up to 3 levels.</summary>
        public const float RunwayLayerStep = 0.002f;
        public const float Paint = 0.031f;

        public static float Of(AirfieldKind kind) => kind switch
        {
            AirfieldKind.Runway => Runway,
            AirfieldKind.Taxiway or AirfieldKind.Junction => Taxiway,
            AirfieldKind.Apron => Apron,
            AirfieldKind.Shoulder => Shoulder,
            _ => Ground,
        };
    }
}
