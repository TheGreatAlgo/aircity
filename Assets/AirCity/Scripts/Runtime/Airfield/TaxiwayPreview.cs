using System;
using AirCity.Airfield.Building;
using AirCity.World;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// What a taxiway or road about to be laid will look like (the dev taxiway / road tools): its one straight
    /// stretch built by TaxiwayBuilder like the real thing (surface, paint, lights, verges), fresh (no wear, snow or
    /// grass), into objects of its own. It is never part of the airport (nothing routes over it or maintains it),
    /// and its own preview materials draw over the still-ungraded terrain without writing depth or altering
    /// shared materials. Rebuilt only when it changes.
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
        AirfieldPart surfacePart, paintPart, glowPart, fixturePart, vergePart,structurePart;
        Material structureMaterial;
        (Vector3 from, Vector3 to, float width, PavementSurface surface, bool lit, RoadClass road, bool naturalEdges) built;
        bool shown;
        TaxiwayDraft draft;
        Airport source;
        ValleyView valley;
        public IReadOnlyList<(Vector3 a,Vector3 b)> Outline=>draft?.outline;

        public void Show(Airport airport,TaxiwayDraft geometry,ValleyView terrain)
        {
            if(airport==null || geometry==null){Hide();return;}
            Ensure();
            root.transform.SetPositionAndRotation(airport.transform.position+airport.transform.up*Lift,airport.transform.rotation);
            root.transform.localScale=airport.transform.lossyScale;root.SetActive(true);
            if(draft==geometry && source==airport)return;
            RestoreSource();draft=geometry;source=airport;valley=terrain;shown=false;
            surfacePart.Set(geometry.surface.mesh.Apply(surfacePart.Target,"Proposed taxiway network"),
                airport.SurfaceMaterial(PavementSurface.Dirt),airport.SurfaceMaterial(PavementSurface.Asphalt),airport.SurfaceMaterial(PavementSurface.Concrete),airport.SurfaceMaterial(PavementSurface.Grass));
            paintPart.Set(geometry.paint.Apply(paintPart.Target,"Proposed taxiway paint"),airport.paint);
            glowPart.Set(geometry.lights.ApplyGlows(glowPart.Target,"Proposed taxiway lights"),airport.lightGlow);
            fixturePart.Set(geometry.lights.ApplyFixtures(fixturePart.Target,"Proposed taxiway fixtures"),airport.fixture);
            vergePart.Set(geometry.verges.mesh.Apply(vergePart.Target,"Proposed taxiway verges"),airport.grassStrip);
            if(structureMaterial==null)structureMaterial=RoadStructureBuilder.Material(null);
            structurePart.Set(geometry.structures.Create("Proposed road structures",structurePart.Target),structureMaterial);
            source.Taxiways.ShowCommittedGeometry(false);
            valley?.PreviewBuiltSurfaces(null,root.transform.localToWorldMatrix,null,null,geometry.ownership);
        }
        void RestoreSource()
        {
            if(source!=null)source.Taxiways.ShowCommittedGeometry(true);
            if(valley!=null)valley.EndBuiltSurfacePreview();
            source=null;valley=null;draft=null;
        }


        public void Show(Airport airport, Vector2 from, Vector2 to, float width, PavementSurface surface, bool lit, RoadClass road)
            => Show(airport, Geo.X0Z(from), Geo.X0Z(to), width, surface, lit, road);

        public void Show(Airport airport, Vector3 from, Vector3 to, float width, PavementSurface surface, bool lit, RoadClass road, bool naturalEdges = false)
        {
            if (airport == null || Vector3.Distance(from, to) < 0.5f)
            {
                Hide();
                return;
            }
            RestoreSource();
            Ensure();
            if(structurePart.Renderer!=null)structurePart.Renderer.enabled=false;
            var t = airport.transform;
            root.transform.SetPositionAndRotation(t.position + t.up * Lift, t.rotation);
            root.transform.localScale = t.lossyScale;
            root.SetActive(true);
            var wanted = (from, to, width, surface, lit, road, naturalEdges);
            if (shown && wanted == built) return;
            built = wanted;
            shown = true;

            net.nodes.Clear();
            net.edges.Clear();
            net.nodes.Add(new TaxiwayNode { id = 1, position = from });
            net.nodes.Add(new TaxiwayNode { id = 2, position = to });
            net.edges.Add(new TaxiwayEdge { id = 1, a = 1, b = 2, width = width, surface = surface, lit = lit && surface.IsPaved(), road = road, naturalEdges = naturalEdges });
            var s = new SurfaceBuilder(Surfaces.Count);
            var paint = new PaintBuilder();
            var lights = new LightBuilder();
            var verges = new SurfaceBuilder(1);
            var builder = new TaxiwayBuilder(net, Array.Empty<Runway>());
            builder.Build(s, paint, lights);
            builder.BuildVerges(verges);
            float Height(Vector2 p)
            {
                Geo.ClosestOnSegment(p, Geo.XZ(from), Geo.XZ(to), out float t);
                return Mathf.Lerp(from.y,to.y,t);
            }
            var axis=Geo.XZ(to-from); var gradient=axis*(to.y-from.y)/Mathf.Max(.001f,axis.sqrMagnitude);
            AirfieldElevation.Warp(s.mesh,Height,_=>gradient);
            AirfieldElevation.Warp(paint.mesh,Height,_=>gradient,true);
            AirfieldElevation.Warp(lights.glows,Height,_=>gradient,true);
            AirfieldElevation.Warp(lights.fixtures,Height,_=>gradient,false,true);
            AirfieldElevation.Warp(verges.mesh,Height,_=>gradient);
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
            RestoreSource();
            if (root != null) root.SetActive(false);
        }

        public void Destroy()
        {
            RestoreSource();
            surfacePart?.Destroy();
            paintPart?.Destroy();
            glowPart?.Destroy();
            fixturePart?.Destroy();
            vergePart?.Destroy();structurePart?.Destroy();AirfieldPart.DestroyAny(structureMaterial);structureMaterial=null;
            if (net != null) AirfieldPart.DestroyAny(net.gameObject);
            AirfieldPart.DestroyAny(root);
            root = null;
            net = null;
            surfacePart = paintPart = glowPart = fixturePart = vergePart = structurePart = null;
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
            surfacePart = new AirfieldPart(root.transform, "Surface") { constructionPreview = true };
            paintPart = new AirfieldPart(root.transform, "Paint") { constructionPreview = true };
            glowPart = new AirfieldPart(root.transform, "LightGlows");
            fixturePart = new AirfieldPart(root.transform, "Fixtures");
            vergePart = new AirfieldPart(root.transform, "Verges") { constructionPreview = true };
            structurePart=new AirfieldPart(root.transform,"Road structures") {constructionPreview=true};
            shown = false;
        }
    }
}
