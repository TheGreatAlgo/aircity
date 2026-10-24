using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Dev
{
    /// <summary>
    /// Coloured lines just above the ground for the dev tools (hover and selection outlines, the taxiway guide):
    /// pooled LineRenderers, sized in screen pixels so they read at any zoom. Each frame: Begin, then Line / Loop
    /// for everything to show, then End hides the rest.
    /// </summary>
    sealed class DevOutlines
    {
        static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        readonly List<LineRenderer> pool = new List<LineRenderer>();
        MaterialPropertyBlock props;   // made on first use: not allowed while a MonoBehaviour is being constructed
        Material material;
        int used;

        public void Begin() => used = 0;

        /// <summary>Points in world space; width in pixels at the camera's distance from the first point.</summary>
        public void Line(Camera cam, IReadOnlyList<Vector3> points, Color color, float pixels, bool loop = false)
        {
            if (points.Count < 2) return;
            var lr = Next();
            lr.loop = loop;
            lr.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) lr.SetPosition(i, points[i] + Vector3.up * 0.3f);
            float dist = Vector3.Distance(cam.transform.position, points[0]);
            lr.widthMultiplier = pixels * 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(Screen.height, 1);
            props ??= new MaterialPropertyBlock();
            props.SetColor(ColorId, color);
            lr.SetPropertyBlock(props);
            lr.enabled = true;
        }

        public void Loop(Camera cam, IReadOnlyList<Vector3> points, Color color, float pixels) => Line(cam, points, color, pixels, true);

        public void End()
        {
            for (int i = used; i < pool.Count; i++)
                if (pool[i] != null) pool[i].enabled = false;
        }

        public void Destroy()
        {
            foreach (var lr in pool)
                if (lr != null) Object.Destroy(lr.gameObject);
            pool.Clear();
            if (material != null) Object.Destroy(material);
        }

        LineRenderer Next()
        {
            if (used < pool.Count && pool[used] != null) return pool[used++];
            material ??= new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.DontSave };
            var lr = new GameObject("Dev outline") { hideFlags = HideFlags.DontSave }.AddComponent<LineRenderer>();
            lr.sharedMaterial = material;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCornerVertices = 2;
            if (used < pool.Count) pool[used] = lr;
            else pool.Add(lr);
            used++;
            return lr;
        }
    }
}
