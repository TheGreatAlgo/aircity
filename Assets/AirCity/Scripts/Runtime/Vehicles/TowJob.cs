using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum TowState
    {
        /// <summary>Queued: no tug that can move it is free yet.</summary>
        Waiting = 0,
        /// <summary>A tug is on its way and backing onto the nose wheel.</summary>
        Coming = 1,
        /// <summary>The tug's cradle grips the nose wheel and lifts it.</summary>
        Lifting = 2,
        Pushing = 3,
        /// <summary>On the taxiway: the cradle lowers the nose wheel and lets go.</summary>
        Lowering = 4,
        Done = 5,
    }

    /// <summary>
    /// An aircraft to be pushed back off its stand by a tug (AirportMaintenance.PushBack): the way it goes, which tug
    /// has it, and how far it has got. The tug (a VehicleTeam of one) moves the aircraft while it holds its nose
    /// wheel.
    /// </summary>
    public sealed class TowJob
    {
        public readonly Aircraft.Aircraft aircraft;
        public readonly PushBackPath path;

        public TowJob(Aircraft.Aircraft aircraft, PushBackPath path)
        {
            this.aircraft = aircraft;
            this.path = path;
        }

        public VehicleTeam Tug { get; internal set; }
        public TowState State { get; internal set; }
        /// <summary>The nose wheel's lift, 0 on the ground .. 1 up in the cradle.</summary>
        public float Lift { get; internal set; }
        /// <summary>The tug picks up the nose wheel and waits with it until this is cleared (a departure waiting for
        /// its push-back to be cleared: AirTraffic).</summary>
        public bool HoldPush { get; set; }
        /// <summary>Hitched, nose wheel up, waiting for HoldPush to be cleared.</summary>
        public bool ReadyToPush => State == TowState.Lifting && Lift >= 1f;

        /// <summary>The aircraft is still there (a tug takes it home if it isn't).</summary>
        public bool Valid => aircraft != null && path != null;
        /// <summary>The aircraft has left its stand (a fleet change can't hand it to another tug from here).</summary>
        public bool Started => State >= TowState.Pushing;

        public string Name => aircraft != null ? aircraft.name : "aircraft";

        public string Status => State switch
        {
            TowState.Waiting => "waiting for a tug",
            TowState.Coming => $"{TugName} on its way",
            TowState.Lifting => $"{TugName} picking up the nose wheel",
            TowState.Pushing => $"pushing back off {path.stand.Name}",
            TowState.Lowering => "pushed back, the tug letting go",
            _ => "pushed back",
        };

        string TugName => Tug != null && Tug.type != null ? (Tug.type.maxTowMass > 200000f ? "large tug" : "tug") : "tug";

        public override string ToString() => $"{Name} on {path.stand.Name}";
    }

    /// <summary>
    /// Where an aircraft goes when it's pushed back: straight back off its push-back stand, then round onto the
    /// taxiway's centreline, facing the way it will taxi (by preference the way that leads on to a runway, then the
    /// smaller turn). The path is planned for the main gear (the aircraft's origin), with the steering wound on and
    /// off gradually (the curvature ramps up and down), so the nose gear's path, which the tug drives backwards with
    /// the nose wheel in its cradle, is smooth too: the tug faces the way the nose wheel points. Both paths are
    /// sampled every quarter metre; Sample gives the aircraft's pose for the tug's distance along the nose path.
    /// </summary>
    public sealed class PushBackPath
    {
        const float SampleStep = 0.25f;
        /// <summary>The steering is wound on over this share of the wheelbase of main-gear travel.</summary>
        const float RampPerWheelbase = 0.8f;

        public readonly Stand stand;
        public readonly StandLayout layout;
        public readonly AircraftType type;
        /// <summary>Where the aircraft (its origin, under the main gear) starts, and the way it faces then and at the end.</summary>
        public readonly Vector2 start, startFacing, endFacing;
        /// <summary>The tightest the main gear turns (metres).</summary>
        public readonly float radius;

        readonly List<Vector2> gear = new List<Vector2>(), facing = new List<Vector2>(), nose = new List<Vector2>();
        readonly List<float> along = new List<float>();

        PushBackPath(Stand stand, StandLayout layout, AircraftType type, Vector2 start, Vector2 startFacing, Vector2 endFacing, float radius)
        {
            this.stand = stand;
            this.layout = layout;
            this.type = type;
            this.start = start;
            this.startFacing = startFacing;
            this.endFacing = endFacing;
            this.radius = radius;
        }

        /// <summary>The nose gear's way (the tug's), metres.</summary>
        public float Length => along[along.Count - 1];
        /// <summary>The nose gear's way as points, from the stand to the taxiway.</summary>
        public IReadOnlyList<Vector2> NosePoints => nose;
        public Vector2 End => gear[gear.Count - 1];
        public Vector2 NoseStart => nose[0];
        public Vector2 NoseEnd => nose[nose.Count - 1];

        /// <summary>
        /// The aircraft's pose when the nose gear is d metres along its way: its origin, the way it faces, the way its
        /// nose wheel points (the tug faces that way), and how far its main gear has come (for the wheels' speed).
        /// </summary>
        public void Sample(float d, out Vector2 position, out Vector2 heading, out Vector2 wheel, out float gearDistance)
        {
            d = Mathf.Clamp(d, 0f, Length);
            int lo = 0, hi = along.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (along[mid] <= d) lo = mid;
                else hi = mid;
            }
            float span = along[hi] - along[lo];
            float t = span > 1e-5f ? (d - along[lo]) / span : 0f;
            position = Vector2.Lerp(gear[lo], gear[hi], t);
            heading = Vector2.Lerp(facing[lo], facing[hi], t).normalized;
            wheel = Vector2.Lerp(Wheel(lo), Wheel(hi), t).normalized;
            gearDistance = (lo + t) * SampleStep;
        }

        /// <summary>Which way the nose wheel points at sample i: back along the nose gear's way (it rolls backwards).</summary>
        Vector2 Wheel(int i)
        {
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(nose.Count - 1, i + 1);
            var d = nose[a] - nose[b];
            return d.sqrMagnitude > 1e-8f ? d.normalized : facing[i];
        }

        /// <summary>The nose-wheel steering angle at a distance along the way (degrees, positive = right).</summary>
        public float SteeringAt(float d)
        {
            Sample(d, out _, out var heading, out var wheel, out _);
            return -Vector2.SignedAngle(heading, wheel);
        }

        // ------------------------------------------------------------------ planning

        /// <summary>
        /// The push-back for an aircraft of `type` standing at `position` (its origin, Airport space) facing `heading`:
        /// null with the reason if it isn't parked nose-in on a push-back stand or there's no room. With otherWay the
        /// second-best way along the taxiway is taken.
        /// </summary>
        public static PushBackPath Plan(TaxiwayNetwork net, AircraftType type, Vector2 position, Vector2 heading, bool otherWay,
            out string problem)
        {
            problem = null;
            heading = heading.normalized;
            StandLayout l = null;
            foreach (var x in net.Layouts())
                if (!x.stand.DeIcing && x.Contains(position))
                {
                    l = x;
                    break;
                }
            if (l == null)
            {
                problem = "it isn't on a stand";
                return null;
            }
            if (l.TaxiOut)
            {
                problem = $"{l.stand.Name} is a taxi-out stand: it leaves under its own power";
                return null;
            }
            if (l.edge == null)
            {
                problem = $"{l.stand.Name} isn't connected: no taxiway runs along its front";
                return null;
            }
            if (Vector2.Angle(heading, l.nose) > 30f)
            {
                problem = $"it isn't parked nose-in on {l.stand.Name}";
                return null;
            }

            // where the stand's line (back through the aircraft) meets the taxiway's centreline
            if (!Geo.Lines(position, heading, l.entry, l.along, out float t, out _) || t > -1f)
            {
                problem = $"it isn't facing away from the taxiway on {l.stand.Name}";
                return null;
            }
            var corner = position + heading * t;
            float room = -t;
            float wheelbase = Mathf.Max(type.Wheelbase, 0.5f);
            float rMin = wheelbase / Mathf.Tan(Mathf.Min(type.maxSteeringAngle, 65f) * Mathf.Deg2Rad);
            float rPref = Mathf.Max(wheelbase * 1.4f, rMin);

            var options = new List<(float score, Vector2 face, float r)>();
            foreach (var face in new[] { l.along, -l.along })
            {
                float turn = Vector2.SignedAngle(heading, face) * Mathf.Deg2Rad;
                float r = -1f;
                for (int k = 0; k <= 12; k++)
                {
                    float rTry = Mathf.Lerp(rPref, rMin, k / 12f);
                    if (Curve(Mathf.Abs(turn), rTry, wheelbase, null) <= room)
                    {
                        r = rTry;
                        break;
                    }
                }
                if (r < 0f) continue;
                float toRunway = RunwayRoute(net, l.edge, face, type);
                float score = (toRunway < 0f ? 1e6f : toRunway * 0.01f) + Mathf.Abs(turn) * Mathf.Rad2Deg * 10f;
                options.Add((score, face, r));
            }
            if (options.Count == 0)
            {
                problem = $"there's no room to push it back onto the taxiway from {l.stand.Name}";
                return null;
            }
            options.Sort((a, b) => a.score.CompareTo(b.score));
            var pick = options[otherWay && options.Count > 1 ? 1 : 0];

            var p = new PushBackPath(l.stand, l, type, position, heading, pick.face, pick.r);
            p.Build(corner, wheelbase);
            return p;
        }

        /// <summary>
        /// A turn through `angle` (radians) whose curvature ramps up to 1/r, holds, and ramps down. Returns the
        /// tangent distance: from where it starts to where its start and end lines cross. With `into`, its points
        /// every SampleStep, starting at the origin going along +x, turning left.
        /// </summary>
        static float Curve(float angle, float r, float wheelbase, List<Vector2> into)
        {
            float ramp = Mathf.Min(RampPerWheelbase * wheelbase, angle * r);
            float hold = angle * r - ramp;
            float length = 2f * ramp + hold, kMax = 1f / r;
            const float du = 0.05f;
            var p = Vector2.zero;
            float a = 0f, next = SampleStep;
            into?.Add(p);
            for (float u = 0f; u < length - 1e-4f; u += du)
            {
                float step = Mathf.Min(du, length - u), mid = u + step * 0.5f;
                float k = mid < ramp ? kMax * mid / ramp : mid < ramp + hold ? kMax : kMax * (length - mid) / ramp;
                float a1 = a + k * step;
                float am = (a + a1) * 0.5f;
                p += new Vector2(Mathf.Cos(am), Mathf.Sin(am)) * step;
                a = a1;
                if (into != null && u + step >= next - 1e-4f)
                {
                    into.Add(p);
                    next += SampleStep;
                }
            }
            if (into != null && (into[into.Count - 1] - p).sqrMagnitude > 1e-6f) into.Add(p);
            // where the end line crosses the x axis
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            if (Mathf.Abs(dir.y) < 1e-4f) return p.x;
            return p.x - dir.x * p.y / dir.y;
        }

        /// <summary>The main gear's way: straight back to the turn, the turn, a little further along the taxiway; then
        /// the headings and the nose gear's way.</summary>
        void Build(Vector2 corner, float wheelbase)
        {
            var back = -startFacing;
            float turn = Vector2.SignedAngle(startFacing, endFacing) * Mathf.Deg2Rad;
            var local = new List<Vector2>();
            float tangent = Curve(Mathf.Abs(turn), radius, wheelbase, local);
            var curveStart = corner - back * tangent;
            // straight back
            float straight = Vector2.Distance(start, curveStart);
            int n = Mathf.Max(1, Mathf.CeilToInt(straight / SampleStep));
            for (int i = 0; i <= n; i++) gear.Add(Vector2.Lerp(start, curveStart, i / (float)n));
            // the turn: local (x along the way back, y to its left), mirrored for a right turn
            var left = Geo.Left(back);
            float side = Mathf.Sign(turn);
            for (int i = 1; i < local.Count; i++) gear.Add(curveStart + back * local[i].x + left * (local[i].y * side));
            // on a little, so the nose wheel ends straight
            var endBack = -endFacing;
            var curveEnd = gear[gear.Count - 1];
            float extra = Mathf.Max(3f, 0.25f * wheelbase);
            n = Mathf.CeilToInt(extra / SampleStep);
            for (int i = 1; i <= n; i++) gear.Add(curveEnd + endBack * (extra * i / n));

            // the aircraft faces against the way it rolls
            for (int i = 0; i < gear.Count; i++)
            {
                Vector2 f;
                if (i == 0) f = startFacing;
                else if (i == gear.Count - 1) f = endFacing;
                else
                {
                    var d = gear[i - 1] - gear[i + 1];
                    f = d.sqrMagnitude > 1e-8f ? d.normalized : facing[i - 1];
                }
                facing.Add(f);
                nose.Add(gear[i] + f * wheelbase);
            }
            along.Add(0f);
            for (int i = 1; i < nose.Count; i++) along.Add(along[i - 1] + Vector2.Distance(nose[i - 1], nose[i]));
        }

        /// <summary>How far an aircraft would taxi to a runway from the end of the stand's taxiway section that it
        /// faces (not back along that section): -1 if it can't (a dead end).</summary>
        static float RunwayRoute(TaxiwayNetwork net, TaxiwayEdge edge, Vector2 face, AircraftType type)
        {
            var a = net.At(edge.a);
            var b = net.At(edge.b);
            int ahead = Vector2.Dot(b - a, face) > 0f ? edge.b : edge.a;
            var runways = new HashSet<int>();
            foreach (var n in net.nodes)
                if (n.runway != null) runways.Add(n.id);
            if (runways.Contains(ahead)) return 0f;
            var route = net.FindRoute(ahead, runways, type, e => e != edge);
            if (route == null) return -1f;
            float len = 0f;
            for (int i = 1; i < route.Count; i++) len += Vector2.Distance(net.At(route[i - 1]), net.At(route[i]));
            return len;
        }
    }
}
