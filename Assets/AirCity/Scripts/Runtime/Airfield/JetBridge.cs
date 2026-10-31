using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>Why a jet bridge can't serve an aircraft type on its stand (the first reason found).</summary>
    public enum BridgeProblem
    {
        None = 0,
        /// <summary>Light aircraft: their doors are a step off the ground; they board by stairs.</summary>
        SmallAircraft,
        /// <summary>The aircraft hasn't that many doors on its left side.</summary>
        NoSuchDoor,
        /// <summary>The door is closer to the rotunda than the tunnel retracts to.</summary>
        TooClose,
        /// <summary>The door is beyond the tunnel's reach.</summary>
        TooFar,
        /// <summary>The tunnel would slope more than JetBridgeRules.MaxSlope to reach the door's sill.</summary>
        TooSteep,
        /// <summary>The cab would have to turn further than it can to face the door.</summary>
        CabTurn,
        /// <summary>The tunnel would run into a wing, an engine or the tail.</summary>
        HitsWing,
        /// <summary>The tunnel would cross over the fuselage.</summary>
        HitsFuselage,
        /// <summary>It would come within JetBridgeRules.Clearance of another bridge (the stand's, or a neighbour's parked
        /// one) without JetBridgeRules.Headroom between them: on one level, or not high enough to pass over.</summary>
        HitsBridge,
        /// <summary>Retracted out of the way, it still stands where the aircraft parks.</summary>
        NoRoomToPark,
        /// <summary>The rotunda stands where the aircraft parks.</summary>
        RotundaInTheWay,
        /// <summary>The rotunda's column stands on a road or a taxiway.</summary>
        RotundaOnPavement,
        /// <summary>Parked, it stands out over a taxiway, or its drive's wheels on a road.</summary>
        OverTaxiway,
        /// <summary>The fixed link runs over where the aircraft parks.</summary>
        LinkInTheWay,
        /// <summary>It reaches onto the next stand.</summary>
        OnNextStand,
        /// <summary>Taxi-out stands take no bridges: the aircraft turns round on the stand.</summary>
        TaxiOutStand,
        /// <summary>An upper-deck bridge's link is too short to ramp up from the terminal's floor to its raised rotunda
        /// within JetBridgeRules.MaxSlope.</summary>
        LinkTooSteep,
        /// <summary>No spacing of the link's columns keeps them all off the roads and taxiways.</summary>
        ColumnOnPavement,
        /// <summary>The stand's bridge head stands where the aircraft parks.</summary>
        HeadInTheWay,
        /// <summary>Swinging round, retracted, between where it parks and where it docks, it would hit the aircraft.</summary>
        SwingHitsAircraft,
        /// <summary>Its link (or the head's main link) runs through a terminal building from where it leaves the wall.</summary>
        LinkThroughTerminal,
        /// <summary>Its link (or the head's main link) is longer than JetBridgeRules.MaxLink, or doesn't start on a terminal's wall.</summary>
        LinkTooLong,
        /// <summary>The bridge head stands in a terminal building.</summary>
        HeadInTerminal,
    }

    /// <summary>A bridge's tunnel: three telescoping sections of JetBridgeRules.Section(size).</summary>
    public enum TunnelSize
    {
        /// <summary>13 m sections: 14 to 36 m.</summary>
        Long = 0,
        /// <summary>8.5 m sections: 9.5 to 22.5 m. For a bridge close to the aircraft, as on a bridge head: retracted, it
        /// swings round well clear of it.</summary>
        Short = 1,
    }

    public enum DoorSide
    {
        /// <summary>Port: the usual boarding side.</summary>
        Left = 0,
        Right = 1,
    }

    /// <summary>
    /// A passenger boarding bridge, one of a stand's (Stand.bridges; a bridge serves only its own stand). From the
    /// terminal a fixed link runs out to the rotunda, a drum on a column; from the rotunda a three-part telescoping
    /// tunnel on a wheeled drive swings out, extends and tilts to bring the cab, which turns to face the aircraft, to
    /// one of the parked aircraft's doors: on either side, on the main deck or, for a double-deck aircraft, the upper
    /// deck (from a raised rotunda, JetBridgeRules.UpperFloor). A link from the terminal leaves it at the main floor
    /// (JetBridgeRules.RotundaFloor); an upper-deck bridge's ramps up to its raised rotunda. On a stand with a bridge
    /// head (Stand.head) the link is a short level stub from the head's wall instead, at the bridge's own floor.
    ///
    /// Saved in the stand's own frame (StandLayout: x across to the right of its axis, s out along it from its front
    /// line), so it goes wherever the stand goes. Everything else, whether it reaches a terminal and how it docks with
    /// each aircraft type, is worked out (BridgeLayout); terminals and bridges never move each other.
    /// </summary>
    [Serializable]
    public class JetBridge
    {
        [Tooltip("The door it serves, counted from the front on its side and deck: 1 = L1 (or R1, UL1...), 2 = L2...")]
        [Range(1, 6)] public int door = 1;
        [Tooltip("Which side of the aircraft its door is on.")]
        public DoorSide side;
        [Tooltip("An upper-deck door (double-deck aircraft): the bridge stands on a raised rotunda.")]
        public bool upper;
        [Tooltip("Where the rotunda stands, in the stand's frame: x across (right of its axis), s along it from its front line.")]
        public Vector2 rotunda;
        [Tooltip("Where the fixed link meets the terminal (or the stand's bridge head), in the stand's frame. On the " +
                 "rotunda: no link.")]
        public Vector2 root;
        [Tooltip("Its tunnel: long (14-36 m) or short (9.5-22.5 m, for a bridge close to the aircraft, as on a head).")]
        public TunnelSize tunnel;

        /// <summary>"L1", "R2", "UL1" ...</summary>
        public string DoorName => DoorLabel(side, upper, door);

        public static string DoorLabel(DoorSide side, bool upper, int n) => (upper ? "U" : "") + (side == DoorSide.Left ? "L" : "R") + n;

        /// <summary>Its rotunda's (and link's) floor above the ground.</summary>
        public float Floor => upper ? JetBridgeRules.UpperFloor : JetBridgeRules.RotundaFloor;

        /// <summary>Its tunnel's length retracted and fully out.</summary>
        public float MinTunnel => JetBridgeRules.MinTunnelOf(tunnel);
        public float MaxTunnel => JetBridgeRules.MaxTunnelOf(tunnel);

        /// <summary>It parked (retracted): its cab's floor (an upper-deck bridge's raised JetBridgeRules.ParkRise).</summary>
        public BridgePose ParkedPose(Vector2 heading) => new BridgePose
            { heading = heading, length = MinTunnel, cabFloor = Floor + (upper ? JetBridgeRules.ParkRise : 0f), floor = Floor };

        public JetBridge Clone() => (JetBridge)MemberwiseClone();
    }

    /// <summary>
    /// A stand's bridge head (Stand.head): a fixed building on columns that its bridges share, as at most gates with two
    /// or three. One main link (wider than a bridge's) runs from the terminal to the head's near end; the bridges'
    /// rotundas stand along its side on short stub links. It has a second storey, level with the raised rotundas
    /// (JetBridgeRules.UpperFloor), when any of its bridges serves an upper deck, so no link ramps. Saved in the stand's
    /// frame like the bridges; HeadLayout works out the rest.
    /// </summary>
    [Serializable]
    public class BridgeHead
    {
        [Tooltip("The head's centreline, in the stand's frame (x across, s along): from its near end (the main link's) " +
                 "to its far end. The same two points: no head.")]
        public Vector2 from, to;
        [Tooltip("Where the main link meets the terminal, in the stand's frame. At `from`: the head stands against it.")]
        public Vector2 root;

        public bool Exists => (to - from).sqrMagnitude > 1f;

        public BridgeHead Clone() => (BridgeHead)MemberwiseClone();
    }

    /// <summary>Dimensions and limits of the jet bridges. The JetBridge model (ArtSource/Vehicles/JetBridge) is built to
    /// the same numbers (its .vehicle profile's "bridge", checked by a test).</summary>
    public static class JetBridgeRules
    {
        /// <summary>The rotunda's floor above the ground, and the fixed link's where it leaves the terminal (every link
        /// does at this floor); an upper-deck bridge's raised rotunda, high enough for its tunnel to pass over a main-deck
        /// link with Headroom to spare. Its link ramps up to it at most MaxSlope.</summary>
        public const float RotundaFloor = 4f, UpperFloor = 7.8f;
        /// <summary>A passenger door more than this above the type's lowest one is on its upper deck.</summary>
        public const float UpperDeckAbove = 2f;
        public const float RotundaRadius = 2.3f;
        /// <summary>From the rotunda's centre to where the tunnel starts.</summary>
        public const float TunnelStart = 2.3f;
        /// <summary>Each of the three tunnel sections' length (a long tunnel's, and a short one's); the outer two slide
        /// over the one before.</summary>
        public const float SectionLength = 13f, ShortSectionLength = 8.5f;
        /// <summary>How much of a section stays inside the next one at full stretch.</summary>
        public const float MinOverlap = 1.5f;
        /// <summary>A long tunnel's length (rotunda to cab) retracted and fully out.</summary>
        public const float MinTunnel = SectionLength + 1f, MaxTunnel = 3f * SectionLength - 2f * MinOverlap;
        public static float Section(TunnelSize size) => size == TunnelSize.Short ? ShortSectionLength : SectionLength;
        /// <summary>A tunnel's length retracted and fully out.</summary>
        public static float MinTunnelOf(TunnelSize size) => Section(size) + 1f;
        public static float MaxTunnelOf(TunnelSize size) => 3f * Section(size) - 2f * MinOverlap;
        /// <summary>From the cab's pivot (the tunnel's end) to its bumper, and how far the bumper stops off the door.</summary>
        public const float CabReach = 2.6f, DoorGap = 0.1f;
        public const float CabWidth = 3.4f;
        /// <summary>The cab's underside and roof rim, from its floor; its rear is a half circle round its pivot, CabWidth
        /// across.</summary>
        public const float CabBottom = -0.75f, CabTop = 3.18f;
        /// <summary>The outer tunnel section's underside and roof from its floor line (the inner ones fit inside).</summary>
        public const float TunnelBottom = -0.55f, TunnelTop = 2.85f;
        /// <summary>The fixed link's underside and roof from its floor line.</summary>
        public const float LinkBottom = -0.35f, LinkTop = 2.75f;
        /// <summary>The top of the rotunda's roof vent, from its floor.</summary>
        public const float RotundaTop = 3.26f;
        /// <summary>The widest section (the outer one), for the tunnel's footprint.</summary>
        public const float TunnelWidth = 3.1f;
        /// <summary>The drive's wheels stand this far back from the tunnel's end.</summary>
        public const float DriveFromEnd = 4f;
        /// <summary>The top of the drive's frame (its legs start there), and the yoke's underside below the outer
        /// tunnel's floor line (the legs reach up to it).</summary>
        public const float DriveTop = 1f, YokeBottom = -0.95f;
        /// <summary>The fixed link's width, and its longest.</summary>
        public const float LinkWidth = 2.6f, MaxLink = 60f;
        /// <summary>Support columns under the fixed link at most this far apart.</summary>
        public const float LinkSpan = 18f;
        /// <summary>Half a link column's foot (its concrete footing), which stays off roads and taxiways.</summary>
        public const float ColumnFoot = 0.75f;
        /// <summary>The steepest the tunnel or a ramped link may slope (rise over run): 1 in 10.</summary>
        public const float MaxSlope = 0.1f;
        /// <summary>How far above its rotunda's floor an upper-deck bridge's cab is raised while it is parked, over the
        /// main-deck bridges docking beside it (a long tunnel retracted, tilted up MaxSlope; a short one tilts more, as
        /// a parked bridge carries no passengers).</summary>
        public const float ParkRise = 1.39f;
        /// <summary>A bridge head's width, and its underside and parapet from its floor (RotundaFloor); with an upper
        /// storey (floor UpperFloor) its parapet is HeadTop over that, its underside HeadUpperBottom under it, and it is
        /// HeadUpperWider wider. As built in the model.</summary>
        public const float HeadWidth = 7f, HeadBottom = -0.6f, HeadTop = 3.35f, HeadUpperBottom = -0.75f, HeadUpperWider = 0.06f;
        /// <summary>A head's main link: a link this wide.</summary>
        public const float MainLinkWidth = 3.6f;
        /// <summary>From a head's wall to the drum of a bridge on it: the stub link between them.</summary>
        public const float HeadStub = 1.2f;
        /// <summary>A head stands on pairs of link columns, this far either side of its centreline, at most LinkSpan apart
        /// along it, its ends overhanging the end pairs by at most HeadOverhang.</summary>
        public const float HeadColumnX = 2.2f, HeadOverhang = 5f;
        /// <summary>A head's shortest and longest.</summary>
        public const float MinHead = 10f, MaxHead = 60f;
        /// <summary>From a head's centreline to the rotundas along its side.</summary>
        public const float HeadToRotunda = HeadWidth * 0.5f + HeadStub + RotundaRadius;
        /// <summary>How far apart two bridges keep on the ground (any parts: tunnels, cabs, drives, links, columns,
        /// rotundas), unless one passes over the other with Headroom between them. Cabs docked at a double-deck's L2 and
        /// UL1, 4.4 m apart, stand 0.96 m apart.</summary>
        public const float Clearance = 0.75f;
        /// <summary>The least gap between a bridge passing over another and the one under it.</summary>
        public const float Headroom = 0.3f;
        /// <summary>How far the cab can turn either way from straight on along the tunnel (degrees).</summary>
        public const float MaxCabTurn = 95f;
        /// <summary>A link end within this of a terminal's wall (or inside the terminal) is connected.</summary>
        public const float ConnectReach = 1.5f;
        /// <summary>How far a retracted bridge keeps from every aircraft the stand takes.</summary>
        public const float ParkClearance = 1.5f;
        /// <summary>Game seconds to dock or to retract.</summary>
        public const float DockTime = 50f;
        /// <summary>Rotundas stand at least this far apart, centre to centre (room to walk round the columns).</summary>
        public const float RotundaSpacing = 2f * RotundaRadius + 3f;
        /// <summary>The shortest link that ramps from the terminal's floor up to a rotunda at this floor.</summary>
        public static float MinLink(float floor) => Mathf.Max(floor - RotundaFloor, 0f) / MaxSlope;
        /// <summary>The most bridges a stand can have.</summary>
        public const int MaxPerStand = 3;
    }

    /// <summary>Where a bridge's moving parts are: the rotunda's turn, the tunnel's length and tilt, the cab's turn.</summary>
    public struct BridgePose
    {
        /// <summary>The tunnel's direction on the ground (Airport space, unit).</summary>
        public Vector2 heading;
        /// <summary>Tunnel length along its slope, from TunnelStart to the cab's pivot.</summary>
        public float length;
        /// <summary>The cab floor's height.</summary>
        public float cabFloor;
        /// <summary>The rotunda's floor (JetBridge.Floor), where the tunnel starts.</summary>
        public float floor;
        /// <summary>The cab's turn from straight on along the tunnel, degrees (positive: clockwise from above).</summary>
        public float cabTurn;

        public float Rise => cabFloor - floor;
        /// <summary>The tunnel's length on the ground.</summary>
        public float Run => Mathf.Sqrt(Mathf.Max(length * length - Rise * Rise, 0f));
        public float Pitch => Mathf.Atan2(Rise, Run) * Mathf.Rad2Deg;
        public Vector2 CabHeading => Rotate(heading, cabTurn);

        public static Vector2 Rotate(Vector2 d, float clockwiseDegrees)
        {
            float r = -clockwiseDegrees * Mathf.Deg2Rad;
            return new Vector2(d.x * Mathf.Cos(r) - d.y * Mathf.Sin(r), d.x * Mathf.Sin(r) + d.y * Mathf.Cos(r));
        }

        /// <summary>
        /// Part way (t, 0..1) along a bridge's move from one pose to another, as an operator drives it: going out (to a
        /// longer tunnel) it swings round to its new heading first, as retracted as it is, then telescopes out, tilts and
        /// turns the cab; coming in it does that first, then swings. So it never swings round with its tunnel out, across
        /// the aircraft. Each stage takes its share of the time by how far it moves (the swing measured at the cab), and
        /// eases in and out.
        /// </summary>
        public static BridgePose Move(BridgePose from, BridgePose to, float t)
        {
            bool outward = to.length >= from.length;
            // between the stages: swung round, not yet out (going out); in, not yet swung round (coming in)
            var mid = outward ? from : to;
            mid.heading = outward ? to.heading : from.heading;
            mid.floor = to.floor;
            float arm = JetBridgeRules.TunnelStart + Mathf.Min(from.length, to.length) + JetBridgeRules.CabReach;
            float swing = Vector2.Angle(from.heading, to.heading) * Mathf.Deg2Rad * arm;
            float reach = Mathf.Abs(to.length - from.length) + Mathf.Abs(to.cabFloor - from.cabFloor) +
                          Mathf.Abs(to.cabTurn - from.cabTurn) * Mathf.Deg2Rad * JetBridgeRules.CabReach;
            float first = swing + reach > 1e-3f ? (outward ? swing : reach) / (swing + reach) : 0.5f;
            t = Mathf.Clamp01(t);
            if (t < first) return Lerp(from, mid, Ease(t / first));
            return Lerp(mid, to, first >= 1f ? 1f : Ease((t - first) / (1f - first)));
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>Part way from a to b (headings turn the short way).</summary>
        public static BridgePose Lerp(BridgePose a, BridgePose b, float t)
        {
            float ang = Mathf.LerpAngle(Geo.Angle(a.heading) * Mathf.Rad2Deg, Geo.Angle(b.heading) * Mathf.Rad2Deg, t) * Mathf.Deg2Rad;
            return new BridgePose
            {
                heading = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)),
                length = Mathf.Lerp(a.length, b.length, t),
                cabFloor = Mathf.Lerp(a.cabFloor, b.cabFloor, t),
                floor = b.floor,
                cabTurn = Mathf.Lerp(a.cabTurn, b.cabTurn, t),
            };
        }
    }

    /// <summary>
    /// A bridge laid out on its stand (Airport space): rotunda, link, whether the link reaches a terminal, and the
    /// pose it docks in for an aircraft type (Dock) or parks in out of the way (Parked).
    /// </summary>
    public sealed class BridgeLayout
    {
        public JetBridge bridge;
        public StandLayout stand;
        public Vector2 rotunda, root;
        /// <summary>The terminal its link reaches (through the head, if it stands on one), or null (not connected).</summary>
        public Terminal terminal;
        /// <summary>The stand's bridge head its link starts from, or null (it starts from a terminal).</summary>
        public HeadLayout head;

        /// <summary>Where the link's columns stand (Columns), once worked out.</summary>
        public List<Vector2> columns;

        public bool Connected => terminal != null;
        /// <summary>The fixed link's length on the ground, terminal wall to the rotunda's drum (0: the rotunda stands
        /// against it).</summary>
        public float LinkLength => Mathf.Max(Vector2.Distance(rotunda, root) - JetBridgeRules.RotundaRadius, 0f);
        /// <summary>The link's floor where it starts: the terminal's (JetBridgeRules.RotundaFloor), or level with the
        /// rotunda's on a head (whose upper storey meets a raised rotunda).</summary>
        public float LinkStartFloor => head != null ? bridge.Floor : JetBridgeRules.RotundaFloor;
        /// <summary>How far the link climbs from where it starts to the rotunda's floor.</summary>
        public float LinkRise => bridge.Floor - LinkStartFloor;
        /// <summary>The link's slope (rise over run); infinite for a raised rotunda against the wall.</summary>
        public float LinkSlope => LinkRise <= 1e-4f ? 0f : LinkLength > 1e-3f ? LinkRise / LinkLength : float.PositiveInfinity;

        /// <summary>The link's floor over a ground point along it: the terminal's floor at its root, ramping to the
        /// rotunda's at the drum.</summary>
        public float LinkFloorAt(Vector2 p)
        {
            float run = LinkLength;
            if (run <= 1e-3f) return bridge.Floor;
            float t = Vector2.Dot(p - root, (rotunda - root).normalized) / run;
            return LinkStartFloor + LinkRise * Mathf.Clamp01(t);
        }

        /// <summary>
        /// Where the link's columns stand: as many as keep every span (terminal wall, columns, drum) within
        /// JetBridgeRules.LinkSpan, evenly spaced, each foot off the roads' and taxiways' `pavement`; where one would
        /// land on it, moved along the link off it. Null if no spacing keeps them all off (a link along a road).
        /// Worked out once per layout against the pavement (kept in the columns field); with no pavement given, evenly
        /// spaced and not kept.
        /// </summary>
        public List<Vector2> Columns(IList<Vector2[]> pavement)
        {
            if (columns != null) return columns;
            float run = LinkLength;
            var list = SpanColumns(root, run > 1e-3f ? (rotunda - root).normalized : Vector2.up, run, pavement);
            if (pavement != null) columns = list;
            return list;
        }

        /// <summary>The columns under a link from `from` along `dir` for `run` metres (Columns): null if they can't all
        /// keep off the pavement.</summary>
        public static List<Vector2> SpanColumns(Vector2 from, Vector2 dir, float run, IList<Vector2[]> pavement)
        {
            int n = run > 0.05f ? Mathf.FloorToInt(run / JetBridgeRules.LinkSpan) : 0;
            var list = new List<Vector2>();
            if (n == 0) return list;
            // the stretches of the link a column's foot can't stand on
            var blocked = new List<(float a, float b)>();
            if (pavement != null)
                foreach (var q in pavement)
                    if (Blocked(q, from, dir, run, out var a, out var b)) blocked.Add((a, b));
            float near = JetBridgeRules.ColumnFoot + 0.25f, far = run - JetBridgeRules.ColumnFoot - 0.25f;
            bool Free(float s)
            {
                if (s < near || s > far) return false;
                foreach (var (a, b) in blocked)
                    if (s > a && s < b) return false;
                return true;
            }
            const float step = 0.25f;
            // evenly spaced, as most links are; else each moved to the nearest free spot, if the spans still reach
            var at = new List<float>();
            for (int i = 1; i <= n; i++) at.Add(run * i / (n + 1));
            if (!at.TrueForAll(Free))
            {
                var moved = new List<float>();
                foreach (float s0 in at)
                    for (float d = 0f; d <= JetBridgeRules.LinkSpan; d += step)
                    {
                        if (Free(s0 - d)) moved.Add(s0 - d);
                        else if (Free(s0 + d)) moved.Add(s0 + d);
                        else continue;
                        break;
                    }
                at = moved.Count == n && SpansReach(moved, run) ? moved : Greedy(run, Free, step);
                if (at == null) return null;
            }
            foreach (float s in at) list.Add(from + dir * s);
            return list;
        }

        static bool SpansReach(List<float> at, float run)
        {
            float last = 0f;
            foreach (float s in at)
            {
                if (s - last > JetBridgeRules.LinkSpan + 1e-3f || s <= last) return false;
                last = s;
            }
            return run - last <= JetBridgeRules.LinkSpan + 1e-3f;
        }

        /// <summary>Columns each as far along as a span reaches from the last support; null if a span can't.</summary>
        static List<float> Greedy(float run, Func<float, bool> free, float step)
        {
            var at = new List<float>();
            float last = 0f;
            while (run - last > JetBridgeRules.LinkSpan)
            {
                float next = -1f;
                for (float s = last + JetBridgeRules.LinkSpan; s > last + step; s -= step)
                    if (free(s))
                    {
                        next = s;
                        break;
                    }
                if (next < 0f) return null;
                at.Add(next);
                last = next;
            }
            return at;
        }

        /// <summary>The stretch (a..b along the link) where a column's foot would stand on a convex patch of pavement:
        /// the link's line clipped to the patch grown by the foot's reach.</summary>
        internal static bool Blocked(Vector2[] q, Vector2 root, Vector2 dir, float run, out float a, out float b)
        {
            a = float.NegativeInfinity;
            b = float.PositiveInfinity;
            float reach = JetBridgeRules.ColumnFoot * 1.42f;
            float area = Geo.SignedArea(q);
            for (int i = 0; i < q.Length; i++)
            {
                var e = q[(i + 1) % q.Length] - q[i];
                if (e.sqrMagnitude < 1e-8f) continue;
                // outward normal (corners counter-clockwise, or clockwise)
                var n = Geo.Right(e.normalized) * (area >= 0f ? 1f : -1f);
                // n.(root + dir s - q[i]) <= reach
                float c = Vector2.Dot(n, root - q[i]) - reach, k = Vector2.Dot(n, dir);
                if (Mathf.Abs(k) < 1e-6f)
                {
                    if (c > 0f) return false;
                    continue;
                }
                float s = -c / k;
                if (k > 0f) b = Mathf.Min(b, s);
                else a = Mathf.Max(a, s);
            }
            return a < b && b > 0f && a < run;
        }

        /// <summary>A stand's bridge laid out (with the stand's head, if it has one, HeadLayout.Of).</summary>
        public static BridgeLayout Of(JetBridge bridge, StandLayout stand, IList<Terminal> terminals) =>
            Of(bridge, stand, terminals, HeadLayout.Of(stand, terminals));

        /// <summary>A bridge laid out on its stand, whose head (or null) is `head`: its link starts from the head if it
        /// touches the head's wall, else from the terminal it touches.</summary>
        public static BridgeLayout Of(JetBridge bridge, StandLayout stand, IList<Terminal> terminals, HeadLayout head)
        {
            var l = new BridgeLayout
            {
                bridge = bridge,
                stand = stand,
                rotunda = stand.At(bridge.rotunda.x, bridge.rotunda.y),
                root = stand.At(bridge.root.x, bridge.root.y),
            };
            if (head != null && head.Touches(l.root))
            {
                l.head = head;
                l.terminal = head.terminal;
                return l;
            }
            if (terminals != null)
                foreach (var t in terminals)
                    if (t != null && t.corners.Count >= 3 &&
                        (t.Contains(l.root) || t.ClosestOnWall(l.root, out _, out _) <= JetBridgeRules.ConnectReach))
                    {
                        l.terminal = t;
                        break;
                    }
            return l;
        }

        /// <summary>Does a link from `root` (on a terminal's wall) to `end` run through a terminal on its way (sampled
        /// every metre from just off the wall to the rotunda's drum)?</summary>
        public static bool ThroughTerminal(Vector2 root, Vector2 end, IList<Terminal> terminals)
        {
            if (terminals == null) return false;
            var d = end - root;
            float len = d.magnitude - JetBridgeRules.RotundaRadius;
            if (len <= JetBridgeRules.ConnectReach + 0.5f) return false;
            d /= d.magnitude;
            for (float s = JetBridgeRules.ConnectReach + 0.5f; s <= len; s += 1f)
                foreach (var t in terminals)
                    if (t != null && t.Contains(root + d * s)) return true;
            return false;
        }

        /// <summary>A stand-frame point (x, s) for a ground point.</summary>
        public static Vector2 ToStand(StandLayout stand, Vector2 p)
        {
            var d = p - stand.entry;
            return new Vector2(Vector2.Dot(d, stand.Right), Vector2.Dot(d, stand.axis));
        }

        /// <summary>The door a bridge serves on an aircraft type: its n-th passenger door from the front on that side and
        /// deck (upper deck: more than JetBridgeRules.UpperDeckAbove over the type's lowest passenger door).</summary>
        public static bool TryDoor(AircraftType type, JetBridge bridge, out AircraftType.ServicePoint door) =>
            TryDoor(type, bridge.side, bridge.upper, bridge.door, out door);

        public static bool TryDoor(AircraftType type, DoorSide side, bool upper, int n, out AircraftType.ServicePoint door)
        {
            door = default;
            if (type == null) return false;
            float lowest = float.MaxValue;
            foreach (var p in type.PointsOfType(ServicePointType.PassengerDoor)) lowest = Mathf.Min(lowest, p.position.y);
            var list = new List<AircraftType.ServicePoint>();
            foreach (var p in type.PointsOfType(ServicePointType.PassengerDoor))
                if ((side == DoorSide.Left ? p.normal.x < 0f : p.normal.x > 0f) &&
                    (p.position.y > lowest + JetBridgeRules.UpperDeckAbove) == upper)
                    list.Add(p);
            list.Sort((a, b) => b.position.z.CompareTo(a.position.z));
            if (n < 1 || n > list.Count) return false;
            door = list[n - 1];
            return true;
        }

        /// <summary>Does the type have an upper deck with doors (on either side)?</summary>
        public static bool HasUpperDeck(AircraftType type) =>
            TryDoor(type, DoorSide.Left, true, 1, out _) || TryDoor(type, DoorSide.Right, true, 1, out _);

        /// <summary>
        /// The pose that brings the cab's bumper to the door of a type parked on the stand: the cab square on to the
        /// fuselage, the tunnel straight from the rotunda to the cab's pivot. Problem says why it can't (the pose is
        /// still filled in as far as it goes: it shows where it would have to reach).
        /// </summary>
        public BridgePose Dock(AircraftType type, out BridgeProblem problem)
        {
            var pose = new BridgePose { heading = stand.axis, length = bridge.MinTunnel, cabFloor = bridge.Floor, floor = bridge.Floor };
            problem = BridgeProblem.None;
            if (type.Light)
            {
                problem = BridgeProblem.SmallAircraft;
                return pose;
            }
            if (!TryDoor(type, bridge, out var door))
            {
                problem = BridgeProblem.NoSuchDoor;
                return pose;
            }
            var nose = stand.nose;
            var right = Geo.Right(nose);
            var park = stand.ParkPosition(type);
            var outward = right * door.normal.x + nose * door.normal.z;
            var sill = Sill(stand, type, door);
            var cabFacing = -outward.normalized;
            var bumper = sill + outward.normalized * JetBridgeRules.DoorGap;
            var pivot = bumper - cabFacing * JetBridgeRules.CabReach;
            var toPivot = pivot - rotunda;
            float dist = toPivot.magnitude;
            pose.heading = dist > 1e-3f ? toPivot / dist : stand.axis;
            float run = dist - JetBridgeRules.TunnelStart;
            pose.cabFloor = door.position.y;
            pose.length = Mathf.Sqrt(Mathf.Max(run, 0f) * Mathf.Max(run, 0f) + pose.Rise * pose.Rise);
            pose.cabTurn = Vector2.SignedAngle(cabFacing, pose.heading);
            if (pose.length < bridge.MinTunnel) problem = BridgeProblem.TooClose;
            else if (pose.length > bridge.MaxTunnel) problem = BridgeProblem.TooFar;
            else if (Mathf.Abs(pose.Rise) > JetBridgeRules.MaxSlope * Mathf.Max(run, 0.01f)) problem = BridgeProblem.TooSteep;
            else if (Mathf.Abs(pose.cabTurn) > JetBridgeRules.MaxCabTurn) problem = BridgeProblem.CabTurn;
            if (problem != BridgeProblem.None) return pose;
            // the tunnel's footprint (its far end tucked under the cab) clear of the parked aircraft's wings, engines and
            // tail, and not across its fuselage. An upper-deck tunnel runs above the wing root and the engines. The
            // wings are their outline where the type has one (a swept wing leaves room for an L2 door just ahead of
            // the root), else the outline's span-wide box
            var tunnel = Footprint(pose, false);
            var outline = Vehicles.AirportMaintenance.Outline(park, nose, type, WingClearance);
            bool wing = false;
            if (!bridge.upper)
            {
                var wings = Wings(park, nose, type);
                if (wings == null) wing = Overlaps(tunnel, outline[1]);
                else
                    foreach (var w in wings)
                        foreach (var q in tunnel)
                            wing |= Convex.Overlap(q, w, WingClearance);
            }
            if (wing || Overlaps(tunnel, outline[2])) problem = BridgeProblem.HitsWing;
            else if (Overlaps(tunnel, outline[0])) problem = BridgeProblem.HitsFuselage;
            return pose;
        }

        /// <summary>Where a door's sill is on the ground, the type parked on the stand.</summary>
        public static Vector2 Sill(StandLayout stand, AircraftType type, AircraftType.ServicePoint door) =>
            stand.ParkPosition(type) + Geo.Right(stand.nose) * door.position.x + stand.nose * door.position.z;

        /// <summary>How far a docked tunnel keeps off the wings and tail, metres.</summary>
        const float WingClearance = 0.3f;

        /// <summary>
        /// A type's two wings (with their engines) on the ground where it parks: AircraftType.wingOutline and its
        /// mirror, counter-clockwise from above. Null if the type has no outline.
        /// </summary>
        public static List<Vector2[]> Wings(Vector2 park, Vector2 nose, AircraftType type)
        {
            var o = type.wingOutline;
            if (o == null || o.Length < 3) return null;
            var right = Geo.Right(nose);
            var left = new Vector2[o.Length];
            var mirror = new Vector2[o.Length];
            for (int i = 0; i < o.Length; i++)
            {
                left[i] = park + right * o[i].x + nose * o[i].y;
                // mirrored, in reverse to stay counter-clockwise
                var m = o[o.Length - 1 - i];
                mirror[i] = park - right * m.x + nose * m.y;
            }
            return new List<Vector2[]> { left, mirror };
        }

        /// <summary>
        /// Where it parks, out of the way, for a stand taking these types: retracted, level at the rotunda's floor (an
        /// upper-deck bridge's cab raised JetBridgeRules.ParkRise, over the main-deck bridges docking beside it),
        /// pointing at the door it serves on the largest type that has one (so docking is a straight run out), swung away
        /// from the aircraft as far as it has to be to clear every type by ParkClearance, and clear of `avoid` (the
        /// stand's head and its bridges parked before it, ParkAll). If it can, it also keeps out of `clearOf` (where the
        /// stand's other bridges dock the types it can't, Clear), swinging round as far as it must, back along its head
        /// if need be; else it swings at most a right angle either way. False if no direction clears.
        /// </summary>
        public bool Parked(IList<AircraftType> types, out BridgePose pose) => Parked(types, null, null, out pose);

        public bool Parked(IList<AircraftType> types, IList<BridgeShape> avoid, out BridgePose pose) => Parked(types, avoid, null, out pose);

        public bool Parked(IList<AircraftType> types, IList<BridgeShape> avoid, IList<BridgeShape> clearOf, out BridgePose pose)
        {
            var aim = stand.axis;
            AircraftType largest = null;
            foreach (var t in types)
                if (t != null && !t.Light && TryDoor(t, bridge, out _) && (largest == null || t.Wingspan > largest.Wingspan)) largest = t;
            if (largest != null) aim = Dock(largest, out _).heading;
            pose = bridge.ParkedPose(aim);
            if (clearOf != null && clearOf.Count > 0 && Swing(ref pose, aim, 72, types, avoid, clearOf)) return true;
            if (Swing(ref pose, aim, 36, types, avoid, null)) return true;
            pose.heading = aim;
            return false;
        }

        /// <summary>The first heading 0, +5, -5, +10, -10 ... degrees off `aim` (at most `steps` tries) that clears.</summary>
        bool Swing(ref BridgePose pose, Vector2 aim, int steps, IList<AircraftType> types, IList<BridgeShape> avoid, IList<BridgeShape> clearOf)
        {
            for (int k = 0; k <= steps; k++)
            {
                float turn = (k + 1) / 2 * 5f * (k % 2 == 0 ? -1f : 1f);
                pose.heading = BridgePose.Rotate(aim, turn);
                if (!ClearOf(pose, types)) continue;
                var shape = Shape(pose);
                if ((avoid == null || !Clashes(shape, avoid)) && (clearOf == null || !Clashes(shape, clearOf))) return true;
            }
            return false;
        }

        /// <summary>For each of a stand's bridges, where the others dock the types it can't, and their way out there
        /// from swung round, retracted (BridgePose.Move: telescoping out, the cab turning): it parks clear of them (Parked:
        /// a bridge left parked mustn't stand in the way of the others).</summary>
        public static List<BridgeShape>[] Clear(IList<BridgeLayout> list, IList<AircraftType> types)
        {
            var docks = new List<(AircraftType type, BridgeShape shape)>[list.Count];
            var can = new HashSet<AircraftType>[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                docks[i] = new List<(AircraftType, BridgeShape)>();
                can[i] = new HashSet<AircraftType>();
                foreach (var t in types)
                {
                    if (t == null || t.Light) continue;
                    var pose = list[i].Dock(t, out var p);
                    if (p != BridgeProblem.None) continue;
                    var swung = list[i].bridge.ParkedPose(pose.heading);
                    for (int k = 0; k <= 4; k++) docks[i].Add((t, list[i].Shape(k == 4 ? pose : BridgePose.Move(swung, pose, k / 4f))));
                    can[i].Add(t);
                }
            }
            var clear = new List<BridgeShape>[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                clear[i] = new List<BridgeShape>();
                for (int j = 0; j < list.Count; j++)
                    if (j != i)
                        foreach (var (t, shape) in docks[j])
                            if (!can[i].Contains(t)) clear[i].Add(shape);
            }
            return clear;
        }

        /// <summary>
        /// Where a stand's bridges park (in its list's order): each clear of the aircraft and of those parked before it,
        /// so two never swing into each other, and if it can out of the others' way to the types it can't dock (Clear).
        /// ok[i] is false for one that finds no such place (it is left pointing at its door).
        /// </summary>
        public static BridgePose[] ParkAll(IList<BridgeLayout> list, IList<AircraftType> types, out bool[] ok) =>
            ParkAll(list, types, null, out ok);

        /// <summary>ParkAll, each also clear of `fixedShapes` (the stand's head, HeadLayout.Shape).</summary>
        public static BridgePose[] ParkAll(IList<BridgeLayout> list, IList<AircraftType> types, IList<BridgeShape> fixedShapes, out bool[] ok)
        {
            var poses = new BridgePose[list.Count];
            ok = new bool[list.Count];
            var before = fixedShapes != null ? new List<BridgeShape>(fixedShapes) : new List<BridgeShape>();
            var clear = Clear(list, types);
            for (int i = 0; i < list.Count; i++)
            {
                ok[i] = list[i].Parked(types, before, clear[i], out poses[i]);
                before.Add(list[i].Shape(poses[i]));
            }
            return poses;
        }

        /// <summary>Is it clear, in this pose, of every type the stand takes by ParkClearance? An upper-deck bridge's
        /// tunnel and cab pass over the wings (as docked, Dock); its drive, on the ground, doesn't.</summary>
        bool ClearOf(BridgePose pose, IList<AircraftType> types)
        {
            bool high = bridge.upper;
            var quads = Footprint(pose, true);
            var drive = high ? Shape(pose).Drive.Corners(0f) : null;
            foreach (var t in types)
            {
                if (t == null) continue;
                foreach (var q in quads)
                    if (InTheWay(stand, q, t, JetBridgeRules.ParkClearance, !high)) return false;
                if (drive != null && InTheWay(stand, drive, t, JetBridgeRules.ParkClearance)) return false;
            }
            return true;
        }

        /// <summary>Could it park, retracted, pointing this way (or within 30 degrees of it), clear of every type the
        /// stand takes, and swing round from there to dock `type` (if given) without hitting it (SwingClear)?</summary>
        public bool CanParkToward(Vector2 heading, IList<AircraftType> types, AircraftType type = null)
        {
            var pose = bridge.ParkedPose(heading);
            var dock = default(BridgePose);
            if (type != null)
            {
                dock = Dock(type, out var problem);
                if (problem != BridgeProblem.None) return false;
            }
            for (int k = 0; k <= 12; k++)
            {
                pose.heading = BridgePose.Rotate(heading, (k + 1) / 2 * 5f * (k % 2 == 0 ? -1f : 1f));
                if (ClearOf(pose, types)) return type == null || SwingClear(pose, dock, type);
            }
            return false;
        }

        /// <summary>How far a bridge swinging round, retracted, keeps off the aircraft (SwingClear), metres.</summary>
        const float SwingClearance = 0.5f;

        /// <summary>
        /// Swinging round from `from` (where it parks) to the heading it docks at (`to`), as retracted and tilted as it
        /// parks (BridgePose.Move), does it keep SwingClearance off a type parked on the stand: its fuselage and tail,
        /// and its wings unless it passes over them from a raised rotunda (its drive, on the ground, never does)?
        /// </summary>
        public bool SwingClear(BridgePose from, BridgePose to, AircraftType type)
        {
            if (type == null) return true;
            float angle = Vector2.SignedAngle(from.heading, to.heading);
            int n = Mathf.CeilToInt(Mathf.Abs(angle) / 3f);
            var pose = from;
            for (int i = 1; i <= n; i++)
            {
                // (SignedAngle is counter-clockwise, Rotate clockwise)
                pose.heading = BridgePose.Rotate(from.heading, -angle * i / n);
                foreach (var q in Footprint(pose, true))
                    if (InTheWay(stand, q, type, SwingClearance, !bridge.upper)) return false;
                if (bridge.upper && InTheWay(stand, Shape(pose).Drive.Corners(0f), type, SwingClearance)) return false;
            }
            return true;
        }

        /// <summary>
        /// Does a convex quad come within `clearance` of a type parked on the stand: its fuselage and tail
        /// (AirportMaintenance.Outline's boxes) or, if asked, its wings with their engines (their outline, Wings; the
        /// span-wide box if the type has none)? An aircraft only moves forward along the stand to its spot, so the
        /// ground ahead of its swept wings' leading edges is never under them.
        /// </summary>
        bool InTheWay(Vector2[] q, AircraftType type, float clearance) => InTheWay(stand, q, type, clearance);

        internal static bool InTheWay(StandLayout stand, Vector2[] q, AircraftType type, float clearance, bool withWings = true)
        {
            var park = stand.ParkPosition(type);
            var boxes = Vehicles.AirportMaintenance.Outline(park, stand.nose, type, clearance);
            var wings = withWings ? Wings(park, stand.nose, type) : null;
            var one = new List<Vector2[]> { q };
            for (int i = 0; i < boxes.Count; i++)
                if (!(i == 1 && (wings != null || !withWings)) && Overlaps(one, boxes[i])) return true;
            if (wings != null)
                foreach (var w in wings)
                    if (Convex.Overlap(q, w, clearance)) return true;
            return false;
        }

        /// <summary>
        /// The ground under the tunnel (from the rotunda's edge to the cab's pivot) and, if asked, the cab (its round
        /// rear included): convex quads, counter-clockwise from above.
        /// </summary>
        public List<Vector2[]> Footprint(BridgePose pose, bool withCab)
        {
            var h = pose.heading;
            var r = Geo.Right(h) * (JetBridgeRules.TunnelWidth * 0.5f);
            var a = rotunda + h * JetBridgeRules.TunnelStart;
            var b = rotunda + h * (JetBridgeRules.TunnelStart + pose.Run);
            var list = new List<Vector2[]> { new[] { a + r, b + r, b - r, a - r } };
            if (withCab)
            {
                var c = pose.CabHeading;
                var cr = Geo.Right(c) * (JetBridgeRules.CabWidth * 0.5f);
                var f = b + c * JetBridgeRules.CabReach;
                var back = b - c * (JetBridgeRules.CabWidth * 0.5f);
                list.Add(new[] { back + cr, f + cr, f - cr, back - cr });
            }
            foreach (var q in list)
                if (Geo.SignedArea(q) < 0f) Array.Reverse(q);
            return list;
        }

        internal static bool Overlaps(List<Vector2[]> quads, Vehicles.AirportMaintenance.Box box)
        {
            var corners = new[]
            {
                box.c - box.ax * box.hx - box.az * box.hz, box.c + box.ax * box.hx - box.az * box.hz,
                box.c + box.ax * box.hx + box.az * box.hz, box.c - box.ax * box.hx + box.az * box.hz,
            };
            foreach (var q in quads)
                if (Convex.Overlap(q, corners)) return true;
            return false;
        }

        /// <summary>
        /// Do this bridge and another come too close in these poses? Every part of each (rotunda, link, its columns,
        /// tunnel, drive, cab: Solids) keeps JetBridgeRules.Clearance from every part of the other on the ground, unless
        /// one passes over the other with JetBridgeRules.Headroom between them (an upper-deck bridge's tunnel over a
        /// main-deck link). Parts that stand on the ground (rotundas, columns, the drive) have nothing passing under them.
        /// </summary>
        public bool Crosses(BridgePose pose, BridgeLayout other, BridgePose otherPose) => Shape(pose).Clashes(other.Shape(otherPose));

        /// <summary>The bridge's parts in a pose, as boxes over the ground (Solid): rotunda, link and its columns (as
        /// Columns last placed them, else evenly spaced), tunnel, drive (with its legs), cab.</summary>
        public BridgeShape Shape(BridgePose pose)
        {
            var list = new List<Solid>();
            float floor = bridge.Floor, r = JetBridgeRules.RotundaRadius + 0.15f;
            list.Add(new Solid(rotunda - Vector2.up * r, rotunda + Vector2.up * r, r, floor, floor, 0f, JetBridgeRules.RotundaTop, true));
            float run = LinkLength;
            if (run > 0.05f)
            {
                var dir = (rotunda - root).normalized;
                float hw = JetBridgeRules.LinkWidth * 0.5f;
                // to the drum (inside it, the rotunda); a stub from a head is part of the head (HeadLayout.Joint)
                list.Add(new Solid(root, root + dir * run, hw, LinkStartFloor, floor, JetBridgeRules.LinkBottom,
                    JetBridgeRules.LinkTop, false, head != null ? head.Joint : 0));
                foreach (var c in columns ?? Columns(null))
                {
                    float f = LinkFloorAt(c);
                    list.Add(new Solid(c - dir * JetBridgeRules.ColumnFoot, c + dir * JetBridgeRules.ColumnFoot, hw, f, f, 0f,
                        JetBridgeRules.LinkBottom, true));
                }
            }
            var h = pose.heading.sqrMagnitude > 1e-6f ? pose.heading.normalized : stand.axis;
            float tunnelRun = pose.Run, hwT = JetBridgeRules.TunnelWidth * 0.5f;
            var start = rotunda + h * JetBridgeRules.TunnelStart;
            var pivot = start + h * tunnelRun;
            list.Add(new Solid(start, pivot, hwT, pose.floor, pose.cabFloor, JetBridgeRules.TunnelBottom, JetBridgeRules.TunnelTop, false));
            // the drive under the yoke, DriveFromEnd back from the tunnel's end
            float back = pose.length > 1e-3f ? Mathf.Max(pose.length - JetBridgeRules.DriveFromEnd, 0f) * tunnelRun / pose.length : 0f;
            var yoke = start + h * back;
            float yokeFloor = Mathf.Lerp(pose.floor, pose.cabFloor, tunnelRun > 1e-3f ? back / tunnelRun : 0f);
            list.Add(new Solid(yoke - h * 0.5f, yoke + h * 0.5f, hwT, yokeFloor, yokeFloor, 0f, JetBridgeRules.TunnelBottom, true));
            var cab = pose.CabHeading;
            // (its roof rim stands 4 cm proud of the sides)
            list.Add(new Solid(pivot - cab * (JetBridgeRules.CabWidth * 0.5f), pivot + cab * JetBridgeRules.CabReach,
                JetBridgeRules.CabWidth * 0.5f + 0.04f, pose.cabFloor, pose.cabFloor, JetBridgeRules.CabBottom, JetBridgeRules.CabTop, false));
            return new BridgeShape(list);
        }

        /// <summary>A square round the rotunda's drum (for footprint tests).</summary>
        public Vector2[] RotundaSquare
        {
            get
            {
                float r = JetBridgeRules.RotundaRadius;
                return new[] { rotunda + new Vector2(-r, -r), rotunda + new Vector2(r, -r), rotunda + new Vector2(r, r), rotunda + new Vector2(-r, r) };
            }
        }

        /// <summary>The fixed link's footprint (terminal wall to rotunda), or null if there's none.</summary>
        public Vector2[] LinkFootprint
        {
            get
            {
                var d = rotunda - root;
                if (d.magnitude < JetBridgeRules.RotundaRadius + 0.05f) return null;
                var r = Geo.Right(d.normalized) * (JetBridgeRules.LinkWidth * 0.5f);
                var q = new[] { root + r, rotunda + r, rotunda - r, root - r };
                if (Geo.SignedArea(q) < 0f) Array.Reverse(q);
                return q;
            }
        }

        /// <summary>
        /// What's wrong with the bridge where it stands, whatever aircraft comes (BridgeProblem.None if nothing): a
        /// taxi-out stand, the head it stands on (HeadLayout.Placement), the rotunda on a road or taxiway
        /// (`site.pavement`) or where an aircraft the stand takes parks, an upper-deck link too short to ramp up, the
        /// link's columns unable to keep off the pavement, the link where an aircraft parks, nowhere to park clear of
        /// them (or of `avoid`, the stand's head and bridges parked before it), parked over a taxiway or on the next
        /// stand (`site.pads`).
        /// </summary>
        public BridgeProblem Placement(BridgeSite site, IList<BridgeShape> avoid = null, IList<BridgeShape> clearOf = null)
        {
            if (stand.TaxiOut || stand.stand.DeIcing) return BridgeProblem.TaxiOutStand;
            if (head != null)
            {
                var hp = head.Placement(site);
                if (hp != BridgeProblem.None) return hp;
            }
            var types = site.types;
            var pads = site.pads;
            foreach (var q in site.pavement)
                if (Convex.Overlap(RotundaSquare, q)) return BridgeProblem.RotundaOnPavement;
            if (Connected && LinkSlope > JetBridgeRules.MaxSlope + 1e-4f) return BridgeProblem.LinkTooSteep;
            if (head == null && Connected && LinkLength > JetBridgeRules.MaxLink + 1e-3f) return BridgeProblem.LinkTooLong;
            if (head == null && ThroughTerminal(root, rotunda, site.terminals)) return BridgeProblem.LinkThroughTerminal;
            if (Columns(site.pavement) == null) return BridgeProblem.ColumnOnPavement;
            var link = LinkFootprint;
            foreach (var t in types)
            {
                if (t == null) continue;
                if (InTheWay(RotundaSquare, t, JetBridgeRules.ParkClearance)) return BridgeProblem.RotundaInTheWay;
                if (link != null && InTheWay(link, t, JetBridgeRules.ParkClearance)) return BridgeProblem.LinkInTheWay;
            }
            if (!Parked(types, out var parked)) return BridgeProblem.NoRoomToPark;
            if ((avoid != null && avoid.Count > 0) || (clearOf != null && clearOf.Count > 0))
                if (!Parked(types, avoid, clearOf, out parked)) return BridgeProblem.HitsBridge;
            foreach (var q in Footprint(parked, true))
                foreach (var lane in site.taxiways)
                    if (Convex.Overlap(q, lane)) return BridgeProblem.OverTaxiway;
            // nor its wheels on a road
            var drive = Shape(parked).Drive.corners;
            foreach (var q in site.pavement)
                if (Convex.Overlap(drive, q)) return BridgeProblem.OverTaxiway;
            if (pads != null)
            {
                var mine = Footprint(parked, true);
                mine.Add(RotundaSquare);
                if (link != null) mine.Add(link);
                foreach (var pad in pads)
                    foreach (var q in mine)
                        if (Convex.Overlap(q, pad)) return BridgeProblem.OnNextStand;
            }
            return BridgeProblem.None;
        }

        /// <summary>
        /// Every problem the bridge has on its stand: first where it stands (Placement, with a null type; it parks clear
        /// of the stand's head, `site.head`, and of the stand's bridges before it in `others`, or of them all if it
        /// isn't one of them, ParkAll; its rotunda stands RotundaSpacing from theirs; parked, it mustn't come near the
        /// neighbouring stands' heads and bridges, `site.neighbours`), then per type it takes, why it can't dock
        /// (BridgeProblem.None if it can): docked, and on its way out and back (the stand's bridges all move at once),
        /// it keeps clear of the stand's head and other bridges (docked to that type too, or parked if they can't) and
        /// the neighbours', and off the other stands' pads.
        /// </summary>
        public static List<(AircraftType type, BridgeProblem problem)> Check(BridgeLayout l, IList<BridgeLayout> others,
            BridgeSite site)
        {
            var list = new List<(AircraftType, BridgeProblem)>();
            var types = site.types;
            var pads = site.pads;
            // the stand's bridges park in turn (ParkAll), a new one last
            var order = new List<BridgeLayout>(others);
            if (!order.Contains(l)) order.Add(l);
            int me = order.IndexOf(l);
            var parked = new Dictionary<BridgeLayout, (BridgePose pose, BridgeShape shape)>();
            var fixedShapes = site.HeadShapes;
            var before = new List<BridgeShape>(fixedShapes);
            foreach (var o in order) o.Columns(site.pavement);
            var clear = Clear(order, types);
            for (int i = 0; i < me; i++)
            {
                var o = order[i];
                o.Parked(types, before, clear[i], out var op);
                var shape = o.Shape(op);
                parked[o] = (op, shape);
                before.Add(shape);
            }
            var placement = l.Placement(site, before, clear[me]);
            var mine = default(BridgePose);
            if (placement == BridgeProblem.None)
            {
                l.Parked(types, before, clear[me], out mine);
                var mineShape = l.Shape(mine);
                before.Add(mineShape);
                for (int i = me + 1; i < order.Count; i++)
                {
                    var o = order[i];
                    o.Parked(types, before, clear[i], out var op);
                    var shape = o.Shape(op);
                    parked[o] = (op, shape);
                    before.Add(shape);
                }
                foreach (var o in order)
                    if (o != l && Vector2.Distance(l.rotunda, o.rotunda) < JetBridgeRules.RotundaSpacing)
                    {
                        placement = BridgeProblem.HitsBridge;
                        break;
                    }
                if (placement == BridgeProblem.None && Clashes(mineShape, site.neighbours)) placement = BridgeProblem.HitsBridge;
            }
            if (placement != BridgeProblem.None)
            {
                list.Add((null, placement));
                return list;
            }
            var sweep = new BridgeShape[SweepSteps];
            foreach (var t in types)
            {
                if (t == null) continue;
                var pose = l.Dock(t, out var p);
                if (p == BridgeProblem.None && pads != null)
                    foreach (var q in l.Footprint(pose, true))
                        foreach (var pad in pads)
                            if (p == BridgeProblem.None && Convex.Overlap(q, pad)) p = BridgeProblem.OnNextStand;
                if (p == BridgeProblem.None && !l.SwingClear(mine, pose, t)) p = BridgeProblem.SwingHitsAircraft;
                if (p == BridgeProblem.None)
                {
                    // docked (the last step), and on the way (BridgePose.Move): the stand's bridges all set off together
                    for (int i = 1; i <= SweepSteps; i++)
                        sweep[i - 1] = l.Shape(i == SweepSteps ? pose : BridgePose.Move(mine, pose, i / (float)SweepSteps));
                    foreach (var o in order)
                    {
                        if (o == l) continue;
                        var (oParked, oParkedShape) = parked[o];
                        var op = o.Dock(t, out var oProblem);
                        // a bridge that can't dock this type stays parked while the others dock
                        bool oDocks = oProblem == BridgeProblem.None;
                        bool clash = false;
                        for (int i = 1; i <= SweepSteps && !clash; i++)
                        {
                            var theirs = oDocks ? o.Shape(i == SweepSteps ? op : BridgePose.Move(oParked, op, i / (float)SweepSteps)) : oParkedShape;
                            clash = sweep[i - 1].Clashes(theirs);
                        }
                        if (clash)
                        {
                            p = BridgeProblem.HitsBridge;
                            break;
                        }
                    }
                    if (p == BridgeProblem.None)
                        foreach (var shape in sweep)
                            if (Clashes(shape, site.neighbours) || Clashes(shape, fixedShapes))
                            {
                                p = BridgeProblem.HitsBridge;
                                break;
                            }
                }
                list.Add((t, p));
            }
            return list;
        }

        static bool Clashes(BridgeShape shape, IList<BridgeShape> others)
        {
            if (others != null)
                foreach (var o in others)
                    if (shape.Clashes(o)) return true;
            return false;
        }

        /// <summary>Docking (and pulling back) is checked at the poses this many steps apart on the way.</summary>
        const int SweepSteps = 8;

        /// <summary>
        /// A place for a new bridge to a door (side, deck, n-th from the front) on a stand: searched over a grid round the
        /// stand, kept on its
        /// own ground (beside the aircraft or beyond its head), the rotunda clear of the aircraft, its link (if a
        /// terminal is within JetBridgeRules.MaxLink) straight out from the nearest terminal wall, with `door`'s tunnel.
        /// It must dock the largest type with that door without crossing the stand's other bridges; places that dock the
        /// other types too come first (a bridge that can't reach a smaller type's door just stays parked for it, as an
        /// L2 bridge does for a narrow-body on a wide-body stand). Then: the tunnel a little under half out for the
        /// largest type, a short link, the rotunda near the stand's head. Null if nowhere works.
        /// </summary>
        public static JetBridge Suggest(StandLayout stand, JetBridge door, IList<BridgeLayout> others, BridgeSite site)
        {
            var types = site.types;
            var terminals = site.terminals;
            AircraftType design = null;
            foreach (var t in types)
                if (t != null && !t.Light && TryDoor(t, door, out _) && (design == null || t.Wingspan > design.Wingspan)) design = t;
            if (design == null || stand.TaxiOut || stand.stand.DeIcing) return null;
            const float step = 1.5f;
            float target = door.tunnel == TunnelSize.Short ? 14f : 22f;
            var candidates = new List<(float score, JetBridge bridge)>();
            for (float s = stand.noseS - 40f; s <= stand.far + 30f; s += step)
                for (float x = stand.x0 - 10f; x <= stand.x1 + 10f; x += step)
                {
                    // on the stand's own ground: between its sides, or beyond its head
                    bool beside = x - JetBridgeRules.RotundaRadius >= stand.x0 && x + JetBridgeRules.RotundaRadius <= stand.x1;
                    if (!beside && s < stand.far + JetBridgeRules.RotundaRadius) continue;
                    var b = new JetBridge
                    {
                        door = door.door, side = door.side, upper = door.upper, tunnel = door.tunnel, rotunda = new Vector2(x, s),
                        root = new Vector2(x, s),
                    };
                    var at = stand.At(x, s);
                    float link = 0f, nearest = float.MaxValue;
                    bool inside = false;
                    foreach (var t in terminals)
                    {
                        if (t == null || t.corners.Count < 3) continue;
                        // the drum stands clear of the building (it may touch it)
                        float d = t.ClosestOnWall(at, out var wall, out _);
                        if (t.Contains(at) || d < JetBridgeRules.RotundaRadius * 0.9f) inside = true;
                        if (d - JetBridgeRules.RotundaRadius > JetBridgeRules.MaxLink || d >= nearest) continue;
                        nearest = d;
                        link = Mathf.Max(d - JetBridgeRules.RotundaRadius, 0f);
                        b.root = ToStand(stand, wall);
                    }
                    if (inside) continue;
                    // an upper-deck bridge's link ramps up from the terminal's floor: it needs room
                    if (door.upper && nearest < float.MaxValue && link < JetBridgeRules.MinLink(b.Floor)) continue;
                    var l = Of(b, stand, terminals, site.head);
                    var pose = l.Dock(design, out var problem);
                    if (problem != BridgeProblem.None) continue;
                    float score = Mathf.Abs(pose.length - target) + 0.25f * link + 0.03f * Mathf.Abs(pose.cabTurn) +
                                  0.08f * Mathf.Abs(s - stand.far) + (terminals.Count > 0 && link == 0f && !l.Connected ? 30f : 0f);
                    candidates.Add((score, b));
                }
            candidates.Sort((a, b) => a.score.CompareTo(b.score));
            JetBridge fallback = null;
            int tried = 0;
            var docking = Docking(others, site);
            foreach (var (_, b) in candidates)
            {
                if (++tried > 400) break;
                var (placed, designDocks, allDock) = Rate(Of(b, stand, terminals, site.head), design, others, site, docking);
                if (!placed || !designDocks) continue;
                if (allDock) return b;
                fallback ??= b;
            }
            return fallback;
        }

        /// <summary>The types each of a stand's bridges docks now (for Rate).</summary>
        internal static List<HashSet<AircraftType>> Docking(IList<BridgeLayout> list, BridgeSite site)
        {
            var docking = new List<HashSet<AircraftType>>();
            foreach (var o in list)
            {
                var set = new HashSet<AircraftType>();
                foreach (var (t, p) in Check(o, list, site))
                    if (t != null && p == BridgeProblem.None) set.Add(t);
                docking.Add(set);
            }
            return docking;
        }

        /// <summary>How a new bridge would do (Check): does it stand right, does it dock `design`, and does it dock every
        /// type with its door while the stand's other bridges (`others`) still dock every type they did (`docking`,
        /// Docking)?</summary>
        internal static (bool placed, bool designDocks, bool allDock) Rate(BridgeLayout l, AircraftType design, IList<BridgeLayout> others,
            BridgeSite site, List<HashSet<AircraftType>> docking)
        {
            bool designDocks = false, allDock = true;
            foreach (var (t, p) in Check(l, others, site))
            {
                if (t == null) return (false, false, false);
                if (t == design) designDocks = p == BridgeProblem.None;
                else if (p != BridgeProblem.None && p != BridgeProblem.SmallAircraft && p != BridgeProblem.NoSuchDoor) allDock = false;
            }
            if (!designDocks || !allDock || others.Count == 0) return (true, designDocks, allDock);
            var with = new List<BridgeLayout>(others) { l };
            for (int i = 0; i < others.Count && allDock; i++)
                foreach (var (t, p) in Check(others[i], with, site))
                    if (t != null && p != BridgeProblem.None && docking[i].Contains(t))
                    {
                        allDock = false;
                        break;
                    }
            return (true, designDocks, allDock);
        }

        public static string Describe(BridgeProblem p) => p switch
        {
            BridgeProblem.None => "OK",
            BridgeProblem.SmallAircraft => "boards by stairs",
            BridgeProblem.NoSuchDoor => "has no such door",
            BridgeProblem.TooClose => "door too close to the rotunda",
            BridgeProblem.TooFar => "door out of reach",
            BridgeProblem.TooSteep => "too steep to the door",
            BridgeProblem.CabTurn => "cab can't turn to the door",
            BridgeProblem.HitsWing => "tunnel hits the wing",
            BridgeProblem.HitsFuselage => "tunnel crosses the fuselage",
            BridgeProblem.HitsBridge => "crosses another bridge",
            BridgeProblem.NoRoomToPark => "no room to park clear of the aircraft",
            BridgeProblem.RotundaInTheWay => "rotunda stands where the aircraft parks",
            BridgeProblem.RotundaOnPavement => "rotunda column on a road or taxiway",
            BridgeProblem.OverTaxiway => "parked, it stands over a taxiway (or its wheels on a road)",
            BridgeProblem.LinkInTheWay => "link runs over where the aircraft parks",
            BridgeProblem.OnNextStand => "reaches onto the next stand",
            BridgeProblem.LinkTooSteep => $"link too short to ramp up to the raised rotunda (needs {JetBridgeRules.MinLink(JetBridgeRules.UpperFloor):0} m)",
            BridgeProblem.ColumnOnPavement => "link column would stand on a road or taxiway",
            BridgeProblem.HeadInTheWay => "bridge head stands where the aircraft parks",
            BridgeProblem.SwingHitsAircraft => "swings into the aircraft on its way out",
            BridgeProblem.LinkThroughTerminal => "link runs through the terminal",
            BridgeProblem.LinkTooLong => $"link longer than {JetBridgeRules.MaxLink:0} m",
            BridgeProblem.HeadInTerminal => "bridge head stands in the terminal",
            _ => "taxi-out stands take no bridges",
        };
    }

    /// <summary>What a stand's bridges are checked against: the terminals their links may reach, the aircraft types the
    /// stand takes, the other stands' pads (a bridge keeps to its own stand) and the roads' and taxiways' pavement (no
    /// rotunda column on them; tunnels and links pass over). AirportBridges.Site makes one.</summary>
    public sealed class BridgeSite
    {
        /// <summary>The stand's bridge head, or null: its bridges' links start from it, and every bridge keeps clear of it.</summary>
        public HeadLayout head;
        public IList<Terminal> terminals = new List<Terminal>();
        public IList<AircraftType> types = new List<AircraftType>();
        public List<Vector2[]> pads = new List<Vector2[]>();
        public List<Vector2[]> pavement = new List<Vector2[]>();
        /// <summary>The taxiways' pavement only (no part of a parked bridge over them).</summary>
        public List<Vector2[]> taxiways = new List<Vector2[]>();
        /// <summary>The other stands' heads and bridges (parked and docked to each type they take): a bridge keeps clear of them too.</summary>
        public List<BridgeShape> neighbours = new List<BridgeShape>();

        /// <summary>The stand's head as a shape to keep clear of (none, or one).</summary>
        public List<BridgeShape> HeadShapes => head != null ? new List<BridgeShape> { head.Shape } : new List<BridgeShape>();

        /// <summary>The same site with another head (to try one out).</summary>
        public BridgeSite With(HeadLayout h)
        {
            var copy = (BridgeSite)MemberwiseClone();
            copy.head = h;
            return copy;
        }
    }

    /// <summary>A bridge's parts in one pose (BridgeLayout.Shape), with the ground they and their clearance cover.</summary>
    public sealed class BridgeShape
    {
        public readonly List<Solid> parts;
        public readonly Rect bounds;

        public BridgeShape(List<Solid> parts)
        {
            this.parts = parts;
            if (parts.Count == 0) return;
            bounds = parts[0].bounds;
            for (int i = 1; i < parts.Count; i++)
            {
                var b = parts[i].bounds;
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, b.xMin), Mathf.Min(bounds.yMin, b.yMin),
                    Mathf.Max(bounds.xMax, b.xMax), Mathf.Max(bounds.yMax, b.yMax));
            }
        }

        /// <summary>The drive and its legs (BridgeLayout.Shape puts it last but one, before the cab).</summary>
        public Solid Drive => parts[parts.Count - 2];

        /// <summary>Does any part of one come too close to any of the other's (Solid.Clash)?</summary>
        public bool Clashes(BridgeShape other)
        {
            if (parts.Count == 0 || other.parts.Count == 0 || !bounds.Overlaps(other.bounds)) return false;
            foreach (var a in parts)
                foreach (var b in other.parts)
                    if (a.bounds.Overlaps(b.bounds) && Solid.Clash(a, b)) return true;
            return false;
        }
    }

    /// <summary>
    /// A part of a bridge as a box over the ground: a rectangle from a to b, halfWidth either side, whose floor runs
    /// straight from floorA at a to floorB at b; it spans floor + bottom to floor + top, or from the ground if it stands
    /// on it (a rotunda, a column, the drive and its legs). Parts built onto each other (a head and the stub links
    /// from it) share a nonzero `joint` and never clash.
    /// </summary>
    public readonly struct Solid
    {
        public readonly Vector2 a, b;
        public readonly float halfWidth, floorA, floorB, bottom, top;
        public readonly bool grounded;
        public readonly int joint;
        /// <summary>Its rectangle grown by half JetBridgeRules.Clearance all round, and that rectangle's bounds.</summary>
        public readonly Vector2[] corners;
        public readonly Rect bounds;

        public Solid(Vector2 a, Vector2 b, float halfWidth, float floorA, float floorB, float bottom, float top, bool grounded, int joint = 0)
        {
            this.joint = joint;
            this.a = a;
            this.b = b;
            this.halfWidth = halfWidth;
            this.floorA = floorA;
            this.floorB = floorB;
            this.bottom = bottom;
            this.top = top;
            this.grounded = grounded;
            corners = null;
            bounds = default;
            corners = Corners(JetBridgeRules.Clearance * 0.5f);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var c in corners)
            {
                x0 = Mathf.Min(x0, c.x);
                y0 = Mathf.Min(y0, c.y);
                x1 = Mathf.Max(x1, c.x);
                y1 = Mathf.Max(y1, c.y);
            }
            bounds = Rect.MinMaxRect(x0, y0, x1, y1);
        }

        Vector2 Dir => (b - a).sqrMagnitude > 1e-8f ? (b - a).normalized : Vector2.up;

        /// <summary>The floor over a ground point (straight on beyond the ends).</summary>
        public float FloorAt(Vector2 p)
        {
            float len = Vector2.Distance(a, b);
            return len < 1e-3f ? floorA : floorA + (floorB - floorA) * Vector2.Dot(p - a, Dir) / len;
        }

        public float Bottom(Vector2 p) => grounded ? 0f : FloorAt(p) + bottom;
        public float Top(Vector2 p) => FloorAt(p) + top;

        /// <summary>Its rectangle on the ground grown by `grow` all round, counter-clockwise from above.</summary>
        public Vector2[] Corners(float grow)
        {
            var d = Dir;
            var r = Geo.Right(d) * (halfWidth + grow);
            Vector2 p = a - d * grow, q = b + d * grow;
            return new[] { p + r, q + r, q - r, p - r };
        }

        /// <summary>
        /// Do two parts come within JetBridgeRules.Clearance of each other on the ground without one passing over the
        /// other with JetBridgeRules.Headroom to spare? Both rectangles grown by half the clearance; where they overlap
        /// the heights are straight (linear), so their gap is least at a corner of the overlap.
        /// </summary>
        public static bool Clash(Solid x, Solid y)
        {
            if (x.joint != 0 && x.joint == y.joint) return false;
            var overlap = Convex.Clip(x.corners, y.corners);
            if (overlap.Count < 3) return false;
            bool xOver = true, yOver = true;
            foreach (var p in overlap)
            {
                xOver &= x.Bottom(p) - y.Top(p) >= JetBridgeRules.Headroom;
                yOver &= y.Bottom(p) - x.Top(p) >= JetBridgeRules.Headroom;
            }
            return !xOver && !yOver;
        }
    }

    /// <summary>Convex polygon tests (corners counter-clockwise from above).</summary>
    static class Convex
    {
        /// <summary>The overlap of two convex polygons (the clip counter-clockwise), empty (fewer than 3 corners) if none.</summary>
        public static List<Vector2> Clip(Vector2[] subject, Vector2[] clip)
        {
            Debug.Assert(Geo.SignedArea(clip) >= 0f, "Convex.Clip: clip polygon must be counter-clockwise");
            var output = new List<Vector2>(subject);
            for (int i = 0; i < clip.Length && output.Count > 0; i++)
            {
                Vector2 c0 = clip[i], c1 = clip[(i + 1) % clip.Length];
                var input = output;
                output = new List<Vector2>(input.Count + 2);
                for (int k = 0; k < input.Count; k++)
                {
                    Vector2 p = input[k], q = input[(k + 1) % input.Count];
                    float dp = Geo.Cross(c1 - c0, p - c0), dq = Geo.Cross(c1 - c0, q - c0);
                    if (dp >= 0f) output.Add(p);
                    if ((dp >= 0f) != (dq >= 0f)) output.Add(p + (q - p) * (dp / (dp - dq)));
                }
            }
            return output;
        }

        /// <summary>Do they overlap, or come within `clearance` of each other (measured square to their edges)?</summary>
        public static bool Overlap(Vector2[] a, Vector2[] b, float clearance = 0f) =>
            !Separated(a, b, clearance) && !Separated(b, a, clearance);

        static bool Separated(Vector2[] a, Vector2[] b, float clearance)
        {
            for (int i = 0; i < a.Length; i++)
            {
                var e = a[(i + 1) % a.Length] - a[i];
                if (e.sqrMagnitude < 1e-8f) continue;
                var n = Geo.Right(e.normalized);
                float min = float.MaxValue;
                foreach (var p in b) min = Mathf.Min(min, Vector2.Dot(p - a[i], n));
                if (min > clearance - 0.05f) return true;
            }
            return false;
        }
    }
}
