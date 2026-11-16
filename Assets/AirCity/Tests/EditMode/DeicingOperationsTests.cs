using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Vehicles
{
    public sealed class DeicingOperationsTests
    {
        readonly List<Object> spawned = new List<Object>();
        [TearDown] public void Cleanup() { foreach (var item in spawned) if (item != null) Object.DestroyImmediate(item); spawned.Clear(); }
        (Airport airport, DeicingFacility facility, DeicingWorkArea area, AirCity.Aircraft.Aircraft plane) Create(int trucks = 2, bool aircraft = true)
        {
            var go = new GameObject("Deicing operations test"); go.SetActive(false);
            var airport = go.AddComponent<Airport>(); airport.groundSize = 0f; go.SetActive(true); spawned.Add(go);
            airport.aprons.Add(ApronArea.Rect(new Vector2(0,-20),new Vector2(300,300),0,PavementSurface.Concrete));
            var type = AircraftSetup.AllTypes().First(t => t.model == "Narrowbody");
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(0,0,-200)), b = net.AddNode(new Vector3(0,0,200));
            net.Connect(a,b,RunwayRules.TaxiwayWidthFor(type.SizeCode),PavementSurface.Concrete);
            var stand = net.AddStand(StandKind.DeIcing,Vector3.zero,0,SizeCode.C,PavementSurface.Concrete);
            Assert.That(stand,Is.Not.Null);
            var facility = airport.Deicing.Configure(stand);
            var area = airport.Deicing.AddWorkArea(new Vector2(-90,-80),0);
            Assert.That(area,Is.Not.Null); Assert.That(airport.Deicing.AssignWorkArea(facility,area),Is.True);
            facility.machines=trucks; // Legacy fleet; purchase and physical arrival are covered separately.
            foreach (var truck in airport.Deicing.TrucksFor(facility)) truck.onboardLitres = truck.CapacityLitres;
            AirCity.Aircraft.Aircraft plane = null;
            if (aircraft)
            {
                var aircraftObject = new GameObject("Recovery test aircraft"); spawned.Add(aircraftObject);
                var position = net.Layout(stand).ParkPosition(type);
                aircraftObject.transform.position = new Vector3(position.x,0,position.y);
                plane = aircraftObject.AddComponent<AirCity.Aircraft.Aircraft>(); plane.type = type;
            }
            return (airport,facility,area,plane);
        }
        static void Run(Airport airport, Func<bool> done, float limit = 300f)
        { for (int step = 0; step < limit * 10f && !done(); step++) airport.Deicing.Simulate(.1f); }

        [Test] public void UpgradePurchase_IsAtomicAndNeverGrantsFluid()
        {
            var (airport,_,area,_) = Create(0,false); var account = airport.Deicing.Accounts;
            account.limitedBudget = true; account.funds = AirportDeicing.RecyclingPrice - 1d;
            double spent = account.TotalSpent;
            Assert.That(airport.Deicing.BuyRecycling(area,out var problem),Is.False); StringAssert.Contains("funds",problem);
            Assert.That(area.recyclingInstalled,Is.False); Assert.That(account.TotalSpent,Is.EqualTo(spent));
            Assert.That(account.AddFunding(1d),Is.True);
            Assert.That(airport.Deicing.BuyRecycling(area,out problem),Is.True,problem);
            Assert.That(account.funds,Is.Zero.Within(.0001)); Assert.That(area.recyclingInstalled,Is.True);
            Assert.That(airport.Deicing.BuyRecycling(area,out _),Is.False);
            airport.Deicing.Simulate(120f);
            Assert.That(area.fluidLitres,Is.Zero); Assert.That(area.runoffLitres,Is.Zero); Assert.That(area.recycledLitres,Is.Zero);
            Assert.That(account.TotalSpent,Is.EqualTo(spent + AirportDeicing.RecyclingPrice));
        }
        [Test] public void FullWingTreatment_RecyclesActualRunoffAndReducesFreshFluidLoss()
        {
            var (airport,facility,area,_) = Create();
            Assert.That(airport.Deicing.BuyRecycling(area,out var problem),Is.True,problem);
            airport.Deicing.Simulate(.1f); var job = airport.Deicing.JobFor(facility);
            Assert.That(job,Is.Not.Null);
            Run(airport,()=>job.Completed);
            Assert.That(job.Completed,Is.True,job.Waiting); Assert.That(job.Progress,Is.EqualTo(1f).Within(.001f));
            Assert.That(job.UsedLitres,Is.EqualTo(job.requiredLitres).Within(.1f));
            airport.Deicing.Simulate(60f);
            float onboard = airport.Deicing.TrucksFor(facility).Sum(t=>t.FluidLitres);
            Assert.That(area.recycledLitres,Is.EqualTo(job.UsedLitres * AirportDeicing.RecoveryFraction).Within(.1d));
            Assert.That(area.runoffLitres,Is.Zero.Within(.001f));
            Assert.That(onboard + area.fluidLitres,Is.EqualTo(2400f - job.UsedLitres * (1f-AirportDeicing.RecoveryFraction)).Within(.2f));
            Assert.That(airport.Deicing.Accounts.recoveredLitres,Is.EqualTo(area.recycledLitres).Within(.001d));
            Assert.That(airport.Deicing.Accounts.appliedLitres,Is.EqualTo(job.UsedLitres).Within(.1d));
        }
        [Test] public void Recovery_PausesAndKeepsSupplierStorageReservation()
        {
            var (airport,_,area,_) = Create();
            Assert.That(airport.Deicing.BuyRecycling(area,out var problem),Is.True,problem);
            Run(airport,()=>area.runoffLitres>0f);
            Assert.That(area.runoffLitres,Is.GreaterThan(0f));
            float pending=area.runoffLitres, delay=area.recyclingDelayRemaining;
            airport.Deicing.Simulate(0f);
            Assert.That(area.runoffLitres,Is.EqualTo(pending)); Assert.That(area.recyclingDelayRemaining,Is.EqualTo(delay));
            // A paid supplier load owns the final 100 litres of capacity; recovered fluid must wait.
            area.fluidLitres=area.capacityLitres-100f; area.deliveryLitres=100f; area.deliverySecondsRemaining=AirportDeicing.DeliveryLeadSeconds;
            airport.Deicing.Simulate(40f);
            Assert.That(area.fluidLitres,Is.EqualTo(area.capacityLitres-100f).Within(.001f));
            Assert.That(area.runoffLitres,Is.GreaterThan(0f)); Assert.That(area.recycledLitres,Is.Zero);
            area.fluidLitres-=12f; airport.Deicing.Simulate(.1f);
            Assert.That(area.fluidLitres,Is.GreaterThan(area.capacityLitres-112f));
            Assert.That(area.fluidLitres+area.deliveryLitres,Is.LessThanOrEqualTo(area.capacityLitres+.001f));
        }
        [Test] public void Payroll_UsesGameTimeAndStopsWhenCrewEndsItsShift()
        {
            var (airport,facility,_,_) = Create(2,false); var account=airport.Deicing.Accounts;
            double before=account.funds;
            airport.Deicing.Simulate(60f);
            Assert.That(account.payrollSpent,Is.EqualTo(2d*AirportDeicing.OperatorHourlyWage/60d).Within(.0001d));
            Assert.That(account.runningSpent,Is.EqualTo((2d*AirportDeicing.TruckHourlyUpkeep+AirportDeicing.PadHourlyPower)/60d).Within(.0001d));
            Assert.That(before-account.funds,Is.EqualTo(airport.Deicing.StandingHourlyCost(facility)/60d).Within(.0001d));
            double wages=account.payrollSpent, running=account.runningSpent;
            airport.Deicing.Simulate(0f); Assert.That(account.payrollSpent,Is.EqualTo(wages));
            Assert.That(airport.Deicing.SetCrewOnDuty(facility,false,out var problem),Is.True,problem);
            airport.Deicing.Simulate(60f);
            Assert.That(account.payrollSpent,Is.EqualTo(wages)); Assert.That(account.runningSpent,Is.EqualTo(running));
        }
        [Test] public void OffShiftPad_WaitsThenTreatsAfterCrewReturns()
        {
            var (airport,facility,_,_) = Create();
            Assert.That(airport.Deicing.SetCrewOnDuty(facility,false,out var problem),Is.True,problem);
            var poses=airport.Deicing.TrucksFor(facility).Select(t=>t.Position).ToArray();
            airport.Deicing.Simulate(30f);
            Assert.That(airport.Deicing.JobFor(facility),Is.Null);
            Assert.That(airport.Deicing.Accounts.appliedLitres,Is.Zero);
            CollectionAssert.AreEqual(poses,airport.Deicing.TrucksFor(facility).Select(t=>t.Position).ToArray());
            Assert.That(airport.Deicing.SetCrewOnDuty(facility,true,out problem),Is.True,problem);
            Run(airport,()=>airport.Deicing.JobFor(facility)?.IsSpraying==true);
            Assert.That(airport.Deicing.JobFor(facility)?.IsSpraying,Is.True);
            Assert.That(airport.Deicing.SetCrewOnDuty(facility,false,out _),Is.False,"Active treatment keeps its crew until the trucks return.");
        }
        [Serializable] sealed class SavedOperations { public DeicingAccounts account; public DeicingWorkArea area; public DeicingFacility facility; }
        [Test] public void SerializedOperations_RetainUpgradeQueueCostsAndCrewShift()
        {
            var saved=new SavedOperations { account=new DeicingAccounts { limitedBudget=true, funds=1234.56, payrollSpent=4.25 },
                area=new DeicingWorkArea { recyclingInstalled=true, runoffLitres=222f, recyclingDelayRemaining=12f, recycledLitres=345.5 },
                facility=new DeicingFacility { staffingVersion=1, crewOnDuty=false, machines=2 } };
            var loaded=JsonUtility.FromJson<SavedOperations>(JsonUtility.ToJson(saved));
            Assert.That(loaded.account.funds,Is.EqualTo(1234.56).Within(.00001d)); Assert.That(loaded.account.payrollSpent,Is.EqualTo(4.25));
            Assert.That(loaded.area.recyclingInstalled,Is.True); Assert.That(loaded.area.runoffLitres,Is.EqualTo(222f));
            Assert.That(loaded.area.recyclingDelayRemaining,Is.EqualTo(12f)); Assert.That(loaded.area.recycledLitres,Is.EqualTo(345.5));
            Assert.That(loaded.facility.crewOnDuty,Is.False); Assert.That(loaded.facility.staffingVersion,Is.EqualTo(1));
        }
    }
}
