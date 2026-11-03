using System;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// What a taxiway or road about to be laid will look like (the dev taxiway / road tools): its one straight
    /// stretch built by TaxiwayBuilder like the real thing (surface, paint, lights, verges), fresh (no wear, snow or
    /// grass), into objects of its own. It is never part of the airport (nothing routes over it or maintains it),
    /// and it is lifted a little so it shows over the pavement it starts on. Rebuilt only when it changes.
    ///
    ///   preview.Show(airport, from, to, 15f, PavementSurface.Concrete, lit: false, RoadClass.None);   // every frame
    ///   preview.Hide();
    ///   preview.Destroy();
    /// </summary>
    public sealed class TaxiwayPreview
    {
        /// <summary>How far it's lifted over the airfield's layers (metres): above aprons and junctions, under the
        /// aircraft contact shadow's 3 cm.</summary>
        const float Lift = 0.02f;

        GameObject root;
        TaxiwayNetwork net;
        AirfieldPart surfacePart, paintPart, glowPart, fixturePart, vergePart;
        (Vector2 from, Vector2 to, float width, PavementSurface surface, bool lit, RoadClass road) built;
        bool shown;

        public void Show(Airport airport, Vector2 from, Vector2 to, float width, PavementSurface surface, bool lit, RoadClass road)
        {
            if (airport == null || Vector2.Distance(from, to) < 0.5f)
            {
                Hide();
                return;
            }
            Ensure();
            var t = airport.transform;
            root.transform.SetPositionAndRotation(t.position + t.up * Lift, t.rotation);
            root.transform.localScale = t.lossyScale;
            root.SetActive(true);
            var wanted = (from, to, width, surface, lit, road);
            if (shown && wanted == built) return;
            built = wanted;
            shown = true;

            net.nodes.Clear();
            net.edges.Clear();
            net.nodes.Add(new TaxiwayNode { id = 1, position = Geo.X0Z(from) });
            net.nodes.Add(new TaxiwayNode { id = 2, position = Geo.X0Z(to) });
            net.edges.Add(new TaxiwayEdge { id = 1, a = 1, b = 2, width = width, surface = surface, lit = lit && surface.IsPaved(), road = road });
            var s = new SurfaceBuilder(Surfaces.Count);
            var paint = new PaintBuilder();
            var lights = new LightBuilder();
            var verges = new SurfaceBuilder(1);
            var builder = new TaxiwayBuilder(net, Array.Empty<Runway>());
            builder.Build(s, paint, lights);
            builder.BuildVerges(verges);
            surfacePart.Set(s.mesh.Apply(surfacePart.Target, "Taxiway preview surface"),
                airport.SurfaceMaterial(PavementSurface.Dirt), airport.SurfaceMaterial(PavementSurface.Asphalt),
                airport.SurfaceMaterial(PavementSurface.Concrete), airport.SurfaceMaterial(PavementSurface.Grass));
            paintPart.Set(paint.Apply(paintPart.Target, "Taxiway preview paint"), airport.paint);
            glowPart.Set(lights.ApplyGlows(glowPart.Target, "Taxiway preview light glows"), airport.lightGlow);
            fixturePart.Set(lights.ApplyFixtures(fixturePart.Target, "Taxiway preview fixtures"), airport.fixture);
            vergePart.Set(verges.mesh.Apply(vergePart.Target, "Taxiway preview verges"), airport.grassStrip);
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        public void Destroy()
        {
            surfacePart?.Destroy();
            paintPart?.Destroy();
            glowPart?.Destroy();
            fixturePart?.Destroy();
            vergePart?.Destroy();
            if (net != null) AirfieldPart.DestroyAny(net.gameObject);
            AirfieldPart.DestroyAny(root);
            root = null;
            net = null;
            surfacePart = paintPart = glowPart = fixturePart = vergePart = null;
            shown = false;
        }

        void Ensure()
        {
            if (root != null) return;
            root = new GameObject("Taxiway preview") { hideFlags = HideFlags.DontSave };
            // the builder reads its graph from a network; kept on an inactive object of its own, so it never builds
            // itself and nothing looking for the airport's network finds it
            var data = new GameObject("Taxiway preview graph") { hideFlags = HideFlags.DontSave };
            data.SetActive(false);
            net = data.AddComponent<TaxiwayNetwork>();
            surfacePart = new AirfieldPart(root.transform, "Surface");
            paintPart = new AirfieldPart(root.transform, "Paint");
            glowPart = new AirfieldPart(root.transform, "LightGlows");
            fixturePart = new AirfieldPart(root.transform, "Fixtures");
            vergePart = new AirfieldPart(root.transform, "Verges");
            shown = false;
        }
    }
}
