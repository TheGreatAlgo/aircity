using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>The saved road endpoints and generated equipment needed by either bulk or container planning.</summary>
    public sealed class BaggageServiceAccess
    {
        public VehicleType tractorType, cartType, loaderType, unitType;
        public int dispatchNode, arrivalsNode;
        public float dispatchBearing, arrivalsBearing, loaderParkingBearing;
        public Vector2 loaderParking;
        public BaggageServiceAccess(BaggageStation station)
        {
            tractorType = station.tractorType; cartType = station.cartType; loaderType = station.loaderType;
            dispatchNode = station.dispatchNode; arrivalsNode = station.arrivalsNode;
            dispatchBearing = station.dispatchBearing; arrivalsBearing = station.arrivalsBearing;
            loaderParking = station.loaderParking; loaderParkingBearing = station.loaderParkingBearing;
        }
        public BaggageServiceAccess(ContainerStation station)
        {
            unitType = station.unitType;
            tractorType = station.tractorType; cartType = station.dollyType; loaderType = station.loaderType;
            dispatchNode = station.dispatchNode; arrivalsNode = station.arrivalsNode;
            dispatchBearing = station.dispatchBearing; arrivalsBearing = station.arrivalsBearing;
            loaderParking = station.loaderParking; loaderParkingBearing = station.loaderParkingBearing;
        }
    }
}
