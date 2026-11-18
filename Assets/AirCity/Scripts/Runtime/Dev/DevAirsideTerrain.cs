using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Game;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevAirfieldTool
    {
        AirsideTerrainPlan airsideTerrain;
        (int from,Vector3 end,float width,PavementSurface surface,bool road,int version,int funds) airsideQuoteKey;
        string airsideQuoteProblem;
        public int AirsidePrice=>airsideTerrain?.Price??0;
        public int AirsideGradingPrice=>airsideTerrain?.GradingPrice??0;
        public bool AirsideDrawing=>(tool==DevTool.Taxiway || tool==DevTool.Road) && chainNode>=0 && lineSnapShown;
        public Vector3 AirsideEndpoint=>airport.transform.TransformPoint(LinePoint(airport.Taxiways,lineSnap));
        public float AirsideGrade
        {
            get
            {
                if(chainNode<0)return 0f;
                var start=airport.Taxiways.Node(chainNode).position;var end=LinePoint(airport.Taxiways,lineSnap);
                return (end.y-start.y)/Mathf.Max(1f,Vector2.Distance(Geo.XZ(start),Geo.XZ(end)));
            }
        }
        Vector3 LinePoint(TaxiwayNetwork net,LineSnap snap)
        {
            if(snap.node>=0 && net.Node(snap.node)!=null)return net.Node(snap.node).position;
            if(snap.edge!=null)
            {
                var path=net.Path(snap.edge);var p=path.Position(path.ClosestParameter(snap.p));
                p.y=snap.edge.IsRoad&&(snap.edge.structure!=AirCity.World.RoadStructure.Ground||snap.edge.alignment.smooth||snap.edge.alignment.curved)?
                    airport.RoadEdgeElevation(snap.edge,Geo.XZ(p)):airport.PavementElevationAt(Geo.XZ(p));
                return p;
            }
            if(snap.runway!=null)return snap.runway.ClosestOnCentreline(Geo.X0Z(snap.p));
            if(snap.hangarFrontage!=null)return Geo.X0Z(snap.p);
            float height=game!=null?game.valley.DisplayHeight(snap.p+Geo.XZ(airport.transform.position))-airport.transform.position.y:0f;
            if(tool==DevTool.Road)height=RoadTargetHeight(snap.p+Geo.XZ(airport.transform.position))-airport.transform.position.y;
            return Geo.X0Z(snap.p,height);
        }
        AirsideTerrainPlan standTerrain;
        (Vector3 at,float heading,float front,float lane,SizeCode code,PavementSurface surface,StandExit exit,int version,int funds) standQuoteKey;
        string standQuoteProblem;
        public bool StandDrawing=>(tool==DevTool.Stand || tool==DevTool.DeIcing) && ghost!=null;
        public int StandPrice=>standTerrain?.Price??CareerConstruction.StandPrice;
        public int StandGradingPrice=>standTerrain?.GradingPrice??0;
        public Vector3 StandEndpoint=>ghost==null?Vector3.zero:airport.transform.TransformPoint(Geo.X0Z(ghost.Centre,ghost.stand.position.y));
        void QuoteStand(bool force)
        {
            var s=ghost.stand;
            var key=(s.position,s.heading,s.front,s.lane,s.code,s.surface,s.exit,airport.LayoutVersion,game.State.funds);
            if(!force && key==standQuoteKey){ghostProblem=standQuoteProblem;return;}
            standQuoteKey=key;
            var centre=ghost.Centre+Geo.XZ(airport.transform.position);
            float reach=Vector2.Distance(ghost.Pad[0],ghost.Pad[2])*.5f+20f;
            var bounds=new Rect(centre-Vector2.one*reach,Vector2.one*reach*2f);
            AirsideTerrainPlan.TryCreateStand(game.State,airport,ghost,out standTerrain,out ghostProblem,game.valley.GroundCandidates(bounds));
            standQuoteProblem=ghostProblem;
        }

        void QuoteAirside(bool road,bool force)
        {
            var end=LinePoint(airport.Taxiways,lineSnap);
            var key=(chainNode,end,NewWidth,surface,road,airport.LayoutVersion,game.State.funds);
            if(key==airsideQuoteKey){roadProblem=airsideQuoteProblem;return;}
            airsideQuoteKey=key;
            var from=airport.Taxiways.At(chainNode)+Geo.XZ(airport.transform.position);var to=Geo.XZ(end)+Geo.XZ(airport.transform.position);
            // Include the previous bend's approaches and the largest possible junction flare in the prop query.
            const float margin=350f;
            var bounds=Rect.MinMaxRect(Mathf.Min(from.x,to.x)-margin,Mathf.Min(from.y,to.y)-margin,Mathf.Max(from.x,to.x)+margin,Mathf.Max(from.y,to.y)+margin);
            AirsideTerrainPlan.TryCreate(game.State,airport,chainNode,end,lineSnap.node,lineSnap.edge,lineSnap.runway,NewWidth,
                road && surface==PavementSurface.Grass?PavementSurface.Asphalt:surface,road?RoadClass.Service:RoadClass.None,
                out airsideTerrain,out roadProblem,game.valley.GroundCandidates(bounds),road?(RoadBuildSettings?)roadBuildSettings:null);
            airsideQuoteProblem=roadProblem;
        }
    }
}
