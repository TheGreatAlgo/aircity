using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>Lifetime of embedded, player-authored bridge appearances restored with an airport.</summary>
    public sealed class AirportOwnedAssets : MonoBehaviour
    {
        public readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        void OnDestroy()
        {
            foreach (var asset in assets)
                if (asset != null) { if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset); }
        }
    }
}
