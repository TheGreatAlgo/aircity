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
            Note(station.name);
            Buttons(B("View station", baggage.FocusStation, "Move the camera to this baggage station"));
            Buttons(B("Auto handling", () => station.automaticHandling = !station.automaticHandling,
                "Create manifests for the configured aircraft on this station's assigned stands", station.automaticHandling),
                B("Send to reclaim", () => airport.Baggage.SendToReclaim(station), "Move arrivals to passenger reclaim and free the arrivals bay"));
            Note($"Dispatch {fleet?.Dispatch.Count ?? 0} · Arrivals {fleet?.Arrivals.Count ?? 0}");
            var current = fleet?.Job;
            if (current == null)
                foreach (var job in airport.Baggage.Jobs)
                    if (job.Fleet.Station == station && !job.Finished) { current = job; break; }
            if (current != null) Note(current.Aircraft?.name + ": " + (current.Waiting ?? current.State.ToString()));
            Note(station.Problem ?? baggage.Message);
            if (!Fold("baggage-setup", "STATION SETUP")) return;
            Buttons(B("Next station", baggage.NextStation, "Select the next baggage station"),
                B("Add station", baggage.AddStation, "Add a station, then place its endpoints and assign stands"));
            PropStepper("Arriving", station.incomingBags.ToString(), () => station.incomingBags = Mathf.Max(0, station.incomingBags - 10),
                () => station.incomingBags += 10, "Bags on future arriving aircraft", baggage.CanConfigure && station.incomingBags > 0, baggage.CanConfigure);
            PropStepper("Departing", station.outgoingBags.ToString(), () => station.outgoingBags = Mathf.Max(0, station.outgoingBags - 10),
                () => station.outgoingBags += 10, "Bags for future departures", baggage.CanConfigure && station.outgoingBags > 0, baggage.CanConfigure);
            Buttons(B("Dispatch", () => baggage.BeginPlacement(BaggagePlacement.Dispatch), "Place dispatch at a service-road junction", enabled: baggage.CanConfigure),
                B("Arrivals", () => baggage.BeginPlacement(BaggagePlacement.Arrivals), "Place arrivals at a service-road junction", enabled: baggage.CanConfigure),
                B("Loader bay", () => baggage.BeginPlacement(BaggagePlacement.Loader), "Place loader parking on the apron", enabled: baggage.CanConfigure));
            PropStepper("Dispatch heading", $"{station.dispatchBearing:000}°", () => baggage.Turn(BaggagePlacement.Dispatch, -90f),
                () => baggage.Turn(BaggagePlacement.Dispatch, 90f), "Direction leaving and returning to dispatch", baggage.CanConfigure, baggage.CanConfigure);
            PropStepper("Arrivals heading", $"{station.arrivalsBearing:000}°", () => baggage.Turn(BaggagePlacement.Arrivals, -90f),
                () => baggage.Turn(BaggagePlacement.Arrivals, 90f), "Direction entering the arrivals bay", baggage.CanConfigure, baggage.CanConfigure);
            PropStepper("Loader heading", $"{station.loaderParkingBearing:000}°", () => baggage.Turn(BaggagePlacement.Loader, -90f),
                () => baggage.Turn(BaggagePlacement.Loader, 90f), "Loader parking direction", baggage.CanConfigure, baggage.CanConfigure);
            var stand = baggage.AssignmentStand;
            PropStepper("Assign stand", stand?.Name ?? "None", () => baggage.standIndex--, () => baggage.standIndex++, "Choose a stand to serve");
            if (stand != null && !stand.DeIcing)
                Buttons(B("Serve " + stand.Name, () => baggage.ToggleStand(stand), "Assign this stand to the selected baggage station",
                    station.standIds.Contains(stand.id), enabled: baggage.CanConfigure));
            Note($"{station.standIds.Count} assigned stands");
        }

        void AircraftBaggage(Aircraft.Aircraft aircraft)
        {
            var airport = airfield != null ? airfield.airport : sandbox.Airport;
            var job = airport != null ? airport.Baggage.JobOf(aircraft) : null;
            if (job == null) return;
            Section("BAGGAGE");
            Note(job.Waiting ?? job.State.ToString());
            Note($"Hold {job.Hold.Count} · Cart {job.Fleet.Cart.Count} · Trips {job.ArrivalTrips} in / {job.DepartureTrips} out");
            if (!job.Finished)
                Buttons(B("Release screening", () => airport.Baggage.ScreenDepartures(job), "Release this departure's screening exceptions"),
                    B("Cancel handling", job.Cancel, "Withdraw equipment and retain the manifest for recovery", danger: true));
            else if (job.State == BaggageTurnaroundState.Cancelled)
                Buttons(B("Retry handling", () => { job.Retry(out _); }, "Resume this manifest with the aircraft on its original stand"));
        }
    }
}
