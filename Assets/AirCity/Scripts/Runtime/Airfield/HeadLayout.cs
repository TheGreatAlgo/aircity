using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// A stand's bridge head (BridgeHead) laid out (Airport space): its centreline from `a` (where the main link comes
    /// in) to `b`, the main link from the terminal (`root`) to `a`, the columns under both, whether it has an upper
    /// storey (any of the stand's bridges on it serves an upper deck), and the shape that bridges keep clear of. Its
    /// bridges stand along its side (Spot), each on a short level stub link (BridgeLayout.head).
    /// </summary>
    public sealed class HeadLayout
    {
        public BridgeHead head;
        public StandLayout stand;
        public Vector2 a, b, root;
        /// <summary>The terminal its main link (or, against it, its near end) reaches, or null (not connected).</summary>
        public Terminal terminal;
        /// <summary>Has it an upper storey (floor JetBridgeRules.UpperFloor)?</summary>
        public bool upper;
        /// <summary>Where the main link's columns and the head's (in pairs across it) stand, once worked out (Columns).</summary>
        public List<Vector2> linkColumns, columns;
        BridgeShape shape;

        public Vector2 Dir => (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : stand.axis;
        public float Length => Vector2.Distance(a, b);
        /// <summary>The main link's length on the ground (0: the head stands against the terminal).</summary>
        public float LinkLength => Vector2.Distance(root, a);
        public bool HasLink => LinkLength > 0.05f;
        public bool Connected => terminal != null;
        /// <summary>Half its width (the upper storey's, a little wider, if it has one).</summary>
        public float HalfWidth => (JetBridgeRules.HeadWidth + (upper ? JetBridgeRules.HeadUpperWider : 0f)) * 0.5f;
        /// <summary>Its Solid.joint, shared with its bridges' stub links (they're built onto it).</summary>
        public int Joint => stand.stand.id + 1;

        /// <summary>A stand's saved head laid out (null if it has none), with an upper storey if any of the stand's
        /// upper-deck bridges stands on it.</summary>
        public static HeadLayout Of(StandLayout stand, IList<Terminal> terminals)
        {
            var h = stand?.stand?.head;
            if (h == null || !h.Exists) return null;
            var l = Of(h, stand, terminals, false);
            foreach (var br in stand.stand.bridges)
                if (br != null && br.upper && l.Touches(stand.At(br.root.x, br.root.y))) l.upper = true;
            return l;
        }

        public static HeadLayout Of(BridgeHead h, StandLayout stand, IList<Terminal> terminals, bool upper)
        {
            var l = new HeadLayout
            {
                head = h,
                stand = stand,
                a = stand.At(h.from.x, h.from.y),
                b = stand.At(h.to.x, h.to.y),
                root = stand.At(h.root.x, h.root.y),
                upper = upper,
            };
            var end = l.HasLink ? l.root : l.a;
            if (terminals != null && l.LinkLength <= JetBridgeRules.MaxLink)
                foreach (var t in terminals)
                    if (t != null && t.corners.Count >= 3 &&
                        (t.Contains(end) || t.ClosestOnWall(end, out _, out _) <= JetBridgeRules.ConnectReach))
                    {
                        l.terminal = t;
                        break;
                    }
            return l;
        }

        /// <summary>Its footprint, counter-clockwise from above.</summary>
        public Vector2[] Outline => Quad(a, b, HalfWidth);

        /// <summary>The main link's footprint, or null if there's none.</summary>
        public Vector2[] LinkFootprint => HasLink ? Quad(root, a, JetBridgeRules.MainLinkWidth * 0.5f) : null;

        static Vector2[] Quad(Vector2 p, Vector2 q, float half)
        {
            var d = (q - p).sqrMagnitude > 1e-8f ? (q - p).normalized : Vector2.up;
            var r = Geo.Right(d) * half;
            var quad = new[] { p + r, q + r, q - r, p - r };
            if (Geo.SignedArea(quad) < 0f) Array.Reverse(quad);
            return quad;
        }

        /// <summary>The nearest point of its walls to a ground point, and how far that is.</summary>
        public float ClosestOnWall(Vector2 p, out Vector2 onWall)
        {
            var o = Outline;
            float best = float.MaxValue;
            onWall = p;
            for (int i = 0; i < o.Length; i++)
            {
                var q = Geo.ClosestOnSegment(p, o[i], o[(i + 1) % o.Length], out _);
                float d = Vector2.Distance(p, q);
                if (d >= best) continue;
                best = d;
                onWall = q;
            }
            return best;
        }

        /// <summary>Does a link's root at this ground point start from the head (on or near its wall, or inside it)?</summary>
        public bool Touches(Vector2 p) => Geo.Inside(Outline, p) || ClosestOnWall(p, out _) <= JetBridgeRules.ConnectReach;

        /// <summary>Where a bridge's rotunda stands t metres along the head from `a`, on its right (side &gt; 0) or left,
        /// and where its stub link meets the head's wall.</summary>
        public (Vector2 rotunda, Vector2 root) Spot(float t, float side)
        {
            var d = Dir;
            var n = Geo.Right(d) * Mathf.Sign(side);
            var c = a + d * t;
            return (c + n * JetBridgeRules.HeadToRotunda, c + n * (JetBridgeRules.HeadWidth * 0.5f));
        }

        /// <summary>
        /// Where its columns stand, off the roads' and taxiways' `pavement` (kept once worked out against it; with none
        /// given, evenly spaced and not kept): under the main link as under a bridge's (BridgeLayout.SpanColumns), and
        /// under the head in pairs, evenly at most JetBridgeRules.LinkSpan apart, its ends overhanging the end pairs by at
        /// most HeadOverhang. False if no spacing keeps them off.
        /// </summary>
        public bool Columns(IList<Vector2[]> pavement)
        {
            if (linkColumns != null && columns != null) return true;
            if (!Compute(pavement, out var link, out var pairs)) return false;
            if (pavement != null)
            {
                linkColumns = link;
                columns = pairs;
                shape = null;
            }
            return true;
        }

        bool Compute(IList<Vector2[]> pavement, out List<Vector2> link, out List<Vector2> pairs)
        {
            pairs = null;
            link = BridgeLayout.SpanColumns(root, HasLink ? (a - root).normalized : Dir, LinkLength, pavement);
            if (link == null) return false;
            var d = Dir;
            float len = Length;
            var r = Geo.Right(d) * JetBridgeRules.HeadColumnX;
            var blocked = new List<(float a, float b)>();
            if (pavement != null)
                foreach (var q in pavement)
                    foreach (var line in new[] { a + r, a - r })
                        if (BridgeLayout.Blocked(q, line, d, len, out var x, out var y)) blocked.Add((x, y));
            bool Free(float s)
            {
                if (s < JetBridgeRules.ColumnFoot || s > len - JetBridgeRules.ColumnFoot) return false;
                foreach (var (x, y) in blocked)
                    if (s > x && s < y) return false;
                return true;
            }
            var at = Stations(len, Free);
            if (at == null) return false;
            pairs = new List<Vector2>();
            foreach (float s in at)
            {
                pairs.Add(a + d * s + r);
                pairs.Add(a + d * s - r);
            }
            return true;
        }

        /// <summary>Column stations along a head `len` long: evenly from near one end to near the other, each moved to
        /// the nearest free spot; more of them if the spans or the ends' overhangs won't do. Null if none do.</summary>
        static List<float> Stations(float len, Func<float, bool> free)
        {
            const float inset = 1.2f, step = 0.25f;
            float span = JetBridgeRules.LinkSpan, over = JetBridgeRules.HeadOverhang;
            int first = Mathf.Max(2, Mathf.CeilToInt((len - 2f * inset) / span) + 1);
            for (int n = first; n <= first + 3; n++)
            {
                var at = new List<float>();
                for (int i = 0; i < n; i++)
                {
                    float s0 = inset + (len - 2f * inset) * i / (n - 1);
                    for (float d = 0f; d <= over; d += step)
                    {
                        if (free(s0 - d)) at.Add(s0 - d);
                        else if (free(s0 + d)) at.Add(s0 + d);
                        else continue;
                        break;
                    }
                }
                if (at.Count < n) continue;
                at.Sort();
                bool ok = at[0] <= over && len - at[at.Count - 1] <= over;
                for (int i = 1; i < at.Count && ok; i++)
                    ok = at[i] - at[i - 1] <= span + 1e-3f && at[i] - at[i - 1] >= 2f * JetBridgeRules.ColumnFoot + 0.5f;
                if (ok) return at;
            }
            return null;
        }

        /// <summary>
        /// The head as boxes over the ground (Solid): the head (both storeys, if it has two), the main link, and the
        /// columns under them. The head and its columns share its Joint with its bridges' stub links (built onto it).
        /// </summary>
        public BridgeShape Shape
        {
            get
            {
                if (shape != null) return shape;
                var link = linkColumns;
                var pairs = columns;
                if (link == null || pairs == null) Compute(null, out link, out pairs);
                var list = new List<Solid>();
                float floor = JetBridgeRules.RotundaFloor;
                float top = upper ? JetBridgeRules.UpperFloor - floor + JetBridgeRules.HeadTop : JetBridgeRules.HeadTop;
                list.Add(new Solid(a, b, HalfWidth, floor, floor, JetBridgeRules.HeadBottom, top, false, Joint));
                if (HasLink)
                {
                    float hw = JetBridgeRules.MainLinkWidth * 0.5f;
                    list.Add(new Solid(root, a, hw, floor, floor, JetBridgeRules.LinkBottom, JetBridgeRules.LinkTop, false));
                    var dl = (a - root).normalized;
                    foreach (var c in link ?? new List<Vector2>())
                        list.Add(new Solid(c - dl * JetBridgeRules.ColumnFoot, c + dl * JetBridgeRules.ColumnFoot, hw, floor, floor, 0f,
                            JetBridgeRules.LinkBottom, true));
                }
                var d = Dir;
                foreach (var c in pairs ?? new List<Vector2>())
                    list.Add(new Solid(c - d * JetBridgeRules.ColumnFoot, c + d * JetBridgeRules.ColumnFoot, JetBridgeRules.ColumnFoot,
                        floor, floor, 0f, JetBridgeRules.HeadBottom, true, Joint));
                return shape = new BridgeShape(list);
            }
        }

        /// <summary>
        /// What's wrong with the head where it stands (BridgeProblem.None if nothing): a taxi-out stand, its columns (or
        /// its main link's) unable to keep off the roads and taxiways, the head or its main link where an aircraft the
        /// stand takes parks, the head over a taxiway or either on another stand's pad, or too near another stand's head
        /// or bridges (`site.neighbours`).
        /// </summary>
        public BridgeProblem Placement(BridgeSite site)
        {
            if (stand.TaxiOut || stand.stand.DeIcing) return BridgeProblem.TaxiOutStand;
            if (!Columns(site.pavement)) return BridgeProblem.ColumnOnPavement;
            var box = Outline;
            var link = LinkFootprint;
            foreach (var t in site.types)
            {
                if (t == null) continue;
                if (BridgeLayout.InTheWay(stand, box, t, JetBridgeRules.ParkClearance)) return BridgeProblem.HeadInTheWay;
                if (link != null && BridgeLayout.InTheWay(stand, link, t, JetBridgeRules.ParkClearance)) return BridgeProblem.LinkInTheWay;
            }
            foreach (var lane in site.taxiways)
                if (Convex.Overlap(box, lane)) return BridgeProblem.OverTaxiway;
            if (site.pads != null)
                foreach (var pad in site.pads)
                    if (Convex.Overlap(box, pad) || (link != null && Convex.Overlap(link, pad))) return BridgeProblem.OnNextStand;
            foreach (var n in site.neighbours)
                if (Shape.Clashes(n)) return BridgeProblem.HitsBridge;
            return BridgeProblem.None;
        }

        // ------------------------------------------------------------------ placing heads

        /// <summary>
        /// A head for bridges to these doors (all on one side of the aircraft; the doors no type the stand takes has are
        /// left out), and the bridges on it, as at most gates with two or three: the head runs along the stand beside
        /// the aircraft, parallel to it, a bridge's reach out from its doors, from just aft of its aftmost bridge to a
        /// little ahead of the foremost door, clear of every type's wings, and a straight main link runs on from its
        /// near end to the terminal (or it stands against it). Heads are tried nearest a 19 m tunnel and shortest first;
        /// on each the bridges are placed in turn from the aftmost door forward (SuggestBridge; the head ends just aft of
        /// the first, so it can park swung back past the end), which is also the order they park in. The first head
        /// whose bridges dock every type with their doors wins, else the first that takes most of them. Null if no head
        /// takes two.
        /// </summary>
        public static (BridgeHead head, List<JetBridge> bridges) Suggest(StandLayout stand, IList<JetBridge> doors, BridgeSite site)
        {
            if (doors == null || stand.TaxiOut || stand.stand.DeIcing) return (null, null);
            var types = site.types;
            var want = new List<JetBridge>();
            foreach (var d in doors)
                if (Design(types, d) != null) want.Add(d);
            if (want.Count < 2) return (null, null);
            foreach (var d in want)
                if (d.side != want[0].side) return (null, null);
            bool upper = want.Exists(d => d.upper);
            // the largest type with the first door: where its doors are, in the stand's frame
            var design = Design(types, want[0]);
            float doorX = 0f, sMin = float.MaxValue, sMax = float.MinValue;
            var along = new Dictionary<JetBridge, float>();
            foreach (var d in want)
            {
                if (!BridgeLayout.TryDoor(design, d, out var door)) continue;
                var f = BridgeLayout.ToStand(stand, BridgeLayout.Sill(stand, design, door));
                along[d] = f.y;
                doorX = f.x;
                sMin = Mathf.Min(sMin, f.y);
                sMax = Mathf.Max(sMax, f.y);
            }
            if (sMin > sMax) return (null, null);
            // placed aftmost door first: the wing leaves it the least room, the ones ahead can angle back past it
            want.Sort((p, q) => (along.TryGetValue(p, out var x) ? x : 0f).CompareTo(along.TryGetValue(q, out var y) ? y : 0f));
            float side = Mathf.Sign(doorX);
            float hw = (JetBridgeRules.HeadWidth + (upper ? JetBridgeRules.HeadUpperWider : 0f)) * 0.5f;
            // each head as long as it could be aft (SuggestBridge trims it to its aftmost bridge)
            var heads = new List<(float score, BridgeHead head)>();
            float sB = sMin - 12f;
            for (float lat = 14f; lat <= 34f; lat += 2f)
            {
                float x = doorX + side * (lat + JetBridgeRules.HeadToRotunda);
                if (x - hw < stand.x0 || x + hw > stand.x1) continue;
                if (!WallAhead(stand, x, sMax, site.terminals, out float sWall)) continue;
                for (float sA = sMax + 2f; ; sA += 3f)
                {
                    bool last = sA >= sWall - 1f;
                    if (last) sA = sWall;
                    float len = sA - sB, link = sWall - sA;
                    if (len > JetBridgeRules.MaxHead + 12f) break;
                    if (link <= JetBridgeRules.MaxLink)
                        heads.Add((0.6f * Mathf.Abs(lat - 24f) + 0.12f * (sA - sMin) + 0.05f * link,
                            new BridgeHead { from = new Vector2(x, sA), to = new Vector2(x, sB), root = new Vector2(x, sWall) }));
                    if (last) break;
                }
            }
            heads.Sort((p, q) => p.score.CompareTo(q.score));
            (BridgeHead head, List<JetBridge> bridges) best = (null, null);
            int tried = 0;
            foreach (var (_, longest) in heads)
            {
                var h = Of(longest, stand, site.terminals, upper);
                if (!h.Connected || InTerminal(h, site.terminals)) continue;
                if (++tried > 30) break;
                var placed = new List<BridgeLayout>();
                var bridges = new List<JetBridge>();
                bool all = true;
                foreach (var d in want)
                {
                    // the first bridge (the aftmost) sets where the head ends
                    var b = Place(h, d, placed, site.With(h), placed.Count == 0, out bool allDock, out var trimmed);
                    if (b == null)
                    {
                        all = false;
                        continue;
                    }
                    h = trimmed;
                    all &= allDock;
                    bridges.Add(b);
                    placed.Add(BridgeLayout.Of(b, stand, site.terminals, h));
                }
                if (bridges.Count < 2) continue;
                if (all && bridges.Count == want.Count) return (h.head, bridges);
                if (best.bridges == null || bridges.Count > best.bridges.Count) best = (h.head, bridges);
            }
            return best;
        }

        /// <summary>The largest type the stand takes with this door (not a light one), or null.</summary>
        static AircraftType Design(IList<AircraftType> types, JetBridge door)
        {
            AircraftType design = null;
            foreach (var t in types)
                if (t != null && !t.Light && BridgeLayout.TryDoor(t, door, out _) && (design == null || t.Wingspan > design.Wingspan)) design = t;
            return design;
        }

        /// <summary>Where a line x across the stand, from s = sFrom on towards the stand's head, first meets a terminal's
        /// wall (within a head's and a main link's reach). False if it doesn't, or starts inside one.</summary>
        static bool WallAhead(StandLayout stand, float x, float sFrom, IList<Terminal> terminals, out float sWall)
        {
            sWall = float.MaxValue;
            var p = stand.At(x, sFrom);
            float reach = JetBridgeRules.MaxHead + JetBridgeRules.MaxLink;
            foreach (var t in terminals)
            {
                if (t == null || t.corners.Count < 3) continue;
                if (t.Contains(p)) return false;
                var o = t.Outline();
                for (int i = 0; i < o.Count; i++)
                {
                    var q0 = o[i];
                    var q1 = o[(i + 1) % o.Count];
                    if (Geo.Lines(p, stand.axis, q0, q1 - q0, out float u, out float v) && u >= 0f && u <= reach && v >= 0f && v <= 1f)
                        sWall = Mathf.Min(sWall, sFrom + u);
                }
            }
            return sWall < float.MaxValue;
        }

        static bool InTerminal(HeadLayout h, IList<Terminal> terminals)
        {
            var o = h.Outline;
            foreach (var t in terminals)
                if (t != null && t.corners.Count >= 3 && Geo.Overlap(o, t.Outline(), 0.3f)) return true;
            return false;
        }

        /// <summary>
        /// A place on a head for a bridge to a door (side, deck, n-th from the front): its rotunda along the side of the
        /// head facing the aircraft, on a stub link. As BridgeLayout.Suggest: it must dock the largest type with that
        /// door without crossing the head or the stand's other bridges (`others`), checked against `site` (whose head is
        /// this one); places that dock every type with the door, the others still docking all they did, come first
        /// (allDock), then for the largest type the tunnel square to the fuselage (the cab turned least) and nearest
        /// 20 m. Null if nowhere along the head works.
        /// </summary>
        public static JetBridge SuggestBridge(HeadLayout h, JetBridge door, IList<BridgeLayout> others, BridgeSite site, out bool allDock) =>
            Place(h, door, others, site, false, out allDock, out _);

        /// <summary>SuggestBridge; with `trim`, the head cut to end just aft of the bridge, whichever place it takes (as
        /// `trimmed`; else the head itself).</summary>
        static JetBridge Place(HeadLayout h, JetBridge door, IList<BridgeLayout> others, BridgeSite site, bool trim, out bool allDock,
            out HeadLayout trimmed)
        {
            allDock = false;
            trimmed = h;
            var stand = h.stand;
            var design = Design(site.types, door);
            if (design == null) return null;
            var mid = (h.a + h.b) * 0.5f;
            float side = Vector2.Dot(stand.ParkPosition(design) - mid, Geo.Right(h.Dir)) >= 0f ? 1f : -1f;
            var candidates = new List<(float score, JetBridge bridge, float t)>();
            float len = h.Length, edge = JetBridgeRules.LinkWidth * 0.5f + 0.2f;
            for (float t = edge; t <= len - edge + 1e-3f; t += 1f)
            {
                if (trim && t + edge < JetBridgeRules.MinHead) continue;
                var (rot, root) = h.Spot(t, side);
                var b = new JetBridge
                {
                    door = door.door, side = door.side, upper = door.upper,
                    rotunda = BridgeLayout.ToStand(stand, rot), root = BridgeLayout.ToStand(stand, root),
                };
                var l = BridgeLayout.Of(b, stand, site.terminals, h);
                var pose = l.Dock(design, out var problem);
                if (problem != BridgeProblem.None) continue;
                candidates.Add((0.3f * Mathf.Abs(pose.length - 20f) + 0.1f * Mathf.Abs(pose.cabTurn), b, t));
            }
            candidates.Sort((p, q) => p.score.CompareTo(q.score));
            JetBridge fallback = null;
            HeadLayout fallbackHead = null;
            int tried = 0;
            var docking = BridgeLayout.Docking(others, site);
            foreach (var (_, b, t) in candidates)
            {
                if (++tried > 40) break;
                var head = trim ? h.Trimmed(t + edge) : h;
                if (trim && head.Placement(site.With(head)) != BridgeProblem.None) continue;
                var hs = trim ? site.With(head) : site;
                var (placed, designDocks, all) = BridgeLayout.Rate(BridgeLayout.Of(b, stand, site.terminals, head), design, others, hs, docking);
                if (!placed || !designDocks) continue;
                if (all)
                {
                    allDock = true;
                    trimmed = head;
                    return b;
                }
                if (fallback == null)
                {
                    fallback = b;
                    fallbackHead = head;
                }
            }
            if (fallbackHead != null) trimmed = fallbackHead;
            return fallback;
        }

        /// <summary>The same head cut to `length` from its near end (an upper storey kept as it is).</summary>
        public HeadLayout Trimmed(float length)
        {
            var to = a + Dir * length;
            var h = new BridgeHead { from = head.from, to = BridgeLayout.ToStand(stand, to), root = head.root };
            var l = new HeadLayout { head = h, stand = stand, a = a, b = to, root = root, terminal = terminal, upper = upper };
            return l;
        }
    }
}
