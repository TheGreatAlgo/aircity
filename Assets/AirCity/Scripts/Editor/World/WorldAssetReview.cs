using System;
using System.IO;
using System.Linq;
using AirCity.EditorTools.Dev;
using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.World
{
    public static class WorldAssetReview
    {
        const string Root = "Captures/WorldAssets/";

        [MenuItem("AirCity/World/Create Tree and Road Review Scene")]
        public static void CreateReview()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            WorldAssetSetup.Ensure(); PublicRoadKitSetup.Create();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CameraAndLight();
            var tree = AssetDatabase.LoadAssetAtPath<GameObject>(WorldAssetSetup.Root + "/Vegetation/Models/Spruce1.prefab");
            var pine = AssetDatabase.LoadAssetAtPath<GameObject>(WorldAssetSetup.Root + "/Vegetation/Models/Pine1.prefab");
            Object.Instantiate(tree, new Vector3(-55f, 0f, 50f), Quaternion.identity);
            Object.Instantiate(pine, new Vector3(-35f, 0f, 50f), Quaternion.identity);
            Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PublicRoadKitSetup.PrefabPath("SignalTjunction")), new Vector3(60f, 0f, 45f), Quaternion.identity);
            Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PublicRoadKitSetup.PrefabPath("HighwayOverpass")), new Vector3(-40f, 0f, -100f), Quaternion.identity);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Review ground";
            floor.transform.localScale = Vector3.one * 60f;
            floor.GetComponent<Renderer>().sharedMaterial = WorldAssetSetup.SolidMaterial("ReviewGround", new Color(.24f, .29f, .16f));
            Camera.main.transform.SetPositionAndRotation(new Vector3(175f, 145f, -230f), Quaternion.LookRotation(new Vector3(-175f, -125f, 180f)));
            EditorSceneManager.SaveScene(scene, "Assets/AirCity/Scenes/WorldAssets.unity");
        }

        static Camera CameraAndLight()
        {
            var go = new GameObject("Main Camera"); go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>(); camera.fieldOfView = 40f; camera.nearClipPlane = .15f; camera.farClipPlane = 20000f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.65f, .75f, .80f);
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.7f;
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f); RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = new Color(.61f, .69f, .75f);
            RenderSettings.ambientEquatorColor = new Color(.35f, .41f, .34f); RenderSettings.ambientGroundColor = new Color(.18f, .21f, .16f);
            RenderSettings.fog = false; return camera;
        }

        public static void FromCommandLine()
        {
            try
            {
                Directory.CreateDirectory(Root); WorldAssetSetup.Ensure(); PublicRoadKitSetup.Create(true);
                CreateReview(); var camera = Camera.main;
                camera.transform.position = new Vector3(-28f, 16f, 6f); camera.transform.LookAt(new Vector3(-48f, 12f, 50f));
                SceneCapture.Capture(camera, Root + "trees-close.png", 1600, 1100);
                camera.transform.position = new Vector3(9f, 42f, -19f); camera.transform.LookAt(new Vector3(60f, 0f, 45f));
                SceneCapture.Capture(camera, Root + "public-junction.png", 1600, 1100);
                camera.transform.position = new Vector3(35f, 45f, -190f); camera.transform.LookAt(new Vector3(-40f, 5f, -100f));
                SceneCapture.Capture(camera, Root + "highway-overpass.png", 1600, 1100);

                if (File.Exists(NewGameSetup.ScenePath)) NewGameSetup.UpdateWorldAssets(); else NewGameSetup.Create();
                var game = Object.FindAnyObjectByType<CareerGame>();
                var state = CareerState.New(19770616, false); state.Build(out _, 0);
                while (!state.IsOpen) if (!state.Build(out string why)) throw new Exception(why);
                game.Initialize(state, false); camera = game.view.GetComponent<Camera>();
                SceneCapture.Capture(camera, Root + "forest-airport.png", 1600, 1100);
                var junction = game.valley.RoadNetwork.nodes.First(n => n.control == JunctionControl.TrafficLights);
                camera.transform.position = junction.position + new Vector3(-52f, 33f, -47f); camera.transform.LookAt(junction.position);
                SceneCapture.Capture(camera, Root + "airport-road-connection.png", 1600, 1100);
                if (!game.AcceptCharter()) throw new Exception(game.Message);
                for (int i = 0; i < 1800 && state.charterActive; i++) game.Advance(1f);
                if (state.completedCharters != 1) throw new Exception("The airport charter did not finish with the new public-road network.");
                File.WriteAllText("Logs/world-assets-review.txt", "Tree/road assets, elevated crossing, signal junction, airport connection and charter verified.\n");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
