using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// Where the airport has a hard (or at least built) surface to drive on: every runway with its shoulders and
    /// blast pads, taxiway, service road, junction and apron, taken from the generated surface meshes and bucketed
    /// in a coarse grid. The depots' ploughs use it to keep their turns off the snowy grass (VehicleManeuver).
    /// Airport.Pavement rebuilds it after the layout changes.
    /// </summary>
    public sealed class Pavement
    {
        const float Cell = 16f;

        readonly List<Vector2> tris = new List<Vector2>();
        readonly List<Vector3> planes = new List<Vector3>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();

        static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        /// <summary>Add a mesh's triangles; toAirport maps its vertices into the Airport's space.</summary>
        public void Add(Mesh mesh, Matrix4x4 toAirport)
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            var v = mesh.vertices;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var t = mesh.GetTriangles(sub);
                for (int i = 0; i < t.Length; i += 3)
                {
                    AddTriangle(toAirport.MultiplyPoint3x4(v[t[i]]),toAirport.MultiplyPoint3x4(v[t[i+1]]),toAirport.MultiplyPoint3x4(v[t[i+2]]));
                }
            }
        }

        /// <summary>Sparse build-time index; no region-sized native arrays or GPU buffers are needed.</summary>
        internal void AddTriangle(Vector3 aa,Vector3 bb,Vector3 cc)
        {
            var a=Flat(aa);var b=Flat(bb);var c=Flat(cc);
            if(Mathf.Abs(Cross(b-a,c-a))<1e-4f)return;
            int index=tris.Count/3;tris.Add(a);tris.Add(b);tris.Add(c);
            var normal=Vector3.Cross(bb-aa,cc-aa);float dx=-normal.x/normal.y,dz=-normal.z/normal.y;
            planes.Add(new Vector3(dx,dz,aa.y-dx*aa.x-dz*aa.z));
            var min=Vector2.Min(a,Vector2.Min(b,c));var max=Vector2.Max(a,Vector2.Max(b,c));
            for(int x=Mathf.FloorToInt(min.x/Cell);x<=Mathf.FloorToInt(max.x/Cell);x++)
            for(int y=Mathf.FloorToInt(min.y/Cell);y<=Mathf.FloorToInt(max.y/Cell);y++)
            {long key=Key(x,y);if(!grid.TryGetValue(key,out var list))grid[key]=list=new List<int>();list.Add(index);}
        }
        internal bool Intersects(Vector2 p,float radius)
        {
            float limit=radius*radius;
            for(int x=Mathf.FloorToInt((p.x-radius)/Cell);x<=Mathf.FloorToInt((p.x+radius)/Cell);x++)
            for(int y=Mathf.FloorToInt((p.y-radius)/Cell);y<=Mathf.FloorToInt((p.y+radius)/Cell);y++)
            {
                if(!grid.TryGetValue(Key(x,y),out var list))continue;
                foreach(int index in list)
                {
                    var a=tris[index*3];var b=tris[index*3+1];var c=tris[index*3+2];
                    float ab=Cross(b-a,p-a),bc=Cross(c-b,p-b),ca=Cross(a-c,p-c);
                    if(ab>=0 && bc>=0 && ca>=0 || ab<=0 && bc<=0 && ca<=0)return true;
                    if(DistanceSquared(p,a,b)<limit || DistanceSquared(p,b,c)<limit || DistanceSquared(p,c,a)<limit)return true;
                }
            }
            return false;
        }
        static float DistanceSquared(Vector2 p,Vector2 a,Vector2 b)
        {
            var d=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.000001f,d.sqrMagnitude));return (p-a-d*t).sqrMagnitude;
        }

        internal bool Project(Vector2 p,float hint,out float height,out Vector2 gradient)
        {
            height=0;gradient=Vector2.zero;
            if(!grid.TryGetValue(Key(Mathf.FloorToInt(p.x/Cell),Mathf.FloorToInt(p.y/Cell)),out var list))return false;
            float best=float.MaxValue;
            foreach(int index in list)
            {
                var a=tris[index*3];var b=tris[index*3+1];var c=tris[index*3+2];
                float x=Cross(b-a,p-a),y=Cross(c-b,p-b),z=Cross(a-c,p-c);
                if(!(x>=-.00001f && y>=-.00001f && z>=-.00001f || x<=.00001f && y<=.00001f && z<=.00001f))continue;
                var plane=planes[index];float h=plane.x*p.x+plane.y*p.y+plane.z;float distance=Mathf.Abs(h-hint);
                if(distance>=best)continue;best=distance;height=h;gradient=new Vector2(plane.x,plane.y);
            }
            return best<float.MaxValue;
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Is this ground point (x, z) on pavement?</summary>
        public bool Contains(Vector2 p)
        {
            if (!grid.TryGetValue(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell)), out var list)) return false;
            foreach (int i in list)
            {
                var a = tris[i * 3];
                var b = tris[i * 3 + 1];
                var c = tris[i * 3 + 2];
                float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
                bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(neg && pos)) return true;
            }
            return false;
        }

        public int TriangleCount => tris.Count / 3;
    }
}
