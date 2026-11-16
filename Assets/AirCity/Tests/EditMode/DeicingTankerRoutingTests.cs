using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AirCity.Airfield;
using AirCity.EditorTools.Airfield;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests.Vehicles
{
    public sealed class DeicingTankerRoutingTests
    {
        readonly List<Object> spawned=new List<Object>();
        [SetUp] public void Started() => UnityEngine.Debug.Log("TANKER ROUTING START "+TestContext.CurrentContext.Test.Name);
        [TearDown] public void Cleanup()
        {
            UnityEngine.Debug.Log("TANKER ROUTING END "+TestContext.CurrentContext.Test.Name+": "+TestContext.CurrentContext.Result.Outcome+" "+TestContext.CurrentContext.Result.Message);
            foreach(var item in spawned) if(item!=null) Object.DestroyImmediate(item); spawned.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
        (Airport airport,DeicingWorkArea area) SandboxStation()
        {
            var airport=AirfieldSetup.BuildSandboxAirport(); spawned.Add(airport.gameObject);
            var area=airport.Deicing.AddWorkArea(new Vector2(-40f,-8f),0f);
            Assert.That(area,Is.Not.Null,airport.Deicing.WorkAreaProblem(new Vector2(-40f,-8f),0f));
            return (airport,area);
        }
        [Test] public void ActualSandboxFrontRoadOrderIsFastAndPhysicallyUnloadsAndReturns()
        {
            var (airport,area)=SandboxStation(); var service=airport.Deicing;
            UnityEngine.Debug.Log("TANKER ROUTING actual Sandbox station built");
            Assert.That(Vector2.Distance(area.UnloadExit,new Vector2(-46.25f,-20f)),Is.LessThan(.01f));
            int searches=DeicingMotion.RoadGraphSearchCount;
            var timer=Stopwatch.StartNew();
            Assert.That(service.OrderFluid(area,5000f,out var problem),Is.True,problem);
            timer.Stop(); double orderMs=timer.Elapsed.TotalMilliseconds;
            UnityEngine.Debug.Log($"TANKER ROUTING order returned in {orderMs:0.0}ms");
            Assert.That(DeicingMotion.RoadGraphSearchCount,Is.EqualTo(searches),"An order must not synchronously run the vehicle route planner.");
            Assert.That(orderMs,Is.LessThan(500d),"Ordering is a bounded local check, independent of the 680m supplier road.");
            area.deliverySecondsRemaining=0f;
            timer.Restart(); service.Simulate(.05f); timer.Stop(); double planMs=timer.Elapsed.TotalMilliseconds;
            UnityEngine.Debug.Log($"TANKER ROUTING first planning step returned in {planMs:0.0}ms");
            var delivery=service.DeliveryFor(area);
            Assert.That(delivery,Is.Not.Null); Assert.That(delivery.Drive,Is.Not.Null,delivery.Status+"; road candidates "+DeicingMotion.LastRoadCandidates);
            Assert.That(planMs,Is.LessThan(1000d),"The first supplier step must use a bounded road-guided planner.");
            Assert.That(delivery.Drive.Path.Length,Is.GreaterThan(600f));
            Assert.That(DeicingMotion.LastRoadCandidates,Is.LessThanOrEqualTo(4));
            float elapsed=0f,waiting=0f,nextReport=100f;
            while(elapsed<600f && (area.DeliveryPending || service.Deliveries.Count>0))
            {
                service.Simulate(.1f); elapsed+=.1f;
                Assert.That(area.fluidLitres+area.deliveryLitres,Is.EqualTo(5000f).Within(.2f));
                waiting=delivery.Phase==DeicingDeliveryPhase.Waiting && delivery.Drive.Finished?waiting+.1f:0f;
                if(waiting>10f) Assert.Fail($"Tanker cannot finish its static journey at {elapsed:0.0}s: {delivery.Status}; pose {delivery.Position}/{delivery.Facing}, phase {delivery.Phase}, stock {area.fluidLitres}, cargo {delivery.CargoLitres}; candidates {DeicingMotion.LastRoadCandidates}.");
                if(elapsed>=nextReport)
                {
                    UnityEngine.Debug.Log($"TANKER ROUTING {elapsed:0}s: {delivery.Status}, {delivery.Position}, stock {area.fluidLitres}, cargo {delivery.CargoLitres}"); nextReport+=100f;
                }
            }
            Assert.That(area.fluidLitres,Is.EqualTo(5000f).Within(.1f),delivery.Status);
            Assert.That(service.Deliveries,Is.Empty,delivery.Status);
            Assert.That(delivery.Phase,Is.EqualTo(DeicingDeliveryPhase.Complete));
            TestContext.Out.WriteLine($"SANDBOX TANKER: order {orderMs:0.0}ms; first planning step {planMs:0.0}ms; physical unload/return {elapsed:0.0}s; {delivery.DeliveredLitres:0.0}L delivered.");
            UnityEngine.Debug.Log($"TANKER ROUTING METRICS order {orderMs:0.0}ms, plan {planMs:0.0}ms, lifecycle {elapsed:0.0}s.");
        }
        [Test] public void UnpavedUnloadLaneRejectsImmediatelyWithoutPlanningOrSpending()
        {
            var (airport,area)=SandboxStation(); var service=airport.Deicing;
            airport.aprons.Clear(); airport.BuildGround();
            int searches=DeicingMotion.RoadGraphSearchCount; double spent=service.Accounts.TotalSpent;
            var timer=Stopwatch.StartNew();
            for(int i=0;i<20;i++) Assert.That(service.OrderFluid(area,5000f,out _),Is.False);
            timer.Stop(); Assert.That(timer.Elapsed.TotalMilliseconds,Is.LessThan(500d));
            Assert.That(DeicingMotion.RoadGraphSearchCount,Is.EqualTo(searches));
            Assert.That(service.Accounts.TotalSpent,Is.EqualTo(spent)); Assert.That(area.deliveryLitres,Is.Zero);
            TestContext.Out.WriteLine($"SANDBOX TANKER: 20 invalid orders {timer.Elapsed.TotalMilliseconds:0.0}ms; no graph planning or spending.");
        }
        [Test] public void RoadGuidedPlannerUsesActualGatewayAxleAndCachesUnchangedGraphSearches()
        {
            var (airport,area)=SandboxStation(); var service=airport.Deicing; var type=service.TankerType;
            var node=airport.Taxiways.Node(airport.GetComponent<SandboxOutsideAccess>().OutsideNodeId);
            var origin=new Vector2(node.position.x,node.position.z);
            var from=new BaggageVehiclePose(origin+Vector2.up*(-type.bounds.min.z+.5f),Vector2.up);
            var to=service.DeliveryPose(area);
            var first=DeicingMotion.PlanRoadJourney(airport,type,from,to,null,area.UnloadExit);
            Assert.That(first,Is.Not.Null); Assert.That(first.Sample(0f),Is.EqualTo(from.position));
            int searches=DeicingMotion.RoadGraphSearchCount;
            var second=DeicingMotion.PlanRoadJourney(airport,type,from,to,null,area.UnloadExit);
            Assert.That(second,Is.Not.Null); Assert.That(DeicingMotion.RoadGraphSearchCount,Is.EqualTo(searches));
            Assert.That(DeicingMotion.PathClear(airport,type,first),Is.True);
        }
        [Test] public void CollinearPortSplitsDoNotTightenTheTankerCorner()
        {
            var (airport,area)=SandboxStation(); var net=airport.Taxiways; var type=airport.Deicing.TankerType;
            foreach(float x in new[]{area.UnloadExit.x,area.FillEntry.x})
            {
                var point=new Vector3(x,0f,-20f);
                Assert.That(net.TryNearestEdge(point,1f,out var edge,out var at),Is.True); net.SplitEdge(edge,at);
            }
            net.Rebuild();
            var access=airport.GetComponent<SandboxOutsideAccess>();
            var from=new BaggageVehiclePose(access.Position+Vector2.up*(-type.bounds.min.z+.5f),Vector2.up);
            var path=DeicingMotion.PlanRoadJourney(airport,type,from,airport.Deicing.DeliveryPose(area),null,area.UnloadExit);
            Assert.That(path,Is.Not.Null);
            for(float at=0f;at<path.Length;at+=.5f)
            {
                path.Pose(at,out _,out float curvature);
                Assert.That(Mathf.Abs(curvature),Is.LessThanOrEqualTo(1f/type.turnRadius+.02f));
            }
        }
        [Test] public void ActualSandboxTruckPurchaseUsesTheBoundedOutsideRoadPlanner()
        {
            var (airport,_)=SandboxStation(); var service=airport.Deicing;
            var facility=service.Configure(airport.Taxiways.stands.First(s=>s.DeIcing));
            var bay=service.AddParkingBay(new Vector2(-35f,30f),0f); Assert.That(bay,Is.Not.Null,service.LastPurchaseProblem);
            var timer=Stopwatch.StartNew();
            Assert.That(service.OrderTruck(facility,bay,out var problem),Is.True,problem);
            timer.Stop(); Assert.That(timer.Elapsed.TotalMilliseconds,Is.LessThan(1000d));
            Assert.That(service.TrucksFor(facility).Single().DeliveryPending,Is.True);
            Assert.That(DeicingMotion.LastRoadCandidates,Is.LessThanOrEqualTo(4));
            TestContext.Out.WriteLine($"SANDBOX TRUCK: physical truck order validation {timer.Elapsed.TotalMilliseconds:0.0}ms.");
        }
        [Test] public void LongFrontOverhangUsesWiderSandboxExitTurnWithoutLeavingPavement()
        {
            var (airport,_)=SandboxStation(); var type=airport.Deicing.TankerType;
            var source=airport.GetComponent<SandboxOutsideAccess>().Position;
            var from=new BaggageVehiclePose(new Vector2(0f,-20f),Vector2.right);
            var to=new BaggageVehiclePose(source+Vector2.up*(type.bounds.max.z+.5f),Vector2.down);
            var path=DeicingMotion.PlanRoadJourney(airport,type,from,to);
            Assert.That(path,Is.Not.Null,"The minimum-radius front-overhang clip must have a safe wider turn alternative.");
            Assert.That(DeicingMotion.PathClear(airport,type,path),Is.True);
            float maximum=0f;
            for(float at=0f;at<path.Length;at+=.4f) { path.Pose(at,out _,out float curvature); maximum=Mathf.Max(maximum,Mathf.Abs(curvature)); }
            Assert.That(maximum,Is.LessThan(1f/(type.turnRadius*1.5f)+.005f),"This junction needs the wider road fillet rather than the steering minimum.");
        }
    }
}
