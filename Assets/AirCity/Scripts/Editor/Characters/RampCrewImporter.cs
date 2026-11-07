using System.IO;
using AirCity.Characters;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace AirCity.EditorTools.Characters
{
    [ScriptedImporter(1, "crew")]
    public sealed class RampCrewImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var type = ScriptableObject.CreateInstance<RampCrewType>();
            JsonUtility.FromJsonOverwrite(File.ReadAllText(ctx.assetPath), type);
            ctx.AddObjectToAsset("crew", type); ctx.SetMainObject(type);
        }
    }

    public sealed class RampCrewAssetPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 2;
        void OnPreprocessModel()
        {
            if (!assetPath.Contains("/Characters/RampCrew/Models/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.bakeAxisConversion = true;
            importer.isReadable = true; importer.optimizeGameObjects = false;
        }
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Characters/RampCrew/Textures/")) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.Contains("_Normal");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true; importer.anisoLevel = 2;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = !normal && !assetPath.Contains("MetalGloss");
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
        }
    }
}
