using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AirCity.Tests.World
{
    public class LocationTerrainLoaderTests
    {
        const int N = RegionTerrainData.Resolution;
        static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(Application.dataPath, "AirCity/Tests/EditMode/Fixtures/" + name + ".bytes"));

        [Test]
        public void RealEsaTiffRangesDecodeOriginalClassCodesAndGeographicReference()
        {
            var tiff = new WorldCoverTiff(Fixture("WorldCoverN45W123Header"));
            Assert.That(tiff.width, Is.EqualTo(36000)); Assert.That(tiff.height, Is.EqualTo(36000));
            Assert.That(tiff.blockWidth, Is.EqualTo(1024)); Assert.That(tiff.offsets[0], Is.EqualTo(23317994));
            Assert.That(tiff.byteCounts[0], Is.EqualTo(6497));
            Assert.That(tiff.west, Is.EqualTo(-123).Within(1e-10)); Assert.That(tiff.north, Is.EqualTo(48).Within(1e-10));
            Assert.That(tiff.PixelX(-122.999), Is.EqualTo(12).Within(1));
            Assert.That(tiff.PixelY(47.999), Is.EqualTo(12).Within(1));
            var decoded = tiff.Decode(Fixture("WorldCoverN45W123Block0"));
            Assert.That(LocationTerrainLoader.Sha256(decoded), Is.EqualTo("8db6a70a7d7736b222337c3c3152d9985e6976e64b366f5b1c2f49a818582183"));
            Assert.That(decoded.Count(c => c == 10), Is.EqualTo(1032635));
            Assert.That(decoded.Count(c => c == 30), Is.EqualTo(15837));
            Assert.That(decoded.Count(c => c == 60), Is.EqualTo(80));
            Assert.That(decoded.Count(c => c == 50), Is.EqualTo(24));
            var corrupt = Fixture("WorldCoverN45W123Block0"); corrupt[corrupt.Length - 1] ^= 1;
            Assert.Throws<InvalidDataException>(() => tiff.Decode(corrupt), "Class masks must never silently accept a damaged HTTP range.");
            Assert.Throws<InvalidDataException>(() => new WorldCoverTiff(new byte[16]));
        }

        [Test]
        public void GeographicCropStaysNorthUpAndWrapsAtTheDateline()
        {
            var latitudes = new double[N]; var longitudes = new double[N];
            LocationTerrainLoader.SampleCoordinates(-16.5, 179.99, 8000, latitudes, longitudes);
            Assert.That(latitudes[0], Is.LessThan(-16.5)); Assert.That(latitudes[N - 1], Is.GreaterThan(-16.5));
            Assert.That(latitudes[N / 2], Is.EqualTo(-16.5).Within(1e-10));
            Assert.That(longitudes[0], Is.GreaterThan(179)); Assert.That(longitudes[N - 1], Is.LessThan(-179));
            Assert.That(LocationTerrainLoader.CoverTile(-16.5, 179.99), Is.EqualTo("S18E177"));
            Assert.That(LocationTerrainLoader.CoverTile(-16.5, -179.99), Is.EqualTo("S18W180"));
            Assert.That(LocationTerrainLoader.CoverTile(47.665, -121.928), Is.EqualTo("N45W123"));
            Assert.Throws<ArgumentException>(() => LocationTerrainLoader.Validate(-89, 0, 8000));
            Assert.Throws<ArgumentException>(() => LocationTerrainLoader.Validate(83, 0, 8000));
            Assert.Throws<ArgumentException>(() => LocationTerrainLoader.Validate(double.NaN, 0, 8000));
        }

        [Test]
        public void TerrariumRgbDecodingRetainsHeightPrecisionAndNorthSouthOrientation()
        {
            var pixels = Enumerable.Repeat(new Color32(128, 0, 0, 255), 256 * 256).ToArray();
            pixels[255 * 256] = new Color32(128, 100, 64, 255); // Unity's top-left: 100.25 m.
            pixels[0] = new Color32(127, 255, 128, 255); // Unity's bottom-left: -0.5 m.
            var decoded = LocationTerrainLoader.DecodeTerrarium(pixels);
            Assert.That(decoded[0], Is.EqualTo(100.25f));
            Assert.That(decoded[255 * 256], Is.EqualTo(-.5f));
            Assert.That(decoded[1], Is.Zero);
        }

        [Test]
        public void CachedTerrainRoundTripsOfflineAndRejectsChangedSamplesOrLocation()
        {
            var data = new RegionTerrainData { latitude = 47.665, longitude = -121.928, extent = 8000, name = "Actual source crop",
                elevations = Enumerable.Range(0, N * N).Select(i => i / 1024f).ToArray(),
                cover = Enumerable.Range(0, N * N).Select(i => (byte)(i % 5 == 0 ? 80 : 10)).ToArray() };
            data.elevationSha256 = LocationTerrainLoader.Sha256(LocationTerrainLoader.FloatBytes(data.elevations));
            data.coverSha256 = LocationTerrainLoader.Sha256(data.cover);
            var encoded = LocationTerrainLoader.WriteCache(data);
            var restored = LocationTerrainLoader.ReadCache(encoded, data.latitude, data.longitude, data.extent);
            Assert.That(restored.name, Is.EqualTo(data.name));
            CollectionAssert.AreEqual(data.elevations, restored.elevations); CollectionAssert.AreEqual(data.cover, restored.cover);
            Assert.Throws<InvalidDataException>(() => LocationTerrainLoader.ReadCache(encoded, 47.7, data.longitude, data.extent));
            encoded[encoded.Length - 1] ^= 1;
            Assert.Throws<InvalidDataException>(() => LocationTerrainLoader.ReadCache(encoded, data.latitude, data.longitude, data.extent));
            var culture = CultureInfo.CurrentCulture;
            try
            {
                string key = LocationTerrainLoader.CacheKey(47.665, 238.072, 8000);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.That(LocationTerrainLoader.CacheKey(47.665, -121.928, 8000), Is.EqualTo(key));
                Assert.That(LocationTerrainLoader.CacheKey(47.666, -121.928, 8000), Is.Not.EqualTo(key));
            }
            finally { CultureInfo.CurrentCulture = culture; }
        }

        [Test]
        public void InlandWaterDoesNotBecomeAnOceanJustBecauseTheMaskContainsWater()
        {
            var elevations = Enumerable.Repeat(120f, N * N).ToArray(); var cover = Enumerable.Repeat((byte)80, N * N).ToArray();
            Assert.IsFalse(LocationTerrainLoader.DetectOcean(elevations, cover));
            Array.Fill(elevations, 0f); Assert.IsTrue(LocationTerrainLoader.DetectOcean(elevations, cover));
            Array.Fill(elevations, -500f); Assert.IsTrue(LocationTerrainLoader.DetectOcean(elevations, cover), "Deep marine crops remain ocean.");
            Array.Fill(elevations, 0f);
            Array.Fill(cover, (byte)10);
            for (int z = 40; z < 100; z++) for (int x = 40; x < 100; x++) cover[z * N + x] = 80;
            Assert.IsFalse(LocationTerrainLoader.DetectOcean(elevations, cover), "An isolated near-sea-level lake is not a marine coastline.");
        }

        [UnityTest, Explicit("Downloads real public source data; run when validating the terrain provider integration.")]
        public IEnumerator DownloadsRealLocationAndLoadsTheSameGridFromOfflineCache()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var download = LocationTerrainLoader.LoadAsync(47.665, -121.928, 8000, cancellation.Token);
            while (!download.IsCompleted) yield return null;
            if (download.IsFaulted) throw download.Exception.GetBaseException();
            Assert.IsFalse(download.IsCanceled);
            var data = download.Result;
            Assert.That(data.elevations.Length, Is.EqualTo(N * N)); Assert.That(data.cover.Length, Is.EqualTo(N * N));
            Assert.That(data.elevations.Min(), Is.InRange(0, 30)); Assert.That(data.elevations.Max(), Is.InRange(150, 250));
            Assert.That(data.cover.Count(c => c == 80) / (float)data.cover.Length, Is.InRange(.01f, .04f));
            Assert.IsFalse(data.hasOcean); Assert.IsTrue(LocationTerrainLoader.HasCached(data.latitude, data.longitude));
            var cached = LocationTerrainLoader.LoadAsync(data.latitude, data.longitude);
            while (!cached.IsCompleted) yield return null;
            if (cached.IsFaulted) throw cached.Exception.GetBaseException();
            CollectionAssert.AreEqual(data.cover, cached.Result.cover); CollectionAssert.AreEqual(data.elevations, cached.Result.elevations);
        }
    }
}
