using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using AirCity.Game;
using AirCity.Traffic;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>One leased bay. A multi-bay building saves its centre bay and sibling bays under it.
    /// Each door faces +Z and each resident's portable pose is owned by its lease.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SmallHangar : MonoBehaviour
    {
        public const float Width = 18f, Depth = 16f, EaveHeight = 5f, RidgeHeight = 6.5f;
        public const float FloorHeight = .075f;
        public const float DoorWidth = 14f, DoorHeight = 4.5f, RoofOverhang = .3f;
        public const int BuildPrice = 12000, MonthlyRent = 600;
        public const float RentPeriodSeconds = CareerClock.SimulationSecondsPerDay * 30f;

        [SerializeField] HangarSize size;
        // Keep the footprint and parking poses of executive hangars placed before the deeper shell.
        [SerializeField] bool extendedExecutive;
        [SerializeField] bool doorsOpen = true;
        [SerializeField] AircraftType residentType;
        [SerializeField] AircraftLiveryAsset residentLivery;
        [SerializeField] string tenantName = "", registration = "";
        [SerializeField] double rentRemainder;
        [SerializeField] long rentEarned;
        [SerializeField] bool residentAway, residentAbsent;
        [SerializeField] Vector3 residentPosition;
        [SerializeField] float residentHeading;
        // 1: return to this bay, 2: taxi to a stand, 3: depart. Never save a Flight or graph references.
        [SerializeField] int residentTrip;
        AirCity.Aircraft.Aircraft residentAircraft;
        int connectionVersion = -1;
        Light[] interiorLights = Array.Empty<Light>();

        public Airport Airport => GetComponentInParent<Airport>();
        public SmallHangar BuildingRoot => transform.parent != null && transform.parent.TryGetComponent<SmallHangar>(out var parent)
            ? parent : this;
        public IEnumerable<SmallHangar> Bays => BuildingRoot.GetComponentsInChildren<SmallHangar>(true)
            .Where(bay => bay.gameObject.activeSelf);
        public bool CanDemolish => !Bays.Any(bay => bay.Occupied);
        public HangarSize Size => BuildingRoot.size;
        public HangarSpec Spec => Size == HangarSize.Executive && !BuildingRoot.extendedExecutive
            ? HangarSpec.OriginalExecutive : HangarSpec.For(Size);
        public string BayName => Spec.Bays == 1 ? name : $"{BuildingRoot.name} · Spot {BayNumber}";
        public int BayNumber => this == BuildingRoot ? Spec.Bays / 2 + 1 :
            Mathf.RoundToInt(transform.localPosition.x / Spec.BayWidth) + Spec.Bays / 2 + 1;
        internal void SetSize(HangarSize value)
        {
            size = value;
            extendedExecutive = value == HangarSize.Executive;
        }

        public Vector2 Door => this == BuildingRoot ? Geo.XZ(transform.localPosition) :
            BuildingRoot.Door + Geo.Right(BuildingRoot.Axis) * transform.localPosition.x;
        public float Heading => BuildingRoot.transform.localEulerAngles.y;
        public Vector2 Axis => Facing(Heading);
        public Vector2[] Pad => Spec.Footprint(Door, Heading, false);
        public Vector2[] BuildingPad => Spec.Footprint(BuildingRoot.Door, Heading);
        public AirCity.Vehicles.AirportMaintenance.Box BuildingBox => AirCity.Vehicles.AirportMaintenance.Box.Of(
            Door-Axis*Spec.Depth/2f, Axis, new Bounds(Vector3.zero,new Vector3(Spec.BayWidth+RoofOverhang*2f,Spec.Ridge,Spec.Depth+RoofOverhang*2f)), 0f);
        public bool DoorsOpen => BuildingRoot.doorsOpen;
        public bool Occupied => residentType != null && !string.IsNullOrWhiteSpace(tenantName);
        public AircraftType ResidentType => residentType;
        public string TenantName => tenantName;
        public string Registration => registration;
        public long RentEarned => rentEarned;
        public double RentRemainder => rentRemainder;
        public string AccessProblem => AccessFor(residentType);
        public string AccessFor(AircraftType type) => HangarConnection.TryGet(this, type, out _, out var problem) ? null : problem;
        public AirCity.Aircraft.Aircraft ResidentAircraft => residentAircraft;
        public bool ResidentInBay => Occupied && !residentAbsent && !ResidentTravelling && (residentAircraft==null ? !residentAway :
            Vector2.Distance(Geo.XZ(Airport.transform.InverseTransformPoint(residentAircraft.transform.position)),ParkPosition(residentType))<.5f &&
            Vector2.Angle(Geo.XZ(Airport.transform.InverseTransformDirection(residentAircraft.transform.forward)),ParkFacing)<5f);
        public bool ResidentPresent => Occupied && !residentAbsent;
        public bool ResidentTravelling => residentAircraft != null &&
            (Airport.Traffic.FlightOf(residentAircraft) != null || Airport.Maintenance.TowOf(residentAircraft) != null);
        public Vector2 ParkFacing => -Axis;
        public Vector2 ParkPosition(AircraftType type) => Door-Axis*(Spec.Depth/2f-type.bounds.center.z)+Geo.Right(Axis)*type.bounds.center.x;
        public bool CanEndLease => !ResidentTravelling && (!ResidentPresent || ResidentInBay);
        public bool CanCloseDoors => Spec.Bays == 1 ? DoorClear : Bays.All(bay => bay.DoorClear);
        bool DoorClear
        {
            get
            {
                if (ResidentTravelling) return false;
                if (residentAircraft == null) return true;
                var p=Airport.transform.InverseTransformPoint(residentAircraft.transform.position);
                float along=Vector2.Dot(Geo.XZ(p)-Door,Axis);
                return Mathf.Abs(along)>residentType.Length+1f || ResidentInBay;
            }
        }

        public static SmallHangar OfResident(AirCity.Aircraft.Aircraft aircraft)
        {
            var airport=aircraft!=null ? aircraft.GetComponentInParent<Airport>() : null;
            return airport!=null ? airport.Hangars.FirstOrDefault(h=>h.residentAircraft==aircraft) : null;
        }

        public static Vector2 Facing(float heading) => new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));

        /// <summary>The complete projected footprint, including the roof overhang.</summary>
        public static Vector2[] Footprint(Vector2 door, float heading) => HangarSpec.For(HangarSize.Small).Footprint(door, heading);

        public bool Contains(Vector2 point, float margin = 0f)
        {
            var delta = point-Door; float across = Vector2.Dot(delta, Geo.Right(Axis)), along = Vector2.Dot(delta, Axis);
            float sideOverhang = Size == HangarSize.Executive ? 0f : RoofOverhang;
            return Mathf.Abs(across) <= Spec.BayWidth/2+sideOverhang+margin && along >= -Spec.Depth-RoofOverhang-margin && along <= RoofOverhang+margin;
        }

        public static string AircraftProblem(AircraftType type, HangarSize size = HangarSize.Small)
        {
            if (type == null) return "Choose a resident aircraft type.";
            if (!Finite(type.Wingspan) || !Finite(type.Length) || !Finite(type.Height) ||
                type.Wingspan <= 0f || type.Length <= 0f || type.Height <= 0f) return "This aircraft has invalid dimensions.";
            switch (size)
            {
                case HangarSize.Small when type.category != AircraftCategory.GeneralAviation || type.SizeCode != 'A':
                    return "This hangar takes light general aviation aircraft.";
                case HangarSize.Executive when type.category != AircraftCategory.GeneralAviation && type.category != AircraftCategory.BusinessJet:
                    return "The executive hangar takes business jets and general aviation aircraft.";
                case HangarSize.Airline when type.category != AircraftCategory.Airliner:
                    return "The airline hangar takes commercial aircraft.";
            }
            var spec = HangarSpec.For(size);
            if (type.Wingspan > spec.AircraftSpan || type.Length > spec.AircraftLength || type.Height > spec.DoorHeight-.3f)
                return "This aircraft does not fit through the door or inside the bay.";
            return null;
        }

        public static bool Fits(AircraftType type) => AircraftProblem(type) == null;
        public bool CanStore(AircraftType type) => AircraftProblem(type, Size) == null;

        public bool TryLease(AircraftType type, string tenant, string tail, out string problem)
        {
            problem = Occupied ? "End the current lease before choosing another resident." : AircraftProblem(type, Size);
            if (problem != null) return false;
            tenant = (tenant ?? "").Trim(); tail = (tail ?? "").Trim().ToUpperInvariant();
            if (tenant.Length == 0 || tenant.Length > 60) { problem = "Enter a tenant name (up to 60 characters)."; return false; }
            if (tail.Length < 3 || tail.Length > 10 || tail.Any(c => !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-'))
            { problem = "Enter a registration of 3–10 letters, numbers or hyphens."; return false; }
            var airport = Airport;
            if (airport == null) { problem = "Place this hangar at an airport first."; return false; }
            if (airport.Hangars.Any(h => h != this && h.Occupied && string.Equals(h.Registration, tail, StringComparison.OrdinalIgnoreCase)))
            { problem = "That aircraft already has a hangar lease."; return false; }
            problem = AccessFor(type);
            if (problem != null) return false;
            residentType = type; residentLivery=null; tenantName = tenant; registration = tail;
            residentAway=residentAbsent=false; residentTrip=0;
            RebuildResident(); return true;
        }

        public void EndLease()
        {
            if (!CanEndLease) return;
            ClearResident();
            residentType = null; residentLivery=null; tenantName = registration = "";
            residentAway=residentAbsent=false; residentTrip=0;
        }

        public void SetDoorsOpen(bool open)
        {
            if (!open && !CanCloseDoors) return;
            var building = BuildingRoot;
            if (building.doorsOpen == open) return;
            building.doorsOpen = open;
            HangarBuilder.Rebuild(building);
        }

        /// <summary>Accrue rent only for live, connected occupancy. Called by shared session accounting, not
        /// Airport.Simulate, which is also used to reconstruct historical weather and active flights on load.</summary>
        public int AccrueRent(float seconds)
        {
            if (!Occupied || !Finite(seconds) || seconds <= 0f || AccessProblem != null) return 0;
            double total = rentRemainder + seconds * (Spec.Rent / (double)RentPeriodSeconds);
            int whole = (int)Math.Min(int.MaxValue, Math.Floor(total + 1e-9d));
            rentRemainder = Math.Max(0d, total-whole); rentEarned += whole;
            return whole;
        }

        public static string AccessIssue(Airport airport, Vector2 door, float heading, AircraftType type = null)
        {
            return HangarConnection.TryGet(airport,door,heading,type,out _,out var problem) ? null : problem;
        }

        public void Rebuild()
        {
            if (!isActiveAndEnabled) return;
            HangarBuilder.Rebuild(BuildingRoot);
            HangarBuilder.RefreshConnection(BuildingRoot);
            RebuildResident();
        }

        void RebuildResident()
        {
            if (!Occupied || !CanStore(residentType) || residentAbsent) { ClearResident(); return; }
            if (residentAircraft==null)
                residentAircraft=Airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>(true)
                    .FirstOrDefault(a=>a.gameObject.activeSelf && a.name=="Resident "+registration && a.type==residentType);
            if (residentAircraft != null) return;
            var catalog = GameAssetCatalog.Load();
            var prefab = catalog?.entries.Select(e => e.asset).OfType<GameObject>()
                .FirstOrDefault(go => go.GetComponent<AirCity.Aircraft.Aircraft>()?.type == residentType);
            if (prefab == null) return;
            var staging = new GameObject("Resident visual staging"); staging.SetActive(false);
            staging.hideFlags = HideFlags.DontSave; staging.transform.SetParent(transform,false);
            var go=Instantiate(prefab,staging.transform,false); go.SetActive(false);
            foreach (var body in go.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic=true;
            foreach (var child in go.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags=HideFlags.DontSave;
            go.name = "Resident " + registration;
            go.transform.SetParent(Airport.transform,false);
            residentAircraft=go.GetComponent<AirCity.Aircraft.Aircraft>();
            go.transform.localPosition=residentAway ? residentPosition : Geo.X0Z(ParkPosition(residentType),FloorHeight+.015f-residentType.bounds.min.y);
            go.transform.localRotation=Quaternion.Euler(0,residentAway ? residentHeading : Heading+180f,0);
            var paint = go.GetComponent<AircraftLiveryApplier>();
            if (paint != null)
            {
                if (residentLivery!=null) paint.SetLivery(residentLivery,registration);
                else { paint.registration=registration; residentLivery=paint.liveryAsset; }
            }
            go.SetActive(true);
            StopResidentEngines();
            AirfieldPart.DestroyAny(staging);
        }

        void ClearResident()
        {
            if (residentAircraft==null) return;
            residentAircraft.gameObject.SetActive(false); AirfieldPart.DestroyAny(residentAircraft.gameObject);
            residentAircraft=null;
        }

        /// <summary>Attach the physical aircraft after a successful taxi request; never duplicate or teleport it.</summary>
        public bool AttachResident(AirCity.Aircraft.Aircraft aircraft,out string problem)
        {
            problem=AircraftProblem(aircraft!=null ? aircraft.type : null, Size);
            if (problem!=null) return false;
            if (Occupied && residentAircraft!=aircraft) { problem="This hangar is reserved for another aircraft."; return false; }
            var owner=OfResident(aircraft);
            if (owner!=null && owner!=this) { problem="That aircraft already has a hangar lease."; return false; }
            if (aircraft.GetComponentInParent<Airport>()!=Airport) { problem="Choose an aircraft at this airport."; return false; }
            if (!Occupied)
            {
                residentType=aircraft.type; tenantName=Size == HangarSize.Airline ? "Airline operator" : "Private aircraft owner";
                string tail=aircraft.GetComponent<AircraftLiveryApplier>()?.CurrentRegistration;
                tail=(tail??"").Trim().ToUpperInvariant();
                if (tail.Length<3 || tail.Length>10 || tail.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))
                {
                    int number=1;
                    while (Airport.Hangars.Any(h=>h!=this && h.Registration=="AC-"+number.ToString("000"))) number++;
                    tail="AC-"+number.ToString("000");
                }
                if (Airport.Hangars.Any(h=>h!=this && h.Occupied && string.Equals(h.Registration,tail,StringComparison.OrdinalIgnoreCase)))
                { residentType=null; tenantName=""; problem="That registration already has a hangar lease."; return false; }
                registration=tail;
                aircraft.GetComponent<AircraftLiveryApplier>()?.SetRegistration(tail);
            }
            residentAircraft=aircraft; residentLivery=aircraft.GetComponent<AircraftLiveryApplier>()?.liveryAsset;
            residentAbsent=false; residentAway=true;
            aircraft.name="Resident "+registration;
            foreach(var child in aircraft.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags|=HideFlags.DontSave;
            CaptureResidentState();
            return true;
        }

        public bool TaxiToStand(out string problem)
        {
            if (residentAircraft==null) { problem="The resident aircraft is off airport."; return false; }
            SetDoorsOpen(true);
            var flight=Airport.Traffic.TaxiToStand(residentAircraft,out problem);
            if (flight==null) return false;
            residentAway=true; residentTrip=2; CaptureResidentState(); return true;
        }

        public bool TaxiIntoHangar(out string problem)
        {
            if (residentAircraft==null) { problem="The resident aircraft is off airport."; return false; }
            SetDoorsOpen(true);
            var flight=Airport.Traffic.TaxiToHangar(residentAircraft,this,out problem);
            if (flight==null) return false;
            residentAway=true; residentTrip=1; CaptureResidentState(); return true;
        }

        public void StoreAircraft(AirCity.Aircraft.Aircraft aircraft)
        {
            if (!AttachResident(aircraft,out _)) return;
            residentAway=residentAbsent=false; residentTrip=0;
            // The traffic route already ends here. Retain its exact pose; do not snap a different aircraft in.
            CaptureResidentState(); StopResidentEngines();
        }

        public void OnResidentGone(AirCity.Aircraft.Aircraft aircraft)
        {
            if (residentAircraft!=aircraft) return;
            residentAbsent=residentAway=true; residentTrip=0; residentAircraft=null;
        }

        public void CaptureResidentState()
        {
            if (residentAircraft==null || Airport==null) return;
            residentPosition=Airport.transform.InverseTransformPoint(residentAircraft.transform.position);
            var facing=Airport.transform.InverseTransformDirection(residentAircraft.transform.forward);
            residentHeading=Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg;
            var flight=Airport.Traffic.FlightOf(residentAircraft);
            if (flight!=null)
            {
                residentAway=true;
                residentTrip=flight.kind==AirCity.Traffic.FlightKind.GroundTransfer ? (flight.Route.hangar==this ? 1 : 2) : 3;
                // Flights themselves are transient throughout AirCity. A resident that has flown away remains away.
                if (!flight.OnGround) { residentAbsent=true; residentTrip=0; }
            }
            else
            {
                residentTrip=0;
                residentAway=Vector2.Distance(Geo.XZ(residentPosition),ParkPosition(residentType))>.5f ||
                    Mathf.Abs(Mathf.DeltaAngle(residentHeading,Heading+180f))>5f;
            }
        }

        public void ResumeResidentTrip()
        {
            int trip=residentTrip;
            if (trip==0 || residentAircraft==null || ResidentTravelling) return;
            residentTrip=0;
            if (trip==1 && ResidentInBay && Vector2.Distance(Geo.XZ(residentPosition),ParkPosition(residentType))<.15f)
            { StoreAircraft(residentAircraft); return; }
            if (trip==1) TaxiIntoHangar(out _);
            else if (trip==2) TaxiToStand(out _);
            else Airport.Traffic.Depart(residentAircraft,out _);
        }

        void StopResidentEngines()
        {
            if (residentAircraft==null) return;
            residentAircraft.GetComponent<AircraftGroundMotion>()?.Drive(0,0);
            foreach(var prop in residentAircraft.GetComponentsInChildren<PropellerSpinner>(true)) prop.rpm=0;
            foreach(var light in residentAircraft.GetComponentsInChildren<AircraftLights>(true))
                light.beacon=light.strobes=light.landing=light.taxi=false;
        }

        void Update()
        {
            if (BuildingRoot != this) return;
            RefreshInteriorLights();
            var airport=Airport;
            if (airport!=null && connectionVersion!=airport.LayoutVersion)
            { connectionVersion=airport.LayoutVersion; HangarBuilder.RefreshConnection(this); }
        }

        internal void SetInteriorLights(Light[] lights)
        {
            interiorLights = lights;
            RefreshInteriorLights();
        }

        void RefreshInteriorLights()
        {
            bool night = AircraftLights.Glow > .5f;
            if (interiorLights.Length == 0 || interiorLights[0] == null || interiorLights[0].enabled == night) return;
            foreach (var light in interiorLights) light.enabled = night;
        }

        void OnEnable()
        {
            if (BuildingRoot == this)
            {
                HangarBuilder.Rebuild(this);
                HangarBuilder.RefreshConnection(this);
            }
            RebuildResident();
        }
        void OnDisable()
        {
            CaptureResidentState(); HangarBuilder.Clear(this);
            interiorLights = Array.Empty<Light>();
            // Residents are airport siblings. Unity owns their teardown while that parent is deactivating.
            if (Airport!=null && Airport.gameObject.activeInHierarchy) ClearResident();
        }
        void OnValidate() { if (isActiveAndEnabled) Rebuild(); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
