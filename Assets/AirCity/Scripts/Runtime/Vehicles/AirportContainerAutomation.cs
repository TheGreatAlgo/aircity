using System;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    public sealed partial class AirportBaggage
    {
        public ContainerStation[] ContainerStations => airport.GetComponentsInChildren<ContainerStation>();
        public ContainerTurnaround ContainerJobOf(Aircraft.Aircraft aircraft) => containerJobs.FindLast(j => j.Aircraft == aircraft);

        void EndDepartedVisits()
        {
            foreach (var job in jobs)
                if (!job.VisitEnded && job.DepartureReady && !OnStand(job.Aircraft, job.Stand)) job.VisitEnded = true;
            foreach (var job in containerJobs)
                if (!job.VisitEnded && job.DepartureReady && !OnStand(job.Aircraft, job.Stand)) job.VisitEnded = true;
        }

        ContainerStation AutomaticContainerStation(Aircraft.Aircraft aircraft, ContainerStation[] stations, out Stand stand)
        {
            stand = null;
            if (aircraft == null || aircraft.type == null) return null;
            foreach (var station in stations)
            {
                if (!station.automaticHandling || station.automaticHold?.aircraft != aircraft.type) continue;
                foreach (int id in station.standIds)
                {
                    var candidate = airport.Taxiways.stands.Find(s => s.id == id);
                    if (candidate != null && OnStand(aircraft, candidate)) { stand = candidate; return station; }
                }
            }
            return null;
        }

        /// <summary>Validate a whole visit before creating any units or bag records. Gross limits include tare.</summary>
        public string ContainerDemandProblem(ContainerStation station, out int loadSize)
        {
            loadSize = 0;
            if (station == null || station.Airport != airport || station.automaticHold == null) return "configure a container hold at this airport";
            if (!station.automaticHold.Resolve(station.unitType, station.dollyType, station.loaderType, out _, out var reason)) return reason;
            bool Endpoint(int id) => airport.Taxiways.Node(id) != null && airport.Taxiways.edges.Exists(e =>
                e.road == RoadClass.Service && (e.a == id || e.b == id));
            if (!Endpoint(station.dispatchNode) || !Endpoint(station.arrivalsNode)) return "place dispatch and arrivals on service-road junctions";
            if (station.incomingBags < 0 || station.outgoingBags < 0 || station.bagsPerContainer < 1 ||
                !BaggageLedger.Finite(station.kilogramsPerBag) || station.kilogramsPerBag <= 0f)
                return "container demand needs nonnegative bag counts and a positive bag weight and load size";
            var unit = station.unitType.container;
            if (!BaggageLedger.Finite(unit.tare) || !BaggageLedger.Finite(unit.maxGross) || unit.tare <= 0f || unit.maxGross <= unit.tare)
                return "container needs valid tare and gross capacities";
            loadSize = Mathf.Min(station.bagsPerContainer, Mathf.FloorToInt((unit.maxGross - unit.tare) / station.kilogramsPerBag));
            if (loadSize < 1) return "a bag exceeds container payload";
            int capacity = loadSize;
            int Units(int bags) => bags == 0 ? 0 : 1 + (bags - 1) / capacity;
            int incoming = Units(station.incomingBags), outgoing = Units(station.outgoingBags);
            var hold = station.automaticHold;
            if (incoming > hold.slots || outgoing > hold.slots ||
                (double)station.incomingBags * station.kilogramsPerBag + incoming * unit.tare > hold.maxGross ||
                (double)station.outgoingBags * station.kilogramsPerBag + outgoing * unit.tare > hold.maxGross)
                return "container demand exceeds hold slots or gross capacity";
            if (station.hallSlots < 1 || !BaggageLedger.Finite(station.hallGrossCapacity) || station.hallGrossCapacity <= 0f ||
                station.arrivalsBagCapacity < 1 || !BaggageLedger.Finite(station.arrivalsBagWeight) || station.arrivalsBagWeight <= 0f)
                return "container hall and arrivals need positive capacities";
            if (station.bulkStation != null && (station.bulkStation.Airport != airport ||
                station.bulkStation.automaticHold?.cargoPoint == hold.cargoPoint))
                return "companion bulk station needs a separate cargo point at this airport";
            if (station.bulkStation != null && (!station.bulkStation.isActiveAndEnabled || !station.bulkStation.automaticHandling ||
                station.bulkStation.automaticHold?.aircraft != hold.aircraft ||
                station.standIds.Exists(id => !station.bulkStation.standIds.Contains(id))))
                return "enable the companion bulk station for the same aircraft and assigned stands";
            return null;
        }

        void PrepareAutomaticContainers(Aircraft.Aircraft aircraft, ContainerStation[] stations)
        {
            var previous = ContainerJobOf(aircraft);
            if (previous != null && !previous.VisitEnded) return;
            var station = AutomaticContainerStation(aircraft, stations, out var stand);
            if (station == null) return;
            station.Problem = ContainerDemandProblem(station, out int loadSize);
            if (station.Problem != null) return;
            var fleet = ContainerFleet(station);
            if (fleet == null) { station.Problem = "no compatible container equipment"; return; }
            int units = station.outgoingBags == 0 ? 0 : 1 + (station.outgoingBags - 1) / loadSize;
            double gross = (double)station.outgoingBags * station.kilogramsPerBag + units * station.unitType.container.tare;
            if ((long)fleet.Dispatch.Count + units > fleet.Dispatch.Capacity || fleet.Dispatch.Weight + gross > fleet.Dispatch.WeightCapacity)
            { station.Problem = "container dispatch is full; waiting to prepare the next manifest"; return; }
            string visit = "CT" + (++nextManifest).ToString("0000");
            var job = RequestContainers(aircraft, stand, station.automaticHold, station, visit + "-IN", visit + "-OUT", out var reason);
            if (job == null) { station.Problem = reason; return; }
            void Pack(int bags, string flight, ContainerBay bay, bool screened)
            {
                for (int n = 0; n < bags;)
                {
                    int count = Mathf.Min(loadSize, bags - n);
                    var unit = Containers.Create(flight + "-ULD-" + n, flight, station.unitType, loadSize, bay);
                    for (int b = 0; b < count; b += 10)
                    {
                        int batch = Mathf.Min(10, count - b);
                        Ledger.Add(unit.Id + "-" + b, flight, batch, batch * station.kilogramsPerBag, screened, unit.Contents);
                    }
                    unit.Seal(); n += count;
                }
            }
            Pack(station.incomingBags, visit + "-IN", job.Hold, true);
            Pack(station.outgoingBags, visit + "-OUT", fleet.Dispatch, station.outgoingScreened);
            if (!job.Seal(out reason)) throw new InvalidOperationException(reason);
            station.Problem = null;
        }

        public void ScreenDepartures(ContainerTurnaround job)
        {
            if (job == null || !containerJobs.Contains(job)) return;
            foreach (var batch in Ledger.Batches) if (batch.Flight == job.DepartureFlight) Ledger.Screen(batch);
        }

        public void SendToReclaim(ContainerStation station)
        {
            var fleet = ContainerFleet(station);
            if (fleet == null) return;
            foreach (var batch in Ledger.Batches)
                if (batch.Location == fleet.ArrivalsBags) Ledger.Transfer(batch, fleet.ArrivalsBags, Reclaim, out _);
        }

        public void RefreshParking(ContainerStation station)
        {
            if (containerFleets.TryGetValue(station, out var fleet) && !containerJobs.Exists(j => j.Fleet == fleet && !j.Finished)) fleet.ResetParking();
        }
    }
}
