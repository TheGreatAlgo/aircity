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
    public sealed class VisitorCentreConstructionTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown] public void Cleanup()
        {foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();}

        static CareerState State(GameMode mode=GameMode.Career)
        {
            var world=new ValleyWorld {heights=Enumerable.Repeat(100f,ValleyWorld.Resolution*ValleyWorld.Resolution).ToArray()};
            world.sites.Add(new AirportSite {name="Visitor centre construction fixture",centre=Vector2.zero,elevation=100f});
            var graph=new PublicRoadNetwork();
            int left=graph.AddNode(new Vector3(-150f,100f,46f));graph.Node(left).outsideConnection=true;
            int entrance=graph.AddNode(new Vector3(80f,100f,46f),"airport-reception");
            int right=graph.AddNode(new Vector3(150f,100f,46f));graph.Node(right).outsideConnection=true;
            graph.AddSegment(left,entrance,PublicRoadStyle.AirportAccess,RoadStructure.Ground);
            graph.AddSegment(entrance,right,PublicRoadStyle.AirportAccess,RoadStructure.Ground);
            var layout=new CareerLayout {unlocked=255,elevation=100f,runwayBuilt=true,standBuilt=true,
                runwayA=new Vector2(-400f,-300f),runwayB=new Vector2(400f,-300f),stand=new Vector2(0f,-200f),standAlong=Vector2.right};
            layout.taxiways.Add(new CareerTaxiway {points=new List<Vector2> {new Vector2(-100f,-200f),new Vector2(100f,-200f)}});
            return new CareerState {world=world,selectedSite=0,handsOn=true,wholeRegion=true,mode=mode,funds=1000000,
                layout=layout,publicRoads=graph,accessRoadRoute=new List<Vector2> {new Vector2(80f,46f),new Vector2(150f,46f)}};
        }

        Airport Airport()
        {
            var root=new GameObject("Visitor centre construction airport");root.SetActive(false);owned.Add(root);
            root.transform.position=Vector3.up*100f;
            var airport=root.AddComponent<Airport>();airport.groundSize=0f;root.SetActive(true);return airport;
        }

        [TestCase(GameMode.Career)] [TestCase(GameMode.Sandbox)]
        public void PreviewAndPurchaseUseOneWholeSiteQuoteAndConnectTheSavedPublicGraph(GameMode mode)
        {
            var state=State(mode);Assert.IsTrue(state.HasAccess);
            var source=state.publicRoads;string roadsBefore=JsonUtility.ToJson(source),layoutBefore=JsonUtility.ToJson(state.layout);
            var heights=(float[])state.world.heights.Clone();var authored=source.nodes.Select(n=>(n.id,n.position,n.terminalEntrance,n.outsideConnection)).ToArray();
            VisitorCentreConstruction preview=null;
            Assert.IsTrue(CareerConstruction.Reception(state,Vector2.zero,0f,false,out var why,preview:p=>preview=p),why);
            Assert.IsNotNull(preview);Assert.Greater(preview.Road.Price,0);
            Assert.AreEqual(CareerConstruction.ReceptionPrice+preview.Road.Price,preview.Price);
            Assert.AreEqual(Mathf.CeilToInt(preview.Road.Length*RoadTypes.Of(PublicRoadStyle.AirportAccess).pricePerMetre),preview.Road.Price);
            Assert.AreEqual(roadsBefore,JsonUtility.ToJson(source));Assert.AreEqual(layoutBefore,JsonUtility.ToJson(state.layout));
            Assert.AreEqual(1000000,state.funds);CollectionAssert.AreEqual(heights,state.world.heights);

            Assert.IsTrue(CareerConstruction.Reception(state,Vector2.zero,0f,true,out why),why);
            Assert.IsTrue(state.layout.receptionBuilt);Assert.AreEqual(VisitorCentreConstruction.SiteVersion,state.layout.receptionSiteVersion);
            Assert.AreEqual(Vector2.zero,state.layout.reception);Assert.AreEqual(0f,state.layout.receptionHeading);
            Assert.AreEqual(mode==GameMode.Career ? 1000000-preview.Price : 1000000,state.funds);
            Assert.AreEqual(roadsBefore,JsonUtility.ToJson(source),"the original graph remains a detached source");
            CollectionAssert.AreEqual(heights,state.world.heights,"building never silently grades the prepared site");
            var graph=state.publicRoads;
            Assert.IsTrue(graph.Validate(out why),why);
            var port=graph.nodes.Single(n=>n.terminalEntrance==VisitorCentreLayout.EntranceId);
            var existing=graph.nodes.Single(n=>n.terminalEntrance=="airport-reception");
            Assert.IsTrue(graph.HasRoute(port.id,existing.id));Assert.IsTrue(graph.HasRoute(existing.id,port.id));
            foreach(var node in authored)
            {
                var retained=graph.Node(node.id);Assert.IsNotNull(retained);
                Assert.AreEqual(node.position,retained.position);Assert.AreEqual(node.terminalEntrance,retained.terminalEntrance);
                Assert.AreEqual(node.outsideConnection,retained.outsideConnection);
            }
            foreach(var outside in graph.nodes.Where(n=>n.outsideConnection))
            {Assert.IsTrue(graph.HasRoute(port.id,outside.id));Assert.IsTrue(graph.HasRoute(outside.id,port.id));}

            var snapshot=CareerSnapshot.Capture(state);
            Assert.AreNotSame(state.publicRoads,snapshot.publicRoads);Assert.AreNotSame(state.layout,snapshot.layout);
            Assert.AreEqual(VisitorCentreConstruction.SiteVersion,snapshot.layout.receptionSiteVersion,"background autosaves retain the parking package version");
            var restored=JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(snapshot));
            Assert.AreEqual(VisitorCentreConstruction.SiteVersion,restored.layout.receptionSiteVersion);
            var savedPort=restored.publicRoads.nodes.Single(n=>n.terminalEntrance==VisitorCentreLayout.EntranceId);
            var savedOutside=restored.publicRoads.nodes.First(n=>n.outsideConnection);
            Assert.IsTrue(restored.publicRoads.HasRoute(savedPort.id,savedOutside.id));
            Assert.IsTrue(restored.publicRoads.HasRoute(savedOutside.id,savedPort.id));
            int balance=state.funds;string committed=JsonUtility.ToJson(state.publicRoads);
            Assert.IsFalse(CareerConstruction.Reception(state,Vector2.zero,0f,true,out _));
            Assert.AreEqual(balance,state.funds);Assert.AreEqual(committed,JsonUtility.ToJson(state.publicRoads));
        }

        [TestCase("terrain")] [TestCase("water")] [TestCase("hangar")]
        [TestCase("funds")] [TestCase("public road")]
        public void InvalidSiteNeverChangesTheAirportRoadGraphFundsOrTerrain(string obstruction)
        {
            var state=State();Airport airport=null;
            switch(obstruction)
            {
                case "terrain":
                    for(int i=0;i<state.world.heights.Length;i++)state.world.heights[i]=101f;
                    break;
                case "water":
                    state.world.generatorVersion=4;
                    state.world.waterHeights=Enumerable.Repeat(101f,state.world.heights.Length).ToArray();
                    Assert.IsFalse(state.world.HasOcean);Assert.IsTrue(state.world.IsWater(new Vector2(0f,27f)));
                    break;
                case "hangar":
                    airport=Airport();Assert.IsNotNull(airport.AddHangar(new Vector2(0f,27f),0f));
                    break;
                case "funds":
                    state.funds=CareerConstruction.ReceptionPrice;
                    break;
                case "public road":
                    int a=state.publicRoads.AddNode(new Vector3(-100f,100f,28f)),b=state.publicRoads.AddNode(new Vector3(100f,100f,28f));
                    state.publicRoads.AddSegment(a,b,PublicRoadStyle.Rural,RoadStructure.Ground);
                    break;
            }
            string roads=JsonUtility.ToJson(state.publicRoads),layout=JsonUtility.ToJson(state.layout);
            var heights=(float[])state.world.heights.Clone();int money=state.funds,version=airport!=null ? airport.LayoutVersion : 0;
            int quoted=0;
            Assert.IsFalse(CareerConstruction.Reception(state,Vector2.zero,0f,true,out var why,airport,p=>quoted=p.Price));
            Assert.IsNotNull(why);
            Assert.AreEqual(obstruction=="funds",quoted>0,"An unaffordable but valid site still exposes the complete quote.");
            if(obstruction=="funds")Assert.Greater(quoted,money);
            Assert.AreEqual(roads,JsonUtility.ToJson(state.publicRoads));Assert.AreEqual(layout,JsonUtility.ToJson(state.layout));
            Assert.AreEqual(money,state.funds);CollectionAssert.AreEqual(heights,state.world.heights);
            if(airport!=null){Assert.AreEqual(version,airport.LayoutVersion);Assert.AreEqual(1,airport.Hangars.Count());}
        }

        [Test]
        public void VisitorSiteCannotCoverAnApproachWhenTheHangarAndTaxiwayAreOutsideTheSite()
        {
            var state = State();
            var airport = Airport();
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(22f, 0f, 6f)), b = net.AddNode(new Vector3(75f, 0f, 6f));
            net.Connect(a, b, 7.5f, PavementSurface.Asphalt);
            var hangar = airport.AddHangar(new Vector2(26.5f, -9f), 0f);
            Assert.IsTrue(HangarConnection.Approach(hangar, out _));

            Assert.IsFalse(CareerConstruction.Reception(state, Vector2.zero, 0f, true, out var why, airport));
            StringAssert.Contains("paved taxiway approach", why);
            Assert.IsFalse(state.layout.receptionBuilt);
            Assert.AreEqual(1000000, state.funds);
        }

        [Test]
        public void ExistingLegacyReceptionCanPurchaseItsParkingAndDrivewayUpgradeOnce()
        {
            var state=State();state.layout.receptionBuilt=true;state.layout.receptionSiteVersion=0;
            var pose=state.layout.reception;float heading=state.layout.receptionHeading;
            Assert.IsTrue(CareerConstruction.Reception(state,pose,heading,true,out var why),why);
            Assert.AreEqual(VisitorCentreConstruction.SiteVersion,state.layout.receptionSiteVersion);
            Assert.AreEqual(pose,state.layout.reception);Assert.AreEqual(heading,state.layout.receptionHeading);
            Assert.AreEqual(1,state.publicRoads.nodes.Count(n=>n.terminalEntrance==VisitorCentreLayout.EntranceId));
            int funds=state.funds;int segments=state.publicRoads.segments.Count;
            Assert.IsFalse(CareerConstruction.Reception(state,pose,heading,true,out _));
            Assert.AreEqual(funds,state.funds);Assert.AreEqual(segments,state.publicRoads.segments.Count);
        }
    }
}
