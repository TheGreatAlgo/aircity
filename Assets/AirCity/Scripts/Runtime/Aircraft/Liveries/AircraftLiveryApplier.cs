using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>
    /// Paints an aircraft instance with a livery and a registration.
    ///
    ///   GetComponent&lt;AircraftLiveryApplier&gt;().SetLivery(livery, "N123AB");
    ///
    /// Aircraft of the same type with identical liveries share one generated body texture and
    /// material. Each aircraft additionally gets a small texture for its registration patch.
    /// In edit mode the livery is previewed through MaterialPropertyBlocks, so nothing is
    /// written into scenes or prefabs; in play mode materials are swapped (SRP-Batcher friendly).
    ///
    /// Dirt is the Lit shader's detail albedo (the type's baked grime map) scaled by `dirt`. Body materials
    /// are shared per (type, livery, dirt step), so dirt comes in DirtSteps levels to keep sharing high.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class AircraftLiveryApplier : MonoBehaviour
    {
        [Tooltip("The aircraft type (generated with the model).")]
        public AircraftType type;
        public AircraftLiveryAsset liveryAsset;
        [Tooltip("Registration painted on the fuselage. Empty = the livery's default registration.")]
        public string registration = "";
        [Tooltip("Grime: 0 = factory fresh, 1 = overdue for a wash.")]
        [Range(0f, 1f)] public float dirt = 0.3f;

        public const int DirtSteps = 6;

        [Tooltip("The model's imported 'Body' material (found by name if left empty).")]
        public Material bodyMaterial;
        [Tooltip("The model's imported 'Registration' material (found by name if left empty).")]
        public Material registrationMaterial;

        AircraftLivery runtimeLivery;
        Texture2D registrationTexture;
        Material registrationInstance;
        MaterialPropertyBlock block;

        struct Slot
        {
            public Renderer renderer;
            public int index;
            public bool isRegistration;
        }

        readonly List<Slot> slots = new List<Slot>();
        readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();

        // one body material per (aircraft type, livery, dirt step); textures live in the compositor's cache
        static readonly Dictionary<(AircraftType, string, int), Material> bodyMaterials =
            new Dictionary<(AircraftType, string, int), Material>();
        static readonly int DetailMapId = Shader.PropertyToID("_DetailAlbedoMap"),
            DetailScaleId = Shader.PropertyToID("_DetailAlbedoMapScale");

        public AircraftLivery CurrentLivery => runtimeLivery ?? (liveryAsset != null ? liveryAsset.livery : null);

        public string CurrentRegistration
        {
            get
            {
                if (!string.IsNullOrEmpty(registration)) return registration;
                var l = CurrentLivery;
                return l != null ? l.defaultRegistration : "";
            }
        }

        // ------------------------------------------------------------------ API

        /// <summary>Paint with livery data (e.g. from your airline definitions).</summary>
        public void SetLivery(AircraftLivery livery, string newRegistration = null)
        {
            runtimeLivery = livery;
            if (newRegistration != null) registration = newRegistration;
            Apply();
        }

        public void SetLivery(AircraftLiveryAsset asset, string newRegistration = null)
        {
            liveryAsset = asset;
            runtimeLivery = null;
            if (newRegistration != null) registration = newRegistration;
            Apply();
        }

        /// <summary>Change only the registration (cheap: the body texture comes from the cache).</summary>
        public void SetRegistration(string newRegistration)
        {
            registration = newRegistration;
            Apply();
        }

        /// <summary>Change the grime level (cheap: swaps to a cached material).</summary>
        public void SetDirt(float amount)
        {
            dirt = Mathf.Clamp01(amount);
            Apply();
        }

        /// <summary>Dirt rounded to the shared material steps.</summary>
        public float QuantizedDirt => Mathf.Round(Mathf.Clamp01(dirt) * (DirtSteps - 1)) / (DirtSteps - 1);

        /// <summary>Free every shared livery texture and material (e.g. between levels). Aircraft that
        /// are still in use must call Apply() again afterwards.</summary>
        public static void ClearSharedCache()
        {
            foreach (var m in bodyMaterials.Values)
                if (m != null) DestroyAny(m);
            bodyMaterials.Clear();
            AircraftLiveryCompositor.ClearCaches();
        }

        // ------------------------------------------------------------------ lifecycle

        void OnEnable() => Apply();

        void OnDisable()
        {
            if (!Application.isPlaying) ClearPropertyBlocks();
        }

        void OnDestroy()
        {
            if (registrationInstance != null) DestroyAny(registrationInstance);
            if (registrationTexture != null) DestroyAny(registrationTexture);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled) Apply();
            };
        }
#endif

        // ------------------------------------------------------------------ apply

        public void Apply()
        {
            var livery = CurrentLivery;
            if (livery == null || type == null || type.detail == null) return;
            FindSlots();
            if (slots.Count == 0) return;

            var body = GetBody(livery);
            registrationTexture = AircraftLiveryCompositor.ComposeRegistration(type, livery, CurrentRegistration, registrationTexture);

            if (Application.isPlaying)
            {
                if (registrationInstance == null)
                {
                    var src = registrationMaterial != null ? registrationMaterial : body.mat;
                    registrationInstance = new Material(src) { name = "Registration (" + name + ")" };
                }
                registrationInstance.mainTexture = registrationTexture;
                SetSmoothness(registrationInstance, livery.paintSmoothness);
                SetDirt(registrationInstance, type.registrationDirt, dirt);
                foreach (var kv in originals)
                {
                    var mats = (Material[])kv.Value.Clone();
                    foreach (var s in slots)
                        if (s.renderer == kv.Key)
                            mats[s.index] = s.isRegistration ? registrationInstance : body.mat;
                    kv.Key.sharedMaterials = mats;
                }
            }
            else
            {
                if (block == null) block = new MaterialPropertyBlock();
                foreach (var s in slots)
                {
                    var mat = originals[s.renderer][s.index];
                    s.renderer.GetPropertyBlock(block, s.index);
                    SetTexture(block, mat, s.isRegistration ? registrationTexture : body.tex);
                    if (mat.HasProperty("_Smoothness")) block.SetFloat("_Smoothness", livery.paintSmoothness);
                    if (mat.HasProperty("_Glossiness")) block.SetFloat("_Glossiness", livery.paintSmoothness);
                    if (mat.HasProperty(DetailScaleId)) block.SetFloat(DetailScaleId, dirt);
                    s.renderer.SetPropertyBlock(block, s.index);
                }
            }
        }

        /// <summary>The body texture this aircraft currently shows (null before the first Apply).</summary>
        public Texture2D CurrentBodyTexture =>
            CurrentLivery != null && type != null ? AircraftLiveryCompositor.GetBodyTexture(type, CurrentLivery) : null;

        /// <summary>This aircraft's registration texture (null before the first Apply).</summary>
        public Texture2D CurrentRegistrationTexture => registrationTexture;

        (Texture2D tex, Material mat) GetBody(AircraftLivery livery)
        {
            int step = Mathf.RoundToInt(QuantizedDirt * (DirtSteps - 1));
            var key = (type, livery.CacheKey(), step);
            var tex = AircraftLiveryCompositor.GetBodyTexture(type, livery);
            if (!bodyMaterials.TryGetValue(key, out var mat) || mat == null || mat.mainTexture != tex)
            {
                var src = bodyMaterial != null ? bodyMaterial : FirstOriginal(false);
                mat = new Material(src) { name = "Body (livery)", hideFlags = HideFlags.DontSave };
                mat.mainTexture = tex;
                SetSmoothness(mat, livery.paintSmoothness);
                SetDirt(mat, type.dirt, QuantizedDirt);
                bodyMaterials[key] = mat;
            }
            return (tex, mat);
        }

        Material FirstOriginal(bool registrationSlot)
        {
            foreach (var s in slots)
                if (s.isRegistration == registrationSlot) return originals[s.renderer][s.index];
            return null;
        }

        void FindSlots()
        {
            if (slots.Count > 0 && slots.TrueForAll(s => s.renderer != null)) return;
            slots.Clear();
            originals.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    // by reference, or by name (also matches painted copies such as "Body (livery)")
                    bool isReg = m == registrationMaterial || BaseName(m) == "Registration";
                    bool isBody = !isReg && (m == bodyMaterial || BaseName(m) == "Body");
                    if (!isBody && !isReg) continue;
                    slots.Add(new Slot { renderer = r, index = i, isRegistration = isReg });
                    any = true;
                }
                if (any) originals[r] = mats;
            }
            if (bodyMaterial == null) bodyMaterial = FirstOriginal(false);
            if (registrationMaterial == null) registrationMaterial = FirstOriginal(true);
        }

        void ClearPropertyBlocks()
        {
            foreach (var s in slots)
                if (s.renderer != null) s.renderer.SetPropertyBlock(null, s.index);
        }

        static string BaseName(Material m)
        {
            string n = m.name;
            int i = n.IndexOf(" (");
            return i >= 0 ? n.Substring(0, i) : n;
        }

        static void SetTexture(MaterialPropertyBlock b, Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) b.SetTexture("_BaseMap", t);             // URP
            if (m.HasProperty("_BaseColorMap")) b.SetTexture("_BaseColorMap", t);   // HDRP
            if (m.HasProperty("_MainTex")) b.SetTexture("_MainTex", t);             // Built-in
        }

        /// <summary>URP Lit detail albedo in "scaled" mode: albedo *= 1 + amount * (2 * dirt - 1).</summary>
        static void SetDirt(Material m, Texture2D map, float amount)
        {
            if (map == null || !m.HasProperty(DetailMapId)) return;
            m.SetTexture(DetailMapId, map);
            m.SetFloat(DetailScaleId, amount);
            m.EnableKeyword("_DETAIL_SCALED");
        }

        static void SetSmoothness(Material m, float s)
        {
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", s);
        }

        static void DestroyAny(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
