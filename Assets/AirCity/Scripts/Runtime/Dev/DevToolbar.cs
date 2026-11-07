using System;
using System.Collections.Generic;
using AirCity.Aircraft;
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
    ///   bottom: the tools as icons, in groups (select | runway, taxiway, road, apron | lights | terminal, stand, bridge,
    ///     gates | de-icing bay, depot | delete); over the tool on, a tray of cards for its kinds (surface, width; a
    ///     terminal's floors; a bay's size code; the kinds of lights), shown with what they look like
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
    public partial class DevToolbar : MonoBehaviour
    {
        DevAirfieldTool airfield;
        DevSandboxControls sandbox;
        DevTrafficControls traffic;
        readonly DevGateDesigner gateDesigner = new DevGateDesigner();
        public DevGateDesigner GateDesigner => gateDesigner;

        public void OpenGateDesigner(Stand stand = null)
        {
            if (airfield == null) airfield = GetComponent<DevAirfieldTool>();
            if (airfield == null || airfield.airport == null) return;
            gateDesigner.Open(airfield.airport, stand != null ? stand : airfield.SelectedStand);
            if (gateDesigner.Stand != null) airfield.SelectStand(gateDesigner.Stand);
            openPop = null;
        }
        public void OpenTerminalDesigner(Terminal terminal)
        {
            OpenGateDesigner();
            gateDesigner.SelectTerminal(terminal);
        }
        public void OpenBridgeBuilder()
        {
            if (!gateDesigner.IsOpen) OpenGateDesigner();
            gateDesigner.BeginBridgePlacement(airfield);
        }
        public void OpenStandBuilder()
        {
            if (!gateDesigner.IsOpen) OpenGateDesigner();
            gateDesigner.BeginStandPlacement(airfield);
        }
        public void EditBridge(Stand stand, JetBridge bridge)
        {
            if (!gateDesigner.IsOpen) OpenGateDesigner(stand);
            airfield.SelectStand(stand); airfield.SelectBridge(bridge);
            gateDesigner.FocusBridge(airfield, stand, bridge);
        }
        public void EditParking(Stand stand)
        {
            if (!gateDesigner.IsOpen) OpenGateDesigner(stand);
            airfield.SelectStand(stand); gateDesigner.FocusParking(airfield, stand);
        }
        void OnDisable() => gateDesigner.Close();
        void OnDestroy() => gateDesigner.Dispose();

        /// <summary>The bottom bar's buttons: the tool (null: the gate designer), its icon (Resources/AirCityIcons), name,
        /// tip and key, and its group (a divider between groups).</summary>
        static readonly (DevTool? tool, string icon, string label, string tip, string hint, int group)[] Tools =
        {
            (DevTool.Select, "select", "Select", "Select runways, taxiways, roads, depots, stands, aprons and aircraft", "Esc", 0),
            (DevTool.Runway, "runway", "Runway", "Click one threshold, then the other (or drag); B cycles the build tools", null, 1),
            (DevTool.Taxiway, "taxiway", "Taxiway", "Click points to lay a taxiway; right-click ends it", null, 1),
            (DevTool.Road, "road", "Road", "Click points to lay an airside service road (across a paved apron it is just paint); right-click ends it", null, 1),
            (DevTool.Apron, "apron", "Apron", "Click corners to lay an apron of any shape, or drag out a rectangle", null, 1),
            (DevTool.Lights, "lights", "Lights", "Light taxiways and runways: at night aircraft use only what's lit", null, 2),
            (DevTool.Terminal, "terminal", "Terminal", "Click corners for a terminal building (or drag out a rectangle); select one and Shift+scroll to curve a wall", null, 3),
            (DevTool.Stand, "stand", "Stand", "Click beside a taxiway to add an aircraft stand (it snaps to the stands beside it)", null, 3),
            (DevTool.Bridge, "bridge", "Bridge", "Place jet bridges and bridge heads by hand, starting from a terminal wall", null, 3),
            (null, "gates", "Gates", "Design gate layouts, aircraft stops, bridge families and saved presets", null, 3),
            (DevTool.DeIcing, "deice", "De-icing bay", "Click on a taxiway to add a drive-through de-icing bay", null, 4),
            (DevTool.Depot, "depot", "Depot", "Drag from the doors outwards to build a maintenance depot", null, 4),
        };

        static readonly string[] CodeTips =
        {
            "Code A: wingspan under 15 m (Starling A4)", "Code B: under 24 m (commuter turboprops and small regional jets)",
            "Code C: under 36 m (Starling C180)", "Code D: under 52 m (mid-size widebodies)",
            "Code E: under 65 m (Starling E350)", "Code F: under 80 m (Starling F450, F550)",
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
            if (airfield != null && !gateDesigner.IsOpen)
            {
                if (airfield.Tool == DevTool.Bridge) OpenBridgeBuilder();
                else if (airfield.Tool == DevTool.Stand) OpenStandBuilder();
            }
            var keys = Keyboard.current;
            if (keys == null || DevGui.KeyboardCaptured) return;
            if (gateDesigner.IsOpen && airfield != null && gateDesigner.HandleWorldInput(airfield, GetComponent<Camera>(), Mouse.current, keys)) return;
            if (keys.hKey.wasPressedThisFrame) DevGui.Visible = !DevGui.Visible;
            if (keys.escapeKey.wasPressedThisFrame)
            {
                openPop = null;
                if (gateDesigner.IsOpen)
                {
                    if (airfield.Tool == DevTool.Bridge || airfield.Tool == DevTool.Stand) airfield.SetTool(DevTool.Select);
                    else gateDesigner.Close();
                }
            }
            if (keys.deleteKey.wasPressedThisFrame || keys.backspaceKey.wasPressedThisFrame) DeleteSelection();
        }

        /// <summary>Remove the aircraft or the airfield selection, whichever was made last.</summary>
        void DeleteSelection()
        {
            if (gateDesigner.IsOpen) { gateDesigner.DeleteCurrent(); return; }
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
            if (gateDesigner.IsOpen) gateDesigner.Draw(airfield, traffic);
            else RightColumn();
            DevGui.Tooltip();
        }

        static float U(float units) => DevGui.U(units);

        static bool Shifted => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        // ------------------------------------------------------------------ tools (bottom)

        /// <summary>The tool buttons, bottom centre: square icons, a divider between groups (smaller buttons on a narrow
        /// screen); and over the tool on, its tray.</summary>
        void ToolBar()
        {
            if (airfield == null || airfield.airport == null) return;
            float w = U(48f), gap = U(4f), pad = U(7f), split = U(14f);
            int groups = Tools[Tools.Length - 1].group + 2;   // and delete's
            float room = Screen.width - U(24f) - pad * 2f - Tools.Length * gap - split * (groups - 1);
            w = Mathf.Max(U(32f), Mathf.Min(w, Mathf.Floor(room / (Tools.Length + 1))));
            float width = pad * 2f + (Tools.Length + 1) * w + (Tools.Length - groups + 1) * gap + split * (groups - 1);
            var r = new Rect(Mathf.Round((Screen.width - width) * 0.5f), Screen.height - w - pad * 2f - U(14f), width, w + pad * 2f);
            DevGui.Panel(r);
            float x = r.x + pad, y = r.y + pad;
            var on = default(Rect);
            for (int i = 0; i < Tools.Length; i++)
            {
                var (tool, icon, label, tip, hint, group) = Tools[i];
                if (i > 0 && group != Tools[i - 1].group) x = Split(x, y, w, gap, split);
                bool active = tool == null ? gateDesigner.IsOpen : airfield.Tool == tool;
                var b = new Rect(x, y, w, w);
                if (active && tool != null) on = b;
                if (DevGui.IconButton(b, DevGui.Icon(icon), label, $"<b>{label}</b>   {tip}", active, hint: hint))
                {
                    if (tool == null)
                    {
                        if (gateDesigner.IsOpen) gateDesigner.Close(); else OpenGateDesigner();
                        GUIUtility.ExitGUI();
                    }
                    else if (tool == DevTool.Bridge) OpenBridgeBuilder();
                    else if (tool == DevTool.Stand) OpenStandBuilder();
                    else
                    {
                        if (gateDesigner.IsOpen) gateDesigner.Close();
                        // the tool on again: put it away (its tray closes)
                        airfield.SetTool(active ? DevTool.Select : tool.Value);
                    }
                }
                x += w + gap;
            }
            x = Split(x, y, w, gap, split);
            bool deleting = airfield.Tool == DevTool.Delete;
            if (DevGui.IconButton(new Rect(x, y, w, w), DevGui.Icon("delete"), gateDesigner.IsOpen ? "Remove" : "Delete",
                    gateDesigner.IsOpen ? "<b>Remove</b>   the selected gate item"
                        : "<b>Delete</b>   click anything to remove it; Shift+click: a taxiway's or road's whole run",
                    deleting, danger: true, hint: "X"))
            {
                if (gateDesigner.IsOpen) gateDesigner.DeleteCurrent();
                else airfield.SetTool(deleting ? DevTool.Select : DevTool.Delete);
            }
            if (!gateDesigner.IsOpen && on.width > 0f) Tray(on, r);
        }

        /// <summary>A divider between button groups; where the next group starts.</summary>
        static float Split(float x, float y, float w, float gap, float split)
        {
            x -= gap;
            DevGui.Divider(x + split * 0.5f, y + U(8f), w - U(16f));
            return x + split;
        }

        // ------------------------------------------------------------------ the tray (the tool's kinds, over its button)

        /// <summary>A card in the tray: its picture (null: a plain button), name, small line under it, tip, whether it's
        /// the one chosen (or can be had), and what picking it does.</summary>
        readonly struct Card
        {
            public readonly Action<Rect> picture;
            public readonly string label, sub, tip;
            public readonly bool on, enabled;
            public readonly Action act;

            public Card(Action<Rect> picture, string label, string sub, string tip, bool on, bool enabled, Action act)
            {
                this.picture = picture;
                this.label = label;
                this.sub = sub;
                this.tip = tip;
                this.on = on;
                this.enabled = enabled;
                this.act = act;
            }
        }

        /// <summary>The tray's groups of cards, side by side under their captions; built afresh each OnGUI call.</summary>
        readonly List<(string caption, float units, Card[] cards)> tray = new List<(string, float, Card[])>();

        void Group(string caption, float cardUnits, params Card[] cards) => tray.Add((caption, cardUnits, cards));

        /// <summary>The tool's kinds: surface and width for pavement, floors for terminals, size code, surface and
        /// direction for de-icing bays, the kinds of lights. Other tools have none.</summary>
        void BuildTray()
        {
            tray.Clear();
            switch (airfield.Tool)
            {
                case DevTool.Runway:
                    SurfaceCards(airfield.Surface, true, false);
                    WidthCards(RunwayRules.RunwayWidths, airfield.SelectedRunway != null ? airfield.SelectedRunway.width : airfield.NewWidth, false);
                    break;
                case DevTool.Taxiway:
                    SurfaceCards(airfield.Surface, true, false);
                    WidthCards(RunwayRules.TaxiwayWidths, airfield.NewWidth, true);
                    break;
                case DevTool.Lights:
                    LightCards();
                    break;
                case DevTool.Road:
                    SurfaceCards(airfield.Surface, false, false);
                    break;
                case DevTool.Apron:
                    SurfaceCards(airfield.Surface, true, false);
                    break;
                case DevTool.Terminal:
                    FloorCards(airfield.TerminalFloors);
                    break;
                case DevTool.DeIcing:
                    CodeCards();
                    SurfaceCards(airfield.Surface.IsPaved() ? airfield.Surface : PavementSurface.Concrete, false, true);
                    Group("DIRECTION", 76f, new Card(null, "Turn round", null, "Aircraft taxi through it the other way (Shift+Z)",
                        false, true, airfield.FlipStand));
                    break;
            }
        }

        void SurfaceCards(PavementSurface current, bool grass, bool pavedOnly)
        {
            var cards = new List<Card>();
            for (int i = 0; i < Surfaces.Count; i++)
            {
                var s = (PavementSurface)i;
                if ((s == PavementSurface.Grass && !grass) || (pavedOnly && !s.IsPaved())) continue;
                cards.Add(new Card(r => Swatch(r, s), s.ToString(), null, SurfaceTips[i] + " (V)", current == s, true, () => airfield.SetSurface(s)));
            }
            Group("SURFACE   V", 64f, cards.ToArray());
        }

        /// <summary>A card per width, each a strip of the chosen surface drawn to scale, with the size code it's for.</summary>
        void WidthCards(float[] widths, float current, bool taxiway)
        {
            var cards = new Card[widths.Length];
            float widest = widths[widths.Length - 1];
            var surface = airfield.Surface;
            for (int i = 0; i < widths.Length; i++)
            {
                float w = widths[i];
                string codes = taxiway ? RunwayRules.TaxiwayCode(w).ToString() : RunwayCodes(w);
                cards[i] = new Card(r => Strip(r, surface, w / widest, taxiway), $"{w:0.#} m", "code " + codes,
                    $"{w:0.#} m wide: for code {codes} aircraft and smaller ([ / ])", Mathf.Abs(current - w) < 0.05f, true,
                    () => airfield.SetWidth(w));
            }
            Group("WIDTH   [ ]", 56f, cards);
        }

        /// <summary>The size codes a runway this wide is the width for ("C", "D-E").</summary>
        static string RunwayCodes(float width)
        {
            char first = ' ', last = ' ';
            for (char c = 'A'; c <= 'F'; c++)
                if (Mathf.Abs(RunwayRules.RunwayWidthFor(c) - width) < 0.05f)
                {
                    if (first == ' ') first = c;
                    last = c;
                }
            return first == ' ' ? "?" : first == last ? first.ToString() : $"{first}-{last}";
        }

        static readonly int[] FloorChoices = { 1, 2, 3, 4, 6, 8 };

        void FloorCards(int current)
        {
            var cards = new List<Card>();
            foreach (int n in FloorChoices)
            {
                if (n > Terminal.MaxFloors) continue;
                int floors = n;
                cards.Add(new Card(r => BuildingPicture(r, floors), floors.ToString(), floors == 1 ? "floor" : "floors",
                    $"{floors} floor{(floors == 1 ? "" : "s")} ([ / ] for any up to {Terminal.MaxFloors})", current == floors, true,
                    () => airfield.StepFloors(floors - airfield.TerminalFloors)));
            }
            bool preset = Array.IndexOf(FloorChoices, current) >= 0;
            Group(preset ? "FLOORS   [ ]" : $"FLOORS   {current}   [ ]", 52f, cards.ToArray());
        }

        void CodeCards()
        {
            var cards = new Card[SizeCodes.Count];
            for (int i = 0; i < cards.Length; i++)
            {
                var c = (SizeCode)i;
                bool on = airfield.StandCode == c;
                float span = RunwayRules.MaxWingspan(c.Letter());
                cards[i] = new Card(r => AircraftPicture(r, span / RunwayRules.MaxWingspan('F'), on), c.Letter().ToString(), $"to {span:0} m",
                    CodeTips[i] + " ([ / ])", on, true, () => airfield.SetStandCode(c));
            }
            Group("SIZE CODE   [ ]", 52f, cards);
        }

        static readonly Color TaxiwayBlue = new Color(0.35f, 0.6f, 1f), RunwayWhite = new Color(1f, 0.95f, 0.8f),
            ThresholdGreen = new Color(0.4f, 1f, 0.5f), EndRed = new Color(1f, 0.3f, 0.25f), Unlit = new Color(0.32f, 0.33f, 0.35f),
            FogGrey = new Color(0.78f, 0.8f, 0.82f);

        /// <summary>The kinds of lights the lights tool puts down (TaxiwayEdge.lit, RunwayLighting), and Remove.</summary>
        void LightCards()
        {
            var k = airfield.LightKind;
            Group("TAXIWAY", 64f,
                new Card(r => LightsPicture(r, PavementSurface.Asphalt, true, TaxiwayBlue), "Edge", "blue",
                    "Blue edge lights along taxiway sections: at night aircraft taxi only where they're lit",
                    k == LightKind.TaxiwayEdge, true, () => airfield.SetLightKind(LightKind.TaxiwayEdge)));
            Group("RUNWAY", 64f,
                new Card(r => LightsPicture(r, PavementSurface.Concrete, false, RunwayWhite, ThresholdGreen, EndRed), "Basic", "night",
                    "White edge lights, green threshold and red end lights, and PAPI: the runway stays open at night",
                    k == LightKind.RunwayBasic, true, () => airfield.SetLightKind(LightKind.RunwayBasic)),
                new Card(r => ApproachPicture(r), "Precision", "fog",
                    "Basic plus centreline lights, approach lights with sequenced flashers and stop bars: for landing in " +
                    "fog and low cloud (low-visibility weather isn't in yet, so for now it works like basic)",
                    k == LightKind.RunwayPrecision, true, () => airfield.SetLightKind(LightKind.RunwayPrecision)));
            Group("REMOVE", 64f,
                new Card(r => LightsPicture(r, PavementSurface.Asphalt, true, Unlit), "Remove", "unlit",
                    "Take the lights out of taxiway sections and runways", k == LightKind.Remove, true,
                    () => airfield.SetLightKind(LightKind.Remove)));
        }

        /// <summary>The tray over the tool's button: groups of cards side by side, each under its caption (narrower cards
        /// on a narrow screen), and a tab in amber over the button it belongs to.</summary>
        void Tray(Rect button, Rect bar)
        {
            BuildTray();
            if (tray.Count == 0) return;
            float pad = U(8f), gap = U(4f), split = U(14f), caption = U(18f), cardH = U(80f);
            float units = 0f;
            int cards = 0;
            foreach (var (_, u, cs) in tray)
            {
                units += u * cs.Length;
                cards += cs.Length;
            }
            float fixedW = pad * 2f + split * (tray.Count - 1) + gap * (cards - tray.Count);
            float scale = Mathf.Min(DevGui.Scale, (Screen.width - U(24f) - fixedW) / units);
            float width = fixedW + units * scale;
            var r = new Rect(Mathf.Clamp(Mathf.Round(button.center.x - width * 0.5f), U(12f), Mathf.Max(U(12f), Screen.width - U(12f) - width)),
                bar.y - U(8f) - pad * 2f - caption - cardH, width, pad * 2f + caption + cardH);
            DevGui.Panel(r);
            DevGui.Fill(new Rect(button.x + U(10f), r.yMax - U(3f), button.width - U(20f), U(3f)), DevGui.GUI(DevGui.Accent), U(1.5f));
            float x = r.x + pad, y = r.y + pad;
            for (int g = 0; g < tray.Count; g++)
            {
                var (title, u, cs) = tray[g];
                if (g > 0)
                {
                    DevGui.Divider(x + split * 0.5f - gap, y + U(4f), caption + cardH - U(8f));
                    x += split - gap;
                }
                float cw = Mathf.Round(u * scale);
                GUI.Label(new Rect(x, y - U(2f), cs.Length * (cw + gap), caption), title, DevGui.Caption);
                foreach (var c in cs)
                {
                    var cr = new Rect(x, y + caption, cw, cardH);
                    bool clicked = c.picture == null
                        ? DevGui.Button(cr, c.label, c.tip, c.on, enabled: c.enabled)
                        : DevGui.Card(cr, c.picture, c.label, c.sub, c.tip, c.on, c.enabled);
                    if (clicked) c.act();
                    x += cw + gap;
                }
            }
        }

        // pictures for the cards (drawn at repaint into the card's dark well)

        /// <summary>The surface's own ground texture, a patch of it.</summary>
        Texture SurfaceTexture(PavementSurface s)
        {
            var m = airfield.airport.SurfaceMaterial(s);
            return m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
        }

        /// <summary>The surfaces' mean albedo (sRGB), as textures.py matches them (dirt, asphalt, concrete, grass).</summary>
        static readonly Color[] SurfaceColours =
        {
            new Color(0.47f, 0.40f, 0.31f), new Color(0.25f, 0.25f, 0.26f), new Color(0.63f, 0.62f, 0.59f), new Color(0.29f, 0.37f, 0.14f),
        };

        /// <summary>
        /// A patch of the surface's texture filling the rect, `texels` of it per pixel (so strips of different sizes
        /// show the same grain). In a linear-space project IMGUI shows an sRGB texture's values gamma-encoded once more
        /// (asphalt's 0.25 comes out 0.54), so it's tinted by its mean to the power 1.2, which brings the mean back.
        /// </summary>
        void Patch(Rect r, PavementSurface s, float texels = 0.006f)
        {
            var tex = SurfaceTexture(s);
            var mean = SurfaceColours[(int)s];
            if (tex == null)
            {
                DevGui.Fill(r, DevGui.GUI(mean));
                return;
            }
            var was = GUI.color;
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                GUI.color = new Color(Mathf.Pow(mean.r, 1.2f), Mathf.Pow(mean.g, 1.2f), Mathf.Pow(mean.b, 1.2f));
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(0.1f, 0.1f, r.width * texels / DevGui.Scale, r.height * texels / DevGui.Scale));
            GUI.color = was;
        }

        void Swatch(Rect r, PavementSurface s) => Patch(r, s, 0.008f);

        /// <summary>A strip of pavement across the rect on grass (shaded, so a grass strip still shows), `share` of the
        /// widest it gets, with its centreline (paved: yellow for a taxiway, white dashes for a runway).</summary>
        void Strip(Rect r, PavementSurface s, float share, bool taxiway)
        {
            Patch(r, PavementSurface.Grass);
            DevGui.Fill(r, new Color(0f, 0f, 0f, 0.45f), U(4f));
            float h = Mathf.Max(U(3f), Mathf.Round((r.height - U(8f)) * share));
            var band = new Rect(r.x, Mathf.Round(r.center.y - h * 0.5f), r.width, h);
            Patch(band, s);
            if (!s.IsPaved() || h < U(8f)) return;
            float line = Mathf.Max(1f, U(1.5f)), mid = Mathf.Round(band.center.y - line * 0.5f);
            if (taxiway)
                DevGui.Fill(new Rect(band.x, mid, band.width, line), DevGui.GUI(new Color(1f, 0.82f, 0.2f)));
            else
                for (float dx = U(3f); dx < band.width; dx += U(10f))
                    DevGui.Fill(new Rect(band.x + dx, mid, Mathf.Min(U(6f), band.width - dx), line), Color.white);
        }

        /// <summary>A strip of pavement with a row of lights along each edge (`edge`), and across its ends the
        /// threshold's (`start`, left) and the end's (`end`, right) if given.</summary>
        void LightsPicture(Rect r, PavementSurface s, bool taxiway, Color edge, Color? start = null, Color? end = null)
        {
            Strip(r, s, 0.5f, taxiway);
            float h = Mathf.Round((r.height - U(8f)) * 0.5f);
            float x0 = r.x + U(6f), x1 = r.xMax - U(6f);
            for (float x = x0; x <= x1 + 0.5f; x += (x1 - x0) / 5f)
            {
                Light(new Vector2(x, r.center.y - h * 0.5f), edge);
                Light(new Vector2(x, r.center.y + h * 0.5f), edge);
            }
            for (int i = 1; i < 4; i++)
            {
                float y = r.center.y - h * 0.5f + h * i / 4f;
                if (start != null) Light(new Vector2(x0, y), start.Value);
                if (end != null) Light(new Vector2(x1, y), end.Value);
            }
        }

        /// <summary>A runway's threshold with its centreline lights, and the approach lights running out before it (a
        /// centre row with crossbars), shining through a bank of fog: what precision lighting is for.</summary>
        void ApproachPicture(Rect r)
        {
            float mid = Mathf.Round(r.x + r.width * 0.55f);
            Patch(r, PavementSurface.Grass);
            DevGui.Fill(r, new Color(0f, 0f, 0f, 0.45f), U(4f));
            Strip(new Rect(mid, r.y, r.xMax - mid, r.height), PavementSurface.Concrete, 0.5f, false);
            // fog: thickest out on the approach, thinning over the runway
            var fog = DevGui.GUI(FogGrey);
            float step = Mathf.Max(1f, Mathf.Round(U(2f)));
            for (float x = r.x; x < r.xMax; x += step)
                DevGui.Fill(new Rect(x, r.y, Mathf.Min(step, r.xMax - x), r.height),
                    new Color(fog.r, fog.g, fog.b, Mathf.Lerp(0.6f, 0.08f, (x - r.x) / r.width)));
            float cy = r.center.y, h = Mathf.Round((r.height - U(8f)) * 0.5f);
            for (int i = 1; i < 4; i++) Light(new Vector2(mid + U(2f), cy - h * 0.5f + h * i / 4f), ThresholdGreen);
            for (float x = mid + U(10f); x < r.xMax - U(2f); x += U(8f)) Light(new Vector2(x, cy), RunwayWhite);
            for (float x = r.x + U(5f); x < mid - U(3f); x += U(6f)) Light(new Vector2(x, cy), RunwayWhite);
            for (float x = r.x + U(8f); x < mid - U(5f); x += U(12f))
                for (int k = -2; k <= 2; k++)
                    if (k != 0) Light(new Vector2(x, cy + k * U(4f)), RunwayWhite);
        }

        /// <summary>One light: a bright dot with a soft glow.</summary>
        static void Light(Vector2 at, Color colour)
        {
            float d = Mathf.Max(2f, U(3f));
            var c = DevGui.GUI(colour);
            var dot = new Rect(Mathf.Round(at.x - d * 0.5f), Mathf.Round(at.y - d * 0.5f), d, d);
            DevGui.Fill(new Rect(dot.x - d * 0.5f, dot.y - d * 0.5f, d * 2f, d * 2f), new Color(c.r, c.g, c.b, 0.3f), d);
            DevGui.Fill(dot, c, d * 0.5f);
        }

        /// <summary>A terminal of so many floors: glass bands under a roof, standing on the bottom of the rect.</summary>
        static void BuildingPicture(Rect r, int floors)
        {
            float floorH = Mathf.Min(U(6f), Mathf.Floor((r.height - U(10f)) / FloorChoices[FloorChoices.Length - 1]));
            float w = Mathf.Round(r.width * 0.62f), x = Mathf.Round(r.center.x - w * 0.5f), y = r.yMax - U(4f);
            var glass = DevGui.GUI(new Color(0.55f, 0.72f, 0.85f));
            var frame = DevGui.GUI(new Color(0.85f, 0.87f, 0.9f));
            DevGui.Fill(new Rect(x - U(3f), y, w + U(6f), Mathf.Max(1f, U(1f))), frame);
            for (int f = 0; f < floors; f++)
            {
                y -= floorH;
                DevGui.Fill(new Rect(x, y, w, floorH), frame);
                DevGui.Fill(new Rect(x + 1f, y + Mathf.Max(1f, U(1f)), w - 2f, floorH - Mathf.Max(1f, U(1f))), glass);
            }
            DevGui.Fill(new Rect(x - U(2f), y - U(2f), w + U(4f), U(2f)), frame);
        }

        /// <summary>An aircraft from above, sized by its share of the largest wingspan.</summary>
        static void AircraftPicture(Rect r, float share, bool on)
        {
            float s = Mathf.Round(Mathf.Lerp(0.3f, 1f, share) * (r.height - U(6f)));
            DevGui.DrawIcon(new Rect(Mathf.Round(r.center.x - s * 0.5f), Mathf.Round(r.center.y - s * 0.5f), s, s), DevGui.Icon("stand"),
                on ? new Color(0.1f, 0.08f, 0.04f) : new Color(0.91f, 0.93f, 0.96f));
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
        Vector2 sidebarScroll;

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
            DrawRows(rows, panel);
        }

        static void DrawRows(List<(float h, Action<Rect> draw)> rows, Rect panel)
        {
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
            if (e.type == EventType.MouseDown && openPop != null &&
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
            // Leave room for the scrollbar when a station's setup is expanded.
            float scrollbar = U(18f);
            innerWidth = colW - Pad * 2f - scrollbar;
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
            if (side.Count > 0)
            {
                float available = Mathf.Max(U(80f), Screen.height - margin - sideRect.y);
                if (sideRect.height <= available) { sidebarScroll = Vector2.zero; Draw(side, sideRect); }
                else
                {
                    var viewport = new Rect(sideRect.x, sideRect.y, sideRect.width, available);
                    // Cover only the visible panel in screen coordinates so wheel input stays off the world.
                    DevGui.Panel(viewport);
                    var content = new Rect(0f, 0f, sideRect.width - scrollbar, sideRect.height);
                    sidebarScroll = GUI.BeginScrollView(viewport, sidebarScroll, content, false, true);
                    DrawRows(side, content);
                    GUI.EndScrollView();
                }
            }
            if (repaint && pop.Count > 0) Draw(pop, popRect);
        }

        // ------------------------------------------------------------------ inspector

        void Inspector()
        {
            switch (airfield.Tool)
            {
                case DevTool.Runway:
                {
                    Title("Runway tool");
                    var placing = airfield.PlacingRunway;
                    if (placing != null) Note($"{DevGui.Tag(Cyan)}Runway {placing.Designation}, {placing.Length:0} m</color>");
                    if (airfield.RunwayProblem != null) Danger(airfield.RunwayProblem);
                    Note(placing == null ? "click where the runway starts, or drag it out   Shift: 10° and 50 m steps"
                        : "click where it ends   Shift: 10° and 50 m steps   right-click or Esc drops it");
                    return;
                }
                case DevTool.Taxiway:
                    Title("Taxiway tool");
                    if (airfield.RoadProblem != null) Danger(airfield.RoadProblem);
                    else Note(Joins("click points   Shift: 10° and 5 m steps   right-click ends   dashed: stands' front lines"));
                    return;
                case DevTool.Road:
                    Title("Road tool");
                    if (airfield.RoadProblem != null) Danger(airfield.RoadProblem);
                    else
                        Note(Joins($"service road, {RunwayRules.RoadWidth:0} m wide   click points   Shift: 10° and 5 m steps   " +
                                   "right-click ends   paint only across paved aprons"));
                    return;
                case DevTool.Apron:
                    Title("Apron tool");
                    if (airfield.ApronProblemText != null) Danger(airfield.ApronProblemText);
                    else
                        Note(Joins(airfield.ApronCornerCount == 0 ? "click corners, or drag out a rectangle   Shift: 10° and 5 m steps"
                            : "click the first corner or right-click to finish   Shift: 10° and 5 m steps   Esc drops it"));
                    return;
                case DevTool.Terminal:
                    Title("Terminal tool");
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
                    if (!bay) StandProps(false, airfield.Surface);   // a bay's are in the tray
                    if (airfield.GhostProblem != null) Danger(airfield.GhostProblem);
                    else if (airfield.GhostNote != null) Note($"{DevGui.Tag(Cyan)}{airfield.GhostNote}</color>");
                    Note(bay ? "click on a taxiway or anywhere   Shift+scroll: turn it"
                        : "click anywhere   Shift+scroll: turn it   Ctrl (Cmd): free, no snapping   Ctrl+drag: turn it to any angle");
                    return;
                }
                case DevTool.Bridge:
                    BridgeTool();
                    return;
                case DevTool.Lights:
                    LightsInspector();
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
                Buttons(B("Design building", () => OpenTerminalDesigner(airfield.SelectedTerminal), "Edit storey heights, facades and wall angles"));
                Space();
                Buttons(B("Delete terminal", () => airfield.DeleteSelection(), "Remove the terminal", hint: "Del", danger: true));
                if (airfield.ApronProblemText != null) Danger(airfield.ApronProblemText);
                else Note("Shift+scroll: bow the outlined wall out or in (Alt: finer)");
            }
            else if (airfield.SelectedStand != null) StandInspector(airfield.SelectedStand);
        }

        static readonly Color Cyan = new Color(0.3f, 0.9f, 1f), Green = new Color(0.55f, 1f, 0.55f);

        /// <summary>The lights tool: what a click does here, how much is lit, and lighting or clearing every taxiway.</summary>
        void LightsInspector()
        {
            Title("Lights");
            var k = airfield.LightKind;
            if (airfield.LightProblem != null) Danger(airfield.LightProblem);
            else if (airfield.LightNote != null) Note($"{DevGui.Tag(Cyan)}{airfield.LightNote}</color>");
            Note(k == LightKind.TaxiwayEdge ? "click or drag along taxiway sections   Shift: a section's whole run"
                : k == LightKind.Remove ? "click or drag over taxiway sections and runways   Shift: a section's whole run"
                : "click a runway");
            var (lit, all) = airfield.TaxiwayLighting;
            PropText("Taxiways", all < 1f ? "none paved" : $"{lit / 1000f:0.0} of {all / 1000f:0.0} km lit");
            var runways = airfield.airport.Runways;
            int litRunways = 0, paved = 0;
            foreach (var r in runways)
            {
                if (r.Paved) paved++;
                if (r.lighting != RunwayLighting.None) litRunways++;
            }
            PropText("Runways", runways.Count == 0 ? "none" : $"{litRunways} of {runways.Count} lit" + (paved < runways.Count ? $" ({paved} paved)" : ""));
            Buttons(B("Light all taxiways", () => airfield.SetAllTaxiwayLights(true), "Edge lights on every paved taxiway section"),
                B("Clear all", () => airfield.SetAllTaxiwayLights(false), "Every taxiway's lights out (runways keep theirs)"));
            Note("at night aircraft taxi only on lit taxiways and use only lit runways");
        }

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
            PropButtons("Lights", B(runway.lighting == RunwayLighting.Precision ? "Precision (fog)" : runway.lighting.ToString(),
                airfield.ToggleLighting, "None / basic / precision lighting (unlit runways close at night; precision is for fog " +
                "and low cloud)", runway.lighting != RunwayLighting.None, "L", runway.Paved));
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
            // A placed stand keeps the size and exit it was built with: shown, not offered.
            bool fixedStand = airfield.StandFixed;
            if (fixedStand) PropText("Code", airfield.StandCode.Letter().ToString());
            else
            {
                var codes = new Btn[SizeCodes.Count];
                for (int i = 0; i < codes.Length; i++)
                {
                    var c = (SizeCode)i;
                    codes[i] = B(c.Letter().ToString(), () => airfield.SetStandCode(c), CodeTips[i] + " ([ / ])", airfield.StandCode == c);
                }
                PropButtons("Code", codes);
            }
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
            if (fixedStand) PropText("Exit", airfield.StandExit == StandExit.PushBack ? "Push-back" : "Taxi-out");
            else PropButtons("Exit", B("Push-back", () => airfield.SetStandExit(StandExit.PushBack), "Nose in; a tug pushes the aircraft back out (I)",
                    airfield.StandExit == StandExit.PushBack),
                B("Taxi-out", () => airfield.SetStandExit(StandExit.TaxiOut),
                    "Nose out: the aircraft swings round on the way in and taxis out itself; needs about twice the room (I)",
                    airfield.StandExit == StandExit.TaxiOut));
            PropButtons("Light a/c", B("Take", () => airfield.SetStandNoSmall(false), "Light aircraft (under 5.7 t, such as the Starling A4) may park here",
                    !airfield.StandNoSmall),
                B("Turn away", () => airfield.SetStandNoSmall(true), "Turn light aircraft away: keep it for bigger aircraft", airfield.StandNoSmall));
        }

        void StandInspector(Stand st)
        {
            Title(airfield.SelectionName);
            if (!st.DeIcing)
            {
                Buttons(B("Edit gate", () => EditParking(st), "Parking, bridges, appearance and aircraft in one workspace"));
                return;
            }
            StandProps(true, st.surface);
            Space();
            Buttons(B("Delete bay", airfield.DeleteSelectedStand, "Remove this de-icing bay", danger: true));
        }

        void BridgeTool()
        {
            Title("Gate designer");
            Buttons(B("Build or edit bridges", OpenBridgeBuilder, "Choose sections and appearance, then place or select a bridge"));
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
            AircraftBaggage(a);
            AircraftContainers(a);
            var flight = traffic != null ? traffic.SelectedFlight : null;
            if (a.type != null && a.type.cargoDoors.Length > 0)
                Buttons(B("Open cargo", () => a.CargoDoors.SetOpen(true), "Open the cargo hatches for inspection", enabled: !a.Moving && flight == null && sandbox.SelectedTow == null),
                    B("Close cargo", () => a.CargoDoors.SetOpen(false), "Close the cargo hatches; active baggage service controls its own door"));
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
        static readonly HashSet<string> folded = new HashSet<string> { "baggage-setup", "container-setup" };

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
                BaggageSidebar(airport);
                ContainerSidebar(airport);
                if (traffic != null && Fold("traffic", $"TRAFFIC   wind from {traffic.Wind:000}°"))
                {
                    var lands = new List<Btn>();
                    foreach (var t in traffic.Types)
                    {
                        var type = t;
                        lands.Add(B("Land " + (type.maxTakeoffMass < 5700f ? "light" : "jet"), () => traffic.Land(type),
                            $"A {type.TypeName} comes in on final for the runway in use and taxis to a free stand"));
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
