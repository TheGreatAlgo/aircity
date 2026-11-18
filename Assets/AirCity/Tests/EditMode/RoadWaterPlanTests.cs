using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    /// <summary>
    /// Water crossings at the plan and graph level (RoadConstructionPlan / AirsideTerrainPlan quotes committed in
    /// turn): over water the reference is the water surface, never the bed, so shallow margins are bridged rather than
    /// refused as ground road; bridge ends stand on the banks; tunnels need cover under the bed and say how much more.
    /// Land at 100 m, water at 99 m (low banks).
    /// </summary>
    public sealed class RoadWaterPlanTests
    {
        static float Piecewise(float d,params float[] k)
        {
            if(d<=k[0])return k[1];
            for(int i=2;i<k.Length;i+=2)if(d<=k[i])return Mathf.Lerp(k[i-1],k[i+1],(d-k[i-2])/(k[i]-k[i-2]));
            return k[k.Length-1];
        }
        /// <summary>A river along z at x = 0: 1.4 m deep, 25 m shallow margins, banks at 100 m.</summary>
        static float River(float x,float z)=>Piecewise(Mathf.Abs(x),37.5f,97.6f,62.5f,99f,75f,100f);
        static CareerState State(Func<float,float,float> land)
        {
            var world=new ValleyWorld {heights=new float[ValleyWorld.Resolution*ValleyWorld.Resolution],hasOcean=true,seaLevel=99f};
            world.sites.Add(new AirportSite {centre=Vector2.zero,elevation=100f});
            for(int z=0;z<ValleyWorld.Resolution;z++)for(int x=0;x<ValleyWorld.Resolution;x++)
                world.heights[z*ValleyWorld.Resolution+x]=land(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f);
            return new CareerState {mode=GameMode.Sandbox,funds=0,wholeRegion=true,handsOn=true,selectedSite=0,layout=new CareerLayout(),publicRoads=new PublicRoadNetwork(),world=world};
        }
        static PublicRoadSegment Street(PublicRoadNetwork g,CareerState s,float x)
        {
            var a=new Vector3(x,s.world.Height(new Vector2(x,-300)),-300);var b=new Vector3(x,s.world.Height(new Vector2(x,300)),300);
            var e=g.AddSegment(g.AddNode(a),g.AddNode(b),PublicRoadStyle.Urban,RoadStructure.Ground);e.alignment=RoadBuildSettings.Default.Shape(a,b);return e;
        }
        static RoadBuildSettings Mode(RoadBuildMode mode)=>new RoadBuildSettings {mode=mode,smooth=true};
        static int Step(CareerState state,ref PublicRoadNetwork graph,int from,Vector3 end,int endSegment,RoadBuildMode mode)
        {
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,graph,from,end,-1,endSegment,PublicRoadStyle.Urban,Mode(mode),out var plan,out var why),why);
            Assert.IsTrue(plan.TryCommit(state,graph,out var next,out why),why);graph=next;return plan.EndNode;
        }
        static int Start(PublicRoadNetwork g,PublicRoadSegment street,float z){var path=g.Path(street);return g.Split(street,path.Position(path.ClosestParameter(new Vector2(g.Path(street).start.x,z))));}
        static int On(PublicRoadNetwork g,float x,float z,ICollection<int> old)=>g.segments.First(e=>old.Contains(e.id)&&g.Path(e).DistanceTo(new Vector2(x,z))<.1f).id;
        /// <summary>No new ground road over water; some new `crossing` over water; ground ramps where it meets `ends`;
        /// and every node where a ground ramp meets the crossing stands on dry land (the bank).</summary>
        static void CheckCrossing(CareerState s,PublicRoadNetwork g,ICollection<int> old,RoadStructure crossing,params int[] ends)
        {
            var made=g.segments.Where(e=>!old.Contains(e.id)).ToList();
            bool Wet(PublicRoadSegment e,int i)=>s.world.IsWater(Alignment3D.XZ(g.Path(e).Position(i/8f)));
            foreach(var e in made)for(int i=0;i<=8;i++)Assert.IsFalse(e.structure==RoadStructure.Ground&&Wet(e,i),"A ground road stands in the water.");
            Assert.IsTrue(made.Any(e=>e.structure==crossing&&Enumerable.Range(0,9).Any(i=>Wet(e,i))),$"No {crossing} crosses the water.");
            foreach(int end in ends)Assert.IsTrue(g.At(end).Where(e=>!old.Contains(e.id)).All(e=>e.structure==RoadStructure.Ground),"The crossing meets the street without a ground ramp.");
            foreach(var n in g.nodes)
            {
                var arms=g.At(n.id).Where(e=>!old.Contains(e.id)).ToList();
                if(arms.Any(e=>e.structure==RoadStructure.Ground)&&arms.Any(e=>e.structure==crossing))
                    Assert.IsFalse(s.world.IsWater(PublicRoadNetwork.XZ(n.position)),"A bridge abutment or tunnel portal stands in the water.");
            }
            Assert.IsTrue(g.Validate(out string why),why);
        }

        [Test]public void W1_AutoBankToBankAcrossShallowMarginsIsABridge()
        {
            var state=State(River);var g=new PublicRoadNetwork();var west=Street(g,state,-200);var east=Street(g,state,200);
            var old=new HashSet<int>(g.segments.Select(e=>e.id));int start=Start(g,west,0);old.UnionWith(g.segments.Select(e=>e.id));
            int end=Step(state,ref g,start,new Vector3(200,100,0),On(g,200,0,old),RoadBuildMode.Automatic);
            Assert.IsTrue(g.HasRoute(start,end));
            CheckCrossing(state,g,old,RoadStructure.Elevated,start,end);
        }

        [Test]public void W2_BridgeModeRampsOnLandAndStandsOnTheBanks()
        {
            var state=State(River);var g=new PublicRoadNetwork();var west=Street(g,state,-200);var east=Street(g,state,200);
            var old=new HashSet<int>(g.segments.Select(e=>e.id));int start=Start(g,west,0);old.UnionWith(g.segments.Select(e=>e.id));
            int mid=Step(state,ref g,start,new Vector3(0,108,0),-1,RoadBuildMode.Bridge);
            int end=Step(state,ref g,mid,new Vector3(200,100,0),On(g,200,0,old),RoadBuildMode.Bridge);
            Assert.IsTrue(g.HasRoute(start,end));
            CheckCrossing(state,g,old,RoadStructure.Elevated,start,end);
        }

        [Test]public void W3_ALowBridgeOverWaterContinuesOntoTheBank()
        {
            // a bridge 1.5 m over the water (where the tools put a free point over water at 0) ends mid-river
            var state=State(River);var g=new PublicRoadNetwork();var east=Street(g,state,200);
            var a=new Vector3(-30,100.5f,-100);var b=new Vector3(0,100.5f,-100);
            var low=g.AddSegment(g.AddNode(a),g.AddNode(b),PublicRoadStyle.Urban,RoadStructure.Elevated);low.alignment=RoadBuildSettings.Default.Shape(a,b);
            var old=new HashSet<int>(g.segments.Select(e=>e.id));
            int end=Step(state,ref g,low.b,new Vector3(200,100,-100),On(g,200,-100,old),RoadBuildMode.Automatic);
            Assert.IsTrue(g.HasRoute(low.a,end));
            CheckCrossing(state,g,old,RoadStructure.Elevated,end);
        }

        [Test]public void W4_TunnelUnderAShallowRiverHasOpenRampsOnBothBanks()
        {
            var state=State(River);var g=new PublicRoadNetwork();var west=Street(g,state,-250);var east=Street(g,state,250);
            var old=new HashSet<int>(g.segments.Select(e=>e.id));int start=Start(g,west,0);old.UnionWith(g.segments.Select(e=>e.id));
            int p=Step(state,ref g,start,new Vector3(-90,90,0),-1,RoadBuildMode.Tunnel);
            int q=Step(state,ref g,p,new Vector3(90,90,0),-1,RoadBuildMode.Tunnel);
            int end=Step(state,ref g,q,new Vector3(250,100,0),On(g,250,0,old),RoadBuildMode.Tunnel);
            Assert.IsTrue(g.HasRoute(start,end));
            CheckCrossing(state,g,old,RoadStructure.Tunnel,start,end);
        }

        [Test]public void W4_TooLittleCoverUnderDeepWaterSaysHowMuchDeeper()
        {
            var state=State((x,z)=>Piecewise(Mathf.Abs(x),100f,82f,125f,98f,150f,99f,162.5f,100f));var g=new PublicRoadNetwork();
            int from=g.AddNode(new Vector3(-250,90,0));
            Assert.IsFalse(RoadConstructionPlan.TryCreate(state,g,from,new Vector3(250,90,0),-1,-1,PublicRoadStyle.Urban,Mode(RoadBuildMode.Tunnel),out _,out var why));
            StringAssert.Contains("deeper",why);StringAssert.Contains("15.0 m",why);
        }

        [Test]public void W5_ALowBankAtTheWatersEdgeSaysToStartTheClimbFurtherBack()
        {
            // the west bank is only 0.4 m above the water (its edge at x = -65 m)
            var state=State((x,z)=>x<0f?Piecewise(-x,37.5f,97.6f,62.5f,99f,70f,99.4f):River(x,z));var g=new PublicRoadNetwork();
            Vector3 On(float x,float z)=>new Vector3(x,state.world.Height(new Vector2(x,z)),z);
            PublicRoadSegment Line(Vector3 a,Vector3 b){var e=g.AddSegment(g.AddNode(a),g.AddNode(b),PublicRoadStyle.Urban,RoadStructure.Ground);e.alignment=RoadBuildSettings.Default.Shape(a,b);return e;}
            var edge=Line(On(-76,-300),On(-76,-50));var back=Line(On(-116,0),On(-116,300));
            int from=Start(g,edge,-150);
            Assert.IsFalse(RoadConstructionPlan.TryCreate(state,g,from,new Vector3(40,107.4f,-150),-1,-1,PublicRoadStyle.Urban,Mode(RoadBuildMode.Bridge),out _,out var why),
                "A street 10 m from the water cannot climb 1 m above it in time.");
            StringAssert.Contains("clear the water",why);StringAssert.Contains("further back",why);
            // 50 m back the ramp climbs on the bank, over the shallows, and the bridge stands on the bank
            var old=new HashSet<int>(g.segments.Select(e=>e.id));int start=Start(g,back,100);old.UnionWith(g.segments.Select(e=>e.id));
            Step(state,ref g,start,new Vector3(10,107.4f,100),-1,RoadBuildMode.Bridge);
            CheckCrossing(state,g,old,RoadStructure.Elevated,start);
        }

        GameObject root;
        [TearDown]public void Cleanup(){if(root!=null)Object.DestroyImmediate(root);}

        [Test]public void WE_ServiceRoadCrossesACanalWithShallowMarginsInOneClick()
        {
            var state=State(River);state.mode=GameMode.Career;state.funds=10000000;
            root=new GameObject("Service road over a canal");root.SetActive(false);root.transform.position=Vector3.up*100;
            var airport=root.AddComponent<Airport>();airport.groundSize=0;airport.TerrainHeight=p=>state.world.Height(p)-100f;root.SetActive(true);
            var net=airport.Taxiways;
            net.Connect(net.AddNode(new Vector3(-200,0,-200)),net.AddNode(new Vector3(-200,0,200)),RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Connect(net.AddNode(new Vector3(200,0,-200)),net.AddNode(new Vector3(200,0,200)),RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Rebuild();
            var old=new HashSet<int>(net.edges.Select(e=>e.id));
            int start=net.SplitEdge(net.edges.First(e=>net.Path(e).DistanceTo(new Vector2(-200,0))<.1f),new Vector3(-200,0,0));old.UnionWith(net.edges.Select(e=>e.id));
            Assert.IsTrue(AirsideTerrainPlan.TryCreate(state,airport,start,new Vector3(200,0,0),-1,net.edges.First(e=>old.Contains(e.id)&&net.Path(e).DistanceTo(new Vector2(200,0))<.1f),null,
                RunwayRules.RoadWidth,PavementSurface.Asphalt,RoadClass.Service,out var plan,out var why,null,Mode(RoadBuildMode.Automatic)),why);
            Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();
            var made=net.edges.Where(e=>!old.Contains(e.id)).ToList();
            bool Wet(TaxiwayEdge e,int i)=>state.world.IsWater(Geo.XZ(net.Path(e).Position(i/8f)));
            foreach(var e in made)for(int i=0;i<=8;i++)Assert.IsFalse(e.structure==RoadStructure.Ground&&Wet(e,i),"A ground road stands in the canal.");
            Assert.IsTrue(made.Any(e=>e.structure==RoadStructure.Elevated&&Enumerable.Range(0,9).Any(i=>Wet(e,i))),"No bridge crosses the canal.");
            Assert.IsNotNull(net.FindVehicleRoute(start,plan.EndNode));
        }
    }
}
