using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class RoadFinishTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void Cleanup(){foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();}
        void Own(PublicRoadGeometry g){owned.AddRange(new Object[]{g.pavement,g.shoulders,g.markings,g.kerbMesh,g.medianGrass,g.structures});}
        [Test]
        public void GroundRoadEndsRoundTheirCornersButOutsideConnectionsKeepTheirFullWidth()
        {
            var graph=new PublicRoadNetwork();int a=graph.AddNode(new Vector3(-50,100,0)),b=graph.AddNode(new Vector3(50,100,0));
            graph.AddSegment(a,b,PublicRoadStyle.Urban,RoadStructure.Ground);
            var geometry=new PublicRoadGeometry(graph,null,null,_=>100);Own(geometry);
            using var mask=new BuiltSurfaceMap();mask.AddMesh(geometry.pavement,Matrix4x4.identity,false);
            float half=graph.segments[0].Width*.5f;
            Assert.IsTrue(mask.Contains(new Vector2(49.9f,0)));Assert.IsFalse(mask.Contains(new Vector2(49.9f,half-.1f)));
            Assert.LessOrEqual(geometry.pavement.bounds.max.x,50.001f);
            graph.Node(b).outsideConnection=true;var outside=new PublicRoadGeometry(graph,null,null,_=>100);Own(outside);
            using var border=new BuiltSurfaceMap();border.AddMesh(outside.pavement,Matrix4x4.identity,false);
            Assert.IsTrue(border.Contains(new Vector2(49.9f,half-.1f)));
        }
        [TestCase(PublicRoadStyle.Urban)] [TestCase(PublicRoadStyle.Avenue)]
        public void LaneArrowHeadsAreContinuousAndAllJunctionPaintFollowsTheRoadGrade(PublicRoadStyle style)
        {
            var graph=new PublicRoadNetwork();int c=graph.AddNode(new Vector3(0,100,0));
            foreach(var p in new[]{new Vector3(-180,92,0),new Vector3(180,111,0),new Vector3(0,107,-160)})
                graph.AddSegment(c,graph.AddNode(p),style,RoadStructure.Ground);
            var geometry=new PublicRoadGeometry(graph,null,null,_=>100);Own(geometry);
            var mesh=geometry.markings;var vertices=mesh.vertices;var shapes=new List<Vector4>();mesh.GetUVs(1,shapes);
            Assert.Greater(shapes.Count(v=>v.w>1.5f),8,"Arrows must use analytic triangular heads.");
            var road=geometry.pavement.vertices;var indices=geometry.pavement.triangles;int checkedVertices=0;
            for(int i=0;i<vertices.Length;i++)
            {
                var p=vertices[i];float surface=float.NegativeInfinity;
                for(int t=0;t<indices.Length;t+=3)
                {
                    var triangle=new BuiltSurfaceMap.Triangle(road[indices[t]],road[indices[t+1]],road[indices[t+2]]);
                    if(Mathf.Abs(Vector3.Cross(triangle.b-triangle.a,triangle.c-triangle.a).y)<.0001f)continue;
                    if(triangle.DistanceSquared(new Vector2(p.x,p.z))<.000001f)surface=Mathf.Max(surface,triangle.Height(new Vector2(p.x,p.z)));
                }
                Assert.IsFalse(float.IsNegativeInfinity(surface),"A marking leaves its road at "+p);
                Assert.GreaterOrEqual(p.y-surface,-.002f,"Paint is buried in the road at "+p);
                Assert.Less(p.y-surface,.1f,"Paint floats above its road at "+p);checkedVertices++;
            }
            Assert.Greater(checkedVertices,100);
        }
        [TestCase(false)] [TestCase(true)]
        public void CachedStreetPreviewMatchesAFullBuildIncludingGradedJunctionPaintAndKeepsDistantRoadsStable(bool split)
        {
            var source=new PublicRoadNetwork();int previous=-1,from=-1;
            for(int i=0;i<14;i++)
            {
                int node=source.AddNode(new Vector3(-780+i*120,94+i*.8f,0));
                if(previous>=0)source.AddSegment(previous,node,PublicRoadStyle.Avenue,RoadStructure.Ground);
                previous=node;if(i==6)from=node;
                if(i==10)source.AddSegment(node,source.AddNode(new Vector3(-780+i*120,104,-160)),PublicRoadStyle.Urban,RoadStructure.Ground);
            }
            source.AddSegment(source.AddNode(new Vector3(-3000,90,-1000)),source.AddNode(new Vector3(3000,110,-1000)),PublicRoadStyle.Rural,RoadStructure.Ground);
            var original=new PublicRoadGeometry(source,null,null,_=>90);Own(original);
            var draft=source.Clone();
            if(split)
            {
                var edge=draft.segments[5];var mid=(draft.Node(edge.a).position+draft.Node(edge.b).position)*.5f;
                from=draft.Split(edge,mid);
            }
            int end=draft.AddNode(draft.Node(from).position+new Vector3(45,3,160));
            Assert.IsTrue(draft.Connect(from,end,PublicRoadStyle.Avenue,RoadStructure.Ground,out var problem),problem);
            var partition=new PublicRoadPreviewPartition(source,draft);
            Assert.IsNotEmpty(partition.unchangedSegments,"The remote regional road must be reused.");
            var unchanged=new PublicRoadGeometry(draft,null,null,_=>90,partition.unchangedSegments,partition.unchangedJunctions,original);Own(unchanged);
            var changing=new PublicRoadGeometry(draft,null,null,_=>90,partition.changedSegments,partition.changedJunctions,unchanged);Own(changing);
            var full=new PublicRoadGeometry(draft,null,null,_=>90);Own(full);
            string[] Points(params Mesh[] meshes)=>meshes.SelectMany(m=>m.vertices).Select(p=>p.ToString("F3")).OrderBy(p=>p).ToArray();
            CollectionAssert.AreEqual(Points(full.pavement),Points(unchanged.pavement,changing.pavement));
            CollectionAssert.AreEqual(Points(full.shoulders),Points(unchanged.shoulders,changing.shoulders));
            CollectionAssert.AreEqual(Points(full.kerbMesh),Points(unchanged.kerbMesh,changing.kerbMesh));
            CollectionAssert.AreEqual(Points(full.markings),Points(unchanged.markings,changing.markings));
            draft.Node(end).position+=new Vector3(15,1,25);
            var moved=new PublicRoadPreviewPartition(source,draft);
            CollectionAssert.AreEquivalent(partition.unchangedSegments,moved.unchangedSegments);
            CollectionAssert.AreEquivalent(partition.unchangedJunctions,moved.unchangedJunctions);
            var unchangedMoved=new PublicRoadGeometry(draft,null,null,_=>90,moved.unchangedSegments,moved.unchangedJunctions,original);Own(unchangedMoved);
            CollectionAssert.AreEqual(Points(unchanged.pavement),Points(unchangedMoved.pavement));
            CollectionAssert.AreEqual(Points(unchanged.markings),Points(unchangedMoved.markings));
            Assert.AreEqual(15,source.segments.Count,"Preview cannot split or edit the source graph.");
        }
    }
}
