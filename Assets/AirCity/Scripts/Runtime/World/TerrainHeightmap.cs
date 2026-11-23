using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Refines water-bearing terrain/colliders from the saved bilinear field without changing saved samples.</summary>
    public static class TerrainHeightmap
    {
        public static int RenderResolution(ValleyWorld world) => world != null && (world.HasOcean || world.HasInlandWater) ?
            (ValleyWorld.Resolution - 1) * 4 + 1 : ValleyWorld.Resolution;

        /// <summary>Coarse samples and interpolation halo needed by an arbitrary native heightmap rectangle.</summary>
        public static RectInt SourceBounds(RectInt target, int renderResolution)
        {
            if (target.width == 0 || target.height == 0) return new RectInt();
            float scale = (ValleyWorld.Resolution - 1f) / (renderResolution - 1f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(target.xMin * scale) - 1);
            int z0 = Mathf.Max(0, Mathf.FloorToInt(target.yMin * scale) - 1);
            int x1 = Mathf.Min(ValleyWorld.Resolution - 1, Mathf.FloorToInt((target.xMax - 1) * scale) + 2);
            int z1 = Mathf.Min(ValleyWorld.Resolution - 1, Mathf.FloorToInt((target.yMax - 1) * scale) + 2);
            return new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        }

        /// <summary>Every result is a convex combination of the same four saved/road-graded source samples.</summary>
        public static float[,] Resample(float[,] source, RectInt sourceBounds, RectInt target, int renderResolution,
            float divisor = 1f, BuiltSurfaceMap surfaces = null, ValleyWorld world = null)
        {
            if (source == null || source.GetLength(0) != sourceBounds.height || source.GetLength(1) != sourceBounds.width)
                throw new ArgumentException("The source heights must match their source-grid rectangle.");
            if (renderResolution < ValleyWorld.Resolution || (renderResolution - 1) % (ValleyWorld.Resolution - 1) != 0 || divisor <= 0f)
                throw new ArgumentException("Terrain refinement requires a whole source-grid subdivision and a positive height range.");
            surfaces?.Build();
            var flat=new float[source.Length];Buffer.BlockCopy(source,0,flat,0,flat.Length*sizeof(float));
            if (surfaces != null && surfaces.Formation.Length > 0)
            {
                var limits = surfaces.Formation;
                for (int z = 0; z < sourceBounds.height; z++)
                for (int x = 0; x < sourceBounds.width; x++)
                {
                    int gx = x + sourceBounds.x, gz = z + sourceBounds.y;
                    float ceiling = limits[gz * ValleyWorld.Resolution + gx];
                    if (float.IsPositiveInfinity(ceiling)) continue;
                    int index = z * sourceBounds.width + x;
                    float original = flat[index];
                    // Keep the soil supporting the pavement after edits, while preserving the wet bed and dry bank.
                    float formed = Mathf.Clamp(original, ceiling - .035f, ceiling);
                    var point = new Vector2(gx, gz) * (ValleyWorld.Size / (ValleyWorld.Resolution-1)) - Vector2.one * (ValleyWorld.Size*.5f);
                    flat[index] = world != null ? world.RetainShore(point, original, formed) : formed;
                }
            }
            using var input=new NativeArray<float>(flat,Allocator.TempJob);
            using var output=new NativeArray<float>(target.width*target.height,Allocator.TempJob);
            new ResampleJob {source=input,result=output,
                sourceBounds=sourceBounds,target=target,scale=(ValleyWorld.Resolution-1f)/(renderResolution-1f),divisor=divisor}
                .Schedule(output.Length,256).Complete();
            var result=new float[target.height,target.width];var heights=output.ToArray();
            Buffer.BlockCopy(heights,0,result,0,heights.Length*sizeof(float));return result;
        }
        [BurstCompile]struct ResampleJob:IJobParallelFor
        {
            [ReadOnly]public NativeArray<float> source;
            [WriteOnly]public NativeArray<float> result;
            public RectInt sourceBounds,target;
            public float scale,divisor;
            float Sample(int x,int z)=>source[z*sourceBounds.width+x];
            public void Execute(int i)
            {
                float px=(i%target.width+target.x)*scale,pz=(i/target.width+target.y)*scale;
                int ix=Mathf.FloorToInt(px),iz=Mathf.FloorToInt(pz);float tx=px-ix,tz=pz-iz;
                int x0=Mathf.Clamp(ix-sourceBounds.x,0,sourceBounds.width-1),x1=Mathf.Min(x0+1,sourceBounds.width-1);
                int z0=Mathf.Clamp(iz-sourceBounds.y,0,sourceBounds.height-1),z1=Mathf.Min(z0+1,sourceBounds.height-1);
                result[i]=Mathf.Lerp(Mathf.Lerp(Sample(x0,z0),Sample(x1,z0),tx),Mathf.Lerp(Sample(x0,z1),Sample(x1,z1),tx),tz)/divisor;
            }
        }
    }
}
