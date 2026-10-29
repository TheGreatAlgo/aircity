using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Traffic
{
    /// <summary>
    /// The way an aircraft's nose wheel goes on the ground: along the taxiway centrelines and round curves sized for
    /// the taxiway, into and out of stands, onto, along and off runways. Sampled every Step metres, with the main
    /// gear's way (the aircraft's origin trails the nose wheel by its wheelbase and cuts inside on curves, like a
    /// trailer) and the fastest it may go at each point: the turns ahead and the stop at the end, braking at each
    /// stretch's own rate. The aircraft's pose comes from how far its nose wheel has come (Pose).
    ///
    ///   var b = new TaxiPath.Builder(noseStart, heading);
    ///   b.Limit(10f, 1f);  b.Via(node, outDirection, 37.5f, 60f, 60f);  b.LineTo(end);
    ///   var path = b.Build(gearStart, type.Wheelbase, 0.8f);
    ///
    /// Positions are in the Airport's space (x, z).
    /// </summary>
    public sealed class TaxiPath
    {
        public const float Step = 0.5f;

        readonly List<Vector2> nose = new List<Vector2>(), gear = new List<Vector2>(), heading = new List<Vector2>();
        readonly List<float> steer = new List<float>(), brake = new List<float>(), gearRatio = new List<float>();

        TaxiPath() { }

        /// <summary>The nose wheel's way, metres.</summary>
        public float Length => (nose.Count - 1) * Step;
        public int Count => nose.Count;
        public Vector2 NoseAt(int i) => nose[Mathf.Clamp(i, 0, nose.Count - 1)];
        public Vector2 GearAt(int i) => gear[Mathf.Clamp(i, 0, gear.Count - 1)];
        public Vector2 End => nose[nose.Count - 1];
        public Vector2 EndHeading => heading[heading.Count - 1];
        public Vector2 GearEnd => gear[gear.Count - 1];

        /// <summary>
        /// The pose with the nose wheel d metres along: the origin (main gear), the way the aircraft faces, the nose
        /// wheel's steering angle (degrees, positive = right) and how far the main gear goes per metre of the nose
        /// wheel's way (for the wheels' speed).
        /// </summary>
        public void Pose(float d, out Vector2 origin, out Vector2 facing, out float steering, out float ratio)
        {
            float f = Mathf.Clamp(d, 0f, Length) / Step;
            int i = Mathf.Min(Mathf.FloorToInt(f), nose.Count - 2);
            if (i < 0)
            {
                origin = gear[0];
                facing = heading[0];
                steering = steer[0];
                ratio = 1f;
                return;
            }
            float t = f - i;
            origin = Vector2.Lerp(gear[i], gear[i + 1], t);
            facing = Vector2.Lerp(heading[i], heading[i + 1], t).normalized;
            steering = Mathf.Lerp(steer[i], steer[i + 1], t);
            ratio = gearRatio[i];
        }

        /// <summary>The fastest the aircraft may go with its nose wheel d metres along, so it can still take every
        /// turn ahead and stop at the end.</summary>
        public float SpeedLimit(float d)
        {
            float f = Mathf.Clamp(d, 0f, Length) / Step;
            int i = Mathf.Min(Mathf.FloorToInt(f), nose.Count - 1);
            return Mathf.Lerp(brake[i], brake[Mathf.Min(i + 1, brake.Count - 1)], f - i);
        }

        /// <summary>Nose-wheel points from d0 to d1 (for drawing), every `every` metres.</summary>
        public void Points(float d0, float d1, float every, List<Vector2> into)
        {
            d0 = Mathf.Clamp(d0, 0f, Length);
            d1 = Mathf.Clamp(d1, d0, Length);
            int step = Mathf.Max(1, Mathf.RoundToInt(every / Step));
            int i0 = Mathf.CeilToInt(d0 / Step - 1e-4f), i1 = Mathf.FloorToInt(d1 / Step + 1e-4f);
            for (int i = i0; i <= i1; i += step) into.Add(nose[i]);
            if (into.Count == 0 || (into[into.Count - 1] - nose[i1]).sqrMagnitude > 1e-4f) into.Add(nose[i1]);
        }

        // ------------------------------------------------------------------ building

        /// <summary>Lays the nose wheel's way piece by piece: straights, arcs, corners rounded off with a fillet,
        /// a U-turn; each piece takes the speed limit and braking set last (Limit).</summary>
        public sealed class Builder
        {
            const float Fine = 0.25f;
            readonly List<Vector2> pts = new List<Vector2>();
            readonly List<float> curve = new List<float>(), top = new List<float>(), braking = new List<float>();
            Vector2 at, dir;
            float length, speed = float.PositiveInfinity, decel = 1f;

            public Builder(Vector2 start, Vector2 heading)
            {
                at = start;
                dir = heading.sqrMagnitude > 1e-8f ? heading.normalized : Vector2.up;
                Add(start, 0f);
            }

            public Vector2 At => at;
            public Vector2 Heading => dir;
            /// <summary>How far the way has come so far (metres).</summary>
            public float Length => length;

            /// <summary>Top speed (m/s) and braking (m/s²) for the pieces laid from here on.</summary>
            public void Limit(float topSpeed, float brakingRate)
            {
                speed = topSpeed;
                decel = Mathf.Max(brakingRate, 0.05f);
            }

            void Add(Vector2 p, float k)
            {
                if (pts.Count > 0) length += Vector2.Distance(pts[pts.Count - 1], p);
                pts.Add(p);
                curve.Add(k);
                top.Add(speed);
                braking.Add(decel);
            }

            public void LineTo(Vector2 p)
            {
                var d = p - at;
                float len = d.magnitude;
                if (len < 1e-3f) return;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / Fine));
                for (int i = 1; i <= n; i++) Add(at + d * (i / (float)n), 0f);
                dir = d / len;
                at = p;
            }

            /// <summary>Straight on for a distance.</summary>
            public void Ahead(float distance) => LineTo(at + dir * distance);

            /// <summary>An arc of this radius through `angle` radians (positive = left, counter-clockwise from above).</summary>
            public void Turn(float radius, float angle)
            {
                if (Mathf.Abs(angle) < 1e-4f) return;
                if (radius < 0.05f)
                {
                    dir = Rotate(dir, angle);
                    return;
                }
                var centre = at + (angle > 0f ? Geo.Left(dir) : Geo.Right(dir)) * radius;
                var r0 = at - centre;
                int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(angle) * radius / Fine));
                for (int i = 1; i <= n; i++) Add(centre + Rotate(r0, angle * i / n), 1f / radius);
                at = pts[pts.Count - 1];
                dir = Rotate(dir, angle);
            }

            /// <summary>
            /// Straight to `corner`, rounding it off onto `outward` with an arc of `radius`, tangent no further than
            /// maxIn back along the way in (nor behind where the way is now) or maxOut out along the way out (the
            /// radius shrinks to fit), taken at no more than arcSpeed. Returns the tangent distance used: the way
            /// rejoins the straight that far beyond the corner.
            /// </summary>
            public float Via(Vector2 corner, Vector2 outward, float radius, float maxIn, float maxOut,
                float arcSpeed = float.PositiveInfinity)
            {
                var din = corner - at;
                float room = din.magnitude;
                if (room < 1e-3f) din = dir;
                din.Normalize();
                outward.Normalize();
                float turn = Vector2.SignedAngle(din, outward) * Mathf.Deg2Rad;
                if (Mathf.Abs(turn) < 0.5f * Mathf.Deg2Rad)
                {
                    LineTo(corner);
                    dir = outward;
                    return 0f;
                }
                float tanHalf = Mathf.Tan(Mathf.Abs(turn) * 0.5f);
                float cut = Mathf.Max(0f, Mathf.Min(radius * tanHalf, Mathf.Min(Mathf.Min(maxIn, room), maxOut)));
                LineTo(corner - din * cut);
                dir = din;
                float was = speed;
                speed = Mathf.Min(speed, arcSpeed);
                Turn(cut / Mathf.Max(tanHalf, 1e-4f), turn);
                speed = was;
                return cut;
            }

            /// <summary>
            /// Turn round within a strip (a runway): out to the right, a wide left turn of 300° and back in to the
            /// right, ending where it began facing the other way. It reaches 2.73 radii ahead and one to each side.
            /// Mirrored with left = false.
            /// </summary>
            public void UTurn(float radius, bool left = true)
            {
                float s = left ? 1f : -1f;
                const float beta = 60f * Mathf.Deg2Rad;
                var start = at;
                var back = -dir;
                Turn(radius, -s * beta);
                Turn(radius, s * (Mathf.PI + 2f * beta));
                Turn(radius, -s * beta);
                at = start;
                dir = back;
            }

            /// <summary>How far ahead of where it starts a U-turn of this radius reaches.</summary>
            public static float UTurnReach(float radius) => radius * (1f + 2f * Mathf.Sin(60f * Mathf.Deg2Rad));

            /// <summary>
            /// The finished way: resampled every Step metres, the main gear's way trailing the nose wheel from
            /// gearStart (wheelbase behind it), and the speed limits: the top speeds, turns taken at no more than
            /// `lateral` m/s² sideways, and braking to a stop at the end.
            /// </summary>
            public TaxiPath Build(Vector2 gearStart, float wheelbase, float lateral)
            {
                var p = new TaxiPath();
                var limit = new List<float>();
                var rate = new List<float>();
                // resample by distance along the fine points
                float walked = 0f, next = 0f;
                for (int i = 0; i < pts.Count; i++)
                {
                    float seg = i == 0 ? 0f : Vector2.Distance(pts[i - 1], pts[i]);
                    while (next <= walked + seg + 1e-5f)
                    {
                        float t = seg > 1e-6f ? Mathf.Clamp01((next - walked) / seg) : 1f;
                        var q = i == 0 ? pts[0] : Vector2.Lerp(pts[i - 1], pts[i], t);
                        p.nose.Add(q);
                        float k = curve[i];
                        limit.Add(Mathf.Min(top[i], k > 1e-4f ? Mathf.Sqrt(lateral / k) : float.PositiveInfinity));
                        rate.Add(braking[i]);
                        next += Step;
                    }
                    walked += seg;
                }
                if (p.nose.Count == 0 || (p.nose[p.nose.Count - 1] - pts[pts.Count - 1]).sqrMagnitude > 1e-4f)
                {
                    // the last bit, shorter than a step: end exactly at the last point
                    if (p.nose.Count > 1 && (p.nose[p.nose.Count - 1] - pts[pts.Count - 1]).magnitude < Step * 0.5f)
                        p.nose[p.nose.Count - 1] = pts[pts.Count - 1];
                    else
                    {
                        p.nose.Add(pts[pts.Count - 1]);
                        limit.Add(top[top.Count - 1]);
                        rate.Add(braking[braking.Count - 1]);
                    }
                }
                if (p.nose.Count == 1)
                {
                    p.nose.Add(p.nose[0] + dir * 0.01f);
                    limit.Add(0f);
                    rate.Add(1f);
                }

                // the main gear trails the nose wheel (a trailer's tractrix)
                float wb = Mathf.Max(wheelbase, 0.3f);
                var g = gearStart;
                if ((p.nose[0] - g).sqrMagnitude < 1e-6f) g = p.nose[0] - dir * wb;
                for (int i = 0; i < p.nose.Count; i++)
                {
                    var n = p.nose[i];
                    var d = n - g;
                    if (d.sqrMagnitude < 1e-8f) d = i > 0 ? p.heading[i - 1] : dir;
                    var prev = g;
                    g = n - d.normalized * wb;
                    p.gear.Add(g);
                    p.heading.Add((n - g).normalized);
                    if (i > 0) p.gearRatio.Add(Vector2.Distance(prev, g) / Step);
                }
                p.gearRatio.Add(p.gearRatio.Count > 0 ? p.gearRatio[p.gearRatio.Count - 1] : 1f);
                for (int i = 0; i < p.nose.Count; i++)
                {
                    var tangent = p.nose[Mathf.Min(i + 1, p.nose.Count - 1)] - p.nose[Mathf.Max(i - 1, 0)];
                    p.steer.Add(tangent.sqrMagnitude > 1e-8f ? -Vector2.SignedAngle(p.heading[i], tangent) : 0f);
                }

                // braking: from the stop at the end backwards, each stretch at its own rate
                for (int i = 0; i < p.nose.Count; i++) p.brake.Add(0f);
                p.brake[p.nose.Count - 1] = 0f;
                for (int i = p.nose.Count - 2; i >= 0; i--)
                {
                    float v = p.brake[i + 1];
                    p.brake[i] = Mathf.Min(limit[i], Mathf.Sqrt(v * v + 2f * rate[i] * Step));
                }
                return p;
            }

            static Vector2 Rotate(Vector2 v, float a)
            {
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
            }
        }
    }
}
