using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Plans the region's existing country road over dry, accessible terrain before scenery is scattered.
    /// The resulting route and vertical profile are saved; loading never reroutes roads around player earthworks.</summary>
    public static class RegionRoadPlanner
    {
        public const float MaximumGrade = .075f;
        const int Grid = 65;
        const float Cell = ValleyWorld.Size / (Grid - 1);
        static readonly int[] DX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        static readonly int[] DZ = { -1, -1, -1, 0, 0, 1, 1, 1 };

        public static void Plan(ValleyWorld world)
        {
            var heights = new float[Grid * Grid]; var dry = new bool[heights.Length];
            for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                int i = z * Grid + x; var p = Point(i);
                heights[i] = world.Height(p); dry[i] = ClearLand(world, p, 14f);
            }
            var land = LargestLand(world, dry);
            if (land.Count < 2)
            {
                // A malformed or almost submerged custom source still yields a finite, land-bound road.
                world.publicRoad.Clear(); world.roadHeights = Array.Empty<float>(); return;
            }
            int start, end;
            if (world.HasOcean)
            {
                var coast = land.FindAll(i => heights[i] < world.SeaLevel + 45f && NearWater(dry, i));
                if (coast.Count < 2) coast = land;
                start = coast[0];
                foreach (int i in coast) if (Point(i).x < Point(start).x) start = i;
                end = Farthest(coast, start);
                start = Farthest(coast, end);
            }
            else
            {
                var targetA = world.publicRoad.Count > 0 ? world.publicRoad[0] : new Vector2(1400f, -ValleyWorld.Size * .5f);
                var targetB = world.publicRoad.Count > 1 ? world.publicRoad[world.publicRoad.Count - 1] : new Vector2(1400f, ValleyWorld.Size * .5f);
                start = Nearest(land, targetA); end = Nearest(land, targetB);
                if (start == end) end = Farthest(land, start);
            }
            var allowed = new bool[heights.Length]; foreach (int i in land) allowed[i] = true;
            var route = Search(world, allowed, heights, start, end);
            if (route.Count < 2)
            {
                // The flood component uses the same footprint checks as the search, so this only covers custom data.
                end = Farthest(land, start); route = Search(world, allowed, heights, start, end);
            }
            Smooth(world, route);
            world.publicRoad = route;
            world.roadHeights = Profile(world, route);
        }

        public static float[] Profile(ValleyWorld world, IList<Vector2> route)
        {
            var result = new float[route.Count]; var raw = new float[route.Count];
            for (int i = 0; i < raw.Length; i++) raw[i] = world.Height(route[i]);
            // Gentle vertical curves are chosen before enforcing the maximum grade.
            for (int i = 0; i < result.Length; i++)
            {
                float sum = 0f, weights = 0f;
                for (int k = -2; k <= 2; k++)
                {
                    int at = Mathf.Clamp(i + k, 0, raw.Length - 1); float w = 3f - Mathf.Abs(k);
                    sum += raw[at] * w; weights += w;
                }
                result[i] = Mathf.Max(world.HasOcean ? world.SeaLevel + 2.5f : 10f, sum / weights);
            }
            for (int pass = 0; pass < 8; pass++)
            {
                for (int i = 1; i < result.Length; i++)
                {
                    float rise = Vector2.Distance(route[i - 1], route[i]) * MaximumGrade;
                    result[i] = Mathf.Clamp(result[i], result[i - 1] - rise, result[i - 1] + rise);
                }
                for (int i = result.Length - 2; i >= 0; i--)
                {
                    float rise = Vector2.Distance(route[i], route[i + 1]) * MaximumGrade;
                    result[i] = Mathf.Clamp(result[i], result[i + 1] - rise, result[i + 1] + rise);
                }
            }
            return result;
        }

        static Vector2 Point(int i) => new Vector2(i % Grid * Cell - ValleyWorld.Size * .5f, i / Grid * Cell - ValleyWorld.Size * .5f);
        static int Nearest(List<int> candidates, Vector2 point)
        {
            int best = candidates[0]; float d = float.MaxValue;
            foreach (int i in candidates) { float next = (Point(i) - point).sqrMagnitude; if (next < d) { d = next; best = i; } }
            return best;
        }
        static int Farthest(List<int> candidates, int point)
        {
            int best = point; float d = -1f;
            foreach (int i in candidates) { float next = (Point(i) - Point(point)).sqrMagnitude; if (next > d) { d = next; best = i; } }
            return best;
        }
        static bool NearWater(bool[] dry, int i)
        {
            int x = i % Grid, z = i / Grid;
            for (int n = 0; n < DX.Length; n++)
            {
                int nx = x + DX[n], nz = z + DZ[n];
                if (nx < 0 || nz < 0 || nx >= Grid || nz >= Grid || !dry[nz * Grid + nx]) return true;
            }
            return false;
        }
        static bool ClearLand(ValleyWorld world, Vector2 p, float margin)
        {
            bool Water(Vector2 q) => world.IsWater(q) || world.HasRiver && Mathf.Abs(q.x - world.RiverX(q.y)) < world.River.halfWidth + 12f;
            if (Water(p)) return false;
            return !Water(p + Vector2.left * margin) && !Water(p + Vector2.right * margin) &&
                !Water(p + Vector2.up * margin) && !Water(p + Vector2.down * margin);
        }
        static bool ClearSegment(ValleyWorld world, Vector2 a, Vector2 b)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 20f));
            for (int i = 0; i <= steps; i++) if (!ClearLand(world, Vector2.Lerp(a, b, i / (float)steps), 10f)) return false;
            return true;
        }
        static List<int> LargestLand(ValleyWorld world, bool[] dry)
        {
            var seen = new bool[dry.Length]; var largest = new List<int>(); var queue = new Queue<int>();
            for (int first = 0; first < dry.Length; first++)
            {
                if (!dry[first] || seen[first]) continue;
                var component = new List<int>(); seen[first] = true; queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    int at = queue.Dequeue(); component.Add(at); int x = at % Grid, z = at / Grid;
                    for (int n = 0; n < DX.Length; n++)
                    {
                        int nx = x + DX[n], nz = z + DZ[n];
                        if (nx < 0 || nz < 0 || nx >= Grid || nz >= Grid) continue;
                        int next = nz * Grid + nx;
                        if (!dry[next] || seen[next]) continue;
                        if (!ClearSegment(world, Point(at), Point(next))) continue;
                        seen[next] = true; queue.Enqueue(next);
                    }
                }
                if (component.Count > largest.Count) largest = component;
            }
            return largest;
        }

        static List<Vector2> Search(ValleyWorld world, bool[] land, float[] heights, int start, int end)
        {
            int count = heights.Length;
            var cost = new float[count]; var previous = new int[count]; var closed = new bool[count];
            for (int i = 0; i < count; i++) { cost[i] = float.PositiveInfinity; previous[i] = -1; }
            var heap = new MinHeap(); cost[start] = 0f; heap.Add(start, 0f);
            while (heap.Count > 0)
            {
                int at = heap.Take(); if (closed[at]) continue;
                closed[at] = true; if (at == end) break;
                int x = at % Grid, z = at / Grid;
                for (int n = 0; n < DX.Length; n++)
                {
                    int nx = x + DX[n], nz = z + DZ[n];
                    if (nx < 0 || nz < 0 || nx >= Grid || nz >= Grid) continue;
                    int next = nz * Grid + nx; if (!land[next] || closed[next]) continue;
                    var a = Point(at); var b = Point(next);
                    if (!ClearSegment(world, a, b)) continue;
                    float length = Vector2.Distance(a, b), grade = Mathf.Abs(heights[at] - heights[next]) / length;
                    float excess = Mathf.Max(0f, grade - .055f);
                    float ascent = Mathf.Abs(heights[at] - heights[next]);
                    float high = Mathf.Max(0f, (heights[at] + heights[next]) * .5f - (world.HasOcean ? world.SeaLevel + 12f : 65f));
                    float turn = 0f;
                    if (previous[at] >= 0) turn = Vector2.Angle(a - Point(previous[at]), b - a) / 90f;
                    float candidate = cost[at] + length * (1f + excess * excess * 450f + high * .008f + turn * .25f) + ascent * 2f;
                    if (candidate >= cost[next]) continue;
                    cost[next] = candidate; previous[next] = at;
                    heap.Add(next, candidate + Vector2.Distance(b, Point(end)));
                }
            }
            var result = new List<Vector2>();
            if (!closed[end]) return result;
            for (int at = end; at >= 0; at = previous[at]) result.Add(Point(at));
            result.Reverse(); return result;
        }
        static void Smooth(ValleyWorld world, List<Vector2> route)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                var before = route.ToArray();
                for (int i = 1; i < route.Count - 1; i++)
                {
                    var point = (before[i - 1] + before[i] * 2f + before[i + 1]) * .25f;
                    if (!ClearSegment(world, before[i - 1], point) || !ClearSegment(world, point, before[i + 1])) continue;
                    if (world.Height(point) > Mathf.Max(world.Height(before[i - 1]), world.Height(before[i + 1])) + 8f) continue;
                    route[i] = point;
                }
                // Both ends of a segment can move in one pass. Check their final corridor together, too.
                for (int i = 1; i < route.Count; i++)
                    if (!ClearSegment(world, route[i - 1], route[i]))
                    { for (int k = 0; k < route.Count; k++) route[k] = before[k]; break; }
            }
        }

        sealed class MinHeap
        {
            readonly List<(int node, float cost)> items = new List<(int, float)>();
            public int Count => items.Count;
            public void Add(int node, float cost)
            {
                items.Add((node, cost)); int at = items.Count - 1;
                while (at > 0)
                {
                    int parent = (at - 1) / 2; if (items[parent].cost <= cost) break;
                    items[at] = items[parent]; at = parent;
                }
                items[at] = (node, cost);
            }
            public int Take()
            {
                int result = items[0].node; var last = items[items.Count - 1]; items.RemoveAt(items.Count - 1);
                if (items.Count == 0) return result;
                int at = 0;
                while (at * 2 + 1 < items.Count)
                {
                    int child = at * 2 + 1;
                    if (child + 1 < items.Count && items[child + 1].cost < items[child].cost) child++;
                    if (items[child].cost >= last.cost) break;
                    items[at] = items[child]; at = child;
                }
                items[at] = last; return result;
            }
        }
    }
}
