using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Airfield
{
    public class StandMarkingsTests
    {
        readonly List<Object> objects = new List<Object>();
        [TearDown] public void Cleanup() { foreach (var o in objects) if (o != null) Object.DestroyImmediate(o); objects.Clear(); }
        static AircraftType Type(string model) => AircraftSetup.AllTypes().Single(t => t.model == model);
        static Stand AddStand(TaxiwayNetwork net, Vector3 position, SizeCode code)
        {
            var stand = new Stand { position = position, code = code };
            stand.Frame(Vector2.right,0); return net.Add(stand);
        }
        Airport Airport()
        {
            var go = new GameObject("Markings test"); go.SetActive(false); objects.Add(go);
            var airport = go.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 0; airport.aircraftTypes = AircraftSetup.AllTypes().ToList();
            airport.aprons.Add(ApronArea.Rect(Vector2.zero,new Vector2(400,400),0,PavementSurface.Concrete));
            go.SetActive(true); return airport;
        }

        [TestCase(0f)] [TestCase(45f)] [TestCase(180f)]
        public void DifferentStops_MatchActualNoseWheelsAndPreserveAuthoredOverrides(float heading)
        {
            var stand = new Stand { code = SizeCode.F, heading = heading, noSmallAircraft = true };
            stand.Frame(new Vector2(stand.Axis.y,-stand.Axis.x),0);
            var layout = StandLayout.Of(stand); var jet = Type("Narrowbody");
            Assert.Greater(layout.StopOffsetFor(jet),1f);
            var park = layout.ParkPosition(jet);
            Assert.Less(Vector2.Distance(park+layout.nose*jet.bounds.max.z,layout.At(0,layout.noseS)),.001f);
            var marks = StandMarkings.Stops(layout,AircraftSetup.AllTypes());
            Assert.Greater(marks.Count,1);
            foreach (var mark in marks)
                foreach (var type in mark.aircraft)
                {
                    Assert.Less(Vector2.Distance(mark.position,layout.ParkPosition(type)+layout.nose*type.Wheelbase),.001f);
                    Assert.IsTrue(layout.ParkingFits(type));
                }
            stand.serviceConfigurations.Add(new StandServiceConfiguration { aircraft = jet, stopOffset = 0 });
            Assert.AreEqual(layout.stop,layout.StopFor(jet),"explicit zero stays authoritative");
            stand.ServiceFor(jet).allowed = false;
            Assert.IsFalse(StandMarkings.Stops(layout,AircraftSetup.AllTypes()).Any(m => m.aircraft.Contains(jet)));
        }

        [Test]
        public void SharedAndCloseStops_GroupOnlyCoincidentTypesAndRejectInvalidStops()
        {
            var stand = new Stand { code = SizeCode.F, noSmallAircraft = true };
            foreach (var type in AircraftSetup.AllTypes()) stand.serviceConfigurations.Add(new StandServiceConfiguration { aircraft = type });
            var layout = StandLayout.Of(stand);
            Assert.AreEqual(1,StandMarkings.Stops(layout,AircraftSetup.AllTypes()).Count);
            stand.ServiceFor(Type("Narrowbody")).stopOffset = .25f;
            Assert.AreEqual(2,StandMarkings.Stops(layout,AircraftSetup.AllTypes()).Count);
            stand.ServiceFor(Type("Narrowbody")).stopOffset = 500;
            Assert.IsFalse(StandMarkings.Stops(layout,AircraftSetup.AllTypes()).Any(m => m.aircraft.Contains(Type("Narrowbody"))));
            stand.surface = PavementSurface.Grass;
            Assert.IsEmpty(StandMarkings.Stops(layout,AircraftSetup.AllTypes()));
            stand.surface = PavementSurface.Concrete; stand.kind = StandKind.DeIcing;
            Assert.IsEmpty(StandMarkings.Stops(layout,AircraftSetup.AllTypes()));
        }

        [Test]
        public void PaintAndParking_UseTheSameConfiguredStops()
        {
            var airport = Airport(); var net = airport.Taxiways;
            var stand = AddStand(net,Vector3.zero,SizeCode.E);
            stand.noSmallAircraft = true;
            stand.serviceConfigurations.Add(new StandServiceConfiguration { aircraft = Type("Narrowbody"),stopOffset = 2.5f });
            net.Rebuild(); var vertices = net.PaintMesh.vertices;
            foreach (var mark in StandMarkings.Stops(net.Layout(stand),airport.aircraftTypes))
            {
                bool found = false;
                for (int i = 0; i+3 < vertices.Length; i++)
                {
                    var c = (vertices[i]+vertices[i+1]+vertices[i+2]+vertices[i+3])*.25f;
                    if (Vector2.Distance(new Vector2(c.x,c.z),mark.position) < .001f &&
                        Mathf.Abs(Vector3.Distance(vertices[i],vertices[i+1])-6f) < .01f) { found = true; break; }
                }
                Assert.IsTrue(found,"missing painted bar at "+mark.Label);
            }
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AirCity/Airfield/Textures/RunwayGlyphs.png");
            Assert.AreEqual(2048,atlas.height,"labels must retain the full alphabet atlas");
        }

        BaggageStation Station(Airport airport)
        {
            var net = airport.Taxiways;
            var stand = AddStand(net,new Vector3(-100,0,-100),SizeCode.C);
            var go = new GameObject("Stand baggage"); go.transform.SetParent(airport.transform,false);
            var station = go.AddComponent<BaggageStation>();
            station.parkAtStand=false;
            station.standIds.Add(stand.id);
            station.tractorType = VehicleSetup.Type("BaggageTractor"); station.cartType = VehicleSetup.Type("BaggageCart");
            station.loaderType = VehicleSetup.Type("BeltLoader"); station.cartCount = 2;
            station.dispatchNode = net.AddNode(new Vector3(20,0,20)); station.loaderParking = new Vector2(45,30);
            station.loaderParkingBearing = 35; station.dispatchBearing = 70;
            return station;
        }

        [Test]
        public void EquipmentBays_ContainEveryParkedVehicleAndGrowWithTheTrain()
        {
            var airport = Airport(); var station = Station(airport);
            var bays = StandMarkings.Equipment(airport); Assert.AreEqual(2,bays.Count);
            var bay = bays.Single(b => b.equipment == "BAG TRAIN");
            var path = new VehiclePath(); var facing = BaggageServicePlan.Facing(station.dispatchBearing);
            var at = airport.Taxiways.At(station.dispatchNode); path.Start(at,1); path.LineTo(at+facing,1);
            var train = new BaggageTrain(station.tractorType,station.cartType,path,station.cartCount);
            void Fits(StandMarkings.EquipmentBay box,BaggageVehiclePose pose,VehicleType type)
            {
                foreach (float x in new[] { type.bounds.min.x,type.bounds.max.x })
                    foreach (float z in new[] { type.bounds.min.z,type.bounds.max.z }) Assert.IsTrue(box.Contains(pose.Point(new Vector3(x,0,z))));
            }
            Fits(bay,train.Tractor,station.tractorType);
            for (int i=0;i<train.CartCount;i++) Fits(bay,train.CartAt(i),station.cartType);
            Fits(bays.Single(b=>b.equipment == "BELT"),new BaggageVehiclePose(station.loaderParking,
                BaggageServicePlan.Facing(station.loaderParkingBearing)),station.loaderType);
            station.cartCount = 3;
            Assert.Greater(StandMarkings.Equipment(airport).Single(b=>b.equipment == "BAG TRAIN").length,bay.length+2f);
        }

        [Test]
        public void RoadPaint_ClipsOverlappingRotatedBaysAndRestoresTheOriginalLine()
        {
            var f = new Vector2(.6f,.8f);
            var bay = new StandMarkings.EquipmentBay { centre = new Vector2(10,20), facing = f, width = 4, length = 8 };
            var a = bay.centre-f*12; var b = bay.centre+f*12;
            var pieces = StandMarkings.RoadSegments(a,b,new[] { bay },.25f);
            Assert.AreEqual(2,pieces.Count);
            Assert.AreEqual(15.5f,pieces.Sum(p=>Vector2.Distance(p.a,p.b)),.001f);
            foreach (var piece in pieces)
                for (int i=0;i<=20;i++) Assert.IsFalse(bay.Contains(Vector2.Lerp(piece.a,piece.b,i/20f)));
            Assert.AreEqual(2,StandMarkings.RoadSegments(b,a,new[] { bay,bay },.25f).Count,"overlapping boxes form one gap");
            var restored = StandMarkings.RoadSegments(a,b,new StandMarkings.EquipmentBay[0],.25f);
            Assert.AreEqual(1,restored.Count); Assert.AreEqual(a,restored[0].a); Assert.AreEqual(b,restored[0].b);
        }

        [TestCase(-1f)] [TestCase(1f)]
        public void SharedCourt_LeavesItsAircraftClearanceCornerUnpainted(float side)
        {
            var court=new StandMarkings.EquipmentBay { centre=new Vector2(10,20),facing=new Vector2(.6f,.8f),
                width=12,length=14,notchWidth=4,notchLength=5,notchSide=side,storageArea=true };
            var a=court.Point(side*5,-4); var b=court.Point(side*3,-4);
            Assert.IsFalse(court.Contains(a)); Assert.IsFalse(court.Contains(b));
            var visible=StandMarkings.RoadSegments(a,b,new[] { court },.1f);
            Assert.AreEqual(1,visible.Count,"aircraft clearance corner retains its paint");
            Assert.AreEqual(a,visible[0].a); Assert.AreEqual(b,visible[0].b);
            a=court.Point(side*5,2); b=court.Point(side*3,2);
            Assert.IsTrue(court.Contains(a)); Assert.IsTrue(court.Contains(b));
            Assert.IsEmpty(StandMarkings.RoadSegments(a,b,new[] { court },.1f),"shared storage clips crossing paint");
            Assert.AreEqual(court.Area,court.Rectangles().Sum(r=>r.Area),.001f);
            foreach (var p in court.Boundary(.25f)) Assert.IsTrue(court.Contains(p),"inset border stays inside the L-shaped court");
        }

        [Test]
        public void EquipmentPaint_UpdatesWithoutInvalidatingOperationsAndDisappearsOffPavement()
        {
            var airport = Airport(); var station = Station(airport); var net = airport.Taxiways;
            net.Rebuild(); int version = airport.LayoutVersion;
            var before = net.EquipmentPaintMesh.vertices;
            var pavement = net.SurfaceMesh.vertices;
            station.loaderParking += Vector2.right*12;
            airport.Baggage.RefreshParking(station);
            Assert.AreEqual(version,airport.LayoutVersion);
            CollectionAssert.AreEqual(pavement,net.SurfaceMesh.vertices);
            Assert.IsFalse(before.SequenceEqual(net.EquipmentPaintMesh.vertices));
            station.enabled = false; net.RefreshEquipmentPaint(); Assert.AreEqual(0,net.EquipmentPaintMesh.vertexCount);
            station.enabled = true; airport.aprons.Clear(); net.RefreshEquipmentPaint();
            Assert.IsEmpty(StandMarkings.Equipment(airport)); Assert.AreEqual(0,net.EquipmentPaintMesh.vertexCount);
        }
    }
}
