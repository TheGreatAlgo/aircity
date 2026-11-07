using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>A logical batch. Meshes never own baggage; its ledger records exactly one location.</summary>
    public sealed class BaggageBatch
    {
        public string Id { get; }
        public string Flight { get; }
        public int Count { get; }
        public float Weight { get; }
        public bool Screened { get; internal set; }
        public BaggageStore Location { get; internal set; }

        internal BaggageBatch(string id, string flight, int count, float weight, bool screened)
        { Id = id; Flight = flight; Count = count; Weight = weight; Screened = screened; }
    }

    public sealed class BaggageStore
    {
        public string Name { get; }
        public string Flight { get; private set; }
        public int Capacity { get; private set; }
        public float WeightCapacity { get; private set; }
        public bool RequiresScreening { get; }
        public int Count { get; internal set; }
        public float Weight { get; internal set; }
        internal BaggageLedger Ledger { get; }
        internal ContainerUnit Container { get; set; }

        internal BaggageStore(BaggageLedger ledger, string name, int capacity, float weightCapacity,
            string flight, bool requiresScreening)
        {
            Ledger = ledger; Name = name; Capacity = capacity; WeightCapacity = weightCapacity;
            Flight = flight; RequiresScreening = requiresScreening;
        }

        internal string Refusal(BaggageBatch batch)
        {
            if (Container != null && Container.Sealed) return "container is sealed";
            float bayAddition = batch.Location?.Container?.Location == Container?.Location ? 0f : batch.Weight;
            if (Container != null && Container.Location != null && Container.Location.Weight + bayAddition > Container.Location.WeightCapacity + 0.001f)
                return "container bay weight capacity exceeded";
            if (Flight != null && Flight != batch.Flight) return "wrong flight for " + Name;
            if (RequiresScreening && !batch.Screened) return "baggage awaiting screening";
            if (Count + batch.Count > Capacity || Weight + batch.Weight > WeightCapacity + 0.001f)
                return Name + " is full";
            return null;
        }

        public bool AssignFlight(string flight)
        {
            if (Count != 0 || string.IsNullOrWhiteSpace(flight)) return false;
            Flight = flight;
            return true;
        }

        internal void Resize(int capacity,float weightCapacity)
        {
            if (capacity<Count || weightCapacity<Weight) throw new ArgumentException("A baggage store cannot shrink below its contents.");
            Capacity=capacity; WeightCapacity=weightCapacity;
        }
    }

    /// <summary>Atomic transfers of immutable counts/weights. Duplicate IDs and stale handovers are refused.</summary>
    public sealed class BaggageLedger
    {
        readonly Dictionary<string, BaggageBatch> batches = new Dictionary<string, BaggageBatch>();
        public IEnumerable<BaggageBatch> Batches => batches.Values;
        public int Count { get; private set; }
        public float Weight { get; private set; }
        public int Transfers { get; private set; }

        public BaggageStore Store(string name, int capacity, float weightCapacity,
            string flight = null, bool requiresScreening = false)
        {
            if (string.IsNullOrWhiteSpace(name) || capacity < 0 || !Finite(weightCapacity) || weightCapacity < 0f)
                throw new ArgumentException("A store needs a name and finite, nonnegative capacities.");
            return new BaggageStore(this, name, capacity, weightCapacity, flight, requiresScreening);
        }

        public BaggageBatch Add(string id, string flight, int count, float weight, bool screened, BaggageStore location)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(flight) || count <= 0 ||
                !Finite(weight) || weight <= 0f || location == null || location.Ledger != this)
                throw new ArgumentException("A batch needs an ID, flight, positive count/weight and a store in this ledger.");
            if (batches.ContainsKey(id)) throw new ArgumentException("Duplicate baggage batch: " + id);
            var batch = new BaggageBatch(id, flight, count, weight, screened);
            string refusal = location.Refusal(batch);
            if (refusal != null) throw new ArgumentException(refusal);
            batches.Add(id, batch);
            Move(batch, location);
            Count += count; Weight += weight;
            return batch;
        }

        public bool Transfer(BaggageBatch batch, BaggageStore from, BaggageStore to, out string reason)
        {
            reason = null;
            if (batch == null || !batches.TryGetValue(batch.Id, out var known) || known != batch ||
                from == null || to == null || from.Ledger != this || to.Ledger != this)
                reason = "baggage and stores must belong to this ledger";
            else if (from == to || batch.Location != from) reason = "baggage is no longer at the handover source";
            else if (from.Container != null && from.Container.Sealed) reason = "container is sealed";
            else reason = to.Refusal(batch);
            if (reason != null) return false;
            Move(batch, to);
            Transfers++;
            return true;
        }

        public void Screen(BaggageBatch batch)
        {
            if (batch == null || !batches.TryGetValue(batch.Id, out var known) || known != batch)
                throw new ArgumentException("Unknown baggage batch.");
            batch.Screened = true;
        }

        static void Move(BaggageBatch batch, BaggageStore to)
        {
            if (batch.Location != null)
            { batch.Location.Count -= batch.Count; batch.Location.Weight -= batch.Weight; }
            batch.Location = to;
            to.Count += batch.Count; to.Weight += batch.Weight;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Explicit bulk capability, attached to an operation rather than inferred from a CargoDoor.</summary>
    [Serializable]
    public sealed class BulkHoldConfiguration
    {
        public AircraftType aircraft;
        public string cargoPoint = "Cargo_Fwd";
        [Min(1)] public int bagCapacity = 100;
        [Min(1f)] public float weightCapacity = 1500f;

        public bool Resolve(VehicleType loader, out AircraftType.ServicePoint point, out string reason)
        {
            point = default;
            reason = null;
            if (aircraft == null || !aircraft.TryGetPoint(cargoPoint, out point) || point.type != ServicePointType.CargoDoor)
                reason = "bulk hold needs an explicit cargo-door point";
            else if (bagCapacity <= 0 || !BaggageLedger.Finite(weightCapacity) || weightCapacity <= 0f)
                reason = "bulk hold needs positive count and weight capacities";
            else if (loader == null || loader.kind != VehicleKind.BeltLoader || loader.baggage == null)
                reason = "bulk hold requires a belt loader";
            else if (point.position.y < loader.baggage.frontMinHeight || point.position.y > loader.baggage.frontMaxHeight)
                reason = "cargo sill is outside the belt loader's reach";
            else if (new Vector2(point.normal.x, point.normal.z).sqrMagnitude < 0.5f)
                reason = "cargo point needs an outward approach normal";
            return reason == null;
        }
    }
}
