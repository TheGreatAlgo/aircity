using System;
using System.Collections.Generic;
using System.IO;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Traffic;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>Owns the guided career scene. Existing sandbox tools and their scene are deliberately independent.</summary>
    public class CareerGame : MonoBehaviour
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
        public int Speed { get; set; } = 5;
        public int SurveyedSite { get; private set; }
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
            ClearAirport();
            State = state; persist = enableSaving; Paused = false; accumulated = 0f; saveTimer = 0f;
            var builder = GetComponent<CareerBuildTool>(); if (builder != null) builder.ResetTool();
            var roadTool = GetComponent<CareerRoadTool>(); if (roadTool != null) roadTool.Cancel();
            access = reception = null; InMenu = state.Site == null; SurveyedSite = Mathf.Max(0, state.selectedSite);
            valley.Build(state.world, State.RoadNetwork(), false); BuildAirport();
            if (State.HasAccess) access = valley.BuildAccess(State.Site, State.RoadNetwork(), false);
            if (State.IsOpen) reception = BuildReception();
            RefreshGroundCover();
            if (State.Site != null) view.Frame(State.Site); else view.Frame(State.world.sites[0]);
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
            Message = null;
        }

        public void NewWorld(int seed)
        {
            Initialize(CareerState.New(seed), false);
            Message = "Choose a site. Survey its ground and access before buying.";
        }

        public void Survey(int index)
        {
            SurveyedSite = Mathf.Clamp(index, 0, State.world.sites.Count - 1);
            view.Frame(State.world.sites[SurveyedSite]);
        }

        public bool Construct(IList<Vector2> accessRoute = null)
        {
            var step = State.Step;
            if (!State.Build(out string problem, SurveyedSite, accessRoute)) { Message = problem; return false; }
            InMenu = false; persist = true;
            if (step == CareerStep.ChooseSite) { BuildAirport(); view.Frame(State.Site); }
            if (step == CareerStep.ClearTrees || step == CareerStep.GradeLand || step == CareerStep.Expansion) valley.RefreshLand();
            if (step == CareerStep.Runway || step == CareerStep.Parking || step == CareerStep.Expansion) BuildAirport();
            if (State.HasAccess && access == null) access = valley.BuildAccess(State.Site, State.RoadNetwork());
            if (State.IsOpen && reception == null) reception = BuildReception();
            RefreshGroundCover();
            Message = step == CareerStep.Reception ? "Your airfield is open. A local charter is ready to visit." : "Construction complete.";
            Save(false);
            return true;
        }

        void BuildAirport()
        {
            ClearAirport();
            if (State.Site == null) return;
            var go = Instantiate(airportTemplate.gameObject, transform);
            go.name = State.Site.name + " Airfield";
            go.transform.position = new Vector3(State.Site.centre.x, State.handsOn && State.HasRunway ? State.layout.elevation : State.Site.elevation, State.Site.centre.y);
            Airport = go.GetComponent<Airport>(); Airport.timeScale = 0f; Airport.groundSize = 0f; Airport.windFrom = 270f;
            go.SetActive(true);
            if (!State.HasRunway) return;
            if (State.handsOn) { BuildPlayerAirport(); return; }
            float half = State.RunwayLength * .5f;
            var runway = Airport.AddRunway(new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), PavementSurface.Grass, 18f);
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

        GameObject BuildReception() => State.handsOn ? valley.BuildReception(State.layout.reception, State.layout.elevation, State.layout.receptionHeading) : valley.BuildReception(State.Site);

        void RefreshGroundCover()
        {
            var exclusions = new List<GroundCoverExclusion>();
            if (State.Site != null && State.HasRunway)
            {
                var c = State.Site.centre;
                void Strip(Vector2 a, Vector2 b, float radius) => exclusions.Add(new GroundCoverExclusion { a = a, b = b, radius = radius });
                if (State.handsOn)
                {
                    var l = State.layout;
                    Strip(l.runwayA, l.runwayB, 13f);
                    foreach (var e in l.TaxiSegments()) Strip(e.a, e.b, RunwayRules.TaxilaneClearance(7.5f));
                    if (l.receptionBuilt) Strip(l.reception, l.reception, 18f);
                }
                else
                {
                    Strip(c + Vector2.left * State.RunwayLength * .5f, c + Vector2.right * State.RunwayLength * .5f, 13f);
                    if (State.HasParking)
                    {
                        float entry = State.RunwayLength * .5f - 20f;
                        float clearance = RunwayRules.TaxilaneClearance(7.5f);
                        Strip(c + new Vector2(-entry, -65f), c + new Vector2(entry, -65f), clearance);
                        Strip(c + new Vector2(-entry, 0f), c + new Vector2(-entry, -65f), clearance);
                        Strip(c + new Vector2(entry, 0f), c + new Vector2(entry, -65f), clearance);
                        Strip(c, c + new Vector2(0f, -65f), clearance);
                    }
                    if (State.IsOpen) Strip(c + new Vector2(80f, -150f), c + new Vector2(80f, -150f), 18f);
                }
                // Use the real pad, including the taxi-out turnaround beyond its front-line anchor.
                if (Airport != null)
                    foreach (var stand in Airport.Taxiways.stands)
                    {
                        var pad = StandLayout.Of(stand); float radius = 0f;
                        foreach (var corner in pad.Pad) radius = Mathf.Max(radius, Vector2.Distance(corner, pad.Centre));
                        var centre = c + pad.Centre; Strip(centre, centre, radius + 2f);
                    }
            }
            valley.SetGroundCoverExclusions(exclusions);
        }

        public void PlayerChanged(CareerTool tool, Rect? terrainBounds = null)
        {
            persist = true;
            if (tool == CareerTool.ClearTrees) valley.RefreshForest(false);
            else if (tool == CareerTool.Level) valley.RefreshLand(terrainBounds ?? State.Site.Parcel);
            else if (tool == CareerTool.Runway || tool == CareerTool.Taxiway || tool == CareerTool.Stand) BuildAirport();
            else if (tool == CareerTool.Reception && reception == null) reception = BuildReception();
            if (tool != CareerTool.ClearTrees && tool != CareerTool.Level) RefreshGroundCover();
            Message = null;
        }
        public void SetMessage(string message) { Message = message; }

        public void FinishTerrainStroke(CareerTool tool, Rect strokeBounds)
        {
            if (tool == CareerTool.ClearTrees) valley.QueueClearingSurface(strokeBounds);
            else if (tool == CareerTool.Level) valley.QueueLevelSurface(strokeBounds);
            QueueAutosave();
        }

        public bool QueueAutosave()
        {
            saveTimer = 0f;
            if (!persist || replaying) return false;
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
            saveTimer = 0f;
            bool ok = Store.Save(State, out string problem);
            if (!ok || showMessage) Message = ok ? "Career saved." : problem;
            return ok;
        }

        public bool Continue()
        {
            if (!Store.Load(out var state, out string problem)) { Message = problem; return false; }
            state.UpgradeToPlayerConstruction();
            Initialize(state); Message = problem ?? "Career resumed."; return true;
        }

        void Update()
        {
            if (store != null && store.TryTakeSaveProblem(out string problem)) Message = problem;
            if (State == null || InMenu) return;
            saveTimer += Time.unscaledDeltaTime;
            if (saveTimer >= 15f) QueueAutosave();
            if (Paused) return;
            if (!State.charterActive)
            {
                State.roadTrafficSeconds += Time.unscaledDeltaTime * Speed;
                valley.SetRoadClock(State.roadTrafficSeconds); return;
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
