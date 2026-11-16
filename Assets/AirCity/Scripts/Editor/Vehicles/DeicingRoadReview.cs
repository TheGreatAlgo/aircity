using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>Small render-only supply/road authoring fixture; no long simulation or native window automation.</summary>
    public static class DeicingRoadReview
    {
        const string Root = "Captures/DeicingRoad/";
        public const string ScenePath = "Assets/AirCity/Scenes/DeicingRoadReview.unity";
        public static void RunFromCommandLine()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        public static void Build()
        {
            Keyboard temporaryKeyboard = null;
            try
            {
                if (Keyboard.current == null) temporaryKeyboard = InputSystem.AddDevice<Keyboard>();
                BuildCore();
            }
            finally { if (temporaryKeyboard != null) InputSystem.RemoveDevice(temporaryKeyboard); }
        }

        static void BuildCore()
        {
            var elapsed = Stopwatch.StartNew();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            if (assets == null || assets.truckType == null || assets.tankerType == null || assets.tankPrefab == null)
                throw new InvalidOperationException("Generate deicing assets before the road review.");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.60f, .69f, .80f);
            RenderSettings.ambientEquatorColor = new Color(.43f, .48f, .56f);
            RenderSettings.ambientGroundColor = new Color(.26f, .29f, .33f);
            var sun = new GameObject("Winter daylight").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40, -35, 0); RenderSettings.sun = sun;
            var root = new GameObject("Supply and road authoring review"); root.SetActive(false);
            var airport = root.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 150; airport.timeScale = 0; root.SetActive(true);
            var draft = new DeicingWorkArea();
            // This is the entire apron: service vehicles must fit without an invisible paved margin.
            airport.aprons.Add(ApronArea.Polygon(draft.Outline, PavementSurface.Concrete));
            airport.BuildGround();
            var service = airport.Deicing;
            var area = service.AddWorkArea(Vector2.zero, 0);
            if (area == null) throw new InvalidOperationException(service.LastPurchaseProblem);
            if (!service.BuyRecycling(area, out var why)) throw new InvalidOperationException(why);
            var rig = airport.GetComponent<DeicingRig>(); if (rig == null) rig = airport.gameObject.AddComponent<DeicingRig>();
            rig.assets = assets; rig.Refresh();
            if (service.ParkingBays.Count != 0 || rig.TruckCount != 0)
                throw new InvalidOperationException("A supply station created truck parking or trucks automatically.");
            AssertInside(area, area.RefillPose(assets.truckType), assets.truckType, "fill truck");
            AssertInside(area, service.DeliveryPose(area), assets.tankerType, "unloading tanker");
            AssertInside(area, new BaggageVehiclePose(area.RecyclerPosition, area.RecyclerFacing), assets.recyclerType, "recycler");
            var camera = new GameObject("Supply review camera").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 500;
            var tool = camera.gameObject.AddComponent<DevAirfieldTool>(); tool.airport = airport; tool.enabled = false;
            Field(tool, "cam", camera);
            Directory.CreateDirectory(Root);
            View(camera, area.position, 17);
            // Invoke the actual placement builder while the installed presentation is hidden.
            rig.enabled = false;
            tool.BeginMoveDeicingWorkArea(area);
            Invoke(tool, "DeicingWorkAreaTool", Keyboard.current, false, true, World(airport, area.position));
            Invoke(tool, "RefreshBuildPreview", true, World(airport, area.position));
            if (!tool.PlacementPreviewVisible || tool.PlacementPreviewCount != 2)
                throw new InvalidOperationException("Supply preview is missing the tank or recycler.");
            SceneCapture.Capture(camera, Root + "supply-preview.png", 1440, 1080);
            tool.SetTool(DevTool.Road); rig.enabled = true; rig.Refresh();
            var net = airport.Taxiways;
            var line = new TaxiwayPreview();
            var ports = area.RoadPorts;
            for (int i = 0; i < ports.Length; i++)
            {
                var port = ports[i];
                var outward = Vector2.Dot(port - area.position, area.Axis) > 0 ? area.Axis : -area.Axis;
                var start = port + outward * 24;
                int startNode = net.AddNode(Ground(start)); Field(tool, "chainNode", startNode);
                var query = port + area.Right * .45f + outward * .2f;
                var snapped = Invoke(tool, "SnapLine", net, query, true, false, World(airport, query));
                var at = (Vector2)snapped.GetType().GetField("p").GetValue(snapped);
                if (Vector2.Distance(at, port) > .01f)
                    throw new InvalidOperationException("Road did not snap to " + DeicingWorkArea.RoadPortName(i) + ": " + at);
                if (i == 0 || i == 2)
                {
                    line.Show(airport, start, at, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
                    View(camera, area.position + outward * 10, 28);
                    SceneCapture.Capture(camera, Root + (i == 0 ? "road-snap-fill.png" : "road-snap-unload.png"), 1440, 1080);
                    line.Hide();
                }
                int endNode = (int)Invoke(tool, "Commit", net, snapped);
                net.Connect(startNode, endNode, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            }
            line.Destroy(); net.Rebuild(); rig.Refresh();
            if (!tool.TrySnapDeicingStationToRoad(area.position + area.Right * .6f, area.heading, 2, false,
                    out var snappedStation, out float snappedHeading, out _, area) ||
                Vector2.Distance(snappedStation, area.position) > .01f || Mathf.Abs(Mathf.DeltaAngle(snappedHeading, area.heading)) > .01f)
                throw new InvalidOperationException("A manually rotated station did not snap back onto an existing service-road mouth.");
            var truckStart = new BaggageVehiclePose(area.FillEntry - area.Axis * 18, area.Axis);
            var tankerStart = new BaggageVehiclePose(area.UnloadEntry + area.Axis * 18, -area.Axis);
            if (DeicingMotion.Plan(airport, assets.truckType, truckStart, area.RefillPose(assets.truckType), null) == null ||
                DeicingMotion.Plan(airport, assets.tankerType, tankerStart, service.DeliveryPose(area), null) == null)
                throw new InvalidOperationException("An aligned service vehicle cannot reach its berth over the roads and exact station apron.");
            View(camera, area.position, 42);
            SceneCapture.Capture(camera, Root + "four-connected-ports.png", 1440, 1080);
            tool.SetTool(DevTool.Select);
            if (tool.PlacementPreviewVisible) throw new InvalidOperationException("Cancelling left a placement model behind.");
            if (service.ParkingBays.Count != 0) throw new InvalidOperationException("Road authoring created truck parking.");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
            File.WriteAllText(Root + "report.json", JsonUtility.ToJson(new Report { seconds = (float)elapsed.Elapsed.TotalSeconds,
                roadPorts = ports.Length, parkingBays = service.ParkingBays.Count, exactApron = true }, true));
            Debug.Log($"Deicing road review passed in {elapsed.Elapsed.TotalSeconds:0.0}s: real supply preview, four snapped road ports, contained fill/unload vehicles, no automatic parking.");
        }

        [Serializable] sealed class Report { public float seconds; public int roadPorts, parkingBays; public bool exactApron; }
        static void AssertInside(DeicingWorkArea area, BaggageVehiclePose pose, VehicleType type, string label)
        {
            if (type == null) throw new InvalidOperationException("Missing " + label + " type.");
            foreach (float x in new[] { type.bounds.min.x, type.bounds.max.x })
                foreach (float z in new[] { type.bounds.min.z, type.bounds.max.z })
                    if (!area.Contains(pose.Point(new Vector3(x, 0, z))))
                        throw new InvalidOperationException(label + " extends beyond the station's visible paved footprint.");
        }
        static void View(Camera camera, Vector2 centre, float size)
        {
            camera.orthographicSize = size;
            camera.transform.position = Ground(centre) + new Vector3(0, 50, -20);
            camera.transform.LookAt(Ground(centre));
        }
        static Vector3 Ground(Vector2 point) => new Vector3(point.x, 0, point.y);
        static Vector3 World(Airport airport, Vector2 point) => airport.transform.TransformPoint(Ground(point));
        static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
