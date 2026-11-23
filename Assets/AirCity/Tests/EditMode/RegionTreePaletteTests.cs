using System.Linq;
using AirCity.EditorTools.World;
using AirCity.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class RegionTreePaletteTests
    {
        static TreeLibrary Source => AssetDatabase.LoadAssetAtPath<TreeLibrary>(WorldAssetSetup.TreesPath);
        static long Triangles(Mesh mesh) => Enumerable.Range(0, mesh.subMeshCount).Sum(sub => (long)mesh.GetIndexCount(sub) / 3);

        static long Solid(Mesh mesh) => mesh.GetIndexCount(0) / 3;
        static long Cards(Mesh mesh) => mesh.GetIndexCount(1) / 3;

        static void AssertSolidFirstLevels(TreeVariant variant)
        {
            Assert.AreEqual(RegionTreePalette.Levels, variant.levels.Length, variant.name);
            Assert.LessOrEqual(Triangles(variant.levels[0]), 900, variant.name);
            Assert.LessOrEqual(Triangles(variant.levels[1]), 350, variant.name);
            Assert.LessOrEqual(Triangles(variant.levels[2]), 70, variant.name);
            Assert.LessOrEqual(Triangles(variant.levels[3]), 24, variant.name);
            for (int level = 1; level < RegionTreePalette.Levels; level++)
                Assert.Less(Triangles(variant.levels[level]), Triangles(variant.levels[level - 1]), variant.name);
            // Far trees are opaque shapes: no alpha-tested cards, so nothing to overdraw or to shimmer.
            Assert.AreEqual(0, Cards(variant.levels[2]), variant.name); Assert.AreEqual(0, Cards(variant.levels[3]), variant.name);
            Assert.Greater(Solid(variant.levels[3]), 0, variant.name);
            foreach (var mesh in variant.levels)
            {
                Assert.AreEqual(2, mesh.subMeshCount);
                Assert.That(mesh.bounds.max.y, Is.InRange(10f, 30f), variant.name);
            }
            Assert.Greater(variant.crown.radius, 1f, variant.name + ": the shadow crown comes from the generator.");
            Assert.Greater(variant.crown.top, variant.crown.bottom);
            Assert.IsTrue(variant.bark.enableInstancing && variant.foliage.enableInstancing);
            Assert.AreEqual("AirCity/TreeCanopy", variant.bark.shader.name);
            Assert.IsFalse(variant.bark.IsKeywordEnabled("_CARDS"), "Trunks and crown volumes are opaque.");
        }

        [Test]
        public void WoodlandHasSixEvergreensAndThreeBroadleafCrownsAsSolidShapesWithBoughsOnlyNearby()
        {
            var source = Source; Assert.IsNotNull(source);
            var atlas = source.foliage.GetTexture("_BaseMap"); Assert.IsNotNull(atlas);
            using (var palette = RegionTreePalette.Create(source, RegionPreset.Woodland))
            {
                var variants = palette.Library.variants;
                Assert.AreEqual(9, variants.Length);
                foreach (var variant in variants) AssertSolidFirstLevels(variant);
                for (int i = 0; i < 6; i++)
                {
                    Assert.IsTrue(variants[i].name.Contains("fir") || variants[i].name.Contains("spruce") || variants[i].name.Contains("pine"), variants[i].name);
                    Assert.Greater(Cards(variants[i].levels[0]), 100, "Evergreens carry boughs nearby.");
                    Assert.AreSame(atlas, variants[i].foliage.GetTexture("_BaseMap"), "Conifer boughs reuse the authored branch atlas.");
                    Assert.IsTrue(variants[i].foliage.IsKeywordEnabled("_CARDS"));
                }
                // A fir is a spire, a pine carries its crown high on a bare stem.
                Assert.Less(variants[0].crown.bottom, 5f); Assert.Greater(variants[4].crown.bottom, 10f);
                var alder = variants[6]; var birch = variants[7]; var spreading = variants[8];
                Assert.Greater(spreading.levels[0].bounds.size.x, birch.levels[0].bounds.size.x * 1.4f);
                Assert.Greater(birch.levels[0].bounds.size.y / birch.levels[0].bounds.size.x,
                    alder.levels[0].bounds.size.y / alder.levels[0].bounds.size.x);
                // The far shape keeps the near one's outline, so a tree does not change size as the camera moves.
                foreach (var variant in variants)
                    Assert.That(variant.levels[2].bounds.size.x, Is.InRange(variant.levels[0].bounds.size.x * .55f, variant.levels[0].bounds.size.x * 1.15f), variant.name);
                using var mountains = RegionTreePalette.Create(source, RegionPreset.Mountains);
                Assert.AreEqual(6, mountains.Library.variants.Length, "Mountain forests are evergreen.");
            }
            Assert.IsNotNull(source.foliage); Assert.IsNotNull(source.foliage.GetTexture("_BaseMap"), "Disposing a runtime palette must preserve authored assets.");
        }

        [Test]
        public void WoodlandIsMostlyEvergreenWithBroadleafGroves()
        {
            using var palette = RegionTreePalette.Create(Source, RegionPreset.Woodland);
            var world = new ValleyWorld { preset = RegionPreset.Woodland, seed = 1986 };
            int evergreen = 0, total = 0; var seen = new int[9];
            for (int z = -2500; z <= 2500; z += 250)
            for (int x = -2500; x <= 2500; x += 250)
            for (int i = 0; i < 20; i++)
            { int variant = palette.Variant(world, new Vector2(x, z), (i + .5f) / 20f); seen[variant]++; total++; if (variant < 6) evergreen++; }
            Assert.Greater(evergreen, total * .6f); Assert.Less(evergreen, total * .95f);
            Assert.IsTrue(seen.All(count => count > 0), "Every woodland form grows somewhere.");
        }

        [Test]
        public void TropicalCrownsAndPalmsAreSolidShapesWithBoundedGeometry()
        {
            using var palette = RegionTreePalette.Create(Source, RegionPreset.Tropical);
            var variants = palette.Library.variants;
            Assert.AreEqual(7, variants.Length);
            Assert.GreaterOrEqual(variants.Count(v => v.name.Contains("palm")), 3);
            Assert.GreaterOrEqual(variants.Count(v => v.name.Contains("crown") || v.name.Contains("broadleaf")), 4);
            Assert.Greater(variants[0].levels[0].bounds.size.y, variants[2].levels[0].bounds.size.y * 1.4f,
                "Tall coconut and short fan palms must retain different silhouettes at a shared root scale.");
            foreach (var variant in variants)
            {
                AssertSolidFirstLevels(variant);
                // Fronds are geometry; only branched crowns carry leaf sprays.
                if (variant.name.Contains("palm") || variant.name.Contains("pandanus")) Assert.AreEqual(0, Cards(variant.levels[0]), variant.name);
                else Assert.Greater(Cards(variant.levels[0]), 40, variant.name);
            }
            using var desert = RegionTreePalette.Create(null, RegionPreset.Desert);
            Assert.AreEqual(2, desert.Library.variants.Length, "Warm regions need no authored conifer assets.");
        }

        [Test]
        public void TropicalInteriorUsesRainforestCrownsWhileMappedCoastsFavorPalmsAndSelectionStaysStable()
        {
            using var palette = RegionTreePalette.Create(Source, RegionPreset.Tropical);
            var world = new ValleyWorld { preset = RegionPreset.Tropical,
                landCover = Enumerable.Repeat((byte)10, ValleyWorld.Resolution * ValleyWorld.Resolution).ToArray() };
            int[] interior = new int[7], coast = new int[7];
            for (int i = 0; i < 1000; i++) interior[palette.Variant(world, Vector2.zero, i / 1000f)]++;
            int stable = palette.Variant(world, Vector2.zero, .7523f);
            world.trees.Add(new ForestTree { position = Vector2.zero, height = 24f, removed = true });
            Assert.AreEqual(stable, palette.Variant(world, Vector2.zero, .7523f));
            world.landCover[256 * ValleyWorld.Resolution + 259] = 80;
            for (int i = 0; i < 1000; i++) coast[palette.Variant(world, Vector2.zero, i / 1000f)]++;
            Assert.Less(interior.Take(3).Sum(), 200); Assert.Greater(coast.Take(3).Sum(), 500);
            Assert.IsTrue(interior.All(count => count > 0)); Assert.IsTrue(coast.All(count => count > 0));
        }
    }
}
