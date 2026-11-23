using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace AirCity.Airfield
{
    /// <summary>A generated child renderer (surface, paint, glows, fixtures). Made when its owner is enabled and
    /// never saved into scenes: the owner's data is what gets saved, the meshes are rebuilt from it.</summary>
    sealed class AirfieldPart
    {
        readonly Transform owner;
        readonly string name;
        MeshFilter filter;
        MeshRenderer renderer;
        Mesh mesh;
        public bool constructionPreview;
        public bool previewRespectsDepth;
        readonly ConstructionMaterials previewMaterials = new ConstructionMaterials();

        public AirfieldPart(Transform owner, string name)
        {
            this.owner = owner;
            this.name = name;
        }

        public Mesh Mesh => mesh;
        public MeshRenderer Renderer => renderer;

        /// <summary>The part's mesh to rebuild into (null the first time).</summary>
        public Mesh Target
        {
            get
            {
                Ensure();
                return mesh;
            }
        }

        public void Set(Mesh built, params Material[] materials)
        {
            Ensure();
            mesh = built;
            filter.sharedMesh = built;
            if (constructionPreview)
            {
                var preview = new Material[materials.Length];
                for (int i = 0; i < materials.Length; i++) preview[i] = previewMaterials.Get(materials[i], previewRespectsDepth);
                renderer.sharedMaterials = preview;
            }
            else { renderer.sharedMaterials = materials; previewMaterials.Clear(); }
            renderer.enabled = built != null && built.vertexCount > 0;
        }

        void Ensure()
        {
            if (renderer != null) return;
            var t = owner.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(owner, false);
                t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                t.gameObject.layer = owner.gameObject.layer;
            }
            filter = t.GetComponent<MeshFilter>();
            if (filter == null) filter = t.gameObject.AddComponent<MeshFilter>();
            renderer = t.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            mesh = filter.sharedMesh;
        }

        public void Destroy()
        {
            previewMaterials.Clear();
            if (mesh != null) DestroyAny(mesh);
            if (renderer != null) DestroyAny(renderer.gameObject);
            mesh = null;
            renderer = null;
            filter = null;
        }

        public static void DestroyAny(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
    /// <summary>Temporary forward-only material copies for construction previews over ungraded land.</summary>
    sealed class ConstructionMaterials
    {
        readonly Dictionary<(Material source, bool respectDepth),Material> previewMaterials =
            new Dictionary<(Material, bool),Material>();

        public Material Get(Material source, bool respectDepth = false)
        {
            if (source == null || !source.HasProperty("_ConstructionZTest")) return source;
            var key = (source, respectDepth);
            if (previewMaterials.TryGetValue(key, out var material)) return material;
            bool paint = source.shader.name == "AirCity/AirfieldPaint";
            material = new Material(source) { name = source.name + " construction preview", hideFlags = HideFlags.HideAndDontSave,
                renderQueue = paint ? 3001 : 3000 };
            // A tunnel's opening already matches the draft mesh. Preserve depth against the roof
            // and the remaining hillside when rendering a draft that includes an excavation.
            material.SetFloat("_ConstructionZTest", (float)(respectDepth ? CompareFunction.LessEqual : CompareFunction.Always));
            material.SetFloat("_ConstructionZWrite", respectDepth && !paint ? 1f : 0f);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormals", false);
            previewMaterials.Add(key, material);
            return material;
        }

        public void Clear()
        {
            foreach (var material in previewMaterials.Values) AirfieldPart.DestroyAny(material);
            previewMaterials.Clear();
        }

    }

}
