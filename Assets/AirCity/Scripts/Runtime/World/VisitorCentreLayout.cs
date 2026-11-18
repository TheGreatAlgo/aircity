using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using AirCity.Game;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>The visitor centre's shared site frame. +Z is the public front; the car park joins the public
    /// road graph on its right. Construction, preview, protected land and generated art use these dimensions.</summary>
    public static class VisitorCentreLayout
    {
        public const string EntranceId = "airport-visitor-centre";
        public const float AisleCentre = 21.5f, AisleWidth = 6.5f, ParkingSurface = .035f, WalkSurface = .075f;
        public const float MaximumConnection = 95f;
        public static readonly Vector2 LocalEntrance = new Vector2(22f,AisleCentre);

        public readonly struct ParkingBay
        {
            public readonly Vector2 centre;
            public readonly float width, length;
            public readonly bool accessible, frontRow;
            public ParkingBay(float x,float z,bool accessible,bool front)
            { centre=new Vector2(x,z);width=accessible?3.2f:2.6f;length=5.5f;this.accessible=accessible;frontRow=front; }
        }
        static readonly ParkingBay[] parking = MakeParking();
        public static IReadOnlyList<ParkingBay> ParkingBays => parking;
        static ParkingBay[] MakeParking()
        {
            var bays=new List<ParkingBay>();
            foreach(float x in new[]{-17.4f,-14.6f,-11.8f,4.2f,7f,9.8f}) bays.Add(new ParkingBay(x,15.5f,false,true));
            bays.Add(new ParkingBay(-8.6f,15.5f,true,true));bays.Add(new ParkingBay(-4.2f,15.5f,true,true));
            foreach(float x in new[]{-15f,-12.2f,-9.4f,-6.6f,-3.8f,3.8f,6.6f,9.4f,12.2f,15f})
                bays.Add(new ParkingBay(x,27.5f,false,false));
            return bays.ToArray();
        }
        public static Vector2 Axis(float heading) => new Vector2(Mathf.Sin(heading*Mathf.Deg2Rad),Mathf.Cos(heading*Mathf.Deg2Rad));
        public static Vector2 Point(Vector2 origin,float heading,Vector2 local)
        { var axis=Axis(heading);return origin+Geo.Right(axis)*local.x+axis*local.y; }
        public static Vector2 Local(Vector2 origin,float heading,Vector2 world)
        { var axis=Axis(heading);var delta=world-origin;return new Vector2(Vector2.Dot(delta,Geo.Right(axis)),Vector2.Dot(delta,axis)); }
        public static Vector3 Entrance(Vector2 origin,float elevation,float heading) => Geo.X0Z(Point(origin,heading,LocalEntrance),elevation);
        public static Vector2[] Rectangle(Vector2 origin,float heading,float x0,float z0,float x1,float z1) =>
            new[]{Point(origin,heading,new Vector2(x0,z0)),Point(origin,heading,new Vector2(x1,z0)),
                Point(origin,heading,new Vector2(x1,z1)),Point(origin,heading,new Vector2(x0,z1))};

        public static Vector2[] Footprint(Vector2 origin,float heading,bool includeParking=true)
        {
            if(!includeParking)return Rectangle(origin,heading,-12,-8,12,10);
            var points=new[]{new Vector2(-20,-8),new Vector2(20,-8),new Vector2(20,17),new Vector2(24.7f,17),
                new Vector2(24.7f,26),new Vector2(20,26),new Vector2(20,32),new Vector2(-20,32)};
            return points.Select(p=>Point(origin,heading,p)).ToArray();
        }
        /// <summary>Convex protected regions for construction overlap checks; the small east throat does not
        /// reserve a strip all the way behind the building.</summary>
        public static IEnumerable<Vector2[]> Footprints(Vector2 origin,float heading,bool includeParking=true)
        {
            if(!includeParking){yield return Rectangle(origin,heading,-12,-8,12,10);yield break;}
            yield return Rectangle(origin,heading,-20,-8,20,32);
            yield return Rectangle(origin,heading,20,17,24.7f,26);
        }

        /// <summary>Opaque ground only: no roof or decorative fixture projection. Heights are supplied separately
        /// by the site's datum when registering terrain foundations.</summary>
        public static IEnumerable<Vector2[]> PavedPolygons(Vector2 origin,float heading,bool includeParking=true)
        {
            yield return Rectangle(origin,heading,-11.2f,-6.2f,11.2f,6.2f);
            if(!includeParking)yield break;
            yield return Rectangle(origin,heading,-12,6.2f,12,12.75f);
            yield return Rectangle(origin,heading,-20,12.75f,20,32);
            yield return Rectangle(origin,heading,20,18.25f,24.7f,24.75f);
        }

        public sealed class RoadPlan
        {
            public PublicRoadNetwork Draft {get;internal set;}
            public int EntranceNode {get;internal set;}
            public IReadOnlyList<int> NewSegments {get;internal set;}
            public float Length {get;internal set;}
            public int Price {get;internal set;}
        }

        /// <summary>Build a detached, bidirectional public road connection. Existing roads, node positions and
        /// entrance identities are retained. The caller validates terrain/airport clearance and purchases the plan.</summary>
        public static bool TryConnect(PublicRoadNetwork source,Vector2 origin,float elevation,float heading,
            out RoadPlan plan,out string problem)
        {
            plan=null;problem="Connect a public road beside the visitor centre's parking entrance.";
            if(source==null || source.segments.Count==0 || !Finite(origin.x) || !Finite(origin.y) || !Finite(elevation) || !Finite(heading))return false;
            var entrance=Entrance(origin,elevation,heading);
            if(source.nodes.Any(n=>Vector3.Distance(n.position,entrance)<.1f && n.terminalEntrance!=null && n.terminalEntrance!=EntranceId))
            {problem="Leave a little room between the parking entrance and the existing public-road entrance node.";return false;}
            var outsideNodes = source.nodes.Where(n => n.outsideConnection ||
                Mathf.Abs(n.position.x) > ValleyWorld.Size * .5f - 20f || Mathf.Abs(n.position.z) > ValleyWorld.Size * .5f - 20f)
                .Select(n => n.id).ToArray();
            bool HasOutsideRoute(PublicRoadNetwork graph, int node) =>
                outsideNodes.Any(id => id != node && graph.HasRoute(node, id) && graph.HasRoute(id, node));
            var already=source.nodes.FirstOrDefault(n=>n.terminalEntrance==EntranceId);
            if(already!=null && Vector3.Distance(already.position,entrance)>.15f)
            {problem="The visitor centre entrance is already connected at a different position.";return false;}
            if(already!=null && source.At(already.id).Any())
            {
                if (!HasOutsideRoute(source, already.id))
                { problem = "Reconnect the visitor centre entrance to a two-way public road."; return false; }
                plan=new RoadPlan {Draft=source.Clone(),EntranceNode=already.id,NewSegments=Array.Empty<int>()};problem=null;return true;
            }
            var lead=Point(origin,heading,new Vector2(34,AisleCentre));
            var candidates=new List<(Vector3 point,int node,int edge,float cost)>();
            foreach(var edge in source.segments)
            {
                if(edge.structure!=RoadStructure.Ground || !edge.Type.atGrade || edge.BackwardLanes==0 || edge.ForwardLanes==0)continue;
                var path=source.Path(edge);var at=path.Position(path.ClosestParameter(lead));
                int target=-1;
                foreach(int id in new[]{edge.a,edge.b})
                    if(Vector2.Distance(Geo.XZ(source.Node(id).position),Geo.XZ(at))<12f)
                    {target=id;at=source.Node(id).position;break;}
                var local=Local(origin,heading,Geo.XZ(at));
                if(local.x>-24.5f && local.x<27f && local.y>-12.5f && local.y<36.5f)continue;
                float distance=Vector2.Distance(Geo.XZ(entrance),Geo.XZ(at));
                if(distance>MaximumConnection || distance<6f)continue;
                int atNode=target>=0?target:edge.a;
                if (!outsideNodes.Contains(atNode) && !HasOutsideRoute(source, atNode)) continue;
                bool reception=source.Node(edge.a).terminalEntrance=="airport-reception" || source.Node(edge.b).terminalEntrance=="airport-reception";
                candidates.Add((at,target,edge.id,distance+(reception?-10f:0f)));
            }
            foreach(var candidate in candidates.OrderBy(c=>c.cost))
            {
                var target=Local(origin,heading,Geo.XZ(candidate.point));
                var alternatives=new List<List<Vector2>> {
                    new List<Vector2>{LocalEntrance,new Vector2(34,AisleCentre),target},
                    new List<Vector2>{LocalEntrance,new Vector2(34,AisleCentre),new Vector2(34,41),new Vector2(target.x,41),target},
                    new List<Vector2>{LocalEntrance,new Vector2(34,AisleCentre),new Vector2(34,-17),new Vector2(target.x,-17),target},
                    new List<Vector2>{LocalEntrance,new Vector2(34,AisleCentre),new Vector2(34,41),new Vector2(-29,41),target},
                    new List<Vector2>{LocalEntrance,new Vector2(34,AisleCentre),new Vector2(34,-17),new Vector2(-29,-17),target},
                };
                alternatives=alternatives.Select(Compact).Where(p=>SafePath(p)).OrderBy(PathLength).ToList();
                foreach(var local in alternatives)
                {
                    float length=PathLength(local);
                    if(length>MaximumConnection*1.8f || Mathf.Abs(elevation-candidate.point.y)/Mathf.Max(length,.01f)>PublicRoadConstruction.MaximumGroundGrade+.0001f)
                    {problem="Give the visitor centre driveway a longer, level approach (maximum 12% grade).";continue;}
                    var draft=source.Clone();var made=new List<int>();
                    int previous=already?.id ?? draft.AddNode(entrance,EntranceId),port=previous;
                    float along=0;bool valid=true;
                    for(int i=1;i<local.Count;i++)
                    {
                        along+=Vector2.Distance(local[i-1],local[i]);
                        var point=Geo.X0Z(Point(origin,heading,local[i]),Mathf.Lerp(elevation,candidate.point.y,along/length));
                        int next=i==local.Count-1 && candidate.node>=0 ? candidate.node : draft.AddNode(point);
                        if(!draft.Connect(previous,next,PublicRoadStyle.AirportAccess,RoadStructure.Ground,out var why,created:ids=>made.AddRange(ids)))
                        {problem=why;valid=false;break;}
                        previous=next;
                    }
                    if(!valid)continue;
                    if(source.nodes.Any(n=>draft.Node(n.id)==null || draft.Node(n.id).position!=n.position))
                    {problem="Move the visitor centre driveway away from an existing road bend; connecting here would move an authored road node.";continue;}
                    if(!draft.Validate(out problem))continue;
                    if (!HasOutsideRoute(draft, port))
                    { problem = "The driveway must connect to the region in both directions."; continue; }
                    var ids=made.Distinct().Where(id=>draft.Segment(id)!=null).ToArray();
                    float built=ids.Sum(id=>draft.Path(draft.Segment(id)).Length);
                    plan=new RoadPlan {Draft=draft,EntranceNode=port,NewSegments=ids,Length=built,
                        Price=Mathf.CeilToInt(built*RoadTypes.Of(PublicRoadStyle.AirportAccess).pricePerMetre)};
                    problem=null;return true;
                }
            }
            return false;
        }

        static List<Vector2> Compact(List<Vector2> source)
        {
            var result=new List<Vector2>();
            foreach(var p in source)if(result.Count==0 || Vector2.Distance(result[result.Count-1],p)>1f)result.Add(p);
            for(int i=result.Count-2;i>0;i--)
                if(Vector2.Angle(result[i]-result[i-1],result[i+1]-result[i])<1f)result.RemoveAt(i);
            return result;
        }
        static bool SafePath(List<Vector2> path)
        {
            if(path.Count<2)return false;
            for(int i=1;i<path.Count;i++)
            {
                var a=path[i-1];var b=path[i];
                if(i>1 && Vector2.Angle(a-path[i-2],b-a)>135f)return false;
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)));
                for(int k=0;k<=count;k++)
                {
                    var p=Vector2.Lerp(a,b,k/(float)count);
                    if(p.x<24.4f && p.x>-24.4f && p.y>-12.4f && p.y<36.4f && !(i==1 && p.x>=21.9f && Mathf.Abs(p.y-AisleCentre)<.1f))return false;
                }
            }
            return true;
        }
        static float PathLength(List<Vector2> points)
        {float length=0;for(int i=1;i<points.Count;i++)length+=Vector2.Distance(points[i-1],points[i]);return length;}
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
