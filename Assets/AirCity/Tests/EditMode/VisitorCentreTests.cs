using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests
{
    public sealed class VisitorCentreTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown] public void Cleanup(){foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();}
        static PublicRoadNetwork ExistingRoad(float z=50f,float elevation=50f)
        {
            var road=new PublicRoadNetwork();
            int a=road.AddNode(new Vector3(-200,elevation,z));road.Node(a).outsideConnection=true;
            int reception=road.AddNode(new Vector3(80,elevation,z),"airport-reception");
            int b=road.AddNode(new Vector3(200,elevation,z));
            road.AddSegment(a,reception,PublicRoadStyle.AirportAccess,RoadStructure.Ground);
            road.AddSegment(reception,b,PublicRoadStyle.Rural,RoadStructure.Ground);
            return road;
        }
        [Test] public void ParkingHasEighteenDistinctBaysAndAnUnblockedPedestrianRoute()
        {
            var bays=VisitorCentreLayout.ParkingBays;
            Assert.AreEqual(18,bays.Count);Assert.AreEqual(2,bays.Count(b=>b.accessible));
            foreach(var bay in bays)
            {
                Assert.GreaterOrEqual(Mathf.Abs(bay.centre.x)-bay.width*.5f,1.3f,"the central pedestrian path stays clear");
                Assert.IsTrue(bay.centre.y+bay.length*.5f<=18.25f+.01f || bay.centre.y-bay.length*.5f>=24.75f-.01f,"no bay occupies the driving aisle");
            }
            for(int i=0;i<bays.Count;i++)for(int j=i+1;j<bays.Count;j++)
                if(Mathf.Abs(bays[i].centre.y-bays[j].centre.y)<1f)
                    Assert.GreaterOrEqual(Mathf.Abs(bays[i].centre.x-bays[j].centre.x),(bays[i].width+bays[j].width)*.5f);
        }
        [Test] public void DrivewayConnectsThroughPublicRoadsWithoutMutatingOrMovingAuthoredNodes()
        {
            var source=ExistingRoad();string before=JsonUtility.ToJson(source);
            Assert.True(VisitorCentreLayout.TryConnect(source,Vector2.zero,50,0,out var plan,out var why),why);
            Assert.AreEqual(before,JsonUtility.ToJson(source),"quotes are detached");
            Assert.AreEqual(VisitorCentreLayout.EntranceId,plan.Draft.Node(plan.EntranceNode).terminalEntrance);
            Assert.True(plan.Draft.Validate(out why),why);
            var outside=source.nodes.First(n=>n.outsideConnection);
            Assert.True(plan.Draft.HasRoute(plan.EntranceNode,outside.id));Assert.True(plan.Draft.HasRoute(outside.id,plan.EntranceNode));
            foreach(var n in source.nodes)
            {Assert.AreEqual(n.position,plan.Draft.Node(n.id).position);Assert.AreEqual(n.terminalEntrance,plan.Draft.Node(n.id).terminalEntrance);}
            Assert.IsNotEmpty(plan.NewSegments);Assert.Greater(plan.Price,0);
            float length=plan.NewSegments.Sum(id=>plan.Draft.Path(plan.Draft.Segment(id)).Length);
            Assert.AreEqual(length,plan.Length,.001f);
            Assert.AreEqual(Mathf.CeilToInt(length*RoadTypes.Of(PublicRoadStyle.AirportAccess).pricePerMetre),plan.Price);
            Assert.True(VisitorCentreLayout.TryConnect(plan.Draft,Vector2.zero,50,0,out var repeat,out why),why);
            Assert.AreEqual(0,repeat.NewSegments.Count,"existing saved connections are not duplicated");
        }
        [Test] public void FailedConnectionPreservesNetworkAndExistingEntranceIdentity()
        {
            var road=ExistingRoad();int node=road.AddNode(VisitorCentreLayout.Entrance(Vector2.zero,50,0),"airport-reception");
            string before=JsonUtility.ToJson(road);
            Assert.False(VisitorCentreLayout.TryConnect(road,Vector2.zero,50,0,out _,out _));
            Assert.AreEqual(before,JsonUtility.ToJson(road));Assert.AreEqual("airport-reception",road.Node(node).terminalEntrance);
            road=ExistingRoad(200);before=JsonUtility.ToJson(road);
            Assert.False(VisitorCentreLayout.TryConnect(road,Vector2.zero,50,0,out _,out _));Assert.AreEqual(before,JsonUtility.ToJson(road));
            road=ExistingRoad(50,100);before=JsonUtility.ToJson(road);
            Assert.False(VisitorCentreLayout.TryConnect(road,Vector2.zero,50,0,out _,out _));Assert.AreEqual(before,JsonUtility.ToJson(road));
        }

        [Test]
        public void ExistingEntranceMarkerNeedsARealTwoWayRoute()
        {
            var source = ExistingRoad();
            int port = source.AddNode(VisitorCentreLayout.Entrance(Vector2.zero, 50f, 0f), VisitorCentreLayout.EntranceId);
            Assert.IsTrue(VisitorCentreLayout.TryConnect(source, Vector2.zero, 50f, 0f, out var connected, out var why), why);
            Assert.AreEqual(port, connected.EntranceNode);
            Assert.IsNotEmpty(connected.NewSegments);
            Assert.IsEmpty(source.At(port));
            int outside = source.nodes.Single(n => n.outsideConnection).id;
            Assert.IsTrue(connected.Draft.HasRoute(port, outside));
            Assert.IsTrue(connected.Draft.HasRoute(outside, port));

            int stub = source.AddNode(new Vector3(34f, 50f, VisitorCentreLayout.AisleCentre));
            source.AddSegment(port, stub, PublicRoadStyle.AirportAccess, RoadStructure.Ground);
            Assert.IsFalse(VisitorCentreLayout.TryConnect(source, Vector2.zero, 50f, 0f, out _, out why));
            StringAssert.Contains("two-way public road", why);
        }
        [Test] public void DrivewayRefusesAnIntersectionThatWouldMoveAnExistingRoadBend()
        {
            var road=ExistingRoad(46,0);
            int a=road.AddNode(new Vector3(28,0,-20)),bend=road.AddNode(new Vector3(28,0,14)),b=road.AddNode(new Vector3(28,0,46));
            Assert.True(road.Connect(a,bend,PublicRoadStyle.Rural,RoadStructure.Ground,out var why),why);
            Assert.True(road.Connect(bend,b,PublicRoadStyle.Rural,RoadStructure.Ground,out why),why);
            Assert.True(road.Validate(out why),why);string before=JsonUtility.ToJson(road);
            Assert.False(VisitorCentreLayout.TryConnect(road,Vector2.zero,0,0,out _,out _),"the mandatory driveway crossing must not relocate the nearby degree-two node");
            Assert.AreEqual(new Vector3(28,0,14),road.Node(bend).position);Assert.AreEqual(before,JsonUtility.ToJson(road));
        }
        [Test] public void SiteRotationAndGroundOwnershipIncludeTheLotButNeverRoofOrFixtures()
        {
            var origin=new Vector2(100,-50);var entry=VisitorCentreLayout.Entrance(origin,20,90);
            Assert.AreEqual(121.5f,entry.x,.001f);Assert.AreEqual(-72f,entry.z,.001f);
            var polys=VisitorCentreLayout.PavedPolygons(Vector2.zero,0).ToArray();
            Assert.True(polys.Any(p=>Geo.Inside(p,new Vector2(10,21.5f))));
            Assert.False(polys.Any(p=>Geo.Inside(p,new Vector2(19,-7))),"land outside the slab cannot be clipped by a roof projection");
            var go=new GameObject("Visitor centre site test");owned.Add(go);
            var root=VisitorCentreBuilder.Build(go.transform,Vector2.zero,0,0,null);
            var site=VisitorCentreSiteBuilder.Build(root.transform,null);
            var ground=VisitorCentreSiteBuilder.GroundMesh(site);
            Assert.NotNull(ground);Assert.AreEqual("Visitor centre ground",ground.name);
            Assert.LessOrEqual(ground.bounds.max.y,.245f,"only the receiving driveway rises to the public road deck");
            Assert.Greater(root.GetComponentsInChildren<MeshRenderer>(true).Length,1);
        }

        [Test] public void ParkingMouthAndRoundedPublicRoadCapHaveContinuousPavementAcrossTheFullWidth()
        {
            var network=ExistingRoad(46,0);
            Assert.True(VisitorCentreLayout.TryConnect(network,Vector2.zero,0,0,out var plan,out var why),why);
            var rural=ScriptableObject.CreateInstance<PublicRoadProfile>();owned.Add(rural);
            var access=ScriptableObject.CreateInstance<PublicRoadProfile>();owned.Add(access);
            access.pavementWidth=6.5f;access.shoulderWidth=.8f;access.crown=.065f;
            var geometry=new PublicRoadGeometry(plan.Draft,rural,access,_=>0f);
            var road=geometry.pavement;owned.Add(road);
            foreach(var extra in new[]{geometry.shoulders,geometry.markings,geometry.kerbMesh,geometry.structures,geometry.medianGrass})if(extra!=null)owned.Add(extra);
            var host=new GameObject("Parking mouth test");owned.Add(host);
            var root=VisitorCentreBuilder.Build(host.transform,Vector2.zero,0,0,null);
            var site=VisitorCentreSiteBuilder.Build(root.transform,null);
            var ground=VisitorCentreSiteBuilder.GroundMesh(site);
            bool Covered(Mesh mesh,Vector2 p)
            {
                var vertices=mesh.vertices;var triangles=mesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=Geo.XZ(vertices[triangles[i]]);var b=Geo.XZ(vertices[triangles[i+1]]);var c=Geo.XZ(vertices[triangles[i+2]]);
                    float ab=Geo.Cross(b-a,p-a),bc=Geo.Cross(c-b,p-b),ca=Geo.Cross(a-c,p-c);
                    if(ab>=-.0001f && bc>=-.0001f && ca>=-.0001f || ab<=.0001f && bc<=.0001f && ca<=.0001f)
                        if(Mathf.Abs(Geo.Cross(b-a,c-a))>.0001f)return true;
                }
                return false;
            }
            for(float x=20.05f;x<24.7f;x+=.2f)for(float z=18.3f;z<24.75f;z+=.2f)
                Assert.True(Covered(ground,new Vector2(x,z)) || Covered(road,new Vector2(x,z)),"grass gap in driveway mouth at "+new Vector2(x,z));
            var lights=root.GetComponent<VisitorCentreVisual>();
            lights.SetLighting(0);Assert.True(lights.interiorLights.All(l=>!l.enabled));
            lights.SetLighting(1);Assert.AreEqual(2,lights.interiorLights.Count(l=>l.type==LightType.Spot && l.enabled && l.intensity>=59f));
        }
    }
}
