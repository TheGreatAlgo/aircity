using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Aircraft;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Airfield
{
    /// <summary>Portable gate layouts for the sandbox. Asset names are resolved against the airport's catalog,
    /// rather than writing Unity instance IDs that would break after restarting the game.</summary>
    public sealed class GatePresetStore : IDisposable
    {
        [Serializable] public sealed class Entry { public string id, name; }
        [Serializable] sealed class Appearance
        {
            public string asset;
            public Color cladding, glazing, roof;
            public float glazingAmount;
        }
        [Serializable] sealed class FileData
        {
            public int version = 1;
            public string name;
            public BridgeHead head;
            public List<BridgeHead> additionalHeads;
            public List<JetBridge> bridges;
            public List<StandServiceConfiguration> aircraft;
            public string[] families, aircraftModels;
            public Appearance[] bridgeAppearances, headAppearances;
        }

        readonly string directory;
        readonly List<JetBridgeDefinition> definitions;
        readonly List<JetBridgeAppearance> appearances;
        readonly List<AircraftType> types;
        readonly List<Object> owned = new List<Object>();
        public string DirectoryPath => directory;

        public GatePresetStore(string directory, IEnumerable<JetBridgeDefinition> definitions,
            IEnumerable<JetBridgeAppearance> appearances, IEnumerable<AircraftType> types)
        {
            this.directory = directory;
            this.definitions = definitions.Where(x => x != null).Distinct().ToList();
            this.appearances = appearances.Where(x => x != null).Distinct().ToList();
            this.types = types.Where(x => x != null).Distinct().ToList();
        }

        public List<Entry> List()
        {
            var entries = new List<Entry>();
            if (!Directory.Exists(directory)) return entries;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (!Guid.TryParseExact(id, "N", out _)) continue;
                try
                {
                    var data = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
                    if (data != null && data.version == 1 && !string.IsNullOrWhiteSpace(data.name))
                        entries.Add(new Entry { id = id, name = data.name });
                }
                catch (ArgumentException) { }
                catch (IOException) { }
            }
            entries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return entries;
        }

        static Appearance Style(JetBridgeAppearance appearance) => appearance == null ? null : new Appearance
        {
            asset = appearance.name, cladding = appearance.cladding, glazing = appearance.glazing,
            roof = appearance.roof, glazingAmount = appearance.glazingAmount,
        };

        public Entry Save(string name, Stand stand)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the preset a name.");
            var data = new FileData
            {
                name = name.Trim(), head = stand.head.Clone(), additionalHeads = stand.additionalHeads.ConvertAll(x => x.Clone()),
                bridges = stand.bridges.ConvertAll(x => x.Clone()), aircraft = stand.serviceConfigurations.ConvertAll(x => x.Clone()),
                families = stand.bridges.Select(x => x.definition != null ? x.definition.name : "").ToArray(),
                aircraftModels = stand.serviceConfigurations.Select(x => x.aircraft != null ? x.aircraft.model : "").ToArray(),
                bridgeAppearances = stand.bridges.Select(x => Style(x.appearance)).ToArray(),
                headAppearances = new[] { Style(stand.head.appearance) }.Concat(stand.additionalHeads.Select(x => Style(x.appearance))).ToArray(),
            };
            // Keep the file independent of this Unity process's object IDs.
            data.head.appearance = null;
            foreach (var h in data.additionalHeads) h.appearance = null;
            foreach (var b in data.bridges) { b.definition = null; b.appearance = null; }
            foreach (var c in data.aircraft) c.aircraft = null;
            var entry = new Entry { id = Guid.NewGuid().ToString("N"), name = data.name };
            Directory.CreateDirectory(directory);
            string path = PathFor(entry.id), pending = path + ".tmp";
            try { File.WriteAllText(pending, JsonUtility.ToJson(data, true)); File.Move(pending, path); }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            return entry;
        }

        string PathFor(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid preset identifier.");
            return Path.Combine(directory, id + ".json");
        }

        static T Resolve<T>(IEnumerable<T> catalog, string name, Func<T, string> key, string kind) where T : Object
        {
            if (string.IsNullOrEmpty(name)) return null;
            var matches = catalog.Where(x => key(x) == name).ToArray();
            if (matches.Length != 1) throw new InvalidDataException($"The preset's {kind} '{name}' is missing or ambiguous.");
            return matches[0];
        }

        JetBridgeAppearance Restore(Appearance style)
        {
            if (style == null) return null;
            var original = Resolve(appearances, style.asset, x => x.name, "appearance");
            var result = original != null ? Object.Instantiate(original) : ScriptableObject.CreateInstance<JetBridgeAppearance>();
            result.name = style.asset ?? "";
            result.hideFlags = HideFlags.DontSave;
            result.cladding = style.cladding; result.glazing = style.glazing; result.roof = style.roof;
            result.glazingAmount = Mathf.Clamp01(style.glazingAmount);
            owned.Add(result);
            return result;
        }

        public GateLayoutPreset Load(string id)
        {
            var data = JsonUtility.FromJson<FileData>(File.ReadAllText(PathFor(id)));
            if (data == null || data.version != 1 || data.head == null || data.bridges == null || data.additionalHeads == null ||
                data.aircraft == null || data.families == null || data.aircraftModels == null || data.bridgeAppearances == null ||
                data.headAppearances == null || data.bridges.Count > JetBridgeRules.MaxPerStand || data.additionalHeads.Count > 64 ||
                data.aircraft.Count > 1024 || data.families.Length != data.bridges.Count || data.aircraftModels.Length != data.aircraft.Count ||
                data.bridgeAppearances.Length != data.bridges.Count || data.headAppearances.Length != data.additionalHeads.Count + 1 ||
                data.bridges.Any(x => x == null) || data.additionalHeads.Any(x => x == null) || data.aircraft.Any(x => x == null))
                throw new InvalidDataException("This gate preset is incomplete or uses an unsupported format.");
            var preset = ScriptableObject.CreateInstance<GateLayoutPreset>();
            preset.name = data.name; preset.hideFlags = HideFlags.DontSave;
            owned.Add(preset);
            preset.head = data.head; preset.additionalHeads = data.additionalHeads; preset.bridges = data.bridges; preset.aircraft = data.aircraft;
            preset.head.appearance = Restore(data.headAppearances[0]);
            for (int i = 0; i < preset.additionalHeads.Count; i++) preset.additionalHeads[i].appearance = Restore(data.headAppearances[i + 1]);
            for (int i = 0; i < preset.bridges.Count; i++)
            {
                preset.bridges[i].definition = Resolve(definitions, data.families[i], x => x.name, "bridge family");
                preset.bridges[i].appearance = Restore(data.bridgeAppearances[i]);
            }
            for (int i = 0; i < preset.aircraft.Count; i++)
                preset.aircraft[i].aircraft = Resolve(types, data.aircraftModels[i], x => x.model, "aircraft");
            return preset;
        }

        public void Dispose()
        {
            foreach (var obj in owned)
                if (obj != null) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
            owned.Clear();
        }
    }
}
