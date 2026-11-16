using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class Alignment3DTests
    {
        [Test]public void DefaultPathPreservesLegacyLinearElevationAndOffsets()
        {
            var path=new Alignment3D(new Vector3(0,10,0),new Vector3(100,20,0));
            Assert.AreEqual(new Vector3(50,15,0),path.Position(.5f));Assert.AreEqual(.1f,path.Grade(.5f),.00001f);
            Assert.AreEqual(new Vector3(50,15,-3),path.Offset(50,3));
        }
        [Test]public void SmoothGradeAndHorizontalCurveSurviveSplittingWithoutChangingThePath()
        {
            var path=new Alignment3D(new Vector3(0,100,0),new Vector3(200,110,0),new AlignmentShape {curved=true,control=new Vector2(90,70),smooth=true,startGrade=0,endGrade=.02f});
            Assert.Greater(path.Length,200);Assert.AreEqual(0,path.Grade(0),.000001f);
            float cut=.43f;var split=new Alignment3D(path.Position(cut),path.end,path.Slice(cut,1));
            for(int i=0;i<=20;i++)Assert.Less(Vector3.Distance(path.Position(Mathf.Lerp(cut,1,i/20f)),split.Position(i/20f)),.001f);
            var p=path.Horizontal(.37f);Assert.AreEqual(.37f,path.ClosestParameter(p),.00001f);
        }
        [Test]public void ConnectingAndSplittingACurvedRoadPreservesItsAuthoredElevationAndIndependentOverpass()
        {
            var net=new PublicRoadNetwork();int a=net.AddNode(new Vector3(-100,100,0)),b=net.AddNode(new Vector3(100,100,0));
            var edge=net.AddSegment(a,b,PublicRoadStyle.Urban,RoadStructure.Ground);edge.alignment=new AlignmentShape {curved=true,control=new Vector2(0,100)};
            var before=net.Path(edge);var at=before.Position(.5f);int mid=net.Split(edge,at);
            Assert.AreEqual(at,net.Node(mid).position);Assert.AreEqual(2,net.segments.Count);
            int c=net.AddNode(new Vector3(0,110,-50)),d=net.AddNode(new Vector3(0,110,150));
            Assert.IsTrue(net.Connect(c,d,PublicRoadStyle.Urban,RoadStructure.Elevated,out var problem),problem);
            Assert.AreEqual(3,net.segments.Count,"An overpass cannot connect to the road below.");
            string saved=JsonUtility.ToJson(net);var loaded=JsonUtility.FromJson<PublicRoadNetwork>(saved);
            Assert.AreEqual(at,loaded.Node(mid).position);Assert.IsTrue(loaded.segments.Exists(e=>e.alignment.curved));
        }
        [Test]public void DeckPickingUsesCurvesAndHeightAndRejectsDistantRays()
        {
            var path=new Alignment3D(new Vector3(-100,10,0),new Vector3(100,20,0),new AlignmentShape {curved=true,control=new Vector2(0,80),smooth=true,startGrade=0,endGrade=0});
            var at=path.Position(.5f);
            Assert.IsTrue(path.Raycast(new Ray(at+Vector3.up*100,Vector3.down),4,out var distance));
            Assert.AreEqual(100,distance,.01f);
            Assert.IsFalse(path.Raycast(new Ray(new Vector3(1000,100,1000),Vector3.down),4,out _));
        }
    }
}
