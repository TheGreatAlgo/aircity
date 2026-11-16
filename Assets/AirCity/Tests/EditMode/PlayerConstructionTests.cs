using System.Linq;
using AirCity.EditorTools.Game;
using AirCity.Airfield;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Game
{
    public class PlayerConstructionTests
    {
        static CareerState Bought()
        {
            // Transactions use a stable woodland fixture; biome/source tests exercise the actual new regions.
            var s = new CareerState { world = ValleyWorld.GenerateLegacy(42), handsOn = true, wholeRegion = true, layout = new CareerLayout() };
            s.publicRoads = PublicRoadNetwork.FromWorld(s.world);
            Assert.IsTrue(s.StartRegion(out _)); return s;
        }

        [Test]
        public void NewRegionOpensWithoutAPurchaseAndPreparationToolsAreImmediatelyAvailable()
        {
            var s = Bought(); string before = JsonUtility.ToJson(s);
            Assert.AreEqual(CareerState.StartingFunds, s.funds); Assert.IsTrue(s.wholeRegion);
            Assert.IsTrue(s.layout.Unlocked(CareerTool.ClearTrees)); Assert.IsTrue(s.layout.Unlocked(CareerTool.Level));
            Assert.IsTrue(s.layout.Unlocked(CareerTool.Runway)); Assert.IsFalse(s.layout.Unlocked(CareerTool.Taxiway));
            Assert.IsFalse(s.Build(out _)); Assert.AreEqual(before, JsonUtility.ToJson(s));
        }
        [TestCase(RegionPreset.Woodland)] [TestCase(RegionPreset.Mountains)]
        [TestCase(RegionPreset.Tropical)] [TestCase(RegionPreset.Desert)] [TestCase(RegionPreset.Random)]
        public void EveryBiomeCanStartWithNoTreesAndNoLandPurchase(RegionPreset preset)
        {
            var state = CareerState.New(73, preset: preset); state.world.trees.Clear(); state.funds = 0;
            Assert.IsTrue(state.StartRegion(out string why), why);
            Assert.AreEqual(0, state.funds); Assert.AreEqual(CareerStep.Runway, state.Step);
            Assert.IsTrue(state.layout.Unlocked(CareerTool.Level)); Assert.IsTrue(state.layout.Unlocked(CareerTool.Runway));
            Assert.IsTrue(state.Validate(out why), why);
        }
        [Test]
        public void TreesAndTerrainCanBeEditedBeyondEveryFormerSiteParcel()
        {
            var s = Bought();
            var tree = s.world.trees.First(t => !s.world.sites.Any(site => site.Parcel.Contains(t.position)) &&
                s.world.publicRoad.All(p => Vector2.Distance(p, t.position) > 250f));
            var point = tree.position;
            Assert.IsTrue(CareerConstruction.Editable(s, point));
            int funds = s.funds;
            Assert.IsTrue(CareerConstruction.Clear(s, point, 4f, out string why, s.world.trees.IndexOf(tree)), why);
            Assert.AreEqual(funds - CareerConstruction.TreePrice, s.funds);
            // This test starts the grading transaction with a previously cleared brush footprint.
            foreach (var t in s.world.trees.Where(t => Vector2.Distance(t.position, point) < 110f)) t.removed = true;
            float target = Mathf.Min(399f, s.world.Height(point) + 1f);
            s.funds = 100000000;
            Assert.IsTrue(CareerConstruction.Level(s, point, 15f, target, false, out int price, out why), why);
            Assert.IsTrue(CareerConstruction.Level(s, point, 15f, target, true, out _, out why), why);
            Assert.AreEqual(100000000 - price, s.funds);
            Assert.IsFalse(CareerConstruction.Editable(s, new Vector2(ValleyWorld.Size, 0f)));
            Assert.IsFalse(CareerConstruction.Editable(s, new Vector2(float.NaN, 0f)));
        }
        [Test]
        public void RunwayAnywhereInTheRegionAndRegionAccessSurviveSaveAndSnapshot()
        {
            var s = Bought(); var centre = new Vector2(-2200f, 2200f);
            Assert.IsFalse(s.world.sites.Any(site => site.Parcel.Contains(centre)));
            s.world.Grade(new Rect(centre.x - 500f, centre.y - 100f, 1000f, 200f), 80f);
            Assert.IsTrue(CareerConstruction.Runway(s, centre + Vector2.left * 350f, centre + Vector2.right * 350f,
                true, out int price, out string why), why);
            Assert.AreEqual(14000, price);
            var copy = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(CareerSnapshot.Capture(s)));
            Assert.IsTrue(copy.wholeRegion); Assert.AreEqual(s.world.RegionBounds, copy.BuildableBounds);
            Assert.AreEqual(centre + Vector2.left * 350f, copy.layout.runwayA);
            Assert.IsTrue(copy.Validate(out why), why);
        }
        [Test]
        public void ExistingParcelSavesKeepTheirOriginalBounds()
        {
            var s = Bought(); s.wholeRegion = false;
            var copy = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(CareerSnapshot.Capture(s)));
            Assert.IsFalse(copy.wholeRegion); Assert.AreEqual(copy.Site.Parcel, copy.BuildableBounds);
            Assert.IsFalse(CareerConstruction.Editable(copy, new Vector2(-2200f, 2200f)));
            Assert.IsTrue(copy.Validate(out string why), why);
        }
        [Test]
        public void LongerRoadConnectionsCostMoreWithoutBuyingRemoteLand()
        {
            var s = Bought(); var roads = new PublicRoadNetwork();
            int start = roads.AddNode(new Vector3(-2000f, 80f, 2000f));
            Assert.IsTrue(PublicRoadConstruction.Build(s, roads, start, new Vector3(-1800f, 80f, 2000f), -1, -1,
                PublicRoadStyle.Rural, false, out _, out int nearby, out string why), why);
            Assert.IsTrue(PublicRoadConstruction.Build(s, roads, start, new Vector3(-800f, 80f, 2000f), -1, -1,
                PublicRoadStyle.Rural, false, out _, out int remote, out why), why);
            Assert.AreEqual(nearby * 6, remote); Assert.AreEqual(CareerState.StartingFunds, s.funds);
        }
        [Test]
        public void SingleTreeRemovalChargesOnceLeavesNeighboursAndUnlocksTerrain()
        {
            var s = Bought(); int index = s.world.trees.FindIndex(t => s.Site.Parcel.Contains(t.position));
            int living = s.world.trees.Count(t => !t.removed), money = s.funds;
            Assert.IsTrue(CareerConstruction.Clear(s, s.world.trees[index].position, 4f, out string why, index), why);
            Assert.AreEqual(living - 1, s.world.trees.Count(t => !t.removed)); Assert.AreEqual(money - CareerConstruction.TreePrice, s.funds);
            Assert.IsTrue(s.layout.Unlocked(CareerTool.Level)); Assert.IsTrue(s.layout.Unlocked(CareerTool.Runway));
            money = s.funds; Assert.IsFalse(CareerConstruction.Clear(s, s.world.trees[index].position, 4f, out _, index)); Assert.AreEqual(money, s.funds);
        }
        [Test]
        public void BrushRemovalAndUnaffordableRemovalAreAtomic()
        {
            var s = Bought(); var p = s.world.trees.First(t => s.Site.Preparation.Contains(t.position)).position;
            var chosen = CareerConstruction.Trees(s, p, 40f); int count = s.world.trees.Count(t => !t.removed);
            var quote = CareerConstruction.QuoteClear(s, p, 40f);
            int funds = s.funds; s.funds = 1; string before = JsonUtility.ToJson(s);
            Assert.IsFalse(CareerConstruction.Clear(s, p, 40f, out _)); Assert.AreEqual(before, JsonUtility.ToJson(s));
            s.funds = funds; Assert.IsTrue(CareerConstruction.Clear(s, p, 40f, out string why), why);
            Assert.AreEqual(count - chosen.Count, s.world.trees.Count(t => !t.removed)); Assert.AreEqual(funds - quote.Price, s.funds);
        }
        [Test]
        public void GradingNeverRemovesTreesAndRefusesAnOccupiedFootprint()
        {
            var s = Bought(); int first = s.world.trees.FindIndex(t => s.Site.Parcel.Contains(t.position));
            CareerConstruction.Clear(s, s.world.trees[first].position, 4f, out _, first);
            var point = s.world.trees.First(t => !t.removed && s.Site.Preparation.Contains(t.position)).position;
            string before = JsonUtility.ToJson(s);
            Assert.IsFalse(CareerConstruction.Level(s, point, 45f, s.Site.elevation, true, out _, out string why));
            StringAssert.Contains("trees", why); Assert.AreEqual(before, JsonUtility.ToJson(s));
        }
        [TestCase("runway")]
        [TestCase("taxiway")]
        [TestCase("parking")]
        [TestCase("reception")]
        [TestCase("road")]
        [TestCase("bridge")]
        public void GradingNearConstructionRequiresDemolitionEvenAtTheAirportHeight(string structure)
        {
            var s = Bought(); var c = s.Site.centre;
            s.world.Clear(s.Site.Parcel); s.layout.Unlock(CareerTool.Level); s.layout.elevation = s.Site.elevation;
            var p = c;
            switch (structure)
            {
                case "runway":
                    s.layout.runwayBuilt = true; s.layout.runwayA = c + Vector2.left * 350f; s.layout.runwayB = c + Vector2.right * 350f;
                    // The visible 15 m brush misses the runway; its interpolation halo still reaches it.
                    p += Vector2.up * (15f + 15f + s.world.Spacing - 1f); break;
                case "taxiway":
                    s.layout.taxiways.Add(new CareerTaxiway { points = new System.Collections.Generic.List<Vector2> { c + Vector2.left * 100f, c + Vector2.right * 100f } });
                    p += Vector2.up * (15f + 6f + s.world.Spacing - 1f); break;
                case "parking":
                    s.layout.standBuilt = true; s.layout.stand = c; s.layout.standAlong = Vector2.right; s.layout.standHeading = 0f;
                    p = StandLayout.Of(s.layout.Parking(Vector2.zero)).Centre; break;
                case "reception":
                    s.layout.receptionBuilt = true; s.layout.reception = c; s.layout.receptionHeading = 45f; break;
                default:
                    s.publicRoads = new PublicRoadNetwork();
                    int a = s.publicRoads.AddNode(new Vector3(c.x - 100f, s.Site.elevation + 8f, c.y));
                    int b = s.publicRoads.AddNode(new Vector3(c.x + 100f, s.Site.elevation + 8f, c.y));
                    s.publicRoads.AddSegment(a, b, PublicRoadStyle.AirportAccess, structure == "bridge" ? RoadStructure.Elevated : RoadStructure.Ground); break;
            }
            var heights = (float[])s.world.heights.Clone(); int funds = s.funds, unlocks = s.layout.unlocked;
            foreach (float target in new[] { s.Site.elevation, s.Site.elevation + 2f })
            foreach (bool commit in new[] { false, true })
            {
                Assert.IsFalse(CareerConstruction.Level(s, p, 15f, target, commit, out int price, out string why));
                StringAssert.Contains("Demolish", why); Assert.AreEqual(0, price);
            }
            Assert.AreEqual(funds, s.funds); Assert.AreEqual(unlocks, s.layout.unlocked); Assert.AreEqual(0f, s.layout.earthwork);
            CollectionAssert.AreEqual(heights, s.world.heights); Assert.IsNull(s.world.disturbance);
            // Removing the obstruction must permit ordinary paid earthworks at that same point.
            s.layout = new CareerLayout(); s.layout.Unlock(CareerTool.Level); s.publicRoads = PublicRoadNetwork.FromWorld(s.world);
            Assert.IsTrue(CareerConstruction.Level(s, p, 15f, s.Site.elevation + 2f, false, out _, out string allowed), allowed);
            Assert.IsTrue(CareerConstruction.Level(s, p, 15f, s.Site.elevation + 2f, true, out _, out allowed), allowed);
            Assert.Less(s.funds, funds);
        }
        [Test]
        public void PlacedRunwayUsesChosenThresholdsAndLeavesUnselectedForest()
        {
            var s = Bought(); PlayerConstructionReview.PreparePatch(s);
            int remaining = s.world.trees.Count(t => !t.removed);
            var a = s.Site.centre + new Vector2(-350f, 25f); var b = s.Site.centre + new Vector2(350f, 25f);
            Assert.IsTrue(CareerConstruction.Runway(s, a, b, false, out int price, out string why), why);
            int funds = s.funds;
            Assert.IsTrue(CareerConstruction.Runway(s, a, b, true, out _, out why), why);
            Assert.AreEqual(a, s.layout.runwayA); Assert.AreEqual(b, s.layout.runwayB); Assert.AreEqual(funds - price, s.funds);
            Assert.AreEqual(remaining, s.world.trees.Count(t => !t.removed)); Assert.IsTrue(s.layout.Unlocked(CareerTool.ClearTrees));
            Assert.IsTrue(s.layout.Unlocked(CareerTool.Taxiway)); Assert.IsFalse(s.layout.Unlocked(CareerTool.Reception));
        }
        [Test]
        public void RoadsRefuseTreesInsteadOfAutomaticallyClearingACorridor()
        {
            var s = Bought(); PlayerConstructionReview.PreparePatch(s); PlayerConstructionReview.Runway(s); PlayerConstructionReview.Parking(s);
            var start = s.Site.centre + new Vector2(220f, -170f);
            var route = new[] { start, new Vector2(s.world.RoadAt(start.y), start.y) };
            string before = JsonUtility.ToJson(s);
            Assert.IsFalse(CareerConstruction.BuildRoad(s, route, out string why)); StringAssert.Contains("trees", why);
            Assert.AreEqual(before, JsonUtility.ToJson(s));
        }
        [Test]
        public void CustomLayoutGroundEditsAndEarnedToolsSurviveSaveSerialization()
        {
            var s = PlayerConstructionReview.Open(); var copy = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(s));
            Assert.IsTrue(copy.Validate(out string why), why); Assert.IsTrue(copy.IsOpen);
            Assert.AreEqual(JsonUtility.ToJson(s), JsonUtility.ToJson(copy));
            Assert.Greater(copy.world.disturbance.Max(), .8f); Assert.IsTrue(copy.layout.Unlocked(CareerTool.Reception));
        }
        [Test]
        public void ContinuingAnOriginalSavePreservesItsLayoutWhileEnablingManualTools()
        {
            var s = CareerState.New(17, false); s.Build(out _, 0);
            while (!s.IsOpen) Assert.IsTrue(s.Build(out string why), why);
            s.saveVersion = 1;
            Assert.IsTrue(s.Validate(out string legacyProblem), legacyProblem);
            int funds = s.funds; var heights = (float[])s.world.heights.Clone(); int trees = s.world.trees.Count(t => !t.removed);
            s.UpgradeToPlayerConstruction();
            Assert.AreEqual(CareerState.SaveVersion, s.saveVersion);
            Assert.IsTrue(s.handsOn); Assert.IsTrue(s.IsOpen); Assert.AreEqual(funds, s.funds); CollectionAssert.AreEqual(heights, s.world.heights);
            Assert.AreEqual(trees, s.world.trees.Count(t => !t.removed)); Assert.IsTrue(s.Validate(out string problem), problem);
        }
    }
}
