using UnityEditor;
using UnityEngine;

namespace AirCity.EditorTools.World
{
    public class WorldTexturePostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 1;
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/AirCity/World/Textures/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 16;
            importer.maxTextureSize = 1024;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (assetPath.EndsWith("_Normal.png")) importer.textureType = TextureImporterType.NormalMap;
            else importer.sRGBTexture = assetPath.EndsWith("_Albedo.png");
        }
    }
}
