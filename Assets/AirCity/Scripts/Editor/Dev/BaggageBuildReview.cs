using System;
using System.IO;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Dev
{
    /// <summary>Review the player palette, placement footprint and selected-hall controls in the actual Game view.</summary>
    [InitializeOnLoad]
    public static class BaggageBuildReview
    {
        const string Running="AirCity.BaggageBuildReview.Running",Stage="AirCity.BaggageBuildReview.Stage",Started="AirCity.BaggageBuildReview.Started";
        const string Root="Captures/BaggageBuild/";
        static double captureAt;
        static BaggageBuildReview() { if (SessionState.GetBool(Running,false)) EditorApplication.update+=Update; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            Directory.CreateDirectory(Root);
            foreach (var name in new[] { "palette","placed-hall","terminal-link" }) File.Delete(Root+name+".png");
            var view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
            view.position=new Rect(20,20,1440,930); view.Show();
            SessionState.SetBool(Running,true); SessionState.SetInt(Stage,0);
            SessionState.SetFloat(Started,(float)EditorApplication.timeSinceStartup);
            EditorApplication.update-=Update; EditorApplication.update+=Update; EditorApplication.EnterPlaymode();
        }
        static void Update()
        {
            try
            {
                if (EditorApplication.timeSinceStartup-SessionState.GetFloat(Started,0)>100) throw new Exception("Baggage build HUD review timed out.");
                int stage=SessionState.GetInt(Stage,0);
                if (stage==4 && !EditorApplication.isPlaying)
                { SessionState.SetBool(Running,false); EditorApplication.update-=Update; EditorApplication.Exit(0); return; }
                if (!EditorApplication.isPlaying) return;
                var toolbar=Object.FindAnyObjectByType<DevToolbar>(); if (toolbar==null) return;
                var tool=toolbar.GetComponent<DevAirfieldTool>(); var airport=tool.airport;
                if (stage==1 && Mouse.current!=null)
                {
                    var screen=tool.GetComponent<Camera>().WorldToScreenPoint(new Vector3(8000,0,8000));
                    InputState.Change(Mouse.current.position,new Vector2(screen.x,screen.y));
                }
                if (stage==0)
                {
                    toolbar.ShowMore=false; airport.timeScale=1;
                    var centre=new Vector2(8000,8000);
                    airport.aprons.Add(ApronArea.Rect(centre,new Vector2(500,500),0,PavementSurface.Concrete));
                    var net=airport.Taxiways;
                    int a=net.AddNode(new Vector3(8000,0,7800)),b=net.AddNode(new Vector3(8000,0,8200));
                    net.Connect(a,b,12,PavementSurface.Concrete,false,RoadClass.Service);
                    airport.terminals.Add(Terminal.Polygon(new[] { new Vector2(8130,7980),new Vector2(8160,7980),new Vector2(8160,8060),new Vector2(8130,8060) },3));
                    airport.BuildGround(); net.Rebuild();
                    var orbit=tool.GetComponent<DevOrbitCamera>(); orbit.pivot=new Vector3(8060,0,8000); orbit.distance=240; orbit.pitch=55; orbit.yaw=215;
                    toolbar.OpenBaggageBuilder(); toolbar.GetComponent<DevBaggageControls>().BeginHall(BaggageDepotSize.Medium);
                    captureAt=EditorApplication.timeSinceStartup+2; SessionState.SetInt(Stage,1); return;
                }
                if (stage>3 || EditorApplication.timeSinceStartup<captureAt) return;
                var controls=tool.GetComponent<DevBaggageControls>();
                string name=stage==1 ? "palette" : stage==2 ? "placed-hall" : "terminal-link";
                string path=Root+name+".png";
                if (!File.Exists(path))
                {
                    if (stage==1) controls.Preview(new Vector3(8000,0,8000));
                    ScreenCapture.CaptureScreenshot(path); captureAt=EditorApplication.timeSinceStartup+1;
                    Debug.Log($"BAGGAGE BUILD HUD: {name}, {Screen.width}×{Screen.height}"); return;
                }
                if (stage==1)
                {
                    controls.Place(new Vector3(8000,0,8000));
                    if (tool.SelectedBaggageHall==null) throw new Exception("Player hall placement failed: "+controls.Message);
                }
                if (stage==2)
                {
                    var hall=tool.SelectedBaggageHall;
                    controls.BeginPlacement(BaggagePlacement.Underground);
                    controls.Place(new Vector3(hall.PaintArea.centre.x,0,hall.PaintArea.centre.y));
                    controls.Place(new Vector3(8140,0,8000));
                    if (hall.ConnectedTerminal==null) throw new Exception("Player terminal link failed: "+controls.Message);
                }
                SessionState.SetInt(Stage,stage+1); captureAt=EditorApplication.timeSinceStartup+1;
                if (stage==3) { Debug.Log("BAGGAGE BUILD HUD passed: player palette, first hall and clicked terminal connection."); EditorApplication.ExitPlaymode(); }
            }
            catch (Exception e) { SessionState.SetBool(Running,false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
