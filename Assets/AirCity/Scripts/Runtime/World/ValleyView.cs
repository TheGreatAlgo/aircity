using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.World
{
    /// <summary>Disposable presentation of a world snapshot, with spatially instanced forests and separate public roads.</summary>
    public class ValleyView : MonoBehaviour
    {
        public Material terrainMaterial, sceneryMaterial;
        public Texture2D grassTexture, dirtTexture;
        public TerrainLayer grassLayer, dirtLayer, roadLayer, dryLayer, rockLayer, forestLayer;
        public TreeLibrary trees;
        public GroundCoverLibrary groundCover;
        public PublicRoadProfile ruralRoad, airportRoad;
        public Material bridgeMaterial;
        public GameObject trafficSignalPrefab;
        public Terrain Terrain { get; private set; }
        public ValleyWorld World { get; private set; }
        readonly List<Object> owned = new List<Object>();
        Material water, roof, wall, paint;
        TerrainData data;
        AirportSite roadConnection;
        PublicRoadNetwork roadNetwork;
        PublicRoadGeometry roadGeometry;
        GameObject roadRoot;
        ForestRenderer forest;
        GroundCoverRenderer groundDetails;
        readonly List<GroundCoverExclusion> groundExclusions = new List<GroundCoverExclusion>();
        const int SurfaceTileSize = 32;
        readonly Queue<int> dirtySurface = new Queue<int>();
        readonly HashSet<int> queuedSurface = new HashSet<int>();
        readonly Queue<int> dirtyGroundCover = new Queue<int>();
        readonly HashSet<int> queuedGroundCover = new HashSet<int>();
        PublicRoadSignalsView signals;
        public PublicRoadNetwork RoadNetwork => roadNetwork;
        public int PendingSurfaceTiles => dirtySurface.Count;
        public int PendingGroundCoverTiles => dirtyGroundCover.Count;

        public void Build(ValleyWorld world, PublicRoadNetwork savedRoads = null, bool refreshGroundCover = true)
        {
            Dispose(); groundExclusions.Clear(); World = world; roadConnection = null;
            water = Material("River water", new Color(.12f, .39f, .43f), .8f);
            roof = Material("Settlement roofs", new Color(.28f, .32f, .35f));
            wall = Material("Settlement walls", new Color(.69f, .64f, .52f));
            paint = Material("Road markings", new Color(.83f, .80f, .64f));
            data = Own(new TerrainData { name = "Saved valley terrain", heightmapResolution = ValleyWorld.Resolution,
                size = new Vector3(ValleyWorld.Size, ValleyWorld.HeightRange, ValleyWorld.Size), alphamapResolution = 1024 });
            var grass = grassLayer != null ? grassLayer : Own(new TerrainLayer { name = "Valley grass", diffuseTexture = grassTexture, tileSize = new Vector2(16f, 16f) });
            var dirt = dirtLayer != null ? dirtLayer : Own(new TerrainLayer { name = "Exposed ground", diffuseTexture = dirtTexture, tileSize = new Vector2(20f, 20f) });
            var paving = roadLayer != null ? roadLayer : Own(new TerrainLayer { name = "Road surface", diffuseTexture = dirtTexture, tileSize = new Vector2(12f, 12f) });
            data.terrainLayers = new[] { grass, dirt, paving, dryLayer != null ? dryLayer : grass, rockLayer != null ? rockLayer : dirt, forestLayer != null ? forestLayer : dirt };
            roadNetwork = savedRoads ?? PublicRoadNetwork.FromWorld(world);
            BuildRoadGeometry(); SetHeights();
            var terrainObject = UnityEngine.Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Valley landscape"; terrainObject.transform.SetParent(transform, false);
            terrainObject.transform.localPosition = new Vector3(-ValleyWorld.Size * .5f, 0f, -ValleyWorld.Size * .5f);
            Terrain = terrainObject.GetComponent<Terrain>();
            Terrain.materialTemplate = terrainMaterial;
            Terrain.heightmapPixelError = 1f; Terrain.basemapDistance = 5000f; Terrain.drawInstanced = true;
            Terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;
            PaintRoads(); Terrain.Flush();
            BuildRiver(); BuildSettlements(); RebuildForest(); if (refreshGroundCover) RebuildGroundCover();
        }

        public void RefreshLand(bool repaint = true)
        {
            BuildRoadGeometry(); SetHeights(); if (repaint) { PaintRoads(); RebuildGroundCover(); } RebuildForest();
        }

        /// <summary>A brush changes height samples, not the public-road layout or the rest of the valley.</summary>
        public void RefreshLand(Rect brushBounds)
        {
            if (data == null || World == null) return;
            var region = ValleySurface.PixelBounds(brushBounds, data.heightmapResolution);
            if (region.width == 0 || region.height == 0) return;
            var h = roadGeometry.GradedHeightRegion(World, region);
            for (int z = 0; z < region.height; z++)
            for (int x = 0; x < region.width; x++) h[z, x] /= ValleyWorld.HeightRange;
            data.SetHeights(region.x, region.y, h);
            // Bilinear height interpolation reaches one height sample beyond the actual edit.
            float margin = World.Spacing;
            if (forest != null) forest.RefreshHeights(Rect.MinMaxRect(brushBounds.xMin - margin, brushBounds.yMin - margin,
                brushBounds.xMax + margin, brushBounds.yMax + margin), DisplayHeight);
        }

        void SetHeights()
        {
            var h = new float[ValleyWorld.Resolution, ValleyWorld.Resolution];
            var roadbed = roadGeometry.GradedHeights(World);
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
                h[z, x] = roadbed[z * ValleyWorld.Resolution + x] / ValleyWorld.HeightRange;
            data.SetHeights(0, 0, h);
        }

        void BuildRiver()
        {
            var mesh = new LandscapeMesh();
            for (int i = 0; i < 512; i++)
            {
                float z0 = -ValleyWorld.Size * .5f + i * ValleyWorld.Size / 512f, z1 = z0 + ValleyWorld.Size / 512f;
                float x0 = World.RiverX(z0), x1 = World.RiverX(z1);
                mesh.Quad(new Vector3(x0 - 120f, World.WaterHeight(z0), z0), new Vector3(x1 - 120f, World.WaterHeight(z1), z1),
                    new Vector3(x1 + 120f, World.WaterHeight(z1), z1), new Vector3(x0 + 120f, World.WaterHeight(z0), z0));
            }
            MakeMesh("River", mesh, water);
        }

        public GameObject BuildAccess(AirportSite site, PublicRoadNetwork savedRoads = null, bool refreshGroundCover = true)
        {
            roadConnection = site;
            roadNetwork = savedRoads ?? PublicRoadNetwork.FromWorld(World);
            if (savedRoads == null && !roadNetwork.AddAirportAccess(World, site, PublicRoadLayout.Suggested(site), out string why))
                throw new System.InvalidOperationException(why);
            BuildRoadGeometry(); SetHeights(); PaintRoads(); RebuildForest(); if (refreshGroundCover) RebuildGroundCover();
            var marker = new GameObject("Airport public access"); marker.transform.SetParent(transform, false); return marker;
        }

        void BuildRoadGeometry()
        {
            if (roadGeometry != null)
                foreach (var mesh in new[] { roadGeometry.pavement, roadGeometry.shoulders, roadGeometry.markings, roadGeometry.structures })
                { owned.Remove(mesh); DestroyOwned(mesh); }
            if (roadRoot != null) DestroyOwned(roadRoot);
            roadGeometry = new PublicRoadGeometry(roadNetwork, ruralRoad, airportRoad, World.Height);
            roadRoot = new GameObject("Public road network"); roadRoot.transform.SetParent(transform, false);
            RoadMesh("Asphalt and junctions", roadGeometry.pavement, ruralRoad != null ? ruralRoad.asphalt : sceneryMaterial);
            RoadMesh("Gravel shoulders", roadGeometry.shoulders, ruralRoad != null ? ruralRoad.shoulder : sceneryMaterial);
            RoadMesh("Lanes and stop bars", roadGeometry.markings, ruralRoad != null ? ruralRoad.markings : sceneryMaterial);
            RoadMesh("Bridges and supports", roadGeometry.structures, bridgeMaterial != null ? bridgeMaterial : sceneryMaterial);
            signals = roadRoot.AddComponent<PublicRoadSignalsView>(); signals.Build(roadNetwork, trafficSignalPrefab);
        }

        void RoadMesh(string name, Mesh mesh, Material material)
        {
            owned.Add(mesh);
            var go = new GameObject(name); go.transform.SetParent(roadRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = name == "Bridges and supports" ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        public void SetRoadClock(float seconds) { if (signals != null) signals.SetClock(seconds); }
        public float DisplayHeight(Vector2 p) => data.GetInterpolatedHeight((p.x + ValleyWorld.Size * .5f) / ValleyWorld.Size,
            (p.y + ValleyWorld.Size * .5f) / ValleyWorld.Size);

        void PaintRoads()
        {
            data.SetAlphamaps(0, 0, ValleySurface.Paint(World, roadGeometry, data.alphamapResolution));
            data.SetBaseMapDirty(); dirtySurface.Clear(); queuedSurface.Clear();
        }

        public void RefreshForest(bool repaint = true) { if (repaint) { PaintRoads(); RebuildGroundCover(); } RebuildForest(); }
        public void RefreshSurface() { PaintRoads(); RebuildGroundCover(); }

        /// <summary>Clearing changes soil/canopy colour, never heights, roads or the original understory habitat.</summary>
        public void QueueClearingSurface(Rect strokeBounds)
        {
            if (data == null || World == null) return;
            var area = ValleySurface.PixelBounds(strokeBounds, data.alphamapResolution, 50f);
            if (area.width == 0 || area.height == 0) return;
            int columns = (data.alphamapResolution + SurfaceTileSize - 1) / SurfaceTileSize;
            for (int z = area.yMin / SurfaceTileSize; z <= (area.yMax - 1) / SurfaceTileSize; z++)
            for (int x = area.xMin / SurfaceTileSize; x <= (area.xMax - 1) / SurfaceTileSize; x++)
            {
                int key = z * columns + x;
                if (queuedSurface.Add(key)) dirtySurface.Enqueue(key);
            }
        }

        public void QueueLevelSurface(Rect strokeBounds)
        {
            QueueClearingSurface(strokeBounds);
            if (groundDetails == null || World == null) return;
            // Include height/disturbance interpolation and the neighbouring samples used for prop tilt.
            float margin = World.Spacing + 6f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((strokeBounds.xMin - margin + ValleyWorld.Size * .5f) / GroundCoverRenderer.TileSize), 0, GroundCoverRenderer.TileCount - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((strokeBounds.yMin - margin + ValleyWorld.Size * .5f) / GroundCoverRenderer.TileSize), 0, GroundCoverRenderer.TileCount - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((strokeBounds.xMax + margin + ValleyWorld.Size * .5f) / GroundCoverRenderer.TileSize), 0, GroundCoverRenderer.TileCount - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((strokeBounds.yMax + margin + ValleyWorld.Size * .5f) / GroundCoverRenderer.TileSize), 0, GroundCoverRenderer.TileCount - 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int key = z * GroundCoverRenderer.TileCount + x;
                if (queuedGroundCover.Add(key)) dirtyGroundCover.Enqueue(key);
            }
        }

        // Bound release work even for a long stroke. Repeated strokes share already queued tiles.
        void LateUpdate() { ProcessSurfaceUpdates(); ProcessGroundCoverUpdates(); }
        public int ProcessGroundCoverUpdates(int maxTiles = 1)
        {
            if (groundDetails == null || World == null) return 0;
            int refreshed = 0;
            while (maxTiles-- > 0 && dirtyGroundCover.Count > 0)
            {
                int key = dirtyGroundCover.Dequeue(); queuedGroundCover.Remove(key);
                groundDetails.RefreshTile(World, key, DisplayHeight, roadGeometry, groundExclusions); refreshed++;
            }
            return refreshed;
        }
        public int ProcessSurfaceUpdates(int maxTiles = 1)
        {
            if (data == null || World == null) return 0;
            int painted = 0, columns = (data.alphamapResolution + SurfaceTileSize - 1) / SurfaceTileSize;
            while (maxTiles-- > 0 && dirtySurface.Count > 0)
            {
                int key = dirtySurface.Dequeue(); queuedSurface.Remove(key);
                int x = key % columns * SurfaceTileSize, z = key / columns * SurfaceTileSize;
                var region = new RectInt(x, z, Mathf.Min(SurfaceTileSize, data.alphamapResolution - x),
                    Mathf.Min(SurfaceTileSize, data.alphamapResolution - z));
                data.SetAlphamaps(x, z, ValleySurface.PaintRegion(World, roadGeometry, data.alphamapResolution, region));
                painted += region.width * region.height;
            }
            if (painted > 0) data.SetBaseMapDirty();
            return painted;
        }

        public void SetGroundCoverExclusions(IEnumerable<GroundCoverExclusion> exclusions)
        {
            groundExclusions.Clear();
            if (exclusions != null) groundExclusions.AddRange(exclusions);
            RebuildGroundCover();
        }

        void RebuildGroundCover()
        {
            dirtyGroundCover.Clear(); queuedGroundCover.Clear();
            if (groundCover == null || World == null || data == null) return;
            if (groundDetails == null)
            {
                var go = new GameObject("Instanced rocks and undergrowth"); go.transform.SetParent(transform, false);
                groundDetails = go.AddComponent<GroundCoverRenderer>();
            }
            groundDetails.library = groundCover;
            groundDetails.Rebuild(World, DisplayHeight, roadGeometry, groundExclusions);
        }

        void BuildSettlements()
        {
            var walls = new LandscapeMesh(); var roofs = new LandscapeMesh();
            var rng = new WorldRandom(World.seed + 173);
            foreach (var town in World.settlements)
            for (int i = 0; i < 18; i++)
            {
                var p = town + new Vector2(30f + i % 3 * 31f, (i / 3 - 2.5f) * 34f);
                float height = 5f + rng.Next() * 5f;
                var at = new Vector3(p.x, World.Height(p), p.y);
                walls.Box(at + Vector3.up * height * .5f, new Vector3(17f, height, 22f));
                roofs.Box(at + Vector3.up * (height + .7f), new Vector3(19f, 1.4f, 24f));
            }
            MakeMesh("Valley settlements", walls, wall); MakeMesh("Settlement roofs", roofs, roof);
        }

        void RebuildForest()
        {
            if (forest == null)
            {
                var go = new GameObject("Instanced forest"); go.transform.SetParent(transform, false);
                forest = go.AddComponent<ForestRenderer>();
            }
            forest.library = trees; forest.Rebuild(World, DisplayHeight);
        }

        public GameObject BuildReception(AirportSite site) => BuildReception(site.centre + new Vector2(80f, -150f), site.elevation, 0f);

        public GameObject BuildReception(Vector2 position, float elevation, float heading)
        {
            var mesh = new LandscapeMesh(); var roofs = new LandscapeMesh(); var glass = new LandscapeMesh();
            var at = new Vector3(position.x, elevation, position.y);
            mesh.Box(at + Vector3.up * 2.3f, new Vector3(22f, 4.6f, 12f));
            roofs.Box(at + Vector3.up * 5f, new Vector3(25f, .8f, 15f));
            glass.Box(at + new Vector3(0f, 2.3f, 6.05f), new Vector3(17f, 2.6f, .1f));
            var root = MakeMesh("Passenger reception", mesh, wall);
            MakeMesh("Reception roof", roofs, roof).transform.SetParent(root.transform, true);
            MakeMesh("Reception windows", glass, water).transform.SetParent(root.transform, true);
            root.transform.RotateAround(at, Vector3.up, heading);
            return root;
        }

        Material Material(string name, Color color, float smoothness = .05f)
        {
            var mat = Own(sceneryMaterial != null ? new Material(sceneryMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit")));
            mat.name = name; mat.SetColor("_BaseColor", color); mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        GameObject MakeMesh(string name, LandscapeMesh builder, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var mesh = Own(builder.Create(name));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        T Own<T>(T obj) where T : Object { owned.Add(obj); return obj; }
        public void Dispose()
        {
            dirtySurface.Clear(); queuedSurface.Clear();
            dirtyGroundCover.Clear(); queuedGroundCover.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyOwned(transform.GetChild(i).gameObject);
            foreach (var obj in owned) DestroyOwned(obj);
            owned.Clear(); forest = null; groundDetails = null; roadGeometry = null; roadRoot = null; roadNetwork = null; signals = null;
            Terrain = null; data = null;
        }
        void OnDestroy() { foreach (var obj in owned) DestroyOwned(obj); owned.Clear(); }
        static void DestroyOwned(Object obj)
        {
            if (obj == null) return;
            if (obj is GameObject go) go.SetActive(false);
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
    }

    internal sealed class LandscapeMesh
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> triangles = new List<int>();
        public void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Triangle(a, b, c); Triangle(a, c, d); }
        public void Box(Vector3 centre, Vector3 size)
            => OrientedBox(centre, size, Quaternion.identity);

        public void OrientedBox(Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++) p[i] = centre + rotation * Vector3.Scale(size * .5f, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            Quad(p[0], p[4], p[6], p[2]); Quad(p[5], p[1], p[3], p[7]);
            Quad(p[4], p[5], p[7], p[6]); Quad(p[1], p[0], p[2], p[3]);
            Quad(p[2], p[6], p[7], p[3]); Quad(p[0], p[1], p[5], p[4]);
        }
        public void Cone(Vector3 at, float height, float radius, float topRadius, int sides)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides, b = (i + 1f) * Mathf.PI * 2f / sides;
                var r0 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); var r1 = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                if (topRadius <= 0f) Triangle(at + r1 * radius, at + r0 * radius, at + Vector3.up * height);
                else Quad(at + r1 * radius, at + r0 * radius, at + r0 * topRadius + Vector3.up * height, at + r1 * topRadius + Vector3.up * height);
            }
        }
        public Mesh Create(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
