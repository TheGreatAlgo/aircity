using System.IO;
using System.Linq;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.Vehicles
{
    public class DeicingEquipmentTests
    {
        [Test]
        public void GeneratedDeicer_HasMetreScaleWheelPivotsAndArticulatedLods()
        {
            var type = VehicleSetup.Type("DeicingTruck");
            Assert.IsNotNull(type);
            Assert.AreEqual(VehicleKind.DeicingTruck, type.kind);
            Assert.IsFalse(type.IsBuilding || type.IsTug || type.IsBaggageEquipment);
            Assert.That(type.Length, Is.InRange(11.9f, 12.2f), "Includes the enclosed tip cabin and forward spray monitor.");
            Assert.That(type.Width, Is.InRange(2.5f, 3.2f), "Includes extended cab mirrors.");
            Assert.AreEqual(4, type.wheels.Length);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AirCity/Vehicles/DeicingTruck/Models/DeicingTruck.fbx");
            Assert.IsNotNull(model);
            var root = Object.Instantiate(model);
            try
            {
                var transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (var wheel in type.wheels)
                {
                    var pivot = transforms.Single(t => t.name == wheel.name);
                    Assert.That(Vector3.Distance(root.transform.InverseTransformPoint(pivot.position), wheel.centre), Is.LessThan(.001f));
                }
                foreach (string name in new[] { "BoomYaw", "BoomLift", "BoomElbow", "BoomExtend", "BoomCabin", "SprayNozzle" })
                {
                    var pivot = transforms.Single(t => t.name == name);
                    Assert.AreEqual(3, pivot.GetComponentsInChildren<MeshRenderer>().Length, name);
                }
                var lift = transforms.Single(t => t.name == "BoomLift");
                var elbow = transforms.Single(t => t.name == "BoomElbow");
                var cabin = transforms.Single(t => t.name == "BoomCabin");
                var nozzle = transforms.Single(t => t.name == "SprayNozzle");
                var assets = Resources.Load<DeicingAssets>("DeicingAssets");
                Assert.IsNotNull(assets);
                Assert.That(Vector3.Distance(lift.position, elbow.position), Is.EqualTo(assets.boomLowerLength).Within(.005f));
                Assert.That(Vector3.Distance(elbow.position, cabin.position), Is.EqualTo(assets.boomUpperLength).Within(.005f));
                Assert.That(Vector3.Distance(lift.position, cabin.position), Is.EqualTo(assets.boomRestReach).Within(.005f));
                Assert.That(Vector3.Distance(root.transform.InverseTransformPoint(lift.position), assets.boomLiftPivot), Is.LessThan(.005f));
                Assert.That(Vector3.Distance(cabin.InverseTransformPoint(nozzle.position), assets.cabinNozzleOffset), Is.LessThan(.005f));
                Assert.That(root.transform.InverseTransformPoint(cabin.position).z, Is.GreaterThan(8f),
                    "The enclosed operator cabin belongs at the boom tip, clear of the slewing pedestal.");
                var group = root.GetComponent<LODGroup>();
                Assert.IsNotNull(group);
                var lods = group.GetLODs();
                Assert.AreEqual(3, lods.Length);
                long[] indices = lods.Select(l => l.renderers.Sum(r => (long)r.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0))).ToArray();
                Assert.Less(indices[1], indices[0] * .4f);
                Assert.Less(indices[2], indices[0] * .15f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void StorageAndResources_ProvideDistinctStationaryTankAndDrivenTruck()
        {
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            Assert.IsNotNull(assets, "Run DeicingEquipmentSetup.CreateAssets.");
            Assert.IsNotNull(assets.truckPrefab); Assert.IsNotNull(assets.tankPrefab);
            Assert.IsNotNull(assets.sprayMaterial);
            Assert.AreEqual(VehicleKind.DeicingTruck, assets.truckType.kind);
            Assert.AreEqual(VehicleKind.DeicingTank, assets.tankType.kind);
            Assert.IsTrue(assets.tankType.IsBuilding);
            Assert.IsFalse(assets.tankType.IsBaggageEquipment);
            Assert.That(assets.tankType.Width, Is.InRange(4.8f, 5f));
            Assert.That(assets.tankType.Length, Is.InRange(8.5f, 8.7f));
            Assert.That(assets.tankType.bounds.min.y, Is.GreaterThanOrEqualTo(-.001f));
            Assert.IsNull(assets.tankPrefab.GetComponent<Vehicle>(), "Fluid storage must never join a maintenance team.");
            Assert.That(assets.boomLowerLength + assets.boomUpperLength + assets.boomExtension,
                Is.EqualTo(10.44475f).Within(.001f));
            Assert.IsTrue(assets.truckPrefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "BoomCabin"),
                "Existing prefabs must migrate to the enclosed tip-cabin model.");
        }

        [Test]
        public void DeliveryTanker_HasSixMetreScaleWheelsAndAnArticulatedReel()
        {
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            Assert.IsNotNull(assets); Assert.IsNotNull(assets.tankerPrefab); Assert.IsNotNull(assets.tankerType);
            Assert.AreEqual(VehicleKind.DeicingTanker, assets.tankerType.kind);
            Assert.IsFalse(assets.tankerType.IsBuilding || assets.tankerType.IsTug || assets.tankerType.IsBaggageEquipment);
            Assert.AreEqual(6, assets.tankerType.wheels.Length);
            Assert.AreEqual(2, assets.tankerType.wheels.Count(w => w.steerable));
            Assert.That(assets.tankerType.Length, Is.InRange(11.4f,11.8f));
            Assert.That(assets.tankerType.Width, Is.InRange(3.1f,3.2f));
            var root = Object.Instantiate(assets.tankerPrefab);
            try
            {
                var parts = root.GetComponentsInChildren<Transform>(true);
                foreach (var wheel in assets.tankerType.wheels)
                {
                    var pivot = parts.Single(t => t.name == wheel.name);
                    Assert.Less(Vector3.Distance(root.transform.InverseTransformPoint(pivot.position),wheel.centre),.001f);
                }
                var reel = parts.Single(t => t.name == "HoseReel");
                Assert.AreEqual(3,reel.GetComponentsInChildren<MeshRenderer>().Length);
                Assert.AreEqual(3,root.GetComponent<LODGroup>().GetLODs().Length);
                Assert.Less(Vector3.Distance(assets.tankerHosePoint,new Vector3(-1.25f,1f,-2f)),.001f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RecyclingUpgrade_IsACompactStationarySkidWithSeparateDeliveryInlet()
        {
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            Assert.IsNotNull(assets); Assert.IsNotNull(assets.recyclerPrefab); Assert.IsNotNull(assets.recyclerType);
            Assert.AreEqual(VehicleKind.DeicingRecycler, assets.recyclerType.kind);
            Assert.IsTrue(assets.recyclerType.IsBuilding);
            Assert.AreEqual(0,assets.recyclerType.wheels.Length);
            Assert.That(assets.recyclerType.Width,Is.InRange(2.59f,2.61f));
            Assert.That(assets.recyclerType.Length,Is.InRange(4.59f,4.61f));
            Assert.That(assets.recyclerType.bounds.center.z,Is.EqualTo(0f).Within(.001f));
            Assert.IsNull(assets.recyclerPrefab.GetComponent<Vehicle>());
            Assert.That(assets.tankDeliveryPoint.x,Is.GreaterThan(2f),"Supplier inlet is opposite the truck refill reel.");
            Assert.Less(Vector3.Distance(assets.tankDeliveryPoint,new Vector3(2.42f,.86f,1.51f)),.001f);
        }

        [Test]
        public void DeliveryTankerAtlas_ContainsBakedPaintAndDarkRunningGear()
        {
            var texture = new Texture2D(2,2,TextureFormat.RGB24,false);
            try
            {
                Assert.IsTrue(ImageConversion.LoadImage(texture,File.ReadAllBytes(
                    "Assets/AirCity/Vehicles/DeicingTanker/Textures/DeicingTanker_Albedo.png")));
                var pixels = texture.GetPixels32();
                Assert.IsTrue(pixels.Any(c => c.r < 90 && c.g < 110 && c.b < 130), "Empty/grey atlas lost tyres and fleet trim.");
                Assert.IsTrue(pixels.Any(c => c.r > 200 && c.g > 200 && c.b > 190), "The white cab and vessel must survive the bake.");
            }
            finally { Object.DestroyImmediate(texture); }
        }
    }
}
