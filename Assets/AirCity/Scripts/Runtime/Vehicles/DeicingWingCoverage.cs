using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Aircraft-data-driven upper-wing coverage, including root, tip and the complete chord.</summary>
    public static class DeicingWingCoverage
    {
        public const int ChordStations = 3;
        public const float SwathSpacing = 1.25f;
        public static int SpanStations(AircraftType type)
        {
            var sections = type.deicingWing;
            float run = sections != null && sections.Length > 0 ? sections[sections.Length - 1].span - sections[0].span : type.Wingspan * .42f;
            return Mathf.Max(2, Mathf.CeilToInt(run / SwathSpacing) + 1);
        }
        public static List<Vector3> Create(AircraftType type, int side)
        {
            var result = new List<Vector3>();
            if (type == null) return result;
            int stations = SpanStations(type);
            var rear = new List<Vector3>();
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            int sign = side < 0 ? -1 : 1;
            for (int station = 0; station < stations; station++)
                for (int chord = 0; chord < ChordStations; chord++)
                {
                    float across = (float)station / (stations - 1);
                    float along = (station % 2 == 0 ? chord : ChordStations - chord - 1) / (float)(ChordStations - 1);
                    var point = Point(type, across, along, sign);
                    if (assets != null && assets.truckType != null && NeedsRear(type, assets.truckType, point, assets)) rear.Add(point);
                    else result.Add(point);
                }
            rear.Reverse(); result.AddRange(rear);
            return result;
        }
        public static Vector3 Point(AircraftType type, float spanFraction, float chordFraction, int side)
        {
            float sign = side < 0 ? -1f : 1f;
            var sections = type.deicingWing;
            Vector3 point;
            if (sections != null && sections.Length > 0)
            {
                float span = Mathf.Lerp(sections[0].span, sections[sections.Length - 1].span, Mathf.Clamp01(spanFraction));
                int next = 1;
                while (next < sections.Length && sections[next].span < span) next++;
                next = Mathf.Min(next, sections.Length - 1); int previous = Mathf.Max(0, next - 1);
                float t = Mathf.InverseLerp(sections[previous].span, sections[next].span, span);
                Vector3 Chord(AircraftType.DeicingWingSection section) => chordFraction <= .5f ?
                    Vector3.Lerp(section.leading, section.middle, chordFraction * 2f) :
                    Vector3.Lerp(section.middle, section.trailing, chordFraction * 2f - 1f);
                point = Vector3.Lerp(Chord(sections[previous]), Chord(sections[next]), t);
            }
            else
            {
                // Compatibility for old/custom aircraft before the upper-skin samples were exported.
                float root = type.Wingspan * .08f, tip = type.Wingspan * .5f;
                float x = Mathf.Lerp(root, tip, Mathf.Clamp01(spanFraction));
                Extents(type.wingOutline, -x, out float trailing, out float leading);
                point = new Vector3(x, Mathf.Max(.7f, type.Height * .25f), Mathf.Lerp(leading, trailing, chordFraction));
            }
            point.x = Mathf.Abs(point.x) * sign;
            return point + Vector3.up * .035f;
        }

        /// <summary>A tangent driving lane beside the leading or trailing edge, chosen within the mechanical spray range.</summary>
        public static BaggageVehiclePose WorkingPose(Airport airport, Aircraft.Aircraft aircraft, VehicleType truck, Vector3 localTarget)
        {
            var assets = Resources.Load<DeicingAssets>("DeicingAssets");
            bool rear=NeedsRear(aircraft.type,truck,localTarget,assets);
            var pose = LocalWorkingPose(aircraft.type, truck, localTarget, rear, false);
            if(!Usable(aircraft.type,truck,localTarget,pose,assets))
            {
                var alternative=LocalWorkingPose(aircraft.type,truck,localTarget,rear,true);
                if(Usable(aircraft.type,truck,localTarget,alternative,assets) || DeicingBoomKinematics.Solve(alternative,localTarget,assets).jetDistance<DeicingBoomKinematics.Solve(pose,localTarget,assets).jetDistance) pose=alternative;
            }
            var localPosition = new Vector3(pose.position.x,0f,pose.position.y);
            var localFacing = new Vector3(pose.facing.x,0f,pose.facing.y);
            var position = airport.transform.InverseTransformPoint(aircraft.transform.TransformPoint(localPosition));
            var facing = airport.transform.InverseTransformDirection(aircraft.transform.TransformDirection(localFacing));
            return new BaggageVehiclePose(Geo.XZ(position), Geo.XZ(facing));
        }
        public static bool TargetReachable(Airport airport, VehicleType truck, BaggageVehiclePose pose, Vector3 airportTarget) =>
            DeicingBoomKinematics.Solve(pose, airportTarget, Resources.Load<DeicingAssets>("DeicingAssets")).Reachable;
        static bool NeedsRear(AircraftType type, VehicleType truck, Vector3 target, DeicingAssets assets) =>
            // Keep the front pass facing inboard. Swapping its heading for one chord target creates a
            // full truck turn over a tiny distance; cover that target from the rear pass instead.
            !Usable(type,truck,target,LocalWorkingPose(type,truck,target,false),assets);
        static bool Usable(AircraftType type,VehicleType truck,Vector3 target,BaggageVehiclePose pose,DeicingAssets assets) =>
            DeicingBoomKinematics.Solve(pose,target,assets).Reachable && EntryClear(type,truck,pose);
        sealed class CachedPoses
        {
            public object wing, parts;
            public VehicleType truck;
            public Bounds bounds;
            public readonly Dictionary<(Vector3,bool,bool),BaggageVehiclePose> poses=new Dictionary<(Vector3,bool,bool),BaggageVehiclePose>();
            public readonly Dictionary<(Vector2,Vector2),bool> entries=new Dictionary<(Vector2,Vector2),bool>();
        }
        static readonly ConditionalWeakTable<AircraftType,CachedPoses> poseCaches=new ConditionalWeakTable<AircraftType,CachedPoses>();
        static BaggageVehiclePose LocalWorkingPose(AircraftType type, VehicleType truck, Vector3 target, bool rear, bool outboard=false)
        {
            if(!poseCaches.TryGetValue(type,out var cache)) { cache=new CachedPoses(); poseCaches.Add(type,cache); }
            if(!ReferenceEquals(cache.wing,type.deicingWing) || !ReferenceEquals(cache.parts,type.serviceFootprints) || cache.truck!=truck || cache.bounds!=type.bounds)
            { cache.poses.Clear(); cache.entries.Clear(); cache.wing=type.deicingWing; cache.parts=type.serviceFootprints; cache.truck=truck; cache.bounds=type.bounds; }
            var key=(target,rear,outboard); if(cache.poses.TryGetValue(key,out var saved)) return saved;
            float sign=target.x<0?-1f:1f;
            float fuselageHalf=type.Wingspan*.065f;
            foreach(var part in type.serviceFootprints) if(part.name=="Fuselage")
                foreach(var p in part.outline) fuselageHalf=Mathf.Max(fuselageHalf,Mathf.Abs(p.x));
            float rootX=Mathf.Max(Mathf.Abs(target.x)-1.2f,fuselageHalf+3f);
            Vector2 facing=Vector2.right;
            for(int iteration=0;iteration<3;iteration++)
            {
                float slope=(WingEdge(type,rootX+.2f,rear)-WingEdge(type,rootX-.2f,rear))/.4f;
                facing=new Vector2(1f,slope).normalized*(outboard?1f:-1f);
                var right=Geo.Right(facing); float minX=float.PositiveInfinity;
                foreach(float x in new[]{truck.bounds.min.x,truck.bounds.max.x}) foreach(float z in new[]{truck.bounds.min.z,truck.bounds.max.z})
                    minX=Mathf.Min(minX,right.x*x+facing.x*z);
                rootX=Mathf.Max(Mathf.Abs(target.x)-facing.x*1.2f,fuselageHalf+1f-minX);
            }
            var across=Geo.Right(facing);
            float rootZ=rear?float.PositiveInfinity:float.NegativeInfinity;
            int nx=Mathf.Max(2,Mathf.CeilToInt(truck.Width)),nz=Mathf.Max(2,Mathf.CeilToInt(truck.Length));
            for(int x=0;x<=nx;x++) for(int z=0;z<=nz;z++)
            {
                if(x!=0 && x!=nx && z!=0 && z!=nz) continue;
                var offset=across*Mathf.Lerp(truck.bounds.min.x,truck.bounds.max.x,(float)x/nx)+facing*Mathf.Lerp(truck.bounds.min.z,truck.bounds.max.z,(float)z/nz);
                float edge=BodyEdge(type,truck,rootX+offset.x,rear);
                float required=edge+(rear?-1f:1f)-offset.y;
                rootZ=rear?Mathf.Min(rootZ,required):Mathf.Max(rootZ,required);
            }
            var result=new BaggageVehiclePose(new Vector2(rootX*sign,rootZ),new Vector2(facing.x*sign,facing.y));
            // Perimeter samples estimate the lane, then the exact protected-polygon SAT closes small gaps
            // around propeller discs and engine corners that lie between those samples.
            for(int step=0;step<60 && !LocalBodyClear(type,truck,result);step++)
                result=new BaggageVehiclePose(result.position+Vector2.up*(rear?-.15f:.15f),result.facing);
            var assets=Resources.Load<DeicingAssets>("DeicingAssets");
            for(int step=0;step<24 && !EntryClear(type,truck,result);step++)
            {
                var next=new BaggageVehiclePose(result.position+Vector2.up*(rear?-.5f:.5f),result.facing);
                if(!DeicingBoomKinematics.Solve(next,target,assets).Reachable) break;
                result=next;
            }
            cache.poses[key]=result; return result;
        }
        static bool EntryClear(AircraftType type,VehicleType truck,BaggageVehiclePose pose)
        {
            var cache=poseCaches.GetValue(type,_=>new CachedPoses()); var key=(pose.position,pose.facing);
            if(cache.entries.TryGetValue(key,out bool valid)) return valid;
            bool outboard=pose.position.x*pose.facing.x>0f;
            valid=true;
            for(float distance=0;distance<=30f;distance+=2f)
            {
                var at=new BaggageVehiclePose(pose.position+pose.facing*(distance*(outboard?1f:-1f)),pose.facing);
                if(!LocalBodyClear(type,truck,at)) { valid=false; break; }
            }
            cache.entries[key]=valid; return valid;
        }
        static bool LocalBodyClear(AircraftType aircraft,VehicleType truck,BaggageVehiclePose pose)
        {
            var box=AirportMaintenance.Box.Of(pose.position,pose.facing,truck.bounds,.25f);
            foreach(var part in aircraft.serviceFootprints)
            {
                if((part.name=="Tail" || part.name=="Left wing" || part.name=="Right wing") && part.bottom>truck.bounds.max.y+.25f) continue;
                if(AircraftServiceClearance.Overlaps(box,part.outline)) return false;
            }
            return true;
        }
        static float WingEdge(AircraftType type,float span,bool rear)
        {
            var sections=type.deicingWing;
            if(sections==null || sections.Length==0) return type.bounds.center.z;
            int hi=1; while(hi<sections.Length && sections[hi].span<span) hi++;
            hi=Mathf.Min(hi,sections.Length-1); int lo=Mathf.Max(0,hi-1);
            var a=rear?sections[lo].trailing:sections[lo].leading; var b=rear?sections[hi].trailing:sections[hi].leading;
            return Mathf.Lerp(a.z,b.z,Mathf.InverseLerp(sections[lo].span,sections[hi].span,span));
        }
        static float BodyEdge(AircraftType type,VehicleType truck,float x,bool rear)
        {
            float edge=WingEdge(type,x,rear);
            foreach(var part in type.serviceFootprints)
            {
                if(part.bottom>truck.bounds.max.y+.3f) continue;
                for(int i=0;i<part.outline.Length;i++)
                {
                    var a=part.outline[i]; var b=part.outline[(i+1)%part.outline.Length];
                    if(x<Mathf.Min(a.x,b.x) || x>Mathf.Max(a.x,b.x)) continue;
                    float z=Mathf.Lerp(a.y,b.y,Mathf.InverseLerp(a.x,b.x,x));
                    edge=rear?Mathf.Min(edge,z):Mathf.Max(edge,z);
                }
            }
            return edge;
        }
        static void Extents(Vector2[] outline, float x, out float trailing, out float leading)
        {
            trailing = float.PositiveInfinity; leading = float.NegativeInfinity;
            for (int i = 0; i < (outline?.Length ?? 0); i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Length];
                if (x < Mathf.Min(a.x, b.x) || x > Mathf.Max(a.x, b.x)) continue;
                float t = Mathf.InverseLerp(a.x, b.x, x); float z = Mathf.Lerp(a.y, b.y, t);
                trailing = Mathf.Min(trailing, z); leading = Mathf.Max(leading, z);
            }
            if (float.IsInfinity(trailing)) { trailing = -1f; leading = 1f; }
        }
    }
}
