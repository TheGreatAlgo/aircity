using System;
using System.IO;
using AirCity.EditorTools.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>A standalone art-review scene; does not modify the airport sandbox or any aircraft.</summary>
    public static class BaggageEquipmentSetup
    {
        const string ScenePath = "Assets/AirCity/Scenes/BaggageEquipment.unity";
        const string CaptureFolder = "Captures/BaggageEquipment";

        [MenuItem("AirCity/Vehicles/Create Baggage Equipment Showroom")]
        public static void CreateShowroom()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }

        public static void FromCommandLine()
        {
            try { Build(true); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Build(bool capture)
        {
            VehicleSetup.CreateMissing();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.73f, 0.83f);
            RenderSettings.ambientEquatorColor = new Color(0.49f, 0.54f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.29f, 0.31f);
            var sun = new GameObject("Daylight").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            RenderSettings.sun = sun;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Equipment apron";
            ground.transform.position = new Vector3(0f, -0.055f, 0f);
            ground.transform.localScale = new Vector3(65f, 0.1f, 55f);
            ground.GetComponent<Renderer>().sharedMaterial = Material("BaggageApron", new Color(0.38f, 0.40f, 0.41f), 0.12f);
            var marking = Material("BaggageBayPaint", new Color(0.78f, 0.76f, 0.62f), 0.25f);
            for (int i = 0; i < 5; i++)
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = "Equipment bay line";
                stripe.transform.position = new Vector3(-13f + i * 6f, 0.004f, -1f);
                stripe.transform.localScale = new Vector3(0.10f, 0.008f, 18f);
                stripe.GetComponent<Renderer>().sharedMaterial = marking;
            }

            var tractor = Equipment("BaggageTractor", new Vector3(-7f, 0f, 6f));
            var cart = Equipment("BaggageCart", Vector3.zero, -8f);
            cart.cartSteering = 8f;
            cart.CoupleBehind(tractor);
            var open = Equipment("BaggageCart", Vector3.zero, -22f);
            open.cartSteering = 14f; open.curtainOpen = 1f; open.displayLoad = true;
            open.CoupleBehind(cart);
            var loader = Equipment("BeltLoader", new Vector3(3f, 0f, 1f), -15f);
            loader.beltFrontHeight = 3.05f;
            loader.Apply();
            var parked = Equipment("BeltLoader", new Vector3(8f, 0f, -8f), 70f);
            parked.Apply();

            int index = 0;
            foreach (string name in new[] { "Bag_HardLarge", "Bag_HardSmall", "Bag_Soft", "Bag_Duffel" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{VehicleSetup.Root}/BaggageProps/Props/{name}.prefab");
                if (prefab == null) throw new InvalidOperationException($"Missing prop prefab: {name}");
                var bag = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                bag.transform.SetPositionAndRotation(new Vector3(-2.0f + index++ * 0.75f, 0f, 5.5f), Quaternion.Euler(0f, 160f, 0f));
            }

            var camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.66f, 0.72f, 0.79f);
            camera.nearClipPlane = 0.05f; camera.farClipPlane = 200f;
            camera.fieldOfView = 42f;
            camera.orthographic = true; camera.orthographicSize = 10.5f;
            View(camera, new Vector3(-20f, 19f, 24f), new Vector3(0f, 0.8f, -1f));
            foreach (var group in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude))
                group.animateCrossFading = false;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            if (!capture) return;

            Directory.CreateDirectory(CaptureFolder);
            Shot(camera, "overview", camera.transform.position, new Vector3(0f, 0.8f, -1f));
            Shot(camera, "tractor", tractor.transform.position + new Vector3(-4.4f, 2.7f, 5.0f), tractor.transform.position + Vector3.up * 0.7f);
            Shot(camera, "cart-open", open.transform.TransformPoint(new Vector3(-5.2f, 2.5f, -0.4f)), open.transform.TransformPoint(new Vector3(0f, 0.95f, 1.1f)));
            Shot(camera, "belt-loader", loader.transform.position + new Vector3(-8.5f, 4.5f, -7.0f), loader.transform.position + new Vector3(0f, 1.3f, 1.7f));
            Shot(camera, "bags", new Vector3(-3.2f, 1.6f, 8.2f), new Vector3(-0.85f, 0.35f, 5.5f));
            Debug.Log("Baggage equipment: created standalone showroom, prefabs and review captures.");
        }

        static BaggageEquipmentRig Equipment(string model, Vector3 position, float bearing = 0f)
        {
            var prefab = VehicleSetup.Prefab(model);
            if (prefab == null) throw new InvalidOperationException($"Missing equipment prefab: {model}");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, bearing, 0f));
            var rig = go.GetComponent<BaggageEquipmentRig>();
            rig.Apply();
            return rig;
        }

        static Material Material(string name, Color color, float smoothness)
        {
            string path = $"{VehicleSetup.Root}/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = AirCity.EditorTools.Aircraft.AircraftAssetPostprocessor.LitShader();
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void View(Camera camera, Vector3 position, Vector3 target)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);
        }

        static void Shot(Camera camera, string name, Vector3 position, Vector3 target)
        {
            camera.orthographic = name == "overview";
            View(camera, position, target);
            SceneCapture.Capture(camera, $"{CaptureFolder}/{name}.png", 1800, 1100);
        }
    }
}
