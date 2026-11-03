using System;
using AirCity.Aircraft;
using AirCity.EditorTools.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Aircraft
{
    /// <summary>Close-up closed/open review of every generated cargo hatch, without modifying saved scenes.</summary>
    public static class CargoDoorReview
    {
        public static void FromCommandLine()
        {
            try
            {
                AircraftSetup.CreateMissingPrefabsAndLiveries();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AircraftSetup.SetUpDaylight();
                var camera = new GameObject("Cargo review camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.19f, 0.23f, 0.29f);
                camera.fieldOfView = 36f;
                camera.nearClipPlane = 0.05f;
                var args = Environment.GetCommandLineArgs();
                int modelArgument = Array.IndexOf(args, "-cargoModel");
                string model = modelArgument < 0 ? null : args[modelArgument + 1];
                foreach (var type in AircraftSetup.AllTypes())
                {
                    if (model != null && type.model != model) continue;
                    var root = Object.Instantiate(AircraftSetup.Prefab(type));
                    try
                    {
                        var group = root.GetComponent<LODGroup>();
                        group.animateCrossFading = false;
                        group.ForceLOD(0);
                        var rig = root.GetComponent<AircraftCargoDoors>();
                        foreach (var door in type.cargoDoors)
                        {
                            if (!type.TryGetPoint(door.point, out var point)) throw new Exception("missing cargo point");
                            var target = point.position + Vector3.up * point.size.y * 0.6f;
                            float distance = Mathf.Max(point.size.x, point.size.y) * 3.6f;
                            camera.transform.position = target + point.normal * distance + Vector3.up * distance * 0.2f + Vector3.forward * distance * 0.25f;
                            camera.transform.LookAt(target);
                            string path = $"Captures/CargoDoors/{type.model}-{door.point}";
                            SceneCapture.Capture(camera, path + "-closed.png", 1200, 900);
                            if (!rig.Operate(door.point, true, 100f, out var reason)) throw new Exception(reason);
                            SceneCapture.Capture(camera, path + "-open.png", 1200, 900);
                            group.ForceLOD(1);
                            SceneCapture.Capture(camera, path + "-open-lod1.png", 1200, 900);
                            group.ForceLOD(0);
                            rig.Operate(door.point, false, 100f, out _);
                        }
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
