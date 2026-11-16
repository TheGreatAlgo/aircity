using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Vehicles
{
    public sealed class DeicingDeliveryTests
    {
        readonly List<Object> spawned = new List<Object>();
        [TearDown] public void Cleanup()
        { foreach (var item in spawned) if (item != null) Object.DestroyImmediate(item); spawned.Clear(); }

        (Airport airport, DeicingWorkArea area) Supply()
        {
            var go = new GameObject("Delivery test airport"); go.SetActive(false);
            var airport = go.AddComponent<Airport>(); airport.groundSize = 0f; go.SetActive(true); spawned.Add(go);
            airport.aprons.Add(ApronArea.Rect(Vector2.zero, new Vector2(400f, 400f), 0f, PavementSurface.Concrete));
            var net = airport.Taxiways;
            int west = net.AddNode(new Vector3(-120f, 0f, -22f)), east = net.AddNode(new Vector3(120f, 0f, -22f));
            net.Connect(west, east, RunwayRules.RoadWidth, PavementSurface.Asphalt, false, RoadClass.Service);
            Assert.That(airport.Deicing.AddOutsideAccess(west,out var gateProblem),Is.Not.Null,gateProblem);
            var area = airport.Deicing.AddWorkArea(Vector2.zero, 0f);
            Assert.That(area, Is.Not.Null);
            Assert.That(airport.Deicing.TankerType, Is.Not.Null, "Run DeicingEquipmentSetup.CreateAssets after importing the generated tanker.");
            return (airport, area);
        }
        static float Run(AirportDeicing service, Func<bool> done, float maximum = 600f, Action each = null)
        {
            float elapsed = 0f;
            while (!done() && elapsed < maximum) { service.Simulate(.1f); each?.Invoke(); elapsed += .1f; }
            return elapsed;
        }
        static DeicingDelivery Dispatch(AirportDeicing service, DeicingWorkArea area, float litres)
        {
            Assert.That(service.OrderFluid(area, litres, out var problem), Is.True, problem);
            Run(service, () => service.DeliveryFor(area)?.Drive != null, 240f);
            var delivery = service.DeliveryFor(area);
            Assert.That(delivery, Is.Not.Null);
            Assert.That(delivery.Drive, Is.Not.Null, delivery.Status);
            return delivery;
        }

        [Test] public void StockArrivesOnlyAfterDrivenArrivalHoseAndFinitePump()
        {
            var (airport, area) = Supply(); var service = airport.Deicing;
            var delivery = Dispatch(service, area, 5000f);
            Assert.That(area.fluidLitres, Is.Zero, "Supplier lead time only dispatches a vehicle; it grants no stock.");
            Assert.That(delivery.CargoLitres, Is.EqualTo(5000f));
            Assert.That(Vector2.Distance(delivery.Position, service.DeliveryPose(area).position), Is.GreaterThan(20f));
            Assert.That(airport.Taxiways.nodes.Any(n => Vector2.Distance(new Vector2(n.position.x, n.position.z), delivery.Position) < service.TankerType.Length), Is.True);
            Run(service, () => delivery.Phase == DeicingDeliveryPhase.Connecting, 240f);
            Assert.That(delivery.Phase, Is.EqualTo(DeicingDeliveryPhase.Connecting), delivery.Status);
            Assert.That(delivery.Drive.Finished, Is.True); Assert.That(area.fluidLitres, Is.Zero);
            Run(service, () => delivery.Phase == DeicingDeliveryPhase.Unloading, 30f);
            Assert.That(delivery.HoseConnected, Is.True);
            float before = area.fluidLitres;
            service.Simulate(.5f);
            Assert.That(area.fluidLitres - before, Is.EqualTo(AirportDeicing.DeliveryPumpLitresPerSecond * .5f).Within(.05f));
            Assert.That(area.fluidLitres + delivery.CargoLitres, Is.EqualTo(5000f).Within(.05f));
            Assert.That(area.deliveryLitres, Is.EqualTo(delivery.CargoLitres).Within(.05f));
            Run(service, () => !area.DeliveryPending && service.Deliveries.Count == 0, 300f);
            Assert.That(area.fluidLitres, Is.EqualTo(5000f).Within(.05f));
            Assert.That(delivery.Phase, Is.EqualTo(DeicingDeliveryPhase.Complete));
            Assert.That(delivery.HoseConnected, Is.False);
        }

        [Test] public void PausedGameFreezesVehicleHoseCargoAndStock()
        {
            var (airport, area) = Supply(); var service = airport.Deicing;
            var delivery = Dispatch(service, area, 1000f);
            var at = delivery.Position; var facing = delivery.Facing; float cargo = delivery.CargoLitres;
            service.Simulate(0f);
            Assert.That(delivery.Position, Is.EqualTo(at)); Assert.That(delivery.Facing, Is.EqualTo(facing));
            Assert.That(delivery.CargoLitres, Is.EqualTo(cargo)); Assert.That(area.fluidLitres, Is.Zero);
            Run(service, () => delivery.Phase == DeicingDeliveryPhase.Unloading, 240f);
            float hose = delivery.HoseProgress, stock = area.fluidLitres; cargo = delivery.CargoLitres;
            service.Simulate(0f);
            Assert.That(delivery.HoseProgress, Is.EqualTo(hose)); Assert.That(area.fluidLitres, Is.EqualTo(stock));
            Assert.That(delivery.CargoLitres, Is.EqualTo(cargo));
        }

        [Test] public void ClosedRoadStopsInboundAndUnloadingWithoutLosingPaidCargo()
        {
            var (airport, area) = Supply(); var service = airport.Deicing;
            var delivery = Dispatch(service, area, 2000f);
            var road = airport.Taxiways.edges.Single(e => e.IsRoad);
            var at = delivery.Position;
            road.wear = RunwayRules.ClosedWear; service.Simulate(.1f);
            Assert.That(delivery.Position, Is.EqualTo(at)); Assert.That(delivery.Phase, Is.EqualTo(DeicingDeliveryPhase.Waiting));
            service.Simulate(10f);
            Assert.That(area.fluidLitres, Is.Zero); Assert.That(delivery.CargoLitres, Is.EqualTo(2000f));
            Assert.That(area.deliveryLitres, Is.EqualTo(2000f));
            road.wear = 0f;
            Run(service, () => delivery.Phase == DeicingDeliveryPhase.Unloading, 300f);
            Assert.That(delivery.Phase, Is.EqualTo(DeicingDeliveryPhase.Unloading), delivery.Status);
            float stock = area.fluidLitres, cargo = delivery.CargoLitres;
            road.wear = RunwayRules.ClosedWear; service.Simulate(1f);
            Assert.That(area.fluidLitres, Is.EqualTo(stock)); Assert.That(delivery.CargoLitres, Is.EqualTo(cargo));
            Assert.That(delivery.HoseConnected, Is.True);
            road.wear = 0f;
            Run(service, () => !area.DeliveryPending && service.Deliveries.Count == 0, 300f);
            Assert.That(area.fluidLitres, Is.EqualTo(2000f).Within(.1f));
        }

        [Test] public void LargeOrderUsesSeparatePhysicalLoadsAndReservesOnlyRemainingVolume()
        {
            var (airport, area) = Supply(); var service = airport.Deicing;
            Assert.That(service.OrderFluid(area, 30000f, out var problem), Is.True, problem);
            var loads = new HashSet<DeicingDelivery>();
            Run(service, () => !area.DeliveryPending && service.Deliveries.Count == 0, 900f, () =>
            {
                foreach (var delivery in service.Deliveries) if (delivery.Drive != null) loads.Add(delivery);
                Assert.That(area.fluidLitres + service.PendingDeliveryReservation(area), Is.EqualTo(30000f).Within(.2f));
                Assert.That(area.fluidLitres, Is.LessThanOrEqualTo(area.capacityLitres + .001f));
            });
            Assert.That(area.fluidLitres, Is.EqualTo(30000f).Within(.2f));
            Assert.That(loads.Count, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(new[] { 20000f, 10000f }, loads.Select(d => d.DeliveredLitres).ToArray());
        }

        [Test] public void RestartResumesOnlyPaidUndeliveredLitresWithoutASecondCharge()
        {
            var (airport, area) = Supply(); var service = airport.Deicing;
            Dispatch(service, area, 5000f);
            Run(service, () => area.fluidLitres >= 500f, 300f);
            Assert.That(area.fluidLitres, Is.InRange(500f, 4999f));
            double spent = service.Accounts.fluidSpent;
            float pending = area.deliveryLitres;
            var restarted = new AirportDeicing(airport);
            restarted.Simulate(.1f);
            Assert.That(restarted.DeliveryFor(area).CargoLitres, Is.EqualTo(pending).Within(.1f));
            Run(restarted, () => !area.DeliveryPending && restarted.Deliveries.Count == 0, 400f);
            Assert.That(area.fluidLitres, Is.EqualTo(5000f).Within(.1f));
            Assert.That(restarted.Accounts.fluidSpent, Is.EqualTo(spent));
        }

        [Test] public void UnpavedBerthRejectsOrderBeforeMoneyIsSpent()
        {
            var (airport, area) = Supply(); var service = airport.Deicing;
            airport.aprons.Clear(); airport.BuildGround();
            double spent = service.Accounts.fluidSpent;
            Assert.That(service.OrderFluid(area, 1000f, out var problem), Is.False);
            StringAssert.Contains("pave", problem);
            Assert.That(service.Accounts.fluidSpent, Is.EqualTo(spent)); Assert.That(area.deliveryLitres, Is.Zero);
            Assert.That(service.Deliveries, Is.Empty);
        }

        [Test] public void RecyclingCannotConsumeReservedSupplierStorage()
        {
            var (airport, area) = Supply(); var service = airport.Deicing;
            area.fluidLitres = 25000f;
            Assert.That(service.OrderFluid(area, 5000f, out var problem), Is.True, problem);
            area.recyclingInstalled = true; area.runoffLitres = 1000f; area.recyclingDelayRemaining = 0f;
            Run(service, () => !area.DeliveryPending && service.Deliveries.Count == 0, 500f, () =>
            {
                Assert.That(area.fluidLitres + area.deliveryLitres, Is.EqualTo(30000f).Within(.1f));
                Assert.That(area.runoffLitres, Is.EqualTo(1000f));
            });
            Assert.That(area.fluidLitres, Is.EqualTo(30000f).Within(.1f));
        }

        [Test] public void SupplierAndEmptyRefillTruckTakeTurnsWithoutBlockingTheDelivery()
        {
            var (airport, area) = Supply(); var service = airport.Deicing;
            var net = airport.Taxiways;
            int south = net.AddNode(new Vector3(150f, 0f, 80f)), north = net.AddNode(new Vector3(150f, 0f, 180f));
            net.Connect(south, north, RunwayRules.TaxiwayWidthFor('C'), PavementSurface.Concrete);
            var stand = net.AddStand(StandKind.DeIcing, new Vector3(150f, 0f, 130f), 0f, SizeCode.C, PavementSurface.Concrete);
            Assert.That(stand, Is.Not.Null);
            var facility = service.Configure(stand);
            Assert.That(service.AssignWorkArea(facility, area), Is.True);
            facility.machines=1; // The refill fleet is already owned and delivered.
            var truck = service.TrucksFor(facility)[0];
            Assert.That(service.OrderFluid(area, 5000f, out var problem), Is.True, problem);
            bool emptyTruckWaited = false, unloadingAndRefilling = false;
            Run(service, () => !area.DeliveryPending && service.Deliveries.Count == 0 &&
                truck.Stage == DeicingTruckStage.Parked && truck.Drive.Finished && truck.FluidLitres >= 1199f, 600f, () =>
            {
                var delivery = service.DeliveryFor(area);
                if (truck.Stage == DeicingTruckStage.Filling && truck.FluidLitres <= .001f) emptyTruckWaited = true;
                if (delivery == null || delivery.Drive == null) return;
                if (delivery.Phase == DeicingDeliveryPhase.Unloading && truck.FluidLitres > 0f) unloadingAndRefilling = true;
                if (delivery.Phase == DeicingDeliveryPhase.Inbound || delivery.Phase == DeicingDeliveryPhase.Outbound)
                    Assert.That(truck.Drive.Finished, Is.True, "An existing refill journey finishes before supplier transit, and new ones wait.");
                var tankerBox = AirportMaintenance.Box.Of(delivery.Position, delivery.Facing, service.TankerType.bounds, 0f);
                var truckBox = AirportMaintenance.Box.Of(truck.Position, truck.Facing, service.TruckType.bounds, 0f);
                Assert.That(tankerBox.Overlaps(truckBox), Is.False, "Supplier and refill truck bodies stay separate.");
            });
            Assert.That(emptyTruckWaited, Is.True, "An empty refill truck may wait at the tank without reserving the supplier out.");
            Assert.That(unloadingAndRefilling, Is.True);
            Assert.That(truck.FluidLitres, Is.EqualTo(1200f).Within(.1f), truck.Status);
            Assert.That(area.fluidLitres + truck.FluidLitres, Is.EqualTo(5000f).Within(.2f));
            Assert.That(service.Deliveries, Is.Empty);
        }
    }
}
