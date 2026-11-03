using System.Collections.Generic;
using AirCity.Aircraft;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// Derived, joint door assignment for one aircraft. Placement previews and simulation consume the same checked
    /// plan; unused bridges stay in their automatically calculated parking positions. Nothing here is saved.
    /// </summary>
    public sealed class BridgeServicePlan
    {
        public readonly struct Entry
        {
            public readonly BridgeLayout layout;
            public readonly BridgePose parked, docked;
            public readonly BridgeProblem problem;
            public bool Operates => problem == BridgeProblem.None && layout.Connected;
            public string DoorName => (layout.serviceDoor ?? new BridgeDoor(layout.bridge.side, layout.bridge.upper, layout.bridge.door)).Name;

            public Entry(BridgeLayout layout, BridgePose parked, BridgePose docked, BridgeProblem problem)
            {
                this.layout = layout; this.parked = parked; this.docked = docked; this.problem = problem;
            }
        }

        readonly Dictionary<JetBridge, Entry> entries = new Dictionary<JetBridge, Entry>();
        public Entry this[JetBridge bridge] => entries[bridge];
        public bool TryGet(JetBridge bridge, out Entry entry) => entries.TryGetValue(bridge, out entry);

        public static BridgeServicePlan Build(IList<BridgeLayout> list, BridgeSite site, AircraftType type)
        {
            var plan = new BridgeServicePlan();
            var parks = BridgeLayout.ParkAll(list, site.types, site.HeadShapes, out var canPark);
            var choices = new List<BridgeDoor>[list.Count];
            var reasons = new BridgeProblem[list.Count];
            var working = new List<BridgeLayout>();
            var configuration = list.Count > 0 ? list[0].stand.stand.ServiceFor(type) : null;
            var single = new BridgeSite
            {
                head = site.head, terminals = site.terminals, types = new[] { type }, pads = site.pads,
                pavement = site.pavement, taxiways = site.taxiways, neighbours = site.neighbours,
                additionalHeads = site.additionalHeads,
            };
            for (int i = 0; i < list.Count; i++)
            {
                var l = list[i];
                choices[i] = new List<BridgeDoor>();
                working.Add(l.ForService(null, true, parks[i]));
                reasons[i] = BridgeProblem.NotInService;
                if (configuration?.allowed == false || configuration?.unusedBridges.Contains(l.bridge.id) == true) continue;
                if (!l.stand.ParkingFits(type)) { reasons[i] = BridgeProblem.AircraftStop; continue; }
                var placement = l.Placement(site);
                if (placement != BridgeProblem.None) { reasons[i] = placement; continue; }
                l.Dock(type, out reasons[i]);
                if (!canPark[i]) { reasons[i] = BridgeProblem.NoRoomToPark; continue; }
                foreach (var side in new[] { DoorSide.Left, DoorSide.Right })
                foreach (bool upper in new[] { false, true })
                for (int n = 1; n <= 6; n++)
                {
                    var door = new BridgeDoor(side, upper, n);
                    var candidate = l.ForService(door, false, parks[i]);
                    candidate.Dock(type, out var problem);
                    if (problem == BridgeProblem.None) choices[i].Add(door);
                }
                float Preference(BridgeDoor d) => Mathf.Abs(d.number - l.bridge.door) + (d.side != l.bridge.side ? 2f : 0f) + (d.upper != l.bridge.upper ? 2f : 0f);
                choices[i].Sort((a, b) => Preference(a).CompareTo(Preference(b)));
                if (choices[i].Count > 0) reasons[i] = BridgeProblem.HitsBridge;
            }

            int bestCount = -1;
            float bestScore = float.PositiveInfinity;
            List<BridgeLayout> best = null;
            var used = new HashSet<BridgeDoor>();
            void Search(int at, int count, float score)
            {
                if (count + list.Count - at < bestCount) return;
                if (at == list.Count)
                {
                    if (count < bestCount || (count == bestCount && score >= bestScore)) return;
                    foreach (var l in working)
                    {
                        if (l.inactive) continue;
                        foreach (var (_, p) in BridgeLayout.Check(l, working, single))
                            if (p != BridgeProblem.None) return;
                    }
                    bestCount = count; bestScore = score;
                    best = new List<BridgeLayout>(working);
                    return;
                }
                var original = list[at];
                foreach (var door in choices[at])
                {
                    if (!used.Add(door)) continue;
                    working[at] = original.ForService(door, false, parks[at]);
                    Search(at + 1, count + 1, score + Mathf.Abs(door.number - original.bridge.door) +
                        (door.side != original.bridge.side ? 2f : 0f) + (door.upper != original.bridge.upper ? 2f : 0f));
                    used.Remove(door);
                }
                working[at] = original.ForService(null, true, parks[at]);
                Search(at + 1, count, score);
            }
            Search(0, 0, 0f);
            for (int i = 0; i < list.Count; i++)
            {
                var l = best != null ? best[i] : working[i];
                var dock = l.Dock(type, out var problem);
                if (l.inactive) problem = reasons[i];
                plan.entries[list[i].bridge] = new Entry(l, parks[i], dock, problem);
            }
            return plan;
        }
    }
}
