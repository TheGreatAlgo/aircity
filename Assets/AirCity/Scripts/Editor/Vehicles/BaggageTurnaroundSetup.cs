using System;
using System.IO;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Dev;
using AirCity.Traffic;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Vehicles
{
    public static class BaggageTurnaroundSetup
    {
        public const string ScenePath = "Assets/AirCity/Scenes/BaggageTurnaround.unity";
        const string Captures = "Captures/BaggageTurnaround";

        [MenuItem("AirCity/Vehicles/Create Baggage Turnaround Review")]
        public static void CreateReview()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }

        public static void FromCommandLine()
        {
            try { Build(true); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static BaggageTurnaroundDemo Build(bool capture)
        {
            VehicleSetup.CreateMissing(); AirfieldSetup.CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.73f, 0.83f);
            RenderSettings.ambientEquatorColor = new Color(0.49f, 0.54f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.29f, 0.31f);
            var sun = new GameObject("Daylight").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f); RenderSettings.sun = sun;
            var root = new GameObject("Turnaround airport"); root.SetActive(false);
            var airport = root.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 5000f; airport.timeScale = 12f; airport.windFrom = 270f;
            var aircraftType = AssetDatabase.LoadAssetAtPath<AirCity.Aircraft.AircraftType>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.aircraft");
            airport.aircraftTypes.Add(aircraftType); root.SetActive(true);
            var runway = airport.AddRunway(new Vector3(-1500f, 0f, -200f), new Vector3(1500f, 0f, -200f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int w = net.AddNode(new Vector3(-1450f, 0f, -200f), runway), e = net.AddNode(new Vector3(1450f, 0f, -200f), runway);
            int sw = net.AddNode(new Vector3(-1450f, 0f, -400f)), se = net.AddNode(new Vector3(1450f, 0f, -400f));
            net.Connect(w, sw, 15f, PavementSurface.Asphalt); net.Connect(sw, se, 15f, PavementSurface.Asphalt);
            net.Connect(se, e, 15f, PavementSurface.Asphalt);
            var stand = net.AddStand(StandKind.Parking, new Vector3(0f, 0f, -400f), 0f, SizeCode.C, PavementSurface.Concrete);
            if (stand == null) throw new InvalidOperationException("Could not create turnaround stand.");
            var layout = net.Layout(stand); var park = layout.ParkPosition(aircraftType);
            airport.aprons.Add(ApronArea.Rect(park + new Vector2(40f, 0f), new Vector2(180f, 160f), 0f, PavementSurface.Concrete));
            var station = new GameObject("Baggage hall").AddComponent<BaggageStation>(); station.transform.SetParent(root.transform, false);
            station.tractorType = VehicleSetup.Type("BaggageTractor"); station.cartType = VehicleSetup.Type("BaggageCart"); station.loaderType = VehicleSetup.Type("BeltLoader");
            station.bagPrefabs = VehicleSetup.BagPrefabs();
            station.tractorPrefab = VehicleSetup.Prefab("BaggageTractor"); station.cartPrefab = VehicleSetup.Prefab("BaggageCart"); station.loaderPrefab = VehicleSetup.Prefab("BeltLoader");
            float x = park.x + aircraftType.bounds.max.x + 7f + 7.8f + 3.5f;
            int home = net.AddNode(new Vector3(x + 20f, 0f, park.y - 15f));
            int ne = net.AddNode(new Vector3(x + 20f, 0f, park.y + 32f));
            int nw = net.AddNode(new Vector3(x, 0f, park.y + 32f));
            int exit = net.AddNode(new Vector3(x, 0f, park.y - 40f));
            int arrivals = net.AddNode(new Vector3(x + 10f, 0f, park.y - 40f));
            int corner = net.AddNode(new Vector3(x + 20f, 0f, park.y - 40f));
            foreach (var pair in new[] { (home, ne), (ne, nw), (nw, exit), (exit, arrivals), (arrivals, corner), (corner, home) })
                net.Connect(pair.Item1, pair.Item2, 12f, PavementSurface.Concrete, false, RoadClass.Service);
            // Maintenance traffic needs an actual graph connection between the baggage circuit and the taxilane.
            var frontage = net.edges.Find(edge => !edge.IsRoad &&
                Mathf.Abs(net.At(edge.a).y + 400f) < 0.01f && Mathf.Abs(net.At(edge.b).y + 400f) < 0.01f);
            int junction = net.SplitEdge(frontage, new Vector3(x, 0f, -400f));
            net.Connect(exit, junction, 12f, PavementSurface.Concrete, false, RoadClass.Service);
            stand.baggageEntryNode = nw; stand.baggageExitNode = exit;
            station.dispatchNode = home; station.arrivalsNode = arrivals;
            station.arrivalsBearing = 90f; station.loaderParking = park + new Vector2(x + 40f, -15f); station.loaderParkingBearing = 0f;
            var depot = airport.AddDepot(park + new Vector2(65f, -63f), Vector2.up);
            depot.SetAssets(VehicleSetup.Type("SnowPlough"), VehicleSetup.Type("Mower"), VehicleSetup.Type("Depot"), null, null, null);
            depot.SetTugAssets(VehicleSetup.Type("TugSmall"), VehicleSetup.Type("TugLarge"), VehicleSetup.Prefab("TugSmall"), VehicleSetup.Prefab("TugLarge"));
            depot.autoDispatch = false; depot.SetFleet(0, 0); depot.SetTugs(1, 0);
            airport.BuildGround(); net.Rebuild(); runway.Rebuild();
            var planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.prefab");
            var aircraft = ((GameObject)PrefabUtility.InstantiatePrefab(planePrefab)).GetComponent<AirCity.Aircraft.Aircraft>();
            aircraft.name = "AC101 → AC102";
            aircraft.transform.SetPositionAndRotation(new Vector3(park.x, 0f, park.y), Quaternion.LookRotation(new Vector3(layout.nose.x, 0f, layout.nose.y)));
            var blue = BaggageDeliverySetup.Material("BaggageDeliveryBlue", new Color(0.08f, 0.27f, 0.40f));
            var white = BaggageDeliverySetup.Material("BaggageDeliveryWhite", new Color(0.88f, 0.88f, 0.79f));
            BaggageDeliverySetup.Box("Baggage hall endpoint", new Vector3(x + 50f, 3f, park.y - 24f), new Vector3(10f, 6f, 28f), blue);
            BaggageDeliverySetup.Text("DISPATCH", new Vector3(x + 24f, 0.025f, park.y - 16f), 1f, Color.white);
            BaggageDeliverySetup.Text("ARRIVALS", new Vector3(x + 12f, 0.025f, park.y - 45f), 1f, Color.white);
            var demo = new GameObject("Turnaround review").AddComponent<BaggageTurnaroundDemo>();
            demo.airport = airport; demo.station = station; demo.aircraft = aircraft; demo.standId = stand.id;
            var camera = new GameObject("Turnaround review camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.62f, 0.69f, 0.76f);
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 25000f; camera.fieldOfView = 45f;
            var orbit = camera.gameObject.AddComponent<DevOrbitCamera>(); orbit.pivot = new Vector3(park.x + 27f, 0f, park.y - 2f);
            orbit.distance = 140f; orbit.pitch = 58f; orbit.yaw = 215f;
            var rotation = Quaternion.Euler(orbit.pitch, orbit.yaw, 0f);
            camera.transform.SetPositionAndRotation(orbit.pivot - rotation * Vector3.forward * orbit.distance, rotation);
            var overviewPosition = camera.transform.position;
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            if (!capture) return demo;
            Directory.CreateDirectory(Captures);
            demo.Initialize();
            bool unloaded = false, loaded = false;
            void HandlingCapture(string name)
            {
                var q = demo.Job.Fleet.LoaderPose.position;
                var target = airport.transform.TransformPoint(new Vector3(q.x, 1f, q.y));
                camera.transform.position = target + new Vector3(16f, 12f, 18f);
                camera.transform.LookAt(target);
                SceneCapture.Capture(camera, Captures + "/" + name + ".png", 1800, 1100);
            }
            for (int i = 0; i < 6000; i++)
            {
                airport.Simulate(0.25f); demo.Observe(); station.Present(0.25f);
                if (demo.Problem != null) throw new InvalidOperationException(demo.Problem);
                if (!unloaded && demo.Job.State == BaggageTurnaroundState.UnloadingHold)
                { HandlingCapture("unloading"); unloaded = true; }
                if (!loaded && demo.Job.State == BaggageTurnaroundState.LoadingHold)
                { HandlingCapture("loading"); loaded = true; }
                if (demo.Job.DepartureReady && demo.Departure != null && demo.Departure.Phase == FlightPhase.Taxi) break;
            }
            if (!demo.Job.DepartureReady || !demo.ObservedDepartureHold || !unloaded || !loaded || demo.Departure?.Phase != FlightPhase.Taxi)
                throw new InvalidOperationException($"Turnaround incomplete: {demo.Job.State}, {demo.Job.Waiting}, departure {demo.Departure?.Phase}: {demo.Departure?.Status}; tug {airport.Maintenance.TowOf(aircraft)?.Tug?.Status} at {airport.Maintenance.TowOf(aircraft)?.Tug?.LeadPosition}");
            if (demo.Job.Fleet.Arrivals.Count != 30 || demo.Job.Hold.Count != 60 || airport.Baggage.Ledger.Count != 90)
                throw new InvalidOperationException("Baggage conservation failed in the turnaround review.");
            camera.transform.SetPositionAndRotation(overviewPosition, rotation);
            SceneCapture.Capture(camera, Captures + "/departure-clear.png", 1800, 1100);
            Debug.Log("Baggage turnaround: actual arrival parked; 30 bags / 450 kg at arrivals, 60 bags / 900 kg loaded; equipment clear, held departure pushed back and began taxiing.");
            return demo;
        }
    }
}
