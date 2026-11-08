using System;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Vehicles
{
    public static class TerminalBaggageReview
    {
        public const string ScenePath="Assets/AirCity/Scenes/TerminalBaggage.unity";
        const string Captures="Captures/TerminalBaggage/";
        [MenuItem("AirCity/Vehicles/Create Terminal Baggage Review")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Covered(false);
        }
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Captures);
                EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
                var depot=Object.FindAnyObjectByType<BaggageDepot>(); depot.Rebuild();
                if (depot.ConnectedTerminal==null) throw new Exception("The saved Sandbox needs a terminal-connected baggage hall.");
                Review(depot,"underground",120,180);
                Covered(true);
                Debug.Log("TERMINAL BAGGAGE passed: check-in → timed link → hall → aircraft, and arriving baggage → hall → terminal reclaim; all bags conserved in both connection modes.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Covered(bool simulate)
        {
            var scene=EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var depot=Object.FindAnyObjectByType<BaggageDepot>(); var airport=depot.Airport; depot.Rebuild();
            var port=depot.Point(depot.Layout.Right,depot.Layout.WorkZ-6)+depot.Right*40;
            var terminal=airport.AddTerminal(new[] { port+new Vector2(-10,-16),port+new Vector2(10,-16),
                port+new Vector2(10,16),port+new Vector2(-10,16) },3);
            if (terminal==null) throw new Exception("The terminal review site is occupied.");
            terminal.exterior.facade.cladding=new Color(.78f,.81f,.8f);
            terminal.exterior.roof=new Color(.27f,.33f,.36f); airport.BuildGround();
            if (!airport.Baggage.ConnectTerminal(depot,terminal,BaggageConnectionKind.Covered,out var why)) throw new Exception(why);
            depot.ShowConnection(true); depot.SetInterior(false);
            var team=depot.Teams.Single(); team.incomingBags=72; team.outgoingBags=90;
            var camera=Object.FindAnyObjectByType<Camera>(); Aim(camera,depot,false);
            var orbit=camera.GetComponent<DevOrbitCamera>();
            if (orbit!=null)
            {
                var at=depot.Point(30,-3); orbit.pivot=airport.transform.TransformPoint(new Vector3(at.x,0,at.y));
                orbit.distance=140; orbit.pitch=45; orbit.yaw=depot.bearing-140;
            }
            EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
            if (simulate) Review(depot,"covered",72,90);
        }
        static void Review(BaggageDepot depot,string mode,int incoming,int outgoing)
        {
            var airport=depot.Airport; var team=depot.Teams.Single(); team.incomingBags=incoming; team.outgoingBags=outgoing;
            var plane=airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>().Single(a=>a.name=="Narrowbody baggage turnaround");
            var camera=Object.FindAnyObjectByType<Camera>();
            airport.Simulate(.05f); var job=airport.Baggage.JobOf(plane); var link=airport.Baggage.TerminalLink(depot);
            if (job==null || link==null || job.DepartureSource!=link.Facility.CheckIn || job.Fleet.Dispatch.Count!=0 || link.InTransit==0)
                throw new Exception(mode+": departing bags must begin at terminal check-in and travel before reaching the hall.");
            bool handled=false,observedQueue=link.QueuedDepartures>0; float elapsed=.05f;
            for (int i=0;i<20000 && (!job.Finished || link.Pending);i++)
            {
                airport.Simulate(.25f); elapsed+=.25f;
                observedQueue|=link.QueuedDepartures>0;
                if (airport.Baggage.Ledger.Count!=incoming+outgoing) throw new Exception("Baggage ownership changed the total count.");
                if (link.Outbound.Count>link.Capacity || link.Inbound.Count>link.Capacity) throw new Exception("A conveyor exceeded its capacity.");
                if (!handled && job.DepotTransfer)
                {
                    team.Present(.25f); depot.Present(); depot.ShowConnection(true); Aim(camera,depot,false);
                    BaggageDepotFamilyReview.CaptureShot(camera,Captures+mode+"-handling.png"); handled=true;
                }
                if (i%2000==0) Debug.Log($"TERMINAL BAGGAGE {mode} {elapsed:F0}s: {job.State}; {job.Waiting}; queue {link.QueuedDepartures}; transit {link.InTransit}; reclaimed {link.Facility.Delivered}");
            }
            if (!job.DepartureReady || !handled || !observedQueue || job.DeliveredArrivals!=incoming || job.Hold.Count!=outgoing ||
                link.Facility.Delivered!=incoming || link.InTransit!=0 || depot.Working!=0 || depot.BusyBays!=0)
                throw new Exception($"{mode}: {job.State}: {job.Waiting}; reclaimed {link.Facility.Delivered}; transit {link.InTransit}; handled {handled}");
            team.Present(.1f); depot.Present(); Aim(camera,depot,true);
            BaggageDepotFamilyReview.CaptureShot(camera,Captures+mode+"-connection.png");
            Debug.Log($"TERMINAL BAGGAGE {mode} passed in {elapsed:F1}s: {incoming} incoming reclaimed / {outgoing} outgoing loaded; {incoming+outgoing} bags conserved.");
        }
        static void Aim(Camera camera,BaggageDepot depot,bool connection)
        {
            var p=depot.Point(connection ? 29 : 13,-3);
            var target=depot.Airport.transform.TransformPoint(new Vector3(p.x,2,p.y));
            float distance=connection ? 70 : 48;
            camera.orthographic=false; camera.fieldOfView=48; camera.nearClipPlane=.05f; camera.farClipPlane=5000;
            camera.transform.position=target+new Vector3(-distance*.65f,distance*.7f,-distance*.9f); camera.transform.LookAt(target);
        }
    }
}
