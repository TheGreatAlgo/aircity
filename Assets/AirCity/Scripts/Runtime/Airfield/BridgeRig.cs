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
        public Shader surfaceShader;
        public List<JetBridgeDefinition> definitions = new List<JetBridgeDefinition>();
        public List<JetBridgeAppearance> appearances = new List<JetBridgeAppearance>();
        public IEnumerable<JetBridgeDefinition> Definitions
        {
            get
            {
                foreach (var d in definitions) if (d != null) yield return d;
                var type = prefab != null ? prefab.GetComponent<Vehicles.VehicleLights>()?.type : null;
                if (type != null && type.bridgeDefinitions != null)
                    foreach (var d in type.bridgeDefinitions) if (d != null && !definitions.Contains(d)) yield return d;
            }
        }
        Stand previewStand;
        Aircraft.AircraftType previewType;
        float previewProgress = 1f;
        public void Preview(Stand stand, Aircraft.AircraftType type, float progress = 1f)
        {
            previewStand = stand; previewType = type; previewProgress = Mathf.Clamp01(progress);
            Refresh();
        }

        public void RefreshAppearance(JetBridgeAppearance appearance)
        {
            foreach (var instance in instances.Values) if (instance.appearance == appearance) instance.assetVersion = -1;
            foreach (var instance in headInstances.Values) if (instance.appearance == appearance) instance.assetVersion = -1;
            Refresh();
        }

        sealed class Instance
        {
            public GameObject root, source;
            public BridgeSpecification specification;
            public JetBridgeAppearance appearance;
            public int version = -1;
            public int assetVersion = -1;
            public BridgePose lastPose;
            public Transform column, highColumn;
            public readonly CorridorParts corridor = new CorridorParts();
            public Transform rotunda, rotundaHigh, cab, drive, legs, link;
            /// <summary>The long tunnel's sections, and the short one's (null in a model built before it had them).</summary>
            public Transform[] tunnel, shortTunnel;
            /// <summary>The link's columns' feet, shafts and heads (the first of each is the model's own).</summary>
            public readonly List<Transform> feet = new List<Transform>(), shafts = new List<Transform>(), heads = new List<Transform>();
        }

        sealed class HeadInstance
        {
            public GameObject root;
            public AirfieldPart building;
            public JetBridgeAppearance appearance;
            public int assetVersion;
            public Transform head, upper, link;
            public int version = -1;
            public readonly CorridorParts corridor = new CorridorParts();
            /// <summary>The storeys' end walls: at the far end, and turned round at the near end.</summary>
            public Transform[] ends, upperEnds;
            public readonly List<Transform> feet = new List<Transform>(), shafts = new List<Transform>(), heads = new List<Transform>();
        }

        /// <summary>A fixed corridor's segments: copies of the model's Link (the first is the model's own), each with its
        /// own mesh bent to its segment (CorridorMesh), made again only when the corridor does.</summary>
        sealed class CorridorParts
        {
            public readonly List<Transform> links = new List<Transform>();
            public readonly List<Mesh> meshes = new List<Mesh>();
            public Mesh source;
            public bool sourceFound;
            public BridgeCorridor placed;
            public void Destroy()
            {
                foreach (var mesh in meshes) AirfieldPart.DestroyAny(mesh);
                meshes.Clear(); placed = null;
            }
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
        readonly Dictionary<Material, Material> surfaceMaterials = new Dictionary<Material, Material>();
        Instance placementInstance;
        public GameObject PlacementPreviewObject => placementInstance?.root;
        Transform container;
        Airport airport;

        /// <summary>Pick actual bridge equipment directly, including its tunnel and fixed corridor. Preview models
        /// are kept separately and cannot be selected as saved equipment.</summary>
        public bool TryPickBridge(Ray ray, out Stand stand, out JetBridge bridge)
        {
            stand = null; bridge = null;
            if (airport == null) airport = GetComponent<Airport>();
            if (airport == null) return false;
            float nearest = float.MaxValue;
            foreach (var (sl, layouts) in airport.Bridges.Layouts)
                foreach (var layout in layouts)
                {
                    if (!instances.TryGetValue(layout.bridge, out var instance) || instance.root == null) continue;
                    foreach (var renderer in instance.root.GetComponentsInChildren<Renderer>())
                    {
                        if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                            !renderer.bounds.IntersectRay(ray, out float distance) || distance >= nearest) continue;
                        nearest = distance; stand = sl.stand; bridge = layout.bridge;
                    }
                }
            return bridge != null;
        }

        /// <summary>Visual-only equipment under the build cursor; never enters the airport's saved layout or simulation.</summary>
        public void ShowPlacement(BridgeLayout layout, BridgePose pose, bool valid)
        {
            if (layout == null || prefab == null) { ClearPlacement(); return; }
            var bridge = layout.bridge;
            var source = bridge.definition != null && bridge.definition.prefab != null ? bridge.definition.prefab : prefab;
            if (placementInstance == null || placementInstance.root == null || placementInstance.source != source ||
                placementInstance.specification != bridge.Spec || placementInstance.appearance != bridge.appearance ||
                placementInstance.assetVersion != BridgeAssetRevision.Version)
            {
                ClearPlacement();
                placementInstance = Make(bridge, source, true);
                placementInstance.root.name = "Bridge placement preview";
                foreach (var behaviour in placementInstance.root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var collider in placementInstance.root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            }
            Place(placementInstance, layout, pose);
            var block = new MaterialPropertyBlock();
            foreach (var renderer in placementInstance.root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", valid ? new Color(.72f, 1f, .85f) : new Color(1f, .3f, .25f));
                renderer.SetPropertyBlock(block);
            }
        }

        public void ClearPlacement()
        {
            if (placementInstance?.root != null) placementInstance.root.SetActive(false);
            if (placementInstance != null) Discard(placementInstance);
            placementInstance = null;
        }

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
                    var source = l.bridge.definition != null && l.bridge.definition.prefab != null ? l.bridge.definition.prefab : prefab;
                    if (!instances.TryGetValue(l.bridge, out var inst) || inst.root == null || inst.source != source ||
                        inst.specification != l.bridge.Spec || inst.appearance != l.bridge.appearance || inst.assetVersion != BridgeAssetRevision.Version)
                    {
                        if (inst != null) Discard(inst);
                        instances[l.bridge] = inst = Make(l.bridge, source);
                    }
                    var pose = airport.Bridges.PoseOf(l);
                    if (l.stand.stand == previewStand && previewType != null)
                    {
                        var entry = airport.Bridges.ServicePlan(previewStand, previewType)[l.bridge];
                        pose = entry.Operates ? BridgePose.Move(entry.parked, entry.docked, previewProgress) : entry.parked;
                    }
                    if (inst.version != airport.LayoutVersion || !pose.Equals(inst.lastPose))
                    {
                        Place(inst, l, pose);
                        inst.version = airport.LayoutVersion; inst.lastPose = pose;
                    }
                }
            var gone = new List<JetBridge>();
            foreach (var kv in instances)
                if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var b in gone)
            {
                Discard(instances[b]);
                instances.Remove(b);
            }
            var seenHeads = new HashSet<BridgeHead>();
            foreach (var h in airport.Bridges.Heads)
            {
                seenHeads.Add(h.head);
                if (!headInstances.TryGetValue(h.head, out var inst) || inst.root == null || inst.appearance != h.head.appearance ||
                    inst.assetVersion != BridgeAssetRevision.Version)
                {
                    if (inst != null) Discard(inst);
                    headInstances[h.head] = inst = MakeHead(h.head.appearance);
                }
                if (inst.version != airport.LayoutVersion)
                {
                    PlaceHead(inst, h);
                    inst.version = airport.LayoutVersion;
                }
            }
            var goneHeads = new List<BridgeHead>();
            foreach (var kv in headInstances)
                if (!seenHeads.Contains(kv.Key)) goneHeads.Add(kv.Key);
            foreach (var h in goneHeads)
            {
                Discard(headInstances[h]);
                headInstances.Remove(h);
            }
        }

        GameObject Copy(string name, GameObject source = null, bool visualOnly = false)
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
            GameObject inactive = null;
            if (visualOnly)
            {
                inactive = new GameObject("Build preview staging") { hideFlags = HideFlags.DontSave };
                inactive.SetActive(false);
                inactive.transform.SetParent(container, false);
            }
            var go = Instantiate(source != null ? source : prefab, inactive != null ? inactive.transform : container);
            if (inactive != null)
            {
                go.SetActive(false);
                foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                go.transform.SetParent(container, false);
                AirfieldPart.DestroyAny(inactive);
                go.SetActive(true);
            }
            go.name = name;
            go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            var shader = surfaceShader;
            var lights = prefab != null ? prefab.GetComponent<Vehicles.VehicleLights>() : null;
            if (shader == null && lights != null && lights.type != null) shader = lights.type.bridgeShader;
            if (shader != null)
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var original = materials[i];
                        if (original == null || original.shader == shader || original.shader.name != "Universal Render Pipeline/Lit") continue;
                        if (!surfaceMaterials.TryGetValue(original, out var material))
                        {
                            // Keep the generated atlas material intact. Bind the appearance shader on the main
                            // thread, after shader import, and share one converted material across every gate.
                            material = new Material(shader) { name = original.name + " bridge surface", hideFlags = HideFlags.DontSave };
                            material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                            material.SetTexture("_MetallicGlossMap", original.GetTexture("_MetallicGlossMap"));
                            material.SetColor("_BaseColor", Color.white);
                            material.SetColor("_CladdingTint", Color.white); material.SetColor("_GlassTint", Color.white);
                            material.SetColor("_RoofTint", Color.white); material.SetFloat("_GlassAmount", 1f);
                            surfaceMaterials[original] = material;
                        }
                        materials[i] = material;
                    }
                    renderer.sharedMaterials = materials;
                }
            return go;
        }

        HeadInstance MakeHead(JetBridgeAppearance appearance)
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
            var instance = new HeadInstance
            {
                root = go,
                appearance = appearance, assetVersion = BridgeAssetRevision.Version,
                head = t.Find("Head"),
                upper = t.Find("HeadUpper"),
                link = t.Find("Link"),
                corridor = { links = { t.Find("Link") } },
                ends = new[] { head, Turned(head) },
                upperEnds = new[] { upper, Turned(upper) },
                feet = { t.Find("LinkColumnFoot") },
                shafts = { t.Find("LinkColumn") },
                heads = { t.Find("LinkColumnHead") },
            };
            ApplyAppearance(t, appearance);
            return instance;
        }

        Instance Make(JetBridge bridge, GameObject source, bool visualOnly = false)
        {
            var go = Copy("Jet bridge", source, visualOnly);
            var t = go.transform;
            foreach (Transform part in t)
                if (System.Array.IndexOf(NotOnBridges, part.name) >= 0 || part.name.StartsWith("Tunnel_") || part.name.StartsWith("ShortTunnel_"))
                    part.gameObject.SetActive(false);
            var sections = new Transform[bridge.Spec.sections.Length];
            for (int i = 0; i < sections.Length; i++) sections[i] = t.Find(bridge.Spec.sections[i].part);
            var inst = new Instance
            {
                root = go, source = source, specification = bridge.Spec, appearance = bridge.appearance, assetVersion = BridgeAssetRevision.Version,
                rotunda = t.Find("Rotunda"), rotundaHigh = t.Find("RotundaHigh"),
                column = t.Find("RotundaColumn"), highColumn = t.Find("RotundaHighColumn"),
                tunnel = sections, cab = t.Find("Cab"), drive = t.Find("Drive"), legs = t.Find("DriveLegs"),
                link = t.Find("Link"), corridor = { links = { t.Find("Link") } },
                feet = { t.Find("LinkColumnFoot") }, shafts = { t.Find("LinkColumn") }, heads = { t.Find("LinkColumnHead") },
            };
            ApplyAppearance(t, bridge.appearance);
            return inst;
        }

        static void ApplyAppearance(Transform root, JetBridgeAppearance appearance)
        {
            if (appearance == null) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                block.Clear();
                block.SetColor("_CladdingTint", appearance.cladding);
                block.SetColor("_GlassTint", appearance.glazing);
                block.SetColor("_RoofTint", appearance.roof);
                block.SetFloat("_GlassAmount", appearance.glazingAmount);
                renderer.SetPropertyBlock(block);
            }
            foreach (var style in appearance.parts)
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!string.IsNullOrEmpty(style.part) && renderer.transform.name != style.part) continue;
                    if (style.material != null) renderer.sharedMaterial = style.material;
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", style.tint);
                    renderer.SetPropertyBlock(block);
                }
            foreach (var attachment in appearance.attachments)
            {
                var part = root.Find(attachment.part);
                if (part == null || attachment.prefab == null) continue;
                var go = Instantiate(attachment.prefab, part);
                go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                go.transform.SetLocalPositionAndRotation(attachment.position, Quaternion.Euler(attachment.rotation));
                go.transform.localScale = attachment.scale;
            }
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
            var spec = l.bridge.Spec;
            // the raised rotunda for an upper-deck bridge (the low one stretched if the model has none)
            bool high = floor > spec.rotundaFloor + JetBridgeRules.UpperDeckAbove;
            var drum = high && inst.rotundaHigh != null ? inst.rotundaHigh : inst.rotunda;
            var other = drum == inst.rotunda ? inst.rotundaHigh : inst.rotunda;
            if (other != null) other.gameObject.SetActive(false);
            if (drum != null) drum.gameObject.SetActive(true);
            float modelFloor = drum == inst.rotundaHigh ? spec.upperFloor : spec.rotundaFloor;
            var column = drum == inst.rotundaHigh ? inst.highColumn : inst.column;
            var hiddenColumn = drum == inst.rotundaHigh ? inst.column : inst.highColumn;
            if (hiddenColumn != null) hiddenColumn.gameObject.SetActive(false);
            if (column != null)
            {
                column.gameObject.SetActive(true);
                Set(column, Vector3.zero, Quaternion.identity, new Vector3(1f, (floor - 0.4f) / (modelFloor - 0.4f), 1f));
                Set(drum, Vector3.up * (floor - modelFloor), Yaw(h));
            }
            else Set(drum, Vector3.zero, Yaw(h), new Vector3(1f, floor / modelFloor, 1f));

            var start = Geo.X0Z(h * spec.tunnelStart, floor);
            var end = Geo.X0Z(h * (spec.tunnelStart + pose.Run), pose.cabFloor);
            var along = end - start;
            float length = along.magnitude;
            var dir = length > 1e-3f ? along / length : Geo.X0Z(h);
            var pitched = Quaternion.LookRotation(dir, Vector3.up);
            float lastLength = spec.sections[spec.sections.Length - 1].length;
            float extension = Mathf.Max(length - lastLength, 0f);
            float capacity = spec.MaxTunnel - lastLength;
            float offset = 0f;
            for (int i = 0; i < inst.tunnel.Length; i++)
            {
                var part = inst.tunnel[i];
                if (part != null) part.gameObject.SetActive(true);
                Set(part, start + dir * offset, pitched, new Vector3(1f, 1f, spec.sections[i].length / spec.sections[i].modelLength));
                offset += (spec.sections[i].length - spec.minOverlap) * extension / Mathf.Max(capacity, 0.01f);
            }

            // the cab on the tunnel's end, level, turned to the door
            Set(inst.cab, end, Yaw(pose.CabHeading));

            // the drive on the ground under the yoke (on the outer section, tilting with it), its legs up to the yoke
            float back = Mathf.Max(length - spec.driveFromEnd, 0f);
            var yoke = start + dir * back + pitched * Vector3.up * spec.yokeBottom;
            var ground = new Vector3(yoke.x, 0f, yoke.z);
            Set(inst.drive, ground, Yaw(h));
            Set(inst.legs, ground + Vector3.up * spec.driveTop, Yaw(h),
                new Vector3(1f, Mathf.Max(yoke.y - spec.driveTop, 0.05f), 1f));

            // the fixed link from inside the terminal's wall (at its boarding floor) into the rotunda's drum (its corners
            // meet the curved wall), ramping up to a raised rotunda, on its columns; or a level stub from inside its head's wall
            PlaceCorridor(inst.corridor, l.Corridor, root, l.rotunda, spec.linkWidth, l.head != null ? l.head.Building : l.terminal,
                l.head != null ? StubIntoHead : 0f, spec.rotundaRadius - LinkIntoDrum);
            var spots = l.columns ?? l.Columns(null) ?? NoColumns;
            PlaceColumns(inst.feet, inst.shafts, inst.heads, spots, root, l.rotunda, h,
                p => l.LinkFloorAt(p) + spec.linkBottom, 1f, l.Corridor.DirectionAt);
        }

        /// <summary>Place a head and its main link, on their columns, in the instance's space: on the ground under the
        /// head's near end, unturned.</summary>
        void PlaceHead(HeadInstance inst, HeadLayout h)
        {
            var root = inst.root.transform;
            root.localPosition = Geo.X0Z(h.a);
            root.localRotation = Quaternion.identity;
            var d = h.Dir;
            float len = h.Length, floor = h.Floor, width = h.Width / JetBridgeRules.HeadWidth;
            var along = Yaw(d);
            var back = Yaw(-d);
            bool procedural = h.Grounded || h.head.Exterior.usesStyle || h.head.Exterior.roofShape.MaxRise > 0f;
            foreach (var part in new[] { inst.head, inst.ends[0], inst.ends[1] })
                if (part != null) part.gameObject.SetActive(!procedural);
            if (procedural)
            {
                var body = h.Building.Clone();
                body.corners = body.corners.ConvertAll(p => p - h.a);
                float baseHeight = h.Grounded ? 0f : h.Floor + h.Bottom;
                if (!h.Grounded)
                {
                    // Elevated heads retain their mechanical envelope and support positions.
                    body.exterior.facade.lean = 0f;
                    foreach (var face in body.exterior.faces) face.facade.lean = 0f;
                    body.storeyHeights = h.upper ? new List<float> { h.UpperFloor - baseHeight, JetBridgeRules.HeadTop - body.exterior.parapet } :
                        new List<float> { h.Floor - baseHeight + JetBridgeRules.HeadTop - body.exterior.parapet };
                    body.floors = body.storeyHeights.Count; body.boardingHeight = h.Floor - baseHeight;
                }
                if (!h.head.Exterior.usesStyle && h.head.appearance != null)
                {
                    var appearance = h.head.appearance;
                    void Tint(BuildingFacade face)
                    { face.cladding *= appearance.cladding; face.glassTint *= appearance.glazing; face.glazing *= appearance.glazingAmount; }
                    Tint(body.exterior.facade);
                    foreach (var face in body.exterior.faces) Tint(face.facade);
                    body.exterior.roof *= appearance.roof;
                }
                inst.building ??= new AirfieldPart(root, "Concourse building");
                var builder = new TerminalBuilder(); builder.Build(body);
                inst.building.Set(builder.mesh.Apply(inst.building.Target, "Concourse building", false, 2), airport.building);
                inst.building.Renderer.transform.localPosition = Vector3.up * baseHeight;
                inst.building.Renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            else { inst.building?.Destroy(); inst.building = null; }
            Set(inst.head, Geo.X0Z(Vector2.zero, floor), along, new Vector3(width, h.LowerScale, len));
            Set(inst.ends[0], Geo.X0Z(d * len, floor), along, new Vector3(width, h.LowerScale, 1f));
            Set(inst.ends[1], Geo.X0Z(Vector2.zero, floor), back, new Vector3(width, h.LowerScale, 1f));
            foreach (var part in new[] { inst.upper, inst.upperEnds[0], inst.upperEnds[1] })
                if (part != null) part.gameObject.SetActive(h.upper && !procedural);
            if (h.upper)
            {
                float upperWidth = (h.Width + JetBridgeRules.HeadUpperWider) / (JetBridgeRules.HeadWidth + JetBridgeRules.HeadUpperWider);
                Set(inst.upper, Geo.X0Z(Vector2.zero, h.UpperFloor), along, new Vector3(upperWidth, 1f, len));
                Set(inst.upperEnds[0], Geo.X0Z(d * len, h.UpperFloor), along, new Vector3(upperWidth, 1f, 1f));
                Set(inst.upperEnds[1], Geo.X0Z(Vector2.zero, h.UpperFloor), back, new Vector3(upperWidth, 1f, 1f));
            }
            var linkDir = h.HasLink ? (h.a - h.root).normalized : d;
            PlaceCorridor(inst.corridor, h.Corridor, root, h.a, JetBridgeRules.LinkWidth, h.parent != null ? h.parent.Building : h.terminal,
                0f, LinkIntoHead);
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
                float under = underLink ? h.Corridor.FloorAt(p) + JetBridgeRules.LinkBottom : floor + h.Bottom;
                PlaceColumn(inst.feet[i], inst.shafts[i], inst.heads[i], Geo.X0Z(p - h.a), under, Yaw(underLink ? h.Corridor.DirectionAt(p) : d),
                    underLink ? JetBridgeRules.MainLinkWidth / JetBridgeRules.LinkWidth : 1f);
            }
        }

        /// <summary>A fixed corridor's segments, in the instance's space (`origin` its ground point): bent to their
        /// segments and mitred at the corners, the first reaching back into `wall` (at least `intoStart`) so it is
        /// buried in the facade, the last `intoEnd` past its end. A model whose Link can't be bent is stretched.</summary>
        static void PlaceCorridor(CorridorParts parts, BridgeCorridor corridor, Transform root, Vector2 origin,
            float modelWidth, Terminal wall, float intoStart, float intoEnd)
        {
            if (parts.placed == corridor) return;
            parts.placed = corridor;
            var links = parts.links;
            if (!parts.sourceFound && links.Count > 0 && links[0] != null)
            {
                var filter = links[0].GetComponent<MeshFilter>();
                if (filter != null) parts.source = filter.sharedMesh;
                parts.sourceFound = true;
            }
            bool bend = CorridorMesh.CanBend(parts.source);
            intoStart = Mathf.Max(intoStart, corridor.IntoWall(wall));
            int count = corridor.points.Count - 1;
            Columns(links, count, root);
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i] == null) continue;
                links[i].gameObject.SetActive(i < count);
                if (i >= count) continue;
                var a = corridor.points[i] - Geo.X0Z(origin);
                var b = corridor.points[i + 1] - Geo.X0Z(origin);
                if (bend)
                {
                    var ground = Geo.XZ(b - a);
                    Set(links[i], a, Yaw(ground.sqrMagnitude > 1e-8f ? ground : Vector2.up));
                    while (parts.meshes.Count <= i) parts.meshes.Add(new Mesh { name = "Corridor segment", hideFlags = HideFlags.DontSave });
                    var (start, end) = CorridorMesh.Cuts(corridor.points, i, corridor.width, intoStart, intoEnd);
                    CorridorMesh.Bend(parts.source, parts.meshes[i], ground.magnitude, b.y - a.y, corridor.width / modelWidth, start, end);
                    var filter = links[i].GetComponent<MeshFilter>();
                    if (filter != null) filter.sharedMesh = parts.meshes[i];
                    continue;
                }
                var dir = (b - a).normalized;
                float before = i == 0 ? intoStart : corridor.width * 0.5f;
                float after = i == count - 1 ? intoEnd : corridor.width * 0.5f;
                Set(links[i], a - dir * before, Quaternion.LookRotation(dir, Vector3.up),
                    new Vector3(corridor.width / modelWidth, 1f, Vector3.Distance(a, b) + before + after));
            }
        }

        /// <summary>A link's columns at `spots` (ground points) under it, its underside over each given by `under`.</summary>
        static void PlaceColumns(List<Transform> feet, List<Transform> shafts, List<Transform> heads, List<Vector2> spots, Transform root,
            Vector2 origin, Vector2 dir, System.Func<Vector2, float> under, float widen, System.Func<Vector2, Vector2> direction = null)
        {
            Columns(feet, spots.Count, root);
            Columns(shafts, spots.Count, root);
            Columns(heads, spots.Count, root);
            for (int i = 0; i < feet.Count; i++)
            {
                bool on = i < spots.Count;
                foreach (var parts in new[] { feet, shafts, heads })
                    if (i < parts.Count && parts[i] != null) parts[i].gameObject.SetActive(on);
                if (on) PlaceColumn(feet[i], shafts[i], heads[i], Geo.X0Z(spots[i] - origin), under(spots[i]), Yaw(direction != null ? direction(spots[i]) : dir), widen);
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
                var from = template.GetComponent<Renderer>();
                var to = c.GetComponent<Renderer>();
                if (from != null && to != null)
                {
                    var block = new MaterialPropertyBlock();
                    from.GetPropertyBlock(block);
                    to.SetPropertyBlock(block);
                }
                parts.Add(c.transform);
            }
        }

        static void Discard(Instance inst)
        {
            inst.corridor.Destroy();
            if (inst.root != null) AirfieldPart.DestroyAny(inst.root);
        }

        static void Discard(HeadInstance inst)
        {
            inst.building?.Destroy();
            inst.corridor.Destroy();
            if (inst.root != null) AirfieldPart.DestroyAny(inst.root);
        }

        void Clear()
        {
            ClearPlacement();
            foreach (var inst in instances.Values) Discard(inst);
            instances.Clear();
            foreach (var inst in headInstances.Values) Discard(inst);
            headInstances.Clear();
            foreach (var material in surfaceMaterials.Values) AirfieldPart.DestroyAny(material);
            surfaceMaterials.Clear();
            if (container != null) AirfieldPart.DestroyAny(container.gameObject);
            container = null;
        }
    }
}
