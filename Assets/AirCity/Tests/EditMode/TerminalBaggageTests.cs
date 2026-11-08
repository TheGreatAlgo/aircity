using System.Linq;
using AirCity.Airfield;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests
{
    public sealed class TerminalBaggageTests
    {
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        (Airport airport,BaggageDepot depot,BaggageStation team,Terminal terminal) Create()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var depot=Object.FindAnyObjectByType<BaggageDepot>(); var airport=depot.Airport;
            var team=depot.Teams.Single(); team.automaticHandling=false; depot.Rebuild();
            var port=depot.Point(depot.Layout.Right,depot.Layout.WorkZ-6)+depot.Right*40;
            var terminal=Terminal.Polygon(new[] { port+new Vector2(-10,-10),port+new Vector2(10,-10),port+new Vector2(10,10),port+new Vector2(-10,10) },3);
            airport.terminals.Add(terminal);
            Assert.IsTrue(airport.Baggage.ConnectTerminal(depot,terminal,BaggageConnectionKind.Underground,out var why),why);
            return (airport,depot,team,terminal);
        }
        BaggageTurnaround Demand(Airport airport,BaggageStation team,int count)
        {
            var plane=airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>().Single(a=>a.name=="Narrowbody baggage turnaround");
            var stand=airport.Taxiways.stands.First(s=>team.standIds.Contains(s.id));
            var job=airport.Baggage.Request(plane,stand,team.automaticHold,team,"TERMINAL-IN","TERMINAL-OUT",out var why);
            Assert.IsNotNull(job,why);
            for (int i=0;i<count;i+=36)
            {
                int bags=Mathf.Min(36,count-i);
                airport.Baggage.Ledger.Add("TERMINAL-"+i,job.DepartureFlight,bags,bags*15,true,job.DepartureSource);
            }
            Assert.IsTrue(job.Seal(out why),why);
            plane.gameObject.SetActive(false); // The link can be tested independently of apron route planning.
            return job;
        }

        [Test]
        public void Departures_QueueAtCheckInTravelThroughBoundedConveyorsAndPreserveTheManifest()
        {
            var (airport,depot,team,terminal)=Create();
            Assert.IsTrue(airport.Baggage.ConfigureTerminalLink(depot,36,60,out _));
            var job=Demand(airport,team,108); var link=airport.Baggage.TerminalLink(depot);
            Assert.AreSame(airport.Baggage.TerminalFacility(terminal).CheckIn,job.DepartureSource);
            Assert.AreEqual(108,job.DepartureSource.Count); Assert.AreEqual(0,job.Fleet.Dispatch.Count);
            airport.Baggage.Simulate(.05f);
            Assert.AreEqual(36,link.Outbound.Count); Assert.AreEqual(72,link.QueuedDepartures); Assert.AreEqual(0,job.Fleet.Dispatch.Count);
            airport.Baggage.Simulate(10); Assert.AreEqual(0,job.Fleet.Dispatch.Count,"bags cannot arrive before the travel and admission time");
            for (int i=0;i<300 && link.Pending;i++)
            {
                airport.Baggage.Simulate(1); Assert.LessOrEqual(link.Outbound.Count,36); Assert.AreEqual(108,airport.Baggage.Ledger.Count);
            }
            Assert.IsFalse(link.Pending,link.Waiting); Assert.AreEqual(108,job.Fleet.Dispatch.Count); Assert.AreEqual(0,job.DepartureSource.Count);
            Assert.IsTrue(airport.Baggage.Ledger.Batches.All(b=>b.Flight==job.DepartureFlight));
        }

        [Test]
        public void FullHall_BackpressuresTheConnectionWithoutLosingBags()
        {
            var (airport,depot,team,_)=Create(); airport.Baggage.ConfigureTerminalLink(depot,36,300,out _);
            var job=Demand(airport,team,72); var link=airport.Baggage.TerminalLink(depot);
            var filler=airport.Baggage.Ledger.Add("FULL-HALL","OTHER",job.Fleet.Dispatch.Capacity,job.Fleet.Dispatch.Capacity,true,job.Fleet.Dispatch);
            airport.Baggage.Simulate(100);
            Assert.AreEqual(36,link.Outbound.Count); Assert.AreEqual(36,link.QueuedDepartures);
            Assert.IsNotEmpty(link.Waiting); Assert.AreEqual(72+filler.Count,airport.Baggage.Ledger.Count);
            Assert.IsTrue(airport.Baggage.Ledger.Transfer(filler,job.Fleet.Dispatch,airport.Baggage.Reclaim,out _));
            airport.Baggage.Simulate(100);
            Assert.AreEqual(72,job.Fleet.Dispatch.Count); Assert.AreEqual(0,link.InTransit);
        }

        [Test]
        public void Arrivals_ReachTheirTerminalReclaimOnlyAfterTravelling()
        {
            var (airport,depot,team,terminal)=Create(); var fleet=airport.Baggage.Fleet(team);
            var link=airport.Baggage.TerminalLink(depot); var facility=airport.Baggage.TerminalFacility(terminal);
            var other=Terminal.Polygon(terminal.corners.Select(p=>p+Vector2.right*200),3); airport.terminals.Add(other);
            var otherFacility=airport.Baggage.TerminalFacility(other);
            var batch=airport.Baggage.Ledger.Add("ARRIVING","IN",36,540,true,fleet.Arrivals);
            airport.Baggage.Simulate(.05f);
            Assert.AreSame(link.Inbound,batch.Location); Assert.AreEqual(0,facility.Reclaim.Count);
            airport.Baggage.Simulate(40);
            Assert.AreSame(facility.Reclaim,batch.Location); Assert.AreEqual(36,facility.Delivered);
            Assert.AreEqual(0,otherFacility.Reclaim.Count); Assert.AreEqual(36,airport.Baggage.Ledger.Count);
        }

        [Test]
        public void Connections_PreserveTerminalIdentityAcrossEditsAndListChanges()
        {
            var (airport,depot,_,terminal)=Create(); string id=terminal.baggageId;
            var copy=terminal.Clone(); Assert.AreEqual(id,copy.baggageId);
            copy.baggage.checkInFloor=2; Assert.AreNotEqual(copy.baggage.checkInFloor,terminal.baggage.checkInFloor);
            Assert.IsTrue(airport.ChangeTerminal(terminal,copy));
            airport.terminals.RemoveAt(0); Assert.AreSame(terminal,depot.ConnectedTerminal);
            var restored=JsonUtility.FromJson<Terminal>(JsonUtility.ToJson(terminal)); Assert.AreEqual(id,restored.baggageId);
            Assert.AreNotEqual(id,new Terminal().baggageId);
        }

        [Test]
        public void FloorAndUndergroundGeometry_ContributeToTravelTime()
        {
            var (airport,depot,_,terminal)=Create(); var link=airport.Baggage.TerminalLink(depot);
            Assert.IsTrue(airport.Baggage.SetTerminalBaggageFloors(terminal,0,0,out _)); float ground=link.OutboundSeconds;
            Assert.IsTrue(airport.Baggage.SetTerminalBaggageFloors(terminal,2,0,out _)); Assert.Greater(link.OutboundSeconds,ground);
            float shallow=link.OutboundSeconds; depot.connection.depth+=5; Assert.Greater(link.OutboundSeconds,shallow);
            Assert.IsFalse(airport.Baggage.SetTerminalBaggageFloors(terminal,terminal.floors,0,out _));
            terminal.floors=1; Assert.AreEqual(0,link.Facility.CheckInFloor);
        }

        [Test]
        public void BusyConnections_CannotBeReassignedDeletedOrShrunk()
        {
            var (airport,depot,team,terminal)=Create(); var job=Demand(airport,team,72);
            airport.Baggage.Simulate(8);
            Assert.IsFalse(airport.Baggage.DisconnectTerminal(depot,out _));
            Assert.IsFalse(airport.Baggage.ConfigureTerminalLink(depot,36,60,out _));
            Assert.IsTrue(airport.Baggage.ConfigureTerminalLink(depot,180,600,out _),"capacity and rate increases can relieve an active queue");
            Assert.IsFalse(airport.Baggage.CanRemoveTerminal(terminal,out _));
            airport.RemoveTerminal(terminal); Assert.Contains(terminal,airport.terminals);
            job.Cancel(); airport.Baggage.Simulate(100);
            Assert.IsTrue(airport.Baggage.CanRemoveTerminal(terminal,out var why),why);
            airport.RemoveTerminal(terminal); Assert.IsFalse(airport.terminals.Contains(terminal));
            Assert.IsNull(depot.ConnectedTerminal); Assert.IsTrue(depot.connection.enabled,"a disconnected hall must not create its own departing baggage");
            Assert.AreEqual(72,airport.Baggage.Ledger.Count);
        }

        [Test]
        public void CoveredConnection_ReusesTheTerminalFinishAndRejectsRemoteSites()
        {
            var (airport,depot,_,terminal)=Create();
            terminal.exterior.facade.cladding=new Color(.25f,.45f,.75f);
            Assert.IsTrue(airport.Baggage.ConnectTerminal(depot,terminal,BaggageConnectionKind.Covered,out var why),why);
            Assert.IsNotNull(depot.transform.Find("Terminal baggage connection").GetComponent<MeshFilter>().sharedMesh);
            Assert.IsTrue(depot.transform.Find("Baggage depot building").GetComponent<MeshFilter>().sharedMesh.colors.Any(c=>Vector4.Distance(c,terminal.exterior.facade.cladding.linear)<.001f));
            var remote=Terminal.Polygon(terminal.corners.Select(p=>p+Vector2.right*300),3); airport.terminals.Add(remote);
            Assert.IsFalse(airport.Baggage.ConnectTerminal(depot,remote,BaggageConnectionKind.Covered,out why));
            Assert.That(why,Does.Contain("80 m")); Assert.AreSame(terminal,depot.ConnectedTerminal);
        }
    }
}
