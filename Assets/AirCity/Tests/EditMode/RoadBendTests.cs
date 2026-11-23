using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.EditorTools.Airfield;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    /// <summary>Bends on public roads and airside service roads: the dashes keep their rhythm through them, and a
    /// short dead-end section doesn't squeeze them tight.</summary>
    public class RoadBendTests
    {
        readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        /// <summary>The dashes of a paint mesh (those of its marks about as long as a dash, chosen by `mark`), in order
        /// along the road: each as its two ends. A dash painted in two pieces (across the join between a section and
        /// its bend) counts as one.</summary>
        internal static List<(Vector2 a, Vector2 b)> DashesOf(Mesh mesh, System.Func<Vector2, Color, bool> mark)
        {
            var positions = mesh.vertices; var colours = mesh.colors; var triangles = mesh.triangles;
            var parent = Enumerable.Range(0, positions.Length).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < triangles.Length; i += 3)
            { parent[Find(triangles[i + 1])] = Find(triangles[i]); parent[Find(triangles[i + 2])] = Find(triangles[i]); }
            var pieces = new List<(Vector2 a, Vector2 b)>();
            foreach (var group in Enumerable.Range(0, positions.Length).GroupBy(Find))
            {
                var points = group.Select(i => new Vector2(positions[i].x, positions[i].z)).ToList();
                if (!group.All(i => mark(new Vector2(positions[i].x, positions[i].z), colours[i]))) continue;
                // the two ends: the middles of the first and last pair of corners along the mark
                var far = points.OrderByDescending(p => Vector2.Distance(p, points[0])).First();
                var other = points.OrderByDescending(p => Vector2.Distance(p, far)).First();
                var axis = (far - other).normalized;
                if (Vector2.Distance(far, other) > 4.5f) continue;
                var along = points.OrderBy(p => Vector2.Dot(p - other, axis)).ToList();
                pieces.Add(((along[0] + along[1]) * .5f, (along[along.Count - 1] + along[along.Count - 2]) * .5f));
            }
            // join the pieces that meet end to end
            for (bool joined = true; joined;)
            {
                joined = false;
                for (int i = 0; i < pieces.Count && !joined; i++)
                    for (int k = i + 1; k < pieces.Count && !joined; k++)
                        foreach (var (p, q, r, s) in new[] { (pieces[i].a, pieces[i].b, pieces[k].a, pieces[k].b), (pieces[i].a, pieces[i].b, pieces[k].b, pieces[k].a),
                                     (pieces[i].b, pieces[i].a, pieces[k].a, pieces[k].b), (pieces[i].b, pieces[i].a, pieces[k].b, pieces[k].a) })
                            if (Vector2.Distance(q, r) < .12f)
                            { pieces[i] = (p, s); pieces.RemoveAt(k); joined = true; break; }
            }
            // shorter marks are not dashes (a stub's edge lines); a dash cut short shows up as a stretched gap
            pieces.RemoveAll(d => Vector2.Distance(d.a, d.b) < 2.5f);
            if (pieces.Count < 2) return pieces;
            // in order along the road: from one end of it, always on to the nearest dash
            Vector2 Middle((Vector2 a, Vector2 b) d) => (d.a + d.b) * .5f;
            var first = pieces.OrderByDescending(d => pieces.Max(e => Vector2.Distance(Middle(d), Middle(e)))).First();
            var ordered = new List<(Vector2 a, Vector2 b)> { first }; pieces.Remove(first);
            while (pieces.Count > 0)
            {
                var last = Middle(ordered[ordered.Count - 1]);
                var next = pieces.OrderBy(d => Vector2.Distance(Middle(d), last)).First();
                ordered.Add(next); pieces.Remove(next);
            }
            return ordered;
        }

        static List<float> Gaps(List<(Vector2 a, Vector2 b)> dashes)
        {
            var gaps = new List<float>();
            for (int i = 1; i < dashes.Count; i++)
                gaps.Add(Mathf.Min(Mathf.Min(Vector2.Distance(dashes[i - 1].a, dashes[i].a), Vector2.Distance(dashes[i - 1].a, dashes[i].b)),
                    Mathf.Min(Vector2.Distance(dashes[i - 1].b, dashes[i].a), Vector2.Distance(dashes[i - 1].b, dashes[i].b))));
            return gaps;
        }

        internal static void AssertEvenDashes(List<(Vector2 a, Vector2 b)> dashes, int atLeast, string what, float nominal = 6f)
        {
            Assert.GreaterOrEqual(dashes.Count, atLeast, what + ": dashes");
            foreach (var d in dashes) Assert.AreEqual(3f, Vector2.Distance(d.a, d.b), .15f, what + ": every dash is whole");
            var gaps = Gaps(dashes);
            string all = " (" + nominal + " m nominal): " + string.Join(" ", gaps.Select(g => g.ToString("0.0")));
            Assert.Greater(gaps.Min(), nominal - Mathf.Max(1f, nominal * .25f), what + ": no gap is squeezed" + all);
            Assert.Less(gaps.Max(), nominal * 1.25f, what + ": no gap is stretched" + all);
        }

        internal static bool Yellow(Color c) => c.r > .9f && c.b < .3f;

        static PublicRoadGeometry Road(PublicRoadStyle style, bool entrance, params Vector2[] points)
        {
            var graph = new PublicRoadNetwork(); int previous = 0;
            for (int i = 0; i < points.Length; i++)
            {
                int node = graph.AddNode(new Vector3(points[i].x, 0f, points[i].y), i == 0 && entrance ? FlyingSchoolLayout.EntranceId("test") : null);
                if (i > 0) Assert.IsTrue(graph.Connect(previous, node, style, RoadStructure.Ground, out string why), why);
                previous = node;
            }
            return new PublicRoadGeometry(graph, null, null, _ => 0f);
        }

        internal static void Destroy(PublicRoadGeometry geometry)
        { foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures, geometry.kerbMesh, geometry.medianGrass }) Object.DestroyImmediate(mesh); }

        [Test]
        public void PublicRoad_DashesKeepTheirRhythmThroughBends()
        {
            var layouts = new (string name, Vector2[] points)[]
            {
                ("a right angle", new[] { new Vector2(80f, 0f), Vector2.zero, new Vector2(0f, -80f) }),
                ("a 40 degree bend", new[] { new Vector2(80f, 0f), Vector2.zero, Dir(220f) * 80f }),
                ("a driveway's stub", new[] { Vector2.zero, new Vector2(-12f, 0f), new Vector2(-12f, 0f) + Dir(235f) * 80f }),
                ("bends close together", new[] { new Vector2(90f, 0f), new Vector2(40f, 0f), new Vector2(15f, -25f), new Vector2(-15f, -25f), new Vector2(-35f, -55f), new Vector2(-90f, -55f) }),
            };
            foreach (var (name, points) in layouts)
            {
                var geometry = Road(PublicRoadStyle.AirportAccess, name.Contains("driveway"), points);
                try { AssertEvenDashes(DashesOf(geometry.markings, (p, c) => Yellow(c)), 6, name, 9f); }
                finally { Destroy(geometry); }
            }
        }

        [Test]
        public void PublicRoad_BendTakesAShortDeadEndSectionAndStaysRound()
        {
            // 12 m out of a car park's mouth, then 55 degrees round: the stub has nothing to share with at its far end
            var bend = new Vector2(-12f, 0f);
            var geometry = Road(PublicRoadStyle.AirportAccess, true, Vector2.zero, bend, bend + Dir(235f) * 80f);
            try
            {
                // the bend's centreline passes its corner at radius x (sec(turn / 2) - 1): over 15 m of radius here
                float apex = geometry.links.Min(l => Vector2.Distance(Geo.ClosestOnSegment(bend, l.a, l.b, out _), bend));
                Assert.Greater(apex / (1f / Mathf.Cos(27.5f * Mathf.Deg2Rad) - 1f), 15f, "the bend's radius");
                // and its pavement edge turns at most 6 degrees from one piece to the next
                var arc = geometry.links.Where(l => Vector2.Distance(l.a, l.b) < 2.01f && Vector2.Distance((l.a + l.b) * .5f, bend) < 12f).ToList();
                Assert.Greater(arc.Count, 8, "the bend is sampled finely");
                for (int i = 1; i < arc.Count; i++)
                    Assert.Less(Vector2.Angle(arc[i - 1].b - arc[i - 1].a, arc[i].b - arc[i].a), 6f, "no visible corners round the bend");
            }
            finally { Destroy(geometry); }
        }

        [Test]
        public void ServiceRoad_OutlineFollowsTheBendInsteadOfRunningToTheCorner()
        {
            AirfieldSetup.CreateMaterials();
            var go = new GameObject("Road outline test"); go.SetActive(false); spawned.Add(go);
            var airport = go.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport); airport.groundSize = 0f; go.SetActive(true);
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(-80f, 0f, 0f)), c = net.AddNode(Vector3.zero), b = net.AddNode(new Vector3(0f, 0f, -80f));
            net.Connect(a, c, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            net.Connect(c, b, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            net.Rebuild();
            float half = RunwayRules.RoadWidth * .5f;
            foreach (var edge in net.edges)
            {
                var loop = new List<Vector3>();
                Assert.IsTrue(airport.SectionOutline(edge, 0f, net.Path(edge).Length, half, loop));
                // the loop runs up one side and back down the other: opposite points pair up across the centreline.
                // The bend cuts the corner off, so the centreline goes round it and never out to the corner node
                // (an outline run straight to the node would end with its centre on it)
                var centre = Enumerable.Range(0, loop.Count / 2).Select(k => (loop[k] + loop[loop.Count - 1 - k]) * .5f)
                    .Select(p => new Vector2(p.x, p.z)).ToList();
                var nearest = centre.OrderBy(p => p.magnitude).First();
                Assert.Greater(nearest.magnitude, 5f, "the outline never runs out to the corner");
                Assert.Less(Vector2.Distance(nearest, new Vector2(-1f, -1f).normalized * nearest.magnitude), 1.5f,
                    "its nearest point to the corner is on the bend's diagonal: it reaches the middle of the arc");
                // a piece in the middle of a section is just that stretch
                var piece = new List<Vector3>();
                Assert.IsTrue(airport.SectionOutline(edge, 30f, 50f, half, piece));
                float span = Mathf.Max(piece.Max(p => p.x) - piece.Min(p => p.x), piece.Max(p => p.z) - piece.Min(p => p.z));
                Assert.AreEqual(20f, span, .01f);
            }
        }

        [Test]
        public void ServiceRoad_DashesKeepTheirRhythmThroughBends()
        {
            AirfieldSetup.CreateMaterials();
            var go = new GameObject("Road bend test"); go.SetActive(false); spawned.Add(go);
            var airport = go.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport); airport.groundSize = 0f; go.SetActive(true);
            var net = airport.Taxiways;
            var layouts = new[]
            {
                new[] { new Vector2(80f, 0f), Vector2.zero, new Vector2(0f, -80f) },
                new[] { Vector2.zero, new Vector2(-12f, 0f), new Vector2(-12f, 0f) + Dir(235f) * 80f },
                new[] { new Vector2(90f, 0f), new Vector2(40f, 0f), new Vector2(15f, -25f), new Vector2(-15f, -25f), new Vector2(-35f, -55f), new Vector2(-90f, -55f) },
            };
            for (int i = 0; i < layouts.Length; i++)
            {
                int previous = 0;
                for (int k = 0; k < layouts[i].Length; k++)
                {
                    var p = layouts[i][k] + new Vector2(i * 500f, 0f);
                    int node = net.AddNode(new Vector3(p.x, 0f, p.y));
                    if (k > 0) net.Connect(previous, node, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
                    previous = node;
                }
            }
            net.Rebuild();
            for (int i = 0; i < layouts.Length; i++)
            {
                float x0 = i * 500f - 250f, x1 = i * 500f + 250f;
                AssertEvenDashes(DashesOf(net.PaintMesh, (p, c) => p.x > x0 && p.x < x1), 8, "service road layout " + i);
            }
        }
    }
}
