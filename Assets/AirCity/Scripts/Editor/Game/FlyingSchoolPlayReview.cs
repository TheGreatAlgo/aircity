using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.Game;
using AirCity.Traffic;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>The flying school in the real game: the shared build tool's preview and mouse purchase, the driveway
    /// in the saved road graph, buying an aircraft and hiring, a lesson flown and paid for, a circuit with a
    /// touch-and-go, and a private save/reload. Run in an isolated review project (it opens the Game view).</summary>
    [InitializeOnLoad]
    public static class FlyingSchoolPlayReview
    {
        const string Key="AirCity.FlyingSchoolPlayReview.",Output="Captures/FlyingSchool/Play/",Slot="Logs/FlyingSchoolPlayReview";
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.FlattenHierarchy;
        // The clubhouse stands north of the starter taxiway (z = -180), its flight line snapped onto it.
        static readonly Vector2 Clubhouse=new Vector2(-150f,-180f+FlyingSchoolLayout.BuildingCentre(7.5f));
        static EditorWindow window;
        static CareerGame reviewGame;
        static Mouse reviewMouse;
        static Keyboard reviewKeyboard;
        static bool held;
        static double next,stageStarted;
        static int initialFunds,quotedPrice,fundsBeforeLessons,studentsSaved,lessonsSaved;
        static long earnedSaved;
        static string originalRoads,siteId;
        static Vector2 target;
        static Flight circuit;
        [Serializable] sealed class ViewState {public string id;public int index;}
        [Serializable] sealed class Views {public List<ViewState> items=new List<ViewState>();}
        [Serializable] sealed class Result
        {public bool previewPure,placedByMouse,roadConnected,groundOwned,lessonFlownAndPaid,circuitFlown,savedAndReloaded;public int price,lessons,touchAndGoes,students;public long earned,wages;public string saveDirectory;}
        static FlyingSchoolPlayReview()
        {if(SessionState.GetBool(Key+"Running",false))EditorApplication.update+=Update;}

        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var game=NewGameSetup.Build();
            game.enabled=false;
            typeof(CareerGame).GetField("store",Instance).SetValue(game,new CareerSaveStore(Path.GetFullPath(Slot)));
            Directory.CreateDirectory(Output);Directory.CreateDirectory(Slot);
            foreach(string name in new[]{"preview","built","manage","lesson","students","circuit","reloaded"})File.Delete(Output+name+".png");
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
            // The editor draws a cyan placeholder while a shader variant compiles in the background; captures wait instead.
            SessionState.SetBool(Key+"AsyncShaders",ShaderUtil.allowAsyncCompilation);ShaderUtil.allowAsyncCompilation=false;
            SessionState.SetBool(Key+"Running",true);SessionState.SetInt(Key+"Stage",0);
            SessionState.SetFloat(Key+"Started",(float)EditorApplication.timeSinceStartup);
            EditorApplication.update-=Update;EditorApplication.update+=Update;EditorApplication.EnterPlaymode();
        }

        static FlyingSchool School(CareerGame game)=>game.Airport!=null?game.Airport.FlyingSchools.FirstOrDefault():null;
        static void Tab(CareerGame game,int tab)=>
            typeof(DevToolbar).GetField("schoolTab",Instance).SetValue(game.BuildTools.GetComponent<DevToolbar>(),tab);
        static void Look(CareerGame game,float range=150f,float bearing=160f)=>
            game.view.FocusPoint(new Vector3(Clubhouse.x,100f,Clubhouse.y-6f),range,42f,bearing);

        static void Update()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"Started",0f)>520f)
                    throw new TimeoutException("Flying school play review timed out at stage "+SessionState.GetInt(Key+"Stage",0)+".");
                int stage=SessionState.GetInt(Key+"Stage",0);
                if(stage==20 && !EditorApplication.isPlaying){Finish(0);return;}
                if(!EditorApplication.isPlaying)return;
                var game=Object.FindAnyObjectByType<CareerGame>();if(game==null || stage>0 && game.State==null)return;
                if(stage==0)
                {
                    typeof(CareerGame).GetField("store",Instance).SetValue(game,new CareerSaveStore(Path.GetFullPath(Slot)));
                    game.Initialize(Fixture(),false);game.Paused=true;game.enabled=true;reviewGame=game;
                    reviewMouse=InputSystem.AddDevice<Mouse>("Flying school review mouse");reviewMouse.MakeCurrent();
                    reviewKeyboard=InputSystem.AddDevice<Keyboard>("Flying school review keyboard");reviewKeyboard.MakeCurrent();
                    InputSystem.onBeforeUpdate+=FeedInput;held=false;target=Clubhouse;
                    Look(game);
                    game.BuildTools.PlacementSnap=true;
                    game.BuildTools.SetTool(DevTool.FlyingSchool);
                    Require(game.BuildTools.Tool==DevTool.FlyingSchool,"The flying school tool is not available: "+game.Message);
                    DevGui.Visible=true;game.BuildTools.GetComponent<DevToolbar>().ShowMore=false;
                    initialFunds=game.State.funds;originalRoads=JsonUtility.ToJson(game.State.publicRoads);
                    Go(1,5f);return;
                }
                if(EditorApplication.timeSinceStartup<next)return;
                ShaderUtil.allowAsyncCompilation=false;
                if(ShaderUtil.anythingCompiling){next=EditorApplication.timeSinceStartup+.2f;return;}
                var school=School(game);
                switch(stage)
                {
                    case 1:
                        Require(game.BuildTools.FlyingSchoolProblem==null,"The preview is refused: "+game.BuildTools.FlyingSchoolProblem);
                        Require(game.BuildTools.FlyingSchoolSnapped,"The flight line did not snap onto the taxiway.");
                        Require(game.BuildTools.PlacementPreviewVisible,"The construction preview is not shown.");
                        Require(game.BuildTools.FlyingSchoolPrice>FlyingSchoolRules.BuildPrice,"The quote leaves out the driveway.");
                        Require(game.State.funds==initialFunds && school==null && game.Airport.Taxiways.stands.Count(s=>s.Owned)==0,"The preview spent funds or built something.");
                        Require(JsonUtility.ToJson(game.State.publicRoads)==originalRoads,"The preview changed the live road graph.");
                        if(!Capture("preview"))return;
                        quotedPrice=game.BuildTools.FlyingSchoolPrice;held=true;Go(2,.3f);break;
                    case 2:held=false;Go(3,3f);break;
                    case 3:
                        Require(school!=null,"The mouse click built no school: "+game.BuildTools.FlyingSchoolProblem+" / "+game.Message);
                        Require(game.BuildTools.SelectedFlyingSchool==school,"The new school is not selected.");
                        Require(game.State.funds==initialFunds-quotedPrice,$"The purchase charged {initialFunds-game.State.funds}, not its quote of {quotedPrice}.");
                        Require(school.AccessProblem==null,"The flight line is not connected: "+school.AccessProblem);
                        Require(school.RoadProblem==null,"The driveway is not connected: "+school.RoadProblem);
                        CheckSite(game,school);siteId=school.SiteId;
                        if(!Capture("built"))return;
                        // The Manage tab's purchases, as its buttons make them.
                        game.Spend(FlyingSchoolRules.HireFee);Require(school.Hire(),"Could not hire an instructor.");
                        var type=game.Airport.aircraftTypes.First(t=>FlyingSchoolLayout.TrainerProblem(t)==null);
                        game.Spend(FlyingSchoolRules.TrainerPrice);Require(school.BuyTrainer(type,out var why),"Could not buy a trainer: "+why);
                        Require(school.Trainers[0].aircraft!=null && school.AtHome(school.Trainers[0]),"The new trainer is not on its tie-down.");
                        Tab(game,1);Go(4,1.5f);break;
                    case 4:
                        if(!Capture("manage"))return;
                        Tab(game,0);fundsBeforeLessons=game.State.funds;
                        game.Paused=false;game.Speed=60;stageStarted=EditorApplication.timeSinceStartup;Go(5,.5f);break;
                    case 5:
                        // The first student's introductory flight: out, away and back.
                        if(school.LessonsFlown<1)
                        {
                            Require(EditorApplication.timeSinceStartup-stageStarted<150f,"No lesson was flown: "+(school.Trainers[0].status??"no status")+" / students "+school.Students.Count);
                            return;
                        }
                        game.Paused=true;school.LessonsPaused=true;
                        Require(school.Earned>=FlyingSchoolRules.DualFee,"The lesson earned no fee.");
                        Require(game.State.funds==fundsBeforeLessons+(int)(school.Earned-school.WagesPaid-school.Unbanked),
                            $"Funds {game.State.funds} do not match fees and wages ({fundsBeforeLessons} + {school.Earned} - {school.WagesPaid} - {school.Unbanked}).");
                        Require(school.AtHome(school.Trainers[0]),"The trainer did not come back to its tie-down.");
                        Tab(game,0);Go(6,1.5f);break;
                    case 6:
                        if(!Capture("lesson"))return;
                        Tab(game,2);Go(7,1.5f);break;
                    case 7:
                        if(!Capture("students"))return;
                        Tab(game,0);
                        circuit=game.Airport.Traffic.DepartForCircuits(school.Trainers[0].aircraft,1,out var problem);
                        Require(circuit!=null,"The trainer cannot leave for circuits: "+problem);
                        game.Paused=false;game.Speed=20;stageStarted=EditorApplication.timeSinceStartup;Go(8,.2f);break;
                    case 8:
                        Require(EditorApplication.timeSinceStartup-stageStarted<120f,"The circuit was never joined: "+circuit.Status);
                        if(circuit.Phase!=FlightPhase.Pattern || !circuit.Status.Contains("downwind"))return;
                        game.Paused=true;
                        var at=circuit.aircraft.transform.position;
                        // Looking north over the aircraft on downwind, the airfield beyond it.
                        game.view.FocusPoint(at,230f,17f,0f);
                        Go(9,2f);break;
                    case 9:
                        if(!Capture("circuit"))return;
                        Look(game);game.Paused=false;game.Speed=20;stageStarted=EditorApplication.timeSinceStartup;Go(10,.2f);break;
                    case 10:
                        if(!circuit.Finished)
                        {Require(EditorApplication.timeSinceStartup-stageStarted<150f,"The circuit did not finish: "+circuit.Status);return;}
                        game.Paused=true;
                        Require(circuit.Phase==FlightPhase.Parked && circuit.TouchAndGoes==1,"The circuit ended "+circuit.Status+" after "+circuit.TouchAndGoes+" touch-and-goes.");
                        Require(school.AtHome(school.Trainers[0]),"The trainer did not return to its tie-down after circuits.");
                        school.LessonsPaused=false;
                        studentsSaved=school.Students.Count;lessonsSaved=school.LessonsFlown;earnedSaved=school.Earned;
                        // The fixture was opened without saving; the review writes to its own private slot.
                        typeof(CareerGame).GetField("persist",Instance).SetValue(game,true);
                        Require(game.Save(false),"Private save failed: "+game.Message);
                        Require(File.Exists(Path.Combine(Slot,"career.json")),"The private review save was not written.");
                        int funds=game.State.funds;
                        Require(game.Continue(),"The saved airport did not resume: "+game.Message);
                        game.Paused=true;Look(game);
                        Require(game.State.funds==funds,"Reloading changed the balance.");
                        Go(11,5f);break;
                    case 11:
                        Require(school!=null && school.SiteId==siteId,"The school was lost on reload.");
                        Require(school.Instructors==1 && school.Trainers.Count==1 && school.Students.Count==studentsSaved &&
                            school.LessonsFlown==lessonsSaved && school.Earned==earnedSaved,"The school's staff, aircraft, students or accounts changed on reload.");
                        Require(school.Trainers[0].aircraft!=null && school.AtHome(school.Trainers[0]),"The reloaded trainer is not on its tie-down.");
                        Require(school.AccessProblem==null && school.RoadProblem==null,"The reloaded school lost its taxiway or driveway: "+(school.AccessProblem??school.RoadProblem));
                        Require(game.Airport.FlyingSchools.Count()==1 && game.Airport.Taxiways.stands.Count(s=>s.owner==siteId)==FlyingSchoolLayout.Spots,"Reload duplicated the school or lost its tie-downs.");
                        CheckSite(game,school);
                        game.BuildTools.SelectFlyingSchool(school);Tab(game,0);Go(12,1.5f);break;
                    case 12:
                        if(!Capture("reloaded"))return;
                        File.WriteAllText(Path.Combine(Slot,"review.json"),JsonUtility.ToJson(new Result{
                            previewPure=true,placedByMouse=true,roadConnected=true,groundOwned=true,lessonFlownAndPaid=true,circuitFlown=true,savedAndReloaded=true,
                            price=quotedPrice,lessons=school.LessonsFlown,touchAndGoes=1,students=school.Students.Count,earned=school.Earned,wages=school.WagesPaid,
                            saveDirectory=Path.GetFullPath(Slot)},true));
                        Debug.Log("FLYING SCHOOL PLAY passed: live preview, real mouse purchase at the quoted price, driveway in the road graph, ground ownership, "+
                            "a lesson flown and paid, a circuit with a touch-and-go, and private save/reload.");
                        Go(20,0f);EditorApplication.ExitPlaymode();break;
                }
            }
            catch(Exception e){Debug.LogException(e);Finish(1);}
        }

        static void CheckSite(CareerGame game,FlyingSchool school)
        {
            var graph=game.State.publicRoads;
            var port=graph.nodes.SingleOrDefault(n=>n.terminalEntrance==school.EntranceId);
            Require(port!=null,"The saved road graph has no entrance node for the school.");
            Require(graph.nodes.Where(n=>n.outsideConnection).Any(n=>graph.HasRoute(port.id,n.id)&&graph.HasRoute(n.id,port.id)),"The car park has no two-way regional access.");
            var origin=new Vector2(game.Airport.transform.position.x,game.Airport.transform.position.z);
            float lane=school.Lane;
            Vector2 World(float x,float z)=>FlyingSchoolLayout.Point(school.Origin,school.Heading,new Vector2(x,z))+origin;
            Require(game.valley.BuiltSurfaces!=null && game.valley.BuiltSurfaces.Contains(World(0f,FlyingSchoolLayout.AisleCentre(lane))),"The car park does not own its ground.");
            Require(game.valley.BuiltSurfaces.Contains(World(0f,FlyingSchoolLayout.ApronDepth(lane)+2f)),"The terrace does not own its ground.");
            Require(game.valley.BuiltSurfaces.Contains(World(FlyingSchoolLayout.SpotOffset(0),FlyingSchoolLayout.ApronDepth(lane)-8f)),"The flight line's tie-down does not own its ground.");
            Require(!game.valley.BuiltSurfaces.Contains(World(-FlyingSchoolLayout.BlockHalfWidth-6f,FlyingSchoolLayout.BuildingCentre(lane))),"Ground beside the clubhouse was claimed.");
        }

        static CareerState Fixture()
        {
            int count=ValleyWorld.Resolution*ValleyWorld.Resolution;
            var world=new ValleyWorld {generatorVersion=ValleyWorld.GeneratorVersion,forestVersion=ForestPopulation.Version,preset=RegionPreset.Woodland,
                seed=42,hasOcean=false,hasRiver=false,seaLevel=1f,heights=Enumerable.Repeat(100f,count).ToArray(),
                disturbance=new float[count],landCover=Enumerable.Repeat((byte)30,count).ToArray(),roadHeights=new[]{100f,100f}};
            world.sites.Add(new AirportSite{name="Flying school review",centre=Vector2.zero,elevation=100f});
            world.sites.Add(new AirportSite{name="Distant east",centre=new Vector2(900f,-900f),elevation=100f});
            world.sites.Add(new AirportSite{name="Distant west",centre=new Vector2(-900f,900f),elevation=100f});
            world.publicRoad.Add(new Vector2(-300f,-60f));world.publicRoad.Add(new Vector2(300f,-60f));
            world.settlements.Add(new Vector2(-2200f,-2200f));world.settlements.Add(new Vector2(2200f,2200f));
            world.RemoveGroundProps(GroundCoverScatter.Generate(world,area:new Rect(-460f,-360f,920f,340f)).Select(p=>p.id));
            var roads=new PublicRoadNetwork();
            int a=roads.AddNode(new Vector3(-300f,100f,-60f)),e=roads.AddNode(new Vector3(80f,100f,-60f),"airport-reception"),b=roads.AddNode(new Vector3(300f,100f,-60f));
            roads.Node(a).outsideConnection=roads.Node(b).outsideConnection=true;
            roads.AddSegment(a,e,PublicRoadStyle.AirportAccess,RoadStructure.Ground);roads.AddSegment(e,b,PublicRoadStyle.AirportAccess,RoadStructure.Ground);
            // The starter grass airfield: an 800 m strip, a taxiway loop to both ends and one public stand.
            var layout=new CareerLayout {unlocked=255,elevation=100f,runwayBuilt=true,standBuilt=true,
                runwayA=new Vector2(-400f,-300f),runwayB=new Vector2(400f,-300f),stand=new Vector2(120f,-180f),standAlong=Vector2.right,standHeading=180f};
            layout.taxiways.Add(new CareerTaxiway {points=new List<Vector2>{new Vector2(-350f,-300f),new Vector2(-350f,-180f),new Vector2(350f,-180f),new Vector2(350f,-300f)}});
            var state=new CareerState {world=world,selectedSite=0,handsOn=true,wholeRegion=true,mode=GameMode.Career,funds=1000000,layout=layout,
                publicRoads=roads,accessRoadRoute=new List<Vector2>{new Vector2(80f,-60f),new Vector2(300f,-60f)}};
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
            var size=Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize",true),Instance,null,new[]{kind,(object)1440,900,"Flying school review temporary"},null);
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
                File.WriteAllBytes(Output+name+".png",image.EncodeToPNG());Debug.Log("FLYING SCHOOL PLAY captured "+Output+name+".png");return true;
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
            ShaderUtil.allowAsyncCompilation=SessionState.GetBool(Key+"AsyncShaders",true);
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
