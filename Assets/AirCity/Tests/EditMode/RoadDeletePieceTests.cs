using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    /// <summary>A long public road is bulldozed a piece at a time however long it was when laid, and a section is
    /// outlined as it is drawn: round its bends.</summary>
    public class RoadDeletePieceTests
    {
        static PublicRoadNetwork Line(float length, out PublicRoadSegment edge, AlignmentShape shape = default)
        {
            var graph = new PublicRoadNetwork();
            int a = graph.AddNode(Vector3.zero), b = graph.AddNode(new Vector3(length, 0f, 0f));
            Assert.IsTrue(graph.Connect(a, b, PublicRoadStyle.Rural, RoadStructure.Ground, out string why, shape), why);
            edge = graph.segments.Single();
            return graph;
        }

        [Test]
        public void Pieces_AreEqualAndNoLongerThanTheLimit()
        {
            Assert.AreEqual((0f, 30f), PublicRoadNetwork.DeletePiece(30f, 12f), "a short section is one piece");
            Assert.AreEqual((80f, 120f), PublicRoadNetwork.DeletePiece(200f, 90f));
            Assert.AreEqual((160f, 200f), PublicRoadNetwork.DeletePiece(200f, 200f), "the far end is in the last piece");
            var (from, to) = PublicRoadNetwork.DeletePiece(1000f, 517f);
            Assert.LessOrEqual(to - from, PublicRoadNetwork.DeletePieceLength + .001f);
            Assert.IsTrue(from <= 517f && 517f <= to);
        }

        [Test]
        public void RemovePart_TakesTheMiddleAndLeavesBothEnds()
        {
            var graph = Line(200f, out var edge);
            graph.RemovePart(edge.id, 80f, 120f);
            Assert.AreEqual(2, graph.segments.Count, "the road either side stays");
            Assert.AreEqual(4, graph.nodes.Count);
            foreach (var s in graph.segments) Assert.AreEqual(80f, graph.Path(s).Length, .01f);
            var ends = graph.nodes.Select(n => n.position.x).OrderBy(x => x).ToArray();
            Assert.That(ends, Is.EqualTo(new[] { 0f, 80f, 120f, 200f }).Within(.01f));
            Assert.IsTrue(graph.Validate(out string problem), problem);
        }

        [Test]
        public void RemovePart_AtAnEndTakesThatEndAndAWholeSectionGoesEntirely()
        {
            var graph = Line(200f, out var edge);
            graph.RemovePart(edge.id, 0f, 40f);
            Assert.AreEqual(1, graph.segments.Count);
            Assert.AreEqual(2, graph.nodes.Count, "the end node goes with its piece");
            Assert.AreEqual(160f, graph.Path(graph.segments[0]).Length, .01f);
            Assert.AreEqual(40f, graph.nodes.Min(n => n.position.x), .01f);

            graph = Line(200f, out edge);
            graph.RemovePart(edge.id, 160f, 200f);
            Assert.AreEqual(160f, graph.Path(graph.segments.Single()).Length, .01f);
            Assert.AreEqual(160f, graph.nodes.Max(n => n.position.x), .01f);

            graph = Line(30f, out edge);
            graph.RemovePart(edge.id, 0f, 30f);
            Assert.AreEqual(0, graph.segments.Count);
            Assert.AreEqual(0, graph.nodes.Count);
        }

        [Test]
        public void RemovePart_KeepsACurvedSectionsShapeEitherSide()
        {
            var graph = Line(200f, out var edge, new AlignmentShape { curved = true, control = new Vector2(100f, 60f) });
            var path = graph.Path(edge);
            float length = path.Length;
            Vector3 before = path.AtDistance(30f), after = path.AtDistance(length - 30f);
            var (from, to) = PublicRoadNetwork.DeletePiece(length, length * .5f);
            graph.RemovePart(edge.id, from, to);
            Assert.AreEqual(2, graph.segments.Count);
            Assert.AreEqual(length - (to - from), graph.segments.Sum(s => graph.Path(s).Length), .5f, "only the piece is gone");
            float Off(Vector3 p) => graph.segments.Min(s => graph.Path(s).DistanceTo(new Vector2(p.x, p.z)));
            Assert.Less(Off(before), .05f, "the road before the piece lies where it did");
            Assert.Less(Off(after), .05f, "the road after the piece lies where it did");
        }

        [Test]
        public void Outline_FollowsTheBendInsteadOfRunningToTheCorner()
        {
            // 80 m sections either side of a right-angle bend at the origin
            var graph = new PublicRoadNetwork();
            int a = graph.AddNode(new Vector3(-80f, 0f, 0f)), c = graph.AddNode(Vector3.zero), b = graph.AddNode(new Vector3(0f, 0f, -80f));
            Assert.IsTrue(graph.Connect(a, c, PublicRoadStyle.AirportAccess, RoadStructure.Ground, out string why), why);
            Assert.IsTrue(graph.Connect(c, b, PublicRoadStyle.AirportAccess, RoadStructure.Ground, out why), why);
            var layout = PublicRoadGeometry.Layout(graph);
            var drawn = new PublicRoadGeometry(graph, null, null, _ => 0f);
            try
            {
                float half = RoadTypes.Of(PublicRoadStyle.AirportAccess).Width * .5f;
                float FromRoad(Vector3 p) => drawn.links.Min(l => Vector2.Distance(Geo.ClosestOnSegment(new Vector2(p.x, p.z), l.a, l.b, out _), new Vector2(p.x, p.z)));
                foreach (var edge in graph.segments)
                {
                    var loop = new List<Vector3>();
                    Assert.IsTrue(layout.SectionOutline(edge, 0f, graph.Path(edge).Length, half, loop));
                    foreach (var p in loop) Assert.AreEqual(half, FromRoad(p), .35f, "the outline runs along the pavement's edge at " + p);
                    // the bend's arc (25 m radius) passes 10.4 m inside the corner: the outline reaches its middle
                    var middle = new Vector2(-25f, -25f) + new Vector2(1f, 1f).normalized * 25f;
                    Assert.Less(loop.Min(p => Vector2.Distance(new Vector2(p.x, p.z), middle)), half + .5f, "the outline carries on to the middle of the bend");
                    Assert.Greater(loop.Min(p => new Vector2(p.x, p.z).magnitude), 4f, "and never runs out to the corner");
                }
                // a piece in the middle of a section: just that stretch
                var piece = new List<Vector3>();
                Assert.IsTrue(layout.SectionOutline(graph.segments[0], 30f, 50f, half, piece));
                Assert.AreEqual(-50f, piece.Min(p => p.x), .01f);
                Assert.AreEqual(-30f, piece.Max(p => p.x), .01f);
            }
            finally { RoadBendTests.Destroy(drawn); layout.DestroyMeshes(); }
        }
    }
}
