using System;
using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.EditorTools.Vehicles
{
    public static class BaggageDepotSetup
    {
        public static BaggageDepot Attach(BaggageStation station)
        {
            if (station.depot!=null)
            {
                var existing=station.depot;
                if (existing.bayLayoutVersion<3)
                {
                    if (existing.bayLayoutVersion==0) existing.firstBay+=existing.Facing*8f;
                    ExtendFrontage(station,existing); existing.ConnectBay(station);
                }
                existing.Rebuild(); return existing;
            }
            var airport=station.Airport;
            var go=new GameObject("Baggage depot"); go.transform.SetParent(airport.transform,false);
            var depot=go.AddComponent<BaggageDepot>();
            depot.bearing=station.dispatchBearing; depot.firstBay=airport.Taxiways.At(station.dispatchNode)+depot.Facing*8f;
            station.depot=depot; station.depotBay=0; station.workersPerTeam=4;
            airport.Baggage.ParkingChanged();
            ExtendFrontage(station,depot); depot.ConnectBay(station); depot.Rebuild(); return depot;
        }

        static void ExtendFrontage(BaggageStation station,BaggageDepot depot)
        {
            var net=station.Airport.Taxiways;
            var stand=net.stands.FirstOrDefault(s=>station.standIds.Contains(s.id));
            var entry=stand==null ? null : net.Node(stand.baggageEntryNode);
            if (entry==null) return;
            var front=net.At(entry.id);
            foreach (var node in net.nodes.Where(n=>n.runway==null && Vector2.Distance(net.At(n.id),front)<60 &&
                Mathf.Abs(Vector2.Dot(net.At(n.id)-front,depot.Facing))<.1f &&
                net.edges.Any(e=>e.IsRoad && (e.a==n.id || e.b==n.id))).ToArray())
                node.position+=new Vector3(depot.Facing.x,0,depot.Facing.y)*12f;
        }

        /// <summary>Remove the separately generated container example; the optional review remains available.</summary>
        public static void Consolidate(Airport airport)
        {
            var container=airport.GetComponentsInChildren<ContainerStation>().FirstOrDefault(s=>s.name=="Sandbox container hall");
            if (container==null) return;
            var net=airport.Taxiways;
            var stand=net.stands.FirstOrDefault(s=>container.standIds.Contains(s.id));
            if (stand!=null)
            {
                var layout=net.Layout(stand); var park=layout.ParkPosition(container.automaticHold.aircraft);
                var anchors=net.nodes.Where(n=>Vector2.Distance(net.At(n.id),new Vector2(stand.position.x-200,230))<.1f ||
                    Vector2.Distance(net.At(n.id),new Vector2(stand.position.x-200,stand.position.z))<.1f ||
                    Vector2.Distance(net.At(n.id),new Vector2(stand.position.x+200,stand.position.z))<.1f).ToArray();
                if (anchors.Length==3)
                {
                    int first=anchors.Min(n=>n.id);
                    foreach (var node in net.nodes.Where(n=>n.id>=first && n.position.x>=stand.position.x-201 &&
                        n.position.x<=stand.position.x+300 && n.position.z>=stand.position.z-20 && n.position.z<=231).ToArray())
                        net.RemoveNode(node.id);
                }
                airport.aprons.RemoveAll(a=>a.outline.Count==4 && a.Contains(park+new Vector2(70,0)) &&
                    Mathf.Abs(a.outline.Max(p=>p.x)-a.outline.Min(p=>p.x)-280)<.1f &&
                    Mathf.Abs(a.outline.Max(p=>p.y)-a.outline.Min(p=>p.y)-260)<.1f);
                foreach (var depot in airport.Depots.Where(d=>Vector2.Distance(
                    BaggageServicePlan.XZ(airport.transform.InverseTransformPoint(d.transform.position)),park+new Vector2(90,-100))<5).ToArray())
                    airport.RemoveDepot(depot);
                net.RemoveStand(stand);
            }
            var bulk=container.bulkStation;
            if (bulk!=null && bulk.name=="Sandbox widebody bulk hall") Object.DestroyImmediate(bulk.gameObject);
            Object.DestroyImmediate(container.gameObject);
            foreach (var aircraft in airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>())
                if (aircraft.name=="Widebody sandbox container turnaround") Object.DestroyImmediate(aircraft.gameObject);
            foreach (var controls in Object.FindObjectsByType<DevContainerControls>(FindObjectsInactive.Include))
                if (controls.station==null) Object.DestroyImmediate(controls);
            airport.BuildGround(); net.Rebuild();
        }

        public static void InstallSandbox()
        {
            try
            {
                var scene=EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
                var airport=Object.FindAnyObjectByType<Airport>(); Consolidate(airport);
                var station=BaggageSandboxSetup.Install(airport); Attach(station);
                airport.BuildGround(); airport.Taxiways.Rebuild();
                EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
                Debug.Log("Sandbox now contains one baggage example, with a staffed Small depot and shared collection/unloading bay.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
