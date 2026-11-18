using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Traffic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    public sealed class HangarConnectionTests
    {
        readonly List<Object> spawned=new List<Object>();
        [TearDown] public void Cleanup(){foreach(var o in spawned)if(o!=null)Object.DestroyImmediate(o);spawned.Clear();}

        (Airport airport,SmallHangar hangar,AircraftType type,TaxiwayEdge edge) Setup()
        {
            var go=new GameObject("Hangar connection airport");go.SetActive(false);spawned.Add(go);
            var airport=go.AddComponent<Airport>();airport.groundSize=0f;go.SetActive(true);
            var net=airport.Taxiways;
            int a=net.AddNode(new Vector3(-60,0,15)),b=net.AddNode(new Vector3(60,0,15));
            var edge=net.Connect(a,b,7.5f,PavementSurface.Asphalt,lit:true)[0];
            var hangar=airport.AddHangar(Vector2.zero,0f);
            var type=ScriptableObject.CreateInstance<AircraftType>();spawned.Add(type);
            type.category=AircraftCategory.GeneralAviation;type.bounds=new Bounds(new Vector3(.25f,1.4f,1f),new Vector3(11f,2.8f,8f));
            type.steeringPivot=new Vector3(0f,0f,1.6f);
            return (airport,hangar,type,edge);
        }

        [Test] public void ConnectionIsCentredOnDoorAxisAndProducesAConcreteThroat()
        {
            var (airport,h,type,edge)=Setup();
            Assert.True(HangarConnection.TryGet(h,type,out var layout,out var why),why);
            Assert.AreSame(edge,layout.edge);Assert.AreEqual(new Vector2(0f,15f),layout.entry);
            Assert.AreEqual(-h.Axis,layout.nose);
            var expected=new Vector2(.25f,-7f)-h.Axis*type.Wheelbase;
            Assert.Less(Vector2.Distance(expected,layout.stop),.001f);
            Assert.True(HangarConnection.Approach(h,out var outline));
            Assert.AreEqual(4,outline.Length);Assert.AreEqual(-7f,outline[0].x,.001f);Assert.AreEqual(7f,outline[1].x,.001f);
            Assert.AreEqual(11.3f,outline[2].y,.001f);
            Assert.AreEqual(SmallHangar.FloorHeight,HangarConnection.SurfaceHeight(h,new Vector2(0,-1)),.001f);
            Assert.AreEqual(AirfieldHeights.Stand,HangarConnection.SurfaceHeight(h,new Vector2(0,11.3f)),.001f);
            Assert.NotNull(h.transform.Find("Generated hangar approach"));
        }

        [Test] public void ServiceRoadBackOffsetEndpointRaisedAndClosedLinksAreNotConnections()
        {
            var (airport,h,type,edge)=Setup();var net=airport.Taxiways;
            edge.road=RoadClass.Service;Assert.False(HangarConnection.TryGet(h,type,out _,out _));
            edge.road=RoadClass.None;edge.wear=RunwayRules.ClosedWear;
            Assert.False(HangarConnection.TryGet(h,type,out _,out _));
            Assert.True(HangarConnection.Approach(h,out _),"A weather/maintenance closure must not remove the constructed approach.");
            edge.wear=0f;net.Node(edge.a).position+=Vector3.up;
            Assert.False(HangarConnection.TryGet(h,type,out _,out _));
            net.Node(edge.a).position-=Vector3.up;
            h.transform.localRotation=Quaternion.Euler(0,180,0);
            Assert.False(HangarConnection.TryGet(h,type,out _,out _));
            h.transform.localRotation=Quaternion.identity;
            net.Node(edge.a).position=new Vector3(2f,0,15);
            Assert.False(HangarConnection.TryGet(h,type,out _,out _),"A nearest endpoint beside the axis is not an aligned doorway connection.");
        }

        [Test] public void AircraftBoundsMustClearDoorCheeksAndSideAndRearWalls()
        {
            var (_,h,type,_)=Setup();
            Assert.True(HangarConnection.PoseFits(h,type,new Vector2(.25f,-7f),Vector2.down));
            Assert.False(HangarConnection.PoseFits(h,type,new Vector2(4f,-7f),Vector2.down));
            Assert.False(HangarConnection.PoseFits(h,type,new Vector2(.25f,-14f),Vector2.down));
            Assert.False(HangarConnection.PoseFits(h,type,new Vector2(2f,0f),Vector2.down));
            Assert.True(HangarConnection.PoseFits(h,type,new Vector2(.25f,10f),Vector2.right),"Turns wholly outside remain possible.");
            var b=new TaxiPath.Builder(new Vector2(.25f,15f),Vector2.down);
            b.LineTo(new Vector2(.25f,-8.6f));
            var path=b.Build(new Vector2(.25f,16.6f),1.6f,.8f);
            Assert.True(HangarConnection.ValidateMovement(h,type,path,out var why),why);
        }
    }
}
