using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Airfield;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.Tests
{
    public sealed class BaggageBuildTests
    {
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        (Airport airport,DevAirfieldTool tool,DevBaggageControls controls) Create(bool paved=true)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var root=new GameObject("Empty player airport"); root.SetActive(false);
            var airport=root.AddComponent<Airport>(); airport.groundSize=0; AirfieldSetup.AssignMaterials(airport);
            if (paved) airport.aprons.Add(ApronArea.Rect(Vector2.zero,new Vector2(1000,1000),0,PavementSurface.Concrete));
            root.SetActive(true);
            var net=airport.Taxiways; int a=net.AddNode(new Vector3(0,0,-250)),b=net.AddNode(new Vector3(0,0,250));
            net.Connect(a,b,12,PavementSurface.Concrete,false,RoadClass.Service);
            var camera=new GameObject("Player camera"); camera.AddComponent<Camera>();
            var tool=camera.AddComponent<DevAirfieldTool>(); tool.airport=airport;
            return (airport,tool,camera.AddComponent<DevBaggageControls>());
        }

        [TestCase(BaggageDepotSize.Small)]
        [TestCase(BaggageDepotSize.Medium)]
        [TestCase(BaggageDepotSize.Large)]
        public void PlayerCanBuildFirstHallWithEquipmentAndSelectIt(BaggageDepotSize size)
        {
            var (airport,tool,controls)=Create(); Assert.IsEmpty(airport.Baggage.Stations);
            controls.BeginHall(size); controls.Place(new Vector3(2,0,11));
            var hall=tool.SelectedBaggageHall; Assert.IsNotNull(hall,controls.Message);
            Assert.AreEqual(DevTool.Select,tool.Tool); Assert.AreEqual(size,hall.size);
            Assert.AreEqual(new Vector2(0,11),hall.firstBay,"snaps to a segment, not just its end nodes");
            var team=hall.Teams.Single(); Assert.AreSame(team,controls.Station);
            Assert.IsNotNull(team.tractorPrefab); Assert.IsNotNull(team.loaderPrefab); Assert.IsNotNull(team.crewType);
            Assert.IsEmpty(team.standIds,"a new hall never inherits another team's stand assignments");
            Assert.AreEqual(2,team.cartCount); Assert.AreEqual(4,hall.hiredWorkers); Assert.IsTrue(team.automaticHandling);
            Assert.IsTrue(hall.connection.enabled); Assert.IsNull(hall.ConnectedTerminal);
            Assert.IsNull(hall.PlacementProblem());
            var fleet=airport.Baggage.Fleet(team); Assert.IsNotNull(fleet.Train); Assert.AreEqual(2,fleet.Train.CartCount);
        }

        [Test]
        public void PreviewDoesNotMutateAirportAndFlipChangesRoadSide()
        {
            var (airport,_,controls)=Create(); int nodes=airport.Taxiways.nodes.Count,edges=airport.Taxiways.edges.Count;
            controls.BeginHall(BaggageDepotSize.Medium); controls.Preview(new Vector3(2,0,11));
            Assert.IsTrue(controls.HasPreview); Assert.IsNull(controls.PreviewProblem);
            var first=BaggageDepot.Site(controls.depotSize,controls.PreviewOrigin,controls.PreviewBearing);
            controls.FlipSite(); controls.Preview(new Vector3(2,0,11));
            var flipped=BaggageDepot.Site(controls.depotSize,controls.PreviewOrigin,controls.PreviewBearing);
            Assert.Greater(first.centre.x,0); Assert.Less(flipped.centre.x,0);
            Assert.AreEqual(nodes,airport.Taxiways.nodes.Count); Assert.AreEqual(edges,airport.Taxiways.edges.Count);
            Assert.IsEmpty(airport.GetComponentsInChildren<BaggageDepot>()); Assert.IsEmpty(airport.Baggage.Stations);
            controls.Place(new Vector3(2,0,11)); Assert.Less(controls.Station.depot.PaintArea.centre.x,0);
        }

        [Test]
        public void InvalidSitesDoNotCreateHallsTeamsOrRoads()
        {
            var (airport,tool,controls)=Create(false); int nodes=airport.Taxiways.nodes.Count,edges=airport.Taxiways.edges.Count;
            controls.BeginHall(BaggageDepotSize.Large); controls.Place(new Vector3(1,0,0));
            Assert.IsNotEmpty(controls.Message); Assert.IsNull(tool.SelectedBaggageHall);
            Assert.IsEmpty(airport.GetComponentsInChildren<BaggageDepot>()); Assert.IsEmpty(airport.Baggage.Stations);
            Assert.AreEqual(nodes,airport.Taxiways.nodes.Count); Assert.AreEqual(edges,airport.Taxiways.edges.Count);
            controls.Place(new Vector3(100,0,0)); StringAssert.Contains("15 m",controls.Message);
        }

        [TestCase(BaggagePlacement.Underground)]
        [TestCase(BaggagePlacement.Covered)]
        public void HallLinkToolConnectsClickedBuildingsAndRejectsInvalidTargets(BaggagePlacement placement)
        {
            var (airport,tool,controls)=Create(); controls.BeginHall(BaggageDepotSize.Small); controls.Place(Vector3.zero);
            var hall=tool.SelectedBaggageHall;
            var terminal=Terminal.Polygon(new[] {new Vector2(100,-10),new Vector2(120,-10),new Vector2(120,10),new Vector2(100,10)},3);
            airport.terminals.Add(terminal);
            controls.BeginPlacement(placement);
            controls.Place(new Vector3(200,0,200)); Assert.IsNotEmpty(controls.Message); Assert.IsNull(hall.ConnectedTerminal);
            controls.Place(new Vector3(hall.PaintArea.centre.x,0,hall.PaintArea.centre.y)); Assert.AreSame(hall,controls.LinkSource);
            controls.Place(new Vector3(110,0,0));
            Assert.AreSame(terminal,hall.ConnectedTerminal); Assert.AreSame(hall,tool.SelectedBaggageHall); Assert.AreEqual(DevTool.Select,tool.Tool);
            Assert.IsNotNull(airport.Baggage.TerminalLink(hall));
        }

        [Test]
        public void PlayerEquipmentTeamsRespectHallCapacityAndStartUnassigned()
        {
            var (_,tool,controls)=Create(); controls.BeginHall(BaggageDepotSize.Small); controls.Place(Vector3.zero);
            var hall=tool.SelectedBaggageHall; controls.Station.standIds.Add(999);
            var catalog=BaggageBuildCatalog.Load(); var next=catalog.AddTeam(hall,out var why);
            Assert.IsNotNull(next,why); Assert.IsEmpty(next.standIds); Assert.AreNotEqual(controls.Station.depotBay,next.depotBay);
            Assert.IsNull(catalog.AddTeam(hall,out why)); Assert.IsNotEmpty(why); Assert.AreEqual(4,hall.Trailers);
        }

        [Test]
        public void ToolbarOpensBaggageOnAnAirportWithNoExistingStation()
        {
            var (_,tool,controls)=Create(); var toolbar=tool.gameObject.AddComponent<DevToolbar>();
            toolbar.OpenBaggageBuilder(); Assert.AreEqual(DevTool.Baggage,tool.Tool);
            Assert.AreEqual(BaggagePlacement.Depot,controls.Placement);
            controls.Cancel(); Assert.AreEqual(DevTool.Select,tool.Tool); Assert.IsNull(controls.LinkSource);
        }

        [Test]
        public void DemolitionKeepsStoredBagsAndReleasesEmptyEquipmentFromSimulation()
        {
            var (airport,tool,controls)=Create(); controls.BeginHall(BaggageDepotSize.Small); controls.Place(Vector3.zero);
            var hall=tool.SelectedBaggageHall; var fleet=airport.Baggage.Fleet(controls.Station);
            var batch=airport.Baggage.Ledger.Add("STORED","ARRIVAL",1,15,true,fleet.Arrivals);
            Assert.IsFalse(airport.Baggage.ReleaseHall(hall,out var why)); Assert.IsNotEmpty(why);
            Assert.AreEqual(1,airport.Baggage.Ledger.Count);
            Assert.IsTrue(airport.Baggage.Ledger.Transfer(batch,fleet.Arrivals,airport.Baggage.Reclaim,out why),why);
            Assert.IsTrue(airport.Baggage.ReleaseHall(hall,out why),why);
            foreach (var team in hall.Teams) Object.DestroyImmediate(team.gameObject);
            Object.DestroyImmediate(hall.gameObject);
            airport.Baggage.Simulate(.1f);
            Assert.IsEmpty(airport.Baggage.Stations); Assert.IsEmpty(airport.Baggage.VehicleBoxes());
            Assert.AreEqual(1,airport.Baggage.Ledger.Count);
        }
    }
}
