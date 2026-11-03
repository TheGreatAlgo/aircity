using System.Linq;
using AirCity.Aircraft;
using AirCity.EditorTools.Aircraft;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public class AircraftCargoDoorTests
    {
        [TestCase("Cessna172", 1)]
        [TestCase("Narrowbody", 2)]
        [TestCase("Widebody", 3)]
        [TestCase("DoubleDeck", 3)]
        [TestCase("Quadjet", 3)]
        public void EveryCargoPoint_HasAPaintedAnimatedDoorWithTheAuthoredPivot(string model, int count)
        {
            var type = AircraftSetup.AllTypes().Single(t => t.model == model);
            Assert.That(type.cargoDoors.Length, Is.EqualTo(count));
            Assert.That(type.PointsOfType(ServicePointType.CargoDoor).Select(p => p.name),
                Is.EquivalentTo(type.cargoDoors.Select(d => d.point)));
            var prefab = AircraftSetup.Prefab(type);
            Assert.That(prefab.GetComponent<AircraftCargoDoors>(), Is.Not.Null);
            var root = Object.Instantiate(prefab);
            try
            {
                var rig = root.GetComponent<AircraftCargoDoors>();
                foreach (var definition in type.cargoDoors)
                {
                    var part = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == definition.transform);
                    var rest = part.localRotation;
                    Assert.That(Vector3.Distance(root.transform.InverseTransformPoint(part.position), definition.pivot), Is.LessThan(0.01f));
                    Assert.That(part.GetComponent<MeshRenderer>().sharedMaterials.Any(m => m.name == "Body"), Is.True);
                    Assert.That(part.GetComponentsInChildren<Transform>().Any(t => t.name == definition.transform + "_LOD1"), Is.True);
                    Assert.That(type.TryGetPoint(definition.point, out var point), Is.True);
                    var sill = root.transform.TransformPoint(point.position);
                    var localSill = part.InverseTransformPoint(sill);
                    rig.Operate(definition.point, true, definition.seconds * 0.25f, out _);
                    float outward = Vector3.Dot(part.TransformPoint(localSill) - sill, root.transform.TransformDirection(point.normal));
                    Assert.That(definition.point == "Cargo_Bulk" ? -outward : outward, Is.GreaterThan(0.01f), "opening direction");
                    rig.Operate(definition.point, false, definition.seconds, out _);
                    Assert.That(rig.Operate(definition.point, true, definition.seconds / 2f, out _), Is.False);
                    Assert.That(rig.Progress(definition.point), Is.EqualTo(0.5f).Within(0.001f));
                    Assert.That(rig.AllClosed, Is.False);
                    Assert.That(Quaternion.Angle(rest, part.localRotation), Is.EqualTo(Mathf.Abs(definition.openAngle) / 2f).Within(0.1f));
                    Assert.That(rig.Operate(definition.point, true, definition.seconds, out _), Is.True);
                    Assert.That(rig.IsOpen(definition.point), Is.True);
                    Assert.That(Quaternion.Angle(rest, part.localRotation), Is.EqualTo(Mathf.Abs(definition.openAngle)).Within(0.1f));
                    Assert.That(rig.Operate(definition.point, false, definition.seconds, out _), Is.True);
                    Assert.That(Quaternion.Angle(rest, part.localRotation), Is.LessThan(0.01f));
                    Assert.That(rig.AllClosed, Is.True);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Doors_AreIndependentAndReverseWithoutSnapping()
        {
            var type = AircraftSetup.AllTypes().Single(t => t.model == "Widebody");
            var root = Object.Instantiate(AircraftSetup.Prefab(type));
            try
            {
                var rig = root.GetComponent<AircraftCargoDoors>();
                var a = type.cargoDoors[0]; var b = type.cargoDoors[1];
                rig.Operate(a.point, true, a.seconds * 0.75f, out _);
                Assert.That(rig.Progress(b.point), Is.Zero);
                rig.Operate(a.point, false, a.seconds * 0.25f, out _);
                Assert.That(rig.Progress(a.point), Is.EqualTo(0.5f).Within(0.001f));
                rig.Operate(b.point, true, b.seconds, out _);
                Assert.That(rig.Progress(a.point), Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(rig.IsOpen(b.point), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RemovedPanel_DoesNotLeaveTheLogicalDoorOpenAfterEquipmentWithdrawal()
        {
            var type = AircraftSetup.AllTypes().Single(t => t.model == "Narrowbody");
            var root = Object.Instantiate(AircraftSetup.Prefab(type));
            try
            {
                var rig = root.GetComponent<AircraftCargoDoors>();
                var definition = type.cargoDoors[0];
                Assert.That(rig.Operate(definition.point, true, 100f, out _), Is.True);
                var part = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == definition.transform);
                Object.DestroyImmediate(part.gameObject);
                Assert.That(rig.Operate(definition.point, false, 100f, out _), Is.True);
                Assert.That(rig.AllClosed, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MissingDoor_BlocksLoadingButAllowsCancelledEquipmentToReturn()
        {
            var type = AircraftSetup.AllTypes().Single(t => t.model == "Narrowbody");
            var root = new GameObject("headless missing rig");
            try
            {
                var aircraft = root.AddComponent<AirCity.Aircraft.Aircraft>(); aircraft.type = type;
                Assert.That(aircraft.CargoDoors.Operate("Cargo_Fwd", true, 100f, out var reason), Is.False);
                Assert.That(reason, Does.Contain("rig is missing"));
                Assert.That(aircraft.CargoDoors.Operate("Cargo_Fwd", false, 100f, out _), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
