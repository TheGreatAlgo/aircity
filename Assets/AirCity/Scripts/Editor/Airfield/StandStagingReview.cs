using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Characters;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>Native captures of the actual fleets at their stand homes and cargo working positions.</summary>
    public static class StandStagingReview
    {
        public static void FromCommandLine()
        {
            try
            {
                Sandbox(); Working(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Present(Airport airport)
        {
            foreach (var s in airport.GetComponentsInChildren<BaggageStation>()) s.Present(.25f);
            foreach (var s in airport.GetComponentsInChildren<ContainerStation>()) s.Present(.25f);
        }

        static void Sandbox()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var airport=Object.FindAnyObjectByType<Airport>(); var camera=Object.FindAnyObjectByType<Camera>();
            var parking=StandEquipmentLayout.All(airport);
            if (parking.Count==0 || parking.Any(p=>!p.Valid)) throw new Exception(string.Join("; ",parking.Select(p=>p.problem)));
            foreach (var group in parking.GroupBy(p=>p.stand))
            {
                var bulk=group.Select(p=>p.station).OfType<BaggageStation>().FirstOrDefault();
                var container=group.Select(p=>p.station).OfType<ContainerStation>().FirstOrDefault();
                var type=container != null ? container.automaticHold.aircraft : bulk.automaticHold.aircraft;
                var layout=airport.Taxiways.Layout(group.Key); var at=layout.ParkPosition(type);
                foreach (var existing in AirCity.Aircraft.Aircraft.All.ToArray())
                {
                    var p=airport.transform.InverseTransformPoint(existing.transform.position);
                    if (Vector2.Distance(new Vector2(p.x,p.z),at)<10) existing.gameObject.SetActive(false);
                }
                var path=AssetDatabase.GetAssetPath(type).Replace(".aircraft",".prefab");
                var plane=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                plane.transform.SetParent(airport.transform,false);
                plane.transform.localPosition=new Vector3(at.x,0,at.y);
                plane.transform.localRotation=Quaternion.LookRotation(new Vector3(layout.nose.x,0,layout.nose.y));
            }
            airport.Taxiways.Rebuild(); Present(airport);
            var bays=StandMarkings.Equipment(airport);
            Debug.Log($"Stand staging review: {bays.Count(b=>b.storageArea)} shared equipment courts, {bays.Count(b=>b.working)} cargo working areas.");
            foreach (var area in bays.Where(b=>b.storageArea)) Debug.Log($"Stand {area.stands} equipment court: {area.width:F1} m x {area.length:F1} m.");
            foreach (float yaw in new[] { 0f,180f })
            {
                airport.transform.rotation=Quaternion.Euler(0,yaw,0); Present(airport);
                foreach (var group in parking.GroupBy(p=>p.stand))
                {
                    var layout=airport.Taxiways.Layout(group.Key);
                    Shot(camera,airport,layout.Centre,Mathf.Max((layout.far-Mathf.Min(layout.near0,layout.near1))*.57f,
                        (layout.x1-layout.x0)*.4f),$"stand-{group.Key.Label}-{yaw:0}");
                    var storage=bays.Where(b=>b.storageArea && b.stands==group.Key.Label).ToArray();
                    if (storage.Length==0) throw new Exception("No shared equipment court for stand "+group.Key.Label);
                    var bounds=new Bounds(new Vector3(storage[0].centre.x,0,storage[0].centre.y),Vector3.zero);
                    foreach (var b in storage)
                        foreach (float x in new[] { -b.width*.5f,b.width*.5f })
                            foreach (float z in new[] { -b.length*.5f,b.length*.5f })
                            { var q=b.Point(x,z); bounds.Encapsulate(new Vector3(q.x,0,q.y)); }
                    Shot(camera,airport,new Vector2(bounds.center.x,bounds.center.z),
                        Mathf.Max(bounds.size.z*.6f,bounds.size.x*.4f)+3,$"equipment-{group.Key.Label}-{yaw:0}");
                }
            }
        }

        static void Working()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo=Object.FindAnyObjectByType<BaggageTurnaroundDemo>(); demo.startWithArrival=false; demo.autoDeparture=false;
            demo.Initialize();
            for (int i=0;i<14000 && demo.Job.State!=BaggageTurnaroundState.LoadingHold;i++)
            { demo.airport.Simulate(.25f); demo.Observe(); }
            if (demo.Job.State!=BaggageTurnaroundState.LoadingHold) throw new Exception(demo.Job.Waiting);
            demo.station.Present(.25f);
            var layout=demo.airport.Taxiways.Layout(demo.Job.Stand);
            var camera=Object.FindAnyObjectByType<Camera>();
            Shot(camera,demo.airport,layout.Centre,(layout.far-Mathf.Min(layout.near0,layout.near1))*.58f,"baggage-working");
            var train=demo.Job.Fleet.Train;
            Shot(camera,demo.airport,(train.Tractor.position+train.CartAt(train.CartCount-1).position)*.5f,13,"baggage-transfer");
        }

        static void Shot(Camera camera,Airport airport,Vector2 at,float size,string name)
        {
            // CPU snapshots keep animated crew correct across captures rendered in the same editor frame.
            var hidden=new List<(SkinnedMeshRenderer skin,bool enabled)>(); var snapshots=new List<GameObject>(); var meshes=new List<Mesh>();
            foreach (var rig in airport.GetComponentsInChildren<RampCrewRig>(true))
                foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    hidden.Add((skin,skin.enabled));
                    if (skin.name.EndsWith("LOD0"))
                    {
                        var mesh=new Mesh(); skin.BakeMesh(mesh); meshes.Add(mesh);
                        var go=new GameObject("Capture skin snapshot"); go.transform.SetParent(skin.transform,false);
                        go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                        snapshots.Add(go);
                    }
                    skin.enabled=false;
                }
            camera.orthographic=true; camera.orthographicSize=size; camera.nearClipPlane=.05f; camera.farClipPlane=1500;
            camera.transform.position=airport.transform.TransformPoint(new Vector3(at.x,180,at.y));
            camera.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
            try { SceneCapture.Capture(camera,"Captures/StandStaging/"+name+".png",1800,1200); }
            finally
            {
                foreach (var go in snapshots) Object.DestroyImmediate(go);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                foreach (var (skin,enabled) in hidden) skin.enabled=enabled;
            }
        }
    }
}
