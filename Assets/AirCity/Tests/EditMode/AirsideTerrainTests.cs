using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public class AirsideTerrainTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown] public void Cleanup(){foreach(var o in owned)if(o!=null)Object.DestroyImmediate(o);owned.Clear();}
        CareerState State(Func<float,float,float> height)
        {
            var world=new ValleyWorld {heights=new float[ValleyWorld.Resolution*ValleyWorld.Resolution]};
            world.sites.Add(new AirportSite {name="Pavement fixture",centre=Vector2.zero,elevation=100f});
            for(int z=0;z<ValleyWorld.Resolution;z++)for(int x=0;x<ValleyWorld.Resolution;x++)
                world.heights[z*ValleyWorld.Resolution+x]=height(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f);
            return new CareerState {world=world,wholeRegion=true,handsOn=true,selectedSite=0,funds=2000000,layout=new CareerLayout(),publicRoads=new PublicRoadNetwork()};
        }
        Airport Airport()
        {
            var go=new GameObject("Airside terrain fixture");go.SetActive(false);owned.Add(go);
            go.transform.position=new Vector3(0f,100f,0f);var a=go.AddComponent<Airport>();a.groundSize=0;go.SetActive(true);return a;
        }
        static Vector3 Point(CareerState state,float x,float z)=>new Vector3(x,state.world.Height(new Vector2(x,z))-100f,z);
        static bool Quote(CareerState state,Airport airport,int start,Vector3 end,out AirsideTerrainPlan plan,out string why,RoadClass road=RoadClass.None,int node=-1,TaxiwayEdge edge=null)
            =>AirsideTerrainPlan.TryCreate(state,airport,start,end,node,edge,null,road==RoadClass.None?15f:7f,PavementSurface.Asphalt,road,out plan,out why);
        static void Supported(CareerState state,Airport airport)
        {
            var mesh=airport.Taxiways.SurfaceMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;
            float maxGap=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var a=airport.transform.TransformPoint(vertices[triangles[i]]);var b=airport.transform.TransformPoint(vertices[triangles[i+1]]);var c=airport.transform.TransformPoint(vertices[triangles[i+2]]);
                // The road's edge faces descend into the supporting soil. Check only the drivable top.
                if (Mathf.Abs(Vector3.Cross(b-a,c-a).y) < .0001f) continue;
                for(int j=0;j<=4;j++)for(int k=0;k<=4-j;k++)
                {
                    var p=a*(j/4f)+b*(k/4f)+c*(1f-(j+k)/4f);
                    float terrain=state.world.Height(new Vector2(p.x,p.z));
                    Assert.LessOrEqual(terrain,p.y+.003f,$"Terrain clips pavement at {p}: ground {terrain}.");
                    maxGap=Mathf.Max(maxGap,p.y-terrain);
                }
            }
            Assert.Less(maxGap,2f,"A supported road must not float high above its formation.");
        }

        [TestCase(RoadClass.None)] [TestCase(RoadClass.Service)]
        public void SlopedPavementGradesCrossfallAndBumpsWithAnExactPureQuote(RoadClass road)
        {
            var state=State((x,z)=>100f+x*.07f+z*.10f+Mathf.Sin(x*.025f)*2f);var airport=Airport();var net=airport.Taxiways;
            int start=net.AddNode(Point(state,-220,0));var heights=(float[])state.world.heights.Clone();
            Assert.IsTrue(Quote(state,airport,start,Point(state,220,0),out var plan,out var why,road),why);
            Assert.Greater(plan.GradingPrice,0);Assert.Greater(plan.End.y,net.Node(start).position.y);
            CollectionAssert.AreEqual(heights,state.world.heights);Assert.AreEqual(0,net.edges.Count);Assert.AreEqual(2000000,state.funds);
            Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();
            Assert.AreEqual(2000000-plan.Price,state.funds);
            double volume=0;for(int i=0;i<heights.Length;i++)volume+=Math.Abs(state.world.heights[i]-heights[i])*state.world.Spacing*state.world.Spacing;
            Assert.AreEqual(plan.Earthwork,volume,Math.Max(2f,volume*.00002f));
            Assert.IsTrue(net.edges.All(e=>e.naturalEdges));Assert.AreEqual(0,net.VergeMesh.vertexCount);Supported(state,airport);
            int funds=state.funds;Assert.IsFalse(plan.TryCommit(state,out _));Assert.AreEqual(funds,state.funds);
            Assert.AreEqual((net.Node(start).position.y+plan.End.y)*.5f,airport.PavementElevationAt(Vector2.zero),.01f);
        }

        [TestCase(RoadClass.None)] [TestCase(RoadClass.Service)]
        public void ASlopedBendAndItsPreviousApproachRemainAboveTheGradedTerrain(RoadClass road)
        {
            var state=State((x,z)=>100f+x*.035f+z*.045f+Mathf.Sin(z*.025f)*1.4f);var airport=Airport();var net=airport.Taxiways;
            int a=net.AddNode(Point(state,-240,0));
            Assert.IsTrue(Quote(state,airport,a,Point(state,0,0),out var first,out var why,road),why);
            Assert.IsTrue(first.TryCommit(state,out why),why);net.Rebuild();
            var end=Point(state,0,240);
            Assert.IsTrue(Quote(state,airport,first.EndNode,end,out var bend,out why,road),why);
            Assert.IsTrue(bend.TryCommit(state,out why),why);net.Rebuild();
            Supported(state,airport);Assert.IsTrue(airport.HasElevatedPavement);Assert.AreEqual(0,net.VergeMesh.vertexCount);
        }

        [TestCase(PavementSurface.Grass,StandKind.Parking)]
        [TestCase(PavementSurface.Asphalt,StandKind.Parking)]
        [TestCase(PavementSurface.Concrete,StandKind.DeIcing)]
        public void StandPreviewAndCommitShareAutomaticGradingClearingAndSavedElevation(PavementSurface surface,StandKind kind)
        {
            var state=State((x,z)=>100f+x*.025f+z*.04f+Mathf.Sin(x*.08f));var airport=Airport();
            var stand=new Stand {position=new Vector3(200,0,200),heading=0,code=SizeCode.A,surface=surface,kind=kind};
            stand.Frame(kind==StandKind.DeIcing?Vector2.up:Vector2.right,0);
            var layout=StandLayout.Of(stand);stand.position.y=state.world.Height(layout.Centre)-100f;
            state.world.trees.Add(new ForestTree {position=layout.Centre,height=12});
            var rock=new GroundCoverInstance {id=17,kind=GroundCoverKind.Boulder,position=layout.Centre,scale=1};
            var before=(float[])state.world.heights.Clone();
            Assert.IsTrue(AirsideTerrainPlan.TryCreateStand(state,airport,layout,out var plan,out var why,new[]{rock}),why);
            Assert.Greater(plan.GradingPrice,0);CollectionAssert.AreEqual(before,state.world.heights);
            Assert.IsFalse(state.world.trees[0].removed);Assert.IsEmpty(airport.Taxiways.stands);
            Assert.IsTrue(plan.TryCommit(state,out why),why);airport.Taxiways.Rebuild();
            Assert.AreEqual(2000000-plan.Price,state.funds);Assert.IsTrue(state.world.trees[0].removed);Assert.IsTrue(state.world.GroundPropRemoved(rock.id));
            Assert.AreEqual(stand.position.y,plan.PlacedStand.position.y,.001f);
            Assert.AreEqual(stand.position.y,airport.PavementElevationAt(layout.Centre),.001f);Supported(state,airport);
            var saved=AirportSaveData.Capture(airport,GameAssetCatalog.Load());
            var loaded=JsonUtility.FromJson<AirportSaveData>(JsonUtility.ToJson(saved)).Restore(null,GameAssetCatalog.Load());owned.Add(loaded.gameObject);
            Assert.AreEqual(stand.position.y,loaded.Taxiways.stands.Single().position.y,.001f);
            Assert.AreEqual(stand.position.y,loaded.PavementElevationAt(layout.Centre),.001f);
            int balance=state.funds;Assert.IsFalse(plan.TryCommit(state,out _));Assert.AreEqual(balance,state.funds);
        }

        [Test]
        public void StandRefusalsAreQuotedBeforeClickAndNeverPartiallyWriteTheWorld()
        {
            var state=State((x,z)=>100f+x*.03f);var airport=Airport();
            var stand=new Stand {position=new Vector3(200,6,200),code=SizeCode.A,surface=PavementSurface.Grass};stand.Frame(Vector2.right,0);
            var layout=StandLayout.Of(stand);var heights=(float[])state.world.heights.Clone();state.funds=1;
            Assert.IsFalse(AirsideTerrainPlan.TryCreateStand(state,airport,layout,out var plan,out var why));StringAssert.Contains("funds",why);
            Assert.IsFalse(plan.TryCommit(state,out _));CollectionAssert.AreEqual(heights,state.world.heights);Assert.IsEmpty(airport.Taxiways.stands);
            state.funds=2000000;
            Assert.IsTrue(AirsideTerrainPlan.TryCreateStand(state,airport,layout,out plan,out why),why);
            int index=state.world.heights.Length/2;state.world.heights[index]+=1; // outside footprint doesn't stale a local quote
            int touched=(Mathf.RoundToInt((layout.Centre.y+3200)/state.world.Spacing))*ValleyWorld.Resolution+Mathf.RoundToInt((layout.Centre.x+3200)/state.world.Spacing);
            state.world.heights[touched]+=1;
            var changed=(float[])state.world.heights.Clone();Assert.IsFalse(plan.TryCommit(state,out _));CollectionAssert.AreEqual(changed,state.world.heights);
            stand.position=new Vector3(3195,6,200);layout=StandLayout.Of(stand);
            Assert.IsFalse(AirsideTerrainPlan.TryCreateStand(state,airport,layout,out plan,out why));StringAssert.Contains("region",why);
            if(plan!=null)Assert.IsFalse(plan.TryCommit(state,out _));Assert.AreEqual(2000000,state.funds);Assert.IsEmpty(airport.Taxiways.stands);
        }

        [Test]
        public void ParkingPadMeetsAGradedTaxilaneWithoutLosingItsOwnHeight()
        {
            var state=State((x,z)=>100f+x*.04f+z*.025f);var airport=Airport();var net=airport.Taxiways;
            int a=net.AddNode(Point(state,-200,0));
            Assert.IsTrue(Quote(state,airport,a,Point(state,200,0),out var road,out var why),why);Assert.IsTrue(road.TryCommit(state,out why),why);net.Rebuild();
            var stand=new Stand {position=new Vector3(-40,airport.PavementElevationAt(new Vector2(-40,0)),0),heading=0,code=SizeCode.B,surface=PavementSurface.Asphalt};
            stand.Frame(Vector2.right,15);var layout=StandLayout.Of(stand);layout.edge=net.edges.Single();
            Assert.IsTrue(AirsideTerrainPlan.TryCreateStand(state,airport,layout,out var plan,out why),why);Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();
            Assert.AreEqual(-1.6f,plan.PlacedStand.position.y,.01f);
            Assert.AreEqual(-1.6f,airport.PavementElevationAt(layout.Centre),.01f);
            Assert.AreEqual(-1.6f,airport.PavementElevationAt(layout.entry),.01f);Supported(state,airport);
        }

        [Test]
        public void CurvedDraftUsesTheCommittedGeometryAndCancelRestoresTheTerrainMask()
        {
            var state=State((x,z)=>100f+x*.015f+z*.02f);var airport=Airport();var net=airport.Taxiways;
            int from=net.AddNode(Point(state,-160,0));
            Assert.IsTrue(Quote(state,airport,from,Point(state,0,0),out var first,out var why,RoadClass.Service),why);
            Assert.IsTrue(first.TryCommit(state,out why),why);net.Rebuild();
            string graph=JsonUtility.ToJson(net);int funds=state.funds;var heights=(float[])state.world.heights.Clone();
            Assert.IsTrue(Quote(state,airport,first.EndNode,Point(state,90,100),out var plan,out why,RoadClass.Service),why);
            Assert.IsNotNull(plan.Preview);Assert.Greater(plan.Preview.outline.Count,16);
            var expected=plan.Preview.surface.mesh.positions.ToArray();
            var go=new GameObject("Preview terrain");owned.Add(go);var valley=go.AddComponent<ValleyView>();valley.Build(state.world,state.publicRoads,false);valley.SetBuiltAirport(airport);
            var committed=valley.BuiltSurfaces;var preview=new TaxiwayPreview();
            try
            {
                preview.Show(airport,plan.Preview,valley);
                Assert.IsTrue(valley.HasConstructionPreview);Assert.AreSame(committed,valley.BuiltSurfaces);
                Assert.IsFalse(net.transform.Find("Surface").GetComponent<Renderer>().enabled);
                Assert.AreEqual(graph,JsonUtility.ToJson(net));Assert.AreEqual(funds,state.funds);CollectionAssert.AreEqual(heights,state.world.heights);
                preview.Hide();Assert.IsFalse(valley.HasConstructionPreview);
                Assert.IsTrue(net.transform.Find("Surface").GetComponent<Renderer>().enabled);Assert.AreSame(committed,valley.BuiltSurfaces);
                Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();
                CollectionAssert.AreEqual(expected,net.SurfaceMesh.vertices,"The reviewed curve must be the geometry that commits.");
            }
            finally{preview.Destroy();}
        }

        [Test]
        public void SplitKeepsInterpolatedElevationAndNaturalEdgeState()
        {
            var airport=Airport();var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-100,-10,0)),b=net.AddNode(new Vector3(100,10,0));
            var edge=net.Connect(a,b,15,PavementSurface.Asphalt,false,RoadClass.None,true).Single();
            int mid=net.SplitEdge(edge,new Vector3(40,999,0));
            Assert.AreEqual(4f,net.Node(mid).position.y,.001f);Assert.IsTrue(net.edges.All(e=>e.naturalEdges));
        }

        [Test]
        public void UnaffordableStaleAndMismatchedCrossingsLeaveGraphGroundAndFundsUntouched()
        {
            var state=State((x,z)=>100f+x*.05f+z*.06f);var airport=Airport();var net=airport.Taxiways;
            int a=net.AddNode(Point(state,-200,0));
            Assert.IsTrue(Quote(state,airport,a,Point(state,200,0),out var plan,out var why),why);
            var before=(float[])state.world.heights.Clone();state.funds=1;
            Assert.IsFalse(plan.TryCommit(state,out _));CollectionAssert.AreEqual(before,state.world.heights);Assert.AreEqual(0,net.edges.Count);Assert.AreEqual(1,net.nodes.Count);
            state.funds=2000000;int index=256*ValleyWorld.Resolution+256;state.world.heights[index]+=1f;
            var changed=(float[])state.world.heights.Clone();Assert.IsFalse(plan.TryCommit(state,out _));CollectionAssert.AreEqual(changed,state.world.heights);Assert.AreEqual(2000000,state.funds);
            int c=net.AddNode(new Vector3(0,20,-100)),d=net.AddNode(new Vector3(0,20,100));net.Connect(c,d,15,PavementSurface.Asphalt);net.Rebuild();
            Assert.IsFalse(Quote(state,airport,a,Point(state,200,0),out _,out why));StringAssert.Contains("height",why);
            Assert.AreEqual(1,net.edges.Count);CollectionAssert.AreEqual(changed,state.world.heights);
        }

        [Test]
        public void RoadPreviewIncludesVegetationAndOnlyAcceptedConstructionClearsIt()
        {
            var state=State((x,z)=>100f+x*.06f);var airport=Airport();var net=airport.Taxiways;int a=net.AddNode(Point(state,-100,0));
            state.world.trees.Add(new ForestTree {position=Vector2.zero,height=12f});
            var rock=new GroundCoverInstance {id=1,kind=GroundCoverKind.Boulder,position=Vector2.zero,scale=1f};
            Assert.IsTrue(AirsideTerrainPlan.TryCreate(state,airport,a,Point(state,100,0),-1,null,null,15,PavementSurface.Asphalt,RoadClass.Service,out var plan,out var why,new[]{rock}),why);
            Assert.IsFalse(state.world.trees[0].removed);Assert.IsFalse(state.world.GroundPropRemoved(rock.id));
            state.funds=1;Assert.IsFalse(plan.TryCommit(state,out _));Assert.IsFalse(state.world.trees[0].removed);Assert.IsFalse(state.world.GroundPropRemoved(rock.id));
            state.funds=2000000;Assert.IsTrue(plan.TryCommit(state,out why),why);
            Assert.IsTrue(state.world.trees[0].removed);Assert.IsTrue(state.world.GroundPropRemoved(rock.id));
            Assert.IsFalse(Quote(state,airport,a,new Vector3(100,90,0),out _,out why));StringAssert.Contains("steep",why);
        }

        [Test]
        public void AChangedForestInvalidatesAutomaticClearanceBeforeAnyTerrainOrFundsAreWritten()
        {
            var state=State((x,z)=>100f+x*.05f+z*.04f);var airport=Airport();int from=airport.Taxiways.AddNode(Point(state,-100,0));
            state.world.trees.Add(new ForestTree {position=Vector2.zero,height=12});
            Assert.IsTrue(Quote(state,airport,from,Point(state,100,0),out var plan,out var why),why);
            var heights=(float[])state.world.heights.Clone();state.world.trees[0].position=new Vector2(50,60);
            Assert.IsFalse(plan.TryCommit(state,out _));CollectionAssert.AreEqual(heights,state.world.heights);
            Assert.IsFalse(state.world.trees[0].removed);Assert.AreEqual(2000000,state.funds);
        }

        [Test]
        public void SplittingAGradedApproachUsesItsRenderedHeightAfterTheBendWasTrimmed()
        {
            var state=State((x,z)=>100f+x*.06f+z*.03f);var airport=Airport();var net=airport.Taxiways;
            int a=net.AddNode(Point(state,-300,0));
            Assert.IsTrue(Quote(state,airport,a,Point(state,0,0),out var first,out var why),why);
            Assert.IsTrue(first.TryCommit(state,out why),why);net.Rebuild();
            Assert.IsTrue(Quote(state,airport,first.EndNode,Point(state,0,300),out var bend,out why),why);
            Assert.IsTrue(bend.TryCommit(state,out why),why);net.Rebuild();
            var edge=net.edges.First(e=>e.a==a || e.b==a);var point=new Vector2(-120f,0f);
            float expected=airport.PavementElevationAt(point);
            int split=net.SplitEdge(edge,new Vector3(point.x,999f,point.y));
            Assert.AreEqual(expected,net.Node(split).position.y,.001f,"A T-junction must meet the actual trimmed approach grade.");
        }

        [Test]
        public void AGraphChangedWithoutARebuildStillInvalidatesAQuoteBeforeAnyGroundIsWritten()
        {
            var state=State((x,z)=>100f+x*.05f+z*.05f);var airport=Airport();var net=airport.Taxiways;
            int a=net.AddNode(Point(state,-200,0));
            Assert.IsTrue(Quote(state,airport,a,Point(state,200,0),out var plan,out var why),why);
            var heights=(float[])state.world.heights.Clone();net.AddNode(new Vector3(400f,10f,400f));
            Assert.IsFalse(plan.TryCommit(state,out _));CollectionAssert.AreEqual(heights,state.world.heights);Assert.AreEqual(2000000,state.funds);Assert.AreEqual(0,net.edges.Count);
        }

        [Test]
        public void PublicRoadProtectionAllowsNearbyCompatibleSoilButRefusesPavementOverlap()
        {
            var state=State((x,z)=>100f);var airport=Airport();var net=airport.Taxiways;int from=net.AddNode(Point(state,-200,0));
            int a=state.publicRoads.AddNode(new Vector3(-500,100,120)),b=state.publicRoads.AddNode(new Vector3(500,100,120));
            state.publicRoads.AddSegment(a,b,PublicRoadStyle.Rural,RoadStructure.Ground);
            Assert.IsTrue(Quote(state,airport,from,Point(state,200,0),out _,out var why),why);
            state.publicRoads.nodes[0].position=new Vector3(0,100,-300);state.publicRoads.nodes[1].position=new Vector3(0,100,300);
            var heights=(float[])state.world.heights.Clone();
            Assert.IsFalse(Quote(state,airport,from,Point(state,200,0),out _,out why));StringAssert.Contains("public road",why);
            CollectionAssert.AreEqual(heights,state.world.heights);Assert.AreEqual(2000000,state.funds);
        }

        [Test]
        public void SmallFormationAdjustmentsDoNotPaintArtificialSideBandsOnFlatNaturalLand()
        {
            var state=State((x,z)=>100f);var airport=Airport();int a=airport.Taxiways.AddNode(Point(state,-100,0));
            Assert.IsTrue(Quote(state,airport,a,Point(state,100,0),out var plan,out var why),why);
            Assert.IsTrue(plan.TryCommit(state,out why),why);airport.Taxiways.Rebuild();
            Assert.IsNull(state.world.disturbance,"Automatic formation must not paint the ground beside the pavement.");
            Assert.AreEqual(0,airport.Taxiways.VergeMesh.vertexCount);Supported(state,airport);
        }

        [Test]
        public void TaxiwayLeavesASlopedRunwayAtItsActualElevationAndKeepsTheRunwaySupported()
        {
            var state=State((x,z)=>100f+x*.06f+z*.03f);var airport=Airport();var net=airport.Taxiways;
            var runway=airport.AddRunway(new Vector3(-300,0,0),new Vector3(300,0,0),PavementSurface.Asphalt,18f);
            Assert.IsTrue(RunwayTerrainPlan.TryCreate(state,airport,runway,null,out var rwyPlan,out var why),why);
            Assert.IsTrue(rwyPlan.TryCommit(state,12000,out _,out why),why);rwyPlan.PlaceProfile(airport,runway,false);runway.Rebuild();
            int a=net.AddNode(new Vector3(100,0,0),runway);
            Assert.AreEqual(6f,net.Node(a).position.y,.002f);
            Assert.IsTrue(Quote(state,airport,a,Point(state,100,-250),out var plan,out why),why);
            Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();Supported(state,airport);
            Assert.AreEqual(107.8f,state.world.Height(new Vector2(130,0)),.05f,"Taxiway grading must keep the existing runway's support plane.");
        }

        [Test]
        public void GradedTJunctionSupportsEveryArmAndTheirSharedIntersection()
        {
            var state=State((x,z)=>100f+x*.06f+z*.035f);var airport=Airport();var net=airport.Taxiways;
            int a=net.AddNode(Point(state,-250,0));
            Assert.IsTrue(Quote(state,airport,a,Point(state,250,0),out var first,out var why),why);
            Assert.IsTrue(first.TryCommit(state,out why),why);net.Rebuild();
            var edge=net.edges.Single();int b=net.AddNode(Point(state,0,-250));
            Assert.IsTrue(Quote(state,airport,b,new Vector3(0,999,0),out var junction,out why,RoadClass.None,-1,edge),why);
            Assert.AreEqual(0f,junction.End.y,.01f);
            Assert.IsTrue(junction.TryCommit(state,out why),why);net.Rebuild();Supported(state,airport);
            Assert.AreEqual(3,net.edges.Count);Assert.AreEqual(0f,airport.PavementElevationAt(Vector2.zero),.001f);
        }

        [Test]
        public void NodeElevationsNaturalEdgesAndTerrainSurvivePortableSave()
        {
            var state=State((x,z)=>100f+x*.07f+z*.03f);var airport=Airport();int a=airport.Taxiways.AddNode(Point(state,-200,0));
            Assert.IsTrue(Quote(state,airport,a,Point(state,200,0),out var plan,out var why),why);Assert.IsTrue(plan.TryCommit(state,out why),why);airport.Taxiways.Rebuild();
            var saved=AirportSaveData.Capture(airport,GameAssetCatalog.Load());var restored=JsonUtility.FromJson<AirportSaveData>(JsonUtility.ToJson(saved)).Restore(null,GameAssetCatalog.Load());owned.Add(restored.gameObject);
            Assert.AreEqual(airport.Taxiways.nodes[0].position,restored.Taxiways.nodes[0].position);Assert.IsTrue(restored.Taxiways.edges.All(e=>e.naturalEdges));
            Assert.AreEqual(airport.PavementElevationAt(new Vector2(125,0)),restored.PavementElevationAt(new Vector2(125,0)),.001f);Supported(state,restored);
        }
    }
}
