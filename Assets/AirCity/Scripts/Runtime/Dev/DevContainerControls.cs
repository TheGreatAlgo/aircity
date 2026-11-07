using System;
using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    /// <summary>Saved container-station authoring; operational state remains in AirportBaggage.</summary>
    public sealed class DevContainerControls : MonoBehaviour
    {
        public ContainerStation station;
        public int standIndex;
        public string Message { get; private set; }
        public BaggagePlacement Placement { get; private set; }
        Airport Airport => GetComponent<DevAirfieldTool>()?.airport;
        public ContainerStation Station
        {
            get
            {
                if (station == null && Airport != null)
                {
                    var stations = Airport.Baggage.ContainerStations;
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
                foreach (var job in Airport.Baggage.ContainerJobs)
                    if (!job.Finished && (job.Fleet.Station == station || station.standIds.Contains(job.Stand.id))) return false;
                foreach (var job in Airport.Baggage.Jobs)
                    if (!job.Finished && station.standIds.Contains(job.Stand.id)) return false;
                return true;
            }
        }
        public Stand AssignmentStand
        {
            get
            {
                if (Airport == null) return null;
                var stands = Airport.Taxiways.stands.FindAll(s => !s.DeIcing);
                return stands.Count == 0 ? null : stands[(standIndex % stands.Count + stands.Count) % stands.Count];
            }
        }
        public void NextStation()
        {
            var stations = Airport.Baggage.ContainerStations;
            if (stations.Length > 0) station = stations[(Array.IndexOf(stations, Station) + 1) % stations.Length];
            GetComponent<DevAirfieldTool>()?.SetTool(DevTool.Select);
        }
        public void FocusStation()
        {
            if (Station == null) return;
            var orbit = GetComponent<DevOrbitCamera>();
            if (orbit == null) return;
            var at = Airport.Taxiways.Node(station.dispatchNode) != null ? Airport.Taxiways.At(station.dispatchNode) : station.loaderParking;
            orbit.pivot = Airport.transform.TransformPoint(new Vector3(at.x - 30f, 0f, at.y + 20f));
            orbit.distance = 240f; orbit.pitch = 58f; orbit.yaw = 215f;
        }
        public void AddStation()
        {
            var source = Station;
            if (source == null) return;
            var go = new GameObject("Container station " + (Airport.Baggage.ContainerStations.Length + 1));
            go.transform.SetParent(Airport.transform, false);
            station = go.AddComponent<ContainerStation>();
            station.tractorType = source.tractorType; station.dollyType = source.dollyType;
            station.loaderType = source.loaderType; station.unitType = source.unitType;
            station.tractorPrefab = source.tractorPrefab; station.dollyPrefab = source.dollyPrefab;
            station.loaderPrefab = source.loaderPrefab; station.unitPrefab = source.unitPrefab;
            station.crewPrefab = source.crewPrefab;
            var h = source.automaticHold;
            station.automaticHold = new ContainerHoldConfiguration { aircraft = h.aircraft, cargoPoint = h.cargoPoint,
                acceptedCode = h.acceptedCode, slots = h.slots, maxGross = h.maxGross };
            station.parkAtStand=true;
            Message = "Place dispatch and arrivals, assign stands, then enable automatic handling.";
        }
        public void ChangeAircraft(int step)
        {
            if (!CanConfigure || Airport.aircraftTypes.Count == 0) return;
            var types = Airport.aircraftTypes; int i = types.IndexOf(station.automaticHold.aircraft);
            station.automaticHold.aircraft = types[(i + step + types.Count) % types.Count];
            Airport.Baggage.RefreshParking(station);
        }
        public void ChangeCargoPoint(int step)
        {
            if (!CanConfigure || station.automaticHold.aircraft == null) return;
            var points = new System.Collections.Generic.List<string>();
            foreach (var point in station.automaticHold.aircraft.PointsOfType(AirCity.Aircraft.ServicePointType.CargoDoor)) points.Add(point.name);
            if (points.Count == 0) return;
            int i = points.IndexOf(station.automaticHold.cargoPoint);
            station.automaticHold.cargoPoint = points[(i + step + points.Count) % points.Count];
            Airport.Baggage.RefreshParking(station);
        }
        public void NextBulkStation()
        {
            if (!CanConfigure) return;
            var stations = Airport.Baggage.Stations;
            int next = Array.IndexOf(stations, station.bulkStation) + 1;
            station.bulkStation = next < stations.Length ? stations[next] : null;
        }
        public void ToggleStand(Stand stand)
        {
            if (!CanConfigure || stand == null || stand.DeIcing) return;
            if (!station.standIds.Remove(stand.id)) station.standIds.Add(stand.id);
            Airport.Baggage.RefreshParking(station);
        }
        public void Turn(BaggagePlacement endpoint, float degrees)
        {
            if (!CanConfigure) return;
            if (endpoint == BaggagePlacement.Dispatch) station.dispatchBearing = Mathf.Repeat(station.dispatchBearing + degrees, 360f);
            else if (endpoint == BaggagePlacement.Arrivals) station.arrivalsBearing = Mathf.Repeat(station.arrivalsBearing + degrees, 360f);
            else { station.parkAtStand=false; station.loaderParkingBearing = Mathf.Repeat(station.loaderParkingBearing + degrees, 360f); }
            Airport.Baggage.RefreshParking(station);
        }
        public void BeginPlacement(BaggagePlacement placement)
        {
            if (!CanConfigure) return;
            Placement = placement;
            Message = placement == BaggagePlacement.Loader ? "Click loader parking on the apron." : "Click a service-road junction.";
            GetComponent<DevAirfieldTool>().SetTool(DevTool.Containers);
        }
        public void Place(Vector3 worldPoint)
        {
            if (!CanConfigure) return;
            var point = BaggageServicePlan.XZ(Airport.transform.InverseTransformPoint(worldPoint));
            if (Placement == BaggagePlacement.Loader) { station.parkAtStand=false; station.loaderParking = point; }
            else
            {
                var net = Airport.Taxiways; TaxiwayNode best = null; float distance = 15f;
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
            Message = null; Airport.Baggage.RefreshParking(station);
            GetComponent<DevAirfieldTool>().SetTool(DevTool.Select);
        }
        public void StandParking(bool enabled)
        { if (!CanConfigure) return; station.parkAtStand=enabled; Airport.Baggage.RefreshParking(station); }
        public void StageAt(Stand stand)
        { if (!CanConfigure || stand==null || !station.standIds.Contains(stand.id)) return; station.parkingStandId=stand.id; station.parkAtStand=true; Airport.Baggage.RefreshParking(station); }
    }
}
