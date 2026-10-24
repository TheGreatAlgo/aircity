using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.EditorTools.Aircraft;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.Aircraft
{
    /// <summary>Gameplay data and the components built on it.</summary>
    public class AircraftTypeTests
    {
        readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
            AircraftLiveryApplier.ClearSharedCache();
        }

        static AircraftType Type(string model)
        {
            var t = AircraftSetup.AllTypes().FirstOrDefault(x => x.model == model);
            Assert.IsNotNull(t, $"{model}.aircraft not imported");
            return t;
        }

        GameObject Spawn(AircraftType type)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AircraftSetup.Prefab(type));
            spawned.Add(go);
            return go;
        }

        [Test]
        public void Dimensions_MatchTheRealAircraftClass()
        {
            var cessna = Type("Cessna172");
            var jet = Type("Narrowbody");
            Debug.Log($"Cessna172: {cessna.Wingspan:F2} x {cessna.Length:F2} x {cessna.Height:F2} m, code {cessna.SizeCode}, " +
                      $"wheelbase {cessna.Wheelbase:F2}, min turn {cessna.MinTurnRadius:F1} m; Narrowbody: {jet.Wingspan:F2} x " +
                      $"{jet.Length:F2} x {jet.Height:F2} m, code {jet.SizeCode}, wheelbase {jet.Wheelbase:F2}, min turn {jet.MinTurnRadius:F1} m");
            Assert.That(cessna.Wingspan, Is.InRange(10.5f, 11.5f));     // C172: 11.0 m
            Assert.That(cessna.Length, Is.InRange(7.9f, 8.6f));         // 8.28 m
            Assert.AreEqual('A', cessna.SizeCode);
            Assert.That(jet.Wingspan, Is.InRange(34f, 36f));            // A320 with sharklets: 35.8 m
            Assert.That(jet.Length, Is.InRange(36.5f, 38.5f));          // 37.57 m
            Assert.That(jet.Height, Is.InRange(11f, 12.5f));            // 11.76 m
            Assert.AreEqual('C', jet.SizeCode);
            Assert.That(jet.Wheelbase, Is.InRange(12f, 13.5f));         // 12.64 m
            // origin under the main gear, nose forwards (+Z), on the ground
            foreach (var t in new[] { cessna, jet })
            {
                Assert.Greater(t.bounds.max.z, t.Wheelbase, $"{t.model}: the nose should be ahead of the nose gear");
                Assert.That(t.bounds.min.y, Is.InRange(-0.05f, 0.05f), $"{t.model}: tyres should touch y = 0");
            }
        }

        [Test]
        public void Model_MatchesTypeData()
        {
            // the gameplay data (wheels, doors, lights) and the imported mesh must agree on the axes:
            // a model that faces -Z would put landing lights at the tail and wingtip lights metres off the wing
            foreach (var model in new[] { "Cessna172", "Narrowbody" })
            {
                var type = Type(model);
                var go = Spawn(type);
                var byName = go.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                foreach (var w in type.wheels)
                {
                    Assert.IsTrue(byName.TryGetValue(w.name, out var t), $"{model}: wheel {w.name} in the model");
                    var local = go.transform.InverseTransformPoint(t.position);
                    Assert.Less(Vector3.Distance(local, w.position), 0.05f, $"{model}: {w.name} at {local}, data says {w.position}");
                }
                var body = go.GetComponentsInChildren<Renderer>().First(r => r.name == "Body_LOD0");
                var meshBounds = body.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.Greater(meshBounds.max.z, type.Wheelbase, $"{model}: the mesh's nose should point along +Z");
            }
        }

        [Test]
        public void ServicePoints_AreOnTheCorrectSide()
        {
            var jet = Type("Narrowbody");
            // airliners board from the left (port = -X in Unity) and load cargo on the right
            foreach (var p in jet.PointsOfType(ServicePointType.PassengerDoor))
                Assert.AreEqual(p.name.Contains("_L") ? -1f : 1f, Mathf.Sign(p.position.x), p.name);
            foreach (var p in jet.PointsOfType(ServicePointType.CargoDoor))
                Assert.Greater(p.position.x, 0f, p.name);
            Assert.IsTrue(jet.TryGetPoint("Door_L1", out var l1));
            Assert.That(l1.position.y, Is.InRange(2.5f, 3.6f), "L1 sill height (jet bridges need ~3.4 m)");
            Assert.Greater(l1.position.z, jet.bounds.max.z - jet.Length * 0.25, "L1 is in the front quarter");
            Assert.IsTrue(Type("Cessna172").PointsOfType(ServicePointType.Fuel).All(p => p.position.y > 1.9f),
                "a Cessna is fuelled on top of its high wing");
        }

        [Test]
        public void NavigationLights_RedOnTheLeft()
        {
            foreach (var t in new[] { Type("Cessna172"), Type("Narrowbody") })
            {
                var red = t.lights.Single(l => l.type == AircraftLightType.Navigation && l.color.r > 0.9f && l.color.g < 0.2f);
                var green = t.lights.Single(l => l.type == AircraftLightType.Navigation && l.color.g > 0.9f && l.color.r < 0.2f);
                Assert.Less(red.position.x, -t.Wingspan * 0.4f, $"{t.model}: red nav light on the left wingtip");
                Assert.Greater(green.position.x, t.Wingspan * 0.4f, $"{t.model}: green nav light on the right wingtip");
                Assert.IsTrue(t.lights.Any(l => l.type == AircraftLightType.Beacon), $"{t.model}: beacon");
            }
        }

        [Test]
        public void Beacons_AlternateTopAndBottom()
        {
            var jet = Type("Narrowbody");
            var beacons = jet.lights.Where(l => l.type == AircraftLightType.Beacon).ToArray();
            Assert.AreEqual(2, beacons.Length, "a jet has a top and a bottom beacon");
            var top = beacons.Single(l => l.direction.y > 0.5f);
            var bottom = beacons.Single(l => l.direction.y < -0.5f);
            Assert.AreEqual(0.5f, Mathf.Abs(AircraftLights.PhaseOffset(top) - AircraftLights.PhaseOffset(bottom)), 1e-4f,
                "half a second apart");

            // and the glow mesh carries it (uv2.x per vertex, 4 vertices per light, in light order)
            var phases = new List<Vector2>();
            AircraftLights.GlowMesh(jet).GetUVs(2, phases);
            int iTop = System.Array.IndexOf(jet.lights, top), iBottom = System.Array.IndexOf(jet.lights, bottom);
            Assert.AreNotEqual(phases[iTop * 4].x, phases[iBottom * 4].x, "the shader sees different phases");

            var cessna = Type("Cessna172").lights.Single(l => l.type == AircraftLightType.Beacon);
            Assert.AreEqual(0f, AircraftLights.PhaseOffset(cessna), "a single top beacon keeps phase 0");
        }

        [Test]
        public void Prefab_HasEveryGameplayComponent()
        {
            foreach (var model in new[] { "Cessna172", "Narrowbody" })
            {
                var type = Type(model);
                var go = Spawn(type);
                var aircraft = go.GetComponent<AirCity.Aircraft.Aircraft>();
                Assert.IsNotNull(aircraft, model);
                Assert.AreSame(type, aircraft.type);
                Assert.IsNotNull(aircraft.outlineMaterial, model);
                var lights = go.GetComponent<AircraftLights>();
                Assert.IsNotNull(lights, model);
                Assert.IsNotNull(lights.glowMaterial, model);
                var motion = go.GetComponent<AircraftGroundMotion>();
                Assert.AreEqual(type.wheels.Length, motion.wheels.Length, $"{model}: every wheel found by name");
                Assert.IsNotNull(motion.noseGear, $"{model}: steerable nose gear");
                var collider = go.GetComponentInChildren<MeshCollider>();
                Assert.IsNotNull(collider, $"{model}: picking collider");
                Assert.AreEqual(LayerMask.NameToLayer(AirCity.Aircraft.Aircraft.LayerName), collider.gameObject.layer);
                var shadow = go.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "GroundShadow");
                Assert.IsNotNull(shadow, $"{model}: contact shadow");
                Assert.AreEqual("AirCity/AircraftGroundShadow", shadow.sharedMaterial.shader.name);
                var surfaces = go.GetComponent<AircraftControlSurfaces>();
                Assert.IsNotEmpty(surfaces.elevators, model);
                Assert.IsNotEmpty(surfaces.flapsLeft, model);
                Assert.IsNotEmpty(surfaces.rudders, model);
            }
        }

        [Test]
        public void Picking_HitsTheAircraft()
        {
            foreach (var model in new[] { "Cessna172", "Narrowbody" })
            {
                var type = Type(model);
                var go = Spawn(type);
                go.transform.position = new Vector3(500f, 0f, 0f);
                Physics.SyncTransforms();
                var centre = go.transform.TransformPoint(type.bounds.center);
                Assert.IsTrue(AirCity.Aircraft.Aircraft.TryPick(new Ray(centre + Vector3.up * 50f, Vector3.down), out var hit), model);
                Assert.AreSame(go.GetComponent<AirCity.Aircraft.Aircraft>(), hit);
                Assert.IsFalse(AirCity.Aircraft.Aircraft.TryPick(new Ray(centre + new Vector3(0f, 50f, type.Length), Vector3.down), out _),
                    $"{model}: a ray beside the aircraft must miss");
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GroundMotion_SpinsWheelsAndSteers()
        {
            var type = Type("Narrowbody");
            var go = Spawn(type);
            var motion = go.GetComponent<AircraftGroundMotion>();
            var wheel = motion.wheels[0].transform;
            var rest = wheel.localRotation;
            motion.Drive(5f, 1f);
            motion.Animate(0.1f);
            Assert.AreNotEqual(rest, wheel.localRotation, "wheel turned");
            Assert.AreEqual(type.maxSteeringAngle, Quaternion.Angle(motion.noseGearRest, motion.noseGear.localRotation), 0.5f);
            Assert.That(type.MinTurnRadius, Is.InRange(2f, 6f), "A320: ~4 m to the main-gear midpoint at 75 deg");
        }

        [Test]
        public void Logo_IsPaintedOnTheFin()
        {
            foreach (var model in new[] { "Cessna172", "Narrowbody" })
            {
                var type = Type(model);
                var example = type.examples.First(e => e.logo != null);
                var withLogo = new AircraftLivery
                {
                    style = example.style, baseColor = example.baseColor, color1 = example.color1,
                    color2 = example.color2, color3 = example.color3, logo = example.logo,
                };
                var without = withLogo.Clone();
                without.logo = null;
                var a = Pixels(AircraftLiveryCompositor.GetBodyTexture(type, withLogo));
                var b = Pixels(AircraftLiveryCompositor.GetBodyTexture(type, without));
                int changed = 0;
                for (int i = 0; i < a.Length; i++)
                    if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 60) changed++;
                float frac = changed / (float)a.Length;
                Debug.Log($"{model}: logo covers {frac:P2} of the atlas");
                Assert.That(frac, Is.InRange(0.001f, 0.05f), $"{model}: logo painted, and only on the fin");
            }
        }

        [Test]
        public void QualitySettings_DoNotCapLods()
        {
            // a capped maximumLODLevel (e.g. left behind by SceneCapture -captureLod) draws every aircraft at its
            // far LOD, even close up
            var names = QualitySettings.names;
            int current = QualitySettings.GetQualityLevel();
            try
            {
                for (int i = 0; i < names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    Assert.AreEqual(0, QualitySettings.maximumLODLevel, $"quality level '{names[i]}' caps the LOD level");
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(current, false);
            }
        }

        [Test]
        public void Dirt_SharesMaterialsPerStep()
        {
            var type = Type("Cessna172");
            var a = Spawn(type).GetComponent<AircraftLiveryApplier>();
            var b = Spawn(type).GetComponent<AircraftLiveryApplier>();
            var livery = AircraftSetup.AllLiveries().First();
            a.SetLivery(livery);
            b.SetLivery(livery);
            a.SetDirt(0.0f);
            b.SetDirt(1.0f);
            Assert.AreEqual(0f, a.QuantizedDirt);
            Assert.AreEqual(1f, b.QuantizedDirt);
            Assert.AreSame(a.CurrentBodyTexture, b.CurrentBodyTexture, "dirt must not need its own livery texture");
        }

        static Color32[] Pixels(Texture2D tex)
        {
            var copy = AircraftLiveryCompositor.ReadBack(tex, false, new RectInt(0, 0, tex.width, tex.height));
            var px = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            return px;
        }
    }
}
