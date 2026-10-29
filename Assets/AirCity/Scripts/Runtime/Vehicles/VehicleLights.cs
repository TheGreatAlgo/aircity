using System.Collections.Generic;
using AirCity.Aircraft;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Vehicles
{
    /// <summary>
    /// A vehicle's (or a building's) lights, from its type's light points, drawn by AirCity/VehicleLightGlow in one
    /// draw call per vehicle: a glow per lamp that stays a few pixels wide however far the camera is, beacons that
    /// rotate on the GPU, and at night pools of light on the ground where the head, work and flood lights point.
    /// Which groups are on goes in the renderer's shader user value (all vehicles share one material).
    ///
    ///   lights.beacon = true;   // out on the airfield
    ///   lights.work = true;     // ploughing / mowing
    ///   // headlights follow the night (AircraftLights.Glow) unless headlights is set false
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class VehicleLights : MonoBehaviour
    {
        public VehicleType type;
        [Tooltip("AirCity/VehicleLightGlow material (shared by every vehicle).")]
        public Material glowMaterial;

        public bool headlights = true, beacon, work, markers = true, flood = true;
        [Tooltip("Headlights, work and flood lights only at night.")]
        public bool onlyAtNight = true;
        [Tooltip("The beacon (and the markers with it) only at night: a control tower's aerodrome beacon.")]
        public bool beaconOnlyAtNight;

        MeshRenderer glowRenderer;

        static readonly Dictionary<VehicleType, Mesh> meshes = new Dictionary<VehicleType, Mesh>();

        public uint StateBits
        {
            get
            {
                bool dark = AircraftLights.Glow > 0.3f;
                bool night = !onlyAtNight || dark;
                bool turning = beacon && (dark || !beaconOnlyAtNight);
                return (headlights && night ? 1u : 0u) | (turning ? 2u : 0u) | (work && night ? 4u : 0u) |
                       (markers && (turning || night) ? 8u : 0u) | (flood && night ? 16u : 0u) | (Phase << 8);
            }
        }

        /// <summary>A stable per-vehicle beacon phase (from its name, which the depot sets after spawning it), so a
        /// team's beacons don't turn in step.</summary>
        uint Phase => (uint)(Mathf.Abs(name.GetHashCode() * 7919) % 255);

        void OnEnable() => Build();

        void OnDisable()
        {
            if (glowRenderer != null) glowRenderer.enabled = false;
        }

        void Build()
        {
            if (type == null || glowMaterial == null || type.lights.Length == 0) return;
            if (glowRenderer == null)
            {
                var t = transform.Find("LightGlows");
                if (t == null)
                {
                    t = new GameObject("LightGlows").transform;
                    t.SetParent(transform, false);
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
        }

        void LateUpdate() => UpdateState();

        /// <summary>Push the light switches to the GPU (LateUpdate does this every frame).</summary>
        public void UpdateState()
        {
            if (glowRenderer == null) Build();
            if (glowRenderer != null) glowRenderer.SetShaderUserValue(StateBits);
        }

        void OnDestroy()
        {
            if (glowRenderer == null) return;
            if (Application.isPlaying) Destroy(glowRenderer.gameObject);
            else DestroyImmediate(glowRenderer.gameObject);
        }

        /// <summary>Pool type ids in the glow mesh: PoolBase + the lamp type that casts it.</summary>
        public const int PoolBase = 10;

        /// <summary>One glow quad per lamp, plus a ground pool per head / work / flood lamp (shared per type).</summary>
        public static Mesh GlowMesh(VehicleType type)
        {
            if (meshes.TryGetValue(type, out var mesh) && mesh != null) return mesh;
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var col = new List<Color>();
            var corner = new List<Vector2>();
            var kind = new List<Vector4>();
            var idx = new List<int>();
            var bounds = type.bounds;

            void Quad(Vector3 p, Vector3 dir, Color c, Vector4 k)
            {
                int b = pos.Count;
                foreach (var cn in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                {
                    pos.Add(p);
                    nrm.Add(dir);
                    col.Add(c);
                    corner.Add(cn);
                    kind.Add(k);
                }
                idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }

            foreach (var l in type.lights)
            {
                var dir = l.direction.sqrMagnitude > 1e-6f ? l.direction.normalized : Vector3.up;
                Quad(l.position, dir, l.color, new Vector4((int)l.type, l.size, l.phase, 0f));

                // a pool of light on the ground: (centre, along, length, width) by lamp type
                Vector3 centre;
                float length, width;
                var flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
                flat.Normalize();
                switch (l.type)
                {
                    case VehicleLightType.Head:
                        length = l.position.y > 2.5f ? 22f : 14f;
                        width = 7f;
                        centre = new Vector3(l.position.x, 0f, l.position.z) + flat * (length * 0.5f + 1f);
                        break;
                    case VehicleLightType.Work:
                    case VehicleLightType.Flood:
                        float t = dir.y < -0.05f ? Mathf.Min(l.position.y / -dir.y, 14f) : 6f;
                        var hit = l.position + dir * t;
                        centre = new Vector3(hit.x, 0f, hit.z);
                        length = l.type == VehicleLightType.Flood ? 16f : 9f;
                        width = l.type == VehicleLightType.Flood ? 14f : 6f;
                        break;
                    default:
                        continue;
                }
                centre.y = 0.045f;    // over the paint (3.1 cm), under an aircraft's wheels
                Quad(centre, flat, l.color, new Vector4(PoolBase + (int)l.type, length * 0.5f, width * 0.5f, 0f));
                bounds.Encapsulate(centre + flat * length * 0.5f);
                bounds.Encapsulate(centre - flat * length * 0.5f);
            }

            mesh = new Mesh { name = type.model + " light glows", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetColors(col);
            mesh.SetUVs(0, corner);
            mesh.SetUVs(1, kind);
            mesh.SetTriangles(idx, 0);
            bounds.Expand(4f);
            mesh.bounds = bounds;
            meshes[type] = mesh;
            return mesh;
        }
    }
}
