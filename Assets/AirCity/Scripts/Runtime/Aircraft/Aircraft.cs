using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Aircraft
{
    public enum AircraftHighlight
    {
        None = 0,
        Hover = 1,
        Selected = 2,
    }

    /// <summary>
    /// The root of an aircraft instance: its type, picking, and hover / selection outline.
    ///
    ///   if (Aircraft.TryPick(camera.ScreenPointToRay(mouse), out var a)) a.Highlight = AircraftHighlight.Selected;
    ///   Vector3 door = a.WorldPosition(a.type.points[0]);
    ///
    /// Picking uses the model's "Collider" child (a coarse MeshCollider on the Aircraft layer). The outline is
    /// an inverted hull of whichever LOD is showing, drawn only while highlighted.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class Aircraft : MonoBehaviour
    {
        public const string LayerName = "Aircraft";

        public AircraftType type;
        [Tooltip("AirCity/AircraftOutline material.")]
        public Material outlineMaterial;
        public Color hoverColor = new Color(1f, 1f, 1f, 0.75f);
        public Color selectedColor = new Color(1f, 0.74f, 0.1f, 1f);
        [Tooltip("Outline width in pixels.")]
        public float outlineWidth = 2.5f;

        AircraftHighlight highlight;
        LODGroup lodGroup;
        Renderer[][] lodRenderers;
        MaterialPropertyBlock outlineProps;

        static readonly List<Aircraft> highlighted = new List<Aircraft>();
        static readonly List<Aircraft> all = new List<Aircraft>();

        /// <summary>Every enabled aircraft (the depots' vehicles keep clear of them).</summary>
        public static IReadOnlyList<Aircraft> All => all;

        /// <summary>Moving under its own power or towed (its ground motion has a speed).</summary>
        public bool Moving
        {
            get
            {
                var m = GetComponent<AircraftGroundMotion>();
                return m != null && Mathf.Abs(m.groundSpeed) > 0.05f;
            }
        }
        static bool hooked;
        static readonly int ColorId = Shader.PropertyToID("_Color"), WidthId = Shader.PropertyToID("_Width");

        public AircraftHighlight Highlight
        {
            get => highlight;
            set
            {
                highlight = value;
                highlighted.Remove(this);
                if (value != AircraftHighlight.None && isActiveAndEnabled) highlighted.Add(this);
                Hook();
            }
        }

        /// <summary>Raycast against aircraft colliders.</summary>
        public static bool TryPick(Ray ray, out Aircraft aircraft, float maxDistance = 10000f)
        {
            aircraft = null;
            int layer = LayerMask.NameToLayer(LayerName);
            int mask = layer >= 0 ? 1 << layer : Physics.DefaultRaycastLayers;
            if (!Physics.Raycast(ray, out var hit, maxDistance, mask, QueryTriggerInteraction.Ignore)) return false;
            aircraft = hit.collider.GetComponentInParent<Aircraft>();
            return aircraft != null;
        }

        public Vector3 WorldPosition(in AircraftType.ServicePoint point) => transform.TransformPoint(point.position);
        public Vector3 WorldDirection(in AircraftType.ServicePoint point) => transform.TransformDirection(point.normal);

        void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
            CacheLods();
            if (highlight != AircraftHighlight.None && !highlighted.Contains(this)) highlighted.Add(this);
        }

        void OnDisable()
        {
            highlighted.Remove(this);
            all.Remove(this);
        }

        void CacheLods()
        {
            lodGroup = GetComponent<LODGroup>();
            if (lodGroup == null)
            {
                lodRenderers = new[] { GetComponentsInChildren<Renderer>() };
                return;
            }
            var lods = lodGroup.GetLODs();
            lodRenderers = new Renderer[lods.Length][];
            for (int i = 0; i < lods.Length; i++) lodRenderers[i] = lods[i].renderers;
        }

        /// <summary>The LOD a camera shows (mirrors LODGroup's screen-height rule), -1 when culled.</summary>
        public int CurrentLod(Camera cam)
        {
            if (lodGroup == null) return 0;
            var lods = lodGroup.GetLODs();
            var t = lodGroup.transform;
            var s = t.lossyScale;
            float size = lodGroup.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            float relative = cam.orthographic
                ? size / (2f * cam.orthographicSize)
                : size / (2f * Vector3.Distance(cam.transform.position, t.TransformPoint(lodGroup.localReferencePoint)) *
                          Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad));
            relative *= QualitySettings.lodBias;
            for (int i = 0; i < lods.Length; i++)
                if (relative >= lods[i].screenRelativeTransitionHeight) return i;
            return -1;
        }

        // ------------------------------------------------------------------ outline

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += DrawOutlines;
        }

        static void DrawOutlines(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            for (int i = highlighted.Count - 1; i >= 0; i--)
            {
                var a = highlighted[i];
                if (a == null)
                {
                    highlighted.RemoveAt(i);
                    continue;
                }
                a.DrawOutline(cam);
            }
        }

        void DrawOutline(Camera cam)
        {
            if (outlineMaterial == null || lodRenderers == null) return;
            int lod = CurrentLod(cam);
            if (lod < 0 || lod >= lodRenderers.Length) return;
            outlineProps ??= new MaterialPropertyBlock();
            outlineProps.SetColor(ColorId, highlight == AircraftHighlight.Selected ? selectedColor : hoverColor);
            outlineProps.SetFloat(WidthId, outlineWidth);
            var rp = new RenderParams(outlineMaterial)
            {
                camera = cam,
                matProps = outlineProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer,
            };
            foreach (var r in lodRenderers[lod])
            {
                // spinning propellers / fans would draw a flickering outline
                if (r == null || !r.enabled || r.name.StartsWith("Propeller") || r.name.StartsWith("Fan")) continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                rp.worldBounds = r.bounds;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    Graphics.RenderMesh(rp, mesh, sub, r.localToWorldMatrix);
            }
        }
    }
}
