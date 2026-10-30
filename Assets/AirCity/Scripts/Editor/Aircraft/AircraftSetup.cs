using System.IO;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AirCity.EditorTools.Aircraft
{
    /// <summary>
    /// One-off authoring steps around generated aircraft. Never overwrites existing liveries or
    /// prefabs, so hand edits are safe; delete an asset to have it recreated.
    /// Batch: -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.Bootstrap
    /// </summary>
    public static class AircraftSetup
    {
        public const string LiveriesFolder = "Assets/AirCity/Liveries";
        public const string SandboxScene = "Assets/AirCity/Scenes/Sandbox.unity";
        const string MaterialsFolder = "Assets/AirCity/Materials";

        public static void Bootstrap()
        {
            CreateMissingPrefabsAndLiveries();
            if (!File.Exists(SandboxScene)) CreateSandboxScene();
        }

        [MenuItem("AirCity/Aircraft/Create Missing Prefabs and Example Liveries")]
        public static void CreateMissingPrefabsAndLiveries()
        {
            EnsureFolder(LiveriesFolder);
            foreach (var type in AllTypes())
            {
                AircraftLiveryAsset first = null;
                foreach (var ex in type.examples)
                {
                    var asset = CreateLiveryIfMissing(ex);
                    if (first == null) first = asset;
                }
                string prefabPath = PrefabPath(type);
                if (!File.Exists(prefabPath)) CreatePrefab(type, $"{TypeFolder(type)}/Models/{type.model}.fbx", prefabPath, first);
            }
            AssetDatabase.SaveAssets();
        }

        public static AircraftType[] AllTypes() =>
            AssetDatabase.FindAssets("t:AircraftType")
                .Select(g => AssetDatabase.LoadAssetAtPath<AircraftType>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(t => t != null).OrderBy(t => t.Wingspan).ToArray();

        public static AircraftLiveryAsset[] AllLiveries() =>
            AssetDatabase.FindAssets("t:AircraftLiveryAsset", new[] { LiveriesFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<AircraftLiveryAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .OrderBy(a => a.name).ToArray();

        public static string TypeFolder(AircraftType type) =>
            Path.GetDirectoryName(AssetDatabase.GetAssetPath(type)).Replace('\\', '/');

        public static string PrefabPath(AircraftType type) => $"{TypeFolder(type)}/{type.model}.prefab";

        public static GameObject Prefab(AircraftType type) => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(type));

        static AircraftLiveryAsset CreateLiveryIfMissing(AircraftType.ExampleLivery ex)
        {
            string path = $"{LiveriesFolder}/{ex.name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<AircraftLiveryAsset>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<AircraftLiveryAsset>();
            asset.livery = new AircraftLivery
            {
                style = ex.style,
                baseColor = ex.baseColor,
                color1 = ex.color1,
                color2 = ex.color2,
                color3 = ex.color3,
                registrationColor = ex.registrationColor,
                defaultRegistration = ex.registration,
                logo = ex.logo,
            };
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void CreatePrefab(AircraftType type, string modelPath, string prefabPath, AircraftLiveryAsset livery)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"AircraftSetup: no model at {modelPath}");
                return;
            }
            var mats = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().ToArray();
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var aircraft = inst.AddComponent<AirCity.Aircraft.Aircraft>();
                aircraft.type = type;
                aircraft.outlineMaterial = SharedMaterial("AircraftOutline", "AirCity/AircraftOutline");

                var applier = inst.AddComponent<AircraftLiveryApplier>();
                applier.type = type;
                applier.liveryAsset = livery;
                applier.bodyMaterial = mats.FirstOrDefault(m => m.name == "Body");
                applier.registrationMaterial = mats.FirstOrDefault(m => m.name == "Registration");

                inst.AddComponent<AircraftControlSurfaces>().FindParts();
                inst.AddComponent<AircraftGroundMotion>().FindParts(type);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    if ((t.name == "Propeller" || t.name.StartsWith("Fan_")) && t.GetComponent<PropellerSpinner>() == null)
                        t.gameObject.AddComponent<PropellerSpinner>();

                // the glow material is assigned after AddComponent, so nothing is generated into the prefab
                var lights = inst.AddComponent<AircraftLights>();
                lights.type = type;
                lights.glowMaterial = SharedMaterial("AircraftLightGlow", "AirCity/AircraftLightGlow");
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);   // a variant of the model: re-exports flow through
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        static Material SharedMaterial(string name, string shaderName)
        {
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find(shaderName)) { name = name, enableInstancing = false };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ------------------------------------------------------------------ sandbox

        [MenuItem("AirCity/Dev/Rebuild Sandbox Scene")]
        public static void CreateSandboxScene()
        {
            EnsureFolder(MaterialsFolder);
            EnsureFolder(Path.GetDirectoryName(SandboxScene).Replace('\\', '/'));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = SetUpDaylight();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Apron";
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            var groundRenderer = ground.GetComponent<Renderer>();
            groundRenderer.sharedMaterial = ApronMaterial();
            groundRenderer.shadowCastingMode = ShadowCastingMode.Off;   // a flat ground only receives

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 45f;
            var orbit = cam.gameObject.AddComponent<DevOrbitCamera>();
            orbit.pivot = new Vector3(14f, 0f, 40f);
            orbit.distance = 150f;
            orbit.pitch = 38f;
            orbit.yaw = 20f;   // facing the same way as the light, so the apron is lit, not back-lit
            var rot = Quaternion.Euler(orbit.pitch, orbit.yaw, 0f);
            cam.transform.SetPositionAndRotation(orbit.pivot - rot * Vector3.forward * orbit.distance, rot);
            cam.gameObject.AddComponent<AudioListener>();
            var controls = cam.gameObject.AddComponent<DevSandboxControls>();
            controls.sun = sun;
            cam.gameObject.AddComponent<DevToolbar>();

            // one parked aircraft per (type, livery): light aircraft in front, airliners behind. Widebodies show one
            // row of every other livery (the airline ones), which keeps them on the apron
            var parent = new GameObject("Aircraft").transform;
            var liveries = AllLiveries();
            float rowZ = 0f;
            foreach (var type in AllTypes())
            {
                var prefab = Prefab(type);
                if (prefab == null) continue;
                float dx = type.Wingspan + 2f, dz = type.Length + 4f;
                bool wide = type.SizeCode > 'C';
                int shown = wide ? Mathf.Min(3, liveries.Length) : liveries.Length;
                for (int i = 0; i < shown; i++)
                {
                    var livery = liveries[wide ? (2 * i + 1) % liveries.Length : i];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    go.name = $"{type.model} {livery.name}";
                    var pos = new Vector3((i % 3 - 1) * dx + 14f, 0f, rowZ + (i / 3) * dz);
                    go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 215f, 0f));
                    var a = go.GetComponent<AircraftLiveryApplier>();
                    a.liveryAsset = livery;
                    a.dirt = (i % 5) / 4f;          // clean to filthy, to show the range
                    a.Apply();
                }
                rowZ += (shown + 2) / 3 * dz + 6f;
            }

            EditorSceneManager.SaveScene(scene, SandboxScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SandboxScene, true) };
        }

        static Light SetUpDaylight()
        {
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.52f, 0.55f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.22f, 0.22f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 2.0f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            RenderSettings.sun = sun;
            return sun;
        }

        static Material ApronMaterial()
        {
            string path = MaterialsFolder + "/Apron.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var rp = GraphicsSettings.currentRenderPipeline;
            var shader = rp != null ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            mat = new Material(shader) { name = "Apron" };
            var c = new Color(0.56f, 0.56f, 0.55f);   // weathered concrete
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ------------------------------------------------------------------ thumbnails

        public const int ThumbWidth = 384, ThumbHeight = 256;

        /// <summary>Renders every (type, livery) to &lt;Type folder&gt;/Thumbnails/&lt;Model&gt;_&lt;Livery&gt;.png with a
        /// transparent background, for build menus and airline screens.
        /// Batch: -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.RenderThumbnails</summary>
        [MenuItem("AirCity/Aircraft/Render Thumbnails")]
        public static void RenderThumbnails()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string previous = SceneManager.GetActiveScene().path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetUpDaylight();
            var cam = new GameObject("Thumbnail Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;                   // an LDR target keeps the alpha channel
            cam.fieldOfView = 24f;
            cam.farClipPlane = 1000f;
            int count = 0;
            try
            {
                foreach (var type in AllTypes())
                {
                    var prefab = Prefab(type);
                    if (prefab == null) continue;
                    string dir = $"{TypeFolder(type)}/Thumbnails";
                    Directory.CreateDirectory(dir);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.rotation = Quaternion.identity;
                    // front three-quarter view from the left, framed on the bounds
                    var b = type.bounds;
                    float radius = b.extents.magnitude;
                    float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.82f;
                    var dirToCam = new Vector3(-1.0f, 0.62f, 1.25f).normalized;
                    cam.transform.position = b.center + dirToCam * dist;
                    cam.transform.LookAt(b.center + Vector3.down * b.extents.y * 0.15f);
                    var applier = go.GetComponent<AircraftLiveryApplier>();
                    foreach (var livery in AllLiveries())
                    {
                        applier.dirt = 0.15f;
                        applier.SetLivery(livery);
                        SaveThumbnail(cam, $"{dir}/{type.model}_{livery.name}.png");
                        count++;
                    }
                    Object.DestroyImmediate(go);
                }
            }
            finally
            {
                AssetDatabase.Refresh();
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            }
            Debug.Log($"AircraftSetup: rendered {count} thumbnails");
        }

        static void SaveThumbnail(Camera cam, string path)
        {
            const int ss = 2;   // render at 2x and box-filter down: clean edges without MSAA resolve quirks
            var rt = new RenderTexture(ThumbWidth * ss, ThumbHeight * ss, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var big = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            big.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = prev;
            rt.Release();
            Object.DestroyImmediate(rt);
            var src = big.GetPixels32();
            int bigWidth = big.width;
            var dst = new Color32[ThumbWidth * ThumbHeight];
            for (int y = 0; y < ThumbHeight; y++)
                for (int x = 0; x < ThumbWidth; x++)
                {
                    int r = 0, g = 0, bl = 0, a = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            var c = src[(y * ss + sy) * bigWidth + x * ss + sx];
                            r += c.r * c.a; g += c.g * c.a; bl += c.b * c.a; a += c.a;   // premultiplied average
                        }
                    dst[y * ThumbWidth + x] = a == 0 ? new Color32(0, 0, 0, 0)
                        : new Color32((byte)(r / a), (byte)(g / a), (byte)(bl / a), (byte)(a / (ss * ss)));
                }
            Object.DestroyImmediate(big);
            var tex = new Texture2D(ThumbWidth, ThumbHeight, TextureFormat.RGBA32, false);
            tex.SetPixels32(dst);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
