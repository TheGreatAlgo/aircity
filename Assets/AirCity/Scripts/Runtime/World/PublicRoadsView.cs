using UnityEngine;
using AirCity.Airfield;
using UnityEngine.Rendering;

namespace AirCity.World
{
    /// <summary>
    /// Public (landside) roads on a flat airport, as the sandbox has them: a saved PublicRoadNetwork drawn by
    /// PublicRoadGeometry (pavement, shoulders or sidewalks, kerbs, medians, markings) with its traffic signals. The
    /// career draws the same network over its valley (ValleyView); this is the same roads without terrain. Positions
    /// are in this object's space (the airport's); roads sit a few centimetres above its ground.
    /// </summary>
    [ExecuteAlways]
    public sealed class PublicRoadsView : MonoBehaviour
    {
        public PublicRoadNetwork network = new PublicRoadNetwork();
        public Material asphalt, shoulder, markings, kerb, medianGrass, structures;
        public GameObject trafficSignalPrefab;
        [Tooltip("Height of the road surface above the airport's ground (metres).")]
        public float level = .02f;

        public bool constructionPreview;
        public System.Func<Vector2,float> GroundHeight {get;set;}
        readonly ConstructionMaterials previewMaterials = new ConstructionMaterials();
        PublicRoadGeometry geometry;
        GameObject root;
        PublicRoadSignalsView signals;
        float clock;

        public PublicRoadGeometry Geometry => geometry;
        public void ShowCommittedGeometry(bool visible){if(root!=null)root.SetActive(visible);}
        // The same tool can use the valley's terrain renderer without owning a second graph or mesh.
        public System.Action<PublicRoadNetwork> RenderNetwork { get; set; }

        void OnEnable() { if(!constructionPreview)Rebuild(); }

        void OnDisable() => Clear();

        void Update()
        {
            if (signals == null) return;
            clock += Application.isPlaying ? Time.deltaTime : 0f;
            signals.SetClock(clock);
        }

        /// <summary>Draw the network again (after it changed).</summary>
        public void Rebuild()=>Rebuild(null,null);

        /// <summary>Preview only: retain full junction context while drawing selected sections and nodes.</summary>
        public void Rebuild(System.Collections.Generic.ISet<int> segmentIds,System.Collections.Generic.ISet<int> junctionIds,PublicRoadGeometry projectionContext=null)
        {
            if (RenderNetwork != null) { RenderNetwork(network); return; }
            Clear();
            network ??= new PublicRoadNetwork();
            if (network.segments.Count == 0) return;
            float y = level - .18f;
            geometry = new PublicRoadGeometry(network, null, null, GroundHeight??(_ => y),segmentIds,junctionIds,projectionContext);
            root = new GameObject("Public roads") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.up * y;
            Part("Asphalt", geometry.pavement, asphalt);
            Part("Shoulders and sidewalks", geometry.shoulders, shoulder);
            Part("Markings", geometry.markings, markings);
            Part("Kerbs and barriers", geometry.kerbMesh, kerb);
            Part("Median islands", geometry.medianGrass, medianGrass);
            Part("Bridges", geometry.structures, structures);
            signals = root.AddComponent<PublicRoadSignalsView>();
            signals.Build(network, trafficSignalPrefab, geometry.StopLine,junctionIds);
            signals.SetClock(clock);
        }

        void Part(string name, Mesh mesh, Material material)
        {
            mesh.hideFlags = HideFlags.DontSave;
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = constructionPreview ? previewMaterials.Get(material) : material;
            r.shadowCastingMode = mesh == geometry.structures || mesh == geometry.kerbMesh ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        void Clear()
        {
            if (geometry != null)
                foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures, geometry.kerbMesh, geometry.medianGrass })
                    Destroy(mesh);
            if (root != null) Destroy(root);
            geometry = null; root = null; signals = null;
            previewMaterials.Clear();
        }

        static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else DestroyImmediate(o);
        }
    }
}
