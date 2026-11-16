using System;
using System.IO;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>Captures the actual selected-pad inspector and supply pop-out in the Game view.</summary>
    [InitializeOnLoad]
    public static class DeicingHudReview
    {
        const string Running = "AirCity.DeicingHudReview.Running", Stage = "AirCity.DeicingHudReview.Stage";
        const string Started = "AirCity.DeicingHudReview.Started", Root = "Captures/Deicing/";
        static double captureAt;
        static DeicingHudReview() { if (SessionState.GetBool(Running, false)) EditorApplication.update += Update; }

        public static void RunFromCommandLine()
        {
            try
            {
                if (!File.Exists(DeicingReview.ScenePath)) DeicingReview.Build(false);
                EditorSceneManager.OpenScene(File.Exists(DeicingSupplyReview.ScenePath) ? DeicingSupplyReview.ScenePath : DeicingReview.ScenePath);
                Directory.CreateDirectory(Root);
                foreach (var name in new[] { "pad-inspector", "equipment-supply", "supply-area-inspector", "recycling-inspector", "operating-budget", "crew-costs" }) File.Delete(Root + name + ".png");
                var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
                view.position = new Rect(20, 20, 1600, 1050); view.Show();
                SessionState.SetBool(Running, true); SessionState.SetInt(Stage, 0);
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
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Started, 0) > 100)
                    throw new Exception("Deicing HUD capture timed out.");
                int stage = SessionState.GetInt(Stage, 0);
                if (stage == 8 && !EditorApplication.isPlaying)
                {
                    SessionState.SetBool(Running, false); EditorApplication.update -= Update;
                    Debug.Log("Deicing HUD review passed: equipment, supply, recycling, operating budget and crew controls captured in Play mode.");
                    EditorApplication.Exit(0); return;
                }
                if (!EditorApplication.isPlaying) return;
                var airport = Object.FindAnyObjectByType<Airport>(); var camera = Camera.main;
                if (airport == null || camera == null) return;
                if (stage == 0)
                {
                    airport.timeScale = 0f;
                    var tool = camera.GetComponent<DevAirfieldTool>();
                    if (tool == null) tool = camera.gameObject.AddComponent<DevAirfieldTool>();
                    tool.airport = airport;
                    var toolbar = camera.GetComponent<DevToolbar>();
                    if (toolbar == null) toolbar = camera.gameObject.AddComponent<DevToolbar>();
                    toolbar.ShowMore = false; DevGui.Visible = true;
                    var stand = airport.Taxiways.stands.Find(s => s.DeIcing);
                    if (stand == null) throw new Exception("Deicing review scene has no bay.");
                    tool.SelectStand(stand);
                    bool supplier = false, ready = false;
                    foreach (var supply in airport.Deicing.WorkAreas) supplier |= supply.DeliveryPending;
                    for (int i = 0; i < 12000; i++)
                    {
                        airport.Simulate(.05f);
                        if (supplier)
                        {
                            foreach (var delivery in airport.Deicing.Deliveries)
                                if (delivery.Phase == DeicingDeliveryPhase.Unloading && delivery.HoseConnected) ready = true;
                        }
                        else ready = airport.Deicing.JobFor(airport.Deicing.For(stand))?.IsSpraying == true;
                        if (ready) break;
                    }
                    if (!ready) throw new Exception("Play-mode deicing or tanker service did not start.");
                    var area = airport.Deicing.WorkAreas[0];
                    if (!area.recyclingInstalled && !airport.Deicing.BuyRecycling(area, out var problem)) throw new Exception(problem);
                    airport.Simulate(.5f);
                    airport.GetComponent<DeicingRig>()?.Refresh();
                    if (supplier && (airport.GetComponent<DeicingRig>().TankerCount == 0 || airport.GetComponent<DeicingRig>().DeliveryHoseCount == 0))
                        throw new Exception("Play-mode tanker or connected transfer hose is missing.");
                    SessionState.SetInt(Stage, 1); captureAt = EditorApplication.timeSinceStartup + 2; return;
                }
                if (stage > 7 || EditorApplication.timeSinceStartup < captureAt) return;
                var hud = camera.GetComponent<DevToolbar>();
                if (stage >= 1 && stage <= 6)
                {
                    string[] names = { "pad-inspector", "equipment-supply", "supply-area-inspector", "recycling-inspector", "operating-budget", "crew-costs" };
                    string name = names[stage-1];
                    string path = Root + name + ".png";
                    if (!File.Exists(path))
                    {
                        ScreenCapture.CaptureScreenshot(path); captureAt = EditorApplication.timeSinceStartup + 1;
                        Debug.Log($"Deicing HUD capturing {name}, {Screen.width} x {Screen.height}"); return;
                    }
                    if (stage == 1)
                    {
                        var toggle = typeof(DevToolbar).GetMethod("Toggle", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (toggle == null) throw new Exception("DevToolbar pop-out toggle was not found.");
                        toggle.Invoke(hud, new object[] { "deicing-equipment" });
                    }
                    if (stage == 2)
                    {
                        typeof(DevToolbar).GetField("openPop", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(hud, null);
                        camera.GetComponent<DevAirfieldTool>().SelectDeicingWorkArea(airport.Deicing.WorkAreas[0]);
                    }
                    if (stage == 3 || stage == 4)
                        typeof(DevToolbar).GetMethod("Toggle", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(hud,
                            new object[] { stage == 3 ? "deicing-recycling" : "deicing-budget" });
                    if (stage == 5)
                    {
                        camera.GetComponent<DevAirfieldTool>().SelectStand(airport.Taxiways.stands.Find(s=>s.DeIcing));
                        typeof(DevToolbar).GetMethod("Toggle", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(hud,new object[] { "deicing-operations" });
                    }
                    SessionState.SetInt(Stage, stage + 1); captureAt = EditorApplication.timeSinceStartup + 1; return;
                }
                SessionState.SetInt(Stage, 8); EditorApplication.ExitPlaymode();
            }
            catch (Exception error) { Fail(error); }
        }
        static void Fail(Exception error)
        {
            SessionState.SetBool(Running, false); EditorApplication.update -= Update;
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }
}
