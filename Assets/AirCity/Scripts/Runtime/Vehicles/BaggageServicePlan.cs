using System;
using System.Collections.Generic;
using AirCity.Airfield;
using UnityEngine;
using Box = AirCity.Vehicles.AirportMaintenance.Box;

namespace AirCity.Vehicles
{
    /// <summary>Derived sill docking poses, safe approach/withdrawal and equipment routes in airport coordinates.</summary>
    public sealed class BaggageServicePlan
    {
        public VehiclePath toStand, toArrivals, toDispatch, outbound, home, loaderIn, loaderOut;
        public VehiclePath loaderDock, loaderUndock;
        public BaggageVehiclePose handoff, loaderWork, loaderPark;
        public BaggageVehiclePose loaderStage;
        public Vector3 door, inward, loaderTip;
        public float sill;
        public Rect reservation;
        public Vector3 aircraftPosition;
        public Quaternion aircraftRotation;
        public int layoutVersion;
        public readonly List<Box> obstacles = new List<Box>();
        readonly Airport airport;
        Aircraft.Aircraft aircraft;
        Aircraft.AircraftType.ServicePoint cargoPoint;
        VehicleType loaderType;
        float serviceHeight;
        Dictionary<(VehicleType, Vector2, Vector2), bool> planningCache = new Dictionary<(VehicleType, Vector2, Vector2), bool>();
        readonly List<(TaxiwayEdge edge, Vector2 start, Vector2 direction, float lengthSquared)> corridors =
            new List<(TaxiwayEdge, Vector2, Vector2, float)>();
        readonly List<Box> corridorBoxes = new List<Box>();
        int geometryVersion;

        BaggageServicePlan(Airport airport)
        {
            this.airport = airport;
            CaptureCorridors();
        }

        void CaptureCorridors()
        {
            geometryVersion = airport.LayoutVersion;
            corridors.Clear(); corridorBoxes.Clear();
            var net = airport.Taxiways;
            foreach (var edge in net.edges)
            {
                var start = net.At(edge.a); var delta = net.At(edge.b) - start;
                corridors.Add((edge, start, delta, Mathf.Max(0.001f, delta.sqrMagnitude)));
                corridorBoxes.Add(Box.Of(start + delta * 0.5f, delta.normalized,
                    new Bounds(Vector3.zero, new Vector3(edge.width, 1f, delta.magnitude)), 0f));
            }
        }

        internal void RefreshGeometry()
        {
            if (geometryVersion == airport.LayoutVersion) return;
            CaptureCorridors(); obstacles.Clear(); Obstacles();
        }
        public static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        public static Vector2 Facing(float bearing) => new Vector2(Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));

        public static bool Build(Airport airport, BaggageTurnaround job, BaggageStation station,
            out BaggageServicePlan plan, out string reason)
        {
            plan = null;
            if (!job.HoldConfiguration.Resolve(station.loaderType, out var cargo, out reason)) return false;
            return Build(airport, job.Aircraft, job.Stand, cargo, new BaggageServiceAccess(station), out plan, out reason);
        }

        public static bool Build(Airport airport, Aircraft.Aircraft a, Stand stand, Aircraft.AircraftType.ServicePoint cargo,
            BaggageServiceAccess station, out BaggageServicePlan plan, out string reason)
        {
            plan = null; reason = null;
            try
            {
                var p = new BaggageServicePlan(airport) { aircraftPosition = a.transform.position,
                    aircraftRotation = a.transform.rotation, layoutVersion = airport.LayoutVersion,
                    aircraft = a, cargoPoint = cargo, loaderType = station.loaderType };
                var t = airport.transform;
                var door = t.InverseTransformPoint(a.WorldPosition(cargo));
                var normal = XZ(t.InverseTransformDirection(a.WorldDirection(cargo))).normalized;
                var forward = XZ(t.InverseTransformDirection(a.transform.forward)).normalized;
                var right = new Vector2(forward.y, -forward.x);
                var position = XZ(t.InverseTransformPoint(a.transform.position));
                var bounds = a.type.bounds;
                float clearance = 0f;
                foreach (float x in new[] { bounds.min.x, bounds.max.x })
                foreach (float z in new[] { bounds.min.z, bounds.max.z })
                    clearance = Mathf.Max(clearance, Vector2.Dot(position + right * x + forward * z - XZ(door), normal));
                p.sill = door.y;
                p.serviceHeight = p.sill + (station.unitType != null ? station.unitType.bounds.max.y : .85f);
                p.door = door; p.inward = new Vector3(-normal.x, 0f, -normal.y);
                var front = XZ(door) + normal * 0.10f;
                var loaderFacing = -normal;
                var loaderRight = new Vector2(loaderFacing.y, -loaderFacing.x);
                Vector2 rear;
                var c = station.loaderType.container;
                if (station.loaderType.kind == VehicleKind.ContainerLoader)
                {
                    var tip = c.frontTransfer; tip.z = station.loaderType.bounds.max.z;
                    // Centre the asymmetric ULD contour in the opening, rather than centring only its base.
                    if (station.unitType != null) tip.x += station.unitType.bounds.center.x;
                    p.loaderTip = tip;
                    p.loaderWork = new BaggageVehiclePose(front - loaderFacing * tip.z - loaderRight * tip.x, loaderFacing);
                    rear = p.loaderWork.Point(c.mainTransfer);
                }
                else
                {
                    var b = station.loaderType.baggage;
                    float run = Mathf.Sqrt(b.beltLength * b.beltLength - Mathf.Pow(p.sill - b.beltRear.y, 2f));
                    p.loaderTip = b.beltRear + new Vector3(0f, p.sill - b.beltRear.y, run);
                    rear = front - loaderFacing * run;
                    p.loaderWork = new BaggageVehiclePose(rear - loaderFacing * b.beltRear.z - loaderRight * b.beltRear.x, loaderFacing);
                }
                p.loaderPark = new BaggageVehiclePose(station.loaderParking, Facing(station.loaderParkingBearing));
                p.loaderStage = new BaggageVehiclePose(p.loaderWork.position - loaderFacing * Mathf.Max(6f, clearance + 4f), loaderFacing);
                p.loaderDock = new VehiclePath(); p.loaderDock.Start(p.loaderStage.position, 0.5f);
                p.loaderDock.LineTo(p.loaderWork.position, 0.5f);
                p.loaderUndock = new VehiclePath(); p.loaderUndock.Start(p.loaderWork.position, 0.5f);
                p.loaderUndock.LineTo(p.loaderStage.position, 0.5f, reverse: true);
                p.Obstacles();
                var cart = station.cartType.baggage;
                float offset = -station.tractorType.baggage.rearHitch.z + cart.frontHitch.z - (cart.loadMin.z + cart.loadMax.z) * 0.5f;
                var trainFacing = station.loaderType.kind == VehicleKind.ContainerLoader ? -loaderRight :
                    forward * (cargo.position.z >= a.type.bounds.center.z ? 1f : -1f);
                p.handoff = station.loaderType.kind == VehicleKind.ContainerLoader
                    ? new BaggageVehiclePose(rear - loaderRight * (c.deckWidth * 0.5f + station.cartType.Length * 0.5f + 1.4f) + trainFacing * offset, trainFacing)
                    : new BaggageVehiclePose(rear + normal * 3.5f + trainFacing * offset, trainFacing);
                var net = airport.Taxiways;
                int Nearest(Vector2 target)
                {
                    int best = 0; float distance = float.MaxValue;
                    foreach (var n in net.nodes)
                    {
                        if (n.runway != null || !net.edges.Exists(e => (e.a == n.id || e.b == n.id) && e.road == RoadClass.Service)) continue;
                        float d = Vector2.SqrMagnitude(net.At(n.id) - target);
                        if (d < distance) { distance = d; best = n.id; }
                    }
                    return best;
                }
                int entry = stand.baggageEntryNode != 0 ? stand.baggageEntryNode : Nearest(p.handoff.position + forward * 20f);
                int exit = stand.baggageExitNode != 0 ? stand.baggageExitNode : Nearest(p.handoff.position - forward * 20f);
                bool containerDolly = station.cartType.kind == VehicleKind.ContainerDolly;
                float radius = Mathf.Max(containerDolly ? 0f : 6f, station.tractorType.turnRadius, station.cartType.turnRadius);
                float speed = Mathf.Min(station.tractorType.transitSpeed, station.cartType.transitSpeed);
                float width = Mathf.Max(station.tractorType.Width, station.cartType.Width) + 1f;
                VehiclePath Road(int from, int to, Vector2? start = null, Vector2? end = null)
                {
                    if (!BaggageRoute.Plan(net, from, to, radius, speed, width, out var path, out var why, start, end))
                        throw new ArgumentException(why);
                    return path;
                }
                VehiclePath Approach(int from, float bearing)
                {
                    VehiclePath Finish(VehiclePath path)
                    {
                        path.TurnTo(path.EndHeading, p.handoff.position - p.handoff.facing * (containerDolly ? .1f : 2f), p.handoff.facing, radius, speed);
                        path.LineTo(p.handoff.position, speed); return path;
                    }
                    var direct = Finish(Road(from, entry, Facing(bearing)));
                    var loaderBox = Box.Of(p.loaderWork.position, p.loaderWork.facing, station.loaderType.bounds, 0.4f);
                    float bestDistance = float.MaxValue; string approachFailure = null;
                    bool ClearApproach(VehiclePath route)
                    {
                        var train = new BaggageTrain(station.tractorType, station.cartType, route);
                        string obstacle = null;
                        bool result = BaggageRoute.Validate(train, (q, type) =>
                        {
                            if (!p.Clear(q, type)) { obstacle = "surface or aircraft"; return false; }
                            if (Box.Of(q.position, q.facing, type.bounds, 0.3f).Overlaps(loaderBox))
                            { obstacle = type.kind + " reaches the loader"; return false; }
                            return true;
                        }, out var why);
                        float remaining = Vector2.Distance(train.Tractor.position, p.handoff.position);
                        if (!result && remaining < bestDistance)
                        {
                            bestDistance = remaining;
                            approachFailure = $"{obstacle ?? why}; tractor {train.Tractor.position} facing {train.Tractor.facing}; dolly {train.Cart.position}; target {p.handoff.position}; loader {p.loaderWork.position}";
                        }
                        return result;
                    }
                    if (ClearApproach(direct)) return direct;
                    var entryHeading = -normal;
                    float sideSign = Airfield.Building.Geo.Cross(entryHeading, p.handoff.facing) > 0f ? 1f : -1f;
                    Vector2 TurnSide(Vector2 heading) => sideSign > 0f ? new Vector2(-heading.y, heading.x) : new Vector2(heading.y, -heading.x);
                    var arcEnd = p.handoff.position - p.handoff.facing * .1f;
                    var centre = arcEnd + TurnSide(p.handoff.facing) * radius;
                    var arcStart = centre - TurnSide(entryHeading) * radius;
                    foreach (float lead in Mathf.Abs(Vector2.Dot(entryHeading, p.handoff.facing)) < .1f ? new[] { 12f, 20f, 30f } : Array.Empty<float>())
                    {
                        var route = Road(from, entry, Facing(bearing));
                        route.TurnTo(route.EndHeading, arcStart - entryHeading * lead, entryHeading, radius, speed);
                        route.LineTo(arcStart, speed);
                        route.ArcAround(centre, sideSign * Mathf.PI * 0.5f, speed);
                        route.LineTo(p.handoff.position, speed);
                        if (ClearApproach(route)) return route;
                    }
                    foreach (float side in new[] { 12f, 20f, 30f })
                    foreach (float along in new[] { radius + 0.5f, radius + 2f, radius + 4f, radius + 6f, 0f, -6f, 6f, -12f, 12f })
                    foreach (var heading in new[] { -normal, -forward, forward })
                    {
                        var route = Road(from, entry, Facing(bearing));
                        var via = p.handoff.position + normal * side + forward * along;
                        route.TurnTo(route.EndHeading, via, heading, radius, speed);
                        route.LineTo(via + heading * 2f, speed);
                        route = Finish(route);
                        if (ClearApproach(route)) return route;
                    }
                    throw new ArgumentException("no clear dolly approach around the working container loader: " + approachFailure);
                }
                VehiclePath Return(int destination, float bearing)
                {
                    var road = Road(exit, destination, end: Facing(bearing));
                    var start = road.Pose(0f, out var heading, out _);
                    VehiclePath Route(Vector2? via)
                    {
                        var path = new VehiclePath(); path.Start(p.handoff.position, speed);
                        path.LineTo(p.handoff.position + p.handoff.facing * (containerDolly ? .1f : 2f), speed);
                        if (via.HasValue)
                        {
                            path.TurnTo(p.handoff.facing, via.Value, normal, radius, speed);
                            path.LineTo(via.Value + normal * 2f, speed);
                        }
                        path.TurnTo(via.HasValue ? normal : p.handoff.facing, start - heading * 2f, heading, radius, speed);
                        path.LineTo(start, speed); path.Append(road);
                        return path;
                    }
                    var seed = new BaggageTrain(station.tractorType, station.cartType, p.toStand);
                    BaggageRoute.Validate(seed, null, out _);
                    var loader = Box.Of(p.loaderWork.position, p.loaderWork.facing, station.loaderType.bounds, .4f);
                    string withdrawalFailure = null; float furthest = -1f;
                    bool Valid(VehiclePath route)
                    {
                        var trial = seed.Preview(route); string obstacle = null;
                        bool valid = BaggageRoute.Validate(trial, (pose, type) =>
                        {
                            if (!p.Clear(pose, type)) { obstacle = type.kind + " reaches a surface exclusion or aircraft"; return false; }
                            if (Box.Of(pose.position, pose.facing, type.bounds, .3f).Overlaps(loader))
                            { obstacle = type.kind + " reaches the loader"; return false; }
                            return true;
                        }, out var why);
                        if (!valid && trial.Distance > furthest)
                        {
                            furthest = trial.Distance;
                            withdrawalFailure = $"{obstacle ?? why}; tractor {trial.Tractor.position} facing {trial.Tractor.facing}; cart {trial.Cart.position}; travelled {trial.Distance:F1} m";
                        }
                        return valid;
                    }
                    var direct = Route(null);
                    if (Valid(direct)) return direct;
                    foreach (float outboard in new[] { clearance + 15f, clearance + 25f, clearance + 40f })
                    foreach (float along in new[] { radius + (containerDolly ? .1f : 2f), radius * 2f, radius * 3f, radius * 4f })
                    {
                        var route = Route(p.handoff.position + normal * outboard + p.handoff.facing * along);
                        if (Valid(route)) return route;
                    }
                    throw new ArgumentException("no clear train withdrawal from the cargo handoff: " + withdrawalFailure);
                }
                p.toStand = Approach(station.dispatchNode, station.dispatchBearing);
                p.toArrivals = Return(station.arrivalsNode, station.arrivalsBearing);
                p.toDispatch = station.arrivalsNode == station.dispatchNode ? null :
                    Road(station.arrivalsNode, station.dispatchNode, Facing(station.arrivalsBearing), Facing(station.dispatchBearing));
                p.outbound = new VehiclePath(); p.outbound.Append(p.toStand);
                p.home = Return(station.dispatchNode, station.dispatchBearing);
                VehiclePath Loader(BaggageVehiclePose from, BaggageVehiclePose to)
                {
                    var path = new VehiclePath(); path.Start(from.position, station.loaderType.transitSpeed);
                    path.LineTo(from.position + from.facing * 2f, station.loaderType.transitSpeed);
                    path.TurnTo(from.facing, to.position - to.facing * 2f, to.facing, station.loaderType.turnRadius, station.loaderType.transitSpeed);
                    path.LineTo(to.position, station.loaderType.transitSpeed);
                    return path;
                }
                p.loaderIn = Loader(p.loaderPark, p.loaderStage); p.loaderOut = Loader(p.loaderStage, p.loaderPark);
                var preview = new BaggageTrain(station.tractorType, station.cartType, p.toStand);
                foreach (var leg in new[] { p.toStand, p.toArrivals, p.toDispatch, p.outbound, p.home })
                {
                    if (leg == null) continue;
                    if (leg != p.toStand) preview.Continue(leg);
                    if (!BaggageRoute.Validate(preview, p.Clear, out reason))
                    { reason = $"{(leg == p.toStand ? "to stand" : leg == p.toArrivals ? "to arrivals" : leg == p.toDispatch ? "to dispatch" : leg == p.outbound ? "outbound" : "home")}: {reason}; tractor {preview.Tractor.position}; cart {preview.Cart.position}"; return false; }
                }
                var parkedTrain = new BaggageTrain(station.tractorType, station.cartType, p.toStand);
                var parkedTractor = Box.Of(parkedTrain.Tractor.position, parkedTrain.Tractor.facing, station.tractorType.bounds, 0.4f);
                var parkedCart = Box.Of(parkedTrain.Cart.position, parkedTrain.Cart.facing, station.cartType.bounds, 0.4f);
                string loaderRefusal = null;
                bool LoaderClear(VehiclePath leg)
                {
                    for (float d = 0f; d <= leg.Length; d += 0.4f)
                    {
                        var q = leg.Pose(d, out var f, out _);
                        var box = Box.Of(q, f, station.loaderType.bounds, 0.3f);
                        if (!p.Clear(new BaggageVehiclePose(q, f), station.loaderType))
                        { loaderRefusal = "loader footprint is blocked at " + q; return false; }
                        if (box.Overlaps(parkedTractor) || box.Overlaps(parkedCart))
                        { loaderRefusal = "loader passes the parked train at " + q; return false; }
                    }
                    return true;
                }
                VehiclePath LoaderRoute(BaggageVehiclePose from, BaggageVehiclePose to)
                {
                    var direct = Loader(from, to);
                    if (LoaderClear(direct)) return direct;
                    foreach (float along in new[] { 0f, 15f, -15f })
                    foreach (float heading in new[] { 1f, -1f })
                    {
                        var via = new BaggageVehiclePose(p.loaderPark.position + normal * 15f + forward * along, forward * heading);
                        var route = Loader(from, via); route.Append(Loader(via, to));
                        if (LoaderClear(route)) return route;
                    }
                    throw new ArgumentException("no clear loader route: " + loaderRefusal);
                }
                p.loaderIn = LoaderRoute(p.loaderPark, p.loaderStage);
                p.loaderOut = LoaderRoute(p.loaderStage, p.loaderPark);
                if (!LoaderClear(p.loaderDock) || !LoaderClear(p.loaderUndock)) throw new ArgumentException(loaderRefusal);
                var work = Box.Of(p.loaderWork.position, p.loaderWork.facing, station.loaderType.bounds, 1f);
                var cartBox = Box.Of(p.handoff.position, p.handoff.facing, station.cartType.bounds, 6f);
                var min = Vector2.Min(work.c - Vector2.one * work.Radius, cartBox.c - Vector2.one * cartBox.Radius);
                var max = Vector2.Max(work.c + Vector2.one * work.Radius, cartBox.c + Vector2.one * cartBox.Radius);
                foreach (int node in new[] { entry, exit })
                {
                    min = Vector2.Min(min, net.At(node) - Vector2.one * 8f);
                    max = Vector2.Max(max, net.At(node) + Vector2.one * 8f);
                }
                foreach (var leg in new[] { p.loaderIn, p.loaderOut, p.loaderDock, p.loaderUndock })
                    for (float d = 0f; d <= leg.Length; d += 1f)
                    {
                        var q = leg.Sample(d);
                        min = Vector2.Min(min, q - Vector2.one * 8f); max = Vector2.Max(max, q + Vector2.one * 8f);
                    }
                p.reservation = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                p.planningCache = null; // Live clearance must observe moved aircraft and changed surfaces.
                plan = p;
                return true;
            }
            catch (ArgumentException e) { reason = e.Message; return false; }
        }

        void Obstacles()
        {
            foreach (var rect in airport.Bridges.BaggageExclusions())
                obstacles.Add(Box.Of(rect.center, Vector2.up, new Bounds(Vector3.zero, new Vector3(rect.width, 1f, rect.height)), 0.5f));
            foreach (var terminal in airport.terminals)
            {
                if (terminal == null) continue;
                foreach (var polygon in terminal.Footprints())
                {
                    if (polygon.Count == 0) continue;
                    Vector2 min = polygon[0], max = min;
                    foreach (var point in polygon) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
                    obstacles.Add(Box.Of((min + max) * 0.5f, Vector2.up, new Bounds(Vector3.zero, new Vector3(max.x - min.x, 1f, max.y - min.y)), 0.5f));
                }
            }
        }

        public bool Clear(BaggageVehiclePose pose, VehicleType type)
        {
            var key = (type, pose.position, pose.facing);
            if (planningCache != null && planningCache.TryGetValue(key, out bool clear)) return clear;
            bool result = ClearUncached(pose, type);
            if (planningCache != null && planningCache.Count < 100000) planningCache[key] = result;
            return result;
        }

        bool ClearUncached(BaggageVehiclePose pose, VehicleType type)
        {
            var bounds = type.bounds;
            if (type.IsBaggageTrailer)
            {
                float sweep = type.baggage.frontHitch.z - type.baggage.steeringPivot.z + 0.1f;
                bounds.Expand(new Vector3(Mathf.Max(0f, 2f * sweep - bounds.size.x), 0f, 0f));
            }
            var box = Box.Of(pose.position, pose.facing, bounds, 0.2f);
            foreach (var a in Aircraft.Aircraft.All)
                if (a != null && a.type != null && !AircraftServiceClearance.Clear(airport, a, pose, bounds,
                    type == loaderType ? type : null, a == aircraft, cargoPoint, loaderTip, serviceHeight)) return false;
            // Exact obstacle overlap first; coverage sampling never has to detect small obstacles.
            foreach (var obstacle in obstacles) if (box.Overlaps(obstacle)) return false;
            for (int i = 0; i < corridors.Count; i++)
            {
                var edge = corridors[i].edge;
                if ((!edge.IsRoad || TaxiwayNetwork.Closed(edge) || edge.wear >= RunwayRules.ClosedWear) && box.Overlaps(corridorBoxes[i])) return false;
            }
            int nx = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / 0.75f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / 0.75f));
            for (int x = 0; x <= nx; x++)
            for (int z = 0; z <= nz; z++)
            {
                if (x != 0 && x != nx && z != 0 && z != nz) continue;
                var point = pose.Point(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)x / nx), 0f,
                    Mathf.Lerp(bounds.min.z, bounds.max.z, (float)z / nz)));
                if (!OnSurface(point)) return false;
            }
            return true;
        }

        bool OnSurface(Vector2 point)
        {
            bool onRoad = false;
            foreach (var corridor in corridors)
            {
                var edge = corridor.edge;
                float t = Mathf.Clamp01(Vector2.Dot(point - corridor.start, corridor.direction) / corridor.lengthSquared);
                if (Vector2.Distance(point, corridor.start + corridor.direction * t) >= edge.width * 0.5f - 0.2f) continue;
                if (!edge.IsRoad || TaxiwayNetwork.Closed(edge) || edge.wear >= RunwayRules.ClosedWear) return false;
                if (edge.road == RoadClass.Service) onRoad = true;
            }
            if (onRoad) return true;
            foreach (var apron in airport.aprons) if (apron.Contains(point)) return true;
            return false;
        }
    }
}
