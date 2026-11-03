using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>A fixed boarding corridor, shared by rendering, support placement and clearance checks.</summary>
    public sealed class BridgeCorridor
    {
        public readonly List<Vector3> points = new List<Vector3>();
        public readonly float width, bottom, top;
        public float Length { get; private set; }
        public float Slope { get; private set; }

        public BridgeCorridor(StandLayout stand, Vector2 root, float startFloor, Vector2 end, float endFloor,
            IList<Vector3> bends, float trimEnd, float width, float bottom, float top)
        {
            this.width = width; this.bottom = bottom; this.top = top;
            points.Add(Geo.X0Z(root, startFloor));
            if (bends != null)
                foreach (var bend in bends)
                {
                    var p = Geo.X0Z(stand.At(bend.x, bend.z), bend.y);
                    if (Vector2.Distance(Geo.XZ(points[points.Count - 1]), Geo.XZ(p)) > 0.05f) points.Add(p);
                }
            var last = points[points.Count - 1];
            var delta = end - Geo.XZ(last);
            float run = Mathf.Max(delta.magnitude - trimEnd, 0f);
            if (run > 0.05f) points.Add(Geo.X0Z(Geo.XZ(last) + delta.normalized * run, endFloor));
            else if (Mathf.Abs(last.y - endFloor) > 0.001f) Slope = float.PositiveInfinity;
            for (int i = 1; i < points.Count; i++)
            {
                float length = Vector2.Distance(Geo.XZ(points[i - 1]), Geo.XZ(points[i]));
                Length += length;
                Slope = Mathf.Max(Slope, Mathf.Abs(points[i].y - points[i - 1].y) / length);
            }
        }

        public IEnumerable<Solid> Solids(int joint = 0)
        {
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                var dir = (b - a).normalized;
                if (i > 1) a -= dir * (width * 0.5f);
                if (i < points.Count - 1) b += dir * (width * 0.5f);
                yield return new Solid(Geo.XZ(a), Geo.XZ(b), width * 0.5f, a.y, b.y, bottom, top, false, joint);
            }
        }

        /// <summary>A drawn corridor reaches at most this far back into a wall, and this much past the facade.</summary>
        const float MaxIntoWall = 6f, PastWall = 0.1f;

        /// <summary>
        /// How far the corridor's first segment has to reach back for its end to be hidden in `building`: every
        /// corner of its end section (both sides, underside and roof) inside the facade at its own height, also where
        /// the corridor meets the wall at an angle, stops short of it (within ConnectReach) or the wall leans. A corner
        /// whose line back doesn't meet the building within MaxIntoWall adds nothing. Drawing only: placement checks
        /// use the corridor as laid out.
        /// </summary>
        public float IntoWall(Terminal building)
        {
            if (building == null || building.corners.Count < 3 || points.Count < 2) return 0f;
            var start = Geo.XZ(points[0]);
            var heading = Geo.XZ(points[1]) - start;
            if (heading.sqrMagnitude < 1e-8f) return 0f;
            heading.Normalize();
            var right = Geo.Right(heading);
            float reach = 0f;
            foreach (float height in new[] { points[0].y + bottom, points[0].y + top })
            {
                var wall = building.WallOutline(Mathf.Clamp(height, 0f, building.RoofHeight));
                foreach (float side in new[] { -0.5f, 0.5f })
                {
                    var corner = start + right * (side * width);
                    if (Geo.Inside(wall, corner)) continue;
                    float back = float.MaxValue;
                    for (int i = 0; i < wall.Count; i++)
                    {
                        var q = wall[i];
                        if (Geo.Lines(corner, -heading, q, wall[(i + 1) % wall.Count] - q, out float u, out float v) && u >= 0f && v >= 0f && v <= 1f)
                            back = Mathf.Min(back, u);
                    }
                    if (back <= MaxIntoWall) reach = Mathf.Max(reach, back + PastWall);
                }
            }
            return reach;
        }

        public List<Vector2[]> Footprints()
        {
            var list = new List<Vector2[]>();
            foreach (var solid in Solids()) list.Add(solid.corners);
            return list;
        }

        public float FloorAt(Vector2 p) => Sample(p).floor;
        public Vector2 DirectionAt(Vector2 p) => Sample(p).direction;
        (float floor, Vector2 direction, float station, float distance) Sample(Vector2 p)
        {
            float best = float.MaxValue, floor = points[0].y, station = 0f, along = 0f;
            var direction = Vector2.up;
            for (int i = 1; i < points.Count; i++)
            {
                var a = Geo.XZ(points[i - 1]);
                var b = Geo.XZ(points[i]);
                float distance = Vector2.Distance(p, Geo.ClosestOnSegment(p, a, b, out float t));
                float length = Vector2.Distance(a, b);
                if (distance < best)
                {
                    best = distance; floor = Mathf.Lerp(points[i - 1].y, points[i].y, t);
                    direction = (b - a).normalized; station = along + t * length;
                }
                along += length;
            }
            return (floor, direction, station, best);
        }

        static bool Free(Vector2 p, IList<Vector2[]> pavement)
        {
            float r = JetBridgeRules.ColumnFoot;
            var foot = new Solid(p - Vector2.up * r, p + Vector2.up * r, r, 0f, 0f, 0f, 1f, true);
            if (pavement != null)
                foreach (var q in pavement)
                    if (Convex.Overlap(foot.corners, q)) return false;
            return true;
        }

        public List<Vector2> Columns(StandLayout stand, IList<Vector2> authored, IList<Vector2[]> pavement)
        {
            var result = new List<Vector2>();
            if (authored != null && authored.Count > 0)
            {
                var stations = new List<(float station, Vector2 point)>();
                foreach (var v in authored)
                {
                    var p = stand.At(v.x, v.y);
                    var sample = Sample(p);
                    if (sample.distance > 0.1f || !Free(p, pavement)) return null;
                    stations.Add((sample.station, p));
                }
                stations.Sort((a, b) => a.station.CompareTo(b.station));
                float last = 0f;
                foreach (var (s, p) in stations)
                {
                    if (s - last > JetBridgeRules.LinkSpan + 0.001f || s <= last + 0.05f) return null;
                    result.Add(p); last = s;
                }
                return Length - last <= JetBridgeRules.LinkSpan + 0.001f ? result : null;
            }
            for (int i = 1; i < points.Count; i++)
            {
                var a = Geo.XZ(points[i - 1]);
                var b = Geo.XZ(points[i]);
                var cols = BridgeLayout.SpanColumns(a, (b - a).normalized, Vector2.Distance(a, b), pavement);
                if (cols == null) return null;
                result.AddRange(cols);
                if (i < points.Count - 1)
                {
                    if (!Free(b, pavement)) return null;
                    result.Add(b);
                }
            }
            return result;
        }

        public bool ThroughTerminal(IList<Terminal> terminals)
        {
            if (terminals == null) return false;
            // A vertical terminal has the same section at every sample height. Build that outline once for the
            // whole corridor, rather than tessellating its curved walls for each half-metre sample.
            var vertical = new Dictionary<Terminal, List<Vector2>>();
            foreach (var terminal in terminals)
                if (terminal != null && !terminal.exterior.HasLean) vertical[terminal] = terminal.Outline();
            for (int i = 1; i < points.Count; i++)
            {
                var a = Geo.XZ(points[i - 1]);
                var b = Geo.XZ(points[i]);
                var d = (b - a).normalized;
                float length = Vector2.Distance(a, b);
                for (float s = i == 1 ? JetBridgeRules.ConnectReach + 0.5f : 0f; s <= length; s += 0.5f)
                    foreach (var t in terminals)
                    {
                        if (t == null) continue;
                        float floor = Mathf.Lerp(points[i - 1].y, points[i].y, s / length);
                        if (vertical.TryGetValue(t, out var outline))
                        {
                            if (floor + top >= 0f && floor + bottom <= t.Height && Geo.Inside(outline, a + d * s)) return true;
                        }
                        else if (t.ContainsAtHeight(a + d * s, floor) || t.ContainsAtHeight(a + d * s, floor + bottom) ||
                            t.ContainsAtHeight(a + d * s, floor + top)) return true;
                    }
            }
            return false;
        }
    }
}
