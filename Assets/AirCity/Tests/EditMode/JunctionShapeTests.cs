using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    /// <summary>The shared junction solver at awkward angles, widths and section lengths: the outline must be a simple
    /// polygon a fan from the node fills without folding, no two sections' strips may overlap, and the pavement must
    /// leave no hole where the sections meet.</summary>
    public class JunctionShapeTests
    {
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return a + ab * t;
        }

        static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        /// <summary>Arms sorted counter-clockwise, each reaching as far as the builders let a junction reach when the
        /// section's far end needs nothing (JunctionShape.Reach).</summary>
        static List<JunctionArm> Arms(params (float degrees, float width, float length)[] spec)
        {
            var arms = spec.Select(s => new JunctionArm(Dir(s.degrees), s.width * 0.5f, s.length, s.length))
                .OrderBy(a => Mathf.Atan2(a.dir.y, a.dir.x)).ToList();
            var need = JunctionShape.Needs(arms);
            for (int i = 0; i < arms.Count; i++) arms[i].reach = JunctionShape.Reach(arms[i].length, need[i], 0f);
            return arms;
        }

        /// <summary>Checks the shape against the plain geometry by sampling: every point of a section's own strip is
        /// covered by no other strip, every point the sections cover is paved, and the outline is star-shaped about the
        /// node. Returns a description of the first failure, or null.</summary>
        static string Problem(List<JunctionArm> arms, List<Vector2> outline)
        {
            var c = Vector2.zero;
            // folds: the outline turning back round the node by more than float rounding (1e-5 rad: 1.5 mm at 150 m)
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 p0 = outline[i] - c, p1 = outline[(i + 1) % outline.Count] - c;
                float scale = p0.magnitude * p1.magnitude;
                if (scale > 1e-6f && Cross(p0, p1) / scale < -1e-5f) return $"outline folds back at {outline[i]}";
            }
            // samples on a grid around the node
            float extent = arms.Max(a => a.length);
            float step = Mathf.Max(0.25f, extent / 160f);
            for (float x = -extent; x <= extent; x += step)
                for (float y = -extent; y <= extent; y += step)
                {
                    var p = new Vector2(x, y);
                    int strips = 0;
                    bool section = false;
                    foreach (var a in arms)
                    {
                        float s = Vector2.Dot(p, a.dir), off = Mathf.Abs(Cross(a.dir, p));
                        if (off > a.halfWidth - 0.02f || s > a.length - 0.02f) continue;
                        if (s > 0.02f) section = true;
                        if (s > a.cut + 0.02f) strips++;
                    }
                    if (strips > 1) return $"strips overlap at {p}";
                    if (section && strips == 0 && (outline.Count < 3 || !Inside(outline, p, -0.02f)))
                        return $"hole at {p}";
                    if (strips == 1 && outline.Count >= 3 && Inside(outline, p, 0.05f))
                        return $"junction overlaps a strip at {p}";
                }
            return null;
        }

        /// <summary>Is p inside the polygon, at least `margin` from its edges (negative: up to that far outside)?</summary>
        static bool Inside(List<Vector2> poly, Vector2 p, float margin)
        {
            bool inside = false;
            float nearest = float.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                nearest = Mathf.Min(nearest, Vector2.Distance(Closest(p, a, b), p));
            }
            return margin >= 0f ? inside && nearest >= margin : inside || nearest <= -margin;
        }

        static readonly object[] Awkward =
        {
            new object[] { "road T", new[] { (0f, 7f, 80f), (180f, 7f, 80f), (90f, 7f, 80f) } },
            new object[] { "road joins at 30°", new[] { (0f, 7f, 80f), (180f, 7f, 80f), (30f, 7f, 80f) } },
            new object[] { "road joins at 15°", new[] { (0f, 7f, 120f), (180f, 7f, 120f), (15f, 7f, 120f) } },
            new object[] { "roads cross at 25°", new[] { (0f, 7f, 80f), (180f, 7f, 80f), (25f, 7f, 80f), (205f, 7f, 80f) } },
            new object[] { "sharp bend on 20 m sections", new[] { (0f, 7f, 20f), (40f, 7f, 20f) } },
            new object[] { "sharp bend on 15 m sections", new[] { (0f, 7f, 15f), (50f, 7f, 15f) } },
            new object[] { "road across a 23 m taxiway at 20°", new[] { (0f, 23f, 120f), (180f, 23f, 120f), (20f, 7f, 80f), (200f, 7f, 80f) } },
            new object[] { "road meets a 23 m taxiway at 30°", new[] { (0f, 23f, 120f), (180f, 23f, 120f), (30f, 7f, 80f) } },
            new object[] { "slight bend", new[] { (0f, 7f, 50f), (184f, 7f, 50f) } },
            new object[] { "narrow carries on from wide", new[] { (0f, 23f, 50f), (180f, 7f, 50f) } },
            new object[] { "narrow leaves wide at 150°", new[] { (0f, 23f, 80f), (150f, 7f, 80f) } },
            new object[] { "five roads", new[] { (0f, 7f, 60f), (25f, 7f, 60f), (120f, 7f, 60f), (200f, 7f, 60f), (280f, 7f, 60f) } },
            new object[] { "taxiways join at 30°", new[] { (0f, 23f, 300f), (180f, 23f, 300f), (30f, 23f, 300f) } },
            new object[] { "highway bends 10°", new[] { (0f, 15.5f, 90f), (190f, 15.5f, 90f) } },
        };

        [TestCaseSource(nameof(Awkward))]
        public void AwkwardJunction_IsCleanPavement(string name, (float, float, float)[] spec)
        {
            foreach (float radius in new[] { 0f, 8f, 30f })
            {
                var arms = Arms(spec);
                var outline = JunctionShape.Solve(Vector2.zero, arms, (i, k) => radius, out bool ok);
                Assert.IsTrue(ok, $"{name}: every section is long enough");
                Assert.IsNull(Problem(arms, outline), $"{name}, kerb radius {radius} m");
            }
        }

        [Test]
        public void RandomJunctions_AreCleanPavement()
        {
            var rng = new System.Random(20261113);
            float[] widths = { 6.5f, 7f, 15f, 15.5f, 23f };
            float[] lengths = { 8f, 12f, 20f, 40f, 80f, 200f };
            int tried = 0;
            for (int n = 0; n < 400; n++)
            {
                int count = new[] { 2, 2, 3, 3, 3, 4, 4, 5 }[rng.Next(8)];
                var spec = new List<(float, float, float)>();
                float widest = 0f;
                for (int k = 0; k < count; k++)
                {
                    float w = widths[rng.Next(widths.Length)];
                    widest = Mathf.Max(widest, w);
                    spec.Add(((float)rng.NextDouble() * 360f, w, lengths[rng.Next(lengths.Length)] * (0.8f + 0.4f * (float)rng.NextDouble())));
                }
                // sections at least 2.5 widths long, roads at least 15° apart (what the build tools allow)
                for (int k = 0; k < spec.Count; k++) spec[k] = (spec[k].Item1, spec[k].Item2, Mathf.Max(spec[k].Item3, 2.5f * widest));
                var arms = Arms(spec.ToArray());
                bool tooClose = false;
                for (int k = 0; k < arms.Count; k++)
                    tooClose |= Vector2.Angle(arms[k].dir, arms[(k + 1) % arms.Count].dir) < 15f;
                if (tooClose) continue;
                tried++;
                float radius = new[] { 0f, 4f, 8f, 30f }[rng.Next(4)];
                var outline = JunctionShape.Solve(Vector2.zero, arms, (i, k) => radius, out bool ok);
                string spec0 = string.Join("; ", spec.Select(s => $"{s.Item1:0.#}° {s.Item2} m x {s.Item3:0.#} m"));
                Assert.IsTrue(ok, $"fits: {spec0}");
                Assert.IsNull(Problem(arms, outline), $"{spec0}, kerb radius {radius} m");
            }
            Assert.Greater(tried, 150);
        }

        [Test]
        public void TooShortSection_IsReported()
        {
            // a 2 m section shared with a junction at its far end that needs 3 m of it: both ends can't fit
            var arms = Arms((0f, 7f, 2f), (180f, 7f, 80f), (90f, 7f, 80f));
            var need = JunctionShape.Needs(arms);
            var shortArm = arms.FindIndex(a => a.length < 3f);
            Assert.Greater(need[shortArm], 0f, "the T needs some of the short section");
            arms[shortArm].reach = JunctionShape.Reach(2f, need[shortArm], 3f);
            JunctionShape.Solve(Vector2.zero, arms, (i, k) => 8f, out bool ok);
            Assert.IsFalse(ok);
        }

        [Test]
        public void ShallowJoin_KerbsMeetWhereTheRoadsDo()
        {
            // two 7 m roads 20° apart: the inner kerbs cross 3.5 / tan(10°) = 19.85 m out, so neither strip may start
            // before that, and the outline reaches at least that far between them
            var arms = Arms((0f, 7f, 200f), (20f, 7f, 200f), (180f, 7f, 200f));
            var outline = JunctionShape.Solve(Vector2.zero, arms, (i, k) => 0f, out bool ok);
            Assert.IsTrue(ok);
            float meet = 3.5f / Mathf.Tan(10f * Mathf.Deg2Rad);
            var east = arms.Single(a => Vector2.Angle(a.dir, Dir(0f)) < 1f);
            var shallow = arms.Single(a => Vector2.Angle(a.dir, Dir(20f)) < 1f);
            Assert.GreaterOrEqual(east.cut, meet - 0.01f);
            Assert.GreaterOrEqual(shallow.cut, meet - 0.01f);
            Assert.Greater(JunctionShape.Radial(Vector2.zero, outline, 10f), meet * 0.99f);
        }

        // ------------------------------------------------------------------ the builders using it

        [Test]
        public void TaxiwayNetwork_OddAngleRoads_BuildWithoutDownFacingTriangles()
        {
            var go = new GameObject("Junction test airport");
            try
            {
                go.SetActive(false);
                var airport = go.AddComponent<Airport>();
                airport.groundSize = 0f;
                go.SetActive(true);
                var net = airport.Taxiways;
                int Node(float x, float z) => net.AddNode(new Vector3(x, 0f, z));
                void Road(int a, int b) => net.Connect(a, b, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
                // a shallow join, a slanted crossing over a taxiway, a short sharp zigzag
                int c = Node(0f, 0f);
                Road(Node(-80f, 0f), c);
                Road(c, Node(80f, 0f));
                Road(c, Node(80f * Mathf.Cos(20f * Mathf.Deg2Rad), 80f * Mathf.Sin(20f * Mathf.Deg2Rad)));
                net.Connect(Node(200f, 0f), Node(440f, 0f), 23f, PavementSurface.Concrete);
                Road(Node(300f - 80f * Mathf.Cos(20f * Mathf.Deg2Rad), -80f * Mathf.Sin(20f * Mathf.Deg2Rad)),
                    Node(300f + 80f * Mathf.Cos(20f * Mathf.Deg2Rad), 80f * Mathf.Sin(20f * Mathf.Deg2Rad)));
                int z0 = Node(600f, 0f), z1 = Node(620f, 0f), z2 = Node(620f - 15.3f, 12.9f), z3 = Node(625f, 12.9f);
                Road(z0, z1);
                Road(z1, z2);
                Road(z2, z3);
                net.Rebuild();
                var mesh = net.SurfaceMesh;
                Assert.IsNotNull(mesh);
                var v = mesh.vertices;
                int down = 0;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var t = mesh.GetTriangles(s);
                    for (int i = 0; i < t.Length; i += 3)
                        if (Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y < -1e-3f) down++;
                }
                Assert.AreEqual(0, down, "down-facing (folded) triangles");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void JoinProblem_RefusesShallowJoinsAndCrowdedCrossings()
        {
            var go = new GameObject("Join test airport");
            try
            {
                go.SetActive(false);
                var airport = go.AddComponent<Airport>();
                airport.groundSize = 0f;
                go.SetActive(true);
                var net = airport.Taxiways;
                int Node(float x, float z) => net.AddNode(new Vector3(x, 0f, z));
                const float w = RunwayRules.RoadWidth;
                // a road west-east through a T at the origin, the T's stem going north
                int west = Node(-100f, 0f), c = Node(0f, 0f), east = Node(100f, 0f), north = Node(0f, 100f);
                net.Connect(west, c, w, PavementSurface.Asphalt, false, RoadClass.Service);
                net.Connect(c, east, w, PavementSurface.Asphalt, false, RoadClass.Service);
                net.Connect(c, north, w, PavementSurface.Asphalt, false, RoadClass.Service);
                var service = RoadClass.Service;
                var from = new Vector2(80f, -60f);
                // joining the T at 8° off the eastern arm: too shallow
                var shallow = new Vector2(100f * Mathf.Cos(8f * Mathf.Deg2Rad), 100f * Mathf.Sin(8f * Mathf.Deg2Rad));
                StringAssert.Contains("8°", net.JoinProblem(shallow, -1, null, Vector2.zero, c, null, w, service));
                // joining it at 40°: fine
                var fine = new Vector2(100f * Mathf.Cos(40f * Mathf.Deg2Rad), 100f * Mathf.Sin(40f * Mathf.Deg2Rad));
                Assert.IsNull(net.JoinProblem(fine, -1, null, Vector2.zero, c, null, w, service));
                // crossing the west-east road 2 m east of the T: the 2 m section between can't hold both junctions
                StringAssert.Contains("too short", net.JoinProblem(new Vector2(2f, -50f), -1, null, new Vector2(2f, 50f), -1, null, w, service));
                // crossing it 40 m east of the T: fine
                Assert.IsNull(net.JoinProblem(new Vector2(40f, -50f), -1, null, new Vector2(40f, 50f), -1, null, w, service));
                // ending on the eastern section 2 m from the T (the click splits it there): refused
                var eastEdge = net.edges.Find(e => (e.a == c && e.b == east) || (e.a == east && e.b == c));
                StringAssert.Contains("too short", net.JoinProblem(from, -1, null, new Vector2(2f, 0f), -1, eastEdge, w, service));
                // a free-standing line: nothing to check
                Assert.IsNull(net.JoinProblem(new Vector2(300f, 300f), -1, null, new Vector2(400f, 300f), -1, null, w, service));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PublicRoads_BendsAreCurvesAndShallowJoinsFit()
        {
            var graph = new PublicRoadNetwork();
            int Node(float x, float z) => graph.AddNode(new Vector3(x, 0f, z));
            // a bend of the same road: a curve, its strips meeting the arc (no junction polygon)
            int b0 = Node(-70f, 0f), b1 = Node(0f, 0f), b2 = Node(35f, 60.6f);
            graph.AddSegment(b0, b1, PublicRoadStyle.Rural, RoadStructure.Ground);
            graph.AddSegment(b1, b2, PublicRoadStyle.Rural, RoadStructure.Ground);
            // a 20° join on a separate road
            int j0 = Node(300f, 0f), j1 = Node(380f, 0f), j2 = Node(460f, 0f);
            graph.AddSegment(j0, j1, PublicRoadStyle.Urban, RoadStructure.Ground);
            graph.AddSegment(j1, j2, PublicRoadStyle.Urban, RoadStructure.Ground);
            graph.AddSegment(j1, Node(380f + 80f * Mathf.Cos(20f * Mathf.Deg2Rad), 80f * Mathf.Sin(20f * Mathf.Deg2Rad)), PublicRoadStyle.Urban, RoadStructure.Ground);
            var geometry = new PublicRoadGeometry(graph, null, null, _ => 0f);
            try
            {
                Assert.IsEmpty(geometry.MisfitJunctions);
                var v = geometry.pavement.vertices;
                // the bend (a 60° turn; rural radius 60 m wants tangent points 34.6 m out, clamped to 45% of the 70 m
                // sections, 31.5 m, so the radius shrinks to 31.5 x tan 60° = 54.6 m) is an arc: pavement runs along the
                // arc's middle and none is left at the old corner
                float cut = 0.45f * 70f, radius = cut * Mathf.Tan(60f * Mathf.Deg2Rad);
                var arcCentre = new Vector2(-cut, radius);
                var arcMiddle = arcCentre + (Vector2.zero - arcCentre).normalized * radius;
                Assert.IsTrue(v.Any(p => Vector2.Distance(new Vector2(p.x, p.z), arcMiddle) < 2f), "pavement along the arc");
                Assert.IsFalse(v.Any(p => Vector2.Distance(new Vector2(p.x, p.z), Vector2.zero) < 1.5f), "no pavement left at the corner");
                Assert.IsTrue(geometry.links.Count > graph.segments.Count, "the arc is part of the road for grading and ground cover");
                int down = 0;
                var t = geometry.pavement.triangles;
                for (int i = 0; i < t.Length; i += 3)
                    if (Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y < -1e-3f) down++;
                Assert.AreEqual(0, down, "down-facing triangles");
            }
            finally
            {
                foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures, geometry.kerbMesh, geometry.medianGrass }) Object.DestroyImmediate(mesh);
            }
        }
    }
}
