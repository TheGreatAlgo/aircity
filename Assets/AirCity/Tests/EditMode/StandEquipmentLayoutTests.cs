using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using AirCity.Dev;
using Object=UnityEngine.Object;

namespace AirCity.Tests
{
    public class StandEquipmentLayoutTests
    {
        readonly List<Object> objects=new List<Object>();
        [TearDown] public void Cleanup() { foreach (var o in objects) if (o != null) Object.DestroyImmediate(o); objects.Clear(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        static AircraftType Type(string model)=>AircraftSetup.AllTypes().Single(t=>t.model == model);
        public static void AssertParked(BaggageTrain train,StandMarkings.EquipmentBay bay)
        {
            void Fits(BaggageVehiclePose pose,VehicleType type)
            {
                foreach (float x in new[] { type.bounds.min.x,type.bounds.max.x })
                    foreach (float z in new[] { type.bounds.min.z,type.bounds.max.z })
                        Assert.IsTrue(bay.Contains(pose.Point(new Vector3(x,0,z))),type.model+" outside its parking bay");
            }
            Fits(train.Tractor,train.TractorType);
            for (int i=0;i<train.CartCount;i++) Fits(train.CartAt(i),train.CartType);
        }
        [Test]
        public void AircraftFootprintChecks_MatchConvexSeparatingAxesInBothWindings()
        {
            var random=new System.Random(5157);
            bool Reference(AirportMaintenance.Box box,Vector2[] polygon)
            {
                var axes=new List<Vector2> { box.ax,box.az };
                for (int i=0;i<polygon.Length;i++) { var e=polygon[(i+1)%polygon.Length]-polygon[i]; axes.Add(new Vector2(-e.y,e.x)); }
                foreach (var axis in axes)
                {
                    float min=float.MaxValue,max=float.MinValue;
                    foreach (var p in polygon) { float d=Vector2.Dot(p-box.c,axis); min=Mathf.Min(min,d); max=Mathf.Max(max,d); }
                    float r=box.hx*Mathf.Abs(Vector2.Dot(box.ax,axis))+box.hz*Mathf.Abs(Vector2.Dot(box.az,axis));
                    if (min>r || max<-r) return false;
                }
                return true;
            }
            foreach (var type in AircraftSetup.AllTypes())
                foreach (var part in type.serviceFootprints)
                    for (int i=0;i<20;i++)
                    {
                        var at=new Vector2(Mathf.Lerp(type.bounds.min.x,type.bounds.max.x,(float)random.NextDouble()),
                            Mathf.Lerp(type.bounds.min.z,type.bounds.max.z,(float)random.NextDouble()));
                        var f=BaggageServicePlan.Facing((float)random.NextDouble()*360);
                        var box=AirportMaintenance.Box.Of(at,f,new Bounds(Vector3.zero,new Vector3(2,1,5)),.2f);
                        bool expected=Reference(box,part.outline);
                        Assert.AreEqual(expected,AircraftServiceClearance.Overlaps(box,part.outline),type.model+" "+part.name);
                        Assert.AreEqual(expected,AircraftServiceClearance.Overlaps(box,part.outline.Reverse().ToArray()),"reversed "+type.model+" "+part.name);
                    }
        }
        (Airport airport,Stand stand,BaggageStation station) Create(float heading,SizeCode code=SizeCode.C)
        {
            var go=new GameObject("Staging test"); go.SetActive(false); objects.Add(go);
            var airport=go.AddComponent<Airport>(); airport.groundSize=0; AirfieldSetup.AssignMaterials(airport);
            airport.aprons.Add(ApronArea.Rect(Vector2.zero,new Vector2(800,800),0,PavementSurface.Concrete));
            airport.aircraftTypes=AircraftSetup.AllTypes().ToList(); go.SetActive(true);
            var stand=new Stand { code=code,heading=heading,noSmallAircraft=true };
            stand.Frame(new Vector2(stand.Axis.y,-stand.Axis.x),0); airport.Taxiways.Add(stand);
            var station=new GameObject("Bulk staging").AddComponent<BaggageStation>(); station.transform.SetParent(go.transform,false);
            station.standIds.Add(stand.id); station.cartCount=2;
            station.dispatchNode=airport.Taxiways.AddNode(new Vector3(400,0,400));
            station.arrivalsNode=airport.Taxiways.AddNode(new Vector3(410,0,400));
            station.tractorType=VehicleSetup.Type("BaggageTractor"); station.cartType=VehicleSetup.Type("BaggageCart");
            station.loaderType=VehicleSetup.Type("BeltLoader"); station.automaticHold.aircraft=Type(code == SizeCode.C ? "Narrowbody" : "Widebody");
            airport.Taxiways.Rebuild();
            return (airport,stand,station);
        }

        static void AssertSharedCourt(Airport airport,Stand stand)
        {
            var items=StandEquipmentLayout.All(airport).Where(p=>p.stand==stand)
                .SelectMany(p=>new[] { p.loaderBay,p.trainBay }).ToArray();
            var painted=StandMarkings.Equipment(airport).Where(b=>!b.working && b.stands==stand.Label).ToArray();
            Assert.AreEqual(1,painted.Length,"equipment shares one painted storage area");
            var court=painted[0]; Assert.IsTrue(court.storageArea);
            Assert.AreEqual("EQUIPMENT",court.equipment);
            Assert.GreaterOrEqual(court.Area,items.Sum(b=>b.width*b.length)*1.5f,"reserve space beyond the parked equipment");
            var right=new Vector2(court.facing.y,-court.facing.x);
            var corners=items.SelectMany(b=>b.Boundary()).Select(p=>new Vector2(Vector2.Dot(p,right),Vector2.Dot(p,court.facing))).ToArray();
            float groupWidth=corners.Max(p=>p.x)-corners.Min(p=>p.x),groupLength=corners.Max(p=>p.y)-corners.Min(p=>p.y);
            Assert.LessOrEqual(court.Area,groupWidth*groupLength*1.35f,"shared courts stay compact around the equipment group");
            Assert.Greater(Vector2.Dot(court.facing,airport.Taxiways.Layout(stand).nose),.999f);
            foreach (var parking in StandEquipmentLayout.All(airport).Where(p=>p.stand==stand))
            {
                var equipment=parking.station is BaggageStation bulk ? new BaggageServiceAccess(bulk) :
                    new BaggageServiceAccess((ContainerStation)parking.station);
                var path=new VehiclePath(); path.Start(parking.train.position,1); path.LineTo(parking.train.position+parking.train.facing,1);
                AssertParked(new BaggageTrain(equipment.tractorType,equipment.cartType,path,equipment.cartCount),court);
                foreach (float x in new[] { equipment.loaderType.bounds.min.x,equipment.loaderType.bounds.max.x })
                    foreach (float z in new[] { equipment.loaderType.bounds.min.z,equipment.loaderType.bounds.max.z })
                        Assert.IsTrue(court.Contains(parking.loader.Point(new Vector3(x,0,z))),"shared court contains the loader");
            }
            var exclusions=StandEquipmentLayout.StorageExclusions(airport);
            foreach (var section in court.Rectangles())
                Assert.IsFalse(exclusions.Any(q=>q.Overlaps(StandEquipmentLayout.BayBox(section,.15f))),"court stays clear of aircraft and fixed infrastructure");
        }

        [TestCase(0f)] [TestCase(90f)] [TestCase(180f)] [TestCase(270f)] [TestCase(37f)]
        public void SharedCourt_FollowsStandAndIncludesSpareSpaceForMultipleItems(float heading)
        {
            var (airport,stand,station)=Create(heading);
            AssertSharedCourt(airport,stand);
        }

        [Test]
        public void SavedSandbox_SharedCourtsContainBothFleetsAndRequirePavement()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var airport=Object.FindAnyObjectByType<Airport>();
            var stands=StandEquipmentLayout.All(airport).Select(p=>p.stand).Distinct().ToArray();
            Assert.AreEqual(1,stands.Length);
            foreach (var stand in stands) AssertSharedCourt(airport,stand);
            airport.aprons.Clear();
            foreach (var stand in airport.Taxiways.stands) stand.surface=PavementSurface.Grass;
            Assert.IsFalse(StandMarkings.Equipment(airport).Any(b=>b.storageArea),"shared paint requires paving throughout its area");
        }

        [TestCase(0f)] [TestCase(90f)] [TestCase(180f)] [TestCase(270f)] [TestCase(37f)]
        public void Staging_FollowsStandAndCargoSideInsideItsPad(float heading)
        {
            var (airport,stand,station)=Create(heading);
            var parking=StandEquipmentLayout.For(station);
            Assert.IsTrue(parking.Valid,parking.problem);
            var layout=airport.Taxiways.Layout(stand);
            foreach (var bay in new[] { parking.loaderBay,parking.trainBay })
            {
                Assert.Greater(Vector2.Dot(bay.centre-layout.stop,new Vector2(layout.nose.y,-layout.nose.x)),0);
                Assert.IsTrue(StandEquipmentLayout.Clear(airport,layout,bay,airport.Bridges.TypesFor(stand)));
                foreach (float x in new[] { -bay.width*.5f,bay.width*.5f })
                    foreach (float z in new[] { -bay.length*.5f,bay.length*.5f }) Assert.IsTrue(layout.Contains(bay.Point(x,z)));
            }
            var path=new VehiclePath(); path.Start(parking.train.position,1); path.LineTo(parking.train.position+parking.train.facing,1);
            var train=new BaggageTrain(station.tractorType,station.cartType,path,station.cartCount);
            for (int i=0;i<train.CartCount;i++)
                foreach (float x in new[] { station.cartType.bounds.min.x,station.cartType.bounds.max.x })
                    foreach (float z in new[] { station.cartType.bounds.min.z,station.cartType.bounds.max.z })
                        Assert.IsTrue(parking.trainBay.Contains(train.CartAt(i).Point(new Vector3(x,0,z))));
        }

        [Test]
        public void SharedStand_PacksBulkAndContainerEquipmentWithoutOverlap()
        {
            var (airport,stand,bulk)=Create(0,SizeCode.E);
            bulk.cartCount=1; bulk.automaticHold.cargoPoint="Cargo_Bulk";
            var container=new GameObject("ULD staging").AddComponent<ContainerStation>(); container.transform.SetParent(airport.transform,false);
            container.standIds.Add(stand.id); container.automaticHold.aircraft=Type("Widebody");
            container.tractorType=bulk.tractorType; container.dollyType=VehicleSetup.Type("ContainerDolly");
            container.loaderType=VehicleSetup.Type("ContainerLoader"); container.unitType=VehicleSetup.Type("ContainerLD3");
            var parking=StandEquipmentLayout.All(airport);
            Assert.AreEqual(2,parking.Count); foreach (var p in parking) Assert.IsTrue(p.Valid,p.problem);
            var bays=parking.SelectMany(p=>new[] { p.loaderBay,p.trainBay }).ToArray();
            for (int i=0;i<bays.Length;i++) for (int j=i+1;j<bays.Length;j++)
                Assert.IsFalse(StandEquipmentLayout.BayBox(bays[i],0).Overlaps(StandEquipmentLayout.BayBox(bays[j],0)));
            AssertSharedCourt(airport,stand);
        }

        [Test]
        public void Staging_ReportsInsufficientSpaceWithoutParkingOutsideTheStand()
        {
            var (airport,stand,station)=Create(0);
            station.cartCount=20;
            var parking=StandEquipmentLayout.For(station);
            Assert.IsFalse(parking.Valid); Assert.IsNotEmpty(parking.problem);
            Assert.IsNull(airport.Baggage.Fleet(station).Train);
            Assert.IsFalse(StandMarkings.Equipment(airport).Any(b=>!b.working));
            station.cartCount=2; airport.Baggage.RefreshParking(station);
            Assert.IsNotNull(airport.Baggage.Fleet(station).Train);
            Assert.IsNull(station.Problem,"correcting the layout clears the staging problem");
        }

        [Test]
        public void ChangingHome_RefreshesTheActualFleetAndPaintWithoutChangingHallEndpoints()
        {
            var (airport,stand,station)=Create(0);
            int dispatch=station.dispatchNode, arrivals=station.arrivalsNode;
            var fleet=airport.Baggage.Fleet(station); var old=fleet.Train.Tractor.position;
            var other=new Stand { position=new Vector3(200,0,200),heading=180,code=SizeCode.C,noSmallAircraft=true };
            other.Frame(Vector2.right,0); airport.Taxiways.Add(other); airport.Taxiways.Rebuild();
            station.standIds.Add(other.id); station.parkingStandId=other.id;
            airport.Baggage.RefreshParking(station);
            var parking=StandEquipmentLayout.For(station);
            Assert.IsTrue(parking.Valid,parking.problem);
            Assert.Greater(Vector2.Distance(old,fleet.Train.Tractor.position),100);
            Assert.AreEqual(parking.train.position,fleet.Train.Tractor.position);
            AssertParked(fleet.Train,parking.trainBay);
            Assert.AreEqual(dispatch,station.dispatchNode); Assert.AreEqual(arrivals,station.arrivalsNode);
            Assert.AreEqual(other.Label,StandMarkings.Equipment(airport).Single(b=>b.storageArea).stands);
            var work=StandMarkings.Equipment(airport).Where(b=>b.working).ToArray();
            Assert.AreEqual(2,work.Length,"working areas follow all served stands, parking follows the selected home");
        }

        [TestCase("BaggageTurnaround")] [TestCase("ContainerTurnaround")]
        public void SavedStand_HasContinuousEquipmentRoutesFromAndBackToStaging(string scene)
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/"+scene+".unity");
            var airport=Object.FindAnyObjectByType<Airport>();
            var bulk=Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            var container=Object.FindAnyObjectByType<ContainerTurnaroundDemo>();
            var plane=bulk != null ? bulk.aircraft : container.aircraft;
            var stand=airport.Taxiways.stands.Find(s=>s.id == (bulk != null ? bulk.standId : container.standId));
            BaggageServiceAccess equipment;
            AircraftType.ServicePoint cargo;
            if (bulk != null)
            {
                bulk.station.automaticHold.aircraft=plane.type;
                Assert.IsTrue(bulk.station.automaticHold.Resolve(bulk.station.loaderType,out cargo,out var why),why);
                equipment=new BaggageServiceAccess(bulk.station);
            }
            else
            {
                container.station.automaticHold.aircraft=plane.type;
                container.bulkStation.automaticHold.aircraft=plane.type;
                container.bulkStation.automaticHold.cargoPoint="Cargo_Bulk";
                Assert.IsTrue(container.station.automaticHold.Resolve(container.station.unitType,container.station.dollyType,container.station.loaderType,out cargo,out var why),why);
                equipment=new BaggageServiceAccess(container.station);
            }
            Assert.IsTrue(equipment.parking.Valid,equipment.parking.problem);
            Assert.IsTrue(BaggageServicePlan.Build(airport,plane,stand,cargo,equipment,out var plan,out var reason),reason);
            Assert.IsNotNull(plan.initial); Assert.IsNotNull(plan.park); Assert.IsNotNull(plan.dispatchPark);
            Assert.Less(Vector2.Distance(plan.initial.Sample(0),equipment.parking.train.position),.001f);
            Assert.Less(Vector2.Distance(plan.park.End,equipment.parking.train.position),.001f);
            Assert.Less(Vector2.Distance(plan.dispatchPark.End,equipment.parking.train.position),.001f);
            Assert.AreEqual(airport.Taxiways.At(equipment.dispatchNode),plan.toStand.Sample(0),"dispatch remains separate from stand parking");
            airport.aprons.Clear();
            Assert.IsTrue(plan.Clear(equipment.parking.loader,equipment.loaderType),"the stand's own paved pad supports staged equipment");
            Assert.IsFalse(plan.Clear(new BaggageVehiclePose(new Vector2(10000,10000),Vector2.up),equipment.tractorType),"grass outside paved coverage remains unavailable");
        }

        [Test]
        public void RoadIndex_PreservesClosuresAndTaxiwayExclusionsAcrossCellBoundaries()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo=Object.FindAnyObjectByType<BaggageTurnaroundDemo>(); var airport=demo.airport; var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(120,0,-352)), b=net.AddNode(new Vector3(136,0,-288));
            var road=net.Connect(a,b,8,PavementSurface.Concrete,false,RoadClass.Service);
            Assert.IsNotEmpty(road); net.Rebuild();
            var stand=net.stands.Find(s=>s.id==demo.standId);
            Assert.IsTrue(demo.station.automaticHold.Resolve(demo.station.loaderType,out var cargo,out var why),why);
            Assert.IsTrue(BaggageServicePlan.Build(airport,demo.aircraft,stand,cargo,new BaggageServiceAccess(demo.station),out var plan,out why),why);
            var pose=new BaggageVehiclePose(new Vector2(128,-320),Vector2.up);
            Assert.IsTrue(plan.Clear(pose,demo.station.tractorType));
            foreach (var edge in road) edge.wear=RunwayRules.ClosedWear;
            Assert.IsFalse(plan.Clear(pose,demo.station.tractorType),"closed road remains excluded even over a paved apron");
            foreach (var edge in road) { edge.wear=0; edge.road=RoadClass.None; }
            Assert.IsFalse(plan.Clear(pose,demo.station.tractorType),"a taxiway cannot become a baggage shortcut");
            foreach (var edge in road) edge.road=RoadClass.Service;
            Assert.IsTrue(plan.Clear(pose,demo.station.tractorType));
        }
    }
}
