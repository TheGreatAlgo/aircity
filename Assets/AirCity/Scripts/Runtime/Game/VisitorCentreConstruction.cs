using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>One detached building, parking and public-road quote in both game modes.</summary>
    public sealed class VisitorCentreConstruction
    {
        public const int SiteVersion=1;
        public Vector2 Position { get; private set; }
        public float Heading { get; private set; }
        public int Price { get; private set; }
        public VisitorCentreLayout.RoadPlan Road { get; private set; }
        public IReadOnlyList<Vector2> GroundSamples { get; private set; }

        public static bool TryCreate(CareerState state,Vector2 position,float heading,Airport airport,
            out VisitorCentreConstruction plan,out string problem)
        {
            plan=null; problem="Choose a visitor centre site inside the airport region.";
            if (state?.world==null || state.layout==null || !ValleyWorld.Finite(position.x) || !ValleyWorld.Finite(position.y) || !ValleyWorld.Finite(heading)) return false;
            var footprints=VisitorCentreLayout.Footprints(position,heading,true).ToArray();
            var samples=footprints.SelectMany(Samples).ToArray();
            var prepared=new List<Vector2>(samples);
            if (!CareerConstruction.Ground(state,samples,state.layout.elevation,out problem)) return false;
            if (samples.Any(p=>state.world.IsWater(p) || Mathf.Abs(state.world.Height(p)-state.layout.elevation)>.35f))
            { problem="Prepare dry, level ground for the complete visitor centre and parking lot."; return false; }
            foreach(var polygon in footprints)
            {
                problem=AirportProblem(airport,polygon);
                if(problem!=null)return false;
                // The tutorial projection also protects headless and original-layout fixtures.
                if (airport==null && (polygon.Any(p=>state.layout.runwayBuilt && CareerConstruction.Distance(p,state.layout.runwayA,state.layout.runwayB)<40f) ||
                    state.layout.TaxiSegments().Any(e=>Geo.Overlap(polygon,Strip(e.a,e.b,9f)))))
                {problem="Keep the visitor centre and parking clear of aircraft pavement.";return false;}
            }
            var source=state.RoadNetwork();
            foreach(var road in source.segments)
                foreach(var span in source.Path(road).Spans())
                    if(SiteOverlaps(position,heading,Strip(Geo.XZ(span.a),Geo.XZ(span.b),road.Type.FullWidth*.5f),true,true))
                    {problem="Keep the building and parking bays beside the public road; align its driveway opening with the road.";return false;}
            if(!VisitorCentreLayout.TryConnect(source,position,state.layout.elevation,heading,out VisitorCentreLayout.RoadPlan connection,out problem))return false;
            var clearance=airport!=null ? new RoadConstructionPlan.AirportClearance(airport) : null;
            double earthwork=0d;
            foreach(int id in connection.NewSegments)
            {
                var road=connection.Draft.Segment(id);var path=connection.Draft.Path(road);
                foreach(var span in path.Spans())
                {
                    var strip=Strip(Geo.XZ(span.a),Geo.XZ(span.b),road.Type.FullWidth*.5f);
                    problem=AirportProblem(airport,strip);if(problem!=null)return false;
                    int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(span.a,span.b)/3f));
                    for(int i=0;i<=count;i++)
                    {
                        var at=Vector3.Lerp(span.a,span.b,i/(float)count);var across=Geo.Right(Geo.XZ(span.b-span.a).normalized);
                        if(clearance!=null && !clearance.Clear(at,road.Type.FullWidth*.5f,RoadStructure.Ground,out problem))return false;
                        foreach(float side in new[]{-1f,0f,1f})
                        {
                            var p=Geo.XZ(at)+across*(side*road.Type.FullWidth*.5f);
                            prepared.Add(p);
                            if(!state.BuildableBounds.Contains(p) || state.world.IsWater(p))
                            {problem="Keep the visitor driveway on dry land inside the region.";return false;}
                            if(ForestQuery.AnyWithin(state.world,p,6f))
                            {problem="Clear the trees along the visitor driveway first.";return false;}
                            if(i<count)earthwork+=Mathf.Max(0f,Mathf.Abs(state.world.Height(p)-at.y)-.35f)*
                                Vector3.Distance(span.a,span.b)/count*road.Type.FullWidth/3f;
                        }
                    }
                }
            }
            connection.Price+=Mathf.CeilToInt((float)earthwork*RunwayTerrainPlan.EarthworkPrice);
            plan=new VisitorCentreConstruction {Position=position,Heading=heading,Road=connection,
                Price=CareerConstruction.ReceptionPrice+connection.Price,GroundSamples=prepared};
            problem=state.mode!=GameMode.Sandbox && state.funds<plan.Price ? "Not enough funds for the visitor centre, parking and driveway." : null;
            return problem==null;
        }

        public static IEnumerable<Vector2> Samples(IList<Vector2> polygon)
        {
            foreach(var p in polygon)yield return p;
            var min=polygon.Aggregate(Vector2.Min);var max=polygon.Aggregate(Vector2.Max);
            for(float x=min.x;x<=max.x;x+=3f)for(float z=min.y;z<=max.y;z+=3f)
            {var p=new Vector2(x,z);if(Geo.Inside(polygon,p))yield return p;}
        }

        static string AirportProblem(Airport airport,IList<Vector2> world)
        {
            if(airport==null)return null;
            var local=world.Select(p=>Geo.XZ(airport.transform.InverseTransformPoint(Geo.X0Z(p)))).ToArray();
            // The public-road stub can be smaller than the minimum user-authored terminal.
            foreach(var terminal in airport.terminals)
                if(terminal!=null && terminal.Footprints().Any(p=>Geo.Overlap(local,p)))return "Keep the visitor centre site clear of terminal buildings.";
            foreach (var hangar in airport.HangarBuildings)
            {
                if (Geo.Overlap(local, hangar.BuildingPad)) return "Keep the visitor centre site clear of aircraft hangars.";
                if (HangarConnection.Approach(hangar, out var approach) && Geo.Overlap(local, approach))
                    return "Keep the visitor centre and driveway clear of the hangar's paved taxiway approach.";
            }
            foreach(var depot in airport.Depots)if(Geo.Overlap(local,depot.Corners))return "Keep the visitor centre site clear of maintenance depots.";
            foreach(var depot in airport.GetComponentsInChildren<BaggageDepot>(true))
            {
                var b=depot.BuildingBox;
                if(Geo.Overlap(local,new[]{b.c-b.ax*b.hx-b.az*b.hz,b.c+b.ax*b.hx-b.az*b.hz,b.c+b.ax*b.hx+b.az*b.hz,b.c-b.ax*b.hx+b.az*b.hz}))
                    return "Keep the visitor centre site clear of baggage depots.";
            }
            foreach(var runway in airport.Runways)
                if(Geo.Overlap(local,Strip(Geo.XZ(runway.start)-runway.Axis2D*runway.WorkMargin,Geo.XZ(runway.end)+runway.Axis2D*runway.WorkMargin,runway.HalfWidthTotal+6f)))
                    return "Keep the visitor centre site clear of runways.";
            foreach(var edge in airport.Taxiways.edges)
                foreach(var span in airport.Taxiways.Path(edge).Spans())
                    if(Geo.Overlap(local,Strip(Geo.XZ(span.a),Geo.XZ(span.b),edge.width*.5f+2f)))
                        return "Keep the visitor centre site clear of taxiways and service roads.";
            foreach(var stand in airport.Taxiways.Layouts())if(Geo.Overlap(local,stand.Pad))return "Keep the visitor centre site clear of aircraft stands.";
            foreach(var apron in airport.aprons)if(Geo.Overlap(local,apron.outline))return "Keep the visitor centre site clear of aircraft aprons.";
            foreach(var area in airport.deicingWorkAreas)if(area!=null && Geo.Overlap(local,area.Outline))return "Keep the visitor centre site clear of deicing equipment.";
            foreach(var bay in airport.deicingParkingBays)if(bay!=null && Geo.Overlap(local,bay.Outline))return "Keep the visitor centre site clear of deicing parking.";
            return null;
        }

        public static string OverlapProblem(CareerState state,IList<Vector2> polygon,bool allowDriveway=false)
        {
            var layout=state?.layout;
            return layout!=null && layout.receptionBuilt && SiteOverlaps(layout.reception,layout.receptionHeading,polygon,layout.receptionSiteVersion>=SiteVersion,allowDriveway)
                ? "Keep construction clear of the visitor centre and its parking lot." : null;
        }

        /// <summary>The way out of a facility's car park at its public-road entrance node: the visitor centre's, or
        /// a flying school's.</summary>
        internal static Vector2? DrivewayOutward(CareerState state, PublicRoadNode entrance, Airport airport)
        {
            if (airport != null && FlyingSchoolLayout.IsEntrance(entrance?.terminalEntrance))
                foreach (var school in airport.FlyingSchools)
                    if (school.EntranceId == entrance.terminalEntrance &&
                        Vector2.Distance(school.Entrance + Geo.XZ(airport.transform.position), Geo.XZ(entrance.position)) <= .15f)
                        return school.DrivewayOutward;
            return DrivewayOutward(state, entrance);
        }

        internal static Vector2? DrivewayOutward(CareerState state, PublicRoadNode entrance)
        {
            var layout = state?.layout;
            if (layout == null || !layout.receptionBuilt || layout.receptionSiteVersion < SiteVersion ||
                entrance?.terminalEntrance != VisitorCentreLayout.EntranceId) return null;
            var mouth = VisitorCentreLayout.Entrance(layout.reception, layout.elevation, layout.receptionHeading);
            return Vector3.Distance(mouth, entrance.position) <= .15f ? Geo.Right(VisitorCentreLayout.Axis(layout.receptionHeading)) : null;
        }

        static bool SiteOverlaps(Vector2 position,float heading,IList<Vector2> polygon,bool includeParking,bool allowDriveway)
        {
            Vector2 P(float x,float z)=>VisitorCentreLayout.Point(position,heading,new Vector2(x,z));
            Vector2[] Rect(float x0,float z0,float x1,float z1)=>new[]{P(x0,z0),P(x1,z0),P(x1,z1),P(x0,z1)};
            if(!includeParking)return Geo.Overlap(polygon,Rect(-13,-8,13,10));
            if(!allowDriveway)return VisitorCentreLayout.Footprints(position,heading,true).Any(p=>Geo.Overlap(polygon,p));
            return Geo.Overlap(polygon,Rect(-20,-8,17,32)) || Geo.Overlap(polygon,Rect(17,-8,24.7f,17)) || Geo.Overlap(polygon,Rect(17,26,24.7f,32));
        }

        public static Vector2[] Strip(Vector2 a,Vector2 b,float half)
        {
            var delta=b-a;var right=Geo.Right(delta.sqrMagnitude>.001f ? delta.normalized : Vector2.up)*half;
            return new[]{a-right,b-right,b+right,a+right};
        }
    }
}
