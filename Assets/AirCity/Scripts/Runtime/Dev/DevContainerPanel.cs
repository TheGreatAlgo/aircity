using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevToolbar
    {
        DevContainerControls containers;

        void ContainerSidebar(Airport airport)
        {
            if (containers == null) containers = GetComponent<DevContainerControls>();
            if (containers == null || containers.Station == null || !Fold("containers", "CONTAINERS")) return;
            var station = containers.Station; var baggage = airport.Baggage;
            var fleet = baggage.ContainerFleet(station);
            Note(station.name);
            Buttons(B("View station", containers.FocusStation, "Move the camera to the container station"),
                B("Auto handling", () => station.automaticHandling = !station.automaticHandling,
                    "Prepare container manifests for compatible aircraft on assigned stands", station.automaticHandling));
            Buttons(B("Send to reclaim", () => baggage.SendToReclaim(station), "Free arrivals bag capacity without deleting records"),
                B(station.accessBlocked ? "Clear access" : "Block access", () => station.accessBlocked = !station.accessBlocked, "Pause equipment access and transfers", station.accessBlocked));
            Note($"Dispatch {fleet?.Dispatch.Count ?? 0} ULDs · Arrivals {fleet?.ArrivalsBags.Count ?? 0} bags");
            ContainerTurnaround current = fleet?.Job;
            if (current == null)
                foreach (var job in baggage.ContainerJobs)
                    if (job.Fleet.Station == station && !job.Finished) { current = job; break; }
            if (current != null)
            {
                Note(current.Aircraft?.name + ": " + (current.Waiting ?? current.State.ToString()));
                ContainerActions(baggage, current);
            }
            else
                foreach (var job in baggage.ContainerJobs)
                    if (job.Fleet.Station == station && job.State == ContainerTurnaroundState.Cancelled) ContainerActions(baggage, job);
            Note(station.Problem ?? containers.Message);
            if (!Fold("container-setup", "CONTAINER STATION SETUP")) return;
            bool edit = containers.CanConfigure;
            Buttons(B("Next station", containers.NextStation, "Select the next container station"),
                B("Add station", containers.AddStation, "Create an unassigned station with automatic handling off"));
            var hold = station.automaticHold;
            PropStepper("Aircraft", hold.aircraft?.displayName ?? "Choose aircraft", () => containers.ChangeAircraft(-1),
                () => containers.ChangeAircraft(1), "Explicit aircraft compatibility", edit, edit);
            string cargo = hold.cargoPoint switch { "Cargo_Fwd" => "Forward hold", "Cargo_Aft" => "Aft hold", "Cargo_Bulk" => "Bulk hold", _ => hold.cargoPoint ?? "Choose hold" };
            PropStepper("Cargo hold", cargo, () => containers.ChangeCargoPoint(-1),
                () => containers.ChangeCargoPoint(1), "Use a cargo point compatible with this ULD and loader", edit, edit);
            Note("ULD " + hold.acceptedCode + " · " + (station.unitType?.name ?? "missing equipment"));
            PropStepper("Hold slots", hold.slots.ToString(), () => hold.slots = Mathf.Max(1, hold.slots - 1),
                () => hold.slots++, "Maximum containers in this hold", edit && hold.slots > 1, edit);
            PropStepper("Hold gross kg", hold.maxGross.ToString("0"), () => hold.maxGross = Mathf.Max(100, hold.maxGross - 100),
                () => hold.maxGross += 100, "Combined container and baggage weight", edit && hold.maxGross > 100, edit);
            PropStepper("Arriving bags", station.incomingBags.ToString(), () => station.incomingBags = Mathf.Max(0, station.incomingBags - 10),
                () => station.incomingBags += 10, "Demand for future arriving aircraft", edit && station.incomingBags > 0, edit);
            PropStepper("Departing bags", station.outgoingBags.ToString(), () => station.outgoingBags = Mathf.Max(0, station.outgoingBags - 10),
                () => station.outgoingBags += 10, "Demand for future departures", edit && station.outgoingBags > 0, edit);
            PropStepper("Bags per ULD", station.bagsPerContainer.ToString(), () => station.bagsPerContainer = Mathf.Max(1, station.bagsPerContainer - 10),
                () => station.bagsPerContainer += 10, "Packing limit; gross weight may require smaller loads", edit && station.bagsPerContainer > 1, edit);
            PropStepper("kg per bag", station.kilogramsPerBag.ToString("0.0"), () => station.kilogramsPerBag = Mathf.Max(.5f, station.kilogramsPerBag - .5f),
                () => station.kilogramsPerBag += .5f, "Demand weight per bag", edit && station.kilogramsPerBag > .5f, edit);
            Buttons(B("Screen departures", () => station.outgoingScreened = !station.outgoingScreened,
                "New departing bags start screened; otherwise release their screening from handling controls", station.outgoingScreened, enabled: edit));
            Buttons(B("Bulk station: " + (station.bulkStation?.name ?? "None"), containers.NextBulkStation,
                "Choose a loose-bag station whose automatic jobs wait for containers to finish", enabled: edit));
            Buttons(B("Dispatch", () => containers.BeginPlacement(BaggagePlacement.Dispatch), "Place at a service-road junction", enabled: edit),
                B("Arrivals", () => containers.BeginPlacement(BaggagePlacement.Arrivals), "Place at a service-road junction", enabled: edit),
                B("Loader bay", () => containers.BeginPlacement(BaggagePlacement.Loader), "Place loader parking", enabled: edit));
            PropStepper("Dispatch heading", $"{station.dispatchBearing:000}°", () => containers.Turn(BaggagePlacement.Dispatch, -90),
                () => containers.Turn(BaggagePlacement.Dispatch, 90), "Direction leaving and returning to dispatch", edit, edit);
            PropStepper("Arrivals heading", $"{station.arrivalsBearing:000}°", () => containers.Turn(BaggagePlacement.Arrivals, -90),
                () => containers.Turn(BaggagePlacement.Arrivals, 90), "Direction entering arrivals", edit, edit);
            PropStepper("Loader heading", $"{station.loaderParkingBearing:000}°", () => containers.Turn(BaggagePlacement.Loader, -90),
                () => containers.Turn(BaggagePlacement.Loader, 90), "Loader parking direction", edit, edit);
            var stand = containers.AssignmentStand;
            PropStepper("Assign stand", stand?.Name ?? "None", () => containers.standIndex--, () => containers.standIndex++, "Choose a stand to serve");
            if (stand != null)
                Buttons(B("Serve " + stand.Name, () => containers.ToggleStand(stand), "Assign this stand to the selected station",
                    station.standIds.Contains(stand.id), enabled: edit));
            Note($"{station.standIds.Count} assigned stands");
            Note(baggage.ContainerDemandProblem(station, out int size) ?? $"Packing up to {size} bags per ULD, within gross limits");
            if (!edit) Note("Station setup is locked while handling is unfinished.");
        }

        void ContainerActions(AirportBaggage baggage, ContainerTurnaround job)
        {
            if (!job.Finished)
                Buttons(B("Release screening", () => baggage.ScreenDepartures(job), "Release this departure's screening exceptions"),
                    B("Cancel containers", job.Cancel, "Recover containers and return equipment", danger: true));
            else if (job.State == ContainerTurnaroundState.Cancelled)
                Buttons(B("Retry containers", () => { job.Retry(out _); }, "Resume the same manifest on its original stand"));
        }

        void AircraftContainers(Aircraft.Aircraft aircraft)
        {
            var airport = airfield != null ? airfield.airport : sandbox.Airport;
            var job = airport != null ? airport.Baggage.ContainerJobOf(aircraft) : null;
            if (job == null) return;
            Section("CONTAINERS"); Note(job.Waiting ?? job.State.ToString());
            Note($"Hold {job.Hold.Count} ULDs · {job.Unloaded} unloaded / {job.Loaded} loaded");
            Note($"Dolly {job.Fleet.Dolly.Count} · Main {job.Fleet.Main.Count} · Front {job.Fleet.Front.Count}");
            ContainerActions(airport.Baggage, job);
        }
    }
}
