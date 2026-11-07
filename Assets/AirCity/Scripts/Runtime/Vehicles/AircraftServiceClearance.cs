using AirCity.Aircraft;
using AirCity.Airfield;
using UnityEngine;
using System.Runtime.CompilerServices;
using Box = AirCity.Vehicles.AirportMaintenance.Box;

namespace AirCity.Vehicles
{
    /// <summary>Projected aircraft parts protect swept wings, engines, tail and gear during close servicing.</summary>
    public static class AircraftServiceClearance
    {
        sealed class Envelope
        {
            public object parts;
            public Bounds original,bounds;
        }
        static readonly ConditionalWeakTable<AircraftType,Envelope> envelopes = new ConditionalWeakTable<AircraftType,Envelope>();
        static Bounds ServiceBounds(AircraftType type)
        {
            if (envelopes.TryGetValue(type,out var cached) && ReferenceEquals(cached.parts,type.serviceFootprints) && cached.original==type.bounds)
                return cached.bounds;
            var bounds=type.bounds;
            foreach (var part in type.serviceFootprints)
                foreach (var p in part.outline) bounds.Encapsulate(new Vector3(p.x,0,p.y));
            envelopes.Remove(type); envelopes.Add(type,new Envelope { parts=type.serviceFootprints,original=type.bounds,bounds=bounds });
            return bounds;
        }
        public static bool Clear(Airport airport, Aircraft.Aircraft aircraft, BaggageVehiclePose pose, Bounds bounds,
            VehicleType vehicle, bool servicing, AircraftType.ServicePoint cargo, Vector3 tip, float serviceHeight)
        {
            var t = aircraft.transform;
            var at = airport.transform.InverseTransformPoint(t.position);
            if (at.y > 3f) return true;
            var facing = BaggageServicePlan.XZ(airport.transform.InverseTransformDirection(t.forward)).normalized;
            var box = Box.Of(pose.position, pose.facing, bounds, 0.2f);
            if (!servicing || aircraft.type.serviceFootprints.Length == 0)
                return !box.Overlaps(Box.Of(BaggageServicePlan.XZ(at), facing, aircraft.type.bounds, 0.75f));
            if (!box.Overlaps(Box.Of(BaggageServicePlan.XZ(at),facing,ServiceBounds(aircraft.type),.5f))) return true;

            var right = new Vector2(facing.y, -facing.x);
            Vector2 Local(Vector2 p)
            {
                var d = p - BaggageServicePlan.XZ(at);
                return new Vector2(Vector2.Dot(d, right), Vector2.Dot(d, facing));
            }
            var local = new BaggageVehiclePose(Local(pose.position),
                new Vector2(Vector2.Dot(pose.facing, right), Vector2.Dot(pose.facing, facing)));
            var normal = BaggageServicePlan.XZ(cargo.normal).normalized;
            var door = BaggageServicePlan.XZ(cargo.position);
            var nose = local.Point(tip);
            var side = local.Right;
            // Only the forward belt/roller deck may enter the skin's projected overhang, in the hatch lane.
            // Its tip must still stop outside the sill. Wings, engines, tail and gear receive the full footprint.
            bool contact = vehicle != null && (vehicle.kind == VehicleKind.BeltLoader || vehicle.kind == VehicleKind.ContainerLoader) &&
                Vector2.Dot(local.facing, -normal) >= 0.7f &&
                Mathf.Abs(Vector2.Dot(nose - door - normal * 0.10f, side)) < 0.04f && Vector2.Dot(nose - door, normal) >= 0.075f;
            foreach (var part in aircraft.type.serviceFootprints)
            {
                float equipmentTop = vehicle != null ? Mathf.Max(bounds.max.y, serviceHeight) : bounds.max.y;
                if ((part.name == "Tail" || part.name == "Left wing" || part.name == "Right wing") &&
                    part.bottom + at.y > equipmentTop + .25f) continue;
                var shape = bounds;
                if (contact && part.name == "Fuselage")
                {
                    float overhang = 0f;
                    foreach (var p in part.outline) overhang = Mathf.Max(overhang, Vector2.Dot(p - door, normal));
                    var min = shape.min; var max = shape.max;
                    float corner = bounds.extents.x * Mathf.Abs(Vector2.Dot(local.Right, normal));
                    max.z = Mathf.Min(max.z, tip.z - (overhang + corner + 0.45f) / Vector2.Dot(local.facing, -normal));
                    shape.SetMinMax(min, max);
                }
                if (Overlaps(Box.Of(local.position, local.facing, shape, 0.2f), part.outline)) return false;
            }
            return true;
        }

        public static bool Overlaps(Box box, Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return false;
            bool Separated(Vector2 axis)
            {
                float min = float.MaxValue, max = float.MinValue;
                foreach (var p in polygon) { float d = Vector2.Dot(p - box.c, axis); min = Mathf.Min(min, d); max = Mathf.Max(max, d); }
                float radius = box.hx * Mathf.Abs(Vector2.Dot(box.ax, axis)) + box.hz * Mathf.Abs(Vector2.Dot(box.az, axis));
                return min > radius || max < -radius;
            }
            if (Separated(box.ax) || Separated(box.az)) return false;
            float area=0f;
            for (int i=0;i<polygon.Length;i++)
            { var a=polygon[i]; var b=polygon[(i+1)%polygon.Length]; area+=a.x*b.y-a.y*b.x; }
            for (int i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                // A convex polygon already lies inside each of its edge planes. Only project the box.
                var outward=new Vector2(edge.y,-edge.x)*(area>=0f ? 1f : -1f);
                float radius=box.hx*Mathf.Abs(Vector2.Dot(box.ax,outward))+box.hz*Mathf.Abs(Vector2.Dot(box.az,outward));
                if (Vector2.Dot(box.c-polygon[i],outward)>radius) return false;
            }
            return true;
        }
    }
}
