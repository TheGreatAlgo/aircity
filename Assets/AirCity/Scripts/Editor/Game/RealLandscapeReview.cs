using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Game;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Game
{
    /// <summary>Synchronous world-only renders for an isolated batch validation project; no Game view is required.</summary>
    public static class RealLandscapeReview
    {
        const string Root = "Captures/NewGame/RealLandscapes/";
        public static void RunFromCommandLine()
        {
            try
            {
                Directory.CreateDirectory(Root); Directory.CreateDirectory("Logs");
                EditorSceneManager.OpenScene(NewGameSetup.ScenePath, OpenSceneMode.Single);
                var game = UnityEngine.Object.FindAnyObjectByType<CareerGame>();
                if (game == null) throw new InvalidOperationException("New Game scene has no game host.");
                File.WriteAllText("Logs/real-landscape-review.txt", "World rendering diagnostics\n");
                foreach (var preset in new[] { RegionPreset.Woodland, RegionPreset.Mountains, RegionPreset.Tropical, RegionPreset.Desert })
                {
                    int seed = preset == RegionPreset.Woodland ? 19770616 : preset == RegionPreset.Mountains ? 873241 : preset == RegionPreset.Tropical ? 445090 : 692835;
                    var state = CareerState.New(seed, preset: preset);
                    if (!state.world.Validate(out string problem))
                        throw new InvalidOperationException($"{preset}: {problem} source={state.world.landscapeSource}, coords={state.world.latitude}/{state.world.longitude}, extent={state.world.sourceExtent}, trees={state.world.trees.Count}, sites={state.world.sites.Count}, roads={state.world.publicRoad.Count}, settlements={state.world.settlements.Count}");
                    game.Initialize(state, false);
                    InspectInlandMeshEdges(game);
                    game.valley.SetRoadClock(45f);
                    var camera = game.view.GetComponent<Camera>();
                    game.view.Region(); Capture(game,camera,preset + "-overview");
                    if (preset == RegionPreset.Mountains) { FocusCliff(game); Capture(game,camera,"Mountain-cliff-surface"); }
                    FocusRoad(game); Capture(game,camera,preset + "-road-support");
                    var scenery = game.GetComponentInChildren<RegionSceneryView>();
                    if (scenery != null && scenery.Footprints.Count > 0)
                    {
                        var p = scenery.Footprints[0].a;
                        game.view.FocusPoint(new Vector3(p.x,game.valley.DisplayHeight(p)+5f,p.y),260f,44f,-35f);
                        Capture(game,camera,preset+"-village");
                    }
                    if (preset == RegionPreset.Tropical)
                    {
                        var p = scenery != null && scenery.HasHarbour ? scenery.HarbourPosition : game.State.world.publicRoad[0];
                        game.view.FocusPoint(new Vector3(p.x,game.State.world.SeaLevel+5f,p.y),700f,49f,-35f);
                        Capture(game,camera,"Tropical-lagoon-harbour");
                        CaptureNaturalCoasts(game, camera);
                    }
                    if (game.State.world.HasInlandWater) CaptureInlandBanks(game, camera);
                }
                File.AppendAllText("Logs/real-landscape-review.txt","Completed world-only landscape renders.\n");
                EditorApplication.Exit(0);
            }
            catch(Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
        static void Capture(CareerGame game, Camera camera, string name)
        {
            Dev.SceneCapture.Capture(camera,Root+name+".png",1600,1000);
            var scenery=game.GetComponentInChildren<RegionSceneryView>();
            File.AppendAllText("Logs/real-landscape-review.txt",name+": source="+game.State.world.landscapeSource+
                ", trees="+game.State.world.trees.Count+", houses="+(scenery!=null?scenery.HouseCount:0)+
                ", harbour="+(scenery!=null&&scenery.HasHarbour)+", road nodes="+game.State.world.publicRoad.Count+"\n");
        }
        static void InspectInlandMeshEdges(CareerGame game)
        {
            // Split coarse lake edges into the same native segments as refined banks. Index-only counts
            // mistake fine/coarse T-junctions for exposed mesh boundaries.
            const int subdivision = 4;
            int native = (ValleyWorld.Resolution - 1) * subdivision + 1;
            float half = ValleyWorld.Size * .5f, spacing = game.State.world.Spacing / subdivision;
            ulong Key(int a, int b) => ((ulong)(uint)Mathf.Min(a,b) << 32) | (uint)Mathf.Max(a,b);
            void Toggle(HashSet<ulong> set, ulong edge) { if (!set.Add(edge)) set.Remove(edge); }
            int Gcd(int a, int b) { while (b != 0) { int remainder = a % b; a = b; b = remainder; } return a; }
            int edges = 0, exposed = 0; float minimumGap = float.MaxValue; Vector3 worst = default;
            foreach (var water in game.GetComponentsInChildren<RegionWaterView>())
            foreach (var filter in water.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || renderer.sharedMaterial.GetFloat("_Inland") < .5f) continue;
                var mesh = filter.sharedMesh; var vertices = mesh.vertices; var triangles = mesh.triangles;
                var indexed = new HashSet<ulong>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Toggle(indexed,Key(triangles[i],triangles[i+1]));
                    Toggle(indexed,Key(triangles[i+1],triangles[i+2]));
                    Toggle(indexed,Key(triangles[i+2],triangles[i]));
                }
                var segments = new HashSet<ulong>();
                foreach (ulong edge in indexed)
                {
                    var a = filter.transform.TransformPoint(vertices[(int)(edge >> 32)]);
                    var b = filter.transform.TransformPoint(vertices[(int)(edge & uint.MaxValue)]);
                    int ax = Mathf.RoundToInt((a.x + half) / spacing), az = Mathf.RoundToInt((a.z + half) / spacing);
                    int bx = Mathf.RoundToInt((b.x + half) / spacing), bz = Mathf.RoundToInt((b.z + half) / spacing);
                    int steps = Gcd(Mathf.Abs(bx-ax),Mathf.Abs(bz-az));
                    for (int step = 0; step < steps; step++)
                    {
                        int start = (az+(bz-az)*step/steps)*native+ax+(bx-ax)*step/steps;
                        int end = (az+(bz-az)*(step+1)/steps)*native+ax+(bx-ax)*(step+1)/steps;
                        Toggle(segments,Key(start,end));
                    }
                }
                foreach (ulong segment in segments)
                {
                    int a = (int)(segment >> 32), b = (int)(segment & uint.MaxValue);
                    var p = new Vector2(((a % native)+(b % native))*.5f*spacing-half,
                        ((a / native)+(b / native))*.5f*spacing-half);
                    if (Mathf.Abs(p.x) >= half-.1f || Mathf.Abs(p.y) >= half-.1f) continue;
                    float level = game.State.world.InlandWaterHeight(p)+.025f;
                    float gap = game.valley.DisplayHeight(p)-level; edges++;
                    if (gap < -.1f) exposed++;
                    if (gap >= minimumGap) continue; minimumGap = gap; worst = new Vector3(p.x,level,p.y);
                }
            }
            File.AppendAllText("Logs/real-landscape-review.txt", $"Inland mesh edges: {game.State.world.preset}, segments={edges}, above ground={exposed}, minimum ground-water gap={(edges>0?minimumGap:0f):0.000}m, worst={worst}.\n");
        }
        static void FocusCliff(CareerGame game)
        {
            var world=game.State.world;float best=0;var p=Vector2.zero;
            for(int z=50;z<ValleyWorld.Resolution-50;z+=2)
            for(int x=50;x<ValleyWorld.Resolution-50;x+=2)
            {
                int i=z*ValleyWorld.Resolution+x;
                float slope=Mathf.Abs(world.heights[i+1]-world.heights[i-1])+Mathf.Abs(world.heights[i+ValleyWorld.Resolution]-world.heights[i-ValleyWorld.Resolution]);
                if(slope<=best)continue;best=slope;p=new Vector2(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f);
            }
            game.view.FocusPoint(new Vector3(p.x,world.Height(p),p.y),400f,38f,-35f);
        }
        static void FocusRoad(CareerGame game)
        {
            var world=game.State.world;int at=world.publicRoad.Count/2;float best=-1f;
            for(int i=2;i<world.publicRoad.Count-2;i++)
            {
                var q=world.publicRoad[i];if(Mathf.Abs(q.x)>2800||Mathf.Abs(q.y)>2800)continue;
                float cut=Mathf.Abs(world.Height(q)-world.roadHeights[i]);
                if(cut<=best)continue;best=cut;at=i;
            }
            var p=world.publicRoad[at];
            game.view.FocusPoint(new Vector3(p.x,game.valley.DisplayHeight(p)+4f,p.y),190f,36f,-35f);
        }

        static void CaptureInlandBanks(CareerGame game, Camera camera)
        {
            var world = game.State.world;
            const int resolution = ValleyWorld.Resolution;
            var directions = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            float best = float.MinValue, level = 0f;
            Vector2 bank = default, towardLand = default;
            for (int z = 24; z < resolution - 24; z++)
            for (int x = 24; x < resolution - 24; x++)
            {
                int index = z * resolution + x;
                if (world.waterHeights[index] <= 0f) continue;
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                if (!NaturalCoastClearOfRoads(world, p)) continue;
                int wet = 0;
                for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++) if (world.waterHeights[index + dz * resolution + dx] > 0f) wet++;
                if (wet < 8 || wet > 42) continue;
                foreach (var direction in directions)
                {
                    int step = direction.y * resolution + direction.x;
                    if (world.waterHeights[index + step] > 0f) continue;
                    float relief = Mathf.Abs(world.heights[index + step * 3] - world.waterHeights[index]);
                    float score = wet * .2f - relief * .12f - p.sqrMagnitude * .0000001f;
                    if (score <= best) continue;
                    best = score; level = world.waterHeights[index];
                    bank = p; towardLand = new Vector2(direction.x, direction.y);
                }
            }
            if (best == float.MinValue) throw new InvalidOperationException("No inland bank available for " + world.preset + " review.");
            float bearing = Mathf.Atan2(-towardLand.x, -towardLand.y) * Mathf.Rad2Deg;
            var target = new Vector3(bank.x, level + 2f, bank.y);
            game.view.FocusPoint(target, 300f, 43f, bearing + 25f);
            Capture(game, camera, world.preset + "-river-bank-oblique");
            game.view.FocusPoint(target, 350f, 82f, bearing);
            Capture(game, camera, world.preset + "-river-bank-overhead");
            game.view.FocusPoint(target, 850f, 55f, bearing + 80f);
            Capture(game, camera, world.preset + "-river-reach");
            File.AppendAllText("Logs/real-landscape-review.txt", $"Inland bank target: {world.preset} {bank}, water={level:0.00}m, native terrain={game.valley.Terrain.terrainData.heightmapResolution}.\n");
        }

        static void CaptureNaturalCoasts(CareerGame game, Camera camera)
        {
            var world = game.State.world;
            if (!world.HasOcean || world.landCover == null) return;
            const int resolution = ValleyWorld.Resolution;
            var directions = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            var axes = new[] { Vector2Int.right, Vector2Int.up };
            var components = LandComponents(world.landCover, out int mainland);
            Vector2 steepCoast = default, steepSea = default, motu = default, motuSea = default;
            float steepScore = -1f, motuScore = -1f;
            for (int z = 20; z < resolution - 20; z++)
            for (int x = 20; x < resolution - 20; x++)
            {
                int index = z * resolution + x;
                if (world.landCover[index] == 80) continue;
                var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
                if (!NaturalCoastClearOfRoads(world, p)) continue;
                foreach (var direction in directions)
                {
                    int step = direction.y * resolution + direction.x;
                    if (world.landCover[index + step] != 80) continue;
                    float relief = 0f;
                    for (int inland = 2; inland <= 6; inland++)
                        relief = Mathf.Max(relief, world.heights[index - step * inland] - world.SeaLevel);
                    if (relief <= steepScore) continue;
                    steepScore = relief;
                    steepCoast = p + new Vector2(direction.x, direction.y) * world.Spacing * .5f;
                    steepSea = new Vector2(direction.x, direction.y);
                }
                if (world.heights[index] > world.SeaLevel + 6f || components[index] == mainland) continue;
                foreach (var across in axes)
                {
                    int crossStep = across.y * resolution + across.x;
                    int near = FirstWater(world, index, crossStep), far = FirstWater(world, index, -crossStep);
                    if (near + far > 8) continue;
                    int alongStep = across.x == 1 ? resolution : 1, length = 0;
                    for (int along = -6; along <= 6; along++) if (world.landCover[index + along * alongStep] != 80) length++;
                    if (length < 7) continue; // Inspect a real narrow motu, rather than an isolated one-pixel rock.
                    float score = length * 10f - near - far;
                    if (score <= motuScore) continue;
                    motuScore = score; motu = p;
                    motuSea = new Vector2(across.x, across.y) * (near <= far ? 1f : -1f);
                }
            }
            if (steepScore < 0f || motuScore < 0f)
                throw new InvalidOperationException("The tropical review could not find both an unworked steep coast and narrow motu away from regional roads.");
            float steepBearing = Mathf.Atan2(-steepSea.x, -steepSea.y) * Mathf.Rad2Deg;
            game.view.FocusPoint(new Vector3(steepCoast.x, world.SeaLevel + 3f, steepCoast.y), 230f, 40f, steepBearing + 18f);
            Capture(game, camera, "Tropical-natural-coast-oblique");
            game.view.FocusPoint(new Vector3(steepCoast.x, world.SeaLevel + 1f, steepCoast.y), 250f, 83f, steepBearing);
            Capture(game, camera, "Tropical-natural-coast-overhead");
            float motuBearing = Mathf.Atan2(-motuSea.x, -motuSea.y) * Mathf.Rad2Deg;
            game.view.FocusPoint(new Vector3(motu.x, world.SeaLevel + 1f, motu.y), 210f, 43f, motuBearing + 18f);
            Capture(game, camera, "Tropical-narrow-motu-oblique");
            game.view.FocusPoint(new Vector3(motu.x, world.SeaLevel + 1f, motu.y), 240f, 83f, motuBearing);
            Capture(game, camera, "Tropical-narrow-motu-overhead");
            File.AppendAllText("Logs/real-landscape-review.txt", $"Natural shore targets: coast={steepCoast}, relief={steepScore:0.0}m, motu={motu}.\n");
        }

        static int FirstWater(ValleyWorld world, int index, int step)
        {
            for (int distance = 1; distance <= 5; distance++) if (world.landCover[index + step * distance] == 80) return distance;
            return 99;
        }

        static int[] LandComponents(byte[] cover, out int largest)
        {
            const int resolution = ValleyWorld.Resolution;
            var labels = new int[cover.Length]; var queue = new int[cover.Length];
            int label = 0, largestCount = 0; largest = 0;
            for (int origin = 0; origin < cover.Length; origin++)
            {
                if (cover[origin] == 80 || labels[origin] != 0) continue;
                int head = 0, count = 1; label++; queue[0] = origin; labels[origin] = label;
                while (head < count)
                {
                    int i = queue[head++], x = i % resolution, z = i / resolution;
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int next = direction == 0 ? i - 1 : direction == 1 ? i + 1 : direction == 2 ? i - resolution : i + resolution;
                        if (direction == 0 && x == 0 || direction == 1 && x == resolution - 1 ||
                            direction == 2 && z == 0 || direction == 3 && z == resolution - 1) continue;
                        if (cover[next] == 80 || labels[next] != 0) continue;
                        labels[next] = label; queue[count++] = next;
                    }
                }
                if (count > largestCount) { largestCount = count; largest = label; }
            }
            return labels;
        }

        static bool NaturalCoastClearOfRoads(ValleyWorld world, Vector2 p)
        {
            for (int i = 1; i < world.publicRoad.Count; i++)
                if (CareerConstruction.Distance(p, world.publicRoad[i - 1], world.publicRoad[i]) < PublicRoadGeometry.EarthworkReach + 32f) return false;
            return true;
        }
    }
}
