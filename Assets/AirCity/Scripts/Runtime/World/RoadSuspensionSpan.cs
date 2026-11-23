using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>A two-tower suspension bridge shared by every piece of a continuous road crossing.</summary>
    internal sealed class RoadSuspensionSpan
    {
        internal const float MinimumTowerSpan = 60f;
        const float LongSpanLength = 180f;
        const float DeepClearance = 32f;
        const float TallClearance = 50f;
        const float HangerSpacing = 9f;
        static readonly Color32 CableSteel = new Color32(85, 96, 105, 0);
        static readonly Color32 TowerSteel = new Color32(111, 133, 143, 0);
        static readonly Color32 Concrete = new Color32(255, 255, 255, 255);

        internal struct Section
        {
            public float station, halfWidth;
            public Vector3 centre, across;
        }

        readonly float length, towerA, towerB, crown, sag, anchorA, anchorB;
        internal int Signature => (length, towerA, towerB, crown, sag, anchorA, anchorB).GetHashCode();

        RoadSuspensionSpan(float length, float towerA, float towerB, float crown, float sag, float anchorA, float anchorB)
        {
            this.length=length;this.towerA=towerA;this.towerB=towerB;
            this.crown=crown;this.sag=sag;this.anchorA=anchorA;this.anchorB=anchorB;
        }

        internal static RoadSuspensionSpan Plan(RunSpan run, List<Section> sections, Func<Vector2,float> ground,
            Func<Vector2,float?> water, Func<Vector3,float,bool> canSupport, bool requested)
        {
            if(run.total<MinimumTowerSpan||!FitsAlignment(run,sections,ground,water))return null;
            var first=sections[0];var last=sections[sections.Count-1];
            float crossing=0f,longest=0f,deepCrossing=0f,peak=0f,highDeck=float.NegativeInfinity;
            float requiredDropLength=Mathf.Max(18f,run.total*.25f);
            bool tallSpan=false;
            var previous=first;
            foreach(var section in sections)
            {
                var mid=(section.centre+previous.centre)*.5f;
                var xy=Alignment3D.XZ(mid);var surface=water?.Invoke(xy);
                float clearance=mid.y-(surface??ground(xy)),distance=section.station-previous.station;
                crossing=clearance>=(surface.HasValue?.7f:DeepClearance)?crossing+distance:0f;
                longest=Mathf.Max(longest,crossing);
                if(clearance>=DeepClearance)
                {
                    deepCrossing+=distance;
                    peak=Mathf.Max(peak,clearance);
                    tallSpan|=deepCrossing>=requiredDropLength&&peak>=TallClearance;
                }
                else{deepCrossing=0f;peak=0f;}
                highDeck=Mathf.Max(highDeck,section.centre.y);
                previous=section;
            }
            bool longSpan=run.total>=LongSpanLength&&longest>=Mathf.Max(90f,run.total*.32f);
            if(!requested&&!longSpan&&!tallSpan)return null;

            float towerA=TowerStation(run.total*.19f),towerB=TowerStation(run.total*.81f);
            if(towerA<0f||towerB<0f)return null;
            float sag=Mathf.Clamp((towerB-towerA)*.13f,8f,110f);
            return new RoadSuspensionSpan(run.total,towerA,towerB,highDeck+sag+4f,sag,first.centre.y+1.1f,last.centre.y+1.1f);

            float TowerStation(float nominal)
            {
                float step=Mathf.Min(4f,run.total*.025f);
                for(int i=0;i<9;i++)
                {
                    float at=nominal+(i%2==0?1f:-1f)*((i+1)/2)*step;
                    var section=Sample(sections,at);
                    var across=section.across.normalized;
                    float offset=LegOffset(section.halfWidth), radius=LegWidth(section.halfWidth)*.8f;
                    if(canSupport==null || canSupport(section.centre+across*offset,radius)&&canSupport(section.centre-across*offset,radius))return at;
                }
                return -1f;
            }

        }

        internal static bool FitsAlignment(RunSpan run,List<Section> sections,Func<Vector2,float> ground,Func<Vector2,float?> water)
        {
            if(run.closed||sections.Count<2)return false;
            sections.Sort((a,b)=>a.station.CompareTo(b.station));
            var first=sections[0];var last=sections[sections.Count-1];
            var a=Alignment3D.XZ(first.centre);var b=Alignment3D.XZ(last.centre);
            if(Vector2.Distance(a,b)<run.total*.992f||!Bank(first,run.freeStart)||!Bank(last,run.freeEnd))return false;
            float tolerance=Mathf.Min(12f,run.total*.035f);
            foreach(var section in sections)
                if(Vector2.Distance(Alignment3D.XZ(section.centre),Vector2.Lerp(a,b,section.station/run.total))>tolerance)
                    return false;
            return true;

            bool Bank(Section section,bool free)
            {
                var point=Alignment3D.XZ(section.centre);
                // A ground approach already has an abutment at the next station. A free end must
                // find its own bank. The sample at a sharp ground-to-deck cut can lie well below it.
                return water?.Invoke(point)==null&&(!free||Mathf.Abs(section.centre.y-ground(point))<9f);
            }
        }

        internal static Section Sample(List<Section> sections,float station)
        {
            int low=0,high=sections.Count-1;
            while(high-low>1)
            {
                int mid=(low+high)/2;
                if(sections[mid].station<station)low=mid;else high=mid;
            }
            var a=sections[low];var b=sections[high];
            float t=Mathf.InverseLerp(a.station,b.station,station);
            return new Section {station=station,centre=Vector3.Lerp(a.centre,b.centre,t),
                across=Vector3.Lerp(a.across,b.across,t).normalized,halfWidth=Mathf.Lerp(a.halfWidth,b.halfWidth,t)};
        }

        static float LegWidth(float half) => Mathf.Clamp(half*.40f,1.6f,4.4f);
        static float LegOffset(float half) => half+LegWidth(half)*.65f+.45f;

        float CableHeight(float station)
        {
            if(station<towerA)return Mathf.Lerp(anchorA,crown,Mathf.Pow(station/towerA,1.4f));
            if(station>towerB)return Mathf.Lerp(anchorB,crown,Mathf.Pow((length-station)/(length-towerB),1.4f));
            float middle=2f*Mathf.InverseLerp(towerA,towerB,station)-1f;
            return crown-sag*(1f-middle*middle);
        }

        internal void Build(StructurePiece piece,LandscapeMesh mesh,Vector3[] centre,Vector3[] across,float[] along,float[] half,
            Func<Vector2,float> ground,Func<Vector2,float?> water)
        {
            var samples=DeckSections(piece,centre,across,along,half);
            float start=samples[0].station,end=samples[samples.Count-1].station;
            if(end-start<.001f)return;
            var points=new List<float>{start,end};
            for(float station=Mathf.Ceil(start/4f)*4f;station<end;station+=4f)if(station>start)points.Add(station);
            foreach(float station in new[]{towerA,towerB})if(station>start&&station<end)points.Add(station);
            points.Sort();

            mesh.colour=CableSteel;
            for(int side=-1;side<=1;side+=2)
            {
                var last=Cable(Sample(samples,points[0]),side);
                for(int i=1;i<points.Count;i++)
                {
                    var current=Cable(Sample(samples,points[i]),side);
                    Cylinder(mesh,last,current,Mathf.Clamp(length*.001f,.24f,1.25f));last=current;
                }
                for(float station=Mathf.Ceil(start/HangerSpacing)*HangerSpacing;station<end;station+=HangerSpacing)
                {
                    var section=Sample(samples,station);
                    var upper=Cable(section,side);
                    if(upper.y-section.centre.y<1.5f)continue;
                    var lower=new Vector3(upper.x,section.centre.y-.25f,upper.z);
                    var deckEdge=section.centre+section.across*(section.halfWidth-.3f)*side-Vector3.up*.25f;
                    float bracket=Vector3.Distance(lower,deckEdge);
                    mesh.OrientedBox((lower+deckEdge)*.5f,new Vector3(bracket+.24f,.4f,.5f),Rotation(section));
                    Cylinder(mesh,lower,upper,Mathf.Clamp(length*.00048f,.12f,.48f));
                }
            }
            foreach(float tower in new[]{towerA,towerB})
                if(tower>=start && tower<end)BuildTower(Sample(samples,tower),mesh,ground,water);
            if(start==0f)BuildAnchor(samples[0],mesh);
            if(end==length)BuildAnchor(samples[samples.Count-1],mesh);
        }

        internal static List<Section> DeckSections(StructurePiece piece,Vector3[] centre,Vector3[] across,float[] along,float[] half)
        {
            var stations=piece.Stations(along);
            var samples=new List<Section>(stations.Length+2);
            for(int i=0;i<stations.Length;i++)
            {
                int k=piece.run.reversed?stations.Length-1-i:i;
                samples.Add(new Section{station=stations[k],centre=centre[k],
                    across=across[k]*(piece.run.reversed?-1f:1f),halfWidth=half[k]+piece.verge});
            }
            if(piece.run.offset==0f)Extend(true,0f);
            if(piece.run.offset+piece.length>=piece.run.total-.01f)Extend(false,piece.run.total);
            return samples;

            void Extend(bool atStart,float at)
            {
                int index=atStart?0:samples.Count-1;
                var sample=samples[index];var other=samples[index+(atStart?1:-1)];
                float step=sample.station-other.station;
                if(Mathf.Abs(sample.station-at)<.001f||Mathf.Abs(step)<.001f)return;
                sample.centre+=(sample.centre-other.centre)*((at-sample.station)/step);sample.station=at;
                if(atStart)samples.Insert(0,sample);else samples.Add(sample);
            }
        }

        Vector3 Cable(Section section,int side)
        {
            var p=section.centre+section.across*LegOffset(section.halfWidth)*side;
            p.y=Mathf.Max(section.centre.y+1.1f,CableHeight(section.station));return p;
        }

        void BuildAnchor(Section section,LandscapeMesh mesh)
        {
            float width=LegWidth(section.halfWidth);
            mesh.colour=Concrete;
            for(int side=-1;side<=1;side+=2)
            {
                var top=Cable(section,side)+Vector3.up*.2f;
                mesh.OrientedBox(top-Vector3.up*.95f,new Vector3(width*1.25f,1.9f,width*2.2f),Rotation(section));
            }
        }

        void BuildTower(Section section,LandscapeMesh mesh,Func<Vector2,float> ground,Func<Vector2,float?> water)
        {
            float w=LegWidth(section.halfWidth),offset=LegOffset(section.halfWidth);
            var rotation=Rotation(section);float deck=section.centre.y;
            for(int side=-1;side<=1;side+=2)
            {
                var p=section.centre+section.across*offset*side;
                float foot=Foot(p);
                for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
                    foot=Mathf.Min(foot,Foot(p+rotation*new Vector3(x*w*.61f,0f,z*w*.775f)));
                var surface=water?.Invoke(Alignment3D.XZ(p));
                if(surface.HasValue)foot=Mathf.Max(foot,surface.Value-RoadStructureBuilder.SubmergedFootDepth);
                float baseTop=Mathf.Max(deck,w+foot);
                mesh.colour=Concrete;
                Box(p,foot,baseTop,w*1.22f,w*1.55f);
                mesh.colour=TowerSteel;
                Box(p,baseTop-.15f,crown+w*.55f,w,w*1.26f);
                Box(p,crown-w*.1f,crown+w*.56f,w*1.24f,w*1.48f);
            }
            mesh.colour=TowerSteel;
            float upper=Mathf.Max(deck+8f,crown-w*1.35f);
            float lower=Mathf.Lerp(deck,upper,.47f);
            Box(section.centre,upper-w*.5f,upper+w*.5f,offset*2f,w*.95f);
            if(lower>deck+6f) Box(section.centre,lower-w*.4f,lower+w*.4f,offset*2f,w*.67f);
            // The crosshead below the carriageway also carries the deck at the tower.
            Box(section.centre,deck-RoadStructureBuilder.DeckDepth-w*.65f,deck-RoadStructureBuilder.DeckDepth+.1f,offset*2f,w*1.3f);

            float Foot(Vector3 point)
            {
                var at=Alignment3D.XZ(point);
                return RoadStructureBuilder.SupportFoot(at,ground(at),water);
            }

            void Box(Vector3 at,float bottom,float top,float width,float depth)
            {
                if(top<=bottom)return;
                at.y=(top+bottom)*.5f;
                mesh.OrientedBox(at,new Vector3(width,top-bottom,depth),rotation);
            }
        }

        internal static Quaternion Rotation(Section section) =>
            Quaternion.LookRotation(Vector3.Cross(section.across,Vector3.up));

        internal static void Cylinder(LandscapeMesh mesh,Vector3 a,Vector3 b,float radius)
        {
            Vector3 axis=b-a;if(axis.sqrMagnitude<.0001f)return;
            axis.Normalize();
            var x=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.right:Vector3.up).normalized*radius;
            var y=Vector3.Cross(axis,x);
            const int sides=8;
            for(int i=0;i<sides;i++)
            {
                float from=i*Mathf.PI*2f/sides,to=(i+1)*Mathf.PI*2f/sides;
                var p=x*Mathf.Cos(from)+y*Mathf.Sin(from);var q=x*Mathf.Cos(to)+y*Mathf.Sin(to);
                mesh.Face(a+p,b+p,b+q,a+q,p+q);
            }
        }
    }
}
