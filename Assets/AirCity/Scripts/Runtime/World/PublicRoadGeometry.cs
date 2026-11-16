using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Own 3D public-road strips, bends, junctions, lane markings, roadbeds and elevated structures. A node joining
    /// two sections of the same road becomes a curve (radius by style, shortened to fit the sections); any other node
    /// becomes a junction shaped by the shared JunctionShape (sections cut back clear of each other, kerb fillets,
    /// flares where widths differ), so any angle between roads draws cleanly.</summary>
    public sealed class PublicRoadGeometry
    {
        /// <summary>Maximum terrain extent of country-road cuts and filled embankments. Local terrain updates include it.</summary>
        public const float EarthworkReach = 192f;
        public readonly struct Link
        {
            public readonly Vector2 a, b;
            public readonly float heightA, heightB, halfWidth;
            public readonly RoadStructure structure;
            public Link(Vector3 a, Vector3 b, float halfWidth, RoadStructure structure)
            { this.a = PublicRoadNetwork.XZ(a); this.b = PublicRoadNetwork.XZ(b); heightA = a.y; heightB = b.y; this.halfWidth = halfWidth; this.structure = structure; }
        }
        sealed class Arm
        {
            public PublicRoadSegment edge;
            public Vector2 direction;
            public float halfWidth, length, cut, need;
            /// <summary>Junctions: the kerb from this arm's left mouth corner round to the next arm's right one.</summary>
            public List<Vector2> kerb;
        }
        sealed class Junction
        {
            public PublicRoadNode node;
            public readonly List<Arm> arms = new List<Arm>();
            public bool IsJunction, IsCurve;
            /// <summary>Junctions: false where a section is too short for what meets it (JunctionShape).</summary>
            public bool fits = true;
            /// <summary>Curves: the centreline from arms[0]'s cut round to arms[1]'s.</summary>
            public List<Vector2> arc;
            public List<Vector2> outline;
            public Vector2 p => PublicRoadNetwork.XZ(node.position);
        }
        public readonly List<Link> links = new List<Link>();
        public readonly List<BuiltSurfaceMap.Triangle> groundTriangles = new List<BuiltSurfaceMap.Triangle>();
        public readonly PublicRoadNetwork graph;
        /// <summary>Kerbs (raised concrete edges, sidewalks' fronts, island edges) and grass on raised medians.</summary>
        readonly Mesh[] meshes=new Mesh[6];
        readonly bool[] combined=new bool[6];
        IReadOnlyList<PublicRoadGeometry> pieces;
        public Mesh pavement {get=>MeshAt(0);private set=>meshes[0]=value;}
        public Mesh shoulders {get=>MeshAt(1);private set=>meshes[1]=value;}
        public Mesh markings {get=>MeshAt(2);private set=>meshes[2]=value;}
        public Mesh structures {get=>MeshAt(3);private set=>meshes[3]=value;}
        public Mesh kerbMesh {get=>MeshAt(4);private set=>meshes[4]=value;}
        public Mesh medianGrass {get=>MeshAt(5);private set=>meshes[5]=value;}
        public int SignalJunctionCount { get; private set; }
        readonly Dictionary<int, Junction> junctions = new Dictionary<int, Junction>();
        readonly PublicRoadProfile rural, airport;
        readonly Func<Vector2,float> groundHeight;
        readonly MeshData surface = new MeshData(), shoulder = new MeshData();
        readonly PaintBuilder paint = new PaintBuilder();
        readonly LandscapeMesh concrete = new LandscapeMesh(), kerbs = new LandscapeMesh(), verges = new LandscapeMesh();
        Rect[] roadbedBounds;
        readonly Dictionary<int, float> groundCeilings = new Dictionary<int, float>();
        readonly Dictionary<int, float> groundFloors = new Dictionary<int, float>();

        public PublicRoadGeometry(PublicRoadNetwork graph, PublicRoadProfile rural, PublicRoadProfile airport, Func<Vector2, float> groundHeight, ISet<int> segmentIds=null, ISet<int> junctionIds=null, PublicRoadGeometry projectionContext=null,bool generateMeshes=true)
        {
            this.graph = graph; this.rural = rural; this.airport = airport;this.groundHeight=groundHeight;
            foreach (var node in graph.nodes)
            {
                var joint = new Junction { node = node };
                foreach (var edge in graph.At(node.id))
                {
                    var other = graph.Node(edge.a == node.id ? edge.b : edge.a);
                    float width = Width(edge);
                    var path=graph.Path(edge);var tangent=path.Tangent(edge.a==node.id?0f:1f)*(edge.a==node.id?1f:-1f);
                    var delta = new Vector2(tangent.x,tangent.z)*path.Length;
                    if (delta.magnitude < .01f) continue;
                    joint.arms.Add(new Arm { edge = edge, direction = delta.normalized, halfWidth = width * .5f, length = delta.magnitude });
                }
                joint.arms.Sort((a, b) => Mathf.Atan2(a.direction.y, a.direction.x).CompareTo(Mathf.Atan2(b.direction.y, b.direction.x)));
                if (joint.arms.Count >= 2)
                {
                    var need = JunctionShape.Needs(joint.arms.Select(a => new JunctionArm(a.direction, a.halfWidth, a.length, a.length)).ToList());
                    for (int i = 0; i < joint.arms.Count; i++) joint.arms[i].need = need[i];
                }
                if (node.control == JunctionControl.TrafficLights && joint.arms.Count >= 3) SignalJunctionCount++;
                junctions.Add(node.id, joint);
            }
            // laid out once every node's needs are known: each end of a section gets its share (JunctionShape.Reach)
            foreach (var joint in junctions.Values)
            {
                if (joint.arms.Count == 2 && StraightOn(joint)) { }
                else if (joint.arms.Count == 2 && TryCurve(joint)) joint.IsCurve = true;
                else if (joint.arms.Count >= 2) Shape(joint);
            }
            foreach (var edge in graph.segments)
            {
                if(segmentIds!=null && !segmentIds.Contains(edge.id))continue;
                var a = graph.Node(edge.a).position; var b = graph.Node(edge.b).position;
                var path=graph.Path(edge);var axis = PublicRoadNetwork.XZ(b - a); float length = path.Length;
                float start = ArmAt(edge.a, edge.id).cut, end = length - ArmAt(edge.b, edge.id).cut;
                if (length > .01f && end > start)
                {
                    int count=path.shape.curved||path.shape.smooth?Mathf.Max(1,Mathf.CeilToInt((end-start)/8f)):1;
                    for(int i=0;i<count;i++)
                    {
                        var p=Alignment3D.XZ(path.AtDistance(Mathf.Lerp(start,end,i/(float)count)));
                        var q=Alignment3D.XZ(path.AtDistance(Mathf.Lerp(start,end,(i+1f)/count)));
                        links.Add(new Link(World(p,Elevation(edge,p)),World(q,Elevation(edge,q)),Width(edge)*.5f+ShoulderWidth(edge),edge.structure));
                    }
                }
                int first = surface.Count, firstShoulder = shoulder.Count;
                BuildSegment(edge, groundHeight);
                if (edge.structure == RoadStructure.Ground) RememberGround(first, firstShoulder);
            }
            foreach (var junction in junctions.Values)
            {
                if(junctionIds!=null && !junctionIds.Contains(junction.node.id))continue;
                int first = surface.Count, firstShoulder = shoulder.Count;
                if (junction.IsJunction) BuildJunction(junction);
                else if (junction.IsCurve) BuildCurve(junction);
                if (junction.arms.Any(a => a.edge.structure == RoadStructure.Ground)) RememberGround(first, firstShoulder);
            }
            roadbedBounds = links.Where(l => l.structure == RoadStructure.Ground).Select(l => Rect.MinMaxRect(Mathf.Min(l.a.x, l.b.x) - EarthworkReach,
                Mathf.Min(l.a.y, l.b.y) - EarthworkReach, Mathf.Max(l.a.x, l.b.x) + EarthworkReach, Mathf.Max(l.a.y, l.b.y) + EarthworkReach)).ToArray();
            if(!generateMeshes)return;
            ProjectPaintOntoPavement(projectionContext);
            for(int i=0;i<surface.Count;i++)surface.uv1[i]=new Vector4((float)AirfieldKind.Road,-1f);
            pavement = surface.Apply(null, "Public road pavement"); pavement.RecalculateNormals();
            shoulders = shoulder.Apply(null, "Public road shoulders"); shoulders.RecalculateNormals();
            markings = paint.mesh.Apply(null, "Public road lane markings", true, 4);
            structures = concrete.Create("Public road bridge structures");
            kerbMesh = kerbs.Create("Public road kerbs");
            medianGrass = verges.Create("Public road median grass");
        }

        // A composed logical view reuses CPU formations and triangle footprints. Full combined meshes are made
        // lazily for exports/diagnostics; the game renders the independently cached sections directly.
        internal static PublicRoadGeometry Compose(PublicRoadNetwork graph,PublicRoadProfile rural,PublicRoadProfile airport,
            Func<Vector2,float> height,IReadOnlyList<PublicRoadGeometry> parts)
        {
            var empty=new HashSet<int>();var result=new PublicRoadGeometry(graph,rural,airport,height,empty,empty,null,false) {pieces=parts};
            foreach(var part in parts)
            {
                result.links.AddRange(part.links);result.groundTriangles.AddRange(part.groundTriangles);
                foreach(var item in part.groundCeilings)
                    if(!result.groundCeilings.TryGetValue(item.Key,out float old)||item.Value<old)result.groundCeilings[item.Key]=item.Value;
                foreach(var item in part.groundFloors)
                    if(!result.groundFloors.TryGetValue(item.Key,out float old)||item.Value>old)result.groundFloors[item.Key]=item.Value;
            }
            result.roadbedBounds=result.links.Where(l=>l.structure==RoadStructure.Ground).Select(l=>Rect.MinMaxRect(Mathf.Min(l.a.x,l.b.x)-EarthworkReach,
                Mathf.Min(l.a.y,l.b.y)-EarthworkReach,Mathf.Max(l.a.x,l.b.x)+EarthworkReach,Mathf.Max(l.a.y,l.b.y)+EarthworkReach)).ToArray();return result;
        }
        Mesh MeshAt(int index)
        {
            if(pieces==null || combined[index])return meshes[index];
            var combine=pieces.Select(p=>p.MeshAt(index)).Where(m=>m!=null&&m.vertexCount>0)
                .Select(m=>new CombineInstance {mesh=m,transform=Matrix4x4.identity}).ToArray();
            var mesh=new Mesh {name="Combined public roads",hideFlags=HideFlags.DontSave,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            if(combine.Length>0)mesh.CombineMeshes(combine,true,false);meshes[index]=mesh;combined[index]=true;return mesh;
        }
        internal void DestroyMeshes()
        {
            for(int i=0;i<meshes.Length;i++)if(meshes[i]!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(meshes[i]);else UnityEngine.Object.DestroyImmediate(meshes[i]);meshes[i]=null;}
        }
        bool ProjectPaint(Vector2 point,float hint,out float height,out Vector2 gradient)
        {
            if(paintProjection.Project(point,hint,out height,out gradient))return true;
            if(pieces!=null)foreach(var piece in pieces)if(piece.ProjectPaint(point,hint,out height,out gradient))return true;
            height=0;gradient=Vector2.zero;return false;
        }

        /// <summary>Kerb to kerb (RoadTypes; the country and access roads' profiles may set theirs by hand).</summary>
        float Width(PublicRoadSegment edge) =>
            edge.style == PublicRoadStyle.AirportAccess && airport != null ? airport.pavementWidth :
            edge.style == PublicRoadStyle.Rural && rural != null ? rural.pavementWidth : edge.Width;
        /// <summary>The verge outside the kerb or carriageway edge: gravel shoulder, or sidewalk where the type has kerbs.</summary>
        float ShoulderWidth(PublicRoadSegment edge) =>
            edge.style == PublicRoadStyle.AirportAccess && airport != null ? airport.shoulderWidth :
            edge.style == PublicRoadStyle.Rural && rural != null ? rural.shoulderWidth : edge.Type.verge;
        float Crown(PublicRoadSegment edge) => edge.style == PublicRoadStyle.AirportAccess && airport != null ? airport.crown : rural != null ? rural.crown : .065f;
        /// <summary>A road's type, its width as drawn (a profile may widen a country or access road's lanes).</summary>
        RoadType TypeOf(PublicRoadSegment edge) => edge.Type;
        Arm ArmAt(int node, int edge) => junctions[node].arms.Find(a => a.edge.id == edge);

        /// <summary>Junctions where a section is too short for the roads that meet it (their strips would overlap).</summary>
        public IEnumerable<PublicRoadNode> MisfitJunctions => junctions.Values.Where(j => j.IsJunction && !j.fits).Select(j => j.node);

        /// <summary>Centreline radius a bend of this style is drawn with, where the sections leave room for it.</summary>
        public static float CurveRadius(PublicRoadStyle style) => RoadTypes.Of(style).curveRadius;

        /// <summary>Kerb radius between two roads at a junction: the larger of the two types'.</summary>
        static float KerbRadius(Arm a, Arm b) => Mathf.Max(a.edge.Type.kerbRadius, b.edge.Type.kerbRadius);

        /// <summary>Two sections of the same width carrying straight on: they simply meet.</summary>
        static bool StraightOn(Junction j) =>
            Mathf.Abs(j.arms[0].halfWidth - j.arms[1].halfWidth) < .01f && Vector2.Angle(j.arms[0].direction, j.arms[1].direction) > 179.5f;

        /// <summary>A bend between two sections of the same road: an arc of the style's radius, shortened to fit (the
        /// sections' other ends need room too). False where the road turns back too sharply or the arc would be too
        /// tight for its width, or the sections differ (a junction shape tapers between them instead).</summary>
        bool TryCurve(Junction j)
        {
            Arm a0 = j.arms[0], a1 = j.arms[1];
            if (a0.edge.alignment.curved || a1.edge.alignment.curved || a0.edge.style != a1.edge.style || a0.edge.structure != a1.edge.structure || Mathf.Abs(a0.halfWidth - a1.halfWidth) > .01f) return false;
            float alpha = Vector2.Angle(a0.direction, a1.direction) * Mathf.Deg2Rad;
            if (alpha < 25f * Mathf.Deg2Rad) return false;
            float radius = CurveRadius(a0.edge.style), tanHalf = Mathf.Tan(alpha * .5f);
            float cut = radius / tanHalf, room = Mathf.Min(Reach(j, a0), Reach(j, a1));
            if (cut > room) { cut = room; radius = cut * tanHalf; }
            if (radius < a0.halfWidth * 1.25f) return false;
            Geo.Fillet(j.p, a0.direction, a1.direction, radius, out var centre);
            j.arc = new List<Vector2>();
            Geo.Arc(j.arc, centre, j.p + a0.direction * cut, j.p + a1.direction * cut, 2f);
            a0.cut = a1.cut = cut;
            return true;
        }

        /// <summary>How far along an arm this node's junction or bend may reach, sharing the section with its other end.</summary>
        float Reach(Junction j, Arm a)
        {
            int other = a.edge.a == j.node.id ? a.edge.b : a.edge.a;
            float there = junctions.TryGetValue(other, out var far) ? far.arms.Find(x => x.edge.id == a.edge.id)?.need ?? 0f : 0f;
            return JunctionShape.Reach(a.length, a.need, there);
        }

        /// <summary>Lay out a junction with JunctionShape: the arms' cuts, kerbs and outline.</summary>
        void Shape(Junction j)
        {
            j.IsJunction = true;
            var shape = j.arms.Select(a => new JunctionArm(a.direction, a.halfWidth, a.length, Reach(j, a))).ToList();
            j.outline = JunctionShape.SolveCached(j.p, shape, (i, k) => KerbRadius(j.arms[i], j.arms[k]), out j.fits);
            for (int i = 0; i < j.arms.Count; i++)
            {
                j.arms[i].cut = shape[i].cut;
                j.arms[i].kerb = new List<Vector2>(shape[i].kerb);
            }
        }
        static Vector3 World(Vector2 p, float y) => new Vector3(p.x, y, p.y);
        static Vector2 Left(Vector2 p) => new Vector2(-p.y, p.x);
        static Vector2 Right(Vector2 p) => new Vector2(p.y, -p.x);

        readonly struct HeightProfile
        {
            public readonly Vector2 a,b,delta;
            public readonly Alignment3D path;
            public readonly float h0,h1,inverseLength,cutA,cutB;
            public readonly bool flatA,flatB;
            public HeightProfile(Vector3 a,Vector3 b,Junction ja,Junction jb,Arm aa,Arm ab,Alignment3D path)
            {
                this.path=path;this.a=PublicRoadNetwork.XZ(a);this.b=PublicRoadNetwork.XZ(b);delta=this.b-this.a;
                h0=a.y;h1=b.y;inverseLength=1f/Mathf.Max(.000001f,delta.sqrMagnitude);
                cutA=aa.cut;cutB=ab.cut;flatA=ja.IsJunction;flatB=jb.IsJunction;
            }
            public float Height(Vector2 p)
            {
                float h=path.Height(path.ClosestParameter(p));
                if(flatA)h=Mathf.Lerp(h,h0,1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(cutA,cutA+20f,Vector2.Distance(p,a))));
                if(flatB)h=Mathf.Lerp(h,h1,1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(cutB,cutB+20f,Vector2.Distance(p,b))));
                return h;
            }
        }
        readonly Dictionary<int,HeightProfile> heightProfiles=new Dictionary<int,HeightProfile>();
        float Elevation(PublicRoadSegment edge,Vector2 p)
        {
            if(!heightProfiles.TryGetValue(edge.id,out var profile))
            {
                profile=new HeightProfile(graph.Node(edge.a).position,graph.Node(edge.b).position,junctions[edge.a],junctions[edge.b],ArmAt(edge.a,edge.id),ArmAt(edge.b,edge.id),graph.Path(edge));
                heightProfiles.Add(edge.id,profile);
            }
            return profile.Height(p);
        }

        void BuildSegment(PublicRoadSegment edge, Func<Vector2, float> groundHeight)
        {
            var start = graph.Node(edge.a).position; var end = graph.Node(edge.b).position;
            var a = PublicRoadNetwork.XZ(start); var b = PublicRoadNetwork.XZ(end);
            var path=graph.Path(edge);var delta = b - a; float length = path.Length;
            if (length < .01f) return;
            var axis = delta.normalized; var right = Right(axis);
            float s0 = ArmAt(edge.a, edge.id).cut, s1 = length - ArmAt(edge.b, edge.id).cut;
            float half = Width(edge) * .5f, verge = ShoulderWidth(edge);
            bool kerbed = edge.Type.edge == RoadEdge.Kerb;
            bool roundA=RoundEnd(edge.a,edge),roundB=RoundEnd(edge.b,edge);
            float radius=Mathf.Min(3f,half*.55f,(s1-s0)*.2f);
            if(roundA)s0+=radius;if(roundB)s1-=radius;
            if(roundA)BuildEnd(edge,edge.a,-Alignment3D.XZ(path.Tangent(0)).normalized,half,verge,radius);
            if(roundB)BuildEnd(edge,edge.b,Alignment3D.XZ(path.Tangent(1)).normalized,half,verge,radius);
            int steps = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 5f));
            float[] xs = { -half, 0f, half };
            for (int step = 0; step < steps; step++)
            {
                float sa = Mathf.Lerp(s0, s1, step / (float)steps), sb = Mathf.Lerp(s0, s1, (step + 1f) / steps);
                Vector3 At(float s, float x, float extra = 0f) {
                    var p = Alignment3D.XZ(path.Offset(s,x,ArmAt(edge.a,edge.id).cut,ArmAt(edge.b,edge.id).cut));
                    float crossfall = Crown(edge) * Mathf.Clamp01(1f - Mathf.Abs(x) / half);
                    return World(p, Elevation(edge, p) + .18f + crossfall + extra);
                }
                for (int col = 0; col < 2; col++) Quad(surface, At(sa, xs[col]), At(sb, xs[col]), At(sb, xs[col + 1]), At(sa, xs[col + 1]));
                for(int side=-1;side<=1;side+=2)
                {
                    float inner = half * side, outer = (half + verge) * side;
                    if (kerbed)
                    {
                        // a kerb face up to the sidewalk, the sidewalk level at kerb height
                        float back = inner + KerbWidth * side;
                        Quad(kerbs, At(sa, inner, KerbHeight), At(sb, inner, KerbHeight), At(sb, inner), At(sa, inner));
                        Quad(kerbs, At(sa, inner, KerbHeight), At(sb, inner, KerbHeight), At(sb, back, KerbHeight), At(sa, back, KerbHeight));
                        Quad(shoulder, At(sa, back, KerbHeight), At(sb, back, KerbHeight), At(sb, outer, KerbHeight), At(sa, outer, KerbHeight));
                    }
                    else Quad(shoulder, At(sa, inner, -.035f), At(sb, inner, -.035f), At(sb, outer, -.09f), At(sa, outer, -.09f));
                }
            }
            paint.frame = RunwayFrame.Along(a, axis);
            int first = paint.mesh.Count;
            // lane and centre lines stop at the stop lines (beyond them is the crosswalk and junction)
            float stopA = Mathf.Max(s0, StopLine(edge.a, edge.id)), stopB = Mathf.Min(s1, length - StopLine(edge.b, edge.id));
            if (stopB < stopA) stopA = stopB = (s0 + s1) * .5f;
            Markings(edge, half, s0, s1, stopA, stopB, IsJunctionAt(edge.a), IsJunctionAt(edge.b),
                (x,f,t)=>
                {
                    int count=Mathf.Max(1,Mathf.CeilToInt((t-f)/2f));var points=new List<Vector2>(count+1);
                    for(int i=0;i<=count;i++)points.Add(Alignment3D.XZ(path.Offset(Mathf.Lerp(f,t,i/(float)count),x,ArmAt(edge.a,edge.id).cut,ArmAt(edge.b,edge.id).cut)));return points;
                });
            Median(edge, s1 - s0, MedianInset(edge.a, edge), MedianInset(edge.b, edge),
                (sa,sb,x0,x1,top)=>
                {
                    sa+=s0;sb+=s0;
                    MedianSlice(edge,Alignment3D.XZ(path.Offset(sa,x0)),Alignment3D.XZ(path.Offset(sa,x1)),Alignment3D.XZ(path.Offset(sb,x0)),Alignment3D.XZ(path.Offset(sb,x1)),
                        Elevation(edge,Alignment3D.XZ(path.AtDistance(sa)))+.18f,Elevation(edge,Alignment3D.XZ(path.AtDistance(sb)))+.18f,top);
                });
            DrapePaint(first, p => Elevation(edge, p) + Crown(edge) * Mathf.Clamp01(1f - path.DistanceTo(p) / half));
            RoadStructureBuilder.Build(path,edge.structure,half+verge,groundHeight,concrete,groundTriangles,
                (p,radius)=>
                {
                    foreach(var lower in graph.segments)
                        if(lower.id!=edge.id && lower.structure!=RoadStructure.Tunnel && graph.Path(lower).DistanceTo(PublicRoadNetwork.XZ(p))<lower.Width*.5f+radius &&
                            graph.Path(lower).Height(graph.Path(lower).ClosestParameter(PublicRoadNetwork.XZ(p)))<p.y-1f)return false;
                    return true;
                },s0,s1);
        }

        bool RoundEnd(int node,PublicRoadSegment edge)=>edge.structure==RoadStructure.Ground &&
            !graph.Node(node).outsideConnection && junctions[node].arms.Count==1;

        void BuildEnd(PublicRoadSegment edge,int node,Vector2 outward,float half,float verge,float radius)
        {
            var end=PublicRoadNetwork.XZ(graph.Node(node).position);var right=Right(outward);
            var inner=SurfaceBuilder.RoadEndOutline(end,outward,half,radius);
            // Offset the same sampled outline; matching subdivisions keep the curved shoulder watertight.
            var outer=new List<Vector2>();
            foreach(var p in inner)
            {
                Vector2 normal;
                float x=Vector2.Dot(p-end,right),s=Vector2.Dot(p-end,outward);
                if(Mathf.Abs(x)>half-radius+.001f)
                {var arcCentre=end-outward*radius+right*Mathf.Sign(x)*(half-radius);normal=(p-arcCentre).normalized;}
                else normal=outward;
                outer.Add(p+normal*verge);
            }
            float Height(Vector2 p)=>Elevation(edge,p)+.18f+Crown(edge)*Mathf.Clamp01(1f-Mathf.Abs(Vector2.Dot(p-end,right))/half);
            var centre=end-outward*(radius*.5f);
            for(int i=0;i<inner.Count;i++)
                Quad(surface,World(centre,Height(centre)),World(inner[i],Height(inner[i])),World(inner[(i+1)%inner.Count],Height(inner[(i+1)%inner.Count])),World(centre,Height(centre)));
            bool raised=edge.Type.edge==RoadEdge.Kerb;
            for(int i=0;i+1<inner.Count;i++)
            {
                var a=inner[i];var b=inner[i+1];var aa=outer[i];var bb=outer[i+1];
                float ya=Height(a),yb=Height(b);
                if(raised)
                {
                    Quad(kerbs,World(a,ya),World(b,yb),World(b,yb+KerbHeight),World(a,ya+KerbHeight));
                    Quad(shoulder,World(a,ya+KerbHeight),World(b,yb+KerbHeight),World(bb,yb+KerbHeight),World(aa,ya+KerbHeight));
                }
                else Quad(shoulder,World(a,ya-.035f),World(b,yb-.035f),World(bb,yb-.09f),World(aa,ya-.09f));
            }
            if(!raised)
            {
                int first=paint.mesh.Count;
                var line=SurfaceBuilder.RoadEndOutline(end-outward*.25f,outward,half-.25f,Mathf.Max(.1f,radius-.25f));
                paint.Strip(line,LineHalf,PaintBuilder.White);DrapePaint(first,p=>Height(p)-.18f);
            }
        }

        void BuildJunction(Junction junction)
        {
            var centre = junction.p;
            float y = junction.node.position.y;
            if (junction.outline.Count >= 3) Fan(surface, junction.node.position + Vector3.up * .19f, junction.outline);
            int n = junction.arms.Count;
            for (int i = 0; i < n; i++)
            {
                // gravel shoulder along each kerb, from this arm's shoulder width to the next arm's
                var arm = junction.arms[i]; var next = junction.arms[(i + 1) % n];
                var kerb = arm.kerb;
                if (kerb == null || kerb.Count < 2) continue;
                float v0 = ShoulderWidth(arm.edge), v1 = ShoulderWidth(next.edge);
                var outward = new Vector2[kerb.Count];
                for (int k = 0; k < kerb.Count; k++)
                {
                    var d = kerb[Mathf.Min(k + 1, kerb.Count - 1)] - kerb[Mathf.Max(k - 1, 0)];
                    outward[k] = k == 0 ? Left(arm.direction) : k == kerb.Count - 1 ? Right(next.direction) : d.sqrMagnitude > 1e-8f ? Right(d.normalized) : Left(arm.direction);
                }
                bool raised = arm.edge.Type.edge == RoadEdge.Kerb || next.edge.Type.edge == RoadEdge.Kerb;
                // a kerbed corner: the sidewalk level behind the kerb (KerbAlong draws the kerb on the line itself);
                // a shoulder corner: gravel falling away from the pavement edge
                float rise = raised ? .19f + KerbHeight : .145f, fall = raised ? .19f + KerbHeight : .09f, start = raised ? KerbWidth : 0f;
                for (int k = 0; k + 1 < kerb.Count; k++)
                {
                    float w0 = Mathf.Lerp(v0, v1, k / (kerb.Count - 1f)), w1 = Mathf.Lerp(v0, v1, (k + 1f) / (kerb.Count - 1f));
                    Quad(shoulder, World(kerb[k] + outward[k] * start, y + rise), World(kerb[k + 1] + outward[k + 1] * start, y + rise),
                        World(kerb[k + 1] + outward[k + 1] * w1, y + fall), World(kerb[k] + outward[k] * w0, y + fall));
                }
            }
            int first = paint.mesh.Count;
            paint.frame = RunwayFrame.None;
            for (int i = 0; i < n; i++)
            {
                var arm = junction.arms[i];
                // the kerb line: white edge paint where the roads have shoulders; a kerb (concrete) where they have kerbs
                if (arm.kerb != null && arm.kerb.Count >= 2)
                {
                    var next = junction.arms[(i + 1) % n];
                    if (arm.edge.Type.edge == RoadEdge.Kerb || next.edge.Type.edge == RoadEdge.Kerb) KerbAlong(arm.kerb, y + .19f);
                    else paint.Strip(Inset(arm.kerb, .25f), .075f, PaintBuilder.White);
                }
                if (n >= 3) Approach(junction, arm);
            }
            if (junction.node.control != JunctionControl.TrafficLights)
            {
                // a priority road straight through keeps its lane lines across the junction
                var main = junction.arms.Find(a => PublicRoadSignals.Group(graph, junction.node.id, a.edge.id) == 0);
                var opposite = main == null ? null : junction.arms.Find(a => a != main && Vector2.Dot(a.direction, main.direction) < -.9f);
                if (main != null && opposite != null && main.edge.Type.Lanes == opposite.edge.Type.Lanes && !main.edge.Type.OneWay)
                    paint.Dashes(centre + main.direction * main.cut, centre + opposite.direction * opposite.cut, .075f, 3f, 6f, PaintBuilder.Yellow);
            }
            DrapePaint(first,p=>
            {
                if(Geo.Inside(junction.outline,p) || Geo.DistanceToOutline(junction.outline,p)<.001f)return y+.01f;
                Arm best=null;float distance=float.MaxValue;
                foreach(var arm in junction.arms)
                {
                    var end=PublicRoadNetwork.XZ(graph.Node(arm.edge.a==junction.node.id?arm.edge.b:arm.edge.a).position);
                    float d=(Geo.ClosestOnSegment(p,junction.p,end,out _)-p).sqrMagnitude;
                    if(d<distance){distance=d;best=arm;}
                }
                float across=Mathf.Abs(Vector2.Dot(p-junction.p,Right(best.direction)));
                return Elevation(best.edge,p)+Crown(best.edge)*Mathf.Clamp01(1f-across/best.halfWidth);
            });
        }

        /// <summary>A bend: the road's strip, shoulders and markings carried round the arc, its height eased from one
        /// section's to the other's.</summary>
        void BuildCurve(Junction junction)
        {
            Arm a0 = junction.arms[0], a1 = junction.arms[1];
            var edge = a0.edge;
            var arc = junction.arc;
            int m = arc.Count;
            if (m < 2) return;
            float half = a0.halfWidth, verge = ShoulderWidth(edge), crown = Crown(edge);
            float h0 = Elevation(a0.edge, arc[0]), h1 = Elevation(a1.edge, arc[m - 1]);
            var along = new float[m];
            for (int k = 1; k < m; k++) along[k] = along[k - 1] + Vector2.Distance(arc[k - 1], arc[k]);
            float total = Mathf.Max(along[m - 1], 1e-4f);
            var right = new Vector2[m];
            for (int k = 0; k < m; k++)
            {
                var d = k == 0 ? -a0.direction : k == m - 1 ? a1.direction : arc[k + 1] - arc[k - 1];
                right[k] = Right(d.normalized);
            }
            Vector3 At(int k, float x, float extra = 0f)
            {
                float crossfall = crown * Mathf.Clamp01(1f - Mathf.Abs(x) / half);
                return World(arc[k] + right[k] * x, Mathf.Lerp(h0, h1, along[k] / total) + .18f + crossfall + extra);
            }
            // the arc is road too: roadbed grading, ground texture and ground cover follow it, not the cut-off corner
            for (int k = 0; k + 1 < m; k++)
                links.Add(new Link(World(arc[k], Mathf.Lerp(h0, h1, along[k] / total)), World(arc[k + 1], Mathf.Lerp(h0, h1, along[k + 1] / total)),
                    half + verge, edge.structure));
            float[] xs = { -half, 0f, half };
            for (int k = 0; k + 1 < m; k++)
            {
                for (int col = 0; col < 2; col++) Quad(surface, At(k, xs[col]), At(k + 1, xs[col]), At(k + 1, xs[col + 1]), At(k, xs[col + 1]));
                foreach (float side in new[] { -1f, 1f })
                    if (edge.Type.edge == RoadEdge.Kerb)
                    {
                        float back = (half + KerbWidth) * side;
                        Quad(kerbs, At(k, half * side, KerbHeight), At(k + 1, half * side, KerbHeight), At(k + 1, half * side), At(k, half * side));
                        Quad(kerbs, At(k, half * side, KerbHeight), At(k + 1, half * side, KerbHeight), At(k + 1, back, KerbHeight), At(k, back, KerbHeight));
                        Quad(shoulder, At(k, back, KerbHeight), At(k + 1, back, KerbHeight), At(k + 1, (half + verge) * side, KerbHeight),
                            At(k, (half + verge) * side, KerbHeight));
                    }
                    else Quad(shoulder, At(k, half * side, -.035f), At(k + 1, half * side, -.035f), At(k + 1, (half + verge) * side, -.09f), At(k, (half + verge) * side, -.09f));
            }
            paint.frame = RunwayFrame.None;
            int first = paint.mesh.Count;
            List<Vector2> Line(float x)
            {
                var line = new List<Vector2>(m);
                for (int k = 0; k < m; k++) line.Add(arc[k] + right[k] * x);
                return line;
            }
            // a0 runs away from the node and so does the arc, starting on a0's side: x to the arc's right is a0's left
            bool flip = edge.a == junction.node.id;
            Markings(edge, half, 0f, total, 0f, total, false, false, (x, f, t) => Slice(Line(flip ? -x : x), along, f, t));
            Median(edge, total, 0f, 0f, (sa, sb, x0, x1, top) =>
            {
                for (int k = 0; k + 1 < m; k++)
                {
                    if (along[k + 1] < sa || along[k] > sb) continue;
                    float u0 = flip ? -x1 : x0, u1 = flip ? -x0 : x1;
                    float y0 = Mathf.Lerp(h0, h1, along[k] / total) + .18f, y1 = Mathf.Lerp(h0, h1, along[k + 1] / total) + .18f;
                    MedianSlice(edge, arc[k] + right[k] * u0, arc[k] + right[k] * u1, arc[k + 1] + right[k + 1] * u0, arc[k + 1] + right[k + 1] * u1, y0, y1, top);
                }
            });
            DrapePaint(first, p =>
            {
                int best = 0; float bestD = float.MaxValue;
                for (int k = 0; k < m; k++)
                {
                    float d = (arc[k] - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = k; }
                }
                float x = Mathf.Abs(Vector2.Dot(p - arc[best], right[best]));
                return Mathf.Lerp(h0, h1, along[best] / total) + crown * Mathf.Clamp01(1f - x / half);
            });
            if(edge.structure!=RoadStructure.Ground)
                for(int k=0;k+1<arc.Count;k++)
                    RoadStructureBuilder.Build(new Alignment3D(World(arc[k],Mathf.Lerp(h0,h1,along[k]/total)),World(arc[k+1],Mathf.Lerp(h0,h1,along[k+1]/total))),
                        edge.structure,half+verge,groundHeight,concrete,groundTriangles);
        }

        // ------------------------------------------------------------------ markings and furniture by road type

        const float LineHalf = .075f, KerbHeight = .15f, KerbWidth = .25f;

        /// <summary>
        /// The lane markings along a stretch of road (`line(x)`: the stretch's line at offset x from its centreline,
        /// right positive looking from node a to b): edge lines on roads with shoulders, the centre line of an undivided
        /// two-way road (dashed, or double solid on streets and wider), dashed lines between lanes running the same way,
        /// solid lines along a median's edges.
        /// </summary>
        /// <param name="line">The stretch's line at offset x (right positive looking from node a to b) between two
        /// distances along it.</param>
        void Markings(PublicRoadSegment edge, float half, float from, float to, float stopA, float stopB, bool junctionA, bool junctionB,
            Func<float, float, float, List<Vector2>> line)
        {
            var t = edge.Type;
            float scale = half / Mathf.Max(t.Width * .5f, .01f);   // a profile may widen a country road: lanes scale with it
            if (stopB - stopA < .5f) return;
            if (t.edge == RoadEdge.Shoulder)
                foreach (float side in new[] { -1f, 1f }) paint.Strip(line((half - .25f) * side, from, to), LineHalf, PaintBuilder.White);
            if (!t.OneWay && !t.Divided)
            {
                if (t.centre == CentreLine.Dashed) paint.Dashes(line(0f, stopA, stopB), LineHalf, 3f, 6f, PaintBuilder.Yellow);
                else foreach (float side in new[] { -1f, 1f }) paint.Strip(line(.15f * side, stopA, stopB), LineHalf, PaintBuilder.Yellow);
            }
            // lines between lanes running the same way: dashed, solid for the last stretch before a junction (no lane
            // changes on the approach)
            float solid = Mathf.Min(SolidApproach, (stopB - stopA) / 3f);
            float da = junctionA ? stopA + solid : stopA, db = junctionB ? stopB - solid : stopB;
            var lanes = t.LaneCentres();
            for (int i = 0; i + 1 < lanes.Count; i++)
            {
                if (lanes[i].forwards != lanes[i + 1].forwards) continue;
                float x = (lanes[i].x + lanes[i + 1].x) * .5f * scale;
                if (db - da > 1f) paint.Dashes(line(x, da, db), LineHalf, 3f, 9f, PaintBuilder.White);
                if (junctionA) paint.Strip(line(x, stopA, da), LineHalf, PaintBuilder.White);
                if (junctionB) paint.Strip(line(x, db, stopB), LineHalf, PaintBuilder.White);
            }
            if (t.Divided)
                foreach (float side in new[] { -1f, 1f })
                    paint.Strip(line((t.median * .5f + t.innerShoulder - .2f) * side * scale, stopA, stopB), LineHalf, PaintBuilder.Yellow);
        }

        /// <summary>Lane lines are solid this far back from a junction's stop line.</summary>
        const float SolidApproach = 25f;

        bool IsJunctionAt(int node) => junctions.TryGetValue(node, out var j) && j.IsJunction && j.arms.Count >= 3;

        /// <summary>The part of a polyline between two distances along it (`along`: each point's distance).</summary>
        static List<Vector2> Slice(List<Vector2> points, float[] along, float from, float to)
        {
            var result = new List<Vector2>();
            int n = points.Count;
            Vector2 At(float d)
            {
                int i = 1;
                while (i < n - 1 && along[i] < d) i++;
                float seg = along[i] - along[i - 1];
                return Vector2.Lerp(points[i - 1], points[i], seg > 1e-6f ? Mathf.Clamp01((d - along[i - 1]) / seg) : 0f);
            }
            result.Add(At(from));
            for (int i = 1; i < n - 1; i++) if (along[i] > from && along[i] < to) result.Add(points[i]);
            result.Add(At(to));
            return result;
        }

        /// <summary>The median of a divided road along a stretch `length` long: a raised kerbed island or a concrete
        /// barrier, built by `slice(from, to, x0, x1, top)` (along the stretch, across it, top height above the road).
        /// It stops short of the stretch's ends, where the junction or curve meets it, so turning traffic has room.</summary>
        void Median(PublicRoadSegment edge, float length, float insetStart, float insetEnd, Action<float, float, float, float, float> slice)
        {
            var t = edge.Type;
            if (!t.Divided || length - insetStart - insetEnd < 1f) return;
            float w = t.median * .5f, top = t.medianKind == RoadMedian.Barrier ? .8f : KerbHeight;
            const float step = 6f;
            for (float sa = insetStart; sa < length - insetEnd - 1e-3f; sa += step)
                slice(sa, Mathf.Min(sa + step, length - insetEnd), -w, w, top);
        }

        /// <summary>Where a median ends short of a junction: behind the stop line (and crosswalk), so it never covers
        /// paint and turning traffic has room; nothing at a bend or a dead end.</summary>
        float MedianInset(int node, PublicRoadSegment edge)
        {
            if (!junctions.TryGetValue(node, out var j) || !j.IsJunction) return 0f;
            var arm = j.arms.Find(a => a.edge.id == edge.id);
            return arm == null ? 0f : StopAt(arm) - arm.cut + 1f;
        }

        /// <summary>A slice of median on a straight stretch.</summary>
        void MedianQuad(PublicRoadSegment edge, Vector2 a, Vector2 axis, Vector2 right, float sa, float sb, float x0, float x1, float top)
        {
            float ya = Elevation(edge, a + axis * sa) + .18f, yb = Elevation(edge, a + axis * sb) + .18f;
            MedianSlice(edge, a + axis * sa + right * x0, a + axis * sa + right * x1, a + axis * sb + right * x0, a + axis * sb + right * x1, ya, yb, top);
        }

        /// <summary>A median block between two cross-lines (left/right at the start, left/right at the end): its top (grass
        /// on a raised island, concrete on a barrier) and its sides.</summary>
        void MedianSlice(PublicRoadSegment edge, Vector2 l0, Vector2 r0, Vector2 l1, Vector2 r1, float y0, float y1, float top)
        {
            bool barrier = edge.Type.medianKind == RoadMedian.Barrier;
            if (barrier)
            {
                // a concrete safety barrier: narrow at the top, sides to the road
                var m0 = (l0 + r0) * .5f; var m1 = (l1 + r1) * .5f;
                var d0 = (r0 - l0).normalized; var d1 = (r1 - l1).normalized;
                float foot = .35f, head = .12f;
                Concrete(m0 - d0 * foot, m1 - d1 * foot, m1 - d1 * head, m0 - d0 * head, y0, y1, 0f, top);
                Concrete(m0 + d0 * head, m1 + d1 * head, m1 + d1 * foot, m0 + d0 * foot, y0, y1, top, 0f);
                concrete.TwoSided(World(m0 - d0 * head, y0 + top), World(m1 - d1 * head, y1 + top), World(m1 + d1 * head, y1 + top), World(m0 + d0 * head, y0 + top));
                return;
            }
            // a raised island: kerbs along both sides, grass on top
            kerbs.TwoSided(World(l0, y0 + top), World(l1, y1 + top), World(l1, y1), World(l0, y0));
            kerbs.TwoSided(World(r1, y1 + top), World(r0, y0 + top), World(r0, y0), World(r1, y1));
            kerbs.TwoSided(World(l0, y0 + top), World(r0, y0 + top), World(r0, y0), World(l0, y0));
            kerbs.TwoSided(World(r1, y1 + top), World(l1, y1 + top), World(l1, y1), World(r1, y1));
            var inL0 = Vector2.Lerp(l0, r0, KerbWidth / Mathf.Max(Vector2.Distance(l0, r0), .01f));
            var inR0 = Vector2.Lerp(r0, l0, KerbWidth / Mathf.Max(Vector2.Distance(l0, r0), .01f));
            var inL1 = Vector2.Lerp(l1, r1, KerbWidth / Mathf.Max(Vector2.Distance(l1, r1), .01f));
            var inR1 = Vector2.Lerp(r1, l1, KerbWidth / Mathf.Max(Vector2.Distance(l1, r1), .01f));
            kerbs.TwoSided(World(l0, y0 + top), World(l1, y1 + top), World(inL1, y1 + top), World(inL0, y0 + top));
            kerbs.TwoSided(World(inR0, y0 + top), World(inR1, y1 + top), World(r1, y1 + top), World(r0, y0 + top));
            verges.TwoSided(World(inL0, y0 + top), World(inL1, y1 + top), World(inR1, y1 + top), World(inR0, y0 + top));
        }

        void Concrete(Vector2 a0, Vector2 a1, Vector2 b1, Vector2 b0, float y0, float y1, float ha, float hb) =>
            concrete.TwoSided(World(a0, y0 + ha), World(a1, y1 + ha), World(b1, y1 + hb), World(b0, y0 + hb));

        /// <summary>A kerb along a line (the junction kerbs): a low concrete edge whose face is on the line, its top
        /// running a kerb's width outward (to the line's right).</summary>
        void KerbAlong(List<Vector2> line, float y)
        {
            int n = line.Count;
            var o = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                var d = line[Mathf.Min(k + 1, n - 1)] - line[Mathf.Max(k - 1, 0)];
                o[k] = d.sqrMagnitude > 1e-10f ? Right(d.normalized) * KerbWidth : Vector2.zero;
            }
            for (int k = 0; k + 1 < n; k++)
            {
                if ((line[k + 1] - line[k]).sqrMagnitude < 1e-6f) continue;
                kerbs.TwoSided(World(line[k], y + KerbHeight), World(line[k + 1], y + KerbHeight), World(line[k + 1], y), World(line[k], y));
                kerbs.TwoSided(World(line[k] + o[k], y + KerbHeight), World(line[k + 1] + o[k + 1], y + KerbHeight), World(line[k + 1], y + KerbHeight),
                    World(line[k], y + KerbHeight));
            }
        }

        /// <summary>
        /// Paint where a road meets a junction, for the traffic arriving along it: a solid stop line across its
        /// approach lanes (give-way dashes where it gives way at an unsignalled junction), a zebra crossing in front of it
        /// on streets with sidewalks, and an arrow in each approach lane showing the moves it may make (LaneConnections).
        /// </summary>
        void Approach(Junction junction, Arm arm)
        {
            var t = arm.edge.Type;
            bool arriving = arm.edge.b == junction.node.id;          // traffic coming in drives along -direction
            int lanesIn = arriving ? t.forward : t.backward;
            if (lanesIn == 0) return;
            var u = arm.direction;
            var across = Left(u);                                     // seen arriving, the approach lanes are on this side
            float scale = arm.halfWidth / Mathf.Max(t.Width * .5f, .01f);
            var (inner, outer) = t.Carriageway(arriving);
            // the approach lanes across: from the centre side to the kerb side, in "across" units
            float a0 = Mathf.Abs(inner) * scale, a1 = Mathf.Abs(outer) * scale;
            if (t.OneWay) { a0 = -(t.Width * .5f - t.innerShoulder) * scale; a1 = (t.Width * .5f - t.outerShoulder) * scale; }
            var centre = junction.p;
            bool signals = junction.node.control == JunctionControl.TrafficLights;
            bool gives = !signals && PublicRoadSignals.Group(graph, junction.node.id, arm.edge.id) == 1;
            bool crossing = t.edge == RoadEdge.Kerb;
            float stop = StopAt(arm);
            var c0 = centre + u * stop + across * a0;
            var c1 = centre + u * stop + across * a1;
            if (signals || (!gives && junction.arms.Count >= 3 && t.Lanes > 1)) paint.Strip(SampleLine(c0,c1),.25f,PaintBuilder.White);
            else if (gives)
                for (int k = 0; k < 2; k++)
                {
                    var o = u * (k * .6f);
                    paint.Dashes(c0 + o, c1 + o, .15f, .6f, .3f, PaintBuilder.White);
                }
            if (crossing)
            {
                // zebra stripes across the whole road, between the stop line and the junction
                float zebra = arm.cut + 2.2f, half = arm.halfWidth - .3f;
                // bars 3 m long along the road, 0.55 m wide, 0.55 m apart, centred across it
                int bars = Mathf.FloorToInt((2f * half + .55f) / 1.1f);
                float first = -(bars - 1) * .55f;
                for (int k = 0; k < bars; k++)
                    paint.Box(centre + u * zebra + across * (first + k * 1.1f), u, .275f, 1.5f, PaintBuilder.White);
            }
            // Short approaches cannot fit an arrow between their two junctions.
            if(arm.length-stop-ArmAt(arm.edge.a==junction.node.id?arm.edge.b:arm.edge.a,arm.edge.id).cut<7f)return;
            // arrows: one per approach lane, a few metres before the stop line
            var exits = graph.LaneConnections(junction.node.id).FindAll(c => c.incoming == arm.edge.id);
            for (int lane = 0; lane < lanesIn; lane++)
            {
                // lane 0 = the kerb lane: on the "across" side, at the far end of a0..a1
                float x = Mathf.Lerp(a1, a0, (lane + .5f) / lanesIn);
                bool left = exits.Exists(c => c.incomingLane == lane && c.turn == RoadTurn.Left);
                bool straight = exits.Exists(c => c.incomingLane == lane && c.turn == RoadTurn.Straight);
                bool rightTurn = exits.Exists(c => c.incomingLane == lane && c.turn == RoadTurn.Right);
                if (!left && !straight && !rightTurn) continue;
                Arrow(centre + u * (stop + 6f) + across * x, -u, left, straight, rightTurn);
            }
        }

        /// <summary>How far from a junction's centre an arm's stop line is: past the junction's own pavement, and past a
        /// crosswalk where the road has sidewalks (kept short of the section's middle).</summary>
        static float StopAt(Arm arm)
        {
            float stop = arm.cut + (arm.edge.Type.edge == RoadEdge.Kerb ? 4.5f : .8f);
            if (stop + 8f > arm.length * .5f) stop = Mathf.Max(arm.cut + .5f, arm.length * .5f - 8f);
            return stop;
        }

        /// <summary>Where a road's stop line is at a junction, metres from the junction's node (0 if it isn't a junction).</summary>
        public float StopLine(int node, int edge)
        {
            if (!junctions.TryGetValue(node, out var j) || !j.IsJunction) return 0f;
            var arm = j.arms.Find(a => a.edge.id == edge);
            return arm != null ? StopAt(arm) : 0f;
        }

        /// <summary>Lane-contained arrows: one continuous head per movement and rounded turn stems.</summary>
        void Arrow(Vector2 tail, Vector2 forward, bool left, bool straight, bool right)
        {
            var side=Right(forward);
            paint.Strip(new[]{tail,tail+forward*(straight?3.2f:1.55f)},.15f,PaintBuilder.White);
            if(straight)paint.ArrowHead(tail+forward*4.5f,forward,1.4f,.5f,PaintBuilder.White);
            foreach(var (on,sign) in new[]{(left,-1f),(right,1f)})
            {
                if(!on)continue;
                var start=tail+forward*1.4f;var direction=side*sign;
                var centre=start+direction*.65f;var end=centre+forward*.65f;
                var arc=new List<Vector2>();Geo.Arc(arc,centre,start,end,.12f);
                paint.Strip(arc,.15f,PaintBuilder.White);
                paint.ArrowHead(tail+forward*2.05f+direction*1.4f,direction,.95f,.45f,PaintBuilder.White);
            }
        }

        /// <summary>A kerb line moved `inset` metres onto the pavement: to the left of the way it runs (kerbs are walked
        /// counter-clockwise round a junction).</summary>
        static List<Vector2> Inset(List<Vector2> line, float inset)
        {
            var result = new List<Vector2>(line.Count);
            for (int k = 0; k < line.Count; k++)
            {
                var d = line[Mathf.Min(k + 1, line.Count - 1)] - line[Mathf.Max(k - 1, 0)];
                result.Add(d.sqrMagnitude > 1e-8f ? line[k] + Left(d.normalized) * inset : line[k]);
            }
            return result;
        }

        /// <summary>A quad into a plain mesh (kerbs), facing whichever way its corners wind.</summary>
        static void Quad(LandscapeMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d) => mesh.TwoSided(a, b, c, d);

        static void Quad(MeshData mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = mesh.Count; var normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.y < 0f) { var swap = b; b = d; d = swap; normal = -normal; }
            void Vertex(Vector3 p)=>mesh.Vertex(p,normal,new Color(0f,0f,0f,1f),new Vector4(p.x,p.z),new Vector4((float)AirfieldKind.Junction,0f));
            Vertex(a);Vertex(b);Vertex(c);Vertex(d);
            mesh.Quad(0, i, i + 1, i + 2, i + 3);
        }
        static void Fan(MeshData mesh, Vector3 centre, List<Vector2> outline)
        {
            for (int i = 0; i < outline.Count; i++)
            {
                var a = World(outline[i], centre.y); var b = World(outline[(i + 1) % outline.Count], centre.y);
                Quad(mesh, centre, a, b, centre);
            }
        }
        static List<Vector2> SampleLine(Vector2 a,Vector2 b)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/2f));var result=new List<Vector2>(count+1);
            for(int i=0;i<=count;i++)result.Add(Vector2.Lerp(a,b,i/(float)count));return result;
        }
        void DrapePaint(int first, Func<Vector2, float> height)
        {
            for (int i = first; i < paint.mesh.positions.Count; i++)
            {
                var p=paint.mesh.positions[i];var at=new Vector2(p.x,p.z);
                // This is only the deck-selection hint. Final projection below supplies exact height/gradient.
                p.y+=height(at)+.18f;paint.mesh.positions[i]=p;
            }
        }

        readonly Pavement paintProjection=new Pavement();
        void ProjectPaintOntoPavement(PublicRoadGeometry context)
        {
            var pavement=paintProjection;
            foreach(var ids in surface.indices)for(int i=0;i<ids.Count;i+=3)
                pavement.AddTriangle(surface.positions[ids[i]],surface.positions[ids[i+1]],surface.positions[ids[i+2]]);
            for(int i=0;i<paint.mesh.Count;i++)
            {
                var p=paint.mesh.positions[i];
                if(!pavement.Project(new Vector2(p.x,p.z),p.y,out float height,out var gradient) &&
                    (context==null || !context.ProjectPaint(new Vector2(p.x,p.z),p.y,out height,out gradient)))continue;
                p.y=height+.014f;paint.mesh.positions[i]=p;
                var grow=paint.mesh.normals[i];grow.y=gradient.x*grow.x+gradient.y*grow.z;paint.mesh.normals[i]=grow;
                var tangent=paint.mesh.tangents[i];tangent.y=gradient.x*tangent.x+gradient.y*tangent.z;paint.mesh.tangents[i]=tangent;
            }
        }

        public void SampleGround(Vector2 p, out float height, out float distance, out float halfWidth)
        {
            height = 0f; distance = float.MaxValue; halfWidth = 3.25f;
            foreach (var link in links)
            {
                if (link.structure != RoadStructure.Ground) continue;
                var q = Geo.ClosestOnSegment(p, link.a, link.b, out float t); float d = Vector2.Distance(p, q);
                if (d >= distance) continue;
                distance = d; height = Mathf.Lerp(link.heightA, link.heightB, Mathf.Clamp01(t)); halfWidth = link.halfWidth;
            }
            foreach (var j in junctions.Values)
            {
                if (!j.IsJunction || j.arms.All(a => a.edge.structure != RoadStructure.Ground)) continue;
                float cut = j.arms.Max(a => a.cut), d = Vector2.Distance(p, PublicRoadNetwork.XZ(j.node.position));
                float flat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cut, cut + 20f, d));
                height = Mathf.Lerp(height, j.node.position.y, flat);
            }
        }

        public float[] GradedHeights(ValleyWorld world)
        {
            var heights = new float[world.heights.Length];
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
                heights[z * ValleyWorld.Resolution + x] = GradedHeight(world, x, z);
            return heights;
        }

        /// <summary>The same roadbed as a full rebuild, sampled only inside the edited heightmap rectangle (metres).</summary>
        public float[,] GradedHeightRegion(ValleyWorld world, RectInt region)
        {
            var heights = new float[region.height, region.width];
            for (int z = 0; z < region.height; z++)
            for (int x = 0; x < region.width; x++) heights[z, x] = GradedHeight(world, region.x + x, region.y + z);
            return heights;
        }

        void RememberGround(int first, int firstShoulder)
        {
            void Keep(MeshData mesh,int start)
            {
                foreach(var ids in mesh.indices)for(int i=0;i<ids.Count;i+=3)
                {
                    int a=ids[i],b=ids[i+1],c=ids[i+2];
                    if(a<start || b<start || c<start || Mathf.Min(mesh.colors[a].a,mesh.colors[b].a,mesh.colors[c].a)<.99f)continue;
                    groundTriangles.Add(new BuiltSurfaceMap.Triangle(mesh.positions[a],mesh.positions[b],mesh.positions[c]));
                }
            }
            Keep(surface,first);Keep(shoulder,firstShoulder);
            // Follow the longitudinal grade, with a flat soil formation across the carriageway. Extending a
            // crowned pavement's two inclined roof planes onto 12.5 m terrain corners digs a trench under its crown.
            Vector2 AxisAt(Vector2 point)
            {
                float best = float.MaxValue; var axis = Vector2.right;
                foreach (var link in links)
                {
                    if (link.structure != RoadStructure.Ground) continue;
                    float distance = (Geo.ClosestOnSegment(point, link.a, link.b, out _) - point).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance; axis = (link.b - link.a).normalized;
                }
                return axis;
            }
            void Remember(List<Vector3> positions, int start)
            {
                const float spacing = ValleyWorld.Size / (ValleyWorld.Resolution - 1);
                for (int quad = start; quad + 3 < positions.Count; quad += 4)
                {
                    var origin = positions[quad];
                    var normal = Vector3.Cross(positions[quad + 1] - origin, positions[quad + 2] - origin);
                    var centre = (positions[quad] + positions[quad + 1] + positions[quad + 2] + positions[quad + 3]) * .25f;
                    var axis = AxisAt(new Vector2(centre.x, centre.z));
                    var gradient = Mathf.Abs(normal.y) > .00001f ? new Vector2(-normal.x, -normal.z) / normal.y : Vector2.zero;
                    gradient = axis * Vector2.Dot(gradient, axis);
                    float formation = float.MaxValue;
                    for (int vertex = quad; vertex < quad + 4; vertex++)
                    {
                        var v = positions[vertex];
                        formation = Mathf.Min(formation, v.y - Vector2.Dot(gradient, new Vector2(v.x - origin.x, v.z - origin.z)));
                    }
                    formation -= .055f;
                    for (int vertex = quad; vertex < quad + 4; vertex++)
                    {
                        var p = positions[vertex];
                        int x0 = Mathf.Clamp(Mathf.FloorToInt((p.x + ValleyWorld.Size * .5f) / spacing), 0, ValleyWorld.Resolution - 1);
                        int z0 = Mathf.Clamp(Mathf.FloorToInt((p.z + ValleyWorld.Size * .5f) / spacing), 0, ValleyWorld.Resolution - 1);
                        for (int z = z0; z <= Mathf.Min(z0 + 1, ValleyWorld.Resolution - 1); z++)
                        for (int x = x0; x <= Mathf.Min(x0 + 1, ValleyWorld.Resolution - 1); x++)
                        {
                            float px = x * spacing - ValleyWorld.Size * .5f, pz = z * spacing - ValleyWorld.Size * .5f;
                            float height = formation + Vector2.Dot(gradient, new Vector2(px - origin.x, pz - origin.z));
                            int key = z * ValleyWorld.Resolution + x; float ceiling = height + .008f;
                            if (!groundCeilings.TryGetValue(key, out float previous) || ceiling < previous) groundCeilings[key] = ceiling;
                            float floor = height - .008f;
                            if (!groundFloors.TryGetValue(key, out previous) || floor > previous) groundFloors[key] = floor;
                        }
                    }
                }
            }
            // At this terrain resolution the shoulder crossfall is sub-grid detail. Let soil meet its outer edge;
            // imposing the shoulder's lower plane beneath every pavement sample would excavate the roadbed again.
            Remember(surface.positions, first);
        }

        float GradedHeight(ValleyWorld world, int x, int z)
        {
            int index = z * ValleyWorld.Resolution + x;
            float original = world.heights[index], graded = original;
            var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
            foreach (var box in roadbedBounds)
            {
                if (!box.Contains(p)) continue;
                SampleGround(p, out float h, out float d, out float halfWidth);
                if (d >= EarthworkReach) continue;
                float roadbed = h + .125f;
                float run = Mathf.Max(0f, d - Mathf.Max(halfWidth + 4f, world.Spacing * 1.35f));
                float easedRun = run * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(run / 12f));
                // Ground roads are supported by soil all the way to their shoulders. Hill cuts and 1:2 fill slopes
                // meet the original terrain naturally; elevated links never participate in this derived roadbed.
                graded = original > roadbed ? Mathf.Min(original, roadbed + easedRun * .7f) : Mathf.Max(original, roadbed - easedRun * .5f);
                break;
            }
            if (groundCeilings.TryGetValue(index, out float ceiling))
            {
                float floor = groundFloors.TryGetValue(index, out float support) ? Mathf.Min(support, ceiling) : float.NegativeInfinity;
                graded = Mathf.Clamp(graded, floor, ceiling);
            }
            return graded;
        }
    }
}
