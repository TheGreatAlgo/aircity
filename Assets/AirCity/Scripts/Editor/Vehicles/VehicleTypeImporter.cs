using System;
using System.Collections.Generic;
using System.IO;
using AirCity.Vehicles;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace AirCity.EditorTools.Vehicles
{
    /// <summary>
    /// Imports &lt;Model&gt;.vehicle (JSON written by the vehicle pipeline in ArtSource/Vehicles) as a
    /// <see cref="VehicleType"/>. Positions are already in Unity space (+Z forward, +X right).
    /// </summary>
    [ScriptedImporter(7, "vehicle")]
    public class VehicleTypeImporter : ScriptedImporter
    {
#pragma warning disable 0649   // assigned by JsonUtility
        [Serializable] class BoundsJson { public float[] min, max; }
        [Serializable] class WheelJson { public string name; public float[] centre; public float radius; public bool steerable; }
        [Serializable] class SpinnerJson { public string name; public float[] axis; public float rpm; }
        [Serializable] class LightJson { public string name, type, color; public float[] position, direction; public float size, phase; }
        [Serializable] class DoorJson { public string name; public float[] position; public float width, height; }

        [Serializable]
        class WorkJson
        {
            public float laneWidth, workSpeed, transitSpeed, turnRadius;
            public float[] sprayPoint, sprayDirection, broomPoint;
        }

        [Serializable]
        class TowJson
        {
            public float minMass, maxMass, lift, opening;
            public float[] cradle;
            public string part;
        }

        [Serializable]
        class BridgeJson
        {
            public float rotundaFloor, rotundaRadius, tunnelStart, sectionLength, cabReach, cabWidth, driveFromEnd, linkWidth, yokeBottom, upperFloor,
                cabBottom, cabTop, linkBottom, linkTop, headWidth, headBottom, headTop, headUpperBottom, headUpperWider;
        }

        [Serializable]
        class TypeJson
        {
            public string name, displayName, kind;
            public BoundsJson bounds;
            public float wheelbase;
            public WheelJson[] wheels;
            public SpinnerJson[] spinners;
            public LightJson[] lights;
            public WorkJson work;
            public TowJson tow;
            public DoorJson[] doors;
            public BoundsJson interior;
            public BridgeJson bridge;
        }
#pragma warning restore 0649

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var json = JsonUtility.FromJson<TypeJson>(File.ReadAllText(ctx.assetPath));
            var t = ScriptableObject.CreateInstance<VehicleType>();
            t.name = json.name;
            t.model = json.name;
            t.displayName = string.IsNullOrEmpty(json.displayName) ? json.name : json.displayName;
            if (!Enum.TryParse(json.kind, out t.kind)) ctx.LogImportWarning($"unknown vehicle kind '{json.kind}'");
            if (json.bounds?.min != null && json.bounds.max != null)
            {
                var b = new Bounds();
                b.SetMinMax(V3(json.bounds.min), V3(json.bounds.max));
                t.bounds = b;
            }
            t.wheelbase = json.wheelbase;

            var wheels = new List<VehicleWheel>();
            foreach (var w in json.wheels ?? Array.Empty<WheelJson>())
                wheels.Add(new VehicleWheel { name = w.name, centre = V3(w.centre), radius = w.radius, steerable = w.steerable });
            t.wheels = wheels.ToArray();

            var spinners = new List<VehicleSpinner>();
            foreach (var s in json.spinners ?? Array.Empty<SpinnerJson>())
                spinners.Add(new VehicleSpinner { name = s.name, axis = V3(s.axis).normalized, rpm = s.rpm });
            t.spinners = spinners.ToArray();

            var lights = new List<VehicleLight>();
            foreach (var l in json.lights ?? Array.Empty<LightJson>())
            {
                if (!Enum.TryParse(l.type, out VehicleLightType type))
                {
                    ctx.LogImportWarning($"{l.name}: unknown light type '{l.type}'");
                    continue;
                }
                ColorUtility.TryParseHtmlString(l.color, out var c);
                lights.Add(new VehicleLight
                {
                    name = l.name, type = type, position = V3(l.position), direction = V3(l.direction).normalized,
                    color = c.linear, size = l.size, phase = l.phase,
                });
            }
            t.lights = lights.ToArray();

            var doors = new List<VehicleDoor>();
            foreach (var d in json.doors ?? Array.Empty<DoorJson>())
                doors.Add(new VehicleDoor { name = d.name, position = V3(d.position), width = d.width, height = d.height });
            t.doors = doors.ToArray();
            if (json.interior?.min != null && json.interior.max != null && json.interior.min.Length >= 3)
            {
                var b = new Bounds();
                b.SetMinMax(V3(json.interior.min), V3(json.interior.max));
                t.interior = b;
            }

            if (json.work != null && json.work.laneWidth > 0f)
            {
                t.laneWidth = json.work.laneWidth;
                t.workSpeed = json.work.workSpeed;
                t.transitSpeed = json.work.transitSpeed;
                t.turnRadius = json.work.turnRadius;
                t.sprayPoint = V3(json.work.sprayPoint);
                t.sprayDirection = V3(json.work.sprayDirection).normalized;
                t.broomPoint = V3(json.work.broomPoint);
            }
            if (json.tow != null && json.tow.maxMass > 0f)
            {
                t.minTowMass = json.tow.minMass;
                t.maxTowMass = json.tow.maxMass;
                t.cradle = V3(json.tow.cradle);
                t.liftHeight = json.tow.lift;
                t.cradleOpening = json.tow.opening;
                t.liftPart = json.tow.part;
            }
            if (json.bridge != null && json.bridge.sectionLength > 0f)
            {
                var b = json.bridge;
                t.bridge = new JetBridgeDimensions
                {
                    rotundaFloor = b.rotundaFloor, rotundaRadius = b.rotundaRadius, tunnelStart = b.tunnelStart,
                    sectionLength = b.sectionLength, cabReach = b.cabReach, cabWidth = b.cabWidth,
                    driveFromEnd = b.driveFromEnd, linkWidth = b.linkWidth, yokeBottom = b.yokeBottom, upperFloor = b.upperFloor,
                    cabBottom = b.cabBottom, cabTop = b.cabTop, linkBottom = b.linkBottom, linkTop = b.linkTop,
                    headWidth = b.headWidth, headBottom = b.headBottom, headTop = b.headTop, headUpperBottom = b.headUpperBottom,
                    headUpperWider = b.headUpperWider,
                };
            }
            ctx.AddObjectToAsset("type", t);
            ctx.SetMainObject(t);
        }

        static Vector3 V3(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
    }
}
