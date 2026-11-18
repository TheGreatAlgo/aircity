using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public sealed class VisitorCentreProtectionTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown] public void Cleanup()
        {foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();}

        static CareerState State(bool built=true)
        {
            var world=new ValleyWorld {heights=Enumerable.Repeat(100f,ValleyWorld.Resolution*ValleyWorld.Resolution).ToArray()};
            world.sites.Add(new AirportSite {name="Visitor protection fixture",centre=Vector2.zero,elevation=100f});
            return new CareerState {world=world,selectedSite=0,wholeRegion=true,handsOn=true,funds=1000000,
                layout=new CareerLayout {elevation=100f,receptionBuilt=built,reception=Vector2.zero,receptionSiteVersion=1},
                publicRoads=new PublicRoadNetwork()};
        }
        Airport Airport()
        {
            var root=new GameObject("Visitor protection airport");root.SetActive(false);owned.Add(root);
            root.transform.position=Vector3.up*100f;
            var airport=root.AddComponent<Airport>();airport.groundSize=0f;root.SetActive(true);return airport;
        }
        static void Refused(string problem)
        {Assert.IsNotNull(problem);StringAssert.Contains("visitor",problem.ToLowerInvariant());}

        [TestCase(0f)] [TestCase(28f)]
        public void PublicRoadCannotCrossTheBuildingOrParkingRowsAndRefusalIsAtomic(float z)
        {
            var state=State();var graph=state.publicRoads;
            int from=graph.AddNode(new Vector3(-80f,100f,z));
            string before=JsonUtility.ToJson(graph);var heights=(float[])state.world.heights.Clone();
            Assert.IsFalse(RoadConstructionPlan.TryCreate(state,graph,from,new Vector3(80f,100f,z),-1,-1,
                PublicRoadStyle.Urban,new RoadBuildSettings {mode=RoadBuildMode.Ground},out var plan,out var why));
            Refused(why);Assert.IsNull(plan);Assert.AreEqual(before,JsonUtility.ToJson(graph));
            Assert.AreEqual(1000000,state.funds);CollectionAssert.AreEqual(heights,state.world.heights);
        }

        [Test]
        public void PublicRoadMayJoinTheReservedDrivewayOpeningWithoutCuttingAcrossParking()
        {
            var state=State();var graph=state.publicRoads;
            int from=graph.AddNode(new Vector3(17.5f,100f,21.5f),"airport-visitor-centre");
            string before=JsonUtility.ToJson(graph);
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,graph,from,new Vector3(90f,100f,21.5f),-1,-1,
                PublicRoadStyle.AirportAccess,new RoadBuildSettings {mode=RoadBuildMode.Ground},out var plan,out var why),why);
            Assert.AreEqual(before,JsonUtility.ToJson(graph));Assert.AreEqual(1000000,state.funds);
            Assert.IsTrue(plan.TryCommit(state,graph,out var connected,out why),why);
            Assert.IsTrue(connected.HasRoute(from,plan.EndNode));Assert.AreEqual(1000000-plan.Price,state.funds);
        }

        [TestCase(RoadClass.None)] [TestCase(RoadClass.Service)]
        public void TaxiwayAndServiceRoadCannotCrossTheCarPark(RoadClass road)
        {
            var state=State();var airport=Airport();var net=airport.Taxiways;
            int from=net.AddNode(new Vector3(-80f,0f,27f));
            string before=JsonUtility.ToJson(net);var heights=(float[])state.world.heights.Clone();
            Assert.IsFalse(AirsideTerrainPlan.TryCreate(state,airport,from,new Vector3(80f,0f,27f),-1,null,null,
                7.5f,PavementSurface.Asphalt,road,out var plan,out var why));
            Refused(why);Assert.IsNull(plan);Assert.AreEqual(before,JsonUtility.ToJson(net));
            Assert.AreEqual(1000000,state.funds);CollectionAssert.AreEqual(heights,state.world.heights);
        }

        [Test]
        public void StandAndRunwayCannotOccupyTheVisitorCentreSite()
        {
            var state=State();var airport=Airport();
            var stand=new Stand {position=Vector3.zero,heading=0f,code=SizeCode.A,surface=PavementSurface.Concrete};
            stand.Frame(Vector2.right,7.5f);
            stand.position+=new Vector3(0f,0f,27f)-Geo.X0Z(StandLayout.Of(stand).Centre);
            Assert.IsFalse(AirsideTerrainPlan.TryCreateStand(state,airport,StandLayout.Of(stand),out _,out var why));
            Refused(why);Assert.IsEmpty(airport.Taxiways.stands);
            var runway=airport.AddRunway(new Vector3(-150f,0f,27f),new Vector3(150f,0f,27f),PavementSurface.Grass,18f);
            var heights=(float[])state.world.heights.Clone();
            Assert.IsFalse(RunwayTerrainPlan.TryCreate(state,airport,runway,null,out _,out why));
            Refused(why);Assert.AreEqual(1000000,state.funds);CollectionAssert.AreEqual(heights,state.world.heights);
        }

        [Test]
        public void AQuotedPublicRoadCannotOverwriteAVisitorCentreBuiltBeforeCommit()
        {
            var state=State(false);var graph=state.publicRoads;int from=graph.AddNode(new Vector3(-80f,100f,28f));
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,graph,from,new Vector3(80f,100f,28f),-1,-1,
                PublicRoadStyle.Rural,new RoadBuildSettings {mode=RoadBuildMode.Ground},out var plan,out var why),why);
            string before=JsonUtility.ToJson(graph);state.layout.receptionBuilt=true;
            Assert.IsFalse(plan.TryCommit(state,graph,out var result,out why));Refused(why);
            Assert.AreSame(graph,result);Assert.AreEqual(before,JsonUtility.ToJson(graph));Assert.AreEqual(1000000,state.funds);
        }

        [Test]
        public void AQuotedTaxiwayCannotOverwriteAVisitorCentreBuiltBeforeCommit()
        {
            var state=State(false);var airport=Airport();var net=airport.Taxiways;
            int from=net.AddNode(new Vector3(-80f,0f,27f));
            Assert.IsTrue(AirsideTerrainPlan.TryCreate(state,airport,from,new Vector3(80f,0f,27f),-1,null,null,
                7.5f,PavementSurface.Asphalt,RoadClass.None,out var plan,out var why),why);
            var heights=(float[])state.world.heights.Clone();string before=JsonUtility.ToJson(net);state.layout.receptionBuilt=true;
            Assert.IsFalse(plan.TryCommit(state,out why));Refused(why);
            Assert.AreEqual(before,JsonUtility.ToJson(net));Assert.AreEqual(1000000,state.funds);CollectionAssert.AreEqual(heights,state.world.heights);
        }

        [Test]
        public void AQuotedRunwayCannotOverwriteAVisitorCentreBuiltBeforeCommit()
        {
            var state=State(false);var airport=Airport();
            var runway=airport.AddRunway(new Vector3(-150f,0f,27f),new Vector3(150f,0f,27f),PavementSurface.Grass,18f);
            Assert.IsTrue(RunwayTerrainPlan.TryCreate(state,airport,runway,null,out var plan,out var why),why);
            var heights=(float[])state.world.heights.Clone();state.layout.receptionBuilt=true;
            Assert.IsFalse(plan.TryCommit(state,1000,out _,out why));Refused(why);
            Assert.AreEqual(1000000,state.funds);CollectionAssert.AreEqual(heights,state.world.heights);
        }
    }
}
