using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public class SandboxAircraftTests
    {
        Airport airport;

        [SetUp]
        public void Load()
        {
            EditorSceneManager.OpenScene(AircraftSetup.SandboxScene, OpenSceneMode.Single);
            airport = Object.FindFirstObjectByType<Airport>();
        }

        [TearDown]
        public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void SavedSandbox_HasOneOfEveryTypeOnDifferentConnectedStands()
        {
            var aircraft = airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>();
            var types = AircraftSetup.AllTypes();
            Assert.That(aircraft.Length, Is.EqualTo(types.Length));
            CollectionAssert.AreEquivalent(types, aircraft.Select(a => a.type));
            CollectionAssert.AreEquivalent(types, airport.aircraftTypes);
            Assert.That(airport.gameObject.scene.GetRootGameObjects().Any(r => r.name == "Aircraft"), Is.False);
            var net = airport.Taxiways;
            var homes = aircraft.Select(a =>
            {
                var at = airport.transform.InverseTransformPoint(a.transform.position);
                var home = net.Layouts().Single(l => !l.stand.DeIcing &&
                    Vector2.Distance(l.ParkPosition(a.type), new Vector2(at.x, at.z)) < 0.1f);
                Assert.That(home.edge, Is.Not.Null, a.name);
                Assert.That(net.CheckStand(home.stand, a.type), Is.EqualTo(StandProblem.None), a.name);
                Assert.That(net.Fits(home), Is.True, a.name);
                Assert.That(Vector3.Dot(a.transform.forward, airport.transform.TransformDirection(
                    new Vector3(home.nose.x, 0f, home.nose.y))), Is.GreaterThan(0.99f), a.name);
                return home.stand;
            }).ToArray();
            Assert.That(homes.Distinct().Count(), Is.EqualTo(aircraft.Length));
            var oldRamp = ApronArea.Rect(new Vector2(600f, -440f), new Vector2(300f, 560f), 0f, PavementSurface.Concrete);
            Assert.That(airport.aprons.Any(a => a.outline.SequenceEqual(oldRamp.outline)), Is.False);
            var station = airport.GetComponentsInChildren<BaggageStation>().Single();
            var narrowbody = aircraft.Single(a => a.type == station.automaticHold.aircraft);
            airport.Simulate(0.1f);
            Assert.That(airport.Baggage.JobOf(narrowbody), Is.Not.Null, station.Problem);
        }

        [Test]
        public void Refresh_RemovesDuplicatesRestoresMissingTypeAndKeepsBaggageAircraftAndLivery()
        {
            var original = airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>();
            var baggage = original.Single(a => a.name == "Narrowbody baggage turnaround");
            var widebody = original.Single(a => a.type.model == "Widebody");
            var applier = widebody.GetComponent<AircraftLiveryApplier>();
            applier.liveryAsset = AircraftSetup.AllLiveries().Last();
            var livery = applier.liveryAsset;
            var at = baggage.transform.position;
            Object.DestroyImmediate(original.Single(a => a.type.Light).gameObject);
            var display = new GameObject("Aircraft");
            foreach (var type in new[] { baggage.type, widebody.type })
                PrefabUtility.InstantiatePrefab(AircraftSetup.Prefab(type), display.transform);

            int count = AirfieldSetup.RefreshAircraft(airport);
            Assert.That(AirfieldSetup.RefreshAircraft(airport), Is.EqualTo(count));
            var refreshed = airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>();
            Assert.That(count, Is.EqualTo(AircraftSetup.AllTypes().Length));
            Assert.That(refreshed.Length, Is.EqualTo(count));
            Assert.That(refreshed.Select(a => a.type).Distinct().Count(), Is.EqualTo(count));
            Assert.That(refreshed.Single(a => a.type == baggage.type), Is.SameAs(baggage));
            Assert.That(baggage.transform.position, Is.EqualTo(at));
            Assert.That(refreshed.Single(a => a.type == widebody.type), Is.SameAs(widebody));
            Assert.That(applier.liveryAsset, Is.SameAs(livery));
            Assert.That(display == null, Is.True);
        }
    }
}
