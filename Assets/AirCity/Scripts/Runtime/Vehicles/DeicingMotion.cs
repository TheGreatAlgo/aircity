using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;
using Box = AirCity.Vehicles.AirportMaintenance.Box;

namespace AirCity.Vehicles
{
    /// <summary>A real ground-vehicle journey: finite-radius paths, acceleration, braking and wheel steering.</summary>
    public sealed class DeicingDrive
    {
        readonly VehicleType type;
        readonly List<float> cusps = new List<float>();
        int cuspIndex;
        float shiftTime;
        public VehiclePath Path { get; private set; }
        public float Along { get; private set; }
        public float Speed { get; private set; }
        public Vector2 Position { get; private set; }
        public Vector2 Forward { get; private set; }
        public float Steering { get; private set; }
        public bool Finished => Path == null || Along >= Path.Length - .0001f;
        public BaggageVehiclePose Pose => new BaggageVehiclePose(Position, Forward,Path!=null?Path.Height(Along,float.NaN):float.NaN);
        public DeicingDrive(BaggageVehiclePose initial, VehicleType type)
        { Position = initial.position; Forward = initial.facing; this.type = type; }
        public void Begin(VehiclePath path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (Vector2.Distance(path.Sample(0f), Position) > .08f)
                throw new ArgumentException("A truck journey must start at its current position.");
            Path = path; Along = 0f; Speed = 0f; Steering = 0f; shiftTime = 0f;
            cusps.Clear(); path.Cusps(cusps); cuspIndex = 0;
        }
        public void Hold() { Speed = 0f; Steering = 0f; }
        public void StopAndClear()
        { Path=null; Along=0f; Speed=0f; Steering=0f; shiftTime=0f; cusps.Clear(); cuspIndex=0; }
        public void Step(float seconds)
        {
            if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            for (float left = seconds; left > .00001f && !Finished;)
            {
                float dt = Mathf.Min(left, .05f); left -= dt;
                if (shiftTime > 0f) { shiftTime = Mathf.Max(0f, shiftTime - dt); Speed = 0f; continue; }
                const float acceleration = 1.3f, braking = 2f;
                Path.Sample(Along, out _, out _, out float limit);
                float stop = cuspIndex < cusps.Count ? cusps[cuspIndex] : Path.Length;
                float remaining = Mathf.Max(0f, stop - Along);
                float old = Mathf.Abs(Speed);
                float Envelope(float distance, float atSpeed)
                {
                    float bdt = braking * dt;
                    return Mathf.Max(0f, (-bdt + Mathf.Sqrt(Mathf.Max(0f, bdt * bdt - 4f * bdt * old + 4f * atSpeed * atSpeed + 8f * braking * distance))) * .5f);
                }
                float target = Mathf.Min(limit, Envelope(remaining, 0f));
                float horizon = Mathf.Min(Path.Length, Along + old * old / (2f * braking) + 8f);
                for (float d = Along + .5f; d <= horizon; d += .5f)
                {
                    Path.Sample(d, out _, out _, out float aheadLimit);
                    target = Mathf.Min(target, Envelope(d - Along, aheadLimit));
                }
                float speed = Mathf.MoveTowards(old, target, (target < old ? braking : acceleration) * dt);
                float next = Mathf.Min(stop, Along + (old + speed) * .5f * dt);
                if (remaining < .0001f && old < braking * dt + .001f) next = stop;
                Along = next;
                Position = Path.Pose(Along, out var facing, out float curvature); Forward = facing;
                Speed = Path.Reversing(Along) ? -speed : speed;
                float wheelbase = type != null ? Mathf.Max(.5f, type.wheelbase) : 4f;
                Steering = Mathf.Clamp(-Mathf.Atan(curvature * wheelbase) * Mathf.Rad2Deg, -40f, 40f);
                if (Along >= stop - .0001f && cuspIndex < cusps.Count)
                { Speed = 0f; Steering = 0f; cuspIndex++; shiftTime = .6f; }
                if (Finished) { Speed = 0f; Steering = 0f; }

            }
        }
    }

    /// <summary>Shared planning for pad manoeuvres and journeys to a separate service-road work area.</summary>
    public static class DeicingMotion
    {
        public static BaggageVehiclePose ParkPosition(StandLayout layout, int index, VehicleType type)
        {
            float side = index % 2 == 0 ? -1f : 1f;
            float half = type != null ? type.Width * .5f : 1.6f;
            float rear = type != null ? -type.bounds.min.z : 2.1f;
            float x = side < 0 ? layout.x0 + half + 1f : layout.x1 - half - 1f;
            return new BaggageVehiclePose(layout.At(x, layout.near0 + rear + 1f), layout.axis);
        }

        public static VehiclePath Plan(Airport airport, VehicleType type, BaggageVehiclePose from,
            BaggageVehiclePose to, Aircraft.Aircraft servicing = null, Func<BaggageVehiclePose, bool> extraClear = null)
        {
            if (airport == null || type == null || !SurfaceConnected(airport, from.position, to.position)) return null;
            var clearance = new Clearance(airport, type, servicing);
            bool Clear(BaggageVehiclePose pose, VehicleType _) => clearance.At(pose) && (extraClear == null || extraClear(pose));
            if (!Clear(from, type) || !Clear(to, type)) return null;
            var direct = Local(from, to, type, Clear, false);
            if (direct != null && direct.Length <= Vector2.Distance(from.position,to.position)+Mathf.PI*Radius(type)) return direct;
            var crossover=servicing!=null?AcrossAircraft(airport,type,from,to,servicing,Clear):null;
            if(crossover!=null && (direct==null || crossover.Length<direct.Length)) return crossover;
            var around=servicing!=null?AroundWingTip(airport,type,from,to,servicing,Clear):null;
            if(around!=null && (direct==null || around.Length<direct.Length)) return around;
            if(direct!=null) return direct;
            if (Vector2.Distance(from.position,to.position)<80f && SharedWorkspace(airport,from.position,to.position))
            { var manoeuvre=Local(from,to,type,Clear,true); if(manoeuvre!=null) return manoeuvre; }
            var net = airport.Taxiways;
            var starts = RoadNodes(net, from.position); var ends = RoadNodes(net, to.position);
            foreach (int start in starts)
                foreach (int end in ends)
                {
                    if (start == end) continue;
                    if (!BaggageRoute.Plan(net, start, end, Radius(type), type.transitSpeed,
                        type.Width + .5f, out var road, out _)) continue;
                    if (!Validate(road, type, Clear)) continue;
                    var roadStart = road.Pose(0f, out var startFacing, out _);
                    var approach = Local(from, new BaggageVehiclePose(roadStart, startFacing), type, Clear, false);
                    if (approach == null) continue;
                    var departure = Local(new BaggageVehiclePose(road.End, road.EndFacing), to, type, Clear, false);
                    if (departure == null) continue;
                    approach.Append(road); approach.Append(departure); return approach;
                }
            // A bounded hybrid search finds multi-point turns around local wings and narrow pad entrances.
            return Local(from, to, type, Clear, true);
        }
        public static int LastRoadCandidates { get; private set; }
        public static VehiclePath PlanLocalJourney(Airport airport,VehicleType type,BaggageVehiclePose from,BaggageVehiclePose to,
            Func<BaggageVehiclePose,bool> extraClear=null)
        {
            if(airport==null || type==null) return null;
            float maximum=SharedWorkspace(airport,from.position,to.position)?250f:80f;
            if(Vector2.Distance(from.position,to.position)>maximum || !SurfaceConnected(airport,from.position,to.position)) return null;
            var clearance=new Clearance(airport,type,null);
            bool Clear(BaggageVehiclePose pose,VehicleType _) => clearance.At(pose) && (extraClear==null || extraClear(pose));
            return Clear(from,type) && Clear(to,type)?Local(from,to,type,Clear,false):null;
        }
        /// <summary>Bounded road journeys with short docking connectors. Never runs a hybrid search.</summary>
        public static VehiclePath PlanRoadJourney(Airport airport,VehicleType type,BaggageVehiclePose from,BaggageVehiclePose to,
            Vector2? fromPort=null,Vector2? toPort=null,Func<BaggageVehiclePose,bool> extraClear=null)
        {
            LastRoadCandidates=0;
            if(airport==null || type==null || !SurfaceConnected(airport,from.position,to.position)) return null;
            var clearance=new Clearance(airport,type,null);
            bool Clear(BaggageVehiclePose pose,VehicleType _) => clearance.At(pose) && (extraClear==null || extraClear(pose));
            if(!Clear(from,type) || !Clear(to,type)) return null;
            var net=airport.Taxiways;
            var starts=RoadProjections(net,fromPort??from.position,type,fromPort.HasValue);
            var ends=RoadProjections(net,toPort??to.position,type,toPort.HasValue);
            var candidates=new List<VehiclePath>();
            var unique=new HashSet<int>();
            void Candidate(List<Vector2> points,List<TaxiwayEdge> routeEdges)
            {
                for(int i=points.Count-1;i>0;i--) if((points[i]-points[i-1]).sqrMagnitude<.0025f) points.RemoveAt(i);
                for(int i=1;i<points.Count-1;)
                {
                    var a=(points[i]-points[i-1]).normalized; var b=(points[i+1]-points[i]).normalized;
                    if(Vector2.Dot(a,b)>.999f) points.RemoveAt(i); else i++;
                }
                if(points.Count<2) return;
                int hash=points.Count; foreach(var point in points) hash=unchecked(hash*31+point.GetHashCode());
                if(!unique.Add(hash)) return;
                var origin=points[0]; points.RemoveAt(0);
                // A long front overhang may leave the inside pavement even at the steering minimum.
                // Try a wider physical road turn as well; docking connectors keep their normal radius.
                foreach(float radius in new[]{Radius(type),Radius(type)*2f})
                {
                    var path=new VehiclePath(); path.Start(origin,type.transitSpeed);
                    path.Through(points,radius,type.transitSpeed);BaggageRoute.BindElevation(net,path,routeEdges);candidates.Add(path);
                }
            }
            foreach(var start in starts) foreach(var end in ends)
            {
                if(start.edge==end.edge) Candidate(new List<Vector2>{start.point,end.point},new List<TaxiwayEdge>{start.edge});
                foreach(int a in new[]{start.edge.a,start.edge.b}) foreach(int b in new[]{end.edge.a,end.edge.b})
                {
                    var route=ServiceRoute(airport,a,b,type.Width+.5f);
                    if(route==null) continue;
                    if(route.Count>1 && route[1]==TaxiwayNetwork.Other(start.edge,a) && Vector2.Distance(start.point,net.At(a))>.1f) continue;
                    if(route.Count>1 && route[route.Count-2]==TaxiwayNetwork.Other(end.edge,b) && Vector2.Distance(end.point,net.At(b))>.1f) continue;
                    var points=new List<Vector2>{start.point};var routeEdges=new List<TaxiwayEdge>{start.edge,end.edge};
                    if(route.Count>0)points.Add(net.At(route[0]));
                    for(int i=1;i<route.Count;i++)
                    {
                        int fromNode=route[i-1],toNode=route[i];var edge=net.edges.Find(e=>e.a==fromNode&&e.b==toNode||e.a==toNode&&e.b==fromNode);routeEdges.Add(edge);
                        var path=net.Path(edge);int count=edge.alignment.curved?Mathf.Max(1,Mathf.CeilToInt(path.Length/6f)):1;
                        for(int k=1;k<=count;k++)points.Add(Geo.XZ(path.Position(edge.a==fromNode?k/(float)count:1-k/(float)count)));
                    }
                    points.Add(end.point);Candidate(points,routeEdges);
                }
            }
            float Score(VehiclePath path)
            {
                var start=path.Pose(0f,out var facing,out _); float radius=Radius(type);
                float Join(BaggageVehiclePose a,BaggageVehiclePose b) => Mathf.Min(
                    VehiclePath.DubinsLength(a.position,a.facing,b.position,b.facing,radius),
                    VehiclePath.DubinsLength(a.position,-a.facing,b.position,-b.facing,radius));
                return path.Length+Join(from,new BaggageVehiclePose(start,facing))+Join(new BaggageVehiclePose(path.End,path.EndFacing),to);
            }
            candidates.Sort((a,b)=>Score(a).CompareTo(Score(b)));
            foreach(var road in candidates)
            {
                if(++LastRoadCandidates>4) { LastRoadCandidates=4; break; }
                var first=road.Pose(0f,out var facing,out _); var roadStart=new BaggageVehiclePose(first,facing);
                var roadEnd=new BaggageVehiclePose(road.End,road.EndFacing);
                if(Vector2.Distance(from.position,first)>70f || Vector2.Distance(to.position,road.End)>70f) continue;
                if(Vector2.Distance(from.position,first)<.1f && Vector2.Dot(from.facing,facing)<.7f) continue;
                if(Vector2.Distance(to.position,road.End)<.1f && Vector2.Dot(to.facing,road.EndFacing)<.7f) continue;
                var approach=Local(from,roadStart,type,Clear,false); if(approach==null) continue;
                var departure=Local(roadEnd,to,type,Clear,false); if(departure==null) continue;
                if(!Validate(road,type,Clear)) continue;
                approach.Append(road); approach.Append(departure); return approach;
            }
            return null;
        }
        sealed class ServiceRouteCache
        {
            public int stamp=int.MinValue;
            public readonly Dictionary<(int,int,float),List<int>> routes=new Dictionary<(int,int,float),List<int>>();
        }
        static readonly ConditionalWeakTable<Airport,ServiceRouteCache> serviceRoutes=new ConditionalWeakTable<Airport,ServiceRouteCache>();
        public static int RoadGraphSearchCount { get; private set; }
        static List<int> ServiceRoute(Airport airport,int from,int to,float width)
        {
            var net=airport.Taxiways; int stamp=airport.LayoutVersion;
            foreach(var edge in net.edges) stamp=unchecked(stamp*31+edge.id+edge.width.GetHashCode()+edge.road.GetHashCode()+edge.surface.GetHashCode()+
                (TaxiwayNetwork.Closed(edge)||edge.wear>=RunwayRules.ClosedWear?1:0));
            foreach(var node in net.nodes) stamp=unchecked(stamp*31+node.position.GetHashCode());
            var cache=serviceRoutes.GetValue(airport,_=>new ServiceRouteCache());
            if(cache.stamp!=stamp) { cache.stamp=stamp; cache.routes.Clear(); }
            var key=(from,to,width); if(cache.routes.TryGetValue(key,out var found)) return found;
            RoadGraphSearchCount++;
            found=net.FindRoute(from,to,e=>e.road==RoadClass.Service && e.surface.IsPaved() && e.width>=width &&
                !TaxiwayNetwork.Closed(e) && e.wear<RunwayRules.ClosedWear && net.Node(e.a).runway==null && net.Node(e.b).runway==null,r=>false);
            cache.routes[key]=found; return found;
        }
        static List<(TaxiwayEdge edge,Vector2 point,float distance)> RoadProjections(TaxiwayNetwork net,Vector2 point,VehicleType type,bool docking)
        {
            var result=new List<(TaxiwayEdge edge,Vector2 point,float distance)>();
            foreach(var edge in net.edges)
            {
                if(edge.road!=RoadClass.Service || !edge.surface.IsPaved() || edge.width<type.Width+.5f || TaxiwayNetwork.Closed(edge) || edge.wear>=RunwayRules.ClosedWear) continue;
                var near=Geo.ClosestOnSegment(point,net.At(edge.a),net.At(edge.b),out _); float d=(point-near).sqrMagnitude;
                float reach=docking?25f:70f;
                if(d>reach*reach) continue;
                void Add(Vector2 anchor)
                {
                    float distance=(point-anchor).sqrMagnitude; if(distance>reach*reach) return;
                    foreach(var old in result) if((old.point-anchor).sqrMagnitude<.01f) return;
                    result.Add((edge,anchor,distance));
                }
                Add(near);
                if(docking)
                {
                    var direction=(net.At(edge.b)-net.At(edge.a)).normalized; float lead=Radius(type)*2f+type.Width;
                    Add(Geo.ClosestOnSegment(point+direction*lead,net.At(edge.a),net.At(edge.b),out _));
                    Add(Geo.ClosestOnSegment(point-direction*lead,net.At(edge.a),net.At(edge.b),out _));
                }
            }
            result.Sort((a,b)=>a.distance.CompareTo(b.distance));
            int limit=docking?6:2;
            if(result.Count>limit) result.RemoveRange(limit,result.Count-limit); return result;
        }
        static VehiclePath AroundWingTip(Airport airport,VehicleType truck,BaggageVehiclePose from,BaggageVehiclePose to,
            Aircraft.Aircraft aircraft,Func<BaggageVehiclePose,VehicleType,bool> clear)
        {
            var origin=Geo.XZ(airport.transform.InverseTransformPoint(aircraft.transform.position));
            var forward=Geo.XZ(airport.transform.InverseTransformDirection(aircraft.transform.forward)).normalized; var right=Geo.Right(forward);
            Vector2 ToLocal(Vector2 p) { var d=p-origin; return new Vector2(Vector2.Dot(d,right),Vector2.Dot(d,forward)); }
            Vector2 World(Vector2 p)=>origin+right*p.x+forward*p.y;
            var a=ToLocal(from.position); var b=ToLocal(to.position);
            if(a.x*b.x<=0f || Mathf.Abs(a.x)>aircraft.type.Wingspan || Mathf.Abs(b.x)>aircraft.type.Wingspan) return null;
            var sections=aircraft.type.deicingWing; if(sections==null || sections.Length==0) return null;
            var tip=sections[sections.Length-1]; float side=Mathf.Sign(a.x),radius=Radius(truck),speed=truck.transitSpeed;
            bool aft=a.y>b.y;
            VehiclePath best=null;
            foreach(float extra in new[]{truck.Width*.5f+1f,radius+truck.Width*.5f+1f})
            {
                float x=side*(aircraft.type.Wingspan*.5f+extra);
                float front=tip.leading.z+radius+truck.Length;
                float rear=tip.trailing.z-radius-truck.Length*.5f;
                var first=World(new Vector2(x,aft?front:rear)); var second=World(new Vector2(x,aft?rear:front));
                var travel=forward*(aft?-1f:1f);
                var approach=Local(from,new BaggageVehiclePose(first,travel),truck,clear,false);
                if(approach==null) continue;
                var crossing=new VehiclePath(); crossing.Start(first,speed); crossing.LineTo(second,speed);
                if(!Validate(crossing,truck,clear)) continue;
                var departure=Local(new BaggageVehiclePose(second,travel),to,truck,clear,false);
                if(departure==null) continue;
                approach.Append(crossing); approach.Append(departure);
                float cap=Vector2.Distance(from.position,to.position)+4f*Mathf.PI*radius+truck.Length;
                if(approach.Length<=cap && (best==null || approach.Length<best.Length)) best=approach;
            }
            return best;
        }
        static VehiclePath AcrossAircraft(Airport airport,VehicleType truck,BaggageVehiclePose from,BaggageVehiclePose to,
            Aircraft.Aircraft aircraft,Func<BaggageVehiclePose,VehicleType,bool> clear)
        {
            var origin=Geo.XZ(airport.transform.InverseTransformPoint(aircraft.transform.position));
            var forward=Geo.XZ(airport.transform.InverseTransformDirection(aircraft.transform.forward)).normalized; var right=Geo.Right(forward);
            Vector2 LocalPoint(Vector2 point) { var delta=point-origin; return new Vector2(Vector2.Dot(delta,right),Vector2.Dot(delta,forward)); }
            var a=LocalPoint(from.position); var b=LocalPoint(to.position);
            if(a.x*b.x>=0f) return null;
            float span=Mathf.Max(Mathf.Abs(aircraft.type.bounds.min.x),Mathf.Abs(aircraft.type.bounds.max.x));
            float front=aircraft.type.bounds.max.z,rear=aircraft.type.bounds.min.z;
            foreach(var part in aircraft.type.serviceFootprints) foreach(var point in part.outline)
            { span=Mathf.Max(span,Mathf.Abs(point.x)); front=Mathf.Max(front,point.y); rear=Mathf.Min(rear,point.y); }
            float outboard=span+Radius(truck)+truck.Width*.5f+1f;
            float side=Mathf.Sign(b.x-a.x); var facing=right*side;
            VehiclePath best=null;
            foreach(float z in new[]{front+truck.Width*.5f+1f,rear-truck.Width*.5f-1f})
            {
                var first=new BaggageVehiclePose(origin-right*(side*outboard)+forward*z,facing);
                var last=new BaggageVehiclePose(origin+right*(side*outboard)+forward*z,facing);
                var approach=Local(from,first,truck,clear,false)??AroundWingTip(airport,truck,from,first,aircraft,clear);
                if(approach==null) continue;
                var crossing=new VehiclePath(); crossing.Start(first.position,truck.transitSpeed); crossing.LineTo(last.position,truck.transitSpeed);
                if(!Validate(crossing,truck,clear)) continue;
                var departure=Local(last,to,truck,clear,false)??AroundWingTip(airport,truck,last,to,aircraft,clear);
                if(departure==null) continue;
                approach.Append(crossing); approach.Append(departure);
                float maximum=Vector2.Distance(from.position,to.position)+8f*Mathf.PI*Radius(truck)+truck.Length*2f;
                if(approach.Length<=maximum && (best==null || approach.Length<best.Length)) best=approach;
            }
            return best;
        }
        static bool SharedWorkspace(Airport airport,Vector2 from,Vector2 to)
        {
            foreach(var apron in airport.aprons) if(apron.surface.IsPaved() && apron.Contains(from) && apron.Contains(to)) return true;
            foreach(var pad in airport.Taxiways.Layouts()) if(pad.stand.surface.IsPaved() && pad.Contains(from) && pad.Contains(to)) return true;
            return false;
        }
        sealed class SurfaceGraph
        {
            public int stamp;
            public readonly List<IList<Vector2>> polygons = new List<IList<Vector2>>();
            public readonly List<List<int>> adjacent = new List<List<int>>();
            public void Build(Airport airport, int stamp)
            {
                this.stamp = stamp; polygons.Clear(); adjacent.Clear();
                foreach (var apron in airport.aprons) if (apron.surface.IsPaved() && apron.outline.Count >= 3) polygons.Add(apron.outline);
                foreach (var pad in airport.Taxiways.Layouts()) if (pad.stand.surface.IsPaved()) polygons.Add(pad.Pad);
                foreach (var edge in airport.Taxiways.edges)
                {
                    if (edge.road != RoadClass.Service || !edge.surface.IsPaved() || TaxiwayNetwork.Closed(edge) || edge.wear >= RunwayRules.ClosedWear) continue;
                    var a=airport.Taxiways.At(edge.a); var b=airport.Taxiways.At(edge.b); var delta=b-a;
                    if(delta.sqrMagnitude<.001f) continue;
                    var along=delta.normalized; var side=Geo.Right(along)*edge.width*.5f;
                    // Generous end caps include the junction mesh. This is only a connectivity rejection;
                    // every actual truck footprint still receives exact surface and obstacle validation.
                    a-=along*(edge.width*.5f); b+=along*(edge.width*.5f);
                    polygons.Add(new[]{a-side,a+side,b+side,b-side});
                }
                for(int i=0;i<polygons.Count;i++) adjacent.Add(new List<int>());
                for(int i=0;i<polygons.Count;i++) for(int j=0;j<i;j++)
                    if(Touch(polygons[i],polygons[j])) { adjacent[i].Add(j); adjacent[j].Add(i); }
            }
            static bool Contains(IList<Vector2> polygon,Vector2 point) => Geo.Inside(polygon,point) || Geo.DistanceToOutline(polygon,point)<.12f;
            static bool Touch(IList<Vector2> a,IList<Vector2> b)
            {
                var amin=a[0]; var amax=amin; var bmin=b[0]; var bmax=bmin;
                foreach(var p in a) { amin=Vector2.Min(amin,p); amax=Vector2.Max(amax,p); }
                foreach(var p in b) { bmin=Vector2.Min(bmin,p); bmax=Vector2.Max(bmax,p); }
                if(amax.x+.12f<bmin.x || bmax.x+.12f<amin.x || amax.y+.12f<bmin.y || bmax.y+.12f<amin.y) return false;
                foreach(var p in a) if(Contains(b,p)) return true;
                foreach(var p in b) if(Contains(a,p)) return true;
                for(int i=0;i<a.Count;i++) for(int j=0;j<b.Count;j++)
                    if(Geo.SegmentsMeet(a[i],a[(i+1)%a.Count],b[j],b[(j+1)%b.Count])) return true;
                return false;
            }
            public bool Connected(Vector2 from,Vector2 to)
            {
                var queue=new Queue<int>(); var seen=new bool[polygons.Count];
                for(int i=0;i<polygons.Count;i++) if(Contains(polygons[i],from)) { queue.Enqueue(i); seen[i]=true; }
                while(queue.Count>0)
                {
                    int i=queue.Dequeue(); if(Contains(polygons[i],to)) return true;
                    foreach(int next in adjacent[i]) if(!seen[next]) { seen[next]=true; queue.Enqueue(next); }
                }
                return false;
            }
        }
        static readonly ConditionalWeakTable<Airport,SurfaceGraph> surfaceGraphs = new ConditionalWeakTable<Airport,SurfaceGraph>();
        public static bool SurfaceConnected(Airport airport,Vector2 from,Vector2 to)
        {
            int stamp=airport.LayoutVersion;
            foreach(var edge in airport.Taxiways.edges) if(edge.IsRoad)
                stamp=unchecked(stamp*31+edge.id+(TaxiwayNetwork.Closed(edge)?1:0)+(edge.wear>=RunwayRules.ClosedWear?2:0));
            if(!surfaceGraphs.TryGetValue(airport,out var graph))
            { graph=new SurfaceGraph{stamp=int.MinValue}; surfaceGraphs.Add(airport,graph); }
            if(graph.stamp!=stamp) graph.Build(airport,stamp);
            return graph.Connected(from,to);
        }

        static VehiclePath Local(BaggageVehiclePose from, BaggageVehiclePose to, VehicleType type,
            Func<BaggageVehiclePose, VehicleType, bool> clear, bool search)
        {
            float speed = Mathf.Max(.5f, type.transitSpeed), radius = Radius(type);
            float cap=Vector2.Distance(from.position,to.position)+4f*Mathf.PI*radius+type.Length;
            bool Allowed(VehiclePath candidate) => candidate!=null && candidate.Length<=cap && Validate(candidate,type,clear);
            var path = new VehiclePath(); path.Start(from.position, speed);
            if (Vector2.Distance(from.position, to.position) < .04f && Vector2.Dot(from.facing, to.facing) > .999f) return path;
            path.TurnTo(from.facing, to.position, to.facing, radius, speed);
            VehiclePath best=Allowed(path)?path:null;
            float directDistance=Vector2.Distance(from.position,to.position);
            if(best!=null && best.Length<directDistance+radius*.65f) return best;
            var reversing=new VehiclePath(); reversing.Start(from.position,2.8f);
            reversing.TurnTo(-from.facing,to.position,-to.facing,radius,2.8f,true);
            if((best==null || reversing.Length<best.Length) && Allowed(reversing)) best=reversing;
            foreach(float lead in new[]{6f,12f,20f,32f,40f})
            {
                var staged=new VehiclePath(); staged.Start(from.position,speed);
                staged.TurnTo(from.facing,to.position+to.facing*lead,to.facing,radius,speed);
                staged.LineTo(to.position,2.8f,reverse:true);
                if((best==null || staged.Length<best.Length) && Allowed(staged)) best=staged;
                var forwardEntry=new VehiclePath(); forwardEntry.Start(from.position,speed);
                forwardEntry.TurnTo(from.facing,to.position-to.facing*lead,to.facing,radius,speed);
                forwardEntry.LineTo(to.position,speed);
                if((best==null || forwardEntry.Length<best.Length) && Allowed(forwardEntry)) best=forwardEntry;
                var reverseExit=new VehiclePath(); reverseExit.Start(from.position,2.8f);
                reverseExit.LineTo(from.position-from.facing*lead,2.8f,reverse:true);
                reverseExit.TurnTo(from.facing,to.position,to.facing,radius,speed);
                if((best==null || reverseExit.Length<best.Length) && Allowed(reverseExit)) best=reverseExit;
            }
            if(best!=null) return best;

            if (search)
            { var found = ApronPathfinder.Vehicle(from, to, type, clear, maximumExpansions: 128); return Allowed(found) ? found : null; }
            return null;
        }
        public static bool Validate(VehiclePath path, VehicleType type, Func<BaggageVehiclePose, VehicleType, bool> clear)
        {
            if (path == null) return false;
            for (float d = 0f; d < path.Length; d += .4f)
            {
                var p = path.Pose(d, out var f, out float curvature);
                if (Mathf.Abs(curvature) > Mathf.Tan(40f * Mathf.Deg2Rad) / Mathf.Max(.5f, type.wheelbase) + .015f) return false;
                if (!clear(new BaggageVehiclePose(p, f,path.Height(d,float.NaN)), type)) return false;
            }
            var end = path.Pose(path.Length, out var endFacing, out _);
            return clear(new BaggageVehiclePose(end, endFacing,path.Height(path.Length,float.NaN)), type);
        }
        static float Radius(VehicleType type) => Mathf.Max(2f, type.turnRadius, type.wheelbase / Mathf.Tan(38f * Mathf.Deg2Rad));

        /// <summary>Live solid-object guard used before each driven step, including newly moved storage tanks.</summary>
        public static bool StaticClear(Airport airport, VehicleType type, BaggageVehiclePose pose)
        {
            if(!float.IsNaN(pose.elevation)&&pose.elevation+type.bounds.max.y+1f<airport.PavementElevationAt(pose.position))return true;
            var box = Box.Of(pose.position, pose.facing, type.bounds, .25f);
            foreach (var solid in SolidBoxes(airport)) if (box.Overlaps(solid)) return false;
            foreach (var plane in Aircraft.Aircraft.All)
                if (plane != null && plane.type != null && AircraftNear(airport,plane,type,pose.position) && !AircraftServiceClearance.Clear(airport, plane, pose,
                    type.bounds, type, true, default, Vector3.zero, type.bounds.max.y)) return false;
            return true;
        }
        static float FootprintRadius(Bounds bounds) => new Vector2(bounds.center.x,bounds.center.z).magnitude+new Vector2(bounds.extents.x,bounds.extents.z).magnitude;
        static bool AircraftNear(Airport airport,Aircraft.Aircraft plane,VehicleType type,Vector2 point)
        {
            float radius=FootprintRadius(plane.type.bounds)+FootprintRadius(type.bounds)+1f;
            return (Geo.XZ(airport.transform.InverseTransformPoint(plane.transform.position))-point).sqrMagnitude<=radius*radius;
        }
        public static bool SurfaceClear(Airport airport,VehicleType type,BaggageVehiclePose pose) =>
            airport!=null && type!=null && new Clearance(airport,type,null).At(pose);
        public static bool PavementClear(Airport airport,VehicleType type,BaggageVehiclePose pose) =>
            airport!=null && type!=null && new Clearance(airport,type,null).Paved(pose);
        public static bool PathClear(Airport airport,VehicleType type,VehiclePath path,float from=0f)
        {
            if(airport==null || type==null || path==null) return false;
            var clearance=new Clearance(airport,type,null);
            for(float at=Mathf.Max(0f,from);at<path.Length;at+=.6f)
            {
                var point=path.Pose(at,out var facing,out _);
                if(!clearance.At(new BaggageVehiclePose(point,facing,path.Height(at,float.NaN)))) return false;
            }
            return clearance.At(new BaggageVehiclePose(path.End,path.EndFacing,path.Height(path.Length,float.NaN)));
        }
        public static string DescribeAircraftBlockage(Airport airport,VehicleType truck,BaggageVehiclePose pose)
        {
            foreach(var plane in Aircraft.Aircraft.All)
            {
                if(plane==null || plane.type==null || AircraftServiceClearance.Clear(airport,plane,pose,truck.bounds,truck,true,default,Vector3.zero,truck.bounds.max.y)) continue;
                var local=plane.transform.InverseTransformPoint(airport.transform.TransformPoint(Geo.X0Z(pose.position)));
                var facing=plane.transform.InverseTransformDirection(airport.transform.TransformDirection(Geo.X0Z(pose.facing)));
                var box=Box.Of(Geo.XZ(local),Geo.XZ(facing).normalized,truck.bounds,.2f);
                foreach(var part in plane.type.serviceFootprints)
                {
                    if((part.name=="Tail" || part.name=="Left wing" || part.name=="Right wing") && part.bottom>truck.bounds.max.y+.25f) continue;
                    if(AircraftServiceClearance.Overlaps(box,part.outline)) return plane.type.model+" "+part.name;
                }
                return plane.type.model+" conservative bounds";
            }
            return "no aircraft overlap";
        }
        static List<Box> SolidBoxes(Airport airport)
        {
            var boxes = new List<Box>();
            foreach (var hangar in airport.Hangars) boxes.Add(hangar.BuildingBox);
            foreach (var school in airport.FlyingSchools) boxes.Add(school.BuildingBox);
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            if (assets != null && assets.tankType != null)
                foreach (var area in airport.Deicing.WorkAreas)
                {
                    boxes.Add(Box.Of(area.TankPosition - area.Axis * 4.2f, area.Axis, assets.tankType.bounds, .3f));
                    if (area.recyclingInstalled && assets.recyclerType != null)
                        boxes.Add(Box.Of(area.RecyclerPosition, area.RecyclerFacing, assets.recyclerType.bounds, .3f));
                }
            foreach (var terminal in airport.terminals)
                foreach (var polygon in terminal.Footprints())
                {
                    if (polygon.Count == 0) continue;
                    var min=polygon[0]; var max=min;
                    foreach(var p in polygon) { min=Vector2.Min(min,p); max=Vector2.Max(max,p); }
                    boxes.Add(Box.Of((min+max)*.5f,Vector2.up,new Bounds(Vector3.zero,new Vector3(max.x-min.x,1,max.y-min.y)),.3f));
                }
            foreach (var depot in airport.Depots)
            {
                var corners=depot.Corners; var min=corners[0]; var max=min;
                foreach(var p in corners) { min=Vector2.Min(min,p); max=Vector2.Max(max,p); }
                boxes.Add(Box.Of((min+max)*.5f,Vector2.up,new Bounds(Vector3.zero,new Vector3(max.x-min.x,1,max.y-min.y)),.3f));
            }
            foreach (var depot in airport.GetComponentsInChildren<BaggageDepot>()) boxes.AddRange(depot.SolidBoxes());
            foreach (var rect in airport.Bridges.BaggageExclusions())
                boxes.Add(Box.Of(rect.center,Vector2.up,new Bounds(Vector3.zero,new Vector3(rect.width,1f,rect.height)),.3f));
            foreach (var runway in airport.Runways)
            {
                var a=Geo.XZ(runway.start); var b=Geo.XZ(runway.end);
                boxes.Add(Box.Of((a+b)*.5f,(b-a).normalized,new Bounds(Vector3.zero,new Vector3(runway.width,1f,(b-a).magnitude)),.3f));
            }
            return boxes;
        }
        static List<int> RoadNodes(TaxiwayNetwork net, Vector2 point)
        {
            var candidates = new List<int>();
            foreach (var node in net.nodes)
                if (node.runway == null && net.edges.Exists(e => e.road == RoadClass.Service &&
                    (e.a == node.id || e.b == node.id) && !TaxiwayNetwork.Closed(e))) candidates.Add(node.id);
            candidates.Sort((a, b) => (net.At(a) - point).sqrMagnitude.CompareTo((net.At(b) - point).sqrMagnitude));
            if (candidates.Count > 5) candidates.RemoveRange(5, candidates.Count - 5);
            return candidates;
        }

        sealed class Clearance
        {
            readonly Airport airport;
            readonly VehicleType type;
            readonly List<StandLayout> pads;
            List<Box> terminals;
            readonly Dictionary<(Vector2,Vector2,float), bool> cache = new Dictionary<(Vector2,Vector2,float), bool>();
            readonly List<(Aircraft.Aircraft aircraft,Vector2 origin,float radiusSquared)> planes=new List<(Aircraft.Aircraft,Vector2,float)>();
            readonly struct SurfaceStrip
            {
                public readonly Vector2 a,b;
                public readonly float halfWidthSquared,h0,h1;
                public readonly bool road,open;
                public SurfaceStrip(Vector3 a,Vector3 b,TaxiwayEdge edge)
                { this.a=Geo.XZ(a); this.b=Geo.XZ(b);h0=a.y;h1=b.y; halfWidthSquared=Mathf.Pow(Mathf.Max(0f,edge.width*.5f-.15f),2f); road=edge.IsRoad;
                    open=edge.road==RoadClass.Service && edge.surface.IsPaved() && !TaxiwayNetwork.Closed(edge) && edge.wear<RunwayRules.ClosedWear; }
            }
            const float SurfaceCell=32f;
            readonly List<SurfaceStrip> strips=new List<SurfaceStrip>();
            readonly Dictionary<Vector2Int,List<int>> surfaceCells=new Dictionary<Vector2Int,List<int>>();
            public Clearance(Airport airport, VehicleType type, Aircraft.Aircraft servicing)
            {
                this.airport = airport; this.type = type; pads = airport.Taxiways.Layouts();
                foreach(var plane in Aircraft.Aircraft.All) if(plane!=null && plane.type!=null)
                {
                    float radius=FootprintRadius(plane.type.bounds)+FootprintRadius(type.bounds)+1f;
                    planes.Add((plane,Geo.XZ(airport.transform.InverseTransformPoint(plane.transform.position)),radius*radius));
                }
                foreach(var edge in airport.Taxiways.edges)
                foreach(var span in airport.Taxiways.Path(edge).Spans())
                {
                    var a=Geo.XZ(span.a);var b=Geo.XZ(span.b);int index=strips.Count;strips.Add(new SurfaceStrip(span.a,span.b,edge));
                    var min=Vector2.Min(a,b)-Vector2.one*edge.width*.5f;var max=Vector2.Max(a,b)+Vector2.one*edge.width*.5f;
                    for(int x=Mathf.FloorToInt(min.x/SurfaceCell);x<=Mathf.FloorToInt(max.x/SurfaceCell);x++)
                    for(int z=Mathf.FloorToInt(min.y/SurfaceCell);z<=Mathf.FloorToInt(max.y/SurfaceCell);z++)
                    {var cell=new Vector2Int(x,z);if(!surfaceCells.TryGetValue(cell,out var entries))surfaceCells[cell]=entries=new List<int>();entries.Add(index);}
                }
            }
            public bool At(BaggageVehiclePose pose)
            {
                var key = (pose.position, pose.facing,pose.elevation);
                if (cache.TryGetValue(key, out bool valid)) return valid;
                valid = Check(pose); cache[key] = valid; return valid;
            }
            bool Check(BaggageVehiclePose pose)
            {
                var box = Box.Of(pose.position, pose.facing, type.bounds, .25f);
                terminals??=SolidBoxes(airport);
                bool underground=!float.IsNaN(pose.elevation)&&pose.elevation+type.bounds.max.y+1f<airport.PavementElevationAt(pose.position);
                if(!underground)foreach(var terminal in terminals)if(terminal.Overlaps(box))return false;
                foreach (var plane in planes)
                    if ((plane.origin-pose.position).sqrMagnitude<=plane.radiusSquared && !AircraftServiceClearance.Clear(airport, plane.aircraft, pose,
                        type.bounds, type, true, default, Vector3.zero, type.bounds.max.y)) return false;
                return Paved(pose);
            }
            public bool Paved(BaggageVehiclePose pose)
            {
                int nx = Mathf.Max(2, Mathf.CeilToInt(type.Width));
                int nz = Mathf.Max(2, Mathf.CeilToInt(type.Length));
                for (int x = 0; x <= nx; x++)
                    for (int z = 0; z <= nz; z++)
                    {
                        if (x != 0 && x != nx && z != 0 && z != nz) continue;
                        var point = pose.Point(new Vector3(Mathf.Lerp(type.bounds.min.x, type.bounds.max.x, (float)x / nx), 0f,
                            Mathf.Lerp(type.bounds.min.z, type.bounds.max.z, (float)z / nz)));
                        if (!OnSurface(point,pose.elevation)) return false;
                    }
                return true;
            }
            bool OnSurface(Vector2 point,float elevation)
            {
                bool road = false, taxiway = false;
                if(surfaceCells.TryGetValue(new Vector2Int(Mathf.FloorToInt(point.x/SurfaceCell),Mathf.FloorToInt(point.y/SurfaceCell)),out var nearby))
                foreach (int index in nearby)
                {
                    var strip=strips[index]; var near=Geo.ClosestOnSegment(point,strip.a,strip.b,out float t);
                    if((point-near).sqrMagnitude>strip.halfWidthSquared) continue;
                    float deck=float.IsNaN(elevation)?airport.PavementElevationAt(point):elevation;
                    if(Mathf.Abs(Mathf.Lerp(strip.h0,strip.h1,t)-deck)>type.Length*.22f+1f)continue;
                    if(!strip.road) { taxiway=true; continue; }
                    if(strip.open) road=true;
                }

                if (road) return true;
                if(!float.IsNaN(elevation)&&Mathf.Abs(elevation-airport.PavementElevationAt(point))>type.Length*.22f+1f)return false;
                foreach (var pad in pads) if (pad.stand.surface.IsPaved() && pad.Contains(point)) return true;
                if (taxiway) return false;
                foreach (var apron in airport.aprons) if (apron.surface.IsPaved() && apron.Contains(point)) return true;
                return false;
            }
        }
    }
}
