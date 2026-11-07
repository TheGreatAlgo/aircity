using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AirCity.Aircraft;
using AirCity.Airfield;
using UnityEngine;
using Box = AirCity.Vehicles.AirportMaintenance.Box;

namespace AirCity.Vehicles
{
    /// <summary>Idle equipment belongs to its stand. Positions follow its frame and generated aircraft clearances.</summary>
    public static class StandEquipmentLayout
    {
        sealed class Cached
        {
            public int stamp;
            public List<Parking> parking;
        }
        static readonly ConditionalWeakTable<Airport,Cached> cache = new ConditionalWeakTable<Airport,Cached>();

        static int Stamp(Airport airport,List<Request> requests)
        {
            unchecked
            {
                int h=airport.LayoutVersion;
                void Add(int n) => h=h*31+n;
                int Id(Object o) => o != null ? o.GetEntityId().GetHashCode() : 0;
                foreach (var t in airport.aircraftTypes) Add(Id(t));
                foreach (var s in airport.Taxiways.stands)
                {
                    Add(s.id); Add(s.position.GetHashCode()); Add(s.heading.GetHashCode()); Add(s.front.GetHashCode());
                    Add(s.lane.GetHashCode()); Add((int)s.code); Add((int)s.surface); Add((int)s.exit); Add((int)s.kind); Add(s.noSmallAircraft ? 1 : 0);
                    foreach (var c in s.serviceConfigurations) { Add(Id(c.aircraft)); Add(c.stopOffset.GetHashCode()); Add(c.allowed ? 1 : 0); }
                }
                foreach (var r in requests)
                {
                    Add(Id(r.station)); Add(r.stand.id); Add(r.count); Add(Id(r.aircraft)); Add(r.cargo?.GetHashCode() ?? 0);
                    Add(Id(r.tractor)); Add(Id(r.cart)); Add(Id(r.loader)); Add(Id(r.unit));
                }
                return h;
            }
        }
        public sealed class Parking
        {
            public Component station;
            public Stand stand;
            public BaggageVehiclePose loader, train;
            public StandMarkings.EquipmentBay loaderBay, trainBay;
            public string problem;
            public bool Valid => problem == null && loaderBay != null && trainBay != null;
        }

        sealed class Request
        {
            public Component station;
            public Stand stand;
            public VehicleType loader, tractor, cart, unit;
            public AircraftType aircraft;
            public string cargo;
            public int count;
        }

        sealed class Candidate
        {
            public int id;
            public bool loader;
            public StandMarkings.EquipmentBay bay;
            public BaggageVehiclePose pose;
            public Box box, parked, close, entrance, exit;
        }
        sealed class Slot
        {
            public Parking parking;
            public bool loader;
            public float length;
            public readonly List<Candidate> candidates = new List<Candidate>();
        }

        public static Parking For(BaggageStation station) => !station.parkAtStand ? null :
            All(station.Airport).Find(p=>p.station == station) ?? new Parking { station = station, problem = "Assign a stand for equipment staging." };
        public static Parking For(ContainerStation station) => !station.parkAtStand ? null :
            All(station.Airport).Find(p=>p.station == station) ?? new Parking { station = station, problem = "Assign a stand for equipment staging." };

        public static Stand Home(Airport airport,List<int> ids,int selected) =>
            airport.Taxiways.stands.FirstOrDefault(s=>s.id==selected && ids.Contains(s.id)) ??
            airport.Taxiways.stands.Where(s=>ids.Contains(s.id)).OrderBy(s=>s.number).FirstOrDefault();

        public static List<Parking> All(Airport airport)
        {
            var result = new List<Parking>();
            if (airport == null) return result;
            var requests = new List<Request>();
            Stand Anchor(List<int> ids,int selected) => Home(airport,ids,selected);
            // Stable hierarchy order: container equipment, then its bulk companions. Shared stands pack jointly.
            foreach (var s in airport.GetComponentsInChildren<ContainerStation>())
                if (s.isActiveAndEnabled && s.parkAtStand && Anchor(s.standIds,s.parkingStandId) is Stand stand)
                    requests.Add(new Request { station=s,stand=stand,loader=s.loaderType,tractor=s.tractorType,cart=s.dollyType,unit=s.unitType,
                        aircraft=s.automaticHold.aircraft,cargo=s.automaticHold.cargoPoint,count=1 });
            foreach (var s in airport.GetComponentsInChildren<BaggageStation>())
                if (s.isActiveAndEnabled && s.parkAtStand && Anchor(s.standIds,s.parkingStandId) is Stand stand)
                    requests.Add(new Request { station=s,stand=stand,loader=s.loaderType,tractor=s.tractorType,cart=s.cartType,
                        aircraft=s.automaticHold.aircraft,cargo=s.automaticHold.cargoPoint,count=Mathf.Max(1,s.cartCount) });
            int stamp=Stamp(airport,requests);
            if (cache.TryGetValue(airport,out var previous) && previous.stamp==stamp) return previous.parking;
            foreach (var group in requests.GroupBy(r=>r.stand))
            {
                var layout = airport.Taxiways.Layout(group.Key);
                var batch = new List<Parking>();
                var obstacles = Fixed(airport);
                var types = airport.Bridges.TypesFor(group.Key);
                var guarded = layout == null ? null : Swept(layout, types);
                if (guarded != null) obstacles.AddRange(guarded);
                var workspaces=new List<(Component station,Box loader,Box train)>();
                foreach (var r in group)
                {
                    r.aircraft ??=types.OrderByDescending(t=>t.Wingspan).FirstOrDefault();
                    if (layout==null || r.aircraft==null || r.loader==null || r.tractor?.baggage==null || r.cart?.baggage==null) continue;
                    var cargo=r.aircraft.PointsOfType(ServicePointType.CargoDoor).FirstOrDefault(p=>p.name==r.cargo);
                    if (cargo.name==null) continue;
                    var working=BaggageWorkLayout.For(layout,r.aircraft,cargo,r.tractor,r.cart,r.loader,r.unit);
                    workspaces.Add((r.station,working.LoaderLane(r.loader),
                        Box.Of(working.train.position,working.train.facing,TrainBounds(r.tractor,r.cart,r.count),.4f)));
                }
                var slots = new List<Slot>(); int candidateId=0;
                foreach (var request in group)
                {
                    request.aircraft ??= types.OrderByDescending(t => t.Wingspan).FirstOrDefault();
                    var parking = new Parking { station = request.station, stand = request.stand }; batch.Add(parking);
                    if (layout == null || layout.stand.DeIcing || layout.TaxiOut || !layout.stand.surface.IsPaved())
                    { parking.problem = "Equipment staging needs a paved push-back stand."; continue; }
                    if (request.loader == null || request.tractor?.baggage == null || request.cart?.baggage == null || request.aircraft == null)
                    { parking.problem = "Choose the equipment and aircraft for stand staging."; continue; }
                    var cargo = request.aircraft.PointsOfType(ServicePointType.CargoDoor).FirstOrDefault(q => q.name == request.cargo);
                    if (cargo.name == null) { parking.problem = "Choose a cargo door for stand staging."; continue; }
                    float side = Mathf.Abs(cargo.normal.x) < .1f ? 1f : Mathf.Sign(cargo.normal.x);
                    void Generate(Bounds bounds, string label, bool loader)
                    {
                        var slot = new Slot { parking = parking, loader = loader, length = bounds.size.z }; slots.Add(slot);
                        float width = bounds.size.x + 1f, length = bounds.size.z + 2f;
                        var facings = loader ? new[] { GeoRight(layout.nose) * side } : new[] { -layout.nose,-GeoRight(layout.nose) * side };
                        foreach (var facing in facings)
                        {
                            bool lateral = Mathf.Abs(Vector2.Dot(facing, layout.nose)) < .5f;
                            float acrossHalf = (lateral ? length : width) * .5f;
                            float alongHalf = (lateral ? width : length) * .5f;
                            for (float inset = 1f; inset <= 12f; inset += .5f)
                                for (float across = acrossHalf + (loader || !lateral ? .75f : 6f);
                                    across + acrossHalf <= Mathf.Max(-layout.x0, layout.x1) - .25f; across += .5f)
                                {
                                    var bay = new StandMarkings.EquipmentBay { centre = layout.At(side * across, layout.far - inset - alongHalf),
                                        facing = facing, width = width, length = length, equipment = label, stands = request.stand.Label };
                                    var box = BayBox(bay, .15f);
                                    if (guarded == null || !OnPad(layout, bay) || obstacles.Any(o => o.Overlaps(box))) continue;
                                    if (workspaces.Any(w => w.station == request.station ? !loader && w.loader.Overlaps(box) :
                                        w.loader.Overlaps(box) || w.train.Overlaps(box))) continue;
                                    var pose = new BaggageVehiclePose(bay.centre - GeoRight(facing) * bounds.center.x - facing * bounds.center.z, facing);
                                    Box entrance = default, exit = default;
                                    if (!loader)
                                    {
                                        var back = bay.Point(0, -length * .5f);
                                        var edge = lateral ? layout.At(side * (side > 0 ? layout.x1-.25f : -layout.x0-.25f),
                                            Vector2.Dot(bay.centre-layout.entry,layout.axis)) : layout.At(side*across,layout.far-.25f);
                                        entrance = Box.Of((back + edge) * .5f, facing,
                                            new Bounds(Vector3.zero, new Vector3(width + .3f, 1, Vector2.Distance(back, edge))), 0);
                                    }
                                    float radius = loader ? request.loader.turnRadius : Mathf.Max(6,request.tractor.turnRadius,request.cart.turnRadius);
                                    var turnSide = lateral ? layout.nose : GeoRight(layout.nose) * side;
                                    var turning = loader ? request.loader.bounds : request.tractor.bounds;
                                    turning.Expand(new Vector3(radius,0,radius));
                                    exit = Box.Of(pose.position + facing * (2f + radius * .5f) + turnSide * (radius * .5f),facing,turning,.5f);
                                    if (obstacles.Any(o=>o.Overlaps(exit))) continue;
                                    slot.candidates.Add(new Candidate { id=candidateId++,loader=loader,bay = bay, pose = pose, box = box,
                                        parked=BayBox(bay,.5f),close=BayBox(bay,.3f),entrance = entrance, exit = exit });
                                }
                        }
                    }
                    Generate(TrainBounds(request.tractor, request.cart, request.count),
                        request.cart.kind == VehicleKind.ContainerDolly ? "ULD TRAIN" : "BAG TRAIN", false);
                    Generate(request.loader.bounds, request.loader.kind == VehicleKind.ContainerLoader ? "ULD LOADER" : "BELT", true);
                }
                slots = slots.OrderBy(q=>q.loader).ThenByDescending(q=>q.length).ToList();
                // Bound authoring work on crowded stands; failed layouts stay cached until an input changes.
                int comparisons=0;
                var compatibility=new Dictionary<long,bool>();
                bool Compatible(Candidate previous,Candidate next)
                {
                    long key=((long)previous.id<<32)|(uint)next.id;
                    if (compatibility.TryGetValue(key,out var allowed)) return allowed;
                    if (++comparisons>10000000) return false;
                    allowed=!previous.parked.Overlaps(next.box) &&
                        (next.loader || !previous.close.Overlaps(next.entrance)) && !previous.close.Overlaps(next.exit) &&
                        (previous.loader || !previous.entrance.Overlaps(next.box)) && !previous.exit.Overlaps(next.box);
                    compatibility.Add(key,allowed); return allowed;
                }
                bool Place(int index,List<Candidate>[] choices)
                {
                    if (index == slots.Count) return true;
                    var slot = slots[index];
                    foreach (var c in choices[index])
                    {
                        if (comparisons>10000000) return false;
                        // Propagate each choice to every remaining item, detecting an impossible last
                        // loader before enumerating all combinations of the earlier equipment.
                        var remaining=(List<Candidate>[])choices.Clone(); bool viable=true;
                        for (int i=index+1;i<slots.Count;i++)
                        {
                            var candidates=new List<Candidate>();
                            foreach (var other in choices[i]) if (Compatible(c,other)) candidates.Add(other);
                            if (candidates.Count==0) { viable=false; break; }
                            remaining[i]=candidates;
                        }
                        if (!viable) continue;
                        if (slot.loader) { slot.parking.loaderBay = c.bay; slot.parking.loader = c.pose; }
                        else { slot.parking.trainBay = c.bay; slot.parking.train = c.pose; }
                        if (Place(index + 1,remaining)) return true;
                        if (slot.loader) slot.parking.loaderBay = null; else slot.parking.trainBay = null;
                    }
                    return false;
                }
                if (batch.Any(q => q.problem != null) || !Place(0,slots.Select(s=>s.candidates).ToArray()) || batch.Any(q => !q.Valid))
                    foreach (var q in batch) { q.problem ??= "No clear equipment staging layout found on " + group.Key.Name + "."; q.loaderBay = q.trainBay = null; }
                result.AddRange(batch);
            }
            cache.Remove(airport); cache.Add(airport,new Cached { stamp=stamp,parking=result });
            return result;
        }

        public static Bounds TrainBounds(VehicleType tractor,VehicleType cart,int count)
        {
            var bounds=tractor.bounds; var hitch=tractor.baggage.rearHitch;
            for (int i=0;i<Mathf.Max(1,count);i++)
            { var origin=hitch-cart.baggage.frontHitch; bounds.Encapsulate(origin+cart.bounds.min); bounds.Encapsulate(origin+cart.bounds.max); hitch=origin+cart.baggage.rearHitch; }
            return bounds;
        }

        public static Box BayBox(StandMarkings.EquipmentBay bay,float margin) =>
            Box.Of(bay.centre,bay.facing,new Bounds(Vector3.zero,new Vector3(bay.width,1,bay.length)),margin);
        static Vector2 GeoRight(Vector2 f) => new Vector2(f.y,-f.x);

        static List<Box> Fixed(Airport airport,BaggageDepot except=null)
        {
            var boxes = new List<Box>();
            foreach (var depot in airport.GetComponentsInChildren<BaggageDepot>()) if (depot!=except) boxes.Add(depot.BuildingBox);
            foreach (var terminal in airport.terminals)
                foreach (var polygon in terminal.Footprints())
                {
                    if (polygon.Count == 0) continue;
                    Vector2 min=polygon[0],max=min;
                    foreach (var p in polygon) { min=Vector2.Min(min,p); max=Vector2.Max(max,p); }
                    boxes.Add(Box.Of((min+max)*.5f,Vector2.up,new Bounds(Vector3.zero,new Vector3(max.x-min.x,1,max.y-min.y)),.75f));
                }
            foreach (var r in airport.Bridges.BaggageExclusions())
                boxes.Add(Box.Of(r.center,Vector2.up,new Bounds(Vector3.zero,new Vector3(r.width,1,r.height)),.75f));
            foreach (var e in airport.Taxiways.edges)
            {
                var a=airport.Taxiways.At(e.a); var b=airport.Taxiways.At(e.b);
                boxes.Add(Box.Of((a+b)*.5f,(b-a).normalized,new Bounds(Vector3.zero,new Vector3(e.width,1,Vector2.Distance(a,b))),.5f));
            }
            return boxes;
        }

        /// <summary>Shared equipment courts may use the adjoining apron, clear of every stand's arrival envelope.</summary>
        public static List<Box> StorageExclusions(Airport airport,BaggageDepot except=null)
        {
            var boxes=Fixed(airport,except);
            foreach (var layout in airport.Taxiways.Layouts())
            {
                var swept=Swept(layout,airport.Bridges.TypesFor(layout.stand));
                if (swept != null) boxes.AddRange(swept);
            }
            return boxes;
        }

        public static bool Clear(Airport airport, StandLayout layout, StandMarkings.EquipmentBay bay,
            IEnumerable<AircraftType> types, List<Box> obstacles = null)
        {
            var box = BayBox(bay, .15f); var swept = Swept(layout, types);
            return OnPad(layout, bay) && swept != null && !(obstacles ?? Fixed(airport)).Any(q => box.Overlaps(q)) &&
                !swept.Any(q => box.Overlaps(q));
        }

        static bool OnPad(StandLayout layout, StandMarkings.EquipmentBay bay)
        {
            foreach (float x in new[] { -bay.width * .5f, bay.width * .5f })
                foreach (float z in new[] { -bay.length * .5f, bay.length * .5f })
                    if (!layout.Contains(bay.Point(x, z))) return false;
            return true;
        }

        static List<Box> Swept(StandLayout layout, IEnumerable<AircraftType> types)
        {
            var sweeps = new List<Box>();
            foreach (var type in types)
            {
                if (!layout.ParkingFits(type)) continue;
                if (type.serviceFootprints.Length == 0) return null;
                var park = layout.ParkPosition(type); var right = GeoRight(layout.nose);
                foreach (var part in type.serviceFootprints)
                {
                    if (part.outline.Length == 0) continue;
                    float minX = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                    foreach (var p in part.outline) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); maxZ = Mathf.Max(maxZ, p.y); }
                    if (part.name.ToLowerInvariant().Contains("engine")) maxZ += 5f;
                    float back = -type.Length - Vector2.Distance(layout.entry, park);
                    var centre = park + right * ((minX + maxX) * .5f) + layout.nose * ((back + maxZ) * .5f);
                    sweeps.Add(Box.Of(centre, layout.nose, new Bounds(Vector3.zero, new Vector3(maxX - minX, 1, maxZ - back)), 1f));
                }
            }
            return sweeps;
        }
    }
}
