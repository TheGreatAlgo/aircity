using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Game;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public sealed class SmallHangarTests
    {
        readonly List<Object> spawned=new List<Object>();
        [TearDown] public void Cleanup() { foreach(var o in spawned) if(o!=null) Object.DestroyImmediate(o); spawned.Clear(); }

        Airport Airport(bool connected=true)
        {
            var go=new GameObject("Hangar test airport"); go.SetActive(false); spawned.Add(go);
            var airport=go.AddComponent<Airport>(); airport.groundSize=0f; go.SetActive(true);
            if(connected)
            {
                var net=airport.Taxiways;
                int a=net.AddNode(new Vector3(-40,0,8)), b=net.AddNode(new Vector3(40,0,8));
                net.Connect(a,b,7.5f,PavementSurface.Asphalt);
            }
            return airport;
        }

        AircraftType Light()
        {
            var type=ScriptableObject.CreateInstance<AircraftType>(); spawned.Add(type);
            type.model="HangarTestLight"; type.seats=4; type.category=AircraftCategory.GeneralAviation;
            type.bounds=new Bounds(new Vector3(0,1.4f,1),new Vector3(11,2.8f,8)); type.unpavedRunways=true;
            return type;
        }

        SmallHangar Lease(Airport airport,AircraftType type=null)
        {
            var hangar=airport.AddHangar(Vector2.zero,0);
            Assert.NotNull(hangar);
            Assert.True(hangar.TryLease(type!=null?type:Light(),"Valley Flying","N100AC",out var why),why);
            return hangar;
        }

        [Test]
        public void OnlyCompatibleAircraftCanLeaseAnAccessibleVacantBay()
        {
            var airport=Airport(); var hangar=airport.AddHangar(Vector2.zero,0); var type=Light();
            type.bounds=new Bounds(Vector3.up*2,new Vector3(16,4,10));
            Assert.False(hangar.TryLease(type,"Valley Flying","N100AC",out _)); Assert.False(hangar.Occupied);
            type.bounds=new Bounds(Vector3.up*1.4f,new Vector3(11,2.8f,8));
            Assert.True(hangar.TryLease(type,"  Valley Flying  ","n100ac",out var why),why);
            Assert.AreEqual("Valley Flying",hangar.TenantName); Assert.AreEqual("N100AC",hangar.Registration);
            Assert.False(hangar.TryLease(type,"Another owner","N200AC",out _));
            airport.RemoveHangar(hangar); Assert.True(airport.Hangars.Contains(hangar),"A leased building cannot be demolished.");
            hangar.EndLease(); airport.RemoveHangar(hangar); Assert.IsEmpty(airport.Hangars);
        }

        [Test]
        public void LeaseNeedsAFrontTaxiwayAndDoesNotAcceptAServiceRoadOrBackAccess()
        {
            var airport=Airport(false); var hangar=airport.AddHangar(Vector2.zero,0); var type=Light();
            Assert.False(hangar.TryLease(type,"Owner","N100AC",out _));
            var net=airport.Taxiways; int a=net.AddNode(new Vector3(-40,0,8)),b=net.AddNode(new Vector3(40,0,8));
            net.Connect(a,b,8,PavementSurface.Asphalt,road:RoadClass.Service);
            Assert.False(hangar.TryLease(type,"Owner","N100AC",out _));
            net.edges[0].road=RoadClass.None;
            Assert.True(hangar.TryLease(type,"Owner","N100AC",out var why),why);
            hangar.EndLease(); hangar.transform.localRotation=Quaternion.Euler(0,180,0);
            Assert.False(hangar.TryLease(type,"Owner","N100AC",out _));
        }

        [Test]
        public void DuplicateResidentRegistrationsCannotLeaseTwoBays()
        {
            var airport=Airport(); var type=Light(); Lease(airport,type);
            var other=airport.AddHangar(new Vector2(22,0),0);
            Assert.NotNull(other); Assert.False(other.TryLease(type,"Second owner","n100ac",out _));
            Assert.True(other.TryLease(type,"Second owner","N200AC",out var why),why);
        }

        [Test]
        public void LiveRentUsesCalendarTimeAndStopsWhileVacantDisconnectedOrInSandbox()
        {
            var airport=Airport(); var hangar=Lease(airport); var state=new CareerState { funds=1000 };
            Assert.AreEqual(300,HangarAccounts.Advance(airport,state,SmallHangar.RentPeriodSeconds/2));
            Assert.AreEqual(1300,state.funds); Assert.AreEqual(300,hangar.RentEarned);
            state.mode=GameMode.Sandbox;
            Assert.Zero(HangarAccounts.Advance(airport,state,SmallHangar.RentPeriodSeconds)); Assert.AreEqual(1300,state.funds);
            state.mode=GameMode.Career; airport.Taxiways.edges.Clear();
            Assert.Zero(HangarAccounts.Advance(airport,state,SmallHangar.RentPeriodSeconds)); Assert.AreEqual(1300,state.funds);
            hangar.EndLease();
            Assert.Zero(HangarAccounts.Advance(airport,state,SmallHangar.RentPeriodSeconds));
        }

        [Test]
        public void RentRemainderAndLeaseSurvivePortableSaveWithoutHistoricalBackpay()
        {
            var airport=Airport(); var type=Light(); var hangar=Lease(airport,type);
            var catalog=ScriptableObject.CreateInstance<GameAssetCatalog>(); spawned.Add(catalog);
            catalog.entries.Add(new GameAssetCatalog.Entry { key="asset:test-light:1",asset=type });
            var state=new CareerState { funds=1000 };
            Assert.Zero(HangarAccounts.Advance(airport,state,17.5f)); double remainder=hangar.RentRemainder;
            hangar.SetDoorsOpen(false);
            var saved=AirportSaveData.Capture(airport,catalog);
            var detached=JsonUtility.FromJson<AirportSaveData>(JsonUtility.ToJson(saved));
            Assert.True(detached.Validate(out var why),why);
            var restored=detached.Restore(null,catalog); spawned.Add(restored.gameObject);
            var loaded=restored.Hangars.Single();
            Assert.True(loaded.Occupied); Assert.AreSame(type,loaded.ResidentType); Assert.AreEqual("Valley Flying",loaded.TenantName);
            Assert.AreEqual("N100AC",loaded.Registration); Assert.False(loaded.DoorsOpen); Assert.AreEqual(remainder,loaded.RentRemainder,1e-10);
            restored.Simulate(60f);
            Assert.AreEqual(remainder,loaded.RentRemainder,1e-10,"Historical reconstruction must not accrue rent.");
            Assert.AreEqual(SmallHangar.MonthlyRent,HangarAccounts.Advance(restored,state,SmallHangar.RentPeriodSeconds-17.5f));
            Assert.AreEqual(1600,state.funds); Assert.AreEqual(SmallHangar.MonthlyRent,loaded.RentEarned);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExecutiveSavePreservesIndividualLeasesAndOneSharedDoor(bool legacy)
        {
            var airport = Airport(false);
            var executive = airport.AddHangar(Vector2.zero, 0f, HangarSize.Executive);
            var spec = executive.Spec;
            var net = airport.Taxiways;
            float z = spec.Setback + 7.5f;
            int a = net.AddNode(new Vector3(-95, 0, z)), b = net.AddNode(new Vector3(95, 0, z));
            net.Connect(a, b, 15f, PavementSurface.Asphalt);
            var type = Light();
            type.category = AircraftCategory.BusinessJet;
            type.bounds = new Bounds(new Vector3(0, 3, 1), new Vector3(22, 6, 24));
            var catalog = ScriptableObject.CreateInstance<GameAssetCatalog>(); spawned.Add(catalog);
            catalog.entries.Add(new GameAssetCatalog.Entry { key = "asset:test-business-jet:1", asset = type });
            var bays = executive.Bays.OrderBy(bay => bay.BayNumber).ToArray();
            for (int i = 0; i < bays.Length; i++)
                Assert.IsTrue(bays[i].TryLease(type, "Executive tenant " + i, "JET-00" + i, out var why), why);
            bays[0].SetDoorsOpen(false);
            Assert.IsTrue(bays.All(bay => !bay.DoorsOpen));
            Assert.AreEqual(3 * spec.Rent, HangarAccounts.Advance(airport, new CareerState(), SmallHangar.RentPeriodSeconds));
            airport.RemoveHangar(bays[2]);
            Assert.AreEqual(3, airport.Hangars.Count());
            var saved = AirportSaveData.Capture(airport, catalog);
            if (legacy)
                foreach (var part in saved.parts)
                    part.json = part.json.Replace("\"extendedExecutive\":true,", "");
            var restored = saved.Restore(null, catalog);
            spawned.Add(restored.gameObject);
            var loaded = restored.Hangars.OrderBy(bay => bay.BayNumber).ToArray();
            Assert.AreEqual(3, loaded.Length);
            for (int i = 0; i < loaded.Length; i++)
            {
                Assert.AreSame(loaded[1], loaded[i].BuildingRoot);
                Assert.AreEqual(HangarSize.Executive, loaded[i].Size);
                Assert.AreEqual(bays[i].Registration, loaded[i].Registration);
                Assert.AreEqual(bays[i].DoorsOpen, loaded[i].DoorsOpen);
                Assert.AreEqual(spec.Rent, loaded[i].RentEarned);
                Assert.IsNull(loaded[i].AccessProblem);
                Assert.AreEqual(legacy ? 32f : 42f, loaded[i].Spec.Depth);
            }
        }

        [Test]
        public void RotatedFootprintIncludesOverhangAndRejectsBuildingOverlap()
        {
            var airport=Airport(false); var first=airport.AddHangar(new Vector2(40,50),90);
            Assert.NotNull(first); Assert.True(first.Contains(new Vector2(40.2f,50)));
            Assert.IsNull(airport.AddHangar(new Vector2(42,50),90));
            Assert.IsNotNull(airport.TerminalProblem(Terminal.Polygon(new[] { new Vector2(30,40),new Vector2(45,40),new Vector2(45,60),new Vector2(30,60) },1)));
            Assert.IsNotNull(airport.HangarProblem(new Vector2(float.NaN,0),0));
        }
    }
}
