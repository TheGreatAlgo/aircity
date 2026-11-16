using System;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class LevelBrushCompletionTests
    {
        [Test]
        public void OneStampCompletesItsFlatCoreAndItsFeatherDoesNotMoveOnRepeatedClicks()
        {
            var world = Relief();
            var centre = new Vector2(12.5f, -12.5f);
            var land = new Rect(-100f, -100f, 200f, 200f);
            const float radius = 80f, target = 86f;
            var original = (float[])world.heights.Clone();
            float quote = world.BrushEarthwork(centre, radius, target, land);
            Assert.Greater(quote, 1f);
            world.LevelBrush(centre, radius, target, land);
            double actual = 0; int core = 0;
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
            {
                int i = z * ValleyWorld.Resolution + x;
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                actual += Math.Abs(original[i] - world.heights[i]) * world.Spacing * world.Spacing;
                if (land.Contains(p) && Vector2.Distance(p, centre) < radius * .72f)
                { Assert.AreEqual(target, world.heights[i]); core++; }
                if (!land.Contains(p) || Vector2.Distance(p, centre) >= radius) Assert.AreEqual(original[i], world.heights[i]);
            }
            Assert.Greater(core, 30);
            Assert.AreEqual(actual, quote, actual * .00001);
            var finished = (float[])world.heights.Clone();
            for (int click = 0; click < 5; click++)
            {
                Assert.AreEqual(0f, world.BrushEarthwork(centre, radius, target, land));
                world.LevelBrush(centre, radius, target, land);
            }
            CollectionAssert.AreEqual(finished, world.heights);
            Assert.Greater(world.BrushEarthwork(centre, radius, target + 4f, land), 1f, "Changing the requested height is new work.");
        }

        [Test]
        public void CompletedCareerPatchCannotChargeForAnotherClickAndUnaffordableWorkIsAtomic()
        {
            var state = new CareerState { world = ValleyWorld.GenerateLegacy(42), handsOn = true, wholeRegion = true, layout = new CareerLayout() };
            state.publicRoads = PublicRoadNetwork.FromWorld(state.world); Assert.IsTrue(state.StartRegion(out _));
            var point = new Vector2(-2200f, 2200f);
            state.world.trees.Clear(); state.funds = 100000000;
            float target = Mathf.Min(399f, state.world.Height(point) + 5f);
            Assert.IsTrue(CareerConstruction.Level(state, point, 60f, target, false, out int quoted, out string why), why);
            Assert.IsTrue(CareerConstruction.Level(state, point, 60f, target, true, out int charged, out why), why);
            Assert.AreEqual(quoted, charged);
            int balance = state.funds; var completed = (float[])state.world.heights.Clone();
            Assert.IsFalse(CareerConstruction.Level(state, point, 60f, target, true, out int repeat, out _));
            Assert.AreEqual(0, repeat); Assert.AreEqual(balance, state.funds);
            CollectionAssert.AreEqual(completed, state.world.heights);
            state.funds = 0;
            Assert.IsFalse(CareerConstruction.Level(state, point, 60f, target + 10f, true, out _, out _));
            CollectionAssert.AreEqual(completed, state.world.heights); Assert.AreEqual(0, state.funds);
        }

        static ValleyWorld Relief()
        {
            var world = new ValleyWorld { heights = new float[ValleyWorld.Resolution * ValleyWorld.Resolution] };
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
                world.heights[z * ValleyWorld.Resolution + x] = 75f + Mathf.Sin(x * .6f) * 12f + Mathf.Cos(z * .4f) * 8f;
            return world;
        }
    }
}
