using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Characters;
using AirCity.EditorTools.Dev;
using AirCity.Traffic;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug=UnityEngine.Debug;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>Actual Sandbox depot flow, warm simulation timings and an unloading animation.</summary>
    public static class BaggageDepotReview
    {
        public static void Run()
        {
            try
            {
                var clock=Stopwatch.StartNew();
                EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
                Debug.Log($"DEPOT STARTUP scene {clock.Elapsed.TotalSeconds:F3}s");
                var airport=Object.FindAnyObjectByType<Airport>(); var depot=Object.FindAnyObjectByType<BaggageDepot>();
                if (depot==null || airport.Baggage.ContainerStations.Length!=0 || depot.Teams.Length!=1)
                    throw new Exception("Expected one baggage depot and one example team.");
                var station=depot.Teams[0]; depot.Rebuild();
                var plane=airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>().Single(a=>a.name=="Narrowbody baggage turnaround");
                for (int i=0;i<4;i++) { clock.Restart(); airport.Simulate(.05f); Debug.Log($"DEPOT STARTUP tick {i} {clock.Elapsed.TotalSeconds:F3}s"); }
                var job=airport.Baggage.JobOf(plane);
                if (job==null) throw new Exception(station.Problem);
                if (!job.Acquired) throw new Exception("Depot route planning: "+job.Waiting);
                var departure=airport.Traffic.Depart(plane,out var reason); if (departure==null) throw new Exception(reason);
                var camera=Object.FindAnyObjectByType<Camera>();
                bool unloaded=false,collected=false,loaded=false; int frame=0;
                var sample=Stopwatch.StartNew(); double simulation=0; int samples=0;
                for (int tick=0;tick<36000 && departure.Phase!=FlightPhase.Taxi;tick++)
                {
                    sample.Restart(); airport.Simulate(.25f); simulation+=sample.Elapsed.TotalMilliseconds; samples++;
                    if (tick%1200==0) Debug.Log($"DEPOT progress {tick*.25f:F0}s: {job.State}; {job.Waiting}");
                    if (job.DepotTransfer && job.State==BaggageTurnaroundState.UnloadingArrivals && !unloaded)
                    {
                        var stationary=job.Fleet.Train.Tractor.position;
                        for (int i=0;i<160 && job.DepotTransfer;i++)
                        {
                            airport.Simulate(.05f); station.Present(.05f); depot.Present();
                            if (Vector2.Distance(stationary,job.Fleet.Train.Tractor.position)>.001f) throw new Exception("Tractor moved while unloading at its depot bay.");
                            Aim(camera,airport,depot.Point(5,-4),new Vector3(-23,20,-22));
                            Shot(camera,$"Captures/BaggageDepot/Frames/unload-{frame++:000}.png",1000,700);
                        }
                        Aim(camera,airport,depot.Point(10,5),new Vector3(-45,38,-38)); Shot(camera,"Captures/BaggageDepot/depot-unloading.png");
                        unloaded=true;
                    }
                    if (job.DepotTransfer && job.State==BaggageTurnaroundState.CollectingDepartures && !collected)
                    {
                        station.Present(.25f); depot.Present();
                        Aim(camera,airport,depot.Point(10,5),new Vector3(-45,38,-38)); Shot(camera,"Captures/BaggageDepot/depot-collection.png"); collected=true;
                    }
                    if (job.State==BaggageTurnaroundState.LoadingHold && job.TransferSeconds>5 && !loaded)
                    {
                        station.Present(.25f); depot.Present();
                        var target=job.Plan.door-job.Plan.inward*5;
                        var offset=-job.Plan.inward*20+Vector3.up*11+plane.transform.forward*8;
                        var at=airport.transform.TransformPoint(target);
                        camera.orthographic=false; camera.fieldOfView=48; camera.transform.position=at+airport.transform.TransformDirection(offset); camera.transform.LookAt(at);
                        Shot(camera,"Captures/BaggageDepot/ramp-team.png"); loaded=true;
                    }
                }
                if (!job.DepartureReady || departure.Phase!=FlightPhase.Taxi || job.Fleet.Arrivals.Count!=station.incomingBags ||
                    job.Hold.Count!=station.outgoingBags || depot.Working!=0 || airport.Baggage.Ledger.Count!=station.incomingBags+station.outgoingBags)
                    throw new Exception($"Depot turnaround: {job.State}: {job.Waiting}; {departure.Status}");
                if (!unloaded || !collected || !loaded || frame<20) throw new Exception("Missing depot review phases.");
                station.Present(.25f); depot.Present();
                Aim(camera,airport,depot.Point(-15,0),new Vector3(-100,120,-100)); Shot(camera,"Captures/BaggageDepot/sandbox-overview.png");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/baggage-depot-native.json",$"{{\"passed\":true,\"incoming\":{job.IncomingCount},\"outgoing\":{job.OutgoingCount},\"assignedWorkersAfterReturn\":{depot.Working},\"frames\":{frame},\"meanQuarterSecondSimulationMs\":{(simulation/samples).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)}}}");
                Debug.Log($"DEPOT REVIEW passed: {job.IncomingCount} arrivals / {job.OutgoingCount} departures, all bags conserved, depot team returned, aircraft taxiing; mean .25s simulation {simulation/samples:F3} ms.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Aim(Camera camera,Airport airport,Vector2 at,Vector3 offset)
        {
            camera.orthographic=false; camera.fieldOfView=48; camera.nearClipPlane=.05f; camera.farClipPlane=2000;
            var target=airport.transform.TransformPoint(new Vector3(at.x,1,at.y));
            camera.transform.position=target+airport.transform.TransformDirection(offset); camera.transform.LookAt(target);
        }
        static void Shot(Camera camera,string path,int width=1800,int height=1200)
        {
            var skins=new List<SkinnedMeshRenderer>(); var snapshots=new List<GameObject>(); var meshes=new List<Mesh>();
            foreach (var rig in Object.FindAnyObjectByType<Airport>().GetComponentsInChildren<RampCrewRig>())
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!skin.enabled) continue;
                    if (skin.name.EndsWith("LOD0"))
                    {
                        var mesh=new Mesh(); skin.BakeMesh(mesh); meshes.Add(mesh);
                        var copy=new GameObject("Crew capture"); copy.transform.SetParent(skin.transform,false);
                        copy.AddComponent<MeshFilter>().sharedMesh=mesh; copy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials; snapshots.Add(copy);
                    }
                    skins.Add(skin); skin.enabled=false;
                }
            try { SceneCapture.Capture(camera,path,width,height); }
            finally
            {
                foreach (var copy in snapshots) Object.DestroyImmediate(copy);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                foreach (var skin in skins) if (skin!=null) skin.enabled=true;
            }
        }
    }
}
