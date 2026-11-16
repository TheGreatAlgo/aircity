using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Shared bridge deck/supports and tunnel lining/portals. Structure surfaces never own the ground
    /// below a bridge or above a covered tunnel; only an open approach contributes terrain clipping.</summary>
    internal static class RoadStructureBuilder
    {
        public const float DeckDepth=.8f,TunnelClearance=5.5f,TunnelCover=1.5f;
        public static Material Material(Material source)
        {
            var result=new Material(Resources.Load<Shader>("RoadStructure")) {name="Road concrete",hideFlags=HideFlags.DontSave};
            if(source!=null && source.HasProperty("_BaseColor"))result.SetColor("_BaseColor",source.GetColor("_BaseColor"));return result;
        }
        public static void Build(Alignment3D path,RoadStructure structure,float half,Func<Vector2,float> ground,
            LandscapeMesh mesh,List<BuiltSurfaceMap.Triangle> openings,Func<Vector3,float,bool> support=null,float from=0,float to=-1,Func<Vector2,float> deckHeight=null)
        {
            if(structure==RoadStructure.Ground)return;
            Vector3 At(float s,float x)
            {var p=path.Offset(s,x,from,path.Length-(to<0?path.Length:to));if(deckHeight!=null)p.y=deckHeight(Alignment3D.XZ(p));return p;}
            to=to<0?path.Length:to;int count=Mathf.Max(1,Mathf.CeilToInt((to-from)/4f));
            for(int i=0;i<count;i++)
            {
                float s0=Mathf.Lerp(from,to,i/(float)count),s1=Mathf.Lerp(from,to,(i+1f)/count);
                var l0=At(s0,-half);var r0=At(s0,half);var l1=At(s1,-half);var r1=At(s1,half);
                var c0=At(s0,0);var c1=At(s1,0);float g0=ground(Alignment3D.XZ(c0)),g1=ground(Alignment3D.XZ(c1));
                if(structure==RoadStructure.Elevated)
                {
                    if(Mathf.Max(g0-c0.y,g1-c1.y)>-.5f)
                    {openings.Add(new BuiltSurfaceMap.Triangle(l0,l1,r1));openings.Add(new BuiltSurfaceMap.Triangle(l0,r1,r0));}
                    var down=Vector3.down*(DeckDepth-.18f);var top=Vector3.up*.16f;
                    mesh.Quad(l0+down,l1+down,r1+down,r0+down);
                    mesh.TwoSided(l0+down,l1+down,l1+top,l0+top);mesh.TwoSided(r1+down,r0+down,r0+top,r1+top);
                    Rail(mesh,l0,l1,-1,path,s0,s1);Rail(mesh,r0,r1,1,path,s0,s1);
                    if(i==0)mesh.Quad(r0+down,l0+down,l0+top,r0+top);
                    if(i==count-1)mesh.Quad(l1+down,r1+down,r1+top,l1+top);
                }
                else
                {
                    // Open retaining walls continue down to the carriageway, rather than leaving grass floating
                    // above its ramp. The covered bore has an opaque lining; road-facing sides are two-sided.
                    float roof0=c0.y+TunnelClearance,roof1=c1.y+TunnelClearance;
                    bool covered0=roof0+TunnelCover<g0,covered1=roof1+TunnelCover<g1;
                    var u0=Vector3.up*(covered0?TunnelClearance:Mathf.Max(TunnelClearance,g0-c0.y+.5f));
                    var u1=Vector3.up*(covered1?TunnelClearance:Mathf.Max(TunnelClearance,g1-c1.y+.5f));
                    mesh.TwoSided(l0,l1,l1+u1,l0+u0);mesh.TwoSided(r1,r0,r0+u0,r1+u1);
                    if(covered0 && covered1)mesh.TwoSided(l0+u0,l1+u1,r1+u1,r0+u0);
                    else
                    {
                        openings.Add(new BuiltSurfaceMap.Triangle(l0,l1,r1));openings.Add(new BuiltSurfaceMap.Triangle(l0,r1,r0));
                        if(covered0!=covered1)
                        {
                            float s=covered0?s0:s1;var c=At(s,0);var direction=path.Tangent(path.ParameterAtDistance(s));
                            var rotation=Quaternion.LookRotation(new Vector3(direction.x,0,direction.z));
                            mesh.OrientedBox(c+Vector3.up*(TunnelClearance+.3f),new Vector3(half*2+1.2f,.6f,1.4f),rotation);
                            for(int side=-1;side<=1;side+=2)
                                mesh.OrientedBox(At(s,(half+.3f)*side)+Vector3.up*(TunnelClearance*.5f),new Vector3(.6f,TunnelClearance,1.4f),rotation);
                        }
                    }
                }
            }
            if(structure!=RoadStructure.Elevated)return;
            for(float s=from+14f;s<to-8f;s+=28f)
            {
                var p=At(s,0);float floor=ground(Alignment3D.XZ(p)),height=p.y-floor-DeckDepth;
                if(height<=1f || support!=null && !support(p,2.5f))continue;
                float column=Mathf.Clamp(half*.14f,.9f,2.5f);
                mesh.Box(new Vector3(p.x,floor+height*.5f,p.z),new Vector3(column,height,column*1.3f));
                var direction=path.Tangent(path.ParameterAtDistance(s));var rotation=Quaternion.LookRotation(new Vector3(direction.x,0,direction.z));
                mesh.OrientedBox(p+Vector3.down*(DeckDepth+.45f),new Vector3(half*1.5f,.9f,column*1.7f),rotation);
                mesh.Box(new Vector3(p.x,floor+.15f,p.z),new Vector3(column*2.2f,.6f,column*2.2f));
                float radius=column*1.1f;var a=new Vector3(p.x-radius,floor,p.z-radius);var b=new Vector3(p.x-radius,floor,p.z+radius);var c=new Vector3(p.x+radius,floor,p.z+radius);var d=new Vector3(p.x+radius,floor,p.z-radius);
                openings.Add(new BuiltSurfaceMap.Triangle(a,b,c));openings.Add(new BuiltSurfaceMap.Triangle(a,c,d));
            }
        }
        static void Rail(LandscapeMesh mesh,Vector3 a,Vector3 b,int side,Alignment3D path,float s0,float s1)
        {
            var outward0=(path.Offset(s0,side)-path.AtDistance(s0))*.22f;
            var outward1=(path.Offset(s1,side)-path.AtDistance(s1))*.22f;
            var low=Vector3.up*.3f;var top=Vector3.up*1.1f;
            mesh.TwoSided(a+low,b+low,b+top,a+top);
            mesh.TwoSided(a+top,b+top,b+top+outward1,a+top+outward0);
        }
    }
}
