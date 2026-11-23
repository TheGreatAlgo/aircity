using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Airfield.Building
{
    /// <summary>Turns the school's windsock into the wind (Airport.windFrom), a little limp in light air.</summary>
    [ExecuteAlways]
    public sealed class FlyingSchoolWindsock : MonoBehaviour
    {
        void Update() => Refresh();

        public void Refresh()
        {
            var airport = GetComponentInParent<Airport>();
            if (airport == null || transform.parent == null) return;
            float sway = Application.isPlaying ? Mathf.Sin(Time.time * 1.7f) * 4f : 0f;
            // The sock streams downwind; its parent is turned with the school.
            float bearing = airport.windFrom + 180f - transform.parent.eulerAngles.y + airport.transform.eulerAngles.y + sway;
            transform.localRotation = Quaternion.Euler(0f, bearing, 0f);
        }
    }

    /// <summary>
    /// The generated flying school in FlyingSchoolLayout's site frame: a white-boarded clubhouse with a tall glazed
    /// briefing hall facing the flight line, two lower wings, a small observation cab, a veranda and terrace airside,
    /// and the forecourt, car park and driveway throat landside. The flight line's stands are ordinary stands drawn
    /// by StandBuilder; this adds only their tie-down rings.
    /// </summary>
    sealed class FlyingSchoolBuilder
    {
        const string VisualName = "Generated flying school";
        public const int GeometryVersion = 1;
        const float HalfWidth = FlyingSchoolLayout.BuildingWidth * .5f, HalfDepth = FlyingSchoolLayout.BuildingDepth * .5f;
        const float HallHalf = 6f, Floor = FlyingSchoolLayout.FloorHeight;
        const float WingTop = 3.3f, WingOuter = 3.12f, HallAir = 5.3f, HallLand = 3.75f;
        const float CabX = 10.4f, CabHalf = 1.8f, CabSill = 3.6f, CabHead = 5.4f;
        /// <summary>The building shader varies the night glow per window bay by a hash of (bay, uv1.x); this value
        /// lands on a bright one (0.87 of full).</summary>
        const float InteriorBay = 6f;

        public readonly MeshData body = new MeshData(), glazing = new MeshData();
        /// <summary>While above zero, opaque faces are built as lit interior: this share of the shader's night glow.</summary>
        float lit;
        readonly int detail, side;
        readonly float lane, c, z0, z1;

        static readonly Color Board = C(.90f, .89f, .84f), BoardShade = C(.79f, .79f, .75f), Plinth = C(.40f, .42f, .41f),
            Slab = C(.65f, .64f, .59f), Roof = C(.17f, .26f, .30f), Frame = C(.16f, .20f, .22f), Timber = C(.55f, .38f, .23f),
            WoodLight = C(.68f, .50f, .31f), Accent = C(.87f, .44f, .12f), Glass = C(.44f, .63f, .67f), Ink = C(.95f, .93f, .86f),
            Dark = C(.12f, .16f, .18f), Asphalt = C(.23f, .25f, .25f), Concrete = C(.65f, .64f, .59f), Kerb = C(.57f, .58f, .54f),
            White = C(.91f, .91f, .86f), Metal = C(.18f, .22f, .22f), Blue = C(.14f, .39f, .58f), Yellow = C(.88f, .69f, .21f),
            Green = C(.30f, .39f, .24f), Soil = C(.25f, .22f, .17f), Seat = C(.33f, .42f, .44f);
        static Color C(float r, float g, float b) => new Color(r, g, b).linear;

        public FlyingSchoolBuilder(int detail, float lane, int side)
        {
            this.detail = detail; this.lane = FlyingSchoolLayout.Lane(lane); this.side = FlyingSchoolLayout.Side(side);
            c = FlyingSchoolLayout.BuildingCentre(this.lane); z0 = c - HalfDepth; z1 = c + HalfDepth;
        }

        // ------------------------------------------------------------------ objects

        public static void Rebuild(FlyingSchool school)
        {
            Clear(school);
            var airport = school.Airport;
            var root = Build(school.transform, school.Lane, school.DrivewaySide, airport != null ? airport.building : null,
                TieDownType(airport), school.Surface.IsPaved());
            root.name = VisualName;
        }

        public static void Clear(FlyingSchool school)
        {
            var visual = school.transform.Find(VisualName);
            if (visual == null) return;
            visual.gameObject.SetActive(false); visual.name = "Retired flying school mesh";
            AirfieldPart.DestroyAny(visual.gameObject);
        }

        /// <summary>The trainer whose tie-down points place the rings: the airport's first suitable light type.</summary>
        public static AircraftType TieDownType(Airport airport) =>
            airport != null ? airport.aircraftTypes.FirstOrDefault(t => t != null && FlyingSchoolLayout.TrainerProblem(t) == null) : null;

        /// <summary>The whole generated school under `parent`, whose frame is the site frame. Never creates a saved
        /// layout component: review scenes and previews use it too.</summary>
        public static GameObject Build(Transform parent, float lane, int side, Material building, AircraftType tieDowns = null, bool paved = true)
        {
            var root = new GameObject(VisualName) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, false);
            var owner = root.AddComponent<VisitorCentreVisual>();
            owner.geometryVersion = GeometryVersion;
            if (building == null || building.shader == null || building.shader.name != "AirCity/Building")
                building = owner.Own(new Material(Shader.Find("AirCity/Building")) { name = "Flying school palette", hideFlags = HideFlags.DontSave });
            var glass = owner.Own(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Flying school glass", hideFlags = HideFlags.DontSave });
            glass.SetColor("_BaseColor", new Color(.31f, .49f, .53f, .21f));
            glass.SetFloat("_Surface", 1); glass.SetFloat("_Blend", 0);
            glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_ZWrite", 0); glass.SetFloat("_Cull", (float)CullMode.Off);
            glass.SetFloat("_Smoothness", .92f); glass.SetFloat("_Metallic", .15f);
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glass.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            glass.renderQueue = 3000; glass.SetOverrideTag("RenderType", "Transparent");

            var shadowArt = new FlyingSchoolBuilder(2, lane, side); shadowArt.Structure();
            var shadowMesh = owner.Own(shadowArt.body.Apply(null, "Flying school shadow shell"));
            var levels = new LOD[3];
            for (int lod = 0; lod < 3; lod++)
            {
                var art = new FlyingSchoolBuilder(lod, lane, side); art.Structure();
                var shell = Part(root.transform, "Flying school LOD" + lod, owner.Own(art.body.Apply(null, "Flying school structure LOD" + lod)), building);
                shell.shadowCastingMode = ShadowCastingMode.Off;
                var panes = Part(root.transform, "Flying school glass LOD" + lod, owner.Own(art.glazing.Apply(null, "Flying school glazing LOD" + lod)), glass);
                panes.shadowCastingMode = ShadowCastingMode.Off;
                var shadow = Part(root.transform, "Flying school shadow LOD" + lod, shadowMesh, building);
                shadow.shadowCastingMode = ShadowCastingMode.ShadowsOnly; shadow.receiveShadows = false;
                levels[lod] = new LOD(lod == 0 ? .55f : lod == 1 ? .10f : .002f, new Renderer[] { shell, panes, shadow });
            }
            var group = root.AddComponent<LODGroup>(); group.SetLODs(levels); group.RecalculateBounds();

            var ground = new FlyingSchoolBuilder(0, lane, side); ground.Ground();
            Part(root.transform, "Flying school ground", owner.Own(ground.body.Apply(null, "Flying school ground")), building)
                .shadowCastingMode = ShadowCastingMode.Off;
            var details = new FlyingSchoolBuilder(0, lane, side); details.Markings(); details.Fixtures(); details.TieDowns(tieDowns, paved);
            Part(root.transform, "Flying school site furniture", owner.Own(details.body.Apply(null, "Flying school site furniture")), building);

            var probe = new FlyingSchoolBuilder(0, lane, side);
            var sockRoot = new GameObject("Windsock"); sockRoot.transform.SetParent(root.transform, false);
            sockRoot.transform.localPosition = probe.WindsockTop;
            var windsock = sockRoot.AddComponent<FlyingSchoolWindsock>();
            var sock = new FlyingSchoolBuilder(0, lane, side); sock.Sock();
            Part(sockRoot.transform, "Windsock cone", owner.Own(sock.body.Apply(null, "Flying school windsock")), building);

            foreach (float x in new[] { -3.2f, 3.2f })
                Lamp(root.transform, owner, "Hall downlight", new Vector3(x, 3.4f, probe.c - .6f), LightType.Point, 22f, 10f);
            Lamp(root.transform, owner, "Cab light", new Vector3(CabX, 5.0f, probe.z0 + CabHalf + .1f), LightType.Point, 7f, 5f);
            Lamp(root.transform, owner, "Porch downlight", new Vector3(0f, 2.6f, probe.z1 + 1.3f), LightType.Point, 8f, 6f);
            Lamp(root.transform, owner, "Veranda downlight", new Vector3(0f, 2.6f, probe.z0 - 1.6f), LightType.Point, 8f, 8f);
            foreach (float x in new[] { -13.6f, 13.6f })
            {
                var lamp = Lamp(root.transform, owner, "Car park light", new Vector3(x, 3.65f, FlyingSchoolLayout.LotBack(probe.lane) - 1.1f), LightType.Spot, 60f, 18f);
                lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); lamp.spotAngle = 125f; lamp.innerSpotAngle = 85f;
            }
            owner.SetLighting(AircraftLights.Glow);
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            { child.gameObject.hideFlags = HideFlags.DontSave; child.gameObject.layer = parent.gameObject.layer; }
            windsock.Refresh();
            return root;
        }

        static Light Lamp(Transform parent, VisitorCentreVisual owner, string name, Vector3 at, LightType type, float intensity, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = at;
            var light = go.AddComponent<Light>(); light.type = type; light.color = new Color(1f, .82f, .60f);
            light.range = range; light.shadows = LightShadows.None;
            owner.RegisterNightLight(light, intensity);
            return light;
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave }; go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; return renderer;
        }

        // ------------------------------------------------------------------ the clubhouse

        /// <summary>The clubhouse: opaque parts into `body`, real panes into `glazing`.</summary>
        public void Structure()
        {
            Box(new Vector3(0, Floor - .12f, c), new Vector3(HalfWidth * 2f + .3f, .24f, HalfDepth * 2f + .3f), Slab);
            Hall();
            Wing(-1); Wing(1);
            Cab();
            Veranda();
            Porch();
            if (detail < 2) Signs();
        }

        void Hall()
        {
            float slope = (HallLand - HallAir) / (HalfDepth * 2f);
            float RoofAt(float z) => HallAir + slope * (z - z0);
            // The landside wall with the entrance, and the cheeks that stand above the wing roofs.
            Box(new Vector3(0, (HallLand + Floor) * .5f, z1 - .09f), new Vector3(HallHalf * 2f, HallLand - Floor, .18f), Board);
            Box(new Vector3(0, .32f, z1 + .01f), new Vector3(HallHalf * 2f, .5f, .22f), Plinth);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * HallHalf;
                Facing(new Vector3(x, Floor, z0), new Vector3(x, Floor, z1), new Vector3(x, HallLand, z1), new Vector3(x, HallAir, z0), Vector3.right * s, Board);
                Facing(new Vector3(x, Floor, z0), new Vector3(x, Floor, z1), new Vector3(x, HallLand, z1), new Vector3(x, HallAir, z0), Vector3.left * s, BoardShade);
                // Corner posts carry the tall glazed front.
                Box(new Vector3(s * (HallHalf - .11f), (HallAir + Floor) * .5f, z0 + .11f), new Vector3(.22f, HallAir - Floor, .22f), Timber);
            }
            // Entrance: a glazed pair of doors under the porch, a lit sidelight either side.
            Window(new Vector3(0, 1.32f, z1 + .012f), 2.3f, 2.45f, Vector3.right, Vector3.forward, 2, false);
            if (detail < 2)
                foreach (float x in new[] { -3.9f, 3.9f }) Window(new Vector3(x, 1.75f, z1 + .012f), 1.7f, 1.5f, Vector3.right, Vector3.forward, 2);
            // The briefing hall's front: real panes from a low plinth to the roof, timber mullions between them.
            Box(new Vector3(0, .30f, z0 + .05f), new Vector3(HallHalf * 2f - .4f, .46f, .2f), Plinth);
            Pane(new Vector3(0, 2.72f, z0 + .06f), HallHalf * 2f - .44f, 4.36f, Vector3.right);
            Box(new Vector3(0, HallAir - .2f, z0 + .08f), new Vector3(HallHalf * 2f - .3f, .4f, .2f), Frame);
            Box(new Vector3(0, .56f, z0 + .02f), new Vector3(HallHalf * 2f - .4f, .09f, .16f), Frame);
            if (detail < 2)
            {
                for (int i = 1; i < 6; i++) Box(new Vector3(-HallHalf + i * 2f, 2.75f, z0 + .02f), new Vector3(.09f, 4.4f, .16f), Timber);
                Box(new Vector3(0, 3.12f, z0 + .02f), new Vector3(HallHalf * 2f - .4f, .09f, .14f), Frame);
            }
            // One pitch, high over the glass: it throws the light deep into the hall.
            RoofPanel(new Vector3(-HallHalf - .45f, RoofAt(z0 - 1f) + .14f, z0 - 1f), new Vector3(HallHalf + .45f, RoofAt(z0 - 1f) + .14f, z0 - 1f),
                new Vector3(HallHalf + .45f, RoofAt(z1 + .6f) + .14f, z1 + .6f), new Vector3(-HallHalf - .45f, RoofAt(z1 + .6f) + .14f, z1 + .6f), .2f);
            if (detail == 0)
                for (float x = -HallHalf; x <= HallHalf + .01f; x += 1.5f)
                    Beam(new Vector3(x, RoofAt(z0 - 1f) + .2f, z0 - 1f), new Vector3(x, RoofAt(z1 + .6f) + .2f, z1 + .6f), .06f, .07f, Roof * 1.25f);
            if (detail < 2) Interior(RoofAt);
        }

        void Interior(System.Func<float, float> roofAt)
        {
            // Seen through real panes: these surfaces carry the shared night glow themselves, so the hall reads as
            // lit after dark whatever the scene's real lights do.
            lit = .34f;
            Box(new Vector3(0, Floor + .004f, c), new Vector3(HallHalf * 2f - .3f, .008f, HalfDepth * 2f - .3f), C(.70f, .66f, .56f));
            // Exposed rafter ties read through the glass.
            foreach (float z in new[] { z0 + 2.2f, c, z1 - 2.2f }) Beam(new Vector3(-HallHalf, roofAt(z) - .35f, z), new Vector3(HallHalf, roofAt(z) - .35f, z), .16f, .2f, Timber);
            // A briefing table with chairs, a planning counter and the wall chart behind it.
            Box(new Vector3(-2.4f, .72f, c - 1.6f), new Vector3(2.6f, .07f, 1.2f), WoodLight);
            foreach (float x in new[] { -3.5f, -1.3f }) Box(new Vector3(x, .38f, c - 1.6f), new Vector3(.08f, .62f, 1f), Frame);
            foreach (float x in new[] { -3.2f, -2.4f, -1.6f }) foreach (float dz in new[] { -.95f, .95f })
            {
                Box(new Vector3(x, .45f, c - 1.6f + dz), new Vector3(.46f, .07f, .44f), Seat);
                Box(new Vector3(x, .78f, c - 1.6f + dz * 1.22f), new Vector3(.46f, .6f, .06f), Seat);
            }
            Box(new Vector3(3.1f, .55f, c + 1.9f), new Vector3(3.6f, .95f, .8f), Timber);
            Box(new Vector3(3.1f, 1.06f, c + 1.9f), new Vector3(3.8f, .08f, .95f), Plinth);
            Box(new Vector3(-1.2f, 2.05f, z1 - .2f), new Vector3(3.4f, 1.7f, .05f), C(.74f, .80f, .72f));
            Box(new Vector3(-1.2f, 2.05f, z1 - .23f), new Vector3(3.56f, 1.86f, .04f), Frame);
            foreach (float x in new[] { 3.4f, 4.6f })
            {
                Box(new Vector3(x, .44f, c - 2.6f), new Vector3(1f, .2f, .7f), Seat);
                Box(new Vector3(x, .8f, c - 2.3f), new Vector3(1f, .6f, .14f), Seat);
            }
            // The inner faces of the hall's walls and the underside of its roof, a little inside the outer skin.
            Facing(new Vector3(-HallHalf + .02f, Floor, z1 - .2f), new Vector3(HallHalf - .02f, Floor, z1 - .2f),
                new Vector3(HallHalf - .02f, HallLand - .2f, z1 - .2f), new Vector3(-HallHalf + .02f, HallLand - .2f, z1 - .2f), Vector3.back, Board);
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (HallHalf - .02f);
                Facing(new Vector3(x, Floor, z0 + .2f), new Vector3(x, Floor, z1 - .2f), new Vector3(x, roofAt(z1 - .2f) - .2f, z1 - .2f),
                    new Vector3(x, roofAt(z0 + .2f) - .2f, z0 + .2f), Vector3.left * s, Board);
            }
            Facing(new Vector3(-HallHalf, roofAt(z0 + .2f) - .19f, z0 + .2f), new Vector3(HallHalf, roofAt(z0 + .2f) - .19f, z0 + .2f),
                new Vector3(HallHalf, roofAt(z1 - .2f) - .19f, z1 - .2f), new Vector3(-HallHalf, roofAt(z1 - .2f) - .19f, z1 - .2f), Vector3.down, WoodLight);
            lit = 0f;
        }

        void Wing(int s)
        {
            float x = s * (HallHalf + HalfWidth) * .5f, width = HalfWidth - HallHalf, mid = (WingTop + Floor) * .5f, high = WingTop - Floor;
            Box(new Vector3(x, .32f, c), new Vector3(width + .04f, .5f, HalfDepth * 2f + .04f), Plinth);
            Box(new Vector3(x, mid, z0 + .11f), new Vector3(width, high, .22f), Board);
            Box(new Vector3(x, mid, z1 - .11f), new Vector3(width, high, .22f), Board);
            Box(new Vector3(s * (HalfWidth - .11f), mid, c), new Vector3(.22f, high, HalfDepth * 2f), Board);
            if (detail == 0)
            {
                // Shadow gaps between the boards: geometry, so they hold up in close views without a texture.
                for (float y = .95f; y < WingTop - .2f; y += .42f)
                {
                    Box(new Vector3(x, y, z0 - .004f), new Vector3(width - .02f, .022f, .012f), BoardShade);
                    Box(new Vector3(x, y, z1 + .004f), new Vector3(width - .02f, .022f, .012f), BoardShade);
                    Box(new Vector3(s * (HalfWidth + .004f), y, c), new Vector3(.012f, .022f, HalfDepth * 2f - .02f), BoardShade);
                }
            }
            if (detail < 2)
            {
                foreach (float dx in new[] { -1.75f, 1.75f }) Window(new Vector3(x + dx, 1.92f, z0 - .015f), 2.3f, 1.5f, Vector3.left, Vector3.back, 2);
                Window(new Vector3(x, 1.92f, z1 + .015f), 4.6f, 1.5f, Vector3.right, Vector3.forward, 3);
                Window(new Vector3(s * (HalfWidth + .015f), 1.92f, c), 4.2f, 1.5f, Vector3.back * s, Vector3.right * s, 3);
            }
            else
            {
                Window(new Vector3(x, 1.92f, z0 - .015f), 5.8f, 1.5f, Vector3.left, Vector3.back, 1);
                Window(new Vector3(x, 1.92f, z1 + .015f), 4.6f, 1.5f, Vector3.right, Vector3.forward, 1);
            }
            // A shallow fall away from the hall, with a closed soffit and a signal-orange fascia.
            float inner = s * HallHalf, outer = s * (HalfWidth + .6f), front = z0 - .55f, back = z1 + .55f;
            RoofPanel(new Vector3(inner, WingTop + .04f, front), new Vector3(outer, WingOuter, front),
                new Vector3(outer, WingOuter, back), new Vector3(inner, WingTop + .04f, back), .16f);
            Beam(new Vector3(inner, WingTop - .05f, front - .03f), new Vector3(outer, WingOuter - .09f, front - .03f), .05f, .16f, Accent);
            Beam(new Vector3(inner, WingTop - .05f, back + .03f), new Vector3(outer, WingOuter - .09f, back + .03f), .05f, .16f, Accent);
            Beam(new Vector3(outer + s * .03f, WingOuter - .09f, front), new Vector3(outer + s * .03f, WingOuter - .09f, back), .05f, .16f, Accent);
            if (detail < 2)
            {
                // Downpipe at the low outer corner.
                Box(new Vector3(s * (HalfWidth + .08f), 1.6f, z1 - .35f), new Vector3(.09f, WingOuter - .3f, .09f), Frame);
            }
        }

        void Cab()
        {
            float zc = z0 + CabHalf + .1f;
            // A glazed lookout over the flight line and the runway beyond: the instructors' desk.
            Box(new Vector3(CabX, (CabSill + WingOuter) * .5f - .05f, zc), new Vector3(CabHalf * 2f, CabSill - WingOuter + .3f, CabHalf * 2f), Board);
            Box(new Vector3(CabX, CabSill + .02f, zc), new Vector3(CabHalf * 2f + .12f, .08f, CabHalf * 2f + .12f), Frame);
            float mid = (CabSill + CabHead) * .5f, high = CabHead - CabSill;
            foreach (var (normal, right) in new[] { (Vector3.back, Vector3.left), (Vector3.forward, Vector3.right), (Vector3.right, Vector3.back), (Vector3.left, Vector3.forward) })
            {
                Pane(new Vector3(CabX, mid, zc) + normal * (CabHalf - .03f), CabHalf * 2f - .1f, high, right);
                if (detail < 2) OrientedBox(new Vector3(CabX, mid, zc) + normal * (CabHalf - .03f), right, normal, new Vector3(.07f, high, .09f), Frame);
            }
            foreach (float dx in new[] { -1f, 1f }) foreach (float dz in new[] { -1f, 1f })
                Box(new Vector3(CabX + dx * (CabHalf - .05f), mid, zc + dz * (CabHalf - .05f)), new Vector3(.13f, high, .13f), Frame);
            Box(new Vector3(CabX, CabHead + .1f, zc), new Vector3(CabHalf * 2f + 1.1f, .2f, CabHalf * 2f + 1.1f), Roof, .35f);
            Box(new Vector3(CabX, CabHead - .03f, zc), new Vector3(CabHalf * 2f + 1.14f, .07f, CabHalf * 2f + 1.14f), Accent);
            if (detail < 2)
            {
                lit = .34f;
                Box(new Vector3(CabX, CabSill + .45f, zc - .9f), new Vector3(2.6f, .8f, .7f), Seat);
                Box(new Vector3(CabX - .5f, CabSill + .95f, zc - .95f), new Vector3(.7f, .32f, .08f), Frame);
                Box(new Vector3(CabX, CabSill + .07f, zc), new Vector3(CabHalf * 2f - .2f, .04f, CabHalf * 2f - .2f), C(.70f, .66f, .56f));
                Facing(new Vector3(CabX - CabHalf, CabHead - .02f, zc - CabHalf), new Vector3(CabX + CabHalf, CabHead - .02f, zc - CabHalf),
                    new Vector3(CabX + CabHalf, CabHead - .02f, zc + CabHalf), new Vector3(CabX - CabHalf, CabHead - .02f, zc + CabHalf), Vector3.down, Board);
                lit = 0f;
                // Radio mast and a wind vane.
                Cylinder(new Vector3(CabX + 1.25f, CabHead + 1.45f, zc + 1.25f), .035f, 2.5f, Metal, 6);
                Beam(new Vector3(CabX + .9f, CabHead + 2.3f, zc + 1.25f), new Vector3(CabX + 1.6f, CabHead + 2.3f, zc + 1.25f), .03f, .03f, Metal);
                Beam(new Vector3(CabX + 1.25f, CabHead + 2.0f, zc + .95f), new Vector3(CabX + 1.25f, CabHead + 2.0f, zc + 1.55f), .03f, .03f, Metal);
                Box(new Vector3(CabX + 1.25f, CabHead + 2.74f, zc + 1.25f), new Vector3(.09f, .09f, .09f), C(.80f, .16f, .12f));
            }
        }

        void Veranda()
        {
            float outer = z0 - 3.4f;
            RoofPanel(new Vector3(-HallHalf - .2f, 3.06f, z0 + .1f), new Vector3(HallHalf + .2f, 3.06f, z0 + .1f),
                new Vector3(HallHalf + .2f, 2.76f, outer), new Vector3(-HallHalf - .2f, 2.76f, outer), .14f);
            Beam(new Vector3(-HallHalf, 2.62f, outer + .3f), new Vector3(HallHalf, 2.62f, outer + .3f), .18f, .22f, Timber);
            foreach (float x in new[] { -5.8f, -1.95f, 1.95f, 5.8f })
            {
                Box(new Vector3(x, 1.42f, outer + .3f), new Vector3(.16f, 2.4f, .16f), Timber);
                Box(new Vector3(x, .19f, outer + .3f), new Vector3(.3f, .24f, .3f), Plinth);
                if (detail < 2) Beam(new Vector3(x, 2.94f, z0), new Vector3(x, 2.66f, outer + .2f), .1f, .14f, WoodLight);
            }
        }

        void Porch()
        {
            RoofPanel(new Vector3(-2.6f, 2.98f, z1 - .1f), new Vector3(2.6f, 2.98f, z1 - .1f),
                new Vector3(2.6f, 2.78f, z1 + 2.4f), new Vector3(-2.6f, 2.78f, z1 + 2.4f), .14f);
            foreach (float x in new[] { -2.3f, 2.3f })
            {
                Box(new Vector3(x, 1.45f, z1 + 2.1f), new Vector3(.16f, 2.5f, .16f), Timber);
                Box(new Vector3(x, .19f, z1 + 2.1f), new Vector3(.3f, .24f, .3f), Plinth);
            }
            Beam(new Vector3(-2.5f, 2.66f, z1 + 2.1f), new Vector3(2.5f, 2.66f, z1 + 2.1f), .16f, .2f, Timber);
        }

        void Signs()
        {
            // Lettering is mesh strokes, independent of any font, on both public faces.
            Box(new Vector3(0, 3.36f, z1 + .05f), new Vector3(7.4f, .6f, .1f), Frame);
            Text("FLIGHT SCHOOL", new Vector3(0, 3.17f, z1 + .12f), .38f, Ink, true);
            Text("FLIGHT SCHOOL", new Vector3(0, HallAir - .36f, z0 - .04f), .30f, Ink, false);
        }

        // ------------------------------------------------------------------ the site

        /// <summary>Opaque ground: terrace, forecourt, car park and the driveway throat (FlyingSchoolLayout.PavedPolygons).</summary>
        public void Ground()
        {
            float d = FlyingSchoolLayout.ApronDepth(lane), front = FlyingSchoolLayout.LotFront(lane), back = FlyingSchoolLayout.LotBack(lane);
            float half = FlyingSchoolLayout.LotHalfWidth, walk = FlyingSchoolLayout.WalkSurface, lot = FlyingSchoolLayout.ParkingSurface;
            float aisle = FlyingSchoolLayout.AisleCentre(lane), a = FlyingSchoolLayout.AisleWidth * .5f;
            // The terrace starts a little under the flight line's far edge, so no gap opens between them.
            Flat(-half, d - .4f, half, z0, walk, Concrete);
            Quad(new Vector3(-half, AirfieldHeights.Ground, d - .4f), new Vector3(half, AirfieldHeights.Ground, d - .4f),
                new Vector3(half, walk, d - .4f), new Vector3(-half, walk, d - .4f), Kerb);
            foreach (int s in new[] { -1, 1 })
                Flat(Mathf.Min(s * HalfWidth, s * half), z0, Mathf.Max(s * HalfWidth, s * half), z1, walk, Concrete);
            Flat(-half, z1, half, front, walk, Concrete);
            Flat(-half, front, half, back, lot, Asphalt);
            // The public road ends on the entrance node with its crowned deck; rise to meet it under the cap.
            foreach (var span in new[] { (half, half + 2f), (half + 2f, half + FlyingSchoolLayout.ThroatLength) })
                foreach (var cross in new[] { (aisle - a, aisle), (aisle, aisle + a) })
                {
                    float Y(float x, float z)
                    {
                        float road = .174f + .065f * (1f - Mathf.Abs(z - aisle) / a);
                        return Mathf.Lerp(lot, road, Mathf.Clamp01((x - half) / 2f));
                    }
                    Vector3 P(float x, float z) => new Vector3(side * x, Y(x, z), z);
                    Facing(P(span.Item1, cross.Item1), P(span.Item1, cross.Item2), P(span.Item2, cross.Item2), P(span.Item2, cross.Item1), Vector3.up, Asphalt);
                }
            // Flush ramps: the forecourt meets the asphalt without a step on the way to the door.
            Quad(new Vector3(-1.5f, walk, front), new Vector3(-1.5f, lot, front + .5f), new Vector3(1.5f, lot, front + .5f), new Vector3(1.5f, walk, front), Concrete);
        }

        public void Markings()
        {
            const float y = .043f;
            float front = FlyingSchoolLayout.LotFront(lane), half = FlyingSchoolLayout.LotHalfWidth;
            float aisle = FlyingSchoolLayout.AisleCentre(lane), a = FlyingSchoolLayout.AisleWidth * .5f;
            foreach (var bay in FlyingSchoolLayout.ParkingBays(lane))
            {
                float x0 = bay.centre.x - bay.width * .5f, x1 = bay.centre.x + bay.width * .5f;
                float b0 = bay.centre.y - bay.length * .5f, b1 = bay.centre.y + bay.length * .5f;
                Line(new Vector2(x0, b0), new Vector2(x0, b1), .075f, y, White);
                Line(new Vector2(x1, b0), new Vector2(x1, b1), .075f, y, White);
                float head = bay.frontRow ? b0 : b1;
                Line(new Vector2(x0, head), new Vector2(x1, head), .075f, y, White);
                float stop = bay.frontRow ? b0 + .65f : b1 - .65f;
                Box(new Vector3(bay.centre.x, .105f, stop), new Vector3(1.65f, .14f, .16f), Kerb);
                if (!bay.accessible) continue;
                Flat(bay.centre.x - 1.15f, bay.centre.y - .95f, bay.centre.x + 1.15f, bay.centre.y + 1.05f, y + .001f, Blue);
                Wheelchair(bay.centre, y + .004f);
            }
            // The hatched path between the accessible bays leads straight to the door; a crossing serves the far row.
            Line(new Vector2(-1.7f, front + .25f), new Vector2(-1.7f, aisle - a), .08f, y, White);
            Line(new Vector2(1.7f, front + .25f), new Vector2(1.7f, aisle - a), .08f, y, White);
            for (float z = front + .7f; z < aisle - a - .5f; z += .7f) Line(new Vector2(-1.64f, z), new Vector2(1.64f, z + .55f), .065f, y, White);
            for (float z = aisle - a + .35f; z < aisle + a - .5f; z += .85f) Flat(-1.2f, z, 1.2f, z + .42f, y + .001f, White);
            // Give-way bars where the aisle meets the driveway.
            for (float z = aisle + .25f; z < aisle + a - .2f; z += .65f) Flat(side * (half - 1.2f), z, side * (half - .95f), z + .42f, y, White);
            foreach (float x in new[] { -8.5f, 8.5f })
            {
                float sign = x < 0 ? 1 : -1, z = aisle + (x < 0 ? -1.4f : 1.4f);
                Line(new Vector2(x - sign * 1.15f, z), new Vector2(x + sign * 1.15f, z), .12f, y, White);
                Line(new Vector2(x + sign * 1.15f, z), new Vector2(x + sign * .45f, z + .55f), .12f, y, White);
                Line(new Vector2(x + sign * 1.15f, z), new Vector2(x + sign * .45f, z - .55f), .12f, y, White);
            }
        }

        public void Fixtures()
        {
            float d = FlyingSchoolLayout.ApronDepth(lane), front = FlyingSchoolLayout.LotFront(lane), back = FlyingSchoolLayout.LotBack(lane);
            float half = FlyingSchoolLayout.LotHalfWidth, aisle = FlyingSchoolLayout.AisleCentre(lane), a = FlyingSchoolLayout.AisleWidth * .5f;
            // Kerbs round the car park, open at the driveway and the path to the door.
            Beam(new Vector3(-half, .10f, back), new Vector3(half, .10f, back), .20f, .15f, Kerb);
            foreach (int s in new[] { -1, 1 })
            {
                if (s == side)
                {
                    Beam(new Vector3(s * half, .10f, front), new Vector3(s * half, .10f, aisle - a - .25f), .20f, .15f, Kerb);
                    Beam(new Vector3(s * half, .10f, aisle + a + .25f), new Vector3(s * half, .10f, back), .20f, .15f, Kerb);
                }
                else Beam(new Vector3(s * half, .10f, front), new Vector3(s * half, .10f, back), .20f, .15f, Kerb);
            }
            // A low post-and-rail fence keeps visitors off the flight line; a gate stands open at each tie-down.
            float fence = d + .35f;
            var gaps = Enumerable.Range(0, FlyingSchoolLayout.Spots).Select(FlyingSchoolLayout.SpotOffset).Where(x => Mathf.Abs(x) < half - 1f).ToList();
            float from = -half + .2f;
            foreach (float gap in gaps.Append(half + 1.3f))
            {
                float to = Mathf.Min(gap - 1.3f, half - .2f);
                if (to - from > .6f) Fence(from, to, fence);
                from = gap + 1.3f;
            }
            // The terrace: two picnic tables and a bench under the veranda, planters at the corners.
            Picnic(new Vector3(-9.6f, FlyingSchoolLayout.WalkSurface, d + 2.6f));
            Picnic(new Vector3(9.6f, FlyingSchoolLayout.WalkSurface, d + 2.6f));
            Bench(new Vector3(-3.2f, FlyingSchoolLayout.WalkSurface, z0 - .55f), 1f);
            Bench(new Vector3(3.2f, FlyingSchoolLayout.WalkSurface, z0 - .55f), 1f);
            Planter(new Vector3(-13.9f, FlyingSchoolLayout.WalkSurface, c), new Vector3(1.1f, .4f, 6f));
            Planter(new Vector3(-side * 9.6f, FlyingSchoolLayout.WalkSurface, z1 + 2.3f), new Vector3(7f, .4f, 1.5f));
            Planter(new Vector3(side * 9.6f, FlyingSchoolLayout.WalkSurface, z1 + 2.3f), new Vector3(7f, .4f, 1.5f));
            // Cycle hoops beside the porch.
            foreach (float x in new[] { 3.6f, 4.5f, 5.4f })
            {
                Beam(new Vector3(x, .08f, z1 + .5f), new Vector3(x, .95f, z1 + .5f), .06f, .06f, Metal);
                Beam(new Vector3(x, .95f, z1 + .5f), new Vector3(x, .95f, z1 + 1.4f), .06f, .06f, Metal);
                Beam(new Vector3(x, .95f, z1 + 1.4f), new Vector3(x, .08f, z1 + 1.4f), .06f, .06f, Metal);
            }
            foreach (float x in new[] { -13.6f, 13.6f }) LampPost(new Vector3(x, 0, back - .6f));
            ParkingSign(new Vector3(side * (half - .7f), 0, aisle + a + 1.1f));
            // The windsock on its mast, clear of the wings at the terrace's corner.
            var top = WindsockTop;
            Cylinder(new Vector3(top.x, top.y * .5f, top.z), .05f, top.y, Metal, 8);
            Box(new Vector3(top.x, .2f, top.z), new Vector3(.42f, .3f, .42f), Kerb);
            for (int i = 0; i < 8; i++)
            {
                float u = i * Mathf.PI / 4f, v = (i + 1) * Mathf.PI / 4f;
                Beam(top + new Vector3(0, Mathf.Sin(u), Mathf.Cos(u)) * .34f, top + new Vector3(0, Mathf.Sin(v), Mathf.Cos(v)) * .34f, .03f, .03f, Metal);
            }
        }

        /// <summary>The top of the windsock mast, in the site frame.</summary>
        public Vector3 WindsockTop => new Vector3(-FlyingSchoolLayout.BlockHalfWidth + 1.2f, 6.4f, FlyingSchoolLayout.ApronDepth(lane) + 1.5f);

        /// <summary>The sock itself, about its hoop: it streams along local +Z.</summary>
        public void Sock()
        {
            const int rings = 6, sides = 10;
            for (int r = 0; r < rings; r++)
            {
                float za = r * .42f, zb = (r + 1) * .42f, ra = Mathf.Lerp(.33f, .13f, r / (float)rings), rb = Mathf.Lerp(.33f, .13f, (r + 1) / (float)rings);
                float ya = -za * za * .05f, yb = -zb * zb * .05f;
                var colour = r % 2 == 0 ? Accent : White;
                for (int i = 0; i < sides; i++)
                {
                    float u = i * Mathf.PI * 2f / sides, v = (i + 1) * Mathf.PI * 2f / sides;
                    var a = new Vector3(Mathf.Cos(u) * ra, ya + Mathf.Sin(u) * ra, za); var b = new Vector3(Mathf.Cos(v) * ra, ya + Mathf.Sin(v) * ra, za);
                    var e = new Vector3(Mathf.Cos(v) * rb, yb + Mathf.Sin(v) * rb, zb); var f = new Vector3(Mathf.Cos(u) * rb, yb + Mathf.Sin(u) * rb, zb);
                    var outward = new Vector3(Mathf.Cos((u + v) * .5f), Mathf.Sin((u + v) * .5f), 0f);
                    Facing(a, b, e, f, outward, colour);
                    Facing(a, b, e, f, -outward, colour * .8f);
                }
            }
        }

        /// <summary>Yellow tie-down rings set in the stands, where this type's wings and tail are anchored. On grass
        /// each ring sits in a small concrete anchor pad, and white corner boards mark the tie-down, so the flight
        /// line can be found without pavement or paint.</summary>
        public void TieDowns(AircraftType type, bool paved = true)
        {
            if (type == null) return;
            for (int i = 0; i < FlyingSchoolLayout.Spots; i++)
            {
                var layout = StandLayout.Of(FlyingSchoolLayout.SpotStand(Vector2.zero, 0f, lane, PavementSurface.Asphalt, i));
                if (layout == null) continue;
                var park = layout.ParkPosition(type); var nose = layout.nose; var right = Geo.Right(nose);
                if (!paved)
                {
                    float half = type.Wingspan * .5f + 1.2f, ahead = type.bounds.max.z + 1.2f, behind = type.bounds.min.z - 1.2f;
                    foreach (float sx in new[] { -1f, 1f }) foreach (float z in new[] { ahead, behind })
                    {
                        var corner = park + right * (sx * half) + nose * z;
                        float into = z > 0f ? -1f : 1f;
                        Line(corner, corner - right * (sx * 1.4f), .22f, AirfieldHeights.Paint + .006f, White);
                        Line(corner, corner + nose * (into * 1.4f), .22f, AirfieldHeights.Paint + .006f, White);
                    }
                }
                foreach (var point in type.points)
                {
                    if (point.type != ServicePointType.TieDown) continue;
                    var at = park + right * point.position.x + nose * point.position.z;
                    if (!paved) Flat(at.x - .45f, at.y - .45f, at.x + .45f, at.y + .45f, AirfieldHeights.Paint + .001f, Concrete);
                    Ring(at, .17f, .07f, AirfieldHeights.Paint + .004f, Yellow);
                    Cylinder(new Vector3(at.x, AirfieldHeights.Paint + .02f, at.y), .045f, .04f, Metal, 6);
                }
            }
        }

        void Fence(float x0, float x1, float z)
        {
            int posts = Mathf.Max(2, Mathf.CeilToInt((x1 - x0) / 2.2f) + 1);
            for (int i = 0; i < posts; i++)
                Box(new Vector3(Mathf.Lerp(x0, x1, i / (float)(posts - 1)), .56f, z), new Vector3(.1f, .96f, .1f), White);
            foreach (float y in new[] { .5f, .92f }) Beam(new Vector3(x0, y, z), new Vector3(x1, y, z), .05f, .09f, White);
        }

        void Picnic(Vector3 at)
        {
            Box(at + new Vector3(0, .74f, 0), new Vector3(1.9f, .06f, .8f), WoodLight);
            foreach (float dz in new[] { -.72f, .72f }) Box(at + new Vector3(0, .44f, dz), new Vector3(1.9f, .05f, .28f), WoodLight);
            foreach (float dx in new[] { -.7f, .7f })
            {
                Beam(at + new Vector3(dx, 0, -.75f), at + new Vector3(dx, .72f, -.2f), .07f, .07f, Timber);
                Beam(at + new Vector3(dx, 0, .75f), at + new Vector3(dx, .72f, .2f), .07f, .07f, Timber);
                Beam(at + new Vector3(dx, .42f, -.8f), at + new Vector3(dx, .42f, .8f), .07f, .06f, Timber);
            }
        }

        void Bench(Vector3 at, float back)
        {
            foreach (float x in new[] { -.8f, .8f })
            { Box(at + new Vector3(x, .26f, 0), new Vector3(.09f, .5f, .6f), Metal); Box(at + new Vector3(x, .72f, back * .27f), new Vector3(.09f, .6f, .08f), Metal); }
            for (int i = 0; i < 4; i++) Box(at + new Vector3(0, .5f, -.23f + i * .15f), new Vector3(2f, .06f, .12f), WoodLight);
            for (int i = 0; i < 3; i++) Box(at + new Vector3(0, .7f + i * .14f, back * .3f), new Vector3(2f, .11f, .06f), WoodLight);
        }

        void Planter(Vector3 at, Vector3 size)
        {
            Box(at + Vector3.up * (size.y * .5f), size, Kerb);
            Box(at + Vector3.up * (size.y + .01f), new Vector3(size.x - .18f, .025f, size.z - .18f), Soil);
            for (float z = -size.z * .5f + .5f; z <= size.z * .5f - .4f; z += .8f)
                for (float x = -size.x * .5f + .5f; x <= size.x * .5f - .4f; x += .8f)
                {
                    var p = at + new Vector3(x, size.y + .02f, z);
                    foreach (var lean in new[] { new Vector3(.18f, .36f, 0), new Vector3(-.18f, .30f, .12f), new Vector3(.04f, .34f, -.20f) })
                        Beam(p, p + lean, .13f, .12f, Green);
                }
        }

        void LampPost(Vector3 at)
        {
            Cylinder(at + Vector3.up * 1.95f, .065f, 3.8f, Metal, 8);
            Box(at + new Vector3(0, 3.85f, -.38f), new Vector3(.25f, .12f, 1f), Metal);
            int first = body.Count;
            Box(at + new Vector3(0, 3.78f, -.48f), new Vector3(.18f, .025f, .56f), C(.98f, .91f, .69f));
            // A glazed face glows at night with the rest of the lit glass.
            for (int i = first; i < body.Count; i++) { body.uv0[i] = new Vector4(1, .85f, 10, 3.78f); body.uv1[i] = new Vector4(2, 2.5f, 0, 0); }
        }

        void ParkingSign(Vector3 at)
        {
            Cylinder(at + Vector3.up * .85f, .04f, 1.6f, Metal, 8);
            Box(at + Vector3.up * 1.65f, new Vector3(.07f, .72f, .72f), Blue);
            foreach (float x in new[] { -.041f, .041f })
            {
                Box(at + new Vector3(x, 1.65f, x > 0 ? .14f : -.14f), new Vector3(.008f, .46f, .07f), White);
                Box(at + new Vector3(x, 1.85f, 0), new Vector3(.008f, .065f, .28f), White);
                Box(at + new Vector3(x, 1.65f, 0), new Vector3(.008f, .065f, .28f), White);
                Box(at + new Vector3(x, 1.75f, x > 0 ? -.12f : .12f), new Vector3(.008f, .20f, .065f), White);
            }
        }

        void Wheelchair(Vector2 at, float y)
        {
            Ring(at + new Vector2(-.15f, -.25f), .55f, .10f, y, White);
            Ring(at + new Vector2(-.12f, .67f), .15f, .15f, y, White);
            Line(at + new Vector2(-.10f, .40f), at + new Vector2(-.03f, -.10f), .12f, y, White);
            Line(at + new Vector2(-.03f, -.10f), at + new Vector2(.53f, -.10f), .12f, y, White);
            Line(at + new Vector2(.53f, -.10f), at + new Vector2(.73f, -.65f), .12f, y, White);
            Line(at + new Vector2(.73f, -.65f), at + new Vector2(.98f, -.65f), .12f, y, White);
        }

        // ------------------------------------------------------------------ primitives

        void Window(Vector3 centre, float width, float height, Vector3 right, Vector3 normal, int bays, bool sill = true)
        {
            // A dark reveal, then lit glazing (the shared building shader's glass), framed and divided.
            OrientedBox(centre - normal * .02f, right, normal, new Vector3(width + .18f, height + .18f, .07f), Frame);
            var at = centre + normal * .05f;
            Quad(at - right * width * .5f - Vector3.up * height * .5f, at + right * width * .5f - Vector3.up * height * .5f,
                at + right * width * .5f + Vector3.up * height * .5f, at - right * width * .5f + Vector3.up * height * .5f, Glass, .95f, 1f);
            for (int i = 0; i <= bays; i++)
                OrientedBox(at + right * (width * (i / (float)bays - .5f)) + normal * .04f, right, normal, new Vector3(.065f, height + .10f, .1f), Frame);
            if (sill) OrientedBox(at - Vector3.up * (height * .5f + .035f), right, normal, new Vector3(width + .23f, .1f, .24f), Plinth);
            OrientedBox(at + Vector3.up * height * .5f, right, normal, new Vector3(width + .1f, .065f, .1f), Frame);
        }

        void Pane(Vector3 centre, float width, float height, Vector3 right)
        {
            var mesh = glazing; var up = Vector3.up * height * .5f; var r = right * width * .5f;
            var a = centre - r - up; var b = centre + r - up; var e = centre + r + up; var f = centre - r + up;
            var normal = Vector3.Cross(b - a, e - a).normalized; int first = mesh.Count;
            foreach (var p in new[] { a, b, e, f }) mesh.Vertex(p, normal, Glass, new Vector4(0, .95f, p.x + p.z, p.y), new Vector4(0, 2.5f, 0, 0));
            mesh.Quad(0, first, first + 1, first + 2, first + 3);
        }

        void RoofPanel(Vector3 a, Vector3 b, Vector3 e, Vector3 f, float thickness)
        {
            if (Vector3.Cross(b - a, e - a).y < 0f) { var t = b; b = f; f = t; }
            Quad(a, b, e, f, Roof, .35f);
            var down = Vector3.down * thickness;
            Quad(f + down, e + down, b + down, a + down, Timber, .12f);
            var top = new[] { a, b, e, f };
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; Quad(top[j], top[i], top[i] + down, top[j] + down, Frame, .3f); }
        }

        void Text(string text, Vector3 at, float height, Color colour, bool facesForward)
        {
            float width = height * .62f, gap = height * .24f, total = text.Length * (width + gap) - gap;
            // A face looking along +Z is read from +Z, so its left is local +X; one looking along -Z the other way.
            float step = facesForward ? -1f : 1f, x = at.x - step * total * .5f;
            foreach (char letter in text)
            {
                if (Glyphs.TryGetValue(letter, out var glyph))
                    foreach (var stroke in glyph.Split(';'))
                    {
                        var values = stroke.Split(',');
                        float F(int i) => float.Parse(values[i], System.Globalization.CultureInfo.InvariantCulture);
                        Beam(new Vector3(x + step * width * F(0), at.y + height * F(1), at.z), new Vector3(x + step * width * F(2), at.y + height * F(3), at.z),
                            height * .08f, .035f, colour);
                    }
                x += step * (width + gap);
            }
        }

        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['F'] = "0,0,0,1;0,1,1,1;0,.52,.8,.52", ['L'] = "0,1,0,0;0,0,1,0", ['I'] = ".5,0,.5,1",
            ['G'] = "1,.8,.8,1;.8,1,.2,1;.2,1,0,.8;0,.8,0,.2;0,.2,.2,0;.2,0,.8,0;.8,0,1,.2;1,.2,1,.48;1,.48,.55,.48",
            ['H'] = "0,0,0,1;1,0,1,1;0,.5,1,.5", ['T'] = "0,1,1,1;.5,1,.5,0",
            ['S'] = "1,.82,.8,1;.8,1,.2,1;.2,1,0,.82;0,.82,0,.66;0,.66,.2,.52;.2,.52,.8,.48;.8,.48,1,.34;1,.34,1,.18;1,.18,.8,0;.8,0,.2,0;.2,0,0,.18",
            ['C'] = "1,.8,.8,1;.8,1,.2,1;.2,1,0,.8;0,.8,0,.2;0,.2,.2,0;.2,0,.8,0;.8,0,1,.2",
            ['O'] = ".2,0,.8,0;.8,0,1,.2;1,.2,1,.8;1,.8,.8,1;.8,1,.2,1;.2,1,0,.8;0,.8,0,.2;0,.2,.2,0",
        };

        void Box(Vector3 centre, Vector3 size, Color colour, float smooth = .2f) => OrientedBox(centre, Vector3.right, Vector3.forward, size, colour, smooth);

        void OrientedBox(Vector3 centre, Vector3 right, Vector3 forward, Vector3 size, Color colour, float smooth = .2f)
        {
            var r = right * (size.x * .5f); var u = Vector3.up * (size.y * .5f); var f = forward * (size.z * .5f);
            Vector3 P(int x, int y, int z) => centre + r * x + u * y + f * z;
            Facing(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), forward, colour, smooth);
            Facing(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -forward, colour, smooth);
            Facing(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), right, colour, smooth);
            Facing(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -right, colour, smooth);
            Facing(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), Vector3.up, colour, smooth);
            Facing(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down, colour, smooth);
        }

        void Beam(Vector3 from, Vector3 to, float width, float depth, Color colour)
        {
            var axis = (to - from).normalized; var right = Vector3.Cross(axis, Vector3.forward);
            if (right.sqrMagnitude < .01f) right = Vector3.right; else right.Normalize();
            var up = Vector3.Cross(axis, right).normalized; var centre = (from + to) * .5f;
            var r = right * width * .5f; var u = up * depth * .5f; var f = (to - from) * .5f;
            Vector3 P(int x, int y, int z) => centre + r * x + u * y + f * z;
            Facing(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), axis, colour);
            Facing(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -axis, colour);
            Facing(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), right, colour);
            Facing(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -right, colour);
            Facing(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), up, colour);
            Facing(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -up, colour);
        }

        void Cylinder(Vector3 centre, float radius, float height, Color colour, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float u = i * Mathf.PI * 2f / count, v = (i + 1) * Mathf.PI * 2f / count;
                var a = new Vector3(Mathf.Cos(u), 0, Mathf.Sin(u)) * radius; var b = new Vector3(Mathf.Cos(v), 0, Mathf.Sin(v)) * radius;
                var low = centre + Vector3.down * height * .5f; var high = centre + Vector3.up * height * .5f;
                Facing(low + a, low + b, high + b, high + a, (a + b).normalized, colour);
                Facing(high, high + a, high + b, high, Vector3.up, colour);
            }
        }

        void Ring(Vector2 centre, float radius, float thickness, float y, Color colour)
        {
            const int count = 20;
            for (int i = 0; i < count; i++)
            {
                float u = i * Mathf.PI * 2 / count, v = (i + 1) * Mathf.PI * 2 / count;
                var da = new Vector2(Mathf.Cos(u), Mathf.Sin(u)); var db = new Vector2(Mathf.Cos(v), Mathf.Sin(v));
                Facing(Geo.X0Z(centre + da * (radius - thickness * .5f), y), Geo.X0Z(centre + db * (radius - thickness * .5f), y),
                    Geo.X0Z(centre + db * (radius + thickness * .5f), y), Geo.X0Z(centre + da * (radius + thickness * .5f), y), Vector3.up, colour);
            }
        }

        void Flat(float x0, float za, float x1, float zb, float y, Color colour) =>
            Facing(new Vector3(x0, y, za), new Vector3(x0, y, zb), new Vector3(x1, y, zb), new Vector3(x1, y, za), Vector3.up, colour);

        void Line(Vector2 a, Vector2 b, float width, float y, Color colour)
        {
            var right = Geo.Right((b - a).normalized) * width * .5f;
            Facing(Geo.X0Z(a - right, y), Geo.X0Z(b - right, y), Geo.X0Z(b + right, y), Geo.X0Z(a + right, y), Vector3.up, colour);
        }

        /// <summary>A quad wound so that it faces `outward`.</summary>
        void Facing(Vector3 a, Vector3 b, Vector3 e, Vector3 f, Vector3 outward, Color colour, float smooth = .2f)
        {
            var normal = Vector3.Cross(b - a, e - a);
            if (normal.sqrMagnitude < 1e-12f) normal = Vector3.Cross(e - a, f - a);
            if (Vector3.Dot(normal, outward) < 0f) Quad(f, e, b, a, colour, smooth);
            else Quad(a, b, e, f, colour, smooth);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 e, Vector3 f, Color colour, float smooth = .2f, float glass = 0f)
        {
            var normal = Vector3.Cross(b - a, e - a);
            if (normal.sqrMagnitude < 1e-12f) normal = Vector3.Cross(e - a, f - a);
            normal.Normalize(); int first = body.Count;
            bool interior = lit > 0f && glass == 0f;
            if (interior)
            {
                // The glass channel darkens the day colour towards the shader's tint: brighten it back by that much.
                colour = new Color(Mathf.Min(1f, colour.r / Mathf.Lerp(1f, .10f, lit)), Mathf.Min(1f, colour.g / Mathf.Lerp(1f, .15f, lit)),
                    Mathf.Min(1f, colour.b / Mathf.Lerp(1f, .18f, lit)), colour.a);
                smooth = Mathf.Max(0f, (smooth - .92f * lit) / (1f - lit));
            }
            // An interior shares one "window bay" (uv0.z, uv1.x), so its glow is even rather than banded.
            foreach (var p in new[] { a, b, e, f })
                body.Vertex(p, normal, colour, new Vector4(interior ? lit : glass, smooth, interior ? .5f : p.x + p.z, p.y), new Vector4(interior ? InteriorBay : 0f, 2.5f, 0, 0));
            body.Quad(0, first, first + 1, first + 2, first + 3);
        }
    }
}
