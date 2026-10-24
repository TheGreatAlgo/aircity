using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>Spins a propeller or a jet engine's fan about its forward axis (local Z), clockwise as seen
    /// from behind.</summary>
    public class PropellerSpinner : MonoBehaviour
    {
        [Tooltip("Rotor RPM. Propeller: ~700 idle, ~1200 taxi, ~2400 take-off. Jet fan: ~1000 idle, ~4500 take-off.")]
        public float rpm = 0f;
        [Tooltip("How quickly the rotor follows rpm changes (seconds).")]
        public float spoolTime = 1.5f;

        float currentRpm;

        void Update()
        {
            currentRpm = Mathf.MoveTowards(currentRpm, rpm, Mathf.Max(rpm, 2400f) / Mathf.Max(spoolTime, 0.01f) * Time.deltaTime);
            // Unity is left-handed: a positive roll about +Z looks counter-clockwise from behind
            transform.Rotate(0f, 0f, -currentRpm * 6f * Time.deltaTime, Space.Self);
        }
    }
}
