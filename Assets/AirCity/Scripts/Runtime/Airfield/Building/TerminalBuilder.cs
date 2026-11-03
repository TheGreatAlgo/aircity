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
    sealed partial class TerminalBuilder
    {
        public readonly MeshData mesh = new MeshData(1);

        const float Plinth = 0.6f;
        const float ParapetInset = 0.35f, Coping = 0.08f;
        /// <summary>Pieces turning less than this (degrees) share normals at their joint: an arc, not a corner.</summary>
        const float SmoothTurn = 20f;

        static readonly Color Fascia = new Color(0.88f, 0.89f, 0.88f).linear;
        static readonly Color Plant = new Color(0.66f, 0.67f, 0.66f).linear;

        /// <summary>
        /// The spandrel band over storey `f` (its glass stops at the bottom), as deep as the style's spandrel: at a floor,
        /// ending Terminal.BandBelowFloor under the floor line, so a boarding corridor arriving there sits on top of it
        /// and the next storey's glass starts level with the corridor; at the roof, under the roof line (the top is the caller's).
        /// </summary>
        static (float bottom, float top) SlabBand(Terminal t, int f, BuildingFacade face)
        {
            float slab = t.FloorElevation(f + 1), below = Mathf.Min(face.spandrel, t.StoreyHeight(f) * 0.45f);
            float top = f + 1 >= t.floors ? slab : slab - Terminal.BandBelowFloor;
            return (top - below, top);
        }

        public void Build(Terminal t)
        {
            if (t.exterior != null && t.exterior.roofShape != null && t.exterior.roofShape.MaxRise > 0f)
            {
                if (t.corners.Count < 3) return;
                // Dense roof cuts need building-local precision, even far from the airport origin.
                var origin = t.corners[0]; var local = t.Clone();
                local.corners = local.corners.ConvertAll(p => p - origin);
                int start = mesh.Count; BuildShaped(local);
                var offset = Geo.X0Z(origin);
                for (int i = start; i < mesh.Count; i++) mesh.positions[i] += offset;
                return;
            }
            var o = t.OutlineWithFaces(out var faces);
            int n = o.Count;
            if (n < 3 || t.EnvelopeProblem() != null) return;
            float top = t.RoofHeight, height = t.Height;
            var details = FacadeStyles.Geometry(t.exterior);
            var sections = new Dictionary<float, List<Vector2>>();
            List<Vector2> At(float y)
            {
                if (!sections.TryGetValue(y, out var result)) sections[y] = result = t.WallOutline(o, faces, y);
                return result;
            }
            float along = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var face = t.exterior.ForFace(faces[i]);
                float len = Vector2.Distance(o[i], o[j]);
                if (len < 1e-4f) continue;
                void Band(float y0, float y1, bool glass, Color color, float smoothness, int level)
                {
                    if (y1 <= y0 + 0.001f) return;
                    var bottom = At(y0); var upper = At(y1);
                    // Share normals only along a curve, never across an architectural corner.
                    Vector3 Normal(int edge)
                    {
                        int end = (edge + 1) % n;
                        return Vector3.Cross(Geo.X0Z(upper[edge] - bottom[edge], y1 - y0), Geo.X0Z(bottom[end] - bottom[edge])).normalized;
                    }
                    var normal = Normal(i); var previous = Normal((i + n - 1) % n); var next = Normal(j);
                    var na = Vector3.Angle(previous, normal) < SmoothTurn ? (previous + normal).normalized : normal;
                    var nb = Vector3.Angle(next, normal) < SmoothTurn ? (next + normal).normalized : normal;
                    var uv = new Vector4(level, face.mullionSpacing * 2f, faces[i], 0f);
                    int v0 = mesh.Vertex(Geo.X0Z(bottom[i], y0), na, color, new Vector4(glass ? face.glazing : 0f, smoothness, along, y0), uv);
                    int v1 = mesh.Vertex(Geo.X0Z(bottom[j], y0), nb, color, new Vector4(glass ? face.glazing : 0f, smoothness, along + len, y0), uv);
                    int v2 = mesh.Vertex(Geo.X0Z(upper[j], y1), nb, color, new Vector4(glass ? face.glazing : 0f, smoothness, along + len, y1), uv);
                    int v3 = mesh.Vertex(Geo.X0Z(upper[i], y1), na, color, new Vector4(glass ? face.glazing : 0f, smoothness, along, y1), uv);
                    mesh.Quad(0, v0, v3, v2, v1);
                }
                Band(0f, Plinth, false, face.cladding.linear * 0.375f, 0.15f, 0);
                float y = Plinth;
                for (int f = 0; f < t.floors; f++)
                {
                    var (bandBottom, bandTop) = SlabBand(t, f, face);
                    float glassTop = Mathf.Max(y, bandBottom);
                    Band(y, glassTop, face.glazing > 0f, face.glazing > 0f ? face.glassTint.linear : face.cladding.linear, face.glazing > 0f ? 0.9f : 0.45f, f);
                    float end = f + 1 < t.floors ? bandTop : height;
                    Band(glassTop, end, false, face.cladding.linear, 0.45f, f);
                    y = end;
                }
                if (face.glazing > 0f)
                {
                    float y0 = Plinth, y1 = top - Mathf.Min(face.spandrel, t.StoreyHeight(t.floors - 1) * 0.45f);
                    int count = Mathf.Max(1, Mathf.RoundToInt(len / face.mullionSpacing));
                    for (int k = 0; k < count; k++)
                    {
                        float u = (float)k / count;
                        var p0 = Vector2.Lerp(At(y0)[i], At(y0)[j], u);
                        var p1 = Vector2.Lerp(At(y1)[i], At(y1)[j], u);
                        var d = (o[j] - o[i]).normalized;
                        SlopedFin(p0, p1, d, Geo.Right(d), y0, y1, face.frames.linear, details.frameWidth, details.frameDepth);
                    }
                }
                along += len;
            }
            // Space expressed columns along original faces, so tessellated arcs keep the same rhythm.
            if (details.pierWidth > 0f)
            {
                for (int edge = 0; edge < t.EdgeCount; edge++)
                {
                    var segments = new List<int>(); float length = 0f;
                    for (int i = 0; i < n; i++) if (faces[i] == edge)
                    { segments.Add(i); length += Vector2.Distance(o[i], o[(i + 1) % n]); }
                    if (length < 2f) continue;
                    Vector2 Point(float station, float y, out Vector2 direction)
                    {
                        foreach (int i in segments)
                        {
                            int j = (i + 1) % n; float len = Vector2.Distance(o[i], o[j]);
                            if (station <= len || i == segments[segments.Count - 1])
                            {
                                direction = (o[j] - o[i]).normalized;
                                return Vector2.Lerp(At(y)[i], At(y)[j], len > 0f ? station / len : 0f);
                            }
                            station -= len;
                        }
                        direction = Vector2.right; return Vector2.zero;
                    }
                    int count = Mathf.Max(1, Mathf.RoundToInt(length / details.pierSpacing));
                    float bay = length / count;
                    for (int k = 0; k < count; k++)
                    {
                        float station = (k + 0.5f) * bay;
                        var foot = Point(station, 0f, out var d) + Geo.Right(d) * t.exterior.columnGap;
                        var color = t.exterior.ForFace(edge).cladding.linear;
                        float pierTop = height - 0.25f;
                        if (details.forked)
                        {
                            float arm = Mathf.Min(1.4f, bay * 0.18f);
                            foreach (float offset in new[] { -arm, arm })
                                SlopedFin(foot, Point(station + offset, pierTop, out _) + Geo.Right(d) * t.exterior.columnGap, d, Geo.Right(d), 0f, pierTop, color, details.pierWidth, t.exterior.ColumnDepth);
                        }
                        else SlopedFin(foot, Point(station, pierTop, out _) + Geo.Right(d) * t.exterior.columnGap, d, Geo.Right(d), 0f, pierTop, color, details.pierWidth, t.exterior.ColumnDepth);
                    }
                }
            }
            var outer = At(height);
            var inner = Inset(outer, ParapetInset);
            var roofEdge = Inset(At(top), ParapetInset);
            Parapet(outer, inner, roofEdge, top, height, t.exterior.usesStyle ? t.exterior.facade.cladding.linear : Fascia, t.exterior.RoofOverhang);
            RoofFill(roofEdge, top, t.exterior.roof.linear);
            PlantRooms(t, roofEdge, top);
        }

        void SlopedFin(Vector2 bottom, Vector2 top, Vector2 d, Vector2 n, float y0, float y1, Color color, float width, float projection)
        {
            var w = d * (width * 0.5f); var depth = n * projection;
            var a = new[] { bottom + w, bottom - w, bottom - w + depth, bottom + w + depth };
            var b = new[] { top + w, top - w, top - w + depth, top + w + depth };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                var normal = Vector3.Cross(Geo.X0Z(b[i] - a[i], y1 - y0), Geo.X0Z(a[j] - a[i])).normalized;
                int v = mesh.Count;
                mesh.Vertex(Geo.X0Z(a[i], y0), normal, color, new Vector4(0f, 0.35f, 0f, y0));
                mesh.Vertex(Geo.X0Z(b[i], y1), normal, color, new Vector4(0f, 0.35f, 0f, y1));
                mesh.Vertex(Geo.X0Z(b[j], y1), normal, color, new Vector4(0f, 0.35f, 0f, y1));
                mesh.Vertex(Geo.X0Z(a[j], y0), normal, color, new Vector4(0f, 0.35f, 0f, y0));
                mesh.Quad(0, v, v + 1, v + 2, v + 3);
            }
            RoofQuad(Geo.X0Z(a[0], y0), Geo.X0Z(a[1], y0), Geo.X0Z(a[2], y0), Geo.X0Z(a[3], y0), color);
            RoofQuad(Geo.X0Z(b[0], y1), Geo.X0Z(b[3], y1), Geo.X0Z(b[2], y1), Geo.X0Z(b[1], y1), color);
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
            => Terminal.OffsetOutline(o, -d);

        /// <summary>The parapet: its inner face down to the roof and the coping cap across its top, overhanging a little.</summary>
        void Parapet(List<Vector2> outer, List<Vector2> inner, List<Vector2> roofEdge, float roof, float top, Color fascia, float overhang)
        {
            int n = outer.Count;
            var lip = Inset(outer, -Coping - overhang);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                // inner face, facing in: walk the inner outline backwards so its right is inward
                var ni = Vector3.Cross(Geo.X0Z(inner[j] - roofEdge[j], top - roof), Geo.X0Z(roofEdge[i] - roofEdge[j])).normalized;
                int v = mesh.Count;
                mesh.Vertex(Geo.X0Z(roofEdge[j], roof), ni, fascia, new Vector4(0f, 0.4f, 0f, roof));
                mesh.Vertex(Geo.X0Z(inner[j], top), ni, fascia, new Vector4(0f, 0.4f, 0f, top));
                mesh.Vertex(Geo.X0Z(inner[i], top), ni, fascia, new Vector4(0f, 0.4f, 0f, top));
                mesh.Vertex(Geo.X0Z(roofEdge[i], roof), ni, fascia, new Vector4(0f, 0.4f, 0f, roof));
                mesh.Quad(0, v, v + 1, v + 2, v + 3);
                // coping: a flat ring from the inner edge out past the wall face, and its thin outer edge
                int a = mesh.Vertex(Geo.X0Z(inner[i], top), Vector3.up, fascia, new Vector4(0f, 0.4f, 0f, top));
                int b = mesh.Vertex(Geo.X0Z(inner[j], top), Vector3.up, fascia, new Vector4(0f, 0.4f, 0f, top));
                int c = mesh.Vertex(Geo.X0Z(lip[j], top), Vector3.up, fascia, new Vector4(0f, 0.4f, 0f, top));
                int d = mesh.Vertex(Geo.X0Z(lip[i], top), Vector3.up, fascia, new Vector4(0f, 0.4f, 0f, top));
                mesh.Quad(0, a, b, c, d);
                var no = Geo.Right((lip[j] - lip[i]).normalized);
                Wall(lip[i], lip[j], no, no, top - 0.25f, top, 0f, Vector2.Distance(lip[i], lip[j]), 0f, fascia, 0.4f);
                RoofQuad(Geo.X0Z(outer[i], top - 0.25f), Geo.X0Z(lip[i], top - 0.25f), Geo.X0Z(lip[j], top - 0.25f), Geo.X0Z(outer[j], top - 0.25f), fascia);
            }
        }

        void RoofFill(List<Vector2> inner, float y, Color color)
        {
            int i0 = mesh.Count;
            foreach (var p in inner) mesh.Vertex(Geo.X0Z(p, y), Vector3.up, color, new Vector4(0f, 0.2f, 0f, y));
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
