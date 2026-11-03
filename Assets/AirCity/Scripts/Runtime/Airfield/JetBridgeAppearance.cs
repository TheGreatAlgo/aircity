using System;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>Reusable airport branding and equipment, independent of bridge dimensions.</summary>
    [CreateAssetMenu(menuName = "AirCity/Jet bridge appearance")]
    public sealed class JetBridgeAppearance : ScriptableObject
    {
        public Color cladding = Color.white, glazing = Color.white, roof = Color.white;
        [Range(0f, 1f)] public float glazingAmount = 1f;
        [Serializable]
        public sealed class PartStyle
        {
            [Tooltip("Part name, or empty for every part.")]
            public string part;
            public Material material;
            public Color tint = Color.white;
        }
        [Serializable]
        public sealed class Attachment
        {
            [Tooltip("Part the sign, advertisement or equipment follows.")]
            public string part = "Cab";
            public GameObject prefab;
            public Vector3 position, rotation;
            public Vector3 scale = Vector3.one;
        }
        public PartStyle[] parts = Array.Empty<PartStyle>();
        public Attachment[] attachments = Array.Empty<Attachment>();
        void OnValidate() => BridgeAssetRevision.Touch();
    }
}
