using AirCity.World;
using NUnit.Framework;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Tests.World
{
    public class RegionWaterViewTests
    {
        static ValleyWorld Inland(float level, bool everywhere)
        {
            int count = ValleyWorld.Resolution * ValleyWorld.Resolution;
            var world = new ValleyWorld { generatorVersion = 4, preset = RegionPreset.Mountains,
                heights = new float[count], waterHeights = new float[count] };
            for (int i = 0; i < count; i++)
            { world.heights[i] = level - 1.5f; if (everywhere) world.waterHeights[i] = level; }
            if (!everywhere)
            {
                int centre = ValleyWorld.Resolution / 2;
                world.waterHeights[centre * ValleyWorld.Resolution + centre] = level;
                world.waterHeights[centre * ValleyWorld.Resolution + centre + 1] = level;
            }
            return world;
        }

        [Test]
        public void MappedRiverHasItsSavedElevationAndDoesNotFloodTheRestOfTheValley()
        {
            var world = Inland(173f, false);
            var go = new GameObject("Inland water test");
            try
            {
                var view = go.AddComponent<RegionWaterView>(); view.Build(world, world.Height);
                var filters = go.GetComponentsInChildren<MeshFilter>();
                Assert.AreEqual(1, filters.Length);
                var mesh = filters[0].sharedMesh;
                Assert.Greater(mesh.vertexCount, 0);
                Assert.Less(mesh.bounds.size.x, 150f, "The bank support halo must remain local to the mapped river.");
                Assert.Less(mesh.bounds.size.z, 125f);
                foreach (var vertex in mesh.vertices) Assert.AreEqual(173.025f, vertex.y, .001f);
                Assert.IsTrue(mesh.vertices.Any(v => Mathf.Abs((v.x + ValleyWorld.Size * .5f) / world.Spacing -
                    Mathf.Round((v.x + ValleyWorld.Size * .5f) / world.Spacing)) > .01f), "River banks need sub-grid surface triangles.");
                view.SetClock(12.5f);
                var material = filters[0].GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreEqual(1f, material.GetFloat("_Inland"));
                Assert.AreEqual(12.5f, material.GetFloat("_RegionTime"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NarrowRiverKeepsItsDownhillProfileAndExtendsUnderDryBanks()
        {
            var world = Inland(173f, false);
            System.Array.Clear(world.waterHeights,0,world.waterHeights.Length);
            for (int i=0;i<world.heights.Length;i++)world.heights[i]=190f;
            int centre=ValleyWorld.Resolution/2;
            for (int z=centre-4;z<=centre+4;z++)
            {
                int i=z*ValleyWorld.Resolution+centre;
                world.waterHeights[i]=173f-(z-centre)*.25f;
                world.heights[i]=world.waterHeights[i]-1.5f;
            }
            var saved=(float[])world.waterHeights.Clone();
            var go=new GameObject("Downhill river test");
            try
            {
                go.AddComponent<RegionWaterView>().Build(world,world.Height);
                var vertices=go.GetComponentInChildren<MeshFilter>().sharedMesh.vertices;
                for (int z=centre-4;z<=centre+4;z++)
                {
                    float pz=z*world.Spacing-ValleyWorld.Size*.5f;
                    var vertex=vertices.First(v=>Mathf.Abs(v.x)<.001f&&Mathf.Abs(v.z-pz)<.001f);
                    Assert.AreEqual(world.waterHeights[z*ValleyWorld.Resolution+centre]+.025f,vertex.y,.001f);
                }
                Assert.IsTrue(vertices.Any(v=>Mathf.Abs(v.x)>world.Spacing), "The sheet must continue beneath dry bank terrain instead of ending at a mask square.");
                Assert.IsFalse(world.IsWater(new Vector2(world.Spacing,0f)), "A supported sheet must not turn raised dry land into water.");
                CollectionAssert.AreEqual(saved,world.waterHeights);
            }
            finally{Object.DestroyImmediate(go);}
        }

        [Test]
        public void CoastalWorldCanRenderOceanAndElevatedFreshwaterSeparately()
        {
            var world=Inland(173f,false);world.hasOcean=true;
            var ocean=new GameObject("Ocean test");var lake=new GameObject("Elevated lake test");
            try
            {
                ocean.AddComponent<RegionWaterView>().Build(world,world.Height);
                lake.AddComponent<RegionWaterView>().Build(world,world.Height,inlandSurface:true);
                var seaMesh=ocean.GetComponentInChildren<MeshFilter>();var lakeMesh=lake.GetComponentInChildren<MeshFilter>();
                Assert.IsNotNull(seaMesh);Assert.IsNotNull(lakeMesh);
                Assert.AreEqual(0f,seaMesh.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Inland"));
                Assert.AreEqual(1f,lakeMesh.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Inland"));
                Assert.Greater(lakeMesh.sharedMesh.bounds.center.y,seaMesh.sharedMesh.bounds.center.y+100f);
            }
            finally{Object.DestroyImmediate(ocean);Object.DestroyImmediate(lake);}
        }

        [Test]
        public void LargeMappedWaterBodyUsesIndicesThatCanAddressAllVertices()
        {
            var world = Inland(90f, true);
            var go = new GameObject("Large inland water test");
            try
            {
                go.AddComponent<RegionWaterView>().Build(world, world.Height);
                var mesh = go.GetComponentInChildren<MeshFilter>().sharedMesh;
                Assert.AreEqual(ValleyWorld.Resolution * ValleyWorld.Resolution, mesh.vertexCount);
                Assert.AreEqual(IndexFormat.UInt32, mesh.indexFormat);
                Assert.AreEqual((ValleyWorld.Resolution - 1) * (ValleyWorld.Resolution - 1) * 6, mesh.GetIndexCount(0));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
