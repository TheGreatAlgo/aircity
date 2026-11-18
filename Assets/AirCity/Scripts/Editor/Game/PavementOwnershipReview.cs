using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Dev;
using AirCity.Airfield;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Actual shared taxiway, service-road and street placement and map prices on a controlled hill. Use an isolated non-batch editor.
    /// Captures the GameView's own IMGUI-inclusive render target, without waiting on ScreenCapture's end-of-frame queue.</summary>
    [InitializeOnLoad]
    public static partial class PavementOwnershipReview
    {
        const string Key = "AirCity.PavementOwnershipReview.";
        const string Folder = "Captures/NewGame/PavementOwnership/";
        const string SaveFolder = "Logs/PavementOwnershipReview";
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        const int SavedFunds = 1000000;
        static CareerGame game;
        static CareerHud hud;
        static EditorWindow window;
        static Mouse mouse;
        static Keyboard keyboard;
        static Vector2 pointer;
        static bool pressed, releasePending;
        static double next, releaseAt, actionDeadline;
        static int width, height;
        static readonly Vector2 Start=new Vector2(-350,300),End=new Vector2(350,300);
        static readonly Vector2 RoadStart=new Vector2(-300,-150),RoadMiddle=new Vector2(0,-150),RoadEnd=new Vector2(300,-150);
        static readonly Vector2 StreetStart=new Vector2(-300,-500),StreetEnd=new Vector2(300,-500);
        static int totalPrice,edgesBefore,initialTreeCount,initialRoadProps;
        static bool stressVerified;
        static readonly Vector2 GrassStand=new Vector2(180,20),PavedStand=new Vector2(-180,20);
        static int standCountBefore;
        static float grassStandHeight;
        static readonly Vector2 RoadCurveEnd=new Vector2(450,20),StreetCurveEnd=new Vector2(450,-350),StreetTEnd=new Vector2(0,-290);
        static string previewGraph;
        static float[] previewHeights;
        static int previewFunds;
        static DevAirfieldTool tool;
        static float[] originalHeights;
        static int quotedPrice;

        static readonly List<Shot> shots = new List<Shot>();
        [Serializable] sealed class Shot { public string name; public int width, height, price, gradingPrice; public float slope; }
        [Serializable] sealed class Result
        {
            public string result, unityVersion, utc;
            public int totalPrice;
            public int clearedTrees,clearedProps,triangleCount;
            public bool textureSurvivesForcedTerrainRaise,saveLoadVerified;
            public List<Shot> screenshots;
        }
        [Serializable] sealed class SavedGameView { public string id; public int size; }
        [Serializable] sealed class GameViewSetup { public SavedGameView[] views; }
        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class Setup { public SavedScene[] scenes; }

        static PavementOwnershipReview()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update;
        }

        [MenuItem("AirCity/Game/Review Pavement ownership")]
        public static void Run() => Begin(false);
        public static void RunFromCommandLine() => Begin(true);

        static void Begin(bool exitEditor,bool performanceOnly=false)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before running the airside build review.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save modified scenes before running the airside build review.");
                Directory.CreateDirectory(Folder); Directory.CreateDirectory(SaveFolder);
                foreach (string file in Directory.GetFiles(Folder, "*.png")) File.Delete(file);
                SessionState.SetString(Key + "Scenes", JsonUtility.ToJson(new Setup { scenes = EditorSceneManager.GetSceneManagerSetup()
                    .Select(s => new SavedScene { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray() }));
                SessionState.SetBool(Key + "Exit", exitEditor);
                SessionState.SetBool(Key+"PerformanceOnly",performanceOnly);
                SessionState.SetBool(Key+"StructuresOnly",false);SessionState.SetBool(Key+"ConnectionsOnly",false);SessionState.SetBool(Key+"WaterOnly",false);
                SessionState.SetBool(Key + "Background", Application.runInBackground);
                SessionState.SetBool(Key+"AsyncShaders",ShaderUtil.allowAsyncCompilation);ShaderUtil.allowAsyncCompilation=false;
                SessionState.SetBool(Key + "Paused", EditorApplication.isPaused);
                SessionState.SetBool(Key + "HudVisible", DevGui.Visible);
                SessionState.SetInt(Key + "InputBackground", (int)InputSystem.settings.backgroundBehavior);
                SessionState.SetInt(Key + "InputEditor", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
                SessionState.SetInt(Key + "OriginalMouse", Mouse.current != null ? Mouse.current.deviceId : -1);
                SessionState.SetInt(Key + "OriginalKeyboard", Keyboard.current != null ? Keyboard.current.deviceId : -1);
                SessionState.SetInt(Key + "CustomSize", -1);
                File.WriteAllText("Logs/pavement-ownership-review.txt", "Running actual pavement ownership, vegetation clearance and repeated terrain updates.\n");
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                SessionState.SetBool(Key + "Running", true); SessionState.SetInt(Key + "Stage", 0);
                SessionState.SetFloat(Key + "Started", (float)EditorApplication.timeSinceStartup);
                SessionState.SetString(Key + "Failure", "");
                EditorApplication.isPaused = false; Application.runInBackground = true;
                EditorApplication.update -= Update; EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                if (SessionState.GetBool(Key + "Running", false)) Fail(e);
                else
                {
                    Debug.LogException(e); Directory.CreateDirectory("Logs");
                    File.WriteAllText("Logs/pavement-ownership-review.txt", "Failed to start: " + e + "\n");
                    if (exitEditor) EditorApplication.Exit(1);
                }
            }
        }

        static void Update()
        {
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage >= 100 && stage < 200 || stage >= 400)
            {
                if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) Finish(stage == 100);
                return;
            }
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0f) > 300f)
                    throw new TimeoutException("Pavement ownership review timed out; use a graphics-enabled isolated editor.");
                if (!EditorApplication.isPlaying) return;
                EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
                if (window != null) window.Repaint();
                if (releasePending && EditorApplication.timeSinceStartup >= releaseAt)
                { pressed = false; SendMouse(EventType.MouseUp); releasePending = false; }
                if (EditorApplication.timeSinceStartup < next) return;
                if(stage==0)
                {
                    if(!Initialize())return;
                    Go(SessionState.GetBool(Key+"WaterOnly",false)?300:SessionState.GetBool(Key+"ConnectionsOnly",false)?200:SessionState.GetBool(Key+"StructuresOnly",false)?60:SessionState.GetBool(Key+"PerformanceOnly",false)?-1:1,3f);
                    return;
                }
                if(stage>=60&&stage<90){ReviewStructures(stage);return;}
                if(stage>=200&&stage<300){ReviewConnections(stage);return;}
                if(stage>=300&&stage<400){ReviewWater(stage);return;}
                switch(stage)
                {
                    case -1:BenchmarkPlacement();SessionState.SetInt(Key+"Stage",100);RestoreInput();EditorApplication.ExitPlaymode();break;
                    case 1:Click(WorldPoint(Start));Go(2);break;
                    case 2:Require(tool.PlacingRunway!=null,"Actual grass runway did not start.");MovePointer(End);Go(3);break;
                    case 3:
                        Require(string.IsNullOrEmpty(tool.RunwayProblem),tool.RunwayProblem);
                        if(!Capture("01-grass-runway-preview-1800"))return;
                        game.view.FocusPoint(new Vector3(0,FixtureHeight(0,300),300),5200,75,0);Go(4);break;
                    case 4:
                        MovePointer(End);Go(5);break;
                    case 5:
                        if(!Capture("02-runway-outline-zoomed-out"))return;
                        VerifyOutline("02-runway-outline-zoomed-out");
                        VerifyUnbuiltTerrain("02-runway-outline-zoomed-out");
                        quotedPrice=tool.RunwayPrice;Click(WorldPoint(End));Go(6);break;
                    case 6:
                        if(!Capture("02b-placed-grass-runway-natural-surroundings"))return;
                        Require(tool.PlacingRunway==null,"Runway did not commit: "+tool.RunwayProblem);totalPrice+=quotedPrice;
                        Require(game.Airport.Runways.Count==1,"Missing committed runway.");
                        Require(game.State.funds==SavedFunds-totalPrice,"Runway quote mismatch.");
                        tool.SetTool(DevTool.Road);FocusRoad();Go(7);break;
                    case 7:Click(WorldPoint(RoadStart));Go(8);break;
                    case 8:MovePointer(RoadMiddle);Go(9);break;
                    case 9:
                        Require(tool.AirsideDrawing && string.IsNullOrEmpty(tool.RoadProblem),"Road refused vegetation: "+tool.RoadProblem);
                        Require(game.State.world.trees.Count(t=>t.removed)==0,"Merely previewing removed vegetation.");
                        if(!Capture("03-service-road-through-vegetation-preview"))return;
                        VerifyRoadPreview("03-service-road-through-vegetation-preview");
                        quotedPrice=tool.AirsidePrice;Click(WorldPoint(RoadMiddle));Go(10);break;
                    case 10:
                        CheckCommit(1);MovePointer(RoadEnd);Go(11);break;
                    case 11:
                        Require(string.IsNullOrEmpty(tool.RoadProblem),tool.RoadProblem);quotedPrice=tool.AirsidePrice;Click(WorldPoint(RoadEnd));Go(12);break;
                    case 12:
                        CheckCommit(2);Require(game.State.world.trees.Count(t=>t.removed)>=11,"Road construction did not automatically clear its trees.");
                        CheckNature();tool.SetTool(DevTool.Select);Go(13);break;
                    case 13:
                        if(!Capture("04-built-service-road-no-plants"))return;
                        tool.SetTool(DevTool.Street);tool.SetStreetStyle(PublicRoadStyle.Urban);FocusStreet();Go(14);break;
                    case 14:Click(WorldPoint(StreetStart));Go(15);break;
                    case 15:MovePointer(StreetEnd);Go(16);break;
                    case 16:
                        Require(tool.StreetDrawing && string.IsNullOrEmpty(tool.StreetProblem),"Street refused vegetation: "+tool.StreetProblem);
                        if(!Capture("05-street-through-vegetation-preview"))return;
                        VerifyRoadPreview("05-street-through-vegetation-preview",true);
                        quotedPrice=tool.StreetPrice;edgesBefore=tool.Streets.network.segments.Count;Click(WorldPoint(StreetEnd));Go(17,1f);break;
                    case 17:
                        Require(tool.Streets.network.segments.Count==edgesBefore+1,"Street click failed: "+tool.StreetProblem);
                        totalPrice+=quotedPrice;Require(game.State.funds==SavedFunds-totalPrice,"Street quote mismatch.");CheckNature();
                        Require(game.State.world.trees.All(t=>t.removed),"Public road did not clear its trees.");
                        tool.SetTool(DevTool.Select);StressGrading();FocusRoad();Go(18,1f);break;
                    case 18:
                        CheckNature();if(!Capture("06-after-twenty-grading-updates"))return;
                        ForceBadTerrain();game.view.FocusPoint(new Vector3(0,FixtureHeight(0,-150),-150),850,85,0);Go(19,1f);break;
                    case 19:
                        if(!Capture("07-pavement-visible-with-terrain-raised-three-metres"))return;
                        VerifyAsphaltPixels("07-pavement-visible-with-terrain-raised-three-metres");stressVerified=true;
                        game.valley.RefreshLand();
                        Require(game.Save(),"Save failed: "+game.Message);Require(game.Continue(),"Continue failed: "+game.Message);game.Paused=true;tool=game.BuildTools;tool.SetTool(DevTool.Select);FocusRoad();Go(20,1f);break;
                    case 20:
                        CheckNature();Require(game.State.world.trees.All(t=>t.removed),"Saved tree clearances were lost.");
                        if(!Capture("08-reloaded-road-and-ground"))return;
                        VerifyAsphaltPixels("08-reloaded-road-and-ground");
                        PaintDirt();game.view.FocusPoint(new Vector3(0,FixtureHeight(0,300),300),850,65,0);Go(21);break;
                    case 21:
                        if(!Capture("09-placed-grass-runway-on-dirt"))return;
                        VerifyGrassPixels("09-placed-grass-runway-on-dirt");
                        Resize(680,650);FocusRoad();Go(22,1f);break;
                    case 22:
                        if(!Capture("10-pavement-680"))return;
                        Resize(1440,900);game.State.layout.Unlock(CareerTool.Stand);tool.SetTool(DevTool.Stand);tool.SetStandCode(SizeCode.A);tool.SetSurface(PavementSurface.Grass);
                        game.State.layout.Unlock(CareerTool.Stand);
                        game.State.world.trees.Add(new ForestTree {position=GrassStand,height=10});game.State.world.trees.Add(new ForestTree {position=PavedStand,height=10});
                        game.valley.RefreshForest(false);game.view.FocusPoint(new Vector3(0,100,20),950,65,0);Go(23,1f);break;
                    case 23:MovePointer(GrassStand);Go(24);break;
                    case 24:
                        Require(tool.StandDrawing && string.IsNullOrEmpty(tool.GhostProblem),"Grass stand is not ready: "+tool.GhostProblem);
                        if(!Capture("11-grass-stand-ready-with-grading-price"))return;
                        quotedPrice=tool.StandPrice;standCountBefore=game.Airport.Taxiways.stands.Count;Click(WorldPoint(GrassStand));Go(25);break;
                    case 25:
                        CheckStandCommit();grassStandHeight=game.Airport.Taxiways.stands.Last().position.y;CloseParkingDesigner();CheckNature();
                        Go(26);break;
                    case 26:
                        if(!Capture("12-grass-stand-built-on-hill"))return;
                        tool.SetTool(DevTool.Stand);tool.SetSurface(PavementSurface.Asphalt);MovePointer(PavedStand);Go(27);break;
                    case 27:
                        Require(tool.StandDrawing && string.IsNullOrEmpty(tool.GhostProblem),"Paved stand is not ready: "+tool.GhostProblem);
                        game.State.funds=0;Go(28);break;
                    case 28:
                        Require(!string.IsNullOrEmpty(tool.GhostProblem) && tool.GhostProblem.Contains("funds"),"An unaffordable stand was shown as valid.");
                        if(!Capture("13-stand-refusal-visible-before-click"))return;
                        standCountBefore=game.Airport.Taxiways.stands.Count;Click(WorldPoint(PavedStand));Go(29);break;
                    case 29:
                        Require(game.Airport.Taxiways.stands.Count==standCountBefore && game.State.funds==0,"Rejected stand changed the airport or funds.");
                        game.State.funds=SavedFunds-totalPrice;Go(30);break;
                    case 30:
                        Require(string.IsNullOrEmpty(tool.GhostProblem),tool.GhostProblem);quotedPrice=tool.StandPrice;
                        if(!Capture("14-paved-stand-ready-with-grading-price"))return;
                        Click(WorldPoint(PavedStand));Go(31);break;
                    case 31:
                        CheckStandCommit();CloseParkingDesigner();CheckNature();
                        Require(game.State.world.trees.All(t=>t.removed),"Stands did not automatically remove their vegetation.");
                        Require(game.Save(),game.Message);Require(game.Continue(),game.Message);game.Paused=true;tool=game.BuildTools;tool.SetTool(DevTool.Select);
                        Require(game.Airport.Taxiways.stands.Count==2,"Reload lost a placed stand.");
                        Require(Mathf.Abs(game.Airport.Taxiways.stands.First().position.y-grassStandHeight)<.001f,"Reload lost the grass stand height.");
                        game.view.FocusPoint(new Vector3(0,100,20),950,65,0);Go(32,1f);break;
                    case 32:
                        CheckNature();if(!Capture("15-both-stands-reloaded"))return;
                        game.view.FocusPoint(new Vector3(0,FixtureHeight(0,300),300),450,60,0);pointer=new Vector2(720,20);SendMouse(EventType.MouseMove);Go(33,1f);break;
                    case 33:
                        if(!Capture("16-grass-runway-turf-and-tracks"))return;
                        tool.SetTool(DevTool.Road);tool.SetSurface(PavementSurface.Asphalt);
                        game.view.FocusPoint(new Vector3(300,105,-100),620,65,0);Go(34,1f);break;
                    case 34:Click(WorldPoint(RoadEnd));Go(35);break;
                    case 35:
                        previewGraph=JsonUtility.ToJson(game.Airport.Taxiways);previewFunds=game.State.funds;previewHeights=(float[])game.State.world.heights.Clone();
                        MovePointer(RoadCurveEnd);Go(36);break;
                    case 36:
                        Require(string.IsNullOrEmpty(tool.RoadProblem),tool.RoadProblem);Require(game.valley.HasConstructionPreview,"Missing curved road preview ownership.");
                        Require(previewGraph==JsonUtility.ToJson(game.Airport.Taxiways) && previewFunds==game.State.funds && previewHeights.SequenceEqual(game.State.world.heights),"Road preview changed the world.");
                        if(!Capture("17-service-road-actual-curve-preview-cost-only"))return;
                        tool.SetTool(DevTool.Select);Require(!game.valley.HasConstructionPreview,"Cancel retained a terrain mask.");Go(37);break;
                    case 37:
                        if(!Capture("18-cancel-restores-original-road-end"))return;
                        tool.SetTool(DevTool.Road);Click(WorldPoint(RoadEnd));Go(38);break;
                    case 38:MovePointer(RoadCurveEnd);Go(39);break;
                    case 39:
                        Require(string.IsNullOrEmpty(tool.RoadProblem),tool.RoadProblem);quotedPrice=tool.AirsidePrice;
                        Click(WorldPoint(RoadCurveEnd));Go(40);break;
                    case 40:
                        CheckCommit(3);tool.SetTool(DevTool.Select);CheckNature();
                        game.view.FocusPoint(new Vector3(350,107,-90),350,65,25);Go(41);break;
                    case 41:
                        if(!Capture("19-built-service-road-curve-and-weathered-asphalt"))return;
                        tool.SetTool(DevTool.Street);tool.SetStreetStyle(PublicRoadStyle.Urban);
                        game.view.FocusPoint(new Vector3(300,100,-450),620,65,0);Go(42);break;
                    case 42:Click(WorldPoint(StreetEnd));Go(43);break;
                    case 43:MovePointer(StreetCurveEnd);Go(44);break;
                    case 44:
                        Require(string.IsNullOrEmpty(tool.StreetProblem),tool.StreetProblem);Require(game.valley.HasConstructionPreview,"Missing public curve mask.");
                        if(!Capture("20-street-actual-curve-preview-cost-only"))return;
                        quotedPrice=tool.StreetPrice;edgesBefore=tool.Streets.network.segments.Count;Click(WorldPoint(StreetCurveEnd));Go(45);break;
                    case 45:
                        Require(tool.Streets.network.segments.Count==edgesBefore+1,"Street bend failed: "+tool.StreetProblem);
                        totalPrice+=quotedPrice;Require(game.State.funds==SavedFunds-totalPrice,"Street curve price mismatch.");
                        tool.SetTool(DevTool.Select);game.view.FocusPoint(new Vector3(340,100,-445),430,65,15);Go(46);break;
                    case 46:
                        if(!Capture("21-street-rounded-end-and-curved-kerbs"))return;
                        tool.SetTool(DevTool.Street);game.view.FocusPoint(new Vector3(0,100,-435),650,65,0);Go(47);break;
                    case 47:Click(WorldPoint(new Vector2(0,-500)));Go(48);break;
                    case 48:MovePointer(StreetTEnd);Go(49);break;
                    case 49:
                        Require(string.IsNullOrEmpty(tool.StreetProblem),tool.StreetProblem);quotedPrice=tool.StreetPrice;
                        if(!Capture("22-graded-junction-preview-with-clean-arrows"))return;
                        Click(WorldPoint(StreetTEnd));Go(50);break;
                    case 50:
                        totalPrice+=quotedPrice;Require(game.State.funds==SavedFunds-totalPrice,"Junction price mismatch.");
                        tool.SetTool(DevTool.Select);game.view.FocusPoint(new Vector3(0,95,-500),190,65,20);Go(51);break;
                    case 51:
                        pointer=new Vector2(720,20);SendMouse(EventType.MouseMove);Go(52);break;
                    case 52:
                        if(!Capture("23-clean-junction-arrows-on-graded-pavement"))return;
                        Require(game.Save(),game.Message);Require(game.Continue(),game.Message);game.Paused=true;tool=game.BuildTools;tool.SetTool(DevTool.Select);
                        Require(!game.valley.HasConstructionPreview,"Reload retained a preview mask.");
                        var result=new Result {result="Passed: real mouse construction, curve previews and cost-only labels; cancel restores geometry/masks without edits; rounded road ends; graded junction arrows; natural asphalt; vegetation, repeated grading and save/load.",
                            utc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,totalPrice=totalPrice,
                            clearedTrees=game.State.world.trees.Count(t=>t.removed),clearedProps=game.State.world.clearedProps.Count,
                            triangleCount=game.valley.BuiltSurfaces.TriangleCount,textureSurvivesForcedTerrainRaise=stressVerified,saveLoadVerified=true,screenshots=shots};
                        File.WriteAllText("Logs/pavement-ownership-review.json",JsonUtility.ToJson(result,true));File.AppendAllText("Logs/pavement-ownership-review.txt",result.result+"\n");
                        SessionState.SetInt(Key+"Stage",100);RestoreInput();EditorApplication.ExitPlaymode();break;
                }
            }
            catch (Exception e) { Fail(e); }
        }

        static bool Initialize()
        {
            game = Object.FindAnyObjectByType<CareerGame>();
            if (game == null || game.State == null) { next = EditorApplication.timeSinceStartup + .2f; return false; }
            hud = game.GetComponent<CareerHud>();
            Require(hud != null && hud.enabled, "The production map-price HUD must be enabled.");
            typeof(CareerGame).GetField("store", Instance).SetValue(game, new CareerSaveStore(Path.GetFullPath(SaveFolder)));
            var state = CareerState.New(19770616, preset: RegionPreset.Woodland);
            var world = state.world; world.trees.Clear(); world.hasOcean = world.hasRiver = false; world.waterHeights = new float[world.heights.Length];
            world.landscapeSource = "Controlled hill UI fixture"; world.hasCoordinates = false;
            world.latitude = world.longitude = 0d; world.elevationSha256 = world.coverSha256 = null;
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                float px = x * world.Spacing - ValleyWorld.Size * .5f, pz = z * world.Spacing - ValleyWorld.Size * .5f;
                int i = z * ValleyWorld.Resolution + x;
                world.heights[i] = FixtureHeight(px, pz);
                world.landCover[i]=(byte)(Mathf.Abs(px)<350f && (Mathf.Abs(pz+150)<75 || Mathf.Abs(pz+500)<75)?10:50);
                if(px>2200 && px<2250 && pz>2200 && pz<2250){world.waterHeights[i]=world.heights[i]+2f;world.landCover[i]=80;}
            }
            world.publicRoad = new List<Vector2> { new Vector2(2000f, -3200f), new Vector2(2000f, 3200f) };
            world.roadHeights = world.publicRoad.Select(p => FixtureHeight(p.x, p.y)).ToArray();
            world.settlements = new List<Vector2> { new Vector2(2300f, -2600f), new Vector2(2300f, 2600f) };
            foreach (var site in world.sites) { site.centre = Vector2.zero; site.elevation = 100f; site.name = "Controlled hill"; }
            for(int i=0;i<13;i++)
            {world.trees.Add(new ForestTree {position=new Vector2(-280+i*45,-150),height=12});world.trees.Add(new ForestTree {position=new Vector2(-280+i*45,-500),height=12});}
            initialTreeCount=world.trees.Count;
            state.publicRoads = PublicRoadNetwork.FromWorld(world);
            Require(state.StartRegion(out string why), why); state.funds = SavedFunds;
            state.layout.Unlock(CareerTool.Runway);state.layout.Unlock(CareerTool.Taxiway);state.layout.Unlock(CareerTool.Road);
            Require(state.Validate(out why), "Controlled fixture is invalid: " + why);
            game.Initialize(state, true); game.Paused = true;RenderSettings.fog=false;
            tool = game.BuildTools; Require(game.SharedTools && tool != null, "The actual shared build tool must be active.");
            tool.SetTool(DevTool.Runway);tool.SetSurface(PavementSurface.Grass);tool.SetWidth(18f);
            totalPrice=0;
            originalHeights = (float[])world.heights.Clone();
            DevGui.Visible = true; shots.Clear();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("Pavement ownership review mouse"); SessionState.SetInt(Key + "Mouse", mouse.deviceId);
            keyboard = InputSystem.AddDevice<Keyboard>("Pavement ownership review keyboard"); SessionState.SetInt(Key + "Keyboard", keyboard.deviceId);
            InputSystem.onBeforeUpdate -= FeedInput; InputSystem.onBeforeUpdate += FeedInput;
            SessionState.SetString(Key + "GameViews", JsonUtility.ToJson(new GameViewSetup { views = GameViews()
                .Select(v => new SavedGameView { id = v.GetEntityId().ToString(), size = (int)v.GetType().GetProperty("selectedSizeIndex", Instance).GetValue(v) }).ToArray() }));
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
            window = (EditorWindow)ScriptableObject.CreateInstance(type); window.titleContent = new GUIContent("Pavement ownership review"); window.Show();
            SessionState.SetString(Key + "Window", window.GetEntityId().ToString());
            pointer = new Vector2(0f, 0f); Resize(1440, 900); FocusHill();
            initialRoadProps=game.GetComponentInChildren<GroundCoverRenderer>().Instances.Count(p=>Mathf.Abs(p.position.y+150)<8f && Mathf.Abs(p.position.x)<300f);
            Require(initialRoadProps>0,"The vegetation fixture has no plants in the service-road footprint.");
            return true;
        }
        static float FixtureHeight(float x,float z)=>100f+.025f*Mathf.Clamp(x,-600,600)+.012f*Mathf.Clamp(z,-900,600)+Mathf.Sin(x*.025f)
            +8f*Mathf.Exp(-Mathf.Pow((x+150f)/65f,2)-Mathf.Pow((z+150f)/60f,2))
            +8f*Mathf.Exp(-Mathf.Pow(x/90f,2)-Mathf.Pow((z+500f)/60f,2));
        static void FocusHill()=>game.view.FocusPoint(new Vector3(0,FixtureHeight(0,300),300),1800,65,0);
        static void FocusRoad()=>game.view.FocusPoint(new Vector3(0,FixtureHeight(0,-150),-150),900,65,0);
        static void FocusStreet()=>game.view.FocusPoint(new Vector3(0,FixtureHeight(0,-500),-500),950,65,0);
        static Vector2 WorldPoint(Vector2 point)
        {
            var viewport = game.view.GetComponent<Camera>().WorldToViewportPoint(new Vector3(point.x, game.valley.DisplayHeight(point), point.y));
            return new Vector2(viewport.x * width, (1f - viewport.y) * height);
        }
        static void MovePointer(Vector2 point) { pointer = WorldPoint(point); SendMouse(EventType.MouseMove); }
        static void CheckCommit(int count)
        {
            totalPrice+=quotedPrice;Require(game.Airport.Taxiways.edges.Count==count,"Road did not commit: "+tool.RoadProblem);
            Require(game.State.funds==SavedFunds-totalPrice,"Road price changed at commit.");
        }
        static void CloseParkingDesigner()=>tool.GetComponent<DevToolbar>().GateDesigner.Close();
        static void CheckStandCommit()
        {
            Require(game.Airport.Taxiways.stands.Count==standCountBefore+1,"Green stand click failed: "+tool.GhostProblem);
            totalPrice+=quotedPrice;Require(game.State.funds==SavedFunds-totalPrice,"Stand price changed on click.");
        }
        static void VerifyRoadPreview(string shot,bool street=false)
        {
            var image=new Texture2D(2,2,TextureFormat.RGB24,false);image.LoadImage(File.ReadAllBytes(Folder+shot+".png"));
            try
            {
                int start=street?-1:(int)typeof(DevAirfieldTool).GetField("chainNode",Instance).GetValue(tool);
                var from=street?new Vector3(StreetStart.x,FixtureHeight(StreetStart.x,StreetStart.y),StreetStart.y):
                    game.Airport.transform.TransformPoint(game.Airport.Taxiways.Node(start).position);
                var to=street?tool.StreetEndpoint:tool.AirsideEndpoint;
                int checkedPixels=0;
                for(int n=1;n<12;n++)
                {
                    var point=Vector3.Lerp(from,to,n/12f);point.z+=1.3f;point.y+=.033f;
                    var q=game.view.GetComponent<Camera>().WorldToViewportPoint(point);int px=Mathf.RoundToInt(q.x*image.width),py=Mathf.RoundToInt(q.y*image.height);
                    bool asphalt=false;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {var c=image.GetPixel(px+dx,py+dy);if(Mathf.Max(c.r,c.g,c.b)-Mathf.Min(c.r,c.g,c.b)<.13f && c.r<.55f)asphalt=true;}
                    Require(asphalt,"The hill clipped the proposed road at "+point);checkedPixels++;
                }
                File.AppendAllText("Logs/pavement-ownership-review.txt","Preview visible through real hill at "+checkedPixels+" sampled locations.\n");
            }
            finally{Object.DestroyImmediate(image);}
        }
        static void CheckNature()
        {
            var map=game.valley.BuiltSurfaces;Require(map!=null && map.TriangleCount>0,"The shared built-surface map is missing.");
            var forest=game.GetComponentInChildren<ForestRenderer>();
            Require(forest!=null && forest.TreeCount==game.State.world.trees.Count(t=>!t.removed),"Removed tree roots remain in rendered forest batches.");
            var renderer=game.GetComponentInChildren<GroundCoverRenderer>();
            Require(renderer!=null && !renderer.Instances.Any(p=>map.Contains(p.position,GroundCoverScatter.FootprintRadius(p.kind,p.scale))),"A vegetation model overlaps built pavement.");
            var block=new MaterialPropertyBlock();game.valley.Terrain.GetSplatMaterialPropertyBlock(block);
            Require(block.GetFloat("_AirCitySurfaceEnabled")>.5f,"Terrain coverage was not bound to the actual terrain renderer.");
        }
        static void StressGrading()
        {
            for(int i=0;i<20;i++)
            {
                var centre=new Vector2(-160+i*16,-150);
                game.State.world.LevelBrush(centre,80,FixtureHeight(centre.x,centre.y)+(i%2==0?2:-2),game.State.world.RegionBounds);
                var area=new Rect(centre.x-100,centre.y-100,200,200);game.valley.RefreshLand(area);game.valley.QueueLevelSurface(area);
            }
            while(game.valley.PendingSurfaceTiles>0)game.valley.ProcessSurfaceUpdates(16);
            while(game.valley.PendingGroundCoverTiles>0)game.valley.ProcessGroundCoverUpdates(8);
            for(int i=-250;i<=250;i+=25)
            {
                var local=new Vector2(i,-150)-new Vector2(game.Airport.transform.position.x,game.Airport.transform.position.z);
                float surface=game.Airport.transform.position.y+game.Airport.PavementElevationAt(local);
                float terrain=game.valley.DisplayHeight(new Vector2(i,-150));
                Require(terrain<surface+.025f && terrain>surface-.12f,"Later terrain update displaced the road formation.");
            }
        }
        static void ForceBadTerrain()
        {
            var data=game.valley.Terrain.terrainData;int resolution=data.heightmapResolution;var h=data.GetHeights(0,0,resolution,resolution);
            for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++)h[z,x]+=3f/ValleyWorld.HeightRange;
            data.SetHeights(0,0,h);
            PaintDirt();
        }
        static void PaintDirt()
        {
            var data=game.valley.Terrain.terrainData;
            int size=data.alphamapResolution,layers=data.alphamapLayers;var alpha=new float[size,size,layers];
            for(int z=0;z<size;z++)for(int x=0;x<size;x++)alpha[z,x,1]=1f;
            data.SetAlphamaps(0,0,alpha);
        }
        static void VerifyUnbuiltTerrain(string shot)
        {
            var image=new Texture2D(2,2,TextureFormat.RGB24,false);image.LoadImage(File.ReadAllBytes(Folder+shot+".png"));
            try
            {
                var point=new Vector3(-500,FixtureHeight(-500,0),0);var q=game.view.GetComponent<Camera>().WorldToViewportPoint(point);
                var c=image.GetPixel(Mathf.RoundToInt(q.x*image.width),Mathf.RoundToInt(q.y*image.height));
                Require(c.g>c.b+.03f,"Unbuilt far terrain disappeared while pavement ownership was enabled: "+c);
                File.AppendAllText("Logs/pavement-ownership-review.txt","Unbuilt far terrain preserved: "+c+".\n");
            }
            finally{Object.DestroyImmediate(image);}
        }
        static void VerifyOutline(string shot)
        {
            var image=new Texture2D(2,2,TextureFormat.RGB24,false);image.LoadImage(File.ReadAllBytes(Folder+shot+".png"));
            try
            {
                var runway=tool.PlacingRunway;var axis=(runway.end-runway.start);var right=new Vector3(axis.z,0,-axis.x).normalized*runway.HalfWidthTotal;
                var corners=new[]{runway.start-right,runway.end-right,runway.end+right,runway.start+right};var camera=game.view.GetComponent<Camera>();
                int seen=0,total=0;
                for(int edge=0;edge<4;edge++)for(int i=1;i<20;i++)
                {
                    var p=runway.Airport.transform.TransformPoint(Vector3.Lerp(corners[edge],corners[(edge+1)%4],i/20f));var q=camera.WorldToViewportPoint(p);
                    int px=Mathf.RoundToInt(q.x*image.width),py=Mathf.RoundToInt(q.y*image.height);bool gold=false;
                    for(int y=Mathf.Max(0,py-3);y<=Mathf.Min(image.height-1,py+3);y++)for(int x=Mathf.Max(0,px-3);x<=Mathf.Min(image.width-1,px+3);x++)
                    {var c=image.GetPixel(x,y);if(c.r>.7f && c.g>.35f && c.b<.45f && c.r>c.g+.05f)gold=true;}
                    if(gold)seen++;total++;
                }
                Require(seen>=total*.95f,$"Zoomed-out runway outline was clipped: {seen}/{total} samples visible.");
                File.AppendAllText("Logs/pavement-ownership-review.txt",$"Zoomed runway boundary: {seen}/{total} samples visible.\n");
            }
            finally{Object.DestroyImmediate(image);}
        }
        static void VerifyGrassPixels(string shot)
        {
            var image=new Texture2D(2,2,TextureFormat.RGB24,false);image.LoadImage(File.ReadAllBytes(Folder+shot+".png"));
            try
            {
                var runway=game.Airport.Runways.Single();var camera=game.view.GetComponent<Camera>();int checkedPixels=0;
                for(int x=-300;x<=300;x+=25)
                {
                    var local=new Vector2(x,302)-new Vector2(game.Airport.transform.position.x,game.Airport.transform.position.z);
                    float y=game.Airport.transform.position.y+runway.ElevationAt(local)+runway.SurfaceHeight;
                    var q=camera.WorldToViewportPoint(new Vector3(x,y,302));int px=Mathf.RoundToInt(q.x*image.width),py=Mathf.RoundToInt(q.y*image.height);
                    if(px<1 || px>=image.width-1 || py<1 || py>=image.height-1)continue;
                    var c=image.GetPixel(px,py);Require(c.g>c.r+.03f && c.g>c.b+.07f,"Dirt obscures the grass runway at x="+x+": "+c);checkedPixels++;
                }
                Require(checkedPixels>=23,"Not enough grass-runway pixels were sampled.");
                File.AppendAllText("Logs/pavement-ownership-review.txt",shot+": "+checkedPixels+" actual grass pixels preserved over dirt.\n");
            }
            finally{Object.DestroyImmediate(image);}
        }
        static void VerifyAsphaltPixels(string shot)
        {
            var image=new Texture2D(2,2,TextureFormat.RGB24,false);image.LoadImage(File.ReadAllBytes(Folder+shot+".png"));
            try
            {
                var cam=game.view.GetComponent<Camera>();int checkedPixels=0;
                for(int x=-250;x<=250;x+=25)
                {
                    var p=new Vector2(x,-148.7f);var local=p-new Vector2(game.Airport.transform.position.x,game.Airport.transform.position.z);
                    float height=game.Airport.transform.position.y+game.Airport.PavementElevationAt(local)+.013f;
                    var pixel=cam.WorldToViewportPoint(new Vector3(p.x,height,p.y));int px=Mathf.RoundToInt(pixel.x*image.width),py=Mathf.RoundToInt(pixel.y*image.height);
                    if(px<1 || px>=image.width-1 || py<1 || py>=image.height-1)continue;
                    bool asphalt=false;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {var c=image.GetPixel(px+dx,py+dy);if(Mathf.Max(c.r,c.g,c.b)-Mathf.Min(c.r,c.g,c.b)<.13f && c.r<.55f)asphalt=true;}
                    Require(asphalt,$"Terrain replaced asphalt at x={x}.");checkedPixels++;
                }
                Require(checkedPixels>=18,"Not enough actual asphalt pixels were checked.");
                File.AppendAllText("Logs/pavement-ownership-review.txt",shot+": "+checkedPixels+" actual pavement pixels verified.\n");
            }
            finally{Object.DestroyImmediate(image);}
        }
        static float U(float value) => DevGui.U(value);
        static void Go(int stage, float settle = .7f)
        {
            SessionState.SetInt(Key + "Stage", stage); next = EditorApplication.timeSinceStartup + settle;
            File.AppendAllText("Logs/pavement-ownership-review.txt", "Stage " + stage + "\n");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static bool Await(bool condition, string message)
        {
            if (condition) return true;
            Require(EditorApplication.timeSinceStartup < actionDeadline, message);
            next = EditorApplication.timeSinceStartup + .1f; return false;
        }
        static void Click(Vector2 guiPoint)
        {
            pointer = guiPoint; pressed = true; releasePending = true; releaseAt = EditorApplication.timeSinceStartup + .15f;
            actionDeadline = EditorApplication.timeSinceStartup + 8f;
            File.AppendAllText("Logs/pavement-ownership-review.txt", $"Click GUI {guiPoint}, target {width}x{height}, editor Screen {Screen.width}x{Screen.height}\n");
            window.Focus(); SendMouse(EventType.MouseDown);
        }
        static void SendMouse(EventType type)
        {
            if (window == null) return;
            var queue = typeof(EditorGUIUtility).GetMethod("QueueGameViewInputEvent", Static, null, new[] { typeof(Event) }, null);
            if (queue != null)
            {
                queue.Invoke(null, new object[] { new Event { type = type, button = 0, mousePosition = pointer, displayIndex = 0 } });
                window.Repaint(); EditorApplication.QueuePlayerLoopUpdate(); return;
            }
            var offsetProperty = window.GetType().GetProperty("gameMouseOffset", Instance);
            var scaleProperty = window.GetType().GetProperty("gameMouseScale", Instance);
            var offset = offsetProperty != null ? (Vector2)offsetProperty.GetValue(window) : new Vector2(0f, -21f);
            float scale = scaleProperty != null ? (float)scaleProperty.GetValue(window) : 1f;
            window.SendEvent(new Event { type = type, button = 0, mousePosition = pointer / scale - offset });
            window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        }
        static void Hold(Vector2 guiPoint, float seconds)
        {
            pointer = guiPoint; pressed = true; releasePending = true; releaseAt = EditorApplication.timeSinceStartup + seconds;
            actionDeadline = releaseAt + 8f; window.Focus(); SendMouse(EventType.MouseDown);
        }
        static void SendKey(Event e)
        {
            window.SendEvent(e);
            window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        }
        static void FeedInput()
        {
            if (mouse == null || InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(pointer.x, height - pointer.y) }
                .WithButton(MouseButton.Left, pressed));
            if (keyboard != null) { keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }
        }

        static object SizeGroup()
        {
            var assembly = typeof(Editor).Assembly;
            var type = assembly.GetType("UnityEditor.GameViewSizes", true);
            var instance = type.GetProperty("instance", Static).GetValue(null);
            var groupType = window != null ? window.GetType().GetProperty("currentSizeGroupType", Static)?.GetValue(null) : null;
            if (groupType == null) groupType = Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeGroupType", true), 0);
            return type.GetMethod("GetGroup", Instance).Invoke(instance, new[] { groupType });
        }
        static EditorWindow[] GameViews() => Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(v => v.GetType().FullName == "UnityEditor.GameView").ToArray();
        static void Resize(int wantedWidth, int wantedHeight)
        {
            width = wantedWidth; height = wantedHeight; var group = SizeGroup(); int previous = SessionState.GetInt(Key + "CustomSize", -1);
            if (previous >= 0)
            {
                foreach (var view in GameViews()) view.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(view, 0);
                group.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(group, new object[] { previous });
            }
            var assembly = typeof(Editor).Assembly; var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            var fixedKind = Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType", true), "FixedResolution");
            var size = Activator.CreateInstance(sizeType, Instance, null, new[] { fixedKind, (object)wantedWidth, wantedHeight, "Pavement ownership review temporary" }, null);
            int index = (int)group.GetType().GetMethod("GetTotalCount", Instance).Invoke(group, null);
            group.GetType().GetMethod("AddCustomSize", Instance).Invoke(group, new[] { size }); SessionState.SetInt(Key + "CustomSize", index);
            // Each GameView renders OnGUI against its own target size. Keep every local view at the review size so
            // a background GameView cannot replace RegionBrowser's last production hit rectangles with stale ones.
            foreach (var view in GameViews())
            { view.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(view, index); view.Repaint(); }
            // EditorApplication.update runs outside GameView's temporary aspect scope. Keep this
            // isolated review camera explicit so projected clicks match the fixed render target.
            if (game != null && game.view != null) game.view.GetComponent<Camera>().aspect = width / (float)height;
            window.position = new Rect(30f, 30f, Mathf.Min(1440f, wantedWidth), Mathf.Min(940f, wantedHeight + 42f));
            window.Focus(); window.Repaint(); EditorApplication.QueuePlayerLoopUpdate();
        }
        static bool Capture(string name)
        {
            if(ShaderUtil.anythingCompiling){next=EditorApplication.timeSinceStartup+.3f;window.Repaint();return false;}
            var field = window.GetType().GetField("m_RenderTexture", Instance);
            if (field == null) throw new MissingFieldException("GameView", "m_RenderTexture");
            var target = field.GetValue(window) as RenderTexture;
            if (target == null || !target.IsCreated() || target.width != width || target.height != height)
            { window.Repaint(); next = EditorApplication.timeSinceStartup + .2f; return false; }
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                int railHeight = Mathf.RoundToInt(U(width >= U(680) ? 40 : 72));
                var pixels = texture.GetPixels32();
                float DividerCoverage(bool top)
                {
                    int boundary = top ? texture.height - railHeight : railHeight - 1, best = 0;
                    for (int delta = -4; delta <= 4; delta++)
                    {
                        int y = Mathf.Clamp(boundary + delta, 0, texture.height - 1), gold = 0;
                        for (int x = 0; x < texture.width; x++)
                        {
                            var p = pixels[y * texture.width + x];
                            if (p.r > 180 && p.g > 80 && p.b < 130 && p.r > p.g + 20 && p.g > p.b + 20) gold++;
                        }
                        best = Mathf.Max(best, gold);
                    }
                    return best / (float)texture.width;
                }
                float topDivider = DividerCoverage(true), bottomDivider = DividerCoverage(false);
                File.AppendAllText("Logs/pavement-ownership-review.txt", $"{name}: divider top={topDivider:0.000}, bottom={bottomDivider:0.000}\n");
                Require(Mathf.Max(topDivider, bottomDivider) > .8f,
                    "The GameView render target did not contain the real HUD's full-width amber divider.");
                if (bottomDivider > topDivider)
                {
                    // Match GameView's Metal render-target flip when reading its texture directly.
                    var flipped = new Color32[pixels.Length];
                    for (int row = 0; row < texture.height; row++) Array.Copy(pixels, row * texture.width, flipped,
                        (texture.height - 1 - row) * texture.width, texture.width);
                    texture.SetPixels32(flipped); texture.Apply();
                }
                File.WriteAllBytes(Folder + name + ".png", texture.EncodeToPNG());
                shots.Add(new Shot { name = name, width = target.width, height = target.height, price=tool.PlacingRunway!=null?tool.RunwayPrice:tool.StreetDrawing?tool.StreetPrice:tool.StandDrawing?tool.StandPrice:tool.AirsidePrice,gradingPrice=tool.PlacingRunway!=null?tool.RunwayGradingPrice:tool.StandDrawing?tool.StandGradingPrice:tool.AirsideGradingPrice,slope=tool.PlacingRunway!=null?tool.PlacingRunway.Grade:tool.StreetDrawing?tool.StreetGrade:tool.AirsideGrade });
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
            return true;
        }
        static void RestoreInput()
        {
            InputSystem.onBeforeUpdate -= FeedInput;
            var reviewMouse = mouse != null ? mouse : InputSystem.GetDeviceById(SessionState.GetInt(Key + "Mouse", -1)) as Mouse;
            if (reviewMouse != null && reviewMouse.added) InputSystem.RemoveDevice(reviewMouse); mouse = null;
            var originalMouse = InputSystem.GetDeviceById(SessionState.GetInt(Key + "OriginalMouse", -1)) as Mouse;
            if (originalMouse != null) originalMouse.MakeCurrent();
            var reviewKeyboard = keyboard != null ? keyboard : InputSystem.GetDeviceById(SessionState.GetInt(Key + "Keyboard", -1)) as Keyboard;
            if (reviewKeyboard != null && reviewKeyboard.added) InputSystem.RemoveDevice(reviewKeyboard); keyboard = null;
            var originalKeyboard = InputSystem.GetDeviceById(SessionState.GetInt(Key + "OriginalKeyboard", -1)) as Keyboard;
            if (originalKeyboard != null) originalKeyboard.MakeCurrent();
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt(Key + "InputBackground", 0);
            InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Key + "InputEditor", 0);
            pressed = releasePending = false;
        }
        static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            Directory.CreateDirectory("Logs"); File.AppendAllText("Logs/pavement-ownership-review.txt", "Failed: " + exception + "\n");
            SessionState.SetString(Key + "Failure", exception.ToString()); SessionState.SetInt(Key + "Stage", 101);
            RestoreInput();
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else Finish(false);
        }
        static void Finish(bool success)
        {
            bool exit = SessionState.GetBool(Key + "Exit", false);
            try
            {
                RestoreInput(); EditorApplication.update -= Update; SessionState.SetBool(Key + "Running", false);
                if (window == null)
                {
                    string id = SessionState.GetString(Key + "Window", "");
                    window = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetEntityId().ToString() == id);
                }
                object group = null;
                try { group = SizeGroup(); } catch (Exception e) { Debug.LogWarning("Could not inspect temporary GameView size: " + e.Message); }
                string savedViews = SessionState.GetString(Key + "GameViews", "");
                if (!string.IsNullOrEmpty(savedViews))
                    foreach (var savedView in JsonUtility.FromJson<GameViewSetup>(savedViews).views ?? Array.Empty<SavedGameView>())
                    {
                        var existingView = GameViews().FirstOrDefault(v => v.GetEntityId().ToString() == savedView.id);
                        if (existingView != null) existingView.GetType().GetProperty("selectedSizeIndex", Instance).SetValue(existingView, savedView.size);
                    }
                if (window != null) window.Close();
                int size = SessionState.GetInt(Key + "CustomSize", -1);
                if (size >= 0 && group != null) group.GetType().GetMethod("RemoveCustomSize", Instance).Invoke(group, new object[] { size });
                SessionState.SetInt(Key + "CustomSize", -1);
                string setup = SessionState.GetString(Key + "Scenes", "");
                var original = string.IsNullOrEmpty(setup) ? Array.Empty<SavedScene>() : JsonUtility.FromJson<Setup>(setup).scenes ?? Array.Empty<SavedScene>();
                var scenes = original.Where(s => !string.IsNullOrEmpty(s.path)).ToArray();
                if (scenes.Any(s => s.loaded))
                {
                    if (!scenes.Any(s => s.loaded && s.active)) scenes.First(s => s.loaded).active = true;
                    EditorSceneManager.RestoreSceneManagerSetup(scenes.Select(s => new SceneSetup {
                        path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
                }
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception cleanup)
            {
                success = false; Debug.LogException(cleanup);
                File.AppendAllText("Logs/pavement-ownership-review.txt", "Cleanup failed: " + cleanup + "\n");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=SessionState.GetBool(Key+"AsyncShaders",true);
                Application.runInBackground = SessionState.GetBool(Key + "Background", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "Paused", false); DevGui.Visible = SessionState.GetBool(Key + "HudVisible", true);
                if (exit) EditorApplication.Exit(success ? 0 : 1);
                else Debug.Log("PavementOwnershipReview: " + (success ? "passed" : "failed") + "; see Logs/pavement-ownership-review.txt");
            }
        }
    }
}
