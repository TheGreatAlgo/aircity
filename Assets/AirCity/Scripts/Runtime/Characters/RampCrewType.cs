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

    /// <summary>How a hand holds what it is attached to; decides the wrist and finger pose, never the palm position.</summary>
    public enum RampCrewGrip : byte
    {
        /// <summary>Chosen from context: two free-standing hands grip the sides of a load, seated hands a wheel, a worker
        /// with pinned feet operates controls palm down, a single hand takes a handle.</summary>
        Auto,
        /// <summary>Palm flat against the side of a load, facing the other hand; fingers wrap down and forward.</summary>
        Bag,
        /// <summary>Fist around a handle or bar, palm down.</summary>
        Handle,
        /// <summary>Hand round a steering wheel rim, palm toward the other hand, fingers forward over the rim.</summary>
        Wheel,
        /// <summary>Palm down on a lever, joystick or button panel, fingers forward and half curled.</summary>
        Control,
        /// <summary>Open palm pushing forward (a door, a bag sliding into the hold).</summary>
        Flat,
        /// <summary>Closed fist holding a wand or a stick; the wrist continues the forearm.</summary>
        Wand,
    }

    /// <summary>
    /// What a worker is doing at one moment, in the actor's frame. The rig turns it into bone transforms; it is
    /// continuous in every field, so producers should blend values rather than switch them (booleans still work as
    /// weight 0 / 1 and the rig eases their flips). Leave new fields at their defaults to get the automatic behaviour.
    /// </summary>
    public struct RampCrewPose
    {
        /// <summary>Ground point under the worker and facing, in the parent (airport) frame. The rig derives walking,
        /// stepping, turning on the spot and speed from how these change between Apply calls.</summary>
        public Vector3 position;
        public Quaternion rotation;
        /// <summary>Hip joint height above <see cref="position"/> (0.94 standing; ~.65 low pick; ~.38 kneeling) and forward
        /// trunk bend in degrees, shared by pelvis, spine and chest.</summary>
        public float hipHeight, lean;
        /// <summary>Legacy gait hints. The rig steps from the root's real motion, so these are ignored.</summary>
        public float stride, phase;
        /// <summary>Legacy switches: both palms on their targets, sitting with feet pinned, kneeling. Each is combined with
        /// its weight below as max(switch, weight).</summary>
        public bool hands, seated, kneeling;
        /// <summary>Palm centres (hands) and floor points under the ankles (feet), in the actor's local frame. Non-zero foot
        /// targets pin the feet there (vehicle controls, seats); zero leaves the feet to the stepper.</summary>
        public Vector3 leftHand, rightHand, leftFoot, rightFoot;

        /// <summary>Continuous kneel and sit weights, 0..1. Kneeling needs a low hip (below ~.75) to put the knees down.</summary>
        public float kneel, sit;
        /// <summary>Per-hand attachment weight 0..1: 0 the arm hangs and swings, 1 the palm is exactly on its target.</summary>
        public float leftReach, rightReach;
        /// <summary>Weight 0..1 of pinning the feet to <see cref="leftFoot"/> / <see cref="rightFoot"/> (1 whenever the
        /// worker is seated or a foot target is set).</summary>
        public float feet;
        public RampCrewGrip leftGrip, rightGrip;
        /// <summary>Optional palm normal and finger direction per hand, actor frame; zero = from the grip kind.</summary>
        public Vector3 leftPalmNormal, rightPalmNormal, leftFingers, rightFingers;
        /// <summary>Optional point to look at, actor frame; zero = the work in the hands, the way ahead, or idle glances.</summary>
        public Vector3 lookAt;
        /// <summary>How heavy what the hands hold feels, 0..1: the body counterbalances it and the elbows tuck in.</summary>
        public float load;
        /// <summary>Optional root velocity in the parent frame, game metres per second; zero = measured from motion.</summary>
        public Vector3 velocity;
        /// <summary>Extra trunk side bend (positive to the right) and twist (positive turns the chest right), degrees.</summary>
        public float sideBend, twist;

        public static RampCrewPose Standing(Vector3 p, Vector3 facing) => new RampCrewPose
        { position = p, rotation = Quaternion.LookRotation(facing, Vector3.up), hipHeight = .94f };
    }
}
