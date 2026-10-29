using System.Collections.Generic;
using System.Linq;
using System.Text;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>The sandbox's airfield tools (DevToolbar has a button for each).</summary>
    public enum DevTool { Select, Runway, Taxiway, Road, Apron, Depot, Stand, DeIcing, Delete }

    /// <summary>
    /// Sandbox airfield building (development only, not game UI). DevToolbar has buttons for all of it; the keys:
    ///   B = next build tool (select / runway / taxiway / road / apron / depot / stand / de-icing bay)   X = delete tool
    ///   Esc = end the line being laid, else back to select, else deselect
    ///   select: click a runway, taxiway or road section, depot or apron to select it (outlined in amber; the panel
    ///     lists which aircraft can use a selected runway); Delete / Backspace removes it (DevToolbar)
    ///   delete: click anything to remove it (outlined in red first); Shift+click takes a taxiway's or road's whole
    ///     run; aircraft too (DevSandboxControls)
    ///   Placing is free; a point sticks to something already built only within a few pixels of it (a ring shows
    ///     where it will go and the tool strip says what it joins). Hold Shift for steps: lines turn in 10 deg steps
    ///     from the nearest taxiway, road, runway or apron edge (runways: from north) and run in whole 5 m (runways:
    ///     50 m)
    ///   runway: left-drag from threshold to threshold
    ///   taxiway / road: click points to lay a taxiway or a service road; right-click or Esc ends the line. A
    ///     taxiway sticks to taxiways and runways, and joins a road only when clicked on its pavement; a road sticks
    ///     to roads, the front of a depot and the head of a stand, and joins a taxiway or runway only when clicked on
    ///     it. Across a paved apron a road is only its paint; it may touch a stand (that's the stand's way in for
    ///     vehicles) but never cross one
    ///   apron: click corners (they stick to aprons' and stands' corners and edges), then click the first corner or
    ///     right-click to finish (Esc drops it); or drag out a rectangle, lined up with what it starts on (else the
    ///     nearest runway)
    ///   depot: drag from where the doors go out the way they face (snaps to 10 deg) to build a depot; click one to
    ///     select it
    ///   stand: click to add a parking stand. Beside a taxiway (or another stand's front line, the taxilane to come)
    ///     it opens onto it, its centreline under the cursor, pulled against the stands beside it; anywhere else it
    ///     stands free, centred on the cursor, and is connected later by laying a taxiway along its front line (shown
    ///     dashed). Red where it won't fit. Click one to select it
    ///   de-icing bay: click on a taxiway (or anywhere, to connect later) to add a drive-through bay there
    ///   V = surface   [ / ] = width (stands: size code)   L = lighting (selected runway; all taxiways with the
    ///     taxiway tool)   Z = stand angle (90 / 60 / 45 / 30, keeping its lean), Shift+Z = lean the other way
    ///     (bays: direction)
    ///   Shift+scroll = turn the stand being placed (or the selected one) 5 deg a notch, with Alt 1 deg: on a
    ///     taxiway its angle to it, 25 to 155 deg (through square on to leaning the other way); a free stand all the
    ///     way round
    ///   I = push-back / taxi-out stand
    ///   C = weather (dry / rain / snowing; snow builds up over game time)   Y = time speed (1x .. 60x)
    ///   O = a 2 cm snowfall at once   U = grass strips and verges grow 6 cm at once
    ///   P = plough the selection (taxiway / road tool: all taxiways; nothing selected: everything)
    ///   M = mow the selection, Shift+M its verges (nothing selected: all grass and verges)
    ///   , / . = fewer / more snow ploughs in the selected depot, with Shift: mowers (ploughs work in echelons of
    ///     up to four)
    ///   K / J = wear / repair the selected runway
    ///   (DevSandboxControls has N, T, G, R, 1-4 and + / - for the aircraft.)
    /// </summary>
    public class DevAirfieldTool : MonoBehaviour
    {
        public Airport airport;
        [Tooltip("For the first depot built when the airport has none (else the new one copies an existing depot's).")]
        public VehicleType ploughType, mowerType, depotType, smallTugType, largeTugType;
        public GameObject ploughPrefab, mowerPrefab, depotPrefab, smallTugPrefab, largeTugPrefab;

        /// <summary>True while a tool other than select is on: other sandbox tools leave the left mouse button alone.</summary>
        public static bool Active { get; private set; }
        /// <summary>The delete tool is on: a click on an aircraft removes it (DevSandboxControls).</summary>
        public static bool Deleting { get; private set; }

        public static readonly float[] TimeScales = { 1f, 5f, 20f, 60f };

        enum Kind { None, Runway, Edge, Depot, Apron, Stand }

        /// <summary>Something on the airfield to select or delete.</summary>
        struct Pick
        {
            public Kind kind;
            public Runway runway;
            public TaxiwayEdge edge;
            public MaintenanceDepot depot;
            public int apron;
            public Stand stand;

            public static Pick None => new Pick { apron = -1 };
            public bool Same(in Pick o) => kind == o.kind && runway == o.runway && edge == o.edge && depot == o.depot &&
                                           apron == o.apron && stand == o.stand;
        }

        DevTool tool;
        PavementSurface surface = PavementSurface.Asphalt;
        int runwayWidth = 2, taxiwayWidth = 2;
        Pick selection = Pick.None, hover = Pick.None;
        bool hoverRun;
        Runway dragging;
        MaintenanceDepot fleetDepot;
        Vector3 dragStart, lastGround;
        bool placingDepot;
        int chainNode = -1;
        SizeCode standCode = SizeCode.C;
        /// <summary>New parking stands' angle to their front line, 25..155 degrees (over 90: leaning the other way),
        /// and free stands' and bays' heading.</summary>
        float standAngle = 90f, freeHeading, scrollTurn;
        bool bayFlip, ghostAttached;
        string ghostNote;
        StandExit standExit;
        bool standNoSmall;
        StandLayout ghost;
        string ghostProblem;
        readonly List<Vector2> apronCorners = new List<Vector2>();
        Vector2 apronCursor, apronDragStart, pressScreen;
        bool apronDrag, apronPress;
        Vector2[] apronRect;
        string apronProblem;
        string roadProblem;
        /// <summary>Where the taxiway / road tool's next point goes, and what it would join (null: a new end).</summary>
        LineSnap lineSnap;
        bool lineSnapShown;
        /// <summary>What the apron tool's corner sticks to, or null.</summary>
        string apronSnap;
        Camera cam;
        AircraftType[] types = new AircraftType[0];
        readonly DevOutlines outlines = new DevOutlines();
        readonly List<Vector3> loop = new List<Vector3>();
        GUIContent panel = GUIContent.none;

        // ------------------------------------------------------------------ for the toolbar

        public DevTool Tool => tool;
        /// <summary>The surface new pavement gets.</summary>
        public PavementSurface Surface => surface;
        /// <summary>The width a new runway or taxiway gets (roads are always RunwayRules.RoadWidth).</summary>
        public float NewWidth => tool == DevTool.Taxiway ? RunwayRules.TaxiwayWidths[taxiwayWidth]
            : tool == DevTool.Road ? RunwayRules.RoadWidth : RunwayRules.RunwayWidths[runwayWidth];

        public bool HasSelection => selection.kind != Kind.None;
        public Runway SelectedRunway => selection.kind == Kind.Runway ? selection.runway : null;
        public TaxiwayEdge SelectedEdge => selection.kind == Kind.Edge ? selection.edge : null;
        public MaintenanceDepot SelectedDepot => selection.kind == Kind.Depot ? selection.depot : null;
        public bool ApronSelected => selection.kind == Kind.Apron;
        public Stand SelectedStand => selection.kind == Kind.Stand ? selection.stand : null;
        public ApronArea SelectedApron => selection.kind == Kind.Apron ? airport.aprons[selection.apron] : null;
        /// <summary>Why the apron being drawn (or the selected one's new surface) won't do, or null.</summary>
        public string ApronProblemText => apronProblem;
        /// <summary>Why the road or taxiway can't go to the cursor (it would cross a stand), or null.</summary>
        public string RoadProblem => tool == DevTool.Road || tool == DevTool.Taxiway ? roadProblem : null;
        /// <summary>Corners of the apron being drawn so far.</summary>
        public int ApronCornerCount => apronCorners.Count;

        /// <summary>The usual angles a parking stand meets its taxiway at (degrees, leaning either way); any angle from
        /// MinStandAngle to MaxStandAngle can be had by turning it (Shift+scroll, the stepper).</summary>
        public static readonly float[] StandAngles = { 90f, 60f, 45f, 30f };
        /// <summary>The angles a parking stand can meet its front line at, measured from the front line's direction:
        /// 25 degrees (StandLayout.MinSin, rounded up) either side of the line.</summary>
        public const float MinStandAngle = 25f, MaxStandAngle = 155f;
        /// <summary>The size code, angle and exit new stands get (or the selected stand has).</summary>
        public SizeCode StandCode => SelectedStand != null ? SelectedStand.code : standCode;

        /// <summary>The selected parking stand's angle to its front line, else new stands' (degrees, 25..155: over 90
        /// it leans the other way).</summary>
        public float StandAngle
        {
            get
            {
                var st = SelectedStand;
                var l = st != null && !st.DeIcing ? airport.Taxiways.Layout(st) : null;
                return l == null ? standAngle : Vector2.Angle(l.along, l.axis);
            }
        }

        /// <summary>Is the stand angle leaning the other way (over 90)?</summary>
        public bool StandLeans => StandAngle > 90.5f;

        /// <summary>Where the stand under the cursor would go (on a taxiway, on a stand's taxilane, free), in words.</summary>
        public string GhostNote => ghost != null ? ghostNote : null;

        /// <summary>The tool turns stands with Shift+scroll just now: the camera mustn't zoom on it (DevOrbitCamera).</summary>
        public static bool TakesScroll { get; private set; }

        /// <summary>What the next point of the taxiway, road or apron tool would join, in words, or null.</summary>
        public string SnapText => tool == DevTool.Apron ? apronSnap
            : (tool == DevTool.Taxiway || tool == DevTool.Road) && lineSnapShown ? lineSnap.what : null;
        public StandExit StandExit => SelectedStand != null ? SelectedStand.exit : standExit;
        /// <summary>New parking stands, or the selected one, turn small aircraft away.</summary>
        public bool StandNoSmall => SelectedStand != null ? SelectedStand.noSmallAircraft : standNoSmall;
        /// <summary>Why the stand or bay under the cursor can't be built, or null.</summary>
        public string GhostProblem => ghostProblem;
        /// <summary>The frame the selection was made (DevToolbar deletes whichever of it and the aircraft came last).</summary>
        public int SelectedFrame { get; private set; }
        /// <summary>The selection, in words ("Runway 09/27"), or null.</summary>
        public string SelectionName => Describe(selection);
        /// <summary>What a click with the delete tool would remove, in words, or null.</summary>
        public string HoverName => hoverRun ? Describe(hover) + " and its whole run" : Describe(hover);
        /// <summary>The depot the fleet keys work on: the last one selected, else the first.</summary>
        public MaintenanceDepot FleetDepot => fleetDepot != null ? fleetDepot : airport != null ? airport.Depot : null;

        public bool TaxiwaysLit => airport.Taxiways.edges.Any(e => e.lit);

        /// <summary>Do the depots send their ploughs and mowers by themselves?</summary>
        public bool AutoDispatch
        {
            get => airport.Depots.Any(d => d.autoDispatch);
            set
            {
                foreach (var d in airport.Depots) d.autoDispatch = value;
            }
        }

        public static string WeatherName(SurfaceCondition c) => c switch
        {
            SurfaceCondition.Wet => "Rain",
            SurfaceCondition.Snow => "Snow",
            _ => "Dry",
        };

        public void SetTool(DevTool t)
        {
            FinishDrag();
            if (t != tool) selection = Pick.None;
            tool = t;
            chainNode = -1;
            placingDepot = false;
            ghost = null;
            apronCorners.Clear();
            apronDrag = apronPress = false;
            apronRect = null;
            apronProblem = null;
            apronSnap = null;
            lineSnapShown = false;
        }

        /// <summary>The surface for new pavement; with a runway selected (select or runway tool), that runway's too.</summary>
        public void SetSurface(PavementSurface s)
        {
            if (tool == DevTool.Road && s == PavementSurface.Grass) s = PavementSurface.Dirt;
            surface = s;
            var apron = SelectedApron;
            if (apron != null && !airport.SetApronSurface(apron, s))
                apronProblem = airport.ApronProblem(apron.outline, s, apron);
            var st = SelectedStand;
            if (st != null) EditStand(x => x.surface = st.DeIcing && !s.IsPaved() ? PavementSurface.Concrete : s);
            var r = SelectedRunway;
            if (r == null || (tool != DevTool.Select && tool != DevTool.Runway)) return;
            r.surface = s;
            if (!r.Paved) r.lighting = RunwayLighting.None;
            airport.RunwaysChanged();
        }

        /// <summary>The next width up or down for new taxiways (taxiway tool) or runways, and the selected runway.</summary>
        public void StepWidth(int step)
        {
            if (tool == DevTool.Road) return;   // roads come in one width
            if (tool == DevTool.Stand || tool == DevTool.DeIcing || SelectedStand != null)
            {
                SetStandCode((SizeCode)Mathf.Clamp((int)StandCode + step, 0, SizeCodes.Count - 1));
                return;
            }
            if (tool == DevTool.Taxiway)
            {
                taxiwayWidth = Mathf.Clamp(taxiwayWidth + step, 0, RunwayRules.TaxiwayWidths.Length - 1);
                return;
            }
            var r = SelectedRunway;
            if (r != null) runwayWidth = Nearest(RunwayRules.RunwayWidths, r.width);
            runwayWidth = Mathf.Clamp(runwayWidth + step, 0, RunwayRules.RunwayWidths.Length - 1);
            if (r == null) return;
            r.width = RunwayRules.RunwayWidths[runwayWidth];
            airport.RunwaysChanged();
        }

        /// <summary>The size code for new stands and bays, and the selected one's.</summary>
        public void SetStandCode(SizeCode code)
        {
            standCode = code;
            if (SelectedStand != null) EditStand(x => x.code = code);
        }

        /// <summary>The angle new stands meet their front line at (MinStandAngle to MaxStandAngle degrees), and the
        /// selected stand's.</summary>
        public void SetStandAngle(float degrees)
        {
            standAngle = Mathf.Clamp(Mathf.Round(degrees * 10f) / 10f, MinStandAngle, MaxStandAngle);
            var st = SelectedStand;
            if (st == null || st.DeIcing) return;
            var l = airport.Taxiways.Layout(st);
            if (l == null) return;
            EditStand(x => x.heading = Bearing(AxisFor(l.along, l.side, standAngle)));
        }

        /// <summary>One of the usual angles (StandAngles), leaning the way the stand leans now.</summary>
        public void SetStandPreset(float preset) => SetStandAngle(StandLeans ? 180f - preset : preset);

        /// <summary>Turn the stand angle by some degrees (new stands' and the selected stand's).</summary>
        public void TurnStand(float degrees) => SetStandAngle(StandAngle + degrees);

        /// <summary>Z: the next of the usual angles below the current one, round to 90 again, keeping the lean.</summary>
        public void NextStandAngle()
        {
            float a = StandAngle;
            float m = StandLeans ? 180f - a : a;
            float next = StandAngles[0];
            foreach (float preset in StandAngles)
                if (preset < m - 0.5f)
                {
                    next = preset;
                    break;
                }
            SetStandPreset(next);
        }

        /// <summary>Lean new stands the other way along their front line (bays: face the other way); the selected one
        /// too.</summary>
        public void FlipStand()
        {
            var st = SelectedStand;
            bool bay = st != null ? st.DeIcing : tool == DevTool.DeIcing;
            if (!bay)
            {
                SetStandAngle(180f - StandAngle);
                return;
            }
            bayFlip = !bayFlip;
            freeHeading = Mathf.Repeat(freeHeading + 180f, 360f);
            if (st != null) EditStand(x => x.heading = Mathf.Repeat(x.heading + 180f, 360f));
        }

        /// <summary>Turn the selected stand, if it isn't connected, clockwise about the middle of its pad.</summary>
        public void RotateStand(float degrees)
        {
            var st = SelectedStand;
            var l = st != null ? airport.Taxiways.Layout(st) : null;
            if (l == null || l.edge != null) return;
            var about = l.Centre;
            EditStand(x => x.Turn(degrees, about));
        }

        /// <summary>Push-back or taxi-out, for new stands and the selected one.</summary>
        public void SetStandExit(StandExit exit)
        {
            standExit = exit;
            if (SelectedStand != null && !SelectedStand.DeIcing) EditStand(x => x.exit = exit);
        }

        /// <summary>Turn small aircraft away from new stands and the selected one, or take them again.</summary>
        public void SetStandNoSmall(bool on)
        {
            standNoSmall = on;
            if (SelectedStand != null && !SelectedStand.DeIcing) EditStand(x => x.noSmallAircraft = on);
        }

        /// <summary>Change the selected stand; put it back if it no longer fits.</summary>
        void EditStand(System.Action<Stand> change)
        {
            var st = SelectedStand;
            var net = airport.Taxiways;
            var (code, surf, exit, heading, position, front, lane) = (st.code, st.surface, st.exit, st.heading, st.position, st.Front, st.lane);
            change(st);
            if (!RunwayRules.StandSurfaceOk(st.Code, st.surface) || !net.Fits(net.Layout(st)))
            {
                (st.code, st.surface, st.exit, st.heading, st.position) = (code, surf, exit, heading, position);
                st.Frame(front, lane);
            }
            net.Rebuild();
        }

        /// <summary>A stand centreline at `angle` degrees from `along` (0..180), on the `side` of it.</summary>
        static Vector2 AxisFor(Vector2 along, Vector2 side, float angle)
        {
            float rad = angle * Mathf.Deg2Rad;
            return (along * Mathf.Cos(rad) + side * Mathf.Sin(rad)).normalized;
        }

        /// <summary>A direction turned clockwise by some degrees.</summary>
        static Vector2 Turned(Vector2 d, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(d.x * c + d.y * s, -d.x * s + d.y * c);
        }

        static Vector2 FromBearing(float degrees) =>
            new Vector2(Mathf.Sin(degrees * Mathf.Deg2Rad), Mathf.Cos(degrees * Mathf.Deg2Rad));

        static float Bearing(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

        /// <summary>L: with the taxiway tool all taxiway lights on / off; else the selected runway's lighting goes
        /// round none / basic / precision.</summary>
        public void ToggleLighting()
        {
            if (tool == DevTool.Taxiway) ToggleTaxiwayLights();
            else if (SelectedRunway != null) SelectedRunway.SetLighting((RunwayLighting)(((int)SelectedRunway.lighting + 1) % 3));
        }

        public void ToggleTaxiwayLights()
        {
            var net = airport.Taxiways;
            net.SetLit(!net.edges.Any(e => e.lit));
        }

        public void SetWeather(SurfaceCondition c) => airport.Condition = c;
        public void SetTimeScale(float scale) => airport.timeScale = scale;
        public void Snowfall() => AddSnow(2f);
        public void GrowGrass() => AddGrass(6f);

        /// <summary>Plough the selection; with the taxiway or road tool every taxiway and road; with nothing selected
        /// everything.</summary>
        public void Plough()
        {
            var work = airport.Maintenance;
            if (tool == DevTool.Taxiway || tool == DevTool.Road) work.PloughTaxiways();
            else if (SelectedRunway != null) work.Plough(SelectedRunway);
            else if (SelectedEdge != null) work.Plough(SelectedEdge);
            else work.PloughAll();
        }

        /// <summary>Mow the selection (or its verges); with nothing selected all grass and verges.</summary>
        public void Mow(bool verges)
        {
            var work = airport.Maintenance;
            if (SelectedRunway != null)
            {
                if (verges) work.MowVerges(SelectedRunway);
                else work.Mow(SelectedRunway);
            }
            else if (SelectedEdge != null)
            {
                if (verges) work.MowVerges(SelectedEdge);
                else work.Mow(SelectedEdge);
            }
            else work.MowAll();
        }

        public void PloughAll() => airport.Maintenance.PloughAll();
        public void MowAll() => airport.Maintenance.MowAll();

        /// <summary>More or fewer ploughs / mowers in the fleet depot.</summary>
        public void StepFleet(int ploughs, int mowers)
        {
            var d = FleetDepot;
            if (d != null) d.SetFleet(Mathf.Clamp(d.snowPloughs + ploughs, 0, 8), Mathf.Clamp(d.mowers + mowers, 0, 4));
        }

        /// <summary>More or fewer small / large tugs in the fleet depot.</summary>
        public void StepTugs(int small, int large)
        {
            var d = FleetDepot;
            if (d != null) d.SetTugs(Mathf.Clamp(d.smallTugs + small, 0, 6), Mathf.Clamp(d.largeTugs + large, 0, 4));
        }

        public void Wear()
        {
            var r = SelectedRunway;
            if (r != null) r.SetWear(r.wear + 0.25f);
        }

        public void Repair()
        {
            var r = SelectedRunway;
            if (r != null) r.Repair();
        }

        /// <summary>Remove what's selected; false if nothing on the airfield is.</summary>
        public bool DeleteSelection()
        {
            if (selection.kind == Kind.None) return false;
            Remove(selection, false);
            return true;
        }

        /// <summary>Remove the selected taxiway or road section and the rest of its run.</summary>
        public void DeleteSelectedRun()
        {
            if (selection.kind == Kind.Edge) Remove(selection, true);
        }

        // ------------------------------------------------------------------ frame

        void Start()
        {
            cam = GetComponent<Camera>();
            if (airport == null) airport = FindAnyObjectByType<Airport>();
            types = FindObjectsByType<Aircraft.Aircraft>(FindObjectsInactive.Include)
                .Select(a => a.type).Where(t => t != null).Distinct().OrderBy(t => t.RunwayLength).ToArray();
            // sandbox scenes made before the toolbar existed don't have one
            if (GetComponent<DevToolbar>() == null) gameObject.AddComponent<DevToolbar>();
        }

        void OnDisable()
        {
            Active = Deleting = TakesScroll = false;
            outlines.Begin();
            outlines.End();
        }

        void OnDestroy() => outlines.Destroy();

        void Update()
        {
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            if (airport == null || cam == null || keys == null || mouse == null) return;
            if (!Valid(selection)) selection = Pick.None;
            if (chainNode >= 0 && airport.Taxiways.Node(chainNode) == null) chainNode = -1;
            HandleKeys(keys, mouse);
            StandScroll(keys, mouse);
            Active = tool != DevTool.Select;
            Deleting = tool == DevTool.Delete;

            var screen = mouse.position.ReadValue();
            bool onGround = Ground(screen, out var point);
            if (onGround) lastGround = point;
            else point = lastGround;
            bool free = onGround && !DevGui.PointerOverHud;
            bool click = free && mouse.leftButton.wasPressedThisFrame;
            bool overAircraft = free && Aircraft.Aircraft.TryPick(cam.ScreenPointToRay(screen), out _);
            hover = (tool == DevTool.Select || tool == DevTool.Delete) && free && !overAircraft ? PickAt(point) : Pick.None;
            hoverRun = tool == DevTool.Delete && hover.kind == Kind.Edge && keys.shiftKey.isPressed;
            switch (tool)
            {
                case DevTool.Runway: RunwayTool(mouse, keys, click, point); break;
                case DevTool.Taxiway: TaxiwayTool(click, free, point, false); break;
                case DevTool.Road: TaxiwayTool(click, free, point, true); break;
                case DevTool.Apron: ApronTool(mouse, keys, click, free, point); break;
                case DevTool.Depot: DepotTool(mouse, click, point); break;
                case DevTool.Stand:
                case DevTool.DeIcing: StandTool(click, free, point); break;
                case DevTool.Delete:
                    if (click && hover.kind != Kind.None) Remove(hover, hoverRun);
                    break;
                default:
                    // a click on an aircraft selects it instead (DevSandboxControls)
                    if (click) Select(hover);
                    break;
            }
            DrawOutlines(free, point);
        }

        void HandleKeys(Keyboard keys, Mouse mouse)
        {
            if (keys.bKey.wasPressedThisFrame) SetTool(tool >= DevTool.DeIcing ? DevTool.Select : tool + 1);
            if (keys.xKey.wasPressedThisFrame) SetTool(tool == DevTool.Delete ? DevTool.Select : DevTool.Delete);
            if (keys.escapeKey.wasPressedThisFrame)
            {
                if (chainNode >= 0) chainNode = -1;
                else if (apronCorners.Count > 0 || apronDrag) SetTool(tool);
                else if (tool != DevTool.Select) SetTool(DevTool.Select);
                else selection = Pick.None;
            }
            if (mouse.rightButton.wasPressedThisFrame) chainNode = -1;
            if (keys.vKey.wasPressedThisFrame) SetSurface((PavementSurface)(((int)surface + 1) % Surfaces.Count));
            if (keys.leftBracketKey.wasPressedThisFrame) StepWidth(-1);
            if (keys.rightBracketKey.wasPressedThisFrame) StepWidth(1);
            if (keys.lKey.wasPressedThisFrame) ToggleLighting();
            if (keys.cKey.wasPressedThisFrame) SetWeather((SurfaceCondition)(((int)airport.Condition + 1) % 3));
            if (keys.yKey.wasPressedThisFrame)
                SetTimeScale(TimeScales[(System.Array.IndexOf(TimeScales, airport.timeScale) + 1) % TimeScales.Length]);
            if (keys.oKey.wasPressedThisFrame) Snowfall();
            if (keys.uKey.wasPressedThisFrame) GrowGrass();
            if (keys.pKey.wasPressedThisFrame) Plough();
            if (keys.mKey.wasPressedThisFrame) Mow(keys.shiftKey.isPressed);
            if (keys.commaKey.wasPressedThisFrame || keys.periodKey.wasPressedThisFrame)
            {
                int step = keys.periodKey.wasPressedThisFrame ? 1 : -1;
                if (keys.shiftKey.isPressed) StepFleet(0, step);
                else StepFleet(step, 0);
            }
            if (keys.kKey.wasPressedThisFrame) Wear();
            if (keys.jKey.wasPressedThisFrame) Repair();
            if (keys.zKey.wasPressedThisFrame)
            {
                if (keys.shiftKey.isPressed) FlipStand();
                else NextStandAngle();
            }
            if (keys.iKey.wasPressedThisFrame) SetStandExit(StandExit == StandExit.PushBack ? StandExit.TaxiOut : StandExit.PushBack);
        }

        /// <summary>
        /// Shift+scroll turns the stand being placed, or the selected one: 5 degrees a notch, 1 with Alt. One opening
        /// onto a taxiway (or a stand's taxilane) turns against it, 25 to 155 degrees; a free one or a bay all the way
        /// round. (With Shift held, macOS turns a mouse wheel's scroll sideways, so both axes count.)
        /// </summary>
        void StandScroll(Keyboard keys, Mouse mouse)
        {
            var st = SelectedStand;
            bool placing = tool == DevTool.Stand || tool == DevTool.DeIcing;
            TakesScroll = keys.shiftKey.isPressed && !DevGui.PointerOverHud && (placing || (tool == DevTool.Select && st != null));
            if (!TakesScroll)
            {
                scrollTurn = 0f;
                return;
            }
            var s = mouse.scroll.ReadValue();
            scrollTurn += Mathf.Clamp(Mathf.Abs(s.y) >= Mathf.Abs(s.x) ? s.y : s.x, -4f, 4f);
            float step = keys.altKey.isPressed ? 1f : 5f;
            int notches = (int)scrollTurn;
            if (notches == 0) return;
            scrollTurn -= notches;
            // 5 degree steps land on multiples of 5
            float Stepped(float a) => step > 1f
                ? (notches > 0 ? Mathf.Floor(a / step + 0.01f) + notches : Mathf.Ceil(a / step - 0.01f) + notches) * step
                : Mathf.Round(a) + notches;
            if (placing)
            {
                // turning the next one: the stand placed last is let go
                if (st != null) selection = Pick.None;
                if (ghostAttached && tool == DevTool.Stand) SetStandAngle(Stepped(standAngle));
                else freeHeading = Mathf.Repeat(Stepped(freeHeading), 360f);
                return;
            }
            var l = airport.Taxiways.Layout(st);
            if (l == null) return;
            if (l.edge == null) RotateStand(Stepped(st.heading) - st.heading);
            else if (!st.DeIcing) SetStandAngle(Stepped(StandAngle));
        }

        // ------------------------------------------------------------------ tools

        void RunwayTool(Mouse mouse, Keyboard keys, bool click, Vector3 point)
        {
            var local = airport.transform.InverseTransformPoint(point);
            if (click)
            {
                dragStart = local;
                dragging = airport.AddRunway(local, local + Vector3.forward * 50f, surface,
                    RunwayRules.RunwayWidths[runwayWidth]);
                Select(new Pick { kind = Kind.Runway, runway = dragging, apron = -1 });
            }
            if (dragging == null) return;
            var d = local - dragStart;
            d.y = 0f;
            float length = d.magnitude;
            if (keys.shiftKey.isPressed && length > 1f)
            {
                float bearing = Mathf.Round(RunwayRules.Bearing(d) / 10f) * 10f * Mathf.Deg2Rad;
                length = Mathf.Max(Mathf.Round(length / 50f) * 50f, 50f);
                d = new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * length;
            }
            if (length > 1f && (dragging.end - (dragStart + d)).sqrMagnitude > 0.01f)
            {
                dragging.start = dragStart;
                dragging.end = dragStart + d;
                dragging.gameObject.name = "Runway " + dragging.Designation;
                dragging.Rebuild();
            }
            if (mouse.leftButton.wasReleasedThisFrame) FinishDrag();
        }

        /// <summary>The runway being dragged out is done (one shorter than 100 m is dropped).</summary>
        void FinishDrag()
        {
            if (dragging == null) return;
            if (dragging.Length < 100f)
            {
                airport.RemoveRunway(dragging);
                selection = Pick.None;
            }
            else airport.RunwaysChanged();
            dragging = null;
        }

        void TaxiwayTool(bool click, bool free, Vector3 point, bool road)
        {
            var net = airport.Taxiways;
            roadProblem = null;
            lineSnapShown = free;
            if (!free) return;
            lineSnap = SnapLine(net, Geo.XZ(airport.transform.InverseTransformPoint(point)), road, Keyboard.current.shiftKey.isPressed, point);
            if (chainNode >= 0)
            {
                var across = net.StandAcross(net.At(chainNode), lineSnap.p, NewWidth, road);
                if (across != null)
                    roadProblem = road ? $"would cross {across.Name} (roads may only touch stands)"
                        : $"would cross {across.Name} (taxiways run along a stand's front, the dashed line)";
            }
            if (!click || roadProblem != null) return;
            int node = Commit(net, lineSnap);
            if (chainNode >= 0 && chainNode != node)
            {
                if (road)
                    net.Connect(chainNode, node, RunwayRules.RoadWidth, surface == PavementSurface.Grass ? PavementSurface.Asphalt : surface,
                        false, RoadClass.Service);
                else
                    net.Connect(chainNode, node, RunwayRules.TaxiwayWidths[taxiwayWidth], surface,
                        net.edges.Any(e => e.lit) && surface.IsPaved());
                net.Rebuild();
            }
            chainNode = node;
        }

        /// <summary>Apron tool: corners one click at a time (finished by clicking the first again or right-clicking), or
        /// a rectangle dragged out from the first. The outline shows red, with the reason in the tool strip, where it
        /// won't do.</summary>
        void ApronTool(Mouse mouse, Keyboard keys, bool click, bool free, Vector3 point)
        {
            bool shift = keys.shiftKey.isPressed;
            var raw = Geo.XZ(airport.transform.InverseTransformPoint(point));
            var p = SnapCorner(raw, shift, point, out apronSnap);
            apronCursor = p;
            if (click)
            {
                apronPress = true;
                pressScreen = mouse.position.ReadValue();
                apronDragStart = p;
            }
            // a press that moves off before any corner is down drags out a rectangle
            if (apronPress && !apronDrag && apronCorners.Count == 0 && mouse.leftButton.isPressed &&
                Vector2.Distance(mouse.position.ReadValue(), pressScreen) > 6f)
                apronDrag = true;
            if (apronDrag)
            {
                apronRect = DragRect(apronDragStart, p, shift);
                apronProblem = airport.ApronProblem(apronRect, surface);
                if (!mouse.leftButton.wasReleasedThisFrame) return;
                if (apronProblem == null) SelectApron(airport.AddApron(apronRect, surface));
                apronDrag = apronPress = false;
                apronRect = null;
                return;
            }
            bool closing = apronCorners.Count >= 3 && p == apronCorners[0];
            // while drawing, only crossing edges and overlaps are worth a warning
            var drawn = new List<Vector2>(apronCorners);
            if (!closing) drawn.Add(p);
            apronProblem = drawn.Count >= 3 && (Geo.SelfIntersects(drawn) || Overlapping(drawn))
                ? airport.ApronProblem(drawn, surface) : null;
            if (apronPress && mouse.leftButton.wasReleasedThisFrame)
            {
                apronPress = false;
                if (closing) FinishApron();
                else if (apronCorners.Count == 0 || Vector2.Distance(p, apronCorners[apronCorners.Count - 1]) > 0.5f) apronCorners.Add(p);
            }
            if (free && mouse.rightButton.wasPressedThisFrame) FinishApron();
        }

        bool Overlapping(List<Vector2> outline) =>
            airport.aprons.Any(a => a.surface != surface && a.surface.IsPaved() == surface.IsPaved() && Geo.Overlap(outline, a.outline));

        /// <summary>Build the apron drawn so far (fewer than three corners: drop it). One that won't do stays to be
        /// fixed or dropped (Esc).</summary>
        void FinishApron()
        {
            if (apronCorners.Count < 3)
            {
                apronCorners.Clear();
                return;
            }
            apronProblem = airport.ApronProblem(apronCorners, surface);
            if (apronProblem != null) return;
            SelectApron(airport.AddApron(apronCorners, surface));
            apronCorners.Clear();
        }

        void SelectApron(ApronArea a)
        {
            if (a != null) Select(new Pick { kind = Kind.Apron, apron = airport.aprons.IndexOf(a) });
        }

        /// <summary>
        /// A corner for the apron tool and what it sticks to: the first corner (to close the outline), else a corner of
        /// an apron or a stand pad, else a point on one of their edges, within SnapReach. Otherwise the cursor; with
        /// Shift (steps), the edge from the last corner turned to 10 degree steps from the reference direction and a
        /// whole 5 m long.
        /// </summary>
        Vector2 SnapCorner(Vector2 p, bool steps, Vector3 world, out string what)
        {
            float reach = SnapReach(world);
            what = null;
            if (apronCorners.Count >= 3 && Vector2.Distance(p, apronCorners[0]) < reach)
            {
                what = "the first corner: closes the apron";
                return apronCorners[0];
            }
            var shapes = new List<(IList<Vector2> outline, string name)>();
            foreach (var a in airport.aprons) shapes.Add((a.outline, "apron"));
            foreach (var l in airport.Taxiways.Layouts()) shapes.Add((l.Pad, l.stand.Name));
            Vector2 best = p;
            float bestD = reach;
            foreach (var (outline, name) in shapes)
                foreach (var c in outline)
                    if (Vector2.Distance(c, p) < bestD)
                    {
                        bestD = Vector2.Distance(c, p);
                        best = c;
                        what = $"a corner of the {name}";
                    }
            if (what != null) return best;
            foreach (var (outline, name) in shapes)
                for (int i = 0; i < outline.Count; i++)
                {
                    var c = Geo.ClosestOnSegment(p, outline[i], outline[(i + 1) % outline.Count], out _);
                    if (Vector2.Distance(c, p) < bestD)
                    {
                        bestD = Vector2.Distance(c, p);
                        best = c;
                        what = $"the edge of the {name}";
                    }
                }
            if (what != null || !steps || apronCorners.Count == 0) return what != null ? best : p;
            var last = apronCorners[apronCorners.Count - 1];
            var before = apronCorners.Count >= 2 ? last - apronCorners[apronCorners.Count - 2] : (Vector2?)null;
            return last + Stepped(Reference(last, before), p - last);
        }

        /// <summary>The rectangle with opposite corners a and b, its sides lined up with what a sits on (an apron's
        /// or a stand's edge, a taxiway, a road), else the nearest runway, else north; with steps, the sides whole 5 m
        /// long.</summary>
        Vector2[] DragRect(Vector2 a, Vector2 b, bool steps)
        {
            var axis = Reference(a, null);
            var right = Geo.Right(axis);
            var ab = b - a;
            float du = Vector2.Dot(ab, axis), dv = Vector2.Dot(ab, right);
            if (steps)
            {
                du = Mathf.Round(du / LengthStep) * LengthStep;
                dv = Mathf.Round(dv / LengthStep) * LengthStep;
            }
            var u = axis * du;
            var v = right * dv;
            return new[] { a, a + u, a + u + v, a + v };
        }

        // ------------------------------------------------------------------ snapping and steps

        /// <summary>Shift's steps: lines turn in AngleStep degrees from the reference direction and run in whole
        /// LengthStep metres.</summary>
        const float AngleStep = 10f, LengthStep = 5f;

        /// <summary>How near (metres) the cursor must come to something to stick to it: a few pixels at any zoom,
        /// never under 1.5 m.</summary>
        float SnapReach(Vector3 world) => Mathf.Max(1.5f, MetresPerPixel(world) * 14f);

        /// <summary>A line from a point turned to AngleStep steps from `reference` and a whole LengthStep long.</summary>
        static Vector2 Stepped(Vector2 reference, Vector2 d)
        {
            float len = d.magnitude;
            if (len < 0.5f) return d;
            float angle = Vector2.SignedAngle(reference, d);
            angle = Mathf.Round(angle / AngleStep) * AngleStep * Mathf.Deg2Rad;
            var dir = new Vector2(reference.x * Mathf.Cos(angle) - reference.y * Mathf.Sin(angle),
                reference.x * Mathf.Sin(angle) + reference.y * Mathf.Cos(angle));
            return dir * Mathf.Max(LengthStep, Mathf.Round(len / LengthStep) * LengthStep);
        }

        /// <summary>
        /// The direction Shift's angle steps count from at a point: the line it sits on (a taxiway, road, runway, or an
        /// apron's or stand's edge, within half a metre), else `previous` (the line being drawn's last stretch), else
        /// the nearest of those lines within 60 m, else the nearest runway, else north. So steps line up with what's
        /// built: parallel, square, or 10, 20 .. degrees off it.
        /// </summary>
        Vector2 Reference(Vector2 at, Vector2? previous)
        {
            var lines = new List<(Vector2 a, Vector2 b)>();
            var net = airport.Taxiways;
            foreach (var e in net.edges) lines.Add((net.At(e.a), net.At(e.b)));
            foreach (var r in airport.Runways) lines.Add((Geo.XZ(r.start), Geo.XZ(r.end)));
            foreach (var a in airport.aprons)
                for (int i = 0; i < a.outline.Count; i++) lines.Add((a.outline[i], a.outline[(i + 1) % a.outline.Count]));
            foreach (var l in net.Layouts())
            {
                var pad = l.Pad;
                for (int i = 0; i < pad.Length; i++) lines.Add((pad[i], pad[(i + 1) % pad.Length]));
            }
            Vector2 Nearest(float within)
            {
                float best = within;
                var dir = Vector2.zero;
                foreach (var (a, b) in lines)
                {
                    if ((b - a).sqrMagnitude < 0.25f) continue;
                    float d = Vector2.Distance(Geo.ClosestOnSegment(at, a, b, out _), at);
                    if (d >= best) continue;
                    best = d;
                    dir = (b - a).normalized;
                }
                return dir;
            }
            var on = Nearest(0.5f);
            if (on != Vector2.zero) return on;
            if (previous.HasValue && previous.Value.sqrMagnitude > 0.25f) return previous.Value.normalized;
            var near = Nearest(60f);
            if (near != Vector2.zero) return near;
            float far = float.MaxValue;
            var axis = Vector2.up;
            foreach (var r in airport.Runways)
            {
                float d = Vector2.Distance(Geo.ClosestOnSegment(at, Geo.XZ(r.start), Geo.XZ(r.end), out _), at);
                if (d >= far) continue;
                far = d;
                axis = r.Axis2D;
            }
            return axis;
        }

        /// <summary>Where the taxiway / road tool's next point goes and what it joins.</summary>
        struct LineSnap
        {
            public Vector2 p;
            /// <summary>An existing node, or -1.</summary>
            public int node;
            /// <summary>A section to split at p, or null.</summary>
            public TaxiwayEdge edge;
            /// <summary>A runway to put a node on, or null.</summary>
            public Runway runway;
            /// <summary>What it joins, in words, or null for a new end.</summary>
            public string what;
        }

        /// <summary>Drag out a new depot (from its door front, the way the doors face), or click one to select it.</summary>
        void DepotTool(Mouse mouse, bool click, Vector3 point)
        {
            var local = airport.transform.InverseTransformPoint(point);
            var p = new Vector2(local.x, local.z);
            if (click)
            {
                if (airport.TryPickDepot(p, out var hit, 2f))
                {
                    Select(new Pick { kind = Kind.Depot, depot = hit, apron = -1 });
                    return;
                }
                dragStart = local;
                placingDepot = true;
            }
            if (placingDepot && mouse.leftButton.wasReleasedThisFrame)
            {
                placingDepot = false;
                var d = p - new Vector2(dragStart.x, dragStart.z);
                if (d.sqrMagnitude < 4f) d = Vector2.up;
                float bearing = Mathf.Atan2(d.x, d.y);
                if (Keyboard.current.shiftKey.isPressed)
                    bearing = Bearing(Stepped(Reference(new Vector2(dragStart.x, dragStart.z), null), d)) * Mathf.Deg2Rad;
                var template = airport.Depot;
                var depot = airport.AddDepot(new Vector2(dragStart.x, dragStart.z), new Vector2(Mathf.Sin(bearing), Mathf.Cos(bearing)), template);
                if (template == null)
                {
                    depot.SetAssets(ploughType, mowerType, depotType, ploughPrefab, mowerPrefab, depotPrefab);
                    depot.SetTugAssets(smallTugType, largeTugType, smallTugPrefab, largeTugPrefab);
                    depot.gameObject.SetActive(false);
                    depot.gameObject.SetActive(true);
                }
                Select(new Pick { kind = Kind.Depot, depot = depot, apron = -1 });
            }
        }

        /// <summary>Stand / de-icing bay tool: a ghost of the stand under the cursor (on a taxiway or a stand's
        /// taxilane, snapped to its neighbours; or free), built on a click; a click on a stand selects it.</summary>
        void StandTool(bool click, bool free, Vector3 point)
        {
            var net = airport.Taxiways;
            var p = Geo.XZ(airport.transform.InverseTransformPoint(point));
            ghost = null;
            ghostProblem = null;
            if (!free) return;
            if (net.TryPickStand(p, out var existing))
            {
                hover = new Pick { kind = Kind.Stand, stand = existing, apron = -1 };
                if (click) Select(hover);
                return;
            }
            ghost = Ghost(p, out ghostProblem);
            if (!click || ghost == null || ghostProblem != null) return;
            var made = net.Add(ghost.stand);
            net.Rebuild();
            if (made != null) Select(new Pick { kind = Kind.Stand, stand = made, apron = -1 });
            ghost = null;
        }

        /// <summary>
        /// The stand or bay the tool would build for a ground point. Where the point is beside a taxiway, or beside
        /// another stand's front line (the taxilane still to be laid), within the depth of the stand's pad (a bay: on
        /// it), it opens onto that line: its centreline under the point (bays: centred on it), pulled against a
        /// neighbour within reach. Anywhere else it stands free, its pad centred on the point, turned to freeHeading,
        /// its front line at standAngle to it. Null with a reason if it can't be laid out; a layout with a reason if
        /// it doesn't fit.
        /// </summary>
        StandLayout Ghost(Vector2 p, out string problem)
        {
            problem = null;
            var net = airport.Taxiways;
            bool bay = tool == DevTool.DeIcing;
            var stand = new Stand { kind = bay ? StandKind.DeIcing : StandKind.Parking, code = standCode,
                surface = bay && !surface.IsPaved() ? PavementSurface.Concrete : surface, exit = standExit,
                noSmallAircraft = !bay && standNoSmall };
            char c = stand.Code;
            float span = RunwayRules.MaxWingspan(c), length = RunwayRules.StandLength(c), clr = RunwayRules.StandClearance(c);

            // the lines it can open onto: taxiway sections, and the front lines of stands of its kind (a way on beyond
            // their taxiway's end, or the taxilane still to come)
            var lines = new List<(Vector2 a, Vector2 b, TaxiwayEdge edge, float lane, string name)>();
            foreach (var e in net.edges)
                if (!e.IsRoad) lines.Add((net.At(e.a), net.At(e.b), e, e.width, "the taxiway"));
            foreach (var o in net.Layouts())
            {
                if (o.stand.kind != stand.kind) continue;
                var (m0, m1) = o.Mouth;
                lines.Add((m0 - o.along * 150f, m1 + o.along * 150f, null, o.stand.Lane, $"{o.stand.Name}'s taxilane"));
            }
            var best = (a: default(Vector2), b: default(Vector2), edge: (TaxiwayEdge)null, lane: 0f, name: (string)null);
            float bestD = float.MaxValue;
            foreach (var line in lines)
            {
                var d = line.b - line.a;
                float len = d.magnitude;
                if (len < 1f) continue;
                var t = d / len;
                float s = Vector2.Dot(p - line.a, t), off = Mathf.Abs(Geo.Cross(t, p - line.a));
                float depth = bay ? span * 0.5f + clr : RunwayRules.TaxilaneClearance(line.lane) + length + clr;
                if (s < 0f || s > len || off > depth || off >= bestD) continue;
                bestD = off;
                best = line;
            }

            StandLayout l;
            ghostAttached = best.name != null;
            if (ghostAttached)
            {
                var t = (best.b - best.a).normalized;
                float len = Vector2.Distance(best.a, best.b);
                Vector2 entry;
                if (bay)
                {
                    entry = best.a + t * Vector2.Dot(p - best.a, t);
                    stand.heading = Bearing(bayFlip ? -t : t);
                }
                else
                {
                    float cross = Geo.Cross(t, p - best.a);
                    var axis = AxisFor(t, cross >= 0f ? Geo.Left(t) : Geo.Right(t), standAngle);
                    entry = p - axis * (Mathf.Abs(cross) / Mathf.Abs(Geo.Cross(t, axis)));
                    stand.heading = Bearing(axis);
                }
                entry = best.a + t * Mathf.Clamp(Vector2.Dot(entry - best.a, t), 0.5f, len - 0.5f);
                stand.position = Geo.X0Z(entry);
                stand.Frame(t, best.lane);
                l = StandLayout.Of(stand);
                if (l != null)
                {
                    l.edge = best.edge;
                    l = Snap(l);
                }
                ghostNote = best.edge != null ? "opens onto the taxiway" : $"opens onto {best.name} (connect it with a taxiway along the dashed line)";
            }
            else
            {
                // free: centred on the cursor, turned to freeHeading, its front line at standAngle to it
                var axis = FromBearing(freeHeading);
                stand.heading = freeHeading;
                stand.position = Geo.X0Z(p);
                stand.Frame(bay ? axis : Turned(axis, standAngle), 0f);
                l = StandLayout.Of(stand);
                if (l != null)
                {
                    stand.position += Geo.X0Z(p - l.Centre);
                    l = StandLayout.Of(stand);
                }
                ghostNote = "free: connect it later with a taxiway along the dashed line";
            }
            if (l == null)
            {
                problem = "can't lay it out at this angle";
                return null;
            }
            if (!RunwayRules.StandSurfaceOk(stand.Code, stand.surface)) problem = "grass and dirt stands take codes A and B only";
            else problem = FitProblem(l);
            return l;
        }

        /// <summary>Why a stand doesn't fit where it is, or null.</summary>
        string FitProblem(StandLayout l)
        {
            var net = airport.Taxiways;
            if (net.Fits(l)) return null;
            foreach (var o in net.Layouts())
                if (o.stand != l.stand && l.Overlaps(o)) return $"overlaps {o.stand.Name}";
            return "runs over a taxiway, road or runway";
        }

        /// <summary>Slide a ghost along its front line to sit against a stand (bays: a bay) beside it on the same line,
        /// if one is within 40% of the ghost's frontage and the result fits.</summary>
        StandLayout Snap(StandLayout g)
        {
            var net = airport.Taxiways;
            var t = g.along;
            var a = g.entry;
            // where a stand's sides (bays: ends) cross the front line, metres along from the ghost's entry
            float[] Lines(StandLayout l) => l.stand.DeIcing
                ? new[] { Vector2.Dot(l.At(0f, l.near0) - a, t), Vector2.Dot(l.At(0f, l.far) - a, t) }
                : new[] { Vector2.Dot(l.At(l.x0, l.SAt(l.x0, 0f)) - a, t), Vector2.Dot(l.At(l.x1, l.SAt(l.x1, 0f)) - a, t) };
            var mine = Lines(g);
            float reach = 0.4f * Mathf.Abs(mine[1] - mine[0]);
            StandLayout snapped = g;
            float bestShift = reach;
            foreach (var other in net.Layouts())
            {
                if (other.stand.kind != g.stand.kind || Mathf.Abs(Geo.Cross(other.along, t)) > 0.02f ||
                    Mathf.Abs(Geo.Cross(t, other.entry - a)) > StandLayout.OnCentreline) continue;
                if (!g.stand.DeIcing && Vector2.Dot(other.side, g.side) < 0.5f) continue;
                foreach (float theirs in Lines(other))
                    foreach (float own in mine)
                    {
                        float shift = theirs - own;
                        if (Mathf.Abs(shift) >= bestShift) continue;
                        var l = StandLayout.Of(g.stand.At(g.stand.position + Geo.X0Z(t * shift)));
                        if (l == null || !net.Fits(l)) continue;
                        l.edge = g.edge;
                        bestShift = Mathf.Abs(shift);
                        snapped = l;
                    }
            }
            return snapped;
        }

        void AddSnow(float cm)
        {
            airport.groundSnow += cm;
            foreach (var r in airport.Runways) r.SetSnow(r.snow + cm);
            foreach (var e in airport.Taxiways.edges) e.snow += cm;
            airport.Taxiways.RefreshState();
            airport.Simulate(0.001f);
        }

        void AddGrass(float cm)
        {
            foreach (var r in airport.Runways)
            {
                if (r.surface == PavementSurface.Grass) r.SetGrass(r.grass + cm);
                r.SetVergeGrass(r.vergeGrass + cm);
            }
            foreach (var e in airport.Taxiways.edges)
            {
                if (e.surface == PavementSurface.Grass) e.grass += cm;
                if (e.HasVerges) e.vergeGrass += cm;
            }
            airport.Taxiways.RefreshState();
        }

        /// <summary>
        /// Where the taxiway or road tool's next point goes, within SnapReach of something:
        ///  - a road: the middle of a depot's doors' front, 12 m out (vehicles drive straight out onto it); the head of
        ///    a parking stand, alongside it with the road's edge on the pad's (so a straight road along a row of
        ///    stands serves them all), or onto it when the road comes at it end-on;
        ///  - a node, then a section, of its own kind (taxiways: taxiways, roads: roads); one of the other kind, or a
        ///    runway, only with the cursor on its pavement, so a line laid beside a taxiway doesn't jump onto it;
        ///  - else the cursor, or with Shift (steps) the line from the last point turned to 10 degree steps from the
        ///    reference direction and a whole 5 m long.
        /// </summary>
        LineSnap SnapLine(TaxiwayNetwork net, Vector2 q, bool road, bool steps, Vector3 world)
        {
            float reach = SnapReach(world);
            var none = new LineSnap { p = q, node = -1 };
            if (road)
            {
                foreach (var d in airport.Depots)
                {
                    var front = d.Front + d.Outward * 12f;
                    if (Vector2.Distance(front, q) < Mathf.Max(reach * 2f, 6f))
                        return Onto(net, front, reach, $"the front of {d.name}'s doors");
                }
                float hw = RunwayRules.RoadWidth * 0.5f, best = reach * 1.5f;
                LineSnap? head = null;
                foreach (var l in net.Layouts())
                {
                    if (l.stand.DeIcing) continue;
                    var h0 = l.At(l.x0, l.far);
                    var h1 = l.At(l.x1, l.far);
                    var t = (h1 - h0).normalized;
                    bool endOn = chainNode >= 0 && Mathf.Abs(Vector2.Dot((q - net.At(chainNode)).normalized, t)) < 0.7f;
                    var along = endOn ? Geo.ClosestOnSegment(q, h0, h1, out _)
                        : Geo.ClosestOnSegment(q, h0 + l.axis * hw - t * hw, h1 + l.axis * hw + t * hw, out _);
                    float dist = Vector2.Distance(along, q);
                    if (dist >= best) continue;
                    best = dist;
                    head = Onto(net, along, 0.5f, $"the head of {l.stand.Name} (its way in)");
                }
                if (head.HasValue) return head.Value;
            }
            var snap = Onto(net, q, reach, null, road);
            if (snap.what == null && !road)
            {
                // a stand's front line: the taxilane that connects it
                float bestOff = reach * 1.5f;
                foreach (var l in net.Layouts())
                {
                    var (m0, m1) = l.Mouth;
                    var mid = (m0 + m1) * 0.5f;
                    if (Vector2.Distance(q, Geo.ClosestOnSegment(q, m0, m1, out _)) > 400f) continue;
                    var on = mid + l.along * Vector2.Dot(q - mid, l.along);
                    float off = Vector2.Distance(on, q);
                    if (off >= bestOff) continue;
                    bestOff = off;
                    snap = new LineSnap { p = on, node = -1,
                        what = $"{l.stand.Name}'s front line" + (l.edge == null ? " (connects it)" : "") +
                               (RunwayRules.TaxiwayCode(l.stand.Lane) < RunwayRules.TaxiwayCode(NewWidth)
                                   ? $": only code {RunwayRules.TaxiwayCode(l.stand.Lane)} and smaller will pass it" : "") };
                }
            }
            if (snap.what != null || !steps || chainNode < 0) return snap;
            var from = net.At(chainNode);
            Vector2? previous = null;
            foreach (var e in net.EdgesAt(chainNode))
            {
                previous = net.At(TaxiwayNetwork.Other(e, chainNode)) - from;
                break;
            }
            var p = from + Stepped(Reference(from, previous), q - from);
            var stepped = Onto(net, p, 0.5f, null, road);
            return stepped.what != null ? stepped : new LineSnap { p = p, node = -1 };
        }

        /// <summary>What a point lands on: a node, a section or a runway within reach (`road` says which kind sticks
        /// from further than its pavement), else nothing. `what` names a target that isn't one of those.</summary>
        LineSnap Onto(TaxiwayNetwork net, Vector2 q, float reach, string what, bool road = true)
        {
            TaxiwayNode bestNode = null;
            float bestD = float.MaxValue;
            foreach (var n in net.nodes)
            {
                float d = Vector2.Distance(Geo.XZ(n.position), q);
                if (d >= bestD) continue;
                bool same = n.runway != null && !road;
                float pavement = n.runway != null ? n.runway.HalfWidthTotal : 0f;
                foreach (var e in net.EdgesAt(n.id))
                {
                    same |= e.IsRoad == road;
                    pavement = Mathf.Max(pavement, e.width * 0.5f);
                }
                // its own kind sticks from anywhere on its pavement or within reach; the other kind only on its pavement
                if (d <= (same ? Mathf.Max(reach, pavement) : Mathf.Min(reach, pavement)))
                {
                    bestD = d;
                    bestNode = n;
                }
            }
            if (bestNode != null)
                return new LineSnap { p = Geo.XZ(bestNode.position), node = bestNode.id,
                    what = what ?? (bestNode.runway != null ? $"runway {bestNode.runway.Designation}" : JoinName(net, bestNode.id, road)) };
            TaxiwayEdge bestEdge = null;
            var on = q;
            bestD = float.MaxValue;
            foreach (var e in net.edges)
            {
                var c = Geo.ClosestOnSegment(q, net.At(e.a), net.At(e.b), out _);
                float d = Vector2.Distance(c, q);
                if (d >= bestD || d > (e.IsRoad == road ? Mathf.Max(reach, e.width * 0.5f) : e.width * 0.5f)) continue;
                bestD = d;
                bestEdge = e;
                on = c;
            }
            if (bestEdge != null)
                return new LineSnap { p = on, node = -1, edge = bestEdge,
                    what = what ?? (bestEdge.IsRoad ? (road ? "the road" : "the road (crossing: the road gives way)")
                        : road ? "the taxiway (crossing: the road gives way)" : "the taxiway") };
            foreach (var r in airport.Runways)
                if (r.Contains(Geo.X0Z(q)))
                    return new LineSnap { p = Geo.XZ(r.ClosestOnCentreline(Geo.X0Z(q))), node = -1, runway = r,
                        what = what ?? $"runway {r.Designation}" + (road ? " (a stop line)" : "") };
            return new LineSnap { p = q, node = -1, what = what };
        }

        /// <summary>A node's name for the strip: the end of a section, or a junction.</summary>
        static string JoinName(TaxiwayNetwork net, int node, bool road)
        {
            int roads = 0, taxiways = 0;
            foreach (var e in net.EdgesAt(node))
                if (e.IsRoad) roads++;
                else taxiways++;
            string kind = taxiways == 0 ? "road" : roads == 0 ? "taxiway" : "junction";
            return roads + taxiways == 1 ? $"the end of the {kind}" : $"the {kind} junction";
        }

        /// <summary>The node for a snapped point: the one it's on, a section split there, a runway node or a new one.</summary>
        int Commit(TaxiwayNetwork net, LineSnap s)
        {
            if (s.node >= 0 && net.Node(s.node) != null) return s.node;
            if (s.edge != null && net.edges.Contains(s.edge)) return net.SplitEdge(s.edge, Geo.X0Z(s.p));
            if (s.runway != null) return net.AddNode(Geo.X0Z(s.p), s.runway);
            return net.AddNode(Geo.X0Z(s.p));
        }

        bool Ground(Vector2 screen, out Vector3 point)
        {
            var ray = cam.ScreenPointToRay(screen);
            var plane = new Plane(airport.transform.up, airport.transform.position);
            if (plane.Raycast(ray, out float t))
            {
                point = ray.GetPoint(t);
                return true;
            }
            point = default;
            return false;
        }

        static int Nearest(float[] values, float v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
                if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v)) best = i;
            return best;
        }

        // ------------------------------------------------------------------ selecting and deleting

        void Select(Pick p)
        {
            selection = p;
            if (tool != DevTool.Apron) apronProblem = null;
            SelectedFrame = Time.frameCount;
            if (p.kind == Kind.Depot) fleetDepot = p.depot;
        }

        /// <summary>What's at a ground point: a depot, a runway, a taxiway or road section (a few pixels' slack, so thin
        /// ones can be hit from far out), or an apron.</summary>
        Pick PickAt(Vector3 world)
        {
            var local = airport.transform.InverseTransformPoint(world);
            var p = Geo.XZ(local);
            if (airport.TryPickDepot(p, out var d, 2f)) return new Pick { kind = Kind.Depot, depot = d, apron = -1 };
            if (airport.TryPickRunway(local, out var r)) return new Pick { kind = Kind.Runway, runway = r, apron = -1 };
            float slack = Mathf.Max(2f, MetresPerPixel(world) * 6f);
            if (airport.Taxiways.TryPickEdge(local, out var e, slack)) return new Pick { kind = Kind.Edge, edge = e, apron = -1 };
            if (airport.Taxiways.TryPickStand(p, out var st)) return new Pick { kind = Kind.Stand, stand = st, apron = -1 };
            if (airport.TryPickApron(p, out int i)) return new Pick { kind = Kind.Apron, apron = i };
            return Pick.None;
        }

        bool Valid(in Pick p) => p.kind switch
        {
            Kind.Runway => p.runway != null,
            Kind.Edge => airport.Taxiways.edges.Contains(p.edge),
            Kind.Depot => p.depot != null,
            Kind.Apron => p.apron >= 0 && p.apron < airport.aprons.Count,
            Kind.Stand => airport.Taxiways.stands.Contains(p.stand),
            _ => true,
        };

        string Describe(in Pick p)
        {
            switch (p.kind)
            {
                case Kind.Runway: return "Runway " + p.runway.Designation;
                case Kind.Edge: return p.edge.IsRoad ? RoadName(p.edge.road) : "Taxiway";
                case Kind.Depot: return p.depot.name;
                case Kind.Apron: return airport.aprons[p.apron].surface + " apron";
                case Kind.Stand: return p.stand.Name;
                default: return null;
            }
        }

        void Remove(Pick p, bool wholeRun)
        {
            switch (p.kind)
            {
                case Kind.Runway:
                    airport.RemoveRunway(p.runway);
                    break;
                case Kind.Edge:
                    var net = airport.Taxiways;
                    net.RemoveEdges(wholeRun ? net.Run(p.edge, _ => true).Select(x => x.edge) : new[] { p.edge });
                    net.Rebuild();
                    break;
                case Kind.Depot:
                    airport.RemoveDepot(p.depot);
                    break;
                case Kind.Apron:
                    airport.RemoveApron(p.apron);
                    break;
                case Kind.Stand:
                    airport.Taxiways.RemoveStand(p.stand);
                    airport.Taxiways.Rebuild();
                    break;
            }
            // removing an apron renumbers the ones after it
            if (selection.Same(p) || (p.kind == Kind.Apron && selection.kind == Kind.Apron)) selection = Pick.None;
            hover = Pick.None;
        }

        public static string RoadName(RoadClass c) => c switch
        {
            RoadClass.Street => "Street",
            RoadClass.Highway => "Highway",
            _ => "Service road",
        };

        float MetresPerPixel(Vector3 world) =>
            2f * Vector3.Distance(cam.transform.position, world) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(Screen.height, 1);

        // ------------------------------------------------------------------ outlines

        /// <summary>The selection in amber, what's under the cursor in white (red with the delete tool), and the
        /// taxiway or road being laid from its last point to the cursor.</summary>
        void DrawOutlines(bool pointing, Vector3 point)
        {
            outlines.Begin();
            if (selection.kind != Kind.None && Corners(selection)) outlines.Loop(cam, loop, DevGui.Accent, 3f);
            if (hover.kind != Kind.None && !hover.Same(selection))
            {
                var colour = tool == DevTool.Delete ? DevGui.Danger : new Color(0.92f, 0.94f, 0.97f);
                if (hoverRun)
                {
                    foreach (var (e, _) in airport.Taxiways.Run(hover.edge, _ => true))
                        if (Corners(new Pick { kind = Kind.Edge, edge = e, apron = -1 })) outlines.Loop(cam, loop, colour, 3f);
                }
                else if (Corners(hover)) outlines.Loop(cam, loop, colour, tool == DevTool.Delete ? 3f : 2f);
            }
            if (ghost != null && pointing)
            {
                loop.Clear();
                foreach (var v in ghost.Pad) loop.Add(airport.transform.TransformPoint(Geo.X0Z(v)));
                outlines.Loop(cam, loop, ghostProblem == null ? new Color(0.55f, 1f, 0.55f) : DevGui.Danger, 3f);
                if (ghost.edge == null) FrontLine(ghost);
            }
            // stands with no taxiway yet: where theirs goes, while laying taxiways or stands
            if (tool == DevTool.Taxiway || tool == DevTool.Stand || tool == DevTool.DeIcing || SelectedStand != null)
                foreach (var l in airport.Taxiways.Layouts())
                    if (l.edge == null) FrontLine(l);
            if (tool == DevTool.Apron && pointing)
            {
                var colour = apronProblem == null ? new Color(0.55f, 1f, 0.55f) : DevGui.Danger;
                loop.Clear();
                if (apronRect != null)
                {
                    foreach (var v in apronRect) loop.Add(airport.transform.TransformPoint(Geo.X0Z(v)));
                    outlines.Loop(cam, loop, colour, 3f);
                }
                else if (apronCorners.Count > 0)
                {
                    foreach (var v in apronCorners) loop.Add(airport.transform.TransformPoint(Geo.X0Z(v)));
                    loop.Add(airport.transform.TransformPoint(Geo.X0Z(apronCursor)));
                    outlines.Line(cam, loop, colour, 3f);
                    if (apronCorners.Count >= 2)
                    {
                        loop.Clear();
                        loop.Add(airport.transform.TransformPoint(Geo.X0Z(apronCursor)));
                        loop.Add(airport.transform.TransformPoint(Geo.X0Z(apronCorners[0])));
                        outlines.Line(cam, loop, colour * new Color(1f, 1f, 1f, 0.45f), 2f);
                    }
                }
            }
            if (tool == DevTool.Apron && pointing && apronRect == null) Marker(apronCursor, apronSnap != null, apronProblem != null, point);
            if ((tool == DevTool.Taxiway || tool == DevTool.Road) && pointing && lineSnapShown)
            {
                var colour = RoadProblem != null ? DevGui.Danger : new Color(1f, 0.8f, 0.1f);
                if (chainNode >= 0)
                {
                    // the new pavement's outline, so it shows where its edges will run
                    var from = airport.Taxiways.At(chainNode);
                    if (Vector2.Distance(from, lineSnap.p) > 0.5f)
                    {
                        loop.Clear();
                        foreach (var v in Strip(from, lineSnap.p, NewWidth * 0.5f)) loop.Add(airport.transform.TransformPoint(Geo.X0Z(v)));
                        outlines.Loop(cam, loop, colour, 2f);
                        loop.Clear();
                        loop.Add(airport.transform.TransformPoint(Geo.X0Z(from)));
                        loop.Add(airport.transform.TransformPoint(Geo.X0Z(lineSnap.p)));
                        outlines.Line(cam, loop, colour, 3f);
                    }
                }
                Marker(lineSnap.p, lineSnap.what != null, RoadProblem != null, point);
            }
            outlines.End();
        }

        /// <summary>A stand's front line, dashed in amber a stand's width past each side: the taxilane that will
        /// connect it.</summary>
        void FrontLine(StandLayout l)
        {
            var (m0, m1) = l.Mouth;
            float w = Vector2.Distance(m0, m1);
            var from = m0 - l.along * w;
            var to = m1 + l.along * w;
            float total = Vector2.Distance(from, to);
            int n = Mathf.Max(1, Mathf.FloorToInt(total / 8f));
            for (int i = 0; i < n; i++)
            {
                loop.Clear();
                loop.Add(airport.transform.TransformPoint(Geo.X0Z(Vector2.Lerp(from, to, i / (float)n))));
                loop.Add(airport.transform.TransformPoint(Geo.X0Z(Vector2.Lerp(from, to, (i + 0.55f) / n))));
                outlines.Line(cam, loop, DevGui.Accent, 2f);
            }
        }

        /// <summary>A ring where the next point goes: cyan where it sticks to something, white where it's free, red
        /// where it can't go.</summary>
        void Marker(Vector2 p, bool snapped, bool problem, Vector3 world)
        {
            float r = MetresPerPixel(world) * (snapped ? 9f : 6f);
            loop.Clear();
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                loop.Add(airport.transform.TransformPoint(Geo.X0Z(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r)));
            }
            var colour = problem ? DevGui.Danger : snapped ? new Color(0.3f, 0.9f, 1f) : new Color(0.92f, 0.94f, 0.97f);
            outlines.Loop(cam, loop, colour, snapped ? 3f : 2f);
        }

        /// <summary>A pick's outline on the ground (world space) into `loop`.</summary>
        bool Corners(in Pick p)
        {
            loop.Clear();
            Vector2[] c;
            switch (p.kind)
            {
                case Kind.Runway:
                    c = Strip(Geo.XZ(p.runway.start), Geo.XZ(p.runway.end), p.runway.HalfWidthTotal);
                    break;
                case Kind.Edge:
                    var net = airport.Taxiways;
                    c = Strip(net.At(p.edge.a), net.At(p.edge.b), p.edge.width * 0.5f);
                    break;
                case Kind.Depot:
                    c = p.depot.Corners;
                    break;
                case Kind.Apron:
                    c = airport.aprons[p.apron].outline.ToArray();
                    break;
                case Kind.Stand:
                    var layout = airport.Taxiways.Layout(p.stand);
                    if (layout == null) return false;
                    c = layout.Pad;
                    break;
                default:
                    return false;
            }
            foreach (var v in c) loop.Add(airport.transform.TransformPoint(Geo.X0Z(v)));
            return true;
        }

        static Vector2[] Strip(Vector2 a, Vector2 b, float half)
        {
            var d = b - a;
            var n = Geo.Right(d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up) * half;
            return new[] { a - n, b - n, b + n, a + n };
        }

        // ------------------------------------------------------------------ panel

        // rich-text colours, converted for the colour space (DevGui.Tag)
        static string Dim => DevGui.Tag(DevGui.Muted);
        static string Good => DevGui.Tag(new Color(0.49f, 0.99f, 0f));
        static string Bad => DevGui.Tag(new Color(1f, 0.5f, 0.31f));

        /// <summary>The airfield's state (top left): weather, snow, the depots and their teams, and the selection.</summary>
        void OnGUI()
        {
            if (airport == null || !DevGui.Visible) return;
            DevGui.Begin();
            if (Event.current.type == EventType.Layout) panel = new GUIContent(PanelText());
            var size = DevGui.Label.CalcSize(panel);
            float pad = DevGui.U(12f);
            var rect = new Rect(DevGui.U(12f), DevGui.U(12f), size.x + pad * 2f, size.y + pad * 1.5f);
            DevGui.Panel(rect);
            GUI.Label(new Rect(rect.x + pad, rect.y + pad * 0.75f, size.x, size.y), panel, DevGui.Label);
        }

        static string StandProblemText(StandProblem p) => p switch
        {
            StandProblem.WingspanTooBig => "wingspan too big",
            StandProblem.TooLong => "too long",
            StandProblem.NeedsPavement => "needs a paved stand",
            StandProblem.TaxiwayTooSmall => "taxiway too narrow",
            StandProblem.NotConnected => "no taxiway yet",
            StandProblem.NoSmallAircraft => "no small aircraft",
            _ => "OK",
        };

        string PanelText()
        {
            var sb = new StringBuilder();
            sb.Append($"<b>Airfield</b>   {WeatherName(airport.Condition)}, {(Airport.IsNight ? "night" : "day")}, time x{airport.timeScale:0}\n");
            var net = airport.Taxiways;
            int closed = net.edges.Count(TaxiwayNetwork.Closed);
            sb.Append($"{Dim}ground snow</color> {airport.groundSnow:0.0} cm   {Dim}taxiways closed (snow, grass, work)</color> {closed} of {net.edges.Count}");
            int waiting = airport.Maintenance.Queue.Count;
            if (waiting > 0) sb.Append($"\n{waiting} maintenance jobs waiting");
            int pushes = airport.Maintenance.Tows.Count(j => j.State == TowState.Waiting);
            if (pushes > 0) sb.Append($"\n{pushes} push-backs waiting for a tug");
            foreach (var depot in airport.Depots)
            {
                sb.Append($"\n<b>{depot.name}</b>{(depot == FleetDepot ? $" {Dim}(fleet)</color>" : "")}: {depot.snowPloughs} ploughs, {depot.mowers} mowers, " +
                          $"{depot.smallTugs} + {depot.largeTugs} large tugs");
                foreach (var t in depot.Teams)
                    sb.Append($"\n  {Dim}{t.name} ({t.size}):</color> {t.Status}");
            }
            var r = SelectedRunway;
            if (r != null)
            {
                sb.Append($"\n\n<b>Runway {r.Designation}</b>  {r.Length:N0} x {r.width:0} m, {r.surface}");
                sb.Append($"\n{Dim}lighting</color> {r.lighting}   {Dim}wear</color> {r.wear:P0}");
                var work = r.Work;
                if (work != null) sb.Append($"\n{(work.kind == WorkKind.Plough ? "ploughing" : "mowing")}: {work.Progress:P0}{(work.Entered ? "" : " (on the way)")}");
                else sb.Append($"\n{Dim}snow</color> {r.snow:0.0} cm" + (r.surface == PavementSurface.Grass ? $"   {Dim}grass</color> {r.grass:0} cm" : ""));
                sb.Append($"   {Dim}verges</color> {r.vergeGrass:0} cm{(r.VergeWork != null ? $", mowing {r.VergeWork.Progress:P0}" : "")}");
                foreach (var t in types)
                {
                    var check = r.Check(t);
                    string verdict = check.Ok ? $"{Good}OK</color>" : $"{Bad}{check}</color>";
                    sb.Append($"\n  {t.displayName}: {verdict}");
                }
            }
            var e = SelectedEdge;
            if (e != null)
            {
                sb.Append($"\n\n<b>{(e.IsRoad ? RoadName(e.road) : "Taxiway")}</b>  {net.EdgeLength(e):N0} x {e.width:0.#} m, {e.surface}{(e.lit ? ", lit" : "")}");
                if (net.LaneLimits().TryGetValue(e, out char maxCode))
                    sb.Append($"\n{Bad}code {maxCode} and smaller only</color>: stands along it are laid out for a narrower taxilane");
                sb.Append($"\n{Dim}wear</color> {e.wear:P0}   {Dim}snow</color> {e.snow:0.0} cm" +
                          (e.surface == PavementSurface.Grass ? $"   {Dim}grass</color> {e.grass:0} cm" : "") +
                          (e.HasVerges ? $"   {Dim}verges</color> {e.vergeGrass:0} cm" : "") + (TaxiwayNetwork.Closed(e) ? $"   {Bad}closed</color>" : ""));
            }
            if (ApronSelected)
            {
                var a = airport.aprons[selection.apron];
                sb.Append($"\n\n<b>Apron</b>  {a.Area:N0} m², {a.outline.Count} corners, {a.surface}");
            }
            var st = SelectedStand;
            var sl = st != null ? net.Layout(st) : null;
            if (sl != null)
            {
                char c = st.Code;
                sb.Append($"\n\n<b>{st.Name}</b>  code {c}: wingspan up to {RunwayRules.MaxWingspan(c):0} m, length {RunwayRules.StandLength(c):0} m");
                sb.Append($"\n{Dim}pad</color> {sl.x1 - sl.x0:0} x {sl.far - Mathf.Min(sl.near0, sl.near1):0} m, {st.surface}");
                if (!st.DeIcing)
                    sb.Append($"   {Dim}angle</color> {Vector2.Angle(sl.along, sl.axis):0} deg   " +
                              (st.exit == StandExit.PushBack ? "push-back" : "taxi-out") + (st.noSmallAircraft ? ", no small aircraft" : ""));
                sb.Append(sl.edge != null
                    ? $"\n{Dim}opens onto a</color> {sl.edge.width:0.#} m taxiway (code {RunwayRules.TaxiwayCode(sl.edge.width)})"
                    : $"\n{Bad}not connected</color>: lay a taxiway along its front (the dashed line)");
                char laneCode = RunwayRules.TaxiwayCode(st.Lane);
                if (!st.DeIcing && sl.edge != null && laneCode < RunwayRules.TaxiwayCode(sl.edge.width))
                    sb.Append($"\n{Bad}laid out for a code {laneCode} taxilane</color>: bigger aircraft can't taxi past it");
                var access = net.RoadAccess(sl);
                sb.Append(access.Count == 0 ? $"\n{Dim}service road</color> none: vehicles come along the taxiway"
                    : $"\n{Dim}service road</color> at its {(access[0].side == 0 ? "head" : "side")}" +
                      (access.Count > 1 ? $" (+{access.Count - 1} more)" : ""));
                foreach (var t in types)
                {
                    var problem = net.CheckStand(st, t);
                    string verdict = problem == StandProblem.None ? $"{Good}OK</color>" : $"{Bad}{StandProblemText(problem)}</color>";
                    sb.Append($"\n  {t.displayName}: {verdict}");
                }
            }
            return sb.ToString();
        }
    }
}
