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
    // temporary: reproducing the push-back / arrival deadlock with sandbox-like auto traffic on the real scene
    public class TrafficExplore
    {
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

        [Test]
        public void Explore_AutoTraffic([Values(1, 2, 3, 4, 5, 6)] int seed)
        {
            scene = EditorSceneManager.OpenScene(AircraftSetup.SandboxScene, OpenSceneMode.Additive);
            var airport = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Airport>()).First(a => a != null);
            var rng = new System.Random(seed);
            foreach (var st in airport.Taxiways.stands)
                if (st.kind == StandKind.Parking && st.code >= SizeCode.C) st.noSmallAircraft = true;
            float frozen = 0f;
            var teamStill = new Dictionary<string, float>();
            var teamAt = new Dictionary<string, Vector2>();
            var jlog = new List<string>();
            var touching = new HashSet<string>();
            int touches = 0;
            var types = new[] { "Narrowbody", "Cessna172" }.Select(m => AircraftSetup.AllTypes().First(t => t.model == m)).ToArray();
            var traffic = airport.Traffic;
            var parkedSince = new Dictionary<AirCity.Aircraft.Aircraft, float>();
            var stopped = new Dictionary<Flight, float>();
            var lastPos = new Dictionary<Flight, Vector3>();
            var worst = new Dictionary<Flight, float>();
            var teamLog = new Dictionary<string, List<string>>();
            var teamLast = new Dictionary<string, string>();
            var all = new List<Flight>();
            float nextArrival = 30f;
            int n = 0;
            var log = new List<string>();
            var t0 = airport.transform;
            string Where(AirCity.Aircraft.Aircraft a)
            {
                var p = t0.InverseTransformPoint(a.transform.position);
                return $"({p.x:0},{p.z:0})";
            }
            for (int s = 0; s < 3 * 3600; s++)
            {
                float now = traffic.Clock;
                var stood = new List<AirCity.Aircraft.Aircraft>();
                foreach (var a in AirCity.Aircraft.Aircraft.All)
                {
                    if (a == null || a.type == null || !a.gameObject.activeInHierarchy || traffic.FlightOf(a) != null || a.Moving || a.transform.position.y > 1f) continue;
                    // only those on a stand
                    var p = t0.InverseTransformPoint(a.transform.position);
                    if (!airport.Taxiways.TryPickStand(new Vector2(p.x, p.z), out _)) continue;
                    if (!parkedSince.ContainsKey(a)) parkedSince[a] = now;
                    stood.Add(a);
                }
                int leaving = traffic.Flights.Count(f => f.kind == FlightKind.Departure && f.Phase <= FlightPhase.StartingEngines);
                if (leaving < 2)
                {
                    var longest = stood.Where(a => now - parkedSince[a] >= 300f).OrderBy(a => parkedSince[a]).FirstOrDefault();
                    if (longest != null)
                    {
                        var f = traffic.Depart(longest, out _);
                        if (f == null)
                        {
                            parkedSince[longest] = now;
                            traffic.PreviewDeparture(longest, out string whyNot);
                            log.Add($"{now:0}: can't depart {longest.name}: {whyNot}");
                        }
                        else
                        {
                            all.Add(f);
                            log.Add($"{now:0}: depart {f.Name} {Where(longest)} -> {f.Route.RunwayName}");
                        }
                    }
                }
                if (now >= nextArrival)
                {
                    nextArrival = now + 150f * (0.7f + 0.6f * (float)rng.NextDouble());
                    foreach (var t in types.OrderBy(_ => rng.Next()))
                    {
                        if (!traffic.CanArrive(t, out _)) continue;
                        var go = Object.Instantiate(AircraftSetup.Prefab(t), new Vector3(0f, 0f, -9000f), Quaternion.identity);
                        go.name = $"{t.model} #{++n}";
                        spawned.Add(go);
                        var f = traffic.Arrive(go.GetComponent<AirCity.Aircraft.Aircraft>(), out string why);
                        if (f == null) Object.DestroyImmediate(go);
                        else
                        {
                            all.Add(f);
                            log.Add($"{now:0}: arrive {f.Name} -> {f.Route.RunwayName} {f.Route.Destination}");
                        }
                        break;
                    }
                }
                for (int sub = 0; sub < 4; sub++)
                {
                    var before = traffic.Flights.ToDictionary(f => f, f => f.Route.holds.Select(h => h.cleared).ToArray());
                    var routes = traffic.Flights.ToDictionary(f => f, f => f.Route);
                    airport.Simulate(0.25f);
                    foreach (var f in traffic.Flights)
                    {
                        if (!before.TryGetValue(f, out var was) || routes[f] != f.Route) continue;
                        for (int k = 0; k < f.Route.holds.Count && k < was.Length; k++)
                        {
                            var h = f.Route.holds[k];
                            if (h.cleared && !was[k] && h.kind == HoldKind.Junction)
                            {
                                var others = traffic.Flights.Where(g => g != f && g.OnGround && Vector3.Distance(g.aircraft.transform.position, f.aircraft.transform.position) < 150f)
                                    .Select(g => $"{g.Name} {Where(g.aircraft)} {g.Phase}");
                                jlog.Add($"{traffic.Clock:0}: {f.Name} {f.Phase} at {Where(f.aircraft)} along {f.Along:0} cleared J{h.node} hold {h.at:0} (rel {h.release:0}); near: {string.Join(", ", others)}");
                            }
                        }
                    }
                }
                foreach (var f in traffic.Flights)
                {
                    var p = f.aircraft.transform.position;
                    bool moved = !lastPos.TryGetValue(f, out var q) || (p - q).sqrMagnitude > 0.01f;
                    lastPos[f] = p;
                    bool still = f.OnGround && !moved && f.Phase != FlightPhase.LinedUp && !f.Status.Contains("tug on its way");
                    stopped[f] = still ? (stopped.TryGetValue(f, out float v) ? v : 0f) + 1f : 0f;
                    worst[f] = Mathf.Max(worst.TryGetValue(f, out float w) ? w : 0f, stopped[f]);
                }
                foreach (var tm in airport.Depots.SelectMany(d => d.Teams))
                {
                    string st = tm.Status;
                    if (teamLast.TryGetValue(tm.name, out var was) && was == st) continue;
                    teamLast[tm.name] = st;
                    if (!teamLog.TryGetValue(tm.name, out var tl)) teamLog[tm.name] = tl = new List<string>();
                    tl.Add($"{traffic.Clock:0}: {st} at {tm.LeadPosition}");
                }
                if (touches < 5)
                {
                    var ground = AirCity.Aircraft.Aircraft.All.Where(x => x != null && x.gameObject.activeInHierarchy && x.transform.position.y < 1f).ToList();
                    for (int i = 0; i < ground.Count && touches < 5; i++)
                        for (int k = i + 1; k < ground.Count && touches < 5; k++)
                        {
                            var a = ground[i];
                            var b = ground[k];
                            if ((a.transform.position - b.transform.position).sqrMagnitude > 3600f) continue;
                            var pa = AirportMaintenance.Outline(new Vector2(a.transform.position.x, a.transform.position.z), new Vector2(a.transform.forward.x, a.transform.forward.z).normalized, a.type, 0f);
                            var pb = AirportMaintenance.Outline(new Vector2(b.transform.position.x, b.transform.position.z), new Vector2(b.transform.forward.x, b.transform.forward.z).normalized, b.type, 0f);
                            if (!pa.Any(x => pb.Any(y => x.Overlaps(y)))) continue;
                            string key = string.CompareOrdinal(a.name, b.name) < 0 ? a.name + "|" + b.name : b.name + "|" + a.name;
                            if (!touching.Add(key)) continue;
                            touches++;
                            var fa = traffic.FlightOf(a);
                            var fb = traffic.FlightOf(b);
                            Debug.Log($"EXPLORE seed {seed} TOUCH at {traffic.Clock:0}s: {a.name} {Where(a)} {(fa != null ? fa.Phase + " along " + fa.Along.ToString("0") + ": " + fa.Status : "parked")} / {b.name} {Where(b)} {(fb != null ? fb.Phase + " along " + fb.Along.ToString("0") + ": " + fb.Status : "parked")}");
                        }
                }
                bool anyMoved = traffic.Flights.Any(f => f.OnGround && stopped[f] == 0f && f.Phase != FlightPhase.LinedUp);
                bool anyWaiting = traffic.Flights.Any(f => f.OnGround && stopped[f] > 0f);
                frozen = anyWaiting && !anyMoved ? frozen + 1f : 0f;
                foreach (var tm in airport.Depots.SelectMany(d => d.Teams))
                {
                    var lp = tm.LeadPosition;
                    bool still = tm.Active && teamAt.TryGetValue(tm.name, out var q) && (q - lp).sqrMagnitude < 0.01f && !tm.Status.Contains("picking up") && !tm.Status.Contains("mowing") && !tm.Status.Contains("ploughing");
                    teamAt[tm.name] = lp;
                    teamStill[tm.name] = still ? (teamStill.TryGetValue(tm.name, out var w) ? w : 0f) + 1f : 0f;
                }
                var stuckTeam = teamStill.FirstOrDefault(kv => kv.Value > 400f).Key;
                var stuck = frozen > 400f || stuckTeam != null ? traffic.Flights.FirstOrDefault() : traffic.Flights.FirstOrDefault(f => stopped[f] > 1500f);
                if (stuckTeam != null)
                {
                    log.Add($"TEAM STUCK: {stuckTeam}");
                    foreach (var vb in airport.Maintenance.VehicleBoxes(null).Where(b => b.team.name == stuckTeam))
                        foreach (var a in AirCity.Aircraft.Aircraft.All.Where(x => x != null && x.gameObject.activeInHierarchy && x.transform.position.y < 3f))
                        {
                            var ap = new Vector2(a.transform.position.x, a.transform.position.z);
                            if ((ap - vb.c).magnitude > 60f) continue;
                            var outline = AirportMaintenance.Outline(ap, new Vector2(a.transform.forward.x, a.transform.forward.z).normalized, a.type, 0.3f);
                            for (float ahead = 0f; ahead <= 8f; ahead += 2f)
                            {
                                var m = vb;
                                m.c += m.az * ahead;
                                int part = outline.FindIndex(o => o.Overlaps(m));
                                if (part >= 0)
                                {
                                    log.Add($"  {vb.what} at {vb.c} facing {vb.az} {(ahead == 0f ? "now" : $"{ahead} m on")} touches {a.name} at {ap} facing {new Vector2(a.transform.forward.x, a.transform.forward.z)} part {part} (0 fuselage, 1 wing, 2 tail)");
                                    break;
                                }
                            }
                        }
                }
                if (stuck != null)
                {
                    var lines = traffic.Flights.Select(f => $"  {f.Name} {f.Phase} along {f.Along:0}/{f.Route.path.Length:0} at {Where(f.aircraft)} stopped {stopped[f]:0}s: {f.Status}");
                    var teams = airport.Depots.SelectMany(d => d.Teams).Select(tm =>
                    {
                        var lp = tm.LeadPosition;
                        var near = AirCity.Aircraft.Aircraft.All.Where(x => x != null && x.gameObject.activeInHierarchy)
                            .OrderBy(x => Vector2.Distance(lp, new Vector2(x.transform.position.x, x.transform.position.z))).Take(2)
                            .Select(x => $"{x.name} {Where(x)} {Vector2.Distance(lp, new Vector2(x.transform.position.x, x.transform.position.z)):0} m{(traffic.FlightOf(x) != null ? " " + traffic.FlightOf(x).Phase : " parked")}");
                        string hist = tm.type.IsTug && teamLog.TryGetValue(tm.name, out var tl) ? "\n      " + string.Join("\n      ", tl.Skip(Mathf.Max(0, tl.Count - 10))) : "";
                        return $"  team {tm.name}: {tm.Status} at {lp}; near {string.Join(", ", near)}{hist}";
                    });
                    Debug.Log($"EXPLORE seed {seed} STUCK at {traffic.Clock:0}s:\n" + string.Join("\n", lines) + "\n" + string.Join("\n", teams) + "\nlog:\n" + string.Join("\n", log.Skip(Mathf.Max(0, log.Count - 12))) +
                              "\njunctions:\n" + string.Join("\n", jlog.Skip(Mathf.Max(0, jlog.Count - 14))) +
                              "\nholds:\n" + string.Join("\n", traffic.Flights.Select(f => $"  {f.Name} along {f.Along:0}: " + string.Join(" ", f.Route.holds.Select(h => $"{h.kind}{(h.node >= 0 ? h.node.ToString() : "")}@{h.at:0}{(h.cleared ? "+" : "")}{(h.released ? "r" : "")}")))));
                    Assert.Fail($"{stuck}");
                }
            }
            var deps = all.Where(f => f.kind == FlightKind.Departure).ToList();
            var arrs = all.Where(f => f.kind == FlightKind.Arrival).ToList();
            Debug.Log($"EXPLORE seed {seed} unfinished: " + string.Join("; ", traffic.Flights.Select(f => f.ToString())) + "\nlog:\n" + string.Join("\n", log.Skip(Mathf.Max(0, log.Count - 10))));
            Debug.Log($"EXPLORE seed {seed}: 3 h no deadlock, {deps.Count} departures ({deps.Count(f => f.Phase == FlightPhase.Gone)} gone), " +
                      $"{arrs.Count} arrivals ({arrs.Count(f => f.Phase == FlightPhase.Parked)} parked), go-arounds {all.Sum(f => f.GoArounds)}, " +
                      $"departures stood still up to {deps.Select(f => worst.TryGetValue(f, out var w) ? w : 0f).DefaultIfEmpty().Max():0} s (mean wait {deps.Select(f => f.WaitedFor).DefaultIfEmpty().Average():0}), " +
                      $"arrivals up to {arrs.Select(f => worst.TryGetValue(f, out var w) ? w : 0f).DefaultIfEmpty().Max():0} s (mean wait {arrs.Select(f => f.WaitedFor).DefaultIfEmpty().Average():0})");
        }
    }
}
