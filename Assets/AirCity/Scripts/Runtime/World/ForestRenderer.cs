using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.World
{
    /// <summary>Shared tree assets, spatial culling and instancing. No individual tree GameObjects or copied meshes.</summary>
    [ExecuteAlways]
    public sealed class ForestRenderer : MonoBehaviour
    {
        public TreeLibrary library;
        public int TreeCount { get; private set; }
        public int LastDrawCalls { get; private set; }
        public int LastSubmittedTrees { get; private set; }
        public int LastFullDetailTrees { get; private set; }
        public int LastMediumDetailTrees { get; private set; }
        public int LastImpostorTrees { get; private set; }
        public long LastSubmittedTriangles { get; private set; }
        public int LastRemovalCheckedTrees { get; private set; }
        const float DetailTileSize = 200f, DistanceTileSize = 800f;
        const float FullDetailDistance = 90f, ImpostorDistance = 360f, ShadowDistance = 180f;
        const int InstanceLimit = 500;
        sealed class Batch
        {
            public int variant;
            public Bounds bounds;
            public Matrix4x4[] matrices;
            public int[] treeIndices;
        }
        struct Instance { public Matrix4x4 matrix; public int treeIndex; }
        readonly List<Batch> batches = new List<Batch>();
        // Fine cells only matter close to the camera. Far cells submit the same trees in larger buffers,
        // keeping a whole-region view inexpensive without retaining detailed meshes over an 800 m cell.
        sealed class Cell
        {
            public Bounds bounds;
            public readonly List<Batch> detail = new List<Batch>(), distant = new List<Batch>();
        }
        readonly List<Cell> cells = new List<Cell>();
        readonly Plane[] planes = new Plane[6];
        TreeLibrary activeLibrary, paletteSource;
        RegionTreePalette activePalette;
        RegionPreset sourcePreset;
        readonly Dictionary<RegionPreset, RegionTreePalette> palettes = new Dictionary<RegionPreset, RegionTreePalette>();
        Bounds[] variantBounds;
        ValleyWorld sourceWorld;
        List<ForestTree> sourceTrees;
        int sourceTreeCount;

        void OnDestroy()
        {
            foreach (var palette in palettes.Values) palette?.Dispose();
            palettes.Clear();
        }

        RegionTreePalette Palette(RegionPreset preset)
        {
            if (paletteSource != library)
            {
                foreach (var palette in palettes.Values) palette?.Dispose();
                palettes.Clear(); paletteSource = library;
            }
            if (!palettes.TryGetValue(preset, out var result))
            { result = RegionTreePalette.Create(library, preset); palettes.Add(preset, result); }
            return result;
        }

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Draw; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; }

        public void Rebuild(ValleyWorld world, System.Func<Vector2, float> heightAt = null)
        {
            batches.Clear(); cells.Clear(); TreeCount = 0;
            sourceWorld = world; sourceTrees = world.trees; sourceTreeCount = world.trees.Count;
            sourcePreset = world.preset;
            activePalette = Palette(world.preset); activeLibrary = activePalette?.Library;
            if (activeLibrary == null || activeLibrary.variants == null || activeLibrary.variants.Length == 0) return;
            int variants = activeLibrary.variants.Length;
            variantBounds = new Bounds[variants];
            for (int i = 0; i < variants; i++)
            {
                var bounds = activeLibrary.variants[i].levels[0].bounds;
                foreach (var level in activeLibrary.variants[i].levels) bounds.Encapsulate(level.bounds);
                bounds.Expand(2f); // Include animated branch movement at every level.
                variantBounds[i] = bounds;
            }
            int side = Mathf.CeilToInt(ValleyWorld.Size / DetailTileSize);
            int distantSide = Mathf.CeilToInt(ValleyWorld.Size / DistanceTileSize);
            var groups = new List<Instance>[side * side * variants];
            var distantGroups = new Cell[distantSide * distantSide * variants];
            var rng = new WorldRandom(world.seed ^ 914853);
            for (int index = 0; index < world.trees.Count; index++)
            {
                var tree = world.trees[index];
                // Consume the same random values for removed trees, so clearing never changes survivors.
                var p = tree.position;
                int variant = activePalette.Variant(world, p, rng.Next());
                float yaw = rng.Next() * 360f, width = Mathf.Lerp(.83f, 1.13f, rng.Next());
                if (tree.removed) continue;
                int tileX = Mathf.Clamp((int)((p.x + ValleyWorld.Size * .5f) / DetailTileSize), 0, side - 1);
                int tileZ = Mathf.Clamp((int)((p.y + ValleyWorld.Size * .5f) / DetailTileSize), 0, side - 1);
                int group = (tileZ * side + tileX) * variants + variant;
                groups[group] ??= new List<Instance>();
                float height = tree.height / activeLibrary.variants[variant].height;
                groups[group].Add(new Instance { treeIndex = index, matrix = Matrix4x4.TRS(
                    new Vector3(p.x, heightAt != null ? heightAt(p) : world.Height(p), p.y), Quaternion.Euler(0f, yaw, 0f),
                    new Vector3(height * width, height, height * width)) });
                TreeCount++;
            }
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i]; if (group == null) continue;
                int tile = i / variants, variant = i % variants;
                int distantX = (int)(tile % side * DetailTileSize / DistanceTileSize);
                int distantZ = (int)(tile / side * DetailTileSize / DistanceTileSize);
                int distantIndex = (distantZ * distantSide + distantX) * variants + variant;
                var cell = distantGroups[distantIndex] ??= new Cell();
                // Unity's instance buffer also stores inverse transforms for our nonuniform scales: 511 is portable.
                for (int start = 0; start < group.Count; start += InstanceLimit)
                {
                    int count = Mathf.Min(InstanceLimit, group.Count - start);
                    var matrices = new Matrix4x4[count]; var treeIndices = new int[count];
                    for (int j = 0; j < count; j++)
                    { matrices[j] = group[start + j].matrix; treeIndices[j] = group[start + j].treeIndex; }
                    var batch = new Batch { variant = variant, matrices = matrices, treeIndices = treeIndices };
                    RecalculateBounds(batch); batches.Add(batch); cell.detail.Add(batch);
                }
            }
            foreach (var cell in distantGroups)
            {
                if (cell == null) continue;
                cell.bounds = cell.detail[0].bounds;
                foreach (var batch in cell.detail) cell.bounds.Encapsulate(batch.bounds);
                // Retain a second, densely packed transform buffer, never a second set of tree meshes.
                // Matrices plus stable saved indices cost 68 bytes/tree; fine-cell edges do not multiply far draws.
                var instances = new List<Matrix4x4>();
                var indices = new List<int>();
                foreach (var batch in cell.detail) { instances.AddRange(batch.matrices); indices.AddRange(batch.treeIndices); }
                for (int start = 0; start < instances.Count; start += InstanceLimit)
                {
                    var matrices = new Matrix4x4[Mathf.Min(InstanceLimit, instances.Count - start)];
                    instances.CopyTo(start, matrices, 0, matrices.Length);
                    var treeIndices = new int[matrices.Length]; indices.CopyTo(start, treeIndices, 0, treeIndices.Length);
                    var batch = new Batch { variant = cell.detail[0].variant, matrices = matrices, treeIndices = treeIndices };
                    RecalculateBounds(batch); cell.distant.Add(batch);
                }
                cells.Add(cell);
            }
        }

        /// <summary>Compact only buffers containing cleared trees. Appended populations need a full rebuild.</summary>
        public void RefreshRemoved(ValleyWorld world, System.Func<Vector2, float> heightAt = null, Rect? area = null)
        {
            LastRemovalCheckedTrees = 0;
            if (world != sourceWorld || world.trees != sourceTrees || world.trees.Count != sourceTreeCount ||
                sourcePreset != world.preset || paletteSource != library)
            { Rebuild(world, heightAt); return; }
            foreach (var cell in cells)
            {
                if (area.HasValue && !Overlaps(cell.bounds, area.Value)) continue;
                bool changed = false;
                foreach (var batch in cell.detail)
                {
                    if (area.HasValue && !Overlaps(batch.bounds, area.Value)) continue;
                    int removed = RemoveFromBatch(batch, world);
                    TreeCount -= removed; changed |= removed > 0;
                }
                if (!changed) continue;
                // A distant buffer spans the entire cell, so compact all its buffers after a local change.
                foreach (var batch in cell.distant) RemoveFromBatch(batch, world);
                RefreshCellBounds(cell);
            }
        }

        int RemoveFromBatch(Batch batch, ValleyWorld world)
        {
            int remaining = 0;
            LastRemovalCheckedTrees += batch.treeIndices.Length;
            for (int i = 0; i < batch.treeIndices.Length; i++) if (!world.trees[batch.treeIndices[i]].removed) remaining++;
            int removed = batch.treeIndices.Length - remaining;
            if (removed == 0) return 0;
            var matrices = new Matrix4x4[remaining]; var indices = new int[remaining];
            int target = 0;
            for (int i = 0; i < batch.treeIndices.Length; i++)
            {
                if (world.trees[batch.treeIndices[i]].removed) continue;
                matrices[target] = batch.matrices[i]; indices[target++] = batch.treeIndices[i];
            }
            batch.matrices = matrices; batch.treeIndices = indices;
            if (remaining > 0) RecalculateBounds(batch);
            return removed;
        }

        static bool Overlaps(Bounds bounds, Rect area)
        { var min = bounds.min; var max = bounds.max; return area.Overlaps(Rect.MinMaxRect(min.x, min.z, max.x, max.z)); }

        static void RefreshCellBounds(Cell cell)
        {
            bool first = true;
            foreach (var batch in cell.detail)
            {
                if (batch.matrices.Length == 0) continue;
                if (first) { cell.bounds = batch.bounds; first = false; }
                else cell.bounds.Encapsulate(batch.bounds);
            }
        }

        /// <summary>Retain surviving trees and their transforms; only re-seat trees beside an edited height patch.</summary>
        public void RefreshHeights(Rect area, System.Func<Vector2, float> heightAt)
        {
            foreach (var cell in cells)
            {
                var min = cell.bounds.min; var max = cell.bounds.max;
                if (!area.Overlaps(Rect.MinMaxRect(min.x, min.z, max.x, max.z))) continue;
                foreach (var batch in cell.detail) RefreshBatch(batch, area, heightAt);
                foreach (var batch in cell.distant) RefreshBatch(batch, area, heightAt);
                RefreshCellBounds(cell);
            }
        }

        void RefreshBatch(Batch batch, Rect area, System.Func<Vector2, float> heightAt)
        {
            var min = batch.bounds.min; var max = batch.bounds.max;
            if (!area.Overlaps(Rect.MinMaxRect(min.x, min.z, max.x, max.z))) return;
            bool changed = false;
            for (int i = 0; i < batch.matrices.Length; i++)
            {
                var matrix = batch.matrices[i];
                var p = new Vector2(matrix.m03, matrix.m23);
                if (!area.Contains(p)) continue;
                matrix.m13 = heightAt(p); batch.matrices[i] = matrix; changed = true;
            }
            if (changed) RecalculateBounds(batch);
        }

        void RecalculateBounds(Batch batch)
        {
            var local = variantBounds[batch.variant];
            for (int i = 0; i < batch.matrices.Length; i++)
            {
                var matrix = batch.matrices[i];
                var x = matrix.MultiplyVector(new Vector3(local.extents.x, 0f, 0f));
                var y = matrix.MultiplyVector(new Vector3(0f, local.extents.y, 0f));
                var z = matrix.MultiplyVector(new Vector3(0f, 0f, local.extents.z));
                var extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                var bounds = new Bounds(matrix.MultiplyPoint3x4(local.center), extents * 2f);
                if (i == 0) batch.bounds = bounds;
                else batch.bounds.Encapsulate(bounds);
            }
        }

        void Draw(ScriptableRenderContext context, Camera camera) => DrawForCamera(camera);

        public void DrawForCamera(Camera camera)
        {
            LastDrawCalls = 0; LastSubmittedTrees = 0; LastFullDetailTrees = 0;
            LastMediumDetailTrees = 0; LastImpostorTrees = 0; LastSubmittedTriangles = 0;
            if (activeLibrary == null || camera == null || !isActiveAndEnabled || !SystemInfo.supportsInstancing || (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            foreach (var cell in cells)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, cell.bounds)) continue;
                float distance = Vector3.Distance(camera.transform.position, cell.bounds.ClosestPoint(camera.transform.position));
                bool distant = distance >= ImpostorDistance;
                foreach (var batch in distant ? cell.distant : cell.detail)
                {
                    if (!GeometryUtility.TestPlanesAABB(planes, batch.bounds)) continue;
                    float batchDistance = Vector3.Distance(camera.transform.position, batch.bounds.ClosestPoint(camera.transform.position));
                    int level = distant ? 2 : batchDistance < FullDetailDistance ? 0 : batchDistance < ImpostorDistance ? 1 : 2;
                    DrawBatch(batch, level, batchDistance, camera);
                }
            }
        }

        void DrawBatch(Batch batch, int level, float distance, Camera camera)
        {
            var variant = activeLibrary.variants[batch.variant];
            var mesh = variant.levels[level];
            int count = batch.matrices.Length;
            if (count == 0) return;
            LastSubmittedTrees += count;
            if (level == 0) LastFullDetailTrees += count;
            else if (level == 1) LastMediumDetailTrees += count;
            else LastImpostorTrees += count;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                uint indices = mesh.GetIndexCount(submesh);
                if (indices == 0) continue;
                var material = submesh == 0 ? variant.bark != null ? variant.bark : activeLibrary.bark :
                    level == 2 && variant.distantFoliage != null ? variant.distantFoliage :
                    level == 2 && activeLibrary.distantFoliage != null ? activeLibrary.distantFoliage :
                    variant.foliage != null ? variant.foliage : activeLibrary.foliage;
                var parameters = new RenderParams(material) {
                    camera = camera, worldBounds = batch.bounds, receiveShadows = true,
                    shadowCastingMode = distance < ShadowDistance ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    lightProbeUsage = LightProbeUsage.Off, layer = gameObject.layer
                };
                Graphics.RenderMeshInstanced(parameters, mesh, submesh, batch.matrices, count);
                LastSubmittedTriangles += indices / 3L * count; LastDrawCalls++;
            }
        }
    }
}
