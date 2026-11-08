using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum BaggageDepotSize { Small, Medium, Large }
    [System.Flags]
    public enum BaggageDepotUpgrade { None=0, HandlingAssist=1, PoweredBelts=2, AutomaticSorting=4, BufferStorage=8 }

    /// <summary>A shared baggage destination, equipment capacity and hired workforce. Bags stay in the airport ledger.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BaggageDepot : MonoBehaviour
    {
        public BaggageDepotSize size;
        [Min(0)] public int hiredWorkers=4;
        public Vector2 firstBay;
        public float bearing;
        public int bayLayoutVersion;
        public BaggageDepotUpgrade upgrades;
        public bool showInterior;
        public BaggageTerminalConnection connection=new BaggageTerminalConnection();
        public bool showConnection;
        public Terminal ConnectedTerminal => Airport?.Baggage.ConnectedTerminal(this);
        public void RefreshTerminalPresentation() => presentation?.Build();
        public void RefreshTerminalConnection() => presentation?.RefreshConnection();
        public void ShowConnection(bool visible) { showConnection=visible; presentation?.RefreshConnection(); }
        public readonly struct Limits
        {
            public readonly int teams,trailers,workers;
            public Limits(int teams,int trailers,int workers) { this.teams=teams; this.trailers=trailers; this.workers=workers; }
        }
        public static Limits Capacity(BaggageDepotSize size) => size==BaggageDepotSize.Large ? new Limits(8,24,40) :
            size==BaggageDepotSize.Medium ? new Limits(4,12,20) : new Limits(2,4,8);
        public Limits Maximum => Capacity(size);
        public BaggageDepotLayout Layout => new BaggageDepotLayout(size);
        public int ProcessingBays => Layout.Lanes;
        public int StorageCapacity => Maximum.trailers*200*(Has(BaggageDepotUpgrade.BufferStorage) ? 2 : 1);
        public bool Has(BaggageDepotUpgrade upgrade) => (upgrades&upgrade)==upgrade;
        public bool Supports(BaggageDepotUpgrade upgrade) => upgrade!=BaggageDepotUpgrade.AutomaticSorting || size!=BaggageDepotSize.Small;
        public Airport Airport => GetComponentInParent<Airport>();
        BaggageStation[] teamRegistry,teamCache;
        public BaggageStation[] Teams
        {
            get
            {
                if (Airport==null) return System.Array.Empty<BaggageStation>();
                var registry=Airport.Baggage.Stations;
                if (!ReferenceEquals(registry,teamRegistry)) { teamRegistry=registry; teamCache=registry.Where(s=>s.depot==this).ToArray(); }
                return teamCache;
            }
        }
        public int Trailers => Teams.Sum(s=>Mathf.Max(1,s.cartCount));
        public int Working => assignments.Values.Sum();
        public int Available => Mathf.Max(0,hiredWorkers-Working);
        readonly Dictionary<BaggageFleet,int> assignments=new Dictionary<BaggageFleet,int>();
        readonly Dictionary<int,BaggageFleet> bayOwners=new Dictionary<int,BaggageFleet>();
        readonly List<BaggageFleet> bayQueue=new List<BaggageFleet>();
        public int BusyBays => bayOwners.Count;
        BaggageLedger ledger;
        BaggageStore dispatch,arrivals;
        BaggageDepotPresentation presentation;
        bool dirty=true;
        bool built;
        int builtLayout;
        public Vector2 Facing => BaggageServicePlan.Facing(bearing);
        public Vector2 Right => new Vector2(Facing.y,-Facing.x);
        public Vector2 Point(float across,float along) => firstBay+Right*across+Facing*along;
        public Vector2 Bay(int index) { var p=Layout.Home(index); return Point(p.x,p.y); }
        public Vector2 WorkingBay(int lane) { var p=Layout.Work(lane); return Point(p.x,p.y); }
        public StandMarkings.EquipmentBay PaintArea => new StandMarkings.EquipmentBay {
            centre=Point(Layout.YardRight*.5f,(Layout.EntryZ+Layout.ExitZ)*.5f),facing=Facing,
            width=Layout.YardRight+4,length=Layout.ExitZ-Layout.EntryZ+12 };
        public float ConveyorSeconds => 4.5f/(BaggageLoadPath.BeltSpeed*(Has(BaggageDepotUpgrade.PoweredBelts) ? 1.5f : 1));
        public float HandSeconds => 3.6f/(BaggagePacing.WorkRate*(Has(BaggageDepotUpgrade.HandlingAssist) ? 1.3f : 1));
        public float SortingSeconds => (Has(BaggageDepotUpgrade.AutomaticSorting) ? 1.25f : 3.6f)/BaggagePacing.WorkRate;
        public Vector2 Door(int lane) => Point(Layout.LaneX(lane)+7,Layout.WorkZ-6);
        public Vector3 Feed(BaggageFleet fleet,bool outgoing)
        { var p=Point(Layout.LaneX(Layout.LaneFor(fleet.Station.depotBay))+2.5f,Layout.WorkZ-6+(outgoing ? .9f : -.9f)); return new Vector3(p.x,.8f,p.y); }
        public Vector3 CartPickup(BaggageFleet fleet,int worker)
        {
            var load=fleet.Station.cartType.baggage;
            var local=new Vector3(load.loadMax.x-.2f,load.loadMin.y,Mathf.Lerp(load.loadMin.z+.4f,load.loadMax.z-.4f,worker==0 ? .4f : .7f));
            var p=fleet.CartPose.Point(local); return new Vector3(p.x,local.y,p.y);
        }
        public BaggageCrew.BagPose BagAt(BaggageFleet fleet,float elapsed,int bagIndex)
        {
            var job=fleet.Job; bool incoming=fleet.IsCart(job.TransferFrom); int worker=bagIndex%fleet.Crew.GroundWorkers;
            var feed=Feed(fleet,!incoming); var door=Door(Layout.LaneFor(fleet.Station.depotBay));
            var inside=new Vector3(door.x,.8f,door.y)+(incoming ? -1 : 1)*new Vector3(Facing.x,0,Facing.y)*.9f;
            float conveyor=ConveyorSeconds;
            var result=new BaggageCrew.BagPose { holder=-1,worker=worker,rotation=Quaternion.LookRotation(new Vector3(Right.x,0,Right.y)) };
            if (incoming && elapsed<HandSeconds || !incoming && elapsed>=conveyor)
            {
                float t=Mathf.Clamp01((incoming ? elapsed : elapsed-conveyor)/HandSeconds);
                var a=incoming ? CartPickup(fleet,worker) : feed; var b=incoming ? feed : CartPickup(fleet,worker);
                result.bottom=Vector3.Lerp(a,b,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.2f); result.holder=0;
            }
            else result.bottom=Vector3.Lerp(incoming ? feed : inside,incoming ? inside : feed,
                Mathf.Clamp01((incoming ? elapsed-HandSeconds : elapsed)/conveyor));
            return result;
        }
        public AirportMaintenance.Box BuildingBox => AirportMaintenance.Box.Of(PaintArea.centre,Facing,
            new Bounds(Vector3.zero,new Vector3(PaintArea.width,Layout.Ridge,PaintArea.length)),.5f);
        public IEnumerable<AirportMaintenance.Box> SolidBoxes() => Layout.Obstacles(this);

        public bool TryEnterBay(BaggageFleet fleet,out string reason)
        {
            reason=null;
            if (fleet?.Station.depot!=this) { reason="The handling team belongs to another depot."; return false; }
            int lane=Layout.LaneFor(fleet.Station.depotBay);
            if (bayOwners.TryGetValue(lane,out var owner) && owner==fleet) return true;
            if (!bayQueue.Contains(fleet)) bayQueue.Add(fleet);
            if (!bayOwners.ContainsKey(lane) && bayQueue.First(f=>Layout.LaneFor(f.Station.depotBay)==lane)==fleet)
            { bayQueue.Remove(fleet); bayOwners.Add(lane,fleet); return true; }
            reason="waiting for a free depot working bay"; return false;
        }
        public void ReleaseBay(BaggageFleet fleet)
        {
            bayQueue.Remove(fleet);
            foreach (int lane in bayOwners.Where(p=>p.Value==fleet).Select(p=>p.Key).ToArray()) bayOwners.Remove(lane);
        }
        internal void ReleaseBayWhenClear(BaggageFleet fleet)
        {
            int lane=Layout.LaneFor(fleet.Station.depotBay);
            if (!bayOwners.TryGetValue(lane,out var owner) || owner!=fleet || fleet.Train==null) return;
            var zone=AirportMaintenance.Box.Of(Point(Layout.LaneX(lane),(Layout.WorkEntryZ+Layout.ExitZ)*.5f),Facing,
                new Bounds(Vector3.zero,new Vector3(6,1,Layout.ExitZ-Layout.WorkEntryZ+4)),0);
            bool Inside(BaggageVehiclePose p,VehicleType t) => zone.Overlaps(AirportMaintenance.Box.Of(p.position,p.facing,t.bounds,.5f));
            if (Inside(fleet.Train.Tractor,fleet.Equipment.Tractor)) return;
            for (int i=0;i<fleet.Train.CartCount;i++) if (Inside(fleet.Train.CartAt(i),fleet.Equipment.Cart)) return;
            ReleaseBay(fleet);
        }
        public bool SetUpgrade(BaggageDepotUpgrade upgrade,bool installed,out string reason)
        {
            reason=null;
            if (upgrade!=BaggageDepotUpgrade.HandlingAssist && upgrade!=BaggageDepotUpgrade.PoweredBelts &&
                upgrade!=BaggageDepotUpgrade.AutomaticSorting && upgrade!=BaggageDepotUpgrade.BufferStorage)
            { reason="Choose one depot equipment upgrade."; return false; }
            if (Working>0) { reason="Wait for handling teams to return before changing depot equipment."; return false; }
            if (installed && !Supports(upgrade)) { reason="Automatic sorting needs a Medium or Large baggage hall."; return false; }
            var previous=upgrades; upgrades=installed ? upgrades|upgrade : upgrades&~upgrade;
            if (dispatch!=null && (dispatch.Count>StorageCapacity || arrivals.Count>StorageCapacity ||
                dispatch.Weight>StorageCapacity*30f || arrivals.Weight>StorageCapacity*30f))
            { upgrades=previous; reason="Move stored luggage before removing buffer storage."; return false; }
            if (dispatch!=null) { dispatch.Resize(StorageCapacity,StorageCapacity*30f); arrivals.Resize(StorageCapacity,StorageCapacity*30f); }
            presentation?.Build(); return true;
        }
        public void SetInterior(bool visible) { showInterior=visible; presentation?.SetInterior(); }

        public string PlacementProblem()
        {
            var box=BuildingBox;
            if (StandEquipmentLayout.StorageExclusions(Airport,this).Any(q=>q.Overlaps(box)))
                return "The depot needs clear space beside the road, outside aircraft, buildings and equipment.";
            foreach (float x in new[] { -box.hx,box.hx })
                foreach (float z in new[] { -box.hz,box.hz })
                {
                    var p=box.c+box.ax*x+box.az*z;
                    if (!Airport.aprons.Any(a=>a.surface.IsPaved() && a.Contains(p))) return "Pave the depot footprint before building or expanding it.";
                }
            for (float z=Layout.EntryZ;z<=Layout.ExitZ;z+=2)
            {
                var p=Point(0,z);
                if (!Airport.Taxiways.edges.Any(e=>e.road==RoadClass.Service &&
                    DistanceToSegment(p,Airport.Taxiways.At(e.a),Airport.Taxiways.At(e.b))<.1f &&
                    Mathf.Abs(Vector2.Dot((Airport.Taxiways.At(e.b)-Airport.Taxiways.At(e.a)).normalized,Facing))>.99f))
                    return "Extend the straight service-road frontage for this depot size.";
            }
            return null;
        }

        internal BaggageStore Dispatch(BaggageLedger owner) { Stores(owner); return dispatch; }
        internal BaggageStore Arrivals(BaggageLedger owner) { Stores(owner); return arrivals; }
        void Stores(BaggageLedger owner)
        {
            if (ledger==owner) return;
            ledger=owner;
            int capacity=StorageCapacity;
            dispatch=owner.Store(name+" departures",capacity,capacity*30f);
            arrivals=owner.Store(name+" arrivals",capacity,capacity*30f);
        }

        public bool Hire(out string reason)
        {
            reason=null;
            if (hiredWorkers>=Maximum.workers) { reason="Depot staff capacity reached."; return false; }
            hiredWorkers++; return true;
        }
        public bool Fire(out string reason)
        {
            reason=null;
            if (Available==0) { reason=Working>0 ? "Assigned workers finish their turnaround before leaving." : "No workers to dismiss."; return false; }
            hiredWorkers--; return true;
        }
        public bool Resize(BaggageDepotSize requested,out string reason)
        {
            reason=null; var limit=Capacity(requested);
            if (Working>0) { reason="Wait for handling teams to return before rebuilding the depot."; return false; }
            if (requested==BaggageDepotSize.Small && Has(BaggageDepotUpgrade.AutomaticSorting))
            { reason="Remove automatic sorting before switching to the Small baggage shed."; return false; }
            if (hiredWorkers>limit.workers || Teams.Length>limit.teams || Trailers>limit.trailers ||
                Teams.Any(t=>t.cartCount>new BaggageDepotLayout(requested).CartsPerTrain))
            { reason="The smaller depot cannot accommodate the current staff and equipment."; return false; }
            int capacity=limit.trailers*200*(Has(BaggageDepotUpgrade.BufferStorage) ? 2 : 1);
            if (dispatch!=null && (dispatch.Count>capacity || arrivals.Count>capacity || dispatch.Weight>capacity*30f || arrivals.Weight>capacity*30f))
            { reason="Move the stored luggage before reducing depot capacity."; return false; }
            var previous=size; size=requested;
            reason=PlacementProblem(); if (reason!=null) { size=previous; return false; }
            if (dispatch!=null) { dispatch.Resize(capacity,capacity*30f); arrivals.Resize(capacity,capacity*30f); }
            Rebuild(); foreach (var team in Teams) ConnectBay(team); Airport.Baggage.ParkingChanged(); return true;
        }
        public bool CanAddTrailer(BaggageStation station,out string reason)
        {
            reason=null;
            if (station.depot!=this || Trailers>=Maximum.trailers) { reason="Depot trailer capacity reached."; return false; }
            if (station.cartCount>=Layout.CartsPerTrain) { reason="This depot's working lanes cannot accommodate a longer train."; return false; }
            return true;
        }
        public bool TryAssignCrew(BaggageFleet fleet,out string reason)
        {
            reason=null;
            if (fleet==null || fleet.Station.depot!=this) { reason="The handling team belongs to another depot."; return false; }
            if (assignments.ContainsKey(fleet)) return true;
            if (Available<2) { reason="waiting for two available baggage workers; hire staff at the depot"; return false; }
            assignments.Add(fleet,Mathf.Min(Available,Mathf.Clamp(fleet.Station.workersPerTeam,2,4)));
            return true;
        }
        internal void Rebalance(BaggageFleet fleet)
        {
            if (!assignments.TryGetValue(fleet,out int previous)) return;
            assignments[fleet]=Mathf.Min(previous+Available,Mathf.Clamp(fleet.Station.workersPerTeam,2,4));
        }
        internal int Workers(BaggageFleet fleet) => assignments.TryGetValue(fleet,out var n) ? n : 0;
        public void ReleaseCrew(BaggageFleet fleet) { assignments.Remove(fleet); ReleaseBay(fleet); }


        /// <summary>Each added team has a distinct forward-facing depot bay and its own equipment lease.</summary>
        public BaggageStation AddTeam(BaggageStation source,out string reason)
        {
            reason=null; var teams=Teams;
            if (source==null || source.Airport!=Airport) { reason="Choose a baggage team at this airport."; return null; }
            int carts=Mathf.Min(source.cartCount,Layout.CartsPerTrain);
            if (teams.Length>=Maximum.teams || Trailers+carts>Maximum.trailers)
            { reason="Depot team or trailer capacity reached."; return null; }
            reason=PlacementProblem(); if (reason!=null) return null;
            Rebuild();
            int bay=Enumerable.Range(0,Maximum.teams).First(i=>!teams.Any(t=>t.depotBay==i));
            var go=new GameObject(name+" team "+(bay+1)); go.SetActive(false); go.transform.SetParent(Airport.transform,false);
            var team=go.AddComponent<BaggageStation>();
            team.tractorType=source.tractorType; team.cartType=source.cartType; team.loaderType=source.loaderType;
            team.tractorPrefab=source.tractorPrefab; team.cartPrefab=source.cartPrefab; team.loaderPrefab=source.loaderPrefab;
            team.bagPrefabs=source.bagPrefabs; team.bagPropType=source.bagPropType; team.crewPrefab=source.crewPrefab; team.crewType=source.crewType;
            team.cartCount=carts; team.workersPerTeam=source.workersPerTeam;
            var hold=source.automaticHold;
            team.automaticHold=new BulkHoldConfiguration { aircraft=hold.aircraft,cargoPoint=hold.cargoPoint,bagCapacity=hold.bagCapacity,weightCapacity=hold.weightCapacity };
            team.standIds=new List<int>(source.standIds); team.incomingBags=source.incomingBags; team.outgoingBags=source.outgoingBags;
            team.kilogramsPerBag=source.kilogramsPerBag; team.automaticHandling=source.automaticHandling;
            team.depot=this; team.depotBay=bay; team.parkAtStand=false;
            ConnectBay(team); go.SetActive(true); Airport.Taxiways.EquipmentPaintChanged(); return team;
        }

        int RoadNode(float x,float z)
        {
            var net=Airport.Taxiways; var at=Point(x,z);
            var existing=net.nodes.FirstOrDefault(n=>n.runway==null && Vector2.Distance(net.At(n.id),at)<.05f);
            if (existing!=null) return existing.id;
            var road=net.edges.FirstOrDefault(e=>e.road==RoadClass.Service && DistanceToSegment(at,net.At(e.a),net.At(e.b))<.05f);
            if (road!=null) return net.SplitEdge(road,new Vector3(at.x,0,at.y));
            return net.AddNode(new Vector3(at.x,0,at.y));
        }
        void Link(int a,int b,float width=4.5f)
        {
            if (a!=b && !Connected(a,b)) Airport.Taxiways.Connect(a,b,width,PavementSurface.Concrete,false,RoadClass.Service);
        }
        void BuildRoads()
        {
            var net=Airport.Taxiways; var layout=Layout;
            // Headers stay outside the shell and leave a full train length behind parked tractors.
            var headers=new SortedSet<float> { layout.EntryZ,layout.HomeExitZ,layout.WorkEntryZ,layout.ExitZ };
            foreach (float z in headers)
            {
                int spine=RoadNode(0,z);
                var join=net.nodes.Where(n=>n.id!=spine && n.runway==null &&
                    Mathf.Abs(Vector2.Dot(net.At(n.id)-firstBay,Right))<.1f &&
                    net.edges.Any(e=>e.road==RoadClass.Service && (e.a==n.id || e.b==n.id)))
                    .OrderBy(n=>Vector2.SqrMagnitude(net.At(n.id)-net.At(spine))).FirstOrDefault();
                if (join!=null) Link(join.id,spine,12);
                Link(spine,RoadNode(layout.YardRight-2,z),8);
            }
            for (int i=0;i<Maximum.teams;i++)
            {
                float x=layout.HomeX(i);
                Link(RoadNode(x,layout.EntryZ),RoadNode(x,layout.HomeZ));
                Link(RoadNode(x,layout.HomeZ),RoadNode(x,layout.HomeExitZ));
            }
            for (int i=0;i<layout.Lanes;i++)
            {
                float x=layout.LaneX(i);
                Link(RoadNode(x,layout.WorkEntryZ),RoadNode(x,layout.WorkZ));
                Link(RoadNode(x,layout.WorkZ),RoadNode(x,layout.ExitZ));
            }
            // A clear outside lane gives the large centre an alternative to the working lanes.
            if (size==BaggageDepotSize.Large)
                Link(RoadNode(layout.YardRight-2,layout.WorkEntryZ),RoadNode(layout.YardRight-2,layout.ExitZ),6);
        }
        public int WorkNode(int team)
        {
            var p=Layout.Work(Layout.LaneFor(team)); return RoadNode(p.x,p.y);
        }
        bool Connected(int a,int b,TaxiwayEdge except=null)
        {
            var net=Airport.Taxiways; var start=net.At(a); var end=net.At(b); var delta=end-start;
            var spans=new List<Vector2>();
            foreach (var edge in net.edges)
            {
                if (edge==except || edge.road!=RoadClass.Service || edge.width<4.49f) continue;
                var p=net.At(edge.a); var q=net.At(edge.b);
                if (DistanceToSegment(p,start,end)>.05f || DistanceToSegment(q,start,end)>.05f) continue;
                float u=Vector2.Dot(p-start,delta)/delta.sqrMagnitude,v=Vector2.Dot(q-start,delta)/delta.sqrMagnitude;
                spans.Add(new Vector2(Mathf.Min(u,v),Mathf.Max(u,v)));
            }
            spans.Sort((p,q)=>p.x.CompareTo(q.x)); float covered=0;
            foreach (var span in spans)
            {
                if (span.x>covered+.001f) return false;
                covered=Mathf.Max(covered,span.y); if (covered>=.999f) return true;
            }
            return false;
        }
        public void ConnectBay(BaggageStation team)
        {
            BuildRoads();
            var p=Layout.Home(team.depotBay); int home=RoadNode(p.x,p.y);
            team.dispatchNode=team.arrivalsNode=home; team.dispatchBearing=team.arrivalsBearing=bearing;
            if (!team.parkAtStand)
            {
                var loader=Layout.Loader(team.depotBay); team.loaderParking=Point(loader.x,loader.y);
                team.loaderParkingBearing=bearing;
            }
            bayLayoutVersion=4;
        }
        static float DistanceToSegment(Vector2 p,Vector2 a,Vector2 b) => Vector2.Distance(p,
            Vector2.Lerp(a,b,Mathf.Clamp01(Vector2.Dot(p-a,b-a)/Mathf.Max(.001f,(b-a).sqrMagnitude))));

        public void Rebuild()
        {
            if (!isActiveAndEnabled || Airport==null) return;
            var net=Airport.Taxiways; int nodes=net.nodes.Count,edges=net.edges.Count;
            BuildRoads();
            foreach (var team in Teams) ConnectBay(team);
            if (nodes!=net.nodes.Count || edges!=net.edges.Count) net.Rebuild();
            presentation ??= new BaggageDepotPresentation(this);
            presentation.Build(); dirty=false;
            int layout=System.HashCode.Combine(firstBay,bearing,size);
            if (built && layout!=builtLayout) { Airport.LayoutChanged(); Airport.Taxiways.EquipmentPaintChanged(); }
            built=true; builtLayout=layout;
        }
        internal void EnsureBuilt() { if (dirty || presentation==null) Rebuild(); }
        void PaintChanged() { var airport=Airport; if (airport!=null) airport.GetComponentInChildren<TaxiwayNetwork>()?.EquipmentPaintChanged(); }
        void OnEnable() { dirty=true; PaintChanged(); }
        void OnValidate() => dirty=true;
        void Update() { if (dirty) Rebuild(); }
        public void Present() { if (presentation==null) Rebuild(); presentation?.Present(); }
        void LateUpdate() { if (Application.isPlaying) Present(); }
        void OnDisable() { presentation?.Destroy(); presentation=null; PaintChanged(); }
    }
}
