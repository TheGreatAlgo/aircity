using System;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.World;
using UnityEngine;
using UnityEditor;

namespace AirCity.EditorTools.Game
{
    public static partial class PavementOwnershipReview
    {
        public static void RunPerformanceFromCommandLine()=>Begin(true,true);
        [Serializable]sealed class PlacementTiming {public double meanMs,maxMs;}
        [Serializable]sealed class PlacementPerformance
        {
            public string unityVersion,utc;
            public int serviceRoads;
            public PlacementTiming roadMoving,roadStationary,runwayMoving,runwayStationary,streetMoving,streetStationary;
            public double roadQuoteMs,roadMeshAndMaskMs,runwayMeshMs,runwayQuoteMs;
        }
        static void BenchmarkPlacement()
        {
            game.State.world.trees.Clear();game.valley.RefreshForest(false);
            var camera=game.view.GetComponent<Camera>();camera.transform.position=new Vector3(0,600,-500);
            camera.transform.rotation=Quaternion.LookRotation(new Vector3(0,100,-150)-camera.transform.position);
            var net=game.Airport.Taxiways;var origin=game.Airport.transform.position;
            Vector3 Local(float x,float z)=>game.Airport.transform.InverseTransformPoint(new Vector3(x,game.valley.DisplayHeight(new Vector2(x,z)),z));
            for(int i=0;i<16;i++)
            {
                float z=-900+i*24;
                net.Connect(net.AddNode(Local(-800,z)),net.AddNode(Local(-500,z)),7,PavementSurface.Asphalt,false,RoadClass.Service,true);
            }
            int start=net.AddNode(Local(-300,-150)),end=net.AddNode(Local(0,-150));
            net.Connect(start,end,7,PavementSurface.Asphalt,false,RoadClass.Service,true);net.Rebuild();game.SharedConstructionChanged();
            tool.SetTool(DevTool.Road);tool.SetSurface(PavementSurface.Asphalt);
            typeof(DevAirfieldTool).GetField("chainNode",Instance).SetValue(tool,end);
            var road=typeof(DevAirfieldTool).GetMethod("TaxiwayTool",Instance);var line=typeof(DevAirfieldTool).GetMethod("LinePreview",Instance);
            object[] roadArgs={false,true,Vector3.zero,true};
            double roadQuote=0,roadMesh=0;int roadCount=0;
            void Road(int i)
            {
                float x=160+i*3;roadArgs[2]=new Vector3(x,game.valley.DisplayHeight(new Vector2(x,-50)),-50);
                var phase=System.Diagnostics.Stopwatch.StartNew();road.Invoke(tool,roadArgs);roadQuote+=phase.Elapsed.TotalMilliseconds;
                phase.Restart();line.Invoke(tool,null);roadMesh+=phase.Elapsed.TotalMilliseconds;roadCount++;
                Require(string.IsNullOrEmpty(tool.RoadProblem),tool.RoadProblem+" from="+net.Node(end).position+" to="+tool.AirsideEndpoint+" chain="+typeof(DevAirfieldTool).GetField("chainNode",Instance).GetValue(tool)+" airport="+tool.airport.name);
            }
            Road(0);Road(1);
            var result=new PlacementPerformance {unityVersion=Application.unityVersion,utc=DateTime.UtcNow.ToString("O"),serviceRoads=net.edges.Count};
            roadQuote=roadMesh=0;roadCount=0;
            result.roadMoving=TimePlacement(i=>Road(i+2),24);result.roadQuoteMs=roadQuote/roadCount;result.roadMeshAndMaskMs=roadMesh/roadCount;
            result.roadStationary=TimePlacement(_=>Road(25),100);
            tool.SetTool(DevTool.Runway);tool.SetSurface(PavementSurface.Grass);
            var runway=game.Airport.AddRunway(Local(-350,300),Local(-200,300),PavementSurface.Grass,18);
            runway.naturalEdges=true;runway.gameObject.hideFlags=HideFlags.DontSave;
            typeof(DevAirfieldTool).GetField("dragging",Instance).SetValue(tool,runway);
            typeof(DevAirfieldTool).GetField("dragStart",Instance).SetValue(tool,runway.start);
            var draw=typeof(DevAirfieldTool).GetMethod("RunwayTool",Instance);object[] runwayArgs={mouse,keyboard,false,true,Vector3.zero};
            void Runway(int i)
            {
                float x=150+i*3;runwayArgs[4]=new Vector3(x,game.valley.DisplayHeight(new Vector2(x,300)),300);
                draw.Invoke(tool,runwayArgs);Require(string.IsNullOrEmpty(tool.RunwayProblem),tool.RunwayProblem);
            }
            Runway(0);Runway(1);
            result.runwayMoving=TimePlacement(i=>Runway(i+2),24);result.runwayStationary=TimePlacement(_=>Runway(25),100);
            var quote=typeof(DevAirfieldTool).GetMethod("QuoteRunway",Instance);
            result.runwayMeshMs=TimePlacement(_=>runway.Rebuild(),24).meanMs;
            result.runwayQuoteMs=TimePlacement(_=>quote.Invoke(tool,null),24).meanMs;
            tool.SetTool(DevTool.Street);tool.SetStreetStyle(PublicRoadStyle.Avenue);
            var originalStreets=tool.Streets.network;var streets=originalStreets.Clone();
            var streetStart=new Vector3(300,game.valley.DisplayHeight(new Vector2(300,-500)),-500);
            int streetNode=streets.AddNode(streetStart);tool.Streets.network=streets;
            typeof(DevAirfieldTool).GetField("streetChain",Instance).SetValue(tool,streetNode);
            typeof(DevAirfieldTool).GetField("streetChainPoint",Instance).SetValue(tool,(Vector2?)new Vector2(300,-500));
            typeof(DevAirfieldTool).GetField("streetDraftBase",Instance).SetValue(tool,originalStreets);
            var street=typeof(DevAirfieldTool).GetMethod("StreetTool",Instance);object[] streetArgs={mouse,keyboard,false,true,Vector3.zero};
            void Street(int i)
            {
                float x=450+i*3;streetArgs[4]=new Vector3(x,game.valley.DisplayHeight(new Vector2(x,-350)),-350);
                street.Invoke(tool,streetArgs);Require(string.IsNullOrEmpty(tool.StreetProblem),tool.StreetProblem);
            }
            Street(0);Street(1);result.streetMoving=TimePlacement(i=>Street(i+2),24);result.streetStationary=TimePlacement(_=>Street(25),100);
            tool.SetTool(DevTool.Select);
            File.WriteAllText("Logs/placement-performance.json",JsonUtility.ToJson(result,true));
        }
        static PlacementTiming TimePlacement(Action<int> operation,int count)
        {
            double total=0,max=0;
            for(int i=0;i<count;i++)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();operation(i);watch.Stop();double ms=watch.Elapsed.TotalMilliseconds;
                total+=ms;max=Math.Max(max,ms);
            }
            return new PlacementTiming {meanMs=total/count,maxMs=max};
        }
    }
}
