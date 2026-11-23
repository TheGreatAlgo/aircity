using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    /// <summary>Paint where public roads meet, as on North American roads: stop lines only where traffic stops and
    /// only across its own lanes, a double solid centre line up to them, arrows only where there are lanes to choose
    /// between, and the through road's centre line carried across a side road.</summary>
    public class RoadJunctionMarkingTests
    {
        /// <summary>A junction at the origin: the road east-west, and a side road from the south (and the north).</summary>
        static PublicRoadGeometry Junction(PublicRoadStyle main, PublicRoadStyle side, bool cross, out PublicRoadNetwork graph)
        {
            graph = new PublicRoadNetwork();
            int centre = graph.AddNode(Vector3.zero);
            var ends = new List<(Vector3 p, PublicRoadStyle style)> { (new Vector3(120f, 0f, 0f), main), (new Vector3(-120f, 0f, 0f), main), (new Vector3(0f, 0f, -120f), side) };
            if (cross) ends.Add((new Vector3(0f, 0f, 120f), side));
            foreach (var (p, style) in ends)
                Assert.IsTrue(graph.Connect(centre, graph.AddNode(p), style, RoadStructure.Ground, out string why), why);
            return new PublicRoadGeometry(graph, null, null, _ => 0f);
        }

        /// <summary>The paint marks of a mesh: each one's corners, colour and half width.</summary>
        static List<(List<Vector2> points, Color colour, float halfWidth, bool arrow)> Marks(Mesh mesh)
        {
            var positions = mesh.vertices; var colours = mesh.colors; var triangles = mesh.triangles;
            var shapes = new List<Vector4>(); mesh.GetUVs(1, shapes);
            var parent = Enumerable.Range(0, positions.Length).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < triangles.Length; i += 3)
            { parent[Find(triangles[i + 1])] = Find(triangles[i]); parent[Find(triangles[i + 2])] = Find(triangles[i]); }
            return Enumerable.Range(0, positions.Length).GroupBy(Find).Select(g =>
                (g.Select(i => new Vector2(positions[i].x, positions[i].z)).ToList(), colours[g.Key], shapes[g.Key].x, shapes[g.Key].w > 1.5f)).ToList();
        }

        static bool StopLine((List<Vector2> points, Color colour, float halfWidth, bool arrow) m) =>
            !m.arrow && !RoadBendTests.Yellow(m.colour) && Mathf.Abs(m.halfWidth - .25f) < .01f;

        [Test]
        public void CountryJunction_OnlyTheSideRoadStopsAndItsLineStaysInItsLane()
        {
            var geometry = Junction(PublicRoadStyle.Rural, PublicRoadStyle.Rural, false, out var graph);
            try
            {
                Assert.AreEqual(JunctionControl.Priority, graph.nodes[0].control);
                var marks = Marks(geometry.markings);
                var stops = marks.Where(StopLine).ToList();
                Assert.AreEqual(1, stops.Count, "one stop line: the road straight through has none");
                // traffic arrives from the south driving north on the right: its lane is 0 < x < 3.25 m, the edge
                // line 0.25 m inside the pavement's edge
                float half = graph.segments[2].Width * .5f;
                foreach (var p in stops[0].points)
                {
                    Assert.Less(p.y, -half, "the stop line is on the side road");
                    Assert.Greater(p.x, .1f, "the stop line starts at the centre line");
                    Assert.Less(p.x, half - .25f + .08f, "the stop line ends at the edge line, not on the shoulder");
                }
                Assert.IsFalse(marks.Any(m => m.arrow), "a single lane carries no arrow");
                // the side road's centre line is double and solid up to its stop line
                float stop = -stops[0].points.Max(p => p.y);
                var centre = marks.Where(m => RoadBendTests.Yellow(m.colour) && m.points.All(p => Mathf.Abs(p.x) < 1f) && m.points.Any(p => p.y > -stop - 2f) &&
                                              m.points.All(p => p.y < -half)).ToList();
                Assert.AreEqual(2, centre.Count, "a double centre line at the stop line");
                foreach (var line in centre)
                    Assert.Greater(line.points.Max(p => p.y) - line.points.Min(p => p.y), 15f, "the double line runs back from the stop line");
                Assert.IsTrue(centre.Any(m => m.points.All(p => p.x > 0f)) && centre.Any(m => m.points.All(p => p.x < 0f)), "one line each side of the centre");
            }
            finally { RoadBendTests.Destroy(geometry); }
        }

        [Test]
        public void CountryJunction_TheThroughRoadsCentreLineKeepsItsRhythmAcross()
        {
            foreach (bool cross in new[] { false, true })
            {
                var geometry = Junction(PublicRoadStyle.Rural, PublicRoadStyle.Rural, cross, out _);
                try
                {
                    var dashes = RoadBendTests.DashesOf(geometry.markings, (p, c) => RoadBendTests.Yellow(c) && Mathf.Abs(p.y) < .5f);
                    RoadBendTests.AssertEvenDashes(dashes, 12, cross ? "across a crossroads" : "past a side road", 9f);
                    Assert.IsTrue(dashes.Any(d => Mathf.Abs((d.a.x + d.b.x) * .5f) < 8f), "the centre line carries on across the junction");
                }
                finally { RoadBendTests.Destroy(geometry); }
            }
        }

        [Test]
        public void Wear_PavementAndPaintCarryTheirPlaceInTheLanes()
        {
            var geometry = Junction(PublicRoadStyle.Avenue, PublicRoadStyle.Urban, false, out var graph);
            try
            {
                var type = RoadTypes.Of(PublicRoadStyle.Avenue);
                float start = type.median * .5f + type.innerShoulder;
                // the avenue's pavement, well clear of the junction: metres right of the centreline (it runs out from
                // the junction, so east of it "right" is south), the lane width, two lanes each way from the median
                var positions = geometry.pavement.vertices; var colours = geometry.pavement.colors;
                var uv0 = new List<Vector4>(); var uv1 = new List<Vector4>();
                geometry.pavement.GetUVs(0, uv0); geometry.pavement.GetUVs(1, uv1);
                int section = 0, braking = 0;
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i];
                    if (p.x < 40f || p.x > 110f || Mathf.Abs(p.z) > type.Width * .5f + .01f) continue;   // (the dead end beyond has no lanes)
                    section++;
                    Assert.AreEqual(-p.z, uv0[i].z, .01f, "metres right of the centreline at " + p);
                    Assert.AreEqual(type.laneWidth, uv0[i].w, .001f, "the lane width");
                    Assert.AreEqual(2f, uv1[i].z, .001f, "lanes each way");
                    Assert.AreEqual(start, uv1[i].w, .001f, "where the lanes start");
                    Assert.AreEqual((float)AirCity.Airfield.AirfieldKind.Road, uv1[i].x, .001f, "still a road surface");
                    if (p.x < 45f) { Assert.Greater(colours[i].b, .2f, "traffic brakes near the stop line"); braking++; }
                    if (p.x > 100f) Assert.Less(colours[i].b, .05f, "and runs free far from it");
                }
                Assert.Greater(section, 20, "section vertices found");
                Assert.Greater(braking, 0, "vertices near the stop line found");
                Assert.IsTrue(Enumerable.Range(0, positions.Length).Any(i => new Vector2(positions[i].x, positions[i].z).magnitude < 3f && uv1[i].z < -.5f),
                    "the junction carries the through road's lanes, marked as a junction");
                // paint: its place in its own side's lanes, and worn
                var paint = geometry.markings.vertices; var uv3 = new List<Vector4>(); geometry.markings.GetUVs(3, uv3);
                Assert.AreEqual(paint.Length, uv3.Count);
                int lines = 0;
                for (int i = 0; i < paint.Length; i++)
                {
                    Assert.Greater(uv3[i].w, .2f, "road paint is worn");
                    Assert.Less(uv3[i].z, -.5f, "road paint is marked as lane paint, not runway paint");
                    var p = paint[i];
                    // the lane line between the avenue's two eastbound lanes... both sides: one lane width into the lanes
                    if (p.x < 60f || p.x > 100f || Mathf.Abs(Mathf.Abs(p.z) - start - type.laneWidth) > .2f) continue;
                    lines++;
                    Assert.AreEqual(Mathf.Abs(p.z) - start, uv3[i].x, .2f, "metres into its side's lanes");
                    Assert.AreEqual(2f, uv3[i].y, .001f, "lanes on its side");
                    Assert.AreEqual(-type.laneWidth, uv3[i].z, .001f, "minus the lane width");
                }
                Assert.Greater(lines, 4, "lane-line vertices found");
            }
            finally { RoadBendTests.Destroy(geometry); }
        }

        [Test]
        public void SignalledJunction_EveryApproachStopsBehindItsCrosswalkAndOnlyLaneChoicesGetArrows()
        {
            var street = Junction(PublicRoadStyle.Urban, PublicRoadStyle.Urban, false, out var graph);
            try
            {
                Assert.AreEqual(JunctionControl.TrafficLights, graph.nodes[0].control);
                var marks = Marks(street.markings);
                Assert.AreEqual(3, marks.Count(StopLine), "a stop line on each approach");
                Assert.IsFalse(marks.Any(m => m.arrow), "single lanes carry no arrows");
                float half = graph.segments[0].Width * .5f;
                foreach (var stop in marks.Where(StopLine))
                    foreach (var p in stop.points)
                        Assert.LessOrEqual(Mathf.Min(Mathf.Abs(p.x), Mathf.Abs(p.y)), half - .2f, "a stop line stays between the kerbs");
            }
            finally { RoadBendTests.Destroy(street); }
            var avenue = Junction(PublicRoadStyle.Avenue, PublicRoadStyle.Avenue, true, out graph);
            try
            {
                var marks = Marks(avenue.markings);
                Assert.AreEqual(4, marks.Count(StopLine), "a stop line on each approach");
                // two lanes on each of four approaches: a head for each move a lane may make (kerb lane straight and
                // right, centre lane straight and left)
                Assert.AreEqual(16, marks.Count(m => m.arrow), "arrow heads");
                // every arrow sits inside its lane: clear of the lane lines either side
                var type = RoadTypes.Of(PublicRoadStyle.Avenue);
                foreach (var arrow in marks.Where(m => m.arrow))
                    foreach (var p in arrow.points)
                    {
                        float across = Mathf.Min(Mathf.Abs(p.x), Mathf.Abs(p.y));   // from the arm's own centreline
                        float inLane = Mathf.Repeat(across - type.median * .5f - type.innerShoulder, type.laneWidth);
                        Assert.Greater(inLane, .25f, "an arrow head keeps clear of its lane's lines");
                        Assert.Less(inLane, type.laneWidth - .25f, "an arrow head keeps clear of its lane's lines");
                    }
            }
            finally { RoadBendTests.Destroy(avenue); }
        }
    }
}
