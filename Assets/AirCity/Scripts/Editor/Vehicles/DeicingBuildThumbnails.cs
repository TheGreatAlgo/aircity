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
    /// <summary>Build-browser portraits from the actual equipment prefabs, independent of the open airport.</summary>
    public static class DeicingBuildThumbnails
    {
        [MenuItem("AirCity/Vehicles/Refresh Deicing Build Portraits")]
        public static void Build()
        {
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool restore = !string.IsNullOrEmpty(previous.path);
            if (!restore && previous.rootCount > 0) throw new InvalidOperationException("Save the open scene before refreshing build portraits.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, restore ? NewSceneMode.Additive : NewSceneMode.Single);
            EditorSceneManager.SetActiveScene(scene);
            try
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.8f, .86f, .88f);
                RenderSettings.ambientEquatorColor = new Color(.65f, .72f, .73f);
                RenderSettings.ambientGroundColor = new Color(.39f, .43f, .42f);
                var sun = new GameObject("Portrait light").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 2.2f;
                sun.transform.rotation = Quaternion.Euler(45, -35, 0);
                var camera = new GameObject("Build portrait").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.82f, .88f, .85f, 0);
                camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 300;
                const string folder = "Assets/AirCity/UI/Resources/AirCityBuild";
                Directory.CreateDirectory(folder);
                foreach (string model in new[] { "DeicingTank", "DeicingTruck", "DeicingRecycler" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/AirCity/Vehicles/{model}/{model}.prefab");
                    var type = AssetDatabase.LoadAssetAtPath<VehicleType>($"Assets/AirCity/Vehicles/{model}/{model}.vehicle");
                    if (prefab == null || type == null) throw new Exception("Missing deicing build asset: " + model);
                    var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    foreach (var group in copy.GetComponentsInChildren<LODGroup>()) group.ForceLOD(0);
                    foreach (var lights in copy.GetComponentsInChildren<VehicleLights>()) { lights.enabled = false; }
                    var target = type.bounds.center;
                    camera.transform.position = target + new Vector3(-1.2f, .9f, 1.4f).normalized * 65;
                    camera.transform.LookAt(target);
                    float projectedH = 0, projectedW = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = type.bounds.center + Vector3.Scale(type.bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var local = camera.transform.InverseTransformPoint(corner);
                        projectedH = Mathf.Max(projectedH, Mathf.Abs(local.y)); projectedW = Mathf.Max(projectedW, Mathf.Abs(local.x));
                    }
                    camera.orthographicSize = Mathf.Max(projectedH, projectedW / 1.7f) * 1.08f;
                    SceneCapture.Capture(camera, folder + "/" + model + ".png", 510, 300);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
                AssetDatabase.Refresh();
                foreach (string model in new[] { "DeicingTank", "DeicingTruck", "DeicingRecycler" })
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(folder + "/" + model + ".png");
                    importer.textureType = TextureImporterType.Default; importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                if (restore) { EditorSceneManager.CloseScene(scene, true); EditorSceneManager.SetActiveScene(previous); }
            }
        }

        public static void FromCommandLine()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Build(); Debug.Log("Deicing build portraits generated."); EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
