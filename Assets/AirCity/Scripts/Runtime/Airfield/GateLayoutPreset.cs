using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>A reusable physical gate layout, in the stand's frame. Docking poses are always calculated.</summary>
    [CreateAssetMenu(menuName = "AirCity/Gate layout preset")]
    public sealed class GateLayoutPreset : ScriptableObject
    {
        public BridgeHead head = new BridgeHead();
        public List<BridgeHead> additionalHeads = new List<BridgeHead>();
        public List<JetBridge> bridges = new List<JetBridge>();
        public List<StandServiceConfiguration> aircraft = new List<StandServiceConfiguration>();

        public void Capture(Stand stand)
        {
            head = stand.head.Clone();
            additionalHeads = stand.additionalHeads.ConvertAll(h => h.Clone());
            bridges = stand.bridges.ConvertAll(b => b.Clone());
            aircraft = stand.serviceConfigurations.ConvertAll(c => c.Clone());
        }

        public bool Apply(Airport airport, Stand stand, bool mirror, out BridgeProblem problem)
        {
            problem = BridgeProblem.None;
            if (airport == null || stand == null || airport.Taxiways.Layout(stand) == null || bridges.Count > JetBridgeRules.MaxPerStand)
            { problem = BridgeProblem.InvalidDefinition; return false; }
            var wasHead = stand.head;
            var wasAdditional = stand.additionalHeads;
            var wasBridges = stand.bridges;
            var wasAircraft = stand.serviceConfigurations;
            stand.head = head.Clone();
            stand.additionalHeads = additionalHeads.ConvertAll(h => h.Clone());
            stand.bridges = bridges.ConvertAll(b => b.Clone());
            stand.serviceConfigurations = aircraft.ConvertAll(c => c.Clone());
            stand.head.id = System.Guid.NewGuid().ToString("N");
            foreach (var h in stand.additionalHeads) h.id = System.Guid.NewGuid().ToString("N");
            if (mirror)
            {
                Vector2 Flip(Vector2 p) => new Vector2(-p.x, p.y);
                void Path(List<Vector3> path)
                {
                    for (int i = 0; i < path.Count; i++) path[i] = new Vector3(-path[i].x, path[i].y, path[i].z);
                }
                foreach (var h in stand.BridgeHeads)
                {
                    h.Exterior.MirrorHead();
                    h.from = Flip(h.from); h.to = Flip(h.to); h.root = Flip(h.root);
                    Path(h.linkBends);
                    h.supportColumns = h.supportColumns.ConvertAll(Flip);
                    h.linkSupportColumns = h.linkSupportColumns.ConvertAll(Flip);
                }
                foreach (var b in stand.bridges)
                {
                    b.rotunda = Flip(b.rotunda); b.root = Flip(b.root);
                    b.side = b.side == DoorSide.Left ? DoorSide.Right : DoorSide.Left;
                    Path(b.linkBends); b.supportColumns = b.supportColumns.ConvertAll(Flip);
                }
            }
            airport.Taxiways.Rebuild();
            var site = airport.Bridges.Site(stand);
            problem = BridgeProblem.None;
            var sl = airport.Taxiways.Layout(stand);
            foreach (var h in HeadLayout.All(sl, airport.terminals))
            {
                problem = h.Placement(site);
                if (problem != BridgeProblem.None) break;
            }
            if (problem == BridgeProblem.None)
                foreach (var b in stand.bridges)
                {
                    problem = BridgeLayout.Of(b, sl, airport.terminals).Placement(site);
                    if (problem != BridgeProblem.None) break;
                }
            if (problem == BridgeProblem.None) return true;
            stand.head = wasHead; stand.additionalHeads = wasAdditional;
            stand.bridges = wasBridges; stand.serviceConfigurations = wasAircraft;
            airport.Taxiways.Rebuild();
            return false;
        }
    }
}
