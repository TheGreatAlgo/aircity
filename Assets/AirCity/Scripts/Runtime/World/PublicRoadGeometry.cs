using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Own 3D public-road strips, curved junction kerbs, lane markings, roadbeds and elevated structures.</summary>
    public sealed class PublicRoadGeometry
    {
        public readonly struct Link
        {
            public readonly Vector2 a, b;
            public readonly float heightA, heightB, halfWidth;
            public readonly RoadStructure structure;
            public Link(Vector3 a, Vector3 b, float halfWidth, RoadStructure structure)
            { this.a = PublicRoadNetwork.XZ(a); this.b = PublicRoadNetwork.XZ(b); heightA = a.y; heightB = b.y; this.halfWidth = halfWidth; this.structure = structure; }
        }
        sealed class Arm
        {
            public PublicRoadSegment edge;
            public Vector2 direction;
            public float halfWidth, cut;
        }
        sealed class Junction
        {
            public PublicRoadNode node;
            public readonly List<Arm> arms = new List<Arm>();
            public bool IsJunction;
        }
        public readonly List<Link> links = new List<Link>();
        public readonly PublicRoadNetwork graph;
        public Mesh pavement, shoulders, markings, structures;
        public int SignalJunctionCount { get; private set; }
        readonly Dictionary<int, Junction> junctions = new Dictionary<int, Junction>();
        readonly PublicRoadProfile rural, airport;
        readonly MeshData surface = new MeshData(), shoulder = new MeshData();
        readonly PaintBuilder paint = new PaintBuilder();
        readonly LandscapeMesh concrete = new LandscapeMesh();
        readonly Rect[] roadbedBounds;

        public PublicRoadGeometry(PublicRoadNetwork graph, PublicRoadProfile rural, PublicRoadProfile airport, Func<Vector2, float> groundHeight)
        {
            this.graph = graph; this.rural = rural; this.airport = airport;
            foreach (var node in graph.nodes)
            {
                var joint = new Junction { node = node };
                foreach (var edge in graph.At(node.id))
                {
                    var other = graph.Node(edge.a == node.id ? edge.b : edge.a);
                    float width = Width(edge);
                    joint.arms.Add(new Arm { edge = edge, direction = PublicRoadNetwork.XZ(other.position - node.position).normalized, halfWidth = width * .5f });
                }
                joint.arms.Sort((a, b) => Mathf.Atan2(a.direction.y, a.direction.x).CompareTo(Mathf.Atan2(b.direction.y, b.direction.x)));
                joint.IsJunction = joint.arms.Count >= 3 || (joint.arms.Count == 2 && Vector2.Dot(joint.arms[0].direction, joint.arms[1].direction) > -.985f);
                foreach (var arm in joint.arms)
                {
                    var edge = arm.edge;
                    float length = Vector2.Distance(PublicRoadNetwork.XZ(graph.Node(edge.a).position), PublicRoadNetwork.XZ(graph.Node(edge.b).position));
                    arm.cut = joint.IsJunction ? Mathf.Min(arm.halfWidth + 8f, length * .35f) : 0f;
                }
                if (node.control == JunctionControl.TrafficLights && joint.arms.Count >= 3) SignalJunctionCount++;
                junctions.Add(node.id, joint);
            }
            foreach (var edge in graph.segments)
            {
                var a = graph.Node(edge.a).position; var b = graph.Node(edge.b).position;
                links.Add(new Link(a, b, Width(edge) * .5f, edge.structure));
                BuildSegment(edge, groundHeight);
            }
            foreach (var junction in junctions.Values) if (junction.IsJunction) BuildJunction(junction);
            roadbedBounds = links.Where(l => l.structure == RoadStructure.Ground).Select(l => Rect.MinMaxRect(Mathf.Min(l.a.x, l.b.x) - 32f,
                Mathf.Min(l.a.y, l.b.y) - 32f, Mathf.Max(l.a.x, l.b.x) + 32f, Mathf.Max(l.a.y, l.b.y) + 32f)).ToArray();
            pavement = surface.Apply(null, "Public road pavement"); pavement.RecalculateNormals();
            shoulders = shoulder.Apply(null, "Public road shoulders"); shoulders.RecalculateNormals();
            markings = paint.mesh.Apply(null, "Public road lane markings", true, 4);
            structures = concrete.Create("Public road bridge structures");
        }

        float Width(PublicRoadSegment edge) => edge.style == PublicRoadStyle.Highway || edge.style == PublicRoadStyle.Ramp ? edge.Width :
            edge.style == PublicRoadStyle.AirportAccess && airport != null ? airport.pavementWidth : rural != null ? rural.pavementWidth : edge.Width;
        float ShoulderWidth(PublicRoadSegment edge) => edge.style == PublicRoadStyle.Highway ? 2.5f : edge.style == PublicRoadStyle.Ramp ? .8f :
            edge.style == PublicRoadStyle.AirportAccess && airport != null ? airport.shoulderWidth : rural != null ? rural.shoulderWidth : 1.2f;
        float Crown(PublicRoadSegment edge) => edge.style == PublicRoadStyle.AirportAccess && airport != null ? airport.crown : rural != null ? rural.crown : .065f;
        Arm ArmAt(int node, int edge) => junctions[node].arms.Find(a => a.edge.id == edge);
        static Vector3 World(Vector2 p, float y) => new Vector3(p.x, y, p.y);
        static Vector2 Left(Vector2 p) => new Vector2(-p.y, p.x);
        static Vector2 Right(Vector2 p) => new Vector2(p.y, -p.x);

        float Elevation(PublicRoadSegment edge, Vector2 p)
        {
            var a = graph.Node(edge.a).position; var b = graph.Node(edge.b).position;
            Geo.ClosestOnSegment(p, PublicRoadNetwork.XZ(a), PublicRoadNetwork.XZ(b), out float t);
            float h = Mathf.Lerp(a.y, b.y, Mathf.Clamp01(t));
            foreach (int id in new[] { edge.a, edge.b })
            {
                var j = junctions[id]; if (!j.IsJunction) continue;
                float cut = ArmAt(id, edge.id).cut, d = Vector2.Distance(p, PublicRoadNetwork.XZ(j.node.position));
                float flat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cut, cut + 20f, d));
                h = Mathf.Lerp(h, j.node.position.y, flat);
            }
            return h;
        }

        void BuildSegment(PublicRoadSegment edge, Func<Vector2, float> groundHeight)
        {
            var start = graph.Node(edge.a).position; var end = graph.Node(edge.b).position;
            var a = PublicRoadNetwork.XZ(start); var b = PublicRoadNetwork.XZ(end);
            var delta = b - a; float length = delta.magnitude;
            if (length < .01f) return;
            var axis = delta / length; var right = Right(axis);
            float s0 = ArmAt(edge.a, edge.id).cut, s1 = length - ArmAt(edge.b, edge.id).cut;
            float half = Width(edge) * .5f, verge = ShoulderWidth(edge);
            int steps = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 5f));
            float[] xs = { -half, 0f, half };
            for (int step = 0; step < steps; step++)
            {
                float sa = Mathf.Lerp(s0, s1, step / (float)steps), sb = Mathf.Lerp(s0, s1, (step + 1f) / steps);
                Vector3 At(float s, float x, float extra = 0f) {
                    var p = a + axis * s + right * x;
                    float crossfall = Crown(edge) * Mathf.Clamp01(1f - Mathf.Abs(x) / half);
                    return World(p, Elevation(edge, p) + .18f + crossfall + extra);
                }
                for (int col = 0; col < 2; col++) Quad(surface, At(sa, xs[col]), At(sb, xs[col]), At(sb, xs[col + 1]), At(sa, xs[col + 1]));
                foreach (float side in new[] { -1f, 1f })
                {
                    float inner = half * side, outer = (half + verge) * side;
                    Quad(shoulder, At(sa, inner, -.035f), At(sb, inner, -.035f), At(sb, outer, -.09f), At(sa, outer, -.09f));
                }
            }
            paint.frame = RunwayFrame.Along(a, axis);
            int first = paint.mesh.Count;
            foreach (float side in new[] { -1f, 1f })
            {
                var off = right * (half - .25f) * side;
                paint.Strip(new[] { a + axis * s0 + off, a + axis * s1 + off }, .075f, PaintBuilder.White);
            }
            if (edge.style == PublicRoadStyle.Highway)
            {
                paint.Dashes(a + axis * s0 + right * 4.25f, a + axis * s1 + right * 4.25f, .075f, 3f, 6f, PaintBuilder.White);
                paint.Dashes(a + axis * s0 - right * 4.25f, a + axis * s1 - right * 4.25f, .075f, 3f, 6f, PaintBuilder.White);
                foreach (float side in new[] { -1f, 1f }) paint.Strip(new[] { a + axis * s0 + right * side * .8f, a + axis * s1 + right * side * .8f }, .075f, PaintBuilder.Yellow);
            }
            else if (edge.style != PublicRoadStyle.Ramp) paint.Dashes(a + axis * s0, a + axis * s1, .075f, 3f, 6f, PaintBuilder.Yellow);
            DrapePaint(first, p => Elevation(edge, p) + Crown(edge) * Mathf.Clamp01(1f - Mathf.Abs(Vector2.Dot(p - a, right)) / half));
            if (edge.structure == RoadStructure.Elevated)
            {
                var direction = (end - start).normalized;
                var rotation = Quaternion.LookRotation(direction, Vector3.up);
                concrete.OrientedBox((start + end) * .5f - Vector3.up * .32f, new Vector3(half * 2f + 1f, .8f, length), rotation);
                for (float s = 14f; s < length - 8f; s += 28f)
                {
                    var p = Vector3.Lerp(start, end, s / length); float ground = groundHeight(PublicRoadNetwork.XZ(p));
                    float height = p.y - ground - .75f;
                    bool clear = true;
                    foreach (var lower in graph.segments)
                    {
                        if (lower.structure != RoadStructure.Ground) continue;
                        var nearest = Geo.ClosestOnSegment(PublicRoadNetwork.XZ(p), PublicRoadNetwork.XZ(graph.Node(lower.a).position), PublicRoadNetwork.XZ(graph.Node(lower.b).position), out _);
                        if (Vector2.Distance(nearest, PublicRoadNetwork.XZ(p)) < lower.Width * .5f + 3f) { clear = false; break; }
                    }
                    if (height > 1f && clear) concrete.Box(new Vector3(p.x, ground + height * .5f, p.z), new Vector3(1.4f, height, 2f));
                }
                foreach (float side in new[] { -1f, 1f })
                    concrete.OrientedBox((start + end) * .5f + World(right * side * (half + .3f), .5f), new Vector3(.3f, .8f, length), rotation);
            }
        }

        void BuildJunction(Junction junction)
        {
            var centre = PublicRoadNetwork.XZ(junction.node.position);
            var outline = Outline(junction, 0f); var outside = Outline(junction, 1.2f);
            Fan(shoulder, junction.node.position + Vector3.up * .13f, outside);
            Fan(surface, junction.node.position + Vector3.up * .19f, outline);
            int first = paint.mesh.Count;
            paint.frame = RunwayFrame.None;
            for (int i = 0; i < junction.arms.Count; i++)
            {
                var arm = junction.arms[i]; var next = junction.arms[(i + 1) % junction.arms.Count];
                paint.Strip(Corner(centre, arm, next, -.25f), .075f, PaintBuilder.White);
                if (junction.arms.Count >= 3)
                {
                    var across = Left(arm.direction);
                    var at = centre + arm.direction * (arm.cut + .6f) + across * (arm.halfWidth * .5f);
                    if (junction.node.control == JunctionControl.TrafficLights) paint.Box(at, across, .22f, Mathf.Max(.2f, arm.halfWidth * .5f - .2f), PaintBuilder.White);
                    else if (PublicRoadSignals.Group(graph, junction.node.id, arm.edge.id) == 1)
                        paint.Dashes(at - across * arm.halfWidth * .4f, at + across * arm.halfWidth * .4f, .12f, .5f, .35f, PaintBuilder.White);
                }
            }
            var main = junction.arms.Find(a => PublicRoadSignals.Group(graph, junction.node.id, a.edge.id) == 0);
            if (main != null)
            {
                var opposite = junction.arms.Find(a => a != main && Vector2.Dot(a.direction, main.direction) < -.9f);
                if (opposite != null) paint.Dashes(centre + main.direction * main.cut, centre + opposite.direction * opposite.cut, .075f, 3f, 6f, PaintBuilder.Yellow);
            }
            DrapePaint(first, _ => junction.node.position.y + .02f);
        }

        List<Vector2> Outline(Junction junction, float offset)
        {
            var result = new List<Vector2>(); var centre = PublicRoadNetwork.XZ(junction.node.position);
            for (int i = 0; i < junction.arms.Count; i++)
            {
                var arm = junction.arms[i]; var next = junction.arms[(i + 1) % junction.arms.Count];
                result.Add(centre + arm.direction * arm.cut + Right(arm.direction) * (arm.halfWidth + offset));
                result.AddRange(Corner(centre, arm, next, offset));
            }
            return result;
        }

        static List<Vector2> Corner(Vector2 centre, Arm arm, Arm next, float offset)
        {
            var start = centre + arm.direction * arm.cut + Left(arm.direction) * (arm.halfWidth + offset);
            var end = centre + next.direction * next.cut + Right(next.direction) * (next.halfWidth + offset);
            var result = new List<Vector2> { start };
            if (!Geo.Lines(start, -arm.direction, end, -next.direction, out float t, out _) || t < 0f || t > 60f)
            { result.Add(end); return result; }
            var control = start - arm.direction * t;
            for (int i = 1; i <= 8; i++)
            {
                float u = i / 8f;
                result.Add((1f - u) * (1f - u) * start + 2f * u * (1f - u) * control + u * u * end);
            }
            return result;
        }

        static void Quad(MeshData mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = mesh.Count; var normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.y < 0f) { var swap = b; b = d; d = swap; normal = -normal; }
            foreach (var p in new[] { a, b, c, d })
                mesh.Vertex(p, normal, new Color(0f, 0f, 0f, 1f), new Vector4(p.x, p.z), new Vector4((float)AirfieldKind.Junction, 0f));
            mesh.Quad(0, i, i + 1, i + 2, i + 3);
        }
        static void Fan(MeshData mesh, Vector3 centre, List<Vector2> outline)
        {
            for (int i = 0; i < outline.Count; i++)
            {
                var a = World(outline[i], centre.y); var b = World(outline[(i + 1) % outline.Count], centre.y);
                Quad(mesh, centre, a, b, centre);
            }
        }
        void DrapePaint(int first, Func<Vector2, float> height)
        {
            for (int i = first; i < paint.mesh.positions.Count; i++)
            { var p = paint.mesh.positions[i]; p.y += height(new Vector2(p.x, p.z)) + .18f; paint.mesh.positions[i] = p; }
        }

        public void SampleGround(Vector2 p, out float height, out float distance, out float halfWidth)
        {
            height = 0f; distance = float.MaxValue; halfWidth = 3.25f;
            foreach (var link in links)
            {
                if (link.structure != RoadStructure.Ground) continue;
                var q = Geo.ClosestOnSegment(p, link.a, link.b, out float t); float d = Vector2.Distance(p, q);
                if (d >= distance) continue;
                distance = d; height = Mathf.Lerp(link.heightA, link.heightB, Mathf.Clamp01(t)); halfWidth = link.halfWidth;
            }
            foreach (var j in junctions.Values)
            {
                if (!j.IsJunction || j.arms.All(a => a.edge.structure == RoadStructure.Elevated)) continue;
                float cut = j.arms.Max(a => a.cut), d = Vector2.Distance(p, PublicRoadNetwork.XZ(j.node.position));
                float flat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cut, cut + 20f, d));
                height = Mathf.Lerp(height, j.node.position.y, flat);
            }
        }

        public float[] GradedHeights(ValleyWorld world)
        {
            var heights = new float[world.heights.Length];
            for (int z = 0; z < ValleyWorld.Resolution; z++)
            for (int x = 0; x < ValleyWorld.Resolution; x++)
                heights[z * ValleyWorld.Resolution + x] = GradedHeight(world, x, z);
            return heights;
        }

        /// <summary>The same roadbed as a full rebuild, sampled only inside the edited heightmap rectangle (metres).</summary>
        public float[,] GradedHeightRegion(ValleyWorld world, RectInt region)
        {
            var heights = new float[region.height, region.width];
            for (int z = 0; z < region.height; z++)
            for (int x = 0; x < region.width; x++) heights[z, x] = GradedHeight(world, region.x + x, region.y + z);
            return heights;
        }

        float GradedHeight(ValleyWorld world, int x, int z)
        {
            float original = world.heights[z * ValleyWorld.Resolution + x];
            var p = new Vector2(x * world.Spacing - ValleyWorld.Size * .5f, z * world.Spacing - ValleyWorld.Size * .5f);
            foreach (var box in roadbedBounds)
            {
                if (!box.Contains(p)) continue;
                SampleGround(p, out float h, out float d, out _);
                float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(15f, 31f, d));
                // Leave subgrade clearance for bilinear terrain interpolation at junction mouths.
                return Mathf.Lerp(original, h - .15f, weight);
            }
            return original;
        }
    }
}
