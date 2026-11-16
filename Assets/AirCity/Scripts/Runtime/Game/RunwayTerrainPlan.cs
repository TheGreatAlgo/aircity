using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>A detached cut/fill quote. Thresholds follow the hill; crossfall and bumps are graded out.</summary>
    public sealed class RunwayTerrainPlan
    {
        public const float MaximumGrade = .20f, EarthworkPrice = .055f;
        public Vector3 Start { get; private set; }
        public Vector3 End { get; private set; }
        public Rect Bounds { get; private set; }
        public float Earthwork { get; private set; }
        public int GradingPrice => Earthwork < 1f ? 0 : Mathf.CeilToInt(Earthwork * EarthworkPrice);
        public float Grade => (End.y - Start.y) / Mathf.Max(1f, Vector2.Distance(XZ(Start), XZ(End)));
        public int ChangedSamples => indices.Count;
        readonly List<int> indices = new List<int>();
        readonly List<float> after = new List<float>(), weights = new List<float>();
        readonly List<int> observed = new List<int>();
        readonly List<float> observedHeights = new List<float>();
        ValleyWorld source;
        bool applied;

        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        public static bool TryCreate(CareerState state, Airport airport, Runway draft, Runway extending,
            out RunwayTerrainPlan plan, out string problem,IEnumerable<GroundCoverInstance> ground = null)
        {
            plan = null; problem = "Choose a runway inside the region.";
            if (state?.world?.heights == null || airport == null || draft == null || draft.Length < 1f) return false;
            var world = state.world; var origin = XZ(airport.transform.position);
            var a = XZ(draft.start) + origin; var b = XZ(draft.end) + origin;
            if (!Finite(a) || !Finite(b)) return false;
            float length = Vector2.Distance(a, b); var axis = (b - a) / length; var right = new Vector2(axis.y, -axis.x);
            float h0 = extending != null ? airport.transform.position.y + extending.ElevationAt(XZ(draft.start), false) : world.Height(a);
            float h1 = extending != null ? airport.transform.position.y + extending.ElevationAt(XZ(draft.end), false) : world.Height(b);
            if (Mathf.Abs(h1 - h0) / length > MaximumGrade)
            { problem = "This hill is too steep for a runway. Choose a gentler direction."; return false; }
            float half = draft.HalfWidthTotal + (draft.Paved ? 1f : 3f);
            float over = draft.WorkMargin;
            float support = world.Spacing + 2f, feather = 30f;
            var footprint = CareerConstruction.Strip(a, b, half + 6f, over).ToArray();
            if (footprint.Any(p => !state.BuildableBounds.Contains(p))) return false;
            problem = "Prepare dry land above the water before building here.";
            if (footprint.Any(world.IsWater) || world.HasOcean && Mathf.Min(h0, h1) <= world.SeaLevel + .5f) return false;
            var rect = Rect.MinMaxRect(Mathf.Min(a.x,b.x)-half-over-6f,Mathf.Min(a.y,b.y)-half-over-6f,
                Mathf.Max(a.x,b.x)+half+over+6f,Mathf.Max(a.y,b.y)+half+over+6f);
            foreach (int i in ForestQuery.Candidates(world, rect))
            {
                var tree = world.trees[i]; if (tree.removed) continue;
                var d = tree.position-a; float along = Vector2.Dot(d,axis), across = Mathf.Abs(Vector2.Dot(d,right));
                if (along >= -over-6f && along <= length+over+6f && across <= half+6f)
                { problem = "Clear the trees in the runway footprint first."; return false; }
            }
            if(ground!=null)foreach(var item in ground)
            {
                if(!CareerConstruction.Clearable(item.kind) || world.GroundPropRemoved(item.id))continue;
                float radius=GroundCoverScatter.FootprintRadius(item.kind,item.scale);
                var d=item.position-a;float along=Vector2.Dot(d,axis),across=Mathf.Abs(Vector2.Dot(d,right));
                if(along>=-over-radius && along<=length+over+radius && across<half+radius)
                {problem="Clear the rocks and vegetation in the runway footprint first.";return false;}
            }
            var candidate = new RunwayTerrainPlan { source = world,
                Start = new Vector3(draft.start.x,h0-airport.transform.position.y,draft.start.z),
                End = new Vector3(draft.end.x,h1-airport.transform.position.y,draft.end.z) };
            float outer = half+support+feather, end = over+support+feather;
            candidate.Bounds = Rect.MinMaxRect(Mathf.Min(a.x,b.x)-outer-end,Mathf.Min(a.y,b.y)-outer-end,
                Mathf.Max(a.x,b.x)+outer+end,Mathf.Max(a.y,b.y)+outer+end);
            bool StructureClear(Alignment3D path,RoadStructure kind,float width)
            {
                if(kind==RoadStructure.Ground)return true;
                int count=Mathf.Max(1,Mathf.CeilToInt(path.Length/4f));
                for(int i=0;i<=count;i++)
                {
                    var point=path.AtDistance(path.Length*i/count);var delta=XZ(point)-a;
                    float along=Vector2.Dot(delta,axis),across=Mathf.Abs(Vector2.Dot(delta,right));
                    if(along< -over-width || along>length+over+width || across>half+width*.5f+8f)continue;
                    float runwayHeight=Mathf.LerpUnclamped(h0,h1,along/length);
                    if(kind!=RoadStructure.Tunnel || point.y+RoadStructureBuilder.TunnelClearance+RoadStructureBuilder.TunnelCover>=runwayHeight)return false;
                }
                return true;
            }
            foreach(var road in airport.Taxiways.edges)if(road.structure!=RoadStructure.Ground)
            {
                var path=airport.Taxiways.Path(road);
                var shifted=new Alignment3D(airport.transform.TransformPoint(path.start),airport.transform.TransformPoint(path.end),
                    new AlignmentShape {curved=path.shape.curved,control=path.shape.control+origin,smooth=path.shape.smooth,startGrade=path.shape.startGrade,endGrade=path.shape.endGrade});
                if(!StructureClear(shifted,road.structure,road.width))
                {problem="Keep the runway clear of bridges, and leave sufficient rock cover over service-road tunnels.";return false;}
            }
            var roads = state.RoadNetwork();
            foreach(var road in roads.segments)
                if(!StructureClear(roads.Path(road),road.structure,road.Type.FullWidth))
                {problem="Keep the runway clear of bridges, and leave sufficient rock cover over public-road tunnels.";return false;}
            var roadSpans = roads.segments.Where(r=>r.structure==RoadStructure.Ground).Select(r=>(
                a:PublicRoadNetwork.XZ(roads.Node(r.a).position),b:PublicRoadNetwork.XZ(roads.Node(r.b).position),
                half:Mathf.Max(PublicRoadGeometry.EarthworkReach,r.Width*.5f)+world.Spacing,width:r.Width)).ToArray();
            foreach(var road in roadSpans)
                if(SegmentDistance(a-axis*over,b+axis*over,road.a,road.b)<half+road.half)
                {problem="Keep the runway clear of public roads.";return false;}
            var otherRunways=airport.Runways.Where(r=>r!=draft && r!=extending).ToArray();
            var net=airport.Taxiways;
            var pavementSpans=net.edges.Where(e=>e.structure==RoadStructure.Ground).Select(e=>(a:net.Node(e.a),b:net.Node(e.b),half:e.width*.5f+world.Spacing)).ToArray();
            var terminalOutlines=airport.terminals.Select(t=>t.Outline()).ToArray();
            bool Existing(Vector2 p)
            {
                var local = p-origin;
                foreach (var r in otherRunways)
                    if (r.Contains(new Vector3(local.x,0f,local.y),world.Spacing)) return true;
                foreach(var span in pavementSpans)
                {
                    if(extending!=null && (span.a.runway==extending || span.b.runway==extending) && extending.Contains(new Vector3(local.x,0f,local.y),world.Spacing))continue;
                    if(CareerConstruction.Distance(local,XZ(span.a.position),XZ(span.b.position))<span.half)return true;
                }
                foreach(var area in airport.aprons)if(area.Contains(local,world.Spacing))return true;
                foreach(var outline in terminalOutlines)
                    if(Geo.Inside(outline,local) || Geo.DistanceToOutline(outline,local)<world.Spacing)return true;
                foreach (var road in roadSpans)
                    if (CareerConstruction.Distance(p,road.a,road.b) < road.half) return true;
                return false;
            }
            int x0 = Grid(candidate.Bounds.xMin), x1=Grid(candidate.Bounds.xMax), z0=Grid(candidate.Bounds.yMin), z1=Grid(candidate.Bounds.yMax);
            for(int z=z0;z<=z1;z++) for(int x=x0;x<=x1;x++)
            {
                var p=new Vector2(x*world.Spacing-ValleyWorld.Size*.5f,z*world.Spacing-ValleyWorld.Size*.5f);
                if(!state.BuildableBounds.Contains(p))continue;
                var d=p-a;float along=Vector2.Dot(d,axis),across=Mathf.Abs(Vector2.Dot(d,right));
                float away=Mathf.Max(across-half-support,-over-support-along,along-length-over-support,0f);
                if(away>=feather)continue;
                float weight=1f-Mathf.SmoothStep(0f,1f,away/feather);
                int i=z*ValleyWorld.Resolution+x;float old=world.heights[i],target=h0+(h1-h0)*along/length;
                candidate.observed.Add(i);candidate.observedHeights.Add(old);
                float next=Mathf.Lerp(old,target,weight);
                if(Mathf.Abs(next-old)<.002f)continue;
                if(next<1f || next>ValleyWorld.HeightRange-1f || Mathf.Abs(next-old)>100f)
                {problem="This placement needs too much cut and fill. Choose a gentler stretch of ground.";return false;}
                if(Existing(p)) {problem="The runway earthwork would affect existing pavement, buildings or a public road.";return false;}
                candidate.indices.Add(i);candidate.after.Add(next);candidate.weights.Add(weight);
                candidate.Earthwork+=Mathf.Abs(next-old)*world.Spacing*world.Spacing;
            }
            plan=candidate;problem=null;return true;
        }

        /// <summary>Checks every saved sample before writing any, so a stale quote is never partially applied.</summary>
        public bool Apply(ValleyWorld world)
        {
            if(applied || world!=source)return false;
            for(int i=0;i<observed.Count;i++)if(world.heights[observed[i]]!=observedHeights[i])return false;
            for(int i=0;i<indices.Count;i++)
            {world.heights[indices[i]]=after[i];}
            applied=true;
            return true;
        }
        /// <summary>The placement transaction pays for construction and the exact quoted cut/fill once.</summary>
        public bool TryCommit(CareerState state,int constructionPrice,out int totalPrice,out string problem)
        {
            totalPrice=0;problem="The runway quote no longer matches this region.";
            if(state==null || state.world!=source || constructionPrice<0)return false;
            long total=(long)constructionPrice+GradingPrice;
            if(total>int.MaxValue){problem="This runway costs too much to build.";return false;}
            totalPrice=(int)total;
            if(state.mode!=GameMode.Sandbox && state.funds<totalPrice){problem="Not enough funds for this runway and its earthwork.";return false;}
            if(!Apply(state.world)){problem="The ground changed. Move the endpoint to refresh the quote.";return false;}
            if(state.mode!=GameMode.Sandbox)state.funds-=totalPrice;
            if(state.layout!=null)state.layout.earthwork+=Earthwork;
            problem=null;return true;
        }
        /// <summary>An empty airport uses the upper plateau as the datum for its future apron and taxiways.</summary>
        public void PlaceProfile(Airport airport,Runway runway,bool anchorEmptyAirport)
        {
            float delta=0f;
            if(anchorEmptyAirport && !airport.Runways.Any(r=>r!=runway) && airport.Taxiways.edges.Count==0 && airport.Taxiways.nodes.Count==0 &&
                airport.Taxiways.stands.Count==0 && airport.aprons.Count==0 && airport.terminals.Count==0 && airport.Depots.Count==0 &&
                airport.deicingFacilities.Count==0 && airport.deicingWorkAreas.Count==0 && airport.deicingParkingBays.Count==0 &&
                airport.deicingOutsideAccess.Count==0 && airport.GetComponentsInChildren<AirCity.Vehicles.BaggageDepot>(true).Length==0 &&
                airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>(true).Length==0)
            {
                delta=Mathf.Max(Start.y,End.y);
                airport.transform.position+=Vector3.up*delta;
            }
            runway.start=Start-Vector3.up*delta;runway.end=End-Vector3.up*delta;
            runway.naturalEdges=true;airport.LayoutChanged();
        }
        static int Grid(float v)=>Mathf.Clamp(Mathf.FloorToInt((v+ValleyWorld.Size*.5f)/(ValleyWorld.Size/(ValleyWorld.Resolution-1))),0,ValleyWorld.Resolution-1);
        static bool Finite(Vector2 p)=>ValleyWorld.Finite(p.x)&&ValleyWorld.Finite(p.y);
        static float SegmentDistance(Vector2 a,Vector2 b,Vector2 c,Vector2 d)=>Geo.SegmentsMeet(a,b,c,d)?0f:
            Mathf.Min(CareerConstruction.Distance(a,c,d),CareerConstruction.Distance(b,c,d),CareerConstruction.Distance(c,a,b),CareerConstruction.Distance(d,a,b));
    }
}
