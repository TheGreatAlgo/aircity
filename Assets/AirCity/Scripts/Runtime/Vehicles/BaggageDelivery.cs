using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum BaggageDeliveryState { Waiting, Collecting, Outbound, Delivering, Returning, Complete, Cancelled }

    /// <summary>A lease on one tractor/cart/loader set, held until the train is back in its bay.</summary>
    public sealed class BaggageEquipmentSet
    {
        public VehicleType Tractor { get; }
        public VehicleType Cart { get; }
        public VehicleType Loader { get; }
        public object Owner { get; private set; }

        public BaggageEquipmentSet(VehicleType tractor, VehicleType cart, VehicleType loader)
        { Tractor = tractor; Cart = cart; Loader = loader; }

        public bool Reserve(object owner, out string reason)
        {
            reason = null;
            if (owner == null) reason = "equipment needs a job owner";
            else if (Tractor == null || Tractor.kind != VehicleKind.BaggageTractor || Tractor.baggage == null ||
                Cart == null || Cart.kind != VehicleKind.BaggageCart || Cart.baggage == null ||
                Loader == null || Loader.kind != VehicleKind.BeltLoader || Loader.baggage == null)
                reason = "bulk delivery needs a baggage tractor, cart and belt loader";
            else if (Owner != null && Owner != owner) reason = "waiting for baggage equipment";
            if (reason != null) return false;
            Owner = owner;
            return true;
        }

        public void Release(object owner) { if (owner != null && Owner == owner) Owner = null; }
    }

    /// <summary>
    /// One outbound bulk delivery, then a forward return to dispatch. All time is game seconds, stepped at
    /// 20 Hz independently of rendering. Batches transfer once after their handling time; a failed transfer
    /// waits with the baggage and equipment still owned. Cancellation brings any undelivered bags home.
    /// </summary>
    public sealed class BaggageDelivery
    {
        const double Tick = 0.05;
        readonly BaggageLedger ledger;
        readonly BaggageStore dispatch, cart, hold;
        readonly BaggageEquipmentSet equipment;
        readonly List<BaggageBatch> shipment = new List<BaggageBatch>();
        readonly float handoffDistance, secondsPerBag;
        readonly Func<BaggageVehiclePose, VehicleType, bool> clear;
        readonly Func<bool, float, bool> operateDoor;
        double accumulated, handling;
        bool cancelled;
        int collectIndex, deliverIndex;

        public BaggageTrain Train { get; }
        public BaggageDeliveryState State { get; private set; } = BaggageDeliveryState.Waiting;
        public string Waiting { get; private set; }
        public string Flight { get; }
        public int BagCount { get; }
        public float Weight { get; }
        public float Elapsed { get; private set; }
        public float Speed { get; private set; }
        public bool Finished => State == BaggageDeliveryState.Complete || State == BaggageDeliveryState.Cancelled;
        public bool EquipmentClear => Finished;
        public bool DepartureReady => State == BaggageDeliveryState.Complete;

        public BaggageDelivery(string flight, BaggageLedger ledger, BaggageStore dispatch, BaggageStore cart,
            BaggageStore hold, BaggageEquipmentSet equipment, BaggageTrain train, float handoffDistance,
            float secondsPerBag, Func<BaggageVehiclePose, VehicleType, bool> clear = null,
            Func<bool, float, bool> operateDoor = null)
        {
            if (string.IsNullOrWhiteSpace(flight) || ledger == null || dispatch == null || cart == null || hold == null ||
                dispatch.Ledger != ledger || cart.Ledger != ledger || hold.Ledger != ledger ||
                dispatch == cart || cart == hold || dispatch == hold || equipment == null || train == null ||
                train.Distance != 0f || train.TractorType != equipment.Tractor || train.CartType != equipment.Cart ||
                !BaggageLedger.Finite(handoffDistance) || handoffDistance <= 0f || handoffDistance >= train.Path.Length ||
                !BaggageLedger.Finite(secondsPerBag) || secondsPerBag <= 0f || hold.Flight != flight ||
                !cart.RequiresScreening || !hold.RequiresScreening)
                throw new ArgumentException("A delivery needs compatible stores, a fresh train, a hold assigned to its flight and a return route.");
            Flight = flight; this.ledger = ledger; this.dispatch = dispatch; this.cart = cart; this.hold = hold;
            this.equipment = equipment; Train = train; this.handoffDistance = handoffDistance;
            this.secondsPerBag = secondsPerBag; this.clear = clear;
            this.operateDoor = operateDoor;
            foreach (var batch in ledger.Batches)
                if (batch.Location == dispatch && batch.Flight == flight)
                { shipment.Add(batch); BagCount += batch.Count; Weight += batch.Weight; }
            shipment.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (shipment.Count == 0) throw new ArgumentException("No baggage assigned to this flight at dispatch.");
            var start = train.Path.Pose(0f, out var startFacing, out _);
            if (Vector2.Distance(start, train.Path.End) > 0.1f || Vector2.Dot(startFacing, train.Path.EndFacing) < 0.99f)
                throw new ArgumentException("Delivery route must return forwards to its dispatch bay.");
        }

        public void Step(float seconds)
        {
            if (!BaggageLedger.Finite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            accumulated += seconds;
            while (accumulated + 0.000001 >= Tick && !Finished)
            {
                accumulated -= Tick;
                Elapsed += (float)Tick;
                AdvanceTick();
            }
            if (Finished) accumulated = 0;
        }

        public void Cancel()
        {
            if (Finished) return;
            cancelled = true;
            handling = 0;
            if (State == BaggageDeliveryState.Waiting)
            { State = BaggageDeliveryState.Cancelled; return; }
            if (State == BaggageDeliveryState.Collecting)
            { ReturnBaggage(); return; }
            if (State == BaggageDeliveryState.Delivering) State = BaggageDeliveryState.Returning;
        }

        void AdvanceTick()
        {
            Waiting = null; Speed = 0f;
            switch (State)
            {
                case BaggageDeliveryState.Waiting:
                    if (cart.Count != 0) { Waiting = "cart is occupied"; break; }
                    if (BagCount > cart.Capacity || Weight > cart.WeightCapacity || Weight > equipment.Cart.baggage.payload)
                    { Waiting = "shipment exceeds this cart's capacity"; break; }
                    if (clear != null && (!clear(Train.Tractor, Train.TractorType) || !clear(Train.Cart, Train.CartType)))
                    { Waiting = "equipment bay is blocked"; break; }
                    if (!equipment.Reserve(this, out var reason)) { Waiting = reason; break; }
                    State = BaggageDeliveryState.Collecting;
                    break;
                case BaggageDeliveryState.Collecting:
                    if (cancelled) { ReturnBaggage(); break; }
                    if (Handle(ref collectIndex, dispatch, cart)) State = BaggageDeliveryState.Outbound;
                    break;
                case BaggageDeliveryState.Outbound:
                    if (Drive(handoffDistance))
                        State = cancelled ? BaggageDeliveryState.Returning : BaggageDeliveryState.Delivering;
                    break;
                case BaggageDeliveryState.Delivering:
                    if (operateDoor != null && !operateDoor(true, (float)Tick))
                    { Waiting = "opening cargo door"; break; }
                    if (Handle(ref deliverIndex, cart, hold)) State = BaggageDeliveryState.Returning;
                    break;
                case BaggageDeliveryState.Returning:
                    if (Drive(Train.Path.Length)) ReturnBaggage();
                    break;
            }
        }

        bool Handle(ref int index, BaggageStore from, BaggageStore to)
        {
            if (index >= shipment.Count) return true;
            var batch = shipment[index];
            // Refuse before spending handling time, and check again in the atomic transfer.
            Waiting = batch.Location != from ? "shipment moved from its assigned source" : to.Refusal(batch);
            if (Waiting != null) return false;
            handling += Tick;
            if (handling + 0.000001 < batch.Count * secondsPerBag) return false;
            if (!ledger.Transfer(batch, from, to, out var reason)) { Waiting = reason; return false; }
            handling -= batch.Count * secondsPerBag;
            index++;
            return index == shipment.Count;
        }

        bool Drive(float target)
        {
            Train.Path.Sample(Train.Distance, out _, out _, out float limit);
            float speed = Mathf.Min(limit, equipment.Tractor.transitSpeed, equipment.Cart.transitSpeed);
            float before = Train.Distance;
            bool moved = Train.Advance(Mathf.Min(speed * (float)Tick, target - before), clear, out var reason);
            Speed = (Train.Distance - before) / (float)Tick;
            if (!moved) Waiting = reason;
            return moved && Train.Distance >= target - 0.0001f;
        }

        void ReturnBaggage()
        {
            foreach (var batch in shipment)
                if (batch.Location == cart && !ledger.Transfer(batch, cart, dispatch, out var reason))
                { Waiting = reason; return; }
            if (cart.Count != 0) { Waiting = "cart contains baggage outside this shipment"; return; }
            if (operateDoor != null && !operateDoor(false, (float)Tick))
            { Waiting = "closing cargo door"; return; }
            equipment.Release(this);
            State = cancelled ? BaggageDeliveryState.Cancelled : BaggageDeliveryState.Complete;
            Speed = 0f;
        }
    }
}
