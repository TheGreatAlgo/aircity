using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AirCity.World
{
    /// <summary>Downloads a geographic crop, without changing the active world. Call from Unity's main thread.</summary>
    public static class LocationTerrainLoader
    {
        internal const string SourceVersion = "mapzen-terrarium-esa-worldcover-2021-v200-v1";
        const string ElevationUrl = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/";
        const string CoverUrl = "https://esa-worldcover.s3.eu-central-1.amazonaws.com/v200/2021/map/ESA_WorldCover_10m_2021_v200_";
        const int N = RegionTerrainData.Resolution;
        const double MetresPerDegree = 111320;

        public static bool HasCached(double latitude, double longitude, float extent = 8000f)
        {
            try { Validate(latitude, longitude, extent); return File.Exists(CachePath(latitude, longitude, extent)); }
            catch (ArgumentException) { return false; }
        }

        public static async Task<RegionTerrainData> LoadAsync(double latitude, double longitude, float extent = 8000f,
            CancellationToken cancellationToken = default, IProgress<RegionTerrainProgress> progress = null)
        {
            Validate(latitude, longitude, extent); latitude = Canonical(latitude); longitude = Canonical(WrapLongitude(longitude));
            cancellationToken.ThrowIfCancellationRequested();
            var path = CachePath(latitude, longitude, extent);
            Report(progress, 0, "Checking downloaded terrain");
            if (File.Exists(path))
            {
                try
                {
                    var cached = ReadCache(File.ReadAllBytes(path), latitude, longitude, extent);
                    cancellationToken.ThrowIfCancellationRequested(); Report(progress, 1, "Loaded cached terrain"); return cached;
                }
                catch (Exception e) when (e is IOException || e is ArgumentException || e is OverflowException || e is FormatException)
                { /* A damaged or obsolete cache is replaced only after a complete successful download. */ }
            }
            var latitudes = new double[N]; var longitudes = new double[N];
            SampleCoordinates(latitude, longitude, extent, latitudes, longitudes);
            var result = new RegionTerrainData { latitude = latitude, longitude = longitude, extent = extent,
                name = string.Format(CultureInfo.InvariantCulture, "{0:0.000}°{1}, {2:0.000}°{3}", Math.Abs(latitude), latitude < 0 ? "S" : "N",
                    Math.Abs(longitude), longitude < 0 ? "W" : "E") };
            result.elevations = await LoadElevations(latitude, extent, latitudes, longitudes, cancellationToken, progress);
            if (result.elevations.Any(h => float.IsNaN(h) || float.IsInfinity(h) || h < -12000 || h > 9000))
                throw new InvalidDataException("The elevation provider has missing or invalid height samples at this location. Choose another location.");
            result.cover = await LoadCover(latitudes, longitudes, cancellationToken, progress);
            cancellationToken.ThrowIfCancellationRequested();
            result.hasOcean = DetectOcean(result.elevations, result.cover);
            result.elevationSha256 = Sha256(FloatBytes(result.elevations)); result.coverSha256 = Sha256(result.cover);
            Report(progress, .97f, "Saving downloaded terrain for offline use");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, WriteCache(result)); cancellationToken.ThrowIfCancellationRequested();
                // Concurrent requests are allowed: a completed file is never visible until all samples and hashes exist.
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Report(progress, 1, "Terrain ready"); return result;
        }

        static async Task<float[]> LoadElevations(double latitude, float extent, double[] latitudes, double[] longitudes,
            CancellationToken cancellation, IProgress<RegionTerrainProgress> progress)
        {
            // Match output spacing without downloading hundreds of tiny polar tiles. Provider resolution is about 30 m.
            int zoom = Math.Clamp((int)Math.Round(Math.Log(156543.03392 * Math.Cos(latitude * Math.PI / 180) / (extent / (N - 1)), 2)), 8, 13);
            int tilesAcross = 1 << zoom;
            var xs = new double[N]; var ys = new double[N];
            for (int i = 0; i < N; i++)
            {
                xs[i] = (longitudes[i] + 180) / 360 * tilesAcross * 256 - .5;
                double rad = latitudes[i] * Math.PI / 180;
                ys[i] = (1 - Math.Log(Math.Tan(rad) + 1 / Math.Cos(rad)) / Math.PI) * .5 * tilesAcross * 256 - .5;
            }
            var xTiles = new HashSet<int>(); var yTiles = new HashSet<int>();
            for (int i = 0; i < N; i++)
            {
                xTiles.Add(WrapTile(FloorDiv((int)Math.Floor(xs[i]) + 1, 256), tilesAcross));
                // Floor division is important for the half-pixel neighbour across the dateline.
                xTiles.Add(WrapTile(FloorDiv((int)Math.Floor(xs[i]), 256), tilesAcross));
                yTiles.Add(Math.Clamp(FloorDiv((int)Math.Floor(ys[i]), 256), 0, tilesAcross - 1));
                yTiles.Add(Math.Clamp(FloorDiv((int)Math.Floor(ys[i]) + 1, 256), 0, tilesAcross - 1));
            }
            var ids = (from x in xTiles from y in yTiles select (x, y)).ToArray();
            var images = new Dictionary<(int x, int y), float[]>(); int done = 0;
            await Batches(ids, async id =>
            {
                cancellation.ThrowIfCancellationRequested();
                var encoded = await Download(ElevationUrl + zoom + "/" + id.x + "/" + id.y + ".png", cancellation);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    if (!ImageConversion.LoadImage(texture, encoded, false) || texture.width != 256 || texture.height != 256)
                        throw new InvalidDataException("The elevation provider returned an invalid terrain tile.");
                    images[id] = DecodeTerrarium(texture.GetPixels32());
                }
                finally { if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture); }
                done++; Report(progress, .03f + .44f * done / ids.Length, "Downloading elevation " + done + "/" + ids.Length);
            });
            float At(int x, int y)
            {
                int tileX = WrapTile(FloorDiv(x, 256), tilesAcross), tileY = Math.Clamp(FloorDiv(y, 256), 0, tilesAcross - 1);
                return images[(tileX, tileY)][Mod(y, 256) * 256 + Mod(x, 256)];
            }
            var result = new float[N * N];
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                int ix = (int)Math.Floor(xs[x]), iy = (int)Math.Floor(ys[z]);
                float fx = (float)(xs[x] - ix), fy = (float)(ys[z] - iy);
                result[z * N + x] = Mathf.Lerp(Mathf.Lerp(At(ix, iy), At(ix + 1, iy), fx), Mathf.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), fx), fy);
            }
            return result;
        }

        sealed class CoverSource
        {
            internal string url;
            internal WorldCoverTiff tiff;
            internal readonly Dictionary<int, byte[]> blocks = new Dictionary<int, byte[]>();
        }

        static async Task<byte[]> LoadCover(double[] latitudes, double[] longitudes, CancellationToken cancellation,
            IProgress<RegionTerrainProgress> progress)
        {
            var sources = new Dictionary<string, CoverSource>();
            var names = new string[N * N];
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                string tile = CoverTile(latitudes[z], longitudes[x]); names[z * N + x] = tile;
                if (!sources.ContainsKey(tile)) sources.Add(tile, new CoverSource { url = CoverUrl + tile + "_Map.tif" });
            }
            Report(progress, .49f, "Reading real land-cover classification");
            await Batches(sources.Values.ToArray(), async s => s.tiff = new WorldCoverTiff(await Download(s.url, cancellation, 0, 65536)));
            var needed = new HashSet<(string source, int block)>();
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                string name = names[z * N + x]; var tiff = sources[name].tiff;
                needed.Add((name, tiff.Block(tiff.PixelX(longitudes[x]), tiff.PixelY(latitudes[z]))));
            }
            int done = 0;
            await Batches(needed.ToArray(), async id =>
            {
                cancellation.ThrowIfCancellationRequested(); var s = sources[id.source];
                var encoded = await Download(s.url, cancellation, s.tiff.offsets[id.block], s.tiff.byteCounts[id.block]);
                s.blocks[id.block] = s.tiff.Decode(encoded);
                done++; Report(progress, .5f + .44f * done / needed.Count, "Downloading land cover " + done + "/" + needed.Count);
            });
            var result = new byte[N * N];
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                var source = sources[names[z * N + x]]; var tiff = source.tiff;
                int px = tiff.PixelX(longitudes[x]), py = tiff.PixelY(latitudes[z]);
                byte cover = source.blocks[tiff.Block(px, py)][tiff.InBlock(px, py)];
                if (!ValidCover(cover)) throw new InvalidDataException("WorldCover returned an unknown land-cover classification.");
                result[z * N + x] = cover;
            }
            if (result.All(c => c == 0)) throw new InvalidDataException("ESA WorldCover has no land-cover coverage for this location. Choose another location.");
            return result;
        }

        static async Task Batches<T>(T[] items, Func<T, Task> action)
        {
            for (int i = 0; i < items.Length; i += 4)
            {
                int count = Math.Min(4, items.Length - i); var tasks = new Task[count];
                for (int j = 0; j < count; j++) tasks[j] = action(items[i + j]);
                await Task.WhenAll(tasks);
            }
        }

        static async Task<byte[]> Download(string url, CancellationToken cancellation, long offset = -1, int length = 0)
        {
            for (int attempt = 0; ; attempt++)
            {
                cancellation.ThrowIfCancellationRequested(); using var request = UnityWebRequest.Get(url); request.timeout = 45;
                if (offset >= 0) request.SetRequestHeader("Range", "bytes=" + offset + "-" + (offset + length - 1));
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellation.IsCancellationRequested) { request.Abort(); cancellation.ThrowIfCancellationRequested(); }
                    await Task.Yield();
                }
                cancellation.ThrowIfCancellationRequested();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var data = request.downloadHandler.data;
                    if (offset >= 0 && (request.responseCode != 206 || data.Length != length))
                        throw new InvalidDataException("The land-cover provider did not honour its byte-range request. Please try again later.");
                    return data;
                }
                if (request.responseCode == 404)
                    throw new InvalidDataException("Real terrain or ESA WorldCover is unavailable for this location. Try a nearby area within 60°S–83°N.");
                if (attempt >= 1 || (request.responseCode >= 400 && request.responseCode < 500 && request.responseCode != 429))
                    throw new IOException("Terrain download failed (" + (request.responseCode > 0 ? "HTTP " + request.responseCode : request.error) + "). Check your connection and retry.");
            }
        }

        internal static void Validate(double latitude, double longitude, float extent)
        {
            if (double.IsNaN(latitude) || double.IsInfinity(latitude) || double.IsNaN(longitude) || double.IsInfinity(longitude) ||
                float.IsNaN(extent) || float.IsInfinity(extent) || extent < 2000 || extent > 16000)
                throw new ArgumentException("Choose a valid geographic location and a terrain extent between 2 and 16 km.");
            double radius = extent / (2 * MetresPerDegree);
            if (latitude - radius < -60 || latitude + radius > 83)
                throw new ArgumentException("Real elevation and land cover are available between 60°S and 83°N. Choose a location away from the coverage edge.");
        }
        internal static double WrapLongitude(double longitude) => ((longitude + 180) % 360 + 360) % 360 - 180;
        internal static void SampleCoordinates(double latitude, double longitude, float extent, double[] latitudes, double[] longitudes)
        {
            for (int i = 0; i < N; i++)
            {
                double metres = ((double)i / (N - 1) - .5) * extent;
                latitudes[i] = latitude + metres / MetresPerDegree;
                longitudes[i] = WrapLongitude(longitude + metres / (MetresPerDegree * Math.Cos(latitude * Math.PI / 180)));
            }
        }
        internal static string CoverTile(double latitude, double longitude)
        {
            int south = (int)Math.Floor(latitude / 3) * 3, west = (int)Math.Floor(WrapLongitude(longitude) / 3) * 3;
            return (south < 0 ? "S" : "N") + Math.Abs(south).ToString("D2", CultureInfo.InvariantCulture) +
                (west < 0 ? "W" : "E") + Math.Abs(west).ToString("D3", CultureInfo.InvariantCulture);
        }
        internal static float[] DecodeTerrarium(Color32[] unityPixels)
        {
            if (unityPixels.Length != 256 * 256) throw new InvalidDataException("Wrong elevation tile size.");
            var result = new float[unityPixels.Length];
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            { var p = unityPixels[(255 - y) * 256 + x]; result[y * 256 + x] = p.r * 256 + p.g + p.b / 256f - 32768; }
            return result;
        }
        internal static bool DetectOcean(float[] elevations, byte[] cover)
        {
            var seen = new bool[N * N]; var pending = new Queue<int>();
            void Add(int i) { if (!seen[i] && cover[i] == 80 && elevations[i] <= 2) { seen[i] = true; pending.Enqueue(i); } }
            for (int i = 0; i < N; i++) { Add(i); Add((N - 1) * N + i); Add(i * N); Add(i * N + N - 1); }
            while (pending.Count > 0)
            {
                int i = pending.Dequeue();
                if (elevations[i] <= 2) return true;
                int x = i % N, z = i / N;
                if (x > 0) Add(i - 1); if (x + 1 < N) Add(i + 1); if (z > 0) Add(i - N); if (z + 1 < N) Add(i + N);
            }
            return false;
        }
        static bool ValidCover(byte c) => c == 0 || c == 10 || c == 20 || c == 30 || c == 40 || c == 50 || c == 60 || c == 70 || c == 80 || c == 90 || c == 95 || c == 100;
        static int Mod(int value, int size) => (value % size + size) % size;
        static int WrapTile(int value, int size) => Mod(value, size);
        static int FloorDiv(int value, int divisor) => (int)Math.Floor((double)value / divisor);
        static double Canonical(double value) => Math.Round(value, 7, MidpointRounding.AwayFromZero);
        static void Report(IProgress<RegionTerrainProgress> progress, float fraction, string message) => progress?.Report(new RegionTerrainProgress(fraction, message));

        static string CachePath(double latitude, double longitude, float extent) => Path.Combine(Application.persistentDataPath, "TerrainCache", "v1", CacheKey(latitude, longitude, extent) + ".terrain");
        internal static string CacheKey(double latitude, double longitude, float extent) => Sha256(System.Text.Encoding.UTF8.GetBytes(SourceVersion + "|" + N + "|" +
            Canonical(latitude).ToString("F7", CultureInfo.InvariantCulture) + "|" + Canonical(WrapLongitude(longitude)).ToString("F7", CultureInfo.InvariantCulture) + "|" + extent.ToString("R", CultureInfo.InvariantCulture)));
        internal static byte[] WriteCache(RegionTerrainData data)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write("ACTERRAIN1"); writer.Write(SourceVersion); writer.Write(N); writer.Write(data.latitude); writer.Write(data.longitude); writer.Write(data.extent);
            writer.Write(data.name ?? ""); writer.Write(data.hasOcean); writer.Write(data.elevationSha256); writer.Write(data.coverSha256);
            writer.Write(FloatBytes(data.elevations)); writer.Write(data.cover); writer.Flush(); return stream.ToArray();
        }
        internal static RegionTerrainData ReadCache(byte[] bytes, double latitude, double longitude, float extent)
        {
            if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("Oversized terrain cache.");
            using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream);
            if (reader.ReadString() != "ACTERRAIN1" || reader.ReadString() != SourceVersion || reader.ReadInt32() != N)
                throw new InvalidDataException("Obsolete terrain cache.");
            var data = new RegionTerrainData { latitude = reader.ReadDouble(), longitude = reader.ReadDouble(), extent = reader.ReadSingle(), name = reader.ReadString(),
                hasOcean = reader.ReadBoolean(), elevationSha256 = reader.ReadString(), coverSha256 = reader.ReadString() };
            if (Canonical(data.latitude) != Canonical(latitude) || Canonical(data.longitude) != Canonical(WrapLongitude(longitude)) || data.extent != extent)
                throw new InvalidDataException("Terrain cache location mismatch.");
            var raw = reader.ReadBytes(N * N * 4); data.cover = reader.ReadBytes(N * N);
            if (raw.Length != N * N * 4 || data.cover.Length != N * N || stream.Position != stream.Length ||
                Sha256(raw) != data.elevationSha256 || Sha256(data.cover) != data.coverSha256)
                throw new InvalidDataException("Terrain cache checksum failed.");
            data.elevations = new float[N * N]; Buffer.BlockCopy(raw, 0, data.elevations, 0, raw.Length);
            if (data.elevations.Any(h => float.IsNaN(h) || float.IsInfinity(h)) || data.cover.Any(c => !ValidCover(c)))
                throw new InvalidDataException("Terrain cache contains invalid samples.");
            return data;
        }
        internal static byte[] FloatBytes(float[] samples) { var bytes = new byte[samples.Length * 4]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length); return bytes; }
        internal static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
