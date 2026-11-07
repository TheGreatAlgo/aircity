using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using AirCity.Characters;
using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Traffic;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public sealed class TrainPackingMarshallingTests
    {
        [UnityTearDown] public IEnumerator Close()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
        BaggageTurnaroundDemo Load()
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.station.bagPropType = VehicleSetup.Type("BaggageProps");
            demo.startWithArrival = false; demo.autoDeparture = false; demo.station.automaticHandling = false; return demo;
        }
        static VehiclePath Path()
        {
            var path = new VehiclePath(); path.Start(Vector2.zero,3); path.LineTo(new Vector2(0,40),3);
            path.TurnTo(Vector2.up,new Vector2(25,65),Vector2.right,8,3); path.LineTo(new Vector2(70,65),3); return path;
        }
        [TestCase(2)] [TestCase(3)]
        public void EveryCart_RollsThroughCornersWithConnectedHitches(int count)
        {
            var train = new BaggageTrain(VehicleSetup.Type("BaggageTractor"),VehicleSetup.Type("BaggageCart"),Path(),count);
            var steering = new float[count];
            while (train.Distance < train.Path.Length-.001f)
            {
                Assert.That(train.Advance(.25f,null,out var why),Is.True,why);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(Vector2.Distance(train.FrontHitchAt(i),train.RearHitchAt(i-1)),Is.LessThan(.0001f));
                    steering[i] = Mathf.Max(steering[i],Mathf.Abs(train.SteeringAt(i)));
                }
            }
            foreach (float angle in steering) Assert.That(angle,Is.InRange(5f,BaggageTrain.SteeringLimit));
        }
        [Test]
        public void BlockedRearCart_DoesNotAdvanceAnyLink()
        {
            var train = new BaggageTrain(VehicleSetup.Type("BaggageTractor"),VehicleSetup.Type("BaggageCart"),Path(),3);
            var positions = new Vector2[3]; for (int i = 0; i < 3; i++) positions[i] = train.CartAt(i).position;
            int carts = 0;
            Assert.That(train.Advance(1,(p,t)=>t.kind != VehicleKind.BaggageCart || ++carts < 3,out _),Is.False);
            Assert.That(train.Distance,Is.Zero);
            for (int i = 0; i < 3; i++) Assert.That(train.CartAt(i).position,Is.EqualTo(positions[i]));
        }
        static void Until(BaggageTurnaroundDemo demo, BaggageTurnaround job,Func<bool> ready)
        {
            for (int i = 0; i < 16000 && !ready(); i++) demo.airport.Simulate(.5f);
            Assert.That(ready(),Is.True,$"{job.State}: {job.Waiting}; cart {job.Fleet.ActiveCart+1}, load {job.Fleet.CartCount}");
        }
        BaggageTurnaround Job(BaggageTurnaroundDemo demo)
        {
            demo.station.cartCount = 2;
            var stand = demo.airport.Taxiways.stands.Find(s=>s.id == demo.standId);
            var job = demo.airport.Baggage.Request(demo.aircraft,stand,new BulkHoldConfiguration
                { aircraft = demo.aircraft.type,bagCapacity = 250,weightCapacity = 5000 },demo.station,"TWO-IN","TWO-OUT",out var why);
            Assert.That(job,Is.Not.Null,why);
            var ledger = demo.airport.Baggage.Ledger;
            for (int i = 0; i < 12; i++) ledger.Add("IN-"+i,"TWO-IN",10,150,true,job.Hold);
            for (int i = 0; i < 15; i++) ledger.Add("OUT-"+i,"TWO-OUT",10,150,true,job.Fleet.Dispatch);
            Assert.That(job.Seal(out why),Is.True,why); return job;
        }
        [Test]
        public void TwoCarts_CarryIndependentLoadsAndCompleteWithFewerTrips()
        {
            var demo = Load(); var job = Job(demo);
            Until(demo,job,()=>job.State == BaggageTurnaroundState.OutboundDrive);
            Assert.That(job.Fleet.Carts[0].Count,Is.EqualTo(100)); Assert.That(job.Fleet.Carts[1].Count,Is.EqualTo(50));
            Assert.That(job.ArrivalTrips,Is.EqualTo(1)); Assert.That(job.DepartureTrips,Is.EqualTo(1));
            Until(demo,job,()=>job.DepartureReady);
            Assert.That(job.Hold.Count,Is.EqualTo(150)); Assert.That(job.Fleet.Arrivals.Count,Is.EqualTo(120));
            Assert.That(job.Fleet.CartCount,Is.Zero); Assert.That(demo.airport.Baggage.Ledger.Count,Is.EqualTo(270));
            Assert.That(job.Fleet.Crew.GroundSeated && job.Fleet.Crew.HoldSeated,Is.True);
        }
        [Test]
        public void CancellationAtSecondCart_ReturnsEveryCartAndRetriesTheSamePacking()
        {
            var demo = Load(); var job = Job(demo);
            Until(demo,job,()=>job.State == BaggageTurnaroundState.LoadingHold && job.Fleet.ActiveCart == 1 && job.TransferSeconds > 1);
            job.Cancel(); Until(demo,job,()=>job.Finished);
            Assert.That(job.Fleet.CartCount,Is.Zero); Assert.That(job.Hold.Count+job.Fleet.Dispatch.Count,Is.EqualTo(150));
            Assert.That(job.Retry(out var why),Is.True,why); Until(demo,job,()=>job.DepartureReady);
            Assert.That(job.Hold.Count,Is.EqualTo(150)); Assert.That(demo.airport.Baggage.Ledger.Count,Is.EqualTo(270));
        }
        [Test]
        public void Packing_UsesTheGeneratedBayAndKeepsRowsLayersAndAisleSeparate()
        {
            var demo = Load(); var job = Job(demo); var packing = job.Packing;
            Assert.That(packing.SideMax-packing.SideMin,Is.GreaterThan(5)); Assert.That(packing.SlotCount,Is.GreaterThanOrEqualTo(60));
            for (int i = 0; i < packing.SlotCount; i++)
            {
                var p = packing.LocalSlot(i);
                Assert.That(p.x,Is.InRange(packing.SideMin+.24f,packing.SideMax-.24f));
                Assert.That(p.z,Is.InRange(.40f,packing.Depth-.34f));
                Assert.That(p.y,Is.InRange(0,.94f));
                Assert.That(p.x <= -.20f || p.x >= .85f,Is.True,"The worker's aisle must remain empty.");
                for (int j = 0; j < i; j++)
                {
                    var d = p-packing.LocalSlot(j);
                    Assert.That(Mathf.Abs(d.x) >= .499f || Mathf.Abs(d.y) >= .309f || Mathf.Abs(d.z) >= .799f,Is.True,"Slots overlap");
                }
            }
            var occupied = new HashSet<int>();
            foreach (var allocation in packing.Allocations)
                foreach (int slot in allocation.Value) if (slot >= 0) Assert.That(occupied.Add(slot),Is.True);
        }
        [Test]
        public void Marshaller_GuidesTheActualArrivalStopsAndWalksClearInGameTime()
        {
            var demo = Load(); demo.startWithArrival = true; demo.Initialize();
            var flight = demo.airport.Traffic.FlightOf(demo.aircraft); Assert.That(flight,Is.Not.Null,demo.Problem);
            var signals = new HashSet<MarshallingSignal>(); StandMarshaller worker = null;
            for (int i = 0; i < 16000 && !flight.Finished; i++)
            {
                demo.airport.Simulate(.25f); var current = demo.airport.Marshalling.For(flight);
                if (current == null) continue; worker = current; signals.Add(worker.Signal);
                float elapsed = worker.Elapsed; var position = worker.Position; demo.airport.Simulate(0);
                Assert.That(worker.Elapsed,Is.EqualTo(elapsed)); Assert.That(worker.Position,Is.EqualTo(position));
                if (worker.Ready && worker.Remaining < 25) Assert.That(flight.Speed,Is.LessThanOrEqualTo(1.51f));
            }
            Assert.That(flight.Phase,Is.EqualTo(FlightPhase.Parked)); Assert.That(worker,Is.Not.Null);
            Assert.That(signals.Contains(MarshallingSignal.Ahead),Is.True); Assert.That(signals.Contains(MarshallingSignal.Slow),Is.True);
            demo.airport.Simulate(.25f); Assert.That(worker.Signal,Is.EqualTo(MarshallingSignal.Stop));
            demo.airport.Simulate(20); Assert.That(worker.Phase,Is.EqualTo(MarshallerPhase.Complete));
            Assert.That(demo.airport.Marshalling.Crew.Count,Is.Zero);
        }

        [UnityTest]
        public IEnumerator Marshaller_SpawnsAndAnimatesWithWandsInPlayMode()
        {
            var demo = Load(); demo.startWithArrival = true;
            yield return new EnterPlayMode(); yield return null;
            demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>(); demo.airport.timeScale = 0;
            for (int i = 0; i < 2000 && demo.airport.Marshalling.Crew.Count == 0; i++)
            { demo.airport.Simulate(1f); yield return null; }
            var flight = demo.airport.Traffic.FlightOf(demo.aircraft);
            Assert.That(demo.airport.Marshalling.Crew.Count,Is.GreaterThan(0),$"{demo.Problem}; {flight?.Status}; job {demo.Job?.State}; arrival {demo.startWithArrival}");
            var worker = demo.airport.Marshalling.Crew[0];
            var camera = Camera.main;
            var orbit = camera.GetComponent<DevOrbitCamera>(); if (orbit != null) orbit.enabled = false;
            camera.transform.position = demo.airport.transform.TransformPoint(worker.Position)+Vector3.up*2f+worker.Pose.rotation*Vector3.forward*5f;
            camera.transform.LookAt(demo.airport.transform.TransformPoint(worker.Position)+Vector3.up);
            yield return null; yield return null;
            var rig = demo.airport.GetComponentsInChildren<RampCrewRig>(true).FirstOrDefault(r=>r.name.StartsWith("Marshaller"));
            Assert.That(rig,Is.Not.Null);
            Assert.That(rig.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r.name.Contains("MarshallingWand")),Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived(); yield return new ExitPlayMode();
        }
    }
}
