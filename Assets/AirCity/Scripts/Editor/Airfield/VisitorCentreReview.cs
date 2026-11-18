using System;
using System.IO;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.EditorTools.Dev;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>A standalone visual fixture: no player save is opened or changed.</summary>
    public static class VisitorCentreReview
    {
        const string Output="Captures/VisitorCentre/";
        [MenuItem("AirCity/Airfield/Create Visitor Centre Review")]
        public static void Create()
        {
            if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Build(false);
        }
        public static void Run()
        {
            try{Directory.CreateDirectory(Output);Build(true);Debug.Log("VISITOR CENTRE: model, parking and bidirectional public-road connection reviewed.");EditorApplication.Exit(0);}
            catch(Exception exception){Debug.LogException(exception);EditorApplication.Exit(1);}
        }
        static void Build(bool capture)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var priorGlow=AircraftLights.Glow;
            try
            {
                AircraftLights.Glow=0f;Shader.SetGlobalFloat("_AirCityLightGlow",0f);
                RenderSettings.ambientMode=AmbientMode.Trilight;
                RenderSettings.ambientSkyColor=new Color(.66f,.72f,.80f);
                RenderSettings.ambientEquatorColor=new Color(.47f,.48f,.45f);
                RenderSettings.ambientGroundColor=new Color(.24f,.27f,.21f);
                var sun=new GameObject("Daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;
                sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(43,-36,0);RenderSettings.sun=sun;
                var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Review meadow";ground.transform.localScale=new Vector3(35,1,35);
                var grass=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Review grass"};grass.color=new Color(.29f,.36f,.22f);ground.GetComponent<MeshRenderer>().sharedMaterial=grass;
                var host=new GameObject("Visitor centre fixture");
                var material=new Material(Shader.Find("AirCity/Building")){name="Visitor centre review palette"};
                var visitor=VisitorCentreBuilder.Build(host.transform,Vector2.zero,0f,0f,material);
                var owner=visitor.GetComponent<VisitorCentreVisual>();owner.Own(material);owner.Own(grass);
                VisitorCentreSiteBuilder.Build(visitor.transform,material);
                var network=new PublicRoadNetwork();int a=network.AddNode(new Vector3(-120,0,46)),b=network.AddNode(new Vector3(120,0,46));
                network.Node(a).outsideConnection=true;
                if(!network.Connect(a,b,PublicRoadStyle.Rural,RoadStructure.Ground,out var why))throw new InvalidOperationException(why);
                if(!VisitorCentreLayout.TryConnect(network,Vector2.zero,0f,0f,out var plan,out why))throw new InvalidOperationException(why);
                bool connected=false;
                foreach(var node in plan.Draft.nodes)
                    if(node.outsideConnection && plan.Draft.HasRoute(plan.EntranceNode,node.id) && plan.Draft.HasRoute(node.id,plan.EntranceNode))connected=true;
                if(!connected)throw new InvalidOperationException("The review driveway is not connected in both directions.");
                var rural=AssetDatabase.LoadAssetAtPath<PublicRoadProfile>("Assets/AirCity/World/Roads/RuralRoad.asset");
                var access=AssetDatabase.LoadAssetAtPath<PublicRoadProfile>("Assets/AirCity/World/Roads/AirportAccessRoad.asset");
                var geometry=new PublicRoadGeometry(plan.Draft,rural,access,_=>0f);
                RoadPart(host.transform,"Public road and visitor driveway",geometry.pavement,rural.asphalt,owner);
                RoadPart(host.transform,"Road shoulder",geometry.shoulders,rural.shoulder,owner);
                RoadPart(host.transform,"Road markings",geometry.markings,rural.markings,owner);
                RoadPart(host.transform,"Road kerbs",geometry.kerbMesh,material,owner);
                RoadPart(host.transform,"Road structure",geometry.structures,material,owner);
                RoadPart(host.transform,"Road median",geometry.medianGrass,material,owner);
                var camera=new GameObject("Review camera").AddComponent<Camera>();camera.tag="MainCamera";
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.66f,.73f,.80f);
                camera.nearClipPlane=.05f;camera.farClipPlane=1200;camera.fieldOfView=44;
                Aim(camera,new Vector3(34,17,42),new Vector3(0,2,2));
                if(capture)
                {
                    SceneCapture.Capture(camera,Output+"front.png",1800,1125);
                    Aim(camera,new Vector3(18,6,23),new Vector3(0,2.2f,3));SceneCapture.Capture(camera,Output+"entrance.png",1800,1125);
                    Aim(camera,new Vector3(-36,14,-25),new Vector3(0,2,0));SceneCapture.Capture(camera,Output+"rear.png",1800,1125);
                    Aim(camera,new Vector3(76,87,100),new Vector3(5,0,20));SceneCapture.Capture(camera,Output+"site.png",1800,1125);
                    AircraftLights.Glow=1f;Shader.SetGlobalFloat("_AirCityLightGlow",1f);owner.SetLighting(1f);
                    RenderSettings.ambientSkyColor=new Color(.065f,.095f,.15f);RenderSettings.ambientEquatorColor=new Color(.035f,.05f,.075f);
                    RenderSettings.ambientGroundColor=new Color(.015f,.025f,.035f);sun.intensity=.12f;sun.color=new Color(.58f,.68f,1f);
                    camera.backgroundColor=new Color(.05f,.075f,.12f);
                    Aim(camera,new Vector3(34,17,42),new Vector3(0,2,2));SceneCapture.Capture(camera,Output+"night.png",1800,1125);
                    AircraftLights.Glow=0f;Shader.SetGlobalFloat("_AirCityLightGlow",0f);owner.SetLighting(0f);
                    sun.intensity=1.6f;sun.color=Color.white;
                    RenderSettings.ambientSkyColor=new Color(.66f,.72f,.80f);RenderSettings.ambientEquatorColor=new Color(.47f,.48f,.45f);
                    RenderSettings.ambientGroundColor=new Color(.24f,.27f,.21f);camera.backgroundColor=new Color(.66f,.73f,.80f);
                }
                // All model/site meshes are derived DontSave art. Keep this a fresh, unsaved review scene;
                // the menu reconstructs it each time instead of writing a scene that loses its generated art.
            }
            finally{AircraftLights.Glow=priorGlow;Shader.SetGlobalFloat("_AirCityLightGlow",priorGlow);}
        }
        static void RoadPart(Transform parent,string name,Mesh mesh,Material material,VisitorCentreVisual owner)
        {
            if(mesh==null || mesh.vertexCount==0)return;
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=owner.Own(mesh);
            go.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        static void Aim(Camera camera,Vector3 position,Vector3 target){camera.transform.position=position;camera.transform.LookAt(target);}
    }
}
