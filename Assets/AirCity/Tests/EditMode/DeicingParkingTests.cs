using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Vehicles
{
    public sealed class DeicingParkingTests
    {
        readonly List<Object> spawned = new List<Object>();
        [TearDown] public void Cleanup()
        { foreach (var item in spawned) if (item != null) Object.DestroyImmediate(item); spawned.Clear(); }

        (Airport airport, DeicingFacility facility, DeicingParkingBay bay, int gate) Create(bool outside = true)
        {
            var go = new GameObject("Deicing parking test"); go.SetActive(false);
            var airport = go.AddComponent<Airport>(); airport.groundSize = 0f; go.SetActive(true); spawned.Add(go);
            airport.aprons.Add(ApronArea.Rect(Vector2.zero,new Vector2(400f,400f),0f,PavementSurface.Concrete));
            var net = airport.Taxiways;
            int west = net.AddNode(new Vector3(-120f,0f,-22f)), east = net.AddNode(new Vector3(120f,0f,-22f));
            net.Connect(west,east,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            if (outside) Assert.That(airport.Deicing.AddOutsideAccess(west,out var problem),Is.Not.Null,problem);
            int south = net.AddNode(new Vector3(100f,0f,40f)), north = net.AddNode(new Vector3(100f,0f,180f));
            net.Connect(south,north,RunwayRules.TaxiwayWidthFor('C'),PavementSurface.Concrete);
            var stand = net.AddStand(StandKind.DeIcing,new Vector3(100f,0f,100f),0f,SizeCode.C,PavementSurface.Concrete);
            Assert.That(stand,Is.Not.Null);
            var facility = airport.Deicing.Configure(stand);
            var bay = airport.Deicing.AddParkingBay(new Vector2(-55f,-65f),0f);
            Assert.That(bay,Is.Not.Null,airport.Deicing.LastPurchaseProblem);
            return (airport,facility,bay,west);
        }
        static float Run(AirportDeicing service,Func<bool> done,float limit=240f)
        {
            float elapsed=0f;
            while (!done() && elapsed<limit) { service.Simulate(.1f); elapsed+=.1f; }
            return elapsed;
        }

        [Test] public void OrdinaryRoadEndpointDoesNotGrantOutsideSuppliesOrTruckPurchases()
        {
            var (airport,facility,bay,gate)=Create(false); var service=airport.Deicing;
            var area=service.AddWorkArea(Vector2.zero,0f); Assert.That(area,Is.Not.Null);
            double capital=service.Accounts.capitalSpent, fluid=service.Accounts.fluidSpent;
            Assert.That(service.HasDeliveryAccess(area),Is.False);
            Assert.That(service.OrderFluid(area,1000f,out _),Is.False);
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.False); StringAssert.Contains("outside",problem);
            Assert.That(facility.machines,Is.Zero); Assert.That(service.Accounts.capitalSpent,Is.EqualTo(capital));
            Assert.That(service.Accounts.fluidSpent,Is.EqualTo(fluid));
            Assert.That(service.AddOutsideAccess(gate,out problem),Is.Not.Null,problem);
            Assert.That(service.HasDeliveryAccess(area),Is.True);
            Assert.That(service.OrderTruck(facility,bay,out problem),Is.True,problem);
        }

        [Test] public void NearbyIsolatedRoadNeedsOpenGraphConnectionToMarkedOutsideGate()
        {
            var (airport,_,_,gate)=Create(); var service=airport.Deicing; var net=airport.Taxiways;
            net.RemoveEdges(net.edges.Where(e=>e.IsRoad).ToArray());
            int a=net.AddNode(new Vector3(-120f,0f,-22f)), b=net.AddNode(new Vector3(-60f,0f,-22f));
            int c=net.AddNode(new Vector3(-20f,0f,-22f)), d=net.AddNode(new Vector3(60f,0f,-22f));
            net.Connect(a,b,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Connect(c,d,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            Assert.That(service.AddOutsideAccess(a,out var problem),Is.Not.Null,problem);
            var area=service.AddWorkArea(Vector2.zero,0f); Assert.That(area,Is.Not.Null);
            Assert.That(service.HasDeliveryAccess(area),Is.False,"An apron shortcut cannot join two service-road graphs.");
            var bridge=net.Connect(b,c,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service).Single();
            Assert.That(service.HasDeliveryAccess(area),Is.True);
            bridge.wear=RunwayRules.ClosedWear; Assert.That(service.HasDeliveryAccess(area),Is.False);
            bridge.wear=0f; Assert.That(service.HasDeliveryAccess(area),Is.True);
            Assert.That(service.RemoveOutsideAccess(service.OutsideAccessForNode(a),out problem),Is.True,problem);
            Assert.That(service.HasDeliveryAccess(area),Is.False);
        }

        [Test] public void PaidEmptyTruckDrivesFromOutsideToParkingAndPauseFreezesItsArrival()
        {
            var (airport,facility,bay,_)=Create(); var service=airport.Deicing;
            double capital=service.Accounts.capitalSpent;
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.True,problem);
            var truck=service.TrucksFor(facility).Single();
            Assert.That(truck.DeliveryPending,Is.True); Assert.That(truck.Drive,Is.Null);
            Assert.That(service.DeliveredTruckCount(facility),Is.Zero); Assert.That(service.OperatorsFor(facility),Is.Zero);
            service.Simulate(AirportDeicing.TruckDeliveryLeadSeconds-1f);
            Assert.That(truck.Drive,Is.Null); Assert.That(service.Accounts.payrollSpent,Is.Zero);
            Run(service,()=>truck.Drive!=null,10f); Assert.That(truck.Drive,Is.Not.Null,truck.Status);
            Assert.That(Vector2.Distance(truck.Position,service.ParkingPosition(bay)),Is.GreaterThan(30f));
            var at=truck.Position; var facing=truck.Facing; service.Simulate(0f);
            Assert.That(truck.Position,Is.EqualTo(at)); Assert.That(truck.Facing,Is.EqualTo(facing));
            float elapsed=Run(service,()=>truck.IsDelivered);
            Assert.That(truck.IsDelivered,Is.True,$"{elapsed}s: {truck.Status}");
            Assert.That(Vector2.Distance(truck.Position,service.ParkingPosition(bay)),Is.LessThan(.3f));
            Assert.That(truck.FluidLitres,Is.Zero); Assert.That(service.ParkingBayFor(truck),Is.SameAs(bay));
            Assert.That(service.Accounts.capitalSpent,Is.EqualTo(capital+AirportDeicing.TruckPrice));
            Assert.That(service.DeliveredTruckCount(facility),Is.EqualTo(1));
        }

        [Test] public void InboundTruckStopsInPlaceOnOutsideConnectionLossThenResumes()
        {
            var (airport,facility,bay,gate)=Create(); var service=airport.Deicing;
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.True,problem);
            var truck=service.TrucksFor(facility).Single(); Run(service,()=>truck.Drive!=null,45f);
            service.Simulate(1f); var at=truck.Position; var facing=truck.Facing;
            Assert.That(service.RemoveOutsideAccess(service.OutsideAccessForNode(gate),out problem),Is.True,problem);
            service.Simulate(5f);
            Assert.That(truck.Position,Is.EqualTo(at)); Assert.That(truck.Facing,Is.EqualTo(facing));
            Assert.That(truck.DeliveryPending,Is.True); Assert.That(truck.FluidLitres,Is.Zero);
            Assert.That(service.AddOutsideAccess(gate,out problem),Is.Not.Null,problem);
            Run(service,()=>truck.IsDelivered);
            Assert.That(truck.IsDelivered,Is.True,truck.Status);
            Assert.That(Vector2.Distance(truck.Position,service.ParkingPosition(bay)),Is.LessThan(.3f));
        }

        [Serializable] sealed class SavedTruckOrder { public DeicingFacility facility; public DeicingParkingBay bay; }
        [Test] public void SerializedPaidOrderKeepsItsBayAndRemainingLeadWithoutASecondPurchase()
        {
            var (airport,facility,bay,_)=Create(); var service=airport.Deicing;
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.True,problem); service.Simulate(5f);
            var saved=JsonUtility.FromJson<SavedTruckOrder>(JsonUtility.ToJson(new SavedTruckOrder { facility=facility,bay=bay }));
            Assert.That(saved.facility.trucks.Single().DeliveryPending,Is.True);
            Assert.That(saved.facility.trucks.Single().parkingBayId,Is.EqualTo(saved.bay.id));
            Assert.That(saved.facility.trucks.Single().truckDeliverySeconds,Is.InRange(24.9f,25.1f));
            double spent=service.Accounts.capitalSpent;
            airport.deicingFacilities.Clear(); airport.deicingFacilities.Add(saved.facility);
            airport.deicingParkingBays.Clear(); airport.deicingParkingBays.Add(saved.bay);
            var restarted=new AirportDeicing(airport); var truck=restarted.TrucksFor(saved.facility).Single();
            Assert.That(truck.Drive,Is.Null); Assert.That(restarted.ParkingBays.Count,Is.EqualTo(1));
            restarted.Simulate(20f); Assert.That(truck.Drive,Is.Null,"Reload must preserve the remaining supplier preparation.");
            Run(restarted,()=>truck.IsDelivered);
            Assert.That(truck.IsDelivered,Is.True,truck.Status); Assert.That(restarted.Accounts.capitalSpent,Is.EqualTo(spent));
            Assert.That(restarted.ParkingBayFor(truck).id,Is.EqualTo(saved.bay.id));
        }

        [Test] public void LegacyOwnedFleetKeepsItsHomesWithoutCreatingParkingOrChangingStockAndMoney()
        {
            var (airport,facility,_,_)=Create(); var service=airport.Deicing;
            airport.deicingParkingBays.Clear(); facility.machines=2;
            facility.trucks.Add(new DeicingTruckState { onboardLitres=123f });
            facility.trucks.Add(new DeicingTruckState { onboardLitres=456f });
            double spent=service.Accounts.capitalSpent;
            var trucks=service.TrucksFor(facility); Assert.That(service.ParkingBays,Is.Empty);
            for (int i=0;i<trucks.Count;i++)
            {
                Assert.That(trucks[i].IsDelivered,Is.True); Assert.That(service.ParkingBayFor(trucks[i]),Is.Null);
                Assert.That(trucks[i].parkingBayId,Is.Zero);
                Assert.That(trucks[i].Position,Is.EqualTo(DeicingMotion.ParkPosition(service.Layout(facility),i,service.TruckType).position));
            }
            service.TrucksFor(facility); service.DeliveredTruckCount(facility); Assert.That(service.ParkingBays,Is.Empty);
            Assert.That(trucks.Sum(t=>t.FluidLitres),Is.EqualTo(579f)); Assert.That(facility.machines,Is.EqualTo(2));
            Assert.That(service.Accounts.capitalSpent,Is.EqualTo(spent));
        }

        [Test] public void AutomaticBayCleanupPreservesPlacedBaysCurrentPoseFluidAndPaidTruckOrders()
        {
            var (airport,facility,placed,_)=Create(); var service=airport.Deicing;
            facility.machines=1;
            var existing=service.TrucksFor(facility).Single(); existing.onboardLitres=579f;
            var auto=new DeicingParkingBay { id=90,position=existing.Position,heading=0f,standId=facility.standId,legacy=true };
            airport.deicingParkingBays.Add(auto); existing.parkingBayId=auto.id;
            var pose=existing.Position; var drive=existing.Drive;
            Assert.That(service.OrderTruck(facility,placed,out var problem),Is.True,problem);
            var pending=service.TrucksFor(facility)[1];
            placed.legacy=true; // An early save may have reused an automatic bay for a paid purchase.
            double capital=service.Accounts.capitalSpent;
            float lead=pending.truckDeliverySeconds;
            Assert.That(service.ParkingBays,Is.EquivalentTo(new[] { placed }));
            Assert.That(placed.legacy,Is.False);
            Assert.That(existing.parkingBayId,Is.Zero); Assert.That(existing.Drive,Is.SameAs(drive));
            Assert.That(existing.Position,Is.EqualTo(pose)); Assert.That(existing.FluidLitres,Is.EqualTo(579f));
            Assert.That(pending.DeliveryPending,Is.True); Assert.That(pending.parkingBayId,Is.EqualTo(placed.id));
            Assert.That(pending.truckDeliverySeconds,Is.EqualTo(lead)); Assert.That(service.Accounts.capitalSpent,Is.EqualTo(capital));
        }

        [Test] public void BuyingAnotherTruckCannotCreateItsOwnParkingCapacity()
        {
            var (airport,facility,_,_)=Create(); var service=airport.Deicing;
            airport.deicingParkingBays.Clear(); facility.machines=1;
            var owned=service.TrucksFor(facility).Single(); owned.onboardLitres=321f;
            double capital=service.Accounts.capitalSpent;
            Assert.That(service.BuyMachine(facility,out var problem),Is.False); StringAssert.Contains("parking",problem);
            Assert.That(service.ParkingBays,Is.Empty); Assert.That(facility.machines,Is.EqualTo(1));
            Assert.That(service.TrucksFor(facility).Single(),Is.SameAs(owned)); Assert.That(owned.FluidLitres,Is.EqualTo(321f));
            Assert.That(service.Accounts.capitalSpent,Is.EqualTo(capital));
        }

        [Test] public void ExistingTruckCanDriveToAnExplicitPlacedBayWithoutTeleportOrAnotherPurchase()
        {
            var (airport,facility,bay,_)=Create(); var service=airport.Deicing;
            facility.machines=1; var truck=service.TrucksFor(facility).Single(); truck.onboardLitres=700f;
            var start=truck.Position; double capital=service.Accounts.capitalSpent,fluid=service.Accounts.fluidSpent;
            Assert.That(service.AssignParkingBay(truck,bay,out var problem),Is.True,problem);
            Assert.That(truck.Position,Is.EqualTo(start),"Assigning a bay must not move the truck instantly.");
            Assert.That(truck.Drive.Finished,Is.False); Assert.That(service.TruckFor(bay),Is.SameAs(truck));
            service.Simulate(0f); Assert.That(truck.Position,Is.EqualTo(start));
            service.Simulate(1f); Assert.That(Vector2.Distance(truck.Position,start),Is.GreaterThan(.01f));
            Run(service,()=>truck.Drive.Finished && truck.Stage==DeicingTruckStage.Parked);
            Assert.That(truck.Stage,Is.EqualTo(DeicingTruckStage.Parked),truck.Status);
            Assert.That(Vector2.Distance(truck.Position,service.ParkingPosition(bay)),Is.LessThan(.35f));
            Assert.That(truck.FluidLitres,Is.EqualTo(700f)); Assert.That(service.Accounts.fluidSpent,Is.EqualTo(fluid));
            Assert.That(service.Accounts.capitalSpent,Is.EqualTo(capital)); Assert.That(service.ParkingBays.Count,Is.EqualTo(1));
        }

        [Test] public void ParkingReassignmentRejectsBusyOrUnreachableTargetsWithoutLosingTheCurrentBay()
        {
            var (airport,facility,first,_)=Create(); var service=airport.Deicing;
            var second=service.AddParkingBay(new Vector2(-35f,-65f),0f); Assert.That(second,Is.Not.Null);
            facility.machines=1; var truck=service.TrucksFor(facility).Single(); truck.onboardLitres=700f;
            Assert.That(service.AssignParkingBay(truck,first,out var problem),Is.True,problem);
            Assert.That(service.AssignParkingBay(truck,second,out problem),Is.False); StringAssert.Contains("finish",problem);
            Assert.That(service.TruckFor(first),Is.SameAs(truck)); Assert.That(service.TruckFor(second),Is.Null);
            Run(service,()=>truck.Drive.Finished && truck.Stage==DeicingTruckStage.Parked);
            var pose=truck.Position; double capital=service.Accounts.capitalSpent;
            airport.aprons.Clear();
            Assert.That(service.AssignParkingBay(truck,second,out problem),Is.False); StringAssert.Contains("route",problem);
            Assert.That(service.TruckFor(first),Is.SameAs(truck)); Assert.That(service.TruckFor(second),Is.Null);
            Assert.That(truck.Position,Is.EqualTo(pose)); Assert.That(truck.FluidLitres,Is.EqualTo(700f));
            Assert.That(service.Accounts.capitalSpent,Is.EqualTo(capital));
        }

        [Test] public void DeliveredTruckCoversBothWingsWhileSecondTruckIsStillAtSupplier()
        {
            var (airport,facility,bay,_)=Create(); var service=airport.Deicing;
            facility.machines=1; var first=service.TrucksFor(facility).Single(); first.onboardLitres=first.CapacityLitres;
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.True,problem);
            var type=AircraftSetup.AllTypes().First(t=>t.model=="Narrowbody");
            var go=new GameObject("Aircraft awaiting treatment"); spawned.Add(go);
            var park=service.Layout(facility).ParkPosition(type); go.transform.position=new Vector3(park.x,0f,park.y);
            var aircraft=go.AddComponent<AirCity.Aircraft.Aircraft>(); aircraft.type=type;
            service.Simulate(.1f);
            Assert.That(service.JobFor(facility),Is.Not.Null);
            Assert.That(first.TargetCount,Is.EqualTo(DeicingWingCoverage.Create(type,-1).Count+DeicingWingCoverage.Create(type,1).Count));
            Assert.That(service.TrucksFor(facility)[1].DeliveryPending,Is.True);
            Assert.That(service.TrucksFor(facility)[1].TargetCount,Is.Zero);
        }

        [Test] public void ParkingPlacementAndReservationRejectOverlapAndOccupiedDeletion()
        {
            var (airport,facility,bay,_)=Create(); var service=airport.Deicing;
            Assert.That(service.AddParkingBay(bay.position,bay.heading),Is.Null);
            Assert.That(service.AddParkingBay(new Vector2(300f,300f),0f),Is.Null);
            StringAssert.Contains("truck parking",service.WorkAreaProblem(bay.position,bay.heading));
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.True,problem);
            Assert.That(service.RemoveParkingBay(bay,out problem),Is.False); StringAssert.Contains("truck",problem);
            Assert.That(service.MoveParkingBay(bay,new Vector2(-40f,-65f),0f,out problem),Is.False);
            Assert.That(service.OrderTruck(facility,bay,out problem),Is.False); StringAssert.Contains("occupied",problem);
            // All four corners can be paved while the middle is still grass.
            airport.aprons.Clear();
            airport.aprons.Add(ApronArea.Rect(new Vector2(-55f,-70f),new Vector2(10f,3f),0f,PavementSurface.Concrete));
            airport.aprons.Add(ApronArea.Rect(new Vector2(-55f,-60f),new Vector2(10f,3f),0f,PavementSurface.Concrete));
            StringAssert.Contains("pave",service.ParkingBayProblem(bay.position,bay.heading,bay));
        }

        [Test] public void PaidPendingTruckPreventsPadDeletionAndKeepsItsReservedBay()
        {
            var (airport,facility,bay,_)=Create(); var service=airport.Deicing;
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.True,problem);
            var stand=service.Layout(facility).stand; double spent=service.Accounts.capitalSpent;
            StringAssert.Contains("paid truck order",service.PadRemovalProblem(stand));
            airport.Taxiways.RemoveStand(stand);
            Assert.That(airport.Taxiways.stands,Does.Contain(stand));
            Assert.That(service.Facilities,Does.Contain(facility));
            Assert.That(service.TruckFor(bay).DeliveryPending,Is.True);
            Assert.That(service.Accounts.capitalSpent,Is.EqualTo(spent));
        }

        [Test] public void OrderedTruckAndFluidTankerTakeTurnsThenRefillWithoutStockLoss()
        {
            var (airport,facility,bay,_)=Create(); var service=airport.Deicing;
            var area=service.AddWorkArea(Vector2.zero,0f); Assert.That(area,Is.Not.Null);
            Assert.That(service.AssignWorkArea(facility,area),Is.True);
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.True,problem);
            var truck=service.TrucksFor(facility).Single();
            // Both suppliers become ready together, exercising the shared access rather than two disjoint trips.
            truck.truckDeliverySeconds=AirportDeicing.DeliveryLeadSeconds;
            Assert.That(service.OrderFluid(area,5000f,out problem),Is.True,problem);
            bool incoming=false, supplierWaited=false;
            float elapsed=0f;
            while (elapsed<600f && !(truck.IsDelivered && truck.Stage==DeicingTruckStage.Parked && truck.Drive.Finished &&
                truck.FluidLitres>=1199f && !area.DeliveryPending && service.Deliveries.Count==0))
            {
                service.Simulate(.1f); elapsed+=.1f;
                var delivery=service.DeliveryFor(area);
                if(truck.DeliveryPending && truck.Drive!=null && !truck.Drive.Finished)
                {
                    incoming=true;
                    if(delivery!=null)
                    {
                        Assert.That(delivery.Phase==DeicingDeliveryPhase.Inbound || delivery.Phase==DeicingDeliveryPhase.Outbound,Is.False,
                            "The tanker must yield until the purchased truck reaches its parking.");
                        supplierWaited|=area.deliverySecondsRemaining<=.01f;
                    }
                }
                if(delivery?.Drive!=null && truck.Drive!=null)
                {
                    var tankerBox=AirportMaintenance.Box.Of(delivery.Position,delivery.Facing,service.TankerType.bounds,0f);
                    var truckBox=AirportMaintenance.Box.Of(truck.Position,truck.Facing,service.TruckType.bounds,0f);
                    Assert.That(tankerBox.Overlaps(truckBox),Is.False);
                }
                float delivered=service.Deliveries.Sum(d=>d.CargoLitres);
                Assert.That(area.fluidLitres+area.deliveryLitres+truck.FluidLitres,Is.EqualTo(5000f).Within(.2f),
                    "Paid pending volume already includes physical tanker cargo.");
                Assert.That(delivered,Is.LessThanOrEqualTo(area.deliveryLitres+.1f));
            }
            Assert.That(incoming && supplierWaited,Is.True);
            Assert.That(truck.IsDelivered,Is.True,truck.Status); Assert.That(service.Deliveries,Is.Empty);
            Assert.That(truck.Stage,Is.EqualTo(DeicingTruckStage.Parked),$"{elapsed}s: {truck.Status}");
            Assert.That(truck.FluidLitres,Is.EqualTo(1200f).Within(.1f));
            Assert.That(area.fluidLitres+truck.FluidLitres,Is.EqualTo(5000f).Within(.2f));
        }
    }
}
