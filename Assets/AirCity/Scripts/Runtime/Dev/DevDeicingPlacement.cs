using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    public partial class DevAirfieldTool
    {
        DeicingWorkArea deicingAreaMoving, deicingAreaPreview;
        DeicingFacility deicingAreaAssignment;
        float deicingAreaHeading;
        string deicingAreaNote, deicingPadNote;
        public string DeicingPadNote => deicingPadNote;

        public DeicingWorkArea SelectedDeicingWorkArea => selection.kind == Kind.DeicingWorkArea ? selection.deicingWorkArea : null;
        public string DeicingAreaNote => deicingAreaNote;

        public void BeginDeicingWorkArea(DeicingFacility assignTo = null)
        {
            SetTool(DevTool.DeicingStorage);
            deicingAreaMoving = null;
            deicingAreaAssignment = assignTo;
            deicingAreaPreview = null;
            deicingAreaNote = null;
            deicingRoadSnapNote = null;
        }

        public void BeginMoveDeicingWorkArea(DeicingWorkArea area)
        {
            if (area == null) return;
            SetTool(DevTool.DeicingStorage);
            deicingAreaMoving = area;
            deicingAreaAssignment = null;
            deicingAreaHeading = area.heading;
            deicingAreaNote = null;
            deicingRoadSnapNote = null;
        }

        public void SelectDeicingWorkArea(DeicingWorkArea area)
        {
            if (airport == null || area == null || !airport.deicingWorkAreas.Contains(area)) return;
            SetTool(DevTool.Select);
            Select(new Pick { kind = Kind.DeicingWorkArea, deicingWorkArea = area, apron = -1 });
        }

        void DeicingWorkAreaTool(Keyboard keys, bool click, bool free, Vector3 worldPoint)
        {
            if (!free) { deicingAreaPreview = null; return; }
            var point = Geo.XZ(airport.transform.InverseTransformPoint(worldPoint));
            if (keys.shiftKey.isPressed) point = new Vector2(Mathf.Round(point.x / 5f) * 5f, Mathf.Round(point.y / 5f) * 5f);
            deicingRoadSnapNote = null;
            bool unsnapped = keys.ctrlKey.isPressed || keys.leftCommandKey.isPressed || keys.rightCommandKey.isPressed;
            if (!unsnapped && TrySnapDeicingStationToRoad(point, deicingAreaHeading, Mathf.Max(2f, SnapReach(worldPoint)),
                !placementHeadingSet && deicingAreaMoving == null, out var snapped, out var heading, out var label, deicingAreaMoving))
            { point = snapped; deicingAreaHeading = heading; deicingRoadSnapNote = label; }
            deicingAreaPreview = new DeicingWorkArea { position = point, heading = deicingAreaHeading };
            deicingAreaNote = airport.Deicing.WorkAreaProblem(point, deicingAreaHeading, deicingAreaMoving);
            if (deicingAreaMoving != null && airport.Deicing.InUse(deicingAreaMoving))
                deicingAreaNote = "Wait for refilling trucks to clear before moving the area.";
            if (!click) return;
            if (deicingAreaMoving == null)
                foreach (var area in airport.Deicing.WorkAreas)
                    if (area != null && area.Contains(point)) { SelectDeicingWorkArea(area); return; }
            if (deicingAreaNote != null) return;
            DeicingWorkArea placed = deicingAreaMoving;
            if (placed != null)
            {
                if (!airport.Deicing.MoveWorkArea(placed, point, deicingAreaHeading, out deicingAreaNote)) return;
            }
            else placed = airport.Deicing.AddWorkArea(point, deicingAreaHeading);
            if (placed == null) { deicingAreaNote = airport.Deicing.LastPurchaseProblem; return; }
            if (airport.GetComponent<DeicingRig>() == null) airport.gameObject.AddComponent<DeicingRig>();
            if (deicingAreaAssignment != null) airport.Deicing.AssignWorkArea(deicingAreaAssignment, placed);
            deicingAreaAssignment = null;
            SelectDeicingWorkArea(placed);
        }

        DeicingParkingBay deicingParkingMoving, deicingParkingPreview;
        DeicingFacility deicingParkingAssignment;
        float deicingParkingHeading;
        string deicingParkingNote, deicingAccessNote;
        int deicingAccessNode = -1;
        public DeicingParkingBay SelectedDeicingParkingBay => selection.kind == Kind.DeicingParking ? selection.deicingParking : null;
        public DeicingOutsideAccess SelectedDeicingOutsideAccess => selection.kind == Kind.DeicingOutsideAccess ? selection.deicingAccess : null;
        public string DeicingParkingNote => deicingParkingNote;
        public string DeicingAccessNote => deicingAccessNote;

        public void BeginDeicingParking(DeicingFacility assignTo = null)
        {
            SetTool(DevTool.DeicingParking); deicingParkingMoving = null; deicingParkingAssignment = assignTo;
            deicingParkingPreview = null; deicingParkingNote = null;
        }
        public void BeginMoveDeicingParking(DeicingParkingBay bay)
        {
            if (bay == null) return;
            SetTool(DevTool.DeicingParking); deicingParkingMoving = bay; deicingParkingAssignment = null;
            deicingParkingHeading = bay.heading; deicingParkingNote = null;
        }
        public void SelectDeicingParkingBay(DeicingParkingBay bay)
        {
            if (airport == null || bay == null || !airport.Deicing.ParkingBays.Contains(bay)) return;
            SetTool(DevTool.Select); Select(new Pick { kind = Kind.DeicingParking, deicingParking = bay, apron = -1 });
        }
        public void BeginDeicingOutsideAccess()
        {
            SetTool(DevTool.DeicingOutsideAccess); deicingAccessNode = -1; deicingAccessNote = null;
        }
        public void SelectDeicingOutsideAccess(DeicingOutsideAccess access)
        {
            if (airport == null || access == null) return;
            SetTool(DevTool.Select); Select(new Pick { kind = Kind.DeicingOutsideAccess, deicingAccess = access, apron = -1 });
        }

        void DeicingParkingTool(Keyboard keys, bool click, bool free, Vector3 worldPoint)
        {
            if (!free) { deicingParkingPreview = null; return; }
            var point = Geo.XZ(airport.transform.InverseTransformPoint(worldPoint));
            if (keys.shiftKey.isPressed) point = new Vector2(Mathf.Round(point.x), Mathf.Round(point.y));
            deicingParkingPreview = new DeicingParkingBay { position = point, heading = deicingParkingHeading };
            deicingParkingNote = airport.Deicing.ParkingBayProblem(point, deicingParkingHeading, deicingParkingMoving);
            if (!click) return;
            if (deicingParkingMoving == null)
                foreach (var bay in airport.Deicing.ParkingBays)
                    if (bay != null && bay.Contains(point)) { SelectDeicingParkingBay(bay); return; }
            if (deicingParkingNote != null) return;
            var placed = deicingParkingMoving;
            if (placed != null)
            {
                if (!airport.Deicing.MoveParkingBay(placed, point, deicingParkingHeading, out deicingParkingNote)) return;
            }
            else placed = airport.Deicing.AddParkingBay(point, deicingParkingHeading);
            if (placed == null) { deicingParkingNote = airport.Deicing.LastPurchaseProblem; return; }
            if (deicingParkingAssignment != null) airport.Deicing.AssignParkingBay(deicingParkingAssignment, placed);
            if (airport.GetComponent<DeicingRig>() == null) airport.gameObject.AddComponent<DeicingRig>();
            deicingParkingAssignment = null; SelectDeicingParkingBay(placed);
        }

        void DeicingOutsideAccessTool(bool click, bool free, Vector3 worldPoint)
        {
            deicingAccessNode = -1;
            if (!free) return;
            var point = Geo.XZ(airport.transform.InverseTransformPoint(worldPoint));
            float radius = Mathf.Max(8f, SnapReach(worldPoint));
            if (!airport.Deicing.TryOutsideAccess(point, radius, out deicingAccessNode))
            {
                deicingAccessNode = -1;
                float best = radius;
                foreach (var node in airport.Taxiways.nodes)
                {
                    int arms = airport.Taxiways.EdgesAt(node.id).Count(edge => edge.road == RoadClass.Service);
                    float distance = Vector2.Distance(point, airport.Taxiways.At(node.id));
                    if (arms != 1 || distance > best) continue;
                    best = distance; deicingAccessNode = node.id;
                }
                if (deicingAccessNode < 0) { deicingAccessNote = "Point at an open paved service-road endpoint."; return; }
            }
            deicingAccessNote = airport.Deicing.OutsideAccessProblem(deicingAccessNode);
            if (!click) return;
            var existing = airport.Deicing.OutsideAccessForNode(deicingAccessNode);
            if (existing != null) { SelectDeicingOutsideAccess(existing); return; }
            if (deicingAccessNote != null) return;
            var made = airport.Deicing.AddOutsideAccess(deicingAccessNode, out deicingAccessNote);
            if (made != null) SelectDeicingOutsideAccess(made);
        }

        Vector2 OutsideAccessFacing(int nodeId)
        {
            var net = airport.Taxiways;
            foreach (var edge in net.edges)
                if (edge.a == nodeId || edge.b == nodeId)
                    return (net.At(TaxiwayNetwork.Other(edge, nodeId)) - net.At(nodeId)).normalized;
            return Vector2.up;
        }

        void DrawDeicingObjectPreview(bool pointing)
        {
            if (!pointing) return;
            Vector2[] corners = null; string problem = null;
            if (tool == DevTool.DeicingParking && deicingParkingPreview != null)
            { corners = deicingParkingPreview.Outline; problem = deicingParkingNote; }
            else if (tool == DevTool.DeicingOutsideAccess && deicingAccessNode >= 0)
            {
                var p = airport.Taxiways.At(deicingAccessNode); var axis = OutsideAccessFacing(deicingAccessNode); var right = Geo.Right(axis);
                corners = new[] { p - right * 4 - axis * 2, p + right * 4 - axis * 2, p + right * 4 + axis * 2, p - right * 4 + axis * 2 };
                problem = deicingAccessNote;
            }
            if (corners == null) return;
            loop.Clear(); foreach (var corner in corners) loop.Add(airport.transform.TransformPoint(Geo.X0Z(corner)));
            outlines.Loop(cam, loop, problem == null ? new Color(.25f, .85f, .72f) : DevGui.Danger, 3);
        }

        void DrawDeicingAreaPreview(bool pointing)
        {
            if (tool != DevTool.DeicingStorage || !pointing || deicingAreaPreview == null) return;
            loop.Clear();
            foreach (var corner in deicingAreaPreview.Outline)
                loop.Add(airport.transform.TransformPoint(Geo.X0Z(corner)));
            var color = deicingAreaNote == null ? new Color(.55f, 1f, .55f) : DevGui.Danger;
            outlines.Loop(cam, loop, color, 3f);
        }
    }
}
