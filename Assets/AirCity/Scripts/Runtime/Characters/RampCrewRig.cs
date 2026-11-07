using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Characters
{
    /// <summary>Game-time poses for the shared skinned worker. IK keeps both palms on a bag or the controls.</summary>
    [DisallowMultipleComponent]
    public sealed class RampCrewRig : MonoBehaviour
    {
        public RampCrewType type;
        public Material[] uniforms;
        sealed class Bone { public Transform transform; public Quaternion rest; public Vector3 axis; }
        readonly Dictionary<string, Bone> bones = new Dictionary<string, Bone>();
        Transform carrySocket;
        public Transform CarrySocket => carrySocket;
        public Vector3 LeftPalm { get; private set; }
        public Vector3 RightPalm { get; private set; }

        public void Initialize(int variation)
        {
            if (bones.Count > 0) return;
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name == "Hips" || t.name == "Spine" || t.name == "Chest" || t.name == "Neck" || t.name == "Head" ||
                    t.name.StartsWith("UpperArm_") || t.name.StartsWith("Forearm_") || t.name.StartsWith("Hand_") ||
                    t.name.StartsWith("Thigh_") || t.name.StartsWith("Shin_") || t.name.StartsWith("Foot_"))
                    bones[t.name] = new Bone { transform = t, rest = Quaternion.Inverse(transform.rotation) * t.rotation };
            }
            foreach (var pair in bones)
            {
                var b = pair.Value; string child = null;
                switch (pair.Key)
                {
                    case "Hips": child = "Spine"; break; case "Spine": child = "Chest"; break;
                    case "Chest": child = "Neck"; break; case "Neck": child = "Head"; break;
                }
                foreach (string side in new[] { "L", "R" })
                {
                    if (pair.Key == "UpperArm_" + side) child = "Forearm_" + side;
                    if (pair.Key == "Forearm_" + side) child = "Hand_" + side;
                    if (pair.Key == "Thigh_" + side) child = "Shin_" + side;
                    if (pair.Key == "Shin_" + side) child = "Foot_" + side;
                }
                if (child != null) b.axis = transform.InverseTransformDirection(bones[child].transform.position - b.transform.position).normalized;
                else if (pair.Key.StartsWith("Hand_"))
                    b.axis = transform.InverseTransformDirection(b.transform.position - bones[pair.Key.Replace("Hand_", "Forearm_")].transform.position).normalized;
                else b.axis = pair.Key.StartsWith("Foot_") ? Vector3.forward : Vector3.up;
            }
            carrySocket = new GameObject("Carried bag").transform; carrySocket.SetParent(bones["Hand_L"].transform, false);
            if (uniforms != null && uniforms.Length > 0)
                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) r.sharedMaterial = uniforms[Mathf.Abs(variation) % uniforms.Length];
        }

        void BoneAt(string name, Vector3 point, Vector3 direction)
        {
            var b = bones[name];
            b.transform.SetPositionAndRotation(transform.TransformPoint(point), transform.rotation *
                Quaternion.FromToRotation(b.axis, direction.normalized) * b.rest);
        }

        public void Apply(RampCrewPose pose)
        {
            Initialize(0);
            transform.localPosition = pose.position; transform.localRotation = pose.rotation;
            // Off-screen workers keep their last bone pose; simulation and baggage handovers still advance.
            var camera = Camera.main;
            if (Application.isPlaying && camera != null && (camera.transform.position - transform.position).sqrMagnitude > 250f * 250f) return;
            float gait = pose.phase * Mathf.PI * 2f;
            var hips = new Vector3(0, pose.hipHeight + Mathf.Abs(Mathf.Sin(gait)) * .018f * pose.stride, 0);
            var up = Quaternion.AngleAxis(pose.lean, Vector3.right) * Vector3.up;
            var spine = hips + up * .16f; var chest = spine + up * .22f;
            var neck = chest + up * .17f; var head = neck + Vector3.up * .04f;
            BoneAt("Hips", hips, up); BoneAt("Spine", spine, up); BoneAt("Chest", chest, up);
            BoneAt("Neck", neck, Vector3.up); BoneAt("Head", head, Vector3.up);
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "L" : "R"; float sign = i == 0 ? -1f : 1f;
                float swing = Mathf.Sin(gait + i * Mathf.PI) * pose.stride;
                Vector3 ankle;
                if (pose.seated) ankle = (i == 0 ? pose.leftFoot : pose.rightFoot) + Vector3.up * .09f;
                else if (pose.kneeling) ankle = new Vector3(sign * .14f, .10f, -.28f);
                else ankle = new Vector3(sign * .105f, .09f + Mathf.Max(0, Mathf.Cos(gait + i * Mathf.PI)) * .10f * pose.stride, swing * .29f);
                var hip = hips + Vector3.right * (sign * .105f);
                var knee = pose.kneeling ? new Vector3(sign * .14f,.085f,
                    Mathf.Sqrt(Mathf.Max(.01f,.421f*.421f-Mathf.Pow(hips.y-.085f,2)))) :
                    Joint(hip, ankle, .421f, .430f, Vector3.forward);
                if (pose.kneeling) ankle = knee + new Vector3(0,.005f,-.430f);
                BoneAt("Thigh_" + side, hip, knee - hip); BoneAt("Shin_" + side, knee, ankle - knee);
                BoneAt("Foot_" + side, ankle, pose.kneeling ? Vector3.back : Vector3.forward);
                var shoulder = chest + up * .08f + Vector3.right * (sign * .205f);
                var palm = pose.hands ? (i == 0 ? pose.leftHand : pose.rightHand) :
                    hips + new Vector3(sign * .245f, -.17f + Mathf.Abs(swing) * .045f, .025f - swing * .20f);
                var handDirection = pose.hands ? new Vector3(0,-.65f,.76f).normalized : Vector3.down;
                var wrist = palm - handDirection * .075f;
                var elbow = Joint(shoulder, wrist, .30f, .27f, new Vector3(sign * .8f,-.5f,-.45f));
                BoneAt("UpperArm_" + side, shoulder, elbow - shoulder); BoneAt("Forearm_" + side, elbow, wrist - elbow);
                BoneAt("Hand_" + side, wrist, handDirection);
                if (i == 0) LeftPalm = transform.TransformPoint(wrist + handDirection * .075f);
                else RightPalm = transform.TransformPoint(wrist + handDirection * .075f);
            }
        }

        public void Grip(Vector3 bottom, Quaternion facing)
        {
            carrySocket.SetPositionAndRotation(transform.parent.TransformPoint(bottom), transform.parent.rotation * facing);
        }

        public static Vector3 Joint(Vector3 a, Vector3 b, float upper, float lower, Vector3 pole)
        {
            var d = b - a; float length = Mathf.Clamp(d.magnitude, .015f, upper + lower - .001f);
            var axis = d.sqrMagnitude > .00001f ? d.normalized : Vector3.down;
            var across = Vector3.ProjectOnPlane(pole, axis).normalized;
            if (across.sqrMagnitude < .1f) across = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
            float along = (upper * upper - lower * lower + length * length) / (2f * length);
            return a + axis * along + across * Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        }
    }
}
