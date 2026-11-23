using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>
    /// One detached quote for a flying school in both game modes: the clubhouse and car park, the flight line's
    /// stands and the two-way public-road driveway. Quote and commit use the same package; a refused placement
    /// changes no funds, roads or airport data. Positions are in Airport space; the public road graph is in world
    /// space (the airport is never rotated in the world).
    /// </summary>
    public sealed class FlyingSchoolConstruction
    {
        public Vector2 Origin { get; private set; }
        public float Heading { get; private set; }
        public float Lane { get; private set; }
        public int Side { get; private set; }
        public PavementSurface Surface { get; private set; }
        public int Price { get; private set; }
        public RoadPlan Road { get; private set; }
        /// <summary>The site id the school will be given, so the driveway's entrance node already carries it.</summary>
        public string SiteId { get; private set; }

        public sealed class RoadPlan
        {
            public PublicRoadNetwork Draft { get; internal set; }
            public int EntranceNode { get; internal set; }
            public IReadOnlyList<int> NewSegments { get; internal set; }
            public float Length { get; internal set; }
            public int Price { get; internal set; }
        }

        /// <summary>Quote a school at this site. With no game (fixtures, authored scenes) only the airport's own
        /// layout is checked and no driveway is planned.</summary>
        public static bool TryCreate(CareerGame game, Airport airport, Vector2 origin, float heading, float lane, int side,
            PavementSurface surface, out FlyingSchoolConstruction plan, out string problem)
        {
            plan = null;
            if (airport == null || !Finite(origin.x) || !Finite(origin.y) || !Finite(heading))
            { problem = "Choose a flying school position inside the airport."; return false; }
            lane = FlyingSchoolLayout.Lane(lane); side = FlyingSchoolLayout.Side(side);
            problem = airport.FlyingSchoolProblem(origin, heading, lane, side, surface);
            if (problem != null) return false;
            var draft = new FlyingSchoolConstruction
            {
                Origin = origin, Heading = heading, Lane = lane, Side = side, Surface = surface,
                SiteId = Guid.NewGuid().ToString("N").Substring(0, 12), Price = FlyingSchoolRules.BuildPrice,
            };
            if (game == null) { plan = draft; return true; }
            problem = game.ToolProblem(AirCity.Dev.DevTool.FlyingSchool);
            if (problem != null) return false;
            var state = game.State;
            var world = Geo.XZ(airport.transform.position);
            float datum = airport.transform.position.y;
            var footprints = FlyingSchoolLayout.Footprints(origin, heading, lane, side).ToArray();
            var samples = footprints.SelectMany(p => FlyingSchoolLayout.Samples(p)).ToArray();
            if (samples.Any(p => state.world.IsWater(p + world)))
            { problem = "Prepare dry land above the water for the whole flying school."; return false; }
            if (!game.CheckFootprint(samples, 0f, out problem)) return false;
            if (samples.Any(p => Mathf.Abs(state.world.Height(p + world) - datum) > .35f))
            { problem = "Level the flight line, clubhouse and car park to the airport height first."; return false; }
            foreach (var polygon in footprints)
            {
                problem = VisitorCentreConstruction.OverlapProblem(state, polygon.Select(p => p + world).ToArray());
                if (problem != null) return false;
            }
            var roads = state.RoadNetwork();
            foreach (var road in roads.segments)
                foreach (var span in roads.Path(road).Spans())
                {
                    var strip = VisitorCentreConstruction.Strip(Geo.XZ(span.a) - world, Geo.XZ(span.b) - world, road.Type.FullWidth * .5f + 1f);
                    if (footprints.Any(p => Geo.Overlap(p, strip)))
                    { problem = "Keep the school beside the public road, not on it; its driveway joins the road for you."; return false; }
                }
            // The driveway leaves the car park straight, then turns towards the road. A long lead leaves room for the
            // access road's full bend radius; where that can't be built (the road is close by, something is in the
            // way) the short one is judged instead, and its refusal is the one reported.
            bool Driveway(float leadRun, out RoadPlan connection, out string why)
            {
                if (!TryConnect(roads, airport, draft, leadRun, out connection, out why)) return false;
                // The driveway follows a graded profile: check its whole width and charge the cut and fill.
                double earthwork = 0d;
                foreach (int id in connection.NewSegments)
                {
                    var road = connection.Draft.Segment(id);
                    foreach (var span in connection.Draft.Path(road).Spans())
                    {
                        float half = road.Type.FullWidth * .5f;
                        var a = Geo.XZ(span.a) - world; var b = Geo.XZ(span.b) - world;
                        why = airport.SiteProblem(VisitorCentreConstruction.Strip(a, b, half), "driveway");
                        if (why != null) return false;
                        if (airport.aprons.Any(apron => apron != null && apron.outline.Count >= 3 && Geo.Overlap(VisitorCentreConstruction.Strip(a, b, half), apron.outline)))
                        { why = "Keep the driveway off aircraft aprons."; return false; }
                        if (VisitorCentreConstruction.OverlapProblem(state, VisitorCentreConstruction.Strip(Geo.XZ(span.a), Geo.XZ(span.b), half)) != null)
                        { why = "Keep the driveway clear of the visitor centre and its car park."; return false; }
                        int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(span.a, span.b) / 3f));
                        var across = Geo.Right((Geo.XZ(span.b) - Geo.XZ(span.a)).normalized);
                        for (int i = 0; i <= count; i++)
                        {
                            var at = Vector3.Lerp(span.a, span.b, i / (float)count);
                            foreach (float offset in new[] { -1f, 0f, 1f })
                            {
                                var p = Geo.XZ(at) + across * (offset * half);
                                if (!state.BuildableBounds.Contains(p) || state.world.IsWater(p))
                                { why = "Keep the school's driveway on dry land inside the region."; return false; }
                                if (ForestQuery.AnyWithin(state.world, p, 6f))
                                { why = "Clear the trees along the school's driveway first."; return false; }
                                if (i < count) earthwork += Mathf.Max(0f, Mathf.Abs(state.world.Height(p) - at.y) - .35f) *
                                    Vector3.Distance(span.a, span.b) / count * road.Type.FullWidth / 3f;
                            }
                        }
                    }
                }
                connection.Price += Mathf.CeilToInt((float)earthwork * RunwayTerrainPlan.EarthworkPrice);
                return true;
            }
            RoadPlan driveway = null;
            foreach (float leadRun in FlyingSchoolLayout.DrivewayLeads)
                if (Driveway(leadRun, out var judged, out problem)) { driveway = judged; break; }
            if (driveway == null) return false;
            draft.Road = driveway; draft.Price += driveway.Price;
            plan = draft;
            return game.CanSpend(draft.Price, out problem);
        }

        /// <summary>Quote and build in one step. The school, its stands and the driveway all appear, or nothing does.</summary>
        public static bool TryBuild(CareerGame game, Airport airport, Vector2 origin, float heading, float lane, int side,
            PavementSurface surface, out FlyingSchool school, out string problem)
        {
            school = null;
            if (!TryCreate(game, airport, origin, heading, lane, side, surface, out var plan, out problem)) return false;
            school = airport.AddFlyingSchool(origin, heading, plan.Lane, plan.Side, surface, plan.SiteId);
            if (school == null) { problem = "The flying school no longer fits here."; return false; }
            if (game != null)
            {
                game.Spend(plan.Price);
                if (plan.Road != null) game.SchoolRoadBuilt(plan.Road.Draft);
                game.SharedConstructionChanged();
            }
            return true;
        }

        /// <summary>Demolish a school that has sold its aircraft. Its driveway stays as a public road stub.</summary>
        public static bool TryDemolish(CareerGame game, Airport airport, FlyingSchool school, out string problem)
        {
            problem = school == null ? "Choose a flying school." : school.CanDemolish ? null : "Sell the school's aircraft before demolishing it.";
            if (problem != null) return false;
            string entrance = school.EntranceId;
            if (!airport.RemoveFlyingSchool(school)) { problem = "The flying school could not be removed."; return false; }
            if (game != null)
            {
                // The driveway stays as an ordinary public road; its end is no longer a car park entrance.
                var roads = game.State.RoadNetwork().Clone();
                var node = roads.nodes.FirstOrDefault(n => n.terminalEntrance == entrance);
                if (node != null) { node.terminalEntrance = null; game.SchoolRoadBuilt(roads); }
            }
            game?.SharedConstructionChanged();
            return true;
        }

        /// <summary>Why this school's driveway does not reach the public roads, or null.</summary>
        public static string RoadProblem(PublicRoadNetwork roads, FlyingSchool school)
        {
            if (roads == null || school == null) return null;
            var entrance = roads.nodes.FirstOrDefault(n => n.terminalEntrance == school.EntranceId);
            if (entrance == null || !roads.At(entrance.id).Any()) return "Connect the school's car park to a public road with the Street tool.";
            return OutsideNodes(roads).Any(id => id != entrance.id && roads.HasRoute(entrance.id, id) && roads.HasRoute(id, entrance.id))
                ? null : "The school's driveway does not reach the region's roads in both directions.";
        }

        static int[] OutsideNodes(PublicRoadNetwork roads) => roads.nodes.Where(n => n.outsideConnection ||
            Mathf.Abs(n.position.x) > ValleyWorld.Size * .5f - 20f || Mathf.Abs(n.position.z) > ValleyWorld.Size * .5f - 20f).Select(n => n.id).ToArray();

        /// <summary>A detached, bidirectional connection from the car park's aisle to a public road. Existing roads,
        /// node positions and entrance identities are retained.</summary>
        static bool TryConnect(PublicRoadNetwork source, Airport airport, FlyingSchoolConstruction site, float leadRun, out RoadPlan plan, out string problem)
        {
            plan = null; problem = "Place the school beside a connected public road: its car park needs a driveway.";
            if (source == null || source.segments.Count == 0) return false;
            var world = Geo.XZ(airport.transform.position); float elevation = airport.transform.position.y;
            float lane = site.Lane; int s = site.Side;
            var localEntrance = FlyingSchoolLayout.LocalEntrance(lane, s);
            Vector2 World(Vector2 local) => FlyingSchoolLayout.Point(site.Origin, site.Heading, local) + world;
            Vector2 Local(Vector3 point) => FlyingSchoolLayout.Local(site.Origin, site.Heading, Geo.XZ(point) - world);
            var entrance = Geo.X0Z(World(localEntrance), elevation);
            if (source.nodes.Any(n => Vector3.Distance(n.position, entrance) < .1f))
            { problem = "Leave a little room between the car park entrance and the existing public-road node."; return false; }
            var outside = OutsideNodes(source);
            bool HasOutsideRoute(PublicRoadNetwork graph, int node) =>
                outside.Any(id => id != node && graph.HasRoute(node, id) && graph.HasRoute(id, node));
            float back = FlyingSchoolLayout.LotBack(lane) + 9f, apron = FlyingSchoolLayout.ApronDepth(lane);
            float leadX = s * (FlyingSchoolLayout.LotHalfWidth + FlyingSchoolLayout.EntranceOffset + leadRun);
            var lead = new Vector2(leadX, localEntrance.y);
            // A long lead must stay well short of every road: it would otherwise cross the one it should join.
            bool shortest = leadRun <= FlyingSchoolLayout.DrivewayLeads[FlyingSchoolLayout.DrivewayLeads.Length - 1];
            if (!shortest && source.segments.Any(e =>
                {
                    var road = source.Path(e);
                    for (float x = 0f; x <= leadRun; x += 2f)
                        if (road.DistanceTo(World(new Vector2(localEntrance.x + s * x, localEntrance.y))) < 20f) return true;
                    return false;
                })) { problem = "The driveway's long approach would run into the public road."; return false; }
            // Keep clear of the whole site: the flight line (and the airside in front of it) and the block behind.
            bool Inside(Vector2 p) => (p.y < apron + 4.4f && Mathf.Abs(p.x) < FlyingSchoolLayout.ApronHalfWidth + 4.4f) ||
                (p.y < back - 4.6f && Mathf.Abs(p.x) < FlyingSchoolLayout.BlockHalfWidth + 4.4f);
            var candidates = new List<(Vector3 point, int node, float cost)>();
            foreach (var edge in source.segments)
            {
                if (edge.structure != RoadStructure.Ground || !edge.Type.atGrade || edge.BackwardLanes == 0 || edge.ForwardLanes == 0) continue;
                var path = source.Path(edge); var at = path.Position(path.ClosestParameter(World(lead)));
                int target = -1;
                foreach (int id in new[] { edge.a, edge.b })
                    if (Vector2.Distance(Geo.XZ(source.Node(id).position), Geo.XZ(at)) < 12f) { target = id; at = source.Node(id).position; break; }
                if (Inside(Local(at))) continue;
                float distance = Vector2.Distance(Geo.XZ(entrance), Geo.XZ(at));
                if (distance > FlyingSchoolLayout.MaximumConnection || distance < 6f) continue;
                int atNode = target >= 0 ? target : edge.a;
                if (!outside.Contains(atNode) && !HasOutsideRoute(source, atNode)) continue;
                candidates.Add((at, target, distance));
            }
            foreach (var candidate in candidates.OrderBy(c => c.cost))
            {
                var target = Local(candidate.point);
                var alternatives = new List<List<Vector2>>
                {
                    new List<Vector2> { localEntrance, lead, target },
                    new List<Vector2> { localEntrance, lead, new Vector2(leadX, back), new Vector2(target.x, back), target },
                    new List<Vector2> { localEntrance, lead, new Vector2(leadX, back), new Vector2(-leadX, back), target },
                };
                foreach (var local in alternatives.Select(Compact).Where(p => SafePath(p, Inside, localEntrance, s)).OrderBy(PathLength))
                {
                    float length = PathLength(local);
                    if (length > FlyingSchoolLayout.MaximumConnection * 1.8f ||
                        Mathf.Abs(elevation - candidate.point.y) / Mathf.Max(length, .01f) > PublicRoadConstruction.MaximumGroundGrade + .0001f)
                    { problem = "Give the school's driveway a longer, level approach (maximum 12% grade)."; continue; }
                    var draft = source.Clone(); var made = new List<int>();
                    int port = draft.AddNode(entrance, FlyingSchoolLayout.EntranceId(site.SiteId)), previous = port;
                    float along = 0f; bool valid = true;
                    for (int i = 1; i < local.Count; i++)
                    {
                        along += Vector2.Distance(local[i - 1], local[i]);
                        var point = Geo.X0Z(World(local[i]), Mathf.Lerp(elevation, candidate.point.y, along / length));
                        int next = i == local.Count - 1 && candidate.node >= 0 ? candidate.node : draft.AddNode(point);
                        if (!draft.Connect(previous, next, PublicRoadStyle.AirportAccess, RoadStructure.Ground, out var why, created: ids => made.AddRange(ids)))
                        { problem = why; valid = false; break; }
                        previous = next;
                    }
                    if (!valid) continue;
                    if (source.nodes.Any(n => draft.Node(n.id) == null || draft.Node(n.id).position != n.position))
                    { problem = "Move the school away from an existing road bend; connecting here would move an authored road node."; continue; }
                    if (!draft.Validate(out problem)) continue;
                    if (!HasOutsideRoute(draft, port)) { problem = "The driveway must connect to the region in both directions."; continue; }
                    var ids = made.Distinct().Where(id => draft.Segment(id) != null).ToArray();
                    float built = ids.Sum(id => draft.Path(draft.Segment(id)).Length);
                    plan = new RoadPlan { Draft = draft, EntranceNode = port, NewSegments = ids, Length = built,
                        Price = Mathf.CeilToInt(built * RoadTypes.Of(PublicRoadStyle.AirportAccess).pricePerMetre) };
                    problem = null; return true;
                }
            }
            return false;
        }

        static List<Vector2> Compact(List<Vector2> source)
        {
            var result = new List<Vector2>();
            foreach (var p in source) if (result.Count == 0 || Vector2.Distance(result[result.Count - 1], p) > 1f) result.Add(p);
            for (int i = result.Count - 2; i > 0; i--)
                if (Vector2.Angle(result[i] - result[i - 1], result[i + 1] - result[i]) < 1f) result.RemoveAt(i);
            return result;
        }

        static bool SafePath(List<Vector2> path, Func<Vector2, bool> inside, Vector2 entrance, int side)
        {
            if (path.Count < 2) return false;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1]; var b = path[i];
                if (i > 1 && Vector2.Angle(a - path[i - 2], b - a) > 135f) return false;
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int k = 0; k <= count; k++)
                {
                    var p = Vector2.Lerp(a, b, k / (float)count);
                    // The first leg leaves the car park straight out of its aisle.
                    bool mouth = i == 1 && (p.x - entrance.x) * side >= -.1f && Mathf.Abs(p.y - entrance.y) < .1f;
                    if (inside(p) && !mouth) return false;
                }
            }
            return true;
        }

        static float PathLength(List<Vector2> points)
        { float length = 0; for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]); return length; }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Live session money for flying schools: lesson fees in, instructors' wages out.</summary>
    public static class FlyingSchoolAccounts
    {
        public static long Advance(Airport airport, CareerState state)
        {
            if (airport == null || state == null) return 0;
            long net = 0;
            foreach (var school in airport.FlyingSchools) net += school.TakeNet();
            if (state.mode == GameMode.Sandbox || net == 0) return 0;
            state.funds = (int)Math.Max(0L, Math.Min(100000000L, state.funds + net));
            return net;
        }
    }
}
