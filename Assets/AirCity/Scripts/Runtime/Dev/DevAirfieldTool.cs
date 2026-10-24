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
    public enum DevTool { Select, Runway, Taxiway, Road, Depot, Delete }

    /// <summary>
    /// Sandbox airfield building (development only, not game UI). DevToolbar has buttons for all of it; the keys:
    ///   B = next build tool (select / runway / taxiway / road / depot)   X = delete tool
    ///   Esc = end the line being laid, else back to select, else deselect
    ///   select: click a runway, taxiway or road section, depot or apron to select it (outlined in amber; the panel
    ///     lists which aircraft can use a selected runway); Delete / Backspace removes it (DevToolbar)
    ///   delete: click anything to remove it (outlined in red first); Shift+click takes a taxiway's or road's whole
    ///     run; aircraft too (DevSandboxControls)
    ///   runway: left-drag from threshold to threshold (snaps to 10 deg and 50 m; hold Alt for free)
    ///   taxiway / road: click points to lay a taxiway or a service road (snaps to nodes, taxiways, roads and
    ///     runway centrelines, and for roads to the front of a depot); right-click or Esc ends the line
    ///   depot: drag from where the doors go out the way they face (snaps to 10 deg) to build a depot; click one to
    ///     select it
    ///   V = surface   [ / ] = width   L = lighting (selected runway; all taxiways with the taxiway tool)
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
        public VehicleType ploughType, mowerType, depotType;
        public GameObject ploughPrefab, mowerPrefab, depotPrefab;

        /// <summary>True while a tool other than select is on: other sandbox tools leave the left mouse button alone.</summary>
        public static bool Active { get; private set; }
        /// <summary>The delete tool is on: a click on an aircraft removes it (DevSandboxControls).</summary>
        public static bool Deleting { get; private set; }

        public static readonly float[] TimeScales = { 1f, 5f, 20f, 60f };

        enum Kind { None, Runway, Edge, Depot, Apron }

        /// <summary>Something on the airfield to select or delete.</summary>
        struct Pick
        {
            public Kind kind;
            public Runway runway;
            public TaxiwayEdge edge;
            public MaintenanceDepot depot;
            public int apron;

            public static Pick None => new Pick { apron = -1 };
            public bool Same(in Pick o) => kind == o.kind && runway == o.runway && edge == o.edge && depot == o.depot && apron == o.apron;
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
        }

        /// <summary>The surface for new pavement; with a runway selected (select or runway tool), that runway's too.</summary>
        public void SetSurface(PavementSurface s)
        {
            if (tool == DevTool.Road && s == PavementSurface.Grass) s = PavementSurface.Dirt;
            surface = s;
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
            Active = Deleting = false;
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
                case DevTool.Taxiway: TaxiwayTool(click, point, false); break;
                case DevTool.Road: TaxiwayTool(click, point, true); break;
                case DevTool.Depot: DepotTool(mouse, click, point); break;
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
            if (keys.bKey.wasPressedThisFrame) SetTool(tool >= DevTool.Depot ? DevTool.Select : tool + 1);
            if (keys.xKey.wasPressedThisFrame) SetTool(tool == DevTool.Delete ? DevTool.Select : DevTool.Delete);
            if (keys.escapeKey.wasPressedThisFrame)
            {
                if (chainNode >= 0) chainNode = -1;
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
            if (!keys.altKey.isPressed && length > 1f)
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

        void TaxiwayTool(bool click, Vector3 point, bool road)
        {
            if (!click) return;
            var net = airport.Taxiways;
            var local = airport.transform.InverseTransformPoint(point);
            int node = SnapNode(net, local, road);
            if (chainNode >= 0 && chainNode != node)
            {
                if (road)
                    net.Connect(chainNode, node, RunwayRules.RoadWidth, surface == PavementSurface.Grass ? PavementSurface.Asphalt : surface,
                        false, true);
                else
                    net.Connect(chainNode, node, RunwayRules.TaxiwayWidths[taxiwayWidth], surface,
                        net.edges.Any(e => e.lit) && surface.IsPaved());
                net.Rebuild();
            }
            chainNode = node;
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
                float bearing = Mathf.Round(Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg / 10f) * 10f * Mathf.Deg2Rad;
                var template = airport.Depot;
                var depot = airport.AddDepot(new Vector2(dragStart.x, dragStart.z), new Vector2(Mathf.Sin(bearing), Mathf.Cos(bearing)), template);
                if (template == null)
                {
                    depot.SetAssets(ploughType, mowerType, depotType, ploughPrefab, mowerPrefab, depotPrefab);
                    depot.gameObject.SetActive(false);
                    depot.gameObject.SetActive(true);
                }
                Select(new Pick { kind = Kind.Depot, depot = depot, apron = -1 });
            }
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

        int SnapNode(TaxiwayNetwork net, Vector3 p, bool road)
        {
            if (road)
                foreach (var d in airport.Depots)
                {
                    // the middle of the doors' front, 12 m out: vehicles drive straight out onto the road
                    var front = d.Front + d.Outward * 12f;
                    if (Vector2.Distance(front, new Vector2(p.x, p.z)) < 20f)
                    {
                        p = new Vector3(front.x, 0f, front.y);
                        break;
                    }
                }
            var near = net.NearestNode(p, 15f);
            if (near != null) return near.id;
            if (net.TryNearestEdge(p, 8f, out var edge, out var onEdge)) return net.SplitEdge(edge, onEdge);
            foreach (var r in airport.Runways)
                if (r.Contains(p, 5f))
                    return net.AddNode(p, r);
            return net.AddNode(p);
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
            if (airport.TryPickApron(p, out int i)) return new Pick { kind = Kind.Apron, apron = i };
            return Pick.None;
        }

        bool Valid(in Pick p) => p.kind switch
        {
            Kind.Runway => p.runway != null,
            Kind.Edge => airport.Taxiways.edges.Contains(p.edge),
            Kind.Depot => p.depot != null,
            Kind.Apron => p.apron >= 0 && p.apron < airport.aprons.Count,
            _ => true,
        };

        string Describe(in Pick p)
        {
            switch (p.kind)
            {
                case Kind.Runway: return "Runway " + p.runway.Designation;
                case Kind.Edge: return p.edge.road ? "Service road" : "Taxiway";
                case Kind.Depot: return p.depot.name;
                case Kind.Apron: return "Apron";
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
            }
            // removing an apron renumbers the ones after it
            if (selection.Same(p) || (p.kind == Kind.Apron && selection.kind == Kind.Apron)) selection = Pick.None;
            hover = Pick.None;
        }

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
            if ((tool == DevTool.Taxiway || tool == DevTool.Road) && chainNode >= 0 && pointing)
            {
                loop.Clear();
                loop.Add(airport.transform.TransformPoint(airport.Taxiways.Node(chainNode).position));
                loop.Add(point);
                outlines.Line(cam, loop, new Color(1f, 0.8f, 0.1f), 4f);
            }
            outlines.End();
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
                    c = Airport.Corners(airport.aprons[p.apron]);
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

        string PanelText()
        {
            var sb = new StringBuilder();
            sb.Append($"<b>Airfield</b>   {WeatherName(airport.Condition)}, {(Airport.IsNight ? "night" : "day")}, time x{airport.timeScale:0}\n");
            var net = airport.Taxiways;
            int closed = net.edges.Count(TaxiwayNetwork.Closed);
            sb.Append($"{Dim}ground snow</color> {airport.groundSnow:0.0} cm   {Dim}taxiways closed (snow, grass, work)</color> {closed} of {net.edges.Count}");
            int waiting = airport.Maintenance.Queue.Count;
            if (waiting > 0) sb.Append($"\n{waiting} maintenance jobs waiting");
            foreach (var depot in airport.Depots)
            {
                sb.Append($"\n<b>{depot.name}</b>{(depot == FleetDepot ? $" {Dim}(fleet)</color>" : "")}: {depot.snowPloughs} ploughs, {depot.mowers} mowers");
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
                sb.Append($"\n\n<b>{(e.road ? "Service road" : "Taxiway")}</b>  {net.EdgeLength(e):N0} x {e.width:0.#} m, {e.surface}{(e.lit ? ", lit" : "")}");
                sb.Append($"\n{Dim}wear</color> {e.wear:P0}   {Dim}snow</color> {e.snow:0.0} cm" +
                          (e.surface == PavementSurface.Grass ? $"   {Dim}grass</color> {e.grass:0} cm" : "") +
                          (e.HasVerges ? $"   {Dim}verges</color> {e.vergeGrass:0} cm" : "") + (TaxiwayNetwork.Closed(e) ? $"   {Bad}closed</color>" : ""));
            }
            if (ApronSelected)
            {
                var a = airport.aprons[selection.apron];
                sb.Append($"\n\n<b>Apron</b>  {a.size.x:0} x {a.size.y:0} m, {a.surface}");
            }
            return sb.ToString();
        }
    }
}
