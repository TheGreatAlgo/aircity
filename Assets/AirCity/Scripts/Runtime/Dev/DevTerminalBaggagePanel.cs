using System.Linq;
using AirCity.Airfield;
using AirCity.Vehicles;

namespace AirCity.Dev
{
    public partial class DevToolbar
    {
        int terminalHallIndex,baggageTerminalIndex;
        string terminalBaggageMessage;
        void TerminalBaggageInspector(Airport airport,Terminal terminal)
        {
            Section("BAGGAGE");
            var facility=airport.Baggage.TerminalFacility(terminal);
            Note($"Check-in queue {facility.CheckIn.Count} · Reclaimed {facility.Delivered}");
            BaggageFloors(airport,terminal);
            var halls=airport.GetComponentsInChildren<BaggageDepot>().Where(d=>d.isActiveAndEnabled).ToArray();
            if (halls.Length==0) { Note("Build a baggage hall to connect this terminal."); return; }
            int index=(terminalHallIndex%halls.Length+halls.Length)%halls.Length;
            var hall=halls[index];
            PropStepper("Baggage hall",hall.name,()=>terminalHallIndex--,()=>terminalHallIndex++,"Choose the hall that receives this terminal's bags",halls.Length>1,halls.Length>1);
            BaggageConnectionButtons(airport,hall,terminal);
            if (hall.ConnectedTerminal==terminal) BaggageConnectionStatus(airport,hall);
            if (!string.IsNullOrEmpty(terminalBaggageMessage)) Note(terminalBaggageMessage);
        }
        void BaggageFloors(Airport airport,Terminal terminal)
        {
            var facility=airport.Baggage.TerminalFacility(terminal);
            string Name(int floor) => floor==0 ? "Ground" : "Floor "+(floor+1);
            void Set(int checkIn,int reclaim)
            { airport.Baggage.SetTerminalBaggageFloors(terminal,checkIn,reclaim,out terminalBaggageMessage); }
            PropStepper("Check-in floor",Name(facility.CheckInFloor),()=>Set(facility.CheckInFloor-1,facility.ReclaimFloor),
                ()=>Set(facility.CheckInFloor+1,facility.ReclaimFloor),"Conveyor travel includes the height of the check-in floor",facility.CheckInFloor>0,facility.CheckInFloor<terminal.floors-1);
            PropStepper("Reclaim floor",Name(facility.ReclaimFloor),()=>Set(facility.CheckInFloor,facility.ReclaimFloor-1),
                ()=>Set(facility.CheckInFloor,facility.ReclaimFloor+1),"The floor where arriving baggage reaches reclaim",facility.ReclaimFloor>0,facility.ReclaimFloor<terminal.floors-1);
        }
        void DepotTerminalInspector(Airport airport,BaggageDepot hall)
        {
            if (!Fold("baggage-terminal","TERMINAL CONNECTION")) return;
            if (airport.terminals.Count==0) { Note("Build a terminal to connect this hall."); return; }
            int index=(baggageTerminalIndex%airport.terminals.Count+airport.terminals.Count)%airport.terminals.Count;
            var terminal=airport.terminals[index];
            PropStepper("Connect to","Terminal "+(index+1),()=>baggageTerminalIndex--,()=>baggageTerminalIndex++,
                "Choose the terminal supplying this baggage hall",airport.terminals.Count>1,airport.terminals.Count>1);
            BaggageConnectionButtons(airport,hall,terminal);
            BaggageConnectionStatus(airport,hall);
            if (!string.IsNullOrEmpty(terminalBaggageMessage)) Note(terminalBaggageMessage);
        }
        void BaggageConnectionButtons(Airport airport,BaggageDepot hall,Terminal terminal)
        {
            bool available=airport.Baggage.CanChangeTerminalLink(hall,out var reason);
            void Connect(BaggageConnectionKind kind)
            {
                if (airport.Baggage.ConnectTerminal(hall,terminal,kind,out terminalBaggageMessage)) hall.ShowConnection(true);
            }
            Buttons(B("Connect underground",()=>Connect(BaggageConnectionKind.Underground),"Connect the terminal and hall through an abstract underground baggage conveyor",enabled:available),
                B("Connect covered",()=>Connect(BaggageConnectionKind.Covered),"Connect a nearby hall with an enclosed conveyor bridge",enabled:available));
            if (hall.connection.enabled && !string.IsNullOrEmpty(hall.connection.terminalId))
                Buttons(B("Disconnect",()=>airport.Baggage.DisconnectTerminal(hall,out terminalBaggageMessage),
                    "Disconnect after all baggage and handling teams have reached their destinations",enabled:available));
            if (!available) Note(reason);
        }
        void BaggageConnectionStatus(Airport airport,BaggageDepot hall)
        {
            var link=airport.Baggage.TerminalLink(hall);
            if (link==null) { Note(hall.connection.enabled ? "Terminal connection required" : "Independent baggage station"); return; }
            Note($"Terminal {airport.terminals.IndexOf(link.Facility.Terminal)+1} · {hall.connection.kind}");
            Note($"Waiting {link.QueuedDepartures} departures / {link.WaitingArrivals} arrivals · In transit {link.InTransit}");
            Note($"Travel {link.OutboundSeconds:0.0}s out / {link.InboundSeconds:0.0}s in · {link.HorizontalLength:0} m");
            if (link.Waiting!=null) Note(link.Waiting);
            Buttons(B("Show baggage link",()=>hall.ShowConnection(!hall.showConnection),"Show the abstract baggage route between the terminal and hall",hall.showConnection));
            bool canReduce=hall.connection.capacity>36 && link.Outbound.Count<=hall.connection.capacity-36 && link.Inbound.Count<=hall.connection.capacity-36;
            void Configure(int capacity,float rate) => airport.Baggage.ConfigureTerminalLink(hall,capacity,rate,out terminalBaggageMessage);
            PropStepper("Connection capacity",hall.connection.capacity+" bags / direction",()=>Configure(hall.connection.capacity-36,hall.connection.bagsPerMinute),
                ()=>Configure(hall.connection.capacity+36,hall.connection.bagsPerMinute),"Maximum bags on each directional conveyor",canReduce,true);
            PropStepper("Connection rate",hall.connection.bagsPerMinute+" bags/min",()=>Configure(hall.connection.capacity,hall.connection.bagsPerMinute-60),
                ()=>Configure(hall.connection.capacity,hall.connection.bagsPerMinute+60),"Admission rate for each conveyor; powered belts increase effective speed and rate",hall.connection.bagsPerMinute>60,true);
        }
    }
}
