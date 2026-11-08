using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests
{
    public sealed class BaggageGameplayTests
    {
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        static BaggageStation Load()
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            return Object.FindAnyObjectByType<BaggageStation>();
        }

        [Test]
        public void FullCart_ShowsEveryBagInsideTheLoadSpaceAndRefusesAnotherBag()
        {
            var station=Load(); var airport=station.Airport; var fleet=airport.Baggage.Fleet(station);
            Assert.AreEqual(36,fleet.Cart.Capacity,"three columns, four rows and three layers of the actual bag props");
            airport.Baggage.Ledger.Add("full-cart","IN",fleet.Cart.Capacity,fleet.Cart.Capacity*15,true,fleet.Cart);
            Assert.Throws<ArgumentException>(()=>airport.Baggage.Ledger.Add("overflow","IN",1,15,true,fleet.Cart));
            var root=new GameObject("Physical luggage packing").transform; root.SetParent(airport.transform,false);
            var visuals=new BaggageLoadVisuals(root,station.bagPrefabs); visuals.Present(fleet);
            var bags=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.gameObject.activeInHierarchy).ToArray();
            Assert.AreEqual(fleet.Cart.Count,bags.Length,"each stored suitcase has its own visible prop");
            var pose=fleet.Train.CartAt(0); var load=station.cartType.baggage;
            foreach (var bag in bags)
                foreach (int x in new[] { -1,1 }) foreach (int y in new[] { -1,1 }) foreach (int z in new[] { -1,1 })
                {
                    var bounds=bag.sharedMesh.bounds;
                    var at=airport.transform.InverseTransformPoint(bag.transform.TransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z))));
                    var offset=new Vector2(at.x,at.z)-pose.position;
                    Assert.That(Vector2.Dot(offset,pose.Right),Is.InRange(load.loadMin.x-.002f,load.loadMax.x+.002f));
                    Assert.That(Vector2.Dot(offset,pose.facing),Is.InRange(load.loadMin.z-.002f,load.loadMax.z+.002f));
                    Assert.That(at.y,Is.InRange(load.loadMin.y-.002f,load.loadMax.y+.002f));
                }
        }

        [Test]
        public void SavedSandbox_CompletesAllBagsAndReturnsItsCrewAtGamePace()
        {
            var station=Load(); var airport=station.Airport;
            airport.Simulate(.1f); var job=airport.Baggage.Jobs.Single();
            var phases=new Dictionary<BaggageTurnaroundState,float>(); float elapsed=.1f;
            while (!job.Finished && elapsed<900)
            {
                phases.TryGetValue(job.State,out float duration); phases[job.State]=duration+.1f;
                airport.Simulate(.1f); elapsed+=.1f;
                foreach (var cart in job.Fleet.Carts) Assert.LessOrEqual(cart.Count,cart.Capacity);
            }
            Debug.Log($"GAMEPLAY BAGGAGE {elapsed:F1}s; "+string.Join(", ",phases.Select(p=>$"{p.Key}={p.Value:F1}s")));
            Assert.IsTrue(job.DepartureReady,job.State+": "+job.Waiting);
            Assert.AreEqual(120,job.Fleet.Arrivals.Count); Assert.AreEqual(180,job.Hold.Count);
            Assert.AreEqual(300,airport.Baggage.Ledger.Count); Assert.AreEqual(0,station.depot.Working);
            Assert.AreEqual(2,job.ArrivalTrips); Assert.AreEqual(3,job.DepartureTrips);
            Assert.That(elapsed,Is.InRange(240f,300f),"the complete default cycle should take four to five minutes at 1x");
        }
    }
}
