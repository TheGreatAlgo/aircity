using AirCity.Aircraft;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// Sandbox keys for trying the aircraft features (development only, not game UI; DevToolbar has buttons too):
    ///   left click = select an aircraft (hover highlights); with the delete tool (X) it removes it   N = day / night
    ///   1 = beacons   2 = strobes   3 = landing + taxi lights   4 = logo lights
    ///   R = engines (props / fans spin)   T = taxi selected aircraft   G = dirt on selected   +/- = time of day
    /// </summary>
    public class DevSandboxControls : MonoBehaviour
    {
        public Light sun;
        [Range(0f, 1f)] public float night;

        Camera cam;
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

            foreach (var l in FindObjectsByType<AircraftLights>(FindObjectsInactive.Exclude))
            {
                l.beacon = Beacons || Engines;
                l.strobes = Strobes;
                l.landing = l.taxi = Landing;
                l.logo = Logos;
            }
            foreach (var p in FindObjectsByType<PropellerSpinner>(FindObjectsInactive.Exclude))
                p.rpm = Engines ? (p.name.StartsWith("Fan") ? 1100f : 900f) : 0f;
            if (selected != null)
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
