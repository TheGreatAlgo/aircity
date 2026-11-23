using System;
using System.Linq;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>A repeatable mixed-terrain Sandbox for construction and close-up road rendering.</summary>
    [InitializeOnLoad]
    public static class RoadTerrainScenario
    {
        const string Pending = "AirCity.RoadTerrainScenario.Pending";
        const string PreviousScene = "AirCity.RoadTerrainScenario.PreviousScene";
        public const float RiverLevel = 100f;
        const float SeaLevel = 73f;
        public static readonly Vector2 BankStart = new Vector2(-375f, -425f), BankEnd = new Vector2(-375f, -175f);
        public static readonly Vector2 CrossingStart = new Vector2(-600f, -50f), CrossingEnd = new Vector2(-300f, -50f);
        public static readonly Vector2 CliffStart = new Vector2(600f, 610f), CliffEnd = new Vector2(600f, 1020f);
        public static readonly Vector2 CliffProbe = new Vector2(650f, 825f);
        public static readonly Vector2 RavineStart = new Vector2(680f, 1060f), RavineEnd = new Vector2(1110f, 1060f);
        public static readonly Vector2 GorgeStart = new Vector2(837.5f,1237.5f), GorgeEnd = new Vector2(1000f,1237.5f);
        public static readonly Vector2 InletStart = new Vector2(-1540f, 0f), InletEnd = new Vector2(-1540f, 1575f);

        static RoadTerrainScenario() => EditorApplication.playModeStateChanged += PlayModeChanged;

        [MenuItem("AirCity/Game/Play Road Terrain Scenario", true)]
        static bool CanPlay() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("AirCity/Game/Play Road Terrain Scenario")]
        static void Play()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(NewGameSetup.ScenePath);
            if (scene == null) throw new InvalidOperationException("Create the New Game scene before opening the road terrain scenario.");
            var current = EditorSceneManager.playModeStartScene;
            SessionState.SetString(PreviousScene, current != null ? AssetDatabase.GetAssetPath(current) : "");
            SessionState.SetBool(Pending, true);
            EditorSceneManager.playModeStartScene = scene;
            EditorApplication.EnterPlaymode();
        }

        static void PlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                var game = Object.FindAnyObjectByType<CareerGame>();
                Initialize(game);
                FocusBank(game);
            }
            if (change != PlayModeStateChange.EnteredEditMode) return;
            string previous = SessionState.GetString(PreviousScene, "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            SessionState.EraseBool(Pending);
            SessionState.EraseString(PreviousScene);
        }

        public static CareerState State()
        {
            int samples = ValleyWorld.Resolution * ValleyWorld.Resolution;
            var world = new ValleyWorld
            {
                seed = 20261122, landscapeSource = "Road Terrain Scenario", forestVersion = ForestPopulation.Version,
                hasOcean = true, seaLevel = SeaLevel, heights = new float[samples], waterHeights = new float[samples],
                landCover = new byte[samples],
                publicRoad = { new Vector2(1850f,-3200f), new Vector2(1850f,3200f) },
                settlements = { new Vector2(2150f,-2700f), new Vector2(2150f,2700f) }
            };
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                float px = x*world.Spacing-ValleyWorld.Size*.5f, pz = z*world.Spacing-ValleyWorld.Size*.5f;
                int index = z*ValleyWorld.Resolution+x;
                float channelDistance = Mathf.Abs(px-RiverCentre(pz));
                world.heights[index] = Height(px,pz,channelDistance);
                bool river = channelDistance <= 62.5f;
                if (river) world.waterHeights[index] = RiverLevel;
                world.landCover[index] = (byte)(river || world.heights[index] <= world.SeaLevel ? 80 : 30);
            }
            world.roadHeights = world.publicRoad.Select(world.Height).ToArray();
            var origin = new Vector2(-120f,-290f);
            for (int i = 0; i < 3; i++)
                world.sites.Add(new AirportSite { name = world.landscapeSource, centre = origin, elevation = world.Height(origin) });
            var state = new CareerState { world = world, handsOn = true, layout = new CareerLayout(),
                mode = GameMode.Sandbox, publicRoads = PublicRoadNetwork.FromWorld(world) };
            if (!state.StartRegion(out string problem)) throw new InvalidOperationException(problem);
            return state;
        }

        static float Ramp(float a, float b, float x) => Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(a,b,x));
        static float RiverCentre(float z) => -450f + Ramp(450f,1000f,Mathf.Abs(z)) * Mathf.Sin(z*.0025f)*65f;

        static float Height(float x, float z, float channelDistance)
        {
            float fields = 102f + Mathf.Sin(x*.014f+z*.004f)*1.7f + Mathf.Sin(z*.023f+x*.009f)*1.1f;
            float river = 76f + Ramp(22f,62.5f,channelDistance)*24.08f +
                Ramp(62.5f,75f,channelDistance)*(.74f + .13f*Mathf.Sin(z*.08f));
            float ground = Mathf.Lerp(river, fields, Ramp(82f,150f,channelDistance));
            float cliff = Ramp(610f,650f,x) * (1f-Ramp(1170f,1400f,x)) * Ramp(260f,600f,z) *
                (1f-Ramp(1270f,1640f,z)) * (91f+8f*Mathf.Sin(z*.006f));
            float ravineAxis = 890f + Mathf.Sin(z*.006f)*36f;
            float ravineWidth = Mathf.Lerp(1f,.39f,Ramp(1100f,1237.5f,z));
            float ravine = Ramp(90f,170f,cliff) + Ramp(100f*ravineWidth,148f*ravineWidth,Mathf.Abs(x-ravineAxis));
            ground += cliff - (1f-Mathf.Clamp01(ravine))*110f*Ramp(710f,925f,z)*(1f-Ramp(1410f,1690f,z));
            float coast = 1600f + Mathf.Cos(z*.004f)*95f;
            return Mathf.Lerp(Mathf.Max(SeaLevel+5f,ground), 43f, Ramp(coast-145f,coast+95f,-x));
        }

        static void Initialize(CareerGame game)
        {
            game.Initialize(State(), false);
            game.Paused = true;
            game.BuildTools.SetTool(DevTool.Street);
            DevGui.Visible = true;
        }

        static void FocusBank(CareerGame game) => game.view.FocusPoint(new Vector3(-405f,101f,-300f), 380f, 40f, 255f);

        public static void CaptureFromCommandLine()
        {
            EditorSceneManager.OpenScene(NewGameSetup.ScenePath);
            var game = Object.FindAnyObjectByType<CareerGame>();
            Initialize(game);
            using var driver = new RoadToolDriver(game);
            var camera = game.view.GetComponent<Camera>();
            var previous = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                Place(driver, DevTool.Street, BankStart, BankEnd);
                Vector3 bank = new Vector3(-375f,100.8f,-300f);
                Capture("shoreline", bank, bank+new Vector3(29f,4.5f,23f), 58f);
                Place(driver, DevTool.Road, BankStart + new Vector2(-150f,0f), BankEnd + new Vector2(-150f,0f));
                Vector3 service = new Vector3(-525f,100.8f,-300f);
                Capture("service-road-bank",service,service+new Vector3(-24f,4.5f,24f),58f);
                Place(driver, DevTool.Street, CrossingStart, CrossingEnd);
                Capture("river-crossing", new Vector3(-450f,99f,-135f), new Vector3(-670f,270f,-500f), 53f);
                driver.tool.SetRoadBridgeDesign(RoadBridgeDesign.Arch);
                Place(driver, DevTool.Street, CrossingStart+new Vector2(0f,235f), CrossingEnd+new Vector2(0f,235f));
                driver.tool.SetRoadBridgeDesign(RoadBridgeDesign.Automatic);
                Capture("river-arch", new Vector3(-451f,108f,185f), new Vector3(-586f,177f,29f), 59f);
                Place(driver, DevTool.Street, CliffStart, CliffEnd);
                Place(driver, DevTool.Street, RavineStart, RavineEnd);
                Capture("cliffside-and-ravine", new Vector3(725f,125f,840f), new Vector3(270f,275f,555f), 55f);
                Capture("ravine-suspension", new Vector3(895f,190f,1060f), new Vector3(812f,260f,866f), 61f);
                Place(driver, DevTool.Street, GorgeStart, GorgeEnd);
                Capture("short-gorge-suspension", new Vector3(921f,170f,1237.5f), new Vector3(824f,232f,1380f), 57f);
                Place(driver, DevTool.Street, InletStart, InletEnd);
                Capture("coastal-inlet", new Vector3(-1540f,80f,780f), new Vector3(-2310f,540f,980f), 49f);
            }
            finally { ShaderUtil.allowAsyncCompilation = previous; }

            void Capture(string name, Vector3 target, Vector3 position, float fov)
            {
                camera.transform.position = position;
                camera.transform.LookAt(target);
                camera.fieldOfView = fov;
                SceneCapture.Capture(camera, $"Captures/RoadTerrain/{name}.png", 1440, 900);
            }
        }

        public static void Place(RoadToolDriver driver, DevTool tool, Vector2 start, Vector2 end)
        {
            driver.tool.SetTool(DevTool.Select);
            driver.tool.SetTool(tool);
            Click(start);
            Click(end);
            driver.tool.SetTool(DevTool.Select);

            void Click(Vector2 point)
            {
                var aim = new Vector3(point.x,driver.game.valley.DisplayHeight(point),point.y);
                driver.GestureFrame(aim, false, centre:true, cameraOffset:new Vector3(120f,400f,-140f));
                if (driver.Problem != null) throw PlacementError(point);
                int previous = driver.Chain;
                driver.GestureFrame(aim,true);
                driver.GestureFrame(aim,false);
                if (driver.Chain < 0 || driver.Chain == previous) throw PlacementError(point);
            }

            InvalidOperationException PlacementError(Vector2 point) =>
                new InvalidOperationException($"{tool} placement at {point} on {start} to {end}: {driver.Describe()}");
        }
    }
}
