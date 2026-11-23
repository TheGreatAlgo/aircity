using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Airfield
{
    public partial class Airport
    {
        /// <summary>Set while a session replays history on load (an active charter): schools take no decisions then,
        /// as their saved state is already that of the moment of saving.</summary>
        [NonSerialized] public bool reconstructing;

        /// <summary>Optional live-session check that a school's driveway reaches the public roads. Core tests and
        /// authored scenes may omit it.</summary>
        [NonSerialized] public Func<FlyingSchool, string> SchoolRoadProblem;

        FlyingSchool[] schoolCache = Array.Empty<FlyingSchool>();
        int schoolCacheVersion = -1;

        /// <summary>Authored flying schools, excluding pending demolition and construction previews.</summary>
        public IEnumerable<FlyingSchool> FlyingSchools => GetComponentsInChildren<FlyingSchool>(true)
            .Where(s => s.gameObject.activeSelf && (s.gameObject.hideFlags & HideFlags.DontSave) == 0);

        void SimulateSchools(float seconds)
        {
            if (schoolCacheVersion != layoutVersion)
            {
                schoolCacheVersion = layoutVersion;
                schoolCache = FlyingSchools.ToArray();
            }
            foreach (var school in schoolCache)
                if (school != null) school.Simulate(seconds);
        }

        /// <summary>Why a flying school cannot occupy this site (Airport space): its flight line as stands, and the
        /// clubhouse, car park and driveway throat behind it. Terrain, water and funds are the session's to check.</summary>
        public string FlyingSchoolProblem(Vector2 origin, float heading, float lane, int side, PavementSurface surface,
            FlyingSchool except = null)
        {
            if (float.IsNaN(origin.x) || float.IsInfinity(origin.x) || float.IsNaN(origin.y) || float.IsInfinity(origin.y) ||
                float.IsNaN(heading) || float.IsInfinity(heading)) return "Choose a valid flying school position.";
            lane = FlyingSchoolLayout.Lane(lane);
            var net = GetComponentInChildren<TaxiwayNetwork>(true);
            if (net == null) return "This airport has no taxiway network.";
            for (int i = 0; i < FlyingSchoolLayout.Spots; i++)
            {
                var layout = net.Layout(FlyingSchoolLayout.SpotStand(origin, heading, lane, surface, i));
                if (layout == null) return "The flight line cannot be laid out here.";
                if (!net.Fits(layout, except != null ? except.SiteId : null))
                    return "Keep the flight line clear of other stands, taxiways, roads, runways and buildings.";
                foreach (var apron in aprons)
                    if (apron != null && apron.outline.Count >= 3 && apron.surface.IsPaved() != surface.IsPaved() && Geo.Overlap(layout.Pad, apron.outline))
                        return "Match the flight line's surface to the apron it stands on.";
            }
            foreach (var outline in FlyingSchoolLayout.Footprints(origin, heading, lane, side, false))
            {
                string problem = SiteProblem(outline, "flying school", except: except);
                if (problem != null) return problem;
                foreach (var apron in aprons)
                    if (apron != null && apron.outline.Count >= 3 && Geo.Overlap(outline, apron.outline))
                        return "Keep the clubhouse and car park off aircraft aprons.";
            }
            return null;
        }

        /// <summary>Why a building's convex footprint cannot stand here: what is already built on the airport.</summary>
        internal string SiteProblem(IList<Vector2> outline, string what, FlyingSchool except = null)
        {
            foreach (var other in HangarBuildings)
            {
                if (Geo.Overlap(outline, other.BuildingPad)) return "Overlaps a hangar.";
                if (HangarConnection.Approach(other, out var approach) && Geo.Overlap(outline, approach))
                    return $"Keep the {what} clear of hangar approaches.";
            }
            foreach (var school in FlyingSchools)
            {
                if (school == except) continue;
                if (school.Footprints().Any(p => Geo.Overlap(outline, p))) return "Overlaps another flying school.";
            }
            foreach (var terminal in terminals)
                if (terminal != null && terminal.Footprints().Any(p => Geo.Overlap(outline, p))) return "Overlaps a terminal.";
            foreach (var runway in Runways)
            {
                var a = Geo.XZ(runway.start) - runway.Axis2D * runway.WorkMargin;
                var b = Geo.XZ(runway.end) + runway.Axis2D * runway.WorkMargin;
                var right = Geo.Right(runway.Axis2D) * runway.HalfWidthTotal;
                if (Geo.Overlap(outline, new[] { a + right, b + right, b - right, a - right })) return $"Keep the {what} clear of the runway.";
            }
            var net = GetComponentInChildren<TaxiwayNetwork>(true);
            if (net != null)
            {
                foreach (var edge in net.edges)
                    foreach (var span in net.Path(edge).Spans())
                    {
                        var a = Geo.XZ(span.a); var b = Geo.XZ(span.b); if ((b - a).sqrMagnitude < .0001f) continue;
                        var right = Geo.Right((b - a).normalized) * edge.width * .5f;
                        if (Geo.Overlap(outline, new[] { a + right, b + right, b - right, a - right }))
                            return edge.IsRoad ? $"Keep the {what} clear of service roads." : $"Keep the {what} clear of taxiways.";
                    }
                foreach (var layout in net.Layouts())
                {
                    if (except != null && layout.stand.owner == except.SiteId) continue;
                    if (Geo.Overlap(outline, layout.Pad)) return $"Keep the {what} clear of aircraft stands.";
                }
            }
            foreach (var depot in Depots)
                if (Geo.Overlap(outline, depot.Corners)) return "Overlaps a maintenance depot.";
            foreach (var depot in GetComponentsInChildren<BaggageDepot>())
            {
                var b = depot.BuildingBox;
                if (Geo.Overlap(outline, new[] { b.c - b.ax * b.hx - b.az * b.hz, b.c + b.ax * b.hx - b.az * b.hz,
                        b.c + b.ax * b.hx + b.az * b.hz, b.c - b.ax * b.hx + b.az * b.hz })) return "Overlaps a baggage depot.";
            }
            foreach (var area in deicingWorkAreas)
                if (area != null && Geo.Overlap(outline, area.Outline)) return $"Keep the {what} clear of deicing supply areas.";
            foreach (var bay in deicingParkingBays)
                if (bay != null && Geo.Overlap(outline, bay.Outline)) return $"Keep the {what} clear of truck parking.";
            return null;
        }

        /// <summary>Place a flying school: its building object and the stands of its flight line. Null if it does not
        /// fit (FlyingSchoolProblem).</summary>
        public FlyingSchool AddFlyingSchool(Vector2 origin, float heading, float lane, int side, PavementSurface surface, string id = null)
        {
            lane = FlyingSchoolLayout.Lane(lane);
            if (FlyingSchoolProblem(origin, heading, lane, side, surface) != null) return null;
            var net = Taxiways;
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N").Substring(0, 12);
            var made = new List<Stand>();
            for (int i = 0; i < FlyingSchoolLayout.Spots; i++)
            {
                var stand = FlyingSchoolLayout.SpotStand(origin, heading, lane, surface, i);
                stand.owner = id;
                if (net.Add(stand) == null) { foreach (var s in made) net.RemoveStand(s); return null; }
                made.Add(stand);
            }
            int number = 1;
            var names = new HashSet<string>(FlyingSchools.Select(s => s.name));
            while (names.Contains("Flying school " + number)) number++;
            var go = new GameObject("Flying school " + number); go.SetActive(false);
            go.transform.SetParent(transform, false); go.transform.localPosition = Geo.X0Z(origin);
            go.transform.localRotation = Quaternion.Euler(0f, heading, 0f);
            var school = go.AddComponent<FlyingSchool>();
            school.Configure(id, lane, surface, side, made.Select(s => s.id).ToArray());
            go.SetActive(true);
            net.Rebuild(); LayoutChanged();
            return school;
        }

        /// <summary>Remove a school and its flight line once it has no aircraft left.</summary>
        public bool RemoveFlyingSchool(FlyingSchool school)
        {
            if (school == null || school.Airport != this || !school.CanDemolish) return false;
            var net = Taxiways;
            foreach (var stand in net.stands.Where(s => s.owner == school.SiteId).ToArray()) net.RemoveStand(stand);
            school.gameObject.SetActive(false); AirfieldPart.DestroyAny(school.gameObject);
            net.Rebuild(); LayoutChanged();
            return true;
        }

        public bool TryPickFlyingSchool(Vector2 point, out FlyingSchool school, float margin = 0f)
        {
            school = FlyingSchools.LastOrDefault(s => s.Contains(point, margin));
            return school != null;
        }
    }
}
