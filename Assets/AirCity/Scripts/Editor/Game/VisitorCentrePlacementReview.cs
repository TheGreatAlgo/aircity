using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Live shared-tool placement and private save/load review. Run in an isolated review project.</summary>
    [InitializeOnLoad]
    public static class VisitorCentrePlacementReview
    {
        const string Key="AirCity.VisitorCentrePlacementReview.",Output="Captures/VisitorCentre/Placement/",Slot="Logs/VisitorCentrePlacementReview";
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.FlattenHierarchy;
        static EditorWindow window;
        static CareerGame reviewGame;
        static Mouse reviewMouse;
        static Keyboard reviewKeyboard;
        static bool held;
        static double next;
        static int initialFunds,quotedPrice;
        static string originalRoads;
        static Vector2 target;
        [Serializable] sealed class ViewState {public string id;public int index;}
        [Serializable] sealed class Views {public List<ViewState> items=new List<ViewState>();}
        [Serializable] sealed class Result
        {public bool previewPure,rotatedPreview,placedByMouse,roadConnected,groundOwned,savedAndReloaded;public int bays,price,fundsBefore,fundsAfter;public string saveDirectory;}
        static VisitorCentrePlacementReview()
        {if(SessionState.GetBool(Key+"Running",false))EditorApplication.update+=Update;}

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var game=NewGameSetup.Build();
            game.enabled=false;
            typeof(CareerGame).GetField("store",Instance).SetValue(game,new CareerSaveStore(Path.GetFullPath(Slot)));
            Directory.CreateDirectory(Output);Directory.CreateDirectory(Slot);
            foreach(string name in new[]{"preview","preview-rotated","built","reloaded"})File.Delete(Output+name+".png");
            window=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            window.position=new Rect(20,20,1440,930);window.Show();window.Focus();
            var saved=new Views();
            foreach(var view in GameViews())saved.items.Add(new ViewState{id=view.GetEntityId().ToString(),index=(int)view.GetType().GetProperty("selectedSizeIndex",Instance).GetValue(view)});
            SessionState.SetString(Key+"Views",JsonUtility.ToJson(saved));
            SessionState.SetInt(Key+"Input",(int)InputSystem.settings.editorInputBehaviorInPlayMode);
            SessionState.SetInt(Key+"BackgroundInput",(int)InputSystem.settings.backgroundBehavior);
            SessionState.SetBool(Key+"Background",Application.runInBackground);
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.runInBackground=true;SetReviewSize();
            SessionState.SetBool(Key+"Running",true);SessionState.SetInt(Key+"Stage",0);
            SessionState.SetFloat(Key+"Started",(float)EditorApplication.timeSinceStartup);
            EditorApplication.update-=Update;EditorApplication.update+=Update;EditorApplication.EnterPlaymode();
        }

        static void Update()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"Started",0f)>210f)
                    throw new TimeoutException("Visitor centre placement review timed out.");
                int stage=SessionState.GetInt(Key+"Stage",0);
                if(stage==8 && !EditorApplication.isPlaying){Finish(0);return;}
                if(!EditorApplication.isPlaying)return;
                var game=Object.FindAnyObjectByType<CareerGame>();if(game==null || stage>0 && game.State==null)return;
                var tool=game.GetComponent<CareerBuildTool>();
                if(stage==0)
                {
                    typeof(CareerGame).GetField("store",Instance).SetValue(game,new CareerSaveStore(Path.GetFullPath(Slot)));
                    game.Initialize(Fixture(),false);game.Paused=true;game.enabled=true;reviewGame=game;
                    reviewMouse=InputSystem.AddDevice<Mouse>("Visitor centre review mouse");reviewMouse.MakeCurrent();
                    reviewKeyboard=InputSystem.AddDevice<Keyboard>("Visitor centre review keyboard");reviewKeyboard.MakeCurrent();
                    InputSystem.onBeforeUpdate+=FeedInput;held=false;target=Vector2.zero;
                    game.view.FocusPoint(new Vector3(9f,100f,17f),118f,43f,202f);
                    game.BuildTools.SetTool(DevTool.Reception);tool.Choose(CareerTool.Reception);tool.BuildingHeading=0f;
                    DevGui.Visible=true;game.BuildTools.GetComponent<DevToolbar>().ShowMore=false;
                    initialFunds=game.State.funds;originalRoads=JsonUtility.ToJson(game.State.publicRoads);
                    Go(1,5f);return;
                }
                if(EditorApplication.timeSinceStartup<next)return;
                switch(stage)
                {
                    case 1:
                        CheckPreview(game,tool);
                        if(!Capture("preview"))return;
                        tool.RotatePlacement(90f);Go(2,2f);break;
                    case 2:
                        CheckPreview(game,tool);
                        Require(Mathf.Abs(Mathf.DeltaAngle(tool.BuildingHeading,90f))<.1f,"Rotation did not reach the shared placement draft.");
                        if(!Capture("preview-rotated"))return;
                        tool.RotatePlacement(-90f);Go(3,2f);break;
                    case 3:
                        CheckPreview(game,tool);quotedPrice=tool.Price;held=true;Go(4,.3f);break;
                    case 4:held=false;Go(5,3f);break;
                    case 5:
                        CheckBuilt(game);
                        Require(game.State.funds==initialFunds-quotedPrice,"The mouse purchase did not charge its exact preview quote.");
                        Require(Vector2.Distance(game.State.layout.reception,Vector2.zero)<.15f,"The mouse placed a different site from its preview.");
                        if(!Capture("built"))return;
                        Require(game.Save(false),"Private save failed: "+game.Message);
                        Require(File.Exists(Path.Combine(Slot,"career.json")),"The private review save was not written.");
                        Require(game.Continue(),"Private saved airport did not resume: "+game.Message);
                        game.Paused=true;
                        game.view.FocusPoint(new Vector3(9f,100f,17f),118f,43f,202f);
                        Go(6,5f);break;
                    case 6:
                        CheckBuilt(game);
                        Require(game.State.funds==initialFunds-quotedPrice,"Reload charged the building twice or lost its balance.");
                        if(!Capture("reloaded"))return;
                        File.WriteAllText(Path.Combine(Slot,"review.json"),JsonUtility.ToJson(new Result{
                            previewPure=true,rotatedPreview=true,placedByMouse=true,roadConnected=true,groundOwned=true,savedAndReloaded=true,
                            bays=VisitorCentreLayout.ParkingBays.Count,price=quotedPrice,fundsBefore=initialFunds,fundsAfter=game.State.funds,
                            saveDirectory=Path.GetFullPath(Slot)},true));
                        Debug.Log("VISITOR CENTRE PLACEMENT passed: live 0/90 degree previews, real mouse purchase, exact charge, 18 bays, public graph connection, ground ownership and private save/reload.");
                        Go(8,0f);EditorApplication.ExitPlaymode();break;
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }

        static void CheckPreview(CareerGame game,CareerBuildTool tool)
        {
            Require(tool.Tool==CareerTool.Reception && tool.Valid,"The shared reception preview is invalid: "+tool.Status);
            Require(game.State.funds==initialFunds && !game.State.layout.receptionBuilt,"Preview spent funds or committed a building.");
            Require(JsonUtility.ToJson(game.State.publicRoads)==originalRoads,"Preview mutated the live public road graph.");
            var preview=typeof(CareerBuildTool).GetField("receptionPreview",Instance).GetValue(tool) as GameObject;
            var driveway=typeof(CareerBuildTool).GetField("visitorDrivewayFilter",Instance).GetValue(tool) as MeshFilter;
            Require(preview!=null && preview.activeInHierarchy && VisitorCentreSiteBuilder.GroundMesh(preview)!=null,"The ghost omitted the building or parking ground.");
            Require(driveway!=null && driveway.gameObject.activeInHierarchy && driveway.sharedMesh!=null && driveway.sharedMesh.vertexCount>0,"The ghost omitted its real road connection.");
            Require(game.valley.BuiltSurfaces!=null && !game.valley.BuiltSurfaces.Contains(new Vector2(9f,21.5f)),"Preview registered permanent parking ground.");
        }

        static void CheckBuilt(CareerGame game)
        {
            Require(game.State.layout.receptionBuilt && game.State.layout.receptionSiteVersion==VisitorCentreConstruction.SiteVersion,"The complete visitor site was not saved.");
            Require(VisitorCentreLayout.ParkingBays.Count==18 && VisitorCentreLayout.ParkingBays.Count(b=>b.accessible)==2,"Parking bay design changed.");
            var centre=typeof(CareerGame).GetField("reception",Instance).GetValue(game) as GameObject;
            Require(centre!=null && centre.activeInHierarchy && VisitorCentreSiteBuilder.GroundMesh(centre)!=null,"The placed site is missing its model or lot.");
            Require(game.valley.GetComponentsInChildren<VisitorCentreVisual>().Count(v=>v.name=="Visitor centre")==1,"The placed or loaded centre was duplicated.");
            var graph=game.State.publicRoads;
            var port=graph.nodes.Single(n=>n.terminalEntrance==VisitorCentreLayout.EntranceId);
            var original=graph.nodes.Single(n=>n.terminalEntrance=="airport-reception");
            Require(graph.HasRoute(port.id,original.id) && graph.HasRoute(original.id,port.id),"The parking lot is not connected both ways to the existing airport entrance.");
            Require(graph.nodes.Where(n=>n.outsideConnection).Any(n=>graph.HasRoute(port.id,n.id)&&graph.HasRoute(n.id,port.id)),"The site has no bidirectional regional access.");
            Require(game.valley.BuiltSurfaces!=null && game.valley.BuiltSurfaces.Contains(new Vector2(9f,21.5f)),"The actual parking pavement does not own its ground.");
            Require(!game.valley.BuiltSurfaces.Contains(new Vector2(19f,-7f)),"The roof or fixture projection clipped land beside the building.");
        }

        static CareerState Fixture()
        {
            int count=ValleyWorld.Resolution*ValleyWorld.Resolution;
            var world=new ValleyWorld {generatorVersion=ValleyWorld.GeneratorVersion,forestVersion=ForestPopulation.Version,preset=RegionPreset.Woodland,
                seed=42,hasOcean=false,hasRiver=false,seaLevel=1f,heights=Enumerable.Repeat(100f,count).ToArray(),
                disturbance=new float[count],landCover=Enumerable.Repeat((byte)30,count).ToArray(),roadHeights=new[]{100f,100f}};
            world.sites.Add(new AirportSite{name="Visitor centre review",centre=Vector2.zero,elevation=100f});
            world.sites.Add(new AirportSite{name="Distant east",centre=new Vector2(900f,-900f),elevation=100f});
            world.sites.Add(new AirportSite{name="Distant west",centre=new Vector2(-900f,900f),elevation=100f});
            world.publicRoad.Add(new Vector2(-150f,46f));world.publicRoad.Add(new Vector2(150f,46f));
            world.settlements.Add(new Vector2(-2200f,-2200f));world.settlements.Add(new Vector2(2200f,2200f));
            world.RemoveGroundProps(GroundCoverScatter.Generate(world,area:new Rect(-110f,-100f,270f,230f)).Select(p=>p.id));
            var roads=new PublicRoadNetwork();
            int a=roads.AddNode(new Vector3(-150f,100f,46f)),e=roads.AddNode(new Vector3(80f,100f,46f),"airport-reception"),b=roads.AddNode(new Vector3(150f,100f,46f));
            roads.Node(a).outsideConnection=roads.Node(b).outsideConnection=true;
            roads.AddSegment(a,e,PublicRoadStyle.AirportAccess,RoadStructure.Ground);roads.AddSegment(e,b,PublicRoadStyle.AirportAccess,RoadStructure.Ground);
            var layout=new CareerLayout {unlocked=255,elevation=100f,runwayBuilt=true,standBuilt=true,
                runwayA=new Vector2(-400f,-300f),runwayB=new Vector2(400f,-300f),stand=new Vector2(0f,-180f),standAlong=Vector2.right,standHeading=180f};
            layout.taxiways.Add(new CareerTaxiway {points=new List<Vector2>{new Vector2(-350f,-300f),new Vector2(-350f,-180f),new Vector2(100f,-180f)}});
            var state=new CareerState {world=world,selectedSite=0,handsOn=true,wholeRegion=true,mode=GameMode.Career,funds=1000000,layout=layout,
                publicRoads=roads,accessRoadRoute=new List<Vector2>{new Vector2(80f,46f),new Vector2(150f,46f)}};
            Require(state.Validate(out var why),"Prepared review fixture is invalid: "+why);return state;
        }

        static void FeedInput()
        {
            if(InputState.currentUpdateType!=InputUpdateType.Dynamic || reviewMouse==null || reviewGame==null)return;
            var texture=Target();if(texture==null)return;
            var at=reviewGame.view.GetComponent<Camera>().WorldToViewportPoint(new Vector3(target.x,100f,target.y));
            reviewMouse.MakeCurrent();reviewKeyboard.MakeCurrent();
            InputSystem.QueueStateEvent(reviewMouse,new MouseState{position=new Vector2(at.x*texture.width,at.y*texture.height),buttons=(ushort)(held?1:0)});
            InputSystem.QueueStateEvent(reviewKeyboard,new KeyboardState());
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Go(int stage,float wait){SessionState.SetInt(Key+"Stage",stage);next=EditorApplication.timeSinceStartup+wait;}
        static EditorWindow[] GameViews()=>Resources.FindObjectsOfTypeAll<EditorWindow>().Where(v=>v.GetType().FullName=="UnityEditor.GameView").ToArray();
        static object SizeGroup()
        {
            var type=typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizes",true);
            return type.GetMethod("GetGroup",Instance).Invoke(type.GetProperty("instance",Static).GetValue(null),
                new[]{Enum.ToObject(typeof(Editor).Assembly.GetType("UnityEditor.GameViewSizeGroupType",true),0)});
        }
        static void SetReviewSize()
        {
            var group=SizeGroup();var assembly=typeof(Editor).Assembly;
            var kind=Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType",true),"FixedResolution");
            var size=Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize",true),Instance,null,new[]{kind,(object)1440,900,"Visitor centre review temporary"},null);
            int index=(int)group.GetType().GetMethod("GetTotalCount",Instance).Invoke(group,null);
            group.GetType().GetMethod("AddCustomSize",Instance).Invoke(group,new[]{size});SessionState.SetInt(Key+"Size",index);
            foreach(var view in GameViews()){view.GetType().GetProperty("selectedSizeIndex",Instance).SetValue(view,index);view.Repaint();}
        }
        static RenderTexture Target()
        {
            if(window==null)window=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            return window.GetType().GetField("m_RenderTexture",Instance)?.GetValue(window) as RenderTexture;
        }
        static bool Capture(string name)
        {
            var texture=Target();
            if(texture==null || !texture.IsCreated() || texture.width!=1440 || texture.height!=900){window.Repaint();return false;}
            var previous=RenderTexture.active;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();
                var pixels=image.GetPixels32();
                // The fixed dark top rail distinguishes orientation even when the lower-right inspector
                // is also a pale card. Sample its blank left margin, away from brand text and ground shadows.
                bool Header(int row)
                {
                    int dark=0;
                    foreach(int x in new[]{8,12,20})
                    {var p=pixels[row*texture.width+x];if(p.r<65 && p.g<65 && p.b<65)dark++;}
                    return dark>=2;
                }
                if(Header(10) && !Header(texture.height-11))
                {
                    var flipped=new Color32[pixels.Length];
                    for(int row=0;row<texture.height;row++)Array.Copy(pixels,row*texture.width,flipped,(texture.height-1-row)*texture.width,texture.width);
                    image.SetPixels32(flipped);image.Apply();
                }
                File.WriteAllBytes(Output+name+".png",image.EncodeToPNG());Debug.Log("VISITOR CENTRE PLACEMENT captured "+Output+name+".png");return true;
            }
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        static void Finish(int result)
        {
            SessionState.SetBool(Key+"Running",false);EditorApplication.update-=Update;InputSystem.onBeforeUpdate-=FeedInput;
            if(reviewMouse!=null && reviewMouse.added)InputSystem.RemoveDevice(reviewMouse);
            if(reviewKeyboard!=null && reviewKeyboard.added)InputSystem.RemoveDevice(reviewKeyboard);
            InputSystem.settings.editorInputBehaviorInPlayMode=(InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Key+"Input",0);
            InputSystem.settings.backgroundBehavior=(InputSettings.BackgroundBehavior)SessionState.GetInt(Key+"BackgroundInput",0);
            Application.runInBackground=SessionState.GetBool(Key+"Background",false);
            try
            {
                var saved=JsonUtility.FromJson<Views>(SessionState.GetString(Key+"Views","{\"items\":[]}"));
                foreach(var state in saved.items)
                {var view=GameViews().FirstOrDefault(v=>v.GetEntityId().ToString()==state.id);if(view!=null)view.GetType().GetProperty("selectedSizeIndex",Instance).SetValue(view,state.index);}
                int index=SessionState.GetInt(Key+"Size",-1);
                if(index>=0){var group=SizeGroup();group.GetType().GetMethod("RemoveCustomSize",Instance).Invoke(group,new object[]{index});}
                SessionState.SetInt(Key+"Size",-1);
            }
            catch(Exception e){Debug.LogException(e);result=1;}
            EditorApplication.Exit(result);
        }
    }
}
