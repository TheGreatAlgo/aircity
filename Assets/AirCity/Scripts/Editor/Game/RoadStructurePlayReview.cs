using System;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.Game;
using AirCity.Vehicles;
using AirCity.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    public static partial class PavementOwnershipReview
    {
        public static void RunStructuresFromCommandLine(){Begin(true);SessionState.SetBool(Key+"StructuresOnly",true);SessionState.SetBool(Key+"StructureDiagnostics",false);}
        public static void RunStructureDiagnosticsFromCommandLine(){RunStructuresFromCommandLine();SessionState.SetBool(Key+"StructureDiagnostics",true);}
        static int structureFunds,tunnelStart;
        static readonly Vector2 BridgeStart=new Vector2(-400,-150),BridgeEnd=new Vector2(400,-150),BridgeExit=new Vector2(650,-150);
        static readonly Vector2 BoreStart=new Vector2(-600,220),BoreDeepStart=new Vector2(-200,220),BoreDeepEnd=new Vector2(200,220),BoreExit=new Vector2(600,220);
        static GameObject reviewTruck;
        static float reviewedTruckHeight;
        static Vector2 StructureGui(Vector2 p)
        {
            float elevation=100+tool.RoadElevation;
            var viewport=game.view.GetComponent<Camera>().WorldToViewportPoint(new Vector3(p.x,elevation,p.y));
            return new Vector2(viewport.x*width,(1-viewport.y)*height);
        }
        static void StructurePointer(Vector2 p){pointer=StructureGui(p);SendMouse(EventType.MouseMove);}
        static void StructureCharge(bool street)
        {
            Require(string.IsNullOrEmpty(street?tool.StreetProblem:tool.RoadProblem),street?tool.StreetProblem:tool.RoadProblem);
            quotedPrice=street?tool.StreetPrice:tool.AirsidePrice;Require(quotedPrice>0,"Missing structure quote.");
        }
        static void StructureCommitted()
        {Require(game.State.funds==structureFunds-quotedPrice,"The click did not charge the displayed structure quote. Expected "+(structureFunds-quotedPrice)+", actual "+game.State.funds+"; "+tool.RoadProblem+" / "+tool.StreetProblem);structureFunds=game.State.funds;}
        static void ReviewStructures(int stage)
        {
            switch(stage)
            {
                case 60:
                    var world=game.State.world;Array.Fill(world.heights,100f);world.hasOcean=world.hasRiver=false;world.waterHeights=new float[world.heights.Length];
                    world.trees=new System.Collections.Generic.List<ForestTree> {new ForestTree {position=new Vector2(-12,-150),height=4},new ForestTree {position=new Vector2(48,-150),height=18}};
                    game.valley.RefreshLand(true);game.Airport.TerrainHeight=_=>0;game.State.funds=2000000;structureFunds=game.State.funds;
                    tool.SetTool(DevTool.Street);tool.SetStreetStyle(PublicRoadStyle.Avenue);tool.SetRoadMode(RoadBuildMode.Ground);
                    game.view.FocusPoint(new Vector3(0,100,-150),1300,65,0);Go(61);break;
                case 61:Click(StructureGui(BridgeStart));Go(62);break;
                case 62:
                    tool.SetRoadMode(RoadBuildMode.Automatic);tool.SetRoadElevation(12);Go(63);break;
                case 63:StructurePointer(BridgeEnd);Go(64);break;
                case 64:
                    StructureCharge(true);Require(Mathf.Abs(tool.StreetEndpoint.y-112)<.2f,"The raised cursor did not resolve to its chosen elevation.");
                    if(!Capture("3d-01-automatic-bridge-preview"))return;Click(StructureGui(BridgeEnd));Go(65);break;
                case 65:
                    StructureCommitted();Require(game.State.publicRoads.segments.Any(e=>e.structure==RoadStructure.Elevated),"No bridge section was committed.");
                    tool.SetRoadMode(RoadBuildMode.Ground);game.view.FocusPoint(new Vector3(200,100,-150),1500,65,0);StructurePointer(BridgeExit);Go(66);break;
                case 66:
                    StructureCharge(true);Click(StructureGui(BridgeExit));Go(67);break;
                case 67:
                    StructureCommitted();tool.SetTool(DevTool.Select);game.view.FocusPoint(new Vector3(0,108,-150),240,25,25);Go(68);break;
                case 68:
                    File.AppendAllText("Logs/pavement-ownership-review.txt","Bridge terrain diagnostic: cut="+Shader.GetGlobalFloat("_AirCityUndergroundCut")+", height="+Shader.GetGlobalFloat("_AirCityUndergroundHeight")+", enabled="+game.valley.Terrain.enabled+", draw="+game.valley.Terrain.drawHeightmap+", ownsOffroad="+game.valley.BuiltSurfaces.Contains(Vector2.zero)+", triangles="+game.valley.BuiltSurfaces.TriangleCount+", cam="+game.view.transform.position+"\n");
                    if(!Capture("3d-02-supported-bridge-natural-undercroft"))return;
                    Require(!game.State.world.trees[0].removed,"Low vegetation under the bridge was removed.");Require(game.State.world.trees[1].removed,"A canopy intersects the bridge deck.");
                    var net=game.Airport.Taxiways;net.Connect(net.AddNode(new Vector3(0,0,40)),net.AddNode(new Vector3(0,0,500)),23,PavementSurface.Asphalt);net.Rebuild();game.SharedConstructionChanged();
                    tool.SetTool(DevTool.Road);tool.SetSurface(PavementSurface.Asphalt);tool.SetRoadMode(RoadBuildMode.Ground);
                    game.view.FocusPoint(new Vector3(-200,100,220),1500,65,0);Go(69);break;
                case 69:Click(StructureGui(BoreStart));Go(70);break;
                case 70:
                    tunnelStart=(int)typeof(DevAirfieldTool).GetField("chainNode",Instance).GetValue(tool);Require(tunnelStart>=0,"The road start did not resolve.");
                    tool.SetRoadMode(RoadBuildMode.Tunnel);Go(71);break;
                case 71:StructurePointer(BoreDeepStart);Go(72);break;
                case 72:
                    StructureCharge(false);if(!Capture("3d-03-tunnel-ramp-preview"))return;Click(StructureGui(BoreDeepStart));Go(73);break;
                case 73:StructureCommitted();StructurePointer(BoreDeepEnd);Go(74);break;
                case 74:
                    StructureCharge(false);if(!Capture("3d-04-airside-underpass-preview"))return;Click(StructureGui(BoreDeepEnd));Go(75);break;
                case 75:
                    StructureCommitted();Require(!game.Airport.Taxiways.nodes.Any(n=>Vector2.Distance(new Vector2(n.position.x,n.position.z),new Vector2(0,220))<1f),"A separated crossing created a false graph junction.");
                    Require(Mathf.Abs(game.Airport.RoadElevationAt(new Vector2(0,220),-10)+10)<.05f,"The underpass elevation was lost.");
                    Require(Mathf.Abs(game.Airport.PavementElevationAt(new Vector2(0,220)))<.05f,"The taxiway was pulled down into the tunnel.");
                    Require(Mathf.Abs(game.State.world.Height(new Vector2(0,220))-100)<.01f,"The covered tunnel excavated its roof terrain.");
                    tool.SetRoadMode(RoadBuildMode.Ground);game.view.FocusPoint(new Vector3(200,100,220),1500,65,0);StructurePointer(BoreExit);Go(76);break;
                case 76:StructureCharge(false);Click(StructureGui(BoreExit));Go(77);break;
                case 77:
                    StructureCommitted();tool.SetTool(DevTool.Select);game.view.FocusPoint(new Vector3(-330,95,220),170,20,90);Go(78);break;
                case 78:
                    if(!Capture("3d-05-tunnel-portal-and-retained-roof"))return;
                    tool.SetTool(DevTool.Road);tool.SetRoadMode(RoadBuildMode.Tunnel);game.view.FocusPoint(new Vector3(0,90,220),110,60,20);Go(79);break;
                case 79:
                    ShowTunnelTruck();Go(80);break;
                case 80:
                    if(!Capture("3d-06-service-vehicle-on-underground-road"))return;
                    Require(Mathf.Abs(reviewTruck.transform.position.y-90)<.08f,"The service vehicle did not follow its tunnel deck.");
                    tool.SetTool(DevTool.Select);tool.SetRoadUnderground(false);Require(game.Save(),game.Message);Require(game.Continue(),game.Message);game.Paused=true;tool=game.BuildTools;
                    Require(Mathf.Abs(game.Airport.RoadElevationAt(new Vector2(0,220),-10)+10)<.05f,"Reload lost the underpass profile.");
                    Require(game.State.publicRoads.segments.Any(e=>e.structure==RoadStructure.Elevated),"Reload lost the bridge.");
                    game.view.FocusPoint(new Vector3(-120,100,70),1800,50,20);pointer=new Vector2(720,20);SendMouse(EventType.MouseMove);Go(81);break;
                case 81:
                    var paintPart=game.Airport.Taxiways.transform.Find("Paint");var paintMaterial=paintPart.GetComponent<Renderer>().sharedMaterial;var paintMesh=paintPart.GetComponent<MeshFilter>().sharedMesh;
                    var undergroundVertices=paintMesh.vertices.Select((p,i)=>(p,i)).Where(item=>item.p.x>30&&item.p.x<100&&Mathf.Abs(item.p.z-220)<4).ToArray();
                    Require(undergroundVertices.Length>0&&undergroundVertices.All(v=>v.p.y< -9.9f),"An equipment-paint refresh lifted the tunnel markings to the surface.");
                    File.AppendAllText("Logs/pavement-ownership-review.txt","Paint diagnostic: material="+paintMaterial.name+", test="+paintMaterial.GetFloat("_ConstructionZTest")+", write="+paintMaterial.GetFloat("_ConstructionZWrite")+", transform="+paintPart.position+", vertices="+string.Join(";",undergroundVertices.Take(4).Select(v=>v.p+" normal="+paintMesh.normals[v.i]+" tangent="+paintMesh.tangents[v.i]))+"\n");
                    if(!Capture("3d-07-bridge-and-tunnel-after-reload"))return;
                    var savedRoads=game.State.publicRoads.Clone();
                    tool.SetTool(DevTool.Delete);
                    var pickMethod=typeof(DevAirfieldTool).GetMethod("PickAt",Instance);
                    var picked=pickMethod.Invoke(tool,new object[]{new Vector3(100,game.State.publicRoads.Path(game.State.publicRoads.segments.First(e=>e.structure==RoadStructure.Elevated)).Height(.5f),-150)});
                    Require(picked.GetType().GetField("publicRoad",Instance).GetValue(picked)!=null,"The elevated public road cannot be selected.");
                    typeof(DevAirfieldTool).GetMethod("Remove",Instance).Invoke(tool,new[]{picked,(object)false});
                    Require(tool.Streets.network.segments.Count==savedRoads.segments.Count-1,"Road deletion did not use the public graph.");
                    tool.Streets.network=savedRoads;tool.Streets.Rebuild();tool.SetTool(DevTool.Select);
                    var report=new StructuresResult {result="Passed: actual mouse ground/bridge/tunnel placement, raised picking, separate-height crossing, vehicle support, retained roof terrain and save/load.",utc=DateTime.UtcNow.ToString("O"),funds=game.State.funds,truckElevation=reviewedTruckHeight,publicSections=game.State.publicRoads.segments.Count,airsideSections=game.Airport.Taxiways.edges.Count};
                    File.WriteAllText("Logs/road-structures-play-review.json",JsonUtility.ToJson(report,true));File.AppendAllText("Logs/pavement-ownership-review.txt",report.result+"\n");
                    if(SessionState.GetBool(Key+"StructureDiagnostics",false)){Go(82);break;}
                    SessionState.SetInt(Key+"Stage",100);RestoreInput();EditorApplication.ExitPlaymode();break;
                case 82:
                    game.Airport.Taxiways.transform.Find("Paint").GetComponent<Renderer>().enabled=false;Go(83);break;
                case 83:
                    if(!Capture("3d-08-no-airside-paint-diagnostic"))return;
                    game.Airport.Taxiways.transform.Find("Surface").GetComponent<Renderer>().enabled=false;Go(84);break;
                case 84:
                    if(!Capture("3d-09-structure-only-diagnostic"))return;
                    game.Airport.Taxiways.transform.Find("Road structures").GetComponent<Renderer>().enabled=false;Go(85);break;
                case 85:
                    if(!Capture("3d-10-terrain-only-diagnostic"))return;
                    SessionState.SetInt(Key+"Stage",100);RestoreInput();EditorApplication.ExitPlaymode();break;
            }
        }
        // ------------------------------------------------------------------ connection flows (RoadConnectionFixture)

        /// <summary>The bridge and tunnel connection flows a to e through actual mouse clicks on the shared tools, on
        /// the RoadConnectionFixture region: captures in Captures/NewGame/PavementOwnership/conn-*.png, results in
        /// Logs/road-connections-play-review.json. Each flow must join the roads it is clicked onto.</summary>
        public static void RunConnectionsFromCommandLine(){Begin(true);SessionState.SetBool(Key+"StructuresOnly",false);SessionState.SetBool(Key+"ConnectionsOnly",true);}
        static Func<bool>[] connectionSteps;
        static PublicRoadNetwork connectionBefore;
        static int airsideBefore;
        static readonly System.Collections.Generic.List<string> connectionResults=new System.Collections.Generic.List<string>();
        [Serializable]sealed class ConnectionsResult {public string result,utc;public string[] flows;}
        static Vector2 Gui(Vector3 world)
        {var v=game.view.GetComponent<Camera>().WorldToViewportPoint(world);return new Vector2(v.x*width,(1-v.y)*height);}
        /// <summary>A free point where the marker ring sits: at the tool's working level.</summary>
        static Vector3 Free(Vector2 p)=>new Vector3(p.x,(float)typeof(DevAirfieldTool).GetMethod("RoadTargetHeight",Instance).Invoke(tool,new object[]{p}),p.y);
        static float StreetLevel(Vector2 p)
        {var g=tool.Streets.network;var e=g.segments.OrderBy(s=>g.Path(s).DistanceTo(p)).First();var path=g.Path(e);return path.Height(path.ClosestParameter(p));}
        /// <summary>A road's surface (in the underground view, its outline drawn over the cutaway).</summary>
        static Vector3 RoadAt(Vector2 p,float y)=>new Vector3(p.x,y+.2f,p.y);
        /// <summary>Frames a flow; its points must stay left of the inspector panel on the right of the Game view.</summary>
        static void Over(Vector2 centre,float range,float tilt=65,float bearing=0)=>game.view.FocusPoint(new Vector3(centre.x,RoadConnectionFixture.Land(centre),centre.y),range,tilt,bearing);
        static void Hover(Vector3 world){pointer=Gui(world);SendMouse(EventType.MouseMove);}
        static void Away(){pointer=new Vector2(720,20);SendMouse(EventType.MouseMove);}
        static string Built(PublicRoadNetwork before)
        {
            var g=tool.Streets.network;var made=g.segments.Where(e=>before.Segment(e.id)==null).ToList();
            return string.Join(", ",made.Select(e=>$"{e.structure} {g.Path(e).Length:0} m"));
        }
        static int StreetNode(Vector2 p)=>tool.Streets.network.nodes.Where(n=>Vector2.Distance(PublicRoadNetwork.XZ(n.position),p)<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
        static void Joined(string flow,Vector2 a,Vector2 b)
        {
            var g=tool.Streets.network;int na=StreetNode(a),nb=StreetNode(b);
            Require(na>0&&nb>0&&g.HasRoute(na,nb),flow+": the roads clicked are not joined. "+tool.StreetProblem);
            Require(g.Validate(out string why),flow+": "+why);
            connectionResults.Add(flow+": joined; built "+Built(connectionBefore));
        }
        static bool Snapped(string what,bool street=true)
        {
            string snap=street?tool.StreetSnapName:tool.SnapText,problem=street?tool.StreetProblem:tool.RoadProblem;
            Require(snap!=null&&snap.Contains(what),"The cursor did not snap to "+what+": "+(snap??"nothing")+" / "+problem);
            Require(string.IsNullOrEmpty(problem),problem);return true;
        }
        static Func<bool>[] ConnectionSteps()
        {
            var origin=Vector3.zero;
            Vector2 W(Vector2 local)=>local+new Vector2(origin.x,origin.z);
            return new Func<bool>[]
            {
                ()=>{game.Initialize(RoadConnectionFixture.State(),false);game.Paused=true;tool=game.BuildTools;RoadConnectionFixture.AddAirside(game);
                    origin=game.Airport.transform.position;connectionResults.Clear();return true;},
                // a. Bridge pressed first; R1 → over R3 → down onto R2
                ()=>{tool.SetTool(DevTool.Street);tool.SetStreetStyle(PublicRoadStyle.Urban);tool.SetRoadMode(RoadBuildMode.Bridge);connectionBefore=tool.Streets.network.Clone();
                    Over(new Vector2(-1500,-1300),620);return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1700,-1300),StreetLevel(new Vector2(-1700,-1300)))));return true;},
                ()=>{Click(Gui(Free(new Vector2(-1580,-1300))));return true;},
                ()=>{Click(Gui(Free(new Vector2(-1420,-1300))));return true;},
                ()=>{Hover(RoadAt(new Vector2(-1300,-1300),StreetLevel(new Vector2(-1300,-1300))));return true;},
                ()=>Snapped("two-lane street")&&Capture("conn-a1-bridge-points-at-street-below-and-ramps-to-it"),
                ()=>{Click(Gui(RoadAt(new Vector2(-1300,-1300),StreetLevel(new Vector2(-1300,-1300)))));return true;},
                ()=>{Joined("a (bridge R1 → over R3 → R2)",RoadConnectionFixture.R1A,RoadConnectionFixture.R2A);tool.SetTool(DevTool.Select);
                    game.view.FocusPoint(new Vector3(-1500,104,-1300),330,24,-35);Away();return true;},
                ()=>Capture("conn-a2-bridge-joined-to-both-streets-over-a-third"),
                // c. Tunnel pressed after starting on R4; under the hill; up onto R5 through the underground guide
                ()=>{tool.SetTool(DevTool.Street);tool.SetRoadMode(RoadBuildMode.Automatic);tool.SetRoadElevation(0);connectionBefore=tool.Streets.network.Clone();
                    Over(new Vector2(-1420,-900),620);return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1750,-900),StreetLevel(new Vector2(-1750,-900)))));return true;},
                ()=>{tool.SetRoadMode(RoadBuildMode.Tunnel);Hover(Free(new Vector2(-1620,-900)));return true;},
                ()=>{Click(Gui(Free(new Vector2(-1620,-900))));return true;},
                ()=>{Click(Gui(Free(new Vector2(-1380,-900))));return true;},
                ()=>{Hover(RoadAt(new Vector2(-1250,-900),StreetLevel(new Vector2(-1250,-900))));return true;},
                ()=>{Hover(RoadAt(new Vector2(-1250,-900),StreetLevel(new Vector2(-1250,-900))));return true;},
                ()=>Snapped("two-lane street")&&Capture("conn-c1-underground-guide-tunnel-comes-up-to-street"),
                ()=>{Click(Gui(RoadAt(new Vector2(-1250,-900),StreetLevel(new Vector2(-1250,-900)))));return true;},
                ()=>{Joined("c (tunnel R4 → under the hill → R5)",RoadConnectionFixture.R4A,RoadConnectionFixture.R5A);tool.SetTool(DevTool.Select);tool.SetRoadUnderground(false);
                    game.view.FocusPoint(new Vector3(-1660,100,-900),260,28,40);Away();return true;},
                ()=>Capture("conn-c2-tunnel-ramp-from-street-into-hill"),
                // d1. Tunnel pressed first: T1's end joins T2's middle underground
                ()=>{tool.SetTool(DevTool.Street);tool.SetRoadMode(RoadBuildMode.Tunnel);connectionBefore=tool.Streets.network.Clone();Over(new Vector2(-1760,-500),420);return true;},
                ()=>{Hover(RoadAt(RoadConnectionFixture.T1B,RoadConnectionFixture.TunnelLevel));return true;},
                ()=>{Click(Gui(RoadAt(RoadConnectionFixture.T1B,RoadConnectionFixture.TunnelLevel)));return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1650,-500),RoadConnectionFixture.TunnelLevel)));return true;},
                ()=>{Joined("d1 (tunnel T1's end → T2's middle)",RoadConnectionFixture.T1A,RoadConnectionFixture.T2A);tool.SetTool(DevTool.Select);return true;},
                // d2. Tunnel mode from T3's end inside the ridge, straight out over the valley
                ()=>{tool.SetTool(DevTool.Street);tool.SetRoadMode(RoadBuildMode.Tunnel);connectionBefore=tool.Streets.network.Clone();Over(new Vector2(-1060,-500),560);return true;},
                ()=>{Hover(RoadAt(RoadConnectionFixture.T3B,RoadConnectionFixture.RidgeTunnelLevel));return true;},
                ()=>{Click(Gui(RoadAt(RoadConnectionFixture.T3B,RoadConnectionFixture.RidgeTunnelLevel)));return true;},
                ()=>{Click(Gui(Free(new Vector2(-960,-500))));return true;},
                ()=>{var g=tool.Streets.network;var made=g.segments.Where(e=>connectionBefore.Segment(e.id)==null).ToList();
                    Require(made.Any(e=>e.structure==RoadStructure.Elevated)&&made.Any(e=>e.structure==RoadStructure.Tunnel),"d2: the tunnel did not leave the ridge as a bridge: "+Built(connectionBefore)+" "+tool.StreetProblem);
                    connectionResults.Add("d2 (tunnel out of the ridge over the valley): built "+Built(connectionBefore));
                    tool.SetTool(DevTool.Select);tool.SetRoadUnderground(false);game.view.FocusPoint(new Vector3(-1140,100,-500),420,12,0);Away();return true;},
                ()=>Capture("conn-d2-tunnel-leaves-ridge-as-a-bridge"),
                // e. Airside: S1 → service tunnel under the taxiway → S2
                ()=>{tool.SetTool(DevTool.Road);tool.SetSurface(PavementSurface.Asphalt);tool.SetRoadMode(RoadBuildMode.Automatic);tool.SetRoadElevation(0);
                    airsideBefore=game.Airport.Taxiways.edges.Count;game.view.FocusPoint(origin+new Vector3(70,0,0),560,65,0);return true;},
                ()=>{Click(Gui(RoadAt(W(new Vector2(-200,0)),origin.y)));return true;},
                ()=>{tool.SetRoadMode(RoadBuildMode.Tunnel);Hover(Free(W(new Vector2(-80,0))));return true;},
                ()=>{Click(Gui(Free(W(new Vector2(-80,0)))));return true;},
                ()=>{Click(Gui(Free(W(new Vector2(80,0)))));return true;},
                ()=>{Hover(RoadAt(W(new Vector2(200,0)),origin.y));return true;},
                ()=>{Hover(RoadAt(W(new Vector2(200,0)),origin.y));return true;},
                ()=>Snapped("the road",false)&&Capture("conn-e1-service-tunnel-comes-up-to-service-road"),
                ()=>{Click(Gui(RoadAt(W(new Vector2(200,0)),origin.y)));return true;},
                ()=>{var net=game.Airport.Taxiways;
                    int s1=net.nodes.Where(n=>Vector2.Distance(new Vector2(n.position.x,n.position.z),new Vector2(-200,0))<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
                    int s2=net.nodes.Where(n=>Vector2.Distance(new Vector2(n.position.x,n.position.z),new Vector2(200,0))<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
                    Require(s1>=0&&s2>=0&&net.FindVehicleRoute(s1,s2)!=null,"e: the service roads are not joined. "+tool.RoadProblem);
                    Require(!net.nodes.Any(n=>Vector2.Distance(new Vector2(n.position.x,n.position.z),Vector2.zero)<1f),"e: the tunnel made a junction with the taxiway.");
                    connectionResults.Add("e (service tunnel S1 → under the taxiway → S2): joined; "+string.Join(", ",net.edges.Skip(airsideBefore).Select(e=>e.structure+" "+net.Path(e).Length.ToString("0")+" m")));
                    tool.SetTool(DevTool.Select);tool.SetRoadUnderground(false);game.view.FocusPoint(origin+new Vector3(-60,0,0),300,30,30);Away();return true;},
                ()=>Capture("conn-e2-service-tunnel-under-taxiway-joined"),
                // b. Auto at 0: continue from B1's end at deck level, join B2's middle
                ()=>{tool.SetTool(DevTool.Street);tool.SetRoadMode(RoadBuildMode.Automatic);tool.SetRoadElevation(0);connectionBefore=tool.Streets.network.Clone();
                    Over(new Vector2(-1400,-1700),480);return true;},
                ()=>{Click(Gui(RoadAt(RoadConnectionFixture.B1B,RoadConnectionFixture.BridgeLevel)));return true;},
                ()=>{Hover(Free(new Vector2(-1400,-1700)));return true;},
                ()=>{Require(Mathf.Abs(tool.StreetEndpoint.y-RoadConnectionFixture.BridgeLevel)<.3f,"b: continuing a bridge left its deck level: "+tool.StreetEndpoint.y);
                    return Capture("conn-b1-continuing-a-bridge-at-deck-level");},
                ()=>{Click(Gui(Free(new Vector2(-1400,-1700))));return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1300,-1700),RoadConnectionFixture.BridgeLevel)));return true;},
                ()=>{Joined("b (B1's end → B2's middle at deck level)",RoadConnectionFixture.B1A,RoadConnectionFixture.B2A);tool.SetTool(DevTool.Select);return true;},
            };
        }
        static void ReviewConnections(int stage)
        {
            connectionSteps??=ConnectionSteps();int step=stage-200;
            if(step<connectionSteps.Length){if(connectionSteps[step]())Go(stage+1);return;}
            var report=new ConnectionsResult {result="Passed: bridge and tunnel connection flows a-e by actual mouse clicks.",utc=DateTime.UtcNow.ToString("O"),flows=connectionResults.ToArray()};
            File.WriteAllText("Logs/road-connections-play-review.json",JsonUtility.ToJson(report,true));File.AppendAllText("Logs/pavement-ownership-review.txt",report.result+"\n");
            connectionSteps=null;SessionState.SetInt(Key+"Stage",100);RestoreInput();EditorApplication.ExitPlaymode();
        }

        // ------------------------------------------------------------------ water crossings (RoadConnectionFixture water variant)

        /// <summary>Water crossings by actual mouse clicks on the shared tools: a one-click river bridge in Auto, a
        /// several-click bridge over the ocean channel, a tunnel under the river, a refusal at a low bank's edge and a
        /// service-road bridge over the airport canal. Captures conn-water-*.png; Logs/road-water-play-review.json.</summary>
        public static void RunWaterCrossingsFromCommandLine(){Begin(true);SessionState.SetBool(Key+"StructuresOnly",false);SessionState.SetBool(Key+"WaterOnly",true);}
        static Func<bool>[] waterSteps;
        static void OverWater(Vector2 centre,float range,float tilt=65,float bearing=0)=>game.view.FocusPoint(new Vector3(centre.x,RoadConnectionFixture.WaterLand(centre),centre.y),range,tilt,bearing);
        static Func<bool>[] WaterSteps()
        {
            var origin=Vector3.zero;Vector2 W(float x,float z)=>new Vector2(x+origin.x,z+origin.z);
            var net=default(AirCity.Airfield.TaxiwayNetwork);int edgesBefore=0;
            return new Func<bool>[]
            {
                ()=>{game.Initialize(RoadConnectionFixture.WaterState(),false);game.Paused=true;tool=game.BuildTools;RoadConnectionFixture.AddWaterAirside(game);
                    origin=game.Airport.transform.position;net=game.Airport.Taxiways;connectionResults.Clear();return true;},
                // w1b. Auto at 0, one click from bank to bank across the river's shallow margins
                ()=>{tool.SetTool(DevTool.Street);tool.SetStreetStyle(PublicRoadStyle.Urban);tool.SetRoadMode(RoadBuildMode.Automatic);tool.SetRoadElevation(0);
                    connectionBefore=tool.Streets.network.Clone();OverWater(new Vector2(-1480,-1300),420);return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1600,-1300),StreetLevel(new Vector2(-1600,-1300)))));return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1400,-1300),StreetLevel(new Vector2(-1400,-1300)))));return true;},
                ()=>{Joined("w1b (river, Auto, one click bank to bank)",new Vector2(-1600,-1300),new Vector2(-1400,-1300));tool.SetTool(DevTool.Select);return true;},
                // w2a. Bridge pressed first, the ocean channel in several clicks over open water
                ()=>{tool.SetTool(DevTool.Street);tool.SetRoadMode(RoadBuildMode.Bridge);connectionBefore=tool.Streets.network.Clone();OverWater(new Vector2(-800,-1400),640);return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-650,-1400),StreetLevel(new Vector2(-650,-1400)))));return true;},
                ()=>{Click(Gui(Free(new Vector2(-760,-1400))));return true;},
                ()=>{Click(Gui(Free(new Vector2(-940,-1400))));return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1075,-1400),StreetLevel(new Vector2(-1075,-1400)))));return true;},
                ()=>{Joined("w2a (ocean channel, Bridge, four clicks)",new Vector2(-650,-1400),new Vector2(-1075,-1400));tool.SetTool(DevTool.Select);
                    game.view.FocusPoint(new Vector3(-860,99,-1400),360,16,-20);Away();return true;},
                ()=>Capture("conn-water-1-bridge-over-the-channel-from-bank-to-low-bank"),
                // w4a. Tunnel under the river with ramps on both banks; capture the last hover in the underground view
                ()=>{tool.SetTool(DevTool.Street);tool.SetRoadMode(RoadBuildMode.Automatic);tool.SetRoadElevation(0);connectionBefore=tool.Streets.network.Clone();
                    OverWater(new Vector2(-1480,-1000),640);return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1750,-1000),StreetLevel(new Vector2(-1750,-1000)))));return true;},
                ()=>{tool.SetRoadMode(RoadBuildMode.Tunnel);Hover(Free(new Vector2(-1590,-1000)));return true;},
                ()=>{Click(Gui(Free(new Vector2(-1590,-1000))));return true;},
                ()=>{Click(Gui(Free(new Vector2(-1410,-1000))));return true;},
                ()=>{Hover(RoadAt(new Vector2(-1250,-1000),StreetLevel(new Vector2(-1250,-1000))));return true;},
                ()=>{Hover(RoadAt(new Vector2(-1250,-1000),StreetLevel(new Vector2(-1250,-1000))));return true;},
                ()=>Snapped("two-lane street")&&Capture("conn-water-2-tunnel-under-the-river-comes-up-to-the-street"),
                ()=>{Click(Gui(RoadAt(new Vector2(-1250,-1000),StreetLevel(new Vector2(-1250,-1000)))));return true;},
                ()=>{Joined("w4a (tunnel under the river)",new Vector2(-1750,-1000),new Vector2(-1250,-1000));tool.SetTool(DevTool.Select);tool.SetRoadUnderground(false);return true;},
                // w5b. At the low bank's edge the deck cannot clear the water: the label says what to do
                ()=>{tool.SetTool(DevTool.Street);tool.SetRoadMode(RoadBuildMode.Bridge);connectionBefore=tool.Streets.network.Clone();OverWater(new Vector2(-980,-700),420);return true;},
                ()=>{Click(Gui(RoadAt(new Vector2(-1040,-700),StreetLevel(new Vector2(-1040,-700)))));return true;},
                ()=>{Hover(Free(new Vector2(-900,-700)));return true;},
                ()=>{Require(tool.StreetProblem!=null&&tool.StreetProblem.Contains("further back"),"w5b: expected a refusal saying to start further back: "+tool.StreetProblem);
                    connectionResults.Add("w5b (low bank at the water's edge): refused: "+tool.StreetProblem);tool.SetTool(DevTool.Select);return true;},
                // we2. Airside: S1 → Bridge → over the canal → S2
                ()=>{tool.SetTool(DevTool.Road);tool.SetSurface(PavementSurface.Asphalt);tool.SetRoadMode(RoadBuildMode.Automatic);tool.SetRoadElevation(0);
                    edgesBefore=net.edges.Count;game.view.FocusPoint(origin+new Vector3(40,0,-160),520,65,0);return true;},
                ()=>{Click(Gui(RoadAt(W(-200,-160),origin.y)));return true;},
                ()=>{tool.SetRoadMode(RoadBuildMode.Bridge);Hover(Free(W(0,-160)));return true;},
                ()=>{Click(Gui(Free(W(0,-160))));return true;},
                ()=>{Click(Gui(RoadAt(W(200,-160),origin.y)));return true;},
                ()=>{int s1=net.nodes.Where(n=>Vector2.Distance(new Vector2(n.position.x,n.position.z),new Vector2(-200,-160))<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
                    int s2=net.nodes.Where(n=>Vector2.Distance(new Vector2(n.position.x,n.position.z),new Vector2(200,-160))<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
                    Require(s1>=0&&s2>=0&&net.FindVehicleRoute(s1,s2)!=null,"we2: the service roads are not joined over the canal. "+tool.RoadProblem);
                    connectionResults.Add("we2 (service bridge over the canal): joined; "+string.Join(", ",net.edges.Skip(edgesBefore).Select(e=>e.structure+" "+net.Path(e).Length.ToString("0")+" m")));
                    tool.SetTool(DevTool.Select);game.view.FocusPoint(origin+new Vector3(0,0,-160),230,20,-25);Away();return true;},
                ()=>Capture("conn-water-3-service-road-bridge-over-the-airport-canal"),
            };
        }
        static void ReviewWater(int stage)
        {
            waterSteps??=WaterSteps();int step=stage-300;
            if(step<waterSteps.Length){if(waterSteps[step]())Go(stage+1);return;}
            var report=new ConnectionsResult {result="Passed: water crossings w1, w2, w4, w5 and airside by actual mouse clicks.",utc=DateTime.UtcNow.ToString("O"),flows=connectionResults.ToArray()};
            File.WriteAllText("Logs/road-water-play-review.json",JsonUtility.ToJson(report,true));File.AppendAllText("Logs/pavement-ownership-review.txt",report.result+"\n");
            waterSteps=null;SessionState.SetInt(Key+"Stage",100);RestoreInput();EditorApplication.ExitPlaymode();
        }

        [Serializable]sealed class StructuresResult {public string result,utc;public int funds,publicSections,airsideSections;public float truckElevation;}
        static void ShowTunnelTruck()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AirCity/Vehicles/BaggageTractor/BaggageTractor.prefab");
            Require(prefab!=null,"The real baggage tractor prefab is missing.");
            var type=prefab.GetComponent<Vehicle>().type;var net=game.Airport.Taxiways;
            int end=net.nodes.First(n=>Vector2.Distance(new Vector2(n.position.x,n.position.z),BoreExit)<.1f).id;
            Require(BaggageRoute.Plan(net,tunnelStart,end,8,8,type.Width+.5f,out var route,out var why),why);
            float at=0;
            for(float d=0;d<=route.Length;d+=.5f){var p=route.Sample(d);if(Mathf.Abs(p.x)<1f){at=d;break;}}
            Require(at>0,"The service route does not pass beneath the taxiway.");
            var point=route.Pose(at,out var facing,out _);float elevation=route.Height(at);reviewedTruckHeight=elevation+game.Airport.transform.position.y;
            Require(Mathf.Abs(elevation+10)<.05f,"The service route lost its deck height.");
            reviewTruck=Object.Instantiate(prefab,game.Airport.transform);reviewTruck.name="Underpass review tractor";reviewTruck.hideFlags=HideFlags.DontSave;
            var grade=game.Airport.RoadGradientAt(point,elevation);
            reviewTruck.GetComponent<Vehicle>().Place(game.Airport.transform.TransformPoint(new Vector3(point.x,elevation,point.y)),game.Airport.transform.TransformDirection(new Vector3(facing.x,Vector2.Dot(grade,facing),facing.y)),0,0,0,false);
        }
    }
}
