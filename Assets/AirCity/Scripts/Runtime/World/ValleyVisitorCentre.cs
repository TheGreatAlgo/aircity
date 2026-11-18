using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.World
{
    public partial class ValleyView
    {
        GameObject visitorCentreRoot;
        Vector2 visitorCentrePosition;
        float visitorCentreElevation,visitorCentreHeading;
        bool visitorCentreParking;
        int visitorCentreVersion,mappedVisitorCentreVersion=-1;
        Matrix4x4 visitorPreviewMatrix;
        PublicRoadGeometry visitorPreviewRoads;
        int visitorPreviewAirportVersion;
        ulong visitorPreviewDraft;

        public void PreviewVisitorCentre(Transform root,Mesh driveway,ulong signature)
        {
            var matrix=root.localToWorldMatrix;int version=builtAirport!=null ? builtAirport.LayoutVersion : -1;
            if(constructionActive && visitorPreviewMatrix==matrix && visitorPreviewRoads==roadGeometry &&
                visitorPreviewAirportVersion==version && visitorPreviewDraft==signature)return;
            visitorPreviewMatrix=matrix;visitorPreviewRoads=roadGeometry;visitorPreviewAirportVersion=version;visitorPreviewDraft=signature;
            PreviewBuiltSurfaces(null,Matrix4x4.identity,additional:map=>
            {
                foreach(var polygon in VisitorCentreLayout.PavedPolygons(Vector2.zero,0f,true))
                {
                    Vector3 Point(int i)=>matrix.MultiplyPoint3x4(new Vector3(polygon[i].x,0f,polygon[i].y));
                    for(int i=1;i+1<polygon.Length;i++)map.AddTriangle(new BuiltSurfaceMap.Triangle(Point(0),Point(i),Point(i+1),false));
                }
                if(driveway!=null)map.AddMesh(driveway,matrix,false);
            });
        }

        void RegisterVisitorCentre(GameObject root,Vector2 position,float elevation,float heading,bool parking)
        {
            visitorCentreRoot=root;visitorCentrePosition=position;visitorCentreElevation=elevation;
            visitorCentreHeading=heading;visitorCentreParking=parking;visitorCentreVersion++;
            var changed=SyncBuiltSurfaces();
            if(changed.HasValue && data!=null)RefreshLand(changed.Value);
        }

        void AddVisitorCentre(BuiltSurfaceMap map,bool foundation=true)
        {
            if(visitorCentreRoot==null || !visitorCentreRoot.activeSelf)return;
            foreach(var polygon in VisitorCentreLayout.PavedPolygons(Vector2.zero,0f,visitorCentreParking))
            {
                Vector3 Point(int i)
                {
                    var p=VisitorCentreLayout.Point(visitorCentrePosition,visitorCentreHeading,polygon[i]);
                    return new Vector3(p.x,visitorCentreElevation,p.y);
                }
                for(int i=1;i+1<polygon.Length;i++)map.AddTriangle(new BuiltSurfaceMap.Triangle(Point(0),Point(i),Point(i+1),foundation));
            }
        }
    }
}
