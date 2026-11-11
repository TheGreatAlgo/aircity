using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Aircraft
{
    /// <summary>
    /// Afterburner flames out of the type's afterburning nozzles (AircraftType.exhausts), lit for the takeoff.
    ///  - One shared flame mesh per type (an inner core with shock diamonds, an outer plume and a hot disc in each
    ///    nozzle exit) drawn additively by AirCity/AircraftAfterburner. The vertex shader shapes and flickers the
    ///    flames; how far they are lit goes in the renderer's shader user value, so every aircraft shares the material.
    ///  - A real orange point light behind the nozzles while the camera is close, so the flames light the runway.
    ///
    ///   afterburner.reheat = 1;     // takeoff power: the flames light within `lightTime` seconds
    ///   afterburner.reheat = 0;     // cancelled: they die away
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class AircraftAfterburner : MonoBehaviour
    {
        /// <summary>The generated child that draws the flames (never saved, kept out of the LOD group).</summary>
        public const string RendererName = "AfterburnerFlames";
        /// <summary>The generated child holding the point light behind the nozzles (never saved).</summary>
        public const string GlowName = "Afterburner glow";

        public AircraftType type;
        [Tooltip("AirCity/AircraftAfterburner material (shared by every aircraft).")]
        public Material flameMaterial;

        [Tooltip("Afterburner setting: 0 = off, 1 = full reheat.")]
        [Range(0f, 1f)] public float reheat;
        [Tooltip("Seconds for the flames to light fully, and to die away.")]
        public float lightTime = 0.8f, cancelTime = 0.5f;
        [Tooltip("Real point light behind the nozzles within this camera distance (metres).")]
        public float lightDistance = 350f;
        public float lightIntensity = 10f;

        /// <summary>How lit the flames are now (eases towards `reheat`).</summary>
        public float Level { get; private set; }

        MeshRenderer flames;
        Light glow;
        uint phase;
        float flicker;

        static readonly Dictionary<AircraftType, Mesh> meshes = new Dictionary<AircraftType, Mesh>();
        const int Rings = 24, Segments = 12, DiscRings = 4;
        /// <summary>Flame lengths in exit radii at full reheat (match the shader's _CoreLength / _PlumeLength).</summary>
        public const float PlumeLength = 19f;
        static readonly Color GlowColor = new Color(1f, 0.55f, 0.22f);

        void OnEnable()
        {
            phase = (uint)(Mathf.Abs(gameObject.name.GetHashCode() ^ transform.position.GetHashCode()) % 255);
            Build();
            UpdateState(0f);
        }

        void OnDisable()
        {
            if (flames != null) flames.enabled = false;
            if (glow != null) glow.enabled = false;
        }

        void Build()
        {
            if (type == null || flameMaterial == null || !type.HasAfterburner) return;
            if (flames == null)
            {
                var t = transform.Find(RendererName);
                if (t == null)
                {
                    t = new GameObject(RendererName).transform;
                    t.SetParent(transform, false);
                    // generated each time the aircraft is enabled: never saved into scenes or prefabs
                    t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                }
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
                flames = t.GetComponent<MeshRenderer>();
                if (flames == null) flames = t.gameObject.AddComponent<MeshRenderer>();
                mf.sharedMesh = FlameMesh(type);
                flames.sharedMaterial = flameMaterial;
                flames.shadowCastingMode = ShadowCastingMode.Off;
                flames.receiveShadows = false;
                flames.lightProbeUsage = LightProbeUsage.Off;
                flames.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            if (glow == null)
            {
                // an Instantiate copy (or a domain reload) already has one: reuse it rather than pile up lights
                var t = transform.Find(GlowName);
                if (t == null)
                {
                    t = new GameObject(GlowName) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable }.transform;
                    t.SetParent(transform, false);
                }
                t.localPosition = GlowPosition(type);
                glow = t.GetComponent<Light>();
                if (glow == null) glow = t.gameObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = GlowColor;
                glow.range = Mathf.Max(type.Wingspan * 0.9f, 20f);
                glow.shadows = LightShadows.None;
                glow.enabled = false;
            }
        }

        /// <summary>Behind the middle of the afterburning nozzles, a few metres down the flames.</summary>
        public static Vector3 GlowPosition(AircraftType type)
        {
            Vector3 sum = Vector3.zero, dir = Vector3.zero;
            float r = 0f;
            int n = 0;
            foreach (var e in type.exhausts)
            {
                if (!e.afterburner) continue;
                sum += e.position;
                dir += e.direction;
                r += e.radius;
                n++;
            }
            if (n == 0) return Vector3.zero;
            return sum / n + dir.normalized * (r / n * 4f);
        }

        void LateUpdate() => UpdateState(Application.isPlaying ? Time.deltaTime : 1f);

        /// <summary>Ease the flames towards `reheat` over `dt` seconds and push the state to the GPU (LateUpdate does
        /// this every frame; tests call it directly).</summary>
        public void UpdateState(float dt)
        {
            if (flames == null) Build();
            float target = Mathf.Clamp01(reheat);
            float rate = 1f / Mathf.Max(target > Level ? lightTime : cancelTime, 0.01f);
            Level = Mathf.MoveTowards(Level, target, rate * Mathf.Max(dt, 0f));
            if (flames == null) return;
            bool on = Level > 0.002f && isActiveAndEnabled;
            flames.enabled = on;
            if (on) flames.SetShaderUserValue((uint)Mathf.RoundToInt(Level * 255f) | (phase << 8));
            if (glow == null) return;
            var cam = Camera.main;
            bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < lightDistance * lightDistance;
            glow.enabled = on && near;
            if (glow.enabled)
            {
                // a quick, uneven flicker (real-time: it's only a light)
                flicker = Mathf.Lerp(flicker, Random.value, 0.5f);
                glow.intensity = lightIntensity * Level * (0.8f + 0.4f * flicker) * (1f + AircraftLights.Glow);
            }
        }

        void OnDestroy()
        {
            if (glow != null) DestroyAny(glow.gameObject);
            if (flames != null) DestroyAny(flames.gameObject);
        }

        /// <summary>Per afterburning nozzle: a core and a plume tube (rings along the flame, segments around it) and a
        /// disc in the exit. Vertices sit on the nozzle: the shader moves them out along the flame (see the shader for
        /// the layout). Shared by every aircraft of the type.</summary>
        public static Mesh FlameMesh(AircraftType type)
        {
            if (meshes.TryGetValue(type, out var mesh) && mesh != null) return mesh;
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var tan = new List<Vector4>();
            var tLayer = new List<Vector2>();
            var radiusPhase = new List<Vector2>();
            var idx = new List<int>();
            var bounds = new Bounds();
            bool first = true;
            int k = 0;
            foreach (var e in type.exhausts)
            {
                if (!e.afterburner) continue;
                var dir = e.direction.sqrMagnitude > 0f ? e.direction.normalized : Vector3.back;
                var side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var up = Vector3.Cross(side, dir).normalized;
                float nozzlePhase = k++ * 1.37f;
                void Ring(float t, float layer)
                {
                    for (int s = 0; s < Segments; s++)
                    {
                        float a = 2f * Mathf.PI * s / Segments;
                        var radial = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        pos.Add(e.position);
                        nrm.Add(dir);
                        tan.Add(new Vector4(radial.x, radial.y, radial.z, 1f));
                        tLayer.Add(new Vector2(t, layer));
                        radiusPhase.Add(new Vector2(e.radius, nozzlePhase));
                    }
                }
                foreach (float layer in new[] { 0f, 1f })
                {
                    int b = pos.Count;
                    for (int i = 0; i <= Rings; i++) Ring(i / (float)Rings, layer);
                    for (int i = 0; i < Rings; i++)
                        for (int s = 0; s < Segments; s++)
                        {
                            int s2 = (s + 1) % Segments;
                            int a0 = b + i * Segments + s, a1 = b + i * Segments + s2;
                            int b0 = a0 + Segments, b1 = a1 + Segments;
                            idx.AddRange(new[] { a0, b0, b1, a0, b1, a1 });
                        }
                }
                // the disc: rings of growing radius (t = radius fraction), a fan at the centre
                int d = pos.Count;
                for (int i = 0; i <= DiscRings; i++) Ring(i / (float)DiscRings, 2f);
                for (int i = 0; i < DiscRings; i++)
                    for (int s = 0; s < Segments; s++)
                    {
                        int s2 = (s + 1) % Segments;
                        int a0 = d + i * Segments + s, a1 = d + i * Segments + s2;
                        idx.AddRange(new[] { a0, a0 + Segments, a1 + Segments, a0, a1 + Segments, a1 });
                    }
                // the flame's reach for culling: the plume at full reheat (the shader stretches it up to ~5% more)
                var reach = new Bounds(e.position, Vector3.one * e.radius * 3f);
                reach.Encapsulate(new Bounds(e.position + dir * e.radius * PlumeLength * 1.1f, Vector3.one * e.radius * 3f));
                if (first) bounds = reach;
                else bounds.Encapsulate(reach);
                first = false;
            }
            mesh = new Mesh { name = type.model + " afterburner flames", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetTangents(tan);
            mesh.SetUVs(0, tLayer);
            mesh.SetUVs(1, radiusPhase);
            mesh.SetTriangles(idx, 0);
            mesh.bounds = bounds;
            meshes[type] = mesh;
            return mesh;
        }

        static void DestroyAny(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
