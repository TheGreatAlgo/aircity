using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    public partial class TerminalBridgeTests
    {
        [TestCase(RoofForm.Wave, false)]
        [TestCase(RoofForm.Scalloped, true)]
        public void RoofForms_FollowSlantedGlassAndFillRotatedConcaveBuildings(RoofForm form, bool across)
        {
            var (airport, _, _) = TestAirport(terminal: false);
            var points = new[] { Vector2.zero, new Vector2(60f, 0f), new Vector2(60f, 15f), new Vector2(25f, 15f), new Vector2(25f, 40f), new Vector2(0f, 40f) };
            var rotated = points.Select(p => new Vector2(p.x * 0.8f - p.y * 0.6f, p.x * 0.6f + p.y * 0.8f) + new Vector2(900f, -900f));
            var terminal = airport.AddTerminal(rotated);
            Assert.IsNotNull(terminal);
            var copy = terminal.Clone();
            FacadeStyles.Apply(copy.exterior, FacadeStyle.Colonnade);
            copy.exterior.facade.lean = 5f;
            copy.exterior.roofShape.form = form; copy.exterior.roofShape.rise = 4f; copy.exterior.roofShape.across = across;
            Assert.AreEqual(terminal.RoofHeight, copy.RoofHeight);
            Assert.AreEqual(terminal.boardingHeight, copy.boardingHeight);
            Assert.IsTrue(airport.ChangeTerminal(terminal, copy), airport.TerminalProblem(copy, terminal));
            var mesh = airport.TerminalMesh;
            Assert.IsTrue(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)));
            Assert.IsTrue(mesh.normals.All(v => v.sqrMagnitude > 0.9f));
            Assert.LessOrEqual(mesh.bounds.max.y, terminal.Height + 0.02f);
            Assert.Greater(mesh.bounds.max.y, terminal.RoofHeight + 3.6f);
            var vertices = mesh.vertices; var colors = mesh.colors; var normals = mesh.normals;
            var roof = Enumerable.Range(0, vertices.Length).Where(i => colors[i] == terminal.exterior.roof.linear && normals[i].y > 0.2f).ToList();
            Assert.Greater(roof.Count, 50, "the roof needs interior curved geometry, not only a patterned fascia");
            Assert.Greater(roof.Max(i => vertices[i].y) - roof.Min(i => vertices[i].y), 3.5f);
            Assert.IsTrue(roof.Any(i => Mathf.Abs(normals[i].x) + Mathf.Abs(normals[i].z) > 0.1f));
            var heights = terminal.storeyHeights.ToArray(); var shape = terminal.exterior.roofShape.Clone();
            var restyled = terminal.Clone(); FacadeStyles.Apply(restyled.exterior, FacadeStyle.Minimal);
            Assert.AreEqual(form, restyled.exterior.roofShape.form); Assert.AreEqual(4f, restyled.exterior.roofShape.rise);
            CollectionAssert.AreEqual(heights, restyled.storeyHeights);
            restyled.exterior.roofShape.rise = 1f; Assert.AreEqual(shape.rise, terminal.exterior.roofShape.rise);
        }

        [Test]
        public void RuntimeShapeControls_ChangeSelectedWallsAndRejectCollapsingAngles()
        {
            var (airport, stands, terminal) = TestAirport();
            using var designer = new DevGateDesigner(); designer.Open(airport, stands[0]); designer.SelectTerminal(terminal);
            Assert.IsTrue(designer.SetFacadeAngle(8f, 0), designer.Message);
            Assert.AreEqual(8f, terminal.exterior.ForFace(0).lean); Assert.AreEqual(0f, terminal.exterior.ForFace(1).lean);
            Assert.IsTrue(designer.SetRoofForm(RoofForm.Wave), designer.Message);
            Assert.IsTrue(designer.SetRoofRise(4f), designer.Message);
            Assert.IsTrue(designer.ApplyFacadeStyle(FacadeStyle.Framed), designer.Message);
            Assert.AreEqual(8f, terminal.exterior.ForFace(0).lean); Assert.AreEqual(RoofForm.Wave, terminal.exterior.roofShape.form);
            Assert.IsTrue(designer.SetFacadeAngle(0f), designer.Message);
            Assert.AreEqual(0f, terminal.exterior.ForFace(0).lean);
            Assert.IsTrue(designer.EditTerminal(terminal, t => { t.SetStoreyHeight(0, 40f); t.SetStoreyHeight(1, 40f); }), designer.Message);
            Assert.IsFalse(designer.SetFacadeAngle(-30f));
            Assert.AreEqual(0f, terminal.exterior.facade.lean);
            Assert.IsNotEmpty(designer.Message);
        }

        [Test]
        public void RoofForms_OldProfilesStayFlatAndConcourseEnvelopesIncludeTheCrest()
        {
            var old = JsonUtility.FromJson<BuildingExterior>("{\"parapet\":0.3}");
            Assert.IsNotNull(old.roofShape); Assert.AreEqual(0f, old.roofShape.MaxRise);
            var (airport, stands, _) = TestAirport(SizeCode.F); var stand = stands[0];
            Assert.AreEqual(3, airport.Bridges.SetBridges(stand, 3), Problems(airport, stand));
            var head = airport.Bridges.HeadOf(stand); float flatHeight = head.Building.Height;
            stand.head.Exterior.roofShape.form = RoofForm.Wave; stand.head.Exterior.roofShape.rise = 2f;
            airport.Taxiways.Rebuild(); head = airport.Bridges.HeadOf(stand);
            Assert.AreEqual(flatHeight + 2f, head.Building.Height, 0.01f);
            Assert.AreEqual(head.Building.Height, head.Shape.parts[0].Top(head.a), 0.01f);
            var rig = airport.GetComponent<BridgeRig>();
            if (rig == null) rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = VehicleSetup.Prefab("JetBridge"); rig.Refresh();
            var mesh = rig.transform.Find("Bridges/Bridge head/Concourse building").GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.bounds.max.y, flatHeight + 1.8f);
        }

        [TestCase(FacadeStyle.Classic)]
        [TestCase(FacadeStyle.Minimal)]
        [TestCase(FacadeStyle.Framed)]
        [TestCase(FacadeStyle.Colonnade)]
        public void FacadeRecipes_KeepTallFloorsAngledWallsAndConnections(FacadeStyle style)
        {
            var (airport, _, terminal) = TestAirport();
            var original = terminal.Clone();
            original.SetStoreyHeight(0, 12f); original.exterior.facade.lean = 2f;
            original.exterior.Override(0).lean = 8f; original.exterior.Override(0).cladding = Color.red;
            original.exterior.Override(1).glassTint = Color.magenta;
            var change = original.Clone();
            FacadeStyles.Apply(change.exterior, style); FacadeStyles.SetTint(change.exterior, GlassTint.Blue);
            CollectionAssert.AreEqual(original.corners, change.corners);
            CollectionAssert.AreEqual(original.storeyHeights, change.storeyHeights);
            Assert.AreEqual(original.boardingHeight, change.boardingHeight);
            CollectionAssert.AreEqual(original.WallOutline(original.boardingHeight), change.WallOutline(change.boardingHeight));
            Assert.AreEqual(8f, change.exterior.ForFace(0).lean);
            Assert.AreEqual(2f, change.exterior.ForFace(1).lean);
            Assert.AreEqual(Color.red, original.exterior.ForFace(0).cladding);
            for (int edge = 0; edge < change.EdgeCount; edge++)
            {
                Assert.AreEqual(1f, change.exterior.ForFace(edge).glazing);
                Assert.AreEqual(change.exterior.facade.cladding, change.exterior.ForFace(edge).cladding);
                Assert.AreEqual(FacadeStyles.Tint(GlassTint.Blue), change.exterior.ForFace(edge).glassTint);
            }
            Assert.IsTrue(airport.ChangeTerminal(terminal, change), airport.TerminalProblem(change, terminal));
            var mesh = airport.TerminalMesh;
            Assert.Greater(mesh.vertexCount, 100);
            Assert.IsTrue(mesh.vertices.All(v => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y)));
            Assert.IsTrue(mesh.normals.All(v => v.sqrMagnitude > 0.9f));
            var uv = new List<Vector4>(); mesh.GetUVs(0, uv);
            Assert.IsTrue(uv.Any(v => v.x == 1f), "recipes must keep glazed curtain walls");
        }

        [Test]
        public void FacadeRecipes_ElevatedHeadsKeepTheirHeightAndMechanicalSupports()
        {
            var (airport, stands, _) = TestAirport(SizeCode.F); var stand = stands[0];
            Assert.AreEqual(3, airport.Bridges.SetBridges(stand, 3), Problems(airport, stand));
            stand.head.foundation = HeadFoundation.Columns;
            FacadeStyles.Apply(stand.head.Exterior, FacadeStyle.Colonnade);
            airport.Taxiways.Rebuild();
            var head = airport.Bridges.HeadOf(stand);
            Assert.AreEqual(BridgeProblem.None, head.Placement(airport.Bridges.Site(stand)));
            Assert.IsTrue(head.Columns(airport.Bridges.Site(stand).pavement));
            Assert.Greater(head.columns.Count, 0);
            var rig = airport.GetComponent<BridgeRig>();
            if (rig == null) rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = VehicleSetup.Prefab("JetBridge"); rig.Refresh();
            var root = rig.transform.Find("Bridges/Bridge head");
            Assert.IsFalse(root.Find("Head").gameObject.activeSelf);
            var body = root.Find("Concourse building");
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.vertexCount, 0);
            Assert.AreEqual(head.Floor + head.Bottom, body.localPosition.y, 0.001f);
            Assert.AreEqual(head.UpperFloor + JetBridgeRules.HeadTop, body.localPosition.y + mesh.bounds.max.y, 0.01f);
        }

        [Test]
        public void HeadBuilding_ReachesTheGroundAndKeepsElevatedCorridorSupports()
        {
            var (airport, stands, _) = TestAirport(SizeCode.F);
            var stand = stands[0];
            Assert.AreEqual(3, airport.Bridges.SetBridges(stand, 3), Problems(airport, stand));
            var head = airport.Bridges.HeadOf(stand);
            Assert.IsTrue(head.Grounded);
            Assert.IsTrue(head.Columns(airport.Bridges.Site(stand).pavement));
            Assert.AreEqual(0, head.columns.Count);
            Assert.AreEqual(0f, head.Shape.parts[0].Bottom(head.a));
            Assert.AreEqual(head.UpperFloor + JetBridgeRules.HeadTop, head.Building.Height, 0.001f);
            var rig = airport.GetComponent<BridgeRig>();
            if (rig == null) rig = airport.gameObject.AddComponent<BridgeRig>();
            rig.prefab = VehicleSetup.Prefab("JetBridge"); rig.Refresh();
            var root = rig.transform.Find("Bridges/Bridge head");
            Assert.IsFalse(root.Find("Head").gameObject.activeSelf);
            Assert.IsFalse(root.Find("HeadUpper").gameObject.activeSelf);
            var body = root.Find("Concourse building").GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(0f, body.bounds.min.y, 0.001f);
            Assert.AreEqual(head.Building.Height, body.bounds.max.y, 0.01f);
            Assert.AreEqual("AirCity/Building", root.Find("Concourse building").GetComponent<Renderer>().sharedMaterial.shader.name);
            stand.head.foundation = HeadFoundation.Columns; airport.Taxiways.Rebuild(); rig.Refresh();
            Assert.IsTrue(root.Find("Head").gameObject.activeSelf);
            Assert.IsNull(root.Find("Concourse building"));
            Assert.Greater(airport.Bridges.HeadOf(stand).Shape.parts[0].Bottom(head.a), 0f);
        }

        [Test]
        public void HeadBuilding_CannotCoverARoadThatAnElevatedHeadCanStraddle()
        {
            var sl = new StandLayout { stand = new Stand(), axis = Vector2.up, nose = Vector2.up };
            var saved = new BridgeHead { from = new Vector2(0f, 20f), to = new Vector2(0f, 50f), root = Vector2.zero };
            var road = new[] { new Vector2(-50f, 31.5f), new Vector2(50f, 31.5f), new Vector2(50f, 38.5f), new Vector2(-50f, 38.5f) };
            var site = new BridgeSite { pavement = new List<Vector2[]> { road } };
            Assert.AreEqual(BridgeProblem.GroundedHeadOnPavement, HeadLayout.Of(saved, sl, null, false).Placement(site));
            saved.foundation = HeadFoundation.Columns;
            Assert.AreEqual(BridgeProblem.None, HeadLayout.Of(saved, sl, null, false).Placement(site));
        }

        [Test]
        public void TerminalEnvelope_UsesIndividualStoreysAndTheFacadeAtTheConnectionHeight()
        {
            var terminal = Terminal.Polygon(new[] { new Vector2(-20f, -10f), new Vector2(20f, -10f), new Vector2(20f, 0f), new Vector2(-20f, 0f) }, 3);
            terminal.storeyHeights = new List<float> { 6f, 12f, 5f }; terminal.SetBoardingFloor(2);
            terminal.exterior.Override(2).lean = 30f;
            Assert.IsNull(terminal.EnvelopeProblem());
            Assert.AreEqual(18f, terminal.FloorElevation(2)); Assert.AreEqual(23f, terminal.RoofHeight);
            Assert.AreEqual(18f, terminal.BoardingLevel());
            float outset = Mathf.Tan(30f * Mathf.Deg2Rad) * terminal.BoardingLevel();
            terminal.ClosestOnWall(new Vector2(0f, 30f), out var wall, out _);
            Assert.AreEqual(outset, wall.y, 0.001f);
            Assert.IsFalse(terminal.Contains(new Vector2(0f, 2f)));
            Assert.IsTrue(terminal.ContainsAtHeight(new Vector2(0f, 2f), 10f));
            var sl = new StandLayout { stand = new Stand(), axis = Vector2.up, nose = Vector2.up };
            var bridge = new JetBridge { root = wall, rotunda = wall + new Vector2(0f, 20f), rotundaFloor = 18f };
            var layout = BridgeLayout.Of(bridge, sl, new[] { terminal });
            Assert.IsTrue(layout.Connected); Assert.AreEqual(18f, layout.LinkStartFloor);
            bridge.connectionFloor = 5f; bridge.root = new Vector2(0f, Mathf.Tan(30f * Mathf.Deg2Rad) * 6f);
            layout = BridgeLayout.Of(bridge, sl, new[] { terminal });
            Assert.IsTrue(layout.Connected, "an explicit lower connection must use that facade section");
            Assert.AreEqual(6f, layout.LinkStartFloor, "at the floor nearest the height asked for");
        }

        [Test]
        public void TerminalEnvelope_RendersLeaningGlassAndRejectsOverhangOverARoad()
        {
            var (airport, _, terminal) = TestAirport();
            var changed = terminal.Clone(); changed.storeyHeights = new List<float> { 6f, 12f };
            changed.exterior.Override(0).lean = 8f;
            Assert.IsTrue(airport.ChangeTerminal(terminal, changed), airport.TerminalProblem(changed, terminal));
            Assert.AreEqual(18f, terminal.RoofHeight);
            var mesh = airport.TerminalMesh;
            Assert.IsTrue(mesh.normals.Any(n => n.y < -0.1f && Mathf.Abs(n.z) > 0.8f));
            var levels = new List<Vector4>(); mesh.GetUVs(1, levels);
            Assert.IsTrue(levels.Any(x => Mathf.Approximately(x.x, 1f) && x.y > 0f));
            var outline = terminal.Outline();
            float roadZ = outline.Min(p => p.y) - 4f;
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(-200f, 0f, roadZ)), b = net.AddNode(new Vector3(200f, 0f, roadZ));
            net.Connect(a, b, 3f, PavementSurface.Asphalt, false, RoadClass.Service);
            StringAssert.Contains("road", airport.TerminalProblem(terminal, terminal));
            changed = terminal.Clone(); changed.exterior.Override(0).lean = 0f;
            Assert.IsNull(airport.TerminalProblem(changed, terminal));
        }

        [Test]
        public void FacadeFaces_KeepTheirIdentityOnArcsAndDeepCopiesAndMirrors()
        {
            var terminal = Terminal.Polygon(new[] { Vector2.zero, new Vector2(30f, 0f), new Vector2(30f, 20f), new Vector2(0f, 20f) });
            terminal.SetBulge(0, 8f); terminal.exterior.Override(0).cladding = Color.red;
            var points = terminal.OutlineWithFaces(out var faces);
            Assert.Greater(faces.Count(x => x == 0), 3);
            Assert.AreEqual(points.Count, faces.Count);
            Assert.IsTrue(faces.Where(x => x == 0).All(x => terminal.exterior.ForFace(x).cladding == Color.red));
            var copy = terminal.Clone(); copy.exterior.Override(0).cladding = Color.blue; copy.SetStoreyHeight(0, 15f);
            Assert.AreEqual(Color.red, terminal.exterior.ForFace(0).cladding); Assert.AreEqual(Terminal.GroundStorey, terminal.StoreyHeight(0));
            var head = new BridgeHead(); head.exterior.Override(0).cladding = Color.red;
            var mirrored = head.Clone(); mirrored.exterior.MirrorHead();
            Assert.AreEqual(Color.red, mirrored.exterior.ForFace(2).cladding);
            Assert.AreEqual(Color.red, head.exterior.ForFace(0).cladding);
            copy.exterior.facade.lean = -30f;
            StringAssert.Contains("facade", copy.EnvelopeProblem());
        }

        [Test]
        public void DifferentlyLeaningFacades_JoinTangentialCurvesWithoutMitreSpikes()
        {
            var terminal = Terminal.Polygon(new[] { new Vector2(190, 35), new Vector2(680, 35), new Vector2(680, 76),
                new Vector2(345.5f, 76), new Vector2(345.5f, 111), new Vector2(190, 111) });
            terminal.bulge = new List<float> { 8f, 20.5f, 0f, 0f, 0f, 38f };
            terminal.storeyHeights = new List<float> { 5f, 12f };
            terminal.exterior.Override(0).lean = 12f;
            Assert.IsNull(terminal.EnvelopeProblem());
            var top = terminal.WallOutline(terminal.Height);
            Assert.Greater(top.Min(p => p.x), 140f); Assert.Less(top.Max(p => p.x), 730f);
        }

        [Test]
        public void RuntimeTerminalDesigner_WorksWithoutAStandAndRefusesInvalidProfiles()
        {
            var (airport, _, terminal) = TestAirport();
            airport.Taxiways.stands.Clear(); airport.Taxiways.Rebuild();
            using var designer = new DevGateDesigner();
            designer.Open(airport, null); designer.SelectTerminal(terminal);
            Assert.IsTrue(designer.IsOpen); Assert.IsNull(designer.Stand);
            Assert.IsTrue(designer.EditTerminal(terminal, t => t.SetStoreyHeight(1, 12f)), designer.Message);
            Assert.AreEqual(Terminal.GroundStorey + 12f, terminal.RoofHeight);
            Assert.IsFalse(designer.EditTerminal(terminal, t => t.SetStoreyHeight(1, -2f)));
            Assert.AreEqual(Terminal.GroundStorey + 12f, terminal.RoofHeight, "a rejected change must keep the last valid building");
        }

        [Test]
        public void BranchHead_ConnectsToTheSlantedParentAtItsExplicitBoardingHeight()
        {
            var stand = new Stand(); var sl = new StandLayout { stand = stand, axis = Vector2.up, nose = Vector2.up };
            var terminal = Terminal.Polygon(new[] { new Vector2(-10, -10), new Vector2(10, -10), new Vector2(10, 0), new Vector2(-10, 0) });
            stand.head = new BridgeHead { from = new Vector2(0, 10), to = new Vector2(0, 30), root = Vector2.zero, upperFloor = 8f };
            stand.head.exterior.Override(0).lean = 30f;
            float x = JetBridgeRules.HeadWidth * 0.5f + Mathf.Tan(30f * Mathf.Deg2Rad) * 8f;
            stand.additionalHeads.Add(new BridgeHead { from = new Vector2(20, 20), to = new Vector2(20, 40),
                root = new Vector2(x, 20), floor = 8f, connectionFloor = 8f });
            var heads = HeadLayout.All(sl, new[] { terminal });
            Assert.IsTrue(heads[1].Connected); Assert.AreSame(heads[0], heads[1].parent);
            Assert.AreEqual(8f, heads[1].ConnectionFloor);
        }
    }
}
