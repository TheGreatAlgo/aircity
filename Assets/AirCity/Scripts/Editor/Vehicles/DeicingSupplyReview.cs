using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using AirCity.EditorTools.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>Capture real tanker travel, hose handling, transfer, departure and the purchased recovery skid.</summary>
    public static class DeicingSupplyReview
    {
        const string CaptureRoot = "Captures/DeicingSupply/";
        const string AnimationRoot = CaptureRoot + "Animation/";
        const float FrameSeconds = .25f;
        const int MaximumFrames = 600;
        public const string ScenePath = "Assets/AirCity/Scenes/DeicingSupplyReview.unity";
        public static void RunFromCommandLine()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
        public static void Build()
        {
            DeicingReview.Build(false);
            var airport = UnityEngine.Object.FindAnyObjectByType<AirCity.Airfield.Airport>();
            var service = airport.Deicing;
            var area = service.WorkAreas[0];
            var rig = airport.GetComponent<DeicingRig>();
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            var camera = Camera.main;
            if(assets==null || assets.tankerPrefab==null || assets.recyclerPrefab==null)
                throw new InvalidOperationException("Generate tanker and recycling art before the supply review.");
            var reviewAircraft=UnityEngine.Object.FindObjectsByType<AirCity.Aircraft.Aircraft>(FindObjectsInactive.Exclude);
            foreach(var aircraft in reviewAircraft) aircraft.gameObject.SetActive(false);
            for(int waiting=0;waiting<6000 && service.Deliveries.Any(d=>d.Area==area && d.Phase!=DeicingDeliveryPhase.Complete);waiting++)
            { airport.Simulate(.1f); rig.Refresh(); }
            if(service.Deliveries.Any(d=>d.Area==area && d.Phase!=DeicingDeliveryPhase.Complete))
                throw new InvalidOperationException("Initial supplier tanker did not clear the work area.");
            if(!service.BuyRecycling(area,out var problem)) throw new InvalidOperationException(problem);
            foreach(var aircraft in reviewAircraft) aircraft.gameObject.SetActive(true);
            const float ordered = 2500f;
            var previousDeliveries=new HashSet<DeicingDelivery>(service.Deliveries);
            if(!service.OrderFluid(area,ordered,out problem)) throw new InvalidOperationException(problem);
            rig.Refresh();
            if(rig.RecyclerCount!=1) throw new InvalidOperationException("Purchased recycling skid is missing.");
            float stockBefore=Stored(service,area);
            double appliedBefore=service.Accounts.appliedLitres,recoveredBefore=service.Accounts.recoveredLitres;
            Directory.CreateDirectory(CaptureRoot); Directory.CreateDirectory(AnimationRoot);
            foreach(var oldFrame in Directory.GetFiles(AnimationRoot,"frame-*.png")) File.Delete(oldFrame);
            var movie=new SupplyMovie(); float movieStart=-1f,nextFrame=0f;
            Overview(camera,area.position);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
            AssetDatabase.SaveAssets();
            Shot(camera,"recycling-upgrade",Ground(area.RecyclerPosition)+new Vector3(7,5,8),Ground(area.RecyclerPosition)+Vector3.up);
            bool inbound=false,connecting=false,unloading=false,disconnecting=false,outbound=false,complete=false;
            DeicingDelivery tracked=null;
            for(int frame=0;frame<18000;frame++)
            {
                airport.Simulate(.05f); rig.Refresh();
                tracked ??= service.Deliveries.FirstOrDefault(d=>d.Area==area && !previousDeliveries.Contains(d) && d.Phase!=DeicingDeliveryPhase.Complete);
                if(tracked==null) continue;
                if(tracked.Drive!=null && Mathf.Abs(tracked.CargoLitres+tracked.DeliveredLitres-ordered)>.1f)
                    throw new InvalidOperationException("Tanker cargo changed outside the delivery transfer ledger.");
                if(!inbound && tracked.Phase==DeicingDeliveryPhase.Inbound && tracked.Drive!=null && tracked.Drive.Along>6f)
                {
                    inbound=true;
                    if(rig.TankerCount<1 || rig.DeliveryHoseCount!=0) throw new InvalidOperationException("Inbound tanker art or retracted hose is incorrect.");
                    VehicleShot(camera,airport.transform,tracked,"tanker-inbound");
                }
                if(!connecting && tracked.Phase==DeicingDeliveryPhase.Connecting && tracked.HoseProgress>.35f && tracked.HoseProgress<.85f)
                {
                    connecting=true; HoseShot(camera,rig,assets,area,"hose-paying-out");
                    if(rig.DeliveryHoseCount<1) throw new InvalidOperationException("Connecting tanker has no paying-out hose.");
                }
                if(!unloading && tracked.Phase==DeicingDeliveryPhase.Unloading && tracked.DeliveredLitres>500f)
                {
                    unloading=true;
                    if(!tracked.HoseConnected || rig.DeliveryHoseCount<1) throw new InvalidOperationException("Fluid transfer began without a visible connected hose.");
                    Overview(camera,area.position); SceneCapture.Capture(camera,CaptureRoot+"tanker-unloading.png",1800,1200);
                    HoseShot(camera,rig,assets,area,"hose-connected");
                    float progress=tracked.HoseProgress,cargo=tracked.CargoLitres,stock=area.fluidLitres,clock=service.Clock;
                    var pose=tracked.Drive.Pose;
                    for(int paused=0;paused<4;paused++) rig.Refresh();
                    if(progress!=tracked.HoseProgress || cargo!=tracked.CargoLitres || stock!=area.fluidLitres || clock!=service.Clock || pose.position!=tracked.Drive.Position)
                        throw new InvalidOperationException("Rendering or a paused frame advanced the tanker simulation.");
                }
                if(!disconnecting && tracked.Phase==DeicingDeliveryPhase.Disconnecting && tracked.HoseProgress>.35f && tracked.HoseProgress<.85f)
                { disconnecting=true; HoseShot(camera,rig,assets,area,"hose-retracting"); }
                if(!outbound && tracked.Phase==DeicingDeliveryPhase.Outbound && tracked.Drive!=null && tracked.Drive.Along>4f)
                {
                    outbound=true;
                    if(rig.DeliveryHoseCount!=0) throw new InvalidOperationException("The tanker departed with its hose still attached.");
                    VehicleShot(camera,airport.transform,tracked,"tanker-departing");
                }
                if(tracked.Drive!=null)
                {
                    if(movieStart<0f) movieStart=service.Clock;
                    float elapsed=service.Clock-movieStart;
                    if(elapsed+.001f>=nextFrame)
                    {
                        if(movie.frames.Count<MaximumFrames)
                        {
                            bool close=tracked.Phase==DeicingDeliveryPhase.Connecting || tracked.Phase==DeicingDeliveryPhase.Disconnecting ||
                                tracked.Phase==DeicingDeliveryPhase.Unloading && tracked.DeliveredLitres<ordered*.25f || tracked.HoseProgress>.001f && tracked.Phase==DeicingDeliveryPhase.Waiting;
                            if(close) HoseView(camera,rig,assets,area);
                            else if(tracked.Phase==DeicingDeliveryPhase.Inbound || tracked.Phase==DeicingDeliveryPhase.Outbound) VehicleView(camera,airport.transform,tracked);
                            else Overview(camera,area.position);
                            int number=movie.frames.Count;
                            SceneCapture.Capture(camera,$"{AnimationRoot}frame-{number:000}.png",960,640);
                            movie.frames.Add(new SupplyFrame { frame=number,gameSeconds=elapsed,phase=tracked.Phase.ToString(),position=tracked.Drive.Position,
                                hoseProgress=tracked.HoseProgress,cargoLitres=tracked.CargoLitres,deliveredLitres=tracked.DeliveredLitres });
                        }
                        else movie.capped=true;
                        nextFrame+=FrameSeconds;
                    }
                }
                if(tracked.Phase!=DeicingDeliveryPhase.Complete) continue;
                complete=true; rig.Refresh(); break;
            }
            movie.deliveryGameSeconds=movieStart>=0f?service.Clock-movieStart:0f;
            if(!complete || !inbound || !connecting || !unloading || !disconnecting || !outbound)
                throw new InvalidOperationException($"Supply review missed a delivery phase: {tracked?.Phase}; {tracked?.Waiting}");
            if(rig.TankerCount!=0 || rig.DeliveryHoseCount!=0) throw new InvalidOperationException("Completed delivery left runtime tanker or hose art behind.");
            if(Mathf.Abs(tracked.DeliveredLitres-ordered)>.1f) throw new InvalidOperationException("Tanker did not unload the ordered amount.");
            for(int filter=0;filter<900 && service.Accounts.recoveredLitres<=recoveredBefore;filter++)
            { airport.Simulate(.1f); rig.Refresh(); }
            double expected=stockBefore+ordered-(service.Accounts.appliedLitres-appliedBefore)+(service.Accounts.recoveredLitres-recoveredBefore);
            if(Math.Abs(Stored(service,area)-expected)>.3d) throw new InvalidOperationException("Combined delivery, deicing and recycling stock did not balance.");
            if(service.Accounts.recoveredLitres<=recoveredBefore) throw new InvalidOperationException("Installed recovery equipment did not return any runoff.");
            Overview(camera,area.position); SceneCapture.Capture(camera,CaptureRoot+"supply-complete.png",1800,1200);
            movie.elapsedGameSeconds=movie.frames.Count>0?movie.frames[movie.frames.Count-1].gameSeconds:0f;
            File.WriteAllText(AnimationRoot+"report.json",JsonUtility.ToJson(movie,true));
            Debug.Log($"Supply animation: {movie.frames.Count} frames at {FrameSeconds}s game intervals, {movie.elapsedGameSeconds:0.0}s elapsed, capped={movie.capped}.");
            Debug.Log($"Deicing supply review passed: physical tanker inbound, animated payout, {tracked.DeliveredLitres:0} L unload, hose retraction and departure; recycling returned {service.Accounts.recoveredLitres-recoveredBefore:0.0} L; render callbacks preserved simulation state.");
        }
        [Serializable] sealed class SupplyMovie
        {
            public float frameGameSeconds=FrameSeconds,elapsedGameSeconds,deliveryGameSeconds;
            public bool capped;
            public List<SupplyFrame> frames=new List<SupplyFrame>();
        }
        [Serializable] sealed class SupplyFrame
        {
            public int frame;
            public float gameSeconds,hoseProgress,cargoLitres,deliveredLitres;
            public string phase;
            public Vector2 position;
        }
        static float Stored(AirportDeicing service,DeicingWorkArea area) => area.fluidLitres+
            service.Facilities.Where(f=>service.WorkAreaFor(f)==area).Sum(f=>service.TrucksFor(f).Sum(t=>t.FluidLitres));
        static void Overview(Camera camera,Vector2 centre)
        { camera.transform.position=Ground(centre)+new Vector3(25,30,34); camera.transform.LookAt(Ground(centre)+new Vector3(-5,1,0)); }
        static void VehicleShot(Camera camera,Transform airport,DeicingDelivery delivery,string name)
        {
            VehicleView(camera,airport,delivery); SceneCapture.Capture(camera,CaptureRoot+name+".png",1800,1200);
        }
        static void VehicleView(Camera camera,Transform airport,DeicingDelivery delivery)
        {
            var p=airport.TransformPoint(Ground(delivery.Drive.Position));
            var forward=airport.TransformDirection(Ground(delivery.Drive.Forward));
            var right=Vector3.Cross(airport.up,forward);
            camera.transform.position=p+forward*16f+right*14f+airport.up*9f; camera.transform.LookAt(p+forward*2.5f+airport.up*1.8f);
        }
        static void HoseShot(Camera camera,DeicingRig rig,DeicingAssets assets,DeicingWorkArea area,string name)
        {
            HoseView(camera,rig,assets,area); SceneCapture.Capture(camera,CaptureRoot+name+".png",1800,1200);
        }
        static void HoseView(Camera camera,DeicingRig rig,DeicingAssets assets,DeicingWorkArea area)
        {
            var tanker=rig.transform.Find("Deicing equipment (runtime)/Glycol delivery tanker");
            var tank=rig.transform.Find($"Deicing equipment (runtime)/Deicing work area {area.id} fluid storage");
            if(tanker==null || tank==null) throw new InvalidOperationException("Hose review could not find the generated delivery equipment.");
            var outlet=tanker.TransformPoint(assets.tankerHosePoint); var inlet=tank.TransformPoint(assets.tankDeliveryPoint);
            var middle=(outlet+inlet)*.5f;
            var along=rig.transform.TransformDirection(Ground(area.Axis));
            camera.transform.position=middle-along*9f+rig.transform.up*6f; camera.transform.LookAt(middle);
        }
        static void Shot(Camera camera,string name,Vector3 position,Vector3 target)
        { camera.transform.position=position; camera.transform.LookAt(target); SceneCapture.Capture(camera,CaptureRoot+name+".png",1800,1200); }
        static Vector3 Ground(Vector2 p) => new Vector3(p.x,0f,p.y);
    }
}
