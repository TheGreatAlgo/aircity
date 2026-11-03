using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    sealed partial class TerminalBuilder
    {
        // Perimeter points intersect the slanted facade with a roof height field. Surface fills use their
        // final horizontal projection, so curved heights cannot invert the roof or the projecting canopy.
        readonly struct RoofVertex
        {
            public readonly Vector2 plan, source, lean;
            public RoofVertex(Vector2 plan, Vector2 source, Vector2 lean) { this.plan = plan; this.source = source; this.lean = lean; }
            public static RoofVertex Lerp(RoofVertex a, RoofVertex b, float u) =>
                new RoofVertex(Vector2.Lerp(a.plan, b.plan, u), Vector2.Lerp(a.source, b.source, u), Vector2.Lerp(a.lean, b.lean, u));
            public Vector3 Point(BuildingRoof.Profile profile, float height)
            { float y = profile.SurfaceHeight(plan, lean, height); return Geo.X0Z(plan + lean * y, y); }
        }

        void BuildShaped(Terminal t)
        {
            if (t.EnvelopeProblem() != null) return;
            var outline = t.OutlineWithFaces(out var originalFaces);
            var profile = t.exterior.roofShape.For(t);
            var o = profile.Sample(outline, originalFaces, out var faces);
            int n = o.Count;
            if (n < 3) return;
            var lean = t.WallOffsets(o, faces);
            var details = FacadeStyles.Geometry(t.exterior);
            Vector3 WallPoint(int i, float y) => Geo.X0Z(o[i] + lean[i] * y, y);
            float along = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var face = t.exterior.ForFace(faces[i]);
                float length = Vector2.Distance(o[i], o[j]);
                void Band(float a0, float b0, float a1, float b1, bool glass, Color color, int floor)
                {
                    if (a1 <= a0 + 0.001f && b1 <= b0 + 0.001f) return;
                    var uv = new Vector4(floor, face.mullionSpacing * 2f, faces[i], 0f);
                    RoofQuad(WallPoint(i, a0), WallPoint(i, a1), WallPoint(j, b1), WallPoint(j, b0), color,
                        glass ? face.glazing : 0f, glass ? 0.9f : 0.45f, uv, along, along + length);
                }
                Band(0f, 0f, Plinth, Plinth, false, face.cladding.linear * 0.375f, 0);
                float y = Plinth;
                for (int f = 0; f < t.floors; f++)
                {
                    bool last = f == t.floors - 1;
                    var (bandBottom, bandTop) = SlabBand(t, f, face);
                    float glassTop = Mathf.Max(y, bandBottom);
                    float a = last ? profile.SurfaceHeight(o[i], lean[i], glassTop) : glassTop;
                    float b = last ? profile.SurfaceHeight(o[j], lean[j], glassTop) : glassTop;
                    Band(y, y, a, b, face.glazing > 0f,
                        face.glazing > 0f ? face.glassTint.linear : face.cladding.linear, f);
                    float end = last ? t.RoofHeight + t.exterior.parapet : bandTop;
                    Band(a, b, last ? profile.SurfaceHeight(o[i], lean[i], end) : end,
                        last ? profile.SurfaceHeight(o[j], lean[j], end) : end, false, face.cladding.linear, f);
                    y = end;
                }
                along += length;
            }

            for (int edge = 0; edge < t.EdgeCount; edge++)
            {
                var segments = new List<int>(); float length = 0f;
                for (int i = 0; i < n; i++) if (faces[i] == edge)
                { segments.Add(i); length += Vector2.Distance(o[i], o[(i + 1) % n]); }
                if (length < 0.01f) continue;
                var face = t.exterior.ForFace(edge);
                void Sample(float station, out Vector2 ground, out Vector2 offset, out Vector2 direction)
                {
                    foreach (int i in segments)
                    {
                        int j = (i + 1) % n; float len = Vector2.Distance(o[i], o[j]);
                        if (station <= len || i == segments[segments.Count - 1])
                        {
                            float u = len > 0f ? station / len : 0f;
                            ground = Vector2.Lerp(o[i], o[j], u); offset = Vector2.Lerp(lean[i], lean[j], u);
                            direction = (o[j] - o[i]).normalized; return;
                        }
                        station -= len;
                    }
                    ground = offset = Vector2.zero; direction = Vector2.right;
                }
                void Fin(float station, float offset, float bottom, float top, float width, float depth, Color color, float gap = 0f)
                {
                    Sample(station, out var ground, out var shift, out var d);
                    Sample(station + offset, out var upper, out var upperShift, out _);
                    var standOff = Geo.Right(d) * gap;
                    top = profile.SurfaceHeight(upper + standOff, upperShift, top);
                    SlopedFin(ground + shift * bottom + standOff, upper + upperShift * top + standOff, d, Geo.Right(d), bottom, top, color, width, depth);
                }
                int mullions = Mathf.Max(1, Mathf.RoundToInt(length / face.mullionSpacing));
                if (face.glazing > 0f)
                    for (int k = 0; k < mullions; k++)
                        Fin(k * length / mullions, 0f, Plinth, t.RoofHeight - Mathf.Min(face.spandrel, t.StoreyHeight(t.floors - 1) * 0.45f),
                            details.frameWidth, details.frameDepth, face.frames.linear);
                if (details.pierWidth <= 0f || length < 2f) continue;
                int piers = Mathf.Max(1, Mathf.RoundToInt(length / details.pierSpacing)); float bay = length / piers;
                float pierTop = t.RoofHeight + t.exterior.parapet - 0.25f;
                for (int k = 0; k < piers; k++)
                {
                    float station = (k + 0.5f) * bay;
                    if (details.forked)
                    {
                        float arm = Mathf.Min(1.4f, bay * 0.18f);
                        Fin(station, -arm, 0f, pierTop, details.pierWidth, t.exterior.ColumnDepth, face.cladding.linear, t.exterior.columnGap);
                        Fin(station, arm, 0f, pierTop, details.pierWidth, t.exterior.ColumnDepth, face.cladding.linear, t.exterior.columnGap);
                    }
                    else Fin(station, 0f, 0f, pierTop, details.pierWidth, t.exterior.ColumnDepth, face.cladding.linear, t.exterior.columnGap);
                }
            }

            var inset = Inset(o, ParapetInset); var lip = Inset(o, -Coping - t.exterior.RoofOverhang);
            var roof = new List<RoofVertex>();
            Color fascia = t.exterior.usesStyle ? t.exterior.facade.cladding.linear : Fascia;
            float rim = t.RoofHeight + t.exterior.parapet;
            List<RoofVertex> Surface(params Vector3[] points)
            {
                var result = new List<RoofVertex>();
                foreach (var p in points) result.Add(new RoofVertex(Geo.XZ(p), Geo.XZ(p), Vector2.zero));
                return result;
            }
            for (int i = 0; i < n; i++) roof.Add(new RoofVertex(inset[i], o[i], lean[i]));
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a = roof[i]; var b = roof[j];
                var outerA = new RoofVertex(lip[i], o[i], lean[i]); var outerB = new RoofVertex(lip[j], o[j], lean[j]);
                RoofQuad(b.Point(profile, t.RoofHeight), b.Point(profile, rim), a.Point(profile, rim), a.Point(profile, t.RoofHeight), fascia);
                RoofSurface(Surface(a.Point(profile, rim), b.Point(profile, rim), outerB.Point(profile, rim), outerA.Point(profile, rim)), profile, rim, fascia, 0.4f);
                RoofQuad(outerA.Point(profile, rim - 0.25f), outerA.Point(profile, rim), outerB.Point(profile, rim), outerB.Point(profile, rim - 0.25f), fascia);
                var wallA = new RoofVertex(o[i], o[i], lean[i]); var wallB = new RoofVertex(o[j], o[j], lean[j]);
                RoofSurface(Surface(wallA.Point(profile, rim - 0.25f), outerA.Point(profile, rim - 0.25f), outerB.Point(profile, rim - 0.25f), wallB.Point(profile, rim - 0.25f)), profile, rim - 0.25f, fascia, 0.4f, true);
            }
            // Triangulate the final projected roof. Interpolating lean * curved height inside triangles of
            // the ground footprint can turn a perfectly valid roof inside out at rounded, leaning corners.
            var fill = new List<RoofVertex>();
            foreach (var vertex in roof)
            {
                var p = Geo.XZ(vertex.Point(profile, t.RoofHeight));
                fill.Add(new RoofVertex(p, p, Vector2.zero));
            }
            RoofSurface(fill, profile, t.RoofHeight, t.exterior.roof.linear, 0.2f);
        }

        void RoofSurface(List<RoofVertex> fill, BuildingRoof.Profile profile, float height, Color color, float smoothness, bool underside = false)
        {
            var projected = new List<Vector2>();
            foreach (var vertex in fill) projected.Add(vertex.plan);
            foreach (var (a, b, c) in Geo.Triangulate(projected))
            {
                var triangle = new List<RoofVertex> { fill[a], fill[b], fill[c] };
                float min = Mathf.Min(profile.Station(fill[a].source), Mathf.Min(profile.Station(fill[b].source), profile.Station(fill[c].source)));
                float max = Mathf.Max(profile.Station(fill[a].source), Mathf.Max(profile.Station(fill[b].source), profile.Station(fill[c].source)));
                for (int k = Mathf.FloorToInt(min / profile.step); k <= Mathf.FloorToInt(max / profile.step); k++)
                {
                    var strip = ClipRoof(ClipRoof(triangle, profile, k * profile.step, true), profile, (k + 1) * profile.step, false);
                    for (int v = 1; v + 1 < strip.Count; v++)
                    {
                        var p0 = strip[0].Point(profile, height); var p1 = strip[v + 1].Point(profile, height); var p2 = strip[v].Point(profile, height);
                        var cross = Vector3.Cross(p1 - p0, p2 - p0);
                        // Float clipping can leave zero-area slivers where a cut meets an existing vertex.
                        if (cross.y <= 0.00001f) continue;
                        int group = profile.SmoothingGroup((profile.Station(strip[0].source) + profile.Station(strip[v].source) + profile.Station(strip[v + 1].source)) / 3f);
                        int start = mesh.Count;
                        foreach (var p in new[] { p0, p1, p2 })
                        {
                            mesh.Vertex(p, profile.Normal(Geo.XZ(p), group) * (underside ? -1f : 1f), color, new Vector4(0f, smoothness, p.x, p.z));
                        }
                        mesh.Triangle(0, start, start + (underside ? 2 : 1), start + (underside ? 1 : 2));
                    }
                }
            }
        }

        static List<RoofVertex> ClipRoof(List<RoofVertex> polygon, BuildingRoof.Profile profile, float station, bool above)
        {
            var result = new List<RoofVertex>();
            if (polygon.Count == 0) return result;
            var a = polygon[polygon.Count - 1]; float da = profile.Station(a.source) - station;
            foreach (var b in polygon)
            {
                float db = profile.Station(b.source) - station;
                bool insideA = above ? da >= 0f : da <= 0f, insideB = above ? db >= 0f : db <= 0f;
                if (insideA != insideB) result.Add(RoofVertex.Lerp(a, b, da / (da - db)));
                if (insideB) result.Add(b);
                a = b; da = db;
            }
            return result;
        }

        void RoofQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float glass = 0f, float smoothness = 0.4f,
            Vector4 level = default, float alongA = 0f, float alongB = 0f)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude < 0.5f) return;
            int start = mesh.Count;
            mesh.Vertex(a, normal, color, new Vector4(glass, smoothness, alongA, a.y), level);
            mesh.Vertex(b, normal, color, new Vector4(glass, smoothness, alongA, b.y), level);
            mesh.Vertex(c, normal, color, new Vector4(glass, smoothness, alongB, c.y), level);
            mesh.Vertex(d, normal, color, new Vector4(glass, smoothness, alongB, d.y), level);
            mesh.Quad(0, start, start + 1, start + 2, start + 3);
        }
    }
}
