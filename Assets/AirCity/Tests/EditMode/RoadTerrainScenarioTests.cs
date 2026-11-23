using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using static AirCity.EditorTools.Game.RoadTerrainScenario;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public sealed class RoadTerrainScenarioTests
    {
        CareerGame game;
        RoadToolDriver driver;
        HashSet<GameObject> roots;

        [SetUp]
        public void Build()
        {
            roots = SceneManager.GetActiveScene().GetRootGameObjects().ToHashSet();
            game = NewGameSetup.Build();
            game.Initialize(State(), false);
            driver = new RoadToolDriver(game);
        }

        [TearDown]
        public void Restore()
        {
            driver?.Dispose();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (!roots.Contains(go)) Object.DestroyImmediate(go);
        }

        [TestCase(DevTool.Street, -375f, 1f)]
        [TestCase(DevTool.Road, -525f, -1f)]
        public void LowRoadAlongShoreKeepsTheDryBankAndWetBed(DevTool tool, float x, float side)
        {
            var start = new Vector2(x,BankStart.y);
            var end = new Vector2(x,BankEnd.y);
            var valley = game.valley;
            var world = game.State.world;
            var dry = new Vector2(x, -300f);
            var wet = dry - Vector2.right*side*37.5f;
            float wetHeight = valley.DisplayHeight(wet);
            Assert.That(world.InlandWaterHeight(dry), Is.EqualTo(RiverLevel).Within(.1f), "The rendered river extends beneath this bank.");
            Assert.That(valley.DisplayHeight(dry), Is.GreaterThan(RiverLevel + .15f));
            Assert.That(wetHeight, Is.LessThan(RiverLevel - 2f));

            var oldPublic = game.State.RoadNetwork().segments.Select(e=>e.id).ToHashSet();
            Place(driver, tool, start, end);
            for (float z = -410f; z < -185f; z += 3.125f)
            for (float offset = 0f; offset < 10f; offset += 1.25f)
                Assert.That(valley.DisplayHeight(new Vector2(x+offset*side,z)), Is.GreaterThan(RiverLevel + .16f), $"The dry bank was flooded at {x+offset*side}, {z}.");
            Assert.That(valley.DisplayHeight(wet), Is.EqualTo(wetHeight).Within(.035f));

            bool street = tool == DevTool.Street;
            var structures = street ? game.State.RoadNetwork().segments.Where(e=>!oldPublic.Contains(e.id)).Select(e=>e.structure)
                : game.Airport.Taxiways.edges.Where(e=>e.IsRoad).Select(e=>e.structure);
            Assert.That(structures.ToArray(), Is.Not.Empty.And.All.EqualTo(RoadStructure.Ground));
            var mesh = street ? valley.RoadGeometry.shoulders : game.Airport.Taxiways.SurfaceMesh;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            float offsetX = street ? 0f : game.Airport.transform.position.x;
            Assert.That(Enumerable.Range(0,vertices.Length).Any(i => Mathf.Abs(vertices[i].x + offsetX - x) < 10f &&
                Mathf.Abs(normals[i].y) < .001f), Is.True, "The new road needs a solid edge face down to its roadbed.");
        }

        [TestCase(DevTool.Street)]
        [TestCase(DevTool.Road)]
        public void CliffsideRoadLeavesTheWallStanding(DevTool tool)
        {
            var state = game.State;
            float cliff = game.valley.DisplayHeight(CliffProbe);
            Assert.That(cliff - game.valley.DisplayHeight(new Vector2(600f,825f)), Is.GreaterThan(75f));
            Place(driver, tool, CliffStart, CliffEnd);
            Assert.That(game.valley.DisplayHeight(CliffProbe), Is.GreaterThan(cliff-15f));
            Assert.That(state.world.Height(CliffProbe), Is.EqualTo(cliff).Within(.03f));
        }

        [TestCase(RoadBuildMode.Ground, 0f)]
        [TestCase(RoadBuildMode.Bridge, 4f)]
        public void RoadAlongTheCliffKeepsHeadroomAndNaturalRock(RoadBuildMode mode, float lift)
        {
            var state = game.State;
            var valley = game.valley;
            var a = new Vector2(613f, 660f);
            var b = new Vector2(613f, 915f);
            var bank = new Vector2(625f, 800f);
            float rock = valley.DisplayHeight(bank);
            var start = Geo.X0Z(a, state.world.Height(a) + lift);
            var end = Geo.X0Z(b, state.world.Height(b) + lift);
            var network = state.RoadNetwork();
            int node = network.AddNode(start);
            var settings = RoadBuildSettings.Default;
            settings.mode = mode;
            Assert.That(RoadConstructionPlan.TryCreate(state, network, node, end, -1, -1, PublicRoadStyle.Avenue,
                settings, out var plan, out var reason), Is.True, reason);
            Assert.That(plan.TryCommit(state, network, out var built, out reason), Is.True, reason);
            valley.RefreshRoads(built);
            if (mode == RoadBuildMode.Bridge)
                Assert.That(plan.NewSegments.Select(built.Segment).Any(e => e.structure == RoadStructure.Elevated));
            Assert.That(valley.DisplayHeight(bank), Is.EqualTo(rock).Within(.05f));

            var geometry = valley.RoadGeometry;
            var point = new Vector2(613f, 801f);
            var cut = geometry.groundTriangles.Where(t => t.DistanceSquared(point) < .00001f).ToArray();
            Assert.That(cut, Is.Not.Empty);
            Assert.That(cut.All(t => t.excavationHeight > 0f));
            Assert.That(cut.Any(t => t.excavationHeight >= RoadStructureBuilder.TunnelClearance));
            float road = plan.Path.Height(plan.Path.ClosestParameter(point)) + .18f;
            Assert.That(geometry.structures.vertices.Any(p => Mathf.Abs(p.z - point.y) < 5f &&
                Mathf.Abs(p.y - road - RoadStructureBuilder.TunnelExcavationHeight) < .1f));
            Assert.That(geometry.structures.vertices.Any(p => Mathf.Abs(p.z - b.y) < .05f &&
                p.x > b.x + 1f && p.x < b.x + 7f && p.y > end.y + .6f && p.y < end.y + 5.4f),
                "The dead end retains the earth exposed by the cut.");
        }

        [Test]
        public void ServiceRoadAlongTheCliffUsesTheSameBoundedCut()
        {
            var state = game.State;
            var airport = game.Airport;
            var network = airport.Taxiways;
            var a = new Vector2(617.5f, 660f);
            var b = new Vector2(617.5f, 915f);
            var bank = new Vector2(625f, 800f);
            float rock = state.world.Height(bank);
            var start = airport.transform.InverseTransformPoint(Geo.X0Z(a, state.world.Height(a)));
            var end = airport.transform.InverseTransformPoint(Geo.X0Z(b, state.world.Height(b)));
            int node = network.AddNode(start);
            var settings = RoadBuildSettings.Default;
            settings.mode = RoadBuildMode.Ground;
            Assert.That(AirsideTerrainPlan.TryCreate(state, airport, node, end, -1, null, null, 7f,
                PavementSurface.Asphalt, RoadClass.Service, out var plan, out var reason, settings: settings), Is.True, reason);
            Assert.That(plan.TryCommit(state, out reason), Is.True, reason);
            network.Rebuild();
            game.valley.RefreshLand(plan.Bounds);
            game.valley.SetBuiltAirport(airport);
            Assert.That(state.world.Height(bank), Is.EqualTo(rock).Within(.01f));
            Assert.That(game.valley.DisplayHeight(bank), Is.EqualTo(rock).Within(.05f));
            var road = network.EdgesAt(node).Single(edge => edge.IsRoad);
            var point = Geo.XZ(network.Path(road).Position(.55f));
            var cut = network.GroundOwnership.Where(t => t.DistanceSquared(point) < .00001f).ToArray();
            Assert.That(cut, Is.Not.Empty);
            Assert.That(cut.All(t => t.excavationHeight > 0f && t.foundation == 0));
            Assert.That(cut.Any(t => t.excavationHeight >= RoadStructureBuilder.TunnelClearance));
        }

        [Test]
        public void CrossingsKeepTheirBedsUnderAutomaticBridges()
        {
            CheckBridge(CrossingStart,CrossingEnd,false);
            CheckBridge(RavineStart,RavineEnd,true);
            CheckBridge(GorgeStart,GorgeEnd,true,shortTall:true);
            CheckBridge(InletStart,InletEnd,true);

            void CheckBridge(Vector2 a, Vector2 b, bool suspension, bool shortTall=false)
            {
                var midpoint = Vector2.Lerp(a,b,.5f);
                var valley = game.valley;
                float bed = valley.DisplayHeight(midpoint);
                var old = game.State.RoadNetwork().segments.Select(e=>e.id).ToHashSet();
                Place(driver,DevTool.Street,a,b);
                var network = game.State.RoadNetwork();
                var span = network.segments.FirstOrDefault(e=>!old.Contains(e.id)&&e.structure==RoadStructure.Elevated&&
                    network.Path(e).DistanceTo(midpoint)<2f);
                Assert.That(span, Is.Not.Null, $"No bridge over {midpoint}.");
                var path = network.Path(span);
                Assert.That(path.Height(path.ClosestParameter(midpoint)), Is.GreaterThan(bed+(shortTall?50f:7f)));
                if(shortTall)
                {
                    Assert.That(Vector2.Distance(a,b),Is.LessThan(180f));
                    foreach(var segment in network.segments.Where(e=>!old.Contains(e.id)))
                    {
                        var p=network.Path(segment);
                        TestContext.WriteLine($"{segment.structure}, {p.Length:0.0} m, {p.start} to {p.end}, support " +
                            $"{valley.RoadGeometry.DrawnGround(Geo.XZ(p.start)):0.0} / {valley.RoadGeometry.DrawnGround(Geo.XZ(p.end)):0.0}.");
                    }
                }
                Assert.That(valley.RoadGeometry.SuspensionFor(span)!=null, Is.EqualTo(suspension), $"Bridge type at {midpoint}.");
                Assert.That(valley.DisplayHeight(midpoint), Is.EqualTo(bed).Within(.05f));
            }
        }

        [Test]
        public void DiagonalHighwayTunnelAsksForAnAlignmentWithAShorterOpening()
        {
            var state = game.State;
            var network = state.RoadNetwork();
            float floor = state.world.Height(new Vector2(595f, 650f)) - 1.4f;
            int start = network.AddNode(new Vector3(595f, floor, 650f));
            bool valid = RoadConstructionPlan.TryCreate(state, network, start, new Vector3(665f, floor, 850f),
                -1, -1, PublicRoadStyle.Highway, RoadBuildSettings.Default, out _, out string problem);
            Assert.That(valid, Is.False);
            Assert.That(problem, Is.EqualTo(RoadConstructionPlan.TunnelApproachProblem));
        }

        [Test]
        public void LegacyGroundApproachUsesThePortalCutWhereItMeetsTheCliff()
        {
            var state = game.State;
            var network = state.RoadNetwork().Clone();
            var a = new Vector2(595f, 650f);
            var b = new Vector2(665f, 850f);
            var split = Vector2.MoveTowards(a, b, 52f);
            float floor = state.world.Height(a) - 1.4f;
            int start = network.AddNode(Geo.X0Z(a, floor));
            int mouth = network.AddNode(Geo.X0Z(split, floor));
            int end = network.AddNode(Geo.X0Z(b, floor));
            var approach = network.AddSegment(start, mouth, PublicRoadStyle.Highway, RoadStructure.Ground);
            network.AddSegment(mouth, end, PublicRoadStyle.Highway, RoadStructure.Tunnel);

            game.valley.RefreshRoads(network);
            var geometry = game.valley.RoadGeometry;
            var onOriginalGroundSection = new Vector2(622.14f, 694.10f);
            var cut = geometry.groundTriangles.Where(t => t.DistanceSquared(onOriginalGroundSection) < .00001f).ToArray();
            Assert.That(cut, Is.Not.Empty);
            Assert.That(cut.All(t => t.excavationHeight > 0f));
            Assert.That(approach.structure, Is.EqualTo(RoadStructure.Ground));
        }

        [TestCase(DevTool.Street)]
        [TestCase(DevTool.Road)]
        public void ADirectTunnelKeepsTheHillsideAndSavesItsPortalStyle(DevTool tool)
        {
            var state = game.State;
            var airport = game.Airport;
            var valley = game.valley;
            var sample = new Vector2(637.5f, 650f);
            float natural = state.world.Height(sample);
            float floor = state.world.Height(new Vector2(595f, sample.y)) - 1.4f;
            var start = new Vector3(595f, floor, sample.y);
            var end = new Vector3(690f, floor, sample.y);
            var settings = RoadBuildSettings.Default;
            settings.tunnelPortalStyle = TunnelPortalStyle.Arched;

            if (tool == DevTool.Street)
            {
                var network = state.RoadNetwork();
                int node = network.AddNode(start);
                Assert.That(RoadConstructionPlan.TryCreate(state, network, node, end, -1, -1, PublicRoadStyle.Highway,
                    settings, out var plan, out var problem), Is.True, problem);
                Assert.That(plan.TryCommit(state, network, out var built, out problem), Is.True, problem);
                valley.RefreshRoads(built);
                var saved = JsonUtility.FromJson<PublicRoadNetwork>(JsonUtility.ToJson(built));
                var bore = plan.NewSegments.Select(saved.Segment).Single(edge => edge.structure == RoadStructure.Tunnel);
                Assert.That(bore.tunnelPortalStyle, Is.EqualTo(TunnelPortalStyle.Arched));
                int mid = saved.Split(bore, saved.Path(bore).Position(.5f));
                Assert.That(saved.At(mid).Select(edge => edge.tunnelPortalStyle), Is.All.EqualTo(TunnelPortalStyle.Arched));
            }
            else
            {
                var network = airport.Taxiways;
                int node = network.AddNode(airport.transform.InverseTransformPoint(start));
                Assert.That(AirsideTerrainPlan.TryCreate(state, airport, node, airport.transform.InverseTransformPoint(end),
                    -1, null, null, 7f, PavementSurface.Asphalt, RoadClass.Service, out var plan, out var problem,
                    settings: settings), Is.True, problem);
                Assert.That(plan.TryCommit(state, out problem), Is.True, problem);
                var catalog = GameAssetCatalog.Load();
                var restored = AirportSaveData.Capture(airport, catalog).Restore(null, catalog);
                try
                {
                    var bore = restored.Taxiways.edges.Single(edge => edge.structure == RoadStructure.Tunnel);
                    Assert.That(bore.tunnelPortalStyle, Is.EqualTo(TunnelPortalStyle.Arched));
                }
                finally { Object.DestroyImmediate(restored.gameObject); }
            }

            Assert.That(state.world.Height(sample), Is.EqualTo(natural).Within(.001f));
            Assert.That(valley.DisplayHeight(sample), Is.EqualTo(natural).Within(.035f));
        }

        [TestCase(DevTool.Street)]
        [TestCase(DevTool.Road)]
        public void BuildAnArchAndChangeTheDesignOfTheSelectedBridge(DevTool tool)
        {
            driver.tool.SetRoadBridgeDesign(RoadBridgeDesign.Arch);
            Place(driver,tool,CrossingStart,CrossingEnd);
            bool street=tool==DevTool.Street;
            var publicNetwork=game.State.RoadNetwork();
            var airport=game.Airport;
            var serviceNetwork=airport.Taxiways;
            var publicBridge=street?publicNetwork.segments.First(e=>e.structure==RoadStructure.Elevated):null;
            var serviceBridge=street?null:serviceNetwork.edges.First(e=>e.structure==RoadStructure.Elevated);
            var path=street?publicNetwork.Path(publicBridge):serviceNetwork.Path(serviceBridge);
            int id=street?publicBridge.id:serviceBridge.id;
            Vector3 centre=path.Position(.5f);
            driver.SelectAt(street?centre:airport.transform.TransformPoint(centre));
            Assert.That(driver.tool.SelectedBridgeDesign,Is.EqualTo(RoadBridgeDesign.Arch));
            Assert.That(VisibleDesign(),Is.EqualTo(RoadBridgeDesign.Arch));
            driver.tool.SetSelectedBridgeDesign(RoadBridgeDesign.Suspension);
            Assert.That(VisibleDesign(),Is.EqualTo(RoadBridgeDesign.Suspension));
            driver.tool.SetSelectedBridgeDesign(RoadBridgeDesign.Piers);
            Assert.That(VisibleDesign(),Is.EqualTo(RoadBridgeDesign.Piers));
            Assert.That(driver.tool.SelectedBridgeDesign,Is.EqualTo(RoadBridgeDesign.Piers));
            if(street)
            {
                var roundTrip=JsonUtility.FromJson<PublicRoadNetwork>(JsonUtility.ToJson(game.State.RoadNetwork()));
                Assert.That(roundTrip.Segment(id).bridgeDesign,Is.EqualTo(RoadBridgeDesign.Piers));
                Assert.That(roundTrip.Path(roundTrip.Segment(id)).Position(.5f),Is.EqualTo(centre));
            }
            else
            {
                var catalog=GameAssetCatalog.Load();
                var saved=AirportSaveData.Capture(airport,catalog);
                var rebuilt=saved.Restore(null,catalog);
                try
                {
                    Assert.That(rebuilt.Taxiways.Edge(id).bridgeDesign,Is.EqualTo(RoadBridgeDesign.Piers));
                    Assert.That(rebuilt.Taxiways.Path(rebuilt.Taxiways.Edge(id)).Position(.5f),Is.EqualTo(centre));
                }
                finally{Object.DestroyImmediate(rebuilt.gameObject);}
            }

            RoadBridgeDesign VisibleDesign()=>street
                ? game.valley.RoadGeometry.BridgeFor(game.State.RoadNetwork().Segment(id)).Design
                : serviceNetwork.BridgeDesignFor(id);
        }
    }
}
