using AirCity.Characters;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Characters
{
    public static class RampCrewSetup
    {
        public const string Root = "Assets/AirCity/Characters/RampCrew";
        public static GameObject Prefab => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/RampCrew.prefab");
        public static RampCrewType Type => AssetDatabase.LoadAssetAtPath<RampCrewType>(Root + "/RampCrew.crew");
        public static GameObject Wand => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/MarshallingWand.prefab");
        [MenuItem("AirCity/Characters/Create Ramp Crew")]
        public static void CreateAssets()
        {
            var materials = CreateMaterials();
            CreateWand();
            if (Prefab != null)
            {
                var existing = PrefabUtility.LoadPrefabContents(Root + "/RampCrew.prefab");
                bool changed = RemoveImportedControllers(existing);
                if (changed) PrefabUtility.SaveAsPrefabAsset(existing,Root + "/RampCrew.prefab");
                PrefabUtility.UnloadPrefabContents(existing); AssetDatabase.SaveAssets(); return;
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/RampCrew.fbx");
            if (source == null) return;
            var go = new GameObject("RampCrew");
            var model = Object.Instantiate(source, go.transform); model.name = "Model";
            RemoveImportedControllers(go);
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var lod = go.AddComponent<LODGroup>(); var levels = new LOD[3];
            for (int i = 0; i < 3; i++)
            {
                var renderer = System.Array.Find(renderers, r => r.name.EndsWith("LOD" + i));
                renderer.sharedMaterial = materials[0];
                renderer.localBounds = new Bounds(new Vector3(0,.9f,0), new Vector3(2.5f,2.4f,2.5f));
                levels[i] = new LOD(i == 0 ? .08f : i == 1 ? .025f : .006f, new Renderer[] { renderer });
            }
            lod.SetLODs(levels); lod.RecalculateBounds();
            var rig = go.AddComponent<RampCrewRig>(); rig.type = Type; rig.uniforms = materials;
            PrefabUtility.SaveAsPrefabAsset(go, Root + "/RampCrew.prefab"); Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
        }

        // Also upgrade existing generated materials when the atlas gains fabric normals/roughness.
        static void CreateWand()
        {
            if (Wand != null) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/MarshallingWand.fbx");
            if (model == null) return;
            string path = Root + "/Materials/MarshallingWand.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Wand_Albedo.png"));
                material.SetTexture("_EmissionMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Wand_Emission.png"));
                material.SetColor("_EmissionColor",Color.white*2f); material.EnableKeyword("_EMISSION");
                AssetDatabase.CreateAsset(material,path);
            }
            var go = Object.Instantiate(model); go.name = "MarshallingWand";
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>()) renderer.sharedMaterial = material;
            foreach (var animator in go.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(animator);
            PrefabUtility.SaveAsPrefabAsset(go,Root + "/MarshallingWand.prefab"); Object.DestroyImmediate(go);
        }

        public static void Assign(AirCity.Airfield.Airport airport)
        {
            var presentation = airport.GetComponent<MarshallerPresentation>();
            if (presentation == null) presentation = airport.gameObject.AddComponent<MarshallerPresentation>();
            presentation.crewPrefab = Prefab; presentation.wandPrefab = Wand;
        }
        static Material[] CreateMaterials()
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
            var materials = new Material[4];
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/RampCrew_Normal.png");
            var surface = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/RampCrew_MetalGloss.png");
            for (int i = 0; i < materials.Length; i++)
            {
                string path = Root + $"/Materials/Uniform_{i}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(m, path);
                }
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + $"/Textures/RampCrew_{i}.png"));
                m.SetTexture("_MetallicGlossMap", surface);
                m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f);
                m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", .65f);
                if (normal != null) m.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(m); materials[i] = m;
            }
            return materials;
        }

        static bool RemoveImportedControllers(GameObject go)
        {
            bool changed = false;
            foreach (var group in go.GetComponentsInChildren<LODGroup>())
                if (group.gameObject != go) { Object.DestroyImmediate(group); changed = true; }
            foreach (var animator in go.GetComponentsInChildren<Animator>()) { Object.DestroyImmediate(animator); changed = true; }
            return changed;
        }

        public static void Assign(BaggageStation station) { station.crewPrefab = Prefab; station.crewType = Type; station.bagPropType = Vehicles.VehicleSetup.Type("BaggageProps"); }
        public static void Assign(ContainerStation station) { station.crewPrefab = Prefab; }

        [MenuItem("AirCity/Characters/Install Ramp Crew In Review Scenes")]
        public static void Install()
        {
            AssetDatabase.Refresh(); CreateAssets();
            foreach (var name in new[] { "Sandbox", "BaggageTurnaround", "ContainerTurnaround", "BaggageSandbox", "ContainerSandbox" })
            {
                string path = $"Assets/AirCity/Scenes/{name}.unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
                var scene = EditorSceneManager.OpenScene(path);
                bool changed = false;
                foreach (var airport in Object.FindObjectsByType<AirCity.Airfield.Airport>(FindObjectsSortMode.None))
                    if (airport.GetComponent<MarshallerPresentation>() == null || airport.GetComponent<MarshallerPresentation>().wandPrefab == null)
                    { Assign(airport); changed = true; }
                foreach (var station in Object.FindObjectsByType<BaggageStation>(FindObjectsSortMode.None))
                {
                    if (station.bagPropType == null) { station.bagPropType = Vehicles.VehicleSetup.Type("BaggageProps"); changed = true; }
                    if (station.crewPrefab == null) { station.crewPrefab = Prefab; changed = true; }
                    if (station.crewType == null) { station.crewType = Type; changed = true; }
                }
                foreach (var station in Object.FindObjectsByType<ContainerStation>(FindObjectsSortMode.None))
                    if (station.crewPrefab == null) { Assign(station); changed = true; }
                if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
        }
    }
}
