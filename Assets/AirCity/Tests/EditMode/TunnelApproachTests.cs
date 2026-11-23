using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    /// <summary>Tunnel approaches (retaining walls that follow the ground, the portal, what terrain a tunnel
    /// opens) and dead ends on bridges and in tunnels.</summary>
    public class TunnelApproachTests
    {
        readonly List<Object> owned=new List<Object>();
        GameObject root;
        [TearDown]public void Cleanup()
        {
            foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();
            if(root!=null)Object.DestroyImmediate(root);root=null;
        }
        PublicRoadGeometry Geometry(PublicRoadNetwork graph,System.Func<Vector2,float> ground)
        {
            var g=new PublicRoadGeometry(graph,null,null,ground);
            owned.AddRange(new Object[]{g.pavement,g.shoulders,g.markings,g.kerbMesh,g.medianGrass,g.structures});return g;
        }
        static PublicRoadNetwork Road(PublicRoadStyle style,params (Vector3 p,RoadStructure structure)[] points)
        {
            var graph=new PublicRoadNetwork();int previous=graph.AddNode(points[0].p);
            for(int i=1;i<points.Length;i++){int next=graph.AddNode(points[i].p);graph.AddSegment(previous,next,style,points[i].structure);previous=next;}
            return graph;
        }
        static float Verge(PublicRoadStyle style)=>Mathf.Max(RoadTypes.Of(style).verge,RoadStructureBuilder.ParapetWidth);
        static float Reach(PublicRoadStyle style)=>RoadTypes.Of(style).Width*.5f+Verge(style);
        static BuiltSurfaceMap Opened(PublicRoadGeometry g)
        {var mask=new BuiltSurfaceMap();foreach(var triangle in g.groundTriangles)mask.AddTriangle(triangle);return mask;}

        [TestCase(PublicRoadStyle.Urban)] [TestCase(PublicRoadStyle.Rural)] [TestCase(PublicRoadStyle.Avenue)]
        public void OpenApproachWallsFollowTheGroundAndGrowOutOfIt(PublicRoadStyle style)
        {
            // a ramp down into a cut across a side slope, over rolling ground; never deep enough to be covered
            float Ground(Vector2 p)=>.04f*p.y+.5f*Mathf.Sin(p.x/7f);
            var graph=Road(style,(new Vector3(-200,0,0),RoadStructure.Ground),(new Vector3(-100,0,0),RoadStructure.Ground),
                (new Vector3(0,-5,0),RoadStructure.Tunnel),(new Vector3(150,-5,0),RoadStructure.Tunnel));
            var g=Geometry(graph,Ground);float reach=Reach(style),thick=RoadStructureBuilder.WallThickness;
            var walls=g.structures.vertices;var road=g.pavement.vertices;
            Assert.Greater(walls.Length,500);
            // the ground as drawn: the ground road's roadbed is graded into it near the top of the ramp
            float Drawn(Vector2 p)=>g.DrawnGround(p);
            float Pavement(Vector3 p)
            {
                float best=float.MaxValue,height=0;
                foreach(var q in road){float d=(q.x-p.x)*(q.x-p.x)+(q.z-p.z)*(q.z-p.z);if(d<best){best=d;height=q.y;}}
                return height;
            }
            // nothing stands higher than a coping above the ground beside it (or the sidewalk's level, where the ground is lower)
            foreach(var p in walls)
            {
                if(p.x<-99||p.x>149)continue;
                float highest=float.MinValue;
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)highest=Mathf.Max(highest,Drawn(new Vector2(p.x+dx*.7f,p.z+dz*.5f)));
                Assert.LessOrEqual(p.y,Mathf.Max(highest+RoadStructureBuilder.Coping+.03f,Pavement(p)+.36f),"A wall stands clear of the ground at "+p);
            }
            // where the cut is deep the wall's top is above the ground all along both sides: no terrain comes over it
            int held=0;
            foreach(float side in new[]{-1f,1f})
                for(float x=-20;x<=140;x+=.5f)
                {
                    var at=new Vector2(x,side*(reach+thick));float ground=Drawn(at),top=float.MinValue;
                    foreach(var p in walls)if(Mathf.Abs(p.x-x)<1.3f&&Mathf.Abs(p.z-at.y)<.05f)top=Mathf.Max(top,p.y);
                    Assert.Greater(top,ground+.1f,"Ground comes over the wall at "+at);held++;
                }
            Assert.Greater(held,600);
            // at the top of the ramp the walls have not started: nothing above the sidewalk
            foreach(var p in walls)if(p.x>-99.9f&&p.x<-96f)Assert.Less(p.y,Pavement(p)+.5f,"A wall stands at the top of the ramp at "+p);
            // the terrain is opened from wall to wall (under the walls too), and nowhere outside them
            using var mask=Opened(g);
            foreach(float x in new[]{-60f,0f,90f})
            {
                Assert.IsTrue(mask.Contains(new Vector2(x,0)));Assert.IsTrue(mask.Contains(new Vector2(x,reach+thick-.05f)));Assert.IsTrue(mask.Contains(new Vector2(x,-reach-thick+.05f)));
                Assert.IsFalse(mask.Contains(new Vector2(x,reach+thick+.1f)));Assert.IsFalse(mask.Contains(new Vector2(x,-reach-thick-.1f)));
            }
        }

        [Test]
        public void WallsMeetTheGroundAsGradedBesideAGroundRoadNotTheSavedTerrain()
        {
            // a ground road in a 6 m cutting (the valley grades the drawn terrain down to its roadbed) carries on as a tunnel
            var style=PublicRoadStyle.Urban;float reach=Reach(style),thick=RoadStructureBuilder.WallThickness;
            var graph=Road(style,(new Vector3(-300,0,0),RoadStructure.Ground),(new Vector3(-100,0,0),RoadStructure.Ground),
                (new Vector3(0,-5,0),RoadStructure.Tunnel),(new Vector3(300,-5,0),RoadStructure.Tunnel));
            var g=Geometry(graph,_=>6f);var walls=g.structures.vertices;
            Assert.Less(g.DrawnGround(new Vector2(-95,reach+thick)),5f,"The approach cuts into the saved 6 m terrain.");
            Assert.AreEqual(6f,g.DrawnGround(new Vector2(250,reach+thick)),.01f);
            float Top(float x){float top=float.MinValue;foreach(var p in walls)if(Mathf.Abs(p.x-x)<1.3f&&Mathf.Abs(Mathf.Abs(p.z)-reach-thick)<.05f)top=Mathf.Max(top,p.y);return top;}
            using var mask = Opened(g);
            Assert.IsTrue(mask.Contains(new Vector2(-96, 0)));
            Assert.IsFalse(mask.Contains(new Vector2(-60, 0)), "The preserved surface is above the tunnel roof.");
            for (float x = -95; x <= -85; x += 2.5f)
                Assert.LessOrEqual(Top(x), -5f * Mathf.InverseLerp(-100, 0, x) + .18f + RoadStructureBuilder.TunnelExcavationHeight + .8f);
            Assert.Less(Top(-95),6f,"The wall follows the cut at this station, below the saved terrain.");
        }

        [TestCase(PublicRoadStyle.Rural)]
        [TestCase(PublicRoadStyle.Avenue)]
        public void OpenTunnelWallClosesBelowTheGroundShoulder(PublicRoadStyle style)
        {
            var graph = Road(style, (new Vector3(-40f, 0f, 0f), RoadStructure.Ground),
                (Vector3.zero, RoadStructure.Ground), (new Vector3(40f, 0f, 0f), RoadStructure.Tunnel),
                (new Vector3(80f, 0f, 0f), RoadStructure.Ground));
            var geometry = Geometry(graph, p => .05f * p.y);
            const float pavement = .18f;
            float reach = Reach(style);
            float wallMiddle = reach + RoadStructureBuilder.WallThickness * .5f;
            root = new GameObject("Open tunnel wall joins");
            var mesh = root.AddComponent<MeshCollider>();
            mesh.sharedMesh = geometry.structures;
            Physics.SyncTransforms();

            foreach (float side in new[] { -1f, 1f })
            foreach (float end in new[] { 0f, 40f })
            {
                var direction = end == 0f ? Vector3.right : Vector3.left;
                var origin = new Vector3(end, pavement - .2f, side * wallMiddle) - direction;
                Assert.That(mesh.Raycast(new Ray(origin, direction), out var hit, 1.01f), Is.True);
                Assert.That(hit.point.x, Is.EqualTo(end).Within(.3f));
            }

            Assert.That(geometry.structures.vertices.Any(p => p.x < -.3f && p.y < pavement && Mathf.Abs(p.z) > reach - .1f));
            Assert.That(geometry.structures.vertices.Any(p => p.x > 40.3f && p.y < pavement && Mathf.Abs(p.z) > reach - .1f));
        }

        [Test]
        public void GravelShoulderClosesTheStepAtTheAsphaltEdge()
        {
            var graph = Road(PublicRoadStyle.Rural, (new Vector3(-40f, 0f, 0f), RoadStructure.Ground),
                (new Vector3(40f, 0f, 0f), RoadStructure.Ground));
            var geometry = Geometry(graph, _ => 0f);
            root = new GameObject("Asphalt and gravel joint");
            var mesh = root.AddComponent<MeshCollider>();
            mesh.sharedMesh = geometry.shoulders;
            Physics.SyncTransforms();

            float halfWidth = RoadTypes.Of(PublicRoadStyle.Rural).Width * .5f;
            foreach (float side in new[] { -1f, 1f })
            {
                var origin = new Vector3(3f, .16f, side * (halfWidth + 2f));
                Assert.That(mesh.Raycast(new Ray(origin, -side * Vector3.forward), out var hit, 2.01f), Is.True);
                Assert.That(hit.point.z, Is.EqualTo(side * halfWidth).Within(.01f));
            }
        }

        [Test]
        public void PortalProjectsFromTheRoofAndPreservesTheCoveredHill()
        {
            float Ground(Vector2 p) => Mathf.Clamp(p.x * .4f, 0f, 40f);
            var style = PublicRoadStyle.Urban;
            float verge = Verge(style), thick = RoadStructureBuilder.WallThickness;
            var graph = Road(style, (new Vector3(-100, -2, 0), RoadStructure.Tunnel), (new Vector3(200, -2, 0), RoadStructure.Tunnel));
            var g = Geometry(graph, Ground);
            var v = g.structures.vertices;
            var colours = g.structures.colors32;
            float roof = -2f + .18f + RoadStructureBuilder.TunnelClearance;
            using var mask = Opened(g);
            Assert.IsTrue(mask.Contains(new Vector2(0, 0)));
            Assert.IsFalse(mask.Contains(new Vector2(17, 0)));
            Assert.IsFalse(mask.Contains(new Vector2(100, 0)));

            float front = v.Where(p => Mathf.Abs(p.y - (roof + RoadStructureBuilder.LintelHeight)) < .001f).Min(p => p.x);
            Assert.That(front, Is.InRange(8f, 11f));
            Assert.That(v.Max(p => p.y), Is.EqualTo(roof + RoadStructureBuilder.LintelHeight).Within(.01f));
            Assert.That(v.Any(p => p.x > front + 1f && Mathf.Abs(p.y - (roof + RoadStructureBuilder.TunnelRoofThickness)) < .01f));
            var lamps = Enumerable.Range(0, v.Length).Where(i => colours[i].a > 200 && colours[i].a < 245).Select(i => v[i]).ToArray();
            Assert.That(lamps.Length, Is.GreaterThanOrEqualTo(36 * 2 * 10));
            Assert.That(lamps.All(p => p.x > front && p.y < roof && p.y > roof - .2f));
            // both dead ends are closed round the pavement's rounded end: a wall in the open cut, the lining under ground
            Assert.LessOrEqual(g.pavement.bounds.max.x,200.001f);Assert.GreaterOrEqual(g.pavement.bounds.min.x,-100.001f);
            Assert.AreEqual(-100-verge-thick,v.Min(p=>p.x),.02f);Assert.AreEqual(200+verge+thick,v.Max(p=>p.x),.02f);
            Assert.IsTrue(mask.Contains(new Vector2(-100-verge-thick+.1f,0)));Assert.IsFalse(mask.Contains(new Vector2(-100-verge-thick-.2f,0)));
            var endWall=v.Where(p=>p.x<-100-verge+.01f).ToArray();
            Assert.AreEqual(RoadStructureBuilder.Coping,endWall.Max(p=>p.y),.02f,"The end wall's coping stands just above the ground.");
        }

        [Test]
        public void ArchedPortalChangesTheFrameWithoutMovingTheOpening()
        {
            float Ground(Vector2 p) => Mathf.Clamp(p.x * .4f, 0f, 40f);
            var style = PublicRoadStyle.Urban;
            var road = Road(style, (new Vector3(-100, -2, 0), RoadStructure.Tunnel),
                (new Vector3(200, -2, 0), RoadStructure.Tunnel));
            var rectangle = Geometry(road, Ground);
            road.segments[0].tunnelPortalStyle = TunnelPortalStyle.Arched;
            var arch = Geometry(road, Ground);
            var opening = rectangle.groundTriangles.Where(t => t.excavationHeight > 0f).ToArray();
            CollectionAssert.AreEqual(opening, arch.groundTriangles.Where(t => t.excavationHeight > 0f));
            float frameTop = -2f + .18f + RoadStructureBuilder.TunnelClearance + RoadStructureBuilder.LintelHeight;
            float face = rectangle.structures.vertices.Where(p => Mathf.Abs(p.y - frameTop) < .001f).Min(p => p.x);
            float FrameTop(Mesh mesh)
            {
                var vertices = mesh.vertices;
                var colors = mesh.colors32;
                return vertices.Where((point, index) => colors[index].a == 255 && Mathf.Abs(point.x - face) < .01f)
                    .Max(point => point.y);
            }
            Assert.That(FrameTop(arch.structures) - FrameTop(rectangle.structures), Is.GreaterThan(1f));
        }

        [Test]
        public void ASteepDiagonalAvenueHasASolidRoofAndAnExcavationBoundedInHeight()
        {
            float Ground(Vector2 p) => Mathf.Clamp(2f * (p.x + 1.8f * p.y), 0f, 70f);
            var graph = Road(PublicRoadStyle.Avenue, (new Vector3(-65f, 0f, 0f), RoadStructure.Ground),
                (new Vector3(-25f, 0f, 0f), RoadStructure.Tunnel), (new Vector3(100f, 0f, 0f), RoadStructure.Tunnel));
            var geometry = Geometry(graph, Ground);
            const float floor = .18f;
            float top = floor + RoadStructureBuilder.TunnelClearance + RoadStructureBuilder.LintelHeight;
            Assert.That(geometry.structures.bounds.max.y, Is.EqualTo(top).Within(.02f));

            var highLane = new Vector2(1f, 5f);
            var lowLane = new Vector2(1f, -5f);
            Assert.That(Ground(highLane), Is.GreaterThan(15f));
            Assert.That(Ground(lowLane), Is.EqualTo(0f));

            var cuts = geometry.groundTriangles.Where(triangle => triangle.excavationHeight > 0f).ToArray();
            Assert.That(cuts.All(t => t.foundation == 0 && t.excavationHeight == RoadStructureBuilder.TunnelExcavationHeight));
            foreach (var point in new[] { highLane, lowLane, new Vector2(60f, 0f) })
            {
                var triangle = cuts.First(t => t.DistanceSquared(point) < .000001f);
                Assert.That(triangle.Height(point), Is.EqualTo(floor).Within(.015f));
                Assert.That(triangle.OwnsGround, Is.EqualTo(point.x < 60f));
                var moved = triangle.Transform(Matrix4x4.Translate(Vector3.up * 40f), false);
                Assert.That(moved.Height(point), Is.EqualTo(floor + 40f).Within(.015f));
                Assert.That(moved.excavationHeight, Is.EqualTo(triangle.excavationHeight));
                Assert.That(moved.OwnsGround, Is.EqualTo(triangle.OwnsGround));
            }

            var roof = geometry.structures.vertices.Where(p => Mathf.Abs(p.y - (floor + RoadStructureBuilder.TunnelExcavationHeight)) < .01f);
            Assert.That(roof.Any(p => p.x < 1f && p.z > 5f));
            Assert.That(roof.Any(p => p.x < 1f && p.z < -5f));
            using var mask = Opened(geometry);
            Assert.That(mask.Contains(new Vector2(60f, 0f)), Is.False);
        }

        [TestCase(PublicRoadStyle.Urban)] [TestCase(PublicRoadStyle.Rural)] [TestCase(PublicRoadStyle.Avenue)] [TestCase(PublicRoadStyle.Highway)]
        public void BridgeDeadEndCarriesItsBarrierAndRailRoundTheRoundedEnd(PublicRoadStyle style)
        {
            var graph=Road(style,(new Vector3(-300,12,0),RoadStructure.Ground),(new Vector3(-150,12,0),RoadStructure.Ground),(new Vector3(0,12,0),RoadStructure.Elevated));
            var g=Geometry(graph,_=>0f);var type=RoadTypes.Of(style);float reach=Reach(style),verge=Verge(style);
            float top=12+.18f+RoadStructureBuilder.ParapetHeight+(type.edge==RoadEdge.Kerb?.15f:0f);
            var v=g.structures.vertices;
            // one level rail: up one side, round the end and back down the other
            var rail=v.Where(p=>Mathf.Abs(p.y-top)<.003f).ToArray();
            var tip=rail.Where(p=>p.x>verge-RoadStructureBuilder.ParapetWidth-.01f).ToArray();
            Assert.IsTrue(tip.Any(p=>p.z>.5f)&&tip.Any(p=>p.z<-.5f),"The rail crosses the end of the deck.");
            Assert.AreEqual(top,v.Max(p=>p.y),.003f,"The rail is the top of the bridge, also round its end.");
            Assert.AreEqual(verge,v.Max(p=>p.x),.02f,"The deck ends the verge's width past the pavement.");
            Assert.LessOrEqual(g.pavement.bounds.max.x,.001f);
            // the end's corners are rounded: nothing in the square corner outside the arc
            float inner=Mathf.Min(3f,type.Width*.5f*.55f),radius=inner+verge;
            var corner=new Vector2(-inner,type.Width*.5f-inner);
            foreach(var p in v)
                if(p.x>corner.x&&Mathf.Abs(p.z)>corner.y)
                    Assert.LessOrEqual(Vector2.Distance(new Vector2(p.x,Mathf.Abs(p.z)),corner),radius+.02f,"Deck outside its rounded corner at "+p);
            // no soil verge or sidewalk laid on the deck's end, and a pier stands under it
            foreach(var p in g.shoulders.vertices)Assert.LessOrEqual(p.x,-149.9f);
            Assert.IsTrue(g.piers.Any(p=>p.x>-RoadStructureBuilder.PierEndInset-3f));
        }

        Airport Airfield()
        {
            root=new GameObject("Service road ends");root.SetActive(false);
            var airport=root.AddComponent<Airport>();airport.groundSize=0;airport.TerrainHeight=_=>0;root.SetActive(true);return airport;
        }

        [Test]
        public void ServiceRoadGroundApproachUsesTheTunnelLiningAcrossTheSlope()
        {
            var airport = Airfield();
            airport.TerrainHeight = p => Mathf.Clamp(2f * (p.x + 3f * p.y), 0f, 60f);
            var net = airport.Taxiways;
            int start = net.AddNode(new Vector3(-40f, 0f, 0f));
            int mouth = net.AddNode(Vector3.zero);
            int end = net.AddNode(new Vector3(80f, 0f, 0f));
            net.Connect(start, mouth, 7f, PavementSurface.Asphalt, false, RoadClass.Service, true);
            net.Connect(mouth, end, 7f, PavementSurface.Asphalt, false, RoadClass.Service, true, RoadStructure.Tunnel);
            var geometry = new TaxiwayDraft(airport, net);
            var cut = geometry.ownership.Where(t => t.DistanceSquared(new Vector2(-2f, 3f)) < .00001f).ToArray();
            Assert.That(cut, Is.Not.Empty);
            Assert.That(cut.All(t => t.excavationHeight > 0f));
            Assert.That(net.edges.Single(e => e.a == start).structure, Is.EqualTo(RoadStructure.Ground));
        }

        [Test]
        public void ServiceRoadBridgeAndOpenCutCloseTheirDeadEnds()
        {
            var airport=Airfield();var net=airport.Taxiways;
            net.Connect(net.AddNode(new Vector3(-80,10,0)),net.AddNode(new Vector3(0,10,0)),7,PavementSurface.Asphalt,false,RoadClass.Service,false,RoadStructure.Elevated);
            net.Connect(net.AddNode(new Vector3(-80,-3,100)),net.AddNode(new Vector3(0,-3,100)),7,PavementSurface.Asphalt,false,RoadClass.Service,false,RoadStructure.Tunnel);
            net.Rebuild();
            var builder=new TaxiwayBuilder(net,airport.Runways);var opened=new List<BuiltSurfaceMap.Triangle>();
            var mesh=AirfieldRoadGeometry.Structures(airport,net,builder,opened).Create("Service road ends");owned.Add(mesh);
            var v=mesh.vertices;float reach=3.5f+TaxiwayBuilder.StructureVerge,thick=RoadStructureBuilder.WallThickness;
            // the bridge: the rail runs across both ends
            float top=10+AirfieldHeights.Road+RoadStructureBuilder.ParapetHeight;
            var rail=v.Where(p=>Mathf.Abs(p.y-top)<.003f).ToArray();
            Assert.IsTrue(rail.Any(p=>p.x>0&&Mathf.Abs(p.z)<.5f));Assert.IsTrue(rail.Any(p=>p.x<-80&&Mathf.Abs(p.z)<.5f));
            var deck=v.Where(p=>Mathf.Abs(p.z)<20).ToArray();
            Assert.AreEqual(TaxiwayBuilder.StructureVerge,deck.Max(p=>p.x),.02f);Assert.AreEqual(-80-TaxiwayBuilder.StructureVerge,deck.Min(p=>p.x),.02f);
            // the open cut: a wall across both ends, its coping just above the ground, the terrain opened up to it
            var cut=v.Where(p=>Mathf.Abs(p.z-100)<20).ToArray();
            Assert.AreEqual(TaxiwayBuilder.StructureVerge+thick,cut.Max(p=>p.x),.02f);Assert.AreEqual(-80-TaxiwayBuilder.StructureVerge-thick,cut.Min(p=>p.x),.02f);
            Assert.AreEqual(RoadStructureBuilder.Coping,cut.Max(p=>p.y),.02f);
            Assert.IsTrue(cut.Any(p=>p.x>TaxiwayBuilder.StructureVerge&&Mathf.Abs(p.z-100)<.7f&&p.y>0));
            using var mask=new BuiltSurfaceMap();foreach(var triangle in opened)mask.AddTriangle(triangle);
            Assert.IsTrue(mask.Contains(new Vector2(TaxiwayBuilder.StructureVerge+thick-.1f,100)));
            Assert.IsFalse(mask.Contains(new Vector2(TaxiwayBuilder.StructureVerge+thick+.2f,100)));
            Assert.IsFalse(mask.Contains(new Vector2(-40,100+reach+thick+.2f)));
        }
    }
}
