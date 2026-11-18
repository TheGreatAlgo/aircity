using System.Linq;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;

namespace AirCity.Tests.Game
{
    /// <summary>RoadConstructionPlan.Structures, the shared rule for which stretches of a street or service road are
    /// bridge, tunnel or ground.</summary>
    public sealed class RoadStructureRuleTests
    {
        [Test]public void StructuresFollowTheGroundAwayFromItAndRampAtGradeEnds()
        {
            var gaps=new[]{0f,1f,3f,8f,1f,8f,3f,.5f,-.5f,-3f,-8f,-1f,-8f,-3f,0f};
            var e=RoadStructure.Elevated;var t=RoadStructure.Tunnel;var o=RoadStructure.Ground;
            CollectionAssert.AreEqual(new[]{o,o,e,e,o,e,e,o,o,t,t,o,t,t,o},RoadConstructionPlan.Structures(RoadBuildMode.Automatic,gaps));
            CollectionAssert.AreEqual(new[]{o,o,e,e,e,e,e,o,o,t,t,o,t,t,o},RoadConstructionPlan.Structures(RoadBuildMode.Bridge,gaps),"Bridge keeps a low stretch between bridge stretches.");
            CollectionAssert.AreEqual(new[]{o,o,e,e,o,e,e,o,o,t,t,t,t,t,o},RoadConstructionPlan.Structures(RoadBuildMode.Tunnel,gaps),"Tunnel keeps a shallow stretch between tunnel stretches.");
            Assert.IsTrue(RoadConstructionPlan.Structures(RoadBuildMode.Ground,gaps).All(k=>k==o));
            CollectionAssert.AreEqual(new[]{e,e,e},RoadConstructionPlan.Structures(RoadBuildMode.Bridge,new[]{5f,5f,5f}));
        }

        [Test]public void OverWaterTheSurfaceDecidesAndBridgeEndsStandOnTheBank()
        {
            var e=RoadStructure.Elevated;var t=RoadStructure.Tunnel;var o=RoadStructure.Ground;float dry=float.NaN;
            // a road 1.5 m over the bed of shallow water that is 1 m deep: 0.5 m above the surface, so a bridge
            // (the abutments stand ShoreStretches = 3 stretches back on each bank)
            var gaps=new[]{0f,0f,0f,0f,0f,1.5f,1.5f,1.5f,0f,0f,0f,0f,0f};var water=new[]{dry,dry,dry,dry,dry,.5f,.5f,.5f,dry,dry,dry,dry,dry};
            var bridged=new[]{o,o,e,e,e,e,e,e,e,e,e,o,o};
            CollectionAssert.AreEqual(bridged,RoadConstructionPlan.Structures(RoadBuildMode.Automatic,gaps,water));
            CollectionAssert.AreEqual(bridged,RoadConstructionPlan.Structures(RoadBuildMode.Bridge,gaps,water));
            CollectionAssert.AreEqual(bridged,RoadConstructionPlan.Structures(RoadBuildMode.Tunnel,gaps,water),"Above the surface is never a tunnel.");
            // below the surface with a tunnel's cover under the bed it is a tunnel; Ground mode stays ground (and is refused)
            water=new[]{dry,dry,dry,-8f,-8f,-8f,dry,dry};gaps=new[]{-8f,-8f,-8f,-7f,-7f,-7f,-8f,-8f};
            CollectionAssert.AreEqual(new[]{t,t,t,t,t,t,t,t},RoadConstructionPlan.Structures(RoadBuildMode.Automatic,gaps,water));
            CollectionAssert.AreEqual(new[]{t,t,t,t,t,t,t,t},RoadConstructionPlan.Structures(RoadBuildMode.Bridge,gaps,water),"A covered bore stays a bore in Bridge mode.");
            Assert.IsTrue(RoadConstructionPlan.Structures(RoadBuildMode.Ground,gaps,water).All(k=>k==o));
        }

        [Test]public void UnderTheSurfaceWithoutCoverBridgeMakesABridgeAndAutomaticTheNearerFix()
        {
            var e=RoadStructure.Elevated;var t=RoadStructure.Tunnel;var o=RoadStructure.Ground;float dry=float.NaN;
            // a road at grade sagging 0.3 m under the surface of 1 m deep water (a lake's shore band, stub grades): a
            // bridge 1.3 m higher clears it, a bore needs 6.3 m more. Bridge and Automatic make it a (too low) bridge
            // for TryCreate to lift or refuse as a deck, never a bore; Tunnel keeps it a bore (refused for cover).
            var gaps=new[]{0f,0f,0f,0f,.7f,.7f,.7f,0f,0f,0f,0f};var water=new[]{dry,dry,dry,dry,-.3f,-.3f,-.3f,dry,dry,dry,dry};
            var bridged=new[]{o,e,e,e,e,e,e,e,e,e,o};
            CollectionAssert.AreEqual(bridged,RoadConstructionPlan.Structures(RoadBuildMode.Bridge,gaps,water));
            CollectionAssert.AreEqual(bridged,RoadConstructionPlan.Structures(RoadBuildMode.Automatic,gaps,water));
            CollectionAssert.AreEqual(new[]{o,t,t,t,t,t,t,t,t,t,o},RoadConstructionPlan.Structures(RoadBuildMode.Tunnel,gaps,water));
            // 6 m under the surface of water 5.5 m deep: 7 m from clearing it, 6.5 m from cover. Automatic bores (unless
            // asked to bridge the water), Bridge still bridges.
            gaps=new[]{-3f,-3f,-3f,-3f,-.5f,-.5f,-.5f,-3f,-3f,-3f,-3f};water=new[]{dry,dry,dry,dry,-6f,-6f,-6f,dry,dry,dry,dry};
            CollectionAssert.AreEqual(new[]{t,t,t,t,t,t,t,t,t,t,t},RoadConstructionPlan.Structures(RoadBuildMode.Automatic,gaps,water));
            CollectionAssert.AreEqual(new[]{t,t,t,t,e,e,e,t,t,t,t},RoadConstructionPlan.Structures(RoadBuildMode.Automatic,gaps,water,bridgeWater:true));
            CollectionAssert.AreEqual(new[]{t,t,t,t,e,e,e,t,t,t,t},RoadConstructionPlan.Structures(RoadBuildMode.Bridge,gaps,water));
        }

        [Test]public void ABridgeEndStopsWhereTheBankIsHigherThanTheDeck()
        {
            var e=RoadStructure.Elevated;var o=RoadStructure.Ground;float dry=float.NaN;
            // the bank rises 0.4 m above the road just before the water (a cutting): the deck reaches back over the
            // shore only up to it, so no bridge deck lies in the ground. `bank` (the lowest edge) counts as well.
            var gaps=new[]{0f,0f,-.4f,0f,1.5f,1.5f,0f,0f,0f,0f};var water=new[]{dry,dry,dry,dry,.5f,.5f,dry,dry,dry,dry};
            CollectionAssert.AreEqual(new[]{o,o,o,e,e,e,e,e,e,o},RoadConstructionPlan.Structures(RoadBuildMode.Automatic,gaps,water));
            CollectionAssert.AreEqual(new[]{o,o,o,e,e,e,e,e,e,o},RoadConstructionPlan.Structures(RoadBuildMode.Bridge,gaps,water));
            var bank=new[]{0f,0f,-.4f,0f,1.5f,1.5f,0f,0f,-.3f,0f};
            CollectionAssert.AreEqual(new[]{o,o,o,e,e,e,e,e,o,o},RoadConstructionPlan.Structures(RoadBuildMode.Automatic,gaps,water,bank:bank));
        }
    }
}