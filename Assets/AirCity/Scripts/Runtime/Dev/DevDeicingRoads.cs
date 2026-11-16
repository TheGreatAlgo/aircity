using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevAirfieldTool
    {
        string deicingRoadSnapNote;
        public string DeicingRoadSnapNote => deicingRoadSnapNote;

        /// <summary>Service roads terminate on the painted mouths, with ordinary nodes and edges underneath.</summary>
        public bool TrySnapDeicingRoadPort(Vector2 cursor, float reach, out Vector2 position, out string label)
        {
            position = cursor; label = null;
            if (airport == null) return false;
            float best = reach * reach;
            foreach (var area in airport.Deicing.WorkAreas)
            {
                var ports = area.RoadPorts;
                for (int i = 0; i < ports.Length; i++)
                {
                    float distance = (ports[i] - cursor).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance; position = ports[i]; label = area.Name + " · " + DeicingWorkArea.RoadPortName(i);
                }
            }
            return label != null;
        }

        /// <summary>Snap a station mouth onto an existing service road while keeping its equipment island clear.</summary>
        public bool TrySnapDeicingStationToRoad(Vector2 position, float heading, float reach, bool align,
            out Vector2 snapped, out float angle, out string label, DeicingWorkArea except = null)
        {
            snapped = position; angle = heading; label = null;
            if (airport == null) return false;
            float best = reach * reach;
            foreach (var edge in airport.Taxiways.edges)
            {
                if (edge.road != RoadClass.Service || !edge.surface.IsPaved()) continue;
                var a = airport.Taxiways.At(edge.a); var b = airport.Taxiways.At(edge.b);
                if ((b - a).sqrMagnitude < .01f) continue;
                float roadHeading = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
                float h = align ? roadHeading + Mathf.Round(Mathf.DeltaAngle(roadHeading, heading) / 90f) * 90f : heading;
                var candidate = new DeicingWorkArea { position = position, heading = h };
                var ports = candidate.RoadPorts;
                var right = Geo.Right((b - a).normalized) * edge.width * .5f;
                var road = new[] { a + right, b + right, b - right, a - right };
                for (int i = 0; i < ports.Length; i++)
                {
                    var nearest = Geo.ClosestOnSegment(ports[i], a, b, out _);
                    var shift = nearest - ports[i]; float distance = shift.sqrMagnitude;
                    if (distance >= best) continue;
                    candidate.position = position + shift;
                    if (Geo.Overlap(candidate.EquipmentOutline, road)) continue;
                    if (airport.Deicing.WorkAreaProblem(candidate.position, h, except) != null) continue;
                    best = distance; snapped = candidate.position; angle = Mathf.Repeat(h, 360);
                    label = DeicingWorkArea.RoadPortName(i) + " snapped to service road";
                }
            }
            return label != null;
        }

        string DeicingRoadProblem(Vector2 from, Vector2 to, float width)
        {
            if ((to - from).sqrMagnitude < .01f) return null;
            var right = Geo.Right((to - from).normalized) * width * .5f;
            var road = new[] { from + right, to + right, to - right, from - right };
            foreach (var area in airport.Deicing.WorkAreas)
                if (Geo.Overlap(road, area.EquipmentOutline)) return "keep the road clear of the tank; connect to FILL or UNLOAD";
            return null;
        }
    }
}
