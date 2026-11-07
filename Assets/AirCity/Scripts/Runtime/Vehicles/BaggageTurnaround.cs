using System;
using System.Collections.Generic;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum BaggageTurnaroundState
    {
        WaitingAircraft, WaitingEquipment, LoaderApproach, InboundDrive, UnloadingHold, ArrivalsDrive,
        UnloadingArrivals, DispatchDrive, CollectingDepartures, OutboundDrive, LoadingHold, Withdrawing, Complete, Cancelled,
        ReloadDrive, Docking, Undocking, ParkingTrain
    }

    public sealed class BaggageTurnaround
    {
        readonly AirportBaggage coordinator;
        readonly Airport airport;
        readonly List<BaggageBatch> inbound = new List<BaggageBatch>(), outbound = new List<BaggageBatch>();
        readonly string arrivalFlight, departureFlight;
        float handling, loaderDistance, retry;
        int index;
        bool sealedManifest, cancelled, trainHome, loaderHome, docked;
        string planningFailure;
        public Aircraft.Aircraft Aircraft { get; }
        public Stand Stand { get; }
        public BulkHoldConfiguration HoldConfiguration { get; }
        public BaggageStore Hold { get; }
        public BaggageHoldPacking Packing { get; }
        internal bool VisitEnded { get; set; }
        public BaggageFleet Fleet { get; }
        public BaggageServicePlan Plan { get; internal set; }
        public BaggageTurnaroundState State { get; private set; } = BaggageTurnaroundState.WaitingAircraft;
        public string Waiting { get; private set; }
        public bool Acquired => Plan != null;
        public bool Finished => State == BaggageTurnaroundState.Complete || State == BaggageTurnaroundState.Cancelled;
        public bool DepartureReady => State == BaggageTurnaroundState.Complete && inbound.TrueForAll(b => b.Location == Fleet.Arrivals || b.Location == coordinator.Reclaim) &&
            outbound.TrueForAll(b => b.Location == Hold) && Hold.Count == OutgoingCount;
        public int IncomingCount { get; private set; }
        public int OutgoingCount { get; private set; }
        public string DepartureFlight => departureFlight;
        public bool HandlingCart => State == BaggageTurnaroundState.UnloadingHold || State == BaggageTurnaroundState.UnloadingArrivals ||
            State == BaggageTurnaroundState.CollectingDepartures || State == BaggageTurnaroundState.LoadingHold;
        public int ArrivalTrips { get; private set; }
        public int DepartureTrips { get; private set; }
        public BaggageBatch TransferBatch { get; private set; }
        public BaggageStore TransferFrom { get; private set; }
        public BaggageStore TransferTo { get; private set; }
        public float TransferSeconds { get; private set; }
        public bool DepotTransfer => TransferBatch!=null && TransferFrom!=Hold && TransferTo!=Hold && Fleet.Station.depot!=null;
        public float TransferInterval => Plan == null || Finished ? Fleet.Station.secondsPerBag : DepotTransfer ?
            Mathf.Max(.65f,3.6f/Mathf.Max(1,Fleet.Crew.GroundWorkers)) : Fleet.Crew.Interval;
        public float BagTravelSeconds => Plan == null || Finished ? 0f : DepotTransfer ? Fleet.Station.depot.ConveyorSeconds+3.6f :
            Fleet.Crew.Travel(TransferTo == Hold || State == BaggageTurnaroundState.LoadingHold);

        /// <summary>Resume the same manifests after cancellation, preserving all completed handovers.</summary>
        public bool Retry(out string reason)
        {
            reason = null;
            if (State != BaggageTurnaroundState.Cancelled || !sealedManifest || !Parked())
            { reason = "retry needs a cancelled, closed manifest and the aircraft on its original stand"; return false; }
            cancelled = false; Plan = null; retry = 0f; planningFailure = null;
            loaderDistance = 0f; docked = trainHome = loaderHome = false;
            SetState(BaggageTurnaroundState.WaitingAircraft);
            return true;
        }

        internal BaggageTurnaround(AirportBaggage coordinator, Airport airport, Aircraft.Aircraft aircraft, Stand stand,
            BulkHoldConfiguration configuration, BaggageFleet fleet, string arrival, string departure)
        {
            this.coordinator = coordinator; this.airport = airport; Aircraft = aircraft; Stand = stand; Fleet = fleet;
            HoldConfiguration = new BulkHoldConfiguration { aircraft = configuration.aircraft, cargoPoint = configuration.cargoPoint,
                bagCapacity = configuration.bagCapacity, weightCapacity = configuration.weightCapacity };
            arrivalFlight = arrival; departureFlight = departure;
            Hold = coordinator.Ledger.Store(aircraft.name + " bulk hold", configuration.bagCapacity, configuration.weightCapacity, arrival);
            Packing = new BaggageHoldPacking(this);
        }

        /// <summary>Explicit manifest closure prevents a departure from treating missing or late bags as zero demand.</summary>
        public bool Seal(out string reason)
        {
            reason = null;
            if (sealedManifest || Finished) { reason = "turnaround manifest is already closed"; return false; }
            foreach (var batch in coordinator.Ledger.Batches)
            {
                if (batch.Location == Hold) inbound.Add(batch);
                if (batch.Location == Fleet.Dispatch && batch.Flight == departureFlight) outbound.Add(batch);
            }
            inbound.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            outbound.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            float outgoingWeight = 0f;
            foreach (var b in inbound) IncomingCount += b.Count;
            foreach (var b in outbound) { OutgoingCount += b.Count; outgoingWeight += b.Weight; }
            bool oversizedBatch = inbound.Exists(b => b.Count > Fleet.Cart.Capacity || b.Weight > Fleet.Cart.WeightCapacity) ||
                outbound.Exists(b => b.Count > Fleet.Cart.Capacity || b.Weight > Fleet.Cart.WeightCapacity);
            if (oversizedBatch || OutgoingCount > Hold.Capacity || outgoingWeight > Hold.WeightCapacity)
            {
                reason = "manifest exceeds the configured hold or a batch exceeds single-cart capacity";
                inbound.Clear(); outbound.Clear(); IncomingCount = OutgoingCount = 0;
                return false;
            }
            sealedManifest = true;
            Packing.Seed(inbound);
            return true;
        }

        public void Cancel() { if (!Finished) cancelled = true; }

        bool Parked()
        {
            if (Aircraft == null || !Aircraft.isActiveAndEnabled || Aircraft.type != HoldConfiguration.aircraft || Aircraft.Moving ||
                !airport.Taxiways.stands.Contains(Stand)) return false;
            var layout = airport.Taxiways.Layout(Stand);
            if (layout == null || layout.edge == null || Stand.DeIcing || !layout.ParkingFits(Aircraft.type) ||
                Stand.ServiceFor(Aircraft.type)?.allowed == false) return false;
            var p = airport.transform.InverseTransformPoint(Aircraft.transform.position);
            var facing = BaggageServicePlan.XZ(airport.transform.InverseTransformDirection(Aircraft.transform.forward));
            return Mathf.Abs(p.y) < 0.5f && Vector2.Distance(BaggageServicePlan.XZ(p), layout.ParkPosition(Aircraft.type)) < 1.5f &&
                Vector2.Angle(facing, layout.nose) < 6f;
        }

        internal void Step(float dt)
        {
            Waiting = null;
            if (Acquired)
            {
                Plan.RefreshGeometry();
                Fleet.TrainSpeed = Fleet.LoaderSpeed = 0f;
                if (!Parked() || airport.LayoutVersion != Plan.layoutVersion || Fleet.Station == null || !Fleet.Station.isActiveAndEnabled ||
                    Vector3.Distance(Aircraft.transform.position, Plan.aircraftPosition) > 0.1f ||
                    Quaternion.Angle(Aircraft.transform.rotation, Plan.aircraftRotation) > 0.5f) cancelled = true;
            }
            if (cancelled && !Acquired) { State = BaggageTurnaroundState.Cancelled; Waiting = "turnaround cancelled"; return; }
            switch (State)
            {
                case BaggageTurnaroundState.WaitingAircraft:
                    if (!sealedManifest) { Waiting = "waiting for closed baggage manifests"; break; }
                    if (!Parked()) { Waiting = "waiting for the aircraft to park on its stand"; break; }
                    if (!coordinator.ContainersReady(this)) { Waiting = "waiting for container equipment to return"; break; }
                    State = BaggageTurnaroundState.WaitingEquipment;
                    break;
                case BaggageTurnaroundState.WaitingEquipment:
                    if (!Parked()) { State = BaggageTurnaroundState.WaitingAircraft; break; }
                    retry -= dt;
                    if (retry > 0f) { Waiting = planningFailure ?? "waiting for a usable baggage service plan"; break; }
                    retry = 1f;
                    if (!coordinator.Start(this, out var reason))
                    {
                        Waiting = reason;
                        if (reason != planningFailure) Debug.Log("Baggage planning: " + reason);
                        planningFailure = reason;
                        break;
                    }
                    State = BaggageTurnaroundState.LoaderApproach;
                    break;
                case BaggageTurnaroundState.LoaderApproach:
                    if (!MoveLoader(Plan.loaderIn, dt)) break;
                    if (cancelled) { BeginWithdrawal(true); break; }
                    if (!cancelled && !Door(true, dt)) break;
                    if (!RaiseLoader(cancelled ? Fleet.Equipment.Loader.baggage.frontMinHeight : Plan.sill, dt)) break;
                    loaderDistance = 0f;
                    State = BaggageTurnaroundState.Docking;
                    break;
                case BaggageTurnaroundState.Docking:
                    if (!docked && !MoveLoader(Plan.loaderDock, dt)) break;
                    docked = true;
                    if (cancelled) { BeginWithdrawal(true); break; }
                    if (!Fleet.Crew.MoveHold(true, dt)) { Waiting = "loader operator entering the hold"; break; }
                    loaderDistance = 0f;
                    State = BaggageTurnaroundState.InboundDrive;
                    break;
                case BaggageTurnaroundState.InboundDrive:
                    if (Drive(dt)) SetState(cancelled ? BaggageTurnaroundState.ArrivalsDrive : BaggageTurnaroundState.UnloadingHold,
                        cancelled ? Plan.toArrivals : null);
                    break;
                case BaggageTurnaroundState.UnloadingHold:
                    if (cancelled && TransferBatch == null || Handle(inbound, Hold, Fleet.Cart, dt, true, true))
                    { if (Fleet.CartCount > 0) ArrivalTrips++; SetState(BaggageTurnaroundState.ArrivalsDrive, Plan.toArrivals); }
                    break;
                case BaggageTurnaroundState.ArrivalsDrive:
                    if (Drive(dt)) SetState(BaggageTurnaroundState.UnloadingArrivals);
                    break;
                case BaggageTurnaroundState.UnloadingArrivals:
                    if (Handle(inbound, Fleet.Cart, Fleet.Arrivals, dt, true))
                    {
                        if (Plan.toDispatch != null) SetState(BaggageTurnaroundState.DispatchDrive, Plan.toDispatch);
                        else AtDispatch();
                    }
                    break;
                case BaggageTurnaroundState.DispatchDrive:
                    if (Drive(dt)) AtDispatch();
                    break;
                case BaggageTurnaroundState.CollectingDepartures:
                    if (cancelled) { ReturnOutbound(); BeginWithdrawal(true); break; }
                    if (Handle(outbound, Fleet.Dispatch, Fleet.Cart, dt, true, true))
                    { if (Fleet.CartCount > 0) DepartureTrips++; SetState(BaggageTurnaroundState.OutboundDrive, Plan.outbound); }
                    break;
                case BaggageTurnaroundState.OutboundDrive:
                    if (Drive(dt))
                    {
                        if (cancelled) BeginWithdrawal(false);
                        else SetState(BaggageTurnaroundState.LoadingHold);
                    }
                    break;
                case BaggageTurnaroundState.LoadingHold:
                    if (cancelled && TransferBatch == null) BeginWithdrawal(false);
                    else if (Handle(outbound, Fleet.Cart, Hold, dt, true))
                    {
                        if (!cancelled && outbound.Exists(b => b.Location == Fleet.Dispatch)) SetState(BaggageTurnaroundState.ReloadDrive, Plan.home);
                        else BeginWithdrawal(false);
                    }
                    break;
                case BaggageTurnaroundState.ReloadDrive:
                    if (Drive(dt))
                    {
                        if (cancelled) BeginWithdrawal(true);
                        else SetState(BaggageTurnaroundState.CollectingDepartures);
                    }
                    break;
                case BaggageTurnaroundState.Withdrawing:
                    if (!trainHome) trainHome = Drive(dt);
                    if (trainHome && docked)
                    {
                        if (!Fleet.Crew.MoveHold(false, dt)) { Waiting = "hold handler returning to the loader"; break; }
                        State = BaggageTurnaroundState.Undocking; loaderDistance = 0f; break;
                    }
                    if (!loaderHome && trainHome && RaiseLoader(Fleet.Equipment.Loader.baggage.frontMinHeight, dt))
                        loaderHome = MoveLoader(Plan.loaderOut, dt);
                    if (trainHome && loaderHome && ReturnOutbound())
                    {
                        if (!Door(false, dt)) break;
                        if (Plan.dispatchPark != null && Vector2.Distance(Fleet.Train.Tractor.position,Plan.trainParking.Value.position)>.05f)
                            SetState(BaggageTurnaroundState.ParkingTrain,Plan.dispatchPark);
                        else Complete();
                    }
                    break;
                case BaggageTurnaroundState.ParkingTrain:
                    if (Drive(dt)) Complete();
                    break;
                case BaggageTurnaroundState.Undocking:
                    if (MoveLoader(Plan.loaderUndock, dt))
                    { docked = false; loaderDistance = 0f; State = BaggageTurnaroundState.Withdrawing; }
                    break;
            }
        }

        void Complete()
        {
            Fleet.TrainSpeed=Fleet.LoaderSpeed=0f;
            Fleet.Equipment.Release(this); Fleet.Job=null;
            Fleet.Station.depot?.ReleaseCrew(Fleet);
            State=cancelled ? BaggageTurnaroundState.Cancelled : BaggageTurnaroundState.Complete;
            if (cancelled) Waiting="turnaround cancelled; baggage retained at its recorded locations";
        }

        void AtDispatch()
        {
            if (cancelled) { BeginWithdrawal(true); return; }
            if (inbound.Exists(b => b.Location == Hold)) { SetState(BaggageTurnaroundState.InboundDrive, Plan.toStand); return; }
            if (Hold.Flight != departureFlight && !Hold.AssignFlight(departureFlight)) { Waiting = "incoming hold is not empty"; return; }
            SetState(BaggageTurnaroundState.CollectingDepartures);
        }

        void BeginWithdrawal(bool alreadyHome)
        {
            trainHome = alreadyHome; loaderHome = false; loaderDistance = 0f;
            SetState(BaggageTurnaroundState.Withdrawing, alreadyHome ? null : Plan.home);
        }

        bool ReturnOutbound()
        {
            foreach (var batch in outbound)
                if (Fleet.IsCart(batch.Location) && !coordinator.Ledger.Transfer(batch, batch.Location, Fleet.Dispatch, out var reason))
                { Waiting = reason; return false; }
            return Fleet.CartCount == 0;
        }

        void SetState(BaggageTurnaroundState state, VehiclePath path = null)
        {
            State = state; index = 0; handling = 0f;
            TransferBatch = null; TransferFrom = TransferTo = null; TransferSeconds = 0f;
            if (path != null) Fleet.Train.Continue(path);
        }

        bool Handle(List<BaggageBatch> batches, BaggageStore from, BaggageStore to, float dt, bool onlyPresent = false, bool partialLoad = false)
        {
            if (Fleet.Station.accessBlocked) { Waiting = "baggage transfer access is blocked"; return false; }
            if ((from == Hold || to == Hold) && Aircraft != null && !Aircraft.CargoDoors.IsOpen(HoldConfiguration.cargoPoint))
            { Waiting = "waiting for the cargo door to open"; return false; }
            bool fromCart = Fleet.IsCart(from), toCart = Fleet.IsCart(to);
            while (index < batches.Count && onlyPresent && !(fromCart ? Fleet.IsCart(batches[index].Location) : batches[index].Location == from))
            {
                var location = batches[index].Location;
                bool known = batches == inbound
                    ? location == Hold || Fleet.IsCart(location) || location == Fleet.Arrivals || location == coordinator.Reclaim
                    : location == Fleet.Dispatch || Fleet.IsCart(location) || location == Hold;
                if (!known) { Waiting = "manifest batch moved from its assigned source"; return false; }
                index++;
            }
            if (index >= batches.Count) return true;
            var batch = batches[index];
            if (fromCart)
            { Fleet.SelectCart(Array.IndexOf(Fleet.Carts,batch.Location)); from = Fleet.Cart; }
            if (toCart)
            {
                int selected = Array.FindIndex(Fleet.Carts,c => c.Refusal(batch) == null);
                if (selected < 0)
                { if (partialLoad && Fleet.CartCount > 0) return true; Waiting = "all baggage carts are full"; return false; }
                Fleet.SelectCart(selected); to = Fleet.Cart;
            }
            if (batch.Location != from) { Waiting = "manifest batch moved from its assigned source"; return false; }
            if (to == Hold && !batch.Screened) { Waiting = "departing baggage awaiting screening"; return false; }
            if (to == Fleet.Cart && State == BaggageTurnaroundState.CollectingDepartures && !batch.Screened)
            { Waiting = "departing baggage awaiting screening"; return false; }
            Waiting = to.Refusal(batch);
            if (partialLoad && Waiting != null && to.Count > 0 &&
                (to.Count + batch.Count > to.Capacity || to.Weight + batch.Weight > to.WeightCapacity + 0.001f))
            { Waiting = null; return true; }
            if (Waiting != null) return false;
            bool aircraftTransfer = from == Hold || to == Hold;
            bool depotTransfer=!aircraftTransfer && Fleet.Station.depot!=null;
            if (handling<=0f) Fleet.Station.depot?.Rebalance(Fleet);
            if (aircraftTransfer && !Fleet.Crew.MoveGround(true, dt, to == Hold))
            { Waiting = "tractor driver walking to the baggage cart"; return false; }
            if (aircraftTransfer || depotTransfer)
            { if (to == Hold) Packing.Reserve(batch); TransferBatch = batch; TransferFrom = from; TransferTo = to; }
            handling += dt;
            if (aircraftTransfer || depotTransfer) TransferSeconds = handling;
            float duration = aircraftTransfer || depotTransfer ? (batch.Count - 1) * TransferInterval + BagTravelSeconds : batch.Count * Fleet.Station.secondsPerBag;
            if (handling + 0.00001f < duration) return false;
            if (!coordinator.Ledger.Transfer(batch, from, to, out var reason)) { Waiting = reason; return false; }
            if (from == Hold) Packing.Release(batch);
            handling = 0f; index++;
            TransferBatch = null; TransferFrom = TransferTo = null; TransferSeconds = 0f;
            return cancelled && aircraftTransfer;
        }

        bool Drive(float dt)
        {
            if (!Fleet.Crew.MoveGround(false, dt)) { Waiting = "baggage handler returning to the tractor"; return false; }
            var train = Fleet.Train;
            train.Path.Sample(train.Distance, out _, out _, out float limit);
            float before = train.Distance;
            bool moved = train.Advance(Mathf.Min(limit, Fleet.Equipment.Cart.transitSpeed) * dt,
                (pose, type) => coordinator.Clear(this, pose, type), out var reason);
            Fleet.TrainSpeed = (train.Distance - before) / dt;
            if (!moved) Waiting = reason;
            return moved && train.Distance >= train.Path.Length - 0.001f;
        }

        bool Door(bool open, float dt)
        {
            if (!open && !Fleet.Crew.HoldSeated) { Waiting = "hold handler returning to the loader"; return false; }
            if (Aircraft == null) return !open;
            bool ready = Aircraft.CargoDoors.Operate(HoldConfiguration.cargoPoint, open, dt, out var reason);
            if (!ready) Waiting = reason;
            return ready;
        }

        bool RaiseLoader(float height, float dt)
        {
            Fleet.LoaderHeight = Mathf.MoveTowards(Fleet.LoaderHeight, height, dt * 0.25f);
            return Mathf.Abs(Fleet.LoaderHeight - height) < 0.001f;
        }

        bool MoveLoader(VehiclePath path, float dt)
        {
            if (!Fleet.Crew.HoldSeated) { Waiting = "waiting for the loader operator"; return false; }
            path.Sample(loaderDistance, out _, out _, out float limit);
            float before = loaderDistance;
            float target = Mathf.Min(path.Length, loaderDistance + Mathf.Min(limit, Fleet.Equipment.Loader.transitSpeed) * dt);
            while (loaderDistance < target - 0.00001f)
            {
                float next = Mathf.Min(target, loaderDistance + 0.05f);
                var position = path.Pose(next, out var facing, out _);
                var pose = new BaggageVehiclePose(position, facing);
                if (!coordinator.Clear(this, pose, Fleet.Equipment.Loader)) { Waiting = "belt loader access is blocked"; return false; }
                Fleet.LoaderPose = pose; loaderDistance = next;
                Fleet.LoaderSpeed = (loaderDistance - before) / dt * (path.Reversing(loaderDistance) ? -1f : 1f);
            }
            return loaderDistance >= path.Length - 0.001f;
        }
    }
}
