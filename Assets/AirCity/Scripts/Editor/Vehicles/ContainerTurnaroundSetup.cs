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
    public static class ContainerTurnaroundSetup
    {
        public const string ScenePath = "Assets/AirCity/Scenes/ContainerTurnaround.unity";
        [MenuItem("AirCity/Vehicles/Create Container Turnaround Review")]
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
        public static ContainerTurnaroundDemo Build(bool capture)
        {
            VehicleSetup.CreateMissing(); AirfieldSetup.CreateMaterials();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.66f, .73f, .83f);
            RenderSettings.ambientEquatorColor = new Color(.49f, .54f, .60f);
            RenderSettings.ambientGroundColor = new Color(.27f, .29f, .31f);
            var sun = new GameObject("Daylight").AddComponent<Light>(); sun.type = LightType.Directional;
            sun.intensity = 2f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(45, -35, 0); RenderSettings.sun = sun;
            var root = new GameObject("Container airport"); root.SetActive(false);
            var airport = root.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 5000f; airport.windFrom = 270f; airport.timeScale = 12f;
            var type = AssetDatabase.LoadAssetAtPath<AirCity.Aircraft.AircraftType>("Assets/AirCity/Aircraft/Widebody/Widebody.aircraft");
            airport.aircraftTypes.Add(type); root.SetActive(true);
            var runway = airport.AddRunway(new Vector3(-1800, 0, -200), new Vector3(1800, 0, -200), PavementSurface.Concrete, 60f);
            var net = airport.Taxiways;
            int Node(float x, float z) => net.AddNode(new Vector3(x, 0, z));
            int west = net.AddNode(new Vector3(-1750, 0, -200), runway), east = net.AddNode(new Vector3(1750, 0, -200), runway);
            int sw = Node(-1750, -500), se = Node(1750, -500);
            net.Connect(west, sw, 25f, PavementSurface.Concrete); net.Connect(sw, se, 25f, PavementSurface.Concrete); net.Connect(se, east, 25f, PavementSurface.Concrete);
            var stand = net.AddStand(StandKind.Parking, new Vector3(0, 0, -500), 0f, SizeCode.E, PavementSurface.Concrete);
            if (stand == null) throw new InvalidOperationException("Could not create widebody stand.");
            var layout = net.Layout(stand); var park = layout.ParkPosition(type);
            airport.aprons.Add(ApronArea.Rect(park + new Vector2(70, 0), new Vector2(280, 260), 0, PavementSurface.Concrete));
            var station = new GameObject("Container hall").AddComponent<ContainerStation>(); station.transform.SetParent(root.transform, false);
            station.tractorType = VehicleSetup.Type("BaggageTractor"); station.dollyType = VehicleSetup.Type("ContainerDolly");
            station.loaderType = VehicleSetup.Type("ContainerLoader"); station.unitType = VehicleSetup.Type("ContainerLD3");
            station.tractorPrefab = VehicleSetup.Prefab("BaggageTractor"); station.dollyPrefab = VehicleSetup.Prefab("ContainerDolly");
            station.loaderPrefab = VehicleSetup.Prefab("ContainerLoader"); station.unitPrefab = VehicleSetup.Prefab("ContainerLD3");
            Characters.RampCrewSetup.Assign(station);
            float x = park.x + type.bounds.max.x + 7f + station.loaderType.turnRadius + station.loaderType.bounds.max.z - station.loaderType.container.mainTransfer.z;
            int home = Node(x + 25, park.y - 35), ne = Node(x + 25, park.y + 70), entry = Node(x, park.y + 70);
            int exit = Node(x, park.y - 65), arrivals = Node(x + 12, park.y - 65), corner = Node(x + 25, park.y - 65);
            foreach (var pair in new[] { (home, ne), (ne, entry), (entry, exit), (exit, arrivals), (arrivals, corner), (corner, home) })
                net.Connect(pair.Item1, pair.Item2, 14f, PavementSurface.Concrete, false, RoadClass.Service);
            var frontage = net.edges.Find(e => !e.IsRoad && Mathf.Abs(net.At(e.a).y + 500) < .01f && Mathf.Abs(net.At(e.b).y + 500) < .01f);
            int junction = net.SplitEdge(frontage, new Vector3(x, 0, -500));
            net.Connect(exit, junction, 14f, PavementSurface.Concrete, false, RoadClass.Service);
            stand.baggageEntryNode = ne; stand.baggageExitNode = exit;
            station.dispatchNode = home; station.arrivalsNode = arrivals; station.arrivalsBearing = 90;
            station.loaderParking = new Vector2(x + 55, park.y + 30);
            station.automaticHold.aircraft=type;
            station.standIds.Add(stand.id);
            var bulk = new GameObject("Bulk baggage hall").AddComponent<BaggageStation>(); bulk.transform.SetParent(root.transform, false);
            bulk.tractorType = VehicleSetup.Type("BaggageTractor"); bulk.cartType = VehicleSetup.Type("BaggageCart"); bulk.loaderType = VehicleSetup.Type("BeltLoader");
            bulk.bagPrefabs = VehicleSetup.BagPrefabs();
            Characters.RampCrewSetup.Assign(bulk);
            bulk.tractorPrefab = VehicleSetup.Prefab("BaggageTractor"); bulk.cartPrefab = VehicleSetup.Prefab("BaggageCart"); bulk.loaderPrefab = VehicleSetup.Prefab("BeltLoader");
            int bulkHome = Node(x + 50, park.y + 5), bulkNorth = Node(x + 50, park.y + 70);
            int bulkArrival = Node(x + 37, park.y - 65), bulkCorner = Node(x + 50, park.y - 65);
            foreach (var pair in new[] { (bulkHome, bulkNorth), (bulkNorth, ne), (corner, bulkArrival), (bulkArrival, bulkCorner), (bulkCorner, bulkHome) })
                net.Connect(pair.Item1, pair.Item2, 14f, PavementSurface.Concrete, false, RoadClass.Service);
            bulk.dispatchNode = bulkHome; bulk.arrivalsNode = bulkArrival; bulk.arrivalsBearing = 90;
            bulk.loaderParking = new Vector2(x + 75, park.y + 5);
            bulk.automaticHold.aircraft=type; bulk.automaticHold.cargoPoint="Cargo_Bulk";
            bulk.standIds.Add(stand.id);
            var depot = airport.AddDepot(park + new Vector2(90, -100), Vector2.up);
            VehicleSetup.Assign(depot); depot.autoDispatch = false; depot.SetFleet(0, 0); depot.SetTugs(0, 1);
            airport.BuildGround(); net.Rebuild(); runway.Rebuild();
            var plane = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AirCity/Aircraft/Widebody/Widebody.prefab"))).GetComponent<AirCity.Aircraft.Aircraft>();
            plane.name = "Widebody container turnaround";
            plane.transform.SetPositionAndRotation(new Vector3(park.x, 0, park.y), Quaternion.LookRotation(new Vector3(layout.nose.x, 0, layout.nose.y)));
            var demo = new GameObject("Container review").AddComponent<ContainerTurnaroundDemo>();
            demo.airport = airport; demo.station = station; demo.bulkStation = bulk; demo.aircraft = plane; demo.standId = stand.id;
            var camera = new GameObject("Container review camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.nearClipPlane = .1f; camera.farClipPlane = 25000; camera.fieldOfView = 45;
            var orbit = camera.gameObject.AddComponent<DevOrbitCamera>(); orbit.pivot = new Vector3(park.x + 40, 0, park.y + 5);
            orbit.distance = 230; orbit.pitch = 55; orbit.yaw = 215;
            var rotation = Quaternion.Euler(orbit.pitch, orbit.yaw, 0);
            camera.transform.SetPositionAndRotation(orbit.pivot - rotation * Vector3.forward * orbit.distance, rotation);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            if (!capture) return demo;
            Directory.CreateDirectory("Captures/ContainerTurnaround"); demo.Initialize();
            if (demo.Problem != null) throw new InvalidOperationException(demo.Problem);
            airport.Simulate(1f); demo.Observe();
            if (!demo.Job.Acquired) throw new InvalidOperationException(demo.Job.Waiting ?? "Container service did not acquire equipment.");
            bool mainCaptured = false, frontCaptured = false;
            for (int i = 0; i < 20000; i++)
            {
                airport.Simulate(.25f); demo.Observe(); station.Present(.25f); bulk.Present(.25f);
                if (demo.Problem != null) throw new InvalidOperationException(demo.Problem);
                void Capture(string name)
                {
                    var p = demo.Job.Fleet.LoaderPose.position; var target = new Vector3(p.x - 2, 1, p.y + 3);
                    camera.transform.position = target + new Vector3(18, 14, 19); camera.transform.LookAt(target);
                    SceneCapture.Capture(camera, "Captures/ContainerTurnaround/" + name + ".png", 1800, 1100);
                }
                if (!mainCaptured && demo.Job.State == ContainerTurnaroundState.RaiseMain && demo.Job.Fleet.MainHeight > 1.2f)
                { Capture("main-lift"); mainCaptured = true; }
                if (!frontCaptured && demo.Job.State == ContainerTurnaroundState.MainToFront && demo.Job.TransferProgress > .3f)
                { Capture("platform-transfer"); frontCaptured = true; }
                if (demo.Job.DepartureReady && demo.Bulk.DepartureReady && demo.Departure?.Phase == FlightPhase.Taxi) break;
            }
            if (!demo.Job.DepartureReady || !demo.Bulk.DepartureReady || demo.Departure?.Phase != FlightPhase.Taxi)
                throw new InvalidOperationException($"Container: {demo.Job.State}: {demo.Job.Waiting}; tractor {demo.Job.Fleet.Train.Tractor.position}, dolly {demo.Job.Fleet.Train.Cart.position}, loader {demo.Job.Fleet.LoaderPose.position}; " +
                    $"bulk: {demo.Bulk.State}: {demo.Bulk.Waiting}; bulk tractor {demo.Bulk.Fleet.Train.Tractor.position}, cart {demo.Bulk.Fleet.Train.Cart.position}, loader {demo.Bulk.Fleet.LoaderPose.position}; departure: {demo.Departure?.Status}");
            if (airport.Baggage.Ledger.Count != 380 || Mathf.Abs(airport.Baggage.Ledger.Weight - 5700) > .01f)
                throw new InvalidOperationException("Container/bulk baggage conservation failed.");
            Debug.Log("Container review passed: two incoming / three departing LD3s, 20 incoming / 30 departing bulk bags; all 380 bags / 5700 kg conserved; equipment returned and departure taxiing.");
            return demo;
        }
    }
}
