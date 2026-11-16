using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Dev;
using AirCity.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>Bakes the same equipment references for both modes and a stable save-reference catalog.</summary>
    [InitializeOnLoad]
    public sealed class SharedGameAssets : IPreprocessBuildWithReport
    {
        const string Folder = "Assets/AirCity/Game/Resources";
        const string CatalogPath = Folder + "/AirCityGameAssets.asset";
        public int callbackOrder => 0;
        static SharedGameAssets()
        {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingEditMode && Object.FindObjectsByType<CareerGame>(FindObjectsInactive.Include).Length > 0) Ensure();
            };
        }
        public void OnPreprocessBuild(BuildReport report) => Ensure();

        [MenuItem("AirCity/Game/Update Shared Game Assets")]
        public static void Ensure()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<GameAssetCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            var active = SceneManager.GetActiveScene();
            const string sandboxPath = "Assets/AirCity/Scenes/Sandbox.unity";
            var sandbox = SceneManager.GetSceneByPath(sandboxPath);
            bool opened = !sandbox.isLoaded;
            if (opened) sandbox = EditorSceneManager.OpenScene(sandboxPath, OpenSceneMode.Additive);
            try
            {
                var source = sandbox.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<DevAirfieldTool>(true)).FirstOrDefault();
                if (source != null)
                {
                    var go = new GameObject("Shared building tool assets"); go.SetActive(false);
                    try
                    {
                        var tool = go.AddComponent<DevAirfieldTool>();
                        EditorUtility.CopySerialized(source, tool); tool.airport = null;
                        var sourceBridge = source.airport != null ? source.airport.GetComponent<AirCity.Airfield.BridgeRig>() : null;
                        if (sourceBridge != null) EditorUtility.CopySerialized(sourceBridge, go.AddComponent<AirCity.Airfield.BridgeRig>());
                        var sourceCrew = source.airport != null ? source.airport.GetComponent<AirCity.Characters.MarshallerPresentation>() : null;
                        if (sourceCrew != null) EditorUtility.CopySerialized(sourceCrew, go.AddComponent<AirCity.Characters.MarshallerPresentation>());
                        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Folder + "/GameToolDefaults.prefab");
                        catalog.toolDefaults = prefab.GetComponent<DevAirfieldTool>();
                        catalog.bridgeDefaults = prefab.GetComponent<AirCity.Airfield.BridgeRig>();
                        catalog.marshallerDefaults = prefab.GetComponent<AirCity.Characters.MarshallerPresentation>();
                    }
                    finally { Object.DestroyImmediate(go); }
                }
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(sandbox, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
            var entries = new List<GameAssetCatalog.Entry>(); var ids = new HashSet<UnityEngine.EntityId>();
            void Add(Object asset)
            {
                if (asset == null || asset == catalog || !ids.Add(asset.GetEntityId())) return;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long local)) return;
                entries.Add(new GameAssetCatalog.Entry { key = "asset:" + guid + ":" + local, asset = asset });
            }
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject t:ScriptableObject t:Material t:Shader", new[] { "Assets/AirCity" }))
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                {
                    if (asset is GameObject go)
                        foreach (var t in go.GetComponentsInChildren<Transform>(true)) { Add(t.gameObject); foreach (var c in t.GetComponents<Component>()) Add(c); }
                    else if (asset is ScriptableObject || asset is Material || asset is Shader) Add(asset);
                }
            catalog.entries = entries.OrderBy(e => e.key).ToList();
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        }
    }
}
