using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Authored path data. Default values preserve the straight, constant-grade legacy save contract.</summary>
    [Serializable]
    public struct AlignmentShape : IEquatable<AlignmentShape>
    {
        public bool curved,smooth;
        public Vector2 control;
        public float startGrade,endGrade;
        public bool Equals(AlignmentShape other)=>curved==other.curved && smooth==other.smooth && control==other.control && startGrade==other.startGrade && endGrade==other.endGrade;
        public override bool Equals(object other)=>other is AlignmentShape shape && Equals(shape);
        public override int GetHashCode()=>HashCode.Combine(curved,smooth,control,startGrade,endGrade);
        public bool IsValid=>Finite(control.x)&&Finite(control.y)&&Finite(startGrade)&&Finite(endGrade);
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }

    /// <summary>Immutable shared centreline for geometry, marks, clearance, snapping, costs and vehicle contact.
    /// Horizontal curves are quadratic Beziers; vertical profiles are cubic Hermite. Distances use horizontal arc
    /// length, so grades and lane offsets are independent of the number of render subdivisions.</summary>
    public sealed class Alignment3D
    {
        public readonly Vector3 start,end;
        public readonly AlignmentShape shape;
        public readonly float Length;
        readonly float[] distances;
        readonly Vector2 a,b,control;
        Bounds? pickingBounds;
        public Alignment3D(Vector3 start,Vector3 end,AlignmentShape shape=default)
        {
            this.start=start;this.end=end;this.shape=shape;a=XZ(start);b=XZ(end);control=shape.control;
            if(!shape.curved){Length=Vector2.Distance(a,b);return;}
            int count=Mathf.Max(8,Mathf.CeilToInt((Vector2.Distance(a,control)+Vector2.Distance(control,b))/2f));
            distances=new float[count+1];var previous=a;
            for(int i=1;i<=count;i++){var next=Horizontal(i/(float)count);distances[i]=distances[i-1]+Vector2.Distance(previous,next);previous=next;}
            Length=distances[count];
        }
        public static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
        public Vector2 Horizontal(float t)
        {t=Mathf.Clamp01(t);return shape.curved?(1f-t)*(1f-t)*a+2f*(1f-t)*t*control+t*t*b:Vector2.Lerp(a,b,t);}
        Vector2 Derivative(float t)=>shape.curved?2f*((1f-t)*(control-a)+t*(b-control)):b-a;
        public float Height(float t)
        {
            t=Mathf.Clamp01(t);if(!shape.smooth)return Mathf.Lerp(start.y,end.y,t);
            float t2=t*t,t3=t2*t;
            return (2*t3-3*t2+1)*start.y+(t3-2*t2+t)*shape.startGrade*Length+(-2*t3+3*t2)*end.y+(t3-t2)*shape.endGrade*Length;
        }
        float HeightDerivative(float t)=>!shape.smooth?end.y-start.y:
            (6*t*t-6*t)*start.y+(3*t*t-4*t+1)*shape.startGrade*Length+(-6*t*t+6*t)*end.y+(3*t*t-2*t)*shape.endGrade*Length;
        public Vector3 Position(float t){var p=Horizontal(t);return new Vector3(p.x,Height(t),p.y);}
        public Vector3 Tangent(float t){var d=Derivative(Mathf.Clamp01(t));return new Vector3(d.x,HeightDerivative(Mathf.Clamp01(t)),d.y).normalized;}
        public float Grade(float t)=>HeightDerivative(Mathf.Clamp01(t))/Mathf.Max(.001f,Derivative(Mathf.Clamp01(t)).magnitude);
        public float MaximumGrade
        {get{float maximum=0;for(int i=0;i<=32;i++)maximum=Mathf.Max(maximum,Mathf.Abs(Grade(i/32f)));return maximum;}}
        public float ParameterAtDistance(float distance)
        {
            if(distances==null)return Mathf.Clamp01(distance/Mathf.Max(.001f,Length));
            distance=Mathf.Clamp(distance,0,Length);int low=0,high=distances.Length-1;
            while(high-low>1){int mid=(low+high)/2;if(distances[mid]<distance)low=mid;else high=mid;}
            return (low+Mathf.InverseLerp(distances[low],distances[high],distance))/(distances.Length-1f);
        }
        public float DistanceAtParameter(float t)
        {
            if(distances==null)return Mathf.Clamp01(t)*Length;
            float index=Mathf.Clamp01(t)*(distances.Length-1);int low=Mathf.Min((int)index,distances.Length-2);
            return Mathf.Lerp(distances[low],distances[low+1],index-low);
        }
        public Vector3 AtDistance(float distance)=>Position(ParameterAtDistance(distance));
        public Vector3 Offset(float distance,float right,float cutA=0,float cutB=0)
        {
            Vector3 Centre(float s)
            {
                float t=ParameterAtDistance(s);var p=Position(t);
                if(!shape.curved)return p;
                if(cutA>0)
                {
                    var d=Derivative(0).normalized;var straight=new Vector3(start.x+d.x*s,p.y,start.z+d.y*s);
                    p=Vector3.Lerp(straight,p,Mathf.SmoothStep(0,1,Mathf.InverseLerp(cutA,cutA+20,s)));
                }
                if(cutB>0)
                {
                    var d=Derivative(1).normalized;float back=Length-s;var straight=new Vector3(end.x-d.x*back,p.y,end.z-d.y*back);
                    p=Vector3.Lerp(straight,p,Mathf.SmoothStep(0,1,Mathf.InverseLerp(cutB,cutB+20,back)));
                }
                return p;
            }
            var at=Centre(distance);var tangent=shape.curved&&(cutA>0||cutB>0)?XZ(Centre(Mathf.Min(Length,distance+.05f))-Centre(Mathf.Max(0,distance-.05f))).normalized:Derivative(ParameterAtDistance(distance)).normalized;
            return at+new Vector3(tangent.y,0,-tangent.x)*right;
        }
        public float ClosestParameter(Vector2 p)
        {
            if(!shape.curved)return Mathf.Clamp01(Vector2.Dot(p-a,b-a)/Mathf.Max(.000001f,(b-a).sqrMagnitude));
            float best=0,distance=float.MaxValue;var second=2f*(b-2f*control+a);
            for(int seed=0;seed<3;seed++)
            {
                float t=seed*.5f;
                for(int iteration=0;iteration<7;iteration++)
                {var q=Horizontal(t)-p;var d=Derivative(t);float den=Vector2.Dot(d,d)+Vector2.Dot(q,second);if(Mathf.Abs(den)<.000001f)break;t=Mathf.Clamp01(t-Vector2.Dot(q,d)/den);}
                float d2=(Horizontal(t)-p).sqrMagnitude;if(d2<distance){distance=d2;best=t;}
            }
            return best;
        }
        public Rect Bounds(float padding = 0f)
        {
            var min = a; var max = a;
            int samples = shape.curved ? Mathf.Max(8, Mathf.CeilToInt(Length / 4f)) : 1;
            for (int i = 1; i <= samples; i++) { var p = Horizontal(i / (float)samples); min = Vector2.Min(min,p); max = Vector2.Max(max,p); }
            return Rect.MinMaxRect(min.x-padding,min.y-padding,max.x+padding,max.y+padding);
        }
        public float DistanceTo(Vector2 p)=>Vector2.Distance(p,Horizontal(ClosestParameter(p)));
        public IEnumerable<(float t0,float t1,Vector3 a,Vector3 b)> Spans(float spacing=4f)
        {
            int count=shape.curved?Mathf.Max(1,Mathf.CeilToInt(Length/spacing)):1;
            for(int i=0;i<count;i++){float t0=ParameterAtDistance(Length*i/count),t1=ParameterAtDistance(Length*(i+1)/count);yield return(t0,t1,Position(t0),Position(t1));}
        }
        public bool Raycast(Ray ray,float half,out float distance,float lift=0f)
        {
            if(!pickingBounds.HasValue)
            {
                var min=Vector3.Min(start,end);var max=Vector3.Max(start,end);
                if(shape.curved){min=Vector3.Min(min,new Vector3(control.x,min.y,control.y));max=Vector3.Max(max,new Vector3(control.x,max.y,control.y));}
                if(shape.smooth)
                {
                    float first=start.y+shape.startGrade*Length/3f,last=end.y-shape.endGrade*Length/3f;
                    min.y=Mathf.Min(min.y,first,last);max.y=Mathf.Max(max.y,first,last);
                }
                var bounds=new Bounds();bounds.SetMinMax(min,max);pickingBounds=bounds;
            }
            var broad=pickingBounds.Value;broad.center+=Vector3.up*lift;broad.Expand(new Vector3(half*2,.1f,half*2));
            if(!broad.IntersectRay(ray)){distance=float.PositiveInfinity;return false;}
            float best=float.PositiveInfinity;int count=shape.curved||shape.smooth?Mathf.Max(1,Mathf.CeilToInt(Length/4f)):1;
            void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                var ab=b-a;var ac=c-a;var cross=Vector3.Cross(ray.direction,ac);float determinant=Vector3.Dot(ab,cross);
                if(Mathf.Abs(determinant)<.000001f)return;float inverse=1/determinant;var delta=ray.origin-a;
                float u=Vector3.Dot(delta,cross)*inverse;if(u<0||u>1)return;var q=Vector3.Cross(delta,ab);float v=Vector3.Dot(ray.direction,q)*inverse;
                if(v<0||u+v>1)return;float t=Vector3.Dot(ac,q)*inverse;if(t>=0&&t<best)best=t;
            }
            var up=Vector3.up*lift;
            for(int i=0;i<count;i++)
            {
                float a=Length*i/count,b=Length*(i+1)/count;
                var l0=Offset(a,-half)+up;var r0=Offset(a,half)+up;var l1=Offset(b,-half)+up;var r1=Offset(b,half)+up;
                Triangle(l0,l1,r1);Triangle(l0,r1,r0);
            }
            distance=best;return !float.IsPositiveInfinity(best);
        }
        public AlignmentShape Slice(float t0,float t1)
        {
            t0=Mathf.Clamp01(t0);t1=Mathf.Clamp01(t1);var first=Horizontal(t0);var last=Horizontal(t1);
            var result=shape;
            if(shape.curved)result.control=first+Derivative(t0)*(t1-t0)*.5f;
            var sliced=new Alignment3D(Position(t0),Position(t1),new AlignmentShape {curved=result.curved,control=result.control});
            if(shape.smooth){result.startGrade=HeightDerivative(t0)*(t1-t0)/Mathf.Max(.001f,sliced.Length);result.endGrade=HeightDerivative(t1)*(t1-t0)/Mathf.Max(.001f,sliced.Length);}
            return result;
        }
    }
}
