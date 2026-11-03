using System;
using UnityEngine;

namespace AirCity.Vehicles
{
    public readonly struct BaggageVehiclePose
    {
        public readonly Vector2 position, facing;
        public Vector2 Right => new Vector2(facing.y, -facing.x);
        public BaggageVehiclePose(Vector2 position, Vector2 facing)
        { this.position = position; this.facing = facing.normalized; }
        public Vector2 Point(Vector3 local) => position + Right * local.x + facing * local.z;
    }

    /// <summary>
    /// A tractor and four-wheel turntable cart, moving forwards. The drawbar and body are two rigid links:
    /// project the front axle behind the hitch, then the rear axle behind the front axle. Small distance steps
    /// approximate rolling constraints; the actual hitch eye stays exactly on the tractor pin at every step.
    /// </summary>
    public sealed class BaggageTrain
    {
        const float DistanceStep = 0.05f;
        public const float SteeringLimit = 60f;
        public VehicleType TractorType { get; }
        public VehicleType CartType { get; }
        public VehiclePath Path { get; private set; }
        public float Distance { get; private set; }
        public BaggageVehiclePose Tractor { get; private set; }
        public BaggageVehiclePose Cart { get; private set; }
        public Vector2 DrawbarFacing { get; private set; }
        public float CartSteering => Mathf.DeltaAngle(Bearing(Cart.facing), Bearing(DrawbarFacing));
        public Vector2 TractorHitch => Tractor.Point(TractorType.baggage.rearHitch);
        public Vector2 CartHitch => Cart.Point(CartType.baggage.steeringPivot) + DrawbarFacing * DrawbarLength;
        float DrawbarLength => CartType.baggage.frontHitch.z - CartType.baggage.steeringPivot.z;

        public BaggageTrain(VehicleType tractor, VehicleType cart, VehiclePath path)
        {
            if (tractor == null || tractor.kind != VehicleKind.BaggageTractor || tractor.baggage == null ||
                cart == null || !cart.IsBaggageTrailer || cart.baggage == null || path == null || path.Count < 2)
                throw new ArgumentException("A baggage train needs a tractor, cart, interfaces and a forward path.");
            var b = cart.baggage;
            if (b.steeringPivot.z <= 0f || b.frontHitch.z <= b.steeringPivot.z ||
                Mathf.Abs(b.frontHitch.x) > 0.001f || Mathf.Abs(b.steeringPivot.x) > 0.001f ||
                Mathf.Abs(b.frontHitch.y - tractor.baggage.rearHitch.y) > 0.01f)
                throw new ArgumentException("Unsupported cart geometry or incompatible hitch heights.");
            for (float d = 0f; d <= path.Length; d += 0.5f)
                if (path.Reversing(d)) throw new ArgumentException("Baggage trains need forward circulation.");
            TractorType = tractor; CartType = cart; Path = path;
            var p = path.Pose(0f, out var f, out _);
            Tractor = new BaggageVehiclePose(p, f);
            Cart = new BaggageVehiclePose(TractorHitch - f * b.frontHitch.z, f);
            DrawbarFacing = f;
        }

        public bool Advance(float metres, Func<BaggageVehiclePose, VehicleType, bool> clear, out string reason)
        {
            if (!BaggageLedger.Finite(metres) || metres < 0f) throw new ArgumentOutOfRangeException(nameof(metres));
            reason = null;
            float end = Mathf.Min(Distance + metres, Path.Length);
            while (Distance < end - 0.00001f)
            {
                float at = Mathf.Min(end, Distance + DistanceStep);
                var p = Path.Pose(at, out var f, out _);
                var tractor = new BaggageVehiclePose(p, f);
                var hitch = tractor.Point(TractorType.baggage.rearHitch);
                var oldAxle = Cart.Point(CartType.baggage.steeringPivot);
                var drawbar = (hitch - oldAxle).normalized;
                var axle = hitch - drawbar * DrawbarLength;
                var body = (axle - Cart.position).normalized;
                var cart = new BaggageVehiclePose(axle - body * CartType.baggage.steeringPivot.z, body);
                float steer = Mathf.DeltaAngle(Bearing(body), Bearing(drawbar));
                if (Mathf.Abs(steer) > SteeringLimit)
                { reason = "cart turn exceeds the drawbar steering limit"; return false; }
                if (clear != null && (!clear(tractor, TractorType) || !clear(cart, CartType)))
                { reason = "baggage train access is blocked"; return false; }
                Tractor = tractor; Cart = cart; DrawbarFacing = drawbar; Distance = at;
            }
            return true;
        }

        /// <summary>Continue along another forward leg without resetting the cart's axle/drawbar pose.</summary>
        public void Continue(VehiclePath path)
        {
            if (path == null || path.Count < 2) throw new ArgumentException("A continuation needs a path.");
            var start = path.Pose(0f, out var facing, out _);
            if (Vector2.Distance(start, Tractor.position) > 0.01f || Vector2.Dot(facing, Tractor.facing) < 0.99f)
                throw new ArgumentException($"A continuation must begin at the train's current pose: start {start} facing {facing}, current {Tractor.position} facing {Tractor.facing}.");
            for (float d = 0f; d <= path.Length; d += 0.5f)
                if (path.Reversing(d)) throw new ArgumentException("Baggage trains need forward circulation.");
            Path = path; Distance = 0f;
        }

        internal BaggageTrain Preview(VehiclePath continuation)
        {
            var copy = (BaggageTrain)MemberwiseClone();
            copy.Continue(continuation);
            return copy;
        }

        /// <summary>Conservative generated bounds, including drawbar sweep; used for corridor/obstacle checks.</summary>
        public static bool FootprintClear(BaggageVehiclePose pose, VehicleType type, Func<Vector2, bool> clear)
        {
            var bounds = type.bounds;
            if (type.IsBaggageTrailer && type.baggage != null)
            {
                float sweep = type.baggage.frontHitch.z - type.baggage.steeringPivot.z + 0.1f;
                bounds.SetMinMax(new Vector3(Mathf.Min(bounds.min.x, -sweep), bounds.min.y, bounds.min.z),
                    new Vector3(Mathf.Max(bounds.max.x, sweep), bounds.max.y, bounds.max.z));
            }
            // Sample the footprint, so obstacles between the corners of a long cart are checked too.
            int nx = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / 0.4f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / 0.4f));
            for (int x = 0; x <= nx; x++)
                for (int z = 0; z <= nz; z++)
                {
                    var local = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)x / nx), 0f,
                        Mathf.Lerp(bounds.min.z, bounds.max.z, (float)z / nz));
                    if (!clear(pose.Point(local))) return false;
                }
            return true;
        }

        public static float Bearing(Vector2 facing) => Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg;
    }
}
