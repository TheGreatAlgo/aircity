using System;
using System.Collections.Generic;
using UnityEngine;
using static AirCity.World.RoadSuspensionSpan;

namespace AirCity.World
{
    /// <summary>The visible support design of one complete elevated run. An unsuitable override uses regular piers.</summary>
    internal readonly struct RoadBridgeSpan
    {
        internal readonly RoadSuspensionSpan suspension;
        internal readonly RoadArchSpan arch;
        internal RoadBridgeDesign Design => arch!=null?RoadBridgeDesign.Arch:
            suspension!=null?RoadBridgeDesign.Suspension:RoadBridgeDesign.Piers;
        internal int Signature => (Design,suspension?.Signature??0,arch?.Signature??0).GetHashCode();

        RoadBridgeSpan(RoadSuspensionSpan suspension,RoadArchSpan arch)
        {this.suspension=suspension;this.arch=arch;}

        internal static RoadBridgeSpan Plan(RunSpan run,List<Section> sections,RoadBridgeDesign design,
            Func<Vector2,float> ground,Func<Vector2,float?> water,Func<Vector3,float,bool> canSupport)
        {
            if(design==RoadBridgeDesign.Piers)return default;
            if(design==RoadBridgeDesign.Arch)
                return new RoadBridgeSpan(null,RoadArchSpan.Plan(run,sections,ground,water,canSupport));
            return new RoadBridgeSpan(RoadSuspensionSpan.Plan(run,sections,ground,water,canSupport,
                design==RoadBridgeDesign.Suspension),null);
        }
    }

    /// <summary>Twin steel through arches with vertical hangers, deck-level ties and bearings at the banks.</summary>
    internal sealed class RoadArchSpan
    {
        internal const float MinimumSpan = 30f;
        internal const float MaximumSpan = 350f;
        static readonly Color32 Rib = new Color32(70,102,112,0);
        static readonly Color32 Hanger = new Color32(100,119,122,0);
        static readonly Color32 Concrete = new Color32(255,255,255,255);
        readonly float length,rise,start,end;
        internal int Signature => (length,rise,start,end).GetHashCode();

        RoadArchSpan(float length,float start,float end)
        {
            this.length=length;this.start=start;this.end=end;
            rise=Mathf.Clamp(length*.20f,7f,56f);
        }

        internal static RoadArchSpan Plan(RunSpan run,List<Section> sections,Func<Vector2,float> ground,
            Func<Vector2,float?> water,Func<Vector3,float,bool> canSupport)
        {
            if(run.total<MinimumSpan||run.total>MaximumSpan||!FitsAlignment(run,sections,ground,water))return null;
            var a=sections[0];var b=sections[sections.Count-1];
            if(canSupport!=null&&(!AnchorFits(a)||!AnchorFits(b)))return null;
            return new RoadArchSpan(run.total,a.centre.y,b.centre.y);

            bool AnchorFits(Section section) => canSupport(section.centre,section.halfWidth+1f);
        }

        internal void Build(StructurePiece piece,LandscapeMesh mesh,Vector3[] centre,Vector3[] across,float[] along,float[] half,
            Func<Vector2,float> ground,Func<Vector2,float?> water)
        {
            var sections=DeckSections(piece,centre,across,along,half);
            float from=sections[0].station,to=sections[sections.Count-1].station;
            if(to-from<.001f)return;
            float radius=Mathf.Clamp(length*.0085f,.44f,2.1f);
            mesh.colour=Rib;
            for(int side=-1;side<=1;side+=2)
            {
                var section=sections[0];
                var previous=Top(section,side);var tie=Tie(section,side);
                for(float station=Mathf.Floor(from/3f)*3f+3f;;station+=3f)
                {
                    section=Sample(sections,Mathf.Min(station,to));
                    var top=Top(section,side);var nextTie=Tie(section,side);
                    Cylinder(mesh,previous,top,radius);Cylinder(mesh,tie,nextTie,radius*.7f);
                    if(station>=to)break;
                    previous=top;tie=nextTie;
                }
            }

            float hangerSpacing=Mathf.Clamp(length/22f,4f,12f);
            for(float station=Mathf.Ceil(from/hangerSpacing)*hangerSpacing;station<to;station+=hangerSpacing)
            {
                var section=Sample(sections,station);
                mesh.colour=Hanger;
                for(int side=-1;side<=1;side+=2)
                    Cylinder(mesh,Tie(section,side),Top(section,side),radius*.24f);
                var left=Top(section,-1);var right=Top(section,1);
                if(left.y-section.centre.y>6.5f+radius)
                {
                    mesh.colour=Rib;
                    Cylinder(mesh,left,right,radius*.54f);
                }
            }
            if(from==0f)Bearing(sections[0]);
            if(to==length)Bearing(sections[sections.Count-1]);

            Vector3 Tie(Section section,int side)=>
                section.centre+section.across*((section.halfWidth+.45f)*side)-Vector3.up*.28f;

            Vector3 Top(Section section,int side)
            {
                var point=Tie(section,side);
                float t=Mathf.Clamp01(section.station/length);
                point.y=Mathf.Max(point.y,Mathf.Lerp(start,end,t)-.28f+4f*rise*t*(1f-t));
                return point;
            }

            void Bearing(Section section)
            {
                mesh.colour=Concrete;
                for(int side=-1;side<=1;side+=2)
                {
                    var p=Tie(section,side);var at=Alignment3D.XZ(p);
                    float foot=RoadStructureBuilder.SupportFoot(at,ground(at),water);
                    float top=Mathf.Max(p.y+.65f,foot+.75f);
                    p.y=(top+foot)*.5f;
                    mesh.OrientedBox(p,new Vector3(radius*3f+.7f,top-foot,radius*3.6f+1f),Rotation(section));
                }
            }
        }
    }
}
