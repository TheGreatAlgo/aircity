using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// A fixed corridor's segments drawn with the model's Link (its section extruded 1 m along +z) bent to each one,
    /// rather than the Link stretched as a box. Neighbouring segments end on one mitre plane at their corner, so the
    /// corner reads as one corridor turning instead of two boxes overlapping. Sections stay upright (a slope shears the
    /// Link rather than pitching it), so every vertex on a corner's mitre has the corner's floor and both sides meet.
    /// The ends are cut square: the first a given distance back into the wall it leaves, the last into the rotunda's drum
    /// or the head. Needs the Link readable (VehicleAssetPostprocessor keeps it so); a custom model whose Link isn't
    /// falls back to stretching it (CanBend).
    /// </summary>
    public sealed class CorridorMesh
    {
        /// <summary>A mitre only while the corner turns less than this sharply (the cosine of half the turn's
        /// supplement); sharper, the segments overlap square as before, rather than reaching out into a long spike.</summary>
        const float MitreLimit = 0.35f;

        /// <summary>One segment's end: how far it reaches past its point along the segment (a square cut), and how
        /// much further each metre out to the right (a mitre).</summary>
        public struct Cut
        {
            public float reach, shear;
            public static Cut Square(float reach) => new Cut { reach = reach };
        }

        readonly Vector3[] vertices, normals;
        readonly Vector2[] uv;
        readonly Color[] colors;
        readonly int[][] triangles;
        readonly float z0, z1;

        static readonly Dictionary<Mesh, CorridorMesh> sources = new Dictionary<Mesh, CorridorMesh>();

        CorridorMesh(Mesh link)
        {
            vertices = link.vertices; normals = link.normals; uv = link.uv; colors = link.colors;
            triangles = new int[link.subMeshCount][];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = link.GetTriangles(i);
            z0 = link.bounds.min.z; z1 = link.bounds.max.z;
        }

        public static bool CanBend(Mesh link) => link != null && link.isReadable && link.vertexCount > 0 && link.bounds.size.z > 1e-3f;

        static CorridorMesh Of(Mesh link)
        {
            if (!sources.TryGetValue(link, out var source)) sources[link] = source = new CorridorMesh(link);
            return source;
        }

        /// <summary>
        /// Segment `i`'s cuts at its start and end: square into the wall/drum at the corridor's ends (`intoStart`,
        /// `intoEnd`), mitred where it meets its neighbours (square, half the width past the corner, where they turn
        /// too sharply for a mitre).
        /// </summary>
        public static (Cut start, Cut end) Cuts(IList<Vector3> points, int i, float width, float intoStart, float intoEnd)
        {
            var d = Direction(points, i);
            var start = i == 0 ? Cut.Square(intoStart) : Mitre(Direction(points, i - 1), d, d, width);
            var end = i + 2 >= points.Count ? Cut.Square(intoEnd) : Mitre(d, Direction(points, i + 1), d, width);
            return (start, end);
        }

        static Vector2 Direction(IList<Vector3> points, int i)
        {
            var d = Geo.XZ(points[i + 1] - points[i]);
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
        }

        /// <summary>The cut on the plane bisecting a corner from `before` to `after`, in the frame of the segment
        /// heading `own`: a point `x` out to the right on it lies `x * shear` along.</summary>
        static Cut Mitre(Vector2 before, Vector2 after, Vector2 own, float width)
        {
            var m = Geo.Right(before) + Geo.Right(after);
            if (m.sqrMagnitude < 1e-6f) return Cut.Square(width * 0.5f);
            m.Normalize();
            float across = Vector2.Dot(m, Geo.Right(own));
            if (across < MitreLimit) return Cut.Square(width * 0.5f);
            return new Cut { shear = Vector2.Dot(m, own) / across };
        }

        /// <summary>
        /// Fill `into` with the Link bent to one segment, in the segment's frame (on its start point's floor, turned to
        /// its heading on the ground, unscaled): `length` along the ground, rising `rise`, widened by `widen`.
        /// </summary>
        public static void Bend(Mesh link, Mesh into, float length, float rise, float widen, Cut start, Cut end)
        {
            var s = Of(link);
            var positions = new Vector3[s.vertices.Length];
            float span = Mathf.Max(s.z1 - s.z0, 1e-4f);
            for (int v = 0; v < positions.Length; v++)
            {
                var p = s.vertices[v];
                float t = Mathf.Clamp01((p.z - s.z0) / span), x = p.x * widen;
                float from = -start.reach + x * start.shear;
                float to = Mathf.Max(length + end.reach + x * end.shear, from + 0.01f);
                positions[v] = new Vector3(x, p.y + rise * t, Mathf.Lerp(from, to, t));
            }
            into.Clear();
            into.vertices = positions;
            if (s.normals.Length == positions.Length) into.normals = s.normals;
            if (s.uv.Length == positions.Length) into.uv = s.uv;
            if (s.colors.Length == positions.Length) into.colors = s.colors;
            into.subMeshCount = s.triangles.Length;
            for (int i = 0; i < s.triangles.Length; i++) into.SetTriangles(s.triangles[i], i, false);
            into.RecalculateBounds();
        }
    }
}
