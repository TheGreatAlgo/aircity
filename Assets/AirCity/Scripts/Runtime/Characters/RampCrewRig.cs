using UnityEngine;

namespace AirCity.Characters
{
    /// <summary>
    /// Poses the shared skinned worker from a <see cref="RampCrewPose"/>. Palms land exactly on in-reach hand targets;
    /// everything else is procedural and continuous: a foot-planting stepper driven by the root's real motion, a curved
    /// trunk that bends, twists and shifts to bring the hands within a comfortable reach, gait counter-rotation, arm
    /// swing, grip-dependent wrists and fingers, a head that looks at the work, and idle breathing and weight shifts.
    /// State (feet, eased weights, gaze) advances in screen seconds = game dt / airport time scale; it snaps on the first
    /// Apply, on teleports, after being skipped or disabled, and whenever no time passes (edit mode, tests, pause).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RampCrewRig : MonoBehaviour
    {
        public RampCrewType type;
        public Material[] uniforms;

        sealed class Bone
        {
            public Transform transform;
            public Vector3 position;   // rest joint, actor frame
            public Quaternion rest;    // rest rotation relative to the actor
            public Quaternion frame;   // Inverse(LookRotation(rest axis, rest reference)) * rest
            public Bone Frame(Vector3 axis, Vector3 reference)
            { frame = Quaternion.Inverse(Quaternion.LookRotation(axis, reference)) * rest; return this; }
        }

        struct Foot
        {
            public Vector3 position;              // ground point under the flat-footed ankle, pose parent frame
            public float yaw;                     // degrees, pose parent frame
            public bool swinging;
            public Vector3 from;
            public float fromYaw, fromPitch, progress, height;
            public float pitch, landPitch, landed, rise, lift;
            public Vector3 land;                  // where a swinging foot comes down, less the body's travel until then
            public float landYaw;
        }

        // Tuning, in metres, degrees and screen seconds.
        const float MaxCadence = 3.0f;            // steps per screen second before the feet start to slide with the body
        const float PalmOffset = .075f;           // palm centre along the hand axis from the wrist joint
        const float KneeFloor = .075f;            // knee joint height when it rests on the floor
        const float KneelPitch = -62f;            // toes tucked under when kneeling
        const float FarDistance = 250f;
        const float AttachSpeed = 3.5f;           // m per screen second a palm may travel to take hold
        // Knee caps: the straightest a weight-bearing knee may be (interior thigh-shin angle, 180 = locked). The pelvis
        // height is solved from them, so a planted leg is exactly this straight unless the pose asks for lower hips.
        // The rest skeleton stands at about 176 deg and the two-bone solver tops out at about 176.1 deg.
        const float UprightKnee = 175.5f;         // standing, heel strike, mid and terminal stance
        const float LoadingKnee = 163f;           // the knee gives as the leading leg takes the body's weight
        const float ReleasedKnee = 175.9f;        // pre-swing and swing: the leg follows the pelvis, it does not hold it
        const float PelvisRise = .45f;            // m per screen second the legs' bound on the pelvis may rise

        Bone hips, spine, chest, neck, head;
        readonly Bone[] clavicle = new Bone[2], upperArm = new Bone[2], forearm = new Bone[2], hand = new Bone[2],
            fingers = new Bone[2], thumb = new Bone[2], thigh = new Bone[2], shin = new Bone[2], foot = new Bone[2], toe = new Bone[2];
        readonly float[] upperLength = new float[2], foreLength = new float[2], thighLength = new float[2], shinLength = new float[2];
        readonly Quaternion[] fingersLocal = new Quaternion[2], thumbLocal = new Quaternion[2];
        readonly Quaternion[] upperRestFrame = new Quaternion[2];
        float ankleHeight = .09f, ballForward = .15f, heelBack = .07f;
        Transform carrySocket;
        int seed;
        public Transform CarrySocket => carrySocket;
        public Vector3 LeftPalm { get; private set; }
        public Vector3 RightPalm { get; private set; }
        /// <summary>World heel and ball-of-foot sole points after the last Apply (side 0 = left), e.g. for footsteps.</summary>
        public void FootContact(int side, out Vector3 heel, out Vector3 ball) { heel = heelWorld[side]; ball = ballWorld[side]; }
        /// <summary>True while the stepper has that foot in the air.</summary>
        public bool FootSwinging(int side) => steps[side].swinging;
        readonly Vector3[] heelWorld = new Vector3[2], ballWorld = new Vector3[2];

        // ---------------------------------------------------------------- persistent state
        readonly Foot[] steps = new Foot[2];
        bool snap = true;
        Vector3 lastPosition, velocity;
        float lastYaw, yawRate, stepClock = 1f, walk;
        int nextFoot;
        float kneelState, sitState, feetState, hipState, leanState;
        readonly float[] kneeCap = { UprightKnee, UprightKnee }, legReach = new float[2];
        float legsTop, legsTopUsed, frameH;      // the legs' bound on the pelvis: last frame's, this frame's, and this frame's h
        readonly float[] reachState = new float[2], easeState = new float[2], hold = new float[2];
        readonly Vector3[] grabOffset = new Vector3[2], grabStart = new Vector3[2];
        readonly float[] grabTime = { 1f, 1f }, grabSpan = { 1f, 1f };
        readonly bool[] attaching = new bool[2];
        Vector3 gaze;
        float life, shuffleTimer;
        readonly Vector3[] jitter = new Vector3[2];

        // ---------------------------------------------------------------- per-Apply scratch (actor frame)
        Quaternion rootRotation; Vector3 rootPosition; float bodyYaw;
        float kneelFootZ, kneelGoal, homeZ, homeZGoal;
        float kneel, sit, pin, crouchable, standing, idle, load;
        readonly float[] reach = new float[2], curl = new float[2], lifted = new float[2], bearing = { 1f, 1f };
        /// <summary>How much each hand holds a wheel rim (fingers wrapped round it, thumb closed over it), 0..1.</summary>
        readonly float[] wrap = new float[2];
        /// <summary>Wheel rim: fingers wrap to this (degrees, a fist is about 80) and the thumb closes over the rim.</summary>
        const float WheelFingers = 75f, WheelThumb = 35f;
        readonly Vector3[] palmGoal = new Vector3[2], fingerGoal = new Vector3[2], normalGoal = new Vector3[2], poleGoal = new Vector3[2];
        readonly Vector3[] ankle = new Vector3[2], footForward = new Vector3[2], footUp = new Vector3[2], ground = new Vector3[2];
        readonly float[] footPitch = new float[2];
        readonly Vector3[] landAnkle = new Vector3[2];
        readonly float[] footLift = new float[2];
        Vector3 pelvis;
        float lean, gaitYaw, turnYaw, gaitRoll, twist, side, breath, bodyLag, lagYaw;
        float solveLean, solveTwist, solveSide; Vector3 solveShift;
        Quaternion pelvisRotation, spineRotation, chestRotation, neckRotation, headRotation;
        Vector3 spineJoint, chestJoint, neckJoint, headJoint;
        readonly Vector3[] clavicleJoint = new Vector3[2], shoulder = new Vector3[2], hipJoint = new Vector3[2];
        readonly Quaternion[] clavicleTurn = new Quaternion[2];
        readonly float[] protraction = new float[2], elevation = new float[2];

        public void Initialize(int variation)
        {
            if (hips != null) return;
            seed = variation;
            var all = GetComponentsInChildren<Transform>(true);
            Bone Find(string name)
            {
                foreach (var t in all)
                    if (t.name == name)
                        return new Bone { transform = t, position = transform.InverseTransformPoint(t.position), rest = Quaternion.Inverse(transform.rotation) * t.rotation };
                return null;
            }
            var core = new[] { Find("Hips"), Find("Spine"), Find("Chest"), Find("Neck"), Find("Head") };
            for (int i = 0; i < 2; i++)
            {
                string s = i == 0 ? "_L" : "_R";
                clavicle[i] = Find("Clavicle" + s); upperArm[i] = Find("UpperArm" + s); forearm[i] = Find("Forearm" + s);
                hand[i] = Find("Hand" + s); fingers[i] = Find("Fingers" + s); thumb[i] = Find("Thumb" + s);
                thigh[i] = Find("Thigh" + s); shin[i] = Find("Shin" + s); foot[i] = Find("Foot" + s); toe[i] = Find("Toe" + s);
                if (upperArm[i] == null || forearm[i] == null || hand[i] == null || thigh[i] == null || shin[i] == null || foot[i] == null)
                { Debug.LogError($"{name}: ramp crew skeleton is missing limb bones"); return; }
            }
            foreach (var b in core) if (b == null) { Debug.LogError($"{name}: ramp crew skeleton is missing trunk bones"); return; }
            hips = core[0]; spine = core[1]; chest = core[2]; neck = core[3]; head = core[4];
            for (int i = 0; i < 2; i++)
            {
                upperLength[i] = Vector3.Distance(upperArm[i].position, forearm[i].position);
                foreLength[i] = Vector3.Distance(forearm[i].position, hand[i].position);
                thighLength[i] = Vector3.Distance(thigh[i].position, shin[i].position);
                shinLength[i] = Vector3.Distance(shin[i].position, foot[i].position);
                var armAxis = (hand[i].position - forearm[i].position).normalized;
                upperArm[i].Frame(forearm[i].position - upperArm[i].position, Vector3.forward);
                upperRestFrame[i] = Quaternion.Inverse(Quaternion.LookRotation(forearm[i].position - upperArm[i].position, Vector3.forward));
                forearm[i].Frame(armAxis, Vector3.down);
                hand[i].Frame(armAxis, Vector3.down);
                thigh[i].Frame(shin[i].position - thigh[i].position, Vector3.forward);
                shin[i].Frame(foot[i].position - shin[i].position, Vector3.forward);
                foot[i].Frame(Vector3.forward, Vector3.up);
                toe[i]?.Frame(Vector3.forward, Vector3.up);
                if (fingers[i] != null) fingersLocal[i] = Quaternion.Inverse(hand[i].rest) * fingers[i].rest;
                if (thumb[i] != null) thumbLocal[i] = Quaternion.Inverse(hand[i].rest) * thumb[i].rest;
            }
            ankleHeight = (foot[0].position.y + foot[1].position.y) * .5f;
            if (toe[0] != null) ballForward = Mathf.Max(.08f, toe[0].position.z - foot[0].position.z);
            carrySocket = new GameObject("Carried bag").transform; carrySocket.SetParent(hand[0].transform, false);
            if (uniforms != null && uniforms.Length > 0)
                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) r.sharedMaterial = uniforms[Mathf.Abs(variation) % uniforms.Length];
            snap = true;
        }

        void OnEnable() => snap = true;

        /// <summary>Forget all motion state; the next Apply places the worker afresh (use after a deliberate jump).</summary>
        public void Snap() => snap = true;

        /// <summary>Legacy entry point: no game time is known, so in play mode a frame of real time is assumed.</summary>
        public void Apply(RampCrewPose pose) => Drive(pose, Application.isPlaying ? Time.deltaTime : 0f, Application.isPlaying ? Time.deltaTime : 0f);

        /// <summary>Pose the worker after <paramref name="dt"/> game seconds at airport <paramref name="timeScale"/>
        /// (game seconds per real second). dt = 0 (paused, edit mode, tests) moves nothing and settles every eased
        /// value on its target.</summary>
        public void Apply(RampCrewPose pose, float dt, float timeScale = 1f) => Drive(pose, dt > 0f && timeScale > 0f ? dt / timeScale : 0f, Mathf.Max(0f, dt));

        // ================================================================ main
        void Drive(RampCrewPose pose, float h, float gameDt)
        {
            Initialize(0);
            if (hips == null) return;
            transform.localPosition = pose.position; transform.localRotation = pose.rotation;
            // Off-screen workers keep their last bone pose; simulation and baggage handovers still advance.
            var camera = Camera.main;
            if (Application.isPlaying && camera != null && (camera.transform.position - transform.position).sqrMagnitude > FarDistance * FarDistance)
            { snap = true; return; }
            if (!(h > 0f)) h = 0f;
            h = Mathf.Min(h, .1f);
            rootRotation = transform.rotation; rootPosition = transform.position;
            var forward = pose.rotation * Vector3.forward;
            bodyYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

            // No time passing: an unchanged pose stays frozen (pause); a changed one is placed afresh (edit mode, tests).
            // A teleport is a jump no producer walks: 3 m plus 40 game metres per game second.
            float jump = 3f + 40f * gameDt;
            bool settle = snap || (pose.position - lastPosition).sqrMagnitude > jump * jump || h == 0f && !Same(pose, lastPose);
            if (settle) h = -1f;
            Remember(pose);
            Inputs(pose, h);
            if (settle) Settle(pose); else Stepper(pose, h);
            snap = false; lastPosition = pose.position; lastYaw = bodyYaw; lastPose = pose;

            frameH = h;
            Legs(pose);
            Body(pose, h);
            Grips(pose);
            Solve();
            legsTop = legsTopUsed;
            Look(pose, h);
            Knees();
            Arms(h);
            Write(h);
        }

        // ================================================================ inputs and easing
        // h < 0 settles on the target, h = 0 holds, h > 0 advances by h screen seconds.
        static float Toward(float current, float target, float h, float seconds) => h < 0f ? target : Mathf.MoveTowards(current, target, h / seconds);
        static float Follow(float current, float target, float h, float seconds) => h < 0f ? target : current + (target - current) * (1f - Mathf.Exp(-h / seconds));

        static bool Same(in RampCrewPose a, in RampCrewPose b) =>
            a.position == b.position && a.rotation == b.rotation && a.hipHeight == b.hipHeight && a.lean == b.lean &&
            a.hands == b.hands && a.seated == b.seated && a.kneeling == b.kneeling && a.leftHand == b.leftHand && a.rightHand == b.rightHand &&
            a.leftFoot == b.leftFoot && a.rightFoot == b.rightFoot && a.kneel == b.kneel && a.sit == b.sit && a.leftReach == b.leftReach &&
            a.rightReach == b.rightReach && a.feet == b.feet && a.leftGrip == b.leftGrip && a.rightGrip == b.rightGrip &&
            a.leftPalmNormal == b.leftPalmNormal && a.rightPalmNormal == b.rightPalmNormal && a.leftFingers == b.leftFingers &&
            a.rightFingers == b.rightFingers && a.lookAt == b.lookAt && a.load == b.load && a.velocity == b.velocity &&
            a.sideBend == b.sideBend && a.twist == b.twist;

        /// <summary>Producers zero targets they no longer use; keep the last real ones while their weights ease out.</summary>
        void Remember(RampCrewPose pose)
        {
            if (pose.hands || pose.leftReach > 0f) heldHand[0] = pose.leftHand;
            if (pose.hands || pose.rightReach > 0f) heldHand[1] = pose.rightHand;
            bool feet = pose.seated || pose.feet > 0f || pose.leftFoot.sqrMagnitude > 1e-6f || pose.rightFoot.sqrMagnitude > 1e-6f;
            if (feet) { heldFoot[0] = pose.leftFoot; heldFoot[1] = pose.rightFoot; }
            if (pose.leftGrip != RampCrewGrip.Auto || pose.hands || pose.leftReach > 0f) heldGrip[0] = pose.leftGrip;
            if (pose.rightGrip != RampCrewGrip.Auto || pose.hands || pose.rightReach > 0f) heldGrip[1] = pose.rightGrip;
        }
        readonly Vector3[] heldHand = new Vector3[2], heldFoot = new Vector3[2];
        readonly RampCrewGrip[] heldGrip = new RampCrewGrip[2];
        RampCrewPose lastPose;
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static float Step(float edge0, float edge1, float x) => Smooth((x - edge0) / (edge1 - edge0));
        static float SoftPlus(float x, float k) => x > 20f * k ? x : k * Mathf.Log(1f + Mathf.Exp(x / k));
        static float SoftLimit(float x, float low, float high)
        {
            // Identity in the middle, approaching the limits smoothly.
            float mid = (low + high) * .5f, half = (high - low) * .5f;
            float u = (x - mid) / half;
            return mid + half * (float)System.Math.Tanh(u);
        }
        static float SmoothMin(float a, float b, float k)
        {
            float t = Mathf.Clamp01(.5f + .5f * (b - a) / k);
            return Mathf.Lerp(b, a, t) - k * t * (1f - t);
        }
        static float Wave(float t, float s) => (Mathf.Sin(t * 1.00f + s) + .6f * Mathf.Sin(t * 2.31f + s * 1.7f) + .3f * Mathf.Sin(t * 4.13f + s * 2.9f)) / 1.9f;

        void Inputs(RampCrewPose pose, float h)
        {
            kneelState = Toward(kneelState, Mathf.Max(pose.kneeling ? 1f : 0f, Mathf.Clamp01(pose.kneel)), h, .35f);
            sitState = Toward(sitState, Mathf.Max(pose.seated ? 1f : 0f, Mathf.Clamp01(pose.sit)), h, .3f);
            bool footTargets = pose.leftFoot.sqrMagnitude > 1e-6f || pose.rightFoot.sqrMagnitude > 1e-6f;
            feetState = Toward(feetState, Mathf.Max(pose.seated || footTargets ? 1f : 0f, Mathf.Clamp01(pose.feet)), h, .2f);
            // Taking hold: the attachment weight follows the producer at once, and the palm closes whatever distance
            // separated it from the target when the target appeared at AttachSpeed (see Arms). A target the producer
            // moves continuously from the hanging hand is therefore followed exactly, however fast the work runs, and
            // only a target that appears far away is reached by a short move. Letting go eases over .2 s.
            for (int i = 0; i < 2; i++)
            {
                float target = Mathf.Max(pose.hands ? 1f : 0f, Mathf.Clamp01(i == 0 ? pose.leftReach : pose.rightReach));
                attaching[i] = h >= 0f && target > 0f && reachState[i] <= 0f;
                reachState[i] = h < 0f || target >= reachState[i] ? target : Mathf.MoveTowards(reachState[i], target, h / .2f);
                // Wrist turn, elbow pole, finger curl and the body's share of the reach ease in over .22 s.
                easeState[i] = Toward(easeState[i], reachState[i], h, target > easeState[i] ? .22f : .2f);
            }
            hipState = Follow(hipState, pose.hipHeight > .05f ? pose.hipHeight : .94f, h, .05f);
            leanState = Follow(leanState, pose.lean, h, .05f);
            sit = Smooth(sitState); pin = Mathf.Max(Smooth(feetState), sit);
            // Knees reach the floor only once the hips are low enough: higher up, a kneel weight is a crouch.
            crouchable = Step(.80f, .52f, hipState);
            kneel = Smooth(kneelState) * crouchable * (1f - sit);
            standing = (1f - kneel) * (1f - sit);
            hold[0] = Smooth(reachState[0]); hold[1] = Smooth(reachState[1]);
            reach[0] = Smooth(easeState[0]); reach[1] = Smooth(easeState[1]);
            load = pose.load > 0f ? Mathf.Clamp01(pose.load) : 0f;
            // Where the feet go is decided by the kneel being asked for, so they step there once instead of chasing the hips.
            float targetHip = pose.hipHeight > .05f ? pose.hipHeight : .94f;
            kneelGoal = Mathf.Max(pose.kneeling ? 1f : 0f, Mathf.Clamp01(pose.kneel)) * Step(.80f, .52f, targetHip) * (1f - sit);
            kneelFootZ = kneelGoal > 0f ? KneelFootZ(targetHip) : 0f;
            // Two stages, so the feet start moving toward a new kneel/stand position without a jolt.
            homeZGoal = Follow(homeZGoal, kneelFootZ * kneelGoal, h, .04f);
            homeZ = Follow(homeZ, homeZGoal, h, .04f);
            if (h > 0f) life += h;
            if (h < 0f) { life = seed * 7.123f; }
        }

        // ================================================================ stepper
        Vector3 Home(int i, float walkWeight)
        {
            float sideSign = i == 0 ? -1f : 1f;
            float width = Mathf.Lerp(Mathf.Lerp(.112f, .092f, walkWeight), .125f, kneelGoal);
            float z = homeZ;
            var j = jitter[i] * ((1f - walkWeight) * standing);
            return new Vector3(sideSign * width + j.x, 0f, z + j.z);
        }
        float ToeOut(int i) => (i == 0 ? -1f : 1f) * Mathf.Lerp(7f, 4f, walk);

        float KneelFootZ(float hipHeight)
        {
            // Hip joint over a floor knee and a tucked foot: where the foot's ground point has to be.
            float h = Mathf.Clamp(hipHeight, .25f, KneeFloor + thighLength[0]);
            float t = thighLength[0], s = shinLength[0];
            float knee = Mathf.Sqrt(Mathf.Max(0f, t * t - (h - KneeFloor) * (h - KneeFloor)));
            AnklePose(Vector3.zero, 0f, KneelPitch, out var a, out _, out _);
            float shinRun = Mathf.Sqrt(Mathf.Max(0f, s * s - (a.y - KneeFloor) * (a.y - KneeFloor)));
            return knee - shinRun - a.z;
        }

        Vector3 Target(int i, RampCrewPose pose, Vector3 ahead, float turnAhead, out float yaw)
        {
            yaw = bodyYaw + turnAhead;
            var root = pose.position + ahead;
            var target = root + Quaternion.Euler(0f, yaw, 0f) * Home(i, walk);
            target.y = pose.position.y + ahead.y;
            yaw += ToeOut(i);
            return target;
        }

        void Settle(RampCrewPose pose)
        {
            velocity = Vector3.zero; yawRate = 0f; walk = 0f; stepClock = 1f; nextFoot = seed & 1; idleStart = true; shuffleTimer = 0f;
            for (int i = 0; i < 2; i++)
            {
                ref var f = ref steps[i];
                f.position = Target(i, pose, Vector3.zero, 0f, out f.yaw);
                f.swinging = false; f.pitch = f.landPitch = f.rise = f.lift = 0f; f.landed = 1f;
                bearing[i] = 1f; lifted[i] = 0f;
            }
            gaze = rootRotation * Vector3.forward;
        }

        void Stepper(RampCrewPose pose, float h)
        {
            var delta = pose.position - lastPosition;
            float turned = Mathf.DeltaAngle(lastYaw, bodyYaw);
            if (h > 0f)
            {
                var measured = pose.velocity.sqrMagnitude > 1e-8f ? pose.velocity : delta / h;
                float k = 1f - Mathf.Exp(-h / .1f);
                velocity += (measured - velocity) * k;
                yawRate += (turned / h - yawRate) * k;
            }
            var planar = new Vector3(velocity.x, 0f, velocity.z);
            float speed = planar.magnitude, turning = Mathf.Abs(yawRate) * Mathf.Deg2Rad;
            float cadence = Mathf.Min(MaxCadence, (1.05f + .7f * (speed + .6f * turning)) * Mathf.Lerp(1f, .75f, kneel));
            float longest = Mathf.Lerp(.85f, .32f, kneel);
            // Whatever no human cadence could step, the feet slide along with (time-compressed work, fast-forward).
            float slip = speed > 1e-4f ? Mathf.Clamp01(1f - longest * cadence / speed) : 0f;
            var effective = velocity * (1f - slip);
            // Far beyond human speed the stride shortens into a quick, steady trot rather than lunging.
            longest *= 1f - .45f * slip;
            float effectiveSpeed = new Vector2(effective.x, effective.z).magnitude;
            walk = Follow(walk, Step(.08f, .7f, effectiveSpeed), h, .12f);
            float swing = Mathf.Clamp(.8f / cadence, .22f, .5f), stance = Mathf.Max(0f, 2f / cadence - swing);
            bool moving = effectiveSpeed > .06f || Mathf.Abs(yawRate) > 25f;

            for (int i = 0; i < 2; i++)
            {
                ref var f = ref steps[i];
                // The smoothed velocity hides a root that jumps back and forth between frames (a work cycle shorter than a
                // frame under fast-forward): whatever this frame moved beyond one human step is slid, never stretched.
                float moved = new Vector2(delta.x, delta.z).magnitude;
                float slipNow = moved > 1e-5f ? Mathf.Clamp01(1f - longest * .5f / moved) : 0f;
                float drag = Mathf.Max(slip, slipNow);
                if (f.swinging) f.from += delta * drag; else f.position += delta * drag;
                if (f.swinging)
                {
                    var homeSwing = Target(i, pose, Vector3.zero, 0f, out _);
                    var fromAway = f.from - homeSwing; fromAway.y = 0f;
                    float reachLimit = Mathf.Lerp(.56f, .42f, kneel);
                    if (fromAway.magnitude > reachLimit) f.from -= fromAway * (1f - reachLimit / fromAway.magnitude);
                    continue;
                }
                // A planted foot never twists more than a hip allows, nor trails beyond a leg's reach: it pivots or drags.
                float relative = Mathf.DeltaAngle(bodyYaw, f.yaw - ToeOut(i));
                if (Mathf.Abs(relative) > 50f)
                {
                    // Swivel on the ball of the foot, the way a turning walker does.
                    float excess = relative - Mathf.Sign(relative) * 50f;
                    var ball = f.position + Quaternion.Euler(0f, f.yaw, 0f) * Vector3.forward * ballForward;
                    f.yaw -= excess;
                    f.position = ball - Quaternion.Euler(0f, f.yaw, 0f) * Vector3.forward * ballForward;
                }
                var home = Target(i, pose, Vector3.zero, 0f, out _);
                var away = f.position - home; away.y = 0f;
                float limit = Mathf.Lerp(.56f, .42f, kneel);
                if (away.magnitude > limit) f.position -= away * (1f - limit / away.magnitude);
                float rise = f.position.y - pose.position.y;
                if (Mathf.Abs(rise) > .45f) f.position.y = pose.position.y + Mathf.Sign(rise) * .45f;
            }

            // Advance swings toward a landing spot predicted from the body's motion.
            for (int i = 0; i < 2; i++)
            {
                ref var f = ref steps[i];
                if (f.swinging)
                {
                    f.progress += h / (moving ? swing : Mathf.Min(swing, .32f));
                    float s = Mathf.Clamp01(f.progress), remaining = (1f - s) * swing + stance * .5f;
                    var target = Target(i, pose, Ahead(effective, remaining, longest), Mathf.Clamp(yawRate * remaining, -50f, 50f), out float yaw);
                    float e = Smooth(s);
                    f.position = Vector3.Lerp(f.from, target, e);
                    f.yaw = f.fromYaw + Mathf.DeltaAngle(f.fromYaw, yaw) * e;
                    // The foot leaves the ground with no upward jolt (a nearly straight knee would amplify one), then clears early.
                    f.lift = f.height * Mathf.Sin(Mathf.PI * Mathf.Pow(s, .8f)) * Step(0f, .2f, s);
                    // Where it lands relative to where the body will be then (the target above is clamped to a stride).
                    f.land = Target(i, pose, Ahead(effective, stance * .5f, longest), Mathf.Clamp(yawRate * stance * .5f, -50f, 50f), out f.landYaw);
                    float strike = 16f * walk;
                    f.pitch = f.fromPitch * (1f - Step(0f, .45f, s)) + 7f * walk * Mathf.Sin(Mathf.PI * s) + strike * Step(.55f, 1f, s);
                    if (f.progress >= 1f)
                    {
                        f.swinging = false; f.position = target; f.yaw = yaw; f.lift = 0f;
                        f.landPitch = f.pitch; f.landed = 0f; f.rise = 0f;
                    }
                }
                else
                {
                    f.landed += h;
                    float strike = f.landPitch * (1f - Step(0f, Mathf.Min(.14f, stance * .5f + .01f), f.landed));
                    // Heel peels off ahead of the next lift while walking.
                    float other = steps[1 - i].swinging ? steps[1 - i].progress : 1f;
                    float riseTarget = i == nextFoot && moving ? walk * 24f * Step(.45f, 1f, stepClock) * Mathf.Max(Step(.3f, 1f, other), stepClock) : 0f;
                    f.rise = Follow(f.rise, Mathf.Min(riseTarget, toe[i] != null ? 32f : 20f), h, .04f);
                    f.pitch = strike - f.rise;
                    f.lift = 0f;
                }
            }

            // The step clock: one foot leaves per tick while there is somewhere to go.
            bool[] need = needs;
            for (int i = 0; i < 2; i++)
            {
                var home = Target(i, pose, Vector3.zero, 0f, out float yaw);
                var off = steps[i].position - home;
                need[i] = moving || new Vector2(off.x, off.z).magnitude > .065f || Mathf.Abs(off.y) > .04f ||
                    Mathf.Abs(Mathf.DeltaAngle(steps[i].yaw, yaw)) > 14f;
            }
            bool swinging = steps[0].swinging || steps[1].swinging;
            if (stepClock < 1f) stepClock += h * (moving ? cadence : Mathf.Max(cadence, 2.4f));
            if (stepClock >= 1f)
            {
                int pick = -1;
                if (!swinging)
                {
                    if (need[nextFoot] && need[1 - nextFoot] && stepClock >= 1f && idleStart)
                    {
                        // From standing, lead with the foot that has further to go (or the one on the side of motion).
                        float Score(int i)
                        {
                            var t = Target(i, pose, Ahead(effective, swing + stance * .5f, longest), 0f, out _);
                            var lateral = Quaternion.Euler(0f, bodyYaw, 0f) * Vector3.right;
                            return (t - steps[i].position).magnitude + .05f * (i == 0 ? -1f : 1f) * Mathf.Sign(Vector3.Dot(effective, lateral) + 1e-6f)
                                + .03f * (i == 0 ? -1f : 1f) * Mathf.Sign(yawRate + 1e-6f);
                        }
                        pick = Score(0) >= Score(1) ? 0 : 1;
                    }
                    else if (need[nextFoot]) pick = nextFoot;
                    else if (need[1 - nextFoot]) pick = 1 - nextFoot;
                }
                if (pick >= 0)
                {
                    ref var f = ref steps[pick];
                    var target = Target(pick, pose, Ahead(effective, swing + stance * .5f, longest), 0f, out _);
                    float distance = Vector3.Distance(f.position, target);
                    f.swinging = true; f.from = f.position; f.fromYaw = f.yaw; f.fromPitch = f.pitch; f.progress = 0f;
                    f.land = Target(pick, pose, Ahead(effective, stance * .5f, longest), 0f, out f.landYaw);
                    f.height = Mathf.Lerp(Mathf.Lerp(.025f, .085f, Step(.03f, .35f, distance)), .03f, kneel);
                    stepClock = idleStart ? 0f : stepClock - 1f;
                    nextFoot = 1 - pick; idleStart = false;
                }
                else if (!swinging) { stepClock = 1f; idleStart = true; }
            }
            if (!moving && !swinging && !need[0] && !need[1]) idleStart = true;

            // Idle: every so often re-set one foot, which makes for a small adjustment step.
            if (!moving && standing > .9f && reach[0] + reach[1] < .1f)
            {
                shuffleTimer += h;
                float period = 9f + 5f * (.5f + .5f * Mathf.Sin(seed * 12.9898f));
                if (shuffleTimer > period)
                {
                    shuffleTimer = 0f;
                    int i = (int)(life * 7.31f + seed) & 1;
                    float r1 = Mathf.Sin(life * 43.1f + seed * 3.7f), r2 = Mathf.Sin(life * 17.7f + seed * 9.1f);
                    jitter[i] = new Vector3(.025f * r1 + .01f, 0f, .085f * Mathf.Sign(r2) * (.6f + .4f * Mathf.Abs(r2)));
                }
            }
            else shuffleTimer = 0f;

            for (int i = 0; i < 2; i++)
            {
                float p = steps[i].swinging ? Mathf.Clamp01(steps[i].progress) : 0f;
                lifted[i] = steps[i].swinging ? Mathf.Sin(Mathf.PI * p) : 0f;
                // How much a foot still bounds the pelvis: fades over the first third of a swing, returns over the last.
                bearing[i] = steps[i].swinging ? 1f - Step(0f, .35f, p) * Step(1f, .7f, p) : 1f;
            }
        }
        readonly bool[] needs = new bool[2];
        bool idleStart = true;

        static Vector3 Ahead(Vector3 velocity, float seconds, float longest)
        {
            var ahead = velocity * seconds;
            float planar = new Vector2(ahead.x, ahead.z).magnitude, limit = longest * .55f;
            if (planar > limit) { ahead.x *= limit / planar; ahead.z *= limit / planar; }
            return ahead;
        }

        // ================================================================ legs (ankles first: they bound the pelvis)
        void AnklePose(Vector3 groundPoint, float yaw, float pitch, out Vector3 ankleAt, out Vector3 forward, out Vector3 up)
        {
            var f = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float radians = pitch * Mathf.Deg2Rad, c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            forward = f * c + Vector3.up * s; up = Vector3.up * c - f * s;
            float pivot = pitch >= 0f ? -heelBack : ballForward;
            ankleAt = groundPoint + f * pivot - forward * pivot + up * ankleHeight;
        }

        void Legs(RampCrewPose pose)
        {
            var inverse = Quaternion.Inverse(pose.rotation);
            for (int i = 0; i < 2; i++)
            {
                ref var f = ref steps[i];
                var stepped = inverse * (f.position - pose.position);
                float yaw = Mathf.DeltaAngle(bodyYaw, f.yaw), pitch = f.pitch, raise = f.lift;
                var pinned = heldFoot[i];
                if (pin > 0f)
                {
                    stepped = Vector3.Lerp(stepped, pinned, pin);
                    yaw = Mathf.Lerp(yaw, (i == 0 ? -1f : 1f) * 6f, pin);
                    pitch = Mathf.Lerp(pitch, 4f * sit, pin); raise *= 1f - pin;
                    if (pin > .999f)
                    {
                        // Keep the stepper where the pinned foot is, so it resumes from there.
                        f.position = pose.position + pose.rotation * pinned; f.yaw = bodyYaw + yaw; f.swinging = false; f.pitch = pitch;
                    }
                }
                pitch = Mathf.Lerp(pitch, KneelPitch, kneel);
                if (f.swinging)
                {
                    // Where this ankle will be at heel strike: the pelvis comes down to it through the second half of the swing.
                    var land = inverse * (f.land - pose.position);
                    float landYaw = Mathf.DeltaAngle(bodyYaw, f.landYaw), landPitch = 16f * walk;
                    if (pin > 0f) { land = Vector3.Lerp(land, pinned, pin); landYaw = Mathf.Lerp(landYaw, (i == 0 ? -1f : 1f) * 6f, pin); landPitch = Mathf.Lerp(landPitch, 4f * sit, pin); }
                    AnklePose(land, landYaw, Mathf.Lerp(landPitch, KneelPitch, kneel), out landAnkle[i], out _, out _);
                }
                ground[i] = stepped; footLift[i] = raise;
                kneelHome[i] = Home(i, walk);
                footPitch[i] = pitch;
                AnklePose(stepped + Vector3.up * raise, yaw, pitch, out ankle[i], out footForward[i], out footUp[i]);
                // Whatever the stepper says (a root that jumped under fast-forward), a foot never ends up farther from under
                // its hip than the longest stride allows.
                if (pin < 1f)
                {
                    var under = new Vector2(ankle[i].x - (thigh[i].position.x - hips.position.x), ankle[i].z);
                    float most = Mathf.Lerp(.62f, .5f, kneel);
                    if (under.magnitude > most)
                    {
                        var pull = under * ((1f - most / under.magnitude) * (1f - pin));
                        ankle[i] -= new Vector3(pull.x, 0f, pull.y); ground[i] -= new Vector3(pull.x, 0f, pull.y);
                    }
                }
            }
        }

        // ================================================================ trunk
        void Body(RampCrewPose pose, float h)
        {
            float sideSign0 = -1f;
            // Support: swinging feet carry nothing; idle weight drifts from foot to foot.
            idle = (1f - walk) * (1f - .8f * Mathf.Max(reach[0], reach[1])) * standing;
            float shift = Wave(life * .45f, seed * 1.3f) * idle;
            float w0 = bearing[0] * (1f + .8f * shift * sideSign0), w1 = bearing[1] * (1f - .8f * shift * sideSign0);
            float support = (ground[0].x * w0 + ground[1].x * w1) / Mathf.Max(.05f, w0 + w1);
            float sway = Mathf.Clamp(support * .32f, -.05f, .05f) * standing;

            // Fore-aft: hips go back to counter a stoop, forward under a carried load.
            float stoop = Mathf.Sin(Mathf.Clamp(leanState, 0f, 80f) * Mathf.Deg2Rad);
            float back = -.11f * stoop * standing + .03f * load * standing;
            float feetZ = (ground[0].z + ground[1].z) * .5f * (1f - kneel);

            // Height: as asked, but an upright ask (about .94) is the legs' to settle: Trunk lowers the pelvis exactly as far
            // as the weight-bearing knees' caps require, so standing and walking legs are nearly straight, not crouched.
            float hip = hipState + .012f * Step(.86f, .94f, hipState) * standing + .011f * Mathf.Max(lifted[0], lifted[1]) * walk * standing;
            hip += .004f * Mathf.Sin(life * 2f * Mathf.PI / 3.7f + seed) * idle;
            pelvis = new Vector3(sway, hip, back + feetZ * .4f);
            KneeCaps(h);

            // Gait: pelvis turns with the forward leg and drops on the swing side; the chest counter-rotates.
            gaitYaw = -7f * (ground[1].z - ground[0].z) * standing;
            // The producer may turn the root instantly (180 degree route reversals); the body follows at a human rate.
            if (h < 0f) bodyLag = bodyYaw;
            else if (h > 0f)
            {
                float behind = Mathf.DeltaAngle(bodyYaw, bodyLag);
                // Hands on something: the shoulders have to come round with it, so turn faster.
                float holding = Mathf.Max(reach[0], reach[1]);
                behind *= Mathf.Exp(-h / Mathf.Lerp(.07f, .035f, holding));
                behind = Mathf.MoveTowards(behind, 0f, Mathf.Lerp(360f, 1100f, holding) * h);
                bodyLag = bodyYaw + behind;
            }
            lagYaw = Mathf.DeltaAngle(bodyYaw, bodyLag) * standing;
            float feetYaw = Mathf.DeltaAngle(bodyYaw, steps[0].yaw - ToeOut(0)) + Mathf.DeltaAngle(bodyYaw, steps[1].yaw - ToeOut(1));
            turnYaw = Mathf.Clamp(feetYaw * .25f, -30f, 30f) * (1f - pin) * standing;
            gaitRoll = 4f * (lifted[0] - lifted[1]) * walk * standing;
            breath = Mathf.Sin(life * 2f * Mathf.PI / 3.9f + seed * 2.1f);
            lean = leanState + 3f * walk * standing - 4f * load - 7f * sit + .5f * breath * idle;
            twist = pose.twist; side = -pose.sideBend + 1.5f * shift * standing;
            solveLean = solveTwist = solveSide = 0f; solveShift = Vector3.zero;
        }

        /// <summary>How straight each weight-bearing knee may be this frame, as a hip-to-ankle reach. Through a stride:
        /// nearly straight at heel strike, a give of about 12 degrees as the leg loads, straight again from mid-stance to
        /// heel rise; the trailing leg is released once the other foot is down, so it folds into the swing.</summary>
        void KneeCaps(float h)
        {
            for (int i = 0; i < 2; i++)
            {
                var f = steps[i];
                float angle;
                if (f.swinging) angle = Mathf.Lerp(ReleasedKnee, UprightKnee, Step(.4f, .85f, f.progress));
                else
                {
                    float loading = Step(0f, .1f, f.landed) * Step(.3f, .14f, f.landed) * walk;
                    angle = Mathf.Lerp(UprightKnee, LoadingKnee, loading);
                    var other = steps[1 - i];
                    if (!other.swinging && other.landed < f.landed) angle = Mathf.Lerp(angle, ReleasedKnee, Step(0f, .08f, other.landed) * walk);
                }
                kneeCap[i] = Follow(kneeCap[i], angle, h, .03f);
                float t = thighLength[i], s = shinLength[i];
                legReach[i] = Mathf.Sqrt(t * t + s * s - 2f * t * s * Mathf.Cos(kneeCap[i] * Mathf.Deg2Rad));
            }
        }

        /// <summary>The highest pelvis (Hips joint height) at which no weight-bearing leg is straighter than its knee cap,
        /// for this pelvis position and rotation. Exact, so the binding knee sits on its cap and never reaches the
        /// two-bone solver's limit, where a locked knee would swing wildly for millimetres of foot motion.</summary>
        float LegsTop(Vector3 at)
        {
            float a = 0f, b = 0f;
            for (int i = 0; i < 2; i++)
            {
                var r = pelvisRotation * (thigh[i].position - hips.position);
                float top;
                if (!steps[i].swinging) top = Top(at, r, ankle[i], legReach[i]);
                else
                {
                    // A swinging leg: the pelvis may rise above its reach of the ground under the foot by less than the foot
                    // is lifted, so it always reaches its foot with a bent knee (never locked straight, then snapping); and
                    // over the second half of the swing the pelvis comes down smoothly toward the height at which this leg
                    // will land straight (the landing point relative to the body at heel strike).
                    float p = Mathf.Clamp01(steps[i].progress);
                    var ground = ankle[i]; ground.y -= footLift[i];
                    float landing = .06f * Step(1f, .5f, p) + .19f * Step(.45f, .15f, p);
                    top = Mathf.Min(Top(at, r, ground, legReach[i]) + .8f * footLift[i], Top(at, r, landAnkle[i], legReach[i]) + landing);
                }
                if (i == 0) a = top; else b = top;
            }
            return SmoothMin(a, b, .004f);
        }

        /// <summary>Highest Hips joint height at which a hip (offset <paramref name="r"/> from the pelvis) is no farther than
        /// <paramref name="reach"/> from <paramref name="foot"/>.</summary>
        static float Top(Vector3 at, Vector3 r, Vector3 foot, float reach)
        {
            float dx = at.x + r.x - foot.x, dz = at.z + r.z - foot.z;
            return foot.y - r.y + Mathf.Sqrt(Mathf.Max(0f, reach * reach - dx * dx - dz * dz));
        }

        void Trunk()
        {
            // The trunk folds to chase a hand target only while the worker is in place; on the move it reaches with
            // the arms (a moving worker that folds flat reads as a dive).
            float chaseLean = SoftLimit(solveLean, -20f, Mathf.Lerp(55f, 14f, walk));
            float total = SoftLimit(lean + chaseLean, -25f, 82f);
            float turn = SoftLimit(twist + solveTwist, -50f, 50f);
            float bend = SoftLimit(side + solveSide, -28f, 28f);
            // The reach may lower the hips; a worker standing at a workstation (feet pinned) bends forward to a low
            // console instead of squatting.
            float drop = Mathf.Min(SoftPlus(-solveShift.y, .01f), Mathf.Lerp(.3f, .015f, pin * (1f - sit)));
            var at = pelvis + new Vector3(SoftLimit(solveShift.x, -.12f, .12f), -drop * (1f - sit), SoftLimit(solveShift.z, -.12f, .12f));
            pelvisRotation = Quaternion.AngleAxis(gaitYaw + turnYaw + lagYaw, Vector3.up) * Quaternion.AngleAxis(.38f * total, Vector3.right) * Quaternion.AngleAxis(gaitRoll, Vector3.forward);
            // The legs' bound falls at once (a planted leg holds the pelvis) but rises no faster than a body rises onto a
            // straightening leg, so a leg that lifts or relaxes never lets the pelvis jump.
            legsTopUsed = LegsTop(at);
            if (frameH >= 0f) legsTopUsed = Mathf.Min(legsTopUsed, legsTop + PelvisRise * frameH);
            at.y = SmoothMin(at.y, legsTopUsed, .004f);
            at.y = Mathf.Max(at.y, .18f);
            spineRotation = pelvisRotation * Quaternion.AngleAxis(-.8f * gaitYaw - .5f * turnYaw - .2f * lagYaw + .45f * turn, Vector3.up) *
                Quaternion.AngleAxis(.36f * total, Vector3.right) * Quaternion.AngleAxis(-.6f * gaitRoll + .5f * bend, Vector3.forward);
            chestRotation = spineRotation * Quaternion.AngleAxis(-.8f * gaitYaw - .5f * turnYaw + .55f * turn, Vector3.up) *
                Quaternion.AngleAxis(.26f * total + .6f * breath * idle, Vector3.right) * Quaternion.AngleAxis(-.4f * gaitRoll + .5f * bend, Vector3.forward);
            spineJoint = at + pelvisRotation * (spine.position - hips.position);
            chestJoint = spineJoint + spineRotation * (chest.position - spine.position);
            neckJoint = chestJoint + chestRotation * (neck.position - chest.position);
            for (int i = 0; i < 2; i++)
            {
                hipJoint[i] = at + pelvisRotation * (thigh[i].position - hips.position);
                float sideSign = i == 0 ? -1f : 1f;
                clavicleTurn[i] = Quaternion.AngleAxis(-sideSign * protraction[i], Vector3.up) * Quaternion.AngleAxis(sideSign * elevation[i], Vector3.forward);
                if (clavicle[i] != null)
                {
                    clavicleJoint[i] = chestJoint + chestRotation * (clavicle[i].position - chest.position);
                    shoulder[i] = clavicleJoint[i] + chestRotation * clavicleTurn[i] * (upperArm[i].position - clavicle[i].position);
                }
                else shoulder[i] = chestJoint + chestRotation * (upperArm[i].position - chest.position);
            }
            pelvisAt = at;
        }
        Vector3 pelvisAt;

        // ================================================================ hands: where and how each grips
        void Grips(RampCrewPose pose)
        {
            for (int i = 0; i < 2; i++) protraction[i] = elevation[i] = 0f;
            Trunk();
            float bag = 0f;
            if (reach[0] > 0f && reach[1] > 0f)
            {
                var a = heldHand[0]; var b = heldHand[1];
                float separation = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                bag = reach[0] * reach[1] * Step(.16f, .06f, Mathf.Abs(a.y - b.y)) * Step(.12f, .26f, separation) * Step(.80f, .62f, separation) *
                    Step(1.40f, 1.20f, (a.y + b.y) * .5f);
            }
            if (pose.load <= 0f) load = .6f * bag * standing;
            for (int i = 0; i < 2; i++)
            {
                float sideSign = i == 0 ? -1f : 1f;
                var target = heldHand[i];
                var other = heldHand[1 - i];
                var inward = Vector3.right * -sideSign;
                var across = other - target; across.y = 0f;
                var facing = reach[1 - i] > 0f && across.sqrMagnitude > .0025f ? across.normalized : inward;
                var reachDir = target - shoulder[i]; if (reachDir.sqrMagnitude < 1e-4f) reachDir = Vector3.forward; reachDir.Normalize();

                // Candidate grips as (fingers, palm normal, elbow pole, finger curl).
                Frame(reachDir, Vector3.ProjectOnPlane(inward + Vector3.down * .3f, reachDir), out var qPoint);
                Frame(Vector3.forward * .75f + Vector3.down * .65f, facing, out var qBag);
                Frame(Flat(reachDir, Vector3.forward), Vector3.down, out var qHandle);
                Frame(Vector3.forward * .82f + Vector3.up * .55f, facing + Vector3.forward * .25f, out var qWheel);
                Frame(Flat(reachDir, Vector3.forward) + Vector3.down * .25f, Vector3.down + Vector3.forward * .15f, out var qControl);
                Frame(Vector3.up, Vector3.forward, out var qFlat);
                var poleBag = Vector3.down + Vector3.back * .8f + Vector3.right * sideSign * .55f;
                var polePoint = Vector3.down * .7f + Vector3.right * sideSign + Vector3.back * .35f;
                var poleWheel = Vector3.down + Vector3.right * sideSign * .25f + Vector3.back * .35f;
                var poleControl = Vector3.down + Vector3.back * .6f + Vector3.right * sideSign * .4f;
                Quaternion q; Vector3 pole; float grip;
                var kind = heldGrip[i];
                switch (kind)
                {
                    case RampCrewGrip.Bag: q = qBag; pole = poleBag; grip = .55f; break;
                    case RampCrewGrip.Handle: q = qHandle; pole = polePoint; grip = .9f; break;
                    case RampCrewGrip.Wheel: q = qWheel; pole = poleWheel; grip = .8f; break;
                    case RampCrewGrip.Control: q = qControl; pole = poleControl; grip = .45f; break;
                    case RampCrewGrip.Flat: q = qFlat; pole = poleWheel; grip = .05f; break;
                    case RampCrewGrip.Wand: q = qPoint; pole = polePoint; grip = .95f; break;
                    default:
                    {
                        float control = pin * (1f - sit);
                        q = Quaternion.Slerp(qPoint, qBag, bag); pole = Vector3.Lerp(polePoint, poleBag, bag); grip = Mathf.Lerp(.85f, .55f, bag);
                        q = Quaternion.Slerp(q, qControl, control); pole = Vector3.Lerp(pole, poleControl, control); grip = Mathf.Lerp(grip, .45f, control);
                        q = Quaternion.Slerp(q, qWheel, sit); pole = Vector3.Lerp(pole, poleWheel, sit); grip = Mathf.Lerp(grip, .8f, sit);
                        break;
                    }
                }
                var fingersAt = q * Vector3.forward; var normal = q * Vector3.up;
                var explicitFingers = i == 0 ? pose.leftFingers : pose.rightFingers;
                var explicitNormal = i == 0 ? pose.leftPalmNormal : pose.rightPalmNormal;
                if (explicitFingers.sqrMagnitude > 1e-6f || explicitNormal.sqrMagnitude > 1e-6f)
                {
                    var f = explicitFingers.sqrMagnitude > 1e-6f ? explicitFingers : fingersAt;
                    var n = explicitNormal.sqrMagnitude > 1e-6f ? explicitNormal : normal;
                    Frame(f, n, out var given); fingersAt = given * Vector3.forward; normal = given * Vector3.up;
                }
                palmGoal[i] = target; fingerGoal[i] = fingersAt; normalGoal[i] = normal; poleGoal[i] = pole;
                curl[i] = grip; wrap[i] = kind == RampCrewGrip.Wheel ? 1f : kind == RampCrewGrip.Auto ? sit : 0f;
            }
        }
        static Vector3 Flat(Vector3 v, Vector3 fallback) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : fallback; }
        static void Frame(Vector3 forward, Vector3 up, out Quaternion q)
        {
            if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
            var u = Vector3.ProjectOnPlane(up, forward.normalized);
            if (u.sqrMagnitude < 1e-6f) u = Vector3.ProjectOnPlane(Mathf.Abs(forward.normalized.y) < .9f ? Vector3.up : Vector3.forward, forward.normalized);
            q = Quaternion.LookRotation(forward, u);
        }

        // ================================================================ reach: the body comes to the hands
        void Solve()
        {
            if (reach[0] <= .001f && reach[1] <= .001f) { Trunk(); return; }
            for (int it = 0; it < 5; it++)
            {
                Trunk();
                float gLean = 0f, hLean = .015f, gTwist = 0f, hTwist = .05f, gSide = 0f, hSide = .12f;
                Vector3 gShift = Vector3.zero; float hShift = 3f;
                var right = pelvisRotation * Vector3.right; var front = spineRotation * Vector3.forward;
                for (int i = 0; i < 2; i++)
                {
                    if (reach[i] <= .001f) continue;
                    var wrist = palmGoal[i] - fingerGoal[i] * PalmOffset;
                    var d = wrist - shoulder[i]; float distance = d.magnitude;
                    float comfort = Mathf.Lerp(.93f, .80f, load) * (upperLength[i] + foreLength[i]);
                    float excess = SoftPlus(distance - comfort, .012f) * reach[i];
                    if (excess < 1e-5f || distance < 1e-4f) continue;
                    var want = d / distance * excess;
                    var jLean = Vector3.Cross(right, shoulder[i] - spineJoint); gLean += Vector3.Dot(jLean, want); hLean += jLean.sqrMagnitude;
                    var jTwist = Vector3.Cross(Vector3.up, shoulder[i] - spineJoint); gTwist += Vector3.Dot(jTwist, want); hTwist += jTwist.sqrMagnitude;
                    var jSide = Vector3.Cross(front, shoulder[i] - pelvisAt); gSide += Vector3.Dot(jSide, want); hSide += jSide.sqrMagnitude;
                    gShift += want; hShift += 1f;
                }
                solveLean += gLean / hLean * Mathf.Rad2Deg;
                solveTwist += gTwist / hTwist * Mathf.Rad2Deg;
                solveSide += gSide / hSide * Mathf.Rad2Deg;
                solveShift += gShift / hShift * (standing + .3f);
            }
            // Clavicles finish a long reach and shrug for raised arms.
            for (int i = 0; i < 2; i++)
            {
                var wrist = palmGoal[i] - fingerGoal[i] * PalmOffset;
                var d = wrist - shoulder[i]; float length = upperLength[i] + foreLength[i];
                float excess = SoftPlus(d.magnitude - .95f * length, .01f);
                float forwardness = d.sqrMagnitude > 1e-6f ? Vector3.Dot(d.normalized, chestRotation * Vector3.forward) : 0f;
                protraction[i] = reach[i] * Mathf.Min(24f, 8f * Step(-.1f, .6f, forwardness) + 320f * excess);
                elevation[i] = reach[i] * 16f * Step(.25f, .95f, d.y / length) + .8f * breath * idle;
            }
            Trunk();
        }

        // ================================================================ head
        void Look(RampCrewPose pose, float h)
        {
            // Eyes sit about 13 cm above and 9 cm ahead of the head joint (crown 1.78 m, chin 1.54 m).
            var eye = neckJoint + chestRotation * (head.position - neck.position + new Vector3(0f, .13f, .09f));
            Vector3 direction;
            if (pose.lookAt.sqrMagnitude > 1e-6f) direction = pose.lookAt - eye;
            else
            {
                float work = Mathf.Max(reach[0], reach[1]);
                var palms = (palmGoal[0] * reach[0] + palmGoal[1] * reach[1]) / Mathf.Max(1e-4f, reach[0] + reach[1]);
                // Hands right under the eyes: look at them past the nose, never straight down the chest.
                var toWork = (palms - eye).normalized + Vector3.forward * .45f;
                var local = Quaternion.Inverse(rootRotation) * velocity; local.y = 0f;
                var travel = local.sqrMagnitude > 1e-4f ? local.normalized : Vector3.forward;
                travel.z = Mathf.Max(travel.z, .35f);
                travel = (travel.normalized * 3f + Vector3.down * .55f).normalized;
                float glanceYaw = 26f * Wave(life * .23f, seed * 2.7f), glancePitch = 5f + 6f * Wave(life * .17f, seed * .9f + 4f);
                var glance = Quaternion.Euler(glancePitch, glanceYaw, 0f) * Vector3.forward;
                if (sit > 0f) glance = Vector3.Slerp(glance, Vector3.forward, sit * .6f);
                float carrying = work * walk;
                // A worker at a workstation (feet pinned: driving, operating a loader) watches where the vehicle goes,
                // glancing at the controls now and then; everyone else watches their hands.
                float watchHands = work * (1f - .6f * carrying) * (1f - .75f * pin);
                if (pin > 0f) glance = Vector3.Slerp(glance, (Vector3.forward * 3f + Vector3.down * .4f).normalized, .5f * pin);
                direction = Vector3.Slerp(Vector3.Slerp(glance, travel, walk), toWork, watchHands);
            }
            var wanted = rootRotation * direction.normalized;
            gaze = h < 0f ? wanted : Vector3.Slerp(gaze, wanted, 1f - Mathf.Exp(-h / .12f));
            var relative = Quaternion.Inverse(chestRotation) * (Quaternion.Inverse(rootRotation) * gaze);
            // Yaw is undefined looking straight down or up; fade it out there so the head never spins.
            float level = new Vector2(relative.x, relative.z).magnitude;
            float yaw = SoftLimit(Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg, -75f, 75f) * Step(.08f, .35f, level);
            float pitch = SoftLimit(-Mathf.Asin(Mathf.Clamp(relative.y, -1f, 1f)) * Mathf.Rad2Deg, -40f, 60f);
            neckRotation = chestRotation * Quaternion.AngleAxis(.4f * yaw, Vector3.up) * Quaternion.AngleAxis(.4f * pitch, Vector3.right);
            headRotation = neckRotation * Quaternion.AngleAxis(.6f * yaw, Vector3.up) * Quaternion.AngleAxis(.6f * pitch, Vector3.right);
            headJoint = neckJoint + neckRotation * (head.position - neck.position);
        }

        // ================================================================ arms
        readonly Vector3[] palmAt = new Vector3[2], wristAt = new Vector3[2], elbowAt = new Vector3[2], bendAt = new Vector3[2], fingerAt = new Vector3[2], normalAt = new Vector3[2];

        readonly Quaternion[] handState = { Quaternion.identity, Quaternion.identity };
        void Arms(float h)
        {
            var chestForward = chestRotation * Vector3.forward;
            float chestYaw = Mathf.Atan2(chestForward.x, chestForward.z) * Mathf.Rad2Deg;
            var yawOnly = Quaternion.AngleAxis(chestYaw, Vector3.up);
            var hang = yawOnly * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(yawOnly) * chestRotation, .3f);
            for (int i = 0; i < 2; i++)
            {
                float sideSign = i == 0 ? -1f : 1f;
                float u = upperLength[i], l = foreLength[i];
                // Free arm: hangs, swings opposite its leg with a soft elbow, palm toward the thigh.
                float legForward = ground[i].z - pelvis.z;
                float swing = Mathf.Clamp(-34f * legForward * standing, -18f, 20f) * walk + 3f + 6f * load;
                float abduct = 7f + 3f * kneel;
                var upperDir = hang * (Quaternion.AngleAxis(-swing, Vector3.right) * Quaternion.AngleAxis(sideSign * abduct, Vector3.forward) * Vector3.down);
                var freeElbow = shoulder[i] + upperDir * u;
                var bendFree = Vector3.ProjectOnPlane(hang * (Vector3.forward + Vector3.right * -sideSign * .3f), upperDir).normalized;
                float flex = (12f + Mathf.Max(0f, swing) * .5f + 6f * walk) * Mathf.Deg2Rad;
                var foreDir = upperDir * Mathf.Cos(flex) + bendFree * Mathf.Sin(flex);
                var freeWrist = freeElbow + foreDir * l;
                var freeFingers = (foreDir * Mathf.Cos(.15f) + bendFree * Mathf.Sin(.15f)).normalized;
                var freeNormal = Vector3.ProjectOnPlane(hang * (Vector3.right * -sideSign + Vector3.back * .35f), freeFingers);
                Frame(freeFingers, freeNormal, out var freeHand);
                var freePalm = freeWrist + freeFingers * PalmOffset;
                var freePole = Vector3.ProjectOnPlane(freeElbow - shoulder[i], (freeWrist - shoulder[i]).normalized);
                float rest = Mathf.Max(sit, .8f * kneel);
                if (rest > 0f)
                {
                    var along = kneeAt[i] - hipJoint[i];
                    var lap = Vector3.Lerp(hipJoint[i], kneeAt[i], .72f) + Vector3.up * .075f + Vector3.right * (-sideSign * .015f);
                    Frame(Vector3.ProjectOnPlane(along, Vector3.up).normalized * .9f + Vector3.down * .35f + Vector3.right * -sideSign * .1f, Vector3.down, out var lapHand);
                    freePalm = Vector3.Lerp(freePalm, lap, rest);
                    freeHand = Quaternion.Slerp(freeHand, lapHand, rest);
                    freePole = Vector3.Lerp(freePole, Vector3.down + Vector3.right * sideSign * .35f + Vector3.back * .5f, rest);
                }

                // The palm rides its target plus the offset it had when the target appeared; that offset closes at
                // AttachSpeed with an ease, so a target the producer moves continuously from the hanging hand is
                // followed exactly however fast the work runs, and only a target that appears far away is reached late.
                if (h < 0f) { grabStart[i] = Vector3.zero; grabTime[i] = 1f; }
                else if (attaching[i])
                {
                    grabStart[i] = freePalm - palmGoal[i]; grabTime[i] = 0f;
                    // An eased move whose peak palm speed is about AttachSpeed.
                    grabSpan[i] = Mathf.Max(.05f, 1.5f * grabStart[i].magnitude / AttachSpeed);
                }
                if (h > 0f && grabTime[i] < 1f) grabTime[i] = Mathf.Min(1f, grabTime[i] + h / grabSpan[i]);
                grabOffset[i] = grabStart[i] * (1f - Smooth(grabTime[i]));
                float w = reach[i];
                var held = palmGoal[i] + grabOffset[i];
                Frame(fingerGoal[i], normalGoal[i], out var gripHand);
                var handQ = Quaternion.Slerp(freeHand, gripHand, w);
                var fingersDir = handQ * Vector3.forward; var normal = handQ * Vector3.up;
                var palm = Vector3.Lerp(freePalm, held, hold[i]);
                var pole = Vector3.Lerp(freePole.sqrMagnitude > 1e-6f ? freePole.normalized : poleGoal[i].normalized, poleGoal[i].normalized, w);
                var wrist = Reachable(shoulder[i], palm - fingersDir * PalmOffset, u + l);
                var elbow = Elbow(shoulder[i], wrist, u, l, pole, out var across);
                // Keep the wrist within what it can bend: turn the hand toward the forearm, palm stays put.
                var fore = (wrist - elbow).normalized;
                float bendAngle = Vector3.Angle(fore, fingersDir);
                if (bendAngle > 70f)
                {
                    float back = SoftPlus(bendAngle - 70f, 6f) * .8f;
                    var turn = Quaternion.AngleAxis(-back, Vector3.Cross(fore, fingersDir).normalized);
                    handQ = turn * handQ; fingersDir = handQ * Vector3.forward; normal = handQ * Vector3.up;
                    wrist = Reachable(shoulder[i], palm - fingersDir * PalmOffset, u + l);
                    elbow = Elbow(shoulder[i], wrist, u, l, pole, out across);
                }
                // The palm is exact; the hand's orientation turns at a human rate toward what it should be.
                if (h < 0f) handState[i] = handQ;
                else if (h > 0f) handState[i] = Quaternion.RotateTowards(handState[i], Quaternion.Slerp(handState[i], handQ, 1f - Mathf.Exp(-h / .06f)), 600f * h);
                if (h >= 0f && handState[i] != handQ)
                {
                    handQ = handState[i]; fingersDir = handQ * Vector3.forward; normal = handQ * Vector3.up;
                    wrist = Reachable(shoulder[i], palm - fingersDir * PalmOffset, u + l);
                    elbow = Elbow(shoulder[i], wrist, u, l, pole, out across);
                }
                wristAt[i] = wrist; elbowAt[i] = elbow; bendAt[i] = -across; fingerAt[i] = fingersDir; normalAt[i] = normal;
                palmAt[i] = wrist + fingersDir * PalmOffset;
                curl[i] = Mathf.Lerp(.28f, curl[i], w); wrap[i] *= w;
            }
        }

        /// <summary>A wrist no farther from the shoulder than the arm reaches: beyond that the hand stops at full stretch
        /// (the palm misses its target) instead of tearing off the forearm.</summary>
        static Vector3 Reachable(Vector3 shoulder, Vector3 wrist, float length)
        {
            var d = wrist - shoulder; float most = length * .999f;
            return d.sqrMagnitude > most * most ? shoulder + d.normalized * most : wrist;
        }

        /// <summary>Two-bone elbow/knee placement with a smooth fallback when the pole lines up with the limb.</summary>
        static Vector3 Elbow(Vector3 root, Vector3 end, float upper, float lower, Vector3 pole, out Vector3 across)
        {
            var d = end - root; float length = d.magnitude;
            var axis = length > 1e-5f ? d / length : Vector3.down;
            length = Mathf.Clamp(length, Mathf.Abs(upper - lower) + .002f, upper + lower - .0005f);
            var a = Vector3.ProjectOnPlane(pole, axis);
            var b = Vector3.ProjectOnPlane(Mathf.Abs(axis.y) < .8f ? Vector3.down : Vector3.forward, axis);
            float t = Step(.05f, .25f, a.magnitude / Mathf.Max(1e-5f, pole.magnitude));
            across = (a.normalized * t + b.normalized * (1f - t));
            if (across.sqrMagnitude < 1e-6f) across = b;
            across.Normalize();
            float along = (upper * upper - lower * lower + length * length) / (2f * length);
            return root + axis * along + across * Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
        }

        public static Vector3 Joint(Vector3 a, Vector3 b, float upper, float lower, Vector3 pole) => Elbow(a, b, upper, lower, pole, out _);

        // ================================================================ write bones
        void Set(Bone b, Vector3 at, Quaternion actorRotation)
        {
            b.transform.SetPositionAndRotation(rootPosition + rootRotation * at, rootRotation * actorRotation);
        }
        void Set(Bone b, Vector3 at, Vector3 axis, Vector3 reference)
        {
            Frame(axis, reference, out var q);
            Set(b, at, q * b.frame);
        }

        void Write(float h)
        {
            Set(hips, pelvisAt, pelvisRotation * hips.rest);
            Set(spine, spineJoint, spineRotation * spine.rest);
            Set(chest, chestJoint, chestRotation * chest.rest);
            Set(neck, neckJoint, neckRotation * neck.rest);
            Set(head, headJoint, headRotation * head.rest);
            for (int i = 0; i < 2; i++)
            {
                if (clavicle[i] != null) Set(clavicle[i], clavicleJoint[i], chestRotation * clavicleTurn[i] * clavicle[i].rest);
                var upperAxis = (elbowAt[i] - shoulder[i]).normalized;
                var bend = Vector3.ProjectOnPlane(bendAt[i], upperAxis);
                Set(upperArm[i], shoulder[i], upperAxis, bend);
                // Forearm: carry the upper arm's palm reference over the elbow hinge, then twist most of the way to the hand.
                var foreAxis = (wristAt[i] - elbowAt[i]).normalized;
                Frame(upperAxis, bend, out var upperFrame);
                var restPalm = upperFrame * (upperRestFrame[i] * Vector3.down);
                var carried = Quaternion.FromToRotation(upperAxis, foreAxis) * restPalm;
                var a = Vector3.ProjectOnPlane(carried, foreAxis); var b = Vector3.ProjectOnPlane(normalAt[i], foreAxis);
                var reference = a;
                // The forearm rolls toward the hand at a human rate (it would otherwise whip round as the elbow pole and
                // the grip blend change together).
                float wanted = a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(a, b, foreAxis) : 0f;
                twistAngle[i] = h < 0f ? wanted : h > 0f ? Mathf.MoveTowards(twistAngle[i], wanted, 720f * h) : twistAngle[i];
                if (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f)
                    reference = Quaternion.AngleAxis(.8f * twistAngle[i], foreAxis) * a;
                Set(forearm[i], elbowAt[i], foreAxis, reference);
                Frame(fingerAt[i], normalAt[i], out var handFrame);
                var handRotation = handFrame * hand[i].frame;
                Set(hand[i], wristAt[i], handRotation);
                var handWorld = rootRotation * handRotation;
                // Fingers and thumb hinge about their local X; negative curls toward the palm on both sides. The mesh rests
                // slightly curled, so curl 0 opens the hand a little past rest and 1 makes a fist.
                // A wheel rim is wrapped: fingers nearly to a fist round it and the thumb (positive closes) over it.
                float fingerAngle = Mathf.Lerp(-Mathf.Lerp(-12f, 80f, curl[i]), -WheelFingers, wrap[i]);
                float thumbAngle = Mathf.Lerp(-Mathf.Lerp(-8f, 35f, curl[i]), WheelThumb, wrap[i]);
                if (fingers[i] != null) fingers[i].transform.rotation = handWorld * fingersLocal[i] * Quaternion.AngleAxis(fingerAngle, Vector3.right);
                if (thumb[i] != null) thumb[i].transform.rotation = handWorld * thumbLocal[i] * Quaternion.AngleAxis(thumbAngle, Vector3.right);

                var knee = kneeAt[i]; var kneeAcross = kneeBend[i];
                Set(thigh[i], hipJoint[i], knee - hipJoint[i], kneeAcross);
                Set(shin[i], knee, ankle[i] - knee, kneeAcross);
                Set(foot[i], ankle[i], footForward[i], footUp[i]);
                if (toe[i] != null)
                {
                    // The toes stay on the ground while the heel is up.
                    var flatForward = footForward[i]; flatForward.y = 0f; flatForward.Normalize();
                    var toeForward = footPitch[i] < 0f ? flatForward : footForward[i];
                    var toeUp = footPitch[i] < 0f ? Vector3.up : footUp[i];
                    Frame(toeForward, toeUp, out var tq);
                    toe[i].transform.rotation = rootRotation * tq * toe[i].frame;
                }
            }
            for (int i = 0; i < 2; i++)
            {
                heelWorld[i] = rootPosition + rootRotation * (ankle[i] - footForward[i] * heelBack - footUp[i] * ankleHeight);
                ballWorld[i] = rootPosition + rootRotation * (ankle[i] + footForward[i] * ballForward - footUp[i] * ankleHeight);
            }
            LeftPalm = rootPosition + rootRotation * palmAt[0];
            RightPalm = rootPosition + rootRotation * palmAt[1];
        }

        readonly Vector3[] kneeAt = new Vector3[2], kneeBend = new Vector3[2], kneelHome = new Vector3[2];
        readonly float[] twistAngle = new float[2];
        void Knees()
        {
            for (int i = 0; i < 2; i++)
            {
                // Knee forward over the foot, never through the floor.
                float sideSign = i == 0 ? -1f : 1f;
                var pole = footForward[i]; pole.y = 0f;
                pole = pole.normalized + Vector3.forward * .5f + Vector3.right * sideSign * .12f + Vector3.up * (.25f * sit);
                var knee = Elbow(hipJoint[i], ankle[i], thighLength[i], shinLength[i], pole, out var across);
                if (kneel > 0f)
                {
                    // Kneeling: the knee rests on the floor in front of the hip (lifted and carried forward by a knee step),
                    // the shin runs back to a tucked foot. Bone lengths stay exact; the foot follows the shin.
                    float t = thighLength[i], s = shinLength[i];
                    float drop = Mathf.Max(0f, hipJoint[i].y - KneeFloor);
                    var step = ground[i] - kneelHome[i];
                    // The thigh's angle from vertical: on the floor once the hips are low enough; on the way down it is already
                    // tipping forward, so the knee meets the floor in front of the hip instead of shooting forward at contact
                    // (acos has an infinite slope where the thigh would come vertical).
                    float onFloor = drop < t ? Mathf.Acos(drop / t) : 0f;
                    float tipped = .45f * Step(t + .2f, t - .02f, drop);
                    float thighAngle = -SmoothMin(-onFloor, -tipped, .08f);
                    var down = new Vector3(hipJoint[i].x + sideSign * .015f, hipJoint[i].y - t * Mathf.Cos(thighAngle) + steps[i].lift * 1.2f,
                        hipJoint[i].z + t * Mathf.Sin(thighAngle) + step.z * .8f);
                    var blended = Vector3.Lerp(knee, down, kneel) - hipJoint[i];
                    knee = hipJoint[i] + (blended.sqrMagnitude > 1e-8f ? blended.normalized : Vector3.down) * t;
                    var shinDir = ankle[i] - knee; if (shinDir.sqrMagnitude < 1e-8f) shinDir = Vector3.back;
                    ankle[i] = Vector3.Lerp(ankle[i], knee + shinDir.normalized * s, kneel);
                }
                knee = Floor(hipJoint[i], ankle[i], knee);
                var line = ankle[i] - hipJoint[i];
                var bent = line.sqrMagnitude > 1e-8f ? Vector3.ProjectOnPlane(knee - hipJoint[i], line.normalized) : Vector3.zero;
                if (bent.sqrMagnitude > 1e-6f) across = bent.normalized;
                // A foot target out of the leg's reach (a pinned foot as the vehicle moves, a kneel half done) lifts the
                // foot along the shin instead of tearing it off.
                var shinRun = ankle[i] - knee;
                if (shinRun.sqrMagnitude > 1e-8f) ankle[i] = knee + shinRun.normalized * shinLength[i];
                kneeAt[i] = knee; kneeBend[i] = across;
            }
        }

        /// <summary>Swing the knee round its circle until it is no lower than a kneeling knee.</summary>
        static Vector3 Floor(Vector3 hip, Vector3 end, Vector3 knee)
        {
            if (knee.y >= KneeFloor) return knee;
            var axis = (end - hip); float length = axis.magnitude; if (length < 1e-5f) return knee;
            axis /= length;
            var centre = hip + axis * Vector3.Dot(knee - hip, axis);
            var u = knee - centre; float radius = u.magnitude; if (radius < 1e-5f) return knee;
            u /= radius; var v = Vector3.Cross(axis, u);
            float r = Mathf.Sqrt(u.y * u.y + v.y * v.y); if (r < 1e-6f) return knee;
            float k = Mathf.Clamp((KneeFloor - centre.y) / (radius * r), -1f, 1f);
            float phi = Mathf.Atan2(v.y, u.y), spread = Mathf.Acos(k);
            float t1 = phi - spread, t2 = phi + spread;
            float t = Mathf.Abs(Mathf.DeltaAngle(0f, t1 * Mathf.Rad2Deg)) < Mathf.Abs(Mathf.DeltaAngle(0f, t2 * Mathf.Rad2Deg)) ? t1 : t2;
            return centre + radius * (u * Mathf.Cos(t) + v * Mathf.Sin(t));
        }

        public void Grip(Vector3 bottom, Quaternion facing)
        {
            carrySocket.SetPositionAndRotation(transform.parent.TransformPoint(bottom), transform.parent.rotation * facing);
        }
    }
}
