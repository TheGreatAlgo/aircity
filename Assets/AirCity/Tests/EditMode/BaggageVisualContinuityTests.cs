using System.Linq;
using AirCity.Airfield;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests
{
    public sealed class BaggageVisualContinuityTests
    {
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);

        [Test]
        public void StoreBatchIndex_FollowsSuccessfulTransfersAndPreservesRefusedTransfers()
        {
            var ledger=new BaggageLedger(); var cart=ledger.Store("cart",20,300); var depot=ledger.Store("depot",20,300);
            var tiny=ledger.Store("tiny",1,15);
            var batch=ledger.Add("arrival-a","IN",3,45,true,cart);
            Assert.AreSame(batch,cart.Batches.Single());
            Assert.IsFalse(ledger.Transfer(batch,cart,tiny,out _)); Assert.AreSame(batch,cart.Batches.Single());
            Assert.IsTrue(ledger.Transfer(batch,cart,depot,out _));
            Assert.IsEmpty(cart.Batches); Assert.AreSame(batch,depot.Batches.Single());
            Assert.IsFalse(ledger.Transfer(batch,cart,depot,out _)); Assert.AreEqual(1,depot.Batches.Count);
            Assert.AreEqual(3,ledger.Count);
        }

        [Test]
        public void RemovingEarlierBags_DoesNotChangeTheRemainingBagsSlotsOrAppearances()
        {
            var ledger=new BaggageLedger(); var cart=ledger.Store("cart",20,300); var depot=ledger.Store("depot",20,300);
            var first=ledger.Add("first","IN",3,45,true,cart);
            var second=ledger.Add("second","IN",3,45,true,cart);
            var layout=new BaggageStorageLayout(6); layout.Update(cart);
            var before=layout.Bags.Where(b=>b.batch==second).ToDictionary(b=>b.index,b=>(b.slot,b.variant));
            Assert.IsTrue(ledger.Transfer(first,cart,depot,out _)); layout.Update(cart);
            foreach (var bag in layout.Bags) Assert.AreEqual(before[bag.index],(bag.slot,bag.variant));
            ledger.Add("replacement","IN",3,45,true,cart); layout.Update(cart);
            foreach (var bag in layout.Bags.Where(b=>b.batch==second)) Assert.AreEqual(before[bag.index],(bag.slot,bag.variant));
            var destination=new BaggageStorageLayout(6); destination.Update(depot);
            foreach (var bag in destination.Bags) Assert.AreEqual(BaggageHoldPacking.Variant(first,bag.index),bag.variant);
        }

        (Airport airport,BaggageDepot depot,BaggageStation station) Load()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var depot=Object.FindAnyObjectByType<BaggageDepot>();
            return (depot.Airport,depot,depot.Teams.Single());
        }
        static System.Collections.Generic.Dictionary<Vector3,Mesh> Snapshot(Transform root) => root.GetComponentsInChildren<MeshFilter>()
            .Where(f=>f.gameObject.activeInHierarchy).ToDictionary(f=>f.transform.localPosition,f=>f.sharedMesh);

        [Test]
        public void UnloadingOneCart_DoesNotRecolourOrRearrangeAnotherCart()
        {
            var (airport,_,station)=Load(); var fleet=airport.Baggage.Fleet(station);
            Assert.AreEqual(2,fleet.Carts.Length);
            var first=airport.Baggage.Ledger.Add("cart-one","IN",2,30,true,fleet.Carts[0]);
            airport.Baggage.Ledger.Add("cart-two","IN",6,90,true,fleet.Carts[1]);
            var root=new GameObject("Bag continuity test").transform; root.SetParent(airport.transform,false);
            var visuals=new BaggageLoadVisuals(root,station.bagPrefabs); visuals.Present(fleet);
            var before=Snapshot(root); Assert.AreEqual(8,before.Count);
            Assert.IsTrue(airport.Baggage.Ledger.Transfer(first,fleet.Carts[0],fleet.Arrivals,out _));
            visuals.Present(fleet); var after=Snapshot(root); Assert.AreEqual(6,after.Count);
            foreach (var pair in after)
            { Assert.IsTrue(before.ContainsKey(pair.Key),"remaining bag moved"); Assert.AreSame(before[pair.Key],pair.Value,"remaining bag changed appearance"); }
        }

        [Test]
        public void ArrivalsGrowing_DoNotChangeDepartureBagsAtTheDepot()
        {
            var (airport,depot,station)=Load(); var fleet=airport.Baggage.Fleet(station);
            airport.Baggage.Ledger.Add("departures","OUT",6,90,true,fleet.Dispatch); depot.Present();
            var root=depot.transform.Find("Depot luggage (runtime)");
            var before=Snapshot(root); Assert.AreEqual(6,before.Count);
            airport.Baggage.Ledger.Add("arrivals","IN",2,30,true,fleet.Arrivals); depot.Present();
            var after=Snapshot(root); Assert.AreEqual(8,after.Count);
            foreach (var pair in before)
            { Assert.IsTrue(after.ContainsKey(pair.Key),"departure bag moved"); Assert.AreSame(pair.Value,after[pair.Key],"departure bag changed appearance"); }
        }

        [Test]
        public void StartingAnotherHandover_DoesNotRecolourTheRemainingCartLoad()
        {
            var (airport,_,station)=Load(); station.incomingBags=station.outgoingBags=20;
            airport.Simulate(.1f); var job=airport.Baggage.Jobs.Single();
            for (int i=0;i<20000 && !(job.State==BaggageTurnaroundState.UnloadingArrivals && job.TransferSeconds>.01f);i++) airport.Simulate(.05f);
            Assert.AreEqual(BaggageTurnaroundState.UnloadingArrivals,job.State,job.Waiting);
            var root=new GameObject("Live unloading continuity").transform; root.SetParent(airport.transform,false);
            var visuals=new BaggageLoadVisuals(root,station.bagPrefabs); visuals.Present(job.Fleet);
            var before=Snapshot(root);
            airport.Simulate(job.TransferInterval); visuals.Present(job.Fleet);
            var after=Snapshot(root); int unchanged=0;
            foreach (var pair in after)
                if (before.TryGetValue(pair.Key,out var mesh))
                { unchanged++; Assert.AreSame(mesh,pair.Value,"a stationary suitcase changed appearance when the next bag was picked up"); }
            Assert.GreaterOrEqual(unchanged,18,"the remaining load keeps its storage positions");
        }

        [Test]
        public void DepotRoadPaint_LeavesBayConnectorsUnpaintedWithoutChangingRouting()
        {
            var (airport,depot,_)=Load(); var net=airport.Taxiways;
            int version=airport.LayoutVersion,nodes=net.nodes.Count,edges=net.edges.Count;
            net.RefreshEquipmentPaint();
            var vertices=net.PaintMesh.vertices; var colours=net.PaintMesh.colors;
            var inside=Enumerable.Range(0,vertices.Length).Where(i=>depot.PaintArea.Contains(new Vector2(vertices[i].x,vertices[i].z))).ToArray();
            Assert.IsEmpty(inside,"paint inside depot: "+string.Join("; ",inside.Take(8).Select(i=>vertices[i]+" "+colours[i])));
            Assert.AreEqual(version,airport.LayoutVersion); Assert.AreEqual(nodes,net.nodes.Count); Assert.AreEqual(edges,net.edges.Count);
        }
    }
}
