using System.Collections.Generic;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Two workers per bulk fleet. Boarding, walking and handovers advance only in simulation time.</summary>
    public sealed class BaggageCrew
    {
        readonly BaggageFleet fleet;
        readonly List<RampCrewPose> groundRoute = new List<RampCrewPose>(), holdRoute = new List<RampCrewPose>();
        float groundTime, holdTime, groundDuration, holdDuration;
        bool loading;
        public bool GroundSeated => groundTime <= 0f;
        public bool HoldSeated => holdTime <= 0f;
        public bool GroundReady => groundRoute.Count > 0 && groundTime >= groundDuration - .0001f;
        public bool HoldReady => holdRoute.Count > 0 && holdTime >= holdDuration - .0001f;
        public float GroundProgress => groundTime;
        public float HoldProgress => holdTime;
        RampCrewType Type => fleet.Station.crewType;
        float WalkSpeed => Type != null ? Type.walkSpeed : 1.2f;
        float ReturnSpeed => Type != null ? Type.returnSpeed : 1.4f;
        float Pick => Type != null ? Type.pickSeconds : .65f;
        float Lift => Type != null ? Type.liftSeconds : .45f;
        float Turn => Type != null ? Type.turnSeconds : .7f;
        float Place => Type != null ? Type.placeSeconds : .65f;
        float TurnBack => Type != null ? Type.turnBackSeconds : .5f;
        float Settle => Type != null ? Type.settleSeconds : .3f;
        float HoldPick => Type != null ? Type.holdPickSeconds : .55f;
        float HoldLift => Type != null ? Type.holdLiftSeconds : .4f;
        float HoldStow => Type != null ? Type.holdStowSeconds : .75f;
        float HoldReset => Type != null ? Type.holdResetSeconds : .55f;
        const float KneelSpeed = .8f;
        BaggageBatch cachedHoldBatch;
        float cachedHoldWalk;
        float HoldWalk
        {
            get
            {
                var batch = Job?.TransferBatch; if (batch == null) return 0;
                if (batch == cachedHoldBatch) return cachedHoldWalk;
                float longest = 0;
                for (int i = 0; i < batch.Count; i++) longest = Mathf.Max(longest,HoldRouteLength(i));
                cachedHoldBatch = batch; return cachedHoldWalk = longest/KneelSpeed;
            }
        }
        public float HoldCycle => HoldPick + HoldLift + HoldStow + HoldReset + HoldWalk*2f;
        float HoldOutbound => HoldCycle-HoldReset;
        float Distance => Vector3.Distance(CartStand, BeltStand);
        public float GroundOutbound => Pick + Lift + Turn + Distance / WalkSpeed + Place;
        public float GroundCycle => GroundOutbound + TurnBack + Distance / ReturnSpeed + Settle;
        float ConveyorSeconds => (Vector3.Distance(Belt, Front) + Vector3.Distance(Front, HoldPickup)) / BaggageLoadPath.BeltSpeed;
        public int GroundWorkers => fleet.Workers>=3 ? 2 : 1;
        public int HoldWorkers => fleet.Workers>=4 ? 2 : 1;
        public float Interval => Mathf.Max(fleet.Station.secondsPerBag, GroundCycle/GroundWorkers, HoldCycle/HoldWorkers);
        public float Travel(bool load) => load ? GroundOutbound + ConveyorSeconds + HoldCycle :
            HoldOutbound + ConveyorSeconds + GroundCycle;
        BaggageTurnaround Job => fleet.Job;
        BaggageServicePlan Plan => Job.Plan;
        static Vector3 World(BaggageVehiclePose p, Vector3 local)
        { var at = p.Point(local); return new Vector3(at.x, local.y, at.y); }
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static Vector3 Direction(BaggageVehiclePose p) => new Vector3(p.facing.x, 0, p.facing.y);
        Vector3 Belt => BaggageLoadPath.Point(fleet, Plan, 1);
        Vector3 Front => BaggageLoadPath.Point(fleet, Plan, 2);
        Vector3 Side => Vector3.Cross(Vector3.up, Plan.inward);
        float CartSide
        {
            get
            {
                var delta = BaggageServicePlan.XZ(Belt) - fleet.CartPose.position;
                return Vector2.Dot(delta, fleet.CartPose.Right) >= 0 ? 1f : -1f;
            }
        }
        Vector3 CartLocal
        {
            get
            {
                var c = fleet.Equipment.Cart.baggage;
                var delta = BaggageServicePlan.XZ(Belt) - fleet.CartPose.position;
                return new Vector3(CartSide > 0 ? c.loadMax.x - .24f : c.loadMin.x + .24f,
                    c.loadMin.y, Mathf.Clamp(Vector2.Dot(delta, fleet.CartPose.facing), c.loadMin.z + .40f, c.loadMax.z - .40f));
            }
        }
        public Vector3 CartPickup => World(fleet.CartPose, CartLocal);
        public Vector3 CartStand
        {
            get { var p = CartLocal; p.x = CartSide > 0 ? fleet.Equipment.Cart.bounds.max.x + .30f : fleet.Equipment.Cart.bounds.min.x - .30f;
                p.y = 0; return World(fleet.CartPose, p); }
        }
        public Vector3 BeltStand => Flat(Belt) + Flat(CartStand - Belt).normalized * .64f;
        public Vector3 HoldPickup => Plan.door + Plan.inward * .42f;
        public Vector3 HoldStowPoint => Plan.door + Plan.inward * Mathf.Min(1.22f, BaggageLoadPath.HoldDepth(Job) - .35f) - Side * .23f;
        public Vector3 HoldStand => Plan.door + Plan.inward * .77f + Side * .24f;

        public BaggageCrew(BaggageFleet fleet) { this.fleet = fleet; }
        public void Reset() { groundTime = holdTime = groundDuration = holdDuration = 0; groundRoute.Clear(); holdRoute.Clear(); }

        public static RampCrewPose Workstation(VehicleType type, BaggageVehiclePose vehicle)
        {
            var c = type.crew;
            if (c == null) return RampCrewPose.Standing(World(vehicle, Vector3.up * .4f), Direction(vehicle));
            var origin = c.position - Vector3.up * (c.seated ? .94f : 0f);
            var p = RampCrewPose.Standing(World(vehicle, origin), Direction(vehicle));
            p.seated = c.seated; p.hands = true;
            if (!c.seated) p.lean = 15f;
            p.leftHand = c.leftHand - origin; p.rightHand = c.rightHand - origin;
            p.leftFoot = c.leftFoot - origin; p.rightFoot = c.rightFoot - origin;
            return p;
        }

        RampCrewPose ReadyGround(bool load) => RampCrewPose.Standing(load ? CartStand : BeltStand,
            Flat((load ? CartPickup : Belt) - (load ? CartStand : BeltStand)).normalized);
        RampCrewPose ReadyHold()
        {
            var p = RampCrewPose.Standing(HoldStand, -Plan.inward);
            Job.HoldConfiguration.aircraft.TryGetPoint(Job.HoldConfiguration.cargoPoint, out var door);
            float height = Mathf.Clamp(door.size.y - .10f, .78f, 1.15f);
            p.hipHeight = height < 1f ? .27f : .38f;
            p.lean = Mathf.Max(60f, Mathf.Acos(Mathf.Clamp((height - p.hipHeight - .25f) / .55f, .1f, 1f)) * Mathf.Rad2Deg);
            p.kneeling = true; return p;
        }

        internal void CartChanged()
        {
            if (groundRoute.Count == 0) return;
            var next = ReadyGround(loading); var previous = groundRoute[groundRoute.Count-1];
            var direction = Flat(next.position-previous.position).normalized;
            var midpoint = RampCrewPose.Standing(Vector3.Lerp(previous.position,next.position,.5f),direction);
            groundRoute.Add(midpoint); groundRoute.Add(next); groundDuration = Duration(groundRoute);
        }

        public bool MoveGround(bool work, float dt, bool load = false)
        {
            if (fleet.Station.accessBlocked) return work ? GroundReady : GroundSeated;
            if (work && groundRoute.Count == 0)
            {
                loading = load;
                var seat = Workstation(fleet.Equipment.Tractor, fleet.Train.Tractor); groundRoute.Add(seat);
                var local = fleet.Equipment.Tractor.crew?.position ?? Vector3.zero;
                local.x = CartSide > 0 ? fleet.Equipment.Tractor.bounds.max.x + .30f : fleet.Equipment.Tractor.bounds.min.x - .30f; local.y = .25f;
                var step = RampCrewPose.Standing(World(fleet.Train.Tractor, local), CartSide * Vector3.Cross(Vector3.up, Direction(fleet.Train.Tractor)));
                step.hipHeight = .70f; groundRoute.Add(step);
                step.position.y = 0; step.hipHeight = .94f; groundRoute.Add(step);
                // Walk outside the drawbar and the cart rather than through either vehicle.
                var corner = CartStand; corner += Direction(fleet.CartPose) * (fleet.Equipment.Cart.bounds.max.z - CartLocal.z + .55f);
                groundRoute.Add(RampCrewPose.Standing(corner, (CartStand - corner).normalized));
                groundRoute.Add(ReadyGround(load));
                groundDuration = Duration(groundRoute);
            }
            groundTime = Mathf.MoveTowards(groundTime, work ? groundDuration : 0, dt);
            bool done = work ? GroundReady : GroundSeated;
            if (!work && done) groundRoute.Clear();
            return done;
        }

        public bool MoveHold(bool work, float dt)
        {
            if (fleet.Station.accessBlocked) return work ? HoldReady : HoldSeated;
            if (work && holdRoute.Count == 0)
            {
                holdRoute.Add(Workstation(fleet.Equipment.Loader, fleet.LoaderPose));
                var b = fleet.Equipment.Loader.baggage;
                var exit = fleet.Equipment.Loader.crew?.position ?? Vector3.zero;
                exit.x = fleet.Equipment.Loader.bounds.min.x - .25f; exit.y = .30f;
                var step = RampCrewPose.Standing(World(fleet.LoaderPose, exit), -Vector3.Cross(Vector3.up, Direction(fleet.LoaderPose)));
                step.hipHeight = .72f; holdRoute.Add(step);
                exit.y = 0; holdRoute.Add(RampCrewPose.Standing(World(fleet.LoaderPose, exit), -Direction(fleet.LoaderPose)));
                var station = fleet.Equipment.Loader.crew;
                var access = station?.accessSteps;
                exit.z = access != null && access.Length > 0 ? access[0].z - .4f : b.beltRear.z - .48f;
                holdRoute.Add(RampCrewPose.Standing(World(fleet.LoaderPose, exit), -Direction(fleet.LoaderPose)));
                var walkway = station != null && station.walkway != Vector3.zero ? station.walkway : new Vector3(.54f,0,.6f);
                exit.x = b.beltRear.x + walkway.x;
                holdRoute.Add(RampCrewPose.Standing(World(fleet.LoaderPose, exit), Direction(fleet.LoaderPose)));
                if (access != null)
                    foreach (var tread in access) holdRoute.Add(RampCrewPose.Standing(World(fleet.LoaderPose,tread),Direction(fleet.LoaderPose)));
                var top = b.beltRear + new Vector3(0, fleet.LoaderHeight - b.beltRear.y,
                    Mathf.Sqrt(b.beltLength * b.beltLength - Mathf.Pow(fleet.LoaderHeight - b.beltRear.y,2)));
                // The stationary loader's authored access strip runs along the right edge of the belt.
                for (int i = 0; i <= 8; i++)
                {
                    float t = Mathf.Lerp(walkway.z / b.beltLength,.91f,i/8f); var at = Vector3.Lerp(b.beltRear,top,t); at.x += walkway.x; at.y += walkway.y;
                    var p = RampCrewPose.Standing(World(fleet.LoaderPose,at), Direction(fleet.LoaderPose));
                    if (i >= 7) { var crouch = ReadyHold(); p.hipHeight = crouch.hipHeight; p.lean = crouch.lean; p.kneeling = true; }
                    holdRoute.Add(p);
                }
                var enter = ReadyHold(); enter.position = Plan.door - Plan.inward * .12f; holdRoute.Add(enter);
                holdRoute.Add(ReadyHold());
                holdDuration = Duration(holdRoute);
            }
            holdTime = Mathf.MoveTowards(holdTime, work ? holdDuration : 0, dt);
            bool done = work ? HoldReady : HoldSeated;
            if (!work && done) holdRoute.Clear();
            return done;
        }

        float Segment(RampCrewPose a, RampCrewPose b) => Mathf.Max(.35f, Vector3.Distance(a.position,b.position) /
            (Type != null ? Type.boardingSpeed : .9f)) + Quaternion.Angle(a.rotation,b.rotation) / 180f * .3f;
        float Duration(List<RampCrewPose> route)
        { float t = 0; for (int i = 1; i < route.Count; i++) t += Segment(route[i-1],route[i]); return t; }
        RampCrewPose Route(List<RampCrewPose> points, float time, bool reverse)
        {
            for (int i = 1; i < points.Count; i++)
            {
                float segment = Segment(points[i-1], points[i]);
                if (time > segment) { time -= segment; continue; }
                var a = points[i-1]; var b = points[i]; float t = Mathf.Clamp01(time / segment);
                var p = b; p.position = Vector3.Lerp(a.position,b.position,t); p.rotation = Quaternion.Slerp(a.rotation,b.rotation,t);
                p.hipHeight = Mathf.Lerp(a.hipHeight,b.hipHeight,t); p.lean = Mathf.Lerp(a.lean,b.lean,t);
                p.stride = Vector3.Distance(a.position,b.position) > .5f && !p.kneeling ? .7f : .15f;
                p.phase = (reverse ? -1 : 1) * time * 1.5f;
                if (reverse && i > 1 && !p.kneeling) p.rotation *= Quaternion.Euler(0,180,0);
                if (i == 1) { p.hands = true; p.leftHand = Vector3.Lerp(a.leftHand,new Vector3(-.27f,.9f,.05f),t);
                    p.rightHand = Vector3.Lerp(a.rightHand,new Vector3(.27f,.9f,.05f),t); }
                return p;
            }
            return points[points.Count-1];
        }

        public RampCrewPose GroundPose => GroundPoseFor(0);
        public RampCrewPose GroundPoseFor(int worker)
        {
                if (Job?.DepotTransfer==true) return DepotPose(worker);
                if (groundRoute.Count == 0) return Workstation(fleet.Equipment.Tractor, fleet.Train.Tractor);
                if (!GroundReady) return Route(groundRoute,groundTime, Job.State != BaggageTurnaroundState.LoadingHold && Job.State != BaggageTurnaroundState.UnloadingHold);
                if (Job.TransferBatch == null || Job.DepotTransfer) return GroundReadyPose(loading,worker);
                bool load = Job.TransferTo == Job.Hold; float offset = load ? 0 : HoldOutbound + ConveyorSeconds;
                float e = Job.TransferSeconds - offset;
                int bag=Mathf.Min(Job.TransferBatch.Count-1,Mathf.FloorToInt(e/Interval));
                while (bag>=0 && bag%GroundWorkers!=worker) bag--;
                if (bag<0 || bag>=Job.TransferBatch.Count || e-bag*Interval>=GroundCycle) return GroundReadyPose(load,worker);
                GroundCycleAt(e-bag*Interval,load,out var pose,out _,worker); return pose;
        }
        public RampCrewPose HoldPose => HoldPoseFor(0);
        public RampCrewPose HoldPoseFor(int worker)
        {
                if (holdRoute.Count == 0) return Workstation(fleet.Equipment.Loader, fleet.LoaderPose);
                if (!HoldReady) return Route(holdRoute,holdTime, Job.State == BaggageTurnaroundState.Withdrawing);
                if (Job.TransferBatch == null || Job.DepotTransfer) return HoldReadyPose(worker);
                bool load = Job.TransferTo == Job.Hold; float offset = load ? GroundOutbound + ConveyorSeconds : 0;
                float e = Job.TransferSeconds - offset;
                int bag=Mathf.Min(Job.TransferBatch.Count-1,Mathf.FloorToInt(e/Interval));
                while (bag>=0 && bag%HoldWorkers!=worker) bag--;
                if (bag<0 || bag>=Job.TransferBatch.Count || e-bag*Interval>=HoldCycle) return HoldReadyPose(worker);
                HoldCycleAt(e-bag*Interval,load,bag,out var pose,out _); return pose;
        }

        Vector3 GroundOffset(int worker) => Direction(fleet.CartPose)*(.85f*worker);
        RampCrewPose GroundReadyPose(bool load,int worker)
        { var pose=ReadyGround(load); pose.position+=GroundOffset(worker); return pose; }
        RampCrewPose HoldReadyPose(int worker)
        { var pose=ReadyHold(); pose.position-=Side*(.7f*worker); return pose; }

        RampCrewPose DepotPose(int worker)
        {
            var depot=fleet.Station.depot;
            var pickup=depot.CartPickup(fleet,worker);
            var feed=depot.Feed(fleet,Job.TransferFrom==fleet.Arrivals || Job.TransferFrom==fleet.Dispatch);
            var at=Flat(pickup)+new Vector3(depot.Right.x,0,depot.Right.y)*.75f;
            var pose=RampCrewPose.Standing(at,(Flat(pickup)-at).normalized);
            float offset=FleetIncoming() ? 0 : depot.ConveyorSeconds;
            float elapsed=Job.TransferSeconds-offset;
            int bag=Mathf.Min(Job.TransferBatch.Count-1,Mathf.FloorToInt(elapsed/Job.TransferInterval));
            while (bag>=0 && bag%GroundWorkers!=worker) bag--;
            if (bag<0 || elapsed-bag*Job.TransferInterval>3.6f) return pose;
            var current=depot.BagAt(fleet,elapsed-bag*Job.TransferInterval+offset,bag);
            pose.hipHeight=.83f; pose.lean=12f;
            float progress=Mathf.Clamp01((elapsed-bag*Job.TransferInterval)/3.6f);
            pose.rotation=Quaternion.Slerp(pose.rotation,Quaternion.LookRotation(new Vector3(depot.Right.x,0,depot.Right.y)),Mathf.Sin(progress*Mathf.PI));
            pose.position=Vector3.Lerp(at,Flat(feed)-new Vector3(depot.Right.x,0,depot.Right.y)*.65f,Mathf.Sin(progress*Mathf.PI));
            Hands(ref pose,current.bottom,current.rotation); return pose;
            bool FleetIncoming() => fleet.IsCart(Job.TransferFrom);
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t*t*(3f-2f*t); }
        static void Hands(ref RampCrewPose pose, Vector3 bag, Quaternion rotation)
        {
            pose.hands = true;
            var inverse = Quaternion.Inverse(pose.rotation);
            var span = rotation*Vector3.right*.20f;
            if (Vector3.Dot(span,pose.rotation*Vector3.right) < 0) span = -span;
            pose.leftHand = inverse * (bag + Vector3.up * .13f - span - pose.position);
            pose.rightHand = inverse * (bag + Vector3.up * .13f + span - pose.position);
            if (pose.kneeling)
            {
                // Shuffle the body toward the grip instead of stretching an arm across a rotated suitcase.
                var chest = Vector3.up*pose.hipHeight + Quaternion.AngleAxis(pose.lean,Vector3.right)*Vector3.up*.46f;
                var grip = new Vector3(0,-.65f,.76f).normalized*.075f;
                for (int i = 0; i < 12; i++)
                {
                    float reach = Mathf.Max(Vector3.Distance(chest+Vector3.left*.205f,pose.leftHand-grip),
                        Vector3.Distance(chest+Vector3.right*.205f,pose.rightHand-grip));
                    if (reach <= .562f) break;
                    var shift = Vector3.ProjectOnPlane((pose.leftHand+pose.rightHand)*.5f-grip-chest,Vector3.up).normalized*.025f;
                    pose.position += pose.rotation*shift; pose.leftHand -= shift; pose.rightHand -= shift;
                }
            }
        }

        void GroundCycleAt(float e, bool load, out RampCrewPose pose, out Vector3 bag,int worker=0)
        {
            float reach = Ease(e / Pick);
            var shift=GroundOffset(worker);
            var source = (load ? CartStand : BeltStand)+shift; var target = (load ? BeltStand : CartStand)+shift;
            var from = load ? CartPickup+shift : Belt; var to = load ? Belt : CartPickup+shift;
            pose = GroundReadyPose(load,worker); var startRotation = pose.rotation;
            var travelRotation = Quaternion.LookRotation(Flat(target-source).normalized);
            var endRotation = Quaternion.LookRotation(Flat(to-target).normalized);
            Vector3 Carry(RampCrewPose p) => p.position + p.rotation * new Vector3(0,.85f,.43f);
            bag = from;
            if (e < Pick) { pose.hipHeight = Mathf.Lerp(.94f,.69f,Ease(e/Pick)); pose.lean = Mathf.Lerp(0,28,Ease(e/Pick)); }
            else if ((e -= Pick) < Lift) { float t = Ease(e/Lift); pose.hipHeight = Mathf.Lerp(.69f,.94f,t); pose.lean = Mathf.Lerp(28,0,t); bag = Vector3.Lerp(from,Carry(pose),t); }
            else if ((e -= Lift) < Turn) { pose.rotation = Quaternion.Slerp(startRotation,travelRotation,Ease(e/Turn)); bag = Carry(pose); }
            else if ((e -= Turn) < Distance/WalkSpeed)
            { float t = e/(Distance/WalkSpeed); pose.position = Vector3.Lerp(source,target,t); pose.rotation = Quaternion.Slerp(travelRotation,endRotation,Ease((t-.75f)*4)); pose.stride = .65f; pose.phase = e*1.5f; bag = Carry(pose); }
            else if ((e -= Distance/WalkSpeed) < Place)
            { pose.position = target; pose.rotation = endRotation; float t = Ease(e/Place); pose.hipHeight = Mathf.Lerp(.94f,.74f,t); pose.lean = 22*t; bag = Vector3.Lerp(Carry(pose),to,t); }
            else
            {
                bag = to; pose.position = target; pose.rotation = endRotation;
                if ((e -= Place) < TurnBack) pose.rotation = Quaternion.Slerp(endRotation,travelRotation * Quaternion.Euler(0,180,0),Ease(e/TurnBack));
                else if ((e -= TurnBack) < Distance/ReturnSpeed)
                { float t = e/(Distance/ReturnSpeed); pose.position = Vector3.Lerp(target,source,t); pose.rotation = Quaternion.Slerp(travelRotation * Quaternion.Euler(0,180,0),startRotation,Ease((t-.75f)*4)); pose.stride = .8f; pose.phase = e*1.5f; }
                else pose = GroundReadyPose(load,worker);
                return;
            }
            Hands(ref pose,bag,pose.rotation);
            if (reach < 1f)
            {
                pose.leftHand = Vector3.Lerp(new Vector3(-.27f,pose.hipHeight-.025f,.05f),pose.leftHand,reach);
                pose.rightHand = Vector3.Lerp(new Vector3(.27f,pose.hipHeight-.025f,.05f),pose.rightHand,reach);
            }
        }

        Vector3 HoldAisle(int bag)
        {
            var target = Job.Packing.WorkerPosition(Job.TransferBatch,bag);
            float depth = Vector3.Dot(target-Plan.door,Plan.inward);
            return Plan.door+Plan.inward*depth+Side*.24f;
        }
        float HoldRouteLength(int bag) => Vector3.Distance(HoldStand,HoldAisle(bag))+
            Vector3.Distance(HoldAisle(bag),Job.Packing.WorkerPosition(Job.TransferBatch,bag));
        RampCrewPose WalkHold(float progress,int bag,bool returning)
        {
            int worker=bag%HoldWorkers;
            var start = HoldReadyPose(worker).position; var corner = HoldAisle(bag)-Side*(.7f*worker); var end = Job.Packing.WorkerPosition(Job.TransferBatch,bag);
            float a = Vector3.Distance(start,corner), length = a+Vector3.Distance(corner,end);
            float distance = Mathf.Clamp01(returning ? 1-progress : progress)*length;
            var pose = HoldReadyPose(worker);
            pose.position = distance < a ? Vector3.Lerp(start,corner,distance/Mathf.Max(.001f,a)) :
                Vector3.Lerp(corner,end,(distance-a)/Mathf.Max(.001f,length-a));
            var direction = distance < a ? corner-start : end-corner;
            var targetFacing = ReadyHold().rotation;
            var stockDirection = Flat(Job.Packing.BagPosition(Job.TransferBatch,bag)-end);
            var workFacing = stockDirection.sqrMagnitude > .001f ? Quaternion.LookRotation(stockDirection.normalized) : targetFacing;
            if (direction.sqrMagnitude > .001f)
            {
                pose.rotation = Quaternion.Slerp(returning ? workFacing : targetFacing,
                    Quaternion.LookRotation(direction.normalized*(returning ? -1 : 1)),Ease(progress/.15f));
                pose.rotation = Quaternion.Slerp(pose.rotation,returning ? targetFacing : workFacing,Ease((progress-.85f)/.15f));
            }
            pose.stride = .35f; pose.phase = progress*length*2; return pose;
        }
        Quaternion HoldRotation(float elapsed,bool load,RampCrewPose pose,int bagIndex)
        {
            var stored = Job.Packing.BagRotation(Job.TransferBatch,bagIndex);
            float pickAt = load ? 0 : HoldWalk;
            if (elapsed < pickAt+HoldPick) return stored;
            if (elapsed < pickAt+HoldPick+HoldLift) return Quaternion.Slerp(stored,pose.rotation,Ease((elapsed-pickAt-HoldPick)/HoldLift));
            float placeAt = pickAt+HoldPick+HoldLift+HoldWalk;
            return Quaternion.Slerp(pose.rotation,stored,Ease((elapsed-placeAt)/HoldStow));
        }
        void HoldCycleAt(float e,bool load,int bagIndex,out RampCrewPose pose,out Vector3 bag)
        {
            float elapsed = e;
            var stock = Job.Packing.BagPosition(Job.TransferBatch,bagIndex);
            var target = Job.Packing.WorkerPosition(Job.TransferBatch,bagIndex);
            float walk = HoldWalk;
            int worker=bagIndex%HoldWorkers;
            var working = HoldReadyPose(worker); working.position = target;
            var toward = Flat(stock-target); if (toward.sqrMagnitude > .001f) working.rotation = Quaternion.LookRotation(toward.normalized);
            pose = HoldReadyPose(worker); bag = load ? HoldPickup : stock;
            Vector3 Carry(RampCrewPose p) => p.position+p.rotation*new Vector3(0,.25f,.43f);
            if (!load && e < walk) { pose = WalkHold(e/Mathf.Max(.001f,walk),bagIndex,false); return; }
            if (!load) e -= walk;
            if (!load) pose = working;
            if (e < HoldPick)
            {
                Hands(ref pose,bag,HoldRotation(elapsed,load,pose,bagIndex)); float reach = Ease(e/HoldPick);
                pose.leftHand = Vector3.Lerp(new Vector3(-.245f,pose.hipHeight-.17f,.025f),pose.leftHand,reach);
                pose.rightHand = Vector3.Lerp(new Vector3(.245f,pose.hipHeight-.17f,.025f),pose.rightHand,reach); return;
            }
            e -= HoldPick;
            if (e < HoldLift) bag = Vector3.Lerp(load ? HoldPickup : stock,Carry(pose),Ease(e/HoldLift));
            else if ((e -= HoldLift) < walk)
            { pose = WalkHold(e/Mathf.Max(.001f,walk),bagIndex,!load); bag = Carry(pose); }
            else if ((e -= walk) < HoldStow)
            {
                pose = load ? working : HoldReadyPose(worker); var to = load ? stock : HoldPickup;
                bag = Vector3.Lerp(Carry(pose),to,Ease(e/HoldStow));
            }
            else
            {
                e -= HoldStow; bag = load ? stock : HoldPickup;
                if (load && e < walk) pose = WalkHold(e/Mathf.Max(.001f,walk),bagIndex,true);
                else pose = HoldReadyPose(worker);
                return;
            }
            Hands(ref pose,bag,HoldRotation(elapsed,load,pose,bagIndex));
        }

        public struct BagPose { public Vector3 bottom; public Quaternion rotation; public int holder,worker; }
        public BagPose BagAt(float elapsed, bool load, int bagIndex = 0)
        {
            var bag = new BagPose { rotation = Quaternion.LookRotation(Plan.inward), holder = -1 };
            float holdOut = HoldOutbound;
            if (load && elapsed < GroundOutbound)
            { bag.worker=bagIndex%GroundWorkers; GroundCycleAt(elapsed,true,out var p,out bag.bottom,bag.worker); bag.rotation = p.rotation; if (elapsed >= Pick) bag.holder = 0; return bag; }
            if (!load && elapsed < holdOut)
            { bag.worker=bagIndex%HoldWorkers; HoldCycleAt(elapsed,false,bagIndex,out var p,out bag.bottom); bag.rotation = HoldRotation(elapsed,false,p,bagIndex); if (elapsed >= HoldWalk+HoldPick) bag.holder = 1; return bag; }
            elapsed -= load ? GroundOutbound : holdOut;
            if (elapsed < ConveyorSeconds)
            {
                float distance = (load ? elapsed : ConveyorSeconds-elapsed) * BaggageLoadPath.BeltSpeed;
                float length = Vector3.Distance(Belt,Front);
                bag.bottom = distance < length ? Vector3.Lerp(Belt,Front,distance/length) :
                    Vector3.Lerp(Front,HoldPickup,(distance-length)/Mathf.Max(.01f,Vector3.Distance(Front,HoldPickup)));
                return bag;
            }
            elapsed -= ConveyorSeconds;
            if (load) { bag.worker=bagIndex%HoldWorkers; HoldCycleAt(elapsed,true,bagIndex,out var p,out bag.bottom); bag.rotation = HoldRotation(elapsed,true,p,bagIndex); if (elapsed >= HoldPick && elapsed < HoldPick+HoldLift+HoldWalk+HoldStow) bag.holder = 1; }
            else { bag.worker=bagIndex%GroundWorkers; GroundCycleAt(elapsed,false,out var p,out bag.bottom,bag.worker); bag.rotation = p.rotation; if (elapsed >= Pick && elapsed < GroundOutbound) bag.holder = 0; }
            return bag;
        }
    }
}
