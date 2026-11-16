using System.Collections.Generic;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.World
{
    public partial class ValleyView
    {
        BuiltSurfaceMap builtSurfaces;
        BuiltSurfaceMap constructionSurfaces;
        bool constructionActive;
        MaterialPropertyBlock surfaceProperties;
        Airport builtAirport;
        PublicRoadGeometry mappedRoads;
        int mappedAirportVersion=-1;
        Rect? mappedAirportBounds,pendingBuiltChange;
        readonly List<GroundCoverInstance> pendingBuiltProps=new List<GroundCoverInstance>();
        internal PublicRoadGeometry RoadGeometry=>roadGeometry;
        public BuiltSurfaceMap BuiltSurfaces=>builtSurfaces;
        public bool HasConstructionPreview=>constructionActive;

        /// <summary>Render-only ownership for a detached construction network. No land or vegetation is edited.</summary>
        public void PreviewBuiltSurfaces(Mesh taxiways,Matrix4x4 toWorld,PublicRoadGeometry roads=null,PublicRoadGeometry unchangedRoads=null,IReadOnlyList<BuiltSurfaceMap.Triangle> taxiwayOwnership=null)
        {
            var next=constructionSurfaces??new BuiltSurfaceMap();next.ResetPreview();
            var publicRoads=roads??roadGeometry;
            if(publicRoads!=null)foreach(var triangle in publicRoads.groundTriangles)next.AddTriangle(triangle);
            if(unchangedRoads!=null)foreach(var triangle in unchangedRoads.groundTriangles)next.AddTriangle(triangle);
            if(builtAirport!=null)
            {
                var matrix=builtAirport.transform.localToWorldMatrix;
                foreach(var runway in builtAirport.Runways)
                    if(runway.isActiveAndEnabled && (runway.gameObject.hideFlags&HideFlags.DontSave)==0)next.AddMesh(runway.SurfaceMesh,matrix,false);
                if(taxiwayOwnership!=null)AddOwnership(next,taxiwayOwnership,toWorld,false);
                else if(taxiways!=null)next.AddMesh(taxiways,toWorld,false);
                else if(builtAirport.Taxiways.GroundOwnership!=null)AddOwnership(next,builtAirport.Taxiways.GroundOwnership,matrix,false);
                else next.AddMesh(builtAirport.Taxiways.SurfaceMesh,matrix,false);
                next.AddMesh(builtAirport.ApronMesh,matrix,false);
            }
            next.Build();constructionSurfaces=next;constructionActive=true;BindBuiltSurfaces();
            if(roadRoot!=null)roadRoot.SetActive(roads==null);
        }
        public void EndBuiltSurfacePreview()
        {
            constructionActive=false;BindBuiltSurfaces();
            if(roadRoot!=null)roadRoot.SetActive(true);
        }
        public GroundCoverInstance[] ConsumeBuiltSurfaceProps(){var props=pendingBuiltProps.ToArray();pendingBuiltProps.Clear();return props;}

        public void SetBuiltAirport(Airport airport)
        {
            if(builtAirport!=airport){builtAirport=airport;mappedAirportVersion=-1;}
            var changed=SyncBuiltSurfaces();
            if(changed.HasValue && data!=null)RefreshLand(changed.Value);
        }
        public Rect? ConsumeBuiltSurfaceChange(){var area=pendingBuiltChange;pendingBuiltChange=null;return area;}
        static Rect? Union(Rect? a,Rect? b)
        {
            if(!a.HasValue)return b;if(!b.HasValue)return a;
            return Rect.MinMaxRect(Mathf.Min(a.Value.xMin,b.Value.xMin),Mathf.Min(a.Value.yMin,b.Value.yMin),
                Mathf.Max(a.Value.xMax,b.Value.xMax),Mathf.Max(a.Value.yMax,b.Value.yMax));
        }
        Rect? SyncBuiltSurfaces(Rect? roadChange=null,Rect? airportChange=null)
        {
            int version=builtAirport!=null?builtAirport.LayoutVersion:-1;
            if(builtSurfaces!=null && mappedRoads==roadGeometry && mappedAirportVersion==version)return null;
            bool airportChanged=mappedAirportVersion!=version || builtSurfaces==null;
            bool roadsChanged=mappedRoads!=roadGeometry;
            var next=new BuiltSurfaceMap();
            if(roadGeometry!=null)foreach(var triangle in roadGeometry.groundTriangles)next.AddTriangle(triangle);
            // Keep the airport bounds independent of region-wide public roads, so local edits stay local.
            using var airportOnly=new BuiltSurfaceMap();
            if(builtAirport!=null)
            {
                var toWorld=builtAirport.transform.localToWorldMatrix;
                foreach(var runway in builtAirport.Runways)
                    if(runway.isActiveAndEnabled && (runway.gameObject.hideFlags&HideFlags.DontSave)==0)
                    {next.AddMesh(runway.SurfaceMesh,toWorld);airportOnly.AddMesh(runway.SurfaceMesh,toWorld);}
                if(builtAirport.Taxiways.GroundOwnership!=null)
                {AddOwnership(next,builtAirport.Taxiways.GroundOwnership,toWorld);AddOwnership(airportOnly,builtAirport.Taxiways.GroundOwnership,toWorld);}
                else {next.AddMesh(builtAirport.Taxiways.SurfaceMesh,toWorld);airportOnly.AddMesh(builtAirport.Taxiways.SurfaceMesh,toWorld);}
                next.AddMesh(builtAirport.ApronMesh,toWorld);airportOnly.AddMesh(builtAirport.ApronMesh,toWorld);
            }
            Rect? bounds=airportOnly.TriangleCount>0?airportOnly.Bounds:(Rect?)null;
            Rect? changed=airportChanged?(airportChange??Union(mappedAirportBounds,bounds)):null;
            if(roadsChanged)changed=Union(changed,roadChange??(next.TriangleCount>0?next.Bounds:(Rect?)null));
            next.Build();var old=builtSurfaces;builtSurfaces=next;mappedRoads=roadGeometry;mappedAirportVersion=version;mappedAirportBounds=bounds;
            BindBuiltSurfaces();old?.Dispose();
            if(changed.HasValue)
            {
                pendingBuiltChange=Union(pendingBuiltChange,changed);
                if(groundDetails!=null)
                {
                    float margin=GroundCoverScatter.MaximumFootprintRadius;var area=changed.Value;
                    pendingBuiltProps.AddRange(groundDetails.QueryBounds(Rect.MinMaxRect(area.xMin-margin,area.yMin-margin,area.xMax+margin,area.yMax+margin)));
                    groundDetails.RefreshBuiltSurfaces(World,builtSurfaces,area,DisplayHeight);
                }
                QueueGroundCover(changed.Value,false);
            }
            return changed;
        }
        static void AddOwnership(BuiltSurfaceMap map,IReadOnlyList<BuiltSurfaceMap.Triangle> source,Matrix4x4 matrix,bool foundation=true)
        {
            foreach(var t in source)map.AddTriangle(new BuiltSurfaceMap.Triangle(matrix.MultiplyPoint3x4(t.a),matrix.MultiplyPoint3x4(t.b),matrix.MultiplyPoint3x4(t.c),foundation&&t.foundation!=0));
        }
        void BindBuiltSurfaces()
        {
            var visible=constructionActive?constructionSurfaces:builtSurfaces;
            if(Terrain==null || visible==null)return;
            surfaceProperties??=new MaterialPropertyBlock();visible.Bind(surfaceProperties);Terrain.SetSplatMaterialPropertyBlock(surfaceProperties);
        }
        void DisposeBuiltSurfaces()
        {
            constructionSurfaces?.Dispose();constructionSurfaces=null;constructionActive=false;
            builtSurfaces?.Dispose();builtSurfaces=null;builtAirport=null;mappedRoads=null;mappedAirportVersion=-1;
            mappedAirportBounds=pendingBuiltChange=null;pendingBuiltProps.Clear();
        }
    }
}
