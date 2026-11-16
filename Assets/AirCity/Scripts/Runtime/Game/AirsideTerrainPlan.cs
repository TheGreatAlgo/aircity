using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AirCity.Game
{
    /// <summary>A pure construction quote over the actual pavement, including trimmed bends and junctions.</summary>
    public sealed class AirsideTerrainPlan
    {
        public const float MaximumTaxiwayGrade=.15f, MaximumRoadGrade=.20f;
        public Vector3 End { get; private set; }
        public Rect Bounds { get; private set; }
        public float Earthwork { get; private set; }
        public int ConstructionPrice { get; private set; }
        public int GradingPrice => Earthwork<1f ? 0 : Mathf.CeilToInt(Earthwork*RunwayTerrainPlan.EarthworkPrice);
        public int Price => ConstructionPrice+GradingPrice;
        public int EndNode { get; private set; }
        public int ChangedSamples { get; private set; }
        public float Grade { get; private set; }
        ValleyWorld world;
        Airport airport;
        int version, endNode;
        TaxiwayEdge endEdge;
        Runway endRunway;
        float width;
        bool applied, prepared;
        Stand stand;
        public Stand PlacedStand { get; private set; }
        public TaxiwayDraft Preview { get; private set; }
        (int id,Vector3 position,Runway runway)[] sourceNodes;
        (int id,int a,int b,float width,PavementSurface surface,RoadClass road,RoadStructure structure,AlignmentShape alignment)[] sourceEdges;
        TaxiwayNetwork.GraphSnapshot graph;
        readonly List<int> newRoadIds=new List<int>();
        readonly Dictionary<int,float> terrainProbes=new Dictionary<int,float>();
        static (int id,Vector3 position,Runway runway)[] Nodes(TaxiwayNetwork net)=>net.nodes.Select(n=>(n.id,n.position,n.runway)).ToArray();
        static (int id,int a,int b,float width,PavementSurface surface,RoadClass road,RoadStructure structure,AlignmentShape alignment)[] Edges(TaxiwayNetwork net)=>net.edges.Select(e=>(e.id,e.a,e.b,e.width,e.surface,e.road,e.structure,e.alignment)).ToArray();
        List<ForestTree> sourceTrees;
        int sourceTreeCount;
        readonly List<Vector2> treePositions=new List<Vector2>();
        readonly List<int> treesToClear=new List<int>(),propsToClear=new List<int>();
        readonly List<int> indices=new List<int>();
        readonly List<float> before=new List<float>(), after=new List<float>();
        readonly struct Triangle
        {
            public readonly Vector3 a,b,c;
            public Triangle(Vector3 a,Vector3 b,Vector3 c){this.a=a;this.b=b;this.c=c;}
            public float Distance(Vector2 p)
            {
                var aa=Geo.XZ(a);var bb=Geo.XZ(b);var cc=Geo.XZ(c);
                float x=Geo.Cross(bb-aa,p-aa),y=Geo.Cross(cc-bb,p-bb),z=Geo.Cross(aa-cc,p-cc);
                if(x>=-.001f && y>=-.001f && z>=-.001f || x<=.001f && y<=.001f && z<=.001f)return 0f;
                return Mathf.Min(CareerConstruction.Distance(p,aa,bb),Mathf.Min(CareerConstruction.Distance(p,bb,cc),CareerConstruction.Distance(p,cc,aa)));
            }
            public float Height(Vector2 p)
            {
                var n=Vector3.Cross(b-a,c-a);
                return a.y-(n.x*(p.x-a.x)+n.z*(p.y-a.z))/n.y;
            }
            public bool Intersects(Rect r)
            {
                if(r.Contains(Geo.XZ(a)) || r.Contains(Geo.XZ(b)) || r.Contains(Geo.XZ(c)))return true;
                Vector2 Corner(int i)=>new Vector2(i<2?r.xMin:r.xMax,i==0||i==3?r.yMin:r.yMax);
                for(int i=0;i<4;i++)
                {
                    var p=Corner(i);var q=Corner((i+1)%4);
                    if(Distance(p)<.001f)return true;
                    if(Geo.SegmentsMeet(p,q,Geo.XZ(a),Geo.XZ(b)) || Geo.SegmentsMeet(p,q,Geo.XZ(b),Geo.XZ(c)) || Geo.SegmentsMeet(p,q,Geo.XZ(c),Geo.XZ(a)))return true;
                }
                return false;
            }
        }

        [BurstCompile]
        struct NearestFormationJob:IJobParallelFor
        {
            [ReadOnly]public NativeArray<Triangle> triangles;
            [WriteOnly]public NativeArray<Vector2> samples;
            public int x0,z0,width;
            public float spacing;
            public void Execute(int i)
            {
                var p=new Vector2((x0+i%width)*spacing-ValleyWorld.Size*.5f,(z0+i/width)*spacing-ValleyWorld.Size*.5f);
                float best=float.MaxValue,height=0;
                foreach(var triangle in triangles)
                {float distance=triangle.Distance(p);if(distance>=best)continue;best=distance;height=triangle.Height(p)-.025f;}
                samples[i]=new Vector2(best,height);
            }
        }

        public static bool TryCreate(CareerState state,Airport airport,int from,Vector3 end,int endNode,TaxiwayEdge endEdge,
            Runway endRunway,float width,PavementSurface surface,RoadClass road,out AirsideTerrainPlan plan,out string problem,
            IEnumerable<GroundCoverInstance> ground=null,RoadBuildSettings? settings=null)
        {
            plan=null;problem="Choose a longer stretch of dry ground.";
            var world=state?.world;var net=airport!=null?airport.Taxiways:null;var start=net?.Node(from);
            if(world?.heights==null || start==null || !ValleyWorld.Finite(width) || width<1f || !ValleyWorld.Finite(end.x) || !ValleyWorld.Finite(end.y) || !ValleyWorld.Finite(end.z))return false;
            if(endNode>=0) { if(net.Node(endNode)==null)return false; end=net.Node(endNode).position; }
            else if(endEdge!=null)
            {
                if(!net.edges.Contains(endEdge))return false;
                var path=net.Path(endEdge);end=path.Position(path.ClosestParameter(Geo.XZ(end)));
                end.y=endEdge.IsRoad&&(endEdge.structure!=RoadStructure.Ground||endEdge.alignment.smooth||endEdge.alignment.curved)?
                    airport.RoadEdgeElevation(endEdge,Geo.XZ(end)):airport.PavementElevationAt(Geo.XZ(end));
            }
            else if(endRunway!=null)end=endRunway.ClosestOnCentreline(end);
            var options=road!=RoadClass.None?settings??new RoadBuildSettings {mode=RoadBuildMode.Ground}:new RoadBuildSettings {mode=RoadBuildMode.Ground};
            var shape=options.Shape(start.position,end);var alignment=new Alignment3D(start.position,end,shape);
            float length=alignment.Length;
            if(length<6f)return false;
            float limit=road==RoadClass.None?MaximumTaxiwayGrade:MaximumRoadGrade;
            if(alignment.MaximumGrade>limit+.0001f)
            {problem=$"This slope is too steep (maximum {limit*100f:0}% grade). Follow the contour.";return false;}
            if(road==RoadClass.None || !settings.HasValue)
            {
                problem=net.JoinProblem(Geo.XZ(start.position),from,null,Geo.XZ(end),endNode,endEdge,width,road);
                if(problem!=null && endRunway==null)return false;
            }
            RoadStructure Kind(Vector3 local)
            {
                if(options.mode==RoadBuildMode.Bridge)return RoadStructure.Elevated;
                if(options.mode==RoadBuildMode.Tunnel)return RoadStructure.Tunnel;
                if(options.mode==RoadBuildMode.Ground)return RoadStructure.Ground;
                float gap=local.y+airport.transform.position.y-world.Height(Geo.XZ(local)+Geo.XZ(airport.transform.position));
                return gap>2?RoadStructure.Elevated:gap< -2?RoadStructure.Tunnel:RoadStructure.Ground;
            }
            // Intersections are judged on their actual 3D paths. A separated crossing is never split into a node.
            foreach(var edge in net.edges)
            {
                if(edge.a==from||edge.b==from||edge.a==endNode||edge.b==endNode)continue;var existing=net.Path(edge);
                foreach(var span in alignment.Spans())foreach(var other in existing.Spans())
                {
                    if(!Geo.Segments(Geo.XZ(span.a),Geo.XZ(span.b),Geo.XZ(other.a),Geo.XZ(other.b),.001f,out var at))continue;
                    float t=alignment.ClosestParameter(at),h=alignment.Height(t),otherH=existing.Height(existing.ClosestParameter(at)),gap=Mathf.Abs(h-otherH);
                    if(gap<=.35f)continue;
                    if(!settings.HasValue || road==RoadClass.None || gap<PublicRoadNetwork.CrossingClearance)
                    {problem="Meet the existing pavement at its height, or leave enough clearance for an underpass.";return false;}
                    if(!edge.IsRoad && !(Kind(alignment.Position(t))==RoadStructure.Tunnel&&h+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover<otherH))
                    {problem="Use a deeper tunnel beneath aircraft pavement.";return false;}
                }
            }
            foreach(var runway in airport.Runways)
            {
                if(start.runway==runway || endRunway==runway || net.Node(endNode)?.runway==runway)continue;
                foreach(var span in alignment.Spans())
                {
                    if(!Geo.Segments(Geo.XZ(span.a),Geo.XZ(span.b),Geo.XZ(runway.start),Geo.XZ(runway.end),.001f,out var at))continue;
                    float t=alignment.ClosestParameter(at),h=alignment.Height(t),gap=Mathf.Abs(h-runway.ElevationAt(at));
                    if(gap<=.35f)continue;
                    if(road!=RoadClass.None&&settings.HasValue&&Kind(alignment.Position(t))==RoadStructure.Tunnel&&h+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover<runway.ElevationAt(at))continue;
                    problem="Meet the runway at its height, or use a sufficiently deep tunnel.";return false;
                }
            }
            var candidate=new AirsideTerrainPlan {world=world,airport=airport,version=airport.LayoutVersion,sourceTrees=world.trees,sourceTreeCount=world.trees.Count,
                sourceNodes=Nodes(net),sourceEdges=Edges(net),End=end,
                endNode=endNode,endEdge=endEdge,endRunway=endRunway,width=width,
                ConstructionPrice=Mathf.CeilToInt(length*(road==RoadClass.None?8f:12f)),Grade=(end.y-start.position.y)/length};
            var draft=net.ConstructionCopy();
            var triangles=new List<Triangle>();
            var origin=airport.transform.position;
            try
            {
                int to=candidate.Attach(draft);
                var made=draft.Connect(from,to,width,surface,false,road,true,RoadStructure.Ground,shape);
                candidate.EndNode=to;
                int samples=Mathf.Max(1,Mathf.CeilToInt(length/4f));var newIds=new HashSet<int>(made.Select(e=>e.id));
                if(road!=RoadClass.None&&settings.HasValue)
                {
                    var origin3=airport.transform.position;
                    var foundations=airport.terminals.Select(t=>(IList<Vector2>)t.Outline()).Concat(airport.Depots.Select(d=>(IList<Vector2>)d.Corners)).ToArray();
                    for(int i=0;i<samples;i++)
                    {
                        var local=alignment.AtDistance(length*(i+.5f)/samples);var at=Geo.XZ(local)+Geo.XZ(origin3);var type=Kind(local);
                        foreach(var outline in foundations)
                            if(Geo.Inside(outline,Geo.XZ(local))||Geo.DistanceToOutline(outline,Geo.XZ(local))<width*.5f+2f)
                            {problem="Keep the road clear of building foundations.";return false;}
                        for(int side=-1;side<=1;side++)
                        {
                            var sample=alignment.Offset(length*(i+.5f)/samples,side*width*.5f);
                            var point=Geo.XZ(sample)+Geo.XZ(origin3);float floor=sample.y+origin3.y;
                            if(!state.BuildableBounds.Contains(point)){problem="Keep the entire road inside the region.";return false;}
                            float h=world.Height(point);
                            if(type==RoadStructure.Ground&&world.IsWater(point)){problem="Raise this road for a bridge or lower it for a covered tunnel.";return false;}
                            if(type==RoadStructure.Elevated&&floor<(world.IsWater(point)?world.WaterSurfaceAt(point)+1f:h-.2f)){problem="The bridge intersects the hillside. Raise it or use Automatic sections.";return false;}
                            if(type==RoadStructure.Tunnel&&world.IsWater(point)&&floor+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover>h)
                            {problem="Tunnel entrances must stay on dry land, with sufficient cover beneath water.";return false;}
                            int x=Grid(point.x,world),z=Grid(point.y,world);
                            for(int dz=0;dz<=1;dz++)for(int dx=0;dx<=1;dx++){int key=Mathf.Min(z+dz,ValleyWorld.Resolution-1)*ValleyWorld.Resolution+Mathf.Min(x+dx,ValleyWorld.Resolution-1);candidate.terrainProbes[key]=world.heights[key];}
                        }
                        if(i==0)continue;
                        var left=alignment.AtDistance(length*(i-.5f)/samples);
                        if(Kind(left)==type)continue;
                        var cut=alignment.AtDistance(length*i/samples);var edge=draft.edges.Find(e=>newIds.Contains(e.id)&&draft.Path(e).DistanceTo(Geo.XZ(cut))<.1f);
                        if(edge==null)continue;
                        var previousIds=new HashSet<int>(draft.edges.Select(e=>e.id));draft.SplitEdge(edge,cut);newIds.Remove(edge.id);
                        foreach(var child in draft.edges)if(!previousIds.Contains(child.id))newIds.Add(child.id);
                    }
                    made=draft.edges.Where(e=>newIds.Contains(e.id)).ToList();candidate.newRoadIds.AddRange(newIds);double cost=0;
                    foreach(var edge in made)
                    {edge.structure=Kind(draft.Path(edge).Position(.5f));cost+=draft.Path(edge).Length*12f*(edge.structure==RoadStructure.Elevated?3.5f:edge.structure==RoadStructure.Tunnel?5f:1f);}
                    candidate.ConstructionPrice=Mathf.CeilToInt((float)cost);
                }
                if(made.Count==0){problem="This section is already connected.";return false;}
                var touched=new HashSet<int>(made.SelectMany(e=>new[]{e.a,e.b}));
                var affected=new HashSet<int>(draft.edges.Where(e=>touched.Contains(e.a)||touched.Contains(e.b)).Select(e=>e.id));
                var geometry=new TaxiwayDraft(airport,draft);candidate.Preview=geometry;candidate.graph=draft.Snapshot();
                geometry.Outline(affected);
                var builder=geometry.builder;var pavement=geometry.surface;
                foreach(var list in pavement.mesh.indices)
                for(int i=0;i<list.Count;i+=3)
                {
                    int ia=list[i],ib=list[i+1],ic=list[i+2],owner=pavement.mesh.owners[ia];
                    if(owner<0 || !builder.owners[owner].edges.Any(e=>affected.Contains(e.id)) || builder.owners[owner].edges.All(e=>e.structure!=RoadStructure.Ground))continue;
                    var a=pavement.mesh.positions[ia]+origin;var b=pavement.mesh.positions[ib]+origin;var c=pavement.mesh.positions[ic]+origin;
                    var normal=Vector3.Cross(b-a,c-a);
                    if(Mathf.Abs(normal.y)<.0001f)continue;
                    if(new Vector2(normal.x,normal.z).magnitude/Mathf.Abs(normal.y)>limit+.001f)
                    {problem="The bend would be too steep. Give the slope a longer, gentler approach.";return false;}
                    triangles.Add(new Triangle(a,b,c));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(draft.gameObject); }
            plan=candidate;
            bool valid=triangles.Count>0?PrepareGround(state,candidate,triangles,ground,out problem):PrepareStructures(state,candidate,alignment,ground,out problem);
            if(!valid)return false;
            var localBounds=alignment.Bounds(width+20);
            var area=new Rect(localBounds.position+Geo.XZ(origin),localBounds.size);
            candidate.Bounds=Rect.MinMaxRect(Mathf.Min(candidate.Bounds.xMin,area.xMin),Mathf.Min(candidate.Bounds.yMin,area.yMin),Mathf.Max(candidate.Bounds.xMax,area.xMax),Mathf.Max(candidate.Bounds.yMax,area.yMax));
            return true;
        }

        /// <summary>Same pure quote and accepted transaction for a stand or de-icing pad.</summary>
        public static bool TryCreateStand(CareerState state,Airport airport,StandLayout layout,
            out AirsideTerrainPlan plan,out string problem,IEnumerable<GroundCoverInstance> ground=null)
        {
            plan=null;problem="Choose a clear footprint inside the region.";
            if(state?.world?.heights==null || airport==null || layout==null)return false;
            if(!RunwayRules.StandSurfaceOk(layout.Code,layout.stand.surface) || !airport.Taxiways.Fits(layout))
            {problem="The stand overlaps existing pavement or another stand.";return false;}
            var net=airport.Taxiways;var world=state.world;
            var candidate=new AirsideTerrainPlan {world=world,airport=airport,version=airport.LayoutVersion,
                sourceTrees=world.trees,sourceTreeCount=world.trees.Count,sourceNodes=Nodes(net),sourceEdges=Edges(net),
                stand=layout.stand.At(layout.stand.position),ConstructionPrice=CareerConstruction.StandPrice};
            var layouts=net.Layouts();layouts.Add(layout);var runways=airport.Runways.ToArray();
            var profile=new TaxiwayBuilder(net,runways).Elevation();
            var pavement=new SurfaceBuilder(Surfaces.Count);
            new StandBuilder().Build(layout,pavement,new PaintBuilder(),System.Array.Empty<AirCity.Aircraft.AircraftType>());
            AirfieldElevation.Warp(pavement.mesh,p=>Airport.SupportElevation(p,profile,runways,layouts),_=>Vector2.zero);
            var triangles=new List<Triangle>();var origin=airport.transform.position;
            foreach(var list in pavement.mesh.indices)
                for(int i=0;i<list.Count;i+=3)
                {
                    var a=pavement.mesh.positions[list[i]]+origin;var b=pavement.mesh.positions[list[i+1]]+origin;var c=pavement.mesh.positions[list[i+2]]+origin;
                    if(Mathf.Abs(Vector3.Cross(b-a,c-a).y)>.0001f)triangles.Add(new Triangle(a,b,c));
                }
            plan=candidate;
            using var protection=new BuiltSurfaceMap();
            var transform=airport.transform.localToWorldMatrix;
            if(net.GroundOwnership!=null)foreach(var t in net.GroundOwnership)
                {if(t.foundation!=0)protection.AddTriangle(new BuiltSurfaceMap.Triangle(transform.MultiplyPoint3x4(t.a),transform.MultiplyPoint3x4(t.b),transform.MultiplyPoint3x4(t.c),true));}
            else protection.AddMesh(net.SurfaceMesh,transform);
            protection.AddMesh(airport.ApronMesh,transform);
            foreach(var runway in airport.Runways)protection.AddMesh(runway.SurfaceMesh,transform);
            protection.Build();
            return PrepareGround(state,candidate,triangles,ground,out problem,protection);
        }

        static bool PrepareGround(CareerState state,AirsideTerrainPlan candidate,List<Triangle> triangles,
            IEnumerable<GroundCoverInstance> ground,out string problem,BuiltSurfaceMap protection=null)
        {
            problem=null;
            var world=candidate.world;var airport=candidate.airport;var origin=airport.transform.position;
            if(triangles.Count==0){problem="Choose a longer stretch of dry ground.";return false;}
            float minX=triangles.Min(t=>Mathf.Min(t.a.x,t.b.x,t.c.x)),maxX=triangles.Max(t=>Mathf.Max(t.a.x,t.b.x,t.c.x));
            float minZ=triangles.Min(t=>Mathf.Min(t.a.z,t.b.z,t.c.z)),maxZ=triangles.Max(t=>Mathf.Max(t.a.z,t.b.z,t.c.z));
            foreach(var tri in triangles)
                foreach(var p in new[]{Geo.XZ(tri.a),Geo.XZ(tri.b),Geo.XZ(tri.c),(Geo.XZ(tri.a)+Geo.XZ(tri.b)+Geo.XZ(tri.c))/3f})
                {
                    if(!state.BuildableBounds.Contains(p)){problem="Keep the pavement inside the region.";return false;}
                    if(world.IsWater(p)){problem="Prepare dry land above the water before building here.";return false;}
                }
            var footprint=new Pavement();foreach(var triangle in triangles)footprint.AddTriangle(triangle.a,triangle.b,triangle.c);
            var query=Rect.MinMaxRect(minX-14f,minZ-14f,maxX+14f,maxZ+14f);
            foreach(int i in ForestQuery.Candidates(world,query))
            {
                var tree=world.trees[i];
                if(!tree.removed && footprint.Intersects(tree.position,3f))
                {candidate.treesToClear.Add(i);candidate.treePositions.Add(tree.position);}
            }
            if(ground!=null)foreach(var item in ground)
                if(query.Contains(item.position) && CareerConstruction.Clearable(item.kind) && !world.GroundPropRemoved(item.id) &&
                    footprint.Intersects(item.position,GroundCoverScatter.FootprintRadius(item.kind,item.scale)))
                candidate.propsToClear.Add(item.id);
            // Force every corner of every heightfield cell touched by pavement below its triangle plane.
            // Sampling only the centreline misses narrow roads, diagonal strips and the insides of curves.
            var caps=new Dictionary<int,float>();
            foreach(var tri in triangles)
            {
                int x0=Grid(Mathf.Min(tri.a.x,tri.b.x,tri.c.x),world),x1=Grid(Mathf.Max(tri.a.x,tri.b.x,tri.c.x),world);
                int z0=Grid(Mathf.Min(tri.a.z,tri.b.z,tri.c.z),world),z1=Grid(Mathf.Max(tri.a.z,tri.b.z,tri.c.z),world);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {
                    var r=new Rect(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f,world.Spacing,world.Spacing);
                    if(!tri.Intersects(r))continue;
                    for(int dz=0;dz<=1;dz++)for(int dx=0;dx<=1;dx++)
                    {
                        int xx=Mathf.Min(x+dx,ValleyWorld.Resolution-1),zz=Mathf.Min(z+dz,ValleyWorld.Resolution-1),key=zz*ValleyWorld.Resolution+xx;
                        var p=new Vector2(xx*world.Spacing-ValleyWorld.Size*.5f,zz*world.Spacing-ValleyWorld.Size*.5f);
                        float h=tri.Height(p)-.025f;
                        if(!caps.TryGetValue(key,out float old) || h<old)caps[key]=h;
                    }
                }
            }
            foreach(int key in caps.Keys)
            {
                var p=new Vector2((key%ValleyWorld.Resolution)*world.Spacing-ValleyWorld.Size*.5f,
                    (key/ValleyWorld.Resolution)*world.Spacing-ValleyWorld.Size*.5f);
                if(world.IsWater(p) && triangles.Any(t=>t.Distance(p)<world.Spacing*.6f))
                {problem="Prepare dry land above the water before building here.";return false;}
            }
            const float feather=30f;
            candidate.Bounds=Rect.MinMaxRect(minX-world.Spacing-feather,minZ-world.Spacing-feather,maxX+world.Spacing+feather,maxZ+world.Spacing+feather);
            var roads=state.RoadNetwork();
            var roadSpans=roads.segments.Where(e=>e.structure==RoadStructure.Ground).Select(e=>(
                a:roads.Node(e.a).position,b:roads.Node(e.b).position,half:e.Width*.5f)).ToArray();
            var protectedAreas=new List<IList<Vector2>>();
            foreach(var terminal in airport.terminals)protectedAreas.Add(terminal.Outline());
            foreach(var stand in airport.Taxiways.Layouts())protectedAreas.Add(stand.Pad);
            foreach(var depot in airport.Depots)protectedAreas.Add(depot.Corners);
            int sampleX0=Grid(candidate.Bounds.xMin,world),sampleX1=Grid(candidate.Bounds.xMax,world),sampleZ0=Grid(candidate.Bounds.yMin,world),sampleZ1=Grid(candidate.Bounds.yMax,world);
            int columns=sampleX1-sampleX0+1;
            using var queryTriangles=new NativeArray<Triangle>(triangles.ToArray(),Allocator.TempJob);
            using var nearest=new NativeArray<Vector2>(columns*(sampleZ1-sampleZ0+1),Allocator.TempJob);
            new NearestFormationJob {triangles=queryTriangles,samples=nearest,x0=sampleX0,z0=sampleZ0,width=columns,spacing=world.Spacing}.Schedule(nearest.Length,32).Complete();
            for(int z=sampleZ0;z<=sampleZ1;z++)for(int x=sampleX0;x<=sampleX1;x++)
            {
                int index=z*ValleyWorld.Resolution+x;
                var p=new Vector2(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f);
                float old=world.heights[index],next=old;
                if(caps.TryGetValue(index,out float cap))next=cap;
                else
                {
                    var formation=nearest[(z-sampleZ0)*columns+x-sampleX0];float best=formation.x,target=formation.y;
                    if(best>=feather+world.Spacing)continue;
                    float weight=1f-Mathf.SmoothStep(0f,1f,Mathf.Clamp01((best-world.Spacing)/feather));
                    next=Mathf.Lerp(old,target,weight);
                }
                if(protection!=null && protection.Formation.Length>0)next=Mathf.Min(next,protection.Formation[index]);
                if(Mathf.Abs(next-old)<.002f)next=old;
                candidate.indices.Add(index);candidate.before.Add(old);candidate.after.Add(next);
                if(next==old)continue;
                if(!state.BuildableBounds.Contains(p) || next<1f || next>ValleyWorld.HeightRange-1f || Mathf.Abs(next-old)>60f)
                {problem="This placement needs too much cut and fill. Choose gentler ground.";return false;}
                var local=p-Geo.XZ(origin);
                if(Mathf.Abs(next-old)>.15f && (airport.aprons.Any(a=>a.Contains(local,world.Spacing)) ||
                    protectedAreas.Any(outline=>Geo.Inside(outline,local)||Geo.DistanceToOutline(outline,local)<world.Spacing)))
                {problem="The earthwork would affect an apron or building. Level an approach to it first.";return false;}
                foreach(var span in roadSpans)
                {
                    var q=Geo.ClosestOnSegment(p,Geo.XZ(span.a),Geo.XZ(span.b),out float t);float distance=Vector2.Distance(p,q);
                    if(distance>=PublicRoadGeometry.EarthworkReach)continue;
                    float formation=Mathf.Lerp(span.a.y,span.b.y,t)+.125f;
                    float run=Mathf.Max(0f,distance-Mathf.Max(span.half+4f,world.Spacing*1.35f));
                    float eased=run*Mathf.SmoothStep(0f,1f,Mathf.Clamp01(run/12f));
                    if(next>formation+eased*.7f+.08f || next<formation-eased*.5f-.08f ||
                        caps.ContainsKey(index) && distance<span.half+world.Spacing)
                    {problem="This earthwork would affect a public road. Leave room for its roadbed.";return false;}
                }
                candidate.ChangedSamples++;
                candidate.Earthwork+=Mathf.Abs(next-old)*world.Spacing*world.Spacing;
            }
            candidate.prepared=true;
            if(state.mode!=GameMode.Sandbox && state.funds<candidate.Price)
            {problem="Not enough funds for this pavement and its grading.";return false;}
            problem=null;return true;
        }

        static bool PrepareStructures(CareerState state,AirsideTerrainPlan candidate,Alignment3D path,IEnumerable<GroundCoverInstance> ground,out string problem)
        {
            var origin=candidate.airport.transform.position;var min=Geo.XZ(path.start)+Geo.XZ(origin);var max=min;
            for(int i=0;i<=32;i++){var p=Geo.XZ(path.Position(i/32f))+Geo.XZ(origin);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            candidate.Bounds=Rect.MinMaxRect(min.x-candidate.width-10,min.y-candidate.width-10,max.x+candidate.width+10,max.y+candidate.width+10);
            using var footprint=new BuiltSurfaceMap();
            foreach(var t in candidate.Preview.ownership)
                if(candidate.Bounds.Overlaps(Rect.MinMaxRect(Mathf.Min(t.a.x,t.b.x,t.c.x)+origin.x,Mathf.Min(t.a.z,t.b.z,t.c.z)+origin.z,Mathf.Max(t.a.x,t.b.x,t.c.x)+origin.x,Mathf.Max(t.a.z,t.b.z,t.c.z)+origin.z)))
                    footprint.AddTriangle(new BuiltSurfaceMap.Triangle(t.a+origin,t.b+origin,t.c+origin));
            foreach(int index in ForestQuery.Candidates(candidate.world,candidate.Bounds))
            {var tree=candidate.world.trees[index];if(!tree.removed&&footprint.Contains(tree.position,3)){candidate.treesToClear.Add(index);candidate.treePositions.Add(tree.position);}}
            if(ground!=null)foreach(var item in ground)
                if(candidate.Bounds.Contains(item.position)&&CareerConstruction.Clearable(item.kind)&&!candidate.world.GroundPropRemoved(item.id)&&footprint.Contains(item.position,GroundCoverScatter.FootprintRadius(item.kind,item.scale)))candidate.propsToClear.Add(item.id);
            candidate.prepared=true;problem=null;
            if(state.mode!=GameMode.Sandbox&&state.funds<candidate.Price){problem="Not enough funds for this road structure.";return false;}return true;
        }

        int Attach(TaxiwayNetwork net)
        {
            if(endNode>=0)return endNode;
            if(endEdge!=null)return net.SplitEdge(net.Edge(endEdge.id),End);
            return net.AddNode(End,endRunway);
        }
        public bool TryCommit(CareerState state,out string problem)
        {
            problem="The ground or airport changed. Move the endpoint to refresh the quote.";
            if(!prepared || applied || state?.world!=world || airport.LayoutVersion!=version ||
                !sourceNodes.SequenceEqual(Nodes(airport.Taxiways)) || !sourceEdges.SequenceEqual(Edges(airport.Taxiways)))return false;
            if(world.trees!=sourceTrees || world.trees.Count!=sourceTreeCount)return false;
            for(int i=0;i<treesToClear.Count;i++)if(world.trees[treesToClear[i]].position!=treePositions[i])return false;
            for(int i=0;i<indices.Count;i++)if(world.heights[indices[i]]!=before[i])return false;
            foreach(var probe in terrainProbes)if(world.heights[probe.Key]!=probe.Value)return false;
            if(state.mode!=GameMode.Sandbox && state.funds<Price){problem="Not enough funds for this pavement and its grading.";return false;}
            if(stand!=null && !airport.Taxiways.Fits(StandLayout.Of(stand)))
            {problem="The stand footprint changed. Move it to refresh the quote.";return false;}
            for(int i=0;i<indices.Count;i++)world.heights[indices[i]]=after[i];
            int cleared=0;
            foreach(int index in treesToClear)if(!world.trees[index].removed){world.trees[index].removed=true;cleared++;}
            world.RemoveGroundProps(propsToClear);

            if(stand!=null)PlacedStand=airport.Taxiways.Add(stand);
            else
            {
                airport.Taxiways.Apply(graph);
                foreach(int id in newRoadIds)
                {var edge=airport.Taxiways.Edge(id);if(edge!=null&&edge.structure==RoadStructure.Elevated)cleared+=RoadVegetation.ClearCanopies(world,airport.Taxiways.Path(edge),edge.width*.5f,airport.transform.position);}
            }
            if(state.layout!=null)state.layout.clearedTrees+=cleared;
            if(state.mode!=GameMode.Sandbox)state.funds-=Price;
            if(state.layout!=null)state.layout.earthwork+=Earthwork;
            applied=true;problem=null;return true;
        }
        static int Grid(float v,ValleyWorld world)=>Mathf.Clamp(Mathf.FloorToInt((v+ValleyWorld.Size*.5f)/world.Spacing),0,ValleyWorld.Resolution-1);
    }
}
