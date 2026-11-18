using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.EditorTools.World;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests.World
{
    public class BuiltSurfaceMapTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void Cleanup(){foreach(var o in owned)if(o!=null)Object.DestroyImmediate(o);owned.Clear();}
        T Own<T>(T item)where T:Object{owned.Add(item);return item;}
        static void Rectangle(BuiltSurfaceMap map,float x0,float x1,float z0,float z1,float height,bool foundation=true)
        {
            var a=new Vector3(x0,height,z0);var b=new Vector3(x1,height,z0);var c=new Vector3(x1,height,z1);var d=new Vector3(x0,height,z1);
            map.AddTriangle(new BuiltSurfaceMap.Triangle(a,b,c,foundation));map.AddTriangle(new BuiltSurfaceMap.Triangle(a,c,d,foundation));
        }
        ValleyWorld World()=>new ValleyWorld {heights=Enumerable.Repeat(100f,ValleyWorld.Resolution*ValleyWorld.Resolution).ToArray(),
            generatorVersion=6,landCover=Enumerable.Repeat((byte)10,ValleyWorld.Resolution*ValleyWorld.Resolution).ToArray()};
        Airport Airport()
        {
            var go=Own(new GameObject("Ownership airport"));go.SetActive(false);go.transform.position=Vector3.up*100f;
            var airport=go.AddComponent<Airport>();airport.groundSize=0;go.SetActive(true);return airport;
        }
        [Test]
        public void LocalConstructionRefreshKeepsDistantRoadsOutOfTheDirtyAreaAndPreservesThePalette()
        {
            var world=World();var airport=Airport();var net=airport.Taxiways;
            net.Connect(net.AddNode(new Vector3(-1500,0,-1200)),net.AddNode(new Vector3(-1000,0,-1200)),7,PavementSurface.Asphalt,false,RoadClass.Service,true);net.Rebuild();
            var view=Own(new GameObject("Local commit valley")).AddComponent<ValleyView>();view.Build(world,new PublicRoadNetwork(),false);view.SetBuiltAirport(airport);view.ConsumeBuiltSurfaceChange();
            int from=net.AddNode(new Vector3(-100,0,0));
            var state=new CareerState {world=world,wholeRegion=true,mode=GameMode.Sandbox,layout=new CareerLayout(),publicRoads=new PublicRoadNetwork()};
            Assert.IsTrue(AirsideTerrainPlan.TryCreate(state,airport,from,new Vector3(100,0,0),-1,null,null,7,PavementSurface.Asphalt,RoadClass.Service,out var plan,out var why),why);
            Assert.IsTrue(plan.TryCommit(state,out why),why);net.Rebuild();view.RefreshLand(plan.Bounds);view.QueueLevelSurface(plan.Bounds,false);
            Assert.AreEqual(plan.Bounds,view.ConsumeBuiltSurfaceChange());Assert.AreEqual(0,view.PendingSurfaceTiles);
            Assert.IsTrue(view.BuiltSurfaces.Contains(new Vector2(-1250,-1200)));Assert.IsNull(world.disturbance);
        }

        [Test]
        public void ReusedPreviewBuffersMoveTheirFootprintAndClearOldCellsWithoutChangingCapacity()
        {
            using var map=new BuiltSurfaceMap();
            void At(float x){map.AddTriangle(new BuiltSurfaceMap.Triangle(new Vector3(x,10,0),new Vector3(x,10,20),new Vector3(x+20,10,0)));map.Build();}
            At(0);var properties=new MaterialPropertyBlock();map.Bind(properties);
            var field=typeof(BuiltSurfaceMap).GetField("triangleBuffer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var buffer=field.GetValue(map);Assert.IsTrue(map.Contains(new Vector2(2,2)));
            map.ResetPreview();At(200);map.Bind(properties);
            Assert.AreSame(buffer,field.GetValue(map));Assert.IsFalse(map.Contains(new Vector2(2,2)));Assert.IsTrue(map.Contains(new Vector2(202,2)));
            map.ResetPreview();map.Build();map.Bind(properties);
            Assert.IsFalse(map.Contains(new Vector2(202,2)));Assert.AreEqual(0f,properties.GetFloat("_AirCitySurfaceEnabled"));
        }

        [Test]
        public void SpatialIndexMatchesExactTrianglesAtCellEdgesAndKeepsUnbuiltCornerLand()
        {
            using var map=new BuiltSurfaceMap();
            map.AddTriangle(new BuiltSurfaceMap.Triangle(new Vector3(-130,100,-15),new Vector3(200,120,83),new Vector3(180,120,100)));
            map.AddTriangle(new BuiltSurfaceMap.Triangle(new Vector3(0,100,0),new Vector3(32,100,0),new Vector3(0,100,32)));
            map.Build();Assert.IsTrue(map.Contains(new Vector2(8,8)));Assert.IsFalse(map.Contains(new Vector2(28,28)),"Bounding rectangles must not exclude unbuilt corner land.");
            Assert.IsTrue(map.Contains(new Vector2(32,0)));Assert.IsFalse(map.Contains(new Vector2(0,40)));Assert.IsTrue(map.Contains(new Vector2(0,40),9f));
            var props=new[]{new GroundCoverInstance{id=1,position=new Vector2(8,8),kind=GroundCoverKind.Grass,scale=1},
                new GroundCoverInstance{id=2,position=new Vector2(0,40),kind=GroundCoverKind.Log,scale=2},
                new GroundCoverInstance{id=3,position=new Vector2(80,-80),kind=GroundCoverKind.Shrub,scale=1}};
            CollectionAssert.AreEqual(new[]{3},map.Exclude(props).Select(p=>p.id));
        }
        [Test]
        public void GrassRunwayOwnsItsOpaqueCoreAndLeavesItsTransparentFringeOverNaturalGround()
        {
            var airport=Airport();var runway=airport.AddRunway(new Vector3(-300,0,0),new Vector3(300,0,0),PavementSurface.Grass,18f);runway.naturalEdges=true;runway.Rebuild();
            using var map=new BuiltSurfaceMap();map.AddMesh(runway.SurfaceMesh,airport.transform.localToWorldMatrix);map.Build();
            Assert.Greater(map.TriangleCount,0);Assert.IsTrue(map.Contains(Vector2.zero));Assert.IsTrue(map.Contains(new Vector2(0,8.9f)));
            Assert.IsFalse(map.Contains(new Vector2(0,10f)),"The translucent fringe must keep terrain beneath it.");
        }
        [Test]
        public void HangarFloorsExcludePlantsAndProtectTheirFoundationUntilDemolition()
        {
            var world=World();var airport=Airport();
            var door=new Vector2(-1200,-1200);var hangar=airport.AddHangar(door,30f);Assert.IsNotNull(hangar);
            var view=Own(new GameObject("Hangar ownership valley")).AddComponent<ValleyView>();
            view.Build(world,new PublicRoadNetwork(),false);var before=(float[])world.heights.Clone();
            view.SetBuiltAirport(airport);
            var centre=door-hangar.Axis*SmallHangar.Depth*.5f;
            var outside=door+hangar.Axis*20f;
            Assert.IsTrue(view.BuiltSurfaces.Contains(centre));Assert.IsFalse(view.BuiltSurfaces.Contains(outside));
            Assert.AreEqual(ValleyWorld.Resolution*ValleyWorld.Resolution,view.BuiltSurfaces.Formation.Length);
            var props=new[]{new GroundCoverInstance {id=1,kind=GroundCoverKind.Grass,position=centre,scale=1f},
                new GroundCoverInstance {id=2,kind=GroundCoverKind.Grass,position=outside,scale=1f}};
            CollectionAssert.AreEqual(new[]{2},view.BuiltSurfaces.Exclude(props).Select(p=>p.id));
            CollectionAssert.AreEqual(before,world.heights,"Registering a hangar floor must not rewrite source terrain.");
            airport.RemoveHangar(hangar);view.SetBuiltAirport(airport);
            Assert.IsFalse(view.BuiltSurfaces.Contains(centre),"A demolished floor must stop clipping terrain and excluding plants.");
        }
        [Test]
        public void PublicRoadCoverageIncludesGroundCurvesAndSidewalksButLeavesTerrainUnderElevatedDecks()
        {
            var graph=new PublicRoadNetwork();int a=graph.AddNode(new Vector3(-150,100,0)),b=graph.AddNode(new Vector3(0,100,0)),c=graph.AddNode(new Vector3(0,100,150));
            graph.Connect(a,b,PublicRoadStyle.Urban,RoadStructure.Ground,out _);graph.Connect(b,c,PublicRoadStyle.Urban,RoadStructure.Ground,out _);
            int d=graph.AddNode(new Vector3(200,120,-100)),e=graph.AddNode(new Vector3(200,120,100));graph.AddSegment(d,e,PublicRoadStyle.Urban,RoadStructure.Elevated);
            var geometry=new PublicRoadGeometry(graph,null,null,_=>100f);
            try
            {
                using var map=new BuiltSurfaceMap();foreach(var tri in geometry.groundTriangles)map.AddTriangle(tri);map.Build();
                Assert.IsTrue(map.Contains(new Vector2(-75,0)));Assert.IsTrue(map.Contains(new Vector2(-75,5f)),"Raised sidewalks are built ground too.");
                Assert.IsFalse(map.Contains(new Vector2(200,0)),"An overpass must keep its terrain and vegetation below it.");
                foreach(var link in geometry.links.Where(l=>l.structure==RoadStructure.Ground))Assert.IsTrue(map.Contains((link.a+link.b)*.5f));
            }
            finally{foreach(var mesh in new[]{geometry.pavement,geometry.shoulders,geometry.markings,geometry.structures,geometry.kerbMesh,geometry.medianGrass})Object.DestroyImmediate(mesh);}
        }
        [Test]
        public void OwnershipClearancePersistsTreeAndPropRemovalAndDoesNotRemoveNearbyUnbuiltNature()
        {
            var world=World();world.trees.Add(new ForestTree {position=Vector2.zero,height=12f});world.trees.Add(new ForestTree {position=new Vector2(0,40),height=12f});
            using var map=new BuiltSurfaceMap();Rectangle(map,-200,200,-4,4,100);map.Build();
            var rock=new GroundCoverInstance {id=1,position=new Vector2(0,6),kind=GroundCoverKind.Boulder,scale=1f};
            Assert.AreEqual(1,map.ClearVegetation(world,new[]{rock}));Assert.IsTrue(world.trees[0].removed);Assert.IsFalse(world.trees[1].removed);Assert.IsTrue(world.GroundPropRemoved(1));
            var loaded=JsonUtility.FromJson<ValleyWorld>(JsonUtility.ToJson(world));Assert.IsTrue(loaded.trees[0].removed);Assert.IsTrue(loaded.GroundPropRemoved(1));Assert.AreEqual(0,map.ClearVegetation(world,new[]{rock}));
        }
        [Test]
        public void ScatterAndEveryLaterTileRefreshKeepWholePlantModelsOffBuiltPavement()
        {
            var world=World();using var map=new BuiltSurfaceMap();Rectangle(map,-260,260,-3.5f,3.5f,100);map.Build();
            var area=new Rect(-256,-256,512,512);var before=GroundCoverScatter.Generate(world,area:area);Assert.Greater(before.Length,20);
            var after=GroundCoverScatter.Generate(world,area:area,surfaces:map);
            Assert.Less(after.Length,before.Length);Assert.IsFalse(after.Any(p=>map.Contains(p.position,GroundCoverScatter.FootprintRadius(p.kind,p.scale))));
            var root=Own(new GameObject("Ownership ground cover"));var details=root.AddComponent<GroundCoverRenderer>();details.library=AssetDatabase.LoadAssetAtPath<GroundCoverLibrary>(GroundCoverSetup.LibraryPath);
            world.landCover=Enumerable.Repeat((byte)50,world.heights.Length).ToArray();
            for(int z=236;z<276;z++)for(int x=236;x<276;x++)world.landCover[z*ValleyWorld.Resolution+x]=10;
            details.Rebuild(world);Assert.IsTrue(details.Instances.Any(p=>map.Contains(p.position,GroundCoverScatter.FootprintRadius(p.kind,p.scale))));
            details.RefreshBuiltSurfaces(world,map,area,world.Height);
            Assert.IsFalse(details.Instances.Any(p=>map.Contains(p.position,GroundCoverScatter.FootprintRadius(p.kind,p.scale))));
            for(int n=0;n<3;n++)foreach(int key in new[]{12*25+12,12*25+11,11*25+12,11*25+11})details.RefreshTile(world,key,world.Height,surfaces:map);
            Assert.IsFalse(details.Instances.Any(p=>map.Contains(p.position,GroundCoverScatter.FootprintRadius(p.kind,p.scale))),"Queued scattering must not grow tufts back through a road.");
        }
        [Test]
        public void LaterGradingCannotBuryOrUndercutCommittedGrassRunwayAndLocalRefreshMatchesFullRefresh()
        {
            var world=World();var airport=Airport();var runway=airport.AddRunway(new Vector3(-300,0,0),new Vector3(300,0,0),PavementSurface.Grass,18);runway.naturalEdges=true;runway.Rebuild();
            var root=Own(new GameObject("Ownership terrain"));var view=root.AddComponent<ValleyView>();view.Build(world,new PublicRoadNetwork(),false);view.SetBuiltAirport(airport);
            var map=view.BuiltSurfaces;var area=new Rect(-120,-120,240,240);
            for(int i=0;i<12;i++)
            {
                world.LevelBrush(Vector2.zero,100f,i%2==0?130f:80f,world.RegionBounds);var before=(float[])world.heights.Clone();
                view.RefreshLand(area);CollectionAssert.AreEqual(before,world.heights,"Presentation must not rewrite the saved source terrain.");
                Assert.AreSame(map,view.BuiltSurfaces,"Earthwork alone must not rebuild static pavement ownership.");
                float ground=view.DisplayHeight(Vector2.zero);Assert.That(ground,Is.InRange(99.93f,100.02f),"The original runway formation must survive raising and lowering neighbouring source terrain.");
            }
            float local=view.DisplayHeight(Vector2.zero);view.RefreshLand(false);Assert.AreEqual(local,view.DisplayHeight(Vector2.zero),.012f);
            Physics.SyncTransforms();Assert.IsTrue(view.Terrain.GetComponent<TerrainCollider>().Raycast(new Ray(new Vector3(0,500,0),Vector3.down),out var hit,1000));Assert.AreEqual(local,hit.point.y,.02f);
        }
        [Test]
        public void AllTerrainPassesUseTheSameOwnershipAndShaderRegenerationRetainsIt()
        {
            var shader=ValleyTerrainShaderSetup.Ensure();Assert.IsNotNull(shader);Assert.AreEqual("Hidden/AirCity/ValleyTerrain Base",shader.GetDependency("BaseMapShader").name);
            foreach(var path in new[]{"Assets/AirCity/Shaders/ValleyTerrain.shader","Assets/AirCity/Shaders/ValleyTerrainAdd.shader","Assets/AirCity/Shaders/ValleyTerrainBase.shader"})
            {
                var material=AssetDatabase.LoadAssetAtPath<Shader>(path);Assert.IsNotNull(material);
                Assert.IsFalse(ShaderUtil.GetShaderMessages(material).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),path);
            }
            string passes=System.IO.File.ReadAllText("Assets/AirCity/Shaders/ValleyTerrainPasses.hlsl");
            Assert.AreEqual(3,passes.Split(new[]{"AirCityClipBuiltSurface(IN.positionWS)"},StringSplitOptions.None).Length-1);
            StringAssert.Contains("AirCityClipBuiltSurface(IN.positionWS)",System.IO.File.ReadAllText("Assets/AirCity/Shaders/ValleyTerrainDepthNormalsPass.hlsl"));
        }
    }
}
