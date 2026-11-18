using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Game
{
    /// <summary>Player-operated clearing/earthworks and placed airport construction, using terrain raycasts.</summary>
    [RequireComponent(typeof(CareerGame))]
    [DefaultExecutionOrder(100)]
    public sealed partial class CareerBuildTool : MonoBehaviour
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
        bool levelPointPinned;
        Vector2 levelPinnedPoint, levelPinnedPointer;
        float levelPinnedHeight, levelPinnedRadius;
        Vector4 levelPinnedCamera;
        Vector2 levelPinnedPivot;
        Vector2Int levelPinnedScreen;

        void Awake() { game = GetComponent<CareerGame>(); }
        public void FinishEditing() { FinishStroke(); ResetTool(); }
        public void ResetTool() { HideModelPreviews(); Tool = CareerTool.Select; dragging = dirtyStroke = levelPointPinned = false; draft.Clear(); if (line != null) line.enabled = false; }
        public void Choose(CareerTool tool)
        {
            game = GetComponent<CareerGame>();
            if (!game.State.handsOn || (!game.Sandbox && !game.State.layout.Unlocked(tool))) return;
            if (game.SharedTools && (tool == CareerTool.Runway || tool == CareerTool.Taxiway || tool == CareerTool.Stand || tool == CareerTool.Road || tool == CareerTool.Select))
            {
                FinishStroke(); ResetTool();
                game.BuildTools.SetTool(tool switch { CareerTool.Runway => AirCity.Dev.DevTool.Runway, CareerTool.Taxiway => AirCity.Dev.DevTool.Taxiway,
                    CareerTool.Stand => AirCity.Dev.DevTool.Stand, CareerTool.Road => AirCity.Dev.DevTool.Street, _ => AirCity.Dev.DevTool.Select });
                return;
            }
            FinishStroke(); GetComponent<CareerRoadTool>().Cancel(); ResetTool(); Tool = tool;
            if (tool == CareerTool.Level) TargetHeight = game.State.HasRunway ? game.State.layout.elevation : game.State.Site.elevation;
            if (tool == CareerTool.Road) GetComponent<CareerRoadTool>().Begin();
            Status = Hint(tool);
        }
        public static string Hint(CareerTool tool) => tool switch {
            CareerTool.ClearTrees => "Brush away trees, rocks, fallen timber and shrubs, or select one tree.",
            CareerTool.Level => "Click or drag to set the chosen height in one pass. Keep clear of structures and pavement.",
            CareerTool.Runway => "Drag between runway thresholds. Clear and level its footprint first.",
            CareerTool.Taxiway => "Click a route from the runway. Enter finishes; right-click removes a corner.",
            CareerTool.Stand => "Click beside a taxiway to place parking. The side you click sets its direction.",
            CareerTool.Road => "Click your airport entrance, add corners, then finish on the valley road.",
            CareerTool.Reception => "Place the visitor centre and parking beside a public road. The driveway leaves the right side of the lot. R rotates.",
            _ => "Choose a tool below. WASD moves, right drag orbits, scroll zooms."
        };

        void Update()
        {
            if (game == null || game.State == null || !game.State.handsOn || game.State.Site == null) { HideModelPreviews(); return; }
            var mouse = Mouse.current; var keys = Keyboard.current;
            PlacementScroll(keys, mouse);
            if (keys != null && !game.view.KeyboardCaptured)
            {
                if (keys.escapeKey.wasPressedThisFrame) { FinishStroke(); Choose(CareerTool.Select); return; }
                if ((keys.rKey.wasPressedThisFrame || keys.zKey.wasPressedThisFrame) && SupportsPlacementRotation)
                    RotatePlacement(keys.shiftKey.isPressed ? -45f : 45f);
                if (keys.enterKey.wasPressedThisFrame && Tool == CareerTool.Taxiway) FinishTaxiway();
            }
            if (Tool == CareerTool.Select || Tool == CareerTool.Road || mouse == null)
            { HideModelPreviews(); if (line != null) line.enabled = false; return; }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (dragging && Tool == CareerTool.Runway && !game.view.PointerOverHud) PlaceRunway(anchor, EndPoint());
                dragging = false; FinishStroke();
            }
            if (game.view.PointerOverHud) { HideModelPreviews(); if (line != null) line.enabled = false; return; }
            if (!TryPoint(out var point)) { HideModelPreviews(); if (line != null) line.enabled = false; return; }
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
            var camera = game.view.GetComponent<Camera>();
            var pointer = Mouse.current.position.ReadValue();
            var controls = new Vector4(game.view.yaw, game.view.pitch, game.view.distance, camera.fieldOfView);
            if (levelPointPinned && Tool == CareerTool.Level && (pointer - levelPinnedPointer).sqrMagnitude < .25f &&
                TargetHeight == levelPinnedHeight && Radius == levelPinnedRadius && controls == levelPinnedCamera &&
                new Vector2(game.view.pivot.x, game.view.pivot.z) == levelPinnedPivot &&
                new Vector2Int(Screen.width, Screen.height) == levelPinnedScreen)
            { point = levelPinnedPoint; return true; }
            levelPointPinned = false;
            var ray = camera.ScreenPointToRay(pointer);
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
                    var clear = SingleTree && pickedTree < 0 ? default : CareerConstruction.QuoteClear(state, point, Radius,
                        SingleTree ? null : game.valley.ClearCandidates(point, Radius), SingleTree ? pickedTree : -1);
                    Price = clear.Price; Valid = clear.Count > 0 && (game.Sandbox || state.funds >= Price);
                    if (SingleTree && pickedTree >= 0) point = state.world.trees[pickedTree].position;
                    Circle(outline, point, SingleTree ? 5f : Radius);
                    Status = clear.Count == 0 ? SingleTree ? "Choose a tree." : "This area is already clear." :
                        !Valid ? "Not enough funds for this work." : $"Clear land · ${Price:N0}";
                    break;
                case CareerTool.Level:
                    Valid = CareerConstruction.Level(state, point, Radius, TargetHeight, false, out int grading, out why, game.Airport); Price = grading;
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
                    visitorPlan=null; Price=CareerConstruction.ReceptionPrice;
                    Valid = CareerConstruction.Reception(state, point, BuildingHeading, false, out why,game.Airport,p=>{visitorPlan=p;Price=p.Price;},game);
                    outline.AddRange(CareerConstruction.ReceptionCorners(point, BuildingHeading)); outline.Add(outline[0]);
                    Status = why ?? $"Visitor centre + 18 parking bays + connected driveway · ${Price:N0}. R rotates."; break;
            }
            Show(outline);
            RefreshModelPreviews(point);
        }
        int PickTree()
        {
            var ray = game.view.GetComponent<Camera>().ScreenPointToRay(Mouse.current.position.ReadValue());
            return ForestQuery.Pick(game.State.world, ray, game.valley.DisplayHeight,
                p => CareerConstruction.Editable(game.State, p));
        }
        void Stamp(Vector2 point)
        {
            if (Tool == CareerTool.ClearTrees && SingleTree && pickedTree < 0) return;
            bool ok; string why;
            if (Tool == CareerTool.ClearTrees) ok = CareerConstruction.Clear(game.State, point, Radius, out why,
                SingleTree ? pickedTree : -1, SingleTree ? null : game.valley.ClearCandidates(point, Radius));
            else ok = CareerConstruction.Level(game.State, point, Radius, TargetHeight, true, out _, out why, game.Airport);
            if (ok)
            {
                if (Tool == CareerTool.Level && Mouse.current != null)
                {
                    // Raising/cutting terrain changes a perspective ray's ground intersection even
                    // when the mouse stays still. Keep the completed map spot anchored until the
                    // player moves the pointer, camera or brush settings; a second click is a no-op.
                    var camera = game.view.GetComponent<Camera>();
                    levelPointPinned = true; levelPinnedPoint = point;
                    levelPinnedPointer = Mouse.current.position.ReadValue();
                    levelPinnedHeight = TargetHeight; levelPinnedRadius = Radius;
                    // The camera automatically follows edited terrain vertically. Only deliberate
                    // pan/orbit/zoom or a changed viewport should release this stationary map anchor.
                    levelPinnedCamera = new Vector4(game.view.yaw, game.view.pitch, game.view.distance, camera.fieldOfView);
                    levelPinnedPivot = new Vector2(game.view.pivot.x, game.view.pivot.z);
                    levelPinnedScreen = new Vector2Int(Screen.width, Screen.height);
                }
                var centre = Tool == CareerTool.ClearTrees && SingleTree ? game.State.world.trees[pickedTree].position : point;
                float radius = Tool == CareerTool.ClearTrees && SingleTree ? 0f : Radius;
                var area = new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f);
                strokeBounds = dirtyStroke ? Rect.MinMaxRect(Mathf.Min(strokeBounds.xMin, area.xMin), Mathf.Min(strokeBounds.yMin, area.yMin),
                    Mathf.Max(strokeBounds.xMax, area.xMax), Mathf.Max(strokeBounds.yMax, area.yMax)) : area;
                game.PlayerChanged(Tool, area); dirtyStroke = true;
            }
            else Status = why;
            lastStamp = point; nextStamp = Time.unscaledTime + .16f;
            // Quotes describe the remaining work, including the zero-cost completed patch.
            if (ok) Preview(point);
        }
        void FinishStroke()
        {
            if (dirtyStroke && game != null) game.FinishTerrainStroke(Tool, strokeBounds);
            dirtyStroke = false;
        }
        void PlaceRunway(Vector2 a, Vector2 b)
        {
            if (CareerConstruction.Runway(game.State, a, b, true, out _, out string why)) { game.PlayerChanged(CareerTool.Runway); game.QueueAutosave(); }
            else game.SetMessage(why);
        }
        public void FinishTaxiway()
        {
            if (CareerConstruction.Taxiway(game.State, draft, true, out _, out string why))
            { game.PlayerChanged(CareerTool.Taxiway); draft.Clear(); game.QueueAutosave(); }
            else game.SetMessage(why);
        }
        void PlaceStand(Vector2 point)
        {
            if (CareerConstruction.Parking(game.State, point, true, out string why)) { game.PlayerChanged(CareerTool.Stand); game.QueueAutosave(); }
            else game.SetMessage(why);
        }
        void PlaceReception(Vector2 point)
        {
            if (CareerConstruction.Reception(game.State, point, BuildingHeading, true, out string why,game.Airport,game:game)) { game.PlayerChanged(CareerTool.Reception); game.QueueAutosave(); Choose(CareerTool.Select); }
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
