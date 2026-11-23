using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Game;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    public partial class DevAirfieldTool
    {
        RoadBuildSettings roadBuildSettings=RoadBuildSettings.Default;
        float roadElevation;
        float? roadDatum;
        float? undergroundDatum;
        Vector2? lastRoadPointer;
        bool roadUnderground;
        Pick undergroundTunnel;
        int undergroundTunnelNode = -1;
        public float RoadElevation=>roadElevation;
        public bool RoadUnderground=>roadUnderground;
        public RoadBridgeDesign NewBridgeDesign=>roadBuildSettings.bridgeDesign;
        public RoadBridgeDesign? SelectedBridgeDesign=>SelectedPublicRoad?.structure==RoadStructure.Elevated
            ? SelectedPublicRoad.bridgeDesign : SelectedEdge?.IsRoad==true&&SelectedEdge.structure==RoadStructure.Elevated
                ? SelectedEdge.bridgeDesign : (RoadBridgeDesign?)null;

        public void SetRoadBridgeDesign(RoadBridgeDesign design)
        {
            if(roadBuildSettings.bridgeDesign==design)return;
            roadBuildSettings.bridgeDesign=design;
            InvalidateRoadPlan();
        }

        public void SetSelectedBridgeDesign(RoadBridgeDesign design)
        {
            if(!SelectedBridgeDesign.HasValue||!Valid(selection))return;
            if(SelectedPublicRoad is PublicRoadSegment street)
            {
                var view=Streets;
                var draft=view.network.Clone();
                if(!PublicRoadGeometry.Layout(draft).SetBridgeDesign(new[]{street.id},design))return;
                view.network=draft;
                selection.publicRoad=draft.Segment(street.id);
                view.Rebuild();
            }
            else
            {
                var net=airport.Taxiways;
                if(!new TaxiwayBuilder(net,airport.Runways).SetBridgeDesign(new HashSet<int>{SelectedEdge.id},design))return;
                net.Rebuild();
                game?.SharedConstructionChanged();
            }
            InvalidateRoadPlan();
        }

        public TunnelPortalStyle NewTunnelPortalStyle => roadBuildSettings.tunnelPortalStyle;
        public TunnelPortalStyle? SelectedTunnelPortalStyle
        {
            get
            {
                if (SelectedPublicRoad?.structure == RoadStructure.Tunnel) return SelectedPublicRoad.tunnelPortalStyle;
                if (SelectedEdge?.IsRoad == true && SelectedEdge.structure == RoadStructure.Tunnel) return SelectedEdge.tunnelPortalStyle;
                return null;
            }
        }

        public void SetRoadTunnelPortalStyle(TunnelPortalStyle style)
        {
            if (roadBuildSettings.tunnelPortalStyle == style) return;
            roadBuildSettings.tunnelPortalStyle = style;
            InvalidateRoadPlan();
        }

        public void SetSelectedTunnelPortalStyle(TunnelPortalStyle style)
        {
            if (!SelectedTunnelPortalStyle.HasValue || !Valid(selection)) return;
            if (SelectedPublicRoad is PublicRoadSegment street)
            {
                var view = Streets;
                var draft = view.network.Clone();
                var section = draft.Segment(street.id);
                if (!PublicRoadGeometry.Layout(draft).SetTunnelPortalStyle(section, style)) return;
                view.network = draft;
                selection.publicRoad = section;
                view.Rebuild();
            }
            else
            {
                var net = airport.Taxiways;
                if (!new TaxiwayBuilder(net, airport.Runways).SetTunnelPortalStyle(SelectedEdge, style)) return;
                net.Rebuild();
                game?.SharedConstructionChanged();
            }
            InvalidateRoadPlan();
        }

        public string SelectedBridgeDesignNotice
        {
            get
            {
                var requested=SelectedBridgeDesign;
                if(!requested.HasValue)return null;
                if(SelectedPublicRoad is PublicRoadSegment street)
                {
                    var geometry=game!=null?game.valley.RoadGeometry:Streets.Geometry;
                    return geometry!=null?BridgeDesignNotice(requested.Value,geometry.BridgeFor(street).Design):null;
                }
                return BridgeDesignNotice(requested.Value,airport.Taxiways.BridgeDesignFor(SelectedEdge.id));
            }
        }

        public string PreviewBridgeDesignNotice
        {
            get
            {
                var requested=NewBridgeDesign;
                if(!RoadDrawing||requested==RoadBridgeDesign.Automatic||requested==RoadBridgeDesign.Piers)return null;
                if(tool==DevTool.Street)
                {
                    var geometry=streetPreview?.Geometry;
                    if(streetPlan==null||geometry==null||geometry.graph!=streetPlan.Draft)return null;
                    if(!streetPlan.NewSegments.Select(geometry.graph.Segment).Any(e=>e.structure==RoadStructure.Elevated&&
                        geometry.BridgeFor(e).Design!=requested))return null;
                }
                else if(airsideTerrain?.UsesDifferentBridgeDesign(requested)!=true)return null;
                return BridgeDesignNotice(requested,RoadBridgeDesign.Piers);
            }
        }

        static string BridgeDesignNotice(RoadBridgeDesign requested,RoadBridgeDesign actual)
        {
            if(requested==RoadBridgeDesign.Automatic)return "Uses "+(actual==RoadBridgeDesign.Piers?"piers":actual.ToString().ToLowerInvariant());
            if(actual==requested)return null;
            return requested==RoadBridgeDesign.Arch
                ? "Uses piers here · Arch needs straight, supported ends 30–350 m apart."
                : "Uses piers here · Suspension needs straight, supported ends at least 60 m apart.";
        }
        // Explicit modes are used by construction review scenes. The player tools always start in Automatic.
        public void SetRoadMode(RoadBuildMode mode)
        {
            roadBuildSettings.mode=mode;
            if(mode==RoadBuildMode.Bridge && roadElevation<1f)roadElevation=8f;
            if(mode==RoadBuildMode.Tunnel && roadElevation> -1f)roadElevation=-10f;
            if(mode==RoadBuildMode.Ground)roadElevation=0;
            if(mode==RoadBuildMode.Tunnel)SetRoadUnderground(true);else if(mode==RoadBuildMode.Bridge||mode==RoadBuildMode.Ground)SetRoadUnderground(false);
            InvalidateRoadPlan();
        }
        public void SetRoadElevation(float value) => ApplyRoadElevation(Mathf.Clamp(value, -150f, 150f));
        void ApplyRoadElevation(float value)
        {
            if (value == roadElevation) return;
            roadElevation = value;
            InvalidateRoadPlan();
        }
        const float RoadElevationStep = 5f;
        public void StepRoadElevation(int direction)
        {
            bool wasBelowGround = roadElevation < 0f;
            float step = roadElevation / RoadElevationStep;
            float next = (direction > 0 ? Mathf.Floor(step) + 1 : Mathf.Ceil(step) - 1) * RoadElevationStep;
            ApplyRoadElevation(Mathf.Clamp(next, Mathf.Min(-150f, roadElevation), Mathf.Max(150f, roadElevation)));
            if (wasBelowGround != (roadElevation < 0f)) SetRoadUnderground(roadElevation < 0f);
        }

        void SetRoadBend(float bend)
        {
            if (roadBuildSettings.bend == bend) return;
            roadBuildSettings.bend = bend;
            InvalidateRoadPlan();
        }

        void InvalidateRoadPlan(){streetQuoteKey=default;streetPreviewBuilt=default;airsideQuoteKey=default;}
        float RoadTargetHeight(Vector2 p)
        {
            if(roadDatum.HasValue&&(roadBuildSettings.mode==RoadBuildMode.Bridge||roadBuildSettings.mode==RoadBuildMode.Tunnel||roadBuildSettings.mode==RoadBuildMode.Automatic&&Mathf.Abs(roadElevation)>.01f))return roadDatum.Value+roadElevation;
            if (roadUnderground && undergroundDatum.HasValue)
                return RoadDrawing && RoadStart is Vector3 anchor ? anchor.y : undergroundDatum.Value + roadElevation;
            if (game == null) return roadElevation;
            float height = game.valley.DisplayHeight(p);
            var world = game.State.world;
            if (world.IsWater(p))
            {
                if (roadBuildSettings.mode != RoadBuildMode.Tunnel)
                    height = Mathf.Max(height, world.WaterSurfaceAt(p) + LowDeck);
            }
            else if (RoadDrawing && roadBuildSettings.mode == RoadBuildMode.Automatic &&
                roadElevation == 0f && RoadStart is Vector3 start)
            {
                var from = Geo.XZ(start);
                float rise = height - start.y;
                float limit = tool == DevTool.Street ? PublicRoadConstruction.MaximumGroundGrade : AirsideTerrainPlan.MaximumRoadGrade;
                float distance = Vector2.Distance(from, p);
                // A free point against a steep face keeps the road's level so it can bore into the hill or span a drop.
                // A deliberately snapped endpoint still uses the height of the road being joined.
                if (Mathf.Abs(rise) > RoadConstructionPlan.AtGrade)
                {
                    if (Mathf.Abs(rise) > distance * limit) return start.y;
                    float approach = Mathf.Min(24f, distance);
                    var beforeFace = Vector2.MoveTowards(p, from, approach);
                    float faceRise = height - game.valley.DisplayHeight(beforeFace);
                    if (Mathf.Sign(faceRise) == Mathf.Sign(rise) &&
                        Mathf.Abs(faceRise) > approach * limit + RoadConstructionPlan.AtGrade)
                        return start.y;
                }
            }
            return height+roadElevation;
        }
        public void SetRoadUnderground(bool shown)
        {
            if (shown != roadUnderground)
            {
                Vector2 at = lastRoadPointer ?? Geo.XZ(lastGround);
                undergroundDatum = shown && game != null ? RoadGround(at) : (float?)null;
                roadUnderground = shown;
                InvalidateRoadPlan();
            }
            UpdateUndergroundView();
        }
        void UpdateUndergroundView()
        {
            if(game==null)return;
            var view=game.GetComponent<UndergroundRoadView>();
            if(view==null && !roadUnderground)return;
            if(view==null)view=game.gameObject.AddComponent<UndergroundRoadView>();
            bool shown = roadUnderground && (tool == DevTool.Street || tool == DevTool.Road || tool == DevTool.Select || tool == DevTool.Delete);
            view.Show(shown, shown ? RoadTargetHeight(lastRoadPointer ?? Geo.XZ(lastGround)) + UndergroundRoadView.Headroom : 0f);
        }

        // The tunnel guides sit over the rendered cutaway, so they also take priority when picking.
        bool PickUndergroundTunnel(Ray ray, out Vector3 point)
        {
            var hit = Vector3.zero;
            var match = Pick.None;
            int node = -1;
            float best = float.PositiveInfinity;
            const float lift = .3f;

            void Test(Alignment3D path, float half, int a, int b, int anchor, Transform space, Pick pick)
            {
                var localRay = space == null ? ray : new Ray(space.InverseTransformPoint(ray.origin), space.InverseTransformDirection(ray.direction));
                bool End(Vector3 local, int id)
                {
                    if (id == anchor) return false;
                    var world = space == null ? local : space.TransformPoint(local);
                    var offset = world + Vector3.up * lift - ray.origin;
                    float along = Vector3.Dot(offset, ray.direction);
                    float reach = Mathf.Max(1.5f, MetresPerPixel(world) * 10f);
                    if (along < 0f || along >= best || (offset - ray.direction * along).sqrMagnitude > reach * reach)
                        return false;
                    (hit, match, node, best) = (world, pick, id, along);
                    return true;
                }
                bool atStart = End(path.start, a);
                bool atEnd = End(path.end, b);
                if (atStart || atEnd || !path.Raycast(localRay, half, out float distance, lift)) return;
                var localHit = localRay.GetPoint(distance) - Vector3.up * lift;
                var worldHit = space == null ? localHit : space.TransformPoint(localHit);
                float along = Vector3.Dot(worldHit + Vector3.up * lift - ray.origin, ray.direction);
                if (along < best) (hit, match, node, best) = (worldHit, pick, -1, along);
            }

            if (tool != DevTool.Road)
            {
                var roads = Streets.network;
                foreach (var edge in roads.segments)
                    if (edge.structure == RoadStructure.Tunnel)
                        Test(roads.Path(edge), edge.Type.FullWidth * .5f, edge.a, edge.b, streetChain, null,
                            new Pick { kind = Kind.PublicRoad, publicRoad = edge, apron = -1 });
            }
            if (tool != DevTool.Street)
            {
                var net = airport.Taxiways;
                foreach (var edge in net.edges)
                    if (edge.IsRoad && edge.structure == RoadStructure.Tunnel)
                        Test(net.Path(edge), edge.width * .5f, edge.a, edge.b, chainNode, airport.transform,
                            new Pick { kind = Kind.Edge, edge = edge, apron = -1 });
            }
            undergroundTunnel = match;
            undergroundTunnelNode = node;
            point = hit;
            return match.kind != Kind.None;
        }
        bool PickRoadLevel(Ray ray,float maximum,out Vector3 point)
        {
            point=default;float best=maximum;bool found=false;
            if(tool!=DevTool.Street&&tool!=DevTool.Road&&tool!=DevTool.Select&&tool!=DevTool.Delete)return false;
            var roads=Streets.network;
            foreach(var edge in roads.segments)
                if(roads.Path(edge).Raycast(ray,edge.Type.FullWidth*.5f,out float t,.18f)&&t<best)
                {
                    var p=ray.GetPoint(t);if(roadUnderground&&game!=null&&p.y>game.GetComponent<UndergroundRoadView>().Height)continue;
                    best=t;point=p;found=true;
                }
            var transform=airport.transform;var local=new Ray(transform.InverseTransformPoint(ray.origin),transform.InverseTransformDirection(ray.direction));
            foreach(var edge in airport.Taxiways.edges)
                if(airport.Taxiways.Path(edge).Raycast(local,edge.width*.5f,out float t,.01f))
                {
                    var p=transform.TransformPoint(local.GetPoint(t));float distance=Vector3.Distance(ray.origin,p);if(distance>=best)continue;
                    if(roadUnderground&&game!=null&&p.y>game.GetComponent<UndergroundRoadView>().Height)continue;
                    best=distance;point=p;found=true;
                }
            return found;
        }
        bool RoadDrawing => tool == DevTool.Street ? streetChainPoint.HasValue : tool == DevTool.Road && chainNode >= 0;
        Vector3? RoadStart => tool == DevTool.Street ? Streets.network.Node(streetChain)?.position :
            airport.Taxiways.Node(chainNode) is TaxiwayNode node ? airport.transform.TransformPoint(node.position) : (Vector3?)null;

        Vector2? roadEndpointPress;
        Vector3 roadCurveStart, roadCurveEnd;
        bool roadCurveDragging;
        public string RoadPlacementHint => roadCurveDragging ? "Drag sideways to shape the curve. Release to build."
            : RoadDrawing ? "Click to place · Drag sideways to curve\nShift snaps · Right-click ends"
            : roadUnderground ? "Click a cyan tunnel end to continue"
            : "Click where the road starts";

        void ResetRoadDrag()
        {
            roadEndpointPress = null;
            roadCurveDragging = false;
            SetRoadBend(0f);
        }

        // Pressing over an endpoint locks its snap. Moving sideways before releasing bows the midpoint of the
        // proposed road, leaving both connections in place.
        void PlaceRoad(Mouse mouse, Keyboard keys, bool click, bool free, Vector3 point)
        {
            bool street = tool == DevTool.Street;
            if (roadEndpointPress.HasValue && (!RoadDrawing || !free || !mouse.leftButton.isPressed &&
                !mouse.leftButton.wasReleasedThisFrame))
                ResetRoadDrag();

            bool pressEndpoint = click && RoadDrawing && !mouse.leftButton.wasReleasedThisFrame;
            bool releaseEndpoint = roadEndpointPress.HasValue && mouse.leftButton.wasReleasedThisFrame;
            bool place = releaseEndpoint || (click && !pressEndpoint);
            if (roadEndpointPress is Vector2 pressedAt)
                DragRoadCurve(pressedAt, mouse.position.ReadValue());
            if (street) StreetTool(mouse, keys, place, free, point);
            else TaxiwayTool(place, free, point, true);

            if (releaseEndpoint) ResetRoadDrag();
            if (!pressEndpoint || !(RoadStart is Vector3 start)) return;
            roadCurveStart = start;
            roadCurveEnd = street ? StreetEndpoint : AirsideEndpoint;
            if (street && game == null)
            {
                roadCurveStart = airport.transform.TransformPoint(roadCurveStart);
                roadCurveEnd = airport.transform.TransformPoint(roadCurveEnd);
            }
            roadEndpointPress = mouse.position.ReadValue();
        }

        void DragRoadCurve(Vector2 pressedAt, Vector2 cursor)
        {
            if (!roadCurveDragging && (cursor - pressedAt).sqrMagnitude < 36f) return;
            roadCurveDragging = true;
            var plane = new Plane(Vector3.up, roadCurveEnd);
            var pressRay = cam.ScreenPointToRay(pressedAt);
            var cursorRay = cam.ScreenPointToRay(cursor);
            if (!plane.Raycast(pressRay, out float pressDistance) || !plane.Raycast(cursorRay, out float cursorDistance)) return;
            var move = cursorRay.GetPoint(cursorDistance) - pressRay.GetPoint(pressDistance);
            var chord = roadCurveEnd - roadCurveStart;
            if (tool == DevTool.Road || game == null)
            {
                move = airport.transform.InverseTransformVector(move);
                chord = airport.transform.InverseTransformVector(chord);
            }
            float length = Geo.XZ(chord).magnitude;
            var right = new Vector2(chord.z, -chord.x).normalized;
            SetRoadBend(Mathf.Clamp(2f * Vector2.Dot(Geo.XZ(move), right), -length, length));
        }

        void DrawRoadCurveGuide()
        {
            if (!roadCurveDragging) return;
            var colour = new Color(.62f, .78f, .86f, .7f);
            loop.Clear();
            loop.Add(roadCurveStart);
            loop.Add(roadCurveEnd);
            outlines.Line(cam, loop, colour, 1f, false, true);
        }

        void DrawRoadEndpoint(Vector3 end, bool snapped, bool invalid)
        {
            float radius = MetresPerPixel(end) * (snapped ? 9f : 6f);
            loop.Clear();
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                loop.Add(end + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
            outlines.Line(cam, loop, invalid ? DevGui.Danger : DevGui.Accent, 2f, true, true);
        }

        /// <summary>The level the cursor works at (world): before a chain in Automatic at 0, whatever it is on;
        /// otherwise where the next free point would go.</summary>
        float RoadWorkingLevel(Vector2 point)=>!RoadDrawing&&roadBuildSettings.mode==RoadBuildMode.Automatic&&Mathf.Abs(roadElevation)<.1f?lastGround.y:RoadTargetHeight(point);
        bool SnapRoadLayer(float height, float level, Vector2 at)
        {
            if (Mathf.Abs(height - level) >= 3f) return false;
            float roof = height + RoadStructureBuilder.TunnelClearance + RoadStructureBuilder.TunnelCover;
            return game == null || roadUnderground || roof >= game.valley.DisplayHeight(at);
        }
        /// <summary>The last cursor ray (world) and how far along it the visible ground is (+∞ in the underground view
        /// or where pavement clips the terrain): set by Ground.</summary>
        Ray roadRay;
        float roadRayGround=float.PositiveInfinity;
        /// <summary>Where the cursor ray crosses a level (world x, z), unless that is behind the ground it meets first.</summary>
        bool RoadRayAt(float height,out Vector2 at)
        {
            at=default;if(Mathf.Abs(roadRay.direction.y)<.0001f)return false;
            float t=(height-roadRay.origin.y)/roadRay.direction.y;
            if(t<0f||t>roadRayGround+.5f)return false;
            at=Geo.XZ(roadRay.GetPoint(t));return true;
        }
        /// <summary>A road at another level is joined only where the player points at it: the cursor ray, at that road's
        /// own level, passes within `within` of it (world x, z), and that point is not hidden behind the ground.
        /// The cutaway lets the player point at its road outlines, including an upper road to join at the end of a ramp.
        /// Drawing over or under a road crosses it unless the cursor is put on it.</summary>
        bool SnapAcrossLevels(float height,Vector2 world,float within)=>RoadRayAt(height,out var at)&&Vector2.Distance(at,world)<=within;
        /// <summary>SnapAcrossLevels for a section (path in `space`, null for world): its parameter where the ray at its
        /// level comes nearest, and that point's height (world).</summary>
        bool SnapAcrossSection(Alignment3D path,Transform space,float within,out float t,out float height)
        {
            var offset=space!=null?space.position:Vector3.zero;t=0f;height=0f;
            float y=path.Height(path.ClosestParameter(Geo.XZ(roadRay.origin)-Geo.XZ(offset)))+offset.y;
            for(int i=0;i<3;i++)
            {
                if(!RoadRayAt(y,out var at))return false;
                var local=at-Geo.XZ(offset);t=path.ClosestParameter(local);height=path.Height(t)+offset.y;
                if(Mathf.Abs(height-y)<.25f)return path.DistanceTo(local)<=within;
                y=height;
            }
            return false;
        }
        /// <summary>Ranks a road at another level after every one at the working level: the nearest level first.</summary>
        static float AcrossRank(float height,float level,float distance)=>1e5f+Mathf.Abs(height-level)*100f+distance;
        /// <summary>How a snapped road's level compares with the last point, for the strip and map label.</summary>
        string LevelHint(float height,float from)
        {
            float rise=height-from;
            return Mathf.Abs(rise)<=PublicRoadNetwork.JunctionHeightTolerance?"":$", {Mathf.Abs(rise):0.0} m {(rise>0?"up":"down")}: the road ramps to it";
        }

        readonly Plane[] undergroundFrustum = new Plane[6];

        /// <summary>Tunnels stay readable through terrain, roofs and the grid at every depth. Nearby upper roads
        /// give context and can still be joined from the cutaway.</summary>
        void DrawRoadLevelGuide()
        {
            if (game == null) return;
            var view = game.GetComponent<UndergroundRoadView>();
            if (view == null || !view.Visible) return;
            GeometryUtility.CalculateFrustumPlanes(cam, undergroundFrustum);
            int upperRoads = 0;

            void Outline(Alignment3D path, float half, Transform space, RoadStructure structure)
            {
                bool tunnel = structure == RoadStructure.Tunnel;
                Vector3 origin = space == null ? Vector3.zero : space.position;
                if (!tunnel && (upperRoads >= 48 || Mathf.Max(path.start.y, path.end.y) + origin.y <= view.Height)) return;
                var bounds = new Bounds(path.start + origin, Vector3.zero);
                bounds.Encapsulate(path.end + origin);
                if (path.shape.curved) bounds.Encapsulate(Geo.X0Z(path.shape.control, path.start.y) + origin);
                float gradeReach = path.Length * Mathf.Max(Mathf.Abs(path.shape.startGrade), Mathf.Abs(path.shape.endGrade)) / 3f;
                bounds.Expand(new Vector3(half * 2f, 2f * gradeReach + 2f, half * 2f));
                if (!GeometryUtility.TestPlanesAABB(undergroundFrustum, bounds)) return;
                if (!tunnel)
                {
                    var centre = Geo.XZ(lastGround);
                    float radius = 300f;
                    if (centre.x < bounds.min.x - radius || centre.x > bounds.max.x + radius ||
                        centre.y < bounds.min.z - radius || centre.y > bounds.max.z + radius) return;
                    upperRoads++;
                }

                int count = Mathf.Clamp(Mathf.CeilToInt(path.Length / 10f), 1, 60);
                loop.Clear();
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int i = 0; i <= count; i++)
                    {
                        float distance = path.Length * (side < 0 ? i : count - i) / count;
                        var at = path.Offset(distance, side * half);
                        loop.Add(space == null ? at : space.TransformPoint(at));
                    }
                }
                var colour = tunnel ? new Color(.35f, .86f, 1f) : new Color(.62f, .78f, .86f, .55f);
                outlines.Line(cam, loop, colour, tunnel ? 2.5f : 1.5f, true, true);
            }

            if (tool != DevTool.Road)
            {
                var roads = Streets.network;
                foreach (var edge in roads.segments)
                    Outline(roads.Path(edge), edge.Type.FullWidth * .5f, null, edge.structure);
            }
            if (tool != DevTool.Street)
            {
                var net = airport.Taxiways;
                foreach (var edge in net.edges)
                    if (edge.IsRoad) Outline(net.Path(edge), edge.width * .5f, airport.transform, edge.structure);
            }
        }

        /// <summary>A free point over water, where the road follows the ground, is a low deck this far over the surface.</summary>
        const float LowDeck=2.5f;
        float? adoptedElevation;
        float chosenElevation;
        /// <summary>The ground a chain's level is measured from: the terrain, or the water surface for anything but a tunnel.</summary>
        float RoadGround(Vector2 p)
        {
            if(game==null)return 0f;float height=game.valley.DisplayHeight(p);
            if(roadBuildSettings.mode!=RoadBuildMode.Tunnel&&game.State.world.IsWater(p))height=Mathf.Max(height,game.State.world.WaterSurfaceAt(p));
            return height;
        }
        /// <summary>
        /// A chain's level follows the roads it is clicked onto. Its datum is the ground where it starts (or last joined
        /// a road). Starting on, or joining, a bridge, a tunnel or a ramp more than AtGrade off the ground takes that
        /// road's level as the elevation, so the next point continues level with it; joining a ground road returns to 0
        /// (Automatic then follows the ground again) and closes the underground view. Starting on a ground road keeps the
        /// elevation the player chose, so pressing Bridge first climbs away from it. Ground mode always follows the ground. Positions are world.
        /// The chosen elevation comes back when the chain ends (RoadElevationKeys) unless the player changed it since.
        /// </summary>
        void AdoptRoadLevel(Vector2 at,float height,bool joined,bool structure,bool start)
        {
            if(game==null || !joined){if(start)roadDatum=height-roadElevation;return;}
            float ground=RoadGround(at);roadDatum=ground;
            if(roadBuildSettings.mode==RoadBuildMode.Ground)return;
            bool raised=structure||Mathf.Abs(height-ground)>RoadConstructionPlan.AtGrade;
            if(!raised&&start)return;
            if(!adoptedElevation.HasValue)chosenElevation=roadElevation;
            ApplyRoadElevation(raised ? height - ground : 0f);
            adoptedElevation=roadElevation;
            // back on a surface road: the cutaway has nothing left to show
            if(!raised&&roadUnderground)SetRoadUnderground(false);
        }

        void RoadElevationKeys(Keyboard keys)
        {
            if(tool==DevTool.Road&&chainNode<0 || tool==DevTool.Street&&!streetChainPoint.HasValue || tool!=DevTool.Road&&tool!=DevTool.Street)
            {
                roadDatum=null;
                if (adoptedElevation.HasValue)
                {
                    if (roadElevation == adoptedElevation.Value)
                    {
                        var view = game != null ? game.GetComponent<UndergroundRoadView>() : null;
                        if (view != null && view.Visible)
                            undergroundDatum = view.Height - UndergroundRoadView.Headroom - chosenElevation;
                        ApplyRoadElevation(chosenElevation);
                    }
                    adoptedElevation = null;
                }
            }
            if(tool!=DevTool.Street && tool!=DevTool.Road){UpdateUndergroundView();return;}
            if (keys.pageUpKey.wasPressedThisFrame) StepRoadElevation(1);
            if (keys.pageDownKey.wasPressedThisFrame) StepRoadElevation(-1);
            UpdateUndergroundView();
        }
    }
    public partial class DevToolbar
    {
        void RoadBuildControls()
        {
            float elevation = airfield.RoadElevation;
            PropStepper("Elevation", $"{Mathf.RoundToInt(elevation):+0;-0;0} m", () => airfield.StepRoadElevation(-1),
                () => airfield.StepRoadElevation(1), "Change height by 5 m · Page Up / Down",
                canDown: elevation > -150f, canUp: elevation < 150f);
            Buttons(B(airfield.RoadUnderground?"Surface view":"Underground view",()=>airfield.SetRoadUnderground(!airfield.RoadUnderground),"Reveal roads below the landscape",airfield.RoadUnderground));
            BridgeDesignStepper(airfield.NewBridgeDesign,airfield.SetRoadBridgeDesign);
            TunnelPortalStyleStepper(airfield.NewTunnelPortalStyle, airfield.SetRoadTunnelPortalStyle);
            var notice=airfield.PreviewBridgeDesignNotice;
            if(notice!=null)Note(notice);
        }

        void SelectedBridgeDesignControls()
        {
            var design=airfield.SelectedBridgeDesign;
            if(!design.HasValue)return;
            BridgeDesignStepper(design.Value,airfield.SetSelectedBridgeDesign);
            var notice=airfield.SelectedBridgeDesignNotice;
            if(notice!=null)Note(notice);
        }

        void SelectedTunnelPortalStyleControls()
        {
            if (airfield.SelectedTunnelPortalStyle is TunnelPortalStyle style)
                TunnelPortalStyleStepper(style, airfield.SetSelectedTunnelPortalStyle);
        }

        void TunnelPortalStyleStepper(TunnelPortalStyle style, System.Action<TunnelPortalStyle> select)
        {
            PropStepper("Tunnel portal", style == TunnelPortalStyle.Arched ? "Arched" : "Rectangular",
                () => select(TunnelPortalStyle.Rectangular), () => select(TunnelPortalStyle.Arched),
                "Choose the concrete frame at tunnel entrances", canDown: style == TunnelPortalStyle.Arched,
                canUp: style == TunnelPortalStyle.Rectangular);
        }

        void BridgeDesignStepper(RoadBridgeDesign design,System.Action<RoadBridgeDesign> select)
        {
            PropStepper("Bridge design",design.ToString(),()=>select((RoadBridgeDesign)((int)design-1)),
                ()=>select((RoadBridgeDesign)((int)design+1)),"Choose how elevated road spans are supported",
                canDown:design!=RoadBridgeDesign.Automatic,canUp:design!=RoadBridgeDesign.Suspension);
        }
    }
}
