using System;
using System.Collections.Generic;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum BaggageConnectionKind { Underground, Covered }

    [Serializable]
    public sealed class TerminalBaggageSettings
    {
        [Min(0)] public int checkInFloor=1, reclaimFloor;
        [Min(100)] public int checkInCapacity=2400;
        public TerminalBaggageSettings Clone() => (TerminalBaggageSettings)MemberwiseClone();
    }

    [Serializable]
    public sealed class BaggageTerminalConnection
    {
        public bool enabled;
        public string terminalId;
        public BaggageConnectionKind kind;
        [Min(36)] public int capacity=144;
        [Min(1)] public float bagsPerMinute=300;
        [Min(.1f)] public float beltSpeed=3;
        [Min(1)] public float depth=5;
    }

    /// <summary>A terminal's abstract passenger-side endpoints. The ledger remains the only owner of bags.</summary>
    public sealed class TerminalBaggageFacility
    {
        public Terminal Terminal { get; }
        public BaggageStore CheckIn { get; }
        public BaggageStore Reclaim { get; }
        public int Delivered { get; internal set; }
        internal TerminalBaggageFacility(Terminal terminal,BaggageLedger ledger,string name)
        {
            Terminal=terminal;
            CheckIn=ledger.Store(name+" check-in",Mathf.Max(100,terminal.baggage.checkInCapacity),float.MaxValue);
            Reclaim=ledger.Store(name+" reclaim",int.MaxValue,float.MaxValue);
        }
        public int CheckInFloor => Mathf.Clamp(Terminal.baggage.checkInFloor,0,Terminal.floors-1);
        public int ReclaimFloor => Mathf.Clamp(Terminal.baggage.reclaimFloor,0,Terminal.floors-1);
    }

    /// <summary>Two directional conveyors with bounded transit stores, FIFO admission and timed atomic delivery.</summary>
    public sealed class TerminalBaggageLink
    {
        sealed class Journey
        {
            public BaggageBatch batch;
            public BaggageStore transit,destination;
            public float remaining;
        }
        readonly BaggageLedger ledger;
        readonly Queue<BaggageBatch> departures=new Queue<BaggageBatch>();
        readonly List<Journey> journeys=new List<Journey>();
        float outDelay,inDelay;
        public BaggageDepot Depot { get; }
        public TerminalBaggageFacility Facility { get; }
        public BaggageStore Outbound { get; }
        public BaggageStore Inbound { get; }
        public BaggageStore Dispatch { get; }
        public BaggageStore Arrivals { get; }
        public int QueuedDepartures { get; private set; }
        public int InTransit => Outbound.Count+Inbound.Count;
        public int WaitingArrivals => Arrivals.Count;
        public bool Pending => QueuedDepartures>0 || InTransit>0 || WaitingArrivals>0;
        public string Waiting { get; private set; }
        public float OutboundSeconds => Travel(true);
        public float InboundSeconds => Travel(false);
        public Vector3 HallPort
        {
            get { var p=Depot.Point(Depot.Layout.Right,Depot.Layout.WorkZ-6); return new Vector3(p.x,.8f,p.y); }
        }
        public Vector3 TerminalPort(bool outgoing)
        {
            var terminal=Facility.Terminal;
            int floor=outgoing ? Facility.CheckInFloor : Facility.ReclaimFloor;
            float height=terminal.FloorElevation(floor)+.8f;
            terminal.ClosestOnWall(new Vector2(HallPort.x,HallPort.z),height,out var at,out _);
            return new Vector3(at.x,height,at.y);
        }
        public float HorizontalLength
        {
            get { var end=TerminalPort(true); return Vector2.Distance(new Vector2(HallPort.x,HallPort.z),new Vector2(end.x,end.z)); }
        }
        float Travel(bool outgoing)
        {
            var end=TerminalPort(outgoing); var settings=Depot.connection;
            float distance=Vector2.Distance(new Vector2(HallPort.x,HallPort.z),new Vector2(end.x,end.z));
            distance+=settings.kind==BaggageConnectionKind.Underground ?
                HallPort.y+end.y+Mathf.Max(1,settings.depth)*2 :
                Mathf.Max(6.6f,HallPort.y,end.y)*2-HallPort.y-end.y;
            float upgrade=Depot.Has(BaggageDepotUpgrade.PoweredBelts) ? 1.5f : 1;
            return distance/(Mathf.Max(.1f,settings.beltSpeed)*BaggagePacing.TravelRate*upgrade);
        }
        public float Rate => Mathf.Max(1,Depot.connection.bagsPerMinute)/60*
            (Depot.Has(BaggageDepotUpgrade.PoweredBelts) ? 1.5f : 1);
        public int Capacity => Mathf.Max(36,Depot.connection.capacity);

        internal TerminalBaggageLink(BaggageDepot depot,TerminalBaggageFacility facility,BaggageLedger ledger)
        {
            Depot=depot; Facility=facility; this.ledger=ledger;
            Outbound=ledger.Store(depot.name+" terminal-to-hall conveyor",Capacity,float.MaxValue);
            Inbound=ledger.Store(depot.name+" hall-to-terminal conveyor",Capacity,float.MaxValue);
            Dispatch=depot.Dispatch(ledger); Arrivals=depot.Arrivals(ledger);
        }
        internal void Enqueue(BaggageBatch batch)
        {
            if (batch.Location!=Facility.CheckIn) throw new ArgumentException("Departure baggage must enter at terminal check-in.");
            departures.Enqueue(batch); QueuedDepartures+=batch.Count;
        }
        public bool IsSupply(BaggageBatch batch) => batch.Location==Facility.CheckIn || batch.Location==Outbound;
        internal void Step(float seconds)
        {
            Waiting=null;
            if (Depot==null || !Depot.isActiveAndEnabled || !Depot.Airport.terminals.Contains(Facility.Terminal))
            { Waiting="Terminal baggage connection is unavailable."; return; }
            // Occupied stores cannot shrink through an Inspector edit; admitted journeys retain their timing.
            Outbound.Resize(Mathf.Max(Capacity,Outbound.Count),float.MaxValue);
            Inbound.Resize(Mathf.Max(Capacity,Inbound.Count),float.MaxValue);
            for (int i=journeys.Count-1;i>=0;i--)
            {
                var journey=journeys[i]; journey.remaining-=seconds;
                if (journey.remaining>0) continue;
                if (!ledger.Transfer(journey.batch,journey.transit,journey.destination,out var why))
                { Waiting=why; continue; }
                if (journey.destination==Facility.Reclaim) Facility.Delivered+=journey.batch.Count;
                journeys.RemoveAt(i);
            }
            outDelay-=seconds; inDelay-=seconds;
            // Separate directional limits prevent an arrivals backlog from stopping departing baggage.
            if (outDelay<=0 && departures.Count>0)
            {
                var batch=departures.Peek();
                if (batch.Location!=Facility.CheckIn) { departures.Dequeue(); QueuedDepartures-=batch.Count; }
                else if (Admit(batch,Facility.CheckIn,Outbound,Dispatch,true,out var why))
                { departures.Dequeue(); QueuedDepartures-=batch.Count; outDelay=Mathf.Max(0,outDelay)+batch.Count/Rate; }
                else Waiting=why;
            }
            if (inDelay<=0 && Arrivals.Batches.Count>0)
            {
                var batch=Arrivals.Batches[0];
                if (Admit(batch,Arrivals,Inbound,Facility.Reclaim,false,out var why))
                    inDelay=Mathf.Max(0,inDelay)+batch.Count/Rate;
                else Waiting ??= why;
            }
        }
        bool Admit(BaggageBatch batch,BaggageStore from,BaggageStore transit,BaggageStore to,bool outgoing,out string reason)
        {
            reason=null;
            if (batch.Count>Capacity) { reason="Increase connection capacity to fit the waiting baggage batch."; return false; }
            if (outgoing && !batch.Screened) { reason="Departing baggage awaiting screening."; return false; }
            if (transit.Count+batch.Count>Capacity) { reason="Baggage connection is at capacity."; return false; }
            if (!ledger.Transfer(batch,from,transit,out reason)) return false;
            float duration=Travel(outgoing)+batch.Count/Rate;
            foreach (var previous in journeys) if (previous.transit==transit)
                duration=Mathf.Max(duration,previous.remaining+batch.Count/Rate);
            journeys.Add(new Journey { batch=batch,transit=transit,destination=to,remaining=duration });
            return true;
        }
    }
}
