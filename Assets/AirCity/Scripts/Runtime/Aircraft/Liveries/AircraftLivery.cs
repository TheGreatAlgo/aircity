using System;
using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>
    /// Paint layouts every aircraft type implements with its own masks (see
    /// <see cref="AircraftType"/>), so one airline livery fits its whole fleet.
    /// </summary>
    public enum LiveryStyle
    {
        Classic = 0,   // cheat stripe + pinstripe sweeping up the fin
        Swoosh = 1,    // tail swoosh
        TwoTone = 2,   // belly colour, cheatline, painted tail (airline look)
        Trainer = 3,   // painted nose, coloured tips, thin window stripe
        Custom = 4,    // customMask and/or customAlbedo
    }

    /// <summary>
    /// Livery data, independent of the aircraft type. Build it in code from game data or keep
    /// it in an <see cref="AircraftLiveryAsset"/>.
    ///
    /// Zone colours paint over the base colour in order 1, 2, 3 (style masks: R = zone 1,
    /// G = zone 2, B = zone 3). Baked ambient occlusion and panel lines are multiplied on top.
    /// </summary>
    [Serializable]
    public class AircraftLivery
    {
        public LiveryStyle style = LiveryStyle.Classic;

        [Tooltip("Paint that covers everything not in a zone.")]
        public Color baseColor = new Color32(244, 244, 241, 255);
        [Tooltip("Zone 1 (mask red): main stripe / swoosh / belly / nose.")]
        public Color color1 = new Color32(27, 42, 92, 255);
        [Tooltip("Zone 2 (mask green): pinstripe / accent / cheatline / tips.")]
        public Color color2 = new Color32(201, 161, 58, 255);
        [Tooltip("Zone 3 (mask blue): trim - spinner, wheel pants, tips, tail.")]
        public Color color3 = new Color32(27, 42, 92, 255);

        [Tooltip("Registration letters colour.")]
        public Color registrationColor = new Color32(27, 42, 92, 255);
        [Tooltip("Used when the aircraft itself doesn't specify one. Empty = no letters.")]
        public string defaultRegistration = "";

        [Range(0f, 1f)] public float paintSmoothness = 0.55f;

        [Tooltip("Tail logo (RGBA, transparent background). Fitted into each aircraft type's fin logo area, so " +
                 "it works on every type. Square-ish logos read best.")]
        public Texture2D logo;

        [Header("Custom paint (Custom style)")]
        [Tooltip("RGB zone mask painted on the model's UV template. Model-specific.")]
        public Texture2D customMask;
        [Tooltip("Fully hand-painted livery in flat colours on the model's UV template; replaces the colours. " +
                 "Shading is still multiplied in. Model-specific.")]
        public Texture2D customAlbedo;

        public AircraftLivery Clone() => (AircraftLivery)MemberwiseClone();

        /// <summary>Liveries with equal content share one generated texture/material per aircraft type.</summary>
        public string CacheKey()
        {
            return string.Join("|",
                (int)style,
                ColorUtility.ToHtmlStringRGB(baseColor), ColorUtility.ToHtmlStringRGB(color1),
                ColorUtility.ToHtmlStringRGB(color2), ColorUtility.ToHtmlStringRGB(color3),
                logo != null ? logo.GetHashCode() : 0,
                customMask != null ? customMask.GetHashCode() : 0,
                customAlbedo != null ? customAlbedo.GetHashCode() : 0);
        }
    }
}
