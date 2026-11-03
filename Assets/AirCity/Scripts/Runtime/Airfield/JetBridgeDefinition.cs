using System;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>A bridge family. The same dimensions drive docking, clearances and the visual rig.</summary>
    [CreateAssetMenu(menuName = "AirCity/Jet bridge definition")]
    public sealed class JetBridgeDefinition : ScriptableObject
    {
        public GameObject prefab;
        public BridgeSpecification specification = new BridgeSpecification();
        void OnValidate() => BridgeAssetRevision.Touch();
    }

    public static class BridgeAssetRevision
    {
        public static int Version { get; private set; }
        public static void Touch() => Version++;
    }

    [Serializable]
    public sealed class BridgeTunnelSection
    {
        public string part;
        [Min(1f)] public float length = 13f;
        [Min(1f)] public float modelLength = 13f;
        [Min(0.5f)] public float width = 3.1f;
        public float bottom = -0.55f, top = 2.85f;
    }

    [Serializable]
    public sealed class BridgeSpecification
    {
        public float rotundaFloor = 4f, upperFloor = 7.8f, rotundaRadius = 2.3f, rotundaTop = 3.26f;
        public float tunnelStart = 2.3f, minOverlap = 1.5f, retractedAllowance = 1f;
        public BridgeTunnelSection[] sections = Sections(false);
        public float cabReach = 2.6f, cabWidth = 3.4f, cabBottom = -0.75f, cabTop = 3.18f;
        public float driveFromEnd = 4f, driveTop = 1f, yokeBottom = -0.95f;
        public float linkWidth = 2.6f, linkBottom = -0.35f, linkTop = 2.75f;
        public float maxSlope = 0.1f, maxCabTurn = 95f, minCabFloor = 1f, maxCabFloor = 12f;
        public float dockTime = 50f;

        static BridgeTunnelSection[] Sections(bool shortOne)
        {
            float length = shortOne ? 8.5f : 13f;
            string prefix = shortOne ? "ShortTunnel_" : "Tunnel_";
            return new[]
            {
                new BridgeTunnelSection { part = prefix + "A", length = length, modelLength = length, width = 2.5f, bottom = -0.25f, top = 2.55f },
                new BridgeTunnelSection { part = prefix + "B", length = length, modelLength = length, width = 2.8f, bottom = -0.4f, top = 2.7f },
                new BridgeTunnelSection { part = prefix + "C", length = length, modelLength = length },
            };
        }

        public static readonly BridgeSpecification Long = new BridgeSpecification();
        public static readonly BridgeSpecification Short = new BridgeSpecification { sections = Sections(true) };
        public float MinTunnel
        {
            get { float n = 0f; foreach (var s in sections) n = Mathf.Max(n, s.length); return n + retractedAllowance; }
        }
        public float MaxTunnel
        {
            get { float n = 0f; foreach (var s in sections) n += s.length; return n - minOverlap * (sections.Length - 1); }
        }
        public float TunnelWidth
        {
            get { float n = 0f; foreach (var s in sections) n = Mathf.Max(n, s.width); return n; }
        }
        public float TunnelBottom
        {
            get { float n = float.MaxValue; foreach (var s in sections) n = Mathf.Min(n, s.bottom); return n; }
        }
        public float TunnelTop
        {
            get { float n = float.MinValue; foreach (var s in sections) n = Mathf.Max(n, s.top); return n; }
        }

        public bool Valid
        {
            get
            {
                if (sections == null || sections.Length < 2 || sections.Length > 6 || minOverlap <= 0f ||
                    rotundaRadius <= 0f || tunnelStart <= 0f || cabReach <= 0f || cabWidth <= 0f ||
                    linkWidth <= 0f || maxSlope <= 0f || maxCabTurn <= 0f || dockTime <= 0f ||
                    minCabFloor >= maxCabFloor) return false;
                foreach (var s in sections)
                    if (s == null || string.IsNullOrEmpty(s.part) || s.length <= minOverlap || s.modelLength <= 0f ||
                        s.width <= 0f || s.top <= s.bottom) return false;
                return MaxTunnel >= MinTunnel;
            }
        }
    }
}
