using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum ContainerTurnaroundState
    {
        WaitingAircraft, WaitingEquipment, LoaderApproach, SettingUp, ToStand, HoldToFront, AlignMainHigh,
        FrontToMain, LowerMain, MainToDolly, ToArrivals, Unpacking, ToDispatch, Collecting, Outbound,
        DollyToMain, RaiseMain, MainToFront, FrontToHold, ReturnDispatch, Withdrawing, Complete, Cancelled, Docking, Undocking, ParkingTrain
    }

    /// <summary>One dolly, one dual-platform loader and any number of compatible containers up to the hold capacity.</summary>
    public sealed class ContainerTurnaround
    {
        readonly AirportBaggage owner;
        readonly List<ContainerUnit> inbound = new List<ContainerUnit>(), outbound = new List<ContainerUnit>();
        readonly string arrivalFlight;
        ContainerUnit current;
        bool closed, cancelled, trainHome, loaderHome, docked;
        float loaderDistance, retry, handling;
        string planningFailure;
        public Aircraft.Aircraft Aircraft { get; }
        public Airfield.Stand Stand { get; }
        public ContainerHoldConfiguration Configuration { get; }
        public ContainerFleet Fleet { get; }
        public ContainerBay Hold { get; }
        public string DepartureFlight { get; }
        public string ArrivalFlight => arrivalFlight;
        public BaggageServicePlan Plan { get; internal set; }
        public ContainerTurnaroundState State { get; private set; } = ContainerTurnaroundState.WaitingAircraft;
        public string Waiting { get; private set; }
        public bool Acquired => Plan != null;
        internal bool VisitEnded { get; set; }
        public bool Finished => State == ContainerTurnaroundState.Complete || State == ContainerTurnaroundState.Cancelled;
        public bool DepartureReady => State == ContainerTurnaroundState.Complete &&
            inbound.TrueForAll(u => u.Contents.Count == 0 && u.Location == Fleet.EmptyYard) && outbound.TrueForAll(u => u.Location == Hold);
        public ContainerUnit TransferUnit { get; private set; }
        public ContainerBay TransferFrom { get; private set; }
        public ContainerBay TransferTo { get; private set; }
        public float TransferProgress { get; private set; }
        public bool SideTransfer => TransferFrom != null && (State == ContainerTurnaroundState.MainToDolly || State == ContainerTurnaroundState.DollyToMain);
        public bool PlatformTransfer => TransferFrom != null;
        public bool DollyTransfer => TransferFrom == Fleet.Dolly || TransferTo == Fleet.Dolly;
        public int Unloaded { get; private set; }
        public int Loaded { get; private set; }
        Airfield.Airport Airport => Fleet.Station.Airport;
        float DeckHeight => Fleet.Station.dollyType.container.transfer.y;

        internal ContainerTurnaround(AirportBaggage owner, Aircraft.Aircraft aircraft, Airfield.Stand stand,
            ContainerHoldConfiguration configuration, ContainerFleet fleet, string arrival, string departure)
        {
            this.owner = owner; Aircraft = aircraft; Stand = stand; Fleet = fleet; arrivalFlight = arrival; DepartureFlight = departure;
            Configuration = new ContainerHoldConfiguration { aircraft = configuration.aircraft, cargoPoint = configuration.cargoPoint,
                acceptedCode = configuration.acceptedCode, slots = configuration.slots, maxGross = configuration.maxGross };
            Hold = owner.Containers.Bay(aircraft.name + " " + configuration.cargoPoint, ContainerBayKind.Hold,
                configuration.slots, configuration.maxGross, configuration.acceptedCode, arrival);
        }
        public bool Seal(out string reason)
        {
            reason = null;
            if (closed || Finished) { reason = "container manifests are already closed"; return false; }
            inbound.Clear(); outbound.Clear();
            foreach (var unit in owner.Containers.Units)
            {
                if (unit.Location == Hold) inbound.Add(unit);
                if (unit.Location == Fleet.Dispatch && unit.Flight == DepartureFlight) outbound.Add(unit);
            }
            float weight = 0f;
            foreach (var unit in outbound) weight += unit.GrossWeight;
            if (inbound.Exists(u => !u.Sealed) || outbound.Exists(u => !u.Sealed)) reason = "close each ULD before closing manifests";
            else if (outbound.Count > Hold.Capacity || weight > Hold.WeightCapacity) reason = "container manifest exceeds hold slots or gross capacity";
            if (reason != null) { inbound.Clear(); outbound.Clear(); return false; }
            foreach (var unit in inbound)
                if (!Configuration.Resolve(unit.Type, Fleet.Station.dollyType, Fleet.Station.loaderType, out _, out reason))
                { inbound.Clear(); outbound.Clear(); return false; }
            foreach (var unit in outbound)
                if (!Configuration.Resolve(unit.Type, Fleet.Station.dollyType, Fleet.Station.loaderType, out _, out reason))
                { inbound.Clear(); outbound.Clear(); return false; }
            inbound.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); outbound.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            closed = true; return true;
        }
        public void Cancel() { if (!Finished) cancelled = true; }
        public bool Retry(out string reason)
        {
            reason = null;
            if (State != ContainerTurnaroundState.Cancelled || !closed || !Parked())
            { reason = "retry needs a cancelled manifest and the aircraft on its original stand"; return false; }
            cancelled = false; Plan = null; retry = 0f; current = null; loaderDistance = 0f;
            Set(ContainerTurnaroundState.WaitingAircraft); return true;
        }
        bool Parked()
        {
            if (Aircraft == null || !Aircraft.isActiveAndEnabled || Aircraft.type != Configuration.aircraft || Aircraft.Moving ||
                !Airport.Taxiways.stands.Contains(Stand)) return false;
            var layout = Airport.Taxiways.Layout(Stand);
            if (layout == null || layout.edge == null || !layout.ParkingFits(Aircraft.type) || Stand.ServiceFor(Aircraft.type)?.allowed == false) return false;
            var p = Airport.transform.InverseTransformPoint(Aircraft.transform.position);
            var f = BaggageServicePlan.XZ(Airport.transform.InverseTransformDirection(Aircraft.transform.forward));
            return Mathf.Abs(p.y) < 0.5f && Vector2.Distance(BaggageServicePlan.XZ(p), layout.ParkPosition(Aircraft.type)) < 1.5f && Vector2.Angle(f, layout.nose) < 6f;
        }
        internal void Step(float dt)
        {
            Waiting = null;
            if (Acquired)
            {
                Plan.RefreshGeometry(); Fleet.TrainSpeed = Fleet.LoaderSpeed = 0f;
                if (!Parked() || Airport.LayoutVersion != Plan.layoutVersion || !Fleet.Station.isActiveAndEnabled ||
                    Vector3.Distance(Aircraft.transform.position, Plan.aircraftPosition) > 0.1f ||
                    Quaternion.Angle(Aircraft.transform.rotation, Plan.aircraftRotation) > 0.5f) cancelled = true;
            }
            if (cancelled && !Acquired) { State = ContainerTurnaroundState.Cancelled; Waiting = "container handling cancelled"; return; }
            if (!cancelled && closed && !ManifestLocationsValid())
            { Waiting = "container moved from its assigned manifest location"; return; }
            switch (State)
            {
                case ContainerTurnaroundState.WaitingAircraft:
                    if (!closed) Waiting = "waiting for closed container manifests";
                    else if (!Parked()) Waiting = "waiting for the aircraft to park";
                    else Set(ContainerTurnaroundState.WaitingEquipment);
                    break;
                case ContainerTurnaroundState.WaitingEquipment:
                    retry -= dt;
                    if (retry > 0f) { Waiting = planningFailure ?? "waiting for a usable container service plan"; break; }
                    retry = 1f;
                    if (!owner.StartContainer(this, out var reason))
                    {
                        Waiting = reason;
                        if (planningFailure != reason) Debug.Log("Container planning: " + reason);
                        planningFailure = reason; break;
                    }
                    Set(ContainerTurnaroundState.LoaderApproach); break;
                case ContainerTurnaroundState.LoaderApproach:
                    if (MoveLoader(Plan.loaderIn, dt)) Set(ContainerTurnaroundState.SettingUp);
                    break;
                case ContainerTurnaroundState.SettingUp:
                    if (cancelled) { Withdraw(true); break; }
                    if (!Door(true, dt)) break;
                    if (Height(false, Plan.sill, dt)) { loaderDistance = 0f; Set(ContainerTurnaroundState.Docking); }
                    break;
                case ContainerTurnaroundState.Docking:
                    if (!MoveLoader(Plan.loaderDock, dt)) break;
                    docked = true; loaderDistance = 0f;
                    if (cancelled) { Withdraw(true); break; }
                    Set(ContainerTurnaroundState.ToStand, Plan.initial ?? Plan.toStand);
                    break;
                case ContainerTurnaroundState.ToStand:
                    Fleet.Stabilisers = Mathf.MoveTowards(Fleet.Stabilisers, 1f, dt);
                    if (Fleet.Stabilisers != 1f) break;
                    if (Drive(dt))
                    {
                        if (cancelled) Withdraw(false);
                        else if ((current = inbound.Find(u => u.Location == Hold)) != null) Set(ContainerTurnaroundState.HoldToFront);
                        else Set(ContainerTurnaroundState.ReturnDispatch, Plan.home);
                    }
                    break;
                case ContainerTurnaroundState.HoldToFront:
                    if (cancelled && TransferProgress == 0f) { Withdraw(false); break; }
                    if (Transfer(current, Hold, Fleet.Front, dt)) Set(ContainerTurnaroundState.AlignMainHigh);
                    break;
                case ContainerTurnaroundState.AlignMainHigh:
                    if (Height(true, Fleet.FrontHeight, dt)) Set(ContainerTurnaroundState.FrontToMain);
                    break;
                case ContainerTurnaroundState.FrontToMain:
                    if (Transfer(current, Fleet.Front, Fleet.Main, dt)) Set(ContainerTurnaroundState.LowerMain);
                    break;
                case ContainerTurnaroundState.LowerMain:
                    if (Height(true, DeckHeight, dt)) Set(ContainerTurnaroundState.MainToDolly);
                    break;
                case ContainerTurnaroundState.MainToDolly:
                    if (Transfer(current, Fleet.Main, Fleet.Dolly, dt))
                        Set(Incoming(current) ? ContainerTurnaroundState.ToArrivals : ContainerTurnaroundState.ReturnDispatch,
                            Incoming(current) ? Plan.toArrivals : Plan.home);
                    break;
                case ContainerTurnaroundState.ToArrivals:
                    if (Drive(dt)) Set(ContainerTurnaroundState.Unpacking);
                    break;
                case ContainerTurnaroundState.Unpacking:
                    if (current.Location == Fleet.Dolly && !Transfer(current, Fleet.Dolly, Fleet.Arrivals, dt)) break;
                    handling += dt;
                    if (handling < current.Contents.Count * Fleet.Station.unpackSecondsPerBag) break;
                    if (!owner.Containers.Unpack(current, Fleet.ArrivalsBags, out var why)) { Waiting = why; break; }
                    if (!owner.Containers.Transfer(current, Fleet.Arrivals, Fleet.EmptyYard, out why)) { Waiting = why; break; }
                    Unloaded++; current = null;
                    if (Plan.toDispatch != null) Set(ContainerTurnaroundState.ToDispatch, Plan.toDispatch); else AtDispatch();
                    break;
                case ContainerTurnaroundState.ToDispatch:
                case ContainerTurnaroundState.ReturnDispatch:
                    if (Drive(dt)) AtDispatch();
                    break;
                case ContainerTurnaroundState.Collecting:
                    if (cancelled && TransferProgress == 0f) { Withdraw(true); break; }
                    if (!Screened(current)) { Waiting = "container baggage awaiting screening"; break; }
                    if (Transfer(current, Fleet.Dispatch, Fleet.Dolly, dt))
                    {
                        if (cancelled) Withdraw(true); else Set(ContainerTurnaroundState.Outbound, Plan.outbound);
                    }
                    break;
                case ContainerTurnaroundState.Outbound:
                    if (Drive(dt))
                    {
                        if (cancelled) Set(ContainerTurnaroundState.ReturnDispatch, Plan.home);
                        else Set(ContainerTurnaroundState.DollyToMain);
                    }
                    break;
                case ContainerTurnaroundState.DollyToMain:
                    if (cancelled && TransferProgress == 0f) { Set(ContainerTurnaroundState.ReturnDispatch, Plan.home); break; }
                    if (Height(true, DeckHeight, dt) && Transfer(current, Fleet.Dolly, Fleet.Main, dt)) Set(ContainerTurnaroundState.RaiseMain);
                    break;
                case ContainerTurnaroundState.RaiseMain:
                    if (cancelled) Set(ContainerTurnaroundState.LowerMain);
                    else if (Height(true, Fleet.FrontHeight, dt)) Set(ContainerTurnaroundState.MainToFront);
                    break;
                case ContainerTurnaroundState.MainToFront:
                    if (cancelled && TransferProgress == 0f) { Set(ContainerTurnaroundState.LowerMain); break; }
                    if (Transfer(current, Fleet.Main, Fleet.Front, dt)) Set(ContainerTurnaroundState.FrontToHold);
                    break;
                case ContainerTurnaroundState.FrontToHold:
                    if (cancelled && TransferProgress == 0f) { Set(ContainerTurnaroundState.AlignMainHigh); break; }
                    if (Transfer(current, Fleet.Front, Hold, dt))
                    { Loaded++; current = null; Set(ContainerTurnaroundState.ReturnDispatch, Plan.home); }
                    break;
                case ContainerTurnaroundState.Withdrawing:
                    if (!trainHome) trainHome = Drive(dt);
                    if (!trainHome) break;
                    if (Fleet.Dolly.Count > 0)
                    {
                        if (current == null || Incoming(current)) { Waiting = "recover the dolly's container before releasing equipment"; break; }
                        if (!Transfer(current, Fleet.Dolly, Fleet.Dispatch, dt)) break;
                        current = null;
                    }
                    if (Fleet.Main.Count != 0 || Fleet.Front.Count != 0) { Waiting = "container remains on the loader"; break; }
                    Fleet.Stabilisers = Mathf.MoveTowards(Fleet.Stabilisers, 0f, dt);
                    if (Fleet.Stabilisers != 0f) break;
                    if (docked) { loaderDistance = 0f; Set(ContainerTurnaroundState.Undocking); break; }
                    bool mainDown = Height(true, DeckHeight, dt), frontDown = Height(false, Fleet.Station.loaderType.container.frontMinHeight, dt);
                    if (!mainDown || !frontDown) break;
                    if (!loaderHome) loaderHome = MoveLoader(Plan.loaderOut, dt);
                    if (loaderHome)
                    {
                        if (!Door(false, dt)) break;
                        if (Plan.dispatchPark != null && Vector2.Distance(Fleet.Train.Tractor.position,Plan.trainParking.Value.position)>.05f)
                            Set(ContainerTurnaroundState.ParkingTrain,Plan.dispatchPark);
                        else Complete();
                    }
                    break;
                case ContainerTurnaroundState.ParkingTrain:
                    if (Drive(dt)) Complete();
                    break;
                case ContainerTurnaroundState.Undocking:
                    if (MoveLoader(Plan.loaderUndock, dt))
                    { docked = false; loaderDistance = 0f; Set(ContainerTurnaroundState.Withdrawing); }
                    break;
            }
        }
        void Complete()
        {
            Fleet.TrainSpeed=Fleet.LoaderSpeed=0f;
            Fleet.Job=null; Set(cancelled ? ContainerTurnaroundState.Cancelled : ContainerTurnaroundState.Complete);
            if (cancelled) Waiting="cancelled; all containers and baggage retained at their recorded locations";
        }
        bool Incoming(ContainerUnit unit) => unit != null && inbound.Contains(unit);
        bool ManifestLocationsValid()
        {
            foreach (var unit in inbound)
                if (unit.Location != Hold && unit.Location != Fleet.Dolly && unit.Location != Fleet.Main && unit.Location != Fleet.Front &&
                    unit.Location != Fleet.Arrivals && unit.Location != Fleet.EmptyYard) return false;
            foreach (var unit in outbound)
                if (unit.Location != Hold && unit.Location != Fleet.Dolly && unit.Location != Fleet.Main && unit.Location != Fleet.Front && unit.Location != Fleet.Dispatch) return false;
            return true;
        }
        bool Screened(ContainerUnit unit)
        {
            foreach (var batch in owner.Ledger.Batches) if (batch.Location == unit.Contents && !batch.Screened) return false;
            return true;
        }
        void AtDispatch()
        {
            if (cancelled) { Withdraw(true); return; }
            if (inbound.Exists(u => u.Location == Hold)) { Set(ContainerTurnaroundState.ToStand, Plan.toStand); return; }
            if (Hold.Flight != DepartureFlight && !Hold.AssignFlight(DepartureFlight)) { Waiting = "incoming container hold is not empty"; return; }
            current = outbound.Find(u => u.Location == Fleet.Dispatch);
            if (current != null) Set(ContainerTurnaroundState.Collecting); else Withdraw(true);
        }
        void Withdraw(bool home)
        {
            trainHome = home; loaderHome = false; loaderDistance = 0f; Set(ContainerTurnaroundState.Withdrawing, home ? null : Plan.home);
        }
        void Set(ContainerTurnaroundState state, VehiclePath path = null)
        {
            State = state; TransferUnit = null; TransferFrom = TransferTo = null; TransferProgress = 0f; handling = 0f;
            if (path != null) Fleet.Train.Continue(path);
        }
        bool Transfer(ContainerUnit unit, ContainerBay from, ContainerBay to, float dt)
        {
            if ((from == Hold || to == Hold) && Aircraft != null && !Aircraft.CargoDoors.IsOpen(Configuration.cargoPoint))
            { Waiting = "waiting for the cargo door to open"; return false; }
            if (Fleet.Station.accessBlocked) { Waiting = "container transfer access is blocked"; return false; }
            if (unit == null || unit.Location != from) { Waiting = "container moved from its assigned source"; return false; }
            var refusal = to.Refusal(unit, false);
            if (refusal != null) { Waiting = refusal; return false; }
            if (from == Fleet.Main && to == Fleet.Front || from == Fleet.Front && to == Fleet.Main)
                if (Mathf.Abs(Fleet.MainHeight - Fleet.FrontHeight) > 0.001f) { Waiting = "loader platforms are not aligned"; return false; }
            if (from == Fleet.Dolly && to == Fleet.Main || from == Fleet.Main && to == Fleet.Dolly)
            {
                if (Mathf.Abs(Fleet.MainHeight - DeckHeight) > 0.001f) { Waiting = "dolly and main platform are not aligned"; return false; }
                if (!DollyDeck(-Vector2.SignedAngle(Fleet.Train.Cart.Right, Fleet.LoaderPose.facing), dt)) return false;
            }
            TransferUnit = unit; TransferFrom = from; TransferTo = to;
            float distance = Vector3.Distance(Fleet.Mount(from, out _), Fleet.Mount(to, out _));
            TransferProgress = Mathf.Min(1f, TransferProgress + dt * 0.5f / Mathf.Max(1f, distance));
            if (TransferProgress < 1f) return false;
            if (!owner.Containers.Transfer(unit, from, to, out var reason)) { Waiting = reason; return false; }
            return true;
        }
        bool Height(bool main, float height, float dt)
        {
            if (Fleet.Station.accessBlocked) { Waiting = "container loader access is blocked"; return false; }
            if (main) Fleet.MainHeight = Mathf.MoveTowards(Fleet.MainHeight, height, dt * 0.3f);
            else Fleet.FrontHeight = Mathf.MoveTowards(Fleet.FrontHeight, height, dt * 0.3f);
            return Mathf.Abs((main ? Fleet.MainHeight : Fleet.FrontHeight) - height) < 0.001f;
        }

        bool Door(bool open, float dt)
        {
            if (Aircraft == null) return !open;
            bool ready = Aircraft.CargoDoors.Operate(Configuration.cargoPoint, open, dt, out var reason);
            if (!ready) Waiting = reason;
            return ready;
        }
        bool Drive(float dt)
        {
            if (!DollyDeck(0f, dt)) return false;
            var train = Fleet.Train; train.Path.Sample(train.Distance, out _, out _, out float limit);
            float before = train.Distance;
            bool moved = train.Advance(Mathf.Min(limit, Fleet.Station.dollyType.transitSpeed) * dt,
                (pose, type) => owner.ClearContainer(this, pose, type), out var reason);
            Fleet.TrainSpeed = (train.Distance - before) / dt;
            if (!moved) Waiting = reason;
            return moved && train.Distance >= train.Path.Length - 0.001f;
        }

        bool DollyDeck(float angle, float dt)
        {
            Fleet.DollyAngle = Mathf.MoveTowardsAngle(Fleet.DollyAngle, angle, dt * 45f);
            if (Mathf.Abs(Mathf.DeltaAngle(Fleet.DollyAngle, angle)) < .001f) return true;
            Waiting = "aligning the dolly turntable";
            return false;
        }
        bool MoveLoader(VehiclePath path, float dt)
        {
            path.Sample(loaderDistance, out _, out _, out float limit);
            float before = loaderDistance;
            float target = Mathf.Min(path.Length, loaderDistance + Mathf.Min(limit, Fleet.Station.loaderType.transitSpeed) * dt);
            while (loaderDistance < target - 0.00001f)
            {
                float next = Mathf.Min(target, loaderDistance + 0.05f);
                var p = path.Pose(next, out var f, out _); var pose = new BaggageVehiclePose(p, f);
                if (!owner.ClearContainer(this, pose, Fleet.Station.loaderType)) { Waiting = "container loader access is blocked"; return false; }
                Fleet.LoaderPose = pose; loaderDistance = next;
                Fleet.LoaderSpeed = (loaderDistance - before) / dt * (path.Reversing(loaderDistance) ? -1f : 1f);
            }
            return loaderDistance >= path.Length - 0.001f;
        }
    }
}
