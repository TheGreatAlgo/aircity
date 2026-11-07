using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace AirCity.Dev
{
    public sealed partial class DevGateDesigner
    {
        readonly GateLayoutHandles handles = new GateLayoutHandles();
        public GateLayoutHandles Handles => handles;
        int buildTarget, exteriorPage, facadeFace = -1;
        bool familyPicker;
        string previousFocus = "";
        readonly Dictionary<string, (string text, Action<float> change)> numberFields = new Dictionary<string, (string, Action<float>)>();
        static float U(float value) => GateDesignerGui.U(value);
        JetBridge SelectedBridge => Stand == null || Stand.bridges.Count == 0 ? null : Stand.bridges[Mathf.Clamp(bridgeIndex, 0, Stand.bridges.Count - 1)];
        BridgeHead SelectedHead => Stand?.BridgeHeads.ElementAtOrDefault(headIndex);
        Terminal SelectedTerminal => Airport?.terminals.ElementAtOrDefault(terminalIndex);

        void SyncSelection(DevAirfieldTool airfield)
        {
            tool = airfield;
            if (tool.SelectedStand != null && tool.SelectedStand != Stand)
            { Stand = tool.SelectedStand; Aircraft = DefaultAircraft(); bridgeIndex = headIndex = 0; drafts.Clear(); preview.Dispose(); }
            if (Stand != null && !Airport.Taxiways.stands.Contains(Stand)) { Stand = null; NewBridgeMode = true; preview.Dispose(); }
            if (Stand != null && tool.SelectedBridge != null && Stand.bridges.Contains(tool.SelectedBridge))
                bridgeIndex = Stand.bridges.IndexOf(tool.SelectedBridge);
        }
        void BindHandles()
        {
            bool enabled = IsOpen && DevGui.Visible && (tab == 0 || tab == 2) && tool != null && tool.Tool == DevTool.Select && Stand != null && (buildTarget == 0 || buildTarget == 1) && (buildTarget != 0 || !NewBridgeMode);
            handles.Bind(enabled ? Airport : null, enabled ? Stand : null,
                enabled && buildTarget == 0 ? SelectedBridge : null, enabled && buildTarget == 1 ? SelectedHead : null);
        }
        public bool HandleWorldInput(DevAirfieldTool airfield, Camera camera, Mouse mouse, Keyboard keyboard)
        {
            if (!IsOpen || Airport == null || airfield == null) return false;
            SyncSelection(airfield); BindHandles();
            if (DevGui.KeyboardCaptured) return false;
            return handles.Input(camera, mouse, keyboard, Panel);
        }

        public void Draw(DevAirfieldTool airfield, DevTrafficControls trafficControls)
        {
            if (!IsOpen || Airport == null) { Close(); return; }
            SyncSelection(airfield); traffic = trafficControls;
            GateDesignerGui.Begin(); BindHandles();
            numberFields.Clear();
            float width = Mathf.Min(U(420), Screen.width - U(36));
            float preferredHeight = 790;
            Panel = new Rect(Screen.width - U(18) - width, U(18), width, Mathf.Min(U(preferredHeight), Screen.height - U(108)));
            DrawWorld(airfield.GetComponent<Camera>());
            GateDesignerGui.Panel(Panel);
            if (Event.current.type == EventType.MouseDown && !Panel.Contains(Event.current.mousePosition)) GUI.FocusControl(null);
            var area = new Rect(Panel.x + U(24), Panel.y + U(20), Panel.width - U(48), Panel.height - U(40));
            var previousSkin = GUI.skin; GUI.skin = GateDesignerGui.Skin;
            GUILayout.BeginArea(area);
            Header();
            GUILayout.Space(U(18));
            Navigation();
            GUILayout.Space(U(18));
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            // An inner margin keeps content away from the scroll bar and gives every card room to breathe.
            GUILayout.BeginVertical(GUILayout.Width(area.width - U(14)));
            if (tab == 0) BuildPanel();
            else if (tab == 1) AircraftPanel();
            else if (tab == 2) AppearancePanel();
            else PresetsPanel();
            if (!string.IsNullOrEmpty(Message)) Notice(Message);
            if (!string.IsNullOrEmpty(handles.Problem)) Notice(handles.Problem, true);
            GUILayout.Space(U(10)); GUILayout.EndVertical(); GUILayout.EndScrollView();
            GUILayout.Space(U(14));
            var footer = GUILayoutUtility.GetRect(0, U(48), GUILayout.ExpandWidth(true));
            GateDesignerGui.Fill(new Rect(footer.x, footer.y, footer.width, U(1)), GateDesignerGui.Border, 0);
            Footer(new Rect(footer.x, footer.y + U(8), footer.width, U(40)));
            GUILayout.EndArea();
            GUI.skin = previousSkin;
            string focus = GUI.GetNameOfFocusedControl();
            DevGui.KeyboardCaptured = focus.StartsWith(FocusPrefix, StringComparison.Ordinal);
            if (previousFocus != focus && numberFields.ContainsKey(previousFocus) && drafts.ContainsKey(previousFocus + "-dirty"))
            { string field = previousFocus; Queue(() => CommitNumberField(field)); }
            previousFocus = focus;
            if (pending != null)
            {
                var action = pending; pending = null; Try(action);
                if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint) GUIUtility.ExitGUI();
            }
            GameObject template = traffic != null && Aircraft != null ? traffic.TemplateFor(Aircraft) : null;
            if (template == null && Aircraft != null)
                foreach (var a in AirCity.Aircraft.Aircraft.All) if (a.type == Aircraft) { template = a.gameObject; break; }
            RefreshPreview(template);
        }

        void Footer(Rect rect)
        {
            void Action(string text, Action act, bool primary = false, bool danger = false)
            { if (GateDesignerGui.Button(rect, text, primary: primary, danger: danger)) Queue(act); }
            if (tab == 0 && (buildTarget == 0 || buildTarget == 3))
            {
                bool bridge = buildTarget == 0;
                bool placing = bridge ? tool.Tool == DevTool.Bridge : tool.Tool == DevTool.Stand;
                if (placing) { Action("Cancel placement", () => tool.SetTool(DevTool.Select)); return; }
                bool existing = bridge ? !NewBridgeMode && SelectedBridge != null : Stand != null;
                System.Action place = bridge ? () => BeginBridgePlacement(tool) : () => BeginStandPlacement(tool);
                if (!existing) { Action(bridge ? "Place this jetbridge" : "Place parking stand", place, primary: true); return; }
                float width = (rect.width - U(10)) * .5f;
                if (GateDesignerGui.Button(new Rect(rect.x, rect.y, width, rect.height), bridge ? "+  Another bridge" : "+  New stand", primary: true)) Queue(place);
                if (GateDesignerGui.Button(new Rect(rect.x + width + U(10), rect.y, width, rect.height), bridge ? "Remove bridge" : "Remove stand", danger: true,
                    tip: bridge ? "Remove only this jetbridge" : "Remove this stand and its bridges")) Queue(DeleteCurrent);
                return;
            }
            GUI.Label(rect, "Your layout. Automatic aircraft connections.", GateDesignerGui.Small);
        }

        void Header()
        {
            var row = GUILayoutUtility.GetRect(0, U(54), GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(row.x, row.y, row.width - U(120), U(16)), "AIRPORT BUILDER", GateDesignerGui.Caption);
            GUI.Label(new Rect(row.x, row.y + U(19), row.width - U(38), U(32)), "Gate designer", GateDesignerGui.Title);
            if (GateDesignerGui.Button(new Rect(row.xMax - U(108), row.y, U(72), U(25)), pause ? "Ⅱ  Paused" : "▷  Live")) Queue(() => SetPaused(!pause));
            if (GateDesignerGui.Button(new Rect(row.xMax - U(28), row.y, U(28), U(25)), "×")) Queue(Close);
            GUILayout.Space(U(12));
            if (tool.Tool == DevTool.Stand) Label("PLACING A NEW PARKING STAND");
            else if (tool.Tool == DevTool.Bridge) Label(tool.BridgeTarget ?? "NEW CONNECTION · choose a wall, then a gate");
            else if (Airport.Taxiways.stands.Count > 0)
                Selector(null, Airport.Taxiways.stands.Where(x => !x.DeIcing).ToList(), Stand, x => x.Name, next =>
                { Stand = next; Aircraft = DefaultAircraft(); tool.SelectStand(next); bridgeIndex = headIndex = 0; NewBridgeMode = Stand.bridges.Count == 0; drafts.Clear(); preview.Dispose(); });
        }
        void Navigation()
        {
            var row = GUILayoutUtility.GetRect(0, U(40), GUILayout.ExpandWidth(true));
            GateDesignerGui.Fill(row, GateDesignerGui.Surface, 9);
            string[] names = { "Build", "Aircraft", "Appearance", "Saved gates" };
            float width = row.width / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                int n = i; var rect = new Rect(row.x + i * width + U(3), row.y + U(3), width - U(6), row.height - U(6));
                if (tab == i) GateDesignerGui.Fill(rect, Color.white, 7);
                GUI.Label(rect, names[i], tab == i ? GateDesignerGui.Centre : GateDesignerGui.Tab);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Queue(() =>
                { tab = n; if (n == 2 && buildTarget == 3) buildTarget = 0; scroll = Vector2.zero; drafts.Clear(); familyPicker = false; GUI.FocusControl(null); });
            }
        }
        void Categories(bool compact = false)
        {
            var row = GUILayoutUtility.GetRect(0, U(76), GUILayout.ExpandWidth(true));
            string[] names = compact ? new[] { "Jetbridges", "Concourses", "Terminal" } : new[] { "Parking", "Jetbridges", "Concourses", "Terminal" };
            int[] targets = compact ? new[] { 0, 1, 2 } : new[] { 3, 0, 1, 2 };
            float width = (row.width - U(8) * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                int next = targets[i]; var tile = new Rect(row.x + i * (width + U(8)), row.y, width, row.height);
                bool on = buildTarget == next;
                GateDesignerGui.Fill(tile, on ? new Color(0.87f, 0.94f, 0.98f) : GateDesignerGui.Surface, 10);
                if (on) GateDesignerGui.Outline(tile, GateDesignerGui.Blue, 10);
                var icon = new Rect(tile.center.x - U(20), tile.y + U(7), U(40), U(38));
                if (next == 0) GateDesignerGui.BridgeGlyph(icon, GateDesignerGui.Blue);
                else if (next == 3) GateDesignerGui.ParkingGlyph(icon, GateDesignerGui.Blue);
                else GateDesignerGui.BuildingGlyph(icon, GateDesignerGui.Blue, next == 2);
                GUI.Label(new Rect(tile.x, tile.y + U(46), tile.width, U(22)), names[i], GateDesignerGui.Centre);
                if (GUI.Button(tile, GUIContent.none, GUIStyle.none)) Queue(() =>
                { buildTarget = next; scroll = Vector2.zero; drafts.Clear(); familyPicker = false; tool.SetTool(DevTool.Select); });
            }
            GUILayout.Space(U(compact ? 12 : 22));
        }
        void BuildPanel()
        {
            Categories();
            if (buildTarget == 3) { ParkingPanel(); return; }
            if (buildTarget == 2) { TerminalPanel(); return; }
            if (buildTarget == 1)
            {
                if (Stand == null) { Notice("Place a parking stand before connecting its concourse."); if (Button("Place parking stand")) Queue(() => BeginStandPlacement(tool)); return; }
                HeadPanel(); return;
            }
            var b = NewBridgeMode || SelectedBridge == null ? BridgeDraft : SelectedBridge;
            bool draft = b == BridgeDraft;
            Section(draft ? "New jetbridge" : "Selected jetbridge");
            if (!draft && Stand.bridges.Count > 1) Selector(null, Stand.bridges, b, x => "Jetbridge " + (Stand.bridges.IndexOf(x) + 1), next =>
            { bridgeIndex = Stand.bridges.IndexOf(next); tool.SelectBridge(next); drafts.Clear(); });
            FamilyCard(b);
            GUILayout.Space(U(16));
            BridgeAppearance(b);
            GUILayout.Space(U(18));
            if (tool.Tool == DevTool.Bridge && !tool.PlacingHead)
            {
                Notice(tool.BridgeStarted ? "Choose the bridge base. Green means this model can serve an aircraft here." : "Click a terminal or concourse wall to start the connection.");
            }
            if (draft && Stand != null && Stand.bridges.Count > 0 && Button("Edit existing bridges")) Queue(() =>
            { tool.SelectStand(Stand); tool.SelectBridge(Stand.bridges[0]); FocusBridge(tool, Stand, Stand.bridges[0]); });
            if (Stand == null)
            {
                Notice("Choose a model and finish now. Place a parking stand to give the aircraft somewhere to park.");
                if (Button("Place parking stand")) Queue(() => BeginStandPlacement(tool));
            }
            if (!string.IsNullOrEmpty(tool.BridgeToolProblem)) Notice(tool.BridgeToolProblem, true);
            if (!draft)
            {
                var problem = Airport.Bridges.FirstProblem(Stand, b, out var type);
                Status(problem == BridgeProblem.None ? "Ready for automatic docking" : (type != null ? type.TypeName + ": " : "") + BridgeLayout.Describe(problem), problem != BridgeProblem.None);
                Note("Click any bridge to select it. Drag its blue handles to move the base or connection.");
            }
        }
        void ParkingPanel()
        {
            bool placing = tool.Tool == DevTool.Stand;
            Section(placing || Stand == null ? "New parking stand" : Stand.Name + " · parking");
            // A placed stand keeps the size and exit it was built with: shown, not offered.
            bool fixedStand = !placing && Stand != null;
            Label("Aircraft size");
            if (fixedStand) Note("Code " + Stand.code.Letter() + " · fixed once placed");
            else
            {
                GUILayout.BeginHorizontal();
                for (int i = 0; i < SizeCodes.Count; i++)
                {
                    var code = (SizeCode)i;
                    if (Button(code.Letter().ToString(), tool.StandCode == code)) Queue(() => tool.SetStandCode(code));
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(U(12));
            }
            Label("Pavement");
            GUILayout.BeginHorizontal();
            foreach (var surface in new[] { PavementSurface.Concrete, PavementSurface.Asphalt })
            {
                var value = surface;
                if (Button(value.ToString(), (placing || Stand == null ? tool.Surface : Stand.surface) == value)) Queue(() => tool.SetSurface(value));
            }
            GUILayout.EndHorizontal();
            Number("Angle to taxilane (degrees)", "parking-angle", tool.StandAngle, value => tool.SetStandAngle(value), 5f);
            if (fixedStand)
            {
                Label("Exit");
                Note((Stand.exit == StandExit.PushBack ? "Push-back" : "Taxi-out") + " · fixed once placed");
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (Button("Push-back", tool.StandExit == StandExit.PushBack)) Queue(() => tool.SetStandExit(StandExit.PushBack));
                if (Button("Taxi-out", tool.StandExit == StandExit.TaxiOut)) Queue(() => tool.SetStandExit(StandExit.TaxiOut));
                GUILayout.EndHorizontal();
            }
            Toggle("Allow light aircraft", !tool.StandNoSmall, value => tool.SetStandNoSmall(!value));
            GUILayout.Space(U(14));
            if (placing)
            {
                Notice("Click beside a taxilane to place. Hold Ctrl/Cmd and drag to place and turn a free stand.");
                if (!string.IsNullOrEmpty(tool.StandPlacementProblem)) Notice(tool.StandPlacementProblem, true);
            }
            if (Stand != null && !placing)
            {
                if (Button("Add a jetbridge")) Queue(() => BeginBridgePlacement(tool));
                if (Button("Aircraft & parking stop")) Queue(() => { tab = 1; scroll = Vector2.zero; });
            }
        }
        void FamilyCard(JetBridge bridge)
        {
            string Name(JetBridgeDefinition definition) => definition == null ? "Classic jetbridge" : definition.specification == null || !definition.specification.Valid ? definition.name :
                (definition.specification.sections.Length == 2 ? "Two-section" : "Three-section") + (definition.specification.MaxTunnel > 26 ? " · extended" : " · compact");
            var rect = GUILayoutUtility.GetRect(0, U(80), GUILayout.ExpandWidth(true));
            GateDesignerGui.Fill(rect, Color.white, 10); GateDesignerGui.Outline(rect, GateDesignerGui.Border, 10);
            GateDesignerGui.BridgeGlyph(new Rect(rect.x + U(14), rect.y + U(12), U(62), U(50)), GateDesignerGui.Blue, bridge.Spec.sections.Length);
            GUI.Label(new Rect(rect.x + U(88), rect.y + U(15), rect.width - U(110), U(24)), Name(bridge.definition), GateDesignerGui.Bold);
            GUI.Label(new Rect(rect.x + U(88), rect.y + U(42), rect.width - U(100), U(22)), $"Reach {bridge.MinTunnel:0.#}–{bridge.MaxTunnel:0.#} m     ▾", GateDesignerGui.Small);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Queue(() => familyPicker = !familyPicker);
            if (!familyPicker) return;
            GUILayout.Space(U(10));
            var options = new List<JetBridgeDefinition> { null }; options.AddRange(definitions);
            for (int i = 0; i < options.Count; i++)
            {
                var definition = options[i];
                if (Button(Name(definition), on: definition == bridge.definition)) Queue(() =>
                { SetBridgeFamily(definition); familyPicker = false; });
            }
        }
        void HeadPanel()
        {
            Section("Concourse buildings");
            var heads = Stand.BridgeHeads.ToList(); var h = SelectedHead;
            if (h != null)
            {
                Selector("Selected concourse", heads, h, x => x == Stand.head ? "Main concourse" : "Branch " + (Stand.additionalHeads.IndexOf(x) + 1), next =>
                { headIndex = heads.IndexOf(next); drafts.Clear(); });
                GUILayout.BeginHorizontal();
                if (Button("Building", h.foundation == HeadFoundation.Building)) Queue(() => Edit(st => h.foundation = HeadFoundation.Building));
                if (Button("Elevated", h.foundation == HeadFoundation.Columns)) Queue(() => Edit(st => h.foundation = HeadFoundation.Columns));
                GUILayout.EndHorizontal();
                GUILayout.Space(U(14));
                Number("Building width (m)", "head-width", h.width > 0 ? h.width : JetBridgeRules.HeadWidth, value => Edit(st => h.width = Mathf.Max(2f, value)));
                Number("Upper boarding level (m)", "head-upper", h.upperFloor > 0 ? h.upperFloor : (h.floor > 0 ? h.floor : JetBridgeRules.RotundaFloor) + 3.8f,
                    value => Edit(st => h.upperFloor = value));
                Notice("Move the end handles to resize the concourse. Shift-drag a handle to set its boarding height.");
            }
            if (Button("+   Place concourse", primary: true)) Queue(() => { tool.SetTool(DevTool.Bridge); tool.SetPlacingHead(true); });
            if (h != null && Button("+   Add connected branch")) Queue(() => AddBranch(h));
            if (tool.Tool == DevTool.Bridge && tool.PlacingHead)
                Notice(tool.HeadStarted ? "Choose the far end of the concourse." : tool.BridgeStarted ? "Choose where the concourse begins." : "Click a terminal wall to start its connection.");
            if (h == null) return;
            var layout = HeadLayout.All(Airport.Taxiways.Layout(Stand), Airport.terminals).First(x => x.head == h);
            var problem = layout.Placement(Airport.Bridges.Site(Stand));
            if (problem != BridgeProblem.None) Status(BridgeLayout.Describe(problem), true);
            GUILayout.Space(U(14));
            if (Button("Remove concourse", danger: true)) Queue(() => Edit(st =>
            {
                var sl = Airport.Taxiways.Layout(st);
                st.bridges.RemoveAll(b => BridgeLayout.Of(b, sl, Airport.terminals).head?.head == h);
                if (st.head == h) st.head = new BridgeHead(); else st.additionalHeads.Remove(h);
                headIndex = 0;
            }));
        }
        void AddBranch(BridgeHead source)
        {
            Edit(st =>
            {
                var sl = Airport.Taxiways.Layout(st);
                var layout = HeadLayout.All(sl, Airport.terminals).First(x => x.head == source);
                var direction = (source.to - source.from).normalized;
                var offset = new Vector2(direction.y, -direction.x) * 15f;
                var branch = source.Clone(); branch.id = Guid.NewGuid().ToString("N");
                branch.from += offset; branch.to += offset;
                branch.linkBends.Clear(); branch.supportColumns.Clear(); branch.linkSupportColumns.Clear();
                layout.ClosestOnWall(sl.At(branch.from.x, branch.from.y), out var root);
                branch.root = BridgeLayout.ToStand(sl, root); st.additionalHeads.Add(branch);
                headIndex = st.BridgeHeads.Count() - 1; tool.SetTool(DevTool.Select);
            });
        }
        void TerminalPanel()
        {
            Section("Terminal building");
            var terminal = SelectedTerminal;
            if (terminal == null) { Notice("Use the Terminal tool to place a building in the airport."); return; }
            Selector(null, Airport.terminals, terminal, x => "Terminal " + (Airport.terminals.IndexOf(x) + 1), next => { terminalIndex = Airport.terminals.IndexOf(next); drafts.Clear(); });
            // Corridors board at a floor: keep that floor when storeys change height or count.
            void KeepingBoarding(Action<Terminal> edit) => EditTerminal(terminal, t => { int floor = t.BoardingFloor(); edit(t); t.SetBoardingFloor(floor); });
            Number("Storeys", "terminal-count", terminal.floors, value => KeepingBoarding(t => t.floors = Mathf.Clamp(Mathf.RoundToInt(value), 1, Terminal.MaxFloors)), 1);
            for (int i = 0; i < terminal.floors; i++)
            { int f = i; Number("Floor " + (i + 1) + " height (m)", "terminal-height-" + i, terminal.StoreyHeight(i), value => KeepingBoarding(t => t.SetStoreyHeight(f, value))); }
            if (terminal.floors >= 2)
                Selector("Boarding floor", Enumerable.Range(1, terminal.floors - 1).ToList(), terminal.BoardingFloor(),
                    f => "Floor " + (f + 1) + " · " + terminal.FloorElevation(f).ToString("0.##", CultureInfo.InvariantCulture) + " m",
                    f => EditTerminal(terminal, t => t.SetBoardingFloor(f)));
            else Notice("Add a storey to give corridors a floor to board at.");
            Notice("Choose Appearance for facade styles, wall angles and shaped roofs.");
            if (Button("Edit facade appearance")) Queue(() => { tab = 2; scroll = Vector2.zero; });
        }

        void AircraftPanel()
        {
            Section("Try an aircraft at this gate");
            if (Stand == null) { Notice("Select a stand to configure its aircraft parking."); return; }
            Selector("Aircraft", types, Aircraft, x => x.TypeName, SelectAircraft);
            if (Aircraft == null) return;
            Toggle("Preview at this stand", PreviewEnabled, value => SetPreview(value, PreviewProgress));
            if (PreviewEnabled)
            {
                GUILayout.Space(U(10));
                Label("Docking preview");
                var r = GUILayoutUtility.GetRect(0, U(34), GUILayout.ExpandWidth(true));
                float next = GateDesignerGui.Slider(r, PreviewProgress, 0f, 1f);
                if (!Mathf.Approximately(next, PreviewProgress)) Queue(() => SetPreview(true, next));
                Note("Retracted                                      Connected");
            }
            Section("Parking & service");
            var config = Stand.ServiceFor(Aircraft);
            Toggle("Available for this aircraft", config?.allowed ?? true, value => Edit(st => Configuration(st).allowed = value));
            if (!Stand.DeIcing && Stand.exit == StandExit.PushBack)
                Number("Parking stop offset (m)", "stop", Airport.Taxiways.Layout(Stand).StopOffsetFor(Aircraft), value => Edit(st => Configuration(st).stopOffset = value), 0.5f);
            var check = Airport.Taxiways.CheckStand(Stand, Aircraft);
            if (check != StandProblem.None) Status("Parking: " + check, true);
            var plan = Airport.Bridges.ServicePlan(Stand, Aircraft);
            for (int i = 0; i < Stand.bridges.Count; i++)
            {
                var b = Stand.bridges[i];
                Toggle("Jetbridge " + (i + 1), config == null || !config.unusedBridges.Contains(b.id), value => Edit(st =>
                { var unused = Configuration(st).unusedBridges; if (value) unused.Remove(b.id); else if (!unused.Contains(b.id)) unused.Add(b.id); }));
                if (plan.TryGet(b, out var entry)) Note(entry.Operates ? "Connects automatically to " + entry.DoorName : BridgeLayout.Describe(entry.problem));
            }
            if (config != null && Button("Reset aircraft settings")) Queue(() => Edit(st => st.serviceConfigurations.Remove(config)));
        }
        StandServiceConfiguration Configuration(Stand stand)
        {
            var c = stand.ServiceFor(Aircraft);
            if (c == null) stand.serviceConfigurations.Add(c = new StandServiceConfiguration { aircraft = Aircraft,
                stopOffset = Airport.Taxiways.Layout(stand).StopOffsetFor(Aircraft) });
            return c;
        }

        void AppearancePanel()
        {
            Categories(true);
            if (buildTarget == 2)
            {
                var terminal = SelectedTerminal; if (terminal == null) { Notice("Select a terminal first."); return; }
                ExteriorPanel(terminal.exterior, terminal.EdgeCount);
                return;
            }
            if (buildTarget == 1)
            {
                var h = SelectedHead; if (h == null) { Notice("Place a concourse to choose its appearance."); return; }
                ExteriorPanel(h.Exterior, 4);
                return;
            }
            var b = NewBridgeMode || SelectedBridge == null ? BridgeDraft : SelectedBridge;
            Section("Jetbridge appearance");
            BridgeAppearance(b);
        }
        void BridgeAppearance(JetBridge bridge)
        {
            var current = bridge.appearance;
            GUILayout.BeginHorizontal();
            bool glass = current == null || current.glazingAmount > 0.5f;
            if (Button("Glass panels", glass)) Queue(() => SetBridgeGlazing(true));
            if (Button("Enclosed panels", !glass)) Queue(() => SetBridgeGlazing(false));
            GUILayout.EndHorizontal();
            if (glass)
            {
                GUILayout.Space(U(16));
                TintChoices(current != null ? current.glazing : Color.white, SetBridgeTint);
            }
        }
        void Paint(JetBridgeAppearance source, Action<JetBridgeAppearance> assign, Action<JetBridgeAppearance> change)
        {
            var appearance = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<JetBridgeAppearance>();
            if (!owned.Contains(appearance))
            { appearance.name = source != null ? source.name : ""; appearance.hideFlags = HideFlags.DontSave; owned.Add(appearance); }
            change(appearance); assign(appearance); BridgeDesignChanged(); if (rig != null) rig.RefreshAppearance(appearance);
        }
        void ExteriorPanel(BuildingExterior exterior, int edges)
        {
            var navigation = GUILayoutUtility.GetRect(0, U(32), GUILayout.ExpandWidth(true));
            GateDesignerGui.Fill(navigation, GateDesignerGui.Surface, 8);
            string[] pages = { "Finish", "Facade angle", "Roof shape" };
            for (int i = 0; i < pages.Length; i++)
            {
                int page = i;
                var rect = new Rect(navigation.x + i * navigation.width / 3f + U(3), navigation.y + U(3), navigation.width / 3f - U(6), navigation.height - U(6));
                if (exteriorPage == i) GateDesignerGui.Fill(rect, Color.white, 6);
                GUI.Label(rect, pages[i], exteriorPage == i ? GateDesignerGui.ButtonText : GateDesignerGui.Tab);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Queue(() => { exteriorPage = page; scroll = Vector2.zero; });
            }
            GUILayout.Space(U(8));
            if (exteriorPage == 1) { FacadeAnglePanel(exterior, edges); return; }
            if (exteriorPage == 2) { RoofShapePanel(exterior); return; }
            string[] names = { "Classic", "Minimal", "Framed", "Colonnade" };
            string[] descriptions = { "Silver framing", "Slim edges", "Bold structure", "V-shaped columns" };
            for (int row = 0; row < 2; row++)
            {
                var area = GUILayoutUtility.GetRect(0, U(108), GUILayout.ExpandWidth(true));
                float width = (area.width - U(12)) / 2f;
                for (int column = 0; column < 2; column++)
                {
                    int index = row * 2 + column; var style = (FacadeStyle)index;
                    var rect = new Rect(area.x + column * (width + U(12)), area.y, width, U(98));
                    bool selected = exterior.style == style;
                    GateDesignerGui.Fill(rect, selected ? new Color(0.87f, 0.94f, 0.98f) : Color.white, 10);
                    GateDesignerGui.Outline(rect, selected ? GateDesignerGui.Blue : GateDesignerGui.Border, 10, selected ? 2 : 1);
                    GateDesignerGui.FacadeGlyph(new Rect(rect.x + U(14), rect.y + U(10), rect.width - U(28), U(38)), style);
                    GUI.Label(new Rect(rect.x + U(12), rect.y + U(52), rect.width - U(24), U(20)), names[index], GateDesignerGui.Bold);
                    GUI.Label(new Rect(rect.x + U(12), rect.y + U(74), rect.width - U(24), U(16)), descriptions[index], GateDesignerGui.Small);
                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Queue(() => ApplyFacadeStyle(style));
                }
            }
            TintChoices(exterior.facade.glassTint, tint => SetGlassTint(tint));
        }
        void FacadeAnglePanel(BuildingExterior exterior, int edges)
        {
            facadeFace = Mathf.Clamp(facadeFace, -1, edges - 1);
            Selector("Apply angle to", Enumerable.Range(-1, edges + 1).ToList(), facadeFace,
                face => face < 0 ? "All walls" : "Wall " + (face + 1), face => facadeFace = face);
            float angle = facadeFace < 0 ? exterior.facade.lean : exterior.ForFace(facadeFace).lean;
            GUILayout.BeginHorizontal(); GUILayout.BeginVertical();
            Number("Facade angle (°)", "facade-angle-" + facadeFace, angle, value => SetFacadeAngle(Mathf.Clamp(value, -30f, 30f), facadeFace), 1f);
            GUILayout.EndVertical();
            if (FacadeStyles.Geometry(exterior).pierWidth > 0f)
            {
                GUILayout.Space(U(12)); GUILayout.BeginVertical();
                Number("Column gap (m)", "column-gap", exterior.columnGap, value => SetColumnGap(Mathf.Clamp(value, 0f, 6f)), 0.25f);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            var drawing = GUILayoutUtility.GetRect(0, U(84), GUILayout.ExpandWidth(true));
            GateDesignerGui.AngleGlyph(drawing, angle);
            if (GateDesignerGui.Button(new Rect(drawing.xMax - U(78), drawing.y + U(8), U(68), U(26)), "Reset")) Queue(() => SetFacadeAngle(0f, facadeFace));
            Note("Negative leans in. Positive leans out. Floors and boarding levels stay horizontal.");
        }
        void RoofShapePanel(BuildingExterior exterior)
        {
            var roof = exterior.roofShape;
            Label("Roof form");
            var row = GUILayoutUtility.GetRect(0, U(86), GUILayout.ExpandWidth(true));
            float width = (row.width - U(20)) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var form = (RoofForm)i; var rect = new Rect(row.x + i * (width + U(10)), row.y, width, U(78));
                bool selected = roof.form == form;
                GateDesignerGui.Fill(rect, selected ? new Color(0.87f, 0.94f, 0.98f) : Color.white, 8);
                GateDesignerGui.Outline(rect, selected ? GateDesignerGui.Blue : GateDesignerGui.Border);
                GateDesignerGui.RoofGlyph(new Rect(rect.x + U(10), rect.y + U(10), rect.width - U(20), U(34)), form);
                GUI.Label(new Rect(rect.x, rect.y + U(48), rect.width, U(22)), form.ToString(), GateDesignerGui.ButtonText);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Queue(() => SetRoofForm(form));
            }
            GUILayout.Space(U(12));
            GUILayout.BeginHorizontal(); GUILayout.BeginVertical();
            Number("Roof extension (m)", "roof-overhang", roof.overhang, value => SetRoofOverhang(Mathf.Clamp(value, 0f, 8f)), 0.25f);
            GUILayout.EndVertical();
            if (roof.form != RoofForm.Flat)
            {
                GUILayout.Space(U(12)); GUILayout.BeginVertical();
                Number("Roof rise (m)", "roof-rise", roof.rise, value => SetRoofRise(Mathf.Clamp(value, 0f, 12f)), 0.5f);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            if (roof.form != RoofForm.Flat)
            {
                Label("Pattern direction");
                GUILayout.BeginHorizontal();
                if (Button("Along building", !roof.across)) Queue(() => SetRoofAcross(false));
                if (Button("Across building", roof.across)) Queue(() => SetRoofAcross(true));
                GUILayout.EndHorizontal();
            }
            Note("The roof extends far enough to cover the columns. Glass and framing follow the profile.");
        }
        void TintChoices(Color current, Action<GlassTint> change)
        {
            Label("Glass tint");
            var row = GUILayoutUtility.GetRect(0, U(60), GUILayout.ExpandWidth(true));
            float width = (row.width - U(24)) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var tint = (GlassTint)i; var color = FacadeStyles.Tint(tint);
                var rect = new Rect(row.x + i * (width + U(8)), row.y, width, U(57));
                bool selected = (current - color).maxColorComponent < 0.015f && (color - current).maxColorComponent < 0.015f;
                GateDesignerGui.Fill(rect, selected ? new Color(0.87f, 0.94f, 0.98f) : Color.white, 8);
                GateDesignerGui.Outline(rect, selected ? GateDesignerGui.Blue : GateDesignerGui.Border);
                GateDesignerGui.Fill(new Rect(rect.x + U(10), rect.y + U(8), rect.width - U(20), U(22)), new Color(0.57f, 0.71f, 0.76f) * color, 4);
                GUI.Label(new Rect(rect.x, rect.y + U(34), rect.width, U(20)), tint.ToString(), GateDesignerGui.Tab);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Queue(() => change(tint));
            }
            GUILayout.Space(U(10));
        }

        void PresetsPanel()
        {
            Section("Keep this gate design");
            Note("Save a layout to use it at other stands or next time you play.");
            GUILayout.Space(U(12)); Label("Design name");
            var input = GUILayoutUtility.GetRect(0, U(42), GUILayout.ExpandWidth(true)); GateDesignerGui.Fill(input, Color.white, 8); GateDesignerGui.Outline(input, GateDesignerGui.Border);
            GUI.SetNextControlName(FocusPrefix + "preset-name"); presetName = GUI.TextField(input, presetName, GateDesignerGui.Input);
            GUILayout.Space(U(12));
            if (Stand != null && Button("Save gate design", primary: true)) Queue(() => SavePreset(presetName));
            Section("Saved gates");
            if (saved.Count == 0) { Notice("Your saved gate designs will appear here."); return; }
            presetIndex = Mathf.Clamp(presetIndex, 0, saved.Count - 1); var selected = saved[presetIndex];
            Selector(null, saved, selected, x => x.name, next => presetIndex = saved.IndexOf(next));
            if (Button("Use at this stand", primary: true)) Queue(() => ApplyPreset(selected.id, false));
            if (Button("Use mirrored layout")) Queue(() => ApplyPreset(selected.id, true));
        }

        void DrawWorld(Camera camera)
        {
            handles.Draw(camera);
            preview.DrawLabel(camera);
            if (tab == 2 && exteriorPage == 1 && buildTarget != 0 && camera != null)
            {
                Terminal building = buildTarget == 2 ? SelectedTerminal : SelectedHead != null && Stand != null ?
                    HeadLayout.All(Airport.Taxiways.Layout(Stand), Airport.terminals).FirstOrDefault(h => h.head == SelectedHead)?.Building : null;
                if (building != null && (buildTarget == 2 || SelectedHead.foundation == HeadFoundation.Building)) DrawFacadeSelection(camera, building);
            }
            if ((tab != 0 && tab != 2) || (buildTarget != 0 && buildTarget != 1) || tool.Tool != DevTool.Select || Stand == null) return;
            if (Screen.width - Panel.width > U(550))
            {
                var hint = new Rect(U(24), Screen.height - U(116), Mathf.Min(U(540), Screen.width - Panel.width - U(70)), U(38));
                GateDesignerGui.Panel(hint);
                GUI.Label(new Rect(hint.x + U(14), hint.y, hint.width - U(28), hint.height), "Drag to move  ·  Shift-drag to raise  ·  Alt for columns", GateDesignerGui.Small);
            }
        }
        void DrawFacadeSelection(Camera camera, Terminal building)
        {
            var outline = building.OutlineWithFaces(out var sourceFaces);
            var profile = building.exterior.roofShape.For(building);
            var points = profile.Sample(outline, sourceFaces, out var faces);
            var unit = building.WallOffsets(points, faces);
            bool Project(Vector2 point, float y, out Vector2 screen)
            {
                var p = camera.WorldToScreenPoint(Airport.transform.TransformPoint(Geo.X0Z(point, y)));
                screen = new Vector2(p.x, Screen.height - p.y); return p.z > 0f;
            }
            for (int i = 0; i < points.Count; i++)
            {
                if (facadeFace >= 0 && faces[i] != facadeFace) continue;
                int j = (i + 1) % points.Count;
                float a = profile.SurfaceHeight(points[i], unit[i], building.RoofHeight + building.exterior.parapet);
                float b = profile.SurfaceHeight(points[j], unit[j], building.RoofHeight + building.exterior.parapet);
                if (Project(points[i] + unit[i] * a, a, out var p0) &&
                    Project(points[j] + unit[j] * b, b, out var p1)) GateDesignerGui.Line(p0, p1, GateDesignerGui.Blue, 3);
                if (Project(points[i], 0f, out var bottomA) && Project(points[j], 0f, out var bottomB)) GateDesignerGui.Line(bottomA, bottomB, GateDesignerGui.Blue, 2);
                if (i == 0 || faces[i] != faces[i - 1])
                    if (Project(points[i], 0f, out var bottom) && Project(points[i] + unit[i] * a, a, out var top)) GateDesignerGui.Line(bottom, top, GateDesignerGui.Blue, 2);
            }
        }

        static bool Button(string label, bool on = false, bool danger = false, bool primary = false)
        {
            var rect = GUILayoutUtility.GetRect(0, U(danger ? 34 : 40), GUILayout.ExpandWidth(true));
            bool pressed = GateDesignerGui.Button(rect, label, on, primary, danger);
            GUILayout.Space(U(8)); return pressed;
        }
        static void Section(string text) { GUILayout.Space(U(10)); GUILayout.Label(text, GateDesignerGui.Bold, GUILayout.Height(U(26))); GUILayout.Space(U(10)); }
        static void Label(string text) { GUILayout.Label(text, GateDesignerGui.Small, GUILayout.Height(U(22))); GUILayout.Space(U(4)); }
        static void Note(string text) { GUILayout.Label(text, GateDesignerGui.Hint); GUILayout.Space(U(10)); }
        static void Notice(string text, bool error = false)
        {
            var height = GateDesignerGui.Hint.CalcHeight(new GUIContent(text), U(310)) + U(28);
            var r = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            GateDesignerGui.Fill(r, error ? new Color(1, 0.93f, 0.91f) : new Color(0.89f, 0.94f, 0.97f), 9);
            GUI.Label(new Rect(r.x + U(14), r.y + U(12), r.width - U(28), r.height - U(24)), text, GateDesignerGui.Hint);
            GUILayout.Space(U(14));
        }
        static void Status(string text, bool error)
        {
            var r = GUILayoutUtility.GetRect(0, U(40), GUILayout.ExpandWidth(true));
            GateDesignerGui.Fill(new Rect(r.x, r.center.y - U(3), U(6), U(6)), error ? GateDesignerGui.Red : GateDesignerGui.Teal, 4);
            GUI.Label(new Rect(r.x + U(16), r.y, r.width - U(16), r.height), text, GateDesignerGui.Hint);
        }
        void Toggle(string label, bool current, Action<bool> change)
        {
            var r = GUILayoutUtility.GetRect(0, U(42), GUILayout.ExpandWidth(true));
            if (GateDesignerGui.Toggle(r, label, current)) Queue(() => change(!current));
            GUILayout.Space(U(8));
        }
        void Selector<T>(string label, IList<T> values, T selected, Func<T, string> name, Action<T> change)
        {
            if (values.Count == 0) return;
            if (!string.IsNullOrEmpty(label)) Label(label);
            int index = Mathf.Max(0, values.IndexOf(selected));
            var r = GUILayoutUtility.GetRect(0, U(42), GUILayout.ExpandWidth(true));
            GateDesignerGui.Fill(r, Color.white, 8); GateDesignerGui.Outline(r, GateDesignerGui.Border);
            GUI.Label(new Rect(r.x + U(50), r.y, r.width - U(100), r.height), name(values[index]), GateDesignerGui.Centre);
            if (GateDesignerGui.Button(new Rect(r.x + U(4), r.y + U(4), U(34), r.height - U(8)), "‹")) Queue(() => { change(values[(index + values.Count - 1) % values.Count]); drafts.Clear(); });
            if (GateDesignerGui.Button(new Rect(r.xMax - U(38), r.y + U(4), U(34), r.height - U(8)), "›")) Queue(() => { change(values[(index + 1) % values.Count]); drafts.Clear(); });
            GUILayout.Space(U(12));
        }
        void Number(string label, string key, float value, Action<float> change, float step = 0.25f)
        {
            Label(label);
            string id = FocusPrefix + key;
            bool focused = GUI.GetNameOfFocusedControl() == id;
            if (!drafts.TryGetValue(id, out var text)) text = value.ToString("0.##", CultureInfo.InvariantCulture);
            float stepFrom = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float edited) && !float.IsNaN(edited) && !float.IsInfinity(edited) ? edited : value;
            var r = GUILayoutUtility.GetRect(0, U(40), GUILayout.ExpandWidth(true));
            GateDesignerGui.Fill(r, Color.white, 8); GateDesignerGui.Outline(r, GateDesignerGui.Border);
            bool down = GateDesignerGui.Button(new Rect(r.x + U(4), r.y + U(4), U(34), r.height - U(8)), "−");
            bool up = GateDesignerGui.Button(new Rect(r.xMax - U(38), r.y + U(4), U(34), r.height - U(8)), "+");
            bool stepped = down || up;
            if (stepped) Queue(() => { change(stepFrom + (up ? step : -step)); drafts.Clear(); GUI.FocusControl(null); });
            bool enter = focused && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            GUI.SetNextControlName(id);
            string next = GUI.TextField(new Rect(r.x + U(45), r.y, r.width - U(90), r.height), text, GateDesignerGui.NumberInput);
            drafts[id] = next;
            if (next != text) drafts[id + "-dirty"] = "1";
            numberFields[id] = (next, change);
            if (stepped) numberFields.Remove(id);
            if (!stepped && enter)
            {
                Event.current.Use();
                Queue(() => { CommitNumberField(id); GUI.FocusControl(null); });
            }
            if (!focused && !drafts.ContainsKey(id + "-dirty")) drafts.Remove(id);
            GUILayout.Space(U(14));
        }
        void CommitNumberField(string id)
        {
            if (!drafts.ContainsKey(id + "-dirty") || !numberFields.TryGetValue(id, out var field)) return;
            if (float.TryParse(field.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                field.change(parsed);
            else Message = "Enter a valid number.";
            drafts.Remove(id); drafts.Remove(id + "-dirty"); numberFields.Remove(id);
        }
    }
}
