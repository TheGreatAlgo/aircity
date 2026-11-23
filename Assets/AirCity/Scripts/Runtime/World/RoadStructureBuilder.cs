using System;
using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Where a piece lies along its run: the unbroken stretch of one bridge through bends and
    /// straight-through nodes. Piers are spaced along the run, so they stay even however it was drawn.</summary>
    internal struct RunSpan
    {
        public float offset,total;
        public int firstEdge;
        public int startNode,endNode;
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
        public TunnelPortalStyle portalStyle;
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
        public RoadBridgeSpan bridge;
        public bool retainsBank;
        public TunnelPortalStyle? tunnelAtStart, tunnelAtEnd;
        public void Add(Vector3 l,Vector3 r){left.Add(l);right.Add(r);}
    }

    /// <summary>Shared bridge decks and tunnel linings. A tunnel clears only the space between its floor and roof.</summary>
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
        public const float TunnelRoofThickness = .6f;
        public const float TunnelExcavationHeight = TunnelClearance + TunnelRoofThickness;
        public const float PavementClipHeight = .4f;
        internal const float ShortStructureGap = 15f;
        internal const float TunnelFloorThickness = .55f;
        const float PortalArchRise = 1.25f;
        const float WallEmbedment = .5f, ShoulderOverlap = .4f;
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
                var runs=item.bend?bendRuns:edgeRuns;var span=runs[item.id];span.firstEdge=start.edge;
                span.startNode=start.node;span.endNode=last;
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
        public const float SubmergedFootDepth = 1.25f;

        internal static float SupportFoot(Vector2 point, float ground, Func<Vector2,float?> water)
        {
            float? surface = water?.Invoke(point);
            return surface.HasValue ? Mathf.Max(ground-.6f,surface.Value-SubmergedFootDepth) : ground-.6f;
        }

        internal static int UphillSide(Vector3 left, Vector3 right, Func<Vector2,float> ground,
            Func<Vector2,float?> water)
        {
            var l = Alignment3D.XZ(left);
            var r = Alignment3D.XZ(right);
            if (water != null && (water(l).HasValue || water(r).HasValue)) return 0;
            float leftDepth = ground(l) - left.y;
            float rightDepth = ground(r) - right.y;
            float relief = Mathf.Max(3f, Vector2.Distance(l, r) * .225f);
            if (Mathf.Max(leftDepth, rightDepth) < 1.25f ||
                Mathf.Min(leftDepth, rightDepth) > .8f || Mathf.Abs(leftDepth - rightDepth) < relief) return 0;
            return leftDepth > rightDepth ? -1 : 1;
        }

        internal static bool RetainsBank(Alignment3D path, float halfWidth, Func<Vector2,float> ground, Func<Vector2,float?> water)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(path.Length / 4f));
            for (int i = 0; i <= count; i++)
            {
                float station = path.Length * i / count;
                if (UphillSide(path.Offset(station, -halfWidth), path.Offset(station, halfWidth), ground, water) != 0) return true;
            }
            return false;
        }

        internal static bool ShortDryApproach(Alignment3D path, float halfWidth, Func<Vector2,float?> water)
        {
            if (path.Length > ShortStructureGap) return false;
            if (water == null) return true;
            int spans = Mathf.Max(1, Mathf.CeilToInt(path.Length / WallStep));
            for (int i = 0; i <= spans; i++)
            {
                float station = path.Length * i / spans;
                if (!DryCrossSection(path.Offset(station, -halfWidth), path.Offset(station, halfWidth), water)) return false;
            }
            return true;
        }

        static bool DryCrossSection(Vector3 left, Vector3 right, Func<Vector2,float?> water) => water == null ||
            (!water(Alignment3D.XZ(left)).HasValue && !water(Alignment3D.XZ(right)).HasValue &&
                !water(Alignment3D.XZ((left + right) * .5f)).HasValue);

        // Span values share the same boundaries as the concrete and the terrain cut. Bridge short
        // interruptions only when the structure on either side agrees and every span is eligible.
        static void FillShortGaps(int[] spans, Func<int,float> station, Func<int,int,bool> eligible = null)
        {
            for (int start = 1; start < spans.Length - 1; start++)
            {
                if (spans[start] != 0 || spans[start - 1] == 0) continue;
                int kind = spans[start - 1];
                int end = start;
                while (end < spans.Length && spans[end] == 0 && (eligible == null || eligible(end, kind))) end++;
                if (end == start || end == spans.Length || spans[end] != kind ||
                    Mathf.Abs(station(end) - station(start)) > ShortStructureGap + .01f) continue;
                Array.Fill(spans, kind, start, end - start);
            }
        }

        static void ClearSpan(List<BuiltSurfaceMap.Triangle> openings, Vector3 leftA, Vector3 leftB, Vector3 rightB, Vector3 rightA,
            float height, float depth, bool buried = false)
        {
            openings.Add(new BuiltSurfaceMap.Triangle(leftA, leftB, rightB, excavationHeight: height, buried: buried, excavationDepth: depth));
            if (rightA != rightB)
                openings.Add(new BuiltSurfaceMap.Triangle(leftA, rightB, rightA, excavationHeight: height, buried: buried, excavationDepth: depth));
        }

        internal static bool DeckNeedsLining(Vector3 left, Vector3 right, float verge, Func<Vector2,float> ground, Func<Vector2,float?> water)
        {
            var axis = Alignment3D.XZ(right - left).normalized;
            var reach = new Vector3(axis.x, 0f, axis.y) * verge;
            left -= reach;
            right += reach;
            return DryCrossSection(left, right, water) &&
                TunnelCoverAt(left, right, point => ground(Alignment3D.XZ(point)), PavementClipHeight).roofed;
        }

        // A heightfield cut has no vertical face. Close it where the formation falls away
        // below the shoulder and where the uncut bank rises above it at a portal.
        internal static void SealShoulder(LandscapeMesh mesh, Vector3 a, Vector3 b, Vector3 outward, Func<Vector2,float> ground,
            float shoulderRise = 0f)
        {
            float Ground(Vector3 p) => ground(Alignment3D.XZ(p));
            var lip = Vector3.up * (MeshData.RoadEdgeDepth - .01f);
            (Vector3 foot, Vector3 top, Vector3 back, bool exposed) Profile(Vector3 edge)
            {
                float terrain = Ground(edge);
                var back = edge + outward * WallThickness;
                float bank = Ground(back);
                var foot = edge;
                foot.y = Mathf.Min(edge.y - lip.y, terrain - WallEmbedment);
                // Use the tunnel wall's pavement datum and coping profile at the common rim.
                float pavement = edge.y - shoulderRise;
                var top = edge;
                if (Mathf.Max(terrain, bank) > edge.y)
                    top.y = Mathf.Max(edge.y, pavement + WallRise(Mathf.Max(terrain, bank) - pavement));
                back.y = Mathf.Min(bank, top.y) - .02f;
                return (foot, top, back, terrain < edge.y - lip.y);
            }
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / WallStep));
            mesh.colour = Plain;
            mesh.uv = null;
            var edgeA = a;
            var start = Profile(a);
            for (int i = 1; i <= count; i++)
            {
                var edgeB = Vector3.Lerp(a, b, i / (float)count);
                var end = Profile(edgeB);
                if (start.exposed || end.exposed)
                    mesh.Face(edgeA - lip, edgeB - lip, end.foot, start.foot, outward);
                if (start.top.y > edgeA.y + .001f || end.top.y > edgeB.y + .001f)
                {
                    var outerA = start.top + outward * WallThickness;
                    var outerB = end.top + outward * WallThickness;
                    mesh.Face(edgeA, edgeB, end.top, start.top, -outward);
                    mesh.Face(start.top, end.top, outerB, outerA, Vector3.up);
                    mesh.Face(outerA, outerB, end.back, start.back, outward);
                }
                edgeA = edgeB;
                start = end;
            }
        }

        public static void Build(StructurePiece piece,Func<Vector2,float> ground,LandscapeMesh mesh,List<BuiltSurfaceMap.Triangle> openings,
            Func<Vector3,float,bool> support=null,List<Vector3> piers=null,Func<Vector2,float?> water=null)
        {
            int n=piece.left.Count;if(n<2 || piece.structure == RoadStructure.Ground && !piece.retainsBank)return;
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
            int[] lining = null;
            if(piece.structure==RoadStructure.Elevated)
            {
                lining = DeckLinings(piece, along, ground, water);
                Deck(piece,mesh,openings,n,centre,half,across,along,At,Forward,Ground,lining);
                LineDeck(piece, mesh, openings, across, along, Ground, lining);
                // the deck leaves the mesh on its last detail (rail steel): everything after it is plain concrete again
                mesh.colour=Plain;mesh.uv=null;
                if (piece.bridge.suspension != null)
                    piece.bridge.suspension.Build(piece, mesh, centre, across, along, half, ground, water);
                else if (piece.bridge.arch != null)
                    piece.bridge.arch.Build(piece, mesh, centre, across, along, half, ground, water);
                else
                    Piers(piece,mesh,n,centre,across,along,half,Ground,support,piers,water);
            }
            else if (piece.structure == RoadStructure.Tunnel) Tunnel(piece,mesh,openings,n,across,along,Ground);
            if (piece.retainsBank) Cliffside(piece, mesh, openings, across, along, ground, water, lining);
            mesh.colour=Plain;mesh.uv=null;
        }

        // An open retaining wall becomes a roofed gallery where the rock reaches above the
        // lane clearance. Use the same shoulder cross-sections for the wall, roof and cut.
        static void Cliffside(StructurePiece piece, LandscapeMesh mesh, List<BuiltSurfaceMap.Triangle> openings,
            Vector3[] across, float[] along, Func<Vector2,float> ground, Func<Vector2,float?> water, int[] lining)
        {
            float Ground(Vector3 point) => ground(Alignment3D.XZ(point));
            var up = Vector3.up;
            var clearance = up * TunnelClearance;
            var top = up * TunnelExcavationHeight;
            var stations = new List<float>(piece.Stations(along));
            var axes = new List<Vector3>(across);
            var left = new List<Vector3>(piece.left);
            var right = new List<Vector3>(piece.right);
            ExtendToDeadEnd(true, piece.endStart, piece.trimStart);
            int vergeStart = left.Count - across.Length;
            ExtendToDeadEnd(false, piece.endEnd, piece.trimEnd);
            int vergeEnd = vergeStart + across.Length - 1;
            float shoulder = piece.verge;
            var sides = new int[left.Count - 1];
            var roofs = new int[sides.Length];
            var dry = new bool[sides.Length];
            for (int i = 0; i < left.Count; i++)
            {
                left[i] -= axes[i] * shoulder;
                right[i] += axes[i] * shoulder;
            }
            for (int i = 0; i < sides.Length; i++)
            {
                if (lining != null && i >= vergeStart && i < vergeEnd && lining[i - vergeStart] != 0) continue;
                var l = Vector3.Lerp(left[i], left[i + 1], .5f);
                var r = Vector3.Lerp(right[i], right[i + 1], .5f);
                dry[i] = DryCrossSection(left[i], right[i], water) && DryCrossSection(l, r, water) &&
                    DryCrossSection(left[i + 1], right[i + 1], water);
                if (!dry[i]) continue;
                sides[i] = UphillSide(left[i], right[i], ground, null);
                if (sides[i] == 0) sides[i] = UphillSide(l, r, ground, null);
                if (sides[i] == 0) sides[i] = UphillSide(left[i + 1], right[i + 1], ground, null);
            }
            FillShortGaps(sides, i => stations[i], (i, _) => dry[i]);
            for (int i = 0; i < sides.Length; i++)
            {
                int side = sides[i];
                if (side == 0) continue;
                int sections = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(stations[i + 1] - stations[i]) / WallStep));
                for (int j = 0; j <= sections && roofs[i] == 0; j++)
                    if (NeedsRoof(i, j / (float)sections, side)) roofs[i] = side;
            }
            FillShortGaps(roofs, i => stations[i], (i, side) => sides[i] == side);

            for (int i = 0; i < sides.Length; i++)
            {
                var l0 = left[i];
                var r0 = right[i];
                var l1 = left[i + 1];
                var r1 = right[i + 1];
                int side = sides[i];
                if (piece.structure == RoadStructure.Ground && i >= vergeStart && i < vergeEnd)
                {
                    GroundVerge(l0, l1, -axes[i], -axes[i + 1], side >= 0);
                    GroundVerge(r0, r1, axes[i], axes[i + 1], side <= 0);
                }
                if (side == 0) continue;

                var nearA = side < 0 ? l0 : r0;
                var nearB = side < 0 ? l1 : r1;
                var farA = side < 0 ? r0 : l0;
                var farB = side < 0 ? r1 : l1;
                var outwardA = axes[i] * side;
                var outwardB = axes[i + 1] * side;
                var outerA = nearA + outwardA * WallThickness;
                var outerB = nearB + outwardB * WallThickness;
                var cutA = outerA - outwardA * .02f;
                var cutB = outerB - outwardB * .02f;

                float floorDepth = piece.structure == RoadStructure.Elevated ? EdgeDepth : TunnelFloorThickness;
                ClearSpan(openings, cutA, cutB, farB, farA, TunnelExcavationHeight, floorDepth);

                float depthA = Ground(outerA) - outerA.y;
                float depthB = Ground(outerB) - outerB.y;
                bool roofed = roofs[i] == side;
                float minimumCoping = piece.stripRise + EdgeBeamRise;
                var heightA = roofed ? top : up * Mathf.Clamp(depthA + Coping, minimumCoping, TunnelExcavationHeight);
                var heightB = roofed ? top : up * Mathf.Clamp(depthB + Coping, minimumCoping, TunnelExcavationHeight);
                mesh.colour = Plain;
                mesh.uv = null;
                if (piece.structure == RoadStructure.Ground)
                {
                    var floor = up * floorDepth;
                    mesh.Face(outerA - floor, outerB - floor, farB - floor, farA - floor, -up);
                }
                mesh.Face(WallFoot(nearA, Ground), WallFoot(nearB, Ground), nearB + heightB, nearA + heightA, -outwardA);
                mesh.Face(outerA + heightA, outerB + heightB, WallFoot(outerB, Ground), WallFoot(outerA, Ground), outwardA);
                mesh.Face(nearA + heightA, nearB + heightB, outerB + heightB, outerA + heightA, up);
                var heading = (nearB - nearA).normalized;
                mesh.Face(WallFoot(outerA, Ground), WallFoot(nearA, Ground), nearA + heightA, outerA + heightA, -heading);
                mesh.Face(WallFoot(nearB, Ground), WallFoot(outerB, Ground), outerB + heightB, nearB + heightB, heading);
                if (i == 0 && piece.endStart != null) RetainDeadEnd(farA, outerA, -heading, roofed);
                if (i == left.Count - 2 && piece.endEnd != null) RetainDeadEnd(farB, outerB, heading, roofed);
                if (!roofed) continue;

                mesh.Face(nearA + clearance, nearB + clearance, farB + clearance, farA + clearance, -up);
                mesh.Face(outerA + top, outerB + top, farB + top, farA + top, up);
                mesh.Face(farA + clearance, farB + clearance, farB + top, farA + top, -outwardA);
                mesh.Face(nearA + clearance, farA + clearance, farA + top, outerA + top, -heading);
                mesh.Face(farB + clearance, nearB + clearance, outerB + top, farB + top, heading);
                float start = Mathf.Min(stations[i], stations[i + 1]);
                float end = Mathf.Max(stations[i], stations[i + 1]);
                const float spacing = 9f;
                for (float station = (Mathf.Floor(start / spacing) + .5f) * spacing; station < end; station += spacing)
                {
                    if (station < start) continue;
                    float t = Mathf.InverseLerp(stations[i], stations[i + 1], station);
                    var foot = Vector3.Lerp(farA, farB, t) + Vector3.Lerp(outwardA, outwardB, t) * .23f;
                    mesh.Box(foot + clearance * .5f, new Vector3(.46f, TunnelClearance, .46f));
                }
            }

            if (piece.structure == RoadStructure.Ground)
            {
                RoundVerge(piece.endStart);
                RoundVerge(piece.endEnd);
            }

            bool NeedsRoof(int i, float t, int side)
            {
                var l = Vector3.Lerp(left[i], left[i + 1], t);
                var r = Vector3.Lerp(right[i], right[i + 1], t);
                var extension = Vector3.Lerp(axes[i], axes[i + 1], t).normalized * WallThickness;
                return TunnelCoverAt(l - (side < 0 ? extension : Vector3.zero),
                    r + (side > 0 ? extension : Vector3.zero), Ground, TunnelClearance).roofed;
            }

            void GroundVerge(Vector3 a, Vector3 b, Vector3 outA, Vector3 outB, bool edgeFace)
            {
                var aInner = a - outA * (shoulder - piece.stripFrom);
                var bInner = b - outB * (shoulder - piece.stripFrom);
                var rise = up * piece.stripRise;
                Strip(piece, mesh, aInner, bInner, a, b);
                if (edgeFace) mesh.Face(WallFoot(a, Ground), WallFoot(b, Ground), b + rise, a + rise, outA);
                ClearSpan(openings, aInner + rise, bInner + rise, b + rise, a + rise, PavementClipHeight, EdgeDepth);
            }

            void RoundVerge(List<(Vector3 point, Vector3 normal)> ring)
            {
                if (ring == null) return;
                mesh.colour = Plain;
                mesh.uv = null;
                for (int i = 0; i + 1 < ring.Count; i++)
                {
                    var (a, outA) = ring[i];
                    var (b, outB) = ring[i + 1];
                    GroundVerge(a + outA * shoulder, b + outB * shoulder, outA, outB, true);
                }
            }

            void ExtendToDeadEnd(bool start, List<(Vector3 point, Vector3 normal)> ring, float trim)
            {
                if (ring == null || trim < .01f) return;
                int at = start ? 0 : left.Count - 1;
                int neighbour = start ? 1 : at - 1;
                var alongRoad = (left[at] + right[at] - left[neighbour] - right[neighbour]) * .5f;
                float distance = Alignment3D.XZ(alongRoad).magnitude;
                if (distance < .01f) return;
                var offset = alongRoad * (trim / distance);
                int index = start ? 0 : left.Count;
                float direction = start != piece.run.reversed ? -1f : 1f;
                left.Insert(index, left[at] + offset);
                right.Insert(index, right[at] + offset);
                axes.Insert(index, axes[at]);
                stations.Insert(index, stations[at] + direction * trim);
            }

            void RetainDeadEnd(Vector3 from, Vector3 to, Vector3 outward, bool roofed)
            {
                float ceiling = roofed ? TunnelClearance : TunnelExcavationHeight;
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / (.5f * WallStep)));
                var a = from;
                float heightA = EndHeight(a);
                for (int j = 1; j <= count; j++)
                {
                    var b = Vector3.Lerp(from, to, j / (float)count);
                    float heightB = EndHeight(b);
                    if (heightA > .01f || heightB > .01f)
                        mesh.Face(WallFoot(a, Ground), WallFoot(b, Ground), b + up * heightB, a + up * heightA, outward);
                    a = b;
                    heightA = heightB;
                }

                // Overlap the sampled rock face across the terrain triangles, leaving the downhill side open.
                float EndHeight(Vector3 point) => Mathf.Clamp(Ground(point) - point.y + .2f, 0f, ceiling);
            }
        }

        // Find where earth enters the bridge clearance, including between the deck's own frames.
        // An existing cliffside gallery handles its bank except where the tunnel lining takes over.
        static int[] DeckLinings(StructurePiece piece, float[] along, Func<Vector2,float> ground, Func<Vector2,float?> water)
        {
            if (piece.retainsBank && !piece.tunnelAtStart.HasValue && !piece.tunnelAtEnd.HasValue) return null;
            var lined = new int[along.Length - 1];
            for (int i = 0; i < lined.Length; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt((along[i + 1] - along[i]) / WallStep));
                for (int j = 0; j <= steps; j++)
                {
                    float t = j / (float)steps;
                    if (!DeckNeedsLining(Vector3.Lerp(piece.left[i], piece.left[i + 1], t),
                        Vector3.Lerp(piece.right[i], piece.right[i + 1], t), piece.verge, ground, water)) continue;
                    lined[i] = 1;
                    break;
                }
            }
            FillShortGaps(lined, i => along[i], (i, _) =>
                DryCrossSection((piece.left[i] + piece.left[i + 1]) * .5f, (piece.right[i] + piece.right[i + 1]) * .5f, water));
            if (piece.retainsBank)
            {
                int start = 0, end = lined.Length;
                if (piece.tunnelAtStart.HasValue)
                    while (start < end && lined[start] != 0) start++;
                if (piece.tunnelAtEnd.HasValue)
                    while (end > start && lined[end - 1] != 0) end--;
                Array.Clear(lined, start, end - start);
            }
            return lined;
        }

        static void LineDeck(StructurePiece deck, LandscapeMesh mesh, List<BuiltSurfaceMap.Triangle> openings,
            Vector3[] across, float[] along, Func<Vector3,float> ground, int[] lined)
        {
            if (lined == null) return;
            var stations = deck.Stations(along);
            for (int first = 0; first < lined.Length; first++)
            {
                if (lined[first] == 0) continue;
                int last = first + 1;
                while (last < lined.Length && lined[last] != 0) last++;
                bool joinsStart = first == 0 && deck.tunnelAtStart.HasValue;
                bool joinsEnd = last == lined.Length && deck.tunnelAtEnd.HasValue;
                float trimStart = first == 0 ? deck.trimStart : 0f;
                float trimEnd = last == lined.Length ? deck.trimEnd : 0f;
                var approach = new StructurePiece
                {
                    structure = RoadStructure.Tunnel,
                    portalStyle = (joinsStart ? deck.tunnelAtStart : joinsEnd ? deck.tunnelAtEnd : deck.tunnelAtStart ?? deck.tunnelAtEnd)
                        ?? deck.portalStyle,
                    verge = deck.verge, stripFrom = deck.stripFrom, stripRise = deck.stripRise,
                    length = Mathf.Abs(stations[last] - stations[first]) + trimStart + trimEnd,
                    run = new RunSpan { offset = Mathf.Min(stations[first], stations[last]) - (deck.run.reversed ? trimEnd : trimStart),
                        reversed = deck.run.reversed },
                    capStart = !joinsStart, capEnd = !joinsEnd,
                    abutStart = first == 0 && deck.abutStart, abutEnd = last == lined.Length && deck.abutEnd,
                    endStart = first == 0 ? deck.endStart : null, endEnd = last == lined.Length ? deck.endEnd : null,
                    trimStart = trimStart, trimEnd = trimEnd
                };
                int count = last - first + 1;
                var axis = new Vector3[count];
                var distance = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int at = first + i;
                    approach.Add(deck.left[at], deck.right[at]);
                    axis[i] = across[at];
                    distance[i] = along[at] - along[first];
                }
                Tunnel(approach, mesh, openings, count, axis, distance, ground);
                first = last - 1;
            }
        }

        static void Deck(StructurePiece piece,LandscapeMesh mesh,List<BuiltSurfaceMap.Triangle> openings,int n,Vector3[] centre,float[] half,Vector3[] across,
            float[] along,Func<int,float,Vector3> at,Func<int,Vector3> forward,Func<Vector3,float> ground,int[] lining)
        {
            var up=Vector3.up;float lift=piece.stripRise,barrier=lift+BarrierHeight,top=lift+ParapetHeight;
            // metres along the run at each cross-section: panel joints and rail posts carry on from piece to piece
            var stations=piece.Stations(along);
            float Run(int k)=>stations[k];
            for(int k=0;k+1<n;k++)
            {
                float w0=half[k]+piece.verge,w1=half[k+1]+piece.verge,g0=w0*GirderShare,g1=w1*GirderShare;
                bool lined = lining != null && lining[k] != 0;
                if (!lined)
                {
                    var c0 = at(k, 0f);
                    var c1 = at(k + 1, 0f);
                    bool aboveGround = Mathf.Max(ground(c0) - c0.y, ground(c1) - c1.y) + piece.surface <= -.5f;
                    ClearSpan(openings, at(k,-w0), at(k+1,-w1), at(k+1,w1), at(k,w0), TunnelClearance, EdgeDepth, buried: aboveGround);
                }
                // the underside: a flat girder soffit, with wings tapering up to the thin edges
                mesh.colour=Plain;mesh.uv=null;
                mesh.Face(at(k,-g0)+Vector3.down*DeckDepth,at(k+1,-g1)+Vector3.down*DeckDepth,at(k+1,g1)+Vector3.down*DeckDepth,at(k,g0)+Vector3.down*DeckDepth,Vector3.down);
                for(int side=-1;side<=1;side+=2)
                    DeckEdge(piece,mesh,at(k,w0*side),at(k+1,w1*side),across[k]*side,across[k+1]*side,Run(k),Run(k+1),
                        at(k,g0*side)+Vector3.down*DeckDepth,at(k+1,g1*side)+Vector3.down*DeckDepth,
                        at(k,half[k]*side)+across[k]*side*piece.stripFrom,at(k+1,half[k+1]*side)+across[k+1]*side*piece.stripFrom, lined);
            }
            // a dead end: the same edge carried round the pavement's rounded end, the soffit filled in under it
            for(int end=0;end<2;end++)
            {
                var ring=Ring(piece,end==0,WallStep*4f);if(ring==null)continue;
                int k=end==0?0:n-1;float away=(end==1)!=piece.run.reversed?1f:-1f;
                bool lined = lining != null && lining[end == 0 ? 0 : lining.Length - 1] != 0;
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
                    if (!lined)
                        ClearSpan(openings, outer[i], outer[i + 1], centre[k], centre[k], TunnelClearance, EdgeDepth, buried: true);
                    DeckEdge(piece,mesh,outer[i],outer[i+1],ring[i].normal,ring[i+1].normal,RunAt(i),RunAt(i+1),Girder(i),Girder(i+1),
                        ring[i].point+ring[i].normal*piece.stripFrom,ring[i+1].point+ring[i+1].normal*piece.stripFrom, lined);
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
                if (lining == null || lining[end == 0 ? 0 : lining.Length - 1] == 0)
                    CapDeckRails(k, outward);
                mesh.colour=Plain;
                if(!(end==0?piece.abutStart:piece.abutEnd))continue;
                // an abutment wall under the deck's end, down into the ground the road carries on along
                float soffit=at(k,0).y-DeckDepth,floor=Mathf.Min(ground(at(k,-w)),ground(at(k,0)),ground(at(k,w)))-.5f;
                if(soffit-floor<.6f)continue;
                var back=-outward*1f;var l=at(k,-w);var r=at(k,w);l.y=r.y=soffit;var fall=Vector3.down*(soffit-floor);
                mesh.Face(l,r,r+fall,l+fall,outward);mesh.Face(l+back,r+back,r+back+fall,l+back+fall,-outward);
                mesh.Face(l,l+back,l+back+fall,l+fall,-across[k]);mesh.Face(r,r+back,r+back+fall,r+fall,across[k]);
            }
            if (lining != null)
                for (int k = 1; k < lining.Length; k++)
                    if (lining[k - 1] != lining[k]) CapDeckRails(k, forward(k) * (lining[k] != 0 ? 1f : -1f));

            void CapDeckRails(int k, Vector3 outward)
            {
                float w=half[k]+piece.verge;
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
            }
        }

        /// <summary>One length of a deck's edge between two frames (`o`: the deck's outer line at the pavement
        /// edge's height, `outward`: pointing off the deck, `run`: metres along the bridge): the soffit's wing up from
        /// the girder, the paved strip, the edge beam, the barrier, and the rail on its posts.</summary>
        static void DeckEdge(StructurePiece piece,LandscapeMesh mesh,Vector3 o0,Vector3 o1,Vector3 out0,Vector3 out1,float run0,float run1,
            Vector3 girder0,Vector3 girder1,Vector3 strip0,Vector3 strip1,bool lined=false)
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
            if (lined)
            {
                Band(0,-EdgeDepth,0,0,out0);
                return;
            }
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
            Func<Vector3,float> ground,Func<Vector3,float,bool> support,List<Vector3> placed,Func<Vector2,float?> water)
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
                bool restsOnLand=false, hasWater=false;
                foreach(float offset in offsets)
                {
                    var p=c+x*offset;float g=ground(p);
                    var surface=water?.Invoke(Alignment3D.XZ(p));
                    hasWater|=surface.HasValue;
                    restsOnLand|=!surface.HasValue&&soffit-g<MinPierClearance;
                    float floor=surface.HasValue?Mathf.Max(g-.6f,surface.Value-SubmergedFootDepth):g-.6f;
                    low=Mathf.Min(low,floor);high=Mathf.Max(high,floor);
                }
                if(restsOnLand||soffit-high<.3f)return true;
                float diameter=PierDiameter(soffit-low-(hasWater?0f:.6f)),radius=diameter*.5f;
                if(support!=null)foreach(float offset in offsets)if(!support(c+x*offset,radius+.6f))return false;
                float cap=Mathf.Min(PierCap,hasWater?(soffit-high)*.6f:soffit-high-1.1f),reach=Mathf.Abs(offsets[0]);
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
                    var p=c+x*offset;float floor=SupportFoot(Alignment3D.XZ(p),ground(p),water);
                    for(int i=0;i<4;i++)
                    {
                        var edge=p+Quaternion.Euler(0,i*90f,0)*Vector3.right*radius;
                        floor=Mathf.Min(floor,SupportFoot(Alignment3D.XZ(edge),ground(edge),water));
                    }
                    var surface=water?.Invoke(Alignment3D.XZ(p));
                    if(surface.HasValue)floor=Mathf.Max(floor,surface.Value-SubmergedFootDepth);
                    mesh.Tube(new Vector3(p.x,floor,p.z),foot.y-floor,radius,16);
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

        internal static (bool roofed, bool buried) TunnelCoverAt(Vector3 left, Vector3 right, Func<Vector3,float> ground,
            float roofThreshold = TunnelExcavationHeight - .15f)
        {
            int parts = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(left, right) / (2f * WallStep)));
            float lowest = float.PositiveInfinity;
            float highest = float.NegativeInfinity;
            for (int i = 0; i <= parts; i++)
            {
                var point = Vector3.Lerp(left, right, i / (float)parts);
                float depth = ground(point) - point.y;
                lowest = Mathf.Min(lowest, depth);
                highest = Mathf.Max(highest, depth);
            }
            // Start the lining before either shoulder overtops the retaining walls.
            return (highest >= roofThreshold, lowest >= TunnelExcavationHeight + .4f);
        }

        struct Rim
        {
            public Vector3 p, n, mid;
            public float run;
            public bool roofed, buried;
        }

        static void Tunnel(StructurePiece piece, LandscapeMesh mesh, List<BuiltSurfaceMap.Triangle> openings,
            int n, Vector3[] across, float[] along, Func<Vector3,float> ground)
        {
            var stations = piece.Stations(along);
            var left = new List<Rim>();
            var right = new List<Rim>();
            var cover = new (bool roofed, bool buried)[n];
            for (int k = 0; k < n; k++) cover[k] = Cover(piece.left[k], piece.right[k], across[k]);

            for (int k = 0; k < n; k++)
            {
                int next = Mathf.Min(k + 1, n - 1);
                int parts = cover[k].buried && cover[next].buried ? 1 :
                    Mathf.Max(1, Mathf.CeilToInt((along[next] - along[k]) / WallStep));
                for (int j = 0; j < parts; j++)
                {
                    float t = j / (float)parts;
                    var l = Vector3.Lerp(piece.left[k], piece.left[next], t);
                    var r = Vector3.Lerp(piece.right[k], piece.right[next], t);
                    var axis = Vector3.Lerp(across[k], across[next], t).normalized;
                    var (roofed, buried) = j == 0 ? cover[k] : Cover(l, r, axis);
                    var mid = (l + r) * .5f;
                    float run = Mathf.Lerp(stations[k], stations[next], t);
                    left.Add(new Rim { p = l, n = -axis, mid = mid, run = run, roofed = roofed, buried = buried });
                    right.Add(new Rim { p = r, n = axis, mid = mid, run = run, roofed = roofed, buried = buried });
                }
            }

            (bool roofed, bool buried) Cover(Vector3 l, Vector3 r, Vector3 axis)
            {
                float reach = piece.verge + WallThickness;
                return TunnelCoverAt(l - axis * reach, r + axis * reach, ground);
            }

            int count = left.Count;
            Bore(piece, mesh, openings, left, ground);
            Bore(piece, mesh, openings, right, ground);
            bool Roofed(int i) => left[i].roofed || left[i + 1].roofed;
            for (int i = 0; i + 1 < count; i++)
            {
                bool roofed = Roofed(i);
                if (i > 0 && roofed != Roofed(i - 1))
                    Portal(piece, mesh, left[i], right[i], left[roofed ? i - 1 : i + 1].mid - left[i].mid);
                if (!roofed) continue;

                // Lamp spacing remains continuous through sections and bends.
                float low = Mathf.Min(left[i].run, left[i + 1].run);
                float high = Mathf.Max(left[i].run, left[i + 1].run);
                for (int lamp = Mathf.CeilToInt(low / LampSpacing - .5f); (lamp + .5f) * LampSpacing < high; lamp++)
                {
                    float t = Mathf.InverseLerp(left[i].run, left[i + 1].run, (lamp + .5f) * LampSpacing);
                    var l = Vector3.Lerp(left[i].p, left[i + 1].p, t);
                    var r = Vector3.Lerp(right[i].p, right[i + 1].p, t);
                    var ahead = left[i + 1].mid - left[i].mid;
                    ahead.y = 0f;
                    if (ahead.sqrMagnitude < 1e-6f) continue;
                    mesh.colour = Lamp;
                    mesh.uv = null;
                    var rotation = Quaternion.LookRotation(ahead.normalized);
                    foreach (float share in new[] { .25f, .75f })
                        mesh.OrientedBox(Vector3.Lerp(l, r, share) + Vector3.up * (TunnelClearance - .06f), new Vector3(.35f,.1f,1.6f), rotation);
                }
            }

            for (int end = 0; end < 2; end++)
            {
                bool start = end == 0;
                int at = start ? 0 : count - 1;
                bool roofed = Roofed(start ? 0 : count - 2);
                var outward = left[at].mid - left[start ? 1 : count - 2].mid;
                var ring = Ring(piece, start, WallStep);
                if (ring == null)
                {
                    bool cap = start ? piece.capStart : piece.capEnd;
                    if (roofed)
                    {
                        if (cap) Portal(piece, mesh, left[at], right[at], outward);
                        else CloseLiningEnd(piece, mesh, left[at], right[at], outward);
                    }
                    else if (cap)
                    {
                        bool meetsGround = start ? piece.abutStart : piece.abutEnd;
                        CapApproachWall(piece, mesh, left[at], outward, ground, meetsGround);
                        CapApproachWall(piece, mesh, right[at], outward, ground, meetsGround);
                    }
                    continue;
                }

                float away = (end == 1) != piece.run.reversed ? 1f : -1f;
                var rims = new List<Rim>();
                var gone = new float[ring.Count];
                for (int i = 1; i < ring.Count; i++)
                    gone[i] = gone[i - 1] + Vector2.Distance(
                        Alignment3D.XZ(ring[i - 1].point + ring[i - 1].normal * piece.verge),
                        Alignment3D.XZ(ring[i].point + ring[i].normal * piece.verge));
                for (int i = 0; i < ring.Count; i++)
                    rims.Add(new Rim { p = ring[i].point, n = ring[i].normal, mid = left[at].mid,
                        run = left[at].run + away * Mathf.Min(gone[i], gone[ring.Count - 1] - gone[i]),
                        roofed = roofed, buried = left[at].buried });
                Bore(piece, mesh, openings, rims, ground);
            }
        }

        static float WallTop(StructurePiece piece, Rim rim, Func<Vector3,float> ground)
        {
            var inner = rim.p + rim.n * piece.verge;
            float depth = Mathf.Max(ground(inner), ground(inner + rim.n * WallThickness)) - rim.p.y;
            return Mathf.Max(piece.stripRise, WallRise(depth));
        }

        static float WallRise(float depth) => Mathf.Min(TunnelExcavationHeight,
            Mathf.Max(0f, depth) + Coping * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .8f, depth)));

        static Vector3 WallFoot(Vector3 edge, Func<Vector3,float> ground)
        {
            edge.y = Mathf.Min(edge.y - TunnelFloorThickness, ground(edge) - WallEmbedment);
            return edge;
        }

        static void CapApproachWall(StructurePiece piece, LandscapeMesh mesh, Rim rim, Vector3 outward,
            Func<Vector3,float> ground, bool meetsGround)
        {
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-6f) return;
            outward.Normalize();

            var inner = rim.p + rim.n * piece.verge;
            var outer = inner + rim.n * WallThickness;
            var baseOuter = WallFoot(outer, ground);
            var baseInner = WallFoot(inner, ground);
            var coping = Vector3.up * WallTop(piece, rim, ground);
            mesh.colour = Plain;
            mesh.uv = null;
            mesh.Face(baseInner, baseOuter, outer + coping, inner + coping, outward);
            if (!meetsGround) return;

            // Close the edge below the verge and return the footing beneath the adjoining
            // shoulder. Its edge can be lower and narrower than the tunnel's concrete strip.
            var strip = Vector3.up * piece.stripRise;
            var vergeStart = rim.p + rim.n * piece.stripFrom;
            var floor = Vector3.up * TunnelFloorThickness;
            mesh.Face(rim.mid - floor, vergeStart - floor, vergeStart, rim.mid, outward);
            mesh.Face(WallFoot(vergeStart, ground), baseInner, inner + strip, vergeStart + strip, outward);

            var approach = inner + outward * ShoulderOverlap - rim.n * .04f;
            approach.y = Mathf.Min(ground(approach), approach.y + piece.stripRise - .12f);
            var buriedEdge = inner + strip - Vector3.up * .12f;
            var outerEdge = outer + strip - Vector3.up * .12f;
            mesh.Face(approach, buriedEdge, outerEdge, Vector3.up);
            mesh.Face(baseOuter, WallFoot(approach, ground), approach, outerEdge, rim.n);
        }

        /// <summary>Build one side of the lining and the corresponding half of its excavation.</summary>
        static void Bore(StructurePiece piece, LandscapeMesh mesh, List<BuiltSurfaceMap.Triangle> openings,
            List<Rim> rims, Func<Vector3,float> ground)
        {
            var up = Vector3.up;
            var roof = up * TunnelClearance;
            var slab = up * TunnelExcavationHeight;
            var floor = up * TunnelFloorThickness;

            for (int i = 0; i + 1 < rims.Count; i++)
            {
                var a = rims[i];
                var b = rims[i + 1];
                var innerA = a.p + a.n * piece.verge;
                var innerB = b.p + b.n * piece.verge;
                var outerA = innerA + a.n * WallThickness;
                var outerB = innerB + b.n * WallThickness;
                mesh.colour = Plain;
                mesh.uv = null;
                Strip(piece, mesh, a.p + a.n * piece.stripFrom, b.p + b.n * piece.stripFrom, innerA, innerB);

                // Sample the floor and roof from the same cross-sections as the walls. Inset the
                // excavation edge under the concrete so it stays sealed at grazing view angles.
                var cutA = outerA - a.n * .02f;
                var cutB = outerB - b.n * .02f;
                bool buried = a.buried && b.buried;
                ClearSpan(openings, cutA, cutB, b.mid, a.mid, TunnelExcavationHeight, TunnelFloorThickness, buried);
                HalfSlab(outerA - floor, outerB - floor, a.mid - floor, b.mid - floor, -up);

                Vector2 Along(Vector3 p)
                {
                    float d0 = Mathf.Abs((p.x - innerA.x) * a.n.z - (p.z - innerA.z) * a.n.x);
                    float d1 = Mathf.Abs((p.x - innerB.x) * b.n.z - (p.z - innerB.z) * b.n.x);
                    return new Vector2(d0 <= d1 ? a.run : b.run, 0f);
                }
                mesh.colour = Panel;
                mesh.uv = Along;
                if (a.roofed || b.roofed)
                {
                    mesh.Face(innerA, innerB, innerB + roof, innerA + roof, -a.n);
                    mesh.Face(outerA + slab, outerB + slab, outerB - floor, outerA - floor, a.n);
                    mesh.colour = Plain;
                    mesh.uv = null;
                    HalfSlab(innerA + roof, innerB + roof, a.mid + roof, b.mid + roof, -up);
                    HalfSlab(outerA + slab, outerB + slab, a.mid + slab, b.mid + slab, up);
                    continue;
                }

                var topA = up * WallTop(piece, a, ground);
                var topB = up * WallTop(piece, b, ground);
                var footA = WallFoot(outerA, ground);
                var footB = WallFoot(outerB, ground);
                mesh.Face(innerA - floor, innerB - floor, innerB + topB, innerA + topA, -a.n);
                mesh.Face(outerA + topA, outerB + topB, footB, footA, a.n);
                mesh.colour = Plain;
                mesh.uv = null;
                mesh.Face(innerA + topA, innerB + topB, outerB + topB, outerA + topA, up);
            }

            void HalfSlab(Vector3 a, Vector3 b, Vector3 midA, Vector3 midB, Vector3 normal)
            {
                if (midA == midB)
                {
                    mesh.Face(a, b, midA, normal);
                }
                else mesh.Face(a, b, midB, midA, normal);
            }
        }

        // A roof can end exactly at a section split. Seal its thickness even if the next piece
        // starts with an open ramp. At a buried join, this face is inside the continuous lining.
        static void CloseLiningEnd(StructurePiece piece, LandscapeMesh mesh, Rim left, Rim right, Vector3 outward)
        {
            float reach = piece.verge + WallThickness;
            var l = left.p + left.n * reach;
            var r = right.p + right.n * reach;
            var roof = Vector3.up * TunnelClearance;
            var top = Vector3.up * TunnelExcavationHeight;
            mesh.colour = Plain;
            mesh.uv = null;
            mesh.Face(l + roof, r + roof, r + top, l + top, outward);
        }

        /// <summary>The fixed frame projects just beyond the end of the roofed lining.</summary>
        static void Portal(StructurePiece piece, LandscapeMesh mesh, Rim left, Rim right, Vector3 toward)
        {
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-6f) return;
            toward.Normalize();
            var up = Vector3.up;
            var front = toward * LintelProud;
            var l = left.p + left.n * piece.verge;
            var r = right.p + right.n * piece.verge;
            var outerL = l + left.n * WallThickness;
            var outerR = r + right.n * WallThickness;
            int parts = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(outerL, outerR) / WallStep));
            float arch = piece.portalStyle == TunnelPortalStyle.Arched ? PortalArchRise : 0f;

            Vector3 Roof(Vector3 point) => point + up * TunnelClearance;
            mesh.colour = Plain;
            mesh.uv = null;
            mesh.Face(outerL, outerR, outerR - up * TunnelFloorThickness, outerL - up * TunnelFloorThickness, toward);
            var crown = up * LintelHeight;
            for (int i = 0; i < parts; i++)
            {
                float t0 = i / (float)parts, t1 = (i + 1f) / parts;
                var a = Roof(Vector3.Lerp(l, r, t0));
                var b = Roof(Vector3.Lerp(l, r, t1));
                var innerA = a + front + up * (arch * Mathf.Sin(Mathf.PI * t0));
                var innerB = b + front + up * (arch * Mathf.Sin(Mathf.PI * t1));
                mesh.Face(innerA, innerB, innerB + crown, innerA + crown, toward);
                mesh.Face(a, b, innerB, innerA, Vector3.down);
                mesh.Face(innerA + crown, innerB + crown, b + crown, a + crown, up);
            }

            foreach (var (inner, outer) in new[] { (l, outerL), (r, outerR) })
            {
                var top = up * (TunnelClearance + LintelHeight);
                mesh.Face(inner + front, outer + front, outer + front + top, inner + front + top, toward);
                mesh.Face(inner, inner + front, inner + front + top, inner + top, (r + l) * .5f - inner);
                mesh.Face(inner + top, inner + front + top, outer + front + top, outer + top, up);
            }
        }
    }
}
