using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>
    /// Stand and de-icing bay pads and their paint, from their layouts (StandLayout):
    ///  - pad: from under the taxiway out to the far end in the stand's frame (AirfieldKind.Stand), so stands of one
    ///    angle side by side tile and their slab joints line up; grass and dirt get ragged edges and no paint;
    ///  - push-back stands: a lead-in line curving off the taxiway centreline (from both ways when the stand is
    ///    about square on), a stop bar for the nose gear, a red boundary round the parked aircraft, and at the
    ///    entrance the stand number (yellow) and the size code it takes (white);
    ///  - taxi-out stands: the hairpin lead-in round to the stand centreline, down it to the stop bar and on as the
    ///    lead-out turning onto the taxiway; boundary, number and code read by the pilot coming down the centreline;
    ///  - de-icing bays: red lines either side (the de-icing trucks' limit while an aircraft moves), the stop bar,
    ///    and the bay's id ("D1") and code beside the taxiway at its entrance.
    /// </summary>
    sealed class StandBuilder
    {
        const float LineHalfWidth = 0.075f, SafetyHalfWidth = 0.1f, StopBarHalfWidth = 0.2f, TextGap = 1.2f;

        public void Build(StandLayout l, SurfaceBuilder surface, PaintBuilder paint)
        {
            var s = l.stand;
            bool paved = s.surface.IsPaved();
            surface.Pad((int)s.surface, l.entry, l.axis, l.x0, l.x1, l.near0, l.near1, l.far, AirfieldHeights.Stand,
                AirfieldKind.Stand, paved ? 0f : SurfaceBuilder.Fringe, s.DeIcing);
            if (!paved) return;
            paint.frame = RunwayFrame.None;
            if (s.DeIcing) DeIcingPaint(l, paint);
            else if (l.TaxiOut) TaxiOutPaint(l, paint);
            else PushBackPaint(l, paint);
        }

        /// <summary>Stand numbers and code letters (metres tall), by code: readable from the cockpit, small on a
        /// light aircraft's stand.</summary>
        static float TextHeight(char code) => code switch
        {
            'A' => 2.4f,
            'B' => 3.2f,
            'C' => 4.5f,
            'D' => 5.5f,
            'E' => 6.5f,
            _ => 7f,
        };

        static float Pitch(float height) => height * 0.52f;

        static float TextWidth(string text, float height) => (text.Length - 1) * Pitch(height) + height / 3f;

        // ------------------------------------------------------------------ parking stands

        void PushBackPaint(StandLayout l, PaintBuilder paint)
        {
            char c = l.Code;
            float span = RunwayRules.MaxWingspan(c), clr = RunwayRules.StandClearance(c);
            float stopS = Vector2.Dot(l.stop - l.entry, l.axis);
            var yellow = PaintBuilder.Yellow;

            // lead-in: a turn of the stand's radius off the taxiway centreline; both ways when about square on
            float dot = Vector2.Dot(l.axis, l.along);
            var from = Mathf.Abs(dot) < 0.26f ? new[] { l.along, -l.along } : new[] { dot >= 0f ? l.along : -l.along };
            float straight = stopS;
            foreach (var h in from)
            {
                float r = RunwayRules.StandTurnRadius(c);
                float td = Geo.Fillet(l.entry, -h, l.axis, r, out _);
                float room = stopS - 3f;
                if (td > room) r *= Mathf.Max(room, 1f) / td;
                td = Geo.Fillet(l.entry, -h, l.axis, r, out var centre);
                var arc = new List<Vector2>();
                Geo.Arc(arc, centre, l.entry - h * td, l.entry + l.axis * td, 1f);
                paint.Strip(OffUnpaved(l, arc), LineHalfWidth, yellow);
                straight = Mathf.Min(straight, td);
            }
            paint.Strip(OffUnpaved(l, new List<Vector2> { l.entry + l.axis * straight, l.stop }), LineHalfWidth, yellow);
            StopBar(l, paint, span);

            float half = span * 0.5f + clr * 0.25f;
            float rear = l.tailS - clr * 0.25f, front = l.noseS + clr * 0.25f;
            Boundary(l, paint, -half, half, rear, front);

            // number and code at the entrance, read coming in (the captain's side gets the number)
            float h2 = TextHeight(c);
            float textS = rear + h2 * 0.5f + 1.5f;
            Labels(l, paint, l.axis, l.At(0f, textS), h2);
        }

        void TaxiOutPaint(StandLayout l, PaintBuilder paint)
        {
            char c = l.Code;
            float span = RunwayRules.MaxWingspan(c), clr = RunwayRules.StandClearance(c);
            var yellow = PaintBuilder.Yellow;

            // the hairpin lead-in to the stop bar, then on down the centreline as the lead-out turn
            var line = new List<Vector2>();
            foreach (var (p, _) in l.LeadIn(1.5f)) line.Add(p);
            float r = l.turnRadius;
            float stopS = Vector2.Dot(l.stop - l.entry, l.axis);
            float td = Geo.Fillet(l.entry, l.axis, l.outAlong, r, out _);
            if (td > stopS - 3f) r *= Mathf.Max(stopS - 3f, 1f) / td;
            td = Geo.Fillet(l.entry, l.axis, l.outAlong, r, out var centre);
            line.Add(l.entry + l.axis * td);
            Geo.Arc(line, centre, l.entry + l.axis * td, l.entry + l.outAlong * td, 1f, false);
            paint.Strip(OffUnpaved(l, line), LineHalfWidth, yellow);
            StopBar(l, paint, span);

            float half = span * 0.5f + clr * 0.25f;
            float near = l.noseS - clr * 0.25f, back = l.tailS + clr * 0.25f;
            Boundary(l, paint, -half, half, near, back);

            // number and code just inside the far end, read by the pilot coming down the centreline nose first
            float h2 = TextHeight(c);
            Labels(l, paint, -l.axis, l.At(0f, back - h2 * 0.5f - 1.5f), h2);
        }

        /// <summary>A lead-in or lead-out line without the stretches over a grass or dirt taxiway (which isn't
        /// painted), or over the taxilane of a stand not connected yet (there's nothing there): the longest run of
        /// points off it.</summary>
        static List<Vector2> OffUnpaved(StandLayout l, List<Vector2> line)
        {
            if (l.edge != null && l.edge.surface.IsPaved()) return line;
            List<Vector2> best = new List<Vector2>(), run = new List<Vector2>();
            foreach (var p in line)
            {
                if (Mathf.Abs(Geo.Cross(l.along, p - l.entry)) > l.halfWidth) run.Add(p);
                else run = new List<Vector2>();
                if (run.Count > best.Count) best = run;
            }
            return best;
        }

        void StopBar(StandLayout l, PaintBuilder paint, float span) =>
            paint.Box(l.stop, l.Right, StopBarHalfWidth, Mathf.Clamp(span * 0.08f, 1.5f, 4f), PaintBuilder.Yellow);

        /// <summary>The red line round the parked aircraft (x0..x1 across, s0..s1 along).</summary>
        static void Boundary(StandLayout l, PaintBuilder paint, float x0, float x1, float s0, float s1)
        {
            var red = PaintBuilder.Red;
            Vector2 a = l.At(x0, s0), b = l.At(x1, s0), c = l.At(x1, s1), d = l.At(x0, s1);
            paint.Line(a, b, SafetyHalfWidth, red, SafetyHalfWidth);
            paint.Line(b, c, SafetyHalfWidth, red, SafetyHalfWidth);
            paint.Line(c, d, SafetyHalfWidth, red, SafetyHalfWidth);
            paint.Line(d, a, SafetyHalfWidth, red, SafetyHalfWidth);
        }

        /// <summary>The stand's label (yellow) on the reader's left of `centre` and its size code (white) on the
        /// right, both read facing `up`.</summary>
        static void Labels(StandLayout l, PaintBuilder paint, Vector2 up, Vector2 centre, float height, float gap = TextGap)
        {
            string label = l.stand.Label;
            var right = Geo.Right(up);
            paint.Text(centre - right * (gap + TextWidth(label, height) * 0.5f), up, label, Pitch(height),
                PaintBuilder.Yellow, height);
            paint.Glyph(centre + right * (gap + height / 6f), up, l.Code, PaintBuilder.White, height);
        }

        // ------------------------------------------------------------------ de-icing bays

        void DeIcingPaint(StandLayout l, PaintBuilder paint)
        {
            char c = l.Code;
            float span = RunwayRules.MaxWingspan(c), clr = RunwayRules.StandClearance(c);
            float lane = span * 0.5f + clr;
            var red = PaintBuilder.Red;
            foreach (float x in new[] { -lane, lane })
                paint.Line(l.At(x, l.near0 + 1f), l.At(x, l.far - 1f), SafetyHalfWidth, red);
            StopBar(l, paint, span);
            // id and code at the entrance, outside the taxiway's edges
            float h = TextHeight(c);
            var at = l.At(0f, l.near0 + h * 0.5f + 2f);
            Labels(l, paint, l.axis, at, h, l.halfWidth + 1.5f);
        }
    }
}
