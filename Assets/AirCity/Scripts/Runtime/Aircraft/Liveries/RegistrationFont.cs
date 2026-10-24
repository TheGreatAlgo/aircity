using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>
    /// Glyph atlas for registration letters, shared by all aircraft types. Generated with the
    /// aircraft (RegistrationFont.regfont) and imported by RegistrationFontImporter.
    /// Coordinates are in pixels of the atlas as authored (atlasSize²), y up.
    /// </summary>
    public class RegistrationFont : ScriptableObject
    {
        [Tooltip("Alpha = glyph coverage.")]
        public Texture2D atlas;
        public float atlasSize = 1024, cell = 128, baseline = 26, capTop = 101;
        public string chars = "";
        public float[] cellX, cellY, inkLeft, inkRight;

        public int GlyphIndex(char c) => chars.IndexOf(char.ToUpperInvariant(c));
    }
}
