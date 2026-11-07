using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>Paint layouts derived from the same stops and equipment interfaces used by the simulation.</summary>
    public static class StandMarkings
    {
        public sealed class StopMark
        {
            public Vector2 position;
            public readonly List<AircraftType> aircraft = new List<AircraftType>();
            public string Label => string.Join(" / ", aircraft.Select(t => t.Designator));
        }

        public static List<StopMark> Stops(StandLayout layout, IEnumerable<AircraftType> types)
        {
            var marks = new List<StopMark>();
            if (layout.stand.DeIcing || !layout.stand.surface.IsPaved()) return marks;
            foreach (var type in AirportBridges.TypesFor(layout.stand, types).Distinct().OrderBy(t => t.SizeCode).ThenBy(t => t.seats))
            {
                if (!layout.ParkingFits(type)) continue;
                var p = layout.StopFor(type);
                // Only truly coincident stops share paint. Small but meaningful authored offsets keep their bars.
                var mark = marks.Find(m => Vector2.Distance(m.position, p) < .02f);
                if (mark == null) marks.Add(mark = new StopMark { position = p });
                mark.aircraft.Add(type);
            }
            return marks.OrderBy(m => Vector2.Dot(m.position, layout.nose)).ToList();
        }

        public sealed class EquipmentBay
        {
            public Vector2 centre, facing;
            public float width, length;
            public string equipment, stands;
            public bool working, storageArea;
            public float notchWidth, notchLength, notchSide=-1;
            public float Area => width*length-notchWidth*notchLength;
            public Vector2 Point(float x, float z) => centre + Geo.Right(facing) * x + facing * z;
            public bool Contains(Vector2 point)
            {
                var d = point - centre;
                float x=Vector2.Dot(d,Geo.Right(facing)),z=Vector2.Dot(d,facing);
                return Mathf.Abs(x)<=width*.5f && Mathf.Abs(z)<=length*.5f &&
                    !(notchWidth>0 && -notchSide*x < -width*.5f+notchWidth && z < -length*.5f+notchLength);
            }
            public IEnumerable<EquipmentBay> Rectangles()
            {
                if (notchWidth<=0) { yield return this; yield break; }
                yield return new EquipmentBay { centre=Point(-notchSide*notchWidth*.5f,0),facing=facing,
                    width=width-notchWidth,length=length };
                yield return new EquipmentBay { centre=Point(notchSide*(width-notchWidth)*.5f,notchLength*.5f),facing=facing,
                    width=notchWidth,length=length-notchLength };
            }
            public Vector2[] Boundary(float inset=0)
            {
                float x0=-width*.5f+inset,x1=width*.5f-inset,z0=-length*.5f+inset,z1=length*.5f-inset;
                if (notchWidth<=0) return new[] { Point(x0,z0),Point(x1,z0),Point(x1,z1),Point(x0,z1) };
                float cutX=-width*.5f+notchWidth+inset,cutZ=-length*.5f+notchLength+inset;
                Vector2 At(float x,float z) => Point(-notchSide*x,z);
                return new[] { At(cutX,z0),At(x1,z0),At(x1,z1),At(x0,z1),At(x0,cutZ),At(cutX,cutZ) };
            }
        }

        /// <summary>Road paint outside storage boxes, with room for the stroke and border. Road geometry stays put.</summary>
        public static List<(Vector2 a, Vector2 b)> RoadSegments(Vector2 a, Vector2 b, IList<EquipmentBay> bays, float margin)
        {
            var intervals = new List<Vector2> { new Vector2(0,1) };
            foreach (var bay in bays.SelectMany(b=>b.Rectangles()))
            {
                var d = a-bay.centre; var step = b-a; var right = Geo.Right(bay.facing);
                float lo = 0, hi = 1;
                bool Slab(float p, float v, float half)
                {
                    if (Mathf.Abs(v) < .00001f) return Mathf.Abs(p) <= half;
                    float t0 = (-half-p)/v, t1 = (half-p)/v;
                    lo = Mathf.Max(lo,Mathf.Min(t0,t1)); hi = Mathf.Min(hi,Mathf.Max(t0,t1));
                    return lo < hi;
                }
                if (!Slab(Vector2.Dot(d,right),Vector2.Dot(step,right),bay.width*.5f+margin) ||
                    !Slab(Vector2.Dot(d,bay.facing),Vector2.Dot(step,bay.facing),bay.length*.5f+margin)) continue;
                for (int i = intervals.Count-1; i >= 0; i--)
                {
                    var s = intervals[i];
                    if (hi <= s.x || lo >= s.y) continue;
                    intervals.RemoveAt(i);
                    if (lo > s.x) intervals.Insert(i,new Vector2(s.x,lo));
                    if (hi < s.y) intervals.Insert(i,new Vector2(hi,s.y));
                }
            }
            return intervals.OrderBy(s=>s.x).Select(s=>(Vector2.Lerp(a,b,s.x),Vector2.Lerp(a,b,s.y))).ToList();
        }

        public static List<EquipmentBay> Equipment(Airport airport)
        {
            var bays = new List<EquipmentBay>();
            SharedStorage(bays,airport);
            foreach (var station in airport.GetComponentsInChildren<BaggageStation>())
                if (station.isActiveAndEnabled)
                    Working(bays, airport, station.standIds, station.automaticHold.aircraft, station.automaticHold.cargoPoint,
                        station.tractorType, station.cartType, station.cartCount, station.loaderType, null, "BAGGAGE");
            foreach (var station in airport.GetComponentsInChildren<ContainerStation>())
                if (station.isActiveAndEnabled)
                    Working(bays, airport, station.standIds, station.automaticHold.aircraft, station.automaticHold.cargoPoint,
                        station.tractorType, station.dollyType, 1, station.loaderType, station.unitType, "ULD TRANSFER");
            foreach (var station in airport.GetComponentsInChildren<BaggageStation>())
            {
                if (!station.isActiveAndEnabled || station.parkAtStand) continue;
                string stands = StandLabels(airport, station.standIds);
                if (stands.Length == 0) continue;
                Add(bays, station.loaderType, station.loaderParking, station.loaderParkingBearing, "BELT", stands);
                Train(bays, airport.Taxiways, station.tractorType, station.cartType, station.cartCount,
                    station.dispatchNode, station.dispatchBearing, "BAG TRAIN", stands);
            }
            foreach (var station in airport.GetComponentsInChildren<ContainerStation>())
            {
                if (!station.isActiveAndEnabled || station.parkAtStand) continue;
                string stands = StandLabels(airport, station.standIds);
                if (stands.Length == 0) continue;
                Add(bays, station.loaderType, station.loaderParking, station.loaderParkingBearing, "ULD LOADER", stands);
                Train(bays, airport.Taxiways, station.tractorType, station.dollyType, 1,
                    station.dispatchNode, station.dispatchBearing, "ULD TRAIN", stands);
            }
            // No floating paint on grass or an unfinished apron. Check throughout long bays and their border.
            return bays.Where(b => Paved(airport, b)).ToList();
        }

        static string StandLabels(Airport airport, List<int> ids) => string.Join(" / ",
            airport.Taxiways.stands.Where(s => !s.DeIcing && ids.Contains(s.id)).OrderBy(s => s.number).Select(s => s.Label));

        static void SharedStorage(List<EquipmentBay> bays,Airport airport)
        {
            var parking=StandEquipmentLayout.All(airport).Where(p=>p.Valid).ToList();
            if (parking.Count==0) return;
            var exclusions=StandEquipmentLayout.StorageExclusions(airport);
            foreach (var group in parking.GroupBy(p=>p.stand).OrderBy(g=>g.Key.number))
            {
                var layout=airport.Taxiways.Layout(group.Key);
                var items=group.SelectMany(p=>new[] { p.loaderBay,p.trainBay }).ToArray();
                var right=Geo.Right(layout.nose);
                float side=Mathf.Sign(Vector2.Dot(items[0].centre-layout.entry,right));
                var outward=right*side;
                float minU=float.MaxValue,maxU=float.MinValue,minV=float.MaxValue,maxV=float.MinValue;
                // Parking envelopes already include vehicle clearance. Keep a 25 cm paint inset so
                // enclosing their empty corners does not extend into the adjacent aircraft envelope.
                foreach (var item in items)
                    foreach (float x in new[] { -item.width*.5f+.25f,item.width*.5f-.25f })
                        foreach (float z in new[] { -item.length*.5f+.25f,item.length*.5f-.25f })
                        {
                            var d=item.Point(x,z)-layout.entry;
                            float u=Vector2.Dot(d,outward),v=Vector2.Dot(d,layout.nose);
                            minU=Mathf.Min(minU,u); maxU=Mathf.Max(maxU,u);
                            minV=Mathf.Min(minV,v); maxV=Mathf.Max(maxV,v);
                        }
                float occupied=items.Sum(b=>b.width*b.length);
                var otherEquipment=parking.Where(p=>p.stand!=group.Key).SelectMany(p=>new[] {
                    StandEquipmentLayout.BayBox(p.loaderBay,.5f),StandEquipmentLayout.BayBox(p.trainBay,.5f) }).ToList();
                // Shared courts fit the equipment group rather than reserving a large stand-sized yard.
                // A modest edge allowance scales with the group, and shrinks on constrained aprons.
                float reserve=Mathf.Clamp(Mathf.Sqrt(occupied)*.12f,1f,1.5f);
                foreach (float growth in new[] { 1f,.75f,.5f,.25f,0f })
                {
                    float u0=minU,u1=maxU+reserve*growth;
                    float v0=minV-.5f*growth,v1=maxV+reserve*growth;
                    float width=u1-u0,length=v1-v0;
                    if (width<6f || length<6f || width*length<occupied*1.5f) continue;
                    var area=new EquipmentBay { centre=layout.entry+outward*((u0+u1)*.5f)+layout.nose*((v0+v1)*.5f),
                        facing=layout.nose,width=width,length=length,equipment="EQUIPMENT",stands=group.Key.Label,storageArea=true,
                        notchSide=-side };
                    var box=StandEquipmentLayout.BayBox(area,.15f);
                    // A forward train and a loader farther aft can wrap around the nose clearance.
                    // Leave that inboard/aft corner out of the common court instead of moving equipment.
                    foreach (var obstacle in exclusions.Where(q=>q.Overlaps(box)))
                    {
                        float a=float.MaxValue,b=float.MinValue,c=float.MaxValue,d=float.MinValue;
                        foreach (float x in new[] { -obstacle.hx,obstacle.hx })
                            foreach (float z in new[] { -obstacle.hz,obstacle.hz })
                            {
                                var p=obstacle.c+obstacle.ax*x+obstacle.az*z-area.centre;
                                float u=Vector2.Dot(p,outward),v=Vector2.Dot(p,layout.nose);
                                a=Mathf.Min(a,u); b=Mathf.Max(b,u); c=Mathf.Min(c,v); d=Mathf.Max(d,v);
                            }
                        if (a<=-width*.5f && c<=-length*.5f && b<width*.5f-.4f && d<length*.5f-.4f)
                        {
                            area.notchWidth=Mathf.Max(area.notchWidth,b+width*.5f+.2f);
                            area.notchLength=Mathf.Max(area.notchLength,d+length*.5f+.2f);
                        }
                    }
                    var sections=area.Rectangles().Select(r=>StandEquipmentLayout.BayBox(r,.15f)).ToArray();
                    if (area.Area<occupied*1.5f || sections.Any(s=>exclusions.Any(q=>q.Overlaps(s)) || otherEquipment.Any(q=>q.Overlaps(s))) ||
                        bays.SelectMany(b=>b.Rectangles()).Any(b=>sections.Any(s=>StandEquipmentLayout.BayBox(b,.5f).Overlaps(s))) ||
                        items.Any(item=>item.Boundary(.251f).Any(p=>!area.Contains(p))) || !Paved(airport,area)) continue;
                    bays.Add(area); break;
                }
            }
        }

        static void Working(List<EquipmentBay> bays, Airport airport, List<int> ids, AircraftType type, string point,
            VehicleType tractor, VehicleType cart, int count, VehicleType loader, VehicleType unit, string label)
        {
            if (type == null || tractor?.baggage == null || cart?.baggage == null || loader == null) return;
            var cargo = type.PointsOfType(ServicePointType.CargoDoor).FirstOrDefault(p => p.name == point);
            if (cargo.name == null) return;
            foreach (var stand in airport.Taxiways.stands.Where(s => ids.Contains(s.id) && !s.DeIcing))
            {
                var layout = airport.Taxiways.Layout(stand);
                if (layout == null || !layout.ParkingFits(type)) continue;
                var work = BaggageWorkLayout.For(layout,type,cargo,tractor,cart,loader,unit);
                var bounds = StandEquipmentLayout.TrainBounds(tractor,cart,count);
                // Articulated carts can remain fanned after the cargo approach. Include their swing in the working area.
                float swing = Mathf.Max(0,count-1)*(cart.baggage.frontHitch.z-cart.baggage.rearHitch.z);
                bays.Add(new EquipmentBay { centre = work.train.Point(bounds.center), facing = work.train.facing,
                    width = bounds.size.x + 1f + 2f*swing, length = bounds.size.z + 2f, equipment = label,
                    stands = stand.Label, working = true });
            }
        }

        static void Add(List<EquipmentBay> bays, VehicleType type, Vector2 at, float bearing, string label, string stands)
        {
            if (type == null) return;
            var pose = new BaggageVehiclePose(at, BaggageServicePlan.Facing(bearing));
            bays.Add(new EquipmentBay { centre = pose.Point(type.bounds.center), facing = pose.facing,
                width = type.bounds.size.x + 1.5f, length = type.bounds.size.z + 2f, equipment = label, stands = stands });
        }

        static void Train(List<EquipmentBay> bays, TaxiwayNetwork net, VehicleType tractor, VehicleType cart,
            int count, int node, float bearing, string label, string stands)
        {
            if (tractor == null || cart == null || tractor.baggage == null || cart.baggage == null || net.Node(node) == null) return;
            var bounds = tractor.bounds;
            Vector3 hitch = tractor.baggage.rearHitch;
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                var origin = hitch - cart.baggage.frontHitch;
                bounds.Encapsulate(origin + cart.bounds.min); bounds.Encapsulate(origin + cart.bounds.max);
                hitch = origin + cart.baggage.rearHitch;
            }
            var pose = new BaggageVehiclePose(net.At(node), BaggageServicePlan.Facing(bearing));
            bays.Add(new EquipmentBay { centre = pose.Point(bounds.center), facing = pose.facing,
                width = bounds.size.x + 1.5f, length = bounds.size.z + 2f, equipment = label, stands = stands });
        }

        static bool Paved(Airport airport, EquipmentBay bay)
        {
            var layouts = airport.Taxiways.Layouts();
            foreach (var section in bay.Rectangles())
            {
                int nx = Mathf.CeilToInt(section.width), nz = Mathf.CeilToInt(section.length);
                for (int x = 0; x <= nx; x++)
                    for (int z = 0; z <= nz; z++)
                    {
                        var p = section.Point(Mathf.Lerp(-section.width*.5f, section.width*.5f, (float)x/nx),
                            Mathf.Lerp(-section.length*.5f, section.length*.5f, (float)z/nz));
                        if (!airport.aprons.Any(a => a.surface.IsPaved() && a.Contains(p)) &&
                            !layouts.Any(l => l.stand.surface.IsPaved() && l.Contains(p))) return false;
                    }
            }
            return true;
        }
    }
}
