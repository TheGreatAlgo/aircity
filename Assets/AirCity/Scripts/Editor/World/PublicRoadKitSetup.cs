using System.IO;
using System.Linq;
using AirCity.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.World
{
    public static class PublicRoadKitSetup
    {
        public const string Folder = WorldAssetSetup.Root + "/Roads/Models";
        public static string PrefabPath(string name) => Folder + "/" + name + ".prefab";

        [MenuItem("AirCity/World/Create Public Road Model Kit")]
        public static void Create() => Create(false);

        public static void Create(bool force)
        {
            WorldAssetSetup.Ensure(); Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            foreach (string name in new[] { "RuralStraight", "UrbanCorner", "SignalTjunction", "SignalCrossroads", "HighwayOverpass" })
            {
                if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name)) != null) continue;
                var graph = Layout(name);
                var root = CreateModel(name, graph);
                // Persist generated meshes as subassets; runtime scene meshes remain disposable.
                string path = Folder + "/" + name + ".asset";
                var filters = root.GetComponentsInChildren<MeshFilter>().Where(f => f.transform.parent == root.transform).ToArray();
                for (int i = 0; i < filters.Length; i++)
                {
                    var mesh = filters[i].sharedMesh; mesh.hideFlags = HideFlags.None;
                    var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => m.name == mesh.name);
                    if (existing != null)
                    {
                        EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); filters[i].sharedMesh = existing;
                    }
                    else if (i == 0 && AssetDatabase.LoadMainAssetAtPath(path) == null) AssetDatabase.CreateAsset(mesh, path);
                    else AssetDatabase.AddObjectToAsset(mesh, path);
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name)); Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        public static PublicRoadNetwork Layout(string name)
        {
            var graph = new PublicRoadNetwork();
            int Node(float x, float y, float z, string terminal = null) => graph.AddNode(new Vector3(x, y, z), terminal);
            void Edge(int a, int b, PublicRoadStyle style = PublicRoadStyle.Urban, RoadStructure structure = RoadStructure.Ground) => graph.AddSegment(a, b, style, structure);
            if (name == "RuralStraight") Edge(Node(0f, 0f, -30f), Node(0f, 0f, 30f), PublicRoadStyle.Rural);
            else if (name == "UrbanCorner")
            {
                int c = Node(0f, 0f, 0f); Edge(Node(-35f, 0f, 0f), c); Edge(c, Node(0f, 0f, 35f));
            }
            else if (name == "HighwayOverpass")
            {
                // Two disjoint routes at the same x/z crossing, with an explicit ramp to the upper entrance.
                Edge(Node(-65f, 0f, 0f), Node(65f, 0f, 0f));
                int upperSouth = Node(0f, 8f, -65f), upperNorth = Node(0f, 8f, 65f, "terminal-departures");
                Edge(upperSouth, upperNorth, PublicRoadStyle.Highway, RoadStructure.Elevated);
                int rampStart = Node(40f, 0f, -190f), rampMid = Node(25f, 4f, -128f);
                Edge(rampStart, rampMid, PublicRoadStyle.Ramp, RoadStructure.Elevated);
                Edge(rampMid, upperSouth, PublicRoadStyle.Ramp, RoadStructure.Elevated);
            }
            else
            {
                int c = Node(0f, 0f, 0f); graph.Node(c).control = JunctionControl.TrafficLights;
                Edge(Node(0f, 0f, -45f), c); Edge(c, Node(0f, 0f, 45f)); Edge(Node(-45f, 0f, 0f), c);
                if (name == "SignalCrossroads") Edge(c, Node(45f, 0f, 0f, "terminal-arrivals"));
                else graph.Node(graph.nodes.Find(n => n.position.x < -40f).id).terminalEntrance = "terminal-arrivals";
            }
            return graph;
        }

        public static GameObject CreateModel(string name, PublicRoadNetwork graph)
        {
            var rural = AssetDatabase.LoadAssetAtPath<PublicRoadProfile>(WorldAssetSetup.RuralPath);
            var access = AssetDatabase.LoadAssetAtPath<PublicRoadProfile>(WorldAssetSetup.AccessPath);
            var geometry = new PublicRoadGeometry(graph, rural, access, _ => 0f);
            var root = new GameObject(name); root.AddComponent<PublicRoadModel>().network = graph;
            void Part(string label, Mesh mesh, Material material)
            {
                var go = new GameObject(label); go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            Part("Asphalt", geometry.pavement, rural.asphalt); Part("Shoulders", geometry.shoulders, rural.shoulder);
            Part("Lanes", geometry.markings, rural.markings);
            Part("Structures", geometry.structures, WorldAssetSetup.SolidMaterial("BridgeConcrete", new Color(.43f, .45f, .43f)));
            Part("Kerbs", geometry.kerbMesh, WorldAssetSetup.KerbMaterial());
            Part("Median islands", geometry.medianGrass, WorldAssetSetup.MedianGrassMaterial());
            root.AddComponent<PublicRoadSignalsView>().Build(graph, AssetDatabase.LoadAssetAtPath<GameObject>(WorldAssetSetup.SignalPath), geometry.StopLine);
            return root;
        }
    }
}
