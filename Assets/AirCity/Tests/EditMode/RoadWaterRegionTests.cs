using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    /// <summary>
    /// Bank-to-bank crossings on generated regions (real lakes: sloping banks met at an angle, the shore band
    /// IsWater keeps off the waterline, lakes standing above their shore), at the plan level: a ground street stub on
    /// each bank, then one click from stub end to stub end in Automatic and in Bridge. Each either builds a clean
    /// crossing (no ground road or bore over the water, the deck WaterClearance over it, joined at both ends, bridge
    /// ends on dry land), or is refused for a reason that is true and can be acted on in the mode the player is in.
    /// </summary>
    public sealed class RoadWaterRegionTests
    {
        static readonly Dictionary<(int,RegionPreset),ValleyWorld> worlds=new Dictionary<(int,RegionPreset),ValleyWorld>();
        static ValleyWorld World(int seed,RegionPreset preset)
        {
            if(!worlds.TryGetValue((seed,preset),out var world))worlds[(seed,preset)]=world=ValleyWorld.Generate(seed,preset);
            return world;
        }
        static Vector3 On(ValleyWorld w,Vector2 p)=>new Vector3(p.x,w.Height(p),p.y);

        /// <summary>Stubs a0 → a1 and b1 → b0 (50 m each, on the line through a1 and b1), then a1 → b1 in `mode`.
        /// Null when it builds clean; otherwise what was wrong or the refusal.</summary>
        static string Cross(ValleyWorld w,Vector2 a1,Vector2 b1,RoadBuildMode mode,out bool built)
        {
            built=false;var dir=(b1-a1).normalized;Vector2 a0=a1-dir*50f,b0=b1+dir*50f;
            var state=new CareerState {mode=GameMode.Sandbox,funds=0,wholeRegion=true,handsOn=true,selectedSite=0,layout=new CareerLayout(),publicRoads=new PublicRoadNetwork(),world=w};
            var g=new PublicRoadNetwork();
            int na0=g.AddNode(On(w,a0)),na1=g.AddNode(On(w,a1)),nb1=g.AddNode(On(w,b1)),nb0=g.AddNode(On(w,b0));
            g.AddSegment(na0,na1,PublicRoadStyle.Urban,RoadStructure.Ground).alignment=RoadBuildSettings.Default.Shape(On(w,a0),On(w,a1));
            g.AddSegment(nb1,nb0,PublicRoadStyle.Urban,RoadStructure.Ground).alignment=RoadBuildSettings.Default.Shape(On(w,b1),On(w,b0));
            var old=new HashSet<int>(g.segments.Select(e=>e.id));
            if(!RoadConstructionPlan.TryCreate(state,g,na1,g.Node(nb1).position,nb1,-1,PublicRoadStyle.Urban,new RoadBuildSettings {mode=mode,smooth=true},out var plan,out var why))
                return "refused: "+why;
            if(!plan.TryCommit(state,g,out var next,out why))return "commit refused: "+why;
            built=true;var made=next.segments.Where(e=>!old.Contains(e.id)).ToList();var wrong=new List<string>();
            foreach(var e in made)
            {
                var path=next.Path(e);
                for(int i=0;i<=24;i++)
                {
                    var p=path.Position(i/24f);var at=Alignment3D.XZ(p);if(!w.IsWater(at))continue;
                    if(e.structure==RoadStructure.Ground)wrong.Add("ground road over water");
                    else if(e.structure==RoadStructure.Tunnel)wrong.Add("bore under water");
                    else if(p.y<w.WaterSurfaceAt(at)+RoadConstructionPlan.WaterClearance-RoadConstructionPlan.WaterTolerance-.01f)wrong.Add($"deck {p.y-w.WaterSurfaceAt(at):0.00} m over water");
                }
                if(path.MaximumGrade>PublicRoadConstruction.MaximumGroundGrade+.001f)wrong.Add($"grade {path.MaximumGrade*100f:0} %");
            }
            if(!made.Any(e=>e.structure==RoadStructure.Elevated))wrong.Add("no bridge");
            if(!next.At(na1).Any(e=>!old.Contains(e.id))||!next.At(nb1).Any(e=>!old.Contains(e.id))||!next.HasRoute(na0,nb0))wrong.Add("not joined");
            foreach(var n in next.nodes)
            {
                var arms=next.At(n.id).ToList();
                if(arms.Any(e=>e.structure==RoadStructure.Ground)&&arms.Any(e=>e.structure!=RoadStructure.Ground)&&w.IsWater(Alignment3D.XZ(n.position)))
                    wrong.Add("ramp joint in the water");
            }
            return wrong.Count==0?null:string.Join(", ",wrong.Distinct());
        }

        /// <summary>The nine crossings the round-1 rule refused with advice for another mode, and the low-bank one
        /// it refused rightly (0.7 m bank): stub ends a1 and b1.</summary>
        [TestCase(RegionPreset.Woodland,42,160f,-1912f,160f,-1644f,TestName="Woodland lake 144 m, shore met at an angle")]
        [TestCase(RegionPreset.Woodland,42,280f,-1392f,280f,-764f,TestName="Woodland lake 504 m, banks 1.8 m, stub grades sag")]
        [TestCase(RegionPreset.Mountains,42,1360f,480f,1360f,668f,TestName="Mountain lake 64 m, 0.9 m bank met at an angle")]
        [TestCase(RegionPreset.Mountains,42,-560f,160f,-412f,160f,TestName="Mountain shore band 24 m, banks at the water level")]
        [TestCase(RegionPreset.Mountains,42,188f,640f,348f,640f,TestName="Mountain shore band 36 m, banks at the water level")]
        [TestCase(RegionPreset.Desert,692835,-2960f,1740f,-2960f,1900f,TestName="Desert lake 36 m standing 2 m above one bank")]
        [TestCase(RegionPreset.Desert,692835,852f,-1280f,1048f,-1280f,TestName="Desert lake 72 m standing 3 m above one bank")]
        [TestCase(RegionPreset.Desert,692835,-2880f,2320f,-2604f,2320f,TestName="Desert lake 152 m, a rise at the shore")]
        [TestCase(RegionPreset.Desert,692835,-2092f,1120f,-1896f,1120f,TestName="Desert lake 72 m, 0.7 m bank")]
        public void BankToBankAcrossALakeBuildsInAutomaticAndBridge(RegionPreset preset,int seed,float ax,float az,float bx,float bz)
        {
            var w=World(seed,preset);Vector2 a=new Vector2(ax,az),b=new Vector2(bx,bz);
            Assume.That(!w.IsWater(a)&&!w.IsWater(b)&&Enumerable.Range(1,19).Any(i=>w.IsWater(Vector2.Lerp(a,b,i/20f))),"The generated region no longer has this lake.");
            foreach(var mode in new[]{RoadBuildMode.Automatic,RoadBuildMode.Bridge})
            {
                string wrong=Cross(w,a,b,mode,out bool built);
                Assert.IsTrue(built,$"{mode}: {wrong}");Assert.IsNull(wrong,$"{mode}: {wrong}");
            }
        }

        [Test]public void ALakeStandingAboveTheStubsBankSaysTheDeckIsTooLowInEitherMode()
        {
            // Desert 692835: the lake's surface is 110.6 m, 2-3 m above the dry ground beside it, and the stub ends at
            // 106 m 40 m from it; at 12 % the deck cannot be 1 m over the surface by the shore.
            var w=World(692835,RegionPreset.Desert);Vector2 a=new Vector2(864,-1160),b=new Vector2(1012,-1160);
            Assume.That(!w.IsWater(a)&&!w.IsWater(b)&&Enumerable.Range(1,19).Any(i=>w.IsWater(Vector2.Lerp(a,b,i/20f))),"The generated region no longer has this lake.");
            foreach(var mode in new[]{RoadBuildMode.Automatic,RoadBuildMode.Bridge})
            {
                string why=Cross(w,a,b,mode,out bool built);
                Assert.IsFalse(built,$"{mode} built a crossing over a lake standing 3 m above the bank.");
                StringAssert.Contains("clear the water",why,mode.ToString());StringAssert.Contains("further back",why,mode.ToString());
                AssertFitsMode(why,mode);
            }
            // Ground mode still refuses any crossing as ground road
            StringAssert.Contains(RoadConstructionPlan.GroundOnWater,Cross(w,a,b,RoadBuildMode.Ground,out bool ground));Assert.IsFalse(ground);
        }

        static void AssertFitsMode(string refusal,RoadBuildMode mode)
        {
            Assert.IsFalse(refusal.Contains(RoadConstructionPlan.GroundOnWater),$"{mode}: advice for Ground mode: {refusal}");
            if(mode==RoadBuildMode.Automatic)Assert.IsFalse(refusal.Contains("Automatic sections"),$"Automatic: told to use Automatic: {refusal}");
            if(mode==RoadBuildMode.Bridge)Assert.IsFalse(refusal.Contains("tunnel")||refusal.Contains("Page Down"),$"Bridge: tunnel advice to a bridge: {refusal}");
        }

        GameObject root;
        [TearDown]public void Cleanup(){if(root!=null)Object.DestroyImmediate(root);root=null;}

        /// <summary>The airside version of Cross: service road stubs in an airport at the region's origin (so airport and
        /// world coordinates agree), crossed with AirsideTerrainPlan from stub end to stub end.</summary>
        string CrossAirside(ValleyWorld w,Vector2 a1,Vector2 b1,RoadBuildMode mode,out bool built)
        {
            built=false;if(root!=null)Object.DestroyImmediate(root);
            var state=new CareerState {mode=GameMode.Sandbox,funds=0,wholeRegion=true,handsOn=true,selectedSite=0,layout=new CareerLayout(),publicRoads=new PublicRoadNetwork(),world=w};
            root=new GameObject("Airside crossing");root.SetActive(false);
            var airport=root.AddComponent<Airport>();airport.groundSize=0;airport.TerrainHeight=p=>w.Height(p);root.SetActive(true);
            var net=airport.Taxiways;var dir=(b1-a1).normalized;Vector2 a0=a1-dir*50f,b0=b1+dir*50f;
            int na0=net.AddNode(On(w,a0)),na1=net.AddNode(On(w,a1)),nb1=net.AddNode(On(w,b1)),nb0=net.AddNode(On(w,b0));
            net.Connect(na0,na1,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Connect(nb1,nb0,RunwayRules.RoadWidth,PavementSurface.Asphalt,false,RoadClass.Service);
            net.Rebuild();var old=new HashSet<int>(net.edges.Select(e=>e.id));
            if(!AirsideTerrainPlan.TryCreate(state,airport,na1,net.Node(nb1).position,nb1,null,null,RunwayRules.RoadWidth,PavementSurface.Asphalt,RoadClass.Service,
                out var plan,out var why,null,new RoadBuildSettings {mode=mode,smooth=true}))return "refused: "+why;
            // committing grades the shared generated world: put it back afterwards
            var heights=(float[])w.heights.Clone();var removed=w.trees.Select(t=>t.removed).ToArray();
            try{if(!plan.TryCommit(state,out why))return "commit refused: "+why;net.Rebuild();return Judge(w,net,old,na0,nb0,out built);}
            finally{System.Array.Copy(heights,w.heights,heights.Length);for(int i=0;i<removed.Length;i++)w.trees[i].removed=removed[i];}
        }
        static string Judge(ValleyWorld w,TaxiwayNetwork net,HashSet<int> old,int na0,int nb0,out bool built)
        {
            built=true;var wrong=new List<string>();var made=net.edges.Where(e=>!old.Contains(e.id)).ToList();
            foreach(var e in made)
            {
                var path=net.Path(e);
                for(int i=0;i<=24;i++)
                {
                    var p=path.Position(i/24f);var at=new Vector2(p.x,p.z);if(!w.IsWater(at))continue;
                    if(e.structure==RoadStructure.Ground)wrong.Add("ground road over water");
                    else if(e.structure==RoadStructure.Tunnel)wrong.Add("bore under water");
                    else if(p.y<w.WaterSurfaceAt(at)+RoadConstructionPlan.WaterClearance-RoadConstructionPlan.WaterTolerance-.01f)wrong.Add($"deck {p.y-w.WaterSurfaceAt(at):0.00} m over water");
                }
            }
            if(!made.Any(e=>e.structure==RoadStructure.Elevated))wrong.Add("no bridge");
            if(net.FindVehicleRoute(na0,nb0)==null)wrong.Add("not joined");
            return wrong.Count==0?null:string.Join(", ",wrong.Distinct());
        }

        /// <summary>The same lakes crossed by an airside service road (AirsideTerrainPlan, 20 % grade limit), including the
        /// lake standing above its shore that a street cannot climb over in time.</summary>
        [TestCase(RegionPreset.Woodland,160f,-1912f,160f,-1644f,TestName="Service road: Woodland lake 144 m, shore met at an angle")]
        [TestCase(RegionPreset.Woodland,280f,-1392f,280f,-764f,TestName="Service road: Woodland lake 504 m, banks 1.8 m")]
        [TestCase(RegionPreset.Mountains,-560f,160f,-412f,160f,TestName="Service road: Mountain shore band 24 m")]
        [TestCase(RegionPreset.Mountains,188f,640f,348f,640f,TestName="Service road: Mountain shore band 36 m")]
        [TestCase(RegionPreset.Desert,-2960f,1740f,-2960f,1900f,TestName="Service road: Desert lake 36 m standing 2 m above one bank")]
        [TestCase(RegionPreset.Desert,852f,-1280f,1048f,-1280f,TestName="Service road: Desert lake 72 m standing 3 m above one bank")]
        [TestCase(RegionPreset.Desert,-2880f,2320f,-2604f,2320f,TestName="Service road: Desert lake 152 m, a rise at the shore")]
        [TestCase(RegionPreset.Desert,-2092f,1120f,-1896f,1120f,TestName="Service road: Desert lake 72 m, 0.7 m bank")]
        [TestCase(RegionPreset.Desert,864f,-1160f,1012f,-1160f,TestName="Service road: Desert lake 24 m standing 3 m above one bank")]
        [TestCase(RegionPreset.Woodland,-796f,1120f,-460f,1120f,TestName="Service road: Woodland lake 212 m, steep stubs")]
        [TestCase(RegionPreset.Woodland,-1136f,1720f,-896f,1720f,TestName="Service road: Woodland lake 116 m, low bank")]
        public void ServiceRoadBankToBankAcrossALakeBuildsInAutomaticAndBridge(RegionPreset preset,float ax,float az,float bx,float bz)
        {
            var w=World(42,preset);Vector2 a=new Vector2(ax,az),b=new Vector2(bx,bz);
            Assume.That(!w.IsWater(a)&&!w.IsWater(b)&&Enumerable.Range(1,19).Any(i=>w.IsWater(Vector2.Lerp(a,b,i/20f))),"The generated region no longer has this lake.");
            foreach(var mode in new[]{RoadBuildMode.Automatic,RoadBuildMode.Bridge})
            {
                string wrong=CrossAirside(w,a,b,mode,out bool built);
                Assert.IsTrue(built,$"{mode}: {wrong}");Assert.IsNull(wrong,$"{mode}: {wrong}");
            }
            StringAssert.Contains(RoadConstructionPlan.GroundOnWater,CrossAirside(w,a,b,RoadBuildMode.Ground,out bool ground));Assert.IsFalse(ground);
        }

        /// <summary>Every lake, river or channel 24-640 m wide with gentle land either side on a sample of generated
        /// regions, crossed bank to bank from a ground street stub on each side (60 m back from the water).</summary>
        [Test]public void EveryBankToBankCrossingOnGeneratedRegionsBuildsCleanOrSaysWhyInTheModeChosen()
        {
            int crossings=0,builds=0;var failures=new List<string>();
            foreach(var (seed,preset) in new[]{(42,RegionPreset.Woodland),(42,RegionPreset.Tropical),(42,RegionPreset.Mountains),(692835,RegionPreset.Desert),(3,RegionPreset.Mountains),(5,RegionPreset.Desert)})
            {
                var w=World(seed,preset);
                foreach(var (a,b) in Crossings(w))
                {
                    crossings++;
                    foreach(var mode in new[]{RoadBuildMode.Automatic,RoadBuildMode.Bridge})
                    {
                        string result=Cross(w,a,b,mode,out bool built);
                        if(built){if(result!=null)failures.Add($"{preset} {seed} {a}→{b} {mode}: built with {result}");else builds++;continue;}
                        try{AssertFitsMode(result,mode);}catch(AssertionException){failures.Add($"{preset} {seed} {a}→{b} {mode}: {result}");}
                    }
                }
            }
            Assert.IsEmpty(failures,string.Join("\n",failures));
            Assert.That(crossings,Is.GreaterThan(20));Assert.That(builds,Is.GreaterThan(crossings*2*3/4),$"{builds} of {crossings*2} attempts built");
        }

        /// <summary>Water 24-640 m across along grid lines 120 m apart, with 130 m of dry land either side and stubs
        /// under 8 % grade: the stub ends a1 and b1, 60 m back from the water. At most twelve per region, spread over
        /// the widths.</summary>
        static IEnumerable<(Vector2 a1,Vector2 b1)> Crossings(ValleyWorld w)
        {
            var found=new List<(float width,Vector2 a1,Vector2 b1)>();
            const float step=4f;float half=ValleyWorld.Size*.5f-240f;int n=Mathf.FloorToInt(half*2f/step);
            for(int axis=0;axis<2;axis++)
            for(float c=-half;c<=half;c+=120f)
            {
                Vector2 P(float t)=>axis==0?new Vector2(t,c):new Vector2(c,t);
                var wet=new bool[n+1];for(int i=0;i<=n;i++)wet[i]=w.IsWater(P(-half+i*step));
                for(int i=1;i<n;)
                {
                    if(!wet[i]||wet[i-1]){i++;continue;}
                    int j=i;while(j<=n&&wet[j])j++;
                    float width=(j-i)*step;int back=Mathf.CeilToInt(130f/step);bool land=i-back>=0&&j+back<=n;
                    for(int k=1;k<=back&&land;k++)land=!wet[i-k]&&!wet[j-1+k];
                    float t0=-half+(i-1)*step,t1=-half+j*step;
                    if(land&&width>=24f&&width<=640f&&Mathf.Abs(w.Height(P(t0-110f))-w.Height(P(t0-60f)))<4f&&Mathf.Abs(w.Height(P(t1+110f))-w.Height(P(t1+60f)))<4f)
                        found.Add((width,P(t0-60f),P(t1+60f)));
                    i=j;
                }
            }
            found=found.OrderBy(x=>x.width).ToList();int take=Mathf.Min(12,found.Count);
            for(int k=0;k<take;k++){var x=found[take==1?0:Mathf.RoundToInt(k*(found.Count-1f)/(take-1f))];yield return (x.a1,x.b1);}
        }
    }
}
