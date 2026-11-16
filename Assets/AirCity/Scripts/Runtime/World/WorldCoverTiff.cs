using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AirCity.World
{
    /// <summary>Reads the tiled, lossless uint8 GeoTIFF used by ESA WorldCover 2021. No image colour conversion.</summary>
    internal sealed class WorldCoverTiff
    {
        internal readonly int width, height, blockWidth, blockHeight;
        internal readonly long[] offsets;
        internal readonly int[] byteCounts;
        internal readonly double west, north, pixelWidth, pixelHeight;
        readonly int compression, predictor;
        readonly bool littleEndian;
        readonly byte[] header;
        readonly Dictionary<int, Entry> tags = new Dictionary<int, Entry>();
        readonly struct Entry
        {
            internal readonly int type, count, position;
            internal Entry(int type, int count, int position) { this.type = type; this.count = count; this.position = position; }
        }

        internal WorldCoverTiff(byte[] bytes)
        {
            header = bytes;
            if (bytes == null || bytes.Length < 8 || !((bytes[0] == 'I' && bytes[1] == 'I') || (bytes[0] == 'M' && bytes[1] == 'M')))
                throw new InvalidDataException("WorldCover returned an invalid TIFF header.");
            littleEndian = bytes[0] == 'I';
            if (U16(2) != 42) throw new InvalidDataException("Unsupported WorldCover TIFF version.");
            int directory = checked((int)U32(4));
            int count = U16(directory);
            for (int i = 0; i < count; i++)
            {
                int p = checked(directory + 2 + i * 12);
                int tag = U16(p), type = U16(p + 2), length = checked((int)U32(p + 4));
                int size = type == 3 ? 2 : type == 4 ? 4 : type == 12 ? 8 : 1;
                int position = checked(length * size) <= 4 ? p + 8 : checked((int)U32(p + 8));
                tags[tag] = new Entry(type, length, position);
            }
            width = Scalar(256); height = Scalar(257); blockWidth = Scalar(322); blockHeight = Scalar(323);
            compression = Scalar(259); predictor = Scalar(317, 1);
            if (Scalar(258) != 8 || Scalar(277, 1) != 1 || Scalar(284, 1) != 1 ||
                width < 1 || height < 1 || blockWidth < 1 || blockHeight < 1 || blockWidth > 4096 || blockHeight > 4096 ||
                !(compression == 1 || compression == 8 || compression == 32946) || !(predictor == 1 || predictor == 2))
                throw new InvalidDataException("Unsupported WorldCover pixel layout; expected one lossless uint8 classification band.");
            int blocks = checked(((width + blockWidth - 1) / blockWidth) * ((height + blockHeight - 1) / blockHeight));
            offsets = new long[blocks]; byteCounts = new int[blocks];
            if (!tags.TryGetValue(324, out var locations) || !tags.TryGetValue(325, out var sizes) || locations.count != blocks || sizes.count != blocks)
                throw new InvalidDataException("WorldCover tile index is incomplete.");
            for (int i = 0; i < blocks; i++)
            {
                offsets[i] = Integer(locations, i); byteCounts[i] = checked((int)Integer(sizes, i));
                if (offsets[i] < 8 || byteCounts[i] < 1 || byteCounts[i] > 20 * 1024 * 1024)
                    throw new InvalidDataException("WorldCover tile index is invalid.");
            }
            if (!tags.TryGetValue(33550, out var scale) || !tags.TryGetValue(33922, out var origin) || scale.count < 2 || origin.count < 6)
                throw new InvalidDataException("WorldCover geographic reference is missing.");
            pixelWidth = Double(scale.position); pixelHeight = Double(scale.position + 8);
            west = Double(origin.position + 24) - Double(origin.position) * pixelWidth;
            north = Double(origin.position + 32) + Double(origin.position + 8) * pixelHeight;
            if (pixelWidth <= 0 || pixelHeight <= 0) throw new InvalidDataException("Invalid WorldCover geographic resolution.");
        }

        internal int PixelX(double longitude) => Math.Clamp((int)Math.Floor((longitude - west) / pixelWidth), 0, width - 1);
        internal int PixelY(double latitude) => Math.Clamp((int)Math.Floor((north - latitude) / pixelHeight), 0, height - 1);
        internal int Block(int x, int y) => y / blockHeight * ((width + blockWidth - 1) / blockWidth) + x / blockWidth;
        internal int InBlock(int x, int y) => y % blockHeight * blockWidth + x % blockWidth;

        internal byte[] Decode(byte[] encoded)
        {
            int expected = checked(blockWidth * blockHeight);
            byte[] decoded;
            if (compression == 1) decoded = (byte[])encoded.Clone();
            else
            {
                // TIFF Deflate uses a zlib envelope; DeflateStream consumes the raw DEFLATE payload.
                if (encoded.Length < 6 || (encoded[0] & 15) != 8 || ((encoded[0] << 8) + encoded[1]) % 31 != 0 || (encoded[1] & 32) != 0)
                    throw new InvalidDataException("WorldCover compression envelope is invalid.");
                decoded = new byte[expected];
                using var source = new MemoryStream(encoded, 2, encoded.Length - 6, false);
                using var stream = new DeflateStream(source, CompressionMode.Decompress);
                int read = 0;
                while (read < expected)
                {
                    int n = stream.Read(decoded, read, expected - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read != expected || stream.ReadByte() != -1) throw new InvalidDataException("WorldCover block is truncated or oversized.");
                uint a = 1, b = 0;
                for (int i = 0; i < decoded.Length; i++) { a = (a + decoded[i]) % 65521; b = (b + a) % 65521; }
                int end = encoded.Length - 4;
                uint checksum = ((uint)encoded[end] << 24) | ((uint)encoded[end + 1] << 16) | ((uint)encoded[end + 2] << 8) | encoded[end + 3];
                if (((b << 16) | a) != checksum) throw new InvalidDataException("WorldCover block checksum failed.");
            }
            if (decoded.Length != expected) throw new InvalidDataException("WorldCover block has the wrong size.");
            if (predictor == 2)
                for (int y = 0; y < blockHeight; y++) for (int x = 1; x < blockWidth; x++)
                    decoded[y * blockWidth + x] = unchecked((byte)(decoded[y * blockWidth + x] + decoded[y * blockWidth + x - 1]));
            return decoded;
        }

        int Scalar(int tag, int fallback = -1)
        {
            if (tags.TryGetValue(tag, out var e) && e.count == 1) return checked((int)Integer(e, 0));
            if (fallback >= 0) return fallback;
            throw new InvalidDataException("WorldCover TIFF tag " + tag + " is missing.");
        }
        long Integer(Entry e, int index) => e.type == 3 ? U16(checked(e.position + index * 2)) :
            e.type == 4 ? U32(checked(e.position + index * 4)) : throw new InvalidDataException("Unsupported WorldCover TIFF integer type.");
        int U16(int p) { Bounds(p, 2); return littleEndian ? header[p] | header[p + 1] << 8 : header[p] << 8 | header[p + 1]; }
        uint U32(int p) { Bounds(p, 4); return littleEndian ? (uint)(header[p] | header[p + 1] << 8 | header[p + 2] << 16 | header[p + 3] << 24) :
            (uint)(header[p] << 24 | header[p + 1] << 16 | header[p + 2] << 8 | header[p + 3]); }
        double Double(int p)
        {
            Bounds(p, 8);
            if (littleEndian == BitConverter.IsLittleEndian) return BitConverter.ToDouble(header, p);
            var bytes = new byte[8]; Array.Copy(header, p, bytes, 0, 8); Array.Reverse(bytes); return BitConverter.ToDouble(bytes, 0);
        }
        void Bounds(int p, int size) { if (p < 0 || p > header.Length - size) throw new InvalidDataException("WorldCover TIFF header is truncated."); }
    }
}
