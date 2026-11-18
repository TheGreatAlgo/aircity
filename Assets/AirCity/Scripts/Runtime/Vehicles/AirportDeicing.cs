using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Traffic;
using UnityEngine;

namespace AirCity.Vehicles
{
    [Serializable]
    public sealed class DeicingFacility
    {
        public int standId, workAreaId;
        [Range(0, 2)] public int machines;
        public bool crewOnDuty = true;
        [HideInInspector] public int staffingVersion;
        public List<DeicingTruckState> trucks = new List<DeicingTruckState>();
        // Original scene compatibility only. Migrated into a separate work area, never applied to an aircraft.
        [HideInInspector] public float tankCapacityLitres, fluidLitres, deliveryLitres, deliverySecondsRemaining;
        public bool Installed => machines > 0;
        public bool DeliveryPending => deliveryLitres > 0f;
    }

    /// <summary>A separate, player-placed storage and truck-refill apron. All points are airport-local metres.</summary>
    [Serializable]
    public sealed class DeicingWorkArea
    {
        public const float Width = 20f, Length = 24f;
        public int id;
        public Vector2 position;
        public float heading;
        public float capacityLitres = AirportDeicing.TankCapacityLitres;
        public float fluidLitres, deliveryLitres, deliverySecondsRemaining;
        public bool recyclingInstalled;
        public float runoffLitres, recyclingDelayRemaining;
        public double recycledLitres, collectedLitres;
        public bool DeliveryPending => deliveryLitres > 0f;
        public string Name => "Deicing supply " + id;
        public Vector2 Axis => new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
        public Vector2 Right => Geo.Right(Axis);
        public Vector2 Point(float x, float z) => position + Right * x + Axis * z;
        public Vector2[] Outline => new[] { Point(-Width/2, -Length/2), Point(Width/2, -Length/2), Point(Width/2, Length/2), Point(-Width/2, Length/2) };
        public Vector2[] EquipmentOutline => new[] { Point(-2.7f,-4.5f), Point(2.7f,-4.5f), Point(2.7f,8f), Point(-2.7f,8f) };
        public Vector2 TankPosition => position;
        public Vector2 RecyclerPosition => Point(0f, 6.5f);
        public Vector2 RecyclerFacing => Right;
        public Vector2 FillCentre => Point(6.25f, 0f);
        public Vector2 UnloadCentre => Point(-6.25f, 0f);
        public Vector2 FillEntry => Point(6.25f, -Length/2);
        public Vector2 FillExit => Point(6.25f, Length/2);
        public Vector2 UnloadEntry => Point(-6.25f, Length/2);
        public Vector2 UnloadExit => Point(-6.25f, -Length/2);
        public Vector2[] RoadPorts => new[] { FillEntry, FillExit, UnloadEntry, UnloadExit };
        public static string RoadPortName(int index) => index switch
        { 0 => "FILL entry", 1 => "FILL exit", 2 => "UNLOAD entry", _ => "UNLOAD exit" };
        public Vector2 Entrance => FillEntry;
        public Vector2 RefillPosition(int index) => FillCentre;
        public BaggageVehiclePose RefillPose(VehicleType type) => ServicePose(FillCentre, Axis, type);
        public BaggageVehiclePose UnloadPose(VehicleType type) => ServicePose(UnloadCentre, -Axis, type);
        static BaggageVehiclePose ServicePose(Vector2 centre, Vector2 facing, VehicleType type)
        {
            var offset = type != null ? type.bounds.center : Vector3.zero;
            return new BaggageVehiclePose(centre - facing * offset.z - Geo.Right(facing) * offset.x, facing);
        }
        public bool Contains(Vector2 point)
        {
            var delta = point-position;
            return Mathf.Abs(Vector2.Dot(delta,Right)) <= Width/2 && Mathf.Abs(Vector2.Dot(delta,Axis)) <= Length/2;
        }
    }

    public enum DeicingStage { Preparing, Spraying, Clearing, Complete }
    public enum DeicingTruckStage { Parked, ToWorkArea, Filling, ToAircraft, Working, Clearing, Waiting, Delivering }

    [Serializable]
    public sealed class DeicingTruckState
    {
        [Min(0f)] public float onboardLitres;
        public int parkingBayId;
        public bool awaitingDelivery;
        public float truckDeliverySeconds;
        public bool DeliveryPending => awaitingDelivery;
        public bool IsDelivered => !awaitingDelivery;
        [NonSerialized] public DeicingFacility facility;
        [NonSerialized] public int index;
        public DeicingTruckStage Stage { get; internal set; }
        public DeicingDrive Drive { get; internal set; }
        public bool IsSpraying { get; internal set; }
        public Vector3 Target { get; internal set; }
        public string Waiting { get; internal set; }
        public float TreatmentProgress => job != null ? job.Progress : 0f;
        public float CapacityLitres => AirportDeicing.TruckCapacityLitres;
        public float FluidLitres => onboardLitres;
        public DeicingTruckStage Phase => Stage;
        public Vector2 Position => Drive != null ? Drive.Position : Vector2.zero;
        public Vector2 Facing => Drive != null ? Drive.Forward : Vector2.up;
        public float Speed => Drive != null ? Drive.Speed : 0f;
        public string Status => Waiting ?? (DeliveryPending ? Drive==null?"new truck awaiting delivery":"new truck arriving" : Stage switch
        {
            DeicingTruckStage.ToWorkArea => "driving to refill",
            DeicingTruckStage.Filling => "refilling",
            DeicingTruckStage.ToAircraft => "positioning at aircraft",
            DeicingTruckStage.Working => IsSpraying ? "spraying wings" : "working",
            DeicingTruckStage.Clearing => "returning clear",
            DeicingTruckStage.Waiting => "waiting",
            _ => "parked",
        });
        public int TargetIndex => targetIndex;
        public int TargetCount => targets?.Count ?? 0;
        public float TargetProgress => targetDose > 0f ? Mathf.Clamp01(targetUsed/targetDose) : 0f;
        public bool DepartureClear { get; internal set; }
        internal DeicingJob job;
        internal List<Vector3> targets;
        internal int targetIndex;
        internal float targetUsed, targetDose, retry, blocked, clearSince = -1f;
        internal DeicingTruckStage arrivalStage;
        internal BaggageVehiclePose destination;
        internal DeicingWorkArea refillArea;
        internal bool failedRoute;
        internal int failedSignature;
        internal int routeSurfaceStamp;
    }

    public sealed class DeicingJob
    {
        public readonly Aircraft.Aircraft aircraft;
        public readonly DeicingFacility facility;
        public readonly float requiredLitres, SpraySeconds;
        internal readonly List<DeicingTruckState> trucks = new List<DeicingTruckState>();
        public float UsedLitres { get; internal set; }
        public float Progress => Mathf.Clamp01(UsedLitres / requiredLitres);
        public DeicingStage Stage { get; internal set; }
        public float StageProgress { get; internal set; }
        public bool Completed => Stage == DeicingStage.Complete;
        public bool IsSpraying { get; internal set; }
        public string Waiting { get; internal set; }
        internal DeicingJob(Aircraft.Aircraft aircraft, DeicingFacility facility)
        {
            this.aircraft = aircraft; this.facility = facility;
            foreach (var truck in facility.trucks) if (truck.IsDelivered) trucks.Add(truck);
            requiredLitres = Mathf.Max(80f, aircraft.type.bounds.size.x * aircraft.type.bounds.size.z * .32f);
            SpraySeconds = Mathf.Lerp(12f, 22f, Mathf.InverseLerp(12f, 80f, aircraft.type.Wingspan));
        }
    }

    public sealed class DeicingStop { public DeicingFacility facility; public float at; }

    /// <summary>Headless finite stock, naturally driven onboard refills and aircraft treatment.</summary>
    public sealed partial class AirportDeicing
    {
        public const int MaxMachines = 2;
        public const float TankCapacityLitres = 30000f, TruckCapacityLitres = 1200f;
        public const float DeliveryLeadSeconds = 90f, RefillLitresPerSecond = 120f, DeliveryRoadReach = 14f;
        // Retained for source compatibility; stages now finish from actual truck arrival, not these timers.
        public const float PreparingSeconds = 0f, ClearingSeconds = 0f;
        readonly Airport airport;
        readonly Dictionary<DeicingFacility, DeicingJob> jobs = new Dictionary<DeicingFacility, DeicingJob>();
        readonly Dictionary<DeicingWorkArea, DeicingTruckState> refillLeases = new Dictionary<DeicingWorkArea, DeicingTruckState>();
        DeicingAssets assets;
        bool migrating;
        int surfaceStamp;
        public AirportDeicing(Airport airport) => this.airport = airport;
        public IReadOnlyList<DeicingFacility> Facilities => airport.deicingFacilities;
        public IReadOnlyList<DeicingWorkArea> WorkAreas { get { MigrateLegacy(); return airport.deicingWorkAreas; } }
        public float Clock { get; private set; }
        public bool NeedsSimulation => Facilities.Count > 0 || WorkAreas.Count > 0;
        public VehicleType TruckType
        {
            get { if (assets == null) assets = Resources.Load<DeicingAssets>("DeicingAssets"); return assets != null ? assets.truckType : null; }
        }
        public DeicingJob JobFor(DeicingFacility facility) => facility != null && airport.deicingFacilities.Contains(facility) && jobs.TryGetValue(facility, out var job) ? job : null;
        public DeicingFacility For(Stand stand) => stand == null ? null : airport.deicingFacilities.Find(f => f != null && f.standId == stand.id);
        public StandLayout Layout(DeicingFacility facility)
        {
            if (facility == null) return null;
            var stand = airport.Taxiways.stands.Find(s => s.id == facility.standId && s.DeIcing);
            return stand != null ? airport.Taxiways.Layout(stand) : null;
        }
        public static Vector2 TankPosition(StandLayout layout) => layout.At(layout.x1 - 3f, layout.far - 5f);
        public bool CompletedFor(Aircraft.Aircraft aircraft)
        { foreach (var facility in Facilities) { var job = JobFor(facility); if (job != null && job.aircraft == aircraft && job.Completed) return true; } return false; }
        public DeicingFacility Configure(Stand stand)
        {
            if (stand == null || !stand.DeIcing || !airport.Taxiways.stands.Contains(stand)) return null;
            var facility = For(stand); if (facility != null) return facility;
            facility = new DeicingFacility { standId = stand.id, staffingVersion = 1 }; airport.deicingFacilities.Add(facility); return facility;
        }
        public bool BuyMachine(DeicingFacility facility) => BuyMachine(facility, out _);
        public bool BuyMachine(DeicingFacility facility, out string problem)
        {
            DeicingParkingBay selected=null;
            foreach (var bay in ParkingBays) if (TruckFor(bay)==null && (bay.standId==0 || facility!=null && bay.standId==facility.standId)) { selected=bay; break; }
            return OrderTruck(facility,selected,out problem);
        }
        public IReadOnlyList<DeicingTruckState> TrucksFor(DeicingFacility facility)
        { if (facility == null) return Array.Empty<DeicingTruckState>(); EnsureTrucks(facility); return facility.trucks; }
        void EnsureTrucks(DeicingFacility facility)
        {
            if (facility.staffingVersion == 0) { facility.crewOnDuty = true; facility.staffingVersion = 1; }
            facility.trucks ??= new List<DeicingTruckState>();
            while (facility.trucks.Count < Mathf.Clamp(facility.machines, 0, MaxMachines)) facility.trucks.Add(new DeicingTruckState());
            while (facility.trucks.Count > Mathf.Clamp(facility.machines, 0, MaxMachines)) facility.trucks.RemoveAt(facility.trucks.Count-1);
            var layout = Layout(facility); var type = TruckType; if (layout == null || type == null) return;
            EnsureParkingFor(facility);
            for (int i = 0; i < facility.trucks.Count; i++)
            {
                var truck = facility.trucks[i]; truck.facility = facility; truck.index = i;
                if (!truck.DeliveryPending) truck.Drive ??= new DeicingDrive(ParkingPose(truck), type);
            }
        }
        public DeicingWorkArea WorkAreaFor(DeicingFacility facility)
        { MigrateLegacy(); return facility == null ? null : airport.deicingWorkAreas.Find(a => a != null && a.id == facility.workAreaId); }
        public bool AssignWorkArea(DeicingFacility facility, DeicingWorkArea area)
        {
            if (Layout(facility) == null || (area != null && !airport.deicingWorkAreas.Contains(area))) return false;
            foreach (var truck in TrucksFor(facility))
                if (truck.Stage == DeicingTruckStage.ToWorkArea || truck.Stage == DeicingTruckStage.Filling ||
                    (truck.arrivalStage == DeicingTruckStage.Filling && truck.Drive != null && !truck.Drive.Finished)) return false;
            facility.workAreaId = area?.id ?? 0; return true;
        }
        public bool InUse(DeicingWorkArea area)
        {
            if (area == null) return false;
            if (DeliveryInUse(area)) return true;
            if (refillLeases.ContainsKey(area)) return true;
            foreach (var facility in Facilities)
                if (facility != null && facility.workAreaId == area.id)
                    foreach (var truck in TrucksFor(facility))
                        if (truck.Stage == DeicingTruckStage.ToWorkArea || truck.Stage == DeicingTruckStage.Filling ||
                            (truck.arrivalStage == DeicingTruckStage.Filling && truck.Drive != null && !truck.Drive.Finished)) return true;
            return false;
        }
        public DeicingWorkArea AddWorkArea(Vector2 position, float heading)
        {
            LastPurchaseProblem = WorkAreaProblem(position, heading);
            if (LastPurchaseProblem != null) return null;
            if (!Purchase(SupplyAreaPrice, DeicingExpense.Capital, out var problem)) { LastPurchaseProblem = problem; return null; }
            LastPurchaseProblem = null;
            var area = new DeicingWorkArea { id = NextWorkAreaId(), position = position, heading = heading };
            airport.deicingWorkAreas.Add(area); return area;
        }
        int NextWorkAreaId() { int id = 1; foreach (var area in airport.deicingWorkAreas) if (area != null) id = Mathf.Max(id, area.id+1); return id; }
        public bool MoveWorkArea(DeicingWorkArea area, Vector2 position, float heading, out string problem)
        {
            problem = area == null || !airport.deicingWorkAreas.Contains(area) ? "choose a de-icing work area" : InUse(area) ? "wait for refilling trucks to clear" : WorkAreaProblem(position, heading, area);
            if (problem != null) return false;
            area.position = position; area.heading = heading; return true;
        }
        public bool RemoveWorkArea(DeicingWorkArea area, out string problem)
        {
            problem = area == null || !airport.deicingWorkAreas.Contains(area) ? "choose a de-icing work area" : InUse(area) ? "wait for refilling trucks to clear" : area.DeliveryPending ? "wait for the outstanding fluid delivery" : null;
            if (problem != null) return false;
            airport.deicingWorkAreas.Remove(area);
            foreach (var facility in Facilities) if (facility != null && facility.workAreaId == area.id) facility.workAreaId = 0;
            return true;
        }
        public string WorkAreaProblem(Vector2 position, float heading, DeicingWorkArea except = null)
        {
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsNaN(heading) || float.IsInfinity(heading)) return "choose a valid ground position";
            var area = new DeicingWorkArea { position = position, heading = heading }; var outline = area.Outline;
            for (float x = -DeicingWorkArea.Width/2; x <= DeicingWorkArea.Width/2; x += 2f)
                for (float z = -DeicingWorkArea.Length/2; z <= DeicingWorkArea.Length/2; z += 2f)
                {
                    bool paved = false; var point = area.Point(x, z);
                    foreach (var apron in airport.aprons) if (apron.surface.IsPaved() && apron.Contains(point, .01f)) { paved = true; break; }
                    if (!paved) return "build a paved apron for the whole work area";
                }
            foreach (var other in airport.deicingWorkAreas) if (other != null && other != except && Geo.Overlap(outline, other.Outline)) return "overlaps another de-icing work area";
            foreach (var bay in ParkingBays) if (Geo.Overlap(outline,bay.Outline)) return "keep the supply work area clear of truck parking";
            foreach (var layout in airport.Taxiways.Layouts()) if (Geo.Overlap(outline, layout.Pad)) return "keep the storage work area off aircraft pads";
            foreach (var runway in airport.Runways)
            {
                var a = Geo.XZ(runway.start); var b = Geo.XZ(runway.end); var right = Geo.Right(runway.Axis2D)*runway.HalfWidthTotal;
                if (Geo.Overlap(outline, new[] { a+right,b+right,b-right,a-right })) return "keep the work area off the runway";
            }
            var tank = area.EquipmentOutline;
            foreach (var edge in airport.Taxiways.edges)
            {
                var a = airport.Taxiways.At(edge.a); var b = airport.Taxiways.At(edge.b); if ((b-a).sqrMagnitude < .01f) continue;
                var right = Geo.Right((b-a).normalized)*edge.width*.5f; var strip = new[] { a+right,b+right,b-right,a-right };
                if (Geo.Overlap(edge.IsRoad ? tank : outline, strip)) return edge.IsRoad ? "keep the tank clear of the service road" : "keep the work area off the taxiway";
            }
            foreach (var terminal in airport.terminals)
                if (terminal != null) foreach (var footprint in terminal.Footprints()) if (Geo.Overlap(outline, footprint)) return "overlaps a terminal building";
            foreach (var depot in airport.Depots)
                if (Geo.Overlap(outline,depot.Corners)) return "overlaps a maintenance depot";
            foreach (var hangar in airport.Hangars)
                if (Geo.Overlap(outline,hangar.Pad)) return "overlaps an aircraft hangar";
            var workBox = AirportMaintenance.Box.Of(position,area.Axis,new Bounds(Vector3.zero,new Vector3(DeicingWorkArea.Width,1f,DeicingWorkArea.Length)),.3f);
            foreach (var depot in airport.GetComponentsInChildren<BaggageDepot>())
                if (workBox.Overlaps(depot.BuildingBox)) return "overlaps a baggage depot";
            foreach (var bridge in airport.Bridges.BaggageExclusions())
                if (workBox.Overlaps(AirportMaintenance.Box.Of(bridge.center,Vector2.up,new Bounds(Vector3.zero,new Vector3(bridge.width,1f,bridge.height)),.3f))) return "overlaps a bridge or concourse";
            return null;
        }
        public bool HasDeliveryAccess(DeicingWorkArea area)
        {
            if (area == null || !airport.deicingWorkAreas.Contains(area)) return false;
            var outside=OutsideRoadNodes();
            foreach (var edge in airport.Taxiways.edges)
            {
                if (!OpenService(edge) || !outside.Contains(edge.a)) continue;
                foreach(var port in new[]{area.UnloadEntry,area.UnloadExit})
                {
                    var near=Geo.ClosestOnSegment(port,airport.Taxiways.At(edge.a),airport.Taxiways.At(edge.b),out _);
                    if(Vector2.Distance(near,port)<=DeliveryRoadReach+edge.width*.5f) return true;
                }
            }
            return false;
        }
        public bool HasDeliveryAccess(DeicingFacility facility) => HasDeliveryAccess(WorkAreaFor(facility));
        public bool OrderFluid(DeicingWorkArea area, float litres, out string problem)
        {
            problem = area == null || !airport.deicingWorkAreas.Contains(area) ? "choose a de-icing work area" : area.DeliveryPending ? "a delivery is already on its way" :
                float.IsNaN(litres) || float.IsInfinity(litres) || litres <= 0f ? "choose a positive delivery volume" :
                litres > OrderCapacity(area)+.001f ? "the delivery would overfill the tank, including queued recycling" : !HasDeliveryAccess(area) ? "connect an open service road from a marked outside gate to the work-area entrance" : DeliveryOrderProblem(area);
            if (problem != null) return false;
            if (!Purchase(DeliveryPrice(litres), DeicingExpense.Fluid, out problem)) return false;
            area.deliveryLitres = litres; area.deliverySecondsRemaining = DeliveryLeadSeconds; return true;
        }
        public bool OrderFluid(DeicingFacility facility, float litres, out string problem) => OrderFluid(WorkAreaFor(facility), litres, out problem);
        // Only old setup code calls this. New players place a separate area through AddWorkArea.
        public bool InstallTank(DeicingFacility facility)
        { if (Layout(facility) == null || WorkAreaFor(facility) != null) return false; facility.tankCapacityLitres = TankCapacityLitres; MigrateLegacy(); return true; }
        void MigrateLegacy()
        {
            if (migrating) return; migrating = true;
            foreach (var facility in Facilities)
            {
                if (facility == null || facility.tankCapacityLitres <= 0f) continue;
                var area = airport.deicingWorkAreas.Find(a => a != null && a.id == facility.workAreaId);
                var layout = Layout(facility); if (layout == null) continue;
                if (area == null)
                {
                    area = new DeicingWorkArea { id = NextWorkAreaId(), position = TankPosition(layout), heading = layout.stand.heading, capacityLitres = facility.tankCapacityLitres };
                    airport.deicingWorkAreas.Add(area); facility.workAreaId = area.id;
                }
                area.fluidLitres += facility.fluidLitres; area.deliveryLitres += facility.deliveryLitres;
                area.deliverySecondsRemaining = Mathf.Max(area.deliverySecondsRemaining, facility.deliverySecondsRemaining);
                facility.tankCapacityLitres = facility.fluidLitres = facility.deliveryLitres = facility.deliverySecondsRemaining = 0f;
            }
            migrating = false;
        }
        public bool RequiresDepartureService(AircraftType type)
        { if (airport.Condition != SurfaceCondition.Snow) return false; foreach (var facility in Facilities) if (facility != null && facility.Installed && Layout(facility)?.edge != null) return true; return false; }
        public bool CanTreat(DeicingFacility facility, AircraftType type)
        { var layout = Layout(facility); return facility != null && DeliveredTruckCount(facility)>0 && type != null && layout?.edge != null && type.SizeCode <= layout.Code && airport.Taxiways.CheckStand(layout.stand,type) == StandProblem.None; }
        public bool StopMatches(TaxiRoute route, DeicingStop stop, AircraftType type)
        {
            if (route?.path == null || stop == null || !airport.deicingFacilities.Contains(stop.facility) || !CanTreat(stop.facility,type)) return false;
            var layout = Layout(stop.facility); int index = Mathf.RoundToInt(stop.at/TaxiPath.Step);
            if (index < 0 || index >= route.path.Count || Vector2.Distance(route.path.NoseAt(index),layout.stop)>1f) return false;
            route.path.Pose(stop.at,out _,out var facing,out _,out _); return Vector2.Dot(facing,layout.axis)>=.995f;
        }
        public void Simulate(float seconds)
        {
            if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            MigrateLegacy();
            for (float left = seconds; left > .00001f;)
            {
                float dt = Mathf.Min(left,.05f); left -= dt; Clock += dt;
                surfaceStamp = SurfaceStamp();
                TickTruckOrders(dt);
                TickDeliveries(dt);
                TickRecycling(dt);
                foreach (var facility in Facilities)
                {
                    if (facility == null) continue; var layout = Layout(facility); if (layout == null) { jobs.Remove(facility); continue; }
                    EnsureTrucks(facility); StepFacility(facility,layout,dt);
                }
                TickOperatingCosts(dt);
                foreach (var facility in new List<DeicingFacility>(jobs.Keys)) if (!airport.deicingFacilities.Contains(facility)) jobs.Remove(facility);
                foreach (var area in new List<DeicingWorkArea>(refillLeases.Keys))
                    if (!airport.deicingFacilities.Contains(refillLeases[area].facility)) refillLeases.Remove(area);
            }
        }
        void StepFacility(DeicingFacility facility, StandLayout layout, float dt)
        {
            if (!facility.crewOnDuty)
            {
                foreach (var truck in TrucksFor(facility)) truck.IsSpraying = false;
                var waitingJob = JobFor(facility);
                if (waitingJob != null && !waitingJob.Completed) { waitingJob.IsSpraying = false; waitingJob.Waiting = "call the deicing crew onto shift"; }
                return;
            }
            var job = JobFor(facility);
            if (job != null && (job.aircraft == null || !job.aircraft.isActiveAndEnabled || !layout.Contains(Geo.XZ(airport.transform.InverseTransformPoint(job.aircraft.transform.position))))) { jobs.Remove(facility); job = null; }
            if (job == null && facility.Installed)
                foreach (var aircraft in Aircraft.Aircraft.All)
                {
                    if (airport.Traffic.FlightOf(aircraft)?.DeicingCompleted == true || !CanTreat(facility,aircraft.type) || !AtStop(aircraft,layout)) continue;
                    job = new DeicingJob(aircraft,facility); jobs[facility] = job; break;
                }
            foreach (var truck in facility.trucks)
            {
                truck.IsSpraying = false;
                if (truck.DeliveryPending || truck.Drive == null) continue;
                var assignment=job!=null && job.trucks.Contains(truck)?job:null;
                if (truck.job != assignment)
                {
                    truck.job = assignment; truck.targets = null; truck.targetIndex = 0; truck.targetUsed = 0f; truck.clearSince = -1f; truck.DepartureClear = false;
                    if (assignment != null && !assignment.Completed)
                    {
                        truck.targets = new List<Vector3>();
                        if (assignment.trucks.Count == 1) { truck.targets.AddRange(DeicingWingCoverage.Create(assignment.aircraft.type,-1)); truck.targets.AddRange(DeicingWingCoverage.Create(assignment.aircraft.type,1)); }
                        else truck.targets.AddRange(DeicingWingCoverage.Create(assignment.aircraft.type,assignment.trucks.IndexOf(truck) == 0 ? -1 : 1));
                    }
                    if (truck.Stage == DeicingTruckStage.Working || truck.Stage == DeicingTruckStage.ToAircraft) truck.Stage = DeicingTruckStage.Parked;
                }
                StepTruck(truck,layout,dt);
            }
            if (job == null || job.Completed) return;
            job.IsSpraying = false; job.Waiting = null; bool allDosed = true, allClear = true;
            foreach (var truck in job.trucks)
            {
                job.IsSpraying |= truck.IsSpraying;
                allDosed &= truck.targets != null && truck.targetIndex >= truck.targets.Count;
                truck.DepartureClear = ClearOfDeparture(truck,job);
                allClear &= truck.DepartureClear;
                if (truck.Waiting != null) job.Waiting ??= truck.Waiting;
            }
            if (allDosed)
            {
                job.Stage = allClear ? DeicingStage.Complete : DeicingStage.Clearing;
                job.StageProgress = allClear ? 1f : 0f;
                job.Waiting = allClear ? null : "waiting for de-icing trucks to clear";
            }
            else
            {
                job.Stage = job.UsedLitres > 0f ? DeicingStage.Spraying : DeicingStage.Preparing;
                job.StageProgress = job.Progress;
                job.Waiting ??= job.IsSpraying ? "de-icing both wings" : "positioning de-icing trucks";
            }
        }
        bool HasWork(DeicingTruckState truck) => truck.job != null && !truck.job.Completed && truck.targets != null && truck.targetIndex < truck.targets.Count;
        void StepTruck(DeicingTruckState truck, StandLayout layout, float dt)
        {
            truck.Waiting = null;
            if (!truck.Drive.Finished)
            {
                if (truck.routeSurfaceStamp != surfaceStamp)
                {
                    truck.routeSurfaceStamp = surfaceStamp;
                    var endAt=truck.Drive.Path.Pose(truck.Drive.Path.Length,out var endFacing,out _);
                    bool usable = DeicingMotion.SurfaceClear(airport,TruckType,new BaggageVehiclePose(endAt,endFacing,truck.Drive.Path.Height(truck.Drive.Path.Length,float.NaN)));
                    for (float d=truck.Drive.Along;d<=truck.Drive.Path.Length;d+=.5f)
                    {
                        var at=truck.Drive.Path.Pose(d,out var direction,out _);
                        if (!DeicingMotion.SurfaceClear(airport,TruckType,new BaggageVehiclePose(at,direction,truck.Drive.Path.Height(d,float.NaN)))) { usable=false; break; }
                    }
                    if (!usable)
                    {
                        truck.Drive.StopAndClear(); truck.failedRoute=false; truck.retry=0f;
                        var moving=truck.arrivalStage==DeicingTruckStage.Filling?DeicingTruckStage.ToWorkArea:
                            truck.arrivalStage==DeicingTruckStage.Working?DeicingTruckStage.ToAircraft:DeicingTruckStage.Clearing;
                        TryDrive(truck,truck.destination,moving,truck.arrivalStage,dt);
                        return;
                    }
                }
                float ahead = Mathf.Min(truck.Drive.Path.Length,truck.Drive.Along + Mathf.Max(.05f,Mathf.Abs(truck.Drive.Speed)*dt+.02f));
                var next = truck.Drive.Path.Pose(ahead,out var facing,out _);
                if (!ClearOfOthers(truck,new BaggageVehiclePose(next,facing,truck.Drive.Path.Height(ahead,float.NaN))) || !DeicingMotion.StaticClear(airport,TruckType,new BaggageVehiclePose(next,facing,truck.Drive.Path.Height(ahead,float.NaN))))
                {
                    truck.Drive.Hold(); truck.blocked += dt; truck.Waiting = "waiting for ground traffic to clear";
                    if (truck.blocked > 1f)
                    {
                        var moving = truck.arrivalStage == DeicingTruckStage.Filling ? DeicingTruckStage.ToWorkArea :
                            truck.arrivalStage == DeicingTruckStage.Working ? DeicingTruckStage.ToAircraft : DeicingTruckStage.Clearing;
                        TryDrive(truck,truck.destination,moving,truck.arrivalStage,dt);
                    }
                    return;
                }
                truck.blocked = 0f;
                truck.Drive.Step(dt);
                if (truck.Drive.Finished) truck.Stage = truck.arrivalStage;
                return;
            }
            var area = WorkAreaFor(truck.facility);
            if (truck.Stage == DeicingTruckStage.Filling)
            {
                if (area == null) { truck.Stage = DeicingTruckStage.Waiting; truck.Waiting = "assign a de-icing refill work area"; return; }
                float fill = Mathf.Min(TruckCapacityLitres-truck.onboardLitres,Mathf.Min(area.fluidLitres,RefillLitresPerSecond*dt));
                area.fluidLitres -= fill; truck.onboardLitres += fill;
                if (truck.onboardLitres >= TruckCapacityLitres-.001f || (area.fluidLitres <= .001f && truck.onboardLitres > .001f))
                {
                    if (DeliveryTransitBlocksRefill(true)) { truck.Waiting = "waiting for the delivery tanker to clear the service road"; return; }
                    truck.Stage = DeicingTruckStage.Parked; truck.retry = 0f;
                }
                else { truck.Waiting = area.fluidLitres <= .001f ? "waiting for fluid delivery at the work area" : "refilling the truck"; return; }
            }
            bool lowIdle = (truck.job == null || truck.job.Completed) && truck.onboardLitres < TruckCapacityLitres*.2f && area != null && area.fluidLitres > .001f;
            if ((truck.onboardLitres <= .001f || lowIdle) && !(truck.job != null && !truck.job.Completed && truck.targets != null && truck.targetIndex >= truck.targets.Count))
            {
                if (area == null) { truck.Stage = DeicingTruckStage.Waiting; truck.Waiting = "assign a de-icing refill work area"; return; }
                if (DeliveryTransitBlocksRefill())
                { truck.Stage = DeicingTruckStage.Waiting; truck.Waiting = "waiting for the delivery tanker to clear the service road"; return; }
                if (refillLeases.TryGetValue(area,out var owner) && owner != truck)
                { truck.Stage = DeicingTruckStage.Waiting; truck.Waiting = "waiting for the other refill truck to return"; return; }
                refillLeases[area] = truck; truck.refillArea = area;
                TryDrive(truck,area.RefillPose(TruckType),DeicingTruckStage.ToWorkArea,DeicingTruckStage.Filling,dt);
                if (truck.Stage == DeicingTruckStage.Waiting && truck.Drive.Finished) ReleaseRefillLease(truck);
                return;
            }
            if (HasWork(truck))
            {
                if (!AtStop(truck.job.aircraft,layout)) { truck.Waiting = "waiting for the aircraft to stop on the pad"; return; }
                var localTarget = truck.targets[truck.targetIndex];
                truck.Target = airport.transform.InverseTransformPoint(truck.job.aircraft.transform.TransformPoint(localTarget));
                var pose = DeicingWingCoverage.WorkingPose(airport,truck.job.aircraft,TruckType,localTarget);
                var localTruck = truck.job.aircraft.transform.InverseTransformPoint(airport.transform.TransformPoint(Geo.X0Z(truck.Position)));
                bool workingHere = truck.Stage == DeicingTruckStage.Working && localTruck.x*localTarget.x>0f &&
                    DeicingWingCoverage.TargetReachable(airport,TruckType,truck.Drive.Pose,truck.Target);
                if (!workingHere && (Vector2.Distance(truck.Drive.Position,pose.position)>.35f || Vector2.Dot(truck.Drive.Forward,pose.facing)<.995f))
                { TryDrive(truck,pose,DeicingTruckStage.ToAircraft,DeicingTruckStage.Working,dt); return; }
                truck.Stage = DeicingTruckStage.Working;
                ReleaseRefillLease(truck);
                int totalTargets = truck.targets.Count * (truck.job.trucks.Count == 1 ? 1 : 2);
                float dose = truck.job.requiredLitres/totalTargets;
                truck.targetDose = dose;
                float rate = truck.job.requiredLitres/(truck.job.SpraySeconds*2f);
                float used = Mathf.Min(dose-truck.targetUsed,Mathf.Min(truck.onboardLitres,rate*dt));
                var previous = localTarget;
                if (truck.targetIndex > 0)
                {
                    var candidate = truck.targets[truck.targetIndex-1];
                    if (Mathf.Abs(candidate.x-localTarget.x)<.01f)
                    {
                        var priorPose = DeicingWingCoverage.WorkingPose(airport,truck.job.aircraft,TruckType,candidate);
                        if (Vector2.Distance(priorPose.position,pose.position)<.1f && Vector2.Dot(priorPose.facing,pose.facing)>.995f)
                            previous = candidate;
                    }
                }
                var previousAt = airport.transform.InverseTransformPoint(truck.job.aircraft.transform.TransformPoint(previous));
                if (!DeicingWingCoverage.TargetReachable(airport,TruckType,truck.Drive.Pose,previousAt)) previous = localTarget;
                var sweep = Vector3.Lerp(previous,localTarget,Mathf.Clamp01((truck.targetUsed+used)/dose));
                truck.Target = airport.transform.InverseTransformPoint(truck.job.aircraft.transform.TransformPoint(sweep));
                if (!DeicingWingCoverage.TargetReachable(airport,TruckType,truck.Drive.Pose,truck.Target))
                { truck.Waiting = "de-icing nozzle cannot reach its wing target"; return; }
                truck.onboardLitres = Mathf.Max(0f,truck.onboardLitres-used); truck.targetUsed += used; truck.job.UsedLitres += used;
                CollectRunoff(truck.facility, used);
                truck.IsSpraying = used > 0f;
                if (truck.targetUsed >= dose-.001f) { truck.targetIndex++; truck.targetUsed = 0f; }
                return;
            }
            if (truck.job != null && truck.targets != null && truck.targetIndex >= truck.targets.Count && truck.clearSince < 0f)
                truck.clearSince = Clock;
            if (truck.job != null && !truck.job.Completed && truck.targets != null && truck.targetIndex >= truck.targets.Count)
            {
                if (!ClearOfDeparture(truck,truck.job))
                {
                    var clear = ClearDestination(truck);
                    if (Vector2.Distance(clear.position,truck.Position)>.15f)
                    { TryDrive(truck,clear,DeicingTruckStage.Clearing,DeicingTruckStage.Clearing,dt); return; }
                }
                truck.Stage = DeicingTruckStage.Clearing;
                return;
            }
            var parked = ParkingPose(truck);
            if (Vector2.Distance(truck.Drive.Position,parked.position)>.35f || Vector2.Dot(truck.Drive.Forward,parked.facing)<.995f)
            { TryDrive(truck,parked,DeicingTruckStage.Clearing,DeicingTruckStage.Parked,dt); return; }
            truck.Stage = DeicingTruckStage.Parked;
            ReleaseRefillLease(truck);
        }
        BaggageVehiclePose ClearDestination(DeicingTruckState truck)
        {
            var aircraft=truck.job.aircraft;
            var origin=Geo.XZ(airport.transform.InverseTransformPoint(aircraft.transform.position));
            var axis=Geo.XZ(airport.transform.InverseTransformDirection(aircraft.transform.forward)).normalized;
            var right=Geo.Right(axis); float side=Vector2.Dot(truck.Position-origin,right)<0f?-1f:1f;
            var outward=right*side; var travel=Vector2.Dot(truck.Facing,outward)>=0f?truck.Facing:-truck.Facing;
            float nearest=float.PositiveInfinity; var body=TruckType.bounds;
            foreach (float x in new[] { body.min.x,body.max.x }) foreach (float z in new[] { body.min.z,body.max.z })
                nearest=Mathf.Min(nearest,Vector2.Dot(truck.Drive.Pose.Point(new Vector3(x,0f,z))-origin,outward));
            float edge=side<0f?-aircraft.type.bounds.min.x:aircraft.type.bounds.max.x;
            float distance=Mathf.Max(0f,edge+1.3f-nearest)/Mathf.Max(.15f,Vector2.Dot(travel,outward));
            return new BaggageVehiclePose(truck.Position+travel*distance,truck.Facing);
        }
        bool ClearOfDeparture(DeicingTruckState truck, DeicingJob job)
        {
            if (truck.clearSince < 0f || Clock-truck.clearSince < .75f) return false;
            var origin = Geo.XZ(airport.transform.InverseTransformPoint(job.aircraft.transform.position));
            var axis = Geo.XZ(airport.transform.InverseTransformDirection(job.aircraft.transform.forward)).normalized;
            var right = Geo.Right(axis); var body = TruckType.bounds; var plane = job.aircraft.type.bounds;
            bool Clear(BaggageVehiclePose pose)
            {
                float minX=float.PositiveInfinity,maxX=float.NegativeInfinity,maxZ=float.NegativeInfinity;
                foreach (float x in new[] { body.min.x,body.max.x }) foreach (float z in new[] { body.min.z,body.max.z })
                {
                    var delta=pose.Point(new Vector3(x,0f,z))-origin;
                    minX=Mathf.Min(minX,Vector2.Dot(delta,right)); maxX=Mathf.Max(maxX,Vector2.Dot(delta,right)); maxZ=Mathf.Max(maxZ,Vector2.Dot(delta,axis));
                }
                return maxX<plane.min.x-1f || minX>plane.max.x+1f || maxZ<plane.min.z-1f;
            }
            if (!Clear(truck.Drive.Pose)) return false;
            if (!truck.Drive.Finished)
            {
                for (float d=truck.Drive.Along;d<truck.Drive.Path.Length;d+=2f)
                { var at=truck.Drive.Path.Pose(d,out var facing,out _); if (!Clear(new BaggageVehiclePose(at,facing,truck.Drive.Path.Height(d,float.NaN)))) return false; }
                var end=truck.Drive.Path.Pose(truck.Drive.Path.Length,out var lastFacing,out _);
                if (!Clear(new BaggageVehiclePose(end,lastFacing))) return false;
            }
            return true;
        }
        void ReleaseRefillLease(DeicingTruckState truck)
        {
            if (truck.refillArea != null && refillLeases.TryGetValue(truck.refillArea,out var owner) && owner == truck)
                refillLeases.Remove(truck.refillArea);
            truck.refillArea = null;
        }
        void TryDrive(DeicingTruckState truck, BaggageVehiclePose destination, DeicingTruckStage moving, DeicingTruckStage arrived, float dt)
        {
            truck.retry -= dt;
            if (truck.retry > 0f) { truck.Waiting = "waiting for a clear paved truck route"; return; }
            int signature = RouteSignature(truck,destination);
            if (truck.failedRoute && truck.failedSignature == signature)
            { truck.Drive.StopAndClear(); truck.Stage = DeicingTruckStage.Waiting; truck.Waiting = "no connected paved route for the de-icing truck"; ReleaseRefillLease(truck); return; }
            var path = DeicingMotion.Plan(airport,TruckType,new BaggageVehiclePose(truck.Drive.Position,truck.Drive.Forward),destination,truck.job?.aircraft,
                pose => ClearOfOthers(truck,pose));
            if (path == null) { truck.Drive.StopAndClear(); truck.failedRoute = true; truck.failedSignature = signature; truck.retry = 2f; truck.Stage = DeicingTruckStage.Waiting; truck.Waiting = "no connected paved route for the de-icing truck"; ReleaseRefillLease(truck); return; }
            truck.failedRoute = false;
            truck.routeSurfaceStamp = surfaceStamp;
            truck.destination = destination; truck.arrivalStage = arrived; truck.Drive.Begin(path); truck.Stage = truck.Drive.Finished ? arrived : moving;
        }
        int RouteSignature(DeicingTruckState self, BaggageVehiclePose destination)
        {
            unchecked
            {
                int hash=surfaceStamp;
                void Add(Vector2 point) { hash=hash*31+Mathf.RoundToInt(point.x*4f); hash=hash*31+Mathf.RoundToInt(point.y*4f); }
                Add(self.Position); Add(self.Facing); Add(destination.position); Add(destination.facing);
                foreach (var area in WorkAreas) { Add(area.position); hash=hash*31+Mathf.RoundToInt(area.heading); }
                foreach (var facility in Facilities) if (facility?.trucks != null) foreach (var truck in facility.trucks) if (truck != self && truck.Drive != null) { Add(truck.Position); Add(truck.Facing); }
                foreach (var plane in Aircraft.Aircraft.All) if (plane != null) { Add(Geo.XZ(airport.transform.InverseTransformPoint(plane.transform.position))); Add(Geo.XZ(plane.transform.forward)); }
                foreach (var box in airport.Maintenance.VehicleBoxes(null)) Add(box.c);
                foreach (var box in airport.Baggage.VehicleBoxes()) Add(box.c);
                foreach (var box in DeliveryVehicleBoxes()) Add(box.c);
                return hash;
            }
        }
        int SurfaceStamp()
        {
            unchecked
            {
                int stamp=airport.LayoutVersion;
                foreach (var edge in airport.Taxiways.edges)
                    if (edge.IsRoad)
                    {
                        stamp=stamp*31+edge.id; stamp=stamp*31+(TaxiwayNetwork.Closed(edge) || edge.wear>=RunwayRules.ClosedWear ? 1 : 0);
                        stamp=stamp*31+edge.surface.GetHashCode(); stamp=stamp*31+edge.width.GetHashCode();
                    }
                return stamp;
            }
        }
        bool ClearOfOthers(DeicingTruckState self, BaggageVehiclePose pose)
        {
            var box = AirportMaintenance.Box.Of(pose, TruckType.bounds, .3f, airport);
            foreach (var facility in Facilities)
                if (facility?.trucks != null) foreach (var truck in facility.trucks)
                    if (truck != self && truck.Drive != null && box.Overlaps(AirportMaintenance.Box.Of(truck.Drive.Pose, TruckType.bounds, .3f, airport))) return false;
            foreach (var other in airport.Maintenance.VehicleBoxes(null)) if (box.Overlaps(other)) return false;
            foreach (var other in airport.Baggage.VehicleBoxes()) if (box.Overlaps(other)) return false;
            foreach (var other in DeliveryVehicleBoxes()) if (box.Overlaps(other)) return false;
            return true;
        }
        public List<AirportMaintenance.Box> VehicleBoxes()
        {
            var list = new List<AirportMaintenance.Box>(); var type = TruckType; if (type == null) return list;
            foreach (var facility in Facilities)
                if (facility?.trucks != null) foreach (var truck in facility.trucks)
                    if (truck.Drive != null)
                    {
                        var box = AirportMaintenance.Box.Of(truck.Drive.Pose, type.bounds, .3f, airport);
                        box.what = "de-icing truck"; list.Add(box);
                    }
            list.AddRange(DeliveryVehicleBoxes());
            return list;
        }
        bool AtStop(Aircraft.Aircraft aircraft, StandLayout layout)
        {
            if (aircraft == null || !aircraft.isActiveAndEnabled || aircraft.type == null || aircraft.Moving) return false;
            var flight = airport.Traffic.FlightOf(aircraft);
            if (flight != null && (!flight.OnGround || flight.Speed > 0.02f)) return false;
            var local = airport.transform.InverseTransformPoint(aircraft.transform.position);
            if (Mathf.Abs(local.y) > 0.5f) return false;
            var facing = Geo.XZ(airport.transform.InverseTransformDirection(aircraft.transform.forward)).normalized;
            if (Vector2.Dot(facing, layout.axis) < 0.995f) return false;
            var nose = Geo.XZ(local) + facing * aircraft.type.Wheelbase;
            return Vector2.Distance(nose, layout.stop) <= 1f;
        }

        /// <summary>Record precisely where routes cross fitted pads, leaving normal traffic clearances intact.</summary>
        public void AddStops(TaxiRoute route, AircraftType type)
        {
            route.deicingStops.Clear();
            foreach (var facility in Facilities)
            {
                if (!CanTreat(facility, type)) continue;
                var layout = Layout(facility);
                float best = 1f, at = -1f;
                for (int i = 0; i < route.path.Count; i++)
                {
                    float distance = Vector2.Distance(route.path.NoseAt(i), layout.stop);
                    if (distance > best) continue;
                    route.path.Pose(i * TaxiPath.Step, out _, out var facing, out _, out _);
                    if (Vector2.Dot(facing, layout.axis) < 0.995f) continue;
                    best = distance; at = i * TaxiPath.Step;
                }
                if (at >= 0f) route.deicingStops.Add(new DeicingStop { facility = facility, at = at });
            }
            route.deicingStops.Sort((a, b) => a.at.CompareTo(b.at));
        }
    }
}
