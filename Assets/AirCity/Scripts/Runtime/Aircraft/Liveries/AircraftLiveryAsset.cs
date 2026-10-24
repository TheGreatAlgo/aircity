using UnityEngine;

namespace AirCity.Aircraft
{
    /// <summary>A livery saved as an asset (Create ▸ AirCity ▸ Aircraft Livery).</summary>
    [CreateAssetMenu(menuName = "AirCity/Aircraft Livery", fileName = "NewLivery", order = 0)]
    public class AircraftLiveryAsset : ScriptableObject
    {
        public AircraftLivery livery = new AircraftLivery();

#if UNITY_EDITOR
        void OnValidate()
        {
            // live-update aircraft in open scenes that use this asset
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                foreach (var a in FindObjectsByType<AircraftLiveryApplier>(FindObjectsInactive.Exclude))
                    if (a.liveryAsset == this) a.Apply();
            };
        }
#endif
    }
}
