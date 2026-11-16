using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>One straight section meeting a junction. In: its direction (away from the node), half width, length and
    /// how far along it the junction may reach (the section's other end needs room too). Out: `cut`, where the
    /// section's own straight strip starts, and `kerb`, the junction's edge from this arm's left mouth corner round
    /// (counter-clockwise) to the next arm's right mouth corner, both ends included.</summary>
    public sealed class JunctionArm
    {
        public Vector2 dir;
        public float halfWidth, length, reach;
        public float cut;
        public readonly List<Vector2> kerb = new List<Vector2>();

        public JunctionArm(Vector2 dir, float halfWidth, float length, float reach)
        {
            this.dir = dir;
            this.halfWidth = halfWidth;
            this.length = length;
            this.reach = reach;
        }

        public Vector2 LeftMouth(Vector2 centre) => centre + dir * cut + Geo.Left(dir) * halfWidth;
        public Vector2 RightMouth(Vector2 centre) => centre + dir * cut + Geo.Right(dir) * halfWidth;
    }

    /// <summary>
    /// The paved shape where straight sections meet, shared by taxiways, airside roads and public roads, for any angles,
    /// widths and section lengths:
    ///  - each arm's strip is cut back far enough that it overlaps no other section's strip (measured as the sections'
    ///    rectangles, whole length, so shallow angles and wide neighbours are handled, not only the neighbours' kerbs);
    ///  - each corner of less than 180° gets a kerb fillet of the radius asked for, shrunk to fit the arms' reach;
    ///    where one arm is much wider the kerbs meet behind the node and the narrow arm flares into the wide one;
    ///  - each gap of more than 180° is mitred or chamfered, or rounded about the node when very wide;
    ///  - the outline is the union of the arms' mouths and those corner pieces, traced as the farthest boundary seen
    ///    from the node. It is always star-shaped about the node, so a fan from the node fills it without folding.
    /// Arms must be sorted counter-clockwise by direction. Work is done in doubles relative to the node.
    /// </summary>
    public static class JunctionShape
    {
        /// <summary>Within this many degrees of straight on, two arms just meet (no corner piece).</summary>
        public const float StraightDegrees = 0.5f;
        /// <summary>Outside corners up to this angle are mitred or chamfered; wider ones are rounded about the node.</summary>
        public const float MitreDegrees = 200f;
        /// <summary>Kerb radii below this are left as sharp corners.</summary>
        public const float MinRadius = 0.3f;

        /// <summary>
        /// Lay out a junction about `centre`. `radius(i, k)` is the kerb radius wanted between arm i and the next arm k.
        /// Sets each arm's cut and kerb and returns the outline (counter-clockwise, star-shaped about centre), or an
        /// empty list where the arms simply meet end to end. `ok` is false where a section is too short for what meets
        /// it: the strips would still overlap at the cut (callers should refuse such layouts before they're built).
        /// </summary>
        public static List<Vector2> Solve(Vector2 centre, IList<JunctionArm> arms, Func<int, int, float> radius, out bool ok,
            float step = 1.5f)
        {
            ok = true;
            int n = arms.Count;
            var outline = new List<Vector2>();
            if (n == 0) return outline;
            var dir = new D[n];
            var hw = new double[n];
            for (int i = 0; i < n; i++)
            {
                dir[i] = new D(arms[i].dir.x, arms[i].dir.y).Normalized;
                hw[i] = arms[i].halfWidth;
                arms[i].kerb.Clear();
            }

            // how far each strip must stay back so it overlaps no other strip
            var need = new double[n];
            for (int i = 0; i < n; i++)
                for (int k = i + 1; k < n; k++)
                {
                    Overlap(dir[i], hw[i], arms[i].length, dir[k], hw[k], arms[k].length, out double si, out double sk);
                    need[i] = Math.Max(need[i], si);
                    need[k] = Math.Max(need[k], sk);
                }
            var want = (double[])need.Clone();

            var pieces = new List<List<D>>();
            var origin = new D(0, 0);
            double straight = StraightDegrees * Math.PI / 180.0, mitre = MitreDegrees * Math.PI / 180.0;
            for (int i = 0; n > 1 && i < n; i++)
            {
                int k = (i + 1) % n;
                D a = dir[i], b = dir[k];
                double phi = Ccw(a, b);
                D la = a.Left * hw[i], rb = b.Right * hw[k];
                bool hit = Lines(la, a, rb, b, out double ta, out double tb);
                D crotch = la + a * ta;
                double wide = Math.Max(hw[i], hw[k]);
                if (phi < Math.PI - straight)
                {
                    if (!hit) continue;
                    if (ta >= 0 && tb >= 0)
                    {
                        double half = phi * 0.5, rf = Math.Max(radius(i, k), 0f), td = rf / Math.Tan(half);
                        double room = Math.Min(arms[i].reach - ta, arms[k].reach - tb);
                        if (td > room)
                        {
                            td = Math.Max(room, 0);
                            rf = td * Math.Tan(half);
                        }
                        if (rf >= MinRadius)
                        {
                            var centreOf = crotch + (a + b).Normalized * (rf / Math.Sin(half));
                            var piece = new List<D> { crotch };
                            Arc(piece, centreOf, crotch + a * td, crotch + b * td, step, false);
                            pieces.Add(piece);
                            want[i] = Math.Max(want[i], ta + td);
                            want[k] = Math.Max(want[k], tb + td);
                        }
                        else
                        {
                            want[i] = Math.Max(want[i], ta);
                            want[k] = Math.Max(want[k], tb);
                        }
                    }
                    else if (tb >= 0 && -ta <= 2.5 * wide)
                    {
                        // arm i is the wider: its kerb meets k's behind the node, so k flares out to i's edge
                        pieces.Add(new List<D> { origin, la, crotch });
                        want[k] = Math.Max(want[k], tb);
                    }
                    else if (ta >= 0 && -tb <= 2.5 * wide)
                    {
                        pieces.Add(new List<D> { origin, crotch, rb });
                        want[i] = Math.Max(want[i], ta);
                    }
                }
                else if (phi <= Math.PI + straight)
                    pieces.Add(new List<D> { origin, la, rb });
                else if (phi <= mitre)
                {
                    if (hit && ta <= 0 && tb <= 0 && crotch.Length <= 1.5 * wide) pieces.Add(new List<D> { origin, la, crotch, rb });
                    else pieces.Add(new List<D> { origin, la, rb });
                }
                else
                {
                    var piece = new List<D> { origin };
                    Arc(piece, origin, la, rb, step, true);
                    pieces.Add(piece);
                }
            }

            // cuts stay in doubles for the geometry, so a fillet's end and the mouth corner it meets are the same point
            var cuts = new double[n];
            for (int i = 0; i < n; i++)
            {
                double reach = Math.Max(arms[i].reach, 0);
                if (need[i] > reach + 1e-4) ok = false;
                cuts[i] = Math.Min(Math.Max(want[i], 0), reach);
                arms[i].cut = (float)cuts[i];
            }
            for (int i = 0; i < n; i++)
            {
                double cut = cuts[i];
                D r0 = dir[i].Right * hw[i], l0 = dir[i].Left * hw[i];
                if (cut > 1e-6) pieces.Add(new List<D> { r0, r0 + dir[i] * cut, l0 + dir[i] * cut, l0 });
                else pieces.Add(new List<D> { r0, l0 });   // no mouth: its corners still mark the kerbs' ends
            }

            var traced = n > 1 ? Merge(Trace(pieces)) : new List<D>();
            double area = 0;
            for (int i = 0; i < traced.Count; i++) area += D.Cross(traced[i], traced[(i + 1) % traced.Count]);
            if (traced.Count < 3 || area < 2e-3) traced.Clear();
            foreach (var p in traced) outline.Add(centre + new Vector2((float)p.x, (float)p.y));

            for (int i = 0; i < n; i++)
            {
                int k = (i + 1) % n;
                D from = dir[i].Left * hw[i] + dir[i] * cuts[i], to = dir[k].Right * hw[k] + dir[k] * cuts[k];
                int a = Nearest(traced, from), b = Nearest(traced, to);
                var kerb = arms[i].kerb;
                if (n > 1 && a >= 0 && b >= 0 && a != b)
                    for (int j = a; ; j = (j + 1) % traced.Count)
                    {
                        kerb.Add(centre + new Vector2((float)traced[j].x, (float)traced[j].y));
                        if (j == b) break;
                    }
                else
                {
                    kerb.Add(centre + new Vector2((float)from.x, (float)from.y));
                    if (D.Dist(from, to) > 1e-4) kerb.Add(centre + new Vector2((float)to.x, (float)to.y));
                }
            }
            return outline;
        }

        /// <summary>For each arm, how far out its strip must start so it overlaps no other arm's (whole sections):
        /// the least room a junction needs along it, whatever the kerbs.</summary>
        public static float[] Needs(IList<JunctionArm> arms)
        {
            var need = new float[arms.Count];
            for (int i = 0; i < arms.Count; i++)
                for (int k = i + 1; k < arms.Count; k++)
                {
                    OverlapReach(arms[i].dir, arms[i].halfWidth, arms[i].length, arms[k].dir, arms[k].halfWidth, arms[k].length,
                        out float si, out float sk);
                    need[i] = Mathf.Max(need[i], si);
                    need[k] = Mathf.Max(need[k], sk);
                }
            return need;
        }

        /// <summary>How far along a section a junction (or bend) at one end may reach, sharing the section with
        /// whatever is at the other end: each end gets what it needs, plus 45% of what's left (so a stretch of straight
        /// stays between). If the two needs don't fit, each gets its proportional share and the layout misfits.</summary>
        public static float Reach(float length, float needHere, float needThere)
        {
            float spare = length - needHere - needThere;
            if (spare >= 0f) return needHere + 0.45f * spare;
            return needHere + needThere > 1e-6f ? length * needHere / (needHere + needThere) : 0f;
        }

        sealed class Memo
        {
            public float[] key;
            public List<Vector2> outline;
            public float[] cuts;
            public List<Vector2>[] kerbs;
            public bool ok;
        }
        static readonly Dictionary<int, List<Memo>> memos = new Dictionary<int, List<Memo>>();
        static int memoCount;
        /// <summary>Remembered solutions kept at most (the builders lay every junction out again on each change; most
        /// are the same as before).</summary>
        const int MemoLimit = 8192;

        /// <summary>Solve, remembering the answer for the same inputs (centre, arms, radii, step): rebuilding the whole
        /// airfield after one change then only solves the junctions that change touched.</summary>
        public static List<Vector2> SolveCached(Vector2 centre, IList<JunctionArm> arms, Func<int, int, float> radius, out bool ok,
            float step = 1.5f)
        {
            int n = arms.Count;
            var key = new float[4 + n * 6];
            key[0] = centre.x; key[1] = centre.y; key[2] = step; key[3] = n;
            for (int i = 0; i < n; i++)
            {
                var a = arms[i];
                int o = 4 + i * 6;
                key[o] = a.dir.x; key[o + 1] = a.dir.y; key[o + 2] = a.halfWidth; key[o + 3] = a.length; key[o + 4] = a.reach;
                key[o + 5] = n > 1 ? radius(i, (i + 1) % n) : 0f;
            }
            int hash = 17;
            unchecked { foreach (float f in key) hash = hash * 31 + f.GetHashCode(); }
            if (memos.TryGetValue(hash, out var bucket))
                foreach (var m in bucket)
                    if (Same(m.key, key))
                    {
                        for (int i = 0; i < n; i++)
                        {
                            arms[i].cut = m.cuts[i];
                            arms[i].kerb.Clear();
                            arms[i].kerb.AddRange(m.kerbs[i]);
                        }
                        ok = m.ok;
                        return new List<Vector2>(m.outline);
                    }
            var radii = new float[n];
            for (int i = 0; i < n; i++) radii[i] = key[4 + i * 6 + 5];
            var outline = Solve(centre, arms, (i, k) => radii[i], out ok, step);
            if (memoCount >= MemoLimit)
            {
                memos.Clear();
                memoCount = 0;
            }
            var memo = new Memo { key = key, outline = new List<Vector2>(outline), cuts = new float[n], kerbs = new List<Vector2>[n], ok = ok };
            for (int i = 0; i < n; i++)
            {
                memo.cuts[i] = arms[i].cut;
                memo.kerbs[i] = new List<Vector2>(arms[i].kerb);
            }
            if (bucket == null) memos[hash] = bucket = new List<Memo>();
            bucket.Add(memo);
            memoCount++;
            return outline;
        }

        static bool Same(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>How far along each of two sections leaving the same node their pavements overlap (0 where they only
        /// touch): each section's strip has to start at least this far out, so a section shorter than about twice
        /// this (it has a junction at both ends) is too short for the pair.</summary>
        public static void OverlapReach(Vector2 dirA, float halfWidthA, float lengthA, Vector2 dirB, float halfWidthB, float lengthB,
            out float alongA, out float alongB)
        {
            Overlap(new D(dirA.x, dirA.y).Normalized, halfWidthA, lengthA, new D(dirB.x, dirB.y).Normalized, halfWidthB, lengthB,
                out double sa, out double sb);
            alongA = (float)sa;
            alongB = (float)sb;
        }

        /// <summary>Distance from centre at which a ray at `degrees` leaves the outline (0 if it misses), for tests.</summary>
        public static float Radial(Vector2 centre, IList<Vector2> outline, float degrees)
        {
            var edges = new List<(D, D)>();
            for (int i = 0; i < outline.Count; i++)
            {
                var p = outline[i] - centre;
                var q = outline[(i + 1) % outline.Count] - centre;
                edges.Add((new D(p.x, p.y), new D(q.x, q.y)));
            }
            return (float)RayMax(D.Unit(degrees * Math.PI / 180.0), edges, out _);
        }

        // ------------------------------------------------------------------ geometry in doubles

        readonly struct D
        {
            public readonly double x, y;
            public D(double x, double y) { this.x = x; this.y = y; }
            public static D operator +(D a, D b) => new D(a.x + b.x, a.y + b.y);
            public static D operator -(D a, D b) => new D(a.x - b.x, a.y - b.y);
            public static D operator *(D a, double s) => new D(a.x * s, a.y * s);
            public static double Cross(D a, D b) => a.x * b.y - a.y * b.x;
            public static double Dot(D a, D b) => a.x * b.x + a.y * b.y;
            public static double Dist(D a, D b) => (a - b).Length;
            public static D Unit(double angle) => new D(Math.Cos(angle), Math.Sin(angle));
            public D Left => new D(-y, x);
            public D Right => new D(y, -x);
            public double Length => Math.Sqrt(x * x + y * y);
            public double Angle => Math.Atan2(y, x);
            public D Normalized
            {
                get
                {
                    double l = Length;
                    return l > 1e-12 ? new D(x / l, y / l) : this;
                }
            }
        }

        static double Ccw(D a, D b)
        {
            double t = b.Angle - a.Angle;
            while (t < 0) t += 2 * Math.PI;
            while (t >= 2 * Math.PI) t -= 2 * Math.PI;
            return t;
        }

        static bool Lines(D p, D dp, D q, D dq, out double t, out double u)
        {
            double den = D.Cross(dp, dq);
            t = u = 0;
            if (Math.Abs(den) < 1e-9) return false;
            var w = q - p;
            t = D.Cross(w, dq) / den;
            u = D.Cross(w, dp) / den;
            return true;
        }

        /// <summary>Points along an arc about c from p0 to p1 (both included): the short way, or counter-clockwise.</summary>
        static void Arc(List<D> into, D c, D p0, D p1, double step, bool counterClockwise)
        {
            double r0 = D.Dist(p0, c), r1 = D.Dist(p1, c);
            double a0 = (p0 - c).Angle, a1 = (p1 - c).Angle;
            double sweep = a1 - a0;
            if (counterClockwise)
            {
                while (sweep < 0) sweep += 2 * Math.PI;
                while (sweep >= 2 * Math.PI) sweep -= 2 * Math.PI;
            }
            else
            {
                while (sweep > Math.PI) sweep -= 2 * Math.PI;
                while (sweep < -Math.PI) sweep += 2 * Math.PI;
            }
            int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) * Math.Max(r0, r1) / Math.Max(step, 0.01)));
            for (int i = 0; i <= count; i++)
            {
                double f = i / (double)count;
                into.Add(c + D.Unit(a0 + sweep * f) * (r0 + (r1 - r0) * f));
            }
        }

        /// <summary>Clip a convex polygon to the half-plane dot(p - a, normal) >= 0.</summary>
        static List<D> Clip(List<D> poly, D a, D normal)
        {
            var result = new List<D>(poly.Count + 1);
            for (int i = 0; i < poly.Count; i++)
            {
                D p = poly[i], q = poly[(i + 1) % poly.Count];
                double dp = D.Dot(p - a, normal), dq = D.Dot(q - a, normal);
                if (dp >= 0) result.Add(p);
                if ((dp >= 0) != (dq >= 0)) result.Add(p + (q - p) * (dp / (dp - dq)));
            }
            return result;
        }

        /// <summary>Where two sections' rectangles (from the node out to their lengths) overlap: how far along each the
        /// overlap reaches (0 if they only touch).</summary>
        static void Overlap(D a, double ha, double la, D b, double hb, double lb, out double sa, out double sb)
        {
            sa = sb = 0;
            var poly = new List<D> { a.Right * ha, a.Right * ha + a * la, a.Left * ha + a * la, a.Left * ha };
            poly = Clip(poly, new D(0, 0), b);
            if (poly.Count > 0) poly = Clip(poly, b.Right * hb, b.Left);
            if (poly.Count > 0) poly = Clip(poly, b.Left * hb, b.Right);
            if (poly.Count > 0) poly = Clip(poly, b * lb, b * -1);
            if (poly.Count < 3) return;
            double area = 0;
            for (int i = 0; i < poly.Count; i++) area += D.Cross(poly[i], poly[(i + 1) % poly.Count]);
            if (Math.Abs(area) < 1e-6) return;
            foreach (var p in poly)
            {
                sa = Math.Max(sa, D.Dot(p, a));
                sb = Math.Max(sb, D.Dot(p, b));
            }
        }

        /// <summary>The farthest point along a ray from the origin over all edges (0 if none is hit).</summary>
        static double RayMax(D d, List<(D p, D q)> edges, out int which)
        {
            double best = 0;
            which = -1;
            for (int k = 0; k < edges.Count; k++)
            {
                var (p, q) = edges[k];
                var e = q - p;
                double den = D.Cross(d, e);
                if (Math.Abs(den) < 1e-15) continue;
                double t = D.Cross(p, e) / den, u = D.Cross(p, d) / den;
                if (t > 1e-9 && u >= -1e-9 && u <= 1 + 1e-9 && t > best)
                {
                    best = t;
                    which = k;
                }
            }
            return best;
        }

        /// <summary>The boundary of the pieces' union as seen from the origin: for every direction, the farthest edge.
        /// Points are emitted at every vertex direction (twice where the boundary jumps) and wherever the farthest edge
        /// changes between them. Emitted points snap to the pieces' own vertices.</summary>
        static List<D> Trace(List<List<D>> pieces)
        {
            var edges = new List<(D, D)>();
            var verts = new List<D>();
            foreach (var poly in pieces)
            {
                int m = poly.Count;
                for (int k = 0; k < (m > 2 ? m : 1); k++) edges.Add((poly[k], poly[(k + 1) % m]));
                verts.AddRange(poly);
            }
            var angles = new List<double>();
            foreach (var v in verts)
                if (v.Length > 1e-9)
                {
                    double t = v.Angle;
                    if (t < 0) t += 2 * Math.PI;
                    angles.Add(t);
                }
            angles.Sort();
            var unique = new List<double>();
            foreach (var t in angles)
                if (unique.Count == 0 || t - unique[unique.Count - 1] > 1e-10) unique.Add(t);
            var points = new List<D>();
            if (unique.Count == 0) return points;
            const double eps = 1e-7;

            void Emit(double theta, double r)
            {
                if (r <= 1e-9) return;
                var p = D.Unit(theta) * r;
                foreach (var v in verts)
                    if (D.Dist(v, p) < 1e-4)
                    {
                        p = v;
                        break;
                    }
                if (points.Count == 0 || D.Dist(points[points.Count - 1], p) > 1e-5) points.Add(p);
            }

            void Span(double t0, double t1, int depth)
            {
                if (depth > 40 || t1 - t0 < 1e-9) return;
                RayMax(D.Unit(t0 + eps), edges, out int e0);
                RayMax(D.Unit(t1 - eps), edges, out int e1);
                if (e0 < 0 || e1 < 0 || e0 == e1) return;
                var (p0, q0) = edges[e0];
                var (p1, q1) = edges[e1];
                double tm = 0.5 * (t0 + t1);
                if (Lines(p0, q0 - p0, p1, q1 - p1, out double s, out _))
                {
                    double th = (p0 + (q0 - p0) * s).Angle;
                    while (th < t0) th += 2 * Math.PI;
                    if (th > t0 + eps && th < t1 - eps) tm = th;
                }
                Span(t0, tm, depth + 1);
                Emit(tm, RayMax(D.Unit(tm), edges, out _));
                Span(tm, t1, depth + 1);
            }

            for (int k = 0; k < unique.Count; k++)
            {
                double th = unique[k];
                double next = k == unique.Count - 1 ? unique[0] + 2 * Math.PI : unique[k + 1];
                double before = RayMax(D.Unit(th - eps), edges, out _), after = RayMax(D.Unit(th + eps), edges, out _);
                if (Math.Abs(before - after) > 1e-4)
                {
                    Emit(th, before);
                    Emit(th, after);
                }
                else
                {
                    double at = RayMax(D.Unit(th), edges, out _);
                    Emit(th, at > 1e-9 ? at : after);
                }
                Span(th, next, 0);
            }
            while (points.Count > 1 && D.Dist(points[0], points[points.Count - 1]) < 1e-5) points.RemoveAt(points.Count - 1);
            return points;
        }

        /// <summary>Outline points closer than this are one point (a millimetre: below what any mesh shows, above float
        /// rounding at airport distances, which would otherwise make a microscopic zigzag).</summary>
        const double MergeDistance = 1e-3;

        static List<D> Merge(List<D> points)
        {
            var result = new List<D>(points.Count);
            foreach (var p in points)
                if (result.Count == 0 || D.Dist(result[result.Count - 1], p) > MergeDistance) result.Add(p);
            while (result.Count > 1 && D.Dist(result[0], result[result.Count - 1]) <= MergeDistance) result.RemoveAt(result.Count - 1);
            return result;
        }

        static int Nearest(List<D> points, D p)
        {
            int best = -1;
            double bestD = 2 * MergeDistance;
            for (int i = 0; i < points.Count; i++)
            {
                double d = D.Dist(points[i], p);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
