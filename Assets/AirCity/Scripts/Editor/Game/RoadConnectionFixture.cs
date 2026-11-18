using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirCity.Airfield;
using AirCity.Dev;
using AirCity.Game;
using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Game
{
    /// <summary>
    /// A controlled region for the bridge and tunnel connection flows: rolling ground (±1.2 m, about 4% slopes) away
    /// from a flat airport, a hill to tunnel under, a ridge beside a valley, and the existing roads each flow starts
    /// from or finishes on (ground streets, two bridges, three tunnels, airside service roads either side of a
    /// taxiway). World coordinates; the airport's origin is (0, 100, 0). Used by RoadConnection tests and the
    /// connection Play review, so both drive the same layout.
    /// </summary>
    public static class RoadConnectionFixture
    {
        public static readonly Vector2 HillCentre=new Vector2(-1500,-900),RidgeCentre=new Vector2(-1300,-500),ValleyCentre=new Vector2(-1080,-500);
        public const float BridgeLevel=110f,TunnelLevel=88f,RidgeTunnelLevel=105f;
        // Flow a: a bridge from R1 over R3 onto R2, along z = -1300.
        public static readonly Vector2 R1A=new Vector2(-1700,-1500),R1B=new Vector2(-1700,-1100);
        public static readonly Vector2 R3A=new Vector2(-1500,-1500),R3B=new Vector2(-1500,-1100);
        public static readonly Vector2 R2A=new Vector2(-1300,-1500),R2B=new Vector2(-1300,-1100);
        // Flow b: bridge B1 along z = -1700 and bridge B2 across its line at x = -1300, both at BridgeLevel.
        public static readonly Vector2 B1A=new Vector2(-1700,-1700),B1B=new Vector2(-1500,-1700);
        public static readonly Vector2 B2A=new Vector2(-1300,-1800),B2B=new Vector2(-1300,-1600);
        // Flow c: a tunnel under the hill from R4 to R5, along z = -900.
        public static readonly Vector2 R4A=new Vector2(-1750,-1100),R4B=new Vector2(-1750,-700);
        public static readonly Vector2 R5A=new Vector2(-1250,-1100),R5B=new Vector2(-1250,-700);
        // Flow d: tunnel T1 joins tunnel T2 (z = -500); tunnel T3 ends inside the ridge above the valley.
        public static readonly Vector2 T1A=new Vector2(-1950,-500),T1B=new Vector2(-1800,-500);
        public static readonly Vector2 T2A=new Vector2(-1650,-600),T2B=new Vector2(-1650,-400);
        public static readonly Vector2 T3A=new Vector2(-1340,-500),T3B=new Vector2(-1260,-500);
        // Flow e (airport-local): service roads S1 and S2 either side of taxiway TW.
        public static readonly Vector2 S1A=new Vector2(-200,-200),S1B=new Vector2(-200,200),S2A=new Vector2(200,-200),S2B=new Vector2(200,200);
        public static readonly Vector2 TWA=new Vector2(0,-300),TWB=new Vector2(0,300);

        static float G(float dx,float dz,float sx,float sz)=>Mathf.Exp(-(dx/sx)*(dx/sx)-(dz/sz)*(dz/sz));
        public static float Land(float x,float z)
        {
            float flat=Mathf.Clamp01((Mathf.Sqrt(x*x+z*z)-650f)/250f);
            return 100f+1.2f*Mathf.Sin((x+z)/40f)*flat+30f*G(x-HillCentre.x,z-HillCentre.y,90,90)
                +25f*G(x-RidgeCentre.x,z-RidgeCentre.y,70,70)-20f*G(x-ValleyCentre.x,z-ValleyCentre.y,50,200);
        }
        public static float Land(Vector2 p)=>Land(p.x,p.y);

        /// <summary>A section the way the Street tool draws one: a smooth constant-grade profile.</summary>
        static int Section(PublicRoadNetwork graph,Vector3 a,Vector3 b,RoadStructure structure)
        {
            int na=graph.AddNode(a),nb=graph.AddNode(b);var edge=graph.AddSegment(na,nb,PublicRoadStyle.Urban,structure);
            edge.alignment=RoadBuildSettings.Default.Shape(a,b);return edge.id;
        }
        static Vector3 OnLand(Vector2 p)=>new Vector3(p.x,Land(p),p.y);
        static Vector3 At(Vector2 p,float y)=>new Vector3(p.x,y,p.y);

        public static PublicRoadNetwork Roads()
        {
            var graph=new PublicRoadNetwork();
            foreach(var (a,b) in new[]{(R1A,R1B),(R2A,R2B),(R3A,R3B),(R4A,R4B),(R5A,R5B)})Section(graph,OnLand(a),OnLand(b),RoadStructure.Ground);
            Section(graph,At(B1A,BridgeLevel),At(B1B,BridgeLevel),RoadStructure.Elevated);
            Section(graph,At(B2A,BridgeLevel),At(B2B,BridgeLevel),RoadStructure.Elevated);
            Section(graph,At(T1A,TunnelLevel),At(T1B,TunnelLevel),RoadStructure.Tunnel);
            Section(graph,At(T2A,TunnelLevel),At(T2B,TunnelLevel),RoadStructure.Tunnel);
            Section(graph,At(T3A,RidgeTunnelLevel),At(T3B,RidgeTunnelLevel),RoadStructure.Tunnel);
            return graph;
        }

        // ------------------------------------------------------------------ water variant
        // Flat land at 100 m (low banks, 1 m above the water) with the sea at 99 m. A river at x = -1500 (also mapped
        // as inland water, 1.4 m deep with 25 m shallow margins), an ocean channel at x = -850 (17 m deep, shallow
        // margins, its west bank only 0.4 m above the water) and a canal through the airport (x = 0, 1.4 m deep).
        public const float WaterLevel=99f,LowBank=99.4f,WaterBridgeLevel=104.5f;
        public const float RiverX=-1500f,ChannelX=-850f;
        // River: streets on both banks (RW, RE) and further back for a tunnel's ramps (RW2, RE2); bridge BW stands
        // over the river, its east end mid-stream. Channel: streets on the east bank (OE, OE2), on the low west bank
        // 50 m from the water (OW) and at the water's edge (OWE). Airside: service roads S1 and S2 either side of the canal.
        public static readonly Vector2 RWA=new Vector2(-1600,-1900),RWB=new Vector2(-1600,-1100),REA=new Vector2(-1400,-1900),REB=new Vector2(-1400,-1100);
        public static readonly Vector2 RW2A=new Vector2(-1750,-1100),RW2B=new Vector2(-1750,-900),RE2A=new Vector2(-1250,-1100),RE2B=new Vector2(-1250,-900);
        public static readonly Vector2 BWA=new Vector2(-1560,-1700),BWB=new Vector2(-1480,-1700);
        public static readonly Vector2 OEA=new Vector2(-650,-1500),OEB=new Vector2(-650,-1100),OE2A=new Vector2(-450,-1100),OE2B=new Vector2(-450,-900);
        public static readonly Vector2 OWA=new Vector2(-1075,-1500),OWB=new Vector2(-1075,-1100),OWEA=new Vector2(-1040,-800),OWEB=new Vector2(-1040,-600);
        static float Piecewise(float d,params float[] knots)
        {
            // knots: distance, height pairs, increasing distance; flat beyond either end
            if(d<=knots[0])return knots[1];
            for(int i=2;i<knots.Length;i+=2)if(d<=knots[i])return Mathf.Lerp(knots[i-1],knots[i+1],(d-knots[i-2])/(knots[i]-knots[i-2]));
            return knots[knots.Length-1];
        }
        public static float WaterLand(float x,float z)
        {
            float river=Piecewise(Mathf.Abs(x-RiverX),37.5f,97.6f,62.5f,99f,75f,100f);
            float canal=Mathf.Abs(z)<600f?Piecewise(Mathf.Abs(x),25f,97.6f,37.5f,99f,50f,100f):100f;
            float d=x-ChannelX;
            float channel=d>=0?Piecewise(d,100f,82f,125f,98f,150f,99f,162.5f,100f):Piecewise(-d,100f,82f,125f,98f,175f,99f,187.5f,LowBank,262.5f,LowBank,300f,100f);
            return Mathf.Min(river,Mathf.Min(canal,channel));
        }
        public static float WaterLand(Vector2 p)=>WaterLand(p.x,p.y);
        static Vector3 OnWaterLand(Vector2 p)=>new Vector3(p.x,WaterLand(p),p.y);
        public static PublicRoadNetwork WaterRoads()
        {
            var graph=new PublicRoadNetwork();
            foreach(var (a,b) in new[]{(RWA,RWB),(REA,REB),(RW2A,RW2B),(RE2A,RE2B),(OEA,OEB),(OE2A,OE2B),(OWA,OWB),(OWEA,OWEB)})Section(graph,OnWaterLand(a),OnWaterLand(b),RoadStructure.Ground);
            Section(graph,At(BWA,WaterBridgeLevel),At(BWB,WaterBridgeLevel),RoadStructure.Elevated);
            return graph;
        }
        /// <summary>The water variant of State: the same region opened for building, with the water landscape and roads.</summary>
        public static CareerState WaterState()
        {
            var state=State();var world=state.world;
            world.hasOcean=true;world.seaLevel=WaterLevel;world.waterHeights=new float[world.heights.Length];
            for(int z=0;z<ValleyWorld.Resolution;z++)for(int x=0;x<ValleyWorld.Resolution;x++)
            {
                float px=x*world.Spacing-ValleyWorld.Size*.5f,pz=z*world.Spacing-ValleyWorld.Size*.5f;int i=z*ValleyWorld.Resolution+x;
                world.heights[i]=WaterLand(px,pz);
                if(Mathf.Abs(px-RiverX)<80f || Mathf.Abs(px)<60f&&Mathf.Abs(pz)<600f)world.waterHeights[i]=WaterLevel;
            }
            world.roadHeights=world.publicRoad.Select(WaterLand).ToArray();
            var roads=PublicRoadNetwork.FromWorld(world);
            var fixture=WaterRoads();var map=new Dictionary<int,int>();
            foreach(var n in fixture.nodes)map[n.id]=roads.AddNode(n.position);
            foreach(var e in fixture.segments)roads.AddSegment(map[e.a],map[e.b],e.style,e.structure).alignment=e.alignment;
            state.publicRoads=roads;
            return state;
        }
        /// <summary>The water variant's airside part: service roads S1 and S2 either side of the canal.</summary>
        public static void AddWaterAirside(CareerGame game)
        {
            var net=game.Airport.Taxiways;
            foreach(var (a,b) in new[]{(S1A,S1B),(S2A,S2B)})
                net.Connect(net.AddNode(new Vector3(a.x,0,a.y)),net.AddNode(new Vector3(b.x,0,b.y)),RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Rebuild();game.SharedConstructionChanged();
        }
        /// <summary>A new game on the water variant.</summary>
        public static CareerGame CreateWater(List<GameObject> spawned)
        {
            var before=new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            try
            {
                var game=NewGameSetup.Build();
                game.Initialize(WaterState(),false);game.Paused=true;
                AddWaterAirside(game);
                return game;
            }
            finally{foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())if(!before.Contains(go))spawned.Add(go);}
        }

        /// <summary>The fixture region with its streets, bridges and tunnels, opened for building under Sandbox rules
        /// (free construction, every tool).</summary>
        public static CareerState State(Action<PublicRoadNetwork> more=null)
        {
            var state=CareerState.New(19770616,preset:RegionPreset.Woodland);var world=state.world;
            world.trees.Clear();world.hasOcean=world.hasRiver=false;world.waterHeights=new float[world.heights.Length];
            world.landscapeSource="Road connection fixture";world.hasCoordinates=false;world.latitude=world.longitude=0d;world.elevationSha256=world.coverSha256=null;
            for(int z=0;z<ValleyWorld.Resolution;z++)for(int x=0;x<ValleyWorld.Resolution;x++)
                world.heights[z*ValleyWorld.Resolution+x]=Land(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f);
            world.publicRoad=new List<Vector2>{new Vector2(2000f,-3200f),new Vector2(2000f,3200f)};
            world.roadHeights=world.publicRoad.Select(Land).ToArray();
            foreach(var site in world.sites){site.centre=Vector2.zero;site.elevation=100f;site.name="Connection fixture";}
            var roads=PublicRoadNetwork.FromWorld(world);
            var fixture=Roads();more?.Invoke(fixture);var map=new Dictionary<int,int>();
            foreach(var n in fixture.nodes)map[n.id]=roads.AddNode(n.position);
            foreach(var e in fixture.segments)roads.AddSegment(map[e.a],map[e.b],e.style,e.structure).alignment=e.alignment;
            state.publicRoads=roads;
            if(!state.StartRegion(out string why))throw new InvalidOperationException(why);
            state.mode=GameMode.Sandbox;state.funds=10000000;
            foreach(CareerTool tool in Enum.GetValues(typeof(CareerTool)))state.layout.Unlock(tool);
            return state;
        }

        /// <summary>The airside part: taxiway TW with service roads S1 and S2 either side of it.</summary>
        public static void AddAirside(CareerGame game)
        {
            var net=game.Airport.Taxiways;
            net.Connect(net.AddNode(new Vector3(TWA.x,0,TWA.y)),net.AddNode(new Vector3(TWB.x,0,TWB.y)),23,PavementSurface.Asphalt);
            foreach(var (a,b) in new[]{(S1A,S1B),(S2A,S2B)})
                net.Connect(net.AddNode(new Vector3(a.x,0,a.y)),net.AddNode(new Vector3(b.x,0,b.y)),RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Rebuild();game.SharedConstructionChanged();
        }

        /// <summary>A new game on the fixture (State, AddAirside). Every root object it creates is listed in `spawned`
        /// for the caller to destroy.</summary>
        public static CareerGame Create(List<GameObject> spawned)
        {
            var before=new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            try
            {
                var game=NewGameSetup.Build();
                game.Initialize(State(),false);game.Paused=true;
                AddAirside(game);
                return game;
            }
            finally{foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())if(!before.Contains(go))spawned.Add(go);}
        }
    }

    /// <summary>
    /// Drives the shared Street and Road tools one pointer frame at a time, headlessly, through the same steps
    /// DevAirfieldTool.Update takes: the datum/underground update, the cursor ray (Ground: decks first, then the
    /// raised or underground working level, then terrain), then the line tool with that point. The camera is put
    /// over each aimed point at a typical play distance with about 0.4 m per pixel, so snapping reaches are the
    /// game's. Only private methods that exist in every version of the tool are used, so the same driver reports
    /// the original behaviour too.
    /// </summary>
    public sealed class RoadToolDriver : IDisposable
    {
        const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        public readonly CareerGame game;
        public readonly DevAirfieldTool tool;
        readonly Camera cam;
        readonly Mouse mouse;
        readonly Keyboard keyboard;
        public int Clicks {get;private set;}
        public RoadToolDriver(CareerGame game)
        {
            this.game=game;tool=game.BuildTools;cam=game.view.GetComponent<Camera>();
            typeof(DevAirfieldTool).GetField("cam",Any).SetValue(tool,cam);
            mouse=InputSystem.AddDevice<Mouse>("Road connection driver mouse");keyboard=InputSystem.AddDevice<Keyboard>("Road connection driver keyboard");
            mouse.MakeCurrent();keyboard.MakeCurrent();
        }
        public void Dispose()
        {
            if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
        }
        object Call(string name,params object[] args)=>typeof(DevAirfieldTool).GetMethod(name,Any).Invoke(tool,args);
        object Get(string name)=>typeof(DevAirfieldTool).GetField(name,Any).GetValue(tool);
        void Set(string name,object value)=>typeof(DevAirfieldTool).GetField(name,Any).SetValue(tool,value);
        bool Street=>tool.Tool==DevTool.Street;
        Vector3 Origin=>Street?Vector3.zero:game.Airport.transform.position;

        Vector3 LastAim;
        void Aim(Vector3 world)
        {
            LastAim=world;
            cam.transform.position=world+new Vector3(0,220,-100);cam.transform.LookAt(world);
            float distance=Vector3.Distance(cam.transform.position,world);
            cam.fieldOfView=2f*Mathf.Atan(.4f*Mathf.Max(Screen.height,1)/(2f*distance))*Mathf.Rad2Deg;
        }
        /// <summary>One frame with the cursor over a world point.</summary>
        public void Frame(Vector3 aim,bool click)
        {
            // Play mode syncs the terrain collider every physics step; outside it a terrain ray misses until synced.
            Physics.SyncTransforms();
            Aim(aim);var screen=(Vector2)cam.WorldToScreenPoint(aim);
            Call("RoadElevationKeys",keyboard);
            var args=new object[]{screen,null};bool on=(bool)Call("Ground",args);var point=(Vector3)args[1];
            if(on)Set("lastGround",point);else point=(Vector3)Get("lastGround");
            if(click)Clicks++;
            if(Street)Call("StreetTool",mouse,keyboard,click,on,point);
            else Call("TaxiwayTool",click,on,point,tool.Tool==DevTool.Road);
        }
        /// <summary>Hover a free spot at (x, z): the cursor is moved until the marker ring, drawn at the working
        /// level, sits over it (as a player does).</summary>
        public void HoverFree(Vector2 world)
        {
            float y=Street?game.valley.DisplayHeight(world):Origin.y;
            for(int i=0;i<3;i++){Frame(new Vector3(world.x,y,world.y),false);y=Endpoint.y;}
        }
        /// <summary>Hover the visible surface of an existing road at (x, z) and height y (world).</summary>
        /// <summary>Hover an existing road at (x, z), height y (world). Three frames: the underground view's cut follows
        /// the cursor, so what is hidden settles over a frame or two, as it does under a moving mouse.</summary>
        public void HoverRoad(Vector2 world,float y){for(int i=0;i<3;i++)Frame(RoadAim(world,y),false);}
        /// <summary>Where a player points for a road: its surface (in the underground view the roads the cut hides are
        /// outlined there since this change; before it, nothing showed them).</summary>
        static Vector3 RoadAim(Vector2 world,float y)=>new Vector3(world.x,y+.2f,world.y);
        public void ClickFree(Vector2 world){HoverFree(world);Frame(LastAim,true);}
        public void ClickRoad(Vector2 world,float y){HoverRoad(world,y);Frame(RoadAim(world,y),true);}
        public void EndChain(){if(Street)Call("EndStreet");else Set("chainNode",-1);}

        public string Snap=>Street?tool.StreetSnapName:tool.SnapText;
        public string Problem=>Street?tool.StreetProblem:tool.RoadProblem;
        public Vector3 Endpoint=>Street?tool.StreetEndpoint:tool.AirsideEndpoint;
        public int Chain=>(int)Get(Street?"streetChain":"chainNode");
        public float? Datum=>(float?)Get("roadDatum");
        public bool Underground=>tool.RoadUnderground;
        public string Describe()=>$"snap={(Snap??"none")}; endpoint y={Endpoint.y:0.0}; elevation={tool.RoadElevation:+0.0;-0.0;0.0}; problem={(string.IsNullOrEmpty(Problem)?"none":Problem)}";
    }
}
