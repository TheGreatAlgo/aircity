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
        public int cartCount = 1;
        public StandEquipmentLayout.Parking parking;
        public Component owner;
        public BaggageServiceAccess(BaggageStation station)
        {
            owner=station;
            parking=StandEquipmentLayout.For(station);
            tractorType = station.tractorType; cartType = station.cartType; loaderType = station.loaderType;
            cartCount = Mathf.Max(1,station.cartCount);
            dispatchNode = station.dispatchNode; arrivalsNode = station.arrivalsNode;
            dispatchBearing = station.dispatchBearing; arrivalsBearing = station.arrivalsBearing;
            loaderParking = station.loaderParking; loaderParkingBearing = station.loaderParkingBearing;
            if (parking?.Valid == true) { loaderParking=parking.loader.position; loaderParkingBearing=BaggageTrain.Bearing(parking.loader.facing); }
        }
        public BaggageServiceAccess(ContainerStation station)
        {
            owner=station;
            parking=StandEquipmentLayout.For(station);
            unitType = station.unitType;
            tractorType = station.tractorType; cartType = station.dollyType; loaderType = station.loaderType;
            dispatchNode = station.dispatchNode; arrivalsNode = station.arrivalsNode;
            dispatchBearing = station.dispatchBearing; arrivalsBearing = station.arrivalsBearing;
            loaderParking = station.loaderParking; loaderParkingBearing = station.loaderParkingBearing;
            if (parking?.Valid == true) { loaderParking=parking.loader.position; loaderParkingBearing=BaggageTrain.Bearing(parking.loader.facing); }
        }
    }
}
