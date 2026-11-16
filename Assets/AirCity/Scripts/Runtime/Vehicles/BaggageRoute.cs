using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    public static class BaggageRoute
    {
        /// <summary>Service roads only. Endpoints are road nodes; local stand approaches can be added later.</summary>
        public static bool Plan(TaxiwayNetwork network, int from, int to, float radius, float speed,
            float minimumWidth, out VehiclePath path, out string reason,
            Vector2? startFacing = null, Vector2? endFacing = null, Func<TaxiwayEdge,bool> allowed = null)
        {
            path = null; reason = null;
            if (network == null || network.Node(from) == null || network.Node(to) == null || from == to ||
                !BaggageLedger.Finite(radius) || radius <= 0f || !BaggageLedger.Finite(speed) || speed <= 0f ||
                !BaggageLedger.Finite(minimumWidth) || minimumWidth <= 0f)
            { reason = "baggage route needs distinct service-road endpoints and positive speed/radius"; return false; }
            bool Heading(TaxiwayEdge edge, int node, Vector2? facing, bool arriving)
            {
                if (!facing.HasValue || (edge.a != node && edge.b != node)) return true;
                var direction = Geo.XZ(network.Path(edge).Tangent(edge.a==node?0:1))*(edge.a==node?1:-1);
                return Vector2.Dot(direction * (arriving ? -1f : 1f), facing.Value.normalized) > 0.7f;
            }
            var nodes = network.FindRoute(from, to,
                e => e.road == RoadClass.Service && e.width >= minimumWidth && e.wear < RunwayRules.ClosedWear &&
                    !TaxiwayNetwork.Closed(e) && network.Node(e.a).runway == null && network.Node(e.b).runway == null &&
                    Heading(e, from, startFacing, false) && Heading(e, to, endFacing, true) && (allowed==null || allowed(e)),
                _ => false);
            if (nodes == null) { reason = "no open service-road route for baggage"; return false; }
            path = new VehiclePath();
            path.Start(network.At(nodes[0]), speed);
            var points=new List<Vector2>();var edges=new List<TaxiwayEdge>();
            for(int i=1;i<nodes.Count;i++)
            {
                int a=nodes[i-1],b=nodes[i];var edge=network.edges.Find(e=>e.a==a&&e.b==b||e.a==b&&e.b==a);edges.Add(edge);
                var alignment=network.Path(edge);bool reverse=edge.b==a;
                int count=edge.alignment.curved?Mathf.Max(1,Mathf.CeilToInt(alignment.Length/6f)):1;
                for(int k=1;k<=count;k++)points.Add(Geo.XZ(alignment.Position(reverse?1-k/(float)count:k/(float)count)));
            }
            path.Through(points,radius,speed);
            BindElevation(network,path,edges);
            return true;
        }

        internal static void BindElevation(TaxiwayNetwork network,VehiclePath path,IEnumerable<TaxiwayEdge> source)
        {
            var edges=source.Where(e=>e!=null).Distinct().ToArray();
            var builder=new TaxiwayBuilder(network,network.Airport!=null?network.Airport.Runways:System.Array.Empty<Runway>());
            path.BindElevation(p=>
            {
                TaxiwayEdge nearest=null;float best=float.MaxValue;
                foreach(var edge in edges){float d=network.Path(edge).DistanceTo(p);if(d<best){best=d;nearest=edge;}}
                return nearest!=null?builder.RoadHeight(nearest,p):0f;
            });
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
