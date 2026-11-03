using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Dev
{
    /// <summary>The sandbox's gate editor. Uses runtime IMGUI and the same physical layout and service planner as
    /// the editor window; it has no UnityEditor dependency and works in a player build.</summary>
    public sealed partial class DevGateDesigner : IDisposable
    {
        public bool IsOpen { get; private set; }
        public Airport Airport { get; private set; }
        public Stand Stand { get; private set; }
        public string Message { get; private set; }
        public AircraftType Aircraft { get; private set; }
        public Rect Panel { get; private set; }
        public bool PreviewEnabled { get; private set; }
        public float PreviewProgress { get; private set; } = 1f;
        public JetBridge BridgeDraft { get; } = new JetBridge { tunnel = TunnelSize.Long };
        public bool NewBridgeMode { get; private set; }
        public JetBridge CurrentBridge => NewBridgeMode || SelectedBridge == null ? BridgeDraft : SelectedBridge;
        readonly GateAircraftPreview preview = new GateAircraftPreview();
        readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        readonly Dictionary<string, GateLayoutPreset> loaded = new Dictionary<string, GateLayoutPreset>();
        readonly List<JetBridgeAppearance> owned = new List<JetBridgeAppearance>();
        readonly string presetDirectory;
        readonly List<JetBridgeDefinition> definitions = new List<JetBridgeDefinition>();
        readonly List<JetBridgeAppearance> appearances = new List<JetBridgeAppearance>();
        readonly List<AircraftType> types = new List<AircraftType>();
        GatePresetStore store;
        List<GatePresetStore.Entry> saved = new List<GatePresetStore.Entry>();
        BridgeRig rig;
        DevAirfieldTool tool;
        DevTrafficControls traffic;
        Vector2 scroll;
        int tab, bridgeIndex, headIndex, presetIndex;
        int terminalIndex;
        bool pause = true, pausedByUs;
        float previousSpeed;
        string presetName = "My gate";
        Action pending;
        const string FocusPrefix = "gate-designer-";

        public DevGateDesigner(string presetDirectory = null) { this.presetDirectory = presetDirectory; }

        public void Open(Airport airport, Stand stand)
        {
            if (airport == null) return;
            if (IsOpen) Close();
            if (Airport != airport)
            {
                store?.Dispose(); loaded.Clear();
                Airport = airport;
                rig = airport.GetComponent<BridgeRig>();
                definitions.Clear(); appearances.Clear(); types.Clear();
                if (rig != null) { definitions.AddRange(rig.Definitions); appearances.AddRange(rig.appearances.Where(x => x != null)); }
                foreach (var st in airport.Taxiways.stands)
                {
                    foreach (var b in st.bridges)
                    {
                        if (b.definition != null && !definitions.Contains(b.definition)) definitions.Add(b.definition);
                        if (b.appearance != null && !owned.Contains(b.appearance) && !appearances.Contains(b.appearance)) appearances.Add(b.appearance);
                    }
                    foreach (var h in st.BridgeHeads)
                        if (h.appearance != null && !owned.Contains(h.appearance) && !appearances.Contains(h.appearance)) appearances.Add(h.appearance);
                }
                types.AddRange(airport.aircraftTypes.Where(x => x != null).Distinct());
                foreach (var a in AirCity.Aircraft.Aircraft.All)
                    if (a.type != null && !types.Contains(a.type)) types.Add(a.type);
                store = new GatePresetStore(presetDirectory ?? Path.Combine(Application.persistentDataPath, "GateLayouts"), definitions, appearances, types);
                Try(() => saved = store.List());
            }
            Stand = stand != null && airport.Taxiways.stands.Contains(stand) ? stand :
                airport.Taxiways.stands.FirstOrDefault(x => !x.DeIcing && x.exit == StandExit.PushBack);
            Aircraft = DefaultAircraft();
            bridgeIndex = headIndex = 0; tab = 0; buildTarget = 0; drafts.Clear();
            NewBridgeMode = Stand == null || Stand.bridges.Count == 0;
            IsOpen = true; DevGui.GateDesignerOpen = true;
            SetPreview(true);
            SetPaused(pause);
        }

        public void Close()
        {
            if (tool != null && (tool.Tool == DevTool.Bridge || tool.Tool == DevTool.Stand)) tool.SetTool(DevTool.Select);
            preview.Dispose(); handles.Clear();
            if (pausedByUs && Airport != null && Mathf.Approximately(Airport.timeScale, 0f)) Airport.timeScale = previousSpeed;
            pausedByUs = false;
            IsOpen = false; DevGui.GateDesignerOpen = false; DevGui.KeyboardCaptured = false;
        }

        public void SetPaused(bool value)
        {
            pause = value;
            if (Airport == null) return;
            if (value && !pausedByUs) { previousSpeed = Airport.timeScale; Airport.timeScale = 0f; pausedByUs = true; }
            else if (!value && pausedByUs)
            { if (Mathf.Approximately(Airport.timeScale, 0f)) Airport.timeScale = previousSpeed; pausedByUs = false; }
        }

        public void SelectAircraft(AircraftType type) { Aircraft = type; preview.Dispose(); drafts.Clear(); }
        public void BeginBridgePlacement(DevAirfieldTool airfield)
        {
            tool = airfield; tab = 0; buildTarget = 0; NewBridgeMode = true; scroll = Vector2.zero;
            handles.Clear(); drafts.Clear();
            tool.SetTool(DevTool.Bridge); tool.SetPlacingHead(false); tool.SetBridgeDesign(BridgeDraft);
        }
        public void BeginStandPlacement(DevAirfieldTool airfield)
        {
            tool = airfield; tab = 0; buildTarget = 3; scroll = Vector2.zero; handles.Clear(); drafts.Clear();
            tool.SetTool(DevTool.Stand);
        }
        public void FocusBridge(DevAirfieldTool airfield, Stand stand, JetBridge bridge)
        {
            if (stand == null || !stand.bridges.Contains(bridge)) return;
            bool changeAircraft = Stand != stand || Aircraft == null;
            tool = airfield; Stand = stand; if (changeAircraft) Aircraft = DefaultAircraft(); bridgeIndex = stand.bridges.IndexOf(bridge);
            NewBridgeMode = false; buildTarget = tab = 0; scroll = Vector2.zero; drafts.Clear(); handles.Clear();
        }
        public void FocusParking(DevAirfieldTool airfield, Stand stand)
        {
            bool changeAircraft = Stand != stand || Aircraft == null;
            tool = airfield; Stand = stand; if (changeAircraft) Aircraft = DefaultAircraft(); buildTarget = 3; tab = 0;
            scroll = Vector2.zero; drafts.Clear(); handles.Clear(); preview.Dispose();
        }
        public void FollowPlacementStand(Stand stand)
        {
            if (!IsOpen || !NewBridgeMode || stand == null || stand == Stand) return;
            Stand = stand; Aircraft = DefaultAircraft(); preview.Dispose();
        }
        public void SetBridgeFamily(JetBridgeDefinition definition)
        {
            var bridge = CurrentBridge;
            if (bridge == BridgeDraft) NewBridgeMode = true;
            bridge.definition = definition;
            BridgeDesignChanged();
        }
        public void SetBridgeGlazing(bool glazed) => PaintBridge(a =>
        { a.glazingAmount = glazed ? 1f : 0f; a.cladding = a.roof = Color.white; });
        public void SetBridgeTint(GlassTint tint) => PaintBridge(a =>
        { a.glazing = FacadeStyles.Tint(tint); a.cladding = a.roof = Color.white; });
        void PaintBridge(Action<JetBridgeAppearance> change)
        {
            var bridge = CurrentBridge;
            if (bridge == BridgeDraft) NewBridgeMode = true;
            Paint(bridge.appearance, value => bridge.appearance = value, change);
        }
        void BridgeDesignChanged()
        {
            if (NewBridgeMode) tool?.SetBridgeDesign(BridgeDraft);
            else { Airport.LayoutChanged(); if (rig != null) rig.Refresh(); }
            Message = null;
        }
        public void RemoveBridge()
        {
            var bridge = SelectedBridge;
            if (bridge == null || NewBridgeMode) return;
            Airport.Bridges.RemoveBridge(Stand, bridge); bridgeIndex = 0;
            if (tool != null) tool.SelectStand(Stand);
            NewBridgeMode = Stand.bridges.Count == 0; handles.Clear(); drafts.Clear();
        }
        public void DeleteCurrent()
        {
            if (buildTarget == 0) RemoveBridge();
            else if (buildTarget == 3 && Stand != null && tool != null && tool.Tool == DevTool.Select)
            { tool.DeleteSelectedStand(); Stand = null; preview.Dispose(); handles.Clear(); }
        }
        AircraftType DefaultAircraft()
        {
            if (Stand == null) return null;
            var layout = Airport.Taxiways.Layout(Stand);
            foreach (var a in AirCity.Aircraft.Aircraft.All)
                if (a != null && a.type != null && !a.Moving && layout != null &&
                    Vector3.Distance(Airport.transform.InverseTransformPoint(a.transform.position), Geo.X0Z(layout.ParkPosition(a.type))) < 3f)
                    return a.type;
            return Airport.Bridges.TypesFor(Stand).Where(x => !x.Light).OrderByDescending(x => x.Wingspan).FirstOrDefault();
        }
        public void SetPreview(bool enabled, float progress = 1f)
        { PreviewEnabled = enabled; PreviewProgress = Mathf.Clamp01(progress); if (!enabled) preview.Dispose(); }

        public void RefreshPreview(GameObject template)
        {
            if (!IsOpen || !PreviewEnabled || Aircraft == null || Stand == null) { preview.Dispose(); return; }
            if (!preview.Show(Airport, Stand, Aircraft, template, PreviewProgress)) Message = "No preview model for this aircraft is available in this sandbox.";
        }

        public void Edit(Action<Stand> edit)
        {
            if (Stand == null || Airport == null) return;
            edit(Stand); Airport.Taxiways.Rebuild(); Message = null;
        }

        public void SelectTerminal(Terminal terminal)
        {
            if (Airport == null || !Airport.terminals.Contains(terminal)) return;
            terminalIndex = Airport.terminals.IndexOf(terminal); buildTarget = 2; tab = 0;
            scroll = Vector2.zero; drafts.Clear(); preview.Dispose(); SetPreview(false);
        }

        public bool EditTerminal(Terminal terminal, Action<Terminal> edit)
        {
            if (Airport == null || !Airport.terminals.Contains(terminal)) return false;
            var copy = terminal.Clone(); edit(copy);
            if (!Airport.ChangeTerminal(terminal, copy)) { Message = Airport.TerminalProblem(copy, terminal); return false; }
            Airport.Taxiways.Rebuild(); Message = null; return true;
        }

        public bool ApplyFacadeStyle(FacadeStyle style) => EditExterior(e => FacadeStyles.Apply(e, style));
        public bool SetGlassTint(GlassTint tint) => EditExterior(e => FacadeStyles.SetTint(e, tint));
        public bool SetFacadeAngle(float angle, int face = -1)
        {
            if (!BuildingFacade.Finite(angle) || Mathf.Abs(angle) > 30f) return false;
            if (buildTarget == 1 && SelectedHead != null && SelectedHead.foundation != HeadFoundation.Building)
            { Message = "Choose Building in Build > Concourses to angle its walls."; return false; }
            int edges = buildTarget == 2 ? SelectedTerminal?.EdgeCount ?? 0 : 4;
            if (face < -1 || face >= edges) return false;
            return EditExterior(e =>
            {
                if (face >= 0) e.Override(face).lean = angle;
                else { e.facade.lean = angle; foreach (var wall in e.faces) wall.facade.lean = angle; }
            });
        }
        public bool SetRoofForm(RoofForm form) => Enum.IsDefined(typeof(RoofForm), form) && EditExterior(e => e.roofShape.form = form);
        public bool SetRoofRise(float rise)
        {
            if (!BuildingFacade.Finite(rise) || rise < 0f || rise > 12f) return false;
            return EditExterior(e => e.roofShape.rise = rise);
        }
        public bool SetRoofAcross(bool across) => EditExterior(e => e.roofShape.across = across);
        public bool SetColumnGap(float gap)
        {
            if (!BuildingFacade.Finite(gap) || gap < 0f || gap > 6f) return false;
            return EditExterior(e => e.columnGap = gap);
        }
        public bool SetRoofOverhang(float overhang)
        {
            if (!BuildingFacade.Finite(overhang) || overhang < 0f || overhang > 8f) return false;
            return EditExterior(e => e.roofShape.overhang = overhang);
        }
        bool EditExterior(Action<BuildingExterior> edit)
        {
            if (Airport == null) return false;
            if (buildTarget == 2)
                return SelectedTerminal != null && EditTerminal(SelectedTerminal, t => edit(t.exterior));
            var head = SelectedHead;
            if (buildTarget != 1 || head == null || Stand == null) return false;
            var previous = head.Exterior;
            var candidate = previous.Clone(); edit(candidate); head.exterior = candidate;
            Airport.LayoutChanged();
            var layout = HeadLayout.All(Airport.Taxiways.Layout(Stand), Airport.terminals).First(x => x.head == head);
            var problem = layout.Placement(Airport.Bridges.Site(Stand));
            if (problem != BridgeProblem.None)
            {
                head.exterior = previous; Airport.LayoutChanged();
                Message = BridgeLayout.Describe(problem); return false;
            }
            if (rig != null) rig.Refresh();
            Message = null; return true;
        }

        public GatePresetStore.Entry SavePreset(string name)
        {
            if (store == null || Stand == null) throw new InvalidOperationException("Select a gate first.");
            var entry = store.Save(name, Stand); saved = store.List(); presetIndex = saved.FindIndex(x => x.id == entry.id);
            Message = "Preset saved. It will be available next time you play.";
            return entry;
        }

        public bool ApplyPreset(string id, bool mirror)
        {
            if (store == null || Stand == null) return false;
            if (!loaded.TryGetValue(id, out var preset)) loaded[id] = preset = store.Load(id);
            bool ok = preset.Apply(Airport, Stand, mirror, out var problem);
            Message = ok ? "Gate layout applied." : BridgeLayout.Describe(problem);
            drafts.Clear(); return ok;
        }

        void Queue(Action action)
        {
            string field = previousFocus;
            pending += () => { CommitNumberField(field); action(); };
        }
        void Try(Action action)
        {
            try { action(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException)
            { Message = e.Message; }
        }
        public void Dispose()
        {
            Close(); store?.Dispose(); store = null;
            foreach (var style in owned)
                if (style != null) { if (Application.isPlaying) Object.Destroy(style); else Object.DestroyImmediate(style); }
            owned.Clear(); loaded.Clear();
        }
    }
}
