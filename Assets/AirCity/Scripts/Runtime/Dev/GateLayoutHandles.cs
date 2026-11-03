using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>Runtime corridor editing in the world. Changes are checked before being kept; Escape restores the
    /// complete gesture, including a provisional corner and any bridges carried by a concourse.</summary>
    public sealed class GateLayoutHandles : IDisposable
    {
        public enum Kind { Connector, Base, Bend, Insert, Column, HeadEnd, HeadColumn }
        public readonly struct Handle
        {
            public readonly Kind kind;
            public readonly int index;
            public readonly Vector3 point; // across, floor, into the stand
            public Handle(Kind kind, int index, Vector3 point) { this.kind = kind; this.index = index; this.point = point; }
        }
        Airport airport;
        Stand stand;
        JetBridge bridge;
        BridgeHead head;
        readonly List<Handle> nodes = new List<Handle>();
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<Vector2> automaticColumns = new List<Vector2>(), automaticHeadColumns = new List<Vector2>();
        int version = -1, inputFrame = -1, hover = -1;
        bool columnsShown, consumed, heightDrag;
        Handle? selected;
        Snapshot gesture;
        Vector3 startPoint;
        Vector2 startMouse;
        Plane dragPlane;
        float heightPixels;
        Vector3 grabOffset;
        Vector3? refusedPoint;
        Kind refusedKind;
        public bool IsDragging => gesture != null;
        public bool HasSelection => selected.HasValue;
        public string Problem { get; private set; }
        public IReadOnlyList<Handle> Nodes => nodes;
        List<Vector3> Bends => bridge != null ? bridge.linkBends : head.linkBends;
        List<Vector2> Columns => bridge != null ? bridge.supportColumns : head.linkSupportColumns;

        public void Bind(Airport nextAirport, Stand nextStand, JetBridge nextBridge, BridgeHead nextHead)
        {
            if (airport == nextAirport && stand == nextStand && bridge == nextBridge && head == nextHead) return;
            Clear(); airport = nextAirport; stand = nextStand; bridge = nextBridge; head = nextHead; version = -1;
        }
        public void Clear()
        {
            Cancel(); nodes.Clear(); path.Clear(); selected = null; hover = -1; Problem = null;
            airport = null; stand = null; bridge = null; head = null; version = -1;
        }
        public void Dispose() => Clear();
        public void Refresh(bool showColumns = false)
        {
            if (airport == null || stand == null || bridge == null && head == null) { nodes.Clear(); path.Clear(); return; }
            showColumns |= IsDragging && (selected?.kind == Kind.Column || selected?.kind == Kind.HeadColumn);
            if (version == airport.LayoutVersion && columnsShown == showColumns) return;
            version = airport.LayoutVersion; columnsShown = showColumns;
            nodes.Clear(); path.Clear(); automaticColumns.Clear(); automaticHeadColumns.Clear();
            var sl = airport.Taxiways.Layout(stand); if (sl == null) return;
            float start, floor; Vector2 root, end;
            if (bridge != null)
            {
                var layout = BridgeLayout.Of(bridge, sl, airport.terminals);
                root = bridge.root; end = bridge.rotunda; start = layout.LinkStartFloor; floor = bridge.Floor;
                if (showColumns)
                {
                    var points = layout.Columns(airport.Bridges.Site(stand).pavement);
                    if (points != null) automaticColumns.AddRange(points.ConvertAll(p => BridgeLayout.ToStand(sl, p)));
                }
            }
            else
            {
                var layout = HeadLayout.All(sl, airport.terminals).FirstOrDefault(h => h.head == head);
                if (layout == null) return;
                root = head.root; end = head.from; start = layout.ConnectionFloor; floor = layout.Floor;
                if (showColumns && layout.Columns(airport.Bridges.Site(stand).pavement))
                {
                    automaticColumns.AddRange((layout.linkColumns ?? new List<Vector2>()).ConvertAll(p => BridgeLayout.ToStand(sl, p)));
                    automaticHeadColumns.AddRange((layout.columns ?? new List<Vector2>()).ConvertAll(p => BridgeLayout.ToStand(sl, p)));
                }
                nodes.Add(new Handle(Kind.HeadEnd, 0, new Vector3(head.to.x, floor, head.to.y)));
            }
            path.Add(new Vector3(root.x, start, root.y)); path.AddRange(Bends); path.Add(new Vector3(end.x, floor, end.y));
            nodes.Add(new Handle(Kind.Connector, 0, path[0]));
            nodes.Add(new Handle(Kind.Base, 0, path[path.Count - 1]));
            for (int i = 0; i < Bends.Count; i++) nodes.Add(new Handle(Kind.Bend, i, Bends[i]));
            for (int i = 0; i + 1 < path.Count; i++)
                if (Vector3.Distance(path[i], path[i + 1]) > 2f) nodes.Add(new Handle(Kind.Insert, i, (path[i] + path[i + 1]) * 0.5f));
            for (int i = 0; i < automaticColumns.Count; i++)
                nodes.Add(new Handle(Kind.Column, i, new Vector3(automaticColumns[i].x, 0f, automaticColumns[i].y)));
            for (int i = 0; i < automaticHeadColumns.Count; i++)
                nodes.Add(new Handle(Kind.HeadColumn, i, new Vector3(automaticHeadColumns[i].x, 0f, automaticHeadColumns[i].y)));
        }

        public bool InsertCorner(int segment)
        {
            Refresh(columnsShown);
            if (segment < 0 || segment + 1 >= path.Count) return false;
            var point = (path[segment] + path[segment + 1]) * 0.5f;
            return Change(() => { Bends.Insert(segment, point); Columns.Clear(); });
        }
        public bool RemoveCorner(int index)
        {
            if (airport == null || bridge == null && head == null) return false;
            if (index < 0 || index >= Bends.Count) return false;
            bool result = Change(() => { Bends.RemoveAt(index); Columns.Clear(); });
            if (result) selected = null;
            return result;
        }
        public bool Move(Handle handle, Vector3 point, bool vertical = false)
        {
            if (airport == null || !Finite(point)) return false;
            var sl = airport.Taxiways.Layout(stand); if (sl == null) return false;
            bool result = Change(() =>
            {
                var xy = new Vector2(point.x, point.z);
                if (handle.kind == Kind.Bend)
                { Bends[handle.index] = point; Columns.Clear(); }
                else if (handle.kind == Kind.Column)
                {
                    if (Columns.Count == 0) Columns.AddRange(automaticColumns);
                    // Corridor columns slide along their corridor, rather than becoming detached from it.
                    float best = float.MaxValue; var nearest = xy;
                    for (int i = 1; i < path.Count; i++)
                    {
                        var q = Geo.ClosestOnSegment(xy, new Vector2(path[i - 1].x, path[i - 1].z), new Vector2(path[i].x, path[i].z), out _);
                        if ((q - xy).sqrMagnitude < best) { best = (q - xy).sqrMagnitude; nearest = q; }
                    }
                    Columns[handle.index] = nearest;
                }
                else if (handle.kind == Kind.HeadColumn)
                {
                    if (head.supportColumns.Count == 0) head.supportColumns.AddRange(automaticHeadColumns);
                    head.supportColumns[handle.index] = xy;
                }
                else if (handle.kind == Kind.Connector)
                {
                    if (vertical)
                    {
                        // A corridor from a terminal meets one of its floors: keep the floor nearest the drag.
                        Terminal terminal = null;
                        if (bridge != null) { var l = BridgeLayout.Of(bridge, sl, airport.terminals); if (l.head == null) terminal = l.terminal; }
                        else { var h = HeadLayout.All(sl, airport.terminals).FirstOrDefault(x => x.head == head); if (h != null && h.parent == null) terminal = h.terminal; }
                        float level = terminal != null ? terminal.BoardingLevel(point.y) : point.y;
                        if (bridge != null) bridge.connectionFloor = level; else head.connectionFloor = level;
                    }
                    else
                    {
                        var world = sl.At(xy.x, xy.y); float best = float.MaxValue; var wall = world;
                        foreach (var terminal in airport.terminals)
                        {
                            float distance = terminal.ClosestOnWall(world, handle.point.y, out var q, out _);
                            if (distance < best) { best = distance; wall = q; }
                        }
                        foreach (var other in HeadLayout.All(sl, airport.terminals))
                        {
                            if (other.head == head) continue;
                            float distance = other.ClosestOnWall(world, handle.point.y, out var q);
                            if (distance < best) { best = distance; wall = q; }
                        }
                        xy = BridgeLayout.ToStand(sl, wall);
                        if (bridge != null) bridge.root = xy; else head.root = xy;
                    }
                    Columns.Clear();
                }
                else if (bridge != null)
                {
                    if (vertical) bridge.rotundaFloor = point.y; else bridge.rotunda = xy;
                    Columns.Clear();
                }
                else
                {
                    var before = head.Clone();
                    var attached = stand.bridges.Where(b => BridgeLayout.Of(b, sl, airport.terminals).head?.head == head).ToArray();
                    if (vertical)
                    {
                        float delta = point.y - (head.floor > 0f ? head.floor : JetBridgeRules.RotundaFloor);
                        head.floor = point.y; if (head.upperFloor > 0f) head.upperFloor += delta;
                        foreach (var b in attached) b.rotundaFloor = b.Floor + delta;
                    }
                    else
                    {
                        if (handle.kind == Kind.HeadEnd) head.to = xy; else head.from = xy;
                        var oldDir = (before.to - before.from).normalized; var dir = (head.to - head.from).normalized;
                        var origin = handle.kind == Kind.Base ? before.to : before.from;
                        var destination = handle.kind == Kind.Base ? head.to : head.from;
                        Vector2 Carry(Vector2 p) => destination + dir * Vector2.Dot(p - origin, oldDir) + Geo.Right(dir) * Vector2.Dot(p - origin, Geo.Right(oldDir));
                        foreach (var b in attached)
                        {
                            b.rotunda = Carry(b.rotunda); b.root = Carry(b.root);
                            b.linkBends = b.linkBends.ConvertAll(p => { var q = Carry(new Vector2(p.x, p.z)); return new Vector3(q.x, p.y, q.y); });
                            b.supportColumns.Clear();
                        }
                    }
                    Columns.Clear(); head.supportColumns.Clear();
                }
            });
            refusedPoint = result ? null : (Vector3?)point; refusedKind = handle.kind;
            return result;
        }

        public bool BeginEdit(Handle handle)
        {
            if (airport == null || IsDragging) return false;
            var original = new Snapshot(stand, bridge, head);
            if (handle.kind == Kind.Insert)
            {
                if (!InsertCorner(handle.index)) return false;
                handle = new Handle(Kind.Bend, handle.index, handle.point);
            }
            selected = handle; gesture = original; startPoint = handle.point; DevGui.WorldHandleCaptured = true; return true;
        }

        bool Change(Action mutation)
        {
            var before = new Snapshot(stand, bridge, head);
            mutation(); airport.LayoutChanged();
            var sl = airport.Taxiways.Layout(stand); var site = airport.Bridges.Site(stand);
            BridgeProblem problem = bridge != null ? BridgeLayout.Of(bridge, sl, airport.terminals).Placement(site) :
                Vector2.Distance(head.from, head.to) < JetBridgeRules.MinHead || Vector2.Distance(head.from, head.to) > JetBridgeRules.MaxHead
                    ? BridgeProblem.InvalidDefinition : HeadLayout.All(sl, airport.terminals).First(h => h.head == head).Placement(site);
            if (problem != BridgeProblem.None) { before.Restore(); airport.LayoutChanged(); Problem = BridgeLayout.Describe(problem); }
            else Problem = null;
            var rig = airport.GetComponent<BridgeRig>(); if (rig != null) rig.Refresh(); version = -1;
            return problem == BridgeProblem.None;
        }
        static bool Finite(Vector3 p) => BuildingFacade.Finite(p.x) && BuildingFacade.Finite(p.y) && BuildingFacade.Finite(p.z) && p.y >= 0f && p.y <= 40f;

        public bool Input(Camera cam, Mouse mouse, Keyboard keys, Rect panel)
        {
            if (inputFrame == Time.frameCount) return consumed;
            inputFrame = Time.frameCount; consumed = false;
            if (cam == null || mouse == null || keys == null || airport == null) return false;
            Refresh(keys.altKey.isPressed);
            var screen = mouse.position.ReadValue(); var gui = new Vector2(screen.x, Screen.height - screen.y);
            hover = !panel.Contains(gui) && !DevGui.PointerOverHud ? Pick(cam, gui) : -1;
            if (IsDragging)
            {
                consumed = true; DevGui.WorldHandleCaptured = true;
                if (keys.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) { Cancel(); return true; }
                if (!mouse.leftButton.isPressed) { gesture = null; refusedPoint = null; DevGui.WorldHandleCaptured = false; return true; }
                if (mouse.delta.ReadValue().sqrMagnitude < 0.01f) return true;
                var value = startPoint;
                if (heightDrag) value.y = Mathf.Clamp(startPoint.y - (gui.y - startMouse.y) / heightPixels, 0.5f, 40f);
                else if (dragPlane.Raycast(cam.ScreenPointToRay(screen), out float distance))
                {
                    var local = airport.transform.InverseTransformPoint(cam.ScreenPointToRay(screen).GetPoint(distance) - grabOffset);
                    var sl = airport.Taxiways.Layout(stand); var q = BridgeLayout.ToStand(sl, Geo.XZ(local)); value.x = q.x; value.z = q.y;
                }
                if (keys.ctrlKey.isPressed || keys.leftCommandKey.isPressed || keys.rightCommandKey.isPressed)
                {
                    if (heightDrag) value.y = Mathf.Round(value.y * 2f) * 0.5f;
                    else { value.x = Mathf.Round(value.x * 2f) * 0.5f; value.z = Mathf.Round(value.z * 2f) * 0.5f; }
                }
                Move(selected.Value, value, heightDrag); return true;
            }
            if (selected.HasValue && selected.Value.kind == Kind.Bend && (keys.deleteKey.wasPressedThisFrame || keys.backspaceKey.wasPressedThisFrame))
            { RemoveCorner(selected.Value.index); consumed = true; return true; }
            if (hover < 0) return false;
            var node = nodes[hover];
            if (node.kind == Kind.Bend && mouse.rightButton.wasPressedThisFrame)
            { RemoveCorner(node.index); consumed = true; return true; }
            if (!mouse.leftButton.wasPressedThisFrame) return false;
            if (!BeginEdit(node)) return consumed = true;
            node = selected.Value; startMouse = gui;
            heightDrag = keys.shiftKey.isPressed && node.kind != Kind.Column && node.kind != Kind.HeadColumn;
            var world = World(node.point, node.kind); dragPlane = new Plane(airport.transform.up, world);
            var ray = cam.ScreenPointToRay(screen);
            grabOffset = dragPlane.Raycast(ray, out float grabDistance) ? ray.GetPoint(grabDistance) - world : Vector3.zero;
            heightPixels = Mathf.Max(3f, Vector2.Distance(cam.WorldToScreenPoint(world), cam.WorldToScreenPoint(world + airport.transform.up)));
            DevGui.WorldHandleCaptured = true; consumed = true; return true;
        }
        public void Cancel()
        {
            if (gesture != null && airport != null)
            { gesture.Restore(); airport.LayoutChanged(); var rig = airport.GetComponent<BridgeRig>(); if (rig != null) rig.Refresh(); }
            gesture = null; refusedPoint = null; Problem = null; version = -1; DevGui.WorldHandleCaptured = false;
        }
        int Pick(Camera cam, Vector2 mouse)
        {
            int result = -1; float best = GateDesignerGui.U(17f);
            for (int i = 0; i < nodes.Count; i++)
            {
                var p = cam.WorldToScreenPoint(World(nodes[i].point, nodes[i].kind)); if (p.z <= 0f) continue;
                float distance = Vector2.Distance(mouse, new Vector2(p.x, Screen.height - p.y));
                if (distance < best) { best = distance; result = i; }
            }
            return result;
        }
        Vector3 World(Vector3 point, Kind kind)
        {
            var sl = airport.Taxiways.Layout(stand); var q = sl.At(point.x, point.z);
            return airport.transform.TransformPoint(Geo.X0Z(q, point.y + (kind == Kind.Column || kind == Kind.HeadColumn ? 0.15f : 2.95f)));
        }
        public void Draw(Camera cam)
        {
            if (airport == null || cam == null || !DevGui.Visible) return;
            Refresh(Keyboard.current != null && Keyboard.current.altKey.isPressed);
            Vector2 ScreenPoint(Vector3 p, Kind kind) { var q = cam.WorldToScreenPoint(World(p, kind)); return new Vector2(q.x, Screen.height - q.y); }
            for (int i = 1; i < path.Count; i++)
                if (cam.WorldToScreenPoint(World(path[i - 1], Kind.Bend)).z > 0f && cam.WorldToScreenPoint(World(path[i], Kind.Bend)).z > 0f)
                {
                    var a = ScreenPoint(path[i - 1], Kind.Bend); var b = ScreenPoint(path[i], Kind.Bend);
                    GateDesignerGui.Line(a, b, new Color(1, 1, 1, 0.9f), 5);
                    GateDesignerGui.Line(a, b, IsDragging ? Problem == null ? GateDesignerGui.Teal : GateDesignerGui.Red : GateDesignerGui.Blue, 2);
                }
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i]; if (cam.WorldToScreenPoint(World(node.point, node.kind)).z <= 0f) continue;
                bool insert = node.kind == Kind.Insert, column = node.kind == Kind.Column || node.kind == Kind.HeadColumn;
                bool active = selected.HasValue && selected.Value.kind == node.kind && selected.Value.index == node.index;
                float size = GateDesignerGui.U(column ? 14 : insert ? 20 : 26);
                var p = ScreenPoint(node.point, node.kind); var rect = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);
                GateDesignerGui.Fill(new Rect(rect.x, rect.y + GateDesignerGui.U(2), size, size), new Color(0.04f, 0.12f, 0.19f, 0.25f), 30);
                GateDesignerGui.Fill(rect, active || hover == i ? GateDesignerGui.Blue : Color.white, 30);
                GateDesignerGui.Outline(rect, GateDesignerGui.Blue, 30, active ? 2 : 1);
                GUI.Label(rect, insert ? "+" : column ? "·" : node.kind == Kind.Connector ? "□" : "•", active || hover == i ? GateDesignerGui.ButtonOn : GateDesignerGui.Centre);
                if (hover == i || active)
                {
                    string name = insert ? "ADD CORNER" : column ? "COLUMN" : node.kind == Kind.Bend ? "CORRIDOR CORNER" : node.kind == Kind.Connector ? "CONNECTION" : bridge != null ? "BRIDGE BASE" : "CONCOURSE";
                    string text = name + (column || insert ? "" : $"   {node.point.y:0.#} m");
                    float width = GateDesignerGui.Small.CalcSize(new GUIContent(text)).x + GateDesignerGui.U(24);
                    var label = new Rect(p.x + size * 0.7f, p.y - GateDesignerGui.U(30), width, GateDesignerGui.U(28));
                    GateDesignerGui.Fill(label, Color.white, 6); GUI.Label(new Rect(label.x + GateDesignerGui.U(12), label.y, label.width - GateDesignerGui.U(24), label.height), text, GateDesignerGui.Small);
                }
            }
            if (IsDragging && refusedPoint.HasValue)
            {
                var p = ScreenPoint(refusedPoint.Value, refusedKind); float size = GateDesignerGui.U(30);
                var r = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);
                GateDesignerGui.Fill(r, Color.white, 30); GateDesignerGui.Outline(r, GateDesignerGui.Red, 30, 2);
                GUI.Label(r, "×", GateDesignerGui.ErrorCentre);
            }
        }

        sealed class Snapshot
        {
            readonly BridgeHead head, headCopy;
            readonly JetBridge bridge, copy;
            readonly List<(JetBridge bridge, JetBridge copy)> carried = new List<(JetBridge, JetBridge)>();
            public Snapshot(Stand stand, JetBridge bridge, BridgeHead head)
            {
                this.bridge = bridge; copy = bridge?.Clone(); this.head = head; headCopy = head?.Clone();
                if (head != null) foreach (var b in stand.bridges) carried.Add((b, b.Clone()));
            }
            static void Restore(JetBridge b, JetBridge source)
            {
                b.root = source.root; b.rotunda = source.rotunda; b.rotundaFloor = source.rotundaFloor; b.connectionFloor = source.connectionFloor;
                b.linkBends = new List<Vector3>(source.linkBends); b.supportColumns = new List<Vector2>(source.supportColumns);
            }
            public void Restore()
            {
                if (bridge != null) Restore(bridge, copy);
                if (head == null) return;
                head.from = headCopy.from; head.to = headCopy.to; head.root = headCopy.root; head.floor = headCopy.floor;
                head.upperFloor = headCopy.upperFloor; head.connectionFloor = headCopy.connectionFloor;
                head.linkBends = new List<Vector3>(headCopy.linkBends); head.linkSupportColumns = new List<Vector2>(headCopy.linkSupportColumns);
                head.supportColumns = new List<Vector2>(headCopy.supportColumns);
                foreach (var b in carried) Restore(b.bridge, b.copy);
            }
        }
    }
}
