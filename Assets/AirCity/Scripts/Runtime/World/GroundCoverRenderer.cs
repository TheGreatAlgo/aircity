using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.World
{
    /// <summary>Spatial batches of shared nature meshes. No individual ground-prop GameObjects.</summary>
    [ExecuteAlways]
    public sealed class GroundCoverRenderer : MonoBehaviour
    {
        public GroundCoverLibrary library;
        public int InstanceCount { get; private set; }
        public IReadOnlyList<GroundCoverInstance> Instances { get; private set; } = System.Array.Empty<GroundCoverInstance>();
        public int LastDrawCalls { get; private set; }
        public int LastVisibleInstances { get; private set; }
        public const int TileSize = 256, TileCount = 25;

        sealed class Batch
        {
            public int model;
            public Bounds bounds;
            public Matrix4x4[] matrices;
        }
        sealed class Tile
        {
            public GroundCoverInstance[] instances;
            public readonly List<Batch> batches = new List<Batch>();
            public int rendered;
        }
        // Keep the inspection API without copying the valley's 400,000 candidates for a small tile edit.
        sealed class InstanceCollection : IReadOnlyList<GroundCoverInstance>
        {
            readonly Tile[] tiles;
            public InstanceCollection(Tile[] tiles) { this.tiles = tiles; }
            public int Count { get { int n = 0; foreach (var tile in tiles) if (tile != null) n += tile.instances.Length; return n; } }
            public GroundCoverInstance this[int index]
            {
                get
                {
                    if (index < 0) throw new System.ArgumentOutOfRangeException(nameof(index));
                    foreach (var tile in tiles)
                    {
                        if (tile == null) continue;
                        if (index < tile.instances.Length) return tile.instances[index];
                        index -= tile.instances.Length;
                    }
                    throw new System.ArgumentOutOfRangeException(nameof(index));
                }
            }
            public IEnumerator<GroundCoverInstance> GetEnumerator()
            { foreach (var tile in tiles) if (tile != null) foreach (var item in tile.instances) yield return item; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        readonly Tile[] tiles = new Tile[TileCount * TileCount];
        readonly Plane[] planes = new Plane[6];
        MaterialPropertyBlock[] nearProperties, farProperties;
        List<int>[] indices;

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Draw; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; }

        public void Rebuild(ValleyWorld world, System.Func<Vector2, float> heightAt = null, PublicRoadGeometry roads = null,
            IReadOnlyList<GroundCoverExclusion> exclusions = null)
        {
            System.Array.Clear(tiles, 0, tiles.Length); InstanceCount = 0; LastDrawCalls = 0; LastVisibleInstances = 0;
            Instances = System.Array.Empty<GroundCoverInstance>(); indices = null;
            if (library == null || library.models == null || library.models.Length == 0) return;
            int modelCount = library.models.Length;
            indices = new List<int>[5];
            for (int i = 0; i < indices.Length; i++) indices[i] = new List<int>();
            for (int i = 0; i < modelCount; i++)
                if (library.models[i].nearMesh != null && library.models[i].material != null) indices[(int)library.models[i].kind].Add(i);
            nearProperties = new MaterialPropertyBlock[modelCount]; farProperties = new MaterialPropertyBlock[modelCount];
            for (int i = 0; i < modelCount; i++)
            {
                var model = library.models[i];
                Range(model.kind, out Vector2 lod, out Vector2 fade);
                nearProperties[i] = Properties(lod, fade, model.farMesh != null && model.farMesh != model.nearMesh ? 1f : 0f);
                farProperties[i] = Properties(lod, fade, 2f);
            }
            var candidates = new List<GroundCoverInstance>[tiles.Length];
            foreach (var item in GroundCoverScatter.Generate(world, roads, exclusions))
            {
                int x = Mathf.Clamp((int)((item.position.x + ValleyWorld.Size * .5f) / TileSize), 0, TileCount - 1);
                int z = Mathf.Clamp((int)((item.position.y + ValleyWorld.Size * .5f) / TileSize), 0, TileCount - 1);
                int key = z * TileCount + x;
                candidates[key] ??= new List<GroundCoverInstance>(); candidates[key].Add(item);
            }
            Instances = new InstanceCollection(tiles);
            for (int i = 0; i < tiles.Length; i++)
                if (candidates[i] != null) ReplaceTile(i, candidates[i].ToArray(), world, heightAt);
        }

        public static Rect TileBounds(int key) => new Rect(key % TileCount * TileSize - ValleyWorld.Size * .5f,
            key / TileCount * TileSize - ValleyWorld.Size * .5f, TileSize, TileSize);

        public void RefreshTile(ValleyWorld world, int key, System.Func<Vector2, float> heightAt = null,
            PublicRoadGeometry roads = null, IReadOnlyList<GroundCoverExclusion> exclusions = null)
        {
            if (indices == null || library == null || library.models == null || library.models.Length == 0) return;
            ReplaceTile(key, GroundCoverScatter.Generate(world, roads, exclusions, TileBounds(key)), world, heightAt);
        }

        void ReplaceTile(int key, GroundCoverInstance[] candidates, ValleyWorld world, System.Func<Vector2, float> heightAt)
        {
            if (tiles[key] != null) InstanceCount -= tiles[key].rendered;
            var tile = new Tile { instances = candidates }; tiles[key] = tile;
            var groups = new List<Matrix4x4>[library.models.Length];
            foreach (var item in candidates)
            {
                var choices = indices[(int)item.kind]; if (choices.Count == 0) continue;
                int model = choices[(int)(item.variation % (uint)choices.Count)];
                var p = item.position;
                groups[model] ??= new List<Matrix4x4>();
                float h = heightAt != null ? heightAt(p) : world.Height(p);
                const float d = 6f;
                float dx = world.Height(p + Vector2.right * d) - world.Height(p - Vector2.right * d);
                float dz = world.Height(p + Vector2.up * d) - world.Height(p - Vector2.up * d);
                var normal = new Vector3(-dx, d * 2f, -dz).normalized;
                // Rocks settle into the slope; plants retain a mostly upright habit.
                var rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal),
                    item.kind == GroundCoverKind.Boulder || item.kind == GroundCoverKind.Stone ? .8f : .4f) * Quaternion.Euler(0f, item.yaw, 0f);
                float width = Mathf.Lerp(.88f, 1.12f, (item.variation & 255u) / 255f);
                groups[model].Add(Matrix4x4.TRS(new Vector3(p.x, h - .03f * item.scale, p.y), rotation,
                    new Vector3(item.scale * width, item.scale, item.scale / width)));
                tile.rendered++;
            }
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i]; if (group == null) continue;
                for (int start = 0; start < group.Count; start += 500)
                {
                    int count = Mathf.Min(500, group.Count - start);
                    var instances = new Matrix4x4[count]; group.CopyTo(start, instances, 0, count);
                    // Includes the largest generated boulder and plant extent, also on a slope.
                    var bounds = new Bounds(instances[0].GetColumn(3), Vector3.one * 18f);
                    for (int k = 1; k < count; k++) bounds.Encapsulate(new Bounds(instances[k].GetColumn(3), Vector3.one * 18f));
                    tile.batches.Add(new Batch { model = i, bounds = bounds, matrices = instances });
                }
            }
            InstanceCount += tile.rendered;
        }

        static MaterialPropertyBlock Properties(Vector2 lod, Vector2 fade, float role)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetVector("_FadeRange", new Vector4(fade.x, fade.y, 0f, 0f));
            properties.SetVector("_LodRange", new Vector4(lod.x, lod.y, 0f, 0f)); properties.SetFloat("_LodRole", role);
            return properties;
        }

        static void Range(GroundCoverKind kind, out Vector2 lod, out Vector2 fade)
        {
            switch (kind)
            {
                case GroundCoverKind.Boulder: lod = new Vector2(450f, 650f); fade = new Vector2(1800f, 2200f); break;
                case GroundCoverKind.Stone: lod = new Vector2(200f, 300f); fade = new Vector2(500f, 700f); break;
                case GroundCoverKind.Shrub: lod = new Vector2(150f, 220f); fade = new Vector2(450f, 600f); break;
                case GroundCoverKind.Fern: lod = new Vector2(90f, 135f); fade = new Vector2(220f, 300f); break;
                default: lod = new Vector2(70f, 105f); fade = new Vector2(160f, 230f); break;
            }
        }

        void Draw(ScriptableRenderContext context, Camera camera) => DrawForCamera(camera);

        public void DrawForCamera(Camera camera)
        {
            LastDrawCalls = 0; LastVisibleInstances = 0;
            if (library == null || library.models == null || camera == null || !isActiveAndEnabled ||
                !SystemInfo.supportsInstancing || (camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            var eye = camera.transform.position;
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                foreach (var batch in tile.batches)
                {
                    if (!GeometryUtility.TestPlanesAABB(planes, batch.bounds)) continue;
                    var model = library.models[batch.model]; Range(model.kind, out Vector2 lod, out Vector2 fade);
                    float near = Vector3.Distance(eye, batch.bounds.ClosestPoint(eye));
                    if (near >= fade.y) continue;
                    float far = Vector3.Distance(eye, batch.bounds.center) + batch.bounds.extents.magnitude;
                    bool hasFar = model.farMesh != null && model.farMesh != model.nearMesh;
                    var parameters = new RenderParams(model.material) {
                        camera = camera, worldBounds = batch.bounds, receiveShadows = true,
                        // Pebbles/grass receive light and shadow, but needn't add thousands of shadow-map triangles.
                        shadowCastingMode = near < 500f && (model.kind == GroundCoverKind.Boulder || model.kind == GroundCoverKind.Shrub) ?
                            ShadowCastingMode.On : ShadowCastingMode.Off,
                        lightProbeUsage = LightProbeUsage.Off, layer = gameObject.layer
                    };
                    if (!hasFar || near < lod.y)
                    {
                        parameters.matProps = nearProperties[batch.model];
                        Graphics.RenderMeshInstanced(parameters, model.nearMesh, 0, batch.matrices, batch.matrices.Length); LastDrawCalls++;
                    }
                    if (hasFar && far > lod.x)
                    {
                        parameters.matProps = farProperties[batch.model];
                        Graphics.RenderMeshInstanced(parameters, model.farMesh, 0, batch.matrices, batch.matrices.Length); LastDrawCalls++;
                    }
                    LastVisibleInstances += batch.matrices.Length;
                }
            }
        }
    }
}
