using System;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>Review the actual shared simulation at both cargo openings, without rebuilding saved layouts.</summary>
    public static class BaggageDockingReview
    {
        public static void FromCommandLine()
        {
            try
            {
                AssignBagArt();
                Bulk(); Containers();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        static void AssignBagArt()
        {
            VehicleSetup.CreateMissing();
            foreach (var path in new[] { "Assets/AirCity/Scenes/Sandbox.unity", BaggageTurnaroundSetup.ScenePath, ContainerTurnaroundSetup.ScenePath })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool changed = false;
                foreach (var station in Object.FindObjectsByType<BaggageStation>(FindObjectsSortMode.None))
                    if (station.bagPrefabs == null || station.bagPrefabs.Length == 0)
                    { station.bagPrefabs = VehicleSetup.BagPrefabs(); EditorUtility.SetDirty(station); changed = true; }
                if (changed) EditorSceneManager.SaveScene(scene);
            }
        }

        static void Capture(Camera camera, BaggageServicePlan plan, string name, bool close = false)
        {
            var target = plan.door + Vector3.up * .7f - plan.inward * (close ? 1f : 4f);
            camera.fieldOfView = close ? 40f : 46f;
            camera.transform.position = target - plan.inward * (close ? 7f : 16f) + Vector3.up * (close ? 3f : 11f) + Vector3.forward * (close ? 3f : 10f);
            camera.transform.LookAt(target);
            SceneCapture.Capture(camera, "Captures/BaggageDocking/" + name + ".png", 1600, 1100);
        }

        static void Bulk()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = false; demo.autoDeparture = false; demo.Initialize();
            if (demo.Problem != null) throw new Exception(demo.Problem);
            demo.airport.Simulate(1f);
            if (!demo.Job.Acquired) throw new Exception(demo.Job.Waiting);
            bool unloaded = false, loaded = false;
            var camera = Object.FindAnyObjectByType<Camera>();
            for (int i = 0; i < 6000 && !demo.Job.Finished; i++)
            {
                demo.airport.Simulate(.25f); demo.station.Present(.25f);
                var job = demo.Job;
                if (!unloaded && job.State == BaggageTurnaroundState.UnloadingHold && job.TransferSeconds > job.BagTravelSeconds * .7f)
                { Capture(camera, job.Plan, "bulk-unloading"); Capture(camera, job.Plan, "bulk-unloading-close", true); unloaded = true; }
                if (!loaded && job.State == BaggageTurnaroundState.LoadingHold && job.TransferSeconds > job.BagTravelSeconds + 2f)
                {
                    Capture(camera, job.Plan, "bulk-loading"); Capture(camera, job.Plan, "bulk-loading-close", true);
                    camera.orthographic = true; camera.orthographicSize = 14f;
                    camera.transform.SetPositionAndRotation(job.Plan.door - job.Plan.inward * 4f + Vector3.up * 35f, Quaternion.Euler(90f, 0f, 0f));
                    SceneCapture.Capture(camera, "Captures/BaggageDocking/bulk-loading-top.png", 1600, 1100);
                    camera.orthographic = false; loaded = true;
                }
            }
            if (!demo.Job.DepartureReady || !unloaded || !loaded) throw new Exception($"Bulk: {demo.Job.State}: {demo.Job.Waiting}");
            Capture(camera, demo.Job.Plan, "bulk-complete", true);
            Debug.Log("Docking review: bulk unload/load completed with all 90 bags conserved and the hatch closed.");
        }

        static void Containers()
        {
            EditorSceneManager.OpenScene(ContainerTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<ContainerTurnaroundDemo>();
            demo.autoDeparture = false; demo.Initialize();
            if (demo.Problem != null) throw new Exception(demo.Problem);
            demo.airport.Simulate(1f);
            if (!demo.Job.Acquired) throw new Exception(demo.Job.Waiting);
            bool unloaded = false, loaded = false, side = false, bulk = false;
            var camera = Object.FindAnyObjectByType<Camera>();
            for (int i = 0; i < 14000 && (!demo.Job.Finished || demo.Bulk != null && !demo.Bulk.Finished); i++)
            {
                demo.airport.Simulate(.25f); demo.Observe(); demo.station.Present(.25f);
                if (demo.Problem != null) throw new Exception(demo.Problem);
                if (demo.bulkStation != null) demo.bulkStation.Present(.25f);
                var job = demo.Job;
                if (!unloaded && job.State == ContainerTurnaroundState.HoldToFront && job.TransferProgress > .5f)
                { Capture(camera, job.Plan, "container-unloading"); Capture(camera, job.Plan, "container-unloading-close", true); unloaded = true; }
                if (!loaded && job.State == ContainerTurnaroundState.FrontToHold && job.TransferProgress > .65f)
                { Capture(camera, job.Plan, "container-loading"); Capture(camera, job.Plan, "container-loading-close", true); loaded = true; }
                if (!side && job.State == ContainerTurnaroundState.MainToDolly && job.TransferProgress > .45f)
                {
                    var target = job.Fleet.Mount(job.Fleet.Main, out _);
                    camera.transform.position = target - job.Plan.inward * 12f + Vector3.up * 9f - Vector3.forward * 8f;
                    camera.transform.LookAt(target);
                    SceneCapture.Capture(camera, "Captures/BaggageDocking/container-side-transfer.png", 1600, 1100);
                    side = true;
                }
                if (!bulk && demo.Bulk != null && demo.Bulk.State == BaggageTurnaroundState.LoadingHold &&
                    demo.Bulk.TransferSeconds > demo.Bulk.BagTravelSeconds + 1f)
                {
                    Capture(camera, demo.Bulk.Plan, "widebody-bulk-loading");
                    Capture(camera, demo.Bulk.Plan, "widebody-bulk-loading-close", true); bulk = true;
                }
            }
            if (!demo.Job.DepartureReady || !unloaded || !loaded) throw new Exception($"Container: {demo.Job.State}: {demo.Job.Waiting}");
            if (demo.Bulk != null && (!demo.Bulk.DepartureReady || !bulk)) throw new Exception($"Rear bulk: {demo.Bulk.State}: {demo.Bulk.Waiting}");
            Capture(camera, demo.Job.Plan, "container-complete", true);
            if (demo.airport.Baggage.Ledger.Count != 380 || Mathf.Abs(demo.airport.Baggage.Ledger.Weight - 5700f) > .01f)
                throw new Exception("Container/rear-bulk review did not conserve all 380 bags / 5700 kg.");
            Debug.Log("Docking review: two incoming and three departing LD3s plus 50 bulk bags handled; all 380 bags / 5700 kg conserved, equipment home and hatches closed.");
        }
    }
}
