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
            // `level`: a junction or a corner, where the new road leaves level (and a lift keeps it so)
            float? GradeAt(int node,bool beginning,out bool level)
            {
                var edges=network.At(node).ToArray();level=edges.Length>1;if(edges.Length!=1)return edges.Length>1?0f:(float?)null;
                var edge=edges[0];var path=network.Path(edge);bool atA=edge.a==node;
                var tangent=Alignment3D.XZ(path.Tangent(atA?0:1))*(atA?1:-1);
                if(Vector2.Dot(tangent,delta*(beginning?-1:1))<.985f){level=true;return 0f;}
                return path.Grade(atA?0:1)*(atA?-1:1)*(beginning?1:-1);
            }
            bool endLevel=false;
            var shape=settings.Shape(start.position,end,GradeAt(from,true,out bool startLevel),endNode>=0?GradeAt(endNode,false,out endLevel):null);
            var startOutward = VisitorCentreConstruction.DrivewayOutward(state, start);
            var endOutward = VisitorCentreConstruction.DrivewayOutward(state, network.Node(endNode));
            if ((startOutward ?? endOutward) is Vector2 outward)
            {
                bool leavesDriveway = startOutward.HasValue;
                int entrance = leavesDriveway ? from : endNode;
                if (network.At(entrance).Any())
                { problem = "Join the visitor centre driveway outside the car park."; return false; }
                var mouth = Geo.XZ(leavesDriveway ? start.position : end);
                var lead = Geo.XZ(leavesDriveway ? end : start.position) - mouth;
                float forward = Vector2.Dot(lead, outward);
                if (forward < 6f || Mathf.Abs(Geo.Cross(lead, outward)) > forward)
                { problem = "Approach the visitor centre from outside the driveway opening."; return false; }
                // Match the parking aisle at its mouth, then bend into the requested connection.
                style = PublicRoadStyle.AirportAccess;
                shape.curved = Mathf.Abs(Geo.Cross(lead, outward)) > .001f;
                shape.control = mouth + outward * (forward * .8f);
                shape.smooth = true;
                if (leavesDriveway) { shape.startGrade = 0f; startLevel = true; }
                else { shape.endGrade = 0f; endLevel = true; }
            }
            if(Attempt(state,network,from,end,endNode,endSegment,style,settings,airport,shape,false,out plan,out problem,out var low,out bool bored))return true;
            if(plan!=null)return false;
            // A bridge too low over the water (a bank at the water's level, a lake standing above its shore, the joined
            // streets' grades sagging into the crossing): in Automatic and Bridge it arches over the water within the
            // grade limit, keeping both ends and their level junction exits, and is judged again. Automatic, having made
            // a bore of a stretch under the water's surface without cover, also tries it as a bridge. When nothing
            // builds, the first refusal stands.
            string first=problem;
            bool Lifted(LowDeck deck,bool bridgeWater,out RoadConstructionPlan built,out string why)
            {
                built=null;why=null;
                return deck!=null&&Lift(new Alignment3D(start.position,end,shape),deck,PublicRoadConstruction.MaximumGroundGrade,!startLevel,!endLevel,out var lifted)&&
                    Attempt(state,network,from,end,endNode,endSegment,style,settings,airport,lifted,bridgeWater,out built,out why,out _,out _);
            }
            if(Lifted(low,false,out plan,out problem))return true;
            if(plan==null&&bored)
            {
                if(Attempt(state,network,from,end,endNode,endSegment,style,settings,airport,shape,true,out plan,out problem,out low,out _))return true;
                if(plan==null&&Lifted(low,true,out plan,out problem))return true;
            }
            if(plan==null)problem=first;
            return false;
        }
        /// <summary>One judgement of a proposal along `shape` (TryCreate). `bridgeWater`: Automatic makes stretches under
        /// the surface without cover bridge. `low`: its bridge stretches too low at the water, if any; `bored`: Automatic
        /// made a bore of a stretch under the surface without cover.</summary>
        static bool Attempt(CareerState state,PublicRoadNetwork network,int from,Vector3 end,int endNode,int endSegment,
            PublicRoadStyle style,RoadBuildSettings settings,Airport airport,AlignmentShape shape,bool bridgeWater,
            out RoadConstructionPlan plan,out string problem,out LowDeck low,out bool bored)
        {
            plan=null;low=null;bored=false;var start=network.Node(from);
            var alignment=new Alignment3D(start.position,end,shape);
            if(alignment.MaximumGrade>PublicRoadConstruction.MaximumGroundGrade+.0001f)
            {problem=SteepProblem(alignment,PublicRoadConstruction.MaximumGroundGrade);return false;}
            var candidate=new RoadConstructionPlan {source=network,world=state.world,sourceSignature=Signature(network),mode=state.mode,
                airport=airport,airportVersion=airport!=null?airport.LayoutVersion:0,Path=alignment};
            float half=RoadTypes.Of(style).FullWidth*.5f;double earthwork=0;
            var clearance=airport!=null?new AirportClearance(airport):null;
            int samples=Mathf.Max(1,Mathf.CeilToInt(alignment.Length/4f));
            var points=new Vector3[samples+1];var gaps=new float[samples];var waters=new float[samples];var covered=new bool[samples];var banks=new float[samples];
            for(int i=0;i<=samples;i++)points[i]=alignment.AtDistance(alignment.Length*i/samples);
            for(int i=0;i<samples;i++)
            {
                var mid=(points[i]+points[i+1])*.5f;var at=Alignment3D.XZ(mid);
                gaps[i]=state.world!=null?mid.y-state.world.Height(at):0f;
                waters[i]=float.NaN;covered[i]=true;banks[i]=gaps[i];
                // a stretch is over water when its centre or either edge is (a shore met at an angle wets an edge first),
                // judged at the points the checks below use
                if(state.world!=null)for(int side=-1;side<=1;side++)
                {
                    var p=alignment.Offset(alignment.Length*(i+.5f)/samples,side*half);
                    OverWater(state.world,Alignment3D.XZ(p),p.y,ref waters[i],ref covered[i]);banks[i]=Mathf.Min(banks[i],p.y-state.world.Height(Alignment3D.XZ(p)));
                }
            }
            var types=state.world!=null?Structures(settings.mode,gaps,waters,covered,banks,bridgeWater):Enumerable.Repeat(settings.mode==RoadBuildMode.Bridge?RoadStructure.Elevated:
                settings.mode==RoadBuildMode.Tunnel?RoadStructure.Tunnel:RoadStructure.Ground,samples).ToArray();
            for(int i=0;i<samples&&settings.mode==RoadBuildMode.Automatic;i++)bored|=types[i]==RoadStructure.Tunnel&&waters[i]<0f&&!covered[i];
            // a deck too low over the water, whatever is refused first: TryCreate may lift it
            if(state.world!=null&&(settings.mode==RoadBuildMode.Automatic||settings.mode==RoadBuildMode.Bridge))low=LowDeck.Of(state.world,alignment,types,half,Vector3.zero);
            RoadStructure Kind(Vector3 point)=>types[Sample(alignment,samples,point)];
            // How far the worst stretch over water misses: a deck below WaterClearance, or a bore without cover.
            float WaterShortfall(RoadStructure kind)
            {
                float worst=0f;
                for(int i=0;i<samples;i++)if(types[i]==kind)for(int side=-1;side<=1;side++)
                {
                    var p=alignment.Offset(alignment.Length*(i+.5f)/samples,side*half);var at=Alignment3D.XZ(p);if(!state.world.IsWater(at))continue;
                    worst=Mathf.Max(worst,kind==RoadStructure.Elevated?state.world.WaterSurfaceAt(at)+WaterClearance-WaterTolerance-p.y:
                        p.y+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover-state.world.Height(at));
                }
                return worst;
            }
            var min=Alignment3D.XZ(points[0]);var max=min;
            for(int i=0;i<samples;i++)
            {
                var mid=(points[i]+points[i+1])*.5f;
                problem=VisitorCentreConstruction.OverlapProblem(state,VisitorCentreConstruction.Strip(
                    Alignment3D.XZ(points[i]),Alignment3D.XZ(points[i+1]),half),true);
                if(problem!=null)return false;
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
                        if(types[i]==RoadStructure.Ground && water){problem=GroundOnWater;return false;}
                        if(types[i]==RoadStructure.Elevated && water && p.y<state.world.WaterSurfaceAt(at)+WaterClearance-WaterTolerance)
                        {problem=LowOverWater(WaterShortfall(RoadStructure.Elevated));return false;}
                        if(types[i]==RoadStructure.Elevated && !water && p.y<h-HillsideTolerance)
                        {problem=IntoHillside(settings.mode==RoadBuildMode.Bridge&&Structures(RoadBuildMode.Automatic,gaps,waters,covered,banks)[i]==RoadStructure.Ground);return false;}
                        if(types[i]==RoadStructure.Tunnel && water && p.y+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover>h)
                        {problem=ShallowUnderWater(WaterShortfall(RoadStructure.Tunnel));return false;}
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
        /// <summary>Above (+) or below (−) the ground by more than this, a stretch is a bridge or a tunnel.</summary>
        public const float AtGrade=2f;
        /// <summary>A bridge deck stands at least this far above the water it crosses.</summary>
        public const float WaterClearance=1f;
        /// <summary>A deck within this of WaterClearance still passes: a road at a bank 1 m above the water, joined level
        /// across it, must not be refused for centimetres of pavement profile.</summary>
        internal const float WaterTolerance=.15f;
        internal const string GroundOnWater="Ground roads cannot cross water: use Auto or Bridge to bridge it, or Tunnel to pass beneath it.";
        internal static string LowOverWater(float shortfall)=>
            $"The deck must clear the water by {WaterClearance:0} m and is {Mathf.Max(.1f,shortfall):0.0} m too low: raise the road over the water "+
            "(Page Up, or a higher point there), or start the climb further back from the bank.";
        internal static string ShallowUnderWater(float shortfall)=>
            $"Too little cover under the water: the tunnel must be about {Mathf.Max(.1f,shortfall):0.0} m deeper there (Page Down), "+
            "with its descent starting further back from the bank.";
        /// <summary>A bridge stretch on dry land whose deck (or either edge) is further than this below the ground cuts
        /// into it and is refused.</summary>
        internal const float HillsideTolerance=.2f;
        /// <summary>Why a bridge stretch cuts into the ground beside it. Automatic sections help only where Bridge mode
        /// keeps a near-ground stretch as bridge (`automaticHelps`: Automatic would make it ground road).</summary>
        internal static string IntoHillside(bool automaticHelps)=>automaticHelps?
            "The bridge deck intersects the hillside. Raise it or use Automatic sections.":
            "The bridge deck cuts into the slope beside it: raise the road there (Page Up, or a higher point there), or keep it further from the slope.";

        /// <summary>
        /// The structure of each 4 m stretch of a proposal, from its level above (+) or below (−) the ground. In every
        /// mode but Ground the ground decides away from it: more than AtGrade above is a bridge and more than AtGrade
        /// below a tunnel, so a tunnel leaving a hillside crosses the valley as a bridge and a deck running into a hill
        /// bores through it. Near the ground, Automatic builds ground road; Bridge and Tunnel keep their structure
        /// where it runs between two stretches of it (a low viaduct, a shallow bore), but a near-ground stretch reaching
        /// an end of the proposal is the ground ramp that meets the road or terrain there. That is what lets a bridge
        /// or tunnel start from and finish on an ordinary road. Shared by public streets and airside service roads.
        /// Over water (`water[i]`: the lowest level above the surface of the stretch's centre and edges that are over
        /// water, NaN on dry land) the reference is the surface, never the bed: at or above it is a bridge (TryCreate
        /// then asks for WaterClearance); below it with a tunnel's cover under the bed (`covered[i]`; by default from
        /// the gap) a tunnel. Below the surface without cover it is neither yet: Bridge makes it bridge (too low: lifted
        /// or refused as a deck), Tunnel makes it tunnel (refused for cover), Automatic whichever is nearer (raising the
        /// deck to WaterClearance, or lowering the bore to cover, over the whole stretch under water; bridge with
        /// `bridgeWater`). Ground road within
        /// ShoreStretches of the water takes the crossing's structure, so a bridge's end stands on the bank, not at the
        /// water's edge (not where the bank is higher than the deck: that is a cutting, and stays ground; `bank[i]`, the
        /// lowest level above the ground across the stretch's width, by default its gap).
        /// </summary>
        public static RoadStructure[] Structures(RoadBuildMode mode,IReadOnlyList<float> gaps,IReadOnlyList<float> water=null,IReadOnlyList<bool> covered=null,
            IReadOnlyList<float> bank=null,bool bridgeWater=false)
        {
            int n=gaps.Count;var kinds=new RoadStructure[n];
            if(mode==RoadBuildMode.Ground)return kinds;
            bool Wet(int i)=>water!=null&&!float.IsNaN(water[i]);
            bool Open(int i)=>Wet(i)&&water[i]<0f&&!(covered!=null?covered[i]:gaps[i]+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover<=0f);
            for(int i=0;i<n;i++)kinds[i]=Wet(i)?(water[i]>=0f?RoadStructure.Elevated:RoadStructure.Tunnel):
                gaps[i]>AtGrade?RoadStructure.Elevated:gaps[i]< -AtGrade?RoadStructure.Tunnel:RoadStructure.Ground;
            for(int i=0;i<n;)
            {
                if(!Open(i)){i++;continue;}
                int j=i;while(j<n&&Open(j))j++;
                // Automatic: whichever is nearer, raising the deck to WaterClearance or lowering the bore to cover
                float raise=0f,lower=0f;
                for(int k=i;k<j;k++){raise=Mathf.Max(raise,WaterClearance-water[k]);lower=Mathf.Max(lower,gaps[k]+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover);}
                var kind=mode==RoadBuildMode.Tunnel||mode==RoadBuildMode.Automatic&&lower<raise&&!bridgeWater?RoadStructure.Tunnel:RoadStructure.Elevated;
                for(int k=i;k<j;k++)kinds[k]=kind;
                i=j;
            }
            if(mode==RoadBuildMode.Automatic){ShoreAbutments(kinds,Wet,bank??gaps);return kinds;}
            var keep=mode==RoadBuildMode.Bridge?RoadStructure.Elevated:RoadStructure.Tunnel;
            for(int i=0;i<n;)
            {
                if(kinds[i]!=RoadStructure.Ground){i++;continue;}
                int j=i;while(j<n&&kinds[j]==RoadStructure.Ground)j++;
                bool between=i>0&&j<n&&kinds[i-1]==keep&&kinds[j]==keep,side=true;
                for(int k=i;k<j&&between;k++)side&=keep==RoadStructure.Elevated?gaps[k]>=0f:gaps[k]<=0f;
                if(between&&side)for(int k=i;k<j;k++)kinds[k]=keep;
                i=j;
            }
            ShoreAbutments(kinds,Wet,bank??gaps);
            return kinds;
        }
        /// <summary>How many 4 m stretches of bank a crossing's structure reaches back from the water: its abutment or
        /// portal stands about 10 m inland, so the ground ramp's earthworks never regrade the water's edge.</summary>
        public const int ShoreStretches=3;
        static void ShoreAbutments(RoadStructure[] kinds,Func<int,bool> wet,IReadOnlyList<float> bank)
        {
            var source=(RoadStructure[])kinds.Clone();
            // a deck reaches back over the bank only while the bank is not higher than it (a cutting stays ground road)
            bool Bears(int k,RoadStructure kind)=>kind!=RoadStructure.Elevated||source[k]!=RoadStructure.Ground||bank[k]>=-HillsideTolerance;
            for(int i=0;i<kinds.Length;i++)
            {
                if(source[i]!=RoadStructure.Ground)continue;
                for(int d=1;d<=ShoreStretches;d++)
                {
                    int k=i-d>=0&&wet(i-d)?i-d:i+d<kinds.Length&&wet(i+d)?i+d:-1;
                    if(k<0)continue;
                    bool bears=true;for(int m=i;m!=k;m+=k>i?1:-1)bears&=Bears(m,source[k]);
                    if(bears)kinds[i]=source[k];
                    break;
                }
            }
        }
        /// <summary>Adds one point of a stretch (world position, the road's level `y` there) to the stretch's water level
        /// (lowest level above the surface of its points over water; NaN while none is) and cover (every point over
        /// water has a tunnel's clearance and cover under the bed: the same test TryCreate refuses a bore on).</summary>
        internal static void OverWater(ValleyWorld world,Vector2 at,float y,ref float water,ref bool covered)
        {
            if(!world.IsWater(at))return;
            float above=y-world.WaterSurfaceAt(at);water=float.IsNaN(water)?above:Mathf.Min(water,above);
            covered&=y+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover<=world.Height(at);
        }
        /// <summary>The bridge stretches of a proposal at the water (any point of it over water) that are too low: where
        /// (curve parameter) and how high (path coordinates) each must be, WaterClearance over the surface where it is
        /// over water and clear of the ground beside it (a shore met at an angle), and the worst miss over the water.</summary>
        internal sealed class LowDeck
        {
            public readonly List<(float t,float level)> need=new List<(float t,float level)>();
            public float shortfall;
            /// <summary>Null when every bridge stretch at the water is high enough. `origin` turns path coordinates into
            /// world ones.</summary>
            public static LowDeck Of(ValleyWorld world,Alignment3D path,IReadOnlyList<RoadStructure> kinds,float half,Vector3 origin)
            {
                var low=new LowDeck();int samples=kinds.Count;bool short_=false;
                for(int i=0;i<samples;i++)
                {
                    if(kinds[i]!=RoadStructure.Elevated)continue;
                    float distance=path.Length*(i+.5f)/samples,level=float.NegativeInfinity,bank=float.NegativeInfinity,y=0f;
                    for(int side=-1;side<=1;side++)
                    {
                        var p=path.Offset(distance,side*half);var at=Alignment3D.XZ(p)+Alignment3D.XZ(origin);y=p.y;
                        if(!world.IsWater(at)){bank=Mathf.Max(bank,world.Height(at)-origin.y-HillsideTolerance+.05f);continue;}
                        float surface=world.WaterSurfaceAt(at)-origin.y;level=Mathf.Max(level,surface+WaterClearance);
                        low.shortfall=Mathf.Max(low.shortfall,surface+WaterClearance-WaterTolerance-p.y);
                    }
                    if(level==float.NegativeInfinity)continue;
                    short_|=y<bank-.05f;
                    low.need.Add((path.ParameterAtDistance(distance),Mathf.Max(level,bank)));
                }
                return low.shortfall>0f||short_?low:null;
            }
        }
        /// <summary>
        /// The least change to a smooth profile's end grades that brings every point a low deck needs (curve parameter,
        /// level) up to its level within `limit` grade, keeping both ends where they are and changing only the free
        /// ones (not a level junction exit). Only raising the start grade and lowering the end grade are tried, so the
        /// whole profile rises (both Hermite tangent terms are positive inside the span): a deck sagging into the water
        /// with the grades of the streets it joins, or leaving a bank at the water's level, arches over it instead.
        /// False when no profile within the limit clears every point.
        /// </summary>
        internal static bool Lift(Alignment3D path,LowDeck low,float limit,bool startFree,bool endFree,out AlignmentShape lifted)
        {
            lifted=path.shape;var need=low.need;if(!path.shape.smooth||need.Count==0||!startFree&&!endFree)return false;
            var missing=new float[need.Count];for(int i=0;i<need.Count;i++)missing[i]=need[i].level-path.Height(need[i].t);
            const int Shares=40;float best=float.MaxValue;
            for(int k=0;k<=Shares;k++)
            {
                // r: the share of the lift carried by the start grade, the rest by the end grade
                float r=k/(float)Shares,amount=0f;bool possible=true;
                if(!startFree&&k>0||!endFree&&k<Shares)continue;
                for(int i=0;i<need.Count&&possible;i++)
                {
                    if(missing[i]<=0f)continue;
                    float t=need[i].t,t2=t*t,t3=t2*t,rate=(r*(t3-2f*t2+t)+(1f-r)*(t2-t3))*path.Length;
                    if(rate<1e-4f)possible=false;else amount=Mathf.Max(amount,missing[i]/rate);
                }
                float start=amount*r,end=amount*(1f-r),cost=start*start+end*end;
                if(!possible||cost>=best)continue;
                var shape=path.shape;shape.startGrade+=start;shape.endGrade-=end;
                if(new Alignment3D(path.start,path.end,shape).MaximumGrade>limit)continue;
                best=cost;lifted=shape;
            }
            return best<float.MaxValue;
        }
        /// <summary>The 4 m stretch (Structures) a point on the path falls in.</summary>
        internal static int Sample(Alignment3D path,int samples,Vector3 point)=>
            Mathf.Clamp(Mathf.FloorToInt(path.DistanceAtParameter(path.ClosestParameter(Alignment3D.XZ(point)))/Mathf.Max(.001f,path.Length)*samples),0,samples-1);

        /// <summary>Why a path is too steep, with the length it would need at `limit`.</summary>
        internal static string SteepProblem(Alignment3D path,float limit)
        {
            float rise=path.end.y-path.start.y,needed=path.Length*path.MaximumGrade/limit;
            return $"Too steep (maximum {limit*100f:0}% grade): {(rise>=0?"climbing":"descending")} {Mathf.Abs(rise):0.0} m needs about {Mathf.Ceil(needed/5f)*5f:0} m. "+
                "Click further away, or change the elevation.";
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
            foreach(var id in made)
            {
                var edge=Draft.Segment(id);
                foreach(var span in Draft.Path(edge).Spans())
                {
                    problem=VisitorCentreConstruction.OverlapProblem(state,VisitorCentreConstruction.Strip(
                        Alignment3D.XZ(span.a),Alignment3D.XZ(span.b),edge.Type.FullWidth*.5f),true);
                    if(problem!=null)return false;
                }
            }
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
            readonly List<Vector2[]> hangarApproaches = new List<Vector2[]>();
            public AirportClearance(Airport airport)
            {
                this.airport=airport;runways=airport.Runways.ToArray();var net=airport.Taxiways;
                taxiways=net.edges.Where(e=>!e.IsRoad).Select(e=>(net.At(e.a),net.At(e.b),e.width*.5f,net.Node(e.a).position.y,net.Node(e.b).position.y)).ToArray();
                terminals=airport.terminals.Select(t=>t.Outline()).Concat(airport.Depots.Select(d=>d.Corners.ToList()))
                    .Concat(airport.HangarBuildings.Select(h=>h.BuildingPad.ToList())).ToArray();
                foreach (var hangar in airport.HangarBuildings)
                    if (HangarConnection.Approach(hangar, out var approach)) hangarApproaches.Add(approach);
            }
            public bool Clear(Vector3 point,float half,RoadStructure structure,out string problem)
            {
                var local=airport.transform.InverseTransformPoint(point);var p=Geo.XZ(local);
                foreach (var approach in hangarApproaches)
                    if (Geo.Inside(approach, p) || Geo.DistanceToOutline(approach, p) < half + 2f)
                    { problem = "Keep the road clear of the hangar's paved taxiway approach."; return false; }
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
