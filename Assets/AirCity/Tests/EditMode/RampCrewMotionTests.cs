using System.Collections.Generic;
using System.Linq;
using AirCity.Characters;
using AirCity.EditorTools.Characters;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests
{
    /// <summary>Motion quality of the procedural ramp-crew rig: continuity, planted feet, exact palms, cost.</summary>
    public sealed class RampCrewMotionTests
    {
        readonly List<GameObject> spawned = new List<GameObject>();
        [TearDown] public void Clean() { foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go); spawned.Clear(); }

        RampCrewRig Rig(int variation = 0)
        {
            var rig = Object.Instantiate(RampCrewSetup.Prefab).GetComponent<RampCrewRig>();
            rig.Initialize(variation); spawned.Add(rig.gameObject); return rig;
        }
        static Transform[] Bones(RampCrewRig rig) => rig.GetComponentsInChildren<Transform>()
            .Where(t => t.name.Contains("_") || t.name == "Hips" || t.name == "Spine" || t.name == "Chest" || t.name == "Neck" || t.name == "Head")
            .Where(t => !t.name.StartsWith("Body")).ToArray();

        /// <summary>Largest per-sample change over a motion: [0] position (m), [1] rotation (deg), per bone.</summary>
        static (float position, float rotation, string bone, float time) Largest(RampCrewMotion motion, float step)
        {
            var rig = Object.Instantiate(RampCrewSetup.Prefab).GetComponent<RampCrewRig>(); rig.Initialize(0);
            var bones = Bones(rig);
            var positions = new Vector3[bones.Length]; var rotations = new Quaternion[bones.Length];
            float position = 0, rotation = 0, at = 0; string worst = null;
            int count = Mathf.CeilToInt(motion.duration / step);
            for (int s = 0; s <= count; s++)
            {
                rig.Apply(motion.pose(s * step), s == 0 ? 0 : step);
                for (int b = 0; b < bones.Length; b++)
                {
                    // Positions relative to the root, so the root's own travel does not mask a limb pop.
                    var p = rig.transform.InverseTransformPoint(bones[b].position);
                    var q = Quaternion.Inverse(rig.transform.rotation) * bones[b].rotation;
                    if (s > 0)
                    {
                        float dp = Vector3.Distance(p, positions[b]), dq = Quaternion.Angle(q, rotations[b]);
                        // Normalise both to a common scale (1 cm ~ 2 degrees) to find the single worst event.
                        if (dp > position) { position = dp; if (dp * 200f >= rotation) { worst = bones[b].name; at = s * step; } }
                        if (dq > rotation) { rotation = dq; if (dq >= position * 200f) { worst = bones[b].name; at = s * step; } }
                    }
                    positions[b] = p; rotations[b] = q;
                }
            }
            Object.DestroyImmediate(rig.gameObject);
            return (position, rotation, worst, at);
        }

        static IEnumerable<string> HumanMotions => RampCrewMotionScripts.Human.Select(m => m.name);

        [TestCaseSource(nameof(HumanMotions))]
        public void ScriptedMotion_HasNoPops(string name)
        {
            var motion = RampCrewMotionScripts.Human.First(m => m.name == name);
            var coarse = Largest(motion, 1f / 60f);
            var fine = Largest(motion, 1f / 240f);
            Debug.Log($"Pop check {name}: coarse {coarse.position * 1000f:0.0} mm / {coarse.rotation:0.00} deg ({coarse.bone} @ {coarse.time:0.00}s); " +
                      $"fine {fine.position * 1000f:0.0} mm / {fine.rotation:0.00} deg ({fine.bone} @ {fine.time:0.00}s)");
            // A continuous motion changes about four times less per sample at a four times finer step; a pop does not shrink.
            if (coarse.position > .002f) Assert.That(fine.position, Is.LessThan(coarse.position * .45f), $"{name}: position pop at {fine.bone} {fine.time:0.00}s");
            if (coarse.rotation > .5f) Assert.That(fine.rotation, Is.LessThan(coarse.rotation * .45f), $"{name}: rotation pop at {fine.bone} {fine.time:0.00}s");
            // And nothing moves absurdly fast even when continuous: at 60 Hz no bone (relative to the root) moves 10 cm or turns
            // 25 degrees in one frame; a swinging foot peaks around 6 cm walking straight and 8 cm stepping round a quick turn.
            Assert.That(coarse.position, Is.LessThan(.10f), name);
            Assert.That(coarse.rotation, Is.LessThan(25f), name);
        }

        [Test]
        public void Walking_PlantsEachFootUntilItSteps()
        {
            // A foot on the ground pivots about its heel or its ball; one of the two must hold still until the foot lifts.
            foreach (var motion in new[] { RampCrewMotionScripts.WalkTurn(), RampCrewMotionScripts.SidestepBackward() })
            {
                var rig = Rig();
                const float step = 1f / 60f;
                var heel = new Vector3[2]; var ball = new Vector3[2]; int planted = 0, frames = 0, slides = 0; var planted0 = new bool[2];
                for (int s = 0; s * step < motion.duration; s++)
                {
                    rig.Apply(motion.pose(s * step), s == 0 ? 0 : step);
                    bool any = false;
                    for (int i = 0; i < 2; i++)
                    {
                        rig.FootContact(i, out var h, out var b);
                        bool down = !rig.FootSwinging(i), wasDown = planted0[i]; planted0[i] = down;
                        if (s > 0 && down && wasDown)
                        {
                            float still = Mathf.Min(Flat(h - heel[i]), Flat(b - ball[i]));
                            float low = Mathf.Min(h.y, b.y);
                            if (low < .01f && still < .001f) any = true;
                            if (low < .01f && still > .004f) { slides++; if (slides < 6) Debug.Log($"slide {motion.name} foot {i} t={s * step:0.000} {still * 1000:0.0} mm heel {h.y:0.000} ball {b.y:0.000}"); }
                        }
                        heel[i] = h; ball[i] = b;
                    }
                    if (s > 0) { frames++; if (any) planted++; }
                }
                Debug.Log($"Walk/turn {motion.name}: a foot is planted in {planted}/{frames} frames; {slides} grounded-foot slides");
                Assert.That(planted / (float)frames, Is.GreaterThan(.98f), motion.name + ": at human pace one foot is always planted");
                Assert.That(slides, Is.LessThan(frames / 100 + 2), motion.name + ": grounded feet do not skate");
            }
        }
        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        [Test]
        public void TurningOnTheSpot_StepsInsteadOfSpinningTheFeet()
        {
            var rig = Rig(); var foot = Bones(rig).First(b => b.name == "Foot_L");
            var pose = RampCrewPose.Standing(Vector3.zero, Vector3.forward); rig.Apply(pose, 0);
            const float step = 1f / 60f; int still = 0, frames = 0; var last = foot.position;
            for (int s = 1; s <= 90; s++)
            {
                pose.rotation = Quaternion.Euler(0, Mathf.SmoothStep(0, 180, s / 66f), 0); rig.Apply(pose, step);
                var p = foot.position; if (Vector3.Distance(p, last) < .001f) still++; frames++; last = p;
            }
            for (int s = 0; s < 60; s++) rig.Apply(pose, step);
            Assert.That(still, Is.GreaterThan(frames / 4), "a planted foot holds while the other steps round");
            Assert.That(Vector3.Distance(foot.position, rig.transform.TransformPoint(new Vector3(-.11f, .09f, 0))), Is.LessThan(.06f), "feet settle back under the hips after the turn");
        }

        [Test]
        public void FirstApplyAndTeleports_PlaceTheWorkerImmediately()
        {
            var a = Rig(); var b = Rig();
            var pose = RampCrewPose.Standing(new Vector3(3, 0, 2), Vector3.right);
            pose.hands = true; pose.leftHand = new Vector3(-.2f, .9f, .45f); pose.rightHand = new Vector3(.2f, .9f, .45f);
            a.Apply(pose);
            // b walks somewhere else first, then is teleported (with game time passing) to the same pose.
            var walk = RampCrewPose.Standing(new Vector3(40, 0, 0), Vector3.forward);
            for (int s = 0; s < 60; s++) { walk.position.z += .022f; b.Apply(walk, 1f / 60f); }
            b.Apply(pose, 1f / 60f);
            Assert.That(Vector3.Distance(a.LeftPalm, b.LeftPalm), Is.LessThan(.002f));
            Assert.That(Vector3.Distance(a.RightPalm, b.RightPalm), Is.LessThan(.002f));
            var feetA = Bones(a).Where(t => t.name.StartsWith("Foot_")).Select(t => t.position).ToArray();
            var feetB = Bones(b).Where(t => t.name.StartsWith("Foot_")).Select(t => t.position).ToArray();
            for (int i = 0; i < 2; i++) Assert.That(Vector3.Distance(feetA[i], feetB[i]), Is.LessThan(.002f), "a teleport resets the feet");
            Assert.That(Vector3.Distance(a.LeftPalm, pose.position + Quaternion.LookRotation(Vector3.right) * pose.leftHand), Is.LessThan(.002f));
        }

        [Test]
        public void PausedTime_FreezesTheWorkerMidMotion()
        {
            var rig = Rig(); var bones = Bones(rig);
            var motion = RampCrewMotionScripts.KneelShuffleReach();
            for (int s = 0; s < 80; s++) rig.Apply(motion.pose(s / 60f), s == 0 ? 0 : 1f / 60f);
            var pose = motion.pose(80 / 60f);
            rig.Apply(pose, 1f / 60f);
            var before = bones.Select(t => (t.position, t.rotation)).ToArray();
            for (int s = 0; s < 30; s++) rig.Apply(pose, 0f, 0f);
            for (int b = 0; b < bones.Length; b++)
            {
                Assert.That(Vector3.Distance(bones[b].position, before[b].position), Is.LessThan(1e-5f), bones[b].name);
                Assert.That(Quaternion.Angle(bones[b].rotation, before[b].rotation), Is.LessThan(.01f), bones[b].name);
            }
        }

        [Test]
        public void TimeCompressedWork_KeepsLegsReadableInsteadOfStrobing()
        {
            // The baggage handler walks ~20 m/s on screen at WorkRate 18: legs must not cycle faster than a brisk human.
            var rig = Rig(); var bones = Bones(rig);
            var knee = bones.First(b => b.name == "Shin_L"); var thigh = bones.First(b => b.name == "Thigh_L");
            var pose = RampCrewPose.Standing(Vector3.zero, Vector3.forward); rig.Apply(pose, 0);
            const float step = 1f / 60f; float largest = 0; var last = Quaternion.identity;
            int reversals = 0, worstFrame = 0; float extreme = 0; int direction = 0;
            for (int s = 1; s <= 120; s++)
            {
                pose.position.z += 21.6f * step; pose.stride = .65f; pose.phase = s * step * 27f;
                rig.Apply(pose, step);
                var q = Quaternion.Inverse(rig.transform.rotation) * thigh.rotation;
                if (s > 10 && Quaternion.Angle(q, last) > largest) { largest = Quaternion.Angle(q, last); worstFrame = s; } // after the instant start
                last = q;
                float z = rig.transform.InverseTransformPoint(knee.position).z;
                if (direction >= 0 && z < extreme - .01f) { if (direction > 0) reversals++; direction = -1; extreme = z; }
                else if (direction <= 0 && z > extreme + .01f) { if (direction < 0) reversals++; direction = 1; extreme = z; }
                else extreme = direction > 0 ? Mathf.Max(extreme, z) : Mathf.Min(extreme, z);
            }
            Debug.Log($"18x walk: thigh at most {largest:0.0} deg per frame (frame {worstFrame}), knee swing reverses {reversals} times in 2 s");
            Assert.That(largest, Is.LessThan(12f));
            Assert.That(reversals, Is.LessThanOrEqualTo(14), "at most ~3.5 strides per second");
        }

        [Test]
        public void PalmsLandExactlyOnReachableTargets_InEveryPosture()
        {
            var rig = Rig();
            var random = new System.Random(7);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            int checkedPoses = 0;
            for (int n = 0; n < 60; n++)
            {
                var pose = RampCrewPose.Standing(new Vector3(R(-5, 5), 0, R(-5, 5)), Quaternion.Euler(0, R(0, 360), 0) * Vector3.forward);
                int kind = n % 3;
                if (kind == 1) { pose.kneeling = true; pose.hipHeight = R(.35f, .45f); pose.lean = R(20, 50); }
                if (kind == 2) { pose.hipHeight = R(.66f, .94f); pose.lean = R(0, 30); }
                float y = kind == 1 ? R(.2f, .7f) : R(.35f, 1.35f);
                var centre = new Vector3(R(-.1f, .1f), y, R(.3f, .55f));
                float half = R(.18f, .24f);
                pose.hands = true; pose.leftHand = centre + Vector3.left * half; pose.rightHand = centre + Vector3.right * half;
                rig.Apply(pose);
                var left = pose.position + pose.rotation * pose.leftHand; var right = pose.position + pose.rotation * pose.rightHand;
                Assert.That(Vector3.Distance(rig.LeftPalm, left), Is.LessThan(.002f), $"pose {n} left");
                Assert.That(Vector3.Distance(rig.RightPalm, right), Is.LessThan(.002f), $"pose {n} right");
                checkedPoses++;
                // The same targets reached through time (eased weights, a walking stepper) end up just as exact.
                for (int s = 0; s < 30; s++) rig.Apply(pose, 1f / 60f);
                Assert.That(Vector3.Distance(rig.LeftPalm, left), Is.LessThan(.002f), $"pose {n} left after settling");
            }
            Assert.That(checkedPoses, Is.EqualTo(60));
        }

        [Test]
        public void SeatedWorkstation_KeepsFeetOnPedalsAndHandsOnTheWheel()
        {
            var rig = Rig(); var motion = RampCrewMotionScripts.SitWheelStand();
            float t = 3.6f; var pose = motion.pose(t);
            Assert.That(pose.seated && pose.hands, Is.True);
            rig.Apply(pose);
            Assert.That(Vector3.Distance(rig.LeftPalm, pose.position + pose.rotation * pose.leftHand), Is.LessThan(.002f));
            var foot = Bones(rig).First(b => b.name == "Foot_L");
            var expected = pose.position + pose.leftFoot;
            Assert.That(Vector2.Distance(new Vector2(foot.position.x, foot.position.z), new Vector2(expected.x, expected.z)), Is.LessThan(.06f));
        }

        /// <summary>Interior thigh-shin angle in degrees (180 = straight).</summary>
        static float Knee(Transform[] bones, int side)
        {
            string s = side == 0 ? "_L" : "_R";
            var hip = bones.First(b => b.name == "Thigh" + s).position;
            var knee = bones.First(b => b.name == "Shin" + s).position;
            var ankle = bones.First(b => b.name == "Foot" + s).position;
            return Vector3.Angle(hip - knee, ankle - knee);
        }

        [Test]
        public void Standing_KneesAreNearlyStraight()
        {
            // Plain standing, a marshaller signalling with wands (arms high, wide, low), and the container loader operator
            // at the controls with pinned feet. A human stands on nearly locked knees; 30 degrees of flex reads as a crouch.
            var marshal = new[]
            {
                (new Vector3(-.17f, 1.86f, .02f), new Vector3(.17f, 1.86f, .02f)),
                (new Vector3(-.35f, 1.44f, .20f), new Vector3(.35f, 1.44f, .20f)),
                (new Vector3(-.72f, 1.38f, .04f), new Vector3(.35f, 1.44f, .20f)),
                (new Vector3(-.48f, 1.12f, .12f), new Vector3(.48f, 1.12f, .12f)),
                (new Vector3(-.24f, .86f, .04f), new Vector3(.13f, 1.69f, .18f)),
            };
            var loader = AirCity.EditorTools.Vehicles.VehicleSetup.Type("ContainerLoader");
            var station = AirCity.Vehicles.BaggageCrew.Workstation(loader, new AirCity.Vehicles.BaggageVehiclePose(new Vector2(10, 20), new Vector2(1, 0)));
            var cases = new List<(string name, System.Func<float, RampCrewPose> pose, float least)>
            {
                ("standing", _ => RampCrewPose.Standing(new Vector3(1, 0, 2), Vector3.forward), 170f),
                ("marshaller", t =>
                {
                    var p = RampCrewPose.Standing(new Vector3(-3, 0, 5), Vector3.right);
                    int k = Mathf.Min(marshal.Length - 1, (int)(t / 1.2f)); float u = Mathf.SmoothStep(0, 1, (t - k * 1.2f) / .5f);
                    var from = marshal[Mathf.Max(0, k - 1)]; var to = marshal[k];
                    p.hands = true; p.leftGrip = p.rightGrip = RampCrewGrip.Wand;
                    // The waved signals (come ahead, slow down) move the wands up and down.
                    var wave = Vector3.up * (k == 1 || k == 3 ? .15f : 0f) * Mathf.Sin(t * Mathf.PI * 1.3f);
                    p.leftHand = Vector3.Lerp(from.Item1, to.Item1, u) + wave;
                    p.rightHand = Vector3.Lerp(from.Item2, to.Item2, u) + wave;
                    return p;
                }, 170f),
                ("loader operator", _ => station, 165f),
            };
            foreach (var (name, pose, least) in cases)
            {
                var rig = Rig(3); var bones = Bones(rig);
                float lowest = 180f, narrow = 9f, wide = 0f, toeOut = 0f;
                for (int s = 0; s <= 360; s++)
                {
                    var p = pose(s / 60f);
                    rig.Apply(p, s == 0 ? 0f : 1f / 60f);
                    if (s < 30) continue;
                    for (int i = 0; i < 2; i++)
                    {
                        lowest = Mathf.Min(lowest, Knee(bones, i));
                        rig.FootContact(i, out var heel, out var ball);
                        var along = rig.transform.InverseTransformDirection(ball - heel);
                        toeOut = Mathf.Max(toeOut, (i == 0 ? -1f : 1f) * Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg);
                    }
                    float gap = Mathf.Abs(rig.transform.InverseTransformPoint(bones.First(b => b.name == "Foot_R").position).x -
                                          rig.transform.InverseTransformPoint(bones.First(b => b.name == "Foot_L").position).x);
                    narrow = Mathf.Min(narrow, gap); wide = Mathf.Max(wide, gap);
                    if (p.hands)
                    {
                        Assert.That(Vector3.Distance(rig.LeftPalm, p.position + p.rotation * p.leftHand), Is.LessThan(.002f), name + " left palm");
                        Assert.That(Vector3.Distance(rig.RightPalm, p.position + p.rotation * p.rightHand), Is.LessThan(.002f), name + " right palm");
                    }
                }
                Debug.Log($"Standing {name}: knees at least {lowest:0.0} deg, ankles {narrow:0.000}-{wide:0.000} m apart, toes out at most {toeOut:0.0} deg");
                Assert.That(lowest, Is.GreaterThanOrEqualTo(least), name + ": standing knees are nearly straight");
                if (name == "loader operator") continue; // its feet are where the vehicle's workstation puts them
                Assert.That(narrow, Is.GreaterThanOrEqualTo(.18f), name + ": feet not together");
                Assert.That(wide, Is.LessThanOrEqualTo(.26f), name + ": feet not straddled");
                Assert.That(toeOut, Is.LessThanOrEqualTo(10f), name + ": toes turned out at most 10 degrees");
            }
        }

        [Test]
        public void Walking_KneesFollowAHumanGait()
        {
            // Through each stride at 1.2-1.3 m/s: nearly straight at heel strike, a small give as the leg takes weight,
            // straight again before push-off, 55-70 degrees of flexion in swing; the pelvis rises and falls 3-5 cm.
            foreach (var (motion, from, to, loading) in new[] { (RampCrewMotionScripts.WalkTurn(), 1.9f, 4.4f, 158f), (RampCrewMotionScripts.PickCarryPlace(), 3.7f, 5.1f, 155f) })
            {
                var rig = Rig(); var bones = Bones(rig); var hips = bones.First(b => b.name == "Hips");
                var swinging = new bool[2]; var landed = new float[2]; var lifted = new float[2]; var stanceMin = new float[2]; var stanceMax = new float[2]; var swingMax = new float[2];
                float low = 9f, high = 0f; int strides = 0;
                const float step = 1f / 60f;
                for (int s = 0; s * step < to; s++)
                {
                    float t = s * step;
                    rig.Apply(motion.pose(t), s == 0 ? 0f : step);
                    bool inside = t >= from;
                    if (inside)
                    {
                        float y = rig.transform.InverseTransformPoint(hips.position).y; low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        float knee = Knee(bones, i); bool now = rig.FootSwinging(i);
                        if (!now && swinging[i])
                        {
                            // Heel strike.
                            if (inside) Assert.That(knee, Is.GreaterThanOrEqualTo(170f), $"{motion.name}: leg {i} lands straight at {t:0.00}s");
                            if (lifted[i] >= from) Assert.That(swingMax[i], Is.InRange(50f, 75f), $"{motion.name}: leg {i} swing flexion before {t:0.00}s");
                            landed[i] = t; stanceMin[i] = 180f; stanceMax[i] = 0f; swingMax[i] = 0f;
                        }
                        if (now && !swinging[i]) lifted[i] = t;
                        if (now && !swinging[i] && landed[i] > 0f && landed[i] >= from)
                        {
                            // Toe-off: the stance that just ended gave a little under load and straightened again.
                            Assert.That(stanceMin[i], Is.InRange(loading, 170f), $"{motion.name}: leg {i} loading response before {t:0.00}s");
                            Assert.That(stanceMax[i], Is.GreaterThanOrEqualTo(170f), $"{motion.name}: leg {i} straightens in stance before {t:0.00}s");
                            strides++;
                        }
                        if (!now && landed[i] > 0f && t - landed[i] < .3f) stanceMin[i] = Mathf.Min(stanceMin[i], knee);
                        if (!now && landed[i] > 0f && t - landed[i] >= .3f && rig.FootSwinging(1 - i)) stanceMax[i] = Mathf.Max(stanceMax[i], knee);
                        if (now) swingMax[i] = Mathf.Max(swingMax[i], 180f - knee);
                        swinging[i] = now;
                    }
                }
                Debug.Log($"Gait {motion.name}: pelvis {low:0.000}-{high:0.000} m over {strides} checked stances");
                Assert.That(strides, Is.GreaterThanOrEqualTo(2), motion.name);
                Assert.That(high - low, Is.InRange(.025f, .06f), motion.name + ": pelvis rises over the stance leg and drops in double support");
            }
        }

        [Test]
        public void Apply_DoesNotAllocateAndIsCheap()
        {
            var rigs = Enumerable.Range(0, 24).Select(i => Rig(i)).ToArray();
            var motion = RampCrewMotionScripts.PickCarryPlace();
            for (int i = 0; i < rigs.Length; i++) rigs[i].Apply(motion.pose(0), 0);
            var poses = Enumerable.Range(0, 200).Select(s => motion.pose(s / 60f)).ToArray();
            for (int s = 0; s < 20; s++) foreach (var rig in rigs) rig.Apply(poses[s], 1f / 60f); // warm up
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int applies = 0;
            for (int s = 20; s < 200; s++) foreach (var rig in rigs) { rig.Apply(poses[s], 1f / 60f); applies++; }
            watch.Stop();
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            double micro = watch.Elapsed.TotalMilliseconds * 1000.0 / applies;
            Debug.Log($"RampCrewRig.Apply: {micro:0.0} us per worker over {applies} applies (editor, Mono, includes transform writes); {allocated} bytes allocated");
            Assert.That(allocated, Is.EqualTo(0), "Apply must not allocate");
        }
    }
}
