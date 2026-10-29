using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using Pose = AirCity.Vehicles.VehicleManeuver.Pose;

namespace AirCity.Vehicles
{
    /// <summary>
    /// A team of identical vehicles (one to four snow ploughs, or mowers; a tug works alone, see VehicleTeam.Tow.cs)
    /// from a depot. It works one job at a time
    /// (MaintenanceJob): the vehicles drive out of their garage bays one after another as the roller doors go
    /// up, form up in single file on the road or taxiway in front, fan out into echelon on the
    /// strip and work it pass by pass (StripWork); then take the next job or drive home, where each backs into its
    /// bay in turn.
    ///
    /// Most of it is one shared path: vehicle i follows vehicle i-1 a gap behind, pushed out i lanes to the left
    /// by the path's spread. Before the shared path starts (at the merge point on the road) and after it ends
    /// (back there), each vehicle is on its own short path from / to its bay. Each vehicle has its own position
    /// and speed along this timeline, so the followers can wait their turn: for a door, at a turn with reversing
    /// in it (one vehicle at a time), or for whatever is in the way (AirportMaintenance sets the speed caps). The
    /// lead slows to a crawl until the team is back in formation. Taxiway and road runs are worked end to end and
    /// turned round only at their ends, on pavement for the ploughs (VehicleManeuver). Simulated in game seconds
    /// without visuals (tests); the depot gives it Vehicle instances to place.
    /// </summary>
    public sealed partial class VehicleTeam
    {
        public enum State { Parked, Driving, Working, Returning }

        const float Accel = 1.2f, Decel = 2.2f, YardSpeed = 3.5f, ReverseSpeed = 2.2f, RegroupSpeed = 1.2f, TurnSpeed = 6f;
        const float RunwayApproach = 45f, RunwayExit = 30f, VergeApproach = 25f, VergeExit = 20f, RunRamp = 18f;
        const float ZoneClearance = 6f, HomeHoldBack = 40f;

        public readonly MaintenanceDepot depot;
        public readonly VehicleType type;
        public readonly int size, firstBay;
        public readonly string name;
        public readonly List<Vehicle> vehicles = new List<Vehicle>();

        public State state = State.Parked;
        public MaintenanceJob Job { get; private set; }

        readonly VehiclePath path = new VehiclePath();
        VehiclePath[] departures = new VehiclePath[0], arrivals = new VehiclePath[0];
        float[] doorAt = new float[0];          // per vehicle: where on its arrival it backs through its door
        readonly float[] pos, vel;
        /// <summary>Speed caps for what's in the way, per vehicle (AirportMaintenance sets them every step).</summary>
        internal readonly float[] cap;
        internal readonly string[] blockedBy;
        readonly List<(int pass, float switchAt, float start, float end)> marks = new List<(int, float, float, float)>();
        readonly List<(float from, float to)> zones = new List<(float, float)>();
        float finishAt;
        internal float stuck;                   // game seconds the lead has stood behind a parked aircraft
        internal float heldByAircraft;          // game seconds one of them has stood held up by an aircraft
        internal float standoff, backedFor;     // game seconds head to head with an aircraft, and backed off
        internal float backedTotal;             // game seconds since it first backed off
        internal Aircraft.Aircraft backedFrom;  // the aircraft it backed off from

        public VehicleTeam(MaintenanceDepot depot, VehicleType type, int size, int firstBay, string name)
        {
            this.depot = depot;
            this.type = type;
            this.size = Mathf.Max(1, size);
            this.firstBay = firstBay;
            this.name = name;
            pos = new float[this.size];
            vel = new float[this.size];
            cap = new float[this.size];
            blockedBy = new string[this.size];
            for (int i = 0; i < this.size; i++) cap[i] = float.PositiveInfinity;
        }

        public float Gap => type.Gap;
        /// <summary>Free for a job: parked, or on the way home and not yet back in the yard.</summary>
        public bool Idle => state == State.Parked ||
                            (state == State.Returning && Job == null && Tow == null && pos[0] < path.Length - 1f && Min(pos) >= 0f);
        public float Speed => vel[0];
        /// <summary>Out of the depot (or on the way out / in).</summary>
        public bool Active => state != State.Parked;

        /// <summary>Where the lead is (Airport space).</summary>
        public Vector2 LeadPosition => Where(0, pos[0], out _, out _, out _);

        public string Status
        {
            get
            {
                if (Tow != null) return TowStatus;
                string s = state switch
                {
                    State.Parked => "parked",
                    State.Driving => $"driving to {Job}",
                    State.Working => $"{(type.Work == WorkKind.Plough ? "ploughing" : "mowing")} {Job}, pass {Mathf.Min(Job.work.Pass + 1, Job.work.Passes)} of {Job.work.Passes} ({Job.work.Progress:P0})",
                    _ => "returning to the depot",
                };
                for (int i = 0; i < size; i++)
                    if (blockedBy[i] != null && vel[i] < 0.2f) return s + $" (waiting for {blockedBy[i]})";
                return s;
            }
        }

        static float Min(float[] a)
        {
            float m = float.MaxValue;
            foreach (float x in a) m = Mathf.Min(m, x);
            return m;
        }

        TaxiwayNetwork Net => depot.Airport.Taxiways;
        Pavement PavementFor(bool verge) => type.Work == WorkKind.Plough && !verge ? depot.Airport.Pavement : null;

        // ------------------------------------------------------------------ timeline

        float Ld(int i) => i < departures.Length && departures[i] != null ? departures[i].Length : 0f;
        float La(int i) => i < arrivals.Length && arrivals[i] != null ? arrivals[i].Length : 0f;
        float Start(int i) => -Ld(i);
        float End(int i) => path.Length + La(i);
        bool Home(int i) => arrivals.Length > 0 && pos[i] >= End(i) - 0.05f;

        /// <summary>Vehicle i's rear-axle position at timeline distance d, which way it faces, the path's curvature
        /// and whether it is reversing there: in its bay, on its own way out / in, or on the shared path (pushed
        /// out to its lane while in echelon).</summary>
        Vector2 Where(int i, float d, out Vector2 facing, out float curvature, out bool reversing)
        {
            curvature = 0f;
            reversing = false;
            if (state == State.Parked || path.Count < 2)
            {
                facing = depot.Outward;
                return depot.Bay(firstBay + i, type);
            }
            if (d < 0f && i < departures.Length && departures[i] != null)
            {
                var p = departures[i];
                float t = Mathf.Max(0f, p.Length + d);
                reversing = p.Reversing(t);
                return p.Pose(t, out facing, out curvature);
            }
            if (d > path.Length && i < arrivals.Length && arrivals[i] != null)
            {
                var p = arrivals[i];
                float t = Mathf.Min(d - path.Length, p.Length);
                reversing = p.Reversing(t);
                return p.Pose(t, out facing, out curvature);
            }
            var c = path.Pose(d, out facing, out curvature);
            reversing = path.Reversing(d);
            path.Sample(d, out var heading, out float fan, out _);
            return c + Geo.Left(heading) * (i * type.laneWidth * fan);
        }

        internal Vector2 Where(int i, float d, out Vector2 facing) => Where(i, d, out facing, out _, out _);

        /// <summary>Where vehicle i will be through its next turn with reversing in it (a zone), every `step` metres,
        /// once it's within `ahead` metres of it: other teams keep off that ground until it's through.</summary>
        internal void Claim(int i, float ahead, float step, List<(Vector2 p, Vector2 facing)> into)
        {
            float d = pos[i];
            foreach (var (z0, z1) in zones)
            {
                if (d > z1 || d < z0 - ahead) continue;
                for (float x = Mathf.Max(d, z0); x <= z1; x += step) into.Add((Where(i, x, out var f), f));
                return;
            }
        }
        internal float Position(int i) => pos[i];
        internal float Velocity(int i) => vel[i];
        /// <summary>On its own way out of or into the depot (not the shared path).</summary>
        internal bool InYard(int i) => state != State.Parked && (pos[i] < 0f || pos[i] > path.Length);

        /// <summary>Standing in its bay (in the garage or the overflow row), not in anyone's way.</summary>
        internal bool InBay(int i) => state == State.Parked || Home(i) || (Ld(i) > 0f && pos[i] <= Start(i) + 0.01f);

        float Limit(int i, float d)
        {
            if (d < 0f && i < departures.Length && departures[i] != null)
            {
                departures[i].Sample(Mathf.Max(0f, departures[i].Length + d), out _, out _, out float l);
                return l;
            }
            if (d > path.Length && i < arrivals.Length && arrivals[i] != null)
            {
                arrivals[i].Sample(Mathf.Min(d - path.Length, arrivals[i].Length), out _, out _, out float l);
                return l;
            }
            if (d < 0f || d > path.Length) return YardSpeed;
            path.Sample(d, out _, out _, out float limit);
            return limit;
        }

        // ------------------------------------------------------------------ jobs

        /// <summary>Take a job: plan the way there, the passes and the way home. False if it can't be done.</summary>
        public bool Take(MaintenanceJob job)
        {
            if (!job.Valid) return false;
            var net = Net;
            bool parked = state == State.Parked;
            zones.Clear();
            marks.Clear();
            arrivals = new VehiclePath[0];
            doorAt = new float[0];
            if (!parked) Rebase();

            // the route to the strip: from the depot's way onto the network, or from where the team is
            var targets = Targets(net, job);
            var route = parked ? LeaveDepot(net, targets) : Points(net, FindRoute(net, FromHere(net), targets, path.End), null);
            Follow(route);

            // which end the team comes in at: the passes start there
            var arrive = path.End;
            bool reversed;
            if (job.IsRunway) reversed = Vector2.Dot(arrive - job.Origin, job.Axis) > job.Length * 0.5f;
            else
            {
                var (s, e) = job.EndNodes;
                reversed = Vector2.Distance(arrive, net.At(e)) < Vector2.Distance(arrive, net.At(s));
            }
            var work = new StripWork(job.kind, job.Length, job.HalfWidth, job.Margin, type.laneWidth, size, reversed);
            if (job.IsRunway) RunwayPasses(job, work);
            else RunPasses(job, work);
            finishAt = marks[marks.Count - 1].end + 1f;

            // home (re-planned if another job is waiting when this one is done)
            PlanHome();

            Job = job;
            job.Assign(work);
            state = State.Driving;
            stuck = 0f;
            return true;
        }

        /// <summary>Runways and their verges: straight passes with an approach and an exit beyond the ends (over
        /// the grass), U-turns between them.</summary>
        void RunwayPasses(MaintenanceJob job, StripWork work)
        {
            float approach = job.verge ? VergeApproach : RunwayApproach, exit = job.verge ? VergeExit : RunwayExit;
            for (int k = 0; k < work.Passes; k++)
            {
                float lane = LaneX(job, work, k, out float fan);
                var dir = job.Axis * work.Direction(k);
                var a = StripPoint(job, work, k, -approach, lane);
                var s = StripPoint(job, work, k, 0f, lane);
                var e = StripPoint(job, work, k, job.Length + 2f * job.Margin, lane);
                var x = StripPoint(job, work, k, job.Length + 2f * job.Margin + exit, lane);
                path.TurnTo(path.EndFacing, a, dir, type.turnRadius, Mathf.Min(type.transitSpeed, TurnSpeed));
                float dA = path.Length;
                path.LineTo(s, type.workSpeed, fan, approach, 0f);
                float dS = path.Length;
                path.LineTo(e, type.workSpeed, fan);
                float dE = path.Length;
                path.LineTo(x, type.workSpeed, fan, 0f, exit);
                marks.Add((k, dA, dS, dE));
            }
        }

        /// <summary>
        /// The lead's lane on pass k, pulled in where the pattern's band would hang over the edge of the strip (a
        /// team wider than what's left of it: the shader's pattern shows the same result, the vehicles just stay on
        /// the pavement), and how far the echelon fans out: less than a lane per vehicle when even a centred pass
        /// is wider than the strip.
        /// </summary>
        float LaneX(MaintenanceJob job, StripWork work, int k, out float fan)
        {
            float lead = work.LeadOffset(k), lane = type.laneWidth, hw = job.HalfWidth;
            fan = 1f;
            if (work.Centred)
            {
                if (size > 1 && work.BandWidth > 2f * hw)
                {
                    fan = Mathf.Clamp01((2f * hw - lane) / ((size - 1) * lane));
                    lead = Mathf.Sign(lead) * (size - 1) * lane * fan * 0.5f;
                }
                return lead;
            }
            // followers are on the left of the way the pass goes: towards -x going up the strip, +x coming back
            float last = lead - (size - 1) * lane * work.Direction(k);
            float outer = Mathf.Abs(lead) > Mathf.Abs(last) ? lead : last;
            float over = Mathf.Abs(outer) + lane * 0.5f - hw;
            if (over > 0f && over < work.BandWidth) lead -= Mathf.Sign(outer) * over;
            return lead;
        }

        /// <summary>Taxiway and road runs (and their verges): each pass follows the run's centreline round its
        /// curves, the echelon fanning out inside it; the turns at the ends are planned on the pavement.</summary>
        void RunPasses(MaintenanceJob job, StripWork work)
        {
            var pavement = PavementFor(job.verge);
            int n = work.Passes;
            var x = new float[n];
            var fan = new float[n];
            var s0 = new float[n];
            var s1 = new float[n];
            for (int k = 0; k < n; k++)
            {
                x[k] = LaneX(job, work, k, out fan[k]);
                bool up = work.Direction(k) > 0;
                // ploughs stop with the blade at the far edge of the pavement, and start with their tail on it
                s0[k] = up ? -Allow(job, pavement, 0f, -1f, x[k], -type.bounds.min.z) : job.Length + Allow(job, pavement, job.Length, 1f, x[k], -type.bounds.min.z);
                s1[k] = up ? job.Length + Allow(job, pavement, job.Length, 1f, x[k], type.Reach) : -Allow(job, pavement, 0f, -1f, x[k], type.Reach);
            }
            var (startIn, reverseIn) = TurnInto(job, work, x, s0, pavement);
            for (int k = 0; k < n; k++)
            {
                int d = work.Direction(k);
                float from = s0[k] + d * startIn;
                Join(new Pose(job.At(from, x[k]), job.Direction(Mathf.Clamp(from, 0f, job.Length)) * d), reverseIn, pavement);
                float cut = 0f, nextIn = 0f;
                bool nextReverse = false;
                if (k < n - 1)
                {
                    (cut, nextIn, nextReverse) = TurnRound(job, work, k, x, s0, s1, pavement);
                }
                var lane = job.Lane(x[k], from, s1[k] - d * cut);
                float dS = path.Length;
                if (Vector2.Distance(path.End, lane[0]) > 0.5f) path.LineTo(lane[0], type.workSpeed);
                float right = job.Across(x[k]) * d, paved = job.Gap + job.HalfWidth;
                path.Through(lane.GetRange(1, lane.Count - 1), Mathf.Max(job.CornerRadius, type.turnRadius), type.workSpeed, 0f, right,
                    pavement == null ? null : (side, r) =>
                    {
                        // the front corner on the outside swings wide of the rear axle's arc
                        float half = type.Width * 0.5f, front = type.bounds.max.z;
                        float swing = Mathf.Sqrt((r + half) * (r + half) + front * front) - (r + half);
                        float room = paved - (side * right + half) + 0.6f;
                        return swing - room;
                    });
                float dE = path.Length;
                float ramp = Mathf.Min(RunRamp, (dE - dS) * 0.3f);
                path.SetSpread(dS, dE, fan[k], ramp, ramp);
                marks.Add((k, dS, dS, dE));
                startIn = nextIn;
                reverseIn = nextReverse;
            }
        }

        static readonly float[] Cuts = { 0f, 6f, 12f, 18f, 26f }, StartsIn = { 0f, 8f, 16f };

        /// <summary>
        /// How to turn round between pass k and the next at the end of a run, keeping the plough on the pavement:
        /// the pass may stop a little short (a T end is too tight to turn in once the blade is at the far edge)
        /// and the next pass start a little in, or be backed into. The first combination that keeps the footprint
        /// on the pavement wins (else the one that strays least). Mowers turn on the grass: no search.
        /// </summary>
        (float cut, float startIn, bool reverse) TurnRound(MaintenanceJob job, StripWork work, int k, float[] x, float[] s0, float[] s1,
            Pavement pavement)
        {
            if (pavement == null) return (0f, 0f, false);
            int d = work.Direction(k);
            var best = (0f, 0f, false);
            int bestOff = int.MaxValue;
            foreach (float cut in Cuts)
            {
                float end = s1[k] - d * cut;
                var at = job.At(end, x[k]);
                var facing = job.Direction(Mathf.Clamp(end, 0f, job.Length)) * d;
                var (startIn, reverse, off) = Probe(job, work, k + 1, x, s0, at, facing, pavement);
                if (off < bestOff)
                {
                    bestOff = off;
                    best = (cut, startIn, reverse);
                }
                if (off == 0) break;
            }
            return best;
        }

        /// <summary>The way into pass k's start from where the path ends now (the route in).</summary>
        (float startIn, bool reverse) TurnInto(MaintenanceJob job, StripWork work, float[] x, float[] s0, Pavement pavement)
        {
            if (pavement == null) return (0f, false);
            var (startIn, reverse, _) = Probe(job, work, 0, x, s0, path.End, path.EndFacing, pavement);
            return (startIn, reverse);
        }

        /// <summary>From a pose, try the start of pass k pulled in by 0 / 8 / 16 m, reached forwards or backwards:
        /// the first that stays on the pavement, or the one that strays least.</summary>
        (float startIn, bool reverse, int off) Probe(MaintenanceJob job, StripWork work, int k, float[] x, float[] s0, Vector2 at, Vector2 facing,
            Pavement pavement)
        {
            int d = work.Direction(k);
            var via = VehicleManeuver.AroundNodes(Net, at, 50f);
            var probe = new VehiclePath();
            var poses = new List<(Vector2 p, Vector2 facing)>();
            (float, bool, int) best = (0f, false, int.MaxValue);
            foreach (float startIn in StartsIn)
                foreach (bool reverse in new[] { false, true })
                {
                    float from = s0[k] + d * startIn;
                    var goal = new Pose(job.At(from, x[k]), job.Direction(Mathf.Clamp(from, 0f, job.Length)) * d);
                    probe.Clear();
                    probe.Start(at - facing * 3f, TurnSpeed);
                    probe.LineTo(at, TurnSpeed);
                    VehicleManeuver.Append(probe, goal, reverse, via, type, pavement, TurnSpeed, ReverseSpeed);
                    poses.Clear();
                    probe.Poses(3f, probe.Length, 2f, poses);
                    int off = VehicleManeuver.OffPavement(poses, type, pavement, best.Item3);
                    if (off < best.Item3) best = (startIn, reverse, off);
                    if (off == 0) return best;
                }
            return best;
        }

        /// <summary>How far beyond an end of the run (s = at, going `outwards` along it) the lead may go on lane x:
        /// the job's margin, less if the pavement there ends before the vehicle's `need` metres (blade or tail)
        /// would be on it. Without a pavement map (mowers), the margin.</summary>
        float Allow(MaintenanceJob job, Pavement pavement, float at, float outwards, float x, float need)
        {
            if (pavement == null) return job.Margin;
            var p = job.At(at, x);
            var d = job.Direction(Mathf.Clamp(at, 0f, job.Length)) * outwards;
            float t = 0f;
            while (t < 40f && pavement.Contains(p + d * (t + 0.5f))) t += 0.5f;
            return Mathf.Min(job.Margin, t - need - 0.5f);
        }

        /// <summary>The lead's point on pass k, `along` metres from where that pass starts, on lane x.</summary>
        static Vector2 StripPoint(MaintenanceJob job, StripWork w, int k, float along, float x)
        {
            float s = w.PassStart(k) + w.Direction(k) * along;
            return job.At(s, x);
        }

        static HashSet<int> Targets(TaxiwayNetwork net, MaintenanceJob job)
        {
            var t = new HashSet<int>();
            if (job.IsRunway)
            {
                foreach (var n in net.nodes)
                    if (n.runway == job.runway) t.Add(n.id);
            }
            else
            {
                var (s, e) = job.EndNodes;
                t.Add(s);
                t.Add(e);
            }
            return t;
        }

        // ------------------------------------------------------------------ routing

        /// <summary>Start nodes from the end of the path: the two ends of the edge it's on (the one behind costs a
        /// turn round), and the nearest nodes across country, but not one whose straight line passes within
        /// AcrossCountryClear of a parked aircraft (vehicles go between parked aircraft only into the stand they work
        /// on).</summary>
        List<(int, float)> FromHere(TaxiwayNetwork net)
        {
            var p = path.End;
            var facing = path.EndFacing;
            var starts = Nearest(net, p, 4, 1.5f);
            var near = depot.Airport != null ? depot.Airport.Maintenance.RouteBlocked(type, AcrossCountryClear) : null;
            if (near != null) starts.RemoveAll(x => near(p, net.At(x.Item1)));
            if (net.TryNearestEdge(Geo.X0Z(p), 20f, out var e, out _))
                foreach (int n in new[] { e.a, e.b })
                {
                    var d = net.At(n) - p;
                    starts.Add((n, d.magnitude + (Vector2.Dot(d, facing) < 0f ? 2f * VehicleManeuver.CuspCost : 0f)));
                }
            return starts;
        }

        static List<(int, float)> Nearest(TaxiwayNetwork net, Vector2 p, int count, float costPerMetre)
        {
            var sorted = new List<TaxiwayNode>(net.nodes);
            sorted.Sort((a, b) => (Geo.XZ(a.position) - p).sqrMagnitude.CompareTo((Geo.XZ(b.position) - p).sqrMagnitude));
            var list = new List<(int, float)>();
            for (int i = 0; i < Mathf.Min(count, sorted.Count); i++)
                list.Add((sorted[i].id, Vector2.Distance(Geo.XZ(sorted[i].position), p) * costPerMetre));
            return list;
        }

        /// <summary>The vehicles' route from `from` (through one of the start nodes), around parked aircraft if
        /// there's a way: from a start it can reach clear of them if there is one, else (boxed in where it is) from
        /// any; else straight through: they'll wait.</summary>
        List<int> FindRoute(TaxiwayNetwork net, List<(int node, float cost)> starts, ICollection<int> targets, Vector2 from)
        {
            if (starts.Count == 0 || targets.Count == 0) return new List<int>();
            var blocked = depot.Airport.Maintenance.RouteBlocked(type);
            List<int> route = null;
            if (blocked != null)
            {
                var clear = starts.FindAll(x => !blocked(from, net.At(x.node)));
                if (clear.Count > 0) route = net.VehicleRoute(clear, targets, blocked);
                route ??= net.VehicleRoute(starts, targets, blocked);
            }
            return route ?? net.VehicleRoute(starts, targets, null) ?? new List<int>();
        }

        /// <summary>Across country (off the taxiways) vehicles keep this much further from parked aircraft (metres).</summary>
        const float AcrossCountryClear = 10f;

        /// <summary>How far right of the centreline vehicles keep at most (metres): on roads, and on taxiways and
        /// runways (far enough to pass a narrow-body taxiing the other way, clear of its fuselage).</summary>
        internal const float RoadKeepRight = 3.6f, MaxKeepRight = 6f;

        /// <summary>How far right of the centreline of pavement this wide it keeps (at most `cap`).</summary>
        internal float KeepRight(float width, float cap = MaxKeepRight) => Mathf.Clamp(width * 0.5f - type.Width * 0.5f - 0.4f, 0f, cap);

        /// <summary>Route nodes as points, each stretch pushed to the right of the way it's driven (keeping right,
        /// so oncoming vehicles can pass where it's wide enough). `from` is an extra first point (on an edge).</summary>
        List<Vector2> Points(TaxiwayNetwork net, List<int> nodes, Vector2? from)
        {
            var pts = new List<Vector2>();
            var keep = new List<float>();
            if (from.HasValue) pts.Add(from.Value);
            foreach (int id in nodes) pts.Add(net.At(id));
            if (pts.Count < 2) return pts;
            int offset = from.HasValue ? 1 : 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int ia = i - offset, ib = i + 1 - offset;
                float w = 0f, cap = MaxKeepRight;
                if (ia >= 0)
                {
                    int a = nodes[ia], b = nodes[ib];
                    var e = net.edges.Find(x => (x.a == a && x.b == b) || (x.a == b && x.b == a));
                    if (e != null)
                    {
                        w = e.width;
                        if (e.IsRoad) cap = RoadKeepRight;
                    }
                    else
                    {
                        var r = net.Node(a).runway;
                        if (r != null && r == net.Node(b).runway) w = r.width;
                    }
                }
                else if (net.TryNearestEdge(Geo.X0Z(pts[0]), 2f, out var e0, out _))
                {
                    w = e0.width;
                    if (e0.IsRoad) cap = RoadKeepRight;
                }
                keep.Add(KeepRight(w, cap));
            }
            var o = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                if (i == 0 || i == pts.Count - 1)
                {
                    int s = i == 0 ? 0 : i - 1;
                    var d = (pts[s + 1] - pts[s]).normalized;
                    o.Add(pts[i] + Geo.Right(d) * keep[s]);
                    continue;
                }
                var d0 = (pts[i] - pts[i - 1]).normalized;
                var d1 = (pts[i + 1] - pts[i]).normalized;
                var a0 = pts[i - 1] + Geo.Right(d0) * keep[i - 1];
                var a1 = pts[i] + Geo.Right(d1) * keep[i];
                if (Vector2.Angle(d0, d1) < 120f && Geo.Lines(a0, d0, a1, d1, out float t, out _) &&
                    Vector2.Distance(a0 + d0 * t, pts[i]) < 4f * Mathf.Max(keep[i - 1], keep[i]) + 0.5f)
                    o.Add(a0 + d0 * t);
                else o.Add(pts[i] + Geo.Right((d0 + d1).normalized) * Mathf.Min(keep[i - 1], keep[i]));
            }
            return o;
        }

        /// <summary>Drive on from the end of the path along route points: turn (or turn round) onto the first,
        /// then round the corners.</summary>
        void Follow(List<Vector2> route)
        {
            if (route.Count == 0) return;
            var p0 = route[0];
            var dir = route.Count > 1 ? route[1] - p0 : p0 - path.End;
            if (dir.sqrMagnitude < 1e-4f) dir = path.EndFacing;
            dir.Normalize();
            bool onIt = Vector2.Distance(path.End, p0) < 2f && Vector2.Dot(path.EndFacing, dir) > 0.7f && !path.EndReversing;
            if (!onIt) Join(new Pose(p0, dir), false, PavementFor(false));
            if (route.Count > 1) path.Through(route.GetRange(1, route.Count - 1), Mathf.Max(type.turnRadius * 1.5f, 15f), type.transitSpeed);
        }

        /// <summary>From the end of the path to a pose: a turn, or a turn with reversing in it (one vehicle at a
        /// time: a zone the followers wait to enter).</summary>
        int Join(Pose goal, bool endReverse, Pavement pavement, float speed = TurnSpeed)
        {
            float before = path.Length;
            var via = VehicleManeuver.AroundNodes(Net, path.End, 50f);
            via.AddRange(VehicleManeuver.AroundNodes(Net, goal.p, 50f, 2));
            int cusps = VehicleManeuver.Append(path, goal, endReverse, via, type, pavement, Mathf.Min(type.transitSpeed, speed), ReverseSpeed);
            if (cusps > 0) zones.Add((before, path.Length));
            return cusps;
        }

        // ------------------------------------------------------------------ the depot

        /// <summary>Start from the depot: each vehicle's way out of its bay to where they fall into line (a little way
        /// along the road in front, out of the yard), and the route from there on to one of the target nodes.</summary>
        List<Vector2> LeaveDepot(TaxiwayNetwork net, ICollection<int> targets)
        {
            path.Clear();
            Pose merge;
            List<Vector2> route;
            if (depot.TryAccess(net, out var q, out int na, out int nb))
            {
                var starts = new List<(int, float)> { (na, Vector2.Distance(q, net.At(na))) };
                if (nb >= 0) starts.Add((nb, Vector2.Distance(q, net.At(nb))));
                var nodes = FindRoute(net, starts, targets, q);
                route = Points(net, nodes, q);
                float ahead = Mathf.Max(0f, 32f - Vector2.Dot(q - depot.Front, depot.Outward));
                var at = q;
                var dir = depot.Outward;
                int next = 1;
                while (next < route.Count)
                {
                    var seg = route[next] - at;
                    float len = seg.magnitude;
                    if (len > 0.25f) dir = seg / len;
                    if (len > ahead + 8f)
                    {
                        at += dir * ahead;
                        break;
                    }
                    ahead = Mathf.Max(0f, ahead - len);
                    at = route[next++];
                }
                merge = new Pose(at, dir);
                route.RemoveRange(0, Mathf.Min(next, route.Count));
            }
            else
            {
                var p = depot.Front + depot.Outward * 30f;
                route = Points(net, FindRoute(net, Nearest(net, p, 4, 1.5f), targets, p), null);
                merge = new Pose(p, route.Count > 0 ? route[0] - p : depot.Outward);
            }
            path.Start(merge.p, type.transitSpeed);
            path.LineTo(merge.p + merge.facing, type.transitSpeed);
            PlanDepartures(merge);
            for (int i = 0; i < size; i++)
            {
                pos[i] = Start(i);
                vel[i] = 0f;
            }
            return route;
        }

        /// <summary>Each vehicle's way from its bay: straight out of the door, then a turn onto the road.</summary>
        void PlanDepartures(Pose merge)
        {
            departures = new VehiclePath[size];
            var via = VehicleManeuver.AroundNodes(Net, merge.p, 60f, 2);
            via.AddRange(Along(merge));
            for (int i = 0; i < size; i++)
            {
                int bay = firstBay + i;
                var p = new VehiclePath();
                p.Start(depot.Bay(bay, type), YardSpeed);
                p.LineTo(depot.Threshold(bay, type) + depot.Outward * 2f, YardSpeed);
                VehicleManeuver.Append(p, merge, false, via, type, PavementFor(false), TurnSpeed, ReverseSpeed);
                departures[i] = p;
            }
        }

        /// <summary>From the end of the path back to the depot's road, then each vehicle's own way into its bay:
        /// past the door and back in.</summary>
        void PlanHome()
        {
            var net = Net;
            Pose home;
            if (depot.TryAccess(net, out var q, out int na, out int nb))
            {
                // to whichever end of the depot's road comes first, but not one with a parked aircraft between it
                // and the depot
                var targets = new HashSet<int> { na };
                if (nb >= 0) targets.Add(nb);
                var blocked = depot.Airport.Maintenance.RouteBlocked(type);
                if (blocked != null && targets.Count > 1)
                    foreach (int n in new List<int>(targets))
                        if (blocked(net.At(n), q) && targets.Count > 1) targets.Remove(n);
                var nodes = FindRoute(net, FromHere(net), targets, path.End);
                var route = Points(net, nodes, null);
                route.Add(q);
                Follow(route);
                var d = path.EndFacing;
                home = new Pose(path.End, d);
            }
            else
            {
                var p = depot.Front + depot.Outward * 30f;
                Join(new Pose(p, p - path.End), false, null, type.transitSpeed);
                home = new Pose(path.End, path.EndFacing);
            }
            arrivals = new VehiclePath[size];
            doorAt = new float[size];
            var via = VehicleManeuver.AroundNodes(net, home.p, 60f, 2);
            via.AddRange(Along(home));
            for (int i = 0; i < size; i++)
            {
                int bay = firstBay + i;
                var p = new VehiclePath();
                p.Start(home.p - home.facing * 0.5f, YardSpeed);
                p.LineTo(home.p, YardSpeed);
                var threshold = depot.Threshold(bay, type);
                VehicleManeuver.Append(p, new Pose(threshold, depot.Outward), true, via, type, PavementFor(false), YardSpeed * 1.5f, ReverseSpeed);
                doorAt[i] = p.Length;
                if (depot.Inside(bay)) p.LineTo(depot.Bay(bay, type), YardSpeed * 0.5f, reverse: true);
                arrivals[i] = p;
            }
        }

        /// <summary>Poses along the road through a point, both ways and facing both ways, for turning in the yard.</summary>
        IEnumerable<Pose> Along(Pose at)
        {
            foreach (float d in new[] { -30f, -18f, -9f, 9f, 18f, 30f })
            {
                var p = at.p + at.facing * d;
                yield return new Pose(p, at.facing);
                yield return new Pose(p, -at.facing);
            }
            var side = depot.Outward;
            foreach (float d in new[] { 10f, 18f })
            {
                yield return new Pose(depot.Front + side * (d + 12f), side);
                yield return new Pose(depot.Front + side * (d + 12f), -side);
            }
        }

        /// <summary>Drop the path behind the last vehicle and ahead of the lead (the old way home), so the next job
        /// is planned from here and a long session doesn't keep the whole history.</summary>
        void Rebase()
        {
            float from = Mathf.Max(0f, Min(pos) - 5f);
            path.Keep(from, Mathf.Max(pos[0], from));
            for (int i = 0; i < size; i++)
                if (pos[i] >= 0f) pos[i] -= from;
            // everyone is on the shared path by now: the ways out of the depot are done with
            departures = arrivals = new VehiclePath[0];
            doorAt = new float[0];
        }

        /// <summary>A job is waiting and this one is done: drive to it instead of home.</summary>
        public bool Redirect(MaintenanceJob job)
        {
            if (!Idle || state == State.Parked) return false;
            if (Take(job)) return true;
            Rebase();
            PlanHome();
            return false;
        }

        /// <summary>Find a way round a parked aircraft in the way: plan the route again (only between jobs or on the
        /// way to one, not on the strip).</summary>
        internal void Reroute()
        {
            stuck = 0f;
            if (Min(pos) < 0f) return;
            if (Tow != null)
            {
                RerouteTow();
                return;
            }
            if (Job != null)
            {
                if (marks.Count > 0 && pos[0] >= marks[0].switchAt - 1f) return;
                var job = Job;
                job.Release(false);
                Job = null;
                if (!Take(job.Fresh()))
                {
                    Rebase();
                    PlanHome();
                    state = State.Returning;
                }
                return;
            }
            if (state == State.Returning && pos[0] < path.Length - HomeHoldBack)
            {
                Rebase();
                zones.Clear();
                PlanHome();
            }
        }

        /// <summary>Backing off from a taxiing aircraft it met head to head: where the reverse ends (timeline), or -1.</summary>
        float backOffTo = -1f;

        internal bool BackingOff => backOffTo >= 0f;
        /// <summary>Has backed off as far as it was going and stands there.</summary>
        internal bool BackedOff => backOffTo >= 0f && pos[0] >= backOffTo - 0.3f && vel[0] < 0.1f;

        /// <summary>
        /// Met a taxiing aircraft (at `at`, facing `heading`, its fuselage `halfWidth` wide each side) that waits for
        /// it, in its way: pull aside, away from the aircraft and out to `clear` metres beside its line, on the side
        /// it's on, `metres` along (backing up, or driving forward if it was backing towards it; a lone vehicle on its
        /// way to an aircraft or home, not in a stand or on a strip), wait there and then plan its way on again
        /// (EndBackOff). Again from there if the aircraft still waits. False if it can't.
        /// </summary>
        internal bool BackOff(float metres, Vector2 at, Vector2 heading, float clear)
        {
            if (size != 1 || Min(pos) < 0f || Job != null || (BackingOff && !BackedOff)) return false;
            if (Tow != null ? Tow.State != TowState.Coming || pos[0] >= enterAt - 1f : state != State.Returning) return false;
            var p = Where(0, pos[0], out var facing, out _, out _);
            // away from the aircraft: backing up if it faces it
            bool reverse = Vector2.Dot(facing, at - p) > 0f;
            var travel = reverse ? -facing : facing;
            var right = Geo.Right(heading);
            float beside = Vector2.Dot(p - at, right);
            var side = beside >= 0f ? right : -right;
            float more = Mathf.Max(0f, clear + type.Width * 0.5f - Mathf.Abs(beside));
            var to = p + travel * Mathf.Max(metres, 3f * type.turnRadius) + side * more;
            Rebase();
            zones.Clear();
            marks.Clear();
            path.TurnTo(travel, to, travel, type.turnRadius, reverse ? ReverseSpeed : YardSpeed, reverse);
            backOffTo = path.Length;
            // the way into the stand is planned again afterwards
            if (Tow != null) enterAt = hitchAt = pushTo = clearAt = float.MaxValue;
            return true;
        }

        /// <summary>Backed off and the aircraft is by: plan the way on from here.</summary>
        internal void EndBackOff()
        {
            backOffTo = -1f;
            standoff = backedFor = backedTotal = 0f;
            backedFrom = null;
            if (Tow != null)
            {
                RerouteTow();
                return;
            }
            Rebase();
            zones.Clear();
            PlanHome();
            state = State.Returning;
        }

        /// <summary>The job has gone (its runway or taxiways were removed): give it up and drive home from where the
        /// team is. Waits (false) while one of them is still on its way out of the yard.</summary>
        public bool Recall()
        {
            if (Tow != null) return RecallTow();
            if (Job == null) return true;
            if (Min(pos) < 0f) return false;
            Job.Release(false);
            Job = null;
            Rebase();
            zones.Clear();
            marks.Clear();
            PlanHome();
            state = State.Returning;
            return true;
        }

        /// <summary>Give the job back (the team is disbanded or the depot disabled).</summary>
        public void Abandon()
        {
            Job?.Release(false);
            Job = null;
            DropTow();
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>The furthest vehicle i may go now: its door, its turn at a reversing manoeuvre or into the
        /// yard, the one before it leaving.</summary>
        float Hold(int i)
        {
            float h = float.MaxValue;
            float L = path.Length;
            int bay = firstBay + i;
            if (Ld(i) > 0f && pos[i] <= Start(i) + 0.01f && !depot.DoorOpen(bay)) h = Start(i);
            // reversing manoeuvres one at a time: wait a vehicle's length short of it while the one ahead is in it
            if (i > 0)
                foreach (var (z0, z1) in zones)
                {
                    float wait = z0 - type.Length - ZoneClearance;
                    if (pos[i] < z0 && pos[i - 1] > wait && pos[i - 1] < z1 + type.Length + ZoneClearance)
                        h = Mathf.Min(h, Mathf.Max(wait, pos[i]));
                }
            if (Tow != null) h = Mathf.Min(h, TowHold());
            if (backOffTo >= 0f) h = Mathf.Min(h, backOffTo);
            if (Job == null && arrivals.Length > 0)
            {
                if (i > 0 && !Home(i - 1)) h = Mathf.Min(h, Mathf.Max(L - HomeHoldBack, pos[i]));
                if (i < doorAt.Length && depot.Inside(bay) && pos[i] < L + doorAt[i] && !depot.DoorOpen(bay))
                    h = Mathf.Min(h, Mathf.Max(L + doorAt[i] - 0.5f, pos[i]));
            }
            return h;
        }

        /// <summary>Someone has fallen behind the formation (waiting at a door or a reversing turn).</summary>
        bool FormationBroken()
        {
            for (int i = 1; i < size; i++)
                if (pos[i] < pos[0] - i * Gap - 1f) return true;
            return false;
        }

        /// <summary>Advance the team by dt game seconds (AirportMaintenance steps every team together, after
        /// setting the caps). Returns true when it has just finished its job.</summary>
        public bool Step(float dt)
        {
            if (state == State.Parked || path.Count < 2) return false;
            // fallen behind: the lead slows down for the others to catch up, to a crawl near the strip
            float regroup = Job != null && pos[0] >= 0f && FormationBroken()
                ? (marks.Count > 0 && pos[0] < marks[0].switchAt - 120f ? type.transitSpeed * 0.6f : RegroupSpeed)
                : float.PositiveInfinity;
            for (int i = 0; i < size; i++)
            {
                float d = pos[i];
                float hold = Hold(i);
                float braking = vel[i] * vel[i] / (2f * Decel) + 2f;
                float target = Mathf.Min(Limit(i, d), Limit(i, d + braking));
                if (i == 0)
                {
                    if (regroup < float.PositiveInfinity) target = Mathf.Min(target, regroup);
                    else
                        for (int j = 1; j < size; j++)
                            target = Mathf.Min(target, Mathf.Min(Limit(j, pos[j]), Limit(j, pos[j] + braking)));
                }
                target = Mathf.Min(target, cap[i]);
                float end = End(i);
                target = Mathf.Min(target, Mathf.Sqrt(2f * Decel * Mathf.Max(end - d, 0f)) + 0.3f);
                if (hold < float.MaxValue) target = Mathf.Min(target, Mathf.Sqrt(2f * Decel * Mathf.Max(hold - d - 0.3f, 0f)));
                // a gap behind the one ahead, also leaving the depot (their own ways out all lead to the merge
                // point, so they get there a gap apart); not coming home, where each has its own way in
                float follow = i > 0 && pos[i - 1] <= path.Length ? pos[i - 1] - Gap : float.MaxValue;
                if (follow < float.MaxValue && d > follow - 10f) target = Mathf.Min(target, Mathf.Max(0f, vel[i - 1] + 0.6f * (follow - d)));
                float v = vel[i];
                v = v < target ? Mathf.Min(target, v + Accel * dt) : Mathf.Max(target, v - Decel * dt);
                float next = d + v * dt;
                float limit = Mathf.Min(Mathf.Min(hold, end), follow);
                if (next > limit)
                {
                    next = Mathf.Max(limit, d);
                    v = limit == follow && follow < float.MaxValue ? Mathf.Min(v, vel[i - 1]) : 0f;
                }
                // in formation: exactly a gap behind
                if (i > 0 && follow < float.MaxValue && hold >= follow && Mathf.Abs(next - follow) < 0.25f)
                {
                    next = follow;
                    v = vel[i - 1];
                }
                pos[i] = next;
                vel[i] = v;
            }
            WantDoors();

            bool finished = Tow != null && StepTow(dt);
            if (Job != null)
            {
                UpdateWork();
                if (pos[size - 1] >= finishAt)
                {
                    Job.Release(true);
                    Job = null;
                    state = State.Returning;
                    finished = true;
                }
            }
            if (Job == null && Tow == null && arrivals.Length > 0)
            {
                bool home = true;
                for (int i = 0; i < size; i++) home &= Home(i);
                if (home)
                {
                    state = State.Parked;
                    for (int i = 0; i < size; i++) vel[i] = 0f;
                    path.Clear();
                    departures = arrivals = new VehiclePath[0];
                    zones.Clear();
                    marks.Clear();
                }
            }
            return finished;
        }

        /// <summary>Doors go up for a vehicle about to leave (its turn has come) or coming home, and down behind it.</summary>
        void WantDoors()
        {
            float L = path.Length;
            for (int i = 0; i < size; i++)
            {
                int bay = firstBay + i;
                if (!depot.Inside(bay)) continue;
                bool leaving = Ld(i) > 0f && pos[i] < Start(i) + Mathf.Max(Ld(i) * 0.4f, 25f) && (i == 0 || pos[i - 1] > -15f || pos[i] > Start(i));
                bool arriving = Job == null && i < doorAt.Length && pos[i] > L - 5f && pos[i] < End(i) - 0.05f;
                if (leaving || arriving) depot.WantDoor(bay);
            }
        }

        void UpdateWork()
        {
            if (marks.Count == 0) return;
            if (pos[0] < marks[0].switchAt)
            {
                state = State.Driving;
                return;
            }
            state = State.Working;
            int k = 0;
            for (int i = marks.Count - 1; i >= 0; i--)
                if (pos[0] >= marks[i].switchAt && (i == 0 || pos[size - 1] >= marks[i - 1].end + 1f))
                {
                    k = i;
                    break;
                }
            var m = marks[k];
            var w = Job.work;
            float at = pos[0] + type.Reach;
            float front;
            if (Job.IsRunway) front = w.PassStart(m.pass) + w.Direction(m.pass) * (at - m.start);
            else if (at <= m.start) front = w.PassStart(m.pass);
            else if (at >= m.end) front = w.PassEnd(m.pass);
            else front = Job.Locate(path.Sample(at));
            Job.Advance(m.pass, front);
        }

        // ------------------------------------------------------------------ visuals

        /// <summary>Place the team's vehicles (the depot calls this every frame).</summary>
        public void Place(Transform airport, float dt)
        {
            for (int i = 0; i < vehicles.Count && i < size; i++)
            {
                var v = vehicles[i];
                if (v == null) continue;
                var p = Where(i, pos[i], out var facing, out float curvature, out bool reversing);
                bool parked = InBay(i);
                float gear = reversing ? -1f : 1f;
                float steer = parked ? 0f : -Mathf.Atan(curvature * type.wheelbase * gear) * Mathf.Rad2Deg;
                v.Place(airport.TransformPoint(Geo.X0Z(p)), airport.TransformDirection(Geo.X0Z(facing)),
                    parked ? 0f : vel[i] * gear, steer, dt);
                if (type.IsTug)
                {
                    v.SetLift(Tow != null ? Tow.Lift : 0f);
                    v.SetState(!parked, Tow != null && Tow.State >= TowState.Lifting && Tow.State <= TowState.Lowering, 0f);
                    continue;
                }
                bool working = state == State.Working && Job != null && Job.work.Pass >= 0 && WorkingAt(pos[i]);
                float amount = !working ? 0f : type.Work == WorkKind.Plough ? Mathf.Clamp01(Job.Snow / 3f) + 0.15f : 0.8f;
                v.SetState(!parked, working, amount);
            }
        }

        bool WorkingAt(float d)
        {
            foreach (var m in marks)
                if (d >= m.start - 2f && d <= m.end) return true;
            return false;
        }

        // ------------------------------------------------------------------ for tests and tools

        /// <summary>The poses vehicle i will take between two timeline distances (default: its whole way out and
        /// back), every `step` metres (tests check them against the pavement).</summary>
        public List<(Vector2 p, Vector2 facing)> PlannedPoses(int i, float step, float from = float.MinValue, float to = float.MaxValue)
        {
            var list = new List<(Vector2, Vector2)>();
            if (state == State.Parked) return list;
            for (float d = Mathf.Max(Start(i), from); d <= Mathf.Min(End(i), to); d += step) list.Add((Where(i, d, out var f), f));
            return list;
        }

        /// <summary>Positions, speeds and what holds each vehicle back (for debugging).</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder($"{name} {state} path {path.Length:0} m, {zones.Count} zones {string.Join(",", zones.ConvertAll(z => $"{z.from:0}-{z.to:0}"))}");
            for (int i = 0; i < size; i++)
                sb.Append($" | {i}: at {pos[i]:0.0} ({Start(i):0}..{End(i):0}) v {vel[i]:0.0} hold {Hold(i):0.0} cap {cap[i]:0.0}" +
                          $"{(blockedBy[i] != null ? " for " + blockedBy[i] : "")} door {(depot.Inside(firstBay + i) ? depot.DoorOpenAmount(firstBay + i).ToString("0.00") : "-")}");
            return sb.ToString();
        }

        /// <summary>Where vehicle i is now (rear axle, Airport space).</summary>
        public Vector2 PositionOf(int i) => Where(i, pos[i], out _);
        public Vector2 FacingOf(int i)
        {
            Where(i, pos[i], out var f);
            return f;
        }
        public float SpeedOf(int i) => vel[i];
        /// <summary>The lead is backing up (a turn with reversing in it, or into its bay).</summary>
        public bool Reversing0
        {
            get
            {
                Where(0, pos[0], out _, out _, out bool r);
                return r && vel[0] > 0.3f;
            }
        }
        /// <summary>Vehicle i is standing in its bay (home, or waiting for its turn to leave).</summary>
        public bool ParkedInBay(int i) => InBay(i);

        /// <summary>The stretches of the shared path driven during the passes and the turns between them.</summary>
        public (float from, float to) WorkSpan => marks.Count == 0 ? (0f, 0f) : (marks[0].start, marks[marks.Count - 1].end);
        public int ReversingTurns => zones.Count;
    }
}
