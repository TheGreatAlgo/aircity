using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.Traffic;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Vehicles
{
    public class DeicingTests
    {
        readonly List<Object> spawned = new List<Object>();
        float glow;
        static Vector3 X0Z(Vector2 p) => new Vector3(p.x,0f,p.y);
        static Vector2 XZ(Vector3 p) => new Vector2(p.x,p.z);
        [SetUp] public void Setup() { glow = AircraftLights.Glow; AircraftLights.Glow = 0f; }
        [TearDown] public void Cleanup()
        { foreach (var item in spawned) if (item != null) Object.DestroyImmediate(item); spawned.Clear(); AircraftLights.Glow = glow; }

        (Airport airport, StandLayout layout, DeicingFacility facility, AirCity.Aircraft.Aircraft aircraft) Pad(int machines = 2, bool road = true, string model = "Narrowbody")
        {
            var go = new GameObject("Deicing airport"); go.SetActive(false); var airport = go.AddComponent<Airport>();
            airport.groundSize = 0f; go.SetActive(true); spawned.Add(go);
            airport.aprons.Add(ApronArea.Rect(new Vector2(0f,-20f),new Vector2(300f,300f),0f,PavementSurface.Concrete));
            var type = AircraftSetup.AllTypes().First(t=>t.model==model);
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(0f,0f,-200f)), b = net.AddNode(new Vector3(0f,0f,200f));
            net.Connect(a,b,RunwayRules.TaxiwayWidthFor(type.SizeCode),PavementSurface.Concrete);
            var stand = net.AddStand(StandKind.DeIcing,Vector3.zero,0f,(SizeCode)(type.SizeCode-'A'),PavementSurface.Concrete);
            Assert.That(stand,Is.Not.Null); var layout = net.Layout(stand);
            var facility = airport.Deicing.Configure(stand);
            var area = airport.Deicing.AddWorkArea(new Vector2(-90f,-80f),0f);
            Assert.That(area,Is.Not.Null,airport.Deicing.WorkAreaProblem(new Vector2(-90f,-80f),0f));
            Assert.That(airport.Deicing.AssignWorkArea(facility,area),Is.True);
            if (road) AddDeliveryRoad(airport,area);
            facility.machines=machines; // Existing, already delivered fleet; purchase/arrival has its own fixture.
            airport.Deicing.TrucksFor(facility);
            var aircraftObject = new GameObject("Deicing aircraft"); spawned.Add(aircraftObject);
            aircraftObject.transform.position = X0Z(layout.ParkPosition(type));
            var aircraft = aircraftObject.AddComponent<AirCity.Aircraft.Aircraft>(); aircraft.type = type;
            return (airport,layout,facility,aircraft);
        }
        void AddDeliveryRoad(Airport airport,DeicingWorkArea area)
        {
            var net=airport.Taxiways;
            int a=net.AddNode(X0Z(area.Point(-120f,-22f))),b=net.AddNode(X0Z(area.Point(120f,-22f)));
            net.Connect(a,b,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            Assert.That(airport.Deicing.AddOutsideAccess(a,out var problem),Is.Not.Null,problem);
        }
        static float Run(Airport airport,Func<bool> done,float maximum=600f,Action each=null)
        {
            float elapsed=0f;
            while (!done() && elapsed<maximum)
            { airport.Deicing.Simulate(.1f); each?.Invoke(); elapsed+=.1f; }
            return elapsed;
        }
        static void Ready(Airport airport,DeicingFacility facility,float litres=AirportDeicing.TruckCapacityLitres)
        { foreach (var truck in airport.Deicing.TrucksFor(facility)) truck.onboardLitres=litres; }
        static float Stored(Airport airport,DeicingFacility facility)
        { return airport.Deicing.WorkAreaFor(facility).fluidLitres+airport.Deicing.TrucksFor(facility).Sum(t=>t.FluidLitres); }

        [Test] public void Supply_RequiresRoadDelaysArrivalAndRefusesOverfill()
        {
            var (airport,_,facility,_)=Pad(0,false); var area=airport.Deicing.WorkAreaFor(facility);
            Assert.That(area.fluidLitres,Is.Zero);
            Assert.That(airport.Deicing.OrderFluid(area,1000f,out var problem),Is.False); StringAssert.Contains("service road",problem);
            AddDeliveryRoad(airport,area);
            Assert.That(airport.Deicing.OrderFluid(area,area.capacityLitres+1f,out problem),Is.False); StringAssert.Contains("overfill",problem);
            Assert.That(airport.Deicing.OrderFluid(area,1000f,out problem),Is.True,problem);
            Assert.That(airport.Deicing.OrderFluid(area,100f,out problem),Is.False);
            airport.Deicing.Simulate(AirportDeicing.DeliveryLeadSeconds-1f); Assert.That(area.fluidLitres,Is.Zero);
            airport.Deicing.Simulate(1.1f); Assert.That(area.fluidLitres,Is.Zero,"Supplier dispatch must not teleport fluid into storage.");
            Run(airport,()=>!area.DeliveryPending,300f);
            Assert.That(area.fluidLitres,Is.EqualTo(1000f).Within(.001f)); Assert.That(area.DeliveryPending,Is.False);
        }
        [Test] public void NewEmptyTruck_DrivesToRemoteAreaTransfersStockAndReturns()
        {
            var (airport,_,facility,aircraft)=Pad(1); aircraft.gameObject.SetActive(false);
            var area=airport.Deicing.WorkAreaFor(facility); area.fluidLitres=2000f;
            var truck=airport.Deicing.TrucksFor(facility)[0]; var initial=truck.Position;
            Assert.That(truck.FluidLitres,Is.Zero);
            airport.Deicing.Simulate(1f);
            Assert.That(truck.FluidLitres,Is.Zero,"Remote stock cannot teleport into a truck.");
            Assert.That(truck.Stage,Is.EqualTo(DeicingTruckStage.ToWorkArea));
            Run(airport,()=>truck.FluidLitres>0f,300f);
            Assert.That(truck.FluidLitres,Is.GreaterThan(0f),truck.Status);
            Assert.That(Vector2.Distance(truck.Position,initial),Is.GreaterThan(30f));
            Assert.That(Stored(airport,facility),Is.EqualTo(2000f).Within(.05f));
            Run(airport,()=>truck.Stage==DeicingTruckStage.Parked && truck.FluidLitres>=1199f,300f);
            Assert.That(truck.FluidLitres,Is.EqualTo(1200f).Within(.01f),truck.Status);
            Assert.That(Vector2.Distance(truck.Position,initial),Is.LessThan(.5f));
        }
        [Test] public void DisconnectedRemoteArea_HoldsEmptyTruckAndNeverTransfersFluid()
        {
            var (airport,_,facility,aircraft)=Pad(1); aircraft.gameObject.SetActive(false);
            var area=airport.Deicing.WorkAreaFor(facility); area.fluidLitres=2000f;
            airport.aprons.Clear();
            airport.aprons.Add(ApronArea.Rect(Vector2.zero,new Vector2(90f,90f),0f,PavementSurface.Concrete));
            airport.aprons.Add(ApronArea.Rect(area.position,new Vector2(24f,28f),0f,PavementSurface.Concrete));
            var truck=airport.Deicing.TrucksFor(facility)[0]; var initial=truck.Position;
            airport.Deicing.Simulate(.1f);
            Assert.That(truck.FluidLitres,Is.Zero); Assert.That(area.fluidLitres,Is.EqualTo(2000f));
            Assert.That(truck.Position,Is.EqualTo(initial)); StringAssert.Contains("route",truck.Status);
        }
        [Test] public void IdleLowTruck_ProactivelyRefillsWithoutTakingFluidAtADistance()
        {
            var (airport,_,facility,aircraft)=Pad(1); aircraft.gameObject.SetActive(false);
            var area=airport.Deicing.WorkAreaFor(facility); area.fluidLitres=2000f;
            var truck=airport.Deicing.TrucksFor(facility)[0]; truck.onboardLitres=100f;
            airport.Deicing.Simulate(.1f);
            Assert.That(truck.Stage,Is.EqualTo(DeicingTruckStage.ToWorkArea));
            Assert.That(truck.FluidLitres,Is.EqualTo(100f));
            Assert.That(area.fluidLitres,Is.EqualTo(2000f));
        }
        [Test] public void ExhaustedTrucks_RefillAutomaticallyAndResumeSameWingProgress()
        {
            var (airport,_,facility,_)=Pad(); var area=airport.Deicing.WorkAreaFor(facility); area.fluidLitres=2000f;
            Ready(airport,facility,25f); airport.Deicing.Simulate(.1f); var job=airport.Deicing.JobFor(facility);
            Assert.That(job,Is.Not.Null); bool refill=false; float beforeRefill=0f;
            float elapsed=Run(airport,()=>job.Completed,600f,()=>
            {
                if (airport.Deicing.TrucksFor(facility).Any(t=>t.Stage==DeicingTruckStage.ToWorkArea)) { refill=true; beforeRefill=Mathf.Max(beforeRefill,job.UsedLitres); }
                Assert.That(Stored(airport,facility)+job.UsedLitres,Is.EqualTo(2050f).Within(.2f));
            });
            Assert.That(refill,Is.True); Assert.That(beforeRefill,Is.GreaterThan(0f));
            Assert.That(airport.Deicing.JobFor(facility),Is.SameAs(job)); Assert.That(job.Completed,Is.True,$"{elapsed}s: {job.Waiting}");
            Assert.That(job.Progress,Is.EqualTo(1f).Within(.001f));
            float stock=Stored(airport,facility); airport.Deicing.Simulate(10f);
            Assert.That(Stored(airport,facility),Is.EqualTo(stock).Within(.01f),"Completed aircraft is not treated twice.");
        }
        [Test] public void MovingAircraftAndPausedSimulation_DoNotConsumeFluid()
        {
            var (airport,_,facility,aircraft)=Pad(); Ready(airport,facility);
            var motion=aircraft.gameObject.AddComponent<AircraftGroundMotion>(); motion.Drive(2f,0f);
            airport.Deicing.Simulate(1f); Assert.That(airport.Deicing.JobFor(facility),Is.Null);
            motion.Drive(0f,0f); Run(airport,()=>airport.Deicing.JobFor(facility)?.IsSpraying==true,180f);
            var job=airport.Deicing.JobFor(facility); Assert.That(job.IsSpraying,Is.True,job.Waiting);
            float used=job.UsedLitres,clock=airport.Deicing.Clock; var positions=airport.Deicing.TrucksFor(facility).Select(t=>t.Position).ToArray();
            airport.Deicing.Simulate(0f); Assert.That(job.UsedLitres,Is.EqualTo(used)); Assert.That(airport.Deicing.Clock,Is.EqualTo(clock));
            CollectionAssert.AreEqual(positions,airport.Deicing.TrucksFor(facility).Select(t=>t.Position).ToArray());
            motion.Drive(2f,0f); airport.Deicing.Simulate(1f); Assert.That(job.UsedLitres,Is.EqualTo(used)); Assert.That(job.IsSpraying,Is.False);
        }
        [TestCase("Cessna172",240f)]
        [TestCase("Turboprop",240f)]
        [TestCase("Narrowbody",90f)]
        [TestCase("Trijet",240f)]
        [TestCase("Widebody",240f)]
        [TestCase("Quadjet",240f)]
        [TestCase("DoubleDeck",120f)]
        [TestCase("Supersonic",240f)]
        public void PairedTreatment_HasShortSprayPacingAndConservesOnboardFluid(string model,float maximum)
        {
            var (airport,_,facility,aircraft)=Pad(model:model); Ready(airport,facility); airport.Deicing.Simulate(.1f); var job=airport.Deicing.JobFor(facility);
            float spraying=0f;
            float elapsed=Run(airport,()=>job.Completed,240f,()=> { if (job.IsSpraying) spraying+=.1f; });
            Assert.That(job.SpraySeconds,Is.InRange(12f,22f));
            Assert.That(job.Completed,Is.True,$"{elapsed}s: {job.Waiting}; "+string.Join("; ",airport.Deicing.TrucksFor(facility)
                .Select(t=>$"truck {t.index} {t.Status} at {t.Position} facing {t.Facing}, target {t.TargetIndex}/{t.TargetCount} {t.Target}")));
            Assert.That(elapsed,Is.LessThanOrEqualTo(maximum),$"A stocked pair should finish {model} promptly.");
            Assert.That(spraying,Is.LessThan(46f),"Paired spray is a short gameplay action, including asynchronous wing work.");
            Assert.That(Stored(airport,facility)+job.UsedLitres,Is.EqualTo(2400f).Within(.1f));
            foreach (var truck in airport.Deicing.TrucksFor(facility))
            {
                var points=new List<Vector3>(); var body=airport.Deicing.TruckType.bounds;
                foreach (float x in new[] { body.min.x,body.max.x }) foreach (float z in new[] { body.min.z,body.max.z })
                    points.Add(aircraft.transform.InverseTransformPoint(airport.transform.TransformPoint(X0Z(truck.Drive.Pose.Point(new Vector3(x,0f,z))))));
                var plane=aircraft.type.bounds;
                Assert.That(points.Max(p=>p.x)<plane.min.x-.9f || points.Min(p=>p.x)>plane.max.x+.9f || points.Max(p=>p.z)<plane.min.z-.9f,
                    Is.True,"Departure clearance requires the entire truck body outside the aircraft's forward sweep.");
            }
            TestContext.Out.WriteLine($"DEICING {model}: complete {elapsed:0.0}s, active spray {spraying:0.0}s, {airport.Deicing.TrucksFor(facility).Sum(t=>t.TargetCount)} targets.");
        }
        [Test] public void ActiveRefill_SurfaceRemovalStopsInPlaceAndReleasesLease()
        {
            var (airport,_,facility,aircraft)=Pad(1); aircraft.gameObject.SetActive(false);
            var area=airport.Deicing.WorkAreaFor(facility); area.fluidLitres=2000f;
            var truck=airport.Deicing.TrucksFor(facility)[0]; airport.Deicing.Simulate(1f);
            Assert.That(truck.Stage,Is.EqualTo(DeicingTruckStage.ToWorkArea));
            Assert.That(airport.Deicing.InUse(area),Is.True);
            var at=truck.Position; var facing=truck.Facing;
            airport.aprons.Clear(); airport.BuildGround(); airport.Deicing.Simulate(.05f);
            Assert.That(truck.Position,Is.EqualTo(at)); Assert.That(truck.Facing,Is.EqualTo(facing));
            Assert.That(truck.Speed,Is.Zero); Assert.That(truck.Stage,Is.EqualTo(DeicingTruckStage.Waiting));
            Assert.That(airport.Deicing.InUse(area),Is.False,"An unreachable owner must not reserve shared supply forever.");
            Assert.That(truck.FluidLitres,Is.Zero); Assert.That(area.fluidLitres,Is.EqualTo(2000f));
        }
        [Test] public void PendingDelivery_AccessLossPausesThenResumesOnce()
        {
            var (airport,_,facility,_)=Pad(0); var area=airport.Deicing.WorkAreaFor(facility);
            Assert.That(airport.Deicing.OrderFluid(area,1000f,out var problem),Is.True,problem);
            airport.Deicing.Simulate(1f); float remaining=area.deliverySecondsRemaining;
            airport.Taxiways.RemoveEdges(airport.Taxiways.edges.Where(e=>e.IsRoad).ToArray());
            airport.Deicing.Simulate(30f); Assert.That(area.deliverySecondsRemaining,Is.EqualTo(remaining)); Assert.That(area.fluidLitres,Is.Zero);
            AddDeliveryRoad(airport,area); airport.Deicing.Simulate(remaining+.1f);
            Assert.That(area.fluidLitres,Is.Zero,"Restored road access dispatches a tanker before unloading.");
            Run(airport,()=>!area.DeliveryPending,300f);
            Assert.That(area.fluidLitres,Is.EqualTo(1000f).Within(.001f)); Assert.That(area.DeliveryPending,Is.False);
            airport.Deicing.Simulate(100f); Assert.That(area.fluidLitres,Is.EqualTo(1000f).Within(.001f));
        }
        [Test] public void LegacySupply_MigratesStockPendingDeliveryAndOnboardVolumeOnce()
        {
            var (airport,_,facility,_)=Pad(1); airport.deicingWorkAreas.Clear(); facility.workAreaId=0;
            facility.tankCapacityLitres=30000f; facility.fluidLitres=1000f; facility.deliveryLitres=500f; facility.deliverySecondsRemaining=40f;
            var truck=airport.Deicing.TrucksFor(facility)[0]; truck.onboardLitres=123f;
            var area=airport.Deicing.WorkAreas.Single();
            Assert.That(airport.Deicing.WorkAreas.Single(),Is.SameAs(area));
            Assert.That(area.fluidLitres,Is.EqualTo(1000f)); Assert.That(area.deliveryLitres,Is.EqualTo(500f)); Assert.That(area.deliverySecondsRemaining,Is.EqualTo(40f));
            Assert.That(truck.FluidLitres,Is.EqualTo(123f)); Assert.That(facility.fluidLitres,Is.Zero); Assert.That(facility.tankCapacityLitres,Is.Zero);
        }
        [Test] public void CancelledAircraft_AfterRefillReturnsTruckAndReleasesSupply()
        {
            var (airport,_,facility,aircraft)=Pad(1); var area=airport.Deicing.WorkAreaFor(facility); area.fluidLitres=2000f;
            var truck=airport.Deicing.TrucksFor(facility)[0];
            Run(airport,()=>truck.Stage==DeicingTruckStage.ToAircraft && truck.FluidLitres>=1199f,300f);
            Assert.That(truck.Stage,Is.EqualTo(DeicingTruckStage.ToAircraft),truck.Status);
            aircraft.gameObject.SetActive(false);
            Run(airport,()=>truck.Stage==DeicingTruckStage.Parked && truck.Drive.Finished && !airport.Deicing.InUse(area),300f);
            Assert.That(truck.Stage,Is.EqualTo(DeicingTruckStage.Parked),truck.Status);
            Assert.That(airport.Deicing.JobFor(facility),Is.Null); Assert.That(airport.Deicing.InUse(area),Is.False);
            Assert.That(Stored(airport,facility),Is.EqualTo(2000f).Within(.1f));
        }
        [Test] public void WorkAreaPlacement_RejectsGrassPadAndTankOnServiceRoad()
        {
            var (airport,_,facility,_)=Pad(0);
            Assert.That(airport.Deicing.WorkAreaProblem(new Vector2(500f,500f),0f),Does.Contain("paved apron"));
            Assert.That(airport.Deicing.WorkAreaProblem(Vector2.zero,0f),Does.Contain("aircraft pads"));
            Assert.That(airport.Deicing.WorkAreaProblem(airport.Deicing.WorkAreaFor(facility).position,0f),Does.Contain("another"));
        }
        [Test] public void TwoFacilitiesSharingSupply_SerializeRoundTripsAndBothReturnFull()
        {
            var (airport,_,first,aircraft)=Pad(1); aircraft.gameObject.SetActive(false);
            var area=airport.Deicing.WorkAreaFor(first); area.fluidLitres=5000f;
            var stand=airport.Taxiways.AddStand(StandKind.DeIcing,new Vector3(0f,0f,100f),0f,SizeCode.C,PavementSurface.Concrete);
            Assert.That(stand,Is.Not.Null);
            var second=airport.Deicing.Configure(stand);
            Assert.That(airport.Deicing.AssignWorkArea(second,area),Is.True);
            second.machines=1;
            var trucks=new[] { airport.Deicing.TrucksFor(first)[0],airport.Deicing.TrucksFor(second)[0] };
            float elapsed=Run(airport,()=>trucks.All(t=>t.Stage==DeicingTruckStage.Parked && t.FluidLitres>=1199f),600f,()=>
            {
                Assert.That(trucks.Count(t=>t.Stage==DeicingTruckStage.ToWorkArea || t.Stage==DeicingTruckStage.Filling),Is.LessThanOrEqualTo(1));
            });
            Assert.That(trucks.All(t=>t.Stage==DeicingTruckStage.Parked && t.FluidLitres>=1199f),Is.True,$"{elapsed}s: {string.Join("; ",trucks.Select(t=>t.Status))}");
            Assert.That(area.fluidLitres+trucks.Sum(t=>t.FluidLitres),Is.EqualTo(5000f).Within(.1f));
        }
        [Test] public void OneStockedTruck_CoversBothWingsAndPreservesProgressAcrossCrossover()
        {
            var (airport,_,facility,_)=Pad(1); Ready(airport,facility);
            airport.Deicing.Simulate(.1f); var job=airport.Deicing.JobFor(facility);
            var truck=airport.Deicing.TrucksFor(facility)[0];
            bool left=false,right=false; float usedBefore=0f,usedAtRight=0f;
            float elapsed=Run(airport,()=>job.Completed,240f,()=>
            {
                Assert.That(job.UsedLitres,Is.GreaterThanOrEqualTo(usedBefore)); usedBefore=job.UsedLitres;
                Assert.That(Stored(airport,facility)+job.UsedLitres,Is.EqualTo(1200f).Within(.1f));
                if (!truck.IsSpraying) return;
                if (truck.Target.x<0f) left=true;
                if (truck.Target.x>0f && !right) { right=true; usedAtRight=job.UsedLitres; }
            });
            Assert.That(left && right,Is.True,$"{elapsed:0.0}s: {truck.Status}; pose {truck.Position}/{truck.Facing}; target {truck.TargetIndex}/{truck.TargetCount} {truck.Target}; used {job.UsedLitres}/{job.requiredLitres}");
            Assert.That(usedAtRight,Is.InRange(job.requiredLitres*.45f,job.requiredLitres*.7f),"The same treatment keeps its completed left-wing dose when moving to the right.");
            Assert.That(airport.Deicing.JobFor(facility),Is.SameAs(job));
            Assert.That(job.Completed,Is.True,$"{elapsed}s: {job.Waiting}");
            Assert.That(elapsed,Is.LessThanOrEqualTo(180f));
        }
        [Test] public void RemovingBay_RemovesEquipmentStockAndItsTreatment()
        {
            var (airport, layout, facility, _) = Pad();
            Ready(airport,facility);
            airport.Deicing.Simulate(1f);
            Assert.That(airport.Deicing.JobFor(facility), Is.Not.Null);
            airport.Taxiways.RemoveStand(layout.stand);
            airport.Deicing.Simulate(1f);
            Assert.That(airport.Deicing.Facilities, Is.Empty);
            Assert.That(airport.Deicing.JobFor(facility), Is.Null);
        }

        [Test] public void SnowDeparture_ChoosesDeicingDetourInsteadOfDirectBypass()
        {
            var (airport, _, _, aircraft) = Pad();
            airport.windFrom = 0f;
            var runway = airport.AddRunway(new Vector3(200f, 0f, 500f), new Vector3(200f, 0f, 4000f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int south = net.nodes.First(n => n.position.z == -200f).id;
            int north = net.nodes.First(n => n.position.z == 200f).id;
            int start = net.AddNode(new Vector3(200f, 0f, -400f));
            int before = net.AddNode(new Vector3(200f, 0f, -200f));
            int after = net.AddNode(new Vector3(200f, 0f, 200f));
            int entry = net.AddNode(new Vector3(200f, 0f, 550f), runway);
            foreach (var pair in new[] { (start, before), (before, after), (before, south), (north, after), (after, entry) })
                net.Connect(pair.Item1, pair.Item2, 15f, PavementSurface.Asphalt);
            aircraft.transform.position = new Vector3(200f, 0f, -350f);
            var dry = airport.Traffic.PreviewDeparture(aircraft, out var problem);
            Assert.That(dry, Is.Not.Null, problem);
            Assert.That(dry.deicingStops, Is.Empty, "Dry traffic uses the shorter bypass.");
            airport.Condition = SurfaceCondition.Snow;
            var snowy = airport.Traffic.PreviewDeparture(aircraft, out problem);
            Assert.That(snowy, Is.Not.Null, problem);
            Assert.That(snowy.deicingStops.Count, Is.EqualTo(1), "Snow traffic must take the longer equipped-pad route.");
            Assert.That(snowy.path.Length, Is.GreaterThan(dry.path.Length + 100f));
        }

        [TestCase("move")]
        [TestCase("resize")]
        [TestCase("reverse")]
        public void EditingFittedBay_InvalidatesAndReplansItsOldStop(string edit)
        {
            var (airport, layout, _, aircraft) = Pad();
            airport.windFrom = 0f;
            var runway = airport.AddRunway(new Vector3(0f, 0f, 500f), new Vector3(0f, 0f, 4000f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int north = net.nodes.First(n => n.position.z == 200f).id;
            int entry = net.AddNode(new Vector3(0f, 0f, 550f), runway);
            net.Connect(north, entry, 15f, PavementSurface.Asphalt);
            aircraft.transform.position = new Vector3(0f, 0f, -150f);
            airport.Condition = SurfaceCondition.Snow;
            var flight = airport.Traffic.Depart(aircraft, out var problem);
            Assert.That(flight, Is.Not.Null, problem);
            var previous = flight.Route;
            Assert.That(previous.deicingStops.Count, Is.EqualTo(1));
            if (edit == "move") layout.stand.position += new Vector3(0f, 0f, 25f);
            else if (edit == "resize") layout.stand.code = SizeCode.D;
            else layout.stand.heading = 180f;
            net.Rebuild();
            Assert.That(airport.Deicing.StopMatches(previous, previous.deicingStops[0], aircraft.type), Is.False);
            airport.Simulate(0.05f);
            if (edit == "reverse")
            {
                Assert.That(flight.Phase, Is.EqualTo(FlightPhase.Ready));
                StringAssert.Contains("de-icing", flight.Waiting);
            }
            else
            {
                Assert.That(flight.Route, Is.Not.SameAs(previous), "Ready departure must replace its obsolete pad stop.");
                Assert.That(flight.Route.deicingStops.Count, Is.EqualTo(1));
                Assert.That(airport.Deicing.StopMatches(flight.Route, flight.Route.deicingStops[0], aircraft.type), Is.True);
            }
        }

        [Test] public void SnowDeparture_RoutesThroughBayStopsForSprayAndResumesTakeoff()
        {
            var (airport, layout, facility, aircraft) = Pad();
            airport.windFrom = 0f;
            var runway = airport.AddRunway(new Vector3(0f, 0f, 500f), new Vector3(0f, 0f, 4000f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int north = net.nodes.First(n => n.position.z == 200f).id;
            int entry = net.AddNode(new Vector3(0f, 0f, 550f), runway);
            net.Connect(north, entry, 15f, PavementSurface.Asphalt);
            // Start before the bay, with a direct taxiway departure and no tug requirement.
            aircraft.transform.position = new Vector3(0f, 0f, -150f);
            Ready(airport,facility);
            airport.Condition = SurfaceCondition.Snow;
            var flight = airport.Traffic.Depart(aircraft, out var problem);
            Assert.That(flight, Is.Not.Null, problem);
            Assert.That(flight.Route.deicingStops.Count, Is.EqualTo(1));
            bool sprayed = false, completed = false;
            for (int i = 0; i < 18000 && !flight.Finished; i++)
            {
                airport.Simulate(0.05f);
                var job = airport.Deicing.JobFor(facility);
                if (job?.IsSpraying == true)
                {
                    sprayed = true;
                    Assert.That(flight.Speed, Is.Zero);
                    Assert.That(Vector2.Distance(XZ(aircraft.transform.position) + layout.axis * aircraft.type.Wheelbase, layout.stop), Is.LessThan(1f));
                }
                completed |= job?.Completed == true;
                // Keep the runway/taxiway open; this test exercises aircraft treatment, not snow ploughs.
                runway.snow = 0f;
                foreach (var edge in net.edges) edge.snow = 0f;
            }
            Assert.That(sprayed, Is.True, flight.Status);
            Assert.That(completed, Is.True, flight.Status);
            Assert.That(flight.Phase, Is.EqualTo(FlightPhase.Gone), flight.Status);
        }
    }
}
