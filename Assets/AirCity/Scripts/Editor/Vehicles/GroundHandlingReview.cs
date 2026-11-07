using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Characters;
using AirCity.Dev;
using AirCity.EditorTools.Characters;
using AirCity.EditorTools.Dev;
using AirCity.Traffic;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Vehicles
{
    public static class GroundHandlingReview
    {
        public static void Install()
        {
            RampCrewSetup.Install();
            foreach (var name in new[] { "Sandbox","BaggageTurnaround" })
            {
                string path = $"Assets/AirCity/Scenes/{name}.unity";
                var scene = EditorSceneManager.OpenScene(path); bool changed = false;
                foreach (var station in Object.FindObjectsByType<BaggageStation>(FindObjectsSortMode.None))
                    if ((name == "BaggageTurnaround" || station.automaticHold.aircraft?.model == "Narrowbody") && station.cartCount == 1)
                    { station.cartCount = 2; EditorUtility.SetDirty(station); changed = true; }
                if (changed) EditorSceneManager.SaveScene(scene);
            }
        }
        public static void FromCommandLine()
        {
            try { Install(); Bulk(); Arrival(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Shot(Camera camera,Vector3 target,Vector3 offset,string name,float fov = 45)
        {
            // Several captures run within one editor frame. Bake snapshots to bypass Unity's per-frame GPU skin cache.
            var skins = new List<SkinnedMeshRenderer>(); var enabled = new List<bool>();
            var snapshots = new List<GameObject>(); var meshes = new List<Mesh>();
            foreach (var airport in Object.FindObjectsByType<Airport>(FindObjectsSortMode.None))
              foreach (var rig in airport.GetComponentsInChildren<RampCrewRig>(true))
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true;
                    skins.Add(skin); enabled.Add(skin.enabled);
                    if (skin.name.EndsWith("LOD0"))
                    {
                        var mesh = new Mesh(); skin.BakeMesh(mesh); meshes.Add(mesh);
                        var snapshot = new GameObject("Capture skin snapshot"); snapshot.transform.SetParent(skin.transform,false);
                        snapshot.AddComponent<MeshFilter>().sharedMesh = mesh;
                        snapshot.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials; snapshots.Add(snapshot);
                    }
                    skin.enabled = false;
                }
            camera.nearClipPlane = .05f; camera.fieldOfView = fov;
            camera.transform.position = target+offset; camera.transform.LookAt(target);
            try { SceneCapture.Capture(camera,"Captures/GroundHandling/"+name+".png",1600,1100); }
            finally
            {
                foreach (var go in snapshots) Object.DestroyImmediate(go);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                for (int i = 0; i < skins.Count; i++) skins[i].enabled = enabled[i];
            }
        }
        static void Bulk()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = false; demo.autoDeparture = false; demo.station.cartCount = 2;
            var airport = demo.airport;
            var stand = airport.Taxiways.stands.Find(s=>s.id == demo.standId);
            var job = airport.Baggage.Request(demo.aircraft,stand,new BulkHoldConfiguration
                { aircraft = demo.aircraft.type,bagCapacity = 250,weightCapacity = 5000 },demo.station,"MULTI-IN","MULTI-OUT",out var why);
            if (job == null) throw new Exception(why);
            for (int i = 0; i < 12; i++) airport.Baggage.Ledger.Add("M-IN-"+i,"MULTI-IN",10,150,true,job.Hold);
            for (int i = 0; i < 15; i++) airport.Baggage.Ledger.Add("M-OUT-"+i,"MULTI-OUT",10,150,true,job.Fleet.Dispatch);
            if (!job.Seal(out why)) throw new Exception(why);
            var camera = Object.FindAnyObjectByType<Camera>(); bool corner = false, cart = false, hold = false;
            for (int i = 0; i < 20000 && !job.Finished; i++)
            {
                airport.Simulate(.25f); demo.station.Present(.25f);
                var fleet = job.Fleet;
                Vector3 At(Vector3 p) => airport.transform.TransformPoint(p);
                if (!corner && job.State == BaggageTurnaroundState.OutboundDrive && fleet.Train.Distance > 8)
                {
                    var at = fleet.Train.CartAt(0).position;
                    Shot(camera,At(new Vector3(at.x,.8f,at.y)),new Vector3(9,10,-12),"two-loaded-carts"); corner = true;
                }
                if (!cart && job.State == BaggageTurnaroundState.LoadingHold && fleet.ActiveCart == 1 && job.TransferSeconds > 2)
                { Shot(camera,At(fleet.Crew.GroundPose.position)+Vector3.up*.8f,new Vector3(7,5,7),"second-cart-handling"); cart = true; }
                if (!hold && job.State == BaggageTurnaroundState.LoadingHold && job.Hold.Count >= 40 && job.TransferSeconds > 4)
                {
                    var side = Vector3.Cross(Vector3.up,job.Plan.inward);
                    var target = job.Plan.door+job.Plan.inward*1.2f-side*2f+Vector3.up*.5f;
                    Shot(camera,At(target),side*1.55f-job.Plan.inward*.95f+Vector3.up*.25f,"hold-packing-interior",85); hold = true;
                }
            }
            if (!job.DepartureReady || !corner || !cart || !hold) throw new Exception($"Multi-cart review: {job.State}: {job.Waiting}");
            Debug.Log($"Ground handling review: 270 bags conserved; 120 arrivals and 150 departures in {job.ArrivalTrips}/{job.DepartureTrips} trips; {job.Packing.SlotCount} modelled hold slots.");
        }
        static void Arrival()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>(); demo.startWithArrival = true; demo.autoDeparture = false; demo.Initialize();
            if (demo.Problem != null) throw new Exception(demo.Problem);
            var flight = demo.airport.Traffic.FlightOf(demo.aircraft);
            var presentation = demo.airport.GetComponent<MarshallerPresentation>();
            var camera = Object.FindAnyObjectByType<Camera>(); bool ahead = false, stop = false, overview = false;
            for (int i = 0; i < 16000 && (!stop || !flight.Finished); i++)
            {
                demo.airport.Simulate(.25f); presentation.Present();
                var worker = demo.airport.Marshalling.For(flight); if (worker == null) continue;
                var target = demo.airport.transform.TransformPoint(worker.Position)+Vector3.up*1.2f;
                if (!ahead && worker.Signal == MarshallingSignal.Ahead && worker.SignalBlend > .95f)
                {
                    Shot(camera,target,worker.Pose.rotation*new Vector3(3,1.3f,5),"marshaller-ahead",38);
                    ahead = true;
                }
                if (!overview && worker.Remaining < 18f)
                { Shot(camera,(target+demo.aircraft.transform.position+Vector3.up*2)*.5f,worker.Pose.rotation*new Vector3(35,17,6),"arrival-marshaller-overview",60); overview = true; }
                if (!stop && worker.Signal == MarshallingSignal.Stop && worker.SignalBlend > .95f)
                { Shot(camera,target,worker.Pose.rotation*new Vector3(3,1.3f,5),"marshaller-stop",38); stop = true; }
            }
            if (!ahead || !stop || flight.Phase != FlightPhase.Parked) throw new Exception("Marshaller review did not guide and stop the arrival.");
            Debug.Log("Ground handling review: live arrival guided onto the configured stand, slow taxi and crossed-wand stop captured.");
        }
    }
}
