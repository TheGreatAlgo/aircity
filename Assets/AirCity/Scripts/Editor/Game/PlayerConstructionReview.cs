using System;
using System.Linq;
using AirCity.Game;
using AirCity.World;
using UnityEngine;

namespace AirCity.EditorTools.Game
{
    /// <summary>Review fixtures use the same paid manual transactions as the player's tools.</summary>
    public static class PlayerConstructionReview
    {
        public static void PreparePatch(CareerState state)
        {
            var c = state.Site.centre;
            var clear = new Rect(c.x - 510f, c.y - 295f, 1020f, 515f);
            for (int i = 0; i < state.world.trees.Count; i++)
                if (!state.world.trees[i].removed && clear.Contains(state.world.trees[i].position))
                    if (!CareerConstruction.Clear(state, state.world.trees[i].position, 4f, out string why, i)) throw new Exception(why);
            for (float z = -220f; z <= 140f; z += 45f)
            for (float x = -405f; x <= 405f; x += 45f)
            {
                var p = c + new Vector2(x, z);
                if (!CareerConstruction.Level(state, p, 60f, state.Site.elevation, true, out _, out string why) &&
                    !why.Contains("already at")) throw new Exception(why);
            }
        }
        public static void Runway(CareerState state)
        {
            var c = state.Site.centre;
            if (!CareerConstruction.Runway(state, c + new Vector2(-350f, 25f), c + new Vector2(350f, 25f), true, out _, out string why)) throw new Exception(why);
        }
        public static void Parking(CareerState state)
        {
            var c = state.Site.centre;
            var route = new[] { c + new Vector2(-330f, 25f), c + new Vector2(-330f, -65f), c + new Vector2(330f, -65f), c + new Vector2(330f, 25f) };
            if (!CareerConstruction.Taxiway(state, route, true, out _, out string why)) throw new Exception(why);
            if (!CareerConstruction.Parking(state, c + new Vector2(90f, -100f), true, out why)) throw new Exception(why);
        }
        public static void Road(CareerState state)
        {
            var start = state.Site.centre + new Vector2(220f, -170f);
            var end = new Vector2(ValleyWorld.RoadX, start.y);
            for (int i = 0; i < state.world.trees.Count; i++)
            {
                var tree = state.world.trees[i];
                if (!tree.removed && CareerConstruction.Distance(tree.position, start, end) < 20f)
                    if (!CareerConstruction.Clear(state, tree.position, 4f, out string why, i)) throw new Exception(why);
            }
            if (!CareerConstruction.BuildRoad(state, new[] { start, end }, out string problem)) throw new Exception(problem);
        }
        public static CareerState Open(int seed = 42)
        {
            var state = CareerState.New(seed);
            if (!state.Build(out string why, 0)) throw new Exception(why);
            PreparePatch(state); Runway(state); Parking(state); Road(state);
            if (!CareerConstruction.Reception(state, state.Site.centre + new Vector2(215f, -135f), 0f, true, out why)) throw new Exception(why);
            if (!state.Validate(out why)) throw new Exception(why); return state;
        }
    }
}
