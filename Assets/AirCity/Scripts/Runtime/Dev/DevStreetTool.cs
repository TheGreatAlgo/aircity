using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using AirCity.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    /// <summary>
    /// The shared Street tool: public (landside) roads of any type (RoadTypes), drawn on the airport's
    /// PublicRoadsView. Click points to lay a road, right-click ends it. Ends snap to road nodes and onto sections
    /// (joining them: a T junction); a line crossing another road makes a junction there (PublicRoadNetwork.Connect),
    /// and junctions where traffic arrives from two or more roads get traffic lights. A line that would meet another
    /// road under TaxiwayNetwork.MinJoinAngle, or leave a section too short for its junctions, shows red and won't build.
    /// The road being laid shows as itself (the real mesh, markings and signals), rebuilt only when it changes.
    /// </summary>
    public partial class DevAirfieldTool
    {
        PublicRoadStyle streetStyle = PublicRoadStyle.Urban;
        int streetChain = -1;
        PublicRoadNetwork streetDraftBase;
        Vector2? streetChainPoint;
        (Vector2 p, int node, PublicRoadSegment edge, string what) streetSnap;
        bool streetSnapShown;
        string streetProblem;
        PublicRoadsView streetPreview,streetPreviewSource,streetUnchangedPreview;
        PublicRoadNetwork streetUnchangedSource;
        Material streetStructureMaterial;
        HashSet<int> streetUnchangedSegments,streetUnchangedJunctions;
        PublicRoadNetwork streetDraft;
        RoadConstructionPlan streetPlan;
        readonly List<(Vector3 a,Vector3 b)> streetOutline=new List<(Vector3,Vector3)>();
        (PublicRoadNetwork graph,Vector3 a, Vector3 b, PublicRoadStyle style) streetPreviewBuilt;
        (PublicRoadNetwork network,int from,Vector3 end,PublicRoadStyle style,int funds,int version) streetQuoteKey;
        string streetQuoteProblem;
        int streetPrice;
        public int StreetPrice=>streetPrice;
        public bool StreetDrawing=>tool==DevTool.Street && streetChainPoint.HasValue && streetSnapShown;
        public Vector3 StreetEndpoint=>StreetPoint(Streets.network,streetSnap);
        public float StreetGrade
        {
            get
            {
                var start=Streets.network.Node(streetChain);var end=StreetEndpoint;
                return start==null?0f:(end.y-start.position.y)/Mathf.Max(1f,Vector2.Distance(PublicRoadNetwork.XZ(start.position),PublicRoadNetwork.XZ(end)));
            }
        }

        public PublicRoadStyle StreetStyle => streetStyle;
        public string StreetProblem => tool == DevTool.Street ? streetProblem : null;
        public string StreetSnapName => tool == DevTool.Street && streetSnapShown ? streetSnap.what : null;

        public void SetStreetStyle(PublicRoadStyle style)
        {
            streetStyle = style;
            streetPreviewBuilt = default;
        }

        /// <summary>The airport's public roads, made on first use (with the materials of the airfield's own).</summary>
        public PublicRoadsView Streets
        {
            get
            {
                if (game != null) return game.Streets;
                var view = airport.GetComponentInChildren<PublicRoadsView>(true);
                if (view != null) return view;
                var go = new GameObject("Public roads");
                go.transform.SetParent(airport.transform, false);
                view = go.AddComponent<PublicRoadsView>();
                Dress(view);
                return view;
            }
        }

        [Tooltip("The public road traffic signal (Assets/AirCity/World/Roads/TrafficSignal.prefab).")]
        public GameObject trafficSignalPrefab;
        [Tooltip("Kerb and barrier concrete for public roads.")]
        public Material roadKerbMaterial;

        void Dress(PublicRoadsView view)
        {
#if UNITY_EDITOR
            // the sandbox scene may predate the street tool: find the world assets by path
            if (trafficSignalPrefab == null)
                trafficSignalPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AirCity/World/Roads/TrafficSignal.prefab");
            if (roadKerbMaterial == null)
                roadKerbMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/AirCity/World/Roads/Materials/RoadKerb.mat");
#endif
            view.asphalt = airport.SurfaceMaterial(PavementSurface.Asphalt);
            view.shoulder = airport.SurfaceMaterial(PavementSurface.Concrete);
            view.markings = airport.paint;
            view.kerb = roadKerbMaterial != null ? roadKerbMaterial : airport.building;
            view.medianGrass = airport.grass;
            if(streetStructureMaterial==null)streetStructureMaterial=RoadStructureBuilder.Material(roadKerbMaterial);
            view.structures=streetStructureMaterial;
            view.trafficSignalPrefab = trafficSignalPrefab;
            view.GroundHeight=p=>game!=null?game.valley.DisplayHeight(p):0f;
        }

        void StreetTool(Mouse mouse, Keyboard keys, bool click, bool free, Vector3 point)
        {
            var view = Streets;
            var net = view.network;
            if (mouse.rightButton.wasPressedThisFrame || keys.escapeKey.wasPressedThisFrame) EndStreet();
            streetSnapShown = free;
            streetProblem = null;
            if (!free)
            {
                HideStreetPreview();
                return;
            }
            var q = game != null ? Geo.XZ(point) : Geo.XZ(airport.transform.InverseTransformPoint(point));
            streetSnap = SnapStreet(net, q, keys.shiftKey.isPressed);
            if(streetSnap.what==null)streetSnap.p=new Vector2(Mathf.Round(streetSnap.p.x),Mathf.Round(streetSnap.p.y));
            if (streetChainPoint is Vector2 from)
            {
                var quoteKey=(net,streetChain,StreetPoint(net,streetSnap),streetStyle,game!=null?game.State.funds:0,airport.LayoutVersion);
                if(quoteKey!=streetQuoteKey)
                {
                    streetQuoteKey=quoteKey;
                    streetQuoteProblem=StreetLineProblem(net,from,streetSnap.p);
                }
                streetProblem=streetQuoteProblem;
                ShowStreetPreview(from, streetSnap.p);
            }
            else HideStreetPreview();
            if (!click || streetProblem != null) return;
            int node;
            bool joined=streetSnap.node>=0||streetSnap.edge!=null,structure=StreetStructure(net,streetSnap.node,streetSnap.edge);
            if (streetChain >= 0)
            {
                var end = StreetPoint(net, streetSnap);
                // The standalone review scene uses the same transaction with free construction rules.
                var state = game != null ? game.State : new AirCity.Game.CareerState { mode = AirCity.Game.GameMode.Sandbox };
                if(streetPlan==null || !streetPlan.TryCommit(state,net,out var proposed,out streetProblem))
                {streetQuoteKey=default;return;}
                node=streetPlan.EndNode;HideStreetPreview();
                view.network=proposed;net=proposed;streetDraftBase=null;
                view.Rebuild();
                if(joined)AdoptRoadLevel(PublicRoadNetwork.XZ(net.Node(node).position),net.Node(node).position.y,true,structure,false);
            }
            else
            {
                streetDraftBase = net;
                var draft = net.Clone();
                var snap = (streetSnap.p, streetSnap.node, streetSnap.edge != null ? draft.Segment(streetSnap.edge.id) : null, streetSnap.what);
                node = StreetNode(draft, snap);view.network = draft; net = draft;
                AdoptRoadLevel(PublicRoadNetwork.XZ(draft.Node(node).position),draft.Node(node).position.y,joined,structure,true);
            }
            streetChain = node;
            streetChainPoint = PublicRoadNetwork.XZ(net.Node(node).position);
            streetPreviewBuilt = default; streetQuoteKey=default;
        }

        void EndStreet()
        {
            roadDatum=null;
            if (streetDraftBase != null) { Streets.network = streetDraftBase; streetDraftBase = null; }
            streetChain = -1;
            streetChainPoint = null;streetQuoteKey=default;streetPrice=0;
            HideStreetPreview();
        }

        /// <summary>Where the next point goes: a road node within reach, else onto a section, else free (Shift: 10° and
        /// 5 m steps from the last point). Roads at the working level (SnapRoadLayer) stick from within reach; a road at
        /// another level only where the cursor is on it (SnapAcrossLevels), the nearest level first, and the new road
        /// then ramps to its level. So a bridge can come down onto a street, and a tunnel up onto one, by pointing at it.</summary>
        (Vector2 p, int node, PublicRoadSegment edge, string what) SnapStreet(PublicRoadNetwork net, Vector2 q, bool steps)
        {
            float reach = Mathf.Max(MetresPerPixel(game != null ? Geo.X0Z(q) + Vector3.up * game.valley.DisplayHeight(q) : airport.transform.TransformPoint(Geo.X0Z(q))) * 14f, RoadTypes.Of(streetStyle).Width * .5f);
            float level=RoadWorkingLevel(q);var chain=streetChain>=0?net.Node(streetChain):null;
            string Name(string what,float height)=>chain==null?what:what+LevelHint(height,chain.position.y);
            PublicRoadNode bestNode = null;
            float best = float.MaxValue;
            foreach (var n in net.nodes)
            {
                if(n.id==streetChain)continue;
                float d = Vector2.Distance(PublicRoadNetwork.XZ(n.position), q);if(d>=Mathf.Max(reach,WidestStreet)+AcrossParallax)continue;
                bool same=SnapRoadLayer(n.position.y,q);
                if(same?d>=reach:!SnapAcrossLevels(n.position.y,PublicRoadNetwork.XZ(n.position),Mathf.Max(reach,net.At(n.id).Select(e=>e.Type.FullWidth*.5f).DefaultIfEmpty(0f).Max())))continue;
                float rank=same?d:AcrossRank(n.position.y,level,0f);
                if (rank < best) { best = rank; bestNode = n; }
            }
            if (bestNode != null)
                return (PublicRoadNetwork.XZ(bestNode.position), bestNode.id, null,
                    Name(bestNode.terminalEntrance == VisitorCentreLayout.EntranceId ? "the visitor centre's driveway" : "the junction", bestNode.position.y));
            PublicRoadSegment bestEdge = null;
            Vector2 on = q;float onHeight=0f;
            best = float.MaxValue;
            foreach (var e in net.segments)
            {
                var path=net.Path(e);float t=path.ClosestParameter(q);float h=path.Height(t);var c=path.Horizontal(t);
                float d = Vector2.Distance(c, q);bool same=SnapRoadLayer(h,q);
                if(!same)
                {
                    if(d>WidestStreet+AcrossParallax||!SnapAcrossSection(path,null,e.Type.FullWidth*.5f,out t,out h))continue;
                    c=path.Horizontal(t);
                }
                else if(d>Mathf.Max(reach,e.Width*.5f))continue;
                float rank=same?d:AcrossRank(h,level,0f);
                if (rank < best) { best = rank; bestEdge = e; on = c;onHeight=h; }
            }
            if (bestEdge != null) return (on, -1, bestEdge, Name("the " + bestEdge.Type.name.ToLowerInvariant(),onHeight));
            if (VisitorCentreConstruction.DrivewayOutward(game?.State, chain) is Vector2 outward && !net.At(chain.id).Any())
            {
                var mouth = Geo.XZ(chain.position);
                var offset = q - mouth;
                float forward = Vector2.Dot(offset, outward);
                if (forward >= 6f && Mathf.Abs(Geo.Cross(offset, outward)) < Mathf.Clamp(reach, 4f, 8f))
                    return (mouth + outward * forward, -1, null, "the visitor centre driveway alignment");
            }
            if (steps && streetChainPoint is Vector2 from)
            {
                var d = q - from;
                float angle = Mathf.Round(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg / 10f) * 10f * Mathf.Deg2Rad;
                float length = Mathf.Max(5f, Mathf.Round(d.magnitude / 5f) * 5f);
                return (from + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length, -1, null, null);
            }
            return (q, -1, null, null);
        }

        static float widestStreet;
        /// <summary>Half the widest street's full width: no road further than this from the cursor is on its pavement.</summary>
        static float WidestStreet
        {
            get
            {
                if(widestStreet<=0f)foreach(PublicRoadStyle style in System.Enum.GetValues(typeof(PublicRoadStyle)))widestStreet=Mathf.Max(widestStreet,RoadTypes.Of(style).FullWidth*.5f);
                return widestStreet;
            }
        }
        /// <summary>How far (x, z) the cursor at the working level may be from a road at another level that it points
        /// at: the parallax of a low view over a deep level difference.</summary>
        const float AcrossParallax=80f;
        /// <summary>Is the snapped road a bridge or tunnel (any of its sections, for a node)?</summary>
        static bool StreetStructure(PublicRoadNetwork net,int node,PublicRoadSegment edge)=>
            edge!=null?edge.structure!=RoadStructure.Ground:node>=0&&net.At(node).Any(e=>e.structure!=RoadStructure.Ground);

        int StreetNode(PublicRoadNetwork net, (Vector2 p, int node, PublicRoadSegment edge, string what) snap)
        {
            if (snap.node >= 0 && net.Node(snap.node) != null) return snap.node;
            var at = StreetPoint(net, snap);
            if (snap.edge != null && net.segments.Contains(snap.edge))
            {
                int id = net.Split(snap.edge, at);
                net.Control(id);
                return id;
            }
            return net.AddNode(at);
        }

        Vector3 StreetPoint(PublicRoadNetwork net, (Vector2 p, int node, PublicRoadSegment edge, string what) snap)
        {
            if (snap.node >= 0 && net.Node(snap.node) != null) return net.Node(snap.node).position;
            if (snap.edge != null)
            {
                var path=net.Path(snap.edge);return path.Position(path.ClosestParameter(snap.p));
            }
            return new Vector3(snap.p.x,RoadTargetHeight(snap.p),snap.p.y);
        }

        /// <summary>Why a road from `from` to `to` won't do, or null: too short, meeting a road too shallowly, or crossing
        /// one so near a junction that a section between would be too short (PublicRoadNetwork.JoinProblem).</summary>
        string StreetLineProblem(PublicRoadNetwork net, Vector2 from, Vector2 to)
        {
            streetDraft=null;streetPlan=null;
            var state=game!=null?game.State:new CareerState {mode=GameMode.Sandbox};
            bool valid=RoadConstructionPlan.TryCreate(state,net,streetChain,StreetPoint(net,streetSnap),streetSnap.node,
                streetSnap.edge!=null?streetSnap.edge.id:-1,streetStyle,roadBuildSettings,out streetPlan,out var problem,airport);
            streetDraft=streetPlan?.Draft;streetPrice=streetPlan?.Price??0;
            return valid?null:problem;
        }

        void ShowStreetPreview(Vector2 a, Vector2 b)
        {
            if (Vector2.Distance(a, b) < 1f) { HideStreetPreview(); return; }
            if (streetPreview == null)
            {
                var go = new GameObject("Street preview") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(game != null ? game.transform : airport.transform, false);
                go.SetActive(false);
                streetPreview = go.AddComponent<PublicRoadsView>();
                streetPreview.constructionPreview = true;
                Dress(streetPreview);
                streetPreview.level = .06f;   // over the roads it starts from
                go.SetActive(true);
            }
            streetPreview.gameObject.SetActive(true);
            var net = Streets.network;
            var start = streetChain >= 0 && net.Node(streetChain) != null ? net.Node(streetChain).position :
                new Vector3(a.x, game != null ? game.valley.DisplayHeight(a) : 0f, a.y);
            var wanted = (streetDraft,start, StreetPoint(net,streetSnap), streetStyle);
            if (wanted == streetPreviewBuilt) return;
            streetPreviewBuilt = wanted;
            if(streetPreviewSource!=null){streetPreviewSource.ShowCommittedGeometry(true);game?.valley.EndBuiltSurfacePreview();streetPreviewSource=null;}
            var g=streetDraft;
            if(g==null)
            {
                g=new PublicRoadNetwork();
                g.AddSegment(g.AddNode(start),g.AddNode(StreetPoint(net,streetSnap)),streetStyle,RoadStructure.Ground);
            }
            var partition=streetDraft!=null?new PublicRoadPreviewPartition(net,g):null;
            if(partition!=null)
            {
                if(streetUnchangedPreview==null)
                {
                    var go=new GameObject("Unchanged street preview") {hideFlags=HideFlags.DontSave};
                    go.transform.SetParent(streetPreview.transform.parent,false);go.SetActive(false);
                    streetUnchangedPreview=go.AddComponent<PublicRoadsView>();streetUnchangedPreview.constructionPreview=true;
                    Dress(streetUnchangedPreview);streetUnchangedPreview.level=streetPreview.level;
                }
                streetUnchangedPreview.gameObject.SetActive(true);
                if(streetUnchangedPreview.Geometry==null || streetUnchangedSource!=net ||
                    !partition.unchangedSegments.SetEquals(streetUnchangedSegments) || !partition.unchangedJunctions.SetEquals(streetUnchangedJunctions))
                {
                    streetUnchangedSource=net;streetUnchangedSegments=partition.unchangedSegments;streetUnchangedJunctions=partition.unchangedJunctions;
                    streetUnchangedPreview.network=g;streetUnchangedPreview.Rebuild(streetUnchangedSegments,streetUnchangedJunctions,game!=null?game.valley.RoadGeometry:Streets.Geometry);
                }
            }
            else if(streetUnchangedPreview!=null)streetUnchangedPreview.gameObject.SetActive(false);
            streetPreview.network = g;
            streetPreview.Rebuild(partition?.changedSegments,partition?.changedJunctions,partition!=null?streetUnchangedPreview.Geometry:null);
            streetOutline.Clear();
            float reach=PublicRoadGeometry.CurveRadius(streetStyle)*2f+RoadTypes.Of(streetStyle).FullWidth;
            foreach(var edge in MeshBoundary.Edges(streetPreview.Geometry.pavement))
            {
                var middle=Geo.XZ((edge.a+edge.b)*.5f);
                if(CareerConstruction.Distance(middle,Geo.XZ(start),streetSnap.p)<RoadTypes.Of(streetStyle).FullWidth ||
                    Vector2.Distance(middle,Geo.XZ(start))<reach)streetOutline.Add(edge);
            }
            if(streetDraft!=null)
            {
                streetPreviewSource=Streets;streetPreviewSource.ShowCommittedGeometry(false);
                game?.valley.PreviewBuiltSurfaces(null,Matrix4x4.identity,streetPreview.Geometry,streetUnchangedPreview?.Geometry);
            }
        }

        void HideStreetPreview()
        {
            if(streetPreviewSource!=null){streetPreviewSource.ShowCommittedGeometry(true);game?.valley.EndBuiltSurfacePreview();streetPreviewSource=null;}
            streetOutline.Clear();
            if (streetPreview != null) streetPreview.gameObject.SetActive(false);
            if(streetUnchangedPreview!=null)streetUnchangedPreview.gameObject.SetActive(false);
            streetUnchangedSource=null;streetUnchangedSegments=streetUnchangedJunctions=null;
            streetPreviewBuilt = default;
        }

        void DestroyStreetPreview()
        {
            if(streetStructureMaterial!=null)
            {if(Application.isPlaying)Destroy(streetStructureMaterial);else DestroyImmediate(streetStructureMaterial);streetStructureMaterial=null;}
            HideStreetPreview();
            if(streetUnchangedPreview!=null)
            {
                if(Application.isPlaying)Destroy(streetUnchangedPreview.gameObject);else DestroyImmediate(streetUnchangedPreview.gameObject);
                streetUnchangedPreview=null;
            }
            if (streetPreview == null) return;
            if (Application.isPlaying) Destroy(streetPreview.gameObject); else DestroyImmediate(streetPreview.gameObject);
            streetPreview = null;
        }

        /// <summary>The street tool's ring where the next point goes (DrawOutlines).</summary>
        void DrawStreetMarker(Vector3 point)
        {
            if (tool != DevTool.Street || !streetSnapShown) return;
            if (streetChainPoint is Vector2 from && Vector2.Distance(from, streetSnap.p) > .5f)
            {
                var colour=streetProblem!=null?DevGui.Danger:DevGui.Accent;
                foreach(var edge in streetOutline)
                {
                    loop.Clear();
                    var offset=Vector3.up*(streetPreview.level-.18f);
                    loop.Add(streetPreview.transform.TransformPoint(edge.a+offset));loop.Add(streetPreview.transform.TransformPoint(edge.b+offset));
                    outlines.Line(cam,loop,colour,1.5f,false,true);
                }
            }
            if (game == null) Marker(streetSnap.p, streetSnap.what != null, streetProblem != null, point);
            else
            {
                float radius = MetresPerPixel(point) * (streetSnap.what != null ? 9f : 6f);
                loop.Clear();
                for (int i = 0; i < 16; i++)
                {
                    float angle = i / 16f * Mathf.PI * 2f;
                    var p = streetSnap.p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    loop.Add(Geo.X0Z(p,StreetEndpoint.y));
                }
                outlines.Line(cam, loop, streetProblem != null ? DevGui.Danger : DevGui.Accent, 2f,true,true);
            }
        }
    }
}
