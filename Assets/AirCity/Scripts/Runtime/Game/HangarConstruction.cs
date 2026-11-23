using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>The same detached hangar quote and paid placement transaction in both game modes.</summary>
    public static class HangarConstruction
    {
        public static string Problem(CareerGame game, Airport airport, Vector2 door, float heading, HangarSize size = HangarSize.Small)
        {
            if (airport == null || !Finite(door.x) || !Finite(door.y) || !Finite(heading))
                return "Choose a hangar position inside the airport.";
            string problem = airport.HangarProblem(door, heading, size: size);
            if (problem != null) return problem;
            if (game == null) return null;
            problem = game.ToolProblem(DevTool.Hangar);
            if (problem != null) return problem;
            var spec = HangarSpec.For(size);
            var footprint = spec.Footprint(door, heading);
            var samples = Samples(footprint).ToArray();
            var origin = Geo.XZ(airport.transform.position);
            if (samples.Any(p => game.State.world.IsWater(p + origin)))
                return "Prepare dry land above the water before building the hangar.";
            if (!game.CheckFootprint(samples, 0f, out problem)) return problem;
            // Unlike a sloping taxiway, this shell has one flat floor at the airport datum.
            if (samples.Any(p => Mathf.Abs(game.State.world.Height(p + origin) - airport.transform.position.y) > .3f))
                return "Level the whole hangar footprint to the airport height first.";
            problem = VisitorCentreConstruction.OverlapProblem(game.State, footprint.Select(p => p + origin).ToArray());
            if (problem != null) return problem;
            var roads = game.State.RoadNetwork();
            foreach (var road in roads.segments)
                foreach (var span in roads.Path(road).Spans())
                    if (Geo.Overlap(footprint, Strip(Geo.XZ(span.a) - origin, Geo.XZ(span.b) - origin, road.Type.FullWidth * .5f + 2f)))
                        return "Keep the hangar clear of public roads and their foundations.";
            return game.CanSpend(spec.Price, out problem) ? null : problem;
        }

        public static bool TryBuild(CareerGame game, Airport airport, Vector2 door, float heading,
            out SmallHangar hangar, out string problem, HangarSize size = HangarSize.Small)
        {
            hangar = null;
            problem = Problem(game, airport, door, heading, size);
            if (problem != null) return false;
            hangar = airport.AddHangar(door, heading, size);
            if (hangar == null) { problem = "The hangar no longer fits here."; return false; }
            game?.Spend(hangar.Spec.Price);
            game?.SharedConstructionChanged();
            return true;
        }

        /// <summary>Shell collision used by runway tools in fixtures and the shared terrain quote.</summary>
        public static string RunwayProblem(Airport airport, Runway runway)
        {
            if (airport == null || runway == null) return null;
            var a = Geo.XZ(runway.start) - runway.Axis2D * runway.WorkMargin;
            var b = Geo.XZ(runway.end) + runway.Axis2D * runway.WorkMargin;
            return PathProblem(airport, a, b, runway.HalfWidthTotal);
        }

        public static string PathProblem(Airport airport, Vector2 a, Vector2 b, float halfWidth, bool protectApproach = false)
        {
            if (airport == null) return null;
            var strip = Strip(a, b, halfWidth);
            foreach (var school in airport.FlyingSchools)
                foreach (var site in school.Footprints(false))
                    if (Geo.Overlap(strip, site)) return "Keep construction clear of the flying school.";
            foreach (var hangar in airport.HangarBuildings)
            {
                if (Geo.Overlap(strip, hangar.BuildingPad)) return "Keep construction clear of the aircraft hangar.";
                if (protectApproach && HangarConnection.Approach(hangar, out var approach) && Geo.Overlap(strip, approach))
                    return "Keep construction clear of the hangar's paved taxiway approach.";
            }
            return null;
        }

        static IEnumerable<Vector2> Samples(Vector2[] footprint)
        {
            var along = footprint[1] - footprint[0]; var across = footprint[3] - footprint[0];
            int nx = Mathf.Max(1, Mathf.CeilToInt(along.magnitude / 3f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(across.magnitude / 3f));
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                    yield return footprint[0] + along * ((float)x / nx) + across * ((float)z / nz);
        }

        static Vector2[] Strip(Vector2 a, Vector2 b, float half)
        {
            var axis = (b - a).sqrMagnitude > .0001f ? (b - a).normalized : Vector2.up;
            var right = Geo.Right(axis) * half;
            return new[] { a - right, b - right, b + right, a + right };
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
