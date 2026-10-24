using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// A path for a team of vehicles on the ground plane (Airport space, Vector2 = (x, z)), sampled every metre or
    /// two: straight runs, taxiway routes with rounded corners, and turns joining one heading to another within a
    /// turning radius (Dubins curves: turn, straight, turn). Each point also carries the team's spread (0 = single
    /// file, 1 = in echelon), a speed limit and the gear: a stretch can be driven in reverse (backing into a side
    /// taxiway to turn round, or into a garage bay), the vehicle facing against the way it moves. Where the gear
    /// changes (a cusp) the speed limit drops to a crawl, so vehicles stop there.
    ///
    /// Headings passed in are directions of travel; reversing, the vehicle faces the other way.
    /// </summary>
    public sealed class VehiclePath
    {
        const float Step = 1.5f;
        public const float CuspSpeed = 0.4f;

        readonly List<Vector2> points = new List<Vector2>();
        readonly List<float> distance = new List<float>();
        readonly List<float> spread = new List<float>();
        readonly List<float> limit = new List<float>();
        readonly List<sbyte> gear = new List<sbyte>();

        public int Count => points.Count;
        public float Length => distance.Count > 0 ? distance[distance.Count - 1] : 0f;
        public Vector2 End => points.Count > 0 ? points[points.Count - 1] : Vector2.zero;
        /// <summary>Direction of travel at the end.</summary>
        public Vector2 EndHeading
        {
            get
            {
                int n = points.Count;
                for (int i = n - 2; i >= 0; i--)
                {
                    var d = points[n - 1] - points[i];
                    if (d.sqrMagnitude > 0.01f) return d.normalized;
                }
                return Vector2.up;
            }
        }
        /// <summary>Which way the vehicle faces at the end (the heading, turned round if it ends reversing).</summary>
        public Vector2 EndFacing => EndHeading * (EndReversing ? -1f : 1f);
        public bool EndReversing => gear.Count > 0 && gear[gear.Count - 1] < 0;

        public void Clear()
        {
            points.Clear();
            distance.Clear();
            spread.Clear();
            limit.Clear();
            gear.Clear();
        }

        void Add(Vector2 p, float fan, float speed, bool reverse = false)
        {
            sbyte g = reverse ? (sbyte)-1 : (sbyte)1;
            if (points.Count > 0)
            {
                float d = Vector2.Distance(points[points.Count - 1], p);
                if (d < 0.05f)
                {
                    spread[spread.Count - 1] = fan;
                    limit[limit.Count - 1] = Mathf.Min(limit[limit.Count - 1], speed);
                    if (gear[gear.Count - 1] != g && points.Count > 1)
                    {
                        // turning round on the spot: the previous point is the cusp
                        limit[limit.Count - 1] = CuspSpeed;
                        gear[gear.Count - 1] = g;
                    }
                    return;
                }
                // the gear of a segment is the gear of the point it reaches; a change of gear stops at the cusp
                if (points.Count > 1 && gear[gear.Count - 1] != g) limit[limit.Count - 1] = CuspSpeed;
                distance.Add(distance[distance.Count - 1] + d);
            }
            else distance.Add(0f);
            points.Add(p);
            spread.Add(fan);
            limit.Add(speed);
            gear.Add(g);
        }

        public void Start(Vector2 p, float speed) => Add(p, 0f, speed);

        /// <summary>Append another path (its first point joins this one's end).</summary>
        public void Append(VehiclePath other)
        {
            for (int i = 0; i < other.points.Count; i++)
                Add(other.points[i], other.spread[i], other.limit[i], other.gear[i] < 0);
        }

        /// <summary>Keep only the stretch between two distances (resampled), as the whole path.</summary>
        public void Keep(float from, float to)
        {
            from = Mathf.Clamp(from, 0f, Length);
            to = Mathf.Clamp(to, from, Length);
            var kept = new List<(Vector2 p, float fan, float speed, bool reverse)>();
            for (float d = from; ; d += Step)
            {
                float at = Mathf.Min(d, to);
                int i = Segment(at);
                var p = Sample(at, out _, out float fan, out float speed);
                kept.Add((p, fan, speed, gear[Mathf.Min(i + 1, gear.Count - 1)] < 0));
                if (at >= to) break;
            }
            Clear();
            foreach (var (p, fan, speed, reverse) in kept) Add(p, fan, speed, reverse);
        }

        /// <summary>Straight on to p; the spread goes from fan0 to fan1 over the first / last `ramp` metres.</summary>
        public void LineTo(Vector2 p, float speed, float fan = 0f, float rampIn = 0f, float rampOut = 0f, bool reverse = false)
        {
            var a = End;
            float len = Vector2.Distance(a, p);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / Step));
            for (int i = 1; i <= n; i++)
            {
                float s = len * i / n;
                float f = fan;
                if (rampIn > 0f) f = Mathf.Min(f, fan * Mathf.Clamp01(s / rampIn));
                if (rampOut > 0f) f = Mathf.Min(f, fan * Mathf.Clamp01((len - s) / rampOut));
                Add(Vector2.Lerp(a, p, s / Mathf.Max(len, 1e-4f)), f, speed, reverse);
            }
        }

        /// <summary>Round a circle about centre, from the current end through `angle` radians (positive = left /
        /// counter-clockwise).</summary>
        public void ArcAround(Vector2 centre, float angle, float speed, bool reverse = false, float fan = 0f)
        {
            var r0 = End - centre;
            float radius = r0.magnitude;
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(angle) * radius / Step));
            float a0 = Mathf.Atan2(r0.y, r0.x);
            for (int i = 1; i <= n; i++)
            {
                float a = a0 + angle * i / n;
                Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, fan, speed, reverse);
            }
        }

        /// <summary>
        /// The shortest way from the current end, travelling along h0, to p1 travelling along h1, turning no tighter
        /// than radius (turn-straight-turn Dubins curves), forwards or (reverse) backwards.
        /// </summary>
        public void TurnTo(Vector2 h0, Vector2 p1, Vector2 h1, float radius, float speed, bool reverse = false)
        {
            if (!Dubins(End, h0, p1, h1, radius, out var pick))
            {
                LineTo(p1, speed, reverse: reverse);
                return;
            }
            ArcAround(pick.c0, pick.a0, speed, reverse);
            LineTo(pick.t1, speed, reverse: reverse);
            ArcAround(pick.c1, pick.a1, speed, reverse);
            Add(p1, 0f, speed, reverse);
        }

        /// <summary>Length of the TurnTo path (infinite if there's none).</summary>
        public static float DubinsLength(Vector2 p0, Vector2 h0, Vector2 p1, Vector2 h1, float radius) =>
            Dubins(p0, h0, p1, h1, radius, out var pick) ? pick.length : float.PositiveInfinity;

        struct DubinsPath
        {
            public Vector2 c0, c1, t1;
            public float a0, a1, length;
        }

        static bool Dubins(Vector2 p0, Vector2 h0, Vector2 p1, Vector2 h1, float radius, out DubinsPath pick)
        {
            h0 = h0.normalized;
            h1 = h1.normalized;
            float best = float.MaxValue;
            pick = default;
            for (int s0 = 1; s0 >= -1; s0 -= 2)            // +1 left, -1 right
                for (int s1 = 1; s1 >= -1; s1 -= 2)
                {
                    var c0 = p0 + Side(h0, s0) * radius;
                    var c1 = p1 + Side(h1, s1) * radius;
                    var v = c1 - c0;
                    float D = v.magnitude;
                    Vector2 w;
                    if (s0 == s1)
                        w = D > 1e-4f ? v / D : h0;
                    else
                    {
                        if (D < 2f * radius) continue;
                        float alpha = Mathf.Asin(2f * radius / D) * (s0 > 0 ? 1f : -1f);
                        w = Rotate(v / D, alpha);
                    }
                    var t0 = c0 - Side(w, s0) * radius;
                    var t1 = c1 - Side(w, s1) * radius;
                    if (s0 != s1 && Vector2.Dot(t1 - t0, w) < -1e-3f) continue;
                    float a0 = Sweep(p0 - c0, t0 - c0, s0);
                    float a1 = Sweep(t1 - c1, p1 - c1, s1);
                    float len = radius * (a0 + a1) + Vector2.Distance(t0, t1);
                    if (len < best)
                    {
                        best = len;
                        pick = new DubinsPath { c0 = c0, a0 = a0 * s0, c1 = c1, a1 = a1 * s1, t1 = t1, length = len };
                    }
                }
            return best < float.MaxValue;
        }

        /// <summary>Through route points (a taxiway route), rounding each corner with up to `radius`; a lane
        /// `offsetRight` metres right of a centreline rounded with `radius` turns concentrically with it (tighter on
        /// the inside of a bend, wider on the outside). pull(side, r) can ask for a corner to be cut by that many
        /// metres at its apex (side +1 = a left turn), so a long vehicle's front swinging wide stays on the pavement.</summary>
        public void Through(IList<Vector2> route, float radius, float speed, float fan = 0f, float offsetRight = 0f,
            System.Func<int, float, float> pull = null)
        {
            int n = route.Count;
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                var p = route[i];
                if (i == n - 1)
                {
                    LineTo(p, speed, fan);
                    break;
                }
                var prev = i == 0 ? End : route[i - 1];
                var next = route[i + 1];
                var din = p - prev;
                var dout = next - p;
                if (din.sqrMagnitude < 0.01f || dout.sqrMagnitude < 0.01f)
                {
                    LineTo(p, speed, fan);
                    continue;
                }
                float lin = din.magnitude, lout = dout.magnitude;
                din /= lin;
                dout /= lout;
                float theta = Mathf.Acos(Mathf.Clamp(Vector2.Dot(din, dout), -1f, 1f));
                if (theta < 2f * Mathf.Deg2Rad)
                {
                    LineTo(p, speed, fan);
                    continue;
                }
                float tanHalf = Mathf.Tan(Mathf.Min(theta, 170f * Mathf.Deg2Rad) * 0.5f);
                int side = Geo.Cross(din, dout) > 0f ? 1 : -1;
                float rc = Mathf.Max(radius + side * offsetRight, 1f);
                if (pull != null)
                {
                    float apex = 1f / Mathf.Cos(Mathf.Min(theta, 170f * Mathf.Deg2Rad) * 0.5f) - 1f;
                    float cut = pull(side, rc);
                    if (cut > 0f && apex > 1e-3f) rc = Mathf.Min(rc + cut / apex, rc * 3f);
                }
                float t = Mathf.Min(rc * tanHalf, 0.45f * Mathf.Min(lin, lout));
                float r = t / tanHalf;
                float turnSpeed = Mathf.Min(speed, Mathf.Max(3f, Mathf.Sqrt(r * 2.5f)));
                LineTo(p - din * t, speed, fan);
                var centre = End + Side(din, side) * r;
                ArcAround(centre, Sweep(End - centre, p + dout * t - centre, side) * side, turnSpeed, false, fan);
            }
        }

        /// <summary>Set the spread over [from, to] (path distances): `fan`, ramping up over the first `rampIn` and
        /// down over the last `rampOut` metres.</summary>
        public void SetSpread(float from, float to, float fan, float rampIn, float rampOut)
        {
            for (int i = 0; i < points.Count; i++)
            {
                float d = distance[i];
                if (d < from || d > to) continue;
                float f = fan;
                if (rampIn > 0f) f = Mathf.Min(f, fan * Mathf.Clamp01((d - from) / rampIn));
                if (rampOut > 0f) f = Mathf.Min(f, fan * Mathf.Clamp01((to - d) / rampOut));
                spread[i] = f;
            }
        }

        static Vector2 Side(Vector2 d, int s) => s > 0 ? Geo.Left(d) : Geo.Right(d);

        static Vector2 Rotate(Vector2 v, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Angle swept from a to b around a circle turning left (s = 1, counter-clockwise) or right.</summary>
        static float Sweep(Vector2 a, Vector2 b, int s)
        {
            float t = s > 0 ? Geo.AngleCcw(a, b) : Geo.AngleCcw(b, a);
            return t > 2f * Mathf.PI - 1e-4f ? 0f : t;
        }

        // ------------------------------------------------------------------ sampling

        int Segment(float d)
        {
            int lo = 0, hi = distance.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (distance[mid] <= d) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>Position at distance d (clamped), its direction of travel, spread and speed limit.</summary>
        public Vector2 Sample(float d, out Vector2 heading, out float fan, out float speedLimit)
        {
            if (points.Count < 2)
            {
                heading = Vector2.up;
                fan = 0f;
                speedLimit = 0f;
                return points.Count == 1 ? points[0] : Vector2.zero;
            }
            d = Mathf.Clamp(d, 0f, Length);
            int i = Segment(d);
            int j = Mathf.Min(i + 1, points.Count - 1);
            float seg = distance[j] - distance[i];
            float t = seg > 1e-5f ? (d - distance[i]) / seg : 0f;
            heading = Heading(i, d);
            fan = Mathf.Lerp(spread[i], spread[j], t);
            speedLimit = Mathf.Min(limit[i], limit[j]);
            return Vector2.Lerp(points[i], points[j], t);
        }

        /// <summary>Which way segment k (point k to k + 1) faces: its direction, turned round if it's reversed.</summary>
        Vector2 Facing(int k)
        {
            var dir = points[k + 1] - points[k];
            if (dir.sqrMagnitude < 1e-8f) dir = Vector2.up;
            return dir.normalized * gear[k + 1];
        }

        /// <summary>
        /// Direction of travel at d on segment i. A chord's direction is the tangent at its middle, so the facing
        /// turns from one chord's middle to the next (a 1.5 m chord on a 10 m turn is 9°: taken as is, the vehicle
        /// would turn in clicks). Facings, not directions, so a cusp (where the direction flips) stays smooth;
        /// a kink of more than 90° isn't smoothed.
        /// </summary>
        Vector2 Heading(int i, float d)
        {
            var f = Facing(i);
            float mid = (distance[i] + distance[i + 1]) * 0.5f;
            int k = d < mid ? i - 1 : i + 1;
            if (k >= 0 && k < points.Count - 1)
            {
                var g = Facing(k);
                float other = (distance[k] + distance[k + 1]) * 0.5f;
                if (Vector2.Dot(f, g) > 0f && Mathf.Abs(other - mid) > 1e-5f)
                    f = Rotate(f, Vector2.SignedAngle(f, g) * Mathf.Deg2Rad * (d - mid) / (other - mid));
            }
            return f * gear[i + 1];
        }

        public Vector2 Sample(float d) => Sample(d, out _, out _, out _);

        /// <summary>True where the stretch at distance d is driven backwards.</summary>
        public bool Reversing(float d)
        {
            if (gear.Count < 2) return false;
            int i = Segment(Mathf.Clamp(d, 0f, Length));
            return gear[Mathf.Min(i + 1, gear.Count - 1)] < 0;
        }

        /// <summary>The vehicle's pose at d: position, which way it faces (against the travel when reversing), and
        /// the path's curvature there (1/m, positive = turning left as it travels).</summary>
        public Vector2 Pose(float d, out Vector2 facing, out float curvature)
        {
            var p = Sample(d, out var h, out _, out _);
            bool rev = Reversing(d);
            facing = rev ? -h : h;
            Sample(d - 1f, out var h0, out _, out _);
            Sample(d + 1f, out var h1, out _, out _);
            curvature = Reversing(d - 1f) != Reversing(d + 1f) ? 0f : Vector2.SignedAngle(h0, h1) * Mathf.Deg2Rad / 2f;
            return p;
        }

        /// <summary>Distances of the cusps (where the gear changes).</summary>
        public void Cusps(List<float> into)
        {
            for (int i = 1; i < gear.Count - 1; i++)
                if (gear[i] != gear[i + 1]) into.Add(distance[i]);
        }

        /// <summary>Every sample point with its facing (for checking where a vehicle's footprint goes).</summary>
        public void Poses(float from, float to, float step, List<(Vector2 p, Vector2 facing)> into)
        {
            for (float d = Mathf.Max(from, 0f); d <= Mathf.Min(to, Length); d += step)
            {
                var p = Pose(d, out var f, out _);
                into.Add((p, f));
            }
        }
    }
}
