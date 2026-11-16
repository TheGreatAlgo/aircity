using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class TerrainHeightmapTests
    {
        const int Native = (ValleyWorld.Resolution - 1) * 4 + 1;

        [Test]
        public void WaterTerrainRefinesWithoutChangingLegacyOrDryWorldResolution()
        {
            Assert.AreEqual(Native, TerrainHeightmap.RenderResolution(new ValleyWorld { generatorVersion = 5, hasOcean = true }));
            Assert.AreEqual(ValleyWorld.Resolution, TerrainHeightmap.RenderResolution(new ValleyWorld { generatorVersion = 3, hasOcean = true }));
            Assert.AreEqual(ValleyWorld.Resolution, TerrainHeightmap.RenderResolution(new ValleyWorld { generatorVersion = 5, hasOcean = false }));
            Assert.AreEqual(Native, TerrainHeightmap.RenderResolution(new ValleyWorld { generatorVersion = 5,
                waterHeights = new float[ValleyWorld.Resolution * ValleyWorld.Resolution] }));
        }

        [Test]
        public void RefinedHeightsKeepEverySourceCornerAndCannotOvershootRoadGrading()
        {
            var source = new float[2,2] { { 10f,90f }, { 70f,40f } };
            var result = TerrainHeightmap.Resample(source,new RectInt(256,256,2,2),new RectInt(1024,1024,5,5),Native);
            Assert.AreEqual(10f,result[0,0]); Assert.AreEqual(90f,result[0,4]);
            Assert.AreEqual(70f,result[4,0]); Assert.AreEqual(40f,result[4,4]);
            for (int z=0;z<5;z++)
            for (int x=0;x<5;x++)
            {
                float tx=x/4f,tz=z/4f;
                float expected=10f*(1-tx)*(1-tz)+90f*tx*(1-tz)+70f*(1-tx)*tz+40f*tx*tz;
                Assert.AreEqual(expected,result[z,x],.00001f);
                Assert.That(result[z,x],Is.InRange(10f,90f));
            }
        }

        [TestCase(1009,1011)]
        [TestCase(0,0)]
        [TestCase(Native-21,Native-21)]
        public void LocalRegionsAndTheirSeamMatchTheFullSavedField(int x,int z)
        {
            var source=new float[ValleyWorld.Resolution,ValleyWorld.Resolution];
            for (int sz=0;sz<ValleyWorld.Resolution;sz++)
            for (int sx=0;sx<ValleyWorld.Resolution;sx++) source[sz,sx]=20f+(sx*17+sz*31)%70;
            var target=new RectInt(x,z,21,21);
            var expected=TerrainHeightmap.Resample(source,new RectInt(0,0,ValleyWorld.Resolution,ValleyWorld.Resolution),target,Native);
            foreach(var part in new[]{new RectInt(x,z,11,21),new RectInt(x+11,z,10,21)})
            {
                var bounds=TerrainHeightmap.SourceBounds(part,Native);
                Assert.GreaterOrEqual(bounds.xMin,0); Assert.LessOrEqual(bounds.xMax,ValleyWorld.Resolution);
                Assert.GreaterOrEqual(bounds.yMin,0); Assert.LessOrEqual(bounds.yMax,ValleyWorld.Resolution);
                var local=new float[bounds.height,bounds.width];
                for(int pz=0;pz<bounds.height;pz++)
                for(int px=0;px<bounds.width;px++)local[pz,px]=source[bounds.y+pz,bounds.x+px];
                var result=TerrainHeightmap.Resample(local,bounds,part,Native);
                for(int pz=0;pz<part.height;pz++)
                for(int px=0;px<part.width;px++)Assert.AreEqual(expected[pz,px+part.x-x],result[pz,px],.00001f);
            }
        }
    }
}
