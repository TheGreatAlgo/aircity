using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Transient service lanes and road mouths. Permanent truck parking is painted separately.</summary>
    static class DeicingSupplyMarkings
    {
        public static void Build(PaintBuilder paint, DeicingWorkArea area, bool perimeter = true)
        {
            if (perimeter)
            {
                foreach (float side in new[] { -1f, 1f })
                    paint.Line(area.Point(side * DeicingWorkArea.Width * .5f, -DeicingWorkArea.Length * .5f),
                        area.Point(side * DeicingWorkArea.Width * .5f, DeicingWorkArea.Length * .5f), .075f, PaintBuilder.White);
                // The service-road mouths stay open; only the central island has a transverse boundary.
                foreach (float end in new[] { -1f, 1f })
                    paint.Line(area.Point(-2.75f, end * DeicingWorkArea.Length * .5f),
                        area.Point(2.75f, end * DeicingWorkArea.Length * .5f), .075f, PaintBuilder.White);
            }
            ServiceLane(paint, area.FillCentre, area.Axis, "FILL");
            ServiceLane(paint, area.UnloadCentre, -area.Axis, "UNLOAD");
            int index = 0;
            foreach (var port in area.RoadPorts)
            {
                var relative = port - area.position;
                float x = Vector2.Dot(relative, area.Right), z = Vector2.Dot(relative, area.Axis);
                var inward = Mathf.Abs(x) / DeicingWorkArea.Width > Mathf.Abs(z) / DeicingWorkArea.Length
                    ? area.Right * -Mathf.Sign(x) : area.Axis * -Mathf.Sign(z);
                var right = Geo.Right(inward);
                float half = RunwayRules.RoadWidth * .5f;
                // Open-ended guide strokes identify the snap mouth without drawing pavement outside the footprint.
                paint.Line(port - right * half, port - right * half + inward * 2f, .10f, PaintBuilder.White);
                paint.Line(port + right * half, port + right * half + inward * 2f, .10f, PaintBuilder.White);
                bool entry = index++ % 2 == 0;
                Arrow(paint, port + inward * (entry ? 1.9f : .4f), entry ? inward : -inward, PaintBuilder.White, .65f);
            }
        }

        static void ServiceLane(PaintBuilder paint, Vector2 centre, Vector2 axis, string label)
        {
            var right = Geo.Right(axis);
            const float halfWidth = 2.25f, halfLength = 6.5f;
            foreach (float side in new[] { -1f, 1f })
            {
                var a = centre + right * (side * halfWidth) - axis * halfLength;
                var b = centre + right * (side * halfWidth) + axis * halfLength;
                paint.Dashes(a, b, .065f, 1.6f, .75f, PaintBuilder.Yellow);
            }
            // No numbered bays, end bars or parking symbols: these are drive-through service positions.
            var labelAt = centre - axis * (halfLength + 1.1f);
            paint.Text(labelAt, axis, label, .85f, PaintBuilder.White, 1.25f);
            Arrow(paint, centre + axis * (halfLength - .8f), axis, PaintBuilder.Yellow, .65f);
        }

        static void Arrow(PaintBuilder paint, Vector2 tip, Vector2 axis, Color colour, float size)
        {
            var right = Geo.Right(axis);
            paint.Line(tip - axis * size * 1.8f, tip, .09f, colour);
            paint.Line(tip - axis * size - right * size * .65f, tip, .09f, colour);
            paint.Line(tip - axis * size + right * size * .65f, tip, .09f, colour);
        }
    }
}
