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
    /// <summary>Creates deicing assets and upgrades generated model parts without replacing prefab references.</summary>
    public static class DeicingEquipmentSetup
    {
        const string Root = "Assets/AirCity/Vehicles";
        const string ResourcePath = Root + "/Resources/DeicingAssets.asset";

        [MenuItem("AirCity/Vehicles/Create Deicing Equipment Assets")]
        public static void CreateAssets()
        {
            AssetDatabase.Refresh();
            EnsureFolder(Root + "/Resources");
            var assets = AssetDatabase.LoadAssetAtPath<DeicingAssets>(ResourcePath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<DeicingAssets>();
                AssetDatabase.CreateAsset(assets, ResourcePath);
            }
            assets.truckType = Type("DeicingTruck");
            assets.tankType = Type("DeicingTank");
            assets.tankerType = Type("DeicingTanker");
            assets.recyclerType = Type("DeicingRecycler");
            assets.truckPrefab = Prefab("DeicingTruck", assets.truckType);
            assets.tankPrefab = Prefab("DeicingTank", assets.tankType);
            assets.tankerPrefab = Prefab("DeicingTanker", assets.tankerType);
            assets.recyclerPrefab = Prefab("DeicingRecycler", assets.recyclerType);
            assets.sprayMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Spray.mat");
            assets.boomRestReach = Mathf.Sqrt(7.4f * 7.4f + .6f * .6f);
            assets.boomExtension = 3f;
            assets.boomLowerLength = Mathf.Sqrt(4f * 4f + .6f * .6f);
            assets.boomUpperLength = 3.4f;
            assets.boomLowerRestAngle = Mathf.Atan2(.6f, 4f) * Mathf.Rad2Deg;
            assets.boomLiftPivot = new Vector3(0f, 2.8f, 1.2f);
            assets.cabinNozzleOffset = new Vector3(0f, .85f, .85f);
            assets.refillPoint = new Vector3(-1.35f, 1.42f, -.85f);
            assets.tankPumpPoint = new Vector3(-2.12f, 1.43f, 6.02f);
            assets.tankerHosePoint = new Vector3(-1.25f, 1f, -2f);
            assets.tankDeliveryPoint = new Vector3(2.42f, .86f, 1.51f);
            assets.refillPoint = new Vector3(-1.35f, 1.42f, -.85f);
            assets.tankPumpPoint = new Vector3(-2.12f, 1.43f, 6.02f);
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
        }

        public static void FromCommandLine()
        {
            try { CreateAssets(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        /// <summary>Review captures in an untitled empty scene; never saves or replaces Sandbox.</summary>
        public static void CaptureFromCommandLine()
        {
            try
            {
                CreateAssets();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.66f, .73f, .83f);
                RenderSettings.ambientEquatorColor = new Color(.49f, .54f, .60f);
                RenderSettings.ambientGroundColor = new Color(.27f, .29f, .31f);
                var sun = new GameObject("Daylight").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 2f; sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                RenderSettings.sun = sun;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.transform.position = new Vector3(0f, -.055f, 0f);
                ground.transform.localScale = new Vector3(80f, .1f, 80f);
                var apron = new Material(Aircraft.AircraftAssetPostprocessor.LitShader());
                apron.SetColor("_BaseColor", new Color(.38f, .40f, .41f));
                ground.GetComponent<Renderer>().sharedMaterial = apron;
                var assets = AssetDatabase.LoadAssetAtPath<DeicingAssets>(ResourcePath);
                var truck = (GameObject)PrefabUtility.InstantiatePrefab(assets.truckPrefab);
                var tank = (GameObject)PrefabUtility.InstantiatePrefab(assets.tankPrefab);
                tank.transform.position = new Vector3(9f, 0f, 0f);
                var camera = new GameObject("Review camera").AddComponent<Camera>();
                camera.nearClipPlane = .05f; camera.farClipPlane = 200f; camera.fieldOfView = 42f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.66f, .72f, .79f);
                Directory.CreateDirectory("Captures/DeicingEquipment");
                Shot(camera, "truck", new Vector3(-13f, 8f, 18f), new Vector3(0f, 1.8f, 3f));
                truck.SetActive(false);
                Shot(camera, "tank", tank.transform.position + new Vector3(-12f, 8f, -10f),
                    tank.transform.position + new Vector3(0f, 1.6f, 3.8f));
                UnityEngine.Object.DestroyImmediate(apron);
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Shot(Camera camera, string name, Vector3 position, Vector3 target)
        {
            camera.transform.position = position; camera.transform.LookAt(target);
            SceneCapture.Capture(camera, $"Captures/DeicingEquipment/{name}.png", 1800, 1100);
        }

        static VehicleType Type(string model)
        {
            var type = AssetDatabase.LoadAssetAtPath<VehicleType>($"{Root}/{model}/{model}.vehicle");
            if (type == null) throw new InvalidOperationException($"Generate the {model} art before creating prefabs.");
            return type;
        }

        static GameObject Prefab(string model, VehicleType type)
        {
            string path = $"{Root}/{model}/{model}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{model}/Models/{model}.fbx");
            if (source == null) throw new InvalidOperationException($"Missing generated {model}.fbx");
            // The original deicer had a roof pod. Upgrade its mesh hierarchy in place, retaining the prefab GUID,
            // root file ID, authored components and unrelated custom children. Other generated deicing parts
            // receive the same source comparison before an in-place refresh.
            bool upgrade = existing != null && !ModelMatches(existing, source);
            if (existing != null && !upgrade) return existing;
            var go = existing != null ? PrefabUtility.LoadPrefabContents(path) : new GameObject(model);
            var generated = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                go.name = model;
                PrefabUtility.UnpackPrefabInstance(generated, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var sourceGroup = generated.GetComponent<LODGroup>();
                var importedLods = sourceGroup != null ? sourceGroup.GetLODs() : null;
                // Renderers must leave the source group before joining the saved group. Merely destroying the
                // temporary root after SaveAsPrefabAsset registers every renderer with both groups meanwhile.
                if (sourceGroup != null)
                {
                    sourceGroup.enabled = false;
                    sourceGroup.SetLODs(Array.Empty<LOD>());
                    UnityEngine.Object.DestroyImmediate(sourceGroup);
                }
                var lods = go.GetComponent<LODGroup>();
                bool lodsEnabled = lods == null || lods.enabled;
                if (lods != null)
                {
                    lods.enabled = false;
                    lods.SetLODs(Array.Empty<LOD>());
                }
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                {
                    var child = go.transform.GetChild(i);
                    if (GeneratedPart(child.name)) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                while (generated.transform.childCount > 0) generated.transform.GetChild(0).SetParent(go.transform, false);
                if (importedLods != null)
                {
                    if (lods == null) lods = go.AddComponent<LODGroup>();
                    lods.SetLODs(importedLods); lods.RecalculateBounds();
                }
                if (lods != null) lods.enabled = lodsEnabled;
                var lights = go.GetComponent<VehicleLights>();
                if (lights == null) lights = go.AddComponent<VehicleLights>();
                lights.type = type;
                lights.glowMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/VehicleLightGlow.mat");
                if (existing == null)
                {
                    lights.headlights = lights.work = lights.beacon = !type.IsBuilding;
                    lights.flood = type.IsBuilding;
                }
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(generated);
                if (existing != null) PrefabUtility.UnloadPrefabContents(go);
                else UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static bool ModelMatches(GameObject prefab, GameObject source)
        {
            var saved = prefab.GetComponentsInChildren<Transform>(true);
            foreach (var part in source.GetComponentsInChildren<Transform>(true))
            {
                if (part == source.transform) continue;
                Transform match = null;
                foreach (var candidate in saved) if (candidate.name == part.name) { match = candidate; break; }
                if (match == null || Vector3.Distance(match.localPosition, part.localPosition) > .0001f ||
                    Quaternion.Angle(match.localRotation, part.localRotation) > .001f ||
                    Vector3.Distance(match.localScale, part.localScale) > .0001f) return false;
                var mesh = part.GetComponent<MeshFilter>();
                var savedMesh = match.GetComponent<MeshFilter>();
                if (mesh != null && (savedMesh == null || savedMesh.sharedMesh != mesh.sharedMesh)) return false;
            }
            return true;
        }
        static bool GeneratedPart(string name) => name.StartsWith("Body_LOD", StringComparison.Ordinal) ||
            name.StartsWith("Wheel_", StringComparison.Ordinal) || name.StartsWith("Boom", StringComparison.Ordinal) ||
            name == "SprayNozzle" || name == "HoseReel";

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
