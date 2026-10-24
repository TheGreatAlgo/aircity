using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// A maintenance depot: a garage whose snow ploughs and mowers live inside, one per roller door (more park in a
    /// row outside along the front). Vehicles of a kind work together in teams of up to maxTeam (an echelon of
    /// ploughs clears a runway in a few passes; a single plough needs many), so buying vehicles makes ploughing and
    /// mowing faster, and more than a team's worth lets two jobs be done at once. An airport can have several
    /// depots; the jobs (AirportMaintenance) go to the nearest free team.
    ///
    ///   depot.SetFleet(ploughs: 3, mowers: 1);
    ///   airport.Maintenance.Plough(runway);   // or depot.Plough(runway), the same
    ///
    /// Vehicles leave by driving straight out of their door onto whatever is in front of it (a service road, a
    /// taxiway or an apron: the nearest edge of the taxiway network, see TryAccess) and come home by backing in. No
    /// forecourt is needed, though the turns are tidier with some pavement in front.
    ///
    /// A child of the Airport; its transform places the door front (+Z = out of the doors) in Airport space. Teams
    /// are simulated in game seconds with the Airport; vehicles are spawned from the prefabs when set.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class MaintenanceDepot : MonoBehaviour
    {
        [Header("Fleet")]
        [Min(0)] public int snowPloughs = 2;
        [Min(0)] public int mowers = 1;
        [Tooltip("Ploughs work in echelon teams of up to this many.")]
        [Range(1, 6)] public int maxPloughTeam = 4;
        [Range(1, 4)] public int maxMowerTeam = 3;
        [Tooltip("Send ploughs and mowers by themselves when snow builds up or grass gets long (any depot with this on " +
                 "turns it on for the airport).")]
        public bool autoDispatch = true;

        [Header("Assets (VehicleSetup assigns them)")]
        public VehicleType ploughType;
        public VehicleType mowerType;
        [Tooltip("The building's type: its doors and the hall the vehicles park in.")]
        public VehicleType buildingType;
        public GameObject ploughPrefab, mowerPrefab, buildingPrefab;

        /// <summary>Bays outside (beyond the doors) are this far apart; vehicles park this far from the back wall.</summary>
        const float BaySpacing = 8.5f, BackClearance = 0.8f, DoorTime = 5f;
        /// <summary>For a building type without door data: the depot model's doors and hall.</summary>
        static readonly float[] DefaultDoors = { -18f, -9.5f, -1f, 7.5f };
        const float DefaultBack = -23.5f;

        readonly List<VehicleTeam> teams = new List<VehicleTeam>();
        int builtPloughs = -1, builtMowers = -1;
        GameObject building;
        Transform[] doorParts = new Transform[0];
        float[] doorOpen = new float[0];
        bool[] doorWanted = new bool[0];

        public Airport Airport => GetComponentInParent<Airport>();
        public IReadOnlyList<VehicleTeam> Teams
        {
            get
            {
                EnsureTeams();
                return teams;
            }
        }
        /// <summary>The airport's job queue (shared by its depots).</summary>
        public IReadOnlyList<MaintenanceJob> Queue => Airport != null ? Airport.Maintenance.Queue : new List<MaintenanceJob>();

        public void SetAssets(VehicleType plough, VehicleType mower, VehicleType buildingType, GameObject ploughPrefab,
            GameObject mowerPrefab, GameObject buildingPrefab)
        {
            ploughType = plough;
            mowerType = mower;
            this.buildingType = buildingType;
            this.ploughPrefab = ploughPrefab;
            this.mowerPrefab = mowerPrefab;
            this.buildingPrefab = buildingPrefab;
        }

        // ------------------------------------------------------------------ layout (Airport space)

        /// <summary>The middle of the door front.</summary>
        public Vector2 Front => Geo.XZ(transform.localPosition);
        /// <summary>Out of the doors.</summary>
        public Vector2 Outward
        {
            get
            {
                var f = transform.localRotation * Vector3.forward;
                var d = new Vector2(f.x, f.z);
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
            }
        }
        Vector2 Across => Geo.Right(Outward);
        Vector2 Local(float x, float z) => Front + Across * x + Outward * z;

        public int DoorCount => buildingType != null && buildingType.doors.Length > 0 ? buildingType.doors.Length : DefaultDoors.Length;
        float DoorX(int k) => buildingType != null && buildingType.doors.Length > 0 ? buildingType.doors[k].position.x : DefaultDoors[k];
        float Back => buildingType != null && buildingType.interior.size.z > 1f ? buildingType.interior.min.z : DefaultBack;

        /// <summary>The building on the ground, in depot space (x across, y out of the doors; the door front at 0).</summary>
        Rect Footprint => buildingType != null && buildingType.bounds.size.x > 1f
            ? new Rect(buildingType.bounds.min.x, buildingType.bounds.min.z, buildingType.bounds.size.x, buildingType.bounds.size.z)
            : new Rect(-23f, -24f, 46f, 24f);

        /// <summary>Is a ground point on the building (or within margin of it)?</summary>
        public bool Contains(Vector2 p, float margin = 0f)
        {
            var rel = p - Front;
            float x = Vector2.Dot(rel, Across), z = Vector2.Dot(rel, Outward);
            var f = Footprint;
            return x >= f.xMin - margin && x <= f.xMax + margin && z >= f.yMin - margin && z <= f.yMax + margin;
        }

        /// <summary>The building's corners on the ground, going round.</summary>
        public Vector2[] Corners
        {
            get
            {
                var f = Footprint;
                return new[] { Local(f.xMin, f.yMin), Local(f.xMax, f.yMin), Local(f.xMax, f.yMax), Local(f.xMin, f.yMax) };
            }
        }

        /// <summary>Bay k is in the garage, behind door k (else outside along the front).</summary>
        public bool Inside(int bay) => bay < DoorCount;

        float BayX(int k) => k < DoorCount ? DoorX(k) : DoorX(DoorCount - 1) + BaySpacing * (k - DoorCount + 1);

        /// <summary>Parking bay k for a vehicle of type t (rear axle; vehicles park facing out): at the back of the
        /// hall behind its door, or in the row outside.</summary>
        public Vector2 Bay(int k, VehicleType t) =>
            k < DoorCount ? Local(BayX(k), Back + BackClearance - t.bounds.min.z) : Local(BayX(k), 1.5f - t.bounds.min.z);

        /// <summary>Where a vehicle's rear axle is when it has just cleared its door (outside bays: the bay).</summary>
        public Vector2 Threshold(int k, VehicleType t) => k < DoorCount ? Local(BayX(k), 1f - t.bounds.min.z) : Bay(k, t);

        /// <summary>
        /// Where the depot's vehicles join the airfield: the nearest point of a road, taxiway or runway node in front
        /// of the doors (roads count as nearer), with the edge's two nodes (or one node, b = -1). False if there's
        /// no network within reach: the vehicles then drive across the grass.
        /// </summary>
        public bool TryAccess(TaxiwayNetwork net, out Vector2 q, out int a, out int b)
        {
            var from = Front + Outward * 8f;
            q = default;
            a = b = -1;
            float best = 150f;
            foreach (var e in net.edges)
            {
                var c = Geo.ClosestOnSegment(from, net.At(e.a), net.At(e.b), out _);
                float d = Vector2.Distance(c, from) * (e.road ? 0.8f : 1f);
                // only what's in front of the building
                if (Vector2.Dot(c - Front, Outward) < -2f) continue;
                if (d < best)
                {
                    best = d;
                    q = c;
                    a = e.a;
                    b = e.b;
                }
            }
            if (a >= 0) return true;
            best = 300f;
            foreach (var n in net.nodes)
            {
                float d = Vector2.Distance(Geo.XZ(n.position), from);
                if (d < best)
                {
                    best = d;
                    q = Geo.XZ(n.position);
                    a = n.id;
                }
            }
            return a >= 0;
        }

        // ------------------------------------------------------------------ doors

        void EnsureDoors()
        {
            int n = DoorCount;
            if (doorOpen.Length == n) return;
            doorOpen = new float[n];
            doorWanted = new bool[n];
        }

        /// <summary>A vehicle wants door k up now (asked every step while it does).</summary>
        public void WantDoor(int bay)
        {
            EnsureDoors();
            if (bay < doorWanted.Length) doorWanted[bay] = true;
        }

        /// <summary>Is door k (fully) up? Outside bays have no door.</summary>
        public bool DoorOpen(int bay)
        {
            EnsureDoors();
            return bay >= doorOpen.Length || doorOpen[bay] >= 0.98f;
        }

        /// <summary>0 shut .. 1 up.</summary>
        public float DoorOpenAmount(int bay)
        {
            EnsureDoors();
            return bay < doorOpen.Length ? doorOpen[bay] : 1f;
        }

        /// <summary>Doors roll up while wanted (DoorTime seconds), down when not.</summary>
        internal void StepDoors(float dt)
        {
            EnsureDoors();
            for (int k = 0; k < doorOpen.Length; k++)
            {
                doorOpen[k] = Mathf.MoveTowards(doorOpen[k], doorWanted[k] ? 1f : 0f, dt / DoorTime);
                doorWanted[k] = false;
            }
        }

        /// <summary>A team is out, or a door is moving: the airport keeps stepping it.</summary>
        internal bool Busy
        {
            get
            {
                foreach (var t in teams)
                    if (t.Active) return true;
                foreach (float o in doorOpen)
                    if (o > 0f) return true;
                return false;
            }
        }

        void ShowDoors()
        {
            for (int k = 0; k < doorParts.Length && k < doorOpen.Length; k++)
                if (doorParts[k] != null)
                    doorParts[k].localScale = new Vector3(1f, Mathf.Lerp(1f, 0.05f, Mathf.SmoothStep(0f, 1f, doorOpen[k])), 1f);
        }

        // ------------------------------------------------------------------ fleet

        public void SetFleet(int ploughs, int mowerCount)
        {
            snowPloughs = Mathf.Max(0, ploughs);
            mowers = Mathf.Max(0, mowerCount);
            EnsureTeams();
        }

        /// <summary>(Re)form teams when the fleet changed: full teams first, the rest in a smaller one. Teams out on
        /// a job give it back (it's queued again).</summary>
        internal void EnsureTeams()
        {
            if (builtPloughs == snowPloughs && builtMowers == mowers && teams.Count > 0 == (snowPloughs + mowers > 0)) return;
            var airport = Airport;
            foreach (var t in teams)
            {
                if (t.Job != null && airport != null) airport.Maintenance.Requeue(t.Job);
                t.Abandon();
                foreach (var v in t.vehicles)
                    if (v != null) AirfieldPart.DestroyAny(v.gameObject);
            }
            teams.Clear();
            int bay = 0;
            void Form(VehicleType type, int count, int max, string label)
            {
                if (type == null) return;
                int n = 0;
                while (count > 0)
                {
                    int size = Mathf.Min(count, max);
                    count -= size;
                    teams.Add(new VehicleTeam(this, type, size, bay, $"{name}: {label} {(char)('A' + n++)}"));
                    bay += size;
                }
            }
            Form(ploughType, snowPloughs, maxPloughTeam, "snow team");
            Form(mowerType, mowers, maxMowerTeam, "mowing team");
            builtPloughs = snowPloughs;
            builtMowers = mowers;
        }

        // ------------------------------------------------------------------ jobs (the airport's queue)

        AirportMaintenance Work => Airport != null ? Airport.Maintenance : null;

        public bool Plough(Runway r) => Work != null && Work.Plough(r);
        public bool Mow(Runway r) => Work != null && Work.Mow(r);
        public bool MowVerges(Runway r) => Work != null && Work.MowVerges(r);
        public bool Plough(TaxiwayEdge e) => Work != null && Work.Plough(e);
        public bool Mow(TaxiwayEdge e) => Work != null && Work.Mow(e);
        public bool MowVerges(TaxiwayEdge e) => Work != null && Work.MowVerges(e);
        public void PloughTaxiways() => Work?.PloughTaxiways();
        public void PloughAll() => Work?.PloughAll();
        public void MowAll() => Work?.MowAll();

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            builtPloughs = builtMowers = -1;
            if (buildingPrefab != null && building == null)
            {
                building = Instantiate(buildingPrefab, transform);
                building.name = "Depot building";
                building.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                foreach (var c in building.GetComponentsInChildren<Transform>(true)) c.gameObject.hideFlags = building.hideFlags;
            }
            EnsureDoors();
            doorParts = new Transform[DoorCount];
            if (building != null)
            {
                var byName = new Dictionary<string, Transform>();
                foreach (var t in building.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
                for (int k = 0; k < doorParts.Length; k++)
                {
                    string door = buildingType != null && k < buildingType.doors.Length ? buildingType.doors[k].name : $"Door_{k}";
                    doorParts[k] = byName.TryGetValue(door, out var t) ? t : null;
                }
            }
        }

        void OnDisable()
        {
            foreach (var t in teams)
            {
                t.Abandon();
                foreach (var v in t.vehicles)
                    if (v != null) AirfieldPart.DestroyAny(v.gameObject);
            }
            teams.Clear();
            if (building != null) AirfieldPart.DestroyAny(building);
            building = null;
        }

        void OnValidate()
        {
            builtPloughs = -1;
        }

        void LateUpdate()
        {
            var a = Airport;
            if (a != null) UpdateVehicles(Application.isPlaying ? Time.deltaTime * a.timeScale : 0f);
        }

        /// <summary>Spawn missing vehicles and put every team's vehicles where the simulation has them, and the
        /// doors where they are (LateUpdate does this every frame; dt animates wheels and brooms).</summary>
        public void UpdateVehicles(float dt)
        {
            EnsureTeams();
            var a = Airport;
            if (a == null) return;
            foreach (var t in teams)
            {
                SpawnVehicles(t);
                t.Place(a.transform, dt);
                foreach (var v in t.vehicles)
                    if (v != null && v.Lights != null) v.Lights.UpdateState();
            }
            ShowDoors();
        }

        void SpawnVehicles(VehicleTeam t)
        {
            var prefab = t.type == ploughType ? ploughPrefab : mowerPrefab;
            if (prefab == null || t.vehicles.Count == t.size) return;
            for (int i = t.vehicles.Count; i < t.size; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = $"{t.name} {i + 1}";
                go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                var v = go.GetComponent<Vehicle>();
                if (v == null)
                {
                    AirfieldPart.DestroyAny(go);
                    return;
                }
                t.vehicles.Add(v);
            }
        }
    }
}
