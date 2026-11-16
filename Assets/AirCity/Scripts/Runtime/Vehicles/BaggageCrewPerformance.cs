using System.Collections.Generic;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// The bulk crew as the player sees them. The simulation (<see cref="BaggageCrew"/>, <see cref="BaggageTurnaround"/>) stays
    /// the single source of truth and runs in compressed time; these performers act it out at human pace in game seconds:
    /// they get off and on the vehicles, walk to their posts, and pick, carry and place bags while the simulation transfers,
    /// one carried bag standing for many (<see cref="BaggageBagFlow"/> shows the rest). They read the simulation's own
    /// future (bags left times the transfer interval) to set off back to their seats in time, hurry when they could not
    /// know, and are placed directly only as a last resort. Nothing here is read by the simulation.
    /// A hired second cart handler rides on the back of the tractor and gets off when there is time to work (the simulation
    /// never boards them). At a depot the team stays aboard: the bay's hall handler (<see cref="BaggageBayHand"/>) works the
    /// carts and the depot conveyor at the same pace while the train stands in the bay.
    /// </summary>
    public sealed class BaggageCrewPerformance
    {
        // Human pace, game seconds and metres per second.
        const float Walk = 1.35f, CarryWalk = 1.1f, Stairs = 1.15f, Crouched = .75f, Kneeling = .55f, Jog = 2.6f;
        const float Accel = 1.6f, Decel = 1.4f, TurnRate = 220f, CarryTurnRate = 160f;
        const float ReachTime = .7f, LiftTime = .65f, PlaceTime = .75f, ReleaseTime = .45f;
        const float UnseatTime = .55f, RiseTime = 1.0f, StepTime = .6f, SitTime = 1.1f, GripTime = .5f, KneelTime = 1.1f;
        const float Dismount = UnseatTime+RiseTime+StepTime;
        /// <summary>The second cart handler rides sitting on the tractor's rear deck: shorter ways on and off.</summary>
        const float RiderUnseat = .25f, RiderRise = .8f, RiderStep = .25f, RiderUp = .45f, RiderSit = .75f;
        const float RiderDismount = RiderUnseat+RiderRise+RiderStep;
        /// <summary>Tractor-local hip point of the rider (side outward), rear deck top 0.87 m, and the foot drop.</summary>
        static readonly Vector3 RiderHip = new Vector3(.50f,.99f,-.40f);
        /// <summary>Loader-local hip point of the second hold handler, sitting on the power unit's cover (top 1.01 m) on
        /// the operator's side, behind the seat.</summary>
        static readonly Vector3 LoaderRiderHip = new Vector3(-.80f,1.13f,-.30f);
        /// <summary>Riders leave the work this much earlier than the first handler of their side, so the two are not on the
        /// same way back at once (the rider is seated before the driver passes, out of the hold before the operator).</summary>
        const float RiderLead = 6f, HoldRiderLead = 6f;
        /// <summary>Two people keep at least this far apart (hip to hip); a walker slows to a stop short of a colleague ahead.</summary>
        const float Clear = .8f;
        /// <summary>Least time worth getting off for: a person does not climb down for a few seconds of work.</summary>
        const float MinWork = 4f;
        /// <summary>Furthest a handler takes a bag from across the cart: the near column only.</summary>
        const float CartReach = .80f;
        /// <summary>Seconds of slack kept when heading back to a seat.</summary>
        const float Margin = .8f;

        public static int Placements { get; private set; }
        /// <summary>Fastest root speed of a standing (not seated) performer, game m/s, and what it was doing.</summary>
        public static float MaxStandingSpeed { get; private set; }
        public static string MaxStandingWhat { get; private set; }
        public static int DirectBags { get; private set; }
        public static void ResetCounters() { Placements = DirectBags = 0; MaxStandingSpeed = 0; MaxStandingWhat = null; Steps = 0; ticks = 0; bytes = 0; }
        /// <summary>Review measurements: steps taken, mean cost and managed allocation per fleet step.</summary>
        public static int Steps { get; private set; }
        static long ticks, bytes;
        public static float StepMicroseconds => Steps > 0 ? (float)(ticks*1e6/System.Diagnostics.Stopwatch.Frequency/Steps) : 0f;
        public static float StepBytes => Steps > 0 ? bytes/(float)Steps : 0f;
        internal static void CountDirect(int bags) => DirectBags += bags;

        enum Act : byte { Seated, Unseat, Rise, StepDown, Walk, Idle, Reach, Lift, Place, Release, StepUp, SitDown, Grip, External }
        enum Goal : byte { Seat, Post, Carry, Return }

        struct Waypoint { public Vector3 p; public float hip, lean, kneel, speed; public bool gate; }

        sealed class Performer
        {
            public bool ground; public int worker;
            public bool rider;                                // rides on the tractor's rear deck (second cart handler)
            public bool bay; public Vector3 home; public float homeYaw;   // a depot bay's hall handler and its waiting spot
            public int bagSession; public float stay;         // bag session of the owned bag; seconds the train stays (bay)
            public bool late;                                 // carrying an arrivals bag after its state ended (bay)
            public bool gateWait;                             // stopped short of the belt foot while a colleague uses it
            public float yielding;                            // seconds spent stopped for a colleague ahead
            public Performer blockedBy;                       // the colleague this one is waiting for
            public float sideSign;                            // which way round a colleague in the way
            public Act act = Act.Seated, after; public Goal goal;
            public float t;                                   // seconds in the current act
            public Vector3 position; public float yaw, speed;
            public float hip = .94f, lean, kneel, sit = 1f, feet = 1f, reach, load;
            public readonly List<Waypoint> path = new List<Waypoint>(24);
            public int index; public float finalYaw; public bool hasFinal, hurry;
            public Vector3 segmentStart; public float startHip = .94f, startLean, startKneel;
            // The bag the performer owns: reserved in place, in the hands, or being set down.
            public bool owns, held; public int variant, cart = -1, slot = -1; public bool across;
            public Vector3 bagBottom, bagFrom, bagTo; public Quaternion bagRotation, rotFrom, rotTo; public Vector3 size;
            public Vector3 carryOffset;                       // bag bottom in the actor frame while carried
            public bool atPost;                               // set once at the post, cleared when recalled
            public float pickHip, pickLean;
            public RampCrewPose seat;                         // last seated workstation pose
            public Vector3 stepPoint, groundPoint; public float stepYaw;
            public Vector3 leftFootFrom, rightFootFrom;
            public bool visible = true, snap, draw, willTake;
            public RampCrewPose pose;
            public Vector3 last; public bool lastValid;
        }

        readonly BaggageFleet fleet;
        readonly BaggageBagFlow flow;
        readonly Performer[] grounds = { new Performer { ground = true, worker = 0 }, new Performer { ground = true, worker = 1, rider = true } };
        readonly Performer[] holds = { new Performer { worker = 0 }, new Performer { worker = 1, rider = true } };
        /// <summary>The depot bay's hall handler while this fleet's train stands in the bay (worker 2 of the ground side).</summary>
        readonly Performer bay = new Performer { ground = true, worker = 2, bay = true, act = Act.Idle, sit = 0f, feet = 0f, visible = false };
        BaggageBayHand hand;
        readonly List<RampCrewPose> route = new List<RampCrewPose>(32);
        /// <summary>A stand-in performer at the hold stand, for timing the way out before going in.</summary>
        readonly Performer holdStandProbe = new Performer();
        readonly Vector3[] sizes;
        bool started;

        public BaggageBagFlow Flow => flow;
        public BaggageCrewPerformance(BaggageFleet fleet, Vector3[] bagSizes)
        {
            this.fleet = fleet; flow = new BaggageBagFlow(fleet);
            sizes = bagSizes != null && bagSizes.Length > 0 ? bagSizes : new[] { new Vector3(.44f,.67f,.29f) };
        }

        BaggageCrew Crew => fleet.Crew;
        BaggageTurnaround Job => fleet.Job;
        Vector3 Size(int variant) => sizes[variant%sizes.Length];
        static float Ease(float t) { t = Mathf.Clamp01(t); return t*t*(3f-2f*t); }
        static float Yaw(Vector3 d) => Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static Quaternion YawRotation(float yaw) => Quaternion.Euler(0,yaw,0);

        /// <summary>Ground worker 2 is the depot bay's hall handler.</summary>
        Performer Get(bool ground, int worker) => ground ? worker >= 2 ? bay : grounds[worker] : holds[worker];
        public RampCrewPose GroundPose(int worker) => Get(true,worker).pose;
        public RampCrewPose HoldPose(int worker) => holds[worker].pose;
        public bool GroundVisible(int worker) => Get(true,worker).visible;
        public bool HoldVisible(int worker) => holds[worker].visible;
        /// <summary>The performer's bag, if it is drawing one (in place, in the hands or being set down).</summary>
        public bool Bag(bool ground, int worker, out Vector3 bottom, out Quaternion rotation, out int variant, out bool inHands)
        {
            var p = Get(ground,worker);
            bottom = p.bagBottom; rotation = p.bagRotation; variant = p.variant; inHands = p.held;
            return p.owns && p.draw && p.visible;
        }
        /// <summary>Diagnostic description of a performer (allocates; review tools only).</summary>
        public string Describe(bool ground, int worker)
        {
            var p = Get(ground,worker);
            return $"{(ground ? "G" : "H")}{worker} {p.act}/{p.goal}{(p.gateWait ? " gate" : "")}{(p.yielding > 0 ? $" yield {p.yielding:0.0}" : "")} post {p.atPost} at {p.position.x:0.0},{p.position.y:0.00},{p.position.z:0.0} v {p.speed:0.00} " +
                $"path {p.index}/{p.path.Count} bag {(p.owns ? p.held ? "held" : "own" : "-")} rem {flow.RemainingHandling():0.0} back {(ground ? GroundReturnSeconds(p) : HoldExitSeconds(p)):0.0} hd {HoldDeadline():0.0}";
        }
        /// <summary>The performer is off its vehicle (walking, working, kneeling), not seated or riding.</summary>
        public bool OnFoot(bool ground, int worker)
        {
            var p = Get(ground,worker);
            return p.visible && p.act != Act.Seated && p.act != Act.Grip && p.act != Act.External;
        }
        /// <summary>Palm targets of the last pose, airport frame (for measuring how well hands stay on bags).</summary>
        public bool HandTargets(bool ground, int worker, out Vector3 left, out Vector3 right)
        {
            var p = Get(ground,worker); var pose = p.pose;
            left = pose.position+pose.rotation*pose.leftHand; right = pose.position+pose.rotation*pose.rightHand;
            return p.owns && p.held && pose.leftReach > .99f;
        }

        // ================================================================== step

        /// <summary>Advance the performance by <paramref name="dt"/> game seconds after the simulation stepped.</summary>
        public void Step(float dt)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp(), allocated = System.GC.GetAllocatedBytesForCurrentThread();
            Advance(dt);
            ticks += System.Diagnostics.Stopwatch.GetTimestamp()-start; bytes += System.GC.GetAllocatedBytesForCurrentThread()-allocated; Steps++;
        }
        void Advance(float dt)
        {
            if (!started) { Start(); started = true; }
            flow.Step(dt);
            bool blocked = fleet.Station.accessBlocked;
            // Long frames (fast-forward) are taken in small human steps so paths and settles stay exact.
            float left = blocked ? 0f : dt;
            do
            {
                float h = Mathf.Min(left,.05f); left -= h;
                for (int i = 0; i < 2; i++) StepGround(grounds[i],h);
                StepBay(bay,h);
                for (int i = 0; i < 2; i++) StepHold(holds[i],h);
            } while (left > 1e-5f);
            // Flags the bag flow uses next step.
            var g = grounds[0]; var k = holds[0];
            // At a depot the bay hand standing by the conveyor's near end works it, carrying or not.
            bool byBelt = bay.visible && flow.Depot;
            flow.cartPost = Working(grounds[0]) || Working(grounds[1]) || Working(bay) || byBelt;
            // The hold handlers have no part in a depot handover.
            bool hold = !flow.Depot;
            flow.holdPost = hold && (Working(holds[0]) || Working(holds[1]));
            flow.cartReceiving = Receiving(grounds[0]) || Receiving(grounds[1]) || Receiving(bay);
            flow.holdReceiving = hold && (Receiving(holds[0]) || Receiving(holds[1]));
            flow.cartEta = byBelt ? 0f : Mathf.Min(Mathf.Min(Eta(grounds[0]),Eta(grounds[1])),Eta(bay));
            flow.holdEta = hold ? Mathf.Min(Eta(holds[0]),Eta(holds[1])) : float.PositiveInfinity;
            flow.cartHand = Ready(grounds[0]) || Ready(grounds[1]) || Ready(bay);
            flow.holdHand = hold && (Ready(holds[0]) || Ready(holds[1]));
            flow.footReserved = PlacingOnBelt(grounds[0]) || PlacingOnBelt(grounds[1]) || PlacingOnBelt(bay);
            flow.topReserved = hold && (PlacingOnBelt(holds[0]) || PlacingOnBelt(holds[1]));
            int hands = bay.held ? 1 : 0; foreach (var p in grounds) if (p.held) hands++; foreach (var p in holds) if (p.held) hands++;
            flow.handBags = hands;
            for (int i = 0; i < 2; i++) { Compose(grounds[i]); Compose(holds[i]); }
            Compose(bay);
            if (hand != null) { hand.pose = bay.pose; hand.position = bay.position; hand.yaw = bay.yaw; hand.placed = true; }
            if (dt > 0f && !blocked) { for (int i = 0; i < 2; i++) { Measure(grounds[i],dt); Measure(holds[i],dt); } if (bay.visible) Measure(bay,dt); }
        }
        static void Measure(Performer p, float dt)
        {
            bool seated = p.act == Act.Seated || p.act == Act.Grip || p.act == Act.External;
            if (!seated && p.lastValid)
            {
                float v = Vector3.Distance(p.pose.position,p.last)/dt;
                if (v > MaxStandingSpeed) { MaxStandingSpeed = v; MaxStandingWhat = (p.ground ? "ground " : "hold ")+p.act+"/"+p.goal; }
            }
            p.last = p.pose.position; p.lastValid = !seated;
        }

        void Start()
        {
            // Drivers start with their hands on the controls (riders have none).
            foreach (var p in grounds) { Seat(p); p.position = p.seat.position; p.yaw = Yaw(p.seat.rotation*Vector3.forward); p.visible = p.worker == 0; p.reach = p.rider ? 0f : 1f; }
            foreach (var p in holds) { Seat(p); p.position = p.seat.position; p.yaw = Yaw(p.seat.rotation*Vector3.forward); p.visible = p.worker == 0; p.reach = p.rider ? 0f : 1f; }
        }

        /// <summary>A bag session the ground handlers work: at the aircraft, or a depot handover.</summary>
        bool GroundSession => flow.Session && Job != null;
        /// <summary>A bag session the hold handlers work (aircraft only).</summary>
        bool HoldSession => flow.Session && !flow.Depot && Job != null;
        static bool Working(Performer p) => p.atPost && p.goal != Goal.Return && p.goal != Goal.Seat && p.act != Act.External;
        bool Receiving(Performer p) => Working(p) && !p.owns && p.act == Act.Idle && p.ground != flow.Loading && p.willTake;
        /// <summary>At the source post with empty hands and time for a carry: the next owed bag is theirs.</summary>
        bool Ready(Performer p) => (Working(p) || p.bay && p.visible && flow.Depot) && !p.owns && p.willTake && (p.ground ? flow.Loading : !flow.Loading) &&
            (p.act == Act.Idle || p.act == Act.Release || p.act == Act.Walk && p.goal == Goal.Post);
        /// <summary>A handler about to set a bag on the belt end keeps that end clear (only on the final approach).</summary>
        bool PlacingOnBelt(Performer p) => p.held && (p.ground ? flow.Loading : !flow.Loading) &&
            (p.act == Act.Place || p.act == Act.Walk && p.goal == Goal.Carry && p.index >= p.path.Count-1 &&
             (p.index >= p.path.Count || Vector3.Distance(p.position,p.path[p.path.Count-1].p) < 1.5f));
        /// <summary>Seconds until this performer is working at the post: 0 there, infinity when not coming.</summary>
        float Eta(Performer p)
        {
            if (Working(p)) return 0f;
            if (p.act == Act.Walk && p.goal == Goal.Post) return PathTime(p);
            if (p.act == Act.Unseat || p.act == Act.Rise || p.act == Act.StepDown)
                return (p.rider ? RiderDismount : Dismount)-p.t+(p.ground ? GroundWalkSeconds(p) : HoldEntrySeconds(p));
            return float.PositiveInfinity;
        }
        int Needed(bool ground) => ground ? Crew.GroundWorkers : Crew.HoldWorkers;

        // ================================================================== seats

        void Seat(Performer p)
        {
            if (p.rider) { p.seat = p.ground ? RiderSeat() : LoaderRiderSeat(); return; }
            p.seat = p.ground ? BaggageCrew.Workstation(fleet.Equipment.Tractor,fleet.Train.Tractor) : BaggageCrew.Workstation(fleet.Equipment.Loader,fleet.LoaderPose);
        }
        /// <summary>Sitting sideways on the tractor's rear deck on the working side, legs over the edge.</summary>
        RampCrewPose RiderSeat()
        {
            var tractor = fleet.Train.Tractor; float side = Job?.Plan != null ? WorkSide(fleet.ActiveCart) : 1f;
            var hip = RiderHip; hip.x *= side;
            var origin = TractorPoint(hip-Vector3.up*.94f);
            var pose = RampCrewPose.Standing(origin,side*Vector3.Cross(Vector3.up,new Vector3(tractor.facing.x,0,tractor.facing.y)));
            pose.seated = true;
            // Feet hang below the knees, clear of the ground.
            pose.leftFoot = new Vector3(-.12f,.47f,.42f); pose.rightFoot = new Vector3(.12f,.47f,.42f);
            return pose;
        }
        Vector3 TractorPoint(Vector3 local) { var at = fleet.Train.Tractor.Point(local); return new Vector3(at.x,local.y,at.y); }
        /// <summary>Sitting sideways on the loader's power-unit cover, facing out on the operator's side.</summary>
        RampCrewPose LoaderRiderSeat()
        {
            var loader = fleet.LoaderPose;
            var pose = RampCrewPose.Standing(LoaderPoint(LoaderRiderHip-Vector3.up*.94f),-Vector3.Cross(Vector3.up,new Vector3(loader.facing.x,0,loader.facing.y)));
            pose.seated = true; pose.leftFoot = new Vector3(-.12f,.47f,.42f); pose.rightFoot = new Vector3(.12f,.47f,.42f);
            return pose;
        }
        Vector3 LoaderPoint(Vector3 local) { var at = fleet.LoaderPose.Point(local); return new Vector3(at.x,local.y,at.y); }

        /// <summary>Exit points of the vehicle. The tractor's are those of the simulation's boarding route, on the side the
        /// carts are worked from (the aircraft belt, or the depot conveyor).</summary>
        void Exits(Performer p)
        {
            if (Job?.Plan == null) return;
            if (!p.ground)
            {
                Crew.HoldBoarding(route);
                var step = route[1]; p.stepPoint = step.position; p.stepYaw = Yaw(step.rotation*Vector3.forward);
                var out_ = step.rotation*Vector3.forward; p.groundPoint = Flat(route[2].position)+Flat(out_).normalized*.30f;
                if (p.rider)
                {
                    var outwardL = -Vector3.Cross(Vector3.up,new Vector3(fleet.LoaderPose.facing.x,0,fleet.LoaderPose.facing.y));
                    p.stepYaw = Yaw(outwardL);
                    p.stepPoint = LoaderPoint(new Vector3(-1.25f,0,LoaderRiderHip.z)); p.groundPoint = LoaderPoint(new Vector3(-1.5f,0,LoaderRiderHip.z));
                }
                return;
            }
            var tractor = fleet.Train.Tractor; var type = fleet.Equipment.Tractor; float side = WorkSide(fleet.ActiveCart);
            var outward = side*Vector3.Cross(Vector3.up,new Vector3(tractor.facing.x,0,tractor.facing.y));
            p.stepYaw = Yaw(outward);
            if (p.rider)
            {
                p.stepPoint = TractorPoint(new Vector3(side*.98f,0,RiderHip.z)); p.groundPoint = TractorPoint(new Vector3(side*1.2f,0,RiderHip.z));
                return;
            }
            var local = type.crew?.position ?? Vector3.zero;
            local.x = side > 0 ? type.bounds.max.x+.30f : type.bounds.min.x-.30f; local.y = .25f;
            p.stepPoint = TractorPoint(local); local.y = 0; p.groundPoint = TractorPoint(local)+outward*.30f;
        }
        /// <summary>Outside the front of the first cart on the working side: walks to and from the tractor go round it, not
        /// through the drawbar (from a cart further back the way runs along the train's side, never back round the active cart).</summary>
        Vector3 Corner() => CartSideStand(0,fleet.Equipment.Cart.bounds.max.z+.55f);

        // Cart posts, relative to the belt foot of the session: the aircraft belt, or the depot conveyor's near end.
        /// <summary>+1 when the belt foot is on the cart's right, -1 on its left: the side handlers work from.</summary>
        float WorkSide(int cart)
        {
            var pose = fleet.Train.CartAt(cart);
            return Vector2.Dot(BaggageServicePlan.XZ(flow.BeltFoot)-pose.position,pose.Right) >= 0 ? 1f : -1f;
        }
        Vector3 CartSideStand(int cart, float localZ, float outset = .30f)
        {
            var pose = fleet.Train.CartAt(cart); var type = fleet.Equipment.Cart;
            var local = new Vector3(WorkSide(cart) > 0 ? type.bounds.max.x+outset : type.bounds.min.x-outset,0,localZ);
            var at = pose.Point(local); return new Vector3(at.x,0,at.y);
        }
        float CartReachDepth(int cart)
        {
            var pose = fleet.Train.CartAt(cart); var c = fleet.Equipment.Cart.baggage;
            return Mathf.Clamp(Vector2.Dot(BaggageServicePlan.XZ(flow.BeltFoot)-pose.position,pose.facing),c.loadMin.z+.40f,c.loadMax.z-.40f);
        }
        Vector3 CartStandAt(int cart) => CartSideStand(cart,CartReachDepth(cart));
        // Two cart handlers share the work side by side: each keeps to their own half of the cart and their own lane to
        // the belt, waits at the end of the lane while the other uses the belt foot, and steps in when it is free.
        bool Paired(Performer p) => p.ground && !p.bay && Needed(true) >= 2;
        float Lane(Performer p) => p.worker == 0 ? -1f : 1f;
        /// <summary>A handler's post beside cart <paramref name="cart"/>: level with the belt foot, or the middle of their half.</summary>
        Vector3 Post(Performer p, int cart)
        {
            if (!Paired(p)) return CartStandAt(cart);
            var c = fleet.Equipment.Cart.baggage; float lo = c.loadMin.z+.40f, hi = c.loadMax.z-.40f, mid = (lo+hi)*.5f;
            return CartSideStand(cart,Lane(p) < 0 ? (lo+mid-.45f)*.5f : (hi+mid+.45f)*.5f);
        }
        /// <summary>Where a handler stands at the belt foot (shared by a pair, one at a time).</summary>
        Vector3 Foot(int cart) => BeltStandFrom(CartStandAt(cart));
        /// <summary>The end of a paired handler's lane: beside and behind the belt-foot stand, on their own side.</summary>
        Vector3 LaneEnd(Performer p, int cart)
        {
            var foot = Foot(cart); var u = Flat(flow.BeltFoot-CartStandAt(cart)).normalized;
            var f = fleet.Train.CartAt(cart).facing; var v = Vector3.ProjectOnPlane(new Vector3(f.x,0,f.y),u).normalized;
            // 0.9 m from the foot stand (more than the 0.8 m two people keep), 1.3 m from the other lane's end.
            return foot+v*(.65f*Lane(p))-u*.62f;
        }
        /// <summary>The belt foot is in use by the other handler of a pair (there, or past their lane end on the way).</summary>
        bool FootTaken(Performer p)
        {
            if (!Paired(p)) return false;
            var q = grounds[1-p.worker]; if (!q.visible || q.act == Act.Seated || q.act == Act.Grip) return false;
            // Standing at the foot (placing, taking a bag, or about to step back out).
            if (Vector3.Distance(Flat(q.position),Foot(fleet.ActiveCart)) < .6f) return true;
            // On the way in: committed once off the lane end; two at their lane ends at once, the senior goes first.
            if (!OnFootSegment(q) || q.gateWait) return false;
            return Started(q) || Rank(q) < Rank(p);
        }
        static bool OnFootSegment(Performer q) => q.act == Act.Walk && q.index < q.path.Count && q.path[q.index].gate;
        /// <summary>Has moved off the start of the current segment (the gate is decided only there).</summary>
        static bool Started(Performer q) => Vector3.Distance(Flat(q.position),Flat(q.segmentStart)) > .05f;
        /// <summary>Path to the belt foot for a pair: by the lane end, then (gated) to the foot.</summary>
        void ToFoot(Performer p, int cart, float speed)
        {
            if (!Paired(p)) { Add(p,Foot(cart),speed); return; }
            var end = LaneEnd(p,cart);
            if (Vector3.Distance(Flat(p.position),end) > .15f) Add(p,end,speed);
            p.path.Add(new Waypoint { p = Foot(cart), speed = speed, hip = .94f, gate = true });
        }
        /// <summary>From the belt foot, a paired handler first steps back out to their lane end.</summary>
        void FromFoot(Performer p, int cart, float speed)
        {
            if (Paired(p) && Vector3.Distance(Flat(p.position),Foot(cart)) < .6f) Add(p,LaneEnd(p,cart),speed);
        }
        Vector3 BeltStandFrom(Vector3 cartStand) => Flat(flow.BeltFoot)+Flat(cartStand-flow.BeltFoot).normalized*.64f;

        // ================================================================== walking

        void Plan(Performer p, Act after, Goal goal, bool hasFinal, float finalYaw)
        {
            p.index = 0; p.after = after; p.goal = goal; p.hasFinal = hasFinal; p.finalYaw = finalYaw;
            p.segmentStart = p.position; p.startHip = p.hip; p.startLean = p.lean; p.startKneel = p.kneel;
            p.act = Act.Walk; p.t = 0;
        }
        void Add(Performer p, Vector3 at, float speed, float hip = .94f, float lean = 0f, float kneel = 0f) =>
            p.path.Add(new Waypoint { p = at, speed = speed, hip = hip, lean = lean, kneel = kneel });

        /// <summary>One walking step along the planned path: accelerate, brake for the end, turn toward the way ahead and
        /// slow down for sharp turns so the feet can pivot; postures blend along each segment.</summary>
        bool WalkStep(Performer p, float h)
        {
            if (p.index >= p.path.Count)
            {
                p.speed = 0;
                if (!p.hasFinal) return true;
                p.yaw = Mathf.MoveTowardsAngle(p.yaw,p.finalYaw,TurnRate*.7f*h);
                return Mathf.Abs(Mathf.DeltaAngle(p.yaw,p.finalYaw)) < .5f;
            }
            var target = p.path[p.index];
            float remaining = Vector3.Distance(p.position,target.p);
            for (int i = p.index+1; i < p.path.Count; i++) remaining += Vector3.Distance(p.path[i-1].p,p.path[i].p);
            var flat = Flat(target.p-p.position);
            // Steer toward a point a little way along the path so corners are rounded.
            if (flat.magnitude < .35f && p.index+1 < p.path.Count) flat = Flat(p.path[p.index+1].p-p.position);
            float desired = flat.sqrMagnitude > 1e-4f ? Yaw(flat) : p.yaw;
            float error = Mathf.Abs(Mathf.DeltaAngle(p.yaw,desired));
            // The gate is decided at the lane end only: once on the way in, the handler finishes the move.
            if (target.gate && !Started(p) && FootTaken(p))
            {
                // Wait at the lane end, facing the belt, until the other handler has stepped away from the foot.
                p.gateWait = true; p.speed = Mathf.MoveTowards(p.speed,0f,Decel*2f*h);
                if (flat.sqrMagnitude > 1e-4f) p.yaw = Mathf.MoveTowardsAngle(p.yaw,Yaw(flat),TurnRate*.5f*h);
                p.speed = 0f; return false;
            }
            p.gateWait = false;
            float top = p.hurry ? Mathf.Min(Jog,target.speed*1.7f) : target.speed;
            float want = Mathf.Min(top,Mathf.Sqrt(2f*Decel*remaining));
            // A colleague near and ahead: ease off, so the step clipping below is a slowing, not a wall.
            float near = Nearest(p,flat);
            if (near < float.PositiveInfinity) want = Mathf.Min(want,.25f+Mathf.Sqrt(2f*Decel*Mathf.Max(0f,near-Separation(p))));
            want *= Mathf.Clamp01((Mathf.Cos(error*Mathf.Deg2Rad)-.35f)/.65f);
            p.speed = Mathf.MoveTowards(p.speed,want,Accel*(p.hurry ? 2f : 1f)*h);
            p.yaw = Mathf.MoveTowardsAngle(p.yaw,desired,(p.held ? CarryTurnRate : TurnRate)*(p.hurry ? 1.5f : 1f)*h);
            float move = p.speed*h;
            if (remaining < .02f) move = remaining;
            // Never closer than an arm's length to a colleague: the part of the step toward them is taken off (so the walker
            // slides round them), and one who stays stuck steps round a working colleague, or (the junior of two walkers)
            // aside to let the other by.
            var dir = Flat(p.path[p.index].p-p.position); dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero;
            // Blocked is judged on a nominal step, so a walker slowed to a crawl still knows it is stuck.
            float nominal = Mathf.Max(move,.3f*h);
            var probe = dir == Vector3.zero ? Vector3.zero : Avoid(p,dir*nominal);
            bool blocked = p.blockedBy != null && Vector3.Dot(probe,dir) < .3f*nominal;
            if (blocked)
            {
                p.yielding += h;
                var q = p.blockedBy; var step = Vector3.zero;
                if (p.yielding > .3f)
                {
                    var n = Flat(q.position-p.position).normalized; float pace = Mathf.Max(move,.5f*h);
                    if (q.act == Act.Walk && Rank(p) > Rank(q) && !q.gateWait && (q.speed > .05f || q.yielding > 0f))
                    {
                        // A senior coming through: the junior steps off their line (to the side they are already on), not
                        // backwards along it, and lets them by.
                        var way = q.index < q.path.Count ? Flat(q.path[q.index].p-q.position) : YawRotation(q.yaw)*Vector3.forward;
                        way = way.sqrMagnitude > 1e-6f ? way.normalized : -n;
                        var off = Vector3.Cross(Vector3.up,way); if (Vector3.Dot(-n,off) < 0f) off = -off;
                        step = Avoid(p,(off*.85f-way*.15f).normalized*pace);
                    }
                    else
                    {
                        // Round the colleague: perpendicular to the line between the two (keeps the distance), on the side of the way.
                        var side = Vector3.Cross(Vector3.up,n);
                        if (p.sideSign == 0f) p.sideSign = Vector3.Dot(side,dir) >= 0f ? 1f : -1f;
                        step = Avoid(p,side*p.sideSign*pace);
                    }
                    p.blockedBy = q;
                }
                p.position += step; p.speed = Mathf.Min(p.speed,step.magnitude/Mathf.Max(h,1e-5f)); move = 0f;
            }
            else
            {
                // Forgotten slowly: a walker that only creeps between stops still counts as stuck.
                p.yielding = Mathf.Max(0f,p.yielding-.5f*h); if (p.yielding <= 0f) p.sideSign = 0f;
                var step = dir == Vector3.zero ? Vector3.zero : Avoid(p,dir*move);
                if (Vector3.Distance(step,dir*move) > 1e-4f) { p.position += step; move = 0f; } // sliding round someone
            }
            while (move > 0f && p.index < p.path.Count)
            {
                var w = p.path[p.index]; float d = Vector3.Distance(p.position,w.p);
                if (d <= move) { p.position = w.p; move -= d; NextSegment(p); if (p.index < p.path.Count && p.path[p.index].gate) move = 0f; continue; }
                p.position = Vector3.MoveTowards(p.position,w.p,move); move = 0;
            }
            if (p.index < p.path.Count)
            {
                var w = p.path[p.index];
                float total = Vector3.Distance(p.segmentStart,w.p), u = total > 1e-3f ? 1f-Vector3.Distance(p.position,w.p)/total : 1f;
                p.hip = Mathf.Lerp(p.startHip,w.hip,u); p.lean = Mathf.Lerp(p.startLean,w.lean,u); p.kneel = Mathf.Lerp(p.startKneel,w.kneel,u);
            }
            return false;
        }
        /// <summary>Distance to the nearest colleague ahead within a body's width of <paramref name="heading"/>, or infinity.</summary>
        float Nearest(Performer p, Vector3 heading)
        {
            if (heading.sqrMagnitude < 1e-6f) return float.PositiveInfinity;
            heading.Normalize(); float near = float.PositiveInfinity;
            for (int i = 0; i < 5; i++)
            {
                var q = Other(i); if (!Near(p,q)) continue;
                var d = Flat(q.position-p.position); float along = Vector3.Dot(d,heading);
                if (along > 0f && Vector3.Cross(heading,d).magnitude < Separation(p)) near = Mathf.Min(near,along);
            }
            return near;
        }
        Performer Other(int i) => i < 2 ? grounds[i] : i < 4 ? holds[i-2] : bay;
        /// <summary>A colleague on foot on the same level (not up on the belt walkway or in the hold when the other is below).</summary>
        static bool Near(Performer p, Performer q) => q != p && q.visible && q.act != Act.Seated && q.act != Act.Grip &&
            Mathf.Abs(q.position.y-p.position.y) < 1.2f;
        /// <summary>How close two may come, hip to hip: an arm's length on the ramp, a little less kneeling in the hold
        /// (where the posts are 0.7 m apart).</summary>
        static float Separation(Performer p) => p.kneel > .5f ? .68f : Clear;
        /// <summary>The step with whatever would bring the walker closer than <see cref="Separation"/> to a colleague taken off.</summary>
        Vector3 Avoid(Performer p, Vector3 step)
        {
            p.blockedBy = null;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < 5; i++)
                {
                    var q = Other(i); if (!Near(p,q)) continue;
                    // Right of way: a walker keeps the full distance from colleagues at work and from seniors on the move
                    // (ground before hold, first before second); a walking junior is the one who keeps clear and backs off,
                    // so the senior only stops short of touching them.
                    // Following a senior on the way out or in, a junior keeps a step further back (a stooping or kneeling body
                    // reaches further than its feet).
                    float clear = q.act == Act.Walk && Rank(q) > Rank(p) ? Touch :
                        q.act == Act.Walk && Rank(q) < Rank(p) && q.goal == p.goal && q.speed > .05f && !q.gateWait ? Follow : Separation(p);
                    var d = Flat(q.position-p.position); float dist = d.magnitude;
                    if (dist > clear+step.magnitude+.05f || dist < 1e-4f) continue;
                    var n = d/dist; float toward = Vector3.Dot(step,n), allowed = Mathf.Max(0f,dist-clear);
                    if (toward <= allowed) continue;
                    step -= n*(toward-allowed); p.blockedBy = q;
                }
            return step;
        }
        const float Patience = 2.5f;
        /// <summary>The closest a walker with right of way comes to a junior colleague who is getting out of the way.</summary>
        const float Touch = .72f;
        /// <summary>How far a junior stays behind a senior walking the same way.</summary>
        const float Follow = .9f;
        static int Rank(Performer p) => (p.ground ? 0 : 4)+(p.bay ? 2 : p.worker);
        void NextSegment(Performer p)
        {
            var w = p.path[p.index]; p.hip = w.hip; p.lean = w.lean; p.kneel = w.kneel;
            p.index++; p.segmentStart = p.position; p.startHip = p.hip; p.startLean = p.lean; p.startKneel = p.kneel;
        }
        /// <summary>Seconds to walk the rest of the planned path at each segment's speed.</summary>
        float PathTime(Performer p)
        {
            float t = 0; var at = p.position;
            for (int i = p.index; i < p.path.Count; i++) { var w = p.path[i]; t += Vector3.Distance(at,w.p)/w.speed; at = w.p; }
            return t+.6f;
        }
        /// <summary>Seconds to walk <see cref="route"/> from index <paramref name="from"/> to <paramref name="to"/> (either direction).</summary>
        float RouteTime(int from, int to)
        {
            float t = 0; int step = to >= from ? 1 : -1;
            for (int i = from; i != to; i += step)
            {
                int j = i+step, hi = Mathf.Max(i,j);
                HoldWay(hi,out float speed,out _,out _,out _);
                t += Vector3.Distance(route[i].position,route[j].position)/speed;
            }
            return t;
        }

        // ================================================================== ground handler

        /// <summary>Seconds from the foot of the tractor step, round the cart, to the post.</summary>
        float GroundWalkSeconds(Performer p)
        {
            if (Job?.Plan == null) return 0f;
            Exits(p); var corner = Corner(); var post = CartStandAt(fleet.ActiveCart);
            return (Vector3.Distance(Flat(p.groundPoint),corner)+Vector3.Distance(corner,post))/Walk+Walk/Accel;
        }
        /// <summary>Seconds a ground handler needs from where they are to sitting at the wheel.</summary>
        float GroundReturnSeconds(Performer p) => GroundReturnFrom(p,p.position);
        float GroundReturnFrom(Performer g, Vector3 at)
        {
            if (Job?.Plan == null) return 0f;
            // The bay hand only has to be back by the conveyor ends, clear of the train, when it leaves.
            if (g.bay) return Vector3.Distance(Flat(at),g.home)/Walk+Walk/Accel+Margin;
            Exits(g);
            // The way GoBack walks: round the corner outside the cart and drawbar, then to the step.
            var corner = Corner();
            float d = Vector3.Distance(Flat(at),corner) > .6f ? Vector3.Distance(Flat(at),corner)+Vector3.Distance(corner,Flat(g.groundPoint)) :
                Vector3.Distance(Flat(at),Flat(g.groundPoint));
            // A pair walks back in single file and may wait for each other: a little more time.
            return d*1.1f/Walk+Walk/Accel+1.5f+(g.rider ? RiderUp+RiderSit+RiderLead : StepTime+SitTime+GripTime*.5f)+Margin+(Paired(g) ? Patience : 0f);
        }
        /// <summary>Seconds of a whole carry from the moment it is chosen: reach, lift, walk, place, release.</summary>
        float CarrySeconds(Performer p, float distance) => ReachTime+LiftTime+.6f+distance/CarryWalk+PlaceTime+ReleaseTime;

        void StepGround(Performer p, float h)
        {
            var job = Job;
            if (p.act == Act.External) Return(p);
            if (p.rider && (job == null || p.worker >= Needed(true)))
            {
                // Not hired for this team (or no job): nobody rides.
                if (p.held) Drop(p); else if (p.owns) Unreserve(p);
                if (p.visible || p.act != Act.Seated) { p.path.Clear(); Return(p); }
                p.visible = false; return;
            }
            p.visible = true;
            bool moving = Mathf.Abs(fleet.TrainSpeed) > 0f;
            // The simulation boards the driver; the rider gets off with them (and, being quicker off the deck and nearer the
            // carts, leads the way). At a depot the team stays aboard.
            bool simOut = !Crew.GroundSeated;
            if (p.rider)
            {
                // With the driver (who is still getting up), or once the driver is well clear of the deck's foot.
                var d = grounds[0]; simOut = false;
                if (d.act == Act.Unseat || d.act == Act.Rise) simOut = true;
                else if (d.act != Act.Seated && d.act != Act.Grip && d.goal != Goal.Return && job?.Plan != null)
                { Exits(p); simOut = Vector3.Distance(Flat(d.position),p.groundPoint) > 2.5f; }
            }
            bool handling = job != null && (job.State == BaggageTurnaroundState.UnloadingHold || job.State == BaggageTurnaroundState.LoadingHold);
            if (p.act == Act.Seated || p.act == Act.Grip)
            {
                Seat(p); p.position = p.seat.position; p.yaw = Yaw(p.seat.rotation*Vector3.forward);
                if (p.act == Act.Grip) { p.t += h; p.reach = 1f-Ease(1f-p.t/GripTime); if (p.t >= GripTime) { p.act = Act.Seated; p.reach = 1; } }
                // Get off only when there is time to get there, do some work and get back before the simulation needs the
                // driver again (after a recall the simulation may still count the driver as out for a few seconds).
                if (simOut && handling && !moving && job.Plan != null &&
                    flow.RemainingHandling() > (p.rider ? RiderDismount : Dismount)+GroundWalkSeconds(p)+GroundReturnFrom(p,CartStandAt(fleet.ActiveCart))+MinWork)
                { Exits(p); p.act = Act.Unseat; p.t = 0; }
                return;
            }
            // Anything but the seat while the tractor moves: last resort.
            if (moving) { Placed(p); return; }
            float remaining = flow.RemainingHandling();
            bool recalled = !handling;
            switch (p.act)
            {
                case Act.Unseat:
                {
                    float unseat = p.rider ? RiderUnseat : UnseatTime;
                    p.t += h; if (!p.rider) p.reach = 1f-Ease(p.t/(unseat*.8f));
                    if (!p.rider) p.yaw = Mathf.LerpAngle(Yaw(p.seat.rotation*Vector3.forward),p.stepYaw,Ease(p.t/unseat)*.45f);
                    if (p.t >= unseat) { p.act = Act.Rise; p.t = 0; p.leftFootFrom = p.seat.leftFoot; p.rightFootFrom = p.seat.rightFoot; }
                    break;
                }
                case Act.Rise:
                {
                    float rise = p.rider ? RiderRise : RiseTime;
                    p.t += h; Rise(p,Ease(p.t/rise));
                    if (p.t >= rise) { p.act = Act.StepDown; p.t = 0; }
                    break;
                }
                case Act.StepDown:
                {
                    float step = p.rider ? RiderStep : StepTime;
                    p.t += h; StepDown(p,Ease(p.t/step));
                    if (p.t >= step)
                    {
                        // Round the drawbar and the cart, not through them.
                        p.path.Clear(); var corner = Corner();
                        if (Vector3.Distance(Flat(p.position),corner) > .8f) Add(p,corner,Walk);
                        if (flow.CartSource) Add(p,Post(p,fleet.ActiveCart),Walk); else ToFoot(p,fleet.ActiveCart,Walk);
                        Plan(p,Act.Idle,Goal.Post,false,0);
                    }
                    break;
                }
                case Act.StepUp:
                {
                    float up = p.rider ? RiderUp : StepTime;
                    p.t += h; StepUp(p,Ease(p.t/up));
                    if (p.t >= up) { p.act = Act.SitDown; p.t = 0; Seat(p); }
                    break;
                }
                case Act.SitDown:
                {
                    float sit = p.rider ? RiderSit : SitTime;
                    p.t += h; Seat(p); SitDown(p,Ease(p.t/sit));
                    if (p.t >= sit) { p.act = p.rider ? Act.Seated : Act.Grip; p.t = 0; p.atPost = false; p.hurry = false; }
                    break;
                }
                case Act.Walk:
                    if (p.goal != Goal.Return && (recalled || remaining <= GroundReturnSeconds(p) && !p.held)) { GoBack(p,recalled); break; }
                    // Held up on the way back (waiting for a colleague) and now short of time: hurry.
                    if (p.goal == Goal.Return && !p.hurry && remaining < GroundReturnSeconds(p)-Margin) p.hurry = true;
                    if (WalkStep(p,h)) { p.act = p.after; p.t = 0; if (p.goal == Goal.Post || p.goal == Goal.Carry) p.atPost = true; }
                    if (p.held) Carry(p,1f,false);
                    break;
                default:
                    GroundWork(p,h,remaining,recalled);
                    break;
            }
        }

        /// <summary>Seat to step: hands leave the wheel, the body turns out, rises and stands on the step.</summary>
        void Rise(Performer p, float u)
        {
            var seat = p.seat.position; var step = p.stepPoint;
            if (p.rider)
            {
                // Slide forward off the deck and land on both feet.
                p.position = Vector3.Lerp(seat,step,u); p.yaw = Yaw(p.seat.rotation*Vector3.forward);
                p.sit = 1f-u; p.feet = 1f-u; p.hip = Mathf.Lerp(.94f,.90f,u); p.lean = Mathf.Sin(u*Mathf.PI)*12f;
                return;
            }
            p.position = Vector3.Lerp(seat,step,u)+Vector3.up*Mathf.Sin(u*Mathf.PI)*.05f;
            p.yaw = Mathf.LerpAngle(Mathf.LerpAngle(Yaw(p.seat.rotation*Vector3.forward),p.stepYaw,.45f),p.stepYaw,u);
            p.sit = 1f-u; p.feet = 1f; p.hip = Mathf.Lerp(.94f,.86f,u); p.lean = Mathf.Sin(u*Mathf.PI)*14f;
        }
        void StepDown(Performer p, float u)
        {
            p.sit = 0; p.feet = Mathf.Clamp01(1f-u*3f);
            p.position = Vector3.Lerp(p.stepPoint,p.groundPoint,u);
            p.yaw = p.stepYaw; p.hip = Mathf.Lerp(.86f,.94f,u); p.lean = 0;
        }

        /// <summary>Ground to step facing the vehicle; the feet step up by themselves.</summary>
        void StepUp(Performer p, float u)
        {
            if (p.rider)
            {
                // Turn the back to the deck before sitting up onto it.
                float seatYaw = Yaw(p.seat.rotation*Vector3.forward);
                p.sit = 0; p.feet = 0; p.lean = 0; p.hip = .94f; p.position = Vector3.Lerp(p.groundPoint,p.stepPoint,u);
                p.yaw = Mathf.MoveTowardsAngle(p.yaw,seatYaw,TurnRate*.02f+Mathf.Abs(Mathf.DeltaAngle(p.yaw,seatYaw))*u);
                return;
            }
            p.sit = 0; p.feet = 0; p.lean = Mathf.Sin(u*Mathf.PI)*10f;
            p.position = Vector3.Lerp(p.groundPoint,p.stepPoint,u);
            p.hip = .94f-Mathf.Sin(u*Mathf.PI)*.08f;
        }
        /// <summary>Step to seat: turn from facing the vehicle to facing forward while sitting down; feet go to the pedals.</summary>
        void SitDown(Performer p, float u)
        {
            if (p.rider)
            {
                p.position = Vector3.Lerp(p.stepPoint,p.seat.position,u); p.yaw = Yaw(p.seat.rotation*Vector3.forward);
                p.sit = u; p.feet = u; p.hip = Mathf.Lerp(.90f,.94f,u); p.lean = Mathf.Sin(u*Mathf.PI)*10f;
                return;
            }
            float seatYaw = Yaw(p.seat.rotation*Vector3.forward), inYaw = p.stepYaw+180f;
            p.position = Vector3.Lerp(p.stepPoint,p.seat.position,u)+Vector3.up*Mathf.Sin(u*Mathf.PI)*.05f;
            p.yaw = Mathf.LerpAngle(inYaw,seatYaw,Ease(u*1.3f));
            p.sit = u; p.feet = Mathf.Clamp01(u*3f); p.hip = Mathf.Lerp(.86f,.94f,u); p.lean = Mathf.Sin(u*Mathf.PI)*14f;
        }

        void GoBack(Performer p, bool hurry)
        {
            if (p.bay)
            {
                if (p.owns && !p.held) Unreserve(p);
                if (p.held) Drop(p);
                GoHome(p); return;
            }
            if (p.owns && !p.held) Unreserve(p);
            if (p.held) { Drop(p); }
            p.path.Clear(); p.hurry = hurry;
            Exits(p);
            if (p.ground)
            {
                // Outside the cart and drawbar, then to the tractor's step.
                var corner = Corner();
                if (Vector3.Distance(Flat(p.position),corner) > .6f) Add(p,corner,Walk);
                Add(p,p.groundPoint,Walk);
                Plan(p,Act.StepUp,Goal.Return,true,Mathf.DeltaAngle(0,p.stepYaw+180f));
            }
            else HoldExitPath(p);
            p.atPost = false;
        }

        void GroundWork(Performer p, float h, float remaining, bool recalled)
        {
            var job = Job; bool loading = flow.Loading || p.late;
            if (recalled && !p.held) { GoBack(p,true); return; }
            switch (p.act)
            {
                case Act.Idle:
                {
                    p.t += h; Settle(p,h);
                    if ((p.bay ? p.stay : remaining) <= GroundReturnSeconds(p)) { if (!p.bay || !AtHome(p)) GoBack(p,false); return; }
                    if (!GroundSession || fleet.Station.accessBlocked) { ToPost(p,flow.CartSource,!flow.CartSource,h); return; }
                    if (loading)
                    {
                        int cart = flow.SourceCart(); if (cart < 0) cart = fleet.ActiveCart;
                        var stand = Post(p,cart); var belt = Foot(cart);
                        float carry = CarrySeconds(p,Vector3.Distance(stand,belt));
                        // Only a bag that will be on the belt in time and leave time to walk back to the tractor.
                        // (Into a depot hall the ride may run on after the state; the bay hand only has to put the bag down.)
                        p.willTake = p.bay ? remaining > carry && p.stay > carry+GroundReturnFrom(p,belt) :
                            remaining > GroundReturnFrom(p,belt)+carry+flow.RideSeconds*.5f;
                        if (!p.willTake) { GoBack(p,false); return; }
                        if (p.willTake && flow.CartOwed(cart) > 0 &&
                            flow.ReserveCart(cart,stand,CartReach+.33f,out p.slot,out p.variant))
                        {
                            p.owns = true; p.held = false; p.cart = cart; p.draw = false;
                            p.bagBottom = flow.CartSlot(cart,p.slot); p.bagRotation = flow.CartRotation(cart,p.variant); p.size = Size(p.variant);
                            // Stand level with the bag on the cart's working side.
                            var at = CartSideStand(cart,LocalZ(cart,p.bagBottom));
                            Face(p,at,p.bagBottom,Act.Reach,Goal.Carry,Walk);
                            return;
                        }
                        ToPost(p,true,false,h);
                    }
                    else
                    {
                        if (!ToPost(p,false,true,h)) return;
                        if (Paired(p) && Vector3.Distance(Flat(p.position),Foot(fleet.ActiveCart)) > .3f) return; // waiting at the lane end
                        int cart = flow.DestinationCart(); var stand = CartStandAt(Mathf.Max(0,cart));
                        float carry = CarrySeconds(p,Vector3.Distance(stand,BeltStandFrom(stand)));
                        // The carry ends at the cart, so the walk back starts there.
                        p.willTake = remaining > GroundReturnFrom(p,stand)+carry;
                        if (!p.willTake && flow.Session && !flow.Loading) { GoBack(p,false); return; }
                        if (p.willTake &&
                            flow.Waiting(false,out p.bagBottom,out p.bagRotation,out p.variant,out p.cart))
                        {
                            flow.TakeWaiting(false);
                            p.owns = true; p.held = false; p.draw = true; p.size = Size(p.variant);
                            if (p.cart < 0) p.cart = Mathf.Max(0,cart);
                            p.act = Act.Reach; p.t = 0; Pick(p);
                        }
                    }
                    break;
                }
                case Act.Reach:
                    p.t += h; Posture(p,p.pickHip,p.pickLean,Ease(p.t/ReachTime)); p.reach = Ease(p.t/ReachTime);
                    if (p.t >= ReachTime)
                    {
                        p.act = Act.Lift; p.t = 0; p.held = true; p.draw = true; p.load = .6f;
                        if (loading) flow.LiftCart(p.cart,p.slot);
                        p.bagFrom = p.bagBottom; p.rotFrom = p.bagRotation;
                    }
                    break;
                case Act.Lift:
                {
                    p.t += h; float u = Ease(Mathf.Clamp01((p.t-.12f)/(LiftTime-.12f)));
                    // Weight goes onto the legs first (a small dip), then the body rises with the bag.
                    Posture(p,p.pickHip-.03f*Mathf.Sin(Mathf.Clamp01(p.t/.25f)*Mathf.PI),p.pickLean,1f);
                    if (p.t > .12f) Posture(p,Mathf.Lerp(p.pickHip,.94f,u),Mathf.Lerp(p.pickLean,4f,u),1f);
                    Carry(p,u,true);
                    if (p.t >= LiftTime)
                    {
                        p.act = Act.Walk; p.t = 0; p.path.Clear();
                        Vector3 to; float face;
                        if (loading && Paired(p))
                        {
                            ToFoot(p,p.cart,CarryWalk); to = Foot(p.cart); face = Yaw(Flat(flow.BeltFoot-to));
                            Plan(p,Act.Place,Goal.Carry,true,face); break;
                        }
                        if (loading) { var belt = BeltStandFrom(CartSideStand(p.cart,LocalZ(p.cart,p.bagFrom))); to = belt; face = Yaw(Flat(flow.BeltFoot-belt)); }
                        else
                        {
                            p.slot = flow.ReserveSlot(p.cart,Post(p,p.cart),CartReach);
                            var target = p.slot >= 0 ? flow.CartSlot(p.cart,p.slot) : CartStandAt(p.cart);
                            to = CartSideStand(p.cart,LocalZ(p.cart,target)); face = Yaw(Flat(target-to));
                            p.bagTo = target; p.rotTo = flow.CartRotation(p.cart,p.variant);
                            FromFoot(p,p.cart,CarryWalk);
                        }
                        Add(p,to,CarryWalk); Plan(p,Act.Place,Goal.Carry,true,face);
                    }
                    break;
                }
                case Act.Place:
                {
                    if (p.t == 0f && loading && !p.late && !flow.FootClear) { Carry(p,1f,false); break; } // wait for the foot of the belt to clear
                    if (p.t == 0f) { if (p.late) { p.bagTo = flow.GhostFoot; p.rotTo = flow.GhostRotation; } else if (loading) { p.bagTo = flow.BeltFoot; p.rotTo = flow.BeltRotation(0f); } p.bagFrom = p.bagBottom; p.rotFrom = p.bagRotation; PickPosture(p,p.bagTo); }
                    p.t += h; float u = Ease(p.t/PlaceTime);
                    Posture(p,Mathf.Lerp(.94f,p.pickHip,u),Mathf.Lerp(4f,p.pickLean,u),1f);
                    Set(p,u);
                    if (p.t >= PlaceTime)
                    {
                        if (p.late) { flow.PutOnGhost(p.variant); p.late = false; }
                        else if (loading) flow.PutOnBelt(true,p.variant); else flow.PlaceInCart(p.cart,p.slot,p.variant);
                        p.held = false; p.owns = false; p.load = 0; p.act = Act.Release; p.t = 0;
                    }
                    break;
                }
                case Act.Release:
                    p.t += h; p.reach = 1f-Ease(p.t/ReleaseTime); Posture(p,Mathf.Lerp(p.pickHip,.94f,Ease(p.t/ReleaseTime)),Mathf.Lerp(p.pickLean,0,Ease(p.t/ReleaseTime)),1f);
                    if (p.t >= ReleaseTime)
                    {
                        p.reach = 0; p.act = Act.Idle; p.t = 0;
                        // Clear the belt foot for the other handler of a pair.
                        if (Paired(p) && loading && Vector3.Distance(Flat(p.position),Foot(fleet.ActiveCart)) < .6f)
                        { p.path.Clear(); Add(p,LaneEnd(p,fleet.ActiveCart),Walk); Plan(p,Act.Idle,Goal.Post,false,0); }
                    }
                    break;
            }
        }

        float LocalZ(int cart, Vector3 world)
        {
            var pose = fleet.Train.CartAt(cart); var d = BaggageServicePlan.XZ(world)-pose.position;
            return Vector2.Dot(d,pose.facing);
        }

        /// <summary>Walk to the post for the current direction if not there; true when standing at it.</summary>
        bool ToPost(Performer p, bool loading, bool belt, float h)
        {
            int cart = loading ? flow.SourceCart() : flow.DestinationCart();
            if (cart < 0) cart = fleet.ActiveCart;
            var stand = Post(p,cart);
            Vector3 at, look;
            if (belt && Paired(p))
            {
                // Take turns at the belt foot: wait at the end of the own lane while the other handler is there.
                at = FootTaken(p) ? LaneEnd(p,cart) : Foot(cart); look = flow.BeltFoot;
                if (Vector3.Distance(Flat(p.position),Flat(at)) >= .15f)
                {
                    p.path.Clear();
                    if (at == Foot(cart)) ToFoot(p,cart,Walk); else { FromFoot(p,cart,Walk); Add(p,at,Walk); }
                    Plan(p,Act.Idle,Goal.Post,true,Yaw(Flat(look-at))); return false;
                }
            }
            else if (belt) { at = BeltStandFrom(stand); look = flow.Job?.Plan != null ? flow.BeltFoot : at+Vector3.forward; }
            else { at = stand; var c = fleet.Train.CartAt(cart); var centre = c.Point(new Vector3(0,0,LocalZ(cart,stand))); look = new Vector3(centre.x,0,centre.y); }
            if (Vector3.Distance(Flat(p.position),Flat(at)) < .15f)
            {
                float face = Yaw(Flat(look-at));
                p.yaw = Mathf.MoveTowardsAngle(p.yaw,face,TurnRate*.6f*h);
                return Mathf.Abs(Mathf.DeltaAngle(p.yaw,face)) < 20f;
            }
            p.path.Clear(); if (!belt) FromFoot(p,cart,Walk); Add(p,at,Walk); Plan(p,Act.Idle,Goal.Post,true,Yaw(Flat(look-at)));
            return false;
        }

        void Face(Performer p, Vector3 at, Vector3 target, Act then, Goal goal, float speed)
        {
            p.path.Clear();
            if (Vector3.Distance(Flat(p.position),Flat(at)) > .08f) Add(p,at,speed);
            Plan(p,then,goal,true,Yaw(Flat(target-at)));
            if (then == Act.Reach) PickPosture(p,target);
        }
        void Pick(Performer p) => PickPosture(p,p.bagBottom);
        /// <summary>Knees bend for low bags; the rig adds the reach lean itself.</summary>
        void PickPosture(Performer p, Vector3 bottom)
        {
            if (!p.ground) { Crew.HoldPosture(out p.pickHip,out p.pickLean); return; }
            float grip = bottom.y+.55f*Size(p.variant).z, u = Mathf.InverseLerp(.70f,1.30f,grip);
            p.pickHip = Mathf.Lerp(.68f,.94f,u); p.pickLean = Mathf.Lerp(24f,6f,u);
        }
        void Posture(Performer p, float hip, float lean, float u) { p.hip = Mathf.Lerp(p.hip,hip,u); p.lean = Mathf.Lerp(p.lean,lean,u); }
        void Settle(Performer p, float h)
        {
            float k = 1f-Mathf.Exp(-h/.25f);
            if (p.ground) { p.hip += (.94f-p.hip)*k; p.lean += (0f-p.lean)*k; }
            p.reach = Mathf.MoveTowards(p.reach,0f,h/ReleaseTime);
        }

        /// <summary>The bag in the hands: from where it was gripped (u = 0) to the carry position (u = 1).</summary>
        void Carry(Performer p, float u, bool lifting)
        {
            var rot = YawRotation(p.yaw);
            ChooseGrip(p,rot);
            var carryRot = rot*(p.across ? Quaternion.identity : Quaternion.Euler(0,90,0));
            float depth = (p.across ? p.size.y : p.size.x)*.5f;
            var offset = p.ground ? new Vector3(0,.66f,.20f+depth) : new Vector3(0,Mathf.Max(.10f,p.hip-.16f),.22f+depth);
            var carry = p.position+rot*offset;
            if (!lifting) { p.bagBottom = carry; p.bagRotation = carryRot; return; }
            p.bagBottom = Vector3.Lerp(p.bagFrom,carry,u)+Vector3.up*Mathf.Sin(u*Mathf.PI)*.10f;
            p.bagRotation = Quaternion.Slerp(p.rotFrom,carryRot,u);
        }
        void Set(Performer p, float u)
        {
            Carry(p,0f,false); var from = p.bagBottom; var fromRot = p.bagRotation;
            p.bagBottom = Vector3.Lerp(from,p.bagTo,u)+Vector3.up*Mathf.Sin(u*Mathf.PI)*.08f;
            p.bagRotation = Quaternion.Slerp(fromRot,p.rotTo,u);
        }
        /// <summary>Grip the pair of faces across the worker: chosen once per bag so the hands never swap sides.</summary>
        void ChooseGrip(Performer p, Quaternion body)
        {
            if (p.held) return;
            var right = body*Vector3.right;
            p.across = Mathf.Abs(Vector3.Dot(p.bagRotation*Vector3.right,right)) >= Mathf.Abs(Vector3.Dot(p.bagRotation*Vector3.forward,right));
        }

        void Unreserve(Performer p)
        {
            if (p.ground && flow.Loading && p.cart >= 0) flow.ReleaseCart(p.cart,p.slot);
            if (!p.ground) flow.ReleaseHold(p.worker,!flow.Loading);
            p.owns = false;
        }
        /// <summary>The performer is recalled with a bag in the hands (cancellation, an unforeseen end): it goes where the
        /// simulation has it, without hands.</summary>
        void Drop(Performer p)
        {
            if (p.ground && !flow.Loading && p.cart >= 0) flow.PlaceInCart(p.cart,p.slot,p.variant);
            else if (p.ground) flow.PutOnBelt(true,p.variant);
            else if (flow.Loading) flow.StowHold(p.worker,p.variant);
            else flow.PutOnBelt(false,p.variant,-1);
            CountDirect(1);
            p.owns = p.held = false; p.load = 0;
        }

        // ================================================================== depot bay hand

        /// <summary>The depot bay's hall handler: claimed from the depot while this train stands in its bay (a depot
        /// handover state), works the carts and the conveyor like a cart handler, and is back by the conveyor ends before the
        /// train leaves, where the depot takes it over again.</summary>
        void StepBay(Performer p, float h)
        {
            var depot = fleet.Station.depot; if (depot == null) return;
            bool here = flow.AtDepot && Mathf.Abs(fleet.TrainSpeed) <= 0f && Job?.Plan != null;
            if (hand == null)
            {
                var mine = depot.BayHand(depot.Layout.LaneFor(fleet.Station.depotBay));
                if (!here || mine.owner != null) { p.visible = false; return; }
                hand = mine; hand.owner = this;
                p.home = hand.Home; p.homeYaw = hand.HomeYaw;
                p.position = hand.placed ? hand.position : p.home; p.yaw = hand.placed ? hand.yaw : p.homeYaw;
                p.act = Act.Idle; p.goal = Goal.Return; p.atPost = false; p.visible = true; p.path.Clear();
                p.hip = .94f; p.lean = p.kneel = p.sit = p.feet = p.reach = p.load = 0f; p.speed = 0; p.t = 0; p.lastValid = false;
                p.owns = p.held = false;
            }
            p.home = hand.Home; p.homeYaw = hand.HomeYaw;
            if (!here)
            {
                // The train has gone (or is going): whatever is still in the hands goes where the simulation has it; home.
                if (p.held && !p.late) Drop(p); else if (p.held) { p.owns = p.held = p.late = false; p.load = 0; CountDirect(1); } else if (p.owns) Unreserve(p);
                if (p.act == Act.Walk) { if (WalkStep(p,h)) { p.act = Act.Idle; p.t = 0; } return; }
                if (!AtHome(p)) { GoHome(p); return; }
                p.reach = 0; hand.owner = null; hand = null; p.visible = false; return;
            }
            // Work only what this state's piles can show (arrivals and departures are two states in one stay); be home by
            // the end of the stay.
            float remaining = flow.StateRemaining; p.stay = flow.RemainingHandling();
            if (p.owns && p.bagSession != flow.SessionId && !p.late)
            {
                // The state changed under a bag (its pile was set to the ledger, which counts the bag in the hall already).
                // A bag in the hands is still carried to the conveyor and rides it into the hall; a reserved one stays put.
                if (p.held && flow.Ghosting) p.late = true;
                else { if (p.held) CountDirect(1); p.owns = p.held = false; p.load = 0; if (p.act == Act.Reach || p.act == Act.Lift) { p.act = Act.Idle; p.t = 0; } }
            }
            if (p.act == Act.Idle && !p.owns)
            {
                // Take a bag only when the whole carry fits this state: from the cart to the conveyor and the ride into the
                // hall, or from the conveyor foot to the cart and back to the conveyor ends. Otherwise wait by the conveyor.
                var post = CartStandAt(Mathf.Max(0,fleet.ActiveCart)); var foot = BeltStandFrom(post);
                float carry = CarrySeconds(p,Vector3.Distance(post,foot));
                // The bag has to be down before the state ends; the walk home only before the train leaves.
                float need = flow.CartSource ? Vector3.Distance(Flat(p.position),post)/Walk+carry :
                    Vector3.Distance(Flat(p.position),foot)/Walk+carry;
                float after = flow.CartSource ? Mathf.Max(flow.RideSeconds+BaggageBagFlow.Settle,GroundReturnFrom(p,foot)) : GroundReturnFrom(p,post);
                if (remaining < need || p.stay < need+after || !GroundSession || fleet.Station.accessBlocked)
                {
                    p.willTake = false;
                    if (!AtHome(p) && Vector3.Distance(Flat(p.position),p.home) > .1f) { GoHome(p); return; }
                    p.t += h; Settle(p,h); p.yaw = Mathf.MoveTowardsAngle(p.yaw,p.homeYaw,TurnRate*.5f*h);
                    return;
                }
                p.willTake = true; // the flow keeps the next owed bag in the pile for these hands
                // Go straight to that bag rather than to the post first.
                if (flow.CartSource && flow.CartOwed(Mathf.Max(0,flow.SourceCart())) <= 0) { p.t += h; Settle(p,h); return; }
            }
            bool had = p.owns;
            if (p.act == Act.Walk)
            {
                if (p.goal != Goal.Return && !p.held && p.stay <= GroundReturnSeconds(p)) { GoBack(p,false); return; }
                if (WalkStep(p,h)) { p.act = p.after; p.t = 0; if (p.goal == Goal.Post || p.goal == Goal.Carry) p.atPost = true; }
                if (p.held) Carry(p,1f,false);
            }
            else GroundWork(p,h,remaining,false);
            if (p.owns && !had) p.bagSession = flow.SessionId;
        }
        bool AtHome(Performer p) => Vector3.Distance(Flat(p.position),p.home) < .08f && Mathf.Abs(Mathf.DeltaAngle(p.yaw,p.homeYaw)) < 3f;
        void GoHome(Performer p)
        {
            p.path.Clear(); if (Vector3.Distance(Flat(p.position),p.home) > .05f) Add(p,p.home,Walk);
            Plan(p,Act.Idle,Goal.Return,true,p.homeYaw); p.atPost = false;
        }

        // ================================================================== hold handler

        float HoldExitSeconds(Performer p)
        {
            if (Job?.Plan == null) return 0f;
            Exits(p);
            float inside = Vector3.Distance(Flat(p.position),Flat(route[route.Count-1].position))/Kneeling;
            // Turns at each corner of the walkway are not in the distances: a second of slack per few metres.
            return (inside+RouteTime(route.Count-1,2))*1.08f+StepTime+SitTime+Margin*2f;
        }
        float HoldExitFromStand()
        {
            holdStandProbe.position = Crew.HoldIdlePose(0).position; return HoldExitSeconds(holdStandProbe);
        }
        float HoldEntrySeconds(Performer p)
        {
            if (Job?.Plan == null) return 0f;
            Exits(p); return RouteTime(2,route.Count-1);
        }
        /// <summary>Seconds until the simulation needs the hold handler seated: the handling left, then (after loading)
        /// the train's drive home, which the loader waits for.</summary>
        /// <summary>The hold deadline for this handler: the second one (who follows the operator in at docking and stays
        /// while the operator does) is out a little before the operator.</summary>
        float Deadline(Performer p) => p.rider ? HoldDeadline()-HoldRiderLead : HoldDeadline();
        float HoldDeadline()
        {
            var job = Job; if (job == null) return 0f;
            if (job.State == BaggageTurnaroundState.LoadingHold)
            {
                var home = job.Plan?.home;
                float drive = home != null ? home.Length/(fleet.Equipment.Cart.transitSpeed*BaggagePacing.TravelRate)*1.2f+.5f : 0f;
                return flow.RemainingHandling()+drive;
            }
            if (job.State == BaggageTurnaroundState.Withdrawing)
            {
                // The loader waits for the train to get home; the hold handler has until then.
                var train = fleet.Train; var path = train.Path;
                if (path == null || train.Distance >= path.Length-.01f) return 0f;
                return (path.Length-train.Distance)/(fleet.Equipment.Cart.transitSpeed*BaggagePacing.TravelRate)*1.1f;
            }
            return float.PositiveInfinity;
        }

        /// <summary>How the hold handler moves along the boarding route at index <paramref name="i"/>: upright round the
        /// loader, up the treads and the belt walkway; stooped for the last metres to the door; kneeling through the door
        /// and inside. (The simulation's route kneels earlier; a person stays on their feet as long as the roof allows.)</summary>
        void HoldWay(int i, out float speed, out float hip, out float lean, out float kneel)
        {
            int door = route.Count-2;
            Crew.HoldPosture(out float kneelHip, out float kneelLean); kneelLean = Mathf.Min(kneelLean,45f);
            if (i >= door) { speed = Kneeling; hip = kneelHip; lean = kneelLean; kneel = 1f; return; }
            if (i >= door-2) { speed = Crouched; hip = .72f; lean = 32f; kneel = 0f; return; }
            speed = i >= 4 ? Stairs : Walk; hip = .94f; lean = 0f; kneel = 0f;
        }
        void HoldEntryPath(Performer p)
        {
            Exits(p); p.path.Clear();
            for (int i = 2; i < route.Count; i++)
            {
                HoldWay(i,out float speed,out float hip,out float lean,out float kneel);
                var at = i == 2 ? p.groundPoint : i == route.Count-1 ? Crew.HoldIdlePose(p.worker).position : route[i].position;
                Add(p,at,speed,hip,lean,kneel);
            }
            Plan(p,Act.Idle,Goal.Post,true,Yaw(Crew.HoldIdlePose(p.worker).rotation*Vector3.forward));
        }
        void HoldExitPath(Performer p)
        {
            Exits(p); p.path.Clear();
            for (int i = route.Count-2; i >= 2; i--)
            {
                // Posture of the point being walked to; speed of the slower end of the segment.
                HoldWay(i,out float speed,out float hip,out float lean,out float kneel);
                HoldWay(i+1,out float before,out _,out _,out _);
                Add(p,i == 2 ? p.groundPoint : route[i].position,Mathf.Min(speed,before),hip,lean,kneel);
            }
            Plan(p,Act.StepUp,Goal.Return,true,Mathf.DeltaAngle(0,p.stepYaw+180f));
        }

        void StepHold(Performer p, float h)
        {
            var job = Job;
            if (p.act == Act.External) Return(p);
            if (p.rider && (job == null || p.worker >= Needed(false)))
            {
                if (p.held) Drop(p); else if (p.owns) Unreserve(p);
                if (p.visible || p.act != Act.Seated) { p.path.Clear(); Return(p); }
                p.visible = false; return;
            }
            p.visible = true;
            bool loaderMoving = Mathf.Abs(fleet.LoaderSpeed) > 0f;
            // The second hold handler rides on the loader and follows the operator in once they are round the back.
            var lead = holds[0];
            bool simOut = !Crew.HoldSeated;
            if (p.rider)
            {
                // Follow the operator in once they are a few metres ahead (and not on their way out).
                simOut = false;
                if (lead.act != Act.Seated && lead.act != Act.Grip && lead.goal != Goal.Return && job?.Plan != null &&
                    (lead.atPost || lead.act == Act.Walk)) { Exits(p); simOut = Vector3.Distance(Flat(lead.position),p.groundPoint) > 3f; }
            }
            if (p.act == Act.Seated || p.act == Act.Grip)
            {
                Seat(p); p.position = p.seat.position; p.yaw = Yaw(p.seat.rotation*Vector3.forward);
                if (p.act == Act.Grip) { p.t += h; p.reach = 1f-Ease(1f-p.t/GripTime); if (p.t >= GripTime) { p.act = Act.Seated; p.reach = 1; } }
                if (simOut && job != null && job.Plan != null && job.State != BaggageTurnaroundState.Withdrawing && !loaderMoving && fleet.Train != null &&
                    Deadline(p) > (p.rider ? RiderDismount : Dismount)+HoldEntrySeconds(p)+HoldExitFromStand()+MinWork)
                { Exits(p); p.act = Act.Unseat; p.t = 0; }
                return;
            }
            if (loaderMoving) { Placed(p); return; }
            float deadline = Deadline(p);
            bool recalled = job == null || job.Finished || job.State == BaggageTurnaroundState.Undocking ||
                job.State == BaggageTurnaroundState.Withdrawing && deadline <= 0f;
            switch (p.act)
            {
                case Act.Unseat:
                {
                    float unseat = p.rider ? RiderUnseat : UnseatTime;
                    p.t += h; if (!p.rider) p.reach = 1f-Ease(p.t/(unseat*.8f));
                    if (!p.rider) p.yaw = Mathf.LerpAngle(Yaw(p.seat.rotation*Vector3.forward),p.stepYaw,Ease(p.t/unseat)*.45f);
                    if (p.t >= unseat) { p.act = Act.Rise; p.t = 0; }
                    break;
                }
                case Act.Rise:
                {
                    float rise = p.rider ? RiderRise : RiseTime;
                    p.t += h; Rise(p,Ease(p.t/rise));
                    if (p.t >= rise) { p.act = Act.StepDown; p.t = 0; }
                    break;
                }
                case Act.StepDown:
                {
                    float step = p.rider ? RiderStep : StepTime;
                    p.t += h; StepDown(p,Ease(p.t/step));
                    if (p.t >= step) { HoldEntryPath(p); }
                    break;
                }
                case Act.StepUp:
                {
                    float up = p.rider ? RiderUp : StepTime;
                    p.t += h; StepUp(p,Ease(p.t/up));
                    if (p.t >= up) { p.act = Act.SitDown; p.t = 0; Seat(p); }
                    break;
                }
                case Act.SitDown:
                {
                    float sit = p.rider ? RiderSit : SitTime;
                    p.t += h; Seat(p); SitDown(p,Ease(p.t/sit));
                    if (p.t >= sit) { p.act = p.rider ? Act.Seated : Act.Grip; p.t = 0; p.atPost = false; p.hurry = false; }
                    break;
                }
                case Act.Walk:
                    if (p.goal == Goal.Carry && (recalled || deadline <= HoldExitSeconds(p) && !p.held) ||
                        p.goal == Goal.Post && (p.atPost || p.rider) && (recalled || deadline <= HoldExitSeconds(p))) { GoBack(p,recalled || deadline < HoldExitSeconds(p)-Margin); break; }
                    // A carry that would make the handler late is finished briskly.
                    if (p.held && !p.hurry && deadline < PathTime(p)+PlaceTime+ReleaseTime+HoldExitSeconds(p)) p.hurry = true;
                    if (WalkStep(p,h)) { p.act = p.after; p.t = 0; if (p.goal == Goal.Post || p.goal == Goal.Carry) p.atPost = true; }
                    if (p.held) Carry(p,1f,false);
                    break;
                default:
                    HoldWork(p,h,deadline,recalled);
                    break;
            }
        }

        void HoldWork(Performer p, float h, float deadline, bool recalled)
        {
            bool loading = flow.Loading;
            if (recalled && !p.held) { GoBack(p,true); return; }
            Crew.HoldPosture(out float kneelHip, out float kneelLean); kneelLean = Mathf.Min(kneelLean,45f);
            var stand = Crew.HoldIdlePose(p.worker);
            float standYaw = Yaw(stand.rotation*Vector3.forward);
            switch (p.act)
            {
                case Act.Idle:
                {
                    p.t += h; p.reach = Mathf.MoveTowards(p.reach,0f,h/ReleaseTime);
                    float exit = HoldExitSeconds(p);
                    if (deadline <= exit) { GoBack(p,deadline < exit-Margin); return; }
                    if (Vector3.Distance(Flat(p.position),Flat(stand.position)) > .1f)
                    {
                        p.path.Clear(); Add(p,stand.position,Kneeling,kneelHip,kneelLean,1f); Plan(p,Act.Idle,Goal.Post,true,standYaw); return;
                    }
                    p.yaw = Mathf.MoveTowardsAngle(p.yaw,standYaw,TurnRate*.5f*h);
                    if (!HoldSession || fleet.Station.accessBlocked) return;
                    // A stow takes the handler deeper; the way out is then longer by the same walk.
                    float cycle = StowCycle(p);
                    p.willTake = deadline > exit+cycle;
                    // No time for another bag: head out now rather than wait at the post.
                    if (!p.willTake) { GoBack(p,false); return; }
                    // Two in the hold: not into the other's working place (wait at the post until they are clear).
                    if (Needed(false) >= 2 && flow.PeekHold(out var next) && HoldCrowded(p,WorkPoint(next))) break;
                    if (loading)
                    {
                        if (deadline > exit+cycle && flow.Waiting(true,out p.bagBottom,out p.bagRotation,out p.variant,out _))
                        {
                            flow.TakeWaiting(true); p.owns = true; p.held = false; p.draw = true; p.size = Size(p.variant);
                            p.act = Act.Reach; p.t = 0; Pick(p);
                        }
                    }
                    else if (deadline > exit+cycle && flow.HoldOwed > 0 &&
                             flow.ReserveHold(p.worker,out p.variant,out p.bagBottom,out p.bagRotation))
                    {
                        p.owns = true; p.held = false; p.draw = true; p.size = Size(p.variant);
                        HoldPathTo(p,p.bagBottom); Plan(p,Act.Reach,Goal.Carry,true,Yaw(Flat(p.bagBottom-WorkPoint(p.bagBottom))));
                        Pick(p);
                    }
                    break;
                }
                case Act.Reach:
                    p.t += h; p.reach = Ease(p.t/ReachTime); p.hip = kneelHip; p.lean = Mathf.Lerp(p.lean,kneelLean,Ease(p.t/ReachTime));
                    if (p.t >= ReachTime) { p.act = Act.Lift; p.t = 0; p.held = true; p.draw = true; p.load = .5f; p.bagFrom = p.bagBottom; p.rotFrom = p.bagRotation; }
                    break;
                case Act.Lift:
                {
                    p.t += h; float u = Ease(p.t/LiftTime); Carry(p,u,true);
                    if (p.t >= LiftTime)
                    {
                        p.path.Clear();
                        if (loading)
                        {
                            if (!flow.ReserveHold(p.worker,out _,out p.bagTo,out p.rotTo)) { p.bagTo = Job.Packing.BagPosition(null,0); p.rotTo = p.bagRotation; }
                            HoldPathTo(p,p.bagTo); Plan(p,Act.Place,Goal.Carry,true,Yaw(Flat(p.bagTo-WorkPoint(p.bagTo))));
                        }
                        else
                        {
                            p.bagTo = flow.BeltPickup; p.rotTo = flow.BeltRotation(flow.Length);
                            Add(p,AislePoint(p.position,p.worker),Kneeling,kneelHip,kneelLean,1f); Add(p,stand.position,Kneeling,kneelHip,kneelLean,1f);
                            Plan(p,Act.Place,Goal.Carry,true,standYaw);
                        }
                    }
                    break;
                }
                case Act.Place:
                {
                    if (p.t == 0f && !loading && !flow.TopClear) { Carry(p,1f,false); break; }
                    p.t += h; float u = Ease(p.t/PlaceTime); Set(p,u);
                    if (p.t >= PlaceTime)
                    {
                        if (loading) { if (flow.HoldReservation(p.worker)) flow.StowHold(p.worker,p.variant); else flow.StowAny(p.variant); }
                        else flow.PutOnBelt(false,p.variant,-1);
                        p.held = false; p.owns = false; p.load = 0; p.act = Act.Release; p.t = 0;
                    }
                    break;
                }
                case Act.Release:
                    p.t += h; p.reach = 1f-Ease(p.t/ReleaseTime);
                    if (p.t >= ReleaseTime)
                    {
                        p.reach = 0; p.act = Act.Idle; p.t = 0;
                        if (deadline <= HoldExitSeconds(p)+Margin) { GoBack(p,deadline < HoldExitSeconds(p)); break; }
                        if (loading) { p.path.Clear(); Add(p,AislePoint(p.position,p.worker),Kneeling,kneelHip,kneelLean,1f); Add(p,stand.position,Kneeling,kneelHip,kneelLean,1f); Plan(p,Act.Idle,Goal.Post,true,standYaw); }
                    }
                    break;
            }
        }

        Vector3 Side => Vector3.Cross(Vector3.up,Job.Plan.inward);
        /// <summary>Where a kneeling handler works a bag at <paramref name="bag"/>, as the simulation's packing places them.</summary>
        Vector3 WorkPoint(Vector3 bag)
        {
            var job = Job; var plan = job.Plan; var packing = job.Packing;
            float x = Vector3.Dot(bag-plan.door,Side), z = Vector3.Dot(bag-plan.door,plan.inward);
            if (packing.SideMax-packing.SideMin < 1.6f)
                return plan.door+plan.inward*Mathf.Max(.6f,z-.68f)+Side*.08f;
            float side = x < .3f ? x+.52f : x-.52f;
            return plan.door+Side*side+plan.inward*Mathf.Clamp(z-.1f,.5f,packing.Depth-.45f);
        }
        /// <summary>The handler's own aisle along the hold, through their post (the second one 0.7 m across).</summary>
        Vector3 AislePoint(Vector3 at, int worker = 0)
        {
            var plan = Job.Plan; float z = Vector3.Dot(at-plan.door,plan.inward);
            return plan.door+plan.inward*z+Side*(.24f-.7f*worker);
        }
        /// <summary>The other hold handler is at, or on the way to, a place within reach of <paramref name="work"/>.</summary>
        bool HoldCrowded(Performer p, Vector3 work)
        {
            var q = holds[1-p.worker]; if (!q.visible || q.act == Act.Seated || q.act == Act.Grip) return false;
            if (Vector3.Distance(Flat(q.position),Flat(work)) < Clear+.1f) return true;
            return q.act == Act.Walk && q.path.Count > 0 && Vector3.Distance(Flat(q.path[q.path.Count-1].p),Flat(work)) < Clear+.1f;
        }
        /// <summary>Seconds of the next stow (loading) or pick (unloading) and the extra way out it adds: the walk to the
        /// bag's actual place and back, the handling, and slack.</summary>
        float StowCycle(Performer p)
        {
            float metres = 1.2f;
            if (flow.PeekHold(out var bottom))
            {
                var stand = Crew.HoldIdlePose(p.worker).position; var work = WorkPoint(bottom);
                metres = Vector3.Distance(Flat(stand),Flat(AislePoint(work,p.worker)))+Vector3.Distance(Flat(AislePoint(work,p.worker)),Flat(work));
            }
            return 2f*metres/Kneeling+ReachTime+LiftTime+PlaceTime+ReleaseTime+Margin;
        }
        void HoldPathTo(Performer p, Vector3 bag)
        {
            Crew.HoldPosture(out float hip, out float lean); lean = Mathf.Min(lean,45f);
            var work = WorkPoint(bag); p.path.Clear();
            Add(p,AislePoint(work,p.worker),Kneeling,hip,lean,1f); Add(p,work,Kneeling,hip,lean,1f);
        }

        // ================================================================== helpers (second handlers), depot

        void Return(Performer p)
        {
            // Back from the literal depot path: the simulation has the worker seated.
            Seat(p); p.act = Act.Seated; p.sit = 1; p.feet = 1; p.reach = 1; p.hip = .94f; p.lean = 0; p.kneel = 0;
            p.position = p.seat.position; p.yaw = Yaw(p.seat.rotation*Vector3.forward); p.visible = true; p.snap = true;
        }
        /// <summary>Last resort: the vehicle is moving and the performer is not in the seat.</summary>
        void Placed(Performer p)
        {
            if (p.held || p.owns) { if (p.held) Drop(p); else Unreserve(p); }
            Placements++;
            Debug.LogWarning($"Baggage crew performance: {(p.ground ? "ground" : "hold")} handler {p.worker} placed in the seat ({p.act}/{p.goal}, {Job?.State}, path {p.index}/{p.path.Count}, at {p.position}).");
            p.path.Clear(); Return(p);
        }
        /// <summary>True once after a performer was placed directly; the rig should snap.</summary>
        public bool TakeSnap(bool ground, int worker) { var p = Get(ground,worker); bool s = p.snap; p.snap = false; return s; }

        // ================================================================== pose

        void Compose(Performer p)
        {
            if (p.act == Act.External) return;
            RampCrewPose pose;
            if (p.act == Act.Seated || p.act == Act.Grip)
            {
                pose = p.seat; pose.leftReach = pose.rightReach = p.rider ? 0f : p.reach; pose.hands = false;
                pose.sit = 1; pose.feet = 1;
                p.pose = pose; return;
            }
            var rot = YawRotation(p.yaw);
            pose = RampCrewPose.Standing(p.position,rot*Vector3.forward);
            pose.hipHeight = p.hip; pose.lean = p.lean; pose.kneel = p.kneel; pose.sit = p.sit;
            if (p.sit > 0f || p.feet > 0f)
            {
                // Feet blend from the pedals (actor frame of the seat) to a standing stance under the hips.
                var inv = Quaternion.Inverse(rot);
                Vector3 PedalWorld(Vector3 local) => p.seat.position+p.seat.rotation*local;
                float u = 1f-p.sit;
                var stanceL = new Vector3(-.12f,0,.02f); var stanceR = new Vector3(.12f,0,.02f);
                var stepY = p.stepPoint.y;
                var l = Vector3.Lerp(inv*(PedalWorld(p.seat.leftFoot)-p.position),stanceL+Vector3.up*(stepY-p.position.y),u);
                var r = Vector3.Lerp(inv*(PedalWorld(p.seat.rightFoot)-p.position),stanceR+Vector3.up*(stepY-p.position.y),u);
                pose.leftFoot = l; pose.rightFoot = r; pose.feet = p.feet;
            }
            if (p.reach > 0f && !p.rider && (p.act == Act.Unseat || p.act == Act.Grip))
            { pose.leftHand = Quaternion.Inverse(rot)*(p.seat.position+p.seat.rotation*p.seat.leftHand-p.position); pose.rightHand = Quaternion.Inverse(rot)*(p.seat.position+p.seat.rotation*p.seat.rightHand-p.position);
              pose.leftReach = pose.rightReach = p.reach; pose.leftGrip = pose.rightGrip = RampCrewGrip.Wheel; }
            else if (p.owns && p.reach > 0f)
            {
                var axis = p.bagRotation*(p.across ? Vector3.right : Vector3.forward);
                if (Vector3.Dot(axis,rot*Vector3.right) < 0f) axis = -axis;
                float half = (p.across ? p.size.x : p.size.y)*.5f+.012f;
                var centre = p.bagBottom+Vector3.up*(p.size.z*.55f);
                var inv = Quaternion.Inverse(rot);
                pose.leftHand = inv*(centre-axis*half-p.position); pose.rightHand = inv*(centre+axis*half-p.position);
                if (p.act == Act.Reach)
                {
                    // The hands travel from where they hang to the grip with the reach (the rig then tracks them exactly at
                    // any clock speed, instead of chasing a grip that appears out of reach).
                    float u = Ease(p.reach); bool low = p.kneel > .5f;
                    var restL = new Vector3(low ? -.245f : -.27f,p.hip-(low ? .17f : .025f),low ? .025f : .05f);
                    var restR = new Vector3(-restL.x,restL.y,restL.z);
                    pose.leftHand = Vector3.Lerp(restL,pose.leftHand,u); pose.rightHand = Vector3.Lerp(restR,pose.rightHand,u);
                }
                pose.leftReach = pose.rightReach = p.reach; pose.leftGrip = pose.rightGrip = RampCrewGrip.Bag;
                pose.load = p.load;
            }
            else if (p.reach > 0f)
            {
                // Releasing: keep the last palm targets while the weight eases out.
                pose.leftHand = p.pose.leftHand; pose.rightHand = p.pose.rightHand; pose.leftReach = pose.rightReach = p.reach;
                pose.leftGrip = p.pose.leftGrip; pose.rightGrip = p.pose.rightGrip;
            }
            p.pose = pose;
        }
    }
}
