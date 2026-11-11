using System.Collections.Generic;
using AirCity.Airfield;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Game
{
    /// <summary>Player-operated clearing/earthworks and placed airport construction, using terrain raycasts.</summary>
    [RequireComponent(typeof(CareerGame))]
    [DefaultExecutionOrder(100)]
    public sealed class CareerBuildTool : MonoBehaviour
    {
        public Material previewMaterial;
        public CareerTool Tool { get; private set; }
        public string Status { get; private set; }
        public int Price { get; private set; }
        public bool Valid { get; private set; }
        public float Radius = 45f, TargetHeight, BuildingHeading;
        public bool SingleTree;
        public IReadOnlyList<Vector2> Draft => draft;
        CareerGame game;
        LineRenderer line;
        readonly List<Vector2> draft = new List<Vector2>();
        bool dragging, dirtyStroke;
        Rect strokeBounds;
        Vector2 anchor, lastStamp, rightDown;
        float nextStamp;
        int pickedTree = -1;

        void Awake() { game = GetComponent<CareerGame>(); }
        public void ResetTool() { Tool = CareerTool.Select; dragging = dirtyStroke = false; draft.Clear(); if (line != null) line.enabled = false; }
        public void Choose(CareerTool tool)
        {
            game = GetComponent<CareerGame>();
            if (!game.State.handsOn || !game.State.layout.Unlocked(tool)) return;
            FinishStroke(); GetComponent<CareerRoadTool>().Cancel(); ResetTool(); Tool = tool;
            if (tool == CareerTool.Level) TargetHeight = game.State.HasRunway ? game.State.layout.elevation : game.State.Site.elevation;
            if (tool == CareerTool.Road) GetComponent<CareerRoadTool>().Begin();
            Status = Hint(tool);
        }
        public static string Hint(CareerTool tool) => tool switch {
            CareerTool.ClearTrees => "Click a tree or drag a clearing brush. Only selected trees are removed.",
            CareerTool.Level => "Drag over cleared ground to level it. Keep the brush clear of existing structures and pavement.",
            CareerTool.Runway => "Drag between runway thresholds. Clear and level its footprint first.",
            CareerTool.Taxiway => "Click a route from the runway. Enter finishes; right-click removes a corner.",
            CareerTool.Stand => "Click beside a taxiway to place parking. The side you click sets its direction.",
            CareerTool.Road => "Click your airport entrance, add corners, then finish on the valley road.",
            CareerTool.Reception => "Place reception beside the airport end of your public road. R rotates.",
            _ => "Choose a tool below. WASD moves, right drag orbits, scroll zooms."
        };

        void Update()
        {
            if (game == null || game.State == null || !game.State.handsOn || game.State.Site == null) return;
            var mouse = Mouse.current; var keys = Keyboard.current;
            if (keys != null && !game.view.KeyboardCaptured)
            {
                if (keys.escapeKey.wasPressedThisFrame) { FinishStroke(); Choose(CareerTool.Select); return; }
                if (keys.rKey.wasPressedThisFrame && Tool == CareerTool.Reception) BuildingHeading = Mathf.Repeat(BuildingHeading + 15f, 360f);
                if (keys.enterKey.wasPressedThisFrame && Tool == CareerTool.Taxiway) FinishTaxiway();
            }
            if (Tool == CareerTool.Select || Tool == CareerTool.Road || mouse == null)
            { if (line != null) line.enabled = false; return; }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (dragging && Tool == CareerTool.Runway && !game.view.PointerOverHud) PlaceRunway(anchor, EndPoint());
                dragging = false; FinishStroke();
            }
            if (game.view.PointerOverHud) { if (line != null) line.enabled = false; return; }
            if (!TryPoint(out var point)) { if (line != null) line.enabled = false; return; }
            Preview(point);
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (Tool == CareerTool.Runway)
                {
                    anchor = point;
                    if (game.State.HasRunway)
                    {
                        var l = game.State.layout;
                        float da = Vector2.Distance(point, l.runwayA), db = Vector2.Distance(point, l.runwayB);
                        if (Mathf.Min(da, db) > 30f) { Status = "Start the extension at a runway threshold."; return; }
                        anchor = da < db ? l.runwayB : l.runwayA;
                    }
                    dragging = true;
                }
                else if (Tool == CareerTool.Taxiway)
                {
                    point = game.State.layout.SnapTaxi(point, 16f);
                    if (draft.Count == 0 || Vector2.Distance(point, draft[draft.Count - 1]) >= 12f) draft.Add(point);
                }
                else if (Tool == CareerTool.Stand) PlaceStand(point);
                else if (Tool == CareerTool.Reception) PlaceReception(point);
                else Stamp(point);
            }
            else if (mouse.leftButton.isPressed && (Tool == CareerTool.ClearTrees && !SingleTree || Tool == CareerTool.Level) &&
                Time.unscaledTime >= nextStamp && Vector2.Distance(point, lastStamp) > Radius * .45f) Stamp(point);
            if (mouse.rightButton.wasPressedThisFrame) rightDown = mouse.position.ReadValue();
            if (Tool == CareerTool.Taxiway && mouse.rightButton.wasReleasedThisFrame && draft.Count > 0 &&
                Vector2.Distance(rightDown, mouse.position.ReadValue()) < 5f) draft.RemoveAt(draft.Count - 1);
        }

        public bool TryPoint(out Vector2 point)
        {
            point = default;
            if (game.valley.Terrain == null || Mouse.current == null) return false;
            var ray = game.view.GetComponent<Camera>().ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!game.valley.Terrain.GetComponent<TerrainCollider>().Raycast(ray, out var hit, 20000f)) return false;
            point = new Vector2(hit.point.x, hit.point.z); return true;
        }
        Vector2 EndPoint()
        {
            if (!TryPoint(out var point)) return anchor;
            var delta = point - anchor;
            if (game.State.HasRunway)
            {
                var l = game.State.layout; var axis = (l.runwayB - l.runwayA).normalized;
                if (Vector2.Dot(delta, axis) < 0f) axis = -axis;
                return anchor + axis * Mathf.Round(Vector2.Dot(delta, axis) / 10f) * 10f;
            }
            float angle = Mathf.Atan2(delta.y, delta.x);
            if (Keyboard.current == null || !Keyboard.current.shiftKey.isPressed) angle = Mathf.Round(angle / (5f * Mathf.Deg2Rad)) * 5f * Mathf.Deg2Rad;
            return anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Round(delta.magnitude / 10f) * 10f;
        }

        void Preview(Vector2 point)
        {
            var state = game.State; string why = null; Valid = true; Price = 0;
            var outline = new List<Vector2>();
            switch (Tool)
            {
                case CareerTool.ClearTrees:
                    pickedTree = SingleTree ? PickTree() : -1;
                    int count = SingleTree ? pickedTree >= 0 ? 1 : 0 : CareerConstruction.Trees(state, point, Radius).Count;
                    Price = count * CareerConstruction.TreePrice; Valid = count > 0 && state.funds >= Price;
                    if (SingleTree && pickedTree >= 0) point = state.world.trees[pickedTree].position;
                    Circle(outline, point, SingleTree ? 5f : Radius);
                    Status = count > 0 ? $"Remove {count} tree{(count == 1 ? "" : "s")} · ${Price:N0}" : "Choose trees to remove. Remaining woodland stays untouched.";
                    break;
                case CareerTool.Level:
                    Valid = CareerConstruction.Level(state, point, Radius, TargetHeight, false, out int grading, out why); Price = grading;
                    Circle(outline, point, Radius); Status = why ?? $"Level to {TargetHeight:0.0} m · ${Price:N0}"; break;
                case CareerTool.Runway:
                    if (!dragging) { Circle(outline, point, 5f); Status = "Drag the first threshold to the other end of your runway."; break; }
                    var end = EndPoint(); Valid = CareerConstruction.Runway(state, anchor, end, false, out int runway, out why); Price = runway;
                    Rectangle(outline, anchor, end, 9f); Status = why ?? $"{Vector2.Distance(anchor, end):N0} m grass runway · ${Price:N0}"; break;
                case CareerTool.Taxiway:
                    var route = new List<Vector2>(draft) { state.layout.SnapTaxi(point, 16f) };
                    if (draft.Count > 0 && Vector2.Distance(route[route.Count - 1], draft[draft.Count - 1]) < 1f) route.RemoveAt(route.Count - 1);
                    Valid = CareerConstruction.Taxiway(state, route, false, out int taxi, out why); Price = taxi;
                    outline.AddRange(route); Status = why ?? $"Taxiway · ${Price:N0}. Click corners, then Enter to build."; break;
                case CareerTool.Stand:
                    Valid = CareerConstruction.Parking(state, point, false, out why); Price = CareerConstruction.StandPrice;
                    var stand = CareerConstruction.StandPreview(state, point);
                    if (stand != null) { outline.AddRange(StandLayout.Of(stand).Pad); outline.Add(outline[0]); }
                    Status = why ?? $"Place light-aircraft parking · ${Price:N0}"; break;
                case CareerTool.Reception:
                    Valid = CareerConstruction.Reception(state, point, BuildingHeading, false, out why); Price = CareerConstruction.ReceptionPrice;
                    outline.AddRange(CareerConstruction.ReceptionCorners(point, BuildingHeading)); outline.Add(outline[0]);
                    Status = why ?? $"Passenger reception · ${Price:N0}. R rotates."; break;
            }
            Show(outline);
        }
        int PickTree()
        {
            var ray = game.view.GetComponent<Camera>().ScreenPointToRay(Mouse.current.position.ReadValue());
            int best = -1; float near = float.MaxValue;
            for (int i = 0; i < game.State.world.trees.Count; i++)
            {
                var t = game.State.world.trees[i]; if (t.removed || !CareerConstruction.Editable(game.State, t.position)) continue;
                var centre = new Vector3(t.position.x, game.valley.DisplayHeight(t.position) + t.height * .55f, t.position.y);
                float along = Vector3.Dot(centre - ray.origin, ray.direction); if (along < 0f || along >= near) continue;
                if (Vector3.Distance(ray.GetPoint(along), centre) > t.height * .35f) continue;
                near = along; best = i;
            }
            return best;
        }
        void Stamp(Vector2 point)
        {
            if (Tool == CareerTool.ClearTrees && SingleTree && pickedTree < 0) return;
            bool ok; string why;
            if (Tool == CareerTool.ClearTrees) ok = CareerConstruction.Clear(game.State, point, Radius, out why, SingleTree ? pickedTree : -1);
            else ok = CareerConstruction.Level(game.State, point, Radius, TargetHeight, true, out _, out why);
            if (ok)
            {
                var centre = Tool == CareerTool.ClearTrees && SingleTree ? game.State.world.trees[pickedTree].position : point;
                float radius = Tool == CareerTool.ClearTrees && SingleTree ? 0f : Radius;
                var area = new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f);
                strokeBounds = dirtyStroke ? Rect.MinMaxRect(Mathf.Min(strokeBounds.xMin, area.xMin), Mathf.Min(strokeBounds.yMin, area.yMin),
                    Mathf.Max(strokeBounds.xMax, area.xMax), Mathf.Max(strokeBounds.yMax, area.yMax)) : area;
                game.PlayerChanged(Tool, area); dirtyStroke = true;
            }
            else Status = why;
            lastStamp = point; nextStamp = Time.unscaledTime + .16f;
        }
        void FinishStroke()
        {
            if (dirtyStroke && game != null) game.FinishTerrainStroke(Tool, strokeBounds);
            dirtyStroke = false;
        }
        void PlaceRunway(Vector2 a, Vector2 b)
        {
            if (CareerConstruction.Runway(game.State, a, b, true, out _, out string why)) { game.PlayerChanged(CareerTool.Runway); game.Save(false); }
            else game.SetMessage(why);
        }
        public void FinishTaxiway()
        {
            if (CareerConstruction.Taxiway(game.State, draft, true, out _, out string why))
            { game.PlayerChanged(CareerTool.Taxiway); draft.Clear(); game.Save(false); }
            else game.SetMessage(why);
        }
        void PlaceStand(Vector2 point)
        {
            if (CareerConstruction.Parking(game.State, point, true, out string why)) { game.PlayerChanged(CareerTool.Stand); game.Save(false); }
            else game.SetMessage(why);
        }
        void PlaceReception(Vector2 point)
        {
            if (CareerConstruction.Reception(game.State, point, BuildingHeading, true, out string why)) { game.PlayerChanged(CareerTool.Reception); game.Save(false); Choose(CareerTool.Select); }
            else game.SetMessage(why);
        }
        static void Circle(List<Vector2> points, Vector2 centre, float radius)
        { for (int i = 0; i <= 64; i++) { float a = i / 64f * Mathf.PI * 2f; points.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius); } }
        static void Rectangle(List<Vector2> points, Vector2 a, Vector2 b, float half)
        { var side = new Vector2(-(b - a).normalized.y, (b - a).normalized.x) * half; points.AddRange(new[] { a - side, a + side, b + side, b - side, a - side }); }
        void Show(List<Vector2> outline)
        {
            if (line == null)
            {
                var go = new GameObject("Player build preview"); go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>(); line.sharedMaterial = previewMaterial; line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            line.enabled = outline.Count > 1; line.startWidth = line.endWidth = Mathf.Max(.5f, game.view.distance * .001f);
            line.startColor = line.endColor = Valid ? new Color(.95f, .77f, .35f) : new Color(.94f, .35f, .22f);
            var positions = new List<Vector3>();
            for (int i = 1; i < outline.Count; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(outline[i - 1], outline[i]) / 7f));
                for (int s = 0; s < steps; s++)
                {
                    var p = Vector2.Lerp(outline[i - 1], outline[i], s / (float)steps);
                    positions.Add(new Vector3(p.x, game.valley.DisplayHeight(p) + .7f, p.y));
                }
            }
            if (outline.Count > 0) { var p = outline[outline.Count - 1]; positions.Add(new Vector3(p.x, game.valley.DisplayHeight(p) + .7f, p.y)); }
            line.positionCount = positions.Count; line.SetPositions(positions.ToArray());
        }
    }
}
