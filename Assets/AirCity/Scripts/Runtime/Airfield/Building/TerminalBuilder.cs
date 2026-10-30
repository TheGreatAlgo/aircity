using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>
    /// Terminal buildings from their layout (Terminal): a dark concrete plinth, a glass curtain wall on every floor
    /// between light spandrel bands at the floor slabs, steel mullions at a steady pitch round the whole outline, a
    /// fascia and coping round the roof, a flat roof inside the parapet with a few plant rooms on it. Arcs are drawn
    /// as short straight pieces with smoothed normals, so a curved facade reads round. One submesh, the Building
    /// shader (uv0 = glass, smoothness, metres along the wall, metres up).
    /// </summary>
    sealed class TerminalBuilder
    {
        public readonly MeshData mesh = new MeshData(1);

        const float Plinth = 0.6f;
        /// <summary>The spandrel band at each floor slab: below the floor line and above it.</summary>
        const float SlabBelow = 1.1f, SlabAbove = 0.35f;
        const float MullionPitch = 3f, MullionWidth = 0.14f, MullionDepth = 0.22f;
        const float ParapetInset = 0.35f, Coping = 0.08f;
        /// <summary>Pieces turning less than this (degrees) share normals at their joint: an arc, not a corner.</summary>
        const float SmoothTurn = 20f;

        static readonly Color PlinthColor = new Color(0.30f, 0.30f, 0.29f).linear;
        static readonly Color Spandrel = new Color(0.80f, 0.81f, 0.80f).linear;
        static readonly Color Fascia = new Color(0.88f, 0.89f, 0.88f).linear;
        static readonly Color Mullion = new Color(0.42f, 0.44f, 0.46f).linear;
        static readonly Color Roof = new Color(0.58f, 0.58f, 0.56f).linear;
        static readonly Color Plant = new Color(0.66f, 0.67f, 0.66f).linear;
        static readonly Color Glass = Color.black;

        public void Build(Terminal t)
        {
            var o = t.Outline();
            int n = o.Count;
            if (n < 3) return;
            // each piece's outward normal; each corner's normal, shared by arc pieces, else its own per piece
            var pieceNormal = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var d = o[(i + 1) % n] - o[i];
                pieceNormal[i] = d.sqrMagnitude < 1e-8f ? Vector2.zero : Geo.Right(d.normalized);
            }
            var cornerNormal = new Vector2[n];
            var smooth = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var a = pieceNormal[(i + n - 1) % n];
                var b = pieceNormal[i];
                smooth[i] = Vector2.Angle(a, b) < SmoothTurn;
                cornerNormal[i] = (a + b).normalized;
            }

            float top = t.RoofHeight, height = t.Height;
            // the vertical bands up the wall: plinth, then glass and spandrel by floor, then the fascia to the coping
            var bands = new List<(float y0, float y1, bool glass, Color color, float smoothness)>
                { (0f, Plinth, false, PlinthColor, 0.15f) };
            float y = Plinth;
            for (int f = 1; f <= t.floors; f++)
            {
                float slab = f * Terminal.FloorHeight;
                float glassTop = slab - SlabBelow;
                bands.Add((y, glassTop, true, Glass, 0.9f));
                if (f < t.floors)
                {
                    bands.Add((glassTop, slab + SlabAbove, false, Spandrel, 0.45f));
                    y = slab + SlabAbove;
                }
                else bands.Add((glassTop, height, false, Fascia, 0.4f));
            }

            float along = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = o[i];
                var b = o[(i + 1) % n];
                float len = Vector2.Distance(a, b);
                if (len < 1e-4f) continue;
                var na = smooth[i] ? cornerNormal[i] : pieceNormal[i];
                var nb = smooth[(i + 1) % n] ? cornerNormal[(i + 1) % n] : pieceNormal[i];
                foreach (var (y0, y1, glass, color, sm) in bands)
                    Wall(a, b, na, nb, y0, y1, along, len, glass ? 1f : 0f, color, sm);
                along += len;
            }

            Mullions(o, pieceNormal, Plinth, top - SlabBelow);
            var inner = Inset(o, ParapetInset);
            Parapet(o, inner, top, height);
            RoofFill(inner, top);
            PlantRooms(t, inner, top);
        }

        void Wall(Vector2 a, Vector2 b, Vector2 na, Vector2 nb, float y0, float y1, float along, float len, float glass,
            Color color, float smoothness)
        {
            if (y1 - y0 < 0.01f) return;
            var n0 = Geo.X0Z(na);
            var n1 = Geo.X0Z(nb);
            int v0 = mesh.Vertex(Geo.X0Z(a, y0), n0, color, new Vector4(glass, smoothness, along, y0));
            int v1 = mesh.Vertex(Geo.X0Z(b, y0), n1, color, new Vector4(glass, smoothness, along + len, y0));
            int v2 = mesh.Vertex(Geo.X0Z(b, y1), n1, color, new Vector4(glass, smoothness, along + len, y1));
            int v3 = mesh.Vertex(Geo.X0Z(a, y1), n0, color, new Vector4(glass, smoothness, along, y1));
            // seen from outside (the normal's side), a -> b runs left to right: clockwise is v0, v3, v2, v1
            mesh.Quad(0, v0, v3, v2, v1);
        }

        /// <summary>Vertical steel fins every MullionPitch metres of the outline (the count fitted to each straight run so
        /// a fin stands at both ends of it), from the plinth up to the top floor's spandrel.</summary>
        void Mullions(List<Vector2> o, Vector2[] pieceNormal, float y0, float y1)
        {
            int n = o.Count;
            for (int i = 0; i < n; i++)
            {
                var a = o[i];
                var b = o[(i + 1) % n];
                float len = Vector2.Distance(a, b);
                if (len < 0.5f) continue;
                // arc pieces (short) get a fin at their start; straight runs one at each pitch
                int count = len < MullionPitch * 1.5f ? 1 : Mathf.Max(1, Mathf.RoundToInt(len / MullionPitch));
                var d = (b - a) / len;
                for (int k = 0; k < count; k++)
                {
                    var p = a + d * (len * k / count);
                    Fin(p, d, pieceNormal[i], y0, y1);
                }
            }
        }

        void Fin(Vector2 p, Vector2 d, Vector2 n, float y0, float y1)
        {
            var w = d * (MullionWidth * 0.5f);
            var outP = n * MullionDepth;
            // two sides and the face: the back sits on the glass
            Box3(p + w, p - w, p - w + outP, p + w + outP, y0, y1, Mullion, false);
        }

        /// <summary>Four ground corners (counter-clockwise from above) extruded from y0 to y1: sides, and the top if asked.</summary>
        void Box3(Vector2 c0, Vector2 c1, Vector2 c2, Vector2 c3, float y0, float y1, Color color, bool top)
        {
            var c = new[] { c0, c1, c2, c3 };
            for (int i = 0; i < 4; i++)
            {
                var a = c[i];
                var b = c[(i + 1) % 4];
                // edges run counter-clockwise, so outward is to the right
                var nrm = Geo.Right((a - b).sqrMagnitude < 1e-8f ? Vector2.right : (b - a).normalized);
                Wall(a, b, nrm, nrm, y0, y1, 0f, Vector2.Distance(a, b), 0f, color, 0.35f);
            }
            if (!top) return;
            int i0 = mesh.Count;
            foreach (var p in c) mesh.Vertex(Geo.X0Z(p, y1), Vector3.up, color, new Vector4(0f, 0.3f, 0f, y1));
            // corners run counter-clockwise from above; Unity's front face is clockwise seen from the front
            mesh.Triangle(0, i0, i0 + 2, i0 + 1);
            mesh.Triangle(0, i0, i0 + 3, i0 + 2);
        }

        /// <summary>The outline moved in by d (each corner along its mitre, limited so sharp corners don't spike).</summary>
        static List<Vector2> Inset(List<Vector2> o, float d)
        {
            int n = o.Count;
            var list = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                var prev = o[(i + n - 1) % n];
                var cur = o[i];
                var next = o[(i + 1) % n];
                var n0 = Geo.Right((cur - prev).normalized);
                var n1 = Geo.Right((next - cur).normalized);
                var m = (n0 + n1);
                float cos = Mathf.Max(Vector2.Dot(m.normalized, n1), 0.35f);
                list.Add(cur - m.normalized * (d / cos));
            }
            return list;
        }

        /// <summary>The parapet: its inner face down to the roof and the coping cap across its top, overhanging a little.</summary>
        void Parapet(List<Vector2> outer, List<Vector2> inner, float roof, float top)
        {
            int n = outer.Count;
            var lip = Inset(outer, -Coping);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                // inner face, facing in: walk the inner outline backwards so its right is inward
                var ni = -Geo.Right((inner[j] - inner[i]).normalized);
                Wall(inner[j], inner[i], ni, ni, roof, top, 0f, Vector2.Distance(inner[i], inner[j]), 0f, Fascia, 0.4f);
                // coping: a flat ring from the inner edge out past the wall face, and its thin outer edge
                int a = mesh.Vertex(Geo.X0Z(inner[i], top), Vector3.up, Fascia, new Vector4(0f, 0.4f, 0f, top));
                int b = mesh.Vertex(Geo.X0Z(inner[j], top), Vector3.up, Fascia, new Vector4(0f, 0.4f, 0f, top));
                int c = mesh.Vertex(Geo.X0Z(lip[j], top), Vector3.up, Fascia, new Vector4(0f, 0.4f, 0f, top));
                int d = mesh.Vertex(Geo.X0Z(lip[i], top), Vector3.up, Fascia, new Vector4(0f, 0.4f, 0f, top));
                mesh.Quad(0, a, b, c, d);
                var no = Geo.Right((lip[j] - lip[i]).normalized);
                Wall(lip[i], lip[j], no, no, top - 0.25f, top, 0f, Vector2.Distance(lip[i], lip[j]), 0f, Fascia, 0.4f);
            }
        }

        void RoofFill(List<Vector2> inner, float y)
        {
            int i0 = mesh.Count;
            foreach (var p in inner) mesh.Vertex(Geo.X0Z(p, y), Vector3.up, Roof, new Vector4(0f, 0.2f, 0f, y));
            // Triangulate gives counter-clockwise from above; Unity's front face is clockwise seen from above
            foreach (var (a, b, c) in Geo.Triangulate(inner)) mesh.Triangle(0, i0 + a, i0 + c, i0 + b);
        }

        /// <summary>A few rooftop plant rooms on a grid well inside the roof, one per ~1,500 m2 (deterministic).</summary>
        void PlantRooms(Terminal t, List<Vector2> inner, float roof)
        {
            float area = Mathf.Abs(Geo.SignedArea(inner));
            int want = Mathf.Clamp(Mathf.RoundToInt(area / 1500f), area > 300f ? 1 : 0, 12);
            if (want == 0) return;
            var min = inner[0];
            var max = inner[0];
            foreach (var p in inner)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            const float step = 23f, w = 7f, d = 4.5f, h = 2.4f;
            int placed = 0;
            for (float z = min.y + step * 0.5f; z < max.y && placed < want; z += step)
                for (float x = min.x + step * 0.5f + ((int)((z - min.y) / step) % 2) * step * 0.5f; x < max.x && placed < want; x += step)
                {
                    var c = new Vector2(x, z);
                    var corners = new[] { c + new Vector2(-w, -d) * 0.5f, c + new Vector2(w, -d) * 0.5f, c + new Vector2(w, d) * 0.5f, c + new Vector2(-w, d) * 0.5f };
                    bool inside = true;
                    foreach (var p in corners)
                        if (!Geo.Inside(inner, p) || Geo.DistanceToOutline(inner, p) < 4f) inside = false;
                    if (!inside) continue;
                    Box3(corners[0], corners[1], corners[2], corners[3], roof, roof + h, Plant, true);
                    placed++;
                }
        }
    }
}
