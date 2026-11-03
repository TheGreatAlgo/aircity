using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public enum BaggagePlacement { Dispatch, Arrivals, Loader }

    /// <summary>Sandbox authoring actions. The airport owns all baggage and operational state.</summary>
    public sealed class DevBaggageControls : MonoBehaviour
    {
        public BaggageStation station;
        public string Message { get; private set; }
        public BaggagePlacement Placement { get; private set; }
        Airport Airport => GetComponent<DevAirfieldTool>()?.airport;
        public BaggageStation Station
        {
            get
            {
                if (station == null && Airport != null)
                {
                    var stations = Airport.Baggage.Stations;
                    if (stations.Length > 0) station = stations[0];
                }
                return station;
            }
        }
        public bool CanConfigure
        {
            get
            {
                if (Station == null || Airport == null) return false;
                foreach (var job in Airport.Baggage.Jobs) if (job.Fleet.Station == station && !job.Finished) return false;
                return true;
            }
        }
        public int standIndex;
        public Stand AssignmentStand
        {
            get
            {
                var stands = Airport.Taxiways.stands.FindAll(s => !s.DeIcing);
                return stands.Count == 0 ? null : stands[(standIndex % stands.Count + stands.Count) % stands.Count];
            }
        }

        public void Turn(BaggagePlacement endpoint, float degrees)
        {
            if (!CanConfigure) return;
            if (endpoint == BaggagePlacement.Dispatch) station.dispatchBearing = Mathf.Repeat(station.dispatchBearing + degrees, 360f);
            else if (endpoint == BaggagePlacement.Arrivals) station.arrivalsBearing = Mathf.Repeat(station.arrivalsBearing + degrees, 360f);
            else station.loaderParkingBearing = Mathf.Repeat(station.loaderParkingBearing + degrees, 360f);
            Airport.Baggage.RefreshParking(station);
        }

        public void NextStation()
        {
            var stations = Airport.Baggage.Stations;
            if (stations.Length > 0) station = stations[(System.Array.IndexOf(stations, Station) + 1) % stations.Length];
        }

        public void FocusStation()
        {
            if (Station == null) return;
            var orbit = GetComponent<DevOrbitCamera>();
            if (orbit == null) return;
            var at = Airport.Taxiways.Node(station.dispatchNode) != null ? Airport.Taxiways.At(station.dispatchNode) : station.loaderParking;
            orbit.pivot = Airport.transform.TransformPoint(new Vector3(at.x - 20f, 0f, at.y + 15f));
            orbit.distance = 180f; orbit.pitch = 58f; orbit.yaw = 215f;
        }

        public void AddStation()
        {
            var source = Station;
            if (source == null) return;
            var go = new GameObject("Baggage station " + (Airport.Baggage.Stations.Length + 1));
            go.transform.SetParent(Airport.transform, false);
            station = go.AddComponent<BaggageStation>();
            station.tractorType = source.tractorType; station.cartType = source.cartType; station.loaderType = source.loaderType;
            station.tractorPrefab = source.tractorPrefab; station.cartPrefab = source.cartPrefab; station.loaderPrefab = source.loaderPrefab;
            station.bagPrefabs = source.bagPrefabs;
            var h = source.automaticHold;
            station.automaticHold = new BulkHoldConfiguration { aircraft = h.aircraft, cargoPoint = h.cargoPoint,
                bagCapacity = h.bagCapacity, weightCapacity = h.weightCapacity };
            Message = "Place dispatch, arrivals and loader parking, then assign a stand.";
        }

        public void BeginPlacement(BaggagePlacement placement)
        {
            if (!CanConfigure) return;
            Placement = placement;
            Message = placement == BaggagePlacement.Loader ? "Click loader parking on the apron." : "Click a service-road junction.";
            GetComponent<DevAirfieldTool>().SetTool(DevTool.Baggage);
        }

        public void Place(Vector3 worldPoint)
        {
            if (!CanConfigure) return;
            var point = BaggageServicePlan.XZ(Airport.transform.InverseTransformPoint(worldPoint));
            if (Placement == BaggagePlacement.Loader) station.loaderParking = point;
            else
            {
                var net = Airport.Taxiways;
                TaxiwayNode best = null; float distance = 15f;
                foreach (var node in net.nodes)
                {
                    if (node.runway != null || !net.edges.Exists(e => e.road == RoadClass.Service && (e.a == node.id || e.b == node.id))) continue;
                    float d = Vector2.Distance(point, net.At(node.id));
                    if (d < distance) { best = node; distance = d; }
                }
                if (best == null) { Message = "Choose a service-road junction within 15 m."; return; }
                var edge = net.edges.Find(e => e.road == RoadClass.Service && (e.a == best.id || e.b == best.id));
                var heading = (net.At(TaxiwayNetwork.Other(edge, best.id)) - net.At(best.id)).normalized;
                if (Placement == BaggagePlacement.Arrivals) heading = -heading;
                float bearing = Mathf.Repeat(Mathf.Atan2(heading.x, heading.y) * Mathf.Rad2Deg, 360f);
                if (Placement == BaggagePlacement.Dispatch) { station.dispatchNode = best.id; station.dispatchBearing = bearing; }
                else { station.arrivalsNode = best.id; station.arrivalsBearing = bearing; }
            }
            Message = null;
            Airport.Baggage.RefreshParking(station);
            GetComponent<DevAirfieldTool>().SetTool(DevTool.Select);
        }

        public void ToggleStand(Stand stand)
        {
            if (!CanConfigure || stand == null || stand.DeIcing) return;
            if (!station.standIds.Remove(stand.id)) station.standIds.Add(stand.id);
        }
    }
}
