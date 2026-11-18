using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using AirCity.Traffic;
using AirCity.Vehicles;
using AirCity.World;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>The doorway's centred, driveable connection to a taxiway. This is derived from the graph;
    /// no extra stand or graph identity is saved for the hangar.</summary>
    public static class HangarConnection
    {
        const float Clearance = .15f;
        public const float MaxApproach = 12f;
        internal const float PreferredApproach = 10.5f;

        internal static bool IsLevelTaxiway(TaxiwayEdge edge, TaxiwayNode a, TaxiwayNode b) =>
            a != null && b != null && !edge.IsRoad && edge.structure == RoadStructure.Ground &&
            !edge.alignment.curved && !edge.alignment.smooth &&
            Mathf.Abs(a.position.y) <= .1f && Mathf.Abs(b.position.y) <= .1f;

        public static bool TryGet(SmallHangar hangar, AircraftType type, out StandLayout layout, out string problem)
        {
            if (hangar == null) return TryGet(null, Vector2.zero, 0f, type, out layout, out problem);
            layout = null;
            problem = type != null ? SmallHangar.AircraftProblem(type, hangar.Size) : null;
            return problem == null && Find(hangar.Airport, hangar.Door, hangar.Heading, hangar.Spec, type, true, out layout, out problem,
                hangar.BuildingRoot);
        }

        public static bool TryGet(Airport airport, Vector2 door, float heading, AircraftType type,
            out StandLayout layout, out string problem) =>
            Find(airport, door, heading, HangarSpec.For(HangarSize.Small), type, true, out layout, out problem);

        static bool Find(Airport airport, Vector2 door, float heading, HangarSpec spec, AircraftType type, bool operational,
            out StandLayout layout, out string problem, SmallHangar building = null)
        {
            layout = null;
            problem = spec.TaxiCode == 'A' ? "Connect a level taxiway across the front of the doors, within 12 m of the threshold." :
                $"Connect a level paved code {spec.TaxiCode}+ taxiway across the doors, {spec.MinSetback:0}–{spec.MaxSetback:0} m from the threshold.";
            if (airport == null) { problem = "Place this hangar at an airport first."; return false; }
            var net = airport.GetComponentInChildren<TaxiwayNetwork>(true);
            if (net == null) return false;
            var axis = SmallHangar.Facing(heading); var nose = -axis; var across = Geo.Right(axis);
            var threshold = building != null ? building.Door : door;
            float spotOffset = Vector2.Dot(door - threshold, across);
            var ok = type != null && operational ? net.TaxiOk(type) : null;
            float best = float.MaxValue;
            char code = type != null && type.SizeCode > spec.TaxiCode ? type.SizeCode : spec.TaxiCode;
            float width = RunwayRules.TaxiwayWidthFor(code);
            foreach (var edge in net.edges)
            {
                var na = net.Node(edge.a); var nb = net.Node(edge.b);
                // The existing taxi planner lays graph links as straight lines. A curved/graded link needs
                // a dedicated entry model before a doorway can safely attach partway along it.
                if (!IsLevelTaxiway(edge, na, nb) || edge.width + .01f < width || spec.TaxiCode != 'A' && !edge.surface.IsPaved()) continue;
                if (type != null && !RunwayRules.CanTaxi(type, edge.surface, edge.width)) continue;
                var a = Geo.XZ(na.position); var delta = Geo.XZ(nb.position) - a;
                float length = delta.magnitude; if (length < 1f) continue;
                var along = delta / length;
                float sin = Mathf.Abs(Geo.Cross(along, axis));
                if (sin < (spec.TaxiCode == 'A' ? .7f : .985f) ||
                    !Geo.Lines(threshold, axis, a, along, out float centreForward, out float centreAt)) continue;
                float near = centreForward - edge.width / (2f * sin);
                // Leave room in both graph directions for the rounded turn into/out of the bay. An
                // offset nearest endpoint is not a connection through the centre of the doorway.
                float turnRoom = spec.TaxiCode == 'A' ? Mathf.Max(3f, type != null ? type.Wheelbase * 2f : 4f) :
                    Mathf.Max(spec.Frontage, type != null ? type.Wheelbase * 2f : 0f);
                if (near < spec.MinSetback || near > spec.MaxSetback) continue;
                if (spec.Bays > 1 && Mathf.Min(centreAt, length - centreAt) + .01f < spec.ConnectionHalfSpan) continue;
                var normal = Geo.Right(along);
                if (Vector2.Dot(normal, axis) < 0f) normal = -normal;
                float slope = Vector2.Dot(across, normal) / Vector2.Dot(axis, normal);
                float forward = centreForward - spotOffset * slope;
                var entry = door + axis * forward;
                float u = Vector2.Dot(entry - a, along);
                if (Mathf.Min(u, length - u) + .01f < turnRoom) continue;
                if (operational && (ok != null ? !ok(edge) : edge.wear >= RunwayRules.ClosedWear || TaxiwayNetwork.Closed(edge)))
                { problem = "The connecting taxiway is closed or unsuitable for this aircraft."; continue; }
                float half = spec.ApproachWidth * .5f;
                var end = threshold + axis * near;
                float nearLeft = near + half * slope, nearRight = near - half * slope;
                if(Mathf.Min(nearLeft,nearRight)<SmallHangar.RoofOverhang-.05f)continue;
                var lane = new[] { threshold - across * half, threshold + across * half,
                    threshold+across*half+axis*nearRight,threshold-across*half+axis*nearLeft };
                bool blocked = airport.HangarBuildings.Any(h => h != building && (h.Door - threshold).sqrMagnitude > .001f &&
                    Geo.Overlap(lane, h.BuildingPad)) ||
                    airport.terminals.Any(t => t != null && t.Footprints().Any(p => Geo.Overlap(lane,p))) ||
                    airport.Depots.Any(d => Geo.Overlap(lane,d.Corners)) ||
                    airport.deicingWorkAreas.Any(d => d != null && Geo.Overlap(lane,d.Outline)) ||
                    airport.deicingParkingBays.Any(d => d != null && Geo.Overlap(lane,d.Outline)) ||
                    net.Layouts().Any(l=>Geo.Overlap(lane,l.Pad));
                foreach(var other in net.edges)
                {
                    if(blocked)break;if(other==edge || !other.IsRoad)continue;
                    foreach(var span in net.Path(other).Spans())
                    {
                        var from=Geo.XZ(span.a);var to=Geo.XZ(span.b);var r=Geo.Right((to-from).normalized)*other.width*.5f;
                        if(Geo.Overlap(lane,new[]{from+r,to+r,to-r,from-r})){blocked=true;break;}
                    }
                }
                foreach(var runway in airport.Runways)
                {
                    var from=Geo.XZ(runway.start)-runway.Axis2D*runway.WorkMargin;
                    var to=Geo.XZ(runway.end)+runway.Axis2D*runway.WorkMargin;
                    var r=Geo.Right(runway.Axis2D)*runway.HalfWidthTotal;
                    if(Geo.Overlap(lane,new[]{from+r,to+r,to-r,from-r})){blocked=true;break;}
                }
                var box = AirportMaintenance.Box.Of((threshold+end)*.5f,axis,
                    new Bounds(Vector3.zero,new Vector3(half*2f,spec.Eave,Mathf.Max(.1f,near))),0f);
                if (blocked || airport.GetComponentsInChildren<BaggageDepot>(true).Any(d => box.Overlaps(d.BuildingBox)))
                { problem = "Clear the full approach between the hangar doors and the taxiway."; continue; }
                var site = airport.HangarApproachProblem?.Invoke(lane);
                if (site != null) { problem = site; continue; }
                if (centreForward >= best) continue;
                best = centreForward;
                float wb = type != null ? Mathf.Max(type.Wheelbase,.3f) : 1.5f;
                var centre = type != null ? type.bounds.center : Vector3.zero;
                var park = door - axis * (spec.Depth*.5f) + axis * centre.z + across * centre.x;
                var stand = new Stand { code = SizeCodes.Of(code), surface = PavementSurface.Concrete, exit = StandExit.PushBack,
                    position = Geo.X0Z(entry), heading = Mathf.Repeat(heading+180f,360f), lane = edge.width };
                stand.Frame(along,edge.width);
                layout = new StandLayout { stand = stand, edge = edge, entry = entry, along = along, axis = nose, nose = nose,
                    side = nose, halfWidth = edge.width*.5f, clearance = edge.width*.5f, sin = sin,
                    k = Vector2.Dot(Geo.Right(nose),Geo.Right(along)), x0 = -spec.BayWidth*.5f, x1 = spec.BayWidth*.5f,
                    near0 = forward, near1 = forward, far = forward+spec.Depth,
                    stop = park + nose*wb };
            }
            if (layout == null && type != null && code > spec.TaxiCode && problem.StartsWith("Connect"))
                problem = $"This aircraft needs a level paved code {code}+ taxiway across the doors.";
            if (layout == null) return false;
            problem = null; return true;
        }

        /// <summary>The concrete throat, ending just under the connecting taxiway's near pavement edge.
        /// Transient traffic/weather closures do not remove constructed pavement.</summary>
        public static bool Approach(SmallHangar h, out Vector2[] outline)
        {
            outline = null;
            if (h == null) return false;
            h = h.BuildingRoot;
            if (!Find(h.Airport,h.Door,h.Heading,h.Spec,null,false,out var l,out _,h)) return false;
            var across = Geo.Right(h.Axis); float half = h.Spec.ApproachWidth*.5f;
            var near = l.entry - h.Axis*(l.halfWidth / l.sin - .05f);
            // End on the actual angled pavement boundary, rather than leaving a triangular grass gap.
            var normal = Geo.Right(l.along); if (Vector2.Dot(normal,h.Axis) < 0f) normal = -normal;
            float Along(Vector2 p) => Vector2.Dot(near-p,normal)/Mathf.Max(.001f,Vector2.Dot(h.Axis,normal));
            var left=h.Door-across*half; var right=h.Door+across*half;
            outline = new[] { left,right,right+h.Axis*Along(right),left+h.Axis*Along(left) };
            return true;
        }

        public static float SurfaceHeight(SmallHangar h, Vector2 point, Vector2[] approach = null)
        {
            float z=Vector2.Dot(point-h.Door,h.Axis);
            if (z<=0f) return SmallHangar.FloorHeight;
            if (approach == null && !Approach(h,out approach)) return AirfieldHeights.Taxiway;
            var front = approach[3]-approach[2];
            float distance = Geo.Lines(point-h.Axis*z, h.Axis, approach[2], front, out float at, out _) ? at : 0f;
            return Mathf.Lerp(SmallHangar.FloorHeight,AirfieldHeights.Stand,Mathf.Clamp01(z/Mathf.Max(.1f,distance)));
        }

        /// <summary>Conservative full-aircraft bounds against the walls and doorway cheeks. Turns must be
        /// completed outside; fitting the gear track alone does not mean the wings clear a doorway.</summary>
        public static bool PoseFits(SmallHangar h, AircraftType type, Vector2 gear, Vector2 facing)
        {
            if (h == null || type == null) return false;
            var axis = h.Axis;
            var across = Geo.Right(axis);
            var threshold = h.BuildingRoot.Door;
            var aircraft = AirportMaintenance.Box.Of(gear, facing, type.bounds, Clearance);
            bool Wall(float x0, float z0, float x1, float z1) => aircraft.Overlaps(new AirportMaintenance.Box
            {
                c = threshold + across * ((x0 + x1) * .5f) + axis * ((z0 + z1) * .5f),
                ax = across, az = axis, hx = (x1 - x0) * .5f, hz = (z1 - z0) * .5f
            });
            var spec = h.Spec;
            float w=spec.Width*.5f, d=spec.Depth, door=spec.DoorWidth*.5f;
            return !Wall(-w-.2f,-d-.2f,-w+.2f,.2f) && !Wall(w-.2f,-d-.2f,w+.2f,.2f) &&
                !Wall(-w-.2f,-d-.2f,w+.2f,-d+.2f) && !Wall(-w,-.2f,-door+.1f,.2f) && !Wall(door-.1f,-.2f,w,.2f);
        }

        public static bool ValidateMovement(SmallHangar h, AircraftType type, TaxiPath path, out string problem)
        {
            problem = "Move the taxiway farther in front of the doors so the aircraft can straighten before entering.";
            if(path==null)return false;
            for(float d=0f;d<path.Length+.25f;d+=.25f)
            {path.Pose(Mathf.Min(d,path.Length),out var gear,out var facing,out _,out _);if(!PoseFits(h,type,gear,facing))return false;}
            problem=null;return true;
        }

        public static bool ValidateMovement(SmallHangar h, AircraftType type, PushBackPath path, out string problem)
        {
            problem = "Move the taxiway farther in front of the doors so the aircraft can clear the walls before turning.";
            if(path==null)return false;
            for(float d=0f;d<path.Length+.25f;d+=.25f)
            {path.Sample(Mathf.Min(d,path.Length),out var gear,out var facing,out _,out _);if(!PoseFits(h,type,gear,facing))return false;}
            problem=null;return true;
        }
    }
}
