using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>
    /// Taxiway geometry, paint and lights from the network graph:
    ///  - a node joining two edges of the same kind becomes a curve whose centreline radius suits the width's
    ///    aircraft (RunwayRules.TaxiwayTurnRadius), shortened if the edges are short;
    ///  - any other node becomes a junction: a polygon whose corners between neighbouring edges are rounded
    ///    with fillets. A node on a runway treats the runway as two wide edges, so taxiways flare into it (the
    ///    part under the runway is hidden by the runway, which sits higher);
    ///  - paint: yellow centrelines with curved lead-ins through junctions, holding-position markings where a
    ///    taxiway meets a runway;
    ///  - lit edges: blue edge lights along straights, curves and fillets; red stop bars at the holding
    ///    positions of runways with precision lighting;
    ///  - service roads: tighter corners, white edge lines and centre dashes, a give-way marking (double dashed
    ///    line and triangle) before every taxiway they meet, a stop line before runways, never lights;
    ///  - verges (BuildVerges): mown grass bands either side of paved taxiways, stopped short of junctions so
    ///    neighbouring bands never overlap.
    /// Surface submeshes are indexed by PavementSurface.
    /// </summary>
    sealed class TaxiwayBuilder
    {
        const float CentrelineHalfWidth = 0.075f;
        /// <summary>Corner radius where a service road meets anything (kerb-like, not sized for aircraft).</summary>
        const float RoadCornerRadius = 8f;
        const float StraightLightSpacing = 30f, CurveLightSpacing = 10f, LightOffset = 0.6f;

        sealed class Arm
        {
            public TaxiwayEdge edge;      // null: one direction of a runway's centreline
            public Vector2 dir;
            public float hw, length;
            public float cut;             // where the edge's straight strip starts, metres from the node
            public float leftStart, rightStart;
            public readonly List<Vector2> gap = new List<Vector2>();   // outline to the next arm (counter-clockwise)
            public bool Real => edge != null;
            public bool Road => edge != null && edge.road;
        }

        sealed class Joint
        {
            public TaxiwayNode node;
            public Vector2 p;
            public readonly List<Arm> arms = new List<Arm>();
            public List<Vector2> arc;     // curve nodes: the centreline from arms[0] to arms[1]
            public List<Vector2> outline; // junctions
            public Arm ArmOf(TaxiwayEdge e) => arms.Find(a => a.edge == e);
        }

        /// <summary>The edges each vertex owner stands for (SurfaceState): one edge, or a curve's / junction's.</summary>
        public readonly List<TaxiwayNetwork.Owner> owners = new List<TaxiwayNetwork.Owner>();
        /// <summary>The edge each verge vertex owner stands for.</summary>
        public readonly List<TaxiwayEdge> vergeOwners = new List<TaxiwayEdge>();

        readonly TaxiwayNetwork net;
        readonly IReadOnlyList<Runway> runways;
        readonly Dictionary<int, Joint> joints = new Dictionary<int, Joint>();

        public TaxiwayBuilder(TaxiwayNetwork net, IReadOnlyList<Runway> runways)
        {
            this.net = net;
            this.runways = runways;
        }

        public void Build(SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            foreach (var n in net.nodes) joints[n.id] = Layout(n);
            foreach (var e in net.edges)
            {
                Own(surface, paint, new[] { e }, false);
                BuildEdge(e, surface, paint, lights);
            }
            foreach (var j in joints.Values)
            {
                if (j.arc == null && j.outline == null) continue;
                Own(surface, paint, j.arms.Where(a => a.Real).Select(a => a.edge).ToArray(), true);
                if (j.arc != null) BuildCurve(j, surface, paint, lights);
                else BuildJunction(j, surface, paint, lights);
            }
        }

        void Own(SurfaceBuilder surface, PaintBuilder paint, TaxiwayEdge[] edges, bool joint)
        {
            surface.mesh.owner = paint.mesh.owner = owners.Count;
            var owner = new TaxiwayNetwork.Owner { edges = edges, joint = joint };
            surface.state = paint.state = TaxiwayNetwork.StateOf(owner);
            owners.Add(owner);
        }

        // ------------------------------------------------------------------ layout

        Joint Layout(TaxiwayNode node)
        {
            var j = new Joint { node = node, p = Geo.XZ(node.position) };
            foreach (var e in net.EdgesAt(node.id))
            {
                var other = net.Node(e.a == node.id ? e.b : e.a);
                if (other == null) continue;
                var d = Geo.XZ(other.position) - j.p;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                j.arms.Add(new Arm { edge = e, dir = d / len, hw = e.width * 0.5f, length = len });
            }
            if (node.runway != null)
            {
                var r = node.runway;
                r.Locate(node.position, out float s, out _);
                if (r.Length - s > 1f) j.arms.Add(new Arm { dir = r.Axis2D, hw = r.HalfWidthTotal, length = r.Length - s });
                if (s > 1f) j.arms.Add(new Arm { dir = -r.Axis2D, hw = r.HalfWidthTotal, length = s });
            }
            j.arms.Sort((a, b) => Geo.Angle(a.dir).CompareTo(Geo.Angle(b.dir)));
            int real = j.arms.FindAll(a => a.Real).Count;
            if (real == 0 || j.arms.Count == 1) return j;     // nothing here, or a dead end (flat end, cut 0)
            if (real == 2 && j.arms.Count == 2 && TryCurve(j)) return j;
            Junction(j);
            return j;
        }

        bool TryCurve(Joint j)
        {
            var a0 = j.arms[0];
            var a1 = j.arms[1];
            if (Mathf.Abs(a0.edge.width - a1.edge.width) > 0.01f || a0.edge.surface != a1.edge.surface || a0.Road != a1.Road) return false;
            float alpha = Mathf.Acos(Mathf.Clamp(Vector2.Dot(a0.dir, a1.dir), -1f, 1f));
            if (alpha > Mathf.PI - 3f * Mathf.Deg2Rad || alpha < 25f * Mathf.Deg2Rad) return false;
            float radius = RunwayRules.TaxiwayTurnRadius(a0.edge.width);
            float tanHalf = Mathf.Tan(alpha * 0.5f);
            float cut = radius / tanHalf;
            float maxCut = 0.45f * Mathf.Min(a0.length, a1.length);
            if (cut > maxCut)
            {
                cut = maxCut;
                radius = cut * tanHalf;
            }
            if (radius < a0.hw * 1.25f) return false;
            Geo.Fillet(j.p, a0.dir, a1.dir, radius, out var centre);
            j.arc = new List<Vector2>();
            Geo.Arc(j.arc, centre, j.p + a0.dir * cut, j.p + a1.dir * cut, 2f);
            a0.cut = a1.cut = cut;
            return true;
        }

        void Junction(Joint j)
        {
            int n = j.arms.Count;
            for (int i = 0; i < n; i++)
            {
                var a = j.arms[i];
                var b = j.arms[(i + 1) % n];
                float phi = Geo.AngleCcw(a.dir, b.dir) * Mathf.Rad2Deg;
                var la = j.p + Geo.Left(a.dir) * a.hw;
                var rb = j.p + Geo.Right(b.dir) * b.hw;
                a.gap.Clear();
                if (phi < 175f && Geo.Lines(la, a.dir, rb, b.dir, out float ta, out float tb))
                {
                    ta = Mathf.Max(ta, 0f);
                    tb = Mathf.Max(tb, 0f);
                    var corner = la + a.dir * ta;
                    float w = !a.Real ? b.edge.width : !b.Real ? a.edge.width : Mathf.Min(a.edge.width, b.edge.width);
                    float rf = a.Road || b.Road ? RoadCornerRadius : RunwayRules.TaxiwayTurnRadius(w) - w * 0.5f;
                    float tanHalf = Mathf.Tan(phi * 0.5f * Mathf.Deg2Rad);
                    float tdist = rf / tanHalf;
                    float maxT = Mathf.Min(Limit(a) - ta, Limit(b) - tb);
                    if (tdist > maxT)
                    {
                        tdist = Mathf.Max(maxT, 0f);
                        rf = tdist * tanHalf;
                    }
                    if (rf < 0.3f)
                    {
                        a.gap.Add(corner);
                        a.leftStart = ta;
                        b.rightStart = tb;
                    }
                    else
                    {
                        Geo.Fillet(corner, a.dir, b.dir, rf, out var centre);
                        Geo.Arc(a.gap, centre, corner + a.dir * tdist, corner + b.dir * tdist, 1.5f);
                        a.leftStart = ta + tdist;
                        b.rightStart = tb + tdist;
                    }
                }
                else if (phi <= 185f)
                {
                    a.gap.Add(la);
                    if ((rb - la).sqrMagnitude > 1e-4f) a.gap.Add(rb);
                    a.leftStart = 0f;
                    b.rightStart = 0f;
                }
                else
                {
                    // reflex gap: round the outside of the junction around the node
                    Geo.Arc(a.gap, j.p, la, rb, 1.5f);
                    a.leftStart = 0f;
                    b.rightStart = 0f;
                }
            }
            j.outline = new List<Vector2>();
            foreach (var a in j.arms)
            {
                a.cut = Mathf.Min(Mathf.Max(a.leftStart, a.rightStart), Limit(a));
                j.outline.Add(j.p + a.dir * a.cut + Geo.Right(a.dir) * a.hw);
                j.outline.Add(j.p + a.dir * a.cut + Geo.Left(a.dir) * a.hw);
                j.outline.AddRange(a.gap);
            }
        }

        /// <summary>How far along an arm a junction may reach: under half a taxiway edge (the other end needs
        /// room too), or to just short of a runway's end.</summary>
        static float Limit(Arm a) => a.Real ? 0.45f * a.length : Mathf.Max(a.length - 0.5f, 0f);

        // ------------------------------------------------------------------ pieces

        static int Sub(PavementSurface s) => (int)s;

        void BuildEdge(TaxiwayEdge e, SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            var ja = joints[e.a];
            var jb = joints[e.b];
            var armA = ja.ArmOf(e);
            var armB = jb.ArmOf(e);
            if (armA == null || armB == null) return;
            float len = armA.length;
            var u = armA.dir;
            float hw = e.width * 0.5f;
            float s0 = armA.cut, s1 = len - armB.cut;
            bool unpaved = !e.surface.IsPaved();
            if (s1 - s0 > 0.01f)
                surface.Rect(Sub(e.surface), ja.p, u, s0, s1, -hw, hw, AirfieldHeights.Taxiway,
                    AirfieldKind.Taxiway, 0f, unpaved ? SurfaceBuilder.Fringe : 0f, false);
            if (e.road)
            {
                BuildRoadPaint(e, ja, armA, jb, armB, paint);
                return;
            }
            if (unpaved) return;

            // centreline: up to the curve / junction, or across the fillet to the runway edge
            paint.frame = RunwayFrame.Along(ja.p, u);
            float p0 = PaintEnd(ja, armA), p1 = len - PaintEnd(jb, armB);
            if (p1 - p0 > 0.5f)
                paint.Strip(new[] { ja.p + u * p0, ja.p + u * p1 }, CentrelineHalfWidth, PaintBuilder.Yellow);

            Holding(ja, armA, len - armB.cut, paint, lights);
            Holding(jb, armB, len - armA.cut, paint, lights);

            if (e.lit && s1 - s0 > 1f)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / StraightLightSpacing));
                for (int i = 0; i < n; i++)
                {
                    float s = Mathf.Lerp(s0, s1, (i + 0.5f) / n);
                    foreach (float sgn in new[] { -1f, 1f })
                        EdgeLight(lights, ja.p + u * s + Geo.Right(u) * (sgn * (hw + LightOffset)));
                }
            }
        }

        // ------------------------------------------------------------------ service roads

        const float RoadLineHalfWidth = 0.075f, RoadEdgeInset = 0.3f;

        /// <summary>White edge lines and centre dashes along a road's straight, then what it meets at each end.</summary>
        void BuildRoadPaint(TaxiwayEdge e, Joint ja, Arm armA, Joint jb, Arm armB, PaintBuilder paint)
        {
            if (!e.surface.IsPaved()) return;
            float len = armA.length;
            var u = armA.dir;
            float hw = e.width * 0.5f;
            paint.frame = RunwayFrame.Along(ja.p, u);
            float p0 = Mathf.Max(PaintEnd(ja, armA), GiveWayAt(ja, armA)), p1 = len - Mathf.Max(PaintEnd(jb, armB), GiveWayAt(jb, armB));
            if (p1 - p0 > 0.5f)
            {
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var off = Geo.Right(u) * (sgn * (hw - RoadEdgeInset));
                    paint.Strip(new[] { ja.p + u * p0 + off, ja.p + u * p1 + off }, RoadLineHalfWidth, PaintBuilder.White);
                }
                paint.Dashes(ja.p + u * p0, ja.p + u * p1, RoadLineHalfWidth, 3f, 6f, PaintBuilder.White);
            }
            RoadEnd(ja, armA, len - armB.cut, paint);
            RoadEnd(jb, armB, len - armA.cut, paint);
        }

        /// <summary>Where a road meets a taxiway, how far from the node its give-way line is (0: no taxiway).</summary>
        float GiveWayAt(Joint j, Arm arm)
        {
            float t = 0f;
            foreach (var b in j.arms)
            {
                if (b == arm || !b.Real || b.Road) continue;
                float sin = Mathf.Max(Mathf.Abs(Geo.Cross(arm.dir, b.dir)), 0.3f);
                t = Mathf.Max(t, (b.hw + 2f) / sin + 1f);
            }
            return t <= 0f ? 0f : Mathf.Max(t, arm.cut + 0.5f);
        }

        /// <summary>A road's end at a node: a stop line before a runway, a give-way marking before a taxiway.</summary>
        void RoadEnd(Joint j, Arm arm, float room, PaintBuilder paint)
        {
            var u = arm.dir;
            var across = Geo.Right(u);
            float hw = arm.hw;
            var r = j.node.runway;
            if (r != null)
            {
                float sin = Mathf.Max(Mathf.Abs(Geo.Cross(u, r.Axis2D)), 0.15f);
                float t = RunwayRules.HoldingDistance(r.width) / sin;
                if (t + 1f < room) paint.Box(j.p + u * t, across, 0.25f, hw - RoadEdgeInset, PaintBuilder.White);
                return;
            }
            float g = GiveWayAt(j, arm);
            if (g <= 0f || g + 10f > room) return;
            // approaching the node a vehicle drives along -u, keeping right: its lane is on the left of u
            var lane = Geo.Left(u);
            for (int k = 0; k < 2; k++)
            {
                var c = j.p + u * (g + k * 0.6f);
                paint.Dashes(c, c + lane * (hw - RoadEdgeInset), 0.15f, 0.6f, 0.3f, PaintBuilder.White);
            }
            // the give-way triangle in the approach lane, its point towards the oncoming driver
            var mid = j.p + lane * (hw * 0.5f);
            var b0 = mid + u * (g + 3f) + lane * 0.9f;
            var b1 = mid + u * (g + 3f) - lane * 0.9f;
            var apex = mid + u * (g + 7.5f);
            paint.Strip(new[] { b0, b1, apex, b0 }, 0.12f, PaintBuilder.White);
        }

        /// <summary>Where an edge's straight centreline paint stops at a node, in metres from that node.</summary>
        float PaintEnd(Joint j, Arm arm)
        {
            var r = j.node.runway;
            if (r != null)
            {
                float sin = Mathf.Abs(Geo.Cross(arm.dir, r.Axis2D));
                return Mathf.Min((r.HalfWidthTotal + 1f) / Mathf.Max(sin, 0.15f), arm.length);
            }
            if (j.arms.Count == 1) return 1f;
            return arm.cut;
        }

        /// <summary>Runway-holding position marking (two solid, two dashed lines; dashes on the runway side)
        /// and, for precision-lit runways, a red stop bar.</summary>
        void Holding(Joint j, Arm arm, float room, PaintBuilder paint, LightBuilder lights)
        {
            var r = j.node.runway;
            if (r == null) return;
            float sin = Mathf.Max(Mathf.Abs(Geo.Cross(arm.dir, r.Axis2D)), 0.15f);
            float t = RunwayRules.HoldingDistance(r.width) / sin;
            if (t + 1.5f > room) return;
            var u = arm.dir;
            var across = Geo.Right(u);
            var yellow = PaintBuilder.Yellow;
            float hw = arm.hw;
            var c = j.p + u * t;
            paint.Dashes(c - across * hw, c + across * hw, 0.075f, 0.9f, 0.9f, yellow);
            paint.Dashes(c + u * 0.3f - across * hw, c + u * 0.3f + across * hw, 0.075f, 0.9f, 0.9f, yellow);
            paint.Box(c + u * 0.6f, across, 0.075f, hw, yellow);
            paint.Box(c + u * 0.9f, across, 0.075f, hw, yellow);
            if (r.lighting != RunwayLighting.Precision) return;
            int n = Mathf.Max(3, Mathf.FloorToInt(arm.edge.width / 3f) + 1);
            for (int i = 0; i < n; i++)
            {
                var p = c - u * 0.6f + across * Mathf.Lerp(-hw + 0.5f, hw - 0.5f, i / (float)(n - 1));
                lights.Light(p, 0.05f, u, LightBuilder.Off, LightBuilder.Red, 0.9f);
                lights.Inset(p, AirfieldHeights.Taxiway, LightBuilder.Red);
            }
        }

        void BuildCurve(Joint j, SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            var e = j.arms[0].edge;
            float hw = e.width * 0.5f;
            bool unpaved = !e.surface.IsPaved();
            // the arc runs in from arm 0 and out along arm 1: its ends sit square to the straights
            var d0 = -j.arms[0].dir;
            var d1 = j.arms[1].dir;
            surface.Strip(Sub(e.surface), j.arc, hw, AirfieldHeights.Taxiway, AirfieldKind.Taxiway, 0f,
                unpaved ? SurfaceBuilder.Fringe : 0f, d0, d1);
            if (unpaved) return;
            paint.frame = RunwayFrame.None;
            if (e.road)
            {
                foreach (float sgn in new[] { -1f, 1f })
                    paint.Strip(Offset(j.arc, sgn * (hw - RoadEdgeInset), d0, d1), RoadLineHalfWidth, PaintBuilder.White);
                return;
            }
            paint.Strip(j.arc, CentrelineHalfWidth, PaintBuilder.Yellow);
            if (!(j.arms[0].edge.lit && j.arms[1].edge.lit)) return;
            foreach (float sgn in new[] { -1f, 1f })
                LightsAlong(lights, Offset(j.arc, sgn * (hw + LightOffset), d0, d1), CurveLightSpacing, false);
        }

        void BuildJunction(Joint j, SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            // the junction is paved like most of its taxiways (ties: the better surface), so a concrete link
            // doesn't turn a stretch of an asphalt taxiway into concrete
            var count = new int[Surfaces.Count];
            foreach (var a in j.arms)
                if (a.Real) count[(int)a.edge.surface]++;
            var kind = PavementSurface.Concrete;
            foreach (var s in new[] { PavementSurface.Asphalt, PavementSurface.Grass, PavementSurface.Dirt })
                if (count[(int)s] > count[(int)kind]) kind = s;
            surface.Fan(Sub(kind), j.p, j.outline, AirfieldHeights.Taxiway, AirfieldKind.Junction,
                kind.IsPaved() ? 0f : SurfaceBuilder.Fringe);
            if (!kind.IsPaved()) return;

            // lead-in lines between every pair of taxiways a turn can link (not through a runway junction,
            // where the centrelines run straight to the runway edge)
            paint.frame = RunwayFrame.None;
            if (j.node.runway == null)
                for (int i = 0; i < j.arms.Count; i++)
                    for (int k = i + 1; k < j.arms.Count; k++)
                    {
                        var a = j.arms[i];
                        var b = j.arms[k];
                        if (!a.Real || !b.Real || a.Road || b.Road || !a.edge.surface.IsPaved() || !b.edge.surface.IsPaved()) continue;
                        if (Vector2.Angle(a.dir, b.dir) < 50f) continue;
                        var pa = j.p + a.dir * a.cut;
                        var pb = j.p + b.dir * b.cut;
                        var pts = new List<Vector2>();
                        for (int s = 0; s <= 16; s++)
                        {
                            float t = s / 16f;
                            pts.Add((1 - t) * (1 - t) * pa + 2 * (1 - t) * t * j.p + t * t * pb);
                        }
                        paint.Strip(pts, CentrelineHalfWidth, PaintBuilder.Yellow);
                    }

            // edge lights along each gap's outline, where the taxiways on both sides are lit (or one is, next
            // to a runway)
            int n = j.arms.Count;
            for (int i = 0; i < n; i++)
            {
                var a = j.arms[i];
                var b = j.arms[(i + 1) % n];
                bool lit = (a.Real ? a.edge.lit : b.Real && b.edge.lit) && (b.Real ? b.edge.lit : a.Real && a.edge.lit);
                if (!lit) continue;
                var line = new List<Vector2> { j.p + a.dir * a.cut + Geo.Left(a.dir) * a.hw };
                line.AddRange(a.gap);
                line.Add(j.p + b.dir * b.cut + Geo.Right(b.dir) * b.hw);
                var offset = new List<Vector2>();
                for (int k = 0; k < line.Count; k++)
                {
                    var d = (line[Mathf.Min(k + 1, line.Count - 1)] - line[Mathf.Max(k - 1, 0)]);
                    offset.Add(d.sqrMagnitude > 1e-8f ? line[k] + Geo.Right(d.normalized) * LightOffset : line[k]);
                }
                LightsAlong(lights, offset, CurveLightSpacing, true);
            }
        }

        /// <summary>A line x metres to the right of an arc, whose ends run along d0 / d1 (see SurfaceBuilder.Strip).</summary>
        static List<Vector2> Offset(List<Vector2> line, float x, Vector2 d0, Vector2 d1)
        {
            var o = new List<Vector2>(line.Count);
            for (int i = 0; i < line.Count; i++)
            {
                var d = i == 0 ? d0 : i == line.Count - 1 ? d1 : line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                o.Add(d.sqrMagnitude > 1e-8f ? line[i] + Geo.Right(d.normalized) * x : line[i]);
            }
            return o;
        }

        // ------------------------------------------------------------------ verges

        /// <summary>
        /// Mown grass either side of every paved taxiway (TaxiwayEdge.HasVerges), out to its graded strip
        /// (RunwayRules.TaxiwayVerge), one owner per edge. Each band stops where it would run into a crossing
        /// taxiway's verge or a runway's, so verges never overlap; bends and junctions leave small unmown corners.
        /// </summary>
        public void BuildVerges(SurfaceBuilder verges)
        {
            foreach (var e in net.edges)
            {
                if (!e.HasVerges) continue;
                var ja = joints[e.a];
                var jb = joints[e.b];
                var armA = ja.ArmOf(e);
                var armB = jb.ArmOf(e);
                if (armA == null || armB == null) continue;
                float len = armA.length;
                float v = e.VergeHalfWidth;
                float s0 = VergeTrim(ja, armA, v), s1 = len - VergeTrim(jb, armB, v);
                if (s1 - s0 < 2f) continue;
                verges.mesh.owner = vergeOwners.Count;
                vergeOwners.Add(e);
                verges.state = new SurfaceValues { grass = e.vergeGrass, work = e.vergeWork != null ? e.vergeWork.ShaderState : e.vergeStripes };
                verges.Verges(0, ja.p, armA.dir, s0, s1, e.width * 0.5f, v, AirfieldHeights.TaxiwayVerge);
            }
        }

        /// <summary>How far from a node an arm's verges must stop to stay clear of the other arms' verges (and a
        /// runway's): straight continuations don't count, they just meet end to end.</summary>
        float VergeTrim(Joint j, Arm arm, float v)
        {
            float t = 0f;
            foreach (var b in j.arms)
            {
                if (b == arm || Vector2.Angle(arm.dir, b.dir) > 155f) continue;
                float reach = !b.Real ? (j.node.runway != null ? j.node.runway.VergeHalfWidth : b.hw)
                    : b.edge.HasVerges ? b.edge.VergeHalfWidth : b.hw + 1f;
                float sin = Mathf.Max(Mathf.Abs(Geo.Cross(arm.dir, b.dir)), 0.25f);
                float cos = Mathf.Abs(Vector2.Dot(arm.dir, b.dir));
                t = Mathf.Max(t, (reach + v * cos) / sin);
            }
            return Mathf.Min(t, 0.5f * arm.length);
        }

        // ------------------------------------------------------------------ lights

        void LightsAlong(LightBuilder lights, List<Vector2> line, float spacing, bool includeEnds)
        {
            float total = 0f;
            for (int i = 1; i < line.Count; i++) total += (line[i] - line[i - 1]).magnitude;
            if (total < 0.5f) return;
            int n = Mathf.Max(1, Mathf.CeilToInt(total / spacing));
            for (int k = includeEnds ? 0 : 1; k <= (includeEnds ? n : n - 1); k++)
            {
                float target = total * k / n, s = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    float seg = (line[i] - line[i - 1]).magnitude;
                    if (s + seg >= target || i == line.Count - 1)
                    {
                        float f = seg > 1e-5f ? Mathf.Clamp01((target - s) / seg) : 0f;
                        EdgeLight(lights, Vector2.Lerp(line[i - 1], line[i], f));
                        break;
                    }
                    s += seg;
                }
            }
        }

        void EdgeLight(LightBuilder lights, Vector2 p)
        {
            foreach (var r in runways)
                if (r.Contains(Geo.X0Z(p), 2f)) return;
            lights.Light(p, 0.35f, Vector2.zero, LightBuilder.Blue, LightBuilder.Blue, 0.85f);
            lights.Elevated(p, AirfieldHeights.Taxiway, LightBuilder.Blue);
        }
    }
}
