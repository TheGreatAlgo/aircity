using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>Builds pavement meshes for AirCity/AirfieldSurface: strips in a local (across, along) frame,
    /// fan-filled polygons, and ragged dirt / grass fringes. One submesh per material. Vertex colour carries the
    /// owner's state (r wear, g snow cm, b grass cm) and the edge coverage (a); SurfaceState keeps the state
    /// current, along with the ploughs / mowers at work (uv4).</summary>
    sealed class SurfaceBuilder
    {
        public readonly MeshData mesh;
        /// <summary>Width of the ragged fringe around dirt and grass surfaces (metres).</summary>
        public const float Fringe = 2.5f;
        /// <summary>Owner state written into the next vertices.</summary>
        public SurfaceValues state = SurfaceValues.Initial;

        public SurfaceBuilder(int submeshes) => mesh = new MeshData(submeshes);

        Color State(float coverage = 1f) => new Color(state.wear, state.snow, state.grass, coverage);

        /// <summary>A rectangle in a strip frame: origin + axis * s + right * x for s in [s0, s1], x in [x0, x1],
        /// split every maxStep metres along. uv0 = (x, s), uv1 = (kind, length). With a fringe, a ring of vertices
        /// with coverage 0 is added outside the rectangle on the sides (and ends) asked for;
        /// uv0.x can be shifted (uvShiftX is subtracted) and the fringe left off either side.</summary>
        public void Rect(int submesh, Vector2 origin, Vector2 axis, float s0, float s1, float x0, float x1, float y,
            AirfieldKind kind, float length, float fringe = 0f, bool fringeEnds = true, float maxStep = 60f,
            float uvShiftX = 0f, bool fringeX0 = true, bool fringeX1 = true)
        {
            var right = Geo.Right(axis);
            var xs = new List<(float x, float a)>();
            var ss = new List<(float s, float a)>();
            if (fringe > 0f && fringeX0) xs.Add((x0 - fringe, 0f));
            xs.Add((x0, 1f));
            xs.Add((x1, 1f));
            if (fringe > 0f && fringeX1) xs.Add((x1 + fringe, 0f));
            if (fringe > 0f && fringeEnds) ss.Add((s0 - fringe, 0f));
            int steps = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / maxStep));
            for (int i = 0; i <= steps; i++) ss.Add((Mathf.Lerp(s0, s1, i / (float)steps), 1f));
            if (fringe > 0f && fringeEnds) ss.Add((s1 + fringe, 0f));

            int b = mesh.Count;
            foreach (var (s, sa) in ss)
                foreach (var (x, xa) in xs)
                {
                    var p = origin + axis * s + right * x;
                    mesh.Vertex(Geo.X0Z(p, y), Vector3.up, State(Mathf.Min(sa, xa)), new Vector4(x - uvShiftX, s),
                        new Vector4((float)kind, length));
                }
            int cols = xs.Count;
            for (int j = 0; j < ss.Count - 1; j++)
                for (int i = 0; i < cols - 1; i++)
                {
                    int a = b + j * cols + i;
                    // (x, s) -> (x, s+1) -> (x+1, s+1) -> (x+1, s): clockwise from above for a right-handed frame
                    mesh.Quad(submesh, a, a + cols, a + cols + 1, a + 1);
                }
        }

        /// <summary>
        /// The verges either side of a strip (AirfieldKind.Verge): grass from `gap` to `outer` metres from the
        /// centreline, for s in [s0, s1], with a ragged fringe along the outside and the ends. uv0.x is measured
        /// outwards from the pavement edge (negative on the left), so the mowers' pass pattern (StripWork) works
        /// on the verges as on a strip whose pavement has been taken out.
        /// </summary>
        public void Verges(int submesh, Vector2 origin, Vector2 axis, float s0, float s1, float gap, float outer, float y)
        {
            if (outer - gap < 0.5f || s1 - s0 < 0.5f) return;
            Rect(submesh, origin, axis, s0, s1, -outer, -gap, y, AirfieldKind.Verge, 0f, Fringe, true, 60f, -gap, true, false);
            Rect(submesh, origin, axis, s0, s1, gap, outer, y, AirfieldKind.Verge, 0f, Fringe, true, 60f, gap, false, true);
        }

        /// <summary>A strip of constant half width along a polyline (a sampled curve). along starts at s0. dir0 / dir1
        /// are the directions at the ends, if known: a sampled arc's end chord is turned half a step off its tangent,
        /// so its end wouldn't sit square against the straight it continues (a wedge gap on the outside).</summary>
        public void Strip(int submesh, IList<Vector2> points, float halfWidth, float y, AirfieldKind kind,
            float s0 = 0f, float fringe = 0f, Vector2 dir0 = default, Vector2 dir1 = default)
        {
            int n = points.Count;
            if (n < 2) return;
            var xs = fringe > 0f
                ? new[] { (-halfWidth - fringe, 0f), (-halfWidth, 1f), (halfWidth, 1f), (halfWidth + fringe, 0f) }
                : new[] { (-halfWidth, 1f), (halfWidth, 1f) };
            int b = mesh.Count;
            float s = s0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) s += (points[i] - points[i - 1]).magnitude;
                var dir = i == 0 && dir0 != default ? dir0.normalized
                    : i == n - 1 && dir1 != default ? dir1.normalized
                    : (points[Mathf.Min(i + 1, n - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                var right = Geo.Right(dir);
                foreach (var (x, a) in xs)
                    mesh.Vertex(Geo.X0Z(points[i] + right * x, y), Vector3.up, State(a), new Vector4(x, s),
                        new Vector4((float)kind, 0f));
            }
            int cols = xs.Length;
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < cols - 1; i++)
                {
                    int a = b + j * cols + i;
                    mesh.Quad(submesh, a, a + cols, a + cols + 1, a + 1);
                }
        }

        /// <summary>A polygon (counter-clockwise from above) filled as a fan from centre. Junction outlines can
        /// double back along an edge line (where one side of a taxiway needs a longer fillet than the other); those
        /// fan triangles are flipped to face up, which still covers exactly the polygon. uv0 = world (x, z). With a
        /// fringe, a coverage-0 ring is added outside.</summary>
        public void Fan(int submesh, Vector2 centre, IList<Vector2> outline, float y, AirfieldKind kind, float fringe = 0f)
        {
            int n = outline.Count;
            if (n < 3) return;
            int c = mesh.Count;
            mesh.Vertex(Geo.X0Z(centre, y), Vector3.up, State(), new Vector4(centre.x, centre.y), new Vector4((float)kind, 0f));
            int b = mesh.Count;
            foreach (var p in outline)
                mesh.Vertex(Geo.X0Z(p, y), Vector3.up, State(), new Vector4(p.x, p.y), new Vector4((float)kind, 0f));
            for (int i = 0; i < n; i++)
            {
                var p0 = outline[i];
                var p1 = outline[(i + 1) % n];
                float cross = Geo.Cross(p0 - centre, p1 - centre);
                if (Mathf.Abs(cross) < 1e-4f) continue;   // degenerate sliver
                if (cross > 0f) mesh.Triangle(submesh, c, b + (i + 1) % n, b + i);
                else mesh.Triangle(submesh, c, b + i, b + (i + 1) % n);
            }
            if (fringe <= 0f) return;
            int r = mesh.Count;
            for (int i = 0; i < n; i++)
            {
                var prev = outline[(i + n - 1) % n];
                var next = outline[(i + 1) % n];
                var p = outline[i];
                var n0 = (p - prev).sqrMagnitude > 1e-8f ? Geo.Right((p - prev).normalized) : Vector2.zero;
                var n1 = (next - p).sqrMagnitude > 1e-8f ? Geo.Right((next - p).normalized) : Vector2.zero;
                var outward = (n0 + n1).sqrMagnitude > 1e-8f ? (n0 + n1).normalized : n0;
                var q = p + outward * fringe;
                mesh.Vertex(Geo.X0Z(q, y), Vector3.up, State(0f), new Vector4(q.x, q.y), new Vector4((float)kind, 0f));
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                UpTriangle(submesh, b + i, b + j, r + j);
                UpTriangle(submesh, b + i, r + j, r + i);
            }
        }

        /// <summary>A triangle wound to face up whichever way its corners come (fringe quads where an outline
        /// doubles back would otherwise face down).</summary>
        void UpTriangle(int submesh, int a, int b, int c)
        {
            var p = mesh.positions;
            float y = Vector3.Cross(p[b] - p[a], p[c] - p[a]).y;
            if (Mathf.Abs(y) < 1e-6f) return;
            if (y > 0f) mesh.Triangle(submesh, a, b, c);
            else mesh.Triangle(submesh, a, c, b);
        }
    }

    /// <summary>
    /// Wear, snow, grass and the work on them change all the time (every movement, every minute of snowfall, the
    /// ploughs sweeping), so they aren't baked into the geometry. Each surface and paint vertex remembers its
    /// owner (a runway, a taxiway edge, a junction), and Apply rewrites only the state channels from the owners'
    /// current SurfaceValues: surface colour r / g / b and uv4, paint uv3.w / uv0.z and uv4. A few thousand
    /// vertices, cheap enough to run every frame.
    /// </summary>
    sealed class SurfaceState
    {
        Mesh surface, paint;
        int[] surfaceOwners = Array.Empty<int>(), paintOwners = Array.Empty<int>();
        int ownerCount;
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector4> surfaceWork = new List<Vector4>();
        readonly List<Vector4> paintUv0 = new List<Vector4>(), paintUv3 = new List<Vector4>(), paintWork = new List<Vector4>();
        SurfaceValues[] states = Array.Empty<SurfaceValues>();

        /// <summary>Remember each vertex's owner (paint may be null: verges have none).</summary>
        public void Capture(Mesh surfaceMesh, MeshData s, Mesh paintMesh, MeshData p)
        {
            surface = surfaceMesh;
            paint = paintMesh;
            surfaceOwners = s.owners.ToArray();
            paintOwners = p != null ? p.owners.ToArray() : Array.Empty<int>();
            ownerCount = 0;
            foreach (int o in surfaceOwners) ownerCount = Mathf.Max(ownerCount, o + 1);
            foreach (int o in paintOwners) ownerCount = Mathf.Max(ownerCount, o + 1);
            colors.Clear();
            colors.AddRange(s.colors);
            surfaceWork.Clear();
            for (int i = 0; i < s.Count; i++) surfaceWork.Add(StripWork.Idle);
            paintUv0.Clear();
            paintUv3.Clear();
            paintWork.Clear();
            if (p == null) return;
            paintUv0.AddRange(p.uv0);
            paintUv3.AddRange(p.uv3);
            for (int i = 0; i < p.Count; i++) paintWork.Add(StripWork.Idle);
        }

        /// <summary>Write each owner's values into its vertices.</summary>
        public void Apply(Func<int, SurfaceValues> stateOf)
        {
            if (states.Length < ownerCount) states = new SurfaceValues[ownerCount];
            for (int o = 0; o < ownerCount; o++) states[o] = stateOf(o);
            if (surface != null && colors.Count > 0 && surfaceOwners.Length == colors.Count)
            {
                for (int i = 0; i < colors.Count; i++)
                {
                    int o = surfaceOwners[i];
                    if (o < 0) continue;
                    var c = colors[i];
                    c.r = states[o].wear;
                    c.g = states[o].snow;
                    c.b = states[o].grass;
                    colors[i] = c;
                    surfaceWork[i] = states[o].work;
                }
                surface.SetColors(colors);
                surface.SetUVs(4, surfaceWork);
            }
            if (paint != null && paintUv0.Count > 0 && paintOwners.Length == paintUv0.Count)
            {
                for (int i = 0; i < paintUv0.Count; i++)
                {
                    int o = paintOwners[i];
                    if (o < 0) continue;
                    var u0 = paintUv0[i];
                    u0.z = states[o].snow;
                    paintUv0[i] = u0;
                    var u3 = paintUv3[i];
                    u3.w = states[o].wear;
                    paintUv3[i] = u3;
                    paintWork[i] = states[o].work;
                }
                paint.SetUVs(0, paintUv0);
                paint.SetUVs(3, paintUv3);
                paint.SetUVs(4, paintWork);
            }
        }
    }
}
