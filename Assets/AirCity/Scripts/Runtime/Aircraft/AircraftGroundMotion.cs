using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>
    /// Wheel spin and nose-wheel steering while taxiing or towing. Whatever moves the aircraft sets
    /// groundSpeed and steering; this only animates the gear. Wheels and the steerable nose gear are
    /// found by the names in the aircraft type's gear data.
    /// </summary>
    [DisallowMultipleComponent]
    public class AircraftGroundMotion : MonoBehaviour
    {
        [Tooltip("Metres per second, positive = forwards.")]
        public float groundSpeed;
        [Tooltip("Fraction of the type's max steering angle, positive = turning right.")]
        [Range(-1f, 1f)] public float steering;

        [Serializable]
        public struct WheelPart
        {
            public Transform transform;
            public Quaternion rest;
            public float radius;
        }

        public Transform noseGear;
        public Quaternion noseGearRest = Quaternion.identity;
        public float maxSteeringAngle = 30f;
        public WheelPart[] wheels = Array.Empty<WheelPart>();

        float[] spin;

        public float SteeringAngle => steering * maxSteeringAngle;

        /// <summary>Turn radius (to the main-gear midpoint) at the current steering, infinite when straight.</summary>
        public float TurnRadius(AircraftType type) =>
            Mathf.Abs(steering) < 1e-3f ? float.PositiveInfinity : type.Wheelbase / Mathf.Tan(Mathf.Abs(SteeringAngle) * Mathf.Deg2Rad);

        public void Drive(float speed, float steer)
        {
            groundSpeed = speed;
            steering = Mathf.Clamp(steer, -1f, 1f);
        }

        /// <summary>Find the wheels and the nose gear of `type` and record their rest rotations.</summary>
        public void FindParts(AircraftType type)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                byName[t.name] = t;
            var list = new List<WheelPart>();
            foreach (var w in type.wheels)
                if (byName.TryGetValue(w.name, out var t))
                    list.Add(new WheelPart { transform = t, rest = t.localRotation, radius = w.radius });
            wheels = list.ToArray();
            noseGear = byName.TryGetValue("NoseGear", out var ng) ? ng : null;
            noseGearRest = noseGear != null ? noseGear.localRotation : Quaternion.identity;
            maxSteeringAngle = type.maxSteeringAngle;
        }

        void LateUpdate() => Animate(Time.deltaTime);

        /// <summary>Advance the wheels and set the nose gear (LateUpdate does this every frame).</summary>
        public void Animate(float dt)
        {
            if (spin == null || spin.Length != wheels.Length) spin = new float[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w.transform == null) continue;
                // a positive turn about +X (the axle, pointing to starboard) rolls the wheel's top forwards
                spin[i] = Mathf.Repeat(spin[i] + groundSpeed / Mathf.Max(w.radius, 0.05f) * Mathf.Rad2Deg * dt, 360f);
                w.transform.localRotation = w.rest * Quaternion.AngleAxis(spin[i], Vector3.right);
            }
            if (noseGear != null) noseGear.localRotation = noseGearRest * Quaternion.AngleAxis(SteeringAngle, Vector3.up);
        }
    }
}
