using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// The airport's maintenance work across all its depots: one queue of jobs (ploughing, mowing grass runways,
    /// taxiways and verges), handed to the nearest free team of the right kind; with autoDispatch the weather and
    /// the grass fill the queue by themselves. Also its push-backs, each handed to the smallest free tug that can
    /// move the aircraft (TowJob). Steps every team together in game time and keeps their vehicles clear of
    /// aircraft and of each other: each vehicle looks ahead along its path and stops short of anything in the way
    /// (a working team has right of way over one driving by, which has it over one going home), and a team held up
    /// by a parked aircraft for a while plans a way round it.
    ///
    ///   airport.Maintenance.Plough(runway);  .PloughTaxiways();  .MowVerges(runway);  .MowAll();
    ///   airport.Maintenance.PushBack(aircraft, out string problem);
    ///
    /// Taxiway and road jobs are runs (TaxiwayNetwork.Run): asking for one section queues the whole run it's on.
    /// </summary>
    public sealed class AirportMaintenance
    {
        const float Step = 0.5f, CheckEvery = 30f, Decel = 2.2f, StuckReroute = 20f;
        /// <summary>Held up by aircraft this long (game seconds), a vehicle goes under the wings of those on a flight,
        /// clear of the fuselage, to get by (two waiting for each other would otherwise wait for good).</summary>
        const float UnderWingAfter = 30f;
        /// <summary>Head to head this long with a taxiing aircraft that waits for it (game seconds), a vehicle backs off
        /// this far (metres) and waits there till it has gone by (and BackedOffWait more), BackedOffLimit at most.</summary>
        const float StandoffTime = 4f, BackOffDistance = 15f, BackedOffWait = 3f, BackedOffLimit = 180f;
        /// <summary>Pulled aside, a vehicle keeps this far from the aircraft's fuselage (metres).</summary>
        const float AsideClearance = 3f;

        readonly Airport airport;
        readonly List<MaintenanceJob> queue = new List<MaintenanceJob>();
        readonly List<TowJob> tows = new List<TowJob>();
        float checkTimer;

        public AirportMaintenance(Airport airport) => this.airport = airport;

        public IReadOnlyList<MaintenanceJob> Queue => queue;
        /// <summary>The push-backs asked for and not finished: waiting for a tug, or under way.</summary>
        public IReadOnlyList<TowJob> Tows => tows;

        IEnumerable<VehicleTeam> Teams()
        {
            foreach (var d in airport.Depots)
                foreach (var t in d.Teams)
                    yield return t;
        }

        // ------------------------------------------------------------------ jobs

        public bool Plough(Runway r) => r != null && r.snow > 0.05f && Enqueue(new MaintenanceJob(WorkKind.Plough, r));
        public bool Mow(Runway r) => r != null && r.surface == PavementSurface.Grass && Enqueue(new MaintenanceJob(WorkKind.Mow, r));
        public bool MowVerges(Runway r) => r != null && Enqueue(new MaintenanceJob(WorkKind.Mow, r, true));

        /// <summary>Plough the run this taxiway (or road) section is on: its snowy sections end to end.</summary>
        public bool Plough(TaxiwayEdge e) =>
            e != null && e.snow > 0.05f && Enqueue(RunJob(WorkKind.Plough, e, x => x.snow > 0.05f, false));

        public bool Mow(TaxiwayEdge e) =>
            e != null && e.surface == PavementSurface.Grass &&
            Enqueue(RunJob(WorkKind.Mow, e, x => x.surface == PavementSurface.Grass && x.grass > RunwayRules.CutGrass + 0.5f, false));

        public bool MowVerges(TaxiwayEdge e) =>
            e != null && e.HasVerges && Enqueue(RunJob(WorkKind.Mow, e, x => x.HasVerges && x.vergeGrass > RunwayRules.CutGrass + 0.5f, true));

        public void PloughTaxiways()
        {
            foreach (var e in airport.Taxiways.edges.ToArray()) Plough(e);
        }

        /// <summary>Every runway, taxiway and road with snow on it.</summary>
        public void PloughAll()
        {
            foreach (var r in airport.Runways) Plough(r);
            PloughTaxiways();
        }

        /// <summary>Every grass runway and taxiway, and every verge that has grown.</summary>
        public void MowAll()
        {
            foreach (var r in airport.Runways)
            {
                Mow(r);
                if (r.vergeGrass > RunwayRules.CutGrass + 0.5f) MowVerges(r);
            }
            foreach (var e in airport.Taxiways.edges.ToArray())
            {
                Mow(e);
                if (e.vergeGrass > RunwayRules.CutGrass + 0.5f) MowVerges(e);
            }
        }

        MaintenanceJob RunJob(WorkKind kind, TaxiwayEdge seed, Func<TaxiwayEdge, bool> include, bool verge)
        {
            var net = airport.Taxiways;
            if (!net.edges.Contains(seed) || Covered(seed, kind, verge)) return null;
            return new MaintenanceJob(kind, net, net.Run(seed, x => include(x) && !Covered(x, kind, verge)), verge);
        }

        /// <summary>Is this section already in a queued or assigned job of the kind?</summary>
        bool Covered(TaxiwayEdge e, WorkKind kind, bool verge)
        {
            foreach (var j in queue)
                if (j.kind == kind && j.verge == verge && j.Contains(e)) return true;
            foreach (var t in Teams())
                if (t.Job != null && t.Job.kind == kind && t.Job.verge == verge && t.Job.Contains(e)) return true;
            return false;
        }

        bool Enqueue(MaintenanceJob job)
        {
            if (job == null || !job.Valid) return false;
            if (queue.Exists(j => j.Overlaps(job))) return false;
            foreach (var t in Teams())
                if (t.Job != null && t.Job.Overlaps(job)) return false;
            queue.Add(job);
            return true;
        }

        /// <summary>A team gave a job up (its fleet changed): queue it again, first.</summary>
        internal void Requeue(MaintenanceJob job)
        {
            if (job == null || queue.Exists(j => j.Overlaps(job))) return;
            queue.Insert(0, job.Fresh());
        }

        /// <summary>Queue what the weather and the grass call for (autoDispatch).</summary>
        void CheckConditions()
        {
            bool snowing = airport.Condition == SurfaceCondition.Snow;
            bool lying = airport.groundSnow > 0.05f;
            foreach (var r in airport.Runways)
            {
                if (RunwayRules.NeedsPloughing(r.surface, r.snow)) Plough(r);
                if (RunwayRules.NeedsMowing(r.surface, r.grass) && r.snow < 0.05f && !snowing) Mow(r);
                if (r.vergeGrass >= RunwayRules.LongGrass && !lying && !snowing) MowVerges(r);
            }
            foreach (var e in airport.Taxiways.edges.ToArray())
            {
                if (RunwayRules.NeedsPloughing(e.surface, e.snow)) Plough(e);
                if (RunwayRules.NeedsMowing(e.surface, e.grass) && e.snow < 0.05f && !snowing) Mow(e);
                if (e.HasVerges && e.vergeGrass >= RunwayRules.LongGrass && !lying && !snowing) MowVerges(e);
            }
        }

        /// <summary>What goes first: ploughing runways, then taxiways and roads; mowing grass runways, grass
        /// taxiways, then verges.</summary>
        static int Rank(MaintenanceJob j) => j.kind == WorkKind.Plough ? (j.IsRunway ? 0 : 1) : j.verge ? (j.IsRunway ? 4 : 5) : (j.IsRunway ? 2 : 3);

        /// <summary>Hand queued jobs to free teams: by rank (longest runway first), each to the nearest free team of
        /// its kind, from whichever depot.</summary>
        void Dispatch()
        {
            queue.RemoveAll(j => !j.Valid || !j.Needed);
            if (queue.Count == 0) return;
            var idle = new List<VehicleTeam>();
            foreach (var t in Teams())
                if (t.Idle) idle.Add(t);
            if (idle.Count == 0) return;
            var order = new List<(MaintenanceJob job, float key)>();
            foreach (var j in queue)
                order.Add((j, Rank(j) * 1e7f + (j.IsRunway ? -j.Length : Nearest(j, idle, out _))));
            order.Sort((a, b) => a.key.CompareTo(b.key));
            foreach (var (j, _) in order)
            {
                if (j.Taken) continue;
                Nearest(j, idle, out var team);
                if (team == null) continue;
                queue.Remove(j);
                idle.Remove(team);
                bool ok = team.state == VehicleTeam.State.Parked ? team.Take(j) : team.Redirect(j);
                if (!ok) Debug.LogWarning($"{team.name}: can't take {j}");
                if (idle.Count == 0) break;
            }
        }

        static float Nearest(MaintenanceJob j, List<VehicleTeam> teams, out VehicleTeam best)
        {
            best = null;
            float bestD = float.MaxValue;
            var mid = j.At(j.Length * 0.5f, 0f);
            foreach (var t in teams)
            {
                if (t.type.IsTug || t.type.Work != j.kind) continue;
                float d = Vector2.Distance(t.LeadPosition, mid);
                if (d < bestD)
                {
                    bestD = d;
                    best = t;
                }
            }
            return bestD;
        }

        // ------------------------------------------------------------------ push-backs

        /// <summary>The push-back waiting or under way for an aircraft, or null.</summary>
        public TowJob TowOf(Aircraft.Aircraft aircraft)
        {
            foreach (var j in tows)
                if (j.aircraft == aircraft && j.State < TowState.Done) return j;
            return null;
        }

        /// <summary>
        /// Push an aircraft back off its stand: the smallest free tug that can move it comes from its depot, picks up
        /// the nose wheel, pushes it round onto the taxiway facing the way it will taxi (otherWay: the other way) and
        /// goes home. Null, with the reason, if it can't be done: not parked nose-in on a push-back stand, no tug
        /// for its size, no room, or another aircraft in the way.
        /// </summary>
        public TowJob PushBack(Aircraft.Aircraft aircraft, out string problem, bool otherWay = false)
        {
            problem = null;
            if (aircraft == null || aircraft.type == null)
            {
                problem = "no aircraft";
                return null;
            }
            if (TowOf(aircraft) != null)
            {
                problem = "it's being pushed back already";
                return null;
            }
            bool any = false, fits = false;
            float lightest = float.MaxValue;
            foreach (var t in Teams())
            {
                if (!t.type.IsTug) continue;
                any = true;
                fits |= t.type.CanTow(aircraft.type);
                lightest = Mathf.Min(lightest, t.type.minTowMass);
            }
            if (!fits)
            {
                problem = !any ? "the depots have no tugs"
                    : aircraft.type.maxTakeoffMass < lightest ? "light aircraft are pushed by hand, not by a tug"
                    : "no tug here is big enough for it";
                return null;
            }
            var tr = airport.transform;
            var at = tr.InverseTransformPoint(aircraft.transform.position);
            var f = tr.InverseTransformDirection(aircraft.transform.forward);
            var path = PushBackPath.Plan(airport.Taxiways, aircraft.type, new Vector2(at.x, at.z), new Vector2(f.x, f.z), otherWay,
                out problem);
            if (path == null) return null;
            // its way must be clear of the other aircraft, and of where the ones being pushed back will stand (by their
            // outlines: rectangles round the wings would clash where the real aircraft pass)
            var others = new List<(Aircraft.Aircraft who, List<Box> shape)>();
            foreach (var a in Aircraft.Aircraft.All)
            {
                if (a == null || a == aircraft || a.type == null) continue;
                var p = tr.InverseTransformPoint(a.transform.position);
                var d = tr.InverseTransformDirection(a.transform.forward);
                others.Add((a, Outline(new Vector2(p.x, p.z), new Vector2(d.x, d.z).normalized, a.type, 0.5f)));
            }
            foreach (var j in tows)
                if (j.Valid && j.State < TowState.Done)
                    others.Add((j.aircraft, Outline(j.path.End, j.path.endFacing, j.path.type, 0.5f)));
            for (float d = 0f; d <= path.Length + 1f; d += 1.5f)
            {
                path.Sample(d, out var p, out var heading, out _, out _);
                foreach (var me in Outline(p, heading, aircraft.type, 0f))
                    foreach (var (who, shape) in others)
                        foreach (var o in shape)
                            if (me.Overlaps(o))
                            {
                                problem = $"{(who != null ? who.name : "another aircraft")} is in the way";
                                return null;
                            }
            }
            var job = new TowJob(aircraft, path);
            tows.Add(job);
            DispatchTows();
            return job;
        }

        /// <summary>Hand waiting push-backs to free tugs: the smallest that can move the aircraft, then the nearest.</summary>
        void DispatchTows()
        {
            tows.RemoveAll(j => j.State == TowState.Done || (!j.Valid && j.Tug == null));
            foreach (var j in tows)
            {
                if (j.Tug != null || j.State != TowState.Waiting) continue;
                VehicleTeam best = null;
                float bestKey = float.MaxValue;
                foreach (var t in Teams())
                {
                    if (!t.Idle || !t.type.CanTow(j.path.type)) continue;
                    float key = t.type.maxTowMass + Vector2.Distance(t.LeadPosition, j.path.start);
                    if (key < bestKey)
                    {
                        bestKey = key;
                        best = t;
                    }
                }
                if (best == null) continue;
                if (!best.TakeTow(j))
                {
                    Debug.LogWarning($"{best.name}: can't get to {j}");
                    j.State = TowState.Done;
                }
            }
        }

        /// <summary>
        /// An aircraft's outline on the ground as three boxes, from its type's size alone: the fuselage (a ninth of
        /// its length wide), the wings (the span, from 15% of the length behind the main gear to 18% ahead) and the
        /// tailplane (a third of the span, over the last 15%). Close enough to tell whether two aircraft clear each
        /// other as they pass on a taxilane, where their bounding rectangles would overlap.
        /// </summary>
        public static List<Box> Outline(Vector2 position, Vector2 facing, AircraftType type, float inflate)
        {
            var b = type.bounds;
            float length = b.size.z, span = b.size.x;
            Bounds Part(float halfWidth, float z0, float z1)
            {
                var part = new Bounds();
                part.SetMinMax(new Vector3(-halfWidth, b.min.y, Mathf.Max(z0, b.min.z)), new Vector3(halfWidth, b.max.y, Mathf.Min(z1, b.max.z)));
                return part;
            }
            return new List<Box>
            {
                Box.Of(position, facing, Part(Mathf.Max(length / 18f, 0.6f), b.min.z, b.max.z), inflate),
                Box.Of(position, facing, Part(span * 0.5f, -0.15f * length, 0.18f * length), inflate),
                Box.Of(position, facing, Part(span / 6f, b.min.z, b.min.z + 0.15f * length), inflate),
            };
        }

        /// <summary>An aircraft's Outline where it stands, grown by `inflate`.</summary>
        List<Box> OutlineOf(Aircraft.Aircraft a, float inflate)
        {
            var t = airport.transform;
            var p = t.InverseTransformPoint(a.transform.position);
            var f = t.InverseTransformDirection(a.transform.forward);
            return Outline(new Vector2(p.x, p.z), new Vector2(f.x, f.z).normalized, a.type, inflate);
        }

        /// <summary>An aircraft's fuselage on the ground (the first box of its Outline), grown by `inflate`.</summary>
        Box Fuselage(Aircraft.Aircraft a, float inflate)
        {
            var t = airport.transform;
            var p = t.InverseTransformPoint(a.transform.position);
            var f = t.InverseTransformDirection(a.transform.forward);
            var box = Outline(new Vector2(p.x, p.z), new Vector2(f.x, f.z).normalized, a.type, inflate)[0];
            box.what = "aircraft";
            box.aircraft = a;
            return box;
        }

        /// <summary>The parked aircraft's footprints (not grown), but for one.</summary>
        internal List<Box> ParkedAircraft(Aircraft.Aircraft except)
        {
            var list = AircraftBoxes(true, 0f);
            list.RemoveAll(b => b.aircraft == except);
            return list;
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>Advance every depot's teams and doors (game seconds); the Airport calls this from Simulate.</summary>
        public void Simulate(float seconds)
        {
            var depots = airport.Depots;
            foreach (var d in depots) d.EnsureTeams();
            // the runway or taxiways a team was on its way to (or working) have been removed: it comes home
            foreach (var d in depots)
                foreach (var t in d.Teams)
                    if ((t.Job != null && !t.Job.Valid) || (t.Tow != null && !t.Tow.Valid)) t.Recall();
            bool auto = false;
            foreach (var d in depots) auto |= d.autoDispatch;
            if (auto)
            {
                checkTimer -= seconds;
                if (checkTimer <= 0f)
                {
                    checkTimer = CheckEvery;
                    CheckConditions();
                }
            }
            Dispatch();
            DispatchTows();
            while (seconds > 1e-4f)
            {
                bool active = false;
                foreach (var d in depots) active |= d.Busy;
                if (!active) break;
                float dt = Mathf.Min(seconds, Step);
                seconds -= dt;
                Avoid(depots, dt);
                bool finished = false;
                foreach (var d in depots)
                    foreach (var t in d.Teams)
                        if (t.Step(dt)) finished = true;
                foreach (var d in depots) d.StepDoors(dt);
                if (finished)
                {
                    Dispatch();
                    DispatchTows();
                }
            }
        }

        // ------------------------------------------------------------------ keeping clear

        /// <summary>A footprint on the ground: an oriented rectangle (Airport space).</summary>
        public struct Box
        {
            public Vector2 c, ax, az;
            public float hx, hz;
            public VehicleTeam team;
            public int index;
            public string what;
            public bool parkedAircraft;
            public Aircraft.Aircraft aircraft;
            /// <summary>Ground a vehicle will cover turning round with reversing (not where it is now).</summary>
            public bool claim;

            public float Radius => Mathf.Sqrt(hx * hx + hz * hz);

            public static Box Of(Vector2 p, Vector2 facing, Bounds b, float inflate)
            {
                var ax = Geo.Right(facing);
                return new Box
                {
                    c = p + ax * b.center.x + facing * b.center.z, ax = ax, az = facing,
                    hx = b.extents.x + inflate, hz = b.extents.z + inflate,
                };
            }

            public bool Contains(Vector2 p)
            {
                var d = p - c;
                return Mathf.Abs(Vector2.Dot(d, ax)) <= hx && Mathf.Abs(Vector2.Dot(d, az)) <= hz;
            }

            public bool Overlaps(in Box o)
            {
                var d = o.c - c;
                if ((Radius + o.Radius) * (Radius + o.Radius) < d.sqrMagnitude) return false;
                foreach (var axis in new[] { ax, az, o.ax, o.az })
                {
                    float ra = hx * Mathf.Abs(Vector2.Dot(ax, axis)) + hz * Mathf.Abs(Vector2.Dot(az, axis));
                    float rb = o.hx * Mathf.Abs(Vector2.Dot(o.ax, axis)) + o.hz * Mathf.Abs(Vector2.Dot(o.az, axis));
                    if (Mathf.Abs(Vector2.Dot(d, axis)) > ra + rb) return false;
                }
                return true;
            }
        }

        /// <summary>How far vehicles keep from aircraft, and from each other. Tugs work round aircraft and keep only
        /// TugClearance from them.</summary>
        const float AircraftClearance = 2.5f, VehicleClearance = 0.3f, TugClearance = 0.3f;
        /// <summary>How far ahead of a turn with reversing in it a vehicle claims the ground it will cover.</summary>
        const float ClaimAhead = 40f;

        /// <summary>Every aircraft's footprint, in the Airport's space, grown by `inflate`.</summary>
        List<Box> AircraftBoxes(bool parkedOnly, float inflate = AircraftClearance)
        {
            var list = new List<Box>();
            var t = airport.transform;
            foreach (var a in Aircraft.Aircraft.All)
            {
                if (a == null || a.type == null) continue;
                bool parked = !a.Moving;
                if (t.InverseTransformPoint(a.transform.position).y > 3f) continue;   // in the air
                if (parkedOnly && !parked) continue;
                var at = t.InverseTransformPoint(a.transform.position);
                var f = t.InverseTransformDirection(a.transform.forward);
                var facing = new Vector2(f.x, f.z);
                if (facing.sqrMagnitude < 1e-6f) continue;
                var box = Box.Of(new Vector2(at.x, at.z), facing.normalized, a.type.bounds, inflate);
                box.what = "aircraft";
                box.parkedAircraft = parked;
                box.aircraft = a;
                list.Add(box);
            }
            return list;
        }

        /// <summary>For route planning: is the stretch a to b blocked by a parked aircraft, or where one being pushed
        /// back will stand (for vehicles of this type, keeping `extra` metres more clear)? Null when there are none.
        /// From inside an aircraft's clearance (stopped beside it), driving away from it is not blocked.</summary>
        public Func<Vector2, Vector2, bool> RouteBlocked(VehicleType type, float extra = 0f)
        {
            var boxes = AircraftBoxes(true);
            // where the aircraft being pushed back will stand
            foreach (var j in tows)
                if (j.Valid && j.State < TowState.Done)
                    boxes.Add(Box.Of(j.path.End, j.path.endFacing, j.path.type.bounds, AircraftClearance));
            if (boxes.Count == 0) return null;
            // the vehicle's half width, and as far as it keeps right of the line between the nodes (VehicleTeam.Points)
            float grow = type.Width * 0.5f + VehicleTeam.MaxKeepRight + extra;
            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                b.hx += grow;
                b.hz += grow;
                boxes[i] = b;
            }
            return (a, b) =>
            {
                float len = Vector2.Distance(a, b);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 3f));
                foreach (var box in boxes)
                {
                    if (box.Contains(a))
                    {
                        if (Vector2.Dot(b - a, box.c - a) > 0f) return true;
                        continue;
                    }
                    for (int k = 0; k <= n; k++)
                        if (box.Contains(Vector2.Lerp(a, b, k / (float)n))) return true;
                }
                return false;
            };
        }

        /// <summary>Every vehicle out of its bay, as a footprint grown by VehicleClearance (taxiing aircraft stop short
        /// of them), but tugs holding or coming for `except`.</summary>
        public List<Box> VehicleBoxes(Aircraft.Aircraft except)
        {
            var list = new List<Box>();
            foreach (var t in Teams())
            {
                if (!t.Active || (except != null && t.Ignores(except))) continue;
                for (int i = 0; i < t.size; i++)
                {
                    if (t.InBay(i)) continue;
                    var p = t.Where(i, t.Position(i), out var f);
                    var box = Box.Of(p, f, t.type.bounds, VehicleClearance);
                    box.team = t;
                    box.index = i;
                    box.what = t.type.IsTug ? "a tug" : t.type.kind == VehicleKind.Mower ? "a mower" : "a plough";
                    list.Add(box);
                }
            }
            return list;
        }

        static int Priority(VehicleTeam t) => t.state switch
        {
            VehicleTeam.State.Working => 3,
            VehicleTeam.State.Driving => 2,
            _ => 1,
        };

        /// <summary>
        /// Speed caps for every vehicle out: it looks ahead along its own path as far as it needs to stop (plus a
        /// margin), and stops short of the first aircraft or other team's vehicle its footprint would touch. Two
        /// vehicles that would each stop for the other: the one with right of way goes. A team held up by a
        /// parked aircraft for StuckReroute seconds plans a way round.
        /// </summary>
        void Avoid(List<MaintenanceDepot> depots, float dt)
        {
            var obstacles = AircraftBoxes(false);
            var tracks = airport.Traffic.Tracks();
            var claimed = new List<(Vector2, Vector2)>();
            var teams = new List<VehicleTeam>();
            foreach (var d in depots)
                foreach (var t in d.Teams)
                    if (t.Active) teams.Add(t);
            foreach (var t in teams)
                for (int i = 0; i < t.size; i++)
                {
                    t.cap[i] = float.PositiveInfinity;
                    t.blockedBy[i] = null;
                    if (t.InBay(i)) continue;
                    var p = t.Where(i, t.Position(i), out var f);
                    var box = Box.Of(p, f, t.type.bounds, VehicleClearance);
                    box.team = t;
                    box.index = i;
                    box.what = t.type.IsTug ? "a tug" : t.type.kind == VehicleKind.Mower ? "a mower" : "a plough";
                    obstacles.Add(box);
                    claimed.Clear();
                    t.Claim(i, ClaimAhead, 2f, claimed);
                    foreach (var (q, g) in claimed)
                    {
                        // grown by the sampling step, so the ends of the legs are covered
                        var c = Box.Of(q, g, t.type.bounds, VehicleClearance + 1.5f);
                        c.team = t;
                        c.index = i;
                        c.what = box.what;
                        c.claim = true;
                        obstacles.Add(c);
                    }
                }
            if (teams.Count == 0) return;

            var hits = new Dictionary<(VehicleTeam, int), Box>();
            foreach (var t in teams)
                for (int i = 0; i < t.size; i++)
                {
                    float d0 = t.Position(i), v = t.Velocity(i);
                    float look = v * v / (2f * Decel) + 6f + t.type.Length * 0.25f;
                    // taxiing aircraft's tracks, but not one it's on already (it drives on, off it) or of one that waits
                    // for it (it goes on, out of its way)
                    var now = Box.Of(t.Where(i, d0, out var f0), f0, t.type.bounds, VehicleClearance);
                    var yieldTo = new List<(Box box, Vector2 way)>();
                    if (tracks.Count > 0 && !t.InBay(i))
                        foreach (var (who, exempt, track) in tracks)
                        {
                            if (exempt == t || (t.type.IsTug && t.Ignores(who)) || airport.Traffic.WaitsFor(who, t)) continue;
                            bool on = false;
                            foreach (var (b, _) in track)
                                if ((b.c - now.c).sqrMagnitude < (b.Radius + now.Radius) * (b.Radius + now.Radius) && b.Overlaps(now))
                                {
                                    on = true;
                                    break;
                                }
                            if (!on) yieldTo.AddRange(track);
                        }
                    for (float d = d0 + 1.5f; d <= d0 + look; d += 2f)
                    {
                        var p = t.Where(i, d, out var f);
                        var me = Box.Of(p, f, t.type.bounds, VehicleClearance);
                        bool towingNow = t.TowedBox(d0, out _);
                        // a tug pushing an aircraft back looks out for the aircraft's footprint too
                        bool towing = t.TowedBox(d, out var towed);
                        bool hit = false;
                        foreach (var o in obstacles)
                        {
                            // a team keeps its own spacing on the shared path (and waits its turn at its own turns
                            // with reversing in them); in the yard each has its own way
                            if (o.team == t && (o.claim || o.index == i || (!t.InYard(i) && !t.InYard(o.index)))) continue;
                            // pushing an aircraft back it has the right of way over ground others mean to turn on
                            // (they keep off its push: AirTraffic.Tracks)
                            if (o.claim && towingNow) continue;
                            var other = o;
                            if (o.aircraft != null && t.type.IsTug && t.Ignores(o.aircraft)) continue;
                            if (o.aircraft != null && (airport.Traffic.WaitsFor(o.aircraft, t) || airport.Traffic.Holding(o.aircraft) ||
                                                       (t.heldByAircraft > UnderWingAfter && airport.Traffic.FlightOf(o.aircraft) != null)))
                            {
                                // a taxiing aircraft has stopped (for this vehicle, or at a hold), or it's been held
                                // up by flights a while (they may be waiting for it): it goes on, under the wings if it
                                // must, clear of the fuselage. Not under an aircraft moved some other way: that one
                                // won't wait for it
                                other = Fuselage(o.aircraft, TugClearance);
                            }
                            else if (o.aircraft != null && t.type.IsTug)
                            {
                                // inside the stand the parked aircraft (its way was planned round them)
                                if (o.parkedAircraft && t.InStand(d)) continue;
                                // tugs keep their clearance from the aircraft's outline, not its bounding rectangle:
                                // they pass by noses and tails
                                if (!OutlineOf(o.aircraft, TugClearance).Exists(x => x.Overlaps(me))) continue;
                                other.hx -= AircraftClearance - TugClearance;
                                other.hz -= AircraftClearance - TugClearance;
                            }
                            if (!me.Overlaps(other) && !(towing && o.aircraft == null && towed.Overlaps(o))) continue;
                            // already too close to it (an aircraft it was let pass under, one that stopped beside it):
                            // only getting closer counts
                            if (o.aircraft != null && now.Overlaps(other) && (p - other.c).sqrMagnitude >= (now.c - other.c).sqrMagnitude) continue;
                            hits[(t, i)] = o;
                            t.cap[i] = Mathf.Sqrt(2f * Decel * Mathf.Max(0f, d - d0 - 2.5f));
                            t.blockedBy[i] = o.what;
                            hit = true;
                            break;
                        }
                        if (hit) break;
                        // giving way to a taxiing aircraft: short of its track, coming the other way or across
                        foreach (var (b, way) in yieldTo)
                        {
                            if (Vector2.Dot(f, way) > 0.7f || (b.c - p).sqrMagnitude > (b.Radius + 15f) * (b.Radius + 15f) || !me.Overlaps(b)) continue;
                            t.cap[i] = Mathf.Sqrt(2f * Decel * Mathf.Max(0f, d - d0 - 2.5f));
                            t.blockedBy[i] = "a taxiing aircraft";
                            hit = true;
                            break;
                        }
                        if (hit) break;
                    }
                }
            // right of way: of two vehicles each stopping for the other, the one turning round with reversing goes (it
            // claimed the ground); else the one with priority; of two alike, the one in front (the other is behind it,
            // going the way it moves: it may be backing up), else the first team
            foreach (var kv in hits)
            {
                var (t, i) = kv.Key;
                var o = kv.Value;
                if (o.team == null) continue;
                if (!hits.TryGetValue((o.team, o.index), out var back) || back.team != t || back.index != i) continue;
                int mine = Priority(t), theirs = Priority(o.team);
                bool first;
                if (o.team == t) first = i < o.index;
                else if (o.claim != back.claim) first = back.claim;
                else if (mine != theirs) first = mine > theirs;
                else
                {
                    var p = t.Where(i, t.Position(i), out _);
                    var q = o.team.Where(o.index, o.team.Position(o.index), out _);
                    var f = (t.Where(i, t.Position(i) + 1f, out _) - p).normalized;
                    var g = (o.team.Where(o.index, o.team.Position(o.index) + 1f, out _) - q).normalized;
                    float behind = Vector2.Dot(q - p, f), ahead = Vector2.Dot(p - q, g);
                    first = Mathf.Abs(behind - ahead) > 1f ? behind < ahead : Order(t) < Order(o.team);
                }
                if (!first) continue;
                t.cap[i] = float.PositiveInfinity;
                t.blockedBy[i] = null;
            }
            // held up by a parked aircraft: find a way round
            foreach (var t in teams)
            {
                bool held = hits.TryGetValue((t, 0), out var o) && o.parkedAircraft && t.Velocity(0) < 0.1f;
                t.stuck = held ? t.stuck + dt : 0f;
                if (t.stuck > StuckReroute) t.Reroute();
                bool byAircraft = false;
                for (int i = 0; i < t.size; i++)
                    if (hits.TryGetValue((t, i), out var h) && h.aircraft != null && t.Velocity(i) < 0.1f) byAircraft = true;
                t.heldByAircraft = byAircraft ? t.heldByAircraft + dt : 0f;
                // head to head with a taxiing aircraft that waits for it (neither can get by): back off, wait, go on
                if (t.BackingOff) t.backedTotal += dt;
                if (t.BackedOff)
                {
                    if (airport.Traffic.Blocking(t))
                    {
                        t.backedFor = 0f;
                        t.standoff += dt;
                        if (t.standoff > StandoffTime && PullAside(t, t.backedFrom)) t.standoff = 0f;
                    }
                    else
                    {
                        t.backedFor = Gone(t.backedFrom, t) ? t.backedFor + dt : 0f;
                        if (t.backedFor > BackedOffWait || t.backedTotal > BackedOffLimit) t.EndBackOff();
                    }
                }
                else if (!t.BackingOff)
                {
                    bool standoff = hits.TryGetValue((t, 0), out var so) && so.aircraft != null && t.Velocity(0) < 0.1f &&
                                    airport.Traffic.WaitsFor(so.aircraft, t);
                    t.standoff = standoff ? t.standoff + dt : 0f;
                    if (t.standoff > StandoffTime && PullAside(t, so.aircraft))
                    {
                        t.standoff = 0f;
                        t.backedFrom = so.aircraft;
                    }
                }
            }
        }

        /// <summary>Pull a vehicle aside, out of a taxiing aircraft's way (VehicleTeam.BackOff).</summary>
        bool PullAside(VehicleTeam t, Aircraft.Aircraft a)
        {
            if (a == null) return false;
            var tr = airport.transform;
            var p = tr.InverseTransformPoint(a.transform.position);
            var f = tr.InverseTransformDirection(a.transform.forward);
            var fuselage = Outline(new Vector2(p.x, p.z), new Vector2(f.x, f.z).normalized, a.type, 0f)[0];
            return t.BackOff(BackOffDistance, fuselage.c, fuselage.az, fuselage.hx + AsideClearance);
        }

        /// <summary>Has the aircraft a vehicle backed off from gone by (its tail behind the vehicle) or away (80 m),
        /// or stopped taxiing?</summary>
        bool Gone(Aircraft.Aircraft a, VehicleTeam t)
        {
            if (a == null || !a.gameObject.activeInHierarchy || !airport.Traffic.Taxiing(a)) return true;
            var tr = airport.transform;
            var p = tr.InverseTransformPoint(a.transform.position);
            var at = new Vector2(p.x, p.z);
            var me = t.Where(0, t.Position(0), out var facing);
            if ((at - me).sqrMagnitude > 80f * 80f) return true;
            return Vector2.Dot(at - me, facing) < -(-a.type.bounds.min.z + 3f);
        }

        int Order(VehicleTeam t)
        {
            int n = 0;
            foreach (var x in Teams())
            {
                if (x == t) return n;
                n++;
            }
            return n;
        }
    }
}
