using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Search turning manoeuvres in open apron space, retaining every trailer's actual pose.</summary>
    static class ApronPathfinder
    {
        sealed class Node
        {
            public BaggageVehiclePose pose;
            public BaggageTrain train;
            public VehiclePath leg;
            public Node parent;
            public float cost,score;
            public bool reverse;
        }

        sealed class Queue
        {
            readonly List<Node> heap=new List<Node>();
            public int Count=>heap.Count;
            public void Add(Node node)
            {
                int i=heap.Count; heap.Add(node);
                while (i>0)
                { int p=(i-1)/2; if (heap[p].score<=node.score) break; heap[i]=heap[p]; i=p; }
                heap[i]=node;
            }
            public Node Take()
            {
                var first=heap[0]; var last=heap[heap.Count-1]; heap.RemoveAt(heap.Count-1);
                if (heap.Count==0) return first;
                int i=0;
                while (i*2+1<heap.Count)
                {
                    int child=i*2+1;
                    if (child+1<heap.Count && heap[child+1].score<heap[child].score) child++;
                    if (heap[child].score>=last.score) break;
                    heap[i]=heap[child]; i=child;
                }
                heap[i]=last; return first;
            }
        }

        public static VehiclePath Train(BaggageVehiclePose from,BaggageVehiclePose to,BaggageServiceAccess equipment,
            Func<BaggageVehiclePose,VehicleType,bool> clear,BaggageTrain seed=null)
        {
            var start=new VehiclePath(); start.Start(from.position,1); start.LineTo(from.position+from.facing,1);
            seed ??=new BaggageTrain(equipment.tractorType,equipment.cartType,start,equipment.cartCount);
            return Search(from,to,Mathf.Max(6,equipment.tractorType.turnRadius,equipment.cartType.turnRadius),
                Mathf.Min(equipment.tractorType.transitSpeed,equipment.cartType.transitSpeed),equipment.tractorType,clear,seed);
        }

        public static VehiclePath Vehicle(BaggageVehiclePose from,BaggageVehiclePose to,VehicleType type,
            Func<BaggageVehiclePose,VehicleType,bool> clear) => Search(from,to,type.turnRadius,type.transitSpeed,type,clear,null);

        static VehiclePath Search(BaggageVehiclePose from,BaggageVehiclePose to,float radius,float speed,VehicleType type,
            Func<BaggageVehiclePose,VehicleType,bool> clear,BaggageTrain train)
        {
            var queue=new Queue(); var best=new Dictionary<(int,int,int,int,int),float>();
            float margin=Mathf.Max(radius*6f,train != null ? StandEquipmentLayout.TrainBounds(train.TractorType,train.CartType,train.CartCount).size.z*2 : type.Length*2);
            var min=Vector2.Min(from.position,to.position)-Vector2.one*margin;
            var max=Vector2.Max(from.position,to.position)+Vector2.one*margin;
            float Heuristic(BaggageVehiclePose pose) => Vector2.Distance(pose.position,to.position)+radius*.2f*Mathf.Abs(Vector2.SignedAngle(pose.facing,to.facing))*Mathf.Deg2Rad;
            int Heading(Vector2 f)=>Mathf.RoundToInt(Mathf.Repeat(BaggageTrain.Bearing(f),360)/15f)%24;
            (int,int,int,int,int) Key(Node n)=>(Mathf.RoundToInt(n.pose.position.x/1.25f),Mathf.RoundToInt(n.pose.position.y/1.25f),
                Heading(n.pose.facing),n.train != null ? Heading(n.train.CartAt(0).facing) : (n.reverse ? 1 : 0),
                n.train != null ? Heading(n.train.CartAt(n.train.CartCount-1).facing) : 0);
            bool Try(Node node,VehiclePath path,bool reverse,out Node next)
            {
                next=null; BaggageTrain trial=null;
                if (node.train != null)
                {
                    trial=node.train.Preview(path);
                    if (!BaggageRoute.Validate(trial,clear,out _)) return false;
                }
                else
                {
                    for (float d=0;d<path.Length;d+=.3f)
                    { var p=path.Pose(d,out var f,out _); if (!clear(new BaggageVehiclePose(p,f),type)) return false; }
                    var p1=path.Pose(path.Length,out var f1,out _);
                    if (!clear(new BaggageVehiclePose(p1,f1),type)) return false;
                }
                var end=path.Pose(path.Length,out var facing,out _);
                next=new Node { pose=trial?.Tractor ?? new BaggageVehiclePose(end,facing),train=trial,parent=node,leg=path,
                    reverse=reverse,cost=node.cost+path.Length*(reverse ? 1.8f : 1f)+(reverse != node.reverse ? 8f : 0) };
                next.score=next.cost+Heuristic(next.pose)*1.5f; return true;
            }
            VehiclePath Finish(Node node,VehiclePath last)
            {
                var legs=new List<VehiclePath>();
                for (var n=node;n.parent!=null;n=n.parent) legs.Add(n.leg);
                legs.Reverse(); var path=new VehiclePath(); foreach (var leg in legs) path.Append(leg); path.Append(last); return path;
            }
            var root=new Node { pose=from,train=train,score=Heuristic(from) }; queue.Add(root); best[Key(root)]=0;
            // A bounded apron neighbourhood keeps an impossible local manoeuvre from searching the airport graph.
            int remaining=6000;
            while (queue.Count>0 && remaining-->0)
            {
                var node=queue.Take(); if (best.TryGetValue(Key(node),out float cost) && cost<node.cost-.01f) continue;
                float distance=Vector2.Distance(node.pose.position,to.position);
                if (distance<radius*3f)
                    foreach (bool reverse in train == null ? new[] { false,true } : new[] { false })
                    {
                        float gear=reverse ? -1 : 1;
                        float length=VehiclePath.DubinsLength(node.pose.position,node.pose.facing*gear,to.position,to.facing*gear,radius);
                        if (length>distance+radius*3.5f) continue;
                        var finish=new VehiclePath(); finish.Start(node.pose.position,reverse ? 1f : speed);
                        finish.TurnTo(node.pose.facing*gear,to.position,to.facing*gear,radius,reverse ? 1f : speed,reverse);
                        if (Try(node,finish,reverse,out _)) return Finish(node,finish);
                    }
                foreach (bool reverse in train == null ? new[] { false,true } : new[] { false })
                    foreach (int turn in new[] { 0,-1,1 })
                    {
                        float gear=reverse ? -1 : 1, step=2.5f;
                        var path=new VehiclePath(); path.Start(node.pose.position,reverse ? 1f : speed);
                        var travel=node.pose.facing*gear;
                        if (turn==0) path.LineTo(node.pose.position+travel*step,reverse ? 1f : speed,reverse:reverse);
                        else
                        {
                            var left=new Vector2(-travel.y,travel.x);
                            path.ArcAround(node.pose.position+left*(radius*turn),turn*step/radius,reverse ? 1f : speed,reverse);
                        }
                        var point=path.End;
                        if (point.x<min.x || point.x>max.x || point.y<min.y || point.y>max.y) continue;
                        if (!Try(node,path,reverse,out var next)) continue;
                        var key=Key(next); if (best.TryGetValue(key,out var was) && was<=next.cost) continue;
                        best[key]=next.cost; queue.Add(next);
                    }
            }
            return null;
        }
    }
}
