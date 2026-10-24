using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Aircraft
{
    /// <summary>
    /// The aircraft's exterior lights, from the type's light points.
    ///  - Every light gets a glow that stays a fixed pixel size (AirCity/AircraftLightGlow), so nav lights,
    ///    strobes and beacons read at any zoom. Flashing runs on the GPU; all aircraft share one material.
    ///  - Landing and taxi lights also get real spot lights, only while the camera is close.
    ///
    ///   lights.beacon = true;                 // engines running
    ///   AircraftLights.Glow = 1;              // night: glows at full brightness
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class AircraftLights : MonoBehaviour
    {
        public AircraftType type;
        [Tooltip("AirCity/AircraftLightGlow material (shared by every aircraft).")]
        public Material glowMaterial;

        public bool navigation = true, beacon, strobes, landing, taxi, logo;

        [Tooltip("Real spot lights for landing / taxi lights within this camera distance (metres).")]
        public float spotLightDistance = 180f;
        public float spotIntensity = 40f;

        MeshRenderer glowRenderer;
        readonly List<(Light light, AircraftLightType type)> spots = new List<(Light, AircraftLightType)>();
        uint phase;

        static readonly Dictionary<AircraftType, Mesh> meshes = new Dictionary<AircraftType, Mesh>();
        static readonly int GlowId = Shader.PropertyToID("_AirCityLightGlow");
        static float glow;

        /// <summary>0 = daylight (glows faint), 1 = night (full). Global for all aircraft.</summary>
        public static float Glow
        {
            get => glow;
            set
            {
                glow = Mathf.Clamp01(value);
                Shader.SetGlobalFloat(GlowId, glow);
            }
        }

        public uint StateBits =>
            (navigation ? 1u : 0u) | (beacon ? 2u : 0u) | (strobes ? 4u : 0u) | (landing ? 8u : 0u) | (taxi ? 16u : 0u) |
            (logo ? 32u : 0u) | (phase << 8);

        void OnEnable()
        {
            // a stable per-aircraft phase so a fleet doesn't flash in sync
            phase = (uint)(Mathf.Abs(gameObject.name.GetHashCode() ^ transform.position.GetHashCode()) % 255);
            Build();
        }

        void OnDisable()
        {
            if (glowRenderer != null) glowRenderer.enabled = false;
        }

        void Build()
        {
            if (type == null || glowMaterial == null) return;
            if (glowRenderer == null)
            {
                var t = transform.Find("LightGlows");
                if (t == null)
                {
                    t = new GameObject("LightGlows").transform;
                    t.SetParent(transform, false);
                    // generated each time the aircraft is enabled: never saved into scenes or prefabs
                    t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                }
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
                glowRenderer = t.GetComponent<MeshRenderer>();
                if (glowRenderer == null) glowRenderer = t.gameObject.AddComponent<MeshRenderer>();
                mf.sharedMesh = GlowMesh(type);
                glowRenderer.sharedMaterial = glowMaterial;
                glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
                glowRenderer.receiveShadows = false;
                glowRenderer.lightProbeUsage = LightProbeUsage.Off;
                glowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            glowRenderer.enabled = true;
            if (spots.Count == 0)
            {
                foreach (var l in type.lights)
                {
                    if (l.type != AircraftLightType.Landing && l.type != AircraftLightType.Taxi) continue;
                    var go = new GameObject("Spot " + l.name) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = l.position;
                    go.transform.localRotation = Quaternion.LookRotation(l.direction);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.spotAngle = l.spread * 1.4f;
                    light.innerSpotAngle = l.spread * 0.6f;
                    light.range = Mathf.Max(type.Length * 2.5f, 35f);
                    light.color = l.color;
                    light.intensity = spotIntensity;
                    light.shadows = LightShadows.None;
                    light.enabled = false;
                    spots.Add((light, l.type));
                }
            }
        }

        void LateUpdate() => UpdateState();

        /// <summary>Push the light switches to the GPU and toggle the spot lights (LateUpdate does this every frame).</summary>
        public void UpdateState()
        {
            if (glowRenderer == null) Build();
            if (glowRenderer == null) return;
            glowRenderer.SetShaderUserValue(StateBits);
            var cam = Camera.main;
            bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < spotLightDistance * spotLightDistance;
            foreach (var (light, t) in spots)
                if (light != null) light.enabled = near && (t == AircraftLightType.Landing ? landing : taxi) && Glow > 0.05f;
        }

        void OnDestroy()
        {
            foreach (var (light, _) in spots)
                if (light != null) DestroyAny(light.gameObject);
            if (glowRenderer != null) DestroyAny(glowRenderer.gameObject);
        }

        /// <summary>One glow quad per light (shared by every aircraft of the type).</summary>
        /// <summary>Flash phase offset (seconds, uv2.x of the glow mesh). Strobes on one aircraft flash together;
        /// beacons alternate top / bottom: the one shining downward (under the fuselage) flashes half a second
        /// after the top one. By direction, not height, since a jet's belly beacon is well off the ground.</summary>
        public static float PhaseOffset(AircraftType.LightPoint l) =>
            l.type == AircraftLightType.Beacon && l.direction.y < -0.5f ? 0.5f : 0f;

        public static Mesh GlowMesh(AircraftType type)
        {
            if (meshes.TryGetValue(type, out var mesh) && mesh != null) return mesh;
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var col = new List<Color>();
            var corner = new List<Vector2>();
            var sector = new List<Vector2>();
            var phaseSize = new List<Vector2>();
            var idx = new List<int>();
            float scale = Mathf.Clamp(type.Wingspan / 11f, 1f, 1.5f);   // a bigger aircraft gets slightly bigger glows
            foreach (var l in type.lights)
            {
                float cos = l.spread >= 359f ? -2f : Mathf.Cos(0.5f * l.spread * Mathf.Deg2Rad);
                float size = (l.type == AircraftLightType.Landing ? 1.6f : l.type == AircraftLightType.Strobe ? 1.3f :
                    l.type == AircraftLightType.Logo ? 0.8f : 1f) * scale;
                float offset = PhaseOffset(l);
                int b = pos.Count;
                foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                {
                    pos.Add(l.position);
                    nrm.Add(l.direction.normalized);
                    col.Add(l.color);
                    corner.Add(c);
                    sector.Add(new Vector2(cos, (float)l.type));
                    phaseSize.Add(new Vector2(offset, size));
                }
                idx.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            mesh = new Mesh { name = type.model + " light glows", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetColors(col);
            mesh.SetUVs(0, corner);
            mesh.SetUVs(1, sector);
            mesh.SetUVs(2, phaseSize);
            mesh.SetTriangles(idx, 0);
            // the vertex shader expands every quad by a few pixels: grow the bounds so culling keeps them
            var bounds = type.bounds;
            bounds.Expand(4f);
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
