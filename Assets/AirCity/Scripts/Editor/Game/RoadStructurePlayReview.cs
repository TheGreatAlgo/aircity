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
                    StructureCommitted();Require(game.Airport.Taxiways.edges.Count==3,"A separated crossing created a false graph junction.");
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
