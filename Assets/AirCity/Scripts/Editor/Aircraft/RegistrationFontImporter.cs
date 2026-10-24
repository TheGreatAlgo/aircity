using System;
using System.IO;
using AirCity.Aircraft;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace AirCity.EditorTools.Aircraft
{
    /// <summary>Imports RegistrationFont.regfont (glyph metrics written by the aircraft generator)
    /// as a <see cref="RegistrationFont"/> that points at its atlas texture.</summary>
    [ScriptedImporter(1, "regfont")]
    public class RegistrationFontImporter : ScriptedImporter
    {
#pragma warning disable 0649   // assigned by JsonUtility
        [Serializable]
        class FontJson
        {
            public string atlas, chars;
            public float atlasSize, cell, baseline, capTop;
            public float[] cellX, cellY, inkLeft, inkRight;
        }
#pragma warning restore 0649

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var json = JsonUtility.FromJson<FontJson>(File.ReadAllText(ctx.assetPath));
            var f = ScriptableObject.CreateInstance<RegistrationFont>();
            f.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            f.atlas = AircraftTypeImporter.Load<Texture2D>(ctx, json.atlas);
            f.atlasSize = json.atlasSize;
            f.cell = json.cell;
            f.baseline = json.baseline;
            f.capTop = json.capTop;
            f.chars = json.chars ?? "";
            f.cellX = json.cellX;
            f.cellY = json.cellY;
            f.inkLeft = json.inkLeft;
            f.inkRight = json.inkRight;
            ctx.AddObjectToAsset("font", f);
            ctx.SetMainObject(f);
        }
    }
}
