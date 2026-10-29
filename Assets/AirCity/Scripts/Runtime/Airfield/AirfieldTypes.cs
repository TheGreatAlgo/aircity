using UnityEngine;

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

    /// <summary>
    /// What kind of road an edge of the taxiway network is (TaxiwayEdge.road); None means it is a taxiway. Aircraft
    /// never use roads. Service roads are airside, for the airport's own vehicles; streets and highways are landside,
    /// for road traffic, and can't be built until it exists. Lanes, widths and who drives on them: RunwayRules.Road.
    /// </summary>
    public enum RoadClass
    {
        None = 0,
        Service = 1,
        Street = 2,
        Highway = 3,
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
        /// <summary>A stand's or de-icing bay's pad: uv0 in the stand's frame (slab joints line up with it and with
        /// its neighbours at the same angle), kept nearly clear of snow like aprons.</summary>
        Stand = 7,
    }

    /// <summary>ICAO aerodrome reference code letters, by wingspan (AircraftType.SizeCodeFor). Stands are built
    /// for a code and take any aircraft up to it.</summary>
    public enum SizeCode
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3,
        E = 4,
        F = 5,
    }

    public static class SizeCodes
    {
        public const int Count = 6;

        public static char Letter(this SizeCode c) => (char)('A' + (int)c);

        public static SizeCode Of(char letter) => (SizeCode)Mathf.Clamp(letter - 'A', 0, Count - 1);
    }

    /// <summary>What a stand is for.</summary>
    public enum StandKind
    {
        /// <summary>An aircraft parking stand beside a taxiway, at an angle to it.</summary>
        Parking = 0,
        /// <summary>A drive-through de-icing bay on a taxiway: aircraft stop in it, are sprayed, and taxi on.</summary>
        DeIcing = 1,
    }

    /// <summary>How aircraft leave a parking stand.</summary>
    public enum StandExit
    {
        /// <summary>Nose in; a tug pushes the aircraft back onto the taxiway.</summary>
        PushBack = 0,
        /// <summary>Nose out: the aircraft swings round on the stand as it comes in and taxis out under its own power.
        /// Needs more room.</summary>
        TaxiOut = 1,
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
        /// <summary>Dirt and grass aprons: under the roads, which stay visible across them.</summary>
        public const float ApronUnpaved = 0.011f;
        /// <summary>Road pavement (straights and road-only bends and junctions): under paved aprons, so a road across
        /// one is only its paint. Where a road meets a taxiway the junction is the taxiway's height.</summary>
        public const float Road = 0.013f;
        /// <summary>Paved (asphalt and concrete) aprons.</summary>
        public const float Apron = 0.015f;
        /// <summary>Stand pads and de-icing bays: over the plain aprons, under the taxiways they open onto.</summary>
        public const float Stand = 0.0175f;
        public const float Taxiway = 0.02f;
        public const float Runway = 0.024f;
        /// <summary>Crossing runways are stacked this far apart (by Runway.Layer), up to 3 levels.</summary>
        public const float RunwayLayerStep = 0.002f;
        public const float Paint = 0.031f;

        public static float ApronOf(PavementSurface surface) => surface.IsPaved() ? Apron : ApronUnpaved;

        public static float Of(AirfieldKind kind) => kind switch
        {
            AirfieldKind.Runway => Runway,
            AirfieldKind.Taxiway or AirfieldKind.Junction => Taxiway,
            AirfieldKind.Apron => Apron,
            AirfieldKind.Stand => Stand,
            AirfieldKind.Shoulder => Shoulder,
            _ => Ground,
        };
    }
}
