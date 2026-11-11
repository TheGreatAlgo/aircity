using System;
using AirCity.Aircraft;
using AirCity.EditorTools.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Aircraft
{
    /// <summary>
    /// Captures every afterburning type with its flames lit (full reheat), by day and by night, from behind, the side
    /// and above (the game camera), plus a half-lit frame, without modifying any saved scene. Output:
    /// Captures/Afterburner/&lt;Model&gt;-&lt;view&gt;-{day,night}.png.
    ///   Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AfterburnerReview.FromCommandLine
    /// </summary>
    public static class AfterburnerReview
    {
        public static void FromCommandLine()
        {
            try
            {
                AircraftSetup.CreateMissingPrefabsAndLiveries();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var sun = AircraftSetup.SetUpDaylight();
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.localScale = new Vector3(40f, 1f, 40f);
                ground.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/AirCity/Materials/Apron.mat");
                ground.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
                camera.fieldOfView = 40f;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 2000f;
                var dayAmbient = (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor);
                float dayIntensity = sun.intensity;
                var dayColor = sun.color;
                foreach (var type in AircraftSetup.AllTypes())
                {
                    if (!type.HasAfterburner) continue;
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(AircraftSetup.Prefab(type));
                    try
                    {
                        var reheat = root.GetComponent<AircraftAfterburner>();
                        var lights = root.GetComponent<AircraftLights>();
                        lights.beacon = lights.strobes = lights.landing = true;
                        lights.UpdateState();
                        var tail = AircraftAfterburner.GlowPosition(type);
                        float L = type.Length, S = type.Wingspan;
                        var views = new (string name, Vector3 eye, Vector3 at)[]
                        {
                            ("rear34", new Vector3(S * 0.9f, 5f, -L * 0.75f), new Vector3(0f, 3.5f, -L * 0.1f)),
                            ("side", new Vector3(-L * 0.95f, 4f, -L * 0.12f), new Vector3(0f, 3.5f, -L * 0.12f)),
                            ("close", new Vector3(-S * 0.55f, 3.2f, tail.z - 18f), tail + new Vector3(0f, 0f, 4f)),
                            ("game", new Vector3(-L * 0.9f, L * 0.85f, -L * 1.1f), new Vector3(0f, 2f, -L * 0.2f)),
                        };
                        foreach (bool night in new[] { false, true })
                        {
                            AircraftLights.Glow = night ? 1f : 0f;
                            sun.intensity = night ? 0.08f : dayIntensity;
                            sun.color = night ? new Color(0.55f, 0.65f, 1f) : dayColor;
                            (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor) = night
                                ? (new Color(0.08f, 0.10f, 0.18f), new Color(0.06f, 0.07f, 0.10f), new Color(0.03f, 0.03f, 0.03f))
                                : dayAmbient;
                            camera.clearFlags = night ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
                            camera.backgroundColor = new Color(0.02f, 0.025f, 0.05f);
                            foreach (var (name, eye, at) in views)
                            {
                                camera.transform.position = eye;
                                camera.transform.LookAt(at);
                                foreach (float level in name == "close" && !night ? new[] { 0.5f, 1f } : new[] { 1f })
                                {
                                    reheat.reheat = level;
                                    reheat.UpdateState(10f);
                                    string suffix = level < 1f ? "-half" : "";
                                    SceneCapture.Capture(camera, $"Captures/Afterburner/{type.model}-{name}{suffix}-{(night ? "night" : "day")}.png", 1280, 760);
                                }
                            }
                        }
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                AircraftLights.Glow = 0f;
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
