using AirCity.Airfield;
using AirCity.Dev;
using AirCity.Airfield.Building;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

namespace AirCity.Game
{
    public sealed partial class CareerBuildTool
    {
        readonly BuildPlacementPreview modelPreview = new();
        readonly TaxiwayPreview taxiPreview = new();
        GameObject receptionPreview;
        Terrain receptionTerrain;
        float rotationScroll;
        MaterialPropertyBlock previewTint;
        VisitorCentreConstruction visitorPlan;
        Mesh visitorDrivewayMesh;
        MeshFilter visitorDrivewayFilter;
        ulong visitorDrivewaySignature;
        readonly Dictionary<Mesh,Color[]> visitorPreviewColors=new();
        bool? visitorPreviewValid;
        bool visitorSurfacePreview;
        public bool SupportsPlacementRotation => Tool == CareerTool.Reception;
        public bool PlacementSnap { get; set; } = true;
        public bool TakesPlacementScroll { get; private set; }
        public float PlacementHeading => BuildingHeading;
        public void RotatePlacement(float degrees)
        {
            if (!SupportsPlacementRotation) return;
            float heading = BuildingHeading + degrees;
            BuildingHeading = Mathf.Repeat(PlacementSnap ? Mathf.Round(heading / 15f) * 15f : heading, 360f);
        }

        void PlacementScroll(Keyboard keys, Mouse mouse)
        {
            TakesPlacementScroll = keys != null && mouse != null && SupportsPlacementRotation && keys.shiftKey.isPressed &&
                !game.view.PointerOverHud && !game.view.KeyboardCaptured;
            game.view.PlacementTakesScroll = TakesPlacementScroll;
            if (!TakesPlacementScroll) { rotationScroll = 0; return; }
            var scroll = mouse.scroll.ReadValue();
            rotationScroll += Mathf.Clamp(Mathf.Abs(scroll.y) >= Mathf.Abs(scroll.x) ? scroll.y : scroll.x, -4, 4);
            int notches = (int)rotationScroll; rotationScroll -= notches;
            if (keys.altKey.isPressed) BuildingHeading = Mathf.Repeat(BuildingHeading + notches, 360);
            else RotatePlacement(notches * (PlacementSnap ? 15 : 5));
        }

        void RefreshModelPreviews(Vector2 point)
        {
            var airport = game.Airport;
            if (airport == null) { HideModelPreviews(); return; }
            if (receptionPreview != null) receptionPreview.SetActive(false);
            modelPreview.Hide(); taxiPreview.Hide();
            var origin = game.State.Site.centre;
            if (Tool == CareerTool.Reception)
            {
                if (receptionPreview != null && (receptionTerrain != game.valley.Terrain ||
                    receptionPreview.GetComponent<VisitorCentreVisual>()?.geometryVersion!=VisitorCentreBuilder.GeometryVersion)) DestroyReceptionPreview();
                if (receptionPreview == null)
                {
                    receptionPreview = game.valley.BuildReception(Vector2.zero,0,0,true,true);
                    receptionPreview.name = "Visitor centre construction preview";
                    receptionTerrain = game.valley.Terrain;
                    receptionPreview.transform.SetParent(transform, false);
                    foreach (var t in receptionPreview.GetComponentsInChildren<Transform>()) t.gameObject.hideFlags = HideFlags.DontSave;
                }
                receptionPreview.SetActive(true);
                receptionPreview.transform.SetPositionAndRotation(new Vector3(point.x, game.State.layout.elevation + .025f, point.y), Quaternion.Euler(0, BuildingHeading, 0));
                RefreshVisitorDriveway();
                game.valley.PreviewVisitorCentre(receptionPreview.transform,visitorPlan!=null ? visitorDrivewayMesh : null,visitorPlan!=null ? visitorDrivewaySignature : 0);
                visitorSurfacePreview=true;
                previewTint ??= new MaterialPropertyBlock();
                foreach (var renderer in receptionPreview.GetComponentsInChildren<Renderer>())
                {
                    previewTint.Clear(); var material = renderer.sharedMaterial;
                    if (material != null && material.HasProperty("_BaseColor")) previewTint.SetColor("_BaseColor", material.GetColor("_BaseColor") *
                        (Valid ? new Color(.78f, 1f, .94f) : new Color(1f, .42f, .32f)));
                    renderer.SetPropertyBlock(previewTint); renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
                foreach(var filter in receptionPreview.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh=filter.sharedMesh;if(mesh==null)continue;
                    bool known=visitorPreviewColors.TryGetValue(mesh,out var source);
                    if(known && visitorPreviewValid==Valid)continue;
                    if(!known)visitorPreviewColors[mesh]=source=mesh.colors;
                    var tint=Valid ? new Color(.72f,1f,.9f,1f) : new Color(1f,.25f,.2f,1f);
                    var colors=new Color[source.Length];
                    for(int i=0;i<colors.Length;i++)colors[i]=source[i]*tint;
                    mesh.colors=colors;
                }
                visitorPreviewValid=Valid;
            }
            else if (Tool == CareerTool.Stand)
            {
                var stand = CareerConstruction.StandPreview(game.State, point);
                if (stand == null) return;
                stand.position -= new Vector3(origin.x, 0, origin.y);
                var layout = StandLayout.Of(stand); if (layout == null) return;
                modelPreview.Begin(airport);
                string key = "career stand " + stand.position + " " + stand.heading;
                if (modelPreview.GeometryChanged(key)) modelPreview.Stand(airport, layout, System.Array.Empty<AirCity.Aircraft.AircraftType>());
                modelPreview.End(Valid);
            }
            else if (Tool == CareerTool.Runway && dragging)
            {
                var end = EndPoint(); modelPreview.Begin(airport);
                if (modelPreview.GeometryChanged("career runway " + anchor + " " + end))
                    modelPreview.Runway(airport, anchor - origin, end - origin, 18, PavementSurface.Grass);
                modelPreview.End(Valid);
            }
            else if (Tool == CareerTool.Taxiway && draft.Count > 0)
            {
                var end = game.State.layout.SnapTaxi(point, 16f);
                taxiPreview.Show(airport, draft[draft.Count - 1] - origin, end - origin, 7.5f, PavementSurface.Grass, false, RoadClass.None);
            }
        }

        void HideModelPreviews()
        {
            TakesPlacementScroll = false;
            if (game != null && game.view != null) game.view.PlacementTakesScroll = false;
            modelPreview.Hide(); taxiPreview.Hide();
            if (receptionPreview != null) receptionPreview.SetActive(false);
            if(visitorSurfacePreview){game?.valley?.EndBuiltSurfacePreview();visitorSurfacePreview=false;}
        }
        void OnDisable() => HideModelPreviews();
        void OnDestroy()
        {
            modelPreview.Destroy(); taxiPreview.Destroy();
            DestroyReceptionPreview();
        }
        void DestroyReceptionPreview()
        {
            if (receptionPreview == null) return;
            if (Application.isPlaying) Destroy(receptionPreview); else DestroyImmediate(receptionPreview);
            receptionPreview = null; receptionTerrain = null;
            visitorDrivewayMesh=null;visitorDrivewayFilter=null;visitorDrivewaySignature=0;visitorPreviewColors.Clear();
            visitorPreviewValid=null;
        }

        void RefreshVisitorDriveway()
        {
            if(visitorDrivewayFilter==null)
            {
                var child=new GameObject("Visitor centre driveway preview"){hideFlags=HideFlags.DontSave};
                child.transform.SetParent(receptionPreview.transform,false);
                visitorDrivewayFilter=child.AddComponent<MeshFilter>();
                var renderer=child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial=receptionPreview.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
            var connection=visitorPlan?.Road;
            visitorDrivewayFilter.gameObject.SetActive(connection!=null);
            if(connection==null)return;
            ulong signature=RoadConstructionPlan.Signature(connection.Draft);
            if(signature==visitorDrivewaySignature && visitorDrivewayMesh!=null)return;
            visitorDrivewaySignature=signature;
            var mesh=new MeshData();var color=new Color(.13f,.15f,.17f).linear;
            foreach(int id in connection.NewSegments)
            {
                var road=connection.Draft.Segment(id);
                foreach(var span in connection.Draft.Path(road).Spans())
                {
                    var across=Geo.X0Z(Geo.Right(Geo.XZ(span.b-span.a).normalized))*road.Width*.5f;
                    int Vertex(Vector3 p)=>mesh.Vertex(receptionPreview.transform.InverseTransformPoint(p+Vector3.up*.055f),Vector3.up,color,Vector4.zero);
                    mesh.Quad(0,Vertex(span.a-across),Vertex(span.b-across),Vertex(span.b+across),Vertex(span.a+across));
                }
            }
            if(visitorDrivewayMesh!=null)visitorPreviewColors.Remove(visitorDrivewayMesh);
            visitorDrivewayMesh=mesh.Apply(visitorDrivewayMesh,"Visitor centre driveway preview");
            visitorDrivewayFilter.sharedMesh=visitorDrivewayMesh;
        }
    }
}
