using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.World
{
    /// <summary>A cached GPU-rendered NASA globe. Input and pin placement use GlobeProjection in the caller.</summary>
    public sealed class RegionGlobe : IDisposable
    {
        Material material;
        RenderTexture texture;
        Texture2D earth;
        float lastLatitude, lastLongitude, lastZoom;
        bool drawn;

        /// <summary>Fits a centred square in rect; radius is half its shorter side. Zoom scales that sphere.</summary>
        public void Draw(Rect rect, float viewLatitude, float viewLongitude, float zoom = 1f)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || rect.width <= 0f || rect.height <= 0f) return;
            if (float.IsNaN(viewLatitude) || float.IsInfinity(viewLatitude) || float.IsNaN(viewLongitude) ||
                float.IsInfinity(viewLongitude) || float.IsNaN(zoom) || float.IsInfinity(zoom)) return;
            float size = Mathf.Min(rect.width, rect.height);
            var square = new Rect(rect.center.x - size * .5f, rect.center.y - size * .5f, size, size);
            Ensure(size > 512f ? 768 : 512);
            if (material == null || texture == null) return;
            float latitude = Mathf.Clamp(viewLatitude, -90f, 90f), longitude = (float)GlobeProjection.WrapLongitude(viewLongitude);
            zoom = Mathf.Max(.05f, zoom);
            if (!drawn || latitude != lastLatitude || longitude != lastLongitude || zoom != lastZoom)
            {
                material.SetFloat("_ViewLatitude", latitude); material.SetFloat("_ViewLongitude", longitude); material.SetFloat("_Zoom", zoom);
                var previous = RenderTexture.active;
                try { Graphics.Blit(Texture2D.blackTexture, texture, material, 0); }
                finally { RenderTexture.active = previous; }
                lastLatitude = latitude; lastLongitude = longitude; lastZoom = zoom; drawn = true;
            }
            var colour = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(square, texture, ScaleMode.StretchToFill, true);
            GUI.color = colour;
        }

        void Ensure(int resolution)
        {
            if (material == null)
            {
                earth = Resources.Load<Texture2D>("AirCityRegions/Earth");
                var shader = Resources.Load<Shader>("AirCityRegions/Globe");
                if (shader == null || earth == null) return;
                material = new Material(shader) { name = "Region picker globe", hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture("_EarthMap", earth); drawn = false;
            }
            if (texture != null && texture.width == resolution && texture.IsCreated()) return;
            ReleaseTexture();
            texture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                name = "Region picker globe", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false };
            texture.Create(); drawn = false;
        }

        public void Dispose()
        {
            ReleaseTexture(); Release(material); material = null; earth = null; drawn = false;
        }

        void ReleaseTexture()
        {
            if (texture == null) return;
            if (RenderTexture.active == texture) RenderTexture.active = null;
            texture.Release(); Release(texture); texture = null;
        }
        static void Release(Object value)
        { if (value == null) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
