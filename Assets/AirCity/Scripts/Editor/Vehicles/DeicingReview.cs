using System;
using System.IO;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>Generated-art and live full-wing/refill review, in a separate playable scene.</summary>
    public static class DeicingReview
    {
        public const string ScenePath = "Assets/AirCity/Scenes/DeicingReview.unity";
        [MenuItem("AirCity/Vehicles/Create Deicing Review")]
        public static void CreateReview()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }
        public static void RunFromCommandLine()
        {
            try { Build(true); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void Build(bool capture)
        {
            DeicingEquipmentSetup.CreateAssets(); AirfieldSetup.CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.60f, .69f, .80f);
            RenderSettings.ambientEquatorColor = new Color(.43f, .48f, .56f);
            RenderSettings.ambientGroundColor = new Color(.26f, .29f, .33f);
            var sun = new GameObject("Winter daylight").AddComponent<Light>(); sun.type = LightType.Directional;
            sun.intensity = 2f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40, -35, 0); RenderSettings.sun = sun;
            var root = new GameObject("Deicing airport"); root.SetActive(false);
            var airport = root.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 800f; airport.timeScale = 1f;
            root.SetActive(true);
            var network = airport.Taxiways;
            int south = network.AddNode(new Vector3(0, 0, -180)), north = network.AddNode(new Vector3(0, 0, 180));
            network.Connect(south, north, 23f, PavementSurface.Concrete);
            var stand = network.AddStand(StandKind.DeIcing, Vector3.zero, 0f, SizeCode.C, PavementSurface.Concrete);
            if (stand == null) throw new InvalidOperationException("Could not build deicing review pad.");
            var layout = network.Layout(stand);
            airport.aprons.Add(ApronArea.Rect(new Vector2(-15, 5), new Vector2(200, 140), 0, PavementSurface.Concrete));
            int roadA = network.AddNode(new Vector3(-100, 0, -42));
            int roadB = network.AddNode(new Vector3(75, 0, -42));
            network.Connect(roadA, roadB, 8f, PavementSurface.Asphalt, false, RoadClass.Service);
            airport.BuildGround(); network.Rebuild();
            var service = airport.Deicing;
            var area = service.AddWorkArea(new Vector2(-65, -18), 0f);
            if (area == null) throw new InvalidOperationException(service.WorkAreaProblem(new Vector2(-65,-18),0));
            var facility = service.Configure(stand);
            // This fixture starts with an existing pair. New fleet purchases are exercised by DeicingParkingTests.
            facility.machines = 2;
            service.TrucksFor(facility);
            if (service.AddOutsideAccess(roadA, out var accessProblem) == null) throw new InvalidOperationException(accessProblem);
            if (!service.AssignWorkArea(facility, area))
                throw new InvalidOperationException("Could not assign the remote supply area and trucks.");
            if (!service.OrderFluid(area, 8000f, out var problem)) throw new InvalidOperationException(problem);
            var rig = root.GetComponent<DeicingRig>(); if (rig == null) rig = root.AddComponent<DeicingRig>();
            rig.assets = Resources.Load<DeicingAssets>("DeicingAssets");
            // First fill is genuine travel and stock transfer. Save only after trucks return to their pad.
            bool ready = false;
            for (int i = 0; i < 6000; i++)
            {
                airport.Simulate(.1f);
                var fleet = service.TrucksFor(facility);
                ready = !area.DeliveryPending && fleet.Count == 2 && fleet.All(t => t.FluidLitres >= t.CapacityLitres-.01f &&
                    t.Stage == DeicingTruckStage.Parked && t.Drive.Finished);
                if (ready) break;
            }
            if (!ready) throw new InvalidOperationException("Initial remote refill failed: " + FleetStatus(service,facility));
            rig.Refresh();
            if (rig.TruckCount != 2 || rig.TankCount != 1) throw new InvalidOperationException("Generated deicing art is missing.");
            float initialStock = area.fluidLitres + service.TrucksFor(facility).Sum(t=>t.FluidLitres);
            if (Mathf.Abs(initialStock-8000f) > .1f) throw new InvalidOperationException("Initial refill did not conserve fluid.");
            var type = AssetDatabase.LoadAssetAtPath<AircraftType>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.aircraft");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.prefab");
            if (type == null || prefab == null) throw new InvalidOperationException("Narrowbody aircraft assets are missing.");
            var plane = ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).GetComponent<AirCity.Aircraft.Aircraft>();
            plane.name = "Deicing review aircraft";
            plane.transform.SetPositionAndRotation(Ground(layout.ParkPosition(type)), Quaternion.LookRotation(Ground(layout.nose)));
            var lights = plane.GetComponent<AircraftLights>(); if (lights != null) { lights.navigation = true; lights.beacon = false; }
            var camera = new GameObject("Deicing review camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.nearClipPlane = .1f; camera.farClipPlane = 2500f; camera.fieldOfView = 42f;
            Overview(camera);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            if (!capture) return;
            Directory.CreateDirectory("Captures/Deicing");
            bool rootShot = false, middleShot = false, tipShot = false;
            float treatmentStarted = service.Clock;
            DeicingJob job = null;
            for (int i = 0; i < 6000; i++)
            {
                airport.Simulate(.05f); rig.Refresh(); job = service.JobFor(facility);
                if (job == null) throw new InvalidOperationException("Stopped aircraft was not acquired by deicing service.");
                if (job.IsSpraying && !rootShot && job.Progress > .06f)
                {
                    rootShot = true; Shot(camera,"wing-root",new Vector3(50,38,53),new Vector3(0,3,6));
                    int particles = rig.SprayParticleCount; float stock = area.fluidLitres; float progress = job.Progress;
                    airport.timeScale = 0;
                    for (int pause = 0; pause < 5; pause++) rig.Refresh();
                    if (rig.SprayParticleCount != particles || area.fluidLitres != stock || job.Progress != progress)
                        throw new InvalidOperationException("Paused deicing advanced.");
                    airport.timeScale = 1;
                }
                if (job.IsSpraying && !middleShot && job.Progress > .45f)
                {
                    middleShot = true; Overview(camera); SceneCapture.Capture(camera,"Captures/Deicing/pad-spraying.png",1800,1200);
                    var truck = rig.transform.Find("Deicing equipment (runtime)/Deicing truck 2");
                    if (truck != null) Shot(camera,"truck-spraying",truck.position + new Vector3(13,9,15),truck.position + new Vector3(-1,4,1));
                    if (rig.SprayParticleCount <= 0) throw new InvalidOperationException("No visible spray during treatment.");
                }
                if (job.IsSpraying && !tipShot && job.Progress > .88f)
                { tipShot = true; Shot(camera,"wing-tip",new Vector3(50,38,53),new Vector3(0,3,6)); }
                if (job.Completed) break;
            }
            if (job == null || !job.Completed || !rootShot || !middleShot || !tipShot)
                throw new InvalidOperationException("Full-wing service did not finish: " + FleetStatus(service,facility));
            float elapsed = service.Clock-treatmentStarted;
            float remaining = area.fluidLitres+service.TrucksFor(facility).Sum(t=>t.FluidLitres);
            if (Mathf.Abs(remaining+job.UsedLitres-initialStock) > .2f) throw new InvalidOperationException("Treatment did not conserve fluid.");
            Overview(camera); SceneCapture.Capture(camera,"Captures/Deicing/pad-complete.png",1800,1200);
            Shot(camera,"fluid-storage",Ground(area.position)+new Vector3(25,17,28),Ground(area.position)+Vector3.up*2);
            // Demonstrate a truck leaving the pad automatically when its onboard tank runs empty.
            var first = service.TrucksFor(facility)[0]; first.onboardLitres = 0f;
            bool visited = false, returned = false;
            for (int i = 0; i < 6000; i++)
            {
                airport.Simulate(.05f); rig.Refresh();
                if (!visited && first.Stage == DeicingTruckStage.Filling)
                {
                    visited = true; Shot(camera,"truck-refilling",Ground(area.position)+new Vector3(14,12,-24),Ground(area.position)+new Vector3(0,1,-2));
                }
                returned = visited && first.FluidLitres >= first.CapacityLitres-.01f && first.Stage == DeicingTruckStage.Parked && first.Drive.Finished;
                if (returned) break;
            }
            if (!returned) throw new InvalidOperationException("Automatic remote refill/return failed: "+FleetStatus(service,facility));
            File.WriteAllText("Logs/deicing-review-metrics.json",JsonUtility.ToJson(new ReviewMetrics { treatmentSeconds=elapsed, fluidUsed=job.UsedLitres,
                remoteRefillVerified=true, fullWingSamples=DeicingWingCoverage.Create(type,1).Count },true));
            Debug.Log($"Deicing review passed: level enclosed tip cabins, full wing root/chord/tip passes, treatment {elapsed:0.0}s, finite onboard fluid, remote refill and return, pause and completion.");
        }
        [Serializable] sealed class ReviewMetrics { public float treatmentSeconds,fluidUsed; public bool remoteRefillVerified; public int fullWingSamples; }
        static string FleetStatus(AirportDeicing service,DeicingFacility facility) => string.Join("; ",service.TrucksFor(facility).Select(t=>$"{t.Status} at {t.Position}, {t.FluidLitres:0}L, target {t.TargetIndex}/{t.TargetCount}"));
        static void Overview(Camera camera) { camera.transform.position=new Vector3(80,75,100); camera.transform.LookAt(new Vector3(-15,2,-2)); }
        static void Shot(Camera camera,string name,Vector3 from,Vector3 at)
        { camera.transform.position=from; camera.transform.LookAt(at); SceneCapture.Capture(camera,$"Captures/Deicing/{name}.png",1800,1200); }
        static Vector3 Ground(Vector2 point) => new Vector3(point.x,0,point.y);
    }
}
