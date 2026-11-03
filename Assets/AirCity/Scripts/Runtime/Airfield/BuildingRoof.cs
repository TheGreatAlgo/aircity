using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield
{
    public enum RoofForm { Flat, Wave, Scalloped }

    /// <summary>A bounded roof profile in the building's own frame. Floor and boarding elevations stay flat.</summary>
    [Serializable]
    public sealed class BuildingRoof
    {
        public RoofForm form;
        [Range(0f, 12f)] public float rise = 3f;
        [Range(0f, 8f)] public float overhang;
        public bool across;
        public float MaxRise => form == RoofForm.Flat ? 0f : rise;
        public BuildingRoof Clone() => (BuildingRoof)MemberwiseClone();
        internal bool Valid => Enum.IsDefined(typeof(RoofForm), form) && BuildingFacade.Finite(rise) && rise >= 0f && rise <= 12f &&
            BuildingFacade.Finite(overhang) && overhang >= 0f && overhang <= 8f;

        public Profile For(Terminal terminal) => new Profile(this, terminal.corners);

        public sealed class Profile
        {
            readonly BuildingRoof roof;
            public readonly Vector2 axis;
            public readonly float start, pitch, step;
            public Profile(BuildingRoof roof, IList<Vector2> corners)
            {
                this.roof = roof;
                Vector2 direction = Vector2.right; float longest = 0f;
                for (int i = 0; i < corners.Count; i++)
                {
                    var edge = corners[(i + 1) % corners.Count] - corners[i];
                    if (edge.sqrMagnitude > longest) { longest = edge.sqrMagnitude; direction = edge.normalized; }
                }
                if (direction.x < -0.001f || (Mathf.Abs(direction.x) <= 0.001f && direction.y < 0f)) direction = -direction;
                axis = roof.across ? new Vector2(-direction.y, direction.x) : direction;
                float min = float.MaxValue, max = float.MinValue;
                foreach (var p in corners) { float s = Vector2.Dot(p, axis); min = Mathf.Min(min, s); max = Mathf.Max(max, s); }
                start = min;
                float length = Mathf.Max(1f, max - min);
                int bays = Mathf.Max(1, Mathf.RoundToInt(length / (roof.form == RoofForm.Scalloped ? 18f : 70f)));
                pitch = length / bays; step = pitch / 24f;
            }
            public float Station(Vector2 p) => Vector2.Dot(p, axis) - start;
            internal int SmoothingGroup(float station) => roof.form == RoofForm.Scalloped ? Mathf.FloorToInt(station / pitch) : 0;
            internal float MaxSlope => roof.MaxRise * (roof.form == RoofForm.Scalloped ? 4f : Mathf.PI) / pitch;
            internal float SurfaceHeight(Vector2 ground, Vector2 lean, float baseHeight)
            {
                if (roof.MaxRise == 0f) return baseHeight;
                if (lean.sqrMagnitude < 0.000001f) return baseHeight + Lift(ground);
                float low = baseHeight, high = baseHeight + roof.MaxRise;
                for (int i = 0; i < 24; i++)
                {
                    float mid = (low + high) * 0.5f;
                    if (mid < baseHeight + Lift(ground + lean * mid)) low = mid; else high = mid;
                }
                return (low + high) * 0.5f;
            }
            internal Vector3 Normal(Vector2 p, int bay)
            {
                float u = roof.form == RoofForm.Scalloped ? Mathf.Clamp01(Station(p) / pitch - bay) : Mathf.Repeat(Station(p) / pitch, 1f);
                float slope = roof.rise / pitch * (roof.form == RoofForm.Scalloped ? 4f * (2f * u - 1f) : Mathf.PI * Mathf.Sin(u * Mathf.PI * 2f));
                return new Vector3(-axis.x * slope, 1f, -axis.y * slope).normalized;
            }
            public float Lift(Vector2 p)
            {
                if (roof.MaxRise == 0f) return 0f;
                float u = Mathf.Repeat(Station(p) / pitch, 1f);
                return roof.rise * (roof.form == RoofForm.Scalloped ? Mathf.Pow(2f * u - 1f, 2f) : (1f - Mathf.Cos(u * Mathf.PI * 2f)) * 0.5f);
            }

            // Roof cuts and wall cuts share the same stations, including on long straight facades.
            internal List<Vector2> Sample(IList<Vector2> outline, IList<int> sourceFaces, out List<int> faces)
            {
                var points = new List<Vector2>(); faces = new List<int>();
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i]; var b = outline[(i + 1) % outline.Count];
                    points.Add(a); faces.Add(sourceFaces[i]);
                    float s0 = Station(a), s1 = Station(b);
                    if (Mathf.Abs(s1 - s0) < 0.001f) continue;
                    var cuts = new List<float>();
                    for (int k = Mathf.FloorToInt(Mathf.Min(s0, s1) / step) + 1; k * step < Mathf.Max(s0, s1) - 0.0001f; k++)
                    {
                        float u = (k * step - s0) / (s1 - s0);
                        if (u > 0.0001f && u < 0.9999f) cuts.Add(u);
                    }
                    cuts.Sort();
                    foreach (float u in cuts) { points.Add(Vector2.Lerp(a, b, u)); faces.Add(sourceFaces[i]); }
                }
                return points;
            }
        }
    }
}
