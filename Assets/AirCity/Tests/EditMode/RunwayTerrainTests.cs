using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Game;
using AirCity.Traffic;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public class RunwayTerrainTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void Cleanup(){foreach(var o in owned)if(o!=null)Object.DestroyImmediate(o);owned.Clear();}
        CareerState State(Func<float,float,float> height)
        {
            var world=new ValleyWorld {heights=new float[ValleyWorld.Resolution*ValleyWorld.Resolution]};
            world.sites.Add(new AirportSite {name="Slope fixture",centre=Vector2.zero,elevation=80f});
            for(int z=0;z<ValleyWorld.Resolution;z++)for(int x=0;x<ValleyWorld.Resolution;x++)
                world.heights[z*ValleyWorld.Resolution+x]=height(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f);
            return new CareerState {world=world,selectedSite=0,handsOn=true,wholeRegion=true,funds=2000000,
                layout=new CareerLayout(),publicRoads=new PublicRoadNetwork()};
        }
        Airport Airport(bool active=false)
        {
            var root=new GameObject("Runway grade fixture");root.SetActive(false);owned.Add(root);
            root.transform.position=new Vector3(0f,80f,0f);var airport=root.AddComponent<Airport>();airport.groundSize=0f;
            root.SetActive(active);return airport;
        }
        static Runway Draft(Airport airport)=>airport.AddRunway(new Vector3(-300,0,0),new Vector3(300,0,0),PavementSurface.Asphalt,18f);

        [Test]
        public void MovingARunwayGhostKeepsTheCommittedAirportCachesValid()
        {
            var airport=Airport(true);var r=Draft(airport);r.gameObject.hideFlags=HideFlags.DontSave;
            int version=airport.LayoutVersion;
            for(int i=0;i<4;i++){r.end+=new Vector3(3,0,0);r.Rebuild();Assert.AreEqual(version,airport.LayoutVersion);}
            r.gameObject.hideFlags=HideFlags.None;r.Rebuild();Assert.Greater(airport.LayoutVersion,version);
        }

        [Test]
        public void SmoothHillKeepsItsInclineWithoutTerrainWorkOrPreviewMutation()
        {
            var state=State((x,z)=>80f+x*.1f);var airport=Airport();var r=Draft(airport);
            var before=(float[])state.world.heights.Clone();int funds=state.funds;
            Assert.IsTrue(RunwayTerrainPlan.TryCreate(state,airport,r,null,out var plan,out string why),why);
            Assert.AreEqual(-30f,plan.Start.y,.001f);Assert.AreEqual(30f,plan.End.y,.001f);
            Assert.AreEqual(.1f,plan.Grade,.00001f);Assert.AreEqual(0,plan.GradingPrice);Assert.AreEqual(0,plan.ChangedSamples);
            CollectionAssert.AreEqual(before,state.world.heights);Assert.AreEqual(funds,state.funds);Assert.IsNull(state.world.disturbance);
            Assert.IsTrue(plan.TryCommit(state,12000,out int price,out why),why);Assert.AreEqual(12000,price);
            Assert.AreEqual(funds-price,state.funds);CollectionAssert.AreEqual(before,state.world.heights);Assert.IsNull(state.world.disturbance);
            Assert.IsFalse(plan.TryCommit(state,12000,out _,out _),"One quote cannot charge twice, even when no grading was needed.");
        }

        [Test]
        public void CrookedCrossfallIsGradedOnceAndPaidFromActualCutAndFill()
        {
            var state=State((x,z)=>80f+x*.08f+Mathf.Sin(x*.01f)*3f+z*.15f);
            var airport=Airport();var r=Draft(airport);var before=(float[])state.world.heights.Clone();
            Assert.IsTrue(RunwayTerrainPlan.TryCreate(state,airport,r,null,out var plan,out string why),why);
            Assert.Greater(plan.Earthwork,1000f);Assert.Greater(plan.GradingPrice,0);Assert.AreEqual(2000000,state.funds);
            Assert.IsTrue(plan.TryCommit(state,12000,out int price,out why),why);
            double actual=0;for(int i=0;i<before.Length;i++)actual+=Math.Abs(state.world.heights[i]-before[i])*state.world.Spacing*state.world.Spacing;
            Assert.AreEqual(plan.Earthwork,actual,Math.Max(2f,plan.Earthwork*.00002f));
            Assert.AreEqual(12000+Mathf.CeilToInt(plan.Earthwork*.055f),price);Assert.AreEqual(2000000-price,state.funds);
            r.start=plan.Start;r.end=plan.End;
            foreach(float x in new[]{-250f,-100f,0f,125f,275f})foreach(float z in new[]{-9f,0f,9f})
                Assert.AreEqual(80f+r.ElevationAt(new Vector2(x,z)),state.world.Height(new Vector2(x,z)),.002f);
            int outside=336*ValleyWorld.Resolution+336;Assert.AreEqual(before[outside],state.world.heights[outside]);
        }

        [Test]
        public void UnaffordableAndStaleQuotesNeverMutateTerrainOrSpendFunds()
        {
            var state=State((x,z)=>80f+x*.05f+z*.1f);var airport=Airport();var r=Draft(airport);
            Assert.IsTrue(RunwayTerrainPlan.TryCreate(state,airport,r,null,out var plan,out string why),why);
            var before=(float[])state.world.heights.Clone();state.funds=1;
            Assert.IsFalse(plan.TryCommit(state,12000,out _,out _));Assert.AreEqual(1,state.funds);CollectionAssert.AreEqual(before,state.world.heights);
            state.funds=2000000;int at=256*ValleyWorld.Resolution+256;state.world.heights[at]+=1f;
            var changed=(float[])state.world.heights.Clone();Assert.IsFalse(plan.TryCommit(state,12000,out _,out _));
            Assert.AreEqual(2000000,state.funds);CollectionAssert.AreEqual(changed,state.world.heights);Assert.IsNull(state.world.disturbance);
            var flat=State((x,z)=>80f);Assert.IsTrue(RunwayTerrainPlan.TryCreate(flat,airport,r,null,out var flatPlan,out why),why);
            flat.world.heights[at]+=1f;Assert.IsFalse(flatPlan.TryCommit(flat,12000,out _,out _),"A formerly flat quote must notice newly uneven ground too.");
        }

        [Test]
        public void ARunwayCannotCrossTheEngineeredRoadbedEvenWhenItsNaturalTerrainAlreadyFits()
        {
            var state=State((x,z)=>80f);var airport=Airport();var r=Draft(airport);
            int a=state.publicRoads.AddNode(new Vector3(-500,83,45)),b=state.publicRoads.AddNode(new Vector3(500,83,45));
            state.publicRoads.AddSegment(a,b,PublicRoadStyle.Rural,RoadStructure.Ground);
            Assert.IsFalse(RunwayTerrainPlan.TryCreate(state,airport,r,null,out _,out var why));StringAssert.Contains("road",why);
        }

        [Test]
        public void RunwayRequiresWholeRockAndLogFootprintsToBeClearedBeforeGrading()
        {
            var state=State((x,z)=>80f);var airport=Airport();var r=Draft(airport);
            var rock=new GroundCoverInstance {id=1,kind=GroundCoverKind.Boulder,position=new Vector2(0,11),scale=1f};
            Assert.IsFalse(RunwayTerrainPlan.TryCreate(state,airport,r,null,out _,out var why,new[]{rock}));StringAssert.Contains("rocks",why);
            state.world.RemoveGroundProps(new[]{rock.id});
            Assert.IsTrue(RunwayTerrainPlan.TryCreate(state,airport,r,null,out _,out why,new[]{rock}),why);
            var log=new GroundCoverInstance {id=2,kind=GroundCoverKind.Log,position=new Vector2(0,13),scale=1.3f};
            Assert.IsFalse(RunwayTerrainPlan.TryCreate(state,airport,r,null,out _,out _,new[]{log}));
        }

        [Test]
        public void FirstRunwayAnchorsAnEmptyAirportAtItsUpperPlateauWithoutMovingTheWorldProfile()
        {
            var state=State((x,z)=>x>=250f?110f:80f+x*.12f);var airport=Airport(true);var r=Draft(airport);
            Assert.IsTrue(RunwayTerrainPlan.TryCreate(state,airport,r,null,out var plan,out string why),why);
            var start=airport.transform.TransformPoint(plan.Start);var end=airport.transform.TransformPoint(plan.End);
            Assert.IsTrue(plan.TryCommit(state,12000,out _,out why),why);plan.PlaceProfile(airport,r,true);airport.RunwaysChanged();
            Assert.AreEqual(110f,airport.transform.position.y,.001f);Assert.AreEqual(0f,r.end.y,.001f);
            Assert.AreEqual(start,airport.transform.TransformPoint(r.start));Assert.AreEqual(end,airport.transform.TransformPoint(r.end));
            var net=airport.Taxiways;int a=net.AddNode(r.end,r),b=net.AddNode(r.end+new Vector3(0,0,-100));net.Connect(a,b,10,PavementSurface.Asphalt);net.Rebuild();
            Assert.AreEqual(0f,net.Node(a).position.y,.001f);Assert.AreEqual(0f,airport.PavementElevationAt(new Vector2(300,-50)),.001f);
            var host=new GameObject("Upper plateau footprint context");host.SetActive(false);owned.Add(host);var game=host.AddComponent<CareerGame>();
            typeof(CareerGame).GetProperty("State").SetValue(game,state);typeof(CareerGame).GetProperty("Airport").SetValue(game,airport);
            Assert.IsTrue(game.CheckFootprint(CareerConstruction.Strip(new Vector2(300,0),new Vector2(300,-100),5f),0f,out why),why);
            var save=AirportSaveData.Capture(airport,GameAssetCatalog.Load());var loaded=save.Restore(null,GameAssetCatalog.Load());owned.Add(loaded.gameObject);
            Assert.AreEqual(110f,loaded.transform.position.y,.001f);Assert.AreEqual(end,loaded.transform.TransformPoint(loaded.Runways[0].end));
        }

        [Test]
        public void SlopeMovesPavementPaintLightsAndTaxiwayJoinsAndSurvivesPortableSave()
        {
            var airport=Airport(true);var r=Draft(airport);r.start.y=-30f;r.end.y=30f;r.naturalEdges=true;r.lighting=RunwayLighting.Basic;r.Rebuild();
            float Expected(Vector3 p)=>r.ElevationAt(new Vector2(p.x,p.z),false);
            foreach(var p in r.SurfaceMesh.vertices)Assert.Less(Mathf.Abs(p.y-Expected(p)),.1f);
            foreach(var p in r.PaintMesh.vertices)Assert.Less(Mathf.Abs(p.y-Expected(p)),.1f);
            foreach(var p in r.GlowMesh.vertices)Assert.That(p.y-Expected(p),Is.InRange(.04f,1.1f));
            Assert.AreEqual(0,r.VergeMesh.vertexCount);Assert.IsFalse(r.HasVerges);Assert.IsFalse(airport.Maintenance.MowVerges(r));
            var net=airport.Taxiways;int a=net.AddNode(new Vector3(100,0,0),r),b=net.AddNode(new Vector3(100,0,-100));net.Connect(a,b,10,PavementSurface.Asphalt);net.Rebuild();
            Assert.AreEqual(10f,net.Node(a).position.y,.001f);Assert.AreEqual(2.5f,airport.PavementElevationAt(new Vector2(100,-75)),.001f);
            Assert.AreEqual(33f,airport.PavementElevationAt(new Vector2(330,0)),.001f,"Service passes beyond the threshold stay on the same grade.");
            var data=AirportSaveData.Capture(airport,GameAssetCatalog.Load());
            var loaded=JsonUtility.FromJson<AirportSaveData>(JsonUtility.ToJson(data)).Restore(null,GameAssetCatalog.Load());owned.Add(loaded.gameObject);
            Assert.AreEqual(r.start,loaded.Runways[0].start);Assert.AreEqual(r.end,loaded.Runways[0].end);Assert.IsTrue(loaded.Runways[0].naturalEdges);
            Assert.AreEqual(10f,loaded.PavementElevationAt(new Vector2(100,0)),.001f);
        }

        [Test]
        public void GroundAircraftPitchFollowsGradeAndDownhillArrivalStaysAboveTheHighThreshold()
        {
            var airport=Airport(true);var runway=airport.AddRunway(Vector3.zero,new Vector3(1000,100,0),PavementSurface.Asphalt,18f);
            var aircraftObject=new GameObject("Slope aircraft");aircraftObject.SetActive(false);owned.Add(aircraftObject);
            var aircraft=aircraftObject.AddComponent<AirCity.Aircraft.Aircraft>();var type=ScriptableObject.CreateInstance<AircraftType>();owned.Add(type);aircraft.type=type;
            var route=new TaxiRoute {runway=runway,fromStart=false,touchdownS=200f};
            Flight Create(FlightKind kind)=>(Flight)Activator.CreateInstance(typeof(Flight),BindingFlags.Instance|BindingFlags.NonPublic,null,
                new object[]{new AirTraffic(airport),kind,aircraft,route},null);
            var place=typeof(Flight).GetMethod("Place",BindingFlags.Instance|BindingFlags.NonPublic);
            var flight=Create(FlightKind.Arrival);place.Invoke(flight,new object[]{new Vector2(1000,0),Vector2.left,8f});
            Assert.GreaterOrEqual(airport.transform.InverseTransformPoint(aircraft.transform.position).y,108f-.001f);
            flight=Create(FlightKind.Departure);typeof(Flight).GetProperty("Phase").SetValue(flight,FlightPhase.TakeoffRoll);
            place.Invoke(flight,new object[]{new Vector2(400,0),Vector2.right,0f});var grounded=aircraft.transform.rotation;
            Assert.AreEqual(40f,airport.transform.InverseTransformPoint(aircraft.transform.position).y,.001f);
            Assert.Less(Vector3.Angle(aircraft.transform.forward,new Vector3(1,.1f,0)),.01f);
            typeof(Flight).GetProperty("Phase").SetValue(flight,FlightPhase.Climb);
            place.Invoke(flight,new object[]{new Vector2(400,0),Vector2.right,.01f});
            Assert.Less(Quaternion.Angle(grounded,aircraft.transform.rotation),.05f,"Liftoff blends the runway slope out without an abrupt pitch change.");
        }

        [Test]
        public void AnExtensionPreviewKeepsTheOriginalGroundSupportUntilItIsCommittedOrCancelled()
        {
            var airport=Airport(true);var original=airport.AddRunway(Vector3.zero,new Vector3(1000,100,0),PavementSurface.Asphalt,18f);
            Assert.AreEqual(50f,airport.PavementElevationAt(new Vector2(500,0)),.001f);
            var tag=typeof(Runway).GetField("keepElevationWhileHidden",BindingFlags.Instance|BindingFlags.NonPublic);
            tag.SetValue(original,true);original.gameObject.SetActive(false);airport.LayoutChanged();
            var ghost=airport.AddRunway(Vector3.zero,new Vector3(1100,220,0),PavementSurface.Asphalt,18f);
            ghost.gameObject.hideFlags=HideFlags.DontSave;airport.LayoutChanged();
            Assert.AreEqual(50f,airport.PavementElevationAt(new Vector2(500,0)),.001f,"Actors use the existing runway, never the movable ghost.");
            airport.RemoveRunway(ghost);tag.SetValue(original,false);original.gameObject.SetActive(true);airport.RunwaysChanged();
            Assert.AreEqual(50f,airport.PavementElevationAt(new Vector2(500,0)),.001f);
            airport.RemoveRunway(original);Assert.AreEqual(0f,airport.PavementElevationAt(new Vector2(500,0)),.001f);
        }
    }
}
