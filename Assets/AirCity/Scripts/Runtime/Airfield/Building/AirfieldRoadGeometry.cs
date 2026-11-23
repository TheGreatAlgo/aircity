using System.Collections.Generic;
using System.Linq;
using AirCity.World;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>Ground ownership is separate from multi-level carriageways. Bridges preserve their undercroft;
    /// covered tunnels preserve their roof terrain. The shared structure builder owns the excavations.</summary>
    static class AirfieldRoadGeometry
    {
        public static LandscapeMesh Build(Airport airport, TaxiwayNetwork net, TaxiwayBuilder builder, MeshData surface,
            out List<BuiltSurfaceMap.Triangle> ownership, Dictionary<int, RoadBridgeDesign> visibleDesigns)
        {
            float Ground(Vector2 p) => airport.TerrainHeight?.Invoke(p) ?? 0f;
            var portalLinings = builder.PortalLinings(Ground, airport.BridgeWaterHeight);
            var retaining = RetainingRoads(net, Ground, airport.BridgeWaterHeight, portalLinings);
            ownership = Ownership(surface, builder.owners, portalLinings, retaining);
            return Structures(airport, net, builder, ownership, visibleDesigns: visibleDesigns,
                portalLinings: portalLinings, retaining: retaining);
        }

        static List<BuiltSurfaceMap.Triangle> Ownership(MeshData surface,IReadOnlyList<TaxiwayNetwork.Owner> owners,
            IReadOnlyDictionary<int, TaxiwayEdge> portalLinings, ISet<int> retaining)
        {
            var result=new List<BuiltSurfaceMap.Triangle>();
            foreach(var ids in surface.indices)for(int i=0;i<ids.Count;i+=3)
            {
                int a=ids[i],b=ids[i+1],c=ids[i+2],owner=surface.owners[a];
                if(owner>=0 && owners[owner].edges.All(e=>e.structure!=RoadStructure.Ground || portalLinings.ContainsKey(e.id)))continue;
                if(Mathf.Min(surface.colors[a].a,Mathf.Min(surface.colors[b].a,surface.colors[c].a))<.99f)continue;
                bool bank = owner >= 0 && owners[owner].edges.Any(edge => retaining.Contains(edge.id));
                bool hasRoadEdge = owner >= 0 && owners[owner].edges.All(edge => edge.IsRoad && edge.surface.IsPaved());
                var triangle = new BuiltSurfaceMap.Triangle(surface.positions[a], surface.positions[b], surface.positions[c],
                    foundation: !bank, excavationHeight: bank ? RoadStructureBuilder.PavementClipHeight : 0f,
                    excavationDepth: hasRoadEdge ? MeshData.RoadEdgeDepth : BuiltSurfaceMap.SurfaceTolerance);
                if(Mathf.Abs(Vector3.Cross(triangle.b-triangle.a,triangle.c-triangle.a).y)>.0001f)result.Add(triangle);
            }
            return result;
        }
        public static LandscapeMesh Structures(Airport airport,TaxiwayNetwork net,TaxiwayBuilder builder,List<BuiltSurfaceMap.Triangle> ownership,
            List<Vector3> piers=null,Dictionary<int,RoadBridgeDesign> visibleDesigns=null,
            IReadOnlyDictionary<int, TaxiwayEdge> portalLinings = null, ISet<int> retaining = null)
        {
            var mesh=new LandscapeMesh();float Ground(Vector2 p)=>airport.TerrainHeight!=null?airport.TerrainHeight(p):0f;
            var water=airport.BridgeWaterHeight;
            portalLinings ??= builder.PortalLinings(Ground, water);
            var pieces = builder.StructurePieces(portalLinings, retaining ?? RetainingRoads(net, Ground, water, portalLinings), Ground, water);
            foreach(var run in pieces.Where(p=>p.piece.structure==RoadStructure.Elevated&&!p.piece.run.closed&&
                p.piece.run.total>=RoadArchSpan.MinimumSpan).GroupBy(p=>p.piece.run.firstEdge))
            {
                var sections=new List<RoadSuspensionSpan.Section>();
                var edges=run.SelectMany(p=>new[]{p.edge,p.other}).ToHashSet();
                foreach(var part in run)
                {
                    var piece=part.piece;
                    var centre=piece.left.Select((point,i)=>(point+piece.right[i])*.5f).ToArray();
                    var stations=piece.Stations(Geo.Along(centre.Select(Geo.XZ).ToArray()));
                    for(int i=0;i<centre.Length;i++)
                    {
                        var width=Geo.XZ(piece.right[i]-piece.left[i]);
                        sections.Add(new RoadSuspensionSpan.Section {station=stations[i],centre=centre[i],
                            across=Geo.X0Z(width.normalized)*(piece.run.reversed?-1f:1f),halfWidth=width.magnitude*.5f+piece.verge});
                    }
                }
                var design=run.OrderBy(p=>p.piece.run.offset).Select(p=>p.edge.bridgeDesign)
                    .FirstOrDefault(d=>d!=RoadBridgeDesign.Automatic);
                var span=RoadBridgeSpan.Plan(run.First().piece.run,sections,design,Ground,water,(p,radius)=>MaySupport(p,radius,edges.Contains));
                foreach(var part in run)part.piece.bridge=span;
            }
            foreach(var item in pieces)
            {
                if(item.edge==item.other&&item.edge.structure==RoadStructure.Elevated&&visibleDesigns!=null)
                    visibleDesigns[item.edge.id]=item.piece.bridge.Design;
                RoadStructureBuilder.Build(item.piece,Ground,mesh,ownership,
                    (p,radius)=>MaySupport(p,radius,e=>e==item.edge||e==item.other),piers,water);
            }
            return mesh;

            bool MaySupport(Vector3 point,float radius,System.Predicate<TaxiwayEdge> partOfBridge)
            {
                var at=Geo.XZ(point);
                foreach(var edge in net.edges)
                {
                    if(partOfBridge(edge)||edge.structure==RoadStructure.Tunnel)continue;
                    var path=net.Path(edge);
                    if(path.DistanceTo(at)<edge.width*.5f+radius&&path.Height(path.ClosestParameter(at))<point.y-1f)return false;
                }
                foreach(var runway in airport.Runways)
                    if(CareerDistance(at,Geo.XZ(runway.start),Geo.XZ(runway.end))<runway.HalfWidthTotal+radius)return false;
                return true;
            }
        }
        static HashSet<int> RetainingRoads(TaxiwayNetwork net, System.Func<Vector2,float> ground, System.Func<Vector2,float?> water,
            IReadOnlyDictionary<int, TaxiwayEdge> portalLinings)
        {
            return net.edges.Where(edge => edge.IsRoad && edge.structure != RoadStructure.Tunnel && !portalLinings.ContainsKey(edge.id) &&
                RoadStructureBuilder.RetainsBank(net.Path(edge), edge.width * .5f + TaxiwayBuilder.StructureVerge, ground, water))
                .Select(edge => edge.id).ToHashSet();
        }

        static float CareerDistance(Vector2 p,Vector2 a,Vector2 b)=>Vector2.Distance(p,Geo.ClosestOnSegment(p,a,b,out _));
    }
}
