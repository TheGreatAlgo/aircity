using System;
using System.Collections.Generic;
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
    /// Odd-angle road and taxiway junctions laid out on an empty airport and captured from above, one picture per
    /// case (Captures/RoadJunctions). Used to check junction geometry by eye: shallow joins, sharp bends on short
    /// sections, roads across taxiways at a slant, crowded junctions.
    ///   Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Airfield.RoadJunctionReview.FromCommandLine
    /// </summary>
    public static class RoadJunctionReview
    {
        const float Road = RunwayRules.RoadWidth;

        public readonly struct Case
        {
            public readonly string name;
            public readonly Action<TaxiwayNetwork, Vector2> build;
            public readonly float size;
            public Case(string name, float size, Action<TaxiwayNetwork, Vector2> build) { this.name = name; this.size = size; this.build = build; }
        }

        static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        static int Node(TaxiwayNetwork net, Vector2 p) => net.AddNode(new Vector3(p.x, 0f, p.y));

        static void Line(TaxiwayNetwork net, float width, RoadClass road, params Vector2[] points)
        {
            int prev = Node(net, points[0]);
            for (int i = 1; i < points.Length; i++)
            {
                int next = Node(net, points[i]);
                net.Connect(prev, next, width, PavementSurface.Asphalt, false, road);
                prev = next;
            }
        }

        static void RoadLine(TaxiwayNetwork net, params Vector2[] points) => Line(net, Road, RoadClass.Service, points);
        static void TaxiwayLine(TaxiwayNetwork net, float width, params Vector2[] points) => Line(net, width, RoadClass.None, points);

        /// <summary>A road through c from west to east, and another leaving c at `degrees` (joining from the side).</summary>
        static void Join(TaxiwayNetwork net, Vector2 c, float degrees, float arm = 80f)
        {
            RoadLine(net, c - Vector2.right * arm, c, c + Vector2.right * arm);
            int centre = net.NearestNode(new Vector3(c.x, 0f, c.y), 0.1f).id;
            int end = Node(net, c + Dir(degrees) * arm);
            net.Connect(centre, end, Road, PavementSurface.Asphalt, false, RoadClass.Service);
        }

        public static readonly Case[] Cases =
        {
            new Case("road-join-90", 60f, (n, c) => Join(n, c, 90f)),
            new Case("road-join-45", 60f, (n, c) => Join(n, c, 45f)),
            new Case("road-join-30", 60f, (n, c) => Join(n, c, 30f)),
            new Case("road-join-20", 60f, (n, c) => Join(n, c, 20f)),
            new Case("road-join-150", 60f, (n, c) => Join(n, c, 150f)),
            new Case("road-cross-25", 60f, (n, c) =>
            {
                RoadLine(n, c - Vector2.right * 80f, c + Vector2.right * 80f);
                RoadLine(n, c - Dir(25f) * 80f, c + Dir(25f) * 80f);
            }),
            new Case("road-zigzag-short", 45f, (n, c) =>
                RoadLine(n, c + new Vector2(-60f, 0f), c + new Vector2(-40f, 0f), c + new Vector2(-40f, 0f) + Dir(140f) * 20f,
                    c + new Vector2(-40f, 0f) + Dir(140f) * 20f + Vector2.right * 20f, c + new Vector2(10f, 18f), c + new Vector2(30f, 0f),
                    c + new Vector2(45f, 12f), c + new Vector2(60f, 0f))),
            new Case("road-slight-bends", 60f, (n, c) =>
                RoadLine(n, c + new Vector2(-90f, 0f), c + new Vector2(-45f, 0f), c + new Vector2(0f, 3.5f), c + new Vector2(45f, 0f),
                    c + new Vector2(90f, 8f))),
            new Case("road-across-taxiway-20", 70f, (n, c) =>
            {
                TaxiwayLine(n, 23f, c - Vector2.right * 120f, c + Vector2.right * 120f);
                RoadLine(n, c - Dir(20f) * 90f, c + Dir(20f) * 90f);
            }),
            new Case("road-meets-taxiway-30", 70f, (n, c) =>
            {
                TaxiwayLine(n, 23f, c - Vector2.right * 120f, c + Vector2.right * 120f);
                int on = n.SplitEdge(n.edges[n.edges.Count - 1], new Vector3(c.x, 0f, c.y));
                n.Connect(Node(n, c + Dir(30f) * 90f), on, Road, PavementSurface.Asphalt, false, RoadClass.Service);
            }),
            new Case("road-five-way", 50f, (n, c) =>
            {
                int centre = Node(n, c);
                foreach (float a in new[] { 0f, 25f, 120f, 200f, 280f })
                    n.Connect(centre, Node(n, c + Dir(a) * 60f), Road, PavementSurface.Asphalt, false, RoadClass.Service);
            }),
            new Case("road-close-crossings", 55f, (n, c) =>
            {
                RoadLine(n, c + new Vector2(-80f, -6f), c + new Vector2(80f, -6f));
                RoadLine(n, c + new Vector2(-80f, 6f), c + new Vector2(80f, 6f));
                RoadLine(n, c - Dir(35f) * 70f, c + Dir(35f) * 70f);
            }),
            new Case("road-near-junction", 55f, (n, c) =>
            {
                Join(n, c, 90f);
                RoadLine(n, c + new Vector2(9f, -60f), c + new Vector2(9f + 60f * 0.2f, 60f));
            }),
            new Case("taxiway-join-30", 160f, (n, c) =>
            {
                TaxiwayLine(n, 23f, c - Vector2.right * 300f, c, c + Vector2.right * 300f);
                int centre = n.NearestNode(new Vector3(c.x, 0f, c.y), 0.1f).id;
                n.Connect(centre, Node(n, c + Dir(30f) * 300f), 23f, PavementSurface.Asphalt);
            }),
        };

        /// <summary>Public (landside) road cases: the same odd angles on the PublicRoadNetwork, built by PublicRoadGeometry.</summary>
        public static readonly (string name, float size, Action<PublicRoadNetwork> build)[] PublicCases =
        {
            ("street-join-30", 50f, g => PublicJoin(g, 30f, PublicRoadStyle.Urban)),
            ("street-join-20", 50f, g => PublicJoin(g, 20f, PublicRoadStyle.Urban)),
            ("access-join-35-signals", 50f, g =>
            {
                int c = PublicJoin(g, 35f, PublicRoadStyle.Rural, PublicRoadStyle.AirportAccess);
                g.Node(c).control = JunctionControl.TrafficLights;
            }),
            ("street-bend-60", 45f, g => PublicLine(g, PublicRoadStyle.Rural, new Vector2(-70f, 0f), Vector2.zero, Dir(60f) * 70f)),
            ("street-bend-8", 45f, g => PublicLine(g, PublicRoadStyle.Rural, new Vector2(-70f, 0f), Vector2.zero, Dir(8f) * 70f)),
            ("highway-bend-10", 55f, g => PublicLine(g, PublicRoadStyle.Highway, new Vector2(-90f, 0f), Vector2.zero, Dir(10f) * 90f)),
            ("street-clicked-corners", 70f, g => PublicLine(g, PublicRoadStyle.AirportAccess, new Vector2(-90f, -20f), new Vector2(-60f, -20f),
                new Vector2(-45f, 5f), new Vector2(-20f, 10f), new Vector2(0f, -15f), new Vector2(14f, -12f), new Vector2(40f, 25f), new Vector2(90f, 25f))),
            ("street-cross-25", 50f, g =>
            {
                int c = g.AddNode(Vector3.zero);
                foreach (float a in new[] { 0f, 25f, 180f, 205f })
                    g.AddSegment(c, g.AddNode(new Vector3(Dir(a).x * 70f, 0f, Dir(a).y * 70f)), PublicRoadStyle.Urban, RoadStructure.Ground);
            }),
        };

        /// <summary>Road types meeting as a player would build them (Connect: crossings split, junction control by
        /// default), captured from above and at an angle.</summary>
        public static readonly (string name, float size, Action<PublicRoadNetwork> build)[] TypeCases =
        {
            ("street-crossroads", 45f, g => Cross(g, PublicRoadStyle.Urban, PublicRoadStyle.Urban, 90f)),
            ("avenue-crossroads", 55f, g => Cross(g, PublicRoadStyle.Avenue, PublicRoadStyle.Avenue, 90f)),
            ("boulevard-meets-street", 60f, g => Tee(g, PublicRoadStyle.Boulevard, PublicRoadStyle.Urban, 90f)),
            ("avenue-meets-street-60", 55f, g => Tee(g, PublicRoadStyle.Avenue, PublicRoadStyle.Urban, 60f)),
            ("country-T", 45f, g => Tee(g, PublicRoadStyle.Rural, PublicRoadStyle.Rural, 90f)),
            ("oneway-pair", 55f, g =>
            {
                // a two-lane one-way street crossing a two-way street, and a three-lane one-way leaving it
                Line(g, PublicRoadStyle.Urban, new Vector2(-90f, 0f), new Vector2(90f, 0f));
                Line(g, PublicRoadStyle.OneWay2, new Vector2(-30f, -80f), new Vector2(-30f, 80f));
                Line(g, PublicRoadStyle.OneWay3, new Vector2(40f, 0f), new Vector2(40f, -80f));
            }),
        };

        static void Line(PublicRoadNetwork g, PublicRoadStyle style, Vector2 from, Vector2 to)
        {
            int a = g.AddNode(new Vector3(from.x, 0f, from.y)), b = g.AddNode(new Vector3(to.x, 0f, to.y));
            if (!g.Connect(a, b, style, RoadStructure.Ground, out string why)) throw new InvalidOperationException(why);
        }

        static void Cross(PublicRoadNetwork g, PublicRoadStyle main, PublicRoadStyle side, float degrees)
        {
            Line(g, main, new Vector2(-90f, 0f), new Vector2(90f, 0f));
            Line(g, side, -Dir(degrees) * 90f, Dir(degrees) * 90f);
        }

        static void Tee(PublicRoadNetwork g, PublicRoadStyle main, PublicRoadStyle side, float degrees)
        {
            Line(g, main, new Vector2(-90f, 0f), new Vector2(90f, 0f));
            Line(g, side, Dir(degrees) * 90f, Vector2.zero);
        }

        static int PublicJoin(PublicRoadNetwork g, float degrees, PublicRoadStyle main, PublicRoadStyle? side = null)
        {
            int c = g.AddNode(Vector3.zero);
            g.AddSegment(g.AddNode(new Vector3(-80f, 0f, 0f)), c, main, RoadStructure.Ground);
            g.AddSegment(c, g.AddNode(new Vector3(80f, 0f, 0f)), main, RoadStructure.Ground);
            var d = Dir(degrees) * 80f;
            g.AddSegment(c, g.AddNode(new Vector3(d.x, 0f, d.y)), side ?? main, RoadStructure.Ground);
            return c;
        }

        static void PublicLine(PublicRoadNetwork g, PublicRoadStyle style, params Vector2[] points)
        {
            int prev = 0;
            foreach (var p in points)
            {
                int node = g.AddNode(new Vector3(p.x, 0f, p.y));
                if (prev != 0) g.AddSegment(prev, node, style, RoadStructure.Ground);
                prev = node;
            }
        }

        /// <summary>Build every case side by side on one airport (cases 400 m apart in a row of five).</summary>
        public static List<(Case c, Vector2 centre)> Build(TaxiwayNetwork net)
        {
            var placed = new List<(Case, Vector2)>();
            for (int i = 0; i < Cases.Length; i++)
            {
                var centre = new Vector2((i % 5) * 400f, (i / 5) * 400f);
                Cases[i].build(net, centre);
                placed.Add((Cases[i], centre));
            }
            return placed;
        }

        public static void FromCommandLine()
        {
            try
            {
                string folder = Environment.GetCommandLineArgs().SkipWhile(a => a != "-reviewOut").Skip(1).FirstOrDefault() ?? "Captures/RoadJunctions";
                EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
                var source = Object.FindAnyObjectByType<Airport>();
                var camera = Camera.main;
                var go = new GameObject("Road junction review");
                go.SetActive(false);
                var airport = go.AddComponent<Airport>();
                airport.groundSize = 3000f;
                AirfieldSetup.AssignMaterials(airport);
                airport.aircraftTypes = source.aircraftTypes.ToList();
                go.transform.position = new Vector3(9000f, 0f, 9000f);
                go.SetActive(true);
                var net = airport.Taxiways;
                var placed = Build(net);
                net.Rebuild();
                void Oblique(string name, float distance, Vector3 at)
                {
                    camera.orthographic = false;
                    camera.fieldOfView = 40f;
                    camera.farClipPlane = 3000f;
                    camera.transform.position = at + new Vector3(-.55f, .55f, -.65f).normalized * distance;
                    camera.transform.LookAt(at + Vector3.up * 2f);
                    SceneCapture.Capture(camera, $"{folder}/{name}.png", 1200, 900);
                }
                void Shoot(string name, float size, Vector3 at)
                {
                    camera.orthographic = true;
                    camera.orthographicSize = size;
                    camera.farClipPlane = 1500f;
                    camera.transform.position = at + Vector3.up * 300f;
                    camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    SceneCapture.Capture(camera, $"{folder}/{name}.png", 1200, 900);
                }
                foreach (var (c, centre) in placed)
                    Shoot(c.name, c.size, airport.transform.TransformPoint(new Vector3(centre.x, 0f, centre.y)));
                for (int i = 0; i < TypeCases.Length; i++)
                {
                    var (name, size, build) = TypeCases[i];
                    var graph = new PublicRoadNetwork();
                    build(graph);
                    var model = PublicRoadKitSetup.CreateModel(name, graph);
                    var at = airport.transform.TransformPoint(new Vector3((i % 5) * 300f, 0f, 2400f + (i / 5) * 300f));
                    model.transform.position = at;
                    Shoot("type-" + name, size, at);
                    Oblique("type-" + name + "-view", size * 1.6f, at);
                }
                for (int i = 0; i < PublicCases.Length; i++)
                {
                    var (name, size, build) = PublicCases[i];
                    var graph = new PublicRoadNetwork();
                    build(graph);
                    var model = PublicRoadKitSetup.CreateModel(name, graph);
                    var at = airport.transform.TransformPoint(new Vector3((i % 5) * 300f, 0f, 1200f + (i / 5) * 300f));
                    model.transform.position = at;
                    Shoot("public-" + name, size, at);
                }
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
