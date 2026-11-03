using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Traffic;
using UnityEngine;
using Box = AirCity.Vehicles.AirportMaintenance.Box;

namespace AirCity.Vehicles
{
    public sealed partial class AirportBaggage
    {
        readonly Dictionary<ContainerStation, ContainerFleet> containerFleets = new Dictionary<ContainerStation, ContainerFleet>();
        readonly List<ContainerTurnaround> containerJobs = new List<ContainerTurnaround>();
        ContainerLedger containers;
        public ContainerLedger Containers => containers ??= new ContainerLedger(Ledger);
        public IReadOnlyList<ContainerTurnaround> ContainerJobs => containerJobs;

        public ContainerFleet ContainerFleet(ContainerStation station)
        {
            if (station == null || station.Airport != airport) return null;
            if (containerFleets.TryGetValue(station, out var fleet)) return fleet;
            if (airport.Taxiways.Node(station.dispatchNode) == null || airport.Taxiways.Node(station.arrivalsNode) == null) return null;
            if (station.tractorType == null || station.tractorType.kind != VehicleKind.BaggageTractor ||
                station.dollyType == null || station.dollyType.kind != VehicleKind.ContainerDolly || station.dollyType.baggage == null || station.dollyType.container == null ||
                station.loaderType == null || station.loaderType.kind != VehicleKind.ContainerLoader || station.loaderType.container == null ||
                station.unitType == null || station.unitType.kind != VehicleKind.ContainerULD || station.unitType.container == null) return null;
            fleet = new ContainerFleet(station, Containers); containerFleets.Add(station, fleet); return fleet;
        }
        public ContainerTurnaround RequestContainers(Aircraft.Aircraft aircraft, Stand stand, ContainerHoldConfiguration hold,
            ContainerStation station, string arrival, string departure, out string reason)
        {
            reason = null;
            if (aircraft == null || aircraft.type == null || stand == null || !airport.Taxiways.stands.Contains(stand) ||
                hold == null || hold.aircraft != aircraft.type || string.IsNullOrWhiteSpace(arrival) || string.IsNullOrWhiteSpace(departure) || arrival == departure)
            { reason = "container turnaround needs an aircraft, stand, explicit hold and distinct flights"; return null; }
            foreach (var job in containerJobs)
                if (!job.VisitEnded && job.Aircraft == aircraft && job.Configuration.cargoPoint == hold.cargoPoint && (!job.Finished || job.Hold.Count > 0))
                { reason = "this cargo hold already has a container manifest; recover it before replacement"; return null; }
            var flight = airport.Traffic.FlightOf(aircraft);
            if (flight != null && flight.kind == FlightKind.Departure && flight.Phase != FlightPhase.Ready)
            { reason = "aircraft has already started departure"; return null; }
            var fleet = ContainerFleet(station);
            if (fleet == null) { reason = "no compatible container station equipment"; return null; }
            if (!hold.Resolve(station.unitType, station.dollyType, station.loaderType, out _, out reason)) return null;
            foreach (var other in containerJobs)
                if (!other.Finished && other.Fleet == fleet && other.DepartureFlight == departure)
                { reason = "departure container manifest is already assigned"; return null; }
            foreach (var bulk in jobs)
                if (!bulk.VisitEnded && bulk.Aircraft == aircraft && bulk.HoldConfiguration.cargoPoint == hold.cargoPoint)
                { reason = "cargo point is already configured as a bulk hold"; return null; }
            var result = new ContainerTurnaround(this, aircraft, stand, hold, fleet, arrival, departure);
            containerJobs.Add(result); return result;
        }
        internal bool StartContainer(ContainerTurnaround job, out string reason)
        {
            reason = null; var fleet = job.Fleet; var station = fleet.Station;
            if (fleet.Job != null) { reason = "waiting for container equipment"; return false; }
            if (fleet.Dolly.Count != 0 || fleet.Main.Count != 0 || fleet.Front.Count != 0)
            { reason = "container equipment is occupied"; return false; }
            if (!job.Configuration.Resolve(station.unitType, station.dollyType, station.loaderType, out var cargo, out reason)) return false;
            if (!BaggageServicePlan.Build(airport, job.Aircraft, job.Stand, cargo, new BaggageServiceAccess(station), out var plan, out reason)) return false;
            foreach (var other in jobs)
                if (other.Acquired && !other.Finished && plan.reservation.Overlaps(other.Plan.reservation))
                { reason = "baggage working space is reserved"; return false; }
            foreach (var other in containerJobs)
                if (other != job && other.Acquired && !other.Finished && plan.reservation.Overlaps(other.Plan.reservation))
                { reason = "container working space is reserved"; return false; }
            job.Plan = plan; fleet.Job = job;
            if (fleet.Train == null) fleet.Train = new BaggageTrain(station.tractorType, station.dollyType, plan.toStand);
            else fleet.Train.Continue(plan.toStand);
            return true;
        }
        internal bool ClearContainer(ContainerTurnaround job, BaggageVehiclePose pose, VehicleType type)
        {
            if (!job.Plan.Clear(pose, type)) return false;
            var box = Box.Of(pose.position, pose.facing, type.bounds, 0.3f);
            if (job.Fleet.Station.accessBlocked && job.Plan.reservation.Contains(box.c)) return false;
            foreach (var other in liveObstacles) if (box.Overlaps(other)) return false;
            foreach (var fleet in fleets.Values) foreach (var other in Boxes(fleet)) if (box.Overlaps(other)) return false;
            foreach (var fleet in containerFleets.Values)
            {
                if (fleet == job.Fleet)
                {
                    if (type != fleet.Station.loaderType && box.Overlaps(Box.Of(fleet.LoaderPose.position, fleet.LoaderPose.facing, fleet.Station.loaderType.bounds, 0.2f))) return false;
                    if (type == fleet.Station.loaderType && fleet.Train != null &&
                        (box.Overlaps(Box.Of(fleet.Train.Tractor.position, fleet.Train.Tractor.facing, fleet.Station.tractorType.bounds, 0.2f)) ||
                         box.Overlaps(Box.Of(fleet.Train.Cart.position, fleet.Train.Cart.facing, fleet.Station.dollyType.bounds, 0.2f)))) return false;
                }
                else foreach (var other in ContainerBoxes(fleet)) if (box.Overlaps(other)) return false;
            }
            return true;
        }
        static IEnumerable<Box> ContainerBoxes(ContainerFleet fleet)
        {
            var station = fleet.Station;
            if (fleet.Train != null)
            {
                yield return Box.Of(fleet.Train.Tractor.position, fleet.Train.Tractor.facing, station.tractorType.bounds, 0.3f);
                yield return Box.Of(fleet.Train.Cart.position, fleet.Train.Cart.facing, station.dollyType.bounds, 0.3f);
            }
            yield return Box.Of(fleet.LoaderPose.position, fleet.LoaderPose.facing, station.loaderType.bounds, 0.3f);
            if (fleet.Job != null && fleet.Job.SideTransfer)
            {
                var c = station.loaderType.container;
                var p = c.mainTransfer; p.x = -(c.deckWidth + c.sideBridgeReach) * 0.5f;
                yield return Box.Of(fleet.LoaderPose.Point(p), fleet.LoaderPose.facing,
                    new Bounds(Vector3.zero, new Vector3(c.sideBridgeReach, 0.2f, 1.8f)), 0.2f);
            }
        }
    }
}
