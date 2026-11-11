using System.Collections.Generic;
using AirCity.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.World
{
    /// <summary>Metre-scale spruce and pine trunks, branching whorls and layered botanical foliage cards.</summary>
    public static class TreeMeshGenerator
    {
        public static Mesh Build(int variant, int level)
        {
            bool pine = variant >= 3;
            var rng = new WorldRandom(7219 + variant * 739);
            var mesh = new Builder();
            float height = 24f, phase = rng.Next() * 6.28f, lean = (rng.Next() - .5f) * .8f;
            Vector3 Stem(float t) => new Vector3(Mathf.Sin(t * 2.5f + phase) * t * .22f + lean * t, t * height, Mathf.Sin(t * 1.8f) * t * .25f);
            int trunkSteps = level == 0 ? 15 : level == 1 ? 10 : 6;
            int sides = level == 0 ? 9 : level == 1 ? 6 : 4;
            for (int i = 0; i < trunkSteps; i++)
            {
                float a = i / (float)trunkSteps, b = (i + 1f) / trunkSteps;
                mesh.Tube(Stem(a), Stem(b), Mathf.Lerp(.34f, .015f, Mathf.Pow(a, .7f)), Mathf.Lerp(.34f, .015f, Mathf.Pow(b, .7f)), sides);
            }
            int rings = pine ? 15 : 21;
            int stride = level == 0 ? 1 : level == 1 ? 2 : 3;
            float bottom = pine ? .44f : .13f;
            for (int ring = 0; ring < rings; ring += stride)
            {
                rng = new WorldRandom(7219 + variant * 739 + ring * 1057);
                float t = Mathf.Lerp(bottom, .96f, ring / (float)(rings - 1)) + (rng.Next() - .5f) * .014f;
                int arms = 6;
                float crown = pine ? Mathf.Sin(Mathf.InverseLerp(.37f, 1.05f, t) * Mathf.PI) * 5.1f : Mathf.Pow(1f - t, .72f) * 5.6f;
                for (int arm = 0; arm < arms; arm++)
                {
                    rng = new WorldRandom(7219 + variant * 739 + ring * 1057 + arm * 37);
                    float angle = arm * Mathf.PI * 2f / arms + ring * 2.39996f + phase + (rng.Next() - .5f) * .55f;
                    var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    var lateral = Vector3.Cross(Vector3.up, outward);
                    float reach = crown * Mathf.Lerp(.76f, 1.19f, rng.Next());
                    var root = Stem(t);
                    float droop = pine ? .3f + rng.Next() * .8f : -.3f - (1f - t) * .9f;
                    var tip = root + outward * reach + Vector3.up * droop;
                    if (level < 2) mesh.Tube(root, tip, Mathf.Lerp(.075f, .018f, t), .009f, level == 0 ? 5 : 3);
                    int clusters = level == 0 ? 4 : level == 1 ? 2 : 1;
                    for (int cluster = 0; cluster < clusters; cluster++)
                    {
                        float f = clusters == 1 ? .65f : Mathf.Lerp(.34f, .96f, cluster / (float)(clusters - 1));
                        var centre = Vector3.Lerp(root, tip, f);
                        var along = (outward + Vector3.up * (pine ? .28f : -.18f)).normalized;
                        float length = Mathf.Max(pine ? .45f : .85f, reach * (level == 2 ? 1.05f : level == 1 ? .68f : .48f)) * (pine ? 1f : 1.15f);
                        float width = length * (pine ? .9f : .84f);
                        int tile = pine ? 2 + (arm + ring) % 2 : (arm + ring) % 2;
                        float shade = Mathf.Lerp(.7f, 1.05f, rng.Next()) * Mathf.Lerp(.75f, 1f, t);
                        mesh.Card(centre, lateral * width, along * length, tile, shade);
                        // Folded cards have volume from above and from the side, unlike crossed whole-tree billboards.
                        if (level < 2)
                        {
                            var folded = Quaternion.AngleAxis((arm % 2 == 0 ? 1f : -1f) * 58f, along) * lateral;
                            mesh.Card(centre, folded * width, along * length, tile, shade);
                        }
                        if (!pine)
                        {
                            var pendant = (Vector3.down * .85f + outward * .35f).normalized;
                            mesh.Card(centre - Vector3.up * .2f, lateral * width, pendant * Mathf.Max(1.15f, length), tile, shade * .9f);
                        }
                    }
                }
            }
            // A narrow, irregular living leader instead of a blunt cut-off crown.
            mesh.Card(Stem(.94f), Vector3.right * .75f, Vector3.up * 1.9f, pine ? 2 : 0, 1f);
            mesh.Card(Stem(.94f), Vector3.forward * .75f, Vector3.up * 1.9f, pine ? 3 : 1, .92f);
            return mesh.Create((pine ? "Pine" : "Spruce") + variant + " LOD" + level);
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int>[] triangles = { new List<int>(), new List<int>() };
            int Vertex(Vector3 p, Vector3 n, Vector2 t, Color color)
            { int id = vertices.Count; vertices.Add(p); normals.Add(n); uv.Add(t); colors.Add(color); return id; }
            void Quad(int sub, int a, int b, int c, int d)
            { triangles[sub].AddRange(new[] { a, b, c, a, c, d }); }
            public void Tube(Vector3 a, Vector3 b, float ra, float rb, int sides)
            {
                var axis = (b - a).normalized;
                var right = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                var forward = Vector3.Cross(right, axis).normalized;
                int first = vertices.Count;
                for (int row = 0; row < 2; row++)
                for (int side = 0; side <= sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / sides;
                    var n = right * Mathf.Cos(angle) + forward * Mathf.Sin(angle);
                    var p = (row == 0 ? a : b) + n * (row == 0 ? ra : rb);
                    Vertex(p, n, new Vector2(side / (float)sides, p.y / 2f), Color.white);
                }
                for (int side = 0; side < sides; side++)
                    Quad(0, first + side, first + side + sides + 1, first + side + sides + 2, first + side + 1);
            }
            public void Card(Vector3 centre, Vector3 width, Vector3 along, int tile, float shade)
            {
                var n = (centre + Vector3.up * 1.5f).normalized;
                int first = vertices.Count; float u = tile % 2 * .5f, v = tile < 2 ? .5f : 0f;
                var color = new Color(shade, shade, shade, Mathf.Clamp01(centre.y / 24f));
                Vertex(centre - width * .5f - along * .38f, n, new Vector2(u + .008f, v + .008f), color);
                Vertex(centre - width * .5f + along * .62f, n, new Vector2(u + .008f, v + .492f), color);
                Vertex(centre + width * .5f + along * .62f, n, new Vector2(u + .492f, v + .492f), color);
                Vertex(centre + width * .5f - along * .38f, n, new Vector2(u + .492f, v + .008f), color);
                Quad(1, first, first + 1, first + 2, first + 3);
            }
            public Mesh Create(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32, subMeshCount = 2 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetColors(colors);
                mesh.SetTriangles(triangles[0], 0); mesh.SetTriangles(triangles[1], 1); mesh.RecalculateBounds();
                var bounds = mesh.bounds; bounds.Expand(1f); mesh.bounds = bounds;
                return mesh;
            }
        }
    }
}
