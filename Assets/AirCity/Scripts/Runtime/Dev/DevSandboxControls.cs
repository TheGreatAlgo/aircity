using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// Sandbox keys for trying the aircraft features (development only, not game UI; DevToolbar has buttons too):
    ///   left click = select an aircraft (hover highlights); with the delete tool (X) it removes it   N = day / night
    ///   1 = beacons   2 = strobes   3 = landing + taxi lights   4 = logo lights
    ///   R = engines (props / fans spin)   T = taxi selected aircraft   G = dirt on selected   +/- = time of day
    ///   Enter = push the selected aircraft back off its stand (a tug comes for it; Shift: facing the other way)
    /// Flights (departures, arrivals, the wind) are DevTrafficControls'. Aircraft on a flight set their own lights and
    /// engines; the toggles here are for the rest.
    /// </summary>
    public class DevSandboxControls : MonoBehaviour
    {
        public Light sun;
        [Range(0f, 1f)] public float night;

        Camera cam;
        Airport airport;
        Aircraft.Aircraft hovered, selected;
        Color hoveredColor;
        float sunIntensity = 2f;

        // ------------------------------------------------------------------ for the toolbar

        public bool Engines { get; set; }
        public bool Beacons { get; set; }
        public bool Strobes { get; set; }
        public bool Landing { get; set; }
        public bool Logos { get; set; }
        /// <summary>The selected aircraft taxis round in a slow weave.</summary>
        public bool Taxiing { get; set; }

        public Aircraft.Aircraft Selected => selected;
        /// <summary>Why the last push-back or park didn't happen (cleared when the selection changes).</summary>
        public string Message { get; private set; }

        public Airport Airport => airport != null ? airport : airport = FindAnyObjectByType<Airport>();

        /// <summary>On a flight (taxiing, flying): its flight moves it and sets its lights.</summary>
        bool Flying(Aircraft.Aircraft a) => a != null && Airport != null && Airport.Traffic.FlightOf(a) != null;

        /// <summary>The push-back waiting or under way for the selected aircraft.</summary>
        public TowJob SelectedTow => selected != null && Airport != null ? Airport.Maintenance.TowOf(selected) : null;

        /// <summary>Send a tug to push the selected aircraft back off its stand (otherWay: to face the other way).</summary>
        public void PushBack(bool otherWay = false)
        {
            if (selected == null || Airport == null || Flying(selected)) return;
            Taxiing = false;
            Airport.Maintenance.PushBack(selected, out string problem, otherWay);
            Message = problem != null ? "Can't push back: " + problem : null;
        }

        /// <summary>Put the selected aircraft on the nearest free stand that takes it (a sandbox shortcut, no tug).</summary>
        public void Park()
        {
            if (selected == null || Airport == null || SelectedTow != null || Flying(selected)) return;
            var a = Airport;
            var t = a.transform;
            var here = Geo.XZ(t.InverseTransformPoint(selected.transform.position));
            StandLayout best = null;
            float bestD = float.MaxValue;
            var net = a.Taxiways;
            foreach (var l in net.Layouts())
            {
                if (l.stand.DeIcing || net.CheckStand(l.stand, selected.type) != StandProblem.None) continue;
                bool taken = false;
                foreach (var other in Aircraft.Aircraft.All)
                    if (other != null && other != selected && l.Contains(Geo.XZ(t.InverseTransformPoint(other.transform.position))))
                        taken = true;
                float d = Vector2.Distance(here, l.ParkPosition(selected.type));
                if (!taken && d < bestD)
                {
                    bestD = d;
                    best = l;
                }
            }
            if (best == null)
            {
                Message = "No free stand takes it";
                return;
            }
            Taxiing = false;
            selected.transform.SetPositionAndRotation(t.TransformPoint(Geo.X0Z(best.ParkPosition(selected.type))),
                Quaternion.LookRotation(t.TransformDirection(Geo.X0Z(best.nose)), Vector3.up));
            Message = null;
        }
        public Aircraft.Aircraft Hovered => hovered;
        /// <summary>The frame the aircraft was selected (DevToolbar deletes whichever of it and the airfield selection
        /// came last).</summary>
        public int SelectedFrame { get; private set; }

        public bool Night
        {
            get => night > 0.5f;
            set
            {
                night = value ? 1f : 0f;
                ApplyTimeOfDay();
            }
        }

        /// <summary>A quarter more dirt on the selected aircraft (clean again after full).</summary>
        public void AddDirt()
        {
            var a = selected != null ? selected.GetComponent<AircraftLiveryApplier>() : null;
            if (a != null) a.SetDirt(a.dirt >= 0.99f ? 0f : a.dirt + 0.25f);
        }

        public bool DeleteSelected()
        {
            if (selected == null) return false;
            Remove(selected);
            return true;
        }

        // ------------------------------------------------------------------ frame

        void Start()
        {
            cam = GetComponent<Camera>();
            if (sun != null) sunIntensity = sun.intensity;
            ApplyTimeOfDay();
            if (GetComponent<DevToolbar>() == null) gameObject.AddComponent<DevToolbar>();
            if (GetComponent<DevTrafficControls>() == null) gameObject.AddComponent<DevTrafficControls>();
        }

        void Update()
        {
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse != null && cam != null)
            {
                Aircraft.Aircraft under = null;
                bool free = !DevGui.PointerOverHud;
                if (free) Aircraft.Aircraft.TryPick(cam.ScreenPointToRay(mouse.position.ReadValue()), out under);
                if (under != hovered)
                {
                    if (hovered != null)
                    {
                        hovered.hoverColor = hoveredColor;
                        if (hovered != selected) hovered.Highlight = AircraftHighlight.None;
                    }
                    hovered = under;
                    if (hovered != null)
                    {
                        hoveredColor = hovered.hoverColor;
                        if (hovered != selected) hovered.Highlight = AircraftHighlight.Hover;
                    }
                }
                // red under the delete tool
                if (hovered != null) hovered.hoverColor = DevAirfieldTool.Deleting ? DevGui.Danger : hoveredColor;
                if (mouse.leftButton.wasPressedThisFrame && free)
                {
                    if (DevAirfieldTool.Deleting)
                    {
                        if (under != null) Remove(under);
                    }
                    else if (!DevAirfieldTool.Active)
                    {
                        if (selected != null) selected.Highlight = AircraftHighlight.None;
                        if (under != selected) Message = null;
                        selected = under;
                        SelectedFrame = Time.frameCount;
                        if (selected != null) selected.Highlight = AircraftHighlight.Selected;
                    }
                }
            }
            if (keys == null) return;
            if (keys.nKey.wasPressedThisFrame) Night = !Night;
            if (keys.equalsKey.isPressed || keys.minusKey.isPressed)
            {
                night = Mathf.Clamp01(night + (keys.equalsKey.isPressed ? 0.5f : -0.5f) * Time.deltaTime);
                ApplyTimeOfDay();
            }
            if (keys.digit1Key.wasPressedThisFrame) Beacons = !Beacons;
            if (keys.digit2Key.wasPressedThisFrame) Strobes = !Strobes;
            if (keys.digit3Key.wasPressedThisFrame) Landing = !Landing;
            if (keys.digit4Key.wasPressedThisFrame) Logos = !Logos;
            if (keys.rKey.wasPressedThisFrame) Engines = !Engines;
            if (keys.tKey.wasPressedThisFrame) Taxiing = !Taxiing;
            if (keys.gKey.wasPressedThisFrame) AddDirt();
            if (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame) PushBack(keys.shiftKey.isPressed);

            foreach (var l in FindObjectsByType<AircraftLights>(FindObjectsInactive.Exclude))
            {
                if (Flying(l.GetComponent<Aircraft.Aircraft>())) continue;
                l.beacon = Beacons || Engines;
                l.strobes = Strobes;
                l.landing = l.taxi = Landing;
                l.logo = Logos;
            }
            foreach (var p in FindObjectsByType<PropellerSpinner>(FindObjectsInactive.Exclude))
                if (!Flying(p.GetComponentInParent<Aircraft.Aircraft>())) p.rpm = Engines ? (p.name.StartsWith("Fan") ? 1100f : 900f) : 0f;
            // (a tug moves an aircraft it's pushing back)
            if (selected != null && SelectedTow == null && !Flying(selected))
            {
                var motion = selected.GetComponent<AircraftGroundMotion>();
                if (motion != null)
                {
                    float speed = Taxiing ? 6f : 0f;
                    float steer = Taxiing ? Mathf.Sin(Time.time * 0.4f) * 0.6f : 0f;
                    motion.Drive(speed, steer);
                    if (Taxiing)
                    {
                        float turn = motion.SteeringAngle == 0f ? 0f
                            : speed / motion.TurnRadius(selected.type) * Mathf.Rad2Deg * Mathf.Sign(motion.SteeringAngle);
                        selected.transform.Rotate(0f, turn * Time.deltaTime, 0f);
                        selected.transform.position += selected.transform.forward * speed * Time.deltaTime;
                    }
                }
            }
        }

        void Remove(Aircraft.Aircraft a)
        {
            if (a == hovered) hovered = null;
            if (a == selected) selected = null;
            a.Highlight = AircraftHighlight.None;
            Destroy(a.gameObject);
        }

        void ApplyTimeOfDay()
        {
            AircraftLights.Glow = night;
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(sunIntensity, 0.08f, night);
                sun.color = Color.Lerp(new Color(1f, 0.96f, 0.9f), new Color(0.55f, 0.65f, 1f), night);
            }
            RenderSettings.ambientIntensity = Mathf.Lerp(1f, 0.25f, night);
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.62f, 0.70f, 0.82f), new Color(0.08f, 0.10f, 0.18f), night);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.50f, 0.52f, 0.55f), new Color(0.06f, 0.07f, 0.10f), night);
        }
    }
}
