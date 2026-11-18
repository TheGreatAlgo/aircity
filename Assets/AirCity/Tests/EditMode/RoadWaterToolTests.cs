using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using F = AirCity.EditorTools.Game.RoadConnectionFixture;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    /// <summary>
    /// Water crossings (w1 to w5, and airside) driven through the real Street and Road tools on the water variant of
    /// RoadConnectionFixture: a river with shallow margins, an ocean channel with a low bank, a canal through the
    /// airport. Each flow writes what happened to Logs/road-water-flows.md, then checks it: joined to the roads
    /// clicked, a bridge (never ground road) over the water with its ends on the banks, a tunnel only with cover,
    /// and refusals that say what to do.
    /// </summary>
    public sealed class RoadWaterToolTests
    {
        const string Log="Logs/road-water-flows.md";
        readonly List<GameObject> spawned=new List<GameObject>();
        CareerGame game;
        RoadToolDriver drive;
        int actions;
        ValleyWorld water;
        [SetUp]public void Create(){game=F.CreateWater(spawned);drive=new RoadToolDriver(game);actions=0;water=JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(game.State.world));}
        [TearDown]public void Cleanup(){drive?.Dispose();foreach(var go in spawned)if(go!=null)Object.DestroyImmediate(go);spawned.Clear();Shader.SetGlobalFloat("_AirCityUndergroundCut",0);}
        static void ExpectUndergroundGrid()=>LogAssert.Expect(LogType.Error,new System.Text.RegularExpressions.Regex("Destroy may not be called from edit mode"));

        static void Write(string line){Directory.CreateDirectory("Logs");File.AppendAllText(Log,line+"\n");}
        void Heading(string flow){actions=0;Write($"\n### {flow}\n");Write("| # | action | result |");Write("|---|---|---|");}
        void Row(string action,string result=null){actions++;Write($"| {actions} | {action} | {result??drive.Describe()} |");}
        void Mode(RoadBuildMode mode){drive.tool.SetRoadMode(mode);Row($"press {mode}",$"elevation={drive.tool.RoadElevation:+0.0;-0.0;0.0}, underground view={drive.Underground}");}
        /// <summary>The region as it was before building: where the water is (an open cut below sea level is ocean by
        /// ValleyWorld.IsWater afterwards, which is not what the player saw when placing the road).</summary>
        ValleyWorld World=>water;
        PublicRoadNetwork Roads=>drive.tool.Streets.network;
        static float RoadY(PublicRoadNetwork g,Vector2 p)
        {var edge=g.segments.OrderBy(e=>g.Path(e).DistanceTo(p)).First();var path=g.Path(edge);return path.Height(path.ClosestParameter(p));}
        int NodeAt(Vector2 p)=>Roads.nodes.Where(n=>Vector2.Distance(PublicRoadNetwork.XZ(n.position),p)<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
        void ClickRoad(string what,Vector2 p){float y=RoadY(Roads,p);drive.HoverRoad(p,y);string hover=drive.Describe();int chain=drive.Chain;drive.ClickRoad(p,y);
            Row($"click {what}",$"hover: {hover}; click {(drive.Chain!=chain?"accepted":"refused or ignored")}");}
        void ClickFree(string what,Vector2 p){drive.HoverFree(p);string hover=drive.Describe();int chain=drive.Chain;drive.ClickFree(p);
            Row($"click {what}",$"hover: {hover}; click {(drive.Chain!=chain?"accepted":"refused or ignored")}");}
        void HoverFree(string what,Vector2 p){drive.HoverFree(p);Row($"hover {what}");}
        List<PublicRoadSegment> Made(PublicRoadNetwork before)=>Roads.segments.Where(e=>before.Segment(e.id)==null).ToList();
        string Built(PublicRoadNetwork before)
        {
            var made=Made(before).OrderBy(e=>Roads.Node(e.a).position.x).ToList();
            return made.Count==0?"nothing":string.Join(", ",made.Select(e=>$"{e.structure} {Roads.Path(e).Length:0} m ({Roads.Node(e.a).position.y:0.0}→{Roads.Node(e.b).position.y:0.0})"));
        }
        void Result(string text){Write($"\n**Built:** {text}  \n**Actions:** {actions} (tool already chosen)\n");}
        bool Wet(Alignment3D path,int i,int count)=>World.IsWater(Alignment3D.XZ(path.Position(i/(float)count)));
        /// <summary>Every new section over water is a bridge or tunnel, and every bridge or tunnel reaching land at a
        /// joined end meets it with a ground ramp; at least one bridge (or tunnel) crosses the water.</summary>
        void CheckCrossing(PublicRoadNetwork before,RoadStructure crossing,params int[] ends)
        {
            var made=Made(before);
            foreach(var e in made)
            {
                var path=Roads.Path(e);
                for(int i=0;i<=8;i++)Assert.IsFalse(e.structure==RoadStructure.Ground&&Wet(path,i,8),"A ground road stands in the water: "+Built(before));
            }
            Assert.IsTrue(made.Any(e=>e.structure==crossing&&Enumerable.Range(0,9).Any(i=>Wet(Roads.Path(e),i,8))),$"No {crossing} crosses the water: "+Built(before));
            foreach(int end in ends)Assert.IsTrue(Roads.At(end).Where(e=>before.Segment(e.id)==null).All(e=>e.structure==RoadStructure.Ground),"The crossing meets the street without a ground ramp: "+Built(before));
            Assert.IsTrue(Roads.Validate(out string why),why);
        }

        void Street(){drive.tool.SetTool(DevTool.Street);drive.tool.SetStreetStyle(PublicRoadStyle.Urban);}
        void Auto(){drive.tool.SetRoadMode(RoadBuildMode.Automatic);drive.tool.SetRoadElevation(0);}

        [Test]public void W1a_RiverBridgePressedFirstOneClickBankToBank()
        {
            File.WriteAllText(Log,"# Water crossings (actual Street/Road tools, headless)\n\nUnity "+Application.unityVersion+", "+DateTime.UtcNow.ToString("u")+
                $"\n\nWater at {F.WaterLevel:0} m, banks at 100 m (the channel's west bank {F.LowBank:0.0} m).\n");
            Street();Heading("w1a. River, Bridge pressed first: RW (west bank) → RE (east bank) in one click");
            var before=Roads.Clone();
            Mode(RoadBuildMode.Bridge);
            ClickRoad("RW (street on the west bank)",new Vector2(-1600,-1400));
            ClickRoad("RE (street on the east bank)",new Vector2(-1400,-1400));
            drive.EndChain();Result(Built(before));
            int a=NodeAt(new Vector2(-1600,-1400)),b=NodeAt(new Vector2(-1400,-1400));
            Assert.IsTrue(Roads.HasRoute(NodeAt(F.RWA),NodeAt(F.REA)),"w1a: the banks' streets are not joined.");
            CheckCrossing(before,RoadStructure.Elevated,a,b);
        }
        [Test]public void W1b_RiverAutoOneClickBankToBank()
        {
            Street();Heading("w1b. River, Auto at 0: RW → RE in one click");
            var before=Roads.Clone();Auto();
            ClickRoad("RW",new Vector2(-1600,-1300));
            ClickRoad("RE",new Vector2(-1400,-1300));
            drive.EndChain();Result(Built(before));
            int a=NodeAt(new Vector2(-1600,-1300)),b=NodeAt(new Vector2(-1400,-1300));
            Assert.IsTrue(Roads.HasRoute(NodeAt(F.RWA),NodeAt(F.REA))&&Roads.At(a).Count()>=3&&Roads.At(b).Count()>=3,"w1b: the banks' streets are not joined.");
            CheckCrossing(before,RoadStructure.Elevated,a,b);
        }
        void Channel(string flow,float z,bool bridge)
        {
            var before=Roads.Clone();
            if(bridge)Mode(RoadBuildMode.Bridge);else Auto();
            ClickRoad("OE (street on the east bank)",new Vector2(-650,z));
            ClickFree("over open water",new Vector2(-760,z));
            ClickFree("over open water, far side",new Vector2(-940,z));
            ClickRoad("OW (street on the low west bank)",new Vector2(-1075,z));
            drive.EndChain();Result(Built(before));
            int a=NodeAt(new Vector2(-650,z)),b=NodeAt(new Vector2(-1075,z));
            Assert.IsTrue(Roads.HasRoute(NodeAt(F.OEA),NodeAt(F.OWA))&&Roads.At(a).Count()>=3&&Roads.At(b).Count()>=3,flow+": the banks' streets are not joined.");
            CheckCrossing(before,RoadStructure.Elevated,a,b);
        }
        [Test]public void W2a_ChannelBridgePressedFirstSeveralClicksOverOpenWater()
        {Street();Heading("w2a. Ocean channel, Bridge pressed first: OE → two points over open water → OW (low bank)");Channel("w2a",-1400,true);}
        [Test]public void W2b_ChannelAutoSeveralClicksOverOpenWater()
        {Street();Heading("w2b. Ocean channel, Auto at 0: the same clicks (free points over water just clear it)");Channel("w2b",-1200,false);}
        [Test]public void W3a_BranchFromABridgeOverTheRiverAtDeckLevel()
        {
            Street();Heading($"w3a. Branch from the middle of BW (deck {F.WaterBridgeLevel:0.0} m over the river), Auto at 0");
            var before=Roads.Clone();Auto();
            ClickRoad("BW's middle",new Vector2(-1520,-1700));
            drive.HoverFree(new Vector2(-1520,-1620));float branched=drive.Endpoint.y;
            ClickFree("80 m upstream, over the river",new Vector2(-1520,-1620));
            drive.EndChain();Result(Built(before)+$"; branch endpoint at {branched:0.0} m");
            Assert.That(branched,Is.EqualTo(F.WaterBridgeLevel).Within(.3f),"w3a: a branch from a deck over water did not keep the deck's level.");
            Assert.IsTrue(Made(before).Count>0&&Made(before).All(e=>e.structure==RoadStructure.Elevated),"w3a: the branch is not a bridge.");
        }
        [Test]public void W3b_ContinueFromABridgeEndOverTheRiverOntoTheBank()
        {
            Street();Heading("w3b. Continue from BW's end (mid-river) onto RE (east bank), Auto at 0");
            var before=Roads.Clone();Auto();
            ClickRoad("BW's east end",F.BWB);
            ClickRoad("RE",new Vector2(-1400,-1700));
            drive.EndChain();Result(Built(before));
            int b=NodeAt(new Vector2(-1400,-1700));
            Assert.IsTrue(b>0&&Roads.HasRoute(NodeAt(F.BWA),NodeAt(F.REA)),"w3b: BW is not joined to RE.");
            CheckCrossing(before,RoadStructure.Elevated,b);
        }
        [Test]public void W3c_LandANewBridgeOnABridgeOverTheRiver()
        {
            Street();Heading("w3c. Land a new bridge on BW: Bridge pressed first, RE → a point over the river → BW's middle");
            var before=Roads.Clone();
            Mode(RoadBuildMode.Bridge);
            ClickRoad("RE",new Vector2(-1400,-1850));
            ClickFree("over the river",new Vector2(-1500,-1800));
            ClickRoad("BW's middle",new Vector2(-1535,-1700));
            drive.EndChain();Result(Built(before));
            int start=NodeAt(new Vector2(-1400,-1850)),on=NodeAt(new Vector2(-1535,-1700));
            Assert.IsTrue(start>0&&on>0&&Roads.HasRoute(NodeAt(F.REA),NodeAt(F.BWA)),"w3c: the new bridge did not land on BW.");
            Assert.That(Roads.Node(on).position.y,Is.EqualTo(F.WaterBridgeLevel).Within(.01f));
            CheckCrossing(before,RoadStructure.Elevated,start);
        }
        [Test]public void W4a_TunnelUnderTheRiverWithRampsOnTheBanks()
        {
            ExpectUndergroundGrid();
            Street();Heading("w4a. Tunnel under the river (1.4 m deep): RW2 → Tunnel → two points on the banks → RE2");
            var before=Roads.Clone();float z=-1000;Auto();
            ClickRoad("RW2 (street 250 m west of the river)",new Vector2(-1750,z));
            Mode(RoadBuildMode.Tunnel);
            ClickFree("west bank, 90 m from the river",new Vector2(-1590,z));
            ClickFree("east bank",new Vector2(-1410,z));
            ClickRoad("RE2 (street 250 m east)",new Vector2(-1250,z));
            drive.EndChain();Result(Built(before));
            int a=NodeAt(new Vector2(-1750,z)),b=NodeAt(new Vector2(-1250,z));
            Assert.IsTrue(a>0&&b>0&&Roads.HasRoute(NodeAt(F.RW2A),NodeAt(F.RE2A)),"w4a: the streets are not joined under the river.");
            CheckCrossing(before,RoadStructure.Tunnel,a,b);
        }
        [Test]public void W4b_TunnelUnderTheDeepChannelSaysHowMuchDeeper()
        {
            ExpectUndergroundGrid();
            Street();Heading("w4b. Tunnel under the ocean channel (17 m deep) at the Tunnel default: OE2 → Tunnel → bank → beyond the far bank");
            var before=Roads.Clone();float z=-1000;Auto();
            ClickRoad("OE2",new Vector2(-450,z));
            Mode(RoadBuildMode.Tunnel);
            ClickFree("east bank, 50 m from the water",new Vector2(-600,z));
            HoverFree("beyond the west bank",new Vector2(-1120,z));
            string problem=drive.Problem;drive.EndChain();Result(Built(before)+"; refusal: "+(problem??"none"));
            Assert.IsNotNull(problem,"w4b: a tunnel without cover under the channel was accepted.");
            StringAssert.Contains("deeper",problem,"w4b: the refusal must say how much deeper the tunnel has to go.");
        }
        [Test]public void W5a_LowBankStreetClimbsOverTheShallows()
        {
            Street();Heading($"w5a. Low bank ({F.LowBank:0.0} m, 0.4 m above the water): OW → Bridge → over the channel");
            var before=Roads.Clone();
            Mode(RoadBuildMode.Bridge);
            ClickRoad("OW (street 45 m from the water)",new Vector2(-1075,-1300));
            ClickFree("over open water",new Vector2(-940,-1300));
            drive.EndChain();Result(Built(before));
            int a=NodeAt(new Vector2(-1075,-1300));
            Assert.That(a,Is.GreaterThan(0));Assert.IsTrue(Roads.HasRoute(NodeAt(F.OWA),a)&&Roads.At(a).Count()>=3,"w5a: the bridge did not start on OW.");
            CheckCrossing(before,RoadStructure.Elevated,a);
            foreach(var e in Made(before).Where(e=>e.structure==RoadStructure.Elevated))
            foreach(int end in new[]{e.a,e.b})
                if(Roads.At(end).Any(o=>o.structure==RoadStructure.Ground))
                    Assert.IsFalse(World.IsWater(PublicRoadNetwork.XZ(Roads.Node(end).position)),"w5a: the bridge's abutment stands in the water.");
        }
        [Test]public void W5b_StreetAtALowBanksEdgeSaysWhatToDo()
        {
            Street();Heading("w5b. Street at the water's edge on the low bank: OWE → Bridge → over the channel");
            var before=Roads.Clone();
            Mode(RoadBuildMode.Bridge);
            ClickRoad("OWE (street 10 m from the water)",new Vector2(-1040,-700));
            HoverFree("over open water",new Vector2(-900,-700));
            string problem=drive.Problem;drive.EndChain();Result(Built(before)+"; refusal: "+(problem??"none"));
            Assert.IsNotNull(problem,"w5b: a deck that cannot clear the water was accepted.");
            StringAssert.Contains("further back",problem,"w5b: the refusal must say what to do.");
        }

        // airside: service roads S1 and S2 either side of the canal through the airport
        Vector2 W(float x,float z){var o=game.Airport.transform.position;return new Vector2(x+o.x,z+o.z);}
        void Road(string what,float x,float z){float y=game.Airport.transform.position.y;drive.HoverRoad(W(x,z),y);string hover=drive.Describe();int chain=drive.Chain;drive.ClickRoad(W(x,z),y);
            Row($"click {what}",$"hover: {hover}; click {(drive.Chain!=chain?"accepted":"refused or ignored")}");}
        void Free(string what,float x,float z){drive.HoverFree(W(x,z));string hover=drive.Describe();int chain=drive.Chain;drive.ClickFree(W(x,z));
            Row($"click {what}",$"hover: {hover}; click {(drive.Chain!=chain?"accepted":"refused or ignored")}");}
        int Node(float x,float z)=>game.Airport.Taxiways.nodes.Where(n=>Vector2.Distance(Geo.XZ(n.position),new Vector2(x,z))<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
        void CheckCanal(string flow,HashSet<int> old,float z,RoadStructure crossing)
        {
            var net=game.Airport.Taxiways;var origin=game.Airport.transform.position;
            var made=net.edges.Where(e=>!old.Contains(e.id)).ToList();
            Result(made.Count==0?"nothing":string.Join(", ",made.OrderBy(e=>Mathf.Min(net.Node(e.a).position.x,net.Node(e.b).position.x)).Select(e=>$"{e.structure} {net.Path(e).Length:0} m ({net.Node(e.a).position.y:0.0}→{net.Node(e.b).position.y:0.0})")));
            int s1=Node(-200,z),s2=Node(200,z);
            Assert.IsTrue(s1>=0&&s2>=0&&net.FindVehicleRoute(Node(F.S1A.x,F.S1A.y),Node(F.S2A.x,F.S2A.y))!=null,flow+": the service roads are not joined across the canal.");
            foreach(var e in made)
            {var path=net.Path(e);for(int i=0;i<=8;i++)Assert.IsFalse(e.structure==RoadStructure.Ground&&World.IsWater(Geo.XZ(path.Position(i/8f))+Geo.XZ(origin)),flow+$": a ground road stands in the canal at {path.Position(i/8f)} (origin {origin}, ground {World.Height(Geo.XZ(path.Position(i/8f))+Geo.XZ(origin)):0.00}).");}
            Assert.IsTrue(made.Any(e=>e.structure==crossing&&Enumerable.Range(0,9).Any(i=>World.IsWater(Geo.XZ(net.Path(e).Position(i/8f))+Geo.XZ(origin)))),flow+$": no {crossing} crosses the canal.");
            foreach(int end in new[]{s1,s2})Assert.IsTrue(net.EdgesAt(end).Where(e=>!old.Contains(e.id)).All(e=>e.structure==RoadStructure.Ground),flow+": no ground ramp at the service road.");
        }
        HashSet<int> Edges=>new HashSet<int>(game.Airport.Taxiways.edges.Select(e=>e.id));
        [Test]public void WE1_ServiceRoadAutoOneClickOverTheCanal()
        {
            drive.tool.SetTool(DevTool.Road);Heading("we1. Canal, Road tool, Auto at 0: S1 → S2 in one click");
            var old=Edges;Auto();Road("S1",-200,-100);Road("S2",200,-100);drive.EndChain();
            CheckCanal("we1",old,-100,RoadStructure.Elevated);
        }
        [Test]public void WE2_ServiceRoadBridgeWithAPointOverTheCanal()
        {
            drive.tool.SetTool(DevTool.Road);Heading("we2. Canal, Road tool, Bridge pressed after S1: S1 → over the canal → S2");
            var old=Edges;Auto();Road("S1",-200,-160);Mode(RoadBuildMode.Bridge);Free("over the canal",0,-160);Road("S2",200,-160);drive.EndChain();
            CheckCanal("we2",old,-160,RoadStructure.Elevated);
        }
        [Test]public void WE4_ServiceTunnelUnderTheCanal()
        {
            ExpectUndergroundGrid();
            drive.tool.SetTool(DevTool.Road);Heading("we4. Canal, Road tool, Tunnel pressed after S1: S1 → two points on the banks → S2");
            var old=Edges;Auto();Road("S1",-200,150);Mode(RoadBuildMode.Tunnel);Free("west bank",-60,150);Free("east bank",60,150);Road("S2",200,150);drive.EndChain();
            CheckCanal("we4",old,150,RoadStructure.Tunnel);
        }
    }
}
