using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>Preview and commit use the same footprint checks. Clearing, grading and construction are separate transactions.</summary>
    public static class CareerConstruction
    {
        public const int TreePrice = 24, StandPrice = 1800, ReceptionPrice = 4500;
        public static bool Editable(CareerState state, Vector2 p) => state.Site != null && (state.Site.Parcel.Contains(p) ||
            new Rect(state.Site.centre.x, state.Site.centre.y - 650f, ValleyWorld.RoadX + 35f - state.Site.centre.x, 1300f).Contains(p));
        static bool Ready(CareerState state, CareerTool tool, out string why)
        {
            why = state.Site == null ? "Buy a site first." : !state.handsOn ? "This save uses the original airport layout." :
                state.charterActive ? "Wait for the aircraft before changing the airfield." : !state.layout.Unlocked(tool) ? "This tool has not unlocked yet." : null;
            return why == null;
        }
        static bool Pay(CareerState state, int price, out string why)
        { why = state.funds < price ? "Not enough funds for this work." : null; if (why != null) return false; state.funds -= price; return true; }

        public static List<int> Trees(CareerState state, Vector2 point, float radius)
        {
            var found = new List<int>();
            for (int i = 0; i < state.world.trees.Count; i++)
            {
                var t = state.world.trees[i];
                if (!t.removed && Editable(state, t.position) && Vector2.Distance(t.position, point) <= radius) found.Add(i);
            }
            return found;
        }
        public static bool Clear(CareerState state, Vector2 point, float radius, out string why, int treeIndex = -1)
        {
            if (!Ready(state, CareerTool.ClearTrees, out why)) return false;
            var indices = treeIndex >= 0 && treeIndex < state.world.trees.Count ? new List<int> { treeIndex } : Trees(state, point, Mathf.Clamp(radius, 4f, 90f));
            indices.RemoveAll(i => state.world.trees[i].removed || !Editable(state, state.world.trees[i].position));
            if (indices.Count == 0) { why = "Choose a tree on your land or in the airport road corridor."; return false; }
            if (!Pay(state, indices.Count * TreePrice, out why)) return false;
            foreach (int i in indices)
            {
                var tree = state.world.trees[i]; tree.removed = true; state.world.MarkGround(tree.position, 9f, .3f);
            }
            state.layout.clearedTrees += indices.Count; state.layout.Unlock(CareerTool.Level); return true;
        }

        public static bool Level(CareerState state, Vector2 p, float radius, float target, bool commit, out int price, out string why)
        {
            price = 0;
            if (!Ready(state, CareerTool.Level, out why)) return false;
            radius = Mathf.Clamp(radius, 15f, 90f);
            if (!ValleyWorld.Finite(target) || target < 10f || target > 400f || !Editable(state, p) || !state.Site.Parcel.Contains(p))
            { why = "Level ground within your airport land, between 10 and 400 m."; return false; }
            if (state.world.trees.Any(t => !t.removed && Vector2.Distance(t.position, p) < radius + 6f))
            { why = "Remove the trees inside the brush before levelling."; return false; }
            // Changed grid samples also affect interpolated terrain up to one sample outside the brush.
            if (TouchesConstruction(state, p, radius + state.world.Spacing))
            { why = "This brush could affect an existing structure or pavement. Demolish it before levelling here."; return false; }
            float volume = state.world.BrushEarthwork(p, radius, target, state.Site.Parcel);
            if (volume < 1f) { why = "This patch is already at the selected height."; return false; }
            price = Mathf.Max(15, Mathf.CeilToInt(volume * .055f));
            if (state.funds < price) { why = "Not enough funds for this earthwork."; return false; }
            if (commit)
            {
                state.funds -= price; state.world.LevelBrush(p, radius, target, state.Site.Parcel);
                state.layout.earthwork += volume; state.layout.Unlock(CareerTool.Runway);
            }
            why = null; return true;
        }
        static bool TouchesConstruction(CareerState state, Vector2 p, float radius)
        {
            var l = state.layout;
            if (l.runwayBuilt && Distance(p, l.runwayA, l.runwayB) < radius + 15f) return true;
            if (l.TaxiSegments().Any(e => Distance(p, e.a, e.b) < radius + 6f)) return true;
            if (l.standBuilt && TouchesFootprint(p, radius, StandLayout.Of(l.Parking(Vector2.zero)).Pad)) return true;
            if (l.receptionBuilt && TouchesFootprint(p, radius, ReceptionCorners(l.reception, l.receptionHeading))) return true;
            var roads = state.RoadNetwork();
            foreach (var edge in roads.segments)
            {
                var a = PublicRoadNetwork.XZ(roads.Node(edge.a).position);
                var b = PublicRoadNetwork.XZ(roads.Node(edge.b).position);
                // Protect the roadbed and bridge supports, even when the deck is elevated.
                if (Distance(p, a, b) < radius + edge.Width * .5f + 3f) return true;
            }
            return false;
        }
        static bool TouchesFootprint(Vector2 p, float radius, IList<Vector2> corners)
        {
            if (Geo.Inside(corners, p)) return true;
            for (int i = 0; i < corners.Count; i++)
                if (Distance(p, corners[i], corners[(i + 1) % corners.Count]) < radius) return true;
            return false;
        }
        public static float Distance(Vector2 p, Vector2 a, Vector2 b) => Vector2.Distance(p, Geo.ClosestOnSegment(p, a, b, out _));

        static bool Ground(CareerState state, IEnumerable<Vector2> samples, float elevation, out string why)
        {
            var points = samples.ToArray();
            why = "Keep the footprint inside your airport land.";
            if (points.Any(p => !ValleyWorld.Finite(p.x) || !ValleyWorld.Finite(p.y) || !state.Site.Parcel.Contains(p))) return false;
            why = "Clear the trees in this footprint first.";
            var bounds = Rect.MinMaxRect(points.Min(p => p.x) - 8f, points.Min(p => p.y) - 8f, points.Max(p => p.x) + 8f, points.Max(p => p.y) + 8f);
            if (state.world.trees.Any(t => !t.removed && bounds.Contains(t.position) && points.Any(p => (p - t.position).sqrMagnitude < 64f))) return false;
            why = "Level this footprint to one height first. Use the terrain tool.";
            if (points.Any(p => Mathf.Abs(state.world.Height(p) - elevation) > .3f)) return false;
            why = null; return true;
        }
        public static IEnumerable<Vector2> Strip(Vector2 a, Vector2 b, float halfWidth, float padding = 0f)
        {
            var axis = (b - a).normalized; var side = new Vector2(-axis.y, axis.x);
            int steps = Mathf.Max(1, Mathf.CeilToInt((Vector2.Distance(a, b) + 2f * padding) / 7f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a - axis * padding, b + axis * padding, i / (float)steps);
                int across = Mathf.Max(2, Mathf.CeilToInt(halfWidth * 2f / 8f));
                for (int row = 0; row <= across; row++) yield return p + side * Mathf.Lerp(-halfWidth, halfWidth, row / (float)across);
                yield return p;
            }
        }
        public static bool Runway(CareerState state, Vector2 a, Vector2 b, bool commit, out int price, out string why)
        {
            price = 0; if (!Ready(state, CareerTool.Runway, out why)) return false;
            float length = Vector2.Distance(a, b);
            if (!ValleyWorld.Finite(length) || length < 700f || length > 1400f) { why = "Drag a grass runway at least 700 m long, up to 1,400 m."; return false; }
            var l = state.layout;
            if (l.runwayBuilt && (Distance(l.runwayA, a, b) > .2f || Distance(l.runwayB, a, b) > .2f || length < state.RunwayLength + 10f))
            { why = "Drag an existing threshold outwards to extend the runway."; return false; }
            float height = l.runwayBuilt ? l.elevation : Mathf.Round(state.world.Height((a + b) * .5f) * 4f) * .25f;
            if (!Ground(state, Strip(a, b, 22f, 15f), height, out why)) return false;
            if (state.HasAccess && state.accessRoadRoute.Skip(1).Select((p, i) => (a: state.accessRoadRoute[i], b: p))
                .Any(e => Geo.Segments(a, b, e.a, e.b, .1f, out _))) { why = "Keep the runway clear of the public road."; return false; }
            price = Mathf.CeilToInt((length - (l.runwayBuilt ? state.RunwayLength : 0f)) * 20f);
            if (state.funds < price) { why = "Not enough funds for this runway."; return false; }
            if (commit)
            {
                state.funds -= price; l.runwayA = a; l.runwayB = b; l.elevation = height; l.runwayBuilt = true;
                l.Unlock(CareerTool.Taxiway); state.expanded = state.completedCharters > 0 && length >= 1000f;
            }
            return true;
        }
        public static bool Taxiway(CareerState state, IList<Vector2> points, bool commit, out int price, out string why)
        {
            price = 0; if (!Ready(state, CareerTool.Taxiway, out why)) return false;
            if (points == null || points.Count < 2 || points.Count > 32 || state.layout.taxiways.Count >= 32)
            { why = "Click at least two points for the taxiway; Enter finishes the line."; return false; }
            var l = state.layout;
            bool connects = points.Any(p => Distance(p, l.runwayA, l.runwayB) < 1f || l.TaxiSegments().Any(e => Distance(p, e.a, e.b) < 1f));
            if (!connects) { why = "Start or finish on the runway or an existing taxiway."; return false; }
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float distance = Vector2.Distance(points[i - 1], points[i]);
                if (distance < 12f || distance > 2000f) { why = "Leave at least 12 m between taxiway corners."; return false; }
                if (!Ground(state, Strip(points[i - 1], points[i], 7f), l.elevation, out why)) return false;
                length += distance;
            }
            price = 200 + Mathf.CeilToInt(length * 8f);
            if (state.funds < price) { why = "Not enough funds for this taxiway."; return false; }
            if (commit) { state.funds -= price; l.taxiways.Add(new CareerTaxiway { points = new List<Vector2>(points) }); l.Unlock(CareerTool.Stand); }
            return true;
        }

        public static Stand StandPreview(CareerState state, Vector2 requested)
        {
            Vector2 point = default, along = default; float best = 55f;
            foreach (var e in state.layout.TaxiSegments())
            {
                var q = Geo.ClosestOnSegment(requested, e.a, e.b, out float t);
                float d = Vector2.Distance(requested, q);
                if (d >= best || t < .08f || t > .92f || Distance(q, state.layout.runwayA, state.layout.runwayB) < 35f) continue;
                best = d; point = q; along = (e.b - e.a).normalized;
            }
            if (along == Vector2.zero) return null;
            var away = new Vector2(-along.y, along.x);
            if (Vector2.Dot(away, requested - point) < 0f) away = -away;
            var stand = new Stand { kind = StandKind.Parking, code = SizeCode.A, surface = PavementSurface.Grass,
                exit = StandExit.TaxiOut, position = new Vector3(point.x, 0f, point.y), heading = Mathf.Atan2(away.x, away.y) * Mathf.Rad2Deg };
            stand.Frame(along, 7.5f); return stand;
        }
        public static bool Parking(CareerState state, Vector2 point, bool commit, out string why)
        {
            if (!Ready(state, CareerTool.Stand, out why)) return false;
            var stand = StandPreview(state, point);
            if (stand == null) { why = "Click beside the middle of a taxiway, clear of the runway."; return false; }
            var pad = StandLayout.Of(stand);
            var samples = new List<Vector2>(pad.Pad);
            for (float x = pad.x0; x <= pad.x1; x += 5f)
            for (float z = pad.near0; z <= pad.far; z += 5f) samples.Add(pad.At(x, z));
            if (!Ground(state, samples, state.layout.elevation, out why)) return false;
            if (samples.Any(p => Distance(p, state.layout.runwayA, state.layout.runwayB) < 30f)) { why = "The parking pad needs more clearance from the runway."; return false; }
            if (!commit) { why = state.funds < StandPrice ? "Not enough funds for this stand." : null; return why == null; }
            if (!Pay(state, StandPrice, out why)) return false;
            var l = state.layout; l.stand = new Vector2(stand.position.x, stand.position.z); l.standAlong = stand.Front;
            l.standHeading = stand.heading; l.standBuilt = true; l.Unlock(CareerTool.Road); return true;
        }

        public static bool Road(CareerState state, IList<Vector2> route, out string why)
        {
            if (!Ready(state, CareerTool.Road, out why)) return false;
            why = "Start inside your airport land, then finish on the valley road.";
            if (route == null || route.Count < 2 || route.Count > 32 || !state.Site.Parcel.Contains(route[0]) ||
                !PublicRoadLayout.TrySnap(state.world, route[route.Count - 1], .2f, out _) || PublicRoadLayout.Length(route) > 4000f) return false;
            for (int i = 1; i < route.Count; i++)
            {
                var a = route[i - 1]; var b = route[i];
                if (!ValleyWorld.Finite(a.x) || !ValleyWorld.Finite(a.y) || !ValleyWorld.Finite(b.x) || !ValleyWorld.Finite(b.y) || Vector2.Distance(a, b) < 12f)
                { why = "Leave at least 12 m between road corners."; return false; }
                for (int j = 1; j < i - 1; j++)
                    if (Geo.Segments(a, b, route[j - 1], route[j], .01f, out _)) { why = "The access road cannot cross itself."; return false; }
                foreach (var p in Strip(a, b, 6f))
                {
                    if (!Editable(state, p)) { why = "Keep the road inside your land and the airport road corridor."; return false; }
                    if (Distance(p, state.layout.runwayA, state.layout.runwayB) < 30f || state.layout.TaxiSegments().Any(e => Distance(p, e.a, e.b) < 16f))
                    { why = "Keep public vehicles clear of the runway and taxiways."; return false; }
                    if (Mathf.Abs(p.x - state.world.RiverX(p.y)) < 120f) { why = "The first access road must stay on land."; return false; }
                }
                if (state.world.trees.Any(t => !t.removed && Distance(t.position, a, b) < 12f))
                { why = "Clear the trees along your chosen road route first."; return false; }
            }
            why = null; return true;
        }
        public static bool BuildRoad(CareerState state, IList<Vector2> route, out string why)
        {
            if (state.HasAccess) { why = "The airport already has a public road connection."; return false; }
            if (!Road(state, route, out why)) return false;
            int price = PublicRoadLayout.Price(route);
            var graph = state.RoadNetwork().Clone();
            var proxy = new AirportSite { elevation = state.world.Height(route[0]) };
            if (!graph.AddAirportAccess(state.world, proxy, route, out why, false)) return false;
            if (!Pay(state, price, out why)) return false;
            state.publicRoads = graph; state.accessRoadRoute = new List<Vector2>(route); state.layout.Unlock(CareerTool.Reception); return true;
        }
        public static Vector2[] ReceptionCorners(Vector2 p, float heading)
        {
            var rot = Quaternion.Euler(0f, heading, 0f);
            return new[] { new Vector2(-13f, -8f), new Vector2(-13f, 8f), new Vector2(13f, 8f), new Vector2(13f, -8f) }
                .Select(q => { var v = rot * new Vector3(q.x, 0f, q.y); return p + new Vector2(v.x, v.z); }).ToArray();
        }
        public static bool Reception(CareerState state, Vector2 point, float heading, bool commit, out string why)
        {
            if (!Ready(state, CareerTool.Reception, out why)) return false;
            if (state.layout.receptionBuilt) { why = "Passenger reception is already built."; return false; }
            var entrance = state.accessRoadRoute[0];
            if (Vector2.Distance(point, entrance) > 55f || Vector2.Distance(point, entrance) < 12f)
            { why = "Place reception beside the airport end of your public road, 12–55 m away."; return false; }
            var corners = ReceptionCorners(point, heading); var samples = new List<Vector2>(corners) { point };
            if (!Ground(state, samples, state.layout.elevation, out why)) return false;
            if (corners.Any(p => Distance(p, state.layout.runwayA, state.layout.runwayB) < 40f || state.layout.TaxiSegments().Any(e => Distance(p, e.a, e.b) < 25f)))
            { why = "Keep the building clear of aircraft pavement."; return false; }
            for (int i = 1; i < state.accessRoadRoute.Count; i++)
                if (corners.Any(p => Distance(p, state.accessRoadRoute[i - 1], state.accessRoadRoute[i]) < 5f) ||
                    Distance(point, state.accessRoadRoute[i - 1], state.accessRoadRoute[i]) < 9f)
                { why = "Place the building beside the road."; return false; }
            if (!commit) { why = state.funds < ReceptionPrice ? "Not enough funds for reception." : null; return why == null; }
            if (!Pay(state, ReceptionPrice, out why)) return false;
            state.layout.reception = point; state.layout.receptionHeading = heading; state.layout.receptionBuilt = true; return true;
        }

        public static bool Validate(CareerState state, out string why)
        {
            var l = state.layout; why = "The saved player-built airport layout is invalid.";
            if (l == null || l.taxiways == null || l.taxiways.Count > 32 || l.clearedTrees < 0 || l.clearedTrees > 120000 ||
                !ValleyWorld.Finite(l.earthwork) || l.earthwork < 0f || l.unlocked < 0 || l.unlocked > 255 || !ValleyWorld.Finite(l.elevation)) return false;
            bool Point(Vector2 p) => ValleyWorld.Finite(p.x) && ValleyWorld.Finite(p.y) && state.Site != null && state.Site.Parcel.Contains(p);
            if (l.runwayBuilt && (!Point(l.runwayA) || !Point(l.runwayB) || state.RunwayLength < 699.9f || state.RunwayLength > 1400.1f || l.elevation < 10f || l.elevation > 400f)) return false;
            foreach (var t in l.taxiways) if (t == null || t.points == null || t.points.Count < 2 || t.points.Count > 32 || t.points.Any(p => !Point(p))) return false;
            if (l.standBuilt && (!l.runwayBuilt || l.taxiways.Count == 0 || !Point(l.stand) || !ValleyWorld.Finite(l.standHeading) || !ValleyWorld.Finite(l.standAlong.x) ||
                !ValleyWorld.Finite(l.standAlong.y) || Mathf.Abs(l.standAlong.magnitude - 1f) > .01f || !l.TaxiSegments().Any(e => Distance(l.stand, e.a, e.b) < .5f))) return false;
            if (l.receptionBuilt && (!state.HasAccess || !Point(l.reception) || !ValleyWorld.Finite(l.receptionHeading))) return false;
            if (state.HasAccess && (!l.standBuilt || state.accessRoadRoute.Count > 32 || state.accessRoadRoute.Any(p => !ValleyWorld.Finite(p.x) || !ValleyWorld.Finite(p.y)))) return false;
            if ((state.charterActive || state.completedCharters > 0) && !state.IsOpen) return false;
            why = null; return true;
        }
    }
}
