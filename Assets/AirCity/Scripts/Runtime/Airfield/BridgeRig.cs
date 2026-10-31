using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Airfield
{
    /// <summary>
    /// Shows the airport's jet bridges (on the Airport's object): one JetBridge model per bridge, its parts placed each
    /// frame from the pose AirportBridges has it in. The rotunda turns with the tunnel; the three tunnel sections (a short
    /// bridge's ShortTunnel_A/B/C) share the tunnel's length out (the smallest stays at the rotunda, the largest carries
    /// the cab and the drive); the drive
    /// stands under the yoke with its legs stretched up to it; the cab turns to face the aircraft. The fixed link is
    /// stretched from the terminal's wall (at the main floor) to the rotunda, ramping up to an upper-deck bridge's raised
    /// rotunda (RotundaHigh; a model without one stretches the low one), on the columns BridgeLayout.Columns placed off
    /// the pavement: each a foot on the ground, a shaft stretched up to the head under the link. A bridge on a stand's
    /// bridge head has a short level stub link from the head's wall instead. Each head is another copy of the model
    /// showing only its head parts: the storeys (Head, HeadUpper) stretched to its length with their end walls, the main
    /// link (the Link, widened) from the terminal, and the columns under both.
    ///
    /// The instances are made at run time and never saved: the stands' bridges are what gets saved. Outside Play mode
    /// (editor, captures) they are placed again before each frame renders, as LateUpdate doesn't run every frame then.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Airport))]
    public class BridgeRig : MonoBehaviour
    {
        [Tooltip("The JetBridge model (VehicleSetup makes it): parts Rotunda, RotundaHigh, Tunnel_A/B/C, ShortTunnel_A/B/C, Drive, " +
                 "DriveLegs, Cab, Link, LinkColumnFoot, LinkColumn, LinkColumnHead, Head, HeadUpper, HeadEnd, HeadUpperEnd.")]
        public GameObject prefab;

        sealed class Instance
        {
            public GameObject root;
            public Transform rotunda, rotundaHigh, cab, drive, legs, link;
            /// <summary>The long tunnel's sections, and the short one's (null in a model built before it had them).</summary>
            public Transform[] tunnel, shortTunnel;
            /// <summary>The link's columns' feet, shafts and heads (the first of each is the model's own).</summary>
            public readonly List<Transform> feet = new List<Transform>(), shafts = new List<Transform>(), heads = new List<Transform>();
        }

        sealed class HeadInstance
        {
            public GameObject root;
            public Transform head, upper, link;
            /// <summary>The storeys' end walls: at the far end, and turned round at the near end.</summary>
            public Transform[] ends, upperEnds;
            public readonly List<Transform> feet = new List<Transform>(), shafts = new List<Transform>(), heads = new List<Transform>();
        }

        /// <summary>The model's parts a head shows (the rest are hidden on its copy), and those a bridge doesn't.</summary>
        static readonly string[] HeadParts = { "Head", "HeadUpper", "HeadEnd", "HeadUpperEnd", "Link", "LinkColumnFoot", "LinkColumn", "LinkColumnHead" };
        static readonly string[] NotOnBridges = { "Head", "HeadUpper", "HeadEnd", "HeadUpperEnd" };

        /// <summary>The link stops this far from the rotunda's centre: its corners then just meet the drum.</summary>
        const float LinkIntoDrum = 1.7f;
        /// <summary>A stub link starts this far inside its head's wall, and a main link ends this far inside the head.</summary>
        const float StubIntoHead = 0.3f, LinkIntoHead = 0.5f;
        /// <summary>A column's shaft starts on its foot this high, and ends this far under the link's underside (inside
        /// the head's collar). As built in the model (COLUMN_BASE, COLUMN_SHAFT_TOP).</summary>
        const float ShaftBase = 0.15f, ShaftUnderLink = 0.45f;
        static readonly List<Vector2> NoColumns = new List<Vector2>();

        readonly Dictionary<JetBridge, Instance> instances = new Dictionary<JetBridge, Instance>();
        readonly Dictionary<BridgeHead, HeadInstance> headInstances = new Dictionary<BridgeHead, HeadInstance>();
        Transform container;
        Airport airport;

        void OnEnable()
        {
            airport = GetComponent<Airport>();
            RenderPipelineManager.beginContextRendering += BeforeRender;
            Refresh();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= BeforeRender;
            Clear();
        }

        void LateUpdate() => Refresh();

        void BeforeRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!Application.isPlaying && this != null) Refresh();
        }

        /// <summary>Make, place or remove the bridges' models to match the stands and their poses now.</summary>
        public void Refresh()
        {
            if (airport == null) airport = GetComponent<Airport>();
            if (airport == null || prefab == null)
            {
                Clear();
                return;
            }
            var seen = new HashSet<JetBridge>();
            foreach (var (_, list) in airport.Bridges.Layouts)
                foreach (var l in list)
                {
                    seen.Add(l.bridge);
                    if (!instances.TryGetValue(l.bridge, out var inst) || inst.root == null)
                        instances[l.bridge] = inst = Make();
                    Place(inst, l, airport.Bridges.PoseOf(l));
                }
            var gone = new List<JetBridge>();
            foreach (var kv in instances)
                if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var b in gone)
            {
                AirfieldPart.DestroyAny(instances[b].root);
                instances.Remove(b);
            }
            var seenHeads = new HashSet<BridgeHead>();
            foreach (var h in airport.Bridges.Heads)
            {
                seenHeads.Add(h.head);
                if (!headInstances.TryGetValue(h.head, out var inst) || inst.root == null)
                    headInstances[h.head] = inst = MakeHead();
                PlaceHead(inst, h);
            }
            var goneHeads = new List<BridgeHead>();
            foreach (var kv in headInstances)
                if (!seenHeads.Contains(kv.Key)) goneHeads.Add(kv.Key);
            foreach (var h in goneHeads)
            {
                AirfieldPart.DestroyAny(headInstances[h].root);
                headInstances.Remove(h);
            }
        }

        GameObject Copy(string name)
        {
            if (container == null)
            {
                container = transform.Find("Bridges");
                if (container == null)
                {
                    container = new GameObject("Bridges").transform;
                    container.SetParent(transform, false);
                    container.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                }
            }
            var go = Instantiate(prefab, container);
            go.name = name;
            go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            return go;
        }

        HeadInstance MakeHead()
        {
            var go = Copy("Bridge head");
            var t = go.transform;
            var keep = new HashSet<string>(HeadParts);
            foreach (Transform c in t)
                if (!keep.Contains(c.name)) c.gameObject.SetActive(false);
            Transform Turned(Transform end)
            {
                if (end == null) return null;
                var c = Instantiate(end.gameObject, t);
                c.name = end.name;
                c.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                return c.transform;
            }
            var head = t.Find("HeadEnd");
            var upper = t.Find("HeadUpperEnd");
            return new HeadInstance
            {
                root = go,
                head = t.Find("Head"),
                upper = t.Find("HeadUpper"),
                link = t.Find("Link"),
                ends = new[] { head, Turned(head) },
                upperEnds = new[] { upper, Turned(upper) },
                feet = { t.Find("LinkColumnFoot") },
                shafts = { t.Find("LinkColumn") },
                heads = { t.Find("LinkColumnHead") },
            };
        }

        Instance Make()
        {
            var go = Copy("Jet bridge");
            var t = go.transform;
            foreach (var n in NotOnBridges)
            {
                var part = t.Find(n);
                if (part != null) part.gameObject.SetActive(false);
            }
            return new Instance
            {
                root = go,
                rotunda = t.Find("Rotunda"),
                tunnel = new[] { t.Find("Tunnel_A"), t.Find("Tunnel_B"), t.Find("Tunnel_C") },
                shortTunnel = t.Find("ShortTunnel_A") != null ? new[] { t.Find("ShortTunnel_A"), t.Find("ShortTunnel_B"), t.Find("ShortTunnel_C") } : null,
                cab = t.Find("Cab"),
                drive = t.Find("Drive"),
                legs = t.Find("DriveLegs"),
                link = t.Find("Link"),
                feet = { t.Find("LinkColumnFoot") },
                shafts = { t.Find("LinkColumn") },
                heads = { t.Find("LinkColumnHead") },
                rotundaHigh = t.Find("RotundaHigh"),
            };
        }

        static void Set(Transform part, Vector3 position, Quaternion rotation, Vector3? scale = null)
        {
            if (part == null) return;
            part.localPosition = position;
            part.localRotation = rotation;
            part.localScale = scale ?? Vector3.one;
        }

        static Quaternion Yaw(Vector2 d) => Quaternion.LookRotation(Geo.X0Z(d), Vector3.up);

        /// <summary>Place every part, in the instance's space: on the ground under the rotunda's centre, unturned.</summary>
        static void Place(Instance inst, BridgeLayout l, BridgePose pose)
        {
            var root = inst.root.transform;
            root.localPosition = Geo.X0Z(l.rotunda);
            root.localRotation = Quaternion.identity;
            var h = pose.heading.sqrMagnitude > 1e-6f ? pose.heading.normalized : Vector2.up;
            float floor = l.bridge.Floor;
            // the raised rotunda for an upper-deck bridge (the low one stretched if the model has none)
            bool high = l.bridge.upper;
            var drum = high && inst.rotundaHigh != null ? inst.rotundaHigh : inst.rotunda;
            var other = drum == inst.rotunda ? inst.rotundaHigh : inst.rotunda;
            if (other != null) other.gameObject.SetActive(false);
            if (drum != null) drum.gameObject.SetActive(true);
            float stretch = high && drum == inst.rotunda ? floor / JetBridgeRules.RotundaFloor : 1f;
            Set(drum, Vector3.zero, Yaw(h), new Vector3(1f, stretch, 1f));

            // the tunnel: straight from the rotunda's floor at TunnelStart to the cab's pivot
            var start = Geo.X0Z(h * JetBridgeRules.TunnelStart, floor);
            var end = Geo.X0Z(h * (JetBridgeRules.TunnelStart + pose.Run), pose.cabFloor);
            var along = end - start;
            float length = along.magnitude;
            var dir = length > 1e-3f ? along / length : Geo.X0Z(h);
            var pitched = Quaternion.LookRotation(dir, Vector3.up);
            // the bridge's own sections (a model without short ones: the long ones, nested deeper)
            bool shortOne = l.bridge.tunnel == TunnelSize.Short && inst.shortTunnel != null;
            var sections = shortOne ? inst.shortTunnel : inst.tunnel;
            var hidden = shortOne ? inst.tunnel : inst.shortTunnel;
            if (hidden != null)
                foreach (var part in hidden)
                    if (part != null) part.gameObject.SetActive(false);
            float sec = shortOne ? JetBridgeRules.ShortSectionLength : JetBridgeRules.SectionLength;
            float extra = Mathf.Max(length - sec, 0f);
            for (int i = 0; i < 3; i++)
            {
                if (sections[i] != null) sections[i].gameObject.SetActive(true);
                Set(sections[i], start + dir * (extra * i * 0.5f), pitched);
            }

            // the cab on the tunnel's end, level, turned to the door
            Set(inst.cab, end, Yaw(pose.CabHeading));

            // the drive on the ground under the yoke (on the outer section, tilting with it), its legs up to the yoke
            float back = Mathf.Max(length - JetBridgeRules.DriveFromEnd, 0f);
            var yoke = start + dir * back + pitched * Vector3.up * JetBridgeRules.YokeBottom;
            var ground = new Vector3(yoke.x, 0f, yoke.z);
            Set(inst.drive, ground, Yaw(h));
            Set(inst.legs, ground + Vector3.up * JetBridgeRules.DriveTop, Yaw(h),
                new Vector3(1f, Mathf.Max(yoke.y - JetBridgeRules.DriveTop, 0.05f), 1f));

            // the fixed link from the terminal's wall (at the main floor) into the rotunda's drum (its corners meet the curved
            // wall), ramping up to a raised rotunda, on its columns; or a level stub from a little inside its head's wall
            float link = l.LinkLength;
            bool hasLink = link > 0.05f;
            var linkDir = hasLink ? (l.rotunda - l.root).normalized : h;
            float slope = l.LinkSlope;
            if (inst.link != null)
            {
                inst.link.gameObject.SetActive(hasLink);
                if (hasLink)
                {
                    float into = l.head != null ? StubIntoHead : 0f;
                    float run = link + JetBridgeRules.RotundaRadius - LinkIntoDrum + into;
                    var up = new Vector3(linkDir.x, slope, linkDir.y);
                    Set(inst.link, Geo.X0Z(l.root - linkDir * into - l.rotunda, l.LinkStartFloor), Quaternion.LookRotation(up, Vector3.up),
                        new Vector3(1f, 1f, run * up.magnitude));
                }
            }
            var spots = hasLink ? l.columns ?? l.Columns(null) : NoColumns;
            PlaceColumns(inst.feet, inst.shafts, inst.heads, spots, root, l.rotunda, linkDir, p => l.LinkFloorAt(p) + JetBridgeRules.LinkBottom, 1f);
        }

        /// <summary>Place a head and its main link, on their columns, in the instance's space: on the ground under the
        /// head's near end, unturned.</summary>
        static void PlaceHead(HeadInstance inst, HeadLayout h)
        {
            var root = inst.root.transform;
            root.localPosition = Geo.X0Z(h.a);
            root.localRotation = Quaternion.identity;
            var d = h.Dir;
            float len = h.Length, floor = JetBridgeRules.RotundaFloor;
            var along = Yaw(d);
            var back = Yaw(-d);
            Set(inst.head, Geo.X0Z(Vector2.zero, floor), along, new Vector3(1f, 1f, len));
            Set(inst.ends[0], Geo.X0Z(d * len, floor), along);
            Set(inst.ends[1], Geo.X0Z(Vector2.zero, floor), back);
            foreach (var part in new[] { inst.upper, inst.upperEnds[0], inst.upperEnds[1] })
                if (part != null) part.gameObject.SetActive(h.upper);
            if (h.upper)
            {
                Set(inst.upper, Geo.X0Z(Vector2.zero, JetBridgeRules.UpperFloor), along, new Vector3(1f, 1f, len));
                Set(inst.upperEnds[0], Geo.X0Z(d * len, JetBridgeRules.UpperFloor), along);
                Set(inst.upperEnds[1], Geo.X0Z(Vector2.zero, JetBridgeRules.UpperFloor), back);
            }
            // the main link, widened, from the terminal's wall to a little inside the head
            var linkDir = h.HasLink ? (h.a - h.root).normalized : d;
            if (inst.link != null)
            {
                inst.link.gameObject.SetActive(h.HasLink);
                if (h.HasLink)
                    Set(inst.link, Geo.X0Z(h.root - h.a, floor), Yaw(linkDir),
                        new Vector3(JetBridgeRules.MainLinkWidth / JetBridgeRules.LinkWidth, 1f, h.LinkLength + LinkIntoHead));
            }
            // its columns: under the main link (their cross-heads widened with it), then in pairs under the head
            var link = h.linkColumns ?? NoColumns;
            var pairs = h.columns ?? NoColumns;
            int n = link.Count + pairs.Count;
            Columns(inst.feet, n, root);
            Columns(inst.shafts, n, root);
            Columns(inst.heads, n, root);
            for (int i = 0; i < inst.feet.Count; i++)
            {
                bool on = i < n;
                foreach (var parts in new[] { inst.feet, inst.shafts, inst.heads })
                    if (i < parts.Count && parts[i] != null) parts[i].gameObject.SetActive(on);
                if (!on) continue;
                bool underLink = i < link.Count;
                var p = underLink ? link[i] : pairs[i - link.Count];
                float under = floor + (underLink ? JetBridgeRules.LinkBottom : JetBridgeRules.HeadBottom);
                PlaceColumn(inst.feet[i], inst.shafts[i], inst.heads[i], Geo.X0Z(p - h.a), under, Yaw(underLink ? linkDir : d),
                    underLink ? JetBridgeRules.MainLinkWidth / JetBridgeRules.LinkWidth : 1f);
            }
        }

        /// <summary>A link's columns at `spots` (ground points) under it, its underside over each given by `under`.</summary>
        static void PlaceColumns(List<Transform> feet, List<Transform> shafts, List<Transform> heads, List<Vector2> spots, Transform root,
            Vector2 origin, Vector2 dir, System.Func<Vector2, float> under, float widen)
        {
            Columns(feet, spots.Count, root);
            Columns(shafts, spots.Count, root);
            Columns(heads, spots.Count, root);
            for (int i = 0; i < feet.Count; i++)
            {
                bool on = i < spots.Count;
                foreach (var parts in new[] { feet, shafts, heads })
                    if (i < parts.Count && parts[i] != null) parts[i].gameObject.SetActive(on);
                if (on) PlaceColumn(feet[i], shafts[i], heads[i], Geo.X0Z(spots[i] - origin), under(spots[i]), Yaw(dir), widen);
            }
        }

        /// <summary>One column: its foot on the ground at `at`, its head under a link (or head) whose underside is
        /// `under` high, its shaft stretched between them.</summary>
        static void PlaceColumn(Transform foot, Transform shaft, Transform head, Vector3 at, float under, Quaternion yaw, float widen)
        {
            Set(foot, at, yaw);
            Set(shaft, at + Vector3.up * ShaftBase, yaw, new Vector3(1f, Mathf.Max(under - ShaftUnderLink - ShaftBase, 0.05f), 1f));
            Set(head, at + Vector3.up * under, yaw, new Vector3(widen, 1f, 1f));
        }

        /// <summary>At least `count` of a column part (copies of the model's own).</summary>
        static void Columns(List<Transform> parts, int count, Transform root)
        {
            var template = parts.Count > 0 ? parts[0] : null;
            if (template == null) return;
            while (parts.Count < count)
            {
                var c = Instantiate(template.gameObject, root);
                c.name = template.name;
                c.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                parts.Add(c.transform);
            }
        }

        void Clear()
        {
            foreach (var inst in instances.Values)
                if (inst.root != null) AirfieldPart.DestroyAny(inst.root);
            instances.Clear();
            foreach (var inst in headInstances.Values)
                if (inst.root != null) AirfieldPart.DestroyAny(inst.root);
            headInstances.Clear();
            if (container != null) AirfieldPart.DestroyAny(container.gameObject);
            container = null;
        }
    }
}
