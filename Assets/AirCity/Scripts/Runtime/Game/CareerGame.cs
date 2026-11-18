using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Traffic;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>Shared world/session host. The historical name is retained for scene and save compatibility.</summary>
    public partial class CareerGame : MonoBehaviour
    {
        public Airport airportTemplate;
        public GameObject aircraftPrefab;
        public AircraftLiveryAsset charterLivery;
        public ValleyView valley;
        public CareerCamera view;
        public CareerState State { get; private set; }
        public Airport Airport { get; private set; }
        public string Message { get; private set; }
        public bool InMenu { get; private set; } = true;
        public bool Paused { get; set; }
        public int Speed { get; set; } = 1;
        public int SurveyedSite { get; private set; }
        public RegionPreset RegionChoice { get; private set; }
        public Flight ActiveFlight => flight;
        public bool HasSave => Store.Exists;
        public string SaveDirectory => Path.Combine(Application.persistentDataPath, "Career");
        CareerSaveStore Store => store ??= new CareerSaveStore(SaveDirectory);
        CareerSaveStore store;
        GameObject access, reception;
        AirCity.Aircraft.Aircraft charter;
        Flight flight;
        float boarding, accumulated, saveTimer;
        bool persist, replaying;
        const float StepSeconds = .25f;

        void Start()
        {
            if (State == null) Initialize(CareerState.New(19770616), false);
        }

        public void Initialize(CareerState state, bool enableSaving = true)
        {
            if (!state.Validate(out string problem)) throw new ArgumentException(problem);
            if (state.airport != null && state.airport.nodes != null && state.airport.nodes.Count > 0 && !state.airport.AssetsAvailable(GameAssetCatalog.Load(), out problem))
                throw new ArgumentException(problem);
            ClearAirport();
            State = state; RegionChoice = state.world.preset; persist = enableSaving; Paused = false; accumulated = 0f; saveTimer = 0f;
            var builder = GetComponent<CareerBuildTool>(); if (builder != null) builder.ResetTool();
            var roadTool = GetComponent<CareerRoadTool>(); if (roadTool != null) roadTool.Cancel();
            access = reception = null; InMenu = state.Site == null; SurveyedSite = Mathf.Max(0, state.selectedSite);
            valley.Build(state.world, State.RoadNetwork(), false); BuildAirport();
            UpgradeNaturalLandscape();
            if (State.HasAccess && (State.airport == null || State.airport.nodes.Count == 0)) access = valley.BuildAccess(State.Site, State.RoadNetwork(), false);
            if (State.handsOn ? State.layout?.receptionBuilt == true : State.IsOpen) reception = BuildReception();
            BindSharedTools();
            RefreshGroundCover();
            if (State.Site != null) view.Frame(State.Site); else view.Region();
            if (State.charterActive)
            {
                float elapsed = State.charterElapsed;
                float roadClock = State.roadTrafficSeconds;
                State.charterElapsed = 0f; State.operatingSeconds -= elapsed;
                replaying = true;
                try
                {
                    if (!LaunchCharter(out problem)) throw new InvalidOperationException("Could not resume charter: " + problem);
                    Advance(elapsed);
                }
                finally { replaying = false; State.roadTrafficSeconds = roadClock; }
            }
            valley.SetRoadClock(State.roadTrafficSeconds);
            // Resume resident ground trips only after historical charter/weather reconstruction.
            if (Airport!=null) foreach (var hangar in Airport.Hangars) hangar.ResumeResidentTrip();
            Message = null;
        }

        void UpgradeNaturalLandscape()
        {
            var world = State.world;
            bool coast = world.generatorVersion == 4 && world.HasOcean;
            bool inland = world.generatorVersion >= 4 && world.generatorVersion < 6 && world.HasInlandWater;
            bool forest = world.generatorVersion >= 4 && world.forestVersion < ForestPopulation.Version;
            if (!coast && !inland && !forest) return;
            Rect? airportBounds = null;
            if (Airport != null)
                foreach (var renderer in Airport.GetComponentsInChildren<Renderer>(true))
                {
                    var bounds = renderer.bounds;
                    if (!ValleyWorld.Finite(bounds.min.x) || !ValleyWorld.Finite(bounds.min.z) ||
                        !ValleyWorld.Finite(bounds.max.x) || !ValleyWorld.Finite(bounds.max.z)) continue;
                    var area = Rect.MinMaxRect(bounds.min.x - 100f, bounds.min.z - 100f, bounds.max.x + 100f, bounds.max.z + 100f);
                    airportBounds = airportBounds.HasValue ? Rect.MinMaxRect(
                        Mathf.Min(airportBounds.Value.xMin, area.xMin), Mathf.Min(airportBounds.Value.yMin, area.yMin),
                        Mathf.Max(airportBounds.Value.xMax, area.xMax), Mathf.Max(airportBounds.Value.yMax, area.yMax)) : area;
                }
            var roads = State.RoadNetwork();
            bool Protected(Vector2 point)
            {
                if (airportBounds.HasValue && airportBounds.Value.Contains(point)) return true;
                foreach (var segment in roads.segments)
                {
                    var a = PublicRoadNetwork.XZ(roads.Node(segment.a).position);
                    var b = PublicRoadNetwork.XZ(roads.Node(segment.b).position);
                    if (CareerConstruction.Distance(point, a, b) < 100f) return true;
                }
                return false;
            }
            bool changed = coast && RegionCoastline.Upgrade(world, Protected);
            changed |= inland && RegionInlandShore.Upgrade(world, Protected);
            bool denser = forest && ForestPopulation.Upgrade(world, Protected);
            if (changed) valley.RefreshLand();
            else if (denser) valley.RefreshForest(false);
        }

        public void NewWorld(int seed, RegionPreset preset = RegionPreset.Woodland)
        {
            var state = CareerState.New(seed, preset: preset); state.mode = State != null ? State.mode : GameMode.Career;
            Initialize(state, false);
            RegionChoice = preset;
            Message = "Explore the region. The whole map is available for your airport.";
        }

        public void NewLocation(RegionTerrainData data)
        {
            var state = CareerState.NewLocation(data); state.mode = State != null ? State.mode : GameMode.Career;
            Initialize(state, false); RegionChoice = state.world.preset;
            Message = "Explore this location. The whole map is available for your airport.";
        }

        public bool BeginRegion()
        {
            if (!State.StartRegion(out string problem)) { Message = problem; return false; }
            InMenu = false; persist = true; Paused = false; Speed = 1;
            BuildAirport(); BindSharedTools(); RefreshGroundCover();
            Message = "Your region is ready. Build anywhere; longer road connections cost more.";
            Save(false); return true;
        }

        public void Survey(int index)
        {
            SurveyedSite = Mathf.Clamp(index, 0, State.world.sites.Count - 1);
            view.Frame(State.world.sites[SurveyedSite]);
        }

        public bool Construct(IList<Vector2> accessRoute = null)
        {
            if (State.wholeRegion && State.Site == null) return BeginRegion();
            var step = State.Step;
            int balance = State.funds;
            if (Sandbox && step == CareerStep.ChooseSite) State.funds = CareerState.StartingFunds;
            if (!State.Build(out string problem, SurveyedSite, accessRoute)) { Message = problem; return false; }
            if (Sandbox) State.funds = balance;
            InMenu = false; persist = true;
            if (step == CareerStep.ChooseSite) { BuildAirport(); BindSharedTools(); view.Frame(State.Site); }
            if (step == CareerStep.ClearTrees || step == CareerStep.GradeLand || step == CareerStep.Expansion) valley.RefreshLand();
            if (step == CareerStep.Runway || step == CareerStep.Parking || step == CareerStep.Expansion) BuildAirport();
            if (State.HasAccess && access == null)
            {
                access = valley.BuildAccess(State.Site, State.RoadNetwork());
                // the original-save route clears trees along it; a hand-placed road never does (it refuses trees)
                if (!State.handsOn && step == CareerStep.AccessRoad) valley.RefreshForest(false);
            }
            if (State.IsOpen && reception == null) reception = BuildReception();
            RefreshGroundCover();
            Message = step == CareerStep.Reception ? "Your airfield is open. A local charter is ready to visit." : "Construction complete.";
            // a road placed by hand saves in the background (snapshot now, JSON and disk off the main thread), so placing
            // it doesn't stall a frame; the other steps keep their synchronous save
            if (accessRoute != null) QueueAutosave(); else Save(false);
            return true;
        }

        void BuildAirport()
        {
            ClearAirport();
            if (State.Site == null) return;
            if (State.airport != null && State.airport.nodes != null && State.airport.nodes.Count > 0)
            {
                Airport = State.airport.Restore(transform, GameAssetCatalog.Load());
                NaturalRunwayEdges();
                return;
            }
            var go = Instantiate(airportTemplate.gameObject, transform);
            go.name = State.Site.name + " Airfield";
            go.transform.position = new Vector3(State.Site.centre.x, State.handsOn && State.HasRunway ? State.layout.elevation : State.Site.elevation, State.Site.centre.y);
            Airport = go.GetComponent<Airport>(); Airport.timeScale = 0f; Airport.groundSize = 0f; Airport.windFrom = 270f;
            go.SetActive(true);
            NaturalRunwayEdges();
            if (!State.HasRunway) return;
            if (State.handsOn) { BuildPlayerAirport(); return; }
            float half = State.RunwayLength * .5f;
            var runway = Airport.AddRunway(new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), PavementSurface.Grass, 18f);
            NaturalRunwayEdges();
            if (State.HasParking)
            {
                var net = Airport.Taxiways;
                int Node(float x, float z, Runway r = null) => net.AddNode(new Vector3(x, 0f, z), r);
                float entry = half - 20f;
                int w = Node(-entry, 0f, runway), a = Node(-entry, -65f), mid = Node(0f, -65f),
                    b = Node(entry, -65f), e = Node(entry, 0f, runway);
                foreach (var pair in new[] { (w, a), (a, mid), (mid, b), (b, e) })
                    net.Connect(pair.Item1, pair.Item2, 7.5f, PavementSurface.Grass);
                net.Connect(Node(0f, 0f, runway), mid, 7.5f, PavementSurface.Grass);
                var stand = net.AddStand(StandKind.Parking, new Vector3(80f, 0f, -65f), 180f, SizeCode.A, PavementSurface.Grass, StandExit.TaxiOut);
                if (stand == null) throw new InvalidOperationException("The starter parking stand could not be connected.");
                net.Rebuild();
            }
            // Recreate idle surface aging; an active flight is replayed separately below.
            float idle = Mathf.Max(0f, State.operatingSeconds - State.charterElapsed);
            Airport.Simulate(idle);
            var type = aircraftPrefab.GetComponent<AirCity.Aircraft.Aircraft>().type;
            runway.SetWear(State.completedCharters * 2f * RunwayRules.WearPerMovement(PavementSurface.Grass, type.maxTakeoffMass, SurfaceCondition.Dry));
        }

        void BuildPlayerAirport()
        {
            var layout = State.layout; var origin = State.Site.centre;
            Vector3 Local(Vector2 p) => new Vector3(p.x - origin.x, 0f, p.y - origin.y);
            var runway = Airport.AddRunway(Local(layout.runwayA), Local(layout.runwayB), PavementSurface.Grass, 18f);
            NaturalRunwayEdges();
            var net = Airport.Taxiways;
            int Node(Vector2 p)
            {
                var local = Local(p); var existing = net.NearestNode(local, .5f);
                if (existing != null) return existing.id;
                if (net.TryNearestEdge(local, .5f, out var edge, out var onEdge)) return net.SplitEdge(edge, onEdge);
                return net.AddNode(local, CareerConstruction.Distance(p, layout.runwayA, layout.runwayB) < .5f ? runway : null);
            }
            foreach (var path in layout.taxiways)
                for (int i = 1; i < path.points.Count; i++) net.Connect(Node(path.points[i - 1]), Node(path.points[i]), 7.5f, PavementSurface.Grass);
            if (layout.standBuilt && net.Add(layout.Parking(origin)) == null) throw new InvalidOperationException("The saved parking stand could not be built.");
            net.Rebuild();
            Airport.Simulate(Mathf.Max(0f, State.operatingSeconds - State.charterElapsed));
            var type = aircraftPrefab.GetComponent<AirCity.Aircraft.Aircraft>().type;
            runway.SetWear(State.completedCharters * 2f * RunwayRules.WearPerMovement(PavementSurface.Grass, type.maxTakeoffMass, SurfaceCondition.Dry));
        }

        void NaturalRunwayEdges()
        {
            bool taxiwayChanged=false;
            foreach(var edge in Airport.Taxiways.edges)
                if(!edge.naturalEdges){edge.naturalEdges=true;taxiwayChanged=true;}
            if(taxiwayChanged)Airport.Taxiways.Rebuild();
            foreach(var runway in Airport.Runways)
            {
                if(runway.naturalEdges)continue;
                runway.naturalEdges=true;runway.Rebuild();
            }
        }

        GameObject BuildReception() => State.handsOn ? valley.BuildReception(State.layout.reception,State.layout.elevation,State.layout.receptionHeading,
            State.layout.receptionSiteVersion>=VisitorCentreConstruction.SiteVersion) : valley.BuildReception(State.Site);

        void RefreshGroundCover()
        {
            valley.SetBuiltAirport(Airport);
            var builtChange=valley.ConsumeBuiltSurfaceChange();
            if(builtChange.HasValue && State?.world!=null && valley.BuiltSurfaces!=null)
            {
                var area=builtChange.Value;
                var candidates=valley.ConsumeBuiltSurfaceProps();
                int removed=valley.BuiltSurfaces.ClearVegetation(State.world,candidates,area);
                if(State.layout!=null)State.layout.clearedTrees+=removed;
                // The accepted airside plan may already have marked its roots before this render update.
                valley.RefreshForest(false,area);
                valley.RefreshClearedGround(area);
            }
            // Airport and public pavement use their actual mesh ownership, including standalone service roads
            // and free stands. Only the separate tutorial reception needs a non-pavement exclusion here.
            var exclusions=new List<GroundCoverExclusion>();
            if(State.Site!=null)
            {
                if(State.handsOn && State.layout.receptionBuilt && State.layout.receptionSiteVersion<VisitorCentreConstruction.SiteVersion)
                    exclusions.Add(new GroundCoverExclusion(State.layout.reception,State.layout.reception,18f));
                else if(!State.handsOn && State.IsOpen)
                {
                    var reception=State.Site.centre+new Vector2(80f,-150f);
                    exclusions.Add(new GroundCoverExclusion(reception,reception,18f));
                }
            }
            valley.SetGroundCoverExclusions(exclusions);
        }

        public void PlayerChanged(CareerTool tool, Rect? terrainBounds = null)
        {
            persist = true;
            if (tool == CareerTool.ClearTrees)
            {
                valley.RefreshForest(false, terrainBounds);
                valley.RefreshClearedGround(terrainBounds);
            }
            else if (tool == CareerTool.Level)
            {
                valley.RefreshLand(terrainBounds ?? State.BuildableBounds);
                if (Airport != null && Airport.Runways.Count == 0 && Airport.terminals.Count == 0 && Airport.aprons.Count == 0 &&
                    Airport.Taxiways.edges.Count == 0 && Airport.Taxiways.stands.Count == 0 && Airport.Depots.Count == 0 &&
                    !Airport.Hangars.Any() && !State.layout.receptionBuilt)
                {
                    var p = Airport.transform.position; p.y = GetComponent<CareerBuildTool>().TargetHeight; Airport.transform.position = p;
                    State.layout.elevation = p.y;
                }
            }
            else if (tool == CareerTool.Runway || tool == CareerTool.Taxiway || tool == CareerTool.Stand) BuildAirport();
            else if (tool == CareerTool.Reception)
            {
                if(reception!=null)Remove(reception);
                valley.RefreshRoads(State.RoadNetwork(),false);
                reception=BuildReception();
                if(Streets!=null)Streets.network=State.RoadNetwork();
            }
            if (tool != CareerTool.ClearTrees && tool != CareerTool.Level) RefreshGroundCover();
            Message = null;
        }
        public void SetMessage(string message) { Message = message; }

        public void FinishTerrainStroke(CareerTool tool, Rect strokeBounds)
        {
            if (tool == CareerTool.ClearTrees) valley.QueueClearingSurface(strokeBounds);
            else if (tool == CareerTool.Level) valley.QueueLevelSurface(strokeBounds);
            if (Airport != null && Airport.Hangars.Any())
            {
                Airport.LayoutChanged();
                RefreshGroundCover();
            }
            QueueAutosave();
        }

        public bool QueueAutosave()
        {
            saveTimer = 0f;
            if (!persist || replaying) return false;
            if (!CaptureAirport()) return false;
            bool ok = Store.QueueSave(State, out string problem);
            if (!ok) Message = problem;
            return ok;
        }

        public bool AcceptCharter()
        {
            if (!State.BeginCharter(out string problem)) { Message = problem; return false; }
            if (!LaunchCharter(out problem)) { State.CancelCharter(); Message = problem; return false; }
            Message = "Charter accepted. Three passengers will use your new road connection.";
            Save(false); return true;
        }

        bool LaunchCharter(out string problem)
        {
            var go = Instantiate(aircraftPrefab, new Vector3(0f, -1000f, 0f), Quaternion.identity, transform);
            go.name = "Valley Charter"; charter = go.GetComponent<AirCity.Aircraft.Aircraft>();
            var livery = go.GetComponent<AircraftLiveryApplier>();
            if (livery != null && charterLivery != null) livery.SetLivery(charterLivery.livery, "C-VALY");
            flight = Airport.Traffic.Arrive(charter, out problem); boarding = 0f;
            if (flight != null) return true;
            Remove(go); charter = null; return false;
        }

        public void Advance(float seconds)
        {
            for (float left = seconds; left >= StepSeconds - .0001f; left -= StepSeconds)
            {
                if (!State.charterActive || Airport == null) break;
                Airport.Simulate(StepSeconds);
                if (!replaying) HangarAccounts.Advance(Airport, State, StepSeconds);
                State.charterElapsed += StepSeconds; State.operatingSeconds += StepSeconds;
                State.roadTrafficSeconds += StepSeconds;
                valley.SetRoadClock(State.roadTrafficSeconds);
                if (flight == null || (charter == null && flight.Phase != FlightPhase.Gone))
                { FailCharter("The charter was interrupted. Its operating fee has been refunded."); break; }
                if (flight.kind == FlightKind.Arrival && flight.Phase == FlightPhase.Parked)
                {
                    boarding += StepSeconds;
                    if (boarding >= 15f)
                    {
                        var departure = Airport.Traffic.Depart(charter, out string why);
                        if (departure == null) { FailCharter("The charter cannot depart: " + why); break; }
                        flight = departure;
                    }
                }
                else if (flight.kind == FlightKind.Departure && flight.Phase == FlightPhase.Gone)
                {
                    State.CompleteCharter(); flight = null; charter = null;
                    Message = $"Charter complete. Earned ${CareerState.CharterFee:N0}; profit ${CareerState.CharterFee - CareerState.CharterCost:N0}.";
                    Save(false); break;
                }
                if (State.charterElapsed >= 1800f)
                { FailCharter("The charter could not complete. Its operating fee has been refunded; you can retry."); break; }
            }
        }

        void FailCharter(string problem)
        {
            State.CancelCharter(); BuildAirport(); Message = problem; Save(false);
        }

        public string FlightStatus => flight == null ? "No active flight" : flight.kind == FlightKind.Arrival && flight.Phase == FlightPhase.Parked ?
            "Boarding passengers from the access road" : flight.Status;

        public bool Save(bool showMessage = true)
        {
            if (!persist || replaying) return false;
            if (!CaptureAirport()) return false;
            saveTimer = 0f;
            bool ok = Store.Save(State, out string problem);
            if (!ok || showMessage) Message = ok ? "Career saved." : problem;
            return ok;
        }

        public bool Continue()
        {
            if (!Store.Load(out var state, out string problem)) { Message = problem; return false; }
            state.UpgradeToPlayerConstruction();
            try { Initialize(state); Message = problem ?? "Airport resumed."; return true; }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            { Message = "Could not load the airport: " + e.Message; return false; }
        }

        void Update()
        {
            UpdateSharedTools();
            if (store != null && store.TryTakeSaveProblem(out string problem)) Message = problem;
            if (State == null) return;
            if (InMenu) { valley.SetRoadClock(Time.unscaledTime); return; }
            if (SharedTools && Airport != null) { Airport.externallySimulated = true; Airport.timeScale = Paused ? 0f : Speed; }
            saveTimer += Time.unscaledDeltaTime;
            if (saveTimer >= 15f) QueueAutosave();
            if (Paused) return;
            if (!State.charterActive)
            {
                float seconds = Time.unscaledDeltaTime * Speed;
                State.operatingSeconds += seconds;
                State.roadTrafficSeconds += seconds;
                valley.SetRoadClock(State.roadTrafficSeconds);
                if (Airport != null) Airport.Simulate(seconds);
                HangarAccounts.Advance(Airport, State, seconds);
                return;
            }
            accumulated += Time.unscaledDeltaTime * Speed;
            int steps = Mathf.FloorToInt(accumulated / StepSeconds);
            if (steps > 0) { Advance(steps * StepSeconds); accumulated -= steps * StepSeconds; }
        }

        public void Focus() { if (State.Site != null) view.Frame(State.Site); else view.Frame(State.world.sites[SurveyedSite]); }
        void OnApplicationPause(bool paused) { if (paused && State != null) Save(false); }
        void OnApplicationQuit() { if (State != null) Save(false); }
        void OnDestroy() { store?.FlushPending(); ClearAirport(); }
        void ClearAirport()
        {
            if (charter != null) Remove(charter.gameObject);
            charter = null; flight = null;
            if (Airport != null) Remove(Airport.gameObject);
            Airport = null;
        }
        static void Remove(GameObject go)
        {
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
