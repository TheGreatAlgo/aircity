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

        public readonly FlightKind kind;
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
            Phase = kind == FlightKind.Departure ? FlightPhase.Ready : FlightPhase.Approach;
        }

        public string Name => aircraft != null ? aircraft.name : "aircraft";
        public bool Finished => Phase == FlightPhase.Parked || Phase == FlightPhase.Gone || aircraft == null;
        public bool OnGround => Phase != FlightPhase.Approach && Phase != FlightPhase.GoAround && Phase != FlightPhase.Climb &&
                                Phase != FlightPhase.Gone && !(Phase == FlightPhase.TakeoffRoll && Height > 0f);
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
                if (AwaitingPush || Phase == FlightPhase.GoAround || (Phase == FlightPhase.Approach && !LandingCleared))
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
                    FlightPhase.HandPush => "being pushed back by hand",
                    FlightPhase.StartingEngines => "starting engines",
                    FlightPhase.Taxi when kind == FlightKind.Departure => $"taxiing to runway {rwy}{wait}",
                    FlightPhase.Taxi => $"taxiing to {Route.Destination}{wait}",
                    FlightPhase.LinedUp => $"lined up on {rwy}",
                    FlightPhase.TakeoffRoll => Height > 0f ? $"lifting off {rwy}" : $"taking off from {rwy}",
                    FlightPhase.Climb => "climbing out",
                    FlightPhase.Approach => $"on final for {rwy}, {ToTouchdown / 1000f:0.0} km" +
                                            (LandingCleared ? ", cleared to land" : landingProblem != null ? $", not cleared yet: {landingProblem}" : ""),
                    FlightPhase.GoAround => $"going around{wait}",
                    FlightPhase.Landing => $"landing on {rwy}",
                    FlightPhase.Parked => Route != null && Route.stand != null ? $"parked on {Route.stand.stand.Name}" : "parked",
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
                case FlightPhase.Ready: StepReady(); break;
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
            }
            if (Waiting != null) WaitedFor += dt;
            Lights();
        }

        void StepReady()
        {
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
                    handPath = PushBackPath.Plan(traffic.Airport.Taxiways, type, Position, Facing, start.pushOtherWay, out string why);
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
            handAlong = Mathf.Min(handAlong + 0.7f * dt, handPath.Length);
            handPath.Sample(handAlong, out var p, out var heading, out var wheel, out _);
            Place(p, heading, 0f);
            Drive(handAlong < handPath.Length ? -0.7f : 0f, -Vector2.SignedAngle(heading, wheel));
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
            var holds = Route.holds;
            for (int i = 0; i < holds.Count; i++)
            {
                var h = holds[i];
                if (h.cleared) continue;
                float ask = Speed * Speed / (2f * TaxiBraking) + 40f;
                if (Along >= h.at - ask && traffic.TryClear(this, i)) continue;
                stopAt = h.at;
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
            // settle onto the centreline over the first metres of the roll
            var gear0 = Route.path.GearEnd;
            var onLine = Geo.ClosestOnSegment(gear0, Geo.XZ(r.start), Geo.XZ(r.end), out _);
            float blend = Mathf.SmoothStep(0f, 1f, rollS / 60f);
            var p = Vector2.Lerp(gear0, onLine, blend) + T * rollS;
            var f = Vector2.Lerp(Route.path.EndHeading, T, Mathf.SmoothStep(0f, 1f, rollS / 40f)).normalized;
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
            if (Height < 300f) return;
            circling = true;
            timer = CircuitTime;
            aircraft.Highlight = AircraftHighlight.None;
            aircraft.gameObject.SetActive(false);
        }

        /// <summary>Flying round to come back onto final after a go-around (game seconds, out of sight).</summary>
        const float CircuitTime = 90f;

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
            var rotation = Quaternion.LookRotation(t.TransformDirection(Geo.X0Z(facing)), Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
            aircraft.transform.SetPositionAndRotation(t.TransformPoint(Geo.X0Z(origin, height)), rotation);
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

        /// <summary>Beacon from push-back to parking, taxi lights taxiing, landing lights and strobes on the runway
        /// and in the air; engines from engine start.</summary>
        void Lights()
        {
            var l = aircraft.GetComponent<AircraftLights>();
            bool engines = Phase >= FlightPhase.StartingEngines && Phase != FlightPhase.Parked;
            bool runway = Phase == FlightPhase.LinedUp || Phase == FlightPhase.TakeoffRoll || Phase == FlightPhase.Climb ||
                          Phase == FlightPhase.Approach || Phase == FlightPhase.GoAround ||
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
        }

        public override string ToString() => $"{Name}: {Status}";
    }
}
