using UnityEditor;
using UnityEngine;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>
    /// Import settings for the generated ground textures (ArtSource/Airfield/textures.py):
    ///  - *_Albedo: sRGB; *_Normal: normal maps; *_Mask, Macro: linear data;
    ///  - RunwayGlyphs: a one-channel distance field, uncompressed and clamped;
    ///  - ground textures are seen at grazing angles over kilometres: trilinear, high anisotropy.
    /// </summary>
    public class AirfieldTexturePostprocessor : AssetPostprocessor
    {
        public const string Folder = "Assets/AirCity/Airfield/Textures/";

        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 16;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.wrapMode = TextureWrapMode.Repeat;
            if (file == "RunwayGlyphs")
            {
                ti.textureType = TextureImporterType.SingleChannel;
                ti.sRGBTexture = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.anisoLevel = 4;
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                ti.SetTextureSettings(settings);
            }
            else if (file.EndsWith("_Normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = file.EndsWith("_Albedo");
            }
        }
    }
}
