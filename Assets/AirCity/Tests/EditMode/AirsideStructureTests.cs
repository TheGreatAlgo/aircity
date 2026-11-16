using System.Linq;
using AirCity.Airfield;
using AirCity.Game;
using AirCity.Vehicles;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Game
{
    public class AirsideStructureTests
    {
        GameObject root;
        [TearDown]public void Cleanup(){if(root!=null)Object.DestroyImmediate(root);}
        Airport Airport(out CareerState state)
        {
            state=new CareerState {mode=GameMode.Career,funds=1000000,wholeRegion=true,handsOn=true,layout=new CareerLayout(),world=new ValleyWorld {heights=Enumerable.Repeat(100f,ValleyWorld.Resolution*ValleyWorld.Resolution).ToArray()}};
            state.world.sites.Add(new AirportSite {centre=Vector2.zero,elevation=100});
            root=new GameObject("3D road fixture");root.SetActive(false);root.transform.position=Vector3.up*100;
            var airport=root.AddComponent<Airport>();airport.groundSize=0;airport.TerrainHeight=_=>0;root.SetActive(true);return airport;
        }
        [Test]public void TunnelUnderTaxiwayKeepsIndependentRoutingTerrainAndVehicleElevation()
        {
            var airport=Airport(out var state);var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(0,0,-150)),b=net.AddNode(new Vector3(0,0,150));net.Connect(a,b,15,PavementSurface.Asphalt);net.Rebuild();
            int start=net.AddNode(new Vector3(-200,-10,0));state.world.trees.Add(new ForestTree {position=new Vector2(50,0),height=12});
            var before=(float[])state.world.heights.Clone();var options=new RoadBuildSettings {mode=RoadBuildMode.Tunnel,smooth=true};
            Assert.IsTrue(AirsideTerrainPlan.TryCreate(state,airport,start,new Vector3(200,-10,0),-1,null,null,7,PavementSurface.Asphalt,RoadClass.Service,out var plan,out var why,null,options),why);
            Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();
            CollectionAssert.AreEqual(before,state.world.heights);Assert.IsFalse(state.world.trees[0].removed);
            Assert.AreEqual(2,net.edges.Count);Assert.IsNull(net.FindVehicleRoute(a,start),"A crossing cannot connect different elevations.");
            Assert.AreEqual(0,airport.PavementElevationAt(Vector2.zero),.001f);Assert.AreEqual(-10,airport.RoadElevationAt(Vector2.zero,-10),.001f);
            using var mask=new BuiltSurfaceMap();foreach(var triangle in net.GroundOwnership)mask.AddTriangle(triangle);
            Assert.IsFalse(mask.Contains(new Vector2(50,0)),"Covered tunnels cannot own their roof terrain.");
            Assert.IsTrue(BaggageRoute.Plan(net,start,plan.EndNode,8,8,2,out var route,out why),why);
            Assert.AreEqual(-10,route.Height(route.Length*.5f),.001f);
            net.RefreshEquipmentPaint();
            var paint=net.PaintMesh.vertices.Where(p=>p.x>30&&p.x<100&&Mathf.Abs(p.z)<4).ToArray();
            Assert.IsNotEmpty(paint);Assert.IsTrue(paint.All(p=>p.y< -9.9f),"Buried road markings must follow their own deck.");
        }
        [Test]public void ShallowUnderpassRefusesWithoutEditingOrCharging()
        {
            var airport=Airport(out var state);var net=airport.Taxiways;
            net.Connect(net.AddNode(new Vector3(0,0,-150)),net.AddNode(new Vector3(0,0,150)),15,PavementSurface.Asphalt);net.Rebuild();int start=net.AddNode(new Vector3(-200,-3,0));
            string before=JsonUtility.ToJson(net);var options=new RoadBuildSettings {mode=RoadBuildMode.Tunnel,smooth=true};
            Assert.IsFalse(AirsideTerrainPlan.TryCreate(state,airport,start,new Vector3(200,-3,0),-1,null,null,7,PavementSurface.Asphalt,RoadClass.Service,out _,out _,null,options));
            Assert.AreEqual(before,JsonUtility.ToJson(net));Assert.AreEqual(1000000,state.funds);
        }
        [Test]public void BridgeKeepsLowCanopiesAndTerrainButClearsTreesEnteringItsDeck()
        {
            var airport=Airport(out var state);var net=airport.Taxiways;int start=net.AddNode(new Vector3(-200,12,0));
            state.world.trees.Add(new ForestTree {position=new Vector2(-40,0),height=4});state.world.trees.Add(new ForestTree {position=new Vector2(40,0),height=20});
            var before=(float[])state.world.heights.Clone();var options=new RoadBuildSettings {mode=RoadBuildMode.Bridge,smooth=true};
            Assert.IsTrue(AirsideTerrainPlan.TryCreate(state,airport,start,new Vector3(200,12,0),-1,null,null,7,PavementSurface.Asphalt,RoadClass.Service,out var plan,out var why,null,options),why);
            Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();CollectionAssert.AreEqual(before,state.world.heights);
            Assert.IsFalse(state.world.trees[0].removed);Assert.IsTrue(state.world.trees[1].removed);
            Assert.AreEqual(12,airport.RoadElevationAt(Vector2.zero,12),.001f);
        }
        [Test]public void SpatialVehicleFootprintsIgnoreOtherDecksButKeepSameLevelClearance()
        {
            var bounds=new Bounds(new Vector3(0,1,0),new Vector3(2,2,5));
            var tunnel=AirportMaintenance.Box.Of(Vector2.zero,Vector2.up,bounds,.3f,-10);
            var surface=AirportMaintenance.Box.Of(Vector2.zero,Vector2.up,bounds,.3f,0);
            var beside=AirportMaintenance.Box.Of(new Vector2(1,0),Vector2.up,bounds,.3f,-10);
            Assert.IsFalse(tunnel.Overlaps(surface));Assert.IsTrue(tunnel.Overlaps(beside));
            Assert.IsTrue(tunnel.Overlaps(AirportMaintenance.Box.Of(Vector2.zero,Vector2.up,bounds,.3f)),"Legacy purely planar geometry retains its original meaning.");
        }
        [Test]public void MaintenanceRouteUsesTunnelHeightBelowParkedAircraft()
        {
            var airport=Airport(out _);var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-150,-10,0)),b=net.AddNode(new Vector3(150,-10,0));
            net.Connect(a,b,7,PavementSurface.Asphalt,false,RoadClass.Service,false,RoadStructure.Tunnel);net.Rebuild();
            var type=AirCity.EditorTools.Aircraft.AircraftSetup.AllTypes().First(t=>t.model=="Narrowbody");
            var prefab=AirCity.EditorTools.Aircraft.AircraftSetup.Prefab(type);
            var plane=Object.Instantiate(prefab,airport.transform);plane.transform.localPosition=Vector3.zero;
            var vehicle=AirCity.EditorTools.Vehicles.VehicleSetup.Type("SnowPlough");
            var blocked=airport.Maintenance.RouteBlocked(vehicle);Assert.IsNotNull(blocked);
            Assert.IsTrue(blocked(new Vector2(-150,0),new Vector2(150,0)),"The ground route crosses the parked aircraft.");
            Assert.IsNotNull(net.VehicleRoute(new[]{(a,0f)},new[]{b},blocked),"The tunnel must remain usable beneath the aircraft.");
        }
        [Test]public void NewRunwayAllowsDeepUnderpassButRefusesBridgeAboveItsCorridor()
        {
            var airport=Airport(out var state);var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-200,-10,0)),b=net.AddNode(new Vector3(200,-10,0));
            var road=net.Connect(a,b,7,PavementSurface.Asphalt,false,RoadClass.Service,false,RoadStructure.Tunnel).Single();net.Rebuild();
            var runway=airport.AddRunway(new Vector3(0,0,-250),new Vector3(0,0,250),PavementSurface.Grass,18);
            Assert.IsTrue(RunwayTerrainPlan.TryCreate(state,airport,runway,null,out _,out var why),why);
            road.structure=RoadStructure.Elevated;net.Node(a).position.y=net.Node(b).position.y=12;net.Rebuild();
            Assert.IsFalse(RunwayTerrainPlan.TryCreate(state,airport,runway,null,out _,out why));
            StringAssert.Contains("bridges",why);
        }
    }
}
