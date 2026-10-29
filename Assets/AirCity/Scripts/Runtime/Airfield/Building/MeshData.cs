using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Airfield.Building
{
    /// <summary>Vertex and index lists for the airfield's generated meshes. Every channel the airfield shaders
    /// read is a float4 UV so one builder serves surface, paint, glow and fixture meshes.</summary>
    sealed class MeshData
    {
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector4> tangents = new List<Vector4>();
        public readonly List<Color> colors = new List<Color>();
        public readonly List<Vector4> uv0 = new List<Vector4>(), uv1 = new List<Vector4>(), uv2 = new List<Vector4>(), uv3 = new List<Vector4>();
        public readonly List<int>[] indices;
        /// <summary>Owner of each vertex (SurfaceState), from <see cref="owner"/> when it was added; -1 = none.</summary>
        public readonly List<int> owners = new List<int>();
        public int owner = -1;

        public MeshData(int submeshes = 1)
        {
            indices = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) indices[i] = new List<int>();
        }

        public int Count => positions.Count;
        public bool Empty => positions.Count == 0;

        public int Vertex(Vector3 position, Vector3 normal, Color color, Vector4 u0, Vector4 u1 = default,
            Vector4 u2 = default, Vector4 u3 = default, Vector4 tangent = default)
        {
            positions.Add(position);
            normals.Add(normal);
            colors.Add(color);
            uv0.Add(u0);
            uv1.Add(u1);
            uv2.Add(u2);
            uv3.Add(u3);
            tangents.Add(tangent);
            owners.Add(owner);
            return positions.Count - 1;
        }

        /// <summary>A triangle, clockwise seen from its front (Unity's front-face winding).</summary>
        public void Triangle(int submesh, int a, int b, int c)
        {
            var list = indices[submesh];
            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        /// <summary>Two triangles; corners clockwise seen from the front.</summary>
        public void Quad(int submesh, int a, int b, int c, int d)
        {
            Triangle(submesh, a, b, c);
            Triangle(submesh, a, c, d);
        }

        /// <summary>Write into a mesh (made if null). Channels the shader doesn't read can be left out.</summary>
        public Mesh Apply(Mesh mesh, string name, bool withTangents = false, int uvChannels = 2)
        {
            if (mesh == null) mesh = new Mesh { hideFlags = HideFlags.DontSave };
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            if (withTangents) mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            var uvs = new[] { uv0, uv1, uv2, uv3 };
            for (int i = 0; i < uvChannels; i++) mesh.SetUVs(i, uvs[i]);
            mesh.subMeshCount = indices.Length;
            for (int i = 0; i < indices.Length; i++) mesh.SetTriangles(indices[i], i, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>2D helpers on the ground plane: Vector2 (x, y) = world (x, z). Angles are counter-clockwise seen
    /// from above; Left / Right are relative to facing the direction.</summary>
    static class Geo
    {
        public static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        public static Vector3 X0Z(Vector2 v, float y = 0f) => new Vector3(v.x, y, v.y);
        public static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);
        public static Vector2 Right(Vector2 d) => new Vector2(d.y, -d.x);
        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public static float Angle(Vector2 d) => Mathf.Atan2(d.y, d.x);

        /// <summary>Counter-clockwise angle from a to b, in [0, 2 pi).</summary>
        public static float AngleCcw(Vector2 a, Vector2 b)
        {
            float t = Angle(b) - Angle(a);
            while (t < 0f) t += 2f * Mathf.PI;
            while (t >= 2f * Mathf.PI) t -= 2f * Mathf.PI;
            return t;
        }

        /// <summary>Where lines p + dp t and q + dq u cross (false if parallel).</summary>
        public static bool Lines(Vector2 p, Vector2 dp, Vector2 q, Vector2 dq, out float t, out float u)
        {
            float den = Cross(dp, dq);
            t = u = 0f;
            if (Mathf.Abs(den) < 1e-6f) return false;
            var w = q - p;
            t = Cross(w, dq) / den;
            u = Cross(w, dp) / den;
            return true;
        }

        /// <summary>Do segments a0-a1 and b0-b1 cross strictly inside both (ends excluded by margin metres)?</summary>
        public static bool Segments(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, float margin, out Vector2 at)
        {
            at = default;
            var da = a1 - a0;
            var db = b1 - b0;
            if (!Lines(a0, da, b0, db, out float t, out float u)) return false;
            float la = da.magnitude, lb = db.magnitude;
            if (t * la < margin || (1f - t) * la < margin || u * lb < margin || (1f - u) * lb < margin) return false;
            at = a0 + da * t;
            return true;
        }

        /// <summary>Closest point on a segment, and its parameter 0..1.</summary>
        public static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var d = b - a;
            float len2 = d.sqrMagnitude;
            t = len2 < 1e-9f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, d) / len2);
            return a + d * t;
        }

        /// <summary>Points along a circular arc around centre from one point to another (both included), the
        /// short way round, at most maxStep metres apart.</summary>
        public static void Arc(List<Vector2> into, Vector2 centre, Vector2 from, Vector2 to, float maxStep, bool includeFirst = true)
        {
            float r0 = (from - centre).magnitude, r1 = (to - centre).magnitude;
            float a0 = Angle(from - centre);
            float delta = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, Angle(to - centre) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(delta) * Mathf.Max(r0, r1) / Mathf.Max(maxStep, 0.01f)));
            for (int i = includeFirst ? 0 : 1; i <= n; i++)
            {
                float f = i / (float)n;
                float a = a0 + delta * f;
                float r = Mathf.Lerp(r0, r1, f);
                into.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }

        /// <summary>The arc of radius r tangent to two lines that meet at corner, running out along unit
        /// directions da and db (the corner is rounded off on the inside). Returns the tangent distance from
        /// the corner and the arc centre.</summary>
        public static float Fillet(Vector2 corner, Vector2 da, Vector2 db, float r, out Vector2 centre)
        {
            float theta = Mathf.Acos(Mathf.Clamp(Vector2.Dot(da, db), -1f, 1f));
            float half = Mathf.Max(theta * 0.5f, 1e-3f);
            var bisector = (da + db).normalized;
            centre = corner + bisector * (r / Mathf.Sin(half));
            return r / Mathf.Tan(half);
        }

        // ------------------------------------------------------------------ polygons (corners in order, either way round)

        /// <summary>The signed area (positive when the corners run counter-clockwise from above).</summary>
        public static float SignedArea(IList<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Count; i++) a += Cross(poly[i], poly[(i + 1) % poly.Count]);
            return a * 0.5f;
        }

        /// <summary>Is a point inside the polygon (crossing rule; a point on the outline may go either way)?</summary>
        public static bool Inside(IList<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x)) inside = !inside;
            }
            return inside;
        }

        /// <summary>How far a point is from the polygon's outline.</summary>
        public static float DistanceToOutline(IList<Vector2> poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
                best = Mathf.Min(best, Vector2.Distance(p, ClosestOnSegment(p, poly[i], poly[(i + 1) % poly.Count], out _)));
            return best;
        }

        /// <summary>Does the outline cross itself (two edges that aren't neighbours crossing or touching)?</summary>
        public static bool SelfIntersects(IList<Vector2> poly)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
                for (int k = i + 2; k < n; k++)
                {
                    if (i == 0 && k == n - 1) continue;   // neighbours round the end
                    if (SegmentsMeet(poly[i], poly[(i + 1) % n], poly[k], poly[(k + 1) % n])) return true;
                }
            return false;
        }

        /// <summary>Do two segments cross or touch (ends included)?</summary>
        public static bool SegmentsMeet(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            float d1 = Cross(b1 - b0, a0 - b0), d2 = Cross(b1 - b0, a1 - b0);
            float d3 = Cross(a1 - a0, b0 - a0), d4 = Cross(a1 - a0, b1 - a0);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return true;
            const float eps = 1e-4f;
            return (Mathf.Abs(d1) < eps && OnSegment(a0, b0, b1)) || (Mathf.Abs(d2) < eps && OnSegment(a1, b0, b1)) ||
                   (Mathf.Abs(d3) < eps && OnSegment(b0, a0, a1)) || (Mathf.Abs(d4) < eps && OnSegment(b1, a0, a1));
        }

        static bool OnSegment(Vector2 p, Vector2 a, Vector2 b) =>
            p.x >= Mathf.Min(a.x, b.x) - 1e-4f && p.x <= Mathf.Max(a.x, b.x) + 1e-4f &&
            p.y >= Mathf.Min(a.y, b.y) - 1e-4f && p.y <= Mathf.Max(a.y, b.y) + 1e-4f;

        /// <summary>Do two polygons overlap by more than `slack` metres? Polygons that only share (part of) an edge
        /// or a corner don't.</summary>
        public static bool Overlap(IList<Vector2> a, IList<Vector2> b, float slack = 0.05f)
        {
            for (int i = 0; i < a.Count; i++)
                for (int k = 0; k < b.Count; k++)
                    if (Segments(a[i], a[(i + 1) % a.Count], b[k], b[(k + 1) % b.Count], slack, out _)) return true;
            foreach (var p in a)
                if (Inside(b, p) && DistanceToOutline(b, p) > slack) return true;
            foreach (var p in b)
                if (Inside(a, p) && DistanceToOutline(a, p) > slack) return true;
            // identical or edge-sharing outlines with no corner strictly inside: compare a point well inside each
            return Inside(b, InteriorPoint(a)) && DistanceToOutline(b, InteriorPoint(a)) > slack;
        }

        /// <summary>A point inside a simple polygon (the centroid of its first ear).</summary>
        public static Vector2 InteriorPoint(IList<Vector2> poly)
        {
            var t = Triangulate(poly);
            if (t.Count == 0) return poly.Count > 0 ? poly[0] : Vector2.zero;
            return (poly[t[0].a] + poly[t[0].b] + poly[t[0].c]) / 3f;
        }

        /// <summary>
        /// Triangles filling a simple polygon (ear clipping), as corner indices, counter-clockwise from above. Corners
        /// in a straight line are skipped; an outline that crosses itself is filled only as far as it can be.
        /// </summary>
        public static List<(int a, int b, int c)> Triangulate(IList<Vector2> poly)
        {
            var tris = new List<(int, int, int)>();
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            if (SignedArea(poly) < 0f) idx.Reverse();
            while (idx.Count > 3)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count && !clipped; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    float turn = Cross(b - a, c - b);
                    if (Mathf.Abs(turn) < 1e-5f)
                    {
                        idx.RemoveAt(i);      // straight on (or doubling back): no triangle
                        clipped = true;
                        continue;
                    }
                    if (turn < 0f) continue;  // a reflex corner
                    bool ear = true;
                    foreach (int k in idx)
                    {
                        if (k == ia || k == ib || k == ic) continue;
                        var p = poly[k];
                        if (Cross(b - a, p - a) >= -1e-5f && Cross(c - b, p - b) >= -1e-5f && Cross(a - c, p - c) >= -1e-5f)
                        {
                            ear = false;
                            break;
                        }
                    }
                    if (!ear) continue;
                    tris.Add((ia, ib, ic));
                    idx.RemoveAt(i);
                    clipped = true;
                }
                if (!clipped) return tris;
            }
            if (idx.Count == 3 && Cross(poly[idx[1]] - poly[idx[0]], poly[idx[2]] - poly[idx[1]]) > 1e-5f)
                tris.Add((idx[0], idx[1], idx[2]));
            return tris;
        }
    }
}
