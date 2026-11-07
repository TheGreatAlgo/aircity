using System;
using System.Linq;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
using Plane=AirCity.Aircraft.Aircraft;
using PlaneType=AirCity.Aircraft.AircraftType;

namespace AirCity.Tests
{
    public sealed class BaggageDepotTests
    {
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        static PlaneType AircraftType => AircraftSetup.AllTypes().Single(t=>t.model=="Narrowbody");
        (Airport airport,BaggageDepot depot,BaggageStation station) Create(BaggageDepotSize size=BaggageDepotSize.Small)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var root=new GameObject("Depot test"); root.SetActive(false);
            var airport=root.AddComponent<Airport>(); airport.groundSize=0; airport.aircraftTypes.Add(AircraftType);
            AirfieldSetup.AssignMaterials(airport);
            airport.aprons.Add(ApronArea.Rect(Vector2.zero,new Vector2(12000,2000),0,PavementSurface.Concrete)); root.SetActive(true);
            var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-150,0,-50)),b=net.AddNode(new Vector3(-150,0,100));
            net.Connect(a,b,12,PavementSurface.Concrete,false,RoadClass.Service);
            var depotGo=new GameObject("Baggage depot"); depotGo.transform.SetParent(root.transform,false);
            var depot=depotGo.AddComponent<BaggageDepot>(); depot.size=size; depot.firstBay=net.At(a);
            var stationGo=new GameObject("Team 1"); stationGo.SetActive(false); stationGo.transform.SetParent(root.transform,false);
            var station=stationGo.AddComponent<BaggageStation>(); station.depot=depot; station.cartCount=2; station.parkAtStand=false;
            station.tractorType=VehicleSetup.Type("BaggageTractor"); station.cartType=VehicleSetup.Type("BaggageCart"); station.loaderType=VehicleSetup.Type("BeltLoader");
            station.automaticHold=new BulkHoldConfiguration { aircraft=AircraftType };
            station.incomingBags=station.outgoingBags=1; station.workersPerTeam=2;
            depot.ConnectBay(station); stationGo.SetActive(true); net.Rebuild(); return (airport,depot,station);
        }

        [TestCase(BaggageDepotSize.Small,2,4,8)]
        [TestCase(BaggageDepotSize.Medium,4,12,20)]
        [TestCase(BaggageDepotSize.Large,8,24,40)]
        public void Sizes_EnforceStaffAndEquipmentCapacities(BaggageDepotSize size,int teams,int trailers,int workers)
        {
            var (_,depot,_)=Create(size); var maximum=depot.Maximum;
            Assert.AreEqual(teams,maximum.teams); Assert.AreEqual(trailers,maximum.trailers); Assert.AreEqual(workers,maximum.workers);
            depot.hiredWorkers=0;
            for (int i=0;i<workers;i++) Assert.IsTrue(depot.Hire(out _));
            Assert.IsFalse(depot.Hire(out var why)); Assert.IsNotEmpty(why);
            for (int i=0;i<workers;i++) Assert.IsTrue(depot.Fire(out _));
            Assert.IsFalse(depot.Fire(out _)); Assert.AreEqual(0,depot.hiredWorkers);
        }

        [Test]
        public void Teams_ShareLuggageStoresAndUseDistinctDepotBaysWithinCapacity()
        {
            var (airport,depot,first)=Create();
            var second=depot.AddTeam(first,out var why); Assert.IsNotNull(second,why);
            Assert.IsNull(depot.AddTeam(first,out _)); Assert.AreEqual(4,depot.Trailers);
            Assert.IsFalse(depot.CanAddTrailer(first,out _));
            var a=airport.Baggage.Fleet(first); var b=airport.Baggage.Fleet(second);
            Assert.AreSame(a.Dispatch,b.Dispatch); Assert.AreSame(a.Arrivals,b.Arrivals);
            Assert.AreNotEqual(a.Train.Tractor.position,b.Train.Tractor.position);
            Assert.AreEqual(first.dispatchNode,first.arrivalsNode,"unload and collection share the actual depot bay");
            Assert.AreEqual(second.dispatchNode,second.arrivalsNode);
            int capacity=a.Dispatch.Capacity;
            Assert.IsTrue(depot.Resize(BaggageDepotSize.Medium,out why),why);
            Assert.Greater(a.Dispatch.Capacity,capacity);
        }

        [Test]
        public void Hiring_AssignsRealCrewsAndDoesNotFireWorkersDuringTheirTurnaround()
        {
            var (airport,depot,first)=Create(); first.workersPerTeam=4;
            var second=depot.AddTeam(first,out var why); Assert.IsNotNull(second,why);
            var a=airport.Baggage.Fleet(first); var b=airport.Baggage.Fleet(second);
            Assert.IsTrue(depot.TryAssignCrew(a,out why),why); Assert.AreEqual(4,a.Workers);
            Assert.IsFalse(depot.Fire(out _)); Assert.IsFalse(depot.TryAssignCrew(b,out _));
            Assert.IsTrue(depot.Hire(out _)); Assert.IsTrue(depot.Hire(out _));
            Assert.IsTrue(depot.TryAssignCrew(b,out why),why); Assert.AreEqual(2,b.Workers);
            Assert.AreEqual(2,a.Crew.GroundWorkers); Assert.AreEqual(2,a.Crew.HoldWorkers);
            Assert.AreEqual(1,b.Crew.GroundWorkers); Assert.AreEqual(1,b.Crew.HoldWorkers);
            Assert.AreEqual(6,depot.Working); Assert.AreEqual(0,depot.Available);
            Assert.IsFalse(depot.Resize(BaggageDepotSize.Medium,out _));
            depot.ReleaseCrew(a); Assert.IsTrue(depot.Fire(out why),why);
            Assert.AreEqual(5,depot.hiredWorkers); Assert.AreEqual(2,depot.Working);
        }

        [Test]
        public void DepotBays_KeepEveryParkedTrailerClearOfTheServiceRoadSpine()
        {
            var (airport,depot,first)=Create(); Assert.IsNotNull(depot.AddTeam(first,out _));
            for (float z=-30;z<50;z+=.5f)
            {
                var moving=AirportMaintenance.Box.Of(depot.Point(0,z),depot.Facing,first.tractorType.bounds,.3f);
                foreach (var team in depot.Teams)
                {
                    var fleet=airport.Baggage.Fleet(team); var train=fleet.Train;
                    Assert.IsFalse(moving.Overlaps(AirportMaintenance.Box.Of(train.Tractor.position,train.Tractor.facing,team.tractorType.bounds,.3f)));
                    for (int i=0;i<train.CartCount;i++)
                    {
                        var cart=train.CartAt(i);
                        Assert.IsFalse(moving.Overlaps(AirportMaintenance.Box.Of(cart.position,cart.facing,team.cartType.bounds,.3f)));
                    }
                }
            }
        }

        [Test]
        public void DepotPlacement_RejectsOccupiedOrUnpavedBuildingSpace()
        {
            var (airport,depot,_)=Create(); Assert.IsNull(depot.PlacementProblem());
            var other=new GameObject("Other depot").AddComponent<BaggageDepot>(); other.transform.SetParent(airport.transform,false);
            other.firstBay=depot.firstBay;
            Assert.IsNotNull(depot.PlacementProblem()); Object.DestroyImmediate(other.gameObject);
            airport.aprons.Clear(); Assert.That(depot.PlacementProblem(),Does.Contain("Pave"));
        }

        [Test]
        public void BuyingATeam_UsesPreparedBaysWithoutInvalidatingActiveServicePlans()
        {
            var (airport,depot,first)=Create(); depot.Rebuild();
            int version=airport.LayoutVersion,nodes=airport.Taxiways.nodes.Count,edges=airport.Taxiways.edges.Count;
            Assert.IsNotNull(depot.AddTeam(first,out var why),why); airport.Taxiways.RefreshEquipmentPaint();
            Assert.AreEqual(version,airport.LayoutVersion); Assert.AreEqual(nodes,airport.Taxiways.nodes.Count);
            Assert.AreEqual(edges,airport.Taxiways.edges.Count);
        }

        [Test]
        public void FiftyStands_ShareFourTeamsAndQueueWithoutEagerRoutePlanning()
        {
            var (airport,depot,first)=Create(BaggageDepotSize.Medium); depot.hiredWorkers=8;
            for (int i=1;i<4;i++) Assert.IsNotNull(depot.AddTeam(first,out _));
            var teams=depot.Teams; var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-50,0,0)),b=net.AddNode(new Vector3(5050,0,0));
            net.Connect(a,b,15,PavementSurface.Concrete);
            for (int i=0;i<50;i++)
            {
                var stand=net.AddStand(StandKind.Parking,new Vector3(i*100,0,0),0,SizeCode.C,PavementSurface.Concrete);
                Assert.IsNotNull(stand); stand.noSmallAircraft=true;
                foreach (var team in teams) { team.standIds.Add(stand.id); team.automaticHandling=true; }
                var planeGo=new GameObject("Queued aircraft "+i); planeGo.SetActive(false); planeGo.transform.SetParent(airport.transform,false);
                var plane=planeGo.AddComponent<Plane>(); plane.type=AircraftType;
                var at=net.Layout(stand).ParkPosition(plane.type); plane.transform.localPosition=new Vector3(at.x,0,at.y); planeGo.SetActive(true);
            }
            net.Rebuild(); var watch=System.Diagnostics.Stopwatch.StartNew();
            foreach (var plane in airport.GetComponentsInChildren<Plane>()) airport.Baggage.PrepareAutomatic(plane);
            Assert.AreEqual(50,airport.Baggage.Jobs.Count);
            Assert.IsFalse(airport.Baggage.Jobs.Any(j=>j.Acquired));
            var queues=teams.Select(t=>airport.Baggage.Jobs.Count(j=>j.Fleet.Station==t)).ToArray();
            Assert.LessOrEqual(queues.Max()-queues.Min(),1,"work is shared across the depot teams");
            Assert.AreEqual(100,airport.Baggage.Ledger.Count);
            Assert.AreSame(airport.Baggage.Stations,airport.Baggage.Stations,"simulation reuses the station registry");
            foreach (var team in teams) Assert.IsTrue(depot.TryAssignCrew(airport.Baggage.Fleet(team),out _));
            Assert.AreEqual(8,depot.Working); Assert.AreEqual(0,depot.Available);
            Debug.Log($"50-stand queue: {watch.Elapsed.TotalMilliseconds:F1} ms; 4 teams, 8 assigned workers, 50 conserved manifests; no routes built before dispatch.");
        }

        [Test]
        public void HiredHelpers_IncreaseThroughputBetweenBatchesAndConserveEveryBag()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var depot=Object.FindAnyObjectByType<BaggageDepot>(); Assert.IsNotNull(depot);
            var airport=depot.Airport; var station=depot.Teams.Single();
            depot.hiredWorkers=2; station.incomingBags=station.outgoingBags=20;
            var plane=airport.GetComponentsInChildren<Plane>().Single(p=>p.name=="Narrowbody baggage turnaround");
            airport.Simulate(.1f); var job=airport.Baggage.JobOf(plane); Assert.IsNotNull(job,station.Problem);
            void Until(Func<bool> done)
            {
                for (int i=0;i<36000 && !done();i++) airport.Simulate(.25f);
                Assert.IsTrue(done(),job.State+": "+job.Waiting);
            }
            Until(()=>job.TransferBatch!=null && !job.DepotTransfer);
            var batch=job.TransferBatch; float interval=job.TransferInterval;
            Assert.IsTrue(depot.Hire(out _)); Assert.IsTrue(depot.Hire(out _));
            Assert.AreEqual(2,job.Fleet.Workers,"an active handover retains its assigned crew");
            Until(()=>job.TransferBatch!=null && job.TransferBatch!=batch && !job.DepotTransfer);
            Assert.AreEqual(4,job.Fleet.Workers); Assert.Less(job.TransferInterval,interval);
            int layout=airport.LayoutVersion;
            var extra=depot.AddTeam(station,out var why); Assert.IsNotNull(extra,why); airport.Baggage.Fleet(extra);
            Assert.AreEqual(layout,airport.LayoutVersion,"buying a team does not cancel an ongoing turnaround");
            for (int sample=0;sample<300;sample++)
            {
                if (job.TransferBatch!=null && !job.DepotTransfer)
                {
                    var held=new int[2,2];
                    for (int bag=0;bag<job.TransferBatch.Count;bag++)
                    {
                        float elapsed=job.TransferSeconds-bag*job.TransferInterval;
                        if (elapsed<0 || elapsed>=job.BagTravelSeconds) continue;
                        var pose=job.Fleet.Crew.BagAt(elapsed,job.TransferTo==job.Hold,bag);
                        if (pose.holder>=0) held[pose.holder,pose.worker]++;
                    }
                    foreach (int count in held) Assert.LessOrEqual(count,1,"hiring never gives one worker two simultaneous bags");
                }
                airport.Simulate(.1f);
            }
            Until(()=>job.DepartureReady);
            Assert.AreEqual(20,job.Fleet.Arrivals.Count); Assert.AreEqual(20,job.Hold.Count);
            Assert.AreEqual(40,airport.Baggage.Ledger.Count); Assert.AreEqual(0,depot.Working);
        }
    }
}
