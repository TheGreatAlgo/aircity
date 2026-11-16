using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Spatial queries over saved, stationary tree roots. Removal flags are always read live.</summary>
    public static class ForestQuery
    {
        const float CellSize = 100f, MaximumPickRadius = 14f;
        const int Cells = 64;
        static readonly ConditionalWeakTable<ValleyWorld, Index> indices = new ConditionalWeakTable<ValleyWorld, Index>();

        sealed class Cell
        {
            public readonly List<int> trees = new List<int>();
            public Bounds pickBounds;
        }
        sealed class Index
        {
            public List<ForestTree> source;
            public int count;
            public Cell[] cells;
            public readonly List<Cell> occupied = new List<Cell>();
        }

        static Index Get(ValleyWorld world)
        {
            var index = indices.GetValue(world, _ => new Index());
            if (ReferenceEquals(index.source, world.trees) && index.count == world.trees.Count && index.cells != null) return index;
            index.source = world.trees; index.count = world.trees.Count;
            index.cells = new Cell[Cells * Cells]; index.occupied.Clear();
            for (int i = 0; i < world.trees.Count; i++)
            {
                var tree = world.trees[i];
                int x = Coordinate(tree.position.x), z = Coordinate(tree.position.y), key = z * Cells + x;
                var cell = index.cells[key];
                if (cell == null)
                {
                    // Valid worlds constrain terrain to 0..HeightRange and trees to at most 40 m.
                    // Expand every cell for the picking sphere, including roots on its outer edges.
                    cell = new Cell { pickBounds = new Bounds(
                        new Vector3((x + .5f) * CellSize - ValleyWorld.Size * .5f, ValleyWorld.HeightRange * .5f + 20f,
                            (z + .5f) * CellSize - ValleyWorld.Size * .5f),
                        new Vector3(CellSize + MaximumPickRadius * 2f, ValleyWorld.HeightRange + 120f,
                            CellSize + MaximumPickRadius * 2f)) };
                    index.cells[key] = cell; index.occupied.Add(cell);
                }
                cell.trees.Add(i);
            }
            return index;
        }

        static int Coordinate(float value) => Mathf.Clamp(Mathf.FloorToInt((value + ValleyWorld.Size * .5f) / CellSize), 0, Cells - 1);

        /// <summary>Broad-phase candidates, including boundary cells. Callers retain their exact footprint test.</summary>
        public static IEnumerable<int> Candidates(ValleyWorld world, Rect bounds)
        {
            var index = Get(world);
            int x0 = Coordinate(bounds.xMin), x1 = Coordinate(bounds.xMax);
            int z0 = Coordinate(bounds.yMin), z1 = Coordinate(bounds.yMax);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                var cell = index.cells[z * Cells + x];
                if (cell == null) continue;
                foreach (int tree in cell.trees) yield return tree;
            }
        }

        public static bool AnyWithin(ValleyWorld world, Vector2 point, float radius)
        {
            var bounds = new Rect(point.x - radius, point.y - radius, radius * 2f, radius * 2f);
            foreach (int i in Candidates(world, bounds))
            {
                var tree = world.trees[i];
                if (!tree.removed && Vector2.Distance(tree.position, point) < radius) return true;
            }
            return false;
        }

        /// <summary>Same screen-ray picking rule as the construction tool, with coarse cell rejection first.</summary>
        public static int Pick(ValleyWorld world, Ray ray, Func<Vector2, float> heightAt, Func<Vector2, bool> editable = null)
        {
            int best = -1; float near = float.MaxValue;
            foreach (var cell in Get(world).occupied)
            {
                if (!cell.pickBounds.IntersectRay(ray)) continue;
                foreach (int i in cell.trees)
                {
                    var tree = world.trees[i];
                    if (tree.removed || editable != null && !editable(tree.position)) continue;
                    var centre = new Vector3(tree.position.x, heightAt(tree.position) + tree.height * .55f, tree.position.y);
                    float along = Vector3.Dot(centre - ray.origin, ray.direction);
                    if (along < 0f || along > near || along == near && best >= 0 && i > best) continue;
                    if (Vector3.Distance(ray.GetPoint(along), centre) > tree.height * .35f) continue;
                    near = along; best = i;
                }
            }
            return best;
        }
    }
}
