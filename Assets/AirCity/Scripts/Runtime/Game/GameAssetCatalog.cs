using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Game
{
    /// <summary>Build-time asset identities for portable saves. No editor paths or instance IDs reach disk.</summary>
    public sealed class GameAssetCatalog : ScriptableObject
    {
        [Serializable] public sealed class Entry { public string key; public Object asset; }
        public List<Entry> entries = new List<Entry>();
        public AirCity.Dev.DevAirfieldTool toolDefaults;
        public AirCity.Airfield.BridgeRig bridgeDefaults;
        public AirCity.Characters.MarshallerPresentation marshallerDefaults;
        Dictionary<long, string> keys;
        Dictionary<string, Object> assets;
        public static GameAssetCatalog Load() => Resources.Load<GameAssetCatalog>("AirCityGameAssets");
        void Index()
        {
            if (keys != null) return;
            keys = new Dictionary<long, string>(); assets = new Dictionary<string, Object>();
            foreach (var e in entries) if (e.asset != null)
            { keys[SavedObjectReference.Identity(e.asset)] = e.key; assets[e.key] = e.asset; }
        }
        public string Key(long instance) { Index(); return keys.TryGetValue(instance, out var key) ? key : null; }
        public Object Resolve(string key) { Index(); return assets.TryGetValue(key, out var asset) ? asset : null; }
    }
}
