using System.Collections.Generic;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>What lies outside a road's outer lanes: a gravel or paved shoulder (country roads, highways), or a kerb
    /// and sidewalk (streets).</summary>
    public enum RoadEdge { Shoulder, Kerb }

    /// <summary>What separates the two directions: nothing (a painted centre line), a raised kerbed island, or a
    /// concrete barrier (highways).</summary>
    public enum RoadMedian { None, Raised, Barrier }

    /// <summary>The centre line of an undivided two-way road: dashed (passing allowed) or double solid.</summary>
    public enum CentreLine { Dashed, DoubleSolid }

    /// <summary>
    /// One kind of public road: its lanes, what separates and edges them, and how it is drawn and joined. Cross-section
    /// from left to right (looking along a segment from its node a to b; traffic keeps right):
    /// [outer shoulder][backward lanes][inner shoulder][median][inner shoulder][forward lanes][outer shoulder];
    /// a one-way road has only [inner shoulder][forward lanes][outer shoulder]. Paved shoulders are part of the
    /// carriageway (Width); a sidewalk or gravel verge lies outside it.
    /// </summary>
    public readonly struct RoadType
    {
        public readonly PublicRoadStyle style;
        public readonly string name, tip;
        public readonly int forward, backward;
        public readonly float laneWidth, median, innerShoulder, outerShoulder, verge, speed, curveRadius, kerbRadius, pricePerMetre;
        public readonly RoadEdge edge;
        public readonly RoadMedian medianKind;
        public readonly CentreLine centre;
        /// <summary>False for highways: they cross other roads only on bridges, and join them by ramps.</summary>
        public readonly bool atGrade;

        public RoadType(PublicRoadStyle style, string name, string tip, int forward, int backward, float laneWidth, float median,
            RoadMedian medianKind, float innerShoulder, float outerShoulder, RoadEdge edge, float verge, CentreLine centre,
            float speed, float curveRadius, float kerbRadius, float pricePerMetre, bool atGrade = true)
        {
            this.style = style; this.name = name; this.tip = tip; this.forward = forward; this.backward = backward;
            this.laneWidth = laneWidth; this.median = median; this.medianKind = medianKind; this.innerShoulder = innerShoulder;
            this.outerShoulder = outerShoulder; this.edge = edge; this.verge = verge; this.centre = centre; this.speed = speed;
            this.curveRadius = curveRadius; this.kerbRadius = kerbRadius; this.pricePerMetre = pricePerMetre; this.atGrade = atGrade;
        }

        public bool OneWay => backward == 0;
        public bool Divided => medianKind != RoadMedian.None;
        public int Lanes => forward + backward;
        /// <summary>Kerb to kerb (or shoulder edge to shoulder edge): lanes, paved shoulders and median.</summary>
        public float Width => OneWay ? innerShoulder + forward * laneWidth + outerShoulder
            : 2f * outerShoulder + Lanes * laneWidth + (Divided ? median + 2f * innerShoulder : 0f);
        /// <summary>Width including the sidewalks or verges either side.</summary>
        public float FullWidth => Width + 2f * verge;

        /// <summary>The lanes across the road, left to right looking from node a to b: each lane's centre offset (positive
        /// to the right) and whether it carries traffic forwards (a to b).</summary>
        public List<(float x, bool forwards)> LaneCentres()
        {
            var list = new List<(float, bool)>();
            float x = -Width * .5f;
            if (OneWay)
            {
                x += innerShoulder;
                for (int i = 0; i < forward; i++) { list.Add((x + laneWidth * .5f, true)); x += laneWidth; }
                return list;
            }
            x += outerShoulder;
            for (int i = 0; i < backward; i++) { list.Add((x + laneWidth * .5f, false)); x += laneWidth; }
            if (Divided) x += 2f * innerShoulder + median;
            for (int i = 0; i < forward; i++) { list.Add((x + laneWidth * .5f, true)); x += laneWidth; }
            return list;
        }

        /// <summary>The edges of the lanes running one way (forwards: a to b), from the road's centre side to its kerb
        /// side: offsets from the centreline, right positive.</summary>
        public (float inner, float outer) Carriageway(bool forwards)
        {
            float half = Width * .5f;
            if (OneWay) return forwards ? (-half + innerShoulder, half - outerShoulder) : (0f, 0f);
            float inner = Divided ? median * .5f + innerShoulder : 0f;
            return forwards ? (inner, half - outerShoulder) : (-inner, -(half - outerShoulder));
        }
    }

    /// <summary>The public road types (one per PublicRoadStyle). Saved roads store only their style.</summary>
    public static class RoadTypes
    {
        static readonly RoadType[] all =
        {
            new RoadType(PublicRoadStyle.Rural, "Country road", "Two lanes, gravel shoulders, dashed centre line",
                1, 1, 3.25f, 0f, RoadMedian.None, 0f, 0f, RoadEdge.Shoulder, 1.2f, CentreLine.Dashed, 60f, 60f, 8f, 9f),
            new RoadType(PublicRoadStyle.AirportAccess, "Airport access road", "Two lanes, gravel shoulders",
                1, 1, 3.25f, 0f, RoadMedian.None, 0f, 0f, RoadEdge.Shoulder, 1.2f, CentreLine.Dashed, 50f, 25f, 8f, 9f),
            new RoadType(PublicRoadStyle.Urban, "Two-lane street", "Two lanes, kerbs and sidewalks, double centre line",
                1, 1, 3.5f, 0f, RoadMedian.None, 0f, .3f, RoadEdge.Kerb, 2f, CentreLine.DoubleSolid, 50f, 25f, 8f, 14f),
            new RoadType(PublicRoadStyle.Highway, "Highway", "Four lanes, barrier and shoulders; crosses other roads by bridge, joins them by ramps",
                2, 2, 3.75f, .8f, RoadMedian.Barrier, 1f, 2.5f, RoadEdge.Shoulder, 1.5f, CentreLine.DoubleSolid, 100f, 150f, 12f, 40f, false),
            new RoadType(PublicRoadStyle.Ramp, "Ramp", "One lane one way, paved shoulders: joins highways",
                1, 0, 3.75f, 0f, RoadMedian.None, 1f, 1.5f, RoadEdge.Shoulder, 1f, CentreLine.Dashed, 50f, 45f, 10f, 14f),
            new RoadType(PublicRoadStyle.Avenue, "Four-lane avenue", "Two lanes each way, a raised median, kerbs and sidewalks",
                2, 2, 3.5f, 2f, RoadMedian.Raised, .3f, .3f, RoadEdge.Kerb, 2.5f, CentreLine.DoubleSolid, 60f, 60f, 10f, 26f),
            new RoadType(PublicRoadStyle.Boulevard, "Six-lane boulevard", "Three lanes each way, a wide raised median, kerbs and sidewalks",
                3, 3, 3.5f, 4f, RoadMedian.Raised, .3f, .3f, RoadEdge.Kerb, 3f, CentreLine.DoubleSolid, 70f, 90f, 12f, 38f),
            new RoadType(PublicRoadStyle.OneWay1, "One-way street", "One lane one way, kerbs and sidewalks: terminal loops",
                1, 0, 3.75f, 0f, RoadMedian.None, .5f, .5f, RoadEdge.Kerb, 2f, CentreLine.Dashed, 40f, 20f, 8f, 10f),
            new RoadType(PublicRoadStyle.OneWay2, "Two-lane one-way", "Two lanes one way, kerbs and sidewalks: kerbside and terminal loops",
                2, 0, 3.5f, 0f, RoadMedian.None, .5f, .5f, RoadEdge.Kerb, 2.5f, CentreLine.Dashed, 40f, 30f, 8f, 16f),
            new RoadType(PublicRoadStyle.OneWay3, "Three-lane one-way", "Three lanes one way, kerbs and sidewalks: busy terminal frontage",
                3, 0, 3.5f, 0f, RoadMedian.None, .5f, .5f, RoadEdge.Kerb, 3f, CentreLine.Dashed, 40f, 40f, 10f, 22f),
        };

        public static RoadType Of(PublicRoadStyle style)
        {
            int i = (int)style;
            return i >= 0 && i < all.Length ? all[i] : all[0];
        }

        /// <summary>The types a player builds (the access road and ramps are made by their own tools).</summary>
        public static readonly PublicRoadStyle[] Buildable =
        {
            PublicRoadStyle.Urban, PublicRoadStyle.Avenue, PublicRoadStyle.Boulevard, PublicRoadStyle.OneWay1, PublicRoadStyle.OneWay2,
            PublicRoadStyle.OneWay3, PublicRoadStyle.Rural, PublicRoadStyle.Highway,
        };
    }
}
