using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    public partial class CareerGame
    {
        public DevAirfieldTool BuildTools { get; private set; }
        public PublicRoadsView Streets { get; private set; }
        public bool SharedTools => BuildTools != null;
        public bool Sandbox => State != null && State.mode == GameMode.Sandbox;
        int observedLayout;

        void BindSharedTools()
        {
            if (view == null || !State.handsOn) return;
            var host = view.gameObject;
            BuildTools = host.GetComponent<DevAirfieldTool>();
            if (BuildTools == null)
            {
                BuildTools = host.AddComponent<DevAirfieldTool>();
                var defaults = GameAssetCatalog.Load()?.toolDefaults;
                if (defaults != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(defaults), BuildTools);
            }
            host.GetComponent<DevToolbar>()?.ResetWorkspace();
            if (Airport != null)
            {
                Airport.TerrainHeight = p => State.world.Height(p + Geo.XZ(Airport.transform.position)) - Airport.transform.position.y;
                if (Airport.Taxiways.edges.Any(e => e.structure != RoadStructure.Ground)) Airport.Taxiways.Rebuild();
            }
            BuildTools.game = this; BuildTools.airport = Airport; BuildTools.SetTool(DevTool.Select);BuildTools.SetRoadUnderground(false);
            if (Airport != null && !State.HasRunway) BuildTools.UseStarterDefaults();
            var catalog = GameAssetCatalog.Load();
            if (Airport != null && catalog != null)
            {
                Airport.externallySimulated = true;
                Airport.aircraftTypes = catalog.entries.Select(e => e.asset).OfType<AircraftType>().Distinct().ToList();
                if (Airport.GetComponent<BridgeRig>() == null && catalog.bridgeDefaults != null)
                {
                    var rig = Airport.gameObject.AddComponent<BridgeRig>();
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(catalog.bridgeDefaults), rig); rig.Refresh();
                }
                if (Airport.GetComponent<AirCity.Characters.MarshallerPresentation>() == null && catalog.marshallerDefaults != null)
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(catalog.marshallerDefaults), Airport.gameObject.AddComponent<AirCity.Characters.MarshallerPresentation>());
            }
            if (host.GetComponent<DevToolbar>() == null) host.AddComponent<DevToolbar>();
            if (host.GetComponent<DevBaggageControls>() == null) host.AddComponent<DevBaggageControls>();
            if (host.GetComponent<DevContainerControls>() == null) host.AddComponent<DevContainerControls>();
            var aircraft = host.GetComponent<DevSandboxControls>();
            if (aircraft == null) aircraft = host.AddComponent<DevSandboxControls>();
            if (host.GetComponent<DevTrafficControls>() == null) host.AddComponent<DevTrafficControls>();
            if (Streets == null)
            {
                var go = new GameObject("Shared public road tool context"); go.transform.SetParent(transform, false);
                Streets = go.AddComponent<PublicRoadsView>();
            }
            Streets.RenderNetwork = RoadsChanged; Streets.network = State.RoadNetwork();
            if (Airport != null)
            {
                var accounts = Airport.Deicing.Accounts;
                accounts.sharedBalance = () => State.funds - State.expenseRemainder;
                accounts.sharedFreeSpending = () => Sandbox;
                accounts.sharedExpense = amount => {
                    if (Sandbox || replaying) return;
                    double total = State.expenseRemainder + amount;
                    int whole = (int)Math.Floor(total);
                    State.funds = Mathf.Max(0, State.funds - whole); State.expenseRemainder = total - whole;
                };
            }
            observedLayout = Airport != null ? Airport.LayoutVersion : -1;
        }

        void UpdateSharedTools()
        {
            if (BuildTools == null) return;
            BuildTools.airport = Airport;
            bool active = State != null && State.Site != null;
            var host = BuildTools.gameObject;
            BuildTools.enabled = active;
            foreach (var component in host.GetComponents<MonoBehaviour>())
                if (component is DevToolbar || component is DevSandboxControls || component is DevTrafficControls || component is DevBaggageControls || component is DevContainerControls)
                    component.enabled = active;
            if (!active || Airport == null) return;
            view.PointerOverHud |= DevGui.PointerOverHud;
            view.KeyboardCaptured |= DevGui.KeyboardCaptured;
            view.PlacementTakesScroll |= DevAirfieldTool.TakesScroll;
            if (Airport.LayoutVersion != observedLayout && BuildTools.PlacingRunway == null)
            {
                observedLayout = Airport.LayoutVersion;
                SyncProgress(); RefreshGroundCover();
            }
        }

        public void SetMode(GameMode mode)
        {
            if (!Enum.IsDefined(typeof(GameMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (State == null || State.mode == mode) return;
            GetComponent<CareerBuildTool>()?.FinishEditing();
            BuildTools?.SetTool(DevTool.Select);
            State.mode = mode;
            Message = mode == GameMode.Sandbox ? "Sandbox mode · all tools unlocked, construction is free." : "Career mode · funds, progression and objectives apply.";
            if (State.Site != null) QueueAutosave();
        }

        public string ToolProblem(DevTool tool)
        {
            if (tool == DevTool.Select) return null;
            if (State == null || State.Site == null) return "Start building in a region first.";
            if (State.charterActive) return "Wait for the aircraft before changing the airfield.";
            if (Sandbox) return null;
            CareerTool required = tool switch {
                DevTool.ClearTrees => CareerTool.ClearTrees, DevTool.Level => CareerTool.Level,
                DevTool.Runway => CareerTool.Runway, DevTool.Taxiway => CareerTool.Taxiway,
                DevTool.Stand => CareerTool.Stand, DevTool.Street => CareerTool.Road,
                DevTool.Reception => CareerTool.Reception, DevTool.Delete => CareerTool.Select,
                _ => CareerTool.Taxiway
            };
            return State.layout.Unlocked(required) ? null : "Complete the current task to unlock this tool.";
        }

        public bool CanSpend(int price, out string problem)
        {
            problem = !Sandbox && State.funds < price ? "Not enough funds for this work." : null;
            return problem == null;
        }
        public void Spend(int price) { if (!Sandbox) State.funds -= price; }

        public bool CheckFootprint(IEnumerable<Vector2> local, float radius, out string problem)
        {
            var origin = Geo.XZ(Airport.transform.position);
            var points = local.Select(p => p + origin).ToArray();
            problem = "Keep construction inside the region.";
            if(points.Length==0)return false;
            if (points.Any(p => !ValleyWorld.Finite(p.x) || !ValleyWorld.Finite(p.y) || !State.BuildableBounds.Contains(p))) return false;
            problem = "Prepare dry land above sea level before building here.";
            if (State.world.HasOcean && (Airport.transform.position.y <= State.world.SeaLevel + .5f || points.Any(State.world.IsWater))) return false;
            problem = "Clear the trees in this footprint first.";
            float reach=radius+6f;
            var bounds=Rect.MinMaxRect(points.Min(p=>p.x)-reach,points.Min(p=>p.y)-reach,points.Max(p=>p.x)+reach,points.Max(p=>p.y)+reach);
            foreach(int i in ForestQuery.Candidates(State.world,bounds))
            {
                var tree=State.world.trees[i];
                if(!tree.removed && points.Any(p=>(tree.position-p).sqrMagnitude<reach*reach))return false;
            }
            if(valley!=null)
            {
                float prop=GroundCoverScatter.MaximumFootprintRadius;
                var search=Rect.MinMaxRect(bounds.xMin-prop,bounds.yMin-prop,bounds.xMax+prop,bounds.yMax+prop);
                problem="Clear the rocks and vegetation in this footprint first.";
                foreach(var item in valley.GroundCandidates(search))
                {
                    if(!CareerConstruction.Clearable(item.kind) || State.world.GroundPropRemoved(item.id))continue;
                    float radiusWithProp=reach+GroundCoverScatter.FootprintRadius(item.kind,item.scale);
                    if(points.Any(p=>(item.position-p).sqrMagnitude<radiusWithProp*radiusWithProp))return false;
                }
            }
            problem = "Level this footprint to meet the airport pavement first.";
            if (points.Any(p => Mathf.Abs(State.world.Height(p) - Airport.transform.position.y - Airport.PavementElevationAt(p-origin)) > .3f)) return false;
            problem = null; return true;
        }

        public void SharedConstructionChanged()
        {
            SyncProgress(); RefreshGroundCover(); QueueAutosave();
        }
        void SyncProgress()
        {
            if (Airport == null) return;
            var l = State.layout; var origin = Geo.XZ(Airport.transform.position);
            var runway = Airport.Runways.Where(r => (r.gameObject.hideFlags & HideFlags.DontSave) == 0).OrderByDescending(r => r.Length).FirstOrDefault();
            l.runwayBuilt = runway != null;
            if (runway != null)
            {
                l.runwayA = Geo.XZ(runway.start) + origin; l.runwayB = Geo.XZ(runway.end) + origin;
                l.elevation = Airport.transform.position.y; l.Unlock(CareerTool.Taxiway);
            }
            var net = Airport.Taxiways;
            l.taxiways = net.edges.Where(e => !e.IsRoad).Select(e => new CareerTaxiway { points = new List<Vector2> { net.At(e.a) + origin, net.At(e.b) + origin } }).ToList();
            if (l.taxiways.Count > 0) l.Unlock(CareerTool.Stand);
            var stand = net.stands.FirstOrDefault(s => !s.DeIcing);
            l.standBuilt = stand != null;
            if (stand != null) { l.stand = Geo.XZ(stand.position) + origin; l.standHeading = stand.heading; l.standAlong = stand.Front; l.Unlock(CareerTool.Road); }
            if (State.HasAccess) l.Unlock(CareerTool.Reception);
            State.expanded = State.completedCharters > 0 && State.RunwayLength >= 1000f;
        }

        void RoadsChanged(PublicRoadNetwork network)
        {
            State.publicRoads = network;
            var outside = network.nodes.Where(n => n.outsideConnection || Mathf.Abs(n.position.z) >= ValleyWorld.Size * .5f - 1f).ToArray();
            var entrance = network.nodes.FirstOrDefault(n => n.terminalEntrance == "airport-reception");
            if (entrance == null && State.Site != null)
            {
                // The pre-existing region road is already connected. Only an airport end near the player's
                // parking can satisfy access; owning the whole map must not make that objective complete itself.
                var airportEnd = State.layout.standBuilt ? State.layout.stand : State.Site.centre;
                entrance = network.nodes.Where(n => State.BuildableBounds.Contains(PublicRoadNetwork.XZ(n.position)) &&
                    !State.world.publicRoad.Any(p => Vector2.Distance(p, PublicRoadNetwork.XZ(n.position)) < .5f) &&
                    Vector2.Distance(PublicRoadNetwork.XZ(n.position), airportEnd) <= 300f && outside.Any(o => network.HasRoute(n.id, o.id)))
                    .OrderBy(n => Vector2.Distance(PublicRoadNetwork.XZ(n.position), airportEnd)).FirstOrDefault();
            }
            if (entrance != null && outside.Any(n => network.HasRoute(entrance.id, n.id)))
            {
                entrance.terminalEntrance = "airport-reception";
                // Progression remembers the airport end; the graph is the sole saved road layout.
                State.accessRoadRoute = AccessRoute(network, entrance.id, new HashSet<int>(outside.Select(n => n.id)));
                State.layout.Unlock(CareerTool.Reception);
            }
            else State.accessRoadRoute.Clear();
            valley.RefreshRoads(network);
            SharedConstructionChanged();
        }

        static List<Vector2> AccessRoute(PublicRoadNetwork network, int start, HashSet<int> outside)
        {
            var previous = new Dictionary<int, int> { [start] = -1 }; var queue = new Queue<int>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (outside.Contains(current))
                {
                    var path = new List<Vector2>();
                    for (int at = current; at >= 0; at = previous[at]) path.Add(PublicRoadNetwork.XZ(network.Node(at).position));
                    path.Reverse(); return path;
                }
                foreach (var e in network.At(current))
                {
                    int next = e.a == current ? e.b : e.a;
                    if ((e.a == current || e.BackwardLanes > 0) && !previous.ContainsKey(next)) { previous[next] = current; queue.Enqueue(next); }
                }
            }
            return new List<Vector2>();
        }

        bool CaptureAirport()
        {
            if (!SharedTools || Airport == null) return true;
            if (BuildTools.PlacingRunway != null) { Message = "Finish or cancel the runway before saving."; return false; }
            try { SyncProgress(); State.airport = AirportSaveData.Capture(Airport, GameAssetCatalog.Load()); return true; }
            catch (Exception e) { Message = "Could not save the airport: " + e.Message; return false; }
        }
    }
}
