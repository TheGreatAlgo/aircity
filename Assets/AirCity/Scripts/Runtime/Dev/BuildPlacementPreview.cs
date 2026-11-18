using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Dev
{
    /// <summary>Render-only construction drafts. Uses the final models/builders without registering airport objects.</summary>
    sealed class BuildPlacementPreview
    {
        GameObject root, models, data;
        AirfieldPart surface, paint, building, equipment, roof;
        readonly Dictionary<int, (GameObject source, GameObject instance)> instances = new();
        MaterialPropertyBlock tint;
        string geometry;
        BaggageDepot baggage;
        Runway runway;
        int used;
        public bool Visible => root != null && root.activeSelf;
        public GameObject RootObject => root;
        public int ModelCount => Visible ? used : 0;

        public void Begin(Airport airport)
        {
            if (root == null)
            {
                root = new GameObject("Construction model preview") { hideFlags = HideFlags.DontSave };
                models = new GameObject("Models") { hideFlags = HideFlags.DontSave };
                models.transform.SetParent(root.transform, false);
                surface = new AirfieldPart(root.transform, "Pavement"); paint = new AirfieldPart(root.transform, "Markings");
                building = new AirfieldPart(root.transform, "Building"); equipment = new AirfieldPart(root.transform, "Equipment");
                roof = new AirfieldPart(root.transform, "Roof");
            }
            root.transform.SetParent(airport.transform, false);
            root.transform.localPosition = Vector3.up * .025f;
            root.transform.localRotation = Quaternion.identity; root.transform.localScale = Vector3.one;
            root.SetActive(true); used = 0;
        }

        public void Model(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;
            int slot = used++;
            if (!instances.TryGetValue(slot, out var item) || item.source != prefab)
            {
                if (item.instance != null) AirfieldPart.DestroyAny(item.instance);
                // An inactive parent prevents operational behaviours from starting on a draft.
                models.SetActive(false);
                var clone = Object.Instantiate(prefab, models.transform, false);
                clone.name = prefab.name + " preview";
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
                foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
                foreach (var light in clone.GetComponentsInChildren<Light>(true)) light.enabled = false;
                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true)) renderer.shadowCastingMode = ShadowCastingMode.Off;
                item = (prefab, clone); instances[slot] = item;
                models.SetActive(true);
            }
            item.instance.SetActive(true);
            item.instance.transform.SetLocalPositionAndRotation(position, rotation);
        }

        public bool GeometryChanged(string key)
        {
            if (geometry == key) return false;
            geometry = key;
            foreach (var part in new[] { surface, paint, building, equipment, roof })
                if (part.Renderer != null) part.Renderer.enabled = false;
            return true;
        }

        public void Pavement(Airport airport, IList<Vector2> corners, PavementSurface pavement)
        {
            var builder = new SurfaceBuilder(Surfaces.Count);
            if (corners.Count >= 3 && !Geo.SelfIntersects(corners))
                builder.Polygon((int)pavement, corners, AirfieldHeights.ApronOf(pavement), AirfieldKind.Apron,
                    pavement.IsPaved() ? 0 : SurfaceBuilder.Fringe);
            Surface(airport, builder);
        }

        public void Stand(Airport airport, StandLayout layout, IEnumerable<AircraftType> types)
        {
            var s = new SurfaceBuilder(Surfaces.Count); var p = new PaintBuilder();
            new StandBuilder().Build(layout, s, p, types); Surface(airport, s); Paint(airport, p);
        }

        public void Terminal(Airport airport, Terminal terminal)
        {
            var builder = new TerminalBuilder(); builder.Build(terminal);
            building.Set(builder.mesh.Apply(building.Target, "Terminal draft", false, 2), airport.building);
        }

        public void Hangar(Airport airport, HangarSize size)
        {
            var builder = new HangarBuilder(true, size: size); builder.Build();
            building.Set(builder.mesh.Apply(building.Target,"Hangar draft",false,2),HangarBuilder.MaterialFor(airport, size));
        }

        public void Runway(Airport airport, Vector2 from, Vector2 to, float width, PavementSurface pavement)
        {
            EnsureData();
            if (runway == null) runway = data.AddComponent<Runway>();
            runway.start = Geo.X0Z(from); runway.end = Geo.X0Z(to); runway.width = width; runway.surface = pavement;
            var s = new SurfaceBuilder(2); var p = new PaintBuilder(); var lights = new LightBuilder();
            RunwayBuilder.Build(runway, s, p, lights);
            surface.Set(s.mesh.Apply(surface.Target, "Runway draft"), airport.SurfaceMaterial(pavement), airport.SurfaceMaterial(PavementSurface.Asphalt));
            Paint(airport, p);
            equipment.Set(lights.ApplyFixtures(equipment.Target, "Runway draft cones"), airport.fixture);
        }

        public void Baggage(Airport airport, BaggageDepotSize size)
        {
            EnsureData();
            if (baggage == null) baggage = data.AddComponent<BaggageDepot>();
            baggage.size = size; baggage.firstBay = Vector2.zero; baggage.bearing = 0;
            var art = new BaggageDepotArt(baggage); art.Build();
            building.Set(art.shell.Apply(building.Target, "Baggage draft shell"), airport.building);
            equipment.Set(art.equipment.Apply(equipment.Target, "Baggage draft machinery"), airport.building);
            roof.Set(art.roof.Apply(roof.Target, "Baggage draft roof"), airport.building); Paint(airport, art.paint);
        }

        void EnsureData()
        {
            if (data != null) return;
            data = new GameObject("Construction preview data") { hideFlags = HideFlags.DontSave };
            data.SetActive(false);
        }

        public void Pose(Vector2 position, float heading)
        {
            root.transform.localPosition = new Vector3(position.x, .025f, position.y);
            root.transform.localRotation = Quaternion.Euler(0, heading, 0);
        }

        public void Paint(Airport airport, PaintBuilder builder) => paint.Set(builder.Apply(paint.Target, "Construction draft paint"), airport.paint);
        void Surface(Airport airport, SurfaceBuilder builder) => surface.Set(builder.mesh.Apply(surface.Target, "Construction draft surface"),
            airport.SurfaceMaterial(PavementSurface.Dirt), airport.SurfaceMaterial(PavementSurface.Asphalt),
            airport.SurfaceMaterial(PavementSurface.Concrete), airport.SurfaceMaterial(PavementSurface.Grass));

        public void End(bool valid)
        {
            tint ??= new MaterialPropertyBlock();
            foreach (var pair in instances) if (pair.Key >= used) pair.Value.instance.SetActive(false);
            // Preserve all texture maps and vertex colours. The outline carries validity for custom building shaders.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var color = valid ? new Color(.78f, 1f, .94f, 1) : new Color(1f, .42f, .32f, 1);
                tint.Clear();
                var material = renderer.sharedMaterial;
                if (material != null && material.HasProperty("_BaseColor")) tint.SetColor("_BaseColor", material.GetColor("_BaseColor") * color);
                if (material != null && material.HasProperty("_Color")) tint.SetColor("_Color", material.GetColor("_Color") * color);
                renderer.SetPropertyBlock(tint);
            }
        }

        public void Hide() { if (root != null) root.SetActive(false); }
        public void Destroy()
        {
            surface?.Destroy(); paint?.Destroy(); building?.Destroy(); equipment?.Destroy(); roof?.Destroy();
            AirfieldPart.DestroyAny(root); AirfieldPart.DestroyAny(data);
            root = models = data = null; baggage = null; geometry = null; instances.Clear();
        }
    }
}
