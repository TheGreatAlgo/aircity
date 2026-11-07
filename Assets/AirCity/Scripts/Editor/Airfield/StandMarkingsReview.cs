using System;
using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Airfield
{
    public static class StandMarkingsReview
    {
        [MenuItem("AirCity/Review/Stand markings")]
        public static void Install()
        {
            foreach (var name in new[] { "Sandbox","BaggageTurnaround","ContainerTurnaround" })
            {
                var scene = EditorSceneManager.OpenScene($"Assets/AirCity/Scenes/{name}.unity");
                var airport = Object.FindAnyObjectByType<Airport>();
                bool changed = !System.IO.File.ReadAllText(scene.path).Contains("parkAtStand:");
                var bulk = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
                if (bulk != null && bulk.station.automaticHold.aircraft==null)
                { bulk.station.automaticHold.aircraft=bulk.aircraft.type; EditorUtility.SetDirty(bulk.station); changed=true; }
                if (bulk != null && bulk.station.standIds.Count == 0)
                { bulk.station.standIds.Add(bulk.standId); EditorUtility.SetDirty(bulk.station); changed = true; }
                var containers = Object.FindAnyObjectByType<ContainerTurnaroundDemo>();
                if (containers != null)
                {
                    if (containers.station.automaticHold.aircraft==null)
                    { containers.station.automaticHold.aircraft=containers.aircraft.type; EditorUtility.SetDirty(containers.station); changed=true; }
                    if (containers.bulkStation != null && containers.bulkStation.automaticHold.aircraft==null)
                    {
                        containers.bulkStation.automaticHold.aircraft=containers.aircraft.type;
                        containers.bulkStation.automaticHold.cargoPoint="Cargo_Bulk";
                        EditorUtility.SetDirty(containers.bulkStation); changed=true;
                    }
                    if (containers.station.standIds.Count == 0)
                    { containers.station.standIds.Add(containers.standId); EditorUtility.SetDirty(containers.station); changed = true; }
                    if (containers.bulkStation != null && containers.bulkStation.standIds.Count == 0)
                    { containers.bulkStation.standIds.Add(containers.standId); EditorUtility.SetDirty(containers.bulkStation); changed = true; }
                }
                // Upgrade only aircraft exactly on the former shared stop; preserve manually positioned aircraft.
                foreach (var plane in airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>())
                {
                    if (plane.type == null) continue;
                    var p = airport.transform.InverseTransformPoint(plane.transform.position);
                    var f = airport.transform.InverseTransformDirection(plane.transform.forward);
                    foreach (var layout in airport.Taxiways.Layouts())
                    {
                        if (layout.stand.DeIcing || layout.stand.ServiceFor(plane.type) != null ||
                            layout.StopOffsetFor(plane.type) < .01f || !layout.ParkingFits(plane.type)) continue;
                        var old = layout.stop-layout.nose*plane.type.Wheelbase;
                        if (Vector2.Distance(old,new Vector2(p.x,p.z)) > .05f ||
                            Vector2.Dot(layout.nose,new Vector2(f.x,f.z)) < .999f) continue;
                        var next = layout.ParkPosition(plane.type);
                        plane.transform.position = airport.transform.TransformPoint(new Vector3(next.x,p.y,next.y));
                        EditorUtility.SetDirty(plane.transform); changed = true; break;
                    }
                }
                if (changed) EditorSceneManager.SaveScene(scene);
            }
        }

        public static void InstallFromCommandLine()
        {
            try { Install(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void FromCommandLine()
        {
            try
            {
                Install();
                EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
                var airport = Object.FindAnyObjectByType<Airport>(); var net = airport.Taxiways;
                net.Rebuild();
                var camera = Object.FindAnyObjectByType<Camera>();
                var stand = net.stands.First(s=>s.Code == 'F' && !s.DeIcing && s.exit == StandExit.PushBack);
                var layout = net.Layout(stand);
                var centre = layout.StopFor(airport.aircraftTypes.First(t=>t.model == "Narrowbody"));
                Capture(camera,airport,centre,layout.nose,23,"aircraft-stops");
                Capture(camera,airport,layout.Centre,layout.nose,(layout.far-Mathf.Min(layout.near0,layout.near1))*.6f,"stand-overview");
                var bays = StandMarkings.Equipment(airport);
                foreach (var group in bays.GroupBy(b=>b.stands))
                {
                    var bounds = new Bounds(new Vector3(group.First().centre.x,0,group.First().centre.y),Vector3.zero);
                    foreach (var bay in group)
                        foreach (float x in new[] { -bay.width*.5f,bay.width*.5f })
                            foreach (float z in new[] { -bay.length*.5f,bay.length*.5f })
                            { var p = bay.Point(x,z); bounds.Encapsulate(new Vector3(p.x,0,p.y)); }
                    Capture(camera,airport,new Vector2(bounds.center.x,bounds.center.z),Vector2.up,
                        Mathf.Max(bounds.size.z*.6f,bounds.size.x*.4f)+4,"equipment-stand-"+group.Key);
                }
                foreach (var bay in bays.Where(b=>b.equipment == "ULD LOADER"))
                    Capture(camera,airport,bay.centre,bay.facing,Mathf.Max(bay.length*.6f,bay.width*.45f)+1,
                        "uld-loader-stand-"+bay.stands);
                EmptyStand(camera,airport);
                Debug.Log($"Stand markings review: {bays.Count(b=>b.storageArea)} shared equipment courts, {bays.Count(b=>b.working)} cargo working areas; {StandMarkings.Stops(layout,airport.aircraftTypes).Count} aircraft stop groups.");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void EmptyStand(Camera camera,Airport source)
        {
            var go = new GameObject("Empty stand marking review"); go.SetActive(false);
            var airport = go.AddComponent<Airport>(); airport.groundSize = 0;
            AirfieldSetup.AssignMaterials(airport); airport.aircraftTypes = source.aircraftTypes.ToList();
            go.transform.position = new Vector3(8000,0,8000); go.SetActive(true);
            var stand = new Stand { code = SizeCode.F, noSmallAircraft = true, number = 1 };
            stand.Frame(Vector2.right,0); airport.Taxiways.Add(stand); airport.Taxiways.Rebuild();
            var layout = airport.Taxiways.Layout(stand);
            var marks = StandMarkings.Stops(layout,airport.aircraftTypes);
            Capture(camera,airport,(marks.First().position+marks.Last().position)*.5f,layout.nose,13,"aircraft-stops-empty");
            Object.DestroyImmediate(go);
        }

        static void Capture(Camera camera,Airport airport,Vector2 at,Vector2 up,float size,string name)
        {
            camera.orthographic = true; camera.orthographicSize = size; camera.farClipPlane = 1500;
            camera.transform.position = airport.transform.TransformPoint(new Vector3(at.x,140,at.y));
            camera.transform.rotation = Quaternion.LookRotation(-airport.transform.up,airport.transform.TransformDirection(new Vector3(up.x,0,up.y)));
            SceneCapture.Capture(camera,"Captures/StandMarkings/"+name+".png",1800,1200);
        }
    }
}
