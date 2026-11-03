using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Mechanical presentation only. Containers and leases belong to AirportBaggage.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ContainerEquipmentRig : MonoBehaviour
    {
        public VehicleType type;
        public float mainHeight = 0.508f, frontHeight = 1.8f;
        [Range(0f, 1f)] public float stabilisers, curtainOpen;
        public float deckAngle;
        public bool restraintsReleased, sideGateOpen, bridgeExtended, displayLoad;
        [Serializable] sealed class Rest
        {
            public string name;
            public Transform part;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        [SerializeField, HideInInspector] Rest[] references = Array.Empty<Rest>();
        [SerializeField, HideInInspector] VehicleType referenceType;
        readonly Dictionary<string, Rest> parts = new Dictionary<string, Rest>();
        float previousMain, previousFront, previousStabilisers, previousCurtain, previousDeck;
        bool applied, previousReleased, previousGate, previousBridge, previousLoad;

        void OnEnable() => Apply();
        void Update()
        {
            if (!applied || referenceType != type || previousMain != mainHeight || previousFront != frontHeight ||
                previousStabilisers != stabilisers || previousCurtain != curtainOpen || previousDeck != deckAngle ||
                previousReleased != restraintsReleased || previousGate != sideGateOpen || previousBridge != bridgeExtended || previousLoad != displayLoad) Apply();
        }

        void FindParts()
        {
            parts.Clear();
            if (referenceType == type && references.Length > 0)
            { foreach (var rest in references) if (rest.part != null) parts[rest.name] = rest; return; }
            var found = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) found[t.name] = t;
            void Parent(string child, string parent)
            { if (found.TryGetValue(child, out var c) && found.TryGetValue(parent, out var p)) c.SetParent(p, true); }
            if (type.kind == VehicleKind.ContainerDolly)
                foreach (string name in new[] { "Stop_L", "Stop_R", "EndStop_F", "EndStop_R" }) Parent(name, "Deck");
            if (type.kind == VehicleKind.ContainerLoader)
                foreach (string prefix in new[] { "MainPlatform", "FrontPlatform" })
                    foreach (string suffix in new[] { "StopFront", "StopRear", "Gate" }) Parent(prefix + suffix, prefix);
            foreach (var pair in found)
                parts[pair.Key] = new Rest { name = pair.Key, part = pair.Value,
                    position = transform.InverseTransformPoint(pair.Value.position),
                    rotation = Quaternion.Inverse(transform.rotation) * pair.Value.rotation, scale = pair.Value.localScale };
            references = new List<Rest>(parts.Values).ToArray(); referenceType = type;
        }

        public void Apply()
        {
            if (type == null || type.container == null) return;
            if (parts.Count == 0 || referenceType != type) FindParts();
            var c = type.container;
            if (type.kind == VehicleKind.ContainerLoader)
            {
                mainHeight = Mathf.Clamp(mainHeight, c.mainMinHeight, c.mainMaxHeight);
                frontHeight = Mathf.Clamp(frontHeight, c.frontMinHeight, c.frontMaxHeight);
                Platform("MainPlatform", c.mainTransfer.y, mainHeight, c.mainLift);
                Platform("FrontPlatform", c.frontTransfer.y, frontHeight, c.frontLift);
                for (int k = 0; k < 6; k++)
                    if (parts.TryGetValue("Stabiliser_" + k, out var leg)) Pose(leg, leg.position - Vector3.up * (0.20f * stabilisers), leg.rotation);
                Scale("MainPlatformGate", sideGateOpen ? 0.035f : 1f);
                if (parts.TryGetValue("SideBridge", out var bridge))
                    Pose(bridge, bridge.position + Vector3.up * (mainHeight - c.mainTransfer.y - (bridgeExtended ? 0f : 0.14f)) -
                        Vector3.right * (bridgeExtended ? c.sideBridgeReach : 0f), bridge.rotation);
                foreach (string prefix in new[] { "MainPlatform", "FrontPlatform" })
                    foreach (string suffix in new[] { "StopFront", "StopRear" }) Scale(prefix + suffix, restraintsReleased ? 0.03f : 1f);
            }
            else if (type.kind == VehicleKind.ContainerDolly)
            {
                if (parts.TryGetValue("Deck", out var deck)) Pose(deck, deck.position, Quaternion.AngleAxis(deckAngle, Vector3.up) * deck.rotation);
                foreach (string name in new[] { "Stop_L", "Stop_R", "EndStop_F", "EndStop_R" }) Scale(name, restraintsReleased ? 0.03f : 1f);
            }
            else if (type.kind == VehicleKind.ContainerULD)
            {
                Scale("Curtain", Mathf.Lerp(1f, 0.035f, curtainOpen));
                if (parts.TryGetValue("BagLoad", out var load)) load.part.gameObject.SetActive(displayLoad && curtainOpen > 0f);
            }
            previousMain = mainHeight; previousFront = frontHeight; previousStabilisers = stabilisers;
            previousCurtain = curtainOpen; previousDeck = deckAngle; previousReleased = restraintsReleased;
            previousGate = sideGateOpen; previousBridge = bridgeExtended; previousLoad = displayLoad; applied = true;
            var lods = GetComponent<LODGroup>(); if (lods != null) lods.RecalculateBounds();
        }

        void Platform(string prefix, float restHeight, float height, BaggageLiftDimensions lift)
        {
            if (parts.TryGetValue(prefix, out var platform)) Pose(platform, platform.position + Vector3.up * (height - restHeight), platform.rotation);
            if (lift == null || lift.stages < 1) return;
            float step = (height - 0.17f - lift.@base) / lift.stages;
            float restStep = (restHeight - 0.17f - lift.@base) / lift.stages;
            float span = Mathf.Sqrt(Mathf.Max(0f, lift.length * lift.length - step * step));
            float restSpan = Mathf.Sqrt(Mathf.Max(0f, lift.length * lift.length - restStep * restStep));
            for (int stage = 0; stage < lift.stages; stage++)
                for (int side = 0; side < 2; side++)
                    if (parts.TryGetValue(prefix + "Lift_" + (stage * 2 + side), out var arm))
                    {
                        float sign = side == 0 ? 1f : -1f;
                        float angle = Mathf.Atan2(step, sign * span) - Mathf.Atan2(restStep, sign * restSpan);
                        Pose(arm, new Vector3(0f, lift.@base + stage * step, lift.centre - sign * span * 0.5f),
                            Quaternion.AngleAxis(-angle * Mathf.Rad2Deg, Vector3.right) * arm.rotation);
                    }
        }
        void Scale(string name, float y)
        { if (parts.TryGetValue(name, out var part)) part.part.localScale = new Vector3(part.scale.x, part.scale.y * y, part.scale.z); }
        void Pose(Rest part, Vector3 p, Quaternion q) => part.part.SetPositionAndRotation(transform.TransformPoint(p), transform.rotation * q);
    }
}
