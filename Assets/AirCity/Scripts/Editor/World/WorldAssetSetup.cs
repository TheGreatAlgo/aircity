using System;
using System.IO;
using AirCity.EditorTools.Airfield;
using AirCity.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.World
{
    public static class WorldAssetSetup
    {
        public const string Root = "Assets/AirCity/World";
        public const string TreesPath = Root + "/Vegetation/ForestTrees.asset";
        public const string RuralPath = Root + "/Roads/RuralRoad.asset";
        public const string AccessPath = Root + "/Roads/AirportAccessRoad.asset";
        public const string SignalPath = Root + "/Roads/TrafficSignal.prefab";

        [MenuItem("AirCity/World/Regenerate Trees and Public Road Assets")]
        public static void Rebuild() { Ensure(true); AssetDatabase.SaveAssets(); }

        public static void Ensure(bool rebuildTrees = false)
        {
            Directory.CreateDirectory(Root + "/Vegetation/Models"); Directory.CreateDirectory(Root + "/Roads/Materials");
            AssetDatabase.Refresh(); AirfieldSetup.CreateMaterials();
            Trees(rebuildTrees); Road("RuralRoad", "Rural two-lane road", 1.2f); Road("AirportAccessRoad", "Airport access road", .8f);
            Signal(); AssetDatabase.SaveAssets();
        }

        static void Trees(bool rebuild)
        {
            var library = AssetDatabase.LoadAssetAtPath<TreeLibrary>(TreesPath);
            if (library == null) { library = ScriptableObject.CreateInstance<TreeLibrary>(); AssetDatabase.CreateAsset(library, TreesPath); }
            rebuild |= library.generatorVersion != 3;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Vegetation/Textures/ConiferBranches.png");
            if (texture == null) throw new InvalidOperationException("The generated conifer branch atlas is missing.");
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer != null && (!importer.alphaIsTransparency || !importer.mipMapsPreserveCoverage))
            {
                importer.alphaIsTransparency = true; importer.mipmapEnabled = true; importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = .38f; importer.maxTextureSize = 2048; importer.npotScale = TextureImporterNPOTScale.ToLarger;
                importer.anisoLevel = 4; importer.SaveAndReimport();
            }
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/AirCity/Shaders/Vegetation.shader");
            library.bark = VegetationMaterial("Bark", shader, false, texture);
            library.foliage = VegetationMaterial("Needles", shader, true, texture);
            bool missing = library.variants == null || library.variants.Length != 6;
            if (missing) library.variants = new TreeVariant[6];
            for (int variant = 0; variant < 6; variant++)
            {
                library.variants[variant] ??= new TreeVariant { name = (variant < 3 ? "Spruce " : "Pine ") + (variant % 3 + 1), height = 24f };
                var tree = library.variants[variant];
                for (int level = 0; level < 3; level++)
                {
                    if (!rebuild && tree.levels[level] != null) continue;
                    var mesh = TreeMeshGenerator.Build(variant, level);
                    if (tree.levels[level] != null) { EditorUtility.CopySerialized(mesh, tree.levels[level]); Object.DestroyImmediate(mesh); }
                    else { tree.levels[level] = mesh; AssetDatabase.AddObjectToAsset(mesh, library); }
                }
            }
            if (rebuild || library.distantFoliage == null)
            {
                var atlas = TreeImpostorBaker.Bake(library);
                library.distantFoliage = VegetationMaterial("DistantNeedles", shader, true, atlas);
                library.distantFoliage.SetTexture("_BaseMap", atlas); library.distantFoliage.SetColor("_Tint", Color.white);
                library.distantFoliage.SetFloat("_Cutoff", .3f); EditorUtility.SetDirty(library.distantFoliage);
                for (int i = 0; i < library.variants.Length; i++)
                {
                    var mesh = TreeImpostorBaker.Mesh(i);
                    EditorUtility.CopySerialized(mesh, library.variants[i].levels[2]); Object.DestroyImmediate(mesh);
                }
                library.generatorVersion = 3;
            }
            foreach (var tree in library.variants)
            {
                string prefab = Root + "/Vegetation/Models/" + tree.name.Replace(" ", "") + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null || rebuild)
                {
                    var root = new GameObject(tree.name); var lods = new LOD[3];
                    for (int level = 0; level < 3; level++)
                    {
                        var go = new GameObject("LOD" + level); go.transform.SetParent(root.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = tree.levels[level];
                        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { library.bark, level == 2 ? library.distantFoliage : library.foliage };
                        lods[level] = new LOD(level == 0 ? .13f : level == 1 ? .035f : .001f, new Renderer[] { renderer });
                    }
                    var group = root.AddComponent<LODGroup>(); group.SetLODs(lods); group.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root, prefab); Object.DestroyImmediate(root);
                }
            }
            EditorUtility.SetDirty(library);
        }

        static Material VegetationMaterial(string name, Shader shader, bool foliage, Texture2D texture)
        {
            string path = Root + "/Vegetation/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(shader) { name = name, enableInstancing = true };
            mat.SetFloat("_Foliage", foliage ? 1f : 0f);
            mat.SetFloat("_Cutoff", .38f); mat.SetFloat("_Wind", .16f);
            mat.SetColor("_Tint", foliage ? new Color(.68f, .87f, .76f) : new Color(.34f, .27f, .20f));
            if (foliage) { mat.SetTexture("_BaseMap", texture); mat.EnableKeyword("_FOLIAGE"); } else mat.DisableKeyword("_FOLIAGE");
            AssetDatabase.CreateAsset(mat, path); return mat;
        }

        static PublicRoadProfile Road(string name, string display, float shoulder)
        {
            string path = Root + "/Roads/" + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<PublicRoadProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PublicRoadProfile>(); profile.displayName = display;
                profile.pavementWidth = 6.5f; profile.shoulderWidth = shoulder; profile.crown = .065f;
                profile.asphalt = CopyMaterial("PublicRoadAsphalt", AirfieldSetup.Material("Asphalt"));
                profile.shoulder = CopyMaterial("GravelShoulder", AirfieldSetup.Material("Dirt"));
                profile.markings = CopyMaterial("PublicRoadMarkings", AirfieldSetup.Material("Paint"));
                AssetDatabase.CreateAsset(profile, path);
            }
            if (profile.medianGrass == null) { profile.medianGrass = MedianGrassMaterial(); EditorUtility.SetDirty(profile); }
            return profile;
        }

        static Material CopyMaterial(string name, Material source)
        {
            string path = Root + "/Roads/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(source) { name = name }; AssetDatabase.CreateAsset(material, path); }
            return material;
        }

        public static Material SolidMaterial(string name, Color color, bool unlit = false)
        {
            string path = Root + "/Roads/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color); mat.SetFloat("_Smoothness", .25f); AssetDatabase.CreateAsset(mat, path); return mat;
        }

        /// <summary>The grass on raised medians: the airfield's grass surface (texture, clumps and dry patches), a
        /// little darker and lusher than mown airfield grass. An older plain-green material is upgraded in place.</summary>
        public static Material MedianGrassMaterial()
        {
            string path = Root + "/Roads/Materials/MedianGrass.mat";
            var grass = AirfieldSetup.Material("Grass");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (grass == null) return mat != null ? mat : SolidMaterial("MedianGrass", new Color(.33f, .45f, .22f));
            if (mat != null && mat.shader == grass.shader) return mat;
            if (mat == null) { mat = new Material(grass) { name = "MedianGrass" }; AssetDatabase.CreateAsset(mat, path); }
            else { mat.shader = grass.shader; mat.CopyPropertiesFromMaterial(grass); mat.name = "MedianGrass"; }
            mat.SetColor("_Tint", new Color(.9f, .96f, .86f)); mat.SetFloat("_MacroStrength", .2f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Kerbs and median barriers: light concrete (their meshes are two-sided).</summary>
        public static Material KerbMaterial()
        {
            string path = Root + "/Roads/Materials/RoadKerb.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "RoadKerb" };
            mat.SetColor("_BaseColor", new Color(.6f, .6f, .58f)); mat.SetFloat("_Smoothness", .15f);
            AssetDatabase.CreateAsset(mat, path); return mat;
        }

        static void Signal()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SignalPath) != null) return;
            var root = new GameObject("Public traffic signal");
            var metal = SolidMaterial("SignalPole", new Color(.36f, .37f, .35f));
            var housing = SolidMaterial("SignalHousing", new Color(.055f, .06f, .055f));
            var lens = SolidMaterial("SignalLens", Color.white, true);
            void Part(string name, Vector3 p, Vector3 scale, Material mat)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root.transform, false);
                go.transform.localPosition = p; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = mat;
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }
            Part("Galvanised pole", new Vector3(0f, 2.6f, 0f), new Vector3(.14f, 5.2f, .14f), metal);
            Part("Signal case", new Vector3(0f, 4.55f, .18f), new Vector3(.38f, 1.18f, .27f), housing);
            var component = root.AddComponent<RoadSignalHead>();
            Renderer Lens(string name, float height)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0f, height, .335f); go.transform.localScale = new Vector3(.22f, .22f, .045f);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = lens;
                Part(name + " visor", new Vector3(0f, height + .13f, .36f), new Vector3(.29f, .035f, .22f), housing);
                return renderer;
            }
            component.red = Lens("Red", 4.9f); component.amber = Lens("Amber", 4.55f); component.green = Lens("Green", 4.2f);
            PrefabUtility.SaveAsPrefabAsset(root, SignalPath); Object.DestroyImmediate(root);
        }

        public static void FromCommandLine()
        {
            try { Rebuild(); Debug.Log("World assets regenerated."); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
