using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Dev;
using AirCity.Characters;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Vehicles
{
    public static class BaggageDepotFamilyReview
    {
        public const string ScenePath="Assets/AirCity/Scenes/BaggageDepotFamily.unity";
        const string Output="Captures/BaggageDepotFamily/";

        [MenuItem("AirCity/Vehicles/Create Baggage Depot Family Review")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildShowroom();
        }
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Output);
                foreach (BaggageDepotSize size in Enum.GetValues(typeof(BaggageDepotSize)))
                {
                    float baseline=ReviewHandling(size);
                    if (size==BaggageDepotSize.Medium)
                    {
                        float upgraded=ReviewHandling(size,true);
                        if (upgraded>=baseline) throw new Exception("Installed depot equipment did not improve measured handover time.");
                        Debug.Log($"DEPOT FAMILY equipment: depot handling {baseline:F2}s basic / {upgraded:F2}s upgraded.");
                    }
                }
                BuildShowroom();
                Debug.Log("DEPOT FAMILY passed: all three sizes completed unloading, collection, loading and crew recovery with every bag conserved.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void CaptureShowroom()
        {
            try { Directory.CreateDirectory(Output); BuildShowroom(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static float ReviewHandling(BaggageDepotSize size,bool upgraded=false)
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var depot=Object.FindAnyObjectByType<BaggageDepot>(); var airport=depot.Airport;
            var station=depot.Teams.Single();
            depot.size=size; station.parkAtStand=false; station.cartCount=size==BaggageDepotSize.Small ? 2 : 3;
            // The saved Small lot backs onto a taxilane. Larger halls need their own clear site beyond it.
            if (size!=BaggageDepotSize.Small) depot.firstBay+=depot.Facing*110;
            if (upgraded) depot.upgrades=BaggageDepotUpgrade.HandlingAssist|BaggageDepotUpgrade.PoweredBelts|BaggageDepotUpgrade.AutomaticSorting|BaggageDepotUpgrade.BufferStorage;
            depot.Rebuild(); airport.Taxiways.RefreshEquipmentPaint();
            station.incomingBags=72; station.outgoingBags=90;
            var plane=airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>().Single(a=>a.name=="Narrowbody baggage turnaround");
            airport.Simulate(.2f); var job=airport.Baggage.JobOf(plane);
            if (job?.Acquired!=true) throw new Exception(size+" planning: "+(job?.Waiting ?? station.Problem));
            var camera=Object.FindAnyObjectByType<Camera>(); bool captured=false; float depotSeconds=0;
            for (int i=0;i<14000 && !job.Finished;i++)
            {
                if (job.DepotTransfer) depotSeconds+=.25f;
                airport.Simulate(.25f);
                if (!captured && job.State==BaggageTurnaroundState.UnloadingArrivals && job.TransferSeconds>.4f)
                {
                    station.Present(.25f); depot.Present();
                    string suffix=upgraded ? "-operating-upgraded" : "";
                    depot.SetInterior(false); Aim(camera,depot,false); Shot(camera,Output+size.ToString().ToLowerInvariant()+suffix+"-exterior.png");
                    depot.SetInterior(true); Aim(camera,depot,true); Shot(camera,Output+size.ToString().ToLowerInvariant()+suffix+"-interior.png");
                    captured=true;
                }
                if (i%2000==0) Debug.Log($"DEPOT FAMILY {size} {i*.25f:F0}s: {job.State}: {job.Waiting}");
            }
            if (!job.DepartureReady || !captured || job.DeliveredArrivals!=72 || job.Hold.Count!=90 ||
                airport.Baggage.Ledger.Count!=162 || depot.Working!=0 || depot.BusyBays!=0)
                throw new Exception($"{size} handling: {job.State}: {job.Waiting}; captured={captured}; crew={depot.Working}; bays={depot.BusyBays}");
            Debug.Log($"DEPOT FAMILY {size}: 72 incoming / 90 outgoing bags conserved, {station.cartCount} carts, crew and bay released.");
            return depotSeconds;
        }
        static void Assign(BaggageStation station)
        {
            station.tractorType=VehicleSetup.Type("BaggageTractor"); station.cartType=VehicleSetup.Type("BaggageCart"); station.loaderType=VehicleSetup.Type("BeltLoader");
            station.tractorPrefab=VehicleSetup.Prefab("BaggageTractor"); station.cartPrefab=VehicleSetup.Prefab("BaggageCart"); station.loaderPrefab=VehicleSetup.Prefab("BeltLoader");
            station.bagPrefabs=VehicleSetup.BagPrefabs(); station.bagPropType=VehicleSetup.Type("BaggageProps");
            AirCity.EditorTools.Characters.RampCrewSetup.Assign(station);
            station.parkAtStand=false; station.cartCount=2;
        }
        static void BuildShowroom()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.66f,.73f,.83f);
            RenderSettings.ambientEquatorColor=new Color(.49f,.54f,.60f); RenderSettings.ambientGroundColor=new Color(.27f,.29f,.31f);
            var sun=new GameObject("Daylight").AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=2;
            sun.shadows=LightShadows.Soft; sun.transform.rotation=Quaternion.Euler(45,-35,0); RenderSettings.sun=sun;
            var root=new GameObject("Depot family review"); root.SetActive(false);
            var airport=root.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport); airport.groundSize=1000;
            airport.aprons.Add(ApronArea.Rect(Vector2.zero,new Vector2(650,300),0,PavementSurface.Concrete)); root.SetActive(true);
            var camera=new GameObject("Review camera").AddComponent<Camera>(); camera.tag="MainCamera";
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.62f,.69f,.76f);
            int i=0;
            foreach (BaggageDepotSize size in Enum.GetValues(typeof(BaggageDepotSize)))
            {
                var net=airport.Taxiways; float x=-220+i++*170;
                int a=net.AddNode(new Vector3(x,0,-100)),b=net.AddNode(new Vector3(x,0,80));
                net.Connect(a,b,12,PavementSurface.Concrete,false,RoadClass.Service);
                var go=new GameObject(size+" baggage depot"); go.transform.SetParent(root.transform,false);
                var depot=go.AddComponent<BaggageDepot>(); depot.size=size; depot.firstBay=new Vector2(x,0); depot.hiredWorkers=4;
                if (size!=BaggageDepotSize.Small) depot.upgrades=BaggageDepotUpgrade.HandlingAssist|BaggageDepotUpgrade.PoweredBelts|BaggageDepotUpgrade.AutomaticSorting|BaggageDepotUpgrade.BufferStorage;
                var teamGo=new GameObject(size+" team"); teamGo.SetActive(false); teamGo.transform.SetParent(root.transform,false);
                var team=teamGo.AddComponent<BaggageStation>(); Assign(team); team.depot=depot;
                depot.ConnectBay(team); teamGo.SetActive(true); depot.Rebuild();
                var fleet=airport.Baggage.Fleet(team);
                airport.Baggage.Ledger.Add(size+"-arrival","IN",24,360,true,fleet.Arrivals);
                airport.Baggage.Ledger.Add(size+"-departure","OUT",24,360,true,fleet.Dispatch);
                team.Present(.1f); depot.Present();
            }
            airport.BuildGround(); airport.Taxiways.Rebuild(); airport.Taxiways.RefreshEquipmentPaint();
            foreach (var depot in airport.GetComponentsInChildren<BaggageDepot>())
            {
                if (!Application.isBatchMode) continue;
                string name=depot.size.ToString().ToLowerInvariant();
                depot.SetInterior(false); Aim(camera,depot,false); Shot(camera,Output+name+"-upgraded.png");
                depot.SetInterior(true); Aim(camera,depot,true); Shot(camera,Output+name+"-upgraded-interior.png");
                depot.SetInterior(false);
            }
            var orbit=camera.gameObject.AddComponent<AirCity.Dev.DevOrbitCamera>(); orbit.pivot=new Vector3(0,0,-15);
            orbit.distance=480; orbit.pitch=55; orbit.yaw=210;
            camera.transform.rotation=Quaternion.Euler(orbit.pitch,orbit.yaw,0);
            camera.transform.position=orbit.pivot-camera.transform.forward*orbit.distance;
            EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
        }
        static void Aim(Camera camera,BaggageDepot depot,bool interior)
        {
            var layout=depot.Layout; float width=layout.Right-layout.Left,depth=layout.WallTop-layout.WallBottom;
            var p=depot.Point((layout.Left+layout.Right)*.5f,(layout.WallBottom+layout.WallTop)*.5f);
            var target=depot.Airport.transform.TransformPoint(new Vector3(p.x,1,p.y));
            float distance=Mathf.Max(40,Mathf.Max(width,depth)*1.15f);
            var offset=interior ? new Vector3(-distance*.55f,distance*.95f,-distance*.65f) : new Vector3(-distance*.8f,distance*.65f,-distance*.9f);
            camera.orthographic=false; camera.fieldOfView=48; camera.nearClipPlane=.05f; camera.farClipPlane=5000;
            camera.transform.position=target+depot.Airport.transform.TransformDirection(offset); camera.transform.LookAt(target);
        }
        static void Shot(Camera camera,string path)
            => CaptureShot(camera,path);
        internal static void CaptureShot(Camera camera,string path)
        {
            var skins=new List<SkinnedMeshRenderer>(); var copies=new List<GameObject>(); var meshes=new List<Mesh>();
            foreach (var rig in Object.FindObjectsByType<Airport>(FindObjectsSortMode.None).SelectMany(a=>a.GetComponentsInChildren<RampCrewRig>()))
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!skin.enabled) continue;
                    if (skin.name.EndsWith("LOD0"))
                    {
                        var mesh=new Mesh(); skin.BakeMesh(mesh); meshes.Add(mesh);
                        var copy=new GameObject("Crew capture"); copy.transform.SetParent(skin.transform,false);
                        copy.AddComponent<MeshFilter>().sharedMesh=mesh; copy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials; copies.Add(copy);
                    }
                    skins.Add(skin); skin.enabled=false;
                }
            try { SceneCapture.Capture(camera,path,1800,1200); }
            finally
            {
                foreach (var copy in copies) Object.DestroyImmediate(copy);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                foreach (var skin in skins) if (skin!=null) skin.enabled=true;
            }
        }
    }
}
