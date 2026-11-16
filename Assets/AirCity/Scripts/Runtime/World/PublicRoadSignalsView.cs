using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>
    /// The traffic signals at every signalled junction (JunctionControl.TrafficLights), as on a real road: for each road
    /// arriving, a signal on a pole at the kerb just past its stop line, facing its traffic; on roads with two or more
    /// approach lanes a mast arm carries a second head out over the lanes; on a divided road a third stands on the
    /// median. Heads show their approach's phase (PublicRoadSignals).
    /// </summary>
    [ExecuteAlways]
    public sealed class PublicRoadSignalsView : MonoBehaviour
    {
        readonly List<(RoadSignalHead head, int group)> heads = new List<(RoadSignalHead, int)>();
        Material armMaterial;

        void OnEnable()
        {
            heads.Clear();
            foreach (var head in GetComponentsInChildren<RoadSignalHead>()) heads.Add((head, head.signalGroup));
            SetClock(0f);
        }

        /// <summary>How many signal heads were placed (tests).</summary>
        public int HeadCount => heads.Count;

        /// <summary>Place the signals; `stopLine(node, edge)` is where each road's stop line is painted (PublicRoadGeometry.StopLine).</summary>
        public void Build(PublicRoadNetwork graph, GameObject prefab, System.Func<int, int, float> stopLine = null,ISet<int> junctionIds=null)
        {
            if (prefab == null) return;
            foreach (var node in graph.nodes)
            {
                if(junctionIds!=null && !junctionIds.Contains(node.id))continue;
                if (node.control != JunctionControl.TrafficLights || graph.At(node.id).Count() < 3) continue;
                foreach (var edge in graph.At(node.id))
                {
                    var t = edge.Type;
                    bool arriving = edge.b == node.id;
                    int lanes = arriving ? t.forward : t.backward;
                    if (lanes == 0) continue;   // a one-way road leaving the junction: no traffic to signal
                    int group = PublicRoadSignals.Group(graph, node.id, edge.id);
                    var dir = PublicRoadSignals.Direction(graph, node.id, edge);   // from the junction out along the road
                    var d3 = new Vector3(dir.x, 0f, dir.y);
                    // seen by arriving traffic, which drives along -dir keeping right, its kerb is on dir's left
                    var kerbSide = new Vector3(-dir.y, 0f, dir.x);
                    float half = t.Width * .5f;
                    float stop = stopLine != null ? stopLine(node.id, edge.id) : StopDistance(graph, node, edge);
                    var facing = Quaternion.LookRotation(d3);   // the lenses face back down the road at the drivers
                    var kerb = node.position + d3 * (stop - 1.5f) + kerbSide * (half + .9f);
                    Place(prefab, kerb, facing, group);
                    if (lanes >= 2)
                    {
                        // a mast arm out over the approach lanes, a head over their middle
                        var (inner, outer) = t.Carriageway(arriving);
                        float over = t.OneWay ? 0f : (Mathf.Abs(inner) + Mathf.Abs(outer)) * .5f;
                        var hang = node.position + d3 * (stop - 1.5f) + kerbSide * over;
                        Arm(kerb, hang, MastHeight);
                        Place(prefab, hang, facing, group, false);
                    }
                    if (t.Divided)
                    {
                        var median = node.position + d3 * (stop + 2.5f);   // on the median's nose, behind the stop line
                        Place(prefab, median, facing, group);
                    }
                }
            }
            SetClock(0f);
        }

        /// <summary>How far from the junction centre a road's stop line is (as PublicRoadGeometry paints it): the
        /// junction's own size along the road, plus a crosswalk's width on streets.</summary>
        static float StopDistance(PublicRoadNetwork graph, PublicRoadNode node, PublicRoadSegment edge)
        {
            float need = 0f;
            var dir = PublicRoadSignals.Direction(graph, node.id, edge);
            float half = edge.Width * .5f;
            float length = Vector2.Distance(PublicRoadNetwork.XZ(graph.Node(edge.a).position), PublicRoadNetwork.XZ(graph.Node(edge.b).position));
            foreach (var other in graph.At(node.id))
            {
                if (other == edge) continue;
                var od = PublicRoadSignals.Direction(graph, node.id, other);
                AirCity.Airfield.Building.JunctionShape.OverlapReach(dir, half, length, od, other.Width * .5f, 1000f, out float along, out _);
                need = Mathf.Max(need, along);
            }
            float kerb = edge.Type.kerbRadius;
            return need + kerb * .5f + (edge.Type.edge == RoadEdge.Kerb ? 4.5f : .8f);
        }

        void Place(GameObject prefab, Vector3 at, Quaternion facing, int group, bool pole = true)
        {
            var go = Instantiate(prefab, at, facing, transform);
            if (!pole)
            {
                // a head hung from a mast arm: the prefab's pole is left out and its case (4.55 m up the prefab, 1.2 m
                // tall) raised to hang just under the arm
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    if (r.name.ToLowerInvariant().Contains("pole")) r.enabled = false;
                go.transform.position = at + Vector3.up * (MastHeight - .1f - 4.55f - .6f);
            }
            var head = go.GetComponent<RoadSignalHead>();
            head.signalGroup = group;
            heads.Add((head, group));
        }

        /// <summary>Height of a mast arm (clear of tall vehicles: at least 5.2 m under the hanging head).</summary>
        const float MastHeight = 6.6f;

        /// <summary>A horizontal mast arm from the top of the kerb pole out to where a head hangs.</summary>
        void Arm(Vector3 from, Vector3 to, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Signal mast arm";
            var col = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            go.transform.SetParent(transform, false);
            var a = from + Vector3.up * height;
            var b = to + Vector3.up * height;
            go.transform.position = (a + b) * .5f;
            go.transform.rotation = Quaternion.LookRotation(b - a);
            go.transform.localScale = new Vector3(.14f, .14f, Vector3.Distance(a, b) + .1f);
            if (armMaterial == null)
                foreach (var r in GetComponentsInChildren<Renderer>())
                    if (r.name.ToLowerInvariant().Contains("pole")) { armMaterial = r.sharedMaterial; break; }
            if (armMaterial != null) go.GetComponent<Renderer>().sharedMaterial = armMaterial;
        }

        public void SetClock(float seconds)
        { foreach (var item in heads) item.head.SetState(PublicRoadSignals.Signal(item.group, seconds)); }
    }
}
