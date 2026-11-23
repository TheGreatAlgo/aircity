using System;
using System.IO;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.EditorTools.Dev;
using AirCity.Game;
using AirCity.Traffic;
using AirCity.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>A standalone visual fixture for the flying school: clubhouse, flight line with trainers, car park
    /// and driveway, by day and night, and a trainer flying the circuit. No player save is opened or changed.</summary>
    public static class FlyingSchoolReview
    {
        const string Output = "Captures/FlyingSchool/";

        [MenuItem("AirCity/Airfield/Create Flying School Review")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Output); Build(true);
                Debug.Log("FLYING SCHOOL: clubhouse, flight line, car park, driveway, night lighting and a flown circuit reviewed.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        static void Daylight(Light sun, Camera camera)
        {
            AircraftLights.Glow = 0f; Shader.SetGlobalFloat("_AirCityLightGlow", 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.66f, .72f, .80f);
            RenderSettings.ambientEquatorColor = new Color(.47f, .48f, .45f);
            RenderSettings.ambientGroundColor = new Color(.24f, .27f, .21f);
            sun.intensity = 1.6f; sun.color = Color.white;
            if (camera != null) camera.backgroundColor = new Color(.66f, .73f, .80f);
        }

        static void Build(bool capture)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            float priorGlow = AircraftLights.Glow;
            try
            {
                var sun = new GameObject("Daylight").AddComponent<Light>(); sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(41, 148, 0); RenderSettings.sun = sun;
                Daylight(sun, null);
                var root = new GameObject("Flying school airfield"); root.SetActive(false);
                var airport = root.AddComponent<Airport>(); AirfieldSetup.AssignMaterials(airport); airport.groundSize = 2600; airport.windFrom = 270f;
                var type = AssetDatabase.LoadAssetAtPath<AircraftType>("Assets/AirCity/Aircraft/Cessna172/Cessna172.aircraft");
                if (type == null) throw new InvalidOperationException("The Starling A4 aircraft asset is missing.");
                airport.aircraftTypes.Add(type);
                root.SetActive(true);
                var runway = airport.AddRunway(new Vector3(-500, 0, 0), new Vector3(500, 0, 0), PavementSurface.Asphalt, 23f);
                var net = airport.Taxiways;
                int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0, z), on);
                int w = Node(-480, 0, runway), a = Node(-480, -110), m = Node(0, -110), b = Node(480, -110), e = Node(480, 0, runway);
                foreach (var (x, y) in new[] { (w, a), (a, m), (m, b), (b, e) }) net.Connect(x, y, 10.5f, PavementSurface.Asphalt);
                net.Connect(Node(0, 0, runway), m, 10.5f, PavementSurface.Asphalt);
                airport.BuildGround(); net.Rebuild(); runway.Rebuild();

                // A public road behind the site; the school is bought through the same quote the game uses, so the
                // driveway shown is the real graph connection.
                var origin = new Vector2(150, -110);
                float lot = -110f - FlyingSchoolLayout.LotBack(10.5f);
                var network = new PublicRoadNetwork();
                int left = network.AddNode(new Vector3(-150, 0, lot - 34f)), right = network.AddNode(new Vector3(420, 0, lot - 34f));
                network.Node(left).outsideConnection = true; network.Node(right).outsideConnection = true;
                if (!network.Connect(left, right, PublicRoadStyle.Rural, RoadStructure.Ground, out var why)) throw new InvalidOperationException(why);
                var host = new GameObject("Session fixture"); host.SetActive(false);
                var state = new CareerState { world = FlatWorld(), selectedSite = 0, handsOn = true, wholeRegion = true, mode = GameMode.Sandbox,
                    layout = new CareerLayout { unlocked = int.MaxValue }, publicRoads = network };
                var game = host.AddComponent<CareerGame>();
                typeof(CareerGame).GetProperty("State").SetValue(game, state);
                typeof(CareerGame).GetProperty("Airport").SetValue(game, airport);
                if (!FlyingSchoolConstruction.TryCreate(game, airport, origin, 180f, 10.5f, 1, PavementSurface.Asphalt, out var plan, out why) &&
                    !FlyingSchoolConstruction.TryCreate(game, airport, origin, 180f, 10.5f, -1, PavementSurface.Asphalt, out plan, out why))
                    throw new InvalidOperationException("Could not quote the review school: " + why);
                var school = airport.AddFlyingSchool(origin, 180f, plan.Lane, plan.Side, PavementSurface.Asphalt, plan.SiteId);
                if (school == null) throw new InvalidOperationException("Could not place the review school.");
                if (FlyingSchoolConstruction.RoadProblem(plan.Road.Draft, school) != null) throw new InvalidOperationException("The review driveway is not connected in both directions.");
                var prefab = AirCity.EditorTools.Aircraft.AircraftSetup.Prefab(type);
                if (prefab == null) throw new InvalidOperationException("The Starling A4 prefab is missing.");
                SchoolTrainer first = null;
                for (int i = 0; i < 2; i++)
                {
                    var trainer = school.AddTrainer(UnityEngine.Object.Instantiate(prefab).GetComponent<AirCity.Aircraft.Aircraft>());
                    if (trainer == null) throw new InvalidOperationException("Could not put a trainer on the flight line.");
                    first ??= trainer;
                }
                var rural = AssetDatabase.LoadAssetAtPath<PublicRoadProfile>("Assets/AirCity/World/Roads/RuralRoad.asset");
                var access = AssetDatabase.LoadAssetAtPath<PublicRoadProfile>("Assets/AirCity/World/Roads/AirportAccessRoad.asset");
                var geometry = new PublicRoadGeometry(plan.Road.Draft, rural, access, _ => 0f);
                var roads = new GameObject("Public road and school driveway");
                RoadPart(roads.transform, "Pavement", geometry.pavement, rural.asphalt);
                RoadPart(roads.transform, "Shoulder", geometry.shoulders, rural.shoulder);
                RoadPart(roads.transform, "Markings", geometry.markings, rural.markings);
                RoadPart(roads.transform, "Kerbs", geometry.kerbMesh, airport.building);
                RoadPart(roads.transform, "Structure", geometry.structures, airport.building);
                RoadPart(roads.transform, "Median", geometry.medianGrass, airport.building);

                var camera = new GameObject("Review camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor; camera.nearClipPlane = .1f; camera.farClipPlane = 6000; camera.fieldOfView = 40;
                Daylight(sun, camera);
                // The site frame: +Z of the school points south here, so airside views look from the north.
                float c = -110f - FlyingSchoolLayout.BuildingCentre(10.5f);
                var centre = new Vector3(origin.x, 2f, c);
                Aim(camera, new Vector3(origin.x + 46, 22, -92), centre + new Vector3(0, 0, 12));
                if (!capture) return;
                const int W = 1800, H = 1125;
                SceneCapture.Capture(camera, Output + "airside.png", W, H);
                Aim(camera, new Vector3(origin.x - 17, 5.2f, c + 25), centre + new Vector3(2, .6f, 3)); SceneCapture.Capture(camera, Output + "terrace.png", W, H);
                Aim(camera, new Vector3(origin.x + 34, 13, c - 46), centre + new Vector3(0, 0, -8)); SceneCapture.Capture(camera, Output + "landside.png", W, H);
                Aim(camera, new Vector3(origin.x - 12, 3.4f, c - 22), centre + new Vector3(0, .2f, -5)); SceneCapture.Capture(camera, Output + "entrance.png", W, H);
                Aim(camera, new Vector3(origin.x + 6, 2.2f, -116), centre + new Vector3(-3, 1.2f, 10)); SceneCapture.Capture(camera, Output + "flightline.png", W, H);
                Aim(camera, new Vector3(origin.x + 70, 120, c - 105), new Vector3(origin.x, 0, c + 12)); SceneCapture.Capture(camera, Output + "site.png", W, H);
                AircraftLights.Glow = 1f; Shader.SetGlobalFloat("_AirCityLightGlow", 1f);
                foreach (var visual in UnityEngine.Object.FindObjectsByType<VisitorCentreVisual>(FindObjectsInactive.Include)) visual.SetLighting(1f);
                RenderSettings.ambientSkyColor = new Color(.065f, .095f, .15f); RenderSettings.ambientEquatorColor = new Color(.035f, .05f, .075f);
                RenderSettings.ambientGroundColor = new Color(.015f, .025f, .035f); sun.intensity = .12f; sun.color = new Color(.58f, .68f, 1f);
                camera.backgroundColor = new Color(.05f, .075f, .12f);
                Aim(camera, new Vector3(origin.x + 46, 22, -92), centre + new Vector3(0, 0, 12)); SceneCapture.Capture(camera, Output + "night-airside.png", W, H);
                Aim(camera, new Vector3(origin.x + 34, 13, c - 46), centre + new Vector3(0, 0, -8)); SceneCapture.Capture(camera, Output + "night-landside.png", W, H);
                Daylight(sun, camera);
                foreach (var visual in UnityEngine.Object.FindObjectsByType<VisitorCentreVisual>(FindObjectsInactive.Include)) visual.SetLighting(0f);

                // A flown circuit: a chase view on each leg, then the touch-and-go and the aircraft back on its tie-down.
                var flight = airport.Traffic.DepartForCircuits(first.aircraft, 1, out why);
                if (flight == null) throw new InvalidOperationException("The review trainer could not leave for circuits: " + why);
                bool crosswind = false, downwind = false, baseLeg = false, final = false, touch = false; var track = new System.Text.StringBuilder();
                for (int step = 0; step < 4 * 2400 && !flight.Finished; step++)
                {
                    airport.Simulate(.25f);
                    var t = first.aircraft.transform;
                    if (step % 40 == 0) track.AppendLine($"{step * .25f:0} {flight.Phase} {t.position.x:0} {t.position.y:0.0} {t.position.z:0} {flight.Status}");
                    void Chase(string name) { Aim(camera, t.position - t.forward * 34f + Vector3.up * 9f + t.right * 9f, t.position + t.forward * 6f); SceneCapture.Capture(camera, Output + name, W, H); }
                    string status = flight.Status;
                    if (!crosswind && flight.Phase == FlightPhase.Pattern && status.Contains("crosswind") && flight.Height > 135f) { crosswind = true; Chase("circuit-crosswind-turn.png"); }
                    if (!downwind && flight.Phase == FlightPhase.Pattern && status.Contains("downwind") && Mathf.Abs(t.position.x - origin.x) < 12f)
                    {
                        downwind = true; Chase("circuit-downwind.png");
                        Aim(camera, new Vector3(origin.x + 30, 3f, -40), t.position); camera.fieldOfView = 12; SceneCapture.Capture(camera, Output + "circuit-from-ground.png", W, H); camera.fieldOfView = 40;
                    }
                    if (!baseLeg && flight.Phase == FlightPhase.Pattern && status.Contains("turning final")) { baseLeg = true; Chase("circuit-final-turn.png"); }
                    if (!final && flight.Phase == FlightPhase.Approach && flight.ToTouchdown < 500f) { final = true; Chase("circuit-final.png"); }
                    if (!touch && flight.Phase == FlightPhase.TouchAndGo) { touch = true; Aim(camera, t.position + new Vector3(-30, 6, -22), t.position); SceneCapture.Capture(camera, Output + "touch-and-go.png", W, H); }
                }
                File.WriteAllText(Output + "circuit-track.txt", track.ToString());
                if (!flight.Finished || flight.Phase != FlightPhase.Parked || !school.AtHome(first)) throw new InvalidOperationException("The review circuit did not end on its tie-down: " + flight.Status);
                if (!(crosswind && downwind && baseLeg && final && touch)) throw new InvalidOperationException("A circuit leg was not seen.");
                Aim(camera, new Vector3(origin.x + 46, 22, -92), centre + new Vector3(0, 0, 12)); SceneCapture.Capture(camera, Output + "returned.png", W, H);
            }
            finally { AircraftLights.Glow = priorGlow; Shader.SetGlobalFloat("_AirCityLightGlow", priorGlow); }
        }

        static void RoadPart(Transform parent, string name, Mesh mesh, Material material)
        {
            if (mesh == null || mesh.vertexCount == 0) return;
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void Aim(Camera camera, Vector3 position, Vector3 target) { camera.transform.position = position; camera.transform.LookAt(target); }

        static ValleyWorld FlatWorld()
        {
            var world = new ValleyWorld { heights = new float[ValleyWorld.Resolution * ValleyWorld.Resolution] };
            world.sites.Add(new AirportSite { name = "School review", centre = Vector2.zero, elevation = 0f });
            return world;
        }
    }
}
