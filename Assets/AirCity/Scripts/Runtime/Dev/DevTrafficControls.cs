using System.Collections.Generic;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Traffic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// Sandbox flights (development only, not game UI; DevToolbar has the buttons):
    ///   Space = the selected aircraft departs (a tug pushes it back first where it needs one)
    ///   the selected aircraft's way is drawn on the ground: where it will taxi, the holds on the way (red until it's
    ///   cleared past them, green after) and where it ends (the runway in use, or its stand); an arriving aircraft's
    ///   final approach too. A parked one shows the way it would go if it left now.
    ///   Land: a new aircraft of a type comes in on final. Auto traffic sends arrivals every few minutes and sends
    ///   aircraft that have stood a while on their way. The wind picks which way the runways are used.
    /// New aircraft are copies of the aircraft in the scene when it starts (one of each type), in their liveries.
    /// </summary>
    [RequireComponent(typeof(DevSandboxControls))]
    public class DevTrafficControls : MonoBehaviour
    {
        [Tooltip("Game seconds between arrivals with auto traffic on.")]
        public float arrivalEvery = 150f;
        [Tooltip("Game seconds a parked aircraft stands before auto traffic sends it off.")]
        public float turnaround = 300f;

        static readonly Color RouteColour = new Color(0.35f, 0.85f, 1f), PreviewColour = new Color(0.35f, 0.85f, 1f, 0.55f),
            ArrivalColour = new Color(0.55f, 1f, 0.55f), HoldColour = new Color(1f, 0.3f, 0.25f), ClearedColour = new Color(0.55f, 1f, 0.55f);

        DevSandboxControls sandbox;
        Camera cam;
        readonly DevOutlines outlines = new DevOutlines();
        readonly List<Vector2> pts2 = new List<Vector2>();
        readonly List<Vector3> pts = new List<Vector3>();
        readonly Dictionary<AircraftType, GameObject> templates = new Dictionary<AircraftType, GameObject>();
        readonly List<AircraftLiveryAsset> liveries = new List<AircraftLiveryAsset>();
        readonly Dictionary<Aircraft.Aircraft, float> parkedSince = new Dictionary<Aircraft.Aircraft, float>();
        TaxiRoute preview;
        Aircraft.Aircraft previewFor;
        string previewProblem;
        float previewAt = -1f, nextArrival;
        int arrivals;

        public bool AutoTraffic { get; set; }
        public string Message { get; private set; }

        Airport Airport => sandbox != null ? sandbox.Airport : null;
        public AirTraffic Traffic => Airport != null ? Airport.Traffic : null;

        /// <summary>The selected aircraft's flight (null if it's parked or there's none selected).</summary>
        public Flight SelectedFlight => sandbox != null && Traffic != null ? Traffic.FlightOf(sandbox.Selected) : null;

        /// <summary>Why the selected aircraft couldn't leave now (from its preview).</summary>
        public string PreviewProblem => previewProblem;

        public IEnumerable<AircraftType> Types => templates.Keys;

        // ------------------------------------------------------------------ actions

        /// <summary>The selected aircraft leaves: pushed back if it needs to be, taxis to the runway in use, takes off.</summary>
        public void Depart()
        {
            var a = sandbox.Selected;
            if (a == null || Traffic == null) return;
            sandbox.Taxiing = false;
            var f = Traffic.Depart(a, out string problem);
            Message = f == null ? "Can't depart: " + problem : null;
            preview = null;
        }

        /// <summary>A new aircraft of this type comes in to land.</summary>
        public void Land(AircraftType type)
        {
            if (Traffic == null || !templates.TryGetValue(type, out var template)) return;
            if (!Traffic.CanArrive(type, out string problem))
            {
                Message = $"No {type.displayName} can land: {problem}";
                return;
            }
            var go = Instantiate(template, Airport.transform);
            go.hideFlags = HideFlags.DontSave;   // not the hidden template's flags: it shows in the hierarchy
            go.name = $"{type.displayName} {Registration()}";
            go.SetActive(true);
            var applier = go.GetComponent<AircraftLiveryApplier>();
            if (applier != null && liveries.Count > 0) applier.SetLivery(liveries[Random.Range(0, liveries.Count)], go.name.Substring(go.name.LastIndexOf(' ') + 1));
            var a = go.GetComponent<Aircraft.Aircraft>();
            var f = Traffic.Arrive(a, out problem);
            if (f == null)
            {
                Destroy(go);
                Message = $"No {type.displayName} can land: {problem}";
                return;
            }
            Message = null;
        }

        public float Wind => Airport != null ? Airport.windFrom : 0f;

        public void TurnWind(float degrees)
        {
            if (Airport == null) return;
            Airport.windFrom = Mathf.Repeat(Mathf.Round((Airport.windFrom + degrees) / 10f) * 10f, 360f);
            preview = null;
        }

        string Registration()
        {
            arrivals++;
            const string letters = "ABCDEFGHJKLMNPRSTUVWXYZ";
            var r = new System.Random(arrivals * 7919 + 17);
            return "PH-" + letters[r.Next(letters.Length)] + letters[r.Next(letters.Length)] + letters[r.Next(letters.Length)];
        }

        // ------------------------------------------------------------------ frame

        void Start()
        {
            sandbox = GetComponent<DevSandboxControls>();
            cam = GetComponent<Camera>();
            // one hidden copy of each type in the scene, to make new arrivals from
            foreach (var a in FindObjectsByType<Aircraft.Aircraft>(FindObjectsInactive.Exclude))
            {
                if (a.type == null) continue;
                var applier = a.GetComponent<AircraftLiveryApplier>();
                if (applier != null && applier.liveryAsset != null && !liveries.Contains(applier.liveryAsset)) liveries.Add(applier.liveryAsset);
                if (templates.ContainsKey(a.type)) continue;
                var copy = Instantiate(a.gameObject);
                copy.SetActive(false);
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.name = a.type.displayName;
                templates[a.type] = copy;
            }
        }

        void OnDestroy()
        {
            outlines.Destroy();
            foreach (var t in templates.Values)
                if (t != null) Destroy(t);
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys != null && keys.spaceKey.wasPressedThisFrame && SelectedFlight == null) Depart();
            if (sandbox.Selected != previewFor) Message = null;
            if (AutoTraffic) Auto();
        }

        void LateUpdate() => Draw();

        /// <summary>Arrivals every arrivalEvery game seconds (of a type that has a free stand), and the aircraft that
        /// have stood longest sent off after their turnaround.</summary>
        void Auto()
        {
            var traffic = Traffic;
            if (traffic == null) return;
            float now = traffic.Clock;
            // how long each has stood
            var stood = new List<Aircraft.Aircraft>();
            foreach (var a in Aircraft.Aircraft.All)
            {
                if (a == null || a.type == null || traffic.FlightOf(a) != null || a.Moving || a.transform.position.y > 1f) continue;
                if (!parkedSince.ContainsKey(a)) parkedSince[a] = now;
                stood.Add(a);
            }
            foreach (var a in new List<Aircraft.Aircraft>(parkedSince.Keys))
                if (a == null || !stood.Contains(a)) parkedSince.Remove(a);
            int leaving = 0;
            foreach (var f in traffic.Flights)
                if (f.kind == FlightKind.Departure && f.Phase <= FlightPhase.StartingEngines) leaving++;
            if (leaving < 2)
            {
                Aircraft.Aircraft longest = null;
                float since = float.MaxValue;
                foreach (var a in stood)
                    if (parkedSince[a] < since && now - parkedSince[a] >= turnaround && a != sandbox.Selected)
                    {
                        since = parkedSince[a];
                        longest = a;
                    }
                if (longest != null)
                {
                    if (traffic.Depart(longest, out _) == null) parkedSince[longest] = now;   // try again after another turnaround
                }
            }
            if (now >= nextArrival)
            {
                nextArrival = now + arrivalEvery * Random.Range(0.7f, 1.3f);
                var types = new List<AircraftType>(templates.Keys);
                for (int i = types.Count - 1; i > 0; i--)
                {
                    int k = Random.Range(0, i + 1);
                    (types[i], types[k]) = (types[k], types[i]);
                }
                foreach (var t in types)
                    if (traffic.CanArrive(t, out _))
                    {
                        Land(t);
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ the selected aircraft's way

        void Draw()
        {
            outlines.Begin();
            var a = sandbox != null ? sandbox.Selected : null;
            if (a != null && cam != null && Traffic != null && DevGui.Visible)
            {
                var f = Traffic.FlightOf(a);
                if (f != null && f.Route != null) DrawFlight(f);
                else if (!a.Moving && Airport.Maintenance.TowOf(a) == null) DrawPreview(a);
            }
            outlines.End();
        }

        void DrawPreview(Aircraft.Aircraft a)
        {
            // planned again every second (the wind, the layout or the traffic may have changed)
            if (a != previewFor || Time.unscaledTime - previewAt > 1f)
            {
                previewFor = a;
                previewAt = Time.unscaledTime;
                preview = Traffic.PreviewDeparture(a, out previewProblem);
            }
            if (preview == null) return;
            DrawRoute(preview, 0f, PreviewColour, false, true);
        }

        void DrawFlight(Flight f)
        {
            previewFor = null;
            var route = f.Route;
            var colour = f.kind == FlightKind.Arrival ? ArrivalColour : RouteColour;
            var t = Airport.transform;
            if (f.Phase == FlightPhase.Approach || f.Phase == FlightPhase.GoAround)
            {
                // the final approach, down to touchdown
                var r = route.runway;
                var axis = route.fromStart ? r.Axis2D : -r.Axis2D;
                var thr = Geo.XZ(route.fromStart ? r.start : r.end);
                var td = t.TransformPoint(Geo.X0Z(thr + axis * route.touchdownS));
                pts.Clear();
                pts.Add(f.aircraft.transform.position);
                pts.Add(td);
                outlines.Line(cam, pts, colour, 2.5f);
            }
            if (f.Phase == FlightPhase.TakeoffRoll || f.Phase == FlightPhase.Climb)
            {
                // the takeoff: along the runway and out
                var r = route.runway;
                var axis = route.fromStart ? r.Axis2D : -r.Axis2D;
                pts.Clear();
                pts.Add(f.aircraft.transform.position);
                pts.Add(t.TransformPoint(Geo.X0Z(Geo.XZ(route.fromStart ? r.end : r.start) + axis * 600f, 60f)));
                outlines.Line(cam, pts, colour, 2.5f);
                return;
            }
            bool started = f.Taxiing || f.Phase == FlightPhase.Landing;
            DrawRoute(route, started ? f.Along : 0f, colour, true, f.Phase <= FlightPhase.HandPush);
        }

        /// <summary>The way from `from` (nose wheel, metres along) to the end, the holds ahead and where it ends; with
        /// `push`, the push-back off its stand before it.</summary>
        void DrawRoute(TaxiRoute route, float from, Color colour, bool live, bool push)
        {
            var t = Airport.transform;
            var path = route.path;
            if (push && route.pushBack != null)
            {
                var nose = route.pushBack.NosePoints;
                pts.Clear();
                for (int i = 0; i < nose.Count; i += 8) pts.Add(t.TransformPoint(Geo.X0Z(nose[i])));
                pts.Add(t.TransformPoint(Geo.X0Z(nose[nose.Count - 1])));
                outlines.Line(cam, pts, colour, 3f);
            }
            pts2.Clear();
            path.Points(from, path.Length, 3f, pts2);
            pts.Clear();
            foreach (var p in pts2) pts.Add(t.TransformPoint(Geo.X0Z(p)));
            outlines.Line(cam, pts, colour, 3f);
            // the holds still ahead: a bar across the way where the nose stops
            foreach (var h in route.holds)
            {
                if (h.at < from || h.kind == HoldKind.Route) continue;
                path.Pose(h.at, out _, out var facing, out _, out _);
                var across = Geo.Right(facing) * 9f;
                var at = path.NoseAt(Mathf.RoundToInt(h.at / TaxiPath.Step)) + facing * 2f;
                pts.Clear();
                pts.Add(t.TransformPoint(Geo.X0Z(at - across)));
                pts.Add(t.TransformPoint(Geo.X0Z(at + across)));
                outlines.Line(cam, pts, live && h.cleared ? ClearedColour : HoldColour, 3f);
            }
            // where it ends: the stand's pad, or a box round the line-up point
            pts.Clear();
            if (route.stand != null)
                foreach (var c in route.stand.Pad) pts.Add(t.TransformPoint(Geo.X0Z(c)));
            else
            {
                var end = path.End;
                var f = path.EndHeading;
                var r = Geo.Right(f);
                foreach (var (x, y) in new[] { (-8f, -4f), (8f, -4f), (8f, 12f), (-8f, 12f) })
                    pts.Add(t.TransformPoint(Geo.X0Z(end + r * x + f * y)));
            }
            outlines.Loop(cam, pts, colour, 3f);
        }
    }
}
