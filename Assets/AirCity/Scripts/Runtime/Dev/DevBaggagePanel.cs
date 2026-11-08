using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevToolbar
    {
        DevBaggageControls baggage;

        void BaggageSidebar(Airport airport)
        {
            if (baggage == null) baggage = GetComponent<DevBaggageControls>();
            if (baggage == null || baggage.Station == null || !Fold("baggage", "BAGGAGE")) return;
            var station = baggage.Station;
            var fleet = airport.Baggage.Fleet(station);
            var current = fleet?.Job;
            if (current == null)
                foreach (var job in airport.Baggage.Jobs)
                    if (job.Fleet.Station == station && !job.Finished) { current = job; break; }
            if (ShowMore) Note(station.name);
            if (station.depot!=null)
            {
                var depot=station.depot; var limit=depot.Maximum;
                if (ShowMore) Note($"{depot.size} depot · Teams {depot.Teams.Length}/{limit.teams} · Trailers {depot.Trailers}/{limit.trailers}");
                Note($"Workers {depot.hiredWorkers} · {depot.Working} working · {depot.Available} available");
                Note($"Working bays {depot.BusyBays}/{depot.ProcessingBays}");
                Buttons(B("Hire worker",baggage.HireWorker,"Hire a baggage worker for this depot",enabled:depot.hiredWorkers<limit.workers),
                    B("Dismiss worker",baggage.FireWorker,"Dismiss an available worker; assigned crews finish their job",enabled:depot.Available>0));
                if (ShowMore)
                {
                    DepotTerminalInspector(airport,depot);
                    Buttons(B("View interior",()=>depot.SetInterior(!depot.showInterior),"Lift the roof to inspect baggage handling",depot.showInterior));
                    void Upgrade(BaggageDepotUpgrade kind,string title,string help)
                    {
                        bool installed=depot.Has(kind);
                        Buttons(B((installed ? "Remove " : "Install ")+title,()=>
                        { depot.SetUpgrade(kind,!installed,out var why); baggage.SetMessage(why); },help,
                            installed,enabled:depot.Working==0 && (installed || depot.Supports(kind))));
                    }
                    Upgrade(BaggageDepotUpgrade.HandlingAssist,"lifting aids","Lift-assist equipment increases depot handover speed by 30%");
                    Upgrade(BaggageDepotUpgrade.PoweredBelts,"powered belts","Powered conveyor drives move depot bags 50% faster");
                    Upgrade(BaggageDepotUpgrade.AutomaticSorting,"automatic sorting","Medium and Large halls can sort outgoing bags faster into flight loading positions");
                    Upgrade(BaggageDepotUpgrade.BufferStorage,"buffer storage","Additional storage racks double arrivals and departure storage capacity");
                    PropStepper("Crew per team",station.workersPerTeam.ToString(),()=>station.workersPerTeam=Mathf.Max(2,station.workersPerTeam-1),
                    ()=>station.workersPerTeam=Mathf.Min(4,station.workersPerTeam+1),"Up to two cart handlers and two hold handlers; changes apply between batches",station.workersPerTeam>2,station.workersPerTeam<4);
                    PropStepper("Depot size",depot.size.ToString(),()=>baggage.ResizeDepot(-1),()=>baggage.ResizeDepot(1),
                    "Small, Medium and Large depots have different staff, trailer and team capacities",depot.Working==0 && depot.size>BaggageDepotSize.Small,depot.Working==0 && depot.size<BaggageDepotSize.Large);
                }
            }
            Buttons(B("View station", baggage.FocusStation, "Move the camera to this baggage station"),
                B("Auto handling", () => station.automaticHandling = !station.automaticHandling,
                    "Create manifests for the configured aircraft on this station's assigned stands", station.automaticHandling));
            string status=current!=null ? BaggageStatus(current) :
                station.automaticHandling ? "Waiting for aircraft" : "Automatic handling off";
            Note(status);
            var message=station.Problem ?? baggage.Message;
            if (message!=status && message!=current?.Waiting) Note(message);
            if (!ShowMore) return;
            if (station.depot?.connection?.enabled!=true)
                Buttons(B("Send to reclaim", () => airport.Baggage.SendToReclaim(station), "Move arrivals to passenger reclaim and free the arrivals bay"));
            Note($"Dispatch {fleet?.Dispatch.Count ?? 0} · Arrivals {fleet?.Arrivals.Count ?? 0}");
            if (current != null)
            {
                Note(current.Aircraft?.name);
                if (current.Waiting!=status) Note(current.Waiting);
            }
            if (!Fold("baggage-setup", "STATION SETUP")) return;
            Buttons(B("Next station", baggage.NextStation, "Select the next baggage station"),
                B(station.depot!=null ? "Add team" : "Add station", baggage.AddStation, "Add a handling team with its own equipment parking; teams share the depot working bays"));
            Buttons(B("Build depot",()=>baggage.BeginPlacement(BaggagePlacement.Depot),"Place a baggage depot beside a service road"));
            PropStepper("Build size",baggage.depotSize.ToString(),
                ()=>baggage.depotSize=(BaggageDepotSize)Mathf.Max(0,(int)baggage.depotSize-1),
                ()=>baggage.depotSize=(BaggageDepotSize)Mathf.Min(2,(int)baggage.depotSize+1),
                "Size of the next baggage depot",baggage.depotSize>BaggageDepotSize.Small,baggage.depotSize<BaggageDepotSize.Large);
            Buttons(B("Stand staging",()=>baggage.StandParking(!station.parkAtStand),
                "Place equipment beside its assigned stand; bays follow the stand and cargo door",station.parkAtStand,enabled:baggage.CanConfigure));
            if (station.parkAtStand)
                Note("Equipment home: "+(StandEquipmentLayout.Home(airport,station.standIds,station.parkingStandId)?.Name ?? "Assign a stand"));
            PropStepper("Carts per train",station.cartCount.ToString(),
                () => { station.cartCount = Mathf.Max(1,station.cartCount-1); airport.Baggage.RefreshParking(station); },
                () => { if (station.depot==null || station.depot.CanAddTrailer(station,out _)) { station.cartCount++; airport.Baggage.RefreshParking(station); } },
                "Each cart has its own payload and follows its own hitch path",baggage.CanConfigure && station.cartCount > 1,
                baggage.CanConfigure && (station.depot==null || station.depot.Trailers<station.depot.Maximum.trailers && station.cartCount<station.depot.Layout.CartsPerTrain));
            PropStepper("Arriving", station.incomingBags.ToString(), () => station.incomingBags = Mathf.Max(0, station.incomingBags - 10),
                () => station.incomingBags += 10, "Bags on future arriving aircraft", baggage.CanConfigure && station.incomingBags > 0, baggage.CanConfigure);
            PropStepper("Departing", station.outgoingBags.ToString(), () => station.outgoingBags = Mathf.Max(0, station.outgoingBags - 10),
                () => station.outgoingBags += 10, "Bags for future departures", baggage.CanConfigure && station.outgoingBags > 0, baggage.CanConfigure);
            if (station.depot==null)
            {
                Buttons(B("Dispatch", () => baggage.BeginPlacement(BaggagePlacement.Dispatch), "Place dispatch at a service-road junction", enabled: baggage.CanConfigure),
                    B("Arrivals", () => baggage.BeginPlacement(BaggagePlacement.Arrivals), "Place arrivals at a service-road junction", enabled: baggage.CanConfigure),
                    B("Loader bay", () => baggage.BeginPlacement(BaggagePlacement.Loader), "Place manual loader parking on the apron", enabled: baggage.CanConfigure && !station.parkAtStand));
                PropStepper("Dispatch heading", $"{station.dispatchBearing:000}°", () => baggage.Turn(BaggagePlacement.Dispatch, -90f),
                    () => baggage.Turn(BaggagePlacement.Dispatch, 90f), "Direction leaving and returning to dispatch", baggage.CanConfigure, baggage.CanConfigure);
                PropStepper("Arrivals heading", $"{station.arrivalsBearing:000}°", () => baggage.Turn(BaggagePlacement.Arrivals, -90f),
                    () => baggage.Turn(BaggagePlacement.Arrivals, 90f), "Direction entering the arrivals bay", baggage.CanConfigure, baggage.CanConfigure);
                if (!station.parkAtStand)
                    PropStepper("Loader heading", $"{station.loaderParkingBearing:000}°", () => baggage.Turn(BaggagePlacement.Loader, -90f),
                        () => baggage.Turn(BaggagePlacement.Loader, 90f), "Loader parking direction", baggage.CanConfigure, baggage.CanConfigure);
            }
            var stand = baggage.AssignmentStand;
            PropStepper("Assign stand", stand?.Name ?? "None", () => baggage.standIndex--, () => baggage.standIndex++, "Choose a stand to serve");
            if (stand != null && !stand.DeIcing)
                Buttons(B("Serve " + stand.Name, () => baggage.ToggleStand(stand), "Assign this stand to the selected baggage station",
                    station.standIds.Contains(stand.id), enabled: baggage.CanConfigure));
            Note($"{station.standIds.Count} assigned stands");
            if (station.parkAtStand && stand != null && station.standIds.Contains(stand.id))
                Buttons(B("Stage at "+stand.Name,()=>baggage.StageAt(stand),"Use this stand as the equipment home",enabled:baggage.CanConfigure));
        }

        static string BaggageStatus(BaggageTurnaround job)
        {
            var waiting=job.Waiting;
            if (string.IsNullOrEmpty(waiting)) return BaggageStage(job.State);
            if (waiting.StartsWith("opening cargo door:") || waiting=="waiting for the cargo door to open") return "Opening cargo door";
            if (waiting.StartsWith("closing cargo door:")) return "Closing cargo door";
            if (waiting.StartsWith("cargo door rig is missing:")) return "Cargo door unavailable";
            return waiting switch
            {
                "tractor driver walking to the baggage cart" => "Crew moving into position",
                "loader operator entering the hold" => "Crew entering cargo hold",
                "hold handler returning to the loader" or "baggage handler returning to the tractor" => "Crew returning to equipment",
                _ => waiting,
            };
        }

        static string BaggageStage(BaggageTurnaroundState state) => state switch
        {
            BaggageTurnaroundState.WaitingAircraft => "Waiting for aircraft",
            BaggageTurnaroundState.WaitingEquipment => "Waiting for equipment",
            BaggageTurnaroundState.LoaderApproach or BaggageTurnaroundState.Docking => "Preparing aircraft",
            BaggageTurnaroundState.InboundDrive => "Driving to aircraft",
            BaggageTurnaroundState.UnloadingHold => "Unloading aircraft",
            BaggageTurnaroundState.ArrivalsDrive => "Taking bags to depot",
            BaggageTurnaroundState.UnloadingArrivals => "Unloading at depot",
            BaggageTurnaroundState.DispatchDrive or BaggageTurnaroundState.CollectingDepartures => "Collecting departure bags",
            BaggageTurnaroundState.OutboundDrive => "Returning to aircraft",
            BaggageTurnaroundState.LoadingHold => "Loading aircraft",
            BaggageTurnaroundState.ReloadDrive => "Returning for more bags",
            BaggageTurnaroundState.Withdrawing or BaggageTurnaroundState.Undocking or BaggageTurnaroundState.ParkingTrain => "Returning equipment",
            BaggageTurnaroundState.Complete => "Handling complete",
            BaggageTurnaroundState.Cancelled => "Handling cancelled",
            _ => state.ToString(),
        };

        void AircraftBaggage(Aircraft.Aircraft aircraft)
        {
            var airport = airfield != null ? airfield.airport : sandbox.Airport;
            var job = airport != null ? airport.Baggage.JobOf(aircraft) : null;
            if (job == null) return;
            Section("BAGGAGE");
            string status=BaggageStatus(job); Note(status);
            if (!ShowMore) return;
            if (job.Waiting!=status) Note(job.Waiting);
            Note($"Hold {job.Hold.Count} · Cart {job.Fleet.CartCount} · Trips {job.ArrivalTrips} in / {job.DepartureTrips} out");
            if (!job.Finished)
                Buttons(B("Release screening", () => airport.Baggage.ScreenDepartures(job), "Release this departure's screening exceptions"),
                    B("Cancel handling", job.Cancel, "Withdraw equipment and retain the manifest for recovery", danger: true));
            else if (job.State == BaggageTurnaroundState.Cancelled)
                Buttons(B("Retry handling", () => { job.Retry(out _); }, "Resume this manifest with the aircraft on its original stand"));
        }
    }
}
