using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>Builds airfield light glows (AirCity/AirfieldLightGlow) and the small hardware under them
    /// (AirCity/AirfieldFixture). Hardware shrinks away beyond its fade distance, where it would only shimmer.</summary>
    sealed class LightBuilder
    {
        public static readonly Color White = new Color(1f, 0.93f, 0.8f, 1f);
        public static readonly Color Yellow = new Color(1f, 0.72f, 0.18f, 1f);
        public static readonly Color Green = new Color(0.15f, 1f, 0.45f, 1f);
        public static readonly Color Red = new Color(1f, 0.1f, 0.06f, 1f);
        public static readonly Color Blue = new Color(0.15f, 0.35f, 1f, 1f);
        public static readonly Color Off = new Color(0f, 0f, 0f, 0f);

        static readonly Color Paint = new Color(0.85f, 0.65f, 0.08f);     // yellow fittings
        static readonly Color Metal = new Color(0.32f, 0.33f, 0.34f);
        static readonly Color White3 = new Color(0.85f, 0.85f, 0.83f);
        static readonly Color Orange = new Color(0.95f, 0.38f, 0.06f);

        public readonly MeshData glows = new MeshData();
        public readonly MeshData fixtures = new MeshData();
        public int LightCount => glows.Count / 4;

        /// <summary>A glow. axis = horizontal direction separating its two faces (zero: the same all round);
        /// colorBehind is seen from the -axis side, colorAhead from the +axis side (alpha 0 = dark).</summary>
        public void Light(Vector2 p, float height, Vector2 axis, Color colorBehind, Color colorAhead, float size,
            AirfieldLightType type = AirfieldLightType.Steady, float param = 0f)
        {
            var pos = Geo.X0Z(p, height);
            var normal = Geo.X0Z(axis);
            int b = glows.Count;
            var extra = new Vector4(size, (float)type, param, 0f);
            foreach (var c in GlowCorners)
                glows.Vertex(pos, normal, colorBehind, c, colorAhead, extra);
            glows.Triangle(0, b, b + 1, b + 2);
            glows.Triangle(0, b, b + 2, b + 3);
        }

        static readonly Vector4[] GlowCorners = { new Vector4(-1, -1), new Vector4(1, -1), new Vector4(1, 1), new Vector4(-1, 1) };

        // ------------------------------------------------------------------ hardware

        /// <summary>Elevated light: a yellow stem with a lamp head (taxiway / runway edge).</summary>
        public void Elevated(Vector2 p, float baseY, Color lamp)
        {
            var origin = Geo.X0Z(p, baseY);
            Prism(origin, p, 0.035f, baseY, baseY + 0.28f, 6, Paint, 0f, 260f);
            Prism(origin, p, 0.07f, baseY + 0.28f, baseY + 0.38f, 8, Lamp(lamp), 1f, 260f);
        }

        /// <summary>Inset light: a flush metal disc with a small lens (centreline, threshold).</summary>
        public void Inset(Vector2 p, float baseY, Color lamp)
        {
            var origin = Geo.X0Z(p, baseY);
            Prism(origin, p, 0.17f, baseY, baseY + 0.012f, 10, Metal, 0f, 160f);
            Prism(origin, p, 0.05f, baseY, baseY + 0.018f, 6, Lamp(lamp), 1f, 160f);
        }

        /// <summary>A PAPI unit: a white box on legs facing along axis.</summary>
        public void PapiUnit(Vector2 p, float baseY, Vector2 axis)
        {
            var origin = Geo.X0Z(p, baseY);
            Box(origin, p, axis, 0.35f, 0.5f, baseY + 0.45f, baseY + 0.95f, White3, 0f, 450f);
            Box(origin, p + axis * 0.5f, axis, 0.18f, 0.03f, baseY + 0.55f, baseY + 0.85f, Lamp(White), 1f, 450f);
            foreach (float s in new[] { -0.25f, 0.25f })
                Prism(origin, p + Geo.Right(axis) * s, 0.04f, baseY, baseY + 0.45f, 4, Metal, 0f, 450f);
        }

        /// <summary>An approach-light stand: a bar across axis on two legs, lamps along the bar.</summary>
        public void Barrette(Vector2 p, float baseY, Vector2 axis, float halfWidth, float height)
        {
            var origin = Geo.X0Z(p, baseY);
            var across = Geo.Right(axis);
            Box(origin, p, across, 0.06f, halfWidth, baseY + height - 0.08f, baseY + height, Metal, 0f, 700f);
            foreach (float s in new[] { -halfWidth * 0.7f, halfWidth * 0.7f })
                Prism(origin, p + across * s, 0.05f, baseY, baseY + height, 4, Metal, 0f, 700f);
        }

        /// <summary>Dirt-strip edge marker: an orange-and-white cone.</summary>
        public void Cone(Vector2 p, float baseY, bool orange)
        {
            var origin = Geo.X0Z(p, baseY);
            Frustum(origin, p, 0.24f, 0.12f, baseY, baseY + 0.22f, 10, orange ? Orange : White3, 600f);
            Frustum(origin, p, 0.12f, 0.03f, baseY + 0.22f, baseY + 0.45f, 10, orange ? White3 : Orange, 600f);
        }

        static Color Lamp(Color c) => Color.Lerp(c, Color.white, 0.35f);

        void Prism(Vector3 origin, Vector2 c, float r, float y0, float y1, int sides, Color color, float glass, float fade) =>
            Frustum(origin, c, r, r, y0, y1, sides, color, fade, glass);

        void Frustum(Vector3 origin, Vector2 c, float r0, float r1, float y0, float y1, int sides, Color color, float fade, float glass = 0f)
        {
            var g = new Vector4(glass, 0f);
            var o = new Vector4(origin.x, origin.y, origin.z, fade);
            float slope = (r0 - r1) / Mathf.Max(y1 - y0, 1e-4f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                var n = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), slope, Mathf.Sin((a0 + a1) * 0.5f)).normalized;
                int b = fixtures.Count;
                fixtures.Vertex(Geo.X0Z(c + d0 * r0, y0), n, color, g, o);
                fixtures.Vertex(Geo.X0Z(c + d0 * r1, y1), n, color, g, o);
                fixtures.Vertex(Geo.X0Z(c + d1 * r1, y1), n, color, g, o);
                fixtures.Vertex(Geo.X0Z(c + d1 * r0, y0), n, color, g, o);
                fixtures.Quad(0, b, b + 1, b + 2, b + 3);
                if (r1 > 0.005f)
                {
                    int t = fixtures.Count;
                    fixtures.Vertex(Geo.X0Z(c, y1), Vector3.up, color, g, o);
                    fixtures.Vertex(Geo.X0Z(c + d0 * r1, y1), Vector3.up, color, g, o);
                    fixtures.Vertex(Geo.X0Z(c + d1 * r1, y1), Vector3.up, color, g, o);
                    fixtures.Triangle(0, t, t + 2, t + 1);
                }
            }
        }

        void Box(Vector3 origin, Vector2 c, Vector2 along, float halfWidth, float halfLength, float y0, float y1,
            Color color, float glass, float fade)
        {
            along = along.normalized;
            var across = Geo.Right(along);
            var g = new Vector4(glass, 0f);
            var o = new Vector4(origin.x, origin.y, origin.z, fade);
            Vector2 P(float sx, float sy) => c + across * (sx * halfWidth) + along * (sy * halfLength);
            // four sides and the top, each its own quad so normals stay flat
            var faces = new (Vector2 a, Vector2 b, Vector3 n)[]
            {
                (P(-1, -1), P(1, -1), Geo.X0Z(-along)),
                (P(1, -1), P(1, 1), Geo.X0Z(across)),
                (P(1, 1), P(-1, 1), Geo.X0Z(along)),
                (P(-1, 1), P(-1, -1), Geo.X0Z(-across)),
            };
            foreach (var (a, b2, n) in faces)
            {
                int b = fixtures.Count;
                fixtures.Vertex(Geo.X0Z(a, y0), n, color, g, o);
                fixtures.Vertex(Geo.X0Z(a, y1), n, color, g, o);
                fixtures.Vertex(Geo.X0Z(b2, y1), n, color, g, o);
                fixtures.Vertex(Geo.X0Z(b2, y0), n, color, g, o);
                fixtures.Quad(0, b, b + 1, b + 2, b + 3);
            }
            int t = fixtures.Count;
            fixtures.Vertex(Geo.X0Z(P(-1, -1), y1), Vector3.up, color, g, o);
            fixtures.Vertex(Geo.X0Z(P(-1, 1), y1), Vector3.up, color, g, o);
            fixtures.Vertex(Geo.X0Z(P(1, 1), y1), Vector3.up, color, g, o);
            fixtures.Vertex(Geo.X0Z(P(1, -1), y1), Vector3.up, color, g, o);
            fixtures.Quad(0, t, t + 1, t + 2, t + 3);
        }

        public Mesh ApplyGlows(Mesh m, string name)
        {
            m = glows.Apply(m, name, false, 3);
            // the shader grows each quad to a few pixels: keep it from being culled at the edges
            var b = m.bounds;
            b.Expand(4f);
            m.bounds = b;
            return m;
        }

        public Mesh ApplyFixtures(Mesh m, string name) => fixtures.Apply(m, name, false, 2);
    }
}
