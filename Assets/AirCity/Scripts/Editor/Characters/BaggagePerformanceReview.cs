using System;
using System.Collections.Generic;
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
    /// Records the baggage crew performance on the real turnaround scenes into MP4 clips and contact sheets, stepping
    /// the airport at a fixed real-time step: the wide stand view at player distance, close views of the cart/belt and
    /// of the hold, the hired second handlers, the depot, x12 and a pause / blocked-access freeze.
    /// <code>Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Characters.BaggagePerformanceReview.FromCommandLine
    ///   -performanceOut Captures/RampCrewNew/performance/after [-performanceOnly wide,ground] -logFile Logs/performance.log</code>
    /// </summary>
    public static class BaggagePerformanceReview
    {
        const string Ffmpeg = "/opt/homebrew/bin/ffmpeg";
        const int Fps = 30, Width = 960, Height = 540;

        public static void FromCommandLine()
        {
            int code = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                string output = "Captures/RampCrewNew/performance/after", only = null;
                for (int i = 0; i < args.Length-1; i++)
                {
                    if (args[i] == "-performanceOut") output = args[i+1];
                    if (args[i] == "-performanceOnly") only = args[i+1];
                }
                string frames = Path.Combine("Temp","PerformanceFrames");
                bool Want(string name) => only == null || only.Split(',').Contains(name);
                if (only == "probe") Probe();
                else if (only == "sandbox-probe") SandboxProbe(false);
                else if (only == "sandbox-probe-x1") SandboxProbe(true);
                else
                {
                    if (Want("wide") || Want("ground") || Want("hold") || Want("wheel")) Turnaround(output,frames,Want);
                    if (Want("x12")) Fast(output,frames);
                    if (Want("freeze")) Freeze(output,frames);
                    if (Want("helpers") || Want("depot")) Sandbox(output,frames,Want);
                    Report();
                }
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            EditorApplication.Exit(code);
        }

        /// <summary>Last-resort placements and direct bag transfers, read by name so the class also runs before the
        /// performance layer exists.</summary>
        static void Report()
        {
            var type = Type.GetType("AirCity.Vehicles.BaggageCrewPerformance, AirCity.Runtime");
            if (type == null) { Debug.Log("Performance review: no performance layer (literal crew)."); return; }
            string Read(string name) => type.GetProperty(name)?.GetValue(null)?.ToString() ?? "?";
            Debug.Log($"Performance review: last-resort placements {Read("Placements")}, direct bag transfers {Read("DirectBags")}, " +
                $"fastest standing performer {Read("MaxStandingSpeed")} m/s ({Read("MaxStandingWhat")}), step {Read("StepMicroseconds")} us and {Read("StepBytes")} B allocated per step");
        }

        /// <summary>Numbers without rendering: performer speeds, palm-to-bag gaps, stack counts against the ledger at the
        /// moments that matter, last-resort placements and direct bags, at x1 through a whole turnaround.</summary>
        static void Probe()
        {
            var type = Type.GetType("AirCity.Vehicles.BaggageCrewPerformance, AirCity.Runtime");
            type?.GetMethod("ResetCounters")?.Invoke(null,null);
            var demo = Demo(1f); var job = demo.Job; float dt = 1f/Fps, time = 0;
            demo.station.Present(0);
            var rigs = Object.FindObjectsByType<RampCrewRig>(FindObjectsInactive.Include);
            var last = new Dictionary<RampCrewRig,Vector3>(); var maxSpeed = new Dictionary<string,float>();
            var gaps = new List<float>(); string lastState = null; int mismatches = 0; bool holdSeated = true, groundSeated = true;
            for (int frame = 0; frame < 400*Fps && !job.Finished; frame++)
            {
                demo.airport.Simulate(dt); demo.Observe();
                demo.station.Present(dt); time += dt;
                foreach (var rig in Object.FindObjectsByType<RampCrewRig>(FindObjectsInactive.Exclude))
                {
                    var p = rig.transform.position;
                    if (last.TryGetValue(rig,out var q))
                    {
                        float v = Vector3.Distance(new Vector3(p.x,0,p.z),new Vector3(q.x,0,q.z))/dt;
                        string key = rig.name+" "+job.State;
                        maxSpeed[key] = Mathf.Max(maxSpeed.TryGetValue(key,out var m) ? m : 0,v);
                    }
                    last[rig] = p;
                    var socket = rig.CarrySocket;
                }
                var perf = demo.station.Performance;
                if (perf != null)
                    for (int w = 0; w < 2; w++) foreach (bool ground in new[] { true, false })
                    {
                        if (!perf.Bag(ground,w,out var bottom,out _,out _,out bool hands) || !hands) continue;
                        var rig = rigs.FirstOrDefault(r => r.name.StartsWith(ground ? (w == 0 ? "Tractor" : "Additional cart") : (w == 0 ? "Loader" : "Additional hold")));
                        if (rig == null || !perf.HandTargets(ground,w,out var l,out var r2)) continue;
                        var at = demo.airport.transform;
                        gaps.Add(Mathf.Max(Vector3.Distance(at.TransformPoint(l),rig.LeftPalm),Vector3.Distance(at.TransformPoint(r2),rig.RightPalm)));
                    }
                if (perf != null && frame%Fps == 0)
                {
                    var f = perf.Flow; string piles = "";
                    if (f != null) { foreach (var c in f.Carts) piles += c.entries.Count(e => e.motion >= 0)+" "; piles += "h"+f.HoldBags.Count+" b"+f.Tokens.Count+" backlog "+f.Backlog; }
                    Debug.Log($"Probe t {time:000.0} {job.State} ts {job.TransferSeconds:0.0} | {perf.Describe(true,0)} | {perf.Describe(false,0)} | {piles}");
                }
                if (perf != null)
                {
                    bool seated = perf.Describe(false,0).StartsWith("H0 Seated") || perf.Describe(false,0).StartsWith("H0 Grip");
                    if (seated != holdSeated) { Debug.Log($"Probe hold seated={seated} at {time:0.00}"); holdSeated = seated; }
                    bool gs = perf.Describe(true,0).StartsWith("G0 Seated") || perf.Describe(true,0).StartsWith("G0 Grip");
                    if (gs != groundSeated) { Debug.Log($"Probe ground seated={gs} at {time:0.00}"); groundSeated = gs; }
                }
                string state = job.State.ToString();
                if (state != lastState)
                {
                    var flow = perf?.Flow; string shown = "";
                    if (flow != null) { shown = " shown carts"; foreach (var c in flow.Carts) shown += " "+c.entries.Count(e => e.motion >= 0); shown += " hold "+flow.HoldBags.Count+" belt "+flow.Tokens.Count; }
                    string ledger = " ledger carts"; foreach (var c in job.Fleet.Carts) ledger += " "+c.Count; ledger += " hold "+job.Hold.Count;
                    Debug.Log($"Probe {time:0.00} {state}{shown}{ledger}");
                    if (flow != null && (state == "ArrivalsDrive" || state == "Withdrawing"))
                    {
                        int i = 0; foreach (var c in flow.Carts) { if (c.entries.Count(e => e.motion >= 0) != job.Fleet.Carts[i].Count) mismatches++; i++; }
                    }
                    lastState = state;
                }
            }
            foreach (var pair in maxSpeed.OrderBy(p => p.Key)) if (pair.Value > 1.6f) Debug.Log($"Probe speed {pair.Key}: {pair.Value:0.00} m/s");
            gaps.Sort();
            if (gaps.Count > 0)
                Debug.Log($"Probe palm-to-target gap while carrying: n {gaps.Count} mean {gaps.Average()*100:0.0} cm p95 {gaps[(int)(gaps.Count*.95f)]*100:0.0} cm max {gaps[gaps.Count-1]*100:0.0} cm");
            var t2 = Type.GetType("AirCity.Vehicles.BaggageCrewPerformance, AirCity.Runtime");
            Debug.Log($"Probe performance step during the turnaround: {t2.GetProperty("StepMicroseconds").GetValue(null)} us mean per fleet step (4 performers + bag flow), " +
                $"{t2.GetProperty("StepBytes").GetValue(null)} bytes allocated per step, over {t2.GetProperty("Steps").GetValue(null)} steps");
            // Cost of the performance alone (simulation and rigs excluded): step it many times on the finished state and
            // during a fresh loading pass, and count managed allocations.
            {
                var perf2 = demo.station.Performance; var watch = new Stopwatch();
                long before = GC.GetAllocatedBytesForCurrentThread();
                watch.Start(); for (int i = 0; i < 2000; i++) perf2.Step(dt); watch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread()-before;
                Debug.Log($"Probe performance step (idle fleet): {watch.Elapsed.TotalMilliseconds*1000/2000:0.0} us per fleet step, {allocated/2000f:0} bytes per step");
            }
            Debug.Log($"Probe finished {job.State} at {time:0.00}, ledger {demo.airport.Baggage.Ledger.Count}, mismatched carts at release {mismatches}");
            Report();
        }

        /// <summary>Numbers only, the Sandbox (four workers, depot) at Play-mode frame steps: x1 for 120 frames, x4 for 300,
        /// then x30, at 60 frames a second. Logs last resorts with the performers' state just before.</summary>
        static void SandboxProbe(bool x1)
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var depot = Object.FindAnyObjectByType<BaggageDepot>(); var airport = depot.Airport; var station = depot.Teams.Single();
            var type = Type.GetType("AirCity.Vehicles.BaggageCrewPerformance, AirCity.Runtime");
            type?.GetMethod("ResetCounters")?.Invoke(null,null);
            var separation = new Separation(); string[] history = new string[40]; int h = 0; int placements = 0; float time = 0;
            for (int frame = 0; frame < (x1 ? 60*320 : 6500); frame++)
            {
                airport.timeScale = x1 ? 1f : frame < 120 ? 1f : frame < 420 ? 4f : 30f;
                if (x1 && airport.Baggage.Jobs.Count > 0 && airport.Baggage.Jobs[0].Finished) break;
                float step = airport.timeScale/60f;
                airport.Simulate(step); station.Present(step); depot.Present(step); time += step;
                separation.Sample(station,airport.transform,time,airport.Baggage.Jobs.Count > 0 ? airport.Baggage.Jobs[0].State.ToString() : "-");
                var perf = station.Performance; var job = airport.Baggage.Jobs.Count > 0 ? airport.Baggage.Jobs[0] : null;
                if (perf == null || job == null) continue;
                if (x1 && frame%15 != 0) continue;
                history[h++%history.Length] = $"t {time:0.00} {job.State} ts {job.TransferSeconds:0.00} rem {perf.Flow.RemainingHandling():0.0} crew G{(job.Fleet.Crew.GroundSeated ? "s" : "-")}{(job.Fleet.Crew.GroundReady ? "r" : "-")} | {perf.Describe(true,0)} | {perf.Describe(true,1)} | {perf.Describe(false,0)} | {perf.Describe(false,1)}";
                int now = (int)type.GetProperty("Placements").GetValue(null);
                if (now != placements)
                {
                    placements = now; Debug.Log("Sandbox probe last resort at "+time.ToString("0.00"));
                    for (int k = 0; k < history.Length; k++) Debug.Log("  "+history[(h+k)%history.Length]);
                }
            }
            separation.Log(x1 ? "Sandbox x1" : "Sandbox x30");
            Report();
        }

        // ---------------------------------------------------------------- scenes

        sealed class Clip
        {
            public string name; public Func<bool> starts, ends; public Action<Camera> aim; public float maxSeconds = 120;
            public Recorder recorder; public bool done; public float recorded;
        }

        static BaggageTurnaroundDemo Demo(float timeScale)
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = false; demo.autoDeparture = false; demo.Initialize();
            demo.airport.timeScale = timeScale;
            Prepare();
            return demo;
        }

        static void Prepare()
        {
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)) if (c.targetTexture == null) c.enabled = false;
        }

        static void Skins()
        {
            foreach (var rig in Object.FindObjectsByType<RampCrewRig>(FindObjectsInactive.Include))
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                { skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true; }
        }

        static Camera MakeCamera(float fov)
        {
            var c = new GameObject("Performance camera").AddComponent<Camera>();
            c.fieldOfView = fov; c.nearClipPlane = .1f; c.farClipPlane = 2000f; c.enabled = false;
            return c;
        }

        static int wheelLog;
        static void Aim(Camera camera, Vector3 position, Vector3 target)
        { camera.transform.position = position; camera.transform.LookAt(target); }

        static void Run(BaggageTurnaroundDemo demo, string output, string frames, IList<Clip> clips, float timeScale, Action<float> during = null,
            Func<bool> finished = null, int maxFrames = 200000)
        {
            float dt = timeScale/Fps; var camera = MakeCamera(38f);
            demo.station.Present(0);
            float time = 0;
            for (int frame = 0; frame < maxFrames; frame++)
            {
                during?.Invoke(time);
                float step = demo.airport.timeScale/Fps;
                demo.airport.Simulate(step); demo.Observe(); demo.station.Present(step); time += step;
                Skins();
                foreach (var clip in clips)
                {
                    if (clip.done) continue;
                    if (clip.recorder == null)
                    {
                        if (!clip.starts()) continue;
                        clip.recorder = new Recorder(Path.Combine(frames,clip.name));
                        Debug.Log($"Performance review: {clip.name} starts at game {time:0.00} ({demo.Job.State})");
                    }
                    clip.aim(camera);
                    clip.recorder.Frame(camera,clip.recorded);
                    clip.recorded += 1f/Fps;
                    if (clip.ends() || clip.recorded >= clip.maxSeconds)
                    {
                        Debug.Log($"Performance review: {clip.name} ends at game {time:0.00} ({demo.Job.State})");
                        clip.recorder.Finish(output,clip.name); clip.recorder = null; clip.done = true;
                    }
                }
                if (clips.All(c => c.done)) break;
                if (finished != null ? finished() : demo.Job.Finished && clips.All(c => c.recorder == null)) break;
            }
            foreach (var clip in clips) if (clip.recorder != null) { clip.recorder.Finish(output,clip.name); clip.recorder = null; }
            Object.DestroyImmediate(camera.gameObject);
        }

        static void Turnaround(string output, string frames, Func<string,bool> want)
        {
            var demo = Demo(1f);
            var job = demo.Job; var airport = demo.airport.transform;
            Vector3 At(Vector3 p) => airport.TransformPoint(p);
            var clips = new List<Clip>();
            bool unloadSeen = false, loadSeen = false;
            if (want("wide"))
            {
                // Fixed for the whole visit: the train arrives, unloads, leaves, returns loaded and leaves again.
                Vector3 position = Vector3.zero, target = Vector3.zero; bool placed = false;
                clips.Add(new Clip
                {
                    name = "wide-turnaround", maxSeconds = 400,
                    starts = () => job.Plan != null,
                    ends = () => job.Finished,
                    aim = camera =>
                    {
                        if (!placed && job.Plan != null)
                        {
                            var plan = job.Plan; var inward = airport.TransformDirection(plan.inward);
                            var side = Vector3.Cross(Vector3.up,inward);
                            target = At(plan.door)-inward*5.5f+Vector3.down*1.8f;
                            position = target-inward*17f+side*13f+Vector3.up*11f; placed = true;
                        }
                        Aim(camera,position,target);
                    },
                });
            }
            if (want("wheel"))
            {
                // Close on the seated tractor driver's hands on the wheel, from beside and above the seat, while driving in.
                clips.Add(new Clip
                {
                    name = "tractor-wheel-grip", maxSeconds = 8,
                    starts = () => job.State == BaggageTurnaroundState.InboundDrive && job.Fleet.TrainSpeed > .5f,
                    ends = () => false,
                    aim = camera =>
                    {
                        var rig = demo.station.CrewRig(true,0); if (rig == null) return;
                        var palms = (rig.LeftPalm+rig.RightPalm)*.5f; var t = rig.transform;
                        Aim(camera,palms-t.forward*.25f+t.right*.42f+Vector3.up*.32f,palms);
                        if (wheelLog++ % 60 == 0)
                        {
                            // Finger curl relative to the hand: the angle between the hand's and the fingers' long axes.
                            var bones = rig.GetComponentsInChildren<Transform>(true);
                            foreach (var side in new[] { "_L", "_R" })
                            {
                                var handBone = bones.First(b => b.name == "Hand"+side); var finger = bones.First(b => b.name == "Fingers"+side);
                                var thumbBone = bones.First(b => b.name == "Thumb"+side);
                                Debug.Log($"Performance review: wheel{side} fingers bent {Vector3.Angle(handBone.position-bones.First(b => b.name == "Forearm"+side).position, finger.childCount > 0 ? finger.GetChild(0).position-finger.position : handBone.position-finger.position):0} deg from the forearm line (hand-fingers {Quaternion.Angle(handBone.rotation,finger.rotation):0}), thumb {Quaternion.Angle(handBone.rotation,thumbBone.rotation):0} deg from the hand");
                            }
                        }
                    },
                });
            }
            if (want("ground"))
            {
                Clip Ground(string name, BaggageTurnaroundState state, Func<bool> seen, Action mark)
                {
                    Vector3 position = Vector3.zero, target = Vector3.zero; bool placed = false;
                    return new Clip
                    {
                        name = name, maxSeconds = 75,
                        starts = () => !seen() && job.State == state && !job.Fleet.Crew.GroundSeated,
                        ends = () => { bool end = job.State != state && job.Fleet.TrainSpeed > .5f; if (end) mark(); return end; },
                        aim = camera =>
                        {
                            if (!placed)
                            {
                                var crew = job.Fleet.Crew; var cart = At(crew.CartStand); var belt = At(crew.BeltStand);
                                var along = belt-cart; along.y = 0; var across = Vector3.Cross(Vector3.up,along.normalized);
                                var tractor = At(new Vector3(job.Fleet.Train.Tractor.position.x,0,job.Fleet.Train.Tractor.position.y));
                                if (Vector3.Dot(tractor-cart,across) > 0) across = -across;
                                target = Vector3.Lerp(cart,belt,.5f)+Vector3.up*.75f;
                                position = target+across*6.2f-along.normalized*1.2f+Vector3.up*2.3f; placed = true;
                            }
                            Aim(camera,position,target);
                        },
                    };
                }
                clips.Add(Ground("ground-unload",BaggageTurnaroundState.UnloadingHold,() => unloadSeen,() => unloadSeen = true));
                clips.Add(Ground("ground-load",BaggageTurnaroundState.LoadingHold,() => loadSeen,() => loadSeen = true));
            }
            if (want("hold"))
            {
                Clip Hold(string name, Func<bool> starts, Func<bool> ends, float seconds)
                {
                    return new Clip
                    {
                        name = name, maxSeconds = seconds, starts = starts, ends = ends,
                        aim = camera =>
                        {
                            var plan = job.Plan; var inward = airport.TransformDirection(plan.inward);
                            var side = Vector3.Cross(Vector3.up,inward);
                            var loader = airport.TransformDirection(new Vector3(job.Fleet.LoaderPose.facing.x,0,job.Fleet.LoaderPose.facing.y));
                            float belt = Vector3.Dot(loader,side) > 0 ? -1 : 1; // look in from the open side of the belt
                            var door = At(plan.door);
                            Aim(camera,door-inward*2.4f+side*belt*2.3f+Vector3.up*1.1f,door+inward*1.0f+Vector3.down*.25f);
                        },
                    };
                }
                Clip Approach(string name, Func<bool> starts, Func<bool> ends, float seconds) => new Clip
                {
                    name = name, maxSeconds = seconds, starts = starts, ends = ends,
                    aim = camera =>
                    {
                        var plan = job.Plan; var inward = airport.TransformDirection(plan.inward);
                        var side = Vector3.Cross(Vector3.up,inward);
                        var loader = airport.TransformDirection(new Vector3(job.Fleet.LoaderPose.facing.x,0,job.Fleet.LoaderPose.facing.y));
                        float open = Vector3.Dot(loader,side) > 0 ? -1 : 1;
                        var door = At(plan.door); var target = door-inward*3.6f+Vector3.down*1.3f;
                        Aim(camera,target-inward*4.5f+side*open*7.5f+Vector3.up*2.2f,target);
                    },
                };
                bool boarded = false, left = false;
                clips.Add(Approach("hold-boarding",() => job.State == BaggageTurnaroundState.Docking && job.Fleet.Crew.HoldProgress > 0,
                    () => { if (job.State == BaggageTurnaroundState.UnloadingHold) boarded = true; return boarded; },60));
                clips.Add(Hold("hold-unload",() => job.State == BaggageTurnaroundState.UnloadingHold,
                    () => job.State == BaggageTurnaroundState.ArrivalsDrive && job.Fleet.TrainSpeed > .5f,60));
                clips.Add(Hold("hold-load",() => job.State == BaggageTurnaroundState.LoadingHold && job.TransferBatch != null,
                    () => job.State == BaggageTurnaroundState.Withdrawing,75));
                clips.Add(Approach("hold-exit",() => job.State == BaggageTurnaroundState.Withdrawing,
                    () => { if (job.State == BaggageTurnaroundState.Undocking && job.Fleet.LoaderSpeed != 0) left = true; return left; },40));
            }
            Run(demo,output,frames,clips,1f);
            if (!job.DepartureReady && (want("wide") || want("ground") || want("hold"))) throw new Exception("Turnaround did not complete: "+job.State+" "+job.Waiting);
            Debug.Log($"Performance review: turnaround complete, ledger {demo.airport.Baggage.Ledger.Count} bags");
        }

        static void Fast(string output, string frames)
        {
            var demo = Demo(12f); var job = demo.Job; var airport = demo.airport.transform;
            var clip = new Clip
            {
                name = "x12-turnaround", maxSeconds = 40,
                starts = () => job.Plan != null, ends = () => job.Finished,
                aim = camera =>
                {
                    var plan = job.Plan; var inward = airport.TransformDirection(plan.inward); var side = Vector3.Cross(Vector3.up,inward);
                    var target = airport.TransformPoint(plan.door)-inward*5.5f+Vector3.down*1.8f;
                    Aim(camera,target-inward*13f+side*10f+Vector3.up*8f,target);
                },
            };
            Run(demo,output,frames,new[] { clip },12f);
        }

        /// <summary>Loading at x1, a pause (time scale 0), a blocked-access freeze and its release, then x4, x12 and x30 bursts.</summary>
        static void Freeze(string output, string frames)
        {
            var demo = Demo(1f); var job = demo.Job; var airport = demo.airport.transform;
            float started = -1; bool placed = false; Vector3 eye = Vector3.zero, look = Vector3.zero;
            var clip = new Clip
            {
                name = "pause-block-freeze", maxSeconds = 48,
                starts = () => job.State == BaggageTurnaroundState.LoadingHold && job.TransferBatch != null && job.TransferSeconds > 3f,
                ends = () => job.Finished,
                aim = camera =>
                {
                    if (placed) { Aim(camera,eye,look); return; }
                    var crew = job.Fleet.Crew; var cart = airport.TransformPoint(crew.CartStand); var door = airport.TransformPoint(job.Plan.door);
                    var inward = airport.TransformDirection(job.Plan.inward); var side = Vector3.Cross(Vector3.up,inward);
                    var target = Vector3.Lerp(cart,door,.4f); target.y = 1.2f;
                    var tractor = airport.TransformPoint(new Vector3(job.Fleet.Train.Tractor.position.x,0,job.Fleet.Train.Tractor.position.y));
                    float s = Vector3.Dot(tractor-cart,side) > 0 ? -1 : 1;
                    eye = target-inward*7f+side*s*7f+Vector3.up*4.5f; look = target; placed = true;
                    Aim(camera,eye,look);
                },
            };
            Run(demo,output,frames,new[] { clip },1f,time =>
            {
                if (clip.recorder == null) return;
                if (started < 0) started = clip.recorded;
                float t = clip.recorded-started;
                // x1, pause, blocked access, x1, x4, x12, x30, x1.
                demo.station.accessBlocked = t >= 10f && t < 18f;
                demo.airport.timeScale = t < 5f ? 1f : t < 10f ? 0f : t < 22f ? 1f : t < 25f ? 4f : t < 27f ? 12f : t < 28f ? 30f : 1f;
            },() => clip.done);
            demo.station.accessBlocked = false;
        }

        static void Sandbox(string output, string frames, Func<string,bool> want)
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var depot = Object.FindAnyObjectByType<BaggageDepot>();
            var airportComponent = depot.Airport; var airport = airportComponent.transform; var station = depot.Teams.Single();
            station.incomingBags = station.outgoingBags = 60; airportComponent.timeScale = 1f;
            // Two more staff than the team leases, so idle depot workers are in the depot shot (the team still gets four).
            if (want("depot")) { depot.Hire(out _); depot.Hire(out _); }
            Prepare();
            var type = Type.GetType("AirCity.Vehicles.BaggageCrewPerformance, AirCity.Runtime");
            type?.GetMethod("ResetCounters")?.Invoke(null,null);
            var separation = new Separation();
            var camera = MakeCamera(38f);
            var clips = new List<Clip>();
            BaggageTurnaround Job() => airportComponent.Baggage.Jobs.Count > 0 ? airportComponent.Baggage.Jobs[0] : null;
            float time = 0;
            bool helpersDone = false, loadPlaced = false; Vector3 loadEye = Vector3.zero, loadLook = Vector3.zero;
            if (want("helpers"))
                clips.Add(new Clip
                {
                    name = "helpers-load", maxSeconds = 60,
                    starts = () => Job()?.State == BaggageTurnaroundState.LoadingHold && Job().TransferBatch != null && Job().Fleet.Workers >= 4,
                    ends = () => { var j = Job(); if (j != null && j.State != BaggageTurnaroundState.LoadingHold) helpersDone = true; return helpersDone; },
                    aim = cam =>
                    {
                        // Placed once (following the active cart made the camera jump when the second cart was worked).
                        if (!loadPlaced)
                        {
                            var job = Job(); var crew = job.Fleet.Crew; var cart = airport.TransformPoint(crew.CartStand);
                            var door = airport.TransformPoint(job.Plan.door); var inward = airport.TransformDirection(job.Plan.inward);
                            var side = Vector3.Cross(Vector3.up,inward); loadLook = Vector3.Lerp(cart,door,.35f); loadLook.y = 1f;
                            var tractor = airport.TransformPoint(new Vector3(job.Fleet.Train.Tractor.position.x,0,job.Fleet.Train.Tractor.position.y));
                            float s = Vector3.Dot(tractor-cart,side) > 0 ? -1 : 1;
                            loadEye = loadLook-inward*6.5f+side*s*6.5f+Vector3.up*4f; loadPlaced = true;
                        }
                        Aim(cam,loadEye,loadLook);
                    },
                });
            if (want("helpers"))
            {
                // The unloading visit, wide: the second cart handler rides in on the tractor's deck and the second hold
                // handler on the loader; both get off, walk to work and back, and ride away.
                bool placed = false, seen = false; Vector3 eye = Vector3.zero, look = Vector3.zero; float leaving = -1;
                clips.Add(new Clip
                {
                    name = "helpers-arrive-leave", maxSeconds = 90,
                    starts = () => { var j = Job(); return !seen && j?.State == BaggageTurnaroundState.Docking && j.Fleet.Crew.HoldProgress > 0f; },
                    ends = () =>
                    {
                        var j = Job(); if (j == null) return true;
                        if (leaving < 0 && j.State == BaggageTurnaroundState.ArrivalsDrive && j.Fleet.TrainSpeed > .5f) leaving = time;
                        bool end = leaving >= 0 && time > leaving+3f; if (end) seen = true; return end;
                    },
                    aim = cam =>
                    {
                        if (!placed)
                        {
                            // Between the loader and where the train will stop (the belt foot), from the open side.
                            var job = Job(); var foot = airport.TransformPoint(BaggageLoadPath.Point(job.Fleet,job.Plan,1));
                            var loader = airport.TransformPoint(new Vector3(job.Fleet.LoaderPose.position.x,0,job.Fleet.LoaderPose.position.y));
                            look = Vector3.Lerp(foot,loader,.45f); look.y = .9f;
                            var away = foot-airport.TransformPoint(job.Plan.door); away.y = 0; away.Normalize();
                            var side = Vector3.Cross(Vector3.up,away);
                            eye = look+away*9.5f+side*3.5f+Vector3.up*6f; placed = true;
                        }
                        Aim(cam,eye,look);
                    },
                });
            }
            if (want("depot"))
            {
                // Fixed from the moment the train stops in the bay: from the conveyor side, a little behind the train, so the
                // handler (on the conveyor side of the cart), the carts and the conveyor into the depot opening are in frame.
                bool placed = false; Vector3 eye = Vector3.zero, look = Vector3.zero;
                clips.Add(new Clip
                {
                    name = "depot-handling", maxSeconds = 60,
                    starts = () => { var j = Job(); return j != null && (j.State == BaggageTurnaroundState.UnloadingArrivals || j.State == BaggageTurnaroundState.CollectingDepartures) && j.Fleet.TrainSpeed == 0f; },
                    ends = () => Job()?.State == BaggageTurnaroundState.OutboundDrive && Job().Fleet.TrainSpeed > .5f,
                    aim = cam =>
                    {
                        if (!placed)
                        {
                            var fleet = Job().Fleet;
                            var right = airport.TransformDirection(new Vector3(depot.Right.x,0,depot.Right.y)); var facing = airport.TransformDirection(new Vector3(depot.Facing.x,0,depot.Facing.y));
                            var feed = airport.TransformPoint(depot.Feed(fleet,false)); var door = depot.Door(depot.Layout.LaneFor(fleet.Station.depotBay));
                            var cart = fleet.Train.CartAt(0); var cartAt = airport.TransformPoint(new Vector3(cart.position.x,1f,cart.position.y));
                            look = Vector3.Lerp(cartAt,feed,.55f); look.y = 1f;
                            // Under the bay canopy (about 3.6 m): from behind the train on the conveyor side.
                            eye = look-facing*7f+right*3.2f+Vector3.up*1.6f; placed = true;
                            var tractor = fleet.Train.Tractor;
                            Debug.Log($"Performance review: depot geometry tractor {tractor.position} facing {tractor.facing} cart0 {cart.position} cart1 {fleet.Train.CartAt(Mathf.Min(1,fleet.Train.CartCount-1)).position} " +
                                $"feed in {depot.Feed(fleet,false)} out {depot.Feed(fleet,true)} door {door} pickup {depot.CartPickup(fleet,0)} right {depot.Right} facing {depot.Facing}");
                        }
                        Aim(cam,eye,look);
                    },
                });
            }
            if (want("depot"))
            {
                // The staff waiting in the hall (not on a team): a close look, 20 s.
                clips.Add(new Clip
                {
                    name = "depot-staff", maxSeconds = 20,
                    starts = () => Job()?.State == BaggageTurnaroundState.UnloadingArrivals,
                    ends = () => false,
                    aim = cam =>
                    {
                        var right = airport.TransformDirection(new Vector3(depot.Right.x,0,depot.Right.y)); var facing = airport.TransformDirection(new Vector3(depot.Facing.x,0,depot.Facing.y));
                        // Inside the hall with the roof off (View interior), looking back at the group.
                        depot.SetInterior(true);
                        var at = depot.Point(depot.Layout.LaneX(0)+9.2f,depot.Layout.WallTop-7.8f);
                        var look = airport.TransformPoint(new Vector3(at.x,1f,at.y));
                        Aim(cam,look+right*5.5f-facing*3.5f+Vector3.up*2.2f,look);
                    },
                });
            }
            for (int frame = 0; frame < 120000 && !clips.All(c => c.done); frame++)
            {
                float step = 1f/Fps;
                airportComponent.Simulate(step); station.Present(step); depot.Present(step); time += step;
                Skins();
                separation.Sample(station,airport,time,Job()?.State.ToString());
                if (frame%(Fps/2) == 0 && station.Performance != null && Job() != null)
                {
                    var perf = station.Performance; var f = perf.Flow; string piles = "";
                    foreach (var c in f.Carts) piles += c.entries.Count(e => e.motion >= 0)+" ";
                    Debug.Log($"Sandbox t {time:000.0} {Job().State} ts {Job().TransferSeconds:0.00} rem {f.RemainingHandling():0.0} session {f.Session}/{f.Depot} | {perf.Describe(true,0)} | {perf.Describe(true,1)} | {perf.Describe(false,0)} | {perf.Describe(false,1)} | BAY {perf.Describe(true,2)} | carts {piles}belt {f.Tokens.Count} ledger {string.Join(" ",Job().Fleet.Carts.Select(c => c.Count))}");
                }
                foreach (var clip in clips)
                {
                    if (clip.done) continue;
                    if (clip.recorder == null)
                    {
                        if (!clip.starts()) continue;
                        clip.recorder = new Recorder(Path.Combine(frames,clip.name));
                        Debug.Log($"Performance review: {clip.name} starts at game {time:0.00}");
                    }
                    clip.aim(camera); clip.recorder.Frame(camera,clip.recorded); clip.recorded += 1f/Fps;
                    if (clip.ends() || clip.recorded >= clip.maxSeconds)
                    { clip.recorder.Finish(output,clip.name); clip.recorder = null; clip.done = true; }
                }
                if (Job()?.Finished == true) break;
            }
            foreach (var clip in clips) if (clip.recorder != null) clip.recorder.Finish(output,clip.name);
            Object.DestroyImmediate(camera.gameObject);
            separation.Log("Sandbox");
        }

        /// <summary>Smallest distance between the hips of any two crew performers while both are on foot, with the pair and
        /// the moment; also the closest approach of any two visible crew members at all.</summary>
        internal sealed class Separation
        {
            static readonly string[] names = { "Tractor driver / cart handler", "Additional cart handler", "Loader driver / hold handler", "Additional hold handler", "Depot bay handler" };
            static bool Ground(int i) => i < 2 || i == 4;
            static int Worker(int i) => i == 4 ? 2 : i%2;
            readonly Dictionary<RampCrewRig,Transform> hips = new Dictionary<RampCrewRig,Transform>();
            public float least = float.PositiveInfinity, lastClose = -10f; public string what = "none";
            public void Sample(BaggageStation station, Transform airport, float time, string state)
            {
                var perf = station.Performance; if (perf == null) return;
                var rigs = station.Airport.GetComponentsInChildren<RampCrewRig>(false);
                for (int a = 0; a < 5; a++) for (int b = a+1; b < 5; b++)
                {
                    if (!perf.OnFoot(Ground(a),Worker(a)) || !perf.OnFoot(Ground(b),Worker(b))) continue;
                    var ra = rigs.FirstOrDefault(r => r.name == names[a]); var rb = rigs.FirstOrDefault(r => r.name == names[b]);
                    if (ra == null || rb == null) continue;
                    float d = Vector3.Distance(Hip(ra),Hip(rb));
                    if (d < least) { least = d; what = $"{names[a]} / {names[b]} at game {time:0.00} ({state}): {perf.Describe(Ground(a),Worker(a))} || {perf.Describe(Ground(b),Worker(b))}"; }
                    if (d < .6f && time-lastClose > 2f) { lastClose = time; Debug.Log($"Close {d:0.00} m at {time:0.00} {state}: {perf.Describe(Ground(a),Worker(a))} || {perf.Describe(Ground(b),Worker(b))}"); }
                }
            }
            Vector3 Hip(RampCrewRig rig)
            {
                if (!hips.TryGetValue(rig,out var t)) hips[rig] = t = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Hips") ?? rig.transform;
                return t.position;
            }
            public void Log(string scene) => Debug.Log($"Performance review: {scene} least hip separation on foot {least:0.000} m ({what})");
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
                target = new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                pixels = new Texture2D(Width,Height,TextureFormat.RGB24,false);
            }
            public void Frame(Camera camera, float seconds)
            {
                camera.targetTexture = target;
                if (count == 0) camera.Render();
                camera.Render();
                camera.targetTexture = null;
                var previous = RenderTexture.active; RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,Width,Height),0,0); RenderTexture.active = previous;
                Stamp(pixels,seconds.ToString("000.00",System.Globalization.CultureInfo.InvariantCulture));
                pixels.Apply(false);
                File.WriteAllBytes(Path.Combine(folder,$"frame_{count:00000}.jpg"),pixels.EncodeToJPG(90)); count++;
            }
            public void Finish(string output, string name)
            {
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
                Directory.CreateDirectory(output);
                string mp4 = Path.GetFullPath(Path.Combine(output,name+".mp4")), sheet = Path.GetFullPath(Path.Combine(output,name+"-sheet.png"));
                string input = Path.Combine(Path.GetFullPath(folder),"frame_%05d.jpg");
                Run($"-y -loglevel error -framerate {Fps} -i \"{input}\" -c:v libx264 -pix_fmt yuv420p -crf 21 -movflags +faststart \"{mp4}\"");
                int every = Mathf.Max(1,Mathf.CeilToInt(count/24f));
                Run($"-y -loglevel error -i \"{input}\" -vf \"select='not(mod(n\\,{every}))',scale={Width/2}:{Height/2},tile=4x6:padding=4:color=black\" -frames:v 1 \"{sheet}\"");
                Debug.Log($"Performance review: wrote {mp4} and {sheet} ({count} frames, every {every} on the sheet)");
            }
            static void Run(string args)
            {
                var info = new ProcessStartInfo(Ffmpeg,args) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                using var process = Process.Start(info); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("ffmpeg failed: "+error);
            }
        }

        // 3x5 digits so the frames carry the clip time without ffmpeg's drawtext.
        static readonly string[] glyphs =
        {
            "111101101101111","010110010010111","111001111100111","111001111001111","101101111001001",
            "111100111001111","111100111101111","111001001001001","111101111101111","111101111001111",
        };
        static void Stamp(Texture2D texture, string text)
        {
            const int scale = 3, x0 = 6, y0 = 6;
            for (int y = 0; y < 7*scale+4; y++) for (int x = 0; x < text.Length*4*scale+8; x++) texture.SetPixel(x0-4+x,y0-4+y,Color.black);
            int cursor = x0;
            foreach (char c in text)
            {
                if (c == '.') { for (int y = 0; y < scale; y++) for (int x = 0; x < scale; x++) texture.SetPixel(cursor+x,y0+y,Color.white); cursor += 2*scale; continue; }
                var g = glyphs[c-'0'];
                for (int row = 0; row < 5; row++) for (int col = 0; col < 3; col++)
                    if (g[row*3+col] == '1')
                        for (int y = 0; y < scale; y++) for (int x = 0; x < scale; x++) texture.SetPixel(cursor+col*scale+x,y0+(4-row)*scale+y,Color.white);
                cursor += 4*scale;
            }
        }
    }
}
