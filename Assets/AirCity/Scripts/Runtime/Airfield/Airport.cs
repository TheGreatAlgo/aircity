using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using AirCity.Traffic;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// A free-placed apron: open pavement of any shape (a simple polygon). Stands draw over it, and service roads run
    /// under paved ones, so across them a road is only its paint. Aprons of one surface may overlap and merge
    /// (their texture is world-mapped); asphalt and concrete can't overlap each other, nor can dirt and grass
    /// (Airport.ApronProblem), and paved aprons cover unpaved ones.
    /// </summary>
    [Serializable]
    public class ApronArea
    {
        [Tooltip("Corners, counter-clockwise from above (Airport space, x z).")]
        public List<Vector2> outline = new List<Vector2>();
        public PavementSurface surface = PavementSurface.Concrete;
        // the rectangle scenes saved before aprons had outlines (Migrate turns it into one)
        [SerializeField, HideInInspector] Vector3 centre;
        [SerializeField, HideInInspector] Vector2 size;
        [SerializeField, HideInInspector] float rotation;

        /// <summary>An apron with these corners, either way round (stored counter-clockwise).</summary>
        public static ApronArea Polygon(IEnumerable<Vector2> corners, PavementSurface surface)
        {
            var a = new ApronArea { outline = new List<Vector2>(corners), surface = surface };
            if (Geo.SignedArea(a.outline) < 0f) a.outline.Reverse();
            return a;
        }

        /// <summary>A rectangle: size.x across its axis, size.y along it, the axis `rotation` degrees clockwise from
        /// north.</summary>
        public static ApronArea Rect(Vector2 centre, Vector2 size, float rotation, PavementSurface surface) =>
            Polygon(RectCorners(centre, size, rotation), surface);

        static Vector2[] RectCorners(Vector2 c, Vector2 size, float rotation)
        {
            float rad = rotation * Mathf.Deg2Rad;
            var axis = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            var along = axis * (size.y * 0.5f);
            var across = Geo.Right(axis) * (size.x * 0.5f);
            return new[] { c - along - across, c - along + across, c + along + across, c + along - across };
        }

        public float Area => Mathf.Abs(Geo.SignedArea(outline));

        /// <summary>Is a ground point (Airport space, x z) on the apron, or within margin of it?</summary>
        public bool Contains(Vector2 p, float margin = 0f) =>
            outline.Count >= 3 && (Geo.Inside(outline, p) || (margin > 0f && Geo.DistanceToOutline(outline, p) <= margin));

        /// <summary>Can a vehicle drive straight from a to b on the apron (within margin of it all the way)?</summary>
        public bool Crossable(Vector2 a, Vector2 b, float margin)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 4f));
            for (int i = 0; i <= n; i++)
                if (!Contains(Vector2.Lerp(a, b, i / (float)n), margin)) return false;
            return true;
        }

        /// <summary>Give an apron saved as a rectangle its outline.</summary>
        internal void Migrate()
        {
            if (outline.Count > 0 || size.x <= 0f || size.y <= 0f) return;
            outline.AddRange(RectCorners(Geo.XZ(centre), size, rotation));
            size = Vector2.zero;
        }
    }

    /// <summary>
    /// The root of an airport: its runways (child Runway objects), taxiways and service roads (a child
    /// TaxiwayNetwork), aprons, the grass they sit on, its terminal buildings, its maintenance depots (child
    /// MaintenanceDepots, with the work for them in Maintenance) and the surface weather. Holds the shared airfield
    /// materials. Stands' jet bridges work through Bridges and are shown by a BridgeRig on the same object.
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
        [Tooltip("Terminal buildings (the Building shader).")]
        public Material building;

        [Header("Layout")]
        [Tooltip("Side of the square of grass under the airport (metres); 0 = none (terrain provides the ground).")]
        public float groundSize = 6000f;
        public List<ApronArea> aprons = new List<ApronArea>();
        public List<Terminal> terminals = new List<Terminal>();
        [Tooltip("The aircraft types that come here: jet bridges are checked and parked against the ones each stand takes.")]
        public List<AircraftType> aircraftTypes = new List<AircraftType>();

        [Header("Weather")]
        [SerializeField] SurfaceCondition condition;
        [Tooltip("Snow lying on the grass and other unploughed ground (cm).")]
        [Min(0f)] public float groundSnow;
        [Tooltip("Game seconds per real second while playing (until the game clock exists): weather, grass growth and " +
                 "the depot's vehicles all run on it, so a higher value fast-forwards everything together.")]
        public float timeScale = 1f;
        [Tooltip("Where the wind blows from (degrees clockwise from north). Aircraft take off and land into it, so it " +
                 "picks which way each runway is used (AirTraffic.UsedFromStart).")]
        [Range(0f, 360f)] public float windFrom = 270f;

        readonly List<Runway> runways = new List<Runway>();
        AirfieldPart groundPart, apronPart, terminalPart;
        bool dirty;
        AirportMaintenance maintenance;
        AirTraffic traffic;
        AirportBridges bridges;
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

        /// <summary>The flights: departures, arrivals and the tower that keeps them apart.</summary>
        public AirTraffic Traffic => traffic ??= new AirTraffic(this);

        /// <summary>The stands' jet bridges at work: docking with parked aircraft, pulling back for push-back.</summary>
        public AirportBridges Bridges => bridges ??= new AirportBridges(this);

        /// <summary>Bumped whenever runways, taxiways, stands, aprons or terminals are rebuilt.</summary>
        public int LayoutVersion => layoutVersion;

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

        /// <summary>The generated apron mesh (for tests and tools; rebuilt on change).</summary>
        public Mesh ApronMesh => apronPart?.Mesh;

        /// <summary>The generated terminal mesh (for tests and tools; rebuilt on change).</summary>
        public Mesh TerminalMesh => terminalPart?.Mesh;

        /// <summary>Runways, taxiways or aprons were rebuilt.</summary>
        internal void LayoutChanged() => layoutVersion++;

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
            {
                d.SetAssets(assetsFrom.ploughType, assetsFrom.mowerType, assetsFrom.buildingType, assetsFrom.ploughPrefab,
                    assetsFrom.mowerPrefab, assetsFrom.buildingPrefab);
                d.SetTugAssets(assetsFrom.smallTugType, assetsFrom.largeTugType, assetsFrom.smallTugPrefab, assetsFrom.largeTugPrefab);
            }
            d.snowPloughs = 1;
            d.mowers = 1;
            d.smallTugs = 1;
            d.largeTugs = 0;
            go.SetActive(true);
            return d;
        }

        public void RemoveDepot(MaintenanceDepot d)
        {
            if (d == null) return;
            d.gameObject.SetActive(false);
            AirfieldPart.DestroyAny(d.gameObject);
        }

        /// <summary>The smallest apron worth building (square metres).</summary>
        public const float MinApronArea = 100f;

        /// <summary>Why an apron with this outline and surface can't be built (or `except`, an existing one, changed
        /// to it), or null if it can.</summary>
        public string ApronProblem(IList<Vector2> outline, PavementSurface surface, ApronArea except = null)
        {
            if (outline.Count < 3) return "needs three corners";
            if (Geo.SelfIntersects(outline)) return "its edges cross";
            if (Mathf.Abs(Geo.SignedArea(outline)) < MinApronArea) return "too small";
            foreach (var a in aprons)
            {
                if (a == except || a.surface == surface || a.surface.IsPaved() != surface.IsPaved()) continue;
                if (Geo.Overlap(outline, a.outline)) return $"overlaps a {a.surface.ToString().ToLowerInvariant()} apron";
            }
            return null;
        }

        /// <summary>A new apron with these corners (either way round), or null if it can't be built (ApronProblem).</summary>
        public ApronArea AddApron(IEnumerable<Vector2> corners, PavementSurface surface)
        {
            var a = ApronArea.Polygon(corners, surface);
            if (ApronProblem(a.outline, surface) != null) return null;
            aprons.Add(a);
            BuildGround();
            return a;
        }

        /// <summary>Change an apron's surface; false (and unchanged) if it would then overlap an apron it mustn't.</summary>
        public bool SetApronSurface(ApronArea a, PavementSurface surface)
        {
            if (a == null || ApronProblem(a.outline, surface, a) != null) return false;
            a.surface = surface;
            BuildGround();
            return true;
        }

        public void RemoveApron(int index)
        {
            if (index < 0 || index >= aprons.Count) return;
            aprons.RemoveAt(index);
            BuildGround();
        }

        // ------------------------------------------------------------------ terminals

        /// <summary>The smallest terminal worth building (square metres).</summary>
        public const float MinTerminalArea = 200f;

        /// <summary>Why a terminal with this shape can't be built (or `except`, an existing one, changed to it), or null if
        /// it can: its outline mustn't cross itself, nor another terminal, a runway, a taxiway or a stand's pad.</summary>
        public string TerminalProblem(Terminal t, Terminal except = null)
        {
            if (t.corners.Count < 3) return "needs three corners";
            var o = t.Outline();
            if (Geo.SelfIntersects(o)) return "its walls cross";
            if (Mathf.Abs(Geo.SignedArea(o)) < MinTerminalArea) return "too small";
            foreach (var other in terminals)
                if (other != except && other != null && Geo.Overlap(o, other.Outline())) return "overlaps another terminal";
            foreach (var r in Runways)
            {
                var a = Geo.XZ(r.start);
                var b = Geo.XZ(r.end);
                var right = Geo.Right(r.Axis2D) * r.HalfWidthTotal;
                if (Geo.Overlap(o, new[] { a + right, b + right, b - right, a - right })) return "on " + r.name;
            }
            var net = GetComponentInChildren<TaxiwayNetwork>();
            if (net != null)
            {
                foreach (var e in net.edges)
                {
                    var a = net.At(e.a);
                    var b = net.At(e.b);
                    if ((b - a).sqrMagnitude < 1e-4f) continue;
                    var right = Geo.Right((b - a).normalized) * (e.width * 0.5f);
                    if (Geo.Overlap(o, new[] { a + right, b + right, b - right, a - right }))
                        return e.IsRoad ? "across a road" : "across a taxiway";
                }
                foreach (var l in net.Layouts())
                    if (Geo.Overlap(o, l.Pad)) return "on " + l.stand.Name;
            }
            return null;
        }

        /// <summary>A new terminal (corners either way round), or null if it can't be built (TerminalProblem).</summary>
        public Terminal AddTerminal(IEnumerable<Vector2> corners, int floors = 2)
        {
            var t = Terminal.Polygon(corners, floors);
            if (TerminalProblem(t) != null) return null;
            terminals.Add(t);
            BuildGround();
            return t;
        }

        /// <summary>Change a terminal (corners, bulges, floors) to a changed copy; false (and unchanged) if that won't do.</summary>
        public bool ChangeTerminal(Terminal t, Terminal changed)
        {
            if (t == null || changed == null || TerminalProblem(changed, t) != null) return false;
            t.corners = new List<Vector2>(changed.corners);
            t.bulge = new List<float>(changed.bulge);
            t.floors = changed.floors;
            BuildGround();
            return true;
        }

        public void RemoveTerminal(Terminal t)
        {
            if (!terminals.Remove(t)) return;
            BuildGround();
        }

        /// <summary>The terminal at a ground point (Airport space, x z), the last built first.</summary>
        public bool TryPickTerminal(Vector2 point, out Terminal terminal)
        {
            for (int i = terminals.Count - 1; i >= 0; i--)
                if (terminals[i] != null && terminals[i].Contains(point))
                {
                    terminal = terminals[i];
                    return true;
                }
            terminal = null;
            return false;
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
                if (aprons[index].Contains(point)) return true;
            return false;
        }

        // ------------------------------------------------------------------ build

        // ------------------------------------------------------------------ weather

        /// <summary>
        /// Advance game time: while it snows, snow builds up on the ground, runways and taxiways
        /// (RunwayRules.SnowfallRate); otherwise it melts, faster in rain, and grass strips and verges grow (verges
        /// only once the ground is clear of snow). The depots' teams and the flights move on.
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
            Traffic.Simulate(seconds);
            Bridges.Simulate(seconds);
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
            terminalPart?.Destroy();
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

        /// <summary>Rebuild the grass, aprons and terminals (runways and taxiways rebuild themselves). Roads need no
        /// rebuild: they lie under paved aprons whatever the aprons do.</summary>
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
                a.Migrate();
                aprons.Polygon((int)a.surface, a.outline, AirfieldHeights.ApronOf(a.surface), AirfieldKind.Apron,
                    a.surface.IsPaved() ? 0f : SurfaceBuilder.Fringe);
            }
            apronPart.Set(aprons.mesh.Apply(apronPart.Target, "Aprons"), dirt, asphalt, concrete, grassStrip);

            terminalPart ??= new AirfieldPart(transform, "Terminals");
            var terminalMesh = new TerminalBuilder();
            foreach (var t in terminals)
                if (t != null) terminalMesh.Build(t);
            terminalPart.Set(terminalMesh.mesh.Apply(terminalPart.Target, "Terminals", false, 1), building);
            terminalPart.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            LayoutChanged();
        }
    }
}
