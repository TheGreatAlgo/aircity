using System;
using System.Collections.Generic;
using System.IO;
using AirCity.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.World
{
    /// <summary>Authored native geometry: weathered stones, individual curved blades, compound fronds and woody shrubs.</summary>
    public static class GroundCoverSetup
    {
        public const string LibraryPath = WorldAssetSetup.Root + "/Vegetation/GroundCover.asset";
        const int Version = 2;

        [MenuItem("AirCity/World/Regenerate Rocks and Understorey")]
        public static void Regenerate() { EnsureLibrary(true); AssetDatabase.SaveAssets(); }

        public static GroundCoverLibrary EnsureLibrary(bool rebuild = false)
        {
            Directory.CreateDirectory(WorldAssetSetup.Root + "/Vegetation");
            AssetDatabase.Refresh();
            var library = AssetDatabase.LoadAssetAtPath<GroundCoverLibrary>(LibraryPath);
            if (library == null) { library = ScriptableObject.CreateInstance<GroundCoverLibrary>(); AssetDatabase.CreateAsset(library, LibraryPath); }
            rebuild |= library.generatorVersion != Version;
            var rock = Material("GroundRock", true);
            var plant = Material("Understorey", false);
            var kinds = new[] { GroundCoverKind.Boulder, GroundCoverKind.Boulder, GroundCoverKind.Boulder,
                GroundCoverKind.Stone, GroundCoverKind.Stone, GroundCoverKind.Grass, GroundCoverKind.Grass,
                GroundCoverKind.Grass, GroundCoverKind.Fern, GroundCoverKind.Fern, GroundCoverKind.Shrub, GroundCoverKind.Shrub };
            if (library.models == null || library.models.Length != kinds.Length) { library.models = new GroundCoverModel[kinds.Length]; rebuild = true; }
            for (int i = 0; i < kinds.Length; i++)
            {
                var model = library.models[i] ??= new GroundCoverModel();
                model.name = kinds[i] + " " + (i + 1); model.kind = kinds[i];
                model.material = kinds[i] <= GroundCoverKind.Stone ? rock : plant;
                if (rebuild || model.nearMesh == null) Store(Build(kinds[i], i, false), ref model.nearMesh, library);
                if (rebuild || model.farMesh == null) Store(Build(kinds[i], i, true), ref model.farMesh, library);
                model.bounds = model.nearMesh.bounds;
            }
            library.generatorVersion = Version;
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            return library;
        }

        static void Store(Mesh source, ref Mesh target, GroundCoverLibrary library)
        {
            if (target != null) { EditorUtility.CopySerialized(source, target); Object.DestroyImmediate(source); }
            else { target = source; AssetDatabase.AddObjectToAsset(target, library); }
        }

        static Material Material(string name, bool rock)
        {
            string path = WorldAssetSetup.Root + "/Vegetation/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/AirCity/Shaders/GroundCover.shader")) { name = name, enableInstancing = true };
                mat.SetColor("_Tint", Color.white); mat.SetFloat("_Rock", rock ? 1f : 0f);
                mat.SetFloat("_Wind", rock ? 0f : .055f); AssetDatabase.CreateAsset(mat, path);
            }
            if (rock)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(WorldAssetSetup.Root + "/Textures/RockSurface_Albedo.png");
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(WorldAssetSetup.Root + "/Textures/RockSurface_Normal.png");
                if (texture != null) { mat.SetTexture("_BaseMap", texture); mat.SetFloat("_UseMap", 1f); }
                if (normal != null) mat.SetTexture("_NormalMap", normal);
            }
            EditorUtility.SetDirty(mat); return mat;
        }

        static Mesh Build(GroundCoverKind kind, int variant, bool far)
        {
            var mesh = new Builder(); var rng = new System.Random(19471 + variant * 1777);
            switch (kind)
            {
                case GroundCoverKind.Boulder: Rock(mesh, variant, far, false); break;
                case GroundCoverKind.Stone: Rock(mesh, variant, far, true); break;
                case GroundCoverKind.Grass: Grass(mesh, rng, variant, far); break;
                case GroundCoverKind.Fern: Fern(mesh, rng, far); break;
                case GroundCoverKind.Shrub: Shrub(mesh, rng, far); break;
            }
            return mesh.Create(kind + " " + variant + (far ? " distant" : " detail"));
        }

        static float Range(System.Random rng, float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        static Vector3 Horizontal(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

        static void Rock(Builder mesh, int variant, bool far, bool small)
        {
            int sides = far ? 10 : 24, rings = far ? 6 : 14;
            float seed = variant * 15.71f;
            var points = new int[rings + 1, sides + 1];
            var scale = small ? new Vector3(.25f, .20f, .22f) : new Vector3(1.45f, .94f, 1.08f);
            // Direction-space deformations remain identical in both LODs. Two weathered fracture planes
            // create ledges and unequal sides without the uniform facets of a low-poly sphere.
            var fracture = new Vector3(.8f, .45f, -.36f).normalized;
            var fracture2 = new Vector3(-.41f, .57f, .71f).normalized;
            var fracture3 = new Vector3(.18f, -.15f, -.97f).normalized;
            for (int ring = 0; ring <= rings; ring++)
            for (int side = 0; side <= sides; side++)
            {
                float phi = ring / (float)rings * Mathf.PI, theta = side / (float)sides * Mathf.PI * 2f;
                var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                float noise = Mathf.PerlinNoise(seed + n.x * 2.3f + n.y * .4f, seed + n.z * 2.3f + n.y * 1.7f);
                float detail = Mathf.PerlinNoise(seed + n.x * 6f, seed + n.z * 6f + n.y * 2.1f);
                var p = n * (.85f + noise * .29f + (detail - .5f) * .09f);
                float plane = Vector3.Dot(p, fracture);
                if (plane > .57f) p -= fracture * ((plane - .57f) * .97f);
                plane = Vector3.Dot(p, fracture2);
                if (plane > .69f) p -= fracture2 * ((plane - .69f) * .95f);
                plane = Vector3.Dot(p, fracture3);
                if (plane > .74f) p -= fracture3 * ((plane - .74f) * .91f);
                // A sloping upper bed and an offset side cleft make the silhouette a weathered outcrop,
                // rather than an ellipsoid. Fine rippling softens the old fracture planes.
                float upper = .58f + p.x * .17f + p.z * .10f;
                if (p.y > upper) p.y = upper + (p.y - upper) * .09f;
                p.x += Mathf.Clamp01((p.y + .28f) / 1.1f) * .15f;
                p.z -= Mathf.Max(0f, p.x - .24f) * Mathf.Clamp01(p.y * 2f) * .19f;
                p = Vector3.Scale(p, scale); p.y += small ? .10f : .60f;
                float tone = Mathf.Lerp(.7f, 1.05f, noise), ao = Mathf.Lerp(.53f, 1f, Mathf.InverseLerp(-.15f, small ? .2f : 1.2f, p.y));
                points[ring, side] = mesh.Vertex(p, new Vector2(side / (float)sides, ring / (float)rings), new Color(tone, tone * 1.005f, tone * 1.015f, ao));
            }
            for (int ring = 0; ring < rings; ring++)
            for (int side = 0; side < sides; side++)
            {
                int a = points[ring, side], b = points[ring + 1, side], c = points[ring + 1, side + 1], d = points[ring, side + 1];
                if (ring > 0) mesh.Triangle(a, d, b);
                if (ring < rings - 1) mesh.Triangle(d, c, b);
            }
            mesh.SmoothSeams = true;
        }

        static void Grass(Builder mesh, System.Random rng, int variant, bool far)
        {
            int count = far ? 28 : 90;
            for (int blade = 0; blade < count; blade++)
            {
                // Several unequal tufts leave soil visible between their roots, rather than a solid lawn disk.
                float tuftAngle = blade % 5 * 2.39996f;
                var tuft = Horizontal(tuftAngle) * (.19f + blade % 3 * .25f);
                float angle = Range(rng, 0f, Mathf.PI * 2f), distance = Mathf.Sqrt(Range(rng, 0f, 1f)) * .24f;
                var root = tuft + Horizontal(angle) * distance + Vector3.down * .035f;
                float heading = angle + Range(rng, -1.4f, 1.4f), h = Range(rng, .24f, .78f) * (variant == 7 ? 1.22f : 1f);
                float width = Range(rng, .012f, .032f), lean = Range(rng, .14f, .45f);
                var dir = Horizontal(heading); var across = Vector3.Cross(Vector3.up, dir).normalized;
                int segments = far ? 2 : 4, previousL = -1, previousR = -1;
                Color baseColor = Color.Lerp(new Color(.065f, .135f, .036f), new Color(.17f, .22f, .07f), Range(rng, 0f, 1f));
                bool straw = blade % 19 == 0;
                for (int s = 0; s <= segments; s++)
                {
                    float t = s / (float)segments;
                    var p = root + Vector3.up * h * (t - t * t * .24f) + dir * (lean * t * t);
                    float half = width * .5f * (1f - t) + .0005f;
                    var color = Color.Lerp(baseColor * .62f, straw ? new Color(.28f, .25f, .12f) : baseColor * 1.12f, t);
                    color.a = Mathf.Lerp(.58f, 1f, t);
                    int left = mesh.Vertex(p - across * half, new Vector2(0, t), color), right = mesh.Vertex(p + across * half, new Vector2(1, t), color);
                    if (s > 0) mesh.Quad(previousL, left, right, previousR);
                    previousL = left; previousR = right;
                }
            }
            // Sparse seed stems distinguish the taller sedge patch without a dense top silhouette.
            if (variant == 7 && !far)
            for (int i = 0; i < 4; i++)
            {
                var tip = Horizontal(Range(rng, 0f, 6.28f)) * .18f + Vector3.up * Range(rng, .65f, .95f);
                mesh.Stem(Vector3.zero, tip, .006f, new Color(.18f, .20f, .08f), 4);
                mesh.Leaf(tip - Vector3.up * .1f, tip + Vector3.up * .04f, Vector3.right * .018f, new Color(.16f, .14f, .06f));
            }
        }

        static void Fern(Builder mesh, System.Random rng, bool far)
        {
            int count = far ? 6 : 8;
            for (int frond = 0; frond < count; frond++)
            {
                float a = frond / (float)count * Mathf.PI * 2f + Range(rng, -.25f, .25f);
                var direction = Horizontal(a); var across = Vector3.Cross(Vector3.up, direction);
                float length = Range(rng, .60f, .94f), height = Range(rng, .45f, .75f);
                int pairs = far ? 9 : 17; Vector3 previous = Vector3.down * .03f;
                Vector3 At(float t) => direction * (length * Mathf.Pow(t, 1.35f)) + Vector3.up * (height * Mathf.Sin(t * 2.68f));
                for (int j = 0; j < pairs; j++)
                {
                    float t = Mathf.Lerp(.15f, .96f, j / (float)(pairs - 1)); var p = At(t);
                    mesh.Stem(previous, p, .004f * (1f - t * .65f), new Color(.055f, .115f, .025f), 3); previous = p;
                    float width = Mathf.Pow(Mathf.Sin((t - .09f) / .94f * Mathf.PI), .78f) * .23f * (1f - t * .35f);
                    var tangent = (At(Mathf.Min(1f,t+.015f))-At(Mathf.Max(0f,t-.015f))).normalized;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var end = p + across * width * side + tangent * (width * .27f) - Vector3.up * (width * .11f);
                        Color c = Color.Lerp(new Color(.035f, .095f, .025f), new Color(.075f, .17f, .04f), t);
                        c.a = Mathf.Lerp(.66f, 1f, t);
                        if (far) mesh.Leaf(p, end, tangent * .013f, c);
                        else mesh.Pinna(p, end, tangent * .016f, c);
                    }
                }
                mesh.Leaf(previous, At(1f), across * .014f, new Color(.07f, .16f, .035f));
            }
        }

        static void Shrub(Builder mesh, System.Random rng, bool far)
        {
            int branches = far ? 7 : 13;
            for (int b = 0; b < branches; b++)
            {
                float angle = b * 2.39996f; var dir = Horizontal(angle);
                var joint = dir * Range(rng, .15f, .43f) + Vector3.up * Range(rng, .30f, .65f);
                var tip = dir * Range(rng, .45f, .83f) + Vector3.up * Range(rng, .60f, 1.18f);
                Color wood = new Color(.24f, .20f, .13f, .9f);
                mesh.Stem(Vector3.down * .05f, joint, .018f, wood, 4);
                mesh.Stem(joint, tip, .010f, wood, 3);
                int leaves = far ? 6 : 17;
                for (int k = 0; k < leaves; k++)
                {
                    float t = Range(rng, .05f, 1f), leafAngle = angle + k * 2.39996f;
                    var root = Vector3.Lerp(joint, tip, t);
                    var outDir = Horizontal(leafAngle);
                    var leafEnd = root + outDir * Range(rng, .075f, .15f) + Vector3.up * Range(rng, .01f, .05f);
                    var across = Vector3.Cross(outDir, Vector3.up) * Range(rng, .018f, .038f);
                    Color color = Color.Lerp(new Color(.045f, .105f, .035f), new Color(.12f, .19f, .06f), Range(rng, 0f, 1f));
                    color.a = .78f + t * .22f;
                    mesh.Leaf(root, leafEnd, across, color);
                }
            }
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            public bool SmoothSeams;
            public int Vertex(Vector3 p, Vector2 tex, Color color) { int i = vertices.Count; vertices.Add(p); uv.Add(tex); colors.Add(color); return i; }
            public void Triangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            public void Quad(int a, int b, int c, int d) { Triangle(a, b, c); Triangle(a, c, d); }
            public void Leaf(Vector3 root, Vector3 tip, Vector3 across, Color color)
            {
                // Raised midrib catches light on one side and shades the other; narrow pointed silhouette.
                var middle = Vector3.Lerp(root, tip, .47f);
                int a = Vertex(root, new Vector2(.5f, 0f), color), b = Vertex(middle + across, new Vector2(0f, .5f), color * .94f);
                int c = Vertex(tip, new Vector2(.5f, 1f), color), d = Vertex(middle - across, new Vector2(1f, .5f), color);
                int e = Vertex(middle + Vector3.up * across.magnitude * .22f, new Vector2(.5f, .5f), color * 1.04f);
                Triangle(a, b, e); Triangle(b, c, e); Triangle(c, d, e); Triangle(d, a, e);
            }
            public void Pinna(Vector3 root, Vector3 tip, Vector3 across, Color color)
            {
                // Narrow serrated pinnae attach directly to the rachis. Their repeated tapered outline,
                // fine gaps and arched frond are visible at a few metres without a painted leaf card.
                var centre = Vector3.Lerp(root,tip,.44f) + Vector3.up * .0025f;
                int mid = Vertex(centre, new Vector2(.5f,.5f), color * 1.03f);
                var outline = new List<int>();
                outline.Add(Vertex(root, new Vector2(.5f,0),color));
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int step = 1; step < 7; step++)
                    {
                        float t = side < 0 ? step / 7f : 1f-step / 7f;
                        float edge = Mathf.Pow(Mathf.Sin(t * Mathf.PI),.7f) * (step % 2 == 0 ? .67f : 1f);
                        outline.Add(Vertex(Vector3.Lerp(root,tip,t) + across * (side * edge), new Vector2(side < 0 ? 0 : 1,t), color));
                    }
                    if (side < 0) outline.Add(Vertex(tip, new Vector2(.5f,1),color));
                }
                for (int i = 0; i < outline.Count; i++) Triangle(outline[i],outline[(i+1)%outline.Count],mid);
            }
            public void Stem(Vector3 start, Vector3 end, float radius, Color color, int sides)
            {
                var axis = (end - start).normalized;
                var right = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                var forward = Vector3.Cross(axis, right);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides, b = (s + 1) * Mathf.PI * 2f / sides;
                    var ra = (right * Mathf.Cos(a) + forward * Mathf.Sin(a)) * radius;
                    var rb = (right * Mathf.Cos(b) + forward * Mathf.Sin(b)) * radius;
                    int p0 = Vertex(start + ra, Vector2.zero, color), p1 = Vertex(end + ra * .45f, Vector2.up, color);
                    int p2 = Vertex(end + rb * .45f, Vector2.one, color), p3 = Vertex(start + rb, Vector2.right, color);
                    Quad(p0, p1, p2, p3);
                }
            }
            public Mesh Create(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.SetColors(colors);
                mesh.RecalculateNormals();
                if (SmoothSeams)
                {
                    var normals = mesh.normals;
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        Vector3 normal = normals[i]; int count = 1;
                        for (int j = 0; j < vertices.Count; j++)
                            if (i != j && (vertices[i] - vertices[j]).sqrMagnitude < .000001f) { normal += normals[j]; count++; }
                        if (count > 1) normals[i] = normal.normalized;
                    }
                    mesh.normals = normals;
                }
                mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}
