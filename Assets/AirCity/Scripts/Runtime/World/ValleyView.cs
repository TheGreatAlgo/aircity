using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.World
{
    /// <summary>Disposable presentation of a world snapshot, with spatially instanced forests and separate public roads.</summary>
    public partial class ValleyView : MonoBehaviour
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
        Material water, roof, wall, paint, kerb, medianGrass;
        TerrainData data;
        AirportSite roadConnection;
        PublicRoadNetwork roadNetwork;
        /// <summary>The roads as last drawn (a copy): a later change is compared with it, so only the ground near roads that
        /// came, went or changed is regraded, repainted and re-scattered.</summary>
        PublicRoadNetwork roadsDrawn;
        /// <summary>Ground cover has been scattered for the whole valley (later changes refresh only the tiles they touch).</summary>
        bool groundCoverBuilt;
        PublicRoadGeometry roadGeometry;
        PublicRoadChunks roadChunks;
        readonly List<PublicRoadSignalsView> roadSignals=new List<PublicRoadSignalsView>();
        Material roadStructureMaterial;
        GameObject roadRoot;
        ForestRenderer forest;
        GroundCoverRenderer groundDetails;
        RegionWaterView ocean, inlandWater;
        RegionSceneryView regionScenery;
        RegionTrafficView regionTraffic;
        float roadClock;
        readonly List<GroundCoverExclusion> groundExclusions = new List<GroundCoverExclusion>();
        readonly List<GroundCoverExclusion> coverExclusions = new List<GroundCoverExclusion>();
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
            water = Material("River water", world.preset == RegionPreset.Tropical ? new Color(.08f, .49f, .48f) : new Color(.12f, .39f, .43f), .8f);
            roof = Material("Settlement roofs", new Color(.28f, .32f, .35f));
            wall = Material("Settlement walls", new Color(.69f, .64f, .52f));
            paint = Material("Road markings", new Color(.83f, .80f, .64f));
            kerb = Material("Road kerbs", new Color(.6f, .6f, .58f), .15f);
            medianGrass = Material("Median grass", new Color(.33f, .45f, .22f));
            data = Own(new TerrainData { name = "Saved valley terrain", heightmapResolution = TerrainHeightmap.RenderResolution(world),
                size = new Vector3(ValleyWorld.Size, ValleyWorld.HeightRange, ValleyWorld.Size), alphamapResolution = 1024 });
            var grass = grassLayer != null ? grassLayer : Own(new TerrainLayer { name = "Valley grass", diffuseTexture = grassTexture, tileSize = new Vector2(16f, 16f) });
            var dirt = dirtLayer != null ? dirtLayer : Own(new TerrainLayer { name = "Exposed ground", diffuseTexture = dirtTexture, tileSize = new Vector2(20f, 20f) });
            var paving = roadLayer != null ? roadLayer : Own(new TerrainLayer { name = "Road surface", diffuseTexture = dirtTexture, tileSize = new Vector2(12f, 12f) });
            data.terrainLayers = new[] { grass, dirt, paving, dryLayer != null ? dryLayer : grass, rockLayer != null ? rockLayer : dirt, forestLayer != null ? forestLayer : dirt };
            ApplyRegionPalette();
            roadNetwork = savedRoads ?? PublicRoadNetwork.FromWorld(world);
            BuildRoadGeometry(); SetHeights(); roadsDrawn = roadNetwork.Clone();
            var terrainObject = UnityEngine.Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Valley landscape"; terrainObject.transform.SetParent(transform, false);
            terrainObject.transform.localPosition = new Vector3(-ValleyWorld.Size * .5f, 0f, -ValleyWorld.Size * .5f);
            Terrain = terrainObject.GetComponent<Terrain>();
            Terrain.materialTemplate = RegionTerrainMaterial();
            BindBuiltSurfaces();
            Terrain.heightmapPixelError = 1f; Terrain.basemapDistance = 20000f; Terrain.drawInstanced = true;
            Terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;
            PaintRoads(); Terrain.Flush();
            if (World.HasRiver) BuildRiver();
            if (World.HasOcean)
            {
                var sea = new GameObject("Regional ocean"); sea.transform.SetParent(transform, false);
                ocean = sea.AddComponent<RegionWaterView>(); ocean.Build(World, DisplayHeight, data);
            }
            if (World.HasInlandWater)
            {
                var waterBody = new GameObject("Regional rivers and lakes"); waterBody.transform.SetParent(transform, false);
                inlandWater = waterBody.AddComponent<RegionWaterView>(); inlandWater.Build(World, DisplayHeight, data, true);
            }
            BuildSettlements();
            var traffic = new GameObject("Ambient regional traffic"); traffic.transform.SetParent(transform, false);
            regionTraffic = traffic.AddComponent<RegionTrafficView>(); regionTraffic.Build(World, roadGeometry);
            RebuildForest(); if (refreshGroundCover) RebuildGroundCover();
        }

        Material RegionTerrainMaterial()
        {
            if (terrainMaterial == null) return null;
            var material = Own(new Material(terrainMaterial)); material.name = World.PresetName + " terrain";
            if (rockLayer == null || !material.HasProperty("_CliffEnabled")) return material;
            material.SetFloat("_CliffEnabled", 1f);
            material.SetTexture("_CliffAlbedo", rockLayer.diffuseTexture);
            if (rockLayer.normalMapTexture != null) material.SetTexture("_CliffNormal", rockLayer.normalMapTexture);
            if (rockLayer.maskMapTexture != null) material.SetTexture("_CliffMask", rockLayer.maskMapTexture);
            material.SetFloat("_CliffTileSize", 18f); material.SetFloat("_CliffNormalStrength", .65f);
            material.SetColor("_CliffTint", World.preset == RegionPreset.Tropical ? new Color(.63f,.69f,.66f) :
                World.preset == RegionPreset.Desert ? new Color(.98f,.8f,.6f) : World.preset == RegionPreset.Woodland ?
                new Color(.86f,.89f,.85f) : new Color(.9f,.93f,.95f));
            return material;
        }

        void ApplyRegionPalette()
        {
            if (World.preset != RegionPreset.Desert && World.preset != RegionPreset.Tropical) return;
            var layers = data.terrainLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (i == 2) continue; // Public road surfacing keeps its authored colour.
                var layer = Own(Instantiate(layers[i])); layer.name = World.PresetName + " " + layers[i].name;
                Vector4 tint = World.preset == RegionPreset.Desert ?
                    (i == 4 ? new Vector4(1.65f, 1.24f, .78f, 1f) : new Vector4(1.9f, 1.52f, .86f, 1f)) :
                    i == 1 ? new Vector4(1.65f, 1.48f, 1.04f, 1f) : new Vector4(.78f, 1.1f, .83f, 1f);
                layer.diffuseRemapMin = Vector4.zero; layer.diffuseRemapMax = tint;
                layers[i] = layer;
            }
            data.terrainLayers = layers;
        }

        public void RefreshLand(bool repaint = true)
        {
            BuildRoadGeometry(true); SetHeights(); roadsDrawn = roadNetwork?.Clone();
            ocean?.RefreshTerrain(); inlandWater?.RefreshTerrain(); BuildSettlements();
            if (repaint) { PaintRoads(); RebuildGroundCover(); } RebuildForest();
        }

        /// <summary>How far a road's grading (31 m), ground paint (about 53 m) and kept-clear ground cover reach from it.</summary>
        public const float RoadInfluence = PublicRoadGeometry.EarthworkReach + 32f;

        /// <summary>
        /// The public roads changed (a road built, joined or altered): their meshes are rebuilt, and only the ground near
        /// roads that came, went or changed (and the roads meeting them, whose junctions change) is regraded, repainted
        /// and re-scattered, so building a road costs a few frames' work rather than redoing the whole valley.
        /// </summary>
        public void RefreshRoads(PublicRoadNetwork network, bool refreshGroundCover = true)
        {
            if (data == null || World == null || network == null) return;
            var before = roadsDrawn;
            roadNetwork = network;
            BuildRoadGeometry();
            roadsDrawn = network.Clone();
            var changed = ChangedArea(before, network);
            if (changed == null) return;
            var area = changed.Value;
            area = Rect.MinMaxRect(area.xMin - RoadInfluence, area.yMin - RoadInfluence, area.xMax + RoadInfluence, area.yMax + RoadInfluence);
            RefreshLand(area);
            if(SceneryNear(area))BuildSettlements();
            ocean?.RefreshBed();
            inlandWater?.RefreshBed();
            if (regionTraffic != null && RegionalRoadChanged(before,network)) { regionTraffic.Build(World, roadGeometry); regionTraffic.SetClock(roadClock); }
            // The game transaction clears vegetation through the shared built-surface map.
            if (refreshGroundCover && groundCoverBuilt) QueueGroundCover(area, true);
        }

        /// <summary>Where two road networks differ: the bounds of every section added, removed or changed, and of the
        /// sections meeting them; null if they're the same. No earlier network: the whole valley.</summary>
        static Rect? ChangedArea(PublicRoadNetwork before, PublicRoadNetwork after)
        {
            float half = ValleyWorld.Size * .5f;
            if (before == null) return Rect.MinMaxRect(-half, -half, half, half);
            (Vector3, Vector3, PublicRoadStyle, RoadStructure, AlignmentShape) Key(PublicRoadNetwork g, PublicRoadSegment e) =>
                (g.Node(e.a)?.position ?? Vector3.zero, g.Node(e.b)?.position ?? Vector3.zero, e.style, e.structure, e.alignment);
            var old = new Dictionary<int, (Vector3, Vector3, PublicRoadStyle, RoadStructure, AlignmentShape)>();
            foreach (var e in before.segments) old[e.id] = Key(before, e);
            var touched = new HashSet<Vector2>();
            bool any = false;
            Rect bounds = default;
            void Include(Vector3 a, Vector3 b)
            {
                var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
                bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin), Mathf.Max(bounds.xMax, r.xMax),
                    Mathf.Max(bounds.yMax, r.yMax)) : r;
                any = true;
                touched.Add(new Vector2(a.x, a.z));
                touched.Add(new Vector2(b.x, b.z));
            }
            foreach (var e in after.segments)
            {
                var k = Key(after, e);
                if (old.TryGetValue(e.id, out var was))
                {
                    old.Remove(e.id);
                    if (was.Equals(k)) continue;
                    Include(was.Item1, was.Item2);
                }
                Include(k.Item1, k.Item2);
            }
            foreach (var gone in old.Values) Include(gone.Item1, gone.Item2);
            if (!any) return null;
            foreach (var g in new[] { before, after }) foreach (var edge in g.segments)
                if (touched.Contains(PublicRoadNetwork.XZ(g.Node(edge.a).position)) || touched.Contains(PublicRoadNetwork.XZ(g.Node(edge.b).position)))
                {
                    var r = g.Path(edge).Bounds(edge.Type.FullWidth);
                    bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin,r.xMin),Mathf.Min(bounds.yMin,r.yMin),Mathf.Max(bounds.xMax,r.xMax),Mathf.Max(bounds.yMax,r.yMax));
                }
            // the sections meeting a changed one get new junction shapes and grading near it
            foreach (var g in new[] { before, after })
                foreach (var e in g.segments)
                {
                    var a = g.Node(e.a)?.position ?? Vector3.zero; var b = g.Node(e.b)?.position ?? Vector3.zero;
                    if (touched.Contains(new Vector2(a.x, a.z)) || touched.Contains(new Vector2(b.x, b.z)))
                        bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, Mathf.Min(a.x, b.x)), Mathf.Min(bounds.yMin, Mathf.Min(a.z, b.z)),
                            Mathf.Max(bounds.xMax, Mathf.Max(a.x, b.x)), Mathf.Max(bounds.yMax, Mathf.Max(a.z, b.z)));
                }
            return bounds;
        }

        /// <summary>A brush changes height samples, not the public-road layout or the rest of the valley.</summary>
        public void RefreshLand(Rect brushBounds)
        {
            if (data == null || World == null) return;
            if (roadNetwork.segments.Any(e => e.structure != RoadStructure.Ground && roadNetwork.Path(e).Bounds(e.Type.FullWidth).Overlaps(brushBounds)))
                BuildRoadGeometry(false, brushBounds);
            if (builtAirport != null && builtAirport.Taxiways.edges.Any(e => e.structure != RoadStructure.Ground &&
                builtAirport.Taxiways.Path(e).Bounds(e.width).Overlaps(new Rect(brushBounds.position - new Vector2(builtAirport.transform.position.x,builtAirport.transform.position.z),brushBounds.size))))
                builtAirport.Taxiways.RefreshStructures();
            // A changed source sample influences the finer native grid for one whole source cell around it.
            float interpolation = data.heightmapResolution > ValleyWorld.Resolution ? World.Spacing : 0f;
            var region = ValleySurface.PixelBounds(brushBounds, data.heightmapResolution, interpolation);
            if (region.width == 0 || region.height == 0) return;
            var source = TerrainHeightmap.SourceBounds(region, data.heightmapResolution);
            SyncBuiltSurfaces(brushBounds,brushBounds);
            var coarse = roadGeometry.GradedHeightRegion(World, source);
            var h = TerrainHeightmap.Resample(coarse, source, region, data.heightmapResolution, ValleyWorld.HeightRange, builtSurfaces);
            data.SetHeights(region.x, region.y, h);
            // Bilinear height interpolation reaches one height sample beyond the actual edit.
            float margin = World.Spacing;
            if (forest != null) forest.RefreshHeights(Rect.MinMaxRect(brushBounds.xMin - margin, brushBounds.yMin - margin,
                brushBounds.xMax + margin, brushBounds.yMax + margin), DisplayHeight);
        }

        void SetHeights()
        {
            var source = new RectInt(0, 0, ValleyWorld.Resolution, ValleyWorld.Resolution);
            var target = new RectInt(0, 0, data.heightmapResolution, data.heightmapResolution);
            SyncBuiltSurfaces();
            var roadbed = roadGeometry.GradedHeightRegion(World, source);
            var h = TerrainHeightmap.Resample(roadbed, source, target, data.heightmapResolution, ValleyWorld.HeightRange, builtSurfaces);
            data.SetHeights(0, 0, h);
        }

        void BuildRiver()
        {
            var mesh = new LandscapeMesh();
            for (int i = 0; i < 512; i++)
            {
                float z0 = -ValleyWorld.Size * .5f + i * ValleyWorld.Size / 512f, z1 = z0 + ValleyWorld.Size / 512f;
                float x0 = World.RiverX(z0), x1 = World.RiverX(z1);
                float width = World.generatorVersion < 3 ? 120f : World.River.halfWidth + 24f;
                mesh.Quad(new Vector3(x0 - width, World.WaterHeight(z0), z0), new Vector3(x1 - width, World.WaterHeight(z1), z1),
                    new Vector3(x1 + width, World.WaterHeight(z1), z1), new Vector3(x0 + width, World.WaterHeight(z0), z0));
            }
            MakeMesh("River", mesh, water);
        }

        public GameObject BuildAccess(AirportSite site, PublicRoadNetwork savedRoads = null, bool refreshGroundCover = true)
        {
            roadConnection = site;
            var network = savedRoads ?? PublicRoadNetwork.FromWorld(World);
            if (savedRoads == null && !network.AddAirportAccess(World, site, PublicRoadLayout.Suggested(site), out string why))
                throw new System.InvalidOperationException(why);
            RefreshRoads(network, refreshGroundCover);
            var marker = new GameObject("Airport public access"); marker.transform.SetParent(transform, false); return marker;
        }

        void BuildRoadGeometry(bool force=false,Rect? terrainChange=null)
        {
            if(roadRoot==null){roadRoot=new GameObject("Public road network");roadRoot.transform.SetParent(transform,false);}
            roadChunks??=new PublicRoadChunks(ruralRoad,airportRoad);
            if(roadStructureMaterial==null)roadStructureMaterial=Own(RoadStructureBuilder.Material(bridgeMaterial));
            roadChunks.Update(roadNetwork,World.Height,part=>
            {
                var chunk=new GameObject((part.junction?"Junction ":"Section ")+part.id);chunk.transform.SetParent(roadRoot.transform,false);part.root=chunk;
                var geometry=part.geometry;
                RoadMesh("Asphalt and junctions",geometry.pavement,ruralRoad!=null?ruralRoad.asphalt:sceneryMaterial,chunk.transform);
                RoadMesh("Gravel shoulders",geometry.shoulders,ruralRoad!=null?ruralRoad.shoulder:sceneryMaterial,chunk.transform);
                RoadMesh("Lanes and stop bars",geometry.markings,ruralRoad!=null?ruralRoad.markings:sceneryMaterial,chunk.transform);
                RoadMesh("Bridges and supports",geometry.structures,roadStructureMaterial,chunk.transform);
                RoadMesh("Kerbs and barriers",geometry.kerbMesh,kerb!=null?kerb:sceneryMaterial,chunk.transform);
                RoadMesh("Median islands",geometry.medianGrass,medianGrass!=null?medianGrass:sceneryMaterial,chunk.transform);
                if(part.junction)
                {
                    var local=chunk.AddComponent<PublicRoadSignalsView>();local.Build(roadNetwork,trafficSignalPrefab,geometry.StopLine,new HashSet<int>{part.id});local.SetClock(roadClock);
                }
            },force,terrainChange);
            roadGeometry=roadChunks.Geometry;roadSignals.Clear();
            foreach(var part in roadChunks.Parts)if(part.root!=null)
            {var signal=part.root.GetComponent<PublicRoadSignalsView>();if(signal!=null)roadSignals.Add(signal);}
        }

        void RoadMesh(string name, Mesh mesh, Material material,Transform parent=null)
        {
            if(mesh==null || mesh.vertexCount==0)return;
            var go = new GameObject(name); go.transform.SetParent(parent!=null?parent:roadRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = name == "Bridges and supports" ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        public void SetRoadClock(float seconds)
        {
            roadClock = seconds;
            foreach(var signal in roadSignals)if(signal!=null)signal.SetClock(seconds);
            if (regionTraffic != null) regionTraffic.SetClock(seconds);
            if (regionScenery != null) regionScenery.SetClock(seconds);
            if (ocean != null) ocean.SetClock(seconds);
            if (inlandWater != null) inlandWater.SetClock(seconds);
        }
        public float DisplayHeight(Vector2 p) => data.GetInterpolatedHeight((p.x + ValleyWorld.Size * .5f) / ValleyWorld.Size,
            (p.y + ValleyWorld.Size * .5f) / ValleyWorld.Size);

        void PaintRoads()
        {
            data.SetAlphamaps(0, 0, ValleySurface.Paint(World, roadGeometry, data.alphamapResolution));
            data.SetBaseMapDirty();
            dirtySurface.Clear(); queuedSurface.Clear();
        }

        public void RefreshForest(bool repaint = true, Rect? changedArea = null)
        {
            if (repaint) { PaintRoads(); RebuildGroundCover(); }
            if (forest == null) RebuildForest();
            else { forest.library = trees; forest.RefreshRemoved(World, DisplayHeight, changedArea); }
        }
        public void RefreshSurface() { PaintRoads(); RebuildGroundCover(); }

        public IEnumerable<GroundCoverInstance> GroundCandidates(Rect bounds) => groundDetails != null ?
            groundDetails.QueryBounds(bounds) : System.Array.Empty<GroundCoverInstance>();

        public IEnumerable<GroundCoverInstance> ClearCandidates(Vector2 point, float radius)
        {
            float reach = radius + GroundCoverScatter.MaximumFootprintRadius;
            return GroundCandidates(new Rect(point.x - reach, point.y - reach, reach * 2f, reach * 2f));
        }

        public void RefreshClearedGround(Rect? changedArea = null) => groundDetails?.RefreshCleared(World, changedArea, DisplayHeight);

        /// <summary>Clearing changes soil/canopy colour without reshaping terrain or rerolling surviving nature.</summary>
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

        public void QueueLevelSurface(Rect strokeBounds,bool repaint=true)
        {
            if(repaint)QueueClearingSurface(strokeBounds);
            if (ocean != null) ocean.RefreshBed();
            if (inlandWater != null) inlandWater.RefreshBed();
            if (regionScenery != null)
                foreach (var footprint in regionScenery.Footprints)
                    if (Bounds(footprint).Overlaps(strokeBounds)) { BuildSettlements(); break; }
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
        void LateUpdate()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            do ProcessSurfaceUpdates(); while (dirtySurface.Count > 0 && clock.Elapsed.TotalMilliseconds < FrameBudgetMilliseconds);
            clock.Restart();
            do ProcessGroundCoverUpdates(); while (dirtyGroundCover.Count > 0 && clock.Elapsed.TotalMilliseconds < FrameBudgetMilliseconds);
        }
        public int ProcessGroundCoverUpdates(int maxTiles = 1)
        {
            if (groundDetails == null || World == null) return 0;
            int refreshed = 0;
            while (maxTiles-- > 0 && dirtyGroundCover.Count > 0)
            {
                int key = dirtyGroundCover.Dequeue(); queuedGroundCover.Remove(key);
                groundDetails.RefreshTile(World, key, DisplayHeight, roadGeometry, coverExclusions, builtSurfaces); refreshed++;
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

        /// <summary>Where the airport keeps ground cover clear. Once the valley's cover is scattered, only the tiles an
        /// exclusion that came or went touches are scattered again.</summary>
        public void SetGroundCoverExclusions(IEnumerable<GroundCoverExclusion> exclusions)
        {
            var next = exclusions != null ? new List<GroundCoverExclusion>(exclusions) : new List<GroundCoverExclusion>();
            if (!groundCoverBuilt || groundDetails == null)
            {
                groundExclusions.Clear(); groundExclusions.AddRange(next);
                BuildSettlements();
                RebuildGroundCover();
                return;
            }
            var gone = new List<GroundCoverExclusion>(groundExclusions);
            var came = new List<GroundCoverExclusion>();
            foreach (var e in next)
            {
                int i = gone.FindIndex(o => o.a == e.a && o.b == e.b && o.radius == e.radius);
                if (i >= 0) gone.RemoveAt(i); else came.Add(e);
            }
            groundExclusions.Clear(); groundExclusions.AddRange(next);
            if (came.Count > 0 || gone.Count > 0) BuildSettlements();
            foreach (var e in came) QueueGroundCover(Bounds(e), false);
            foreach (var e in gone) QueueGroundCover(Bounds(e), false);
            ProcessGroundCoverUpdates(ImmediateGroundCoverTiles);
        }

        static Rect Bounds(GroundCoverExclusion e) => Rect.MinMaxRect(Mathf.Min(e.a.x, e.b.x) - e.radius, Mathf.Min(e.a.y, e.b.y) - e.radius,
            Mathf.Max(e.a.x, e.b.x) + e.radius, Mathf.Max(e.a.y, e.b.y) + e.radius);

        /// <summary>Ground cover tiles refreshed at once after a build (up to about 8 ms each, where the cover is dense); the
        /// rest follow over the next frames within FrameBudgetMilliseconds.</summary>
        const int ImmediateGroundCoverTiles = 1;
        /// <summary>Per frame, queued ground cover and ground paint work stops after this long (at least one tile each).</summary>
        const float FrameBudgetMilliseconds = 3f;

        /// <summary>Queue the ground cover tiles over an area (and refresh a few at once if asked).</summary>
        void QueueGroundCover(Rect area, bool now)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin + ValleyWorld.Size * .5f) / GroundCoverRenderer.TileSize), 0, GroundCoverRenderer.TileCount - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin + ValleyWorld.Size * .5f) / GroundCoverRenderer.TileSize), 0, GroundCoverRenderer.TileCount - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((area.xMax + ValleyWorld.Size * .5f) / GroundCoverRenderer.TileSize), 0, GroundCoverRenderer.TileCount - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((area.yMax + ValleyWorld.Size * .5f) / GroundCoverRenderer.TileSize), 0, GroundCoverRenderer.TileCount - 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int key = z * GroundCoverRenderer.TileCount + x;
                if (queuedGroundCover.Add(key)) dirtyGroundCover.Enqueue(key);
            }
            if (now) ProcessGroundCoverUpdates(ImmediateGroundCoverTiles);
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
            groundDetails.Rebuild(World, DisplayHeight, roadGeometry, coverExclusions, builtSurfaces);
            groundCoverBuilt = true;
        }

        bool SceneryNear(Rect area)
        {
            foreach(var town in World.settlements)if(area.Overlaps(new Rect(town-Vector2.one*350f,Vector2.one*700f)))return true;
            if(regionScenery!=null)foreach(var footprint in regionScenery.Footprints)if(area.Overlaps(Bounds(footprint)))return true;
            return false;
        }
        static bool RegionalRoadChanged(PublicRoadNetwork before,PublicRoadNetwork after)
        {
            if(before==null)return true;
            foreach(var edge in after.segments)
            {
                if(edge.style!=PublicRoadStyle.Rural)continue;
                var old=before.Segment(edge.id);
                if(old==null || old.a!=edge.a || old.b!=edge.b || old.structure!=edge.structure || !old.alignment.Equals(edge.alignment) ||
                    before.Node(old.a).position!=after.Node(edge.a).position || before.Node(old.b).position!=after.Node(edge.b).position)return true;
            }
            return before.segments.Count(e=>e.style==PublicRoadStyle.Rural)!=after.segments.Count(e=>e.style==PublicRoadStyle.Rural);
        }

        void BuildSettlements()
        {
            var previous = regionScenery != null ? new List<GroundCoverExclusion>(regionScenery.Footprints) : new List<GroundCoverExclusion>();
            if (regionScenery == null)
            {
                var go = new GameObject("Regional villages and harbour"); go.transform.SetParent(transform, false);
                regionScenery = go.AddComponent<RegionSceneryView>();
            }
            regionScenery.Build(World, roadGeometry, DisplayHeight, groundExclusions);
            regionScenery.SetClock(roadClock);
            coverExclusions.Clear(); coverExclusions.AddRange(groundExclusions);
            coverExclusions.AddRange(regionScenery.Footprints);
            if (groundCoverBuilt)
            {
                foreach (var footprint in regionScenery.Footprints)
                {
                    int i = previous.FindIndex(old => old.a == footprint.a && old.b == footprint.b && old.radius == footprint.radius);
                    if (i >= 0) previous.RemoveAt(i); else QueueGroundCover(Bounds(footprint), false);
                }
                foreach (var footprint in previous) QueueGroundCover(Bounds(footprint), false);
            }
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
            roadChunks?.Dispose();roadChunks=null;roadSignals.Clear();roadStructureMaterial=null;
            dirtySurface.Clear(); queuedSurface.Clear();
            dirtyGroundCover.Clear(); queuedGroundCover.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyOwned(transform.GetChild(i).gameObject);
            foreach (var obj in owned) DestroyOwned(obj);
            owned.Clear(); forest = null; groundDetails = null; roadGeometry = null; roadRoot = null; roadNetwork = null; signals = null;
            ocean = null; inlandWater = null; regionScenery = null; regionTraffic = null; roadClock = 0f;
            DisposeBuiltSurfaces();
            coverExclusions.Clear();
            roadsDrawn = null; groundCoverBuilt = false;
            Terrain = null; data = null;
        }
        void OnDestroy() { roadChunks?.Dispose();roadChunks=null;DisposeBuiltSurfaces(); foreach (var obj in owned) DestroyOwned(obj); owned.Clear(); }
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
        /// <summary>A quad seen from both sides (kerb faces, barriers: whichever way their corners wind).</summary>
        public void TwoSided(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Quad(a, b, c, d); Quad(a, d, c, b); }
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
        public Mesh Create(string name,Mesh target=null)
        {
            var mesh=target!=null?target:new Mesh();mesh.Clear();mesh.name=name;mesh.indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
