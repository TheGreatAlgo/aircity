using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>A paved apron rectangle (stands come later).</summary>
    [Serializable]
    public struct ApronArea
    {
        public Vector3 centre;
        public Vector2 size;
        [Tooltip("Degrees clockwise from north (+Z).")]
        public float rotation;
        public PavementSurface surface;
    }

    /// <summary>
    /// The root of an airport: its runways (child Runway objects), taxiways and service roads (a child
    /// TaxiwayNetwork), aprons, the grass they sit on, its maintenance depots (child MaintenanceDepots, with the
    /// work for them in Maintenance) and the surface weather. Holds the shared airfield materials.
    ///
    ///   var rwy = airport.AddRunway(a, b, PavementSurface.Concrete, 45f);
    ///   airport.Taxiways.Connect(...);
    ///   airport.Condition = SurfaceCondition.Snow;   // snow builds up everywhere...
    ///   airport.Simulate(seconds);            // ...as game time passes (Update does this while playing)
    ///   airport.PloughAll();                  // the depots' ploughs clear it; runways close while they work
    ///   airport.RunwaysFor(type)              // the runways an aircraft type can use now
    ///
    /// Everything is in the Airport's local space and generated from its data when enabled, so scenes and saves
    /// only store the layout. The ground is flat for now; terrain flattening comes with the terrain system.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class Airport : MonoBehaviour
    {
        [Header("Materials (AirfieldSetup creates them)")]
        public Material concrete;
        public Material asphalt;
        public Material dirt;
        [Tooltip("Grass runways and taxiways (mown, with ragged edges).")]
        public Material grassStrip;
        [Tooltip("The field around everything.")]
        public Material grass;
        public Material paint;
        public Material lightGlow;
        public Material fixture;

        [Header("Layout")]
        [Tooltip("Side of the square of grass under the airport (metres); 0 = none (terrain provides the ground).")]
        public float groundSize = 6000f;
        public List<ApronArea> aprons = new List<ApronArea>();

        [Header("Weather")]
        [SerializeField] SurfaceCondition condition;
        [Tooltip("Snow lying on the grass and other unploughed ground (cm).")]
        [Min(0f)] public float groundSnow;
        [Tooltip("Game seconds per real second while playing (until the game clock exists): weather, grass growth and " +
                 "the depot's vehicles all run on it, so a higher value fast-forwards everything together.")]
        public float timeScale = 1f;

        readonly List<Runway> runways = new List<Runway>();
        AirfieldPart groundPart, apronPart;
        bool dirty;
        AirportMaintenance maintenance;
        Pavement pavement;
        int layoutVersion, pavementVersion = -1;

        static readonly int WetnessId = Shader.PropertyToID("_AirfieldWetness");
        static readonly int SnowId = Shader.PropertyToID("_AirfieldSnow");

        /// <summary>Night for runway rules: the same switch that turns the aircraft lights' glows up.</summary>
        public static bool IsNight => AircraftLights.Glow > 0.5f;

        public SurfaceCondition Condition
        {
            get => condition;
            set
            {
                condition = value;
                ApplyGlobals();
            }
        }

        public IReadOnlyList<Runway> Runways
        {
            get
            {
                runways.Clear();
                GetComponentsInChildren(false, runways);
                return runways;
            }
        }

        public TaxiwayNetwork Taxiways
        {
            get
            {
                var t = GetComponentInChildren<TaxiwayNetwork>();
                if (t != null) return t;
                var go = new GameObject("Taxiways");
                go.transform.SetParent(transform, false);
                return go.AddComponent<TaxiwayNetwork>();
            }
        }

        public Material SurfaceMaterial(PavementSurface s) => s switch
        {
            PavementSurface.Concrete => concrete,
            PavementSurface.Asphalt => asphalt,
            PavementSurface.Grass => grassStrip,
            _ => dirt,
        };

        /// <summary>The maintenance depots (enabled ones; a new list each time).</summary>
        public List<MaintenanceDepot> Depots => new List<MaintenanceDepot>(GetComponentsInChildren<MaintenanceDepot>(false));

        /// <summary>The first maintenance depot (null if the airport has none yet).</summary>
        public MaintenanceDepot Depot => GetComponentInChildren<MaintenanceDepot>();

        /// <summary>The work for the depots: the job queue, dispatch, stepping the teams.</summary>
        public AirportMaintenance Maintenance => maintenance ??= new AirportMaintenance(this);

        /// <summary>Where there's pavement to drive on (rebuilt after the layout changes).</summary>
        public Pavement Pavement
        {
            get
            {
                if (pavement != null && pavementVersion == layoutVersion) return pavement;
                pavement = new Pavement();
                pavementVersion = layoutVersion;
                foreach (var r in Runways) pavement.Add(r.SurfaceMesh, Matrix4x4.identity);
                var t = GetComponentInChildren<TaxiwayNetwork>();
                if (t != null) pavement.Add(t.SurfaceMesh, Matrix4x4.identity);
                pavement.Add(apronPart?.Mesh, Matrix4x4.identity);
                return pavement;
            }
        }

        /// <summary>Runways, taxiways or aprons were rebuilt.</summary>
        internal void LayoutChanged() => layoutVersion++;

        /// <summary>Is a ground point on (or within margin of) an apron?</summary>
        public static bool OnApron(ApronArea a, Vector2 p, float margin = 0f)
        {
            float rad = a.rotation * Mathf.Deg2Rad;
            var axis = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            var d = p - Geo.XZ(a.centre);
            return Mathf.Abs(Vector2.Dot(d, axis)) <= a.size.y * 0.5f + margin &&
                   Mathf.Abs(Vector2.Dot(d, Geo.Right(axis))) <= a.size.x * 0.5f + margin;
        }

        /// <summary>A new maintenance depot with its door front at `front`, doors facing `outward`, using another
        /// depot's vehicle types and prefabs (if given).</summary>
        public MaintenanceDepot AddDepot(Vector2 front, Vector2 outward, MaintenanceDepot assetsFrom = null)
        {
            var go = new GameObject("Depot " + (Depots.Count + 1));
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Geo.X0Z(front);
            go.transform.localRotation = Quaternion.LookRotation(Geo.X0Z(outward.normalized), Vector3.up);
            var d = go.AddComponent<MaintenanceDepot>();
            if (assetsFrom != null)
                d.SetAssets(assetsFrom.ploughType, assetsFrom.mowerType, assetsFrom.buildingType, assetsFrom.ploughPrefab,
                    assetsFrom.mowerPrefab, assetsFrom.buildingPrefab);
            d.snowPloughs = 1;
            d.mowers = 1;
            go.SetActive(true);
            return d;
        }

        public void RemoveDepot(MaintenanceDepot d)
        {
            if (d == null) return;
            d.gameObject.SetActive(false);
            AirfieldPart.DestroyAny(d.gameObject);
        }

        /// <summary>The apron's corners on the ground, going round.</summary>
        public static Vector2[] Corners(ApronArea a)
        {
            float rad = a.rotation * Mathf.Deg2Rad;
            var along = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * (a.size.y * 0.5f);
            var across = Geo.Right(along.normalized) * (a.size.x * 0.5f);
            var c = Geo.XZ(a.centre);
            return new[] { c - along - across, c - along + across, c + along + across, c + along - across };
        }

        public void RemoveApron(int index)
        {
            if (index < 0 || index >= aprons.Count) return;
            aprons.RemoveAt(index);
            BuildGround();
        }

        // ------------------------------------------------------------------ editing

        public Runway AddRunway(Vector3 start, Vector3 end, PavementSurface surface, float width,
            RunwayLighting lighting = RunwayLighting.None)
        {
            var go = new GameObject("Runway");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            var r = go.AddComponent<Runway>();
            r.start = new Vector3(start.x, 0f, start.z);
            r.end = new Vector3(end.x, 0f, end.z);
            r.surface = surface;
            r.width = width;
            r.lighting = surface.IsPaved() ? lighting : RunwayLighting.None;
            go.SetActive(true);
            RunwaysChanged();
            return r;
        }

        public void RemoveRunway(Runway r)
        {
            if (r == null) return;
            var t = GetComponentInChildren<TaxiwayNetwork>();
            if (t != null) t.ForgetRunway(r);
            r.gameObject.SetActive(false);
            AirfieldPart.DestroyAny(r.gameObject);
            RunwaysChanged();
        }

        /// <summary>After runways are added, moved or removed: re-letter parallels, rebuild runways and the
        /// taxiways that meet them.</summary>
        public void RunwaysChanged()
        {
            AssignLetters();
            foreach (var r in Runways)
            {
                r.gameObject.name = "Runway " + r.Designation;
                r.Rebuild();
            }
            var t = GetComponentInChildren<TaxiwayNetwork>();
            if (t != null) t.Rebuild();
        }

        /// <summary>
        /// Parallel runways share a number and are told apart by L / C / R, as seen from their approach. Runways
        /// are grouped by the number of their lower-numbered end; within a group, left to right.
        /// </summary>
        public void AssignLetters()
        {
            var groups = new Dictionary<int, List<(Runway r, bool startIsLow, float offset)>>();
            foreach (var r in Runways)
            {
                int a = RunwayRules.Number(r.Bearing), b = RunwayRules.Number(r.Bearing + 180f);
                bool startIsLow = a <= b;
                var lowDir = startIsLow ? r.Axis2D : -r.Axis2D;
                var mid = (Geo.XZ(r.start) + Geo.XZ(r.end)) * 0.5f;
                float offset = Vector2.Dot(mid, Geo.Right(lowDir));
                int key = Mathf.Min(a, b);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<(Runway, bool, float)>();
                list.Add((r, startIsLow, offset));
            }
            foreach (var list in groups.Values)
            {
                list.Sort((x, y) => x.offset.CompareTo(y.offset));
                for (int i = 0; i < list.Count; i++)
                {
                    string low = list.Count == 1 ? "" : i == 0 ? "L" : i == list.Count - 1 ? "R" : "C";
                    var (r, startIsLow, _) = list[i];
                    r.letter = startIsLow ? low : Runway.MirrorLetter(low);
                }
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The runways this aircraft type can use now, with the check for each.</summary>
        public List<(Runway runway, RunwayCheck check)> RunwaysFor(AircraftType type)
        {
            var list = new List<(Runway, RunwayCheck)>();
            foreach (var r in Runways) list.Add((r, r.Check(type)));
            return list;
        }

        public bool TryPickRunway(Vector3 point, out Runway runway)
        {
            foreach (var r in Runways)
                if (r.Contains(point))
                {
                    runway = r;
                    return true;
                }
            runway = null;
            return false;
        }

        /// <summary>The depot building at a ground point (Airport space, x z).</summary>
        public bool TryPickDepot(Vector2 point, out MaintenanceDepot depot, float margin = 0f)
        {
            foreach (var d in Depots)
                if (d.Contains(point, margin))
                {
                    depot = d;
                    return true;
                }
            depot = null;
            return false;
        }

        /// <summary>The apron at a ground point (Airport space, x z): its index in aprons, the last drawn first.</summary>
        public bool TryPickApron(Vector2 point, out int index)
        {
            for (index = aprons.Count - 1; index >= 0; index--)
                if (OnApron(aprons[index], point)) return true;
            return false;
        }

        // ------------------------------------------------------------------ build

        // ------------------------------------------------------------------ weather

        /// <summary>
        /// Advance game time: while it snows, snow builds up on the ground, runways and taxiways
        /// (RunwayRules.SnowfallRate); otherwise it melts, faster in rain, and grass strips and verges grow (verges
        /// only once the ground is clear of snow). The depots' teams move on.
        /// </summary>
        public void Simulate(float seconds)
        {
            if (seconds <= 0f) return;
            float perHour = condition == SurfaceCondition.Snow ? RunwayRules.SnowfallRate
                : condition == SurfaceCondition.Wet ? -RunwayRules.RainMeltRate : -RunwayRules.MeltRate;
            float rate = perHour / 3600f;
            float grow = condition == SurfaceCondition.Snow ? 0f : RunwayRules.GrassGrowth / 3600f;
            float verges = groundSnow < 0.05f ? grow : 0f;
            groundSnow = Mathf.Max(0f, groundSnow + rate * seconds);
            foreach (var r in Runways) r.Simulate(seconds, rate, grow, verges);
            var t = GetComponentInChildren<TaxiwayNetwork>();
            if (t != null) t.Simulate(seconds, rate, grow, verges);
            Maintenance.Simulate(seconds);
            ApplyGlobals();
        }

        /// <summary>Send the depots' ploughs to every runway, taxiway and road with snow on it.</summary>
        public void PloughAll() => Maintenance.PloughAll();

        /// <summary>Send the depots' mowers to every grass runway and taxiway and every verge that has grown.</summary>
        public void MowAll() => Maintenance.MowAll();

        void OnEnable()
        {
            ApplyGlobals();
            BuildGround();
        }

        void OnDisable()
        {
            groundPart?.Destroy();
            apronPart?.Destroy();
        }

        void OnValidate() => dirty = true;

        void Update()
        {
            if (dirty)
            {
                dirty = false;
                ApplyGlobals();
                BuildGround();
            }
            if (Application.isPlaying) Simulate(Time.deltaTime * timeScale);
        }

        /// <summary>Shader globals: ground snow depth (cm) and how wet surfaces look (rain, falling or melting
        /// snow).</summary>
        void ApplyGlobals()
        {
            float wet = condition == SurfaceCondition.Wet ? 1f : condition == SurfaceCondition.Snow || groundSnow > 0.05f ? 0.45f : 0f;
            Shader.SetGlobalFloat(WetnessId, wet);
            Shader.SetGlobalFloat(SnowId, groundSnow);
        }

        /// <summary>Rebuild the grass and aprons (runways and taxiways rebuild themselves).</summary>
        public void BuildGround()
        {
            if (!isActiveAndEnabled) return;
            groundPart ??= new AirfieldPart(transform, "Ground");
            apronPart ??= new AirfieldPart(transform, "Aprons");

            var ground = new SurfaceBuilder(1);
            if (groundSize > 0f)
            {
                float h = groundSize * 0.5f;
                ground.Rect(0, new Vector2(0f, -h), Vector2.up, 0f, groundSize, -h, h, AirfieldHeights.Ground,
                    AirfieldKind.Ground, 0f, 0f, true, groundSize / 8f);
            }
            groundPart.Set(ground.mesh.Apply(groundPart.Target, "Airport ground"), grass);

            var aprons = new SurfaceBuilder(Surfaces.Count);
            foreach (var a in this.aprons)
            {
                float rad = a.rotation * Mathf.Deg2Rad;
                var axis = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                var origin = Geo.XZ(a.centre) - axis * (a.size.y * 0.5f);
                aprons.Rect((int)a.surface, origin, axis, 0f, a.size.y, -a.size.x * 0.5f, a.size.x * 0.5f,
                    AirfieldHeights.Apron, AirfieldKind.Apron, 0f, a.surface.IsPaved() ? 0f : SurfaceBuilder.Fringe);
            }
            apronPart.Set(aprons.mesh.Apply(apronPart.Target, "Aprons"), dirt, asphalt, concrete, grassStrip);
            LayoutChanged();
        }
    }
}
