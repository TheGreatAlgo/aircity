using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using AirCity.Aircraft;
using UnityEngine;

namespace AirCity.EditorTools.Dev
{
    /// <summary>
    /// Renders a scene's main camera to PNG files, for reviewing art from the command line:
    ///   Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Dev.SceneCapture.FromCommandLine
    ///         -captureScene Assets/AirCity/Scenes/Sandbox.unity -captureOut Captures/sandbox.png [-captureSize 1920x1080]
    ///         [-captureView "px,py,pz:tx,ty,tz"]...   (extra views: camera position : look-at target)
    ///         [-captureLod 2]                        (force that LOD or coarser, to inspect far LODs up close)
    ///         [-captureNight]                        (night lighting, all aircraft lights on)
    ///         [-captureSelect "Cessna172 SunsetRed"] (outline that aircraft as selected, another as hovered)
    /// </summary>
    public static class SceneCapture
    {
        public static void FromCommandLine()
        {
            string scene = Arg("-captureScene"), outPath = Arg("-captureOut"), size = Arg("-captureSize") ?? "1600x900";
            string lod = Arg("-captureLod"), select = Arg("-captureSelect");
            // QualitySettings are project settings: Unity saves them on exit, so a forced LOD must be put back or
            // every aircraft in the game stays on that LOD
            int maxLod = QualitySettings.maximumLODLevel;
            try
            {
                if (lod != null) QualitySettings.maximumLODLevel = int.Parse(lod);
                var wh = size.Split('x');
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                var cam = Camera.main;
                if (cam == null) throw new Exception("scene has no MainCamera");
                // a capture is a still: no time passes, so an animated LOD cross-fade started by moving the camera
                // would stay half-way, drawing two LODs dithered over each other
                foreach (var g in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude))
                    g.animateCrossFading = false;
                if (Flag("-captureNight")) SetNight();
                if (select != null) Highlight(select);
                Capture(cam, outPath, int.Parse(wh[0]), int.Parse(wh[1]));
                var args = Environment.GetCommandLineArgs();
                int n = 0;
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] != "-captureView") continue;
                    var parts = args[i + 1].Split(':');
                    cam.transform.position = Vec(parts[0]);
                    cam.transform.LookAt(Vec(parts[1]));
                    string p = Path.Combine(Path.GetDirectoryName(outPath), $"{Path.GetFileNameWithoutExtension(outPath)}_{++n}.png");
                    Capture(cam, p, int.Parse(wh[0]), int.Parse(wh[1]));
                }
                QualitySettings.maximumLODLevel = maxLod;
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                QualitySettings.maximumLODLevel = maxLod;
                EditorApplication.Exit(1);
            }
        }

        public static void Capture(Camera cam, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();   // warm-up: the first frame after a scene opens has no shadow map / ambient probe yet
            cam.Render();
            cam.targetTexture = prevTarget;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log("SceneCapture: wrote " + path);
        }

        static void SetNight()
        {
            AircraftLights.Glow = 1f;
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.intensity = 0.08f;
                sun.color = new Color(0.55f, 0.65f, 1f);
            }
            RenderSettings.ambientSkyColor = new Color(0.08f, 0.10f, 0.18f);
            RenderSettings.ambientEquatorColor = new Color(0.06f, 0.07f, 0.10f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.03f);
            foreach (var l in UnityEngine.Object.FindObjectsByType<AircraftLights>(FindObjectsInactive.Exclude))
            {
                l.beacon = l.landing = l.taxi = l.logo = true;
                l.UpdateState();   // no frame loop in batch mode
            }
        }

        /// <summary>Select the named aircraft and hover another of the same type, to show both outline styles.</summary>
        static void Highlight(string name)
        {
            string model = name.Split(' ')[0];
            bool hovered = false;
            foreach (var a in UnityEngine.Object.FindObjectsByType<AirCity.Aircraft.Aircraft>(FindObjectsInactive.Exclude))
            {
                if (a.name == name)
                {
                    a.Highlight = AircraftHighlight.Selected;
                }
                else if (!hovered && a.name.StartsWith(model))
                {
                    a.Highlight = AircraftHighlight.Hover;
                    hovered = true;
                }
            }
        }

        static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static Vector3 Vec(string s)
        {
            var c = s.Split(',');
            return new Vector3(float.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
