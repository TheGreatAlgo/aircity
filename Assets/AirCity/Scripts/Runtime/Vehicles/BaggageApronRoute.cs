using System;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Forward-only connections between stand staging, the working train and the service-road endpoints.</summary>
    static class BaggageApronRoute
    {
        public static VehiclePath Connect(BaggageVehiclePose from,BaggageVehiclePose to,BaggageServiceAccess equipment,
            Vector2 outward,Vector2 aircraftForward,Func<BaggageVehiclePose,VehicleType,bool> clear,BaggageTrain seed=null,float? endLead=null,Func<string> detail=null)
        {
            float radius=Mathf.Max(equipment.tractorType.turnRadius,equipment.cartType.turnRadius,6f);
            float speed=Mathf.Min(equipment.tractorType.transitSpeed,equipment.cartType.transitSpeed);
            float lead=endLead ?? StandEquipmentLayout.TrainBounds(equipment.tractorType,equipment.cartType,equipment.cartCount).size.z+3f;
            if (lead>=1f)
            {
                var targetPath=new VehiclePath(); targetPath.Start(to.position,1); targetPath.LineTo(to.position+to.facing,1);
                var target=new BaggageTrain(equipment.tractorType,equipment.cartType,targetPath,equipment.cartCount);
                bool valid=clear(target.Tractor,equipment.tractorType);
                for (int i=0;i<target.CartCount && valid;i++) valid=clear(target.CartAt(i),equipment.cartType);
                if (!valid) throw new ArgumentException("Train parking target is blocked at "+to.position+"; "+detail?.Invoke());
            }
            VehiclePath Route(Vector2? via,Vector2 heading,float departureLead=2f,Vector2? entry=null,float departureTurn=0,float? arrivalLead=null)
            {
                var p=new VehiclePath(); p.Start(from.position,speed); p.LineTo(from.position+from.facing*departureLead,speed);
                var f=from.facing;
                if (Mathf.Abs(departureTurn)>.001f)
                {
                    var left=new Vector2(-f.y,f.x);
                    p.ArcAround(p.End+left*(radius*Mathf.Sign(departureTurn)),departureTurn,speed);
                    f=p.EndFacing;
                }
                if (via.HasValue)
                {
                    p.TurnTo(f,via.Value,heading,radius,speed); p.LineTo(via.Value+heading*2f,speed); f=heading;
                }
                if (entry.HasValue)
                {
                    var h=entry.Value; float sign=h.x*to.facing.y-h.y*to.facing.x > 0 ? 1f : -1f;
                    Vector2 Side(Vector2 v)=>sign > 0 ? new Vector2(-v.y,v.x) : new Vector2(v.y,-v.x);
                    var end=to.position-to.facing*.1f;
                    var centre=end+Side(to.facing)*radius; var start=centre-Side(h)*radius;
                    p.TurnTo(f,start-h*2f,h,radius,speed); p.LineTo(start,speed);
                    p.ArcAround(centre,sign*Mathf.PI*.5f,speed); p.LineTo(to.position,speed);
                }
                else { p.TurnTo(f,to.position-to.facing*(arrivalLead ?? lead),to.facing,radius,speed); p.LineTo(to.position,speed); }
                return p;
            }
            string reason=null; string blocked=null; float nearest=float.MaxValue;
            bool Valid(VehiclePath route)
            {
                // Reject an obstructed tractor curve before simulating every trailer at 5 cm intervals.
                for (float d=0;d<=route.Length;d+=1f)
                {
                    var at=route.Pose(d,out var f,out _); var pose=new BaggageVehiclePose(at,f);
                    if (clear(pose,equipment.tractorType)) continue;
                    reason="baggage tractor access is blocked";
                    float distance=Vector2.Distance(at,to.position);
                    if (distance<nearest) { nearest=distance; blocked="tractor at "+at+"; "+detail?.Invoke(); }
                    return false;
                }
                var train=seed == null ? new BaggageTrain(equipment.tractorType,equipment.cartType,route,equipment.cartCount) : seed.Preview(route);
                return BaggageRoute.Validate(train,(q,type)=>
                {
                    bool ok=clear(q,type);
                    float distance=Vector2.Distance(train.Tractor.position,to.position);
                    if (!ok && distance<nearest)
                    { nearest=distance; blocked=type.kind+" at "+q.position+" facing "+q.facing+"; "+detail?.Invoke(); }
                    return ok;
                },out reason);
            }
            foreach (float departureLead in new[] { 2f,6f,12f,18f })
            {
            if (lead < 1f)
                foreach (float departureTurn in new[] { 0f,-Mathf.PI,Mathf.PI })
                foreach (var entry in new[] { new Vector2(to.facing.y,-to.facing.x),new Vector2(-to.facing.y,to.facing.x) })
                {
                    var arc=Route(null,from.facing,departureLead,entry,departureTurn); if (Valid(arc)) return arc;
                    foreach (float side in new[] { radius*2f,radius*4f,radius*6f })
                        foreach (float along in new[] { 0f,lead+radius*2f,-radius*2f })
                            foreach (var heading in new[] { aircraftForward,-aircraftForward,outward,-outward })
                            {
                                var via=(from.position+to.position)*.5f+outward*side+aircraftForward*along;
                                var route=Route(via,heading,departureLead,entry,departureTurn); if (Valid(route)) return route;
                            }
                }
            var direct=Route(null,from.facing,departureLead); if (Valid(direct)) return direct;
            if (lead>=1f)
                foreach (float extra in new[] { radius*2f,radius*4f,radius*6f })
                {
                    var longer=Route(null,from.facing,departureLead,arrivalLead:lead+extra);
                    if (Valid(longer)) return longer;
                }
            foreach (float side in new[] { radius*2f,radius*3f,radius*5f,radius*7f })
                foreach (float along in new[] { 0f,lead,-lead,lead*2f,-lead*2f })
                    foreach (var heading in new[] { aircraftForward,-aircraftForward,outward,-outward })
                    {
                        var via=(from.position+to.position)*.5f+outward*side+aircraftForward*along;
                        var route=Route(via,heading,departureLead); if (Valid(route)) return route;
                    }
            }
            var searched=ApronPathfinder.Train(from,to,equipment,clear,seed);
            if (searched != null) return searched;
            throw new ArgumentException("No clear forward route from "+from.position+" ("+from.facing+") to "+to.position+" ("+to.facing+"): "+reason+"; "+blocked);
        }
    }
}
