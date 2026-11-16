using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>The shared mechanical solution used for both fluid eligibility and the articulated model.</summary>
    public readonly struct DeicingBoomPose
    {
        public readonly float yaw, shoulder, elbow, extension, jetDistance;
        public readonly Vector3 cabin, nozzle;
        public readonly Vector2 cabinFacing;
        public bool Reachable => jetDistance <= DeicingBoomKinematics.MaxJetDistance + .001f;
        public DeicingBoomPose(float yaw, float shoulder, float elbow, float extension, Vector3 cabin,
            Vector3 nozzle, Vector2 facing, float distance)
        { this.yaw=yaw; this.shoulder=shoulder; this.elbow=elbow; this.extension=extension; this.cabin=cabin; this.nozzle=nozzle; cabinFacing=facing; jetDistance=distance; }
    }
    public static class DeicingBoomKinematics
    {
        public const float MaxJetDistance = 8f;
        public static DeicingBoomPose Solve(BaggageVehiclePose truck, Vector3 target, DeicingAssets assets)
        {
            float lower=assets!=null?assets.boomLowerLength:4.04475f, upperRest=assets!=null?assets.boomUpperLength:3.4f;
            float maxExtension=assets!=null?assets.boomExtension:3f, restAngle=assets!=null?assets.boomLowerRestAngle:8.530766f;
            var pivot=assets!=null?assets.boomLiftPivot:new Vector3(0,2.8f,1.2f);
            var offset=assets!=null?assets.cabinNozzleOffset:new Vector3(0,.85f,.85f);
            Vector3 ToWorld(Vector3 p) => new Vector3(truck.Point(p).x,p.y,truck.Point(p).y);
            Vector3 ToLocal(Vector3 p)
            { var d=new Vector2(p.x,p.z)-truck.position; return new Vector3(Vector2.Dot(d,truck.Right),p.y,Vector2.Dot(d,truck.facing)); }
            var near=new Vector3(truck.position.x-target.x,0,truck.position.y-target.z).normalized;
            var desired=target+Vector3.up*2.4f+near*1.2f;
            var delta=ToLocal(desired)-pivot;
            float horizontal=new Vector2(delta.x,delta.z).magnitude;
            float extension=Mathf.Clamp(delta.magnitude-lower-upperRest+.25f,0,maxExtension), upper=upperRest+extension;
            float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(lower-upper)+.05f,lower+upper-.02f);
            float bearing=Mathf.Atan2(delta.x,delta.z),line=Mathf.Atan2(delta.y,horizontal);
            float shoulder=line+Mathf.Acos(Mathf.Clamp((lower*lower+distance*distance-upper*upper)/(2f*lower*distance),-1,1));
            float forearm=Mathf.Atan2(Mathf.Sin(line)*distance-lower*Mathf.Sin(shoulder),Mathf.Cos(line)*distance-lower*Mathf.Cos(shoulder));
            var arm=new Vector3(0,lower*Mathf.Sin(shoulder)+upper*Mathf.Sin(forearm),lower*Mathf.Cos(shoulder)+upper*Mathf.Cos(forearm));
            var cabin=ToWorld(pivot+Quaternion.Euler(0,bearing*Mathf.Rad2Deg,0)*arm);
            var facing=new Vector2(target.x-cabin.x,target.z-cabin.z).normalized;
            if(facing.sqrMagnitude<.001f) facing=truck.facing;
            var right=new Vector2(facing.y,-facing.x);
            var nozzle=cabin+new Vector3(right.x*offset.x+facing.x*offset.z,offset.y,right.y*offset.x+facing.y*offset.z);
            var aim=target-nozzle; nozzle+=aim.normalized*.47f;
            float shoulderDelta=shoulder*Mathf.Rad2Deg-restAngle;
            return new DeicingBoomPose(bearing*Mathf.Rad2Deg,shoulderDelta,forearm*Mathf.Rad2Deg-shoulderDelta,
                extension,cabin,nozzle,facing,Vector3.Distance(nozzle,target));
        }
    }
}
