using System.Linq;
using AirCity.EditorTools.World;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    /// <summary>Public road types (2 to 6 lanes, one-way), their junctions (traffic lights, lane rules), the
    /// landside join guard and their geometry (kerbs, medians, signals).</summary>
    public class RoadTypeTests
    {
        static PublicRoadNetwork Cross(PublicRoadStyle main, PublicRoadStyle side)
        {
            var g = new PublicRoadNetwork();
            int w = g.AddNode(new Vector3(-90f, 0f, 0f)), e = g.AddNode(new Vector3(90f, 0f, 0f));
            Assert.IsTrue(g.Connect(w, e, main, RoadStructure.Ground, out string why), why);
            int s = g.AddNode(new Vector3(0f, 0f, -90f)), n = g.AddNode(new Vector3(0f, 0f, 90f));
            Assert.IsTrue(g.Connect(s, n, side, RoadStructure.Ground, out why), why);
            return g;
        }

        static PublicRoadNode Junction(PublicRoadNetwork g) => g.nodes.Single(n => g.At(n.id).Count() >= 3);

        [Test]
        public void Types_HaveTheirLanesAndWidths()
        {
            var street = RoadTypes.Of(PublicRoadStyle.Urban);
            var avenue = RoadTypes.Of(PublicRoadStyle.Avenue);
            var boulevard = RoadTypes.Of(PublicRoadStyle.Boulevard);
            var oneWay = RoadTypes.Of(PublicRoadStyle.OneWay2);
            Assert.AreEqual(2, street.Lanes); Assert.AreEqual(4, avenue.Lanes); Assert.AreEqual(6, boulevard.Lanes);
            Assert.IsTrue(oneWay.OneWay); Assert.AreEqual(2, oneWay.Lanes);
            Assert.Less(street.Width, avenue.Width); Assert.Less(avenue.Width, boulevard.Width);
            // lanes sit inside the carriageway, backward ones on the left, forward on the right
            foreach (var style in RoadTypes.Buildable)
            {
                var t = RoadTypes.Of(style);
                var lanes = t.LaneCentres();
                Assert.AreEqual(t.Lanes, lanes.Count, t.name);
                Assert.IsTrue(lanes.All(l => Mathf.Abs(l.x) + t.laneWidth * .5f <= t.Width * .5f + 1e-3f), t.name);
                Assert.IsTrue(lanes.Where(l => !l.forwards).All(l => l.x < 0f), t.name);
                if (!t.OneWay) Assert.IsTrue(lanes.Where(l => l.forwards).All(l => l.x > 0f), t.name);
            }
            // saved roads keep their number: the old styles are still first
            Assert.AreEqual(0, (int)PublicRoadStyle.Rural); Assert.AreEqual(4, (int)PublicRoadStyle.Ramp);
        }

        [Test]
        public void TownCrossroads_GetTrafficLights_CountryRoadsGivePriority()
        {
            Assert.AreEqual(JunctionControl.TrafficLights, Junction(Cross(PublicRoadStyle.Urban, PublicRoadStyle.Urban)).control);
            Assert.AreEqual(JunctionControl.TrafficLights, Junction(Cross(PublicRoadStyle.Avenue, PublicRoadStyle.Urban)).control);
            Assert.AreEqual(JunctionControl.Priority, Junction(Cross(PublicRoadStyle.Rural, PublicRoadStyle.Rural)).control);
            // chosen by hand: stays
            var g = Cross(PublicRoadStyle.Urban, PublicRoadStyle.Urban);
            var j = Junction(g);
            g.SetControl(j.id, JunctionControl.Priority);
            int extra = g.AddNode(new Vector3(60f, 0f, 60f));
            Assert.IsTrue(g.Connect(j.id, extra, PublicRoadStyle.Urban, RoadStructure.Ground, out string why), why);
            Assert.AreEqual(JunctionControl.Priority, g.Node(j.id).control);
        }

        [Test]
        public void EndingOnARoad_MakesATJunction()
        {
            var g = new PublicRoadNetwork();
            int w = g.AddNode(new Vector3(-90f, 0f, 0f)), e = g.AddNode(new Vector3(90f, 0f, 0f));
            Assert.IsTrue(g.Connect(w, e, PublicRoadStyle.Urban, RoadStructure.Ground, out string why), why);
            int on = g.AddNode(new Vector3(10f, 0f, 0f)), n = g.AddNode(new Vector3(10f, 0f, 80f));
            Assert.IsTrue(g.Connect(n, on, PublicRoadStyle.Urban, RoadStructure.Ground, out why), why);
            Assert.AreEqual(3, g.At(on).Count(), "the road it ends on is split there");
            Assert.AreEqual(JunctionControl.TrafficLights, g.Node(on).control);
            Assert.IsTrue(g.HasRoute(w, n) && g.HasRoute(n, e));
        }

        [Test]
        public void MultiLaneApproach_KerbLaneTurnsRight_CentreLaneTurnsLeft()
        {
            var g = Cross(PublicRoadStyle.Avenue, PublicRoadStyle.Avenue);
            var j = Junction(g);
            var moves = g.LaneConnections(j.id);
            foreach (var incoming in g.At(j.id))
            {
                var mine = moves.Where(m => m.incoming == incoming.id).ToList();
                Assert.IsTrue(mine.Where(m => m.turn == RoadTurn.Right).All(m => m.incomingLane == 0), "right turns from the kerb lane");
                Assert.IsTrue(mine.Where(m => m.turn == RoadTurn.Left).All(m => m.incomingLane == 1), "left turns from the centre lane");
                Assert.AreEqual(2, mine.Count(m => m.turn == RoadTurn.Straight), "both lanes go straight on");
                Assert.IsTrue(mine.Where(m => m.turn == RoadTurn.Right).All(m => m.outgoingLane == 0), "into the kerb lane");
            }
            // a one-way road leaving the junction takes no traffic in: no moves from it
            var one = new PublicRoadNetwork();
            int a = one.AddNode(new Vector3(-90f, 0f, 0f)), b = one.AddNode(new Vector3(90f, 0f, 0f));
            Assert.IsTrue(one.Connect(a, b, PublicRoadStyle.Urban, RoadStructure.Ground, out string why), why);
            int c = one.AddNode(new Vector3(0f, 0f, 0f)) , d = one.AddNode(new Vector3(0f, 0f, -90f));
            Assert.IsTrue(one.Connect(c, d, PublicRoadStyle.OneWay2, RoadStructure.Ground, out why), why);
            var leaving = one.At(c).Single(e => e.style == PublicRoadStyle.OneWay2);
            Assert.IsFalse(one.LaneConnections(c).Any(m => m.incoming == leaving.id));
            Assert.IsTrue(one.LaneConnections(c).Any(m => m.outgoing == leaving.id));
        }

        [Test]
        public void JoinProblem_RefusesShallowJoinsCrowdedCrossingsAndHighwaysAtGrade()
        {
            var g = new PublicRoadNetwork();
            int w = g.AddNode(new Vector3(-100f, 0f, 0f)), e = g.AddNode(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(g.Connect(w, e, PublicRoadStyle.Urban, RoadStructure.Ground, out string why), why);
            var road = g.segments[0];
            var shallow = new Vector2(100f * Mathf.Cos(8f * Mathf.Deg2Rad), 100f * Mathf.Sin(8f * Mathf.Deg2Rad));
            StringAssert.Contains("8°", g.JoinProblem(shallow, -1, new Vector2(0f, 0f), -1, road, PublicRoadStyle.Urban));
            Assert.IsNull(g.JoinProblem(new Vector2(0f, 80f), -1, new Vector2(0f, 0f), -1, road, PublicRoadStyle.Urban));
            // a T at x = 0 and a road crossing 2 m from it
            int t = g.AddNode(new Vector3(0f, 0f, 0f)), n = g.AddNode(new Vector3(0f, 0f, 90f));
            Assert.IsTrue(g.Connect(n, t, PublicRoadStyle.Avenue, RoadStructure.Ground, out why), why);
            StringAssert.Contains("too short", g.JoinProblem(new Vector2(2f, -60f), -1, new Vector2(2f, -10f), -1, null, PublicRoadStyle.Urban) ??
                g.JoinProblem(new Vector2(2f, -60f), -1, new Vector2(2f, 60f), -1, null, PublicRoadStyle.Urban));
            StringAssert.Contains("highway", g.JoinProblem(new Vector2(50f, -60f), -1, new Vector2(50f, 60f), -1, null, PublicRoadStyle.Highway));
        }

        [Test]
        public void Geometry_AvenueHasKerbsMedianAndSignalsOnEveryApproach()
        {
            WorldAssetSetup.Ensure();
            var g = Cross(PublicRoadStyle.Avenue, PublicRoadStyle.Avenue);
            var model = PublicRoadKitSetup.CreateModel("test", g);
            try
            {
                var geometry = new PublicRoadGeometry(g, null, null, _ => 0f);
                Assert.Greater(geometry.kerbMesh.vertexCount, 0, "kerbs");
                Assert.Greater(geometry.medianGrass.vertexCount, 0, "a raised median");
                var j = Junction(g);
                foreach (var e in g.At(j.id)) Assert.Greater(geometry.StopLine(j.id, e.id), 0f, "a stop line on every approach");
                // the median stops behind the stop line: none of it inside the junction
                float stop = g.At(j.id).Min(e => geometry.StopLine(j.id, e.id));
                Assert.IsFalse(geometry.medianGrass.vertices.Any(v => new Vector2(v.x, v.z).magnitude < stop - .1f), "median clear of the junction");
                var signals = model.GetComponent<PublicRoadSignalsView>();
                // four approaches, each with a kerb pole, a mast-arm head (two lanes) and a median head
                Assert.AreEqual(12, signals.HeadCount);
                foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures, geometry.kerbMesh, geometry.medianGrass })
                    Object.DestroyImmediate(mesh);
            }
            finally { Object.DestroyImmediate(model); }
        }

        [Test]
        public void Geometry_EveryBuildableTypeBuildsClean()
        {
            foreach (var main in RoadTypes.Buildable)
            foreach (var side in new[] { PublicRoadStyle.Urban, PublicRoadStyle.OneWay1 })
            {
                if (!RoadTypes.Of(main).atGrade) continue;
                var g = Cross(main, side);
                var geometry = new PublicRoadGeometry(g, null, null, _ => 0f);
                try
                {
                    Assert.IsEmpty(geometry.MisfitJunctions, $"{main} x {side}");
                    var v = geometry.pavement.vertices; var t = geometry.pavement.triangles;
                    Assert.IsTrue(v.All(p => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z)), $"{main} x {side}");
                    int down = 0;
                    for (int i = 0; i < t.Length; i += 3)
                        if (Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y < -1e-3f) down++;
                    Assert.AreEqual(0, down, $"{main} x {side}: down-facing pavement");
                }
                finally
                {
                    foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures, geometry.kerbMesh, geometry.medianGrass })
                        Object.DestroyImmediate(mesh);
                }
            }
        }
    }
}
