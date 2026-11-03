using System.Collections.Generic;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    [DisallowMultipleComponent]
    public sealed class ContainerStation : MonoBehaviour
    {
        public VehicleType tractorType, dollyType, loaderType, unitType;
        public GameObject tractorPrefab, dollyPrefab, loaderPrefab, unitPrefab;
        public int dispatchNode, arrivalsNode;
        public float dispatchBearing, arrivalsBearing, loaderParkingBearing;
        public Vector2 loaderParking;
        [Min(1)] public int hallSlots = 16, arrivalsBagCapacity = 2000;
        [Min(1f)] public float hallGrossCapacity = 25408f, arrivalsBagWeight = 30000f;
        [Min(0.01f)] public float unpackSecondsPerBag = 0.15f;
        public bool accessBlocked;
        [Header("Scheduled container handling")]
        public bool automaticHandling;
        public ContainerHoldConfiguration automaticHold = new ContainerHoldConfiguration();
        public List<int> standIds = new List<int>();
        [Min(0)] public int incomingBags = 120, outgoingBags = 210;
        [Min(1)] public int bagsPerContainer = 70;
        [Min(0.1f)] public float kilogramsPerBag = 15f;
        public bool outgoingScreened = true;
        [Tooltip("Optional loose-bag station. Its automatic job waits until container equipment returns.")]
        public BaggageStation bulkStation;
        public string Problem { get; internal set; }
        public Airport Airport => GetComponentInParent<Airport>();
        Transform visualRoot;
        BaggageEquipmentRig tractor, dolly;
        ContainerEquipmentRig dollyRig, loader;
        readonly Dictionary<ContainerUnit, ContainerEquipmentRig> unitVisuals = new Dictionary<ContainerUnit, ContainerEquipmentRig>();

        void LateUpdate() { if (Application.isPlaying && Airport != null) Present(Time.deltaTime * Airport.timeScale); }
        public void Present(float dt)
        {
            var airport = Airport;
            if (airport == null) return;
            var fleet = airport.Baggage.ContainerFleet(this);
            if (fleet?.Train == null || tractorPrefab == null || dollyPrefab == null || loaderPrefab == null || unitPrefab == null) return;
            if (visualRoot == null)
            {
                visualRoot = new GameObject("Container equipment (runtime)") { hideFlags = HideFlags.DontSave }.transform;
                visualRoot.SetParent(airport.transform, false);
                tractor = Instantiate(tractorPrefab, visualRoot).GetComponent<BaggageEquipmentRig>();
                dolly = Instantiate(dollyPrefab, visualRoot).GetComponent<BaggageEquipmentRig>();
                dollyRig = dolly.GetComponent<ContainerEquipmentRig>();
                loader = Instantiate(loaderPrefab, visualRoot).GetComponent<ContainerEquipmentRig>();
            }
            Place(tractor.gameObject, fleet.Train.Tractor, fleet.TrainSpeed, dt);
            Place(dolly.gameObject, fleet.Train.Cart, fleet.TrainSpeed, dt);
            dolly.cartSteering = fleet.Train.CartSteering; dolly.Apply();
            dollyRig.deckAngle = fleet.DollyAngle;
            dollyRig.restraintsReleased = fleet.Job != null && fleet.Job.DollyTransfer; dollyRig.Apply();
            Place(loader.gameObject, fleet.LoaderPose, fleet.LoaderSpeed, dt);
            loader.mainHeight = fleet.MainHeight; loader.frontHeight = fleet.FrontHeight; loader.stabilisers = fleet.Stabilisers;
            loader.sideGateOpen = loader.bridgeExtended = fleet.Job != null && fleet.Job.SideTransfer;
            loader.restraintsReleased = fleet.Job != null && fleet.Job.PlatformTransfer; loader.Apply();
            foreach (var visual in unitVisuals.Values) if (visual != null) visual.gameObject.SetActive(false);
            foreach (var unit in airport.Baggage.Containers.Units)
            {
                if (!fleet.Pose(unit, out var p, out var facing, out bool show)) continue;
                if (!unitVisuals.TryGetValue(unit, out var visual))
                {
                    visual = Instantiate(unitPrefab, visualRoot).GetComponent<ContainerEquipmentRig>();
                    visual.name = unit.Id; unitVisuals.Add(unit, visual);
                }
                visual.gameObject.SetActive(show);
                if (!show) continue;
                visual.transform.SetPositionAndRotation(airport.transform.TransformPoint(p),
                    Quaternion.LookRotation(airport.transform.TransformDirection(new Vector3(facing.x, 0f, facing.y)), Vector3.up));
                visual.curtainOpen = unit.Sealed ? 0f : 1f; visual.displayLoad = unit.Contents.Count > 0; visual.Apply();
            }
        }
        void Place(GameObject go, BaggageVehiclePose pose, float speed, float dt)
        {
            go.GetComponent<Vehicle>().Place(Airport.transform.TransformPoint(new Vector3(pose.position.x, 0f, pose.position.y)),
                Airport.transform.TransformDirection(new Vector3(pose.facing.x, 0f, pose.facing.y)), speed, 0f, dt);
        }
        void OnDisable()
        {
            if (visualRoot != null) { if (Application.isPlaying) Destroy(visualRoot.gameObject); else DestroyImmediate(visualRoot.gameObject); }
            visualRoot = null; unitVisuals.Clear();
        }
    }

    public sealed class ContainerFleet
    {
        public ContainerStation Station { get; }
        public ContainerBay Dispatch { get; }
        public ContainerBay Arrivals { get; }
        public ContainerBay Dolly { get; }
        public ContainerBay Main { get; }
        public ContainerBay Front { get; }
        public ContainerBay EmptyYard { get; }
        public BaggageStore ArrivalsBags { get; }
        public ContainerTurnaround Job { get; internal set; }
        public BaggageTrain Train { get; internal set; }
        public BaggageVehiclePose LoaderPose { get; internal set; }
        public float MainHeight { get; internal set; }
        public float FrontHeight { get; internal set; }
        public float Stabilisers { get; internal set; }
        public float DollyAngle { get; internal set; }
        public float TrainSpeed { get; internal set; }
        public float LoaderSpeed { get; internal set; }
        internal ContainerFleet(ContainerStation station, ContainerLedger ledger)
        {
            Station = station;
            Dispatch = ledger.Bay(station.name + " dispatch", ContainerBayKind.Dispatch, station.hallSlots, station.hallGrossCapacity);
            Arrivals = ledger.Bay(station.name + " arrivals", ContainerBayKind.Arrivals, station.hallSlots, station.hallGrossCapacity);
            Dolly = ledger.Bay("Dolly", ContainerBayKind.Dolly, 1, station.dollyType.container.maxGross);
            Main = ledger.Bay("Main platform", ContainerBayKind.MainPlatform, 1, station.loaderType.container.maxGross);
            Front = ledger.Bay("Front platform", ContainerBayKind.FrontPlatform, 1, station.loaderType.container.maxGross);
            EmptyYard = ledger.Bay(station.name + " empty ULD yard", ContainerBayKind.EmptyYard, int.MaxValue, float.MaxValue);
            ArrivalsBags = ledger.Baggage.Store(station.name + " arrivals bags", station.arrivalsBagCapacity, station.arrivalsBagWeight);
            ResetParking();
        }
        internal void ResetParking()
        {
            LoaderPose = new BaggageVehiclePose(Station.loaderParking, BaggageServicePlan.Facing(Station.loaderParkingBearing));
            MainHeight = Station.dollyType.container.transfer.y; FrontHeight = Station.loaderType.container.frontMinHeight;
            DollyAngle = 0f;
            var net = Station.Airport.Taxiways;
            if (net.Node(Station.dispatchNode) != null)
            {
                var path = new VehiclePath(); path.Start(net.At(Station.dispatchNode), 1f);
                path.LineTo(net.At(Station.dispatchNode) + BaggageServicePlan.Facing(Station.dispatchBearing), 1f);
                Train = new BaggageTrain(Station.tractorType, Station.dollyType, path);
            }
        }
        public bool Pose(ContainerUnit unit, out Vector3 position, out Vector2 facing, out bool show)
        {
            position = default; facing = Vector2.up; show = false;
            if (Job != null && Job.TransferUnit == unit && Job.TransferFrom != null)
            {
                var a = Mount(Job.TransferFrom, out var af); var b = Mount(Job.TransferTo, out var bf);
                position = Vector3.Lerp(a, b, Job.TransferProgress);
                facing = Vector2.Lerp(af, bf, Job.TransferProgress).normalized; show = true; return true;
            }
            var bay = unit.Location;
            if (bay != Dispatch && bay != Arrivals && bay != Dolly && bay != Main && bay != Front && bay != EmptyYard && bay != Job?.Hold) return false;
            position = Mount(bay, out facing);
            show = bay == Dolly || bay == Main || bay == Front;
            return true;
        }
        public Vector3 Mount(ContainerBay bay, out Vector2 facing)
        {
            if (bay == Job?.Hold && Job?.Plan != null)
            {
                facing = LoaderPose.facing;
                float depth = Station.unitType.bounds.extents.z + 0.08f;
                return Job.Plan.door + Job.Plan.inward * depth - new Vector3(LoaderPose.Right.x, 0f, LoaderPose.Right.y) * Station.unitType.bounds.center.x;
            }
            Vector3 local; BaggageVehiclePose pose;
            var station = Station;
            if (bay == Dolly || bay == Dispatch || bay == Arrivals)
            {
                pose = Train.Cart; local = station.dollyType.container.transfer;
                var direction = Quaternion.AngleAxis(DollyAngle, Vector3.up) * new Vector3(pose.Right.x, 0f, pose.Right.y);
                facing = new Vector2(direction.x, direction.z);
            }
            else
            {
                pose = LoaderPose; facing = pose.facing;
                local = bay == Main ? station.loaderType.container.mainTransfer : station.loaderType.container.frontTransfer;
                local.y = bay == Main ? MainHeight : FrontHeight;
            }
            var p = pose.Point(local); return new Vector3(p.x, local.y, p.y);
        }
    }
}
