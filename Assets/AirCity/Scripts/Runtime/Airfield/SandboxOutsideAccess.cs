using AirCity.Airfield.Building;
using AirCity.Game;
using AirCity.Vehicles;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AirCity.Airfield
{
    /// <summary>The sandbox's authored supplier portal. Career airports earn their own public-road access.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Airport))]
    public sealed class SandboxOutsideAccess : MonoBehaviour
    {
        public const string ScenePath="Assets/AirCity/Scenes/Sandbox.unity";
        public static readonly Vector2 DefaultJunction=new Vector2(100f,-20f);
        public static readonly Vector2 DefaultPortal=new Vector2(100f,-700f);
        [SerializeField, HideInInspector] int outsideNodeId;
        [SerializeField, HideInInspector] bool defaultPortal;
        AirfieldPart markings;
        public string Problem { get; private set; }
        public int OutsideNodeId => outsideNodeId;
        public Mesh MarkerMesh => markings?.Mesh;
        public Vector2 Position => GetComponent<Airport>().Taxiways.Node(outsideNodeId)!=null ? GetComponent<Airport>().Taxiways.At(outsideNodeId) : DefaultPortal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBootstrap() => SceneManager.sceneLoaded-=SceneLoaded;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded-=SceneLoaded; SceneManager.sceneLoaded+=SceneLoaded;
            for(int i=0;i<SceneManager.sceneCount;i++) InstallInScene(SceneManager.GetSceneAt(i));
        }
        static void SceneLoaded(Scene scene,LoadSceneMode mode) => InstallInScene(scene);
        static void InstallInScene(Scene scene)
        {
            if(!IsSandboxScene(scene)) return;
            foreach(var root in scene.GetRootGameObjects())
                foreach(var airport in root.GetComponentsInChildren<Airport>(true)) Install(airport);
        }
        public static bool IsSandboxScene(Scene scene) => scene.IsValid() && scene.path==ScenePath;
        public static SandboxOutsideAccess Install(Airport airport)
        {
            if(airport==null || airport.GetComponentInParent<CareerGame>()!=null) return null;
            var access=airport.GetComponent<SandboxOutsideAccess>();
            if(access==null) access=airport.gameObject.AddComponent<SandboxOutsideAccess>();
            access.EnsureConnection(out _); return access;
        }
        void OnEnable() { if(Application.isPlaying) EnsureConnection(out _); }
        void OnDisable() { markings?.Destroy(); markings=null; }

        /// <summary>Adds only the missing sandbox spur. Existing roads, equipment and valid authored gates stay intact.</summary>
        public bool EnsureConnection(out string problem)
        {
            var airport=GetComponent<Airport>(); var service=airport.Deicing; var net=airport.Taxiways;
            problem=airport.GetComponentInParent<CareerGame>()!=null?"career airports require their own public-road connection":null;
            if(problem!=null) { Problem=problem; return false; }
            foreach(var gate in service.OutsideAccess)
                if(gate!=null && net.Node(gate.nodeId)!=null && Vector2.Distance(net.At(gate.nodeId),DefaultPortal)<1f)
                {
                    outsideNodeId=gate.nodeId; defaultPortal=true; EnsurePresentation(airport);
                    Problem=problem=service.OutsideAccessProblem(gate.nodeId); return problem==null;
                }

            TaxiwayEdge source=null; Vector2 junction=default; float distance=250f;
            foreach(var edge in net.edges)
            {
                if(edge.road!=RoadClass.Service || !edge.surface.IsPaved() || TaxiwayNetwork.Closed(edge) || edge.wear>=RunwayRules.ClosedWear) continue;
                var near=Geo.ClosestOnSegment(DefaultJunction,net.At(edge.a),net.At(edge.b),out _);
                float d=Vector2.Distance(near,DefaultJunction); if(d>distance) continue;
                distance=d; source=edge; junction=near;
            }
            if(source==null) { Problem=problem="the sandbox needs an open service road near its southern apron"; return false; }
            int selectedEnd=Vector2.Distance(junction,net.At(source.a))<.1f?source.a:Vector2.Distance(junction,net.At(source.b))<.1f?source.b:0;
            if(selectedEnd!=0 && service.IsOutsideAccess(selectedEnd))
            {
                // Branch inside the road instead of turning the player's existing outside leaf into a junction.
                var inward=(net.At(TaxiwayNetwork.Other(source,selectedEnd))-net.At(selectedEnd)).normalized;
                junction=net.At(selectedEnd)+inward*Mathf.Min(20f,net.EdgeLength(source)*.5f);
            }
            var axis=(junction-DefaultPortal).normalized;
            var right=Geo.Right(axis)*RunwayRules.RoadWidth*.5f;
            var corridor=new[] { DefaultPortal+right,junction+right,junction-right,DefaultPortal-right };
            foreach(var runway in airport.Runways)
            {
                var a=Geo.XZ(runway.start); var b=Geo.XZ(runway.end); var r=Geo.Right(runway.Axis2D)*runway.HalfWidthTotal;
                if(Geo.Overlap(corridor,new[] { a+r,b+r,b-r,a-r }))
                { Problem=problem="the sandbox outside-road corridor is occupied by a runway"; return false; }
            }
            foreach(var layout in net.Layouts()) if(Geo.Overlap(corridor,layout.Pad))
            { Problem=problem="the sandbox outside-road corridor is occupied by an aircraft pad"; return false; }
            foreach(var terminal in airport.terminals) foreach(var footprint in terminal.Footprints()) if(Geo.Overlap(corridor,footprint))
            { Problem=problem="the sandbox outside-road corridor is occupied by a terminal"; return false; }
            foreach(var area in airport.deicingWorkAreas) if(Geo.Overlap(corridor,area.Outline))
            { Problem=problem="the sandbox outside-road corridor is occupied by a supply area"; return false; }
            foreach(var bay in airport.deicingParkingBays) if(Geo.Overlap(corridor,bay.Outline))
            { Problem=problem="the sandbox outside-road corridor is occupied by truck parking"; return false; }
            foreach(var depot in airport.Depots) if(Geo.Overlap(corridor,depot.Corners))
            { Problem=problem="the sandbox outside-road corridor is occupied by a depot"; return false; }
            var box=AirportMaintenance.Box.Of((DefaultPortal+junction)*.5f,axis,
                new Bounds(Vector3.zero,new Vector3(RunwayRules.RoadWidth,1f,Vector2.Distance(DefaultPortal,junction))),0f);
            foreach(var depot in airport.GetComponentsInChildren<BaggageDepot>()) if(box.Overlaps(depot.BuildingBox))
            { Problem=problem="the sandbox outside-road corridor is occupied by a baggage depot"; return false; }

            int start=Vector2.Distance(junction,net.At(source.a))<.1f?source.a:Vector2.Distance(junction,net.At(source.b))<.1f?source.b:net.SplitEdge(source,Geo.X0Z(junction));
            outsideNodeId=net.AddNode(Geo.X0Z(DefaultPortal));
            net.Connect(outsideNodeId,start,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            float heading=Mathf.Atan2(axis.x,axis.y)*Mathf.Rad2Deg;
            airport.aprons.Add(ApronArea.Rect(DefaultPortal+axis*15f,new Vector2(22f,34f),heading,PavementSurface.Asphalt));
            defaultPortal=true;
            if(service.AddOutsideAccess(outsideNodeId,out problem)==null) { Problem=problem; return false; }
            net.Rebuild(); airport.BuildGround(); EnsurePresentation(airport); Problem=null; return true;
        }
        void EnsurePresentation(Airport airport)
        {
            if(airport.GetComponent<DeicingRig>()==null) airport.gameObject.AddComponent<DeicingRig>();
            var net=airport.Taxiways; TaxiwayEdge road=null;
            foreach(var edge in net.EdgesAt(outsideNodeId)) if(edge.road==RoadClass.Service) { road=edge; break; }
            if(road==null) return;
            var at=net.At(outsideNodeId); var inward=(net.At(TaxiwayNetwork.Other(road,outsideNodeId))-at).normalized;
            var right=Geo.Right(inward); var paint=new PaintBuilder();
            float height=defaultPortal?3f:1.1f, pitch=defaultPortal?2f:.8f;
            paint.Text(at+inward*8f,inward,"OUTSIDE",pitch,PaintBuilder.White,height);
            if(defaultPortal) paint.Text(at+inward*15f,inward,"SUPPLIES",1.6f,PaintBuilder.Yellow,2f);
            paint.Line(at-right*road.width*.5f,at+right*road.width*.5f,.3f,PaintBuilder.Yellow);
            markings??=new AirfieldPart(transform,"Sandbox outside connection markings");
            markings.Set(paint.Apply(markings.Target,"Outside supplier portal"),airport.paint);
        }
    }
}
