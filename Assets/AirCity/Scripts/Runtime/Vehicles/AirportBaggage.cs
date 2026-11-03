using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Traffic;
using UnityEngine;
using Box = AirCity.Vehicles.AirportMaintenance.Box;

namespace AirCity.Vehicles
{
    public sealed class BaggageFleet
    {
        public readonly BaggageStation Station;
        public readonly BaggageEquipmentSet Equipment;
        public readonly BaggageStore Dispatch, Arrivals, Cart;
        public BaggageTurnaround Job { get; internal set; }
        public BaggageTrain Train { get; internal set; }
        public BaggageVehiclePose LoaderPose { get; internal set; }
        public float LoaderHeight { get; internal set; }
        public float TrainSpeed { get; internal set; }
        public float LoaderSpeed { get; internal set; }

        internal BaggageFleet(BaggageStation station, BaggageLedger ledger)
        {
            Station = station;
            Equipment = new BaggageEquipmentSet(station.tractorType, station.cartType, station.loaderType);
            Dispatch = ledger.Store(station.name + " dispatch", station.dispatchCapacity, station.dispatchWeight);
            Arrivals = ledger.Store(station.name + " arrivals", station.arrivalsCapacity, station.arrivalsWeight);
            Cart = ledger.Store(station.name + " cart", 100, station.cartType.baggage.payload);
            ResetParking();
        }

        internal void ResetParking()
        {
            LoaderPose = new BaggageVehiclePose(Station.loaderParking, BaggageServicePlan.Facing(Station.loaderParkingBearing));
            LoaderHeight = Station.loaderType.baggage.frontMinHeight;
            var net = Station.Airport.Taxiways;
            if (net.Node(Station.dispatchNode) != null)
            {
                var path = new VehiclePath(); path.Start(net.At(Station.dispatchNode), 1f);
                path.LineTo(net.At(Station.dispatchNode) + BaggageServicePlan.Facing(Station.dispatchBearing), 1f);
                Train = new BaggageTrain(Station.tractorType, Station.cartType, path);
            }
        }
    }

    /// <summary>Airport-owned manifests, hall queues, equipment leases, stand reservations and departure readiness.</summary>
    public sealed partial class AirportBaggage
    {
        readonly Airport airport;
        readonly List<BaggageTurnaround> jobs = new List<BaggageTurnaround>();
        readonly Dictionary<BaggageStation, BaggageFleet> fleets = new Dictionary<BaggageStation, BaggageFleet>();
        readonly List<Box> liveObstacles = new List<Box>();
        double accumulator;
        int nextManifest;
        public BaggageLedger Ledger { get; } = new BaggageLedger();
        public BaggageStore Reclaim { get; }
        public IReadOnlyList<BaggageTurnaround> Jobs => jobs;
        public bool Active => jobs.Exists(j => !j.Finished) || containerJobs.Exists(j => !j.Finished);
        public BaggageStation[] Stations => airport.GetComponentsInChildren<BaggageStation>();
        public bool NeedsSimulation => Active || Array.Exists(Stations, s => s.automaticHandling) || Array.Exists(ContainerStations, s => s.automaticHandling);
        public AirportBaggage(Airport airport)
        {
            this.airport = airport;
            Reclaim = Ledger.Store("Reclaim", int.MaxValue, float.MaxValue);
        }

        public BaggageTurnaround JobOf(Aircraft.Aircraft aircraft) => jobs.FindLast(j => j.Aircraft == aircraft);
        public void RefreshParking(BaggageStation station)
        {
            if (fleets.TryGetValue(station, out var fleet) && !jobs.Exists(j => j.Fleet == fleet && !j.Finished)) fleet.ResetParking();
        }

        bool OnStand(Aircraft.Aircraft aircraft, Stand stand)
        {
            if (aircraft == null || aircraft.type == null || !aircraft.isActiveAndEnabled || aircraft.Moving) return false;
            var layout = airport.Taxiways.Layout(stand);
            if (layout == null || layout.edge == null || stand.DeIcing || !layout.ParkingFits(aircraft.type) || stand.ServiceFor(aircraft.type)?.allowed == false) return false;
            var p = airport.transform.InverseTransformPoint(aircraft.transform.position);
            var facing = BaggageServicePlan.XZ(airport.transform.InverseTransformDirection(aircraft.transform.forward));
            return Mathf.Abs(p.y) < 0.5f && Vector2.Distance(BaggageServicePlan.XZ(p), layout.ParkPosition(aircraft.type)) < 1.5f &&
                Vector2.Angle(facing, layout.nose) < 6f;
        }

        BaggageStation AutomaticStation(Aircraft.Aircraft aircraft, BaggageStation[] stations, out Stand stand)
        {
            stand = null;
            if (aircraft == null || aircraft.type == null) return null;
            foreach (var station in stations)
            {
                if (!station.automaticHandling || station.automaticHold == null || station.automaticHold.aircraft != aircraft.type) continue;
                foreach (int id in station.standIds)
                {
                    var candidate = airport.Taxiways.stands.Find(s => s.id == id);
                    if (candidate != null && OnStand(aircraft, candidate)) { stand = candidate; return station; }
                }
            }
            return null;
        }

        /// <summary>Demand belongs to an aircraft visit, never to a rendered review or a departure button.</summary>
        public void PrepareAutomatic(Aircraft.Aircraft aircraft)
        {
            EndDepartedVisits();
            PrepareAutomatic(aircraft, Stations, ContainerStations);
        }

        void PrepareAutomatic(Aircraft.Aircraft aircraft, BaggageStation[] stations, ContainerStation[] containerStations)
        {
            PrepareAutomaticContainers(aircraft, containerStations);
            var previous = JobOf(aircraft);
            if (previous != null && !previous.VisitEnded) return;
            var station = AutomaticStation(aircraft, stations, out var stand);
            if (station == null) return;
            var containerStation = Array.Find(containerStations, s => s.automaticHandling && s.bulkStation == station &&
                s.automaticHold?.aircraft == aircraft.type && s.standIds.Contains(stand.id));
            if (containerStation != null && (ContainerJobOf(aircraft) == null || ContainerJobOf(aircraft).VisitEnded))
            { station.Problem = "waiting for container manifest: " + containerStation.Problem; return; }
            var h = station.automaticHold;
            if (!h.Resolve(station.loaderType, out _, out var reason) || station.incomingBags < 0 || station.outgoingBags < 0 ||
                !BaggageLedger.Finite(station.kilogramsPerBag) || station.kilogramsPerBag <= 0f ||
                station.incomingBags > h.bagCapacity || station.outgoingBags > h.bagCapacity ||
                station.incomingBags * station.kilogramsPerBag > h.weightCapacity || station.outgoingBags * station.kilogramsPerBag > h.weightCapacity)
            { station.Problem = reason ?? "bag demand exceeds the configured hold"; return; }
            var fleet = Fleet(station);
            if (fleet == null) { station.Problem = "no compatible baggage equipment"; return; }
            if (fleet.Dispatch.Count + station.outgoingBags > fleet.Dispatch.Capacity ||
                fleet.Dispatch.Weight + station.outgoingBags * station.kilogramsPerBag > fleet.Dispatch.WeightCapacity)
            { station.Problem = "dispatch is full; waiting to prepare the next manifest"; return; }
            int batchSize = Mathf.Min(10, fleet.Cart.Capacity, Mathf.FloorToInt(fleet.Cart.WeightCapacity / station.kilogramsPerBag));
            if (batchSize < 1) { station.Problem = "a bag exceeds cart payload"; return; }
            string visit = "BG" + (++nextManifest).ToString("0000");
            var containerJob = containerStation != null ? ContainerJobOf(aircraft) : null;
            string arrivalFlight = containerJob?.ArrivalFlight ?? visit + "-IN";
            string departureFlight = containerJob?.DepartureFlight ?? visit + "-OUT";
            var job = Request(aircraft, stand, h, station, arrivalFlight, departureFlight, out reason);
            if (job == null) { station.Problem = reason; return; }
            void Add(int count, string flight, BaggageStore store)
            {
                for (int n = 0; n < count; n += batchSize)
                {
                    int bags = Mathf.Min(batchSize, count - n);
                    Ledger.Add(flight + "-" + n, flight, bags, bags * station.kilogramsPerBag, true, store);
                }
            }
            Add(station.incomingBags, arrivalFlight, job.Hold);
            Add(station.outgoingBags, departureFlight, fleet.Dispatch);
            if (!job.Seal(out reason)) throw new InvalidOperationException(reason);
            station.Problem = null;
        }

        public void ScreenDepartures(BaggageTurnaround job)
        {
            if (job == null || !jobs.Contains(job)) return;
            foreach (var batch in Ledger.Batches) if (batch.Flight == job.DepartureFlight) Ledger.Screen(batch);
        }

        public void SendToReclaim(BaggageStation station)
        {
            var fleet = Fleet(station);
            if (fleet == null) return;
            foreach (var batch in Ledger.Batches)
                if (batch.Location == fleet.Arrivals) Ledger.Transfer(batch, fleet.Arrivals, Reclaim, out _);
        }

        public BaggageFleet Fleet(BaggageStation station)
        {
            if (station == null || station.Airport != airport) return null;
            if (fleets.TryGetValue(station, out var fleet)) return fleet;
            if (station.tractorType == null || station.cartType == null || station.loaderType == null ||
                station.tractorType.baggage == null || station.cartType.baggage == null || station.loaderType.baggage == null) return null;
            var equipment = new BaggageEquipmentSet(station.tractorType, station.cartType, station.loaderType);
            var check = new object();
            if (!equipment.Reserve(check, out _)) return null;
            fleet = new BaggageFleet(station, Ledger); fleets.Add(station, fleet);
            return fleet;
        }

        /// <summary>Queue before arrival or while parked. Populate Hold/Dispatch, then Seal the manifests.</summary>
        public BaggageTurnaround Request(Aircraft.Aircraft aircraft, Stand stand, BulkHoldConfiguration hold,
            BaggageStation station, string arrivalFlight, string departureFlight, out string reason)
        {
            reason = null;
            if (aircraft == null || aircraft.type == null || stand == null || !airport.Taxiways.stands.Contains(stand) ||
                hold == null || hold.aircraft != aircraft.type || string.IsNullOrWhiteSpace(arrivalFlight) ||
                string.IsNullOrWhiteSpace(departureFlight) || arrivalFlight == departureFlight)
                reason = "turnaround needs an aircraft, stand, explicit hold and distinct arrival/departure flights";
            else if (jobs.Exists(j => j.Aircraft == aircraft && !j.Finished)) reason = "aircraft already has a baggage turnaround";
            else if (jobs.Exists(j => !j.VisitEnded && j.Aircraft == aircraft && j.Finished && j.Hold.Count > 0))
                reason = "previous turnaround still has baggage in its hold; recover its manifest before replacing it";
            else if (jobs.Exists(j => !j.Finished && j.Fleet.Station == station && j.DepartureFlight == departureFlight))
                reason = "departure baggage manifest is already assigned to a turnaround";
            else if (containerJobs.Exists(j => !j.VisitEnded && j.Aircraft == aircraft && j.Configuration.cargoPoint == hold.cargoPoint))
                reason = "cargo point is already configured as a container hold";
            if (reason != null) return null;
            var flight = airport.Traffic.FlightOf(aircraft);
            if (flight != null && flight.kind == FlightKind.Departure && flight.Phase != FlightPhase.Ready)
            { reason = "aircraft has already started departure; baggage service cannot be dispatched"; return null; }
            var fleet = Fleet(station);
            if (reason == null && fleet == null) reason = "no compatible baggage station equipment";
            if (reason == null && !hold.Resolve(station.loaderType, out _, out reason)) return null;
            if (reason != null) return null;
            var job = new BaggageTurnaround(this, airport, aircraft, stand, hold, fleet, arrivalFlight, departureFlight);
            jobs.Add(job);
            return job;
        }

        public string DepartureBlocker(Aircraft.Aircraft aircraft)
        {
            PrepareAutomatic(aircraft);
            for (int i = jobs.Count - 1; i >= 0; i--)
                if (jobs[i].Aircraft == aircraft && !jobs[i].VisitEnded)
                {
                    if (!jobs[i].DepartureReady) return "waiting for baggage: " + (jobs[i].Waiting ?? jobs[i].State.ToString());
                    break;
                }
            var checkedHolds = new HashSet<string>();
            for (int i = containerJobs.Count - 1; i >= 0; i--)
            {
                var job = containerJobs[i];
                if (job.Aircraft == aircraft && !job.VisitEnded)
                {
                    if (!checkedHolds.Add(job.Configuration.cargoPoint)) continue;
                    if (!job.DepartureReady) return "waiting for containers: " + (job.Waiting ?? job.State.ToString());
                }
            }
            var station = AutomaticStation(aircraft, Stations, out _);
            if (station != null && (JobOf(aircraft) == null || JobOf(aircraft).VisitEnded))
                return "waiting for baggage: " + (station.Problem ?? "preparing manifests");
            var containerStation = AutomaticContainerStation(aircraft, ContainerStations, out _);
            if (containerStation != null && (ContainerJobOf(aircraft) == null || ContainerJobOf(aircraft).VisitEnded))
                return "waiting for containers: " + (containerStation.Problem ?? "preparing manifests");
            if (aircraft != null && !aircraft.CargoDoors.AllClosed) return "waiting for cargo doors to close";
            return null;
        }

        public bool Reserved(Stand stand) => jobs.Exists(j => j.Stand == stand && j.Acquired && !j.Finished) ||
            containerJobs.Exists(j => j.Stand == stand && j.Acquired && !j.Finished);

        internal bool ContainersReady(BaggageTurnaround job)
        {
            foreach (var containers in containerJobs)
                if (!containers.VisitEnded && containers.Aircraft == job.Aircraft && containers.Fleet.Station.bulkStation == job.Fleet.Station && !containers.DepartureReady)
                    return false;
            return true;
        }

        internal bool Start(BaggageTurnaround job, out string reason)
        {
            reason = null;
            var fleet = job.Fleet;
            if (!ContainersReady(job)) { reason = "waiting for container equipment to return"; return false; }
            if (fleet.Equipment.Owner != null) { reason = "waiting for baggage equipment"; return false; }
            if (fleet.Cart.Count != 0) { reason = "baggage cart is occupied"; return false; }
            if (!BaggageServicePlan.Build(airport, job, fleet.Station, out var plan, out reason)) return false;
            foreach (var other in jobs)
                if (other != job && other.Acquired && !other.Finished && plan.reservation.Overlaps(other.Plan.reservation))
                { reason = "stand baggage working space is reserved"; return false; }
            foreach (var other in containerJobs)
                if (other.Acquired && !other.Finished && plan.reservation.Overlaps(other.Plan.reservation))
                { reason = "container working space is reserved"; return false; }
            if (!fleet.Equipment.Reserve(job, out reason)) return false;
            job.Plan = plan;
            if (fleet.Train == null) fleet.Train = new BaggageTrain(fleet.Equipment.Tractor, fleet.Equipment.Cart, plan.toStand);
            else fleet.Train.Continue(plan.toStand);
            fleet.Job = job;
            return true;
        }

        public void Simulate(float seconds)
        {
            if (!BaggageLedger.Finite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            accumulator += seconds;
            while (accumulator + 0.000001 >= 0.05)
            {
                accumulator -= 0.05;
                EndDepartedVisits();
                var stations = Stations; var containerStations = ContainerStations;
                if (Array.Exists(stations, s => s.automaticHandling) || Array.Exists(containerStations, s => s.automaticHandling))
                    foreach (var aircraft in Aircraft.Aircraft.All) PrepareAutomatic(aircraft, stations, containerStations);
                liveObstacles.Clear();
                liveObstacles.AddRange(airport.Maintenance.VehicleBoxes(null));
                foreach (var job in jobs) if (!job.Finished) job.Step(0.05f);
                foreach (var job in containerJobs) if (!job.Finished) job.Step(0.05f);
            }
        }

        internal bool Clear(BaggageTurnaround job, BaggageVehiclePose pose, VehicleType type)
        {
            if (!job.Plan.Clear(pose, type)) return false;
            var box = Box.Of(pose.position, pose.facing, type.bounds, 0.3f);
            if (job.Fleet.Station != null && job.Fleet.Station.accessBlocked && job.Plan.reservation.Contains(box.c)) return false;
            foreach (var obstacle in liveObstacles) if (box.Overlaps(obstacle)) return false;
            foreach (var fleet in fleets.Values)
            {
                if (fleet == job.Fleet)
                {
                    if (type != fleet.Equipment.Loader && box.Overlaps(Box.Of(fleet.LoaderPose.position, fleet.LoaderPose.facing, fleet.Equipment.Loader.bounds, 0.2f))) return false;
                    if (type == fleet.Equipment.Loader && fleet.Train != null &&
                        (box.Overlaps(Box.Of(fleet.Train.Tractor.position, fleet.Train.Tractor.facing, fleet.Equipment.Tractor.bounds, 0.2f)) ||
                         box.Overlaps(Box.Of(fleet.Train.Cart.position, fleet.Train.Cart.facing, fleet.Equipment.Cart.bounds, 0.2f)))) return false;
                    continue;
                }
                foreach (var other in Boxes(fleet)) if (box.Overlaps(other)) return false;
            }
            foreach (var fleet in containerFleets.Values)
                foreach (var other in ContainerBoxes(fleet)) if (box.Overlaps(other)) return false;
            return true;
        }

        static IEnumerable<Box> Boxes(BaggageFleet fleet)
        {
            if (fleet.Train != null)
            {
                yield return Box.Of(fleet.Train.Tractor.position, fleet.Train.Tractor.facing, fleet.Equipment.Tractor.bounds, 0.3f);
                yield return Box.Of(fleet.Train.Cart.position, fleet.Train.Cart.facing, fleet.Equipment.Cart.bounds, 0.3f);
            }
            yield return Box.Of(fleet.LoaderPose.position, fleet.LoaderPose.facing, fleet.Equipment.Loader.bounds, 0.3f);
        }

        public List<Box> VehicleBoxes()
        {
            var list = new List<Box>();
            foreach (var fleet in fleets.Values)
                foreach (var box in Boxes(fleet)) { var b = box; b.what = "baggage equipment"; list.Add(b); }
            foreach (var fleet in containerFleets.Values)
                foreach (var box in ContainerBoxes(fleet)) { var b = box; b.what = "container equipment"; list.Add(b); }
            return list;
        }
    }
}
