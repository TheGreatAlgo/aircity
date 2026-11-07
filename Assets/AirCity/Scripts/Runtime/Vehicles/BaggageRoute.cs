using System;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    public static class BaggageRoute
    {
        /// <summary>Service roads only. Endpoints are road nodes; local stand approaches can be added later.</summary>
        public static bool Plan(TaxiwayNetwork network, int from, int to, float radius, float speed,
            float minimumWidth, out VehiclePath path, out string reason,
            Vector2? startFacing = null, Vector2? endFacing = null)
        {
            path = null; reason = null;
            if (network == null || network.Node(from) == null || network.Node(to) == null || from == to ||
                !BaggageLedger.Finite(radius) || radius <= 0f || !BaggageLedger.Finite(speed) || speed <= 0f ||
                !BaggageLedger.Finite(minimumWidth) || minimumWidth <= 0f)
            { reason = "baggage route needs distinct service-road endpoints and positive speed/radius"; return false; }
            bool Heading(TaxiwayEdge edge, int node, Vector2? facing, bool arriving)
            {
                if (!facing.HasValue || (edge.a != node && edge.b != node)) return true;
                var direction = (network.At(TaxiwayNetwork.Other(edge, node)) - network.At(node)).normalized;
                return Vector2.Dot(direction * (arriving ? -1f : 1f), facing.Value.normalized) > 0.7f;
            }
            var nodes = network.FindRoute(from, to,
                e => e.road == RoadClass.Service && e.width >= minimumWidth && e.wear < RunwayRules.ClosedWear &&
                    !TaxiwayNetwork.Closed(e) && network.Node(e.a).runway == null && network.Node(e.b).runway == null &&
                    Heading(e, from, startFacing, false) && Heading(e, to, endFacing, true),
                _ => false);
            if (nodes == null) { reason = "no open service-road route for baggage"; return false; }
            path = new VehiclePath();
            path.Start(network.At(nodes[0]), speed);
            var points = nodes.ConvertAll(network.At);
            points.RemoveAt(0);
            path.Through(points, radius, speed);
            return true;
        }

        /// <summary>Validate the whole moving train, including the cart's offtracking, before reserving a job.</summary>
        public static bool Validate(BaggageTrain preview, Func<BaggageVehiclePose, VehicleType, bool> clear, out string reason)
        {
            reason = null;
            if (clear != null)
            {
                if (!clear(preview.Tractor,preview.TractorType)) { reason = "baggage parking footprint is blocked"; return false; }
                for (int i = 0; i < preview.CartCount; i++)
                    if (!clear(preview.CartAt(i),preview.CartType)) { reason = "cart " + (i+1) + " parking footprint is blocked"; return false; }
            }
            return preview.Advance(preview.Path.Length, clear, out reason);
        }
    }
}
