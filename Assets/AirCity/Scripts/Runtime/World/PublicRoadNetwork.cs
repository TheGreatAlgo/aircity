using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.World
{
    public enum PublicRoadStyle { Rural, AirportAccess, Urban, Highway, Ramp }
    public enum RoadStructure { Ground, Elevated }
    public enum JunctionControl { Priority, TrafficLights, Uncontrolled }
    public enum RoadTurn { Straight, Left, Right }
    public enum RoadSignal { Red, Amber, Green }

    [Serializable]
    public sealed class PublicRoadNode
    {
        public int id;
        public Vector3 position;
        public JunctionControl control;
        [Tooltip("Terminal/entrance identifier; roads connect to this node, never directly to the aircraft graph.")]
        public string terminalEntrance;
    }

    [Serializable]
    public sealed class PublicRoadSegment
    {
        public int id, a, b;
        public PublicRoadStyle style;
        public RoadStructure structure;
        public int ForwardLanes => style == PublicRoadStyle.Highway ? 2 : 1;
        public int BackwardLanes => style == PublicRoadStyle.Ramp ? 0 : ForwardLanes;
        public float Width => style == PublicRoadStyle.Highway ? 15.5f : style == PublicRoadStyle.Ramp ? 4.5f : 6.5f;
        public float SpeedLimit => style == PublicRoadStyle.Highway ? 100f : style == PublicRoadStyle.Ramp ? 40f : style == PublicRoadStyle.Rural ? 60f : 30f;
        public PublicRoadSegment Copy() => (PublicRoadSegment)MemberwiseClone();
    }

    public readonly struct RoadLaneConnection
    {
        public readonly int incoming, outgoing, incomingLane, outgoingLane;
        public readonly RoadTurn turn;
        public readonly bool yieldsToOpposingTraffic;
        public RoadLaneConnection(int incoming, int outgoing, int fromLane, int toLane, RoadTurn turn)
        { this.incoming = incoming; this.outgoing = outgoing; incomingLane = fromLane; outgoingLane = toLane; this.turn = turn; yieldsToOpposingTraffic = turn == RoadTurn.Left; }
    }

    /// <summary>Independent, serializable 3D public-road graph. Crossings connect only when their elevations agree.</summary>
    [Serializable]
    public sealed class PublicRoadNetwork
    {
        public const float JunctionHeightTolerance = .35f, CrossingClearance = 6.5f;
        public List<PublicRoadNode> nodes = new List<PublicRoadNode>();
        public List<PublicRoadSegment> segments = new List<PublicRoadSegment>();
        [SerializeField] int nextId = 1;
        public PublicRoadNode Node(int id) => nodes.Find(n => n.id == id);
        public PublicRoadSegment Segment(int id) => segments.Find(e => e.id == id);
        public IEnumerable<PublicRoadSegment> At(int node) => segments.Where(e => e.a == node || e.b == node);
        public static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

        public static PublicRoadNetwork FromWorld(ValleyWorld world)
        {
            var graph = new PublicRoadNetwork();
            int previous = 0;
            foreach (var p in world.publicRoad)
            {
                int node = graph.AddNode(new Vector3(p.x, world.Height(p), p.y));
                if (previous != 0) graph.AddSegment(previous, node, PublicRoadStyle.Rural, RoadStructure.Ground);
                previous = node;
            }
            return graph;
        }

        public PublicRoadNetwork Clone()
        {
            var copy = new PublicRoadNetwork { nextId = nextId };
            foreach (var n in nodes) copy.nodes.Add(new PublicRoadNode { id = n.id, position = n.position, control = n.control, terminalEntrance = n.terminalEntrance });
            foreach (var e in segments) copy.segments.Add(e.Copy());
            return copy;
        }

        public int AddNode(Vector3 position, string terminalEntrance = null)
        {
            var existing = nodes.Find(n => Vector3.Distance(n.position, position) < .1f);
            if (existing != null) { if (terminalEntrance != null) existing.terminalEntrance = terminalEntrance; return existing.id; }
            var node = new PublicRoadNode { id = nextId++, position = position, terminalEntrance = terminalEntrance };
            nodes.Add(node); return node.id;
        }

        public PublicRoadSegment AddSegment(int a, int b, PublicRoadStyle style, RoadStructure structure)
        {
            if (a == b || Node(a) == null || Node(b) == null) throw new ArgumentException("Road endpoints must be two existing nodes.");
            var edge = new PublicRoadSegment { id = nextId++, a = a, b = b, style = style, structure = structure };
            segments.Add(edge); return edge;
        }

        public Vector3 Position(PublicRoadSegment edge, float t) => Vector3.Lerp(Node(edge.a).position, Node(edge.b).position, Mathf.Clamp01(t));

        public bool TrySnap(Vector3 requested, float radius, out PublicRoadSegment edge, out Vector3 point)
        {
            edge = null; point = requested; float best = radius;
            foreach (var candidate in segments)
            {
                var a = Node(candidate.a).position; var b = Node(candidate.b).position;
                float t = Mathf.Clamp01(Vector3.Dot(requested - a, b - a) / Mathf.Max((b - a).sqrMagnitude, .001f));
                var p = Vector3.Lerp(a, b, t); float distance = Vector3.Distance(requested, p);
                if (distance >= best) continue;
                best = distance; edge = candidate; point = p;
            }
            return edge != null;
        }

        public int Split(PublicRoadSegment edge, Vector3 point)
        {
            if (Vector3.Distance(point, Node(edge.a).position) < .1f) return edge.a;
            if (Vector3.Distance(point, Node(edge.b).position) < .1f) return edge.b;
            foreach (int id in new[] { edge.a, edge.b })
            {
                var candidate = Node(id);
                if (Vector3.Distance(point, candidate.position) < 16f && At(id).Count() == 2 && candidate.terminalEntrance == null)
                { candidate.position = point; return id; }
            }
            int node = AddNode(point); int b = edge.b; edge.b = node;
            AddSegment(node, b, edge.style, edge.structure); return node;
        }

        /// <summary>Connect a segment atomically: ground-level crossings split; overpasses stay separate.</summary>
        public bool Connect(int a, int b, PublicRoadStyle style, RoadStructure structure, out string problem)
        {
            problem = "Choose two road nodes at least 1 m apart.";
            if (a == b || Node(a) == null || Node(b) == null || Vector2.Distance(XZ(Node(a).position), XZ(Node(b).position)) < 1f) return false;
            var from = Node(a).position; var to = Node(b).position;
            var crossings = new List<(PublicRoadSegment edge, float t, Vector3 position)>();
            foreach (var edge in segments)
            {
                if (edge.a == a || edge.b == a || edge.a == b || edge.b == b) continue;
                var ea = Node(edge.a).position; var eb = Node(edge.b).position;
                if (!Geo.Segments(XZ(from), XZ(to), XZ(ea), XZ(eb), .05f, out var at)) continue;
                Geo.ClosestOnSegment(at, XZ(from), XZ(to), out float t);
                Geo.ClosestOnSegment(at, XZ(ea), XZ(eb), out float u);
                float y = Mathf.Lerp(from.y, to.y, t), otherY = Mathf.Lerp(ea.y, eb.y, u);
                float clearance = Mathf.Abs(y - otherY);
                if (clearance <= JunctionHeightTolerance)
                {
                    if (style == PublicRoadStyle.Highway || edge.style == PublicRoadStyle.Highway)
                    { problem = "Highways connect through explicit ramp/merge nodes, rather than an automatic street intersection."; return false; }
                    crossings.Add((edge, t, new Vector3(at.x, (y + otherY) * .5f, at.y)));
                }
                else if (clearance < CrossingClearance)
                { problem = $"An overpass needs {CrossingClearance:0.0} m of separation, including its deck."; return false; }
            }
            var stops = new List<(float t, int node)> { (0f, a), (1f, b) };
            foreach (var cross in crossings) stops.Add((cross.t, Split(cross.edge, cross.position)));
            stops.Sort((x, y) => x.t.CompareTo(y.t));
            for (int i = 1; i < stops.Count; i++) if (stops[i - 1].node != stops[i].node) AddSegment(stops[i - 1].node, stops[i].node, style, structure);
            problem = null; return true;
        }

        public bool AddAirportAccess(ValleyWorld world, AirportSite site, IList<Vector2> route, out string problem, bool validateLayout = true)
        {
            problem = null;
            if (validateLayout && !PublicRoadLayout.Validate(world, site, route, out problem)) return false;
            var end = route[route.Count - 1];
            var requested = new Vector3(end.x, world.Height(end), end.y);
            if (!TrySnap(requested, 5f, out var trunk, out var snapped)) { problem = "Finish on an existing public road at the same elevation."; return false; }
            int junction = Split(trunk, snapped); Node(junction).control = JunctionControl.TrafficLights;
            int previous = 0;
            for (int i = 0; i < route.Count; i++)
            {
                var p = route[i];
                int node = i == route.Count - 1 ? junction : AddNode(new Vector3(p.x, i == 0 ? site.elevation : world.Height(p), p.y), i == 0 ? "airport-reception" : null);
                if (previous != 0 && !Connect(previous, node, PublicRoadStyle.AirportAccess, RoadStructure.Ground, out problem)) return false;
                previous = node;
            }
            problem = null; return true;
        }

        public bool HasRoute(int from, int to)
        {
            if (Node(from) == null || Node(to) == null) return false;
            var seen = new HashSet<int> { from }; var queue = new Queue<int>(); queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(); if (current == to) return true;
                foreach (var edge in At(current))
                {
                    int next = edge.a == current ? edge.b : edge.a;
                    if ((edge.a == current || edge.BackwardLanes > 0) && seen.Add(next)) queue.Enqueue(next);
                }
            }
            return false;
        }

        public List<RoadLaneConnection> LaneConnections(int junction)
        {
            var result = new List<RoadLaneConnection>(); var centre = Node(junction).position;
            foreach (var incoming in At(junction))
            foreach (var outgoing in At(junction))
            {
                if (incoming == outgoing) continue;
                int inLanes = incoming.b == junction ? incoming.ForwardLanes : incoming.BackwardLanes;
                int outLanes = outgoing.a == junction ? outgoing.ForwardLanes : outgoing.BackwardLanes;
                var from = XZ(Node(incoming.a == junction ? incoming.b : incoming.a).position - centre).normalized;
                var to = XZ(Node(outgoing.a == junction ? outgoing.b : outgoing.a).position - centre).normalized;
                float turnAngle = Vector2.SignedAngle(-from, to);
                var turn = Mathf.Abs(turnAngle) < 30f ? RoadTurn.Straight : turnAngle > 0f ? RoadTurn.Left : RoadTurn.Right;
                for (int lane = 0; lane < Mathf.Min(inLanes, outLanes); lane++) result.Add(new RoadLaneConnection(incoming.id, outgoing.id, lane, lane, turn));
            }
            return result;
        }

        public bool Validate(out string problem)
        {
            problem = "Invalid public-road network.";
            if (nodes == null || segments == null || nodes.Count > 4096 || segments.Count > 8192 || nextId < 1) return false;
            var ids = new HashSet<int>();
            foreach (var n in nodes)
                if (n == null || n.id <= 0 || !ids.Add(n.id) || !ValleyWorld.Finite(n.position.x) || !ValleyWorld.Finite(n.position.y) ||
                    !ValleyWorld.Finite(n.position.z) || Mathf.Abs(n.position.x) > 10000f || Mathf.Abs(n.position.z) > 10000f || n.position.y < -100f || n.position.y > 2000f ||
                    !Enum.IsDefined(typeof(JunctionControl), n.control)) return false;
            var edgeIds = new HashSet<int>();
            foreach (var e in segments)
                if (e == null || e.id <= 0 || !edgeIds.Add(e.id) || e.a == e.b || !ids.Contains(e.a) || !ids.Contains(e.b) ||
                    !Enum.IsDefined(typeof(PublicRoadStyle), e.style) || !Enum.IsDefined(typeof(RoadStructure), e.structure)) return false;
            if (nodes.Any(n => n.id >= nextId) || segments.Any(e => e.id >= nextId)) return false;
            for (int i = 0; i < segments.Count; i++)
            for (int j = i + 1; j < segments.Count; j++)
            {
                var a = segments[i]; var b = segments[j];
                if (a.a == b.a || a.a == b.b || a.b == b.a || a.b == b.b) continue;
                var a0 = Node(a.a).position; var a1 = Node(a.b).position; var b0 = Node(b.a).position; var b1 = Node(b.b).position;
                if (!Geo.Segments(XZ(a0), XZ(a1), XZ(b0), XZ(b1), .05f, out var at)) continue;
                Geo.ClosestOnSegment(at, XZ(a0), XZ(a1), out float ta); Geo.ClosestOnSegment(at, XZ(b0), XZ(b1), out float tb);
                float gap = Mathf.Abs(Mathf.Lerp(a0.y, a1.y, ta) - Mathf.Lerp(b0.y, b1.y, tb));
                if (gap < CrossingClearance)
                { problem = gap <= JunctionHeightTolerance ? "At-grade crossings must share a public junction node." : "The elevated crossing has insufficient clearance."; return false; }
            }
            problem = null; return true;
        }
    }

    /// <summary>Conflict groups for a conventional two-phase junction, with amber and all-red clearance intervals.</summary>
    public static class PublicRoadSignals
    {
        public const float CycleSeconds = 44f;
        public static int Group(PublicRoadNetwork graph, int node, int approach)
        {
            var edges = graph.At(node).ToList();
            if (edges.Count == 0) return 0;
            PublicRoadSegment main = edges[0];
            // The most nearly opposing pair forms the through-road phase; other approaches share the second phase.
            float best = 1f;
            for (int i = 0; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
            {
                var u = Direction(graph, node, edges[i]); var v = Direction(graph, node, edges[j]);
                float dot = Vector2.Dot(u, v); if (dot < best) { best = dot; main = edges[i]; }
            }
            return Mathf.Abs(Vector2.Dot(Direction(graph, node, main), Direction(graph, node, graph.Segment(approach)))) > .75f ? 0 : 1;
        }
        public static Vector2 Direction(PublicRoadNetwork graph, int node, PublicRoadSegment edge) =>
            PublicRoadNetwork.XZ(graph.Node(edge.a == node ? edge.b : edge.a).position - graph.Node(node).position).normalized;
        public static RoadSignal Signal(int group, float seconds)
        {
            float t = Mathf.Repeat(seconds, CycleSeconds);
            if (group == 0) return t < 24f ? RoadSignal.Green : t < 27f ? RoadSignal.Amber : RoadSignal.Red;
            return t >= 28f && t < 40f ? RoadSignal.Green : t >= 40f && t < 43f ? RoadSignal.Amber : RoadSignal.Red;
        }
    }
}
