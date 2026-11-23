using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.World
{
    /// <summary>One biome's trees, generated at load and shared by every root. Each tree is a solid shape first:
    /// opaque trunk and crown volumes with real normals and their own shading in the vertex colours, which are all
    /// the two far levels draw. The two near levels add alpha-tested boughs or leaf sprays over the same volumes,
    /// so a tree keeps its outline and colour from the closest view to the whole-region one.</summary>
    public sealed class RegionTreePalette : IDisposable
    {
        public const int Levels = 4;
        /// <summary>Every model stands this tall; a saved tree's height scales it.</summary>
        public const float Height = 24f;
        public TreeLibrary Library { get; private set; }
        public TreeLibrary Source { get; private set; }
        public RegionPreset Preset { get; private set; }
        readonly List<Object> owned = new List<Object>();
        int conifers;
        enum Shape { Fir, SlenderFir, Spruce, WeepingSpruce, Pine, OpenPine, Alder, Birch, SpreadingOak,
            Coconut, LeaningPalm, FanPalm, Umbrella, Emergent, Coastal, Pandanus }
        static readonly Shape[] Evergreens = { Shape.Fir, Shape.SlenderFir, Shape.Spruce, Shape.WeepingSpruce, Shape.Pine, Shape.OpenPine };
        static readonly Color Wood = new Color(.115f, .082f, .055f);

        public static RegionTreePalette Create(TreeLibrary source, RegionPreset preset)
        {
            bool warm = preset == RegionPreset.Tropical || preset == RegionPreset.Desert;
            if (!warm && (source == null || source.foliage == null || source.foliage.GetTexture("_BaseMap") == null)) return null;
            var palette = new RegionTreePalette { Source = source, Preset = preset };
            try { palette.Build(); return palette; }
            catch { palette.Dispose(); throw; }
        }

        void Build()
        {
            Library = Own(ScriptableObject.CreateInstance<TreeLibrary>());
            Library.name = Preset + " tree palette";
            var shader = Shader.Find("AirCity/TreeCanopy");
            if (shader == null) throw new InvalidOperationException("The shared tree canopy shader is unavailable.");
            var solid = Own(new Material(shader) { name = "Tree trunks and crown volumes", enableInstancing = true, renderQueue = 2000 });
            Material Sprays(string name, Texture texture, Color mean, float cutoff)
            {
                var material = Own(new Material(shader) { name = name, enableInstancing = true, renderQueue = 2450 });
                material.EnableKeyword("_CARDS"); material.SetTexture("_BaseMap", texture);
                material.SetColor("_TexMean", mean); material.SetFloat("_Cutoff", cutoff); return material;
            }
            var shapes = new List<Shape>();
            if (Preset == RegionPreset.Woodland || Preset == RegionPreset.Mountains) { shapes.AddRange(Evergreens); conifers = shapes.Count; }
            if (Preset == RegionPreset.Woodland) shapes.AddRange(new[] { Shape.Alder, Shape.Birch, Shape.SpreadingOak });
            else if (Preset == RegionPreset.Tropical) shapes.AddRange(new[] { Shape.Coconut, Shape.LeaningPalm, Shape.FanPalm, Shape.Umbrella, Shape.Emergent, Shape.Coastal, Shape.Pandanus });
            else if (Preset == RegionPreset.Desert) shapes.AddRange(new[] { Shape.Coconut, Shape.FanPalm });
            Material needles = conifers > 0 ? Sprays("Conifer boughs", Source.foliage.GetTexture("_BaseMap"), ConiferAtlasMean, .38f) : null;
            Material leaves = null;
            var variants = new List<TreeVariant>();
            foreach (var shape in shapes)
            {
                var variant = new TreeVariant { height = Height, levels = new Mesh[Levels], bark = solid };
                bool conifer = shape <= Shape.OpenPine, palm = shape == Shape.Coconut || shape == Shape.LeaningPalm || shape == Shape.FanPalm || shape == Shape.Pandanus;
                if (!conifer && !palm && leaves == null) { var atlas = Own(LeafSprays(out var mean)); leaves = Sprays("Broadleaf sprays", atlas, mean, .42f); }
                variant.foliage = variant.distantFoliage = conifer ? needles : palm ? solid : leaves;
                for (int level = 0; level < Levels; level++)
                {
                    var b = new Builder();
                    variant.name = shape <= Shape.WeepingSpruce ? Conifer(b, shape, level, out variant.crown) :
                        conifer ? Pine(b, shape, level, out variant.crown) :
                        palm ? Palm(b, shape, level, out variant.crown) : Broadleaf(b, shape, level, out variant.crown);
                    variant.levels[level] = Own(b.Mesh(variant.name + " level " + level));
                }
                variants.Add(variant);
            }
            Library.bark = solid; Library.foliage = Library.distantFoliage = needles != null ? needles : leaves != null ? leaves : solid;
            Library.variants = variants.ToArray();
        }

        /// <summary>Stable habitat patches: coast palms, interior rainforest, conifer woodland with broadleaf groves.</summary>
        public int Variant(ValleyWorld world, Vector2 point, float roll)
        {
            if (Preset == RegionPreset.Mountains) return Mathf.Min(conifers - 1, (int)(roll * conifers));
            if (Preset == RegionPreset.Desert) return roll < .6f ? 0 : 1;
            if (Preset == RegionPreset.Woodland)
            {
                float grove = Mathf.PerlinNoise(point.x * .0014f + world.seed % 71, point.y * .0014f + 17f);
                float broadleaf = Mathf.Lerp(.05f, .46f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.35f, .72f, grove)));
                return roll < broadleaf ? conifers + Mathf.Min(2, (int)(roll / broadleaf * 3f)) :
                    Mathf.Min(conifers - 1, (int)((roll - broadleaf) / (1f - broadleaf) * conifers));
            }
            bool coast = false;
            if (world.landCover != null && world.landCover.Length == ValleyWorld.Resolution * ValleyWorld.Resolution)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt((point.x + ValleyWorld.Size * .5f) / world.Spacing), 0, ValleyWorld.Resolution - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt((point.y + ValleyWorld.Size * .5f) / world.Spacing), 0, ValleyWorld.Resolution - 1);
                for (int dz = -6; dz <= 6 && !coast; dz += 3)
                for (int dx = -6; dx <= 6; dx += 3)
                {
                    int nx = Mathf.Clamp(x + dx, 0, ValleyWorld.Resolution - 1), nz = Mathf.Clamp(z + dz, 0, ValleyWorld.Resolution - 1);
                    byte cover = world.landCover[nz * ValleyWorld.Resolution + nx];
                    if (cover == 80 || cover == 95) { coast = true; break; }
                }
            }
            float palm = coast ? .58f : .12f;
            if (roll < palm) return Mathf.Min(2, (int)(roll / palm * 3f));
            float crown = (roll - palm) / (1f - palm);
            if (crown < (coast ? .22f : .58f)) return 3;
            if (crown < (coast ? .40f : .87f)) return 4;
            return crown < .94f ? 5 : 6;
        }

        // Mean linear colour of the opaque texels, which the shader divides out: an atlas varies a tree's colour,
        // it does not set it, so the levels without cards match the ones with.
        static readonly Color ConiferAtlasMean = new Color(.105f, .135f, .026f);

        /// <summary>Firs and spruces: whorls of boughs round a single stem, over a darker tiered core.</summary>
        static string Conifer(Builder b, Shape shape, int level, out TreeCrown crown)
        {
            string name; int seed; float radius, bottom, taper, rise, sag, lean, hang; Color needles;
            switch (shape)
            {
                case Shape.Fir: name = "Broad fir"; seed = 11; radius = 5.5f; bottom = .11f; taper = .86f; rise = .12f; sag = .34f; lean = .2f; hang = .26f; needles = new Color(.026f, .060f, .026f); break;
                case Shape.SlenderFir: name = "Slender fir"; seed = 23; radius = 4.0f; bottom = .08f; taper = .74f; rise = .04f; sag = .32f; lean = -.25f; hang = .30f; needles = new Color(.022f, .056f, .030f); break;
                case Shape.Spruce: name = "Tiered spruce"; seed = 37; radius = 5.1f; bottom = .15f; taper = .98f; rise = -.08f; sag = .28f; lean = .1f; hang = .34f; needles = new Color(.028f, .062f, .024f); break;
                default: name = "Weeping spruce"; seed = 53; radius = 4.5f; bottom = .10f; taper = .82f; rise = -.18f; sag = .40f; lean = .35f; hang = .48f; needles = new Color(.023f, .052f, .025f); break;
            }
            var rng = new Dice(7219 + seed * 739);
            float phase = rng.Next() * 6.28f, y0 = bottom * Height;
            Vector3 Stem(float t) => new Vector3(Mathf.Sin(t * 2.3f + phase) * t * .16f + lean * t * t, t * Height, Mathf.Cos(t * 1.7f + phase) * t * .13f);
            // t: 0 at the lowest boughs, 1 at the tip. The lowest boughs are a little short of the widest.
            float Radius(float t) => radius * Mathf.Pow(Mathf.Max(0f, 1f - t), taper) * Mathf.Lerp(.74f, 1f, Mathf.Clamp01(t / .12f));
            var bark = new Color(.085f, .066f, .052f);
            if (level == 0) for (int i = 0; i < 5; i++) b.Tube(Stem(i * .17f), Stem((i + 1) * .17f), Mathf.Lerp(.34f, .08f, i / 5f), Mathf.Lerp(.34f, .08f, (i + 1) / 5f), 6, bark);
            else if (level == 1) for (int i = 0; i < 2; i++) b.Tube(Stem(i * .3f), Stem((i + 1) * .3f), Mathf.Lerp(.34f, .12f, i / 2f), Mathf.Lerp(.34f, .12f, (i + 1) / 2f), 4, bark);
            else if (level == 2) b.Tube(Stem(0f), Stem(bottom + .12f), .36f, .26f, 3, bark);

            // Near levels: a dark core inside the boughs. Far levels: the core is the whole crown.
            int tiers = level == 0 ? 5 : level == 1 ? 4 : level == 2 ? 3 : 1, sides = level == 0 ? 8 : level == 3 ? 5 : 7;
            // Seen between the boughs it is the crown's own shade; alone it is the crown, a little darker than a
            // lit bough tip because nothing shades it.
            // The far shapes stand a little wide: bough tips no longer show, and a forest keeps its closed canopy.
            float core = level == 0 ? .42f : level == 1 ? .5f : 1.1f, inside = level <= 1 ? .34f : .74f;
            for (int i = 0; i < tiers; i++)
            {
                float ta = i / (float)tiers, tb = i == tiers - 1 ? 1f : Mathf.Min(1f, (i + 1.5f) / tiers);
                float rim = (tiers == 1 ? radius * .84f : Radius(ta) * (i == 0 ? 1.12f : 1f)) * core;
                var low = needles * inside * Mathf.Lerp(.74f, 1f, ta);
                b.Skirt(Stem(Mathf.Lerp(bottom, 1f, ta)), Stem(Mathf.Lerp(bottom, 1f, tb)), rim, i == tiers - 1 ? 0f : Radius(tb) * core * .4f,
                    sides, phase + i * 1.7f, level <= 1 ? .08f : .2f, rim * .16f, low, i == tiers - 1 ? low * 1.12f : low * .62f, rng);
            }
            if (level <= 1)
            {
                float y = y0 + .25f; int whorl = 0;
                while (y < Height * .94f)
                {
                    float t = (y - y0) / (Height - y0), crest = Mathf.SmoothStep(.72f, 1f, t);
                    float spacing = Mathf.Lerp(1.3f, .85f, t) * (level == 0 ? 1f : 1.85f);
                    int count = Mathf.RoundToInt(Mathf.Lerp(level == 0 ? 7f : 5f, level == 0 ? 4f : 3f, t));
                    for (int k = 0; k < count; k++)
                    {
                        float angle = phase + whorl * 2.39996f + k * Mathf.PI * 2f / count + (rng.Next() - .5f) * .5f;
                        var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                        float reach = Mathf.Max(.75f, Radius(t) * Mathf.Lerp(.84f, 1.15f, rng.Next()));
                        var root = Stem(y / Height) + Vector3.up * (rng.Next() - .5f) * spacing * .5f;
                        float tint = Mathf.Lerp(.88f, 1.12f, rng.Next()) * Mathf.Lerp(.74f, 1f, t);
                        // The crest's boughs still reach upwards; the lower ones arch out and hang.
                        b.Bough(root, outward, reach, Mathf.Lerp(rise, .5f, crest) * reach, Mathf.Lerp(sag, .08f, crest) * reach,
                            Mathf.Max(1f, reach * (level == 0 ? 1.0f : 1.5f)), hang, (k + whorl) % 2, needles * tint, level == 0 && t < .6f ? 3 : 2);
                    }
                    y += spacing; whorl++;
                }
                var tip = Stem(.925f);
                b.Bough(tip, Vector3.right, .01f, 2.1f, 0f, 1.25f, 0f, 0, needles, 2, true);
                if (level == 0) b.Bough(tip, Vector3.forward, .01f, 2.1f, 0f, 1.25f, 0f, 1, needles, 2, true);
            }
            crown = new TreeCrown { centre = new Vector2(lean * .25f, 0f), bottom = y0, top = Height, ring = y0 + (Height - y0) * .1f, radius = radius * .8f };
            return name;
        }

        /// <summary>Pines: a long bare stem under an open crown of separate needle clumps.</summary>
        static string Pine(Builder b, Shape shape, int level, out TreeCrown crown)
        {
            bool open = shape == Shape.OpenPine;
            string name = open ? "Open-crowned pine" : "Tall pine";
            float radius = open ? 5.0f : 4.1f, bottom = open ? .50f : .56f, lean = open ? .9f : -.35f;
            var needles = open ? new Color(.040f, .074f, .026f) : new Color(.034f, .066f, .028f);
            var bark = new Color(.135f, .078f, .046f);
            var rng = new Dice(7219 + (open ? 97 : 71) * 739);
            float phase = rng.Next() * 6.28f;
            Vector3 Stem(float t) => new Vector3(Mathf.Sin(t * 1.9f + phase) * t * .3f + lean * t * t, t * Height, Mathf.Cos(t * 1.4f + phase) * t * .25f);
            int segments = level == 0 ? 6 : level == 1 ? 3 : level == 2 ? 2 : 1, sides = level == 0 ? 6 : level == 1 ? 4 : 3;
            float reachUp = level == 3 ? (bottom + 1f) * .5f : .95f;
            for (int i = 0; i < segments && level < 3; i++)
                b.Tube(Stem(i * reachUp / segments), Stem((i + 1) * reachUp / segments), Mathf.Lerp(.36f, .09f, i / (float)segments),
                    Mathf.Lerp(.36f, .09f, (i + 1f) / segments), sides, bark);
            int clumps = open ? 10 : 9;
            var centres = new Vector3[clumps]; var sizes = new float[clumps];
            for (int i = 0; i < clumps; i++)
            {
                float h = i / (clumps - 1f), angle = phase + i * 2.39996f;
                float away = radius * Mathf.Lerp(.82f, .16f, h * h) * Mathf.Lerp(.75f, 1.1f, rng.Next());
                centres[i] = Stem(Mathf.Lerp(bottom + .06f, .97f, h)) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * away + Vector3.up * (rng.Next() - .3f);
                sizes[i] = Mathf.Lerp(2.1f, 1.5f, h) * Mathf.Lerp(.86f, 1.16f, rng.Next());
            }
            var middle = Stem((bottom + 1f) * .5f + .06f); float span = Height * (1f - bottom);
            if (level == 3) b.Blob(middle, new Vector3(radius * 1.05f, span * .44f, radius * 1.05f), 2, needles * .8f, middle, .5f, 1f, rng);
            else if (level == 2)
            {
                // Three masses stand for the clumps: the crown stays open and lop-sided from far off.
                for (int i = 0; i < 3; i++)
                {
                    // Pushed out from the stem as far as the near level's boughs reach, so the outline keeps its width.
                    var c = (centres[i * 3] + centres[i * 3 + 1] + centres[i * 3 + 2]) / 3f;
                    c += Vector3.Scale(c - middle, new Vector3(.7f, 0f, .7f));
                    b.Blob(c, new Vector3(radius * .74f, span * .2f, radius * .74f) * (i == 2 ? .8f : 1f), 1, needles * .8f * Mathf.Lerp(.86f, 1.06f, i / 2f), middle, .55f, 1f, rng);
                }
            }
            else
                for (int i = 0; i < clumps; i++)
                {
                    float r = sizes[i]; var c = centres[i];
                    float tint = Mathf.Lerp(.84f, 1.08f, i / (clumps - 1f)) * Mathf.Lerp(.92f, 1.08f, rng.Next());
                    if (level == 0) b.Tube(Stem(Mathf.Clamp01(c.y / Height - .09f)), c - Vector3.up * r * .25f, .10f, .04f, 3, bark);
                    b.Blob(c, new Vector3(r, r * .78f, r) * .82f, level == 0 ? 0 : 1, needles * tint * .78f, middle, .6f, 1f, rng);
                    int sprays = level == 0 ? 8 : 4;
                    for (int k = 0; k < sprays; k++)
                    {
                        float angle = phase + i + k * 2.39996f + (rng.Next() - .5f) * .6f, lift = Mathf.Lerp(-.15f, 1.2f, (k + rng.Next()) / sprays);
                        var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                        float length = r * Mathf.Lerp(1.15f, 1.5f, rng.Next()) * (level == 0 ? 1f : 1.25f);
                        b.Bough(c + outward * r * .15f - Vector3.up * r * .15f, outward, length * Mathf.Cos(lift), length * Mathf.Sin(lift), length * .1f,
                            length * .95f, .4f, 2 + (i + k) % 2, needles * tint * Mathf.Lerp(.9f, 1.1f, rng.Next()), 2);
                    }
                }
            crown = new TreeCrown { centre = new Vector2(middle.x, middle.z), bottom = bottom * Height, top = Height, ring = middle.y, radius = radius * .8f };
            return name;
        }

        /// <summary>Broadleaf crowns: several overlapping leaf masses on a few limbs, with sprays breaking the outline.</summary>
        static string Broadleaf(Builder b, Shape shape, int level, out TreeCrown crown)
        {
            string name; int seed, lumps; float trunk, top, spread, girth; Color leaf, bark = Wood;
            switch (shape)
            {
                case Shape.Alder: name = "Rounded alder crown"; seed = 3; lumps = 7; trunk = 8f; top = 23f; spread = 5.2f; girth = .40f; leaf = new Color(.048f, .094f, .026f); break;
                case Shape.Birch: name = "Slender birch crown"; seed = 5; lumps = 6; trunk = 9f; top = 24f; spread = 3.5f; girth = .24f; leaf = new Color(.074f, .120f, .032f); bark = new Color(.46f, .45f, .40f); break;
                case Shape.SpreadingOak: name = "Spreading woodland crown"; seed = 7; lumps = 8; trunk = 6.5f; top = 20.5f; spread = 7.4f; girth = .52f; leaf = new Color(.042f, .083f, .022f); break;
                case Shape.Umbrella: name = "Tropical umbrella crown"; seed = 13; lumps = 8; trunk = 15f; top = 24f; spread = 7.8f; girth = .46f; leaf = new Color(.045f, .098f, .024f); break;
                case Shape.Emergent: name = "Tall rainforest crown"; seed = 17; lumps = 7; trunk = 14f; top = 26.5f; spread = 5.4f; girth = .5f; leaf = new Color(.030f, .074f, .022f); break;
                default: name = "Low coastal broadleaf"; seed = 19; lumps = 6; trunk = 4.5f; top = 13.5f; spread = 5.4f; girth = .34f; leaf = new Color(.058f, .104f, .029f); break;
            }
            var rng = new Dice(7219 + seed * 739);
            float phase = rng.Next() * 6.28f;
            var radii = new Vector3(spread, (top - trunk) * .5f, spread * .92f);
            var centre = new Vector3(Mathf.Cos(phase) * spread * .08f, (top + trunk) * .5f, Mathf.Sin(phase) * spread * .08f);
            var fork = new Vector3(centre.x * .5f, trunk + radii.y * .3f, centre.z * .5f);
            if (level == 0)
            {
                for (int i = 0; i < 3; i++) b.Tube(Vector3.Lerp(Vector3.zero, fork, i / 3f), Vector3.Lerp(Vector3.zero, fork, (i + 1) / 3f),
                    Mathf.Lerp(girth, girth * .55f, i / 3f), Mathf.Lerp(girth, girth * .55f, (i + 1) / 3f), 6, bark);
            }
            else if (level <= 2) b.Tube(Vector3.zero, fork, girth, girth * .6f, level == 1 ? 4 : 3, bark);
            if (level == 3) b.Blob(centre, radii * .96f, 2, leaf * .9f, centre, 0f, 1f, rng);
            else if (level == 2)
            {
                // A top and three shoulders: lumpy from above, where the camera is.
                b.Blob(centre + Vector3.up * radii.y * .32f, Vector3.Scale(radii, new Vector3(.66f, .62f, .66f)), 1, leaf * .94f, centre, .45f, 1f, rng);
                for (int i = 0; i < 3; i++)
                {
                    float angle = phase + i * Mathf.PI * 2f / 3f;
                    var c = centre + new Vector3(Mathf.Cos(angle) * radii.x, -radii.y * .3f, Mathf.Sin(angle) * radii.z) * .46f;
                    b.Blob(c, Vector3.Scale(radii, new Vector3(.6f, .62f, .6f)), 1, leaf * .9f * Mathf.Lerp(.9f, 1.02f, i / 2f), centre, .45f, 1f, rng);
                }
            }
            else
                for (int i = 0; i < lumps; i++)
                {
                    // Spread over the crown from its top to below its middle.
                    float y = Mathf.Lerp(.88f, -.42f, i / (lumps - 1f)), angle = phase + i * 2.39996f, ring = Mathf.Sqrt(1f - y * y);
                    var direction = new Vector3(Mathf.Cos(angle) * ring, y, Mathf.Sin(angle) * ring);
                    var c = centre + Vector3.Scale(direction, radii) * .5f;
                    var size = radii * .56f * Mathf.Lerp(.86f, 1.16f, rng.Next());
                    float tint = Mathf.Lerp(.9f, 1.1f, rng.Next());
                    if (level == 0 && i > 0 && i < 5) b.Tube(fork - Vector3.up * .5f, c - Vector3.up * size.y * .3f, girth * .34f, girth * .12f, 3, bark);
                    // The shade between the sprays, which carry the lit colour.
                    b.Blob(c, size * .86f, level == 0 ? 0 : 1, leaf * tint * .84f, centre, .5f, 1f, rng);
                    int sprays = level == 0 ? 15 : 8;
                    for (int k = 0; k < sprays; k++)
                    {
                        float sy = Mathf.Lerp(.95f, -.3f, (k + .5f) / sprays), sa = phase + i * 1.3f + k * 2.39996f, sr = Mathf.Sqrt(1f - sy * sy);
                        var d = new Vector3(Mathf.Cos(sa) * sr, sy, Mathf.Sin(sa) * sr);
                        var p = c + Vector3.Scale(d, size) * .88f;
                        // Sprays that would sit inside the crown are never seen.
                        var local = new Vector3((p.x - centre.x) / radii.x, (p.y - centre.y) / radii.y, (p.z - centre.z) / radii.z);
                        if (local.magnitude < .6f) continue;
                        float extent = Mathf.Max(size.x, size.z) * Mathf.Lerp(1.15f, 1.6f, rng.Next()) * (level == 0 ? 1f : 1.3f);
                        // Tipped off the surface: edges stand out past the outline, and no two lie in one plane.
                        var tipped = (d + new Vector3(rng.Next() - .5f, rng.Next() - .5f, rng.Next() - .5f) * 1.1f).normalized;
                        b.Spray(p, tipped, extent, rng.Next() * 6.28f, (int)(rng.Next() * 3.99f), leaf * tint * Shade(p, centre, radii), (p - centre).normalized);
                    }
                }
            crown = new TreeCrown { centre = new Vector2(centre.x, centre.z), bottom = trunk, top = top, ring = centre.y - radii.y * .1f, radius = spread * .82f };
            return name;
        }

        /// <summary>Palms and pandanus: fronds are their own geometry, there is nothing to cut out of a card.</summary>
        static string Palm(Builder b, Shape shape, int level, out TreeCrown crown)
        {
            var trunk = new Color(.16f, .125f, .085f);
            if (shape == Shape.Pandanus)
            {
                var green = new Color(.055f, .108f, .026f);
                int sides = level == 0 ? 6 : level == 1 ? 4 : 3;
                if (level < 3) b.Tube(Vector3.zero, new Vector3(0, 7, 0), .35f, .20f, sides, trunk);
                if (level == 3) b.Blob(new Vector3(0, 10.4f, 0), new Vector3(5.2f, 2.3f, 5.2f), 2, green, new Vector3(0, 9.5f, 0), .3f, 1f, null);
                else
                    for (int branch = 0; branch < 3; branch++)
                    {
                        float a = branch * 2.39996f; var end = new Vector3(Mathf.Cos(a) * 2.8f, 10f + branch * .8f, Mathf.Sin(a) * 2.8f);
                        b.Tube(new Vector3(0, 5, 0), end, .19f, .10f, 3, trunk);
                        if (level == 2) { b.Skirt(end - Vector3.up * 1.6f, end + Vector3.up * .9f, 3.3f, 0f, 5, branch, .15f, .4f, green * .8f, green * 1.1f, null); continue; }
                        int spokes = level == 0 ? 8 : 6;
                        for (int spoke = 0; spoke < spokes; spoke++)
                        {
                            float angle = spoke * Mathf.PI * 2f / spokes + branch;
                            b.Frond(end, new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)), 3.8f, level == 0 ? .48f : .62f, 2.5f, green * Mathf.Lerp(.86f, 1.1f, spoke % 3 / 2f), level == 0 ? 2 : 1);
                        }
                    }
                if (level == 0) for (int root = 0; root < 4; root++) { float a = root * Mathf.PI * .5f; b.Tube(new Vector3(Mathf.Cos(a) * 1.2f, 0, Mathf.Sin(a) * 1.2f), new Vector3(0, 3.4f, 0), .09f, .14f, 3, trunk); }
                crown = new TreeCrown { bottom = 8f, top = 11.8f, ring = 9.6f, radius = 4.4f };
                return "Low pandanus crown";
            }
            bool fan = shape == Shape.FanPalm;
            float height = fan ? 14f : shape == Shape.LeaningPalm ? 22f : 26f;
            float lean = fan ? .6f : shape == Shape.LeaningPalm ? 4.6f : 1.3f;
            Vector3 Stem(float t) => new Vector3(lean * t * t, height * t, .6f * t * t);
            int segments = level == 0 ? 4 : level == 1 ? 3 : level == 2 ? 2 : 1, around = level == 0 ? 7 : level == 1 ? 5 : 3;
            for (int i = 0; i < segments; i++)
                b.Tube(Stem(i / (float)segments), Stem((i + 1f) / segments), Mathf.Lerp(.42f, .19f, i / (float)segments), Mathf.Lerp(.42f, .19f, (i + 1f) / segments), around, trunk);
            var frond = new Color(.052f, .112f, .022f);
            float longest = fan ? 5.2f : 8.4f;
            if (level == 3) b.Skirt(Stem(1f) - Vector3.up * (fan ? 1.6f : 2.2f), Stem(1f) + Vector3.up * 1.2f, longest * .78f, 0f, 5, 1f, .25f, .5f, frond * .8f, frond * 1.1f, null);
            else
            {
                int count = level == 2 ? 6 : fan ? 11 : 10;
                for (int i = 0; i < count; i++)
                {
                    float angle = i * Mathf.PI * 2f / count;
                    float width = (fan ? 1.45f : .90f) * (level == 2 ? 1.7f : 1f);
                    b.Frond(Stem(1f), new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)), fan ? 5.2f : i % 3 == 0 ? 6.4f : 8.4f, width,
                        fan ? 2.8f : i % 2 == 0 ? 4.2f : 2.7f, frond * Mathf.Lerp(.83f, 1.12f, (i % 4) / 3f), level == 0 ? 3 : level == 1 ? 2 : 1);
                }
            }
            var head = Stem(1f);
            crown = new TreeCrown { centre = new Vector2(head.x, head.z), bottom = height - 3f, top = height + 1.2f, ring = height - .6f, radius = longest * .7f };
            return fan ? "Short fan palm" : shape == Shape.LeaningPalm ? "Leaning coconut palm" : "Tall coconut palm";
        }

        /// <summary>A crown's own shading: darker towards its middle and its underside, where leaves shade leaves.</summary>
        static float Shade(Vector3 p, Vector3 centre, Vector3 radii)
        {
            var local = new Vector3((p.x - centre.x) / radii.x, (p.y - centre.y) / radii.y, (p.z - centre.z) / radii.z);
            return Mathf.Lerp(.5f, 1f, Mathf.Clamp01(local.magnitude)) * Mathf.Lerp(.62f, 1.08f, Mathf.Clamp01(local.y * .5f + .5f));
        }

        /// <summary>Four leafy sprays (2 x 2) as brightness and coverage: small pointed leaves, the upper ones lighter.</summary>
        static Texture2D LeafSprays(out Color mean)
        {
            const int Size = 256, Tile = Size / 2;
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(150, 160, 140, 0);
            var rng = new Dice(90210);
            for (int tile = 0; tile < 4; tile++)
            {
                float cx = (tile % 2 + .5f) * Tile, cy = (tile / 2 + .5f) * Tile, lobes = 3f + tile, turn = rng.Next() * 6.28f;
                const int Leaves = 150;
                for (int leaf = 0; leaf < Leaves; leaf++)
                {
                    // Dense in the middle, ragged at an uneven edge; later leaves lie on top and catch the light.
                    float angle = rng.Next() * 6.28f, reach = Mathf.Sqrt(rng.Next()) * Tile * .43f * (1f + .2f * Mathf.Sin(angle * lobes + turn));
                    float x = cx + Mathf.Cos(angle) * reach, y = cy + Mathf.Sin(angle) * reach;
                    float length = Tile * Mathf.Lerp(.062f, .10f, rng.Next()), width = length * Mathf.Lerp(.36f, .5f, rng.Next());
                    float heading = angle + (rng.Next() - .5f) * 2.4f, c = Mathf.Cos(heading), s = Mathf.Sin(heading);
                    float light = Mathf.Lerp(.6f, 1f, leaf / (Leaves - 1f)) * Mathf.Lerp(.9f, 1.1f, rng.Next()), warm = rng.Next();
                    int span = Mathf.CeilToInt(length);
                    for (int py = -span; py <= span; py++)
                    for (int px = -span; px <= span; px++)
                    {
                        float along = (px * c + py * s) / length, across = (-px * s + py * c) / width;
                        // A leaf: widest below its middle, drawn to a point.
                        float edge = (1f - along * along) * Mathf.Lerp(1f, .55f, along * .5f + .5f);
                        if (Mathf.Abs(along) > 1f || Mathf.Abs(across) > edge) continue;
                        int ix = Mathf.RoundToInt(x) + px, iy = Mathf.RoundToInt(y) + py;
                        if (ix < tile % 2 * Tile + 1 || ix >= (tile % 2 + 1) * Tile - 1 || iy < tile / 2 * Tile + 1 || iy >= (tile / 2 + 1) * Tile - 1) continue;
                        float vein = Mathf.Abs(across) < .12f ? .86f : 1f, value = light * vein * Mathf.Lerp(.9f, 1.06f, Mathf.Abs(across) / Mathf.Max(.05f, edge));
                        pixels[iy * Size + ix] = new Color32((byte)Mathf.Clamp(255f * value * Mathf.Lerp(.86f, 1f, warm), 0f, 255f),
                            (byte)Mathf.Clamp(255f * value, 0f, 255f), (byte)Mathf.Clamp(255f * value * Mathf.Lerp(.9f, .74f, warm), 0f, 255f), 255);
                    }
                }
            }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true) { name = "Broadleaf sprays", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            texture.SetPixels32(pixels);
            // Averaging thins coverage out with every mip, and a far crown would lose its sprays: keep the covered
            // share of each mip equal to the full-size one's.
            int full = 0; foreach (var pixel in pixels) if (pixel.a > 107) full++;
            var previous = pixels; int size = Size;
            for (int mip = 1; size > 1; mip++)
            {
                int half = size / 2; var next = new Color32[half * half]; var alpha = new float[half * half];
                for (int y = 0; y < half; y++)
                for (int x = 0; x < half; x++)
                {
                    float r0 = 0, g0 = 0, b0 = 0, a0 = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        var source = previous[(y * 2 + k / 2) * size + x * 2 + k % 2]; float weight = source.a / 255f;
                        r0 += source.r * weight; g0 += source.g * weight; b0 += source.b * weight; a0 += weight;
                    }
                    next[y * half + x] = a0 > 0f ? new Color32((byte)(r0 / a0), (byte)(g0 / a0), (byte)(b0 / a0), 0) : new Color32(150, 160, 140, 0);
                    alpha[y * half + x] = a0 * .25f;
                }
                var sorted = (float[])alpha.Clone(); Array.Sort(sorted);
                int keep = Mathf.Clamp(Mathf.RoundToInt(full / (float)(Size * Size) * sorted.Length), 1, sorted.Length);
                float threshold = Mathf.Max(.02f, sorted[sorted.Length - keep]), scale = .43f / threshold;
                for (int i = 0; i < next.Length; i++) next[i].a = (byte)Mathf.Clamp(alpha[i] * scale * 255f, 0f, 255f);
                texture.SetPixels32(next, mip); previous = next; size = half;
            }
            texture.Apply(false, true);
            // The texture is linear data: its bytes are the values the shader reads.
            double r = 0, g = 0, bl = 0; int covered = 0;
            foreach (var pixel in pixels) if (pixel.a > 0) { r += pixel.r; g += pixel.g; bl += pixel.b; covered++; }
            mean = new Color((float)(r / covered / 255.0), (float)(g / covered / 255.0), (float)(bl / covered / 255.0));
            return texture;
        }

        /// <summary>One shared stream per model: WorldRandom is a struct, and a copy handed to the builder would
        /// repeat the same values for every part.</summary>
        sealed class Dice
        {
            WorldRandom random;
            public Dice(int seed) { random = new WorldRandom(seed); }
            public float Next() => random.Next();
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Color> colors = new List<Color>(); readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] triangles = { new List<int>(), new List<int>() };
            int Vertex(Vector3 p, Vector3 n, Color color, Vector2 t = default)
            { int i = vertices.Count; vertices.Add(p); normals.Add(n.normalized); color.a = 1f; colors.Add(color); uv.Add(t); return i; }
            void Quad(int sub, int a, int b, int c, int d) { triangles[sub].AddRange(new[] { a, b, c, a, c, d }); }
            void Triangle(int sub, int a, int b, int c) { triangles[sub].Add(a); triangles[sub].Add(b); triangles[sub].Add(c); }

            public void Tube(Vector3 a, Vector3 b, float ra, float rb, int sides, Color color)
            {
                var along = (b - a).normalized; var right = Vector3.Cross(along, Mathf.Abs(along.y) > .9f ? Vector3.forward : Vector3.up).normalized; var forward = Vector3.Cross(right, along);
                int first = vertices.Count;
                for (int row = 0; row < 2; row++)
                for (int side = 0; side <= sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / sides; var n = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                    Vertex((row == 0 ? a : b) + n * (row == 0 ? ra : rb), n, color * (row == 0 && a.y < .5f ? .8f : 1f));
                }
                for (int side = 0; side < sides; side++) Quad(0, first + side, first + side + sides + 1, first + side + sides + 2, first + side + 1);
            }

            /// <summary>One tier of a conifer, or any drooping cone: an uneven lower rim up to a ring or a point.</summary>
            public void Skirt(Vector3 bottom, Vector3 top, float rim, float neck, int sides, float turn, float uneven, float droop, Color low, Color high, Dice rng)
            {
                int first = vertices.Count; float rise = Mathf.Max(.01f, top.y - bottom.y);
                for (int side = 0; side < sides; side++)
                {
                    float angle = turn + side * Mathf.PI * 2f / sides, jitter = rng != null ? rng.Next() : side % 2;
                    var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    float r = rim * (1f + (jitter - .5f) * 2f * uneven);
                    // Boughs face the sky more than the outline they add up to does.
                    var n = outward * rise + Vector3.up * (rim - neck) + Vector3.up * rise * .55f;
                    Vertex(bottom + outward * r - Vector3.up * droop * jitter, n, low * Mathf.Lerp(.92f, 1.1f, jitter));
                    Vertex(top + outward * neck, neck > 0f ? n : n + Vector3.up * rise, high);
                }
                for (int side = 0; side < sides; side++)
                {
                    int a = first + side * 2, c = first + (side + 1) % sides * 2;
                    if (neck > 0f) Quad(0, a, c, c + 1, a + 1); else Triangle(0, a, c, a + 1);
                }
            }

            /// <summary>A leaf or needle mass: a low-poly rounded shape, flatter underneath, shaded as part of a whole
            /// crown. detail 0 = a closed 20 triangles; 1 = a 15 triangle dome; 2 = a 10 triangle double pyramid (a single far crown).</summary>
            public void Blob(Vector3 centre, Vector3 radii, int detail, Color color, Vector3 crownCentre, float crownNormal, float surface, Dice rng)
            {
                int first = vertices.Count;
                void Point(Vector3 unit)
                {
                    if (unit.y < 0f) unit.y *= .62f;
                    var p = centre + Vector3.Scale(unit, radii) * (rng != null ? Mathf.Lerp(.88f, 1.12f, rng.Next()) : 1f);
                    var n = Vector3.Lerp(unit.normalized, (p - crownCentre).normalized, crownNormal);
                    // Light on top, dark underneath: the mass shades itself.
                    Vertex(p, n, color * Mathf.Lerp(.56f, 1.1f, Mathf.Clamp01(unit.y * .62f + .5f)) * surface);
                }
                if (detail == 0)
                {
                    const float t = 1.618034f;
                    foreach (var v in new[] { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
                        new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) }) Point(v.normalized);
                    foreach (int i in new[] { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 })
                        triangles[0].Add(first + i);
                    return;
                }
                if (detail == 2)
                {
                    // The whole-region crown, a few pixels wide: a double pyramid of 10 triangles.
                    Point(Vector3.up); Point(Vector3.down);
                    for (int i = 0; i < 5; i++) { float a = i * Mathf.PI * 2f / 5 + centre.x; Point(new Vector3(Mathf.Cos(a), .08f, Mathf.Sin(a))); }
                    for (int i = 0; i < 5; i++) { int a = first + 2 + i, c = first + 2 + (i + 1) % 5; Triangle(0, first, c, a); Triangle(0, first + 1, a, c); }
                    return;
                }
                // Domes for the middle levels: nobody sees these from underneath.
                const int ring = 5;
                Point(Vector3.up);
                for (int i = 0; i < ring; i++) { float a = (i + .5f) * Mathf.PI * 2f / ring + centre.x; Point(new Vector3(Mathf.Cos(a) * .76f, .62f, Mathf.Sin(a) * .76f)); }
                for (int i = 0; i < ring; i++) { float a = i * Mathf.PI * 2f / ring + centre.x; Point(new Vector3(Mathf.Cos(a), -.42f, Mathf.Sin(a))); }
                for (int i = 0; i < ring; i++)
                {
                    int upper = first + 1 + i, next = first + 1 + (i + 1) % ring, lower = first + 1 + ring + i, lowerNext = first + 1 + ring + (i + 1) % ring;
                    Triangle(0, first, next, upper); Triangle(0, upper, next, lowerNext); Triangle(0, upper, lowerNext, lower);
                }
            }

            /// <summary>A conifer bough: the branch atlas folded along its stem into a ridge, from the trunk to the tip,
            /// arching by rise and hanging by sag. The sides hang by hang x half the width, so a bough has depth from
            /// the side as well as from above. Tiles: 0-1 spruce, 2-3 pine. upright: stands on its root (the leader).</summary>
            public void Bough(Vector3 root, Vector3 outward, float reach, float rise, float sag, float width, float hang, int tile, Color color, int rows, bool upright = false)
            {
                var lateral = upright ? outward : Vector3.Cross(Vector3.up, outward).normalized;
                var facing = upright ? Vector3.Cross(Vector3.up, outward).normalized + Vector3.up * .5f : Vector3.up + outward * .55f;
                float u = tile % 2 * .5f, v = tile < 2 ? .5f : 0f; int first = vertices.Count; int across = hang > 0f ? 3 : 2;
                for (int row = 0; row < rows; row++)
                {
                    float s = row / (rows - 1f);
                    var p = root + (upright ? Vector3.zero : outward * reach * s) + Vector3.up * (rise * s - sag * s * s);
                    // Dark where it leaves the stem, light at the tip that reaches the sun.
                    var shade = color * Mathf.Lerp(.5f, 1.1f, s); float v0 = v + .008f + .484f * s;
                    var drop = Vector3.up * hang * width * .5f;
                    Vertex(p - lateral * width * .5f - drop, facing - lateral * (.4f + hang), shade * (hang > 0f ? .86f : 1f), new Vector2(u + .008f, v0));
                    if (hang > 0f) Vertex(p, facing, shade, new Vector2(u + .25f, v0));
                    Vertex(p + lateral * width * .5f - drop, facing + lateral * (.4f + hang), shade * (hang > 0f ? .86f : 1f), new Vector2(u + .492f, v0));
                }
                for (int row = 0; row < rows - 1; row++)
                for (int i = 0; i < across - 1; i++)
                    Quad(1, first + row * across + i, first + (row + 1) * across + i, first + (row + 1) * across + i + 1, first + row * across + i + 1);
            }

            /// <summary>A leaf spray lying on a crown's surface, a little askew so it shows past the outline.</summary>
            public void Spray(Vector3 centre, Vector3 facing, float size, float spin, int tile, Color color, Vector3 crownNormal)
            {
                facing = (facing + crownNormal * .35f).normalized;
                var right = Vector3.Cross(Mathf.Abs(facing.y) > .9f ? Vector3.forward : Vector3.up, facing).normalized; var up = Vector3.Cross(facing, right);
                var x = (right * Mathf.Cos(spin) + up * Mathf.Sin(spin)) * size * .5f; var y = (up * Mathf.Cos(spin) - right * Mathf.Sin(spin)) * size * .5f;
                var n = Vector3.Lerp(facing, crownNormal, .5f).normalized;
                float u = tile % 2 * .5f, v = tile / 2 * .5f; int first = vertices.Count;
                Vertex(centre - x - y, n - (x + y).normalized * .45f, color, new Vector2(u + .01f, v + .01f));
                Vertex(centre - x + y, n - (x - y).normalized * .45f, color, new Vector2(u + .01f, v + .49f));
                Vertex(centre + x + y, n + (x + y).normalized * .45f, color, new Vector2(u + .49f, v + .49f));
                Vertex(centre + x - y, n + (x - y).normalized * .45f, color, new Vector2(u + .49f, v + .01f));
                Quad(1, first, first + 1, first + 2, first + 3);
            }

            public void Frond(Vector3 root, Vector3 direction, float length, float width, float droop, Color color, int segments)
            {
                int first = vertices.Count; var across = Vector3.Cross(Vector3.up, direction).normalized;
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments; var p = root + direction * length * t + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.1f - droop * t * t);
                    float half = Mathf.Max(segments == 1 ? width * .45f : 0f, Mathf.Sin(t * Mathf.PI) * width);
                    var shade = color * Mathf.Lerp(.7f, 1.08f, t);
                    Vertex(p - across * half, Vector3.up + direction * .5f - across * .3f, shade); Vertex(p + across * half, Vector3.up + direction * .5f + across * .3f, shade);
                }
                for (int i = 0; i < segments; i++) Quad(0, first + i * 2, first + i * 2 + 2, first + i * 2 + 3, first + i * 2 + 1);
            }

            public Mesh Mesh(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = 2 }; mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetUVs(0, uv);
                mesh.SetTriangles(triangles[0], 0); mesh.SetTriangles(triangles[1], 1); mesh.RecalculateBounds(); mesh.UploadMeshData(true); return mesh;
            }
        }

        T Own<T>(T value) where T : Object { value.hideFlags = HideFlags.HideAndDontSave; owned.Add(value); return value; }
        public void Dispose()
        {
            foreach (var value in owned) if (value != null) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
            owned.Clear(); Library = null;
        }
    }
}
