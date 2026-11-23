using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Actual opaque ground pavement owns its footprint, foundation and vegetation clearance.
    /// The same immutable spatial index serves CPU/Burst queries and every terrain render pass.</summary>
    public sealed class BuiltSurfaceMap : IDisposable
    {
        public const float CellSize=32f;
        public const float SurfaceTolerance = .025f;
        // Shared edges can round to different world-space floats after terrain rasterization.
        // Keep the GPU overlap and its cell candidates in the same world-space units.
        const float RenderEdgeOverlap = .001f;
        public const int Columns=200;
        const float Half=ValleyWorld.Size*.5f;
        public struct Triangle
        {
            public Vector3 a,b,c;
            public int foundation;
            public float excavationHeight, excavationDepth;
            readonly int buried;
            public bool OwnsGround => buried == 0;

            public Triangle(Vector3 a, Vector3 b, Vector3 c, bool foundation = false, float excavationHeight = 0f, bool buried = false,
                float excavationDepth = SurfaceTolerance)
            {
                this.a = a;
                this.b = b;
                this.c = c;
                this.foundation = foundation ? 1 : 0;
                this.excavationHeight = excavationHeight;
                this.excavationDepth = excavationDepth;
                this.buried = buried ? 1 : 0;
            }

            public Triangle Transform(Matrix4x4 matrix, bool includeFoundation)
            {
                float up = matrix.MultiplyVector(Vector3.up).y;
                return new Triangle(matrix.MultiplyPoint3x4(a), matrix.MultiplyPoint3x4(b), matrix.MultiplyPoint3x4(c),
                    includeFoundation && foundation != 0, up * excavationHeight, !OwnsGround, up * excavationDepth);
            }
            static Vector2 Flat(Vector3 p)=>new Vector2(p.x,p.z);
            static float Cross(Vector2 p,Vector2 q)=>p.x*q.y-p.y*q.x;
            public float Height(Vector2 p)
            {
                var n=Vector3.Cross(b-a,c-a);
                return a.y-(n.x*(p.x-a.x)+n.z*(p.y-a.z))/n.y;
            }
            public float DistanceSquared(Vector2 p)
            {
                var aa=Flat(a);var bb=Flat(b);var cc=Flat(c);
                float d0=Cross(bb-aa,p-aa),d1=Cross(cc-bb,p-bb),d2=Cross(aa-cc,p-cc);
                if(d0>=-.00001f && d1>=-.00001f && d2>=-.00001f || d0<=.00001f && d1<=.00001f && d2<=.00001f)return 0f;
                return Mathf.Min(SegmentDistance(p,aa,bb),Mathf.Min(SegmentDistance(p,bb,cc),SegmentDistance(p,cc,aa)));
            }
            static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
            {
                var d=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.000001f,d.sqrMagnitude));
                return (p-a-d*t).sqrMagnitude;
            }
            static bool Segments(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
            {
                var ab=b-a;var cd=d-c;float denominator=Cross(ab,cd);
                if(Mathf.Abs(denominator)<.000001f)return false;
                float t=Cross(c-a,cd)/denominator,u=Cross(c-a,ab)/denominator;
                return t>=0f && t<=1f && u>=0f && u<=1f;
            }
            public bool Intersects(Rect r)
            {
                var aa=Flat(a);var bb=Flat(b);var cc=Flat(c);
                if(Mathf.Max(aa.x,Mathf.Max(bb.x,cc.x))<r.xMin || Mathf.Min(aa.x,Mathf.Min(bb.x,cc.x))>r.xMax ||
                    Mathf.Max(aa.y,Mathf.Max(bb.y,cc.y))<r.yMin || Mathf.Min(aa.y,Mathf.Min(bb.y,cc.y))>r.yMax)return false;
                if(r.Contains(aa)||r.Contains(bb)||r.Contains(cc))return true;
                for(int i=0;i<4;i++)
                {
                    var p=new Vector2(i<2?r.xMin:r.xMax,i==0||i==3?r.yMin:r.yMax);
                    int k=(i+1)%4;var q=new Vector2(k<2?r.xMin:r.xMax,k==0||k==3?r.yMin:r.yMax);
                    if(DistanceSquared(p)<.000001f || Segments(p,q,aa,bb) || Segments(p,q,bb,cc) || Segments(p,q,cc,aa))return true;
                }
                return false;
            }
        }
        public struct Cell {public int start,count;}
        readonly List<Triangle> source=new List<Triangle>();
        NativeArray<Triangle> triangles;
        NativeArray<Cell> cells;
        NativeArray<int> indices;
        NativeArray<float> formation;
        GraphicsBuffer triangleBuffer,cellBuffer,indexBuffer;
        public int TriangleCount=>source.Count;
        public Rect Bounds {get;private set;}
        public NativeArray<float> Formation=>formation;
        bool ready,gpuDirty=true;
        readonly List<int>[] buckets=new List<int>[Columns*Columns];
        readonly List<int> occupied=new List<int>(),flatIndices=new List<int>();
        Vector4[] gpuTriangles;
        internal void ResetPreview()
        {ready=false;gpuDirty=true;source.Clear();Bounds=default;}
        static void Capacity<T>(ref NativeArray<T> array,int needed) where T:struct
        {
            if(array.IsCreated && array.Length>=needed)return;
            if(array.IsCreated)array.Dispose();
            array=new NativeArray<T>(Mathf.NextPowerOfTwo(Mathf.Max(1,needed)),Allocator.Persistent);
        }

        public void AddTriangle(Triangle t)
        {
            if(ready)throw new InvalidOperationException("A built surface map is immutable after Build.");
            if(Mathf.Abs(Vector3.Cross(t.b-t.a,t.c-t.a).y)<.0001f)return;
            float x0=Mathf.Min(t.a.x,Mathf.Min(t.b.x,t.c.x)),x1=Mathf.Max(t.a.x,Mathf.Max(t.b.x,t.c.x));
            float z0=Mathf.Min(t.a.z,Mathf.Min(t.b.z,t.c.z)),z1=Mathf.Max(t.a.z,Mathf.Max(t.b.z,t.c.z));
            if(source.Count==0)Bounds=Rect.MinMaxRect(x0,z0,x1,z1);
            else Bounds=Rect.MinMaxRect(Mathf.Min(Bounds.xMin,x0),Mathf.Min(Bounds.yMin,z0),Mathf.Max(Bounds.xMax,x1),Mathf.Max(Bounds.yMax,z1));
            source.Add(t);
        }
        /// <summary>Transparent fringes remain over natural ground; only the opaque core owns terrain.</summary>
        public void AddMesh(Mesh mesh,Matrix4x4 toWorld,bool foundation=true)
        {
            if(mesh==null || mesh.vertexCount==0)return;
            var v=mesh.vertices;var colours=mesh.colors;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var ids=mesh.GetTriangles(sub);
                for(int i=0;i<ids.Length;i+=3)
                {
                    int a=ids[i],b=ids[i+1],c=ids[i+2];
                    if(colours.Length==v.Length && Mathf.Min(colours[a].a,colours[b].a,colours[c].a)<.99f)continue;
                    AddTriangle(new Triangle(toWorld.MultiplyPoint3x4(v[a]),toWorld.MultiplyPoint3x4(v[b]),toWorld.MultiplyPoint3x4(v[c]),foundation));
                }
            }
        }
        static int Coordinate(float v)=>Mathf.Clamp(Mathf.FloorToInt((v+Half)/CellSize),0,Columns-1);
        public void Build()
        {
            if(ready)return;ready=true;
            Capacity(ref triangles,source.Count);
            for(int i=0;i<source.Count;i++)triangles[i]=source[i];
            if(!cells.IsCreated)cells=new NativeArray<Cell>(Columns*Columns,Allocator.Persistent);
            foreach(int key in occupied){buckets[key].Clear();cells[key]=default;}
            occupied.Clear();flatIndices.Clear();bool foundations=false;
            for(int i=0;i<source.Count;i++)
            {
                var t=source[i];foundations|=t.foundation!=0;
                int x0=Coordinate(Mathf.Min(t.a.x,Mathf.Min(t.b.x,t.c.x))-RenderEdgeOverlap),x1=Coordinate(Mathf.Max(t.a.x,Mathf.Max(t.b.x,t.c.x))+RenderEdgeOverlap);
                int z0=Coordinate(Mathf.Min(t.a.z,Mathf.Min(t.b.z,t.c.z))-RenderEdgeOverlap),z1=Coordinate(Mathf.Max(t.a.z,Mathf.Max(t.b.z,t.c.z))+RenderEdgeOverlap);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {
                    var cell = new Rect(x*CellSize-Half-RenderEdgeOverlap, z*CellSize-Half-RenderEdgeOverlap,
                        CellSize+2f*RenderEdgeOverlap, CellSize+2f*RenderEdgeOverlap);
                    if(!t.Intersects(cell))continue;
                    int key=z*Columns+x;var bucket=buckets[key]??=new List<int>();
                    if(bucket.Count==0)occupied.Add(key);bucket.Add(i);
                }
            }
            foreach(int key in occupied)
            {var bucket=buckets[key];cells[key]=new Cell {start=flatIndices.Count,count=bucket.Count};flatIndices.AddRange(bucket);}
            Capacity(ref indices,flatIndices.Count);
            for(int i=0;i<flatIndices.Count;i++)indices[i]=flatIndices[i];
            int limits=foundations?ValleyWorld.Resolution*ValleyWorld.Resolution:0;
            if(!formation.IsCreated || formation.Length!=limits)
            {if(formation.IsCreated)formation.Dispose();formation=new NativeArray<float>(limits,Allocator.Persistent);}
            if(foundations)new FormationJob {triangles=triangles,cells=cells,indices=indices,limits=formation}.Schedule(formation.Length,128).Complete();
            gpuDirty=true;
        }

        public bool Contains(Vector2 p,float radius=0f)
        {Build();return Contains(p,radius,triangles,cells,indices);}
        /// <summary>Closest actual triangle plane at a point; the height hint distinguishes stacked road decks.</summary>
        internal bool Project(Vector2 p,float hint,out float height,out Vector2 gradient)
        {
            Build();height=0f;gradient=Vector2.zero;
            if(p.x<-Half || p.x>Half || p.y<-Half || p.y>Half)return false;
            var cell=cells[Coordinate(p.y)*Columns+Coordinate(p.x)];float best=float.MaxValue;
            for(int i=cell.start;i<cell.start+cell.count;i++)
            {
                var triangle=triangles[indices[i]];
                if(!triangle.OwnsGround || triangle.DistanceSquared(p)>.000001f)continue;
                float h=triangle.Height(p),difference=Mathf.Abs(h-hint);if(difference>=best)continue;
                var normal=Vector3.Cross(triangle.b-triangle.a,triangle.c-triangle.a);
                height=h;gradient=new Vector2(-normal.x,-normal.z)/normal.y;best=difference;
            }
            return best<float.MaxValue;
        }
        internal static bool Contains(Vector2 p,float radius,NativeArray<Triangle> triangles,NativeArray<Cell> cells,NativeArray<int> indices)
        {
            if(p.x+radius<-Half || p.x-radius>Half || p.y+radius<-Half || p.y-radius>Half)return false;
            int x0=Coordinate(p.x-radius),x1=Coordinate(p.x+radius),z0=Coordinate(p.y-radius),z1=Coordinate(p.y+radius);
            float limit=radius*radius+.000001f;
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
            {
                var cell=cells[z*Columns+x];
                for (int i = cell.start; i < cell.start + cell.count; i++)
                {
                    var triangle = triangles[indices[i]];
                    if (triangle.OwnsGround && triangle.DistanceSquared(p) <= limit) return true;
                }
            }
            return false;
        }
        public GroundCoverInstance[] Exclude(IReadOnlyList<GroundCoverInstance> items)
        {
            Build();var array=new GroundCoverInstance[items.Count];for(int i=0;i<array.Length;i++)array[i]=items[i];
            if(source.Count==0 || array.Length==0)return array;
            using var input=new NativeArray<GroundCoverInstance>(array,Allocator.TempJob);
            using var blocked=new NativeArray<bool>(array.Length,Allocator.TempJob);
            new ExcludeJob {items=input,blocked=blocked,triangles=triangles,cells=cells,indices=indices}.Schedule(array.Length,128).Complete();
            var kept=new List<GroundCoverInstance>(array.Length);
            for(int i=0;i<array.Length;i++)if(!blocked[i])kept.Add(array[i]);
            return kept.Count==array.Length?array:kept.ToArray();
        }
        /// <summary>Only called by an accepted construction transaction; clearance is included in the road price.</summary>
        public int ClearVegetation(ValleyWorld world,IEnumerable<GroundCoverInstance> props=null,Rect? area=null)
        {
            Build();var bounds=area??Bounds;int removed=0;
            var treeBounds=Rect.MinMaxRect(bounds.xMin-4f,bounds.yMin-4f,bounds.xMax+4f,bounds.yMax+4f);
            foreach(int i in ForestQuery.Candidates(world,treeBounds))
            {var tree=world.trees[i];if(!tree.removed && Contains(tree.position,3f)){tree.removed=true;removed++;}}
            if(props!=null)
            {
                var cleared=new List<int>();
                foreach(var item in props)if(item.id>0 && Contains(item.position,GroundCoverScatter.FootprintRadius(item.kind,item.scale)))cleared.Add(item.id);
                world.RemoveGroundProps(cleared);
            }
            return removed;
        }
        public void Bind(MaterialPropertyBlock properties)
        {
            Build();
            if(gpuDirty)
            {
                int vertices=Mathf.Max(1,source.Count*3);
                if(gpuTriangles==null || gpuTriangles.Length<vertices)gpuTriangles=new Vector4[Mathf.NextPowerOfTwo(vertices)];
                for(int i=0;i<source.Count;i++)
                {
                    var t = source[i];
                    gpuTriangles[i*3] = new Vector4(t.a.x,t.a.z,t.a.y,t.excavationHeight);
                    gpuTriangles[i*3+1] = new Vector4(t.b.x,t.b.z,t.b.y,t.excavationDepth);
                    gpuTriangles[i*3+2] = new Vector4(t.c.x,t.c.z,t.c.y,RenderEdgeOverlap);
                }
                if(triangleBuffer==null || triangleBuffer.count<vertices)
                {triangleBuffer?.Dispose();triangleBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,gpuTriangles.Length,16);}
                triangleBuffer.SetData(gpuTriangles,0,0,vertices);
                if(cellBuffer==null)cellBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,cells.Length,8);
                cellBuffer.SetData(cells);
                if(indexBuffer==null || indexBuffer.count<indices.Length)
                {indexBuffer?.Dispose();indexBuffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Mathf.Max(1,indices.Length),4);}
                indexBuffer.SetData(indices);gpuDirty=false;
            }
            properties.SetFloat("_AirCitySurfaceEnabled",source.Count>0?1f:0f);
            properties.SetVector("_AirCitySurfaceGrid",new Vector4(-Half,-Half,1f/CellSize,Columns));
            properties.SetBuffer("_AirCitySurfaceTriangles",triangleBuffer);properties.SetBuffer("_AirCitySurfaceCells",cellBuffer);properties.SetBuffer("_AirCitySurfaceIndices",indexBuffer);
        }
        [BurstCompile]struct ExcludeJob:IJobParallelFor
        {
            [ReadOnly]public NativeArray<GroundCoverInstance> items;
            [ReadOnly]public NativeArray<Triangle> triangles;
            [ReadOnly]public NativeArray<Cell> cells;
            [ReadOnly]public NativeArray<int> indices;
            [WriteOnly]public NativeArray<bool> blocked;
            public void Execute(int i){var item=items[i];blocked[i]=Contains(item.position,GroundCoverScatter.FootprintRadius(item.kind,item.scale),triangles,cells,indices);}
        }
        [BurstCompile]struct FormationJob:IJobParallelFor
        {
            [ReadOnly]public NativeArray<Triangle> triangles;
            [ReadOnly]public NativeArray<Cell> cells;
            [ReadOnly]public NativeArray<int> indices;
            [WriteOnly]public NativeArray<float> limits;
            public void Execute(int i)
            {
                const float spacing=ValleyWorld.Size/(ValleyWorld.Resolution-1);
                var p=new Vector2(i%ValleyWorld.Resolution*spacing-Half,i/ValleyWorld.Resolution*spacing-Half);
                var rect=new Rect(p.x-spacing,p.y-spacing,spacing*2f,spacing*2f);float ceiling=float.PositiveInfinity;
                for(int z=Coordinate(rect.yMin);z<=Coordinate(rect.yMax);z++)for(int x=Coordinate(rect.xMin);x<=Coordinate(rect.xMax);x++)
                {
                    var cell=cells[z*Columns+x];
                    for(int n=cell.start;n<cell.start+cell.count;n++)
                    {var t=triangles[indices[n]];if(t.foundation!=0 && t.Intersects(rect))ceiling=Mathf.Min(ceiling,t.Height(p)-.025f);}
                }
                limits[i]=ceiling;
            }
        }
        public void Dispose()
        {
            if(triangles.IsCreated)triangles.Dispose();if(cells.IsCreated)cells.Dispose();if(indices.IsCreated)indices.Dispose();if(formation.IsCreated)formation.Dispose();
            triangleBuffer?.Dispose();cellBuffer?.Dispose();indexBuffer?.Dispose();triangleBuffer=cellBuffer=indexBuffer=null;
        }
    }
}
