using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Temporary editing cutaway. It changes rendering and picking only, never terrain, saves or routing.</summary>
    public sealed class UndergroundRoadView : MonoBehaviour
    {
        public bool Visible {get;private set;}
        public float Height {get;private set;}
        readonly List<(Behaviour item,bool enabled)> hidden=new List<(Behaviour,bool)>();
        readonly List<(Renderer item,bool enabled)> renderers=new List<(Renderer,bool)>();
        readonly List<(Camera camera,CameraClearFlags flags,Color colour)> cameras=new List<(Camera,CameraClearFlags,Color)>();
        GameObject floor;
        Material floorMaterial;
        public void Show(bool visible,float height)
        {
            if(visible==Visible && (!visible || Mathf.Abs(height-Height)<.25f))return;
            Restore();Visible=visible;Height=height;
            Shader.SetGlobalFloat("_AirCityUndergroundCut",visible?1:0);Shader.SetGlobalFloat("_AirCityUndergroundHeight",height);
            if(!visible)return;
            foreach(var camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if(camera.cameraType!=CameraType.Game)continue;
                cameras.Add((camera,camera.clearFlags,camera.backgroundColor));
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.055f,.065f);
            }
            if(floor==null)
            {
                floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Underground construction grid";
                floor.hideFlags=HideFlags.DontSave;floor.transform.SetParent(transform,false);
                Destroy(floor.GetComponent<Collider>());
                floorMaterial=new Material(Resources.Load<Shader>("UndergroundFloor")){hideFlags=HideFlags.DontSave};
                floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
                floor.transform.localScale=new Vector3(ValleyWorld.Size/10f,1,ValleyWorld.Size/10f);
            }
            floor.transform.position=new Vector3(0,height-15f,0);floor.SetActive(true);
            foreach(var forest in FindObjectsByType<ForestRenderer>(FindObjectsInactive.Exclude))Hide(forest);
            foreach(var cover in FindObjectsByType<GroundCoverRenderer>(FindObjectsInactive.Exclude))Hide(cover);
            foreach(var traffic in FindObjectsByType<RegionTrafficView>(FindObjectsInactive.Exclude))Hide(traffic);
            foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if(renderer.gameObject==floor || renderer.bounds.min.y<=height)continue;
                bool custom=false;foreach(var material in renderer.sharedMaterials)
                    if(material!=null && material.shader!=null && (material.shader.name=="AirCity/AirfieldSurface"||material.shader.name=="AirCity/AirfieldPaint"||material.shader.name=="AirCity/Building"||material.shader.name=="AirCity/RoadStructure")){custom=true;break;}
                if(custom)continue;
                renderers.Add((renderer,renderer.enabled));renderer.enabled=false;
            }
        }
        void Hide(Behaviour behaviour){hidden.Add((behaviour,behaviour.enabled));behaviour.enabled=false;}
        void Restore()
        {
            if(floor!=null)floor.SetActive(false);
            foreach(var item in cameras)if(item.camera!=null){item.camera.clearFlags=item.flags;item.camera.backgroundColor=item.colour;}cameras.Clear();
            foreach(var item in hidden)if(item.item!=null)item.item.enabled=item.enabled;hidden.Clear();
            foreach(var item in renderers)if(item.item!=null)item.item.enabled=item.enabled;renderers.Clear();
        }
        void OnDestroy(){if(floor!=null)Destroy(floor);if(floorMaterial!=null)Destroy(floorMaterial);}
        void OnDisable(){Restore();Visible=false;Shader.SetGlobalFloat("_AirCityUndergroundCut",0);}
    }
}
