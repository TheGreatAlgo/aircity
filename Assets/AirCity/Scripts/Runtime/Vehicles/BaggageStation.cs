using System.Collections.Generic;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Saved baggage hall endpoints and one equipment set. The coordinator owns all operational state.</summary>
    [DisallowMultipleComponent]
    public sealed class BaggageStation : MonoBehaviour
    {
        public int dispatchNode, arrivalsNode;
        public float dispatchBearing, arrivalsBearing;
        public VehicleType tractorType, cartType, loaderType;
        public GameObject tractorPrefab, cartPrefab, loaderPrefab;
        public GameObject[] bagPrefabs;
        [Tooltip("Loader parking point, in airport coordinates (rear axle).")]
        public Vector2 loaderParking;
        public float loaderParkingBearing;
        [Min(1)] public int dispatchCapacity = 1000, arrivalsCapacity = 1000;
        [Min(1f)] public float dispatchWeight = 20000f, arrivalsWeight = 20000f;
        [Min(0.01f)] public float secondsPerBag = 0.8f;
        [Tooltip("Review control: blocks the service area while preserving baggage and leases.")]
        public bool accessBlocked;
        [Header("Scheduled bulk handling")]
        public bool automaticHandling;
        public BulkHoldConfiguration automaticHold = new BulkHoldConfiguration();
        [Tooltip("Stands served by this station. Only the explicitly configured aircraft type is handled.")]
        public List<int> standIds = new List<int>();
        [Min(0)] public int incomingBags = 120, outgoingBags = 180;
        [Min(0.1f)] public float kilogramsPerBag = 15f;
        public string Problem { get; internal set; }

        BaggageEquipmentRig tractor, cart, loader;
        Transform visualRoot;
        BaggageLoadVisuals loadVisuals;
        public Airport Airport => GetComponentInParent<Airport>();

        void LateUpdate() { if (Application.isPlaying && Airport != null) Present(Time.deltaTime * Airport.timeScale); }

        public void Present(float dt)
        {
            var airport = Airport;
            if (airport == null) return;
            var fleet = airport.Baggage.Fleet(this);
            if (fleet == null || fleet.Train == null) return;
            if (visualRoot == null)
            {
                if (tractorPrefab == null || cartPrefab == null || loaderPrefab == null) return;
                visualRoot = new GameObject("Baggage equipment (runtime)") { hideFlags = HideFlags.DontSave }.transform;
                visualRoot.SetParent(airport.transform, false);
                tractor = Spawn(tractorPrefab); cart = Spawn(cartPrefab); loader = Spawn(loaderPrefab);
                loadVisuals = new BaggageLoadVisuals(visualRoot, bagPrefabs);
            }
            var train = fleet.Train;
            train.Path.Pose(train.Distance, out _, out float curvature);
            Place(tractor, train.Tractor, fleet.TrainSpeed, -Mathf.Atan(tractorType.wheelbase * curvature) * Mathf.Rad2Deg, dt);
            Place(cart, train.Cart, fleet.TrainSpeed, 0f, dt);
            cart.cartSteering = train.CartSteering;
            cart.curtainOpen = fleet.Job != null && fleet.Job.HandlingCart ? 1f : 0f;
            cart.displayLoad = (bagPrefabs == null || bagPrefabs.Length == 0) && fleet.Cart.Count > 0; cart.Apply();
            Place(loader, fleet.LoaderPose, fleet.LoaderSpeed, 0f, dt);
            loader.beltFrontHeight = fleet.LoaderHeight; loader.Apply();
            loadVisuals.Present(fleet);
        }

        BaggageEquipmentRig Spawn(GameObject prefab) => Instantiate(prefab, visualRoot).GetComponent<BaggageEquipmentRig>();

        void Place(BaggageEquipmentRig rig, BaggageVehiclePose pose, float speed, float steer, float dt)
        {
            var a = Airport.transform;
            rig.GetComponent<Vehicle>().Place(a.TransformPoint(new Vector3(pose.position.x, 0f, pose.position.y)),
                a.TransformDirection(new Vector3(pose.facing.x, 0f, pose.facing.y)), speed, steer, dt);
            var lights = rig.GetComponent<VehicleLights>();
            if (lights != null) { lights.beacon = Mathf.Abs(speed) > 0f; lights.headlights = Mathf.Abs(speed) > 0f; }
        }

        void OnDisable()
        {
            if (visualRoot != null)
            {
                if (Application.isPlaying) Destroy(visualRoot.gameObject); else DestroyImmediate(visualRoot.gameObject);
                visualRoot = null;
            }
        }
    }
}
