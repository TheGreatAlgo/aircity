using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    public enum RoadBuildMode { Ground,Automatic,Bridge,Tunnel }
    public struct RoadBuildSettings : IEquatable<RoadBuildSettings>
    {
        public RoadBuildMode mode;
        public float bend;
        public bool smooth;
        internal bool legacyPrice;
        public static RoadBuildSettings Default=>new RoadBuildSettings {mode=RoadBuildMode.Automatic,smooth=true};
        public bool Equals(RoadBuildSettings other)=>mode==other.mode&&bend==other.bend&&smooth==other.smooth&&legacyPrice==other.legacyPrice;
        public override bool Equals(object other)=>other is RoadBuildSettings settings&&Equals(settings);
        public override int GetHashCode()=>HashCode.Combine(mode,bend,smooth,legacyPrice);
        public AlignmentShape Shape(Vector3 a,Vector3 b,float? initialGrade=null,float? finalGrade=null)
        {
            var delta=Alignment3D.XZ(b-a);float grade=(b.y-a.y)/Mathf.Max(.001f,delta.magnitude);
            return new AlignmentShape {curved=Mathf.Abs(bend)>.01f,control=(Alignment3D.XZ(a)+Alignment3D.XZ(b))*.5f+new Vector2(delta.y,-delta.x).normalized*bend,
                smooth=smooth,startGrade=initialGrade??grade,endGrade=finalGrade??grade};
        }
    }
    /// <summary>Pure quote and detached graph. Preview and commit consume one plan; source layout, terrain and
    /// funds are checked before any charge. Ground/bridge/tunnel spans keep one continuous authored profile.</summary>
    public sealed class RoadConstructionPlan
    {
        public PublicRoadNetwork Draft {get;private set;}
        public Alignment3D Path {get;private set;}
        public Rect Bounds {get;private set;}
        public int Price {get;private set;}
        public int EndNode {get;private set;}
        public Vector3 End=>Path.end;
        public IReadOnlyList<int> NewSegments=>made;
        readonly List<int> made=new List<int>();
        PublicRoadNetwork source;
        ValleyWorld world;
        Airport airport;
        int airportVersion;
        ulong sourceSignature,draftSignature;
        GameMode mode;
        bool committed;
        readonly Dictionary<int,float> terrain=new Dictionary<int,float>();

        public static bool TryCreate(CareerState state,PublicRoadNetwork network,int from,Vector3 end,int endNode,int endSegment,
            PublicRoadStyle style,RoadBuildSettings settings,out RoadConstructionPlan plan,out string problem,Airport airport=null)
        {
            plan=null;problem="Choose a valid road start and end.";
            var start=network?.Node(from);
            if(state==null || start==null || !Finite(end) || !Enum.IsDefined(typeof(PublicRoadStyle),style) || !Enum.IsDefined(typeof(RoadBuildMode),settings.mode) || !ValleyWorld.Finite(settings.bend))return false;
            if(endNode>=0){var snappedNode=network.Node(endNode);if(snappedNode==null)return false;end=snappedNode.position;}
            else if(endSegment>=0)
            {var edge=network.Segment(endSegment);if(edge==null)return false;var path=network.Path(edge);end=path.Position(path.ClosestParameter(Alignment3D.XZ(end)));}
            if(!Finite(end)||Vector2.Distance(Alignment3D.XZ(start.position),Alignment3D.XZ(end))<6f)return false;
            var delta=Alignment3D.XZ(end-start.position).normalized;
            float? GradeAt(int node,bool beginning)
            {
                var edges=network.At(node).ToArray();if(edges.Length!=1)return edges.Length>1?0f:(float?)null;
                var edge=edges[0];var path=network.Path(edge);bool atA=edge.a==node;
                var tangent=Alignment3D.XZ(path.Tangent(atA?0:1))*(atA?1:-1);
                if(Vector2.Dot(tangent,delta*(beginning?-1:1))<.985f)return 0f;
                return path.Grade(atA?0:1)*(atA?-1:1)*(beginning?1:-1);
            }
            var shape=settings.Shape(start.position,end,GradeAt(from,true),endNode>=0?GradeAt(endNode,false):null);
            var alignment=new Alignment3D(start.position,end,shape);
            if(alignment.MaximumGrade>PublicRoadConstruction.MaximumGroundGrade+.0001f)
            {problem="This road would be too steep. Give the ramp a longer approach (maximum 12% grade).";return false;}
            var candidate=new RoadConstructionPlan {source=network,world=state.world,sourceSignature=Signature(network),mode=state.mode,
                airport=airport,airportVersion=airport!=null?airport.LayoutVersion:0,Path=alignment};
            float half=RoadTypes.Of(style).FullWidth*.5f;double earthwork=0;
            var clearance=airport!=null?new AirportClearance(airport):null;
            int samples=Mathf.Max(1,Mathf.CeilToInt(alignment.Length/4f));
            var types=new RoadStructure[samples];var points=new Vector3[samples+1];
            for(int i=0;i<=samples;i++)points[i]=alignment.AtDistance(alignment.Length*i/samples);
            RoadStructure Kind(Vector3 point)
            {
                if(settings.mode==RoadBuildMode.Bridge)return RoadStructure.Elevated;
                if(settings.mode==RoadBuildMode.Tunnel)return RoadStructure.Tunnel;
                if(settings.mode==RoadBuildMode.Ground || state.world==null)return RoadStructure.Ground;
                float gap=point.y-state.world.Height(Alignment3D.XZ(point));
                if(gap>2f)return RoadStructure.Elevated;
                if(gap< -2f)return RoadStructure.Tunnel;
                return RoadStructure.Ground;
            }
            var min=Alignment3D.XZ(points[0]);var max=min;
            for(int i=0;i<samples;i++)
            {
                var mid=(points[i]+points[i+1])*.5f;types[i]=Kind(mid);
                float unit=alignment.Length/samples;
                for(int side=-1;side<=1;side++)
                {
                    var p=alignment.Offset(alignment.Length*(i+.5f)/samples,side*half);var at=Alignment3D.XZ(p);
                    min=Vector2.Min(min,at);max=Vector2.Max(max,at);
                    if(state.world!=null)
                    {
                        if(!state.world.RegionBounds.Contains(at)){problem="Keep the road inside the region.";return false;}
                        float h=state.world.Height(at);candidate.ProbeTerrain(at);
                        bool water=state.world.IsWater(at);
                        if(types[i]==RoadStructure.Ground && water)
                        {problem="Ground roads must stay on land. Raise the road for a bridge or lower it for a covered tunnel.";return false;}
                        if(types[i]==RoadStructure.Elevated && p.y<(water?state.world.WaterSurfaceAt(at)+1f:h-.2f))
                        {problem="The bridge deck intersects the hillside. Raise it or use Automatic sections.";return false;}
                        if(types[i]==RoadStructure.Tunnel && water && p.y+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover>h)
                        {problem="A tunnel entrance must be on dry land, with enough rock cover beneath water.";return false;}
                        if(types[i]==RoadStructure.Ground && !settings.legacyPrice)earthwork+=Mathf.Max(0,Mathf.Abs(p.y-h)-.35f)*unit*(half*2)/3f;
                    }
                }
                if(clearance!=null && !clearance.Clear(mid,half,types[i],out problem))return false;
            }
            // A short section remains a real section (including water crossings); no smoothing may silently turn
            // a small bridge or covered bore into a ground road. Junctions remain at continuous position/grade.
            var proposed=network.Clone();int node=endNode;
            if(node<0){var segment=endSegment>=0?proposed.Segment(endSegment):null;node=segment!=null?proposed.Split(segment,end):proposed.AddNode(end);}
            var newIds=new HashSet<int>();
            if(!proposed.Connect(from,node,style,RoadStructure.Ground,out problem,shape,ids=>newIds.UnionWith(ids)))return false;
            // Split at the actual mode transitions, preserving horizontal and vertical curves via de Casteljau/Hermite.
            for(int i=1;i<samples;i++)if(types[i]!=types[i-1])
            {
                var at=points[i];var edge=proposed.segments.Find(e=>newIds.Contains(e.id)&&proposed.Path(e).DistanceTo(Alignment3D.XZ(at))<.1f);
                if(edge!=null)
                {
                    int oldEnd=edge.b;int split=proposed.Split(edge,at);
                    var child=proposed.segments.Find(e=>e.id!=edge.id && e.a==split && e.b==oldEnd);
                    if(child!=null)newIds.Add(child.id);
                }
            }
            double cost=0;
            foreach(var edge in proposed.segments)if(newIds.Contains(edge.id))
            {
                var path=proposed.Path(edge);edge.structure=Kind(path.Position(.5f));candidate.made.Add(edge.id);
                cost+=path.Length*RoadTypes.Of(style).pricePerMetre*(edge.structure==RoadStructure.Elevated?3.5f:edge.structure==RoadStructure.Tunnel?5f:1f);
            }
            candidate.Price=Mathf.CeilToInt((float)(cost+earthwork*RunwayTerrainPlan.EarthworkPrice));
            candidate.EndNode=node;candidate.Draft=proposed;candidate.draftSignature=Signature(proposed);
            candidate.Bounds=Rect.MinMaxRect(min.x-3,min.y-3,max.x+3,max.y+3);
            plan=candidate;
            if(state.mode!=GameMode.Sandbox && state.funds<candidate.Price){problem="Not enough funds for this road.";return false;}
            problem=null;return true;
        }
        void ProbeTerrain(Vector2 p)
        {
            int x=Mathf.Clamp(Mathf.FloorToInt((p.x+ValleyWorld.Size*.5f)/world.Spacing),0,ValleyWorld.Resolution-2);
            int z=Mathf.Clamp(Mathf.FloorToInt((p.y+ValleyWorld.Size*.5f)/world.Spacing),0,ValleyWorld.Resolution-2);
            for(int dz=0;dz<=1;dz++)for(int dx=0;dx<=1;dx++){int key=(z+dz)*ValleyWorld.Resolution+x+dx;if(!terrain.ContainsKey(key))terrain.Add(key,world.heights[key]);}
        }
        public bool TryCommit(CareerState state,PublicRoadNetwork current,out PublicRoadNetwork result,out string problem)
        {
            result=current;problem="The layout or terrain changed. Move the pointer to refresh the road preview.";
            if(committed || state==null || state.mode!=mode || state.world!=world || current!=source || Signature(current)!=sourceSignature || Signature(Draft)!=draftSignature || airport!=null && airport.LayoutVersion!=airportVersion)return false;
            foreach(var item in terrain)if(world.heights[item.Key]!=item.Value)return false;
            if(state.mode!=GameMode.Sandbox && state.funds<Price){problem="Not enough funds for this road.";return false;}
            if(state.mode!=GameMode.Sandbox)state.funds-=Price;
            int cleared=0;
            foreach(var id in made){var edge=Draft.Segment(id);if(edge.structure==RoadStructure.Elevated)cleared+=RoadVegetation.ClearCanopies(world,Draft.Path(edge),edge.Type.FullWidth*.5f,Vector3.zero);}
            if(state.layout!=null)state.layout.clearedTrees+=cleared;
            committed=true;result=Draft;problem=null;return true;
        }
        internal sealed class AirportClearance
        {
            readonly Airport airport;
            readonly Runway[] runways;
            readonly (Vector2 a,Vector2 b,float half,float h0,float h1)[] taxiways;
            readonly List<Vector2>[] terminals;
            public AirportClearance(Airport airport)
            {
                this.airport=airport;runways=airport.Runways.ToArray();var net=airport.Taxiways;
                taxiways=net.edges.Where(e=>!e.IsRoad).Select(e=>(net.At(e.a),net.At(e.b),e.width*.5f,net.Node(e.a).position.y,net.Node(e.b).position.y)).ToArray();
                terminals=airport.terminals.Select(t=>t.Outline()).Concat(airport.Depots.Select(d=>d.Corners.ToList())).ToArray();
            }
            public bool Clear(Vector3 point,float half,RoadStructure structure,out string problem)
            {
                var local=airport.transform.InverseTransformPoint(point);var p=Geo.XZ(local);
                foreach(var runway in runways)
                    if(CareerConstruction.Distance(p,Geo.XZ(runway.start),Geo.XZ(runway.end))<runway.HalfWidthTotal+half+8f &&
                        !(structure==RoadStructure.Tunnel && local.y+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover<runway.ElevationAt(p)))
                    {problem="Keep this road clear of the runway, or use a sufficiently deep tunnel.";return false;}
                foreach(var edge in taxiways)
                {
                    Geo.ClosestOnSegment(p,edge.a,edge.b,out float t);
                    if(CareerConstruction.Distance(p,edge.a,edge.b)<edge.half+half+3f &&
                        !(structure==RoadStructure.Tunnel && local.y+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover<Mathf.Lerp(edge.h0,edge.h1,t)))
                    {problem="Keep this road clear of aircraft pavement, or use a sufficiently deep tunnel.";return false;}
                }
                foreach(var outline in terminals)if(Geo.Inside(outline,p)||Geo.DistanceToOutline(outline,p)<half+2f)
                {problem="Keep the road clear of building foundations.";return false;}
                problem=null;return true;
            }
        }
        public static ulong Signature(PublicRoadNetwork graph)
        {
            if(graph==null)return 0;ulong hash=1469598103934665603UL;
            void Add(int value){unchecked{hash^=(uint)value;hash*=1099511628211UL;}}
            void Float(float value)=>Add(BitConverter.SingleToInt32Bits(value));
            foreach(var n in graph.nodes){Add(n.id);Float(n.position.x);Float(n.position.y);Float(n.position.z);Add((int)n.control);Add(n.outsideConnection?1:0);Add(n.controlChosen?1:0);Add(n.terminalEntrance?.GetHashCode()??0);}
            foreach(var e in graph.segments){Add(e.id);Add(e.a);Add(e.b);Add((int)e.style);Add((int)e.structure);Add(e.alignment.GetHashCode());}
            return hash;
        }
        static bool Finite(Vector3 p)=>ValleyWorld.Finite(p.x)&&ValleyWorld.Finite(p.y)&&ValleyWorld.Finite(p.z);
    }
}
