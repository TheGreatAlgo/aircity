using System.IO;
using AirCity.Vehicles;
using UnityEditor;
using UnityEngine;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>
    /// Prefabs and materials for the maintenance vehicles, the tugs, the depot and the control towers (created once, then
    /// hand-editable):
    ///   Assets/AirCity/Vehicles/&lt;Model&gt;/&lt;Model&gt;.prefab   model + Vehicle + VehicleLights (buildings: lights only)
    ///   Assets/AirCity/Vehicles/Materials                     the light glow and the snow / clippings spray
    ///   AirCity ▸ Vehicles ▸ Create Missing Prefabs
    /// </summary>
    public static class VehicleSetup
    {
        public const string Root = "Assets/AirCity/Vehicles";
        const string Materials = Root + "/Materials";
        const string Shaders = "Assets/AirCity/Shaders";

        [MenuItem("AirCity/Vehicles/Create Missing Prefabs")]
        public static void CreateMissing()
        {
            AssetDatabase.Refresh();
            EnsureFolder(Materials);
            var glow = LoadOrCreateMaterial("VehicleLightGlow", () => new Material(AssetDatabase.LoadAssetAtPath<Shader>($"{Shaders}/VehicleLightGlow.shader")));
            var spray = LoadOrCreateMaterial("Spray", CreateSprayMaterial);
            foreach (var model in Models)
            {
                var type = Type(model);
                if (type == null)
                {
                    Debug.LogWarning($"VehicleSetup: no {Root}/{model}/{model}.vehicle (run ArtSource/Vehicles/build.py -- --model {model})");
                    continue;
                }
                string path = PrefabPath(model);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) continue;
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{model}/Models/{model}.fbx");
                if (fbx == null)
                {
                    Debug.LogWarning($"VehicleSetup: missing {model}.fbx");
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                go.name = model;
                var lights = go.AddComponent<VehicleLights>();
                lights.type = type;
                lights.glowMaterial = glow;
                if (!type.IsBuilding)
                {
                    var v = go.AddComponent<Vehicle>();
                    v.type = type;
                    v.sprayMaterial = spray;
                    v.FindParts();
                    lights.flood = false;
                }
                else
                {
                    lights.headlights = lights.work = false;
                    // a tower's aerodrome beacon turns from dusk to dawn, its obstruction lights with it
                    lights.beacon = lights.beaconOnlyAtNight = type.kind == VehicleKind.Tower;
                }
                PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
                Debug.Log($"VehicleSetup: created {path}");
            }
            AssetDatabase.SaveAssets();
        }

        public static readonly string[] Models = { "SnowPlough", "Mower", "Depot", "TowerSmall", "TowerMedium", "TowerLarge", "TugSmall", "TugLarge" };
        /// <summary>The control towers, smallest first: a grass airfield's, a regional airport's, a hub's.</summary>
        public static readonly string[] Towers = { "TowerSmall", "TowerMedium", "TowerLarge" };

        public static string PrefabPath(string model) => $"{Root}/{model}/{model}.prefab";
        public static GameObject Prefab(string model) => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(model));
        public static VehicleType Type(string model) => AssetDatabase.LoadAssetAtPath<VehicleType>($"{Root}/{model}/{model}.vehicle");

        /// <summary>Give a depot its vehicle types, prefabs and building, and its tugs.</summary>
        public static void Assign(MaintenanceDepot depot)
        {
            depot.SetAssets(Type("SnowPlough"), Type("Mower"), Type("Depot"), Prefab("SnowPlough"), Prefab("Mower"), Prefab("Depot"));
            depot.SetTugAssets(Type("TugSmall"), Type("TugLarge"), Prefab("TugSmall"), Prefab("TugLarge"));
        }

        static Material LoadOrCreateMaterial(string name, System.Func<Material> make)
        {
            string path = $"{Materials}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = make();
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Soft round particles (URP Particles/Unlit, alpha blended) for the snow spray and clippings; Vehicle
        /// dims them at night.</summary>
        static Material CreateSprayMaterial()
        {
            string texPath = $"{Materials}/SoftParticle.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
            {
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                    }
                File.WriteAllBytes(texPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(texPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = new Material(shader);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            // transparent, alpha blended
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
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
