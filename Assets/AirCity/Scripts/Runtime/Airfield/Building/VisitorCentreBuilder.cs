using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Airfield.Building
{
    /// <summary>A small airfield's timber-and-stone visitor pavilion. +Z is the public entrance.
    /// The 22 x 12 m building retains the reception's original placement frame.</summary>
    public sealed class VisitorCentreBuilder
    {
        public const int GeometryVersion=2;
        public const float Width = 22f, Depth = 12f, FloorHeight = .075f, Height = 6.35f;
        readonly MeshData body = new MeshData(), glazing = new MeshData();
        readonly int detail;
        static readonly Color Cream = C(.82f,.80f,.73f), Timber = C(.52f,.34f,.20f), WoodLight = C(.66f,.46f,.28f),
            Stone = C(.49f,.48f,.43f), StoneDark = C(.32f,.34f,.32f), Frame = C(.18f,.23f,.24f),
            Roof = C(.21f,.27f,.29f), Slab = C(.65f,.64f,.59f), Glass = C(.44f,.63f,.67f),
            Ink = C(.92f,.88f,.76f), Dark = C(.12f,.17f,.18f);

        VisitorCentreBuilder(int detail) { this.detail=detail; }
        static Color C(float r,float g,float b)=>new Color(r,g,b).linear;

        public static GameObject Build(Transform parent,Vector2 position,float elevation,float heading,
            Material building,Material glassMaterial=null)
        {
            var root=new GameObject("Visitor centre") { hideFlags=HideFlags.DontSave };
            root.transform.SetParent(parent,false);
            root.transform.SetPositionAndRotation(new Vector3(position.x,elevation,position.y),Quaternion.Euler(0,heading,0));
            var owner=root.AddComponent<VisitorCentreVisual>();
            owner.geometryVersion=GeometryVersion;
            if (building==null || building.shader==null || building.shader.name!="AirCity/Building")
                building=owner.Own(new Material(Shader.Find("AirCity/Building")) { name="Visitor centre palette",hideFlags=HideFlags.DontSave });
            // Water is never a glazing fallback. A thin real pane lets the entrance hall remain visible.
            if (glassMaterial==null || glassMaterial.renderQueue<2900 || glassMaterial.shader.name.Contains("Water"))
            {
                glassMaterial=owner.Own(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name="Visitor centre architectural glass",hideFlags=HideFlags.DontSave });
                glassMaterial.SetColor("_BaseColor",new Color(.31f,.49f,.53f,.21f));
                glassMaterial.SetFloat("_Surface",1); glassMaterial.SetFloat("_Blend",0);
                glassMaterial.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
                glassMaterial.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                glassMaterial.SetFloat("_ZWrite",0); glassMaterial.SetFloat("_Cull",(float)CullMode.Off);
                glassMaterial.SetFloat("_Smoothness",.92f); glassMaterial.SetFloat("_Metallic",.15f);
                glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                glassMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON"); glassMaterial.renderQueue=3000;
                glassMaterial.SetOverrideTag("RenderType","Transparent");
            }
            var levels=new LOD[3];
            var shadowArt=new VisitorCentreBuilder(2); shadowArt.Build();
            var shadowMesh=owner.Own(shadowArt.body.Apply(null,"Visitor centre shadow shell"));
            for (int lod=0;lod<3;lod++)
            {
                var art=new VisitorCentreBuilder(lod); art.Build();
                var shell=Part(root.transform,"Visitor centre LOD"+lod,owner.Own(art.body.Apply(null,"Visitor centre structure LOD"+lod)),building);
                shell.shadowCastingMode=ShadowCastingMode.Off;
                var panes=Part(root.transform,"Visitor centre glass LOD"+lod,owner.Own(art.glazing.Apply(null,"Visitor centre glazing LOD"+lod)),glassMaterial);
                panes.shadowCastingMode=ShadowCastingMode.Off;
                var shadow=Part(root.transform,"Visitor centre shadow LOD"+lod,shadowMesh,building);
                shadow.shadowCastingMode=ShadowCastingMode.ShadowsOnly; shadow.receiveShadows=false;
                levels[lod]=new LOD(lod==0?.55f:lod==1?.10f:.002f,new Renderer[]{shell,panes,shadow});
            }
            var group=root.AddComponent<LODGroup>();group.SetLODs(levels);group.RecalculateBounds();
            owner.interiorLights=new Light[2];
            owner.lightIntensities=new[]{22f,22f};
            for(int i=0;i<2;i++)
            {
                var lamp=new GameObject("Hall downlight "+(i+1));lamp.transform.SetParent(root.transform,false);
                lamp.transform.localPosition=new Vector3(i==0?-2.8f:2.8f,3.35f,1.2f);
                var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1f,.80f,.57f);
                light.range=9f;light.shadows=LightShadows.None;owner.interiorLights[i]=light;
            }
            var entryLamp=new GameObject("Entrance canopy downlight");entryLamp.transform.SetParent(root.transform,false);
            entryLamp.transform.localPosition=new Vector3(0,2.95f,8.15f);
            var entryLight=entryLamp.AddComponent<Light>();entryLight.type=LightType.Point;entryLight.color=new Color(1f,.82f,.61f);
            entryLight.range=6f;entryLight.shadows=LightShadows.None;owner.RegisterNightLight(entryLight,8f);
            owner.SetLighting(AirCity.Aircraft.AircraftLights.Glow);
            foreach(var child in root.GetComponentsInChildren<Transform>(true)) { child.gameObject.hideFlags=HideFlags.DontSave;child.gameObject.layer=parent.gameObject.layer; }
            return root;
        }

        static MeshRenderer Part(Transform parent,string name,Mesh mesh,Material material)
        {
            var go=new GameObject(name) { hideFlags=HideFlags.DontSave };go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;return renderer;
        }

        void Build()
        {
            Box(new Vector3(0,FloorHeight-.12f,0),new Vector3(22.3f,.24f,12.3f),Slab);
            // The central hall is flanked by lower accommodation wings, rather than one monolithic box.
            SideWing(-1); SideWing(1);
            Box(new Vector3(0,.3f,-5.89f),new Vector3(10f,.45f,.22f),StoneDark);
            WallWithWindows(-4.98f,-5.9f,4.98f,-5.9f,3.95f,3);
            // Low cheek walls, open glazed front, timber portal and clerestory under the gable.
            for(int side=-1;side<=1;side+=2)
            {
                Box(new Vector3(side*4.91f,2.05f,0),new Vector3(.18f,3.95f,12f),Cream);
                Box(new Vector3(side*4.88f,.36f,0),new Vector3(.26f,.57f,12f),Stone);
                Box(new Vector3(side*4.86f,2.07f,5.94f),new Vector3(.23f,4f,.25f),Timber);
            }
            FrontGlass();
            RoofPair(-5f,5f,-6.65f,6.65f,4.05f,6.20f);
            Gable(-5f,5f,6.02f,4.05f,6.08f,false);
            Gable(-5f,5f,-6.02f,4.05f,6.08f,true);
            Canopy();
            if(detail<2)
            {
                Sign(); Downpipes();
                for(int x=-1;x<=1;x+=2)
                {
                    Box(new Vector3(x*2.5f,2.75f,6.18f),new Vector3(.32f,.16f,.25f),Frame);
                    Quad(new Vector3(x*2.5f-.11f,2.67f,6.32f),new Vector3(x*2.5f+.11f,2.67f,6.32f),
                        new Vector3(x*2.5f+.11f,2.79f,6.32f),new Vector3(x*2.5f-.11f,2.79f,6.32f),Ink,.6f,1f);
                }
            }
            if(detail==0) Interior();
        }

        void SideWing(int side)
        {
            float x=side*8f;
            Box(new Vector3(x,.35f,0),new Vector3(6f,.55f,12f),StoneDark);
            // Render and vertical timber surround recessed punched windows on the public face.
            Box(new Vector3(x,2f,5.89f),new Vector3(6f,3.3f,.22f),side<0?Cream:Timber);
            Box(new Vector3(x,2f,-5.89f),new Vector3(6f,3.3f,.22f),Cream);
            Box(new Vector3(side*10.89f,2f,0),new Vector3(.22f,3.3f,12f),Cream);
            if(detail<2)
            {
                Window(new Vector3(x,2f,6.015f),4.55f,1.75f,Vector3.right,Vector3.forward,3);
                Window(new Vector3(x,2.05f,-6.015f),3.5f,1.6f,Vector3.left,Vector3.back,2);
                Window(new Vector3(side*11.015f,2f,-1f),4.15f,1.7f,Vector3.back*side,Vector3.right*side,3);
            }
            else Window(new Vector3(x,2f,6.025f),4.55f,1.75f,Vector3.right,Vector3.forward,1);
            // Wing roofs slope away from the raised central ridge, with closed soffits and thick fascia.
            float inner=side*5f,outer=side*11.65f;
            var a=new Vector3(inner,4.05f,-6.6f);var b=new Vector3(outer,3.65f,-6.6f);
            var c=new Vector3(outer,3.65f,6.6f);var d=new Vector3(inner,4.05f,6.6f);
            RoofPanel(a,b,c,d);
            // Close the shallow wedge below the mono-pitch roof on both end walls.
            float lo=Mathf.Min(inner,outer),hi=Mathf.Max(inner,outer);
            float lowTop=side<0?3.65f:4.05f,highTop=side<0?4.05f:3.65f;
            Quad(new Vector3(lo,3.55f,5.9f),new Vector3(hi,3.55f,5.9f),
                new Vector3(hi,highTop,5.9f),new Vector3(lo,lowTop,5.9f),Timber);
            Quad(new Vector3(hi,3.55f,-5.9f),new Vector3(lo,3.55f,-5.9f),
                new Vector3(lo,lowTop,-5.9f),new Vector3(hi,highTop,-5.9f),Cream);
            if(detail==0)
                for(float z=-6.25f;z<6.4f;z+=.9f) Beam(new Vector3(inner,4.09f,z),new Vector3(outer,3.69f,z),.06f,.075f,Roof);
            Beam(new Vector3(outer,3.62f,-6.4f),new Vector3(outer,3.62f,6.4f),.18f,.20f,Frame);
            // Timber slats are broad enough to remain calm before the first LOD transition.
            if(detail==0)
                for(float sx=x-2.9f;sx<x+2.95f;sx+=.31f)
                {
                    Box(new Vector3(sx,.73f,6.045f),new Vector3(.065f,.30f,.055f),WoodLight);
                    Box(new Vector3(sx,3.2f,6.045f),new Vector3(.065f,.70f,.055f),WoodLight);
                }
            if(detail<2)
            {
                // Expressed pale stone blocks at the base, with coarse joints rather than noise textures.
                for(int i=0;i<7;i++) Box(new Vector3(x-2.52f+i*.84f,.35f,6.03f),new Vector3(.81f,.49f,.07f),i%3==0?Stone:Stone*.92f);
            }
        }

        void FrontGlass()
        {
            // These panes cover a genuine opening. The entrance hall and furnishings sit behind them.
            for(int side=-1;side<=1;side+=2)
            {
                float x=side*3.03f;
                Pane(new Vector3(x,1.9f,6.015f),3.6f,3.55f,Vector3.right,Vector3.forward);
                if(detail<2)
                    for(int i=0;i<3;i++) Box(new Vector3(side*(1.30f+i*1.76f),1.9f,6.055f),new Vector3(.065f,3.65f,.11f),Frame);
            }
            Pane(new Vector3(0,1.43f,6.04f),2.4f,2.65f,Vector3.right,Vector3.forward);
            Box(new Vector3(0,3.8f,6.045f),new Vector3(9.85f,.20f,.18f),Timber);
            Box(new Vector3(0,.12f,6.05f),new Vector3(9.85f,.09f,.18f),Frame);
            foreach(float x in new[]{-1.26f,0f,1.26f}) Box(new Vector3(x,1.47f,6.10f),new Vector3(.075f,2.8f,.12f),Frame);
            Box(new Vector3(0,2.86f,6.10f),new Vector3(2.6f,.09f,.12f),Frame);
            Pane(new Vector3(0,3.32f,6.025f),2.4f,.78f,Vector3.right,Vector3.forward);
            if(detail<2)
            {
                foreach(float x in new[]{-.16f,.16f}) Box(new Vector3(x,1.35f,6.20f),new Vector3(.035f,.48f,.055f),Ink,.7f);
                Box(new Vector3(0,.085f,6.22f),new Vector3(2.5f,.035f,.48f),StoneDark);
                Box(new Vector3(-2.1f,1.58f,6.13f),new Vector3(.6f,.52f,.05f),Cream);
                Text("i",new Vector3(-2.1f,1.34f,6.17f),.38f,Frame);
            }
        }

        void WallWithWindows(float x0,float z0,float x1,float z1,float height,int panes)
        {
            Box(new Vector3((x0+x1)*.5f,height*.5f+FloorHeight,(z0+z1)*.5f),new Vector3(x1-x0,height,.18f),Cream);
            if(detail<2) Window(new Vector3(0,2.05f,z0-.12f),5.4f,1.7f,Vector3.left,Vector3.back,panes);
        }

        void Window(Vector3 centre,float width,float height,Vector3 right,Vector3 normal,int bays)
        {
            // Dark reveals in front of the solid wing walls give punched windows depth without interior overdraw.
            OrientedBox(centre-normal*.018f,right,normal,new Vector3(width+.18f,height+.18f,.075f),Frame);
            var at=centre+normal*.05f;
            // The wing windows use the shared architectural glass shading; central entry is actually transparent.
            Quad(at-right*width*.5f-Vector3.up*height*.5f,at+right*width*.5f-Vector3.up*height*.5f,
                at+right*width*.5f+Vector3.up*height*.5f,at-right*width*.5f+Vector3.up*height*.5f,Glass,.95f,1f);
            for(int i=0;i<=bays;i++)
                OrientedBox(at+right*(width*(i/(float)bays-.5f))+normal*.04f,right,normal,new Vector3(.065f,height+.10f,.11f),Frame);
            OrientedBox(at-Vector3.up*(height*.5f+.035f),right,normal,new Vector3(width+.23f,.11f,.25f),Stone);
            OrientedBox(at+Vector3.up*height*.5f,right,normal,new Vector3(width+.1f,.065f,.11f),Frame);
        }

        void Gable(float x0,float x1,float z,float low,float high,bool rear)
        {
            if(rear)
            {
                Triangle(new Vector3(x0,low,z),new Vector3(0,high,z),new Vector3(x1,low,z),Cream);
                Beam(new Vector3(x0,low,z-.035f),new Vector3(0,high,z-.035f),.18f,.16f,Timber);
                Beam(new Vector3(0,high,z-.035f),new Vector3(x1,low,z-.035f),.18f,.16f,Timber);
                return;
            }
            // Triangular clerestory lights follow the roof pitch; sign fascia leaves glazed strips either side.
            Triangle(new Vector3(x1,low,z),new Vector3(0,high,z),new Vector3(x0,low,z),Glass,glazing);
            Beam(new Vector3(x0,low,z+.08f),new Vector3(0,high,z+.08f),.19f,.20f,Timber);
            Beam(new Vector3(0,high,z+.08f),new Vector3(x1,low,z+.08f),.19f,.20f,Timber);
            // Keep the sign fascia below the sloping roof at both ends.
            Box(new Vector3(0,4.365f,z+.14f),new Vector3(6.8f,.60f,.18f),Frame);
            if(detail<2)
                foreach(float x in new[]{-2.6f,0f,2.6f})
                {
                    float top=Mathf.Lerp(high,low,Mathf.Abs(x)/5f);
                    Box(new Vector3(x,(top+low)*.5f,z+.04f),new Vector3(.075f,top-low,.10f),Frame);
                }
        }

        void RoofPair(float x0,float x1,float z0,float z1,float eave,float ridge)
        {
            RoofPanel(new Vector3(x0,eave,z0),new Vector3(0,ridge,z0),new Vector3(0,ridge,z1),new Vector3(x0,eave,z1));
            RoofPanel(new Vector3(0,ridge,z0),new Vector3(x1,eave,z0),new Vector3(x1,eave,z1),new Vector3(0,ridge,z1));
            Box(new Vector3(0,ridge+.035f,(z0+z1)*.5f),new Vector3(.23f,.10f,z1-z0+.1f),Frame);
            if(detail==0)
                for(float z=z0+.4f;z<z1-.1f;z+=.9f)
                {
                    Beam(new Vector3(x0,eave+.05f,z),new Vector3(0,ridge+.05f,z),.065f,.08f,Frame);
                    Beam(new Vector3(0,ridge+.05f,z),new Vector3(x1,eave+.05f,z),.065f,.08f,Frame);
                }
        }

        void RoofPanel(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            if(Vector3.Cross(b-a,c-a).y<0f) { var t=b;b=d;d=t; }
            Quad(a,b,c,d,Roof,.35f);
            var down=Vector3.down*.16f;
            Quad(d+down,c+down,b+down,a+down,Timber,.12f);
            var top=new[]{a,b,c,d};
            for(int i=0;i<4;i++) { int j=(i+1)%4;Quad(top[j],top[i],top[i]+down,top[j]+down,Frame,.3f); }
        }

        void Canopy()
        {
            RoofPanel(new Vector3(-4.85f,3.58f,5.8f),new Vector3(4.85f,3.58f,5.8f),new Vector3(4.85f,3.18f,9.65f),new Vector3(-4.85f,3.18f,9.65f));
            foreach(float x in new[]{-4.4f,4.4f})
            {
                Box(new Vector3(x,1.6f,9.1f),new Vector3(.22f,3.05f,.22f),Timber);
                Box(new Vector3(x,.22f,9.1f),new Vector3(.38f,.3f,.38f),Stone);
                Beam(new Vector3(x,2.35f,9.1f),new Vector3(x,3.27f,8.2f),.14f,.14f,WoodLight);
                Beam(new Vector3(x,3.28f,6),new Vector3(x,3.02f,9.55f),.17f,.23f,Timber);
            }
            Beam(new Vector3(-4.6f,3f,9.1f),new Vector3(4.6f,3f,9.1f),.20f,.26f,Timber);
            if(detail<2)
                for(float x=-3.5f;x<=3.5f;x+=1.75f) Beam(new Vector3(x,3.37f,6.2f),new Vector3(x,3.04f,9.4f),.10f,.14f,WoodLight);
        }

        void Sign()
        {
            Text("VISITOR CENTRE",new Vector3(0,4.20f,6.29f),.48f,Ink);
            // A restrained wayfinding plaque belongs to the public face, not a debugging label.
            Box(new Vector3(8.1f,2.98f,6.09f),new Vector3(2.6f,.45f,.09f),Frame);
            Text("AIRFIELD",new Vector3(8.1f,2.83f,6.16f),.27f,Ink);
        }

        void Downpipes()
        {
            foreach(float x in new[]{-11.12f,11.12f})
            {
                Box(new Vector3(x,3.52f,0),new Vector3(.14f,.16f,12.6f),Frame);
                Box(new Vector3(x,1.77f,-5.6f),new Vector3(.10f,3.37f,.10f),Frame);
                Beam(new Vector3(x,3.45f,-5.6f),new Vector3(x,3.59f,-6.35f),.10f,.10f,Frame);
            }
        }

        void Interior()
        {
            Box(new Vector3(0,FloorHeight+.003f,0),new Vector3(9.7f,.006f,11.7f),C(.72f,.69f,.58f));
            Box(new Vector3(0,.55f,-1.6f),new Vector3(4.3f,.95f,.85f),Timber);
            Box(new Vector3(0,1.07f,-1.6f),new Vector3(4.5f,.12f,1f),StoneDark);
            Box(new Vector3(.9f,1.43f,-1.6f),new Vector3(.55f,.42f,.06f),Frame);
            Box(new Vector3(.9f,1.18f,-1.6f),new Vector3(.14f,.15f,.20f),Frame);
            foreach(float z in new[]{1.3f,3.1f}) foreach(float x in new[]{-3.7f,3.7f})
            {
                Box(new Vector3(x,.54f,z),new Vector3(1.1f,.20f,.66f),C(.35f,.43f,.40f));
                Box(new Vector3(x,.92f,z-.28f),new Vector3(1.1f,.64f,.16f),C(.35f,.43f,.40f));
                foreach(float dx in new[]{-.39f,.39f}) Box(new Vector3(x+dx,.29f,z),new Vector3(.055f,.40f,.47f),Frame);
            }
            Box(new Vector3(-3.8f,1.6f,-3.8f),new Vector3(.25f,2.9f,1.7f),Cream);
            Box(new Vector3(-3.65f,1.6f,-3.8f),new Vector3(.04f,1.7f,1.1f),C(.37f,.50f,.48f));
            // Exposed interior tie beams remain readable through the glazed gable.
            foreach(float z in new[]{-4f,0f,4f}) Beam(new Vector3(-4.8f,3.9f,z),new Vector3(4.8f,3.9f,z),.18f,.22f,Timber);
        }

        void Pane(Vector3 centre,float width,float height,Vector3 right,Vector3 normal)
        {
            Quad(centre-right*width*.5f-Vector3.up*height*.5f,centre+right*width*.5f-Vector3.up*height*.5f,
                centre+right*width*.5f+Vector3.up*height*.5f,centre-right*width*.5f+Vector3.up*height*.5f,Glass,.95f,0f,glazing);
        }

        // Compact stroke lettering is actual mesh geometry, so it remains independent of editor fonts.
        static readonly Dictionary<char,string> Glyphs=new Dictionary<char,string>
        {
            ['A']="0,0,0,1;0,1,1,1;1,1,1,0;0,.5,1,.5",['C']="1,1,0,1;0,1,0,0;0,0,1,0",
            ['D']="0,0,0,1;0,1,.7,1;.7,1,1,.8;1,.8,1,.2;1,.2,.7,0;.7,0,0,0",
            ['E']="0,0,0,1;0,1,1,1;0,.5,.8,.5;0,0,1,0",['F']="0,0,0,1;0,1,1,1;0,.5,.8,.5",
            ['I']=".5,0,.5,1;0,1,1,1;0,0,1,0",['L']="0,1,0,0;0,0,1,0",
            ['N']="0,0,0,1;0,1,1,0;1,0,1,1",['O']=".2,0,.8,0;.8,0,1,.2;1,.2,1,.8;1,.8,.8,1;.8,1,.2,1;.2,1,0,.8;0,.8,0,.2;0,.2,.2,0",
            ['R']="0,0,0,1;0,1,.8,1;.8,1,1,.8;1,.8,1,.6;1,.6,.8,.5;.8,.5,0,.5;.5,.5,1,0",
            ['S']="1,1,0,1;0,1,0,.5;0,.5,1,.5;1,.5,1,0;1,0,0,0",
            ['T']="0,1,1,1;.5,1,.5,0",['V']="0,1,.5,0;.5,0,1,1",['i']=".5,0,.5,.65;.5,.9,.5,1"
        };
        void Text(string text,Vector3 at,float height,Color color)
        {
            float width=height*.62f,gap=height*.24f,total=text.Length*(width+gap)-gap;
            float x=at.x+total*.5f;
            foreach(char c in text)
            {
                if(Glyphs.TryGetValue(c,out var glyph)) foreach(var stroke in glyph.Split(';'))
                {
                    var values=stroke.Split(',');float F(int i)=>float.Parse(values[i],System.Globalization.CultureInfo.InvariantCulture);
                    // The facade is viewed towards -Z: its readable left is local +X.
                    var a=new Vector3(x-width*F(0),at.y+height*F(1),at.z);
                    var b=new Vector3(x-width*F(2),at.y+height*F(3),at.z);
                    Beam(a,b,height*.07f,.035f,color);
                }
                x-=width+gap;
            }
        }

        void Box(Vector3 centre,Vector3 size,Color colour,float smooth=.2f)=>OrientedBox(centre,Vector3.right,Vector3.forward,size,colour,smooth);
        void OrientedBox(Vector3 centre,Vector3 right,Vector3 forward,Vector3 size,Color colour,float smooth=.2f)
        {
            var r=right*(size.x*.5f);var u=Vector3.up*(size.y*.5f);var f=forward*(size.z*.5f);
            Vector3 P(int x,int y,int z)=>centre+r*x+u*y+f*z;
            Quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1),colour,smooth);
            Quad(P(1,-1,-1),P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),colour,smooth);
            Quad(P(1,-1,1),P(1,-1,-1),P(1,1,-1),P(1,1,1),colour,smooth);
            Quad(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1),colour,smooth);
            Quad(P(-1,1,1),P(1,1,1),P(1,1,-1),P(-1,1,-1),colour,smooth);
            Quad(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1),colour,smooth);
        }
        void Beam(Vector3 from,Vector3 to,float width,float depth,Color colour)
        {
            var axis=(to-from).normalized;var right=Vector3.Cross(axis,Vector3.forward);
            if(right.sqrMagnitude<.01f)right=Vector3.right;else right.Normalize();
            var up=Vector3.Cross(axis,right).normalized;var centre=(from+to)*.5f;
            var r=right*width*.5f;var u=up*depth*.5f;var f=(to-from)*.5f;
            Vector3 P(int x,int y,int z)=>centre+r*x+u*y+f*z;
            Quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1),colour);
            Quad(P(1,-1,-1),P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),colour);
            Quad(P(1,-1,1),P(1,-1,-1),P(1,1,-1),P(1,1,1),colour);
            Quad(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1),colour);
            Quad(P(-1,1,1),P(1,1,1),P(1,1,-1),P(-1,1,-1),colour);
            Quad(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1),colour);
        }
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color colour,float smooth=.2f,float glass=0f,MeshData target=null)
        {
            var mesh=target??body;var normal=Vector3.Cross(b-a,c-a).normalized;int first=mesh.Count;
            foreach(var p in new[]{a,b,c,d})mesh.Vertex(p,normal,colour,new Vector4(glass,smooth,p.x+p.z,p.y),new Vector4(0,2.5f,0,0));
            mesh.Quad(0,first,first+1,first+2,first+3);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Color colour,MeshData target=null)
        {
            var mesh=target??body;var normal=Vector3.Cross(b-a,c-a).normalized;int first=mesh.Count;
            foreach(var p in new[]{a,b,c})mesh.Vertex(p,normal,colour,new Vector4(0,.3f,p.x,p.y));
            mesh.Triangle(0,first,first+1,first+2);
        }
    }
}
