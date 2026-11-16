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
        public long LastSubmittedTriangles { get; private set; }
        public int LastRockInstances { get; private set; }
        public int LastDebrisInstances { get; private set; }
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
            IReadOnlyList<GroundCoverExclusion> exclusions = null, BuiltSurfaceMap surfaces = null)
        {
            System.Array.Clear(tiles, 0, tiles.Length); InstanceCount = 0; LastDrawCalls = 0; LastVisibleInstances = 0;
            LastSubmittedTriangles = 0; LastRockInstances = LastDebrisInstances = 0;
            Instances = System.Array.Empty<GroundCoverInstance>(); indices = null;
            if (library == null || library.models == null || library.models.Length == 0) return;
            int modelCount = library.models.Length;
            indices = new List<int>[System.Enum.GetValues(typeof(GroundCoverKind)).Length];
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
            foreach (var item in GroundCoverScatter.Generate(world, roads, exclusions, surfaces:surfaces))
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

        /// <summary>Only visit intersecting spatial tiles; brush previews never scan the whole region.</summary>
        public IEnumerable<GroundCoverInstance> QueryBounds(Rect area)
        {
            int x0 = Coordinate(area.xMin), x1 = Coordinate(area.xMax);
            int z0 = Coordinate(area.yMin), z1 = Coordinate(area.yMax);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                var tile = tiles[z * TileCount + x];
                if (tile == null) continue;
                foreach (var item in tile.instances) if (area.Contains(item.position)) yield return item;
            }
        }

        /// <summary>Remove paid-for saved slots immediately, keeping every other tile and the collection stable.</summary>
        public void RefreshCleared(ValleyWorld world, Rect? changedArea, System.Func<Vector2, float> heightAt = null)
        {
            var area = changedArea ?? world.RegionBounds;
            float margin = GroundCoverScatter.MaximumFootprintRadius;
            int x0 = Coordinate(area.xMin - margin), x1 = Coordinate(area.xMax + margin);
            int z0 = Coordinate(area.yMin - margin), z1 = Coordinate(area.yMax + margin);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int key = z * TileCount + x; var tile = tiles[key];
                if (tile == null) continue;
                bool changed = false;
                foreach (var item in tile.instances) if (world.GroundPropRemoved(item.id)) { changed = true; break; }
                if (!changed) continue;
                var kept = new List<GroundCoverInstance>(tile.instances.Length);
                foreach (var item in tile.instances) if (!world.GroundPropRemoved(item.id)) kept.Add(item);
                ReplaceTile(key, kept.ToArray(), world, heightAt);
            }
        }

        /// <summary>Hide every overlapping model immediately; queued re-scattering uses the same ownership map.</summary>
        public void RefreshBuiltSurfaces(ValleyWorld world,BuiltSurfaceMap surfaces,Rect area,System.Func<Vector2,float> heightAt)
        {
            float margin=GroundCoverScatter.MaximumFootprintRadius;
            for(int z=Coordinate(area.yMin-margin);z<=Coordinate(area.yMax+margin);z++)
            for(int x=Coordinate(area.xMin-margin);x<=Coordinate(area.xMax+margin);x++)
            {
                int key=z*TileCount+x;var tile=tiles[key];if(tile==null)continue;
                var kept=surfaces.Exclude(tile.instances);
                if(kept.Length!=tile.instances.Length)ReplaceTile(key,kept,world,heightAt);
            }
        }

        static int Coordinate(float position) => Mathf.Clamp(Mathf.FloorToInt((position + ValleyWorld.Size * .5f) / TileSize), 0, TileCount - 1);

        public void RefreshTile(ValleyWorld world, int key, System.Func<Vector2, float> heightAt = null,
            PublicRoadGeometry roads = null, IReadOnlyList<GroundCoverExclusion> exclusions = null, BuiltSurfaceMap surfaces = null)
        {
            if (indices == null || library == null || library.models == null || library.models.Length == 0) return;
            ReplaceTile(key, GroundCoverScatter.Generate(world, roads, exclusions, TileBounds(key), surfaces), world, heightAt);
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
                // Rocks and fallen wood settle into the slope; plants retain a mostly upright habit.
                var rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal),
                    item.kind == GroundCoverKind.Boulder || item.kind == GroundCoverKind.Stone || item.kind == GroundCoverKind.Log ? .9f : .4f) * Quaternion.Euler(0f, item.yaw, 0f);
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
                    // Include each transformed model's actual footprint, including larger outcrops/logs.
                    var bounds = TransformedBounds(library.models[i].bounds, instances[0]);
                    for (int k = 1; k < count; k++) bounds.Encapsulate(TransformedBounds(library.models[i].bounds, instances[k]));
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

        static Bounds TransformedBounds(Bounds local, Matrix4x4 matrix)
        {
            var x = matrix.MultiplyVector(new Vector3(local.extents.x, 0f, 0f));
            var y = matrix.MultiplyVector(new Vector3(0f, local.extents.y, 0f));
            var z = matrix.MultiplyVector(new Vector3(0f, 0f, local.extents.z));
            var size = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f;
            return new Bounds(matrix.MultiplyPoint3x4(local.center), size + Vector3.one * .25f);
        }

        static void Range(GroundCoverKind kind, out Vector2 lod, out Vector2 fade)
        {
            switch (kind)
            {
                case GroundCoverKind.Boulder: lod = new Vector2(260f, 400f); fade = new Vector2(2000f, 2500f); break;
                case GroundCoverKind.Stone: lod = new Vector2(200f, 300f); fade = new Vector2(500f, 700f); break;
                case GroundCoverKind.Log: lod = new Vector2(160f, 240f); fade = new Vector2(800f, 1100f); break;
                case GroundCoverKind.Shrub: lod = new Vector2(110f, 180f); fade = new Vector2(500f, 700f); break;
                case GroundCoverKind.Fern: lod = new Vector2(75f, 115f); fade = new Vector2(280f, 400f); break;
                default: lod = new Vector2(70f, 105f); fade = new Vector2(160f, 230f); break;
            }
        }

        void Draw(ScriptableRenderContext context, Camera camera) => DrawForCamera(camera);

        public void DrawForCamera(Camera camera)
        {
            LastDrawCalls = 0; LastVisibleInstances = 0; LastSubmittedTriangles = 0; LastRockInstances = LastDebrisInstances = 0;
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
                        shadowCastingMode = near < 350f && (model.kind == GroundCoverKind.Boulder || model.kind == GroundCoverKind.Shrub || model.kind == GroundCoverKind.Log) ?
                            ShadowCastingMode.On : ShadowCastingMode.Off,
                        lightProbeUsage = LightProbeUsage.Off, layer = gameObject.layer
                    };
                    if (!hasFar || near < lod.y)
                    {
                        parameters.matProps = nearProperties[batch.model];
                        Graphics.RenderMeshInstanced(parameters, model.nearMesh, 0, batch.matrices, batch.matrices.Length); LastDrawCalls++;
                        LastSubmittedTriangles += model.nearMesh.GetIndexCount(0) / 3L * batch.matrices.Length;
                    }
                    if (hasFar && far > lod.x)
                    {
                        parameters.matProps = farProperties[batch.model];
                        Graphics.RenderMeshInstanced(parameters, model.farMesh, 0, batch.matrices, batch.matrices.Length); LastDrawCalls++;
                        LastSubmittedTriangles += model.farMesh.GetIndexCount(0) / 3L * batch.matrices.Length;
                    }
                    LastVisibleInstances += batch.matrices.Length;
                    if (model.kind == GroundCoverKind.Boulder || model.kind == GroundCoverKind.Stone) LastRockInstances += batch.matrices.Length;
                    if (model.kind == GroundCoverKind.Log) LastDebrisInstances += batch.matrices.Length;
                }
            }
        }
    }
}
