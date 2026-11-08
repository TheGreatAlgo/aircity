using System.Linq;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Shared industrial kit: framed shells, removable roofs, handling machinery and equipment-yard paint.</summary>
    sealed class BaggageDepotArt
    {
        public readonly MeshData shell=new MeshData(),roof=new MeshData(),equipment=new MeshData();
        public readonly PaintBuilder paint=new PaintBuilder();
        readonly BaggageDepot depot;
        readonly BaggageDepotLayout layout;
        readonly Color Cladding,Roof;
        static readonly Color Steel=new Color(.22f,.28f,.31f),
            Blue=new Color(.12f,.33f,.49f),Safety=new Color(.95f,.67f,.12f),Concrete=new Color(.48f,.49f,.47f),
            Belt=new Color(.13f,.15f,.16f),Glass=new Color(.18f,.31f,.36f);
        public BaggageDepotArt(BaggageDepot depot)
        {
            this.depot=depot; layout=depot.Layout; var terminal=depot.ConnectedTerminal;
            Cladding=terminal?.exterior.facade.cladding ?? new Color(.71f,.75f,.76f);
            Roof=terminal?.exterior.roof ?? new Color(.27f,.33f,.36f);
        }
        Vector3 At(float x,float y,float z) { var p=depot.Point(x,z); return new Vector3(p.x,y,p.y); }
        void Quad(MeshData mesh,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color,float glazing=0)
        {
            var normal=Vector3.Cross(b-a,c-a).normalized; int v=mesh.Count;
            foreach (var p in new[] { a,b,c,d })
                mesh.Vertex(p,normal,color.linear,new Vector4(glazing,.35f,Vector3.Dot(p,new Vector3(depot.Right.x,0,depot.Right.y)),p.y));
            mesh.Quad(0,v,v+1,v+2,v+3);
        }
        void Panel(MeshData mesh,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        { Quad(mesh,a,b,c,d,color); Quad(mesh,d,c,b,a,color); }
        void Box(MeshData mesh,float x,float y,float z,float w,float h,float length,Color color,float glazing=0)
        {
            float x0=x-w*.5f,x1=x+w*.5f,z0=z-length*.5f,z1=z+length*.5f,y0=y-h*.5f,y1=y+h*.5f;
            Quad(mesh,At(x0,y0,z0),At(x0,y0,z1),At(x0,y1,z1),At(x0,y1,z0),color,glazing);
            Quad(mesh,At(x1,y0,z1),At(x1,y0,z0),At(x1,y1,z0),At(x1,y1,z1),color,glazing);
            Quad(mesh,At(x0,y0,z1),At(x1,y0,z1),At(x1,y1,z1),At(x0,y1,z1),color,glazing);
            Quad(mesh,At(x1,y0,z0),At(x0,y0,z0),At(x0,y1,z0),At(x1,y1,z0),color,glazing);
            Quad(mesh,At(x0,y1,z1),At(x1,y1,z1),At(x1,y1,z0),At(x0,y1,z0),color,glazing);
        }
        void Beam(MeshData mesh,Vector3 a,Vector3 b,float width,Color color)
        {
            var delta=b-a; var side=Vector3.Cross(delta.normalized,Vector3.forward).normalized;
            if (side.sqrMagnitude<.1f) side=Vector3.right;
            var up=Vector3.Cross(delta.normalized,side).normalized; side*=width*.5f; up*=width*.5f;
            var corners=new[] { side+up,-side+up,-side-up,side-up };
            for (int i=0;i<4;i++) { int j=(i+1)%4; Quad(mesh,a+corners[i],b+corners[i],b+corners[j],a+corners[j],color); }
        }
        void Line(float x0,float z0,float x1,float z1,Color color,float width=.075f)
            => paint.Line(depot.Point(x0,z0),depot.Point(x1,z1),width,color);
        void Label(float x,float z,string text,float height=.45f)
            => paint.Text(depot.Point(x,z),depot.Facing,text,height,PaintBuilder.White,.8f);
        void Bollard(float x,float z)
        {
            Box(equipment,x,.48f,z,.18f,.96f,.18f,Safety);
            Box(equipment,x,.62f,z,.185f,.12f,.185f,Steel);
        }
        void Conveyor(float x0,float x1,float z,bool powered)
        {
            float length=x1-x0;
            Box(equipment,(x0+x1)*.5f,.65f,z,length,.22f,.75f,Blue);
            Box(equipment,(x0+x1)*.5f,.80f,z,length-.1f,.06f,.57f,Belt);
            foreach (float x in new[] { x0+.35f,x1-.35f })
                foreach (float side in new[] { -.28f,.28f }) Box(equipment,x,.32f,z+side,.08f,.64f,.08f,Steel);
            for (float x=x0+.1f;x<x1;x+=.32f) Box(equipment,x,.84f,z,.035f,.025f,.58f,new Color(.43f,.46f,.45f));
            if (powered) { Box(equipment,x1-.4f,.56f,z+.48f,.55f,.35f,.35f,Steel); Box(equipment,x1-.4f,.56f,z+.7f,.2f,.28f,.12f,Blue); }
        }
        void ConveyorBranch(float x,float z0,float z1)
        {
            float length=z1-z0;
            Box(equipment,x,.65f,(z0+z1)*.5f,.75f,.22f,length,Blue);
            Box(equipment,x,.8f,(z0+z1)*.5f,.57f,.06f,length,Belt);
            for (float z=z0;z<z1;z+=.32f) Box(equipment,x,.84f,z,.58f,.025f,.035f,new Color(.43f,.46f,.45f));
            foreach (float z in new[] { z0+.2f,z1-.2f }) Box(equipment,x,.32f,z,.6f,.64f,.08f,Steel);
        }
        void Cage(float x,float z,bool doubleHeight)
        {
            float h=doubleHeight ? 2.6f : 1.4f;
            Box(equipment,x,.10f,z,2.8f,.16f,2.6f,Steel);
            foreach (float dx in new[] { -1.35f,1.35f }) foreach (float dz in new[] { -1.25f,1.25f })
                Box(equipment,x+dx,h*.5f,z+dz,.07f,h,.07f,Blue);
            for (float y=.4f;y<h;y+=.35f)
            {
                foreach (float dx in new[] { -1.35f,1.35f }) Box(equipment,x+dx,y,z,.035f,.025f,2.5f,Steel);
                Box(equipment,x,y,z-1.25f,2.7f,.025f,.035f,Steel);
            }
            if (doubleHeight) Box(equipment,x,1.35f,z,2.7f,.08f,2.5f,Steel);
        }
        void Office()
        {
            float x=layout.Right-3.5f,z=layout.WallTop-3.7f;
            if (layout.Enclosed)
            {
                Box(shell,x,1.5f,z,6,3,7,Cladding);
                Box(roof,x,3.08f,z,6.3f,.18f,7.3f,Roof);
            }
            float face=layout.Enclosed ? x-3.03f : layout.Left-.04f;
            float windowZ=layout.Enclosed ? z : 3.5f;
            Box(shell,face,2,windowZ,.09f,1.1f,3.2f,Steel);
            Box(shell,face-.055f,2,windowZ,.025f,.92f,2.95f,Glass,1);
            Box(shell,face-.08f,2,windowZ,.05f,1.1f,.055f,Steel);
            Box(shell,face,1.05f,windowZ-3,.10f,2.1f,1.05f,Blue);
            Box(shell,face-.08f,1.05f,windowZ-2.63f,.03f,.06f,.18f,Safety);
            Box(roof,face-.6f,2.4f,windowZ-3,1.6f,.12f,1.8f,Steel);
        }
        public void Build()
        {
            float lo=layout.WallBottom,hi=layout.WallTop,h=layout.Eaves;
            // Low concrete upstand, ribbed metal walls and portal frames share the same dimensions as clearance.
            foreach (var r in layout.Solids())
            {
                if (r.width>.3f && r.height>.3f) continue;
                Box(shell,r.center.x,h*.5f,r.center.y,r.width,h,r.height,Cladding);
                Box(shell,r.center.x,.22f,r.center.y,r.width+.06f,.44f,r.height+.06f,Concrete);
            }
            Box(shell,(layout.Left+layout.Right)*.5f,.015f,(lo+hi)*.5f,layout.Right-layout.Left,.03f,hi-lo,Concrete);
            foreach (float x in new[] { layout.Left-.04f,layout.Right+.04f })
                for (float z=lo+.5f;z<hi;z+=.8f) Box(shell,x,h*.5f,z,.07f,h,.045f,new Color(.61f,.66f,.68f));
            foreach (float z in new[] { lo-.04f,hi+.04f })
                for (float x=layout.Left+.5f;x<layout.Right;x+=.8f)
                {
                    if (layout.Enclosed && Enumerable.Range(0,layout.Lanes).Any(i=>Mathf.Abs(x-layout.LaneX(i))<2.55f)) continue;
                    Box(shell,x,h*.5f,z,.045f,h,.07f,new Color(.61f,.66f,.68f));
                }
            for (float z=lo;z<=hi+.1f;z+=8)
                foreach (float x in new[] { layout.Left+.35f,layout.Right-.35f }) Box(shell,x,h*.5f,z,.22f,h,.26f,Steel);
            int spans=layout.Enclosed ? layout.Lanes : 1;
            float span=(layout.Right-layout.Left)/spans;
            for (int i=0;i<spans;i++)
            {
                float left=layout.Left+i*span,right=left+span,mid=(left+right)*.5f;
                Panel(roof,At(left-.35f,h,lo-.5f),At(mid,layout.Ridge,lo-.5f),At(mid,layout.Ridge,hi+.5f),At(left-.35f,h,hi+.5f),Roof);
                Panel(roof,At(mid,layout.Ridge,lo-.5f),At(right+.35f,h,lo-.5f),At(right+.35f,h,hi+.5f),At(mid,layout.Ridge,hi+.5f),Roof);
                foreach (float z in new[] { lo,hi })
                    Panel(roof,At(left,h,z),At(mid,layout.Ridge,z),At(right,h,z),At(left,h,z),Cladding);
                for (float z=lo;z<=hi;z+=8)
                {
                    Beam(roof,At(left,h-.15f,z),At(mid,layout.Ridge-.15f,z),.16f,Steel);
                    Beam(roof,At(mid,layout.Ridge-.15f,z),At(right,h-.15f,z),.16f,Steel);
                    Beam(roof,At(left,h-.3f,z),At(right,h-.3f,z),.12f,Steel);
                }
                for (float z=lo;z<hi;z+=1.2f)
                {
                    var seam=new Color(.37f,.43f,.45f);
                    Beam(roof,At(left,h+.025f,z),At(mid,layout.Ridge+.025f,z),.025f,seam);
                    Beam(roof,At(mid,layout.Ridge+.025f,z),At(right,h+.025f,z),.025f,seam);
                }
                if (depot.size==BaggageDepotSize.Large)
                {
                    Box(roof,mid,layout.Ridge+.45f,(lo+hi)*.5f,2.2f,.75f,12,Steel);
                    Box(roof,mid,layout.Ridge+.45f,(lo+hi)*.5f,2.25f,.45f,11.5f,Glass,1);
                    Box(roof,mid,layout.Ridge+.9f,(lo+hi)*.5f,2.7f,.15f,12.5f,Roof);
                }
            }
            Office();
            foreach (float x in new[] { layout.Left-.15f,layout.Right+.15f })
            {
                Box(roof,x,h-.05f,(lo+hi)*.5f,.18f,.16f,hi-lo+.7f,Steel);
                foreach (float z in new[] { lo+.3f,hi-.3f }) Box(shell,x,h*.5f,z,.10f,h,.10f,Steel);
            }
            if (!layout.Enclosed)
            {
                Cage(15,-11,false); Cage(20,-11,depot.Has(BaggageDepotUpgrade.BufferStorage));
                Cage(20,4,false);
                Box(equipment,15,.76f,-3,2.4f,.14f,1.1f,Steel);
                foreach (float dx in new[] { -1f,1f }) Box(equipment,15+dx,.38f,-3,.09f,.76f,.8f,Blue);
                Conveyor(11.3f,16,-6,true);
                Box(equipment,20,1,-2,2,.12f,.8f,Steel);
                foreach (float dx in new[] { -.8f,.8f }) Box(equipment,20+dx,.5f,-2,.08f,1,.65f,Blue);
                Box(equipment,20,1.35f,-2.2f,.6f,.4f,.08f,Glass);
            }
            for (int i=0;i<layout.Lanes;i++) WorkingLane(i);
            for (int i=0;i<depot.Maximum.teams;i++) Parking(i);
            foreach (float z in new[] { layout.EntryZ,layout.ExitZ })
            {
                Line(1,z-4,layout.YardRight-2,z-4,PaintBuilder.White);
                Label(layout.YardRight-8,z,z==layout.EntryZ ? "IN" : "OUT",.7f);
            }
        }
        void WorkingLane(int lane)
        {
            float x=layout.LaneX(lane),z=layout.WorkZ;
            if (layout.Enclosed)
            {
                foreach (float end in new[] { layout.WallBottom,layout.WallTop })
                {
                    Box(shell,x,(layout.Eaves+4.2f)*.5f,end,5,layout.Eaves-4.2f,.24f,Cladding);
                    Box(shell,x,4.25f,end,5.4f,.2f,.4f,Blue);
                    for (float y=4.45f;y<4.95f;y+=.12f) Box(shell,x,y,end,4.9f,.09f,.3f,Steel);
                    foreach (float dx in new[] { -2.65f,2.65f })
                    { Box(shell,x+dx,2.15f,end,.13f,4.3f,.35f,Blue); Bollard(x+dx,end+Mathf.Sign(end)*.6f); }
                    Box(shell,x,5.15f,end-.18f,1.3f,.5f,.08f,Blue);
                    int pattern=new[] { 6,91,79,102 }[lane];
                    for (int segment=0;segment<7;segment++)
                    {
                        if ((pattern&(1<<segment))==0) continue;
                        bool horizontal=segment==0 || segment==3 || segment==6;
                        float dx=segment==1 || segment==2 ? .16f : segment==4 || segment==5 ? -.16f : 0;
                        float dy=segment==0 ? .17f : segment==3 ? -.17f : segment==6 ? 0 : segment==1 || segment==5 ? .085f : -.085f;
                        Box(shell,x+dx,5.15f+dy,end+(end==layout.WallBottom ? -.24f : .24f),horizontal ? .3f : .055f,horizontal ? .045f : .16f,.025f,Cladding);
                    }
                    Box(shell,x,4.05f,end-.35f,.8f,.08f,.3f,new Color(.95f,.87f,.65f));
                }
            }
            else
            {
                Box(roof,7.1f,3.65f,-5,8.8f,.18f,19,Roof);
                foreach (float at in new[] { -14f,4f })
                { Box(shell,2.85f,1.8f,at,.14f,3.6f,.14f,Steel); Bollard(2.85f,at); }
                Box(shell,10.86f,1.6f,-6,.12f,3.2f,5.5f,Steel);
                for (float y=2.4f;y<3.2f;y+=.15f) Box(shell,10.75f,y,-6,.12f,.09f,5.3f,Blue);
            }
            for (int side=-1;side<=1;side+=2) Line(x+side*2.4f,layout.WorkEntryZ+3,x+side*2.4f,layout.ExitZ-3,PaintBuilder.White);
            Line(x-2.4f,z+3,x+2.4f,z+3,Safety,.12f); Label(x,z+1.7f,"BAY "+(lane+1));
            Label(x,layout.WorkEntryZ+5,"IN"); Label(x,layout.ExitZ-5,"OUT");
            bool powered=depot.Has(BaggageDepotUpgrade.PoweredBelts);
            foreach (float dz in new[] { -.9f,.9f }) Conveyor(x+2.5f,x+7,z-6+dz,powered);
            Cage(x+7,z-12,depot.Has(BaggageDepotUpgrade.BufferStorage));
            Cage(x+7,z-1,depot.Has(BaggageDepotUpgrade.BufferStorage));
            Label(x+7,z-14,"ARRIVALS",.3f); Label(x+7,z+1,"DEPARTURES",.3f);
            if (depot.Has(BaggageDepotUpgrade.HandlingAssist))
            {
                Box(equipment,x+3.4f,1.5f,z-8,.12f,3,.12f,Blue);
                Box(equipment,x+2.65f,2.95f,z-8,1.6f,.12f,.12f,Blue);
                Box(equipment,x+1.9f,2.3f,z-8,.035f,1.2f,.035f,Steel);
                Box(equipment,x+1.9f,1.65f,z-8,.25f,.12f,.25f,Safety);
            }
            if (depot.Has(BaggageDepotUpgrade.AutomaticSorting))
            {
                float end=x+9;
                Conveyor(x+7.4f,end,z-6,true);
                ConveyorBranch(x+8.6f,z-8.5f,z-3.5f);
                foreach (float dz in new[] { -2.5f,2.5f }) Conveyor(x+6.5f,end,z-6+dz,true);
                foreach (float dx in new[] { 7.3f,8.8f }) Box(equipment,x+dx,1.35f,z-6,.12f,1.3f,1.3f,Blue);
                Box(equipment,x+8.05f,2,z-6,1.7f,.15f,1.3f,Blue);
                Box(equipment,x+8.85f,1.4f,z-6.8f,.12f,.35f,.1f,Safety);
            }
            if (depot.size==BaggageDepotSize.Large)
            {
                // Container preparation positions are part of the asset; the existing container fleet remains independent.
                float cx=x+7;
                Box(equipment,cx,.22f,z+3,3.7f,.3f,3.5f,Blue);
                for (float dz=-1.4f;dz<=1.4f;dz+=.3f) Box(equipment,cx,.4f,z+3+dz,3.5f,.1f,.12f,Steel);
                Label(cx,z+6,"ULD",.4f);
            }
            // Guarded pedestrian edge, outside the cart's drawbar sweep and the handover reach.
            if (layout.Enclosed)
                for (float at=layout.WallBottom+2;at<layout.WallTop-2;at+=3)
                    if (Mathf.Abs(at-(z-6))>3) Bollard(x+3.5f,at);
        }
        void Parking(int team)
        {
            var p=layout.Home(team); float x=p.x,z=p.y;
            foreach (float dx in new[] { -1.8f,1.8f }) Line(x+dx,z-15,x+dx,z+3,PaintBuilder.White);
            Line(x-1.8f,z+3,x+1.8f,z+3,PaintBuilder.White); Label(x,z+1.5f,"TEAM "+(team+1),.32f);
            var loader=layout.Loader(team);
            foreach (float dx in new[] { -1.8f,1.8f }) Line(loader.x+dx,loader.y-3,loader.x+dx,loader.y+8,PaintBuilder.White);
            Label(loader.x,loader.y+7,"BELT",.3f);
            Box(equipment,loader.x+2.1f,.6f,loader.y+6,.5f,1.2f,.5f,Blue);
            Box(equipment,loader.x+2.1f,.9f,loader.y+5.72f,.3f,.3f,.08f,Glass);
        }
    }
}
