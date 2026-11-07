using UnityEngine;

namespace AirCity.Characters
{
    /// <summary>Generated human dimensions and handover timings, also usable by a headless airport.</summary>
    public sealed class RampCrewType : ScriptableObject
    {
        public float height = 1.78f, hips = .94f, minCrouch = .78f;
        public float walkSpeed = 1.2f, returnSpeed = 1.4f, boardingSpeed = .9f;
        public float pickSeconds = .65f, liftSeconds = .45f, turnSeconds = .7f, placeSeconds = .65f;
        public float turnBackSeconds = .5f, settleSeconds = .3f;
        public float holdPickSeconds = .55f, holdLiftSeconds = .4f, holdStowSeconds = .75f, holdResetSeconds = .55f;
    }

    public struct RampCrewPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public float hipHeight, lean, stride, phase;
        public bool hands, seated, kneeling;
        // Hand targets are palm centres, in the actor's local frame.
        public Vector3 leftHand, rightHand, leftFoot, rightFoot;

        public static RampCrewPose Standing(Vector3 p, Vector3 facing) => new RampCrewPose
        { position = p, rotation = Quaternion.LookRotation(facing, Vector3.up), hipHeight = .94f };
    }
}
