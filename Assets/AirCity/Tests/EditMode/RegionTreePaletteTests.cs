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

        [Test]
        public void WoodlandMixAddsDistinctBroadleafCrownsAndReusesCheaperConifersWithoutOwningSourceAssets()
        {
            var source = Source; Assert.IsNotNull(source);
            var high = source.variants[0].levels[0]; var bark = source.bark;
            using (var palette = RegionTreePalette.Create(source, RegionPreset.Woodland))
            {
                var variants = palette.Library.variants;
                Assert.AreEqual(source.variants.Length + 3, variants.Length);
                for (int i = 0; i < source.variants.Length; i++)
                {
                    Assert.AreSame(source.variants[i].levels[1], variants[i].levels[0]);
                    Assert.Less(Triangles(variants[i].levels[0]), Triangles(source.variants[i].levels[0]));
                }
                var alder = variants[source.variants.Length]; var birch = variants[source.variants.Length + 1];
                var spreading = variants[source.variants.Length + 2];
                Assert.Greater(spreading.levels[0].bounds.size.x, birch.levels[0].bounds.size.x * 1.4f);
                Assert.Greater(birch.levels[0].bounds.size.y / birch.levels[0].bounds.size.x,
                    alder.levels[0].bounds.size.y / alder.levels[0].bounds.size.x);
                foreach (var variant in variants) Assert.AreEqual(6, Triangles(variant.levels[2]));
            }
            Assert.IsNotNull(high); Assert.IsNotNull(bark);
            Assert.AreSame(high, source.variants[0].levels[0], "Disposing a runtime palette must preserve authored assets.");
        }

        [Test]
        public void TropicalCrownsAndPalmsHaveBoundedGeometryAndSharedCompressedSixTriangleDistanceSilhouettes()
        {
            using var palette = RegionTreePalette.Create(Source, RegionPreset.Tropical);
            var variants = palette.Library.variants;
            Assert.AreEqual(7, variants.Length);
            Assert.GreaterOrEqual(variants.Count(v => v.name.Contains("palm")), 3);
            Assert.GreaterOrEqual(variants.Count(v => v.name.Contains("crown") || v.name.Contains("broadleaf")), 4);
            Assert.Greater(variants[0].levels[0].bounds.size.y, variants[2].levels[0].bounds.size.y * 1.4f,
                "Tall coconut and short fan palms must retain different silhouettes at a shared root scale.");
            var atlas = variants[0].distantFoliage.GetTexture("_BaseMap") as Texture2D;
            Assert.IsNotNull(atlas); Assert.AreEqual(TextureFormat.DXT1, atlas.format);
            Assert.AreEqual(256, atlas.width); Assert.AreEqual(1024, atlas.height);
            foreach (var variant in variants)
            {
                Assert.LessOrEqual(Triangles(variant.levels[0]), 800);
                Assert.LessOrEqual(Triangles(variant.levels[1]), 350);
                Assert.AreEqual(6, Triangles(variant.levels[2]));
                Assert.AreSame(atlas, variant.distantFoliage.GetTexture("_BaseMap"));
                Assert.IsTrue(variant.bark.enableInstancing && variant.foliage.enableInstancing && variant.distantFoliage.enableInstancing);
                foreach (var mesh in variant.levels)
                    Assert.Greater(mesh.bounds.size.sqrMagnitude, 1f);
            }
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
