using System;
using System.IO;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>Standalone daylight review of the hangar shell, door poses and a resident light aircraft.</summary>
    public static class SmallHangarReview
    {
        public const string ScenePath = "Assets/AirCity/Scenes/SmallHangars.unity";
        const string Output = "Captures/SmallHangars/";

        [MenuItem("AirCity/Airfield/Create Small Hangar Review")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Output); Build(true);
                Debug.Log("SMALL HANGARS: captured resident/open, closed, side, airfield-scale and live taxi-in views; the resident completed a graph-routed round trip and all generated LODs stay inside the construction footprint.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        static void Build(bool capture)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.67f, .73f, .80f);
            RenderSettings.ambientEquatorColor = new Color(.45f, .48f, .50f);
            RenderSettings.ambientGroundColor = new Color(.24f, .26f, .25f);
            var light = new GameObject("Daylight").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.8f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(42, -38, 0); RenderSettings.sun = light;
            var root = new GameObject("Small hangar airfield"); root.SetActive(false);
            var airport = root.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport); airport.groundSize = 420;
            var type = AssetDatabase.LoadAssetAtPath<AircraftType>("Assets/AirCity/Aircraft/Cessna172/Cessna172.aircraft");
            if (type == null) throw new InvalidOperationException("The Starling A4 aircraft asset is missing.");
            airport.aircraftTypes.Add(type);
            airport.aprons.Add(ApronArea.Rect(new Vector2(0, 6), new Vector2(78, 12), 0, PavementSurface.Concrete));
            root.SetActive(true);
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(-75, 0, 15)), b = net.AddNode(new Vector3(75, 0, 15));
            net.Connect(a, b, 7.5f, PavementSurface.Asphalt);
            var runway = airport.AddRunway(new Vector3(-155, 0, 65), new Vector3(155, 0, 65), PavementSurface.Grass, 18);
            int c = net.AddNode(new Vector3(-75, 0, 65), runway);
            net.Connect(a, c, 7.5f, PavementSurface.Grass);
            var hangar = airport.AddHangar(Vector2.zero, 0);
            if (hangar == null) throw new InvalidOperationException("Could not place the review hangar: " + airport.HangarProblem(Vector2.zero, 0));
            hangar.name = "Hangar 01";
            if (!hangar.TryLease(type, "Valley Flying Club", "N100AC", out string problem)) throw new InvalidOperationException(problem);
            hangar.SetDoorsOpen(true);
            foreach (float x in new[] { -25f, 25f })
            {
                var other = airport.AddHangar(new Vector2(x, 0), 0);
                if (other == null) throw new InvalidOperationException("Could not place the review row: " + airport.HangarProblem(new Vector2(x, 0), 0));
                other.name = x < 0 ? "Hangar 02" : "Hangar 03"; other.SetDoorsOpen(x > 0);
            }
            if(net.AddStand(StandKind.Parking,new Vector3(52,0,15),0,SizeCode.A,PavementSurface.Concrete)==null)
                throw new InvalidOperationException("Could not create the resident's external parking stand.");
            airport.BuildGround(); net.Rebuild();
            var camera = new GameObject("Review camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.66f, .73f, .79f);
            camera.nearClipPlane = .05f; camera.farClipPlane = 1500; camera.fieldOfView = 42;
            if (capture)
            {
                ValidateBounds(airport);
                Aim(camera, new Vector3(25, 13, 31), new Vector3(0, 2, -5));
                SceneCapture.Capture(camera, Output + "open-resident.png", 1800, 1125);
                hangar.SetDoorsOpen(false); ValidateBounds(airport);
                SceneCapture.Capture(camera, Output + "closed.png", 1800, 1125);
                hangar.SetDoorsOpen(true);
                Aim(camera, new Vector3(25, 11, -25), new Vector3(0, 2, -8));
                SceneCapture.Capture(camera, Output + "side.png", 1800, 1125);
                Aim(camera, new Vector3(90, 105, 120), new Vector3(0, 0, 20));
                SceneCapture.Capture(camera, Output + "airfield.png", 1800, 1125);
                CaptureMotion(camera,airport,hangar);
            }
            var orbit = camera.gameObject.AddComponent<DevOrbitCamera>();
            orbit.pivot = new Vector3(0, 0, 2); orbit.distance = 105; orbit.pitch = 45; orbit.yaw = 210;
            camera.transform.rotation = Quaternion.Euler(orbit.pitch, orbit.yaw, 0);
            camera.transform.position = orbit.pivot - camera.transform.forward * orbit.distance;
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
        }

        static void CaptureMotion(Camera camera,Airport airport,SmallHangar hangar)
        {
            if(hangar.ResidentAircraft==null)throw new InvalidOperationException("The resident prefab was not instantiated.");
            if(!hangar.TaxiToStand(out var why))throw new InvalidOperationException("Resident cannot taxi out: "+why);
            float elapsed=0f;
            while(hangar.ResidentTravelling && elapsed<900f){airport.Traffic.Simulate(.25f);elapsed+=.25f;}
            if(hangar.ResidentTravelling)throw new InvalidOperationException("Resident taxi-out did not finish: "+airport.Traffic.FlightOf(hangar.ResidentAircraft)?.Status);
            Aim(camera,new Vector3(80,32,63),new Vector3(45,0,18));
            SceneCapture.Capture(camera,Output+"taxi-stand.png",1800,1125);
            if(!hangar.TaxiIntoHangar(out why))throw new InvalidOperationException("Resident cannot taxi back in: "+why);
            bool approach=false,doorway=false;
            elapsed=0f;
            Aim(camera,new Vector3(25,13,31),new Vector3(0,2,-3));
            while(hangar.ResidentTravelling && elapsed<900f)
            {
                airport.Traffic.Simulate(.25f);elapsed+=.25f;
                var local=hangar.transform.InverseTransformPoint(hangar.ResidentAircraft.transform.position);
                if(!approach && local.z<9f && local.z>3f)
                {SceneCapture.Capture(camera,Output+"taxi-approach.png",1800,1125);approach=true;}
                if(!doorway && local.z<1f && local.z>-.5f)
                {SceneCapture.Capture(camera,Output+"taxi-doorway.png",1800,1125);doorway=true;}
            }
            if(hangar.ResidentTravelling || !hangar.ResidentInBay || !approach || !doorway)
                throw new InvalidOperationException("Resident taxi-in failed to reach its bay or capture the doorway: "+airport.Traffic.FlightOf(hangar.ResidentAircraft)?.Status);
            SceneCapture.Capture(camera,Output+"taxi-stored.png",1800,1125);
        }

        static void ValidateBounds(Airport airport)
        {
            foreach (var hangar in airport.Hangars)
                foreach (var filter in hangar.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.name.StartsWith("Hangar LOD", StringComparison.Ordinal) && !filter.name.StartsWith("Hangar shadow LOD", StringComparison.Ordinal)) continue;
                    var bounds = filter.sharedMesh.bounds;
                    float half = SmallHangar.Width * .5f + SmallHangar.RoofOverhang + .002f;
                    if (bounds.min.x < -half || bounds.max.x > half ||
                        bounds.min.z < -SmallHangar.Depth - SmallHangar.RoofOverhang - .002f || bounds.max.z > SmallHangar.RoofOverhang + .002f)
                        throw new InvalidOperationException(filter.name + " exceeds the hangar's construction footprint: " + bounds);
                }
        }

        static void Aim(Camera camera, Vector3 position, Vector3 target)
        { camera.transform.position = position; camera.transform.LookAt(target); }
    }
}
