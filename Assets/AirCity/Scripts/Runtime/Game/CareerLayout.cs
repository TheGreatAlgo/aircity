using System;
using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    public enum CareerTool { Select, ClearTrees, Level, Runway, Taxiway, Stand, Road, Reception }

    [Serializable]
    public sealed class CareerTaxiway { public List<Vector2> points = new List<Vector2>(); }

    /// <summary>Only the player's placed layout and earned tool unlocks; no preassigned airport footprint.</summary>
    [Serializable]
    public sealed class CareerLayout
    {
        public int unlocked = (1 << (int)CareerTool.Select) | (1 << (int)CareerTool.ClearTrees);
        public int clearedTrees;
        public float earthwork, elevation;
        public bool runwayBuilt, standBuilt, receptionBuilt;
        public int receptionSiteVersion;
        public Vector2 runwayA, runwayB, stand, standAlong, reception;
        public float standHeading, receptionHeading;
        public List<CareerTaxiway> taxiways = new List<CareerTaxiway>();
        public bool Unlocked(CareerTool tool) => (unlocked & (1 << (int)tool)) != 0;
        public void Unlock(CareerTool tool) => unlocked |= 1 << (int)tool;
        public Stand Parking(Vector2 origin)
        {
            var p = stand - origin;
            var result = new Stand { kind = StandKind.Parking, code = SizeCode.A, surface = PavementSurface.Grass,
                exit = StandExit.TaxiOut, position = new Vector3(p.x, 0f, p.y), heading = standHeading };
            result.Frame(standAlong, 7.5f); return result;
        }
        public IEnumerable<(Vector2 a, Vector2 b)> TaxiSegments()
        {
            foreach (var path in taxiways)
                for (int i = 1; i < path.points.Count; i++) yield return (path.points[i - 1], path.points[i]);
        }
        public Vector2 SnapTaxi(Vector2 p, float radius)
        {
            float best = radius; Vector2 result = p;
            void Try(Vector2 a, Vector2 b) {
                var q = Geo.ClosestOnSegment(p, a, b, out _); float d = Vector2.Distance(q, p);
                if (d < best) { best = d; result = q; }
            }
            if (runwayBuilt) Try(runwayA, runwayB);
            foreach (var e in TaxiSegments()) Try(e.a, e.b);
            return result;
        }
    }
}
