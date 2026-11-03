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
    public static class BaggageSandboxSetup
    {
        [MenuItem("AirCity/Vehicles/Enable Sandbox Baggage")]
        public static void Enable()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(AircraftSetup.SandboxScene, OpenSceneMode.Single);
            var airport = UnityEngine.Object.FindFirstObjectByType<Airport>();
            if (airport == null) throw new InvalidOperationException("The sandbox needs an airport.");
            Install(airport);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }

        public static BaggageStation Install(Airport airport)
        {
            VehicleSetup.CreateMissing();
            var existing = airport.GetComponentsInChildren<BaggageStation>().FirstOrDefault(s => s.name == "Sandbox baggage hall");
            if (existing != null) { WireCamera(existing); return existing; }
            var type = AssetDatabase.LoadAssetAtPath<AirCity.Aircraft.AircraftType>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.aircraft");
            var net = airport.Taxiways;
            int Node(float x, float z) => net.AddNode(new Vector3(x, 0f, z));
            // A remote service stand has room for the current conservative aircraft exclusion and forward circulation.
            var join = net.nodes.Where(n => n.runway == null && net.edges.Any(e => !e.IsRoad && (e.a == n.id || e.b == n.id)))
                .OrderBy(n => Vector2.SqrMagnitude(net.At(n.id) - new Vector2(1200f, 230f))).First();
            int north = Node(1250f, 230f), west = Node(1250f, -400f), east = Node(1550f, -400f);
            net.Connect(join.id, north, 15f, PavementSurface.Concrete);
            net.Connect(north, west, 15f, PavementSurface.Concrete);
            net.Connect(west, east, 15f, PavementSurface.Concrete);
            var stand = net.AddStand(StandKind.Parking, new Vector3(1400f, 0f, -400f), 0f, SizeCode.C, PavementSurface.Concrete);
            if (stand == null) throw new InvalidOperationException("Could not add the sandbox baggage stand.");
            stand.noSmallAircraft = true;
            var layout = net.Layout(stand); var park = layout.ParkPosition(type);
            airport.aprons.Add(ApronArea.Rect(park + new Vector2(40f, 0f), new Vector2(180f, 160f), 0f, PavementSurface.Concrete));
            float x = park.x + type.bounds.max.x + 7f + 7.8f + 3.5f;
            int home = Node(x + 20f, park.y - 15f), ne = Node(x + 20f, park.y + 32f), nw = Node(x, park.y + 32f);
            int exit = Node(x, park.y - 40f), arrivals = Node(x + 10f, park.y - 40f), corner = Node(x + 20f, park.y - 40f);
            foreach (var pair in new[] { (home, ne), (ne, nw), (nw, exit), (exit, arrivals), (arrivals, corner), (corner, home) })
                net.Connect(pair.Item1, pair.Item2, 12f, PavementSurface.Concrete, false, RoadClass.Service);
            var lane = net.edges.First(e => !e.IsRoad && (e.a == west && e.b == east || e.a == east && e.b == west));
            int junction = net.SplitEdge(lane, new Vector3(x, 0f, -400f));
            net.Connect(exit, junction, 12f, PavementSurface.Concrete, false, RoadClass.Service);
            stand.baggageEntryNode = nw; stand.baggageExitNode = exit;
            var station = new GameObject("Sandbox baggage hall").AddComponent<BaggageStation>();
            station.transform.SetParent(airport.transform, false);
            station.tractorType = VehicleSetup.Type("BaggageTractor"); station.cartType = VehicleSetup.Type("BaggageCart"); station.loaderType = VehicleSetup.Type("BeltLoader");
            station.bagPrefabs = VehicleSetup.BagPrefabs();
            station.tractorPrefab = VehicleSetup.Prefab("BaggageTractor"); station.cartPrefab = VehicleSetup.Prefab("BaggageCart"); station.loaderPrefab = VehicleSetup.Prefab("BeltLoader");
            station.dispatchNode = home; station.arrivalsNode = arrivals; station.arrivalsBearing = 90f;
            station.loaderParking = park + new Vector2(x - park.x + 40f, -15f);
            station.automaticHold = new BulkHoldConfiguration { aircraft = type, bagCapacity = 240, weightCapacity = 3600f };
            station.standIds.Add(stand.id); station.automaticHandling = true;
            var plane = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AirCity/Aircraft/Narrowbody/Narrowbody.prefab"), airport.transform)).GetComponent<AirCity.Aircraft.Aircraft>();
            plane.name = "Narrowbody baggage turnaround";
            plane.transform.SetPositionAndRotation(airport.transform.TransformPoint(new Vector3(park.x, 0f, park.y)),
                Quaternion.LookRotation(airport.transform.TransformDirection(new Vector3(layout.nose.x, 0f, layout.nose.y))));
            var depot = airport.AddDepot(park + new Vector2(65f, -63f), Vector2.up);
            VehicleSetup.Assign(depot); depot.autoDispatch = false; depot.SetFleet(0, 0); depot.SetTugs(1, 0);
            airport.BuildGround(); net.Rebuild(); WireCamera(station);
            return station;
        }

        static void WireCamera(BaggageStation station)
        {
            var camera = Camera.main;
            if (camera == null) return;
            var controls = camera.GetComponent<DevBaggageControls>();
            if (controls == null) controls = camera.gameObject.AddComponent<DevBaggageControls>();
            controls.station = station;
        }

        public static void FromCommandLine()
        {
            try
            {
                Enable();
                var airport = UnityEngine.Object.FindFirstObjectByType<Airport>();
                var station = airport.GetComponentsInChildren<BaggageStation>().First(s => s.name == "Sandbox baggage hall");
                var plane = airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>().First(a => a.name == "Narrowbody baggage turnaround");
                airport.Simulate(0.1f);
                var job = airport.Baggage.JobOf(plane);
                if (job == null) throw new InvalidOperationException(station.Problem ?? "No automatic baggage job.");
                var departure = airport.Traffic.Depart(plane, out var reason);
                if (departure == null) throw new InvalidOperationException(reason);
                Directory.CreateDirectory("Captures/BaggageSandbox");
                bool captured = false;
                for (int i = 0; i < 12000 && departure.Phase != FlightPhase.Taxi; i++)
                {
                    airport.Simulate(0.25f); station.Present(0.25f);
                    if (!captured && job.State == BaggageTurnaroundState.LoadingHold)
                    {
                        var camera = Camera.main; var pose = job.Fleet.LoaderPose;
                        var target = airport.transform.TransformPoint(new Vector3(pose.position.x - 15f, 0f, pose.position.y));
                        camera.transform.position = target + new Vector3(100f, 100f, 100f); camera.transform.LookAt(target);
                        SceneCapture.Capture(camera, "Captures/BaggageSandbox/loading.png", 1800, 1100); captured = true;
                    }
                }
                if (!job.DepartureReady || departure.Phase != FlightPhase.Taxi || job.ArrivalTrips < 2 || job.DepartureTrips < 2)
                    throw new InvalidOperationException($"Sandbox baggage: {job.State}: {job.Waiting}; {departure.Status}");
                Debug.Log($"Sandbox baggage passed: {job.IncomingCount} arriving / {job.OutgoingCount} departing bags; " +
                    $"{job.ArrivalTrips} arrival and {job.DepartureTrips} departure trips; departure taxiing.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
