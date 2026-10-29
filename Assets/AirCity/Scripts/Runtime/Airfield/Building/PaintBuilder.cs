using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>The strip a paint mark lies on: its along / across coordinates (for touchdown rubber and where the
    /// ploughs have been) and, on runways, the runway length (AirfieldCommon.hlsl TouchdownRubber; 0 = no rubber).</summary>
    struct RunwayFrame
    {
        public Vector2 origin, axis;
        public float length;

        public static RunwayFrame None => new RunwayFrame { axis = Vector2.up };

        public static RunwayFrame Along(Vector2 origin, Vector2 axis, float length = 0f) =>
            new RunwayFrame { origin = origin, axis = axis, length = length };

        public Vector3 At(Vector2 p)
        {
            var d = p - origin;
            return new Vector3(Vector2.Dot(d, axis), Vector2.Dot(d, Geo.Right(axis)), length);
        }
    }

    /// <summary>Builds the paint mesh for AirCity/AirfieldPaint: boxes, strips along lines and curves, and
    /// runway designation glyphs. Positions are 2D ground points (x, z).</summary>
    sealed class PaintBuilder
    {
        public static readonly Color White = new Color(0.92f, 0.92f, 0.9f, 0.96f);
        public static readonly Color Yellow = new Color(0.96f, 0.72f, 0.1f, 0.96f);
        /// <summary>Apron safety lines: stand boundaries and the de-icing bays' vehicle limits.</summary>
        public static readonly Color Red = new Color(0.62f, 0.05f, 0.04f, 0.96f);

        // keep in sync with ArtSource/Airfield/textures.py (CHARS, CELL, PX_PER_M, ROWS)
        const string GlyphChars = "0123456789LCRABDEF";
        const float GlyphPxPerM = 24f, AtlasW = 1024f, AtlasH = 1024f, CellW = 128f, CellH = 256f;
        /// <summary>Height of a glyph at its atlas scale (metres): runway designators.</summary>
        public const float GlyphHeight = 9f;

        public readonly MeshData mesh = new MeshData();
        public RunwayFrame frame = RunwayFrame.None;
        /// <summary>Owner state written into the next marks; SurfaceState keeps it current.</summary>
        public SurfaceValues state = SurfaceValues.Initial;

        static float Y => AirfieldHeights.Paint;

        Vector4 Local(float x, float y) => new Vector4(x, y, state.snow, 0f);

        Vector4 Strip(Vector2 p)
        {
            var f = frame.At(p);
            return new Vector4(f.x, f.y, f.z, state.wear);
        }

        /// <summary>A rectangle centred on a point, its length along a direction.</summary>
        public void Box(Vector2 centre, Vector2 along, float halfWidth, float halfLength, Color color)
        {
            along = along.normalized;
            var across = Geo.Right(along);
            int b = mesh.Count;
            foreach (var (sx, sy) in Corners)
            {
                var p = centre + across * (sx * halfWidth) + along * (sy * halfLength);
                mesh.Vertex(Geo.X0Z(p, Y), Geo.X0Z(across * sx), color, Local(sx * halfWidth, sy * halfLength),
                    new Vector4(halfWidth, halfLength, 0f, 0f), default, Strip(p), Geo.X0Z(along * sy));
            }
            mesh.Quad(0, b, b + 1, b + 2, b + 3);
        }

        static readonly (float, float)[] Corners = { (-1f, -1f), (-1f, 1f), (1f, 1f), (1f, -1f) };

        /// <summary>A line of constant width through points (straight runs or sampled curves).</summary>
        public void Strip(IList<Vector2> points, float halfWidth, Color color)
        {
            int n = points.Count;
            if (n < 2) return;
            float total = 0f;
            for (int i = 1; i < n; i++) total += (points[i] - points[i - 1]).magnitude;
            if (total < 1e-3f) return;
            var box = new Vector4(halfWidth, total * 0.5f, total * 0.5f, 0f);
            float s = 0f;
            int b = mesh.Count;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) s += (points[i] - points[i - 1]).magnitude;
                var dir = (points[Mathf.Min(i + 1, n - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                var across = Geo.Right(dir);
                var grow = i == 0 ? -dir : i == n - 1 ? dir : Vector2.zero;
                for (int side = -1; side <= 1; side += 2)
                {
                    var p = points[i] + across * (side * halfWidth);
                    mesh.Vertex(Geo.X0Z(p, Y), Geo.X0Z(across * side), color, Local(side * halfWidth, s), box,
                        default, Strip(p), Geo.X0Z(grow));
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                int l0 = b + i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                mesh.Quad(0, l0, l1, r1, r0);
            }
        }

        /// <summary>Dashes along a straight line from a to b: dash, gap, dash... centred so both ends match.</summary>
        public void Dashes(Vector2 a, Vector2 b, float halfWidth, float dash, float gap, Color color)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < dash) return;
            d /= len;
            int count = Mathf.FloorToInt((len + gap) / (dash + gap));
            float used = count * (dash + gap) - gap;
            float s = (len - used) * 0.5f;
            for (int i = 0; i < count; i++, s += dash + gap)
                Box(a + d * (s + dash * 0.5f), d, halfWidth, dash * 0.5f, color);
        }

        /// <summary>Dashes along a polyline (a sampled curve), laid out like Dashes; each dash bends with the line.</summary>
        public void Dashes(IList<Vector2> points, float halfWidth, float dash, float gap, Color color)
        {
            int n = points.Count;
            if (n < 2) return;
            var at = new float[n];
            for (int i = 1; i < n; i++) at[i] = at[i - 1] + (points[i] - points[i - 1]).magnitude;
            float len = at[n - 1];
            if (len < dash) return;
            int count = Mathf.FloorToInt((len + gap) / (dash + gap));
            float s = (len - (count * (dash + gap) - gap)) * 0.5f;
            var piece = new List<Vector2>();
            Vector2 Point(float d)
            {
                int i = 1;
                while (i < n - 1 && at[i] < d) i++;
                float seg = at[i] - at[i - 1];
                return Vector2.Lerp(points[i - 1], points[i], seg > 1e-6f ? (d - at[i - 1]) / seg : 0f);
            }
            for (int k = 0; k < count; k++, s += dash + gap)
            {
                piece.Clear();
                piece.Add(Point(s));
                for (int i = 1; i < n - 1; i++)
                    if (at[i] > s && at[i] < s + dash) piece.Add(points[i]);
                piece.Add(Point(s + dash));
                Strip(piece, halfWidth, color);
            }
        }

        /// <summary>A character `height` metres tall (runway designators: 9 m), read facing "up".</summary>
        public void Glyph(Vector2 centre, Vector2 up, char c, Color color, float height = GlyphHeight)
        {
            int index = GlyphChars.IndexOf(c);
            if (index < 0) return;
            up = up.normalized;
            var right = Geo.Right(up);
            float scale = height / GlyphHeight;
            float hw = CellW / GlyphPxPerM * 0.5f * scale, hh = CellH / GlyphPxPerM * 0.5f * scale;
            int col = index % 8, row = index / 8;
            var atlas = new Vector4((col + 0.5f) * CellW / AtlasW, 1f - (row + 0.5f) * CellH / AtlasH,
                GlyphPxPerM / AtlasW / scale, GlyphPxPerM / AtlasH / scale);
            var box = new Vector4(hw, hh, 0f, 1f);
            int b = mesh.Count;
            foreach (var (sx, sy) in Corners)
            {
                var p = centre + right * (sx * hw) + up * (sy * hh);
                mesh.Vertex(Geo.X0Z(p, Y), Vector3.zero, color, Local(sx * hw, sy * hh), box, atlas, Strip(p));
            }
            mesh.Quad(0, b, b + 1, b + 2, b + 3);
        }

        /// <summary>Several glyphs side by side (e.g. "27"), centred, `pitch` metres apart.</summary>
        public void Text(Vector2 centre, Vector2 up, string text, float pitch, Color color, float height = GlyphHeight)
        {
            var right = Geo.Right(up.normalized);
            for (int i = 0; i < text.Length; i++)
                Glyph(centre + right * ((i - (text.Length - 1) * 0.5f) * pitch), up, text[i], color, height);
        }

        /// <summary>A straight line from a to b, its ends squared off `extend` metres beyond the points (so the
        /// sides of a box meet at the corners).</summary>
        public void Line(Vector2 a, Vector2 b, float halfWidth, Color color, float extend = 0f)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            Box((a + b) * 0.5f, d / len, halfWidth, len * 0.5f + extend, color);
        }

        public Mesh Apply(Mesh m, string name) => mesh.Apply(m, name, true, 4);
    }
}
