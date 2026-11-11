using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.Traffic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.Aircraft
{
    /// <summary>The supersonic type's afterburners: nozzle data from the generator, the flame component, and a
    /// departure that lights them at brake release and keeps them lit through the climb-out.</summary>
    public class AircraftAfterburnerTests
    {
        readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        static AircraftType Type(string model)
        {
            var t = AircraftSetup.AllTypes().FirstOrDefault(x => x.model == model);
            Assert.IsNotNull(t, $"{model}.aircraft not imported");
            return t;
        }

        [Test]
        public void Exhausts_AreFourAfterburningNozzlesPointingAft()
        {
            var sst = Type("Supersonic");
            var nozzles = sst.exhausts.Where(e => e.afterburner).ToArray();
            Assert.AreEqual(4, nozzles.Length, "four afterburning engines");
            Assert.AreEqual(2, nozzles.Count(e => e.position.x < 0f), "two under each wing");
            foreach (var e in nozzles)
            {
                Assert.Less(e.direction.z, -0.99f, $"{e.name}: the jet points aft");
                Assert.That(e.radius, Is.InRange(0.4f, 0.8f), e.name);
                Assert.Less(e.position.z, -8f, $"{e.name}: behind the main gear, at the wing's trailing edge");
                Assert.That(Mathf.Abs(e.position.x), Is.InRange(3f, sst.Wingspan * 0.3f), $"{e.name}: under the wing");
                Assert.That(e.position.y, Is.InRange(2f, 3.5f), e.name);
            }
            foreach (var t in AircraftSetup.AllTypes().Where(t => t.model != "Supersonic"))
                Assert.IsFalse(t.HasAfterburner, $"{t.model}: subsonic types have no afterburners");
        }

        [Test]
        public void Flames_LightWithReheatAndDieAway()
        {
            var type = Type("Supersonic");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AircraftSetup.Prefab(type));
            spawned.Add(go);
            var reheat = go.GetComponent<AircraftAfterburner>();
            Assert.IsNotNull(reheat);
            var flames = go.transform.Find(AircraftAfterburner.RendererName)?.GetComponent<MeshRenderer>();
            Assert.IsNotNull(flames, "flame renderer made on enable");
            Assert.AreEqual("AirCity/AircraftAfterburner", flames.sharedMaterial.shader.name);
            Assert.IsTrue((flames.gameObject.hideFlags & HideFlags.DontSave) != 0, "never saved into scenes or prefabs");
            Assert.IsFalse(flames.enabled, "out while parked");

            reheat.reheat = 1f;
            reheat.UpdateState(reheat.lightTime * 0.5f);
            Assert.AreEqual(0.5f, reheat.Level, 0.01f, "lights over lightTime");
            Assert.IsTrue(flames.enabled);
            reheat.UpdateState(reheat.lightTime);
            Assert.AreEqual(1f, reheat.Level);
            reheat.reheat = 0f;
            reheat.UpdateState(reheat.cancelTime);
            Assert.AreEqual(0f, reheat.Level);
            Assert.IsFalse(flames.enabled, "cancelled: out again");

            // the shared mesh sits on the nozzle exits (the shader moves it out along the jet); its bounds hold the plume
            var mesh = AircraftAfterburner.FlameMesh(type);
            Assert.AreSame(mesh, AircraftAfterburner.FlameMesh(type), "one mesh per type");
            var exits = type.exhausts.Where(e => e.afterburner).Select(e => e.position).ToArray();
            Assert.IsTrue(mesh.vertices.All(v => exits.Any(p => (p - v).sqrMagnitude < 1e-6f)), "every vertex on a nozzle exit");
            float plume = type.exhausts.Min(e => e.position.z - e.radius * AircraftAfterburner.PlumeLength);
            Assert.LessOrEqual(mesh.bounds.min.z, plume, "culling bounds reach the end of the plume");
            Assert.Less(AircraftAfterburner.GlowPosition(type).z, exits.Max(p => p.z), "the glow light is behind the nozzles");
        }

        [Test]
        public void Departure_LightsTheAfterburnersAtBrakeReleaseOnly()
        {
            var airport = LongRunwayAirport();
            var prefab = AircraftSetup.Prefab(Type("Supersonic"));
            var go = Object.Instantiate(prefab, new Vector3(0f, 0f, -200f), Quaternion.LookRotation(Vector3.right));
            spawned.Add(go);
            var sst = go.GetComponent<AirCity.Aircraft.Aircraft>();
            var reheat = go.GetComponent<AircraftAfterburner>();
            var flight = airport.Traffic.Depart(sst, out string problem);
            Assert.IsNotNull(flight, problem);

            bool litEarly = false;
            float t = RunUntil(airport, () => flight.Phase >= FlightPhase.TakeoffRoll, 900f,
                () => litEarly |= flight.Phase < FlightPhase.TakeoffRoll && reheat.reheat > 0f);
            Assert.AreEqual(FlightPhase.TakeoffRoll, flight.Phase, $"{flight.Status} after {t:0} s");
            Assert.IsFalse(litEarly, "no reheat taxiing out or lined up");
            Assert.AreEqual(1f, reheat.reheat, "lit at brake release");
            Assert.IsTrue(flight.Reheat);
            reheat.UpdateState(reheat.lightTime);
            Assert.AreEqual(1f, reheat.Level);

            RunUntil(airport, () => flight.Phase == FlightPhase.Climb, 120f);
            Assert.AreEqual(FlightPhase.Climb, flight.Phase, flight.Status);
            Assert.AreEqual(1f, reheat.reheat, "still burning in the climb-out");
            RunUntil(airport, () => flight.Finished, 300f);
            Assert.AreEqual(FlightPhase.Gone, flight.Phase, flight.Status);
        }

        /// <summary>A 3.8 km runway along z = 0 (wind from the west, so departures roll west) and a parallel taxiway at
        /// z = -200 joining it at both ends. No stands: the aircraft starts on the taxiway.</summary>
        Airport LongRunwayAirport()
        {
            AirfieldSetup.CreateMaterials();
            var go = new GameObject("Long runway airport");
            go.SetActive(false);
            var airport = go.AddComponent<Airport>();
            AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 0f;
            airport.windFrom = 270f;
            go.SetActive(true);
            spawned.Add(go);
            var r = airport.AddRunway(new Vector3(-1900f, 0f, 0f), new Vector3(1900f, 0f, 0f), PavementSurface.Concrete, 45f);
            var net = airport.Taxiways;
            int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0f, z), on);
            int w = Node(-1850f, 0f, r), a = Node(-1850f, -200f), b = Node(1850f, -200f), e = Node(1850f, 0f, r);
            foreach (var (x, y) in new[] { (w, a), (a, b), (b, e) }) net.Connect(x, y, 23f, PavementSurface.Asphalt);
            airport.BuildGround();
            net.Rebuild();
            r.Rebuild();
            return airport;
        }

        static float RunUntil(Airport airport, System.Func<bool> done, float maxSeconds, System.Action each = null)
        {
            float t = 0f;
            while (t < maxSeconds && !done())
            {
                airport.Simulate(1f);
                each?.Invoke();
                t += 1f;
            }
            return t;
        }
    }
}
