using System;
using System.Linq;
using AirCity.Airfield;
using AirCity.EditorTools.Dev;
using AirCity.EditorTools.World;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>
    /// Bridges laid out on an empty airport and captured from fixed cameras (Captures/RoadStructures), to check
    /// decks, parapets, verges and piers by eye: bends, wide decks, a road passing below, a service-road bridge.
    ///   Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Airfield.RoadStructureReview.FromCommandLine [-reviewOut folder]
    /// </summary>
    public static class RoadStructureReview
    {
        static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        static void Line(PublicRoadNetwork g, PublicRoadStyle style, RoadStructure structure, params Vector3[] points)
        {
            int prev = g.AddNode(points[0]);
            for (int i = 1; i < points.Length; i++)
            {
                int node = g.AddNode(points[i]);
                g.AddSegment(prev, node, style, structure);
                prev = node;
            }
        }

        /// <summary>A bridge of one road type: a ground approach, a straight, a bend, a straight, ending in the air.</summary>
        static PublicRoadNetwork Bend(PublicRoadStyle style, float height, float leg, float degrees)
        {
            var g = new PublicRoadNetwork();
            var d = Dir(degrees) * leg;
            int ground = g.AddNode(new Vector3(-leg - 60f, height, 0f)), a = g.AddNode(new Vector3(-leg, height, 0f)), b = g.AddNode(new Vector3(0f, height, 0f)),
                c = g.AddNode(new Vector3(d.x, height, d.y));
            g.AddSegment(ground, a, style, RoadStructure.Ground);
            g.AddSegment(a, b, style, RoadStructure.Elevated);
            g.AddSegment(b, c, style, RoadStructure.Elevated);
            return g;
        }

        public static readonly (string name, Func<PublicRoadNetwork> build)[] Cases =
        {
            ("street-bend", () => Bend(PublicRoadStyle.Urban, 9f, 110f, 55f)),
            ("country-bend", () => Bend(PublicRoadStyle.Rural, 9f, 120f, 40f)),
            ("avenue-bend", () => Bend(PublicRoadStyle.Avenue, 10f, 120f, 50f)),
            ("highway-over-street", () =>
            {
                var g = Bend(PublicRoadStyle.Highway, 12f, 240f, 35f);
                Line(g, PublicRoadStyle.Urban, RoadStructure.Ground, new Vector3(-120f, 0f, -90f), new Vector3(-120f, 0f, 90f));
                return g;
            }),
        };

        public static void FromCommandLine()
        {
            try
            {
                string folder = Environment.GetCommandLineArgs().SkipWhile(a => a != "-reviewOut").Skip(1).FirstOrDefault() ?? "Captures/RoadStructures";
                EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
                var source = Object.FindAnyObjectByType<Airport>();
                var camera = Camera.main;
                var go = new GameObject("Road structure review");
                go.SetActive(false);
                var airport = go.AddComponent<Airport>();
                airport.groundSize = 4000f;
                AirfieldSetup.AssignMaterials(airport);
                airport.aircraftTypes = source.aircraftTypes.ToList();
                go.transform.position = new Vector3(9000f, 0f, 9000f);
                go.SetActive(true);
                // a service-road bridge with a bend, over a taxiway
                var net = airport.Taxiways;
                int Node(float x, float y, float z) => net.AddNode(new Vector3(x, y, z));
                net.Connect(Node(-1000f, 0f, -160f), Node(-1000f, 0f, 160f), 23f, PavementSurface.Asphalt);
                int s0 = Node(-1130f, 8f, 0f), s1 = Node(-1060f, 8f, 0f), s2 = Node(-940f, 8f, 0f), s3 = Node(-880f, 8f, 70f);
                foreach (var (a, b) in new[] { (s0, s1), (s1, s2), (s2, s3) })
                    net.Connect(a, b, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service, false, RoadStructure.Elevated);
                net.Rebuild();
                void Shot(string name, Vector3 at, Vector3 from, float fov = 40f)
                {
                    camera.orthographic = false;
                    camera.fieldOfView = fov;
                    camera.nearClipPlane = .5f;
                    camera.farClipPlane = 3000f;
                    camera.transform.position = at + from;
                    camera.transform.LookAt(at);
                    SceneCapture.Capture(camera, $"{folder}/{name}.png", 1400, 900);
                }
                var concrete = new Material(Resources.Load<Shader>("RoadStructure")) { hideFlags = HideFlags.DontSave };
                for (int i = 0; i < Cases.Length; i++)
                {
                    var (name, build) = Cases[i];
                    var graph = build();
                    var model = PublicRoadKitSetup.CreateModel(name, graph);
                    // as the game draws them: the structure shader on the concrete, which alone casts shadows
                    foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                    {
                        renderer.shadowCastingMode = renderer.name == "Structures" ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                        if (renderer.name == "Structures") renderer.sharedMaterial = concrete;
                    }
                    var at = airport.transform.TransformPoint(new Vector3(i * 700f - 600f, 0f, 900f));
                    model.transform.position = at;
                    float height = graph.nodes.Max(n => n.position.y);
                    // the bend from above and outside (parapets, verge, shadow), then from below (soffit, piers)
                    Shot(name + "-1-above", at + new Vector3(0f, height, 10f), new Vector3(-38f, 16f, -34f));
                    Shot(name + "-2-below", at + new Vector3(-10f, height * .55f, 0f), new Vector3(48f, 3f - height * .55f, -52f), 50f);
                    Shot(name + "-3-along", at + new Vector3(-40f, height + 1f, 0f), new Vector3(-55f, 5f, 3f), 35f);
                    // the deck's side close up: edge beam, barrier, rail, concrete
                    Shot(name + "-4-side", at + new Vector3(-50f, height + .3f, 0f), new Vector3(-9f, 5f, -16f), 35f);
                }
                var bridge = airport.transform.TransformPoint(new Vector3(-1000f, 8f, 0f));
                Shot("service-bridge-1-above", bridge + new Vector3(40f, 0f, 10f), new Vector3(30f, 18f, -42f));
                Shot("service-bridge-2-below", bridge + new Vector3(0f, -4f, 0f), new Vector3(-50f, -1.5f, -45f), 50f);
                // how long each case's geometry takes to build (what placing or changing a bridge costs)
                var timing = new System.Text.StringBuilder();
                foreach (var (name, build) in Cases)
                {
                    var graph = build();
                    double best = double.MaxValue;
                    for (int run = 0; run < 12; run++)
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        var geometry = new PublicRoadGeometry(graph, null, null, _ => 0f);
                        best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
                        foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures, geometry.kerbMesh, geometry.medianGrass })
                            Object.DestroyImmediate(mesh);
                    }
                    timing.AppendLine($"{name,-24} {best,8:0.00} ms");
                }
                System.IO.File.WriteAllText($"{folder}/timing.txt", timing.ToString());
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
