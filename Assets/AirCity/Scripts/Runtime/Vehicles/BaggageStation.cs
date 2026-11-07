using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Saved baggage hall endpoints and one equipment set. The coordinator owns all operational state.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BaggageStation : MonoBehaviour
    {
        public int dispatchNode, arrivalsNode;
        public float dispatchBearing, arrivalsBearing;
        public VehicleType tractorType, cartType, loaderType;
        public GameObject tractorPrefab, cartPrefab, loaderPrefab;
        public GameObject[] bagPrefabs;
        public VehicleType bagPropType;
        public GameObject crewPrefab;
        public RampCrewType crewType;
        [Min(1)] public int cartCount = 1;
        public BaggageDepot depot;
        [Min(0)] public int depotBay;
        [Range(2,4)] public int workersPerTeam=4;
        [Tooltip("Loader parking point, in airport coordinates (rear axle).")]
        public Vector2 loaderParking;
        public float loaderParkingBearing;
        [Tooltip("Park the equipment in clear staging bays relative to its assigned stand.")]
        public bool parkAtStand = true;
        [Tooltip("Preferred assigned stand for idle equipment; 0 uses the first assigned stand.")]
        public int parkingStandId;
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

        BaggageEquipmentRig tractor, loader;
        readonly List<BaggageEquipmentRig> carts = new List<BaggageEquipmentRig>();
        Transform visualRoot;
        BaggageLoadVisuals loadVisuals;
        RampCrewRig groundCrew, holdCrew;
        readonly List<RampCrewRig> groundWorkers=new List<RampCrewRig>(),holdWorkers=new List<RampCrewRig>();
        public Airport Airport => GetComponentInParent<Airport>();

        void OnEnable() => PaintChanged();
        void OnTransformParentChanged() => PaintChanged();
        bool paintDirty;
        void OnValidate() => paintDirty = true;
        void Update() { if (paintDirty) { paintDirty = false; if (Airport != null) Airport.Baggage.RefreshParking(this); } }
        void PaintChanged()
        {
            var airport = Airport;
            if (airport != null) { airport.GetComponentInChildren<TaxiwayNetwork>()?.EquipmentPaintChanged(); airport.Baggage.ParkingChanged(); }
        }

        void LateUpdate() { if (Application.isPlaying && Airport != null) Present(Time.deltaTime * Airport.timeScale); }

        public void Present(float dt)
        {
            var airport = Airport;
            if (airport == null) return;
            var fleet = airport.Baggage.Fleet(this);
            if (fleet == null || fleet.Train == null) { if (visualRoot != null) visualRoot.gameObject.SetActive(false); return; }
            if (visualRoot != null) visualRoot.gameObject.SetActive(true);
            if (visualRoot == null)
            {
                if (tractorPrefab == null || cartPrefab == null || loaderPrefab == null) return;
                visualRoot = new GameObject("Baggage equipment (runtime)") { hideFlags = HideFlags.DontSave }.transform;
                visualRoot.SetParent(airport.transform, false);
                tractor = Spawn(tractorPrefab); loader = Spawn(loaderPrefab);
                carts.Clear();
                loadVisuals = new BaggageLoadVisuals(visualRoot, bagPrefabs);
                if (crewPrefab != null)
                {
                    groundCrew = Instantiate(crewPrefab, visualRoot).GetComponent<RampCrewRig>(); groundCrew.Initialize(0);
                    holdCrew = Instantiate(crewPrefab, visualRoot).GetComponent<RampCrewRig>(); holdCrew.Initialize(1);
                    groundWorkers.Clear(); holdWorkers.Clear(); groundWorkers.Add(groundCrew); holdWorkers.Add(holdCrew);
                    groundCrew.name = "Tractor driver / cart handler"; holdCrew.name = "Loader driver / hold handler";
                }
            }
            var train = fleet.Train;
            train.Path.Pose(train.Distance, out _, out float curvature);
            Place(tractor, train.Tractor, fleet.TrainSpeed, -Mathf.Atan(tractorType.wheelbase * curvature) * Mathf.Rad2Deg, dt);
            while (carts.Count < train.CartCount) carts.Add(Spawn(cartPrefab));
            for (int i = 0; i < carts.Count; i++)
            {
                var cart = carts[i]; cart.gameObject.SetActive(i < train.CartCount); if (i >= train.CartCount) continue;
                Place(cart,train.CartAt(i),fleet.TrainSpeed,0,dt); cart.cartSteering = train.SteeringAt(i);
                cart.curtainOpen = fleet.Job != null && fleet.Job.HandlingCart ? 1 : 0;
                cart.displayLoad = (bagPrefabs == null || bagPrefabs.Length == 0) && fleet.Carts[i].Count > 0; cart.Apply();
            }
            Place(loader, fleet.LoaderPose, fleet.LoaderSpeed, 0f, dt);
            loader.beltFrontHeight = fleet.LoaderHeight; loader.Apply();
            bool staffed=depot==null || fleet.Workers>=2;
            if (groundCrew != null) { groundCrew.gameObject.SetActive(staffed); if (staffed) groundCrew.Apply(fleet.Crew.GroundPose); }
            if (holdCrew != null) { holdCrew.gameObject.SetActive(staffed); if (staffed) holdCrew.Apply(fleet.Crew.HoldPose); }
            void Helpers(List<RampCrewRig> workers,int count,bool ground)
            {
                bool working=staffed && fleet.Job!=null && (ground ? fleet.Crew.GroundReady || fleet.Job.DepotTransfer : fleet.Crew.HoldReady);
                while (working && workers.Count<count)
                {
                    var worker=Instantiate(crewPrefab,visualRoot).GetComponent<RampCrewRig>();
                    worker.Initialize(workers.Count+2); worker.name=ground ? "Additional cart handler" : "Additional hold handler"; workers.Add(worker);
                }
                for (int i=1;i<workers.Count;i++)
                {
                    workers[i].gameObject.SetActive(working && i<count);
                    if (working && i<count) workers[i].Apply(ground ? fleet.Crew.GroundPoseFor(i) : fleet.Crew.HoldPoseFor(i));
                }
            }
            if (groundCrew!=null) { Helpers(groundWorkers,fleet.Crew.GroundWorkers,true); Helpers(holdWorkers,fleet.Crew.HoldWorkers,false); }
            loadVisuals.Present(fleet,groundCrew,holdCrew,groundWorkers,holdWorkers);
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
            PaintChanged();
            if (visualRoot != null)
            {
                if (Application.isPlaying) Destroy(visualRoot.gameObject); else DestroyImmediate(visualRoot.gameObject);
                visualRoot = null;
            }
        }
    }
}
