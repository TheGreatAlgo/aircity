using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// The visible side of a maintenance vehicle or a tug: placed each frame by its team (Place), it rolls and steers
    /// its wheels, spins its broom, raises a tug's cradle, switches its lights and throws snow or grass clippings
    /// while it works. Wheels, spinners and the cradle are found by the names in the vehicle type.
    /// </summary>
    [DisallowMultipleComponent]
    public class Vehicle : MonoBehaviour
    {
        public VehicleType type;
        [Tooltip("Particle material for the snow / clippings spray (AirCity/Vehicles/Materials).")]
        public Material sprayMaterial;

        [Serializable]
        public struct WheelPart
        {
            public Transform transform;
            public Quaternion rest;
            public float radius;
            public bool steerable;
        }

        [Serializable]
        public struct SpinnerPart
        {
            public Transform transform;
            public Quaternion rest;
            public Vector3 axis;
            public float rpm;
        }

        public WheelPart[] wheels = Array.Empty<WheelPart>();
        public SpinnerPart[] spinners = Array.Empty<SpinnerPart>();
        [Tooltip("Tugs: the cradle that lifts the nose wheel, and where it sits lowered.")]
        public Transform liftPart;
        public Vector3 liftRest;

        float[] roll;
        float spin, spinSpeed;
        ParticleSystem spray, broomSpray;
        VehicleLights lights;

        public VehicleLights Lights => lights != null ? lights : lights = GetComponent<VehicleLights>();

        /// <summary>Find the wheels and spinners of the type and record their rest rotations.</summary>
        public void FindParts()
        {
            if (type == null) return;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
            var w = new List<WheelPart>();
            foreach (var wh in type.wheels)
                if (byName.TryGetValue(wh.name, out var t))
                    w.Add(new WheelPart { transform = t, rest = t.localRotation, radius = wh.radius, steerable = wh.steerable });
            wheels = w.ToArray();
            var s = new List<SpinnerPart>();
            foreach (var sp in type.spinners)
                if (byName.TryGetValue(sp.name, out var t))
                    s.Add(new SpinnerPart { transform = t, rest = t.localRotation, axis = sp.axis, rpm = sp.rpm });
            spinners = s.ToArray();
            liftPart = !string.IsNullOrEmpty(type.liftPart) && byName.TryGetValue(type.liftPart, out var lift) ? lift : null;
            liftRest = liftPart != null ? liftPart.localPosition : Vector3.zero;
        }

        /// <summary>A tug's cradle: 0 lowered .. 1 raised by the type's lift height.</summary>
        public void SetLift(float amount)
        {
            if (liftPart != null && type != null)
                liftPart.localPosition = liftRest + Vector3.up * (type.liftHeight * Mathf.Clamp01(amount));
        }

        /// <summary>
        /// Put the vehicle on the ground: world position of the rear axle's centre, facing `forward`, moving at
        /// speed (m/s) with the front wheels steered by `steer` degrees (positive = right). dt animates the wheels.
        /// </summary>
        public void Place(Vector3 position, Vector3 forward, float speed, float steer, float dt)
        {
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            if (roll == null || roll.Length != wheels.Length) roll = new float[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w.transform == null) continue;
                roll[i] = Mathf.Repeat(roll[i] + speed / Mathf.Max(w.radius, 0.05f) * Mathf.Rad2Deg * dt, 360f);
                var steerRot = w.steerable ? Quaternion.AngleAxis(Mathf.Clamp(steer, -40f, 40f), Vector3.up) : Quaternion.identity;
                w.transform.localRotation = steerRot * w.rest * Quaternion.AngleAxis(roll[i], Vector3.right);
            }
            spin = Mathf.Repeat(spin + spinSpeed * 6f * dt, 360f);
            foreach (var s in spinners)
                if (s.transform != null)
                    s.transform.localRotation = Quaternion.AngleAxis(-spin, s.axis) * s.rest;
        }

        /// <summary>Lights and work effects: out on the airfield (beacon), working (work lights, broom, spray
        /// with `amount` 0..1 of snow / grass to throw).</summary>
        public void SetState(bool outside, bool working, float amount)
        {
            var l = Lights;
            if (l != null)
            {
                l.beacon = outside;
                l.work = working;
                l.headlights = outside;
            }
            float target = working && type != null && type.spinners.Length > 0 ? 1f : 0f;
            spinSpeed = Mathf.MoveTowards(spinSpeed, target * (type != null && type.spinners.Length > 0 ? type.spinners[0].rpm : 0f),
                Time.deltaTime * 400f);
            Spray(ref spray, working ? amount : 0f, type != null ? type.sprayPoint : Vector3.zero,
                type != null ? type.sprayDirection : Vector3.left, 1f);
            if (type != null && type.broomPoint != Vector3.zero)
                Spray(ref broomSpray, working ? amount : 0f, type.broomPoint, type.sprayDirection + Vector3.back * 0.5f, 0.6f);
        }

        void Spray(ref ParticleSystem ps, float amount, Vector3 point, Vector3 direction, float scale)
        {
            if (amount <= 0.01f)
            {
                if (ps != null && ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            if (ps == null)
            {
                if (sprayMaterial == null) return;
                ps = MakeSpray(point, direction, scale);
            }
            var em = ps.emission;
            bool snow = type == null || type.kind != VehicleKind.Mower;
            em.rateOverTime = (snow ? 90f : 40f) * scale * Mathf.Clamp01(amount);
            // unlit particles: dim them with the daylight (lit by the vehicle's own lamps at night)
            var main = ps.main;
            float light = Mathf.Lerp(1f, 0.3f, AirCity.Aircraft.AircraftLights.Glow);
            var day = snow ? new Color(0.92f, 0.94f, 0.97f, 0.55f) : new Color(0.35f, 0.5f, 0.18f, 0.9f);
            main.startColor = new Color(day.r * light, day.g * light, day.b * light, day.a);
            if (!ps.isEmitting) ps.Play();
        }

        ParticleSystem MakeSpray(Vector3 point, Vector3 direction, float scale)
        {
            bool snow = type == null || type.kind != VehicleKind.Mower;
            var go = new GameObject(snow ? "SnowSpray" : "Clippings") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = point;
            go.transform.localRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.startLifetime = snow ? new ParticleSystem.MinMaxCurve(0.8f, 1.6f) : new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = snow ? new ParticleSystem.MinMaxCurve(4f * scale, 9f * scale) : new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = snow ? new ParticleSystem.MinMaxCurve(0.6f * scale, 1.6f * scale) : new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startColor = snow ? new Color(0.92f, 0.94f, 0.97f, 0.55f) : new Color(0.35f, 0.5f, 0.18f, 0.9f);
            main.gravityModifier = snow ? 0.35f : 0.8f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = snow ? 22f : 35f;
            shape.radius = snow ? 0.4f : 1.2f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = snow;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = sprayMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }
    }
}
