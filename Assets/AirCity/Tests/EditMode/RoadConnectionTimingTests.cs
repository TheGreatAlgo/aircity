using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using F = AirCity.EditorTools.Game.RoadConnectionFixture;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    /// <summary>
    /// Cursor and quote cost on the connection paths, written to Logs/road-connection-timing.txt (no limits asserted:
    /// the numbers are compared between versions). The street graph gets a 20 × 20 grid of ground streets and the
    /// airside one a 12 × 12 grid of service roads, and the snap is measured while drawing a raised road over them,
    /// the case where every ground road is a candidate at another level.
    /// </summary>
    public sealed class RoadConnectionTimingTests
    {
        const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly List<GameObject> spawned=new List<GameObject>();
        [TearDown]public void Cleanup(){foreach(var go in spawned)if(go!=null)Object.DestroyImmediate(go);spawned.Clear();}
        static double Time(System.Action action,int repeat)
        {
            action();var watch=Stopwatch.StartNew();for(int i=0;i<repeat;i++)action();return watch.Elapsed.TotalMilliseconds/repeat;
        }
        [Test]public void SnapAndQuoteTimings()
        {
            var game=F.Create(spawned);var report=new System.Text.StringBuilder();
            using var drive=new RoadToolDriver(game);var tool=drive.tool;
            var roads=tool.Streets.network;
            for(int i=0;i<=20;i++)for(int j=0;j<20;j++)
            {
                var a=new Vector3(-1000+i*40,0,-2400+j*40);var b=a+new Vector3(0,0,40);a.y=F.Land(a.x,a.z);b.y=F.Land(b.x,b.z);
                roads.AddSegment(roads.AddNode(a),roads.AddNode(b),PublicRoadStyle.Urban,RoadStructure.Ground);
                var c=new Vector3(-1000+j*40,0,-2400+i*40);var d=c+new Vector3(40,0,0);c.y=F.Land(c.x,c.z);d.y=F.Land(d.x,d.z);
                roads.AddSegment(roads.AddNode(c),roads.AddNode(d),PublicRoadStyle.Urban,RoadStructure.Ground);
            }
            report.AppendLine($"Street graph: {roads.nodes.Count} nodes, {roads.segments.Count} sections");
            tool.SetTool(DevTool.Street);tool.SetRoadMode(RoadBuildMode.Bridge);
            drive.ClickFree(new Vector2(-1060,-2000));
            var snap=typeof(DevAirfieldTool).GetMethod("SnapStreet",Any);
            var points=Enumerable.Range(0,50).Select(k=>new Vector2(-1000+k*15.3f,-2380+k*14.1f)).ToArray();
            double street=Time(()=>{foreach(var p in points)snap.Invoke(tool,new object[]{roads,p,false});},4)/points.Length;
            report.AppendLine($"SnapStreet while drawing a bridge over the grid: {street:0.000} ms per call");
            drive.EndChain();

            var net=game.Airport.Taxiways;
            for(int i=0;i<=12;i++)for(int j=0;j<12;j++)
            {
                net.Connect(net.AddNode(new Vector3(-300+i*50,0,350+j*50)),net.AddNode(new Vector3(-300+i*50,0,400+j*50)),RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
                net.Connect(net.AddNode(new Vector3(-300+j*50,0,350+i*50)),net.AddNode(new Vector3(-250+j*50,0,350+i*50)),RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            }
            report.AppendLine($"Airside graph: {net.nodes.Count} nodes, {net.edges.Count} edges");
            tool.SetTool(DevTool.Road);tool.SetRoadMode(RoadBuildMode.Bridge);
            var origin=game.Airport.transform.position;
            drive.ClickFree(new Vector2(-330+origin.x,600+origin.z));
            var onto=typeof(DevAirfieldTool).GetMethod("Onto",Any);
            var airside=Enumerable.Range(0,50).Select(k=>new Vector2(-300+k*12.1f,355+k*11.3f)).ToArray();
            double road=Time(()=>{foreach(var p in airside)onto.Invoke(tool,new object[]{net,p,6f,null,true});},4)/airside.Length;
            report.AppendLine($"Onto (Road tool) while drawing a bridge over the grid: {road:0.000} ms per call");
            drive.EndChain();

            var state=game.State;var graph=F.Roads();int from=graph.nodes.First(n=>Vector2.Distance(PublicRoadNetwork.XZ(n.position),F.B1B)<.1f).id;
            var settings=new RoadBuildSettings {mode=RoadBuildMode.Bridge,smooth=true};
            double quote=Time(()=>RoadConstructionPlan.TryCreate(state,graph,from,new Vector3(-1100,F.BridgeLevel,-1700),-1,-1,PublicRoadStyle.Urban,settings,out _,out _),20);
            report.AppendLine($"RoadConstructionPlan.TryCreate, 400 m bridge: {quote:0.00} ms");
            settings.mode=RoadBuildMode.Tunnel;int t=graph.nodes.First(n=>Vector2.Distance(PublicRoadNetwork.XZ(n.position),F.T3B)<.1f).id;
            double tunnel=Time(()=>RoadConstructionPlan.TryCreate(state,graph,t,new Vector3(-960,F.RidgeTunnelLevel,-500),-1,-1,PublicRoadStyle.Urban,settings,out _,out _),20);
            report.AppendLine($"RoadConstructionPlan.TryCreate, 300 m tunnel out over the valley: {tunnel:0.00} ms");
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/road-connection-timing.txt",report.ToString());
            UnityEngine.Debug.Log(report.ToString());
        }
    }
}
