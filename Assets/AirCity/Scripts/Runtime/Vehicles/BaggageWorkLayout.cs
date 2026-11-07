using AirCity.Aircraft;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>The same generated cargo-door geometry drives operating poses, parking exclusions and paint.</summary>
    public sealed class BaggageWorkLayout
    {
        public Vector3 door,inward,tip;
        public float clearance,serviceHeight;
        public BaggageVehiclePose loader,stage,train;

        public static BaggageWorkLayout For(StandLayout stand,AircraftType type,AircraftType.ServicePoint cargo,
            VehicleType tractor,VehicleType cart,VehicleType loader,VehicleType unit)
        {
            var at=stand.ParkPosition(type); var f=stand.nose; var r=new Vector2(f.y,-f.x);
            var p=at+r*cargo.position.x+f*cargo.position.z;
            var n=(r*cargo.normal.x+f*cargo.normal.z).normalized;
            return At(type,new Vector3(p.x,cargo.position.y,p.y),n,f,at,cargo,tractor,cart,loader,unit);
        }

        public static BaggageWorkLayout At(AircraftType type,Vector3 door,Vector2 normal,Vector2 forward,Vector2 position,
            AircraftType.ServicePoint cargo,VehicleType tractorType,VehicleType cartType,VehicleType loaderType,VehicleType unitType)
        {
            var w=new BaggageWorkLayout { door=door,inward=new Vector3(-normal.x,0,-normal.y),
                serviceHeight=door.y+(unitType != null ? unitType.bounds.max.y : .85f) };
            var right=new Vector2(forward.y,-forward.x);
            foreach (float x in new[] { type.bounds.min.x,type.bounds.max.x })
                foreach (float z in new[] { type.bounds.min.z,type.bounds.max.z })
                    w.clearance=Mathf.Max(w.clearance,Vector2.Dot(position+right*x+forward*z-BaggageServicePlan.XZ(door),normal));
            var front=BaggageServicePlan.XZ(door)+normal*.1f; var facing=-normal;
            var across=new Vector2(facing.y,-facing.x); Vector2 rear;
            if (loaderType.kind==VehicleKind.ContainerLoader)
            {
                var tip=loaderType.container.frontTransfer; tip.z=loaderType.bounds.max.z;
                if (unitType != null) tip.x+=unitType.bounds.center.x;
                w.tip=tip; w.loader=new BaggageVehiclePose(front-facing*tip.z-across*tip.x,facing);
                rear=w.loader.Point(loaderType.container.mainTransfer);
            }
            else
            {
                var b=loaderType.baggage;
                float run=Mathf.Sqrt(b.beltLength*b.beltLength-Mathf.Pow(door.y-b.beltRear.y,2));
                w.tip=b.beltRear+new Vector3(0,door.y-b.beltRear.y,run);
                rear=front-facing*run;
                w.loader=new BaggageVehiclePose(rear-facing*b.beltRear.z-across*b.beltRear.x,facing);
            }
            w.stage=new BaggageVehiclePose(w.loader.position-facing*Mathf.Max(6,w.clearance+4),facing);
            var cart=cartType.baggage;
            float offset=-tractorType.baggage.rearHitch.z+cart.frontHitch.z-(cart.loadMin.z+cart.loadMax.z)*.5f;
            var trainFacing=loaderType.kind==VehicleKind.ContainerLoader ? -across : forward*(cargo.position.z>=type.bounds.center.z ? 1f : -1f);
            w.train=loaderType.kind==VehicleKind.ContainerLoader ?
                new BaggageVehiclePose(rear-across*(loaderType.container.deckWidth*.5f+cartType.Length*.5f+1.4f)+trainFacing*offset,trainFacing) :
                new BaggageVehiclePose(rear+normal*3.5f+trainFacing*offset,trainFacing);
            return w;
        }

        public AirportMaintenance.Box LoaderLane(VehicleType type)
        {
            var bounds=type.bounds; bounds.Expand(new Vector3(0,0,Vector2.Distance(loader.position,stage.position)));
            return AirportMaintenance.Box.Of((loader.position+stage.position)*.5f,loader.facing,bounds,.4f);
        }
    }
}
