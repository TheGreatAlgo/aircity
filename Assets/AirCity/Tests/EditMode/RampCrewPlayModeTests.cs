using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Characters;
using AirCity.Dev;
using AirCity.EditorTools.Characters;
using AirCity.EditorTools.Vehicles;
using AirCity.Traffic;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    /// <summary>
    /// The ramp crew in Play mode, where EditMode tests cannot reach (runtime-only Unity object creation, LateUpdate
    /// presentation, Camera.main, real frame time): every worker's bones stay finite and attached through a whole
    /// turnaround while the airport clock runs 0, 1, 12, 30 and back to 0, and while workers leave and re-enter the
    /// far-camera skip.
    /// </summary>
    public sealed class RampCrewPlayModeTests
    {
        float savedCapture;
        [UnitySetUp] public IEnumerator Open() { savedCapture = Time.captureDeltaTime; yield break; }
        [UnityTearDown] public IEnumerator Close()
        {
            Time.captureDeltaTime = savedCapture;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        /// <summary>Rest distance of every rig bone from its parent, read from the prefab.</summary>
        static Dictionary<string, float> RestLengths()
        {
            var lengths = new Dictionary<string, float>();
            var prefab = RampCrewSetup.Prefab;
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                if (t.parent != null && t.name != "Hips" && Bone(t.name)) lengths[t.name] = t.localPosition.magnitude;
            return lengths;
        }
        static bool Bone(string name) => name == "Spine" || name == "Chest" || name == "Neck" || name == "Head" ||
            name.StartsWith("Clavicle_") || name.StartsWith("UpperArm_") || name.StartsWith("Forearm_") || name.StartsWith("Hand_") ||
            name.StartsWith("Fingers_") || name.StartsWith("Thumb_") || name.StartsWith("Thigh_") || name.StartsWith("Shin_") ||
            name.StartsWith("Foot_") || name.StartsWith("Toe_");

        sealed class Watch
        {
            readonly Dictionary<string, float> rest = RestLengths();
            readonly Dictionary<RampCrewRig, Transform[]> bones = new Dictionary<RampCrewRig, Transform[]>();
            public float worstStretch; public string worstAt = "none";
            public int frames, rigFrames;
            public readonly HashSet<string> seen = new HashSet<string>();
            readonly List<RampCrewRig> rigs = new List<RampCrewRig>();

            public void Check(string when)
            {
                frames++;
                // From the airports' hierarchy: FindObjectsByType skips the marshaller, which is marked DontSave.
                rigs.Clear();
                foreach (var airport in Object.FindObjectsByType<Airport>(FindObjectsInactive.Exclude)) rigs.AddRange(airport.GetComponentsInChildren<RampCrewRig>(false));
                foreach (var rig in Object.FindObjectsByType<RampCrewRig>(FindObjectsInactive.Exclude)) if (!rigs.Contains(rig)) rigs.Add(rig);
                foreach (var rig in rigs)
                {
                    if (!bones.TryGetValue(rig, out var list))
                        bones[rig] = list = rig.GetComponentsInChildren<Transform>(true).Where(t => Bone(t.name) || t.name == "Hips").ToArray();
                    rigFrames++; seen.Add(rig.name);
                    Transform hips = null;
                    foreach (var t in list)
                    {
                        var p = t.position; var q = t.rotation;
                        Assert.That(float.IsNaN(p.x + p.y + p.z + q.x + q.y + q.z + q.w) || float.IsInfinity(p.x + p.y + p.z), Is.False, $"{rig.name} {t.name} is not finite {when}");
                        if (t.name == "Hips") { hips = t; continue; }
                        if (!rest.TryGetValue(t.name, out float length)) continue;
                        float stretch = Mathf.Abs(t.localPosition.magnitude - length);
                        if (stretch > worstStretch) { worstStretch = stretch; worstAt = $"{rig.name} {t.name} {when}"; }
                    }
                    if (hips != null)
                        Assert.That(Vector3.Distance(hips.position, rig.transform.position), Is.LessThan(1.3f), $"{rig.name} hips torn from the root {when}");
                }
            }
        }

        static IEnumerator Frames(int count, Watch watch, string label, Func<bool> stop = null)
        {
            for (int i = 0; i < count && (stop == null || !stop()); i++) { yield return null; watch.Check(label); }
        }

        /// <summary>Camera far away (every worker skipped), then back over a worker.</summary>
        static IEnumerator FarAndBack(Watch watch, Vector3 look)
        {
            var camera = Camera.main; if (camera == null) yield break;
            var orbit = camera.GetComponent<DevOrbitCamera>(); if (orbit != null) orbit.enabled = false;
            var home = camera.transform.position; var homeRotation = camera.transform.rotation;
            camera.transform.position = look + Vector3.up * 900f;
            yield return Frames(20, watch, "while skipped");
            camera.transform.position = look + new Vector3(4f, 2.5f, 4f); camera.transform.LookAt(look);
            yield return Frames(20, watch, "after re-entering view");
            camera.transform.SetPositionAndRotation(home, homeRotation);
        }

        [UnityTest]
        public IEnumerator BulkTurnaround_CrewStaysWholeThroughEveryClockSpeed()
        {
            const string renderTargetWarning = "Releasing render texture that is set to be RenderTexture.active!";
            bool warned = false;
            void Loading(string message, string stack, LogType type) { if (type == LogType.Warning && message == renderTargetWarning) warned = true; }
            Application.logMessageReceived += Loading;
            try { EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning, renderTargetWarning);
            var setup = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            setup.startWithArrival = true; setup.autoDeparture = true;
            warned = false; Application.logMessageReceived += Loading;
            try { yield return new EnterPlayMode(); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning, renderTargetWarning);
            Time.captureDeltaTime = 1f / 60f;
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            yield return null;
            Assert.That(demo.Problem, Is.Null);
            var airport = demo.airport; var watch = new Watch();
            bool marshalled = false, dismount = false, unloading = false, loading = false, boarding = false, droveOff = false;
            void Note()
            {
                var job = demo.Job; var crew = job?.Fleet?.Crew;
                if (airport.Marshalling.Crew.Count > 0 && !marshalled)
                {
                    // Watch the marshaller from close by (the presentation hides it beyond 250 m of the camera).
                    var camera = Camera.main; var orbit = camera.GetComponent<DevOrbitCamera>(); if (orbit != null) orbit.enabled = false;
                    var at = airport.transform.TransformPoint(airport.Marshalling.Crew[0].Position);
                    camera.transform.position = at + new Vector3(6f, 3f, 6f); camera.transform.LookAt(at + Vector3.up);
                }
                marshalled |= airport.Marshalling.Crew.Count > 0;
                if (crew == null) return;
                dismount |= crew.GroundProgress > 0 && !crew.GroundReady;
                boarding |= job.State == BaggageTurnaroundState.Docking && crew.HoldProgress > 0;
                unloading |= job.State == BaggageTurnaroundState.UnloadingHold && job.TransferBatch != null;
                loading |= job.State == BaggageTurnaroundState.LoadingHold && job.TransferBatch != null;
                droveOff |= demo.Departure != null && demo.Departure.Phase == FlightPhase.Taxi;
            }
            airport.timeScale = 0; yield return Frames(30, watch, "paused at start");
            airport.timeScale = 1; yield return Frames(120, watch, "at x1");
            airport.timeScale = 12; for (int i = 0; i < 600; i++) { yield return null; watch.Check("at x12"); Note(); }
            airport.timeScale = 30;
            float deadline = Time.realtimeSinceStartup + 600f; int sinceFar = 0;
            while (!droveOff && Time.realtimeSinceStartup < deadline)
            {
                yield return null; watch.Check($"at x30 ({demo.Job?.State})"); Note();
                if (++sinceFar == 900 && demo.Job?.Fleet?.Crew != null)
                {
                    sinceFar = 0;
                    yield return FarAndBack(watch, airport.transform.TransformPoint(demo.Job.Fleet.Crew.GroundPose.position));
                }
                // Mid-transfer, drop to x1 for a while: human-rate stepping on top of the work.
                if (loading && demo.Job.State == BaggageTurnaroundState.LoadingHold && airport.timeScale > 1)
                { airport.timeScale = 1; yield return Frames(180, watch, "x1 while loading"); airport.timeScale = 30; }
            }
            airport.timeScale = 0; yield return Frames(30, watch, "paused at end");
            TestContext.Out.WriteLine($"Play-mode crew: {watch.frames} frames, {watch.rigFrames} worker poses, workers {string.Join(", ", watch.seen)}; " +
                      $"worst bone stretch {watch.worstStretch * 100f:0.0} cm at {watch.worstAt}");
            Assert.That(marshalled && dismount && unloading && loading && boarding && droveOff, Is.True,
                $"marshalled {marshalled} dismount {dismount} unloading {unloading} loading {loading} boarding {boarding} drove off {droveOff}; {demo.Job?.State}: {demo.Job?.Waiting}");
            Assert.That(watch.seen.Any(n => n.StartsWith("Marshaller")), Is.True, "the arrival marshaller was posed");
            Assert.That(watch.worstStretch, Is.LessThan(.02f), "bones stay attached: " + watch.worstAt);
            LogAssert.NoUnexpectedReceived();
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ContainerTurnaround_OperatorsAndBulkCrewStayWhole()
        {
            // The editor may warn while it swaps render targets loading a large scene (see BaggageSandboxTests).
            const string renderTargetWarning = "Releasing render texture that is set to be RenderTexture.active!";
            bool warned = false;
            void Loading(string message, string stack, LogType type) { if (type == LogType.Warning && message == renderTargetWarning) warned = true; }
            Application.logMessageReceived += Loading;
            try { EditorSceneManager.OpenScene(ContainerTurnaroundSetup.ScenePath); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning, renderTargetWarning);
            warned = false; Application.logMessageReceived += Loading;
            try { yield return new EnterPlayMode(); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning, renderTargetWarning);
            Time.captureDeltaTime = 1f / 60f;
            var demo = Object.FindAnyObjectByType<ContainerTurnaroundDemo>();
            yield return null;
            Assert.That(demo.Problem, Is.Null);
            var airport = demo.airport; var watch = new Watch();
            airport.timeScale = 1; yield return Frames(60, watch, "at x1");
            airport.timeScale = 30;
            float deadline = Time.realtimeSinceStartup + 600f; bool bulkLoading = false;
            while (!(demo.Job.DepartureReady && (demo.Bulk == null || demo.Bulk.DepartureReady)) && Time.realtimeSinceStartup < deadline)
            {
                yield return null; watch.Check($"at x30 ({demo.Job.State}/{demo.Bulk?.State})");
                bulkLoading |= demo.Bulk != null && demo.Bulk.TransferBatch != null;
            }
            airport.timeScale = 0; yield return Frames(20, watch, "paused");
            TestContext.Out.WriteLine($"Play-mode container crew: {watch.frames} frames, {watch.rigFrames} worker poses; worst bone stretch {watch.worstStretch * 100f:0.0} cm at {watch.worstAt}");
            Assert.That(demo.Job.DepartureReady, Is.True, demo.Job.State.ToString());
            Assert.That(bulkLoading || demo.Bulk == null, Is.True);
            Assert.That(watch.worstStretch, Is.LessThan(.02f), "bones stay attached: " + watch.worstAt);
            LogAssert.NoUnexpectedReceived();
            yield return new ExitPlayMode();
        }
            [UnityTest]
        public IEnumerator Sandbox_CrewStaysWholeWithAutomaticTrafficAndHandling()
        {
            const string renderTargetWarning = "Releasing render texture that is set to be RenderTexture.active!";
            bool warned = false;
            void Loading(string message, string stack, LogType type) { if (type == LogType.Warning && message == renderTargetWarning) warned = true; }
            Application.logMessageReceived += Loading;
            try { EditorSceneManager.OpenScene(AirCity.EditorTools.Aircraft.AircraftSetup.SandboxScene, OpenSceneMode.Single); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning, renderTargetWarning);
            warned = false; Application.logMessageReceived += Loading;
            try { yield return new EnterPlayMode(); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning, renderTargetWarning);
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            var airport = Object.FindFirstObjectByType<Airport>(); var watch = new Watch();
            airport.timeScale = 1; yield return Frames(120, watch, "at x1");
            airport.timeScale = 4; yield return Frames(300, watch, "at x4");
            airport.timeScale = 30;
            float deadline = Time.realtimeSinceStartup + 240f; bool working = false;
            while (Time.realtimeSinceStartup < deadline && watch.frames < 6000)
            {
                yield return null; watch.Check("at x30");
                working |= airport.Baggage.Jobs.Any(j => j.TransferBatch != null);
            }
            airport.timeScale = 0; yield return Frames(30, watch, "paused");
            TestContext.Out.WriteLine($"Play-mode sandbox crew: {watch.frames} frames, {watch.rigFrames} worker poses, workers {string.Join(", ", watch.seen)}; worst bone stretch {watch.worstStretch * 100f:0.0} cm at {watch.worstAt}");
            Assert.That(working, Is.True, "baggage was handled");
            Assert.That(watch.worstStretch, Is.LessThan(.02f), "bones stay attached: " + watch.worstAt);
            LogAssert.NoUnexpectedReceived();
            yield return new ExitPlayMode();
        }
    }
}
