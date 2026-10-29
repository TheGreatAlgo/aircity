using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// An aircraft parking stand or a de-icing bay (TaxiwayNetwork.stands). Each stands on its own: it is saved as
    /// its front line (the centreline of the taxilane it opens onto: `position` on it, `front` along it, `lane` its
    /// width) plus a heading, so it can be built before any taxiway and stays put whatever happens to the graph
    /// (splits, rejoins, its taxiway going). It is connected when a taxiway runs along its front line
    /// (TaxiwayNetwork.Connection); until then no aircraft can use it. Everything else, the pad, the paint and where
    /// the aircraft stops, follows from the size code, the angle and the front line (StandLayout).
    /// </summary>
    [Serializable]
    public class Stand
    {
        public int id;
        [Tooltip("Painted identifier: stands 1, 2, ..., de-icing bays D1, D2, ...")]
        public int number;
        public StandKind kind;
        [Tooltip("The largest aircraft it takes (ICAO code letter, by wingspan).")]
        public SizeCode code = SizeCode.C;
        [Tooltip("Grass and dirt stands take codes A and B only; de-icing bays are always paved.")]
        public PavementSurface surface = PavementSurface.Concrete;
        [Tooltip("Parking stands: pushed back, or taxi out nose first.")]
        public StandExit exit;
        [Tooltip("Turns small aircraft away (light aircraft under 5.7 t, such as a Cessna): keeps a jet stand for jets.")]
        public bool noSmallAircraft;
        [Tooltip("Where the stand's centreline crosses its front line, the taxilane centreline (de-icing bays: the bay's " +
                 "middle), Airport space.")]
        public Vector3 position;
        [Tooltip("Bearing of the stand's centreline away from the taxilane, degrees clockwise from north (de-icing " +
                 "bays: the way aircraft taxi through).")]
        public float heading;
        [Tooltip("Bearing of its front line, the taxilane it opens onto (either way along it), degrees clockwise from " +
                 "north. A taxiway laid along it connects the stand.")]
        public float front;
        [Tooltip("Width of the taxilane the stand is laid out for (how far the parked aircraft keeps from it), metres; " +
                 "0: the taxilane its own size code needs. Connecting a wider taxiway doesn't move the stand: it limits " +
                 "the aircraft that may pass it (TaxiwayNetwork.LaneLimits).")]
        [Min(0f)] public float lane;
        // false for stands saved as a point on a taxiway, before stands had their own front line (TaxiwayNetwork.Layout
        // frames them from that taxiway)
        [SerializeField, HideInInspector] bool framed;

        public char Code => code.Letter();
        public bool DeIcing => kind == StandKind.DeIcing;
        public Vector2 Axis => new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
        /// <summary>Along its front line (the way `front` points).</summary>
        public Vector2 Front => new Vector2(Mathf.Sin(front * Mathf.Deg2Rad), Mathf.Cos(front * Mathf.Deg2Rad));
        /// <summary>The taxilane width it's laid out for: `lane`, or its size code's.</summary>
        public float Lane => lane > 0f ? lane : RunwayRules.TaxiwayWidthFor(Code);
        /// <summary>Has it got its own front line (else it's an old save, framed on first layout)?</summary>
        public bool Framed => framed;

        /// <summary>Set its front line's direction and the taxilane width it's laid out for (0: its code's).</summary>
        public void Frame(Vector2 along, float laneWidth)
        {
            front = Mathf.Repeat(Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg, 360f);
            lane = Mathf.Max(laneWidth, 0f);
            framed = true;
        }

        /// <summary>Turn it (heading and front line) clockwise by some degrees about a ground point.</summary>
        public void Turn(float degrees, Vector2 about)
        {
            heading = Mathf.Repeat(heading + degrees, 360f);
            front = Mathf.Repeat(front + degrees, 360f);
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            var d = new Vector2(position.x, position.z) - about;
            position = new Vector3(about.x + d.x * c + d.y * s, 0f, about.y - d.x * s + d.y * c);
        }

        /// <summary>A copy, framed like this one, at another position.</summary>
        public Stand At(Vector3 at)
        {
            var copy = (Stand)MemberwiseClone();
            copy.position = new Vector3(at.x, 0f, at.z);
            return copy;
        }
        /// <summary>"12", "D2": what's painted on it.</summary>
        public string Label => DeIcing ? "D" + number : number.ToString();
        public string Name => DeIcing ? "De-icing bay D" + number : "Stand " + number;
    }

    /// <summary>
    /// Where a stand or de-icing bay lies, worked out from its front line (TaxiwayNetwork.Layout), and the taxiway
    /// connecting it, if one runs along that line (`edge`, else null).
    ///
    /// Frame: s along `axis` from `entry` (on the front line, the taxilane centreline), x across to the right of the
    /// axis. A parking
    /// stand's pad runs from under the taxiway out to `far`, between the lines x = x0 and x = x1, which stay
    /// parallel to the axis: stands of one angle side by side tile with no gaps. The parked aircraft keeps clear of
    /// the taxiway by RunwayRules.TaxilaneClearance (its nearest corner, so angled stands sit further out), with
    /// StandClearance to its neighbours. A taxi-out stand also holds the hairpin the aircraft swings round to end up
    /// nose out (its wings swept clear of the next stand), so it needs about twice the room. A de-icing bay is a
    /// rectangle centred on the taxiway with lanes for the de-icing trucks either side.
    /// </summary>
    public sealed class StandLayout
    {
        /// <summary>Parking stands meet their taxiway at 25 degrees or more.</summary>
        public const float MinSin = 0.42f;
        /// <summary>How far from a taxiway's centreline a stand's position may lie and still be on it.</summary>
        public const float OnCentreline = 0.75f;

        public Stand stand;
        /// <summary>The taxiway it opens onto, or null while none runs along its front line (not connected).</summary>
        public TaxiwayEdge edge;
        /// <summary>The front line's direction, and the unit vector from it towards the stand (de-icing bays: zero).</summary>
        public Vector2 along, side;
        public Vector2 entry, axis;
        /// <summary>Half the width of the taxiway it opens onto (not connected: of the taxilane it's laid out for),
        /// and RunwayRules.TaxilaneClearance for the taxilane it's laid out for (Stand.Lane), which places it.</summary>
        public float halfWidth, clearance;
        /// <summary>Sine of the angle between taxiway and stand (1 = square on), and how much a step in x moves away
        /// from the taxiway: the distance from its centreline is s * sin + x * k.</summary>
        public float sin, k;
        /// <summary>The pad: x0..x1 across, from the near edge (near0 at x0 to near1 at x1) out to far.</summary>
        public float x0, x1, near0, near1, far;
        /// <summary>The design aircraft (the code's longest) parked: its nose and tail along s.</summary>
        public float noseS, tailS;
        /// <summary>Where the nose gear stops (the stop bar), and the way the parked aircraft faces.</summary>
        public Vector2 stop, nose;
        /// <summary>Taxi-out stands: the taxiway direction aircraft arrive along (they turn in on the stand's acute
        /// side) and leave along; the lead-in's turn radius, the x of the track it runs out on beside the stand
        /// centreline, and the s where its U-turn brings it back onto the centreline (LeadIn).</summary>
        public Vector2 inAlong, outAlong;
        public float turnRadius, trackX, uTurnS;

        public char Code => stand.Code;
        public bool TaxiOut => !stand.DeIcing && stand.exit == StandExit.TaxiOut;
        public Vector2 Right => Geo.Right(axis);

        public Vector2 At(float x, float s) => entry + axis * s + Geo.Right(axis) * x;

        /// <summary>The s at which a point x across is d metres from the taxiway centreline (parking stands).</summary>
        public float SAt(float x, float d) => (d - x * k) / sin;

        /// <summary>The pad's corners, counter-clockwise from above.</summary>
        public Vector2[] Pad => new[] { At(x0, near0), At(x1, near1), At(x1, far), At(x0, far) };

        /// <summary>Where an aircraft of this type stands (its origin, under the main gear) with its nose gear on the
        /// stop bar, and the way it faces.</summary>
        public Vector2 ParkPosition(AircraftType type) => stop - nose * type.Wheelbase;

        /// <summary>The middle of the pad.</summary>
        public Vector2 Centre
        {
            get
            {
                var c = Pad;
                return (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            }
        }

        /// <summary>Where the front line crosses the pad's sides: the stretch a taxilane must run along to connect it.</summary>
        public (Vector2 a, Vector2 b) Mouth => stand.DeIcing
            ? (At(0f, near0), At(0f, far))
            : (At(x0, SAt(x0, 0f)), At(x1, SAt(x1, 0f)));

        /// <summary>Is a ground point on the pad?</summary>
        public bool Contains(Vector2 p)
        {
            var c = Pad;
            for (int i = 0; i < 4; i++)
                if (Geo.Cross(c[(i + 1) % 4] - c[i], p - c[i]) < 0f) return false;
            return true;
        }

        /// <summary>Do two pads overlap (more than a few centimetres)? Both are convex quads: separating axes.</summary>
        public bool Overlaps(StandLayout o) => Overlaps(o.Pad);

        /// <summary>Does the pad overlap a convex quad (corners counter-clockwise from above)?</summary>
        public bool Overlaps(Vector2[] quad)
        {
            var a = Pad;
            return !Separated(a, quad) && !Separated(quad, a);
        }

        static bool Separated(Vector2[] a, Vector2[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                var n = Geo.Right((a[(i + 1) % a.Length] - a[i]).normalized);   // outward for a counter-clockwise quad
                // every corner of b is outside this edge of a (within a 5 cm overlap, so neighbours can share an edge)
                float min = float.MaxValue;
                foreach (var p in b) min = Mathf.Min(min, Vector2.Dot(p - a[i], n));
                if (min > -0.05f) return true;
            }
            return false;
        }

        /// <summary>
        /// The layout of a stand not connected to a taxiway, from its own front line, laid out for its own taxilane
        /// width (Stand.Lane). Null if it can't be laid out (a parking stand at too shallow an angle to its front
        /// line; a bay not along it). A stand saved before stands had front lines is taken as square on to one.
        /// </summary>
        public static StandLayout Of(Stand stand)
        {
            var t = stand.Framed ? stand.Front : stand.DeIcing ? stand.Axis : Geo.Right(stand.Axis);
            return Lay(stand, t, stand.Lane, null, Geo.XZ(stand.position));
        }

        /// <summary>
        /// The layout of a stand put against a taxiway section running from a to b: its front line that section's
        /// centreline, its pad running in under the taxiway's pavement. A stand framed with its own front line keeps
        /// that line and its own taxilane (Stand.Lane), so connecting a taxiway never moves it. Null if it can't
        /// sit there (not on its centreline, or at too shallow an angle; a de-icing bay must run along it).
        /// </summary>
        public static StandLayout Of(Stand stand, TaxiwayEdge edge, Vector2 a, Vector2 b)
        {
            if (edge == null || edge.IsRoad) return null;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1f) return null;
            var t = d / len;
            var p = Geo.XZ(stand.position);
            float along = Vector2.Dot(p - a, t);
            if (along < 0f || along > len || Mathf.Abs(Geo.Cross(t, p - a)) > OnCentreline) return null;
            // a stand with its own front line keeps it exactly (the taxiway is within OnCentreline of it); one being put
            // against the taxiway goes onto its centreline
            if (stand.Framed) return Lay(stand, stand.Front, stand.Lane, edge, p);
            return Lay(stand, t, stand.Lane, edge, a + t * along);
        }

        static StandLayout Lay(Stand stand, Vector2 t, float lane, TaxiwayEdge edge, Vector2 entry)
        {
            float paved = edge != null ? edge.width : lane;
            char c = stand.Code;
            float span = RunwayRules.MaxWingspan(c), length = RunwayRules.StandLength(c), clr = RunwayRules.StandClearance(c);
            var l = new StandLayout
            {
                stand = stand,
                edge = edge,
                along = t,
                entry = entry,
                axis = stand.Axis,
                halfWidth = paved * 0.5f,
                clearance = RunwayRules.TaxilaneClearance(lane),
            };
            if (stand.DeIcing)
            {
                if (Mathf.Abs(Vector2.Dot(l.axis, t)) < Mathf.Cos(3f * Mathf.Deg2Rad)) return null;
                float half = span * 0.5f + clr + RunwayRules.DeIcingVehicleLane;
                float halfLength = length * 0.5f + clr + RunwayRules.DeIcingVehicleLane * 0.5f;
                l.x0 = -half;
                l.x1 = half;
                l.near0 = l.near1 = -halfLength;
                l.far = halfLength;
                l.noseS = length * 0.5f;
                l.tailS = -length * 0.5f;
                l.nose = l.axis;
                l.stop = l.At(0f, l.noseS - RunwayRules.NoseGearSetback * length);
                return l;
            }

            float cross = Geo.Cross(t, l.axis);
            l.sin = Mathf.Abs(cross);
            if (l.sin < MinSin) return null;
            l.side = cross > 0f ? Geo.Left(t) : Geo.Right(t);
            l.k = Vector2.Dot(l.Right, l.side);
            float W = (span + clr) * 0.5f;
            // the s at which the aircraft's nearest corner is clearance from the taxiway centreline
            float clearS = (l.clearance + span * 0.5f * Mathf.Abs(l.k)) / l.sin;
            float x0 = -W, x1 = W;
            if (!l.TaxiOut)
            {
                l.tailS = clearS;
                l.noseS = clearS + length;
                l.nose = l.axis;
                l.stop = l.At(0f, l.noseS - RunwayRules.NoseGearSetback * length);
                l.far = l.noseS + clr;
            }
            else
            {
                l.noseS = clearS;
                l.tailS = clearS + length;
                l.nose = -l.axis;
                float stopS = l.noseS + RunwayRules.NoseGearSetback * length;
                l.stop = l.At(0f, stopS);
                l.far = l.tailS + clr;
                l.Hairpin(stopS, span, length, clr, ref x0, ref x1);
            }
            l.x0 = x0;
            l.x1 = x1;
            float under = Mathf.Max(l.halfWidth - 1f, 0f);    // the pad starts under the taxiway, which hides the join
            l.near0 = l.SAt(x0, under);
            l.near1 = l.SAt(x1, under);
            l.far = Mathf.Max(l.far, Mathf.Max(l.near0, l.near1) + 5f);
            return l;
        }

        /// <summary>
        /// A taxi-out stand's lead-in, a hairpin: the aircraft turns off the taxiway (arriving along it on the
        /// stand's acute side) onto a track parallel to the stand centreline, two turn radii to that side, runs out
        /// along it, makes a U-turn back onto the centreline beyond the stop bar and comes down it nose towards the
        /// taxiway. It leaves straight down the centreline and turns the short way onto the taxiway, back the way it
        /// came. The pad widens to hold it: the nose-gear track wherever it's off the taxiway, and the wingtips and
        /// tailplane plus half the clearance wherever they're out of the taxilane's way.
        /// </summary>
        void Hairpin(float stopS, float span, float length, float clr, ref float xMin, ref float xMax)
        {
            // square on (within float noise: the front line is saved as a bearing) counts as leaning `along`
            inAlong = Vector2.Dot(axis, along) >= -1e-4f ? along : -along;
            outAlong = -inAlong;
            turnRadius = RunwayRules.StandTurnRadius(Code);
            trackX = 2f * turnRadius * Mathf.Sign(Vector2.Dot(Right, inAlong) + 1e-4f);
            var corner = At(trackX, SAt(trackX, 0f));
            float td = Geo.Fillet(corner, -inAlong, axis, turnRadius, out _);
            uTurnS = Mathf.Max(stopS + 0.15f * length, Vector2.Dot(corner - entry, axis) + td + 1f);

            float sMax = far, lo = xMin, hi = xMax;
            void Sweep(Vector2 gear, Vector2 heading)
            {
                var g = gear - entry;
                float gx = Vector2.Dot(g, Right), gs = Vector2.Dot(g, axis);
                if (gs * sin + gx * k >= halfWidth)
                {
                    lo = Mathf.Min(lo, gx);
                    hi = Mathf.Max(hi, gx);
                }
                // the nose, tail, wingtips and tailplane tips, bare and with half the clearance
                var r = Geo.Right(heading);
                float wing = span * 0.5f, tail = span * 0.35f, margin = clr * 0.5f;
                var noseP = gear + heading * (RunwayRules.NoseGearSetback * length);
                var wingP = noseP - heading * (0.45f * length);
                var tailP = noseP - heading * length;
                foreach (var q in new[]
                         {
                             noseP, tailP, wingP + r * wing, wingP - r * wing, wingP + r * (wing + margin), wingP - r * (wing + margin),
                             tailP + r * tail, tailP - r * tail, tailP + r * (tail + margin), tailP - r * (tail + margin),
                         })
                {
                    var o = q - entry;
                    float x = Vector2.Dot(o, Right), s = Vector2.Dot(o, axis);
                    if (s * sin + x * k < clearance) continue;    // over the taxilane: kept clear for it anyway
                    lo = Mathf.Min(lo, x);
                    hi = Mathf.Max(hi, x);
                    sMax = Mathf.Max(sMax, s + clr * 0.5f);
                }
            }
            foreach (var (point, heading) in LeadIn(2f)) Sweep(point, heading);
            far = sMax;
            xMin = lo;
            xMax = hi;
        }

        /// <summary>A taxi-out stand's lead-in as points with headings, at most step metres apart: off the taxiway
        /// centreline, out along the track, round the U-turn and down the stand centreline to the stop bar.</summary>
        public List<(Vector2 point, Vector2 heading)> LeadIn(float step)
        {
            var path = new List<(Vector2, Vector2)>();
            var corner = At(trackX, SAt(trackX, 0f));
            float td = Geo.Fillet(corner, -inAlong, axis, turnRadius, out var centre);
            bool left = Geo.Cross(inAlong, axis) > 0f;
            Turn(path, centre, corner - inAlong * td, corner + axis * td, left, step);
            Straight(path, corner + axis * td, At(trackX, uTurnS), axis, step);
            Turn(path, At(trackX * 0.5f, uTurnS), At(trackX, uTurnS), At(0f, uTurnS), trackX > 0f, step);
            Straight(path, At(0f, uTurnS), stop, -axis, step);
            return path;
        }

        static void Straight(List<(Vector2, Vector2)> path, Vector2 from, Vector2 to, Vector2 heading, float step)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / step));
            for (int i = 1; i <= n; i++) path.Add((Vector2.Lerp(from, to, i / (float)n), heading));
        }

        /// <summary>Round a centre from one point to another, turning left (counter-clockwise) or right.</summary>
        static void Turn(List<(Vector2, Vector2)> path, Vector2 centre, Vector2 from, Vector2 to, bool left, float step)
        {
            float r = Vector2.Distance(from, centre);
            float a0 = Geo.Angle(from - centre), a1 = Geo.Angle(to - centre);
            float delta = left ? Mathf.Repeat(a1 - a0, 2f * Mathf.PI) : -Mathf.Repeat(a0 - a1, 2f * Mathf.PI);
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(delta) * r / step));
            for (int i = path.Count == 0 ? 0 : 1; i <= n; i++)
            {
                float ang = a0 + delta * i / n;
                var radial = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                path.Add((centre + radial * r, left ? Geo.Left(radial) : Geo.Right(radial)));
            }
        }

        /// <summary>
        /// The stretch of a taxiway section's sides this stand covers, in that section's s (from its node a) and side
        /// (-1 left, 1 right, 0 both), so the taxiway leaves out its verges and edge lights there; false if it doesn't
        /// run along that section. reach: how far out from the section's centreline to look (its verges).
        /// </summary>
        public bool Frontage(Vector2 a, Vector2 b, float reach, out float s0, out float s1, out int sideOf)
        {
            s0 = s1 = 0f;
            sideOf = 0;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1f) return false;
            var t = d / len;
            if (Mathf.Abs(Geo.Cross(t, along)) > 0.02f || Mathf.Abs(Geo.Cross(t, entry - a)) > OnCentreline) return false;
            var points = new List<Vector2>();
            if (stand.DeIcing)
            {
                points.Add(At(0f, near0));
                points.Add(At(0f, far));
            }
            else
            {
                foreach (float dist in new[] { halfWidth, reach })
                    foreach (float x in new[] { x0, x1 })
                        points.Add(At(x, SAt(x, dist)));
                sideOf = Vector2.Dot(side, Geo.Right(t)) > 0f ? 1 : -1;
            }
            s0 = float.MaxValue;
            s1 = float.MinValue;
            foreach (var q in points)
            {
                float s = Vector2.Dot(q - a, t);
                s0 = Mathf.Min(s0, s);
                s1 = Mathf.Max(s1, s);
            }
            return s1 > 0f && s0 < len;
        }
    }
}
