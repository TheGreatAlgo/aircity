using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AirCity.Rendering
{
    /// <summary>
    /// Turns RGB24 pixels made at runtime into a mip-mapped, BC1 (DXT1) compressed texture. Mips and
    /// compression run in Burst jobs; Unity's Texture2D.Compress does the same on the main thread and
    /// is several times slower.
    ///
    ///   var px = new NativeArray&lt;byte&gt;(Bc1TextureBuilder.ChainLength(w, h), Allocator.TempJob);
    ///   // write level 0 at the start: w * h * 3 bytes, Unity's row order (row 0 = bottom)
    ///   var tex = Bc1TextureBuilder.Build(px, w, h);   // fills the rest of px with the mip chain
    ///   px.Dispose();
    ///
    /// The encoder is the stb_dxt approach: endpoints from the block's principal axis, refined by
    /// least squares; solid blocks use precomputed best-match endpoint pairs.
    /// </summary>
    public static class Bc1TextureBuilder
    {
        public static int MipCount(int width, int height)
        {
            int n = 1, m = Mathf.Max(width, height);
            while (m > 1)
            {
                m >>= 1;
                n++;
            }
            return n;
        }

        /// <summary>Bytes of an RGB24 buffer holding level 0 and all its mips.</summary>
        public static int ChainLength(int width, int height)
        {
            int total = 0;
            for (int k = 0; k < MipCount(width, height); k++)
                total += Mathf.Max(1, width >> k) * Mathf.Max(1, height >> k) * 3;
            return total;
        }

        /// <summary>Build mips, compress (sizes that are multiples of 4), upload and return a non-readable
        /// texture. `chain` must be ChainLength(width, height) long with level 0 filled in.</summary>
        public static Texture2D Build(NativeArray<byte> chain, int width, int height, bool compress = true, bool linear = false)
        {
            int count = MipCount(width, height);
            var levels = new NativeArray<Level>(count, Allocator.TempJob);
            int rgbOffset = 0, blockCount = 0;
            for (int k = 0; k < count; k++)
            {
                int w = Mathf.Max(1, width >> k), h = Mathf.Max(1, height >> k);
                int bx = (w + 3) / 4, by = (h + 3) / 4;
                levels[k] = new Level { w = w, h = h, rgb = rgbOffset, firstBlock = blockCount, blocksX = bx };
                rgbOffset += w * h * 3;
                blockCount += bx * by;
            }
            if (chain.Length < rgbOffset)
            {
                levels.Dispose();
                throw new System.ArgumentException($"chain holds {chain.Length} bytes, {width}x{height} with mips needs {rgbOffset}");
            }

            JobHandle mips = default;
            for (int k = 1; k < count; k++)
                mips = new DownsampleJob { data = chain, src = levels[k - 1], dst = levels[k] }
                    .Schedule(levels[k].w * levels[k].h, 1024, mips);

            Texture2D tex;
            try
            {
                if (compress && width % 4 == 0 && height % 4 == 0)
                {
                    var blocks = new NativeArray<ulong>(blockCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    var m5 = new NativeArray<byte>(SolidMatch5, Allocator.TempJob);
                    var m6 = new NativeArray<byte>(SolidMatch6, Allocator.TempJob);
                    try
                    {
                        new EncodeJob { rgb = chain, levels = levels, levelCount = count, match5 = m5, match6 = m6, blocks = blocks }
                            .Schedule(blockCount, 128, mips).Complete();
                        tex = new Texture2D(width, height, TextureFormat.DXT1, count, linear);
                        tex.LoadRawTextureData(blocks);
                    }
                    finally
                    {
                        blocks.Dispose();
                        m5.Dispose();
                        m6.Dispose();
                    }
                }
                else
                {
                    mips.Complete();
                    tex = new Texture2D(width, height, TextureFormat.RGB24, count, linear);
                    tex.LoadRawTextureData(chain.GetSubArray(0, rgbOffset));
                }
            }
            finally
            {
                mips.Complete();
                levels.Dispose();
            }
            tex.Apply(false, true);   // upload and drop the CPU copy
            return tex;
        }

        struct Level
        {
            public int w, h, rgb, firstBlock, blocksX;
        }

        /// <summary>2x2 box filter from one level to the next (sRGB bytes, like Unity's own mips).</summary>
        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]   // covered by Bc1Encoder test; checks cost ~2x in the editor
        struct DownsampleJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<byte> data;
            public Level src, dst;

            public void Execute(int i)
            {
                int x = i % dst.w, y = i / dst.w;
                int x0 = Mathf.Min(2 * x, src.w - 1), x1 = Mathf.Min(2 * x + 1, src.w - 1);
                int y0 = Mathf.Min(2 * y, src.h - 1), y1 = Mathf.Min(2 * y + 1, src.h - 1);
                int a = src.rgb + (y0 * src.w + x0) * 3, b = src.rgb + (y0 * src.w + x1) * 3;
                int c = src.rgb + (y1 * src.w + x0) * 3, d = src.rgb + (y1 * src.w + x1) * 3;
                int o = dst.rgb + i * 3;
                for (int ch = 0; ch < 3; ch++)
                    data[o + ch] = (byte)((data[a + ch] + data[b + ch] + data[c + ch] + data[d + ch] + 2) >> 2);
            }
        }

        // ------------------------------------------------------------------ BC1 encoder

        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]   // covered by Bc1Encoder test; checks cost ~2x in the editor
        struct EncodeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> rgb;
            [ReadOnly] public NativeArray<Level> levels;
            [ReadOnly] public NativeArray<byte> match5, match6;
            public int levelCount;
            [WriteOnly] public NativeArray<ulong> blocks;

            public void Execute(int b)
            {
                int k = 0;
                while (k + 1 < levelCount && b >= levels[k + 1].firstBlock) k++;
                var l = levels[k];
                int local = b - l.firstBlock;
                blocks[b] = EncodeBlock(l, local % l.blocksX * 4, local / l.blocksX * 4);
            }

            /// <summary>Byte offset of pixel p (0..15, row-major) of the block at x0, y0; edges clamp.</summary>
            int Offset(in Level l, int x0, int y0, int p) =>
                l.rgb + (Mathf.Min(y0 + (p >> 2), l.h - 1) * l.w + Mathf.Min(x0 + (p & 3), l.w - 1)) * 3;

            ulong EncodeBlock(in Level l, int x0, int y0)
            {
                int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
                float mr = 0, mg = 0, mb = 0;
                for (int p = 0; p < 16; p++)
                {
                    int o = Offset(l, x0, y0, p);
                    int r = rgb[o], g = rgb[o + 1], bl = rgb[o + 2];
                    minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                    minG = Mathf.Min(minG, g); maxG = Mathf.Max(maxG, g);
                    minB = Mathf.Min(minB, bl); maxB = Mathf.Max(maxB, bl);
                    mr += r; mg += g; mb += bl;
                }
                mr /= 16f; mg /= 16f; mb /= 16f;
                // flat paint: one exact-as-possible colour beats a 4-colour line through noise of a level or two
                if (maxR - minR + maxG - minG + maxB - minB <= 3)
                    return Solid((int)(mr + 0.5f), (int)(mg + 0.5f), (int)(mb + 0.5f));

                float crr = 0, crg = 0, crb = 0, cgg = 0, cgb = 0, cbb = 0;
                for (int p = 0; p < 16; p++)
                {
                    int o = Offset(l, x0, y0, p);
                    float dr = rgb[o] - mr, dg = rgb[o + 1] - mg, db = rgb[o + 2] - mb;
                    crr += dr * dr; crg += dr * dg; crb += dr * db;
                    cgg += dg * dg; cgb += dg * db; cbb += db * db;
                }
                // principal axis: power iteration on the covariance, starting from the extent
                float vr = maxR - minR, vg = maxG - minG, vb = maxB - minB;
                for (int it = 0; it < 4; it++)
                {
                    float r = vr * crr + vg * crg + vb * crb;
                    float g = vr * crg + vg * cgg + vb * cgb;
                    float bl = vr * crb + vg * cgb + vb * cbb;
                    float m = Mathf.Max(Mathf.Abs(r), Mathf.Max(Mathf.Abs(g), Mathf.Abs(bl)));
                    if (m < 1e-4f) break;
                    vr = r / m; vg = g / m; vb = bl / m;
                }
                if (Mathf.Abs(vr) + Mathf.Abs(vg) + Mathf.Abs(vb) < 1e-4f)
                {
                    vr = 0.299f; vg = 0.587f; vb = 0.114f;
                }

                // the extreme pixels along the axis are the first endpoints
                float lo = float.MaxValue, hi = float.MinValue;
                int pLo = 0, pHi = 0;
                for (int p = 0; p < 16; p++)
                {
                    int o = Offset(l, x0, y0, p);
                    float d = rgb[o] * vr + rgb[o + 1] * vg + rgb[o + 2] * vb;
                    if (d < lo) { lo = d; pLo = p; }
                    if (d > hi) { hi = d; pHi = p; }
                }
                int oh = Offset(l, x0, y0, pHi), ol = Offset(l, x0, y0, pLo);
                int c0 = To565(rgb[oh], rgb[oh + 1], rgb[oh + 2]);
                int c1 = To565(rgb[ol], rgb[ol + 1], rgb[ol + 2]);
                uint idx = Indices(l, x0, y0, c0, c1, out int err);

                for (int pass = 0; pass < 2 && err > 16; pass++)
                {
                    if (!Refine(l, x0, y0, idx, out int r0, out int r1)) break;
                    uint idx2 = Indices(l, x0, y0, r0, r1, out int err2);
                    if (err2 >= err) break;
                    c0 = r0; c1 = r1; idx = idx2; err = err2;
                }
                return Pack(c0, c1, idx);
            }

            /// <summary>Nearest palette entry per pixel; palette in 4-colour order (c0, c1, 2/3 c0 + 1/3 c1, 1/3 c0 + 2/3 c1).</summary>
            uint Indices(in Level l, int x0, int y0, int c0, int c1, out int error)
            {
                Decode(c0, out int r0, out int g0, out int b0);
                Decode(c1, out int r1, out int g1, out int b1);
                int r2 = (2 * r0 + r1) / 3, g2 = (2 * g0 + g1) / 3, b2 = (2 * b0 + b1) / 3;
                int r3 = (r0 + 2 * r1) / 3, g3 = (g0 + 2 * g1) / 3, b3 = (b0 + 2 * b1) / 3;
                uint idx = 0;
                error = 0;
                for (int p = 0; p < 16; p++)
                {
                    int o = Offset(l, x0, y0, p);
                    int r = rgb[o], g = rgb[o + 1], bl = rgb[o + 2];
                    int e0 = Sq(r - r0) + Sq(g - g0) + Sq(bl - b0), e1 = Sq(r - r1) + Sq(g - g1) + Sq(bl - b1);
                    int e2 = Sq(r - r2) + Sq(g - g2) + Sq(bl - b2), e3 = Sq(r - r3) + Sq(g - g3) + Sq(bl - b3);
                    uint best = 0;
                    int be = e0;
                    if (e1 < be) { be = e1; best = 1; }
                    if (e2 < be) { be = e2; best = 2; }
                    if (e3 < be) { be = e3; best = 3; }
                    idx |= best << (2 * p);
                    error += be;
                }
                return idx;
            }

            /// <summary>Least-squares endpoints for the given indices.</summary>
            bool Refine(in Level l, int x0, int y0, uint idx, out int c0, out int c1)
            {
                float aa = 0, ab = 0, bb = 0, ar = 0, ag = 0, abl = 0, br = 0, bg = 0, bbl = 0;
                for (int p = 0; p < 16; p++)
                {
                    uint i = (idx >> (2 * p)) & 3;
                    float a = i == 0 ? 1f : i == 1 ? 0f : i == 2 ? 2f / 3f : 1f / 3f, b = 1f - a;
                    int o = Offset(l, x0, y0, p);
                    aa += a * a; ab += a * b; bb += b * b;
                    ar += a * rgb[o]; ag += a * rgb[o + 1]; abl += a * rgb[o + 2];
                    br += b * rgb[o]; bg += b * rgb[o + 1]; bbl += b * rgb[o + 2];
                }
                float det = aa * bb - ab * ab;
                c0 = c1 = 0;
                if (det < 1e-4f) return false;
                float inv = 1f / det;
                c0 = To565(Clamp255((bb * ar - ab * br) * inv), Clamp255((bb * ag - ab * bg) * inv), Clamp255((bb * abl - ab * bbl) * inv));
                c1 = To565(Clamp255((aa * br - ab * ar) * inv), Clamp255((aa * bg - ab * ag) * inv), Clamp255((aa * bbl - ab * abl) * inv));
                return true;
            }

            ulong Solid(int r, int g, int b)
            {
                // index 2 (2/3 c0 + 1/3 c1) reaches colours that plain 565 can't
                int c0 = (match5[r * 2] << 11) | (match6[g * 2] << 5) | match5[b * 2];
                int c1 = (match5[r * 2 + 1] << 11) | (match6[g * 2 + 1] << 5) | match5[b * 2 + 1];
                return Pack(c0, c1, 0xAAAAAAAAu);
            }

            /// <summary>BC1 needs c0 > c1 for 4-colour mode: swapping the endpoints maps index 0&lt;-&gt;1 and 2&lt;-&gt;3.</summary>
            static ulong Pack(int c0, int c1, uint idx)
            {
                if (c0 < c1)
                {
                    int t = c0;
                    c0 = c1;
                    c1 = t;
                    idx ^= 0x55555555u;
                }
                else if (c0 == c1)
                {
                    idx = 0;
                }
                return (ulong)(uint)c0 | ((ulong)(uint)c1 << 16) | ((ulong)idx << 32);
            }

            static int Sq(int v) => v * v;

            static int Clamp255(float v) => (int)Mathf.Clamp(v + 0.5f, 0f, 255f);
        }

        static int To565(int r, int g, int b) => ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255);

        static void Decode(int c, out int r, out int g, out int b)
        {
            r = Expand((c >> 11) & 31, 5);
            g = Expand((c >> 5) & 63, 6);
            b = Expand(c & 31, 5);
        }

        static int Expand(int v, int bits) => bits == 5 ? (v << 3) | (v >> 2) : (v << 2) | (v >> 4);

        static byte[] solidMatch5, solidMatch6;
        static byte[] SolidMatch5 => solidMatch5 ??= SolidMatch(5);
        static byte[] SolidMatch6 => solidMatch6 ??= SolidMatch(6);

        /// <summary>For each 8-bit value, the endpoint pair whose 2/3 : 1/3 mix comes closest (closest pair on ties).</summary>
        static byte[] SolidMatch(int bits)
        {
            int n = 1 << bits;
            var table = new byte[512];
            for (int v = 0; v < 256; v++)
            {
                int best = int.MaxValue;
                for (int a = 0; a < n; a++)
                {
                    for (int b = 0; b < n; b++)
                    {
                        int ea = Expand(a, bits), eb = Expand(b, bits);
                        int score = Mathf.Abs((2 * ea + eb) / 3 - v) * 1000 + Mathf.Abs(ea - eb);
                        if (score >= best) continue;
                        best = score;
                        table[v * 2] = (byte)a;
                        table[v * 2 + 1] = (byte)b;
                    }
                }
            }
            return table;
        }
    }
}
