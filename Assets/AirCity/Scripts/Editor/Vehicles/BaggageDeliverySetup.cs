using System;
using System.IO;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Dev;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Vehicles
{
    public static class BaggageDeliverySetup
    {
        public const string ScenePath = "Assets/AirCity/Scenes/BaggageDelivery.unity";
        const string CaptureFolder = "Captures/BaggageDelivery";

        [MenuItem("AirCity/Vehicles/Create Baggage Delivery Review")]
        public static void CreateReview()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }

        public static void FromCommandLine()
        {
            try { Build(true); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Build(bool capture)
        {
            VehicleSetup.CreateMissing();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.73f, 0.83f);
            RenderSettings.ambientEquatorColor = new Color(0.49f, 0.54f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.29f, 0.31f);
            var sun = new GameObject("Daylight").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            RenderSettings.sun = sun;
            var apron = Material("BaggageApron", new Color(0.38f, 0.40f, 0.41f));
            var white = Material("BaggageDeliveryWhite", new Color(0.88f, 0.88f, 0.79f));
            var blue = Material("BaggageDeliveryBlue", new Color(0.08f, 0.27f, 0.40f));
            var yellow = Material("BaggageDeliveryYellow", new Color(0.95f, 0.65f, 0.13f));
            var red = Material("BaggageDeliveryBarrier", new Color(0.65f, 0.12f, 0.08f));
            Box("Review apron", new Vector3(20f, -0.055f, -4f), new Vector3(115f, 0.1f, 100f), apron);

            var planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AirCity/Aircraft/Narrowbody/Narrowbody.prefab");
            if (planePrefab == null) throw new InvalidOperationException("Missing Narrowbody prefab.");
            var plane = ((GameObject)PrefabUtility.InstantiatePrefab(planePrefab)).GetComponent<AirCity.Aircraft.Aircraft>();
            plane.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var tractor = Equipment("BaggageTractor");
            var cart = Equipment("BaggageCart");
            var loader = Equipment("BeltLoader");
            var demo = new GameObject("Baggage delivery").AddComponent<BaggageDeliveryDemo>();
            demo.aircraft = plane; demo.tractor = tractor; demo.cart = cart; demo.loader = loader;
            demo.hold = new BulkHoldConfiguration { aircraft = plane.type, cargoPoint = "Cargo_Fwd", bagCapacity = 100, weightCapacity = 1500f };
            if (!demo.hold.Resolve(loader.type, out var cargo, out var reason)) throw new InvalidOperationException(reason);
            var outward = plane.WorldDirection(cargo).normalized;
            var front = plane.WorldPosition(cargo) + outward * (plane.type.bounds.extents.x + 2f);
            var b = loader.type.baggage;
            float rearHeight = b.beltRear.y;
            float run = Mathf.Sqrt(b.beltLength * b.beltLength - Mathf.Pow(cargo.position.y - rearHeight, 2f));
            var rear = front + outward * run;
            rear.y = rearHeight;
            var rotation = Quaternion.LookRotation(-outward, Vector3.up);
            var loaderPosition = rear - rotation * b.beltRear;
            loaderPosition.y = 0f;
            loader.transform.SetPositionAndRotation(loaderPosition, rotation);
            loader.beltFrontHeight = cargo.position.y; loader.Apply();

            float x = rear.x + 3.3f;
            float cartCentre = (cart.type.baggage.loadMin.z + cart.type.baggage.loadMax.z) * 0.5f;
            float cartOffset = -tractor.type.baggage.rearHitch.z + cart.type.baggage.frontHitch.z - cartCentre;
            float handoffZ = rear.z - cartOffset;
            var network = new GameObject("Service roads").AddComponent<TaxiwayNetwork>();
            demo.roads = network;
            // Two forward corridors form a circuit, with the dispatch stop between the eastern corners.
            int home = network.AddNode(new Vector3(x + 20f, 0f, -15f));
            int ne = network.AddNode(new Vector3(x + 20f, 0f, 32f));
            int nw = network.AddNode(new Vector3(x, 0f, 32f));
            int stand = network.AddNode(new Vector3(x, 0f, handoffZ));
            int sw = network.AddNode(new Vector3(x, 0f, -40f));
            int se = network.AddNode(new Vector3(x + 20f, 0f, -40f));
            int[] circuit = { home, ne, nw, stand, sw, se, home };
            for (int i = 1; i < circuit.Length; i++)
                network.edges.Add(new TaxiwayEdge { id = 100 + i, a = circuit[i - 1], b = circuit[i],
                    width = 12f, surface = PavementSurface.Concrete, road = RoadClass.Service });
            demo.dispatchNode = home; demo.handoffNode = stand;
            foreach (var edge in network.edges) RoadPaint(network.At(edge.a), network.At(edge.b), edge.width, white);
            Box("Dispatch hall endpoint", new Vector3(x + 32f, 2.5f, -18f), new Vector3(9f, 5f, 18f), blue);
            Box("Dispatch shutter", new Vector3(x + 27.45f, 1.5f, -18f), new Vector3(0.06f, 3f, 7f), white);
            Text("DISPATCH", new Vector3(x + 25f, 0.025f, -29f), 1f, Color.white);
            Text("STAND HANDOFF", new Vector3(x - 6f, 0.025f, handoffZ - 9f), 0.75f, new Color(1f, 0.78f, 0.25f));
            Text("AC102 / BULK HOLD", new Vector3(-10f, 0.025f, 23f), 1f, Color.white);
            // A logical connection is deliberately kept outside the aircraft's conservative footprint.
            Paint(plane.WorldPosition(cargo), front, yellow, "Logical hold connection", 0.10f);
            demo.accessBarrier = Box("Access barrier", new Vector3(x, 0.6f, 19f), new Vector3(10f, 1.2f, 0.5f), red);
            demo.accessBarrier.SetActive(false);
            demo.Restart();
            if (demo.Problem != null) throw new InvalidOperationException(demo.Problem);
            PaintPath(demo.Delivery.Train.Path, blue);

            var camera = new GameObject("Delivery review camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.69f, 0.76f);
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 400f; camera.fieldOfView = 45f;
            var orbit = camera.gameObject.AddComponent<DevOrbitCamera>();
            orbit.pivot = new Vector3(24f, 0f, -2f); orbit.distance = 120f; orbit.pitch = 58f; orbit.yaw = 215f;
            var view = Quaternion.Euler(orbit.pitch, orbit.yaw, 0f);
            camera.transform.SetPositionAndRotation(orbit.pivot - view * Vector3.forward * orbit.distance, view);
            foreach (var group in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude)) group.animateCrossFading = false;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            if (!capture) return;
            Directory.CreateDirectory(CaptureFolder);
            SceneCapture.Capture(camera, CaptureFolder + "/overview.png", 1800, 1100);
            bool turningCaptured = false;
            for (int i = 0; i < 10000 && demo.Delivery.State != BaggageDeliveryState.Delivering; i++)
            {
                demo.Advance(0.05f);
                if (turningCaptured || Mathf.Abs(demo.Delivery.Train.CartSteering) < 20f) continue;
                turningCaptured = true;
                camera.transform.position = demo.cart.transform.position + new Vector3(8f, 7f, 9f);
                camera.transform.LookAt(demo.cart.transform.position + Vector3.up);
                SceneCapture.Capture(camera, CaptureFolder + "/turning.png", 1800, 1100);
            }
            if (demo.Delivery.State != BaggageDeliveryState.Delivering) throw new InvalidOperationException("Delivery never reached the stand.");
            camera.transform.position = rear + new Vector3(15f, 13f, 17f);
            camera.transform.LookAt(rear + Vector3.up);
            SceneCapture.Capture(camera, CaptureFolder + "/handoff.png", 1800, 1100);
            demo.Advance(300f);
            if (!demo.Delivery.DepartureReady || demo.Hold.Count != demo.Ledger.Count || demo.Equipment.Owner != null)
                throw new InvalidOperationException("Review delivery did not conserve baggage and return its equipment.");
            Debug.Log($"Baggage delivery review: {demo.Hold.Count} bags / {demo.Hold.Weight} kg delivered; train returned, equipment released.");
        }

        static BaggageEquipmentRig Equipment(string model)
        {
            var prefab = VehicleSetup.Prefab(model);
            if (prefab == null) throw new InvalidOperationException("Missing equipment: " + model);
            return ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).GetComponent<BaggageEquipmentRig>();
        }

        internal static Material Material(string name, Color color)
        {
            string path = $"{VehicleSetup.Root}/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(AirCity.EditorTools.Aircraft.AircraftAssetPostprocessor.LitShader()) { name = name };
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0.12f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        internal static GameObject Box(string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.position = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static void RoadPaint(Vector2 a, Vector2 b, float width, Material white)
        {
            var d = (b - a).normalized; var right = new Vector2(d.y, -d.x) * (width * 0.5f - 0.15f);
            Vector3 P(Vector2 p) => new Vector3(p.x, 0.015f, p.y);
            Paint(P(a + right), P(b + right), white, "Road edge", 0.10f);
            Paint(P(a - right), P(b - right), white, "Road edge", 0.10f);
            for (float s = 2f; s < Vector2.Distance(a, b) - 1f; s += 6f)
                Paint(P(a + d * s), P(a + d * Mathf.Min(s + 2f, Vector2.Distance(a, b))), white, "Road centre", 0.10f);
        }

        internal static void Paint(Vector3 a, Vector3 b, Material material, string name, float width)
        {
            a.y = b.y = 0.02f;
            var stripe = Box(name, (a + b) * 0.5f, new Vector3(width, 0.008f, Vector3.Distance(a, b)), material);
            stripe.transform.rotation = Quaternion.LookRotation(b - a, Vector3.up);
            UnityEngine.Object.DestroyImmediate(stripe.GetComponent<Collider>());
            stripe.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void PaintPath(VehiclePath path, Material material)
        {
            for (float s = 0; s < path.Length; s += 3f)
            {
                var a = path.Sample(s); var b = path.Sample(Mathf.Min(s + 1f, path.Length));
                Paint(new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y), material, "Delivery route", 0.16f);
            }
        }

        internal static void Text(string value, Vector3 position, float size, Color color)
        {
            var text = new GameObject(value).AddComponent<TextMesh>();
            text.text = value; text.fontSize = 64; text.characterSize = size * 0.1f; text.color = color;
            text.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));
        }
    }
}
