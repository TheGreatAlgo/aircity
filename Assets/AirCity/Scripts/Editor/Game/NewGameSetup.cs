using System;
using System.IO;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Dev;
using AirCity.EditorTools.World;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    public static class NewGameSetup
    {
        public const string ScenePath = "Assets/AirCity/Scenes/NewGame.unity";
        const string MaterialFolder = "Assets/AirCity/World/Materials";

        [MenuItem("AirCity/Game/Update New Game World Assets")]
        public static void UpdateWorldAssets()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            WorldAssetSetup.Ensure();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var game = Object.FindAnyObjectByType<CareerGame>();
            if (game == null || game.valley == null) throw new InvalidOperationException("The New Game scene has no career/valley.");
            AssignWorldAssets(game.valley);
            LandscapeLight();
            var tool = game.GetComponent<CareerRoadTool>();
            if (tool == null) tool = game.gameObject.AddComponent<CareerRoadTool>();
            tool.previewMaterial = game.GetComponent<CareerHud>().guideMaterial;
            var builder = game.GetComponent<CareerBuildTool>();
            if (builder == null) builder = game.gameObject.AddComponent<CareerBuildTool>();
            builder.previewMaterial = BuildPreviewMaterial();
            var inclusion = game.GetComponentsInChildren<Terrain>(true);
            foreach (var terrain in inclusion)
            {
                terrain.terrainData.terrainLayers = Layers(game.valley); EditorUtility.SetDirty(terrain.terrainData);
            }
            EditorUtility.SetDirty(game.valley); EditorUtility.SetDirty(tool);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }

        static void AssignWorldAssets(ValleyView valley)
        {
            var terrainShader = ValleyTerrainShaderSetup.Ensure();
            if (valley.terrainMaterial != null)
            {
                valley.terrainMaterial.shader = terrainShader;
                EditorUtility.SetDirty(valley.terrainMaterial);
            }
            ValleySurfaceSetup.Assign(valley);
            valley.trees = AssetDatabase.LoadAssetAtPath<TreeLibrary>(WorldAssetSetup.TreesPath);
            valley.groundCover = GroundCoverSetup.EnsureLibrary();
            valley.ruralRoad = AssetDatabase.LoadAssetAtPath<PublicRoadProfile>(WorldAssetSetup.RuralPath);
            valley.airportRoad = AssetDatabase.LoadAssetAtPath<PublicRoadProfile>(WorldAssetSetup.AccessPath);
            valley.trafficSignalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldAssetSetup.SignalPath);
            valley.bridgeMaterial = WorldAssetSetup.SolidMaterial("BridgeConcrete", new Color(.43f, .45f, .43f));
        }
        static TerrainLayer[] Layers(ValleyView v) => new[] { v.grassLayer, v.dirtLayer, v.roadLayer, v.dryLayer, v.rockLayer, v.forestLayer };
        static Material BuildPreviewMaterial()
        {
            string path = MaterialFolder + "/BuildPreview.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/AirCity/Shaders/BuildPreview.shader")) { name = "Build preview" };
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        [MenuItem("AirCity/Game/Create New Game Scene")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AirfieldSetup.CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build(); EditorSceneManager.SaveScene(scene, ScenePath);
            // Career is the launch scene; keep all existing review/sandbox scenes available.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("NewGameSetup: created " + ScenePath);
        }

        public static CareerGame Build()
        {
            WorldAssetSetup.Ensure();
            var type = AircraftSetup.AllTypes().First(t => t.model == "Cessna172");
            var prefab = AircraftSetup.Prefab(type);
            if (prefab == null) throw new InvalidOperationException("Create the light-aircraft prefab before creating New Game.");
            Directory.CreateDirectory(MaterialFolder); AssetDatabase.Refresh();
            var terrainMaterial = Material("ValleyTerrain", "Universal Render Pipeline/Terrain/Lit", Color.white);
            var scenery = Material("ValleyScenery", "Universal Render Pipeline/Lit", Color.white);
            var guide = Material("SurveyOutline", "Universal Render Pipeline/Unlit", new Color(.97f, .72f, .26f));
            var root = new GameObject("Career");
            var game = root.AddComponent<CareerGame>();
            var world = new GameObject("Region"); world.transform.SetParent(root.transform, false);
            game.valley = world.AddComponent<ValleyView>();
            game.valley.terrainMaterial = terrainMaterial; game.valley.sceneryMaterial = scenery;
            AssignWorldAssets(game.valley);
            game.valley.grassTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AirCity/Airfield/Textures/Grass_Albedo.png");
            game.valley.dirtTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AirCity/Airfield/Textures/Dirt_Albedo.png");
            game.valley.grassLayer = Layer("ValleyGrass", game.valley.grassTexture, 16f);
            game.valley.dirtLayer = Layer("ValleyDirt", game.valley.dirtTexture, 20f);
            game.valley.roadLayer = Layer("ValleyRoad", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AirCity/Airfield/Textures/Asphalt_Albedo.png"), 12f);
            // Unity only includes Terrain's native build resources when a build scene contains a Terrain.
            var inclusion = new GameObject("Terrain build resources"); inclusion.transform.SetParent(root.transform, false); inclusion.SetActive(false);
            string terrainPath = "Assets/AirCity/World/TerrainBuildResources.asset";
            var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainPath);
            if (terrainData == null)
            {
                terrainData = new TerrainData { name = "Terrain build resources", heightmapResolution = 33, size = Vector3.one,
                    alphamapResolution = 16, baseMapResolution = 16 };
                terrainData.terrainLayers = new[] { game.valley.grassLayer, game.valley.dirtLayer, game.valley.roadLayer };
                AssetDatabase.CreateAsset(terrainData, terrainPath);
            }
            ValleySurfaceSetup.Assign(game.valley);
            if (terrainData.terrainLayers.Length != 6)
            {
                terrainData.terrainLayers = Layers(game.valley);
                EditorUtility.SetDirty(terrainData);
            }
            if (terrainData.alphamapResolution != 16 || terrainData.baseMapResolution != 16)
            {
                terrainData.alphamapResolution = 16; terrainData.baseMapResolution = 16; EditorUtility.SetDirty(terrainData);
            }
            var includedTerrain = inclusion.AddComponent<Terrain>(); includedTerrain.terrainData = terrainData;
            includedTerrain.materialTemplate = terrainMaterial; includedTerrain.drawInstanced = true;
            var template = new GameObject("Airfield template"); template.transform.SetParent(root.transform, false); template.SetActive(false);
            game.airportTemplate = template.AddComponent<Airport>();
            game.airportTemplate.groundSize = 0f; game.airportTemplate.timeScale = 0f;
            AirfieldSetup.AssignMaterials(game.airportTemplate); game.airportTemplate.aircraftTypes.Add(type);
            game.aircraftPrefab = prefab;
            game.charterLivery = AssetDatabase.LoadAssetAtPath<AircraftLiveryAsset>("Assets/AirCity/Liveries/BushYellow.asset");
            var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>(); camera.fieldOfView = 45f;
            camera.backgroundColor = new Color(.62f, .76f, .80f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = true; camera.allowMSAA = true;
            cameraObject.AddComponent<AudioListener>();
            game.view = cameraObject.AddComponent<CareerCamera>(); game.view.valley = game.valley; game.view.Region();
            var hud = root.AddComponent<CareerHud>(); hud.guideMaterial = guide;
            root.AddComponent<CareerRoadTool>().previewMaterial = guide;
            root.AddComponent<CareerBuildTool>().previewMaterial = BuildPreviewMaterial();
            var sunObject = new GameObject("Morning sun"); var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, .98f, .94f);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .7f; sunObject.transform.rotation = Quaternion.Euler(43f, -32f, 0f);
            RenderSettings.sun = sun; RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.63f, .74f, .82f);
            RenderSettings.ambientEquatorColor = new Color(.46f, .53f, .46f);
            RenderSettings.ambientGroundColor = new Color(.25f, .30f, .23f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = camera.backgroundColor; RenderSettings.fogStartDistance = 5500f; RenderSettings.fogEndDistance = 14500f;
            LandscapeLight(); AircraftLights.Glow = 0f;
            return game;
        }

        static void LandscapeLight()
        {
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.intensity = 1.6f; sun.color = new Color(1f, .98f, .94f);
                sun.shadowStrength = .85f;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.45f, .53f, .60f);
            RenderSettings.ambientEquatorColor = new Color(.25f, .29f, .24f);
            RenderSettings.ambientGroundColor = new Color(.15f, .18f, .14f);
        }

        static Material Material(string name, string shader, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var found = Shader.Find(shader);
            if (found == null) throw new InvalidOperationException("Missing shader " + shader);
            material = new Material(found) { name = name }; material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path); return material;
        }

        static TerrainLayer Layer(string name, Texture2D texture, float tileSize)
        {
            string path = "Assets/AirCity/World/" + name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer != null) return layer;
            layer = new TerrainLayer { name = name, diffuseTexture = texture, tileSize = new Vector2(tileSize, tileSize) };
            AssetDatabase.CreateAsset(layer, path); return layer;
        }

        public static void InstallFromCommandLine()
        {
            try { Create(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void PlayFromCommandLine()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        public static void BuildReviewPlayerFromCommandLine()
        {
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { ScenePath }, target = BuildTarget.StandaloneOSX,
                    locationPathName = "Captures/NewGame/AirCityReview.app", options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded) throw new Exception("Career player build failed: " + report.summary.result);
                Debug.Log("NewGameSetup: review player built successfully"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        /// <summary>Real geometry and flight smoke test, plus captures. Saves only under ignored Logs/Captures.</summary>
        public static void ReviewFromCommandLine()
        {
            try
            {
                Create();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var game = Object.FindAnyObjectByType<CareerGame>();
                game.Initialize(CareerState.New(19770616, false), false);
                var camera = game.view.GetComponent<Camera>();
                game.view.Region();
                SceneCapture.Capture(camera, "Captures/NewGame/region.png", 1600, 1000);
                game.Survey(0);
                game.State.Build(out _, 0);
                game.Initialize(game.State, false);
                SceneCapture.Capture(camera, "Captures/NewGame/starting-site.png", 1600, 1000);
                for (int i = 0; i < 6; i++)
                    if (!game.State.Build(out string why)) throw new Exception(why);
                game.Initialize(game.State, false);
                SceneCapture.Capture(camera, "Captures/NewGame/first-airfield.png", 1600, 1000);
                if (!game.AcceptCharter()) throw new Exception(game.Message);
                for (int i = 0; i < 60; i++) game.Advance(1f);
                var store = new CareerSaveStore(Path.GetFullPath("Logs/NewGameReview"));
                if (!store.Save(game.State, out string problem) || !store.Load(out var resumed, out problem)) throw new Exception(problem);
                game.Initialize(resumed, false);
                for (int i = 0; i < 1800 && game.State.charterActive; i++) game.Advance(1f);
                if (game.State.completedCharters != 1) throw new Exception("The charter did not complete: " + game.Message);
                if (!game.State.Build(out problem)) throw new Exception(problem);
                game.Initialize(game.State, false);
                SceneCapture.Capture(camera, "Captures/NewGame/expanded-airfield.png", 1600, 1000);
                File.WriteAllText("Logs/new-game-review.json", JsonUtility.ToJson(new ReviewResult {
                    seed = game.State.world.seed, completedCharters = game.State.completedCharters, funds = game.State.funds,
                    runwayLength = game.State.RunwayLength, resumedMidFlight = true
                }, true));
                if (Environment.GetCommandLineArgs().Contains("-careerBuildPlayer")) BuildReviewPlayerFromCommandLine();
                else EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [Serializable] class ReviewResult { public int seed, completedCharters, funds; public float runwayLength; public bool resumedMidFlight; }
    }
}
