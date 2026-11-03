using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.EditorTools.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    public partial class TerminalBridgeTests
    {
        [Test]
        public void Boarding_AlwaysMeetsAFloor()
        {
            var t = Terminal.Polygon(new[] { new Vector2(-20f, -10f), new Vector2(20f, -10f), new Vector2(20f, 0f), new Vector2(-20f, 0f) }, 3);
            Assert.AreEqual(JetBridgeRules.RotundaFloor, t.FloorElevation(1), "the default first floor is a bridge's floor");
            Assert.AreEqual(t.FloorElevation(1), t.BoardingLevel());
            Assert.AreEqual(t.FloorElevation(2), t.BoardingLevel(8f), "a connection height picks the nearest floor");
            Assert.AreEqual(t.FloorElevation(1), t.BoardingLevel(0.5f), "never the ground floor");
            t.SetBoardingFloor(2);
            Assert.AreEqual(2, t.BoardingFloor()); Assert.AreEqual(t.FloorElevation(2), t.BoardingLevel());
            var single = Terminal.Polygon(t.corners, 1);
            Assert.AreEqual(3.5f, single.BoardingLevel(3.5f), "one storey has no floor to meet");

            var stand = new StandLayout { stand = new Stand(), axis = Vector2.up, nose = Vector2.up };
            var bridge = new JetBridge { root = Vector2.zero, rotunda = new Vector2(0f, 20f), connectionFloor = 4.6f };
            Assert.AreEqual(t.FloorElevation(1), BridgeLayout.Of(bridge, stand, new[] { t }).LinkStartFloor);
        }

        [Test]
        public void FacadeFloorBand_EndsWhereACorridorsUndersideIs()
        {
            var (airport, _, terminal) = TestAirport();
            float floor = terminal.BoardingLevel(), underside = floor + JetBridgeRules.LinkBottom;
            float depth = Mathf.Min(terminal.exterior.facade.spandrel, 0.45f * terminal.StoreyHeight(0));
            var vertices = airport.TerminalMesh.vertices;
            Assert.IsTrue(vertices.Any(v => Mathf.Abs(v.y - underside) < 0.001f), "the floor's band ends at the corridor's underside");
            Assert.IsTrue(vertices.Any(v => Mathf.Abs(v.y - (underside - depth)) < 0.001f), "and keeps the style's depth below it");
            Assert.IsFalse(vertices.Any(v => v.y > floor + 0.01f && v.y < floor + 1.2f),
                "nothing of the band above the floor: the storey above's glass runs level with the corridor");
        }

        [Test]
        public void BentCorridor_SegmentsShareTheirMitreAndRiseWithoutGaps()
        {
            var link = Box();
            var points = new List<Vector3> { new Vector3(0f, 4f, 0f), new Vector3(0f, 4.5f, 10f), new Vector3(8f, 5f, 16f) };
            const float width = 2.6f;
            var meshes = new List<Vector3[]>();
            for (int i = 0; i < 2; i++)
            {
                var a = points[i]; var b = points[i + 1];
                var ground = XZ(b - a);
                var (start, end) = CorridorMesh.Cuts(points, i, width, 0f, 0f);
                var mesh = new Mesh();
                CorridorMesh.Bend(link, mesh, ground.magnitude, b.y - a.y, 1f, start, end);
                var frame = Matrix4x4.TRS(a, Quaternion.LookRotation(new Vector3(ground.x, 0f, ground.y), Vector3.up), Vector3.one);
                meshes.Add(mesh.vertices.Select(v => frame.MultiplyPoint3x4(v)).ToArray());
                Object.DestroyImmediate(mesh);
            }
            // the first segment's far end and the second's near end are the same section, on one plane
            var source = link.vertices;
            var ends = Enumerable.Range(0, source.Length).Where(k => source[k].z > 0.5f).ToList();
            var starts = Enumerable.Range(0, source.Length).Where(k => source[k].z < 0.5f).ToList();
            Assert.AreEqual(ends.Count, starts.Count);
            for (int k = 0; k < ends.Count; k++)
            {
                Assert.Less(Vector3.Distance(meshes[0][ends[k]], meshes[1][starts[k]]), 1e-3f, "the joint is watertight");
                if (source[ends[k]].y == 0f) Assert.AreEqual(points[1].y, meshes[0][ends[k]].y, 1e-4f, "the joint's floor is the corner's");
            }
            Object.DestroyImmediate(link);
        }

        [Test]
        public void CorridorEnd_IsBuriedInAWallItMeetsAtAnAngle()
        {
            var t = Terminal.Polygon(new[] { new Vector2(-20f, -10f), new Vector2(20f, -10f), new Vector2(20f, 0f), new Vector2(-20f, 0f) }, 2);
            t.exterior.facade.lean = 15f;
            var stand = new StandLayout { stand = new Stand(), axis = Vector2.up, nose = Vector2.up };
            // root on the wall at the boarding floor, the corridor leaving it at 50 degrees
            t.ClosestOnWall(new Vector2(0f, 5f), out var root, out _);
            var bridge = new JetBridge { root = root, rotunda = root + new Vector2(Mathf.Cos(40f * Mathf.Deg2Rad), Mathf.Sin(40f * Mathf.Deg2Rad)) * 20f };
            var l = BridgeLayout.Of(bridge, stand, new[] { t });
            Assert.IsTrue(l.Connected);
            var corridor = l.Corridor;
            float reach = corridor.IntoWall(t);
            Assert.Greater(reach, 0.5f, "the trailing corner stands off an oblique wall");
            var d = (XZ(corridor.points[1]) - XZ(corridor.points[0])).normalized;
            var start = XZ(corridor.points[0]) - d * reach;
            foreach (float y in new[] { corridor.points[0].y + corridor.bottom, corridor.points[0].y + corridor.top })
                foreach (float side in new[] { -0.5f, 0.5f })
                    Assert.IsTrue(t.ContainsAtHeight(start + new Vector2(d.y, -d.x) * side * corridor.width, y), $"corner {side} at {y} m is inside the facade");
        }

        [Test]
        public void BridgeRig_BendsTheImportedLinkAlongACorridor()
        {
            var link = VehicleSetup.Prefab("JetBridge").transform.Find("Link").GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(CorridorMesh.CanBend(link), "the Link must import readable");
            var (airport, stands, _) = TestAirport();
            var st = stands[1];
            Assert.AreEqual(1, airport.Bridges.SetBridges(st, 1), Problems(airport, st));
            var b = st.bridges[0];
            var mid = (b.root + b.rotunda) * 0.5f;
            b.linkBends.Add(new Vector3(mid.x + 3f, b.Floor, mid.y));
            airport.Taxiways.Rebuild();
            var rig = airport.GetComponent<BridgeRig>();
            if (rig == null) rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = VehicleSetup.Prefab("JetBridge"); rig.Refresh();
            var instance = rig.transform.Find("Bridges/Jet bridge");
            var segments = instance.GetComponentsInChildren<MeshFilter>().Where(f => f.name == "Link" && f.gameObject.activeSelf).ToList();
            Assert.AreEqual(2, segments.Count);
            Assert.IsTrue(segments.All(f => f.sharedMesh != link && f.sharedMesh.vertexCount == link.vertexCount));
            Assert.AreEqual(Vector3.one, segments[0].transform.localScale, "bent, not stretched");
        }

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>A stand-in Link: a 2.6 x 3.1 m box section, extruded 1 m along +z, floor at y = 0.</summary>
        static Mesh Box()
        {
            var section = new[] { new Vector2(-1.3f, -0.35f), new Vector2(1.3f, -0.35f), new Vector2(1.3f, 0f), new Vector2(1.3f, 2.75f), new Vector2(-1.3f, 2.75f), new Vector2(-1.3f, 0f) };
            var vertices = new List<Vector3>();
            foreach (float z in new[] { 0f, 1f }) foreach (var p in section) vertices.Add(new Vector3(p.x, p.y, z));
            var triangles = new List<int>();
            int n = section.Length;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; triangles.AddRange(new[] { i, j, n + j, i, n + j, n + i }); }
            var mesh = new Mesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
