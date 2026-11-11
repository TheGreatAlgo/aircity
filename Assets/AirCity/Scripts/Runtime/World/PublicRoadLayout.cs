using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Saved airport access routes join a region road geometrically, without entering the aircraft graph.</summary>
    public static class PublicRoadLayout
    {
        public const float RoadHalfWidth = 3.25f, ClearingHalfWidth = 16f;
        public static List<Vector2> Suggested(AirportSite site) => new List<Vector2> {
            site.Entrance, new Vector2(ValleyWorld.RoadX, site.Entrance.y)
        };
        public static float Length(IList<Vector2> points)
        { float length = 0f; for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]); return length; }
        public static int Price(IList<Vector2> points) => 1500 + Mathf.CeilToInt(Length(points) * 9f);

        public static bool TrySnap(ValleyWorld world, Vector2 requested, float radius, out Vector2 point)
        {
            point = requested; float best = radius;
            for (int i = 1; i < world.publicRoad.Count; i++)
            {
                var nearest = Geo.ClosestOnSegment(requested, world.publicRoad[i - 1], world.publicRoad[i], out _);
                float d = Vector2.Distance(nearest, requested);
                if (d > best) continue;
                best = d; point = nearest;
            }
            return best < radius;
        }

        public static bool Validate(ValleyWorld world, AirportSite site, IList<Vector2> points, out string problem)
        {
            problem = "Start the access road at the airport entrance.";
            if (world == null || site == null || points == null || points.Count < 2 || points.Count > 32 ||
                Vector2.Distance(points[0], site.Entrance) > .1f) return false;
            problem = "Finish on the existing public road.";
            if (!TrySnap(world, points[points.Count - 1], .2f, out _)) return false;
            problem = "Keep the route inside this region.";
            foreach (var p in points)
                if (!ValleyWorld.Finite(p.x) || !ValleyWorld.Finite(p.y) || Mathf.Abs(p.x) > ValleyWorld.Size * .5f - 20f ||
                    Mathf.Abs(p.y) > ValleyWorld.Size * .5f - 20f) return false;
            problem = "The access route is too long.";
            if (Length(points) > 4000f) return false;
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i];
                problem = "Leave at least 12 m between road corners.";
                if (Vector2.Distance(a, b) < 12f) return false;
                problem = "The road cannot cross itself.";
                for (int j = 1; j < i - 1; j++)
                    if (Geo.Segments(a, b, points[j - 1], points[j], .01f, out _)) return false;
                if (i > 1)
                {
                    problem = "Give the road a gentler turn.";
                    if (Vector2.Angle(a - points[i - 2], b - a) > 130f) return false;
                }
                int count = Mathf.CeilToInt(Vector2.Distance(a, b) / 10f);
                for (int j = 0; j <= count; j++)
                {
                    var p = Vector2.Lerp(a, b, j / (float)count);
                    problem = "Keep the public road clear of the runway and aircraft parking.";
                    if (p.x < site.centre.x + 650f && p.x > site.centre.x - 650f && p.y > site.centre.y - 125f && p.y < site.centre.y + 100f) return false;
                    problem = "Keep clear of the passenger reception building.";
                    if (new Rect(site.centre.x + 66f, site.centre.y - 160f, 28f, 20f).Contains(p)) return false;
                    problem = "This route needs a river crossing. Choose land for the first access road.";
                    if (Mathf.Abs(p.x - world.RiverX(p.y)) < 105f || world.Height(p) < world.WaterHeight(p.y) + 1f) return false;
                }
            }
            problem = null; return true;
        }

        public static void Clear(ValleyWorld world, IList<Vector2> points)
        {
            foreach (var tree in world.trees)
            {
                if (tree.removed) continue;
                for (int i = 1; i < points.Count; i++)
                {
                    var p = Geo.ClosestOnSegment(tree.position, points[i - 1], points[i], out _);
                    if ((p - tree.position).sqrMagnitude < ClearingHalfWidth * ClearingHalfWidth) { tree.removed = true; break; }
                }
            }
        }
    }
}
