using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Where a piece lies along its run: the unbroken stretch of one bridge through bends and
    /// straight-through nodes. Piers are spaced along the run, so they stay even however it was drawn.</summary>
    internal struct RunSpan
    {
        public float offset,total;
        /// <summary>The piece's own stations run against the run's.</summary>
        public bool reversed;
        public bool closed,freeStart,freeEnd;
        public ulong Hash
        {
            get
            {
                ulong hash=1469598103934665603UL;
                void Mix(uint value){unchecked{hash^=value;hash*=1099511628211UL;}}
                Mix((uint)BitConverter.SingleToInt32Bits(offset));Mix((uint)BitConverter.SingleToInt32Bits(total));
                Mix((reversed?1u:0u)|(closed?2u:0u)|(freeStart?4u:0u)|(freeEnd?8u:0u));return hash;
            }
        }
    }

    /// <summary>One piece of a bridge or tunnel: a section between its nodes' cuts, or a bend. Its cross-sections
    /// are the pavement's own (the carriageway edges at pavement level), so the structure follows the road exactly
    /// and neighbouring pieces share their boundary cross-section.</summary>
    internal sealed class StructurePiece
    {
        public RoadStructure structure;
        public readonly List<Vector3> left=new List<Vector3>(),right=new List<Vector3>();
        /// <summary>From the carriageway edge to the outer face of the parapet, or to the tunnel wall.</summary>
        public float verge;
        /// <summary>Height of the pavement above the road's own profile.</summary>
        public float surface;
        /// <summary>The paved strip between carriageway and parapet or wall (hard shoulder, or sidewalk behind its
        /// kerb): where it starts outside the carriageway edge, and how far it stands above it.</summary>
        public float stripFrom,stripRise;
        /// <summary>The structure stops at this end (anything but the same structure carrying on).</summary>
        public bool capStart,capEnd;
        /// <summary>A ground road takes over at this end: the deck rests on an abutment.</summary>
        public bool abutStart,abutEnd;
        /// <summary>A dead end: the pavement's outline round it, from one edge's last cross-section to the other's
        /// (each point on the pavement, with the direction pointing out of the road there). The parapet or wall is
        /// carried round it. Null: the end is open (the road carries on) or simply cut off.</summary>
        public List<(Vector3 point,Vector3 normal)> endStart,endEnd;
        /// <summary>How much of `length` lies beyond the first and the last cross-section: a rounded dead end.</summary>
        public float trimStart,trimEnd;
        /// <summary>Metres along the run at each cross-section (`along`: their distances from the first).</summary>
        public float[] Stations(float[] along)
        {
            int n=along.Length;float measured=along[n-1],whole=length>0?length:measured+trimStart+trimEnd;
            float scale=measured>.001f?(whole-trimStart-trimEnd)/measured:1f;var result=new float[n];
            for(int k=0;k<n;k++){float local=trimStart+along[k]*scale;result[k]=run.reversed?run.offset+whole-local:run.offset+local;}
            return result;
        }
        public RunSpan run;
        /// <summary>The piece's length as its run counts it (0: as measured along its cross-sections).</summary>
        public float length;
        public void Add(Vector3 l,Vector3 r){left.Add(l);right.Add(r);}
    }

    /// <summary>Shared bridge deck/supports and tunnel lining/portals. Structure surfaces never own the ground
    /// below a bridge or above a covered tunnel; only an open approach contributes terrain clipping.</summary>
    internal static class RoadStructureBuilder
    {
        public const float DeckDepth=.8f,TunnelClearance=5.5f,TunnelCover=1.5f;
        /// <summary>The edge of a deck, standing on the outer part of the verge: a concrete barrier set back on the
        /// deck's edge beam, and a steel rail on posts along its top (ParapetHeight: the rail's top above the verge).</summary>
        public const float ParapetWidth=.3f,ParapetHeight=1.1f,BarrierHeight=.65f,EdgeBeamRise=.12f,EdgeBeamStep=.06f;
        public const float RailDepth=.08f,RailWidth=.1f,RailInset=.165f,PostSpacing=2.5f,PostWidth=.07f;
        /// <summary>Vertex colours the RoadStructure shader reads: alpha 1 plain concrete, about one half a deck's
        /// side (panel joints along uv.x, metres along the run), 0 painted steel in the colour given (read as linear:
        /// this is galvanised grey in sunlight).</summary>
        static readonly Color32 Plain=new Color32(255,255,255,255),Panel=new Color32(255,255,255,128),Steel=new Color32(58,63,68,0);
        /// <summary>An open approach's retaining walls: their thickness, how far the coping stands above the ground
        /// they hold back, and how closely they sample that ground. A portal's lintel stands proud of its headwall.</summary>
        public const float WallThickness=.4f,Coping=.35f,WallStep=1.25f,LintelHeight=.7f,LintelProud=.35f,LampSpacing=12f;
        /// <summary>A lamp on a bore's ceiling: the shader shows this colour unlit (alpha about 0.9).</summary>
        static readonly Color32 Lamp=new Color32(255,232,190,230);
        /// <summary>The deck's edge is thinner than its middle: the underside tapers from here to DeckDepth.</summary>
        public const float EdgeDepth=.4f,GirderShare=.6f;
        /// <summary>Piers: the span aimed for, and how far a refused pier may move along the road.</summary>
        public const float PierSpan=30f,MaxPierSpan=38f,PierShift=16f,PierEndInset=1.5f,PierCap=1f,MinPierClearance=1.6f;

        public static Material Material(Material source)
        {
            var result=new Material(Resources.Load<Shader>("RoadStructure")) {name="Road concrete",hideFlags=HideFlags.DontSave};
            if(source!=null && source.HasProperty("_BaseColor"))result.SetColor("_BaseColor",source.GetColor("_BaseColor"));return result;
        }

        /// <summary>Lay out a run from any of its sections. `next(node, edge)` is the section the structure carries
        /// on with past that node (-1 where it stops); `bend(node, incoming edge)` the length of the bend there (0
        /// where the sections simply meet) and whether its own stations run against the walk; `free(node, edge)`
        /// whether the run's end there stands in the air. The run always starts at its end with the lower node id,
        /// so every piece (and every separately built chunk) computes the same stations.</summary>
        public static void Runs(int seed,Func<int,(int a,int b)> ends,Func<int,int,int> next,Func<int,float> length,
            Func<int,int,(float length,bool reversed)> bend,Func<int,int,bool> free,Dictionary<int,RunSpan> edgeRuns,Dictionary<int,RunSpan> bendRuns)
        {
            var seen=new HashSet<int> {seed};bool closed=false;
            (int edge,int node) Terminal(int node)
            {
                int edge=seed;
                for(int guard=0;guard<100000;guard++)
                {
                    int onward=next(node,edge);if(onward<0)break;
                    if(!seen.Add(onward)){closed=true;break;}
                    edge=onward;var e=ends(edge);node=e.a==node?e.b:e.a;
                }
                return(edge,node);
            }
            var start=Terminal(ends(seed).a);
            if(closed){int lowest=seed;foreach(int id in seen)lowest=Mathf.Min(lowest,id);start=(lowest,ends(lowest).a);}
            else
            {
                var other=Terminal(ends(seed).b);
                if(closed){int lowest=seed;foreach(int id in seen)lowest=Mathf.Min(lowest,id);start=(lowest,ends(lowest).a);}
                else if(other.node<start.node || other.node==start.node&&other.edge<start.edge)start=other;
            }
            var order=new List<(bool bend,int id)>();float offset=0;int current=start.edge,from=start.node,last=from;
            for(int guard=0;guard<100000;guard++)
            {
                var e=ends(current);int to=e.a==from?e.b:e.a;last=to;
                edgeRuns[current]=new RunSpan {offset=offset,reversed=from!=e.a};order.Add((false,current));offset+=length(current);
                int onward=next(to,current);if(onward<0)break;
                var arc=bend(to,current);
                if(arc.length>0){bendRuns[to]=new RunSpan {offset=offset,reversed=arc.reversed};order.Add((true,to));offset+=arc.length;}
                if(onward==start.edge)break;
                current=onward;from=to;
            }
            bool freeStart=!closed&&free(start.node,start.edge),freeEnd=!closed&&free(last,current);
            foreach(var item in order)
            {
                var runs=item.bend?bendRuns:edgeRuns;var span=runs[item.id];
                span.total=offset;span.closed=closed;span.freeStart=freeStart;span.freeEnd=freeEnd;runs[item.id]=span;
            }
        }

        /// <summary>Where a run's piers stand, in metres along it: evenly spaced at about PierSpan, and one just
        /// inside each end that stands in the air.</summary>
        public static List<float> PierStations(RunSpan run)
        {
            var result=new List<float>();if(run.total<=0)return result;
            if(!run.closed&&run.total<10f){if(run.freeStart||run.freeEnd)result.Add(run.total*.5f);return result;}
            int spans=PierSpans(run.total);float span=run.total/spans;
            for(int k=run.closed?0:1;k<spans;k++)result.Add(k*span);
            if(run.closed)return result;
            if(run.freeStart)result.Add(PierEndInset);
            if(run.freeEnd)result.Add(run.total-PierEndInset);
            return result;
        }

        public static int PierSpans(float total)=>Mathf.Max(1,Mathf.RoundToInt(total/PierSpan),Mathf.CeilToInt(total/MaxPierSpan));

        /// <summary>How many columns carry a deck this wide, and how thick they are for their height.</summary>
        public static int PierColumns(float width)=>width<=13f?1:width<=24f?2:3;
        public static float PierDiameter(float height)=>Mathf.Max(height<=8f?1.2f:height<=15f?1.5f:1.8f,height/12f);

        public static void Build(StructurePiece piece,Func<Vector2,float> ground,LandscapeMesh mesh,List<BuiltSurfaceMap.Triangle> openings,
            Func<Vector3,float,bool> support=null,List<Vector3> piers=null)
        {
            int n=piece.left.Count;if(piece.structure==RoadStructure.Ground||n<2)return;
            var centre=new Vector3[n];var across=new Vector3[n];var along=new float[n];var half=new float[n];
            for(int k=0;k<n;k++)
            {
                centre[k]=(piece.left[k]+piece.right[k])*.5f;var d=piece.right[k]-piece.left[k];d.y=0;half[k]=d.magnitude*.5f;across[k]=d.normalized;
                if(k>0)along[k]=along[k-1]+Vector2.Distance(Alignment3D.XZ(centre[k-1]),Alignment3D.XZ(centre[k]));
            }
            // A point across the piece: the carriageway keeps its own crossfall, anything outside it is level.
            Vector3 At(int k,float x)
            {
                var p=centre[k]+across[k]*x;float w=half[k];
                p.y=x<=-w?piece.left[k].y:x>=w?piece.right[k].y:Mathf.Lerp(piece.left[k].y,piece.right[k].y,(x+w)/Mathf.Max(.001f,2f*w));return p;
            }
            Vector3 Forward(int k)
            {var d=centre[Mathf.Min(k+1,n-1)]-centre[Mathf.Max(k-1,0)];d.y=0;return d.normalized;}
            float Ground(Vector3 p)=>ground(Alignment3D.XZ(p));
            mesh.colour=Plain;mesh.uv=null;
            if(piece.structure==RoadStructure.Elevated)
            {
                Deck(piece,mesh,openings,n,centre,half,across,along,At,Forward,Ground);
                // the deck leaves the mesh on its last detail (rail steel): everything after it is plain concrete again
                mesh.colour=Plain;mesh.uv=null;
                Piers(piece,mesh,n,centre,across,along,half,Ground,support,piers);
            }
            else Tunnel(piece,mesh,openings,n,centre,across,along,Ground);
            mesh.colour=Plain;mesh.uv=null;
        }

        static void Deck(StructurePiece piece,LandscapeMesh mesh,List<BuiltSurfaceMap.Triangle> openings,int n,Vector3[] centre,float[] half,Vector3[] across,
            float[] along,Func<int,float,Vector3> at,Func<int,Vector3> forward,Func<Vector3,float> ground)
        {
            var up=Vector3.up;float lift=piece.stripRise,barrier=lift+BarrierHeight,top=lift+ParapetHeight;
            // metres along the run at each cross-section: panel joints and rail posts carry on from piece to piece
            var stations=piece.Stations(along);
            float Run(int k)=>stations[k];
            for(int k=0;k+1<n;k++)
            {
                float w0=half[k]+piece.verge,w1=half[k+1]+piece.verge,g0=w0*GirderShare,g1=w1*GirderShare;
                var c0=at(k,0);var c1=at(k+1,0);
                if(Mathf.Max(ground(c0)-c0.y,ground(c1)-c1.y)+piece.surface>-.5f)
                {
                    var l0=at(k,-w0);var l1=at(k+1,-w1);var r0=at(k,w0);var r1=at(k+1,w1);
                    openings.Add(new BuiltSurfaceMap.Triangle(l0,l1,r1));openings.Add(new BuiltSurfaceMap.Triangle(l0,r1,r0));
                }
                // the underside: a flat girder soffit, with wings tapering up to the thin edges
                mesh.colour=Plain;mesh.uv=null;
                mesh.Face(at(k,-g0)+Vector3.down*DeckDepth,at(k+1,-g1)+Vector3.down*DeckDepth,at(k+1,g1)+Vector3.down*DeckDepth,at(k,g0)+Vector3.down*DeckDepth,Vector3.down);
                for(int side=-1;side<=1;side+=2)
                    DeckEdge(piece,mesh,at(k,w0*side),at(k+1,w1*side),across[k]*side,across[k+1]*side,Run(k),Run(k+1),
                        at(k,g0*side)+Vector3.down*DeckDepth,at(k+1,g1*side)+Vector3.down*DeckDepth,
                        at(k,half[k]*side)+across[k]*side*piece.stripFrom,at(k+1,half[k+1]*side)+across[k+1]*side*piece.stripFrom);
            }
            // a dead end: the same edge carried round the pavement's rounded end, the soffit filled in under it
            for(int end=0;end<2;end++)
            {
                var ring=Ring(piece,end==0,WallStep*4f);if(ring==null)continue;
                int k=end==0?0:n-1;float away=(end==1)!=piece.run.reversed?1f:-1f;
                var outer=new Vector3[ring.Count];var reach=new float[ring.Count];
                for(int i=0;i<ring.Count;i++)
                {
                    outer[i]=ring[i].point+ring[i].normal*piece.verge;
                    if(i>0)reach[i]=reach[i-1]+Vector2.Distance(Alignment3D.XZ(outer[i-1]),Alignment3D.XZ(outer[i]));
                }
                float round=reach[ring.Count-1];var under=centre[k]+Vector3.down*DeckDepth;
                Vector3 Girder(int i){var g=Vector3.Lerp(centre[k],outer[i],GirderShare);return g+Vector3.down*DeckDepth;}
                float RunAt(int i)=>Run(k)+away*Mathf.Min(reach[i],round-reach[i]);
                for(int i=0;i+1<ring.Count;i++)
                {
                    mesh.colour=Plain;mesh.uv=null;
                    mesh.Face(under,Girder(i),Girder(i+1),Vector3.down);
                    DeckEdge(piece,mesh,outer[i],outer[i+1],ring[i].normal,ring[i+1].normal,RunAt(i),RunAt(i+1),Girder(i),Girder(i+1),
                        ring[i].point+ring[i].normal*piece.stripFrom,ring[i+1].point+ring[i+1].normal*piece.stripFrom);
                }
            }
            mesh.uv=null;
            for(int end=0;end<2;end++)
            {
                if(!(end==0?piece.capStart:piece.capEnd)||(end==0?piece.endStart:piece.endEnd)!=null)continue;
                int k=end==0?0:n-1;float w=half[k]+piece.verge,g=w*GirderShare;var outward=forward(k)*(end==0?-1f:1f);
                var section=new[]
                {
                    at(k,-w),at(k,w),at(k,w)+Vector3.down*EdgeDepth,at(k,g)+Vector3.down*DeckDepth,at(k,-g)+Vector3.down*DeckDepth,at(k,-w)+Vector3.down*EdgeDepth
                };
                mesh.colour=Plain;
                for(int i=1;i+1<section.Length;i++)mesh.Face(section[0],section[i],section[i+1],outward);
                for(int side=-1;side<=1;side+=2)
                {
                    var o=at(k,w*side);var inward=-across[k]*side;
                    mesh.colour=Plain;
                    mesh.Face(o,o+inward*ParapetWidth,o+inward*ParapetWidth+up*(lift+EdgeBeamRise),o+up*(lift+EdgeBeamRise),outward);
                    mesh.Face(o+inward*EdgeBeamStep+up*(lift+EdgeBeamRise),o+inward*ParapetWidth+up*(lift+EdgeBeamRise),o+inward*(ParapetWidth-.03f)+up*barrier,o+inward*(EdgeBeamStep+.03f)+up*barrier,outward);
                    mesh.colour=Steel;
                    mesh.Face(o+inward*(RailInset-RailWidth*.5f)+up*(top-RailDepth),o+inward*(RailInset+RailWidth*.5f)+up*(top-RailDepth),
                        o+inward*(RailInset+RailWidth*.5f)+up*top,o+inward*(RailInset-RailWidth*.5f)+up*top,outward);
                }
                mesh.colour=Plain;
                if(!(end==0?piece.abutStart:piece.abutEnd))continue;
                // an abutment wall under the deck's end, down into the ground the road carries on along
                float soffit=at(k,0).y-DeckDepth,floor=Mathf.Min(ground(at(k,-w)),ground(at(k,0)),ground(at(k,w)))-.5f;
                if(soffit-floor<.6f)continue;
                var back=-outward*1f;var l=at(k,-w);var r=at(k,w);l.y=r.y=soffit;var fall=Vector3.down*(soffit-floor);
                mesh.Face(l,r,r+fall,l+fall,outward);mesh.Face(l+back,r+back,r+back+fall,l+back+fall,-outward);
                mesh.Face(l,l+back,l+back+fall,l+fall,-across[k]);mesh.Face(r,r+back,r+back+fall,r+fall,across[k]);
            }
        }

        /// <summary>One length of a deck's edge between two frames (`o`: the deck's outer line at the pavement
        /// edge's height, `outward`: pointing off the deck, `run`: metres along the bridge): the soffit's wing up from
        /// the girder, the paved strip, the edge beam, the barrier, and the rail on its posts.</summary>
        static void DeckEdge(StructurePiece piece,LandscapeMesh mesh,Vector3 o0,Vector3 o1,Vector3 out0,Vector3 out1,float run0,float run1,
            Vector3 girder0,Vector3 girder1,Vector3 strip0,Vector3 strip1)
        {
            var up=Vector3.up;float lift=piece.stripRise,barrier=lift+BarrierHeight,top=lift+ParapetHeight;
            // every vertex lies on one of the two frames: that one's distance along the run
            Vector2 Along(Vector3 p)
            {
                float d0=Mathf.Abs((p.x-o0.x)*out0.z-(p.z-o0.z)*out0.x),d1=Mathf.Abs((p.x-o1.x)*out1.z-(p.z-o1.z)*out1.x);
                return new Vector2(d0<=d1?run0:run1,0);
            }
            // a point of the edge's profile: `inward` from the deck's outer line, `height` above the carriageway edge
            Vector3 P(int frame,float inward,float height)=>(frame==0?o0-out0*inward:o1-out1*inward)+up*height;
            void Band(float inA,float hA,float inB,float hB,Vector3 facing)=>mesh.Face(P(0,inA,hA),P(1,inA,hA),P(1,inB,hB),P(0,inB,hB),facing);
            mesh.colour=Plain;mesh.uv=null;
            mesh.Face(girder0,girder1,P(1,0,-EdgeDepth),P(0,0,-EdgeDepth),Vector3.down+out0*.3f);
            Strip(piece,mesh,strip0,strip1,P(0,ParapetWidth,0),P(1,ParapetWidth,0));
            mesh.colour=Panel;mesh.uv=Along;
            Band(0,-EdgeDepth,0,lift+EdgeBeamRise,out0);                                           // the edge beam's face
            Band(0,lift+EdgeBeamRise,EdgeBeamStep,lift+EdgeBeamRise,up);                           // its ledge
            Band(EdgeBeamStep,lift+EdgeBeamRise,EdgeBeamStep+.03f,barrier,out0);                   // the barrier, leaning in a little
            Band(EdgeBeamStep+.03f,barrier,ParapetWidth-.03f,barrier,up);
            Band(ParapetWidth-.03f,barrier,ParapetWidth,0,-out0);                                  // its road side
            mesh.colour=Steel;mesh.uv=null;
            float railOut=RailInset-RailWidth*.5f,railIn=RailInset+RailWidth*.5f;
            Band(railOut,top,railIn,top,up);Band(railOut,top-RailDepth,railIn,top-RailDepth,Vector3.down);
            Band(railOut,top-RailDepth,railOut,top,out0);Band(railIn,top-RailDepth,railIn,top,-out0);
            float low=Mathf.Min(run0,run1),high=Mathf.Max(run0,run1);
            for(int post=Mathf.CeilToInt(low/PostSpacing-.5f);(post+.5f)*PostSpacing<high;post++)
            {
                float t=Mathf.InverseLerp(run0,run1,(post+.5f)*PostSpacing);var outward=Vector3.Lerp(out0,out1,t).normalized;
                var foot=Vector3.Lerp(o0,o1,t)-outward*RailInset;float height=top-RailDepth-barrier;
                mesh.OrientedBox(foot+up*(barrier+height*.5f),new Vector3(PostWidth,height,PostWidth),Quaternion.LookRotation(new Vector3(-outward.z,0,outward.x)));
            }
            mesh.colour=Plain;mesh.uv=null;
        }

        /// <summary>A square pavement end as a dead end's outline: its two corners, each turned through a quarter
        /// circle, so the parapet or wall rounds them by its own distance from the pavement.</summary>
        public static List<(Vector3 point,Vector3 normal)> SquareEnd(Vector3 left,Vector3 right,Vector3 outward)
        {
            var across=right-left;across.y=0;across.Normalize();outward.y=0;outward.Normalize();
            var ring=new List<(Vector3,Vector3)>();
            for(int i=0;i<=4;i++){float a=i*Mathf.PI/8f;ring.Add((left,-across*Mathf.Cos(a)+outward*Mathf.Sin(a)));}
            for(int i=0;i<=4;i++){float a=i*Mathf.PI/8f;ring.Add((right,outward*Mathf.Cos(a)+across*Mathf.Sin(a)));}
            return ring;
        }

        /// <summary>A dead end's outline, starting and ending exactly on the piece's last cross-section, with no two
        /// points further apart than `step`.</summary>
        static List<(Vector3 point,Vector3 normal)> Ring(StructurePiece piece,bool start,float step)
        {
            var source=start?piece.endStart:piece.endEnd;if(source==null||source.Count<2)return null;
            int k=start?0:piece.left.Count-1;var ring=new List<(Vector3 point,Vector3 normal)>();
            for(int i=0;i<source.Count;i++)
            {
                var item=source[i];item.normal.y=0;item.normal.Normalize();
                if(i==0||i==source.Count-1)
                {
                    bool left=(item.point-piece.left[k]).sqrMagnitude<=(item.point-piece.right[k]).sqrMagnitude;
                    item.point=left?piece.left[k]:piece.right[k];
                }
                if(i>0)
                {
                    var last=ring[ring.Count-1];int parts=Mathf.CeilToInt(Vector3.Distance(last.point,item.point)/step);
                    for(int j=1;j<parts;j++)ring.Add((Vector3.Lerp(last.point,item.point,j/(float)parts),Vector3.Lerp(last.normal,item.normal,j/(float)parts).normalized));
                }
                ring.Add(item);
            }
            return ring;
        }

        /// <summary>The paved strip outside the carriageway, from its start out to the parapet or wall.</summary>
        static void Strip(StructurePiece piece,LandscapeMesh mesh,Vector3 a0,Vector3 a1,Vector3 b0,Vector3 b1)
        {
            if(Vector2.Distance(Alignment3D.XZ(a0),Alignment3D.XZ(b0))<.02f)return;
            var rise=Vector3.up*piece.stripRise;
            // round a square corner the strip's inner points coincide: a triangle
            if((a0-a1).sqrMagnitude<1e-6f)mesh.Face(a0+rise,b1+rise,b0+rise,Vector3.up);
            else mesh.Face(a0+rise,a1+rise,b1+rise,b0+rise,Vector3.up);
        }

        static void Piers(StructurePiece piece,LandscapeMesh mesh,int n,Vector3[] centre,Vector3[] across,float[] along,float[] half,
            Func<Vector3,float> ground,Func<Vector3,float,bool> support,List<Vector3> placed)
        {
            float measured=along[n-1],length=piece.length>0?piece.length:measured+piece.trimStart+piece.trimEnd;var run=piece.run;
            bool openStart=piece.capStart||piece.endStart!=null,openEnd=piece.capEnd||piece.endEnd!=null;
            if(run.total<=0)run=new RunSpan {total=length,freeStart=openStart&&!piece.abutStart,freeEnd=openEnd&&!piece.abutEnd};
            if(measured<=.01f)return;
            float covered=Mathf.Max(.01f,length-piece.trimStart-piece.trimEnd);
            // false only where something below forbids a support here; a deck resting on its embankment needs none
            bool Try(float s)
            {
                int k=0;while(k+2<n&&along[k+1]<s)k++;
                float t=Mathf.InverseLerp(along[k],along[k+1],s);
                // under a rounded dead end the deck carries on past the last cross-section
                var c=Vector3.LerpUnclamped(centre[k],centre[k+1],(s-along[k])/Mathf.Max(.001f,along[k+1]-along[k]));var x=Vector3.Lerp(across[k],across[k+1],t).normalized;var forward=new Vector3(-x.z,0,x.x);
                float width=2f*(Mathf.Lerp(half[k],half[k+1],t)+piece.verge),soffit=c.y-DeckDepth;
                int columns=PierColumns(width);
                var offsets=columns==1?new[]{0f}:columns==2?new[]{-.19f*width,.19f*width}:new[]{-.23f*width,0f,.23f*width};
                float low=float.MaxValue,high=float.MinValue;
                foreach(float offset in offsets){float g=ground(c+x*offset);low=Mathf.Min(low,g);high=Mathf.Max(high,g);}
                if(soffit-high<MinPierClearance)return true;
                float diameter=PierDiameter(soffit-low),radius=diameter*.5f;
                if(support!=null)foreach(float offset in offsets)if(!support(c+x*offset,radius+.6f))return false;
                float cap=Mathf.Min(PierCap,soffit-high-.5f),reach=Mathf.Abs(offsets[0]);
                // the crosshead: a hammerhead on a single column, a beam across a row of them
                float girder=width*.5f*GirderShare,deep=radius+.2f;
                float top=columns==1?Mathf.Max(radius+.4f,width*.25f):Mathf.Min(reach+radius+.9f,girder),bottom=columns==1?radius+.15f:Mathf.Min(reach+radius+.2f,girder);
                var head=new Vector3(c.x,soffit,c.z);var foot=head+Vector3.down*cap;
                Vector3 Corner(Vector3 origin,float sideways,float ahead)=>origin+x*sideways+forward*ahead;
                mesh.Face(Corner(foot,-bottom,-deep),Corner(foot,bottom,-deep),Corner(foot,bottom,deep),Corner(foot,-bottom,deep),Vector3.down);
                for(int face=-1;face<=1;face+=2)
                {
                    mesh.Face(Corner(head,-top,deep*face),Corner(head,top,deep*face),Corner(foot,bottom,deep*face),Corner(foot,-bottom,deep*face),forward*face);
                    mesh.Face(Corner(head,top*face,-deep),Corner(head,top*face,deep),Corner(foot,bottom*face,deep),Corner(foot,bottom*face,-deep),x*face+Vector3.down*.5f);
                }
                foreach(float offset in offsets)
                {
                    var p=c+x*offset;float floor=ground(p);
                    for(int i=0;i<4;i++)floor=Mathf.Min(floor,ground(p+Quaternion.Euler(0,i*90f,0)*Vector3.right*radius));
                    floor-=.6f;mesh.Tube(new Vector3(p.x,floor,p.z),foot.y-floor,radius,16);
                    placed?.Add(new Vector3(p.x,foot.y,p.z));
                }
                return true;
            }
            float shift=Mathf.Min(PierShift,run.total/PierSpans(run.total)*.45f),end=run.offset+length;
            foreach(float station in PierStations(run))
            {
                // each piece owns the stations from its start up to (not including) its end; the run's last piece owns that too
                if(station<run.offset||station>=end&&end<run.total)continue;
                float local=Mathf.Clamp(station-run.offset,0,length);
                float s=((run.reversed?length-local:local)-piece.trimStart)*measured/covered;
                if(Try(s))continue;
                for(int step=1;step<=Mathf.FloorToInt(shift);step++)
                {
                    if(s+step<=measured-.5f&&s+step>=0&&Try(s+step))break;
                    if(s-step>=.5f&&s-step<=measured&&Try(s-step))break;
                }
            }
        }

        /// <summary>A point on a tunnel's edge: on the pavement's edge, with the direction to its wall, the road's
        /// middle there (roof and terrain opening reach to it) and whether the road is under ground there.</summary>
        struct Rim {public Vector3 p,n,mid;public float run;public bool covered;}

        static void Tunnel(StructurePiece piece,LandscapeMesh mesh,List<BuiltSurfaceMap.Triangle> openings,int n,Vector3[] centre,Vector3[] across,
            float[] along,Func<Vector3,float> ground)
        {
            var up=Vector3.up;float reach=piece.verge+WallThickness;
            var stations=piece.Stations(along);
            float Run(int k)=>stations[k];
            // Under ground only where the whole width has its cover: a bore never shows through a hillside's flank.
            bool Covered(Vector3 mid,Vector3 right,float half)=>
                mid.y-piece.surface+TunnelClearance+TunnelCover<Mathf.Min(ground(mid),ground(mid-right*(half+reach)),ground(mid+right*(half+reach)));
            // the pavement's cross-sections, with more between them in the open: walls follow the ground closely
            var left=new List<Rim>();var right=new List<Rim>();var under=new bool[n];
            for(int k=0;k<n;k++)under[k]=Covered(centre[k],across[k],Vector2.Distance(Alignment3D.XZ(piece.left[k]),Alignment3D.XZ(piece.right[k]))*.5f);
            for(int k=0;k<n;k++)
            {
                int parts=k+1<n&&!(under[k]&&under[k+1])?Mathf.Max(1,Mathf.CeilToInt((along[k+1]-along[k])/WallStep)):1;
                for(int j=0;j<parts;j++)
                {
                    float t=j/(float)parts;int next=Mathf.Min(k+1,n-1);
                    var l=Vector3.Lerp(piece.left[k],piece.left[next],t);var r=Vector3.Lerp(piece.right[k],piece.right[next],t);
                    var x=Vector3.Lerp(across[k],across[next],t).normalized;var mid=(l+r)*.5f;float run=Mathf.Lerp(Run(k),Run(next),t);
                    bool covered=Covered(mid,x,Vector2.Distance(Alignment3D.XZ(l),Alignment3D.XZ(r))*.5f);
                    left.Add(new Rim {p=l,n=-x,mid=mid,run=run,covered=covered});right.Add(new Rim {p=r,n=x,mid=mid,run=run,covered=covered});
                }
            }
            int count=left.Count;
            Bore(piece,mesh,openings,left,ground);Bore(piece,mesh,openings,right,ground);
            for(int i=0;i+1<count;i++)
            {
                if(left[i].covered!=left[i+1].covered){int portal=left[i].covered?i:i+1;Portal(piece,mesh,left[portal],right[portal],left[portal==i?i+1:i].mid,ground);}
                if(!left[i].covered||!left[i+1].covered)continue;
                // lamps along the ceiling, one row over each side of the road
                float low=Mathf.Min(left[i].run,left[i+1].run),high=Mathf.Max(left[i].run,left[i+1].run);
                for(int lamp=Mathf.CeilToInt(low/LampSpacing-.5f);(lamp+.5f)*LampSpacing<high;lamp++)
                {
                    float t=Mathf.InverseLerp(left[i].run,left[i+1].run,(lamp+.5f)*LampSpacing);
                    var l=Vector3.Lerp(left[i].p,left[i+1].p,t);var r=Vector3.Lerp(right[i].p,right[i+1].p,t);var ahead=left[i+1].mid-left[i].mid;ahead.y=0;
                    if(ahead.sqrMagnitude<1e-6f)continue;
                    mesh.colour=Lamp;mesh.uv=null;
                    foreach(float share in new[]{.25f,.75f})
                        mesh.OrientedBox(Vector3.Lerp(l,r,share)+up*(TunnelClearance-.06f),new Vector3(.35f,.1f,1.6f),Quaternion.LookRotation(ahead.normalized));
                    mesh.colour=Plain;
                }
            }
            for(int end=0;end<2;end++)
            {
                int k=end==0?0:n-1,at=end==0?0:count-1;
                var ring=Ring(piece,end==0,WallStep);
                if(ring==null)
                {
                    // cut off (a junction, or another kind of road carries on): the open walls show their ends
                    if(!(end==0?piece.capStart:piece.capEnd)||left[at].covered)continue;
                    var ahead=centre[Mathf.Min(k+1,n-1)]-centre[Mathf.Max(k-1,0)];ahead.y=0;ahead=ahead.normalized*(end==0?-1f:1f);
                    foreach(var rim in new[]{left[at],right[at]})
                    {
                        var foot=rim.p+rim.n*piece.verge;float top=WallTop(piece,rim,ground);if(top<.01f)continue;
                        mesh.colour=Plain;mesh.uv=null;
                        mesh.Face(foot,foot+rim.n*WallThickness,foot+rim.n*WallThickness+up*top,foot+up*top,ahead);
                    }
                    continue;
                }
                // a dead end: the walls (and under ground the lining and roof) carried round it
                float away=(end==1)!=piece.run.reversed?1f:-1f;var rims=new List<Rim>();var gone=new float[ring.Count];
                for(int i=1;i<ring.Count;i++)gone[i]=gone[i-1]+Vector2.Distance(Alignment3D.XZ(ring[i-1].point+ring[i-1].normal*piece.verge),Alignment3D.XZ(ring[i].point+ring[i].normal*piece.verge));
                for(int i=0;i<ring.Count;i++)
                    rims.Add(new Rim {p=ring[i].point,n=ring[i].normal,mid=centre[k],run=Run(k)+away*Mathf.Min(gone[i],gone[ring.Count-1]-gone[i]),covered=left[at].covered});
                Bore(piece,mesh,openings,rims,ground);
            }
            mesh.colour=Plain;mesh.uv=null;
        }

        /// <summary>How far an open approach's wall stands above the pavement's edge: up to the ground it holds back
        /// and a coping above that, growing out of the ground where the cut begins; never below the paved strip.</summary>
        static float WallTop(StructurePiece piece,Rim rim,Func<Vector3,float> ground)
        {
            float depth=ground(rim.p+rim.n*(piece.verge+WallThickness))-rim.p.y;
            return Mathf.Max(piece.stripRise,Mathf.Max(0f,depth)+Coping*Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(0f,.8f,depth)));
        }

        /// <summary>One side of a tunnel along its rim. Under ground: the lining and its half of the roof. In the open:
        /// a retaining wall with a coping that follows the ground, and the terrain opened from wall to wall.</summary>
        static void Bore(StructurePiece piece,LandscapeMesh mesh,List<BuiltSurfaceMap.Triangle> openings,List<Rim> rims,Func<Vector3,float> ground)
        {
            var up=Vector3.up;var rise=up*TunnelClearance;
            var tops=new float[rims.Count];var feet=new float[rims.Count];
            for(int i=0;i<rims.Count;i++)
            {
                tops[i]=WallTop(piece,rims[i],ground);
                // the outer face reaches into the ground wherever that lies, also below the road on a hillside's low flank
                feet[i]=Mathf.Min(0f,ground(rims[i].p+rims[i].n*(piece.verge+WallThickness))-rims[i].p.y)-.5f;
            }
            for(int i=0;i+1<rims.Count;i++)
            {
                var a=rims[i];var b=rims[i+1];
                var a0=a.p+a.n*piece.verge;var b0=b.p+b.n*piece.verge;
                mesh.colour=Plain;mesh.uv=null;
                Strip(piece,mesh,a.p+a.n*piece.stripFrom,b.p+b.n*piece.stripFrom,a0,b0);
                Vector2 Along(Vector3 p)
                {
                    float d0=Mathf.Abs((p.x-a0.x)*a.n.z-(p.z-a0.z)*a.n.x),d1=Mathf.Abs((p.x-b0.x)*b.n.z-(p.z-b0.z)*b.n.x);
                    return new Vector2(d0<=d1?a.run:b.run,0);
                }
                if(a.covered&&b.covered)
                {
                    mesh.colour=Panel;mesh.uv=Along;mesh.TwoSided(a0,b0,b0+rise,a0+rise);
                    mesh.colour=Plain;mesh.uv=null;
                    var ma=a.mid+rise;var mb=b.mid+rise;
                    if((ma-mb).sqrMagnitude<1e-6f){mesh.Triangle(a0+rise,b0+rise,ma);mesh.Triangle(a0+rise,ma,b0+rise);}
                    else mesh.TwoSided(a0+rise,b0+rise,mb,ma);
                    continue;
                }
                var a1=a0+a.n*WallThickness;var b1=b0+b.n*WallThickness;
                openings.Add(new BuiltSurfaceMap.Triangle(a1,b1,b.mid));openings.Add(new BuiltSurfaceMap.Triangle(a1,b.mid,a.mid));
                var ta=up*tops[i];var tb=up*tops[i+1];
                mesh.colour=Panel;mesh.uv=Along;
                if(Mathf.Max(tops[i],tops[i+1])>.01f)mesh.Face(a0,b0,b0+tb,a0+ta,-a.n);
                mesh.Face(a1+ta,b1+tb,b1+up*feet[i+1],a1+up*feet[i],a.n);
                mesh.colour=Plain;mesh.uv=null;
                mesh.Face(a0+ta,b0+tb,b1+tb,a1+ta,up);
            }
        }

        /// <summary>Where a tunnel goes under ground: a headwall from the roof up to the ground it holds back, its
        /// coping following that ground from one approach wall to the other, and a lintel over the opening.</summary>
        static void Portal(StructurePiece piece,LandscapeMesh mesh,Rim left,Rim right,Vector3 open,Func<Vector3,float> ground)
        {
            var up=Vector3.up;var toward=open-left.mid;toward.y=0;if(toward.sqrMagnitude<1e-6f)return;toward.Normalize();
            var l0=left.p+left.n*piece.verge;var r0=right.p+right.n*piece.verge;var l1=l0+left.n*WallThickness;var r1=r0+right.n*WallThickness;
            int parts=Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(l1,r1)/WallStep));
            Vector3 Foot(int j)=>Vector3.Lerp(l1,r1,j/(float)parts)+up*TunnelClearance;
            Vector3 Head(int j){var p=Foot(j);p.y=Mathf.Max(p.y+LintelHeight,ground(p)+Coping);return p;}
            mesh.colour=Plain;mesh.uv=null;
            for(int j=0;j<parts;j++)
            {
                mesh.Face(Foot(j),Foot(j+1),Head(j+1),Head(j),toward);
                mesh.Face(Head(j),Head(j+1),Head(j+1)-toward*WallThickness,Head(j)-toward*WallThickness,up);
            }
            var proud=toward*LintelProud;var a=l0+up*TunnelClearance;var b=r0+up*TunnelClearance;var high=up*LintelHeight;
            mesh.Face(a+proud,b+proud,b+proud+high,a+proud+high,toward);
            mesh.Face(a,b,b+proud,a+proud,Vector3.down);
            mesh.Face(a+high,b+high,b+proud+high,a+proud+high,up);
        }
    }
}
