using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.EditorTools.Aircraft;
using AirCity.Traffic;
using AirCity.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirCity.Tests.Airfield
{
    /// <summary>
    /// The sandbox scene under auto traffic (as DevTrafficControls runs it) for three game hours: fails when the
    /// ground traffic stops for good or two aircraft touch, with every flight's and vehicle's state in the message.
    /// Takes minutes per seed, so it only runs when asked for (-testFilter TrafficSoakTests). Docs/taxi-todo.md has
    /// where it stands.
    /// </summary>
    public class TrafficSoakTests
    {
        const float Hours = 3f, ArrivalEvery = 150f, Turnaround = 300f;
        /// <summary>Stuck: no flight on the ground has moved for this long while some wait, a vehicle out has stood
        /// still this long, or a flight has stood still for StuckFlight (game seconds).</summary>
        const float Frozen = 400f, StuckFlight = 1500f;

        readonly List<Object> spawned = new List<Object>();
        Scene scene;

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
        }

        [Test, Explicit("game hours of auto traffic: minutes per seed"), Category("Soak")]
        public void SandboxScene_AutoTrafficKeepsMoving([Values(1, 2, 3, 4, 5, 6)] int seed)
        {
            scene = EditorSceneManager.OpenScene(AircraftSetup.SandboxScene, OpenSceneMode.Additive);
            var airport = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Airport>()).First(a => a != null);
            var traffic = airport.Traffic;
            var rng = new System.Random(seed);
            // as a rebuilt sandbox has them (the saved scene may not): small aircraft kept off the jet stands
            foreach (var st in airport.Taxiways.stands)
                if (st.kind == StandKind.Parking && st.code >= SizeCode.C) st.noSmallAircraft = true;
            var types = new[] { "Narrowbody", "Cessna172" }.Select(m => AircraftSetup.AllTypes().First(t => t.model == m)).ToArray();
            var at = airport.transform;
            string Where(AirCity.Aircraft.Aircraft a)
            {
                var p = at.InverseTransformPoint(a.transform.position);
                return $"({p.x:0},{p.z:0})";
            }

            var parkedSince = new Dictionary<AirCity.Aircraft.Aircraft, float>();
            var lastPos = new Dictionary<Flight, Vector3>();
            var still = new Dictionary<Flight, float>();
            var teamAt = new Dictionary<VehicleTeam, Vector2>();
            var teamStill = new Dictionary<VehicleTeam, float>();
            var teamLog = new Dictionary<VehicleTeam, List<string>>();
            var touched = new HashSet<string>();
            var touches = new List<string>();
            var log = new List<string>();
            var all = new List<Flight>();
            float nextArrival = 30f, frozen = 0f;
            int n = 0;

            for (int s = 0; s < Hours * 3600f; s++)
            {
                float now = traffic.Clock;
                // the aircraft that have stood longest on their stands leave after their turnaround, two at a time
                if (traffic.Flights.Count(f => f.kind == FlightKind.Departure && f.Phase <= FlightPhase.StartingEngines) < 2)
                {
                    AirCity.Aircraft.Aircraft longest = null;
                    foreach (var a in AirCity.Aircraft.Aircraft.All)
                    {
                        if (a == null || a.type == null || !a.gameObject.activeInHierarchy || traffic.FlightOf(a) != null || a.Moving) continue;
                        var p = at.InverseTransformPoint(a.transform.position);
                        if (p.y > 1f || !airport.Taxiways.TryPickStand(new Vector2(p.x, p.z), out _)) continue;
                        if (!parkedSince.ContainsKey(a)) parkedSince[a] = now;
                        if (now - parkedSince[a] >= Turnaround && (longest == null || parkedSince[a] < parkedSince[longest])) longest = a;
                    }
                    if (longest != null)
                    {
                        var f = traffic.Depart(longest, out string why);
                        if (f == null)
                        {
                            parkedSince[longest] = now;
                            log.Add($"{now:0}: {longest.name} can't depart: {why}");
                        }
                        else
                        {
                            all.Add(f);
                            log.Add($"{now:0}: {f.Name} departs {Where(longest)} for {f.Route.RunwayName}");
                        }
                    }
                }
                // an arrival of a type that has a free stand every ArrivalEvery (±30%)
                if (now >= nextArrival)
                {
                    nextArrival = now + ArrivalEvery * (0.7f + 0.6f * (float)rng.NextDouble());
                    foreach (var t in types.OrderBy(_ => rng.Next()))
                    {
                        if (!traffic.CanArrive(t, out _)) continue;
                        var go = Object.Instantiate(AircraftSetup.Prefab(t), new Vector3(0f, 0f, -9000f), Quaternion.identity);
                        go.name = $"{t.model} #{++n}";
                        spawned.Add(go);
                        var f = traffic.Arrive(go.GetComponent<AirCity.Aircraft.Aircraft>(), out _);
                        if (f == null)
                        {
                            Object.DestroyImmediate(go);
                            continue;
                        }
                        all.Add(f);
                        log.Add($"{now:0}: {f.Name} arrives on {f.Route.RunwayName} for {f.Route.Destination}");
                        break;
                    }
                }

                airport.Simulate(1f);

                foreach (var f in traffic.Flights)
                {
                    var p = f.aircraft.transform.position;
                    bool moved = !lastPos.TryGetValue(f, out var q) || (p - q).sqrMagnitude > 0.01f;
                    lastPos[f] = p;
                    // a tug on its way isn't the flight's doing
                    bool stopped = f.OnGround && !moved && f.Phase != FlightPhase.LinedUp && !f.Status.Contains("on its way");
                    still[f] = stopped ? (still.TryGetValue(f, out float v) ? v : 0f) + 1f : 0f;
                }
                foreach (var tm in airport.Depots.SelectMany(d => d.Teams))
                {
                    var lp = tm.LeadPosition;
                    string status = tm.Status;
                    bool working = status.Contains("picking up") || status.Contains("mowing") || status.Contains("ploughing");
                    bool stopped = tm.Active && !working && teamAt.TryGetValue(tm, out var q) && (q - lp).sqrMagnitude < 0.01f;
                    teamAt[tm] = lp;
                    teamStill[tm] = stopped ? (teamStill.TryGetValue(tm, out var w) ? w : 0f) + 1f : 0f;
                    if (!teamLog.TryGetValue(tm, out var tl)) teamLog[tm] = tl = new List<string>();
                    if (tl.Count == 0 || !tl[tl.Count - 1].Contains(status)) tl.Add($"{now:0}: {status} at {lp}");
                }
                // aircraft touching (one of them on a flight: the saved scene's parked ones are as they were placed)
                var ground = AirCity.Aircraft.Aircraft.All.Where(x => x != null && x.gameObject.activeInHierarchy && x.transform.position.y < 1f).ToList();
                for (int i = 0; i < ground.Count; i++)
                    for (int k = i + 1; k < ground.Count; k++)
                    {
                        var a = ground[i];
                        var b = ground[k];
                        var fa = traffic.FlightOf(a);
                        var fb = traffic.FlightOf(b);
                        if ((fa == null && fb == null) || (a.transform.position - b.transform.position).sqrMagnitude > 3600f) continue;
                        var oa = AirportMaintenance.Outline(XZ(a.transform.position), XZ(a.transform.forward).normalized, a.type, 0f);
                        var ob = AirportMaintenance.Outline(XZ(b.transform.position), XZ(b.transform.forward).normalized, b.type, 0f);
                        if (!oa.Any(x => ob.Any(y => x.Overlaps(y)))) continue;
                        if (!touched.Add(string.CompareOrdinal(a.name, b.name) < 0 ? a.name + "|" + b.name : b.name + "|" + a.name)) continue;
                        touches.Add($"{now:0}: {a.name} {Where(a)} ({(fa != null ? fa.Status : "parked")}) touches {b.name} {Where(b)} ({(fb != null ? fb.Status : "parked")})");
                    }

                bool anyMoved = traffic.Flights.Any(f => f.OnGround && still[f] == 0f && f.Phase != FlightPhase.LinedUp);
                bool anyWaiting = traffic.Flights.Any(f => f.OnGround && still[f] > 0f);
                frozen = anyWaiting && !anyMoved ? frozen + 1f : 0f;
                var stuckTeam = teamStill.FirstOrDefault(kv => kv.Value > Frozen).Key;
                var stuckFlight = traffic.Flights.FirstOrDefault(f => still[f] > StuckFlight);
                if (frozen > Frozen || stuckTeam != null || stuckFlight != null)
                {
                    string why = frozen > Frozen ? $"no aircraft on the ground has moved for {Frozen:0} s"
                        : stuckTeam != null ? $"{stuckTeam.name} has stood still for {Frozen:0} s"
                        : $"{stuckFlight.Name} has stood still for {StuckFlight:0} s";
                    var flights = traffic.Flights.Select(f => $"  {f.Name} at {Where(f.aircraft)}, {f.Phase}, still {still[f]:0} s: {f.Status}");
                    var teams = airport.Depots.SelectMany(d => d.Teams).Where(tm => tm.Active).Select(tm =>
                        $"  {tm.name} at {tm.LeadPosition}: {tm.Status}\n      " + string.Join("\n      ", teamLog[tm].Skip(Mathf.Max(0, teamLog[tm].Count - 6))));
                    Assert.Fail($"seed {seed}, after {traffic.Clock:0} s: {why}\nflights:\n{string.Join("\n", flights)}\nvehicles:\n{string.Join("\n", teams)}\n" +
                                $"last:\n{string.Join("\n", log.Skip(Mathf.Max(0, log.Count - 10)))}" +
                                (touches.Count > 0 ? "\ntouched:\n" + string.Join("\n", touches) : ""));
                }
            }

            var deps = all.Where(f => f.kind == FlightKind.Departure).ToList();
            var arrs = all.Where(f => f.kind == FlightKind.Arrival).ToList();
            Debug.Log($"Soak seed {seed}: {Hours:0} h, {deps.Count} departures ({deps.Count(f => f.Phase == FlightPhase.Gone)} gone), " +
                      $"{arrs.Count} arrivals ({arrs.Count(f => f.Phase == FlightPhase.Parked)} parked), {all.Sum(f => f.GoArounds)} go-arounds, " +
                      $"mean wait {all.Select(f => f.WaitedFor).DefaultIfEmpty().Average():0} s, longest {all.Select(f => f.WaitedFor).DefaultIfEmpty().Max():0} s");
            Assert.IsEmpty(touches, string.Join("\n", touches));
        }

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
    }
}
