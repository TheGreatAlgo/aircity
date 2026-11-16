using System;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>Lift the common 2D builders onto a saved pavement grade without changing their state channels.</summary>
    static class AirfieldElevation
    {
        public static void WarpOwned(MeshData mesh,Func<int,Vector2,float> height,bool directionNormals=false,bool fixtureOrigins=false)
        {
            for(int i=0;i<mesh.positions.Count;i++)
            {
                var p=mesh.positions[i];var at=new Vector2(p.x,p.z);
                var g=new Vector2(height(i,at+Vector2.right*.25f)-height(i,at-Vector2.right*.25f),height(i,at+Vector2.up*.25f)-height(i,at-Vector2.up*.25f))*2f;
                p.y+=height(i,at);mesh.positions[i]=p;var n=mesh.normals[i];
                mesh.normals[i]=directionNormals?new Vector3(n.x,n.y+g.x*n.x+g.y*n.z,n.z):new Vector3(n.x-g.x*n.y,n.y,n.z-g.y*n.y).normalized;
                var tangent=mesh.tangents[i];tangent.y+=g.x*tangent.x+g.y*tangent.z;mesh.tangents[i]=tangent;
                if(fixtureOrigins){var origin=mesh.uv1[i];origin.y+=height(i,new Vector2(origin.x,origin.z));mesh.uv1[i]=origin;}
            }
        }
        public static void Warp(MeshData mesh,Func<Vector2,float> height,Func<Vector2,Vector2> gradient,
            bool directionNormals=false,bool fixtureOrigins=false)
        {
            for(int i=0;i<mesh.positions.Count;i++)
            {
                var p=mesh.positions[i];var at=new Vector2(p.x,p.z);var g=gradient(at);
                p.y+=height(at);mesh.positions[i]=p;
                var n=mesh.normals[i];
                mesh.normals[i]=directionNormals?new Vector3(n.x,n.y+g.x*n.x+g.y*n.z,n.z):new Vector3(n.x-g.x*n.y,n.y,n.z-g.y*n.y).normalized;
                var t=mesh.tangents[i];t.y+=g.x*t.x+g.y*t.z;mesh.tangents[i]=t;
                if(fixtureOrigins)
                {var origin=mesh.uv1[i];origin.y+=height(new Vector2(origin.x,origin.z));mesh.uv1[i]=origin;}
            }
        }
    }
}
