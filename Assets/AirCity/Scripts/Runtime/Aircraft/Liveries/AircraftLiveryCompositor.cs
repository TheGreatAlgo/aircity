using System.Collections;
using System.Collections.Generic;
using AirCity.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>
    /// Builds livery textures. Mirrors the generator (ArtSource/Aircraft/Common/paint.py):
    ///   col = base; col = lerp(col, c1, mask.r); col = lerp(col, c2, mask.g); col = lerp(col, c3, mask.b);
    ///   col = lerp(col, logo.rgb, logo.a) where the type's LogoUV map covers the fin; col *= detail
    /// All maths is on sRGB-encoded bytes; results are sRGB textures.
    ///
    /// Per-texel work, mips and BC1 compression run in Burst jobs. Source masks/detail stay compressed on the GPU and are
    /// read back only while a new livery is built. Results are cached per (aircraft type, livery):
    /// a new livery costs one body texture; a new aircraft only costs its registration patch.
    /// </summary>
    public static class AircraftLiveryCompositor
    {
        /// <summary>Compress generated textures to BC1: ~2.8 MB instead of ~22 MB per livery.</summary>
        public static bool CompressGeneratedTextures = true;

        // Editing colours in the inspector creates a new livery per tweak; keep only the most recent
        // ones in edit mode. At runtime every distinct livery stays cached until ClearCaches().
        const int EditorCacheLimit = 24;

        static readonly Dictionary<(AircraftType, string), Texture2D> bodyCache = new Dictionary<(AircraftType, string), Texture2D>();
        static readonly Dictionary<(AircraftType, string), Color32[]> regBaseCache = new Dictionary<(AircraftType, string), Color32[]>();
        static readonly Dictionary<(AircraftType, string), int> lastUse = new Dictionary<(AircraftType, string), int>();
        static readonly Dictionary<AircraftType, byte[]> regShadeCache = new Dictionary<AircraftType, byte[]>();
        static readonly Dictionary<(Texture2D, int), Pixels> regionCache = new Dictionary<(Texture2D, int), Pixels>();
        static readonly Dictionary<(RegistrationFont, int, int), Glyph> glyphCache = new Dictionary<(RegistrationFont, int, int), Glyph>();
        static int useClock;

        // ------------------------------------------------------------------ public API

        /// <summary>Shared body texture for a livery on an aircraft type (built on first use).</summary>
        public static Texture2D GetBodyTexture(AircraftType type, AircraftLivery livery)
        {
            var key = (type, livery.CacheKey());
            lastUse[key] = ++useClock;
            if (bodyCache.TryGetValue(key, out var tex) && tex != null) return tex;
            if (!Application.isPlaying) EvictOldest(EditorCacheLimit - 1);
            tex = ComposeBody(type, livery);
            bodyCache[key] = tex;
            return tex;
        }

        /// <summary>Build a livery's shared textures up front (e.g. behind a loading screen) so the
        /// first aircraft wearing it doesn't cause a frame hitch.</summary>
        public static void Prewarm(AircraftType type, AircraftLivery livery)
        {
            GetBodyTexture(type, livery);
            RegistrationBase(type, livery);
        }

        /// <summary>Prewarm several liveries, one per frame: StartCoroutine(PrewarmRoutine(...)).</summary>
        public static IEnumerator PrewarmRoutine(AircraftType type, IEnumerable<AircraftLivery> liveries)
        {
            foreach (var l in liveries)
            {
                Prewarm(type, l);
                yield return null;
            }
        }

        /// <summary>Free all generated textures and cached source data. Aircraft still showing one of
        /// those liveries need Apply() again afterwards.</summary>
        public static void ClearCaches()
        {
            foreach (var t in bodyCache.Values)
                if (t != null) DestroyAny(t);
            bodyCache.Clear();
            regBaseCache.Clear();
            lastUse.Clear();
            regShadeCache.Clear();
            regionCache.Clear();
            glyphCache.Clear();
        }

        /// <summary>Number of distinct body textures currently cached.</summary>
        public static int CachedLiveryCount => bodyCache.Count;

        /// <summary>GPU read-back of a rectangle of any texture (compressed / non-readable is fine).
        /// The caller owns (and must destroy) the returned RGBA32 texture.</summary>
        public static Texture2D ReadBack(Texture2D tex, bool linearData, RectInt r)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32,
                linearData ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false, linearData) { hideFlags = HideFlags.HideAndDontSave };
            tmp.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tmp;
        }

        static void EvictOldest(int keep)
        {
            while (bodyCache.Count > keep)
            {
                (AircraftType, string) oldest = default;
                int best = int.MaxValue;
                foreach (var k in bodyCache.Keys)
                {
                    int t = lastUse.TryGetValue(k, out var v) ? v : 0;
                    if (t < best)
                    {
                        best = t;
                        oldest = k;
                    }
                }
                if (bodyCache[oldest] != null) DestroyAny(bodyCache[oldest]);
                bodyCache.Remove(oldest);
                regBaseCache.Remove(oldest);
                lastUse.Remove(oldest);
            }
        }

        // ------------------------------------------------------------------ pixel maths (shared by jobs)

        // exact round(a * (255 - t) / 255 + b * t / 255) for bytes, without division
        static int Lerp255(int a, int b, int t)
        {
            int v = a * (255 - t) + b * t + 128;
            return (v + (v >> 8)) >> 8;
        }

        static int Mul255(int a, int b)
        {
            int v = a * b + 128;
            return (v + (v >> 8)) >> 8;
        }

        /// <summary>The livery's four paint colours as bytes.</summary>
        struct Palette
        {
            public Color32 b, c1, c2, c3;

            public Palette(AircraftLivery l)
            {
                b = l.baseColor;
                c1 = l.color1;
                c2 = l.color2;
                c3 = l.color3;
            }

            public Color32 Paint(Color32 m)
            {
                int r = Lerp255(Lerp255(Lerp255(b.r, c1.r, m.r), c2.r, m.g), c3.r, m.b);
                int g = Lerp255(Lerp255(Lerp255(b.g, c1.g, m.r), c2.g, m.g), c3.g, m.b);
                int bl = Lerp255(Lerp255(Lerp255(b.b, c1.b, m.r), c2.b, m.g), c3.b, m.b);
                return new Color32((byte)r, (byte)g, (byte)bl, 255);
            }
        }

        /// <summary>Bilinear sample of a region (pixel coords of the full texture, centres at +0.5).</summary>
        static Color32 Bilinear(NativeArray<Color32> data, int w, int h, float x, float y)
        {
            x = Mathf.Clamp(x - 0.5f, 0, w - 1);
            y = Mathf.Clamp(y - 0.5f, 0, h - 1);
            int ix = (int)x, iy = (int)y;
            int x1 = Mathf.Min(ix + 1, w - 1), y1 = Mathf.Min(iy + 1, h - 1);
            int fx = (int)((x - ix) * 256f), fy = (int)((y - iy) * 256f);
            Color32 a = data[iy * w + ix], b = data[iy * w + x1], c = data[y1 * w + ix], d = data[y1 * w + x1];
            return new Color32(Bl(a.r, b.r, c.r, d.r, fx, fy), Bl(a.g, b.g, c.g, d.g, fx, fy),
                Bl(a.b, b.b, c.b, d.b, fx, fy), Bl(a.a, b.a, c.a, d.a, fx, fy));
        }

        /// <summary>Tail logo inputs for the body jobs. LogoUV holds (u * cov, v * cov, cov) at its own resolution;
        /// the logo is fitted into the type's logo box by (sx, sy).</summary>
        struct LogoInput
        {
            public int mapW, mapH, logoW, logoH;
            public float sx, sy;
            public bool enabled;
        }

        static Color32 ApplyLogo(Color32 c, float u, float v, in LogoInput li, NativeArray<Color32> map, NativeArray<Color32> logo)
        {
            if (!li.enabled) return c;
            Color32 m = Bilinear(map, li.mapW, li.mapH, u * li.mapW, v * li.mapH);
            if (m.b == 0) return c;
            float lu = (m.r / (float)m.b - 0.5f) / li.sx + 0.5f, lv = (m.g / (float)m.b - 0.5f) / li.sy + 0.5f;
            if (lu < 0f || lu > 1f || lv < 0f || lv > 1f) return c;
            Color32 l = Bilinear(logo, li.logoW, li.logoH, lu * li.logoW, lv * li.logoH);
            int a = Mul255(l.a, m.b);
            return new Color32((byte)Lerp255(c.r, l.r, a), (byte)Lerp255(c.g, l.g, a), (byte)Lerp255(c.b, l.b, a), 255);
        }

        static byte Bl(int a, int b, int c, int d, int fx, int fy)
        {
            int top = a * (256 - fx) + b * fx, bot = c * (256 - fx) + d * fx;
            return (byte)((top * (256 - fy) + bot * fy + 32768) >> 16);
        }

        // ------------------------------------------------------------------ jobs

        /// <summary>Body texture, same-resolution sources: paint + shade straight into the texture (RGB24).</summary>
        [BurstCompile(CompileSynchronously = true)]
        struct ComposeBodyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> source;     // mask, or custom albedo
            [ReadOnly] public NativeArray<Color32> detail;     // shading in .r
            [ReadOnly] public NativeArray<Color32> logoMap, logo;
            [NativeDisableParallelForRestriction] public NativeArray<byte> rgb;
            public Palette palette;
            public LogoInput logoInput;
            public bool custom;
            public int w, h;

            public void Execute(int i)
            {
                Color32 c = custom ? source[i] : palette.Paint(source[i]);
                c = ApplyLogo(c, (i % w + 0.5f) / w, (i / w + 0.5f) / h, logoInput, logoMap, logo);
                int sh = detail[i].r;
                rgb[i * 3] = (byte)Mul255(c.r, sh);
                rgb[i * 3 + 1] = (byte)Mul255(c.g, sh);
                rgb[i * 3 + 2] = (byte)Mul255(c.b, sh);
            }
        }

        /// <summary>Body texture, mismatched resolutions: resample source and detail.</summary>
        [BurstCompile(CompileSynchronously = true)]
        struct ResampleBodyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> source, detail;
            [ReadOnly] public NativeArray<Color32> logoMap, logo;
            public int sw, sh, dw, dh, w, h;
            [NativeDisableParallelForRestriction] public NativeArray<byte> rgb;
            public Palette palette;
            public LogoInput logoInput;
            public bool custom, hasDetail;

            public void Execute(int i)
            {
                float u = (i % w + 0.5f) / w, v = (i / w + 0.5f) / h;
                Color32 m = Bilinear(source, sw, sh, u * sw, v * sh);
                Color32 c = custom ? m : palette.Paint(m);
                c = ApplyLogo(c, u, v, logoInput, logoMap, logo);
                int s = hasDetail ? Bilinear(detail, dw, dh, u * dw, v * dh).r : 255;
                rgb[i * 3] = (byte)Mul255(c.r, s);
                rgb[i * 3 + 1] = (byte)Mul255(c.g, s);
                rgb[i * 3 + 2] = (byte)Mul255(c.b, s);
            }
        }

        /// <summary>Resample a crop of a source region into a W x H strip (registration patch).</summary>
        [BurstCompile(CompileSynchronously = true)]
        struct CropJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> region;
            public int rw, rh, rx0, ry0, fullW, fullH, W, H;
            public Rect crop;
            public Palette palette;
            public bool paint;
            [WriteOnly] public NativeArray<Color32> output;

            public void Execute(int i)
            {
                int x = i % W, y = i / W;
                float u = crop.xMin + crop.width * (x + 0.5f) / W;
                float v = crop.yMin + crop.height * (y + 0.5f) / H;
                Color32 m = Bilinear(region, rw, rh, u * fullW - rx0, v * fullH - ry0);
                output[i] = paint ? palette.Paint(m) : m;
            }
        }

        /// <summary>Multiply the registration canvas by its shading and pack it into the texture (RGB24).</summary>
        [BurstCompile(CompileSynchronously = true)]
        struct ShadePackJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> canvas;
            [ReadOnly] public NativeArray<byte> shade;
            [NativeDisableParallelForRestriction] public NativeArray<byte> rgb;

            public void Execute(int i)
            {
                Color32 c = canvas[i];
                int s = shade[i];
                rgb[i * 3] = (byte)Mul255(c.r, s);
                rgb[i * 3 + 1] = (byte)Mul255(c.g, s);
                rgb[i * 3 + 2] = (byte)Mul255(c.b, s);
            }
        }

        // ------------------------------------------------------------------ source access

        /// <summary>A managed copy of a rectangle of a source texture.</summary>
        struct Pixels
        {
            public Color32[] data;
            public int x0, y0, w, h;       // region inside the full texture
            public int fullW, fullH;
        }

        /// <summary>Full-resolution pixels. Dispose the returned holder texture afterwards (may be null).</summary>
        static NativeArray<Color32> ReadAll(Texture2D tex, bool linearData, out Texture2D holder)
        {
            holder = null;
            if (tex.isReadable && tex.format == TextureFormat.RGBA32) return tex.GetPixelData<Color32>(0);
            holder = ReadBack(tex, linearData, new RectInt(0, 0, tex.width, tex.height));
            return holder.GetPixelData<Color32>(0);
        }

        /// <summary>Managed copy of a rectangle, cached under `regionId`.</summary>
        static Pixels Region(Texture2D tex, bool linearData, RectInt r, int regionId)
        {
            if (tex == null)   // missing texture: behave like an all-zero map instead of throwing
                return new Pixels { data = new Color32[1], w = 1, h = 1, fullW = 1, fullH = 1 };
            var key = (tex, regionId);
            if (regionCache.TryGetValue(key, out var hit) && hit.data != null) return hit;
            r.ClampToBounds(new RectInt(0, 0, tex.width, tex.height));
            var tmp = ReadBack(tex, linearData, r);
            var p = new Pixels { data = tmp.GetPixels32(), x0 = r.x, y0 = r.y, w = r.width, h = r.height, fullW = tex.width, fullH = tex.height };
            DestroyAny(tmp);
            regionCache[key] = p;
            return p;
        }

        static RectInt PixelRect(Texture2D tex, Rect uv, int pad = 3)
        {
            int w = tex != null ? tex.width : 1, h = tex != null ? tex.height : 1;
            int x0 = Mathf.FloorToInt(uv.xMin * w) - pad, y0 = Mathf.FloorToInt(uv.yMin * h) - pad;
            int x1 = Mathf.CeilToInt(uv.xMax * w) + pad, y1 = Mathf.CeilToInt(uv.yMax * h) + pad;
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        static Texture2D MaskFor(AircraftType type, AircraftLivery l) =>
            l.customMask != null ? l.customMask : type.SchemeFor(l).mask;

        // ------------------------------------------------------------------ body texture

        static Texture2D ComposeBody(AircraftType type, AircraftLivery l)
        {
            bool custom = l.customAlbedo != null;
            Texture2D src = custom ? l.customAlbedo : MaskFor(type, l);
            Texture2D detail = type.detail;
            int w = src != null ? src.width : type.atlasSize;
            int h = src != null ? src.height : type.atlasSize;

            var rgb = new NativeArray<byte>(Bc1TextureBuilder.ChainLength(w, h), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var palette = new Palette(l);
            Texture2D tex;
            Texture2D srcHolder = null, detHolder = null, logoHolder = null;
            var logoInput = new LogoInput { mapW = 1, mapH = 1, logoW = 1, logoH = 1, sx = 1, sy = 1 };
            NativeArray<Color32> logoMap = default, logo = default;
            bool ownLogo = false;   // ReadAll hands back pixel data owned by a texture: only dispose what we allocate
            try
            {
                if (l.logo != null && type.logoUV != null)
                {
                    var map = Region(type.logoUV, true, new RectInt(0, 0, type.logoUV.width, type.logoUV.height), RegionLogoMap);
                    logoMap = new NativeArray<Color32>(map.data, Allocator.TempJob);
                    logo = ReadAll(l.logo, false, out logoHolder);
                    float boxAspect = (type.logoBox.y - type.logoBox.x) / Mathf.Max(type.logoBox.w - type.logoBox.z, 1e-3f);
                    float logoAspect = l.logo.width / (float)l.logo.height;
                    logoInput = new LogoInput
                    {
                        mapW = map.w, mapH = map.h, logoW = l.logo.width, logoH = l.logo.height, enabled = true,
                        sx = logoAspect >= boxAspect ? 1f : logoAspect / boxAspect,
                        sy = logoAspect >= boxAspect ? boxAspect / logoAspect : 1f,
                    };
                }
                else
                {
                    logoMap = new NativeArray<Color32>(1, Allocator.TempJob);
                    logo = new NativeArray<Color32>(1, Allocator.TempJob);
                    ownLogo = true;
                }

                if (src == null || detail == null)
                {
                    // nothing to paint from: flat base colour
                    var job = new ComposeBodyJob
                    {
                        source = new NativeArray<Color32>(w * h, Allocator.TempJob),
                        detail = Filled(w * h, new Color32(255, 255, 255, 255)),
                        logoMap = logoMap, logo = logo, logoInput = default,
                        rgb = rgb, palette = palette, custom = false, w = w, h = h,
                    };
                    job.Schedule(w * h, 4096).Complete();
                    job.source.Dispose();
                    job.detail.Dispose();
                }
                else
                {
                    var s = ReadAll(src, !custom, out srcHolder);
                    var d = ReadAll(detail, true, out detHolder);
                    if (detail.width == w && detail.height == h)
                    {
                        new ComposeBodyJob
                        {
                            source = s, detail = d, logoMap = logoMap, logo = logo, logoInput = logoInput,
                            rgb = rgb, palette = palette, custom = custom, w = w, h = h,
                        }.Schedule(w * h, 4096).Complete();
                    }
                    else
                    {
                        new ResampleBodyJob
                        {
                            source = s, detail = d, sw = src.width, sh = src.height, dw = detail.width, dh = detail.height,
                            w = w, h = h, rgb = rgb, palette = palette, custom = custom, hasDetail = true,
                            logoMap = logoMap, logo = logo, logoInput = logoInput,
                        }.Schedule(w * h, 4096).Complete();
                    }
                }
                tex = Bc1TextureBuilder.Build(rgb, w, h, CompressGeneratedTextures);
            }
            finally
            {
                rgb.Dispose();
                if (logoMap.IsCreated) logoMap.Dispose();
                if (ownLogo && logo.IsCreated) logo.Dispose();
                if (srcHolder != null) DestroyAny(srcHolder);
                if (detHolder != null) DestroyAny(detHolder);
                if (logoHolder != null) DestroyAny(logoHolder);
            }
            Configure(tex, type.model + " livery");
            return tex;
        }

        static NativeArray<Color32> Filled(int n, Color32 c)
        {
            var a = new NativeArray<Color32>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++) a[i] = c;
            return a;
        }

        static void Configure(Texture2D tex, string name)
        {
            tex.name = name;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 4;
            tex.hideFlags = HideFlags.DontSave;
        }

        // ------------------------------------------------------------------ registration texture

        const int RegionPort = 1, RegionStarboard = 2, RegionFont = 3, RegionLogoMap = 4;

        /// <summary>The registration patch without letters (port strip on top, starboard below), unshaded.
        /// Cached per livery so each new aircraft only pays for its letters.</summary>
        static Color32[] RegistrationBase(AircraftType type, AircraftLivery l)
        {
            var key = (type, l.CacheKey());
            if (regBaseCache.TryGetValue(key, out var cached)) return cached;
            int W = type.registrationTextureWidth, H = type.registrationHalfHeight;
            bool custom = l.customAlbedo != null;
            Texture2D src = custom ? l.customAlbedo : MaskFor(type, l);
            bool buildShade = !regShadeCache.ContainsKey(type);
            var canvas = new Color32[W * 2 * H];
            var shade = buildShade ? new byte[W * 2 * H] : null;
            var strip = new NativeArray<Color32>(W * H, Allocator.TempJob);
            try
            {
                for (int side = 0; side < 2; side++)
                {
                    var s = side == 0 ? type.port : type.starboard;
                    int rid = side == 0 ? RegionPort : RegionStarboard;
                    int row0 = side == 0 ? H : 0;
                    Crop(Region(src, !custom, PixelRect(src, s.crop), rid), s.crop, W, H, new Palette(l), !custom, strip);
                    NativeArray<Color32>.Copy(strip, 0, canvas, row0 * W, W * H);
                    if (buildShade)
                    {
                        Crop(Region(type.detail, true, PixelRect(type.detail, s.crop), rid), s.crop, W, H, default, false, strip);
                        for (int i = 0; i < W * H; i++) shade[row0 * W + i] = strip[i].r;
                    }
                }
            }
            finally
            {
                strip.Dispose();
            }
            if (buildShade) regShadeCache[type] = shade;
            regBaseCache[key] = canvas;
            return canvas;
        }

        static void Crop(Pixels p, Rect crop, int W, int H, Palette palette, bool paint, NativeArray<Color32> output)
        {
            var region = new NativeArray<Color32>(p.data, Allocator.TempJob);
            new CropJob
            {
                region = region, rw = p.w, rh = p.h, rx0 = p.x0, ry0 = p.y0, fullW = p.fullW, fullH = p.fullH,
                W = W, H = H, crop = crop, palette = palette, paint = paint, output = output,
            }.Schedule(W * H, 2048).Complete();
            region.Dispose();
        }

        /// <summary>
        /// Per-aircraft texture for the registration patch: the livery around the patch (port
        /// strip on top, starboard below) with the letters painted in. Destroys `reuse`.
        /// </summary>
        public static Texture2D ComposeRegistration(AircraftType type, AircraftLivery l, string text, Texture2D reuse = null)
        {
            int W = type.registrationTextureWidth, H = type.registrationHalfHeight;
            var canvas = (Color32[])RegistrationBase(type, l).Clone();
            var shade = regShadeCache[type];

            var font = type.font;
            if (!string.IsNullOrEmpty(text) && font != null && font.atlas != null)
            {
                Vector4 box = type.SchemeFor(l).registrationBox;
                Color32 col = l.registrationColor;
                for (int side = 0; side < 2; side++)
                {
                    var s = side == 0 ? type.port : type.starboard;
                    // metric box -> atlas uv -> strip pixels
                    float ua = s.k * s.sign * box.x + s.tu, ub = s.k * s.sign * box.y + s.tu;
                    float u0 = Mathf.Min(ua, ub), u1 = Mathf.Max(ua, ub);
                    float v0 = s.k * box.z + s.tv, v1 = s.k * box.w + s.tv;
                    var rect = new Rect((u0 - s.crop.xMin) / s.crop.width * W, (v0 - s.crop.yMin) / s.crop.height * H,
                        (u1 - u0) / s.crop.width * W, (v1 - v0) / s.crop.height * H);
                    DrawText(canvas, W, H, side == 0 ? H : 0, rect, text, col, font);
                }
            }

            // finished textures are compressed and non-readable, so a new registration needs a new texture
            if (reuse != null) DestroyAny(reuse);
            var c = new NativeArray<Color32>(canvas, Allocator.TempJob);
            var sh = new NativeArray<byte>(shade, Allocator.TempJob);
            var rgb = new NativeArray<byte>(Bc1TextureBuilder.ChainLength(W, 2 * H), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            Texture2D tex;
            try
            {
                new ShadePackJob { canvas = c, shade = sh, rgb = rgb }.Schedule(canvas.Length, 4096).Complete();
                tex = Bc1TextureBuilder.Build(rgb, W, 2 * H, CompressGeneratedTextures);
            }
            finally
            {
                c.Dispose();
                sh.Dispose();
                rgb.Dispose();
            }
            Configure(tex, type.model + " registration");
            return tex;
        }

        // ------------------------------------------------------------------ lettering

        /// <summary>A glyph rasterised at one size: coverage bytes, drawn at integer positions.</summary>
        class Glyph
        {
            public int w, h;
            public byte[] coverage;
        }

        /// <summary>Rasterise a glyph at `scale` (atlas px -> canvas px), 3x3 supersampled. The bitmap's
        /// origin is one pixel left of the ink and one pixel below the cell bottom.</summary>
        static Glyph GetGlyph(RegistrationFont font, int g, float scale)
        {
            var key = (font, g, Mathf.RoundToInt(scale * 1000f));
            if (glyphCache.TryGetValue(key, out var glyph)) return glyph;
            var atlas = Region(font.atlas, true, new RectInt(0, 0, font.atlas.width, font.atlas.height), RegionFont);
            float ink = font.inkRight[g] - font.inkLeft[g];
            glyph = new Glyph { w = Mathf.CeilToInt(ink * scale) + 2, h = Mathf.CeilToInt(font.cell * scale) + 2 };
            glyph.coverage = new byte[glyph.w * glyph.h];
            float cx = font.cellX[g], cy = font.cellY[g];
            float ax0 = cx + font.inkLeft[g];
            // atlas may have been resized on import: convert authored pixels to actual pixels
            float fx = atlas.fullW / font.atlasSize, fy = atlas.fullH / font.atlasSize;
            var data = new NativeArray<Color32>(atlas.data, Allocator.Temp);
            for (int y = 0; y < glyph.h; y++)
            {
                for (int x = 0; x < glyph.w; x++)
                {
                    int acc = 0;
                    for (int sy = 0; sy < 3; sy++)
                    {
                        for (int sx = 0; sx < 3; sx++)
                        {
                            float ax = ax0 + (x - 1 + (sx + 0.5f) / 3f) / scale;
                            float ay = cy + (y - 1 + (sy + 0.5f) / 3f) / scale;
                            if (ax < cx || ax >= cx + font.cell || ay < cy || ay >= cy + font.cell) continue;
                            acc += Bilinear(data, atlas.w, atlas.h, ax * fx - atlas.x0, ay * fy - atlas.y0).a;
                        }
                    }
                    glyph.coverage[y * glyph.w + x] = (byte)((acc + 4) / 9);
                }
            }
            data.Dispose();
            glyphCache[key] = glyph;
            return glyph;
        }

        static void DrawText(Color32[] canvas, int W, int H, int row0, Rect box, string text, Color32 color, RegistrationFont font)
        {
            float cap = font.capTop - font.baseline;
            float track = 0.16f * cap, space = 0.42f * cap;
            var items = new List<(int g, float pen, float ink)>();
            float pen = 0;
            foreach (char ch in text)
            {
                int g = font.GlyphIndex(ch);
                if (g < 0)
                {
                    pen += space;
                    continue;
                }
                float ink = font.inkRight[g] - font.inkLeft[g];
                items.Add((g, pen, ink));
                pen += ink + track;
            }
            if (items.Count == 0) return;
            float width = pen - track;
            float scale = Mathf.Min(box.height / cap, box.width / width);
            // a few discrete sizes (at most 2% smaller) so most registrations reuse cached glyphs
            scale = Mathf.Floor(scale * 50f) / 50f;
            float startX = box.xMin + (box.width - width * scale) / 2;
            float baseY = box.yMin + (box.height - cap * scale) / 2;

            foreach (var (g, gpen, _) in items)
            {
                var glyph = GetGlyph(font, g, scale);
                // bitmap origin: one pixel left of the ink, one pixel below the cell bottom
                int ox = Mathf.RoundToInt(startX + gpen * scale) - 1;
                int oy = Mathf.RoundToInt(baseY - font.baseline * scale) - 1;
                for (int y = 0; y < glyph.h; y++)
                {
                    int py = oy + y;
                    if (py < 0 || py >= H) continue;
                    for (int x = 0; x < glyph.w; x++)
                    {
                        int px = ox + x;
                        int t = glyph.coverage[y * glyph.w + x];
                        if (t == 0 || px < 0 || px >= W) continue;
                        int i = (row0 + py) * W + px;
                        var c = canvas[i];
                        canvas[i] = new Color32((byte)Lerp255(c.r, color.r, t), (byte)Lerp255(c.g, color.g, t),
                            (byte)Lerp255(c.b, color.b, t), 255);
                    }
                }
            }
        }

        static void DestroyAny(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
