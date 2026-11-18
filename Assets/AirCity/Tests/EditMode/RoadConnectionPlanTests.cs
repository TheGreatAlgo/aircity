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
    /// The bridge and tunnel connection flows (a to e) at the plan and graph level: each step is a
    /// RoadConstructionPlan / AirsideTerrainPlan quote committed in turn, as the tools do. A bridge or tunnel that
    /// starts or finishes on an ordinary road meets it with a ground ramp; a tunnel leaving a hillside crosses the
    /// valley as a bridge; height-separated crossings never become junctions.
    /// </summary>
    public sealed class RoadConnectionPlanTests
    {
        static float G(float dx,float dz,float s)=>Mathf.Exp(-(dx*dx+dz*dz)/(s*s));
        static CareerState State(Func<float,float,float> land)
        {
            var world=new ValleyWorld {heights=new float[ValleyWorld.Resolution*ValleyWorld.Resolution]};
            for(int z=0;z<ValleyWorld.Resolution;z++)for(int x=0;x<ValleyWorld.Resolution;x++)
                world.heights[z*ValleyWorld.Resolution+x]=land(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f);
            return new CareerState {mode=GameMode.Sandbox,funds=0,wholeRegion=true,world=world};
        }
        /// <summary>A section as the Street tool draws one (smooth, constant grade).</summary>
        static PublicRoadSegment Section(PublicRoadNetwork g,Vector3 a,Vector3 b,RoadStructure structure)
        {
            var e=g.AddSegment(g.AddNode(a),g.AddNode(b),PublicRoadStyle.Urban,structure);e.alignment=RoadBuildSettings.Default.Shape(a,b);return e;
        }
        static Vector3 On(CareerState s,float x,float z)=>new Vector3(x,s.world.Height(new Vector2(x,z)),z);
        static RoadBuildSettings Mode(RoadBuildMode mode)=>new RoadBuildSettings {mode=mode,smooth=true};

        /// <summary>One click of the chain: quote to a free point (endSegment -1) or onto a section, then commit.</summary>
        static int Step(CareerState state,ref PublicRoadNetwork graph,int from,Vector3 end,int endSegment,RoadBuildMode mode)
        {
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,graph,from,end,-1,endSegment,PublicRoadStyle.Urban,Mode(mode),out var plan,out var why),why);
            Assert.IsTrue(plan.TryCommit(state,graph,out var next,out why),why);graph=next;return plan.EndNode;
        }
        static int Start(PublicRoadNetwork g,PublicRoadSegment edge,Vector2 at)
        {var path=g.Path(edge);return g.Split(edge,path.Position(path.ClosestParameter(at)));}
        static IEnumerable<PublicRoadSegment> At(PublicRoadNetwork g,int node,ICollection<int> old)=>g.At(node).Where(e=>!old.Contains(e.id));

        [Test]public void A_BridgeFromAGroundStreetOverAnotherOntoAThirdRampsAtBothEnds()
        {
            // a 5% cross-fall: a deck that starts at the street's level would cut into the uphill verge
            var state=State((x,z)=>100f+.05f*z);var g=new PublicRoadNetwork();
            var r1=Section(g,On(state,-200,-200),On(state,-200,200),RoadStructure.Ground);
            var r3=Section(g,On(state,0,-200),On(state,0,200),RoadStructure.Ground);
            var r2=Section(g,On(state,200,-200),On(state,200,200),RoadStructure.Ground);
            var old=new HashSet<int>(g.segments.Select(e=>e.id));
            int start=Start(g,r1,new Vector2(-200,0));old.UnionWith(g.segments.Select(e=>e.id));
            int a=Step(state,ref g,start,new Vector3(-80,108,0),-1,RoadBuildMode.Bridge);
            int b=Step(state,ref g,a,new Vector3(80,108,0),-1,RoadBuildMode.Bridge);
            var r2Part=g.segments.First(e=>e.style==PublicRoadStyle.Urban&&old.Contains(e.id)&&g.Path(e).DistanceTo(new Vector2(200,0))<.1f);
            int end=Step(state,ref g,b,new Vector3(200,100,0),r2Part.id,RoadBuildMode.Bridge);
            Assert.That(g.Node(end).position.y,Is.EqualTo(100).Within(.01f),"The bridge must come down to R2's level.");
            Assert.IsTrue(g.HasRoute(start,end)&&g.HasRoute(end,start));
            Assert.IsTrue(At(g,start,old).All(e=>e.structure==RoadStructure.Ground),"The bridge must meet R1 with a ground ramp.");
            Assert.IsTrue(At(g,end,old).All(e=>e.structure==RoadStructure.Ground),"The bridge must meet R2 with a ground ramp.");
            Assert.IsTrue(g.segments.Any(e=>e.structure==RoadStructure.Elevated&&g.Path(e).DistanceTo(Vector2.zero)<.1f),"R3 is crossed by a bridge.");
            Assert.IsFalse(g.nodes.Any(n=>Vector2.Distance(PublicRoadNetwork.XZ(n.position),Vector2.zero)<1f),"The bridge made a junction with R3 below.");
            Assert.IsTrue(g.Validate(out string why),why);
        }

        [Test]public void B_BridgesJoinAtDeckLevelAndABranchFromADeckRampsDownOntoAGroundStreet()
        {
            var state=State((x,z)=>100f+.05f*z);var g=new PublicRoadNetwork();
            var b1=Section(g,new Vector3(-200,110,-100),new Vector3(0,110,-100),RoadStructure.Elevated);
            var b2=Section(g,new Vector3(200,110,-200),new Vector3(200,110,0),RoadStructure.Elevated);
            var r6=Section(g,On(state,-300,100),On(state,300,100),RoadStructure.Ground);
            int b1End=b1.b;var old=new HashSet<int>(g.segments.Select(e=>e.id));
            int joined=Step(state,ref g,b1End,new Vector3(200,110,-100),b2.id,RoadBuildMode.Automatic);
            Assert.That(g.Node(joined).position.y,Is.EqualTo(110).Within(.01f));
            Assert.IsTrue(g.segments.Where(e=>!old.Contains(e.id)&&(e.a==b1End||e.b==b1End)).All(e=>e.structure==RoadStructure.Elevated));
            Assert.IsTrue(g.HasRoute(b1.a,b2.a),"B1 and B2 are joined at deck level.");
            old=new HashSet<int>(g.segments.Select(e=>e.id));
            int branch=Start(g,g.segments.First(e=>e.structure==RoadStructure.Elevated&&g.Path(e).DistanceTo(new Vector2(-100,-100))<.1f),new Vector2(-100,-100));
            old.UnionWith(g.segments.Select(e=>e.id));
            Assert.That(g.Node(branch).position.y,Is.EqualTo(110).Within(.01f));
            var r6Part=g.segments.First(e=>old.Contains(e.id)&&e.structure==RoadStructure.Ground&&g.Path(e).DistanceTo(new Vector2(-100,100))<.1f);
            int end=Step(state,ref g,branch,new Vector3(-100,105,100),r6Part.id,RoadBuildMode.Bridge);
            Assert.IsTrue(At(g,branch,old).All(e=>e.structure==RoadStructure.Elevated),"The branch leaves the deck as a bridge.");
            Assert.IsTrue(At(g,end,old).All(e=>e.structure==RoadStructure.Ground),"The branch meets the ground street with a ramp, not a deck on the ground.");
            Assert.IsTrue(g.HasRoute(b1.a,end));
            Assert.IsTrue(g.Validate(out string why),why);
        }

        [Test]public void C_TunnelFromAGroundStreetUnderAHillOntoAnotherRampsAtBothEnds()
        {
            var state=State((x,z)=>100f+30f*G(x,z,90f));var g=new PublicRoadNetwork();
            var r4=Section(g,On(state,-250,-200),On(state,-250,200),RoadStructure.Ground);
            var r5=Section(g,On(state,250,-200),On(state,250,200),RoadStructure.Ground);
            var old=new HashSet<int>(g.segments.Select(e=>e.id));
            int start=Start(g,r4,new Vector2(-250,0));old.UnionWith(g.segments.Select(e=>e.id));
            int a=Step(state,ref g,start,new Vector3(-120,90,0),-1,RoadBuildMode.Tunnel);
            int b=Step(state,ref g,a,new Vector3(120,90,0),-1,RoadBuildMode.Tunnel);
            var r5Part=g.segments.First(e=>old.Contains(e.id)&&g.Path(e).DistanceTo(new Vector2(250,0))<.1f);
            int end=Step(state,ref g,b,new Vector3(250,100,0),r5Part.id,RoadBuildMode.Tunnel);
            Assert.IsTrue(g.HasRoute(start,end)&&g.HasRoute(end,start));
            Assert.IsTrue(At(g,start,old).All(e=>e.structure==RoadStructure.Ground),"The tunnel must leave R4 by an open ramp.");
            Assert.IsTrue(At(g,end,old).All(e=>e.structure==RoadStructure.Ground),"The tunnel must come up onto R5 by an open ramp.");
            Assert.IsTrue(g.segments.Any(e=>e.structure==RoadStructure.Tunnel&&g.Path(e).DistanceTo(Vector2.zero)<.1f),"The hill is crossed by a tunnel.");
            Assert.IsFalse(g.segments.Any(e=>e.structure==RoadStructure.Elevated));
            Assert.IsTrue(g.Validate(out string why),why);
        }

        [Test]public void D_TunnelsJoinUndergroundAndATunnelLeavesAHillsideAsABridge()
        {
            // a ridge at x = 0 and a 20 m deep valley east of it
            var state=State((x,z)=>100f+25f*G(x,z,70f)-20f*Mathf.Exp(-((x-220f)*(x-220f))/(50f*50f)));var g=new PublicRoadNetwork();
            var t1=Section(g,new Vector3(-300,88,300),new Vector3(-150,88,300),RoadStructure.Tunnel);
            var t2=Section(g,new Vector3(0,88,200),new Vector3(0,88,400),RoadStructure.Tunnel);
            int joined=Step(state,ref g,t1.b,new Vector3(0,88,300),t2.id,RoadBuildMode.Tunnel);
            Assert.IsTrue(g.HasRoute(t1.a,t2.a)&&g.At(joined).All(e=>e.structure==RoadStructure.Tunnel),"Tunnels join underground.");
            var t3=Section(g,new Vector3(-40,105,0),new Vector3(40,105,0),RoadStructure.Tunnel);
            var old=new HashSet<int>(g.segments.Select(e=>e.id));
            int end=Step(state,ref g,t3.b,new Vector3(330,105,0),-1,RoadBuildMode.Tunnel);
            var made=g.segments.Where(e=>!old.Contains(e.id)).ToList();
            Assert.IsTrue(made.Any(e=>e.structure==RoadStructure.Tunnel),"The stretch still inside the ridge stays a tunnel.");
            Assert.IsTrue(made.Any(e=>e.structure==RoadStructure.Elevated&&g.Path(e).DistanceTo(new Vector2(220,0))<.1f),"Over the valley the road is a bridge, not a tunnel in the air.");
            Assert.IsTrue(made.All(e=>Mathf.Abs(g.Node(e.a).position.y-105)<.01f&&Mathf.Abs(g.Node(e.b).position.y-105)<.01f),"The level is kept.");
            Assert.IsTrue(g.HasRoute(t3.a,end));
        }

        [Test]public void TooSteepSaysHowFarTheRampNeeds()
        {
            var state=State((x,z)=>100f);var g=new PublicRoadNetwork();int from=g.AddNode(new Vector3(0,100,0));
            Assert.IsFalse(RoadConstructionPlan.TryCreate(state,g,from,new Vector3(40,110,0),-1,-1,PublicRoadStyle.Urban,Mode(RoadBuildMode.Bridge),out _,out var why));
            StringAssert.Contains("12% grade",why);StringAssert.Contains("climbing 10.0 m needs about",why);
            int needed=int.Parse(System.Text.RegularExpressions.Regex.Match(why,@"needs about (\d+) m").Groups[1].Value);
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,g,from,new Vector3(needed,110,0),-1,-1,PublicRoadStyle.Urban,Mode(RoadBuildMode.Bridge),out _,out why),why);
        }

        GameObject root;
        [TearDown]public void Cleanup(){if(root!=null)Object.DestroyImmediate(root);}

        [Test]public void E_ServiceTunnelUnderATaxiwayRampsFromAndOntoServiceRoads()
        {
            var state=new CareerState {mode=GameMode.Career,funds=10000000,wholeRegion=true,handsOn=true,selectedSite=0,layout=new CareerLayout(),publicRoads=new PublicRoadNetwork(),
                world=new ValleyWorld {heights=Enumerable.Repeat(100f,ValleyWorld.Resolution*ValleyWorld.Resolution).ToArray()}};
            state.world.sites.Add(new AirportSite {centre=Vector2.zero,elevation=100});
            root=new GameObject("Service tunnel connections");root.SetActive(false);root.transform.position=Vector3.up*100;
            var airport=root.AddComponent<Airport>();airport.groundSize=0;airport.TerrainHeight=_=>0;root.SetActive(true);
            var net=airport.Taxiways;
            net.Connect(net.AddNode(new Vector3(0,0,-300)),net.AddNode(new Vector3(0,0,300)),23,PavementSurface.Asphalt);
            net.Connect(net.AddNode(new Vector3(-200,0,-200)),net.AddNode(new Vector3(-200,0,200)),RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Connect(net.AddNode(new Vector3(200,0,-200)),net.AddNode(new Vector3(200,0,200)),RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Rebuild();
            var old=new HashSet<int>(net.edges.Select(e=>e.id));
            int start=net.SplitEdge(net.edges.First(e=>e.IsRoad&&net.Path(e).DistanceTo(new Vector2(-200,0))<.1f),new Vector3(-200,0,0));
            old.UnionWith(net.edges.Select(e=>e.id));
            int Step(int from,Vector3 end,TaxiwayEdge onto)
            {
                Assert.IsTrue(AirsideTerrainPlan.TryCreate(state,airport,from,end,-1,onto,null,RunwayRules.RoadWidth,PavementSurface.Asphalt,RoadClass.Service,
                    out var plan,out var why,null,Mode(RoadBuildMode.Tunnel)),why);
                Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();return plan.EndNode;
            }
            int a=Step(start,new Vector3(-80,-10,0),null);
            int b=Step(a,new Vector3(80,-10,0),null);
            int end=Step(b,new Vector3(200,0,0),net.edges.First(e=>e.IsRoad&&old.Contains(e.id)&&net.Path(e).DistanceTo(new Vector2(200,0))<.1f));
            Assert.That(Vector2.Distance(net.At(end),new Vector2(200,0)),Is.LessThan(.01f));
            Assert.IsNotNull(net.FindVehicleRoute(start,end),"S1 and S2 are joined through the tunnel.");
            Assert.IsTrue(net.EdgesAt(start).Where(e=>!old.Contains(e.id)).All(e=>e.structure==RoadStructure.Ground),"The tunnel leaves S1 by an open ramp.");
            Assert.IsTrue(net.EdgesAt(end).Where(e=>!old.Contains(e.id)).All(e=>e.structure==RoadStructure.Ground),"The tunnel comes up onto S2 by an open ramp.");
            Assert.IsTrue(net.edges.Any(e=>e.structure==RoadStructure.Tunnel&&net.Path(e).DistanceTo(Vector2.zero)<.1f),"The taxiway is crossed by a tunnel.");
            Assert.IsFalse(net.nodes.Any(n=>Vector2.Distance(Geo.XZ(n.position),Vector2.zero)<1f),"The tunnel made a junction with the taxiway.");
            Assert.AreEqual(0,airport.PavementElevationAt(Vector2.zero),.001f,"The taxiway keeps its surface.");
        }
    }
}
