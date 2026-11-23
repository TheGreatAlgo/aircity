using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// The flying school's shared site frame. The origin lies on the centreline of the taxilane its flight line opens
    /// onto, in the middle of the line; +Z runs away from that taxilane, into the site: first the flight line (its
    /// tie-down stands), then the airside terrace, the clubhouse, the landside forecourt and the car park. The
    /// driveway leaves the car park's aisle on one side (`side`: +1 = local +X). Construction, previews, protected
    /// land, traffic and the generated art all use these dimensions, so nothing is derived from meshes.
    /// </summary>
    public static class FlyingSchoolLayout
    {
        public const string EntrancePrefix = "flying-school-";
        /// <summary>Tie-down spots on the flight line, and the largest aircraft they take.</summary>
        public const int Spots = 3;
        public const char Code = 'A';
        public const float Terrace = 5f, BuildingWidth = 26f, BuildingDepth = 11f, Forecourt = 4f;
        public const float LotHalfWidth = 15f, LotDepth = 18f, AisleWidth = 6.5f, BayLength = 5.5f, LotMargin = .25f;
        public const float ThroatLength = 4.7f, EntranceOffset = 2f;
        public const float ParkingSurface = .035f, WalkSurface = .075f, FloorHeight = .075f;
        public const float MaximumConnection = 95f;
        /// <summary>How far the driveway runs straight out of the car park before it turns towards the road, tried in
        /// this order: room for the access road's full bend radius (25 m) and a short straight at the mouth, or a stub.</summary>
        public static readonly float[] DrivewayLeads = { 29f, 12f };
        /// <summary>Half the width of the protected block behind the flight line (roof overhang and kerbs included).</summary>
        public const float BlockHalfWidth = 16f;

        /// <summary>Distance between neighbouring spots' centrelines: a stand of this code beside another.</summary>
        public static float SpotPitch => RunwayRules.MaxWingspan(Code) + RunwayRules.StandClearance(Code);
        public static float ApronHalfWidth => SpotPitch * Spots * .5f;
        public static float SpotOffset(int index) => (index - (Spots - 1) * .5f) * SpotPitch;

        /// <summary>The taxilane width the flight line is laid out for: never narrower than its own code needs.</summary>
        public static float Lane(float requested) =>
            Mathf.Max(float.IsNaN(requested) ? 0f : requested, RunwayRules.TaxiwayWidthFor(Code));

        /// <summary>From the taxilane centreline to the far end of the stands: where the terrace starts.</summary>
        public static float ApronDepth(float lane) => RunwayRules.TaxilaneClearance(Lane(lane)) +
            RunwayRules.StandLength(Code) + RunwayRules.StandClearance(Code);

        public static float BuildingFront(float lane) => ApronDepth(lane) + Terrace;
        public static float BuildingBack(float lane) => BuildingFront(lane) + BuildingDepth;
        public static float BuildingCentre(float lane) => BuildingFront(lane) + BuildingDepth * .5f;
        public static float LotFront(float lane) => BuildingBack(lane) + Forecourt;
        public static float LotBack(float lane) => LotFront(lane) + LotDepth;
        public static float AisleCentre(float lane) => LotFront(lane) + LotMargin + BayLength + AisleWidth * .5f;
        public static Vector2 LocalEntrance(float lane, int side) =>
            new Vector2(Side(side) * (LotHalfWidth + EntranceOffset), AisleCentre(lane));
        public static int Side(int side) => side < 0 ? -1 : 1;
        public static string EntranceId(string siteId) => EntrancePrefix + siteId;
        public static bool IsEntrance(string terminalEntrance) =>
            terminalEntrance != null && terminalEntrance.StartsWith(EntrancePrefix, System.StringComparison.Ordinal);

        public static Vector2 Axis(float heading) => new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
        public static Vector2 Point(Vector2 origin, float heading, Vector2 local)
        { var axis = Axis(heading); return origin + Geo.Right(axis) * local.x + axis * local.y; }
        public static Vector2 Local(Vector2 origin, float heading, Vector2 point)
        { var axis = Axis(heading); var delta = point - origin; return new Vector2(Vector2.Dot(delta, Geo.Right(axis)), Vector2.Dot(delta, axis)); }
        public static Vector2[] Rectangle(Vector2 origin, float heading, float x0, float z0, float x1, float z1) => new[]
        {
            Point(origin, heading, new Vector2(Mathf.Min(x0, x1), z0)), Point(origin, heading, new Vector2(Mathf.Max(x0, x1), z0)),
            Point(origin, heading, new Vector2(Mathf.Max(x0, x1), z1)), Point(origin, heading, new Vector2(Mathf.Min(x0, x1), z1)),
        };

        public readonly struct ParkingBay
        {
            public readonly Vector2 centre;
            public readonly float width, length;
            public readonly bool accessible, frontRow;
            public ParkingBay(float x, float z, bool accessible, bool front)
            { centre = new Vector2(x, z); width = accessible ? 3.2f : 2.6f; length = BayLength; this.accessible = accessible; frontRow = front; }
        }

        /// <summary>Sixteen bays either side of the aisle; the two wider accessible bays flank the path to the door.</summary>
        public static IReadOnlyList<ParkingBay> ParkingBays(float lane)
        {
            var bays = new List<ParkingBay>();
            float front = LotFront(lane) + LotMargin + BayLength * .5f, back = LotBack(lane) - LotMargin - BayLength * .5f;
            foreach (float x in new[] { -3.3f, 3.3f }) bays.Add(new ParkingBay(x, front, true, true));
            foreach (float x in new[] { 6.3f, 8.9f, 11.5f }) { bays.Add(new ParkingBay(-x, front, false, true)); bays.Add(new ParkingBay(x, front, false, true)); }
            for (int i = 0; i < 8; i++) bays.Add(new ParkingBay(-9.1f + i * 2.6f, back, false, false));
            return bays;
        }

        /// <summary>A stand of the flight line as it would be framed for this site: spot `index`, Airport space.</summary>
        public static Stand SpotStand(Vector2 origin, float heading, float lane, PavementSurface surface, int index)
        {
            var axis = Axis(heading);
            var stand = new Stand
            {
                kind = StandKind.Parking, code = SizeCodes.Of(Code), surface = surface, exit = StandExit.PushBack,
                position = Geo.X0Z(origin + Geo.Right(axis) * SpotOffset(index)), heading = Mathf.Repeat(heading, 360f),
            };
            stand.Frame(Geo.Right(axis), Lane(lane));
            return stand;
        }

        /// <summary>Convex protected regions: the flight line, the block behind it and the driveway throat.</summary>
        public static IEnumerable<Vector2[]> Footprints(Vector2 origin, float heading, float lane, int side, bool flightLine = true)
        {
            lane = Lane(lane);
            if (flightLine) yield return Rectangle(origin, heading, -ApronHalfWidth, lane * .5f, ApronHalfWidth, ApronDepth(lane));
            yield return Rectangle(origin, heading, -BlockHalfWidth, ApronDepth(lane), BlockHalfWidth, LotBack(lane) + .3f);
            yield return Throat(origin, heading, lane, side);
        }

        /// <summary>The block behind the flight line: terrace, clubhouse and car park (no flight line, no throat).</summary>
        public static Vector2[] Block(Vector2 origin, float heading, float lane) =>
            Rectangle(origin, heading, -BlockHalfWidth, ApronDepth(lane), BlockHalfWidth, LotBack(lane) + .3f);

        public static Vector2[] Throat(Vector2 origin, float heading, float lane, int side)
        {
            float aisle = AisleCentre(lane); int s = Side(side);
            return Rectangle(origin, heading, s * LotHalfWidth, aisle - AisleWidth * .5f, s * (LotHalfWidth + ThroatLength), aisle + AisleWidth * .5f);
        }

        /// <summary>Opaque ground only (no roof projection): what owns terrain under the block.</summary>
        public static IEnumerable<Vector2[]> PavedPolygons(Vector2 origin, float heading, float lane, int side)
        {
            lane = Lane(lane);
            float d = ApronDepth(lane);
            yield return Rectangle(origin, heading, -LotHalfWidth, d - .4f, LotHalfWidth, LotFront(lane));
            yield return Rectangle(origin, heading, -LotHalfWidth, LotFront(lane), LotHalfWidth, LotBack(lane));
            yield return Throat(origin, heading, lane, side);
        }

        /// <summary>Why this aircraft type cannot be a school trainer on this flight line, or null.</summary>
        public static string TrainerProblem(AircraftType type)
        {
            if (type == null) return "Choose a trainer aircraft type.";
            if (type.category != AircraftCategory.GeneralAviation || !type.Light) return "The school trains on light general aviation aircraft.";
            if (type.SizeCode > Code || type.Length > RunwayRules.StandLength(Code)) return "This aircraft does not fit the school's tie-downs.";
            return null;
        }

        public static IEnumerable<Vector2> Samples(IList<Vector2> polygon, float step = 3f)
        {
            foreach (var p in polygon) yield return p;
            var min = polygon.Aggregate(Vector2.Min); var max = polygon.Aggregate(Vector2.Max);
            for (float x = min.x; x <= max.x; x += step)
                for (float z = min.y; z <= max.y; z += step)
                { var p = new Vector2(x, z); if (Geo.Inside(polygon, p)) yield return p; }
        }
    }
}
