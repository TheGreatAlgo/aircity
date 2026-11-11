using System.Linq;
using AirCity.Aircraft;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>The equipment package for player-built halls, independent of example stations in a scene.</summary>
    public sealed class BaggageBuildCatalog : ScriptableObject
    {
        public VehicleType tractorType,cartType,loaderType,bagPropType;
        public GameObject tractorPrefab,cartPrefab,loaderPrefab,crewPrefab;
        public GameObject[] bagPrefabs;
        public RampCrewType crewType;
        public AircraftType aircraft;
        public static BaggageBuildCatalog Load() => Resources.Load<BaggageBuildCatalog>("AirCityBuild/Baggage");
        public bool Ready => tractorType!=null && cartType!=null && loaderType!=null && tractorPrefab!=null &&
            cartPrefab!=null && loaderPrefab!=null && crewPrefab!=null && crewType!=null && aircraft!=null;

        public BaggageStation AddTeam(BaggageDepot hall,out string reason)
        {
            reason=null;
            if (!Ready) { reason="Baggage equipment package is missing."; return null; }
            var teams=hall.Teams; int carts=2;
            if (teams.Length>=hall.Maximum.teams || hall.Trailers+carts>hall.Maximum.trailers)
            { reason="Hall team or trailer capacity reached."; return null; }
            reason=hall.PlacementProblem(); if (reason!=null) return null;
            hall.Rebuild();
            int bay=Enumerable.Range(0,hall.Maximum.teams).First(i=>!teams.Any(t=>t.depotBay==i));
            var go=new GameObject(hall.name+" team "+(bay+1)); go.SetActive(false); go.transform.SetParent(hall.Airport.transform,false);
            var team=go.AddComponent<BaggageStation>();
            team.tractorType=tractorType; team.cartType=cartType; team.loaderType=loaderType; team.bagPropType=bagPropType;
            team.tractorPrefab=tractorPrefab; team.cartPrefab=cartPrefab; team.loaderPrefab=loaderPrefab;
            team.crewPrefab=crewPrefab; team.crewType=crewType; team.bagPrefabs=bagPrefabs;
            team.cartCount=carts; team.automaticHandling=true;
            team.automaticHold=new BulkHoldConfiguration { aircraft=aircraft,bagCapacity=240,weightCapacity=3600 };
            team.depot=hall; team.depotBay=bay; team.parkAtStand=false;
            hall.ConnectBay(team); go.SetActive(true); hall.Airport.Taxiways.EquipmentPaintChanged(); return team;
        }
    }
}
