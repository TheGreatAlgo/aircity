using System;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.EditorTools.Characters
{
    /// <summary>A carried prop for a scripted motion: bottom centre and facing, as the baggage producers express bags.</summary>
    public struct RampCrewMotionProp { public bool visible; public Vector3 bottom; public Quaternion rotation; }

    /// <summary>A pose producer scripted directly in game seconds, independent of the airport simulation.</summary>
    public sealed class RampCrewMotion
    {
        public string name;
        public float duration;
        public Func<float,RampCrewPose> pose;
        public Func<float,RampCrewMotionProp> bag;
        /// <summary>Static scenery (seat, shelves, wheel) as boxes: centre, size, rotation.</summary>
        public (Vector3 centre, Vector3 size, Quaternion rotation)[] props = Array.Empty<(Vector3, Vector3, Quaternion)>();
    }

    /// <summary>
    /// Human-pace motions written the way the baggage producers write poses (booleans, eased hip height and lean, hand
    /// targets lerped from an idle point, stride and phase while moving), so they exercise the rig the way the game does.
    /// Shared by <see cref="RampCrewMotionReview"/> and the motion tests.
    /// </summary>
    public static class RampCrewMotionScripts
    {
        public static readonly Vector3 BagSize = new Vector3(.38f,.26f,.50f);
        public static RampCrewMotion[] Human => new[] { WalkTurn(), SidestepBackward(), PickCarryPlace(), KneelShuffleReach(), SitWheelStand(), Idle() };
        public static RampCrewMotion[] All => new[] { WalkTurn(), SidestepBackward(), PickCarryPlace(), KneelShuffleReach(), SitWheelStand(), Idle(), Dash() };

        static float Ease(float t) { t = Mathf.Clamp01(t); return t*t*(3f-2f*t); }
        static float Span(float t, float start, float length) => Mathf.Clamp01((t-start)/length);

        /// <summary>Distance covered after t seconds of a trapezoid speed profile (ease in, cruise, ease out).</summary>
        static float Travel(float t, float distance, float speed, float ramp)
        {
            float cruise = Mathf.Max(0, distance/speed-ramp), total = cruise+2*ramp;
            t = Mathf.Clamp(t,0,total);
            float a = speed/ramp;
            if (t < ramp) return .5f*a*t*t;
            if (t < ramp+cruise) return .5f*speed*ramp+speed*(t-ramp);
            float d = total-t; return distance-.5f*a*d*d;
        }
        static float TravelTime(float distance, float speed, float ramp) => Mathf.Max(0,distance/speed-ramp)+2*ramp;
        static float TravelSpeed(float t, float distance, float speed, float ramp)
        {
            float cruise = Mathf.Max(0, distance/speed-ramp), total = cruise+2*ramp;
            if (t <= 0 || t >= total) return 0;
            if (t < ramp) return speed*t/ramp;
            if (t > ramp+cruise) return speed*(total-t)/ramp;
            return speed;
        }

        static RampCrewPose Stand(Vector3 at, float yaw) => RampCrewPose.Standing(at, Quaternion.Euler(0,yaw,0)*Vector3.forward);
        // Producer-style walking hint: stride while moving, phase from distance (1.5 cycles per second at 1.3 m/s).
        static void Gait(ref RampCrewPose p, float distance, float speed, float stride = .8f)
        { if (speed > .02f) { p.stride = stride*Mathf.Clamp01(speed/1.3f); p.phase = distance/.867f; } }
        static Vector3 IdleHand(RampCrewPose p, float side) => new Vector3(side*.245f, p.hipHeight-.17f, .025f);

        /// <summary>BaggageCrew.Hands: palms on either side of a bag, 13 cm above its bottom.</summary>
        static void Hands(ref RampCrewPose pose, Vector3 bottom, Quaternion rotation, float reach)
        {
            var inverse = Quaternion.Inverse(pose.rotation);
            var span = rotation*Vector3.right*(BagSize.x*.5f+.01f);
            if (Vector3.Dot(span,pose.rotation*Vector3.right) < 0) span = -span;
            var left = inverse*(bottom+Vector3.up*(BagSize.y*.5f)-span-pose.position);
            var right = inverse*(bottom+Vector3.up*(BagSize.y*.5f)+span-pose.position);
            pose.hands = true;
            pose.leftHand = Vector3.Lerp(IdleHand(pose,-1),left,Ease(reach));
            pose.rightHand = Vector3.Lerp(IdleHand(pose,1),right,Ease(reach));
        }

        /// <summary>Walk 6 m at 1.3 m/s, stop, turn 180 degrees on the spot, walk back.</summary>
        public static RampCrewMotion WalkTurn()
        {
            const float distance = 6f, speed = 1.3f, ramp = .6f;
            float walk = TravelTime(distance,speed,ramp);
            float t1 = .5f, t2 = t1+walk, t3 = t2+.5f, t4 = t3+1.1f, t5 = t4+.4f, t6 = t5+walk, end = t6+.8f;
            return new RampCrewMotion { name = "walk-turn", duration = end, pose = t =>
            {
                if (t < t2)
                {
                    float d = Travel(t-t1,distance,speed,ramp); var p = Stand(new Vector3(0,0,d),0);
                    Gait(ref p,d,TravelSpeed(t-t1,distance,speed,ramp)); return p;
                }
                if (t < t5) return Stand(new Vector3(0,0,distance),180f*Ease(Span(t,t3,t4-t3)));
                {
                    float d = Travel(t-t5,distance,speed,ramp); var p = Stand(new Vector3(0,0,distance-d),180);
                    Gait(ref p,d,TravelSpeed(t-t5,distance,speed,ramp)); return p;
                }
            }};
        }

        /// <summary>Side-step 2 m to the right, pause, walk backwards 2 m, all facing forward.</summary>
        public static RampCrewMotion SidestepBackward()
        {
            float side = TravelTime(2,.6f,.4f), back = TravelTime(2,.8f,.5f);
            float t1 = .5f, t2 = t1+side, t3 = t2+.6f, t4 = t3+back, end = t4+.8f;
            return new RampCrewMotion { name = "sidestep-backward", duration = end, pose = t =>
            {
                float x = Travel(t-t1,2,.6f,.4f), z = -Travel(t-t3,2,.8f,.5f);
                var p = Stand(new Vector3(x,0,z),0);
                if (t < t3) Gait(ref p,x,TravelSpeed(t-t1,2,.6f,.4f),.5f); else Gait(ref p,-z,TravelSpeed(t-t3,2,.8f,.5f),.5f);
                return p;
            }};
        }

        /// <summary>Pick a suitcase whose grip is 0.4 m up, carry it 3 m, set it on a 1.0 m shelf, walk back.</summary>
        public static RampCrewMotion PickCarryPlace()
        {
            var pickBottom = new Vector3(0,.28f,.52f);
            var shelfBottom = new Vector3(0,1.0f,-3.56f);
            var carry = new Vector3(0,.80f,.36f);
            float walk = TravelTime(3,1.2f,.5f);
            float reach = .6f, grip = reach+.8f, lift = grip+.25f, turn = lift+.7f, go = turn+.8f, arrive = go+walk,
                place = arrive+.15f, release = place+.75f, free = release+.35f, turnBack = free+.15f, back = turnBack+.8f,
                home = back+walk, end = home+.8f;
            RampCrewPose PoseAt(float t, out RampCrewMotionProp bag)
            {
                bag = new RampCrewMotionProp { visible = true, bottom = pickBottom, rotation = Quaternion.identity };
                RampCrewPose p;
                if (t < lift)
                {
                    float u = Ease(Span(t,reach,grip-reach)); p = Stand(Vector3.zero,0);
                    p.hipHeight = Mathf.Lerp(.94f,.64f,u); p.lean = Mathf.Lerp(0,32,u);
                    if (t >= reach) Hands(ref p,bag.bottom,bag.rotation,Span(t,reach,grip-reach));
                    return p;
                }
                if (t < turn)
                {
                    float u = Ease(Span(t,lift,turn-lift)); p = Stand(Vector3.zero,0);
                    p.hipHeight = Mathf.Lerp(.64f,.94f,u); p.lean = Mathf.Lerp(32,0,u);
                    bag.bottom = Vector3.Lerp(pickBottom,p.position+p.rotation*carry,u);
                }
                else if (t < go) { p = Stand(Vector3.zero,180f*Ease(Span(t,turn,go-turn))); bag.bottom = p.position+p.rotation*carry; bag.rotation = p.rotation; }
                else if (t < place)
                {
                    float d = Travel(t-go,3,1.2f,.5f); p = Stand(new Vector3(0,0,-d),180);
                    Gait(ref p,d,TravelSpeed(t-go,3,1.2f,.5f)); bag.bottom = p.position+p.rotation*carry; bag.rotation = p.rotation;
                }
                else if (t < release)
                {
                    float u = Ease(Span(t,place,release-place)); p = Stand(new Vector3(0,0,-3),180);
                    p.hipHeight = Mathf.Lerp(.94f,.88f,Mathf.Sin(u*Mathf.PI)); p.lean = 14f*Mathf.Sin(u*Mathf.PI);
                    bag.bottom = Vector3.Lerp(p.position+p.rotation*carry,shelfBottom,u); bag.rotation = p.rotation;
                }
                else
                {
                    bag.bottom = shelfBottom; bag.rotation = Quaternion.Euler(0,180,0);
                    if (t < free)
                    {
                        p = Stand(new Vector3(0,0,-3),180); Hands(ref p,bag.bottom,bag.rotation,1-Span(t,release,free-release));
                        return p;
                    }
                    if (t < back) return Stand(new Vector3(0,0,-3),180f+180f*Ease(Span(t,turnBack,back-turnBack)));
                    float d = Travel(t-back,3,1.2f,.5f); p = Stand(new Vector3(0,0,-3+d),0);
                    Gait(ref p,d,TravelSpeed(t-back,3,1.2f,.5f)); return p;
                }
                Hands(ref p,bag.bottom,bag.rotation,1); return p;
            }
            return new RampCrewMotion
            {
                name = "pick-carry-place", duration = end,
                pose = t => PoseAt(t,out _), bag = t => { PoseAt(t,out var b); return b; },
                props = new[]
                {
                    (new Vector3(0,.14f,.62f), new Vector3(.9f,.28f,.7f), Quaternion.identity),
                    (new Vector3(0,.5f,-3.70f), new Vector3(1.0f,1.0f,.5f), Quaternion.identity),
                },
            };
        }

        /// <summary>Stand to kneel, shuffle 1 m on the knees, reach forward to a bag on the floor, stand.</summary>
        public static RampCrewMotion KneelShuffleReach()
        {
            var bagBottom = new Vector3(0,0,1.70f);
            float down = .5f, knelt = down+1.3f, shuffle = knelt+.4f, shuffled = shuffle+3f, reach = shuffled+.3f,
                held = reach+.9f, release = held+.6f, free = release+.6f, up = free+.1f, stood = up+1.4f, end = stood+.6f;
            return new RampCrewMotion
            {
                name = "kneel-shuffle-reach", duration = end,
                bag = _ => new RampCrewMotionProp { visible = true, bottom = bagBottom, rotation = Quaternion.identity },
                pose = t =>
                {
                    float z = Mathf.Lerp(0,1,Ease(Span(t,shuffle,shuffled-shuffle)));
                    var p = Stand(new Vector3(0,0,z),0);
                    if (t < down) return p;
                    if (t < up)
                    {
                        float u = Ease(Span(t,down,knelt-down)); p.kneeling = true;
                        p.hipHeight = Mathf.Lerp(.94f,.38f,u); p.lean = Mathf.Lerp(0,35,u);
                        if (t > shuffle && t < shuffled) { p.stride = .35f; p.phase = z*2f; }
                        if (t >= reach && t < free)
                        {
                            float r = t < held ? Span(t,reach,held-reach) : t < release ? 1 : 1-Span(t,release,free-release);
                            p.lean = Mathf.Lerp(35,58,Ease(r)); Hands(ref p,bagBottom,Quaternion.identity,r);
                        }
                        return p;
                    }
                    float s = Ease(Span(t,up,stood-up)); p.hipHeight = Mathf.Lerp(.38f,.94f,s); p.lean = Mathf.Lerp(35,0,s);
                    return p;
                },
            };
        }

        /// <summary>Sit on a seat, put both hands on a wheel and steer, release, stand back up.</summary>
        public static RampCrewMotion SitWheelStand()
        {
            var seatHips = new Vector3(0,.62f,0);
            var standAt = new Vector3(0,0,.14f);
            var wheel = new Vector3(0,.98f,.44f);
            const float radius = .19f;
            float sit = .6f, sat = sit+1.2f, grab = sat+.2f, gripped = grab+.6f, steer = gripped+2f, letGo = steer+.5f,
                rise = letGo+.15f, risen = rise+1.2f, end = risen+.7f;
            Vector3 Pedal(float side) => new Vector3(side*.12f,0,.50f);
            return new RampCrewMotion
            {
                name = "sit-wheel-stand", duration = end,
                props = new[]
                {
                    (new Vector3(0,.27f,-.06f), new Vector3(.5f,.54f,.46f), Quaternion.identity),
                    (new Vector3(0,.70f,-.30f), new Vector3(.5f,.60f,.08f), Quaternion.Euler(-12,0,0)),
                    (wheel, new Vector3(.40f,.03f,.40f), Quaternion.Euler(-60,0,0)),
                    (new Vector3(0,.04f,.55f), new Vector3(.4f,.08f,.12f), Quaternion.identity),
                },
                pose = t =>
                {
                    float u = Ease(Span(t,sit,sat-sit)) * (1-Ease(Span(t,rise,risen-rise)));
                    var seated = seatHips-Vector3.up*.94f;
                    var p = Stand(Vector3.Lerp(standAt,seated,u),0);
                    p.seated = t >= sit && t < rise;
                    for (int i = 0; i < 2; i++)
                    {
                        float side = i == 0 ? -1 : 1;
                        var foot = Vector3.Lerp(standAt+new Vector3(side*.11f,0,0),Pedal(side),u)-p.position;
                        if (i == 0) p.leftFoot = foot; else p.rightFoot = foot;
                    }
                    if (t >= grab && t < letGo)
                    {
                        float r = t < gripped ? Span(t,grab,gripped-grab) : t < steer ? 1 : 1-Span(t,steer,letGo-steer);
                        float turn = t > gripped && t < steer ? 25f*Mathf.Sin((t-gripped)/(steer-gripped)*Mathf.PI*2) : 0;
                        var tilt = Quaternion.Euler(-60,0,0);
                        p.hands = true;
                        for (int i = 0; i < 2; i++)
                        {
                            float side = i == 0 ? -1 : 1;
                            var rim = wheel+tilt*(Quaternion.Euler(0,turn,0)*new Vector3(side*radius,0,0))-p.position;
                            var lap = new Vector3(side*.16f,.94f+.02f,.22f);
                            var target = Vector3.Lerp(lap,rim,Ease(r));
                            if (i == 0) p.leftHand = target; else p.rightHand = target;
                        }
                    }
                    return p;
                },
            };
        }

        /// <summary>Time-compressed work as the player sees it at WorkRate 18: dash 6 m to a belt and back in 0.6 s, twice.</summary>
        public static RampCrewMotion Dash()
        {
            const float speed = 21.6f, distance = 6f;
            float leg = distance / speed;
            return new RampCrewMotion { name = "dash", duration = 2.4f, pose = t =>
            {
                float u = Mathf.Repeat(t, 1.2f), z;
                bool back = false;
                if (u < .1f) z = 0;
                else if (u < .1f + leg) z = (u - .1f) * speed;
                else if (u < .6f) z = distance;
                else if (u < .6f + leg) { z = distance - (u - .6f) * speed; back = true; }
                else z = 0;
                var p = Stand(new Vector3(0, 0, z), back || u >= .6f + leg ? 180 : 0);
                p.stride = .65f; p.phase = t * 27f; return p;
            }};
        }

        /// <summary>Five seconds of standing with nothing to do.</summary>
        public static RampCrewMotion Idle() => new RampCrewMotion { name = "idle", duration = 5f, pose = _ => Stand(Vector3.zero,0) };
    }
}
