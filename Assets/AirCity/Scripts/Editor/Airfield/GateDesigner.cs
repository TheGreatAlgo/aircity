using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.EditorTools.Aircraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>Precise physical gate authoring. The game's service planner supplies all moving poses.</summary>
    public sealed class GateDesigner : EditorWindow
    {
        Airport airport;
        int standIndex;
        AircraftType aircraft;
        GateLayoutPreset preset;
        Vector2 scroll;
        float previewProgress = 1f, grid = 0.5f;
        bool preview = true, mirror;
        readonly GateAircraftPreview aircraftPreview = new GateAircraftPreview();
        BridgeRig rig;
        string message;

        [MenuItem("AirCity/Airfield/Gate Designer")]
        public static void Open()
        {
            var window = GetWindow<GateDesigner>("Gate Designer");
            window.minSize = new Vector2(420f, 460f);
        }

        void OnEnable() => SceneView.duringSceneGui += SceneGUI;
        void OnDisable()
        {
            SceneView.duringSceneGui -= SceneGUI;
            ClearPreview();
        }

        Stand Selected => airport != null && standIndex >= 0 && standIndex < airport.Taxiways.stands.Count
            ? airport.Taxiways.stands[standIndex] : null;

        void OnGUI()
        {
            var selectedAirport = (Airport)EditorGUILayout.ObjectField("Airport", airport, typeof(Airport), true);
            if (selectedAirport != airport) { ClearPreview(); airport = selectedAirport; standIndex = 0; }
            if (airport == null)
            {
                if (GUILayout.Button("Use airport in scene")) airport = FindAnyObjectByType<Airport>();
                EditorGUILayout.HelpBox("Place the terminal, bridges and aircraft stops. The game calculates door assignments, angles and movement.", MessageType.Info);
                return;
            }
            var net = airport.Taxiways;
            if (net.stands.Count == 0) { EditorGUILayout.HelpBox("Place a stand first.", MessageType.Info); return; }
            int index = EditorGUILayout.Popup("Stand", Mathf.Clamp(standIndex, 0, net.stands.Count - 1), net.stands.Select(s => s.Name).ToArray());
            if (index != standIndex) { ClearPreview(); standIndex = index; }
            var stand = Selected;
            if (aircraft == null || aircraft.SizeCode > stand.Code)
                aircraft = airport.Bridges.TypesFor(stand).Where(t => !t.Light).OrderByDescending(t => t.Wingspan).FirstOrDefault();
            rig = airport.GetComponent<BridgeRig>();
            grid = Mathf.Max(0.01f, EditorGUILayout.FloatField("Scene handle grid (m)", grid));
            Undo.RecordObjects(new Object[] { net, airport }, "Edit gate layout");
            EditorGUI.BeginChangeCheck();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawTerminal();
            DrawAircraft(stand);
            DrawBridges(stand);
            DrawHead(stand.head, "Main bridge head", stand);
            for (int i = 0; i < stand.additionalHeads.Count; i++)
            {
                DrawHead(stand.additionalHeads[i], "Branch head " + (i + 1), stand);
                if (GUILayout.Button("Remove branch " + (i + 1)))
                { stand.additionalHeads.RemoveAt(i); GUI.changed = true; break; }
            }
            if (GUILayout.Button("Add branch head"))
            {
                var h = stand.head.Clone();
                h.id = System.Guid.NewGuid().ToString("N");
                h.from += new Vector2(0f, 10f); h.to += new Vector2(0f, 10f);
                stand.additionalHeads.Add(h); GUI.changed = true;
            }
            DrawPreset(stand);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            EditorGUILayout.EndScrollView();
            if (EditorGUI.EndChangeCheck()) Changed();
            UpdatePreview();
        }

        void DrawTerminal()
        {
            if (airport.terminals.Count == 0) return;
            EditorGUILayout.LabelField("Terminal buildings", EditorStyles.boldLabel);
            for (int i = 0; i < airport.terminals.Count; i++)
            {
                var t = airport.terminals[i];
                EditorGUILayout.LabelField("Terminal " + (i + 1), EditorStyles.boldLabel);
                t.floors = Mathf.Clamp(EditorGUILayout.IntField("Storeys", t.floors), 1, Terminal.MaxFloors);
                for (int f = 0; f < t.floors; f++) t.SetStoreyHeight(f, Mathf.Clamp(EditorGUILayout.FloatField("Storey " + (f + 1) + " height (m)", t.StoreyHeight(f)), 2f, 40f));
                if (t.floors >= 2)
                {
                    int boarding = EditorGUILayout.IntSlider("Boarding floor (" + t.BoardingLevel() + " m)", t.BoardingFloor() + 1, 2, t.floors) - 1;
                    t.SetBoardingFloor(boarding);
                }
                else t.boardingHeight = Mathf.Clamp(EditorGUILayout.FloatField("Terminal " + (i + 1) + " floor (m)", t.boardingHeight), 0.5f, t.RoofHeight - 0.5f);
                DrawExterior(t.exterior, t.EdgeCount);
                var problem = airport.TerminalProblem(t, t);
                if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            }
        }

        void DrawAircraft(Stand stand)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Aircraft service configuration", EditorStyles.boldLabel);
            bool beforeSelection = GUI.changed;
            aircraft = (AircraftType)EditorGUILayout.ObjectField("Aircraft", aircraft, typeof(AircraftType), false);
            GUI.changed = beforeSelection;
            if (aircraft == null) return;
            var saved = stand.ServiceFor(aircraft);
            var configuration = saved ?? new StandServiceConfiguration { aircraft = aircraft };
            EditorGUI.BeginChangeCheck();
            configuration.allowed = EditorGUILayout.Toggle("May use this stand", configuration.allowed);
            if (!stand.DeIcing && stand.exit == StandExit.PushBack)
                configuration.stopOffset = EditorGUILayout.FloatField("Stop offset into stand (m)", configuration.stopOffset);
            foreach (var bridge in stand.bridges)
            {
                bool used = !configuration.unusedBridges.Contains(bridge.id);
                bool next = EditorGUILayout.Toggle("Use bridge " + (stand.bridges.IndexOf(bridge) + 1), used);
                if (next != used)
                {
                    if (next) configuration.unusedBridges.Remove(bridge.id);
                    else configuration.unusedBridges.Add(bridge.id);
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                if (saved == null) stand.serviceConfigurations.Add(configuration);
                GUI.changed = true;
            }
            var check = airport.Taxiways.CheckStand(stand, aircraft);
            if (check != StandProblem.None) EditorGUILayout.HelpBox(check.ToString(), MessageType.Warning);
            var plan = airport.Bridges.ServicePlan(stand, aircraft);
            for (int i = 0; i < stand.bridges.Count; i++)
                if (plan.TryGet(stand.bridges[i], out var entry))
                    EditorGUILayout.LabelField("Bridge " + (i + 1), entry.Operates ? "Automatic → " + entry.DoorName :
                        entry.problem == BridgeProblem.None ? "No terminal connection" : BridgeLayout.Describe(entry.problem));
            bool beforePreview = GUI.changed;
            preview = EditorGUILayout.Toggle("Preview aircraft and docking", preview);
            if (preview) previewProgress = EditorGUILayout.Slider("Docking preview", previewProgress, 0f, 1f);
            GUI.changed = beforePreview;
            if (saved != null && GUILayout.Button("Reset this aircraft to automatic defaults"))
            { stand.serviceConfigurations.Remove(saved); GUI.changed = true; }
        }

        void DrawBridges(Stand stand)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Physical bridges", EditorStyles.boldLabel);
            for (int i = 0; i < stand.bridges.Count; i++)
            {
                var b = stand.bridges[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Bridge " + (i + 1), EditorStyles.boldLabel);
                b.definition = (JetBridgeDefinition)EditorGUILayout.ObjectField("Bridge family", b.definition, typeof(JetBridgeDefinition), false);
                if (rig != null && GUILayout.Button("Choose supplied bridge family"))
                {
                    var menu = new GenericMenu();
                    foreach (var d in rig.Definitions)
                    {
                        var definition = d;
                        menu.AddItem(new GUIContent(d.name), b.definition == d, () =>
                        { Undo.RecordObject(airport.Taxiways, "Change bridge family"); b.definition = definition; Changed(); });
                    }
                    menu.ShowAsContext();
                }
                b.appearance = (JetBridgeAppearance)EditorGUILayout.ObjectField("Appearance", b.appearance, typeof(JetBridgeAppearance), false);
                b.rotunda = EditorGUILayout.Vector2Field("Base (across / into stand)", b.rotunda);
                b.root = EditorGUILayout.Vector2Field("Corridor start (across / into)", b.root);
                b.rotundaFloor = Mathf.Max(0f, EditorGUILayout.FloatField("Base floor (0 = family)", b.rotundaFloor));
                b.connectionFloor = Mathf.Max(0f, EditorGUILayout.FloatField("Connection floor (0 = terminal)", b.connectionFloor));
                if (b.definition == null) b.tunnel = (TunnelSize)EditorGUILayout.EnumPopup("Legacy tunnel", b.tunnel);
                EditorGUILayout.LabelField("Reach", $"{b.MinTunnel:0.#}–{b.MaxTunnel:0.#} m; doors chosen automatically");
                DrawPath(b.linkBends, b.root, b.rotunda, b.Floor);
                DrawSupports(b.supportColumns, "Corridor supports");
                if (GUILayout.Button("Use current automatic supports"))
                {
                    var sl = airport.Taxiways.Layout(stand);
                    var l = BridgeLayout.Of(b, sl, airport.terminals);
                    var columns = l.Columns(airport.Bridges.Site(stand).pavement);
                    if (columns != null) b.supportColumns = columns.ConvertAll(p => BridgeLayout.ToStand(sl, p));
                    GUI.changed = true;
                }
                if (GUILayout.Button("Remove bridge " + (i + 1)))
                { stand.bridges.RemoveAt(i); GUI.changed = true; EditorGUILayout.EndVertical(); break; }
                EditorGUILayout.EndVertical();
            }
            using (new EditorGUI.DisabledScope(stand.bridges.Count >= JetBridgeRules.MaxPerStand || stand.exit != StandExit.PushBack || stand.DeIcing))
                if (GUILayout.Button("Add bridge beside stand"))
                {
                    var sl = airport.Taxiways.Layout(stand);
                    var b = new JetBridge { rotunda = new Vector2(-15f, sl.noseS), root = new Vector2(-15f, sl.far + 10f) };
                    stand.bridges.Add(b);
                    airport.Bridges.MoveBridge(stand, b, sl.At(b.rotunda.x, b.rotunda.y), null, out _, out _);
                    GUI.changed = true;
                }
        }

        static void DrawPath(List<Vector3> path, Vector2 root, Vector2 end, float floor)
        {
            EditorGUILayout.LabelField("Fixed corridor bends", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Coordinates: across, floor height, into stand", EditorStyles.miniLabel);
            for (int i = 0; i < path.Count; i++)
            {
                path[i] = EditorGUILayout.Vector3Field("Bend " + (i + 1), path[i]);
                if (GUILayout.Button("Remove bend " + (i + 1))) { path.RemoveAt(i); GUI.changed = true; break; }
            }
            if (GUILayout.Button("Add corridor bend"))
            {
                var from = path.Count > 0 ? new Vector2(path[path.Count - 1].x, path[path.Count - 1].z) : root;
                var p = (from + end) * 0.5f;
                path.Add(new Vector3(p.x, floor, p.y)); GUI.changed = true;
            }
        }

        static void DrawSupports(List<Vector2> supports, string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (supports.Count == 0) EditorGUILayout.LabelField("Automatically placed clear of roads", EditorStyles.miniLabel);
            for (int i = 0; i < supports.Count; i++) supports[i] = EditorGUILayout.Vector2Field("Support " + (i + 1), supports[i]);
            if (supports.Count > 0 && GUILayout.Button("Place supports automatically")) { supports.Clear(); GUI.changed = true; }
        }

        void DrawHead(BridgeHead head, string title, Stand stand)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            head.from = EditorGUILayout.Vector2Field("Near end", head.from);
            head.to = EditorGUILayout.Vector2Field("Far end", head.to);
            if (!head.Exists) return;
            head.foundation = (HeadFoundation)EditorGUILayout.EnumPopup("Foundation", head.foundation);
            head.appearance = (JetBridgeAppearance)EditorGUILayout.ObjectField("Appearance", head.appearance, typeof(JetBridgeAppearance), false);
            head.root = EditorGUILayout.Vector2Field("Main corridor start", head.root);
            float length = Vector2.Distance(head.from, head.to);
            float angle = Mathf.Atan2(head.to.x - head.from.x, head.to.y - head.from.y) * Mathf.Rad2Deg;
            float nextLength = Mathf.Clamp(EditorGUILayout.FloatField("Length (m)", length), JetBridgeRules.MinHead, JetBridgeRules.MaxHead);
            float nextAngle = EditorGUILayout.FloatField("Heading in stand (degrees)", angle);
            if (!Mathf.Approximately(length, nextLength) || !Mathf.Approximately(angle, nextAngle))
                head.to = head.from + new Vector2(Mathf.Sin(nextAngle * Mathf.Deg2Rad), Mathf.Cos(nextAngle * Mathf.Deg2Rad)) * nextLength;
            head.width = Mathf.Max(0f, EditorGUILayout.FloatField("Width (0 = standard)", head.width));
            head.floor = Mathf.Max(0f, EditorGUILayout.FloatField("Floor (0 = standard)", head.floor));
            head.upperFloor = Mathf.Max(0f, EditorGUILayout.FloatField("Upper floor (0 = standard)", head.upperFloor));
            head.connectionFloor = Mathf.Max(0f, EditorGUILayout.FloatField("Connection floor (0 = terminal)", head.connectionFloor));
            DrawPath(head.linkBends, head.root, head.from, head.floor > 0f ? head.floor : JetBridgeRules.RotundaFloor);
            DrawSupports(head.linkSupportColumns, "Main corridor supports");
            if (head.foundation == HeadFoundation.Columns) DrawSupports(head.supportColumns, "Head supports");
            DrawExterior(head.Exterior, 4, head.foundation == HeadFoundation.Building);
            var sl = airport.Taxiways.Layout(stand);
            var h = HeadLayout.All(sl, airport.terminals).First(x => x.head == head);
            if (GUILayout.Button("Use automatic head supports"))
            {
                if (h.Columns(airport.Bridges.Site(stand).pavement))
                {
                    head.supportColumns = h.columns.ConvertAll(p => BridgeLayout.ToStand(sl, p));
                    head.linkSupportColumns = h.linkColumns.ConvertAll(p => BridgeLayout.ToStand(sl, p));
                }
                GUI.changed = true;
            }
            var problem = h.Placement(airport.Bridges.Site(stand));
            if (problem != BridgeProblem.None) EditorGUILayout.HelpBox(BridgeLayout.Describe(problem), MessageType.Warning);
        }

        int facadeEdge = -1;
        void DrawExterior(BuildingExterior exterior, int edges, bool canLean = true)
        {
            var names = Enum.GetNames(typeof(FacadeStyle));
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Facade style", (int)exterior.style, names);
            if (EditorGUI.EndChangeCheck() && selected >= 0) FacadeStyles.Apply(exterior, (FacadeStyle)selected);
            var tints = Enum.GetNames(typeof(GlassTint));
            int tint = Array.FindIndex(tints, name => FacadeStyles.Tint((GlassTint)Enum.Parse(typeof(GlassTint), name)) == exterior.facade.glassTint);
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("Glass tint", tint, tints);
            if (EditorGUI.EndChangeCheck() && next >= 0) FacadeStyles.SetTint(exterior, (GlassTint)next);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Building shape", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!canLean))
            {
                bool before = GUI.changed;
                string[] walls = new[] { "All walls" }.Concat(Enumerable.Range(1, edges).Select(i => "Wall " + i)).ToArray();
                facadeEdge = EditorGUILayout.Popup("Angle applies to", Mathf.Clamp(facadeEdge + 1, 0, edges), walls) - 1;
                GUI.changed = before;
                float angle = facadeEdge < 0 ? exterior.facade.lean : exterior.ForFace(facadeEdge).lean;
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.Slider("Facade angle (degrees)", angle, -30f, 30f);
                if (EditorGUI.EndChangeCheck())
                {
                    if (facadeEdge >= 0) exterior.Override(facadeEdge).lean = value;
                    else { exterior.facade.lean = value; foreach (var face in exterior.faces) face.facade.lean = value; }
                }
            }
            var roof = exterior.roofShape;
            if (FacadeStyles.Geometry(exterior).pierWidth > 0f) exterior.columnGap = EditorGUILayout.Slider("Column gap (m)", exterior.columnGap, 0f, 6f);
            roof.overhang = EditorGUILayout.Slider("Roof extension (m)", roof.overhang, 0f, 8f);
            roof.form = (RoofForm)EditorGUILayout.EnumPopup("Roof form", roof.form);
            if (roof.form != RoofForm.Flat)
            {
                roof.rise = EditorGUILayout.Slider("Roof rise (m)", roof.rise, 0f, 12f);
                roof.across = EditorGUILayout.Toggle("Pattern across building", roof.across);
            }
        }

        void DrawPreset(Stand stand)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Reusable gate layout", EditorStyles.boldLabel);
            bool beforeSelection = GUI.changed;
            preset = (GateLayoutPreset)EditorGUILayout.ObjectField("Preset", preset, typeof(GateLayoutPreset), false);
            mirror = EditorGUILayout.Toggle("Mirror across stand", mirror);
            GUI.changed = beforeSelection;
            if (GUILayout.Button("Save layout as preset"))
            {
                string path = EditorUtility.SaveFilePanelInProject("Save gate layout", "Gate layout", "asset", "Choose where to save the reusable layout.");
                if (!string.IsNullOrEmpty(path))
                {
                    preset = CreateInstance<GateLayoutPreset>(); preset.Capture(stand);
                    AssetDatabase.CreateAsset(preset, path); AssetDatabase.SaveAssets();
                }
            }
            using (new EditorGUI.DisabledScope(preset == null))
                if (GUILayout.Button("Apply preset"))
                {
                    message = preset.Apply(airport, stand, mirror, out var problem) ? "Gate layout applied." : BridgeLayout.Describe(problem);
                    GUI.changed = true;
                }
        }

        void Changed()
        {
            if (airport == null) return;
            airport.BuildGround();
            airport.Taxiways.Rebuild();
            EditorUtility.SetDirty(airport.Taxiways); EditorUtility.SetDirty(airport);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(airport.gameObject.scene);
            UpdatePreview(); SceneView.RepaintAll(); Repaint();
        }

        void ClearPreview() => aircraftPreview.Dispose();

        void UpdatePreview()
        {
            var stand = Selected;
            if (!preview || aircraft == null || stand == null) { ClearPreview(); return; }
            aircraftPreview.Show(airport, stand, aircraft, AircraftSetup.Prefab(aircraft), previewProgress);
        }

        static class Plane
        {
            public static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
            public static Vector3 X0Z(Vector2 p, float y = 0f) => new Vector3(p.x, y, p.y);
        }

        Vector3 Snap(Vector3 p) => new Vector3(Mathf.Round(p.x / grid) * grid, p.y, Mathf.Round(p.z / grid) * grid);

        void SceneGUI(SceneView view)
        {
            var stand = Selected;
            if (stand == null) return;
            var sl = airport.Taxiways.Layout(stand);
            var transform = airport.transform;
            Undo.RecordObject(airport.Taxiways, "Move gate component");
            EditorGUI.BeginChangeCheck();
            Vector2 Grip(Vector2 point, float floor, string label, System.Action<float> changeHeight = null)
            {
                var world = transform.TransformPoint(Plane.X0Z(sl.At(point.x, point.y), floor));
                Handles.Label(world, label);
                var next = Handles.PositionHandle(world, transform.rotation);
                if (next == world) return point;
                var local = Snap(transform.InverseTransformPoint(next));
                if (Mathf.Abs(local.y - floor) > 0.01f) changeHeight?.Invoke(Mathf.Max(local.y, 0.5f));
                return BridgeLayout.ToStand(sl, Plane.XZ(local));
            }
            for (int i = 0; i < stand.bridges.Count; i++)
            {
                var b = stand.bridges[i];
                b.rotunda = Grip(b.rotunda, b.Floor, "Bridge " + (i + 1), f => b.rotundaFloor = f);
                float connection = BridgeLayout.Of(b, sl, airport.terminals).LinkStartFloor;
                b.root = Grip(b.root, connection, "Corridor start", f => b.connectionFloor = f);
                for (int j = 0; j < b.linkBends.Count; j++)
                {
                    var p = b.linkBends[j];
                    var world = transform.TransformPoint(Plane.X0Z(sl.At(p.x, p.z), p.y));
                    var moved = Handles.PositionHandle(world, transform.rotation);
                    if (moved == world) continue;
                    var next = Snap(transform.InverseTransformPoint(moved));
                    var frame = BridgeLayout.ToStand(sl, Plane.XZ(next));
                    b.linkBends[j] = new Vector3(frame.x, next.y, frame.y);
                }
            }
            foreach (var h in stand.BridgeHeads)
            {
                float floor = h.floor > 0f ? h.floor : 4f;
                h.from = Grip(h.from, floor, "Head near", f => h.floor = f);
                h.to = Grip(h.to, floor, "Head far", f => h.floor = f);
            }
            if (aircraft != null && stand.exit == StandExit.PushBack && !stand.DeIcing)
            {
                var at = transform.TransformPoint(Plane.X0Z(sl.StopFor(aircraft)));
                Handles.Label(at, aircraft.TypeName + " nose-wheel stop");
                var next = Handles.Slider(at, transform.TransformDirection(Plane.X0Z(sl.axis)));
                if (next != at)
                {
                    var c = stand.ServiceFor(aircraft);
                    if (c == null) stand.serviceConfigurations.Add(c = new StandServiceConfiguration { aircraft = aircraft });
                    float offset = Vector2.Dot(Plane.XZ(transform.InverseTransformPoint(next)) - sl.stop, sl.axis);
                    c.stopOffset = Mathf.Round(offset / grid) * grid;
                }
            }
            if (EditorGUI.EndChangeCheck()) Changed();
        }
    }
}
