using System.Collections.Generic;
using System.IO;
using AirCity.EditorTools.Aircraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>
    /// Import settings for generated vehicles and the depot under Assets/AirCity/Vehicles/&lt;Model&gt;/:
    ///  - models: metres, no axis-conversion rotation; every flat-colour submesh is merged into one, drawn with one
    ///    URP Lit material on the baked textures (&lt;Model&gt;_Albedo with the AO and grime, &lt;Model&gt;_MetalGloss);
    ///    a LODGroup from the _LODn name suffixes (as for aircraft); a building's doors (Door_k, kept out of the
    ///    merged far LODs so they can roll up) are drawn at every LOD;
    ///  - textures: albedo sRGB, metal-gloss linear (R metallic, A smoothness).
    /// </summary>
    public class VehicleAssetPostprocessor : AssetPostprocessor
    {
        const string Root = "Assets/AirCity/Vehicles/";

        static bool InVehicles(string path) => path.Replace('\\', '/').StartsWith(Root);

        public override uint GetVersion() => 2;

        void OnPreprocessTexture()
        {
            if (!InVehicles(assetPath) || !assetPath.Contains("/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            ti.maxTextureSize = 2048;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.anisoLevel = 4;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.sRGBTexture = !file.EndsWith("_MetalGloss");
        }

        void OnPreprocessModel()
        {
            if (!InVehicles(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importNormals = ModelImporterNormals.Import;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.isReadable = true;   // OnPostprocessModel merges submeshes, then drops the CPU copies itself
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!InVehicles(assetPath)) return;
            string model = Path.GetFileNameWithoutExtension(assetPath);
            var mat = CreateMaterial(model);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) MergeSubmeshes(mf.sharedMesh);
                r.sharedMaterials = new[] { mat };
            }
            AircraftAssetPostprocessor.BuildLodGroup(root);
            AddDoorsToEveryLod(root);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) mf.sharedMesh.UploadMeshData(true);
        }

        /// <summary>Doors are separate at every distance (the generator doesn't merge them into the far LODs).</summary>
        static void AddDoorsToEveryLod(GameObject root)
        {
            var group = root.GetComponent<LODGroup>();
            if (group == null) return;
            var doors = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Door_")) doors.Add(r);
            if (doors.Count == 0) return;
            var lods = group.GetLODs();
            for (int i = 1; i < lods.Length; i++)
            {
                var list = new List<Renderer>(lods[i].renderers);
                foreach (var d in doors)
                    if (!list.Contains(d)) list.Add(d);
                lods[i].renderers = list.ToArray();
            }
            group.SetLODs(lods);
        }

        Material CreateMaterial(string model)
        {
            var shader = AircraftAssetPostprocessor.LitShader();
            var mat = new Material(shader != null ? shader : Shader.Find("Standard")) { name = model };
            var albedo = ModelTexture(model, "Albedo");
            var metalGloss = ModelTexture(model, "MetalGloss");
            foreach (var p in new[] { "_BaseMap", "_MainTex" })
                if (mat.HasProperty(p)) mat.SetTexture(p, albedo);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (metalGloss != null && mat.HasProperty("_MetallicGlossMap"))
            {
                mat.SetTexture("_MetallicGlossMap", metalGloss);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1f);
            }
            context.AddObjectToAsset(model + " material", mat);
            return mat;
        }

        Texture2D ModelTexture(string model, string suffix)
        {
            string dir = Path.GetDirectoryName(Path.GetDirectoryName(assetPath)).Replace('\\', '/');
            string texPath = $"{dir}/Textures/{model}_{suffix}.png";
            context.DependsOnArtifact(texPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        }

        static void MergeSubmeshes(Mesh mesh)
        {
            if (mesh.subMeshCount <= 1) return;
            if (!mesh.isReadable)
            {
                Debug.LogError($"{mesh.name}: mesh not readable during import, submeshes left unmerged");
                return;
            }
            var tris = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++) tris.AddRange(mesh.GetTriangles(s));
            mesh.subMeshCount = 1;
            mesh.indexFormat = mesh.vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetTriangles(tris, 0, true);
        }
    }
}
