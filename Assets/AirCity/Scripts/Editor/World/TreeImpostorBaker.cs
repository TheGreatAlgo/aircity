using System.Collections.Generic;
using System.IO;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.World
{
    /// <summary>Bakes the real high-detail models into front/top distance impostors, retaining each species' crown.</summary>
    public static class TreeImpostorBaker
    {
        const int Tile = 512, Columns = 4, Rows = 3;
        public const string Path = WorldAssetSetup.Root + "/Vegetation/Textures/ForestImpostors.png";

        public static Texture2D Bake(TreeLibrary library)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Tree impostor bake") { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
            SceneManager.MoveGameObjectToScene(root, scene);
            var filter = root.AddComponent<MeshFilter>(); var renderer = root.AddComponent<MeshRenderer>();
            var wood = new Material(library.bark); var leaves = new Material(library.foliage);
            foreach (var mat in new[] { wood, leaves }) { mat.SetFloat("_BakeMode", 1f); mat.SetFloat("_Wind", 0f); }
            renderer.sharedMaterials = new[] { wood, leaves };
            var cameraObject = new GameObject("Tree atlas camera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false; camera.cullingMask = 1 << 31; camera.orthographic = true; camera.orthographicSize = 13f;
            camera.aspect = 1f; camera.nearClipPlane = .1f; camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; camera.allowHDR = false;
            var rt = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var tile = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false);
            var atlas = new Texture2D(Tile * Columns, Tile * Rows, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                for (int variant = 0; variant < library.variants.Length; variant++)
                {
                    filter.sharedMesh = library.variants[variant].levels[0];
                    for (int view = 0; view < 2; view++)
                    {
                        if (view == 0) camera.transform.SetPositionAndRotation(new Vector3(0f, 13f, -45f), Quaternion.identity);
                        else camera.transform.SetPositionAndRotation(new Vector3(0f, 45f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                        camera.Render(); camera.Render();
                        RenderTexture.active = rt; tile.ReadPixels(new Rect(0f, 0f, Tile, Tile), 0, 0); tile.Apply();
                        int id = variant * 2 + view;
                        atlas.SetPixels32(id % Columns * Tile, (Rows - 1 - id / Columns) * Tile, Tile, Tile, tile.GetPixels32());
                    }
                }
                atlas.Apply(); File.WriteAllBytes(Path, atlas.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(wood); Object.DestroyImmediate(leaves); Object.DestroyImmediate(tile); Object.DestroyImmediate(atlas);
                rt.Release(); Object.DestroyImmediate(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
            importer.alphaIsTransparency = true; importer.mipmapEnabled = true; importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = .3f; importer.maxTextureSize = 2048; importer.npotScale = TextureImporterNPOTScale.None;
            importer.anisoLevel = 4; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
        }

        public static Mesh Mesh(int variant)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var colors = new List<Color>(); var triangles = new List<int>();
            void Card(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int tile)
            {
                int first = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
                for (int i = 0; i < 4; i++) { normals.Add(Vector3.up); colors.Add(Color.white); }
                float x = tile % Columns / (float)Columns, y = (Rows - 1 - tile / Columns) / (float)Rows;
                float left = x + .002f, right = x + 1f / Columns - .002f, bottom = y + .002f, top = y + 1f / Rows - .002f;
                uv.AddRange(new[] { new Vector2(left, bottom), new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom) });
                triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }
            Card(new Vector3(-13f, 0f, 0f), new Vector3(-13f, 26f, 0f), new Vector3(13f, 26f, 0f), new Vector3(13f, 0f, 0f), variant * 2);
            Card(new Vector3(0f, 0f, -13f), new Vector3(0f, 26f, -13f), new Vector3(0f, 26f, 13f), new Vector3(0f, 0f, 13f), variant * 2);
            float crown = variant >= 3 ? 18f : 12f;
            Card(new Vector3(-13f, crown, -13f), new Vector3(-13f, crown, 13f), new Vector3(13f, crown, 13f), new Vector3(13f, crown, -13f), variant * 2 + 1);
            var mesh = new Mesh { name = "Tree " + variant + " distance impostor", subMeshCount = 2 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetColors(colors);
            mesh.SetTriangles(System.Array.Empty<int>(), 0); mesh.SetTriangles(triangles, 1); mesh.RecalculateBounds(); return mesh;
        }
    }
}
