using System.Collections.Generic;
using System.Linq;
using AirCity.World;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>Ground ownership is separate from multi-level carriageways. Bridges preserve their undercroft;
    /// covered tunnels preserve their roof terrain. The shared structure builder adds only portals and footings.</summary>
    static class AirfieldRoadGeometry
    {
        public static List<BuiltSurfaceMap.Triangle> Ownership(MeshData surface,IReadOnlyList<TaxiwayNetwork.Owner> owners)
        {
            var result=new List<BuiltSurfaceMap.Triangle>();
            foreach(var ids in surface.indices)for(int i=0;i<ids.Count;i+=3)
            {
                int a=ids[i],b=ids[i+1],c=ids[i+2],owner=surface.owners[a];
                if(owner>=0 && owners[owner].edges.All(e=>e.structure!=RoadStructure.Ground))continue;
                if(Mathf.Min(surface.colors[a].a,Mathf.Min(surface.colors[b].a,surface.colors[c].a))<.99f)continue;
                var triangle=new BuiltSurfaceMap.Triangle(surface.positions[a],surface.positions[b],surface.positions[c],true);
                if(Mathf.Abs(Vector3.Cross(triangle.b-triangle.a,triangle.c-triangle.a).y)>.0001f)result.Add(triangle);
            }
            return result;
        }
        public static LandscapeMesh Structures(Airport airport,TaxiwayNetwork net,TaxiwayBuilder builder,List<BuiltSurfaceMap.Triangle> ownership,List<Vector3> piers=null)
        {
            var mesh=new LandscapeMesh();float Ground(Vector2 p)=>airport.TerrainHeight!=null?airport.TerrainHeight(p):0f;
            foreach(var item in builder.StructurePieces())
                RoadStructureBuilder.Build(item.piece,Ground,mesh,ownership,(p,radius)=>
                {
                    var at=Geo.XZ(p);
                    foreach(var edge in net.edges)
                        if(edge!=item.edge && edge!=item.other && edge.structure!=RoadStructure.Tunnel && net.Path(edge).DistanceTo(at)<edge.width*.5f+radius &&
                            net.Path(edge).Height(net.Path(edge).ClosestParameter(at))<p.y-1f)return false;
                    foreach(var runway in airport.Runways)
                        if(CareerDistance(at,Geo.XZ(runway.start),Geo.XZ(runway.end))<runway.HalfWidthTotal+radius)return false;
                    return true;
                },piers);
            return mesh;
        }
        static float CareerDistance(Vector2 p,Vector2 a,Vector2 b)=>Vector2.Distance(p,Geo.ClosestOnSegment(p,a,b,out _));
    }
}
