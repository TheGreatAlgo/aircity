using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// Where the airport has a hard (or at least built) surface to drive on: every runway with its shoulders and
    /// blast pads, taxiway, service road, junction and apron, taken from the generated surface meshes and bucketed
    /// in a coarse grid. The depots' ploughs use it to keep their turns off the snowy grass (VehicleManeuver).
    /// Airport.Pavement rebuilds it after the layout changes.
    /// </summary>
    public sealed class Pavement
    {
        const float Cell = 16f;

        readonly List<Vector2> tris = new List<Vector2>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();

        static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        /// <summary>Add a mesh's triangles; toAirport maps its vertices into the Airport's space.</summary>
        public void Add(Mesh mesh, Matrix4x4 toAirport)
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            var v = mesh.vertices;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var t = mesh.GetTriangles(sub);
                for (int i = 0; i < t.Length; i += 3)
                {
                    var a = Flat(toAirport.MultiplyPoint3x4(v[t[i]]));
                    var b = Flat(toAirport.MultiplyPoint3x4(v[t[i + 1]]));
                    var c = Flat(toAirport.MultiplyPoint3x4(v[t[i + 2]]));
                    if (Mathf.Abs(Cross(b - a, c - a)) < 1e-4f) continue;
                    int index = tris.Count / 3;
                    tris.Add(a);
                    tris.Add(b);
                    tris.Add(c);
                    var min = Vector2.Min(a, Vector2.Min(b, c));
                    var max = Vector2.Max(a, Vector2.Max(b, c));
                    for (int x = Mathf.FloorToInt(min.x / Cell); x <= Mathf.FloorToInt(max.x / Cell); x++)
                        for (int y = Mathf.FloorToInt(min.y / Cell); y <= Mathf.FloorToInt(max.y / Cell); y++)
                        {
                            long k = Key(x, y);
                            if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<int>();
                            list.Add(index);
                        }
                }
            }
        }

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Is this ground point (x, z) on pavement?</summary>
        public bool Contains(Vector2 p)
        {
            if (!grid.TryGetValue(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell)), out var list)) return false;
            foreach (int i in list)
            {
                var a = tris[i * 3];
                var b = tris[i * 3 + 1];
                var c = tris[i * 3 + 2];
                float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
                bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(neg && pos)) return true;
            }
            return false;
        }

        public int TriangleCount => tris.Count / 3;
    }
}
