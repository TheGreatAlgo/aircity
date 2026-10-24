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
    }
}
