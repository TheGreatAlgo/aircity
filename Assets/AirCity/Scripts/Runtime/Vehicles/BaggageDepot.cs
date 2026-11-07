using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum BaggageDepotSize { Small, Medium, Large }

    /// <summary>A shared baggage destination, equipment capacity and hired workforce. Bags stay in the airport ledger.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BaggageDepot : MonoBehaviour
    {
        public BaggageDepotSize size;
        [Min(0)] public int hiredWorkers=4;
        public Vector2 firstBay;
        public float bearing;
        public int bayLayoutVersion;
        public readonly struct Limits
        {
            public readonly int teams,trailers,workers;
            public Limits(int teams,int trailers,int workers) { this.teams=teams; this.trailers=trailers; this.workers=workers; }
        }
        public static Limits Capacity(BaggageDepotSize size) => size==BaggageDepotSize.Large ? new Limits(8,24,40) :
            size==BaggageDepotSize.Medium ? new Limits(4,12,20) : new Limits(2,4,8);
        public Limits Maximum => Capacity(size);
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
        BaggageLedger ledger;
        BaggageStore dispatch,arrivals;
        BaggageDepotPresentation presentation;
        bool dirty=true;
        bool built;
        int builtLayout;
        public Vector2 Facing => BaggageServicePlan.Facing(bearing);
        public Vector2 Right => new Vector2(Facing.y,-Facing.x);
        public Vector2 Point(float across,float along) => firstBay+Right*across+Facing*along;
        public Vector2 Bay(int index) => Point(4,index*24f);
        public float ConveyorSeconds => 4.5f/BaggageLoadPath.BeltSpeed;
        public Vector2 Door(int index) => Point(11,index*24f-6f);
        public Vector3 Feed(BaggageFleet fleet,bool outgoing)
        { var p=Point(6.5f,fleet.Station.depotBay*24f-6f+(outgoing ? .9f : -.9f)); return new Vector3(p.x,.8f,p.y); }
        public Vector3 CartPickup(BaggageFleet fleet,int worker)
        {
            var load=fleet.Station.cartType.baggage;
            var local=new Vector3(load.loadMax.x-.2f,load.loadMin.y,Mathf.Lerp(load.loadMin.z+.4f,load.loadMax.z-.4f,worker==0 ? .4f : .7f));
            var p=fleet.CartPose.Point(local); return new Vector3(p.x,local.y,p.y);
        }
        public BaggageCrew.BagPose BagAt(BaggageFleet fleet,float elapsed,int bagIndex)
        {
            var job=fleet.Job; bool incoming=fleet.IsCart(job.TransferFrom); int worker=bagIndex%fleet.Crew.GroundWorkers;
            var feed=Feed(fleet,!incoming); var door=Door(fleet.Station.depotBay);
            var inside=new Vector3(door.x,.8f,door.y)+(incoming ? -1 : 1)*new Vector3(Facing.x,0,Facing.y)*.9f;
            float conveyor=ConveyorSeconds;
            var result=new BaggageCrew.BagPose { holder=-1,worker=worker,rotation=Quaternion.LookRotation(new Vector3(Right.x,0,Right.y)) };
            if (incoming && elapsed<3.6f || !incoming && elapsed>=conveyor)
            {
                float t=Mathf.Clamp01((incoming ? elapsed : elapsed-conveyor)/3.6f);
                var a=incoming ? CartPickup(fleet,worker) : feed; var b=incoming ? feed : CartPickup(fleet,worker);
                result.bottom=Vector3.Lerp(a,b,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.2f); result.holder=0;
            }
            else result.bottom=Vector3.Lerp(incoming ? feed : inside,incoming ? inside : feed,
                Mathf.Clamp01((incoming ? elapsed-3.6f : elapsed)/conveyor));
            return result;
        }
        public AirportMaintenance.Box BuildingBox => AirportMaintenance.Box.Of(Point(17,(Maximum.teams-1)*12f-5f),Facing,
            new Bounds(Vector3.zero,new Vector3(12,5,Maximum.teams*24f-6f)),.5f);

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
            for (float z=-22;z<=(Maximum.teams-1)*24f+20;z+=2)
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
            int capacity=Maximum.trailers*200;
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
            if (hiredWorkers>limit.workers || Teams.Length>limit.teams || Trailers>limit.trailers)
            { reason="The smaller depot cannot accommodate the current staff and equipment."; return false; }
            int capacity=limit.trailers*200;
            if (dispatch!=null && (dispatch.Count>capacity || arrivals.Count>capacity || dispatch.Weight>capacity*30f || arrivals.Weight>capacity*30f))
            { reason="Move the stored luggage before reducing depot capacity."; return false; }
            var previous=size; size=requested;
            reason=PlacementProblem(); if (reason!=null) { size=previous; return false; }
            if (dispatch!=null) { dispatch.Resize(capacity,capacity*30f); arrivals.Resize(capacity,capacity*30f); }
            Rebuild(); return true;
        }
        public bool CanAddTrailer(BaggageStation station,out string reason)
        {
            reason=null;
            if (station.depot!=this || Trailers>=Maximum.trailers) { reason="Depot trailer capacity reached."; return false; }
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
        public void ReleaseCrew(BaggageFleet fleet) => assignments.Remove(fleet);

        internal int VisibleCount(BaggageStore store)
        {
            int count=store.Count;
            foreach (var job in Airport.Baggage.Jobs)
            {
                if (!job.DepotTransfer || job.Fleet.Station.depot!=this) continue;
                if (job.TransferFrom==store) count-=Mathf.Clamp(Mathf.FloorToInt(job.TransferSeconds/job.TransferInterval)+1,0,job.TransferBatch.Count);
                if (job.TransferTo==store && job.TransferSeconds>=job.BagTravelSeconds)
                    count+=Mathf.Clamp(Mathf.FloorToInt((job.TransferSeconds-job.BagTravelSeconds)/job.TransferInterval)+1,0,job.TransferBatch.Count);
            }
            return Mathf.Max(0,count);
        }

        /// <summary>Each added team has a distinct forward-facing depot bay and its own equipment lease.</summary>
        public BaggageStation AddTeam(BaggageStation source,out string reason)
        {
            reason=null; var teams=Teams;
            if (source==null || source.Airport!=Airport) { reason="Choose a baggage team at this airport."; return null; }
            if (teams.Length>=Maximum.teams || Trailers+source.cartCount>Maximum.trailers)
            { reason="Depot team or trailer capacity reached."; return null; }
            reason=PlacementProblem(); if (reason!=null) return null;
            Rebuild();
            int bay=Enumerable.Range(0,Maximum.teams).First(i=>!teams.Any(t=>t.depotBay==i));
            var go=new GameObject(name+" team "+(bay+1)); go.SetActive(false); go.transform.SetParent(Airport.transform,false);
            var team=go.AddComponent<BaggageStation>();
            team.tractorType=source.tractorType; team.cartType=source.cartType; team.loaderType=source.loaderType;
            team.tractorPrefab=source.tractorPrefab; team.cartPrefab=source.cartPrefab; team.loaderPrefab=source.loaderPrefab;
            team.bagPrefabs=source.bagPrefabs; team.bagPropType=source.bagPropType; team.crewPrefab=source.crewPrefab; team.crewType=source.crewType;
            team.cartCount=source.cartCount; team.workersPerTeam=source.workersPerTeam;
            var hold=source.automaticHold;
            team.automaticHold=new BulkHoldConfiguration { aircraft=hold.aircraft,cargoPoint=hold.cargoPoint,bagCapacity=hold.bagCapacity,weightCapacity=hold.weightCapacity };
            team.standIds=new List<int>(source.standIds); team.incomingBags=source.incomingBags; team.outgoingBags=source.outgoingBags;
            team.kilogramsPerBag=source.kilogramsPerBag; team.automaticHandling=source.automaticHandling;
            team.depot=this; team.depotBay=bay; team.parkAtStand=false;
            ConnectBay(team); go.SetActive(true); Airport.Taxiways.EquipmentPaintChanged(); return team;
        }

        int ConnectPort(int index)
        {
            var net=Airport.Taxiways;
            int Node(Vector2 at,bool spine)
            {
                var existing=net.nodes.FirstOrDefault(n=>n.runway==null && Vector2.Distance(net.At(n.id),at)<.05f);
                if (existing!=null) return existing.id;
                var road=net.edges.FirstOrDefault(e=>e.road==RoadClass.Service && DistanceToSegment(at,net.At(e.a),net.At(e.b))<.05f);
                if (road!=null) return net.SplitEdge(road,new Vector3(at.x,0,at.y));
                int id=net.AddNode(new Vector3(at.x,0,at.y));
                if (spine)
                {
                    var join=net.nodes.Where(n=>n.id!=id && n.runway==null && Mathf.Abs(Vector2.Dot(net.At(n.id)-firstBay,Right))<.1f &&
                        net.edges.Any(e=>e.IsRoad && (e.a==n.id || e.b==n.id))).OrderBy(n=>Vector2.SqrMagnitude(net.At(n.id)-at)).First();
                    net.Connect(join.id,id,12,PavementSurface.Concrete,false,RoadClass.Service);
                }
                return id;
            }
            float z=index*24f;
            int entry=Node(Point(0,z-22),true),exit=Node(Point(0,z+14),true);
            int tail=Node(Point(4,z-12),false),bay=Node(Bay(index),false);
            foreach (var edge in net.edges.Where(e=>(e.a==bay || e.b==bay) &&
                Vector2.Dot(net.At(TaxiwayNetwork.Other(e,bay))-Bay(index),Facing)>.1f &&
                Mathf.Abs(Vector2.Dot(net.At(TaxiwayNetwork.Other(e,bay))-Bay(index),Right))>.1f).ToArray())
                net.RemoveEdge(edge.id);
            int leave=Node(Point(4,z+4),false);
            foreach (var pair in new[] { (entry,tail),(tail,bay),(bay,leave),(leave,exit) })
            {
                foreach (var direct in net.edges.Where(e=>e.a==pair.Item1 && e.b==pair.Item2 || e.b==pair.Item1 && e.a==pair.Item2).ToArray())
                    if (Connected(pair.Item1,pair.Item2,direct)) net.RemoveEdge(direct.id);
                if (!Connected(pair.Item1,pair.Item2))
                    net.Connect(pair.Item1,pair.Item2,4.5f,PavementSurface.Concrete,false,RoadClass.Service);
            }
            return bay;
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
            int bay=ConnectPort(team.depotBay);
            team.dispatchNode=team.arrivalsNode=bay; team.dispatchBearing=team.arrivalsBearing=bearing;
            if (!team.parkAtStand) { team.loaderParking=Point(28,team.depotBay*24f); team.loaderParkingBearing=bearing; }
            bayLayoutVersion=3;
        }
        static float DistanceToSegment(Vector2 p,Vector2 a,Vector2 b) => Vector2.Distance(p,
            Vector2.Lerp(a,b,Mathf.Clamp01(Vector2.Dot(p-a,b-a)/Mathf.Max(.001f,(b-a).sqrMagnitude))));

        public void Rebuild()
        {
            if (!isActiveAndEnabled || Airport==null) return;
            var net=Airport.Taxiways; int nodes=net.nodes.Count,edges=net.edges.Count;
            for (int i=0;i<Maximum.teams;i++) ConnectPort(i);
            if (nodes!=net.nodes.Count || edges!=net.edges.Count) net.Rebuild();
            presentation ??= new BaggageDepotPresentation(this);
            presentation.Build(); dirty=false;
            int layout=System.HashCode.Combine(firstBay,bearing,size);
            if (built && layout!=builtLayout) { Airport.LayoutChanged(); Airport.Taxiways.EquipmentPaintChanged(); }
            built=true; builtLayout=layout;
        }
        void OnEnable() => dirty=true;
        void OnValidate() => dirty=true;
        void Update() { if (dirty) Rebuild(); }
        public void Present() { if (presentation==null) Rebuild(); presentation?.Present(); }
        void LateUpdate() { if (Application.isPlaying) Present(); }
        void OnDisable() { presentation?.Destroy(); presentation=null; }
    }
}
