using System.Collections.Generic;
using System.Linq;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.Vehicles
{
    public class BaggageEquipmentTests
    {
        readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        BaggageEquipmentRig Spawn(string model)
        {
            var prefab = VehicleSetup.Prefab(model);
            Assert.IsNotNull(prefab, $"Missing generated {model} prefab");
            var go = Object.Instantiate(prefab);
            spawned.Add(go);
            var rig = go.GetComponent<BaggageEquipmentRig>();
            Assert.IsNotNull(rig);
            rig.Apply();
            return rig;
        }

        static Transform Part(Component rig, string name) => rig.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

        [Test]
        public void ImportedEquipment_HasDistinctKindsAndMatchingWheelPivots()
        {
            foreach (var (model, kind) in new[] { ("BaggageTractor", VehicleKind.BaggageTractor),
                ("BaggageCart", VehicleKind.BaggageCart), ("BeltLoader", VehicleKind.BeltLoader) })
            {
                var rig = Spawn(model);
                var type = rig.type;
                Assert.AreEqual(kind, type.kind);
                Assert.IsTrue(type.IsBaggageEquipment);
                Assert.IsFalse(type.IsTug || type.IsBuilding);
                Assert.AreEqual(4, type.wheels.Length);
                foreach (var wheel in type.wheels)
                {
                    var position = rig.transform.InverseTransformPoint(Part(rig, wheel.name).position);
                    Assert.Less(Vector3.Distance(wheel.centre, position), 0.001f, $"{model}/{wheel.name}: metres and pivot conversion");
                    Assert.That(wheel.radius, Is.InRange(0.19f, 0.35f));
                }
            }
            Assert.That(VehicleSetup.Type("BaggageTractor").Length, Is.InRange(2.9f, 3.2f));
            Assert.That(VehicleSetup.Type("BaggageCart").Width, Is.InRange(1.6f, 1.7f));
            Assert.That(VehicleSetup.Type("BeltLoader").Length, Is.InRange(7.8f, 8.2f));
        }

        [Test]
        public void Trailer_CouplesAtTheActualEyeWhileSteeredAndKeepsWheelHierarchy()
        {
            var tractor = Spawn("BaggageTractor");
            tractor.transform.SetPositionAndRotation(new Vector3(20f, 0f, -30f), Quaternion.Euler(0f, 37f, 0f));
            var trailer = Spawn("BaggageCart");
            trailer.transform.rotation = Quaternion.Euler(0f, 12f, 0f);
            trailer.cartSteering = 25f;
            trailer.curtainOpen = 1f;
            trailer.displayLoad = true;
            trailer.CoupleBehind(tractor);
            Assert.Less(Vector3.Distance(tractor.RearHitchWorld, trailer.FrontHitchWorld), 0.0001f);
            Assert.AreSame(Part(trailer, "FrontAxle"), Part(trailer, "Wheel_FL").parent);
            Assert.AreSame(Part(trailer, "FrontAxle"), Part(trailer, "Drawbar").parent);
            Assert.IsTrue(Part(trailer, "Load").gameObject.activeSelf);
            var eye = trailer.FrontHitchWorld;
            for (int i = 0; i < 5; i++) trailer.Apply();
            Assert.Less(Vector3.Distance(eye, trailer.FrontHitchWorld), 0.0001f, "reapplying a pose must not accumulate rotations");
            trailer.drawbarLift = 75f;
            trailer.Apply();
            Assert.Greater(trailer.FrontHitchWorld.y, eye.y + 0.8f, "raised drawbar and parking-brake pose");
            Assert.Greater(trailer.transform.InverseTransformPoint(Part(trailer, "Drawbar").position).z, 2.62f,
                "the parking hinge must sit beyond the body front, rather than swinging through its wall");
        }

        [Test]
        public void Loader_EndpointsReachEveryRangeCornerWithoutStretchingArms()
        {
            var rig = Spawn("BeltLoader");
            var data = rig.type.baggage;
            var conveyor = Part(rig, "Conveyor");
            float restAngle = Mathf.Asin((data.beltFrontHeight - data.beltRear.y) / data.beltLength);
            var front = data.beltRear + new Vector3(0f, Mathf.Sin(restAngle), Mathf.Cos(restAngle)) * data.beltLength;
            var frontLocal = conveyor.InverseTransformPoint(rig.transform.TransformPoint(front));
            var rearLocal = conveyor.InverseTransformPoint(rig.transform.TransformPoint(data.beltRear));
            var arms = rig.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Lift") && t.name.Last() >= '0' && t.name.Last() <= '3').ToArray();
            var armScales = arms.Select(t => t.localScale).ToArray();
            foreach (float rear in new[] { data.rearMinHeight, data.rearMaxHeight })
                foreach (float height in new[] { data.frontMinHeight, 3.05f, data.frontMaxHeight })
                {
                    rig.beltRearHeight = rear; rig.beltFrontHeight = height;
                    rig.Apply();
                    var actualFront = rig.transform.InverseTransformPoint(conveyor.TransformPoint(frontLocal));
                    var actualRear = rig.transform.InverseTransformPoint(conveyor.TransformPoint(rearLocal));
                    Assert.That(actualFront.y, Is.EqualTo(height).Within(0.001f));
                    Assert.That(actualRear.y, Is.EqualTo(rear).Within(0.001f));
                    Assert.That(Vector3.Distance(actualRear, actualFront), Is.EqualTo(data.beltLength).Within(0.001f));
                    for (int i = 0; i < arms.Length; i++)
                    {
                        Assert.AreEqual(armScales[i], arms[i].localScale, "lift members must rotate, never stretch");
                        Assert.IsFalse(float.IsNaN(arms[i].position.y));
                    }
                }
        }

        [TestCase("BaggageCart")]
        [TestCase("BeltLoader")]
        public void MovingParts_HaveThreeReducingLodsThatFollowTheSamePivots(string model)
        {
            var rig = Spawn(model);
            var group = rig.GetComponent<LODGroup>();
            Assert.IsNotNull(group);
            var lods = group.GetLODs();
            Assert.AreEqual(3, lods.Length);
            var triangles = new long[3];
            for (int level = 0; level < lods.Length; level++)
            {
                foreach (var renderer in lods[level].renderers)
                {
                    Assert.IsTrue(renderer.name.EndsWith($"_LOD{level}"), $"{renderer.name}: no frozen full-detail mover at distance");
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    triangles[level] += (long)mesh.GetIndexCount(0) / 3;
                    if (!renderer.name.StartsWith("Body_"))
                        Assert.AreEqual(renderer.name.Substring(0, renderer.name.Length - 5), renderer.transform.parent.name);
                }
            }
            Assert.Less(triangles[1], triangles[0] * 0.4);
            Assert.Less(triangles[2], triangles[0] * 0.15);
        }

        [TestCase("BaggageCart")]
        [TestCase("BeltLoader")]
        public void PosedClone_PreservesItsReferencePoseAcrossDeserialization(string model)
        {
            var original = Spawn(model);
            original.cartSteering = 35f; original.curtainOpen = 1f; original.displayLoad = true;
            original.beltFrontHeight = 3.8f;
            original.Apply();
            var clone = Object.Instantiate(original.gameObject);
            spawned.Add(clone);
            var rig = clone.GetComponent<BaggageEquipmentRig>();
            rig.Apply();
            string name = model == "BaggageCart" ? "Drawbar" : "Conveyor";
            var expected = original.transform.InverseTransformPoint(Part(original, name).position);
            var actual = rig.transform.InverseTransformPoint(Part(rig, name).position);
            Assert.Less(Vector3.Distance(expected, actual), 0.001f);
            Assert.Less(Quaternion.Angle(Part(original, name).rotation, Part(rig, name).rotation), 0.01f);
        }

        [Test]
        public void BagPrefabs_AreIndependentLifeSizeProps()
        {
            foreach (string name in new[] { "Bag_HardLarge", "Bag_HardSmall", "Bag_Soft", "Bag_Duffel" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{VehicleSetup.Root}/BaggageProps/Props/{name}.prefab");
                Assert.IsNotNull(prefab, name);
                Assert.IsNull(prefab.GetComponent<Vehicle>());
                var go = Object.Instantiate(prefab);
                spawned.Add(go);
                var bounds = go.GetComponent<Renderer>().bounds;
                Assert.That(bounds.min.y, Is.InRange(-0.005f, 0.02f));
                Assert.That(bounds.size.y, Is.InRange(0.3f, 0.8f));
                Assert.That(bounds.size.x, Is.InRange(0.3f, 0.65f));
            }
        }
    }
}
