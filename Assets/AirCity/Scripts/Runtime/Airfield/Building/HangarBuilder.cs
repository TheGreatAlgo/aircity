using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.Airfield.Building
{
    /// <summary>Generated steel hangars. Geometry is in the centre doorway's frame: +Z is outside.</summary>
    sealed class HangarBuilder
    {
        const string VisualName = "Generated hangar";
        const string ApproachName = "Generated hangar approach";
        public readonly MeshData mesh = new MeshData(1);
        readonly bool doorsOpen;
        readonly int detail;
        readonly string number;
        readonly HangarSize size;
        readonly HangarSpec spec;
        enum Finish { Plain, Cladding, Roof, Concrete, Door, Lamp, Skylight }
        static Material fallback, detailedMaterial;

        public HangarBuilder(bool doorsOpen = false, int detail = 0, string number = "01", HangarSize size = HangarSize.Small, HangarSpec spec = null)
        {
            this.doorsOpen = doorsOpen; this.detail = detail; this.number = number;
            this.size = size; this.spec = spec ?? HangarSpec.For(size);
        }

        public static Material MaterialFor(Airport airport, HangarSize size)
        {
            if (size != HangarSize.Small)
            {
                if (detailedMaterial == null) detailedMaterial = Resources.Load<Material>("HangarSurface");
                if (detailedMaterial != null) return detailedMaterial;
            }
            if (airport != null && airport.building != null) return airport.building;
            if (fallback == null)
                fallback = new Material(Shader.Find("AirCity/Building")) { name = "Hangar building", hideFlags = HideFlags.DontSave };
            return fallback;
        }

        public static void Rebuild(SmallHangar hangar)
        {
            ClearVisual(hangar.transform.Find(VisualName));
            Build(hangar.transform, hangar.DoorsOpen);
        }

        public static void Clear(SmallHangar hangar)
        {
            ClearVisual(hangar.transform.Find(VisualName));
            ClearVisual(hangar.transform.Find(ApproachName));
        }

        static void ClearVisual(Transform visual)
        {
            if (visual == null) return;
            visual.gameObject.SetActive(false); visual.name = "Retired hangar mesh";
            var meshes = new HashSet<Mesh>();
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
            foreach (var owned in meshes) AirfieldPart.DestroyAny(owned);
            AirfieldPart.DestroyAny(visual.gameObject);
        }

        /// <summary>Only the derived taxiway throat changes when the graph changes.</summary>
        public static void RefreshConnection(SmallHangar hangar)
        {
            ClearVisual(hangar.transform.Find(ApproachName));
            if (!HangarConnection.Approach(hangar,out var outline)) return;
            var art=new HangarBuilder();
            var points=new Vector3[4];
            var across=Geo.Right(hangar.Axis);
            for(int i=0;i<4;i++)
            {
                var d=outline[i]-hangar.Door;
                points[i]=new Vector3(Vector2.Dot(d,across),i<2?SmallHangar.FloorHeight:AirfieldHeights.Stand,Vector2.Dot(d,hangar.Axis));
            }
            var finish = hangar.Size == HangarSize.Small ? Finish.Plain : Finish.Concrete;
            art.Quad(points[0],points[3],points[2],points[1],Concrete,.10f,finish);
            for(int i=0;i<4;i++)
            {
                int next=(i+1)%4;
                var lowA=points[i];lowA.y=-.02f;
                var lowB=points[next];lowB.y=-.02f;
                art.Quad(points[i],points[next],lowB,lowA,Concrete,.10f,finish);
            }
            var spec = hangar.Spec;
            var paint = new Color(.88f,.69f,.21f).linear;
            float line = hangar.Size == HangarSize.Airline ? .16f : hangar.Size == HangarSize.Executive ? .105f : .06f;
            for (int spot = 0; spot < spec.Bays; spot++)
            {
                float x = spec.BayOffset(spot);
                float length = Mathf.Lerp(points[3].z, points[2].z, x / spec.ApproachWidth + .5f);
                float end = Mathf.Max(.3f, length - .35f);
                float Y(float z) => Mathf.Lerp(SmallHangar.FloorHeight, AirfieldHeights.Stand, z / Mathf.Max(.1f, length)) + .012f;
                art.Quad(new Vector3(x-line,Y(.2f),.2f),new Vector3(x-line,Y(end),end),
                    new Vector3(x+line,Y(end),end),new Vector3(x+line,Y(.2f),.2f),paint,.15f);
            }
            var go=new GameObject(ApproachName){hideFlags=HideFlags.DontSave};go.transform.SetParent(hangar.transform,false);
            go.layer=hangar.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh=art.mesh.Apply(null,"Hangar concrete approach");
            var renderer=go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=MaterialFor(hangar.Airport, hangar.Size);
            renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
        static readonly Color Cladding = new Color(.73f, .74f, .71f).linear;
        static readonly Color Rib = new Color(.67f, .69f, .67f).linear;
        static readonly Color Trim = new Color(.39f, .43f, .44f).linear;
        static readonly Color Roof = new Color(.24f, .29f, .32f).linear;
        static readonly Color Blue = new Color(.29f, .43f, .51f).linear;
        static readonly Color Frame = new Color(.48f, .51f, .51f).linear;
        static readonly Color Concrete = new Color(.52f, .53f, .50f).linear;

        /// <summary>Independent shell for review scenes or previews; never creates a saved layout component.</summary>
        public static GameObject Build(Transform parent, bool doorsOpen, string label = null)
        {
            var root = new GameObject(VisualName) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, false);
            var airport = parent.GetComponentInParent<Airport>();
            var id = NumberOf(label ?? parent.name);
            var hangar = parent.GetComponent<SmallHangar>();
            var size = hangar != null ? hangar.Size : HangarSize.Small;
            var spec = hangar != null ? hangar.Spec : HangarSpec.For(size);
            var material = MaterialFor(airport, size);
            // Fine corrugation must not cast hundreds of subpixel self-shadows. A slightly inset simple
            // shell casts the building's silhouette while the detailed visible mesh receives world shadows.
            var shadowArt = new HangarBuilder(doorsOpen, 2, id, size, spec); shadowArt.Build();
            var centre = new Vector3(0, 0, -spec.Depth * .5f);
            for (int vertex = 0; vertex < shadowArt.mesh.Count; vertex++)
                shadowArt.mesh.positions[vertex] = centre + (shadowArt.mesh.positions[vertex] - centre) * .994f;
            var shadowMesh = shadowArt.mesh.Apply(null, "Hangar shadow shell");
            var levels = new LOD[3];
            for (int i = 0; i < levels.Length; i++)
            {
                var art = new HangarBuilder(doorsOpen, i, id, size, spec); art.Build();
                var go = new GameObject("Hangar LOD" + i) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = art.mesh.Apply(null, "Hangar LOD" + i);
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                var shadow = new GameObject("Hangar shadow LOD" + i) { hideFlags = HideFlags.DontSave };
                shadow.transform.SetParent(root.transform, false);
                shadow.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
                var caster = shadow.AddComponent<MeshRenderer>(); caster.sharedMaterial = material;
                caster.shadowCastingMode = ShadowCastingMode.ShadowsOnly; caster.receiveShadows = false;
                // PC uses a 2x LOD bias; retire fine metal ribs before they become subpixel patterns.
                float near = size == HangarSize.Small ? .70f : .38f;
                levels[i] = new LOD(i == 0 ? near : i == 1 ? .10f : .002f, new Renderer[] { renderer, caster });
            }
            var group = root.AddComponent<LODGroup>(); group.SetLODs(levels); group.RecalculateBounds();
            if (hangar != null && size != HangarSize.Small)
                hangar.SetInteriorLights(AddInteriorLights(root.transform, spec));
            foreach (var child in root.GetComponentsInChildren<Transform>()) child.gameObject.layer = parent.gameObject.layer;
            return root;
        }

        public void Build()
        {
            if (size != HangarSize.Small)
            {
                BuildExpanded();
                return;
            }
            float w = SmallHangar.Width * .5f, d = SmallHangar.Depth;
            float h = SmallHangar.EaveHeight, ridge = SmallHangar.RidgeHeight;
            float door = SmallHangar.DoorWidth * .5f;
            // A shallow slab clears the airport's generated grass without leaving the aircraft floating.
            Box(new Vector3(0f, SmallHangar.FloorHeight - .12f, -d * .5f), new Vector3(w * 2f + .24f, .24f, d + .24f), Concrete, .10f);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(new Vector3(side * (w - .07f), h * .5f, -d * .5f), new Vector3(.14f, h, d), Cladding);
                Box(new Vector3(side * (w + .005f), .20f, -d * .5f), new Vector3(.19f, .40f, d), Trim);
                Box(new Vector3(side * (door + (w - door) * .5f), h * .5f, -.055f),
                    new Vector3(w - door, h, .15f), Cladding);
                Box(new Vector3(side * (door + (w - door) * .5f), .20f, .035f),
                    new Vector3(w - door, .40f, .08f), Trim);
            }
            Box(new Vector3(0, h * .5f, -d + .07f), new Vector3(w * 2f, h, .14f), Cladding);
            Box(new Vector3(0, .20f, -d - .005f), new Vector3(w * 2f, .40f, .19f), Trim);
            Box(new Vector3(0, (SmallHangar.DoorHeight + h) * .5f, -.055f),
                new Vector3(door * 2f, h - SmallHangar.DoorHeight, .15f), Cladding);
            Gable(0.025f, true, Cladding); Gable(-.14f, false, Cladding * .85f);
            Gable(-d - .025f, false, Cladding); Gable(-d + .14f, true, Cladding * .85f);

            // Two closed roof skins with a ridge cap and expressed bargeboards.
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 a = new Vector3(0, ridge, .29f), b = new Vector3(side * (w + .28f), h - .045f, .29f);
                Vector3 c = b + Vector3.back * (d + .58f), e = a + Vector3.back * (d + .58f);
                if (side > 0) Quad(a, b, c, e, Roof, .28f); else Quad(a, e, c, b, Roof, .28f);
                if (side > 0) Quad(a + Vector3.down * .1f, e + Vector3.down * .1f, c + Vector3.down * .1f, b + Vector3.down * .1f, Frame, .1f);
                else Quad(a + Vector3.down * .1f, b + Vector3.down * .1f, c + Vector3.down * .1f, e + Vector3.down * .1f, Frame, .1f);
                Beam(a + Vector3.back * .09f, b + Vector3.back * .09f, .13f, .16f, Trim);
                Beam(e + Vector3.forward * .09f, c + Vector3.forward * .09f, .13f, .16f, Trim);
                Box(new Vector3(side * (w + .20f), h - .10f, -d * .5f), new Vector3(.18f, .19f, d + .58f), Trim);
            }
            Box(new Vector3(0, ridge + .035f, -d * .5f), new Vector3(.25f, .10f, d + .58f), Trim);

            Doors();
            if (detail < 2)
            {
                if (detail == 0) WallRibs(w, d, h, door);
                RoofSeams(w, d, h, ridge);
                PersonnelDoor(w, d);
                Vent(new Vector3(0, 5.65f, .07f), 1.6f, .42f);
                NumberPlate();
                foreach (float side in new[] { -1f, 1f })
                {
                    Box(new Vector3(side * 7.38f, 4.89f, .11f), new Vector3(.50f, .16f, .30f), Roof);
                    Box(new Vector3(side * 7.38f, 4.82f, .20f), new Vector3(.36f, .025f, .15f), new Color(.87f, .84f, .68f).linear);
                    // Downpipes follow the rear corners and turn toward the ground.
                    Box(new Vector3(side * (w + .12f), 2.43f, -d + .34f), new Vector3(.085f, 4.8f, .085f), Trim);
                    Beam(new Vector3(side * (w + .12f), .12f, -d + .34f), new Vector3(side * (w + .25f), .08f, -d + .34f), .07f, .07f, Trim);
                }
                PortalFrames(w, d, h, ridge);
            }
            if (detail == 0) Interior(d, h);
        }

        void BuildExpanded()
        {
            bool airline = size == HangarSize.Airline;
            float half = spec.Width * .5f, depth = spec.Depth, eave = spec.Eave;
            var walls = (airline ? new Color(.92f, .93f, .94f) : new Color(.91f, .90f, .87f)).linear;
            var doors = (airline ? new Color(.81f, .84f, .86f) : new Color(.82f, .83f, .81f)).linear;
            var roof = (airline ? new Color(.56f, .59f, .62f) : new Color(.53f, .57f, .59f)).linear;
            var steel = new Color(.58f, .62f, .64f).linear;
            var skirt = new Color(.62f, .66f, .68f).linear;
            Box(new Vector3(0, SmallHangar.FloorHeight - .21f, -depth * .5f), new Vector3(spec.Width, .42f, depth), Concrete, .14f,
                finish: Finish.Concrete, litFace: Vector3.up);
            Box(new Vector3(0, eave * .5f, -depth + .12f), new Vector3(spec.Width, eave, .24f), walls,
                finish: Finish.Cladding, litFace: Vector3.forward);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(new Vector3(side * (half - .12f), eave * .5f, -depth * .5f), new Vector3(.24f, eave, depth), walls,
                    finish: Finish.Cladding, litFace: Vector3.left * side);
                Box(new Vector3(side * (half - .04f), .65f, -depth * .5f), new Vector3(.16f, 1.3f, depth), skirt, finish: Finish.Cladding);
                Box(new Vector3(side * (half + .18f), eave, -depth * .5f), new Vector3(.18f, .34f, depth + .52f), steel);
            }
            if (detail < 2) PersonnelDoor(half, depth);

            float opening = spec.DoorWidth;
            float pier = (spec.Width - opening) * .5f;
            float fascia = eave - spec.DoorHeight;
            Box(new Vector3(0, spec.DoorHeight + fascia * .5f, .03f), new Vector3(spec.Width, fascia, .29f), doors, finish: Finish.Cladding);
            foreach (float side in new[] { -1f, 1f })
            {
                Box(new Vector3(side * (opening + pier) * .5f, spec.DoorHeight * .5f, -.12f),
                    new Vector3(pier, spec.DoorHeight, .3f), walls, finish: Finish.Cladding);
                Box(new Vector3(side * (opening * .5f + .16f), spec.DoorHeight * .5f, .1f),
                    new Vector3(.31f, spec.DoorHeight, .25f), steel);
            }
            float leafHeight = doorsOpen ? fascia * .55f : spec.DoorHeight - SmallHangar.FloorHeight;
            float baseHeight = doorsOpen ? spec.DoorHeight : SmallHangar.FloorHeight;
            Box(new Vector3(0, baseHeight + leafHeight * .5f, .23f), new Vector3(opening, leafHeight, .10f), doors, .39f, finish: Finish.Door);
            if (detail < 2)
            {
                int bars = doorsOpen ? 2 : airline ? 8 : 4;
                for (int bar = 0; bar <= bars; bar++)
                    Box(new Vector3(0, baseHeight + leafHeight * bar / bars, .286f), new Vector3(opening, .105f, .026f), steel);
                NumberPlate(new Vector3(0, eave - fascia * .16f, .19f), number);
            }

            float RoofY(float x) => eave + (spec.Ridge - eave) *
                (airline ? 1f - x * x / (half * half) : 1f - Mathf.Abs(x) / half);
            int panels = airline ? (detail == 2 ? 6 : 12) : 2;
            for (int i = 0; i < panels; i++)
            {
                float left = Mathf.Lerp(-half, half, (float)i / panels);
                float right = Mathf.Lerp(-half, half, (float)(i + 1) / panels);
                float edge = i == 0 ? -.26f : 0f;
                var a = new Vector3(left + edge, RoofY(left), .28f);
                var b = new Vector3(right + (i == panels - 1 ? .26f : 0f), RoofY(right), .28f);
                var c = b + Vector3.back * (depth + .56f);
                var d = a + Vector3.back * (depth + .56f);
                bool litStrip = detail < 2 && (i == panels / 4 || i == panels - 1 - panels / 4);
                ExpandedRoofPanel(a, b, d.z, roof, steel, litStrip ? airline ? 7 : 4 : 0, airline);
                float capBase = eave - .12f;
                Quad(new Vector3(left, capBase, .12f), new Vector3(right, capBase, .12f),
                    new Vector3(right, b.y, .12f), new Vector3(left, a.y, .12f), walls, .25f, Finish.Cladding);
                Quad(new Vector3(right, capBase, -depth + .05f), new Vector3(left, capBase, -depth + .05f),
                    new Vector3(left, a.y, -depth + .05f), new Vector3(right, b.y, -depth + .05f), walls, .25f, Finish.Cladding);
                var interior = walls * .83f;
                Quad(new Vector3(right, capBase, -.12f), new Vector3(left, capBase, -.12f),
                    new Vector3(left, a.y - .1f, -.12f), new Vector3(right, b.y - .1f, -.12f), interior, .25f, Finish.Cladding, true);
                Quad(new Vector3(left, capBase, -depth + .25f), new Vector3(right, capBase, -depth + .25f),
                    new Vector3(right, b.y - .1f, -depth + .25f), new Vector3(left, a.y - .1f, -depth + .25f),
                    interior, .25f, Finish.Cladding, true);
                if (detail < 2)
                {
                    Beam(a + Vector3.back * .05f, b + Vector3.back * .05f, .10f, .18f, steel);
                    Beam(d + Vector3.forward * .05f, c + Vector3.forward * .05f, .10f, .18f, steel);
                }
            }
            if (!airline) ExecutiveMarkings();
            foreach (var at in InteriorLightPositions(spec))
                DrawInteriorLamp(at, RoofY(at.x), airline, steel);
            if (detail == 2) return;

            int sections = airline ? 9 : 5;
            var glass = new Color(.46f, .54f, .58f).linear;
            for (int i = 1; i < sections; i++)
            {
                float z = -depth * i / sections;
                foreach (float side in new[] { -1f, 1f })
                {
                    Box(new Vector3(side * (half - .19f), eave * .5f, z), new Vector3(.36f, eave, .24f), Frame);
                    Box(new Vector3(side * (half + .04f), eave - (airline ? 3f : 1.6f), z),
                        new Vector3(.10f, airline ? 1.4f : .65f, depth / sections * .77f), glass, .5f, finish: Finish.Skylight);
                }
                if (detail == 0)
                    Box(new Vector3(0, eave - .32f, z), new Vector3(spec.Width - .48f, .22f, .20f), Frame);
            }
        }

        // Split the roof skin around the glazing. All flashing, panes and mullions follow the same facet as the roof.
        void ExpandedRoofPanel(Vector3 a, Vector3 b, float back, Color roof, Color frame, int lights, bool airline)
        {
            float front = a.z;
            var glass = new Color(.44f, .54f, .61f).linear;
            void Panel(float left, float right, float start, float end, Finish finish)
            {
                Vector3 P(float x, float z) => new Vector3(x, Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x)), z);
                var p = P(left, start); var q = P(right, start); var r = P(right, end); var s = P(left, end);
                var offset = Vector3.down * .11f;
                Color top = finish == Finish.Roof ? roof : finish == Finish.Skylight ? glass : frame;
                Color bottom = finish == Finish.Roof ? Frame : top;
                Quad(p, q, r, s, top, finish == Finish.Skylight ? .38f : .24f, finish);
                Quad(p + offset, s + offset, r + offset, q + offset, bottom, .16f,
                    finish == Finish.Skylight ? finish : Finish.Plain, true);
            }
            if (lights == 0) { Panel(a.x, b.x, front, back, Finish.Roof); return; }

            float centre = Mathf.Lerp(a.x, b.x, airline ? .5f : .48f);
            float half = Mathf.Min((b.x - a.x) * .39f, airline ? 2.85f : 4.15f);
            float left = centre - half, right = centre + half;
            Panel(a.x, left, front, back, Finish.Roof);
            Panel(right, b.x, front, back, Finish.Roof);
            float end = front;
            const float trim = .11f;
            for (int i = 1; i <= lights; i++)
            {
                float z = -spec.Depth * i / (lights + 1);
                float near = z + (airline ? 1.06f : .76f), far = 2f * z - near;
                Panel(left, right, end, near, Finish.Roof);
                Panel(left, right, near, near - trim, Finish.Plain);
                Panel(left, right, far + trim, far, Finish.Plain);
                Panel(left, left + trim, near - trim, far + trim, Finish.Plain);
                Panel(right - trim, right, near - trim, far + trim, Finish.Plain);
                Panel(centre - .035f, centre + .035f, near - trim, far + trim, Finish.Plain);
                Panel(left + trim, centre - .035f, near - trim, far + trim, Finish.Skylight);
                Panel(centre + .035f, right - trim, near - trim, far + trim, Finish.Skylight);
                end = far;
            }
            Panel(left, right, end, back, Finish.Roof);
        }

        const int InteriorLightColumns = 3;
        const int InteriorLightRows = 4;

        static IEnumerable<Vector3> InteriorLightPositions(HangarSpec spec)
        {
            float spacing = spec.Bays > 1 ? spec.BayWidth : spec.Width * .23f;
            for (int row = 0; row < InteriorLightRows; row++)
            {
                float depthFraction = Mathf.Lerp(.18f, .82f, (float)row / (InteriorLightRows - 1));
                for (int column = 0; column < InteriorLightColumns; column++)
                    yield return new Vector3((column - InteriorLightColumns / 2) * spacing, spec.Eave - .67f,
                        -spec.Depth * depthFraction);
            }
        }

        static Light[] AddInteriorLights(Transform root, HangarSpec spec)
        {
            var lights = new List<Light>(InteriorLightRows * InteriorLightColumns);
            foreach (var at in InteriorLightPositions(spec))
            {
                var fixture = new GameObject("Hangar high bay light") { hideFlags = HideFlags.DontSave };
                fixture.transform.SetParent(root, false);
                fixture.transform.SetLocalPositionAndRotation(at, Quaternion.Euler(90, 0, 0));
                var lamp = fixture.AddComponent<Light>();
                float floorHeight = at.y - SmallHangar.FloorHeight;
                float endMargin = Mathf.Min(-at.z, spec.Depth + at.z);
                float margin = Mathf.Min(spec.Width * .5f - Mathf.Abs(at.x), endMargin);
                float radius = margin - .5f;
                lamp.type = LightType.Spot;
                lamp.range = Mathf.Sqrt(floorHeight * floorHeight + radius * radius) + 3f;
                lamp.spotAngle = 2f * Mathf.Atan(radius / floorHeight) * Mathf.Rad2Deg;
                lamp.innerSpotAngle = lamp.spotAngle * .47f;
                lamp.color = new Color(1f, .965f, .91f);
                lamp.intensity = 2.65f * floorHeight * floorHeight;
                lamp.shadows = LightShadows.None;
                lamp.enabled = false;
                lights.Add(lamp);
            }
            return lights.ToArray();
        }

        void DrawInteriorLamp(Vector3 at, float roofHeight, bool airline, Color frame)
        {
            // Bake the housing and diffuser into the shell mesh. Only the downward spot is a light component.
            float length = airline ? 3.8f : 2.9f;
            float width = airline ? 1.12f : .74f;
            Box(at + Vector3.up * .13f, new Vector3(width, .26f, length), frame);
            if (detail < 2)
            {
                float drop = roofHeight - at.y - .26f;
                foreach (float end in new[] { -1f, 1f })
                    Box(new Vector3(at.x, at.y + .20f + drop * .5f, at.z + end * length * .34f),
                        new Vector3(.055f, drop, .055f), frame * .66f);
            }
            float x = width * .43f, z = length * .45f;
            at.y -= .007f;
            Quad(at + new Vector3(-x, 0, -z), at + new Vector3(x, 0, -z), at + new Vector3(x, 0, z),
                at + new Vector3(-x, 0, z), new Color(.88f, .88f, .84f).linear, .32f, Finish.Lamp);
        }

        void ExecutiveMarkings()
        {
            var yellow = new Color(.83f, .64f, .18f).linear;
            const float y = SmallHangar.FloorHeight + .008f;
            void Stripe(float left, float right, float back, float front) =>
                Quad(new Vector3(left,y,front), new Vector3(right,y,front), new Vector3(right,y,back),
                    new Vector3(left,y,back), yellow, .12f, interior: true);
            for (int spot = 0; spot < spec.Bays; spot++)
            {
                float x = spec.BayOffset(spot);
                float stop = -spec.Depth + 2.6f;
                Stripe(x-.10f, x+.10f, stop+3f, -.08f);
                Stripe(x-2.1f, x+2.1f, stop+2.82f, stop+3f);
                foreach (float side in new[] { -1f, 1f })
                {
                    float edge = x + side * 12.8f;
                    Stripe(edge-.09f, edge+.09f, stop, -2f);
                    Stripe(Mathf.Min(edge, edge-side*2f), Mathf.Max(edge, edge-side*2f), stop, stop+.18f);
                }
            }
        }

        void Doors()
        {
            float half = SmallHangar.DoorWidth * .5f, leaf = half / 4f, height = SmallHangar.DoorHeight;
            // Four leaves per side telescope into the two-metre cheeks, with separate parallel tracks.
            foreach (float side in new[] { -1f, 1f })
                for (int panel = 0; panel < 4; panel++)
                {
                    float x = side * (doorsOpen ? half + leaf * .5f : leaf * (panel + .5f));
                    float z = .04f + panel * .065f;
                    Color colour = Blue * (1f - panel * .018f);
                    Box(new Vector3(x, height * .5f + .035f, z), new Vector3(leaf + .025f, height - .04f, .055f), colour);
                    if (detail < 2)
                    {
                        Box(new Vector3(x - leaf * .5f + .035f, height * .5f, z + .035f), new Vector3(.055f, height, .035f), Trim);
                        Box(new Vector3(x, .12f, z + .035f), new Vector3(leaf, .10f, .045f), Trim);
                        if (detail == 0)
                            for (float rib = -leaf * .5f + .19f; rib < leaf * .5f; rib += .22f)
                                Box(new Vector3(x + rib, height * .5f, z + .037f), new Vector3(.025f, height - .19f, .022f), colour * .88f);
                    }
                }
            Box(new Vector3(0, height + .10f, .10f), new Vector3(SmallHangar.Width, .20f, .36f), Trim);
            Box(new Vector3(0, .015f, .11f), new Vector3(SmallHangar.Width, .025f, .34f), Trim, .25f);
            foreach (float side in new[] { -1f, 1f })
                Box(new Vector3(side * (half + .055f), height * .5f, -.035f), new Vector3(.12f, height, .24f), Trim);
        }

        void WallRibs(float w, float d, float h, float door)
        {
            float pitch = detail == 0 ? .30f : .60f;
            foreach (float side in new[] { -1f, 1f })
            {
                for (float z = -.15f; z > -d + .05f; z -= pitch)
                    Box(new Vector3(side * (w + .015f), (h + .42f) * .5f, z), new Vector3(.055f, h - .42f, .10f), Rib);
                Box(new Vector3(side * (w - .055f), h * .5f, .025f), new Vector3(.15f, h, .17f), Trim);
                Box(new Vector3(side * (w - .055f), h * .5f, -d - .025f), new Vector3(.15f, h, .17f), Trim);
            }
            for (float x = -w + .15f; x < w - .05f; x += pitch)
            {
                Box(new Vector3(x, (h + .42f) * .5f, -d - .015f), new Vector3(.10f, h - .42f, .055f), Rib);
                float bottom = Mathf.Abs(x) > door ? .42f : SmallHangar.DoorHeight;
                float top = Mathf.Lerp(SmallHangar.RidgeHeight, h, Mathf.Abs(x) / w);
                Box(new Vector3(x, (bottom + top) * .5f, .035f), new Vector3(.10f, top - bottom, .055f), Rib);
            }
        }

        void RoofSeams(float w, float d, float h, float ridge)
        {
            if (detail == 0)
                foreach (float side in new[] { -1f, 1f })
                    for (float z = -.07f; z > -d - .15f; z -= .90f)
                    {
                        if (Mathf.Abs(z + 5f) < .55f || Mathf.Abs(z + 11f) < .55f) continue;
                        Beam(new Vector3(0, ridge + .085f, z), new Vector3(side * (w + .26f), h + .055f, z), .075f, .10f, Roof * 1.10f);
                    }
            // Two rooflights on each slope. Pale opaque glazing keeps the exterior inexpensive to render.
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in new[] { -5.0f, -11.0f })
                {
                    var a = new Vector3(side * 2.8f, Mathf.Lerp(ridge, h, 2.8f / w) + .08f, z + .45f);
                    var b = new Vector3(side * 6.5f, Mathf.Lerp(ridge, h, 6.5f / w) + .08f, z + .45f);
                    var c = b + Vector3.back * .9f; var e = a + Vector3.back * .9f;
                    var glass = new Color(.53f, .62f, .64f).linear;
                    if (side > 0) Quad(a, b, c, e, glass, .52f); else Quad(a, e, c, b, glass, .52f);
                    if (detail == 0) { Beam(a, b, .06f, .08f, Frame); Beam(e, c, .06f, .08f, Frame); }
                }
        }

        void PortalFrames(float w, float d, float h, float ridge)
        {
            foreach (float z in new[] { -.38f, -5.3f, -10.6f, -d + .38f })
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Box(new Vector3(side * (w - .25f), h * .5f, z), new Vector3(.21f, h, .19f), Frame);
                    Beam(new Vector3(side * (w - .25f), h - .1f, z), new Vector3(0, ridge - .15f, z), .17f, .20f, Frame);
                    Beam(new Vector3(side * (w - .25f), h - 1.1f, z), new Vector3(side * (w - 1.5f), h + .1f, z), .10f, .10f, Frame);
                }
                Box(new Vector3(0, h - .16f, z), new Vector3(w * 2f - .5f, .10f, .10f), Frame);
            }
        }

        void Interior(float d, float h)
        {
            var joint = Concrete * .76f;
            for (float z = -4f; z > -d; z -= 4f)
                Box(new Vector3(0, SmallHangar.FloorHeight + .004f, z), new Vector3(SmallHangar.Width - .3f, .006f, .014f), joint, .1f);
            for (float x = -6f; x <= 6f; x += 6f)
                Box(new Vector3(x, SmallHangar.FloorHeight + .004f, -d * .5f), new Vector3(.014f, .006f, d - .2f), joint, .1f);
            // Service bench and storage remain clear of the central aircraft envelope.
            Box(new Vector3(6.6f, .44f, -14.8f), new Vector3(3.3f, .88f, .64f), Blue);
            Box(new Vector3(6.6f, .91f, -14.8f), new Vector3(3.45f, .09f, .76f), Frame);
            Box(new Vector3(-7.5f, .78f, -14.9f), new Vector3(1.4f, 1.56f, .65f), Frame * .7f);
            for (float z = -2f; z > -d; z -= 3.5f)
                foreach (float side in new[] { -1f, 1f })
                    Beam(new Vector3(side * 4.5f, h + .60f, z), new Vector3(side * 4.5f, h + .60f, Mathf.Max(z - 3.5f, -d)), .07f, .10f, Frame);
        }

        void PersonnelDoor(float w, float d)
        {
            // Flush personnel door on the right-hand side wall.
            Box(new Vector3(w + .047f, 1.10f, -3.1f), new Vector3(.08f, 2.18f, 1.03f), Roof);
            Box(new Vector3(w + .093f, 1.10f, -3.1f), new Vector3(.025f, 2.04f, .91f), Blue);
            Box(new Vector3(w + .13f, 1.05f, -2.80f), new Vector3(.09f, .035f, .15f), Frame);
            Box(new Vector3(w + .11f, 2.24f, -3.1f), new Vector3(.34f, .065f, 1.24f), Roof);
            Box(new Vector3(w + .065f, 1.00f, -4.0f), new Vector3(.15f, .46f, .19f), new Color(.58f, .20f, .15f).linear);
        }

        void Vent(Vector3 centre, float width, float height)
        {
            Box(centre, new Vector3(width, height, .06f), Roof);
            if (detail != 0) return;
            for (float y = -height * .5f + .05f; y < height * .5f; y += .075f)
                Box(centre + new Vector3(0, y, .04f), new Vector3(width - .08f, .025f, .04f), Frame);
        }

        void Gable(float z, bool forward, Color colour)
        {
            var a = new Vector3(-SmallHangar.Width * .5f, SmallHangar.EaveHeight, z);
            var b = new Vector3(SmallHangar.Width * .5f, SmallHangar.EaveHeight, z);
            var c = new Vector3(0, SmallHangar.RidgeHeight, z);
            if (forward) Triangle(a, b, c, colour); else Triangle(a, c, b, colour);
        }

        static string NumberOf(string label)
        {
            string digits = "";
            foreach (char c in label) if (c >= '0' && c <= '9') digits += c;
            if (digits.Length == 0) return "01";
            return digits.Length == 1 ? "0" + digits : digits;
        }

        void NumberPlate() => NumberPlate(new Vector3(0, 5.09f, .16f), number);

        void NumberPlate(Vector3 at, string text)
        {
            float pitch = .30f;
            Box(at + Vector3.back * .04f, new Vector3(Mathf.Max(.95f, text.Length * pitch + .35f), .56f, .06f), Roof);
            int[] masks = { 63, 6, 91, 79, 102, 109, 125, 7, 127, 111 };
            var colour = new Color(.88f, .90f, .87f).linear;
            for (int i = 0; i < text.Length; i++)
            {
                int digit = text[i] - '0'; if (digit < 0 || digit > 9) continue;
                // A viewer outside the +Z doorway sees local -X on their right.
                var c = at + Vector3.left * ((i - (text.Length - 1) * .5f) * pitch);
                for (int segment = 0; segment < 7; segment++)
                {
                    if ((masks[digit] & (1 << segment)) == 0) continue;
                    bool horizontal = segment == 0 || segment == 3 || segment == 6;
                    float x = segment == 1 || segment == 2 ? .10f : segment == 4 || segment == 5 ? -.10f : 0;
                    float y = segment == 0 ? .18f : segment == 3 ? -.18f : segment == 6 ? 0 : segment == 1 || segment == 5 ? .09f : -.09f;
                    Box(c + new Vector3(-x, y, 0), horizontal ? new Vector3(.20f, .035f, .008f) : new Vector3(.035f, .18f, .008f), colour);
                }
            }
        }

        void Beam(Vector3 a, Vector3 b, float width, float depth, Color colour)
        {
            var axis = b - a;
            if (axis.sqrMagnitude < .00001f) return;
            Box((a + b) * .5f, new Vector3(width, axis.magnitude, depth), colour, .25f,
                Quaternion.FromToRotation(Vector3.up, axis.normalized));
        }

        void Box(Vector3 centre, Vector3 size, Color colour, float smoothness = .25f, Quaternion rotation = default,
            Finish finish = Finish.Plain, Vector3 litFace = default)
        {
            if (rotation == default) rotation = Quaternion.identity;
            Vector3 P(float x, float y, float z) => centre + rotation * Vector3.Scale(new Vector3(x, y, z), size * .5f);
            Quad(P(-1,-1,1), P(1,-1,1), P(1,1,1), P(-1,1,1), colour, smoothness, finish, litFace.z > 0f);
            Quad(P(1,-1,-1), P(-1,-1,-1), P(-1,1,-1), P(1,1,-1), colour, smoothness, finish, litFace.z < 0f);
            Quad(P(1,-1,1), P(1,-1,-1), P(1,1,-1), P(1,1,1), colour, smoothness, finish, litFace.x > 0f);
            Quad(P(-1,-1,-1), P(-1,-1,1), P(-1,1,1), P(-1,1,-1), colour, smoothness, finish, litFace.x < 0f);
            Quad(P(-1,1,1), P(1,1,1), P(1,1,-1), P(-1,1,-1), colour, smoothness, finish, litFace.y > 0f);
            Quad(P(-1,-1,-1), P(1,-1,-1), P(1,-1,1), P(-1,-1,1), colour, smoothness, finish, litFace.y < 0f);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, float smoothness = .25f,
            Finish finish = Finish.Plain, bool interior = false)
        {
            var n = Vector3.Cross(b - a, c - a).normalized; int start = mesh.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                float across = Mathf.Abs(n.x) > .5f ? p.z : p.x;
                var uv = finish switch
                {
                    Finish.Cladding => new Vector4(across / 7.2f, p.y / 6.6f, (int)finish, p.y),
                    Finish.Roof => new Vector4(p.z / 8.4f, p.x / 18f, (int)finish, 0),
                    Finish.Concrete => new Vector4(p.x / 16f, p.z / 16f, (int)finish, 0),
                    Finish.Door => new Vector4(across / 12f, p.y / 5.4f, (int)finish, p.y),
                    Finish.Lamp or Finish.Skylight => new Vector4(0, 0, (int)finish, 0),
                    _ => Vector4.zero
                };
                mesh.Vertex(p, n, colour, new Vector4(0f, smoothness, p.x + p.z, interior ? 1f : 0f), uv);
            }
            mesh.Quad(0, start, start + 1, start + 2, start + 3);
        }

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Color colour)
        {
            var n = Vector3.Cross(b - a, c - a).normalized; int start = mesh.Count;
            foreach (var p in new[] { a, b, c }) mesh.Vertex(p, n, colour, new Vector4(0, .25f, p.x, p.y));
            mesh.Triangle(0, start, start + 1, start + 2);
        }
    }
}
