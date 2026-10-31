using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Traffic;
using AirCity.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// The sandbox HUD's buttons (development only, not game UI), next to the camera's DevAirfieldTool and
    ///   DevSandboxControls, whose keys still work:
    ///   bottom: the tools (select, runway, taxiway, road, apron, terminal, depot, stand, de-icing bay, bridge | delete)
    ///   top right: the inspector, a column of labelled rows for the tool on (surface, width, lights; a stand's code,
    ///     angle and exit; a terminal's floors) or what's selected (its settings, maintenance, a stand's jet bridges,
    ///     and Delete), with its hints wrapped at the bottom. Settings with many options (surface, a stand's angle, its
    ///     bridges) are a button that pops their options out to the left; a press elsewhere, Esc or the button again
    ///     closes it (the bridges one stays while the world is clicked: bridges are picked and dragged there)
    ///   under it (or beside it, if it won't fit): weather, time speed, day / night, snowfall and grass; maintenance;
    ///     traffic (landings, auto traffic, the wind); aircraft lights and engines, each section folded by its caption
    ///   Delete / Backspace removes the selection (the aircraft or the airfield one, whichever came last)
    ///   H hides the HUD
    /// Clicks and scrolls over the HUD don't reach the world (DevGui.PointerOverHud).
    /// </summary>
    public class DevToolbar : MonoBehaviour
    {
        DevAirfieldTool airfield;
        DevSandboxControls sandbox;
        DevTrafficControls traffic;

        static readonly (DevTool tool, string label, string tip, string hint)[] Tools =
        {
            (DevTool.Select, "Select", "Select runways, taxiways, roads, depots, stands, aprons and aircraft", "Esc"),
            (DevTool.Runway, "Runway", "Drag from threshold to threshold (B cycles the build tools)", null),
            (DevTool.Taxiway, "Taxiway", "Click points to lay a taxiway; right-click ends it", null),
            (DevTool.Road, "Road", "Click points to lay an airside service road (across a paved apron it is just paint); right-click ends it", null),
            (DevTool.Apron, "Apron", "Click corners to lay an apron of any shape, or drag out a rectangle", null),
            (DevTool.Terminal, "Terminal", "Click corners for a terminal building (or drag out a rectangle); select one and Shift+scroll to curve a wall", null),
            (DevTool.Depot, "Depot", "Drag from the doors outwards to build a maintenance depot", null),
            (DevTool.Stand, "Stand", "Click beside a taxiway to add an aircraft stand (it snaps to the stands beside it)", null),
            (DevTool.DeIcing, "De-ice", "Click on a taxiway to add a drive-through de-icing bay", null),
            (DevTool.Bridge, "Bridge", "Place jet bridges and bridge heads by hand, starting from a terminal wall", null),
        };

        static readonly string[] CodeTips =
        {
            "Code A: wingspan under 15 m (Cessna 172)", "Code B: under 24 m (CRJ200, Beech 1900)", "Code C: under 36 m (A320, 737)",
            "Code D: under 52 m (767)", "Code E: under 65 m (777, A350)", "Code F: under 80 m (A380, 747-8)",
        };

        static readonly string[] SurfaceTips =
        {
            "Dirt: unpaved, goes soft in the rain", "Asphalt: paved", "Concrete: paved, wears slowest", "Grass: unpaved, grows and needs mowing",
        };

        void Start()
        {
            airfield = GetComponent<DevAirfieldTool>();
            sandbox = GetComponent<DevSandboxControls>();
            traffic = GetComponent<DevTrafficControls>();
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.hKey.wasPressedThisFrame) DevGui.Visible = !DevGui.Visible;
            if (keys.escapeKey.wasPressedThisFrame) openPop = null;
            if (keys.deleteKey.wasPressedThisFrame || keys.backspaceKey.wasPressedThisFrame) DeleteSelection();
        }

        /// <summary>Remove the aircraft or the airfield selection, whichever was made last.</summary>
        void DeleteSelection()
        {
            bool aircraftFirst = sandbox != null && sandbox.Selected != null &&
                                 (airfield == null || !airfield.HasSelection || sandbox.SelectedFrame >= airfield.SelectedFrame);
            if (aircraftFirst) sandbox.DeleteSelected();
            else if (airfield != null) airfield.DeleteSelection();
        }

        void OnGUI()
        {
            if (!DevGui.Visible) return;
            DevGui.Begin();
            ToolBar();
            RightColumn();
            DevGui.Tooltip();
        }

        static float U(float units) => DevGui.U(units);

        static bool Shifted => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        // ------------------------------------------------------------------ tools (bottom)

        /// <summary>The tool buttons, bottom centre (narrower buttons on a narrow screen).</summary>
        void ToolBar()
        {
            if (airfield == null || airfield.airport == null) return;
            float w = U(72f), h = U(42f), gap = U(4f), pad = U(7f), split = U(14f);
            float room = Screen.width - U(24f) - pad * 2f - Tools.Length * gap - split;
            w = Mathf.Max(U(46f), Mathf.Min(w, Mathf.Floor(room / (Tools.Length + 1))));
            float width = pad * 2f + Tools.Length * w + Tools.Length * gap + split + w;
            var r = new Rect(Mathf.Round((Screen.width - width) * 0.5f), Screen.height - h - pad * 2f - U(14f), width, h + pad * 2f);
            DevGui.Panel(r);
            float x = r.x + pad, y = r.y + pad;
            foreach (var (tool, label, tip, hint) in Tools)
            {
                if (DevGui.Button(new Rect(x, y, w, h), label, tip, airfield.Tool == tool, hint: hint)) airfield.SetTool(tool);
                x += w + gap;
            }
            DevGui.Divider(x + split * 0.5f - gap * 0.5f, y + U(6f), h - U(12f));
            x += split;
            bool deleting = airfield.Tool == DevTool.Delete;
            if (DevGui.Button(new Rect(x, y, w, h), "Delete", "Click anything to remove it; Shift+click: a taxiway's or road's whole run",
                    deleting, danger: true, hint: "X"))
                airfield.SetTool(deleting ? DevTool.Select : DevTool.Delete);
        }

        // ------------------------------------------------------------------ rows (the inspector, its pop-outs, the sidebar)

        /// <summary>A button in a row: its label, what it does, its tip and key, and how it looks.</summary>
        readonly struct Btn
        {
            public readonly string label, tip, hint;
            public readonly Action act;
            public readonly bool on, enabled, danger;

            public Btn(string label, Action act, string tip, bool on, string hint, bool enabled, bool danger)
            {
                this.label = label;
                this.act = act;
                this.tip = tip;
                this.on = on;
                this.hint = hint;
                this.enabled = enabled;
                this.danger = danger;
            }
        }

        static Btn B(string label, Action act, string tip = null, bool on = false, string hint = null, bool enabled = true, bool danger = false) =>
            new Btn(label, act, tip, on, hint, enabled, danger);

        /// <summary>Rows are built afresh each OnGUI call into one of these lists, measured, then drawn.</summary>
        readonly List<(float h, Action<Rect> draw)> main = new List<(float, Action<Rect>)>(), pop = new List<(float, Action<Rect>)>(),
            side = new List<(float, Action<Rect>)>();
        List<(float h, Action<Rect> draw)> into;
        /// <summary>The inner width of the panel rows are being built for (wrapped notes need it).</summary>
        float innerWidth;

        const float ColumnUnits = 260f, PopUnits = 236f;
        static float Pad => U(10f);
        static float RowH => U(26f);
        static float RowGap => U(4f);
        static float LabelW => U(70f);

        void Add(float h, Action<Rect> draw) => into.Add((h, draw));

        void Title(string text) => Add(U(22f), r => GUI.Label(r, text, DevGui.Title));

        /// <summary>A small caption, with some space above it.</summary>
        void Section(string caption) => Add(U(24f), r => GUI.Label(new Rect(r.x, r.y + U(8f), r.width, r.height - U(8f)), caption, DevGui.Caption));

        void Space(float units = 4f) => Add(U(units), _ => { });

        /// <summary>Equal buttons across the row.</summary>
        void Buttons(params Btn[] bs) => Add(RowH, r => DrawButtons(r, bs));

        static void DrawButtons(Rect r, Btn[] bs)
        {
            float gap = RowGap, bw = (r.width - gap * (bs.Length - 1)) / bs.Length;
            for (int i = 0; i < bs.Length; i++)
            {
                var b = bs[i];
                if (DevGui.Button(new Rect(Mathf.Round(r.x + i * (bw + gap)), r.y, Mathf.Round(bw), r.height), b.label, b.tip, b.on, b.danger,
                        b.enabled, b.hint))
                    b.act();
            }
        }

        /// <summary>A labelled row: the label on the left, `control` in the rest.</summary>
        void Prop(string label, Action<Rect> control)
        {
            Add(RowH, r =>
            {
                GUI.Label(new Rect(r.x, r.y, LabelW, r.height), label, DevGui.Small);
                control(new Rect(r.x + LabelW, r.y, r.width - LabelW, r.height));
            });
        }

        void PropButtons(string label, params Btn[] bs) => Prop(label, r => DrawButtons(r, bs));

        void PropText(string label, string text) => Prop(label, r => GUI.Label(r, text, DevGui.Label));

        /// <summary>[-] value [+]</summary>
        void PropStepper(string label, string value, Action down, Action up, string tip, bool canDown = true, bool canUp = true)
        {
            Prop(label, r =>
            {
                float bw = U(30f);
                if (DevGui.Button(new Rect(r.x, r.y, bw, r.height), "-", tip, enabled: canDown)) down();
                GUI.Label(new Rect(r.x + bw, r.y, r.width - bw * 2f, r.height), value, Centred);
                if (DevGui.Button(new Rect(r.xMax - bw, r.y, bw, r.height), "+", tip, enabled: canUp)) up();
            });
        }

        /// <summary>
        /// A labelled row whose control is a button showing `value`: it opens (or closes) the pop-out `key` beside
        /// the inspector, which `fill` builds.
        /// </summary>
        void PropPop(string label, string value, string key, string tip, Action fill, bool enabled = true)
        {
            bool open = openPop == key;
            if (open)
            {
                popRow = into.Count;
                popFill = fill;
                popTitle = label.ToUpperInvariant();
            }
            Prop(label, r =>
            {
                if (DevGui.Button(r, value, tip, open, enabled: enabled, hint: open ? "<" : ">")) Toggle(key);
            });
        }

        /// <summary>Wrapped hint text (rich text; three spaces start a new line).</summary>
        void Note(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var content = new GUIContent(text.Replace("   ", "\n"));
            float h = Wrapped.CalcHeight(content, innerWidth);
            Add(h, r => GUI.Label(r, content, Wrapped));
        }

        void Danger(string text) => Note(text != null ? $"{DevGui.Tag(DevGui.Danger)}{text}</color>" : null);

        static float Height(List<(float h, Action<Rect> draw)> rows)
        {
            if (rows.Count == 0) return 0f;
            float h = Pad * 2f - RowGap;
            foreach (var (rh, _) in rows) h += rh + RowGap;
            return h;
        }

        static void Draw(List<(float h, Action<Rect> draw)> rows, Rect panel)
        {
            DevGui.Panel(panel);
            float y = panel.y + Pad;
            foreach (var (h, draw) in rows)
            {
                draw(new Rect(panel.x + Pad, y, panel.width - Pad * 2f, h));
                y += h + RowGap;
            }
        }

        /// <summary>Where a row starts (from the panel's top).</summary>
        static float RowTop(List<(float h, Action<Rect> draw)> rows, int index)
        {
            float y = Pad;
            for (int i = 0; i < index && i < rows.Count; i++) y += rows[i].h + RowGap;
            return y;
        }

        static GUIStyle centred, wrapped;
        static float stylesScale;

        static void Styles()
        {
            if (centred != null && Mathf.Approximately(stylesScale, DevGui.Scale)) return;
            centred = new GUIStyle(DevGui.Bold) { alignment = TextAnchor.MiddleCenter };
            wrapped = new GUIStyle(DevGui.Hint) { wordWrap = true, clipping = TextClipping.Clip };
            stylesScale = DevGui.Scale;
        }

        static GUIStyle Centred
        {
            get
            {
                Styles();
                return centred;
            }
        }

        static GUIStyle Wrapped
        {
            get
            {
                Styles();
                return wrapped;
            }
        }

        // ------------------------------------------------------------------ the right column: inspector, pop-out, sidebar

        /// <summary>The open pop-out's key (null: none), what the inspector showed when it opened (it closes when that
        /// changes), and, while building, which row opened it, its builder and caption.</summary>
        string openPop;
        object popSubject;
        int popRow = -1;
        Action popFill;
        string popTitle;
        /// <summary>Where the pop-out and the row that opened it were last drawn (a press elsewhere closes it).</summary>
        Rect popRect, popAnchor;
        /// <summary>The bridge picked last seen: picking one opens the stand's bridges pop-out.</summary>
        JetBridge lastBridge;

        /// <summary>Pop-outs that stay open while the world is clicked (picking and dragging bridges needs it).</summary>
        const string BridgesPop = "bridges";

        void Toggle(string key)
        {
            openPop = openPop == key ? null : key;
            popSubject = Subject();
        }

        /// <summary>What the inspector is showing: the tool on, the aircraft selected, or the airfield selection.</summary>
        object Subject()
        {
            if (airfield == null || airfield.airport == null) return sandbox != null ? sandbox.Selected : null;
            if (airfield.Tool != DevTool.Select) return airfield.Tool;
            if (AircraftShown) return sandbox.Selected;
            return (object)airfield.SelectedStand ?? (object)airfield.SelectedRunway ?? (object)airfield.SelectedEdge ??
                   (object)airfield.SelectedDepot ?? (object)airfield.SelectedTerminal ?? airfield.SelectedApron;
        }

        bool AircraftShown => sandbox != null && sandbox.Selected != null &&
                              (airfield == null || !airfield.HasSelection || sandbox.SelectedFrame >= airfield.SelectedFrame);

        /// <summary>
        /// The right column: the inspector (top right: the tool's settings or the selection's), any pop-out it opened
        /// to its left, and the sidebar under it (beside it, left, if it won't fit under). Built, measured, then
        /// drawn; the pop-out takes clicks first (drawn first) but shows on top (painted last).
        /// </summary>
        void RightColumn()
        {
            var e = Event.current;
            // a press anywhere but the pop-out (or its button) closes it, but for the bridges one
            if (e.type == EventType.MouseDown && openPop != null && openPop != BridgesPop &&
                !popRect.Contains(e.mousePosition) && !popAnchor.Contains(e.mousePosition))
                openPop = null;
            if (openPop != null && !Equals(Subject(), popSubject)) openPop = null;

            float margin = U(12f), colW = U(ColumnUnits), popW = U(PopUnits);
            main.Clear();
            pop.Clear();
            side.Clear();
            popRow = -1;
            popFill = null;
            into = main;
            innerWidth = colW - Pad * 2f;
            if (airfield != null && airfield.airport != null) Inspector();
            else if (AircraftShown) AircraftInspector();
            if (popRow < 0) openPop = null;
            else
            {
                into = pop;
                innerWidth = popW - Pad * 2f;
                Section(popTitle);
                popFill();
            }
            into = side;
            innerWidth = colW - Pad * 2f;
            Sidebar();

            float x = Screen.width - margin - colW;
            var inspector = main.Count > 0 ? new Rect(x, margin, colW, Height(main)) : new Rect(x, margin - U(8f), colW, 0f);
            float sideH = Height(side);
            var sideRect = new Rect(x, inspector.yMax + U(8f), colW, sideH);
            if (main.Count > 0 && sideRect.yMax > Screen.height - margin) sideRect = new Rect(x - colW - U(8f), margin, colW, sideH);

            popAnchor = popRow >= 0 ? new Rect(inspector.x, inspector.y + RowTop(main, popRow), colW, main[popRow].h) : default;
            popRect = default;
            if (pop.Count > 0)
            {
                float h = Height(pop);
                float y = Mathf.Clamp(popAnchor.y - Pad - U(24f) - RowGap, margin, Mathf.Max(margin, Screen.height - margin - h));
                popRect = new Rect(inspector.x - U(8f) - popW, y, popW, h);
            }
            bool repaint = e.type == EventType.Repaint;
            if (!repaint && pop.Count > 0) Draw(pop, popRect);
            if (main.Count > 0) Draw(main, inspector);
            if (side.Count > 0) Draw(side, sideRect);
            if (repaint && pop.Count > 0) Draw(pop, popRect);
        }

        // ------------------------------------------------------------------ inspector

        void Inspector()
        {
            switch (airfield.Tool)
            {
                case DevTool.Runway:
                    Title("Runway tool");
                    SurfaceProp(airfield.Surface);
                    RunwayWidth(airfield.NewWidth);
                    Note("drag from threshold to threshold   Shift: 10° and 50 m steps");
                    return;
                case DevTool.Taxiway:
                    Title("Taxiway tool");
                    SurfaceProp(airfield.Surface);
                    float w = airfield.NewWidth;
                    PropStepper("Width", $"{w:0.#} m", () => airfield.StepWidth(-1), () => airfield.StepWidth(1), "Width ([ / ])",
                        w > RunwayRules.TaxiwayWidths[0], w < RunwayRules.TaxiwayWidths[RunwayRules.TaxiwayWidths.Length - 1]);
                    PropButtons("Lights", B(airfield.TaxiwaysLit ? "On" : "Off", airfield.ToggleTaxiwayLights,
                        "Blue edge lights on every paved taxiway", airfield.TaxiwaysLit, "L"));
                    if (airfield.RoadProblem != null) Danger(airfield.RoadProblem);
                    else Note(Joins("click points   Shift: 10° and 5 m steps   right-click ends   dashed: stands' front lines"));
                    return;
                case DevTool.Road:
                    Title("Road tool");
                    SurfaceProp(airfield.Surface, false);
                    if (airfield.RoadProblem != null) Danger(airfield.RoadProblem);
                    else
                        Note(Joins($"service road, {RunwayRules.RoadWidth:0} m wide   click points   Shift: 10° and 5 m steps   " +
                                   "right-click ends   paint only across paved aprons"));
                    return;
                case DevTool.Apron:
                    Title("Apron tool");
                    SurfaceProp(airfield.Surface);
                    if (airfield.ApronProblemText != null) Danger(airfield.ApronProblemText);
                    else
                        Note(Joins(airfield.ApronCornerCount == 0 ? "click corners, or drag out a rectangle   Shift: 10° and 5 m steps"
                            : "click the first corner or right-click to finish   Shift: 10° and 5 m steps   Esc drops it"));
                    return;
                case DevTool.Terminal:
                    Title("Terminal tool");
                    Floors(airfield.TerminalFloors);
                    if (airfield.ApronProblemText != null) Danger(airfield.ApronProblemText);
                    else
                        Note(Joins(airfield.ApronCornerCount == 0 ? "click corners, or drag out a rectangle   click a terminal to select it"
                            : "click the first corner or right-click to finish   Shift: 10° and 5 m steps   Esc drops it"));
                    return;
                case DevTool.Depot:
                    Title("Depot tool");
                    Note("drag from the doors outwards   click a depot to select it");
                    if (airfield.FleetDepot != null)
                    {
                        Section("FLEET  ·  " + airfield.FleetDepot.name.ToUpperInvariant());
                        Fleet(airfield.FleetDepot);
                    }
                    return;
                case DevTool.Stand:
                case DevTool.DeIcing:
                {
                    bool bay = airfield.Tool == DevTool.DeIcing;
                    Title(bay ? "De-icing bay tool" : "Stand tool");
                    StandProps(bay, airfield.Surface);
                    if (airfield.GhostProblem != null) Danger(airfield.GhostProblem);
                    else if (airfield.GhostNote != null) Note($"{DevGui.Tag(Cyan)}{airfield.GhostNote}</color>");
                    Note(bay ? "click on a taxiway or anywhere   Shift+scroll: turn it"
                        : "click anywhere   Shift+scroll: turn it   Ctrl (Cmd): free, no snapping   Ctrl+drag: turn it to any angle");
                    return;
                }
                case DevTool.Bridge:
                    BridgeTool();
                    return;
                case DevTool.Delete:
                {
                    Title("Delete tool");
                    var under = airfield.HoverName;
                    if (under == null && sandbox != null && sandbox.Hovered != null) under = sandbox.Hovered.name;
                    Note(under != null ? $"{DevGui.Tag(DevGui.Danger)}click to remove</color> {under}"
                        : "click a runway, taxiway, road, depot, stand, apron or aircraft to remove it   Shift: a taxiway's whole run");
                    return;
                }
            }
            // select: what can be done to the selection (nothing selected: no inspector)
            if (AircraftShown)
            {
                AircraftInspector();
                return;
            }
            if (airfield.SelectedRunway != null) RunwayInspector(airfield.SelectedRunway);
            else if (airfield.SelectedEdge != null)
            {
                var edge = airfield.SelectedEdge;
                Title(airfield.SelectionName);
                Section("MAINTENANCE");
                Maintenance(edge.surface == PavementSurface.Grass, edge.HasVerges);
                Space();
                Buttons(B("Delete", () => airfield.DeleteSelection(), "Remove this section (between junctions)", hint: "Del", danger: true),
                    B("Delete run", airfield.DeleteSelectedRun, "Remove it and the sections running on from it, as far as it goes", danger: true));
            }
            else if (airfield.SelectedDepot != null)
            {
                var depot = airfield.SelectedDepot;
                Title(depot.name);
                Section("FLEET");
                Fleet(depot);
                Space();
                Buttons(B("Delete depot", () => airfield.DeleteSelection(), "Remove the depot and its vehicles", hint: "Del", danger: true));
            }
            else if (airfield.ApronSelected)
            {
                Title(airfield.SelectionName);
                SurfaceProp(airfield.SelectedApron.surface);
                Space();
                Buttons(B("Delete apron", () => airfield.DeleteSelection(), "Remove the apron", hint: "Del", danger: true));
                Danger(airfield.ApronProblemText);
            }
            else if (airfield.SelectedTerminal != null)
            {
                Title(airfield.SelectionName);
                Floors(airfield.TerminalFloors);
                Space();
                Buttons(B("Delete terminal", () => airfield.DeleteSelection(), "Remove the terminal", hint: "Del", danger: true));
                if (airfield.ApronProblemText != null) Danger(airfield.ApronProblemText);
                else Note("Shift+scroll: bow the outlined wall out or in (Alt: finer)");
            }
            else if (airfield.SelectedStand != null) StandInspector(airfield.SelectedStand);
        }

        static readonly Color Cyan = new Color(0.3f, 0.9f, 1f), Green = new Color(0.55f, 1f, 0.55f);

        /// <summary>The hint, or what the next point sticks to (cyan, like its ring on the ground).</summary>
        string Joins(string hint)
        {
            var what = airfield.SnapText;
            return what == null ? hint : $"{DevGui.Tag(Cyan)}joins {what}</color>   {hint}";
        }

        void RunwayInspector(Runway runway)
        {
            Title(airfield.SelectionName);
            SurfaceProp(runway.surface);
            RunwayWidth(runway.width);
            PropButtons("Lights", B(runway.lighting.ToString(), airfield.ToggleLighting,
                "None / basic / precision lighting (unlit runways close at night)", runway.lighting != RunwayLighting.None, "L", runway.Paved));
            PropText("In use", airfield.airport.Traffic.InUse(runway).ToString());
            PropButtons("Condition", B("Wear", airfield.Wear, "Wear it by a quarter", hint: "K"),
                B("Repair", airfield.Repair, "Repair it (closes it while the work goes on)", hint: "J"));
            Section("MAINTENANCE");
            Maintenance(runway.surface == PavementSurface.Grass, true);
            Space();
            Buttons(B("Delete runway", () => airfield.DeleteSelection(), "Remove the runway", hint: "Del", danger: true));
        }

        /// <summary>The surface for new pavement or the selection, as a button that pops out the choices.</summary>
        void SurfaceProp(PavementSurface current, bool grass = true)
        {
            PropPop("Surface", current.ToString(), "surface", "The surface (V)", () =>
            {
                for (int i = 0; i < Surfaces.Count; i++)
                {
                    var s = (PavementSurface)i;
                    if (s == PavementSurface.Grass && !grass) continue;
                    bool ok = true;
                    string tip = SurfaceTips[i];
                    bool stand = airfield.Tool == DevTool.Stand || airfield.Tool == DevTool.DeIcing || airfield.SelectedStand != null;
                    if (stand)
                    {
                        bool bay = airfield.SelectedStand != null ? airfield.SelectedStand.DeIcing : airfield.Tool == DevTool.DeIcing;
                        if (bay && !s.IsPaved()) continue;
                        ok = RunwayRules.StandSurfaceOk(airfield.StandCode.Letter(), s) || airfield.SelectedStand == null;
                        if (!RunwayRules.StandSurfaceOk(airfield.StandCode.Letter(), s)) tip += " (codes A and B only)";
                    }
                    Buttons(B(s.ToString(), () =>
                    {
                        airfield.SetSurface(s);
                        openPop = null;
                    }, tip, current == s, enabled: ok));
                }
            });
        }

        void Floors(int floors) =>
            PropStepper("Floors", floors.ToString(), () => airfield.StepFloors(-1), () => airfield.StepFloors(1), "Floors ([ / ])",
                floors > 1, floors < Terminal.MaxFloors);

        void RunwayWidth(float width) =>
            PropStepper("Width", $"{width:0} m", () => airfield.StepWidth(-1), () => airfield.StepWidth(1), "Width ([ / ])",
                width > RunwayRules.RunwayWidths[0], width < RunwayRules.RunwayWidths[RunwayRules.RunwayWidths.Length - 1]);

        // ------------------------------------------------------------------ stands and bridges

        /// <summary>Size code, surface, and for parking stands the angle (a pop-out), exit and small aircraft: for new
        /// stands, or the selected one.</summary>
        void StandProps(bool deIcing, PavementSurface surface)
        {
            var codes = new Btn[SizeCodes.Count];
            for (int i = 0; i < codes.Length; i++)
            {
                var c = (SizeCode)i;
                codes[i] = B(c.Letter().ToString(), () => airfield.SetStandCode(c), CodeTips[i] + " ([ / ])", airfield.StandCode == c);
            }
            PropButtons("Code", codes);
            SurfaceProp(surface);
            if (deIcing)
            {
                PropButtons("Direction", B("Turn round", airfield.FlipStand, "Aircraft taxi through it the other way", hint: "Shift+Z"));
                return;
            }
            float angle = airfield.StandAngle;
            bool leans = airfield.StandLeans;
            PropPop("Angle", $"{angle:0.#}°{(leans ? "  leaning" : "")}", "angle", "Its angle to the taxiway: presets, fine turns and lean", () =>
            {
                float lean = leans ? 180f - angle : angle;
                var presets = new Btn[DevAirfieldTool.StandAngles.Length];
                for (int i = 0; i < presets.Length; i++)
                {
                    float preset = DevAirfieldTool.StandAngles[i];
                    presets[i] = B($"{preset:0}°", () => airfield.SetStandPreset(preset), "Angle to the taxiway, keeping its lean (Z)",
                        Mathf.Abs(lean - preset) < 0.5f);
                }
                Buttons(presets);
                bool down = angle > DevAirfieldTool.MinStandAngle + 0.05f, up = angle < DevAirfieldTool.MaxStandAngle - 0.05f;
                PropStepper("Turn 5°", $"{angle:0.#}°", () => airfield.TurnStand(-5f), () => airfield.TurnStand(5f),
                    "Turn it 5° (Shift+scroll)", down, up);
                PropStepper("Turn 1°", $"{angle:0.#}°", () => airfield.TurnStand(-1f), () => airfield.TurnStand(1f),
                    "Turn it 1° (Alt+Shift+scroll)", down, up);
                Buttons(B("Lean the other way", airfield.FlipStand, "Mirror it along the taxiway", leans, "Shift+Z"));
                Note("25° to 155° to the taxiway: past 90° it leans the other way. A free stand turns all the way round.");
            });
            PropButtons("Exit", B("Push-back", () => airfield.SetStandExit(StandExit.PushBack), "Nose in; a tug pushes the aircraft back out (I)",
                    airfield.StandExit == StandExit.PushBack),
                B("Taxi-out", () => airfield.SetStandExit(StandExit.TaxiOut),
                    "Nose out: the aircraft swings round on the way in and taxis out itself; needs about twice the room (I)",
                    airfield.StandExit == StandExit.TaxiOut));
            PropButtons("Light a/c", B("Take", () => airfield.SetStandNoSmall(false), "Light aircraft (under 5.7 t, such as a Cessna) may park here",
                    !airfield.StandNoSmall),
                B("Turn away", () => airfield.SetStandNoSmall(true), "Turn light aircraft away: keep it for bigger aircraft", airfield.StandNoSmall));
        }

        void StandInspector(Stand st)
        {
            Title(airfield.SelectionName);
            StandProps(st.DeIcing, st.surface);
            bool head = st.head != null && st.head.Exists;
            if (!st.DeIcing)
            {
                // picking a bridge (clicking its ring) opens its pop-out
                var picked = airfield.SelectedBridge;
                if (picked != null && picked != lastBridge && openPop != BridgesPop) Toggle(BridgesPop);
                lastBridge = picked;
                bool pushBack = st.exit == StandExit.PushBack;
                PropPop("Bridges", BridgeSummary(st, head), BridgesPop,
                    pushBack ? "Its jet bridges: place them, pick one to change its door or tunnel" : "Taxi-out stands take no bridges",
                    () => BridgesPopout(st, head), pushBack);
            }
            Space();
            Buttons(B("Delete stand", airfield.DeleteSelectedStand, "Remove the stand (and its bridges)", hint: airfield.SelectedBridge == null ? "Del" : null,
                danger: true));
            if (openPop != BridgesPop) Danger(airfield.BridgeNote);
        }

        /// <summary>"L1 L2 + head", red "!" if any has a problem; "none".</summary>
        string BridgeSummary(Stand st, bool head)
        {
            if (st.bridges.Count == 0) return head ? "head only" : "none";
            var sb = new System.Text.StringBuilder();
            bool bad = false;
            foreach (var b in st.bridges)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(b.DoorName);
                bad |= !Status(st, b).ok;
            }
            if (head) sb.Append(" + head");
            if (bad) sb.Append($"  {DevGui.Tag(DevGui.Danger)}!</color>");
            return sb.ToString();
        }

        readonly Dictionary<JetBridge, (int version, bool ok, string text)> statuses = new Dictionary<JetBridge, (int, bool, string)>();

        /// <summary>A bridge's verdict (AirportBridges.FirstProblem), worked out again only after the layout changes.</summary>
        (bool ok, string text) Status(Stand st, JetBridge b)
        {
            int v = airfield.airport.LayoutVersion;
            if (statuses.TryGetValue(b, out var c) && c.version == v) return (c.ok, c.text);
            var p = airfield.airport.Bridges.FirstProblem(st, b, out var type);
            string text = p == BridgeProblem.None ? "OK" : (type != null ? type.displayName + ": " : "") + BridgeLayout.Describe(p);
            statuses[b] = (v, p == BridgeProblem.None, text);
            return (p == BridgeProblem.None, text);
        }

        void BridgesPopout(Stand st, bool head)
        {
            var auto = new Btn[JetBridgeRules.MaxPerStand + 1];
            for (int n = 0; n < auto.Length; n++)
            {
                int count = n;
                auto[n] = B(n.ToString(), () => airfield.SetBridgeCount(count),
                    (n == 0 ? "No jet bridges (0)" : $"Place {n} jet bridge{(n == 1 ? "" : "s")} afresh, to doors L1{(n > 1 ? $"-L{n}" : "")}, where they work") +
                    ": throws away bridges placed by hand", st.bridges.Count == n);
            }
            PropButtons("Auto-place", auto);
            var picked = airfield.SelectedBridge;
            if (st.bridges.Count > 0) Section("ON THIS STAND");
            foreach (var b in st.bridges)
            {
                var bridge = b;
                var (ok, text) = Status(st, b);
                string label = $"{b.DoorName}   {(b.tunnel == TunnelSize.Short ? "short" : "long")}   " +
                               (ok ? $"{DevGui.Tag(Green)}OK</color>" : $"{DevGui.Tag(DevGui.Danger)}!</color>");
                Buttons(B(label, () => airfield.SelectBridge(bridge), ok ? "Pick it to change it" : text, b == picked));
            }
            if (picked != null)
            {
                var b = picked;
                Section($"BRIDGE {b.DoorName}");
                PropButtons("Side", B("Left", () => airfield.ChangeBridgeDoor(DoorSide.Left, b.upper, b.door), "Serve a door on the left (port) side",
                        b.side == DoorSide.Left),
                    B("Right", () => airfield.ChangeBridgeDoor(DoorSide.Right, b.upper, b.door), "Serve a door on the right (starboard) side",
                        b.side == DoorSide.Right));
                PropButtons("Deck", B("Main", () => airfield.ChangeBridgeDoor(b.side, false, b.door), "Main-deck door", !b.upper),
                    B("Upper", () => airfield.ChangeBridgeDoor(b.side, true, b.door), "Upper-deck door (double-deck aircraft): a raised rotunda", b.upper));
                PropStepper("Door", b.door.ToString(), () => airfield.ChangeBridgeDoor(b.side, b.upper, b.door - 1),
                    () => airfield.ChangeBridgeDoor(b.side, b.upper, b.door + 1), "Which door, counted from the front", b.door > 1, b.door < 6);
                PropButtons("Tunnel", B("Long", () => airfield.SetBridgeTunnel(TunnelSize.Long),
                        $"Long tunnel: {JetBridgeRules.MinTunnelOf(TunnelSize.Long):0}-{JetBridgeRules.MaxTunnelOf(TunnelSize.Long):0} m", b.tunnel == TunnelSize.Long),
                    B("Short", () => airfield.SetBridgeTunnel(TunnelSize.Short),
                        $"Short tunnel: {JetBridgeRules.MinTunnelOf(TunnelSize.Short):0.#}-{JetBridgeRules.MaxTunnelOf(TunnelSize.Short):0.#} m, " +
                        "for a bridge close to the aircraft (as on a bridge head): retracted, it swings round clear of it", b.tunnel == TunnelSize.Short));
                var (ok, text) = Status(st, b);
                if (!ok) Danger(text);
                Buttons(B($"Delete {b.DoorName}", airfield.DeleteSelectedBridge, "Take this bridge off the stand", hint: "Del", danger: true));
            }
            if (head)
            {
                Space();
                Buttons(B("Delete head", airfield.DeleteSelectedHead, "Remove the bridge head and the bridges on it", danger: true));
            }
            Danger(airfield.BridgeNote);
            Note((picked == null ? "click a ring on the ground to pick a bridge   " : "") + "drag a ring to move its rotunda   " +
                 "drag a square on the terminal wall to move where its link leaves" +
                 (head ? "   drag the head's end rings to stretch or turn it, the head to move it" : "") +
                 "   the Bridge tool places bridges and heads by hand");
        }

        void BridgeTool()
        {
            bool head = airfield.PlacingHead;
            Title("Bridge tool");
            PropButtons("Place", B("Bridge", () => airfield.SetPlacingHead(false), "Place a jet bridge: its link from a terminal wall (or a bridge head) to its rotunda",
                    !head),
                B("Head", () => airfield.SetPlacingHead(true),
                    "Place a bridge head: a main link from the terminal to a fixed building beside the aircraft, that two or three bridges share", head));
            if (airfield.BridgePlaced != null) Note($"{DevGui.Tag(Green)}{airfield.BridgePlaced}</color>");
            string target = airfield.BridgeTarget;
            if (target != null) Note($"{DevGui.Tag(Cyan)}for {target}</color>");
            if (airfield.BridgeToolProblem != null)
            {
                Danger(airfield.BridgeToolProblem);
                return;
            }
            Note(head
                ? !airfield.BridgeStarted ? "1. click a terminal's wall where the head's main link leaves the building"
                : !airfield.HeadStarted ? "2. click where the head starts (at the wall: no main link)   right-click / Esc: start again"
                : $"3. click where the head ends ({JetBridgeRules.MinHead:0}-{JetBridgeRules.MaxHead:0} m)   right-click / Esc: start again"
                : !airfield.BridgeStarted ? "1. click a terminal's wall where the link leaves the building (or a stand's bridge head)"
                : "2. click where the rotunda stands   right-click / Esc: start again");
        }

        // ------------------------------------------------------------------ maintenance, depots, aircraft

        void Maintenance(bool grass, bool verges) =>
            Buttons(B("Plough", airfield.Plough, "Send the ploughs (a taxiway's or road's whole run)", hint: "P"),
                B("Mow", () => airfield.Mow(false), "Send the mowers (grass only)", hint: "M", enabled: grass),
                B("Verges", () => airfield.Mow(true), "Mow the grass either side (Shift+M)", enabled: verges));

        void Fleet(MaintenanceDepot d)
        {
            PropStepper("Ploughs", d.snowPloughs.ToString(), () => airfield.StepFleet(-1, 0), () => airfield.StepFleet(1, 0), "Snow ploughs (, / .)",
                d.snowPloughs > 0, d.snowPloughs < 8);
            PropStepper("Mowers", d.mowers.ToString(), () => airfield.StepFleet(0, -1), () => airfield.StepFleet(0, 1), "Mowers (Shift + , / .)",
                d.mowers > 0, d.mowers < 4);
            PropStepper("Tugs", d.smallTugs.ToString(), () => airfield.StepTugs(-1, 0), () => airfield.StepTugs(1, 0),
                "Towbarless tugs for regional and narrow-body aircraft (10-100 t)", d.smallTugs > 0, d.smallTugs < 6);
            PropStepper("Large tugs", d.largeTugs.ToString(), () => airfield.StepTugs(0, -1), () => airfield.StepTugs(0, 1),
                "Wide-body tugs (40-600 t)", d.largeTugs > 0, d.largeTugs < 4);
        }

        void AircraftInspector()
        {
            var a = sandbox.Selected;
            Title(a.name);
            var flight = traffic != null ? traffic.SelectedFlight : null;
            if (flight != null)
            {
                Note(flight.Status);
                Space();
                Buttons(B("Delete aircraft", () => sandbox.DeleteSelected(), "Remove the aircraft", hint: "Del", danger: true));
                return;
            }
            var tow = sandbox.SelectedTow;
            if (tow != null) Note(tow.Status);
            else
            {
                if (traffic != null)
                    Buttons(B("Depart", traffic.Depart, "Taxi to the runway in use and take off (a tug pushes it back first); its way is drawn on the ground",
                            hint: "Space"),
                        B("Push back", () => sandbox.PushBack(Shifted), "A tug comes and pushes it back off its stand onto the taxiway (Shift: to face the other way)",
                            hint: "Enter"));
                else
                    Buttons(B("Push back", () => sandbox.PushBack(Shifted),
                        "A tug comes and pushes it back off its stand onto the taxiway (Shift: to face the other way)", hint: "Enter"));
                Buttons(B("Park", sandbox.Park, "Put it on the nearest free stand that takes it"),
                    B("Taxi", () => sandbox.Taxiing = !sandbox.Taxiing, "Taxi it round in a slow weave", sandbox.Taxiing, "T"));
            }
            Buttons(B("Dirt", sandbox.AddDirt, "A quarter more dirt (clean again after full)", hint: "G"));
            Space();
            Buttons(B("Delete aircraft", () => sandbox.DeleteSelected(), "Remove the aircraft", hint: "Del", danger: true));
            string message = traffic != null && traffic.Message != null ? traffic.Message
                : sandbox.Message ?? (traffic != null && traffic.PreviewProblem != null ? "Can't depart: " + traffic.PreviewProblem : null);
            if (tow == null) Note(message);
        }

        // ------------------------------------------------------------------ world, maintenance, traffic, aircraft (sidebar)

        /// <summary>Sidebar sections folded shut (their caption clicked).</summary>
        static readonly HashSet<string> folded = new HashSet<string>();

        /// <summary>A section's caption, clicked to fold it shut or open; true if it's open.</summary>
        bool Fold(string key, string caption)
        {
            bool open = !folded.Contains(key);
            Add(U(20f), r =>
            {
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    if (!folded.Remove(key)) folded.Add(key);
                }
                GUI.Label(r, (open ? "-   " : "+   ") + caption, DevGui.Caption);
            });
            return open;
        }

        void Sidebar()
        {
            var airport = airfield != null ? airfield.airport : null;
            if (airport != null)
            {
                if (Fold("world", "WORLD"))
                {
                    Buttons(Weather(SurfaceCondition.Dry), Weather(SurfaceCondition.Wet), Weather(SurfaceCondition.Snow));
                    var speeds = new Btn[DevAirfieldTool.TimeScales.Length];
                    for (int i = 0; i < speeds.Length; i++)
                    {
                        float s = DevAirfieldTool.TimeScales[i];
                        speeds[i] = B($"{s:0}x", () => airfield.SetTimeScale(s), $"Game time {s:0}x: weather, grass and the vehicles (Y)",
                            Mathf.Approximately(airport.timeScale, s));
                    }
                    Buttons(speeds);
                    if (sandbox != null) Buttons(B("Night", () => sandbox.Night = !sandbox.Night, "Day / night (+ / - for dusk)", sandbox.Night, "N"));
                    Buttons(B("Snowfall", airfield.Snowfall, "2 cm of snow at once", hint: "O"),
                        B("Grass", airfield.GrowGrass, "Grass strips and verges grow 6 cm at once", hint: "U"));
                }
                if (Fold("maintenance", "MAINTENANCE"))
                {
                    Buttons(B("Plough all", airfield.PloughAll, "Ploughs to every runway, taxiway and road with snow on it"),
                        B("Mow all", airfield.MowAll, "Mowers to every grass strip and grown verge"));
                    Buttons(B("Auto dispatch", () => airfield.AutoDispatch = !airfield.AutoDispatch,
                        "The depots send ploughs and mowers by themselves when snow lies or grass grows", airfield.AutoDispatch));
                }
                if (traffic != null && Fold("traffic", $"TRAFFIC   wind from {traffic.Wind:000}°"))
                {
                    var lands = new List<Btn>();
                    foreach (var t in traffic.Types)
                    {
                        var type = t;
                        lands.Add(B("Land " + (type.maxTakeoffMass < 5700f ? "light" : "jet"), () => traffic.Land(type),
                            $"A {type.displayName} comes in on final for the runway in use and taxis to a free stand"));
                    }
                    if (lands.Count > 0) Buttons(lands.ToArray());
                    Buttons(B("Auto traffic", () => traffic.AutoTraffic = !traffic.AutoTraffic,
                        "Arrivals every few minutes; aircraft that have stood a while depart", traffic.AutoTraffic));
                    Buttons(B("Wind -30°", () => traffic.TurnWind(-30f), "Turn the wind: runways are used into it"),
                        B("Wind +30°", () => traffic.TurnWind(30f), "Turn the wind: runways are used into it"));
                }
            }
            else if (sandbox != null && Fold("world", "WORLD"))
                Buttons(B("Night", () => sandbox.Night = !sandbox.Night, "Day / night (+ / - for dusk)", sandbox.Night, "N"));
            if (sandbox != null && Fold("aircraft", "AIRCRAFT"))
            {
                Buttons(B("Engines", () => sandbox.Engines = !sandbox.Engines, "Engines on: props and fans spin, beacons on", sandbox.Engines, "R"),
                    B("Beacons", () => sandbox.Beacons = !sandbox.Beacons, "Red anti-collision beacons", sandbox.Beacons, "1"));
                Buttons(B("Strobes", () => sandbox.Strobes = !sandbox.Strobes, "White wingtip strobes", sandbox.Strobes, "2"),
                    B("Landing", () => sandbox.Landing = !sandbox.Landing, "Landing and taxi lights", sandbox.Landing, "3"));
                Buttons(B("Logo", () => sandbox.Logos = !sandbox.Logos, "Tail logo floodlights", sandbox.Logos, "4"));
            }
            Add(U(16f), r => GUI.Label(r, "H hides the HUD   click a caption to fold it", DevGui.Small));
        }

        Btn Weather(SurfaceCondition c) =>
            B(DevAirfieldTool.WeatherName(c), () => airfield.SetWeather(c),
                c == SurfaceCondition.Snow ? "Snowing: snow builds up until ploughed (C)" : c == SurfaceCondition.Wet ? "Rain (C)" : "Dry (C)",
                airfield.airport.Condition == c);
    }
}
