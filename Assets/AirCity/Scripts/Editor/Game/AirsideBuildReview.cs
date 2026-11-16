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
    public static class AirsideBuildReview
    {
        const string Key = "AirCity.AirsideBuildReview.";
        const string Folder = "Captures/NewGame/AirsideBuild/";
        const string SaveFolder = "Logs/AirsideBuildReview";
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
        static readonly Vector2 Start = new Vector2(-350f,-100f), Middle = new Vector2(0f,-100f), End = new Vector2(0f,250f);
        static readonly Vector2 RoadStart=new Vector2(-300f,-350f),RoadEnd=new Vector2(300f,-350f);
        static readonly Vector2 StreetStart=new Vector2(-300f,-700f),StreetEnd=new Vector2(300f,-700f);
        static int totalPrice,edgesBefore;
        static double movingPreviewMs,stationaryPreviewMs;
        static DevAirfieldTool tool;
        static float[] originalHeights;
        static int quotedPrice;

        static readonly List<Shot> shots = new List<Shot>();
        [Serializable] sealed class Shot { public string name; public int width, height, price, gradingPrice; public float slope; }
        [Serializable] sealed class Result
        {
            public string result, unityVersion, utc;
            public int totalPrice;
            public double movingStreetPreviewMs,stationaryStreetPreviewMs;
            public List<Shot> screenshots;
        }
        [Serializable] sealed class SavedGameView { public string id; public int size; }
        [Serializable] sealed class GameViewSetup { public SavedGameView[] views; }
        [Serializable] sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] sealed class Setup { public SavedScene[] scenes; }

        static AirsideBuildReview()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Update;
        }

        [MenuItem("AirCity/Game/Review Airside build")]
        public static void Run() => Begin(false);
        public static void RunFromCommandLine() => Begin(true);

        static void Begin(bool exitEditor)
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
                SessionState.SetBool(Key + "Background", Application.runInBackground);
                SessionState.SetBool(Key + "Paused", EditorApplication.isPaused);
                SessionState.SetBool(Key + "HudVisible", DevGui.Visible);
                SessionState.SetInt(Key + "InputBackground", (int)InputSystem.settings.backgroundBehavior);
                SessionState.SetInt(Key + "InputEditor", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
                SessionState.SetInt(Key + "OriginalMouse", Mouse.current != null ? Mouse.current.deviceId : -1);
                SessionState.SetInt(Key + "OriginalKeyboard", Keyboard.current != null ? Keyboard.current.deviceId : -1);
                SessionState.SetInt(Key + "CustomSize", -1);
                File.WriteAllText("Logs/airside-build-review.txt", "Running actual shared taxiway, service-road and street placement on a controlled hill with crooked bumps and crossfall.\n");
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
                    File.WriteAllText("Logs/airside-build-review.txt", "Failed to start: " + e + "\n");
                    if (exitEditor) EditorApplication.Exit(1);
                }
            }
        }

        static void Update()
        {
            int stage = SessionState.GetInt(Key + "Stage", 0);
            if (stage >= 100)
            {
                if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) Finish(stage == 100);
                return;
            }
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0f) > 300f)
                    throw new TimeoutException("Airside build review timed out; use a graphics-enabled isolated editor.");
                if (!EditorApplication.isPlaying) return;
                EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
                if (window != null) window.Repaint();
                if (releasePending && EditorApplication.timeSinceStartup >= releaseAt)
                { pressed = false; SendMouse(EventType.MouseUp); releasePending = false; }
                if (EditorApplication.timeSinceStartup < next) return;
                if (stage == 0) { if (Initialize()) Go(1, 3f); return; }
                switch (stage)
                {
                    case 1: Click(WorldPoint(Start));Go(2);break;
                    case 2: Require(tool.AirsideDrawing,"The actual first click did not start a taxiway.");MovePointer(Middle);Go(3);break;
                    case 3:
                        CheckPreview();if(!Capture("01-taxiway-grading-preview"))return;
                        quotedPrice=tool.AirsidePrice;Click(WorldPoint(Middle));Go(4);break;
                    case 4:
                        CheckCommit(1);MovePointer(End);originalHeights=(float[])game.State.world.heights.Clone();Go(5);break;
                    case 5:
                        CheckPreview();if(!Capture("02-bend-grading-preview"))return;
                        quotedPrice=tool.AirsidePrice;Click(WorldPoint(End));Go(6);break;
                    case 6:
                        CheckCommit(2);tool.SetTool(DevTool.Select);Go(7);break;
                    case 7:
                        if(!Capture("03-built-taxiway-bend"))return;
                        tool.SetTool(DevTool.Road);Click(WorldPoint(RoadStart));Go(8);break;
                    case 8: MovePointer(RoadEnd);originalHeights=(float[])game.State.world.heights.Clone();Go(9);break;
                    case 9:
                        CheckPreview();if(!Capture("04-service-road-grading-preview"))return;
                        quotedPrice=tool.AirsidePrice;Click(WorldPoint(RoadEnd));Go(10);break;
                    case 10:
                        CheckCommit(3);tool.SetTool(DevTool.Street);tool.SetStreetStyle(PublicRoadStyle.Urban);
                        game.view.FocusPoint(new Vector3(0f,FixtureHeight(0,-700),-700f),1000f,55f,0f);Go(20);break;
                    case 20:Click(WorldPoint(StreetStart));Go(11);break;
                    case 11: MovePointer(StreetEnd);originalHeights=(float[])game.State.world.heights.Clone();Go(12);break;
                    case 12:
                        Require(tool.StreetDrawing && string.IsNullOrEmpty(tool.StreetProblem),"Street preview refused: "+tool.StreetProblem);
                        Require(originalHeights.SequenceEqual(game.State.world.heights),"Street preview mutated terrain.");
                        if(!Capture("05-street-elevation-preview"))return;
                        quotedPrice=tool.StreetPrice;edgesBefore=tool.Streets.network.segments.Count;Click(WorldPoint(StreetEnd));Go(13,1f);break;
                    case 13:
                        Require(tool.Streets.network.segments.Count==edgesBefore+1,"The actual Street click did not commit: "+tool.StreetProblem);
                        totalPrice+=quotedPrice;Require(game.State.funds==SavedFunds-totalPrice,"Street payment did not match its quote.");
                        TimeStreetPreview();tool.SetTool(DevTool.Select);Go(14);break;
                    case 14:
                        if(!Capture("06-built-roads-and-taxiway"))return;
                        game.view.FocusPoint(new Vector3(0f,FixtureHeight(0,-200),-200),950f,18f,0f);Go(15);break;
                    case 15:
                        if(!Capture("07-low-angle-terrain-support"))return;
                        game.view.FocusPoint(new Vector3(-80f,FixtureHeight(-80,-60),-60f),400f,28f,30f);Go(17);break;
                    case 17:
                        if(!Capture("08-close-bend-terrain-support"))return;
                        Resize(680,650);FocusHill();Go(16,1f);break;
                    case 16:
                        if(!Capture("09-built-pavement-680"))return;
                        VerifySave();
                        var result=new Result {result="Passed: actual shared-tool clicks built a sloped taxiway, bend, service road and public street; exact visible prices, automatic grading, no grass bands, supported pavement, portable save/load, dense-forest preview timing.",
                            unityVersion=Application.unityVersion,utc=DateTime.UtcNow.ToString("O"),totalPrice=totalPrice,
                            movingStreetPreviewMs=movingPreviewMs,stationaryStreetPreviewMs=stationaryPreviewMs,screenshots=shots};
                        File.WriteAllText("Logs/airside-build-review.json",JsonUtility.ToJson(result,true));File.AppendAllText("Logs/airside-build-review.txt",result.result+"\n");
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
            var world = state.world; world.trees.Clear(); world.hasOcean = world.hasRiver = false; world.waterHeights = null;
            world.landscapeSource = "Controlled hill UI fixture"; world.hasCoordinates = false;
            world.latitude = world.longitude = 0d; world.elevationSha256 = world.coverSha256 = null;
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                float px = x * world.Spacing - ValleyWorld.Size * .5f, pz = z * world.Spacing - ValleyWorld.Size * .5f;
                int i = z * ValleyWorld.Resolution + x;
                world.heights[i] = FixtureHeight(px, pz); world.landCover[i] = 50;
            }
            world.publicRoad = new List<Vector2> { new Vector2(2000f, -3200f), new Vector2(2000f, 3200f) };
            world.roadHeights = world.publicRoad.Select(p => FixtureHeight(p.x, p.y)).ToArray();
            world.settlements = new List<Vector2> { new Vector2(2300f, -2600f), new Vector2(2300f, 2600f) };
            foreach (var site in world.sites) { site.centre = Vector2.zero; site.elevation = 100f; site.name = "Controlled hill"; }
            state.publicRoads = PublicRoadNetwork.FromWorld(world);
            Require(state.StartRegion(out string why), why); state.funds = SavedFunds;
            state.layout.Unlock(CareerTool.Taxiway);state.layout.Unlock(CareerTool.Road);
            Require(state.Validate(out why), "Controlled fixture is invalid: " + why);
            game.Initialize(state, false); game.Paused = true;
            tool = game.BuildTools; Require(game.SharedTools && tool != null, "The actual shared build tool must be active.");
            tool.SetTool(DevTool.Taxiway);tool.SetSurface(PavementSurface.Asphalt);tool.SetWidth(15f);
            totalPrice=0;
            originalHeights = (float[])world.heights.Clone();
            DevGui.Visible = true; shots.Clear();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("Airside build review mouse"); SessionState.SetInt(Key + "Mouse", mouse.deviceId);
            keyboard = InputSystem.AddDevice<Keyboard>("Airside build review keyboard"); SessionState.SetInt(Key + "Keyboard", keyboard.deviceId);
            InputSystem.onBeforeUpdate -= FeedInput; InputSystem.onBeforeUpdate += FeedInput;
            SessionState.SetString(Key + "GameViews", JsonUtility.ToJson(new GameViewSetup { views = GameViews()
                .Select(v => new SavedGameView { id = v.GetEntityId().ToString(), size = (int)v.GetType().GetProperty("selectedSizeIndex", Instance).GetValue(v) }).ToArray() }));
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView", true);
            window = (EditorWindow)ScriptableObject.CreateInstance(type); window.titleContent = new GUIContent("Airside build review"); window.Show();
            SessionState.SetString(Key + "Window", window.GetEntityId().ToString());
            pointer = new Vector2(0f, 0f); Resize(1440, 900); FocusHill();
            return true;
        }
        static float FixtureHeight(float x,float z)=>100f+.06f*Mathf.Clamp(x,-600,600)+.03f*Mathf.Clamp(z,-900,600)+2f*Mathf.Sin(x*.025f)*Mathf.Clamp01((800-Mathf.Abs(x))/200f);
        static void FocusHill()=>game.view.FocusPoint(new Vector3(0f,FixtureHeight(0,-200),-200f),1450f,55f,0f);
        static Vector2 WorldPoint(Vector2 point)
        {
            var viewport = game.view.GetComponent<Camera>().WorldToViewportPoint(new Vector3(point.x, game.valley.DisplayHeight(point), point.y));
            return new Vector2(viewport.x * width, (1f - viewport.y) * height);
        }
        static void MovePointer(Vector2 point) { pointer = WorldPoint(point); SendMouse(EventType.MouseMove); }
        static void CheckPreview()
        {
            Require(tool.AirsideDrawing && string.IsNullOrEmpty(tool.RoadProblem),"Airside preview refused: "+tool.RoadProblem);
            Require(tool.AirsidePrice>0 && tool.AirsideGradingPrice>0,"The preview did not include grading.");
            Require(game.State.funds==SavedFunds-totalPrice && originalHeights.SequenceEqual(game.State.world.heights),"Preview changed funds or terrain.");
            File.AppendAllText("Logs/airside-build-review.txt",$"Preview {tool.Tool}: price={tool.AirsidePrice}, grading={tool.AirsideGradingPrice}, grade={tool.AirsideGrade}, end={tool.AirsideEndpoint}.\n");
        }
        static void CheckCommit(int count)
        {
            totalPrice+=quotedPrice;
            Require(game.Airport.Taxiways.edges.Count==count,"Actual airside click did not commit: "+tool.RoadProblem);
            Require(game.State.funds==SavedFunds-totalPrice,"The actual airside commit charged a different price from its preview.");
            Require(game.Airport.Taxiways.edges.All(e=>e.naturalEdges),"The actual build retained taxiway grass bands.");
            Require(game.Airport.Taxiways.VergeMesh.vertexCount==0,"Taxiway grass geometry remains.");
            CheckSupport();
        }
        static void CheckSupport()
        {
            var mesh=game.Airport.Taxiways.SurfaceMesh;var vertices=mesh.vertices;var indices=mesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                var a=game.Airport.transform.TransformPoint(vertices[indices[i]]);var b=game.Airport.transform.TransformPoint(vertices[indices[i+1]]);var c=game.Airport.transform.TransformPoint(vertices[indices[i+2]]);
                for(int j=0;j<=4;j++)for(int k=0;k<=4-j;k++)
                {
                    var p=a*(j/4f)+b*(k/4f)+c*(1f-(j+k)/4f);
                    float ground=game.valley.DisplayHeight(new Vector2(p.x,p.z));
                    Require(ground<=p.y+.035f && p.y-ground<2f,$"Actual rendered terrain clips or leaves pavement floating at {p}, ground {ground}.");
                }
            }
        }
        static void TimeStreetPreview()
        {
            var trees=game.State.world.trees;int original=trees.Count;
            for(int i=0;i<220000;i++)trees.Add(new ForestTree {position=new Vector2(1200+(i%400)*4f,1200+(i/400)*2f),height=12f});
            var method=typeof(DevAirfieldTool).GetMethod("StreetTool",Instance);
            object[] args={mouse,keyboard,false,true,Vector3.zero};
            void Preview(float x)
            {
                args[4]=new Vector3(x,FixtureHeight(x,-800),-800);
                method.Invoke(tool,args);
            }
            Preview(-300);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<40;i++)Preview(-300+i*3);
            movingPreviewMs=watch.Elapsed.TotalMilliseconds/40;
            watch.Restart();for(int i=0;i<200;i++)Preview(-183);stationaryPreviewMs=watch.Elapsed.TotalMilliseconds/200;
            trees.RemoveRange(original,trees.Count-original);
            File.AppendAllText("Logs/airside-build-review.txt",$"220,000 distant trees: moving Street preview mean {movingPreviewMs:0.000} ms, cached stationary {stationaryPreviewMs:0.000} ms.\n");
        }
        static void VerifySave()
        {
            var catalog=GameAssetCatalog.Load();game.State.airport=AirportSaveData.Capture(game.Airport,catalog);
            var store=new CareerSaveStore(Path.GetFullPath(SaveFolder));Require(store.Save(game.State,out string why),why);Require(store.Load(out var loaded,out why),why);
            Require(loaded.world.heights.SequenceEqual(game.State.world.heights),"Saved grading changed.");
            var host=new GameObject("Restored airside review airport");
            try
            {
                var restored=loaded.airport.Restore(host.transform,catalog);
                Require(restored.Taxiways.edges.Count==3 && restored.Taxiways.edges.All(e=>e.naturalEdges),"Portable save lost roads or natural edges.");
                for(int i=0;i<restored.Taxiways.nodes.Count;i++)Require(restored.Taxiways.nodes[i].position==game.Airport.Taxiways.nodes[i].position,"Portable save lost a node elevation.");
            }
            finally{Object.Destroy(host);}
        }
        static float U(float value) => DevGui.U(value);
        static void Go(int stage, float settle = .7f)
        {
            SessionState.SetInt(Key + "Stage", stage); next = EditorApplication.timeSinceStartup + settle;
            File.AppendAllText("Logs/airside-build-review.txt", "Stage " + stage + "\n");
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
            File.AppendAllText("Logs/airside-build-review.txt", $"Click GUI {guiPoint}, target {width}x{height}, editor Screen {Screen.width}x{Screen.height}\n");
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
            var size = Activator.CreateInstance(sizeType, Instance, null, new[] { fixedKind, (object)wantedWidth, wantedHeight, "Airside build review temporary" }, null);
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
                File.AppendAllText("Logs/airside-build-review.txt", $"{name}: divider top={topDivider:0.000}, bottom={bottomDivider:0.000}\n");
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
                shots.Add(new Shot { name = name, width = target.width, height = target.height, price=tool.StreetDrawing?tool.StreetPrice:tool.AirsidePrice,gradingPrice=tool.AirsideGradingPrice,slope=tool.StreetDrawing?tool.StreetGrade:tool.AirsideGrade });
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
            Directory.CreateDirectory("Logs"); File.AppendAllText("Logs/airside-build-review.txt", "Failed: " + exception + "\n");
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
                File.AppendAllText("Logs/airside-build-review.txt", "Cleanup failed: " + cleanup + "\n");
            }
            finally
            {
                Application.runInBackground = SessionState.GetBool(Key + "Background", false);
                EditorApplication.isPaused = SessionState.GetBool(Key + "Paused", false); DevGui.Visible = SessionState.GetBool(Key + "HudVisible", true);
                if (exit) EditorApplication.Exit(success ? 0 : 1);
                else Debug.Log("AirsideBuildReview: " + (success ? "passed" : "failed") + "; see Logs/airside-build-review.txt");
            }
        }
    }
}
