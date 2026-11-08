using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Dev
{
    /// <summary>Capture the actual Game-view HUD, including IMGUI, in compact and expanded modes.</summary>
    [InitializeOnLoad]
    public static class DevHudReview
    {
        const string Running="AirCity.HudReview.Running",Stage="AirCity.HudReview.Stage",Started="AirCity.HudReview.Started";
        const string Root="Captures/DevHud/";
        static double captureAt;
        static DevHudReview() { if (SessionState.GetBool(Running,false)) EditorApplication.update+=Update; }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            Directory.CreateDirectory(Root);
            foreach (var name in new[] { "compact","expanded","diagnostics" }) File.Delete(Root+name+".png");
            var gameView=typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            var view=EditorWindow.GetWindow(gameView);
            view.position=new Rect(20,20,1440,930); view.Show();
            SessionState.SetBool(Running,true); SessionState.SetInt(Stage,0);
            SessionState.SetFloat(Started,(float)EditorApplication.timeSinceStartup);
            EditorApplication.update-=Update; EditorApplication.update+=Update;
            EditorApplication.EnterPlaymode();
        }
        static void Update()
        {
            try
            {
                if (EditorApplication.timeSinceStartup-SessionState.GetFloat(Started,0)>90) throw new Exception("HUD review timed out.");
                int stage=SessionState.GetInt(Stage,0);
                if (stage==4 && !EditorApplication.isPlaying)
                { SessionState.SetBool(Running,false); EditorApplication.update-=Update; EditorApplication.Exit(0); return; }
                if (!EditorApplication.isPlaying) return;
                var toolbar=Object.FindAnyObjectByType<DevToolbar>();
                if (toolbar==null) return;
                if (stage==0)
                {
                    toolbar.ShowMore=false;
                    Object.FindAnyObjectByType<Airport>().timeScale=1;
                    Object.FindAnyObjectByType<DevBaggageControls>()?.FocusStation();
                    captureAt=EditorApplication.timeSinceStartup+2;
                    SessionState.SetInt(Stage,1); return;
                }
                if (stage>3 || EditorApplication.timeSinceStartup<captureAt) return;
                string name=stage==1 ? "compact" : stage==2 ? "expanded" : "diagnostics";
                string path=Root+name+".png";
                if (!File.Exists(path))
                {
                    ScreenCapture.CaptureScreenshot(path);
                    captureAt=EditorApplication.timeSinceStartup+1;
                    Debug.Log($"HUD REVIEW capturing {name}, {Screen.width}x{Screen.height}, details={toolbar.ShowMore}");
                    return;
                }
                if (stage==1) toolbar.ShowMore=true;
                if (stage==2)
                {
                    var folds=(HashSet<string>)typeof(DevToolbar).GetField("folded",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                    folds.Remove("diagnostics");
                }
                SessionState.SetInt(Stage,stage+1); captureAt=EditorApplication.timeSinceStartup+1;
                if (stage==3) { Debug.Log("HUD REVIEW passed: compact, expanded and optional diagnostics captured."); EditorApplication.ExitPlaymode(); }
            }
            catch (Exception e)
            { SessionState.SetBool(Running,false); Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
