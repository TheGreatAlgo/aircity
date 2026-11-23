using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Game
{
    /// <summary>
    /// The complete authored airport, shared by every game mode. Components keep their existing serialized
    /// layout contracts; references are remapped to local identities or stable catalog keys, never session IDs.
    /// Only this explicit set of layout components can be instantiated when loading a save.
    /// </summary>
    [Serializable]
    public sealed class AirportSaveData
    {
        [Serializable] public sealed class Node
        { public string name; public int parent; public Vector3 position, scale; public Quaternion rotation; }
        [Serializable] public sealed class Part { public int node, kind; public string json; }
        [Serializable] public sealed class Appearance { public string name, json; }
        public List<Node> nodes = new List<Node>();
        public List<Part> parts = new List<Part>();
        public List<string> references = new List<string>();
        public List<Appearance> appearances = new List<Appearance>();
        // Save identities: append component kinds, never reorder existing entries.
        static readonly Type[] Kinds = { typeof(Airport), typeof(Runway), typeof(TaxiwayNetwork),
            typeof(MaintenanceDepot), typeof(BaggageDepot), typeof(BaggageStation), typeof(ContainerStation),
            typeof(BridgeRig), typeof(AirCity.Characters.MarshallerPresentation), typeof(DeicingRig), typeof(SmallHangar),
            typeof(FlyingSchool) };
        static readonly Regex Reference = SavedObjectReference.Pattern;

        public static AirportSaveData Capture(Airport airport, GameAssetCatalog catalog)
        {
            foreach (var hangar in airport.Hangars) hangar.CaptureResidentState();
            foreach (var school in airport.FlyingSchools) school.CaptureState();
            var data = new AirportSaveData();
            var objects = new Dictionary<Transform, int>();
            var identities = new Dictionary<long, string>();
            int NodeFor(Transform t)
            {
                if (objects.TryGetValue(t, out int found)) return found;
                int parent = t == airport.transform ? -1 : NodeFor(t.parent);
                int index = data.nodes.Count; objects[t] = index;
                data.nodes.Add(new Node { name = t.name, parent = parent, position = t.localPosition,
                    rotation = t.localRotation, scale = t.localScale });
                identities[SavedObjectReference.Identity(t.gameObject)] = "object:" + index;
                identities[SavedObjectReference.Identity(t)] = "transform:" + index;
                return index;
            }
            NodeFor(airport.transform);
            var components = airport.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(c => c != null && Array.IndexOf(Kinds, c.GetType()) >= 0 &&
                    (c.gameObject.hideFlags & HideFlags.DontSave) == 0 && ActiveLayout(c.transform, airport.transform)).ToArray();
            foreach (var c in components)
            {
                int index = data.parts.Count;
                data.parts.Add(new Part { node = NodeFor(c.transform), kind = Array.IndexOf(Kinds, c.GetType()) });
                identities[SavedObjectReference.Identity(c)] = "part:" + index;
            }
            var embedded = airport.Taxiways.stands.SelectMany(s => s.bridges.Select(b => b.appearance)
                .Concat(s.BridgeHeads.Select(h => h.appearance))).Where(a => a != null && catalog?.Key(SavedObjectReference.Identity(a)) == null).Distinct().ToArray();
            for (int i = 0; i < embedded.Length; i++) identities[SavedObjectReference.Identity(embedded[i])] = "appearance:" + i;
            string Encode(Object source) => Reference.Replace(JsonUtility.ToJson(source), match => {
                    long id = long.Parse(match.Groups[2].Value);
                    if (id == 0) return match.Groups[1].Value + "0";
                    string key = identities.TryGetValue(id, out var local) ? local : catalog?.Key(id);
                    if (key == null) throw new InvalidOperationException("An airport asset is missing from the game catalog (" + source.GetType().Name + "). Update Game Assets before saving.");
                    int slot = data.references.IndexOf(key);
                    if (slot < 0) { slot = data.references.Count; data.references.Add(key); }
                    return match.Groups[1].Value + (slot + 1);
                });
            for (int i = 0; i < components.Length; i++) data.parts[i].json = Encode(components[i]);
            foreach (var appearance in embedded) data.appearances.Add(new Appearance { name = appearance.name, json = Encode(appearance) });
            return data;
        }

        public Airport Restore(Transform parent, GameAssetCatalog catalog)
        {
            if (!Validate(out var problem)) throw new ArgumentException(problem);
            if (!AssetsAvailable(catalog, out problem)) throw new InvalidOperationException(problem);
            var objects = new List<GameObject>(); var components = new List<MonoBehaviour>();
            var embedded = new List<JetBridgeAppearance>();
            try
            {
                foreach (var n in nodes)
                {
                    var go = new GameObject(n.name); go.SetActive(false);
                    go.transform.SetParent(n.parent < 0 ? parent : objects[n.parent].transform, false);
                    go.transform.localPosition = n.position; go.transform.localRotation = n.rotation; go.transform.localScale = n.scale;
                    objects.Add(go);
                }
                foreach (var p in parts) components.Add((MonoBehaviour)objects[p.node].AddComponent(Kinds[p.kind]));
                foreach (var saved in appearances)
                { var appearance = ScriptableObject.CreateInstance<JetBridgeAppearance>(); appearance.name = saved.name; embedded.Add(appearance); }
                objects[0].AddComponent<AirportOwnedAssets>().assets.AddRange(embedded);
                Object Resolve(string key)
                {
                    if (key.StartsWith("part:")) return components[int.Parse(key.Substring(5))];
                    if (key.StartsWith("object:")) return objects[int.Parse(key.Substring(7))];
                    if (key.StartsWith("transform:")) return objects[int.Parse(key.Substring(10))].transform;
                    if (key.StartsWith("appearance:")) return embedded[int.Parse(key.Substring(11))];
                    return catalog?.Resolve(key);
                }
                var resolved = references.Select(key => Resolve(key) ?? throw new InvalidOperationException("Missing saved airport asset: " + key)).ToArray();
                string Decode(string saved) => Reference.Replace(saved, match => {
                        int slot = int.Parse(match.Groups[2].Value);
                        return match.Groups[1].Value + (slot == 0 ? 0 : SavedObjectReference.Identity(resolved[slot - 1]));
                    });
                for (int i = 0; i < embedded.Count; i++) JsonUtility.FromJsonOverwrite(Decode(appearances[i].json), embedded[i]);
                for (int i = 0; i < parts.Count; i++) JsonUtility.FromJsonOverwrite(Decode(parts[i].json), components[i]);
                var airport = (Airport)components.Single(c => c is Airport);
                airport.timeScale = 0f;
                // All identities and fields are restored before any OnEnable rebuilds geometry or teams.
                for (int i = objects.Count - 1; i >= 0; i--) objects[i].SetActive(true);
                return airport;
            }
            catch
            {
                if (objects.Count > 0) { objects[0].SetActive(false); if (Application.isPlaying) Object.Destroy(objects[0]); else Object.DestroyImmediate(objects[0]); }
                throw;
            }
        }

        public bool Validate(out string problem)
        {
            problem = "The saved airport layout is invalid.";
            if (nodes == null || parts == null || references == null || appearances == null || nodes.Count == 0 || parts.Count == 0) return false;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n == null || n.parent < -1 || n.parent >= i || (i == 0 ? n.parent != -1 : n.parent < 0) ||
                    !Finite(n.position) || !Finite(n.scale) || !Finite(new Vector3(n.rotation.x, n.rotation.y, n.rotation.z)) || float.IsNaN(n.rotation.w) || float.IsInfinity(n.rotation.w)) return false;
            }
            if (parts.Count(p => p != null && p.kind == 0 && p.node == 0) != 1 || parts.Count(p => p != null && p.kind == 0) != 1) return false;
            foreach (var p in parts)
            {
                if (p == null || p.node < 0 || p.node >= nodes.Count || p.kind < 0 || p.kind >= Kinds.Length || string.IsNullOrEmpty(p.json)) return false;
            }
            if (parts.GroupBy(p => (p.node, p.kind)).Any(group => group.Count() > 1)) return false;
            foreach (var json in parts.Select(p => p.json).Concat(appearances.Select(a => a?.json)))
            {
                if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{")) return false;
                try { JsonUtility.FromJson<JsonEnvelope>(json); } catch (ArgumentException) { return false; }
                foreach (Match match in Reference.Matches(json))
                    if (!int.TryParse(match.Groups[2].Value, out int slot) || slot < 0 || slot > references.Count) return false;
            }
            foreach (var key in references)
            {
                if (string.IsNullOrEmpty(key)) return false;
                if (key.StartsWith("part:") && (!int.TryParse(key.Substring(5), out int i) || i < 0 || i >= parts.Count)) return false;
                if (key.StartsWith("object:") && (!int.TryParse(key.Substring(7), out int j) || j < 0 || j >= nodes.Count)) return false;
                if (key.StartsWith("transform:") && (!int.TryParse(key.Substring(10), out int k) || k < 0 || k >= nodes.Count)) return false;
                if (key.StartsWith("appearance:") && (!int.TryParse(key.Substring(11), out int a) || a < 0 || a >= appearances.Count)) return false;
            }
            problem = null; return true;
        }
        [Serializable] sealed class JsonEnvelope { }
        public bool AssetsAvailable(GameAssetCatalog catalog, out string problem)
        {
            foreach (var key in references)
                if (!key.StartsWith("part:") && !key.StartsWith("object:") && !key.StartsWith("transform:") && !key.StartsWith("appearance:") && catalog?.Resolve(key) == null)
                { problem = "Missing saved airport asset: " + key; return false; }
            problem = null; return true;
        }
        static bool ActiveLayout(Transform node, Transform airport)
        {
            for (var at = node; at != airport; at = at.parent) if (!at.gameObject.activeSelf) return false;
            return true;
        }
        static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z));
    }
}
