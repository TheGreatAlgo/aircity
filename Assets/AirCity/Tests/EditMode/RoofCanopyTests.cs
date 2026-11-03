using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    public partial class TerminalBridgeTests
    {
        [TestCase(RoofForm.Wave, 25f, false)]
        [TestCase(RoofForm.Scalloped, 25f, true)]
        [TestCase(RoofForm.Wave, -20f, true)]
        [TestCase(RoofForm.Scalloped, -20f, false)]
        public void CurvedRoof_RoundedAngledFacadesStayBoundedAndRoofTrianglesFaceUp(RoofForm form, float angle, bool across)
        {
            var (airport, _, _) = TestAirport(terminal: false);
            var center = new Vector2(10000f, -10000f);
            var terminal = airport.AddTerminal(new[] { center + new Vector2(-60f, -15f), center + new Vector2(60f, -15f), center + new Vector2(60f, 15f), center + new Vector2(-60f, 15f) });
            var copy = terminal.Clone(); copy.SetBulge(1, 15f); copy.SetBulge(3, 15f);
            FacadeStyles.Apply(copy.exterior, FacadeStyle.Framed);
            copy.exterior.facade.lean = angle; copy.exterior.columnGap = 2f;
            copy.exterior.roofShape.form = form; copy.exterior.roofShape.rise = 6f; copy.exterior.roofShape.across = across;
            copy.exterior.roofShape.overhang = 3f;
            Assert.IsTrue(airport.ChangeTerminal(terminal, copy), airport.TerminalProblem(copy, terminal));
            var mesh = airport.TerminalMesh; var vertices = mesh.vertices; var normals = mesh.normals; var colors = mesh.colors;
            var uv = new List<Vector4>(); var faces = new List<Vector4>(); mesh.GetUVs(0, uv); mesh.GetUVs(1, faces);
            Assert.IsTrue(vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)));
            float reach = terminal.Height * Mathf.Tan(30f * Mathf.Deg2Rad) + terminal.exterior.RoofOverhang + 2f;
            Assert.IsTrue(vertices.All(v => Mathf.Abs(v.x - center.x) < 75f + reach && Mathf.Abs(v.z - center.y) < 15f + reach), "nearly parallel facade samples must never produce long spikes");
            var roof = Enumerable.Range(0, vertices.Length).Where(i => colors[i] == terminal.exterior.roof.linear && Mathf.Approximately(uv[i].y, 0.2f)).ToList();
            Assert.Greater(roof.Count, 50);
            Assert.IsTrue(roof.All(i => normals[i].y > 0.01f), "roof projection must not fold its triangles underneath itself: " +
                string.Join(", ", roof.Where(i => normals[i].y <= 0.01f).Take(6).Select(i => vertices[i] + " normal=" + normals[i])));
            var profile = terminal.exterior.roofShape.For(terminal);
            foreach (int i in roof)
                Assert.AreEqual(terminal.RoofHeight + profile.Lift(new Vector2(vertices[i].x, vertices[i].z)), vertices[i].y, 0.015f,
                    "the roof must follow one continuous height field through angled corners");
            var curvedGlass = Enumerable.Range(0, vertices.Length).Where(i => uv[i].x > 0.9f && Mathf.Approximately(faces[i].z, 1f));
            foreach (int i in curvedGlass)
            {
                float radius = Vector2.Distance(new Vector2(vertices[i].x, vertices[i].z), center + new Vector2(60f, 0f));
                Assert.AreEqual(15f + Mathf.Tan(angle * Mathf.Deg2Rad) * vertices[i].y, radius, 0.15f, "glass must follow the circular facade, including at dense roof cuts");
            }
        }

        [TestCase(RoofForm.Flat)]
        [TestCase(RoofForm.Wave)]
        public void Canopy_ColumnsStandClearOfGlassAndRoofHasAnUnderside(RoofForm form)
        {
            var (airport, _, _) = TestAirport(terminal: false);
            var terminal = airport.AddTerminal(new[] { new Vector2(200f, -700f), new Vector2(280f, -700f), new Vector2(280f, -670f), new Vector2(200f, -670f) });
            var copy = terminal.Clone(); FacadeStyles.Apply(copy.exterior, FacadeStyle.Framed);
            copy.exterior.columnGap = 2f; copy.exterior.roofShape.overhang = 3f; copy.exterior.roofShape.form = form;
            Assert.IsTrue(airport.ChangeTerminal(terminal, copy), airport.TerminalProblem(copy, terminal));
            var mesh = airport.TerminalMesh; var vertices = mesh.vertices; var colors = mesh.colors; var normals = mesh.normals;
            float Outside(Vector3 p) => Mathf.Max(200f - p.x, p.x - 280f, -700f - p.z, p.z + 670f);
            var feet = Enumerable.Range(0, vertices.Length).Where(i => vertices[i].y == 0f && colors[i] == terminal.exterior.facade.cladding.linear).ToList();
            Assert.Greater(feet.Count, 0);
            Assert.IsTrue(feet.All(i => Outside(vertices[i]) >= 1.98f && Outside(vertices[i]) <= 2.6f));
            Assert.IsTrue(Enumerable.Range(0, vertices.Length).Any(i => Outside(vertices[i]) > 2.9f && normals[i].y < -0.1f && vertices[i].y > terminal.RoofHeight - 0.3f), "the eave needs a real soffit, not a single-sided ribbon");
            terminal.ClosestOnWall(new Vector2(240f, -660f), out var wall, out _);
            Assert.AreEqual(-670f, wall.y, 0.001f, "boarding connections must meet the glass, not the canopy edge");
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(210f, 0f, -666.9f)), b = net.AddNode(new Vector3(270f, 0f, -666.9f));
            net.Connect(a, b, 1f, PavementSurface.Asphalt, false, RoadClass.Service);
            StringAssert.Contains("road", airport.TerminalProblem(terminal, terminal));
        }
    }
}
