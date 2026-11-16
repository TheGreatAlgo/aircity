using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Vehicles
{
    /// <summary>
    /// Displays player-owned deicing equipment. Truck movement, boom deployment and spray read game-time service
    /// stages; this component never starts treatment, consumes stock or advances the aircraft.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Airport))]
    public sealed class DeicingRig : MonoBehaviour
    {
        public DeicingAssets assets;
        sealed class Truck
        {
            public GameObject root;
            public Transform yaw, lift, elbow, extension, cabin, nozzle;
            public Vector3 extensionRest;
            public Quaternion yawRest, liftRest, elbowRest, nozzleRest;
            public float deployment;
            public ParticleSystem spray, mist;
            public LineRenderer hose;
            public readonly Vector3[] hosePoints = new Vector3[6];
            public VehicleLights lights;
            public Vehicle vehicle;
            public float emitted, mistEmitted;
        }
        sealed class Equipment
        {
            public readonly List<Truck> trucks = new List<Truck>();
        }
        sealed class Tanker
        {
            public GameObject root;
            public Vehicle vehicle;
            public VehicleLights lights;
            public Transform reel;
            public Quaternion reelRest;
            public LineRenderer hose;
            public readonly Vector3[] hosePoints = new Vector3[7];
        }
        readonly Dictionary<DeicingFacility, Equipment> equipment = new Dictionary<DeicingFacility, Equipment>();
        readonly List<DeicingFacility> removed = new List<DeicingFacility>();
        readonly Dictionary<DeicingWorkArea, GameObject> tanks = new Dictionary<DeicingWorkArea, GameObject>();
        readonly List<DeicingWorkArea> removedAreas = new List<DeicingWorkArea>();
        readonly Dictionary<DeicingWorkArea, GameObject> recyclers = new Dictionary<DeicingWorkArea, GameObject>();
        readonly Dictionary<DeicingDelivery, Tanker> tankers = new Dictionary<DeicingDelivery, Tanker>();
        readonly HashSet<DeicingDelivery> seenDeliveries = new HashSet<DeicingDelivery>();
        readonly List<DeicingDelivery> removedDeliveries = new List<DeicingDelivery>();
        Material hoseMaterial;
        AirfieldPart workAreaPaint;
        int paintStamp;
        Airport airport;
        Transform container;
        DeicingAssets builtAssets;
        float lastClock;

        public int TruckCount
        {
            get { int count = 0; foreach (var station in equipment.Values) count += station.trucks.Count; return count; }
        }
        public int TankCount
        {
            get => tanks.Count;
        }
        public int RecyclerCount => recyclers.Count;
        public int TankerCount => tankers.Count;
        public int DeliveryHoseCount
        {
            get { int count=0; foreach(var tanker in tankers.Values) if(tanker.hose!=null && tanker.hose.enabled) count++; return count; }
        }
        public int SprayParticleCount
        {
            get { int count = 0; foreach (var station in equipment.Values) foreach (var truck in station.trucks)
                { if (truck.spray != null) count += truck.spray.particleCount;
                    if (truck.mist != null) count += truck.mist.particleCount; } return count; }
        }

        void OnEnable()
        {
            airport = GetComponent<Airport>();
            lastClock = airport.Deicing.Clock;
            RenderPipelineManager.beginContextRendering += BeforeRender;
            Refresh();
        }
        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= BeforeRender;
            Clear();
        }
        void LateUpdate() => Refresh();
        void BeforeRender(ScriptableRenderContext context, List<Camera> cameras) => Refresh();

        /// <summary>Synchronize equipment and manually advance spray by elapsed simulation time, including captures.</summary>
        public void Refresh()
        {
            if (airport == null) airport = GetComponent<Airport>();
            if (airport == null || !airport.isActiveAndEnabled) { Clear(); return; }
            if (assets == null) assets = Resources.Load<DeicingAssets>("DeicingAssets");
            if (builtAssets != assets) { Clear(); builtAssets = assets; }
            float clock = airport.Deicing.Clock;
            // Long headless fast-forwards show the latest spray, without replaying minutes of discarded particles.
            float dt = Mathf.Clamp(clock - lastClock, 0f, .75f);
            lastClock = clock;
            if (assets == null) return;
            PresentWorkAreas();
            PresentDeliveries(dt);
            removed.Clear();
            foreach (var pair in equipment)
                if (!airport.deicingFacilities.Contains(pair.Key) || airport.Deicing.Layout(pair.Key) == null)
                    removed.Add(pair.Key);
            foreach (var facility in removed) { Destroy(equipment[facility]); equipment.Remove(facility); }
            foreach (var facility in airport.Deicing.Facilities)
            {
                if (facility == null) continue;
                var layout = airport.Deicing.Layout(facility);
                if (layout == null) continue;
                if (!equipment.TryGetValue(facility, out var station))
                { station = new Equipment(); equipment.Add(facility, station); }
                var states = airport.Deicing.TrucksFor(facility);
                int count = assets.truckPrefab != null ? Mathf.Clamp(facility.machines, 0, AirportDeicing.MaxMachines) : 0;
                while (station.trucks.Count < count) station.trucks.Add(MakeTruck(station.trucks.Count));
                while (station.trucks.Count > count)
                {
                    var truck = station.trucks[station.trucks.Count - 1];
                    AirfieldPart.DestroyAny(truck.root); if (truck.hose != null) AirfieldPart.DestroyAny(truck.hose.gameObject); station.trucks.RemoveAt(station.trucks.Count - 1);
                }
                for (int i = 0; i < station.trucks.Count; i++)
                    PresentTruck(station.trucks[i], states.Count > i ? states[i] : null, layout, i, dt);
            }
        }

        Transform Container
        {
            get
            {
                if (container != null) return container;
                container = new GameObject("Deicing equipment (runtime)") { hideFlags = HideFlags.DontSave }.transform;
                container.SetParent(transform, false);
                return container;
            }
        }
        GameObject Copy(GameObject source, string name)
        {
            var copy = Instantiate(source, Container); copy.name = name;
            foreach (var child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.DontSave;
            // These trucks are controlled by this rig, never by a maintenance team.
            foreach (var vehicle in copy.GetComponentsInChildren<Vehicle>(true)) vehicle.enabled = false;
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            return copy;
        }
        void PresentWorkAreas()
        {
            removedAreas.Clear();
            foreach (var area in tanks.Keys) if (!airport.deicingWorkAreas.Contains(area)) removedAreas.Add(area);
            foreach (var area in removedAreas) { AirfieldPart.DestroyAny(tanks[area]); tanks.Remove(area); }
            removedAreas.Clear();
            foreach(var area in recyclers.Keys)
                if(!airport.deicingWorkAreas.Contains(area) || !area.recyclingInstalled || assets.recyclerPrefab==null) removedAreas.Add(area);
            foreach(var area in removedAreas) { AirfieldPart.DestroyAny(recyclers[area]); recyclers.Remove(area); }
            int stamp = airport.LayoutVersion;
            foreach (var area in airport.Deicing.WorkAreas)
            {
                stamp = stamp * 31 + area.position.GetHashCode() + area.heading.GetHashCode() + area.id + (area.recyclingInstalled?1:0);
                if (assets.tankPrefab != null)
                {
                    if (!tanks.TryGetValue(area, out var tank))
                    { tank = Copy(assets.tankPrefab, "Deicing work area " + area.id + " fluid storage"); tanks.Add(area, tank); }
                    tank.transform.SetLocalPositionAndRotation(Geo.X0Z(area.TankPosition + area.Axis * 4.2f), Yaw(-area.Axis));
                }
                if(area.recyclingInstalled && assets.recyclerPrefab!=null)
                {
                    if(!recyclers.TryGetValue(area,out var recycler))
                    { recycler=Copy(assets.recyclerPrefab,area.Name+" glycol recycler"); recyclers.Add(area,recycler); }
                    recycler.transform.SetLocalPositionAndRotation(Geo.X0Z(area.RecyclerPosition),Yaw(area.RecyclerFacing));
                }
            }
            foreach(var facility in airport.Deicing.Facilities)
                if(facility!=null) stamp=stamp*31+facility.standId*7+facility.workAreaId;
            foreach (var bay in airport.Deicing.ParkingBays)
                if (bay != null) stamp = stamp * 31 + bay.id + bay.position.GetHashCode() + bay.heading.GetHashCode();
            foreach (var access in airport.Deicing.OutsideAccess)
                if (access != null) stamp = stamp * 31 + access.nodeId;
            if (workAreaPaint != null && stamp == paintStamp) return;
            paintStamp = stamp; workAreaPaint ??= new AirfieldPart(transform, "Deicing work area markings");
            var paint = new PaintBuilder();
            foreach (var area in airport.Deicing.WorkAreas)
                DeicingSupplyMarkings.Build(paint, area);
            foreach(var facility in airport.Deicing.Facilities)
            {
                if(facility==null || airport.Deicing.WorkAreaFor(facility)?.recyclingInstalled!=true) continue;
                var layout=airport.Deicing.Layout(facility); if(layout==null) continue;
                foreach(float x in new[]{layout.x0+RunwayRules.DeIcingVehicleLane+.5f,layout.x1-RunwayRules.DeIcingVehicleLane-.5f})
                {
                    float start=layout.near0+2f,end=layout.far-2f;
                    paint.Line(layout.At(x,start),layout.At(x,end),.20f,new Color(.13f,.15f,.16f,1f));
                    for(float z=start+.2f;z<end;z+=.65f)
                        paint.Line(layout.At(x-.18f,z),layout.At(x+.18f,z),.045f,new Color(.48f,.51f,.52f,1f));
                }
            }
            foreach (var bay in airport.Deicing.ParkingBays)
            {
                if (bay == null || bay.legacy) continue;
                var outline = bay.Outline;
                for (int i = 0; i < outline.Length; i++)
                    paint.Line(outline[i], outline[(i + 1) % outline.Length], .10f, PaintBuilder.Yellow);
                paint.Text(bay.position + bay.Axis * 3f, bay.Axis, "P" + bay.id, 1.2f, PaintBuilder.White, 1.4f);
                // A short nose stop makes the parking direction readable from the airport view.
                paint.Line(bay.position + bay.Axis * (DeicingParkingBay.Length * .5f - .7f) - bay.Right * 1.4f,
                    bay.position + bay.Axis * (DeicingParkingBay.Length * .5f - .7f) + bay.Right * 1.4f,
                    .18f, PaintBuilder.White);
            }
            foreach (var access in airport.Deicing.OutsideAccess)
            {
                if (access == null) continue;
                var node = airport.Taxiways.nodes.Find(n => n.id == access.nodeId);
                if (node == null) continue;
                var at = airport.Taxiways.At(access.nodeId);
                foreach (var edge in airport.Taxiways.edges)
                {
                    if (!edge.IsRoad || (edge.a != access.nodeId && edge.b != access.nodeId)) continue;
                    var inward = (airport.Taxiways.At(edge.a == access.nodeId ? edge.b : edge.a) - at).normalized;
                    var right = Geo.Right(inward);
                    paint.Line(at - right * edge.width * .5f, at + right * edge.width * .5f, .25f, PaintBuilder.Yellow);
                    paint.Text(at + inward * 4f, inward, "OUT", 1.2f, PaintBuilder.White, 1.35f);
                    break;
                }
            }
            workAreaPaint.Set(paint.Apply(workAreaPaint.Target, "Deicing refill lanes and recovery drains"), airport.paint);
        }

        void PresentDeliveries(float dt)
        {
            seenDeliveries.Clear();
            foreach(var delivery in airport.Deicing.Deliveries)
            {
                if(delivery==null || delivery.Area==null || delivery.Drive==null || delivery.Phase==DeicingDeliveryPhase.Complete ||
                    delivery.Phase==DeicingDeliveryPhase.Queued || assets.tankerPrefab==null) continue;
                seenDeliveries.Add(delivery);
                if(!tankers.TryGetValue(delivery,out var tanker))
                {
                    tanker=new Tanker { root=Copy(assets.tankerPrefab,"Glycol delivery tanker") };
                    tanker.vehicle=tanker.root.GetComponent<Vehicle>();
                    if(tanker.vehicle==null && assets.tankerType!=null) tanker.vehicle=tanker.root.AddComponent<Vehicle>();
                    if(tanker.vehicle!=null) { tanker.vehicle.enabled=false; tanker.vehicle.type=assets.tankerType; tanker.vehicle.FindParts(); }
                    tanker.lights=tanker.root.GetComponent<VehicleLights>();
                    foreach(var part in tanker.root.GetComponentsInChildren<Transform>(true))
                        if(part.name=="HoseReel") { tanker.reel=part; tanker.reelRest=part.localRotation; break; }
                    tankers.Add(delivery,tanker);
                }
                var drive=delivery.Drive;
                var position=airport.transform.TransformPoint(Geo.X0Z(drive.Position,float.IsNaN(drive.Pose.elevation)?airport.PavementElevationAt(drive.Position):drive.Pose.elevation));
                var facing=airport.transform.TransformDirection(Geo.X0Z(drive.Forward));
                if(tanker.vehicle!=null) tanker.vehicle.Place(position,facing,drive.Speed,drive.Steering,dt);
                else tanker.root.transform.SetPositionAndRotation(position,Quaternion.LookRotation(facing,airport.transform.up));
                bool handling=delivery.Phase==DeicingDeliveryPhase.Connecting || delivery.Phase==DeicingDeliveryPhase.Unloading || delivery.Phase==DeicingDeliveryPhase.Disconnecting || delivery.HoseConnected || delivery.HoseProgress>.001f;
                float hose=handling?Mathf.Clamp01(delivery.HoseProgress):0f;
                if(tanker.reel!=null && hose<=.001f) tanker.reel.localRotation=tanker.reelRest;
                if(tanker.lights!=null)
                { tanker.lights.beacon=true; tanker.lights.headlights=!handling; tanker.lights.work=handling; tanker.lights.flood=false; tanker.lights.UpdateState(); }
                if(hose<=.001f || assets.sprayMaterial==null || !tanks.TryGetValue(delivery.Area,out var tank))
                { if(tanker.hose!=null) tanker.hose.enabled=false; continue; }
                if(tanker.hose==null) tanker.hose=MakeHose("Delivery tanker transfer hose",.10f);
                var outlet=airport.transform.InverseTransformPoint(tanker.root.transform.TransformPoint(assets.tankerHosePoint));
                var inlet=airport.transform.InverseTransformPoint(tank.transform.TransformPoint(assets.tankDeliveryPoint));
                var direction=inlet-outlet; direction.y=0f;
                direction=direction.sqrMagnitude>.001f?direction.normalized:delivery.Area.Right;
                tanker.hosePoints[0]=outlet; tanker.hosePoints[1]=outlet+direction*.45f;
                var low=outlet+direction*.7f; low.y=.11f; tanker.hosePoints[2]=low;
                var end=inlet-direction*.65f; end.y=.11f; tanker.hosePoints[4]=end;
                tanker.hosePoints[3]=Vector3.Lerp(low,end,.5f);
                tanker.hosePoints[5]=inlet-direction*.2f; tanker.hosePoints[6]=inlet;
                if(tanker.reel!=null)
                {
                    float length=0f; for(int i=1;i<tanker.hosePoints.Length;i++) length+=Vector3.Distance(tanker.hosePoints[i-1],tanker.hosePoints[i]);
                    tanker.reel.localRotation=tanker.reelRest*Quaternion.AngleAxis(hose*length/.28f*Mathf.Rad2Deg,Vector3.right);
                }
                PayOut(tanker.hose,tanker.hosePoints,hose);
            }
            removedDeliveries.Clear();
            foreach(var delivery in tankers.Keys) if(!seenDeliveries.Contains(delivery)) removedDeliveries.Add(delivery);
            foreach(var delivery in removedDeliveries)
            {
                var tanker=tankers[delivery];
                if(tanker.root!=null) { tanker.root.SetActive(false); AirfieldPart.DestroyAny(tanker.root); }
                if(tanker.hose!=null) AirfieldPart.DestroyAny(tanker.hose.gameObject);
                tankers.Remove(delivery);
            }
        }

        LineRenderer MakeHose(string name,float width)
        {
            if(hoseMaterial==null && assets.sprayMaterial!=null)
            {
                hoseMaterial=new Material(assets.sprayMaterial) { name="Deicing rubber hose",hideFlags=HideFlags.DontSave };
                if(hoseMaterial.HasProperty("_BaseMap")) hoseMaterial.SetTexture("_BaseMap",Texture2D.whiteTexture);
            }
            var go=new GameObject(name) { hideFlags=HideFlags.DontSave }; go.transform.SetParent(Container,false);
            var line=go.AddComponent<LineRenderer>(); line.useWorldSpace=false; line.sharedMaterial=hoseMaterial;
            line.startWidth=line.endWidth=width; line.startColor=line.endColor=new Color(.045f,.055f,.06f,1f);
            line.numCornerVertices=4; line.numCapVertices=4; line.shadowCastingMode=ShadowCastingMode.Off; line.receiveShadows=false;
            return line;
        }
        static void PayOut(LineRenderer line,Vector3[] route,float progress)
        {
            float total=0f; for(int i=1;i<route.Length;i++) total+=Vector3.Distance(route[i-1],route[i]);
            float left=total*Mathf.SmoothStep(0f,1f,Mathf.Clamp01(progress));
            line.positionCount=route.Length; line.SetPosition(0,route[0]); int count=1;
            for(int i=1;i<route.Length;i++)
            {
                float length=Vector3.Distance(route[i-1],route[i]);
                if(left>=length) { line.SetPosition(count++,route[i]); left-=length; continue; }
                line.SetPosition(count++,Vector3.Lerp(route[i-1],route[i],length>.0001f?left/length:1f)); break;
            }
            line.positionCount=count; line.enabled=count>1;
        }

        Truck MakeTruck(int index)
        {
            var truck = new Truck { root = Copy(assets.truckPrefab, "Deicing truck " + (index + 1)) };
            var parts = new Dictionary<string, Transform>();
            foreach (var part in truck.root.GetComponentsInChildren<Transform>(true)) parts[part.name] = part;
            parts.TryGetValue("BoomYaw", out truck.yaw); parts.TryGetValue("BoomLift", out truck.lift);
            parts.TryGetValue("BoomElbow", out truck.elbow); parts.TryGetValue("BoomExtend", out truck.extension);
            parts.TryGetValue("BoomCabin", out truck.cabin); parts.TryGetValue("SprayNozzle", out truck.nozzle);
            void Parent(Transform part, Transform parent) { if (part != null && parent != null) part.SetParent(parent, true); }
            Parent(truck.lift, truck.yaw); Parent(truck.elbow, truck.lift); Parent(truck.extension, truck.elbow);
            Parent(truck.cabin, truck.extension); Parent(truck.nozzle, truck.cabin);
            if (truck.yaw != null) truck.yawRest = truck.yaw.localRotation;
            if (truck.lift != null) truck.liftRest = truck.lift.localRotation;
            if (truck.elbow != null) truck.elbowRest = truck.elbow.localRotation;
            if (truck.extension != null) truck.extensionRest = truck.extension.localPosition;
            if (truck.nozzle != null) truck.nozzleRest = truck.nozzle.localRotation;
            truck.lights = truck.root.GetComponent<VehicleLights>();
            truck.vehicle = truck.root.GetComponent<Vehicle>();
            if (truck.vehicle == null && assets.truckType != null) truck.vehicle = truck.root.AddComponent<Vehicle>();
            if (truck.vehicle != null) { truck.vehicle.enabled = false; truck.vehicle.type = assets.truckType; truck.vehicle.FindParts(); }
            if (assets.sprayMaterial != null && truck.nozzle != null)
            { truck.spray = MakeSpray(truck.root.transform, false); truck.mist = MakeSpray(truck.root.transform, true); }
            return truck;
        }

        void PresentTruck(Truck truck, DeicingTruckState state, StandLayout layout, int index, float dt)
        {
            bool visible = state == null || !state.DeliveryPending || state.Drive != null;
            truck.root.SetActive(visible);
            if (!visible)
            {
                if (truck.hose != null) truck.hose.enabled = false;
                truck.spray?.Clear(); truck.mist?.Clear();
                return;
            }
            var pose = state?.Drive != null ? state.Drive.Pose : DeicingMotion.ParkPosition(layout, index, assets.truckType);
            var position = airport.transform.TransformPoint(Geo.X0Z(pose.position,float.IsNaN(pose.elevation)?airport.PavementElevationAt(pose.position):pose.elevation));
            var forward = airport.transform.TransformDirection(Geo.X0Z(pose.facing));
            float speed = state?.Drive?.Speed ?? 0f, steering = state?.Drive?.Steering ?? 0f;
            if (truck.vehicle != null) truck.vehicle.Place(position, forward, speed, steering, dt);
            else truck.root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, airport.transform.up));
            bool working = state != null && (state.Stage == DeicingTruckStage.Working || state.Stage == DeicingTruckStage.ToAircraft);
            float desired = working ? 1f : 0f;
            if (state?.Drive != null && !state.Drive.Finished)
                desired *= Mathf.Clamp01((4f - (state.Drive.Path.Length - state.Drive.Along)) / 4f);
            truck.deployment = Mathf.MoveTowards(truck.deployment, desired, dt * 2f);
            var target = state?.Target ?? Vector3.zero;
            PoseBoom(truck, target, truck.deployment);
            bool spraying = state != null && state.IsSpraying && truck.deployment > .85f;
            if (truck.lights != null)
            {
                bool active = state != null && state.Stage != DeicingTruckStage.Parked;
                truck.lights.beacon = active; truck.lights.work = working;
                truck.lights.headlights = active; truck.lights.flood = false; truck.lights.UpdateState();
            }
            PresentRefillHose(truck, state);
            PresentSpray(truck, target, spraying, dt);
        }

        void PresentRefillHose(Truck truck, DeicingTruckState state)
        {
            var area=state!=null?airport.Deicing.WorkAreaFor(state.facility):null;
            bool filling=state!=null && state.Stage==DeicingTruckStage.Filling && state.Drive!=null && state.Drive.Finished;
            if(!filling || area==null || !tanks.TryGetValue(area,out var tank) || assets.sprayMaterial==null)
            { if(truck.hose!=null) truck.hose.enabled=false; return; }
            if(truck.hose==null) { truck.hose=MakeHose("Deicing truck refill hose",.09f); truck.hose.positionCount=6; }
            var pump=airport.transform.InverseTransformPoint(tank.transform.TransformPoint(assets.tankPumpPoint));
            var coupling=airport.transform.InverseTransformPoint(truck.root.transform.TransformPoint(assets.refillPoint));
            var direction=(coupling-pump).normalized;
            truck.hosePoints[0]=pump; truck.hosePoints[1]=pump+direction*.55f;
            var low=Vector3.Lerp(pump,coupling,.35f); low.y=.10f; truck.hosePoints[2]=low;
            low=Vector3.Lerp(pump,coupling,.75f); low.y=.10f; truck.hosePoints[3]=low;
            truck.hosePoints[4]=coupling-direction*.35f; truck.hosePoints[5]=coupling;
            truck.hose.SetPositions(truck.hosePoints); truck.hose.enabled=true;
        }

        void PoseBoom(Truck truck, Vector3 target, float deployed)
        {
            if (truck.yaw == null || truck.lift == null || truck.elbow == null || truck.extension == null || truck.cabin == null || truck.nozzle == null) return;
            truck.yaw.localRotation = truck.yawRest; truck.lift.localRotation = truck.liftRest;
            truck.elbow.localRotation = truck.elbowRest; truck.extension.localPosition = truck.extensionRest;
            truck.nozzle.localRotation = truck.nozzleRest;
            var worldTarget = airport.transform.TransformPoint(target);
            var root = truck.root.transform;
            var at = airport.transform.InverseTransformPoint(root.position);
            var forward = Geo.XZ(airport.transform.InverseTransformDirection(root.forward)).normalized;
            var solution = DeicingBoomKinematics.Solve(new BaggageVehiclePose(Geo.XZ(at), forward),target,assets);
            truck.yaw.localRotation = truck.yawRest * Quaternion.Euler(0f, solution.yaw * deployed, 0f);
            truck.lift.localRotation = truck.liftRest * Quaternion.Euler(-solution.shoulder * deployed, 0f, 0f);
            truck.elbow.localRotation = truck.elbowRest * Quaternion.Euler(-solution.elbow * deployed, 0f, 0f);
            truck.extension.localPosition = truck.extensionRest + Vector3.forward * (solution.extension * deployed);
            // The enclosed tip cabin stays level while shoulder, elbow and telescope move. Its front faces the work.
            var flat = worldTarget - truck.cabin.position; flat -= airport.transform.up * Vector3.Dot(flat, airport.transform.up);
            var cabinRotation = flat.sqrMagnitude > .001f ? Quaternion.LookRotation(flat, airport.transform.up) : root.rotation;
            truck.cabin.rotation = Quaternion.Slerp(root.rotation, cabinRotation, deployed);
            var aim = worldTarget - truck.nozzle.position;
            if (deployed > 0f && aim.sqrMagnitude > .001f)
                truck.nozzle.rotation = Quaternion.Slerp(truck.cabin.rotation * truck.nozzleRest, Quaternion.LookRotation(aim, airport.transform.up), deployed);
        }

        ParticleSystem MakeSpray(Transform parent, bool mist)
        {
            var go = new GameObject(mist ? "Deicing impact mist" : "Deicing spray") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = mist ? 160 : 400;
            main.startLifetime = mist ? new ParticleSystem.MinMaxCurve(.5f, .8f) : new ParticleSystem.MinMaxCurve(.34f);
            main.startSpeed = mist ? new ParticleSystem.MinMaxCurve(.25f, .7f) : new ParticleSystem.MinMaxCurve(12f);
            main.startSize = mist ? new ParticleSystem.MinMaxCurve(.22f, .4f) : new ParticleSystem.MinMaxCurve(.07f, .12f);
            main.startColor = new Color(.88f, .95f, 1f, mist ? .16f : .42f);
            main.gravityModifier = mist ? -.015f : .01f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = mist ? 65f : 12f; shape.radius = mist ? .2f : .025f;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, mist ? .5f : .45f, 1, mist ? 2.5f : 2f));
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.6f, .75f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = assets.sprayMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (!mist)
            { renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.lengthScale = 1.5f; renderer.velocityScale = .018f; }
            ps.Pause(); return ps;
        }

        void PresentSpray(Truck truck, Vector3 target, bool spraying, float dt)
        {
            if (truck.spray == null || truck.nozzle == null) return;
            if (!spraying)
            { truck.spray.Clear(); if (truck.mist != null) truck.mist.Clear(); truck.emitted = truck.mistEmitted = 0f; return; }
            var point = airport.transform.TransformPoint(target);
            var outlet = truck.nozzle.TransformPoint(new Vector3(0f, 0f, .47f));
            var direction = point - outlet;
            if (direction.sqrMagnitude < .01f || direction.magnitude > DeicingBoomKinematics.MaxJetDistance + .02f)
            { truck.spray.Clear(); if (truck.mist != null) truck.mist.Clear(); return; }
            truck.spray.transform.SetPositionAndRotation(outlet, Quaternion.LookRotation(direction, airport.transform.up));
            var main = truck.spray.main; main.startSpeed = direction.magnitude / .34f;
            if (truck.mist != null) truck.mist.transform.SetPositionAndRotation(point,
                Quaternion.LookRotation((airport.transform.up - direction.normalized * .25f).normalized));
            // Particle systems stay paused between explicit game-time steps. Repeated render/capture callbacks and
            // paused frames therefore emit nothing and cannot advance or age the spray.
            for (float left = dt; left > .00001f;)
            {
                float step = Mathf.Min(left, 1f / 120f); left -= step;
                truck.spray.Simulate(step, false, false, false);
                truck.emitted += step * 600f;
                int count = Mathf.FloorToInt(truck.emitted); truck.emitted -= count;
                if (count > 0) truck.spray.Emit(count);
                if (truck.mist != null)
                {
                    truck.mist.Simulate(step, false, false, false); truck.mistEmitted += step * 90f;
                    int mistCount = Mathf.FloorToInt(truck.mistEmitted); truck.mistEmitted -= mistCount;
                    if (mistCount > 0) truck.mist.Emit(mistCount);
                }
            }
            truck.spray.Pause(); if (truck.mist != null) truck.mist.Pause();
        }
        static Quaternion Yaw(Vector2 facing) => Quaternion.LookRotation(Geo.X0Z(facing), Vector3.up);
        static void Destroy(Equipment station)
        {
            foreach (var truck in station.trucks)
            { if (truck.root != null) AirfieldPart.DestroyAny(truck.root); if (truck.hose != null) AirfieldPart.DestroyAny(truck.hose.gameObject); }
        }
        public void Clear()
        {
            equipment.Clear(); tanks.Clear(); recyclers.Clear(); tankers.Clear(); seenDeliveries.Clear(); removedDeliveries.Clear();
            if(hoseMaterial!=null) AirfieldPart.DestroyAny(hoseMaterial); hoseMaterial=null;
            workAreaPaint?.Destroy(); workAreaPaint = null; paintStamp = 0;
            if (container != null) AirfieldPart.DestroyAny(container.gameObject);
            container = null; builtAssets = null;
        }
    }
}
