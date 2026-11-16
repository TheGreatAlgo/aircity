using UnityEngine;

namespace AirCity.Vehicles
{
    public enum DeicingDeliveryPhase
    {
        Queued, Inbound, Connecting, Unloading, Disconnecting, Outbound, Waiting, Complete,
    }

    /// <summary>One physical tanker load. The area's pending litres remain the paid, undelivered source of truth.</summary>
    public sealed class DeicingDelivery
    {
        public readonly DeicingWorkArea Area;
        public DeicingDrive Drive { get; internal set; }
        public DeicingDeliveryPhase Phase { get; internal set; }
        public float CargoLitres { get; internal set; }
        public float DeliveredLitres { get; internal set; }
        public float HoseProgress { get; internal set; }
        public bool HoseConnected => hoseAttached;
        public string Waiting { get; internal set; }
        public Vector2 Position => Drive != null ? Drive.Position : Vector2.zero;
        public Vector2 Facing => Drive != null ? Drive.Forward : Vector2.up;
        public float Speed => Drive != null ? Drive.Speed : 0f;
        public string Status => Waiting ?? Phase switch
        {
            DeicingDeliveryPhase.Queued => "preparing fluid delivery",
            DeicingDeliveryPhase.Inbound => "tanker approaching",
            DeicingDeliveryPhase.Connecting => "connecting delivery hose",
            DeicingDeliveryPhase.Unloading => "unloading deicing fluid",
            DeicingDeliveryPhase.Disconnecting => "disconnecting delivery hose",
            DeicingDeliveryPhase.Outbound => "tanker leaving",
            DeicingDeliveryPhase.Complete => "delivery complete",
            _ => "delivery waiting",
        };

        internal DeicingDeliveryPhase resumePhase;
        internal BaggageVehiclePose exit;
        internal int gatewayNode, routeStamp;
        internal float timer, retry;
        internal bool hoseAttached;
        internal BaggageVehiclePose destination;
        internal bool failedRoute;
        internal int failedSignature;

        internal DeicingDelivery(DeicingWorkArea area)
        { Area = area; Phase = DeicingDeliveryPhase.Queued; }
    }
}
