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
        sealed class Batch
        {
            public int variant;
            public Bounds bounds;
            public Matrix4x4[] matrices;
        }
        readonly List<Batch> batches = new List<Batch>();
        readonly Plane[] planes = new Plane[6];

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Draw; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; }

        public void Rebuild(ValleyWorld world, System.Func<Vector2, float> heightAt = null)
        {
            batches.Clear(); TreeCount = 0;
            if (library == null || library.variants == null || library.variants.Length == 0) return;
            int variants = library.variants.Length;
            var groups = new List<Matrix4x4>[64 * variants];
            var rng = new WorldRandom(world.seed ^ 914853);
            for (int index = 0; index < world.trees.Count; index++)
            {
                var tree = world.trees[index];
                // Consume the same random values for removed trees, so clearing never changes survivors.
                int variant = Mathf.Min(variants - 1, (int)(rng.Next() * variants));
                float yaw = rng.Next() * 360f, width = Mathf.Lerp(.83f, 1.13f, rng.Next());
                if (tree.removed) continue;
                var p = tree.position;
                int tileX = Mathf.Clamp((int)((p.x + ValleyWorld.Size * .5f) / 800f), 0, 7);
                int tileZ = Mathf.Clamp((int)((p.y + ValleyWorld.Size * .5f) / 800f), 0, 7);
                int group = (tileZ * 8 + tileX) * variants + variant;
                groups[group] ??= new List<Matrix4x4>();
                float height = tree.height / library.variants[variant].height;
                groups[group].Add(Matrix4x4.TRS(new Vector3(p.x, heightAt != null ? heightAt(p) : world.Height(p), p.y), Quaternion.Euler(0f, yaw, 0f),
                    new Vector3(height * width, height, height * width)));
                TreeCount++;
            }
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i]; if (group == null) continue;
                // Unity's instance buffer also stores inverse transforms for our nonuniform scales: 511 is portable.
                for (int start = 0; start < group.Count; start += 500)
                {
                    int count = Mathf.Min(500, group.Count - start);
                    var instances = new Matrix4x4[count]; group.CopyTo(start, instances, 0, count);
                    var bounds = new Bounds(instances[0].GetColumn(3), Vector3.one * 70f);
                    for (int k = 1; k < count; k++) bounds.Encapsulate(new Bounds(instances[k].GetColumn(3), Vector3.one * 70f));
                    batches.Add(new Batch { variant = i % variants, bounds = bounds, matrices = instances });
                }
            }
        }

        /// <summary>Retain surviving trees and their transforms; only re-seat trees beside an edited height patch.</summary>
        public void RefreshHeights(Rect area, System.Func<Vector2, float> heightAt)
        {
            foreach (var batch in batches)
            {
                var min = batch.bounds.min; var max = batch.bounds.max;
                if (!area.Overlaps(Rect.MinMaxRect(min.x, min.z, max.x, max.z))) continue;
                bool changed = false;
                for (int i = 0; i < batch.matrices.Length; i++)
                {
                    var matrix = batch.matrices[i];
                    var p = new Vector2(matrix.m03, matrix.m23);
                    if (!area.Contains(p)) continue;
                    matrix.m13 = heightAt(p); batch.matrices[i] = matrix; changed = true;
                }
                if (!changed) continue;
                batch.bounds = new Bounds(batch.matrices[0].GetColumn(3), Vector3.one * 70f);
                for (int i = 1; i < batch.matrices.Length; i++)
                    batch.bounds.Encapsulate(new Bounds(batch.matrices[i].GetColumn(3), Vector3.one * 70f));
            }
        }

        void Draw(ScriptableRenderContext context, Camera camera) => DrawForCamera(camera);

        public void DrawForCamera(Camera camera)
        {
            if (library == null || camera == null || !isActiveAndEnabled || !SystemInfo.supportsInstancing || (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            GeometryUtility.CalculateFrustumPlanes(camera, planes); LastDrawCalls = 0;
            foreach (var batch in batches)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, batch.bounds)) continue;
                float distance = Vector3.Distance(camera.transform.position, batch.bounds.ClosestPoint(camera.transform.position));
                int level = distance < 250f ? 0 : distance < 850f ? 1 : 2;
                var mesh = library.variants[batch.variant].levels[level];
                for (int submesh = 0; submesh < 2; submesh++)
                {
                    if (mesh.GetIndexCount(submesh) == 0) continue;
                    var material = submesh == 0 ? library.bark : level == 2 && library.distantFoliage != null ? library.distantFoliage : library.foliage;
                    var parameters = new RenderParams(material) {
                        camera = camera, worldBounds = batch.bounds, receiveShadows = true,
                        shadowCastingMode = distance < 900f ? ShadowCastingMode.On : ShadowCastingMode.Off,
                        lightProbeUsage = LightProbeUsage.Off, layer = gameObject.layer
                    };
                    Graphics.RenderMeshInstanced(parameters, mesh, submesh, batch.matrices, batch.matrices.Length);
                    LastDrawCalls++;
                }
            }
        }
    }
}
