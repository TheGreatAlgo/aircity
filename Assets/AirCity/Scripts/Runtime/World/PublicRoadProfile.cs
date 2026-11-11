using UnityEngine;

namespace AirCity.World
{
    /// <summary>Public-road art and dimensions, independent of the airside vehicle graph.</summary>
    public sealed class PublicRoadProfile : ScriptableObject
    {
        public string displayName;
        public float pavementWidth = 6.5f, shoulderWidth = 1.2f, crown = .065f;
        public Material asphalt, shoulder, markings;
    }
}
