using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    /// <summary>Bridges and tunnels as built: unbroken through bends, on concrete from kerb to parapet, and carried
    /// by evenly spaced piers that avoid what passes below.</summary>
    public class RoadStructureGeometryTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void Cleanup(){foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();}
        PublicRoadGeometry Geometry(PublicRoadNetwork graph,System.Func<Vector2,float> ground=null,System.Func<Vector2,float?> water=null)
        {
            var g=new PublicRoadGeometry(graph,null,null,ground??(_=>0f),waterHeight:water);
            owned.AddRange(new Object[]{g.pavement,g.shoulders,g.markings,g.kerbMesh,g.medianGrass,g.structures});return g;
        }
        static PublicRoadNetwork Road(PublicRoadStyle style,RoadStructure structure,params Vector3[] points)
        {
            var graph=new PublicRoadNetwork();int previous=graph.AddNode(points[0]);
            for(int i=1;i<points.Length;i++){int next=graph.AddNode(points[i]);graph.AddSegment(previous,next,style,structure);previous=next;}
            return graph;
        }
        static float TopRise(PublicRoadStyle style)=>.18f+RoadStructureBuilder.ParapetHeight+(RoadTypes.Of(style).edge==RoadEdge.Kerb?.15f:0f);

        /// <summary>How many separate strips the mesh's vertices at one height form (joined by triangle edges, and by
        /// lying within 1.5 mm of each other: the mesh shares no vertices between faces).</summary>
        static int StripsAt(Mesh mesh,float y)
        {
            var vertices=mesh.vertices;var triangles=mesh.triangles;var parent=new int[vertices.Length];
            for(int i=0;i<parent.Length;i++)parent[i]=i;
            int Find(int i){while(parent[i]!=i)i=parent[i]=parent[parent[i]];return i;}
            bool Top(int i)=>Mathf.Abs(vertices[i].y-y)<.003f;
            for(int t=0;t<triangles.Length;t+=3)for(int e=0;e<3;e++)
            {int a=triangles[t+e],b=triangles[t+(e+1)%3];if(Top(a)&&Top(b))parent[Find(a)]=Find(b);}
            var cells=new Dictionary<(int,int),List<int>>();
            for(int i=0;i<vertices.Length;i++)
            {
                if(!Top(i))continue;
                var cell=(Mathf.FloorToInt(vertices[i].x/.01f),Mathf.FloorToInt(vertices[i].z/.01f));
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                    if(cells.TryGetValue((cell.Item1+dx,cell.Item2+dz),out var near))
                        foreach(int other in near)if((vertices[other]-vertices[i]).sqrMagnitude<.0015f*.0015f)parent[Find(other)]=Find(i);
                if(!cells.TryGetValue(cell,out var list))cells.Add(cell,list=new List<int>());list.Add(i);
            }
            var roots=new HashSet<int>();for(int i=0;i<vertices.Length;i++)if(Top(i))roots.Add(Find(i));
            return roots.Count;
        }

        [TestCase(PublicRoadStyle.Urban,150f)] [TestCase(PublicRoadStyle.Rural,150f)] [TestCase(PublicRoadStyle.Highway,260f)]
        [TestCase(PublicRoadStyle.Boulevard,150f)] [TestCase(PublicRoadStyle.Urban,45f)] [TestCase(PublicRoadStyle.Avenue,60f)]
        public void ParapetsRunUnbrokenRoundABendAndPastStraightThroughNodes(PublicRoadStyle style,float leg)
        {
            // a straight-through node, then a 50 degree bend (a short leg shortens the bend's radius)
            var turn=Quaternion.Euler(0,-50,0)*Vector3.right*leg;
            var graph=Road(style,RoadStructure.Elevated,new Vector3(-leg*2,12,0),new Vector3(-leg,12,0),new Vector3(0,12,0),new Vector3(turn.x,12,turn.z));
            var whole=Geometry(graph);
            Assert.AreEqual(1,StripsAt(whole.structures,12+TopRise(style)),"One unbroken rail, along both sides and round both dead ends, whole build.");
            // each section and bend built as its own chunk, as the game draws them
            using var chunks=new PublicRoadChunks(null,null);chunks.Update(graph,_=>0f,null);
            Assert.AreEqual(1,StripsAt(chunks.Geometry.structures,12+TopRise(style)),"One unbroken rail, along both sides and round both dead ends, chunked build.");
        }

        [Test]
        public void DeckFollowsThePavementAlongAGradedBridge()
        {
            var graph=Road(PublicRoadStyle.Urban,RoadStructure.Elevated,new Vector3(-150,8,0),new Vector3(0,14,0),new Vector3(120,11,90));
            var g=Geometry(graph,p=>-50f);
            var road=g.pavement.vertices;var deck=g.structures.vertices;var placed=g.piers;
            Assert.Greater(deck.Length,200);int compared=0;
            foreach(var p in deck)
            {
                if(placed.Any(c=>Vector2.Distance(new Vector2(c.x,c.z),new Vector2(p.x,p.z))<4f))continue;   // a pier
                float best=float.MaxValue,height=0;
                foreach(var q in road){float d=(q.x-p.x)*(q.x-p.x)+(q.z-p.z)*(q.z-p.z);if(d<best){best=d;height=q.y;}}
                if(best>3f*3f)continue;
                Assert.Greater(p.y-height,-RoadStructureBuilder.DeckDepth-.12f,"Deck hangs below its road at "+p);
                Assert.Less(p.y-height,RoadStructureBuilder.ParapetHeight+.15f+.12f,"Parapet stands above its road at "+p);compared++;
            }
            Assert.Greater(compared,200);
        }

        [TestCase(PublicRoadStyle.Urban,RoadStructure.Elevated)] [TestCase(PublicRoadStyle.Rural,RoadStructure.Elevated)]
        [TestCase(PublicRoadStyle.Highway,RoadStructure.Elevated)] [TestCase(PublicRoadStyle.Urban,RoadStructure.Tunnel)]
        [TestCase(PublicRoadStyle.Rural,RoadStructure.Tunnel)]
        public void NoGravelOrSoilVergeOnABridgeOrInATunnel(PublicRoadStyle style,RoadStructure structure)
        {
            float y=structure==RoadStructure.Elevated?12:-12;
            var graph=new PublicRoadNetwork();
            int ground=graph.AddNode(new Vector3(-300,y,0)),start=graph.AddNode(new Vector3(-150,y,0)),bend=graph.AddNode(new Vector3(0,y,0)),end=graph.AddNode(new Vector3(100,y,100));
            graph.AddSegment(ground,start,style,RoadStructure.Ground);graph.AddSegment(start,bend,style,structure);graph.AddSegment(bend,end,style,structure);
            var g=Geometry(graph);
            Assert.Greater(g.shoulders.vertexCount,0,"The ground section keeps its verge.");
            foreach(var p in g.shoulders.vertices)Assert.LessOrEqual(p.x,-149.9f,"A soil verge on the structure at "+p);
            // the strip between carriageway and parapet or wall is concrete, at the road's (or its sidewalk's) level
            var type=RoadTypes.Of(style);float half=type.Width*.5f,level=y+.18f+(type.edge==RoadEdge.Kerb?.15f:0f);
            var strip=g.structures.vertices.Where(p=>p.x>-140&&p.x<-20&&Mathf.Abs(p.y-level)<.02f&&Mathf.Abs(p.z)>=half-.01f&&Mathf.Abs(p.z)<=half+type.verge+.01f).ToArray();
            Assert.Greater(strip.Length,20,"Concrete strip beside the carriageway.");
            Assert.IsTrue(strip.Any(p=>p.z>0)&&strip.Any(p=>p.z<0));
        }

        [TestCase(RoadStructure.Elevated, 10f, true)]
        [TestCase(RoadStructure.Tunnel, 10f, true)]
        [TestCase(RoadStructure.Tunnel, 24f, false)]
        public void OnlyShortGroundConnectorsBetweenStructuresCarryConcrete(RoadStructure next, float length, bool lined)
        {
            var graph = new PublicRoadNetwork();
            int a = graph.AddNode(new Vector3(-60f, 10f, 0f));
            int b = graph.AddNode(new Vector3(0f, 10f, 0f));
            int c = graph.AddNode(new Vector3(length, 10f, 0f));
            int d = graph.AddNode(new Vector3(length + 60f, 10f, 0f));
            graph.AddSegment(a, b, PublicRoadStyle.Avenue, RoadStructure.Elevated);
            graph.AddSegment(b, c, PublicRoadStyle.Avenue, RoadStructure.Ground);
            graph.AddSegment(c, d, PublicRoadStyle.Avenue, next);
            using var chunks = new PublicRoadChunks(null, null);
            chunks.Update(graph, _ => 10f, null);
            foreach (var geometry in new[] { Geometry(graph, _ => 10f), chunks.Geometry })
            {
                Assert.That(geometry.shoulders.vertices.Any(p => p.x > .1f && p.x < length - .1f), Is.EqualTo(!lined));
                var middle = new Vector2(length * .5f, 0f);
                Assert.That(geometry.groundTriangles.Any(t =>
                    t.excavationHeight >= RoadStructureBuilder.TunnelClearance && t.DistanceSquared(middle) < .0001f), Is.EqualTo(lined));
            }
        }

        [TestCase(10f, true)]
        [TestCase(30f, false)]
        public void CliffsideConcreteAndExcavationCarryThroughShortDips(float length, bool roofed)
        {
            var graph = Road(PublicRoadStyle.Avenue, RoadStructure.Ground, new Vector3(-50f, 10f, 0f), new Vector3(50f, 10f, 0f));
            float Ground(Vector2 p) => p.y <= 0f ? 8f : Mathf.Lerp(8f, Mathf.Abs(p.x) < length * .5f ? 9f : 22f,
                Mathf.Clamp01(p.y / 8f));
            var geometry = Geometry(graph, Ground);
            Assert.That(geometry.shoulders.vertexCount, Is.Zero, "The cliffside road should have a continuous concrete shoulder.");
            Assert.That(geometry.groundTriangles.Any(t => t.excavationHeight >= RoadStructureBuilder.TunnelClearance &&
                t.DistanceSquared(Vector2.zero) < .0001f), Is.EqualTo(roofed), "The retaining cut must match the concrete.");
            Assert.That(geometry.structures.vertices.Any(p => Mathf.Abs(p.x) < 5.5f &&
                Mathf.Abs(p.y - 10.18f - RoadStructureBuilder.TunnelClearance) < .01f), Is.EqualTo(roofed));
        }

        [Test]
        public void ACliffOnOneStretchDoesNotDisableTheRoadbedThroughADistantHill()
        {
            const int size = ValleyWorld.Resolution;
            var world = new ValleyWorld { generatorVersion = 6, heights = new float[size * size] };
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x, z) * world.Spacing - Vector2.one * (ValleyWorld.Size * .5f);
                world.heights[z * size + x] = p.x > 0f ? 60f : 8f + Mathf.Clamp(p.y, 0f, 16f);
            }
            var graph = Road(PublicRoadStyle.Avenue, RoadStructure.Ground, new Vector3(-150f,10f,0f), new Vector3(150f,10f,0f));
            var g = Geometry(graph, world.Height);
            var cliff = new Vector2(-100f, 0f);
            var hill = new Vector2(100f, 0f);
            Assert.That(g.groundTriangles.Any(t => t.excavationHeight >= RoadStructureBuilder.TunnelClearance && t.DistanceSquared(cliff) < .000001f));
            Assert.That(g.groundTriangles.Any(t => t.excavationHeight == 0f && t.DistanceSquared(hill) < .000001f));
            int centre = (size - 1) / 2;
            var height = g.GradedHeightRegion(world, new RectInt(centre + Mathf.RoundToInt(hill.x / world.Spacing), centre, 1, 1));
            Assert.That(height[0,0], Is.InRange(9.9f, 10.16f));
        }

        [Test]
        public void TunnelLiningExtendsOverTheDeckWhereACliffCrossesTheJoinAtAnAngle()
        {
            var graph = new PublicRoadNetwork();
            int a = graph.AddNode(new Vector3(-75f, 10f, 0f));
            int b = graph.AddNode(new Vector3(0f, 10f, 0f));
            int c = graph.AddNode(new Vector3(65f, 10f, 0f));
            graph.AddSegment(a, b, PublicRoadStyle.Avenue, RoadStructure.Elevated);
            graph.AddSegment(b, c, PublicRoadStyle.Avenue, RoadStructure.Tunnel);
            float Ground(Vector2 p) => Mathf.Lerp(-3f, 24f, Mathf.Clamp01((p.x + 15f + p.y * .7f) / 14f));
            using var chunks = new PublicRoadChunks(null, null);
            chunks.Update(graph, Ground, null);
            foreach (var geometry in new[] { Geometry(graph, Ground), chunks.Geometry })
            {
                var joins = new Vector2(-3f, 4f);
                var cut = geometry.groundTriangles.Where(t => t.DistanceSquared(joins) < .0001f).ToArray();
                Assert.That(cut, Is.Not.Empty);
                Assert.That(cut.All(t => t.foundation == 0 && t.excavationHeight == RoadStructureBuilder.TunnelExcavationHeight));
                Assert.That(cut.All(t => t.Height(joins) < 10.3f && t.Height(joins) + t.excavationHeight < Ground(joins)),
                    "The cut follows pavement grade and preserves rock above the lining.");
                Assert.That(geometry.structures.vertices.Any(p => p.x < -1f && p.x > -12f &&
                    Mathf.Abs(p.y - 10.18f - RoadStructureBuilder.TunnelExcavationHeight) < .015f), Is.True,
                    "Rock above the deck's clearance needs a concrete roof.");
                var away = new Vector2(-45f, 0f);
                var open = geometry.groundTriangles.Where(t => t.DistanceSquared(away) < .0001f).ToArray();
                Assert.That(open, Is.Not.Empty);
                Assert.That(open.All(t => !t.OwnsGround && t.Height(away) > Ground(away) + 1f &&
                    t.excavationHeight == RoadStructureBuilder.TunnelClearance), "The open deck preserves the land beneath it.");
            }
        }

        [Test]
        public void KerbsOwnTheStripBetweenPavementAndShoulderAlongSectionsAndBends()
        {
            var graph = Road(PublicRoadStyle.Avenue, RoadStructure.Ground, new Vector3(-100,10,0),
                new Vector3(0,10,0), new Vector3(70,10,70));
            var geometry = Geometry(graph, _ => 10f);
            var kerb = geometry.kerbMesh;
            var vertices = kerb.vertices;
            var indices = kerb.triangles;
            int checkedFaces = 0;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = vertices[indices[i]];
                var b = vertices[indices[i + 1]];
                var c = vertices[indices[i + 2]];
                if (Vector3.Cross(b - a, c - a).y <= .0001f) continue;
                var probe = Alignment3D.XZ((a + b + c) / 3f);
                Assert.That(geometry.groundTriangles.Any(t => t.DistanceSquared(probe) < .000001f),
                    Is.True, "Unclaimed kerb surface at " + probe);
                checkedFaces++;
            }
            Assert.That(checkedFaces, Is.GreaterThan(40));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AngledPortalJoinSharesThePavementAndExcavationCrossSection(bool reverse)
        {
            var graph = new PublicRoadNetwork();
            var join = new Vector3(615.6f, 106.8f, 1025.12f);
            int a = graph.AddNode(join + new Vector3(-56f, -3f, 0f));
            int b = graph.AddNode(join);
            int c = graph.AddNode(join + Quaternion.Euler(0f,.45f,0f) * new Vector3(38f,4f,0f));
            var before = graph.AddSegment(a, b, PublicRoadStyle.Avenue, RoadStructure.Ground);
            var after = graph.AddSegment(reverse ? c : b, reverse ? b : c, PublicRoadStyle.Avenue, RoadStructure.Tunnel);
            using var chunks = new PublicRoadChunks(null, null);
            chunks.Update(graph, _ => join.y, null);
            var approach = chunks.Parts.Single(p => !p.junction && p.id == before.id).geometry;
            var tunnel = chunks.Parts.Single(p => !p.junction && p.id == after.id).geometry;
            var edge = approach.pavement.vertices.Where(p => Mathf.Abs(p.x - join.x) < .1f).Distinct().ToArray();
            Assert.That(edge.Length, Is.EqualTo(3));
            foreach (var point in edge)
                Assert.That(tunnel.pavement.vertices.Min(p => Vector3.Distance(point, p)), Is.LessThan(.0005f));
            var left = edge.OrderBy(p => p.z).First();
            var right = edge.OrderBy(p => p.z).Last();
            for (int i = 0; i <= 20; i++)
            {
                var probe = Alignment3D.XZ(Vector3.Lerp(left, right, i / 20f));
                Assert.That(approach.groundTriangles.Any(t => t.DistanceSquared(probe) < .000001f));
                Assert.That(tunnel.groundTriangles.Any(t => t.DistanceSquared(probe) < .000001f &&
                    t.excavationDepth == RoadStructureBuilder.TunnelFloorThickness));
            }
            var vertices = tunnel.structures.vertices;
            var normals = tunnel.structures.normals;
            Assert.That(Enumerable.Range(0, vertices.Length).Any(i => normals[i].y < -.95f &&
                Mathf.Abs(vertices[i].x - join.x) < .1f &&
                Mathf.Abs(vertices[i].y - (left.y - RoadStructureBuilder.TunnelFloorThickness)) < .001f));
        }

        [Test]
        public void CutFaceFollowsABankThatRisesBetweenShoulderEndpoints()
        {
            var mesh = new LandscapeMesh();
            RoadStructureBuilder.SealShoulder(mesh, new Vector3(0,10,2), new Vector3(5,10,2), Vector3.forward,
                p => 10f + Mathf.Max(0f, 2f - Mathf.Abs(p.x - 2.5f)));
            var closure = mesh.Create("Cut beside shoulder");
            owned.Add(closure);
            Assert.That(closure.vertices.Any(p => Mathf.Abs(p.x - 2.5f) < .001f && Mathf.Abs(p.z - 2f) < .001f && p.y >= 12f));
        }

        [Test]
        public void PortalShoulderClosureExtendsFromThePavementEdgeToTheRock()
        {
            var mesh = new LandscapeMesh();
            var a = new Vector3(0f, 10f, 2f);
            var b = new Vector3(5f, 10f, 2f);
            RoadStructureBuilder.SealShoulder(mesh, a, b, Vector3.forward, p => 10f - p.x);
            var closure = mesh.Create("Portal shoulder closing face");
            owned.Add(closure);
            Assert.That(closure.bounds.max.y, Is.EqualTo(9.56f).Within(.01f));
            Assert.That(closure.bounds.min.y, Is.LessThan(5f));
            Assert.That(closure.bounds.size.z, Is.Zero.Within(.001f));
        }

        static float[] Along(IEnumerable<Vector3> piers)=>piers.Select(p=>p.x).OrderBy(x=>x).ToArray();

        [Test]
        public void PiersAreEvenlySpacedHoweverTheBridgeWasDrawn()
        {
            var one=Geometry(Road(PublicRoadStyle.Urban,RoadStructure.Elevated,new Vector3(-150,12,0),new Vector3(150,12,0)));
            var many=Geometry(Road(PublicRoadStyle.Urban,RoadStructure.Elevated,new Vector3(-150,12,0),new Vector3(-138,12,0),new Vector3(-95,12,0),
                new Vector3(-80,12,0),new Vector3(-12,12,0),new Vector3(40,12,0),new Vector3(55,12,0),new Vector3(150,12,0)));
            var a=Along(one.piers);var b=Along(many.piers);
            // 300 m: nine piers 30 m apart, and one just inside each end standing in the air
            Assert.AreEqual(11,a.Length);Assert.AreEqual(a.Length,b.Length);
            for(int i=0;i<a.Length;i++)Assert.AreEqual(a[i],b[i],.02f,"Pier "+i);
            for(int i=1;i<a.Length;i++)Assert.LessOrEqual(a[i]-a[i-1],RoadStructureBuilder.MaxPierSpan+.01f);
            Assert.AreEqual(-150+RoadStructureBuilder.PierEndInset,a[0],.02f);Assert.AreEqual(-120f,a[1],.02f);Assert.AreEqual(0f,a[5],.02f);
        }

        [Test]
        public void PiersCarryOnRoundABendAndMatchBetweenChunkedAndWholeBuilds()
        {
            var graph=Road(PublicRoadStyle.Highway,RoadStructure.Elevated,new Vector3(-300,14,0),new Vector3(0,14,0),new Vector3(260,14,260));
            var whole=Geometry(graph);
            using var chunks=new PublicRoadChunks(null,null);chunks.Update(graph,_=>0f,null);
            var a=whole.piers.OrderBy(p=>p.x).ThenBy(p=>p.z).ToArray();var b=chunks.Geometry.piers.OrderBy(p=>p.x).ThenBy(p=>p.z).ToArray();
            Assert.AreEqual(a.Length,b.Length);for(int i=0;i<a.Length;i++)Assert.Less(Vector3.Distance(a[i],b[i]),.02f);
            // a highway deck is carried by rows of three columns; rows follow each other at no more than the longest span
            Assert.AreEqual(3,RoadStructureBuilder.PierColumns(RoadTypes.Of(PublicRoadStyle.Highway).FullWidth));Assert.AreEqual(0,a.Length%3);
            var rows=new List<Vector3>();
            foreach(var p in whole.piers){int at=rows.FindIndex(r=>Vector3.Distance(r,p)<13f);if(at<0)rows.Add(p);}
            Assert.AreEqual(a.Length/3,rows.Count);
            foreach(var row in rows)
            {
                float nearest=rows.Where(r=>r!=row).Min(r=>Vector3.Distance(r,row));
                Assert.LessOrEqual(nearest,RoadStructureBuilder.MaxPierSpan+1f,"A row stands alone at "+row);
            }
            // the bend itself (about 100 m of arc) is carried too
            Assert.IsTrue(rows.Any(r=>r.x>20&&r.x<120&&r.z>2&&r.z<90),"No pier on the bend.");

            // lengthening the bridge re-spaces every pier; the chunks that did not change themselves follow
            int end=graph.nodes[graph.nodes.Count-1].id;int further=graph.AddNode(new Vector3(260+70,14,260+70));
            graph.AddSegment(end,further,PublicRoadStyle.Highway,RoadStructure.Elevated);
            chunks.Update(graph,_=>0f,null);var again=Geometry(graph);
            a=again.piers.OrderBy(p=>p.x).ThenBy(p=>p.z).ToArray();b=chunks.Geometry.piers.OrderBy(p=>p.x).ThenBy(p=>p.z).ToArray();
            Assert.AreEqual(a.Length,b.Length);for(int i=0;i<a.Length;i++)Assert.Less(Vector3.Distance(a[i],b[i]),.02f,"Stale pier after lengthening the bridge.");
        }

        [Test]
        public void APierRefusedByTheRoadBelowMovesAsideInsteadOfLeavingALongSpan()
        {
            var graph=Road(PublicRoadStyle.Urban,RoadStructure.Elevated,new Vector3(-120,12,0),new Vector3(120,12,0));
            int south=graph.AddNode(new Vector3(0,0,-80)),north=graph.AddNode(new Vector3(0,0,80));
            graph.AddSegment(south,north,PublicRoadStyle.Urban,RoadStructure.Ground);
            var g=Geometry(graph);var xs=Along(g.piers);var below=RoadTypes.Of(PublicRoadStyle.Urban);
            Assert.IsFalse(xs.Any(x=>Mathf.Abs(x)<below.FullWidth*.5f+.5f),"A column stands on the road below.");
            Assert.IsTrue(xs.Any(x=>Mathf.Abs(x)>below.FullWidth*.5f&&Mathf.Abs(x)<RoadStructureBuilder.PierShift),"The refused pier was dropped.");
            for(int i=1;i<xs.Length;i++)Assert.LessOrEqual(xs[i]-xs[i-1],RoadStructureBuilder.MaxPierSpan+RoadStructureBuilder.PierShift);
        }

        [Test]
        public void ColumnsStandInSlopingGroundAndCountFollowsTheDeckWidth()
        {
            Assert.AreEqual(1,RoadStructureBuilder.PierColumns(RoadTypes.Of(PublicRoadStyle.Urban).FullWidth));
            Assert.AreEqual(2,RoadStructureBuilder.PierColumns(RoadTypes.Of(PublicRoadStyle.Avenue).FullWidth));
            Assert.AreEqual(3,RoadStructureBuilder.PierColumns(RoadTypes.Of(PublicRoadStyle.Boulevard).FullWidth));
            float Ground(Vector2 p)=>p.y*.25f-.05f*p.x;   // falls across and along the road
            var g=Geometry(Road(PublicRoadStyle.Avenue,RoadStructure.Elevated,new Vector3(-90,16,0),new Vector3(90,16,0)),Ground);
            Assert.Greater(g.piers.Count,3);Assert.AreEqual(0,g.piers.Count%2);
            var mesh=g.structures.vertices;
            foreach(var top in g.piers)
            {
                var at=new Vector2(top.x,top.z);
                var column=mesh.Where(p=>Vector2.Distance(new Vector2(p.x,p.z),at)<1.2f&&p.y<top.y-.01f).ToArray();
                Assert.IsNotEmpty(column);float foot=column.Min(p=>p.y);
                foreach(var p in column.Where(p=>p.y<foot+.01f))Assert.Less(p.y,Ground(new Vector2(p.x,p.z))-.05f,"A column's foot hangs in the air at "+p);
                Assert.Less(top.y,16-RoadStructureBuilder.DeckDepth+.2f);
            }
        }

        [Test]
        public void ADeckLyingOnItsEmbankmentGetsNoPiers()
        {
            var g=Geometry(Road(PublicRoadStyle.Urban,RoadStructure.Elevated,new Vector3(-90,2,0),new Vector3(90,2,0)));
            Assert.IsEmpty(g.piers);Assert.Greater(g.structures.vertexCount,100);
        }

        [Test]
        public void ShortBridgeColumnsAndTheirThicknessIgnoreDepthBelowTheWaterline()
        {
            var graph=Road(PublicRoadStyle.Avenue,RoadStructure.Elevated,new Vector3(-67,5,0),new Vector3(67,5,0));
            var deep=Geometry(graph,_=>-100f,_=>0f);
            var shallow=Geometry(graph,_=>-12f,_=>0f);
            Assert.IsNotEmpty(deep.piers);
            Assert.AreEqual(-RoadStructureBuilder.SubmergedFootDepth,deep.structures.bounds.min.y,.01f);
            CollectionAssert.AreEqual(deep.structures.vertices,shallow.structures.vertices);
        }

        [Test]
        public void LegacyRiverSupportsUseTheRenderedBankWidth()
        {
            var world=new ValleyWorld {hasRiver=true,river=new RiverCourse{halfWidth=40f},
                heights=Enumerable.Repeat(10f,ValleyWorld.Resolution*ValleyWorld.Resolution).ToArray()};
            Assert.That(world.IsWater(new Vector2(51f,0f)),Is.False);
            Assert.That(world.BridgeWaterAt(new Vector2(51f,0f)),Is.EqualTo(world.WaterHeight(0f)));
            Assert.That(world.BridgeWaterAt(new Vector2(70f,0f)),Is.Null);
        }

        [TestCase(false,65f,44f,true)]
        [TestCase(false,25f,44f,false)]
        [TestCase(false,65f,7f,false)]
        [TestCase(true,65f,44f,true)]
        [TestCase(true,6f,44f,false)]
        public void ShortBridgeChoosesSuspensionFromTheSustainedVisibleDrop(bool wet,float clearance,float halfGap,bool suspension)
        {
            const float deck=100f;
            var graph=Road(PublicRoadStyle.Avenue,RoadStructure.Elevated,new Vector3(-65,deck,0),
                new Vector3(0,deck,0),new Vector3(65,deck,0));
            bool InGap(Vector2 p)=>Mathf.Abs(p.x)<halfGap;
            float Ground(Vector2 p)=>InGap(p)?(wet?-120f:deck-clearance):deck-.2f;
            float? Water(Vector2 p)=>wet&&InGap(p)?deck-clearance:(float?)null;
            var geometry=Geometry(graph,Ground,Water);
            Assert.That(geometry.SuspensionFor(graph.segments[0])!=null,Is.EqualTo(suspension));
            if(suspension)
            {
                Assert.That(geometry.piers,Is.Empty);
                Assert.That(geometry.structures.bounds.max.y,Is.GreaterThan(deck+10f));
            }
            else
            {
                Assert.That(geometry.structures.bounds.max.y,Is.LessThan(deck+3f));
            }
        }

        [Test]
        public void ArchDesignFollowsASplitAndBuildsTheSameMeshInChunks()
        {
            var graph=Road(PublicRoadStyle.Highway,RoadStructure.Elevated,
                new Vector3(-100,14,0),new Vector3(100,14,0));
            var first=graph.segments[0];
            first.bridgeDesign=RoadBridgeDesign.Arch;
            graph.Split(first,new Vector3(21,14,0));
            Assert.That(graph.segments.Select(e=>e.bridgeDesign),Is.All.EqualTo(RoadBridgeDesign.Arch));
            float Ground(Vector2 point)=>Mathf.Abs(point.x)>89f?14f:0f;
            var geometry=Geometry(graph,Ground);
            using var chunks=new PublicRoadChunks(null,null);
            chunks.Update(graph,Ground,null);
            Assert.That(geometry.BridgeFor(first).Design,Is.EqualTo(RoadBridgeDesign.Arch));
            Assert.That(chunks.Geometry.BridgeFor(first).Design,Is.EqualTo(RoadBridgeDesign.Arch));
            Assert.That(geometry.piers,Is.Empty);
            Assert.That(geometry.structures.bounds.max.y,Is.GreaterThan(49f));
            CollectionAssert.AreEquivalent(geometry.structures.vertices,chunks.Geometry.structures.vertices);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LongCrossingsUseTwoTowersAndStaySuspendedAcrossChunks(bool water)
        {
            float deck=water?10f:94f;
            var graph=Road(PublicRoadStyle.Highway,RoadStructure.Elevated,new Vector3(-285,deck,0),new Vector3(-176.7f,deck,0),
                new Vector3(-44,deck,0),new Vector3(86,deck,0),new Vector3(285,deck,0));
            float Ground(Vector2 point)=>Mathf.Abs(point.x)>266f?deck-.2f:(water?-180f:14f);
            float? Water(Vector2 point)=>water&&Mathf.Abs(point.x)<=266f?0f:(float?)null;
            var whole=Geometry(graph,Ground,Water);
            Assert.IsNotNull(whole.SuspensionFor(graph.segments[0]));
            using var chunks=new PublicRoadChunks(null,null);
            chunks.Update(graph,Ground,null,waterHeight:Water);
            Assert.IsEmpty(whole.piers);
            Assert.IsEmpty(chunks.Geometry.piers);
            Assert.That(whole.structures.bounds.max.y,Is.GreaterThan(deck+25f));
            Assert.That(whole.structures.bounds.min.y,Is.EqualTo(water?-RoadStructureBuilder.SubmergedFootDepth:13.4f).Within(.02f));
            CollectionAssert.AreEquivalent(whole.structures.vertices,chunks.Geometry.structures.vertices);
            var tallest=whole.structures.vertices.Where(p=>p.y>whole.structures.bounds.max.y-.1f).Select(p=>p.x).ToArray();
            Assert.That(tallest.Any(x=>Mathf.Abs(x+176.7f)<6f)&&tallest.Any(x=>Mathf.Abs(x-176.7f)<6f),Is.True);
            Assert.That(tallest.All(x=>Mathf.Abs(x)>165f),Is.True);

            var picked=graph.segments[1].id;
            Assert.That(PublicRoadGeometry.Layout(graph).SetBridgeDesign(new[]{picked},RoadBridgeDesign.Piers),Is.True);
            chunks.Update(graph,Ground,null,waterHeight:Water);
            Assert.That(chunks.Geometry.BridgeFor(graph.segments[0]).Design,Is.EqualTo(RoadBridgeDesign.Piers));
            Assert.That(chunks.Geometry.piers,Is.Not.Empty);
            PublicRoadGeometry.Layout(graph).SetBridgeDesign(new[]{picked},RoadBridgeDesign.Automatic);
            chunks.Update(graph,Ground,null,waterHeight:Water);
            Assert.That(chunks.Geometry.piers,Is.Empty);

            // A lower left bank turns this into an unfinished crossing. Every chunk switches back to piers.
            float ChangedGround(Vector2 point)=>point.x<-270f?deck-13f:Ground(point);
            chunks.Update(graph,ChangedGround,null,waterHeight:Water);
            Assert.IsNull(chunks.Geometry.SuspensionFor(graph.segments[0]));
            Assert.IsNotEmpty(chunks.Geometry.piers);
            Assert.That(chunks.Geometry.structures.bounds.max.y,Is.LessThan(deck+3f));
        }

        [Test]
        public void DeckSidesCarryPanelDistancesAndARailOnEvenlySpacedPostsRoundABend()
        {
            var graph=Road(PublicRoadStyle.Urban,RoadStructure.Elevated,new Vector3(-150,12,0),new Vector3(0,12,0),new Vector3(100,12,120));
            var g=Geometry(graph);var mesh=g.structures;var vertices=mesh.vertices;var colours=mesh.colors32;var uvs=mesh.uv;
            Assert.AreEqual(vertices.Length,colours.Length);Assert.AreEqual(vertices.Length,uvs.Length);
            // the sides' distance along the bridge runs on unbroken from section to bend to section
            var along=Enumerable.Range(0,vertices.Length).Where(i=>colours[i].a>60&&colours[i].a<190).Select(i=>uvs[i].x).Distinct().OrderBy(x=>x).ToArray();
            // (and a few metres on round each rounded dead end)
            Assert.Greater(along.Length,40);Assert.LessOrEqual(along[0],0f);Assert.Greater(along[0],-10f);
            for(int i=1;i<along.Length;i++)Assert.LessOrEqual(along[i]-along[i-1],5.01f,"A gap in the sides' distance at "+along[i]);
            float total=along[along.Length-1]-along[0];Assert.Greater(total,290f);
            // steel: a rail at the top, on posts every PostSpacing along each side
            float railTop=12+.18f+.15f+RoadStructureBuilder.ParapetHeight,postTop=railTop-RoadStructureBuilder.RailDepth;
            var steel=Enumerable.Range(0,vertices.Length).Where(i=>colours[i].a<60).Select(i=>vertices[i]).ToArray();
            Assert.IsTrue(steel.All(p=>p.y>12+.18f+.15f+RoadStructureBuilder.BarrierHeight-.01f&&p.y<railTop+.01f));
            // a post's box has 36 vertices, 18 of them on its base (six for the base, three for each side)
            int posts=steel.Count(p=>Mathf.Abs(p.y-(railTop-RoadStructureBuilder.ParapetHeight+RoadStructureBuilder.BarrierHeight))<.003f)/18;
            Assert.AreEqual(2*Mathf.RoundToInt(total/RoadStructureBuilder.PostSpacing),posts,2);
            Assert.IsTrue(steel.Any(p=>Mathf.Abs(p.y-postTop)<.003f));
            // columns, crossheads and the soffit stay concrete
            Assert.IsNotEmpty(g.piers);
            for(int i=0;i<vertices.Length;i++)if(vertices[i].y<12-RoadStructureBuilder.DeckDepth)Assert.AreEqual(255,colours[i].a,"Not plain concrete at "+vertices[i]);
        }

        GameObject root;
        [TearDown]public void CleanupAirport(){if(root!=null)Object.DestroyImmediate(root);root=null;}
        [Test]
        public void ServiceRoadBridgeIsUnbrokenRoundItsBendWithPiersAndAHardStrip()
        {
            root=new GameObject("Service bridge fixture");root.SetActive(false);
            var airport=root.AddComponent<Airport>();airport.groundSize=0;airport.TerrainHeight=_=>0;root.SetActive(true);
            var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-150,12,0)),b=net.AddNode(new Vector3(-60,12,0)),c=net.AddNode(new Vector3(0,12,0)),d=net.AddNode(new Vector3(90,12,90));
            foreach(var (from,to) in new[]{(a,b),(b,c),(c,d)})net.Connect(from,to,7,PavementSurface.Asphalt,false,RoadClass.Service,false,RoadStructure.Elevated);
            net.Rebuild();
            var builder=new TaxiwayBuilder(net,airport.Runways);var piers=new List<Vector3>();
            var mesh=AirfieldRoadGeometry.Structures(airport,net,builder,new List<BuiltSurfaceMap.Triangle>(),piers).Create("Service bridge");owned.Add(mesh);
            float top=12+AirfieldHeights.Road+RoadStructureBuilder.ParapetHeight;
            Assert.AreEqual(1,StripsAt(mesh,top),"One unbroken rail, along both sides and round both dead ends.");
            // barrier and rail stand outside a hard strip, not on the carriageway's edge
            var onStraight=mesh.vertices.Where(p=>p.x>-140&&p.x<-70&&p.y>12+AirfieldHeights.Road+.05f).ToArray();
            Assert.IsNotEmpty(onStraight);Assert.IsTrue(onStraight.Any(p=>Mathf.Abs(p.y-top)<.003f));
            Assert.GreaterOrEqual(onStraight.Min(p=>Mathf.Abs(p.z)),3.5f+TaxiwayBuilder.StructureVerge-RoadStructureBuilder.ParapetWidth-.01f);
            Assert.LessOrEqual(onStraight.Max(p=>Mathf.Abs(p.z)),3.5f+TaxiwayBuilder.StructureVerge+.01f);
            // about 280 m of bridge: piers along all of it, including one near each free end
            Assert.GreaterOrEqual(piers.Count,9);
            foreach(var p in piers)Assert.LessOrEqual(piers.Where(q=>q!=p).Min(q=>Vector3.Distance(p,q)),RoadStructureBuilder.MaxPierSpan+1f);
            Assert.IsTrue(piers.Any(p=>p.x<-145)&&piers.Any(p=>p.x>80&&p.z>80));
        }

        [TestCase(190f,8f)]
        [TestCase(65f,65f)]
        public void ServiceRoadCrossingUsesTheSameSuspensionAndWaterline(float halfLength,float deck)
        {
            root=new GameObject("Service suspension fixture");root.SetActive(false);
            var airport=root.AddComponent<Airport>();airport.groundSize=0;
            float bank=halfLength*.81f;
            airport.TerrainHeight=p=>Mathf.Abs(p.x)>bank?deck:-100f;
            airport.BridgeWaterHeight=p=>Mathf.Abs(p.x)<=bank?0f:(float?)null;
            root.SetActive(true);
            var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-halfLength,deck,0)),b=net.AddNode(new Vector3(0,deck,0)),
                c=net.AddNode(new Vector3(halfLength,deck,0));
            net.Connect(a,b,7,PavementSurface.Asphalt,false,RoadClass.Service,false,RoadStructure.Elevated);
            net.Connect(c,b,7,PavementSurface.Asphalt,false,RoadClass.Service,false,RoadStructure.Elevated);
            var piers=new List<Vector3>();
            var mesh=AirfieldRoadGeometry.Structures(airport,net,new TaxiwayBuilder(net,airport.Runways),
                new List<BuiltSurfaceMap.Triangle>(),piers).Create("Service suspension bridge");
            owned.Add(mesh);
            Assert.IsEmpty(piers);
            Assert.That(mesh.bounds.max.y,Is.GreaterThan(deck+10f));
            Assert.That(mesh.bounds.min.y,Is.EqualTo(-RoadStructureBuilder.SubmergedFootDepth).Within(.02f));
        }
    }
}
