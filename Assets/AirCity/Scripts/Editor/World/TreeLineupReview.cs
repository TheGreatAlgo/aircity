using System;
using System.Collections.Generic;
using System.IO;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.World
{
    /// <summary>Every tree of a region's palette side by side, one row per detail level, seen close and from a
    /// city-builder camera. Batch entry: <c>-executeMethod AirCity.EditorTools.World.TreeLineupReview.FromCommandLine</c>;
    /// writes small JPEGs under ignored <c>Captures/TreeLineup</c>.</summary>
    public static class TreeLineupReview
    {
        const string Root = "Captures/TreeLineup/";
        const float Spacing = 17f;

        public static void FromCommandLine()
        {
            try
            {
                Directory.CreateDirectory(Root);
                var library = AssetDatabase.LoadAssetAtPath<TreeLibrary>(WorldAssetSetup.Root + "/Vegetation/ForestTrees.asset");
                if (library == null) throw new InvalidOperationException("The tree library is missing.");
                foreach (var preset in new[] { RegionPreset.Woodland, RegionPreset.Tropical })
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var camera = CameraAndLight();
                    library = AssetDatabase.LoadAssetAtPath<TreeLibrary>(WorldAssetSetup.Root + "/Vegetation/ForestTrees.asset");
                    Debug.Log($"Tree lineup {preset}: library {(library == null ? "null" : library.variants?.Length.ToString())} variants");
                    using var palette = RegionTreePalette.Create(library, preset);
                    if (palette == null || palette.Library == null) throw new InvalidOperationException("No palette for " + preset);
                    var variants = palette.Library.variants;
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * 80f;
                    var ground = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.30f, .31f, .19f) };
                    ground.SetFloat("_Smoothness", 0f); floor.GetComponent<Renderer>().sharedMaterial = ground;
                    float width = (variants.Length - 1) * Spacing;
                    for (int level = 0; level < RegionTreePalette.Levels; level++)
                    for (int i = 0; i < variants.Length; i++)
                        Place(variants[i], level, new Vector3(i * Spacing - width * .5f, 0f, level * 34f), 20f * i);
                    // A small stand, as the forest renderer mixes them: near detail in front, cards behind.
                    var random = new System.Random(7);
                    for (int i = 0; i < 260; i++)
                    {
                        var p = new Vector3((float)random.NextDouble() * 150f - 75f, 0f, 190f + (float)random.NextDouble() * 150f);
                        var variant = variants[random.Next(variants.Length)];
                        Place(variant, p.z < 225f ? 0 : p.z < 260f ? 1 : p.z < 300f ? 2 : 3, p, (float)random.NextDouble() * 360f,
                            Mathf.Lerp(.75f, 1.15f, (float)random.NextDouble()));
                    }
                    Shot(camera, new Vector3(0f, 9f, -width * .62f - 12f), new Vector3(0f, 12f, 0f), preset + "-near-front.jpg");
                    Shot(camera, new Vector3(-width * .25f, 26f, -34f), new Vector3(-width * .25f, 9f, 0f), preset + "-near-left.jpg", 30f);
                    Shot(camera, new Vector3(width * .25f, 26f, -34f), new Vector3(width * .25f, 9f, 0f), preset + "-near-right.jpg", 30f);
                    Shot(camera, new Vector3(0f, 150f, -150f), new Vector3(0f, 0f, 30f), preset + "-levels-oblique.jpg");
                    Shot(camera, new Vector3(0f, 120f, 80f), new Vector3(0f, 0f, 265f), preset + "-stand.jpg");
                    Shot(camera, new Vector3(0f, 420f, -220f), new Vector3(0f, 0f, 265f), preset + "-stand-far.jpg");
                    var text = new System.Text.StringBuilder();
                    foreach (var variant in variants)
                    {
                        text.Append(variant.name).Append(':');
                        foreach (var mesh in variant.levels) text.Append(' ').Append(mesh.GetIndexCount(0) / 3).Append('+').Append(mesh.GetIndexCount(1) / 3);
                        text.AppendLine();
                    }
                    File.WriteAllText(Root + preset + "-triangles.txt", text.ToString());
                }
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Place(TreeVariant variant, int level, Vector3 position, float yaw, float scale = 1f)
        {
            var go = new GameObject(variant.name + " level " + level);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f)); go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = variant.levels[level];
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { variant.bark, variant.foliage };
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        static Camera CameraAndLight()
        {
            var go = new GameObject("Main Camera"); go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>(); camera.fieldOfView = 40f; camera.nearClipPlane = .3f; camera.farClipPlane = 5000f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.65f, .75f, .80f);
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, .98f, .94f);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .85f; sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f); RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = new Color(.45f, .53f, .60f);
            RenderSettings.ambientEquatorColor = new Color(.25f, .29f, .24f); RenderSettings.ambientGroundColor = new Color(.15f, .18f, .14f);
            RenderSettings.fog = false; return camera;
        }

        static void Shot(Camera camera, Vector3 position, Vector3 target, string file, float fieldOfView = 40f)
        {
            const int Width = 1400, Height = 700;
            camera.fieldOfView = fieldOfView;
            camera.transform.position = position; camera.transform.LookAt(target);
            AirCity.Rendering.ShadowRange.Follow(camera, target);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            camera.targetTexture = rt; camera.Render(); camera.Render(); camera.targetTexture = null;
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Root + file, image.EncodeToJPG(82));
            Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
            AirCity.Rendering.ShadowRange.Unfollow(camera);
        }
    }
}
