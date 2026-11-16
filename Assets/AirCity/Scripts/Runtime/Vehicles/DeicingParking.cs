using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    [Serializable]
    public sealed class DeicingParkingBay
    {
        public const float Width=4.5f, Length=13f;
        public int id, standId, supplyAreaId;
        public Vector2 position;
        public float heading;
        public bool legacy;
        public string Name => "Truck parking " + id;
        public Vector2 Axis => new Vector2(Mathf.Sin(heading*Mathf.Deg2Rad),Mathf.Cos(heading*Mathf.Deg2Rad));
        public Vector2 Right => Geo.Right(Axis);
        public Vector2 Point(float x,float z) => position+Right*x+Axis*z;
        public Vector2[] Outline => new[] { Point(-Width/2,-Length/2),Point(Width/2,-Length/2),Point(Width/2,Length/2),Point(-Width/2,Length/2) };
        public bool Contains(Vector2 point) { var delta=point-position; return Mathf.Abs(Vector2.Dot(delta,Right))<=Width/2 && Mathf.Abs(Vector2.Dot(delta,Axis))<=Length/2; }
        public BaggageVehiclePose Pose(VehicleType type) => new BaggageVehiclePose(position-Right*type.bounds.center.x-Axis*type.bounds.center.z,Axis);
    }

    public sealed partial class AirportDeicing
    {
        public const double ParkingBayPrice=1000d;
        public const float TruckDeliveryLeadSeconds=30f;
        public IReadOnlyList<DeicingParkingBay> ParkingBays { get { MigrateParking(); return airport.deicingParkingBays; } }
        public int DeliveredTruckCount(DeicingFacility facility)
        {
            if(facility==null) return 0; EnsureParkingFor(facility); int count=0;
            foreach(var truck in facility.trucks) if(truck.IsDelivered) count++;
            return count;
        }
        public string PadRemovalProblem(Stand stand)
        {
            var facility=For(stand);
            if(facility?.trucks!=null)
                foreach(var truck in facility.trucks) if(truck.DeliveryPending) return "wait for the paid truck order to arrive before removing its pad";
            return null;
        }
        public DeicingParkingBay ParkingBayFor(DeicingTruckState truck) => truck==null?null:airport.deicingParkingBays.Find(b=>b.id==truck.parkingBayId);
        public DeicingTruckState TruckFor(DeicingParkingBay bay)
        {
            if(bay==null) return null;
            foreach(var facility in Facilities) if(facility?.trucks!=null) foreach(var truck in facility.trucks) if(truck.parkingBayId==bay.id) return truck;
            return null;
        }
        public BaggageVehiclePose ParkingPose(DeicingTruckState truck)
        {
            var bay=ParkingBayFor(truck);
            return bay!=null?bay.Pose(TruckType):DeicingMotion.ParkPosition(Layout(truck.facility),truck.index,TruckType);
        }
        public Vector2 ParkingPosition(DeicingParkingBay bay) => bay.Pose(TruckType).position;
        int NextParkingId() { int id=1; foreach(var bay in airport.deicingParkingBays) id=Mathf.Max(id,bay.id+1); return id; }
        void MigrateParking()
        {
            // Early deicing saves manufactured marked bays around every existing truck. Those were never
            // player construction: keep the truck's original home pose, but remove the invented capacity.
            // A paid inbound order must retain its reserved destination even in such an early save.
            for(int i=airport.deicingParkingBays.Count-1;i>=0;i--)
            {
                var bay=airport.deicingParkingBays[i]; if(bay==null || !bay.legacy) continue;
                var assigned=TruckFor(bay);
                if(assigned!=null && assigned.DeliveryPending) { bay.legacy=false; continue; }
                foreach(var facility in Facilities) if(facility?.trucks!=null)
                    foreach(var truck in facility.trucks) if(truck.parkingBayId==bay.id) truck.parkingBayId=0;
                airport.deicingParkingBays.RemoveAt(i);
            }
            foreach(var facility in Facilities) if(facility!=null) EnsureParkingFor(facility);
        }
        void EnsureParkingFor(DeicingFacility facility)
        {
            var layout=Layout(facility); var type=TruckType; if(layout==null || type==null) return;
            facility.trucks??=new List<DeicingTruckState>();
            while(facility.trucks.Count<Mathf.Clamp(facility.machines,0,MaxMachines)) facility.trucks.Add(new DeicingTruckState());
            for(int i=0;i<facility.trucks.Count;i++)
            {
                var truck=facility.trucks[i]; truck.facility=facility; truck.index=i;
                if(!truck.DeliveryPending && ParkingBayFor(truck)==null) truck.parkingBayId=0;
            }
        }
        public string ParkingBayProblem(Vector2 position,float heading,DeicingParkingBay except=null)
        {
            if(float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsNaN(heading) || float.IsInfinity(heading)) return "choose a valid parking position";
            var bay=new DeicingParkingBay { position=position,heading=heading }; var outline=bay.Outline;
            bool OnPavement(Vector2 point)
            {
                foreach(var apron in airport.aprons) if(apron.surface.IsPaved() && apron.Contains(point)) return true;
                foreach(var pad in airport.Taxiways.Layouts()) if(pad.stand.DeIcing && pad.Contains(point)) return true;
                return false;
            }
            for(float x=-DeicingParkingBay.Width/2;x<=DeicingParkingBay.Width/2+.01f;x+=DeicingParkingBay.Width/3f)
                for(float z=-DeicingParkingBay.Length/2;z<=DeicingParkingBay.Length/2+.01f;z+=DeicingParkingBay.Length/7f)
                    if(!OnPavement(bay.Point(x,z))) return "pave the whole truck parking bay first";
            foreach(var other in ParkingBays) if(other!=except && Geo.Overlap(outline,other.Outline)) return "overlaps another truck parking bay";
            foreach(var area in WorkAreas) if(Geo.Overlap(outline,area.Outline)) return "keep parking clear of the supply and refill lanes";
            foreach(var pad in airport.Taxiways.Layouts())
            {
                if(!Geo.Overlap(outline,pad.Pad)) continue;
                if(!pad.stand.DeIcing) return "keep truck parking off aircraft stands";
                float half=RunwayRules.MaxWingspan(pad.Code)*.5f+RunwayRules.StandClearance(pad.Code);
                var lane=new[] { pad.At(-half,pad.near0),pad.At(half,pad.near0),pad.At(half,pad.far),pad.At(-half,pad.far) };
                if(Geo.Overlap(outline,lane)) return "keep truck parking outside the aircraft lane";
            }
            foreach(var edge in airport.Taxiways.edges)
            {
                var a=airport.Taxiways.At(edge.a); var b=airport.Taxiways.At(edge.b); if((b-a).sqrMagnitude<.01f) continue;
                var right=Geo.Right((b-a).normalized)*edge.width*.5f;
                if(Geo.Overlap(outline,new[] { a+right,b+right,b-right,a-right })) return "keep parking clear of driving lanes";
            }
            foreach(var runway in airport.Runways)
            {
                var a=Geo.XZ(runway.start); var b=Geo.XZ(runway.end); var right=Geo.Right(runway.Axis2D)*runway.HalfWidthTotal;
                if(Geo.Overlap(outline,new[] { a+right,b+right,b-right,a-right })) return "keep parking off the runway";
            }
            foreach(var terminal in airport.terminals) foreach(var footprint in terminal.Footprints()) if(Geo.Overlap(outline,footprint)) return "overlaps a terminal";
            foreach(var depot in airport.Depots) if(Geo.Overlap(outline,depot.Corners)) return "overlaps a maintenance depot";
            var box=AirportMaintenance.Box.Of(position,bay.Axis,new Bounds(Vector3.zero,new Vector3(DeicingParkingBay.Width,1f,DeicingParkingBay.Length)),.2f);
            foreach(var depot in airport.GetComponentsInChildren<BaggageDepot>()) if(box.Overlaps(depot.BuildingBox)) return "overlaps a baggage depot";
            return null;
        }
        public DeicingParkingBay AddParkingBay(Vector2 position,float heading)
        {
            LastPurchaseProblem=ParkingBayProblem(position,heading); if(LastPurchaseProblem!=null) return null;
            if(!Purchase(ParkingBayPrice,DeicingExpense.Capital,out var problem)) { LastPurchaseProblem=problem; return null; }
            var bay=new DeicingParkingBay { id=NextParkingId(),position=position,heading=heading };
            airport.deicingParkingBays.Add(bay); return bay;
        }
        bool CanChangeParking(DeicingTruckState truck,out string problem)
        {
            problem=truck==null?null:Layout(truck.facility)==null || !truck.facility.trucks.Contains(truck)?"choose an airport truck":
                truck.DeliveryPending?"wait for the ordered truck to arrive":
                !truck.facility.crewOnDuty?"call the crew onto shift before moving a truck":
                truck.Drive==null || !truck.Drive.Finished || truck.Stage==DeicingTruckStage.Filling || truck.refillArea!=null ||
                (JobFor(truck.facility) is DeicingJob job && !job.Completed)?"wait for truck work to finish":null;
            return problem==null;
        }
        public bool CanAssignParkingBay(DeicingTruckState truck,out string problem)
        {
            if(truck==null) { problem="choose an airport truck"; return false; }
            return CanChangeParking(truck,out problem);
        }
        public bool MoveParkingBay(DeicingParkingBay bay,Vector2 position,float heading,out string problem)
        {
            problem=bay==null || !airport.deicingParkingBays.Contains(bay)?"choose a truck parking bay":ParkingBayProblem(position,heading,bay);
            if(problem!=null || !CanChangeParking(TruckFor(bay),out problem)) return false;
            bay.position=position; bay.heading=heading; bay.legacy=false; return true;
        }
        public bool RemoveParkingBay(DeicingParkingBay bay,out string problem)
        {
            problem=bay==null || !airport.deicingParkingBays.Contains(bay)?"choose a truck parking bay":TruckFor(bay)!=null?"assign its truck to another bay first":null;
            if(problem!=null) return false;
            airport.deicingParkingBays.Remove(bay); return true;
        }
        public bool AssignParkingBay(DeicingFacility facility,DeicingParkingBay bay)
        {
            if(Layout(facility)==null || bay==null || !airport.deicingParkingBays.Contains(bay) || TruckFor(bay)!=null) return false;
            bay.standId=facility.standId; return true;
        }
        public bool AssignParkingBay(DeicingTruckState truck,DeicingParkingBay bay,out string problem)
        {
            MigrateParking();
            problem=truck==null || bay==null || !airport.deicingParkingBays.Contains(bay)?"choose a truck and parking bay":
                TruckFor(bay)!=null && TruckFor(bay)!=truck?"that parking bay already has a truck":null;
            if(problem!=null || !CanChangeParking(truck,out problem)) return false;
            if(ParkingBayFor(truck)==bay) return true;
            var goal=bay.Pose(TruckType);
            var path=DeicingMotion.PlanLocalJourney(airport,TruckType,truck.Drive.Pose,goal,pose=>ClearOfOthers(truck,pose))??
                DeicingMotion.PlanRoadJourney(airport,TruckType,truck.Drive.Pose,goal,null,null,pose=>ClearOfOthers(truck,pose));
            if(path==null) { problem="connect a clear paved route to that parking bay"; return false; }
            var previous=ParkingBayFor(truck);
            truck.parkingBayId=bay.id; bay.standId=truck.facility.standId; bay.supplyAreaId=truck.facility.workAreaId;
            if(previous!=null) { previous.standId=0; previous.supplyAreaId=0; }
            truck.destination=goal; truck.arrivalStage=DeicingTruckStage.Parked; truck.routeSurfaceStamp=surfaceStamp;
            truck.retry=truck.blocked=0f; truck.failedRoute=false; truck.Waiting=null;
            truck.Drive.Begin(path); truck.Stage=truck.Drive.Finished?DeicingTruckStage.Parked:DeicingTruckStage.Clearing;
            return true;
        }
        public bool OrderTruck(DeicingFacility facility,DeicingParkingBay bay,out string problem)
        {
            MigrateParking();
            problem=Layout(facility)==null?"choose a deicing pad":bay==null || !airport.deicingParkingBays.Contains(bay)?"place and choose a truck parking bay":
                facility.machines>=MaxMachines?"this pad already owns two trucks":TruckFor(bay)!=null?"that parking bay is occupied":
                JobFor(facility) is DeicingJob job && !job.Completed?"wait for the current aircraft to finish":null;
            if(problem!=null) return false;
            if(TruckType==null) { problem="deicing truck equipment is unavailable"; return false; }
            if(!TryOutsideVehicleRoute(TruckType,bay.Pose(TruckType),null,out _,out _)) { problem="connect a clear paved route from an outside gate to this parking bay"; return false; }
            if(!Purchase(TruckPrice,DeicingExpense.Capital,out problem)) return false;
            var ordered=new DeicingTruckState { facility=facility,index=facility.trucks.Count,parkingBayId=bay.id,awaitingDelivery=true,truckDeliverySeconds=TruckDeliveryLeadSeconds };
            facility.trucks.Add(ordered); facility.machines=facility.trucks.Count; bay.standId=facility.standId; return true;
        }
        void TickTruckOrders(float dt)
        {
            foreach(var facility in Facilities)
            {
                if(facility?.trucks==null) continue;
                foreach(var truck in facility.trucks)
                {
                    if(!truck.DeliveryPending) continue;
                    truck.IsSpraying=false; truck.Waiting=null;
                    var bay=ParkingBayFor(truck);
                    if(bay==null) { truck.Waiting="choose parking for the ordered truck"; continue; }
                    if(truck.truckDeliverySeconds>0f) { truck.truckDeliverySeconds=Mathf.Max(0f,truck.truckDeliverySeconds-dt); truck.Waiting="supplier preparing the new truck"; continue; }
                    var goal=bay.Pose(TruckType);
                    if(truck.Drive==null)
                    {
                        truck.retry-=dt; if(truck.retry>0f) { truck.Waiting="waiting for a clear outside delivery route"; continue; }
                        if(DeliveryTransitBlocksRefill() || SupplyRoadBusy(null) || !TryOutsideVehicleRoute(TruckType,goal,truck,out var entry,out var path)) { truck.retry=2f; truck.Waiting="new truck waiting for its outside road connection"; continue; }
                        truck.Drive=new DeicingDrive(entry,TruckType); truck.Drive.Begin(path); truck.routeSurfaceStamp=surfaceStamp; truck.Stage=DeicingTruckStage.Delivering;
                        continue;
                    }
                    if(truck.routeSurfaceStamp!=surfaceStamp)
                    {
                        truck.routeSurfaceStamp=surfaceStamp; truck.Drive.StopAndClear(); truck.failedRoute=false;
                    }
                    if(truck.Drive.Finished && Vector2.Distance(truck.Position,goal.position)>.3f)
                    {
                        if(!OutsideRoadNodes().Contains(NearestServiceNode(bay.position))) { truck.Waiting="new truck waiting for its outside road connection"; continue; }
                        if(DeliveryTransitBlocksRefill() || SupplyRoadBusy(null)) { truck.Waiting="new truck waiting for ground traffic"; continue; }
                        truck.retry-=dt; if(truck.retry>0f) continue;
                        var path=DeicingMotion.PlanLocalJourney(airport,TruckType,truck.Drive.Pose,goal,pose=>ClearOfOthers(truck,pose))??
                            DeicingMotion.PlanRoadJourney(airport,TruckType,truck.Drive.Pose,goal,null,null,pose=>ClearOfOthers(truck,pose));
                        if(path==null) { truck.retry=2f; truck.Waiting="new truck waiting for a clear paved route"; continue; }
                        truck.Drive.Begin(path);
                    }
                    if(!truck.Drive.Finished)
                    {
                        float d=Mathf.Min(truck.Drive.Path.Length,truck.Drive.Along+Mathf.Max(.05f,Mathf.Abs(truck.Speed)*dt+.02f));
                        var at=truck.Drive.Path.Pose(d,out var facing,out _); var next=new BaggageVehiclePose(at,facing);
                        if(!ClearOfOthers(truck,next) || !DeicingMotion.StaticClear(airport,TruckType,next)) { truck.Drive.StopAndClear(); truck.retry=1f; truck.Waiting="new truck waiting for ground traffic"; continue; }
                        truck.Drive.Step(dt); continue;
                    }
                    truck.awaitingDelivery=false; truck.Stage=DeicingTruckStage.Parked; truck.Waiting=null;
                }
            }
        }
        int NearestServiceNode(Vector2 point)
        {
            int node=0; float best=float.PositiveInfinity;
            foreach(var edge in airport.Taxiways.edges) if(OpenService(edge))
            {
                var near=Geo.ClosestOnSegment(point,airport.Taxiways.At(edge.a),airport.Taxiways.At(edge.b),out _); float d=(near-point).sqrMagnitude;
                if(d<best) { best=d; node=edge.a; }
            }
            return node;
        }
    }
}
