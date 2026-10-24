using System;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// A runway: two thresholds, a surface, a width, a lighting upgrade, wear, lying snow and (grass strips) grass
    /// length, and the grass of its verges (the graded runway strip either side, mown too). Its mesh, paint and
    /// lights are generated from these (RunwayBuilder) whenever it is enabled or reshaped, so any length works;
    /// wear, snow, grass and the ploughs / mowers at work only update vertex data.
    ///
    ///   var rwy = airport.AddRunway(a, b, PavementSurface.Concrete, 45f);
    ///   rwy.Check(type).Ok                    // can this aircraft use it now (length, surface, snow, night)?
    ///   rwy.SetLighting(RunwayLighting.Basic);
    ///   rwy.AddMovement(type);                // wear from a takeoff or landing
    ///   rwy.StartPloughing();                 // the depot sends its ploughs; closed while they work
    ///
    /// Positions are in the Airport's space; the start threshold is the one aircraft cross first when landing
    /// towards the end (its designator comes from that heading).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class Runway : MonoBehaviour
    {
        public Vector3 start;
        public Vector3 end = new Vector3(0f, 0f, 1000f);
        public PavementSurface surface = PavementSurface.Asphalt;
        [Tooltip("Paved width (metres), without shoulders. See RunwayRules.RunwayWidths.")]
        public float width = 30f;
        public RunwayLighting lighting = RunwayLighting.None;
        [Range(0f, 1f)] public float wear;
        [Tooltip("Lying snow (cm). Builds up while it snows (Airport.Simulate); ploughing clears it.")]
        [Min(0f)] public float snow;
        [Tooltip("Grass length (cm), grass strips only. Grows over time; mowing cuts it to RunwayRules.CutGrass.")]
        [Min(0f)] public float grass = RunwayRules.CutGrass;
        [Tooltip("Grass length (cm) on the verges either side (the graded runway strip). Grows; the mowers cut it.")]
        [Min(0f)] public float vergeGrass = RunwayRules.CutGrass;
        [Tooltip("Parallel-runway letter of the start end (L, C, R or empty); the Airport assigns it.")]
        public string letter = "";

        AirfieldPart surfacePart, paintPart, glowPart, fixturePart, vergePart;
        readonly SurfaceState state = new SurfaceState(), vergeState = new SurfaceState();
        [NonSerialized] StripWork work, vergeWork;
        Vector4 stripes = StripWork.Idle, vergeStripes = StripWork.Idle;
        bool dirty;

        public Airport Airport => GetComponentInParent<Airport>();
        public float Length => Vector2.Distance(Geo.XZ(start), Geo.XZ(end));
        public Vector3 Axis => Geo.X0Z(Axis2D);
        internal Vector2 Axis2D
        {
            get
            {
                var d = Geo.XZ(end) - Geo.XZ(start);
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
            }
        }

        public float ShoulderWidth => RunwayRules.ShoulderWidth(surface, width);
        /// <summary>Half the paved width including shoulders.</summary>
        public float HalfWidthTotal => width * 0.5f + ShoulderWidth;
        public bool Paved => surface.IsPaved();

        /// <summary>Ploughs and mowers work this far beyond the thresholds (paved runways: over the blast pads).</summary>
        public float WorkMargin => Paved ? RunwayBuilder.BlastPad + 5f : 10f;

        /// <summary>The verges reach this far from the centreline, and this far beyond each end.</summary>
        public float VergeHalfWidth => Mathf.Max(RunwayRules.RunwayVerge(surface, width), HalfWidthTotal + 1f);
        public float VergeOverrun => RunwayRules.RunwayVergeOverrun(surface);

        /// <summary>Stacking level where runways cross (0..2, by order in the hierarchy): the later one draws on
        /// top instead of flickering.</summary>
        public int Layer => transform.GetSiblingIndex() % 3;
        public float SurfaceHeight => AirfieldHeights.Runway + Layer * AirfieldHeights.RunwayLayerStep;

        /// <summary>Landing bearing at the start threshold, degrees from north (+Z).</summary>
        public float Bearing => RunwayRules.Bearing(Axis);
        public string StartDesignator => RunwayRules.Designator(RunwayRules.Number(Bearing)) + letter;
        public string EndDesignator => RunwayRules.Designator(RunwayRules.Number(Bearing + 180f)) + MirrorLetter(letter);
        /// <summary>"09L/27R".</summary>
        public string Designation => $"{StartDesignator}/{EndDesignator}";

        public static string MirrorLetter(string l) => l == "L" ? "R" : l == "R" ? "L" : l;

        // ------------------------------------------------------------------ gameplay

        /// <summary>Can this aircraft type use the runway now (the Airport's weather and time of day)?</summary>
        public RunwayCheck Check(AircraftType type)
        {
            var airport = Airport;
            var condition = airport != null ? airport.Condition : SurfaceCondition.Dry;
            return RunwayRules.Check(type, surface, Length, width, lighting, wear, condition, Airport.IsNight, snow, IsPloughing,
                grass, IsMowing);
        }

        /// <summary>Wear from one takeoff or landing.</summary>
        public void AddMovement(AircraftType type)
        {
            var airport = Airport;
            var condition = airport != null ? airport.Condition : SurfaceCondition.Dry;
            SetWear(wear + RunwayRules.WearPerMovement(surface, type.maxTakeoffMass, condition));
        }

        public void SetWear(float value)
        {
            wear = Mathf.Clamp01(value);
            RefreshState();
        }

        public void Repair() => SetWear(0f);

        public void SetSnow(float cm)
        {
            snow = Mathf.Max(cm, 0f);
            RefreshState();
        }

        public void SetGrass(float cm)
        {
            grass = Mathf.Max(cm, 0f);
            RefreshState();
        }

        public void SetVergeGrass(float cm)
        {
            vergeGrass = Mathf.Max(cm, 0f);
            RefreshState();
        }

        /// <summary>The ploughs or mowers assigned to this runway (null when none), whether or not they've arrived.</summary>
        public StripWork Work => work;
        /// <summary>The mowers assigned to its verges (null when none). Verges don't close the runway.</summary>
        public StripWork VergeWork => vergeWork;

        /// <summary>Closed while the ploughs are on it.</summary>
        public bool IsPloughing => work != null && work.kind == WorkKind.Plough && work.Closing;
        /// <summary>Closed while the mowers are on it.</summary>
        public bool IsMowing => work != null && work.kind == WorkKind.Mow && work.Closing;

        /// <summary>How far the ploughs or mowers have got, 0..1 (0 when none are out).</summary>
        public float WorkProgress => work != null ? work.Progress : 0f;

        /// <summary>Ask the airport's depot to plough this runway (false: no depot, or nothing to plough).</summary>
        public bool StartPloughing()
        {
            var depot = Airport != null ? Airport.Depot : null;
            return depot != null && depot.Plough(this);
        }

        /// <summary>Ask the airport's depot to mow this grass strip.</summary>
        public bool StartMowing()
        {
            var depot = Airport != null ? Airport.Depot : null;
            return depot != null && depot.Mow(this);
        }

        /// <summary>A team takes the job (VehicleTeam): the runway (or its verges) shows its progress, and the
        /// runway closes once the team is on it.</summary>
        internal void Assign(StripWork w, bool verge = false)
        {
            if (verge) vergeWork = w;
            else work = w;
            RefreshState();
        }

        /// <summary>The team has finished (or given up, done = false).</summary>
        internal void Release(StripWork w, bool done, bool verge = false)
        {
            if (verge)
            {
                if (vergeWork != w) return;
                if (done)
                {
                    vergeGrass = RunwayRules.CutGrass;
                    vergeStripes = w.Stripes;
                }
                vergeWork = null;
                RefreshState();
                return;
            }
            if (work != w) return;
            if (done && w.kind == WorkKind.Plough) snow = 0f;
            if (done && w.kind == WorkKind.Mow)
            {
                grass = RunwayRules.CutGrass;
                stripes = w.Stripes;
            }
            work = null;
            RefreshState();
        }

        /// <summary>Advance time (game seconds): snow builds up or melts at snowRate (cm per second, negative =
        /// melting), grass strips grow at grassRate and the verges at vergeRate (cm per second).</summary>
        public void Simulate(float seconds, float snowRate, float grassRate = 0f, float vergeRate = 0f)
        {
            snow = Mathf.Max(0f, snow + snowRate * seconds);
            if (surface == PavementSurface.Grass && snow < 0.01f) grass += grassRate * seconds;
            vergeGrass += vergeRate * seconds;
            RefreshState();
        }

        /// <summary>Wear, snow, grass and the work on them, as the shaders read it.</summary>
        internal SurfaceValues State => new SurfaceValues
        {
            wear = wear, snow = snow, grass = grass,
            work = work != null ? work.ShaderState : stripes,
        };

        internal SurfaceValues VergeState => new SurfaceValues
        {
            grass = vergeGrass,
            work = vergeWork != null ? vergeWork.ShaderState : vergeStripes,
        };

        /// <summary>Push wear, snow, grass and the work in progress to the meshes (no rebuild).</summary>
        public void RefreshState()
        {
            state.Apply(_ => State);
            vergeState.Apply(_ => VergeState);
        }

        /// <summary>Distance along the centreline from the start threshold (s) and to its right (x) of a point.</summary>
        public void Locate(Vector3 point, out float s, out float x)
        {
            var d = Geo.XZ(point) - Geo.XZ(start);
            var axis = Axis2D;
            s = Vector2.Dot(d, axis);
            x = Vector2.Dot(d, Geo.Right(axis));
        }

        /// <summary>Is a ground point on the runway or its shoulders?</summary>
        public bool Contains(Vector3 point, float margin = 0f)
        {
            Locate(point, out float s, out float x);
            return s >= -margin && s <= Length + margin && Mathf.Abs(x) <= HalfWidthTotal + margin;
        }

        /// <summary>The point on the centreline closest to p (clamped to the runway).</summary>
        public Vector3 ClosestOnCentreline(Vector3 p)
        {
            Locate(p, out float s, out _);
            return Vector3.Lerp(start, end, Mathf.Clamp01(s / Mathf.Max(Length, 1e-3f)));
        }

        /// <summary>Change the lighting upgrade (dirt and grass runways can't be lit).</summary>
        public void SetLighting(RunwayLighting value)
        {
            lighting = Paved ? value : RunwayLighting.None;
            Rebuild();
        }

        // ------------------------------------------------------------------ build

        void OnEnable() => Rebuild();

        void OnDisable()
        {
            surfacePart?.Destroy();
            paintPart?.Destroy();
            glowPart?.Destroy();
            fixturePart?.Destroy();
            vergePart?.Destroy();
        }

        void OnValidate()
        {
            width = Mathf.Max(width, 5f);
            if (!Paved) lighting = RunwayLighting.None;
            dirty = true;
        }

        void Update()
        {
            if (!dirty) return;
            dirty = false;
            var airport = Airport;
            if (airport != null) airport.RunwaysChanged();
            else Rebuild();
        }

        /// <summary>Regenerate the meshes (the Airport also rebuilds taxiways when a runway moves).</summary>
        public void Rebuild()
        {
            var airport = Airport;
            if (airport == null || !isActiveAndEnabled) return;
            surfacePart ??= new AirfieldPart(transform, "Surface");
            paintPart ??= new AirfieldPart(transform, "Paint");
            glowPart ??= new AirfieldPart(transform, "LightGlows");
            fixturePart ??= new AirfieldPart(transform, "Fixtures");
            vergePart ??= new AirfieldPart(transform, "Verges");

            var surfaceMesh = new SurfaceBuilder(2);
            var paint = new PaintBuilder();
            var lights = new LightBuilder();
            RunwayBuilder.Build(this, surfaceMesh, paint, lights);
            surfacePart.Set(surfaceMesh.mesh.Apply(surfacePart.Target, name + " surface"),
                airport.SurfaceMaterial(surface), airport.asphalt);
            paintPart.Set(paint.Apply(paintPart.Target, name + " paint"), airport.paint);
            glowPart.Set(lights.ApplyGlows(glowPart.Target, name + " light glows"), airport.lightGlow);
            fixturePart.Set(lights.ApplyFixtures(fixturePart.Target, name + " fixtures"), airport.fixture);
            state.Capture(surfacePart.Mesh, surfaceMesh.mesh, paintPart.Mesh, paint.mesh);

            var verges = new SurfaceBuilder(1) { state = VergeState };
            verges.mesh.owner = 0;
            float over = VergeOverrun;
            verges.Verges(0, Geo.XZ(start), Axis2D, -over, Length + over, HalfWidthTotal, VergeHalfWidth, AirfieldHeights.RunwayVerge);
            vergePart.Set(verges.mesh.Apply(vergePart.Target, name + " verges"), airport.grassStrip);
            vergeState.Capture(vergePart.Mesh, verges.mesh, null, null);
            RefreshState();
            airport.LayoutChanged();
            LightCount = lights.LightCount;
        }

        /// <summary>Number of light glows built (for tests and budgets).</summary>
        public int LightCount { get; private set; }

        /// <summary>The generated meshes (for tests and tools; rebuilt on change).</summary>
        public Mesh SurfaceMesh => surfacePart?.Mesh;
        public Mesh PaintMesh => paintPart?.Mesh;
        public Mesh GlowMesh => glowPart?.Mesh;
        public Mesh VergeMesh => vergePart?.Mesh;
    }
}
