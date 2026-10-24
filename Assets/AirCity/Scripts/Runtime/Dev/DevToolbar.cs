using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// The sandbox HUD's buttons (development only, not game UI), next to the camera's DevAirfieldTool and
    /// DevSandboxControls, whose keys still work:
    ///   bottom: the tools (select, runway, taxiway, road, depot | delete), and above them a strip for the tool on
    ///     (surface, width, lights) or for what's selected (its upgrades, maintenance and Delete)
    ///   right: weather, time speed, day / night, snowfall and grass; maintenance; aircraft lights and engines
    ///   Delete / Backspace removes the selection (the aircraft or the airfield one, whichever came last)
    ///   H hides the HUD
    /// Clicks and scrolls over the HUD don't reach the world (DevGui.PointerOverHud).
    /// </summary>
    public class DevToolbar : MonoBehaviour
    {
        DevAirfieldTool airfield;
        DevSandboxControls sandbox;
        readonly List<(float width, Action<Rect> draw)> strip = new List<(float, Action<Rect>)>();
        float sidebarHeight;

        static readonly (DevTool tool, string label, string tip, string hint)[] Tools =
        {
            (DevTool.Select, "Select", "Select runways, taxiways, roads, depots, aprons and aircraft", "Esc"),
            (DevTool.Runway, "Runway", "Drag from threshold to threshold (B cycles the build tools)", null),
            (DevTool.Taxiway, "Taxiway", "Click points to lay a taxiway; right-click ends it", null),
            (DevTool.Road, "Road", "Click points to lay an airside service road; right-click ends it", null),
            (DevTool.Depot, "Depot", "Drag from the doors outwards to build a maintenance depot", null),
        };

        static readonly string[] SurfaceTips =
        {
            "Dirt: unpaved, goes soft in the rain", "Asphalt: paved", "Concrete: paved, wears slowest", "Grass: unpaved, grows and needs mowing",
        };

        void Start()
        {
            airfield = GetComponent<DevAirfieldTool>();
            sandbox = GetComponent<DevSandboxControls>();
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
                    Text("drag from threshold to threshold   Alt: any angle", DevGui.Hint);
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
                    Text("click points   right-click ends", DevGui.Hint);
                    return;
                case DevTool.Road:
                    SurfaceChips(airfield.Surface, false);
                    Gap();
                    Text($"{RunwayRules.RoadWidth:0} m wide   click points (start in front of a depot's doors)   right-click ends", DevGui.Hint);
                    return;
                case DevTool.Depot:
                    Text("drag from the doors outwards   click a depot to select it", DevGui.Hint);
                    if (airfield.FleetDepot != null)
                    {
                        Gap();
                        Fleet(airfield.FleetDepot);
                    }
                    return;
                case DevTool.Delete:
                    var under = airfield.HoverName;
                    if (under == null && sandbox != null && sandbox.Hovered != null) under = sandbox.Hovered.name;
                    Text(under != null ? $"{DevGui.Tag(DevGui.Danger)}click to remove</color> {under}"
                        : "click a runway, taxiway, road, depot, apron or aircraft to remove it   Shift: a taxiway's whole run", DevGui.Hint);
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
                Chip("Delete", false, () => airfield.DeleteSelection(), "Remove the apron", "Del", true);
            }
            else Text("click a runway, taxiway, road, depot, apron or aircraft to select it", DevGui.Hint);
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
        }

        void AircraftStrip()
        {
            var a = sandbox.Selected;
            Text(a.name, DevGui.Title);
            Gap();
            Chip("Taxi", sandbox.Taxiing, () => sandbox.Taxiing = !sandbox.Taxiing, "Taxi it round in a slow weave", "T");
            Chip("Dirt", false, sandbox.AddDirt, "A quarter more dirt (clean again after full)", "G");
            Gap();
            Chip("Delete", false, () => sandbox.DeleteSelected(), "Remove the aircraft", "Del", true);
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
