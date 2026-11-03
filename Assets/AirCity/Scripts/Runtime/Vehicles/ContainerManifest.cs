using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum ContainerBayKind { Dispatch, Arrivals, Dolly, MainPlatform, FrontPlatform, Hold, EmptyYard }

    public sealed class ContainerUnit
    {
        public string Id { get; }
        public string Flight { get; }
        public string Code { get; }
        public VehicleType Type { get; }
        public float Tare { get; }
        public float GrossWeight => Tare + Contents.Weight;
        public BaggageStore Contents { get; }
        public ContainerBay Location { get; internal set; }
        public bool Sealed { get; private set; }
        internal ContainerLedger Ledger { get; }
        internal ContainerUnit(ContainerLedger ledger, string id, string flight, VehicleType type, int bags)
        {
            Ledger = ledger; Id = id; Flight = flight; Type = type; Code = type.container.code; Tare = type.container.tare;
            Contents = ledger.Baggage.Store(id + " contents", bags, type.container.maxGross - Tare, flight);
            Contents.Container = this;
        }
        public void Seal() => Sealed = true;
        internal void Open() => Sealed = false;
    }

    public sealed class ContainerBay
    {
        internal ContainerLedger Ledger { get; }
        public string Name { get; }
        public string Flight { get; private set; }
        public string Code { get; }
        public int Capacity { get; }
        public float WeightCapacity { get; }
        public ContainerBayKind Kind { get; }
        public IEnumerable<ContainerUnit> Units { get { foreach (var unit in Ledger.Units) if (unit.Location == this) yield return unit; } }
        public int Count { get { int n = 0; foreach (var _ in Units) n++; return n; } }
        public float Weight { get { float n = 0; foreach (var unit in Units) n += unit.GrossWeight; return n; } }
        internal ContainerBay(ContainerLedger ledger, string name, ContainerBayKind kind, int capacity, float weight, string code, string flight)
        { Ledger = ledger; Name = name; Kind = kind; Capacity = capacity; WeightCapacity = weight; Code = code; Flight = flight; }
        public bool AssignFlight(string flight)
        { if (Count != 0 || string.IsNullOrWhiteSpace(flight)) return false; Flight = flight; return true; }
        internal string Refusal(ContainerUnit unit, bool creating)
        {
            if (Code != null && Code != unit.Code) return "incompatible container code for " + Name;
            if (Flight != null && Flight != unit.Flight) return "wrong flight for " + Name;
            if (Count >= Capacity || Weight + unit.GrossWeight > WeightCapacity + 0.001f) return Name + " is full";
            if (!creating && Kind != ContainerBayKind.Dispatch && Kind != ContainerBayKind.Arrivals && Kind != ContainerBayKind.EmptyYard && !unit.Sealed)
                return "container must be sealed before transport";
            if (!creating && Kind == ContainerBayKind.Hold)
                foreach (var batch in Ledger.Baggage.Batches)
                    if (batch.Location == unit.Contents && !batch.Screened) return "container baggage awaiting screening";
            return null;
        }
    }

    /// <summary>Container moves are atomic. Bag records stay inside the same contents store during transport.</summary>
    public sealed class ContainerLedger
    {
        readonly Dictionary<string, ContainerUnit> units = new Dictionary<string, ContainerUnit>();
        public BaggageLedger Baggage { get; }
        public IEnumerable<ContainerUnit> Units => units.Values;
        public int Transfers { get; private set; }
        public ContainerLedger(BaggageLedger baggage) => Baggage = baggage ?? throw new ArgumentNullException(nameof(baggage));
        public ContainerBay Bay(string name, ContainerBayKind kind, int capacity, float weight, string code = "AKE", string flight = null)
        {
            if (string.IsNullOrWhiteSpace(name) || capacity < 0 || !BaggageLedger.Finite(weight) || weight < 0f)
                throw new ArgumentException("A container bay needs a name and finite nonnegative capacities.");
            return new ContainerBay(this, name, kind, capacity, weight, code, flight);
        }
        public ContainerUnit Create(string id, string flight, VehicleType type, int bagCapacity, ContainerBay bay)
        {
            var c = type != null ? type.container : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(flight) || units.ContainsKey(id) ||
                c == null || type.kind != VehicleKind.ContainerULD || string.IsNullOrWhiteSpace(c.code) ||
                !BaggageLedger.Finite(c.maxGross) || !BaggageLedger.Finite(c.tare) || c.tare <= 0f || c.maxGross <= c.tare ||
                bagCapacity < 1 || bay == null || bay.Ledger != this)
                throw new ArgumentException("A container needs a unique ID, flight, compatible type, capacity and a bay in this ledger.");
            var unit = new ContainerUnit(this, id, flight, type, bagCapacity);
            string reason = bay.Refusal(unit, true);
            if (reason != null) throw new ArgumentException(reason);
            units.Add(id, unit); unit.Location = bay;
            return unit;
        }
        public bool Transfer(ContainerUnit unit, ContainerBay from, ContainerBay to, out string reason)
        {
            reason = null;
            if (unit == null || !units.TryGetValue(unit.Id, out var known) || known != unit || from == null || to == null ||
                from.Ledger != this || to.Ledger != this) reason = "container and bays must belong to this ledger";
            else if (from == to || unit.Location != from) reason = "container is no longer at the handover source";
            else reason = to.Refusal(unit, false);
            if (reason != null) return false;
            unit.Location = to; Transfers++; return true;
        }
        public bool Pack(ContainerUnit unit, BaggageBatch batch, BaggageStore from, out string reason)
        {
            reason = null;
            if (unit == null || unit.Ledger != this) { reason = "unknown container"; return false; }
            if (unit.Sealed || unit.Location.Kind != ContainerBayKind.Dispatch && unit.Location.Kind != ContainerBayKind.Hold)
            { reason = "packing needs an open container at make-up"; return false; }
            return Baggage.Transfer(batch, from, unit.Contents, out reason);
        }
        public bool Unpack(ContainerUnit unit, BaggageStore arrivals, out string reason)
        {
            reason = null;
            if (unit == null || unit.Ledger != this || unit.Location.Kind != ContainerBayKind.Arrivals)
            { reason = "unpacking needs a container at arrivals"; return false; }
            unit.Open();
            foreach (var batch in Baggage.Batches)
                if (batch.Location == unit.Contents && !Baggage.Transfer(batch, unit.Contents, arrivals, out reason)) return false;
            return true;
        }
    }

    [Serializable]
    public sealed class ContainerHoldConfiguration
    {
        public AircraftType aircraft;
        public string cargoPoint = "Cargo_Fwd", acceptedCode = "AKE";
        [Min(1)] public int slots = 4;
        [Min(1f)] public float maxGross = 6352f;
        public bool Resolve(VehicleType unit, VehicleType dolly, VehicleType loader, out AircraftType.ServicePoint point, out string reason)
        {
            point = default; reason = null;
            if (aircraft == null || !aircraft.TryGetPoint(cargoPoint, out point) || point.type != ServicePointType.CargoDoor)
                reason = "container hold needs an explicit cargo-door point";
            else if (slots < 1 || !BaggageLedger.Finite(maxGross) || maxGross <= 0f) reason = "container hold needs positive slots and gross capacity";
            else if (unit == null || unit.kind != VehicleKind.ContainerULD || unit.container == null || unit.container.code != acceptedCode)
                reason = "hold does not accept this ULD type";
            else if (dolly == null || dolly.kind != VehicleKind.ContainerDolly || dolly.container == null || dolly.container.code != acceptedCode ||
                dolly.container.maxGross < unit.container.maxGross) reason = "incompatible container dolly";
            else if (loader == null || loader.kind != VehicleKind.ContainerLoader || loader.container == null || loader.container.code != acceptedCode ||
                loader.container.maxGross < unit.container.maxGross || loader.container.deckWidth < unit.bounds.size.x)
                reason = "incompatible lower-deck loader";
            else if (point.position.y < loader.container.frontMinHeight || point.position.y > loader.container.frontMaxHeight)
                reason = "cargo sill is outside the container loader's reach";
            else if (point.size.y < unit.container.height || point.size.x < unit.bounds.size.x)
                reason = "ULD contour does not fit the cargo opening";
            else if (dolly.container.transfer.y < loader.container.mainMinHeight || dolly.container.transfer.y > loader.container.mainMaxHeight)
                reason = "dolly deck cannot align with the main platform";
            else if (new Vector2(point.normal.x, point.normal.z).sqrMagnitude < 0.5f) reason = "cargo point needs an outward approach normal";
            return reason == null;
        }
    }
}
