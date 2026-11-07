using System;
using AirCity.Characters;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Characters
{
    public static class RampCrewReview
    {
        public static void PortraitsFromCommandLine()
        {
            try
            {
                AssetDatabase.Refresh(); RampCrewSetup.CreateAssets(); Portraits();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void FromCommandLine()
        {
            try
            {
                RampCrewSetup.Install(); Portraits(); Bulk(); Containers();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Shot(Camera camera, Vector3 target, Vector3 offset, string name, float fov = 35)
        {
            foreach (var rig in Object.FindObjectsByType<RampCrewRig>(FindObjectsSortMode.None))
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                { skin.forceMatrixRecalculationPerRender = true; skin.updateWhenOffscreen = true; }
            camera.orthographic = false; camera.fieldOfView = fov;
            camera.transform.position = target+offset; camera.transform.LookAt(target);
            SceneCapture.Capture(camera,"Captures/RampCrew/"+name+".png",1440,1100);
        }
        static void Portraits()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var camera = new GameObject("Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.10f,.14f,.18f);
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.8f;
            light.transform.rotation = Quaternion.Euler(45,-25,0); RenderSettings.sun = light;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.40f,.44f,.5f);
            var rig = Object.Instantiate(RampCrewSetup.Prefab).GetComponent<RampCrewRig>();
            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                Debug.Log($"Crew mesh {skin.name}: {skin.sharedMesh.vertexCount} vertices, {skin.sharedMesh.triangles.Length/3} triangles");
            foreach (var t in rig.GetComponentsInChildren<Transform>())
                if (t.name == "Hips" || t.name == "UpperArm_L" || t.name == "Hand_R") Debug.Log($"Crew import {t.name}: {t.position} {t.rotation}");
            var p = RampCrewPose.Standing(Vector3.zero,Vector3.forward); p.hands = false;
            rig.Apply(p); Shot(camera,Vector3.up*.94f,new Vector3(2f,.5f,3.8f),"worker-standing");
            Shot(camera,Vector3.up*.94f,new Vector3(0,.15f,4.3f),"worker-front");
            Shot(camera,Vector3.up*.94f,new Vector3(-2f,.5f,-3.8f),"worker-back");
            Shot(camera,new Vector3(0,1.61f,0),new Vector3(.5f,.10f,1.3f),"worker-face");
            p.stride = .8f; p.phase = .18f; rig.Apply(p);
            Shot(camera,Vector3.up*.94f,new Vector3(2f,.5f,3.8f),"worker-walking");
            p.stride = 0; p.hipHeight = .35f; p.lean = 40; p.kneeling = true; p.hands = true;
            p.leftHand = new Vector3(-.20f,.30f,.48f); p.rightHand = new Vector3(.20f,.30f,.48f); rig.Apply(p);
            foreach (var r in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh(); r.BakeMesh(mesh);
                var min = new Vector3(float.MaxValue,float.MaxValue,float.MaxValue); var max = -min;
                foreach (var v in mesh.vertices) { var w = r.transform.TransformPoint(v); min = Vector3.Min(min,w); max = Vector3.Max(max,w); }
                Debug.Log($"Crew crouch {r.name} {min} / {max}; palms {rig.LeftPalm} / {rig.RightPalm}"); Object.DestroyImmediate(mesh);
            }
            Shot(camera,Vector3.up*.55f,new Vector3(2f,.7f,3.4f),"worker-kneeling");
        }
        static void Bulk()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = false; demo.autoDeparture = false; demo.Initialize();
            var camera = Object.FindAnyObjectByType<Camera>();
            bool driver = false, boarding = false, walkway = false, pickup = false, carry = false, place = false, hold = false, stocked = false, leaving = false;
            for (int i = 0; i < 16000 && !demo.Job.Finished; i++)
            {
                demo.airport.Simulate(.1f); demo.station.Present(.1f);
                var job = demo.Job; var crew = job.Fleet.Crew;
                Vector3 At(Vector3 p) => demo.airport.transform.TransformPoint(p);
                if (!driver)
                { Shot(camera,At(crew.GroundPose.position)+Vector3.up, new Vector3(3,1.3f,4),"tractor-driver"); driver = true; }
                if (!boarding && job.State == BaggageTurnaroundState.Docking && crew.HoldProgress > 9f)
                { Shot(camera,At(crew.HoldPose.position)+Vector3.up*.7f,-job.Plan.inward*5+Vector3.up*2+Vector3.forward*3,"hold-worker-boarding"); boarding = true; }
                if (!walkway && job.State == BaggageTurnaroundState.Docking && crew.HoldProgress > 16f)
                { Shot(camera,At(crew.HoldPose.position)+Vector3.up*.7f,-job.Plan.inward*5+Vector3.up*2+Vector3.forward*3,"loader-walkway"); walkway = true; }
                if (job.State == BaggageTurnaroundState.LoadingHold && job.TransferBatch != null)
                {
                    float t = job.TransferSeconds;
                    if (!pickup && t > .55f)
                    { Shot(camera,At(crew.GroundPose.position)+Vector3.up*.9f,crew.GroundPose.rotation*Vector3.right*3.5f+Vector3.up*1.2f,"cart-pickup"); pickup = true; }
                    if (!carry && t > 2.2f)
                    { Shot(camera,At(crew.GroundPose.position)+Vector3.up*.9f,crew.GroundPose.rotation*new Vector3(3f,1.2f,-2f),"cart-to-belt-carry"); carry = true; }
                    if (!place && t > crew.GroundOutbound-.25f)
                    { Shot(camera,At(crew.GroundPose.position)+Vector3.up*.9f,new Vector3(3.1f,1.4f,3.2f),"belt-placement"); place = true; }
                    if (!hold && t > job.BagTravelSeconds-1.2f)
                    { Shot(camera,At(job.Plan.door)+Vector3.up*.55f,-job.Plan.inward*3.2f+Vector3.up*.2f+Vector3.forward*.2f,"hold-stow",45); hold = true; }
                    if (!stocked && t > job.BagTravelSeconds+job.TransferInterval*4)
                    { Shot(camera,At(job.Plan.door)+Vector3.up*.55f,-job.Plan.inward*3.2f+Vector3.up*.2f+Vector3.forward*.2f,"hold-stow-loaded",45); stocked = true; }
                }
                if (!leaving && job.State == BaggageTurnaroundState.Withdrawing && !crew.HoldReady && !crew.HoldSeated)
                { Shot(camera,At(crew.HoldPose.position)+Vector3.up*.7f,-job.Plan.inward*5+Vector3.up*2+Vector3.forward*3,"hold-worker-leaving"); leaving = true; }
            }
            if (!demo.Job.DepartureReady) throw new Exception($"Crew bulk: {demo.Job.State}: {demo.Job.Waiting}");
            Debug.Log("Crew review: all 90 bags conserved; both workers seated, hatch closed and equipment home.");
        }
        static void Containers()
        {
            EditorSceneManager.OpenScene(ContainerTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<ContainerTurnaroundDemo>(); demo.autoDeparture = false; demo.Initialize();
            demo.station.Present(0);
            var pose = BaggageCrew.Workstation(demo.station.loaderType,demo.Job.Fleet.LoaderPose);
            Shot(Object.FindAnyObjectByType<Camera>(), demo.airport.transform.TransformPoint(pose.position)+Vector3.up,
                new Vector3(4,1.5f,4),"container-loader-operator");
            bool captured = false;
            for (int i = 0; i < 14000 && (!demo.Job.Finished || demo.Bulk != null && !demo.Bulk.Finished); i++)
            {
                demo.airport.Simulate(.25f); demo.Observe(); demo.station.Present(.25f);
                if (demo.bulkStation != null) demo.bulkStation.Present(.25f);
                if (!captured && demo.Bulk != null && demo.Bulk.State == BaggageTurnaroundState.LoadingHold &&
                    demo.Bulk.TransferSeconds > demo.Bulk.BagTravelSeconds-1.2f)
                {
                    var plan = demo.Bulk.Plan;
                    Shot(Object.FindAnyObjectByType<Camera>(),demo.airport.transform.TransformPoint(plan.door)+Vector3.up*.40f,
                        -plan.inward*3.2f+Vector3.up*.2f,"widebody-bulk-hold",45);
                    captured = true;
                }
            }
            if (!captured || !demo.Job.DepartureReady || !demo.Bulk.DepartureReady || demo.airport.Baggage.Ledger.Count != 380)
                throw new Exception($"Crew combined review: {demo.Job.State}/{demo.Bulk?.State}: {demo.Problem}");
            Debug.Log("Crew combined review: all 380 bags conserved; container and bulk equipment home, operators seated and both hatches closed.");
        }
    }
}
