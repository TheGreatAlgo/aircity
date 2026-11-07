using System;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Dev;
using AirCity.Traffic;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Vehicles
{
    public static class ContainerSandboxSetup
    {
        [MenuItem("AirCity/Vehicles/Enable Sandbox Containers")]
        public static void Enable()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(AircraftSetup.SandboxScene, OpenSceneMode.Single);
            var airport = UnityEngine.Object.FindFirstObjectByType<Airport>();
            if (airport == null) throw new InvalidOperationException("The sandbox needs an airport.");
            Install(airport); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }

        public static ContainerStation Install(Airport airport)
        {
            VehicleSetup.CreateMissing();
            var existing = airport.GetComponentsInChildren<ContainerStation>().FirstOrDefault(s => s.name == "Sandbox container hall");
            if (existing != null) { WireCamera(existing); return existing; }
            var type = AssetDatabase.LoadAssetAtPath<AirCity.Aircraft.AircraftType>("Assets/AirCity/Aircraft/Widebody/Widebody.aircraft");
            var net = airport.Taxiways;
            int Node(float x, float z) => net.AddNode(new Vector3(x, 0f, z));
            var join = net.nodes.Where(n => n.runway == null && net.edges.Any(e => !e.IsRoad && e.width >= 25f && (e.a == n.id || e.b == n.id)))
                .OrderBy(n => Vector2.SqrMagnitude(net.At(n.id) - new Vector2(2050f, 230f))).First();
            int north = Node(2050, 230), west = Node(2050, -900), east = Node(2450, -900);
            net.Connect(join.id, north, 25f, PavementSurface.Concrete);
            net.Connect(north, west, 25f, PavementSurface.Concrete);
            net.Connect(west, east, 25f, PavementSurface.Concrete);
            var stand = net.AddStand(StandKind.Parking, new Vector3(2250, 0, -900), 0f, SizeCode.E, PavementSurface.Concrete);
            if (stand == null) throw new InvalidOperationException("Could not add the sandbox container stand.");
            stand.noSmallAircraft = true;
            var layout = net.Layout(stand); var park = layout.ParkPosition(type);
            airport.aprons.Add(ApronArea.Rect(park + new Vector2(70, 0), new Vector2(280, 260), 0f, PavementSurface.Concrete));
            var station = new GameObject("Sandbox container hall").AddComponent<ContainerStation>();
            station.transform.SetParent(airport.transform, false);
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
            var lane = net.edges.First(e => !e.IsRoad && (e.a == west && e.b == east || e.a == east && e.b == west));
            int junction = net.SplitEdge(lane, new Vector3(x, 0, -900));
            net.Connect(exit, junction, 14f, PavementSurface.Concrete, false, RoadClass.Service);
            stand.baggageEntryNode = ne; stand.baggageExitNode = exit;
            station.dispatchNode = home; station.arrivalsNode = arrivals; station.arrivalsBearing = 90;
            station.loaderParking = new Vector2(x + 55, park.y + 30);
            station.automaticHold = new ContainerHoldConfiguration { aircraft = type };
            station.standIds.Add(stand.id); station.automaticHandling = true;
            var bulk = new GameObject("Sandbox widebody bulk hall").AddComponent<BaggageStation>();
            bulk.transform.SetParent(airport.transform, false);
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
            bulk.automaticHold = new BulkHoldConfiguration { aircraft = type, cargoPoint = "Cargo_Bulk" };
            bulk.incomingBags = 20; bulk.outgoingBags = 30;
            bulk.standIds.Add(stand.id); bulk.automaticHandling = true; station.bulkStation = bulk;
            var plane = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AirCity/Aircraft/Widebody/Widebody.prefab"), airport.transform)).GetComponent<AirCity.Aircraft.Aircraft>();
            plane.name = "Widebody sandbox container turnaround";
            plane.transform.SetPositionAndRotation(airport.transform.TransformPoint(new Vector3(park.x, 0, park.y)),
                Quaternion.LookRotation(airport.transform.TransformDirection(new Vector3(layout.nose.x, 0, layout.nose.y))));
            var depot = airport.AddDepot(park + new Vector2(90, -100), Vector2.up);
            VehicleSetup.Assign(depot); depot.autoDispatch = false; depot.SetFleet(0, 0); depot.SetTugs(0, 1);
            airport.BuildGround(); net.Rebuild(); WireCamera(station);
            return station;
        }

        static void WireCamera(ContainerStation station)
        {
            var camera = Camera.main;
            if (camera == null) return;
            var tool = camera.GetComponent<DevAirfieldTool>();
            if (tool == null) tool = camera.gameObject.AddComponent<DevAirfieldTool>();
            tool.airport = station.Airport;
            var controls = camera.GetComponent<DevContainerControls>();
            if (controls == null) controls = camera.gameObject.AddComponent<DevContainerControls>();
            controls.station = station;
        }

        public static void FromCommandLine()
        {
            try
            {
                Enable();
                var station = UnityEngine.Object.FindObjectsByType<ContainerStation>(FindObjectsInactive.Exclude).Single(s => s.name == "Sandbox container hall");
                var airport = station.Airport;
                var plane = airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>().Single(a => a.name == "Widebody sandbox container turnaround");
                airport.Simulate(.1f);
                var job = airport.Baggage.ContainerJobOf(plane); var bulk = airport.Baggage.JobOf(plane);
                if (job == null || bulk == null) throw new InvalidOperationException(station.Problem ?? station.bulkStation.Problem ?? "No automatic widebody manifests.");
                var departure = airport.Traffic.Depart(plane, out var reason);
                if (departure == null) throw new InvalidOperationException(reason);
                bool captured = false;
                for (int i = 0; i < 20000 && departure.Phase != FlightPhase.Taxi; i++)
                {
                    airport.Simulate(.25f); station.Present(.25f); station.bulkStation.Present(.25f);
                    if (!captured && job.State == ContainerTurnaroundState.MainToFront && job.TransferProgress > .3f)
                    {
                        Directory.CreateDirectory("Captures/ContainerSandbox");
                        var camera = Camera.main; var p = job.Fleet.LoaderPose.position;
                        var target = airport.transform.TransformPoint(new Vector3(p.x - 15f, 0f, p.y));
                        camera.transform.position = target + new Vector3(100, 90, 100); camera.transform.LookAt(target);
                        SceneCapture.Capture(camera, "Captures/ContainerSandbox/loading.png", 1800, 1100); captured = true;
                    }
                }
                if (!job.DepartureReady || !bulk.DepartureReady || departure.Phase != FlightPhase.Taxi)
                    throw new InvalidOperationException($"Sandbox containers: {job.State}: {job.Waiting}; bulk: {bulk.State}: {bulk.Waiting}; {departure.Status}");
                int count = job.Fleet.ArrivalsBags.Count + job.Hold.Units.Sum(u => u.Contents.Count) + bulk.Fleet.Arrivals.Count + bulk.Hold.Count;
                float weight = job.Fleet.ArrivalsBags.Weight + job.Hold.Units.Sum(u => u.Contents.Weight) + bulk.Fleet.Arrivals.Weight + bulk.Hold.Weight;
                if (count != 380 || Mathf.Abs(weight - 5700f) > .01f) throw new InvalidOperationException("Widebody sandbox baggage conservation failed.");
                Debug.Log("Sandbox containers passed: 2 incoming / 3 outgoing LD3s, 20 incoming / 30 outgoing bulk bags; all 380 bags / 5700 kg conserved; both fleets returned and departure taxiing.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
