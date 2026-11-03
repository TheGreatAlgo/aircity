using UnityEditor;
using UnityEngine;

namespace AirCity.EditorTools.Dev
{
    /// <summary>
    /// Import settings for the HUD's icons (ArtSource/UI/icons.py): white on clear, drawn tinted by IMGUI at many
    /// sizes, so uncompressed with mipmaps (trilinear) and transparent edges dilated.
    /// </summary>
    public class UiIconPostprocessor : AssetPostprocessor
    {
        public const string Folder = "Assets/AirCity/UI/";

        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 256;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
