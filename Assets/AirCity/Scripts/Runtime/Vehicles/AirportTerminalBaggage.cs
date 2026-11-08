using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    public sealed partial class AirportBaggage
    {
        readonly Dictionary<string,TerminalBaggageFacility> terminalFacilities=new Dictionary<string,TerminalBaggageFacility>();
        readonly Dictionary<BaggageDepot,TerminalBaggageLink> terminalLinks=new Dictionary<BaggageDepot,TerminalBaggageLink>();
        readonly HashSet<BaggageStore> arrivalDestinations=new HashSet<BaggageStore>();
        public string TerminalMessage { get; private set; }
        public IEnumerable<TerminalBaggageLink> TerminalLinks => terminalLinks.Values;
        public bool TerminalLinksActive => terminalLinks.Values.Any(l=>l.Pending);

        public TerminalBaggageFacility TerminalFacility(Terminal terminal)
        {
            if (terminal==null || !airport.terminals.Contains(terminal)) return null;
            terminal.baggage ??= new TerminalBaggageSettings();
            if (string.IsNullOrEmpty(terminal.baggageId)) terminal.baggageId=Guid.NewGuid().ToString("N");
            if (!terminalFacilities.TryGetValue(terminal.baggageId,out var facility))
            {
                facility=new TerminalBaggageFacility(terminal,Ledger,"Terminal "+(airport.terminals.IndexOf(terminal)+1));
                terminalFacilities.Add(terminal.baggageId,facility); arrivalDestinations.Add(facility.Reclaim);
            }
            return facility;
        }
        public Terminal ConnectedTerminal(BaggageDepot depot) => depot?.connection?.enabled==true ?
            airport.terminals.FirstOrDefault(t=>t!=null && t.baggageId==depot.connection.terminalId) : null;
        public TerminalBaggageLink TerminalLink(BaggageDepot depot)
        {
            if (depot==null || depot.connection==null || !depot.connection.enabled) return null;
            var terminal=ConnectedTerminal(depot); if (terminal==null) return null;
            if (terminalLinks.TryGetValue(depot,out var link)) return link;
            link=new TerminalBaggageLink(depot,TerminalFacility(terminal),Ledger); terminalLinks.Add(depot,link);
            arrivalDestinations.Add(link.Inbound); return link;
        }
        internal bool DepartureSupply(BaggageFleet fleet,out BaggageStore source,out string reason)
        {
            reason=null; source=fleet.Dispatch;
            var depot=fleet.Station.depot;
            if (depot?.connection?.enabled!=true) return true; // Existing independent review stations remain usable.
            var link=TerminalLink(depot);
            if (link==null) { reason="Connect this baggage hall to a terminal before accepting baggage."; return false; }
            source=link.Facility.CheckIn; return true;
        }
        internal void QueueTerminalDeparture(BaggageTurnaround job,BaggageBatch batch)
        {
            if (batch.Location==job.DepartureSource && job.DepartureSource!=job.Fleet.Dispatch)
                TerminalLink(job.Fleet.Station.depot)?.Enqueue(batch);
        }
        internal bool PendingTerminalDeparture(BaggageTurnaround job,BaggageBatch batch)
        {
            if (job.DepartureSource==job.Fleet.Dispatch) return false;
            var link=TerminalLink(job.Fleet.Station.depot);
            return link!=null && link.IsSupply(batch);
        }
        internal bool IsArrivalDestination(BaggageStore location) => location==Reclaim || arrivalDestinations.Contains(location);
        void StepTerminalLinks(float seconds)
        { foreach (var link in terminalLinks.Values) link.Step(seconds); }

        public bool CanChangeTerminalLink(BaggageDepot depot,out string reason)
        {
            reason=null;
            if (depot==null || depot.Airport!=airport) { reason="Choose a baggage hall at this airport."; return false; }
            if (jobs.Any(j=>j.Fleet.Station.depot==depot && !j.Finished) || depot.Working>0)
            { reason="Wait for this hall's handling jobs to finish before changing its terminal connection."; return false; }
            if (terminalLinks.TryGetValue(depot,out var link) && link.Pending)
            { reason="Wait for baggage in this connection to reach its destination."; return false; }
            return true;
        }
        public bool ConnectTerminal(BaggageDepot depot,Terminal terminal,BaggageConnectionKind kind,out string reason)
        {
            reason=null;
            if (terminal==null || !airport.terminals.Contains(terminal)) { reason="Choose a terminal at this airport."; return false; }
            if (!CanChangeTerminalLink(depot,out reason)) { TerminalMessage=reason; return false; }
            var facility=TerminalFacility(terminal); var previous=depot.connection;
            var settings=new BaggageTerminalConnection { enabled=true,terminalId=terminal.baggageId,kind=kind,
                capacity=Mathf.Max(36,previous?.capacity ?? 144),bagsPerMinute=previous?.bagsPerMinute ?? 300,
                beltSpeed=previous?.beltSpeed ?? 3,depth=previous?.depth ?? 5 };
            depot.connection=settings; terminalLinks.Remove(depot);
            var link=new TerminalBaggageLink(depot,facility,Ledger);
            if (kind==BaggageConnectionKind.Covered && !CoveredLinkFits(link,out reason))
            {
                depot.connection=previous;
                TerminalMessage=reason; return false;
            }
            terminalLinks.Add(depot,link); arrivalDestinations.Add(link.Inbound);
            depot.RefreshTerminalPresentation(); TerminalMessage=null; return true;
        }
        bool CoveredLinkFits(TerminalBaggageLink link,out string reason)
        {
            reason=null;
            if (link.HorizontalLength>80)
            { reason="Covered baggage links need a hall within 80 m of the terminal; use an underground connection for this distance."; return false; }
            var start=new Vector2(link.HallPort.x,link.HallPort.z); var end3=link.TerminalPort(true); var end=new Vector2(end3.x,end3.z);
            if (Vector2.Dot((end-start).normalized,link.Depot.Right)<.3f)
            { reason="A covered link needs the terminal beside the hall's machinery wall; use underground for this layout."; return false; }
            var net=airport.Taxiways;
            for (float d=2;d<link.HorizontalLength-1;d+=1)
            {
                var at=Vector2.Lerp(start,end,d/Mathf.Max(1,link.HorizontalLength));
                foreach (var stand in net.Layouts())
                    if (stand.Contains(at)) { reason="The covered baggage link would cross an aircraft stand."; return false; }
                foreach (var edge in net.edges)
                {
                    if (edge.IsRoad) continue;
                    var a=net.At(edge.a); var b=net.At(edge.b); var delta=b-a;
                    var closest=a+delta*Mathf.Clamp01(Vector2.Dot(at-a,delta)/Mathf.Max(.01f,delta.sqrMagnitude));
                    if (Vector2.Distance(at,closest)<edge.width*.5f+1.3f)
                    { reason="The covered baggage link would cross a taxiway; use an underground connection."; return false; }
                }
                if (airport.terminals.Any(t=>t!=link.Facility.Terminal && t.ContainsAtHeight(at,6.6f)))
                { reason="Another terminal blocks the covered baggage link."; return false; }
            }
            return true;
        }
        public bool DisconnectTerminal(BaggageDepot depot,out string reason)
        {
            if (!CanChangeTerminalLink(depot,out reason)) { TerminalMessage=reason; return false; }
            terminalLinks.Remove(depot);
            depot.connection.enabled=true; depot.connection.terminalId=null;
            depot.RefreshTerminalPresentation(); TerminalMessage=null; return true;
        }
        public bool ConfigureTerminalLink(BaggageDepot depot,int capacity,float bagsPerMinute,out string reason)
        {
            reason=null;
            if (depot==null || depot.Airport!=airport || capacity<36 || !BaggageLedger.Finite(bagsPerMinute) || bagsPerMinute<=0)
            { reason="A baggage link needs capacity for at least 36 bags and a positive handling rate."; return false; }
            if (terminalLinks.TryGetValue(depot,out var existing) && (existing.Outbound.Count>capacity || existing.Inbound.Count>capacity))
            { reason="The new connection capacity cannot be smaller than the baggage already in transit."; TerminalMessage=reason; return false; }
            depot.connection.capacity=capacity; depot.connection.bagsPerMinute=bagsPerMinute; TerminalMessage=null; return true;
        }
        public bool SetTerminalBaggageFloors(Terminal terminal,int checkIn,int reclaim,out string reason)
        {
            reason=null;
            if (terminal==null || !airport.terminals.Contains(terminal) || checkIn<0 || reclaim<0 || checkIn>=terminal.floors || reclaim>=terminal.floors)
            { reason="Choose baggage floors within this terminal."; TerminalMessage=reason; return false; }
            terminal.baggage ??= new TerminalBaggageSettings();
            terminal.baggage.checkInFloor=checkIn; terminal.baggage.reclaimFloor=reclaim;
            foreach (var link in terminalLinks.Values) if (link.Facility.Terminal==terminal) link.Depot.RefreshTerminalConnection();
            TerminalMessage=null; return true;
        }
        public bool CanRemoveTerminal(Terminal terminal,out string reason)
        {
            foreach (var depot in airport.GetComponentsInChildren<BaggageDepot>())
                if (ConnectedTerminal(depot)==terminal && !CanChangeTerminalLink(depot,out reason)) return false;
            reason=null; return true;
        }
        internal bool RemoveTerminalBaggage(Terminal terminal)
        {
            if (!CanRemoveTerminal(terminal,out var reason)) { TerminalMessage=reason; return false; }
            foreach (var depot in airport.GetComponentsInChildren<BaggageDepot>())
                if (ConnectedTerminal(depot)==terminal) DisconnectTerminal(depot,out _);
            return true;
        }
    }
}
