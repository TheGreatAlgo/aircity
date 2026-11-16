using System.Linq;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class ForestPopulationTests
    {
        [Test]
        public void MappedForestFormsDenseReproducibleStandsWithoutPlantingFieldsOrWater()
        {
            var a = Patch(); var b = Patch();
            ForestPopulation.Generate(a); ForestPopulation.Generate(b);
            // One square kilometre of mapped woodland: at least 100 stems/ha, with clearings retained.
            Assert.Greater(a.trees.Count, 10000); Assert.Less(a.trees.Count, 15626);
            Assert.AreEqual(ForestPopulation.Version, a.forestVersion);
            CollectionAssert.AreEqual(a.trees.Select(t => t.position), b.trees.Select(t => t.position));
            CollectionAssert.AreEqual(a.trees.Select(t => t.height), b.trees.Select(t => t.height));
            foreach (var tree in a.trees)
            {
                Assert.IsTrue(new Rect(-510f, -510f, 1020f, 1020f).Contains(tree.position));
                Assert.IsFalse(a.IsWater(tree.position));
            }
            Assert.Greater(a.trees.Count(t => t.height > 26f), a.trees.Count / 4);
            Assert.Greater(a.trees.Count(t => t.height < 18f), a.trees.Count / 12);
        }

        [Test]
        public void DensityUpgradeAppendsOnceAndProtectsOldRootsClearingsEarthworkAndAirport()
        {
            var world = Patch();
            var retained = new ForestTree { position = new Vector2(-250f, 0f), height = 23f };
            var removed = new ForestTree { position = Vector2.zero, height = 25f, removed = true };
            world.trees.Add(retained); world.trees.Add(removed);
            world.MarkGround(new Vector2(200f, 0f), 55f, .3f);
            var airport = new Rect(-100f, 200f, 200f, 200f);
            var heights = (float[])world.heights.Clone();
            Assert.IsTrue(ForestPopulation.Upgrade(world, airport.Contains));
            Assert.AreSame(retained, world.trees[0]); Assert.AreSame(removed, world.trees[1]);
            Assert.IsTrue(removed.removed); Assert.AreEqual(new Vector2(-250f, 0f), retained.position);
            foreach (var tree in world.trees.Skip(2))
            {
                Assert.GreaterOrEqual(Vector2.Distance(tree.position, retained.position), 4.5f);
                Assert.GreaterOrEqual(tree.position.magnitude, 32f);
                Assert.Greater(Vector2.Distance(tree.position, new Vector2(200f, 0f)), 55f);
                Assert.IsFalse(airport.Contains(tree.position));
            }
            int count = world.trees.Count;
            Assert.IsFalse(ForestPopulation.Upgrade(world)); Assert.AreEqual(count, world.trees.Count);
            CollectionAssert.AreEqual(heights, world.heights, "A density upgrade must never reshape saved terrain.");
            var state = new CareerState { world = world };
            var copy = CareerSnapshot.Capture(state);
            Assert.AreEqual(world.forestVersion, copy.world.forestVersion);
            var loaded = JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(copy.world));
            Assert.IsFalse(ForestPopulation.Upgrade(loaded)); Assert.AreEqual(count, loaded.trees.Count);
        }

        [Test]
        public void DenseStandsKeepRoadsGardensWetGroundAndCliffsClear()
        {
            var world = Patch();
            world.publicRoad.Add(new Vector2(-500f, -250f)); world.publicRoad.Add(new Vector2(500f, -250f));
            world.settlements.Add(new Vector2(250f, 250f));
            world.waterHeights = new float[world.heights.Length];
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                int i = z * ValleyWorld.Resolution + x;
                if (x >= 246 && x <= 254) { world.waterHeights[i] = 90f; world.heights[i] = 85f; }
                if (z >= 278) world.heights[i] = 80f + (z - 278) * world.Spacing * 2f;
            }
            ForestPopulation.Generate(world);
            Assert.IsNotEmpty(world.trees);
            foreach (var tree in world.trees)
            {
                Assert.GreaterOrEqual(Mathf.Abs(tree.position.y + 250f), 24f);
                Assert.GreaterOrEqual(Vector2.Distance(tree.position, new Vector2(250f, 250f)), 145f);
                Assert.IsFalse(world.IsWater(tree.position));
                Assert.Less(tree.position.y, 290f, "Trees must not populate the steep cliff above the patch.");
            }
        }

        [Test]
        public void LegacyWorldWithoutMappedHabitatIsNotRescattered()
        {
            var world = Patch(); world.generatorVersion = 3;
            Assert.IsFalse(ForestPopulation.Upgrade(world)); Assert.IsEmpty(world.trees);
        }

        [Test]
        public void BoraBoraFormsDenseForestOnItsMappedIslandsWithoutPlantingTheLagoon()
        {
            var world = ValleyWorld.Generate(445090, RegionPreset.Tropical);
            Assert.IsTrue(world.Validate(out string why), why);
            Assert.Greater(world.trees.Count, 70000, "Tropical canopy needs substantially more than the previous 32,922 palms.");
            Assert.Less(world.trees.Count, 170000);
            foreach (var tree in world.trees) Assert.IsFalse(world.IsWater(tree.position));
            // This narrow mapped motu has about half a hectare of forest. Ocean clearance must
            // not erase its vegetation while the mainland receives a dense tropical canopy.
            var pending = new System.Collections.Generic.Queue<int>();
            var island = new System.Collections.Generic.HashSet<int>();
            pending.Enqueue(88 * ValleyWorld.Resolution + 349);
            while (pending.Count > 0)
            {
                int i = pending.Dequeue();
                if (i < 0 || i >= world.landCover.Length || world.landCover[i] == 80 || !island.Add(i)) continue;
                int x = i % ValleyWorld.Resolution, z = i / ValleyWorld.Resolution;
                if (x > 0) pending.Enqueue(i - 1); if (x + 1 < ValleyWorld.Resolution) pending.Enqueue(i + 1);
                if (z > 0) pending.Enqueue(i - ValleyWorld.Resolution); if (z + 1 < ValleyWorld.Resolution) pending.Enqueue(i + ValleyWorld.Resolution);
            }
            Assert.That(island.Count, Is.InRange(20, 60), "The regression must inspect the small motu, not the main island.");
            int roots = world.trees.Count(t => island.Contains(
                Mathf.RoundToInt((t.position.y + ValleyWorld.Size * .5f) / world.Spacing) * ValleyWorld.Resolution +
                Mathf.RoundToInt((t.position.x + ValleyWorld.Size * .5f) / world.Spacing)));
            Assert.GreaterOrEqual(roots, 10, "A small wooded motu should retain a real grove above the local wash zone.");
        }

        [Test]
        public void VersionOneTropicalSaveGainsTreesOnceAndPreservesClearedRoots()
        {
            var world = Patch(); ForestPopulation.Generate(world);
            world.preset = RegionPreset.Tropical; world.forestVersion = 1;
            var originals = world.trees.ToArray(); originals[originals.Length / 2].removed = true;
            var cleared = originals[originals.Length / 2].position;
            Assert.IsTrue(ForestPopulation.Upgrade(world));
            Assert.Greater(world.trees.Count, originals.Length);
            for (int i = 0; i < originals.Length; i++) Assert.AreSame(originals[i], world.trees[i]);
            foreach (var tree in world.trees.Skip(originals.Length)) Assert.GreaterOrEqual(Vector2.Distance(tree.position, cleared), 32f);
            Assert.IsFalse(ForestPopulation.Upgrade(world));
        }

        [Test]
        public void VersionOneWoodlandKeepsItsExistingDensityWhenVisualPalettesChange()
        {
            var world = Patch(); ForestPopulation.Generate(world); world.forestVersion = 1;
            int count = world.trees.Count;
            Assert.IsFalse(ForestPopulation.Upgrade(world));
            Assert.AreEqual(count, world.trees.Count); Assert.AreEqual(ForestPopulation.Version, world.forestVersion);
        }

        [Test]
        public void TropicalReefWoodlandCanGrowAboveTheWashZoneOnLowIslands()
        {
            var world = Patch(); world.preset = RegionPreset.Tropical; world.hasOcean = true; world.seaLevel = 40f;
            for (int i = 0; i < world.heights.Length; i++) world.heights[i] = 41.5f;
            ForestPopulation.Generate(world);
            Assert.Greater(world.trees.Count, 20000, "Mapped low tropical islands must not lose all their trees to a mainland elevation buffer.");
            foreach (var tree in world.trees) Assert.IsFalse(world.IsWater(tree.position));
        }

        [Test]
        public void FullyForestedTropicalDownloadSpreadsItsBoundedTreeBudgetAcrossTheWholeMap()
        {
            var world = Patch(); world.preset = RegionPreset.Tropical;
            for (int i = 0; i < world.landCover.Length; i++) world.landCover[i] = 10;
            ForestPopulation.Generate(world);
            Assert.AreEqual(ValleyWorld.MaxTrees, world.trees.Count);
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                bool east = quadrant % 2 == 0, north = quadrant / 2 == 0;
                int count = world.trees.Count(t => (t.position.x >= 0f) == east && (t.position.y >= 0f) == north);
                Assert.That(count, Is.InRange(ValleyWorld.MaxTrees / 5, ValleyWorld.MaxTrees / 3), "A cap must not leave the last rows of the map empty.");
            }
        }

        static ValleyWorld Patch()
        {
            var world = new ValleyWorld { seed = 45172, preset = RegionPreset.Woodland,
                heights = Enumerable.Repeat(80f, ValleyWorld.Resolution * ValleyWorld.Resolution).ToArray(),
                landCover = Enumerable.Repeat((byte)40, ValleyWorld.Resolution * ValleyWorld.Resolution).ToArray() };
            for (int z = 216; z <= 295; z++)
            for (int x = 216; x <= 295; x++) world.landCover[z * ValleyWorld.Resolution + x] = 10;
            return world;
        }
    }
}
