using UnityEditor;
using UnityEngine;
using AirCity.World;

namespace AirCity.EditorTools.World
{
    public static class ValleySurfaceSetup
    {
        public static void Assign(ValleyView view)
        {
            view.grassLayer = Layer("ValleyGrass", "Meadow", 3.5f, Vector4.one, true);
            view.dirtLayer = Layer("ValleyDirt", "Dirt", 4f, new Vector4(.9f, .82f, .7f, 1f));
            view.roadLayer = Layer("ValleyRoad", "Asphalt", 12f, Vector4.one);
            view.dryLayer = Layer("ValleyDryMeadow", "DryMeadow", 4.5f, Vector4.one, true);
            view.rockLayer = Layer("ValleyRock", "RockSurface", 4f, Vector4.one, true);
            view.forestLayer = Layer("ValleyForestFloor", "ForestFloor", 5f, Vector4.one, true);
        }
        static TerrainLayer Layer(string name, string texture, float metres, Vector4 tint, bool scanned = false)
        {
            string path = WorldAssetSetup.Root + "/" + name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            bool fresh = layer == null;
            if (fresh) { layer = new TerrainLayer { name = name, tileSize = Vector2.one * metres, diffuseRemapMax = tint }; AssetDatabase.CreateAsset(layer, path); }
            string root = (scanned ? "Assets/AirCity/World/Textures/" : "Assets/AirCity/Airfield/Textures/") + texture;
            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "_Albedo.png");
            bool changedSource = layer.diffuseTexture != diffuse;
            layer.diffuseTexture = diffuse;
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "_Normal.png");
            layer.maskMapTexture = scanned ? AssetDatabase.LoadAssetAtPath<Texture2D>(root + "_Mask.png") : null;
            layer.normalScale = scanned ? 1f : .65f; layer.smoothness = .08f;
            if (changedSource) { layer.tileSize = Vector2.one * metres; layer.diffuseRemapMin = Vector4.zero; layer.diffuseRemapMax = tint; }
            // Unity 6.6 defaults to diffuse-alpha smoothness. Our opaque albedos have alpha=1, which makes
            // land a mirror at grazing angles. Use the layer's matte constant instead (shader source mode 2).
            var serialized = new SerializedObject(layer);
            serialized.FindProperty("m_SmoothnessSource").intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // Upgrade the first career's unnormalled, very large grass/soil tiles once.
            if (name == "ValleyGrass" && layer.tileSize.x == 16f || name == "ValleyDirt" && layer.tileSize.x == 20f)
            { layer.tileSize = Vector2.one * metres; layer.diffuseRemapMax = tint; }
            EditorUtility.SetDirty(layer); return layer;
        }
    }
}
