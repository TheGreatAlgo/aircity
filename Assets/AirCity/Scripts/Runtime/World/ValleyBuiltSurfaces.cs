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
        public void PreviewBuiltSurfaces(Mesh taxiways,Matrix4x4 toWorld,PublicRoadGeometry roads=null,PublicRoadGeometry unchangedRoads=null,IReadOnlyList<BuiltSurfaceMap.Triangle> taxiwayOwnership=null,
            System.Action<BuiltSurfaceMap> additional=null)
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
                AddHangarFloors(next,builtAirport,false);
            }
            AddVisitorCentre(next,false);
            additional?.Invoke(next);
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
            if(builtSurfaces!=null && mappedRoads==roadGeometry && mappedAirportVersion==version && mappedVisitorCentreVersion==visitorCentreVersion)return null;
            bool airportChanged=mappedAirportVersion!=version || mappedVisitorCentreVersion!=visitorCentreVersion || builtSurfaces==null;
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
                AddHangarFloors(next,builtAirport);AddHangarFloors(airportOnly,builtAirport);
                AddFlyingSchools(next,builtAirport);AddFlyingSchools(airportOnly,builtAirport);
            }
            AddVisitorCentre(next);AddVisitorCentre(airportOnly);
            Rect? bounds=airportOnly.TriangleCount>0?airportOnly.Bounds:(Rect?)null;
            Rect? changed=airportChanged?(airportChange??Union(mappedAirportBounds,bounds)):null;
            if(roadsChanged)changed=Union(changed,roadChange??(next.TriangleCount>0?next.Bounds:(Rect?)null));
            next.Build();var old=builtSurfaces;builtSurfaces=next;mappedRoads=roadGeometry;mappedAirportVersion=version;mappedAirportBounds=bounds;
            mappedVisitorCentreVersion=visitorCentreVersion;BindBuiltSurfaces();old?.Dispose();
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
        static void AddHangarFloors(BuiltSurfaceMap map,Airport airport,bool foundation=true)
        {
            foreach(var hangar in airport.HangarBuildings)
            {
                // Only the opaque floor owns ground. Projecting the roof overhang would cut holes beside
                // the concrete slab in terrain colour, shadow and depth passes.
                float half=hangar.Spec.Width*.5f,depth=hangar.Spec.Depth;
                var matrix=hangar.transform.localToWorldMatrix;
                var a=matrix.MultiplyPoint3x4(new Vector3(-half,0f,-depth));
                var b=matrix.MultiplyPoint3x4(new Vector3(half,0f,-depth));
                var c=matrix.MultiplyPoint3x4(new Vector3(half,0f,0f));
                var d=matrix.MultiplyPoint3x4(new Vector3(-half,0f,0f));
                map.AddTriangle(new BuiltSurfaceMap.Triangle(a,b,c,foundation));
                map.AddTriangle(new BuiltSurfaceMap.Triangle(a,c,d,foundation));
                if(HangarConnection.Approach(hangar,out var approach))
                {
                    var airportMatrix=airport.transform.localToWorldMatrix;
                    Vector3 P(int index)=>airportMatrix.MultiplyPoint3x4(new Vector3(approach[index].x,0f,approach[index].y));
                    map.AddTriangle(new BuiltSurfaceMap.Triangle(P(0),P(1),P(2),foundation));
                    map.AddTriangle(new BuiltSurfaceMap.Triangle(P(0),P(2),P(3),foundation));
                }
            }
        }
        static void AddFlyingSchools(BuiltSurfaceMap map,Airport airport,bool foundation=true)
        {
            // Terrace, forecourt, car park and driveway throat own the ground; the flight line's stands already do.
            var matrix=airport.transform.localToWorldMatrix;
            foreach(var school in airport.FlyingSchools)
                foreach(var polygon in FlyingSchoolLayout.PavedPolygons(school.Origin,school.Heading,school.Lane,school.DrivewaySide))
                {
                    Vector3 P(int index)=>matrix.MultiplyPoint3x4(new Vector3(polygon[index].x,0f,polygon[index].y));
                    for(int i=1;i+1<polygon.Length;i++)map.AddTriangle(new BuiltSurfaceMap.Triangle(P(0),P(i),P(i+1),foundation));
                }
        }
        static void AddOwnership(BuiltSurfaceMap map,IReadOnlyList<BuiltSurfaceMap.Triangle> source,Matrix4x4 matrix,bool foundation=true)
        {
            foreach(var t in source)map.AddTriangle(t.Transform(matrix, foundation));
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
            visitorCentreRoot=null;visitorCentreVersion=0;mappedVisitorCentreVersion=-1;
        }
    }
}
