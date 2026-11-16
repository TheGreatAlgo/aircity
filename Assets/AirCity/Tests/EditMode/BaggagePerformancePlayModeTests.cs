using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AirCity.Characters;
using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    /// <summary>
    /// The human-pace baggage crew performance in Play mode: the standard bulk turnaround and the four-worker Sandbox
    /// (with its depot and hired second handlers) while the airport clock runs 0, 1, 12, 30 and back to 0, with a
    /// blocked-access pause during handling at x1. No exceptions or errors; no last-resort seat placements at x1 and x12;
    /// every carried bag within 2 cm of both palms at x1 (the rig eases a grip over about 0.2 s of screen time, which at
    /// x12 and x30 spans a whole lift: those gaps are reported, not asserted); no performer faster than 2.6 m/s (a jog); and no two performers on foot
    /// closer than 0.6 m, hip to hip.
    /// </summary>
    public sealed class BaggagePerformancePlayModeTests
    {
        const float PalmGap = .02f, TopSpeed = 2.6f, LeastSeparation = .6f, FarDistance = 250f;
        const string RenderTargetWarning = "Releasing render texture that is set to be RenderTexture.active!";
        const string PlacementWarning = "Baggage crew performance:";

        float savedCapture;
        [UnitySetUp] public IEnumerator Open() { savedCapture = Time.captureDeltaTime; yield break; }
        [UnityTearDown] public IEnumerator Close()
        {
            Time.captureDeltaTime = savedCapture;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        /// <summary>What the performance shows, read every frame after the presentation.</summary>
        sealed class Measure
        {
            readonly BaggageStation station;
            readonly Dictionary<RampCrewRig,Transform> hips = new Dictionary<RampCrewRig,Transform>();
            public float worstGap, fastGap, gap12, least = float.PositiveInfinity; public string gapAt = "none", leastAt = "none", fastGapAt = "none", gap12At = "none";
            /// <summary>At x12 the rig's grip easing (about 0.2 s of screen time) spans a whole lift: reported, not asserted.</summary>
            public bool x12;
            /// <summary>At x30 a frame is half a game second and the rig eases at most 0.1 s a frame: hands may trail there.</summary>
            public bool fast;
            public int carriedFrames, frames;
            public Measure(BaggageStation station) { this.station = station; }
            static readonly (bool ground, int worker)[] performers = { (true,0), (true,1), (true,2), (false,0), (false,1) };

            public void Check(string when)
            {
                frames++;
                // Keep the crew within the rigs' far-camera distance (beyond it bones are not posed, by design).
                var cam = Camera.main; var driver = station.CrewRig(true,0);
                if (cam != null && driver != null)
                {
                    var orbit = cam.GetComponent<DevOrbitCamera>(); if (orbit != null) orbit.enabled = false;
                    var at = driver.transform.position; cam.transform.position = at+new Vector3(0f,35f,-25f); cam.transform.LookAt(at);
                }
                var perf = station.Performance; if (perf == null) return;
                var airport = station.Airport.transform;
                foreach (var (ground, worker) in performers)
                {
                    var rig = station.CrewRig(ground,worker);
                    if (rig == null || !rig.isActiveAndEnabled || !perf.HandTargets(ground,worker,out var l,out var r)) continue;
                    // Rigs beyond the far-camera distance keep their last bones (by design); only posed rigs count.
                    var camera = Camera.main;
                    if (camera != null && (camera.transform.position-rig.transform.position).sqrMagnitude > FarDistance*FarDistance) continue;
                    carriedFrames++;
                    // The bag as drawn (between the palms the rig shows) against both palms.
                    float gap = station.CarriedPalmGap;
                    string at = $"{(ground ? "ground" : "hold")} {worker} {when}: {perf.Describe(ground,worker)}";
                    if (fast) { if (gap > fastGap) { fastGap = gap; fastGapAt = at; } }
                    else if (x12) { if (gap > gap12) { gap12 = gap; gap12At = at; } }
                    else if (gap > worstGap) { worstGap = gap; gapAt = at; }
                }
                for (int a = 0; a < performers.Length; a++)
                for (int b = a+1; b < performers.Length; b++)
                {
                    var pa = performers[a]; var pb = performers[b];
                    if (!perf.OnFoot(pa.ground,pa.worker) || !perf.OnFoot(pb.ground,pb.worker)) continue;
                    var ra = station.CrewRig(pa.ground,pa.worker); var rb = station.CrewRig(pb.ground,pb.worker);
                    if (ra == null || rb == null || !ra.isActiveAndEnabled || !rb.isActiveAndEnabled) continue;
                    float d = Vector3.Distance(Hip(ra),Hip(rb));
                    if (d < least) { least = d; leastAt = $"{when}: {perf.Describe(pa.ground,pa.worker)} || {perf.Describe(pb.ground,pb.worker)}"; }
                }
            }
            Vector3 Hip(RampCrewRig rig)
            {
                if (!hips.TryGetValue(rig,out var t)) hips[rig] = t = rig.GetComponentsInChildren<Transform>(true).First(x => x.name == "Hips");
                return t.position;
            }
        }

        /// <summary>Placement warnings are expected only at x30 (where the frame is half a game second); any other is a failure.</summary>
        sealed class Placements
        {
            public int slow, fast; public bool fastClock; public readonly List<string> messages = new List<string>();
            public void Log(string message, string stack, LogType type)
            {
                if (type != LogType.Warning || !message.StartsWith(PlacementWarning)) return;
                if (fastClock) { fast++; messages.Add(message); } else slow++;
            }
        }

        /// <summary>Run the clock schedule: 0, then x1 until <paramref name="working"/> has held for a while, a blocked-access
        /// pause, x1 again, x12 until <paramref name="late"/>, x30 until <paramref name="finished"/>, and 0.</summary>
        static IEnumerator Schedule(AirCity.Airfield.Airport airport, BaggageStation station, Measure measure, Placements placements,
            Func<bool> approaching, Func<bool> working, Func<bool> late, Func<bool> finished, Func<string> state)
        {
            IEnumerator Frames(int count, string label, Func<bool> stop = null)
            {
                for (int i = 0; i < count && (stop == null || !stop()); i++) { yield return null; measure.Check($"{label} ({state()})"); }
            }
            // The aircraft's arrival (traffic, marshalling) is not the crew's work: it runs at x30 until the baggage
            // equipment is on its way, then the schedule starts.
            airport.timeScale = 30; placements.fastClock = true;
            float until = Time.realtimeSinceStartup+240f;
            while (!approaching() && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(approaching(), Is.True, $"the baggage equipment never set off ({state()})");
            placements.fastClock = false;
            airport.timeScale = 0; yield return Frames(30,"paused at start");
            airport.timeScale = 1; int held = 0;
            yield return Frames(6000,"at x1",() => (held = working() ? held+1 : 0) > 240);
            Assert.That(held, Is.GreaterThan(240), $"handling did not start at x1 ({state()})");
            station.accessBlocked = true; yield return Frames(150,"access blocked at x1");
            station.accessBlocked = false; yield return Frames(120,"access released at x1");
            airport.timeScale = 12; measure.x12 = true; yield return Frames(6000,"at x12",late); measure.x12 = false;
            Assert.That(late(), Is.True, $"x12 phase did not reach its end ({state()})");
            airport.timeScale = 30; placements.fastClock = true; measure.fast = true;
            float deadline = Time.realtimeSinceStartup+300f;
            while (!finished() && Time.realtimeSinceStartup < deadline) { yield return null; measure.Check($"at x30 ({state()})"); }
            Assert.That(finished(), Is.True, $"did not finish at x30 ({state()})");
            airport.timeScale = 0; placements.fastClock = false; measure.fast = false; yield return Frames(30,"paused at end");
        }

        static void Verify(string scene, Measure measure, Placements placements)
        {
            foreach (var message in placements.messages) LogAssert.Expect(LogType.Warning,message);
            TestContext.Out.WriteLine($"{scene}: {measure.frames} frames, {measure.carriedFrames} carrying samples; worst palm gap {measure.worstGap*100:0.00} cm at {measure.gapAt} (x12: {measure.gap12*100:0.0} cm at {measure.gap12At}; x30: {measure.fastGap*100:0.0} cm at {measure.fastGapAt}); " +
                $"least hip separation {measure.least:0.000} m at {measure.leastAt}; fastest {BaggageCrewPerformance.MaxStandingSpeed:0.00} m/s ({BaggageCrewPerformance.MaxStandingWhat}); " +
                $"placements x1/x12 {placements.slow}, x30 {placements.fast}; direct bags {BaggageCrewPerformance.DirectBags}; step {BaggageCrewPerformance.StepMicroseconds:0.0} us, {BaggageCrewPerformance.StepBytes:0.0} B");
            Assert.That(placements.slow, Is.Zero, "last-resort seat placements at x1 and x12");
            Assert.That(measure.carriedFrames, Is.GreaterThan(0), "no bag was carried");
            Assert.That(measure.worstGap, Is.LessThanOrEqualTo(PalmGap), $"palm-to-bag gap at {measure.gapAt}");
            Assert.That(BaggageCrewPerformance.MaxStandingSpeed, Is.LessThanOrEqualTo(TopSpeed+.005f), $"top speed ({BaggageCrewPerformance.MaxStandingWhat})");
            Assert.That(measure.least, Is.GreaterThanOrEqualTo(LeastSeparation), $"two performers on foot too close at {measure.leastAt}");
        }

        [UnityTest]
        public IEnumerator BulkTurnaround_PerformanceHoldsThroughEveryClockSpeed()
        {
            // EnterPlayMode must be yielded by the test itself (a nested enumerator does not survive entering Play mode).
            bool warned = false;
            void Loading(string message, string stack, LogType type) { if (type == LogType.Warning && message == RenderTargetWarning) warned = true; }
            Application.logMessageReceived += Loading;
            try { EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning,RenderTargetWarning);
            var setup = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            setup.startWithArrival = true; setup.autoDeparture = true;
            warned = false; Application.logMessageReceived += Loading;
            try { yield return new EnterPlayMode(); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning,RenderTargetWarning);
            Time.captureDeltaTime = 1f/60f;
            yield return null;
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            Assert.That(demo.Problem, Is.Null);
            BaggageCrewPerformance.ResetCounters();
            var placements = new Placements(); Application.logMessageReceived += placements.Log;
            var measure = new Measure(demo.station);
            try
            {
                yield return Schedule(demo.airport,demo.station,measure,placements,
                    () => demo.Job?.Plan != null,
                    () => demo.Job?.State == BaggageTurnaroundState.UnloadingHold && demo.station.Performance != null && demo.station.Performance.OnFoot(true,0),
                    () => demo.Job?.State == BaggageTurnaroundState.LoadingHold && demo.Job.TransferSeconds > 2f,
                    () => demo.Job == null || demo.Job.Finished, () => demo.Job?.State.ToString() ?? "-");
            }
            finally { Application.logMessageReceived -= placements.Log; demo.station.accessBlocked = false; }
            Assert.That(demo.Job.DepartureReady, Is.True, $"{demo.Job.State}: {demo.Job.Waiting}");
            Verify("Turnaround",measure,placements);
        }

        [UnityTest]
        public IEnumerator Sandbox_FourWorkerPerformanceHoldsThroughEveryClockSpeed()
        {
            bool warned = false;
            void Loading(string message, string stack, LogType type) { if (type == LogType.Warning && message == RenderTargetWarning) warned = true; }
            Application.logMessageReceived += Loading;
            try { EditorSceneManager.OpenScene(AirCity.EditorTools.Aircraft.AircraftSetup.SandboxScene, OpenSceneMode.Single); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning,RenderTargetWarning);
            warned = false; Application.logMessageReceived += Loading;
            try { yield return new EnterPlayMode(); }
            finally { Application.logMessageReceived -= Loading; }
            if (warned) LogAssert.Expect(LogType.Warning,RenderTargetWarning);
            Time.captureDeltaTime = 1f/60f;
            yield return null;
            var depot = Object.FindAnyObjectByType<BaggageDepot>(); var station = depot.Teams.Single(); var airport = depot.Airport;
            BaggageTurnaround Job() => airport.Baggage.Jobs.Count > 0 ? airport.Baggage.Jobs[0] : null;
            BaggageCrewPerformance.ResetCounters();
            var placements = new Placements(); Application.logMessageReceived += placements.Log;
            var measure = new Measure(station); bool four = false, depotWorked = false;
            try
            {
                yield return Schedule(airport,station,measure,placements,
                    () => Job()?.Plan != null,
                    () =>
                    {
                        var j = Job(); four |= j?.Fleet.Workers >= 4;
                        return j?.State == BaggageTurnaroundState.UnloadingHold && station.Performance != null && station.Performance.OnFoot(true,1);
                    },
                    () =>
                    {
                        var j = Job(); depotWorked |= station.Performance != null && station.Performance.OnFoot(true,2);
                        return j?.State == BaggageTurnaroundState.LoadingHold && j.TransferSeconds > 2f;
                    },
                    () => Job() == null || Job().Finished, () => Job()?.State.ToString() ?? "-");
            }
            finally { Application.logMessageReceived -= placements.Log; station.accessBlocked = false; }
            Assert.That(four, Is.True, "the team had four workers");
            Assert.That(depotWorked, Is.True, "the depot bay handler worked the depot handover");
            Verify("Sandbox",measure,placements);
        }
    }
}
