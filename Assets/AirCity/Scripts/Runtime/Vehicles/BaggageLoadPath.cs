using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>The same cart, conveyor and hold path supplies simulation travel time and visual positions.</summary>
    public static class BaggageLoadPath
    {
        public const float BeltSpeed = 0.8f;
        public static float HoldDepth(BaggageTurnaround job)
        {
            foreach (var door in job.HoldConfiguration.aircraft.cargoDoors)
                if (door.point == job.HoldConfiguration.cargoPoint) return door.holdDepth;
            return 0.65f;
        }
        public static Vector3 Point(BaggageFleet fleet, BaggageServicePlan plan, int index)
        {
            var type = fleet.Equipment.Loader.baggage;
            Vector3 local; BaggageVehiclePose pose;
            if (index == 0)
            {
                var cart = fleet.Equipment.Cart.baggage;
                local = (cart.loadMin + cart.loadMax) * 0.5f; local.y = cart.loadMin.y;
                pose = fleet.Train.Cart;
            }
            else if (index == 3) return plan.door + plan.inward * Mathf.Min(0.65f, HoldDepth(fleet.Job) - 0.32f);
            else
            {
                local = type.beltRear; pose = fleet.LoaderPose;
                if (index == 2)
                {
                    local.y = fleet.LoaderHeight;
                    local.z += Mathf.Sqrt(type.beltLength * type.beltLength - Mathf.Pow(fleet.LoaderHeight - type.beltRear.y, 2f));
                }
            }
            var p = pose.Point(local);
            return new Vector3(p.x, local.y, p.y);
        }
        public static float Length(BaggageFleet fleet, BaggageServicePlan plan)
        {
            float length = 0f;
            for (int i = 0; i < 3; i++) length += Vector3.Distance(Point(fleet, plan, i), Point(fleet, plan, i + 1));
            return length;
        }
        public static Vector3 At(BaggageFleet fleet, BaggageServicePlan plan, float progress, bool loading)
        {
            float distance = Mathf.Clamp01(loading ? progress : 1f - progress) * Length(fleet, plan);
            for (int i = 0; i < 3; i++)
            {
                var a = Point(fleet, plan, i); var b = Point(fleet, plan, i + 1);
                float length = Vector3.Distance(a, b);
                if (distance <= length) return Vector3.Lerp(a, b, distance / Mathf.Max(0.001f, length));
                distance -= length;
            }
            return Point(fleet, plan, 3);
        }
    }
}
