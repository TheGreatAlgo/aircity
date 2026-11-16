using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Game;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests.Vehicles
{
    public sealed class SandboxOutsideAccessTests
    {
        readonly List<Object> spawned=new List<Object>();
        [TearDown] public void Cleanup() { foreach(var item in spawned) if(item!=null) Object.DestroyImmediate(item); spawned.Clear(); }
        Airport Create()
        {
            var go=new GameObject("Sandbox access test"); go.SetActive(false);
            var airport=go.AddComponent<Airport>(); airport.groundSize=0f; go.SetActive(true); spawned.Add(go);
            int a=airport.Taxiways.AddNode(new Vector3(-100f,0f,-20f)),b=airport.Taxiways.AddNode(new Vector3(100f,0f,-20f));
            airport.Taxiways.Connect(a,b,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            return airport;
        }
        [Test] public void SandboxSpurConnectsExistingServiceGraphToMarkedOutsideAndIsIdempotent()
        {
            var airport=Create(); int originalNode=airport.Taxiways.nodes[0].id;
            var access=SandboxOutsideAccess.Install(airport);
            Assert.That(access.EnsureConnection(out var problem),Is.True,problem);
            Assert.That(access.Position,Is.EqualTo(SandboxOutsideAccess.DefaultPortal));
            Assert.That(airport.Deicing.IsOutsideAccess(access.OutsideNodeId),Is.True);
            Assert.That(airport.Deicing.OutsideAccessProblem(access.OutsideNodeId),Is.Null);
            var route=airport.Taxiways.FindRoute(access.OutsideNodeId,originalNode,e=>e.road==RoadClass.Service&&e.surface.IsPaved(),r=>false);
            Assert.That(route,Is.Not.Null); Assert.That(route.Count,Is.GreaterThanOrEqualTo(3));
            Assert.That(airport.Taxiways.edges.All(e=>e.road==RoadClass.Service&&e.surface.IsPaved()),Is.True);
            Assert.That(access.MarkerMesh,Is.Not.Null); Assert.That(access.MarkerMesh.vertexCount,Is.GreaterThan(0));
            int nodes=airport.Taxiways.nodes.Count,edges=airport.Taxiways.edges.Count,aprons=airport.aprons.Count;
            Assert.That(access.EnsureConnection(out problem),Is.True,problem);
            Assert.That(airport.Taxiways.nodes.Count,Is.EqualTo(nodes)); Assert.That(airport.Taxiways.edges.Count,Is.EqualTo(edges));
            Assert.That(airport.aprons.Count,Is.EqualTo(aprons)); Assert.That(airport.Deicing.OutsideAccess.Count,Is.EqualTo(1));
        }
        [Test] public void UserMarkedInternalLeafIsPreservedAlongsideVisibleDefaultOutsideSpur()
        {
            var airport=Create(); var old=airport.Deicing.AddOutsideAccess(airport.Taxiways.nodes[0].id,out var problem);
            Assert.That(old,Is.Not.Null,problem); var oldEdges=airport.Taxiways.edges.ToArray();
            var access=SandboxOutsideAccess.Install(airport);
            Assert.That(access.EnsureConnection(out problem),Is.True,problem);
            Assert.That(airport.Deicing.OutsideAccess,Does.Contain(old)); Assert.That(airport.Deicing.OutsideAccess.Count,Is.EqualTo(2));
            foreach(var edge in oldEdges) Assert.That(airport.Taxiways.edges,Does.Contain(edge));
            Assert.That(access.Position,Is.EqualTo(SandboxOutsideAccess.DefaultPortal));
        }
        [Test] public void ClosedAuthoredSpurIsNotReopenedOrDuplicatedByBootstrap()
        {
            var airport=Create(); var access=SandboxOutsideAccess.Install(airport);
            var road=airport.Taxiways.EdgesAt(access.OutsideNodeId).Single(); road.wear=RunwayRules.ClosedWear;
            int nodes=airport.Taxiways.nodes.Count,edges=airport.Taxiways.edges.Count,aprons=airport.aprons.Count;
            Assert.That(access.EnsureConnection(out var problem),Is.False); StringAssert.Contains("open",problem);
            Assert.That(road.wear,Is.EqualTo(RunwayRules.ClosedWear)); Assert.That(airport.Taxiways.nodes.Count,Is.EqualTo(nodes));
            Assert.That(airport.Taxiways.edges.Count,Is.EqualTo(edges)); Assert.That(airport.aprons.Count,Is.EqualTo(aprons));
        }
        [Test] public void MarkedChosenEndpointStaysAValidOutsideLeafWhenDefaultSpurBranchesInsideRoad()
        {
            var airport=Create(); int oldNode=airport.Taxiways.nodes[1].id;
            var old=airport.Deicing.AddOutsideAccess(oldNode,out var problem); Assert.That(old,Is.Not.Null,problem);
            var access=SandboxOutsideAccess.Install(airport); Assert.That(access.EnsureConnection(out problem),Is.True,problem);
            Assert.That(airport.Deicing.OutsideAccess,Does.Contain(old));
            Assert.That(airport.Deicing.OutsideAccessProblem(oldNode),Is.Null,"The user's marked endpoint must remain an endpoint.");
            Assert.That(airport.Taxiways.EdgesAt(oldNode).Count(e=>e.road==RoadClass.Service),Is.EqualTo(1));
            Assert.That(airport.Taxiways.FindRoute(access.OutsideNodeId,oldNode,e=>e.road==RoadClass.Service&&e.surface.IsPaved(),r=>false),Is.Not.Null);
            Assert.That(airport.Taxiways.nodes.Any(n=>Vector2.Distance(new Vector2(n.position.x,n.position.z),new Vector2(80f,-20f))<.1f),Is.True);
        }
        [Test] public void CareerAirportAndUnrelatedTestSceneAreNeverAutomaticallyUpgraded()
        {
            var airport=Create(); var career=new GameObject("Career airport root"); spawned.Add(career); career.AddComponent<CareerGame>();
            airport.transform.SetParent(career.transform); int edges=airport.Taxiways.edges.Count;
            Assert.That(SandboxOutsideAccess.Install(airport),Is.Null);
            Assert.That(airport.Taxiways.edges.Count,Is.EqualTo(edges)); Assert.That(airport.Deicing.OutsideAccess,Is.Empty);
            var unrelated=EditorSceneManager.NewPreviewScene();
            try { Assert.That(SandboxOutsideAccess.IsSandboxScene(unrelated),Is.False); }
            finally { EditorSceneManager.ClosePreviewScene(unrelated); }
        }
        [Test] public void OccupiedOutsideCorridorRefusesUpgradeWithoutChangingExistingAirport()
        {
            var airport=Create(); airport.AddRunway(new Vector3(-200f,0f,-300f),new Vector3(300f,0f,-300f),PavementSurface.Concrete,30f);
            int edges=airport.Taxiways.edges.Count,nodes=airport.Taxiways.nodes.Count,aprons=airport.aprons.Count;
            var access=SandboxOutsideAccess.Install(airport);
            Assert.That(access.EnsureConnection(out var problem),Is.False); StringAssert.Contains("runway",problem);
            Assert.That(airport.Taxiways.edges.Count,Is.EqualTo(edges)); Assert.That(airport.Taxiways.nodes.Count,Is.EqualTo(nodes));
            Assert.That(airport.aprons.Count,Is.EqualTo(aprons)); Assert.That(airport.Deicing.OutsideAccess,Is.Empty);
        }
    }
}
