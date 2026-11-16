using System;
using System.Collections.Generic;
using System.IO;
using AirCity.Airfield;
using AirCity.EditorTools.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>Records the stocked review's approach, wing work and clearing in actual game time.</summary>
    public static class DeicingAnimationReview
    {
        const int Frames = 400;
        const float FrameSeconds = .25f;
        const string Folder = "Captures/Deicing/Animation";
        [Serializable] sealed class TruckFrame
        {
            public int pad, index;
            public Vector2 position, facing;
            public float speed, onboard, targetProgress;
            public int target, targets;
            public string stage, status;
        }
        [Serializable] sealed class Frame
        {
            public int frame;
            public float clock, storage, onboard, pending, applied, accountedFluid;
            public int particles;
            public List<TruckFrame> trucks = new List<TruckFrame>();
        }
        [Serializable] sealed class Report { public List<Frame> frames = new List<Frame>(); }

        public static void RunFromCommandLine()
        {
            try
            {
                if (!File.Exists(DeicingReview.ScenePath))
                    throw new InvalidOperationException("Create the updated DeicingReview scene before recording its animation.");
                EditorSceneManager.OpenScene(DeicingReview.ScenePath, OpenSceneMode.Single);
                var airport = UnityEngine.Object.FindAnyObjectByType<Airport>();
                if (airport == null) throw new InvalidOperationException("Review scene has no Airport.");
                var rig = airport.GetComponent<DeicingRig>();
                if (rig == null) rig = airport.gameObject.AddComponent<DeicingRig>();
                var camera = Camera.main;
                if (camera == null) camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
                if (camera == null) throw new InvalidOperationException("Review scene has no camera.");
                Directory.CreateDirectory(Folder);
                foreach (var old in Directory.GetFiles(Folder,"frame-*.png")) File.Delete(old);
                var report = new Report();
                float completedAt = -1f;
                for (int frame = 0; frame < Frames; frame++)
                {
                    // Keep presentation stepping with simulation: skipping directly by a second would hide the
                    // acceleration, steering and boom deployment that this review exists to inspect.
                    for (int step = 0; step < Mathf.RoundToInt(FrameSeconds / .05f); step++)
                    { airport.Simulate(.05f); rig.Refresh(); }
                    SceneCapture.Capture(camera, $"{Folder}/frame-{frame:000}.png", 960, 640);
                    report.frames.Add(Snapshot(airport, rig, frame));
                    if (frame == 40)
                    {
                        var before = Snapshot(airport, rig, frame);
                        float timeScale = airport.timeScale; airport.timeScale = 0f;
                        for (int refresh = 0; refresh < 30; refresh++) rig.Refresh();
                        SceneCapture.Capture(camera, $"{Folder}/paused.png", 960, 640);
                        var after = Snapshot(airport, rig, frame); airport.timeScale = timeScale;
                        if (before.clock != after.clock || before.accountedFluid != after.accountedFluid || before.particles != after.particles)
                            throw new InvalidOperationException("Paused deicing advanced while recording its animation.");
                    }
                    foreach (var facility in airport.Deicing.Facilities)
                        if (airport.Deicing.JobFor(facility)?.Completed == true && completedAt < 0f) completedAt = airport.Deicing.Clock;
                    if (completedAt >= 0f && airport.Deicing.Clock >= completedAt+2f) break;
                }
                if (completedAt < 0f) throw new InvalidOperationException("Animation did not finish treatment within 100 game seconds.");
                File.WriteAllText($"{Folder}/states.json", JsonUtility.ToJson(report, true));
                Debug.Log($"Deicing animation: recorded {report.frames.Count} frames at {FrameSeconds} game second intervals, plus pause capture and state ledger.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        static Frame Snapshot(Airport airport, DeicingRig rig, int index)
        {
            var frame = new Frame { frame = index, clock = airport.Deicing.Clock, particles = rig.SprayParticleCount };
            foreach (var area in airport.Deicing.WorkAreas)
            { frame.storage += area.fluidLitres; frame.pending += area.deliveryLitres; }
            foreach (var facility in airport.Deicing.Facilities)
            {
                if (facility == null) continue;
                var job = airport.Deicing.JobFor(facility);
                if (job != null) frame.applied += job.UsedLitres;
                int truckIndex = 0;
                foreach (var truck in airport.Deicing.TrucksFor(facility))
                {
                    frame.onboard += truck.FluidLitres;
                    frame.trucks.Add(new TruckFrame { pad = facility.standId, index = truckIndex++,
                        position = truck.Position, facing = truck.Facing, speed = truck.Speed, onboard = truck.FluidLitres,
                        target = truck.TargetIndex, targets = truck.TargetCount, targetProgress = truck.TargetProgress,
                        stage = truck.Stage.ToString(), status = truck.Status });
                }
            }
            frame.accountedFluid = frame.storage + frame.onboard + frame.pending + frame.applied;
            return frame;
        }
    }
}
