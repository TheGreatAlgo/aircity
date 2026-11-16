using System.Collections.Generic;
using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Game
{
    /// <summary>Draw a public access route from the entrance, preview the price and snap its end to the region road.</summary>
    [RequireComponent(typeof(CareerGame))]
    [DefaultExecutionOrder(110)]
    public sealed class CareerRoadTool : MonoBehaviour
    {
        public Material previewMaterial;
        public bool Active { get; private set; }
        public string Status { get; private set; }
        public int Price { get; private set; }
        readonly List<Vector2> points = new List<Vector2>();
        CareerGame game;
        LineRenderer line;
        Vector2 rightDown;

        public void Begin()
        {
            game = GetComponent<CareerGame>();
            if (game.State.Step != CareerStep.AccessRoad) return;
            Active = true; points.Clear();
            if (!game.State.handsOn) points.Add(game.State.Site.Entrance);
            Status = "Click road corners, then finish on the existing public road. Esc cancels; right-click undoes.";
            var entrance = game.State.Site.Entrance;
            game.view.FrameRoad(entrance, new Vector2(game.State.world.RoadAt(entrance.y), entrance.y), game.State.Site.elevation);
            if (line == null)
            {
                var go = new GameObject("Access road preview"); go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>(); line.sharedMaterial = previewMaterial; line.useWorldSpace = true;
                line.widthMultiplier = 1f; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            line.enabled = true;
        }

        public void Cancel() { Active = false; if (line != null) line.enabled = false; }

        void Update()
        {
            if (!Active) return;
            if (game.State.HasAccess || game.State.Site == null) { Cancel(); return; }
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Cancel(); return; }
            if (mouse == null || game.view.PointerOverHud) return;
            if (mouse.rightButton.wasPressedThisFrame) rightDown = mouse.position.ReadValue();
            if (mouse.rightButton.wasReleasedThisFrame && Vector2.Distance(rightDown, mouse.position.ReadValue()) < 5f && points.Count > 1)
                points.RemoveAt(points.Count - 1);
            var ray = game.view.GetComponent<Camera>().ScreenPointToRay(mouse.position.ReadValue());
            if (!game.valley.Terrain.GetComponent<TerrainCollider>().Raycast(ray, out var hit, 20000f)) return;
            var p = new Vector2(hit.point.x, hit.point.z);
            if (points.Count == 0)
            {
                Status = "Click the airport end of your road, beside the future reception building.";
                Price = 0; line.enabled = false;
                if (mouse.leftButton.wasPressedThisFrame && game.State.BuildableBounds.Contains(p)) { points.Add(p); line.enabled = true; }
                return;
            }
            bool snapped = PublicRoadLayout.TrySnap(game.State.world, p, 22f, out var snap);
            if (snapped) p = snap;
            var draft = new List<Vector2>(points) { p };
            Price = PublicRoadLayout.Price(draft);
            bool valid = false; string reason = null;
            if (snapped) valid = game.State.handsOn ? CareerConstruction.Road(game.State, draft, out reason) : PublicRoadLayout.Validate(game.State.world, game.State.Site, draft, out reason);
            Status = snapped ? valid ? "Click to build the road and its signal-controlled junction." : reason : "Click to add a corner; finish on the public road.";
            var display = new List<Vector3>();
            for (int segment = 1; segment < draft.Count; segment++)
            {
                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(draft[segment - 1], draft[segment]) / 10f));
                for (int i = 0; i < samples; i++)
                {
                    var q = Vector2.Lerp(draft[segment - 1], draft[segment], i / (float)samples);
                    display.Add(new Vector3(q.x, game.valley.DisplayHeight(q) + .9f, q.y));
                }
            }
            display.Add(new Vector3(p.x, game.valley.DisplayHeight(p) + .9f, p.y));
            line.positionCount = display.Count; line.SetPositions(display.ToArray());
            line.startWidth = line.endWidth = 1.4f;
            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (snapped)
            {
                bool allowed = game.State.handsOn ? CareerConstruction.Road(game.State, draft, out string problem) : PublicRoadLayout.Validate(game.State.world, game.State.Site, draft, out problem);
                if (allowed)
                { if (game.Construct(draft)) Cancel(); else Status = game.Message; }
                else Status = problem;
            }
            else if (Vector2.Distance(points[points.Count - 1], p) >= 12f && points.Count < 31) points.Add(p);
        }
    }
}
