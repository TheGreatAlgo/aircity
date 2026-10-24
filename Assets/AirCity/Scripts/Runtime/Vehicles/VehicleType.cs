using System;
using AirCity.Airfield;
using UnityEngine;

namespace AirCity.Vehicles
{
    public enum VehicleKind
    {
        SnowPlough = 0,
        Mower = 1,
        /// <summary>Not a vehicle: the maintenance depot building (it shares the pipeline for its model and lights).</summary>
        Depot = 2,
    }

    public enum VehicleLightType
    {
        /// <summary>Headlights and high plough / road lights (white, forward).</summary>
        Head = 0,
        /// <summary>Tail lights (red, backward); on with the headlights.</summary>
        Tail = 1,
        /// <summary>Rotating amber beacon, on whenever the vehicle is out on the airfield.</summary>
        Beacon = 2,
        /// <summary>Work lights aimed at the blade, broom or mower decks (white).</summary>
        Work = 3,
        /// <summary>Steady amber markers at the blade ends / mower wing tips.</summary>
        Marker = 4,
        /// <summary>Building flood lights (white, down onto the forecourt).</summary>
        Flood = 5,
    }

    [Serializable]
    public struct VehicleLight
    {
        public string name;
        public VehicleLightType type;
        public Vector3 position, direction;
        public Color color;
        public float size;
        [Tooltip("Rotating beacons: phase offset (0..1) so two beacons don't turn in step.")]
        public float phase;
    }

    [Serializable]
    public struct VehicleWheel
    {
        public string name;
        public Vector3 centre;
        public float radius;
        public bool steerable;
    }

    /// <summary>A building's roller door (the depot): Door_k rolls up into its hood (Unity scales its local y).</summary>
    [Serializable]
    public struct VehicleDoor
    {
        public string name;
        [Tooltip("Bottom centre of the opening, on the outer wall.")]
        public Vector3 position;
        public float width, height;
    }

    [Serializable]
    public struct VehicleSpinner
    {
        public string name;
        public Vector3 axis;
        public float rpm;
    }

    /// <summary>
    /// A ground vehicle (or the depot building), imported from ArtSource/Vehicles' &lt;Model&gt;.vehicle. Unity space:
    /// origin on the ground under the rear axle, +Z forward, +X right. Gameplay (teams, StripWork) reads the
    /// work figures from here: a team's lane width, speeds and turn radius.
    /// </summary>
    public class VehicleType : ScriptableObject
    {
        public string displayName;
        [Tooltip("ArtSource model folder name.")]
        public string model;
        public VehicleKind kind;
        public Bounds bounds;
        public float wheelbase;
        public VehicleWheel[] wheels = Array.Empty<VehicleWheel>();
        public VehicleSpinner[] spinners = Array.Empty<VehicleSpinner>();
        public VehicleLight[] lights = Array.Empty<VehicleLight>();
        [Tooltip("Buildings: roller doors, left to right seen from outside.")]
        public VehicleDoor[] doors = Array.Empty<VehicleDoor>();
        [Tooltip("Buildings: the hall's free floor space, where vehicles park.")]
        public Bounds interior;

        [Header("Work")]
        [Tooltip("Width each vehicle clears / cuts per pass, with overlap (metres).")]
        public float laneWidth = 5f;
        [Tooltip("Ploughing / mowing speed (m/s).")]
        public float workSpeed = 10f;
        [Tooltip("Speed driving between jobs (m/s).")]
        public float transitSpeed = 12f;
        [Tooltip("Turning radius for U-turns and corners (metres).")]
        public float turnRadius = 10f;
        [Tooltip("Where snow / clippings leave the vehicle while it works (local).")]
        public Vector3 sprayPoint;
        public Vector3 sprayDirection = Vector3.left;
        [Tooltip("Where the broom kicks up snow (local; zero = no broom).")]
        public Vector3 broomPoint;

        public float Length => bounds.size.z;
        public float Width => bounds.size.x;

        public WorkKind Work => kind == VehicleKind.Mower ? WorkKind.Mow : WorkKind.Plough;
        /// <summary>Where the vehicle works the ground, ahead of its rear axle (metres): a plough's blade, or (negative)
        /// the back of a mower's decks. The strip shows cleared / cut behind this point.</summary>
        public float Reach => kind == VehicleKind.Mower ? sprayPoint.z : bounds.max.z - 0.5f;
        /// <summary>Distance between vehicles in a team, in echelon and in single file.</summary>
        public float Gap => laneWidth * StripWork.EchelonGapPerLane;
    }
}
