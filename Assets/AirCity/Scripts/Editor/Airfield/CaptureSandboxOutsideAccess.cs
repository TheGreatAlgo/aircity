using System;
using System.IO;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>Reviews the additive Sandbox upgrade without saving or rebuilding its authored scene.</summary>
    [InitializeOnLoad]
    public static class CaptureSandboxOutsideAccess
    {
        const string Running="AirCity.SandboxOutsideReview.Running",Stage="AirCity.SandboxOutsideReview.Stage",Started="AirCity.SandboxOutsideReview.Started";
        const string PlayCapture="Captures/DeicingBuild/sandbox-outside-play.png";
        static double captureAt;
        static CaptureSandboxOutsideAccess() { if(SessionState.GetBool(Running,false)) EditorApplication.update+=UpdatePlayReview; }
        public static void RunPlayModeFromCommandLine()
        {
            try
            {
                EditorSceneManager.OpenScene(SandboxOutsideAccess.ScenePath,OpenSceneMode.Single);
                Directory.CreateDirectory(Path.GetDirectoryName(PlayCapture)); File.Delete(PlayCapture);
                var view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
                view.position=new Rect(20,20,1600,1050); view.Show(); view.Focus();
                SessionState.SetBool(Running,true); SessionState.SetInt(Stage,0); SessionState.SetFloat(Started,(float)EditorApplication.timeSinceStartup);
                EditorApplication.update-=UpdatePlayReview; EditorApplication.update+=UpdatePlayReview;
                EditorApplication.EnterPlaymode();
            }
            catch(Exception error) { FailPlayReview(error); }
        }
        static void UpdatePlayReview()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Started,0f)>150d) throw new Exception("Sandbox startup review timed out.");
                int stage=SessionState.GetInt(Stage,0);
                if(!EditorApplication.isPlaying)
                {
                    if(stage==2)
                    {
                        SessionState.SetBool(Running,false); EditorApplication.update-=UpdatePlayReview;
                        Debug.Log("Sandbox Play startup verified: outside road, marked portal and Objects catalogue appeared without a manual install or scene rebuild.");
                        EditorApplication.Exit(0);
                    }
                    return;
                }
                if(stage==2) return;
                if(stage==0)
                {
                    var airport=Object.FindAnyObjectByType<Airport>(); var camera=Camera.main;
                    if(airport==null || camera==null) return;
                    // This path must exercise the runtime bootstrap, never call Install or EnsureConnection here.
                    var access=airport.GetComponent<SandboxOutsideAccess>();
                    if(access==null) throw new Exception("Sandbox startup did not install its outside connection.");
                    if(Vector2.Distance(access.Position,SandboxOutsideAccess.DefaultPortal)>1f || !airport.Deicing.IsOutsideAccess(access.OutsideNodeId) ||
                        airport.Deicing.OutsideAccessProblem(access.OutsideNodeId)!=null || access.MarkerMesh==null || access.MarkerMesh.vertexCount==0)
                        throw new Exception("Sandbox bootstrap did not produce its paved, marked default outside gateway: "+access.Problem);
                    var junction=airport.Taxiways.NearestNode(new Vector3(SandboxOutsideAccess.DefaultJunction.x,0f,SandboxOutsideAccess.DefaultJunction.y),1f);
                    if(junction==null || airport.Taxiways.FindRoute(access.OutsideNodeId,junction.id,
                        e=>e.road==RoadClass.Service&&e.surface.IsPaved(),r=>false)==null)
                        throw new Exception("Sandbox startup road is disconnected from the existing service network.");
                    airport.timeScale=0f;
                    var at=airport.transform.TransformPoint(new Vector3(access.Position.x,0f,access.Position.y));
                    var orbit=camera.GetComponent<DevOrbitCamera>();
                    if(orbit!=null) { orbit.pivot=at+Vector3.forward*15f; orbit.distance=150f; orbit.enabled=false; }
                    camera.transform.position=at+new Vector3(50f,100f,-100f); camera.transform.LookAt(at+Vector3.forward*15f);
                    var tool=camera.GetComponent<DevAirfieldTool>(); if(tool==null) tool=camera.gameObject.AddComponent<DevAirfieldTool>(); tool.airport=airport;
                    var hud=camera.GetComponent<DevToolbar>(); if(hud==null) hud=camera.gameObject.AddComponent<DevToolbar>();
                    DevGui.Visible=true; hud.ShowMore=false; hud.OpenDeicingCatalog(2); tool.BeginDeicingOutsideAccess(); tool.enabled=false;
                    SessionState.SetInt(Stage,1); captureAt=EditorApplication.timeSinceStartup+1.5d; return;
                }
                if(EditorApplication.timeSinceStartup<captureAt) return;
                if(!File.Exists(PlayCapture)) { ScreenCapture.CaptureScreenshot(PlayCapture); captureAt=EditorApplication.timeSinceStartup+.75d; return; }
                SessionState.SetInt(Stage,2); EditorApplication.ExitPlaymode();
            }
            catch(Exception error) { FailPlayReview(error); }
        }
        static void FailPlayReview(Exception error)
        {
            SessionState.SetBool(Running,false); EditorApplication.update-=UpdatePlayReview; Debug.LogException(error); EditorApplication.Exit(1);
        }
        public static void FromCommandLine()
        {
            try
            {
                EditorSceneManager.OpenScene(SandboxOutsideAccess.ScenePath,OpenSceneMode.Single);
                var airport=Object.FindAnyObjectByType<Airport>(); var camera=Camera.main;
                if(airport==null || camera==null) throw new Exception("Sandbox airport/camera is missing.");
                var access=SandboxOutsideAccess.Install(airport);
                if(access==null || !access.EnsureConnection(out var problem)) throw new Exception(access?.Problem??"Sandbox access unavailable.");
                int roads=airport.Taxiways.edges.Count,nodes=airport.Taxiways.nodes.Count,aprons=airport.aprons.Count;
                if(!access.EnsureConnection(out problem) || roads!=airport.Taxiways.edges.Count || nodes!=airport.Taxiways.nodes.Count || aprons!=airport.aprons.Count)
                    throw new Exception("Sandbox outside access must be idempotent.");
                airport.timeScale=0f;
                foreach(var group in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude)) group.animateCrossFading=false;
                camera.farClipPlane=30000f; camera.orthographic=false; camera.fieldOfView=46f;
                camera.transform.position=airport.transform.TransformPoint(new Vector3(700f,1100f,-1250f));
                camera.transform.LookAt(airport.transform.TransformPoint(new Vector3(150f,0f,-180f)));
                SceneCapture.Capture(camera,"Captures/DeicingBuild/sandbox-outside-road.png",1800,1200);
                var at=airport.transform.TransformPoint(new Vector3(access.Position.x,0f,access.Position.y));
                camera.transform.position=at+new Vector3(45f,85f,-85f); camera.transform.LookAt(at+Vector3.forward*15f);
                SceneCapture.Capture(camera,"Captures/DeicingBuild/sandbox-outside-portal.png",1800,1200);
                Debug.Log($"Sandbox outside road verified: node {access.OutsideNodeId}, portal {access.Position}, {roads} graph edges; original scene left unsaved.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
