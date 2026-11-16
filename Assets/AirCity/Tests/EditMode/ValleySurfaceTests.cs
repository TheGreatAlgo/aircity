using System.Linq;
using AirCity.World;
using AirCity.Game;
using AirCity.EditorTools.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class ValleySurfaceTests
    {
        [Test]
        public void DirtyRectanglesMatchTheFullSurfaceIncludingCanopyAndWorldEdges()
        {
            var world = ValleyWorld.Generate(42);
            var centre = world.sites[0].centre;
            world.Clear(new Rect(centre.x - 90f, centre.y - 90f, 180f, 180f));
            world.MarkGround(centre, 90f, .3f);
            var geometry = new PublicRoadGeometry(PublicRoadNetwork.FromWorld(world), null, null, world.Height);
            try
            {
                const int resolution = 128;
                var expected = ValleySurface.Paint(world, geometry, resolution);
                foreach (var region in new[] { new RectInt(58, 40, 12, 17), new RectInt(0, 0, 9, 11), new RectInt(119, 117, 9, 11) })
                {
                    var patch = ValleySurface.PaintRegion(world, geometry, resolution, region);
                    for (int z = 0; z < region.height; z++)
                    for (int x = 0; x < region.width; x++)
                    for (int layer = 0; layer < 6; layer++)
                        Assert.AreEqual(expected[region.y + z, region.x + x, layer], patch[z, x, layer], .000001f);
                }
            }
            finally
            { foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures }) Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void ClearingRepaintsBoundedTilesAndKeepsGroundCoverRoadsAndHeights()
        {
            var root = new GameObject("Clearing surface regression");
            try
            {
                var state = CareerState.New(42); Assert.IsTrue(state.Build(out _, 0));
                var view = root.AddComponent<ValleyView>();
                view.groundCover = AssetDatabase.LoadAssetAtPath<GroundCoverLibrary>(GroundCoverSetup.LibraryPath);
                Assert.IsNotNull(view.groundCover);
                view.Build(state.world, state.RoadNetwork(), false);
                view.Terrain.terrainData.alphamapResolution = 128; view.RefreshSurface();
                var details = root.GetComponentInChildren<GroundCoverRenderer>();
                var instances = details.Instances;
                var roadMesh = root.transform.Find("Public road network").GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Asphalt and junctions").sharedMesh;
                var heights = (float[])state.world.heights.Clone();
                var c = state.Site.centre;
                Assert.IsTrue(CareerConstruction.Clear(state, c, 90f, out string why), why);
                var area = new Rect(c.x - 90f, c.y - 90f, 180f, 180f);
                view.QueueClearingSurface(area);
                int queued = view.PendingSurfaceTiles;
                view.QueueClearingSurface(area);
                Assert.AreEqual(queued, view.PendingSurfaceTiles, "Repeated strokes must coalesce dirty tiles.");
                Assert.That(queued, Is.InRange(1, 4));
                int tiles = 0;
                while (view.PendingSurfaceTiles > 0)
                {
                    Assert.That(view.ProcessSurfaceUpdates(), Is.InRange(1, 32 * 32)); tiles++;
                    Assert.AreSame(instances, details.Instances, "Clearing must not reroll the world's groundcover batches.");
                }
                Assert.AreEqual(queued, tiles);
                Assert.AreSame(roadMesh, root.transform.Find("Public road network").GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Asphalt and junctions").sharedMesh);
                CollectionAssert.AreEqual(heights, state.world.heights);
                var geometry = new PublicRoadGeometry(state.RoadNetwork(), null, null, state.world.Height);
                try
                {
                    var expected = ValleySurface.Paint(state.world, geometry, 128);
                    var actual = view.Terrain.terrainData.GetAlphamaps(0, 0, 128, 128);
                    for (int z = 0; z < 128; z++)
                    for (int x = 0; x < 128; x++)
                    for (int layer = 0; layer < 6; layer++) Assert.AreEqual(expected[z, x, layer], actual[z, x, layer], .012f);
                }
                finally
                { foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures }) Object.DestroyImmediate(mesh); }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RoadMeshesAreTheOnlyAsphaltAndTheirTerrainPaletteMatchesUnbuiltLand()
        {
            var world=ValleyWorld.Generate(42);var graph=new PublicRoadNetwork();
            graph.AddSegment(graph.AddNode(new Vector3(-900,100,0)),graph.AddNode(new Vector3(900,100,0)),PublicRoadStyle.Avenue,RoadStructure.Ground);
            var geometry=new PublicRoadGeometry(graph,null,null,world.Height);
            try
            {
                var unbuilt=ValleySurface.Paint(world,null,64);var built=ValleySurface.Paint(world,geometry,64);
                CollectionAssert.AreEqual(unbuilt,built,"A road must not stain the land around its exact mesh.");
                for(int z=0;z<64;z++)for(int x=0;x<64;x++)Assert.AreEqual(0f,built[z,x,2]);
            }
            finally{foreach(var mesh in new[]{geometry.pavement,geometry.shoulders,geometry.markings,geometry.structures,geometry.kerbMesh,geometry.medianGrass})Object.DestroyImmediate(mesh);}
        }

        [Test]
        public void LandscapeLayersUseNormalMapsAndMatteConstantSmoothness()
        {
            var root = new GameObject("Landscape material review");
            try
            {
                var view = root.AddComponent<ValleyView>(); ValleySurfaceSetup.Assign(view);
                foreach (var layer in new[] { view.grassLayer, view.dirtLayer, view.roadLayer, view.dryLayer, view.rockLayer, view.forestLayer })
                {
                    Assert.IsNotNull(layer.normalMapTexture);
                    Assert.Less(layer.smoothness, .15f);
                    Assert.AreEqual(2, new SerializedObject(layer).FindProperty("m_SmoothnessSource").intValue,
                        "Opaque albedo alpha must not turn the landscape into a mirror.");
                }
                Assert.AreNotSame(view.dirtLayer.diffuseTexture, view.rockLayer.diffuseTexture);
                StringAssert.Contains("RockSurface", view.rockLayer.diffuseTexture.name);
                StringAssert.Contains("ForestFloor", view.forestLayer.diffuseTexture.name);
                Assert.IsNotNull(view.forestLayer.maskMapTexture);
                Assert.IsNotNull(view.rockLayer.maskMapTexture);
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void NaturalLandscapeMixIgnoresLegacyConstructionStainsWithNormalizedWeights()
        {
            var world = ValleyWorld.Generate(42);
            var graph = PublicRoadNetwork.FromWorld(world);
            var geometry = new PublicRoadGeometry(graph, null, null, world.Height);
            try
            {
                var first = ValleySurface.Paint(world, geometry, 128);
                var centre = world.sites[0].centre;
                world.MarkGround(centre, 250f, 1f);
                var edited = ValleySurface.Paint(world, geometry, 128);
                float minDry = 1f, maxDry = 0f, maxLitter = 0f, earthChange = 0f;
                for (int z = 0; z < 128; z++)
                for (int x = 0; x < 128; x++)
                {
                    float sum = 0f;
                    for (int layer = 0; layer < 6; layer++) { Assert.That(edited[z, x, layer], Is.InRange(0f, 1.0001f)); sum += edited[z, x, layer]; }
                    Assert.AreEqual(1f, sum, .0001f);
                    minDry = Mathf.Min(minDry, edited[z, x, 3]); maxDry = Mathf.Max(maxDry, edited[z, x, 3]);
                    maxLitter = Mathf.Max(maxLitter, edited[z, x, 5]);
                    earthChange = Mathf.Max(earthChange, edited[z, x, 1] - first[z, x, 1]);
                }
                Assert.Greater(maxDry - minDry, .3f); Assert.Greater(maxLitter, .3f); Assert.AreEqual(0f,earthChange,"Saved construction disturbance must not paint a dirt halo.");
                CollectionAssert.AreEqual(first,edited);
            }
            finally
            { foreach (var mesh in new[] { geometry.pavement, geometry.shoulders, geometry.markings, geometry.structures }) Object.DestroyImmediate(mesh); }
        }
    }
}
