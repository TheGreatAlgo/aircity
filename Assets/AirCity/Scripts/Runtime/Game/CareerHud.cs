using System.Collections.Generic;
using AirCity.Dev;
using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Game
{
    /// <summary>Career objectives and earned tools, using the same visual controls as the sandbox.</summary>
    [RequireComponent(typeof(CareerGame))]
    public partial class CareerHud : MonoBehaviour
    {
        public Material guideMaterial;
        CareerGame game;
        CareerBuildTool builder;
        GUIStyle body, railText, railBold, railSmall, railCaption, railTitle, railButton, railButtonOn, railInline, railBrand, railBalance;
        float styleScale;
        readonly List<Rect> hitAreas = new List<Rect>();
        readonly List<CareerTool> tools = new List<CareerTool>();
        string unlockedMessage, observedMessage;
        RegionBrowser regionBrowser;
        bool newWorldDialog, gameMenu, clearFocus, dismissMenuPointer, visible = true;
        int previousUnlocks;
        float unlockUntil, messageUntil;
        Vector2 scroll;
        LineRenderer boundary;
        ValleyWorld drawnWorld;
        int drawnSite = -2;
        RegionPreset selectedPreset;
        static readonly string[] PresetNotes = { "Forests & river valleys", "Peaks & rocky cliffs", "Warm lush highlands", "Mesas & dry canyons" };
        static readonly string[] PresetIcons = { "clear-trees", "level-ground", "lights", "level-ground" };
        static readonly Color[] PresetColours = { new Color(.42f, .72f, .49f), new Color(.67f, .76f, .84f), new Color(.29f, .82f, .73f), new Color(.91f, .65f, .36f) };
        static readonly Color Rail = new Color(.065f, .10f, .13f, .98f), RailEdge = new Color(.23f, .31f, .35f), RailWell = new Color(.12f, .18f, .21f);
        static float HeaderHeight => U(Screen.width < U(680) ? 72 : 40);
        public static float ContentTop => HeaderHeight + U(12);
        static readonly string[] Icons = { "select", "clear-trees", "level-ground", "runway", "taxiway", "stand", "road", "terminal" };
        static readonly string[] Labels = { "Select", "Clear land", "Level ground", "Runway", "Taxiway", "Parking", "Public road", "Reception" };
        static readonly int[] Groups = { 0, 1, 1, 2, 2, 2, 3, 3 };
        static float U(float units) => DevGui.U(units);

        void Awake() { game = GetComponent<CareerGame>(); builder = GetComponent<CareerBuildTool>(); regionBrowser = new RegionBrowser(game); }
        void OnDestroy() { regionBrowser?.Dispose(); }
        void Update()
        {
            if (game.State == null) return;
            if (builder == null) builder = GetComponent<CareerBuildTool>();
            if (game.SharedTools) visible = DevGui.Visible;
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.escapeKey.wasPressedThisFrame)
                { gameMenu = newWorldDialog = false; regionBrowser?.Close(); game.view.KeyboardCaptured = false; clearFocus = true; }
                if (!game.SharedTools && keys.hKey.wasPressedThisFrame && !game.view.KeyboardCaptured && !newWorldDialog)
                { visible = !visible; hitAreas.Clear(); }
            }
            var mouse = Mouse.current;
            var p = mouse == null ? Vector2.zero : new Vector2(mouse.position.x.ReadValue(), Screen.height - mouse.position.y.ReadValue());
            if (mouse == null || !mouse.leftButton.isPressed) dismissMenuPointer = false;
            game.view.PointerOverHud = visible && (newWorldDialog || gameMenu || regionBrowser.IsOpen || dismissMenuPointer || hitAreas.Exists(r => r.Contains(p)) || DevGui.PointerOverHud);
            if (!visible) game.view.KeyboardCaptured = false;
            int unlocks = game.State.layout != null ? game.State.layout.unlocked : 0;
            if (drawnWorld != game.State.world)
            {
                previousUnlocks = unlocks; unlockedMessage = observedMessage = null; unlockUntil = messageUntil = 0f;
                selectedPreset = game.RegionChoice;
            }
            if (previousUnlocks != 0 && unlocks != previousUnlocks)
            {
                for (int i = 0; i < Labels.Length; i++)
                    if ((unlocks & (1 << i)) != 0 && (previousUnlocks & (1 << i)) == 0) unlockedMessage = Labels[i] + " unlocked";
                unlockUntil = Time.unscaledTime + 5f;
            }
            previousUnlocks = unlocks;
            if (observedMessage != game.Message) { observedMessage = game.Message; messageUntil = Time.unscaledTime + 6f; }
            int site = game.State.Site != null ? game.State.selectedSite : game.SurveyedSite;
            if (drawnWorld != game.State.world || drawnSite != site) { drawnWorld = game.State.world; drawnSite = site; DrawBoundary(); }
        }

        void OnGUI()
        {
            if (game == null || game.State == null || !visible) return;
            GUI.depth = -20;
            DevGui.Begin(); Styles();
            if (clearFocus) { GUI.FocusControl(null); clearFocus = false; }
            if (Event.current.type == EventType.Repaint) hitAreas.Clear();
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !newWorldDialog && !regionBrowser.IsOpen;
            SessionControls();
            GUI.enabled = enabled && !newWorldDialog && !regionBrowser.IsOpen;
            if (game.State.Site == null) SiteSelection();
            else { BuildWorldQuote(); if (!game.Sandbox) Objective(); if (!game.SharedTools) Toolbar(); Notice(); }
            GUI.enabled = enabled && !newWorldDialog && !regionBrowser.IsOpen;
            if (gameMenu) SessionMenu();
            GUI.enabled = enabled;
            if (newWorldDialog) NewWorldDialog();
            regionBrowser.Draw();
            game.view.KeyboardCaptured = newWorldDialog || regionBrowser.IsOpen || BrushKeyboardCaptured;
            DevGui.KeyboardCaptured = game.view.KeyboardCaptured;
            DevGui.Tooltip();
        }

        Rect OptionsButtonRect => new Rect(Screen.width - U(92), U(Screen.width < U(680) ? 39 : 7), U(80), U(26));
        Rect OptionsMenuRect => new Rect(Mathf.Max(U(8), Screen.width - U(244)), HeaderHeight + U(6), U(232), U(game.State.Site != null ? 178 : 146));

        void SessionControls()
        {
            bool narrow = Screen.width < U(680);
            var options = OptionsButtonRect;
            if (gameMenu && Event.current.type == EventType.MouseDown && !options.Contains(Event.current.mousePosition) &&
                !OptionsMenuRect.Contains(Event.current.mousePosition))
            {
                gameMenu = false; dismissMenuPointer = true; Event.current.Use();
            }
            var r = new Rect(0, 0, Screen.width, HeaderHeight); DarkPanel(r, false);
            DevGui.Fill(new Rect(0, r.yMax - U(1), r.width, U(1)), DevGui.GUI(DevGui.Accent));
            float x = U(12), y = U(6);
            if (Screen.width >= U(960))
            {
                GUI.Label(new Rect(x, y, U(112), U(28)), "A I R C I T Y", railBrand);
                x += U(124); RailDivider(x - U(8), y);
            }
            var date = CareerClock.Date(game.State.operatingSeconds);
            string calendar = date.ToString(narrow ? "dd MMM yy" : "dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            string dim = DevGui.Tag(new Color(.57f, .68f, .71f));
            GUI.Label(new Rect(x, y, U(144), U(28)), new GUIContent($"<b>{date:HH:mm}</b>  {dim}{calendar}</color>",
                "Simulation time and date"), railInline);
            x += U(156); RailDivider(x - U(8), y);
            GUI.Label(new Rect(x, y, U(104), U(28)), new GUIContent(
                $"<b>{CareerClock.Temperature(game.State.world, game.State.operatingSeconds):0}°C</b>  {dim}{CareerClock.Season(game.State.operatingSeconds)}</color>",
                "Regional temperature and season"), railInline);
            x += U(116);
            if (narrow) { x = U(12); y = U(39); }
            else { RailDivider(x - U(8), y); y = U(7); }
            bool active = game.State.Site != null;
            if (RailButton(new Rect(x, y, U(50), U(26)), game.Paused ? "Play" : "Pause", game.Paused, active,
                "Pause or resume the simulation")) game.Paused = !game.Paused;
            for (int i = 0; i < 3; i++)
            {
                int speed = i == 0 ? 1 : i == 1 ? 5 : 20;
                if (RailButton(new Rect(x + U(55 + i * 36), y, U(31), U(26)), speed + "×", !game.Paused && game.Speed == speed, active))
                { game.Speed = speed; game.Paused = false; }
            }
            float fundsX = narrow ? Screen.width - U(122) : options.x - U(122);
            GUI.Label(new Rect(fundsX, U(6), U(110), U(28)), new GUIContent(Money(game.State.funds),
                game.Sandbox ? "Sandbox · construction is free" : "Airport funds"), railBalance);
            if (RailButton(options, "Options ▾", gameMenu, tip: "Save, open and game options")) gameMenu = !gameMenu;
        }

        void SessionMenu()
        {
            var menu = OptionsMenuRect; DarkPanel(menu, opaque: true);
            float x = menu.x + U(8), y = menu.y + U(8), width = menu.width - U(16);
            if (RailButton(new Rect(x,y,width,U(26)), "Save", enabled: game.State.Site != null))
            { game.Save(); gameMenu = false; }
            y += U(32);
            if (RailButton(new Rect(x,y,width,U(26)), "Open saved airport", enabled: game.HasSave,
                tip: "Load the latest saved airport"))
            { if (game.Continue()) gameMenu = false; }
            y += U(32);
            DevGui.Fill(new Rect(x,y-U(1),width,U(1)),DevGui.GUI(RailEdge)); y += U(4);
            bool active = game.State.Site != null;
            if (RailButton(new Rect(x,y,width,U(26)), active ? "Focus airport" : "View entire region"))
            { if(active)game.Focus();else game.view.Region(); gameMenu=false; }
            y += U(32);
            if (active)
            {
                if (RailButton(new Rect(x,y,width,U(26)), "New game")) { newWorldDialog = true; gameMenu = false; }
                y += U(32);
            }
            if (RailButton(new Rect(x,y,width,U(26)), game.Sandbox ? "Switch to Career" : "Switch to Sandbox"))
            { game.SetMode(game.Sandbox ? GameMode.Career : GameMode.Sandbox); gameMenu = false; }
        }

        void SiteSelection()
        {
            float width = Mathf.Min(U(344), Screen.width - U(24));
            var r = new Rect(U(16), HeaderHeight + U(16), width, Mathf.Min(U(game.HasSave ? 690 : 652), Screen.height - HeaderHeight - U(32)));
            DarkPanel(r);
            GUILayout.BeginArea(new Rect(r.x + U(18), r.y + U(16), r.width - U(36), r.height - U(32)));
            scroll = GUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Label("YOUR NEXT AIRPORT", railCaption); GUILayout.Space(U(4));
            GUILayout.Label("Choose your location", railTitle); GUILayout.Space(U(8));
            GUILayout.Label("Start with an offline location or find a place on the globe. Build your airport anywhere in the region.", railText); GUILayout.Space(U(10));
            if (RailLayoutButton("Choose on globe  →", true, height: 36)) { regionBrowser.Open(); GUI.FocusControl(null); }
            GUILayout.Space(U(5));
            float cardWidth = (r.width - U(44)) * .5f;
            for (int row = 0; row < 2; row++)
            {
                var rowRect = GUILayoutUtility.GetRect(U(100), U(74), GUILayout.ExpandWidth(true));
                for (int col = 0; col < 2; col++)
                {
                    int i = row * 2 + col;
                    var card = new Rect(rowRect.x + col * (cardWidth + U(8)), rowRect.y, cardWidth, U(68));
                    var location = RegionLocations.Curated[i];
                    bool chosen = game.State.world.hasCoordinates &&
                        System.Math.Abs(game.State.world.latitude - location.latitude) < .00001 &&
                        System.Math.Abs(game.State.world.longitude - location.longitude) < .00001;
                    if (RailButton(card, "", chosen, tip: PresetNotes[i], cover: false)) SelectPreset((RegionPreset)i);
                    DevGui.DrawIcon(new Rect(card.x + U(10), card.y + U(9), U(27), U(27)), DevGui.Icon(PresetIcons[i]), PresetColours[i]);
                    GUI.Label(new Rect(card.x + U(10), card.y + U(37), card.width - U(16), U(22)), RegionLocations.Curated[i].name, chosen ? railButtonOn : railBold);
                }
            }
            GUILayout.Space(U(8));
            string title = game.State.world.PresetName + $"  /  {ValleyWorld.Size / 1000f:0.0} × {ValleyWorld.Size / 1000f:0.0} km";
            GUILayout.Label(title.ToUpperInvariant(), railCaption); GUILayout.Space(U(5));
            GUILayout.Label(PresetDescription(game.State.world.preset), railText);
            GUILayout.Label(new GUIContent("REAL ELEVATION · " + game.State.world.landscapeSource, "Terrain: Mapzen Terrain Tiles. Adapted to the game region.\n© ESA WorldCover project 2021 / Contains modified Copernicus Sentinel data (2021) processed by ESA WorldCover consortium. CC-BY-4.0."), railSmall);
            if (game.State.world.hasCoordinates) GUILayout.Label(RegionLocations.Coordinates(game.State.world.latitude, game.State.world.longitude), railSmall);
            GUILayout.Space(U(12));
            GUILayout.Label("ENTIRE REGION UNLOCKED", railCaption); GUILayout.Space(U(4));
            GUILayout.Label("Build anywhere. Longer road connections and tougher ground cost more to develop.", railText);
            GUILayout.Space(U(12));
            if (RailLayoutButton("Start building  →", true, height: 38))
            { GUI.FocusControl(null); if (game.HasSave) newWorldDialog = true; else game.BeginRegion(); }
            if (game.HasSave && RailLayoutButton("Continue saved airport")) { game.Continue(); GUI.FocusControl(null); }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        void SelectPreset(RegionPreset preset)
        {
            selectedPreset = preset;
            int seed = preset switch { RegionPreset.Mountains => 873241, RegionPreset.Tropical => 445090, RegionPreset.Desert => 692835,
                RegionPreset.Random => NewSeed(), _ => 19770616 };
            game.NewWorld(seed, preset); GUI.FocusControl(null);
        }
        int NewSeed()
        {
            int seed = (int)(System.DateTime.UtcNow.Ticks & 0x7fffffff);
            return seed == game.State.world.seed ? unchecked(seed + 1) : seed;
        }
        static string PresetDescription(RegionPreset preset) => preset switch {
            RegionPreset.Mountains => "Real granite valley walls, wooded foothills and a winding road through the lowlands.",
            RegionPreset.Tropical => "A volcanic island, turquoise lagoon and ocean beaches. Build your island airport.",
            RegionPreset.Desert => "Real redrock canyon country, open desert and rugged sandstone outcrops.",
            _ => "A wooded river valley with open floodplain, low forested hills and room for a regional airport." };

        void Objective()
        {
            var step = game.State.Step;
            bool flights = step == CareerStep.FirstCharter || step == CareerStep.Operating || step == CareerStep.Expansion;
            var r = new Rect(U(12), HeaderHeight + U(12), U(286), U(flights ? 88 : 54)); Panel(r);
            GUI.Label(new Rect(r.x + U(12), r.y + U(7), r.width - U(24), U(14)), "NEXT TASK", DevGui.Caption);
            GUI.Label(new Rect(r.x + U(12), r.y + U(24), r.width - U(24), U(22)),
                new GUIContent(TaskTitle(step), TaskHint(step)), DevGui.Title);
            if (game.State.charterActive)
                GUI.Label(new Rect(r.x + U(12), r.y + U(52), r.width - U(24), U(27)), game.FlightStatus, body);
            else if (flights && Button(new Rect(r.x + U(10), r.y + U(52), r.width - U(20), U(26)),
                "Schedule local charter", enabled: game.State.funds >= CareerState.CharterCost)) game.AcceptCharter();
        }

        void Toolbar()
        {
            if (builder == null || game.State.layout == null) return;
            tools.Clear(); int groups = 0;
            for (int i = 0; i < Labels.Length; i++)
                if (game.State.layout.Unlocked((CareerTool)i))
                {
                    if (tools.Count > 0 && Groups[(int)tools[tools.Count - 1]] != Groups[i]) groups++;
                    tools.Add((CareerTool)i);
                }
            if (tools.Count == 0) return;
            float gap = U(4), pad = U(7), split = U(14), tile = U(48);
            float fixedWidth = pad * 2f + (tools.Count - 1 - groups) * gap + groups * split;
            tile = Mathf.Min(tile, Mathf.Floor((Screen.width - U(24) - fixedWidth) / tools.Count));
            float width = fixedWidth + tools.Count * tile;
            var bar = new Rect(Mathf.Round((Screen.width - width) * .5f), Screen.height - tile - pad * 2f - U(14), width, tile + pad * 2f);
            Panel(bar);
            float x = bar.x + pad, y = bar.y + pad; Rect active = default;
            for (int index = 0; index < tools.Count; index++)
            {
                int i = (int)tools[index];
                if (index > 0 && Groups[(int)tools[index - 1]] != Groups[i])
                { x -= gap; DevGui.Divider(x + split * .5f, y + U(8), tile - U(16)); x += split; }
                var b = new Rect(x, y, tile, tile);
                bool selected = builder.Tool == tools[index];
                if (selected) active = b;
                if (DevGui.IconButton(b, DevGui.Icon(Icons[i]), Labels[i], $"<b>{Labels[i]}</b>   {CareerBuildTool.Hint(tools[index])}",
                    selected, hint: tools[index] == CareerTool.Select ? "Esc" : null))
                    builder.Choose(selected ? CareerTool.Select : tools[index]);
                x += tile + gap;
            }
            if (builder.Tool != CareerTool.Select && active.width > 0f) ToolTray(active, bar);
        }

        public void SharedToolTray(Rect active, Rect bar) => ToolTray(active, bar);

        void ToolTray(Rect active, Rect bar)
        {
            bool brush = builder.Tool == CareerTool.ClearTrees || builder.Tool == CareerTool.Level;
            if (brush) { BrushTray(active, bar); return; }
            ResetHeightHold();
            bool finish = builder.Tool == CareerTool.Taxiway && builder.Draft.Count >= 2;
            string status = builder.Tool == CareerTool.Road ? game.GetComponent<CareerRoadTool>().Status : builder.Status;
            status ??= CareerBuildTool.Hint(builder.Tool);
            float width = Mathf.Min(U(520), Screen.width - U(24)), inner = width - U(24);
            float textHeight = Mathf.Max(U(28), body.CalcHeight(new GUIContent(status), inner));
            float height = U(40) + textHeight + (finish || builder.SupportsPlacementRotation ? U(34) : 0f);
            var r = new Rect(Mathf.Clamp(active.center.x - width * .5f, U(12), Screen.width - width - U(12)), bar.y - U(8) - height, width, height);
            Panel(r);
            GUI.Label(new Rect(r.x + U(12), r.y + U(9), inner, U(18)), Labels[(int)builder.Tool], DevGui.Title);
            GUI.Label(new Rect(r.x + U(12), r.y + U(31), inner, textHeight), status, body);
            float x = r.x + U(12), y = r.yMax - U(34);
            if (finish && Button(new Rect(x, y, inner, U(26)), "Finish taxiway", tip: "Build the drawn taxiway · Enter")) builder.FinishTaxiway();
            else if (builder.SupportsPlacementRotation)
            {
                GUI.Label(new Rect(x, y, U(38), U(26)), "Turn", DevGui.Small);
                if (Button(new Rect(x + U(40), y, U(30), U(26)), "−", tip: "Turn 15° left")) builder.RotatePlacement(-15);
                GUI.Label(new Rect(x + U(78), y, U(48), U(26)), $"{builder.PlacementHeading:0}°", DevGui.Bold);
                if (Button(new Rect(x + U(129), y, U(30), U(26)), "+", tip: "Turn 15° right")) builder.RotatePlacement(15);
                if (Button(new Rect(x + U(169), y, U(88), U(26)), builder.PlacementSnap ? "Snap 15°" : "Free", builder.PlacementSnap,
                    tip: "Toggle angle snapping. Shift+wheel turns; Alt gives 1° steps."))
                {
                    builder.PlacementSnap = !builder.PlacementSnap;
                    if (builder.PlacementSnap) builder.RotatePlacement(0);
                }
                GUI.Label(new Rect(x + U(268), y, inner - U(268), U(26)), "Z · Shift+wheel", DevGui.Small);
            }
        }

        void Notice()
        {
            string text = Time.unscaledTime < unlockUntil ? unlockedMessage : Time.unscaledTime < messageUntil ? observedMessage : null;
            if (string.IsNullOrEmpty(text)) return;
            float width = U(286), height = body.CalcHeight(new GUIContent(text), width - U(24)) + U(16);
            float y = HeaderHeight + U(game.State.IsOpen ? 108 : 74);
            var r = new Rect(U(12), y, width, height); Panel(r);
            GUI.Label(new Rect(r.x + U(12), r.y + U(8), width - U(24), height - U(16)), text, body);
        }

        void NewWorldDialog()
        {
            var full = new Rect(0, 0, Screen.width, Screen.height); Cover(full);
            DevGui.Fill(full, DevGui.GUI(new Color(0f, 0f, 0f, .6f)));
            float width = Mathf.Min(U(420), Screen.width - U(24));
            var r = new Rect((Screen.width - width) * .5f, (Screen.height - U(210)) * .5f, width, U(210)); Panel(r);
            GUI.Label(new Rect(r.x + U(18), r.y + U(18), r.width - U(36), U(24)), "Start a new airport?", DevGui.Title);
            GUI.Label(new Rect(r.x + U(18), r.y + U(57), r.width - U(36), U(77)),
                game.State.Site == null ? "Starting this region replaces the current saved airport. The previous save stays as a backup." :
                "Your airport will be saved before you return to region selection.", body);
            float buttonWidth = (r.width - U(44)) * .5f;
            if (Button(new Rect(r.x + U(18), r.yMax - U(48), buttonWidth, U(30)), "Keep playing")) newWorldDialog = false;
            if (Button(new Rect(r.x + U(26) + buttonWidth, r.yMax - U(48), buttonWidth, U(30)),
                game.State.Site == null ? "Start building" : "Choose region"))
            {
                if (game.State.Site == null) game.BeginRegion(); else if (game.Save()) game.NewWorld(game.State.world.seed, selectedPreset);
                newWorldDialog = false;
            }
        }

        void DrawBoundary()
        {
            if (game.State.wholeRegion)
            {
                if (boundary != null) boundary.positionCount = 0;
                return;
            }
            if (boundary == null)
            {
                var go = new GameObject("Airport land boundary"); go.transform.SetParent(transform, false);
                boundary = go.AddComponent<LineRenderer>(); boundary.sharedMaterial = guideMaterial; boundary.useWorldSpace = true;
                boundary.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var area = game.State.world.sites[drawnSite].Parcel;
            var corners = new[] { new Vector2(area.xMin, area.yMin), new Vector2(area.xMin, area.yMax), new Vector2(area.xMax, area.yMax), new Vector2(area.xMax, area.yMin) };
            var points = new List<Vector3>();
            for (int side = 0; side < 4; side++) for (int i = 0; i < 48; i++)
            { var p = Vector2.Lerp(corners[side], corners[(side + 1) % 4], i / 48f); points.Add(new Vector3(p.x, game.State.world.Height(p) + .7f, p.y)); }
            points.Add(points[0]); boundary.positionCount = points.Count; boundary.SetPositions(points.ToArray()); boundary.widthMultiplier = 1.1f;
        }
        static string TaskTitle(CareerStep step) => step switch {
            CareerStep.ClearTrees => "Clear some space", CareerStep.GradeLand => "Prepare your ground", CareerStep.Runway => "Build a runway",
            CareerStep.Parking => "Connect aircraft parking", CareerStep.AccessRoad => "Connect to the valley", CareerStep.Reception => "Build passenger reception",
            CareerStep.FirstCharter => "Welcome your first flight", CareerStep.Expansion => "Grow your airfield", _ => "Open for business" };
        static string TaskHint(CareerStep step) => step switch {
            CareerStep.ClearTrees => "Choose where to begin. Select trees or brush away a patch of woodland.",
            CareerStep.GradeLand => "Level a patch with the terrain brush. Clear only the land your layout needs.",
            CareerStep.Runway => "Drag a grass runway of at least 700 m. Its position and direction are yours.",
            CareerStep.Parking => "Connect a taxiway near a runway end, then place parking beside the taxiway.",
            CareerStep.AccessRoad => "Clear a route and draw your public road from the airport to the valley road.",
            CareerStep.Reception => "Place a small reception beside the airport end of your road.",
            CareerStep.FirstCharter => "Your airport is ready. Host a local charter and earn its service fee.",
            CareerStep.Expansion => "Keep earning from charters. Clear and level new land, then drag a threshold to extend.",
            _ => "Serve local charters and keep shaping your airport with the tools below." };
        void Styles()
        {
            if (body != null && Mathf.Approximately(styleScale, DevGui.Scale)) return;
            styleScale = DevGui.Scale;
            body = new GUIStyle(DevGui.Label) { wordWrap = true };
            GUIStyle RailStyle(GUIStyle source, Color colour) => new GUIStyle(source) {
                normal = { textColor = DevGui.GUI(colour) }, hover = { textColor = DevGui.GUI(colour) },
                active = { textColor = DevGui.GUI(colour) } };
            var white = new Color(.92f, .95f, .94f); var muted = new Color(.57f, .68f, .71f);
            railText = RailStyle(body, new Color(.72f, .79f, .80f));
            railBold = RailStyle(DevGui.Bold, white); railSmall = RailStyle(DevGui.Small, muted);
            railCaption = RailStyle(DevGui.Caption, muted); railTitle = RailStyle(DevGui.Title, white);
            railInline = RailStyle(DevGui.Label, white); railInline.fontSize = Mathf.RoundToInt(U(13));
            railBrand = RailStyle(DevGui.Bold, white); railBrand.fontSize = Mathf.RoundToInt(U(14));
            railBalance = RailStyle(DevGui.Bold, white); railBalance.fontSize = Mathf.RoundToInt(U(14)); railBalance.alignment = TextAnchor.MiddleRight;
            railButton = RailStyle(DevGui.Bold, white); railButton.fontSize = Mathf.RoundToInt(U(12)); railButton.alignment = TextAnchor.MiddleCenter;
            railButtonOn = RailStyle(railButton, Rail); railButtonOn.alignment = TextAnchor.MiddleLeft;
        }
        void DarkPanel(Rect r, bool rounded = true, bool opaque = false)
        {
            Cover(r); float radius = rounded ? U(10) : 0f;
            DevGui.Fill(new Rect(r.x, r.y + U(4), r.width, r.height), DevGui.GUI(new Color(0, 0, 0, .22f)), radius);
            var fill = Rail; if (opaque) fill.a = 1f;
            DevGui.Fill(r, DevGui.GUI(fill), radius);
            if (rounded) DevGui.Outline(r, DevGui.GUI(RailEdge), radius);
        }
        void RailDivider(float x, float y) => DevGui.Fill(new Rect(x, y + U(5), U(1), U(18)), DevGui.GUI(RailEdge));
        bool RailLayoutButton(string text, bool selected = false, bool enabled = true, string tip = null, float height = 30)
        {
            var r = GUILayoutUtility.GetRect(U(100), U(height), GUILayout.ExpandWidth(true));
            bool clicked = RailButton(r, text, selected, enabled, tip, cover: false); GUILayout.Space(U(5)); return clicked;
        }
        bool RailButton(Rect r, string text, bool selected = false, bool enabled = true, string tip = null, bool cover = true)
        {
            if (cover) Cover(r);
            bool was = GUI.enabled; GUI.enabled = was && enabled;
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
            var fill = selected ? DevGui.Accent : hover ? new Color(.19f, .28f, .31f) : RailWell;
            if (!GUI.enabled) fill = Color.Lerp(fill, Rail, .6f);
            DevGui.Fill(r, DevGui.GUI(fill), U(5));
            DevGui.Outline(r, DevGui.GUI(selected ? DevGui.Accent : RailEdge), U(5));
            var style = selected ? railButtonOn : railButton;
            var label = new Rect(r.x + U(4), r.y, r.width - U(8), r.height);
            if (selected) { var centered = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter }; GUI.Label(label, text, centered); }
            else GUI.Label(label, text, style);
            GUI.enabled = was; return clicked;
        }
        void Cover(Rect r) { if (Event.current.type == EventType.Repaint) hitAreas.Add(r); DevGui.Cover(r); }
        void Panel(Rect r) { Cover(r); DevGui.Panel(r); }
        bool LayoutButton(string text, bool selected = false, bool enabled = true)
        {
            var r = GUILayoutUtility.GetRect(U(100), U(28), GUILayout.ExpandWidth(true));
            // These rects are local to BeginArea; the enclosing site panel already covers their screen area.
            bool result = DevGui.Button(r, text, on: selected, enabled: enabled); GUILayout.Space(U(5)); return result;
        }
        bool Button(Rect r, string text, bool selected = false, bool enabled = true, string tip = null)
        { Cover(r); return DevGui.Button(r, text, tip, selected, enabled: enabled); }
        static string Money(int value) => "$" + value.ToString("N0");
        void OnDisable()
        {
            ResetHeightHold();
            hitAreas.Clear();
            if (game != null && game.view != null) game.view.PointerOverHud = game.view.KeyboardCaptured = false;
        }
    }
}
