using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirCity.EditorTools.World;
using AirCity.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class TerrainBrushViewTests
    {
        [Test]
        public void LocalGradingMatchesAFullRebuildAndPreservesRoadMeshesAndForestBatches()
        {
            var world = ValleyWorld.Generate(42);
            var root = new GameObject("Local terrain regression");
            try
            {
                var view = root.AddComponent<ValleyView>();
                view.trees = AssetDatabase.LoadAssetAtPath<TreeLibrary>(WorldAssetSetup.TreesPath);
                Assert.IsNotNull(view.trees);
                view.Build(world, refreshGroundCover: false);
                Physics.SyncTransforms();
                var road = root.transform.Find("Public road network/Asphalt and junctions").GetComponent<MeshFilter>().sharedMesh;
                var forest = root.GetComponentInChildren<ForestRenderer>();
                var batches = Field<IList>(forest, "batches").Cast<object>().ToArray();
                var transforms = batches.Select(b => Field<Matrix4x4[]>(b, "matrices")).ToArray();
                var treePositions = ForestMatrices(forest).Select(m => new Vector2(m.m03, m.m23)).ToArray();
                int count = forest.TreeCount;
                // Include roads and all heightmap boundaries, not just the starter parcel.
                foreach (var p in new[] { world.sites[0].centre, new Vector2(ValleyWorld.RoadX, 0f),
                    new Vector2(-3200f, -3200f), new Vector2(3200f, 3200f) })
                {
                    const float radius = 90f;
                    var area = new Rect(p.x - radius, p.y - radius, radius * 2f, radius * 2f);
                    world.LevelBrush(p, radius, world.Height(p) + 3f, new Rect(-3201f, -3201f, 6402f, 6402f));
                    view.RefreshLand(area);
                }
                Assert.AreSame(road, root.transform.Find("Public road network/Asphalt and junctions").GetComponent<MeshFilter>().sharedMesh);
                Assert.AreEqual(count, forest.TreeCount);
                CollectionAssert.AreEqual(treePositions, ForestMatrices(forest).Select(m => new Vector2(m.m03, m.m23)).ToArray());
                for (int i = 0; i < batches.Length; i++) Assert.AreSame(transforms[i], Field<Matrix4x4[]>(batches[i], "matrices"));
                foreach (var m in ForestMatrices(forest))
                    Assert.AreEqual(view.DisplayHeight(new Vector2(m.m03, m.m23)), m.m13, .0001f);
                var geometry = new PublicRoadGeometry(view.RoadNetwork, null, null, world.Height);
                try
                {
                    var expected = geometry.GradedHeights(world);
                    var actual = view.Terrain.terrainData.GetHeights(0, 0, ValleyWorld.Resolution, ValleyWorld.Resolution);
                    for (int z = 0; z < ValleyWorld.Resolution; z++)
                    for (int x = 0; x < ValleyWorld.Resolution; x++)
                        Assert.AreEqual(expected[z * ValleyWorld.Resolution + x] / ValleyWorld.HeightRange, actual[z, x], .00002f);
                    // Picking must see the edited terrain immediately, before any surface queue is drained.
                    var p = world.sites[0].centre;
                    p = new Vector2(Mathf.Round(p.x / world.Spacing) * world.Spacing, Mathf.Round(p.y / world.Spacing) * world.Spacing);
                    var ray = new Ray(new Vector3(p.x, 1000f, p.y), Vector3.down);
                    Assert.IsTrue(view.Terrain.GetComponent<TerrainCollider>().Raycast(ray, out var hit, 2000f));
                    Assert.AreEqual(view.DisplayHeight(p), hit.point.y, .02f);
                }
                finally { DestroyMeshes(geometry); }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void LevelReleaseCoalescesBoundedUpdatesAndMatchesFullSurfaceAndGroundCover()
        {
            var world = ValleyWorld.Generate(42);
            var root = new GameObject("Local groundcover regression");
            try
            {
                var view = root.AddComponent<ValleyView>();
                view.groundCover = AssetDatabase.LoadAssetAtPath<GroundCoverLibrary>(GroundCoverSetup.LibraryPath);
                Assert.IsNotNull(view.groundCover);
                view.Build(world, refreshGroundCover: false);
                view.Terrain.terrainData.alphamapResolution = 128; view.RefreshSurface();
                var details = root.GetComponentInChildren<GroundCoverRenderer>();
                var oldTiles = Field<System.Array>(details, "tiles").Cast<object>().ToArray();
                var oldCount = details.InstanceCount;
                // Cross a tile boundary; overlapping stamps must replace each affected tile only once.
                var p = world.sites[0].centre;
                p.x = Mathf.Round((p.x + 3200f) / GroundCoverRenderer.TileSize) * GroundCoverRenderer.TileSize - 3200f;
                const float radius = 90f;
                var area = new Rect(p.x - radius, p.y - radius, radius * 2f, radius * 2f);
                world.LevelBrush(p, radius, world.Height(p) + 2f, world.sites[0].Parcel);
                view.RefreshLand(area); view.QueueLevelSurface(area);
                int surfaces = view.PendingSurfaceTiles, covers = view.PendingGroundCoverTiles;
                view.QueueLevelSurface(area);
                Assert.AreEqual(surfaces, view.PendingSurfaceTiles); Assert.AreEqual(covers, view.PendingGroundCoverTiles);
                Assert.That(surfaces, Is.InRange(1, 4)); Assert.That(covers, Is.InRange(1, 4));
                Assert.AreEqual(oldCount, details.InstanceCount, "Mouse release must only queue prop work.");
                while (view.PendingSurfaceTiles > 0) Assert.That(view.ProcessSurfaceUpdates(), Is.InRange(1, 1024));
                while (view.PendingGroundCoverTiles > 0) Assert.AreEqual(1, view.ProcessGroundCoverUpdates());
                var newTiles = Field<System.Array>(details, "tiles").Cast<object>().ToArray();
                Assert.AreEqual(covers, oldTiles.Where((tile, i) => !ReferenceEquals(tile, newTiles[i])).Count());
                Assert.Less(details.InstanceCount, oldCount);
                var localInstances = details.Instances.ToDictionary(i => i.position);
                var localMatrices = CoverMatrices(details).ToDictionary(m => new Vector2(m.m03, m.m23));
                var geometry = new PublicRoadGeometry(view.RoadNetwork, null, null, world.Height);
                try
                {
                    var expected = ValleySurface.Paint(world, geometry, 128);
                    var actual = view.Terrain.terrainData.GetAlphamaps(0, 0, 128, 128);
                    for (int z = 0; z < 128; z++)
                    for (int x = 0; x < 128; x++)
                    for (int layer = 0; layer < 6; layer++) Assert.AreEqual(expected[z, x, layer], actual[z, x, layer], .012f);
                    details.Rebuild(world, view.DisplayHeight, geometry);
                    Assert.AreEqual(localInstances.Count, details.Instances.Count);
                    foreach (var item in details.Instances) Assert.AreEqual(item, localInstances[item.position]);
                    var fullMatrices = CoverMatrices(details).ToDictionary(m => new Vector2(m.m03, m.m23));
                    Assert.AreEqual(fullMatrices.Count, localMatrices.Count);
                    foreach (var pair in fullMatrices) Assert.AreEqual(pair.Value, localMatrices[pair.Key]);
                }
                finally { DestroyMeshes(geometry); }
            }
            finally { Object.DestroyImmediate(root); }
        }

        static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(obj);
        static IEnumerable<Matrix4x4> ForestMatrices(ForestRenderer renderer)
        {
            foreach (object batch in Field<IList>(renderer, "batches"))
            foreach (var matrix in Field<Matrix4x4[]>(batch, "matrices")) yield return matrix;
        }
        static IEnumerable<Matrix4x4> CoverMatrices(GroundCoverRenderer renderer)
        {
            foreach (object tile in Field<System.Array>(renderer, "tiles"))
            {
                if (tile == null) continue;
                foreach (object batch in Field<IList>(tile, "batches"))
                foreach (var matrix in Field<Matrix4x4[]>(batch, "matrices")) yield return matrix;
            }
        }
        static void DestroyMeshes(PublicRoadGeometry geometry)
        { foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures }) Object.DestroyImmediate(mesh); }
    }
}
