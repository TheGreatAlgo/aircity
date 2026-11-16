using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using Box = AirCity.Vehicles.AirportMaintenance.Box;

namespace AirCity.Vehicles
{
    public sealed partial class AirportDeicing
    {
        public const float DeliveryCapacityLitres = 20000f;
        public const float DeliveryPumpLitresPerSecond = 200f;
        public const float DeliveryHoseSeconds = 2f;
        readonly List<DeicingDelivery> deliveries = new List<DeicingDelivery>();
        public IReadOnlyList<DeicingDelivery> Deliveries => deliveries;
        public VehicleType TankerType
        {
            get
            {
                if (assets == null) assets = Resources.Load<DeicingAssets>("DeicingAssets");
                return assets != null ? assets.tankerType : null;
            }
        }

        public DeicingDelivery DeliveryFor(DeicingWorkArea area) =>
            deliveries.Find(d => d.Area == area && d.Phase != DeicingDeliveryPhase.Complete);

        /// <summary>The reserved volume includes cargo already on the road; do not add cargo a second time.</summary>
        public float PendingDeliveryReservation(DeicingWorkArea area) => area != null ? Mathf.Max(0f, area.deliveryLitres) : 0f;
        public bool DeliveryInUse(DeicingWorkArea area) =>
            deliveries.Exists(d => d.Area == area && d.Drive != null && d.Phase != DeicingDeliveryPhase.Complete);
        public bool DeliveryTransitBlocksRefill(bool existingReturn = false)
        {
            foreach (var facility in Facilities)
                if (facility?.trucks != null) foreach (var truck in facility.trucks)
                    if (truck.DeliveryPending && truck.Drive != null && !truck.Drive.Finished) return true;
            foreach (var delivery in deliveries)
            {
                if (TankerTravelling(delivery)) return true;
                if (!existingReturn && delivery.Phase == DeicingDeliveryPhase.Waiting &&
                    delivery.resumePhase == DeicingDeliveryPhase.Outbound && !delivery.failedRoute) return true;
            }
            return false;
        }
        static bool TankerTravelling(DeicingDelivery delivery) => delivery.Drive != null &&
            (delivery.Phase == DeicingDeliveryPhase.Inbound || delivery.Phase == DeicingDeliveryPhase.Outbound);
        bool SupplyRoadBusy(DeicingDelivery self)
        {
            foreach (var delivery in deliveries) if (delivery != self && TankerTravelling(delivery)) return true;
            foreach (var facility in Facilities)
                if (facility?.trucks != null) foreach (var truck in facility.trucks)
                    if ((truck.refillArea != null || truck.DeliveryPending) && truck.Drive != null && !truck.Drive.Finished) return true;
            return false;
        }
        bool TankerExitQueued(DeicingDelivery self) => deliveries.Exists(d => d != self &&
            d.Phase == DeicingDeliveryPhase.Waiting && d.resumePhase == DeicingDeliveryPhase.Outbound && !d.failedRoute);

        /// <summary>The tanker's complete body fits the painted unload lane inside the station.</summary>
        public BaggageVehiclePose DeliveryPose(DeicingWorkArea area) => area.UnloadPose(TankerType);

        public string DeliveryOrderProblem(DeicingWorkArea area)
        {
            if (area == null || !airport.deicingWorkAreas.Contains(area)) return "choose a deicing supply area";
            if (TankerType == null) return "no delivery tanker is available";
            if (!HasDeliveryAccess(area)) return "connect the unload lane to an open outside service road";
            // Ordering validates local permanent requirements only. Long road searches belong to the queued
            // supplier, so a click never synchronously plans two airport-wide vehicle journeys.
            if (!DeicingMotion.PavementClear(airport,TankerType,DeliveryPose(area)))
                return "pave and clear the painted unloading lane inside the supply area";
            return null;
        }
        public bool TryValidateDeliveryOrder(DeicingWorkArea area, out string problem)
        { problem = DeliveryOrderProblem(area); return problem == null; }

        /// <summary>Advance supplier lead time, physical travel, hose work and unloading in game seconds.</summary>
        public void TickDeliveries(float seconds)
        {
            if (seconds <= 0f) return;
            deliveries.RemoveAll(d => d.Phase == DeicingDeliveryPhase.Complete);
            foreach (var area in airport.deicingWorkAreas)
            {
                if (area == null || !area.DeliveryPending) continue;
                if (HasDeliveryAccess(area)) area.deliverySecondsRemaining = Mathf.Max(0f, area.deliverySecondsRemaining - seconds);
                if (DeliveryFor(area) == null) deliveries.Add(new DeicingDelivery(area));
            }
            foreach (var delivery in deliveries.ToArray()) StepDelivery(delivery, seconds);
        }

        void StepDelivery(DeicingDelivery delivery, float dt)
        {
            delivery.Waiting = null;
            if (!airport.deicingWorkAreas.Contains(delivery.Area))
            {
                delivery.Drive?.Hold();
                WaitDelivery(delivery, delivery.Phase, "delivery destination is unavailable");
                return;
            }
            if (delivery.Phase == DeicingDeliveryPhase.Queued)
            {
                if (!delivery.Area.DeliveryPending) { delivery.Phase = DeicingDeliveryPhase.Complete; return; }
                if (!HasDeliveryAccess(delivery.Area)) { delivery.Waiting = "delivery waiting for an open service road"; return; }
                if (delivery.Area.deliverySecondsRemaining > .0001f)
                { delivery.Waiting = "supplier preparing the delivery"; return; }
                if (SupplyRoadBusy(delivery) || TankerExitQueued(delivery))
                { delivery.Waiting = "delivery waiting for returning service vehicles"; return; }
                delivery.retry -= dt;
                if (delivery.retry > 0f) { delivery.Waiting = "waiting for a clear tanker entry"; return; }
                int signature = DeliveryRouteSignature(delivery, DeliveryPose(delivery.Area));
                if (delivery.failedRoute && delivery.failedSignature == signature)
                { delivery.Waiting = "no clear paved tanker route to the delivery berth"; return; }
                if (!FindDeliveryRoute(delivery.Area, delivery, out int node, out var entry, out var exit, out var path))
                { delivery.failedRoute = true; delivery.failedSignature = signature; delivery.retry = 2f; delivery.Waiting = "no clear paved tanker route to the delivery berth"; return; }
                delivery.failedRoute = false;
                delivery.gatewayNode = node;
                delivery.exit = exit;
                delivery.CargoLitres = Mathf.Min(DeliveryCapacityLitres, delivery.Area.deliveryLitres);
                delivery.Drive = new DeicingDrive(entry, TankerType);
                delivery.Drive.Begin(path);
                delivery.destination = DeliveryPose(delivery.Area);
                delivery.routeStamp = surfaceStamp;
                delivery.Phase = DeicingDeliveryPhase.Inbound;
                return;
            }

            if (delivery.Phase == DeicingDeliveryPhase.Waiting)
            {
                if (delivery.resumePhase == DeicingDeliveryPhase.Queued)
                { delivery.Phase = DeicingDeliveryPhase.Queued; return; }
                if (delivery.resumePhase == DeicingDeliveryPhase.Inbound || delivery.resumePhase == DeicingDeliveryPhase.Outbound)
                {
                    if (delivery.resumePhase == DeicingDeliveryPhase.Inbound && !HasDeliveryAccess(delivery.Area))
                    { delivery.Waiting = "delivery waiting for an open service road"; return; }
                    if (!PlanDelivery(delivery, delivery.resumePhase, dt)) return;
                }
                else
                {
                    if ((delivery.resumePhase == DeicingDeliveryPhase.Connecting || delivery.resumePhase == DeicingDeliveryPhase.Unloading) && !HasDeliveryAccess(delivery.Area))
                    { delivery.Waiting = "delivery waiting for an open service road"; return; }
                    delivery.Phase = delivery.resumePhase;
                }
            }

            if (delivery.Phase == DeicingDeliveryPhase.Inbound || delivery.Phase == DeicingDeliveryPhase.Outbound)
            {
                if (delivery.Phase == DeicingDeliveryPhase.Inbound && !HasDeliveryAccess(delivery.Area))
                {
                    delivery.Drive?.StopAndClear();
                    WaitDelivery(delivery, DeicingDeliveryPhase.Inbound, "delivery waiting for an open service road");
                    return;
                }
                DriveDelivery(delivery, dt);
                return;
            }
            if (!AtDeliveryBerth(delivery))
            {
                delivery.hoseAttached = false; delivery.HoseProgress = 0f;
                WaitDelivery(delivery, DeicingDeliveryPhase.Inbound, "waiting to reach the tank delivery connection");
                return;
            }
            if ((delivery.Phase == DeicingDeliveryPhase.Connecting || delivery.Phase == DeicingDeliveryPhase.Unloading) && !HasDeliveryAccess(delivery.Area))
            {
                WaitDelivery(delivery, delivery.Phase, "delivery waiting for an open service road");
                return;
            }
            switch (delivery.Phase)
            {
                case DeicingDeliveryPhase.Connecting:
                    delivery.timer += dt;
                    delivery.HoseProgress = Mathf.Clamp01(delivery.timer / DeliveryHoseSeconds);
                    if (delivery.timer >= DeliveryHoseSeconds)
                    { delivery.hoseAttached = true; delivery.Phase = DeicingDeliveryPhase.Unloading; delivery.timer = 0f; }
                    break;
                case DeicingDeliveryPhase.Unloading:
                    float room = Mathf.Max(0f, delivery.Area.capacityLitres - delivery.Area.fluidLitres);
                    float amount = Mathf.Min(delivery.CargoLitres, Mathf.Min(delivery.Area.deliveryLitres, Mathf.Min(room, DeliveryPumpLitresPerSecond * dt)));
                    if (amount > 0f)
                    {
                        delivery.Area.fluidLitres += amount;
                        delivery.Area.deliveryLitres = Mathf.Max(0f, delivery.Area.deliveryLitres - amount);
                        delivery.CargoLitres -= amount;
                        delivery.DeliveredLitres += amount;
                    }
                    if (delivery.CargoLitres <= .001f)
                    { delivery.CargoLitres = 0f; delivery.Phase = DeicingDeliveryPhase.Disconnecting; delivery.timer = 0f; }
                    else if (room <= .001f)
                        WaitDelivery(delivery, DeicingDeliveryPhase.Unloading, "waiting for storage capacity; cargo remains in the tanker");
                    else if (delivery.Area.deliveryLitres <= .001f)
                        WaitDelivery(delivery, DeicingDeliveryPhase.Unloading, "delivery reservation changed; cargo remains in the tanker");
                    break;
                case DeicingDeliveryPhase.Disconnecting:
                    delivery.timer += dt;
                    delivery.HoseProgress = 1f - Mathf.Clamp01(delivery.timer / DeliveryHoseSeconds);
                    if (delivery.timer >= DeliveryHoseSeconds)
                    {
                        delivery.hoseAttached = false; delivery.HoseProgress = 0f;
                        if (!PlanDelivery(delivery, DeicingDeliveryPhase.Outbound, dt)) return;
                    }
                    break;
            }
        }

        void DriveDelivery(DeicingDelivery delivery, float dt)
        {
            if (delivery.Drive == null) { WaitDelivery(delivery, delivery.Phase, "waiting for delivery vehicle"); return; }
            if (delivery.routeStamp != surfaceStamp)
            {
                delivery.routeStamp = surfaceStamp;
                bool clear = DeliveryPathClear(delivery);
                if (!clear)
                {
                    delivery.Drive.StopAndClear(); delivery.failedRoute = false; delivery.retry = 0f;
                    PlanDelivery(delivery, delivery.Phase, dt);
                    return;
                }
            }
            if (!delivery.Drive.Finished)
            {
                float ahead = Mathf.Min(delivery.Drive.Path.Length, delivery.Drive.Along + Mathf.Max(.05f, Mathf.Abs(delivery.Drive.Speed) * dt + .03f));
                var at = delivery.Drive.Path.Pose(ahead, out var facing, out _);
                var next = new BaggageVehiclePose(at, facing, delivery.Drive.Path.Height(ahead,float.NaN));
                if (!DeicingMotion.StaticClear(airport, TankerType, next) || !DeliveryTrafficClear(delivery, next))
                {
                    delivery.Drive.Hold();
                    delivery.Drive.StopAndClear();
                    WaitDelivery(delivery, delivery.Phase, "tanker waiting for ground traffic");
                    return;
                }
                delivery.Drive.Step(dt);
                return;
            }
            if (delivery.Phase == DeicingDeliveryPhase.Outbound)
            { delivery.Phase = DeicingDeliveryPhase.Complete; return; }
            if (!HasDeliveryAccess(delivery.Area))
            { WaitDelivery(delivery, DeicingDeliveryPhase.Inbound, "delivery waiting for an open service road"); return; }
            delivery.Phase = DeicingDeliveryPhase.Connecting;
            delivery.timer = 0f;
        }

        bool PlanDelivery(DeicingDelivery delivery, DeicingDeliveryPhase phase, float dt)
        {
            if (SupplyRoadBusy(delivery))
            {
                delivery.failedRoute = false;
                WaitDelivery(delivery, phase, "tanker waiting for returning service vehicles");
                return false;
            }
            delivery.retry -= dt;
            if (delivery.retry > 0f) { WaitDelivery(delivery, phase, "waiting for a clear tanker route"); return false; }
            var goal = phase == DeicingDeliveryPhase.Outbound ? delivery.exit : DeliveryPose(delivery.Area);
            int signature = DeliveryRouteSignature(delivery, goal);
            if (delivery.failedRoute && delivery.failedSignature == signature)
            { WaitDelivery(delivery, phase, "no clear paved tanker route; cargo retained"); return false; }
            var path = phase==DeicingDeliveryPhase.Outbound?
                DeicingMotion.PlanRoadJourney(airport,TankerType,delivery.Drive.Pose,goal,delivery.Area.UnloadExit,null,pose=>DeliveryTrafficClear(delivery,pose)):
                DeicingMotion.PlanRoadJourney(airport,TankerType,delivery.Drive.Pose,goal,null,delivery.Area.UnloadEntry,pose=>DeliveryTrafficClear(delivery,pose));
            if(path==null) path=phase==DeicingDeliveryPhase.Outbound?
                DeicingMotion.PlanRoadJourney(airport,TankerType,delivery.Drive.Pose,goal,delivery.Area.UnloadEntry,null,pose=>DeliveryTrafficClear(delivery,pose)):
                DeicingMotion.PlanRoadJourney(airport,TankerType,delivery.Drive.Pose,goal,null,delivery.Area.UnloadExit,pose=>DeliveryTrafficClear(delivery,pose));
            if (path == null)
            {
                delivery.Drive.StopAndClear(); delivery.failedRoute = true; delivery.failedSignature = signature; delivery.retry = 2f;
                WaitDelivery(delivery, phase, "no clear paved tanker route; cargo retained");
                return false;
            }
            delivery.failedRoute = false; delivery.Drive.Begin(path);
            delivery.destination = goal; delivery.routeStamp = surfaceStamp; delivery.Phase = phase;
            return true;
        }

        bool AtDeliveryBerth(DeicingDelivery delivery)
        {
            if (delivery.Drive == null || !delivery.Drive.Finished) return false;
            var pose = DeliveryPose(delivery.Area);
            return Vector2.Distance(delivery.Position, pose.position) <= .3f && Vector2.Dot(delivery.Facing, pose.facing) >= .995f;
        }
        static void WaitDelivery(DeicingDelivery delivery, DeicingDeliveryPhase resume, string reason)
        {
            if (resume != DeicingDeliveryPhase.Waiting) delivery.resumePhase = resume;
            delivery.Phase = DeicingDeliveryPhase.Waiting;
            delivery.Waiting = reason;
        }

        bool DeliveryPathClear(DeicingDelivery delivery)
        {
            return DeicingMotion.PathClear(airport,TankerType,delivery.Drive.Path,delivery.Drive.Along);
        }

        bool FindDeliveryRoute(DeicingWorkArea area, DeicingDelivery self, out int node, out BaggageVehiclePose entry,
            out BaggageVehiclePose exit, out VehiclePath path)
        {
            node = -1; entry = exit = default; path = null;
            var type = TankerType; if (type == null || !HasDeliveryAccess(area)) return false;
            var net = airport.Taxiways;
            var candidates = new List<(int node, TaxiwayEdge edge, float distance)>();
            foreach (var candidate in net.nodes)
            {
                if (candidate.runway != null) continue;
                if (OutsideAccessForNode(candidate.id)==null || OutsideAccessProblem(candidate.id)!=null || !OutsideConnectsArea(candidate.id,area)) continue;
                TaxiwayEdge arm = null; int count = 0;
                foreach (var edge in net.EdgesAt(candidate.id))
                    if (edge.road == RoadClass.Service && edge.surface.IsPaved() && !TaxiwayNetwork.Closed(edge) && edge.wear < RunwayRules.ClosedWear)
                    { arm = edge; count++; }
                if (count != 1 || arm == null || net.EdgeLength(arm) < type.Length + 2f) continue;
                float distance = Vector2.Distance(net.At(candidate.id), area.Entrance);
                candidates.Add((candidate.id, arm, distance));
            }
            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
            var berth = DeliveryPose(area);
            int tried = 0;
            foreach (var candidate in candidates)
            {
                if (++tried > 2) break;
                var origin = net.At(candidate.node);
                var inward = (net.At(candidate.edge.a == candidate.node ? candidate.edge.b : candidate.edge.a) - origin).normalized;
                var from = new BaggageVehiclePose(origin + inward * (Mathf.Max(0f, -type.bounds.min.z) + .5f), inward);
                var toExit = new BaggageVehiclePose(origin + inward * (Mathf.Max(0f, type.bounds.max.z) + .5f), -inward);
                if (!DeliveryTrafficClear(self, from) || !DeicingMotion.StaticClear(airport, type, from)) continue;
                var inbound = DeicingMotion.PlanRoadJourney(airport,type,from,berth,null,area.UnloadEntry,pose=>DeliveryTrafficClear(self,pose));
                if(inbound==null) inbound=DeicingMotion.PlanRoadJourney(airport,type,from,berth,null,area.UnloadExit,pose=>DeliveryTrafficClear(self,pose));
                if (inbound == null) continue;
                node = candidate.node; entry = from; exit = toExit; path = inbound;
                return true;
            }
            return false;
        }

        bool DeliveryTrafficClear(DeicingDelivery self, BaggageVehiclePose pose)
        {
            var type = TankerType; if (type == null) return false;
            var box = Box.Of(pose, type.bounds, .3f, airport);
            foreach (var facility in Facilities)
                if (facility?.trucks != null) foreach (var truck in facility.trucks)
                    if (truck.Drive != null && box.Overlaps(Box.Of(truck.Drive.Pose, TruckType.bounds, .3f, airport))) return false;
            foreach (var delivery in deliveries)
                if (delivery != self && delivery.Drive != null && delivery.Phase != DeicingDeliveryPhase.Complete &&
                    box.Overlaps(Box.Of(delivery.Drive.Pose, type.bounds, .3f, airport))) return false;
            foreach (var other in airport.Maintenance.VehicleBoxes(null)) if (box.Overlaps(other)) return false;
            foreach (var other in airport.Baggage.VehicleBoxes()) if (box.Overlaps(other)) return false;
            return true;
        }
        int DeliveryRouteSignature(DeicingDelivery delivery, BaggageVehiclePose goal)
        {
            unchecked
            {
                int hash = surfaceStamp;
                if (TankerType != null) { hash = hash * 31 + TankerType.GetEntityId().GetHashCode(); hash = hash * 31 + TankerType.bounds.GetHashCode(); }
                void Add(Vector2 p) { hash = hash * 31 + Mathf.RoundToInt(p.x * 4f); hash = hash * 31 + Mathf.RoundToInt(p.y * 4f); }
                Add(delivery.Position); Add(delivery.Facing); Add(goal.position); Add(goal.facing);
                foreach (var area in WorkAreas) { Add(area.position); hash = hash * 31 + Mathf.RoundToInt(area.heading); }
                foreach (var facility in Facilities) if (facility?.trucks != null) foreach (var truck in facility.trucks) if (truck.Drive != null) Add(truck.Position);
                foreach (var other in deliveries) if (other != delivery && other.Drive != null) Add(other.Position);
                foreach (var plane in Aircraft.Aircraft.All) if (plane != null) Add(Geo.XZ(airport.transform.InverseTransformPoint(plane.transform.position)));
                foreach (var other in airport.Maintenance.VehicleBoxes(null)) Add(other.c);
                foreach (var other in airport.Baggage.VehicleBoxes()) Add(other.c);
                return hash;
            }
        }
        public List<Box> DeliveryVehicleBoxes()
        {
            var result = new List<Box>(); var type = TankerType; if (type == null) return result;
            foreach (var delivery in deliveries)
                if (delivery.Drive != null && delivery.Phase != DeicingDeliveryPhase.Complete)
                {
                    var box = Box.Of(delivery.Drive.Pose, type.bounds, .3f, airport);
                    box.what = "deicing fluid tanker"; result.Add(box);
                }
            return result;
        }
    }
}
