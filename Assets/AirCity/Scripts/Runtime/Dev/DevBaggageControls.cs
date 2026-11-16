using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public enum BaggagePlacement { Dispatch, Arrivals, Loader, Depot, Underground, Covered }

    /// <summary>Sandbox authoring actions. The airport owns all baggage and operational state.</summary>
    public sealed class DevBaggageControls : MonoBehaviour
    {
        public BaggageStation station;
        public string Message { get; private set; }
        public BaggagePlacement Placement { get; private set; }
        public bool Linking => Placement==BaggagePlacement.Underground || Placement==BaggagePlacement.Covered;
        public BaggageDepot LinkSource { get; private set; }
        public bool HasPreview { get; private set; }
        public Vector2 PreviewOrigin { get; private set; }
        public float PreviewBearing { get; private set; }
        public string PreviewProblem { get; private set; }
        bool reverse;
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
                foreach (var job in Airport.Baggage.Jobs)
                    if (!job.Finished && (job.Fleet.Station == station || station.standIds.Contains(job.Stand.id))) return false;
                foreach (var job in Airport.Baggage.ContainerJobs)
                    if (!job.Finished && station.standIds.Contains(job.Stand.id)) return false;
                return true;
            }
        }
        public int standIndex;
        public BaggageDepotSize depotSize;
        public void SetMessage(string message) => Message=message;

        public void BeginHall(BaggageDepotSize size) { depotSize=size; BeginPlacement(BaggagePlacement.Depot); }
        public void SelectHall(BaggageDepot hall)
        {
            if (hall==null) return;
            station=System.Array.Find(hall.Teams,t=>t==station) ?? System.Array.Find(hall.Teams,t=>t!=null);
        }
        public void FlipSite() => reverse=!reverse;
        public void Cancel()
        {
            HasPreview=false; LinkSource=null; Message=null;
            GetComponent<DevAirfieldTool>().SetTool(DevTool.Select);
        }
        public void Preview(Vector3 worldPoint)
        {
            HasPreview=false; PreviewProblem=null;
            if (Airport==null || Placement!=BaggagePlacement.Depot) return;
            var p=BaggageServicePlan.XZ(Airport.transform.InverseTransformPoint(worldPoint));
            float best=15; Vector2 origin=p,facing=Vector2.up;
            foreach (var edge in Airport.Taxiways.edges)
            {
                if (edge.road!=RoadClass.Service) continue;
                var a=Airport.Taxiways.At(edge.a); var b=Airport.Taxiways.At(edge.b); var d=b-a;
                var q=a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude));
                float distance=Vector2.Distance(p,q);
                if (distance>=best) continue;
                best=distance; origin=q; facing=d.normalized; HasPreview=true;
            }
            if (!HasPreview) { PreviewProblem="Move within 15 m of a straight service road."; return; }
            if (reverse) facing=-facing;
            PreviewOrigin=origin; PreviewBearing=Mathf.Repeat(Mathf.Atan2(facing.x,facing.y)*Mathf.Rad2Deg,360);
            PreviewProblem=BaggageDepot.PlacementProblem(Airport,depotSize,origin,PreviewBearing);
        }
        public BaggageDepot HallAt(Vector3 worldPoint)
        {
            if (Airport==null) return null;
            var p=BaggageServicePlan.XZ(Airport.transform.InverseTransformPoint(worldPoint));
            foreach (var hall in Airport.GetComponentsInChildren<BaggageDepot>())
            {
                var b=hall.BuildingBox; var d=p-b.c;
                if (Mathf.Abs(Vector2.Dot(d,b.ax))<=b.hx && Mathf.Abs(Vector2.Dot(d,b.az))<=b.hz) return hall;
            }
            return null;
        }
        void PlaceLink(Vector3 worldPoint)
        {
            var hall=HallAt(worldPoint);
            if (hall!=null) { LinkSource=hall; Message="Now click the terminal to connect to "+hall.name+"."; return; }
            if (LinkSource==null) { Message="Click a baggage hall first, then its terminal."; return; }
            var p=BaggageServicePlan.XZ(Airport.transform.InverseTransformPoint(worldPoint));
            if (!Airport.TryPickTerminal(p,out var terminal)) { Message="Click a terminal building."; return; }
            if (!Airport.Baggage.ConnectTerminal(LinkSource,terminal,Placement==BaggagePlacement.Covered ?
                BaggageConnectionKind.Covered : BaggageConnectionKind.Underground,out var reason)) { Message=reason; return; }
            var connected=LinkSource; connected.ShowConnection(true); LinkSource=null; Message=null;
            GetComponent<DevAirfieldTool>().SelectBaggageHall(connected);
        }

        public void HireWorker() { if (Station?.depot!=null) { Station.depot.Hire(out var why); Message=why; } }
        public void FireWorker() { if (Station?.depot!=null) { Station.depot.Fire(out var why); Message=why; } }
        public void ResizeDepot(int step)
        {
            if (Station?.depot!=null)
            {
                var requested=(BaggageDepotSize)Mathf.Clamp((int)Station.depot.size+step,0,2);
                Station.depot.Resize(requested,out var why); Message=why;
            }
        }
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
            else { station.parkAtStand=false; station.loaderParkingBearing = Mathf.Repeat(station.loaderParkingBearing + degrees, 360f); }
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
            if (orbit == null && GetComponent<AirCity.Game.CareerCamera>() is AirCity.Game.CareerCamera shared)
            {
                var centre = station.depot != null ? station.depot.PaintArea.centre : station.loaderParking;
                shared.FocusPoint(Airport.transform.TransformPoint(new Vector3(centre.x, 0, centre.y)), 180f, 50f, 215f); return;
            }
            if (orbit == null) return;
            var at = Airport.Taxiways.Node(station.dispatchNode) != null ? Airport.Taxiways.At(station.dispatchNode) : station.loaderParking;
            if (station.depot!=null)
            {
                var depot=station.depot; at=depot.PaintArea.centre;
                orbit.pivot=Airport.transform.TransformPoint(new Vector3(at.x,0,at.y));
                orbit.distance=Mathf.Max(75,Mathf.Max(depot.PaintArea.width,depot.PaintArea.length)*1.1f); orbit.pitch=45; orbit.yaw=depot.bearing-110; return;
            }
            orbit.pivot = Airport.transform.TransformPoint(new Vector3(at.x - 20f, 0f, at.y + 15f));
            orbit.distance = 180f; orbit.pitch = 58f; orbit.yaw = 215f;
        }

        public void AddStation()
        {
            var source = Station;
            if (source == null) return;
            if (source.depot!=null)
            {
                var added=source.depot.AddTeam(source,out var why);
                if (added!=null) station=added; Message=why; return;
            }
            var go = new GameObject("Baggage station " + (Airport.Baggage.Stations.Length + 1));
            go.transform.SetParent(Airport.transform, false);
            station = go.AddComponent<BaggageStation>();
            station.tractorType = source.tractorType; station.cartType = source.cartType; station.loaderType = source.loaderType;
            station.tractorPrefab = source.tractorPrefab; station.cartPrefab = source.cartPrefab; station.loaderPrefab = source.loaderPrefab;
            station.bagPrefabs = source.bagPrefabs; station.cartCount = source.cartCount; station.bagPropType = source.bagPropType;
            station.crewPrefab = source.crewPrefab; station.crewType = source.crewType;
            var h = source.automaticHold;
            station.automaticHold = new BulkHoldConfiguration { aircraft = h.aircraft, cargoPoint = h.cargoPoint,
                bagCapacity = h.bagCapacity, weightCapacity = h.weightCapacity };
            station.parkAtStand=true;
            Message = "Place dispatch and arrivals, then assign a stand for equipment staging.";
        }

        public void BeginPlacement(BaggagePlacement placement)
        {
            if (!CanConfigure && placement<BaggagePlacement.Depot) return;
            Placement = placement;
            HasPreview=false; PreviewProblem=null; LinkSource=null;
            if (Linking) LinkSource=GetComponent<DevAirfieldTool>().SelectedBaggageHall;
            Message = placement == BaggagePlacement.Loader ? "Click loader parking on the apron." : placement==BaggagePlacement.Depot ?
                "Click beside a straight service road on a paved apron. Z flips the yard to the other side." : Linking ?
                LinkSource!=null ? "Click the terminal to connect to "+LinkSource.name+"." : "Click a baggage hall, then the terminal it serves." : "Click a service-road junction.";
            GetComponent<DevAirfieldTool>().SetTool(DevTool.Baggage);
        }

        public void Place(Vector3 worldPoint)
        {
            if (Airport==null) return;
            if (Linking) { PlaceLink(worldPoint); return; }
            if (Placement==BaggagePlacement.Depot)
            {
                Preview(worldPoint);
                if (!HasPreview || PreviewProblem!=null) { Message=PreviewProblem; return; }
                var catalog=BaggageBuildCatalog.Load();
                if (catalog==null || !catalog.Ready) { Message="Baggage equipment package is missing."; return; }
                var go=new GameObject(depotSize+" baggage hall"); go.SetActive(false); go.transform.SetParent(Airport.transform,false);
                var depot=go.AddComponent<BaggageDepot>(); depot.size=depotSize; depot.firstBay=PreviewOrigin; depot.bearing=PreviewBearing;
                depot.connection.enabled=true; go.SetActive(true);
                station=catalog.AddTeam(depot,out var why);
                if (station==null) { go.SetActive(false); Destroy(go); Message=why; return; }
                HasPreview=false; Message=null; GetComponent<DevAirfieldTool>().SelectBaggageHall(depot); return;
            }
            if (!CanConfigure) return;
            var point = BaggageServicePlan.XZ(Airport.transform.InverseTransformPoint(worldPoint));
            if (Placement == BaggagePlacement.Loader) { station.parkAtStand=false; station.loaderParking = point; }
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
            Airport.Baggage.RefreshParking(station);
        }

        public void StandParking(bool enabled)
        {
            if (!CanConfigure) return;
            station.parkAtStand=enabled;
            if (!enabled) station.depot?.ConnectBay(station);
            Airport.Baggage.RefreshParking(station);
        }
        public void StageAt(Stand stand)
        { if (!CanConfigure || stand==null || !station.standIds.Contains(stand.id)) return; station.parkingStandId=stand.id; station.parkAtStand=true; Airport.Baggage.RefreshParking(station); }
    }
}
