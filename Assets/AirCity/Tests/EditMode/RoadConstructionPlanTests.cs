using System.Collections.Generic;
using System.Linq;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Game
{
    public class RoadConstructionPlanTests
    {
        static CareerState State()=>new CareerState {mode=GameMode.Career,funds=1000000,wholeRegion=true,world=new ValleyWorld {heights=Enumerable.Repeat(100f,ValleyWorld.Resolution*ValleyWorld.Resolution).ToArray()}};
        static PublicRoadNetwork Graph(out int start){var graph=new PublicRoadNetwork();start=graph.AddNode(new Vector3(-200,100,0));return graph;}
        [Test]public void PreviewIsPureCommitIsExactAndAStaleLayoutOrQuoteCannotSpend()
        {
            var state=State();var graph=Graph(out int from);string before=JsonUtility.ToJson(graph);
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,graph,from,new Vector3(200,108,0),-1,-1,PublicRoadStyle.Urban,RoadBuildSettings.Default,out var plan,out var why),why);
            Assert.AreEqual(before,JsonUtility.ToJson(graph));Assert.AreEqual(1000000,state.funds);
            string expected=JsonUtility.ToJson(plan.Draft);int price=plan.Price;
            Assert.IsTrue(plan.TryCommit(state,graph,out var committed,out why),why);Assert.AreEqual(expected,JsonUtility.ToJson(committed));Assert.AreEqual(1000000-price,state.funds);
            Assert.IsFalse(plan.TryCommit(state,graph,out _,out _));Assert.AreEqual(1000000-price,state.funds);
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,committed,plan.EndNode,new Vector3(450,108,0),-1,-1,PublicRoadStyle.Urban,RoadBuildSettings.Default,out var stale,out why),why);
            committed.Node(plan.EndNode).position+=Vector3.up;int money=state.funds;
            Assert.IsFalse(stale.TryCommit(state,committed,out _,out _));Assert.AreEqual(money,state.funds);
        }
        [Test]public void AutomaticSectionsKeepTheContinuousGradeAndSaveBridgeAndTunnelSpans()
        {
            var state=State();var graph=Graph(out int from);
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,graph,from,new Vector3(200,110,0),-1,-1,PublicRoadStyle.Urban,RoadBuildSettings.Default,out var bridge,out var why),why);
            Assert.IsTrue(bridge.Draft.segments.Any(e=>e.structure==RoadStructure.Ground));Assert.IsTrue(bridge.Draft.segments.Any(e=>e.structure==RoadStructure.Elevated));
            foreach(var e in bridge.Draft.segments)
            {var path=bridge.Draft.Path(e);Assert.AreEqual(bridge.Path.Height(bridge.Path.ClosestParameter(Alignment3D.XZ(path.Position(.4f)))) ,path.Height(.4f),.001f);}
            Assert.IsTrue(RoadConstructionPlan.TryCreate(state,graph,from,new Vector3(200,90,0),-1,-1,PublicRoadStyle.Urban,RoadBuildSettings.Default,out var tunnel,out why),why);
            var loaded=JsonUtility.FromJson<PublicRoadNetwork>(JsonUtility.ToJson(tunnel.Draft));Assert.IsTrue(loaded.segments.Any(e=>e.structure==RoadStructure.Tunnel));
            var geometry=new PublicRoadGeometry(loaded,null,null,_=>100);
            try
            {
                using var mask=new BuiltSurfaceMap();foreach(var triangle in geometry.groundTriangles)mask.AddTriangle(triangle);
                Assert.IsTrue(mask.Contains(new Vector2(-150,0)),"The surface approach must be owned.");
                Assert.IsFalse(mask.Contains(new Vector2(190,0)),"A covered bore cannot remove its overlying terrain.");
                Assert.Greater(geometry.structures.vertexCount,100);
            }
            finally{Object.DestroyImmediate(geometry.pavement);Object.DestroyImmediate(geometry.markings);Object.DestroyImmediate(geometry.shoulders);Object.DestroyImmediate(geometry.structures);Object.DestroyImmediate(geometry.kerbMesh);Object.DestroyImmediate(geometry.medianGrass);}
        }
        [Test]public void LocalEditKeepsDistantCommittedMeshesAndMatchesTheFullTerrainFormation()
        {
            var state=State();var graph=Graph(out int from);int b=graph.AddNode(new Vector3(0,100,0));graph.AddSegment(from,b,PublicRoadStyle.Urban,RoadStructure.Ground);
            var far=graph.AddSegment(graph.AddNode(new Vector3(-1500,100,-1200)),graph.AddNode(new Vector3(1500,100,-1200)),PublicRoadStyle.Rural,RoadStructure.Ground);
            using var chunks=new PublicRoadChunks(null,null);chunks.Update(graph,state.world.Height,null);
            var distant=chunks.Parts.First(p=>!p.junction&&p.id==far.id).geometry.pavement;
            var next=graph.Clone();Assert.IsTrue(next.Connect(b,next.AddNode(new Vector3(150,104,120)),PublicRoadStyle.Urban,RoadStructure.Ground,out var why),why);
            chunks.Update(next,state.world.Height,null);Assert.AreSame(distant,chunks.Parts.First(p=>!p.junction&&p.id==far.id).geometry.pavement);
            var full=new PublicRoadGeometry(next,null,null,state.world.Height);
            try
            {
                CollectionAssert.AreEqual(full.GradedHeights(state.world),chunks.Geometry.GradedHeights(state.world));
                Assert.AreEqual(full.pavement.vertexCount,chunks.Geometry.pavement.vertexCount);
            }
            finally{Object.DestroyImmediate(full.pavement);Object.DestroyImmediate(full.markings);Object.DestroyImmediate(full.shoulders);Object.DestroyImmediate(full.structures);Object.DestroyImmediate(full.kerbMesh);Object.DestroyImmediate(full.medianGrass);}
        }
    }
}
