using System;
using System.IO;
using AirCity.Aircraft;
using AirCity.EditorTools.Characters;
using AirCity.Vehicles;
using UnityEditor;
using UnityEngine;

namespace AirCity.EditorTools.Vehicles
{
    public static class BaggageBuildSetup
    {
        [MenuItem("AirCity/Vehicles/Create Baggage Build Catalog")]
        public static void Create()
        {
            const string folder="Assets/AirCity/UI/Resources/AirCityBuild";
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            string path=folder+"/Baggage.asset";
            var catalog=AssetDatabase.LoadAssetAtPath<BaggageBuildCatalog>(path);
            if (catalog==null) { catalog=ScriptableObject.CreateInstance<BaggageBuildCatalog>(); AssetDatabase.CreateAsset(catalog,path); }
            catalog.tractorType=VehicleSetup.Type("BaggageTractor"); catalog.cartType=VehicleSetup.Type("BaggageCart"); catalog.loaderType=VehicleSetup.Type("BeltLoader");
            catalog.tractorPrefab=VehicleSetup.Prefab("BaggageTractor"); catalog.cartPrefab=VehicleSetup.Prefab("BaggageCart"); catalog.loaderPrefab=VehicleSetup.Prefab("BeltLoader");
            catalog.bagPropType=VehicleSetup.Type("BaggageProps"); catalog.bagPrefabs=VehicleSetup.BagPrefabs();
            catalog.crewPrefab=RampCrewSetup.Prefab; catalog.crewType=RampCrewSetup.Type;
            catalog.aircraft=AssetDatabase.LoadAssetAtPath<AircraftType>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.aircraft");
            if (!catalog.Ready) throw new InvalidOperationException("Baggage build equipment assets are incomplete.");
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("BAGGAGE BUILD: runtime catalog ready, including equipment, crew and hold configuration.");
        }
    }
}
