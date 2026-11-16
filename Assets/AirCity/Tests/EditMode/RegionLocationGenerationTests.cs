using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class RegionLocationGenerationTests
    {
        [Test]
        public void DownloadedLocationPreservesNorthUpFootprintFreshwaterAndDetachedMetadata()
        {
            const int resolution = ValleyWorld.Resolution;
            var data = new RegionTerrainData { latitude = 45.12345, longitude = -123.45678, extent = 8000f,
                name = "Selected coastal landscape", hasOcean = true, elevationSha256 = "elevation-fixture", coverSha256 = "cover-fixture",
                elevations = new float[resolution * resolution], cover = new byte[resolution * resolution] };
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * resolution + x;
                bool marine = x < 120, river = z >= 143 && z <= 145 && x >= 120 && x <= 260;
                bool lake = x >= 320 && x <= 344 && z >= 250 && z <= 274;
                data.cover[i] = marine || river || lake ? (byte)80 : (byte)30;
                data.elevations[i] = marine ? -900f : river ? (x - 120) * .2f : lake ? 120f : 80f + x * .1f + z * .05f;
            }
            var elevations = (float[])data.elevations.Clone(); var cover = (byte[])data.cover.Clone();
            var state = CareerState.NewLocation(data); var world = state.world;
            Assert.IsTrue(world.Validate(out string why), why);
            Assert.IsTrue(world.HasOcean); Assert.IsTrue(world.HasInlandWater);
            Assert.IsTrue(world.hasCoordinates); Assert.AreEqual(data.latitude, world.latitude); Assert.AreEqual(data.longitude, world.longitude);
            Assert.AreEqual(data.extent, world.sourceExtent); Assert.AreEqual(data.name, world.landscapeSource);
            int west = 100 * resolution + 30, east = 100 * resolution + 400, north = 400 * resolution + 400;
            Assert.Less(world.heights[west], world.SeaLevel, "Deep negative boundary water still belongs to the ocean.");
            Assert.AreEqual(0f, world.InlandSurfaceHeights[west], "Marine pixels must not become a second freshwater mesh.");
            Assert.AreEqual(data.elevations[east] * world.elevationScale + world.elevationOffset, world.heights[east], .0001f);
            Assert.Greater(world.heights[north], world.heights[east], "The source's north/south direction must survive generation.");
            int upstream = 144 * resolution + 240;
            Assert.Greater(world.waterHeights[upstream], world.SeaLevel + 20f,
                "A river connected to the ocean must retain its uphill freshwater profile.");
            Assert.Greater(world.InlandSurfaceHeights[upstream], world.SeaLevel + 20f);
            int lakeCentre = 262 * resolution + 332;
            Assert.AreEqual(120f * world.elevationScale + world.elevationOffset, world.InlandSurfaceHeights[lakeCentre], .001f);
            Assert.Greater(world.InlandSurfaceHeights[lakeCentre], world.SeaLevel + 50f);
            Assert.Less(world.heights[lakeCentre], world.InlandSurfaceHeights[lakeCentre]);
            CollectionAssert.AreEqual(elevations, data.elevations); CollectionAssert.AreEqual(cover, data.cover);
            Assert.AreNotSame(data.cover, world.landCover);
            var copy = CareerSnapshot.Capture(state);
            Assert.AreEqual(world.latitude, copy.world.latitude); Assert.AreEqual(world.longitude, copy.world.longitude);
            Assert.IsTrue(copy.world.hasCoordinates); Assert.AreEqual(data.elevationSha256, copy.world.elevationSha256);
            Assert.AreEqual(data.coverSha256, copy.world.coverSha256);
            var loaded = JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(copy.world));
            CollectionAssert.AreEqual(world.heights, loaded.heights); CollectionAssert.AreEqual(world.waterHeights, loaded.waterHeights);
            Assert.AreEqual(world.latitude, loaded.latitude); Assert.IsTrue(loaded.Validate(out why), why);
            loaded.latitude = double.NaN; Assert.IsFalse(loaded.Validate(out _));
        }

        [Test]
        public void InvalidDownloadedGridCannotEnterSavedWorldState()
        {
            Assert.Throws<System.ArgumentException>(() => ValleyWorld.GenerateLocation(null));
            Assert.Throws<System.ArgumentException>(() => ValleyWorld.GenerateLocation(new RegionTerrainData {
                latitude = 90d, longitude = 0d, extent = 8000f,
                elevations = new float[ValleyWorld.Resolution * ValleyWorld.Resolution],
                cover = new byte[ValleyWorld.Resolution * ValleyWorld.Resolution] }));
        }
    }
}
