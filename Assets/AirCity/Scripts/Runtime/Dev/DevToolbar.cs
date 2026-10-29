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
    /// DevSandboxControls, whose keys still work:
    ///   bottom: the tools (select, runway, taxiway, road, apron, depot, stand, de-icing bay | delete), and above them a
    ///     strip for the tool on (surface, width, lights; a stand's code, angle and exit) or for what's selected
    ///     (its upgrades, maintenance and Delete)
    ///   right: weather, time speed, day / night, snowfall and grass; maintenance; traffic (landings, auto traffic,
    ///     the wind); aircraft lights and engines
    ///   Delete / Backspace removes the selection (the aircraft or the airfield one, whichever came last)
    ///   H hides the HUD
    /// Clicks and scrolls over the HUD don't reach the world (DevGui.PointerOverHud).
    /// </summary>
    public class DevToolbar : MonoBehaviour
    {
        DevAirfieldTool airfield;
        DevSandboxControls sandbox;
        DevTrafficControls traffic;
        readonly List<(float width, Action<Rect> draw)> strip = new List<(float, Action<Rect>)>();
        float sidebarHeight;

        static readonly (DevTool tool, string label, string tip, string hint)[] Tools =
        {
            (DevTool.Select, "Select", "Select runways, taxiways, roads, depots, stands, aprons and aircraft", "Esc"),
            (DevTool.Runway, "Runway", "Drag from threshold to threshold (B cycles the build tools)", null),
            (DevTool.Taxiway, "Taxiway", "Click points to lay a taxiway; right-click ends it", null),
            (DevTool.Road, "Road", "Click points to lay an airside service road (across a paved apron it is just paint); right-click ends it", null),
            (DevTool.Apron, "Apron", "Click corners to lay an apron of any shape, or drag out a rectangle", null),
            (DevTool.Depot, "Depot", "Drag from the doors outwards to build a maintenance depot", null),
            (DevTool.Stand, "Stand", "Click beside a taxiway to add an aircraft stand (it snaps to the stands beside it)", null),
            (DevTool.DeIcing, "De-ice", "Click on a taxiway to add a drive-through de-icing bay", null),
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
            float bar = ToolBar();
            Strip(bar);
            Sidebar();
            DevGui.Tooltip();
        }

        static float U(float units) => DevGui.U(units);

        // ------------------------------------------------------------------ tools (bottom)

        /// <summary>The tool buttons, bottom centre; returns the bar's top.</summary>
        float ToolBar()
        {
            if (airfield == null || airfield.airport == null) return Screen.height - U(12f);
            float w = U(72f), h = U(42f), gap = U(4f), pad = U(7f), split = U(14f);
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
            return r.y;
        }

        // ------------------------------------------------------------------ context strip (above the tools)

        void Item(float width, Action<Rect> draw) => strip.Add((width, draw));

        void Text(string text, GUIStyle style) => Item(DevGui.Width(text, style), r => GUI.Label(r, text, style));

        void Gap() => Item(U(9f), r => DevGui.Divider(r.center.x, r.y + U(5f), r.height - U(10f)));

        static bool Shifted => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        void Chip(string label, bool on, Action act, string tip = null, string hint = null, bool danger = false, bool enabled = true)
        {
            float width = Mathf.Max(U(40f), DevGui.Width(label, DevGui.Bold) + U(hint != null ? 30f : 18f));
            Item(width, r =>
            {
                if (DevGui.Button(r, label, tip, on, danger, enabled, hint)) act();
            });
        }

        /// <summary>[-] value [+]</summary>
        void Stepper(string value, Action down, Action up, string tip, bool canDown = true, bool canUp = true)
        {
            Chip("-", false, down, tip, enabled: canDown);
            Item(Mathf.Max(U(46f), DevGui.Width(value, DevGui.Bold) + U(8f)), r => GUI.Label(r, value, Centred));
            Chip("+", false, up, tip, enabled: canUp);
        }

        static GUIStyle centred;
        static float centredScale;
        static GUIStyle Centred
        {
            get
            {
                if (centred == null || !Mathf.Approximately(centredScale, DevGui.Scale))
                {
                    centred = new GUIStyle(DevGui.Bold) { alignment = TextAnchor.MiddleCenter };
                    centredScale = DevGui.Scale;
                }
                return centred;
            }
        }

        /// <summary>A chip per surface, `current` lit (the selected runway's, else what new pavement gets).</summary>
        void SurfaceChips(PavementSurface current, bool grass = true)
        {
            Text("Surface", DevGui.Small);
            for (int i = 0; i < Surfaces.Count; i++)
            {
                var s = (PavementSurface)i;
                if (s == PavementSurface.Grass && !grass) continue;
                Chip(s.ToString(), current == s, () => airfield.SetSurface(s), SurfaceTips[i] + " (V)");
            }
        }

        void RunwayWidth(float width) =>
            Stepper($"{width:0} m", () => airfield.StepWidth(-1), () => airfield.StepWidth(1), "Width ([ / ])",
                width > RunwayRules.RunwayWidths[0], width < RunwayRules.RunwayWidths[RunwayRules.RunwayWidths.Length - 1]);

        /// <summary>The settings of the tool on, or what can be done to the selection, in a strip above the tools.</summary>
        void Strip(float barTop)
        {
            strip.Clear();
            if (airfield != null && airfield.airport != null) FillStrip();
            else if (sandbox != null && sandbox.Selected != null) AircraftStrip();
            if (strip.Count == 0) return;
            float gap = U(4f), pad = U(7f), h = U(30f);
            float width = pad * 2f - gap;
            foreach (var (w, _) in strip) width += w + gap;
            var r = new Rect(Mathf.Round((Screen.width - width) * 0.5f), barTop - h - pad * 2f - U(8f), width, h + pad * 2f);
            DevGui.Panel(r);
            float x = r.x + pad;
            foreach (var (w, draw) in strip)
            {
                draw(new Rect(x, r.y + pad, w, h));
                x += w + gap;
            }
        }

        void FillStrip()
        {
            switch (airfield.Tool)
            {
                case DevTool.Runway:
                    SurfaceChips(airfield.Surface);
                    Gap();
                    Text("Width", DevGui.Small);
                    RunwayWidth(airfield.NewWidth);
                    Gap();
                    Text("drag from threshold to threshold   Shift: 10° and 50 m steps", DevGui.Hint);
                    return;
                case DevTool.Taxiway:
                    SurfaceChips(airfield.Surface);
                    Gap();
                    Text("Width", DevGui.Small);
                    float w = airfield.NewWidth;
                    Stepper($"{w:0.#} m", () => airfield.StepWidth(-1), () => airfield.StepWidth(1), "Width ([ / ])",
                        w > RunwayRules.TaxiwayWidths[0], w < RunwayRules.TaxiwayWidths[RunwayRules.TaxiwayWidths.Length - 1]);
                    Gap();
                    Chip("Lights", airfield.TaxiwaysLit, airfield.ToggleTaxiwayLights, "Blue edge lights on every paved taxiway", "L");
                    Gap();
                    Text(airfield.RoadProblem != null ? $"{DevGui.Tag(DevGui.Danger)}{airfield.RoadProblem}</color>"
                        : Joins("click points   Shift: 10° and 5 m steps   right-click ends   dashed: stands' front lines"), DevGui.Hint);
                    return;
                case DevTool.Road:
                    SurfaceChips(airfield.Surface, false);
                    Gap();
                    Text(airfield.RoadProblem != null ? $"{DevGui.Tag(DevGui.Danger)}{airfield.RoadProblem}</color>"
                        : Joins($"service road, {RunwayRules.RoadWidth:0} m wide   click points   Shift: 10° and 5 m steps   " +
                                "right-click ends   paint only across paved aprons"), DevGui.Hint);
                    return;
                case DevTool.Apron:
                    SurfaceChips(airfield.Surface);
                    Gap();
                    var why = airfield.ApronProblemText;
                    Text(why != null ? $"{DevGui.Tag(DevGui.Danger)}{why}</color>"
                        : Joins(airfield.ApronCornerCount == 0 ? "click corners, or drag out a rectangle   Shift: 10° and 5 m steps"
                            : "click the first corner or right-click to finish   Shift: 10° and 5 m steps   Esc drops it"), DevGui.Hint);
                    return;
                case DevTool.Depot:
                    Text("drag from the doors outwards   click a depot to select it", DevGui.Hint);
                    if (airfield.FleetDepot != null)
                    {
                        Gap();
                        Fleet(airfield.FleetDepot);
                    }
                    return;
                case DevTool.Stand:
                case DevTool.DeIcing:
                    StandChips(airfield.Tool == DevTool.DeIcing, airfield.Surface);
                    Gap();
                    string problem = airfield.GhostProblem, note = airfield.GhostNote;
                    Text(problem != null ? $"{DevGui.Tag(DevGui.Danger)}{problem}</color>"
                        : (note != null ? $"{DevGui.Tag(new Color(0.3f, 0.9f, 1f))}{note}</color>   " : "") +
                          (airfield.Tool == DevTool.DeIcing ? "click on a taxiway or anywhere   Shift+scroll: turn it"
                              : "click anywhere   Shift+scroll: turn it"), DevGui.Hint);
                    return;
                case DevTool.Delete:
                    var under = airfield.HoverName;
                    if (under == null && sandbox != null && sandbox.Hovered != null) under = sandbox.Hovered.name;
                    Text(under != null ? $"{DevGui.Tag(DevGui.Danger)}click to remove</color> {under}"
                        : "click a runway, taxiway, road, depot, stand, apron or aircraft to remove it   Shift: a taxiway's whole run", DevGui.Hint);
                    return;
            }
            // select: what can be done to the selection
            if (sandbox != null && sandbox.Selected != null && (!airfield.HasSelection || sandbox.SelectedFrame >= airfield.SelectedFrame))
            {
                AircraftStrip();
                return;
            }
            var runway = airfield.SelectedRunway;
            var edge = airfield.SelectedEdge;
            var depot = airfield.SelectedDepot;
            if (runway != null)
            {
                Text(airfield.SelectionName, DevGui.Title);
                Gap();
                SurfaceChips(runway.surface);
                Gap();
                RunwayWidth(runway.width);
                Gap();
                Chip($"Lights: {runway.lighting}", runway.lighting != RunwayLighting.None, airfield.ToggleLighting,
                    "None / basic / precision lighting (unlit runways close at night)", "L", enabled: runway.Paved);
                Gap();
                Text($"in use: {airfield.airport.Traffic.InUse(runway)}", DevGui.Small);
                Gap();
                Chip("Wear", false, airfield.Wear, "Wear it by a quarter", "K");
                Chip("Repair", false, airfield.Repair, "Repair it (closes it while the work goes on)", "J");
                Gap();
                Maintenance(runway.surface == PavementSurface.Grass, true);
                Gap();
                Chip("Delete", false, () => airfield.DeleteSelection(), "Remove the runway", "Del", true);
            }
            else if (edge != null)
            {
                Text(airfield.SelectionName, DevGui.Title);
                Gap();
                Maintenance(edge.surface == PavementSurface.Grass, edge.HasVerges);
                Gap();
                Chip("Delete", false, () => airfield.DeleteSelection(), "Remove this section (between junctions)", "Del", true);
                Chip("Delete run", false, airfield.DeleteSelectedRun, "Remove it and the sections running on from it, as far as it goes", null, true);
            }
            else if (depot != null)
            {
                Text(depot.name, DevGui.Title);
                Gap();
                Fleet(depot);
                Gap();
                Chip("Delete", false, () => airfield.DeleteSelection(), "Remove the depot and its vehicles", "Del", true);
            }
            else if (airfield.ApronSelected)
            {
                Text(airfield.SelectionName, DevGui.Title);
                Gap();
                SurfaceChips(airfield.SelectedApron.surface);
                Gap();
                Chip("Delete", false, () => airfield.DeleteSelection(), "Remove the apron", "Del", true);
                if (airfield.ApronProblemText != null)
                {
                    Gap();
                    Text($"{DevGui.Tag(DevGui.Danger)}{airfield.ApronProblemText}</color>", DevGui.Hint);
                }
            }
            else if (airfield.SelectedStand != null)
            {
                var st = airfield.SelectedStand;
                Text(airfield.SelectionName, DevGui.Title);
                Gap();
                StandChips(st.DeIcing, st.surface);
                Gap();
                Chip("Delete", false, () => airfield.DeleteSelection(), "Remove it", "Del", true);
            }
            else Text("click a runway, taxiway, road, depot, stand, apron or aircraft to select it", DevGui.Hint);
        }

        /// <summary>Size code, surface, and for parking stands the angle, lean and exit: for new stands, or the
        /// selected one.</summary>
        /// <summary>The hint, or what the next point sticks to (cyan, like its ring on the ground).</summary>
        string Joins(string hint)
        {
            var what = airfield.SnapText;
            return what == null ? hint : $"{DevGui.Tag(new Color(0.3f, 0.9f, 1f))}joins {what}</color>   {hint}";
        }

        void StandChips(bool deIcing, PavementSurface current)
        {
            Text("Code", DevGui.Small);
            for (int i = 0; i < SizeCodes.Count; i++)
            {
                var c = (SizeCode)i;
                Chip(c.Letter().ToString(), airfield.StandCode == c, () => airfield.SetStandCode(c), CodeTips[i] + " ([ / ])");
            }
            Gap();
            Text("Surface", DevGui.Small);
            for (int i = 0; i < Surfaces.Count; i++)
            {
                var s = (PavementSurface)i;
                if (deIcing && !s.IsPaved()) continue;
                bool ok = RunwayRules.StandSurfaceOk(airfield.StandCode.Letter(), s);
                Chip(s.ToString(), current == s, () => airfield.SetSurface(s), SurfaceTips[i] + (ok ? "" : " (codes A and B only)") + " (V)",
                    enabled: ok || airfield.SelectedStand == null);
            }
            Gap();
            if (deIcing)
            {
                Chip("Turn round", false, airfield.FlipStand, "Aircraft taxi through it the other way", "Shift+Z");
                return;
            }
            Text("Angle", DevGui.Small);
            float angle = airfield.StandAngle;
            float lean = airfield.StandLeans ? 180f - angle : angle;
            foreach (float preset in DevAirfieldTool.StandAngles)
                Chip($"{preset:0}", Mathf.Abs(lean - preset) < 0.5f, () => airfield.SetStandPreset(preset), "Angle to the taxiway, keeping its lean (Z)");
            Stepper($"{angle:0.#}°", () => airfield.TurnStand(-5f), () => airfield.TurnStand(5f),
                "Angle to the taxiway, 25° to 155° (past 90° it leans the other way): Shift+scroll turns it 5° a notch, with Alt 1°",
                angle > DevAirfieldTool.MinStandAngle + 0.05f, angle < DevAirfieldTool.MaxStandAngle - 0.05f);
            Chip("Lean", airfield.StandLeans, airfield.FlipStand, "Lean the other way along the taxiway", "Shift+Z");
            Gap();
            Chip("Push-back", airfield.StandExit == StandExit.PushBack, () => airfield.SetStandExit(StandExit.PushBack),
                "Nose in; a tug pushes the aircraft back out (I)");
            Chip("Taxi-out", airfield.StandExit == StandExit.TaxiOut, () => airfield.SetStandExit(StandExit.TaxiOut),
                "Nose out: the aircraft swings round on the way in and taxis out itself; needs about twice the room (I)");
            Gap();
            Chip("No small", airfield.StandNoSmall, () => airfield.SetStandNoSmall(!airfield.StandNoSmall),
                "Turn small aircraft away (light aircraft under 5.7 t, such as a Cessna): keep it for bigger aircraft");
        }

        void Maintenance(bool grass, bool verges)
        {
            Chip("Plough", false, airfield.Plough, "Send the ploughs (a taxiway's or road's whole run)", "P");
            Chip("Mow", false, () => airfield.Mow(false), "Send the mowers (grass only)", "M", enabled: grass);
            Chip("Verges", false, () => airfield.Mow(true), "Mow the grass either side (Shift+M)", enabled: verges);
        }

        void Fleet(MaintenanceDepot d)
        {
            Text("Ploughs", DevGui.Small);
            Stepper(d.snowPloughs.ToString(), () => airfield.StepFleet(-1, 0), () => airfield.StepFleet(1, 0), "Snow ploughs (, / .)",
                d.snowPloughs > 0, d.snowPloughs < 8);
            Gap();
            Text("Mowers", DevGui.Small);
            Stepper(d.mowers.ToString(), () => airfield.StepFleet(0, -1), () => airfield.StepFleet(0, 1), "Mowers (Shift + , / .)",
                d.mowers > 0, d.mowers < 4);
            Gap();
            Text("Tugs", DevGui.Small);
            Stepper(d.smallTugs.ToString(), () => airfield.StepTugs(-1, 0), () => airfield.StepTugs(1, 0),
                "Towbarless tugs for regional and narrow-body aircraft (10-100 t)", d.smallTugs > 0, d.smallTugs < 6);
            Gap();
            Text("Large", DevGui.Small);
            Stepper(d.largeTugs.ToString(), () => airfield.StepTugs(0, -1), () => airfield.StepTugs(0, 1),
                "Wide-body tugs (40-600 t)", d.largeTugs > 0, d.largeTugs < 4);
        }

        void AircraftStrip()
        {
            var a = sandbox.Selected;
            Text(a.name, DevGui.Title);
            Gap();
            var flight = traffic != null ? traffic.SelectedFlight : null;
            if (flight != null)
            {
                Text(flight.Status, DevGui.Label);
                Gap();
                Chip("Delete", false, () => sandbox.DeleteSelected(), "Remove the aircraft", "Del", true);
                return;
            }
            var tow = sandbox.SelectedTow;
            if (tow != null) Text(tow.Status, DevGui.Label);
            else
            {
                if (traffic != null)
                    Chip("Depart", false, traffic.Depart,
                        "Taxi to the runway in use and take off (a tug pushes it back first); its way is drawn on the ground", "Space");
                Chip("Push back", false, () => sandbox.PushBack(Shifted),
                    "A tug comes and pushes it back off its stand onto the taxiway (Shift: to face the other way)", "Enter");
                Chip("Park", false, sandbox.Park, "Put it on the nearest free stand that takes it");
                Chip("Taxi", sandbox.Taxiing, () => sandbox.Taxiing = !sandbox.Taxiing, "Taxi it round in a slow weave", "T");
            }
            Chip("Dirt", false, sandbox.AddDirt, "A quarter more dirt (clean again after full)", "G");
            Gap();
            Chip("Delete", false, () => sandbox.DeleteSelected(), "Remove the aircraft", "Del", true);
            string message = traffic != null && traffic.Message != null ? traffic.Message
                : sandbox.Message ?? (traffic != null && traffic.PreviewProblem != null ? "Can't depart: " + traffic.PreviewProblem : null);
            if (tow == null && message != null)
            {
                Gap();
                Text(message, DevGui.Hint);
            }
        }

        // ------------------------------------------------------------------ world, maintenance, aircraft (right)

        void Sidebar()
        {
            float pad = U(10f), w = U(190f), row = U(26f), gap = U(4f), cap = U(18f);
            float inner = w - pad * 2f;
            var airport = airfield != null ? airfield.airport : null;
            // as tall as it came out last frame
            var r = new Rect(Screen.width - w - U(12f), U(12f), w, sidebarHeight > 0f ? sidebarHeight : U(420f));
            DevGui.Panel(r);
            float x = r.x + pad, y = r.y + pad;

            void Caption(string text)
            {
                GUI.Label(new Rect(x, y, inner, cap), text, DevGui.Caption);
                y += cap;
            }

            // a row of equal buttons
            void Row(params (string label, bool on, Action act, string tip, string hint, bool enabled)[] buttons)
            {
                float bw = (inner - gap * (buttons.Length - 1)) / buttons.Length;
                for (int i = 0; i < buttons.Length; i++)
                {
                    var b = buttons[i];
                    if (DevGui.Button(new Rect(Mathf.Round(x + i * (bw + gap)), y, Mathf.Round(bw), row), b.label, b.tip, b.on,
                            enabled: b.enabled, hint: b.hint))
                        b.act();
                }
                y += row + gap;
            }

            if (airport != null)
            {
                Caption("WORLD");
                Row(Weather(SurfaceCondition.Dry), Weather(SurfaceCondition.Wet), Weather(SurfaceCondition.Snow));
                var speeds = new (string, bool, Action, string, string, bool)[DevAirfieldTool.TimeScales.Length];
                for (int i = 0; i < speeds.Length; i++)
                {
                    float s = DevAirfieldTool.TimeScales[i];
                    speeds[i] = ($"{s:0}x", Mathf.Approximately(airport.timeScale, s), () => airfield.SetTimeScale(s),
                        $"Game time {s:0}x: weather, grass and the vehicles (Y)", null, true);
                }
                Row(speeds);
                if (sandbox != null)
                    Row(("Night", sandbox.Night, () => sandbox.Night = !sandbox.Night, "Day / night (+ / - for dusk)", "N", true));
                Row(("Snowfall", false, airfield.Snowfall, "2 cm of snow at once", "O", true),
                    ("Grass", false, airfield.GrowGrass, "Grass strips and verges grow 6 cm at once", "U", true));
                y += U(8f);
                Caption("MAINTENANCE");
                Row(("Plough all", false, airfield.PloughAll, "Ploughs to every runway, taxiway and road with snow on it", null, true),
                    ("Mow all", false, airfield.MowAll, "Mowers to every grass strip and grown verge", null, true));
                Row(("Auto dispatch", airfield.AutoDispatch, () => airfield.AutoDispatch = !airfield.AutoDispatch,
                    "The depots send ploughs and mowers by themselves when snow lies or grass grows", null, true));
                y += U(8f);
                if (traffic != null)
                {
                    Caption($"TRAFFIC   wind from {traffic.Wind:000}°");
                    var lands = new List<(string, bool, Action, string, string, bool)>();
                    foreach (var t in traffic.Types)
                    {
                        var type = t;
                        lands.Add(("Land " + (type.maxTakeoffMass < 5700f ? "light" : "jet"), false, () => traffic.Land(type),
                            $"A {type.displayName} comes in on final for the runway in use and taxis to a free stand", null, true));
                    }
                    if (lands.Count > 0) Row(lands.ToArray());
                    Row(("Auto traffic", traffic.AutoTraffic, () => traffic.AutoTraffic = !traffic.AutoTraffic,
                        "Arrivals every few minutes; aircraft that have stood a while depart", null, true));
                    Row(("Wind -30°", false, () => traffic.TurnWind(-30f), "Turn the wind: runways are used into it", null, true),
                        ("Wind +30°", false, () => traffic.TurnWind(30f), "Turn the wind: runways are used into it", null, true));
                    y += U(8f);
                }
            }
            else if (sandbox != null)
            {
                Caption("WORLD");
                Row(("Night", sandbox.Night, () => sandbox.Night = !sandbox.Night, "Day / night (+ / - for dusk)", "N", true));
                y += U(8f);
            }
            if (sandbox != null)
            {
                Caption("AIRCRAFT");
                Row(("Engines", sandbox.Engines, () => sandbox.Engines = !sandbox.Engines, "Engines on: props and fans spin, beacons on", "R", true),
                    ("Beacons", sandbox.Beacons, () => sandbox.Beacons = !sandbox.Beacons, "Red anti-collision beacons", "1", true));
                Row(("Strobes", sandbox.Strobes, () => sandbox.Strobes = !sandbox.Strobes, "White wingtip strobes", "2", true),
                    ("Landing", sandbox.Landing, () => sandbox.Landing = !sandbox.Landing, "Landing and taxi lights", "3", true));
                Row(("Logo", sandbox.Logos, () => sandbox.Logos = !sandbox.Logos, "Tail logo floodlights", "4", true));
            }
            GUI.Label(new Rect(x, y, inner, U(16f)), "H hides the HUD", DevGui.Small);
            y += U(16f);
            if (Event.current.type == EventType.Repaint) sidebarHeight = y + pad - r.y;
        }

        (string, bool, Action, string, string, bool) Weather(SurfaceCondition c) =>
            (DevAirfieldTool.WeatherName(c), airfield.airport.Condition == c, () => airfield.SetWeather(c),
                c == SurfaceCondition.Snow ? "Snowing: snow builds up until ploughed (C)" : c == SurfaceCondition.Wet ? "Rain (C)" : "Dry (C)", null, true);
    }
}
