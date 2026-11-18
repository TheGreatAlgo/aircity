using System.Collections.Generic;
using AirCity.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Airfield.Building
{
    /// <summary>The visitor centre's eighteen-space public car park and pedestrian forecourt. Every fixture is
    /// derived from VisitorCentreLayout; only the explicit ground mesh owns terrain.</summary>
    public sealed class VisitorCentreSiteBuilder
    {
        const string GroundName="Visitor centre ground";
        readonly MeshData mesh=new MeshData();
        static readonly Color Asphalt=new Color(.23f,.25f,.25f).linear;
        static readonly Color Concrete=new Color(.65f,.64f,.59f).linear;
        static readonly Color Kerb=new Color(.57f,.58f,.54f).linear;
        static readonly Color White=new Color(.91f,.91f,.86f).linear;
        static readonly Color Metal=new Color(.18f,.22f,.22f).linear;
        static readonly Color Wood=new Color(.45f,.30f,.19f).linear;
        static readonly Color Blue=new Color(.14f,.39f,.58f).linear;

        public static GameObject Build(Transform parent,Material material=null)
        {
            var root=new GameObject("Visitor centre site"){hideFlags=HideFlags.DontSave};root.transform.SetParent(parent,false);
            var owner=parent.GetComponentInParent<VisitorCentreVisual>();
            if(material==null)
            {
                material=new Material(Shader.Find("AirCity/Building")){name="Visitor centre site material",hideFlags=HideFlags.DontSave};
                owner?.Own(material);
            }
            var ground=new VisitorCentreSiteBuilder();ground.Ground();ground.Part(root.transform,GroundName,material,owner,false);
            var details=new VisitorCentreSiteBuilder();details.Markings();details.Fixtures();
            details.Part(root.transform,"Visitor centre parking furniture",material,owner,true);
            foreach(float x in new[]{-18.2f,18.2f})
            {
                var light=new GameObject("Car park light"){hideFlags=HideFlags.DontSave};light.transform.SetParent(root.transform,false);
                light.transform.localPosition=new Vector3(x,3.65f,30.55f);
                light.transform.localRotation=Quaternion.Euler(90,0,0);
                var lamp=light.AddComponent<Light>();lamp.type=LightType.Spot;lamp.color=new Color(1f,.86f,.65f);
                lamp.range=18f;lamp.spotAngle=125f;lamp.innerSpotAngle=85f;lamp.intensity=60f;lamp.shadows=LightShadows.None;
                owner?.RegisterNightLight(lamp,60f);
            }
            owner?.SetLighting(AirCity.Aircraft.AircraftLights.Glow);
            return root;
        }
        public static Mesh GroundMesh(GameObject root)
        {
            if(root==null)return null;
            foreach(var part in root.GetComponentsInChildren<MeshFilter>(true))if(part.name==GroundName)return part.sharedMesh;
            return null;
        }
        void Part(Transform parent,string name,Material material,VisitorCentreVisual owner,bool shadows)
        {
            var go=new GameObject(name){hideFlags=HideFlags.DontSave};go.transform.SetParent(parent,false);go.layer=parent.gameObject.layer;
            var result=mesh.Apply(null,name);owner?.Own(result);
            go.AddComponent<MeshFilter>().sharedMesh=result;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=shadows?ShadowCastingMode.On:ShadowCastingMode.Off;
        }
        void Ground()
        {
            Flat(-20,12.75f,20,32,VisitorCentreLayout.ParkingSurface,Asphalt);
            // The public road rounds its free end back from the terminal node. Reach underneath the
            // complete rounded cap, including its corners, rather than merely touching the centreline.
            // PublicRoadGeometry places its pavement .18 m above the graph, with the access profile's
            // .065 m crown; keep this receiving surface 6 mm under that deck to avoid overlapping faces.
            foreach(var span in new[]{(20f,22f),(22f,24.7f)})
                foreach(var cross in new[]{(18.25f,21.5f),(21.5f,24.75f)})
                {
                    float Y(float x,float z)
                    {
                        float road=.174f+.065f*(1f-Mathf.Abs(z-VisitorCentreLayout.AisleCentre)/3.25f);
                        return Mathf.Lerp(VisitorCentreLayout.ParkingSurface,road,Mathf.Clamp01((x-20f)/2f));
                    }
                    Quad(new Vector3(span.Item1,Y(span.Item1,cross.Item1),cross.Item1),new Vector3(span.Item1,Y(span.Item1,cross.Item2),cross.Item2),
                        new Vector3(span.Item2,Y(span.Item2,cross.Item2),cross.Item2),new Vector3(span.Item2,Y(span.Item2,cross.Item1),cross.Item1),Asphalt);
                }
            Flat(-12,6.2f,12,12.75f,VisitorCentreLayout.WalkSurface,Concrete);
            Flat(-1.3f,12.75f,1.3f,18.25f,VisitorCentreLayout.WalkSurface,Concrete);
            Flat(-1.3f,24.75f,1.3f,32,VisitorCentreLayout.WalkSurface,Concrete);
            // The frontage path remains flush with the building's accessible slab and gently descends to
            // the asphalt at both crossing ends. No stairs block a wheelchair route to either accessible bay.
            Quad(new Vector3(-1.3f,.075f,18.25f),new Vector3(-1.3f,.035f,18.7f),new Vector3(1.3f,.035f,18.7f),new Vector3(1.3f,.075f,18.25f),Concrete);
            Quad(new Vector3(-1.3f,.035f,24.3f),new Vector3(-1.3f,.075f,24.75f),new Vector3(1.3f,.075f,24.75f),new Vector3(1.3f,.035f,24.3f),Concrete);
            Quad(new Vector3(-7,.075f,12.75f),new Vector3(-7,.035f,13.2f),new Vector3(-5.8f,.035f,13.2f),new Vector3(-5.8f,.075f,12.75f),Concrete);
        }
        void Markings()
        {
            const float y=.043f;
            foreach(var bay in VisitorCentreLayout.ParkingBays)
            {
                float x0=bay.centre.x-bay.width*.5f,x1=bay.centre.x+bay.width*.5f;
                float z0=bay.centre.y-bay.length*.5f,z1=bay.centre.y+bay.length*.5f;
                Line(new Vector2(x0,z0),new Vector2(x0,z1),.075f,y,White);
                Line(new Vector2(x1,z0),new Vector2(x1,z1),.075f,y,White);
                Line(new Vector2(x0,z0+(bay.frontRow?0:bay.length)),new Vector2(x1,z0+(bay.frontRow?0:bay.length)),.075f,y,White);
                float stop=bay.frontRow?z0+.65f:z1-.65f;
                Box(new Vector3(bay.centre.x,.105f,stop),new Vector3(1.65f,.14f,.16f),Kerb);
                foreach(float side in new[]{-.55f,.55f})Box(new Vector3(bay.centre.x+side,.180f,stop),new Vector3(.12f,.012f,.17f),White);
                if(bay.accessible)
                {
                    Flat(bay.centre.x-1.15f,bay.centre.y-.95f,bay.centre.x+1.15f,bay.centre.y+1.05f,y+.001f,Blue);
                    Wheelchair(new Vector2(bay.centre.x,bay.centre.y),y+.004f);
                }
            }
            // Shared access hatch between the two wider bays, leading directly to the front sidewalk.
            Line(new Vector2(-7,12.75f),new Vector2(-7,18.25f),.08f,y,White);
            Line(new Vector2(-5.8f,12.75f),new Vector2(-5.8f,18.25f),.08f,y,White);
            for(float z=13.25f;z<17.7f;z+=.65f)Line(new Vector2(-6.96f,z),new Vector2(-5.84f,z+.55f),.065f,y,White);
            // Zebra stripes face the pedestrian route; the drive aisle is unobstructed between them.
            for(float z=18.9f;z<24.2f;z+=.85f)Flat(-1.18f,z,1.18f,z+.42f,y+.001f,White);
            foreach(float x in new[]{-10f,10f})
            {
                float z=x<0?20.3f:22.7f;float sign=x<0?1:-1;
                Line(new Vector2(x-sign*1.15f,z),new Vector2(x+sign*1.15f,z),.12f,y,White);
                Line(new Vector2(x+sign*1.15f,z),new Vector2(x+sign*.45f,z+.55f),.12f,y,White);
                Line(new Vector2(x+sign*1.15f,z),new Vector2(x+sign*.45f,z-.55f),.12f,y,White);
            }
            // Give-way bars at the lot's exit. The road itself is generated from the real public graph.
            for(float z=21.9f;z<24.7f;z+=.65f)Flat(18.8f,z,19.05f,z+.42f,y,White);
        }
        void Fixtures()
        {
            // Perimeter curbs have a full-width opening at the public road. End curbs stop outside the crossing.
            Beam(new Vector3(-20,.10f,12.8f),new Vector3(-20,.10f,32),.20f,.15f,Kerb);
            Beam(new Vector3(-20,.10f,32),new Vector3(20,.10f,32),.20f,.15f,Kerb);
            Beam(new Vector3(20,.10f,12.8f),new Vector3(20,.10f,17),.20f,.15f,Kerb);
            Beam(new Vector3(20,.10f,26),new Vector3(20,.10f,32),.20f,.15f,Kerb);
            Beam(new Vector3(-20,.10f,12.75f),new Vector3(-12,.10f,12.75f),.20f,.15f,Kerb);
            Beam(new Vector3(12,.10f,12.75f),new Vector3(20,.10f,12.75f),.20f,.15f,Kerb);
            // Slim bollards guard the door apron while leaving an unobstructed 2.6 m centre path.
            foreach(float x in new[]{-10f,-7f,-4f,4f,7f,10f})
            {Cylinder(new Vector3(x,.50f,11.6f),.075f,.85f,Metal,8);Cylinder(new Vector3(x,.83f,11.6f),.081f,.08f,White,8);}
            Bench(new Vector3(-8.5f,0,8.8f));Bench(new Vector3(8.5f,0,8.8f));
            foreach(float x in new[]{-18.2f,18.2f})
            {
                Lamp(new Vector3(x,0,31));
                Planter(new Vector3(x,0,27.4f),new Vector3(1.5f,.35f,5f));
            }
            Planter(new Vector3(15.8f,0,15.5f),new Vector3(5.8f,.35f,4.4f));
            // Three separate inverted U racks sit outside the main pedestrian route.
            foreach(float x in new[]{14f,15.25f,16.5f})
            {
                Beam(new Vector3(x,.1f,8),new Vector3(x,1f,8),.06f,.06f,Metal);
                Beam(new Vector3(x,1f,8),new Vector3(x,1f,9.1f),.06f,.06f,Metal);
                Beam(new Vector3(x,1f,9.1f),new Vector3(x,.1f,9.1f),.06f,.06f,Metal);
            }
            ParkingSign(new Vector3(19.2f,0,27));
        }
        void Bench(Vector3 at)
        {
            foreach(float x in new[]{-.8f,.8f})
            {Box(at+new Vector3(x,.28f,0),new Vector3(.09f,.5f,.65f),Metal);Box(at+new Vector3(x,.75f,-.27f),new Vector3(.09f,.65f,.08f),Metal);}
            for(int i=0;i<4;i++)Box(at+new Vector3(0,.53f,-.23f+i*.15f),new Vector3(2.05f,.07f,.12f),Wood);
            for(int i=0;i<3;i++)Box(at+new Vector3(0,.73f+i*.14f,-.30f),new Vector3(2.05f,.11f,.06f),Wood);
        }
        void Lamp(Vector3 at)
        {
            Cylinder(at+Vector3.up*1.95f,.065f,3.8f,Metal,8);
            Box(at+new Vector3(0,3.85f,-.38f),new Vector3(.25f,.12f,1f),Metal);
            int first=mesh.Count;
            Box(at+new Vector3(0,3.78f,-.48f),new Vector3(.18f,.025f,.56f),new Color(.98f,.91f,.69f).linear);
            for(int i=first;i<mesh.Count;i++)
            {mesh.uv0[i]=new Vector4(1,.85f,10,3.78f);mesh.uv1[i]=new Vector4(2,2.5f,0,0);}
        }
        void Planter(Vector3 at,Vector3 size)
        {
            Box(at+Vector3.up*(size.y*.5f+.035f),size,Kerb);
            Box(at+Vector3.up*(size.y+.045f),new Vector3(size.x-.18f,.025f,size.z-.18f),new Color(.25f,.22f,.17f).linear);
            for(float z=-size.z*.36f;z<=size.z*.36f;z+=.85f)
                for(float x=-size.x*.32f;x<=size.x*.32f;x+=.85f)
                {
                    var p=at+new Vector3(x,size.y+.05f,z);
                    foreach(var d in new[]{new Vector3(.18f,.36f,0),new Vector3(-.18f,.30f,.12f),new Vector3(.04f,.34f,-.20f)})
                        Beam(p,p+d,.13f,.12f,new Color(.30f,.39f,.24f).linear);
                }
        }
        void ParkingSign(Vector3 at)
        {
            Cylinder(at+Vector3.up*.85f,.04f,1.6f,Metal,8);
            Box(at+Vector3.up*1.65f,new Vector3(.72f,.72f,.07f),Blue);
            // A simple P on both sides remains legible at game scale, independent of system fonts.
            foreach(float z in new[]{-.041f,.041f})
            {
                Box(at+new Vector3(-.14f,1.65f,z),new Vector3(.07f,.46f,.008f),White);
                Box(at+new Vector3(0,1.85f,z),new Vector3(.28f,.065f,.008f),White);
                Box(at+new Vector3(0,1.65f,z),new Vector3(.28f,.065f,.008f),White);
                Box(at+new Vector3(.12f,1.75f,z),new Vector3(.065f,.20f,.008f),White);
            }
        }
        void Wheelchair(Vector2 at,float y)
        {
            Ring(at+new Vector2(-.15f,-.25f),.55f,.10f,y,White);
            Ring(at+new Vector2(-.12f,.67f),.15f,.15f,y,White);
            Line(at+new Vector2(-.10f,.40f),at+new Vector2(-.03f,-.10f),.12f,y,White);
            Line(at+new Vector2(-.03f,-.10f),at+new Vector2(.53f,-.10f),.12f,y,White);
            Line(at+new Vector2(.53f,-.10f),at+new Vector2(.73f,-.65f),.12f,y,White);
            Line(at+new Vector2(.73f,-.65f),at+new Vector2(.98f,-.65f),.12f,y,White);
        }
        void Ring(Vector2 centre,float radius,float thickness,float y,Color color)
        {
            const int count=20;
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;
                var da=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var db=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                Quad(Geo.X0Z(centre+da*(radius-thickness*.5f),y),Geo.X0Z(centre+db*(radius-thickness*.5f),y),
                    Geo.X0Z(centre+db*(radius+thickness*.5f),y),Geo.X0Z(centre+da*(radius+thickness*.5f),y),color);
            }
        }
        void Flat(float x0,float z0,float x1,float z1,float y,Color color)=>
            Quad(new Vector3(x0,y,z0),new Vector3(x0,y,z1),new Vector3(x1,y,z1),new Vector3(x1,y,z0),color);
        void Line(Vector2 a,Vector2 b,float width,float y,Color color)
        {var right=Geo.Right((b-a).normalized)*width*.5f;Quad(Geo.X0Z(a-right,y),Geo.X0Z(b-right,y),Geo.X0Z(b+right,y),Geo.X0Z(a+right,y),color);}
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {
            var n=Vector3.Cross(b-a,c-a).normalized;
            int i=mesh.Vertex(a,n,color,new Vector4(0,0,.08f,0));
            mesh.Vertex(b,n,color,new Vector4(0,0,.08f,0));mesh.Vertex(c,n,color,new Vector4(0,0,.08f,0));mesh.Vertex(d,n,color,new Vector4(0,0,.08f,0));mesh.Quad(0,i,i+1,i+2,i+3);
        }
        void Box(Vector3 centre,Vector3 size,Color color)
        {
            var h=size*.5f;var p=new Vector3[8];
            for(int i=0;i<8;i++)p[i]=centre+new Vector3((i&1)==0?-h.x:h.x,(i&2)==0?-h.y:h.y,(i&4)==0?-h.z:h.z);
            Quad(p[0],p[2],p[3],p[1],color);Quad(p[4],p[5],p[7],p[6],color);
            Quad(p[0],p[4],p[6],p[2],color);Quad(p[1],p[3],p[7],p[5],color);
            Quad(p[2],p[6],p[7],p[3],color);Quad(p[0],p[1],p[5],p[4],color);
        }
        void Beam(Vector3 a,Vector3 b,float width,float height,Color color)
        {
            var forward=(b-a).normalized;var right=Vector3.Cross(Vector3.up,forward);
            if(right.sqrMagnitude<.001f)right=Vector3.right;else right.Normalize();
            var up=Vector3.Cross(forward,right).normalized;
            var x=right*width*.5f;var y=up*height*.5f;
            Quad(a-x-y,b-x-y,b+x-y,a+x-y,color);Quad(a-x+y,a+x+y,b+x+y,b-x+y,color);
            Quad(a-x-y,a-x+y,b-x+y,b-x-y,color);Quad(a+x-y,b+x-y,b+x+y,a+x+y,color);
            Quad(a-x-y,a+x-y,a+x+y,a-x+y,color);Quad(b-x-y,b-x+y,b+x+y,b+x-y,color);
        }
        void Cylinder(Vector3 centre,float radius,float height,Color color,int count)
        {
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count;
                var x=new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);var z=new Vector3(Mathf.Cos(b)*radius,0,Mathf.Sin(b)*radius);
                var down=Vector3.down*height*.5f;var up=-down;
                Quad(centre+x+down,centre+x+up,centre+z+up,centre+z+down,color);
                Quad(centre+up,centre+z+up,centre+x+up,centre+up,color);
            }
        }
    }
}
