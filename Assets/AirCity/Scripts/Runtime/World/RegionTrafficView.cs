using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.World
{
    /// <summary>Light regional traffic on the generated road, sharing its saved vertical profile and game clock.</summary>
    [ExecuteAlways]
    public sealed class RegionTrafficView : MonoBehaviour
    {
        const int Cars = 20;
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<float> distances = new List<float>();
        readonly Matrix4x4[][] instances = { new Matrix4x4[5],new Matrix4x4[5],new Matrix4x4[5],new Matrix4x4[5] };
        Material[] paint;
        Material dark;
        Mesh body, glazing;
        Bounds bounds;
        float length;

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Draw; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; }

        public void Build(ValleyWorld world, PublicRoadGeometry roads)
        {
            path.Clear(); distances.Clear(); length=0f;
            if (world.publicRoad.Count < 2 || roads == null) return;
            foreach(var p in world.publicRoad)
            {
                float nearest=float.MaxValue,height=world.Height(p);
                foreach(var link in roads.links)
                {
                    var d=link.b-link.a;
                    float t=Mathf.Clamp01(Vector2.Dot(p-link.a,d)/Mathf.Max(.001f,d.sqrMagnitude));
                    float range=(p-(link.a+d*t)).sqrMagnitude;
                    if(range>=nearest)continue;
                    nearest=range;height=Mathf.Lerp(link.heightA,link.heightB,t);
                }
                var at=new Vector3(p.x,height+.2f,p.y);
                if(path.Count>0)length+=Vector3.Distance(path[path.Count-1],at);
                path.Add(at);distances.Add(length);
            }
            if(body==null)CreateModels();
            bounds=new Bounds(path[0],Vector3.one*20f);
            foreach(var p in path)bounds.Encapsulate(new Bounds(p,Vector3.one*20f));
            SetClock(0f);
        }

        void CreateModels()
        {
            var hull=new LandscapeMesh();var trim=new LandscapeMesh();
            hull.Box(new Vector3(0,.52f,0),new Vector3(1.85f,.65f,4.35f));
            hull.Box(new Vector3(0,1.05f,-.2f),new Vector3(1.62f,.6f,2.1f));
            trim.Box(new Vector3(0,1.15f,.88f),new Vector3(1.46f,.40f,.035f));
            trim.Box(new Vector3(0,1.15f,-1.27f),new Vector3(1.46f,.40f,.035f));
            trim.Box(new Vector3(-.825f,1.13f,-.2f),new Vector3(.035f,.4f,1.8f));
            trim.Box(new Vector3(.825f,1.13f,-.2f),new Vector3(.035f,.4f,1.8f));
            foreach(float x in new[]{-.93f,.93f})foreach(float z in new[]{-1.38f,1.38f})
                trim.Box(new Vector3(x,.28f,z),new Vector3(.20f,.56f,.60f));
            body=hull.Create("Regional car body");glazing=trim.Create("Regional car windows and wheels");
            body.hideFlags=glazing.hideFlags=HideFlags.DontSave;
            paint=new[]{Mat("Ivory regional car",new Color(.82f,.81f,.72f)),Mat("Blue regional car",new Color(.15f,.32f,.43f)),
                Mat("Red regional car",new Color(.49f,.18f,.12f)),Mat("Silver regional car",new Color(.42f,.48f,.48f))};
            dark=Mat("Regional car trim",new Color(.055f,.09f,.11f));
        }

        public void SetClock(float seconds)
        {
            if(path.Count<2||length<1f)return;
            for(int car=0;car<Cars;car++)
            {
                bool reverse=car%2==0;
                float speed=8.5f+(car%4)*1.6f;
                float along=Mathf.Repeat(car/(float)Cars*length+seconds*speed,length);
                if(reverse)along=length-along;
                int segment=1;
                while(segment<distances.Count-1&&distances[segment]<along)segment++;
                var a=path[segment-1];var b=path[segment];
                var forward=(b-a).normalized;
                float t=Mathf.InverseLerp(distances[segment-1],distances[segment],along);
                var at=Vector3.Lerp(a,b,t);
                if(reverse)forward=-forward;
                var right=Vector3.Cross(Vector3.up,forward).normalized;
                at+=right*1.65f;
                instances[car%4][car/4]=Matrix4x4.TRS(at,Quaternion.LookRotation(forward,Vector3.up),Vector3.one);
            }
        }

        void Draw(ScriptableRenderContext context,Camera camera)
        {
            if(body==null||path.Count<2||!isActiveAndEnabled||!SystemInfo.supportsInstancing)return;
            if((camera.cullingMask&(1<<gameObject.layer))==0)return;
            bool shadows=Vector3.Distance(camera.transform.position,bounds.ClosestPoint(camera.transform.position))<800f;
            for(int i=0;i<instances.Length;i++)
            {
                var parameters=new RenderParams(paint[i]){camera=camera,worldBounds=bounds,receiveShadows=true,
                    shadowCastingMode=shadows?ShadowCastingMode.On:ShadowCastingMode.Off,lightProbeUsage=LightProbeUsage.Off,layer=gameObject.layer};
                Graphics.RenderMeshInstanced(parameters,body,0,instances[i],instances[i].Length);
                parameters.material=dark;
                Graphics.RenderMeshInstanced(parameters,glazing,0,instances[i],instances[i].Length);
            }
        }
        static Material Mat(string name,Color colour)
        {
            var result=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,hideFlags=HideFlags.DontSave,enableInstancing=true};
            result.SetColor("_BaseColor",colour);result.SetFloat("_Smoothness",.35f);return result;
        }
        void OnDestroy()
        {
            Release(body);Release(glazing);Release(dark);
            if(paint!=null)foreach(var material in paint)Release(material);
        }
        static void Release(Object value)
        {if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
