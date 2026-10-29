using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using Pose = AirCity.Vehicles.VehicleManeuver.Pose;

namespace AirCity.Vehicles
{
    /// <summary>
    /// A tug's push-back (a team of one tug): it drives from its depot along the taxiways to the stand, turns in along
    /// the side of the stand (the line between it and the next, clear of the wingtips), swings round in front of the
    /// aircraft's nose and backs straight in under it until the nose wheel is in its cradle. Where a road runs
    /// along the stand's head it comes that way instead and backs round off the road into the stand (RoadIn). The cradle grips and
    /// lifts the wheel, and the tug backs along the nose gear's way (PushBackPath) pushing the aircraft off the
    /// stand and round onto the taxiway; it lowers the wheel, drives on out from under the nose and goes home.
    /// While it does, it moves the aircraft (Step) and keeps clear of everything but that aircraft; inside the stand
    /// it doesn't stop for the parked aircraft either, as its way round was planned clear of them.
    /// </summary>
    public sealed partial class VehicleTeam
    {
        const float StandSpeed = 3f, HitchSpeed = 0.6f, LiftTime = 8f, LowerTime = 6f;

        public TowJob Tow { get; private set; }

        // timeline distances on the path: into the stand, at the nose wheel, the end of the push, out from under the nose
        float enterAt, hitchAt, pushTo, clearAt;
        float towTimer;

        string TowStatus
        {
            get
            {
                string s = Tow.State switch
                {
                    TowState.Coming => $"on the way to {Tow}",
                    TowState.Lifting => $"picking up {Tow.Name}'s nose wheel",
                    TowState.Pushing => $"pushing back {Tow}",
                    TowState.Lowering => $"letting go of {Tow.Name}",
                    _ => "returning to the depot",
                };
                return blockedBy[0] != null && vel[0] < 0.2f ? s + $" (waiting for {blockedBy[0]})" : s;
            }
        }

        /// <summary>Take a push-back: plan the way to the aircraft's nose, the push and the way home. False if it
        /// can't be done.</summary>
        public bool TakeTow(TowJob job)
        {
            if (job == null || !job.Valid || !type.CanTow(job.path.type)) return false;
            var net = Net;
            var l = job.path.layout;
            if (l.edge == null || net.Node(l.edge.a) == null || net.Node(l.edge.b) == null) return false;
            bool parked = state == State.Parked;
            zones.Clear();
            marks.Clear();
            arrivals = new VehiclePath[0];
            doorAt = new float[0];
            if (!parked) Rebase();

            bool byRoad = RoadIn(job, net, out var road, out var ways);
            VehiclePath approach = null;
            Pose entry = default;
            if (!byRoad) approach = Approach(job, parked ? depot.Front : path.End, out entry);
            var targets = byRoad ? new HashSet<int> { road.a, road.b } : new HashSet<int> { l.edge.a, l.edge.b };
            var route = parked ? LeaveDepot(net, targets) : Points(net, FindRoute(net, FromHere(net), targets, path.End), null);
            Follow(route);
            if (byRoad) BackInFromRoad(job, net, road, ways);
            else
            {
                // along the stand's taxiway (keeping right, so aircraft taxiing the other way pass) to just short of
                // it, so only the turn in is a manoeuvre
                var na = net.At(l.edge.a);
                var nb = net.At(l.edge.b);
                var mouth = Geo.ClosestOnSegment(entry.p, na, nb, out _);
                var end = path.End;
                var toward = mouth - Geo.ClosestOnSegment(end, na, nb, out _);
                float run = toward.magnitude - (2f * type.turnRadius + 8f);
                if (Vector2.Distance(end, Geo.ClosestOnSegment(end, na, nb, out _)) < l.halfWidth + 2f && run > 5f)
                {
                    toward.Normalize();
                    var at = mouth - toward * (2f * type.turnRadius + 8f) + Geo.Right(toward) * KeepRight(l.edge.width);
                    Join(new Pose(at, toward), false, PavementFor(false));
                }
                Join(entry, false, PavementFor(false));
                enterAt = path.Length;
                path.Append(approach);
            }
            hitchAt = path.Length;
            foreach (var p in job.path.NosePoints) path.LineTo(p, type.workSpeed, reverse: true);
            pushTo = path.Length;
            // on out from under the nose before turning for home
            var a = job.path.type;
            float clear = a.bounds.max.z - a.Wheelbase - type.bounds.min.z + 3f;
            path.LineTo(job.path.NoseEnd + job.path.endFacing * clear, StandSpeed);
            clearAt = path.Length;
            PlanHome();

            Tow = job;
            job.Tug = this;
            job.State = TowState.Coming;
            job.Lift = 0f;
            towTimer = 0f;
            state = State.Driving;
            stuck = 0f;
            return true;
        }

        /// <summary>
        /// The way in from the taxiway's edge to the nose wheel, along whichever side of the stand is clearer of other
        /// aircraft (then the one nearer `from`): out along the stand's edge past the nose, a quarter turn in across
        /// the front of the stand, on past the nose by a turn's width, then backing round into line behind it and
        /// straight back under it to the nose wheel.
        /// </summary>
        VehiclePath Approach(TowJob job, Vector2 from, out Pose entry)
        {
            var plan = job.path;
            var l = plan.layout;
            var n0 = plan.NoseStart;
            float xN = Vector2.Dot(n0 - l.entry, l.Right), sN = Vector2.Dot(n0 - l.entry, l.axis);
            float r = type.turnRadius, rear = -type.bounds.min.z;
            float noseAhead = plan.type.bounds.max.z - plan.type.Wheelbase;
            // the line across the front of the stand: the turn behind the nose starts clear of it
            float head = sN + noseAhead + r + rear + 1.5f;
            var others = depot.Airport.Maintenance.ParkedAircraft(job.aircraft);
            VehiclePath best = null;
            int bestOff = int.MaxValue;
            float bestD = float.MaxValue;
            entry = default;
            var poses = new List<(Vector2 p, Vector2 facing)>();
            foreach (float side in new[] { 1f, -1f })
            {
                float xs = side > 0f ? l.x1 : l.x0;
                var e = l.At(xs, l.SAt(xs, l.halfWidth));
                var p = new VehiclePath();
                p.Start(e, StandSpeed);
                p.LineTo(l.At(xs, head - r), StandSpeed);
                p.ArcAround(l.At(xs - side * r, head - r), side * Mathf.PI * 0.5f, StandSpeed);
                p.LineTo(l.At(xN - side * r, head), StandSpeed);
                p.ArcAround(l.At(xN - side * r, head - r), -side * Mathf.PI * 0.5f, ReverseSpeed, reverse: true);
                p.LineTo(n0, HitchSpeed, reverse: true);
                poses.Clear();
                p.Poses(0f, p.Length, 1f, poses);
                int off = 0;
                foreach (var (q, f) in poses)
                {
                    var box = AirportMaintenance.Box.Of(q, f, type.bounds, 0.2f);
                    foreach (var o in others)
                        if (box.Overlaps(o))
                        {
                            off++;
                            break;
                        }
                }
                float d = Vector2.Distance(from, e);
                if (off < bestOff || (off == bestOff && d < bestD))
                {
                    best = p;
                    bestOff = off;
                    bestD = d;
                    entry = new Pose(e, l.axis);
                }
            }
            return best;
        }

        /// <summary>
        /// A road along the head of the stand (TaxiwayNetwork.RoadAccess) the tug can come in from, and the
        /// directions along it it can do that from (BackIn). False if there's none: it comes along the taxiway.
        /// </summary>
        bool RoadIn(TowJob job, TaxiwayNetwork net, out TaxiwayEdge road, out List<Vector2> ways)
        {
            road = null;
            ways = new List<Vector2>();
            var l = job.path.layout;
            foreach (var access in net.RoadAccess(l))
            {
                if (access.side != 0) continue;
                var u = (net.At(access.road.b) - net.At(access.road.a)).normalized;
                if (Mathf.Abs(Vector2.Dot(u, l.axis)) > 0.5f) continue;    // it must run across the head, not into it
                foreach (var d in new[] { u, -u })
                    if (BackIn(job, net, access.road, d, out _, out _, out _)) ways.Add(d);
                if (ways.Count == 0) continue;
                road = access.road;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Backing in off a road along the stand's head, driving along it in direction d: on in its lane to a turn's
        /// radius past the stand's centreline (start), then backing a quarter turn round `centre` into the stand,
        /// facing out, onto the centreline in front of the nose. False if the turn would start off the end of the
        /// road section or end too near the nose.
        /// </summary>
        bool BackIn(TowJob job, TaxiwayNetwork net, TaxiwayEdge road, Vector2 d, out Vector2 start, out Vector2 centre,
            out float angle)
        {
            var plan = job.path;
            var l = plan.layout;
            var ra = net.At(road.a);
            var rb = net.At(road.b);
            float r = type.turnRadius;
            float keep = Mathf.Clamp(road.width * 0.5f - type.Width * 0.5f - 0.4f, 0f, 3.6f);
            var lane = ra + Geo.Right(d) * keep;
            var n0 = plan.NoseStart;
            start = centre = default;
            angle = Mathf.Sign(Geo.Cross(l.axis, -d)) * Mathf.PI * 0.5f;
            if (!Geo.Lines(lane, d, n0, l.axis, out float t, out _)) return false;
            var x = lane + d * t;
            start = x + d * r;
            centre = start - l.axis * r;
            float along = Vector2.Dot(start - ra, (rb - ra).normalized);
            if (along < 0f || along > Vector2.Distance(ra, rb)) return false;
            // the tug's back, facing out at the end of the turn, clear of the nose
            float noseAhead = plan.type.bounds.max.z - plan.type.Wheelbase;
            return Vector2.Dot(x - l.axis * r - n0, l.axis) + type.bounds.min.z >= noseAhead + 1.5f;
        }

        /// <summary>From the road the route reached on, the way in: along the road heading on towards the stand (if
        /// it can back in from that way), the back-in and straight back to the nose wheel.</summary>
        void BackInFromRoad(TowJob job, TaxiwayNetwork net, TaxiwayEdge road, List<Vector2> ways)
        {
            var end = path.End;
            var d = ways[0];
            BackIn(job, net, road, d, out var start, out _, out _);
            if (ways.Count > 1 && Vector2.Dot(start - end, d) < 0f) d = ways[1];
            BackIn(job, net, road, d, out start, out var centre, out float angle);
            float lead = 2f * type.turnRadius + 8f;
            if (Vector2.Dot(start - end, d) > lead + 5f) Join(new Pose(start - d * lead, d), false, PavementFor(false));
            Join(new Pose(start, d), false, PavementFor(false));
            enterAt = path.Length;
            path.ArcAround(centre, angle, ReverseSpeed, reverse: true);
            path.LineTo(job.path.NoseStart, HitchSpeed, reverse: true);
        }

        /// <summary>Where the tug must stop: at the nose wheel until it's lifted, at the end of the push until it's
        /// lowered.</summary>
        float TowHold() => Tow.State switch
        {
            TowState.Coming or TowState.Lifting => hitchAt,
            TowState.Pushing or TowState.Lowering => pushTo,
            _ => float.MaxValue,
        };

        /// <summary>After the tug has moved: lift, push, lower; the aircraft goes where the nose wheel is. True when the
        /// push-back has just finished.</summary>
        bool StepTow(float dt)
        {
            var job = Tow;
            switch (job.State)
            {
                case TowState.Coming:
                    if (pos[0] >= enterAt) state = State.Working;
                    if (pos[0] >= hitchAt - 0.45f && vel[0] < 0.3f)
                    {
                        pos[0] = hitchAt;
                        vel[0] = 0f;
                        job.State = TowState.Lifting;
                        towTimer = 0f;
                    }
                    break;
                case TowState.Lifting:
                    towTimer += dt;
                    job.Lift = Mathf.Clamp01(towTimer / LiftTime);
                    if (towTimer >= LiftTime && !job.HoldPush) job.State = TowState.Pushing;
                    break;
                case TowState.Pushing:
                    if (pos[0] >= pushTo - 0.45f && vel[0] < 0.3f)
                    {
                        pos[0] = pushTo;
                        vel[0] = 0f;
                        job.State = TowState.Lowering;
                        towTimer = 0f;
                    }
                    break;
                case TowState.Lowering:
                    towTimer += dt;
                    job.Lift = 1f - Mathf.Clamp01(towTimer / LowerTime);
                    if (towTimer >= LowerTime)
                    {
                        job.Lift = 0f;
                        PlaceAircraft(false);
                        job.State = TowState.Done;
                        state = State.Returning;
                        return true;
                    }
                    break;
                default:
                    // clear of the aircraft: an ordinary vehicle again
                    if (pos[0] >= clearAt)
                    {
                        Tow = null;
                        job.Tug = null;
                    }
                    return false;
            }
            if (job.State >= TowState.Lifting) PlaceAircraft(job.State == TowState.Pushing);
            return false;
        }

        /// <summary>Put the aircraft where the nose wheel is along its way, its nose raised by the cradle, its wheels
        /// turning and the nose wheel steered the way the tug faces.</summary>
        void PlaceAircraft(bool moving)
        {
            var job = Tow;
            var a = job.aircraft;
            if (a == null) return;
            var plan = job.path;
            float d = Mathf.Clamp(pos[0] - hitchAt, 0f, plan.Length);
            plan.Sample(d, out var p, out var heading, out var wheel, out float gearAt);
            var airport = depot.Airport;
            if (airport == null) return;   // the airport is going
            var t = airport.transform;
            float pitch = Mathf.Atan2(job.Lift * type.liftHeight, Mathf.Max(plan.type.Wheelbase, 0.5f)) * Mathf.Rad2Deg;
            var rotation = Quaternion.LookRotation(t.TransformDirection(Geo.X0Z(heading)), Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
            a.transform.SetPositionAndRotation(t.TransformPoint(Geo.X0Z(p)), rotation);
            var motion = a.GetComponent<AircraftGroundMotion>();
            if (motion == null) return;
            float speed = 0f;
            if (moving)
            {
                // the main gear's speed: the tug's, scaled by how far the main gear goes per metre of the nose gear's way
                plan.Sample(d + 1f, out _, out _, out _, out float gearNext);
                speed = vel[0] * Mathf.Max(gearNext - gearAt, 0f);
            }
            motion.Drive(-speed, -Vector2.SignedAngle(heading, wheel) / Mathf.Max(motion.maxSteeringAngle, 1f));
        }

        /// <summary>The aircraft has gone: drive home from here (false while still on the way out of the yard).</summary>
        bool RecallTow()
        {
            if (Min(pos) < 0f) return false;
            DropTow();
            Rebase();
            zones.Clear();
            marks.Clear();
            PlanHome();
            state = State.Returning;
            return true;
        }

        /// <summary>Let go of the push-back: the aircraft stays where it is, nose down; one that hasn't left its stand
        /// waits for another tug.</summary>
        void DropTow()
        {
            var job = Tow;
            if (job == null) return;
            if (job.State >= TowState.Lifting && job.State <= TowState.Lowering)
            {
                job.Lift = 0f;
                PlaceAircraft(false);
            }
            Tow = null;
            job.Tug = null;
            job.Lift = 0f;
            job.State = job.Started ? TowState.Done : TowState.Waiting;
        }

        /// <summary>Held up on the way to the stand: plan the way there again (not once it's in the stand).</summary>
        void RerouteTow()
        {
            if (pos[0] >= enterAt - 1f) return;
            var job = Tow;
            Tow = null;
            job.Tug = null;
            if (TakeTow(job)) return;
            job.State = TowState.Waiting;
            Rebase();
            zones.Clear();
            PlanHome();
            state = State.Returning;
        }

        // ------------------------------------------------------------------ for keeping clear (AirportMaintenance)

        /// <summary>The aircraft a tug is coming for, holds, or is driving out from under.</summary>
        internal bool Ignores(Aircraft.Aircraft a) => Tow != null && a == Tow.aircraft;

        /// <summary>Inside the stand (its way round planned clear of the parked aircraft) at timeline distance d.</summary>
        internal bool InStand(float d) => Tow != null && d >= enterAt - 0.5f && d <= clearAt;

        /// <summary>The towed aircraft's fuselage with the tug at timeline distance d, while it holds the nose wheel
        /// (other vehicles may be under its wings).</summary>
        internal bool TowedBox(float d, out AirportMaintenance.Box box)
        {
            box = default;
            if (Tow == null || Tow.State < TowState.Lifting || Tow.State > TowState.Lowering || d < hitchAt || d > pushTo) return false;
            Tow.path.Sample(d - hitchAt, out var p, out var heading, out _, out _);
            box = AirportMaintenance.Outline(p, heading, Tow.path.type, 0.5f)[0];
            return true;
        }
    }
}
