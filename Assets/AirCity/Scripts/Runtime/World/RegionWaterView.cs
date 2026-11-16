using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.World
{
    /// <summary>Saved ocean or mapped inland water surfaces with terrain depth, restrained ripples and shore wash.</summary>
    public sealed class RegionWaterView : MonoBehaviour
    {
        // Shore wash must sample the same 12.5 m field as the terrain; a half-resolution bed displaces the
        // shallow-water band around narrow reef islands and makes otherwise smooth beaches look angular.
        const int BedResolution = ValleyWorld.Resolution;
        ValleyWorld world;
        TerrainData terrain;
        System.Func<Vector2, float> heightAt;
        Texture2D bedMap;
        Material material;
        Mesh mesh;
        MeshFilter surfaceFilter;
        bool inland;
        int bedResolution;

        public void Build(ValleyWorld source, System.Func<Vector2, float> sampleHeight, TerrainData renderedTerrain = null, bool inlandSurface = false)
        {
            world = source; heightAt = sampleHeight; terrain = renderedTerrain;
            inland = inlandSurface || !source.HasOcean && source.HasInlandWater;
            if (inland ? !source.HasInlandWater : !source.HasOcean) return;
            bedResolution = inland ? ValleyWorld.Resolution : BedResolution;
            var shader = Resources.Load<Shader>("RegionOcean");
            if (shader == null) shader = Shader.Find("AirCity/RegionOcean");
            material = new Material(shader) { name = "Coastal ocean and lagoon", hideFlags = HideFlags.DontSave };
            material.SetVector("_MapOrigin", new Vector4(-ValleyWorld.Size * .5f, -ValleyWorld.Size * .5f, 1f / ValleyWorld.Size, 1f / ValleyWorld.Size));
            material.SetFloat("_SeaLevel", world.SeaLevel);
            material.SetFloat("_Inland", inland ? 1f : 0f);
            if (inland)
            {
                material.SetColor("_DeepColour", world.preset == RegionPreset.Desert ? new Color(.10f,.13f,.085f) : new Color(.025f,.095f,.10f));
                material.SetColor("_ShallowColour", world.preset == RegionPreset.Desert ? new Color(.23f,.25f,.15f) : new Color(.08f,.22f,.19f));
            }
            else if (world.preset != RegionPreset.Tropical)
            {
                material.SetColor("_DeepColour", new Color(.018f, .10f, .16f));
                material.SetColor("_ShallowColour", new Color(.11f, .39f, .39f));
            }
            bedMap = new Texture2D(bedResolution, bedResolution, TextureFormat.RGFloat, false, true) {
                name = "Region sea-bed depth", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            RefreshBed(); material.SetTexture("_BedMap", bedMap);
            if (inland) { BuildInland(); return; }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            float half = ValleyWorld.Size * .5f;
            const int grid = 128;
            for (int z = 0; z <= grid; z++)
            for (int x = 0; x <= grid; x++) vertices.Add(new Vector3(x * ValleyWorld.Size / grid - half, world.SeaLevel + .12f, z * ValleyWorld.Size / grid - half));
            for (int z = 0; z < grid; z++)
            for (int x = 0; x < grid; x++)
            {
                int a = z * (grid + 1) + x, b = a + grid + 1;
                triangles.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
            }
            // The sea extends to the horizon; the playable heightfield has no visible blue square edge.
            float far = ValleyWorld.Size * 4f;
            void Outer(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                int i = vertices.Count;
                foreach (var p in new[] { a, b, c, d }) vertices.Add(new Vector3(p.x, world.SeaLevel + .12f, p.y));
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Outer(new Vector2(-far, -far), new Vector2(-far, far), new Vector2(-half, half), new Vector2(-half, -half));
            Outer(new Vector2(half, -half), new Vector2(half, half), new Vector2(far, far), new Vector2(far, -far));
            Outer(new Vector2(-half, half), new Vector2(-far, far), new Vector2(far, far), new Vector2(half, half));
            Outer(new Vector2(-far, -far), new Vector2(-half, -half), new Vector2(half, -half), new Vector2(far, -far));
            mesh = new Mesh { name = "Coastal ocean", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            PresentMesh("Ocean, lagoon and shore wash");
        }

        void PresentMesh(string name)
        {
            if (surfaceFilter == null)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false); surfaceFilter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            }
            surfaceFilter.sharedMesh = mesh;
        }

        void BuildInland()
        {
            const int subdivision = 4;
            int resolution = ValleyWorld.Resolution, native = (resolution - 1) * subdivision + 1;
            var surface = world.InlandSurfaceHeights;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var indices = new Dictionary<int,int>();
            float Level(int x, int z)
            {
                float px = x / (float)subdivision, pz = z / (float)subdivision;
                int ix = Mathf.Min(resolution - 2, Mathf.FloorToInt(px)), iz = Mathf.Min(resolution - 2, Mathf.FloorToInt(pz));
                float tx = px - ix, tz = pz - iz;
                return Mathf.Lerp(Mathf.Lerp(surface[iz * resolution + ix], surface[iz * resolution + ix + 1], tx),
                    Mathf.Lerp(surface[(iz + 1) * resolution + ix], surface[(iz + 1) * resolution + ix + 1], tx), tz);
            }
            int Vertex(int x, int z)
            {
                int key = z * native + x;
                if (indices.TryGetValue(key, out int existing)) return existing;
                int index = vertices.Count; indices.Add(key,index);
                vertices.Add(new Vector3(x * world.Spacing / subdivision - ValleyWorld.Size * .5f,
                    Level(x,z) + .025f, z * world.Spacing / subdivision - ValleyWorld.Size * .5f));
                return index;
            }
            bool Refine(int x, int z)
            {
                // Fine triangles follow the banks; large open lake interiors retain the cheaper source grid.
                // Fine/coarse shared edges are collinear samples of one bilinear field, with no vertical gaps.
                for (int dz = -1; dz <= 2; dz++)
                for (int dx = -1; dx <= 2; dx++)
                {
                    int sx = x + dx, sz = z + dz;
                    if (sx < 0 || sx >= resolution || sz < 0 || sz >= resolution) continue;
                    if (world.waterHeights[sz * resolution + sx] <= 0f) return true;
                }
                return false;
            }
            for (int z = 0; z < resolution - 1; z++)
            for (int x = 0; x < resolution - 1; x++)
            {
                int sample = z * resolution + x;
                // The support field extends underneath the dry bank. Terrain/collider geometry determines the
                // visible shoreline, so neither a wet-mask square nor a nearest-sample height cuts the water off.
                if (surface[sample] <= 0f || surface[sample + 1] <= 0f ||
                    surface[sample + resolution] <= 0f || surface[sample + resolution + 1] <= 0f) continue;
                int step = Refine(x,z) ? 1 : subdivision;
                for (int dz = 0; dz < subdivision; dz += step)
                for (int dx = 0; dx < subdivision; dx += step)
                {
                    int nx = x * subdivision + dx, nz = z * subdivision + dz;
                    int a = Vertex(nx,nz), b = Vertex(nx,nz + step), c = Vertex(nx + step,nz + step), d = Vertex(nx + step,nz);
                    triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(a); triangles.Add(c); triangles.Add(d);
                }
            }
            mesh = new Mesh { name = "Continuous mapped rivers and lakes", hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            PresentMesh("Mapped inland rivers and lakes");
        }

        public void RefreshTerrain()
        {
            RefreshBed();
            if (!inland) return;
            var previous = mesh;
            BuildInland(); Release(previous);
        }

        public void RefreshBed()
        {
            if (bedMap == null || world == null) return;
            var pixels = new Vector2[bedResolution * bedResolution];
            var distance = new float[pixels.Length];
            // One native terrain read avoids tens of thousands of native height queries on a brush release.
            var heights = terrain != null ? terrain.GetHeights(0, 0, terrain.heightmapResolution, terrain.heightmapResolution) : null;
            var inlandLevels = inland ? world.InlandSurfaceHeights : null;
            int step = heights != null ? (terrain.heightmapResolution - 1) / (bedResolution - 1) : 0;
            for (int z = 0; z < bedResolution; z++)
            for (int x = 0; x < bedResolution; x++)
            {
                var p = new Vector2(x * ValleyWorld.Size / (bedResolution - 1) - ValleyWorld.Size * .5f,
                    z * ValleyWorld.Size / (bedResolution - 1) - ValleyWorld.Size * .5f);
                int i = z * bedResolution + x;
                float height = heights != null && step > 0 ? heights[z * step, x * step] * terrain.size.y : heightAt(p);
                pixels[i].x = height;
                float level = inland ? inlandLevels[i] : world.SeaLevel;
                distance[i] = level > 0f && height <= level + .15f ? 100000f : 0f;
            }
            // Coarse global DEMs often mark a lagoon only as water, without useful bathymetry. A shore-distance
            // colour falloff keeps open water from becoming one luminous flat rectangle; it never edits terrain.
            for (int z = 0; z < bedResolution; z++)
            for (int x = 0; x < bedResolution; x++)
            {
                int i = z * bedResolution + x; float d = distance[i];
                if (x > 0) d = Mathf.Min(d, distance[i - 1] + 1f);
                if (z > 0) d = Mathf.Min(d, distance[i - bedResolution] + 1f);
                if (x > 0 && z > 0) d = Mathf.Min(d, distance[i - bedResolution - 1] + 1.414214f);
                if (x + 1 < bedResolution && z > 0) d = Mathf.Min(d, distance[i - bedResolution + 1] + 1.414214f);
                distance[i] = d;
            }
            for (int z = bedResolution - 1; z >= 0; z--)
            for (int x = bedResolution - 1; x >= 0; x--)
            {
                int i = z * bedResolution + x; float d = distance[i];
                if (x + 1 < bedResolution) d = Mathf.Min(d, distance[i + 1] + 1f);
                if (z + 1 < bedResolution) d = Mathf.Min(d, distance[i + bedResolution] + 1f);
                if (x + 1 < bedResolution && z + 1 < bedResolution) d = Mathf.Min(d, distance[i + bedResolution + 1] + 1.414214f);
                if (x > 0 && z + 1 < bedResolution) d = Mathf.Min(d, distance[i + bedResolution - 1] + 1.414214f);
                distance[i] = d;
                pixels[i].y = d * ValleyWorld.Size / (bedResolution - 1);
            }
            bedMap.SetPixelData(pixels, 0); bedMap.Apply(false, false);
        }

        public void SetClock(float seconds) { if (material != null) material.SetFloat("_RegionTime", seconds); }

        void OnDestroy()
        {
            Release(mesh); Release(material); Release(bedMap);
        }
        static void Release(Object value)
        { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
