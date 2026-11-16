using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>The same trimmed straights, bends and junctions used by TaxiwayBuilder, indexed locally.</summary>
    sealed class TaxiwayElevation
    {
        const float Cell=32f,IndexedMargin=4f;
        readonly struct Link
        {
            public readonly Vector2 a,delta;
            public readonly float h0,h1,half,inverseLength;
            public Link(Vector2 a,float h0,Vector2 b,float h1,float half)
            {this.a=a;delta=b-a;this.h0=h0;this.h1=h1;this.half=half;inverseLength=1f/Mathf.Max(.000001f,delta.sqrMagnitude);}
        }
        readonly List<Link> links=new List<Link>();
        readonly List<(List<Vector2> outline,float height)> junctions=new List<(List<Vector2>,float)>();
        readonly Dictionary<long,List<int>> linkCells=new Dictionary<long,List<int>>(),junctionCells=new Dictionary<long,List<int>>();
        static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        static long Key(Vector2 p)=>Key(Mathf.FloorToInt(p.x/Cell),Mathf.FloorToInt(p.y/Cell));
        static void Index(Dictionary<long,List<int>> cells,int id,Rect bounds)
        {
            for(int z=Mathf.FloorToInt(bounds.yMin/Cell);z<=Mathf.FloorToInt(bounds.yMax/Cell);z++)
            for(int x=Mathf.FloorToInt(bounds.xMin/Cell);x<=Mathf.FloorToInt(bounds.xMax/Cell);x++)
            {long key=Key(x,z);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<int>();list.Add(id);}
        }
        public void Add(Vector2 a,float h0,Vector2 b,float h1,float half)
        {
            int id=links.Count;links.Add(new Link(a,h0,b,h1,half));float margin=half+IndexedMargin;
            Index(linkCells,id,Rect.MinMaxRect(Mathf.Min(a.x,b.x)-margin,Mathf.Min(a.y,b.y)-margin,Mathf.Max(a.x,b.x)+margin,Mathf.Max(a.y,b.y)+margin));
        }
        public void Add(Vector2 centre,List<Vector2> outline,float height)
        {
            if(outline==null || outline.Count==0)return;
            int id=junctions.Count;junctions.Add((outline,height));var min=outline[0];var max=min;
            foreach(var p in outline){min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            Index(junctionCells,id,Rect.MinMaxRect(min.x-.001f,min.y-.001f,max.x+.001f,max.y+.001f));
        }
        public bool TryHeight(Vector2 p,out float height,float margin=4f,float hint=float.NaN)
        {
            long key=Key(p);float best=float.MaxValue;float value=0;
            if(junctionCells.TryGetValue(key,out var localJunctions))
                foreach(int id in localJunctions)
                {
                    var j=junctions[id];
                    if(Geo.Inside(j.outline,p) || Geo.DistanceToOutline(j.outline,p)<.001f)
                    {
                        if(float.IsNaN(hint)){height=j.height;return true;}
                        float score=(j.height-hint)*(j.height-hint)*1000f;
                        if(score<best){best=score;value=j.height;}
                    }
                }
            void Sample(Link link)
            {
                float t=Mathf.Clamp01(Vector2.Dot(p-link.a,link.delta)*link.inverseLength);
                float distance=(p-link.a-link.delta*t).sqrMagnitude,reach=link.half+margin;
                if(distance>reach*reach)return;float h=Mathf.Lerp(link.h0,link.h1,t);
                float score=float.IsNaN(hint)?distance:distance+(h-hint)*(h-hint)*1000f;
                if(score>=best)return;best=score;value=h;
            }
            if(margin>IndexedMargin){foreach(var link in links)Sample(link);}
            else if(linkCells.TryGetValue(key,out var localLinks))foreach(int id in localLinks)Sample(links[id]);
            height=value;return best<float.MaxValue;
        }
    }
}
