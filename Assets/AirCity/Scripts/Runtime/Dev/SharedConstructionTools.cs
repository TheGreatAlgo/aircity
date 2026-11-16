using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevAirfieldTool
    {
        /// <summary>Mode-independent starting choices for a small airfield; the full catalog stays available.</summary>
        public void UseStarterDefaults()
        {
            surface = PavementSurface.Grass; runwayWidth = taxiwayWidth = 0;
            standCode = SizeCode.A; standExit = StandExit.TaxiOut; standNoSmall = false;
        }

        ApronArea BuildApron(IList<Vector2> outline, PavementSurface pavement)
        {
            int price = Mathf.CeilToInt(Mathf.Abs(Geo.SignedArea(outline)) * 8f);
            if (game != null && (!game.CheckFootprint(AreaSamples(outline), 0f, out apronProblem) || !game.CanSpend(price, out apronProblem))) return null;
            var made = airport.AddApron(outline, pavement);
            if (made != null) { game?.Spend(price); game?.SharedConstructionChanged(); }
            return made;
        }
        Terminal BuildTerminal(IList<Vector2> outline, int floors)
        {
            int price = Mathf.CeilToInt(Mathf.Abs(Geo.SignedArea(outline)) * floors * 100f);
            if (game != null && (!game.CheckFootprint(AreaSamples(outline), 0f, out apronProblem) || !game.CanSpend(price, out apronProblem))) return null;
            var made = airport.AddTerminal(outline, floors);
            if (made != null) { game?.Spend(price); game?.SharedConstructionChanged(); }
            return made;
        }
        static IEnumerable<Vector2> AreaSamples(IList<Vector2> outline)
        {
            var min = outline[0]; var max = min;
            foreach (var p in outline) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); yield return p; }
            for (float x = min.x; x <= max.x; x += 7f)
                for (float y = min.y; y <= max.y; y += 7f)
                { var p = new Vector2(x, y); if (Geo.Inside(outline, p)) yield return p; }
        }
    }
}
