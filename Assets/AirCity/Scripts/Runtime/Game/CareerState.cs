using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    public enum CareerStep { ChooseSite, ClearTrees, GradeLand, Runway, Parking, AccessRoad, Reception, FirstCharter, Expansion, Operating }

    public readonly struct ConstructionQuote
    {
        public readonly int cost, trees;
        public readonly float earthwork;
        public readonly string title;
        public ConstructionQuote(string title, int cost, int trees = 0, float earthwork = 0f)
        { this.title = title; this.cost = cost; this.trees = trees; this.earthwork = earthwork; }
    }

    /// <summary>Career transactions and progression. No scene objects; failed actions never spend money.</summary>
    [Serializable]
    public class CareerState
    {
        public const int SaveVersion = 3, StartingFunds = 125000, CharterFee = 1800, CharterCost = 250;
        public GameMode mode;
        public AirportSaveData airport;
        public int saveVersion = SaveVersion;
        public ValleyWorld world;
        public int selectedSite = -1, completedBuilds, funds = StartingFunds, completedCharters;
        public bool expanded, charterActive;
        public float charterElapsed, operatingSeconds;
        public float roadTrafficSeconds;
        public double expenseRemainder;
        public PublicRoadNetwork publicRoads;
        public List<Vector2> accessRoadRoute = new List<Vector2>();
        public bool handsOn, wholeRegion;
        public CareerLayout layout;
        public AirportSite Site => selectedSite >= 0 && selectedSite < world.sites.Count ? world.sites[selectedSite] : null;
        public Rect BuildableBounds => wholeRegion || mode == GameMode.Sandbox ? world.RegionBounds : Site != null ? Site.Parcel : default;
        public CareerStep Step => handsOn ? ManualStep : Site == null ? CareerStep.ChooseSite : completedBuilds < 6 ? (CareerStep)(completedBuilds + 1) :
            completedCharters == 0 ? CareerStep.FirstCharter : !expanded ? CareerStep.Expansion : CareerStep.Operating;
        public float RunwayLength => handsOn && layout != null ? Vector2.Distance(layout.runwayA, layout.runwayB) : expanded ? 1000f : 700f;
        public bool HasRunway => handsOn ? layout != null && layout.runwayBuilt : completedBuilds >= 3;
        public bool HasParking => handsOn ? layout != null && layout.standBuilt : completedBuilds >= 4;
        public bool HasAccess => handsOn ? accessRoadRoute != null && accessRoadRoute.Count >= 2 : completedBuilds >= 5;
        public bool IsOpen => handsOn ? HasRunway && HasParking && HasAccess && layout.receptionBuilt : completedBuilds >= 6;
        CareerStep ManualStep => Site == null ? CareerStep.ChooseSite : !HasRunway ?
            wholeRegion ? CareerStep.Runway : layout.clearedTrees == 0 ? CareerStep.ClearTrees : layout.earthwork < 1f ? CareerStep.GradeLand : CareerStep.Runway :
            !HasParking ? CareerStep.Parking : !HasAccess ? CareerStep.AccessRoad : !layout.receptionBuilt ? CareerStep.Reception :
            completedCharters == 0 ? CareerStep.FirstCharter : RunwayLength < 1000f ? CareerStep.Expansion : CareerStep.Operating;
        // Unity JSON may deserialize a null inline object as an empty object. The original career had no graph or route.
        bool ImplicitRoads => publicRoads == null || (publicRoads.nodes != null && publicRoads.segments != null &&
            publicRoads.nodes.Count == 0 && publicRoads.segments.Count == 0 && (accessRoadRoute == null || accessRoadRoute.Count == 0));

        public static CareerState New(int seed, bool handsOn = true, RegionPreset preset = RegionPreset.Woodland)
        {
            var state = new CareerState { world = handsOn ? ValleyWorld.Generate(seed, preset) : ValleyWorld.GenerateLegacy(seed, preset), handsOn = handsOn, wholeRegion = handsOn, layout = new CareerLayout() };
            state.publicRoads = PublicRoadNetwork.FromWorld(state.world); return state;
        }

        public static CareerState NewLocation(RegionTerrainData data)
        {
            var state = new CareerState { world = ValleyWorld.GenerateLocation(data), handsOn = true,
                wholeRegion = true, layout = new CareerLayout() };
            state.publicRoads = PublicRoadNetwork.FromWorld(state.world); return state;
        }

        /// <summary>Accept the current region. The legacy site is only a coordinate anchor; the entire region is available.</summary>
        public bool StartRegion(out string problem)
        {
            problem = Site != null ? "This region is already open for building." : !handsOn ? "This save uses the original airport layout." : null;
            if (problem != null) return false;
            selectedSite = 0; wholeRegion = true;
            layout.elevation = Site.elevation;
            // A clear or arid starting area must not require removing a tree to earn the preparation tools.
            layout.Unlock(CareerTool.Level); layout.Unlock(CareerTool.Runway);
            return true;
        }

        public PublicRoadNetwork RoadNetwork()
        {
            if (!ImplicitRoads) return publicRoads;
            // Additive migration for the first career format, whose access road was implicit.
            var graph = PublicRoadNetwork.FromWorld(world);
            if (HasAccess && !graph.AddAirportAccess(world, Site, accessRoadRoute != null && accessRoadRoute.Count >= 2 ? accessRoadRoute : PublicRoadLayout.Suggested(world, Site), out string why))
                throw new InvalidOperationException(why);
            return graph;
        }

        public void UpgradeToPlayerConstruction()
        {
            saveVersion = SaveVersion;
            if (handsOn) return;
            var l = new CareerLayout();
            if (Site != null)
            {
                l.elevation = Site.elevation;
                if (completedBuilds >= 1) { l.clearedTrees = 1; l.Unlock(CareerTool.Level); }
                if (completedBuilds >= 2) { l.earthwork = 1f; l.Unlock(CareerTool.Runway); }
                if (HasRunway)
                {
                    float half = RunwayLength * .5f;
                    l.runwayBuilt = true; l.runwayA = Site.centre + Vector2.left * half; l.runwayB = Site.centre + Vector2.right * half;
                    l.Unlock(CareerTool.Taxiway);
                    if (HasParking)
                    {
                        float entry = half - 20f;
                        l.taxiways.Add(new CareerTaxiway { points = new List<Vector2> { Site.centre + new Vector2(-entry, 0f), Site.centre + new Vector2(-entry, -65f),
                            Site.centre + new Vector2(0f, -65f), Site.centre + new Vector2(entry, -65f), Site.centre + new Vector2(entry, 0f) } });
                        l.taxiways.Add(new CareerTaxiway { points = new List<Vector2> { Site.centre, Site.centre + new Vector2(0f, -65f) } });
                        l.standBuilt = true; l.stand = Site.centre + new Vector2(80f, -65f); l.standAlong = Vector2.right; l.standHeading = 180f;
                        l.Unlock(CareerTool.Stand); l.Unlock(CareerTool.Road);
                    }
                }
                if (HasAccess)
                {
                    publicRoads = RoadNetwork();
                    if (accessRoadRoute == null || accessRoadRoute.Count < 2) accessRoadRoute = PublicRoadLayout.Suggested(world, Site);
                    l.Unlock(CareerTool.Reception);
                }
                if (IsOpen) { l.receptionBuilt = true; l.reception = Site.centre + new Vector2(80f, -150f); }
            }
            layout = l; handsOn = true;
        }

        public ConstructionQuote Quote(int siteIndex = -1, IList<Vector2> accessRoute = null)
        {
            if (Step == CareerStep.ChooseSite)
            {
                if (handsOn && wholeRegion) return new ConstructionQuote("Start building", 0);
                var site = world.sites[Mathf.Clamp(siteIndex, 0, world.sites.Count - 1)];
                return new ConstructionQuote("Buy " + site.name, site.landPrice);
            }
            var s = Site;
            switch (Step)
            {
                case CareerStep.ClearTrees:
                    int trees = world.TreeCount(s.Clearing);
                    return new ConstructionQuote("Clear the airfield", 600 + trees * 24, trees);
                case CareerStep.GradeLand:
                    float volume = world.Earthwork(s.Preparation, s.elevation);
                    return new ConstructionQuote("Prepare level ground", 1000 + Mathf.CeilToInt(volume * .055f), 0, volume);
                case CareerStep.Runway: return new ConstructionQuote("Build 700 m grass runway", 14000);
                case CareerStep.Parking: return new ConstructionQuote("Build taxiway and parking", 6000);
                case CareerStep.AccessRoad:
                    return new ConstructionQuote("Connect the public road", PublicRoadLayout.Price(accessRoute ?? PublicRoadLayout.Suggested(world, s)));
                case CareerStep.Reception: return new ConstructionQuote("Build passenger reception", 4500);
                case CareerStep.Expansion:
                    float earth = world.Earthwork(s.Expansion, s.elevation);
                    int count = world.TreeCount(ValleyWorld.GradingBounds(s.Expansion));
                    return new ConstructionQuote("Extend runway to 1,000 m", 8500 + count * 24 + Mathf.CeilToInt(earth * .055f), count, earth);
                default: return new ConstructionQuote("Accept local charter", CharterCost);
            }
        }

        public bool Build(out string problem, int siteIndex = -1, IList<Vector2> accessRoute = null)
        {
            problem = null;
            if (handsOn && wholeRegion && Site == null) return StartRegion(out problem);
            if (handsOn && Site != null)
            {
                if (accessRoute != null) return CareerConstruction.BuildRoad(this, accessRoute, out problem);
                problem = "Choose a build tool and place the work on your land."; return false;
            }
            if (charterActive) { problem = "Wait for the charter to finish before construction."; return false; }
            if (Step == CareerStep.FirstCharter || Step == CareerStep.Operating) { problem = "The airfield is ready for flights."; return false; }
            if (Step == CareerStep.ChooseSite && (siteIndex < 0 || siteIndex >= world.sites.Count))
            { problem = "Choose a starting site."; return false; }
            var route = Step == CareerStep.AccessRoad ? accessRoute ?? PublicRoadLayout.Suggested(world, Site) : null;
            if (route != null && !PublicRoadLayout.Validate(world, Site, route, out problem)) return false;
            var quote = Quote(siteIndex, route);
            if (funds < quote.cost) { problem = "Insufficient funds for this work."; return false; }
            PublicRoadNetwork proposed = null;
            if (route != null)
            {
                proposed = RoadNetwork().Clone();
                if (!proposed.AddAirportAccess(world, Site, route, out problem)) return false;
            }
            var step = Step;
            // Retain the original purchase transaction for older saves.
            if (step == CareerStep.ChooseSite) { selectedSite = siteIndex; wholeRegion = false; }
            else if (step == CareerStep.Expansion)
            {
                world.Clear(Site.Expansion); world.Grade(Site.Expansion, Site.elevation); expanded = true;
            }
            else
            {
                if (step == CareerStep.ClearTrees) world.Clear(Site.Clearing);
                if (step == CareerStep.GradeLand) world.Grade(Site.Preparation, Site.elevation);
                if (step == CareerStep.AccessRoad)
                {
                    publicRoads = proposed; accessRoadRoute = new List<Vector2>(route);
                    PublicRoadLayout.Clear(world, route);
                }
                completedBuilds++;
            }
            funds -= quote.cost;
            return true;
        }

        public bool BeginCharter(out string problem)
        {
            problem = !IsOpen ? "Finish the airfield and its road connection first." : charterActive ? "A charter is already in progress." :
                funds < CharterCost ? "Insufficient funds for the charter's operating cost." : null;
            if (problem != null) return false;
            funds -= CharterCost; charterActive = true; charterElapsed = 0f;
            return true;
        }

        public void CancelCharter()
        {
            if (!charterActive) return;
            funds += CharterCost; charterActive = false; charterElapsed = 0f;
        }

        public bool CompleteCharter()
        {
            if (!charterActive) return false;
            charterActive = false; charterElapsed = 0f; funds += CharterFee; completedCharters++;
            return true;
        }

        public bool Validate(out string problem)
        {
            problem = "The career save has invalid progression or finances.";
            if (saveVersion < 1 || saveVersion > SaveVersion || !Enum.IsDefined(typeof(GameMode), mode) || world == null || selectedSite < -1 || selectedSite > 2 || completedBuilds < 0 || completedBuilds > 6 ||
                double.IsNaN(expenseRemainder) || double.IsInfinity(expenseRemainder) || expenseRemainder < 0d || expenseRemainder >= 1d ||
                funds < 0 || funds > 100000000 || completedCharters < 0 || completedCharters > 50000 ||
                !ValleyWorld.Finite(charterElapsed) || charterElapsed < 0f || charterElapsed > 1800f ||
                !ValleyWorld.Finite(operatingSeconds) || operatingSeconds < charterElapsed ||
                !ValleyWorld.Finite(roadTrafficSeconds) || roadTrafficSeconds < 0f ||
                (selectedSite == -1 && (completedBuilds != 0 || completedCharters != 0 || expanded || charterActive)) ||
                (!handsOn && completedBuilds < 6 && (charterActive || completedCharters > 0 || expanded)) ||
                (expanded && completedCharters == 0) || (!charterActive && charterElapsed != 0f)) return false;
            if (!world.Validate(out problem)) return false;
            if (airport != null && airport.nodes != null && airport.nodes.Count > 0)
            { if (!airport.Validate(out problem)) return false; }
            else if (handsOn && !CareerConstruction.Validate(this, out problem)) return false;
            if (!ImplicitRoads)
            {
                if (!publicRoads.Validate(out problem)) return false;
                if (HasAccess && (airport == null || airport.nodes == null || airport.nodes.Count == 0))
                {
                    var entrance = publicRoads.nodes.Find(n => n.terminalEntrance == "airport-reception");
                    var outside = publicRoads.nodes.Where(n => n.outsideConnection || Mathf.Abs(n.position.z) >= ValleyWorld.Size * .5f - 1f).ToList();
                    if (entrance == null || !outside.Any(n => publicRoads.HasRoute(entrance.id, n.id)))
                    { problem = "The terminal entrance is disconnected from the public road."; return false; }
                }
            }
            if (!handsOn && accessRoadRoute != null && accessRoadRoute.Count > 0 && !PublicRoadLayout.Validate(world, Site, accessRoadRoute, out problem)) return false;
            problem = null; return true;
        }
    }
}
