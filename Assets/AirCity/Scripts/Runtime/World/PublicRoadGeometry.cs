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
        public readonly struct GroundLimit
        {
            readonly Vector2 direction;
            readonly float station;
            readonly bool tunnel;

            internal GroundLimit(Vector2 direction, float station, bool tunnel)
            {
                this.direction = direction;
                this.station = station;
                this.tunnel = tunnel;
            }

            internal bool AllowsGrading(Vector2 point, bool nearRoad)
            {
                const float spacing = ValleyWorld.Size / (ValleyWorld.Resolution - 1);
                float past = Vector2.Dot(direction, point) - station;
                return tunnel ? past <= .001f && (nearRoad || past < -2f * spacing) : nearRoad || past <= 0f;
            }

            internal bool AllowsFoundation(Vector2 point) => !tunnel || Vector2.Dot(direction, point) <= station + .001f;

            public override int GetHashCode() => HashCode.Combine(direction, station, tunnel);
        }

        public readonly struct Link
        {
            public readonly Vector2 a, b;
            public readonly float heightA, heightB, halfWidth;
            public readonly RoadStructure structure;
            readonly GroundLimit startLimit,endLimit;
            internal readonly bool retainsBank;
            public Link(Vector3 a, Vector3 b, float halfWidth, RoadStructure structure,GroundLimit startLimit=default,
                GroundLimit endLimit=default,bool retainsBank=false)
            {
                this.a=PublicRoadNetwork.XZ(a);this.b=PublicRoadNetwork.XZ(b);heightA=a.y;heightB=b.y;this.halfWidth=halfWidth;this.structure=structure;
                this.startLimit=startLimit;this.endLimit=endLimit;this.retainsBank=retainsBank;
            }
            internal bool FormsGroundAt(Vector2 point,float distance)
            {
                const float spacing=ValleyWorld.Size/(ValleyWorld.Resolution-1);
                bool nearRoad = distance < halfWidth + spacing;
                return startLimit.AllowsGrading(point, nearRoad) && endLimit.AllowsGrading(point, nearRoad);
            }
            internal bool AllowsFoundation(Vector2 point) => startLimit.AllowsFoundation(point) && endLimit.AllowsFoundation(point);
        }
        sealed class Arm
        {
            public PublicRoadSegment edge;
            public Vector2 direction;
            public Vector2 joinRight;
            public float joinCrown;
            public float halfWidth, length, cut, need;
            /// <summary>Junctions: how traffic arriving along this road is controlled, where its lines stop (metres from
            /// the node: the stop line, or the junction's edge where it has none) and whether a crosswalk is painted.</summary>
            public ApproachControl control;
            public float stop;
            public bool crossing;
            /// <summary>Junctions: the kerb from this arm's left mouth corner round to the next arm's right one.</summary>
            public List<Vector2> kerb;
        }
        /// <summary>How a road's traffic arriving at a junction is controlled, as on North American roads: nothing (the
        /// road straight through a priority junction: no stop line), a stop sign's stop line (the side road), or signals.</summary>
        enum ApproachControl { None, Stop, Signal }
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
        /// <summary>The top of every bridge column (where it meets its crosshead).</summary>
        internal readonly List<Vector3> piers = new List<Vector3>();
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
        readonly Func<Vector2,float,float,float> retainShore;
        readonly Func<Vector2,float?> waterHeight;
        readonly PublicRoadGeometry structureContext;
        readonly bool hasStructures;
        readonly MeshData surface = new MeshData(), shoulder = new MeshData();
        readonly PaintBuilder paint = new PaintBuilder();
        readonly LandscapeMesh concrete = new LandscapeMesh(), kerbs = new LandscapeMesh();
        /// <summary>The grass on raised medians: a surface mesh like the pavement's, drawn with the airfield's grass.</summary>
        readonly MeshData verges = new MeshData();
        Rect[] roadbedBounds;
        readonly Dictionary<int, float> groundCeilings = new Dictionary<int, float>();
        readonly Dictionary<int, float> groundFloors = new Dictionary<int, float>();
        readonly Dictionary<int, PublicRoadSegment> portalLining = new Dictionary<int, PublicRoadSegment>();
        readonly Dictionary<int, bool> retainingBanks = new Dictionary<int, bool>();

        public PublicRoadGeometry(PublicRoadNetwork graph, PublicRoadProfile rural, PublicRoadProfile airport, Func<Vector2, float> groundHeight, ISet<int> segmentIds=null, ISet<int> junctionIds=null, PublicRoadGeometry projectionContext=null,bool generateMeshes=true,
            Func<Vector2,float,float,float> retainShore=null,Func<Vector2,float?> waterHeight=null,PublicRoadGeometry structureContext=null)
        {
            this.graph = graph; this.rural = rural; this.airport = airport;this.groundHeight=groundHeight;this.retainShore=retainShore;
            this.waterHeight=waterHeight;this.structureContext=structureContext;
            hasStructures=graph.segments.Any(edge=>edge.structure!=RoadStructure.Ground);
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
                if (joint.arms.Count == 2 && StraightOn(joint))
                {
                    // Both segments terminate on one cross-section, including nearly straight joins.
                    var first = joint.arms[0];
                    var second = joint.arms[1];
                    var across = Right((first.direction - second.direction).normalized);
                    first.joinRight = first.edge.a == joint.node.id ? across : -across;
                    second.joinRight = second.edge.a == joint.node.id ? -across : across;
                    first.joinCrown = second.joinCrown = (Crown(first.edge) + Crown(second.edge)) * .5f;
                }
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
                    AddLinks(links,edge,path,start,end);
                }
                int first = surface.Count, firstShoulder = shoulder.Count, firstCut = groundTriangles.Count;
                var lining = PortalLining(edge);
                BuildSegment(edge, lining);
                if (edge.structure == RoadStructure.Ground && lining == null) RememberGround(first, firstShoulder, firstCut, RetainsBank(edge));
            }
            foreach (var junction in junctions.Values)
            {
                if(junctionIds!=null && !junctionIds.Contains(junction.node.id))continue;
                int first = surface.Count, firstShoulder = shoulder.Count, firstCut = groundTriangles.Count;
                if (junction.IsJunction) BuildJunction(junction);
                else if (junction.IsCurve) BuildCurve(junction);
                if (junction.arms.Any(a => a.edge.structure == RoadStructure.Ground))
                    RememberGround(first, firstShoulder, firstCut, junction.arms.Any(a => RetainsBank(a.edge)));
            }
            roadbedBounds = links.Where(l => l.structure == RoadStructure.Ground).Select(l => Rect.MinMaxRect(Mathf.Min(l.a.x, l.b.x) - EarthworkReach,
                Mathf.Min(l.a.y, l.b.y) - EarthworkReach, Mathf.Max(l.a.x, l.b.x) + EarthworkReach, Mathf.Max(l.a.y, l.b.y) + EarthworkReach)).ToArray();
            if(!generateMeshes)return;
            ProjectPaintOntoPavement(projectionContext);
            // the kind, and world-mapped texture (-1); z and w are the lanes for tyre wear (LaneVertex)
            for(int i=0;i<surface.Count;i++)surface.uv1[i]=new Vector4((float)AirfieldKind.Road,-1f,surface.uv1[i].z,surface.uv1[i].w);
            for(int i=0;i<verges.Count;i++)verges.uv1[i]=new Vector4((float)AirfieldKind.Ground,0f);
            pavement = surface.Apply(null, "Public road pavement"); pavement.RecalculateNormals();
            shoulders = shoulder.Apply(null, "Public road shoulders"); shoulders.RecalculateNormals();
            markings = paint.mesh.Apply(null, "Public road lane markings", true, 4);
            structures = concrete.Create("Public road bridge structures");
            kerbMesh = kerbs.Create("Public road kerbs");
            medianGrass = verges.Apply(null, "Public road median grass");
        }

        /// <summary>A network's layout only (bends, junction cuts, stop lines), with no meshes: for tools that outline
        /// roads as they are drawn (SectionOutline).</summary>
        public static PublicRoadGeometry Layout(PublicRoadNetwork graph) =>
            Compose(graph, null, null, _ => 0f, Array.Empty<PublicRoadGeometry>());

        /// <summary>
        /// The outline of a section as it is drawn, `half` metres either side of its centreline, between two
        /// distances along it (from its node a): up one side and back down the other. A bend belongs half to each of
        /// its two sections, so an outline that reaches a bend carries on round it to the middle of the arc instead
        /// of running straight to the corner; at a junction it stops at the junction's edge.
        /// </summary>
        public bool SectionOutline(PublicRoadSegment edge, float from, float to, float half, List<Vector3> loop)
        {
            if (!junctions.TryGetValue(edge.a, out var ja) || !junctions.TryGetValue(edge.b, out var jb)) return false;
            Arm aa = ja.arms.Find(x => x.edge.id == edge.id), ab = jb.arms.Find(x => x.edge.id == edge.id);
            if (aa == null || ab == null) return false;
            var path = graph.Path(edge);
            float length = path.Length;
            var centre = new List<(Vector3 p, Vector2 right)>();
            // the half of a bend's arc on this section's side, in the direction a to b
            void HalfArc(Junction j, Arm arm, bool leaving)
            {
                if (!j.IsCurve || j.arc == null || j.arc.Count < 2) return;
                var along = Geo.Along(j.arc);
                float total = along[along.Length - 1];
                bool first = j.arms[0] == arm;       // the arc starts at arms[0]'s cut
                var points = first ? Geo.Slice(j.arc, along, 0f, total * .5f) : Geo.Slice(j.arc, along, total * .5f, total);
                if (first == leaving) points.Reverse();   // leaving the bend: from the arc's middle to this section's cut
                for (int k = 0; k < points.Count; k++)
                {
                    var d = points[Mathf.Min(k + 1, points.Count - 1)] - points[Mathf.Max(k - 1, 0)];
                    if (d.sqrMagnitude > 1e-8f) centre.Add((World(points[k], j.node.position.y), Right(d.normalized)));
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
                    var c = SectionPoint(path, aa, ab, s, 0f);
                    centre.Add((c, Alignment3D.XZ(SectionPoint(path, aa, ab, s, 1f) - c)));
                }
            }
            if (to >= length - .01f) HalfArc(jb, ab, false);
            if (centre.Count < 2) return false;
            for (int i = 0; i < centre.Count; i++) loop.Add(centre[i].p - World(centre[i].right, 0f) * half);
            for (int i = centre.Count - 1; i >= 0; i--) loop.Add(centre[i].p + World(centre[i].right, 0f) * half);
            return true;
        }

        // A composed logical view reuses CPU formations and triangle footprints. Full combined meshes are made
        // lazily for exports/diagnostics; the game renders the independently cached sections directly.
        internal static PublicRoadGeometry Compose(PublicRoadNetwork graph,PublicRoadProfile rural,PublicRoadProfile airport,
            Func<Vector2,float> height,IReadOnlyList<PublicRoadGeometry> parts,Func<Vector2,float,float,float> retainShore=null,Func<Vector2,float?> waterHeight=null)
        {
            var empty=new HashSet<int>();var result=new PublicRoadGeometry(graph,rural,airport,height,empty,empty,null,false,retainShore,waterHeight) {pieces=parts};
            foreach(var part in parts)
            {
                result.links.AddRange(part.links);result.groundTriangles.AddRange(part.groundTriangles);result.piers.AddRange(part.piers);
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

        static Vector3 SectionPoint(Alignment3D path, Arm start, Arm end, float station, float offset)
        {
            if (station <= .0001f && start.joinRight != Vector2.zero)
                return path.start + World(start.joinRight * offset, 0f);
            if (station >= path.Length - .0001f && end.joinRight != Vector2.zero)
                return path.end + World(end.joinRight * offset, 0f);
            return path.Offset(station, offset, start.cut, end.cut);
        }

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
            float cut = radius / tanHalf, room = Mathf.Min(BendReach(j, a0), BendReach(j, a1));
            if (cut > room) { cut = room; radius = cut * tanHalf; }
            if (radius < a0.halfWidth * 1.25f) return false;
            Geo.Fillet(j.p, a0.direction, a1.direction, radius, out var centre);
            j.arc = new List<Vector2>();
            Geo.Arc(j.arc, centre, j.p + a0.direction * cut, j.p + a1.direction * cut, Geo.ArcStep(radius));
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

        /// <summary>How far along an arm a bend may reach. A section that ends in a dead end or at a car park's mouth
        /// has nothing to share with at its far end: the bend takes it all but a short straight, so the stub in
        /// front of a car park doesn't squeeze the bend tight.</summary>
        float BendReach(Junction j, Arm a)
        {
            int other = a.edge.a == j.node.id ? a.edge.b : a.edge.a;
            if (junctions.TryGetValue(other, out var far) && far.arms.Count == 1) return a.length - Mathf.Min(DeadEndStraight, a.length * .25f);
            return Reach(j, a);
        }

        /// <summary>The straight kept between a bend and a dead end (or a car park's mouth) on a short section.</summary>
        const float DeadEndStraight = 4f;

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
            foreach (var arm in j.arms) Control(j, arm);
        }

        /// <summary>
        /// An arm's control, crosswalk and stop line. Signals stop every road arriving; at a priority junction the
        /// road straight through has no stop line and the roads joining it stop. Crosswalks are marked on roads with
        /// sidewalks wherever traffic is stopped. The stop line is 1 m back from the junction's edge, or 1.4 m behind
        /// the crosswalk (kept short of the section's middle).
        /// </summary>
        void Control(Junction j, Arm arm)
        {
            var t = arm.edge.Type;
            bool lights = j.node.control == JunctionControl.TrafficLights, real = j.arms.Count >= 3;
            bool arrives = (arm.edge.b == j.node.id ? t.forward : t.backward) > 0;
            arm.control = !real || !arrives ? ApproachControl.None : lights ? ApproachControl.Signal
                : j.node.control == JunctionControl.Priority && PublicRoadSignals.Group(graph, j.node.id, arm.edge.id) == 1 ? ApproachControl.Stop
                : ApproachControl.None;
            arm.crossing = real && t.edge == RoadEdge.Kerb && (lights || arm.control == ApproachControl.Stop);
            arm.stop = arm.cut;
            if (arm.control == ApproachControl.None && !arm.crossing) return;
            arm.stop = arm.cut + (arm.crossing ? CrosswalkStart + CrosswalkLength + 1.4f : 1f);
            if (arm.stop + 8f > arm.length * .5f) arm.stop = Mathf.Max(arm.cut + .5f, arm.length * .5f - 8f);
        }

        /// <summary>A crosswalk's bars: this far past the junction's edge, and this long along the road.</summary>
        const float CrosswalkStart = .6f, CrosswalkLength = 3f;

        /// <summary>Whether a road's traffic stops at a node (a stop line or a crosswalk there): its lines are solid on
        /// the approach.</summary>
        bool StopsAt(int node, PublicRoadSegment edge)
        {
            if (!junctions.TryGetValue(node, out var j) || !j.IsJunction) return false;
            var arm = j.arms.Find(a => a.edge.id == edge.id);
            return arm != null && (arm.control != ApproachControl.None || arm.crossing);
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

        void BuildSegment(PublicRoadSegment edge, PublicRoadSegment lining)
        {
            var start = graph.Node(edge.a).position; var end = graph.Node(edge.b).position;
            var a = PublicRoadNetwork.XZ(start); var b = PublicRoadNetwork.XZ(end);
            var path=graph.Path(edge);var delta = b - a; float length = path.Length;
            if (length < .01f) return;
            var axis = delta.normalized; var right = Right(axis);
            var startArm = ArmAt(edge.a, edge.id);
            var endArm = ArmAt(edge.b, edge.id);
            float s0 = startArm.cut, s1 = length - endArm.cut;
            Vector3 Point(float s, float x) => SectionPoint(path, startArm, endArm, s, x);
            float half = Width(edge) * .5f, verge = ShoulderWidth(edge);
            float crown = Crown(edge);
            Vector3 At(float s, float x, float extra = 0f)
            {
                var point = Point(s, x);
                var p = Alignment3D.XZ(point);
                Arm join = s <= .0001f ? startArm : s >= length - .0001f ? endArm : null;
                bool atJoin = join != null && join.joinRight != Vector2.zero;
                float crossfall = (atJoin ? join.joinCrown : crown) * Mathf.Clamp01(1f - Mathf.Abs(x) / half);
                return World(p, (atJoin ? point.y : Elevation(edge, p)) + .18f + crossfall + extra);
            }
            bool kerbed = edge.Type.edge == RoadEdge.Kerb;
            bool retainsBank = lining == null && RetainsBank(edge);
            bool roundA=RoundEnd(edge.a,edge),roundB=RoundEnd(edge.b,edge);
            float radius=Mathf.Min(3f,half*.55f,(s1-s0)*.2f);
            if(roundA)s0+=radius;if(roundB)s1-=radius;
            var endA=roundA?BuildEnd(edge,edge.a,-Alignment3D.XZ(path.Tangent(0)).normalized,half,verge,radius,retainsBank):null;
            var endB=roundB?BuildEnd(edge,edge.b,Alignment3D.XZ(path.Tangent(1)).normalized,half,verge,radius,retainsBank):null;
            int steps = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 5f));
            float[] xs = { -half, 0f, half };
            // lane and centre lines stop at the stop lines (beyond them is the crosswalk and junction)
            PaintSpan(edge, out _, out _, out float stopA, out float stopB);
            bool stopsA = StopsAt(edge.a, edge), stopsB = StopsAt(edge.b, edge);
            // traffic brakes and waits on the last 45 m before a stop line: more rubber and oil there
            float Braking(float s)
            {
                float d = Mathf.Min(stopsA ? s - stopA : float.MaxValue, stopsB ? stopB - s : float.MaxValue);
                return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 45f, d));
            }
            // a bridge or tunnel carries the road on concrete: its own cross-sections, no gravel or sidewalk laid on soil
            bool groundShoulder = edge.structure == RoadStructure.Ground && lining == null && !retainsBank;
            bool sealShoulder = groundShoulder && (MeetsTunnel(edge, edge.a) || MeetsTunnel(edge, edge.b));
            var piece = !groundShoulder && s1 > s0 ? Piece(edge, verge) : null;
            if (piece != null) piece.retainsBank = retainsBank;
            if (piece != null && lining != null)
            {
                piece.structure = RoadStructure.Tunnel;
                piece.portalStyle = lining.tunnelPortalStyle;
            }
            for (int step = 0; step < steps; step++)
            {
                float sa = Mathf.Lerp(s0, s1, step / (float)steps);
                float sb = step + 1 == steps ? s1 : Mathf.Lerp(s0, s1, (step + 1f) / steps);
                for (int col = 0; col < 2; col++)
                {
                    int i = surface.Count;
                    Vector3 pa = At(sa, xs[col]), pb = At(sb, xs[col]), pd = At(sa, xs[col + 1]);
                    Quad(surface, pa, pb, At(sb, xs[col + 1]), pd);
                    for (int k = i; k < surface.Count; k++)
                    {
                        var p = surface.positions[k];
                        LaneVertex(k, edge, half, p == pa || p == pb ? xs[col] : xs[col + 1], Braking(p == pa || p == pd ? sa : sb));
                    }
                }
                if (piece != null)
                {
                    if (step == 0) piece.Add(At(sa, -half), At(sa, half));
                    piece.Add(At(sb, -half), At(sb, half));
                }
                for(int side=-1;side<=1;side+=2)
                {
                    float inner = half * side, outer = (half + verge) * side;
                    if (kerbed)
                    {
                        // a kerb face up to the sidewalk, the sidewalk level at kerb height
                        float back = inner + KerbWidth * side;
                        Kerb(At(sa, inner, KerbHeight), At(sb, inner, KerbHeight), At(sb, back, KerbHeight), At(sa, back, KerbHeight),
                            groundShoulder || retainsBank, retainsBank);
                        if (groundShoulder) GroundShoulder(At(sa, back, KerbHeight), At(sb, back, KerbHeight), At(sb, outer, KerbHeight), At(sa, outer, KerbHeight), seal: sealShoulder);
                    }
                    else if (groundShoulder) GroundShoulder(At(sa, inner, -.035f), At(sb, inner, -.035f), At(sb, outer, -.09f), At(sa, outer, -.09f), pavementStep: .035f, seal: sealShoulder);
                }
            }
            paint.frame = RunwayFrame.Along(a, axis);
            int first = paint.mesh.Count;
            List<Vector2> LineAt(float x,float f,float t)
            {
                int count=Mathf.Max(1,Mathf.CeilToInt((t-f)/2f));var points=new List<Vector2>(count+1);
                for(int i=0;i<=count;i++)points.Add(Alignment3D.XZ(Point(Mathf.Lerp(f,t,i/(float)count),x)));return points;
            }
            Markings(edge, half, s0, s1, stopA, stopB, stopsA, stopsB, LineAt,
                (x, f, t, gap, color, lanes) => SectionDashes(edge, x, f, t, gap, color, lanes, LineAt));
            Median(edge, s1 - s0, MedianInset(edge.a, edge), MedianInset(edge.b, edge),
                (sa,sb,x0,x1,top)=>
                {
                    sa+=s0;sb+=s0;
                    MedianSlice(edge,Alignment3D.XZ(Point(sa,x0)),Alignment3D.XZ(Point(sa,x1)),Alignment3D.XZ(Point(sb,x0)),Alignment3D.XZ(Point(sb,x1)),
                        Elevation(edge,Alignment3D.XZ(path.AtDistance(sa)))+.18f,Elevation(edge,Alignment3D.XZ(path.AtDistance(sb)))+.18f,top);
                });
            LanePaint(first, p =>
            {
                if (!edge.alignment.curved) return (edge, Vector2.Dot(p - a, right));
                float t = path.ClosestParameter(p);
                return (edge, Vector2.Dot(p - Alignment3D.XZ(path.Position(t)), Right(Alignment3D.XZ(path.Tangent(t)).normalized)));
            });
            DrapePaint(first, p => Elevation(edge, p) + Crown(edge) * Mathf.Clamp01(1f - path.DistanceTo(p) / half));
            if (piece == null) return;
            if (piece.structure == RoadStructure.Elevated)
            {
                piece.tunnelAtStart = TunnelNeighbor(edge, edge.a)?.tunnelPortalStyle;
                piece.tunnelAtEnd = TunnelNeighbor(edge, edge.b)?.tunnelPortalStyle;
            }
            bool onA = ContinuesStructure(edge.a, 0), onB = ContinuesStructure(edge.b, piece.left.Count - 1);
            bool ContinuesStructure(int node, int frame)
            {
                if (piece.structure != RoadStructure.Tunnel) return Onward(node, edge) != null;
                var joint = junctions[node];
                if (joint.IsJunction || joint.arms.Count != 2) return false;
                var adjacent = joint.arms.First(a => a.edge != edge).edge;
                return adjacent.structure == RoadStructure.Tunnel || PortalLining(adjacent) != null ||
                    adjacent.structure == RoadStructure.Elevated && StraightOn(joint) &&
                    RoadStructureBuilder.DeckNeedsLining(piece.left[frame], piece.right[frame], piece.verge, StructureGround, waterHeight);
            }
            piece.capStart = !onA && !roundA; piece.abutStart = !onA && MeetsGround(edge.a, edge);
            piece.capEnd = !onB && !roundB; piece.abutEnd = !onB && MeetsGround(edge.b, edge);
            // a dead end: the parapets or walls go round the pavement's rounded end
            piece.endStart = endA; piece.endEnd = endB;
            piece.trimStart = roundA ? radius : 0f; piece.trimEnd = roundB ? radius : 0f;
            piece.run = RunOf(edge); piece.length = PieceLength(edge);
            piece.bridge = BridgeFor(edge);
            RoadStructureBuilder.Build(piece, StructureGround, concrete, groundTriangles, (p, radius) => MaySupport(edge, p, radius), piers, waterHeight);
        }

        // ------------------------------------------------------------------ bridges and tunnels

        internal PublicRoadSegment TunnelNeighbor(PublicRoadSegment edge, int node)
        {
            if (!hasStructures || edge.structure == RoadStructure.Tunnel) return null;
            if (structureContext != null) return structureContext.TunnelNeighbor(edge, node);
            var joint = junctions[node];
            if (joint.arms.Count != 2 || !StraightOn(joint)) return null;
            var adjacent = joint.arms.First(a => a.edge != edge).edge;
            return adjacent.structure == RoadStructure.Tunnel ? adjacent : PortalLining(adjacent);
        }

        internal bool MeetsTunnel(PublicRoadSegment edge, int node) => hasStructures && junctions[node].arms.Any(a =>
            a.edge != edge && (a.edge.structure == RoadStructure.Tunnel || PortalLining(a.edge) != null));

        // Carry concrete across short ground connectors and line ground approaches whose rock
        // reaches the roof before the authored tunnel starts.
        internal PublicRoadSegment PortalLining(PublicRoadSegment edge)
        {
            if (!hasStructures || edge.structure != RoadStructure.Ground) return null;
            if (structureContext != null) return structureContext.PortalLining(edge);
            if (portalLining.TryGetValue(edge.id, out var known)) return known;
            var path = graph.Path(edge);
            float half = Width(edge) * .5f + Mathf.Max(ShoulderWidth(edge), RoadStructureBuilder.ParapetWidth) + RoadStructureBuilder.WallThickness;
            PublicRoadSegment Adjacent(int node)
            {
                var joint = junctions[node];
                if (joint.arms.Count != 2 || !StraightOn(joint)) return null;
                var adjacent = joint.arms.First(a => a.edge != edge).edge;
                return adjacent.structure == RoadStructure.Ground ? null : adjacent;
            }
            var a = Adjacent(edge.a);
            var b = Adjacent(edge.b);
            if (a != null && b != null && RoadStructureBuilder.ShortDryApproach(path, half, waterHeight))
                return portalLining[edge.id] = a.structure == RoadStructure.Tunnel ? a : b;
            foreach (int node in new[] { edge.a, edge.b })
            {
                var adjacent = node == edge.a ? a : b;
                if (adjacent?.structure != RoadStructure.Tunnel) continue;
                for (int i = 0; i <= 3; i++)
                {
                    float offset = Mathf.Min(path.Length, i * 2f * RoadStructureBuilder.WallStep);
                    float station = node == edge.a ? offset : path.Length - offset;
                    Vector3 At(float side)
                    {
                        var point = path.Offset(station, side * half);
                        point.y = Elevation(edge, Alignment3D.XZ(point)) + .18f;
                        return point;
                    }
                    if (RoadStructureBuilder.TunnelCoverAt(At(-1f), At(1f), p => DrawnGround(Alignment3D.XZ(p))).roofed)
                        return portalLining[edge.id] = adjacent;
                }
            }
            return portalLining[edge.id] = null;
        }

        internal bool RetainsBank(PublicRoadSegment edge)
        {
            if (edge.structure == RoadStructure.Tunnel) return false;
            if (structureContext != null) return structureContext.RetainsBank(edge);
            if (!retainingBanks.TryGetValue(edge.id, out bool retainsBank))
            {
                float halfWidth = Width(edge) * .5f + Mathf.Max(ShoulderWidth(edge), RoadStructureBuilder.ParapetWidth);
                retainingBanks[edge.id] = retainsBank = RoadStructureBuilder.RetainsBank(graph.Path(edge), halfWidth, groundHeight, waterHeight);
            }
            return retainsBank;
        }

        StructurePiece Piece(PublicRoadSegment edge, float verge)
        {
            bool kerbed = edge.Type.edge == RoadEdge.Kerb;
            return new StructurePiece { structure = edge.structure, portalStyle = edge.tunnelPortalStyle, verge = Mathf.Max(verge, RoadStructureBuilder.ParapetWidth), surface = .18f,
                stripFrom = kerbed ? KerbWidth : 0f, stripRise = kerbed ? KerbHeight : 0f };
        }

        /// <summary>The section a bridge or tunnel carries on with past a node: round a bend, or straight on.</summary>
        PublicRoadSegment Onward(int node, PublicRoadSegment edge)
        {
            var j = junctions[node];
            if (j.arms.Count != 2 || j.IsJunction) return null;
            var other = j.arms[0].edge.id == edge.id ? j.arms[1].edge : j.arms[0].edge;
            return other.id != edge.id && other.structure == edge.structure ? other : null;
        }

        bool MeetsGround(int node, PublicRoadSegment edge) => junctions[node].arms.Any(a => a.edge.id != edge.id && a.edge.structure == RoadStructure.Ground);

        /// <summary>A section's length between its nodes' cuts, as its run counts it.</summary>
        float PieceLength(PublicRoadSegment edge) => Mathf.Max(0f, graph.Path(edge).Length - ArmAt(edge.a, edge.id).cut - ArmAt(edge.b, edge.id).cut);

        void AddLinks(List<Link> destination,PublicRoadSegment edge,Alignment3D path,float start,float end)
        {
            var limits=FormationLimits(edge);
            int count=path.shape.curved||path.shape.smooth?Mathf.Max(1,Mathf.CeilToInt((end-start)/8f)):1;
            float half=Width(edge)*.5f+ShoulderWidth(edge);
            var p=Alignment3D.XZ(path.AtDistance(start));
            for(int i=0;i<count;i++)
            {
                var q=Alignment3D.XZ(path.AtDistance(Mathf.Lerp(start,end,(i+1f)/count)));
                destination.Add(new Link(World(p,Elevation(edge,p)),World(q,Elevation(edge,q)),half,edge.structure,limits.start,limits.end,
                    retainsBank: RetainsBank(edge)));
                p=q;
            }
        }

        internal (GroundLimit start, GroundLimit end) FormationLimits(PublicRoadSegment edge)
        {
            if (!hasStructures || edge.structure != RoadStructure.Ground) return default;
            if (structureContext != null) return structureContext.FormationLimits(edge);
            var run = RunOf(edge);
            return run.closed ? default : (AtStructure(run.startNode), AtStructure(run.endNode));

            GroundLimit AtStructure(int node)
            {
                var joint = junctions[node];
                if (joint.arms.Count != 2) return default;
                var arm = joint.arms.Find(a => a.edge.structure != RoadStructure.Ground);
                if (arm == null) return default;
                bool tunnel = arm.edge.structure == RoadStructure.Tunnel;
                // A bridge still needs the terrain sample beyond its abutment. A tunnel's ground
                // approach ends at its mouth and keeps the slope on either side.
                const float bridgeCollar = ValleyWorld.Size / (ValleyWorld.Resolution - 1) * 1.35f;
                return new GroundLimit(arm.direction, Vector2.Dot(joint.p, arm.direction) + (tunnel ? 0f : bridgeCollar), tunnel);
            }
        }

        // ------------------------------------------------------------------ dashes round bends

        /// <summary>Where a section's lines are painted, in metres from its node a: `s0`..`s1` between its ends' cuts
        /// (short of a dead end's rounding), `stopA`..`stopB` between its stop lines.</summary>
        void PaintSpan(PublicRoadSegment edge, out float s0, out float s1, out float stopA, out float stopB)
        {
            float length = graph.Path(edge).Length, half = Width(edge) * .5f;
            s0 = ArmAt(edge.a, edge.id).cut; s1 = length - ArmAt(edge.b, edge.id).cut;
            float radius = Mathf.Min(3f, half * .55f, (s1 - s0) * .2f);
            if (RoundEnd(edge.a, edge)) s0 += radius;
            if (RoundEnd(edge.b, edge)) s1 -= radius;
            stopA = Mathf.Max(s0, StopLine(edge.a, edge.id)); stopB = Mathf.Min(s1, length - StopLine(edge.b, edge.id));
            if (stopB < stopA) stopA = stopB = (s0 + s1) * .5f;
        }

        /// <summary>The part of a section its dashed lines run along (Markings): the centre line and the lines between
        /// lanes are both solid on the approach to a stop line. Empty where nothing is dashed.</summary>
        (float from, float to) DashSpan(PublicRoadSegment edge)
        {
            PaintSpan(edge, out _, out _, out float stopA, out float stopB);
            if (stopB - stopA < .5f) return (stopA, stopA);
            float solid = Mathf.Min(SolidApproach, (stopB - stopA) / 3f);
            float da = StopsAt(edge.a, edge) ? stopA + solid : stopA, db = StopsAt(edge.b, edge) ? stopB - solid : stopB;
            return db - da > 1f ? (da, db) : (da, da);
        }

        /// <summary>
        /// The two sections a road's dashed lines carry on between at a node, and the distance from one's cut to the
        /// other's: either side of a bend or a plain join in the same road, or (the centre line only, not `lanes`) the
        /// road straight through a junction where it has no stop line: a priority road past a side road.
        /// </summary>
        bool Through(Junction j, bool lanes, out Arm first, out Arm second, out float across)
        {
            first = second = null; across = 0f;
            if (!j.IsJunction)
            {
                if (j.arms.Count != 2 || j.arms[0].edge.id == j.arms[1].edge.id || j.arms[0].edge.style != j.arms[1].edge.style) return false;
                first = j.arms[0]; second = j.arms[1]; across = j.IsCurve ? ArcLength(j) : 0f;
                return true;
            }
            if (lanes || j.arms.Count < 3) return false;
            foreach (var a in j.arms)
            {
                var t = a.edge.Type;
                if (t.OneWay || t.Divided || t.centre != CentreLine.Dashed || a.control != ApproachControl.None) continue;
                foreach (var b in j.arms)
                {
                    if (b == a || b.edge.style != a.edge.style || b.control != ApproachControl.None) continue;
                    if (Vector2.Dot(a.direction, b.direction) > -.9f) continue;
                    first = a; second = b;
                    across = Vector2.Distance(j.p + a.direction * a.cut, j.p + b.direction * b.cut);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether a section's dashed lines carry on past a node into another section (Through).</summary>
        bool Continues(int node, PublicRoadSegment edge, bool lanes) =>
            junctions.TryGetValue(node, out var j) && Through(j, lanes, out var first, out var second, out _) &&
            (first.edge.id == edge.id || second.edge.id == edge.id);

        /// <summary>
        /// Dashes are laid out in stretches that run through a bend, or through a junction the road carries straight
        /// on across (Through; PaintBuilder.Dashes): from the middle of the section before it, round the arc or across
        /// the junction, to the middle of the section after it, or to that section's far end where its dashes stop
        /// there (a stop line, a dead end). The sections and the bend or junction all paint their own part of the
        /// same stretch, so the dashes keep their rhythm through it; the stretches meet in a gap in the middle of a
        /// section. It depends only on the two sections and the nodes at their ends, like the bend's own shape, so a
        /// change further along the road never moves these dashes (PublicRoadPreviewPartition).
        /// `before`: the stretch's length up to the bend or junction, `arc`: its length through it.
        /// </summary>
        void DashStretch(Junction j, bool lanes, out float before, out float arc, out float total)
        {
            float Share(Arm arm)
            {
                var e = arm.edge; var (from, to) = DashSpan(e);
                return Mathf.Max(0f, Continues(e.a, e, lanes) && Continues(e.b, e, lanes) ? (to - from) * .5f : to - from);
            }
            Through(j, lanes, out var first, out var second, out arc);
            before = Share(first); total = before + arc + Share(second);
        }

        /// <summary>A section's dashes between `from` and `to` along it: its part of the stretch through each end
        /// where the road carries on (DashStretch), or a stretch of its own.</summary>
        void SectionDashes(PublicRoadSegment edge, float x, float from, float to, float gap, Color color, bool lanes,
            Func<float, float, float, List<Vector2>> line)
        {
            bool onA = Continues(edge.a, edge, lanes), onB = Continues(edge.b, edge, lanes);
            if (!onA && !onB) { paint.Dashes((a, b) => line(x, a, b), from, to, from, to, LineHalf, 3f, gap, color); return; }
            float mid = onA && onB ? (from + to) * .5f : onA ? to : from;
            foreach (bool atA in new[] { true, false })
            {
                if (!(atA ? onA : onB)) continue;
                var j = junctions[atA ? edge.a : edge.b];
                Through(j, lanes, out var firstArm, out _, out _);
                bool first = firstArm.edge.id == edge.id;
                DashStretch(j, lanes, out float before, out float arc, out float total);
                float own = atA ? mid - from : to - mid;
                // u runs along the stretch; d back along this section from its cut at the node
                float Station(float u) { float d = first ? before - u : u - before - arc; return atA ? from + d : to - d; }
                float u0 = first ? before - own : before + arc, u1 = first ? before : before + arc + own;
                paint.Dashes((a, b) =>
                {
                    float sa = Station(a), sb = Station(b);
                    var points = line(x, Mathf.Min(sa, sb), Mathf.Max(sa, sb));
                    if (sa > sb) points.Reverse();
                    return points;
                }, u0, u1, 0f, total, LineHalf, 3f, gap, color);
            }
        }

        static float ArcLength(Junction j)
        {
            float total = 0f;
            if (j.arc != null) for (int k = 1; k < j.arc.Count; k++) total += Vector2.Distance(j.arc[k - 1], j.arc[k]);
            return total;
        }

        readonly Dictionary<int, RunSpan> edgeRuns = new Dictionary<int, RunSpan>(), bendRuns = new Dictionary<int, RunSpan>();
        readonly Dictionary<int,RoadBridgeSpan> bridgeSpans = new Dictionary<int,RoadBridgeSpan>();
        float StructureGround(Vector2 point) => structureContext != null ? structureContext.DrawnGround(point) : DrawnGround(point);

        internal RoadSuspensionSpan SuspensionFor(PublicRoadSegment edge)=>BridgeFor(edge).suspension;

        internal RoadBridgeSpan BridgeFor(PublicRoadSegment edge)
        {
            if(edge.structure!=RoadStructure.Elevated)return default;
            if(structureContext!=null)return structureContext.BridgeFor(edge);
            var run=RunOf(edge);
            if(run.closed||run.total<RoadArchSpan.MinimumSpan)return default;
            if(bridgeSpans.TryGetValue(run.firstEdge,out var known))return known;
            var members=edgeRuns.Where(item=>item.Value.firstEdge==run.firstEdge).OrderBy(item=>item.Value.offset).ToArray();
            var design=members.Select(item=>graph.Segment(item.Key).bridgeDesign).FirstOrDefault(d=>d!=RoadBridgeDesign.Automatic);
            var sections=new List<RoadSuspensionSpan.Section>();
            foreach(var item in members)
            {
                var member=graph.Segment(item.Key);var span=item.Value;var path=graph.Path(member);
                float cutA=ArmAt(member.a,member.id).cut,cutB=ArmAt(member.b,member.id).cut;
                float length=PieceLength(member),half=Width(member)*.5f+ShoulderWidth(member);
                if(length<=.01f)continue;
                int count=Mathf.CeilToInt(length/8f);
                for(int i=0;i<=count;i++)
                {
                    float d=length*i/count;float s=cutA+d;
                    var at=Alignment3D.XZ(path.Offset(s,0f,cutA,cutB));
                    var tangent=Alignment3D.XZ(path.Tangent(path.ParameterAtDistance(s))).normalized;
                    sections.Add(new RoadSuspensionSpan.Section{station=span.offset+(span.reversed?length-d:d),
                        centre=World(at,Elevation(member,at)+.18f),across=World(Right(tangent)*(span.reversed?-1f:1f),0f),halfWidth=half});
                }
            }
            var layout=RoadBridgeSpan.Plan(run,sections,design,DrawnGround,waterHeight,
                (p,radius)=>MaySupport(graph.Segment(run.firstEdge),p,radius));
            bridgeSpans.Add(run.firstEdge,layout);
            return layout;
        }

        internal bool SetBridgeDesign(IEnumerable<int> sections,RoadBridgeDesign design)
        {
            var runs=new HashSet<int>();
            foreach(int id in sections)
            {
                var edge=graph.Segment(id);
                if(edge?.structure==RoadStructure.Elevated)runs.Add(RunOf(edge).firstEdge);
            }
            bool changed=false;
            foreach(var item in edgeRuns)
            {
                if(!runs.Contains(item.Value.firstEdge))continue;
                var edge=graph.Segment(item.Key);
                if(edge.structure!=RoadStructure.Elevated||edge.bridgeDesign==design)continue;
                edge.bridgeDesign=design;
                changed=true;
            }
            if(changed)bridgeSpans.Clear();
            return changed;
        }

        internal bool SetTunnelPortalStyle(PublicRoadSegment selected, TunnelPortalStyle style)
        {
            int run = RunOf(selected).firstEdge;
            bool changed = false;
            foreach (var item in edgeRuns)
            {
                if (item.Value.firstEdge != run) continue;
                var edge = graph.Segment(item.Key);
                if (edge.tunnelPortalStyle == style) continue;
                edge.tunnelPortalStyle = style;
                changed = true;
            }
            return changed;
        }

        /// <summary>Where a section lies along its bridge (RoadStructureBuilder.Runs): the same answer from any chunk.</summary>
        RunSpan RunOf(PublicRoadSegment edge)
        {
            if (edgeRuns.TryGetValue(edge.id, out var run)) return run;
            RoadStructureBuilder.Runs(edge.id, id => { var e = graph.Segment(id); return (e.a, e.b); },
                (node, id) => Onward(node, graph.Segment(id))?.id ?? -1, id => PieceLength(graph.Segment(id)),
                (node, id) => { var j = junctions[node]; return j.IsCurve ? (ArcLength(j), j.arms[0].edge.id != id) : (0f, false); },
                (node, id) => !MeetsGround(node, graph.Segment(id)), edgeRuns, bendRuns);
            return edgeRuns[edge.id];
        }

        /// <summary>Whether a bridge column may stand here: not on a road passing below.</summary>
        bool MaySupport(PublicRoadSegment edge, Vector3 p, float radius)
        {
            var at = PublicRoadNetwork.XZ(p);
            foreach (var lower in graph.segments)
            {
                if (lower.id == edge.id || lower.structure == RoadStructure.Tunnel) continue;
                var path = graph.Path(lower);
                if (path.DistanceTo(at) < Width(lower) * .5f + ShoulderWidth(lower) + radius && path.Height(path.ClosestParameter(at)) < p.y - 1f) return false;
            }
            return true;
        }

        /// <summary>A dead end is finished with rounded corners, on the ground, on a bridge and in a tunnel alike.</summary>
        bool RoundEnd(int node,PublicRoadSegment edge)=>
            !graph.Node(node).outsideConnection && graph.Node(node).terminalEntrance!=VisitorCentreLayout.EntranceId &&
            !AirCity.Airfield.FlyingSchoolLayout.IsEntrance(graph.Node(node).terminalEntrance) && junctions[node].arms.Count==1;

        /// <summary>The pavement of a dead end and, on the ground, its kerb and verge. Returns the end's outline (on
        /// the pavement, with the direction out of the road at each point) for a bridge's or tunnel's edge to follow.</summary>
        List<(Vector3 point,Vector3 normal)> BuildEnd(PublicRoadSegment edge,int node,Vector2 outward,float half,float verge,float radius,bool retainsBank)
        {
            var end=PublicRoadNetwork.XZ(graph.Node(node).position);var right=Right(outward);
            var inner=SurfaceBuilder.RoadEndOutline(end,outward,half,radius);
            // Offset the same sampled outline; matching subdivisions keep the curved shoulder watertight.
            var outer=new List<Vector2>();var normals=new List<Vector2>();
            foreach(var p in inner)
            {
                Vector2 normal;
                float x=Vector2.Dot(p-end,right),s=Vector2.Dot(p-end,outward);
                if(Mathf.Abs(x)>half-radius+.001f)
                {var arcCentre=end-outward*radius+right*Mathf.Sign(x)*(half-radius);normal=(p-arcCentre).normalized;}
                else normal=outward;
                outer.Add(p+normal*verge);normals.Add(normal);
            }
            bool raised=edge.Type.edge==RoadEdge.Kerb,built=edge.structure!=RoadStructure.Ground;
            float Crowned(Vector2 p)=>Elevation(edge,p)+.18f+Crown(edge)*Mathf.Clamp01(1f-Mathf.Abs(Vector2.Dot(p-end,right))/half);
            // on a bridge or in a tunnel the pavement falls to its edge all the way round: the parapet or wall stays level
            float Height(Vector2 p)=>built?Elevation(edge,p)+.18f:Crowned(p);
            var centre=end-outward*(radius*.5f);
            for(int i=0;i<inner.Count;i++)
                Quad(surface,World(centre,Crowned(centre)),World(inner[i],Height(inner[i])),World(inner[(i+1)%inner.Count],Height(inner[(i+1)%inner.Count])),World(centre,Crowned(centre)));
            for(int i=0;i+1<inner.Count;i++)
            {
                var a=inner[i];var b=inner[i+1];var aa=outer[i];var bb=outer[i+1];
                float ya=Height(a),yb=Height(b);
                if(raised)
                {
                    // a bridge or tunnel paves its own sidewalk behind the kerb, out to its parapet or wall
                    if(built||retainsBank)Kerb(World(a,ya+KerbHeight),World(b,yb+KerbHeight),World(b+normals[i+1]*KerbWidth,yb+KerbHeight),World(a+normals[i]*KerbWidth,ya+KerbHeight),!built,retainsBank);
                    else
                    {
                        Quad(kerbs,World(a,ya),World(b,yb),World(b,yb+KerbHeight),World(a,ya+KerbHeight));
                        GroundShoulder(World(a,ya+KerbHeight),World(b,yb+KerbHeight),World(bb,yb+KerbHeight),World(aa,ya+KerbHeight));
                    }
                }
                else if(!built&&!retainsBank)GroundShoulder(World(a,ya-.035f),World(b,yb-.035f),World(bb,yb-.09f),World(aa,ya-.09f), pavementStep: .035f);
            }
            if(!raised)
            {
                int first=paint.mesh.Count;
                var line=SurfaceBuilder.RoadEndOutline(end-outward*.25f,outward,half-.25f,Mathf.Max(.1f,radius-.25f));
                paint.Strip(line,LineHalf,PaintBuilder.White);LanePaint(first,_=>(null,0f));DrapePaint(first,p=>Height(p)-.18f);
            }
            var ring=new List<(Vector3,Vector3)>(inner.Count);
            for(int i=0;i<inner.Count;i++)ring.Add((World(inner[i],Height(inner[i])),World(normals[i],0f)));
            return ring;
        }

        void BuildJunction(Junction junction)
        {
            var centre = junction.p;
            float y = junction.node.position.y;
            int paved = surface.Count;
            if (junction.outline.Count >= 3) Fan(surface, junction.node.position + Vector3.up * .19f, junction.outline);
            // tyre tracks carry on across the junction along the road that runs straight through it (the most nearly
            // opposite pair of arms of one road type); the whole junction is worn by braking and turning traffic
            Arm through = null; float straightest = -.9f;
            foreach (var a in junction.arms)
            foreach (var b in junction.arms)
            {
                if (a == b || a.edge.style != b.edge.style) continue;
                float dot = Vector2.Dot(a.direction, b.direction);
                if (dot < straightest) { straightest = dot; through = a; }
            }
            for (int i = paved; i < surface.Count; i++)
            {
                var c = surface.colors[i]; c.b = junction.arms.Count >= 3 ? 1f : 0f; surface.colors[i] = c;
                // lanes negative: a junction, scrubbed all over (AirfieldSurface)
                if (through == null) { surface.uv1[i] = new Vector4(0f, 0f, -.25f, 0f); continue; }
                float x = Vector2.Dot(new Vector2(surface.positions[i].x, surface.positions[i].z) - centre, Right(through.direction));
                LaneVertex(i, through.edge, through.halfWidth, through.edge.a == junction.node.id ? x : -x, c.b);
                var lanes = surface.uv1[i]; lanes.z = -lanes.z; surface.uv1[i] = lanes;
            }
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
                // A kerbed corner carries a level sidewalk. A gravel shoulder falls away from the pavement.
                float rise = raised ? .19f + KerbHeight : .145f, fall = raised ? .19f + KerbHeight : .09f, start = raised ? KerbWidth : 0f;
                // between two bridges or tunnels the corner is concrete like their verges, never gravel
                bool built = arm.edge.structure != RoadStructure.Ground && next.edge.structure != RoadStructure.Ground;
                bool retaining = raised && !built && (RetainsBank(arm.edge) || RetainsBank(next.edge));
                bool meetsTunnel = !built && (arm.edge.structure == RoadStructure.Tunnel || next.edge.structure == RoadStructure.Tunnel ||
                    PortalLining(arm.edge) != null || PortalLining(next.edge) != null);
                if (built && !raised) rise = fall = .19f;
                for (int k = 0; k + 1 < kerb.Count; k++)
                {
                    float w0 = Mathf.Lerp(v0, v1, k / (kerb.Count - 1f)), w1 = Mathf.Lerp(v0, v1, (k + 1f) / (kerb.Count - 1f));
                    Vector3 c0 = World(kerb[k] + outward[k] * start, y + rise), c1 = World(kerb[k + 1] + outward[k + 1] * start, y + rise),
                        c2 = World(kerb[k + 1] + outward[k + 1] * w1, y + fall), c3 = World(kerb[k] + outward[k] * w0, y + fall);
                    if (raised) Kerb(World(kerb[k], y + rise), World(kerb[k + 1], y + rise), c1, c0, !built, retaining);
                    if (built) concrete.Face(c0, c1, c2, c3, Vector3.up);
                    else GroundShoulder(c0, c1, c2, c3, pavementStep: raised ? 0f : .19f - rise, seal: meetsTunnel);
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
                    if (arm.edge.Type.edge != RoadEdge.Kerb && next.edge.Type.edge != RoadEdge.Kerb)
                        paint.Strip(Inset(arm.kerb, .25f), .075f, PaintBuilder.White);
                }
                if (n >= 3) Approach(junction, arm);
            }
            if (Through(junction, false, out var main, out var opposite, out float across) && across > .01f)
            {
                // the road straight through keeps its centre line across the junction: the junction's part of the
                // stretch that runs through it from the sections either side
                DashStretch(junction, false, out float before, out _, out float whole);
                Vector2 p0 = centre + main.direction * main.cut, p1 = centre + opposite.direction * opposite.cut;
                paint.Dashes((a, b) => new[] { Vector2.Lerp(p0, p1, (a - before) / across), Vector2.Lerp(p0, p1, (b - before) / across) },
                    before, before + across, 0f, whole, LineHalf, 3f, DashGap, PaintBuilder.Yellow);
            }
            LanePaint(first, p =>
            {
                if (Geo.Inside(junction.outline, p)) return (null, 0f);
                var on = NearestArm(junction, p);
                float x = Vector2.Dot(p - junction.p, Right(on.direction));
                return (on.edge, on.edge.a == junction.node.id ? x : -x);
            });
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

        /// <summary>The arm of a junction a point beside it lies on: the one whose centreline is nearest.</summary>
        Arm NearestArm(Junction junction, Vector2 p)
        {
            Arm best = null; float distance = float.MaxValue;
            foreach (var arm in junction.arms)
            {
                var end = PublicRoadNetwork.XZ(graph.Node(arm.edge.a == junction.node.id ? arm.edge.b : arm.edge.a).position);
                float d = (Geo.ClosestOnSegment(p, junction.p, end, out _) - p).sqrMagnitude;
                if (d < distance) { distance = d; best = arm; }
            }
            return best;
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
            var limits=FormationLimits(edge);
            for (int k = 0; k + 1 < m; k++)
                links.Add(new Link(World(arc[k], Mathf.Lerp(h0, h1, along[k] / total)), World(arc[k + 1], Mathf.Lerp(h0, h1, along[k + 1] / total)),
                    half + verge, edge.structure,limits.start,limits.end, RetainsBank(edge) || RetainsBank(a1.edge)));
            float[] xs = { -half, 0f, half };
            // a0 runs away from the node and so does the arc, starting on a0's side: x to the arc's right is a0's left
            bool flip = edge.a == junction.node.id;
            // where a ground road meets a bridge or tunnel the bend lies on the ground, like the abutment it leads to
            bool built = a0.edge.structure != RoadStructure.Ground && a1.edge.structure != RoadStructure.Ground;
            bool retainsBank = RetainsBank(a0.edge) || RetainsBank(a1.edge);
            bool concreteShoulder = built || retainsBank;
            for (int k = 0; k + 1 < m; k++)
            {
                for (int col = 0; col < 2; col++)
                {
                    int i = surface.Count;
                    Vector3 pa = At(k, xs[col]), pb = At(k + 1, xs[col]);
                    Quad(surface, pa, pb, At(k + 1, xs[col + 1]), At(k, xs[col + 1]));
                    for (int v = i; v < surface.Count; v++)
                    {
                        float x = surface.positions[v] == pa || surface.positions[v] == pb ? xs[col] : xs[col + 1];
                        LaneVertex(v, edge, half, flip ? -x : x, 0f);
                    }
                }
                foreach (float side in new[] { -1f, 1f })
                    if (edge.Type.edge == RoadEdge.Kerb)
                    {
                        float back = (half + KerbWidth) * side;
                        Kerb(At(k, half * side, KerbHeight), At(k + 1, half * side, KerbHeight),
                            At(k + 1, back, KerbHeight), At(k, back, KerbHeight), !built, retainsBank);
                        if (concreteShoulder) continue;
                        GroundShoulder(At(k, back, KerbHeight), At(k + 1, back, KerbHeight), At(k + 1, (half + verge) * side, KerbHeight),
                            At(k, (half + verge) * side, KerbHeight));
                    }
                    else if (!concreteShoulder) GroundShoulder(At(k, half * side, -.035f), At(k + 1, half * side, -.035f), At(k + 1, (half + verge) * side, -.09f), At(k, (half + verge) * side, -.09f), pavementStep: .035f);
            }
            paint.frame = RunwayFrame.None;
            int first = paint.mesh.Count;
            List<Vector2> Line(float x)
            {
                var line = new List<Vector2>(m);
                for (int k = 0; k < m; k++) line.Add(arc[k] + right[k] * x);
                return line;
            }
            Markings(edge, half, 0f, total, 0f, total, false, false, (x, f, t) => Geo.Slice(Line(flip ? -x : x), along, f, t),
                (x, f, t, gap, color, lanes) =>
                {
                    // the bend's part of the stretch that runs through it from the sections either side
                    DashStretch(junction, lanes, out float before, out _, out float whole);
                    var line = Line(flip ? -x : x);
                    paint.Dashes((a, b) => Geo.Slice(line, along, a - before, b - before), before + f, before + t, 0f, whole, LineHalf, 3f, gap, color);
                });
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
            LanePaint(first, p =>
            {
                int near = 0; float nearD = float.MaxValue;
                for (int k = 0; k < m; k++)
                {
                    float d = (arc[k] - p).sqrMagnitude;
                    if (d < nearD) { nearD = d; near = k; }
                }
                float x = Vector2.Dot(p - arc[near], right[near]);
                return (edge, flip ? -x : x);
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
            if (!built && !retainsBank) return;
            // one unbroken piece round the bend, on the pavement's own cross-sections: it meets both sections' ends exactly
            var piece = Piece(edge, verge);
            piece.retainsBank = retainsBank;
            for (int k = 0; k < m; k++) piece.Add(At(k, -half), At(k, half));
            RunOf(edge); piece.run = bendRuns.TryGetValue(junction.node.id, out var run) ? run : default; piece.length = ArcLength(junction);
            piece.capStart = piece.capEnd = a0.edge.structure != a1.edge.structure;
            piece.bridge = BridgeFor(edge);
            RoadStructureBuilder.Build(piece, StructureGround, concrete, groundTriangles, (p, radius) => MaySupport(a0.edge, p, radius) && MaySupport(a1.edge, p, radius), piers, waterHeight);
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
        /// <param name="dashes">Paints a dashed line at offset x between two distances (3 m dashes, the gap given;
        /// lanes: a line between lanes, not the centre line), laid out with the stretch it belongs to (DashStretch).</param>
        void Markings(PublicRoadSegment edge, float half, float from, float to, float stopA, float stopB, bool solidA, bool solidB,
            Func<float, float, float, List<Vector2>> line, Action<float, float, float, float, Color, bool> dashes)
        {
            var t = edge.Type;
            float scale = half / Mathf.Max(t.Width * .5f, .01f);   // a profile may widen a country road: lanes scale with it
            if (stopB - stopA < .5f) return;
            if (t.edge == RoadEdge.Shoulder)
                foreach (float side in new[] { -1f, 1f }) paint.Strip(line((half - .25f) * side, from, to), LineHalf, PaintBuilder.White);
            // dashed lines are solid for the last stretch before a stop line: no passing and no lane changes on the
            // approach (the same spans as DashSpan)
            float solid = Mathf.Min(SolidApproach, (stopB - stopA) / 3f);
            float da = solidA ? stopA + solid : stopA, db = solidB ? stopB - solid : stopB;
            bool dashed = db - da > 1f;
            if (!dashed) da = db = solidA ? stopB : stopA;
            if (!t.OneWay && !t.Divided)
            {
                // the centre line: yellow, broken where passing is allowed, double solid otherwise and up to a stop line
                void Double(float f, float to2)
                {
                    if (to2 - f < .05f) return;
                    foreach (float side in new[] { -1f, 1f }) paint.Strip(line(DoubleLine * side, f, to2), LineHalf, PaintBuilder.Yellow);
                }
                if (t.centre != CentreLine.Dashed) Double(stopA, stopB);
                else
                {
                    if (dashed) dashes(0f, da, db, DashGap, PaintBuilder.Yellow, false);
                    Double(stopA, da); Double(db, stopB);
                }
            }
            // lines between lanes running the same way
            var lanes = t.LaneCentres();
            for (int i = 0; i + 1 < lanes.Count; i++)
            {
                if (lanes[i].forwards != lanes[i + 1].forwards) continue;
                float x = (lanes[i].x + lanes[i + 1].x) * .5f * scale;
                if (dashed) dashes(x, da, db, DashGap, PaintBuilder.White, true);
                if (da - stopA > .05f) paint.Strip(line(x, stopA, da), LineHalf, PaintBuilder.White);
                if (stopB - db > .05f) paint.Strip(line(x, db, stopB), LineHalf, PaintBuilder.White);
            }
            if (t.Divided)
                foreach (float side in new[] { -1f, 1f })
                    paint.Strip(line((t.median * .5f + t.innerShoulder - .2f) * side * scale, stopA, stopB), LineHalf, PaintBuilder.Yellow);
        }

        /// <summary>
        /// Tyre wear is drawn by the shaders from where a point lies in a road's lanes. A pavement vertex `across`
        /// metres right of the road's centreline (looking from node a to b) carries: uv0.z = across, uv0.w = the lane
        /// width (negative on a one-way road, whose lanes run across from one side), uv1.z = the lanes each way,
        /// uv1.w = where the lanes start from the centreline (past a median and its shoulder; on a one-way road its
        /// first lane's edge), colour b = how much traffic brakes and waits here (0..1: near a stop line, in a junction).
        /// In a junction the lanes are the through road's, negated: the whole junction is scrubbed by turning traffic.
        /// </summary>
        void LaneVertex(int i, PublicRoadSegment edge, float half, float across, float braking)
        {
            var t = edge.Type;
            float scale = half / Mathf.Max(t.Width * .5f, .01f), width = t.laneWidth * scale;
            float inner = t.OneWay ? -(half - t.innerShoulder * scale) : t.Divided ? (t.median * .5f + t.innerShoulder) * scale : 0f;
            var p = surface.positions[i];
            surface.uv0[i] = new Vector4(p.x, p.z, across, t.OneWay ? -width : width);
            surface.uv1[i] = new Vector4(0f, 0f, Mathf.Max(t.forward, t.backward), inner);
            var c = surface.colors[i]; c.b = braking; surface.colors[i] = c;
        }

        /// <summary>
        /// Paint wears too, fastest under the wheels: every paint vertex added since `first` carries, in place of a
        /// runway's frame, uv3 = (metres from the inner edge of its side's lanes, lanes that side, minus the lane
        /// width, wear). `at` gives the road a point is on and how far right of its centreline (a to b) it is; no
        /// road for paint inside a junction (lanes -1), which wears evenly.
        /// </summary>
        void LanePaint(int first, Func<Vector2, (PublicRoadSegment edge, float across)> at)
        {
            for (int i = first; i < paint.mesh.Count; i++)
            {
                var p = paint.mesh.positions[i];
                var (edge, across) = at(new Vector2(p.x, p.z));
                // white paint is renewed more often than the yellow centre lines
                var colour = paint.mesh.colors[i];
                float wear = colour.b > .5f ? WhitePaintWear : PaintWear;
                if (edge == null) { paint.mesh.uv3[i] = new Vector4(0f, -1f, -1f, wear); continue; }
                var t = edge.Type;
                float half = Width(edge) * .5f, scale = half / Mathf.Max(t.Width * .5f, .01f), width = t.laneWidth * scale;
                float metres = t.OneWay ? across + half - t.innerShoulder * scale
                    : Mathf.Abs(across) - (t.Divided ? (t.median * .5f + t.innerShoulder) * scale : 0f);
                paint.mesh.uv3[i] = new Vector4(metres, t.OneWay || across >= 0f ? t.forward : t.backward, -width, wear);
            }
        }

        /// <summary>How worn public-road paint is (0 fresh .. 1 nearly gone): chipped all over, more under the wheels.</summary>
        const float PaintWear = .45f, WhitePaintWear = .3f;

        /// <summary>Broken lines are 3 m dashes 9 m apart (10 ft and 30 ft); each line of a double line is this far
        /// from the road's centre.</summary>
        const float DashGap = 9f, DoubleLine = .15f;

        /// <summary>Lane lines are solid this far back from a junction's stop line.</summary>
        const float SolidApproach = 25f;

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
            return arm == null ? 0f : arm.stop - arm.cut + 1f;
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
            Quad(verges, World(inL0, y0 + top), World(inL1, y1 + top), World(inR1, y1 + top), World(inR0, y0 + top));
        }

        void Concrete(Vector2 a0, Vector2 a1, Vector2 b1, Vector2 b0, float y0, float y1, float ha, float hb) =>
            concrete.TwoSided(World(a0, y0 + ha), World(a1, y1 + ha), World(b1, y1 + hb), World(b0, y0 + hb));

        // The pavement ends at the kerb's front and the sidewalk starts at its back. The kerb
        // owns the strip between them on ground roads. Bridges and tunnels supply their own cut.
        void Kerb(Vector3 frontA, Vector3 frontB, Vector3 backB, Vector3 backA, bool ground, bool retainsBank)
        {
            var rise = Vector3.up * KerbHeight;
            kerbs.TwoSided(frontA, frontB, frontB - rise, frontA - rise);
            kerbs.TwoSided(frontA, frontB, backB, backA);
            if (!ground) return;
            float height = retainsBank ? RoadStructureBuilder.PavementClipHeight : 0f;
            groundTriangles.Add(new BuiltSurfaceMap.Triangle(frontA, frontB, backB, excavationHeight: height, excavationDepth: MeshData.RoadEdgeDepth));
            groundTriangles.Add(new BuiltSurfaceMap.Triangle(frontA, backB, backA, excavationHeight: height, excavationDepth: MeshData.RoadEdgeDepth));
        }

        /// <summary>
        /// Paint where a road meets a junction, as on North American roads: a crosswalk of bars along the road where it
        /// has sidewalks and traffic stops, a solid stop line across the lanes arriving (from the centre line to the
        /// edge line or kerb, never onto the shoulder) where they stop for a sign or a signal, and on an approach of
        /// two or more lanes an arrow in each showing the moves it may make (LaneConnections). A single lane carries
        /// no arrow, and the road straight through a priority junction has no stop line.
        /// </summary>
        void Approach(Junction junction, Arm arm)
        {
            var t = arm.edge.Type;
            var u = arm.direction;
            var across = Left(u);                                     // seen arriving, the approach lanes are on this side
            var centre = junction.p;
            if (arm.crossing)
            {
                // bars 0.6 m wide and 0.6 m apart, centred across the road and clear of its kerbs
                float along = arm.cut + CrosswalkStart + CrosswalkLength * .5f, half = arm.halfWidth - .4f;
                int bars = Mathf.Max(1, Mathf.FloorToInt((2f * half + .6f) / 1.2f));
                float first = -(bars - 1) * .6f;
                for (int k = 0; k < bars; k++)
                    paint.Box(centre + u * along + across * (first + k * 1.2f), u, .3f, CrosswalkLength * .5f, PaintBuilder.White);
            }
            if (arm.control == ApproachControl.None) return;
            bool arriving = arm.edge.b == junction.node.id;          // traffic coming in drives along -direction
            int lanesIn = arriving ? t.forward : t.backward;
            float scale = arm.halfWidth / Mathf.Max(t.Width * .5f, .01f);
            var (inner, outer) = t.Carriageway(arriving);
            // the approach lanes across: from the centre side to the kerb side, in "across" units
            float a0 = Mathf.Abs(inner) * scale, a1 = Mathf.Abs(outer) * scale;
            if (t.OneWay) { a0 = -(t.Width * .5f - t.innerShoulder) * scale; a1 = (t.Width * .5f - t.outerShoulder) * scale; }
            // the stop line: from the centre line's edge (or the median's line) to the edge line
            float b0 = t.OneWay || t.Divided ? a0 : DoubleLine + LineHalf;
            float b1 = t.edge == RoadEdge.Shoulder ? Mathf.Min(a1, arm.halfWidth - .25f + LineHalf) : a1;
            paint.Strip(SampleLine(centre + u * arm.stop + across * b0, centre + u * arm.stop + across * b1), StopLineHalf, PaintBuilder.White);
            // arrows: one per lane, their tips a few metres short of the stop line; left out where they cannot fit
            // between this junction and the section's other end
            if (lanesIn < 2) return;
            var far = ArmAt(arm.edge.a == junction.node.id ? arm.edge.b : arm.edge.a, arm.edge.id);
            if (arm.length - arm.stop - far.cut < ArrowSetBack + ArrowLength + 4f) return;
            var exits = graph.LaneConnections(junction.node.id).FindAll(c => c.incoming == arm.edge.id);
            for (int lane = 0; lane < lanesIn; lane++)
            {
                // lane 0 = the kerb lane: on the "across" side, at the far end of a0..a1
                float x = Mathf.Lerp(a1, a0, (lane + .5f) / lanesIn);
                bool left = exits.Exists(c => c.incomingLane == lane && c.turn == RoadTurn.Left);
                bool straight = exits.Exists(c => c.incomingLane == lane && c.turn == RoadTurn.Straight);
                bool rightTurn = exits.Exists(c => c.incomingLane == lane && c.turn == RoadTurn.Right);
                if (!left && !straight && !rightTurn) continue;
                Arrow(centre + u * (arm.stop + ArrowSetBack + ArrowLength) + across * x, -u, left, straight, rightTurn);
            }
        }

        /// <summary>A stop line is 0.5 m wide; a lane arrow is 4.2 m long, its tip this far short of the stop line.</summary>
        const float StopLineHalf = .25f, ArrowLength = 4.2f, ArrowSetBack = 5f;

        /// <summary>Where a road's lines stop at a junction, metres from the junction's node: its stop line, or the
        /// junction's edge where it has none (0 if the node isn't a junction).</summary>
        public float StopLine(int node, int edge)
        {
            if (!junctions.TryGetValue(node, out var j) || !j.IsJunction) return 0f;
            var arm = j.arms.Find(a => a.edge.id == edge);
            return arm != null ? arm.stop : 0f;
        }

        /// <summary>
        /// A lane arrow, ArrowLength long from its tail: a shaft with one continuous head for straight on, and for
        /// each turn a branch that sweeps 60 degrees off the shaft into its own head. A shared arrow's shaft is moved
        /// off the lane's middle, away from its branch, so the whole mark sits in the middle of the lane.
        /// </summary>
        void Arrow(Vector2 tail, Vector2 forward, bool left, bool straight, bool right)
        {
            var side = Right(forward);
            const float shaft = .15f, radius = .6f, sweep = 60f * Mathf.Deg2Rad;
            if (straight && left != right) tail -= side * (right ? .3f : -.3f);
            if (straight)
            {
                paint.Strip(new[] { tail, tail + forward * (ArrowLength - 1.3f) }, shaft, PaintBuilder.White);
                paint.ArrowHead(tail + forward * ArrowLength, forward, 1.5f, .55f, PaintBuilder.White);
            }
            foreach (var (on, sign) in new[] { (left, -1f), (right, 1f) })
            {
                if (!on) continue;
                // the branch leaves the shaft part way along a shared arrow, and further along a turn arrow of its own
                var direction = side * sign;
                var start = tail + forward * (straight ? 1.2f : 2.2f);
                var pivot = start + direction * radius;
                var points = new List<Vector2>();
                if (!straight) points.Add(tail);
                for (int k = 0; k <= 6; k++)
                {
                    float angle = sweep * k / 6f;
                    points.Add(pivot - direction * (radius * Mathf.Cos(angle)) + forward * (radius * Mathf.Sin(angle)));
                }
                var heading = forward * Mathf.Cos(sweep) + direction * Mathf.Sin(sweep);
                paint.Strip(points, shaft, PaintBuilder.White);
                paint.ArrowHead(points[points.Count - 1] + heading * 1.05f, heading, 1.2f, .48f, PaintBuilder.White);
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

        void GroundShoulder(Vector3 innerA, Vector3 innerB, Vector3 outerB, Vector3 outerA, float pavementStep = 0f, bool seal = false)
        {
            Quad(shoulder, innerA, innerB, outerB, outerA);
            var outward = Geo.XZ(outerA + outerB - innerA - innerB);
            var kind = new Vector4((float)AirfieldKind.Junction, 0f);
            shoulder.RoadEdge(0, outerA, outerB, outward, Color.black, kind);
            if (seal) RoadStructureBuilder.SealShoulder(concrete, outerA, outerB, Geo.X0Z(outward.normalized), StructureGround,
                shoulderRise: pavementStep > 0f ? -.09f : KerbHeight);
            if (pavementStep > 0f)
            {
                // The gravel starts below the carriageway. Close that step before either
                // surface clips the terrain around the join.
                var top = Vector3.up * pavementStep;
                shoulder.RoadEdge(0, innerA + top, innerB + top, outward, Color.black, kind);
            }
        }

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

        public void SampleGround(Vector2 p, out float height, out float distance, out float halfWidth) =>
            SampleGround(links, p, out height, out distance, out halfWidth, out _);

        void SampleGround(List<Link> links, Vector2 p, out float height, out float distance, out float halfWidth, out bool retainsBank, bool grading=false)
        {
            height = 0f; distance = float.MaxValue; halfWidth = 3.25f; retainsBank = false;
            Link nearest = default;
            Vector2 station = default;
            foreach (var link in links)
            {
                if (link.structure != RoadStructure.Ground) continue;
                var q = Geo.ClosestOnSegment(p, link.a, link.b, out float t); float d = Vector2.Distance(p, q);
                if (d >= distance || grading&&!link.FormsGroundAt(p,d)) continue;
                distance = d; height = Mathf.Lerp(link.heightA, link.heightB, Mathf.Clamp01(t)); halfWidth = link.halfWidth;
                retainsBank = link.retainsBank;
                nearest = link;
                station = q;
            }
            if (grading && retainsBank)
            {
                // A long straight can cross a cliff, flat land and a ridge in a single authored edge.
                // Protect the uphill slope only alongside the cut at this station.
                var across = Right((nearest.b - nearest.a).normalized) * halfWidth;
                retainsBank = RoadStructureBuilder.UphillSide(World(station - across, height), World(station + across, height),
                    groundHeight, waterHeight) != 0;
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

        void RememberGround(int first, int firstShoulder, int firstCut, bool retainsBank)
        {
            Pavement retained = null;
            if (retainsBank)
                for (int i = firstCut; i < groundTriangles.Count; i++)
                {
                    var cut = groundTriangles[i];
                    if (cut.excavationHeight < RoadStructureBuilder.TunnelClearance) continue;
                    (retained ??= new Pavement()).AddTriangle(cut.a, cut.b, cut.c);
                }
            void Keep(MeshData mesh,int start)
            {
                foreach(var ids in mesh.indices)for(int i=0;i<ids.Count;i+=3)
                {
                    int a=ids[i],b=ids[i+1],c=ids[i+2];
                    if(a<start || b<start || c<start || Mathf.Min(mesh.colors[a].a,mesh.colors[b].a,mesh.colors[c].a)<.99f ||
                        Mathf.Abs(Vector3.Cross(mesh.positions[b]-mesh.positions[a],mesh.positions[c]-mesh.positions[a]).y)<.0001f)continue;
                    var centre = (mesh.positions[a] + mesh.positions[b] + mesh.positions[c]) / 3f;
                    bool inCut = retained != null && retained.Contains(Alignment3D.XZ(centre));
                    groundTriangles.Add(new BuiltSurfaceMap.Triangle(mesh.positions[a],mesh.positions[b],mesh.positions[c],
                        excavationHeight: inCut ? RoadStructureBuilder.PavementClipHeight : 0f, excavationDepth: MeshData.RoadEdgeDepth));
                }
            }
            Keep(surface,first);Keep(shoulder,firstShoulder);
            // Follow the longitudinal grade, with a flat soil formation across the carriageway. Extending a
            // crowned pavement's two inclined roof planes onto 12.5 m terrain corners digs a trench under its crown.
            Link GroundLinkAt(Vector2 point)
            {
                float best = float.MaxValue;
                Link closest = default;
                foreach (var link in links)
                {
                    if (link.structure != RoadStructure.Ground) continue;
                    float distance = (Geo.ClosestOnSegment(point, link.a, link.b, out _) - point).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    closest = link;
                }
                return closest;
            }
            void Remember(List<Vector3> positions, int start)
            {
                const float spacing = ValleyWorld.Size / (ValleyWorld.Resolution - 1);
                for (int quad = start; quad + 3 < positions.Count; quad += 4)
                {
                    var origin = positions[quad];
                    var normal = Vector3.Cross(positions[quad + 1] - origin, positions[quad + 2] - origin);
                    var centre = (positions[quad] + positions[quad + 1] + positions[quad + 2] + positions[quad + 3]) * .25f;
                    bool inCut = retained != null && retained.Contains(Alignment3D.XZ(centre));
                    var link = GroundLinkAt(new Vector2(centre.x, centre.z));
                    var axis = (link.b - link.a).normalized;
                    if (axis == Vector2.zero) axis = Vector2.right;
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
                            if (!link.AllowsFoundation(new Vector2(px, pz))) continue;
                            float height = formation + Vector2.Dot(gradient, new Vector2(px - origin.x, pz - origin.z));
                            if (inCut && groundHeight(new Vector2(px, pz)) > height + 1.25f) continue;
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

        /// <summary>A terrain sample `distance` from a ground road (at height `road` there) graded to its roadbed.</summary>
        internal static float Roadbed(float original, float road, float distance, float halfWidth, float spacing)
        {
            float run = Mathf.Max(0f, distance - Mathf.Max(halfWidth + 1f, spacing * .55f));
            return FitRoadbed(original, road + .125f, run);
        }

        // Two metres of cut or one metre of fill per horizontal metre. Ease only where the slope meets the shoulder.
        internal static float FitRoadbed(float original, float formation, float run)
        {
            float rise = run * Mathf.Lerp(.75f, 1f, Mathf.InverseLerp(0f, 6f, run));
            return Mathf.Clamp(original, formation - rise, formation + rise * 2f);
        }

        List<Link> groundRoads;
        readonly Dictionary<(int, int), float> gradingAt = new Dictionary<(int, int), float>();

        /// <summary>The ground as the valley draws it: the given terrain with every ground road's roadbed graded into
        /// it (as GradedHeight does at the terrain's samples) and interpolated between those samples as the terrain
        /// is. Bridges and tunnels are built against this, not the saved terrain: beside a ground road's cutting or
        /// embankment a retaining wall or abutment must meet the ground the player sees.</summary>
        public float DrawnGround(Vector2 p)
        {
            if (groundRoads == null)
            {
                // every ground road of the network, whichever sections this geometry builds
                groundRoads = new List<Link>();
                foreach (var edge in graph.segments)
                {
                    if (edge.structure != RoadStructure.Ground) continue;
                    var path = graph.Path(edge); float length = path.Length;
                    float start = ArmAt(edge.a, edge.id).cut, end = length - ArmAt(edge.b, edge.id).cut;
                    if (length <= .01f || end <= start) continue;
                    AddLinks(groundRoads,edge,path,start,end);
                }
                foreach (var j in junctions.Values)
                {
                    if (!j.IsCurve || j.arc == null || j.arc.Count < 2 || j.arms[0].edge.structure != RoadStructure.Ground) continue;
                    var edge = j.arms[0].edge; int m = j.arc.Count; float total = Mathf.Max(ArcLength(j), 1e-4f), gone = 0f;
                    var limits=FormationLimits(edge);
                    float h0 = Elevation(edge, j.arc[0]), h1 = Elevation(j.arms[1].edge, j.arc[m - 1]);
                    for (int k = 0; k + 1 < m; k++)
                    {
                        float next = gone + Vector2.Distance(j.arc[k], j.arc[k + 1]);
                        groundRoads.Add(new Link(World(j.arc[k], Mathf.Lerp(h0, h1, gone / total)), World(j.arc[k + 1], Mathf.Lerp(h0, h1, next / total)),
                            j.arms[0].halfWidth + ShoulderWidth(edge), edge.structure,limits.start,limits.end,
                            RetainsBank(edge) || RetainsBank(j.arms[1].edge)));
                        gone = next;
                    }
                }
            }
            if (groundRoads.Count == 0) return groundHeight(p);
            float spacing = ValleyWorld.Size / (ValleyWorld.Resolution - 1f);
            float x = (p.x + ValleyWorld.Size * .5f) / spacing, z = (p.y + ValleyWorld.Size * .5f) / spacing;
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            // how much grading moves one terrain sample
            float Change(int cx, int cz)
            {
                if (gradingAt.TryGetValue((cx, cz), out float known)) return known;
                var corner = new Vector2(cx * spacing - ValleyWorld.Size * .5f, cz * spacing - ValleyWorld.Size * .5f);
                float original = groundHeight(corner);
                SampleGround(groundRoads, corner, out float road, out float distance, out float halfWidth, out bool retainsBank, true);
                float graded = distance < EarthworkReach && !(retainsBank && original > road + 1.25f)
                    ? Roadbed(original, road, distance, halfWidth, spacing) : original;
                float change = (retainShore?.Invoke(corner, original, graded) ?? graded) - original;
                gradingAt[(cx, cz)] = change;
                return change;
            }
            return groundHeight(p) + Mathf.Lerp(Mathf.Lerp(Change(ix, iz), Change(ix + 1, iz), x - ix), Mathf.Lerp(Change(ix, iz + 1), Change(ix + 1, iz + 1), x - ix), z - iz);
        }

        float GradedHeight(ValleyWorld world, int x, int z)
        {
            int index = z * ValleyWorld.Resolution + x;
            float original = world.heights[index], graded = original;
            var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
            foreach (var box in roadbedBounds)
            {
                if (!box.Contains(p)) continue;
                SampleGround(links,p,out float h,out float d,out float halfWidth,out bool retainsBank,true);
                if (d >= EarthworkReach) continue;
                if (!retainsBank || original <= h + 1.25f)
                    graded = Roadbed(original, h, d, halfWidth, world.Spacing);
                break;
            }
            if (groundCeilings.TryGetValue(index, out float ceiling))
            {
                float floor = groundFloors.TryGetValue(index, out float support) ? Mathf.Min(support, ceiling) : float.NegativeInfinity;
                graded = Mathf.Clamp(graded, floor, ceiling);
            }
            return world.RetainShore(p, original, graded);
        }
    }
}
