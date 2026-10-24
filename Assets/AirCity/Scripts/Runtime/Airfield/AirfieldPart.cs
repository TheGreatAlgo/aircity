using UnityEngine;
using UnityEngine.Rendering;

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
            renderer.sharedMaterials = materials;
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
}
