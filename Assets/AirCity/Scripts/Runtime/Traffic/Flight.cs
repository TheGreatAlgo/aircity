using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Traffic
{
    public enum FlightKind
    {
        Departure = 0,
        Arrival = 1,
        /// <summary>A taxi transfer between a hangar and an aircraft parking stand.</summary>
        GroundTransfer = 2,
    }

    public enum FlightPhase
    {
        /// <summary>Departures: waiting for its way off the stand to be clear.</summary>
        Ready = 0,
        /// <summary>A tug is coming or pushing it back.</summary>
        PushBack = 1,
        /// <summary>Light aircraft: pushed back by hand.</summary>
        HandPush = 2,
        StartingEngines = 3,
        Taxi = 4,
        LinedUp = 5,
        TakeoffRoll = 6,
        Climb = 7,
        /// <summary>Arrivals: on final.</summary>
        Approach = 8,
        GoAround = 9,
        /// <summary>The landing roll, then taxiing in (Taxi).</summary>
        Landing = 10,
        Parked = 11,
        /// <summary>Climbed away out of sight.</summary>
        Gone = 12,
        /// <summary>Circuit training: flying the visible pattern round to final (crosswind, downwind, base).</summary>
        Pattern = 13,
        /// <summary>Circuit training: rolling along the runway after touching down, before taking off again.</summary>
        TouchAndGo = 14,
    }

    /// <summary>
    /// One aircraft's movement, from its stand to the sky or from final approach to its stand (AirTraffic.Depart /
    /// Arrive). On the ground it follows its TaxiRoute: at each hold it asks the tower (AirTraffic) to go on and stops
    /// if it may not, and it stops short of any aircraft its outline would touch. In the air it flies the extended
    /// runway centreline: a 3° glide path down to touchdown, or the takeoff roll, rotation and climb. Simulated in game
    /// seconds; it places its aircraft, turns its wheels and sets its lights, engines and flaps.
    /// </summary>
    public sealed class Flight
    {
        /// <summary>Braking taxiing (m/s²), and the hardest it brakes for something it didn't expect.</summary>
        public const float TaxiBraking = 1f, HardBraking = 3f;
        public const float GlideSlope = 3f;
        /// <summary>Asks for clearance to land this long before touchdown; goes around if it hasn't got it by
        /// DecisionTime.</summary>
        public const float LandingCall = 60f, DecisionTime = 12f;

        /// <summary>What the tower takes it for now. Circuit training starts as a departure and is an arrival from
        /// the moment it turns into the pattern.</summary>
        public FlightKind kind { get; internal set; }
        public readonly Aircraft.Aircraft aircraft;
        public readonly AircraftType type;
        readonly AirTraffic traffic;

        public TaxiRoute Route { get; internal set; }
        public FlightPhase Phase { get; internal set; }
        /// <summary>How far the nose wheel has come along the route (metres).</summary>
        public float Along { get; internal set; }
        /// <summary>Ground speed (m/s).</summary>
        public float Speed { get; internal set; }
        /// <summary>Why it's stopped (null while it's going).</summary>
        public string Waiting { get; internal set; }
        /// <summary>Height above the airfield (metres).</summary>
        public float Height { get; private set; }
        /// <summary>Arrivals on final: metres to touchdown.</summary>
        public float ToTouchdown { get; private set; }
        public bool LandingCleared { get; internal set; }
        /// <summary>How long it has waited in all (game seconds), and how many times it went around.</summary>
        public float WaitedFor { get; private set; }
        public int GoArounds { get; private set; }
        /// <summary>This departure has finished treatment and its trucks have returned clear.</summary>
        public bool DeicingCompleted { get; internal set; }

        internal TaxiPlanner.Start start;
        /// <summary>Since when (AirTraffic.Clock) it has waited for traffic the other way, at `queuedAt` metres along
        /// its way (0: to leave its stand), or -1: the first to wait goes first.</summary>
        internal float queuedSince = -1f, queuedAt;
        /// <summary>The flight whose aircraft it last stopped behind (AirTraffic.FreeAhead), and for how long.</summary>
        internal Flight behind;
        float behindFor;
        internal TowJob tow;
        /// <summary>The tug that pushed it back: it waits for it to be off its way before it taxis.</summary>
        VehicleTeam pushTug;
        internal PushBackPath handPath;
        float handAlong, timer, pitch, climbRate, rollS;
        bool rotating, released, circling;
        float replanTimer, lookTimer;
        /// <summary>Arrivals on final: why it isn't cleared to land yet.</summary>
        string landingProblem;

        internal Flight(AirTraffic traffic, FlightKind kind, Aircraft.Aircraft aircraft, TaxiRoute route)
        {
            this.traffic = traffic;
            this.kind = kind;
            this.aircraft = aircraft;
            type = aircraft.type;
            Route = route;
            Phase = kind == FlightKind.Arrival ? FlightPhase.Approach : FlightPhase.Ready;
        }

        public string Name => aircraft != null ? aircraft.name : "aircraft";
        public bool Finished => Phase == FlightPhase.Parked || Phase == FlightPhase.Gone || aircraft == null;
        public bool OnGround => Phase != FlightPhase.Approach && Phase != FlightPhase.GoAround && Phase != FlightPhase.Climb &&
                                Phase != FlightPhase.Pattern && Phase != FlightPhase.Gone && !(Phase == FlightPhase.TakeoffRoll && Height > 0f);
        /// <summary>Departures: off the front of the stand it left (pushed back, its tail past the stand's side; taxiing
        /// out, past the swing out of it). Until then the stand is taken.</summary>
        internal bool ClearOfStand => Route == null || Route.leaving == null ||
                                      (Phase >= FlightPhase.Taxi && (Route.standTo > 0f ? Along >= Route.standTo : TailAlong >= Route.leaving.halfWidth));

        /// <summary>Following its taxi route (not pushing back or in the air).</summary>
        public bool Taxiing => Phase == FlightPhase.Taxi || Phase == FlightPhase.Landing || Phase == FlightPhase.LinedUp;

        /// <summary>Arrivals on final: seconds to touchdown.</summary>
        public float TimeToTouchdown => Phase == FlightPhase.Approach ? ToTouchdown / Mathf.Max(type.ApproachSpeed, 1f) : float.MaxValue;

        float Wheelbase => Mathf.Max(type.Wheelbase, 0.3f);
        /// <summary>From the nose wheel back to the tail (metres).</summary>
        internal float TailBehind => Wheelbase + Mathf.Max(-type.bounds.min.z, 0f);

        /// <summary>The nose wheel's distance along the route of its tail.</summary>
        internal float TailAlong => Along - TailBehind;

        /// <summary>
        /// How far along its route it may go without asking again (the first hold it hasn't been cleared past), or
        /// -1 while it isn't on its route yet (a departure waiting to start, an arrival in the air without clearance).
        /// </summary>
        internal float CommittedTo
        {
            get
            {
                if (Route == null) return -1f;
                if (AwaitingPush || Phase == FlightPhase.GoAround || Phase == FlightPhase.Pattern || (Phase == FlightPhase.Approach && !LandingCleared))
                    return -1f;
                foreach (var h in Route.holds)
                    if (!h.cleared) return h.at;
                return Route.path.Length;
            }
        }

        public string Status
        {
            get
            {
                string rwy = Route != null ? Route.RunwayName : "";
                string wait = Waiting != null ? ": " + Waiting : "";
                return Phase switch
                {
                    FlightPhase.Ready => "ready to leave" + (Waiting != null ? ", " + Waiting : ""),
                    FlightPhase.PushBack when tow != null && tow.HoldPush && tow.ReadyToPush =>
                        "ready to push back" + (Waiting != null ? ", " + Waiting : ""),
                    FlightPhase.PushBack => tow != null ? tow.Status : "pushing back",
                    FlightPhase.HandPush => Route.leavingHangar != null && !type.Light ? "hangar pushback" : "being pushed back by hand",
                    FlightPhase.StartingEngines => "starting engines",
                    FlightPhase.Taxi when kind == FlightKind.Departure => $"taxiing to runway {rwy}{wait}",
                    FlightPhase.Taxi => $"taxiing to {Route.Destination}{wait}",
                    FlightPhase.LinedUp => $"lined up on {rwy}",
                    FlightPhase.TakeoffRoll => Height > 0f ? $"lifting off {rwy}" : $"taking off from {rwy}",
                    FlightPhase.Climb => "climbing out",
                    FlightPhase.Approach => $"on final for {rwy}, {ToTouchdown / 1000f:0.0} km" +
                                            (LandingCleared ? ", cleared to land" : landingProblem != null ? $", not cleared yet: {landingProblem}" : ""),
                    FlightPhase.GoAround => $"going around{wait}",
                    FlightPhase.Pattern => $"in the circuit for {rwy}, {LegName}{wait}",
                    FlightPhase.TouchAndGo => $"touch-and-go on {rwy}",
                    FlightPhase.Landing => $"landing on {rwy}",
                    FlightPhase.Parked => Route != null && Route.hangar != null ? $"stored in {Route.hangar.name}" : Route != null && Route.stand != null ? $"parked on {Route.stand.stand.Name}" : "parked",
                    _ => "gone",
                };
            }
        }

        // ------------------------------------------------------------------ stepping

        internal void Step(float dt)
        {
            if (aircraft == null) return;
            switch (Phase)
            {
                case FlightPhase.Ready: StepReady(dt); break;
                case FlightPhase.PushBack: StepPushBack(); break;
                case FlightPhase.HandPush: StepHandPush(dt); break;
                case FlightPhase.StartingEngines:
                    timer -= dt;
                    if (timer > 0f) break;
                    if (!TugClear() && timer > -TugWait)
                    {
                        Waiting = "waiting for the tug to clear";
                        break;
                    }
                    Waiting = null;
                    pushTug = null;
                    Phase = FlightPhase.Taxi;
                    break;
                case FlightPhase.Taxi:
                case FlightPhase.Landing:
                    StepTaxi(dt);
                    break;
                case FlightPhase.LinedUp:
                    timer -= dt;
                    Speed = 0f;
                    PlaceOnRoute();
                    if (timer <= 0f) StartTakeoff();
                    break;
                case FlightPhase.TakeoffRoll:
                case FlightPhase.Climb:
                    StepTakeoff(dt);
                    break;
                case FlightPhase.Approach: StepApproach(dt); break;
                case FlightPhase.GoAround: StepGoAround(dt); break;
                case FlightPhase.Pattern: StepPattern(dt); break;
                case FlightPhase.TouchAndGo: StepTouchAndGo(dt); break;
            }
            if (Phase != FlightPhase.Pattern && bank != 0f) bank = Mathf.MoveTowards(bank, 0f, 14f * dt);
            if (Waiting != null) WaitedFor += dt;
            Lights();
        }

        void StepReady(float dt)
        {
            var airstair = aircraft.Airstair;
            if (airstair != null && !airstair.StowForDeparture(dt))
            {
                Waiting = "stowing the airstair";
                return;
            }
            string baggage = traffic.Airport.Baggage.DepartureBlocker(aircraft);
            if (baggage != null) { Waiting = baggage; return; }
            if (!traffic.RouteValid(this))
            {
                replanTimer -= 0.25f;
                if (replanTimer > 0f) return;
                replanTimer = 5f;
                if (!traffic.ReplanDeparture(this, out string why))
                {
                    Waiting = why;
                    return;
                }
            }
            // the jet bridges pull back first (they start as soon as the flight exists)
            if (!traffic.Airport.Bridges.Clear(aircraft))
            {
                Waiting = "waiting for the jet bridges to pull back";
                return;
            }
            // a tug is sent for at once: the push-back itself is cleared once it has the nose wheel up
            bool tug = start != null && start.pushBack && !start.handPush;
            if (!tug)
            {
                string blocker = traffic.StartBlocker(this);
                if (blocker != null)
                {
                    Waiting = blocker;
                    return;
                }
            }
            if (start != null && start.pushBack)
            {
                if (start.handPush)
                {
                    string why = null;
                    handPath = Route.leavingHangar != null ? start.push : PushBackPath.Plan(traffic.Airport.Taxiways, type, Position, Facing, start.pushOtherWay, out why);
                    if (handPath == null)
                    {
                        Waiting = why;
                        return;
                    }
                    handAlong = 0f;
                    Phase = FlightPhase.HandPush;
                }
                else
                {
                    var job = traffic.Airport.Maintenance.PushBack(aircraft, out string why, start.pushOtherWay);
                    if (job == null)
                    {
                        Waiting = why;
                        return;
                    }
                    job.HoldPush = true;
                    tow = job;
                    Phase = FlightPhase.PushBack;
                }
            }
            else
            {
                timer = EngineStart;
                Phase = FlightPhase.StartingEngines;
            }
            Waiting = null;
        }

        float EngineStart => type.maxTakeoffMass < 5700f ? 6f : 20f;

        void StepPushBack()
        {
            if (tow == null) return;
            if (tow.HoldPush)
            {
                if (!tow.ReadyToPush) return;
                if (!traffic.RouteValid(this))
                {
                    Waiting = "its way is closed";
                    return;
                }
                string blocker = traffic.StartBlocker(this);
                if (blocker != null)
                {
                    Waiting = blocker;
                    return;
                }
                Waiting = null;
                tow.HoldPush = false;
                return;
            }
            if (tow.Tug != null) pushTug = tow.Tug;
            if (tow.State < TowState.Done) return;
            // the tug has let go: wait for it to drive out from under the nose
            if (tow.Tug != null) return;
            bool pushed = Vector2.Distance(Position, Route.path.GearAt(0)) < 3f;
            tow = null;
            if (!pushed)
            {
                // the tug never got to it: ask again
                Phase = FlightPhase.Ready;
                Waiting = "the tug couldn't get to it";
                return;
            }
            timer = EngineStart * 0.5f;
            Phase = FlightPhase.StartingEngines;
        }

        /// <summary>Stuck this long behind an aircraft that's holding, it gives back the clearances it hasn't reached
        /// (game seconds).</summary>
        const float GiveBackAfter = 20f;

        /// <summary>It goes anyway after waiting this long for its tug (game seconds).</summary>
        const float TugWait = 120f;

        /// <summary>The tug that pushed it back, until it's off its way.</summary>
        internal VehicleTeam PushTug => pushTug;

        /// <summary>Being pushed back now (by a tug, or by hand): the way it's pushed, and the tug.</summary>
        internal PushBackPath Pushing(out VehicleTeam tug)
        {
            tug = null;
            if (Phase == FlightPhase.HandPush) return handPath;
            if (Phase != FlightPhase.PushBack || tow == null || tow.State != TowState.Pushing) return null;
            tug = tow.Tug;
            return tow.path;
        }

        /// <summary>Starting engines, waiting for this tug (that pushed it back) to drive off its way.</summary>
        internal bool WaitsForTug(VehicleTeam team) => Phase == FlightPhase.StartingEngines && pushTug == team && team != null;

        /// <summary>The tug that pushed it back is off the first 300 m of its way (by 25 m), or home.</summary>
        bool TugClear()
        {
            if (pushTug == null || !pushTug.Active) return true;
            var p = pushTug.LeadPosition;
            float end = Mathf.Min(Route.path.Length, 300f);
            for (float d = 0f; d <= end; d += 5f)
            {
                Route.path.Pose(d, out var at, out var f, out _, out _);
                if (Vector2.Distance(at + f * Wheelbase, p) < 25f) return false;
            }
            return true;
        }

        /// <summary>A tug has it but its push-back isn't cleared yet (it commits to nothing until then).</summary>
        internal bool AwaitingPush => Phase == FlightPhase.Ready || (Phase == FlightPhase.PushBack && tow != null && tow.HoldPush);

        /// <summary>Awaiting its push-back with all it needs but the way: the tug on (or pushed by hand, or taxiing out).</summary>
        internal bool ReadyToStart => Phase == FlightPhase.Ready ? start == null || !start.pushBack || start.handPush
            : Phase == FlightPhase.PushBack && tow != null && tow.HoldPush && tow.ReadyToPush;

        void StepHandPush(float dt)
        {
            float speed = Route.leavingHangar != null && !type.Light ? 1.4f : .7f;
            handAlong = Mathf.Min(handAlong + speed * dt, handPath.Length);
            handPath.Sample(handAlong, out var p, out var heading, out var wheel, out _);
            Place(p, heading, 0f);
            Drive(handAlong < handPath.Length ? -speed : 0f, -Vector2.SignedAngle(heading, wheel));
            if (handAlong >= handPath.Length)
            {
                timer = EngineStart;
                Phase = FlightPhase.StartingEngines;
            }
        }

        void StepTaxi(float dt)
        {
            var path = Route.path;
            if (!traffic.RouteValid(this))
            {
                Replan(dt);
                return;
            }
            Waiting = null;
            // the first hold it isn't cleared past: ask in time to go on without stopping
            float stopAt = path.Length;
            DeicingStop deicing = null;
            foreach (var stop in Route.deicingStops)
            {
                if (DeicingCompleted) break;
                var job = traffic.Airport.Deicing.JobFor(stop.facility);
                if (job != null && job.aircraft == aircraft && job.Completed) { DeicingCompleted = true; break; }
                if (stop.at < Along - 1f) continue;
                deicing = stop;
                stopAt = Mathf.Max(Along, stop.at);
                Waiting = job != null && job.aircraft == aircraft ? job.Waiting : "stopping for de-icing";
                break;
            }
            var marshaller = kind == FlightKind.Arrival ? traffic.Airport.Marshalling.For(this) : null;
            if (marshaller != null && !marshaller.Ready)
            { stopAt = Mathf.Min(stopAt, Mathf.Max(Along,path.Length-25f)); Waiting = "waiting for the marshaller to take position"; }
            var holds = Route.holds;
            for (int i = 0; i < holds.Count; i++)
            {
                var h = holds[i];
                if (h.cleared) continue;
                // A treatment may take minutes: don't reserve the next junction or runway while the trucks work.
                if (deicing != null && h.at > stopAt + 0.05f) break;
                float ask = Speed * Speed / (2f * TaxiBraking) + 40f;
                if (Along >= h.at - ask && traffic.TryClear(this, i)) continue;
                stopAt = Mathf.Min(stopAt, h.at);
                if (Along >= h.at - ask) Waiting = h.waiting;
                break;
            }
            foreach (var h in holds)
                if (h.cleared && !h.released && Along >= h.release) traffic.Release(this, h);
            if (Route.runwayFree > 0f && !released && Along >= Route.runwayFree)
            {
                released = true;
                traffic.ReleaseRunway(this, Route.runway);
            }

            float limit = path.SpeedLimit(Along);
            if (Route.hangar!=null && path.Length-Along<30f) limit=Mathf.Min(limit,1.2f);
            if (marshaller != null) limit = Mathf.Min(limit,marshaller.SpeedLimit);
            limit = Mathf.Min(limit, Mathf.Sqrt(2f * TaxiBraking * Mathf.Max(0f, stopAt - Along)));
            float free = traffic.FreeAhead(this, Along, Speed, out var who);
            if (free < float.MaxValue)
            {
                limit = Mathf.Min(limit, Mathf.Sqrt(2f * TaxiBraking * Mathf.Max(0f, free - Along - 3f)));
                if (Speed < 0.3f && free - Along < 12f) Waiting ??= $"behind {who}";
            }
            // stuck behind one that's holding (it may be waiting for what this one was cleared for)
            behindFor = Speed < 0.3f && behind != null && behind.Waiting != null && !behind.Finished ? behindFor + dt : 0f;
            if (behindFor > GiveBackAfter)
            {
                behindFor = 0f;
                traffic.GiveBack(this);
            }
            bool rolling = Phase == FlightPhase.Landing && (Route.runwayFree < 0f || Along < Route.runwayFree);
            if (rolling && Speed > type.TaxiSpeed * 1.5f) limit = Mathf.Min(limit, Speed - start.landingDecel * dt);
            else if (Phase == FlightPhase.Landing) Phase = FlightPhase.Taxi;
            float accel = type.maxTakeoffMass < 5700f ? 0.9f : 0.6f;
            Speed = Speed < limit ? Mathf.Min(limit, Speed + accel * dt) : Mathf.Max(limit, Speed - HardBraking * dt);
            if (Speed < 0.02f) Speed = 0f;
            float next = Mathf.Min(Along + Speed * dt, stopAt);
            if (free < float.MaxValue) next = Mathf.Min(next, free - 1f);
            Along = Mathf.Max(Along, next);
            if (Along >= stopAt - 0.05f && stopAt < path.Length) Speed = Mathf.Min(Speed, 0.5f);
            traffic.Passed(this);
            PlaceOnRoute();
            if (deicing != null && Mathf.Abs(Along - deicing.at) <= 0.05f)
            {
                Along = deicing.at;
                Speed = 0f;
                PlaceOnRoute();
                return;
            }
            pitch = Mathf.MoveTowards(pitch, 0f, 1.5f * dt);
            if (Along < path.Length - 0.05f) return;
            Speed = 0f;
            PlaceOnRoute();
            if (kind == FlightKind.Departure)
            {
                timer = 3f;
                Phase = FlightPhase.LinedUp;
            }
            else
            {
                Phase = FlightPhase.Parked;
                Drive(0f, 0f);
                traffic.Finish(this);
            }
        }

        /// <summary>Its way has gone (a taxiway removed): stop, and look for another every few seconds.</summary>
        void Replan(float dt)
        {
            Speed = Mathf.Max(0f, Speed - HardBraking * dt);
            Along = Mathf.Min(Along + Speed * dt, Route.path.Length);
            PlaceOnRoute();
            replanTimer -= dt;
            if (replanTimer > 0f) return;
            replanTimer = 5f;
            if (traffic.Replan(this, out string problem))
            {
                Waiting = null;
                released = false;
                return;
            }
            Waiting = problem;
        }

        // ------------------------------------------------------------------ takeoff

        float LengthFactor(bool takeoff)
        {
            var r = Route.runway;
            var f = RunwayRules.LengthFactors(r.surface, traffic.Airport.Condition, r.snow, r.grass) ?? Vector2.one;
            return takeoff ? f.x : f.y;
        }

        void StartTakeoff()
        {
            Phase = FlightPhase.TakeoffRoll;
            rollS = 0f;
            Speed = 0f;
            rotating = false;
            touched = false;
            Route.runway.AddMovement(type);
        }

        Vector2 RunwayAxis => Route.fromStart ? Route.runway.Axis2D : -Route.runway.Axis2D;

        void StepTakeoff(float dt)
        {
            var r = Route.runway;
            var T = RunwayAxis;
            float vr = type.RotateSpeed;
            // the ground roll is about 60% of the takeoff distance (to clear 15 m)
            float a = vr * vr / (2f * 0.6f * type.takeoffDistance * LengthFactor(true));
            float rotatePitch = type.maxTakeoffMass < 5700f ? 8f : 8.5f, liftPitch = rotatePitch * 0.7f;
            if (Phase == FlightPhase.TakeoffRoll)
            {
                Speed += (Speed < vr ? a : a * 0.6f) * dt;
                if (Speed >= vr) rotating = true;
                if (rotating) pitch = Mathf.MoveTowards(pitch, rotatePitch, 3f * dt);
                if (pitch >= liftPitch)
                {
                    Phase = FlightPhase.Climb;
                    climbRate = 0f;
                }
            }
            else
            {
                climbRate = Mathf.MoveTowards(climbRate, type.ClimbRate, type.ClimbRate / 5f * dt);
                Speed = Mathf.MoveTowards(Speed, type.ClimbSpeed, 1.5f * dt);
                Height += climbRate * dt;
                float target = Mathf.Clamp(Mathf.Atan2(climbRate, Speed) * Mathf.Rad2Deg + 4f, liftPitch, 16f);
                pitch = Mathf.MoveTowards(pitch, target, 2f * dt);
            }
            rollS += Speed * dt;
            // settle onto the centreline over the first metres of the roll (a touch-and-go is on it already)
            var gear0 = touched ? rollFrom : Route.path.GearEnd;
            var onLine = Geo.ClosestOnSegment(gear0, Geo.XZ(r.start), Geo.XZ(r.end), out _);
            float blend = Mathf.SmoothStep(0f, 1f, rollS / 60f);
            var p = Vector2.Lerp(gear0, onLine, blend) + T * rollS;
            var f = touched ? T : Vector2.Lerp(Route.path.EndHeading, T, Mathf.SmoothStep(0f, 1f, rollS / 40f)).normalized;
            Place(p, f, Height);
            Drive(Height > 0.2f ? 0f : Speed, 0f);
            Surfaces(Height > 0.2f ? 0f : 0.35f, rotating && Phase == FlightPhase.TakeoffRoll ? 0.6f : 0.15f);
            if (!released)
            {
                r.Locate(Geo.X0Z(p), out float s, out _);
                bool past = Route.fromStart ? s > r.Length : s < 0f;
                if (Height > 60f || (Height > 0f && past))
                {
                    released = true;
                    traffic.ReleaseRunway(this, r);
                }
            }
            // circuit training turns crosswind into the pattern instead of flying away
            if (PatternWork && Phase == FlightPhase.Climb && Height >= CrosswindHeight)
            {
                EnterPattern(p);
                return;
            }
            if (Height > 450f || p.magnitude > 9000f) traffic.Gone(this);
        }

        // ------------------------------------------------------------------ approach and landing

        /// <summary>Put an arrival on final, `distance` metres before touchdown.</summary>
        internal void StartApproach(float distance)
        {
            Phase = FlightPhase.Approach;
            released = false;
            lookTimer = 0f;
            landingProblem = null;
            ToTouchdown = distance;
            Speed = type.ApproachSpeed;
            LandingCleared = false;
            Along = 0f;
            PlaceOnFinal();
        }

        Vector2 TouchdownPoint
        {
            get
            {
                var r = Route.runway;
                var thr = Geo.XZ(Route.fromStart ? r.start : r.end);
                return thr + RunwayAxis * Route.touchdownS;
            }
        }

        /// <summary>Height on the glide path, with a flare over the last 15 metres.</summary>
        static float GlideHeight(float toTouchdown)
        {
            float tan = Mathf.Tan(GlideSlope * Mathf.Deg2Rad);
            float flare = 15f / tan;
            return toTouchdown >= flare ? toTouchdown * tan : 15f * Mathf.Pow(Mathf.Max(toTouchdown, 0f) / flare, 1.6f);
        }

        void StepApproach(float dt)
        {
            float vt = type.TouchdownSpeed;
            Speed = ToTouchdown > 600f ? type.ApproachSpeed : Mathf.Lerp(vt, type.ApproachSpeed, ToTouchdown / 600f);
            ToTouchdown -= Speed * dt;
            if (!LandingCleared && TimeToTouchdown <= LandingCall)
            {
                // its way off the runway blocked: another exit or stand, every few seconds
                lookTimer -= dt;
                bool look = lookTimer <= 0f;
                if (look) lookTimer = 5f;
                if (traffic.TryClearLanding(this, look, out landingProblem))
                {
                    LandingCleared = true;
                    landingProblem = null;
                }
                else if (TimeToTouchdown <= DecisionTime)
                {
                    GoAround();
                    return;
                }
            }
            if (ToTouchdown <= 0f && Route.touchAndGo)
            {
                StartTouchAndGo();
                return;
            }
            if (ToTouchdown <= 0f)
            {
                // touchdown: on the runway, rolling along the route
                Height = 0f;
                Phase = FlightPhase.Landing;
                Along = 0f;
                Speed = vt;
                Route.runway.AddMovement(type);
                PlaceOnRoute();
                return;
            }
            PlaceOnFinal();
        }

        void PlaceOnFinal()
        {
            Height = GlideHeight(ToTouchdown);
            float flare = 15f / Mathf.Tan(GlideSlope * Mathf.Deg2Rad);
            float approachPitch = type.maxTakeoffMass < 5700f ? 3f : 2.5f;
            pitch = ToTouchdown >= flare ? approachPitch : Mathf.Lerp(approachPitch + 3f, approachPitch, ToTouchdown / flare);
            Place(TouchdownPoint - RunwayAxis * ToTouchdown, RunwayAxis, Height);
            Drive(0f, 0f);
            Surfaces(1f, ToTouchdown < flare ? 0.3f : 0f);
        }

        void GoAround()
        {
            traffic.CancelLanding(this);
            LandingCleared = false;
            Phase = FlightPhase.GoAround;
            GoArounds++;
            climbRate = 0f;
            Waiting = landingProblem ?? "the runway isn't clear";
            landingProblem = null;
        }

        void StepGoAround(float dt)
        {
            // round the circuit out of sight, then back onto final behind the others
            if (circling)
            {
                timer -= dt;
                if (timer > 0f) return;
                circling = false;
                aircraft.gameObject.SetActive(true);
                Waiting = null;
                if (!traffic.Requeue(this)) traffic.Gone(this);
                return;
            }
            climbRate = Mathf.MoveTowards(climbRate, type.ClimbRate, type.ClimbRate / 4f * dt);
            Speed = Mathf.MoveTowards(Speed, type.ClimbSpeed, 1.5f * dt);
            Height += climbRate * dt;
            ToTouchdown -= Speed * dt;
            pitch = Mathf.MoveTowards(pitch, Mathf.Atan2(climbRate, Speed) * Mathf.Rad2Deg + 4f, 2f * dt);
            Place(TouchdownPoint - RunwayAxis * ToTouchdown, RunwayAxis, Height);
            Surfaces(0.5f, 0.2f);
            // circuit training goes round the visible pattern again
            if (PatternWork && Height >= CrosswindHeight)
            {
                EnterPattern(TouchdownPoint - RunwayAxis * ToTouchdown);
                return;
            }
            if (Height < 300f) return;
            circling = true;
            timer = CircuitTime;
            aircraft.Highlight = AircraftHighlight.None;
            aircraft.gameObject.SetActive(false);
        }

        /// <summary>Flying round to come back onto final after a go-around (game seconds, out of sight).</summary>
        const float CircuitTime = 90f;

        // ------------------------------------------------------------------ circuits

        /// <summary>The circuit: its height above the airfield, the height at which a climb-out turns crosswind, and
        /// the bank in its turns (degrees).</summary>
        public const float PatternHeight = 240f, CrosswindHeight = 110f, PatternBank = 27f;
        /// <summary>A circuit extends its downwind leg behind other traffic this far at most (metres).</summary>
        const float MaxExtension = 3000f;

        /// <summary>Circuit training (AirTraffic.DepartForCircuits): after taking off it flies the pattern back to
        /// the runway it left instead of flying away.</summary>
        public bool PatternWork { get; internal set; }
        /// <summary>Touch-and-goes still to fly before the landing to a full stop.</summary>
        public int CircuitsLeft { get; internal set; }
        /// <summary>Touch-and-goes flown, and how often it has had to plan a way down again.</summary>
        public int TouchAndGoes { get; private set; }
        internal int patternTries;
        /// <summary>The leg flown (0 the turn crosswind, 1 crosswind, 2 the turn downwind, 3 downwind, 4 the turn onto
        /// base, 5 base, 6 the turn onto final), how far along it, and where and which way it started.</summary>
        int leg;
        float legAlong, downwind, abeam, extended, finalFrom, patternRadius, patternCross, bank, touchTimer;
        Vector2 legFrom, legDir, rollFrom;
        bool committed, touched, shadowLooked;
        Renderer contactShadow;

        string LegName => leg <= 1 ? "crosswind" : leg <= 3 ? "downwind" : leg <= 5 ? "base" : "turning final";

        /// <summary>In the circuit: turned (or turning) onto base, so its place in the landing order is fixed.</summary>
        internal bool CommittedToLand => Phase == FlightPhase.Approach || (Phase == FlightPhase.Pattern && committed);

        /// <summary>Seconds until it touches down: on final, or in the circuit with the rest of the way round to
        /// fly; float.MaxValue for anything else.</summary>
        internal float DueIn => Phase == FlightPhase.Approach ? TimeToTouchdown
            : Phase == FlightPhase.Pattern ? RemainingPattern / Mathf.Max(Speed, 1f) + (finalFrom + extended) / Mathf.Max(type.ApproachSpeed, 1f)
            : float.MaxValue;

        bool Arc(int l) => l == 0 || l == 2 || l == 4 || l == 6;
        float LegLength(int l) => Arc(l) ? patternRadius * Mathf.PI * 0.5f : l == 3 ? downwind : patternCross;

        float RemainingPattern
        {
            get
            {
                float d = LegLength(leg) - legAlong;
                for (int l = leg + 1; l <= 6; l++) d += LegLength(l);
                return Mathf.Max(d, 0f);
            }
        }

        static Vector2 Rotated(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Where it is on a leg, and the way it points. The circuit is left-hand: every turn is to the left.</summary>
        void LegPose(int l, float along, out Vector2 at, out Vector2 heading)
        {
            if (!Arc(l))
            {
                at = legFrom + legDir * along;
                heading = legDir;
                return;
            }
            var centre = legFrom + Geo.Left(legDir) * patternRadius;
            float angle = along / patternRadius;
            at = centre + Rotated(legFrom - centre, angle);
            heading = Rotated(legDir, angle);
        }

        /// <summary>
        /// Join the circuit from a point on the runway's extended centreline, heading along it (climbing out after a
        /// takeoff, a touch-and-go or a go-around): the tower plans the way down again (another touch-and-go, or the
        /// landing and the way to its stand), and it flies crosswind, downwind and base round to final. With nowhere
        /// to land it leaves the airfield.
        /// </summary>
        void EnterPattern(Vector2 at)
        {
            var axis = RunwayAxis;
            if (!traffic.PlanCircuit(this))
            {
                PatternWork = false;
                traffic.Gone(this);
                return;
            }
            Phase = FlightPhase.Pattern;
            LandingCleared = false;
            touched = false;
            Waiting = landingProblem = null;
            float v = type.ClimbSpeed;
            patternRadius = Mathf.Clamp(v * v / (9.81f * Mathf.Tan(PatternBank * Mathf.Deg2Rad)), 180f, 1500f);
            patternCross = 0.6f * patternRadius;
            finalFrom = Mathf.Max(1500f, type.ApproachSpeed * 50f);
            // downwind runs from abeam this point back past the touchdown point to where base brings it onto final
            abeam = Mathf.Max(Vector2.Dot(at - TouchdownPoint, axis), 0f);
            downwind = abeam + finalFrom;
            extended = 0f;
            committed = false;
            leg = 0;
            legAlong = 0f;
            legFrom = at;
            legDir = axis;
        }

        void StepPattern(float dt)
        {
            Speed = Mathf.MoveTowards(Speed, leg >= 4 ? type.ApproachSpeed : type.ClimbSpeed, 1.2f * dt);
            float d = Speed * dt;
            while (d > 0f)
            {
                float length = LegLength(leg);
                if (leg == 3 && !committed && legAlong + d >= length)
                {
                    // the end of downwind: turn base, or go on behind someone who lands first
                    float turn = (patternRadius * Mathf.PI + patternCross) / Mathf.Max(type.ApproachSpeed, 1f) +
                                 (finalFrom + extended) / Mathf.Max(type.ApproachSpeed, 1f);
                    string first = extended < MaxExtension ? traffic.PatternWait(this, turn) : null;
                    if (first != null)
                    {
                        downwind += d;
                        extended += d;
                        length = downwind;
                        Waiting = $"extending downwind behind {first}";
                    }
                    else
                    {
                        committed = true;
                        Waiting = null;
                    }
                }
                if (legAlong + d < length)
                {
                    legAlong += d;
                    break;
                }
                d -= length - legAlong;
                LegPose(leg, length, out legFrom, out legDir);
                legAlong = 0f;
                leg++;
                if (leg > 6)
                {
                    // rolled out on the centreline: the ordinary final approach from here
                    StartApproach(finalFrom + extended);
                    return;
                }
            }
            // climb to the circuit height, then down from abeam the touchdown point to the glide path at the roll-out
            bool descending = leg > 3 || (leg == 3 && legAlong >= abeam);
            float vertical;
            if (!descending) vertical = Mathf.Min(type.ClimbRate, (PatternHeight - Height) / Mathf.Max(dt, 1e-3f));
            else
            {
                float left = RemainingPattern / Mathf.Max(Speed, 1f);
                vertical = Mathf.Clamp((GlideHeight(finalFrom + extended) - Height) / Mathf.Max(left, dt), -6f, type.ClimbRate);
            }
            climbRate = Mathf.MoveTowards(climbRate, vertical, type.ClimbRate / 3f * dt);
            Height = Mathf.Max(Height + climbRate * dt, 20f);
            pitch = Mathf.MoveTowards(pitch, Mathf.Atan2(climbRate, Mathf.Max(Speed, 1f)) * Mathf.Rad2Deg + 3f, 2f * dt);
            bank = Mathf.MoveTowards(bank, Arc(leg) ? PatternBank : 0f, 14f * dt);
            LegPose(leg, legAlong, out var p, out var facing);
            Place(p, facing, Height);
            Drive(0f, 0f);
            Surfaces(leg >= 4 ? 0.7f : leg == 3 && descending ? 0.35f : 0f, 0f);
        }

        /// <summary>Touched down on a touch-and-go: it rolls a few seconds, flaps coming up, then takes off again.</summary>
        void StartTouchAndGo()
        {
            Height = 0f;
            Phase = FlightPhase.TouchAndGo;
            rollFrom = TouchdownPoint;
            rollS = 0f;
            touched = true;
            touchTimer = 4f;
            Speed = type.TouchdownSpeed;
            rotating = false;
            if (CircuitsLeft > 0) CircuitsLeft--;
            TouchAndGoes++;
            Route.runway.AddMovement(type);
            Place(rollFrom, RunwayAxis, 0f);
        }

        void StepTouchAndGo(float dt)
        {
            touchTimer -= dt;
            Speed = Mathf.MoveTowards(Speed, Mathf.Min(type.TouchdownSpeed, type.RotateSpeed * 0.92f), 1.5f * dt);
            pitch = Mathf.MoveTowards(pitch, 0f, 4f * dt);
            rollS += Speed * dt;
            Place(rollFrom + RunwayAxis * rollS, RunwayAxis, 0f);
            Drive(Speed, 0f);
            Surfaces(Mathf.Clamp01(touchTimer / 4f) * 0.65f + 0.35f, 0f);
            if (touchTimer > 0f) return;
            // power on: the takeoff roll goes on from here (StepTakeoff)
            Phase = FlightPhase.TakeoffRoll;
        }

        // ------------------------------------------------------------------ placing

        Transform Airport => traffic.Airport.transform;

        Vector2 Position => Geo.XZ(Airport.InverseTransformPoint(aircraft.transform.position));
        Vector2 Facing => Geo.XZ(Airport.InverseTransformDirection(aircraft.transform.forward)).normalized;

        void PlaceOnRoute()
        {
            Route.path.Pose(Along, out var origin, out var facing, out float steering, out float ratio);
            Place(origin, facing, 0f);
            Drive(Speed * ratio, steering);
        }

        void Place(Vector2 origin, Vector2 facing, float height)
        {
            var t = Airport;
            var airport=traffic.Airport;
            float support=0f,slope=0f;
            if(airport.HasElevatedPavement)
            {
                if(Route?.runway!=null && (Phase==FlightPhase.Approach || Phase==FlightPhase.GoAround || Phase==FlightPhase.Pattern))
                    support=Mathf.Max(Route.runway.ElevationAt(TouchdownPoint),Route.runway.ElevationAt(origin));
                else if(Route?.runway!=null && (Phase==FlightPhase.TakeoffRoll || Phase==FlightPhase.Climb))
                    support=Route.runway.ElevationAt(origin);
                else support=airport.PavementElevationAt(origin);
                if(Route?.runway!=null && (Phase==FlightPhase.TakeoffRoll || Phase==FlightPhase.Climb))
                    slope=Vector2.Dot(Route.runway.ElevationGradient,facing)*(1f-Mathf.SmoothStep(0f,1f,Mathf.Clamp01(height/30f)));
                else if(height<=.001f)slope=Vector2.Dot(airport.PavementGradientAt(origin),facing);
            }
            HangarSupport(Route?.hangar);
            HangarSupport(Route?.leavingHangar);
            var direction=new Vector3(facing.x,slope,facing.y).normalized;
            // banked into the circuit's turns: a positive bank rolls it left
            var rotation = Quaternion.LookRotation(t.TransformDirection(direction), Vector3.up) * Quaternion.Euler(-pitch, 0f, bank);
            aircraft.transform.SetPositionAndRotation(t.TransformPoint(Geo.X0Z(origin, support+height)), rotation);
            // The contact shadow is a flat part of the model under the gear: off the ground it would hang in the air.
            if (!shadowLooked)
            {
                shadowLooked = true;
                foreach (var r in aircraft.GetComponentsInChildren<Renderer>(true))
                    if (r.name == "GroundShadow") contactShadow = r;
            }
            if (contactShadow != null) contactShadow.enabled = height < 1.5f;

            void HangarSupport(SmallHangar hangar)
            {
                if (hangar == null) return;
                var delta=origin-hangar.BuildingRoot.Door;
                float forward=Vector2.Dot(delta,hangar.Axis);
                var spec = hangar.Spec;
                if (forward < 0f && hangar.Contains(origin,-.1f))
                    support = Mathf.Max(support, SmallHangar.FloorHeight + .015f - type.bounds.min.y);
                else if (forward >= 0f && forward <= spec.MaxSetback + .1f &&
                    Mathf.Abs(Vector2.Dot(delta, Geo.Right(hangar.Axis))) <= spec.ApproachWidth * .5f &&
                    HangarConnection.Approach(hangar, out var throat) && Geo.Inside(throat, origin))
                    support = Mathf.Max(support, HangarConnection.SurfaceHeight(hangar, origin, throat) + .015f - type.bounds.min.y);
            }
        }

        void Drive(float speed, float steeringDegrees)
        {
            var m = aircraft.GetComponent<AircraftGroundMotion>();
            if (m != null) m.Drive(speed, steeringDegrees / Mathf.Max(m.maxSteeringAngle, 1f));
        }

        void Surfaces(float flaps, float elevator)
        {
            var c = aircraft.GetComponent<AircraftControlSurfaces>();
            if (c == null) return;
            c.flaps = flaps;
            c.elevator = elevator;
        }

        /// <summary>Takeoff power with the afterburners lit: from brake release through the climb-out.</summary>
        public bool Reheat => type.HasAfterburner && (Phase == FlightPhase.TakeoffRoll || Phase == FlightPhase.Climb);

        /// <summary>Beacon from push-back to parking, taxi lights taxiing, landing lights and strobes on the runway
        /// and in the air; engines from engine start.</summary>
        void Lights()
        {
            var l = aircraft.GetComponent<AircraftLights>();
            bool engines = Phase >= FlightPhase.StartingEngines && Phase != FlightPhase.Parked;
            bool runway = Phase == FlightPhase.LinedUp || Phase == FlightPhase.TakeoffRoll || Phase == FlightPhase.Climb ||
                          Phase == FlightPhase.Approach || Phase == FlightPhase.GoAround ||
                          Phase == FlightPhase.Pattern || Phase == FlightPhase.TouchAndGo ||
                          (Phase == FlightPhase.Landing && (Route.runwayFree < 0f || Along < Route.runwayFree));
            if (l != null)
            {
                l.navigation = true;
                l.beacon = Phase != FlightPhase.Ready && Phase != FlightPhase.Parked;
                l.strobes = runway;
                l.landing = runway;
                l.taxi = engines && (runway || Phase == FlightPhase.Taxi);
            }
            float power = Phase == FlightPhase.TakeoffRoll || Phase == FlightPhase.Climb || Phase == FlightPhase.GoAround ? 2.2f : 1f;
            foreach (var p in aircraft.GetComponentsInChildren<PropellerSpinner>())
                p.rpm = engines ? (p.name.StartsWith("Fan") ? 1100f : 900f) * power : 0f;
            // afterburners (supersonic types) light at brake release and burn through the climb-out
            var reheat = aircraft.GetComponent<AircraftAfterburner>();
            if (reheat != null) reheat.reheat = Reheat ? 1f : 0f;
        }

        public override string ToString() => $"{Name}: {Status}";
    }
}
