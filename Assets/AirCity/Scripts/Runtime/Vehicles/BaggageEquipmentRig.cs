using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Poses the reusable equipment art. Dispatch, baggage ownership and aircraft docking are separate.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BaggageEquipmentRig : MonoBehaviour
    {
        public VehicleType type;
        [Range(-65f, 65f)] public float cartSteering;
        [Range(0f, 80f)] public float drawbarLift;
        [Range(0f, 1f)] public float curtainOpen;
        public bool displayLoad;
        public float beltRearHeight = 0.82f, beltFrontHeight = 1.45f;

        [Serializable] sealed class Rest
        {
            public string name;
            public Transform part;
            public Vector3 position, scale;
            public Quaternion rotation;
        }

        readonly Dictionary<string, Rest> parts = new Dictionary<string, Rest>();
        [SerializeField, HideInInspector] Rest[] restParts = Array.Empty<Rest>();
        [SerializeField, HideInInspector] VehicleType restType;
        [SerializeField, HideInInspector] Vector3 couplingInDrawbar;
        VehicleType cachedType;
        float lastSteer, lastDrawbar, lastCurtain, lastRear, lastFront;
        bool lastLoad, applied;

        public Vector3 FrontHitchWorld => parts.TryGetValue("Drawbar", out var drawbar)
            ? drawbar.part.TransformPoint(couplingInDrawbar)
            : transform.TransformPoint(type != null && type.baggage != null ? type.baggage.frontHitch : Vector3.zero);
        public Vector3 RearHitchWorld => transform.TransformPoint(type != null && type.baggage != null ? type.baggage.rearHitch : Vector3.zero);

        void OnEnable() => Apply();
        void Update()
        {
            if (!applied || cachedType != type || lastSteer != cartSteering || lastDrawbar != drawbarLift ||
                lastCurtain != curtainOpen || lastLoad != displayLoad || lastRear != beltRearHeight || lastFront != beltFrontHeight)
                Apply();
        }

        public void FindParts()
        {
            if (type == null || type.baggage == null) return;
            // Reference transforms survive scene saves and domain reloads, even when the model is posed.
            if (restType == type && restParts.Length > 0)
            {
                parts.Clear();
                foreach (var rest in restParts) if (rest.part != null) parts[rest.name] = rest;
                cachedType = type;
                return;
            }
            var found = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) found[t.name] = t;
            void Parent(string child, string parent)
            {
                if (found.TryGetValue(child, out var c) && found.TryGetValue(parent, out var p)) c.SetParent(p, true);
            }
            if (type.IsBaggageTrailer)
            {
                Parent("Drawbar", "FrontAxle");
                Parent("Wheel_FL", "FrontAxle");
                Parent("Wheel_FR", "FrontAxle");
            }
            if (type.kind == VehicleKind.BeltLoader)
            {
                Parent("BeltSurface", "Conveyor");
                Parent("RearRoller", "Conveyor");
                Parent("FrontRoller", "Conveyor");
            }
            parts.Clear();
            foreach (var pair in found)
                parts[pair.Key] = new Rest
                {
                    name = pair.Key,
                    part = pair.Value, position = transform.InverseTransformPoint(pair.Value.position),
                    rotation = Quaternion.Inverse(transform.rotation) * pair.Value.rotation, scale = pair.Value.localScale,
                };
            if (found.TryGetValue("Drawbar", out var drawbar))
                couplingInDrawbar = drawbar.InverseTransformPoint(transform.TransformPoint(type.baggage.frontHitch));
            restParts = new List<Rest>(parts.Values).ToArray();
            restType = type;
            cachedType = type;
            applied = false;
        }

        public void Apply()
        {
            if (type == null || type.baggage == null) return;
            if (cachedType != type || parts.Count == 0) FindParts();
            if (type.IsBaggageTrailer) PoseCart();
            if (type.kind == VehicleKind.BeltLoader) PoseBelt();
            var lods = GetComponent<LODGroup>();
            if (lods != null) lods.RecalculateBounds();
            lastSteer = cartSteering; lastDrawbar = drawbarLift; lastCurtain = curtainOpen;
            lastLoad = displayLoad; lastRear = beltRearHeight; lastFront = beltFrontHeight;
            applied = true;
        }

        void PoseCart()
        {
            var yaw = Quaternion.AngleAxis(Mathf.Clamp(cartSteering, -65f, 65f), Vector3.up);
            if (parts.TryGetValue("FrontAxle", out var axle)) Pose(axle, axle.position, yaw * axle.rotation);
            if (parts.TryGetValue("Drawbar", out var drawbar))
            {
                // The parent carries steering; the drawbar rotates around its own horizontal hinge.
                var restLocal = Quaternion.Inverse(parts["FrontAxle"].rotation) * drawbar.rotation;
                drawbar.part.localRotation = restLocal * Quaternion.AngleAxis(-Mathf.Clamp(drawbarLift, 0f, 80f), Vector3.right);
            }
            foreach (string suffix in new[] { "L", "R" })
            {
                if (parts.TryGetValue("Curtain_" + suffix, out var curtain))
                {
                    var scale = curtain.scale;
                    scale.y *= Mathf.Lerp(1f, 0.035f, Mathf.Clamp01(curtainOpen));
                    curtain.part.localScale = scale;
                }
                if (parts.TryGetValue("CurtainRoll_" + suffix, out var roll))
                {
                    var scale = roll.scale;
                    float radius = Mathf.Lerp(1f, 1.7f, Mathf.Clamp01(curtainOpen));
                    scale.x *= radius; scale.y *= radius;
                    roll.part.localScale = scale;
                }
            }
            if (parts.TryGetValue("Load", out var load)) load.part.gameObject.SetActive(displayLoad);
        }

        void PoseBelt()
        {
            var b = type.baggage;
            if (b.beltLength <= 0f) return;
            beltRearHeight = Mathf.Clamp(beltRearHeight, b.rearMinHeight, b.rearMaxHeight);
            beltFrontHeight = Mathf.Clamp(beltFrontHeight, b.frontMinHeight, b.frontMaxHeight);
            float angle = Mathf.Asin(Mathf.Clamp((beltFrontHeight - beltRearHeight) / b.beltLength, -1f, 1f));
            float restAngle = Mathf.Asin((b.beltFrontHeight - b.beltRear.y) / b.beltLength);
            var delta = Quaternion.AngleAxis(-(angle - restAngle) * Mathf.Rad2Deg, Vector3.right);
            var rear = b.beltRear; rear.y = beltRearHeight;
            if (parts.TryGetValue("Conveyor", out var conveyor))
                Pose(conveyor, rear + delta * (conveyor.position - b.beltRear), delta * conveyor.rotation);
            PoseLift("LiftRear", b.rearLift, angle, restAngle);
            PoseLift("LiftFront", b.frontLift, angle, restAngle);
        }

        void PoseLift(string prefix, BaggageLiftDimensions lift, float angle, float restAngle)
        {
            if (lift == null || lift.stages <= 0) return;
            var b = type.baggage;
            float Top(float a, float rearHeight) => rearHeight + (lift.centre - b.beltRear.z) * Mathf.Tan(a) - b.beltSupportOffset;
            float top = Top(angle, beltRearHeight), restTop = Top(restAngle, b.beltRear.y);
            float step = (top - lift.@base) / lift.stages, restStep = (restTop - lift.@base) / lift.stages;
            float span = Mathf.Sqrt(Mathf.Max(0f, lift.length * lift.length - step * step));
            float restSpan = Mathf.Sqrt(Mathf.Max(0f, lift.length * lift.length - restStep * restStep));
            for (int stage = 0; stage < lift.stages; stage++)
                for (int side = 0; side < 2; side++)
                    if (parts.TryGetValue($"{prefix}_{stage * 2 + side}", out var arm))
                    {
                        float sign = side == 0 ? 1f : -1f;
                        float a = Mathf.Atan2(step, sign * span), rest = Mathf.Atan2(restStep, sign * restSpan);
                        var position = new Vector3(b.beltRear.x, lift.@base + stage * step, lift.centre - sign * span / 2f);
                        Pose(arm, position, Quaternion.AngleAxis(-(a - rest) * Mathf.Rad2Deg, Vector3.right) * arm.rotation);
                    }
            if (parts.TryGetValue(prefix + "_Head", out var head))
                Pose(head, new Vector3(head.position.x, top, head.position.z), head.rotation);
        }

        void Pose(Rest rest, Vector3 position, Quaternion rotation) =>
            rest.part.SetPositionAndRotation(transform.TransformPoint(position), transform.rotation * rotation);

        /// <summary>Align the trailer's actual drawbar eye with the leading vehicle's rear pin, retaining its bearing.</summary>
        public void CoupleBehind(BaggageEquipmentRig leader)
        {
            if (leader == null) throw new ArgumentNullException(nameof(leader));
            Apply(); leader.Apply();
            transform.position += leader.RearHitchWorld - FrontHitchWorld;
        }
    }
}
