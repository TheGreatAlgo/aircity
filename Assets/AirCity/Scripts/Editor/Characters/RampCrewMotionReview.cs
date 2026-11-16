using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AirCity.Characters;
using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Characters
{
    /// <summary>
    /// Motion review: renders ramp-crew frame sequences at a fixed step (two views side by side), then assembles each
    /// into an MP4 and a contact sheet with ffmpeg. "human" plays <see cref="RampCrewMotionScripts"/>; "game" records the
    /// narrowbody baggage turnaround stepped at 1/60 s of real time; "stations" follows single workers at x1 (the
    /// marshaller's walk-in, signals and walk-out, the seated tractor driver, the standing container loader operator).
    /// <code>Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Characters.RampCrewMotionReview.FromCommandLine
    ///   -motionOut Captures/RampCrewNew/motion/after -motionSet all -motionTimeScale 1 -logFile Logs/motion.log</code>
    /// </summary>
    public static class RampCrewMotionReview
    {
        const string Ffmpeg = "/opt/homebrew/bin/ffmpeg";
        const int View = 640, Fps = 60;

        public static void FromCommandLine()
        {
            try
            {
                string output = Argument("-motionOut","Captures/RampCrewNew/motion/after");
                string set = Argument("-motionSet","all");
                string only = Argument("-motionOnly",null);
                float timeScale = float.Parse(Argument("-motionTimeScale","1"),System.Globalization.CultureInfo.InvariantCulture);
                string frames = Argument("-motionFrames",Path.Combine(Path.GetTempPath(),"ramp-crew-motion-frames"));
                AssetDatabase.Refresh(); RampCrewSetup.CreateAssets();
                if (set == "all" || set == "human") Human(output,frames,only);
                if (set == "all" || set == "game") Game(output,frames,timeScale,only);
                if (set == "all" || set == "stations") Stations(output,frames,only);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length-1; i++) if (args[i] == name) return args[i+1];
            return fallback;
        }

        // The rig gained a game-time overload; the review stays compilable against the original rig for "before" clips.
        static readonly System.Reflection.MethodInfo timed = typeof(RampCrewRig).GetMethod("Apply",new[] { typeof(RampCrewPose),typeof(float),typeof(float) });
        static readonly object[] arguments = new object[3];
        public static void Apply(RampCrewRig rig, RampCrewPose pose, float dt, float timeScale)
        {
            if (timed == null) { rig.Apply(pose); return; }
            arguments[0] = pose; arguments[1] = dt; arguments[2] = timeScale; timed.Invoke(rig,arguments);
        }

        // ---------------------------------------------------------------- human pace

        static void Human(string output, string frames, string only)
        {
            foreach (var motion in RampCrewMotionScripts.All)
            {
                if (only != null && !only.Contains(motion.name)) continue;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                var world = new GameObject("World").transform;
                Lights(); Ground(world);
                var rig = Object.Instantiate(RampCrewSetup.Prefab,world).GetComponent<RampCrewRig>(); rig.Initialize(0);
                Prepare(rig);
                foreach (var (centre,size,rotation) in motion.props) Box(world,centre,size,rotation,new Color(.42f,.44f,.47f));
                Transform bag = motion.bag != null ? Box(world,Vector3.zero,RampCrewMotionScripts.BagSize,Quaternion.identity,new Color(.55f,.18f,.12f)) : null;
                var markerL = Marker(world); var markerR = Marker(world);
                var (side,front) = Cameras();
                var recorder = new Recorder(Path.Combine(frames,"human-"+motion.name));
                var knees = new Knees(rig);
                const float step = 1f/Fps;
                int count = Mathf.CeilToInt(motion.duration/step)+1;
                for (int i = 0; i < count; i++)
                {
                    float t = i*step; var pose = motion.pose(t);
                    Apply(rig,pose,i == 0 ? 0f : step,1f);
                    knees.Sample(rig);
                    if (bag != null)
                    {
                        var b = motion.bag(t);
                        bag.gameObject.SetActive(b.visible);
                        bag.SetPositionAndRotation(b.bottom+b.rotation*Vector3.up*(RampCrewMotionScripts.BagSize.y*.5f),b.rotation);
                    }
                    markerL.gameObject.SetActive(pose.hands); markerR.gameObject.SetActive(pose.hands);
                    if (pose.hands)
                    {
                        markerL.position = world.TransformPoint(pose.position+pose.rotation*pose.leftHand);
                        markerR.position = world.TransformPoint(pose.position+pose.rotation*pose.rightHand);
                    }
                    var centre = world.TransformPoint(new Vector3(pose.position.x,0,pose.position.z))+Vector3.up*.85f;
                    side.transform.position = centre+new Vector3(4.2f,.45f,0); side.transform.LookAt(centre);
                    front.transform.position = centre+new Vector3(0,.45f,4.2f); front.transform.LookAt(centre);
                    recorder.Frame(side,front,t);
                }
                recorder.Finish(output,"human-"+motion.name);
                knees.Log("human-"+motion.name);
            }
        }

        // ---------------------------------------------------------------- game pace

        enum Clip { Dismount, Boarding, Loading, Leaving }

        static void Game(string output, string frames, float timeScale, string only)
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = false; demo.autoDeparture = false; demo.Initialize();
            demo.airport.timeScale = timeScale;
            float dt = timeScale/Fps;
            var cameras = Cameras(40f);
            var done = new bool[4]; Recorder recorder = null; Clip current = Clip.Dismount; int recorded = 0, tail = -1;
            string tag = "game-x"+timeScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
            demo.station.Present(0);
            for (int frame = 0; frame < 400000 && !demo.Job.Finished; frame++)
            {
                demo.airport.Simulate(dt); demo.Observe(); demo.station.Present(dt);
                var job = demo.Job; var crew = job.Fleet.Crew;
                if (recorder == null)
                {
                    for (int c = 0; c < 4 && recorder == null; c++)
                    {
                        var clip = (Clip)c; if (done[c] || only != null && !only.Contains(clip.ToString().ToLowerInvariant())) continue;
                        if (!Starts(clip,job,crew)) continue;
                        current = clip; recorded = 0; tail = -1;
                        recorder = new Recorder(Path.Combine(frames,tag+"-"+clip.ToString().ToLowerInvariant()));
                        Frame(clip,demo,cameras.Item1,cameras.Item2);
                        Debug.Log($"Motion review: {clip} starts at game {Time(job)} ({job.State})");
                    }
                }
                if (recorder == null) continue;
                recorder.Frame(cameras.Item1,cameras.Item2,recorded/(float)Fps); recorded++;
                if (tail < 0 && Ends(current,job,crew)) tail = Fps/2;
                if (tail > 0) tail--;
                if (tail == 0 || recorded >= Fps*(current == Clip.Loading ? 4 : 6))
                {
                    recorder.Finish(output,tag+"-"+current.ToString().ToLowerInvariant()); recorder = null; done[(int)current] = true;
                    if (Array.TrueForAll(done,d => d) || only != null && Array.TrueForAll(new[] { 0,1,2,3 },
                        c => done[c] || !only.Contains(((Clip)c).ToString().ToLowerInvariant()))) break;
                }
            }
            if (recorder != null) recorder.Finish(output,tag+"-"+current.ToString().ToLowerInvariant());
        }
        // ---------------------------------------------------------------- stations (x1, camera follows one worker)

        static void Stations(string output, string frames, string only)
        {
            bool Want(string name) => only == null || only.Contains(name);
            if (Want("marshaller") || Want("tractor"))
            {
                EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
                var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
                demo.startWithArrival = Want("marshaller"); demo.autoDeparture = false; demo.Initialize();
                var airport = demo.airport; airport.timeScale = 1f;
                var marshalling = airport.GetComponent<AirCity.Characters.MarshallerPresentation>();
                void Step(float dt) { airport.Simulate(dt); demo.Observe(); demo.station.Present(dt); if (marshalling != null) marshalling.Present(dt); }
                RampCrewRig Rig(string prefix) => airport.GetComponentsInChildren<RampCrewRig>(false).FirstOrDefault(r => r.name.StartsWith(prefix));
                if (Want("marshaller"))
                {
                    // Fast-forward the approach in large steps, then film each phase at x1.
                    for (int i = 0; i < 20000 && airport.Marshalling.Crew.Count == 0; i++) Step(.5f);
                    if (airport.Marshalling.Crew.Count == 0) Debug.LogWarning("Motion review: no marshaller appeared");
                    else
                    {
                        var worker = airport.Marshalling.Crew[0];
                        Follow(output, frames, "station-marshaller-walk-in", Step, () => Rig("Marshaller"), () => worker.Phase == AirCity.Traffic.MarshallerPhase.WalkingIn, 30f, 4.5f);
                        Follow(output, frames, "station-marshaller-signals", Step, () => Rig("Marshaller"), () => worker.Phase != AirCity.Traffic.MarshallerPhase.WalkingOut && worker.Phase != AirCity.Traffic.MarshallerPhase.Complete, 24f, 4.5f);
                        for (int i = 0; i < 20000 && worker.Phase != AirCity.Traffic.MarshallerPhase.WalkingOut && worker.Phase != AirCity.Traffic.MarshallerPhase.Complete; i++) Step(1f / 30f);
                        Follow(output, frames, "station-marshaller-walk-out", Step, () => Rig("Marshaller"), () => worker.Phase == AirCity.Traffic.MarshallerPhase.WalkingOut, 30f, 4.5f);
                    }
                }
                if (Want("tractor"))
                {
                    // The tractor driving with its driver seated: the first drive leg after the job starts moving.
                    var crew = demo.Job.Fleet.Crew;
                    Vector3 last = Vector3.zero; bool moving = false;
                    for (int i = 0; i < 200000 && !moving && !demo.Job.Finished; i++)
                    {
                        Step(1f / 60f);
                        var driver = Rig("Tractor driver");
                        if (driver != null && crew.GroundSeated)
                        {
                            moving = i > 10 && (driver.transform.position - last).magnitude > .5f / 60f;
                            last = driver.transform.position;
                        }
                    }
                    Follow(output, frames, "station-tractor-driver", Step, () => Rig("Tractor driver"), () => crew.GroundSeated, 10f, 3.2f, true);
                }
            }
            if (Want("loader"))
            {
                EditorSceneManager.OpenScene(ContainerTurnaroundSetup.ScenePath);
                var demo = Object.FindAnyObjectByType<ContainerTurnaroundDemo>();
                demo.autoDeparture = false; demo.Initialize();
                var airport = demo.airport; airport.timeScale = 1f;
                void Step(float dt) { airport.Simulate(dt); demo.Observe(); demo.station.Present(dt); if (demo.bulkStation != null) demo.bulkStation.Present(dt); }
                RampCrewRig Operator() => airport.GetComponentsInChildren<RampCrewRig>(false).FirstOrDefault(r => r.name == "Container loader operator");
                // The loader driving up to the aircraft and raising its platform, then working the platform controls.
                for (int i = 0; i < 200000 && demo.Job.State != ContainerTurnaroundState.LoaderApproach && !demo.Job.Finished && Operator() == null; i++) Step(1f / 60f);
                for (int i = 0; i < 200000 && demo.Job.State != ContainerTurnaroundState.LoaderApproach && !demo.Job.Finished; i++) Step(1f / 30f);
                Follow(output, frames, "station-loader-operator", Step, Operator, () => demo.Job.State == ContainerTurnaroundState.LoaderApproach || demo.Job.State == ContainerTurnaroundState.SettingUp, 12f, 3.6f, true);
            }
        }

        /// <summary>Record one worker at x1 while <paramref name="keep"/> holds (at most <paramref name="seconds"/>), the
        /// two cameras following it: one from its right side, one from three-quarters front.</summary>
        static void Follow(string output, string frames, string name, Action<float> step, Func<RampCrewRig> rig, Func<bool> keep, float seconds, float distance, bool rigid = false, float height = .35f)
        {
            var (side, front) = Cameras(32f);
            var recorder = new Recorder(Path.Combine(frames, name));
            int count = 0; Vector3 smooth = Vector3.zero; Vector3 forward = Vector3.forward;
            Knees knees = null;
            foreach (var r in Object.FindObjectsByType<RampCrewRig>(FindObjectsSortMode.None)) Prepare(r);
            for (; count < seconds * Fps && keep(); count++)
            {
                step(1f / Fps);
                var target = rig(); if (target == null) continue;
                Prepare(target);
                if (knees == null) knees = new Knees(target);
                knees.Sample(target);
                var at = target.transform.position + Vector3.up * .95f;
                var f = target.transform.forward; f.y = 0; if (f.sqrMagnitude > .01f) f.Normalize(); else f = forward;
                if (count == 0) { smooth = at; forward = f; }
                // A steady camera: it trails the worker instead of jittering with each step.
                smooth = Vector3.Lerp(smooth, at, rigid ? 1f : .08f); forward = Vector3.Slerp(forward, f, rigid ? 1f : .03f).normalized;
                var right = Vector3.Cross(Vector3.up, forward);
                side.transform.position = smooth + right * distance + Vector3.up * height; side.transform.LookAt(smooth);
                front.transform.position = smooth + (forward * .85f - right * .5f).normalized * distance + Vector3.up * (height + .15f); front.transform.LookAt(smooth);
                recorder.Frame(side, front, count / (float)Fps);
            }
            Object.DestroyImmediate(side.gameObject); Object.DestroyImmediate(front.gameObject);
            if (count > 0) recorder.Finish(output, name); else Debug.LogWarning("Motion review: nothing to film for " + name);
            knees?.Log(name);
        }

        static string Time(BaggageTurnaround job) => job.TransferSeconds.ToString("0.00");

        static bool Starts(Clip clip, BaggageTurnaround job, BaggageCrew crew)
        {
            switch (clip)
            {
                case Clip.Dismount: return crew.GroundProgress > 0 && !crew.GroundReady && job.State != BaggageTurnaroundState.Withdrawing;
                case Clip.Boarding: return crew.HoldProgress > 0 && !crew.HoldReady && job.State == BaggageTurnaroundState.Docking;
                case Clip.Loading: return job.State == BaggageTurnaroundState.LoadingHold && job.TransferBatch != null && job.TransferSeconds > .3f;
                default: return job.State == BaggageTurnaroundState.Withdrawing && !crew.HoldReady && !crew.HoldSeated;
            }
        }
        static bool Ends(Clip clip, BaggageTurnaround job, BaggageCrew crew)
        {
            switch (clip)
            {
                case Clip.Dismount: return crew.GroundReady;
                case Clip.Boarding: return crew.HoldReady;
                case Clip.Loading: return false;
                default: return crew.HoldSeated;
            }
        }

        static void Frame(Clip clip, BaggageTurnaroundDemo demo, Camera a, Camera b)
        {
            var job = demo.Job; var crew = job.Fleet.Crew; var plan = job.Plan; var airport = demo.airport.transform;
            Vector3 At(Vector3 p) => airport.TransformPoint(p);
            var inward = airport.TransformDirection(plan.inward); var side = Vector3.Cross(Vector3.up,inward);
            Vector3 target, offsetA, offsetB;
            if (clip == Clip.Boarding || clip == Clip.Leaving)
            {
                target = At(plan.door)-inward*2.2f+Vector3.down*.6f;
                offsetA = -inward*4.5f+side*4.5f+Vector3.up*1.6f; offsetB = side*-6f+Vector3.up*1.2f-inward*1f;
            }
            else
            {
                var cart = At(crew.CartStand); var belt = At(crew.BeltStand);
                if (clip == Clip.Dismount) cart = At(crew.GroundPose.position);
                var along = belt-cart; along.y = 0; var across = Vector3.Cross(Vector3.up,along.normalized);
                target = Vector3.Lerp(cart,belt,.5f)+Vector3.up*.8f;
                float reach = Mathf.Max(4.5f,along.magnitude*1.1f+2f);
                offsetA = across*reach+Vector3.up*1.5f; offsetB = -across*reach*.6f-along.normalized*reach*.6f+Vector3.up*2f;
            }
            a.transform.position = target+offsetA; a.transform.LookAt(target);
            b.transform.position = target+offsetB; b.transform.LookAt(target);
            foreach (var rig in Object.FindObjectsByType<RampCrewRig>(FindObjectsSortMode.None)) Prepare(rig);
        }

        // ---------------------------------------------------------------- scene helpers

        static void Prepare(RampCrewRig rig)
        {
            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            { skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true; }
        }
        static (Camera, Camera) Cameras(float fov = 30f)
        {
            Camera Make(string name, float x)
            {
                var c = new GameObject(name).AddComponent<Camera>();
                c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(.10f,.14f,.18f);
                c.fieldOfView = fov; c.nearClipPlane = .05f; c.farClipPlane = 600f; c.rect = new Rect(x,0,.5f,1); c.enabled = false; return c;
            }
            foreach (var existing in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (existing.enabled && existing.targetTexture == null && existing.name != "Side" && existing.name != "Front") existing.enabled = existing.enabled;
            return (Make("Side",0), Make("Front",.5f));
        }
        static void Lights()
        {
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.6f;
            light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(50,-35,0); RenderSettings.sun = light;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.42f,.46f,.52f);
        }
        static Material Lit(Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor",color); m.SetFloat("_Smoothness",.15f); return m;
        }
        static void Ground(Transform world)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.name = "Ground"; plane.transform.SetParent(world,false);
            plane.transform.localScale = new Vector3(6,1,6);
            var texture = new Texture2D(2,2,TextureFormat.RGB24,false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            texture.SetPixels(new[] { new Color(.52f,.53f,.55f), new Color(.40f,.41f,.43f), new Color(.40f,.41f,.43f), new Color(.52f,.53f,.55f) }); texture.Apply();
            var m = Lit(Color.white); m.SetTexture("_BaseMap",texture); m.SetTextureScale("_BaseMap",new Vector2(60,60)); // 0.5 m squares
            plane.GetComponent<Renderer>().sharedMaterial = m;
            // A metre line so foot slip reads against something sharp.
            for (int i = -6; i <= 6; i++) Box(world,new Vector3(0,.001f,i),new Vector3(.02f,.002f,.02f),Quaternion.identity,Color.white);
        }
        static Transform Box(Transform world, Vector3 centre, Vector3 size, Quaternion rotation, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(world,false); go.transform.SetLocalPositionAndRotation(centre,rotation); go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Lit(color); return go.transform;
        }
        static Transform Marker(Transform world)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(world,false); go.transform.localScale = Vector3.one*.025f;
            var m = Lit(new Color(1,.1f,.1f)); m.SetColor("_EmissionColor",new Color(1,.1f,.1f)); m.EnableKeyword("_EMISSION");
            go.GetComponent<Renderer>().sharedMaterial = m; return go.transform;
        }

        // ---------------------------------------------------------------- knee angles

        /// <summary>Interior thigh-shin angle (180 = straight) of both knees over a clip, split into frames with both feet
        /// down and frames with a foot in the air, so a clip's posture can be judged from the log.</summary>
        sealed class Knees
        {
            readonly Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
            float standLow = 180f, standSum, standAt; int standCount, frames; float swingLow = 180f;
            public Knees(RampCrewRig rig)
            {
                var all = rig.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < 2; i++)
                {
                    string s = i == 0 ? "_L" : "_R";
                    thigh[i] = all.FirstOrDefault(t => t.name == "Thigh" + s); shin[i] = all.FirstOrDefault(t => t.name == "Shin" + s); foot[i] = all.FirstOrDefault(t => t.name == "Foot" + s);
                }
            }
            public void Sample(RampCrewRig rig)
            {
                if (thigh[0] == null || thigh[1] == null) return;
                bool down = !rig.FootSwinging(0) && !rig.FootSwinging(1);
                for (int i = 0; i < 2; i++)
                {
                    float angle = Vector3.Angle(thigh[i].position - shin[i].position, foot[i].position - shin[i].position);
                    if (down) { if (angle < standLow) { standLow = angle; standAt = frames / (float)Fps; } standSum += angle; standCount++; }
                    else swingLow = Mathf.Min(swingLow, angle);
                }
                frames++;
            }
            public void Log(string name) => Debug.Log($"Motion review knees {name}: both feet down {(standCount > 0 ? standSum / standCount : 0f):0.0} deg mean, " +
                $"{standLow:0.0} least (at {standAt:0.00} s); stepping {swingLow:0.0} least (180 = straight)");
        }

        // ---------------------------------------------------------------- recording

        sealed class Recorder
        {
            readonly string folder; readonly RenderTexture target; readonly Texture2D pixels; int count;
            public Recorder(string folder)
            {
                this.folder = folder;
                if (Directory.Exists(folder)) Directory.Delete(folder,true);
                Directory.CreateDirectory(folder);
                target = new RenderTexture(View*2,View,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                pixels = new Texture2D(View*2,View,TextureFormat.RGB24,false);
            }
            public void Frame(Camera a, Camera b, float seconds)
            {
                a.targetTexture = b.targetTexture = target;
                if (count == 0) { a.Render(); b.Render(); } // warm-up for shadows/ambient
                a.Render(); b.Render();
                a.targetTexture = b.targetTexture = null;
                var previous = RenderTexture.active; RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,View*2,View),0,0); RenderTexture.active = previous;
                Stamp(pixels,seconds.ToString("00.00",System.Globalization.CultureInfo.InvariantCulture));
                pixels.Apply(false);
                File.WriteAllBytes(Path.Combine(folder,$"frame_{count:00000}.jpg"),pixels.EncodeToJPG(92)); count++;
            }
            public void Finish(string output, string name)
            {
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                Directory.CreateDirectory(output);
                string mp4 = Path.GetFullPath(Path.Combine(output,name+".mp4")), sheet = Path.GetFullPath(Path.Combine(output,name+"-sheet.png"));
                string input = Path.Combine(Path.GetFullPath(folder),"frame_%05d.jpg");
                Run($"-y -loglevel error -framerate {Fps} -i \"{input}\" -c:v libx264 -pix_fmt yuv420p -crf 20 -movflags +faststart \"{mp4}\"");
                // 24 evenly spaced frames, 4 x 6, each tile is the side|front pair at 3/4 size.
                int every = Mathf.Max(1,Mathf.CeilToInt(count/24f));
                Run($"-y -loglevel error -i \"{input}\" -vf \"select='not(mod(n\\,{every}))',scale={View*3/2}:{View*3/4},tile=4x6:padding=4:color=black\" -frames:v 1 \"{sheet}\"");
                Debug.Log($"Motion review: wrote {mp4} and {sheet} ({count} frames, every {every} on the sheet)");
            }
            static void Run(string args)
            {
                var info = new ProcessStartInfo(Ffmpeg,args) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                using var process = Process.Start(info); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("ffmpeg failed: "+error);
            }

            // 3x5 digits so contact sheets carry the clip time without ffmpeg's drawtext.
            static readonly string[] glyphs =
            {
                "111101101101111","010110010010111","111001111100111","111001111001111","101101111001001",
                "111100111001111","111100111101111","111001001001001","111101111101111","111101111001111",
            };
            static void Stamp(Texture2D texture, string text)
            {
                const int scale = 5, x0 = 12, y0 = 12;
                int width = text.Length*4*scale+scale*2;
                for (int y = y0-scale; y < y0+6*scale; y++) for (int x = x0-scale; x < x0+width; x++) texture.SetPixel(x,y,Color.black);
                int cursor = x0;
                foreach (char c in text)
                {
                    if (c == '.') { for (int y = 0; y < scale; y++) for (int x = 0; x < scale; x++) texture.SetPixel(cursor+scale+x,y0+y,Color.white); cursor += 3*scale; continue; }
                    var g = glyphs[c-'0'];
                    for (int row = 0; row < 5; row++) for (int col = 0; col < 3; col++)
                        if (g[row*3+col] == '1')
                            for (int y = 0; y < scale; y++) for (int x = 0; x < scale; x++) texture.SetPixel(cursor+col*scale+x,y0+(4-row)*scale+y,Color.white);
                    cursor += 4*scale;
                }
            }
        }
    }
}
