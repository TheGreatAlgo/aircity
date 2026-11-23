using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AirCity.World;

namespace AirCity.Airfield.Building
{
    /// <summary>
    /// Taxiway geometry, paint and lights from the network graph:
    ///  - a node joining two edges of the same kind becomes a curve whose centreline radius suits the width's
    ///    aircraft (RunwayRules.TaxiwayTurnRadius), shortened if the edges are short;
    ///  - any other node becomes a junction (JunctionShape, shared with public roads): every edge cut back clear of
    ///    the others, the corners between neighbouring edges rounded with fillets, narrow edges flared into wide
    ///    ones, for any angle. A node on a runway treats the runway as two wide edges, so taxiways flare into it
    ///    (the part under the runway is hidden by the runway, which sits higher);
    ///  - paint: yellow centrelines with curved lead-ins through junctions, holding-position markings where a
    ///    taxiway meets a runway;
    ///  - lit edges: blue edge lights along straights, curves and fillets; red stop bars at the holding
    ///    positions of runways with precision lighting;
    ///  - roads: tighter corners, white edge lines and centre dashes (round bends too), a give-way marking (double
    ///    dashed line and triangle) before every taxiway they meet, a stop line before runways, never lights; their
    ///    pavement lies under paved aprons (AirfieldHeights.Road), so across an apron only the paint shows. Where
    ///    only roads meet, the edge lines follow the kerbs round the corners, a road running straight through (the
    ///    only one: MainRoad) keeps its centre dashes across, and every other road gives way;
    ///  - verges (BuildVerges): mown grass bands either side of paved taxiways, stopped short of junctions so
    ///    neighbouring bands never overlap;
    ///  - where stands or de-icing bays open onto a side (frontage), no verge and no edge lights.
    /// Surface submeshes are indexed by PavementSurface.
    /// </summary>
    sealed class TaxiwayBuilder
    {
        const float CentrelineHalfWidth = 0.075f;
        /// <summary>Corner radius where a service road meets anything (kerb-like, not sized for aircraft).</summary>
        const float RoadCornerRadius = 8f;
        const float StraightLightSpacing = 30f, CurveLightSpacing = 10f, LightOffset = 0.6f;

        sealed class Arm
        {
            public TaxiwayEdge edge;      // null: one direction of a runway's centreline
            public Vector2 dir;
            public float hw, length;
            public float cut;             // where the edge's straight strip starts, metres from the node
            public float need;            // the least a junction here needs along it (JunctionShape.Needs)
            public readonly List<Vector2> gap = new List<Vector2>();   // kerb to the next arm (counter-clockwise), mouth corners left out
            public bool Real => edge != null;
            public bool Road => edge != null && edge.IsRoad;
        }

        sealed class Joint
        {
            public TaxiwayNode node;
            public Vector2 p;
            public readonly List<Arm> arms = new List<Arm>();
            public List<Vector2> arc;     // curve nodes: the centreline from arms[0] to arms[1]
            public List<Vector2> outline; // junctions (empty where the arms just meet end to end)
            public bool fits = true;      // false: a section here is too short for what meets it (JunctionShape)
            public Arm ArmOf(TaxiwayEdge e) => arms.Find(a => a.edge == e);
        }

        /// <summary>The edges each vertex owner stands for (SurfaceState): one edge, or a curve's / junction's.</summary>
        public readonly List<TaxiwayNetwork.Owner> owners = new List<TaxiwayNetwork.Owner>();
        /// <summary>The edge each verge vertex owner stands for.</summary>
        public readonly List<TaxiwayEdge> vergeOwners = new List<TaxiwayEdge>();

        readonly TaxiwayNetwork net;
        readonly IReadOnlyList<Runway> runways;
        readonly Dictionary<int, Joint> joints = new Dictionary<int, Joint>();
        readonly IReadOnlyDictionary<TaxiwayEdge, List<(float s0, float s1, int side)>> frontage;

        public TaxiwayBuilder(TaxiwayNetwork net, IReadOnlyList<Runway> runways,
            IReadOnlyDictionary<TaxiwayEdge, List<(float s0, float s1, int side)>> frontage = null)
        {
            this.net = net;
            this.runways = runways;
            this.frontage = frontage;
        }

        /// <summary>Is a point s along an edge, on one side of it (-1 left, 1 right), where a stand opens onto it?</summary>
        bool Fronted(TaxiwayEdge e, int side, float s, float margin = 0f)
        {
            if (frontage == null || !frontage.TryGetValue(e, out var list)) return false;
            foreach (var (s0, s1, sd) in list)
                if ((sd == 0 || sd == side) && s >= s0 - margin && s <= s1 + margin) return true;
            return false;
        }

        /// <summary>The parts of [s0, s1] along an edge's side that no stand covers.</summary>
        List<(float s0, float s1)> Unfronted(TaxiwayEdge e, int side, float s0, float s1)
        {
            var free = new List<(float, float)> { (s0, s1) };
            if (frontage == null || !frontage.TryGetValue(e, out var list)) return free;
            foreach (var (c0, c1, sd) in list)
            {
                if (sd != 0 && sd != side) continue;
                var next = new List<(float, float)>();
                foreach (var (f0, f1) in free)
                {
                    if (c1 <= f0 || c0 >= f1)
                    {
                        next.Add((f0, f1));
                        continue;
                    }
                    if (c0 > f0) next.Add((f0, c0));
                    if (c1 < f1) next.Add((c1, f1));
                }
                free = next;
            }
            return free;
        }

        void Prepare()
        {
            if (joints.Count > 0) return;
            foreach (var n in net.nodes) joints[n.id] = Arms(n);
            foreach (var j in joints.Values) Needs(j);
            foreach (var j in joints.Values) Layout(j);
        }

        // Curves and junctions have level crossfall. Their adjoining straight grades terminate at
        // the actual mouths, rather than interpolating across the unbuilt corner of the graph.
        public TaxiwayElevation Elevation(bool includeStructures=false,bool roadsOnly=false)
        {
            Prepare();
            var profile = new TaxiwayElevation();
            foreach (var e in net.edges)
            {
                if((!includeStructures && e.structure!=RoadStructure.Ground)||(roadsOnly&&!e.IsRoad))continue;
                var a = joints[e.a]; var b = joints[e.b]; var aa = a.ArmOf(e); var ab = b.ArmOf(e);
                if (aa == null || ab == null) continue;
                float h0=a.node.runway!=null?Mathf.Lerp(a.node.position.y,b.node.position.y,aa.cut/aa.length):a.node.position.y;
                float h1=b.node.runway!=null?Mathf.Lerp(b.node.position.y,a.node.position.y,ab.cut/ab.length):b.node.position.y;
                if(e.IsRoad&&(e.alignment.curved||e.alignment.smooth))
                {
                    var path=net.Path(e);float end=path.Length-ab.cut;int count=Mathf.Max(1,Mathf.CeilToInt((end-aa.cut)/4f));
                    for(int i=0;i<count;i++)
                    {
                        var p=Geo.XZ(path.Offset(Mathf.Lerp(aa.cut,end,i/(float)count),0,aa.cut,ab.cut));
                        var q=Geo.XZ(path.Offset(Mathf.Lerp(aa.cut,end,(i+1f)/count),0,aa.cut,ab.cut));
                        profile.Add(p,RoadHeight(e,p),q,RoadHeight(e,q),e.width*.5f);
                    }
                }
                else profile.Add(a.p + aa.dir * aa.cut, h0, b.p + ab.dir * ab.cut, h1, e.width * .5f);
            }
            foreach (var j in joints.Values)
            {
                if((!includeStructures && !j.arms.Any(a=>a.Real && a.edge.structure==RoadStructure.Ground))||(roadsOnly&&!j.arms.Any(a=>a.Road)))continue;
                if (j.arc != null)
                    for (int i = 1; i < j.arc.Count; i++) profile.Add(j.arc[i-1], j.node.position.y, j.arc[i], j.node.position.y, j.arms[0].hw);
                else if (j.outline != null) profile.Add(j.p, j.outline, j.node.position.y);
            }
            return profile;
        }

        /// <summary>A service road has no verge: on a bridge or in a tunnel it gets a narrow hard strip, then the
        /// parapet or wall.</summary>
        public const float StructureVerge=.6f;

        internal bool SetBridgeDesign(ISet<int> selected,RoadBridgeDesign design)
        {
            var pieces=StructurePieces();
            var runs=pieces.Where(p=>p.edge==p.other&&p.edge.structure==RoadStructure.Elevated&&selected.Contains(p.edge.id))
                .Select(p=>p.piece.run.firstEdge).ToHashSet();
            bool changed=false;
            foreach(var item in pieces)
            {
                var edge=item.edge;
                if(edge!=item.other||edge.structure!=RoadStructure.Elevated||!runs.Contains(item.piece.run.firstEdge)||edge.bridgeDesign==design)continue;
                edge.bridgeDesign=design;
                changed=true;
            }
            return changed;
        }

        /// <summary>The bridge and tunnel pieces of the service roads (RoadStructureBuilder): each section between its
        /// nodes' cuts, and each bend or straight-through node between two sections of one structure. `edge` and
        /// `other` are the sections a piece belongs to.</summary>
        internal bool SetTunnelPortalStyle(TaxiwayEdge selected, TunnelPortalStyle style)
        {
            var pieces = StructurePieces();
            var selectedPiece = pieces.Find(item => item.edge == selected && item.other == selected).piece;
            if (selectedPiece == null) return false;
            bool changed = false;
            foreach (var item in pieces)
            {
                if (item.edge != item.other || item.piece.run.firstEdge != selectedPiece.run.firstEdge ||
                    item.edge.tunnelPortalStyle == style) continue;
                item.edge.tunnelPortalStyle = style;
                changed = true;
            }
            return changed;
        }

        internal Dictionary<int, TaxiwayEdge> PortalLinings(System.Func<Vector2,float> ground, System.Func<Vector2,float?> water)
        {
            Prepare();
            var result = new Dictionary<int, TaxiwayEdge>();
            foreach (var edge in net.edges)
            {
                if (!edge.IsRoad || edge.structure != RoadStructure.Ground) continue;
                var a = Adjacent(edge.a, edge);
                var b = Adjacent(edge.b, edge);
                if (a != null && b != null && RoadStructureBuilder.ShortDryApproach(net.Path(edge), edge.width * .5f +
                    StructureVerge + RoadStructureBuilder.WallThickness, water))
                    result[edge.id] = a.structure == RoadStructure.Tunnel ? a : b;
            }
            foreach (var joint in joints.Values)
            {
                if (joint.arms.Count != 2 || !joint.arms[0].Real || !joint.arms[1].Real) continue;
                var a = joint.arms[0];
                var b = joint.arms[1];
                if (Mathf.Abs(a.hw - b.hw) > .01f || Vector2.Angle(a.dir, b.dir) <= 179.5f) continue;
                if (a.edge.structure == RoadStructure.Tunnel) (a, b) = (b, a);
                var edge = a.edge;
                if (!edge.IsRoad || edge.structure != RoadStructure.Ground || b.edge.structure != RoadStructure.Tunnel ||
                    result.ContainsKey(edge.id)) continue;
                var path = net.Path(edge);
                float half = a.hw + StructureVerge + RoadStructureBuilder.WallThickness;
                for (int i = 0; i <= 3; i++)
                {
                    float step = Mathf.Min(path.Length, i * 2f * RoadStructureBuilder.WallStep);
                    float station = edge.a == joint.node.id ? step : path.Length - step;
                    Vector3 At(float side)
                    {
                        var point = path.Offset(station, side * half);
                        point.y = RoadHeight(edge, Geo.XZ(point)) + AirfieldHeights.Road;
                        return point;
                    }
                    if (!RoadStructureBuilder.TunnelCoverAt(At(-1f), At(1f), p => ground(Geo.XZ(p))).roofed) continue;
                    result[edge.id] = b.edge;
                    break;
                }
            }
            return result;

            TaxiwayEdge Adjacent(int node, TaxiwayEdge edge)
            {
                var arms = joints[node].arms;
                if (arms.Count != 2 || !arms[0].Real || !arms[1].Real || Mathf.Abs(arms[0].hw - arms[1].hw) > .01f ||
                    Vector2.Angle(arms[0].dir, arms[1].dir) <= 179.5f) return null;
                var other = arms[0].edge == edge ? arms[1].edge : arms[0].edge;
                return other.IsRoad && other.structure != RoadStructure.Ground ? other : null;
            }
        }

        public List<(StructurePiece piece,TaxiwayEdge edge,TaxiwayEdge other)> StructurePieces(
            IReadOnlyDictionary<int, TaxiwayEdge> portalLinings = null, ISet<int> retaining = null,
            System.Func<Vector2,float> ground = null, System.Func<Vector2,float?> water = null)
        {
            Prepare();
            var result=new List<(StructurePiece,TaxiwayEdge,TaxiwayEdge)>();
            var byId=new Dictionary<int,TaxiwayEdge>();
            RoadStructure Structure(TaxiwayEdge edge) => portalLinings?.ContainsKey(edge.id) == true ? RoadStructure.Tunnel : edge.structure;
            TunnelPortalStyle PortalStyle(TaxiwayEdge edge) =>
                portalLinings != null && portalLinings.TryGetValue(edge.id, out var lining) ? lining.tunnelPortalStyle : edge.tunnelPortalStyle;
            bool RetainsBank(TaxiwayEdge edge) => retaining?.Contains(edge.id) == true;
            foreach(var edge in net.edges)
                if(edge.IsRoad && (Structure(edge)!=RoadStructure.Ground || RetainsBank(edge)))byId[edge.id]=edge;
            if(byId.Count==0)return result;
            static Vector2 Right(Vector2 d)=>new Vector2(d.y,-d.x);
            float Length(TaxiwayEdge e)=>Mathf.Max(0f,net.Path(e).Length-joints[e.a].ArmOf(e).cut-joints[e.b].ArmOf(e).cut);
            // the section the structure carries on with past a node: round a bend, or (nearly) straight on
            TaxiwayEdge Onward(int node,TaxiwayEdge e)
            {
                var joint=joints[node];
                if(joint.arms.Count!=2||!joint.arms[0].Real||!joint.arms[1].Real)return null;
                var other=joint.arms[0].edge==e?joint.arms[1].edge:joint.arms[0].edge;
                if(other==e||!byId.ContainsKey(other.id)||Structure(other)!=Structure(e))return null;
                return joint.arc!=null||Vector2.Dot(joint.arms[0].dir,joint.arms[1].dir)<-.9f?other:null;
            }
            bool MeetsGround(int node,TaxiwayEdge e)=>joints[node].arms.Exists(a=>!a.Real||a.edge!=e&&Structure(a.edge)==RoadStructure.Ground);
            TaxiwayEdge StraightNeighbor(int node, TaxiwayEdge edge)
            {
                if (!joints.TryGetValue(node, out var joint) || joint.arms.Count != 2 || !joint.arms[0].Real || !joint.arms[1].Real ||
                    Mathf.Abs(joint.arms[0].hw - joint.arms[1].hw) > .01f ||
                    Vector2.Angle(joint.arms[0].dir, joint.arms[1].dir) <= 179.5f) return null;
                return joint.arms[0].edge == edge ? joint.arms[1].edge : joint.arms[0].edge;
            }
            // a joint's centreline from arms[0]'s mouth to arms[1]'s
            List<Vector2> Line(Joint joint)
            {
                if(joint.arc!=null)return joint.arc;
                var a=joint.p+joint.arms[0].dir*joint.arms[0].cut;var b=joint.p+joint.arms[1].dir*joint.arms[1].cut;
                var line=new List<Vector2>{a};
                if(Vector2.Distance(a,joint.p)>.05f&&Vector2.Distance(b,joint.p)>.05f)line.Add(joint.p);
                if(Vector2.Distance(a,b)>.02f)line.Add(b);return line;
            }
            float LineLength(Joint joint)
            {var line=Line(joint);float total=0;for(int k=1;k<line.Count;k++)total+=Vector2.Distance(line[k-1],line[k]);return total;}
            var edgeRuns=new Dictionary<int,RunSpan>();var bendRuns=new Dictionary<int,RunSpan>();
            foreach(var edge in byId.Values)
            {
                if(edgeRuns.ContainsKey(edge.id))continue;
                RoadStructureBuilder.Runs(edge.id,id=>(byId[id].a,byId[id].b),(node,id)=>Onward(node,byId[id])?.id??-1,id=>Length(byId[id]),
                    (node,id)=>(LineLength(joints[node]),joints[node].arms[0].edge.id!=id),(node,id)=>!MeetsGround(node,byId[id]),edgeRuns,bendRuns);
            }
            foreach(var edge in byId.Values)
            {
                var path=net.Path(edge);float cutA=joints[edge.a].ArmOf(edge).cut,cutB=joints[edge.b].ArmOf(edge).cut,from=cutA,to=path.Length-cutB,half=edge.width*.5f;
                if(to-from<.01f)continue;
                var piece=new StructurePiece {structure=Structure(edge),portalStyle=PortalStyle(edge),verge=StructureVerge,surface=AirfieldHeights.Road,run=edgeRuns[edge.id],length=Length(edge),
                    retainsBank=RetainsBank(edge)};
                int count=Mathf.Max(1,Mathf.CeilToInt((to-from)/4f));
                for(int i=0;i<=count;i++)
                {
                    float s=Mathf.Lerp(from,to,i/(float)count);
                    Vector3 At(float x){var p=path.Offset(s,x,cutA,cutB);p.y=RoadHeight(edge,Geo.XZ(p))+AirfieldHeights.Road;return p;}
                    piece.Add(At(-half),At(half));
                }
                int last=piece.left.Count-1;
                bool onA=Continues(edge.a,0),onB=Continues(edge.b,last);
                bool Continues(int node, int frame)
                {
                    if (Onward(node, edge) != null) return true;
                    var adjacent = StraightNeighbor(node, edge);
                    if (adjacent == null) return false;
                    if (piece.structure == RoadStructure.Elevated && Structure(adjacent) == RoadStructure.Tunnel)
                    {
                        if (frame == 0) piece.tunnelAtStart = PortalStyle(adjacent);
                        else piece.tunnelAtEnd = PortalStyle(adjacent);
                    }
                    return piece.structure == RoadStructure.Tunnel && adjacent.structure == RoadStructure.Elevated && ground != null &&
                        RoadStructureBuilder.DeckNeedsLining(piece.left[frame], piece.right[frame], piece.verge, ground, water);
                }
                piece.capStart=!onA;piece.abutStart=!onA&&MeetsGround(edge.a,edge);piece.capEnd=!onB;piece.abutEnd=!onB&&MeetsGround(edge.b,edge);
                // a dead end: the parapet or wall closes it, rounding the corners of the road's square end
                if(joints[edge.a].arms.Count==1){piece.capStart=false;piece.endStart=RoadStructureBuilder.SquareEnd(piece.left[0],piece.right[0],-path.Tangent(0));}
                if(joints[edge.b].arms.Count==1){piece.capEnd=false;piece.endEnd=RoadStructureBuilder.SquareEnd(piece.left[last],piece.right[last],path.Tangent(1));}
                result.Add((piece,edge,edge));
            }
            foreach(var joint in joints.Values)
            {
                if(joint.arms.Count!=2||!joint.arms[0].Real||!byId.ContainsKey(joint.arms[0].edge.id)||Onward(joint.node.id,joint.arms[0].edge)==null)continue;
                var line=Line(joint);int m=line.Count;if(m<2)continue;
                var edge=joint.arms[0].edge;float half=joint.arms[0].hw,y=joint.node.position.y+AirfieldHeights.Road;
                var piece=new StructurePiece {structure=Structure(edge),portalStyle=PortalStyle(edge),verge=StructureVerge,surface=AirfieldHeights.Road,length=LineLength(joint),
                    retainsBank=RetainsBank(edge) || RetainsBank(joint.arms[1].edge)};
                if(bendRuns.TryGetValue(joint.node.id,out var run))piece.run=run;
                for(int k=0;k<m;k++)
                {
                    var d=k==0?-joint.arms[0].dir:k==m-1?joint.arms[1].dir:line[k+1]-line[k-1];var right=Right(d.normalized)*half;
                    piece.Add(Geo.X0Z(line[k]-right,y),Geo.X0Z(line[k]+right,y));
                }
                result.Add((piece,edge,joint.arms[1].edge));
            }
            return result;
        }

        /// <summary>
        /// The outline of a taxiway or road section as it is drawn, `half` metres either side of its centreline,
        /// between two distances along it (from its node a): up one side and back down the other, airport-local. A
        /// bend belongs half to each of its two sections, so an outline that reaches a bend carries on round it to
        /// the middle of the arc instead of running straight to the corner; at a junction it stops at its edge.
        /// </summary>
        public bool SectionOutline(TaxiwayEdge edge, float from, float to, float half, List<Vector3> loop)
        {
            Prepare();
            if (!joints.TryGetValue(edge.a, out var ja) || !joints.TryGetValue(edge.b, out var jb)) return false;
            Arm aa = ja.ArmOf(edge), ab = jb.ArmOf(edge);
            if (aa == null || ab == null) return false;
            var path = net.Path(edge);
            float length = path.Length;
            var centre = new List<(Vector3 p, Vector2 right)>();
            // the half of a bend's arc on this section's side, in the direction a to b
            void HalfArc(Joint j, Arm arm, bool leaving)
            {
                if (j.arc == null || j.arc.Count < 2 || j.arms.Count != 2) return;
                var along = Geo.Along(j.arc);
                float total = along[along.Length - 1];
                bool first = j.arms[0] == arm;       // the arc starts at arms[0]'s cut
                var points = first ? Geo.Slice(j.arc, along, 0f, total * .5f) : Geo.Slice(j.arc, along, total * .5f, total);
                if (first == leaving) points.Reverse();   // leaving the bend: from the arc's middle to this section's cut
                for (int k = 0; k < points.Count; k++)
                {
                    var d = points[Mathf.Min(k + 1, points.Count - 1)] - points[Mathf.Max(k - 1, 0)];
                    if (d.sqrMagnitude > 1e-8f) centre.Add((Geo.X0Z(points[k], j.node.position.y), Geo.Right(d.normalized)));
                }
            }
            if (from <= .01f) HalfArc(ja, aa, true);
            float s0 = Mathf.Max(from, aa.cut), s1 = Mathf.Min(to, length - ab.cut);
            if (s1 - s0 > .01f)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 4f));
                for (int i = 0; i <= count; i++)
                {
                    float s = Mathf.Lerp(s0, s1, i / (float)count);
                    var c = path.Offset(s, 0f);
                    centre.Add((c, Geo.XZ(path.Offset(s, 1f) - c)));
                }
            }
            if (to >= length - .01f) HalfArc(jb, ab, false);
            if (centre.Count < 2) return false;
            for (int i = 0; i < centre.Count; i++) loop.Add(centre[i].p - Geo.X0Z(centre[i].right) * half);
            for (int i = centre.Count - 1; i >= 0; i--) loop.Add(centre[i].p + Geo.X0Z(centre[i].right) * half);
            return true;
        }

        public float RoadHeight(TaxiwayEdge edge,Vector2 p)
        {
            Prepare();var path=net.Path(edge);float height=path.Height(path.ClosestParameter(p));
            var ja=joints[edge.a];var jb=joints[edge.b];
            if(ja.arms.Count>1)height=Mathf.Lerp(height,ja.node.position.y,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(ja.ArmOf(edge).cut,ja.ArmOf(edge).cut+20,Vector2.Distance(p,ja.p))));
            if(jb.arms.Count>1)height=Mathf.Lerp(height,jb.node.position.y,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(jb.ArmOf(edge).cut,jb.ArmOf(edge).cut+20,Vector2.Distance(p,jb.p))));
            return height;
        }
        public void Warp(MeshData mesh,IReadOnlyList<StandLayout> stands,bool direction=false,bool fixtures=false)
        {
            var profile=Elevation();
            var special=new bool[owners.Count];var shared=new float[owners.Count];
            for(int i=0;i<owners.Count;i++)
            {
                var owner=owners[i];special[i]=owner.edges.Any(e=>e.IsRoad&&(e.structure!=RoadStructure.Ground||e.alignment.smooth||e.alignment.curved));
                if(special[i]&&owner.joint)
                {
                    var first=owner.edges[0];int node=owner.edges.All(e=>e.a==first.a||e.b==first.a)?first.a:first.b;shared[i]=net.Node(node).position.y;
                }
            }
            float Height(int vertex,Vector2 p)
            {
                int id=mesh.owners[vertex];
                if(id>=0&&id<owners.Count&&special[id])return owners[id].joint?shared[id]:RoadHeight(owners[id].edges[0],p);
                return Airport.SupportElevation(p,profile,runways,stands);
            }
            AirfieldElevation.WarpOwned(mesh,Height,direction,fixtures);
        }

        public void Build(SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            Prepare();
            foreach (var e in net.edges)
            {
                Own(surface, paint, new[] { e }, false);
                BuildEdge(e, surface, paint, lights);
            }
            foreach (var j in joints.Values)
            {
                if (j.arc == null && j.outline == null) continue;
                Own(surface, paint, j.arms.Where(a => a.Real).Select(a => a.edge).ToArray(), true);
                if (j.arc != null) BuildCurve(j, surface, paint, lights);
                else BuildJunction(j, surface, paint, lights);
            }
        }

        void Own(SurfaceBuilder surface, PaintBuilder paint, TaxiwayEdge[] edges, bool joint)
        {
            surface.mesh.owner = paint.mesh.owner = owners.Count;
            var owner = new TaxiwayNetwork.Owner { edges = edges, joint = joint };
            surface.state = paint.state = TaxiwayNetwork.StateOf(owner);
            owners.Add(owner);
        }

        // ------------------------------------------------------------------ layout

        /// <summary>A node's arms, sorted counter-clockwise (laid out later, once every node's needs are known).</summary>
        Joint Arms(TaxiwayNode node)
        {
            var j = new Joint { node = node, p = Geo.XZ(node.position) };
            foreach (var e in net.EdgesAt(node.id))
            {
                var other = net.Node(e.a == node.id ? e.b : e.a);
                if (other == null) continue;
                var path=net.Path(e);var tangent=path.Tangent(e.a==node.id?0:1)*(e.a==node.id?1:-1);
                var d=Geo.XZ(tangent).normalized*path.Length;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                j.arms.Add(new Arm { edge = e, dir = d / len, hw = e.width * 0.5f, length = len });
            }
            if (node.runway != null)
            {
                var r = node.runway;
                r.Locate(node.position, out float s, out _);
                if (r.Length - s > 1f) j.arms.Add(new Arm { dir = r.Axis2D, hw = r.HalfWidthTotal, length = r.Length - s });
                if (s > 1f) j.arms.Add(new Arm { dir = -r.Axis2D, hw = r.HalfWidthTotal, length = s });
            }
            j.arms.Sort((a, b) => Geo.Angle(a.dir).CompareTo(Geo.Angle(b.dir)));
            return j;
        }

        /// <summary>What each arm needs at its node: how far out its strip must start to clear the other arms.</summary>
        static void Needs(Joint j)
        {
            if (j.arms.Count < 2) return;
            var need = JunctionShape.Needs(j.arms.ConvertAll(a => new JunctionArm(a.dir, a.hw, a.length, a.length)));
            for (int i = 0; i < j.arms.Count; i++) j.arms[i].need = need[i];
        }

        void Layout(Joint j)
        {
            int real = j.arms.FindAll(a => a.Real).Count;
            if (real == 0 || j.arms.Count == 1) return;     // nothing here, or a dead end (flat end, cut 0)
            if (real == 2 && j.arms.Count == 2 && TryCurve(j)) return;
            Junction(j);
        }

        bool TryCurve(Joint j)
        {
            var a0 = j.arms[0];
            var a1 = j.arms[1];
            if (a0.edge.structure!=a1.edge.structure || a0.edge.alignment.curved || a1.edge.alignment.curved || Mathf.Abs(a0.edge.width - a1.edge.width) > 0.01f || a0.edge.surface != a1.edge.surface || a0.edge.road != a1.edge.road) return false;
            float alpha = Mathf.Acos(Mathf.Clamp(Vector2.Dot(a0.dir, a1.dir), -1f, 1f));
            if (alpha > Mathf.PI - 3f * Mathf.Deg2Rad || alpha < 25f * Mathf.Deg2Rad) return false;
            float radius = RunwayRules.TaxiwayTurnRadius(a0.edge.width);
            float tanHalf = Mathf.Tan(alpha * 0.5f);
            float cut = radius / tanHalf;
            float maxCut = Mathf.Min(BendLimit(j, a0), BendLimit(j, a1));
            if (cut > maxCut)
            {
                cut = maxCut;
                radius = cut * tanHalf;
            }
            if (radius < a0.hw * 1.25f) return false;
            Geo.Fillet(j.p, a0.dir, a1.dir, radius, out var centre);
            j.arc = new List<Vector2>();
            Geo.Arc(j.arc, centre, j.p + a0.dir * cut, j.p + a1.dir * cut, Geo.ArcStep(radius));
            a0.cut = a1.cut = cut;
            return true;
        }

        /// <summary>A junction's shape (JunctionShape): every arm cut back clear of the others, kerb fillets in the
        /// corners (kerb-sized where a road is involved, aircraft-sized between taxiways), flares where a narrow arm
        /// meets a wide one. The outline is star-shaped about the node; each arm's gap is its kerb to the next arm.</summary>
        void Junction(Joint j)
        {
            int n = j.arms.Count;
            var shape = new List<JunctionArm>(n);
            foreach (var a in j.arms) shape.Add(new JunctionArm(a.dir, a.hw, a.length, Limit(j, a)));
            j.outline = JunctionShape.SolveCached(j.p, shape, (i, k) => CornerRadius(j.arms[i], j.arms[k]), out j.fits);
            for (int i = 0; i < n; i++)
            {
                var a = j.arms[i];
                a.cut = shape[i].cut;
                a.gap.Clear();
                var kerb = shape[i].kerb;
                for (int k = 1; k < kerb.Count - 1; k++) a.gap.Add(kerb[k]);   // the mouth corners are added where it's used
            }
        }

        /// <summary>Kerb radius between two neighbouring arms: kerb-sized where a road is involved, else sized for the
        /// narrower taxiway's aircraft (a runway counts as the taxiway beside it).</summary>
        static float CornerRadius(Arm a, Arm b)
        {
            if (a.Road || b.Road) return RoadCornerRadius;
            float w = !a.Real && !b.Real ? 2f * Mathf.Min(a.hw, b.hw) : !a.Real ? b.edge.width : !b.Real ? a.edge.width : Mathf.Min(a.edge.width, b.edge.width);
            return RunwayRules.TaxiwayTurnRadius(w) - w * 0.5f;
        }

        /// <summary>How far along an arm a junction or bend may reach: its share of the edge with whatever is at the
        /// edge's other end (JunctionShape.Reach), or to just short of a runway's end.</summary>
        float Limit(Joint j, Arm a)
        {
            if (!a.Real) return Mathf.Max(a.length - 0.5f, 0f);
            float there = joints.TryGetValue(TaxiwayNetwork.Other(a.edge, j.node.id), out var far) ? far.ArmOf(a.edge)?.need ?? 0f : 0f;
            return JunctionShape.Reach(a.length, a.need, there);
        }

        /// <summary>How far along an arm a bend may reach. A road that ends in a dead end has nothing to share with
        /// there: the bend takes it all but a short straight, so a short stub doesn't squeeze the bend tight. Taxiways
        /// keep their share: aircraft are routed round the radius the rules give (TaxiPlanner).</summary>
        float BendLimit(Joint j, Arm a)
        {
            if (a.Road && joints.TryGetValue(TaxiwayNetwork.Other(a.edge, j.node.id), out var far) && far.arms.Count == 1)
                return a.length - Mathf.Min(4f, a.length * 0.25f);
            return Limit(j, a);
        }

        // ------------------------------------------------------------------ pieces

        static int Sub(PavementSurface s) => (int)s;

        void BuildEdge(TaxiwayEdge e, SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            var ja = joints[e.a];
            var jb = joints[e.b];
            var armA = ja.ArmOf(e);
            var armB = jb.ArmOf(e);
            if (armA == null || armB == null) return;
            float len = armA.length;
            var u = armA.dir;
            float hw = e.width * 0.5f;
            float s0 = armA.cut, s1 = len - armB.cut;
            bool unpaved = !e.surface.IsPaved();
            bool roundA=e.IsRoad && ja.arms.Count==1,roundB=e.IsRoad && jb.arms.Count==1;
            float radius=Mathf.Min(2f,hw*.6f,(s1-s0)*.2f);
            if(roundA)s0+=radius;if(roundB)s1-=radius;
            if(e.IsRoad && (e.alignment.curved||e.alignment.smooth||e.structure!=RoadStructure.Ground))BuildCurvedRoad(e,ja,jb,armA,armB,s0,s1,surface,paint);
            else if (s1 - s0 > 0.01f)
            {
                int first = surface.mesh.Count;
                surface.Rect(Sub(e.surface), ja.p, u, s0, s1, -hw, hw, e.IsRoad ? AirfieldHeights.Road : AirfieldHeights.Taxiway,
                    e.IsRoad?AirfieldKind.Road:AirfieldKind.Taxiway, 0f, unpaved ? SurfaceBuilder.Fringe : 0f, false);
                if (e.IsRoad && !unpaved) RoadStripEdges(surface, e.surface, first, surface.mesh.Count);
            }
            if(e.IsRoad)
            {
                if(roundA) Cap(ja.p,-u);
                if(roundB) Cap(jb.p,u);

                void Cap(Vector2 end, Vector2 outward)
                {
                    var outline = SurfaceBuilder.RoadEndOutline(end, outward, hw, radius);
                    surface.Polygon(Sub(e.surface), outline, AirfieldHeights.Road, AirfieldKind.Road);
                    if (!unpaved && e.structure == RoadStructure.Ground) RoadOutlineEdge(surface, e.surface, outline, true);
                }
            }
            if (e.IsRoad)
            {
                if(!e.alignment.curved&&!e.alignment.smooth&&e.structure==RoadStructure.Ground)BuildRoadPaint(e, ja, armA, jb, armB, paint);
                return;
            }
            if (unpaved) return;

            // centreline: up to the curve / junction, or across the fillet to the runway edge
            paint.frame = RunwayFrame.Along(ja.p, u);
            float p0 = PaintEnd(ja, armA), p1 = len - PaintEnd(jb, armB);
            if (p1 - p0 > 0.5f)
                paint.Strip(new[] { ja.p + u * p0, ja.p + u * p1 }, CentrelineHalfWidth, PaintBuilder.Yellow);

            Holding(ja, armA, len - armB.cut, paint, lights);
            Holding(jb, armB, len - armA.cut, paint, lights);

            if (e.lit && s1 - s0 > 1f)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / StraightLightSpacing));
                for (int i = 0; i < n; i++)
                {
                    float s = Mathf.Lerp(s0, s1, (i + 0.5f) / n);
                    foreach (float sgn in new[] { -1f, 1f })
                        if (!Fronted(e, (int)sgn, s, 2f))
                            EdgeLight(lights, ja.p + u * s + Geo.Right(u) * (sgn * (hw + LightOffset)));
                }
            }
        }

        void BuildCurvedRoad(TaxiwayEdge edge,Joint a,Joint b,Arm aa,Arm ab,float from,float to,SurfaceBuilder surface,PaintBuilder paint)
        {
            var path=net.Path(edge);int steps=Mathf.Max(1,Mathf.CeilToInt((to-from)/4f));float half=edge.width*.5f;
            Vector2 At(float s,float x)=>Geo.XZ(path.Offset(s,x,aa.cut,ab.cut));
            for(int i=0;i<steps;i++)
            {
                float s0=Mathf.Lerp(from,to,i/(float)steps),s1=Mathf.Lerp(from,to,(i+1f)/steps);int first=surface.mesh.Count;
                void Vertex(float s,float x){var p=At(s,x);surface.mesh.Vertex(Geo.X0Z(p,AirfieldHeights.Road),Vector3.up,new Color(surface.state.wear,surface.state.snow,surface.state.grass,1),new Vector4(x,s),new Vector4((float)AirfieldKind.Road,0));}
                Vertex(s0,-half);Vertex(s1,-half);Vertex(s1,half);Vertex(s0,half);surface.mesh.Quad(Sub(edge.surface),first,first+1,first+2,first+3);
                if (edge.structure == RoadStructure.Ground && edge.surface.IsPaved())
                {
                    var points = surface.mesh.positions;
                    RoadStripEdges(surface, edge.surface, points[first], points[first+3], points[first+1], points[first+2]);
                }
            }
            paint.frame=RunwayFrame.None;
            List<Vector2> Line(float x)
            {
                int count=Mathf.Max(1,Mathf.CeilToInt((to-from)/2f));var points=new List<Vector2>(count+1);
                for(int i=0;i<=count;i++)points.Add(At(Mathf.Lerp(from,to,i/(float)count),x));return points;
            }
            paint.Strip(Line(-half+RoadEdgeInset),RoadLineHalfWidth,PaintBuilder.White,true);paint.Strip(Line(half-RoadEdgeInset),RoadLineHalfWidth,PaintBuilder.White,true);
            List<Vector2> Centre(float f,float t)
            {
                int count=Mathf.Max(1,Mathf.CeilToInt((t-f)/2f));var points=new List<Vector2>(count+1);
                for(int i=0;i<=count;i++)points.Add(At(Mathf.Lerp(f,t,i/(float)count),0));return points;
            }
            RoadDashes(edge,Centre,paint);
            RoadEnd(a,aa,path.Length-ab.cut,paint);RoadEnd(b,ab,path.Length-aa.cut,paint);
        }

        // ------------------------------------------------------------------ service roads

        static void RoadStripEdges(SurfaceBuilder surface, PavementSurface material, int first, int end)
        {
            var points = surface.mesh.positions;
            for (int i = first; i+3 < end; i+=2)
                RoadStripEdges(surface, material, points[i], points[i+1], points[i+2], points[i+3]);
        }

        static void RoadStripEdges(SurfaceBuilder surface, PavementSurface material, Vector3 leftA, Vector3 rightA, Vector3 leftB, Vector3 rightB)
        {
            var outward = Geo.XZ(leftA+leftB-rightA-rightB);
            RoadEdge(surface, material, leftA, leftB, outward);
            RoadEdge(surface, material, rightA, rightB, -outward);
        }

        static void RoadOutlineEdge(SurfaceBuilder surface, PavementSurface material, List<Vector2> line, bool left)
        {
            for (int i = 0; i+1 < line.Count; i++)
                RoadEdge(surface, material, Geo.X0Z(line[i],AirfieldHeights.Road), Geo.X0Z(line[i+1],AirfieldHeights.Road),
                    Geo.Right(line[i+1]-line[i])*(left?-1f:1f));
        }

        static void RoadEdge(SurfaceBuilder surface, PavementSurface material, Vector3 a, Vector3 b, Vector2 outward)
        {
            var state = surface.state;
            surface.mesh.RoadEdge(Sub(material), a, b, outward, new Color(state.wear,state.snow,state.grass,1f),
                new Vector4((float)AirfieldKind.Road,0f));
        }

        const float RoadLineHalfWidth = 0.075f, RoadEdgeInset = 0.3f;

        /// <summary>White edge lines and centre dashes along a road's straight, then what it meets at each end.</summary>
        void BuildRoadPaint(TaxiwayEdge e, Joint ja, Arm armA, Joint jb, Arm armB, PaintBuilder paint)
        {
            if (!e.surface.IsPaved()) return;
            float len = armA.length;
            var u = armA.dir;
            float hw = e.width * 0.5f;
            paint.frame = RunwayFrame.Along(ja.p, u);
            float radius=Mathf.Min(2f,hw*.6f,len*.2f);
            bool roundA=ja.arms.Count==1,roundB=jb.arms.Count==1;
            float p0=Mathf.Max(PaintEnd(ja,armA),GiveWayAt(ja,armA),roundA?radius:0f),p1=len-Mathf.Max(PaintEnd(jb,armB),GiveWayAt(jb,armB),roundB?radius:0f);
            if (p1 - p0 > 0.5f)
            {
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var off = Geo.Right(u) * (sgn * (hw - RoadEdgeInset));
                    paint.Strip(new[] { ja.p + u * p0 + off, ja.p + u * p1 + off }, RoadLineHalfWidth, PaintBuilder.White, true);
                }
                RoadDashes(e, (f, t) => new List<Vector2> { ja.p + u * f, ja.p + u * t }, paint);
            }
            if(roundA)paint.Strip(SurfaceBuilder.RoadEndOutline(ja.p+u*RoadEdgeInset,-u,hw-RoadEdgeInset,Mathf.Max(.1f,radius-RoadEdgeInset)),RoadLineHalfWidth,PaintBuilder.White,true);
            if(roundB)paint.Strip(SurfaceBuilder.RoadEndOutline(jb.p-u*RoadEdgeInset,u,hw-RoadEdgeInset,Mathf.Max(.1f,radius-RoadEdgeInset)),RoadLineHalfWidth,PaintBuilder.White,true);
            RoadEnd(ja, armA, len - armB.cut, paint);
            RoadEnd(jb, armB, len - armA.cut, paint);
        }

        // ------------------------------------------------------------------ dashes round bends

        /// <summary>The part of a road its centre dashes run along, in metres from its node a: between its ends' cuts,
        /// give-way lines and rounded dead ends. Empty where nothing is painted.</summary>
        (float from, float to) RoadDashSpan(TaxiwayEdge e)
        {
            var ja = joints[e.a];
            var jb = joints[e.b];
            Arm armA = ja.ArmOf(e), armB = jb.ArmOf(e);
            float len = armA.length, hw = e.width * 0.5f;
            bool roundA = ja.arms.Count == 1, roundB = jb.arms.Count == 1;
            if (e.alignment.curved || e.alignment.smooth || e.structure != RoadStructure.Ground)
            {
                // BuildCurvedRoad: the whole strip
                float s0 = armA.cut, s1 = len - armB.cut, r = Mathf.Min(2f, hw * .6f, (s1 - s0) * .2f);
                if (roundA) s0 += r;
                if (roundB) s1 -= r;
                return s1 > s0 ? (s0, s1) : (s0, s0);
            }
            float radius = Mathf.Min(2f, hw * .6f, len * .2f);
            float p0 = Mathf.Max(PaintEnd(ja, armA), GiveWayAt(ja, armA), roundA ? radius : 0f);
            float p1 = len - Mathf.Max(PaintEnd(jb, armB), GiveWayAt(jb, armB), roundB ? radius : 0f);
            return p1 - p0 > 0.5f ? (p0, p1) : (p0, p0);
        }

        /// <summary>Whether a road's dashes carry on past a node: round a bend, or straight on into another road of
        /// the same width that simply meets it there.</summary>
        static bool RoadContinues(Joint j)
        {
            if (j.arms.Count != 2 || !j.arms[0].Road || !j.arms[1].Road || j.arms[0].edge == j.arms[1].edge) return false;
            if (j.arc != null) return true;
            return Mathf.Abs(j.arms[0].hw - j.arms[1].hw) < 0.01f && j.arms[0].cut < 0.01f && j.arms[1].cut < 0.01f &&
                Vector2.Dot(j.arms[0].dir, j.arms[1].dir) < -0.99f && j.arms[0].edge.surface.IsPaved() && j.arms[1].edge.surface.IsPaved();
        }

        /// <summary>
        /// Dashes are laid out in stretches that run through a bend (PaintBuilder.Dashes): from the middle of the road
        /// before it (arms[0]), round the arc, to the middle of the road after it, or to that road's far end where its
        /// dashes stop there (a junction, a dead end). The roads and the bend each paint their own part of the same
        /// stretch, so the dashes keep their rhythm through the bend; stretches meet in a gap in the middle of a road.
        /// `before`: the stretch's length up to the bend.
        /// </summary>
        void RoadStretch(Joint j, out float before, out float bend, out float total)
        {
            float Share(Arm arm)
            {
                var e = arm.edge;
                var (from, to) = RoadDashSpan(e);
                return Mathf.Max(0f, RoadContinues(joints[e.a]) && RoadContinues(joints[e.b]) ? (to - from) * 0.5f : to - from);
            }
            before = Share(j.arms[0]);
            bend = 0f;
            if (j.arc != null)
                for (int i = 1; i < j.arc.Count; i++) bend += (j.arc[i] - j.arc[i - 1]).magnitude;
            total = before + bend + Share(j.arms[1]);
        }

        /// <summary>A road's centre dashes: its part of the stretch through each end where the road carries on
        /// (RoadStretch), or a stretch of its own. `line(a, b)`: its centreline between two distances from node a.</summary>
        void RoadDashes(TaxiwayEdge e, System.Func<float, float, List<Vector2>> line, PaintBuilder paint)
        {
            var (from, to) = RoadDashSpan(e);
            if (to - from < 0.01f) return;
            bool onA = RoadContinues(joints[e.a]), onB = RoadContinues(joints[e.b]);
            if (!onA && !onB)
            {
                paint.Dashes((a, b) => line(a, b), from, to, from, to, RoadLineHalfWidth, 3f, 6f, PaintBuilder.White, true);
                return;
            }
            float mid = onA && onB ? (from + to) * 0.5f : onA ? to : from;
            foreach (bool atA in new[] { true, false })
            {
                if (!(atA ? onA : onB)) continue;
                var j = joints[atA ? e.a : e.b];
                bool first = j.arms[0].edge == e;
                RoadStretch(j, out float before, out float bend, out float total);
                float own = atA ? mid - from : to - mid;
                // u runs along the stretch; d back along this road from its cut at the node
                float Station(float u)
                {
                    float d = first ? before - u : u - before - bend;
                    return atA ? from + d : to - d;
                }
                float u0 = first ? before - own : before + bend, u1 = first ? before : before + bend + own;
                paint.Dashes((a, b) =>
                {
                    float sa = Station(a), sb = Station(b);
                    var points = line(Mathf.Min(sa, sb), Mathf.Max(sa, sb));
                    if (sa > sb) points.Reverse();
                    return points;
                }, u0, u1, 0f, total, RoadLineHalfWidth, 3f, 6f, PaintBuilder.White, true);
            }
        }

        /// <summary>How far from the node a road's give-way line is (0: it doesn't give way there). Before a taxiway:
        /// 2 m back from the taxiway's edge, never inside the junction. Where only roads meet: at the edge of the
        /// road it joins, unless it is the main road.</summary>
        float GiveWayAt(Joint j, Arm arm)
        {
            float t = 0f;
            foreach (var b in j.arms)
            {
                if (b == arm || !b.Real || b.Road) continue;
                float sin = Mathf.Max(Mathf.Abs(Geo.Cross(arm.dir, b.dir)), 0.3f);
                t = Mathf.Max(t, (b.hw + 2f) / sin + 1f);
            }
            if (t > 0f) return Mathf.Max(t, arm.cut + 0.5f);
            if (!RoadJunction(j) || j.arms.Count < 3 || (MainRoad(j, out var m0, out var m1) && (arm == m0 || arm == m1))) return 0f;
            foreach (var b in j.arms)
            {
                if (b == arm || Vector2.Angle(arm.dir, b.dir) >= MainRoadAngle) continue;
                float sin = Mathf.Max(Mathf.Abs(Geo.Cross(arm.dir, b.dir)), 0.3f);
                t = Mathf.Max(t, (b.hw + 0.5f) / sin);
            }
            return t;
        }

        /// <summary>Two roads at least this far apart (degrees) run straight on through a junction.</summary>
        const float MainRoadAngle = 150f;

        /// <summary>A junction where only roads meet (no taxiway, no runway).</summary>
        static bool RoadJunction(Joint j) => j.outline != null && j.node.runway == null && j.arms.Count >= 2 && j.arms.All(a => a.Road);

        /// <summary>The road running straight on through a road junction, if exactly one does: it has priority. A
        /// crossroads of two straight roads, or a junction with none, has no main road (every road gives way).</summary>
        static bool MainRoad(Joint j, out Arm a, out Arm b)
        {
            a = b = null;
            int found = 0;
            for (int i = 0; i < j.arms.Count; i++)
                for (int k = i + 1; k < j.arms.Count; k++)
                    if (Vector2.Angle(j.arms[i].dir, j.arms[k].dir) >= MainRoadAngle)
                    {
                        found++;
                        a = j.arms[i];
                        b = j.arms[k];
                    }
            if (found == 1) return true;
            a = b = null;
            return false;
        }

        /// <summary>Paint where only roads meet: edge lines round every kerb (straight on across the far side of a
        /// T), and the main road's centre dashes through the middle. The give-way lines come with each road's own
        /// paint (RoadEnd).</summary>
        void RoadJunctionPaint(Joint j, PaintBuilder paint)
        {
            paint.frame = RunwayFrame.None;
            int n = j.arms.Count;
            for (int i = 0; i < n; i++)
            {
                var a = j.arms[i];
                var b = j.arms[(i + 1) % n];
                if (!a.edge.surface.IsPaved() || !b.edge.surface.IsPaved()) continue;
                var kerb = new List<Vector2> { j.p + a.dir * a.cut + Geo.Left(a.dir) * a.hw };
                kerb.AddRange(a.gap);
                kerb.Add(j.p + b.dir * b.cut + Geo.Right(b.dir) * b.hw);
                paint.Strip(Inside(kerb, RoadEdgeInset), RoadLineHalfWidth, PaintBuilder.White, true);
            }
            if (!MainRoad(j, out var m0, out var m1)) return;
            var p0 = j.p + m0.dir * m0.cut;
            var p1 = j.p + m1.dir * m1.cut;
            var pts = new List<Vector2>();
            for (int s = 0; s <= 16; s++)
            {
                float t = s / 16f;
                pts.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * j.p + t * t * p1);
            }
            // laid out like the roads' own dashes (half a gap at each end), so the rhythm carries across
            var at = Geo.Along(pts);
            float across = at[at.Length - 1];
            paint.Dashes((a, b) => Geo.Slice(pts, at, a, b), 0f, across, 0f, across, RoadLineHalfWidth, 3f, 6f, PaintBuilder.White, true);
        }

        /// <summary>A kerb line moved `inset` metres in onto the pavement (to the left of the way it runs: kerbs
        /// are walked counter-clockwise round the junction). Repeated points are dropped.</summary>
        static List<Vector2> Inside(List<Vector2> line, float inset)
        {
            var pts = new List<Vector2>();
            foreach (var p in line)
                if (pts.Count == 0 || (p - pts[pts.Count - 1]).sqrMagnitude > 1e-4f) pts.Add(p);
            var o = new List<Vector2>(pts.Count);
            for (int k = 0; k < pts.Count; k++)
            {
                var d = pts[Mathf.Min(k + 1, pts.Count - 1)] - pts[Mathf.Max(k - 1, 0)];
                o.Add(d.sqrMagnitude > 1e-8f ? pts[k] + Geo.Left(d.normalized) * inset : pts[k]);
            }
            return o;
        }

        /// <summary>A road's end at a node: a stop line before a runway, a give-way marking before a taxiway.</summary>
        void RoadEnd(Joint j, Arm arm, float room, PaintBuilder paint)
        {
            var u = arm.dir;
            var across = Geo.Right(u);
            float hw = arm.hw;
            var r = j.node.runway;
            if (r != null)
            {
                float sin = Mathf.Max(Mathf.Abs(Geo.Cross(u, r.Axis2D)), 0.15f);
                float t = RunwayRules.HoldingDistance(r.width) / sin;
                if (t + 1f < room) paint.Box(j.p + u * t, across, 0.25f, hw - RoadEdgeInset, PaintBuilder.White);
                return;
            }
            float g = GiveWayAt(j, arm);
            if (g <= 0f || g + 10f > room) return;
            // approaching the node a vehicle drives along -u, keeping right: its lane is on the left of u
            var lane = Geo.Left(u);
            for (int k = 0; k < 2; k++)
            {
                var c = j.p + u * (g + k * 0.6f);
                paint.Dashes(c, c + lane * (hw - RoadEdgeInset), 0.15f, 0.6f, 0.3f, PaintBuilder.White, true);
            }
            // the give-way triangle in the approach lane, its point towards the oncoming driver
            var mid = j.p + lane * (hw * 0.5f);
            var b0 = mid + u * (g + 3f) + lane * 0.9f;
            var b1 = mid + u * (g + 3f) - lane * 0.9f;
            var apex = mid + u * (g + 7.5f);
            paint.Strip(new[] { b0, b1, apex, b0 }, 0.12f, PaintBuilder.White, true);
        }

        /// <summary>Where an edge's straight centreline paint stops at a node, in metres from that node.</summary>
        float PaintEnd(Joint j, Arm arm)
        {
            var r = j.node.runway;
            if (r != null)
            {
                float sin = Mathf.Abs(Geo.Cross(arm.dir, r.Axis2D));
                return Mathf.Min((r.HalfWidthTotal + 1f) / Mathf.Max(sin, 0.15f), arm.length);
            }
            if (j.arms.Count == 1) return 1f;
            return arm.cut;
        }

        /// <summary>Runway-holding position marking (two solid, two dashed lines; dashes on the runway side)
        /// and, for precision-lit runways, a red stop bar.</summary>
        void Holding(Joint j, Arm arm, float room, PaintBuilder paint, LightBuilder lights)
        {
            var r = j.node.runway;
            if (r == null) return;
            float sin = Mathf.Max(Mathf.Abs(Geo.Cross(arm.dir, r.Axis2D)), 0.15f);
            float t = RunwayRules.HoldingDistance(r.width) / sin;
            if (t + 1.5f > room) return;
            var u = arm.dir;
            var across = Geo.Right(u);
            var yellow = PaintBuilder.Yellow;
            float hw = arm.hw;
            var c = j.p + u * t;
            paint.Dashes(c - across * hw, c + across * hw, 0.075f, 0.9f, 0.9f, yellow);
            paint.Dashes(c + u * 0.3f - across * hw, c + u * 0.3f + across * hw, 0.075f, 0.9f, 0.9f, yellow);
            paint.Box(c + u * 0.6f, across, 0.075f, hw, yellow);
            paint.Box(c + u * 0.9f, across, 0.075f, hw, yellow);
            if (r.lighting != RunwayLighting.Precision) return;
            int n = Mathf.Max(3, Mathf.FloorToInt(arm.edge.width / 3f) + 1);
            for (int i = 0; i < n; i++)
            {
                var p = c - u * 0.6f + across * Mathf.Lerp(-hw + 0.5f, hw - 0.5f, i / (float)(n - 1));
                lights.Light(p, 0.05f, u, LightBuilder.Off, LightBuilder.Red, 0.9f);
                lights.Inset(p, AirfieldHeights.Taxiway, LightBuilder.Red);
            }
        }

        void BuildCurve(Joint j, SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            var e = j.arms[0].edge;
            float hw = e.width * 0.5f;
            bool unpaved = !e.surface.IsPaved();
            // the arc runs in from arm 0 and out along arm 1: its ends sit square to the straights
            var d0 = -j.arms[0].dir;
            var d1 = j.arms[1].dir;
            int first = surface.mesh.Count;
            surface.Strip(Sub(e.surface), j.arc, hw, e.IsRoad ? AirfieldHeights.Road : AirfieldHeights.Taxiway, e.IsRoad?AirfieldKind.Road:AirfieldKind.Taxiway, 0f,
                unpaved ? SurfaceBuilder.Fringe : 0f, d0, d1);
            if (e.IsRoad && !unpaved && j.arms.Any(a=>a.edge.structure==RoadStructure.Ground))
                RoadStripEdges(surface,e.surface,first,surface.mesh.Count);
            if (unpaved) return;
            paint.frame = RunwayFrame.None;
            if (e.IsRoad)
            {
                foreach (float sgn in new[] { -1f, 1f })
                    paint.Strip(Offset(j.arc, sgn * (hw - RoadEdgeInset), d0, d1), RoadLineHalfWidth, PaintBuilder.White, true);
                // the bend's part of the stretch that runs through it from the roads either side
                RoadStretch(j, out float before, out float bend, out float total);
                var at = Geo.Along(j.arc);
                paint.Dashes((a, b) => Geo.Slice(j.arc, at, a - before, b - before), before, before + bend, 0f, total, RoadLineHalfWidth, 3f, 6f,
                    PaintBuilder.White, true);
                return;
            }
            paint.Strip(j.arc, CentrelineHalfWidth, PaintBuilder.Yellow);
            if (!(j.arms[0].edge.lit && j.arms[1].edge.lit)) return;
            foreach (float sgn in new[] { -1f, 1f })
                LightsAlong(lights, Offset(j.arc, sgn * (hw + LightOffset), d0, d1), CurveLightSpacing, false);
        }

        void BuildJunction(Joint j, SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            // the junction is paved like most of its taxiways (ties: the better surface), so a concrete link
            // doesn't turn a stretch of an asphalt taxiway into concrete
            var count = new int[Surfaces.Count];
            foreach (var a in j.arms)
                if (a.Real) count[(int)a.edge.surface]++;
            var kind = PavementSurface.Concrete;
            foreach (var s in new[] { PavementSurface.Asphalt, PavementSurface.Grass, PavementSurface.Dirt })
                if (count[(int)s] > count[(int)kind]) kind = s;
            // only roads meeting: as low as the roads (under aprons); anything with a taxiway or a runway in it
            // stays at taxiway height
            bool roads = j.node.runway == null && j.arms.All(a => a.Road);
            surface.Fan(Sub(kind), j.p, j.outline, roads ? AirfieldHeights.Road : AirfieldHeights.Taxiway, roads?AirfieldKind.Road:AirfieldKind.Junction,
                kind.IsPaved() ? 0f : SurfaceBuilder.Fringe);
            if (!kind.IsPaved()) return;
            if (RoadJunction(j))
            {
                if (roads)
                    for (int i = 0; i < j.arms.Count; i++)
                    {
                        var a = j.arms[i];var b = j.arms[(i+1)%j.arms.Count];
                        if (a.edge.structure!=RoadStructure.Ground && b.edge.structure!=RoadStructure.Ground) continue;
                        var line = new List<Vector2> { j.p + a.dir*a.cut + Geo.Left(a.dir)*a.hw };
                        line.AddRange(a.gap);
                        line.Add(j.p + b.dir*b.cut + Geo.Right(b.dir)*b.hw);
                        RoadOutlineEdge(surface,kind,line,false);
                    }
                RoadJunctionPaint(j, paint);
                return;
            }

            // lead-in lines between every pair of taxiways a turn can link (not through a runway junction,
            // where the centrelines run straight to the runway edge)
            paint.frame = RunwayFrame.None;
            if (j.node.runway == null)
                for (int i = 0; i < j.arms.Count; i++)
                    for (int k = i + 1; k < j.arms.Count; k++)
                    {
                        var a = j.arms[i];
                        var b = j.arms[k];
                        if (!a.Real || !b.Real || a.Road || b.Road || !a.edge.surface.IsPaved() || !b.edge.surface.IsPaved()) continue;
                        if (Vector2.Angle(a.dir, b.dir) < 50f) continue;
                        var pa = j.p + a.dir * a.cut;
                        var pb = j.p + b.dir * b.cut;
                        var pts = new List<Vector2>();
                        for (int s = 0; s <= 16; s++)
                        {
                            float t = s / 16f;
                            pts.Add((1 - t) * (1 - t) * pa + 2 * (1 - t) * t * j.p + t * t * pb);
                        }
                        paint.Strip(pts, CentrelineHalfWidth, PaintBuilder.Yellow);
                    }

            // edge lights along each gap's outline, where the taxiways on both sides are lit (or one is, next
            // to a runway)
            int n = j.arms.Count;
            for (int i = 0; i < n; i++)
            {
                var a = j.arms[i];
                var b = j.arms[(i + 1) % n];
                bool lit = (a.Real ? a.edge.lit : b.Real && b.edge.lit) && (b.Real ? b.edge.lit : a.Real && a.edge.lit);
                if (!lit) continue;
                var line = new List<Vector2> { j.p + a.dir * a.cut + Geo.Left(a.dir) * a.hw };
                line.AddRange(a.gap);
                line.Add(j.p + b.dir * b.cut + Geo.Right(b.dir) * b.hw);
                var offset = new List<Vector2>();
                for (int k = 0; k < line.Count; k++)
                {
                    var d = (line[Mathf.Min(k + 1, line.Count - 1)] - line[Mathf.Max(k - 1, 0)]);
                    offset.Add(d.sqrMagnitude > 1e-8f ? line[k] + Geo.Right(d.normalized) * LightOffset : line[k]);
                }
                LightsAlong(lights, offset, CurveLightSpacing, true);
            }
        }

        /// <summary>A line x metres to the right of an arc, whose ends run along d0 / d1 (see SurfaceBuilder.Strip).</summary>
        static List<Vector2> Offset(List<Vector2> line, float x, Vector2 d0, Vector2 d1)
        {
            var o = new List<Vector2>(line.Count);
            for (int i = 0; i < line.Count; i++)
            {
                var d = i == 0 ? d0 : i == line.Count - 1 ? d1 : line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                o.Add(d.sqrMagnitude > 1e-8f ? line[i] + Geo.Right(d.normalized) * x : line[i]);
            }
            return o;
        }

        // ------------------------------------------------------------------ verges

        /// <summary>
        /// Mown grass either side of every paved taxiway (TaxiwayEdge.HasVerges), out to its graded strip
        /// (RunwayRules.TaxiwayVerge), one owner per edge. Each band stops where it would run into a crossing
        /// taxiway's verge or a runway's, so verges never overlap; bends and junctions leave small unmown corners.
        /// </summary>
        public void BuildVerges(SurfaceBuilder verges)
        {
            foreach (var e in net.edges)
            {
                if (!e.HasVerges) continue;
                var ja = joints[e.a];
                var jb = joints[e.b];
                var armA = ja.ArmOf(e);
                var armB = jb.ArmOf(e);
                if (armA == null || armB == null) continue;
                float len = armA.length;
                float v = e.VergeHalfWidth;
                float s0 = VergeTrim(ja, armA, v), s1 = len - VergeTrim(jb, armB, v);
                if (s1 - s0 < 2f) continue;
                verges.mesh.owner = vergeOwners.Count;
                vergeOwners.Add(e);
                verges.state = new SurfaceValues { grass = e.vergeGrass, work = e.vergeWork != null ? e.vergeWork.ShaderState : e.vergeStripes };
                foreach (int side in new[] { -1, 1 })
                    foreach (var (p0, p1) in Unfronted(e, side, s0, s1))
                        if (p1 - p0 >= 2f)
                            verges.Verges(0, ja.p, armA.dir, p0, p1, e.width * 0.5f, v, AirfieldHeights.TaxiwayVerge, side);
            }
        }

        /// <summary>How far from a node an arm's verges must stop to stay clear of the other arms' verges (and a
        /// runway's): straight continuations don't count, they just meet end to end.</summary>
        float VergeTrim(Joint j, Arm arm, float v)
        {
            float t = 0f;
            foreach (var b in j.arms)
            {
                if (b == arm || Vector2.Angle(arm.dir, b.dir) > 155f) continue;
                float reach = !b.Real ? (j.node.runway != null ? j.node.runway.VergeHalfWidth : b.hw)
                    : b.edge.HasVerges ? b.edge.VergeHalfWidth : b.hw + 1f;
                float sin = Mathf.Max(Mathf.Abs(Geo.Cross(arm.dir, b.dir)), 0.25f);
                float cos = Mathf.Abs(Vector2.Dot(arm.dir, b.dir));
                t = Mathf.Max(t, (reach + v * cos) / sin);
            }
            return Mathf.Min(t, 0.5f * arm.length);
        }

        // ------------------------------------------------------------------ lights

        void LightsAlong(LightBuilder lights, List<Vector2> line, float spacing, bool includeEnds)
        {
            float total = 0f;
            for (int i = 1; i < line.Count; i++) total += (line[i] - line[i - 1]).magnitude;
            if (total < 0.5f) return;
            int n = Mathf.Max(1, Mathf.CeilToInt(total / spacing));
            for (int k = includeEnds ? 0 : 1; k <= (includeEnds ? n : n - 1); k++)
            {
                float target = total * k / n, s = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    float seg = (line[i] - line[i - 1]).magnitude;
                    if (s + seg >= target || i == line.Count - 1)
                    {
                        float f = seg > 1e-5f ? Mathf.Clamp01((target - s) / seg) : 0f;
                        EdgeLight(lights, Vector2.Lerp(line[i - 1], line[i], f));
                        break;
                    }
                    s += seg;
                }
            }
        }

        void EdgeLight(LightBuilder lights, Vector2 p)
        {
            foreach (var r in runways)
                if (r.Contains(Geo.X0Z(p), 2f)) return;
            lights.Light(p, 0.35f, Vector2.zero, LightBuilder.Blue, LightBuilder.Blue, 0.85f);
            lights.Elevated(p, AirfieldHeights.Taxiway, LightBuilder.Blue);
        }
    }
}
