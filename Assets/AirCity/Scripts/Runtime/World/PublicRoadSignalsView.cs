using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AirCity.World
{
    [ExecuteAlways]
    public sealed class PublicRoadSignalsView : MonoBehaviour
    {
        readonly List<(RoadSignalHead head, int group)> heads = new List<(RoadSignalHead, int)>();
        void OnEnable()
        {
            heads.Clear();
            foreach (var head in GetComponentsInChildren<RoadSignalHead>()) heads.Add((head, head.signalGroup));
            SetClock(0f);
        }
        public void Build(PublicRoadNetwork graph, GameObject prefab)
        {
            if (prefab == null) return;
            foreach (var node in graph.nodes)
            {
                if (node.control != JunctionControl.TrafficLights || graph.At(node.id).Count() < 3) continue;
                foreach (var edge in graph.At(node.id))
                {
                    var dir = PublicRoadSignals.Direction(graph, node.id, edge);
                    var left = new Vector2(-dir.y, dir.x);
                    var position = node.position + new Vector3(dir.x, 0f, dir.y) * (edge.Width * .5f + 6f) +
                        new Vector3(left.x, 0f, left.y) * (edge.Width * .5f + 1.1f);
                    var go = Instantiate(prefab, position, Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)), transform);
                    var head = go.GetComponent<RoadSignalHead>();
                    head.signalGroup = PublicRoadSignals.Group(graph, node.id, edge.id);
                    heads.Add((head, head.signalGroup));
                }
            }
            SetClock(0f);
        }
        public void SetClock(float seconds)
        { foreach (var item in heads) item.head.SetState(PublicRoadSignals.Signal(item.group, seconds)); }
    }
}
