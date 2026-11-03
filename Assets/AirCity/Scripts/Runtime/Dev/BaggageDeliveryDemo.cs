using System;
using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    /// <summary>A standalone operational review, using the same headless delivery model as the tests.</summary>
    public sealed class BaggageDeliveryDemo : MonoBehaviour
    {
        public Aircraft.Aircraft aircraft;
        public BulkHoldConfiguration hold = new BulkHoldConfiguration();
        public TaxiwayNetwork roads;
        public int dispatchNode, handoffNode;
        public float dispatchBearing, handoffBearing = 180f;
        public BaggageEquipmentRig tractor, cart, loader;
        public GameObject accessBarrier;
        public string flight = "AC102";
        [Min(1)] public int batchCount = 6, bagsPerBatch = 10;
        [Min(0.1f)] public float kilogramsPerBag = 15f, secondsPerBag = 0.8f;
        [Min(0f)] public float gameSpeed = 4f;
        public bool paused, accessBlocked;

        public BaggageLedger Ledger { get; private set; }
        public BaggageStore Dispatch { get; private set; }
        public BaggageStore Cart { get; private set; }
        public BaggageStore Hold { get; private set; }
        public BaggageDelivery Delivery { get; private set; }
        public BaggageEquipmentSet Equipment { get; private set; }
        public string Problem { get; private set; }
        Vector3 aircraftPosition;
        Quaternion aircraftRotation;
        Aircraft.AircraftType aircraftType;

        void Start() => Restart();

        public void Restart()
        {
            Problem = null;
            try
            {
                if (aircraft == null || tractor == null || cart == null || loader == null || roads == null)
                    throw new InvalidOperationException("Assign aircraft, equipment and service roads.");
                if (hold.aircraft != aircraft.type) throw new InvalidOperationException("Hold configuration belongs to another aircraft type.");
                if (!hold.Resolve(loader.type, out _, out var reason)) throw new InvalidOperationException(reason);
                float speed = Mathf.Min(tractor.type.transitSpeed, cart.type.transitSpeed);
                float radius = Mathf.Max(6f, tractor.type.turnRadius, cart.type.turnRadius);
                float width = Mathf.Max(tractor.type.Width, cart.type.Width) + 1f;
                Vector2 Facing(float bearing) => new Vector2(Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));
                var homeFacing = Facing(dispatchBearing); var standFacing = Facing(handoffBearing);
                if (!BaggageRoute.Plan(roads, dispatchNode, handoffNode, radius, speed, width, out var outbound, out reason, homeFacing, standFacing) ||
                    !BaggageRoute.Plan(roads, handoffNode, dispatchNode, radius, speed, width, out var inbound, out reason, standFacing, homeFacing))
                    throw new InvalidOperationException(reason);
                // The review graph has two corridors; choose a forward return rather than reversing the outbound route.
                inbound.Sample(0f, out var returning, out _, out _);
                if (Vector2.Dot(outbound.EndHeading, returning) < 0.99f)
                    throw new InvalidOperationException("The return route must continue forwards from the handoff.");
                float handoff = outbound.Length;
                outbound.Append(inbound);
                aircraftPosition = aircraft.transform.position;
                aircraftRotation = aircraft.transform.rotation;
                aircraftType = aircraft.type;
                var preview = new BaggageTrain(tractor.type, cart.type, outbound);
                if (!BaggageRoute.Validate(preview, StaticClear, out reason)) throw new InvalidOperationException(reason);
                Ledger = new BaggageLedger();
                Dispatch = Ledger.Store("Dispatch", 1000, 20000f);
                Cart = Ledger.Store("Cart", 100, cart.type.baggage.payload, flight, true);
                Hold = Ledger.Store("Bulk hold", hold.bagCapacity, hold.weightCapacity, flight, true);
                for (int i = 0; i < batchCount; i++)
                    Ledger.Add($"{flight}-{i + 1:00}", flight, bagsPerBatch, bagsPerBatch * kilogramsPerBag, true, Dispatch);
                Equipment = new BaggageEquipmentSet(tractor.type, cart.type, loader.type);
                aircraft.CargoDoors.Operate(hold.cargoPoint, false, 100f, out _);
                Delivery = new BaggageDelivery(flight, Ledger, Dispatch, Cart, Hold, Equipment,
                    new BaggageTrain(tractor.type, cart.type, outbound), handoff, secondsPerBag, LiveClear,
                    (open, dt) => aircraft.CargoDoors.Operate(hold.cargoPoint, open, dt, out _));
                Present(0f);
            }
            catch (Exception e) { Delivery = null; Problem = e.Message; Debug.LogError("Baggage delivery: " + Problem, this); }
        }

        void Update()
        {
            if (Delivery == null) return;
            if (aircraft.type != aircraftType || aircraft.transform.position != aircraftPosition || aircraft.transform.rotation != aircraftRotation)
            { paused = true; Problem = "Aircraft changed; restart to rebuild the handoff plan."; return; }
            if (paused) return;
            Advance(Time.deltaTime * gameSpeed);
        }

        /// <summary>Also used by batch captures to show an actual simulated delivery.</summary>
        public void Advance(float gameSeconds)
        {
            if (Delivery == null) return;
            Delivery.Step(gameSeconds);
            Present(gameSeconds);
        }

        bool StaticClear(BaggageVehiclePose pose, VehicleType type) =>
            BaggageTrain.FootprintClear(pose, type, p => OnRoad(p) && OutsideAircraft(p));

        bool LiveClear(BaggageVehiclePose pose, VehicleType type) => StaticClear(pose, type) &&
            (!accessBlocked || accessBarrier == null || BaggageTrain.FootprintClear(pose, type, p =>
                !new Bounds(accessBarrier.transform.position, accessBarrier.transform.localScale + Vector3.up * 5f)
                    .Contains(new Vector3(p.x, accessBarrier.transform.position.y, p.y))));

        bool OutsideAircraft(Vector2 p)
        {
            var local = aircraft.transform.InverseTransformPoint(new Vector3(p.x, 0f, p.y));
            var b = aircraft.type.bounds;
            return local.x < b.min.x - 0.75f || local.x > b.max.x + 0.75f ||
                local.z < b.min.z - 0.75f || local.z > b.max.z + 0.75f;
        }

        bool OnRoad(Vector2 p)
        {
            foreach (var edge in roads.edges)
            {
                if (edge.road != RoadClass.Service || TaxiwayNetwork.Closed(edge) || edge.wear >= RunwayRules.ClosedWear) continue;
                Vector2 a = roads.At(edge.a), b = roads.At(edge.b), d = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(d.sqrMagnitude, 0.001f));
                if (Vector2.Distance(p, a + d * t) <= edge.width * 0.5f - 0.2f) return true;
            }
            return false;
        }

        void Present(float dt)
        {
            if (accessBarrier != null) accessBarrier.SetActive(accessBlocked);
            var train = Delivery.Train;
            train.Path.Pose(train.Distance, out _, out float curvature);
            float steering = -Mathf.Atan(tractor.type.wheelbase * curvature) * Mathf.Rad2Deg;
            Place(tractor, train.Tractor, Delivery.Speed, dt, steering);
            Place(cart, train.Cart, Delivery.Speed, dt);
            cart.cartSteering = train.CartSteering;
            cart.drawbarLift = 0f;
            cart.displayLoad = Cart.Count > 0;
            cart.curtainOpen = Delivery.State == BaggageDeliveryState.Collecting || Delivery.State == BaggageDeliveryState.Delivering ? 1f : 0f;
            cart.Apply();
        }

        static void Place(BaggageEquipmentRig rig, BaggageVehiclePose pose, float speed, float dt, float steering = 0f)
        {
            var position = new Vector3(pose.position.x, 0f, pose.position.y);
            var facing = new Vector3(pose.facing.x, 0f, pose.facing.y);
            var vehicle = rig.GetComponent<Vehicle>();
            vehicle.Place(position, facing, speed, steering, dt);
            vehicle.SetState(speed > 0f, false, 0f);
        }

        void OnGUI()
        {
            DevGui.Begin();
            if (!DevGui.Visible) return;
            var rect = new Rect(18f, 18f, 350f, Problem != null ? 160f : 282f);
            DevGui.Panel(rect);
            GUILayout.BeginArea(new Rect(32f, 30f, 322f, rect.height - 20f));
            GUILayout.Label("BULK BAGGAGE DELIVERY", DevGui.Title);
            if (Problem != null) GUILayout.Label(Problem, DevGui.Small);
            else if (Delivery != null)
            {
                GUILayout.Label($"{flight}  ·  {Ledger.Count} bags / {Ledger.Weight:0} kg", DevGui.Bold);
                GUILayout.Label(Delivery.Waiting ?? Delivery.State.ToString(), DevGui.Label);
                GUILayout.Space(6f);
                GUILayout.Label($"Dispatch   {Dispatch.Count,3} bags   {Dispatch.Weight,4:0} kg", DevGui.Small);
                GUILayout.Label($"Cart          {Cart.Count,3} bags   {Cart.Weight,4:0} kg", DevGui.Small);
                GUILayout.Label($"Bulk hold  {Hold.Count,3} bags   {Hold.Weight,4:0} kg", DevGui.Small);
                GUILayout.Label(Delivery.DepartureReady ? "Delivery complete · equipment back in bay" : "Equipment reserved until return to bay", DevGui.Small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(paused ? "Resume" : "Pause")) paused = !paused;
                if (GUILayout.Button("Restart")) Restart();
                if (GUILayout.Button("Cancel")) Delivery.Cancel();
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                foreach (float value in new[] { 1f, 4f, 12f })
                    if (GUILayout.Button($"{value:0}×")) gameSpeed = value;
                if (GUILayout.Button(accessBlocked ? "Clear access" : "Block access"))
                { accessBlocked = !accessBlocked; Present(0f); }
                GUILayout.EndHorizontal();
                GUILayout.Label("Cargo hatch opens for loading and closes after return.", DevGui.Hint);
                GUILayout.Label("Right-drag orbit · scroll zoom · WASD pan", DevGui.Hint);
            }
            GUILayout.EndArea();
        }
    }
}
