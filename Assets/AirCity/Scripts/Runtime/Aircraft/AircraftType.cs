using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Aircraft
{
    public enum AircraftCategory
    {
        GeneralAviation = 0,
        Airliner = 1,
    }

    /// <summary>Places ground equipment and vehicles attach to or service.</summary>
    public enum ServicePointType
    {
        PassengerDoor = 0,
        CargoDoor = 1,
        Fuel = 2,
        GroundPower = 3,
        Water = 4,
        Lavatory = 5,
        TowAttach = 6,
        WheelChock = 7,
        TieDown = 8,
    }

    public enum AircraftLightType
    {
        Navigation = 0,   // steady; red port, green starboard, white tail; each visible only in its sector
        Strobe = 1,       // white double flash
        Beacon = 2,       // red anti-collision flash, on whenever an engine runs
        Landing = 3,
        Taxi = 4,
        Logo = 5,         // floodlights aimed at the fin
    }

    /// <summary>
    /// Everything the game knows about one aircraft type: dimensions, gear, service points and lights for
    /// gameplay, plus the data the livery system paints with. Generated with the model by the aircraft
    /// pipeline (ArtSource/Aircraft) as &lt;Model&gt;.aircraft and imported by AircraftTypeImporter, so it is
    /// read-only: change the generator, not this asset.
    ///
    /// Positions are in the aircraft's local space: origin on the ground under the main gear, +Z = nose,
    /// +X = starboard (right), metres.
    /// </summary>
    public class AircraftType : ScriptableObject
    {
        [Serializable]
        public struct Wheel
        {
            public string name;           // the wheel's transform in the model
            public Vector3 position;      // axle centre
            public float radius;
            public bool steerable;
        }

        [Serializable]
        public struct ServicePoint
        {
            public string name;
            public ServicePointType type;
            [Tooltip("Door sill centre, hatch, attach point or (chocks, tie-downs) ground point.")]
            public Vector3 position;
            [Tooltip("Outward direction: where equipment approaches from.")]
            public Vector3 normal;
            [Tooltip("Opening width and height, metres (0 where it doesn't apply).")]
            public Vector2 size;
        }

        [Serializable]
        public struct LightPoint
        {
            public string name;
            public AircraftLightType type;
            public Vector3 position, direction;
            [Tooltip("Beam / visibility sector in degrees; 360 = all round.")]
            public float spread;
            public Color color;
        }

        [Serializable]
        public struct Scheme
        {
            public LiveryStyle style;
            [Tooltip("RGB zone mask over the body atlas.")]
            public Texture2D mask;
            [Tooltip("Registration box: station from/to and height from/to along the skin, metres.")]
            public Vector4 registrationBox;
        }

        /// <summary>One side of the registration patch: the part of the body atlas it shows and the
        /// metric mapping u = k * sign * station + tu, v = k * height + tv.</summary>
        [Serializable]
        public struct RegistrationSide
        {
            public Rect crop;
            public float k, tu, tv, sign;
        }

        [Serializable]
        public struct ExampleLivery
        {
            public string name;
            public LiveryStyle style;
            public Color baseColor, color1, color2, color3, registrationColor;
            public string registration;
            public Texture2D logo;
        }

        [Header("Identity")]
        public string model;
        public string displayName;
        public AircraftCategory category;

        [Header("Dimensions")]
        public Bounds bounds;
        public Wheel[] wheels = Array.Empty<Wheel>();
        [Tooltip("Ground point of the nose-gear steering axis.")]
        public Vector3 steeringPivot;
        public float maxSteeringAngle = 30f;

        [Header("Performance")]
        [Tooltip("Takeoff distance at maximum weight, sea level, standard day (metres, to clear 15 m).")]
        public float takeoffDistance;
        [Tooltip("Landing distance at maximum landing weight, sea level, standard day (metres, from 15 m).")]
        public float landingDistance;
        [Tooltip("Maximum takeoff mass, kg. Wears runways and taxiways.")]
        public float maxTakeoffMass;
        [Tooltip("Can use unpaved (dirt) runways. Jets can't.")]
        public bool unpavedRunways;

        [Header("Gameplay points")]
        public ServicePoint[] points = Array.Empty<ServicePoint>();
        public LightPoint[] lights = Array.Empty<LightPoint>();

        [Header("Paint")]
        public int atlasSize = 2048;
        public float texelsPerMetre;
        [Tooltip("Baked ambient occlusion + panel lines, multiplied onto the paint.")]
        public Texture2D detail;
        [Tooltip("Grime for the Lit shader's detail albedo (0.5 = clean); per-aircraft amount on the applier.")]
        public Texture2D dirt;
        public Texture2D registrationDirt;
        [Tooltip("Tail logo placement over the body atlas: R, G = logo uv * coverage, B = coverage.")]
        public Texture2D logoUV;
        [Tooltip("Tail logo area: station from/to, height from/to, metres (its aspect ratio fits the logo).")]
        public Vector4 logoBox;
        public Texture2D groundShadow;
        public RegistrationFont font;
        public Scheme[] schemes = Array.Empty<Scheme>();

        public int registrationTextureWidth = 1024;
        public int registrationHalfHeight = 216;
        public RegistrationSide port, starboard;

        [Header("Reference")]
        public Texture2D defaultBody;
        public Texture2D defaultRegistration;
        public Texture2D uvTemplate;
        public ExampleLivery[] examples = Array.Empty<ExampleLivery>();

        // ------------------------------------------------------------------ derived

        public float Wingspan => bounds.size.x;
        public float Length => bounds.size.z;
        public float Height => bounds.size.y;

        /// <summary>ICAO aerodrome reference code letter, from the wingspan. Stands, taxiways and runways
        /// are rated by it: A (&lt; 15 m) small props, C (&lt; 36 m) A320 / 737, E (&lt; 65 m) 777 / A350.</summary>
        public char SizeCode => SizeCodeFor(Wingspan);

        public static char SizeCodeFor(float wingspan) =>
            wingspan < 15f ? 'A' : wingspan < 24f ? 'B' : wingspan < 36f ? 'C' : wingspan < 52f ? 'D' : wingspan < 65f ? 'E' : 'F';

        /// <summary>The longer of the takeoff and landing distances: the runway length this type needs on a dry,
        /// paved runway (Airfield.RunwayRules adds surface and weather factors).</summary>
        public float RunwayLength => Mathf.Max(takeoffDistance, landingDistance);

        /// <summary>Distance from the main gear to the nose gear.</summary>
        public float Wheelbase => steeringPivot.z;

        /// <summary>Tightest turn at full steering, measured to the main-gear midpoint.</summary>
        public float MinTurnRadius => Wheelbase / Mathf.Tan(Mathf.Max(maxSteeringAngle, 1f) * Mathf.Deg2Rad);

        /// <summary>The scheme for a livery's style, falling back to the first scheme.</summary>
        public Scheme SchemeFor(AircraftLivery livery)
        {
            foreach (var s in schemes)
                if (s.style == livery.style) return s;
            return schemes.Length > 0 ? schemes[0] : default;
        }

        public bool TryGetPoint(string pointName, out ServicePoint point)
        {
            foreach (var p in points)
            {
                if (p.name != pointName) continue;
                point = p;
                return true;
            }
            point = default;
            return false;
        }

        public IEnumerable<ServicePoint> PointsOfType(ServicePointType type)
        {
            foreach (var p in points)
                if (p.type == type) yield return p;
        }
    }
}
