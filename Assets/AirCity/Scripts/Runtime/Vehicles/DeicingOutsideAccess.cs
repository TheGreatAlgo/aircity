using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Game;
using AirCity.World;
using UnityEngine;

namespace AirCity.Vehicles
{
    [Serializable]
    public sealed class DeicingOutsideAccess
    {
        public int id, nodeId;
        public Vector2 position;
        public string Name => "Outside road connection " + id;
    }

    public sealed partial class AirportDeicing
    {
        int outsideConnectivityStamp=int.MinValue;
        HashSet<int> outsideConnectivity;
        public IReadOnlyList<DeicingOutsideAccess> OutsideAccess => airport.deicingOutsideAccess;
        public DeicingOutsideAccess OutsideAccessForNode(int nodeId) => airport.deicingOutsideAccess.Find(a => a.nodeId == nodeId);
        public Vector2 OutsideAccessPosition(DeicingOutsideAccess access) =>
            airport.Taxiways.Node(access.nodeId) != null ? airport.Taxiways.At(access.nodeId) : access.position;
        public bool IsOutsideAccess(int nodeId) => OutsideAccessForNode(nodeId) != null;
        static bool OpenService(TaxiwayEdge edge) => edge.road == RoadClass.Service && edge.surface.IsPaved() &&
            !TaxiwayNetwork.Closed(edge) && edge.wear < RunwayRules.ClosedWear;
        public string OutsideAccessProblem(int nodeId)
        {
            var net = airport.Taxiways; var node = net.Node(nodeId);
            if (node == null || node.runway != null) return "choose a service-road endpoint";
            int arms = 0; TaxiwayEdge road = null;
            foreach (var edge in net.EdgesAt(nodeId)) if (edge.road == RoadClass.Service) { arms++; road = edge; }
            if (arms != 1 || road == null) return "outside connections belong at service-road endpoints";
            if (!OpenService(road)) return "the outside service road must be paved and open";
            if (road.width < Mathf.Max(TruckType != null ? TruckType.Width : 0f, TankerType != null ? TankerType.Width : 0f) + .5f)
                return "widen the outside service road for deliveries";
            return PublicOutsideProblem(nodeId);
        }
        string PublicOutsideProblem(int nodeId)
        {
            var career = airport.GetComponentInParent<CareerGame>();
            if (career == null) return null; // In sandbox this explicit portal is the authored outside-world link.
            if (career.State == null || career.valley == null) return "connect this entrance to the public road first";
            var graph = career.State.RoadNetwork();
            if (graph == null) return "connect this entrance to the public road first";
            var world = airport.transform.TransformPoint(Geo.X0Z(airport.Taxiways.At(nodeId)));
            var local = career.valley.transform.InverseTransformPoint(world);
            foreach (var edge in graph.segments)
            {
                if (edge.structure != RoadStructure.Ground) continue;
                var a = graph.Node(edge.a); var b = graph.Node(edge.b);
                if (a == null || b == null) continue;
                var near = Geo.ClosestOnSegment(Geo.XZ(local), Geo.XZ(a.position), Geo.XZ(b.position), out float t);
                if (Vector2.Distance(near, Geo.XZ(local)) > edge.Width*.5f + 1f || Mathf.Abs(Mathf.Lerp(a.position.y,b.position.y,t)-local.y) > .5f) continue;
                foreach (var boundary in graph.nodes)
                    if (Mathf.Max(Mathf.Abs(boundary.position.x),Mathf.Abs(boundary.position.z)) >= ValleyWorld.Size*.5f-1f &&
                        graph.HasRoute(boundary.id,edge.a) && graph.HasRoute(edge.b,boundary.id)) return null;
            }
            return "the gate must meet a public road connected to the outside world";
        }
        public DeicingOutsideAccess AddOutsideAccess(int nodeId, out string problem)
        {
            problem = OutsideAccessProblem(nodeId); if (problem != null) return null;
            var existing = OutsideAccessForNode(nodeId); if (existing != null) return existing;
            int id = 1; foreach (var access in OutsideAccess) id = Mathf.Max(id,access.id+1);
            var added = new DeicingOutsideAccess { id=id, nodeId=nodeId, position=airport.Taxiways.At(nodeId) };
            airport.deicingOutsideAccess.Add(added); airport.LayoutChanged(); return added;
        }
        public bool SetOutsideAccess(int nodeId, bool enabled, out string problem)
        {
            if (enabled) return AddOutsideAccess(nodeId,out problem) != null;
            var access=OutsideAccessForNode(nodeId);
            if (access==null) { problem=null; return true; }
            return RemoveOutsideAccess(access,out problem);
        }
        public bool RemoveOutsideAccess(DeicingOutsideAccess access, out string problem)
        {
            problem=access==null || !airport.deicingOutsideAccess.Contains(access)?"choose an outside connection":null;
            if (problem!=null) return false;
            airport.deicingOutsideAccess.Remove(access); airport.LayoutChanged(); return true;
        }
        public bool TryOutsideAccess(Vector2 point, float radius, out int nodeId)
        {
            nodeId=0; float best=radius*radius;
            foreach (var node in airport.Taxiways.nodes)
            {
                float distance=(airport.Taxiways.At(node.id)-point).sqrMagnitude;
                if (distance>best || OutsideAccessProblem(node.id)!=null) continue;
                best=distance; nodeId=node.id;
            }
            return nodeId!=0;
        }
        HashSet<int> OutsideRoadNodes()
        {
            int stamp=SurfaceStamp();
            foreach(var access in OutsideAccess) unchecked { stamp=stamp*31+access.nodeId; }
            var career=airport.GetComponentInParent<CareerGame>();
            if(career?.State!=null)
            {
                var graph=career.State.publicRoads;
                unchecked { stamp=stamp*31+(graph?.GetHashCode()??0); stamp=stamp*31+(graph?.nodes.Count??0); stamp=stamp*31+(graph?.segments.Count??0); }
            }
            if(outsideConnectivity!=null && outsideConnectivityStamp==stamp) return outsideConnectivity;
            var reached=new HashSet<int>(); var queue=new Queue<int>(); var net=airport.Taxiways;
            foreach (var access in OutsideAccess)
                if (OutsideAccessProblem(access.nodeId)==null && reached.Add(access.nodeId)) queue.Enqueue(access.nodeId);
            while (queue.Count>0)
                foreach (var edge in net.EdgesAt(queue.Dequeue()))
                {
                    if (!OpenService(edge)) continue;
                    if (reached.Add(edge.a)) queue.Enqueue(edge.a);
                    if (reached.Add(edge.b)) queue.Enqueue(edge.b);
                }
            outsideConnectivityStamp=stamp; outsideConnectivity=reached; return reached;
        }
        public bool RoadConnectsOutside(TaxiwayEdge edge) => OpenService(edge) && OutsideRoadNodes().Contains(edge.a);
        bool OutsideConnectsArea(int nodeId,DeicingWorkArea area)
        {
            foreach(var edge in airport.Taxiways.edges)
            {
                if(!OpenService(edge)) continue;
                foreach(var port in new[]{area.UnloadEntry,area.UnloadExit})
                {
                    var at=Geo.ClosestOnSegment(port,airport.Taxiways.At(edge.a),airport.Taxiways.At(edge.b),out _);
                    if(Vector2.Distance(at,port)<=DeliveryRoadReach+edge.width*.5f && RoadRoute(nodeId,edge.a)) return true;
                }
            }
            return false;
        }
        bool RoadRoute(int from, int to)
        {
            var reached=new HashSet<int> { from }; var queue=new Queue<int>(); queue.Enqueue(from);
            while (queue.Count>0)
            {
                int node=queue.Dequeue(); if (node==to) return true;
                foreach (var edge in airport.Taxiways.EdgesAt(node))
                    if (OpenService(edge)) { int next=edge.a==node?edge.b:edge.a; if (reached.Add(next)) queue.Enqueue(next); }
            }
            return false;
        }
        bool TryOutsideVehicleRoute(VehicleType type, BaggageVehiclePose goal, DeicingTruckState self,
            out BaggageVehiclePose entry, out VehiclePath path)
        {
            entry=default; path=null; if(type==null) return false;
            TaxiwayEdge nearest=null; float best=float.PositiveInfinity;
            foreach(var edge in airport.Taxiways.edges)
            {
                if(!OpenService(edge)) continue;
                var at=Geo.ClosestOnSegment(goal.position,airport.Taxiways.At(edge.a),airport.Taxiways.At(edge.b),out _);
                float distance=(at-goal.position).sqrMagnitude;
                if(distance<best) { best=distance; nearest=edge; }
            }
            if(nearest==null) return false;
            foreach(var access in OutsideAccess)
            {
                if(OutsideAccessProblem(access.nodeId)!=null || !RoadRoute(access.nodeId,nearest.a)) continue;
                var net=airport.Taxiways; TaxiwayEdge arm=null;
                foreach(var edge in net.EdgesAt(access.nodeId)) if(OpenService(edge)) arm=edge;
                if(arm==null || net.EdgeLength(arm)<type.Length+2f) continue;
                var origin=net.At(access.nodeId);
                var facing=(net.At(arm.a==access.nodeId?arm.b:arm.a)-origin).normalized;
                var start=new BaggageVehiclePose(origin+facing*(Mathf.Max(0f,-type.bounds.min.z)+.5f),facing);
                var found=DeicingMotion.PlanLocalJourney(airport,type,start,goal,pose=>ClearOfOthers(self,pose))??
                    DeicingMotion.PlanRoadJourney(airport,type,start,goal,null,null,pose=>ClearOfOthers(self,pose));
                if(found==null) continue;
                entry=start; path=found; return true;
            }
            return false;
        }
    }
}
