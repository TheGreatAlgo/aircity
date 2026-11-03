using System;
using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// A terminal building (Airport.terminals): a footprint of corners whose edges may bow out into arcs (a pier's
    /// rounded end, a half-circle satellite), flat floors and a flat or profiled roof. It knows nothing of the stands: a
    /// stand's jet bridges reach it by their fixed links (BridgeLayout), so reshaping a terminal never moves a stand
    /// or a bridge, it only connects or disconnects them.
    ///
    /// Only the layout is saved; Building/TerminalBuilder makes the walls and roof. No interiors yet.
    /// </summary>
    [Serializable]
    public class Terminal
    {
        [Tooltip("Corners, counter-clockwise from above (Airport space, x z).")]
        public List<Vector2> corners = new List<Vector2>();
        [Tooltip("How far each edge (corner i to the next) bows out from straight at its middle, metres (negative: " +
                 "in). Half the edge's length makes a half circle, the most allowed.")]
        public List<float> bulge = new List<float>();
        [Range(1, MaxFloors)] public int floors = 2;
        [Tooltip("Individual storey heights, bottom to top. Missing entries use the standard height.")]
        public List<float> storeyHeights = new List<float>();
        public BuildingExterior exterior = new BuildingExterior();
        [Tooltip("Boarding corridors connect at the floor nearest this height, metres above the apron (BoardingLevel).")]
        [Min(0.5f)] public float boardingHeight = 4f;

        public const int MaxFloors = 12;
        /// <summary>Upper storey height (metres): tall, open airport floors.</summary>
        public const float FloorHeight = 5f;
        /// <summary>Ground storey height (metres): puts the first floor at a jet bridge's standard floor, so its link
        /// runs level from the terminal to the rotunda.</summary>
        public const float GroundStorey = JetBridgeRules.RotundaFloor;
        /// <summary>Each floor's slab band on the facade ends this far below the floor line, where a boarding corridor's
        /// underside is (JetBridgeRules.LinkBottom): a corridor arriving at a floor sits on the band, and the storey
        /// above's glass starts level with the corridor, so it reads as walking straight in.</summary>
        public const float BandBelowFloor = -JetBridgeRules.LinkBottom;
        /// <summary>The roof parapet stands this far above the top floor's ceiling.</summary>
        public const float Parapet = 1.1f;
        /// <summary>Arcs are drawn and tested as straight pieces no longer than this (metres).</summary>
        public const float ArcStep = 2f;

        /// <summary>Height of an individual storey.</summary>
        public float StoreyHeight(int floor) => floor < storeyHeights.Count ? storeyHeights[floor] : DefaultStoreyHeight(floor);
        public static float DefaultStoreyHeight(int floor) => floor == 0 ? GroundStorey : FloorHeight;
        public float FloorElevation(int floor)
        {
            float height = 0f;
            for (int i = 0; i < Mathf.Min(floor, floors); i++) height += StoreyHeight(i);
            return height;
        }
        public void SetStoreyHeight(int floor, float height)
        {
            while (storeyHeights.Count <= floor) storeyHeights.Add(DefaultStoreyHeight(storeyHeights.Count));
            storeyHeights[floor] = height;
        }

        /// <summary>
        /// The floor boarding corridors arrive at: of the floors above the ground, the one nearest `requested` (a
        /// bridge's or head's own connection height), or the terminal's boarding height when that is 0. Corridors
        /// always meet a floor, never part-way up a storey's glass. A one-storey building has no floor above the
        /// ground, so it keeps the height asked for.
        /// </summary>
        public float BoardingLevel(float requested = 0f) => BoardingFloor(requested) is int f && f > 0 ? FloorElevation(f) :
            requested > 0f ? requested : boardingHeight;

        /// <summary>The floor (1 to floors - 1) BoardingLevel picks; 0 for a one-storey building.</summary>
        public int BoardingFloor(float requested = 0f)
        {
            if (floors < 2) return 0;
            float want = requested > 0f ? requested : boardingHeight;
            int best = 1;
            for (int f = 2; f < floors; f++)
                if (Mathf.Abs(FloorElevation(f) - want) < Mathf.Abs(FloorElevation(best) - want)) best = f;
            return best;
        }

        /// <summary>Board at a floor (1 to floors - 1).</summary>
        public void SetBoardingFloor(int floor)
        {
            if (floors >= 2) boardingHeight = FloorElevation(Mathf.Clamp(floor, 1, floors - 1));
        }
        /// <summary>Base ceiling elevation. Curved roofs rise above it without changing boarding levels.</summary>
        public float RoofHeight => FloorElevation(floors);
        /// <summary>Maximum envelope height, including roof crests and the parapet.</summary>
        public float Height => RoofHeight + exterior.parapet + exterior.roofShape.MaxRise;
        public int EdgeCount => corners.Count;

        public float Bulge(int edge) => edge >= 0 && edge < bulge.Count ? bulge[edge] : 0f;

        /// <summary>Set an edge's bulge, limited to a half circle either way.</summary>
        public void SetBulge(int edge, float value)
        {
            while (bulge.Count < corners.Count) bulge.Add(0f);
            float half = Vector2.Distance(corners[edge], corners[(edge + 1) % corners.Count]) * 0.5f;
            bulge[edge] = Mathf.Clamp(value, -half, half);
        }

        /// <summary>A terminal with these corners (either way round: stored counter-clockwise), all edges straight.</summary>
        public static Terminal Polygon(IEnumerable<Vector2> corners, int floors = 2)
        {
            var t = new Terminal { corners = new List<Vector2>(corners), floors = Mathf.Clamp(floors, 1, MaxFloors) };
            if (Geo.SignedArea(t.corners) < 0f) t.corners.Reverse();
            t.bulge = new List<float>(new float[t.corners.Count]);
            return t;
        }

        /// <summary>The middle of an edge's curve (its chord's middle when straight).</summary>
        public Vector2 EdgeMiddle(int edge)
        {
            var a = corners[edge];
            var b = corners[(edge + 1) % corners.Count];
            var d = b - a;
            float len = d.magnitude;
            var mid = (a + b) * 0.5f;
            return len < 1e-4f ? mid : mid + Geo.Right(d / len) * Bulge(edge);
        }

        /// <summary>
        /// The outline with every arc broken into straight pieces (at most ArcStep long), counter-clockwise from above,
        /// corners included. An edge bowing out by h over a chord c is the circle through both corners and the point h
        /// out from the chord's middle: its turn is 4 atan(2h / c).
        /// </summary>
        public List<Vector2> Outline(float step = ArcStep)
            => OutlineWithFaces(out _, step);

        public List<Vector2> OutlineWithFaces(out List<int> faces, float step = ArcStep)
        {
            var list = new List<Vector2>();
            faces = new List<int>();
            for (int i = 0; i < corners.Count; i++)
            {
                list.Add(corners[i]);
                Arc(list, corners[i], corners[(i + 1) % corners.Count], Bulge(i), step);
                while (faces.Count < list.Count) faces.Add(i);
            }
            return list;
        }

        /// <summary>Mitred facade section at a height, shared by mesh generation and boarding connections.</summary>
        public List<Vector2> WallOutline(float height)
        {
            var outline = OutlineWithFaces(out var faces);
            return WallOutline(outline, faces, height);
        }

        internal List<Vector2> WallOutline(List<Vector2> outline, List<int> faces, float height)
        {
            if (height == 0f || !exterior.HasLean) return outline;
            var offsets = WallOffsets(outline, faces);
            var result = new List<Vector2>(outline.Count);
            for (int i = 0; i < outline.Count; i++) result.Add(outline[i] + offsets[i] * height);
            return result;
        }

        internal List<Vector2> WallOffsets(List<Vector2> outline, List<int> faces)
        {
            var result = new List<Vector2>(outline.Count);
            for (int i = 0; i < outline.Count; i++)
            {
                int previous = (i + outline.Count - 1) % outline.Count;
                var d0 = (outline[i] - outline[previous]).normalized;
                var d1 = (outline[(i + 1) % outline.Count] - outline[i]).normalized;
                result.Add(JoinOffset(Geo.Right(d0), Geo.Right(d1), exterior.ForFace(faces[previous]).Outset(1f), exterior.ForFace(faces[i]).Outset(1f)));
            }
            return result;
        }

        // Solve the offset in a frame at the vertex, never by intersecting nearly parallel lines in world space.
        internal static Vector2 JoinOffset(Vector2 n0, Vector2 n1, float offset0, float offset1)
        {
            float dot = Mathf.Clamp(Vector2.Dot(n0, n1), -1f, 1f);
            if (Mathf.Abs(offset0 - offset1) < 0.00001f)
                return (n0 + n1) * ((offset0 + offset1) * 0.5f / Mathf.Max(0.25f, 1f + dot));
            float cross = Geo.Cross(n0, n1);
            if (dot > 0.94f || Mathf.Abs(cross) < 0.0001f) return (n0 * offset0 + n1 * offset1) * 0.5f;
            return new Vector2(n1.y * offset0 - n0.y * offset1, n0.x * offset1 - n1.x * offset0) / cross;
        }

        internal static List<Vector2> OffsetOutline(IList<Vector2> outline, float distance)
        {
            var result = new List<Vector2>(outline.Count);
            for (int i = 0; i < outline.Count; i++)
            {
                var n0 = Geo.Right((outline[i] - outline[(i + outline.Count - 1) % outline.Count]).normalized);
                var n1 = Geo.Right((outline[(i + 1) % outline.Count] - outline[i]).normalized);
                result.Add(outline[i] + JoinOffset(n0, n1, distance, distance));
            }
            return result;
        }

        /// <summary>Horizontal projection of the complete envelope, preserving concave footprints.</summary>
        public IEnumerable<List<Vector2>> Footprints()
        {
            var ground = Outline(); yield return ground;
            IEnumerable<List<Vector2>> Sweep(List<Vector2> a, List<Vector2> b)
            {
                yield return a; yield return b;
                for (int i = 0; i < a.Count; i++)
                {
                    int next = (i + 1) % a.Count;
                    var face = new List<Vector2> { a[i], a[next], b[next], b[i] };
                    if (Mathf.Abs(Geo.SignedArea(face)) > 0.001f) yield return face;
                }
            }
            if (exterior.HasLean) foreach (var part in Sweep(ground, WallOutline(Height))) yield return part;
            if (exterior.RoofOverhang > 0f)
                foreach (var part in Sweep(OffsetOutline(WallOutline(RoofHeight - 0.25f), exterior.RoofOverhang + 0.08f),
                    OffsetOutline(WallOutline(Height), exterior.RoofOverhang + 0.08f))) yield return part;
            if (exterior.ColumnReach > 0f) yield return OffsetOutline(ground, exterior.ColumnReach);
        }

        public string EnvelopeProblem()
        {
            if (floors < 1 || floors > MaxFloors || exterior == null || !exterior.Valid(EdgeCount)) return "invalid building profile";
            for (int i = 0; i < floors; i++)
                if (!BuildingFacade.Finite(StoreyHeight(i)) || StoreyHeight(i) < 2f || StoreyHeight(i) > 40f) return "storey height must be 2–40 m";
            if (!BuildingFacade.Finite(boardingHeight) || BoardingLevel() < 0.5f || BoardingLevel() > RoofHeight - 0.5f) return "boarding level is outside the building";
            var ground = Outline(); var top = WallOutline(Height);
            if (Geo.SelfIntersects(top) || Geo.SignedArea(top) < 1f) return "angled facades cross";
            for (int i = 0; i < ground.Count; i++)
            {
                int j = (i + 1) % ground.Count;
                if (Vector2.Dot(top[j] - top[i], ground[j] - ground[i]) <= 0f) return "angled facade collapses an edge";
            }
            if (exterior.RoofOverhang > 0f)
            {
                var roof = OffsetOutline(top, exterior.RoofOverhang + 0.08f);
                if (Geo.SelfIntersects(roof) || Geo.SignedArea(roof) < 1f) return "roof overhang crosses the building outline";
            }
            if (exterior.roofShape.MaxRise > 0f && exterior.HasLean)
            {
                var profile = exterior.roofShape.For(this);
                var outline = OutlineWithFaces(out var faces);
                foreach (var lean in WallOffsets(outline, faces))
                    if (profile.MaxSlope * Mathf.Abs(Vector2.Dot(lean, profile.axis)) >= 0.95f)
                        return "roof is too steep for this facade angle; reduce its rise or change direction";
            }
            return null;
        }

        /// <summary>Points strictly between a and b along an edge bowing out (to the right of a to b) by h.</summary>
        public static void Arc(List<Vector2> into, Vector2 a, Vector2 b, float h, float step)
        {
            var d = b - a;
            float c = d.magnitude;
            if (Mathf.Abs(h) < 0.05f || c < 0.5f) return;
            var n = Geo.Right(d / c);
            float r = (c * c * 0.25f + h * h) / (2f * Mathf.Abs(h));
            var centre = (a + b) * 0.5f + n * (h - Mathf.Sign(h) * r);
            float turn = 4f * Mathf.Atan(2f * h / c);
            int pieces = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(turn) * r / Mathf.Max(step, 0.1f)));
            float a0 = Geo.Angle(a - centre);
            for (int k = 1; k < pieces; k++)
            {
                float ang = a0 + turn * k / pieces;
                into.Add(centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
            }
        }

        public float Area => Mathf.Abs(Geo.SignedArea(Outline()));

        /// <summary>Is a ground point (Airport space, x z) inside the building?</summary>
        public bool Contains(Vector2 p) => corners.Count >= 3 && Geo.Inside(Outline(), p);
        public bool ContainsAtHeight(Vector2 p, float height) => height >= 0f && height <= Height && corners.Count >= 3 && Geo.Inside(WallOutline(height), p);

        /// <summary>The nearest point of the outer wall to a ground point, and the wall's outward direction there.</summary>
        public float ClosestOnWall(Vector2 p, out Vector2 onWall, out Vector2 outward)
            => ClosestOnWall(p, BoardingLevel(), out onWall, out outward);

        public float ClosestOnWall(Vector2 p, float height, out Vector2 onWall, out Vector2 outward)
        {
            var o = WallOutline(height);
            float best = float.MaxValue;
            onWall = p;
            outward = Vector2.up;
            for (int i = 0; i < o.Count; i++)
            {
                var a = o[i];
                var b = o[(i + 1) % o.Count];
                var q = Geo.ClosestOnSegment(p, a, b, out _);
                float dist = Vector2.Distance(p, q);
                if (dist >= best || (b - a).sqrMagnitude < 1e-8f) continue;
                best = dist;
                onWall = q;
                outward = Geo.Right((b - a).normalized);
            }
            return best;
        }

        /// <summary>A copy (for editing a terminal and putting it back if the change won't do).</summary>
        public Terminal Clone() => new Terminal
            { corners = new List<Vector2>(corners), bulge = new List<float>(bulge), floors = floors, boardingHeight = boardingHeight,
                storeyHeights = new List<float>(storeyHeights), exterior = exterior.Clone() };
    }
}
