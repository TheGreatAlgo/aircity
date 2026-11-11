using System;
using System.IO;
using System.Linq;
using AirCity.EditorTools.Dev;
using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.World
{
    /// <summary>Captures actual seeded ground detail and a fully operating manually prepared airport.</summary>
    public static class NaturalLandscapeReview
    {
        const string Root = "Captures/NewGame/NaturalLandscape/";
        public static void FromCommandLine()
        {
            try
            {
                NewGameSetup.UpdateWorldAssets();
                var game = Object.FindAnyObjectByType<CareerGame>();
                var state = CareerState.New(42);
                if (!state.Build(out string why, 0)) throw new Exception(why);
                game.Initialize(state, false);
                var camera = game.view.GetComponent<Camera>();
                game.view.enabled = false;
                var c = state.Site.centre;
                var target = new Vector3(c.x, state.world.Height(c), c.y);
                camera.transform.position = target + new Vector3(370f, 210f, -440f);
                camera.transform.LookAt(target);
                SceneCapture.Capture(camera, Root + "01-natural-site.png", 1920, 1200);
                var candidates = GroundCoverScatter.Generate(state.world, null, null, state.Site.Parcel);
                var rock = candidates.Where(p => p.kind == GroundCoverKind.Boulder)
                    .OrderBy(p => state.world.trees.Count(t => !t.removed && Vector2.Distance(t.position, p.position) < 10f))
                    .First();
                target = new Vector3(rock.position.x, game.valley.DisplayHeight(rock.position) + .8f, rock.position.y);
                ClearView(camera, state.world, target, 16f, 3.2f);
                camera.nearClipPlane = .15f;
                SceneCapture.Capture(camera, Root + "02-boulders-and-understory.png", 1920, 1200);
                var fern = candidates.Where(p => p.kind == GroundCoverKind.Fern)
                    .OrderBy(p => Vector2.Distance(p.position, c)).First();
                target = new Vector3(fern.position.x, game.valley.DisplayHeight(fern.position) + .35f, fern.position.y);
                ClearView(camera, state.world, target, 7f, 1.9f);
                SceneCapture.Capture(camera, Root + "03-forest-floor.png", 1920, 1200);
                // Actual paid manual operations, with new natural relief, followed by the real charter simulation.
                state = PlayerConstructionReview.Open(42);
                game.Initialize(state, false); game.view.enabled = false;
                target = new Vector3(c.x + 80f, state.Site.elevation, c.y - 50f);
                camera.transform.position = target + new Vector3(320f, 180f, -350f);
                camera.transform.LookAt(target);
                SceneCapture.Capture(camera, Root + "04-worked-airport.png", 1920, 1200);
                if (!game.AcceptCharter()) throw new Exception(game.Message);
                for (int i = 0; i < 2400 && state.charterActive; i++) game.Advance(1f);
                if (state.completedCharters != 1) throw new Exception("Natural landscape airport charter failed: " + game.Message);
                foreach (string name in new[] { "AirCity/ValleyTerrain", "Hidden/AirCity/ValleyTerrain Add", "Hidden/AirCity/ValleyTerrain Basemap Gen", "AirCity/GroundCover" })
                {
                    var shader = Shader.Find(name);
                    if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new Exception("Landscape shader failed: " + name);
                }
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/natural-landscape-review.txt", "Passed: seeded natural terrain/ground cover renders, manual earthwork airport, actual charter.\n");
                AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void ClearView(Camera camera, ValleyWorld world, Vector3 target, float distance, float height)
        {
            var centre = new Vector2(target.x, target.z);
            var nearby = world.trees.Where(t => !t.removed && Vector2.Distance(t.position, centre) < distance + 12f).ToArray();
            float best = float.NegativeInfinity; Vector3 eye = target;
            for (int i = 0; i < 16; i++)
            {
                var offset = new Vector2(Mathf.Cos(i * Mathf.PI / 8f), Mathf.Sin(i * Mathf.PI / 8f)) * distance;
                var p = centre + offset; float score = 20f;
                foreach (var tree in nearby)
                {
                    float t = Mathf.Clamp01(Vector2.Dot(tree.position - centre, offset) / offset.sqrMagnitude);
                    float clear = Vector2.Distance(tree.position, centre + offset * t);
                    score = Mathf.Min(score, clear - t * 2f);
                }
                if (score <= best) continue;
                best = score; eye = new Vector3(p.x, world.Height(p) + height, p.y);
            }
            camera.transform.position = eye; camera.transform.LookAt(target);
        }
    }
}
