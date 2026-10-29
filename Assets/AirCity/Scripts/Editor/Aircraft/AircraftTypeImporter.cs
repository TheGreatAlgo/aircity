using System;
using System.Collections.Generic;
using System.IO;
using AirCity.Aircraft;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace AirCity.EditorTools.Aircraft
{
    /// <summary>
    /// Imports &lt;Model&gt;.aircraft (JSON written by the aircraft pipeline in ArtSource/Aircraft) as an
    /// <see cref="AircraftType"/>. Paths in the file are relative to it, positions are already in Unity space.
    /// Re-imports automatically when the generator rewrites the file or any referenced texture.
    /// </summary>
    [ScriptedImporter(4, "aircraft")]
    public class AircraftTypeImporter : ScriptedImporter
    {
#pragma warning disable 0649   // assigned by JsonUtility
        [Serializable] class SchemeJson { public string style; public string mask; public float[] registrationBox; }
        [Serializable] class SideJson { public float[] crop; public float k, tu, tv, sign; }
        [Serializable] class RegistrationJson { public int textureWidth, halfHeight; public SideJson port, starboard; }
        [Serializable] class BoundsJson { public float[] min, max; }
        [Serializable] class WheelJson { public string name; public float[] position; public float radius; public bool steerable; }
        [Serializable] class GearJson { public WheelJson[] wheels; public float[] steeringPivot; public float maxSteeringAngle; }
        [Serializable]
        class PerformanceJson
        {
            public float takeoffDistance, landingDistance, maxTakeoffMass;
            public bool unpavedRunways;
            public float taxiSpeed, rotateSpeed, approachSpeed, climbRate, climbSpeed;
        }
        [Serializable] class PointJson { public string name, type; public float[] position, normal, size; }
        [Serializable] class LightJson { public string name, type; public float[] position, direction, color; public float spread; }

        [Serializable]
        class ExampleJson
        {
            public string name, style, baseColor, color1, color2, color3, registrationColor, registration, logo;
        }

        [Serializable]
        class TypeJson
        {
            public string model, displayName, category;
            public BoundsJson bounds;
            public int atlasSize;
            public float texelsPerMetre;
            public string detail, dirt, registrationDirt, logoUV, groundShadow, font, defaultBody, defaultRegistration, uvTemplate;
            public float[] logoBox;
            public SchemeJson[] schemes;
            public RegistrationJson registration;
            public ExampleJson[] examples;
            public GearJson gear;
            public PerformanceJson performance;
            public PointJson[] points;
            public LightJson[] lights;
        }
#pragma warning restore 0649

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var json = JsonUtility.FromJson<TypeJson>(File.ReadAllText(ctx.assetPath));
            var t = ScriptableObject.CreateInstance<AircraftType>();
            t.name = json.model;
            t.model = json.model;
            t.displayName = string.IsNullOrEmpty(json.displayName) ? json.model : json.displayName;
            t.category = Parse(ctx, json.category, AircraftCategory.GeneralAviation);

            if (json.bounds?.min != null && json.bounds.max != null)
            {
                var b = new Bounds();
                b.SetMinMax(V3(json.bounds.min), V3(json.bounds.max));
                t.bounds = b;
            }
            if (json.gear != null)
            {
                var wheels = new List<AircraftType.Wheel>();
                foreach (var w in json.gear.wheels ?? Array.Empty<WheelJson>())
                    wheels.Add(new AircraftType.Wheel { name = w.name, position = V3(w.position), radius = w.radius, steerable = w.steerable });
                t.wheels = wheels.ToArray();
                t.steeringPivot = V3(json.gear.steeringPivot);
                t.maxSteeringAngle = json.gear.maxSteeringAngle;
            }
            if (json.performance != null)
            {
                t.takeoffDistance = json.performance.takeoffDistance;
                t.landingDistance = json.performance.landingDistance;
                t.maxTakeoffMass = json.performance.maxTakeoffMass;
                t.unpavedRunways = json.performance.unpavedRunways;
                t.taxiSpeed = json.performance.taxiSpeed;
                t.rotateSpeed = json.performance.rotateSpeed;
                t.approachSpeed = json.performance.approachSpeed;
                t.climbRate = json.performance.climbRate;
                t.climbSpeed = json.performance.climbSpeed;
            }
            var points = new List<AircraftType.ServicePoint>();
            foreach (var p in json.points ?? Array.Empty<PointJson>())
                points.Add(new AircraftType.ServicePoint
                {
                    name = p.name, type = Parse(ctx, p.type, ServicePointType.PassengerDoor),
                    position = V3(p.position), normal = V3(p.normal).normalized,
                    size = p.size != null && p.size.Length == 2 ? new Vector2(p.size[0], p.size[1]) : Vector2.zero,
                });
            t.points = points.ToArray();
            var lights = new List<AircraftType.LightPoint>();
            foreach (var l in json.lights ?? Array.Empty<LightJson>())
                lights.Add(new AircraftType.LightPoint
                {
                    name = l.name, type = Parse(ctx, l.type, AircraftLightType.Navigation),
                    position = V3(l.position), direction = V3(l.direction).normalized, spread = l.spread,
                    color = l.color != null && l.color.Length >= 3 ? new Color(l.color[0], l.color[1], l.color[2]) : Color.white,
                });
            t.lights = lights.ToArray();

            t.atlasSize = json.atlasSize;
            t.texelsPerMetre = json.texelsPerMetre;
            t.detail = Load<Texture2D>(ctx, json.detail);
            t.dirt = Load<Texture2D>(ctx, json.dirt);
            t.registrationDirt = Load<Texture2D>(ctx, json.registrationDirt);
            t.logoUV = Load<Texture2D>(ctx, json.logoUV);
            t.logoBox = json.logoBox != null && json.logoBox.Length == 4
                ? new Vector4(json.logoBox[0], json.logoBox[1], json.logoBox[2], json.logoBox[3]) : Vector4.zero;
            t.groundShadow = Load<Texture2D>(ctx, json.groundShadow);
            t.font = Load<RegistrationFont>(ctx, json.font);
            t.defaultBody = Load<Texture2D>(ctx, json.defaultBody);
            t.defaultRegistration = Load<Texture2D>(ctx, json.defaultRegistration);
            t.uvTemplate = Load<Texture2D>(ctx, json.uvTemplate);

            var schemes = new List<AircraftType.Scheme>();
            foreach (var s in json.schemes ?? Array.Empty<SchemeJson>())
            {
                if (!Enum.TryParse(s.style, out LiveryStyle style))
                {
                    ctx.LogImportWarning($"{ctx.assetPath}: unknown livery style '{s.style}' (add it to LiveryStyle)");
                    continue;
                }
                var b = s.registrationBox;
                schemes.Add(new AircraftType.Scheme
                {
                    style = style,
                    mask = Load<Texture2D>(ctx, s.mask),
                    registrationBox = b != null && b.Length == 4 ? new Vector4(b[0], b[1], b[2], b[3]) : Vector4.zero,
                });
            }
            t.schemes = schemes.ToArray();

            t.registrationTextureWidth = json.registration.textureWidth;
            t.registrationHalfHeight = json.registration.halfHeight;
            t.port = Side(json.registration.port);
            t.starboard = Side(json.registration.starboard);

            var examples = new List<AircraftType.ExampleLivery>();
            foreach (var e in json.examples ?? Array.Empty<ExampleJson>())
            {
                Enum.TryParse(e.style, out LiveryStyle style);
                examples.Add(new AircraftType.ExampleLivery
                {
                    name = e.name,
                    style = style,
                    baseColor = Hex(e.baseColor),
                    color1 = Hex(e.color1),
                    color2 = Hex(e.color2),
                    color3 = Hex(e.color3),
                    registrationColor = Hex(e.registrationColor),
                    registration = e.registration,
                    logo = Load<Texture2D>(ctx, e.logo),
                });
            }
            t.examples = examples.ToArray();

            ctx.AddObjectToAsset("type", t);
            ctx.SetMainObject(t);
        }

        static T Parse<T>(AssetImportContext ctx, string s, T fallback) where T : struct
        {
            if (Enum.TryParse(s, out T v)) return v;
            ctx.LogImportWarning($"{ctx.assetPath}: unknown {typeof(T).Name} '{s}'");
            return fallback;
        }

        static Vector3 V3(float[] a) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        static AircraftType.RegistrationSide Side(SideJson s) => new AircraftType.RegistrationSide
        {
            crop = Rect.MinMaxRect(s.crop[0], s.crop[1], s.crop[2], s.crop[3]),
            k = s.k,
            tu = s.tu,
            tv = s.tv,
            sign = s.sign,
        };

        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;

        internal static T Load<T>(AssetImportContext ctx, string relative) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(relative)) return null;
            string path = Resolve(ctx.assetPath, relative);
            ctx.DependsOnArtifact(path);   // imported first, and we re-import when it changes
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) ctx.LogImportWarning($"{ctx.assetPath}: missing {typeof(T).Name} at {path}");
            return asset;
        }

        /// <summary>Resolve a path relative to an asset ("../Shared/x" etc.) to an Assets/... path.</summary>
        internal static string Resolve(string assetPath, string relative)
        {
            var parts = new List<string>(Path.GetDirectoryName(assetPath).Replace('\\', '/').Split('/'));
            foreach (var seg in relative.Replace('\\', '/').Split('/'))
            {
                if (seg == "..") parts.RemoveAt(parts.Count - 1);
                else if (seg.Length > 0 && seg != ".") parts.Add(seg);
            }
            return string.Join("/", parts);
        }
    }
}
