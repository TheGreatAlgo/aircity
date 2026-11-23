using System.Collections.Generic;
using System.Text;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    public partial class DevAirfieldTool
    {
        readonly BuildPlacementPreview modelPreview = new();
        float objectHeading;
        bool placementSnap = true, placementHeadingSet;
        bool placementTurning;
        Vector2 placementAnchor;
        string depotPreviewProblem;
        public string DepotPreviewProblem => depotPreviewProblem;
        public bool PlacementPreviewVisible => modelPreview.Visible;
        public GameObject PlacementPreviewObject => modelPreview.RootObject;
        public int PlacementPreviewCount => modelPreview.ModelCount;
        public bool PlacementSnap
        {
            get => placementSnap;
            set { placementSnap = value; if (value && SupportsPlacementRotation) SetPlacementHeading(Mathf.Round(PlacementHeading / 15f) * 15f); }
        }
        public bool SupportsPlacementRotation => tool == DevTool.DeicingStorage || tool == DevTool.DeicingParking ||
            tool == DevTool.Depot || tool == DevTool.Hangar || tool == DevTool.FlyingSchool || tool == DevTool.Stand || tool == DevTool.DeIcing || tool == DevTool.Apron ||
            tool == DevTool.Terminal || tool == DevTool.Runway ||
            tool == DevTool.Baggage && GetComponent<DevBaggageControls>()?.Placement == BaggagePlacement.Depot;
        public float PlacementHeading => tool switch
        {
            DevTool.DeicingStorage => deicingAreaHeading,
            DevTool.DeicingParking => deicingParkingHeading,
            DevTool.Stand => ghostAttached ? standAngle : freeHeading,
            DevTool.DeIcing => ghost != null ? ghost.stand.heading : freeHeading,
            DevTool.Baggage => GetComponent<DevBaggageControls>()?.PreviewBearing ?? 0,
            _ => objectHeading,
        };

        public void RotatePlacement(float degrees)
        {
            if (!SupportsPlacementRotation || float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            if (tool == DevTool.Baggage) { GetComponent<DevBaggageControls>()?.FlipSite(); return; }
            float heading = PlacementHeading + degrees;
            SetPlacementHeading(placementSnap ? Mathf.Round(heading / 15f) * 15f : heading);
        }

        void SetPlacementHeading(float heading)
        {
            heading = Mathf.Repeat(heading, 360f); placementHeadingSet = true;
            if ((tool == DevTool.Stand || tool == DevTool.DeIcing) && SelectedStand != null) selection = Pick.None;
            switch (tool)
            {
                case DevTool.DeicingStorage: deicingAreaHeading = heading; break;
                case DevTool.DeicingParking: deicingParkingHeading = heading; break;
                case DevTool.Stand:
                    if (ghostAttached) SetStandAngle(Mathf.Clamp(heading, MinStandAngle, MaxStandAngle));
                    else freeHeading = heading;
                    break;
                case DevTool.DeIcing: freeHeading = heading; break;
                case DevTool.Apron:
                case DevTool.Terminal:
                    float delta = Mathf.DeltaAngle(objectHeading, heading);
                    if (!apronDrag && apronCorners.Count > 1)
                    {
                        var pivot = apronCorners[0]; var q = Quaternion.Euler(0, delta, 0);
                        for (int i = 1; i < apronCorners.Count; i++) apronCorners[i] = pivot + Geo.XZ(q * Geo.X0Z(apronCorners[i] - pivot));
                    }
                    objectHeading = heading; break;
                default: objectHeading = heading; break;
            }
        }

        void PlacementScroll(Keyboard keys, Mouse mouse)
        {
            if (!SupportsPlacementRotation) { StandScroll(keys, mouse); return; }
            TakesScroll = keys.shiftKey.isPressed && !DevGui.PointerOverHud;
            if (!TakesScroll) { scrollTurn = 0; return; }
            var scroll = mouse.scroll.ReadValue();
            scrollTurn += Mathf.Clamp(Mathf.Abs(scroll.y) >= Mathf.Abs(scroll.x) ? scroll.y : scroll.x, -4, 4);
            int notches = (int)scrollTurn;
            if (notches == 0) return;
            scrollTurn -= notches;
            if (keys.altKey.isPressed) SetPlacementHeading(PlacementHeading + notches);
            else RotatePlacement(notches * (placementSnap ? 15f : 5f));
        }

        float DraftHeading(Vector2 direction)
        {
            float heading = direction.sqrMagnitude > 4f ? Bearing(direction) : PlacementHeading;
            return placementSnap ? Mathf.Round(heading / 15f) * 15f : heading;
        }

        void PlacementDrag(Mouse mouse, Keyboard keys, bool free, ref bool click, ref Vector3 point)
        {
            if (tool != DevTool.DeicingStorage && tool != DevTool.DeicingParking) { placementTurning = false; return; }
            bool modifier = keys.ctrlKey.isPressed || keys.leftCommandKey.isPressed || keys.rightCommandKey.isPressed;
            if (!placementTurning && click && modifier)
            { placementAnchor = Geo.XZ(airport.transform.InverseTransformPoint(point)); placementTurning = true; click = false; }
            if (!placementTurning) return;
            if (!free) { if (mouse.leftButton.wasReleasedThisFrame) placementTurning = false; click = false; return; }
            var cursor = Geo.XZ(airport.transform.InverseTransformPoint(point));
            if (Vector2.Distance(cursor, placementAnchor) > 2f) SetPlacementHeading(DraftHeading(cursor - placementAnchor));
            point = airport.transform.TransformPoint(Geo.X0Z(placementAnchor));
            click = mouse.leftButton.wasReleasedThisFrame;
            if (click) placementTurning = false;
        }

        void RefreshBuildPreview(bool pointing, Vector3 point)
        {
            if (!pointing || airport == null) { modelPreview.Hide(); return; }
            var p = Geo.XZ(airport.transform.InverseTransformPoint(point));
            bool valid = true, shown = true;
            modelPreview.Begin(airport);
            switch (tool)
            {
                case DevTool.DeicingStorage:
                    if (deicingAreaPreview == null) { shown = false; break; }
                    var assets = Resources.Load<DeicingAssets>("DeicingAssets");
                    var area = deicingAreaPreview;
                    if (modelPreview.GeometryChanged("deicing storage " + PolygonPreviewKey(area.Outline, 0)))
                    {
                        var supplyPaint = new PaintBuilder();
                        DeicingSupplyMarkings.Build(supplyPaint, area, perimeter: false);
                        modelPreview.Paint(airport, supplyPaint);
                    }
                    var axis = Geo.X0Z(area.Axis);
                    modelPreview.Model(assets?.tankPrefab, Geo.X0Z(area.TankPosition) + axis * 4.2f, Quaternion.LookRotation(-axis));
                    if (deicingAreaMoving != null && deicingAreaMoving.recyclingInstalled)
                        modelPreview.Model(assets?.recyclerPrefab, Geo.X0Z(area.RecyclerPosition), Quaternion.LookRotation(Geo.X0Z(area.RecyclerFacing)));
                    valid = deicingAreaNote == null;
                    break;
                case DevTool.DeicingParking:
                    if (deicingParkingPreview == null) { shown = false; break; }
                    assets = Resources.Load<DeicingAssets>("DeicingAssets");
                    var bay = deicingParkingPreview;
                    modelPreview.GeometryChanged("deicing parking");
                    float offset = assets != null && assets.truckType != null ? assets.truckType.bounds.center.z : 3.9f;
                    modelPreview.Model(assets?.truckPrefab, Geo.X0Z(bay.position - bay.Axis * offset), Quaternion.LookRotation(Geo.X0Z(bay.Axis)));
                    valid = deicingParkingNote == null;
                    break;
                case DevTool.DeicingOutsideAccess:
                    if (deicingAccessNode < 0) { shown = false; break; }
                    var accessPoint = airport.Taxiways.At(deicingAccessNode);
                    if (modelPreview.GeometryChanged("outside " + accessPoint))
                    {
                        var paint = new PaintBuilder();
                        var facing = OutsideAccessFacing(deicingAccessNode); var right = Geo.Right(facing);
                        paint.Line(accessPoint - right * 3, accessPoint + right * 3, .22f, PaintBuilder.Yellow);
                        paint.Text(accessPoint + facing * 3, facing, "ENTRY", 1f, PaintBuilder.White, 1.5f);
                        modelPreview.Paint(airport, paint);
                    }
                    valid = deicingAccessNote == null; break;
                case DevTool.Depot:
                    var template = airport.Depot;
                    var prefab = template != null ? template.buildingPrefab : depotPrefab;
                    modelPreview.GeometryChanged("maintenance depot");
                    var position = placingDepot ? Geo.XZ(dragStart) : p;
                    float heading = placingDepot ? DraftHeading(p - position) : objectHeading;
                    objectHeading = Mathf.Repeat(heading, 360f);
                    modelPreview.Model(prefab, Geo.X0Z(position), Quaternion.Euler(0, heading, 0));
                    depotPreviewProblem = DepotProblem(position, heading, out _);
                    valid = depotPreviewProblem == null;
                    break;
                case DevTool.Stand:
                case DevTool.DeIcing:
                    if (ghost == null) { shown = false; break; }
                    if (modelPreview.GeometryChanged(StandPreviewKey(ghost))) modelPreview.Stand(airport, ghost, types);
                    valid = ghostProblem == null; break;
                case DevTool.Hangar:
                    if (modelPreview.GeometryChanged("hangar " + SelectedHangarSize)) modelPreview.Hangar(airport, SelectedHangarSize);
                    var hangarPose = HangarPlacement(point);
                    modelPreview.Pose(hangarPose.door, hangarPose.heading);
                    valid = hangarProblem == null;
                    break;
                case DevTool.FlyingSchool:
                    var school = FlyingSchoolPlacement(point);
                    if (modelPreview.GeometryChanged($"school {school.lane:0.00} {schoolBuiltSide} {school.surface}"))
                        modelPreview.FlyingSchool(airport, school.lane, schoolBuiltSide, school.surface, types);
                    modelPreview.Pose(school.origin, school.heading);
                    valid = schoolProblem == null;
                    break;
                case DevTool.Apron:
                case DevTool.Terminal:
                    var corners = apronDrag && apronRect != null ? new List<Vector2>(apronRect) : new List<Vector2>(apronCorners);
                    if (!apronDrag && (corners.Count == 0 || corners[0] != apronCursor)) corners.Add(apronCursor);
                    if (corners.Count < 3) { shown = false; break; }
                    string key = PolygonPreviewKey(corners, tool == DevTool.Terminal ? terminalFloors : (int)surface);
                    if (modelPreview.GeometryChanged(tool + key))
                    {
                        if (tool == DevTool.Terminal) modelPreview.Terminal(airport, Terminal.Polygon(corners, terminalFloors));
                        else modelPreview.Pavement(airport, corners, surface);
                    }
                    valid = apronProblem == null; break;
                case DevTool.Baggage:
                    var baggage = GetComponent<DevBaggageControls>();
                    if (baggage == null || baggage.Placement != BaggagePlacement.Depot || !baggage.HasPreview) { shown = false; break; }
                    if (modelPreview.GeometryChanged("baggage " + baggage.depotSize)) modelPreview.Baggage(airport, baggage.depotSize);
                    modelPreview.Pose(baggage.PreviewOrigin, baggage.PreviewBearing);
                    valid = baggage.PreviewProblem == null; break;
                default: shown = false; break;
            }
            if (shown) modelPreview.End(valid); else modelPreview.Hide();
        }

        static string PolygonPreviewKey(IList<Vector2> corners, int setting)
        {
            var key = new StringBuilder().Append(setting);
            foreach (var corner in corners) key.Append('|').Append(Mathf.RoundToInt(corner.x * 20)).Append(',').Append(Mathf.RoundToInt(corner.y * 20));
            return key.ToString();
        }
        static string StandPreviewKey(StandLayout layout) => "stand " + layout.stand.kind + " " + layout.stand.code + " " +
            layout.stand.surface + " " + layout.stand.exit + " " + PolygonPreviewKey(layout.Pad, Mathf.RoundToInt(layout.stand.heading * 100));

        string DepotProblem(Vector2 position, float heading, out Vector2[] corners)
        {
            var type = airport.Depot != null ? airport.Depot.buildingType : depotType;
            var bounds = type != null ? type.bounds : new Bounds(new Vector3(0, 0, -12), new Vector3(46, 12, 24));
            var axis = FromBearing(heading); var right = Geo.Right(axis);
            Vector2 At(float x, float z) => position + right * x + axis * z;
            corners = new[] { At(bounds.min.x, bounds.min.z), At(bounds.max.x, bounds.min.z), At(bounds.max.x, bounds.max.z), At(bounds.min.x, bounds.max.z) };
            foreach (var depot in airport.Depots) if (depot != null && Geo.Overlap(corners, depot.Corners)) return "overlaps a maintenance depot";
            foreach (var hangar in airport.Hangars) if (Geo.Overlap(corners, hangar.Pad)) return "overlaps an aircraft hangar";
            foreach (var school in airport.FlyingSchools) foreach (var site in school.Footprints(false)) if (Geo.Overlap(corners, site)) return "overlaps the flying school";
            foreach (var terminal in airport.terminals) if (Geo.Overlap(corners, terminal.Outline())) return "overlaps a terminal";
            foreach (var area in airport.Deicing.WorkAreas) if (area != null && Geo.Overlap(corners, area.Outline)) return "overlaps a deicing supply area";
            foreach (var bay in airport.Deicing.ParkingBays) if (bay != null && Geo.Overlap(corners, bay.Outline)) return "overlaps a truck parking bay";
            foreach (var stand in airport.Taxiways.Layouts()) if (Geo.Overlap(corners, stand.Pad)) return "overlaps an aircraft pad";
            foreach (var runway in airport.Runways)
                if (Geo.Overlap(corners, Strip(Geo.XZ(runway.start), Geo.XZ(runway.end), runway.HalfWidthTotal))) return "overlaps a runway";
            foreach (var edge in airport.Taxiways.edges)
                if (Geo.Overlap(corners, Strip(airport.Taxiways.At(edge.a), airport.Taxiways.At(edge.b), edge.width * .5f))) return "overlaps a road or taxiway";
            return null;
        }

        void DrawDepotPreview(bool pointing, Vector3 point)
        {
            if (!pointing || tool != DevTool.Depot) return;
            var p = Geo.XZ(airport.transform.InverseTransformPoint(point));
            var position = placingDepot ? Geo.XZ(dragStart) : p;
            float heading = placingDepot ? DraftHeading(p - position) : objectHeading;
            depotPreviewProblem = DepotProblem(position, heading, out var corners);
            loop.Clear(); foreach (var corner in corners) loop.Add(airport.transform.TransformPoint(Geo.X0Z(corner)));
            outlines.Loop(cam, loop, depotPreviewProblem == null ? new Color(.25f, .85f, .72f) : DevGui.Danger, 3);
        }
    }
}
