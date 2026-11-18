using System.Linq;
using System.Reflection;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.EditorTools.Airfield;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.Tests
{
    public sealed class HangarBuildTests
    {
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);

        (Airport,DevAirfieldTool) Create()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var root=new GameObject("Player hangar airport"); root.SetActive(false);
            var airport=root.AddComponent<Airport>(); airport.groundSize=0; AirfieldSetup.AssignMaterials(airport);
            root.SetActive(true);
            var camera=new GameObject("Player camera"); camera.AddComponent<Camera>();
            var tool=camera.AddComponent<DevAirfieldTool>(); tool.airport=airport;
            return (airport,tool);
        }

        [Test]
        public void PlayerCanRotateBuildSelectAndDemolishHangar()
        {
            var (airport,tool)=Create();
            tool.SetTool(DevTool.Hangar); Assert.IsTrue(tool.SupportsPlacementRotation);
            tool.RotatePlacement(90);
            Assert.IsTrue(tool.PlaceHangar(new Vector3(40,0,50)),tool.HangarProblem);
            var hangar=airport.Hangars.Single();
            Assert.AreSame(hangar,tool.SelectedHangar); Assert.AreEqual(DevTool.Select,tool.Tool);
            Assert.AreEqual(new Vector2(40,50),hangar.Door); Assert.AreEqual(90,hangar.Heading,.01f);
            tool.DeleteSelection(); Assert.IsEmpty(airport.Hangars); Assert.IsNull(tool.SelectedHangar);
        }

        [Test]
        public void PreviewUsesRealBuildingWithoutCreatingSavedHangar()
        {
            var (airport,tool)=Create(); tool.SetTool(DevTool.Hangar);
            typeof(DevAirfieldTool).GetMethod("RefreshBuildPreview",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(tool,new object[] {true,new Vector3(40,0,50)});
            Assert.IsTrue(tool.PlacementPreviewVisible);
            Assert.IsEmpty(airport.Hangars);
            var meshes=tool.PlacementPreviewObject.GetComponentsInChildren<MeshFilter>();
            Assert.IsTrue(meshes.Any(f=>f.sharedMesh!=null && f.sharedMesh.vertexCount>0));
            tool.SetTool(DevTool.Select); Assert.IsFalse(tool.PlacementPreviewVisible);
        }

        [Test]
        public void HangarPreviewAndPlacementAlignToADiagonalTaxiway()
        {
            var (airport,tool) = Create();
            var along = new Vector2(2f, 1f).normalized;
            var facing = Geo.Left(along);
            var net = airport.Taxiways;
            int a = net.AddNode(Geo.X0Z(-along * 60f)), b = net.AddNode(Geo.X0Z(along * 60f));
            net.Connect(a, b, 7.5f, PavementSurface.Asphalt);
            var door = -facing * 14.25f;
            var cursor = Geo.X0Z(door + facing * 2f);
            tool.SetTool(DevTool.Hangar);
            typeof(DevAirfieldTool).GetMethod("RefreshBuildPreview", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(tool, new object[] { true, cursor });
            Assert.Less(Vector2.Distance(door, Geo.XZ(tool.PlacementPreviewObject.transform.position)), .01f);
            Assert.IsTrue(tool.PlaceHangar(cursor), tool.HangarProblem);
            var hangar = airport.Hangars.Single();
            Assert.Less(Vector2.Distance(door, hangar.Door), .01f);
            Assert.Less(Vector2.Angle(facing, hangar.Axis), .01f);
            Assert.IsTrue(HangarConnection.Approach(hangar, out _));

            tool.SetTool(DevTool.Hangar);
            tool.PlacementSnap = false;
            cursor = Geo.X0Z(door + along * 30f + facing * 2f);
            Assert.IsTrue(tool.PlaceHangar(cursor), tool.HangarProblem);
            Assert.AreEqual(Geo.XZ(cursor), tool.SelectedHangar.Door);
        }

        [Test]
        public void ExecutivePlacementSnapsItsThreeSpotsAndBuildsOneApron()
        {
            var (airport, tool) = Create();
            var along = new Vector2(2f, 1f).normalized;
            var facing = Geo.Left(along);
            var net = airport.Taxiways;
            int a = net.AddNode(Geo.X0Z(-along * 85f)), b = net.AddNode(Geo.X0Z(along * 85f));
            var edge = net.Connect(a, b, 15f, PavementSurface.Asphalt).Single();
            var spec = HangarSpec.For(HangarSize.Executive);
            var door = -facing * (spec.Setback + edge.width * .5f);
            tool.SetTool(DevTool.Hangar);
            tool.SetHangarSize(HangarSize.Executive);
            Assert.IsTrue(tool.PlaceHangar(Geo.X0Z(door + facing * 2f)), tool.HangarProblem);
            var root = tool.SelectedHangar;
            Assert.Less(Vector2.Distance(door, root.Door), .01f);
            var bays = airport.Hangars.OrderBy(bay => bay.BayNumber).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, bays.Select(bay => bay.BayNumber));
            foreach (var bay in bays)
            {
                Assert.AreSame(root, bay.BuildingRoot);
                Assert.IsTrue(HangarConnection.TryGet(bay, null, out var layout, out var problem), problem);
                Assert.AreSame(edge, layout.edge);
                Assert.IsTrue(HangarConnection.Approach(bay, out var actual));
                Assert.IsTrue(HangarConnection.Approach(root, out var expected));
                CollectionAssert.AreEqual(expected, actual);
            }
            Assert.AreEqual(1, root.GetComponentsInChildren<LODGroup>().Length);
            Assert.AreEqual(1, root.GetComponentsInChildren<MeshFilter>().Count(filter => filter.name == "Generated hangar approach"));
            var lod = root.GetComponentInChildren<LODGroup>().GetLODs()[0];
            var material = lod.renderers[0].sharedMaterial;
            Assert.IsTrue(material.IsKeywordEnabled("_HANGAR_SURFACE"));
            Assert.IsNotNull(material.GetTexture("_HangarSurface"));
            Assert.IsNotNull(airport.HangarProblem(bays[0].Door, root.Heading));
            tool.SelectHangar(bays[2]);
            tool.DeleteSelection();
            Assert.IsEmpty(airport.Hangars);
        }

        [Test]
        public void ExecutiveAndAirlineInteriorLightsFollowTheNightSetting()
        {
            var (airport, _) = Create();
            float previous = AircraftLights.Glow;
            try
            {
                AircraftLights.Glow = 0f;
                var executive = airport.AddHangar(Vector2.zero, 0f, HangarSize.Executive);
                var airline = airport.AddHangar(new Vector2(120f, 0f), 0f, HangarSize.Airline);
                foreach (var hangar in new[] { executive, airline })
                {
                    var lights = hangar.GetComponentsInChildren<Light>();
                    Assert.AreEqual(12, lights.Length);
                    Assert.AreEqual(4, lights.Select(light => light.transform.localPosition.z).Distinct().Count());
                    Assert.IsTrue(lights.All(light => !light.enabled && light.type == LightType.Spot && light.shadows == LightShadows.None));
                }
                AircraftLights.Glow = 1f;
                executive.SendMessage("Update");
                airline.SendMessage("Update");
                Assert.IsTrue(airport.GetComponentsInChildren<Light>().All(light => light.enabled));
                executive.SetDoorsOpen(false);
                Assert.AreEqual(12, executive.GetComponentsInChildren<Light>().Count(light => light.enabled));
                AircraftLights.Glow = 0f;
                executive.SendMessage("Update");
                airline.SendMessage("Update");
                Assert.IsTrue(airport.GetComponentsInChildren<Light>().All(light => !light.enabled));
            }
            finally { AircraftLights.Glow = previous; }
        }

        [Test]
        public void RefusedOverlapKeepsExistingBuildingAndDoesNotSelectDraft()
        {
            var (airport,tool)=Create(); Assert.IsTrue(tool.PlaceHangar(Vector3.zero));
            var first=tool.SelectedHangar; tool.SetTool(DevTool.Hangar);
            Assert.IsFalse(tool.PlaceHangar(new Vector3(1,0,1)));
            Assert.IsNotEmpty(tool.HangarProblem); Assert.AreEqual(1,airport.Hangars.Count());
            Assert.AreSame(first,airport.Hangars.Single()); Assert.IsNull(tool.SelectedHangar);
        }
    }
}
