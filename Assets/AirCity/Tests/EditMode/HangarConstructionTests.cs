using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Game;
using AirCity.Vehicles;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    public sealed class HangarConstructionTests
    {
        readonly List<Object> owned = new List<Object>();
        [TearDown] public void Cleanup()
        {
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        Airport Airport()
        {
            var root = new GameObject("Hangar construction fixture"); root.SetActive(false); owned.Add(root);
            root.transform.position = Vector3.up * 80f;
            var airport = root.AddComponent<Airport>(); airport.groundSize = 0f; return airport;
        }

        CareerGame Game(Airport airport, out CareerState state)
        {
            var world = new ValleyWorld { heights = Enumerable.Repeat(80f, ValleyWorld.Resolution * ValleyWorld.Resolution).ToArray() };
            world.sites.Add(new AirportSite { name = "Hangar fixture", centre = Vector2.zero, elevation = 80f });
            state = new CareerState { world = world, selectedSite = 0, handsOn = true, wholeRegion = true,
                funds = 100000, layout = new CareerLayout { unlocked = int.MaxValue }, publicRoads = new PublicRoadNetwork() };
            var host = new GameObject("Hangar session"); host.SetActive(false); owned.Add(host);
            var game = host.AddComponent<CareerGame>();
            typeof(CareerGame).GetProperty("State").SetValue(game, state);
            typeof(CareerGame).GetProperty("Airport").SetValue(game, airport);
            return game;
        }

        [Test]
        public void UnaffordableHangarAndInvalidGroundNeverMutateTheAirportOrFunds()
        {
            var airport = Airport(); var game = Game(airport, out var state);
            state.funds = SmallHangar.BuildPrice - 1;
            var heights = (float[])state.world.heights.Clone(); int version = airport.LayoutVersion;
            Assert.IsFalse(HangarConstruction.TryBuild(game, airport, Vector2.zero, 0f, out var made, out var why));
            Assert.IsNull(made); StringAssert.Contains("funds", why);
            Assert.IsEmpty(airport.Hangars); Assert.AreEqual(version, airport.LayoutVersion);
            Assert.AreEqual(SmallHangar.BuildPrice - 1, state.funds); CollectionAssert.AreEqual(heights, state.world.heights);
            state.funds = 100000;
            for (int i = 0; i < state.world.heights.Length; i++) state.world.heights[i] = 81f;
            Assert.IsNotNull(HangarConstruction.Problem(game, airport, Vector2.zero, 0f));
            Assert.IsEmpty(airport.Hangars); Assert.AreEqual(100000, state.funds);
        }

        [Test]
        public void DisconnectedHangarCanBeBuiltButAnOverlappingSecondShellIsRefused()
        {
            var airport = Airport();
            Assert.IsTrue(HangarConstruction.TryBuild(null, airport, Vector2.zero, 35f, out var first, out var why), why);
            Assert.IsNotNull(first.AccessProblem);
            Assert.IsFalse(HangarConstruction.TryBuild(null, airport, Vector2.one, 35f, out var second, out why));
            Assert.IsNull(second); Assert.AreEqual(1, airport.Hangars.Count());
            Assert.IsNotNull(HangarConstruction.Problem(null, airport, new Vector2(float.NaN, 0), 0f));
        }

        [Test]
        public void PublicRoadAndRoofOverhangAreIncludedInHangarPlacement()
        {
            var airport = Airport(); var game = Game(airport, out var state);
            int a = state.publicRoads.AddNode(new Vector3(-100, 80, -8));
            int b = state.publicRoads.AddNode(new Vector3(100, 80, -8));
            state.publicRoads.AddSegment(a, b, PublicRoadStyle.Rural, RoadStructure.Ground);
            StringAssert.Contains("public road", HangarConstruction.Problem(game, airport, Vector2.zero, 0f));
            Assert.IsEmpty(airport.Hangars); Assert.AreEqual(100000, state.funds);
            var footprint = SmallHangar.Footprint(Vector2.zero, 0f);
            Assert.Less(footprint.Min(p => p.x), -SmallHangar.Width / 2f);
            Assert.Less(footprint.Min(p => p.y), -SmallHangar.Depth);
            Assert.Greater(footprint.Max(p => p.y), 0f);
            state.publicRoads = new PublicRoadNetwork();
            float kerbOnlyGap = RoadTypes.Of(PublicRoadStyle.Urban).Width / 2f + 2f + .4f;
            a = state.publicRoads.AddNode(new Vector3(-100, 80, kerbOnlyGap));
            b = state.publicRoads.AddNode(new Vector3(100, 80, kerbOnlyGap));
            state.publicRoads.AddSegment(a, b, PublicRoadStyle.Urban, RoadStructure.Ground);
            StringAssert.Contains("public road", HangarConstruction.Problem(game, airport, Vector2.zero, 0f),
                "Sidewalks and verges need the same building clearance as the traffic lanes.");
        }

        [Test]
        public void ExistingVisitorCarParkBlocksAHangarFromBeingBuilt()
        {
            var airport = Airport(); var game = Game(airport, out var state);
            state.layout.receptionBuilt = true;
            state.layout.receptionSiteVersion = VisitorCentreConstruction.SiteVersion;
            var door = new Vector2(0f, 30f);

            StringAssert.Contains("parking lot", HangarConstruction.Problem(game, airport, door, 0f));
            Assert.IsFalse(HangarConstruction.TryBuild(game, airport, door, 0f, out _, out var why));
            StringAssert.Contains("parking lot", why);
            Assert.IsEmpty(airport.Hangars);
            Assert.AreEqual(100000, state.funds);
        }

        [Test]
        public void ServiceAndPublicRoadQuotesProtectTheExistingPavedApproach()
        {
            var airport = Airport(); Game(airport, out var state);
            // Use an active network so that the same graph supplies the hangar and road quotes.
            airport.gameObject.SetActive(true);
            var net = airport.Taxiways;
            int a = net.AddNode(new Vector3(-60, 0, 15)), b = net.AddNode(new Vector3(60, 0, 15));
            net.Connect(a, b, 7.5f, PavementSurface.Asphalt);
            var hangar = airport.AddHangar(Vector2.zero, 0f);
            Assert.IsTrue(HangarConnection.Approach(hangar, out _));

            Vector2 from = new Vector2(-40, 5), to = new Vector2(40, 5);
            StringAssert.Contains("paved taxiway approach", net.JoinProblem(from, -1, null, to, -1, null, 7f, RoadClass.Service));
            int roadStart = state.publicRoads.AddNode(new Vector3(-10f, 80f, 5f));
            Assert.IsFalse(RoadConstructionPlan.TryCreate(state, state.publicRoads, roadStart, Geo.X0Z(to, 80f), -1, -1,
                PublicRoadStyle.Rural, new RoadBuildSettings { mode = RoadBuildMode.Ground }, out _, out var why, airport));
            StringAssert.Contains("paved taxiway approach", why);
            Assert.IsTrue(HangarConnection.Approach(hangar, out _));
            Assert.IsEmpty(state.publicRoads.segments);
            Assert.AreEqual(100000, state.funds);
        }

        [Test]
        public void InlandWaterRefusesTheHangarEvenWithoutAnOcean()
        {
            var airport = Airport(); var game = Game(airport, out var state);
            state.world.generatorVersion = 4;
            state.world.waterHeights = Enumerable.Repeat(81f, state.world.heights.Length).ToArray();
            Assert.IsFalse(state.world.HasOcean); Assert.IsTrue(state.world.IsWater(new Vector2(0, -8)));
            var heights = (float[])state.world.heights.Clone();
            Assert.IsFalse(HangarConstruction.TryBuild(game, airport, Vector2.zero, 0f, out var made, out var why));
            Assert.IsNull(made); StringAssert.Contains("water", why);
            Assert.IsEmpty(airport.Hangars); Assert.AreEqual(100000, state.funds);
            CollectionAssert.AreEqual(heights, state.world.heights);
        }

        [Test]
        public void ExistingHangarBlocksTaxiwaysStandsRunwaysAndRoadsWithoutTerrainChanges()
        {
            var airport = Airport(); var game = Game(airport, out var state);
            var hangar = airport.AddHangar(Vector2.zero, 0f); Assert.IsNotNull(hangar);
            var net = airport.Taxiways;
            var a = new Vector2(-100, -8); var b = new Vector2(100, -8);
            Assert.IsNotNull(net.JoinProblem(a, -1, null, b, -1, null, 7.5f));
            var stand = new Stand { code = SizeCode.A, position = Vector3.zero, heading = 0f };
            stand.position += new Vector3(0, 0, -8) - Geo.X0Z(StandLayout.Of(stand).Centre);
            Assert.IsFalse(net.Fits(StandLayout.Of(stand)));
            var runway = airport.AddRunway(Geo.X0Z(a), Geo.X0Z(b), PavementSurface.Asphalt, 18f);
            Assert.IsNotNull(HangarConstruction.RunwayProblem(airport, runway));
            var heights = (float[])state.world.heights.Clone();
            Assert.IsFalse(RunwayTerrainPlan.TryCreate(state, airport, runway, null, out _, out var why));
            StringAssert.Contains("hangar", why); CollectionAssert.AreEqual(heights, state.world.heights);
            int from = state.publicRoads.AddNode(new Vector3(-100, 80, -8));
            Assert.IsFalse(RoadConstructionPlan.TryCreate(state, state.publicRoads, from, new Vector3(100, 80, -8), -1, -1,
                PublicRoadStyle.Rural, new RoadBuildSettings { mode = RoadBuildMode.Ground }, out _, out why, airport));
            Assert.AreEqual(100000, state.funds);
        }

        [Test]
        public void TerrainBrushProtectsTheHangarAndItsInterpolationHalo()
        {
            var airport = Airport(); var game = Game(airport, out var state);
            Assert.IsNotNull(airport.AddHangar(Vector2.zero, 0f));
            var heights = (float[])state.world.heights.Clone();
            Assert.IsFalse(CareerConstruction.Level(state, new Vector2(0, -8), 15f, 82f, true,
                out var price, out var why, airport));
            Assert.AreEqual(0, price); StringAssert.Contains("Demolish", why);
            CollectionAssert.AreEqual(heights, state.world.heights); Assert.AreEqual(100000, state.funds);
        }

        [Test]
        public void BaggageAndDeicingConstructionRespectAnExistingHangar()
        {
            var airport = Airport(); Assert.IsNotNull(airport.AddHangar(Vector2.zero, 0f));
            var centre = new Vector2(0, -SmallHangar.Depth / 2f);
            airport.aprons.Add(ApronArea.Rect(centre, new Vector2(250, 250), 0f, PavementSurface.Concrete));
            StringAssert.Contains("hangar", airport.Deicing.WorkAreaProblem(centre, 0f));
            StringAssert.Contains("hangar", airport.Deicing.ParkingBayProblem(centre, 0f));
            var site = BaggageDepot.Site(BaggageDepotSize.Small, Vector2.zero, 0f);
            var origin = centre - site.centre;
            var problem = BaggageDepot.PlacementProblem(airport, BaggageDepotSize.Small, origin, 0f);
            Assert.IsNotNull(problem); StringAssert.Contains("clear space", problem);
        }
    }
}
