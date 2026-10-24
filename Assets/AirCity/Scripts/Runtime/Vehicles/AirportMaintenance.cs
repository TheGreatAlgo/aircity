using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// The airport's maintenance work across all its depots: one queue of jobs (ploughing, mowing grass runways,
    /// taxiways and verges), handed to the nearest free team of the right kind; with autoDispatch the weather and
    /// the grass fill the queue by themselves. Steps every team together in game time and keeps their vehicles
    /// clear of aircraft and of each other: each vehicle looks ahead along its path and stops short of anything
    /// in the way (a working team has right of way over one driving by, which has it over one going home), and a
    /// team held up by a parked aircraft for a while plans a way round it.
    ///
    ///   airport.Maintenance.Plough(runway);  .PloughTaxiways();  .MowVerges(runway);  .MowAll();
    ///
    /// Taxiway and road jobs are runs (TaxiwayNetwork.Run): asking for one section queues the whole run it's on.
    /// </summary>
    public sealed class AirportMaintenance
    {
        const float Step = 0.5f, CheckEvery = 30f, Decel = 2.2f, StuckReroute = 20f;

        readonly Airport airport;
        readonly List<MaintenanceJob> queue = new List<MaintenanceJob>();
        float checkTimer;

        public AirportMaintenance(Airport airport) => this.airport = airport;

        public IReadOnlyList<MaintenanceJob> Queue => queue;

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
                if (t.type.Work != j.kind) continue;
                float d = Vector2.Distance(t.LeadPosition, mid);
                if (d < bestD)
                {
                    bestD = d;
                    best = t;
                }
            }
            return bestD;
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
                    if (t.Job != null && !t.Job.Valid) t.Recall();
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
                if (finished) Dispatch();
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

        const float AircraftClearance = 2.5f, VehicleClearance = 0.3f;

        /// <summary>Every aircraft's footprint, in the Airport's space.</summary>
        List<Box> AircraftBoxes(bool parkedOnly)
        {
            var list = new List<Box>();
            var t = airport.transform;
            foreach (var a in Aircraft.Aircraft.All)
            {
                if (a == null || a.type == null) continue;
                bool parked = !a.Moving;
                if (parkedOnly && !parked) continue;
                var at = t.InverseTransformPoint(a.transform.position);
                var f = t.InverseTransformDirection(a.transform.forward);
                var facing = new Vector2(f.x, f.z);
                if (facing.sqrMagnitude < 1e-6f) continue;
                var box = Box.Of(new Vector2(at.x, at.z), facing.normalized, a.type.bounds, AircraftClearance);
                box.what = "aircraft";
                box.parkedAircraft = parked;
                list.Add(box);
            }
            return list;
        }

        /// <summary>For route planning: is the stretch a to b blocked by a parked aircraft (for vehicles of this
        /// type)? Null when no aircraft is parked anywhere.</summary>
        public Func<Vector2, Vector2, bool> RouteBlocked(VehicleType type)
        {
            var boxes = AircraftBoxes(true);
            if (boxes.Count == 0) return null;
            float grow = type.Width * 0.5f;
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
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector2.Lerp(a, b, k / (float)n);
                    foreach (var box in boxes)
                        if (box.Contains(p)) return true;
                }
                return false;
            };
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
                    box.what = t.type.kind == VehicleKind.Mower ? "a mower" : "a plough";
                    obstacles.Add(box);
                }
            if (teams.Count == 0) return;

            var hits = new Dictionary<(VehicleTeam, int), Box>();
            foreach (var t in teams)
                for (int i = 0; i < t.size; i++)
                {
                    float d0 = t.Position(i), v = t.Velocity(i);
                    float look = v * v / (2f * Decel) + 6f + t.type.Length * 0.25f;
                    for (float d = d0 + 1.5f; d <= d0 + look; d += 2f)
                    {
                        var p = t.Where(i, d, out var f);
                        var me = Box.Of(p, f, t.type.bounds, VehicleClearance);
                        bool hit = false;
                        foreach (var o in obstacles)
                        {
                            // a team keeps its own spacing on the shared path; in the yard each has its own way
                            if (o.team == t && (o.index == i || (!t.InYard(i) && !t.InYard(o.index)))) continue;
                            if (!me.Overlaps(o)) continue;
                            hits[(t, i)] = o;
                            t.cap[i] = Mathf.Sqrt(2f * Decel * Mathf.Max(0f, d - d0 - 2.5f));
                            t.blockedBy[i] = o.what;
                            hit = true;
                            break;
                        }
                        if (hit) break;
                    }
                }
            // right of way: of two vehicles each stopping for the other, the one with priority goes
            foreach (var kv in hits)
            {
                var (t, i) = kv.Key;
                var o = kv.Value;
                if (o.team == null) continue;
                if (!hits.TryGetValue((o.team, o.index), out var back) || back.team != t || back.index != i) continue;
                int mine = Priority(t), theirs = Priority(o.team);
                bool first = o.team == t ? i < o.index : mine > theirs || (mine == theirs && Order(t) < Order(o.team));
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
            }
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
