using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>Projected opaque boundary edges, welded across separately authored strips and curves.</summary>
    static class MeshBoundary
    {
        public static List<(Vector3 a,Vector3 b)> Edges(Mesh mesh)
        {
            var data=new MeshData();data.positions.AddRange(mesh.vertices);data.colors.AddRange(mesh.colors);data.indices[0].AddRange(mesh.triangles);
            return Edges(data);
        }
        public static List<(Vector3 a,Vector3 b)> Edges(MeshData mesh,Func<int,bool> include=null)
        {
            var ids=new Dictionary<Vector2Int,int>();var points=new List<Vector3>();
            int Id(int vertex)
            {
                var p=mesh.positions[vertex];var key=new Vector2Int(Mathf.RoundToInt(p.x*1000),Mathf.RoundToInt(p.z*1000));
                if(ids.TryGetValue(key,out int id))return id;id=points.Count;ids.Add(key,id);points.Add(p);return id;
            }
            var counts=new Dictionary<(int,int),int>();
            var coverage=new Pavement();
            foreach(var triangles in mesh.indices)for(int i=0;i<triangles.Count;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(include!=null && !include(mesh.owners[a]))continue;
                if(mesh.colors.Count==mesh.Count && Mathf.Min(mesh.colors[a].a,mesh.colors[b].a,mesh.colors[c].a)<.99f)continue;
                if(Mathf.Abs(Vector3.Cross(mesh.positions[b]-mesh.positions[a],mesh.positions[c]-mesh.positions[a]).y)<.00001f)continue;
                coverage.AddTriangle(mesh.positions[a],mesh.positions[b],mesh.positions[c]);
                var vertices=new[]{Id(a),Id(b),Id(c)};
                for(int k=0;k<3;k++){int x=vertices[k],y=vertices[(k+1)%3];var edge=x<y?(x,y):(y,x);counts.TryGetValue(edge,out int n);counts[edge]=n+1;}
            }
            var result=new List<(Vector3,Vector3)>();
            foreach(var edge in counts)if(edge.Value==1)
            {
                var a=points[edge.Key.Item1];var b=points[edge.Key.Item2];var middle=Geo.XZ((a+b)*.5f);
                var side=Geo.Left((Geo.XZ(b)-Geo.XZ(a)).normalized)*.01f;
                // A strip's single cross edge may meet two shorter edges on a crowned curve.
                // Both sides are owned pavement there, so it is an interior seam rather than an outline.
                if(coverage.Contains(middle+side) && coverage.Contains(middle-side))continue;
                result.Add((a,b));
            }
            return result;
        }
    }
}
