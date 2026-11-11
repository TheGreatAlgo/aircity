using UnityEngine;

namespace AirCity.World
{
    /// <summary>Editable road-model layout. Meshes and junctions are generated from this saved network.</summary>
    public sealed class PublicRoadModel : MonoBehaviour
    {
        public PublicRoadNetwork network;
        void Update()
        {
            var signals = GetComponent<PublicRoadSignalsView>();
            if (signals != null) signals.SetClock(Time.time);
        }
    }
}
