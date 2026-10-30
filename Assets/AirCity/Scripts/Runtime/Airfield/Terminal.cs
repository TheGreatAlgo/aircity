using System;
using System.Collections.Generic;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>
    /// A terminal building (Airport.terminals): a footprint of corners whose edges may bow out into arcs (a pier's
    /// rounded end, a half-circle satellite), a number of floors and a flat roof. It knows nothing of the stands: a
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

        public const int MaxFloors = 4;
        /// <summary>Storey height (metres): tall, open airport floors.</summary>
        public const float FloorHeight = 5f;
        /// <summary>The roof parapet stands this far above the top floor's ceiling.</summary>
        public const float Parapet = 1.1f;
        /// <summary>Arcs are drawn and tested as straight pieces no longer than this (metres).</summary>
        public const float ArcStep = 2f;

        /// <summary>Height of the roof (the parapet stands above it).</summary>
        public float RoofHeight => floors * FloorHeight;
        public float Height => RoofHeight + Parapet;
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
        {
            var list = new List<Vector2>();
            for (int i = 0; i < corners.Count; i++)
            {
                list.Add(corners[i]);
                Arc(list, corners[i], corners[(i + 1) % corners.Count], Bulge(i), step);
            }
            return list;
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

        /// <summary>The nearest point of the outer wall to a ground point, and the wall's outward direction there.</summary>
        public float ClosestOnWall(Vector2 p, out Vector2 onWall, out Vector2 outward)
        {
            var o = Outline();
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
            { corners = new List<Vector2>(corners), bulge = new List<float>(bulge), floors = floors };
    }
}
