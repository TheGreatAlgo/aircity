using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// The bags the player sees during a bulk hold transfer. Presentation only: it reads the simulation and never writes.
    /// The simulation moves about two bags a second in compressed time; here the visible cart and hold stacks trail it and
    /// catch up by the moments that matter. A bag leaves its stack either in a handler's hands or by quietly settling into
    /// the pile, rides the belt at a human belt speed, and joins the other stack either from a handler's hands or by quietly
    /// rising into a free slot. When the belt cannot deliver the backlog in the time the simulation has left, the excess goes
    /// straight from pile to pile (both settle), spread over the remaining seconds. When a cart or the loader is released,
    /// what shows is exactly what the ledger says.
    /// At a baggage depot the same flow runs between the train and the depot conveyor (cart, conveyor foot by the cart, the
    /// opening in the hall wall): the far end is the hall itself, which takes and gives bags at the simulation's pace.
    /// </summary>
    public sealed class BaggageBagFlow
    {
        /// <summary>Belt speed in game m/s, minimum distance between bags on the belt, settle time and depth.</summary>
        public const float BeltSpeed = 1.45f, Pitch = .62f, Settle = .45f, SettleDepth = .30f;
        /// <summary>Smallest gap between two bags that settle straight from pile to pile.</summary>
        const float DirectGap = .22f;
        /// <summary>A handler this close to the source post already counts as working it.</summary>
        const float ArrivingSeconds = 4f;
        /// <summary>A bag left at the end of the belt for this long is settled without hands.</summary>
        const float WaitLimit = 2.5f;

        public struct CartEntry { public int slot, variant; public float anim; public sbyte motion; public bool reserved; }
        public sealed class Cart { public readonly List<CartEntry> entries = new List<CartEntry>(48); public bool[] occupied; public BaggageStore store; }
        public struct HoldEntry { public BaggageBatch batch; public int index, variant; public float anim; public sbyte motion; public Vector3 bottom; public Quaternion rotation; }
        public struct Token { public float s, anim, waited; public int variant, cart; public sbyte motion; public bool up, waiting; }

        readonly BaggageFleet fleet;
        Cart[] carts = new Cart[0];
        readonly List<HoldEntry> hold = new List<HoldEntry>(64), holdLeaving = new List<HoldEntry>(8);
        readonly List<(BaggageBatch batch, int index)> order = new List<(BaggageBatch, int)>(128);
        readonly Dictionary<(BaggageBatch, int), int> variants = new Dictionary<(BaggageBatch, int), int>(128);
        readonly List<Token> tokens = new List<Token>(16);
        readonly (BaggageBatch batch, int index)[] holdReserved = new (BaggageBatch, int)[4];
        readonly int[] simCart = new int[8];
        BaggageTurnaround job;
        int holdShown, perLayer, layers, simHold;
        float directClock;

        public BaggageTurnaround Job => job;
        public IReadOnlyList<Cart> Carts => carts;
        public IReadOnlyList<HoldEntry> HoldBags => hold;
        public IReadOnlyList<HoldEntry> HoldLeaving => holdLeaving;
        public IReadOnlyList<Token> Tokens => tokens;
        /// <summary>Bags still riding into the hall on the conveyor of a depot state that has ended (the ledger already
        /// counts them in the hall); they finish the ride on that conveyor and vanish into the opening.</summary>
        public IReadOnlyList<Token> Ghosts => ghosts;
        readonly List<Token> ghosts = new List<Token>(16);
        Vector3 ghostFoot, ghostTop; float ghostLength;
        public Vector3 GhostBottom(in Token t) => Vector3.Lerp(ghostFoot,ghostTop,t.s/Mathf.Max(.001f,ghostLength))+Vector3.up*Offset(t.motion,t.anim);
        /// <summary>The ended arrivals conveyor is still there to put a carried bag on.</summary>
        public bool Ghosting => ghostLength > 0f && Mathf.Abs(fleet.TrainSpeed) <= 0f && DepotState(job);
        public Vector3 GhostFoot => ghostFoot;
        internal void PutOnGhost(int variant) => ghosts.Add(new Token { s = 0f, up = true, variant = variant, cart = -1 });
        public Quaternion GhostRotation
        {
            get { var d = ghostTop-ghostFoot; d.y = 0; return d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d.normalized) : Quaternion.identity; }
        }
        /// <summary>An aircraft transfer is being shown: the stacks trail the simulation instead of copying it.</summary>
        public bool Session { get; private set; }
        /// <summary>From the carts (to the hold, or to the depot's arrivals); otherwise into the carts.</summary>
        public bool Loading { get; private set; }
        /// <summary>The session is a depot handover: the belt is the depot conveyor and the far end is the hall.</summary>
        public bool Depot { get; private set; }
        BaggageTurnaroundState sessionState;
        int farGiven, sessionCarts;
        /// <summary>Bags still to go from the source pile to the destination pile, including those on the belt or in hands.</summary>
        public int Backlog { get; private set; }

        // Set by the performance each step.
        internal bool cartPost, holdPost;              // a handler is working the cart end / the hold end
        internal bool cartReceiving, holdReceiving;     // ... and will take the next bag that arrives there
        internal bool footReserved, topReserved;
        internal bool cartHand, holdHand;                // a handler at the source post will take the next owed bag
        internal float cartEta = float.PositiveInfinity, holdEta = float.PositiveInfinity; // seconds until the post is worked        // a handler is about to put a bag on that end of the belt
        float deadline = float.PositiveInfinity;
        internal int handBags;                           // bags in the hands of handlers

        /// <summary>Copy the ledger at all times (no performers to trail it).</summary>
        readonly bool copyOnly;
        public BaggageBagFlow(BaggageFleet fleet, bool copyOnly = false) { this.fleet = fleet; this.copyOnly = copyOnly; }

        // ------------------------------------------------------------------ geometry

        Vector3 foot, top, pickup; float lower;
        public float Length { get; private set; }
        public float RideSeconds => Length/BeltSpeed;
        void Belt()
        {
            if (job?.Plan == null || fleet.Job != job) { Length = 0; return; }
            if (DepotState(job))
            {
                // The depot conveyor beside the bay: its near end by the carts, its far end in the hall wall's opening.
                var depot = fleet.Station.depot; bool fromCart = Session ? Loading : job.State == BaggageTurnaroundState.UnloadingArrivals;
                foot = depot.Feed(fleet,!fromCart); var door = depot.Door(depot.Layout.LaneFor(fleet.Station.depotBay));
                top = new Vector3(door.x,foot.y,door.y)+(fromCart ? -1f : 1f)*new Vector3(depot.Facing.x,0f,depot.Facing.y)*.9f;
                pickup = top; lower = Length = Vector3.Distance(foot,top); return;
            }
            foot = BaggageLoadPath.Point(fleet,job.Plan,1); top = BaggageLoadPath.Point(fleet,job.Plan,2); pickup = job.Plan.door+job.Plan.inward*.42f;
            lower = Vector3.Distance(foot,top); Length = lower+Vector3.Distance(top,pickup);
        }
        public Vector3 BeltPoint(float s) => s <= lower ? Vector3.Lerp(foot,top,s/Mathf.Max(.001f,lower)) :
            Vector3.Lerp(top,pickup,(s-lower)/Mathf.Max(.001f,Length-lower));
        public Quaternion BeltRotation(float s)
        {
            var along = s <= lower ? top-foot : pickup-top;
            along.y = 0; return along.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(along.normalized) : Quaternion.identity;
        }
        static float Ease(float t) { t = Mathf.Clamp01(t); return t*t*(3f-2f*t); }
        /// <summary>Vertical offset of a bag that is settling into (motion -1) or rising out of (+1) a pile or the belt.</summary>
        public static float Offset(sbyte motion, float anim) => motion < 0 ? -Ease(anim/Settle)*SettleDepth : motion > 0 ? -(1f-Ease(anim/Settle))*SettleDepth : 0f;
        public Vector3 TokenBottom(in Token t) => BeltPoint(t.s)+Vector3.up*Offset(t.motion,t.anim);
        public Vector3 CartSlot(int cart, int slot)
        {
            var local = fleet.CartPacking.Position(slot); var p = fleet.Train.CartAt(cart).Point(local);
            return new Vector3(p.x,local.y,p.y);
        }
        public Quaternion CartRotation(int cart, int variant)
        {
            var f = fleet.Train.CartAt(cart).facing;
            return Quaternion.LookRotation(new Vector3(f.x,0,f.y))*fleet.CartPacking.Rotation(variant);
        }
        public Vector3 HoldBottom(in HoldEntry e) => e.bottom+Vector3.up*Offset(e.motion,e.anim);
        public Quaternion HoldRotation(in HoldEntry e) => e.rotation;
        /// <summary>World slot of a hold bag; cached by the display because the simulation frees slots when a batch leaves.</summary>
        public void HoldSlot(BaggageBatch batch, int index, out Vector3 bottom, out Quaternion rotation)
        {
            int slot = job.Packing.Slot(batch,index);
            bottom = slot >= 0 ? job.Packing.WorldSlot(slot) : job.Packing.BagPosition(batch,index);
            rotation = job.Packing.BagRotation(batch,index);
        }
        public Vector3 BeltFoot => foot;
        public Vector3 BeltPickup => pickup;

        // ------------------------------------------------------------------ the simulation's picture

        static bool Handling(BaggageTurnaround j) => j != null && !j.Finished &&
            (j.State == BaggageTurnaroundState.UnloadingHold || j.State == BaggageTurnaroundState.LoadingHold || DepotState(j));
        /// <summary>The train stands in a depot bay for a handover with the hall.</summary>
        static bool DepotState(BaggageTurnaround j) => j != null && !j.Finished && j.Fleet.Station.depot != null &&
            (j.State == BaggageTurnaroundState.UnloadingArrivals || j.State == BaggageTurnaroundState.CollectingDepartures);
        /// <summary>The train stands in a depot bay for a handover with the hall (presentation: the depot crew works it).</summary>
        public bool AtDepot => DepotState(job) && fleet.Job == job;
        /// <summary>The cart side of the next handling in this state is the source (loading, depot arrivals).</summary>
        public bool CartSource => Session ? Loading : job == null || job.State == BaggageTurnaroundState.LoadingHold || job.State == BaggageTurnaroundState.UnloadingArrivals;

        /// <summary>Bags the simulation has taken out of a store in the running transfer, and bags it has delivered.</summary>
        void Progress(out int departed, out int arrived)
        {
            departed = arrived = 0;
            if (job?.TransferBatch == null) return;
            float interval = Mathf.Max(.0001f,job.TransferInterval), seconds = job.TransferSeconds, travel = job.BagTravelSeconds;
            int n = job.TransferBatch.Count;
            departed = seconds > 0 ? Mathf.Clamp(Mathf.FloorToInt(seconds/interval+1e-4f)+1,0,n) : 0;
            arrived = seconds >= travel ? Mathf.Clamp(Mathf.FloorToInt((seconds-travel)/interval+1e-4f)+1,0,n) : 0;
        }

        /// <summary>What each pile holds in the simulation now: bags already taken out are gone from their source; bags on
        /// the simulation's conveyor count as delivered when <paramref name="optimistic"/>.</summary>
        void SimCounts(bool optimistic)
        {
            Progress(out int departed, out int arrived);
            int delivered = optimistic ? departed : arrived;
            for (int i = 0; i < carts.Length; i++)
            {
                var store = fleet.Carts[i]; int count = store.Count;
                if (job?.TransferBatch != null && job.TransferBatch.Location == store && job.TransferFrom == store) count -= departed;
                if (job?.TransferBatch != null && job.TransferTo == store && job.TransferBatch.Location != store) count += delivered;
                simCart[i] = count;
            }
            simHold = 0;
            if (job == null) return;
            // Depot handovers count the hall end relative to the session: bags delivered into it, or taken out of it.
            if (Session && Depot)
            {
                // Batches already transferred this session have changed the carts' ledger count; the running one counts by bag.
                int carts = fleet.CartCount;
                simHold = Loading ? sessionCarts-carts+delivered : -(carts-sessionCarts+departed);
                return;
            }
            simHold = job.Hold.Count;
            if (job.TransferBatch != null && job.TransferFrom == job.Hold && job.TransferBatch.Location == job.Hold) simHold -= departed;
            if (job.TransferBatch != null && job.TransferTo == job.Hold && job.TransferBatch.Location != job.Hold) simHold += delivered;
        }

        /// <summary>The hold's bags in simulation order: loading fills in this order; unloading empties it from the front.</summary>
        void Order()
        {
            if (job == null) { order.Clear(); return; }
            if (Session && Depot) return; // the hold display is not part of a depot handover
            var hold = job.Hold;
            if (Session && !Loading)
            {
                // Unloading keeps one order for the whole session (the simulation's, by batch id, last bag out first in the
                // list's tail), so a lagging display never loses bags when the ledger moves a batch.
                if (unloadOrder) return;
                order.Clear(); batches.Clear(); unloadOrder = true;
                foreach (var b in hold.Batches) batches.Add(b);
                batches.Sort(ById);
                for (int k = batches.Count-1; k >= 0; k--) for (int i = batches[k].Count-1; i >= 0; i--) order.Add((batches[k],i));
                return;
            }
            unloadOrder = false; order.Clear();
            foreach (var b in hold.Batches) for (int i = 0; i < b.Count; i++) order.Add((b,i));
            if (job.TransferBatch != null && job.TransferTo == hold && job.TransferBatch.Location != hold)
                for (int i = 0; i < job.TransferBatch.Count; i++) order.Add((job.TransferBatch,i));
        }
        readonly List<BaggageBatch> batches = new List<BaggageBatch>(16);
        static readonly System.Comparison<BaggageBatch> ById = (a,b) => string.CompareOrdinal(a.Id,b.Id);
        bool unloadOrder;
        int Variant((BaggageBatch batch, int index) key) => variants.TryGetValue(key,out var v) ? v : BaggageHoldPacking.Variant(key.batch,key.index);

        // ------------------------------------------------------------------ step

        /// <summary>Follow the simulation after it stepped. <paramref name="dt"/> is game seconds (0 while paused).</summary>
        internal void Step(float dt)
        {
            var next = fleet.Job ?? job;
            if (next != job || carts.Length != fleet.Carts.Length) Reset(next);
            Belt();
            bool handling = Handling(job);
            bool released = Mathf.Abs(fleet.TrainSpeed) > 0f || Mathf.Abs(fleet.LoaderSpeed) > 0f || job == null || job.Finished;
            // A depot handover ends with its state (arrivals unloaded, then departures collected in the same bay).
            if (Session && Depot && job.State != sessionState) { Close(); settleCopy = true; }
            if (handling && !Session && job.TransferBatch != null && !copyOnly)
            {
                Session = true; Depot = DepotState(job); sessionState = job.State; variants.Clear(); SessionId++;
                Loading = Depot ? fleet.IsCart(job.TransferFrom) : job.State == BaggageTurnaroundState.LoadingHold;
                if (Depot) { holdShown = 0; farGiven = 0; sessionCarts = fleet.CartCount; Belt(); }
            }
            // Handling has ended: whatever is still owed settles into place now, before the train or loader moves.
            if (Session && !handling) { Close(); unloadOrder = false; settleCopy = true; }
            if (released) settleCopy = false;
            if (Session && !Depot && handling && job.TransferBatch != null) Loading = job.TransferTo == job.Hold;
            Remaining(dt);
            SimCounts(true);
            Order();
            if (!Session) { Copy(); Animate(dt); return; }
            Animate(dt);
            // The piles must match the ledger when this state ends (a depot bay may hold two states in one stay).
            deadline = handling ? DepotState(job) ? stateRemaining : RemainingHandling() : 0f;
            Flow(dt);
            Backlog = Owed()+tokens.Count+handBags;
        }

        /// <summary>Game seconds of hold handling the simulation still has to do in this state, from what it already knows:
        /// the running batch, the bags left to move at the transfer interval and the conveyor time of each further batch.</summary>
        public float RemainingHandling() => Handling(job) ? remaining : 0f;
        /// <summary>Game seconds left in the current handling state (at a depot, arrivals and departures are two).</summary>
        public float StateRemaining => Handling(job) ? stateRemaining : 0f;
        /// <summary>Counts bag sessions, so a bag taken in one is never put down in the next.</summary>
        public int SessionId { get; private set; }
        float remaining, stateRemaining;
        /// <summary>Between batches the simulation briefly runs no transfer and the estimate would dip; it may then only
        /// fall as fast as time passes.</summary>
        void Remaining(float dt)
        {
            float now = Estimate();
            remaining = job?.TransferBatch != null || !Handling(job) ? now : Mathf.Max(now,remaining-dt);
            stateRemaining = DepotState(job) ? DepotEstimate(false) : remaining;
        }
        float Estimate()
        {
            if (!Handling(job)) return 0f;
            if (DepotState(job)) return DepotEstimate(true);
            float i = Mathf.Max(.0001f,job.TransferInterval), t = job.BagTravelSeconds, now = 0f;
            int batchSize = 10, rest;
            if (job.TransferBatch != null) { now = Mathf.Max(0f,(job.TransferBatch.Count-1)*i+t-job.TransferSeconds); batchSize = job.TransferBatch.Count; }
            if (job.State == BaggageTurnaroundState.LoadingHold) rest = fleet.CartCount-(job.TransferBatch != null && job.TransferBatch.Location != job.Hold ? job.TransferBatch.Count : 0);
            else
            {
                int free = 0; foreach (var c in fleet.Carts) free += c.Capacity-c.Count;
                rest = Mathf.Min(job.Hold.Count,free)-(job.TransferBatch != null && job.TransferBatch.Location == job.Hold ? job.TransferBatch.Count : 0);
            }
            rest = Mathf.Max(0,rest);
            int more = (rest+Mathf.Max(1,batchSize)-1)/Mathf.Max(1,batchSize);
            return now+rest*i+more*(Mathf.Max(0f,t-i)+.1f)+(job.TransferBatch == null && rest > 0 ? .5f : 0f);
        }

        /// <summary>Game seconds the train will certainly still stand in the depot bay: the running batch, the bags still in
        /// the carts (arrivals), and the departures that will fit the carts, at the simulation's own intervals. Waits for
        /// bags from the terminal are not knowable and only make the stay longer.</summary>
        float DepotEstimate(bool stay)
        {
            var depot = fleet.Station.depot; float now = 0f; int carts = 0, capacity = 0;
            float hand = depot.HandSeconds/Mathf.Max(1,fleet.Crew.GroundWorkers), travel = depot.ConveyorSeconds+depot.HandSeconds;
            if (job.TransferBatch != null) now = Mathf.Max(0f,(job.TransferBatch.Count-1)*job.TransferInterval+job.BagTravelSeconds-job.TransferSeconds);
            foreach (var c in fleet.Carts) { carts += c.Count; capacity += c.Capacity; }
            if (job.State == BaggageTurnaroundState.UnloadingArrivals)
            {
                float interval = Mathf.Max(.65f/BaggagePacing.WorkRate,hand);
                int rest = carts-(job.TransferBatch != null && fleet.IsCart(job.TransferBatch.Location) ? job.TransferBatch.Count : 0);
                for (int i = 0; i < fleet.Carts.Length && rest > 0; i++)
                {
                    int n = Mathf.Min(rest,fleet.Carts[i].Count); if (n <= 0) continue;
                    now += (n-1)*interval+travel+.1f; rest -= n;
                }
                // The departures follow in the same bay when there is no separate dispatch stop and no second trip.
                if (!stay || job.Plan?.toDispatch != null || job.Hold.Count > 0) return now;
                return now+Departures(capacity,depot,hand,travel);
            }
            int free = capacity-carts-(job.TransferBatch != null && !fleet.IsCart(job.TransferBatch.Location) ? job.TransferBatch.Count : 0);
            return now+Departures(free,depot,hand,travel);
        }
        float Departures(int free, BaggageDepot depot, float hand, float travel)
        {
            float interval = Mathf.Max(depot.SortingSeconds,hand), t = 0f;
            void Add(BaggageStore store)
            {
                if (store == null) return;
                var list = store.Batches;
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i]; if (b == job.TransferBatch || b.Flight != job.DepartureFlight || b.Count > free) continue;
                    free -= b.Count; t += (b.Count-1)*interval+travel+.1f;
                }
            }
            Add(fleet.Dispatch); if (job.DepartureSource != fleet.Dispatch) Add(job.DepartureSource);
            // Departures already on their way from the terminal (the link exists once the turnaround asked for its supply).
            if (depot.connection != null && depot.connection.enabled) Add(depot.Airport.Baggage.TerminalLink(depot)?.Outbound);
            return t;
        }

        bool settleCopy;
        void Reset(BaggageTurnaround next)
        {
            if (Session) Close();
            job = next; tokens.Clear(); ghosts.Clear(); ghostLength = 0f; hold.Clear(); holdLeaving.Clear(); variants.Clear(); holdShown = 0;
            for (int i = 0; i < holdReserved.Length; i++) holdReserved[i] = default;
            if (carts.Length != fleet.Carts.Length)
            {
                carts = new Cart[fleet.Carts.Length];
                for (int i = 0; i < carts.Length; i++) carts[i] = new Cart { occupied = new bool[fleet.CartPacking.Capacity] };
                perLayer = 1; var first = fleet.CartPacking.Position(0).y;
                while (perLayer < fleet.CartPacking.Capacity && fleet.CartPacking.Position(perLayer).y <= first+.001f) perLayer++;
                layers = Mathf.Max(1,(fleet.CartPacking.Capacity+perLayer-1)/perLayer);
            }
            for (int i = 0; i < carts.Length; i++) { carts[i].store = fleet.Carts[i]; carts[i].entries.Clear(); System.Array.Clear(carts[i].occupied,0,carts[i].occupied.Length); }
        }

        /// <summary>The cart or loader is being released: show the ledger exactly.</summary>
        void Close()
        {
            int direct = tokens.Count;
            if (Depot && Loading && Mathf.Abs(fleet.TrainSpeed) <= 0f)
            {
                // Arrivals end with the train still in the bay: what rides into the hall keeps riding.
                ghosts.Clear(); ghostFoot = foot; ghostTop = top; ghostLength = Length;
                foreach (var t in tokens) if (t.up && t.motion >= 0) { var g = t; g.waiting = false; ghosts.Add(g); direct--; }
            }
            SimCounts(false);
            for (int i = 0; i < carts.Length; i++) direct += Mathf.Abs(Live(i)-simCart[i]);
            direct += Mathf.Abs(holdShown-simHold);
            if (direct > 0) BaggageCrewPerformance.CountDirect(direct);
            tokens.Clear(); Session = false; Depot = false; Backlog = 0; System.Array.Clear(pendingCart,0,pendingCart.Length);
            for (int i = 0; i < holdReserved.Length; i++) holdReserved[i] = default;
        }

        /// <summary>Outside an aircraft transfer the piles copy the ledger (depot handovers bag by bag, as the depot shows them).</summary>
        void Copy()
        {
            SimCounts(false);
            for (int i = 0; i < carts.Length; i++)
            {
                int live = Live(i);
                while (live > simCart[i]) { RemoveCart(i,Top(i,Vector3.zero,false),settleCopy); live--; }
                while (live < simCart[i]) { if (!AddCart(i,Lowest(i),NextVariant(i),settleCopy)) break; live++; }
            }
            holdShown = Mathf.Min(order.Count,simHold); Shown();
        }

        int Live(int cart) { int n = 0; foreach (var e in carts[cart].entries) if (e.motion >= 0) n++; return n; }
        int NextVariant(int cart)
        {
            var store = fleet.Carts[cart]; int live = Live(cart), k = 0;
            foreach (var b in store.Batches) for (int i = 0; i < b.Count; i++) if (k++ == live) return BaggageHoldPacking.Variant(b,i);
            return live*7+cart;
        }

        void Animate(float dt)
        {
            if (dt <= 0f) return;
            for (int i = ghosts.Count-1; i >= 0; i--)
            {
                var g = ghosts[i];
                if (g.motion != 0) { g.anim += dt; if (g.anim >= Settle) { if (g.motion < 0) { ghosts.RemoveAt(i); continue; } g.motion = 0; } }
                if (g.motion >= 0 && !fleet.Station.accessBlocked) g.s = Mathf.Min(ghostLength,g.s+BeltSpeed*dt);
                if (g.motion >= 0 && g.s >= ghostLength-.0005f) { g.motion = -1; g.anim = 0; }
                ghosts[i] = g;
            }
            foreach (var c in carts)
                for (int i = c.entries.Count-1; i >= 0; i--)
                {
                    var e = c.entries[i]; if (e.motion == 0) continue;
                    e.anim += dt;
                    if (e.anim >= Settle) { if (e.motion < 0) { c.entries.RemoveAt(i); continue; } e.motion = 0; }
                    c.entries[i] = e;
                }
            for (int i = hold.Count-1; i >= 0; i--)
            { var e = hold[i]; if (e.motion > 0) { e.anim += dt; if (e.anim >= Settle) e.motion = 0; hold[i] = e; } }
            for (int i = holdLeaving.Count-1; i >= 0; i--)
            { var e = holdLeaving[i]; e.anim += dt; if (e.anim >= Settle) holdLeaving.RemoveAt(i); else holdLeaving[i] = e; }
        }

        // ------------------------------------------------------------------ hold display

        bool Reserved(BaggageBatch b, int i) { foreach (var r in holdReserved) if (r.batch == b && r.index == i) return true; return false; }
        int ReservedCount { get { int n = 0; foreach (var r in holdReserved) if (r.batch != null) n++; return n; } }

        /// <summary>Rebuild the visible hold bags from the simulation order and the visible count, keeping animation state.</summary>
        void Shown(BaggageBatch rising = null, int risingIndex = -1)
        {
            int count = Mathf.Clamp(holdShown,0,order.Count), reserved = ReservedCount;
            int start, end;
            if (Loading && Session) { start = 0; end = Mathf.Min(order.Count,count+reserved); }
            else if (Session) { start = 0; end = Mathf.Min(order.Count,count+reserved); }
            else { start = 0; end = count; }
            int write = 0;
            for (int k = start; k < end; k++)
            {
                var key = order[k]; if (Session && Reserved(key.batch,key.index)) continue;
                var entry = new HoldEntry { batch = key.batch, index = key.index, variant = Variant(key) };
                bool known = false;
                for (int j = 0; j < hold.Count; j++)
                    if (hold[j].batch == key.batch && hold[j].index == key.index)
                    { entry.anim = hold[j].anim; entry.motion = hold[j].motion; entry.bottom = hold[j].bottom; entry.rotation = hold[j].rotation; known = true; break; }
                if (!known) HoldSlot(key.batch,key.index,out entry.bottom,out entry.rotation);
                if (key.batch == rising && key.index == risingIndex) { entry.motion = 1; entry.anim = 0; }
                if (write < hold.Count) hold[write] = entry; else hold.Add(entry);
                write++;
            }
            while (hold.Count > write) hold.RemoveAt(hold.Count-1);
            for (int i = 0; i < hold.Count; i++) for (int j = holdLeaving.Count-1; j >= 0; j--)
                if (holdLeaving[j].batch == hold[i].batch && holdLeaving[j].index == hold[i].index) holdLeaving.RemoveAt(j);
        }

        // ------------------------------------------------------------------ cart piles

        int Height(int cart, int column)
        {
            var o = carts[cart].occupied; int h = 0;
            while (h < layers && column+h*perLayer < o.Length && o[column+h*perLayer]) h++;
            return h;
        }
        /// <summary>Distance across the cart from <paramref name="near"/> to a slot.</summary>
        float Lateral(int cart, int slot, Vector3 near)
        {
            var r = fleet.Train.CartAt(cart).Right; var d = CartSlot(cart,slot)-near;
            return Mathf.Abs(d.x*r.x+d.z*r.y);
        }
        int Lowest(int cart) { var o = carts[cart].occupied; for (int i = 0; i < o.Length; i++) if (!o[i]) return i; return -1; }
        /// <summary>The free slot on top of a column nearest (<paramref name="nearest"/>) or farthest from <paramref name="near"/>,
        /// lower layers first so piles never overhang.</summary>
        public int Free(int cart, Vector3 near, bool nearest, float reach = float.PositiveInfinity)
        {
            int best = -1; float score = 0;
            for (int column = 0; column < perLayer; column++)
            {
                int h = Height(cart,column), slot = column+h*perLayer;
                if (h >= layers || slot >= carts[cart].occupied.Length) continue;
                if (Lateral(cart,slot,near) > reach) continue;
                float d = Vector3.Distance(CartSlot(cart,slot),near), value = (nearest ? -d : d) - h*10f;
                if (best < 0 || value > score) { best = slot; score = value; }
            }
            return best;
        }
        /// <summary>Index of an unreserved bag on top of its column, nearest (or farthest from) <paramref name="near"/>.</summary>
        public int Top(int cart, Vector3 near, bool nearest, float reach = float.PositiveInfinity)
        {
            var c = carts[cart]; int best = -1; float score = 0;
            for (int i = 0; i < c.entries.Count; i++)
            {
                var e = c.entries[i]; if (e.motion < 0 || e.reserved) continue;
                int column = e.slot%perLayer, layer = e.slot/perLayer;
                if (layer != Height(cart,column)-1) continue;
                if (Lateral(cart,e.slot,near) > reach) continue;
                float d = Vector3.Distance(CartSlot(cart,e.slot),near), value = (nearest ? -d : d)+layer*10f;
                if (best < 0 || value > score) { best = i; score = value; }
            }
            return best;
        }
        bool AddCart(int cart, int slot, int variant, bool rising)
        {
            if (slot < 0) return false;
            var c = carts[cart]; c.occupied[slot] = true;
            c.entries.Add(new CartEntry { slot = slot, variant = variant, motion = (sbyte)(rising ? 1 : 0) });
            return true;
        }
        void RemoveCart(int cart, int index, bool settle)
        {
            if (index < 0) return;
            var c = carts[cart]; var e = c.entries[index]; c.occupied[e.slot] = false;
            if (settle) { e.motion = -1; e.anim = 0; e.reserved = false; c.entries[index] = e; } else c.entries.RemoveAt(index);
        }
        int ReservedIn(int cart) { int n = 0; foreach (var e in carts[cart].entries) if (e.reserved && e.motion >= 0) n++; return n; }

        // ------------------------------------------------------------------ flow

        /// <summary>Bags the visible source piles still have to give up.</summary>
        int Owed()
        {
            int owed = 0;
            if (Loading) for (int i = 0; i < carts.Length; i++) owed += Mathf.Max(0,Live(i)-simCart[i]);
            else owed = Mathf.Max(0,holdShown-simHold);
            return owed;
        }
        /// <summary>A loading source cart with bags owed and free to take (not reserved by hands), or -1.</summary>
        public int SourceCart()
        {
            for (int i = 0; i < carts.Length; i++) if (Live(i)-ReservedIn(i)-simCart[i] > 0) return i;
            return -1;
        }
        /// <summary>The cart the next unloaded bag belongs in, or -1.</summary>
        public int DestinationCart()
        {
            for (int i = 0; i < carts.Length; i++)
            {
                int coming = 0; foreach (var t in tokens) if (t.cart == i) coming++;
                if (simCart[i]-Live(i)-coming-pendingCart[i] > 0) return i;
            }
            for (int i = 0; i < carts.Length; i++) if (Lowest(i) >= 0) return i;
            return 0;
        }
        readonly int[] pendingCart = new int[8];

        void Flow(float dt)
        {
            bool blocked = fleet.Station.accessBlocked;
            if (dt > 0f && !blocked) Advance(dt);
            if (blocked || dt <= 0f) return;
            // Quiet departures onto the belt, only while a handler works the source end and the far end is manned or
            // will be (a person at the post is what makes a moving belt plausible).
            // The source end is worked, or its handler is a few seconds from it (bags then appear to come from them).
            // The hall end of a depot conveyor is always worked (by the hall's own staff and machinery).
            bool source = Loading ? cartPost || cartEta <= ArrivingSeconds : Depot || holdPost || holdEta <= ArrivingSeconds;
            int owed = Owed()-HandReservations();
            int keep = (Loading ? cartHand : !Depot && holdHand) ? 1 : 0;
            if (source && owed > keep && Clear(Loading) && !(Loading ? footReserved : topReserved) && Length > 0f && Arrives())
            {
                if (Loading) { int cart = SourceCart(), top = cart >= 0 ? Top(cart,Near(cart),false) : -1; if (top >= 0) { var e = carts[cart].entries[top]; RemoveAt(cart,e,true); Spawn(true,e.variant,-1,true); } }
                else if (Depot) { holdShown--; Spawn(false,FarVariant(),DestinationCart(),true); }
                else { var key = NextHoldOut(); if (key.batch != null) { holdShown--; var v = Variant(key); LeaveHold(key,true); Spawn(false,v,DestinationCart(),true); } }
                owed--;
            }
            // Whatever the belt cannot deliver before the release goes straight from pile to pile, spread out.
            directClock -= dt;
            // The belt can still carry what fits between the handler reaching the source post and the release.
            float eta = Loading ? cartEta : Depot ? 0f : holdEta;
            float ride = DepotRide ? 0f : RideSeconds;
            int capacity = Mathf.Max(0,Mathf.FloorToInt((deadline-eta-ride-Settle)*BeltSpeed/Pitch));
            if (float.IsInfinity(eta)) capacity = 0;
            if (owed > capacity && directClock <= 0f)
            {
                float left = Mathf.Max(.05f,deadline-Settle);
                directClock = Mathf.Clamp(left/Mathf.Max(1,owed-capacity),.05f,DirectGap*2f);
                Direct();
            }
        }
        int HandReservations()
        {
            int n = 0;
            // A hold bag reserved for unloading has already left the shown count.
            if (Loading) for (int i = 0; i < carts.Length; i++) n += ReservedIn(i);
            return n;
        }
        /// <summary>A bag put on the belt now would reach the far end before the simulation releases it.</summary>
        bool Arrives() => deadline > (DepotRide ? Pitch/BeltSpeed : RideSeconds)+Settle;
        /// <summary>Into a depot hall the ride may outlast the state (the bags finish it as <see cref="Ghosts"/>), as long as
        /// the train stays in the bay.</summary>
        bool DepotRide => Depot && Loading && remaining > RideSeconds+Settle;

        void Direct()
        {
            if (Loading)
            {
                int cart = SourceCart(); if (cart < 0) return;
                int index = Top(cart,Near(cart),false); if (index < 0) return;
                var e = carts[cart].entries[index]; RemoveAt(cart,e,true);
                AddHold(e.variant,true);
            }
            else
            {
                int v;
                if (Depot) { holdShown--; v = FarVariant(); }
                else { var key = NextHoldOut(); if (key.batch == null) return; holdShown--; v = Variant(key); LeaveHold(key,true); }
                int cart = DestinationCart(); AddCart(cart,Free(cart,Near(cart),false),v,true);
            }
            BaggageCrewPerformance.CountDirect(1);
        }
        /// <summary>Where the carts are worked from: the belt side of the cart (aircraft) or the depot conveyor's near end.</summary>
        Vector3 Near(int cart) => Depot ? foot : fleet.Crew.CartStandAt(cart);
        /// <summary>Look of the next bag the hall gives out (departures): the running batch's bags in order.</summary>
        int FarVariant()
        {
            var b = job.TransferBatch; int n = farGiven++;
            return b != null && b.Count > 0 ? BaggageHoldPacking.Variant(b,n%b.Count) : n%3;
        }
        void RemoveAt(int cart, CartEntry entry, bool settle)
        {
            var c = carts[cart];
            for (int i = 0; i < c.entries.Count; i++) if (c.entries[i].slot == entry.slot && c.entries[i].motion >= 0) { RemoveCart(cart,i,settle); return; }
        }

        /// <summary>The next bag to leave the hold (unloading) that no handler has reserved.</summary>
        (BaggageBatch batch, int index) NextHoldOut()
        {
            // Unloading order lists the first bag out last, so the next bag out is the last shown one.
            for (int k = Mathf.Min(order.Count,holdShown+ReservedCount)-1; k >= 0; k--) if (!Reserved(order[k].batch,order[k].index)) return order[k];
            return default;
        }
        /// <summary>The next hold position to fill (loading) that no handler has reserved.</summary>
        (BaggageBatch batch, int index) NextHoldIn()
        {
            int k = holdShown+ReservedCount;
            return k < order.Count ? order[k] : default;
        }
        void LeaveHold((BaggageBatch batch, int index) key, bool settle)
        {
            for (int i = 0; i < hold.Count; i++)
                if (hold[i].batch == key.batch && hold[i].index == key.index)
                { if (settle) { var e = hold[i]; e.motion = -1; e.anim = 0; holdLeaving.Add(e); } break; }
            Shown();
        }
        void AddHold(int variant, bool rising)
        {
            if (Depot) { holdShown++; return; } // into the hall
            var key = NextHoldIn(); if (key.batch == null) { holdShown++; return; }
            variants[key] = variant; holdShown++;
            Shown(rising ? key.batch : null,rising ? key.index : -1);
        }

        bool Clear(bool atFoot)
        {
            float end = atFoot ? 0f : Length;
            foreach (var t in tokens) if (Mathf.Abs(t.s-end) < Pitch) return false;
            return true;
        }
        void Spawn(bool up, int variant, int cart, bool rising) =>
            tokens.Add(new Token { s = up ? 0f : Length, up = up, variant = variant, cart = cart, motion = (sbyte)(rising ? 1 : 0) });

        void Advance(float dt)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.motion != 0)
                {
                    t.anim += dt;
                    if (t.anim >= Settle)
                    {
                        if (t.motion < 0) { tokens.RemoveAt(i); i--; continue; }
                        t.motion = 0;
                    }
                }
                if (t.motion < 0) { tokens[i] = t; continue; }
                float end = t.up ? Length : 0f;
                if (t.waiting)
                {
                    t.waited += dt;
                    bool receiver = t.up ? holdReceiving && !Depot : cartReceiving;
                    if (!receiver || t.waited > WaitLimit) { Deliver(ref t,true); }
                    tokens[i] = t; continue;
                }
                float ahead = t.up ? float.PositiveInfinity : float.NegativeInfinity;
                for (int k = 0; k < tokens.Count; k++)
                {
                    if (k == i || tokens[k].up != t.up || tokens[k].motion < 0) continue;
                    float s = tokens[k].s; if (t.up ? s > t.s && s < ahead : s < t.s && s > ahead) ahead = s;
                }
                float move = BeltSpeed*dt;
                t.s = t.up ? Mathf.Min(t.s+move,Mathf.Min(end,ahead-Pitch)) : Mathf.Max(t.s-move,Mathf.Max(end,ahead+Pitch));
                if (Mathf.Abs(t.s-end) < .0005f)
                {
                    bool receiver = t.up ? holdReceiving && !Depot : cartReceiving;
                    if (receiver && !WaitingAt(t.up)) { t.waiting = true; t.waited = 0; }
                    else Deliver(ref t,true);
                }
                tokens[i] = t;
            }
        }
        bool WaitingAt(bool up) { foreach (var t in tokens) if (t.up == up && t.waiting && t.motion >= 0) return true; return false; }

        /// <summary>A bag at the end of the belt with no hands for it settles off the belt and rises in its pile.</summary>
        void Deliver(ref Token t, bool settle)
        {
            t.waiting = false; t.motion = -1; t.anim = 0;
            if (t.up) AddHold(t.variant,true);
            else
            {
                int cart = t.cart >= 0 ? t.cart : DestinationCart();
                AddCart(cart,Free(cart,Near(cart),false),t.variant,true);
                t.cart = -2;
            }
        }

        // ------------------------------------------------------------------ hands

        /// <summary>Reserve the top bag nearest <paramref name="near"/> in a loading source cart for a handler's hands.</summary>
        internal bool ReserveCart(int cart, Vector3 near, float reach, out int slot, out int variant)
        {
            slot = variant = -1;
            if (cart < 0 || Live(cart)-ReservedIn(cart)-simCart[cart] <= 0) return false;
            int i = Top(cart,near,true,reach); if (i < 0) return false;
            var c = carts[cart]; var e = c.entries[i]; e.reserved = true; c.entries[i] = e; slot = e.slot; variant = e.variant;
            return true;
        }
        internal void ReleaseCart(int cart, int slot)
        {
            var c = carts[cart];
            for (int i = 0; i < c.entries.Count; i++) if (c.entries[i].slot == slot && c.entries[i].reserved) { var e = c.entries[i]; e.reserved = false; c.entries[i] = e; }
        }
        /// <summary>The reserved bag leaves the cart in the hands.</summary>
        internal void LiftCart(int cart, int slot)
        {
            var c = carts[cart];
            for (int i = 0; i < c.entries.Count; i++) if (c.entries[i].slot == slot && c.entries[i].motion >= 0) { RemoveCart(cart,i,false); return; }
        }
        /// <summary>Is the belt foot clear for a bag from the hands?</summary>
        internal bool FootClear => Clear(true);
        internal bool TopClear => Clear(false);
        internal void PutOnBelt(bool atFoot, int variant, int cart = -1) => Spawn(atFoot,variant,atFoot ? -1 : cart < 0 ? DestinationCart() : cart,false);

        /// <summary>A bag waiting at the end of the belt for hands.</summary>
        internal bool Waiting(bool atTop, out Vector3 bottom, out Quaternion rotation, out int variant, out int cart)
        {
            foreach (var t in tokens)
                if (t.up == atTop && t.waiting && t.motion >= 0) { bottom = TokenBottom(t); rotation = BeltRotation(t.s); variant = t.variant; cart = t.cart; return true; }
            bottom = default; rotation = Quaternion.identity; variant = cart = -1; return false;
        }
        internal void TakeWaiting(bool atTop)
        {
            for (int i = 0; i < tokens.Count; i++) if (tokens[i].up == atTop && tokens[i].waiting && tokens[i].motion >= 0)
            { if (!atTop && tokens[i].cart >= 0) pendingCart[tokens[i].cart]++; tokens.RemoveAt(i); return; }
        }
        /// <summary>Reserve the cart slot a carried bag is going into (unloading).</summary>
        internal int ReserveSlot(int cart, Vector3 near, float reach)
        {
            if (cart < 0) return -1;
            int slot = Free(cart,near,true,reach); if (slot < 0) slot = Free(cart,near,true);
            if (slot >= 0) carts[cart].occupied[slot] = true;
            return slot;
        }
        internal void PlaceInCart(int cart, int slot, int variant)
        {
            if (cart < 0) return;
            if (pendingCart[cart] > 0) pendingCart[cart]--;
            var c = carts[cart];
            if (slot >= 0) { c.entries.Add(new CartEntry { slot = slot, variant = variant }); c.occupied[slot] = true; }
            else AddCart(cart,Lowest(cart),variant,true);
        }
        internal void CancelSlot(int cart, int slot)
        {
            if (cart < 0 || slot < 0) return;
            if (pendingCart[cart] > 0) pendingCart[cart]--;
            bool used = false; foreach (var e in carts[cart].entries) if (e.slot == slot) used = true;
            if (!used) carts[cart].occupied[slot] = false;
        }

        /// <summary>Reserve the next hold bag to come out (unloading) or the next position to fill (loading).</summary>
        /// <summary>Reserve the next hold bag to come out (unloading) or the next position to fill (loading); the bag's
        /// place in the hold comes back in <paramref name="bottom"/> / <paramref name="rotation"/>. An unloading bag leaves
        /// the display here: its handler draws it in place until the lift.</summary>
        internal bool ReserveHold(int worker, out int variant, out Vector3 bottom, out Quaternion rotation)
        {
            variant = -1; bottom = default; rotation = Quaternion.identity;
            if (!Session || job?.Plan == null || worker >= holdReserved.Length || holdReserved[worker].batch != null) return false;
            var key = Loading ? NextHoldIn() : NextHoldOut();
            if (key.batch == null) return false;
            if (!Loading && holdShown-simHold <= 0) return false;
            bool found = false;
            foreach (var e in hold) if (e.batch == key.batch && e.index == key.index) { bottom = e.bottom; rotation = e.rotation; found = true; break; }
            if (!found) HoldSlot(key.batch,key.index,out bottom,out rotation);
            holdReserved[worker] = key; variant = Variant(key);
            if (!Loading) { holdShown--; Shown(); }
            return true;
        }
        /// <summary>Unloading bags the hold display still owes the belt (not counting reserved ones).</summary>
        internal int HoldOwed => Mathf.Max(0,holdShown-simHold);
        /// <summary>Loading bags a cart still owes the belt, not counting bags reserved for hands.</summary>
        internal int CartOwed(int cart) => cart < 0 ? 0 : Live(cart)-ReservedIn(cart)-simCart[cart];
        /// <summary>Place of the next bag to stow (loading) or take out (unloading).</summary>
        internal bool PeekHold(out Vector3 bottom)
        {
            bottom = default;
            if (!Session || job?.Plan == null) return false;
            var key = Loading ? NextHoldIn() : NextHoldOut(); if (key.batch == null) return false;
            foreach (var e in hold) if (e.batch == key.batch && e.index == key.index) { bottom = e.bottom; return true; }
            HoldSlot(key.batch,key.index,out bottom,out _); return true;
        }
        internal int HoldVariant(BaggageBatch batch, int index) => Variant((batch,index));
        /// <summary>The hand-stowed bag joins the hold display in its own position (loading).</summary>
        internal void StowHold(int worker, int variant)
        {
            if (worker >= holdReserved.Length) return;
            var key = holdReserved[worker]; holdReserved[worker] = default;
            if (key.batch == null) return;
            variants[key] = variant; holdShown++;
            // Keep the display contiguous in simulation order: the stowed position becomes the next shown one.
            Shown();
        }
        /// <summary>Give up a reservation (the handler was recalled before the bag left the hold, or before stowing).</summary>
        internal void ReleaseHold(int worker, bool restore)
        {
            if (worker >= holdReserved.Length || holdReserved[worker].batch == null) return;
            holdReserved[worker] = default;
            if (restore) holdShown++;
            Shown();
        }
        /// <summary>A stowed bag without a reserved position (should not happen): it joins the hold display next in order.</summary>
        internal void StowAny(int variant) => AddHold(variant,false);
        internal bool HoldReservation(int worker) => worker < holdReserved.Length && holdReserved[worker].batch != null;
    }
}
