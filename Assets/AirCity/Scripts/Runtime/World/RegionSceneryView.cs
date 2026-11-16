using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.World
{
    /// <summary>Small inhabited places around the region's road and coast, batched by shared material.</summary>
    public sealed class RegionSceneryView : MonoBehaviour
    {
        readonly List<Object> owned = new List<Object>();
        readonly List<(Transform boat, Vector3 at, float phase)> boats = new List<(Transform, Vector3, float)>();
        readonly List<GroundCoverExclusion> footprints = new List<GroundCoverExclusion>();
        public IReadOnlyList<GroundCoverExclusion> Footprints => footprints;
        public int HouseCount { get; private set; }
        public bool HasHarbour { get; private set; }
        public Vector2 HarbourPosition { get; private set; }
        Material[] walls, roofs;
        Material glass, wood, gravel, garden, pale, metal;
        ValleyWorld world;
        PublicRoadGeometry roads;
        System.Func<Vector2, float> heightAt;
        List<GroundCoverExclusion> exclusions;

        public void Build(ValleyWorld source, PublicRoadGeometry geometry, System.Func<Vector2, float> ground,
            IEnumerable<GroundCoverExclusion> airportExclusions)
        {
            Clear(); world = source; roads = geometry; heightAt = ground;
            exclusions = airportExclusions != null ? new List<GroundCoverExclusion>(airportExclusions) : new List<GroundCoverExclusion>();
            walls = new[] { Mat("Village limestone", new Color(.68f,.66f,.57f)), Mat("Village plaster", new Color(.80f,.76f,.64f)),
                Mat("Village timber", source.preset == RegionPreset.Tropical ? new Color(.71f,.78f,.70f) : new Color(.42f,.40f,.34f)) };
            roofs = new[] { Mat("Village slate", new Color(.23f,.29f,.30f)), Mat("Village terracotta", new Color(.47f,.27f,.19f)),
                Mat("Village metal", new Color(.40f,.47f,.46f)) };
            glass = Mat("Village windows", new Color(.12f,.24f,.29f), .35f);
            wood = Mat("Garden fences and jetty", new Color(.36f,.28f,.18f));
            gravel = Mat("Village driveways", new Color(.48f,.45f,.37f));
            garden = Mat("Garden beds", new Color(.24f,.34f,.14f));
            pale = Mat("Harbour boats", new Color(.87f,.85f,.73f));
            metal = Mat("Harbour fittings", new Color(.18f,.22f,.23f));
            BuildVillages();
            if (world.HasOcean) BuildHarbour();
        }

        void BuildVillages()
        {
            var wallMesh = new[] { new LandscapeMesh(), new LandscapeMesh(), new LandscapeMesh() };
            var roofMesh = new[] { new LandscapeMesh(), new LandscapeMesh(), new LandscapeMesh() };
            var windows = new LandscapeMesh(); var timber = new LandscapeMesh(); var drives = new LandscapeMesh(); var plants = new LandscapeMesh();
            var rng = new WorldRandom(world.seed ^ 879319);
            foreach (var town in world.settlements)
            {
                if (!RoadAt(town, out var roadPoint, out var tangent)) continue;
                var side = new Vector2(-tangent.y, tangent.x);
                for (int i = 0; i < 22; i++)
                {
                    int row = i / 2;
                    float along = (row - 5f) * 23f + (rng.Next() - .5f) * 8f;
                    float offset = (i % 2 == 0 ? -1f : 1f) * (22f + rng.Next() * 15f);
                    var point = roadPoint + tangent * along + side * offset;
                    float width = 9f + rng.Next() * 6f, length = 11f + rng.Next() * 7f, height = 4.2f + rng.Next() * 2.8f;
                    if (!Buildable(point, Mathf.Max(width,length) * .7f)) continue;
                    int kind = Mathf.Min(2, (int)(rng.Next() * 3f));
                    var facing = Quaternion.LookRotation(new Vector3(side.x * -Mathf.Sign(offset), 0f, side.y * -Mathf.Sign(offset)));
                    float level = HighestCorner(point, width, length, facing);
                    var at = new Vector3(point.x, level, point.y);
                    House(wallMesh[kind], roofMesh[kind], windows, at, facing, width, length, height, world.preset == RegionPreset.Tropical);
                    // A drive follows the actual cut/fill surface all the way to the public road.
                    var entrance = point - side * Mathf.Sign(offset) * length * .5f;
                    var roadEnd = roadPoint + tangent * along + side * Mathf.Sign(offset) * 4.8f;
                    GroundStrip(drives, entrance, roadEnd, 2.1f);
                    var back = at - facing * Vector3.forward * (length * .5f + 5f);
                    for (int gardenRow = 0; gardenRow < 3; gardenRow++)
                    {
                        var bed = back + facing * Vector3.forward * ((gardenRow - 1) * 1.6f);
                        var a = bed - facing * Vector3.right * width * .4f;
                        var b = bed + facing * Vector3.right * width * .4f;
                        GroundStrip(plants, new Vector2(a.x,a.z),new Vector2(b.x,b.z),.45f);
                    }
                    for (int post = 0; post < 5; post++)
                    {
                        var p = back + facing * new Vector3((post / 4f - .5f) * width, 0f, -4f);
                        timber.Box(new Vector3(p.x,heightAt(new Vector2(p.x,p.z)) + .65f,p.z), new Vector3(.15f,1.3f,.15f));
                    }
                    footprints.Add(new GroundCoverExclusion { a = point, b = point, radius = Mathf.Max(width,length) * .7f + 8f });
                    HouseCount++;
                }
            }
            for (int i = 0; i < 3; i++) { Part("Village houses " + i, wallMesh[i], walls[i]); Part("Village roofs " + i, roofMesh[i], roofs[i]); }
            Part("Village glazing", windows, glass); Part("Garden fences", timber, wood);
            Part("Village driveways", drives, gravel); Part("Village gardens", plants, garden);
        }

        bool RoadAt(Vector2 point, out Vector2 nearest, out Vector2 direction)
        {
            nearest = point; direction = Vector2.up; float best = float.MaxValue;
            if (roads == null) return false;
            foreach (var link in roads.links)
            {
                if (link.structure != RoadStructure.Ground) continue;
                var d = link.b - link.a; float t = Mathf.Clamp01(Vector2.Dot(point - link.a,d) / Mathf.Max(.001f,d.sqrMagnitude));
                var p = link.a + d * t; float distance = (point - p).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; nearest = p; direction = d.normalized;
            }
            return best < 300f * 300f;
        }

        bool Buildable(Vector2 p, float radius)
        {
            if (!world.RegionBounds.Contains(p) || world.IsWater(p)) return false;
            float h = heightAt(p);
            if (world.HasOcean && h < world.SeaLevel + 2f) return false;
            foreach (var d in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
            { var q = p + d * radius; if (!world.RegionBounds.Contains(q) || world.IsWater(q) || Mathf.Abs(heightAt(q) - h) > 2.2f) return false; }
            foreach (var e in exclusions)
                if (Distance(p,e.a,e.b) < radius + e.radius + 6f) return false;
            foreach (var tree in world.trees)
                if (!tree.removed && (tree.position - p).sqrMagnitude < (radius + 3f) * (radius + 3f)) return false;
            return true;
        }

        float HighestCorner(Vector2 p, float width, float length, Quaternion rotation)
        {
            float h = heightAt(p);
            for (int i = 0; i < 4; i++)
            {
                var d = rotation * new Vector3((i % 2 == 0 ? -1 : 1) * width * .5f,0,(i < 2 ? -1 : 1) * length * .5f);
                h = Mathf.Max(h,heightAt(p + new Vector2(d.x,d.z)));
            }
            return h + .08f;
        }

        void House(LandscapeMesh wallsMesh, LandscapeMesh roofsMesh, LandscapeMesh windows, Vector3 at,
            Quaternion q, float width, float length, float height, bool veranda)
        {
            float footing = 2.4f;
            wallsMesh.OrientedBox(at + Vector3.up * (height - footing) * .5f, new Vector3(width,height + footing,length),q);
            Vector3 P(float x, float y, float z) => at + q * new Vector3(x,y,z);
            float w = width * .5f + .75f, l = length * .5f + .8f, ridge = height + width * .23f;
            roofsMesh.Quad(P(-w,height,-l),P(-w,height,l),P(0,ridge,l),P(0,ridge,-l));
            roofsMesh.Quad(P(0,ridge,-l),P(0,ridge,l),P(w,height,l),P(w,height,-l));
            wallsMesh.Triangle(P(-width*.5f,height,length*.5f),P(width*.5f,height,length*.5f),P(0,ridge,length*.5f));
            wallsMesh.Triangle(P(width*.5f,height,-length*.5f),P(-width*.5f,height,-length*.5f),P(0,ridge,-length*.5f));
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * width * .27f;
                windows.Quad(P(x-.8f,1.8f,length*.5f+.025f),P(x+.8f,1.8f,length*.5f+.025f),P(x+.8f,3.1f,length*.5f+.025f),P(x-.8f,3.1f,length*.5f+.025f));
                windows.Quad(P(x+.8f,1.8f,-length*.5f-.025f),P(x-.8f,1.8f,-length*.5f-.025f),P(x-.8f,3.1f,-length*.5f-.025f),P(x+.8f,3.1f,-length*.5f-.025f));
            }
            if (!veranda) return;
            roofsMesh.OrientedBox(P(0,3.4f,length*.5f+1.8f),new Vector3(width+1,.22f,3.6f),q);
            for (int i = 0; i < 3; i++) wallsMesh.OrientedBox(P((i-1)*width*.42f,1.7f,length*.5f+3.2f),new Vector3(.2f,3.4f,.2f),q);
        }

        void GroundStrip(LandscapeMesh mesh, Vector2 a, Vector2 b, float halfWidth)
        {
            var side = new Vector2(-(b-a).y,(b-a).x).normalized * halfWidth;
            int steps = Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b) / 4f));
            Vector3 P(Vector2 p) => new Vector3(p.x,heightAt(p)+.035f,p.y);
            for (int i = 0; i < steps; i++)
            { var p = Vector2.Lerp(a,b,i/(float)steps); var q = Vector2.Lerp(a,b,(i+1f)/steps); mesh.Quad(P(p+side),P(q+side),P(q-side),P(p-side)); }
        }

        void BuildHarbour()
        {
            // The generated island road ends at its outside ferry connection. Find the adjacent sheltered water.
            Vector2 land = default, wet = default; float best = float.MaxValue;
            if (world.publicRoad.Count < 2) return;
            foreach (var end in new[] { world.publicRoad[0],world.publicRoad[world.publicRoad.Count-1] })
            for (int angle = 0; angle < 32; angle++)
            for (float radius = 24f; radius <= 220f; radius += 12f)
            {
                var direction = new Vector2(Mathf.Cos(angle*Mathf.PI/16f),Mathf.Sin(angle*Mathf.PI/16f));
                var p = end + direction * radius;
                if (!world.RegionBounds.Contains(p) || !world.IsWater(p)) continue;
                float depth = world.SeaLevel - heightAt(p);
                if (depth < .8f || depth > 14f || radius >= best) continue;
                land = end; wet = p; best = radius;
            }
            if (best == float.MaxValue || !Buildable(land,7f)) return;
            var direction3 = new Vector3(wet.x-land.x,0,wet.y-land.y).normalized;
            var qrot = Quaternion.LookRotation(direction3); float length = best + 38f;
            var origin = new Vector3(land.x,world.SeaLevel+1.65f,land.y);
            var dock = new LandscapeMesh(); var fittings = new LandscapeMesh();
            dock.OrientedBox(origin+direction3*length*.5f,new Vector3(5.5f,.5f,length),qrot);
            dock.OrientedBox(origin+direction3*(length-6f),new Vector3(36f,.5f,5.5f),qrot);
            for (float z = 8f; z < length; z += 12f)
            for (int side = -1; side <= 1; side += 2)
            {
                var p = origin+qrot*new Vector3(side*2.25f,0,z);
                float bed = Mathf.Min(world.SeaLevel-.5f,heightAt(new Vector2(p.x,p.z)));
                dock.Box(new Vector3(p.x,(bed+origin.y)*.5f,p.z),new Vector3(.42f,origin.y-bed,.42f));
                fittings.Box(p+Vector3.up*.5f,new Vector3(.22f,.75f,.22f));
            }
            Part("Ferry harbour jetty",dock,wood); Part("Harbour bollards",fittings,metal);
            for (int i = 0; i < 4; i++)
            {
                var p = origin+qrot*new Vector3((i-1.5f)*9f,0,length+3f);
                if (!world.IsWater(new Vector2(p.x,p.z))) continue;
                var boat = CreateBoat(i);
                boat.transform.localPosition = new Vector3(p.x,world.SeaLevel+.15f,p.z);
                boat.transform.localRotation = qrot;
                boats.Add((boat.transform,boat.transform.localPosition,i*1.7f));
            }
            HasHarbour = true; HarbourPosition = land;
            footprints.Add(new GroundCoverExclusion { a = land,b = wet,radius = 9f });
        }

        GameObject CreateBoat(int variant)
        {
            var hull = new LandscapeMesh(); var deck = new LandscapeMesh(); var cabin = new LandscapeMesh();
            float length = 8f + variant;
            var a = new Vector3(-1.6f,0,-length*.45f); var b = new Vector3(1.6f,0,-length*.45f);
            var c = new Vector3(1.5f,0,length*.25f); var tip = new Vector3(0,0,length*.55f); var d = new Vector3(-1.5f,0,length*.25f);
            hull.Quad(a,d,c,b); hull.Triangle(d,tip,c);
            foreach (var pair in new[] { (a,b),(b,c),(c,tip),(tip,d),(d,a) })
                hull.Quad(pair.Item2+Vector3.down*.7f,pair.Item1+Vector3.down*.7f,pair.Item1,pair.Item2);
            deck.Box(new Vector3(0,.28f,-length*.13f),new Vector3(2.5f,.56f,length*.5f));
            cabin.Box(new Vector3(0,1.25f,-length*.14f),new Vector3(2f,1.3f,2.4f));
            var root = new GameObject("Moored fishing boat") { hideFlags = HideFlags.DontSave }; root.transform.SetParent(transform,false);
            Part("Fishing hull",hull,variant%2==0?pale:roofs[1],root.transform);
            Part("Fishing deck",deck,wood,root.transform); Part("Wheelhouse",cabin,pale,root.transform);
            return root;
        }

        public void SetClock(float seconds)
        {
            foreach (var item in boats)
            { if (item.boat != null) item.boat.localPosition = item.at + Vector3.up * (Mathf.Sin(seconds*.7f+item.phase)*.09f); }
        }

        Material Mat(string name, Color colour, float smoothness = .08f)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name,hideFlags = HideFlags.DontSave };
            material.SetColor("_BaseColor",colour); material.SetFloat("_Smoothness",smoothness); owned.Add(material); return material;
        }
        void Part(string name, LandscapeMesh source, Material material, Transform parent = null)
        {
            var mesh = source.Create(name); mesh.hideFlags = HideFlags.DontSave; owned.Add(mesh);
            if (mesh.vertexCount == 0) return;
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave }; go.transform.SetParent(parent!=null?parent:transform,false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.On;
        }
        static float Distance(Vector2 p, Vector2 a, Vector2 b)
        { var d = b-a; return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude))); }
        void Clear()
        {
            for (int i=transform.childCount-1;i>=0;i--) Release(transform.GetChild(i).gameObject);
            foreach (var value in owned) Release(value); owned.Clear(); boats.Clear(); footprints.Clear(); HouseCount=0; HasHarbour=false;
        }
        void OnDestroy() { foreach (var value in owned) Release(value); owned.Clear(); }
        static void Release(Object value)
        { if (value==null) return; if (value is GameObject go) go.SetActive(false); if(Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
