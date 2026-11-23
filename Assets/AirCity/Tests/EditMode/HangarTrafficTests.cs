using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.Traffic;
using AirCity.Game;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    public sealed class HangarTrafficTests
    {
        readonly List<Object> spawned=new List<Object>();
        float glow;
        AircraftType Type => AircraftSetup.AllTypes().First(t=>t.model=="Cessna172");

        [SetUp] public void Setup() { glow=AircraftLights.Glow; AircraftLights.Glow=0f; }
        [TearDown] public void Cleanup()
        {
            foreach (var item in spawned) if (item!=null) Object.DestroyImmediate(item);
            spawned.Clear(); AircraftLights.Glow=glow;
        }

        (Airport airport,SmallHangar hangar,TaxiwayEdge edge) AirportWithHangar()
        {
            AirfieldSetup.CreateMaterials();
            var go=new GameObject("Hangar traffic test"); go.SetActive(false); spawned.Add(go);
            var airport=go.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport);
            airport.groundSize=0f; go.SetActive(true);
            var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-150f,0f,15f)), b=net.AddNode(new Vector3(150f,0f,15f));
            var edge=net.Connect(a,b,7.5f,PavementSurface.Asphalt).Single();
            var stand=net.AddStand(StandKind.Parking,new Vector3(-65f,0f,15f),0f,SizeCode.A,PavementSurface.Concrete);
            Assert.IsNotNull(stand,"test stand");
            net.Rebuild();
            var hangar=airport.AddHangar(new Vector2(50f,0f),0f);
            Assert.IsNotNull(hangar,"test hangar");
            return (airport,hangar,edge);
        }

        AirCity.Aircraft.Aircraft Place(Airport airport,float x=-110f,float z=15f)
        {
            var prefab=AircraftSetup.Prefab(Type); Assert.IsNotNull(prefab);
            var go=Object.Instantiate(prefab,new Vector3(x,0f,z),Quaternion.Euler(0f,90f,0f)); spawned.Add(go);
            go.transform.SetParent(airport.transform,true);
            go.name="Test resident";
            return go.GetComponent<AirCity.Aircraft.Aircraft>();
        }

        static Vector2 XZ(Vector3 value)=>new Vector2(value.x,value.z);
        static void Finish(Airport airport,Flight flight)
        {
            for (int second=0;second<900 && !flight.Finished;second++) airport.Simulate(1f);
            Assert.IsTrue(flight.Finished,flight.Status);
            Assert.AreEqual(FlightPhase.Parked,flight.Phase);
        }

        [Test] public void GroundTransfer_TaxisIntoBayBacksOutAndReturnsWithoutRunway()
        {
            var (airport,hangar,_)=AirportWithHangar(); var aircraft=Place(airport);
            var preview=airport.Traffic.PreviewTaxiToHangar(aircraft,hangar,out string problem);
            Assert.IsNotNull(preview,problem); Assert.IsFalse(hangar.Occupied,"preview reserves nothing");
            Assert.AreSame(hangar,preview.hangar); Assert.IsNull(preview.stand); Assert.IsNull(preview.runway);
            var inward=airport.Traffic.TaxiToHangar(aircraft,hangar,out problem); Assert.IsNotNull(inward,problem);
            Assert.AreEqual(FlightKind.GroundTransfer,inward.kind); Assert.AreEqual(FlightPhase.Ready,inward.Phase);
            Assert.AreSame(aircraft,hangar.ResidentAircraft,"reserve incoming aircraft immediately");
            Finish(airport,inward);
            Assert.IsTrue(hangar.ResidentInBay);
            Assert.Less(Vector2.Distance(XZ(aircraft.transform.position),hangar.ParkPosition(Type)),.15f);
            Assert.Less(Vector2.Angle(XZ(aircraft.transform.forward),hangar.ParkFacing),1f);
            Assert.GreaterOrEqual(aircraft.transform.position.y,SmallHangar.FloorHeight,"supported by floor");
            var outward=airport.Traffic.TaxiToStand(aircraft,out problem); Assert.IsNotNull(outward,problem);
            Assert.IsNotNull(outward.Route.pushBack,"nose-in aircraft is backed through the doors");
            Assert.AreSame(hangar,outward.Route.leavingHangar);
            bool handPush=false;
            for (int second=0;second<900 && !outward.Finished;second++)
            { airport.Simulate(1f); handPush|=outward.Phase==FlightPhase.HandPush; }
            Assert.IsTrue(handPush,"light aircraft moved by hand, without a tug depot");
            Assert.IsTrue(outward.Finished,outward.Status); Assert.IsFalse(hangar.ResidentInBay);
            Assert.IsNotNull(outward.Route.stand);
            Assert.Less(Vector2.Distance(XZ(aircraft.transform.position),outward.Route.stand.ParkPosition(Type)),.15f);
            var back=airport.Traffic.TaxiToHangar(aircraft,hangar,out problem); Assert.IsNotNull(back,problem);
            Finish(airport,back); Assert.IsTrue(hangar.ResidentInBay);
        }

        [Test] public void GroundTransfer_RejectsRoadClosedDoorsAndForeignReservation()
        {
            var (airport,hangar,edge)=AirportWithHangar(); var aircraft=Place(airport);
            hangar.SetDoorsOpen(false);
            Assert.IsNull(airport.Traffic.TaxiToHangar(aircraft,hangar,out string problem)); StringAssert.Contains("doors",problem);
            hangar.SetDoorsOpen(true); edge.road=RoadClass.Service;
            Assert.IsNull(airport.Traffic.PreviewTaxiToHangar(aircraft,hangar,out problem));
            Assert.IsFalse(hangar.Occupied,"failure creates no lease");
            edge.road=RoadClass.None;
            var inward=airport.Traffic.TaxiToHangar(aircraft,hangar,out problem); Assert.IsNotNull(inward,problem);
            var other=Place(airport,-130f);
            Assert.IsNull(airport.Traffic.TaxiToHangar(other,hangar,out problem)); StringAssert.Contains("reserved",problem);
        }

        [TestCase(HangarSize.Executive, null)]
        [TestCase(HangarSize.Airline, "Widebody")]
        [TestCase(HangarSize.Airline, "DoubleDeck")]
        public void ExpandedBay_TaxisInAndBacksOutWithAircraftClearance(HangarSize size, string model)
        {
            var go = new GameObject("Expanded hangar traffic test");
            go.SetActive(false); spawned.Add(go);
            var airport = go.AddComponent<Airport>();
            AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 0;
            go.SetActive(true);
            var net = airport.Taxiways;
            var spec = HangarSpec.For(size);
            float z = spec.Setback + 12.5f;
            int a = net.AddNode(new Vector3(-350, 0, z)), b = net.AddNode(new Vector3(350, 0, z));
            var edge = net.Connect(a, b, 25f, PavementSurface.Asphalt).Single();
            var root = airport.AddHangar(Vector2.zero, 0f, size);
            Assert.IsNotNull(root);
            var bay = root.Bays.OrderBy(bay => bay.BayNumber).First();
            AircraftType type;
            AirCity.Aircraft.Aircraft aircraft;
            var position = new Vector3(-220f, 0f, z);
            var heading = Quaternion.Euler(0f, 90f, 0f);
            if (model == null)
            {
                type = ScriptableObject.CreateInstance<AircraftType>();
                spawned.Add(type);
                type.category = AircraftCategory.BusinessJet;
                type.bounds = new Bounds(new Vector3(0, 3, 0), new Vector3(22, 6, 24));
                type.steeringPivot = Vector3.forward * 8.5f;
                type.maxSteeringAngle = 62f;
                type.maxTakeoffMass = 12000f;
                aircraft = new GameObject("Private jet clearance fixture").AddComponent<AirCity.Aircraft.Aircraft>();
                aircraft.type = type;
                aircraft.transform.SetPositionAndRotation(position, heading);
            }
            else
            {
                type = AircraftSetup.AllTypes().Single(t => t.model == model);
                var prefab = AircraftSetup.Prefab(type);
                Assert.IsNotNull(prefab);
                aircraft = Object.Instantiate(prefab, position, heading).GetComponent<AirCity.Aircraft.Aircraft>();
            }
            aircraft.transform.SetParent(airport.transform, true);
            var storedNeighbours = root.Bays.Where(spot => spot != bay).ToArray();
            foreach (var spot in storedNeighbours)
            {
                var resident = new GameObject("Stored business jet").AddComponent<AirCity.Aircraft.Aircraft>();
                resident.type = type;
                resident.transform.SetParent(airport.transform, false);
                resident.transform.SetLocalPositionAndRotation(Geo.X0Z(spot.ParkPosition(type), SmallHangar.FloorHeight),
                    Quaternion.LookRotation(Geo.X0Z(spot.ParkFacing)));
                spot.StoreAircraft(resident);
                Assert.IsTrue(spot.ResidentInBay);
            }
            Assert.IsTrue(bay.CanStore(type));
            if (type.SizeCode == 'F')
            {
                edge.width = 23f;
                StringAssert.Contains("code F", bay.AccessFor(type));
                edge.width = 25f;
            }
            Assert.IsNotNull(net.AddStand(StandKind.Parking, new Vector3(225, 0, z), 180f,
                SizeCodes.Of(type.SizeCode), PavementSurface.Concrete));
            var inward = airport.Traffic.TaxiToHangar(aircraft, bay, out var why);
            Assert.IsNotNull(inward, why);
            root.SetDoorsOpen(false);
            Assert.IsTrue(root.DoorsOpen, "the shared door protects an incoming aircraft in any spot");
            Finish(airport, inward);
            Assert.IsTrue(bay.ResidentInBay);
            Assert.IsTrue(storedNeighbours.All(spot => spot.ResidentInBay));
            var outward = airport.Traffic.TaxiToStand(aircraft, out why);
            Assert.IsNotNull(outward, why);
            Assert.AreSame(bay, outward.Route.leavingHangar);
            Finish(airport, outward);
            Assert.IsFalse(bay.ResidentInBay);
            Assert.IsTrue(storedNeighbours.All(spot => spot.ResidentInBay));
        }

        [Test] public void GroundTransfer_DisconnectedDestinationStopsAndRetainsItsBay()
        {
            var (airport,hangar,edge)=AirportWithHangar(); var aircraft=Place(airport);
            var flight=airport.Traffic.TaxiToHangar(aircraft,hangar,out string problem); Assert.IsNotNull(flight,problem);
            var before=aircraft.transform.position; airport.Taxiways.edges.Remove(edge);
            airport.Simulate(20f);
            Assert.AreEqual(FlightPhase.Ready,flight.Phase); Assert.IsNotNull(flight.Waiting);
            Assert.Less(Vector3.Distance(before,aircraft.transform.position),.01f);
            Assert.AreSame(hangar,flight.Route.hangar,"never silently diverts to an ordinary stand");
            airport.Taxiways.edges.Add(edge); Finish(airport,flight); Assert.IsTrue(hangar.ResidentInBay);
        }

        [TestCase("LightJet", SizeCode.A, StandExit.PushBack, 970f, "C-STA6", "ClassicBlue", false)]
        [TestCase("ExecutiveJet", SizeCode.B, StandExit.TaxiOut, 1320f, "C-STB8", "MidnightChampagne", true)]
        [TestCase("FlagshipJet", SizeCode.B, StandExit.TaxiOut, 1590f, "C-STLA", "GraphiteSilver", true)]
        public void BusinessJet_LeasesExecutiveSpotAndTaxisToStandAndBack(
            string model, SizeCode standCode, StandExit standExit, float runwayLength, string registration,
            string defaultLivery, bool hasAirstair)
        {
            var type = AircraftSetup.AllTypes().Single(t => t.model == model);
            Assert.AreEqual(AircraftCategory.BusinessJet, type.category);
            Assert.AreEqual(standCode.Letter(), type.SizeCode);
            Assert.That(type.RunwayLength, Is.EqualTo(runwayLength));
            Assert.IsFalse(type.unpavedRunways);
            Assert.IsFalse(SmallHangar.Fits(type), "Business jets use the executive hangar");

            var go = new GameObject(type.TypeName + " lease and ground transfer");
            go.SetActive(false);
            spawned.Add(go);
            var airport = go.AddComponent<Airport>();
            AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 0f;
            go.SetActive(true);
            var net = airport.Taxiways;
            float width = RunwayRules.TaxiwayWidthFor('B');
            float lane = HangarSpec.For(HangarSize.Executive).Setback + width / 2f;
            int start = net.AddNode(new Vector3(-150f, 0f, lane));
            int end = net.AddNode(new Vector3(150f, 0f, lane));
            net.Connect(start, end, width, PavementSurface.Asphalt);
            var stand = net.AddStand(StandKind.Parking, new Vector3(115f, 0f, lane), 180f,
                standCode, PavementSurface.Concrete, standExit);
            Assert.IsNotNull(stand);
            var bay = airport.AddHangar(Vector2.zero, 0f, HangarSize.Executive);
            Assert.IsNotNull(bay);
            Assert.IsTrue(bay.TryLease(type, "Starling Charter", registration, out var problem), problem);
            var jet = bay.ResidentAircraft;
            Assert.IsNotNull(jet, "The game catalog supplies the real aircraft prefab");
            var paint = jet.GetComponent<AircraftLiveryApplier>();
            Assert.AreEqual(registration, paint.registration);
            Assert.AreEqual(defaultLivery, paint.liveryAsset.name);
            Assert.IsTrue(bay.ResidentInBay);

            Assert.AreEqual(hasAirstair, jet.Airstair != null);
            if (hasAirstair)
            {
                jet.Airstair.SetOpen(true);
                jet.Airstair.Animate(type.airstair.seconds);
                Assert.IsFalse(jet.Airstair.Stowed);
            }
            var outbound = airport.Traffic.TaxiToStand(jet, out problem);
            Assert.IsNotNull(outbound, problem);
            Assert.AreSame(stand, outbound.Route.stand.stand);
            if (hasAirstair)
            {
                var parked = jet.transform.position;
                airport.Simulate(.25f);
                Assert.AreEqual(FlightPhase.Ready, outbound.Phase);
                Assert.That(Vector3.Distance(parked, jet.transform.position), Is.LessThan(.01f));
            }
            Finish(airport, outbound);
            if (hasAirstair) Assert.IsTrue(jet.Airstair.Stowed);
            var inbound = airport.Traffic.TaxiToHangar(jet, bay, out problem);
            Assert.IsNotNull(inbound, problem);
            Finish(airport, inbound);
            Assert.That(Vector2.Distance(XZ(jet.transform.position), bay.ParkPosition(type)), Is.LessThan(.15f),
                "The executive approach must preserve the centered stop.");
            Assert.IsTrue(bay.ResidentInBay);
            Assert.AreSame(jet, bay.ResidentAircraft);
        }


        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void ResidentSave_RestoresTaxiHandPushAndFinalDoorwayApproach(int stage)
        {
            bool pushing=stage==1;
            var (airport,hangar,_)=AirportWithHangar(); var aircraft=Place(airport);
            var paint=aircraft.GetComponent<AircraftLiveryApplier>();
            var livery=GameAssetCatalog.Load().entries.Select(e=>e.asset).OfType<AircraftLiveryAsset>().First(l=>l!=paint.liveryAsset);
            paint.SetLivery(livery,"N901AC");
            var trip=airport.Traffic.TaxiToHangar(aircraft,hangar,out string problem); Assert.IsNotNull(trip,problem);
            if (pushing)
            {
                Finish(airport,trip);
                trip=airport.Traffic.TaxiToStand(aircraft,out problem); Assert.IsNotNull(trip,problem);
                for (int second=0;second<200;second++)
                {
                    airport.Simulate(.5f);
                    if (trip.Phase==FlightPhase.HandPush && aircraft.transform.position.z>3f &&
                        Vector2.Angle(XZ(aircraft.transform.forward),hangar.ParkFacing)>10f) break;
                }
                Assert.AreEqual(FlightPhase.HandPush,trip.Phase);
                Assert.Greater(aircraft.transform.position.z,3f,"save after main gear passes the doorway");
            }
            else if (stage==2)
            {
                for (int second=0;second<900 && aircraft.transform.position.z>-.75f;second++) airport.Simulate(.5f);
                Assert.AreEqual(FlightPhase.Taxi,trip.Phase);
                Assert.IsTrue(hangar.Contains(XZ(aircraft.transform.position)),"save with main gear already inside the doorway");
                Assert.Greater(Vector2.Distance(XZ(aircraft.transform.position),hangar.ParkPosition(Type)),3f);
            }
            else airport.Simulate(15f);
            Assert.IsTrue(hangar.ResidentTravelling); Assert.IsFalse(hangar.CanEndLease); Assert.IsFalse(hangar.CanCloseDoors);
            var position=aircraft.transform.position; var facing=aircraft.transform.forward;
            var catalog=GameAssetCatalog.Load();
            hangar.CaptureResidentState(); var saved=AirportSaveData.Capture(airport,catalog);
            Object.DestroyImmediate(airport.gameObject);
            var restored=saved.Restore(null,catalog); spawned.Add(restored.gameObject);
            var bay=restored.Hangars.Single(); var resident=bay.ResidentAircraft;
            Assert.IsNotNull(resident); Assert.AreEqual(1,restored.GetComponentsInChildren<AirCity.Aircraft.Aircraft>().Length);
            Assert.AreSame(livery,resident.GetComponent<AircraftLiveryApplier>().liveryAsset,"the same aircraft keeps its paint on restore");
            Assert.AreEqual("N901AC",bay.Registration);
            Assert.Less(Vector3.Distance(position,resident.transform.position),.001f,"save retains the physical pose");
            Assert.Less(Vector3.Angle(facing,resident.transform.forward),.001f);
            bay.ResumeResidentTrip(); var resumed=restored.Traffic.FlightOf(resident);
            Assert.IsNotNull(resumed,"restored resident resumes its destination");
            Assert.AreEqual(FlightKind.GroundTransfer,resumed.kind);
            if (pushing) Assert.IsNotNull(resumed.Route.stand); else Assert.AreSame(bay,resumed.Route.hangar);
            Finish(restored,resumed);
            Assert.AreEqual(!pushing,bay.ResidentInBay);
            Assert.IsFalse(bay.ResidentTravelling);
        }

        [Test] public void GroundTransfer_HoldsBehindAnAircraftOnItsTaxiway()
        {
            var (airport,hangar,_)=AirportWithHangar(); var aircraft=Place(airport); var blocker=Place(airport,-45f);
            var flight=airport.Traffic.TaxiToHangar(aircraft,hangar,out string problem); Assert.IsNotNull(flight,problem);
            for (int second=0;second<120;second++) airport.Simulate(1f);
            Assert.IsFalse(flight.Finished); Assert.IsNotNull(flight.Waiting);
            Assert.Less(aircraft.transform.position.x,blocker.transform.position.x-8f,"normal aircraft spacing applies");
            Object.DestroyImmediate(blocker.gameObject); Finish(airport,flight);
            Assert.IsTrue(hangar.ResidentInBay);
        }
    }
}
