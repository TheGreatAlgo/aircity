using System.Linq;
using AirCity.Aircraft;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Vehicles
{
    /// <summary>The control towers (ArtSource/Vehicles/Tower*): their type data, prefabs and lights.</summary>
    public class TowerTests
    {
        static VehicleType[] Types() => VehicleSetup.Towers.Select(VehicleSetup.Type).ToArray();

        [Test]
        public void Towers_ImportAsBuildingsInThreeSizes()
        {
            var types = Types();
            for (int i = 0; i < types.Length; i++)
            {
                Assert.IsNotNull(types[i], $"{VehicleSetup.Towers[i]}.vehicle not imported");
                Assert.AreEqual(VehicleKind.Tower, types[i].kind);
                Assert.IsTrue(types[i].IsBuilding);
                Assert.AreEqual(0f, types[i].bounds.min.y, 0.05f, $"{types[i].name} stands on the ground");
            }
            float[] heights = types.Select(t => t.bounds.max.y).ToArray();
            Assert.That(heights[0], Is.InRange(9f, 16f), "a grass airfield's tower: a cab on a two-storey building");
            Assert.That(heights[1], Is.InRange(30f, 45f), "a regional airport's tower");
            Assert.That(heights[2], Is.InRange(90f, 110f), "a hub's tower, Schiphol-sized");
        }

        [Test]
        public void Towers_HaveAnObstructionLightOnTop()
        {
            foreach (var t in Types())
            {
                var top = t.lights.OrderByDescending(l => l.position.y).First();
                Assert.AreEqual(VehicleLightType.Marker, top.type, $"{t.name}: the highest light is an obstruction light");
                Assert.Greater(top.color.r, 0.5f);
                Assert.Less(top.color.g, 0.2f, "red");
                Assert.Less(t.bounds.max.y - top.position.y, 0.5f, $"{t.name}: nothing stands above it");
            }
            var medium = VehicleSetup.Type("TowerMedium");
            var beacon = medium.lights.Where(l => l.type == VehicleLightType.Beacon).ToArray();
            Assert.AreEqual(2, beacon.Length, "the aerodrome beacon: white and green");
            Assert.AreEqual(0.5f, Mathf.Abs(beacon[0].phase - beacon[1].phase), 1e-3f, "half a turn apart");
        }

        [Test]
        public void TowerPrefabs_TurnTheBeaconOnlyAtNight()
        {
            float glow = AircraftLights.Glow;
            try
            {
                foreach (var model in VehicleSetup.Towers)
                {
                    var prefab = VehicleSetup.Prefab(model);
                    Assert.IsNotNull(prefab, $"{model}.prefab (AirCity ▸ Vehicles ▸ Create Missing Prefabs)");
                    Assert.IsNull(prefab.GetComponent<Vehicle>(), "a building, not a vehicle");
                    var lights = prefab.GetComponent<VehicleLights>();
                    Assert.IsNotNull(lights);
                    AircraftLights.Glow = 0f;
                    Assert.AreEqual(0u, lights.StateBits & 0xFFu, $"{model}: everything off by day");
                    AircraftLights.Glow = 1f;
                    Assert.AreNotEqual(0u, lights.StateBits & 2u, $"{model}: the beacon turns at night");
                    Assert.AreNotEqual(0u, lights.StateBits & 8u, $"{model}: obstruction lights on at night");
                }
            }
            finally
            {
                AircraftLights.Glow = glow;
            }
        }
    }
}
