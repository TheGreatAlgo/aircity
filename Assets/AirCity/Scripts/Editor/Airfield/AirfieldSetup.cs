using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>
    /// Airfield materials (from the generated textures) and the sandbox airport with its maintenance depot.
    ///   AirCity ▸ Airfield ▸ Create Materials                 (missing ones only; hand-tuning is kept)
    ///   AirCity ▸ Airfield ▸ Add Sandbox Airport              (replaces the sandbox's Airport object)
    /// Batch: -executeMethod AirCity.EditorTools.Airfield.AirfieldSetup.Bootstrap
    /// </summary>
    public static class AirfieldSetup
    {
        public const string Root = "Assets/AirCity/Airfield";
        public const string MaterialsFolder = Root + "/Materials";
        const string Textures = Root + "/Textures";
        const string Shaders = "Assets/AirCity/Shaders";

        public static void Bootstrap()
        {
            CreateMaterials();
            VehicleSetup.CreateMissing();
            AddSandboxAirport();
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Surface look per material: texture set, worn set, tile size (metres, from the scans), joints.</summary>
        struct SurfaceLook
        {
            public string set, worn;
            public float tile, smoothness, macroStrength, porosity, rubber, puddles;
            public Color rubberColor;
            public Vector2 joints;
            public float jointWidth, jointDarkness, slabVariation;
            public bool fringe, grassLength;
        }

        static readonly (string name, SurfaceLook look)[] Surfaces =
        {
            ("Concrete", new SurfaceLook { set = "Concrete", worn = "ConcreteWorn", tile = 2.5f, smoothness = 0.45f, macroStrength = 0.07f,
                porosity = 0.35f, rubber = 0.9f, puddles = 1f, joints = new Vector2(7.5f, 7.5f), jointWidth = 0.025f, jointDarkness = 0.5f, slabVariation = 0.03f }),
            ("Asphalt", new SurfaceLook { set = "Asphalt", worn = "AsphaltWorn", tile = 2.1f, smoothness = 0.4f, macroStrength = 0.08f,
                porosity = 0.25f, rubber = 0.6f, puddles = 1f, joints = new Vector2(5f, 0f), jointWidth = 0.06f, jointDarkness = 0.12f, slabVariation = 0.03f }),
            ("Dirt", new SurfaceLook { set = "Dirt", worn = "DirtWorn", tile = 2f, smoothness = 0.3f, macroStrength = 0.14f,
                porosity = 0.5f, rubber = 0.6f, rubberColor = new Color(0.2f, 0.16f, 0.12f), puddles = 0.7f, fringe = true }),
            ("Grass", new SurfaceLook { set = "Grass", worn = "Grass", tile = 2.5f, smoothness = 0.25f, macroStrength = 0.12f,
                porosity = 0.3f, puddles = 0.15f }),
            // grass runways and taxiways: worn turf shows the dirt beneath, mown grass shows stripes
            ("GrassStrip", new SurfaceLook { set = "Grass", worn = "Dirt", tile = 2.5f, smoothness = 0.25f, macroStrength = 0.12f,
                porosity = 0.3f, rubber = 0.35f, rubberColor = new Color(0.22f, 0.2f, 0.13f), puddles = 0.2f, fringe = true,
                grassLength = true }),
        };

        [MenuItem("AirCity/Airfield/Create Materials")]
        public static void CreateMaterials()
        {
            EnsureFolder(MaterialsFolder);
            AssetDatabase.Refresh();
            foreach (var (name, look) in Surfaces)
            {
                var mat = LoadOrCreate(name, "AirfieldSurface.shader", out bool created);
                if (!created) continue;
                mat.SetTexture("_BaseMap", Tex($"{look.set}_Albedo"));
                mat.SetTexture("_NormalMap", Tex($"{look.set}_Normal"));
                mat.SetTexture("_MaskMap", Tex($"{look.set}_Mask"));
                mat.SetTexture("_WornMap", Tex($"{look.worn}_Albedo"));
                mat.SetTexture("_WornNormalMap", Tex($"{look.worn}_Normal"));
                mat.SetTexture("_WornMaskMap", Tex($"{look.worn}_Mask"));
                mat.SetTexture("_MacroMap", Tex("Macro"));
                mat.SetFloat("_TileSize", look.tile);
                mat.SetFloat("_Smoothness", look.smoothness);
                mat.SetFloat("_MacroStrength", look.macroStrength);
                mat.SetFloat("_Porosity", look.porosity);
                mat.SetFloat("_Rubber", look.rubber);
                mat.SetColor("_RubberColor", look.rubberColor.a > 0f ? look.rubberColor : new Color(0.03f, 0.03f, 0.03f));
                mat.SetFloat("_Puddles", look.puddles);
                mat.SetVector("_JointSpacing", look.joints);
                mat.SetFloat("_JointWidth", look.jointWidth);
                mat.SetFloat("_JointDarkness", look.jointDarkness);
                mat.SetFloat("_SlabVariation", look.slabVariation);
                mat.SetFloat("_Fringe", look.fringe ? 1f : 0f);
                if (look.fringe) mat.EnableKeyword("_FRINGE");
                mat.SetFloat("_GrassLength", look.grassLength ? 1f : 0f);
                if (look.grassLength) mat.EnableKeyword("_GRASS_LENGTH");
                EditorUtility.SetDirty(mat);
            }
            var paint = LoadOrCreate("Paint", "AirfieldPaint.shader", out bool newPaint);
            if (newPaint)
            {
                paint.SetTexture("_GlyphMap", Tex("RunwayGlyphs"));
                paint.SetTexture("_MacroMap", Tex("Macro"));
                EditorUtility.SetDirty(paint);
            }
            LoadOrCreate("LightGlow", "AirfieldLightGlow.shader", out _);
            LoadOrCreate("Fixture", "AirfieldFixture.shader", out _);
            AssetDatabase.SaveAssets();
        }

        public static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");

        static Material LoadOrCreate(string name, string shaderFile, out bool created)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            created = mat == null;
            if (mat != null) return mat;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>($"{Shaders}/{shaderFile}");
            mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Texture2D Tex(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{name}.png");
            if (t == null) Debug.LogError($"AirfieldSetup: missing {Textures}/{name}.png (run ArtSource/Airfield/textures.py)");
            return t;
        }

        /// <summary>Give an Airport the shared materials.</summary>
        public static void AssignMaterials(Airport a)
        {
            a.concrete = Material("Concrete");
            a.asphalt = Material("Asphalt");
            a.dirt = Material("Dirt");
            a.grass = Material("Grass");
            a.grassStrip = Material("GrassStrip");
            a.paint = Material("Paint");
            a.lightGlow = Material("LightGlow");
            a.fixture = Material("Fixture");
        }

        // ------------------------------------------------------------------ sandbox

        /// <summary>
        /// The sandbox airport, around the parked aircraft: a 2,800 m concrete runway 09/27 with precision
        /// lighting and a parallel taxiway, a 1,600 m asphalt runway 13/31 crossed by a taxiway, a 600 m dirt
        /// strip and a 700 m grass strip (overdue for mowing) for light aircraft, on grass, with mown verges of
        /// various lengths; and west of the apron the maintenance depot (three snow ploughs and a mower inside) on
        /// a small pad, with a service road to the apron.
        /// </summary>
        [MenuItem("AirCity/Airfield/Add Sandbox Airport")]
        public static void AddSandboxAirport()
        {
            CreateMaterials();
            var scene = EditorSceneManager.OpenScene(AircraftSetup.SandboxScene, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Airport" || root.name == "Apron") Object.DestroyImmediate(root);

            var airport = BuildSandboxAirport();
            var cam = Camera.main;
            if (cam != null)
            {
                cam.farClipPlane = 12000f;
                var orbit = cam.GetComponent<DevOrbitCamera>();
                if (orbit != null) orbit.distanceLimits = new Vector2(4f, 5000f);
                if (cam.GetComponent<DevToolbar>() == null) cam.gameObject.AddComponent<DevToolbar>();
                var tool = cam.GetComponent<DevAirfieldTool>();
                if (tool == null) tool = cam.gameObject.AddComponent<DevAirfieldTool>();
                tool.airport = airport;
                tool.ploughType = VehicleSetup.Type("SnowPlough");
                tool.mowerType = VehicleSetup.Type("Mower");
                tool.depotType = VehicleSetup.Type("Depot");
                tool.ploughPrefab = VehicleSetup.Prefab("SnowPlough");
                tool.mowerPrefab = VehicleSetup.Prefab("Mower");
                tool.depotPrefab = VehicleSetup.Prefab("Depot");
            }
            EditorSceneManager.SaveScene(scene);
            Debug.Log("AirfieldSetup: sandbox airport added");
        }

        public static Airport BuildSandboxAirport()
        {
            var go = new GameObject("Airport");
            go.SetActive(false);
            var airport = go.AddComponent<Airport>();
            AssignMaterials(airport);
            airport.groundSize = 20000f;
            airport.aprons.Add(new ApronArea { centre = new Vector3(0f, 0f, 55f), size = new Vector2(240f, 180f), surface = PavementSurface.Concrete });
            // a pad in front of the depot's doors (room to turn out of the doors onto the road and back in)
            airport.aprons.Add(new ApronArea { centre = new Vector3(-195f, 0f, 52f), size = new Vector2(40f, 56f), surface = PavementSurface.Concrete });
            go.SetActive(true);

            var main = airport.AddRunway(new Vector3(-1400f, 0f, 480f), new Vector3(1400f, 0f, 480f), PavementSurface.Concrete, 45f,
                RunwayLighting.Precision);
            var cross = airport.AddRunway(new Vector3(900f, 0f, 1100f), new Vector3(900f, 0f, 1100f) + Heading(130f) * 1600f,
                PavementSurface.Asphalt, 30f, RunwayLighting.Basic);
            var strip = airport.AddRunway(new Vector3(-900f, 0f, -300f), new Vector3(-900f, 0f, -300f) + Heading(40f) * 600f,
                PavementSurface.Dirt, 18f);

            var net = airport.Taxiways;
            const float w = 15f;
            var asphalt = PavementSurface.Asphalt;
            int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0f, z), on);
            void Line(float w2, PavementSurface s, bool lit, params int[] ids)
            {
                for (int i = 0; i < ids.Length - 1; i++) net.Connect(ids[i], ids[i + 1], w2, s, lit);
            }

            // parallel taxiway with entrances at both thresholds and three exits
            int west = Node(-1340f, 330f), east = Node(1340f, 330f);
            int wEntry = Node(-1385f, 480f, main), eEntry = Node(1385f, 480f, main);
            int x1 = Node(-500f, 330f), x2 = Node(-60f, 330f), x3 = Node(60f, 330f), hs = Node(140f, 330f), x4 = Node(600f, 330f);
            Line(w, asphalt, true, wEntry, Node(-1385f, 400f), west, x1, x2, x3, hs, x4, east, Node(1385f, 400f), eEntry);
            Line(w, asphalt, true, x1, Node(-500f, 480f, main));
            Line(w, asphalt, true, x4, Node(600f, 480f, main));
            // a high-speed exit for aircraft landing on 09, angled 30 degrees
            Line(w, asphalt, true, Node(-120f, 480f, main), hs);
            // apron links
            Line(w, PavementSurface.Concrete, true, x2, Node(-60f, 140f));
            Line(w, PavementSurface.Concrete, true, x3, Node(60f, 140f));
            // east: across runway 13/31 (Connect adds the crossing node)
            Line(w, asphalt, true, east, Node(2050f, 330f));
            // dirt taxiway from the strip's north-east end to the apron
            strip.wear = 0.35f;     // a well-used strip: tyre tracks and ruts
            cross.wear = 0.3f;
            var stripExit = strip.end - Heading(40f) * 15f;
            Line(10.5f, PavementSurface.Dirt, false, Node(stripExit.x, stripExit.z, strip), Node(-300f, 120f), Node(-115f, 120f));

            // a grass strip 06/24 south-west of the apron, grown long, with a grass taxiway to the apron
            var grassStrip = airport.AddRunway(new Vector3(-750f, 0f, -550f), new Vector3(-750f, 0f, -550f) + Heading(60f) * 700f,
                PavementSurface.Grass, 30f);
            grassStrip.grass = 19f;
            var grassExit = grassStrip.start + Heading(60f) * 640f;
            foreach (var e in net.Connect(Node(grassExit.x, grassExit.z, grassStrip), Node(-100f, -110f), 10.5f, PavementSurface.Grass))
                e.grass = 19f;
            foreach (var e in net.Connect(net.NearestNode(new Vector3(-100f, 0f, -110f), 1f).id, Node(-100f, -35f), 10.5f, PavementSurface.Grass))
                e.grass = 19f;

            // service roads from the depot's pad: east to the apron, and north across the dirt taxiway (give way)
            // to the parallel taxiway
            net.Connect(Node(-180f, 52f), Node(-119f, 52f), RunwayRules.RoadWidth, asphalt, false, true);
            net.Connect(Node(-195f, 78f), net.SplitEdge(net.edges.First(e => e.a == x1 && e.b == x2 || e.a == x2 && e.b == x1), new Vector3(-195f, 0f, 330f)),
                RunwayRules.RoadWidth, asphalt, false, true);

            // verges: the main runway's nearly due for mowing, the rest at various lengths
            main.vergeGrass = 14f;
            cross.vergeGrass = 9f;
            strip.vergeGrass = 11f;
            grassStrip.vergeGrass = 8f;
            foreach (var e in net.edges)
                if (e.HasVerges) e.vergeGrass = e.lit ? 12f : 7f;

            // the maintenance depot west of the apron, doors facing east onto its forecourt
            var depotGo = new GameObject("Depot");
            depotGo.SetActive(false);
            depotGo.transform.SetParent(go.transform, false);
            depotGo.transform.localPosition = new Vector3(-215f, 0f, 50f);
            depotGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var depot = depotGo.AddComponent<MaintenanceDepot>();
            VehicleSetup.Assign(depot);
            depot.snowPloughs = 3;
            depot.mowers = 1;
            depotGo.SetActive(true);

            airport.RunwaysChanged();
            return airport;
        }

        static Vector3 Heading(float bearing)
        {
            float r = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
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
