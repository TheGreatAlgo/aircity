using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.EditorTools.Dev;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>
    /// Tunnel approaches and structure dead ends on real terrain (the New Game scene on the road-connection hill
    /// fixture, edit mode), captured from fixed cameras: an open approach bending into a hillside across a side
    /// slope, a portal, and bridges and tunnels that simply end.
    ///   Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Game.TunnelEndReview.FromCommandLine [-reviewLabel before]
    /// Writes Captures/TunnelEnds/&lt;label&gt;/*.png.
    /// </summary>
    public static class TunnelEndReview
    {
        static float Land(float x, float z) => RoadConnectionFixture.Land(x, z);

        static void Line(PublicRoadNetwork g, PublicRoadStyle style, params (Vector3 p, RoadStructure structure)[] points)
        {
            int prev = g.AddNode(points[0].p);
            for (int i = 1; i < points.Length; i++)
            {
                int node = g.AddNode(points[i].p);
                g.AddSegment(prev, node, style, points[i].structure).alignment = RoadBuildSettings.Default.Shape(points[i - 1].p, points[i].p);
                prev = node;
            }
        }

        static Vector3 P(float x, float z, float below = 0f) => new Vector3(x, Land(x, z) - below, z);
        static Vector3 At(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>The review's roads, added to the fixture's own.</summary>
        public static void AddRoads(PublicRoadNetwork g)
        {
            const RoadStructure G = RoadStructure.Ground, T = RoadStructure.Tunnel, E = RoadStructure.Elevated;
            // an open approach bending into the hill across its side slope, then the bore
            Line(g, PublicRoadStyle.Urban, (P(-1700f, -1040f), G), (P(-1650f, -1040f), G), (At(-1600f, 96.5f, -1020f), T), (At(-1560f, 93.5f, -985f), T),
                (At(-1500f, 92f, -900f), T));
            // bridges that end in the air: a street and an avenue
            Line(g, PublicRoadStyle.Urban, (P(-1950f, -250f), G), (P(-1900f, -250f), G), (At(-1840f, 107f, -250f), E), (At(-1790f, 107f, -250f), E));
            Line(g, PublicRoadStyle.Avenue, (P(-1950f, -170f), G), (P(-1900f, -170f), G), (At(-1840f, 107f, -170f), E), (At(-1790f, 107f, -170f), E));
            Line(g, PublicRoadStyle.Rural, (P(-1950f, -90f), G), (P(-1900f, -90f), G), (At(-1840f, 107f, -90f), E), (At(-1790f, 107f, -90f), E));
            // tunnels that end: in the open cut (street, avenue), and a street that ends on the ground for comparison
            Line(g, PublicRoadStyle.Urban, (P(-1950f, -330f), G), (P(-1900f, -330f), G), (At(-1840f, 95.5f, -330f), T), (At(-1800f, 95.5f, -330f), T));
            Line(g, PublicRoadStyle.Avenue, (P(-1950f, -400f), G), (P(-1900f, -400f), G), (At(-1840f, 95.5f, -400f), T), (At(-1800f, 95.5f, -400f), T));
            Line(g, PublicRoadStyle.Urban, (P(-1950f, -30f), G), (P(-1800f, -30f), G));
        }

        public static CareerState State()
        {
            // added before the region starts, so the ground roads are graded into the terrain as in a game
            return RoadConnectionFixture.State(AddRoads);
        }

        /// <summary>A tunnel drawn as the player draws one (RoadConstructionPlan, Tunnel mode): from the end of street
        /// R5 down under the hill. Returns where it starts, where it goes under ground and its direction; null when
        /// the plan is refused (logged).</summary>
        static (Vector3 start, Vector3 portal, Vector3 along)? DrawTunnel(CareerState state)
        {
            var graph = state.publicRoads;
            var from = graph.nodes.OrderBy(n => Vector2.Distance(PublicRoadNetwork.XZ(n.position), RoadConnectionFixture.R5A)).First();
            var end = new Vector3(-1450f, 90f, -930f);
            var settings = new RoadBuildSettings { mode = RoadBuildMode.Tunnel, smooth = true };
            if (!RoadConstructionPlan.TryCreate(state, graph, from.id, end, -1, -1, PublicRoadStyle.Urban, settings, out var plan, out string why)
                || !plan.TryCommit(state, graph, out var result, out why))
            {
                Debug.LogWarning("Tunnel end review: the drawn tunnel was refused: " + why);
                return null;
            }
            state.publicRoads = result;
            var along = end - from.position; along.y = 0f; float length = along.magnitude; along.Normalize();
            var portal = end;
            for (float s = 0f; s < length; s += 1f)
            {
                var p = from.position + along * s; p.y = Mathf.Lerp(from.position.y, end.y, s / length);
                if (Land(p.x, p.z) - p.y < 7f) continue;
                portal = p; break;
            }
            Debug.Log($"Tunnel end review: drawn tunnel sections: {string.Join(", ", result.segments.Skip(graph.segments.Count).Select(e => e.structure))}; portal near {portal}");
            return (from.position, portal, along);
        }

        /// <summary>Service roads that end: on a bridge, in an open cut, and on the ground.</summary>
        public static void AddAirside(CareerGame game)
        {
            var net = game.Airport.Taxiways;
            int Node(float x, float y, float z) => net.AddNode(new Vector3(x, y, z));
            void Road(int a, int b, RoadStructure structure) =>
                net.Connect(a, b, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service, false, structure);
            Road(Node(-420f, 8f, -420f), Node(-340f, 8f, -420f), RoadStructure.Elevated);
            Road(Node(-420f, -4.5f, -470f), Node(-340f, -4.5f, -470f), RoadStructure.Tunnel);
            Road(Node(-420f, 0f, -380f), Node(-340f, 0f, -380f), RoadStructure.Ground);
            net.Rebuild(); game.SharedConstructionChanged();
        }

        static readonly (string name, Vector3 at, Vector3 from, float fov)[] Shots =
        {
            ("approach-1-into-hill", new Vector3(-1590f, 98f, -1012f), new Vector3(-48f, 9f, -22f), 40f),
            ("approach-2-bend-wall", new Vector3(-1600f, 99f, -1020f), new Vector3(-6f, 12f, -26f), 45f),
            ("approach-3-above", new Vector3(-1590f, 98f, -1005f), new Vector3(-30f, 60f, -45f), 45f),
            ("approach-4-ramp-top", new Vector3(-1640f, 100f, -1038f), new Vector3(-24f, 7f, -14f), 45f),
            ("end-bridge-street", new Vector3(-1792f, 107f, -250f), new Vector3(20f, 9f, -16f), 40f),
            ("end-bridge-avenue", new Vector3(-1792f, 107f, -170f), new Vector3(24f, 12f, -20f), 40f),
            ("end-bridge-country", new Vector3(-1792f, 107f, -90f), new Vector3(20f, 9f, -16f), 40f),
            ("end-cut-street", new Vector3(-1803f, 97f, -330f), new Vector3(22f, 16f, -14f), 40f),
            ("end-cut-avenue", new Vector3(-1803f, 97f, -400f), new Vector3(26f, 20f, -18f), 40f),
            ("end-ground-street", new Vector3(-1802f, 100f, -30f), new Vector3(18f, 9f, -14f), 40f),
        };

        public static void FromCommandLine()
        {
            try
            {
                string label = Environment.GetCommandLineArgs().SkipWhile(a => a != "-reviewLabel").Skip(1).FirstOrDefault() ?? "after";
                string folder = "Captures/TunnelEnds/" + label;
                Directory.CreateDirectory(folder);
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                var game = Object.FindAnyObjectByType<CareerGame>();
                var state = State();
                var drawn = DrawTunnel(state);
                game.Initialize(state, false); game.Paused = true;
                AddAirside(game);
                var camera = game.view.GetComponent<Camera>();
                void Shot(string name, Vector3 at, Vector3 from, float fov)
                {
                    game.view.FocusPoint(at, from.magnitude);
                    camera.orthographic = false; camera.fieldOfView = fov; camera.nearClipPlane = .5f; camera.farClipPlane = 6000f;
                    camera.transform.position = at + from; camera.transform.LookAt(at);
                    SceneCapture.Capture(camera, $"{folder}/{name}.png", 1400, 900);
                }
                foreach (var (name, at, from, fov) in Shots) Shot(name, at, from, fov);
                if (drawn.HasValue)
                {
                    var (start, portal, along) = drawn.Value;
                    var side = new Vector3(along.z, 0f, -along.x);
                    Shot("drawn-1-ramp-top", start + along * 22f + Vector3.up * 1f, -along * 30f + Vector3.up * 8f - side * 9f, 45f);
                    Shot("drawn-2-ramp", Vector3.Lerp(start, portal, .6f), -along * 34f + Vector3.up * 13f + side * 16f, 45f);
                    Shot("drawn-3-portal", portal + Vector3.up * 3f, -along * 26f + Vector3.up * 5f + side * 5f, 45f);
                    Shot("drawn-4-above", Vector3.Lerp(start, portal, .5f), -along * 20f + Vector3.up * 95f, 45f);
                }
                var origin = game.Airport.transform.position;
                Shot("service-end-bridge", origin + new Vector3(-342f, 8f, -420f), new Vector3(20f, 9f, -16f), 40f);
                Shot("service-end-cut", origin + new Vector3(-343f, -3f, -470f), new Vector3(20f, 16f, -14f), 40f);
                Shot("service-end-ground", origin + new Vector3(-342f, 0f, -380f), new Vector3(18f, 9f, -14f), 40f);
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
