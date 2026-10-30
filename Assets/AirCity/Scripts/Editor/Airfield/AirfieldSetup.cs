using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AirCity.EditorTools.Airfield
{
    /// <summary>
    /// Airfield materials (from the generated textures) and the sandbox airport with its maintenance depot.
    ///   AirCity ▸ Airfield ▸ Create Materials                 (missing ones only; hand-tuning is kept)
    ///   AirCity ▸ Airfield ▸ Add Sandbox Airport              (replaces the sandbox's Airport object)
    /// Batch: -executeMethod AirCity.EditorTools.Airfield.AirfieldSetup.Bootstrap
    /// </summary>
    public static class AirfieldSetup
    {
        public const string Root = "Assets/AirCity/Airfield";
        public const string MaterialsFolder = Root + "/Materials";
        const string Textures = Root + "/Textures";
        const string Shaders = "Assets/AirCity/Shaders";

        public static void Bootstrap()
        {
            CreateMaterials();
            VehicleSetup.CreateMissing();
            AddSandboxAirport();
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Surface look per material: texture set, worn set, tile size (metres, from the scans), joints.</summary>
        struct SurfaceLook
        {
            public string set, worn;
            public float tile, smoothness, macroStrength, porosity, rubber, puddles;
            public Color rubberColor;
            public Vector2 joints;
            public float jointWidth, jointDarkness, slabVariation;
            public bool fringe, grassLength;
        }

        static readonly (string name, SurfaceLook look)[] Surfaces =
        {
            ("Concrete", new SurfaceLook { set = "Concrete", worn = "ConcreteWorn", tile = 2.5f, smoothness = 0.45f, macroStrength = 0.07f,
                porosity = 0.35f, rubber = 0.9f, puddles = 1f, joints = new Vector2(7.5f, 7.5f), jointWidth = 0.025f, jointDarkness = 0.5f, slabVariation = 0.03f }),
            ("Asphalt", new SurfaceLook { set = "Asphalt", worn = "AsphaltWorn", tile = 2.1f, smoothness = 0.4f, macroStrength = 0.08f,
                porosity = 0.25f, rubber = 0.6f, puddles = 1f, joints = new Vector2(5f, 0f), jointWidth = 0.06f, jointDarkness = 0.12f, slabVariation = 0.03f }),
            ("Dirt", new SurfaceLook { set = "Dirt", worn = "DirtWorn", tile = 2f, smoothness = 0.3f, macroStrength = 0.14f,
                porosity = 0.5f, rubber = 0.6f, rubberColor = new Color(0.2f, 0.16f, 0.12f), puddles = 0.7f, fringe = true }),
            ("Grass", new SurfaceLook { set = "Grass", worn = "Grass", tile = 2.5f, smoothness = 0.25f, macroStrength = 0.12f,
                porosity = 0.3f, puddles = 0.15f }),
            // grass runways and taxiways: worn turf shows the dirt beneath, mown grass shows stripes
            ("GrassStrip", new SurfaceLook { set = "Grass", worn = "Dirt", tile = 2.5f, smoothness = 0.25f, macroStrength = 0.12f,
                porosity = 0.3f, rubber = 0.35f, rubberColor = new Color(0.22f, 0.2f, 0.13f), puddles = 0.2f, fringe = true,
                grassLength = true }),
        };

        [MenuItem("AirCity/Airfield/Create Materials")]
        public static void CreateMaterials()
        {
            EnsureFolder(MaterialsFolder);
            AssetDatabase.Refresh();
            foreach (var (name, look) in Surfaces)
            {
                var mat = LoadOrCreate(name, "AirfieldSurface.shader", out bool created);
                if (!created) continue;
                mat.SetTexture("_BaseMap", Tex($"{look.set}_Albedo"));
                mat.SetTexture("_NormalMap", Tex($"{look.set}_Normal"));
                mat.SetTexture("_MaskMap", Tex($"{look.set}_Mask"));
                mat.SetTexture("_WornMap", Tex($"{look.worn}_Albedo"));
                mat.SetTexture("_WornNormalMap", Tex($"{look.worn}_Normal"));
                mat.SetTexture("_WornMaskMap", Tex($"{look.worn}_Mask"));
                mat.SetTexture("_MacroMap", Tex("Macro"));
                mat.SetFloat("_TileSize", look.tile);
                mat.SetFloat("_Smoothness", look.smoothness);
                mat.SetFloat("_MacroStrength", look.macroStrength);
                mat.SetFloat("_Porosity", look.porosity);
                mat.SetFloat("_Rubber", look.rubber);
                mat.SetColor("_RubberColor", look.rubberColor.a > 0f ? look.rubberColor : new Color(0.03f, 0.03f, 0.03f));
                mat.SetFloat("_Puddles", look.puddles);
                mat.SetVector("_JointSpacing", look.joints);
                mat.SetFloat("_JointWidth", look.jointWidth);
                mat.SetFloat("_JointDarkness", look.jointDarkness);
                mat.SetFloat("_SlabVariation", look.slabVariation);
                mat.SetFloat("_Fringe", look.fringe ? 1f : 0f);
                if (look.fringe) mat.EnableKeyword("_FRINGE");
                mat.SetFloat("_GrassLength", look.grassLength ? 1f : 0f);
                if (look.grassLength) mat.EnableKeyword("_GRASS_LENGTH");
                EditorUtility.SetDirty(mat);
            }
            var paint = LoadOrCreate("Paint", "AirfieldPaint.shader", out bool newPaint);
            if (newPaint)
            {
                paint.SetTexture("_GlyphMap", Tex("RunwayGlyphs"));
                paint.SetTexture("_MacroMap", Tex("Macro"));
                EditorUtility.SetDirty(paint);
            }
            LoadOrCreate("LightGlow", "AirfieldLightGlow.shader", out _);
            LoadOrCreate("Fixture", "AirfieldFixture.shader", out _);
            LoadOrCreate("Building", "Building.shader", out _);
            AssetDatabase.SaveAssets();
        }

        public static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/{name}.mat");

        static Material LoadOrCreate(string name, string shaderFile, out bool created)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            created = mat == null;
            if (mat != null) return mat;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>($"{Shaders}/{shaderFile}");
            mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static Texture2D Tex(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{name}.png");
            if (t == null) Debug.LogError($"AirfieldSetup: missing {Textures}/{name}.png (run ArtSource/Airfield/textures.py)");
            return t;
        }

        /// <summary>Give an Airport the shared materials.</summary>
        public static void AssignMaterials(Airport a)
        {
            a.concrete = Material("Concrete");
            a.asphalt = Material("Asphalt");
            a.dirt = Material("Dirt");
            a.grass = Material("Grass");
            a.grassStrip = Material("GrassStrip");
            a.paint = Material("Paint");
            a.lightGlow = Material("LightGlow");
            a.fixture = Material("Fixture");
            a.building = Material("Building");
        }

        // ------------------------------------------------------------------ sandbox

        /// <summary>
        /// The sandbox airport, a small but complete one for testing:
        /// - a 3,400 m x 60 m concrete runway 09/27 with precision lighting, taking every type up to code F; a code F
        ///   (25 m) parallel taxiway with entrances at both thresholds, three straight exits and a rapid exit each way;
        /// - a 1,600 m asphalt runway 13/31 crossed by a taxiway; a 600 m dirt strip and a 700 m grass strip (overdue
        ///   for mowing) for light aircraft, with a GA ramp on the grass taxiway; mown verges of various lengths;
        /// - the terminal apron: a code F taxilane joined to the parallel taxiway at both ends, contact stands for code
        ///   C, E and F with jet bridges (right-hand R1 on the first, the upper-deck UL1 on the first code F stand), and
        ///   three code C stands fanned round the terminal's half-circle east end, served by long bridges;
        /// - two code E de-icing bays on a bypass at the 09 end; the maintenance depot (ploughs, a mower, two small and
        ///   two large tugs) west of the GA apron, with service roads; and a display ramp for the aircraft showcase.
        /// Narrow-bodies, a wide-body, and (once their models exist) the double-deck and the long-haul quadjet stand
        /// ready to leave.
        /// </summary>
        [MenuItem("AirCity/Airfield/Add Sandbox Airport")]
        public static void AddSandboxAirport()
        {
            CreateMaterials();
            var scene = EditorSceneManager.OpenScene(AircraftSetup.SandboxScene, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Airport" || root.name == "Apron") Object.DestroyImmediate(root);

            var airport = BuildSandboxAirport();
            // the showcase rows (one aircraft per type and livery) onto their display ramp, clear of the traffic
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Aircraft")
                    root.transform.position = new Vector3(DisplayRamp.x - 14f, 0f, DisplayRamp.y - 250f);
            var cam = Camera.main;
            if (cam != null)
            {
                cam.farClipPlane = 12000f;
                var orbit = cam.GetComponent<DevOrbitCamera>();
                if (orbit != null) orbit.distanceLimits = new Vector2(4f, 5000f);
                if (cam.GetComponent<DevToolbar>() == null) cam.gameObject.AddComponent<DevToolbar>();
                var tool = cam.GetComponent<DevAirfieldTool>();
                if (tool == null) tool = cam.gameObject.AddComponent<DevAirfieldTool>();
                tool.airport = airport;
                tool.ploughType = VehicleSetup.Type("SnowPlough");
                tool.mowerType = VehicleSetup.Type("Mower");
                tool.depotType = VehicleSetup.Type("Depot");
                tool.ploughPrefab = VehicleSetup.Prefab("SnowPlough");
                tool.mowerPrefab = VehicleSetup.Prefab("Mower");
                tool.depotPrefab = VehicleSetup.Prefab("Depot");
                tool.smallTugType = VehicleSetup.Type("TugSmall");
                tool.largeTugType = VehicleSetup.Type("TugLarge");
                tool.smallTugPrefab = VehicleSetup.Prefab("TugSmall");
                tool.largeTugPrefab = VehicleSetup.Prefab("TugLarge");
            }
            EditorSceneManager.SaveScene(scene);
            Debug.Log("AirfieldSetup: sandbox airport added");
        }

        public static Airport BuildSandboxAirport()
        {
            var go = new GameObject("Airport");
            go.SetActive(false);
            var airport = go.AddComponent<Airport>();
            AssignMaterials(airport);
            airport.groundSize = 20000f;
            // the GA apron, the depot's pad (room to turn out of the doors onto the road and back in), and a display
            // ramp off every taxiway for the aircraft showcase (AddSandboxAirport moves it there)
            airport.aprons.Add(ApronArea.Rect(new Vector2(0f, 55f), new Vector2(240f, 180f), 0f, PavementSurface.Concrete));
            airport.aprons.Add(ApronArea.Rect(new Vector2(-195f, 52f), new Vector2(40f, 56f), 0f, PavementSurface.Concrete));
            airport.aprons.Add(ApronArea.Rect(DisplayRamp, new Vector2(300f, 560f), 0f, PavementSurface.Concrete));
            go.SetActive(true);

            // the main runway takes every type: 3,400 m x 60 m
            var main = airport.AddRunway(new Vector3(-2000f, 0f, 480f), new Vector3(1400f, 0f, 480f), PavementSurface.Concrete, 60f,
                RunwayLighting.Precision);
            var cross = airport.AddRunway(new Vector3(900f, 0f, 1100f), new Vector3(900f, 0f, 1100f) + Heading(130f) * 1600f,
                PavementSurface.Asphalt, 30f, RunwayLighting.Basic);
            var strip = airport.AddRunway(new Vector3(-900f, 0f, -300f), new Vector3(-900f, 0f, -300f) + Heading(40f) * 600f,
                PavementSurface.Dirt, 18f);

            var net = airport.Taxiways;
            // code F (25 m) for every taxiway a jet uses; 15 m for the GA apron's links, 10.5 m dirt and grass
            const float wide = 25f, narrow = 15f;
            var asphalt = PavementSurface.Asphalt;
            var concrete = PavementSurface.Concrete;
            int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0f, z), on);
            void Line(float w2, PavementSurface s, bool lit, params int[] ids)
            {
                for (int i = 0; i < ids.Length - 1; i++) net.Connect(ids[i], ids[i + 1], w2, s, lit);
            }
            int SplitAt(float x, float z)
            {
                var at = new Vector3(x, 0f, z);
                net.TryNearestEdge(at, 1f, out var edge, out _);
                return net.SplitEdge(edge, at);
            }

            // parallel taxiway A, entrances at both thresholds, straight exits and a rapid exit each way
            int west = Node(-1940f, 330f), east = Node(1340f, 330f);
            int wEntry = Node(-1985f, 480f, main), eEntry = Node(1385f, 480f, main);
            int xw = Node(-1100f, 330f), x1 = Node(-500f, 330f), x2 = Node(-60f, 330f), x3 = Node(60f, 330f);
            int hs09 = Node(140f, 330f), l1 = Node(180f, 330f), hs27 = Node(500f, 330f), l2 = Node(760f, 330f), xd = Node(1000f, 330f);
            Line(wide, asphalt, true, wEntry, Node(-1985f, 400f), west, xw, x1, x2, x3, hs09, l1, hs27, l2, xd, east, Node(1385f, 400f), eEntry);
            foreach (var (from, x) in new[] { (xw, -1100f), (x1, -500f), (xd, 1000f) }) Line(wide, asphalt, true, from, Node(x, 480f, main));
            // rapid exits, angled 30 degrees: for landings on 09 (eastwards) and on 27 (westwards)
            Line(wide, asphalt, true, Node(-120f, 480f, main), hs09);
            Line(wide, asphalt, true, Node(760f, 480f, main), hs27);
            // the GA apron's links
            Line(narrow, concrete, true, x2, Node(-60f, 140f));
            Line(narrow, concrete, true, x3, Node(60f, 140f));
            // east: across runway 13/31 (Connect adds the crossing node)
            Line(wide, asphalt, true, east, Node(2050f, 330f));
            // dirt taxiway from the strip's north-east end to the GA apron
            strip.wear = 0.35f;     // a well-used strip: tyre tracks and ruts
            cross.wear = 0.3f;
            var stripExit = strip.end - Heading(40f) * 15f;
            Line(10.5f, PavementSurface.Dirt, false, Node(stripExit.x, stripExit.z, strip), Node(-300f, 120f), Node(-115f, 120f));

            // a grass strip 06/24 south-west of the apron, grown long, with a grass taxiway to the apron
            var grassStrip = airport.AddRunway(new Vector3(-750f, 0f, -550f), new Vector3(-750f, 0f, -550f) + Heading(60f) * 700f,
                PavementSurface.Grass, 30f);
            grassStrip.grass = 19f;
            var grassExit = grassStrip.start + Heading(60f) * 640f;
            foreach (var e in net.Connect(Node(grassExit.x, grassExit.z, grassStrip), Node(-100f, -110f), 10.5f, PavementSurface.Grass))
                e.grass = 19f;
            foreach (var e in net.Connect(net.NearestNode(new Vector3(-100f, 0f, -110f), 1f).id, Node(-100f, -35f), 10.5f, PavementSurface.Grass))
                e.grass = 19f;

            // the terminal apron: taxilane T1 100 m south of A, joined to it at both ends (no dead end), contact stands
            // south of it laid for code F traffic passing; from its east end a code C lane fans round the terminal's
            // half-circle end with three stands facing into it, and leaves north-east back to A
            int t1w = Node(180f, 230f), t1f = Node(700f, 230f), t1e = Node(760f, 230f);
            Line(wide, concrete, false, l1, t1w, t1f, t1e, l2);
            var fanCentre = new Vector2(680f, 59f);
            const float fanApothem = 129f;
            float fanCorner = fanApothem / Mathf.Cos(22.5f * Mathf.Deg2Rad);
            var fan = new List<int>();
            foreach (float b in new[] { 22.5f, 67.5f, 112.5f, 157.5f })
            {
                var v = fanCentre + new Vector2(Mathf.Sin(b * Mathf.Deg2Rad), Mathf.Cos(b * Mathf.Deg2Rad)) * fanCorner;
                fan.Add(Node(v.x, v.y));
            }
            Line(narrow, concrete, false, t1f, fan[0], fan[1], fan[2], fan[3]);
            Line(narrow, concrete, true, fan[1], xd);

            Stand PlaceStand(StandKind kind, float x, float z, float bearing, SizeCode code, PavementSurface s,
                StandExit exit = StandExit.PushBack)
            {
                var st = net.AddStand(kind, new Vector3(x, 0f, z), bearing, code, s, exit);
                if (st == null) Debug.LogError($"AirfieldSetup: no taxiway for the stand at {x}, {z}");
                return st;
            }
            // contact stands, pulled against each other: four code C, a code E, two code F
            var contact = new List<Stand>();
            float edgeX = 200f;
            foreach (var code in new[] { SizeCode.C, SizeCode.C, SizeCode.C, SizeCode.C, SizeCode.E, SizeCode.F, SizeCode.F })
            {
                char c = code.Letter();
                float width = RunwayRules.MaxWingspan(c) + RunwayRules.StandClearance(c);
                contact.Add(PlaceStand(StandKind.Parking, edgeX + width * 0.5f, 230f, 180f, code, concrete));
                edgeX += width;
            }
            var fanStands = new List<Stand>();
            foreach (float b in new[] { 45f, 90f, 135f })
            {
                var m = fanCentre + new Vector2(Mathf.Sin(b * Mathf.Deg2Rad), Mathf.Cos(b * Mathf.Deg2Rad)) * fanApothem;
                fanStands.Add(PlaceStand(StandKind.Parking, m.x, m.y, b + 180f, SizeCode.C, concrete));
            }
            // the jet stands keep small aircraft off them (they go to the GA ramp)
            foreach (var st in net.stands)
                if (st != null && st.kind == StandKind.Parking && st.code >= SizeCode.C) st.noSmallAircraft = true;

            // the GA ramp on the grass taxiway: grass tie-downs and two taxi-out stands
            var gaFrom = new Vector2(grassExit.x, grassExit.z);
            var gaDir = (new Vector2(-100f, -110f) - gaFrom).normalized;
            float gaBearing = Mathf.Atan2(gaDir.x, gaDir.y) * Mathf.Rad2Deg;
            foreach (float s in new[] { 40f, 58f, 76f })
            {
                var p = gaFrom + gaDir * s;
                PlaceStand(StandKind.Parking, p.x, p.y, gaBearing - 90f, SizeCode.A, PavementSurface.Grass);
            }
            var gaOut = gaFrom + gaDir * 100f;
            PlaceStand(StandKind.Parking, gaOut.x, gaOut.y, gaBearing - 45f, SizeCode.A, asphalt, StandExit.TaxiOut);
            PlaceStand(StandKind.Parking, -100f, -80f, 90f, SizeCode.A, asphalt, StandExit.TaxiOut);

            // two drive-through de-icing bays (code E) on a bypass beside A at the 09 end
            Line(wide, asphalt, true, SplitAt(-1300f, 330f), Node(-1240f, 250f), Node(-930f, 250f), SplitAt(-870f, 330f));
            foreach (float x in new[] { -1150f, -1030f }) PlaceStand(StandKind.DeIcing, x, 250f, 270f, SizeCode.E, concrete);

            // service roads: from the depot's pad east onto the GA apron and north across the dirt taxiway (give way) to
            // A; round the GA apron and along the heads of the contact stands, jogging south past the code C ones to the
            // deeper code E and F heads (every stand's way in for vehicles: tugs come along it and back in off it, and
            // never drive the taxilane)
            net.Connect(Node(-180f, 52f), Node(-119f, 52f), RunwayRules.RoadWidth, asphalt, false, RoadClass.Service);
            net.Connect(Node(-195f, 78f), net.SplitEdge(net.edges.First(e => e.a == x1 && e.b == x2 || e.a == x2 && e.b == x1),
                new Vector3(-195f, 0f, 330f)), RunwayRules.RoadWidth, asphalt, false, RoadClass.Service);
            var cHead = net.Layout(contact[0]);
            var cLast = net.Layout(contact[3]);
            var eLayout = net.Layout(contact[4]);
            float halfRoad = RunwayRules.RoadWidth * 0.5f;
            float headZ = cHead.At(0f, cHead.far).y - halfRoad;
            float wideZ = eLayout.At(0f, eLayout.far).y - halfRoad;
            float stepX = cLast.Pad.Max(c => c.x), jogX = stepX - halfRoad - 1.5f;
            float wideEnd = net.Layout(contact[6]).Pad.Max(c => c.x);
            int apronRoad = net.NearestNode(new Vector3(-119f, 0f, 52f), 1f).id;
            foreach (var next in new[] { Node(-100f, 30f), Node(-100f, -20f), Node(100f, -20f), Node(100f, headZ), Node(jogX, headZ),
                         Node(jogX, wideZ), Node(wideEnd, wideZ) })
            {
                net.Connect(apronRoad, next, RunwayRules.RoadWidth, asphalt, false, RoadClass.Service);
                apronRoad = next;
            }

            // the terminal: 12 m beyond the head road (room for the rotundas, the links crossing the road), stepping
            // back behind the jog to the code E and F heads, a half-circle west end and a half-circle east end that the
            // fan stands face, their bridges' links reaching it across the gap
            airport.aircraftTypes = AircraftSetup.AllTypes().ToList();
            float northC = headZ - halfRoad - 12f;
            float northE = wideZ - halfRoad - 12f;
            stepX = jogX - halfRoad - 8f;
            float r = (northE - 35f) * 0.5f;
            fanCentre = new Vector2(fanCentre.x, 35f + r);
            var terminal = Terminal.Polygon(new[]
            {
                new Vector2(190f, 35f), new Vector2(fanCentre.x, 35f), new Vector2(fanCentre.x, northE),
                new Vector2(stepX, northE), new Vector2(stepX, northC), new Vector2(190f, northC),
            }, 2);
            BowEdge(terminal, new Vector2(fanCentre.x, 35f), new Vector2(fanCentre.x, northE), r);
            BowEdge(terminal, new Vector2(190f, northC), new Vector2(190f, 35f), (northC - 35f) * 0.5f);
            BowEdge(terminal, new Vector2(190f, 35f), new Vector2(fanCentre.x, 35f), 8f);
            string terminalProblem = airport.TerminalProblem(terminal);
            if (terminalProblem == null) airport.terminals.Add(terminal);
            else Debug.LogError($"AirfieldSetup: the terminal won't fit ({terminalProblem})");
            airport.BuildGround();

            // jet bridges: one per code C stand (the first has a right-hand one too, R1), two for the code E stand, L1, L2
            // and the upper deck's UL1 for the first code F stand, L1 and L2 for the second (the code E and F stands'
            // sharing a bridge head each), and one long one per fan stand
            var left1 = new JetBridge { door = 1 };
            int Bridges(Stand st, params JetBridge[] doors)
            {
                int n = airport.Bridges.SetBridges(st, doors);
                // a door no type here has yet (the double-deck's UL1 before its model is built) is left out quietly
                int wanted = doors.Count(d => airport.Bridges.TypesFor(st).Any(t => !t.Light && BridgeLayout.TryDoor(t, d, out _)));
                if (n < wanted)
                    Debug.LogWarning($"AirfieldSetup: {st.Name} took {n} of {doors.Length} bridges ({string.Join(", ", doors.Select(d => d.DoorName))})");
                return n;
            }
            Bridges(contact[0], left1, new JetBridge { door = 1, side = DoorSide.Right });
            for (int i = 1; i < 4; i++) Bridges(contact[i], left1);
            Bridges(contact[4], left1, new JetBridge { door = 2 });
            Bridges(contact[5], left1, new JetBridge { door = 2 }, new JetBridge { door = 1, upper = true });
            Bridges(contact[6], left1, new JetBridge { door = 2 });
            foreach (var st in fanStands) Bridges(st, left1);
            var rig = go.AddComponent<BridgeRig>();
            rig.prefab = VehicleSetup.Prefab("JetBridge");

            // verges: the main runway's nearly due for mowing, the rest at various lengths
            main.vergeGrass = 14f;
            cross.vergeGrass = 9f;
            strip.vergeGrass = 11f;
            grassStrip.vergeGrass = 8f;
            foreach (var e in net.edges)
                if (e.HasVerges) e.vergeGrass = e.lit ? 12f : 7f;

            // the maintenance depot west of the GA apron, doors facing east onto its forecourt; two large tugs for the
            // wide-bodies
            var depotGo = new GameObject("Depot");
            depotGo.SetActive(false);
            depotGo.transform.SetParent(go.transform, false);
            depotGo.transform.localPosition = new Vector3(-215f, 0f, 50f);
            depotGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var depot = depotGo.AddComponent<MaintenanceDepot>();
            VehicleSetup.Assign(depot);
            depot.snowPloughs = 3;
            depot.mowers = 1;
            depot.smallTugs = 2;
            depot.largeTugs = 2;
            depotGo.SetActive(true);

            airport.RunwaysChanged();
            // parked, ready to leave: narrow-bodies on two contact stands and a fan stand, a wide-body on the code E
            // stand, the double-deck and the long-haul quadjet (when their models exist) on the code F stands
            ParkAircraft(airport, new List<Stand> { contact[1], contact[3], fanStands[1] }, "Narrowbody");
            ParkAircraft(airport, new List<Stand> { contact[4] }, "Widebody");
            ParkAircraft(airport, new List<Stand> { contact[5] }, "DoubleDeck");
            ParkAircraft(airport, new List<Stand> { contact[6] }, "Quadjet");
            return airport;
        }

        /// <summary>Where the aircraft showcase stands, off every taxiway (AddSandboxAirport moves it there).</summary>
        static readonly Vector2 DisplayRamp = new Vector2(600f, -440f);

        /// <summary>Bow the terminal's edge from a to b (either way along it) out by `metres`.</summary>
        static void BowEdge(Terminal t, Vector2 a, Vector2 b, float metres)
        {
            for (int i = 0; i < t.EdgeCount; i++)
            {
                var p = t.corners[i];
                var q = t.corners[(i + 1) % t.EdgeCount];
                if ((Vector2.Distance(p, a) < 0.01f && Vector2.Distance(q, b) < 0.01f) ||
                    (Vector2.Distance(p, b) < 0.01f && Vector2.Distance(q, a) < 0.01f))
                {
                    t.SetBulge(i, metres);
                    return;
                }
            }
            Debug.LogError("AirfieldSetup: no terminal edge to bow");
        }

        /// <summary>An aircraft of a model on each of these stands (children of the Airport, so they go with it), ready
        /// to push back.</summary>
        static void ParkAircraft(Airport airport, List<Stand> stands, string model)
        {
            var jet = AircraftSetup.AllTypes().FirstOrDefault(t => t.model == model);
            var prefab = jet != null ? AircraftSetup.Prefab(jet) : null;
            if (prefab == null) return;
            var liveries = AircraftSetup.AllLiveries();
            var net = airport.Taxiways;
            var t = airport.transform;
            for (int i = 0; i < stands.Count; i++)
            {
                var l = stands[i] != null ? net.Layout(stands[i]) : null;
                if (l == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, t);
                go.name = $"{model} on {stands[i].Name}";
                var at = l.ParkPosition(jet);
                go.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(at.x, 0f, at.y)),
                    Quaternion.LookRotation(t.TransformDirection(new Vector3(l.nose.x, 0f, l.nose.y)), Vector3.up));
                var applier = go.GetComponent<AircraftLiveryApplier>();
                if (applier != null && liveries.Length > 0)
                {
                    applier.liveryAsset = liveries[(i + 1) % liveries.Length];
                    applier.dirt = 0.25f * (i % 3);
                    applier.Apply();
                }
            }
        }

        static Vector3 Heading(float bearing)
        {
            float r = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
