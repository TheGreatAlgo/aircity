using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>The generated deicing equipment, shared by every player-built pad.</summary>
    public sealed class DeicingAssets : ScriptableObject
    {
        public VehicleType truckType, tankType;
        public GameObject truckPrefab, tankPrefab;
        public VehicleType tankerType, recyclerType;
        public GameObject tankerPrefab, recyclerPrefab;
        public Material sprayMaterial;
        [Tooltip("Distance from shoulder to cabin pivot in the generated truck's rest pose (metres).")]
        public float boomRestReach = 7.4242847f;
        public float boomLowerLength = 4.04475f, boomUpperLength = 3.4f;
        public float boomLowerRestAngle = 8.530766f;
        public Vector3 boomLiftPivot = new Vector3(0f, 2.8f, 1.2f);
        public Vector3 cabinNozzleOffset = new Vector3(0f, .85f, .85f);
        public Vector3 refillPoint = new Vector3(-1.35f, 1.42f, -.85f);
        public Vector3 tankPumpPoint = new Vector3(-2.12f, 1.43f, 6.02f);
        public Vector3 tankerHosePoint = new Vector3(-1.25f, 1f, -2f);
        public Vector3 tankDeliveryPoint = new Vector3(2.42f, .86f, 1.51f);
        [Tooltip("Travel of the inner telescopic boom (metres).")]
        public float boomExtension = 3f;
    }
}
