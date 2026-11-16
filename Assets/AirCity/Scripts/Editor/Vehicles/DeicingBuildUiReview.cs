using System;
using System.IO;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>Real Play-mode screenshots of the shared browser, management and render-only placement models.</summary>
    [InitializeOnLoad]
    public static class DeicingBuildUiReview
    {
        const string Running = "AirCity.DeicingBuildUiReview.Running", Stage = "AirCity.DeicingBuildUiReview.Stage";
        const string Configured = "AirCity.DeicingBuildUiReview.Configured", Started = "AirCity.DeicingBuildUiReview.Started";
        const string ManagementOnly = "AirCity.DeicingBuildUiReview.ManagementOnly", Prepared = "AirCity.DeicingBuildUiReview.Prepared";
        const string CatalogOnly = "AirCity.DeicingBuildUiReview.CatalogOnly";
        const string Root = "Captures/DeicingBuild/";
        static readonly string[] Shots = { "catalog-pads", "catalog-buildings", "catalog-objects", "management-overview", "management-fleet", "management-supply", "supply-inspector", "preview-supply", "preview-supply-rotated", "preview-supply-invalid", "preview-parking", "preview-depot", "preview-terminal", "preview-apron", "preview-pad" };
        static double captureAt;
        static DeicingBuildUiReview() { if (SessionState.GetBool(Running, false)) EditorApplication.update += Update; }

        public static void RunFromCommandLine() => Start(false);
        public static void RunManagementFromCommandLine() => Start(true);
        public static void RunCatalogFromCommandLine() => Start(false, true);
        static void Start(bool managementOnly, bool catalogOnly = false)
        {
            try
            {
                EditorSceneManager.OpenScene(File.Exists(DeicingSupplyReview.ScenePath) ? DeicingSupplyReview.ScenePath : DeicingReview.ScenePath);
                Directory.CreateDirectory(Root);
                for (int i = 0; i < Shots.Length; i++)
                    if (catalogOnly ? i <= 2 || i == 7 : !managementOnly || i >= 3 && i <= 5) File.Delete(Root + Shots[i] + ".png");
                var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
                view.position = new Rect(20, 20, 1600, 1050); view.Show(); view.Focus();
                SessionState.SetBool(Running, true); SessionState.SetBool(Configured, false); SessionState.SetInt(Stage, managementOnly ? 3 : 0);
                SessionState.SetBool(ManagementOnly, managementOnly); SessionState.SetBool(Prepared, false);
                SessionState.SetBool(CatalogOnly, catalogOnly);
                SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
                EditorApplication.update -= Update; EditorApplication.update += Update;
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Fail(error); }
        }

        static void Update()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0) > 240) throw new Exception("Build UI review timed out.");
                int stage = SessionState.GetInt(Stage, 0);
                if (stage == Shots.Length + 1 && !EditorApplication.isPlaying)
                {
                    SessionState.SetBool(Running, false); EditorApplication.update -= Update;
                    Debug.Log(SessionState.GetBool(CatalogOnly, false) ? "Compact deicing catalog review passed: three tabs and a single clean placement boundary."
                        : SessionState.GetBool(ManagementOnly, false) ? "Deicing management review passed: all three management pages."
                        : "Deicing build UI review passed: all catalog and management tabs, selected supply, model previews and arbitrary rotation captured in Play mode.");
                    EditorApplication.Exit(0); return;
                }
                if (!EditorApplication.isPlaying) return;
                if (stage == Shots.Length) { SessionState.SetInt(Stage, stage + 1); EditorApplication.ExitPlaymode(); return; }
                var airport = Object.FindAnyObjectByType<Airport>(); var camera = Camera.main;
                if (airport == null || camera == null) return;
                if (!SessionState.GetBool(Configured, false))
                {
                    Configure(stage, airport, camera);
                    SessionState.SetBool(Configured, true); captureAt = EditorApplication.timeSinceStartup + 1.5; return;
                }
                if (EditorApplication.timeSinceStartup < captureAt) return;
                string path = Root + Shots[stage] + ".png";
                if (!File.Exists(path)) { ScreenCapture.CaptureScreenshot(path); captureAt = EditorApplication.timeSinceStartup + .75; return; }
                Debug.Log("Build UI captured: " + Shots[stage]);
                int next = SessionState.GetBool(ManagementOnly, false) && stage == 5 ? Shots.Length : stage + 1;
                if (SessionState.GetBool(CatalogOnly, false)) next = stage == 2 ? 7 : stage == 7 ? Shots.Length : next;
                SessionState.SetInt(Stage, next);
                SessionState.SetBool(Configured, false);
            }
            catch (Exception error) { Fail(error); }
        }

        static void Configure(int stage, Airport airport, Camera camera)
        {
            airport.timeScale = 0;
            var tool = camera.GetComponent<DevAirfieldTool>(); if (tool == null) tool = camera.gameObject.AddComponent<DevAirfieldTool>(); tool.airport = airport;
            var hud = camera.GetComponent<DevToolbar>(); if (hud == null) hud = camera.gameObject.AddComponent<DevToolbar>();
            hud.ShowMore = false; DevGui.Visible = true;
            var area = airport.Deicing.WorkAreas[0];
            if (!SessionState.GetBool(Prepared, false))
            {
                SessionState.SetFloat(Started, (float)EditorApplication.timeSinceStartup);
                PrepareManagementFixture(airport); SessionState.SetBool(Prepared, true);
            }
            if (stage <= 2) { hud.OpenDeicingCatalog(stage); return; }
            if (stage <= 5)
            {
                hud.OpenDeicingManagement(stage == 3 ? null : stage == 4 && airport.Deicing.WorkAreas.Count > 1 ? airport.Deicing.WorkAreas[1] : area);
                Field(hud, "deicingManagementTab", stage - 3); return;
            }
            hud.CloseDeicingManagement();
            if (stage == 6) { tool.SelectDeicingWorkArea(area); return; }
            // Freeze input while exercising the same draft builders used by the cursor. A preview never places data.
            tool.enabled = false;
            var orbit = camera.GetComponent<DevOrbitCamera>(); if (orbit != null) orbit.enabled = false;
            var rig = airport.GetComponent<DeicingRig>(); if (rig != null) rig.enabled = false;
            Vector2 p = area.position;
            int areaCount = airport.Deicing.WorkAreas.Count, bays = airport.Deicing.ParkingBays.Count;
            if (stage <= 9)
            {
                Field(hud, "deicingCatalogTab", 1);
                tool.BeginMoveDeicingWorkArea(area);
                if (stage == 8) { tool.PlacementSnap = false; tool.RotatePlacement(37); }
                if (stage == 9) p += new Vector2(1000, 1000);
                Invoke(tool, "DeicingWorkAreaTool", Keyboard.current, false, true, airport.transform.TransformPoint(new Vector3(p.x, 0, p.y)));
            }
            else if (stage == 10)
            {
                Field(hud, "deicingCatalogTab", 2);
                tool.BeginDeicingParking();
                foreach (var bay in airport.Deicing.ParkingBays)
                    if (!bay.legacy && airport.Deicing.TruckFor(bay) == null) { p = bay.position; tool.BeginMoveDeicingParking(bay); break; }
                Invoke(tool, "DeicingParkingTool", Keyboard.current, false, true, airport.transform.TransformPoint(new Vector3(p.x, 0, p.y)));
            }
            else if (stage == 11) tool.SetTool(DevTool.Depot);
            else if (stage == 12 || stage == 13)
            {
                tool.SetTool(stage == 12 ? DevTool.Terminal : DevTool.Apron);
                var corners = (System.Collections.Generic.List<Vector2>)typeof(DevAirfieldTool).GetField("apronCorners", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tool);
                corners.Clear(); corners.Add(p + new Vector2(-14, -9)); corners.Add(p + new Vector2(14, -9)); corners.Add(p + new Vector2(14, 9)); corners.Add(p + new Vector2(-14, 9));
                Field(tool, "apronCursor", corners[0]);
            }
            else
            {
                Field(hud, "deicingCatalogTab", 0);
                tool.SetTool(DevTool.DeIcing);
                Invoke(tool, "StandTool", Mouse.current, Keyboard.current, false, true, airport.transform.TransformPoint(new Vector3(p.x, 0, p.y)));
            }
            var world = airport.transform.TransformPoint(new Vector3(p.x, 0, p.y));
            Invoke(tool, "RefreshBuildPreview", true, world);
            if (stage == 7 && SessionState.GetBool(CatalogOnly, false))
            {
                var outlines = typeof(DevAirfieldTool).GetField("outlines", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tool);
                Invoke(outlines, "Begin"); Invoke(tool, "DrawDeicingAreaPreview", true); Invoke(outlines, "End");
                var pool = (System.Collections.IList)outlines.GetType().GetField("pool", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(outlines);
                int visible = 0;
                foreach (LineRenderer line in pool) if (line != null && line.enabled) { visible++; if (line.positionCount != 4) visible += 100; }
                if (visible != 1)
                    throw new Exception("Supply preview must show only its four-corner footprint, without an interior guide line.");
            }
            if (!tool.PlacementPreviewVisible || tool.PlacementPreviewObject.GetComponentsInChildren<Renderer>().Length == 0)
                throw new Exception("Placement model missing for " + Shots[stage]);
            if (areaCount != airport.Deicing.WorkAreas.Count || bays != airport.Deicing.ParkingBays.Count) throw new Exception("Placement preview mutated airport layout.");
            if (stage == 8 && Mathf.Abs(Mathf.DeltaAngle(area.heading + 37, tool.PlacementHeading)) > .01f) throw new Exception("Free rotation did not retain the requested 37°.");
            camera.transform.position = world + new Vector3(-27, 28, -34); camera.transform.LookAt(world + Vector3.up * 1.5f);
        }

        static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static void PrepareManagementFixture(Airport airport)
        {
            var service = airport.Deicing;
            foreach (var node in airport.Taxiways.nodes)
                if (service.OutsideAccessProblem(node.id) == null) { service.AddOutsideAccess(node.id, out _); break; }
            var area = service.AddWorkArea(new Vector2(55, -18), 0);
            var stand = airport.Taxiways.AddStand(StandKind.DeIcing, new Vector3(0, 0, 110), 0, SizeCode.C, PavementSurface.Concrete);
            if (stand != null && area != null) service.AssignWorkArea(service.Configure(stand), area);
            var bay = service.AddParkingBay(new Vector2(-95, 20), 0);
            if (bay == null) throw new Exception("Management review parking fixture: " + service.LastPurchaseProblem);
            airport.Taxiways.Rebuild();
            airport.Simulate(.05f); // Refresh queued statuses after authoring the review's outside connection.
            airport.GetComponent<DeicingRig>()?.Refresh();
        }
        static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);
        static void Fail(Exception error)
        {
            SessionState.SetBool(Running, false); EditorApplication.update -= Update;
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
}
