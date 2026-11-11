using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public partial class DevToolbar
    {
        DevBaggageControls BaggageBuilder => baggage!=null ? baggage : baggage=GetComponent<DevBaggageControls>() ?? gameObject.AddComponent<DevBaggageControls>();
        public void OpenBaggageBuilder()
        {
            airfield=GetComponent<DevAirfieldTool>();
            if (airfield?.airport==null) return;
            gateDesigner.Close(); openPop=null;
            BaggageBuilder.BeginHall(BaggageBuilder.depotSize);
        }
        void BaggageCards()
        {
            var builder=BaggageBuilder; var cards=new Card[3];
            for (int i=0;i<cards.Length;i++)
            {
                var size=(BaggageDepotSize)i; var limits=BaggageDepot.Capacity(size); var layout=new BaggageDepotLayout(size);
                cards[i]=new Card(r=>BaggageHallPicture(r,size),size==BaggageDepotSize.Small ? "Small shed" : size+" hall",
                    $"{limits.teams} teams · {layout.Lanes} bay"+(layout.Lanes>1 ? "s" : ""),
                    $"{size} baggage hall. Includes a tractor, two carts, belt loader and four workers. Pave the entire yard; connect a terminal and assign stands after placement.",
                    builder.Placement==BaggagePlacement.Depot && builder.depotSize==size,true,()=>builder.BeginHall(size));
            }
            Group("BAGGAGE HALLS",112,cards);
            Group("TERMINAL LINKS",112,
                new Card(r=>BaggageLinkPicture(r,false),"Underground","Hidden conveyor","Click a baggage hall, then its terminal. Bags travel underground; terminal interiors stay abstract.",
                    builder.Placement==BaggagePlacement.Underground,true,()=>builder.BeginPlacement(BaggagePlacement.Underground)),
                new Card(r=>BaggageLinkPicture(r,true),"Covered link","Nearby terminal","Click a baggage hall, then a nearby terminal. Requires a clear route of at most 80 m to the hall's machinery side.",
                    builder.Placement==BaggagePlacement.Covered,true,()=>builder.BeginPlacement(BaggagePlacement.Covered)));
        }
        static void BaggageHallPicture(Rect r,BaggageDepotSize size)
        {
            if (Event.current.type!=EventType.Repaint) return;
            int lanes=new BaggageDepotLayout(size).Lanes;
            float width=r.width*(size==BaggageDepotSize.Small ? .54f : size==BaggageDepotSize.Medium ? .72f : .94f);
            float x=r.center.x-width*.5f,y=r.y+U(7),h=r.height-U(10);
            DevGui.Fill(new Rect(x,y,width,h),new Color(.56f,.66f,.7f),U(2));
            DevGui.Fill(new Rect(x-U(3),y-U(3),width+U(6),U(5)),new Color(.27f,.39f,.46f));
            float doorW=width/(lanes*1.9f+1);
            for (int i=0;i<lanes;i++)
            {
                float dx=x+width*(i+1)/(lanes+1)-doorW*.5f;
                DevGui.Fill(new Rect(dx,y+h*.27f,doorW,h*.73f),new Color(.08f,.13f,.18f));
                DevGui.Fill(new Rect(dx,y+h*.27f,doorW,U(2)),new Color(.95f,.71f,.24f));
            }
        }
        static void BaggageLinkPicture(Rect r,bool covered)
        {
            if (Event.current.type!=EventType.Repaint) return;
            var c=covered ? new Color(.56f,.66f,.7f) : DevGui.Accent;
            float y=r.center.y;
            DevGui.Fill(new Rect(r.x+U(5),y-U(10),U(12),U(20)),c,U(2));
            DevGui.Fill(new Rect(r.xMax-U(17),y-U(10),U(12),U(20)),c,U(2));
            if (covered) DevGui.Fill(new Rect(r.x+U(17),y-U(4),r.width-U(34),U(8)),c);
            else for (float x=r.x+U(18);x<r.xMax-U(18);x+=U(10)) DevGui.Fill(new Rect(x,y-U(1),U(5),U(2)),c);
        }
        void BaggageBuildInspector()
        {
            var b=BaggageBuilder;
            Title(b.Linking ? "Connect baggage" : "Build baggage");
            if (b.Placement==BaggagePlacement.Depot)
            {
                var site=BaggageDepot.Site(b.depotSize,Vector2.zero,0);
                Note($"{b.depotSize} · Yard {site.width:0} × {site.length:0} m");
                Note("Move beside a straight service road on a paved apron. Green fits; red needs more space.");
                Buttons(B("Flip road side",b.FlipSite,"Put the hall on the other side of the road",hint:"Z"));
                Note("Includes one equipment team and four workers. Connect a terminal and assign stands after placing.");
                if (b.PreviewProblem!=null) Danger(b.PreviewProblem);
            }
            else Note(b.Placement==BaggagePlacement.Covered ? "Click a hall, then a terminal within 80 m. The covered conveyor needs a clear route to the machinery wall." :
                "Click a hall, then its terminal. The underground conveyor connects check-in and reclaim without showing the interior.");
            if (b.Message!=null) Note(b.Message);
            Buttons(B("Cancel",b.Cancel,"Return to selection",hint:"Esc"));
        }
        void BaggageHallInspector(BaggageDepot hall)
        {
            var b=BaggageBuilder; b.SelectHall(hall); var station=b.Station; var airport=airfield.airport;
            Title(hall.name);
            Note($"{hall.Teams.Length}/{hall.Maximum.teams} teams · {hall.Trailers}/{hall.Maximum.trailers} carts · {hall.BusyBays}/{hall.ProcessingBays} bays busy");
            Section("TERMINAL CONNECTION");
            Note(hall.ConnectedTerminal!=null ? "Connected to Terminal "+(airport.terminals.IndexOf(hall.ConnectedTerminal)+1)+" · "+hall.connection.kind : "1. Connect this hall to a terminal");
            Buttons(B("Underground",()=>b.BeginPlacement(BaggagePlacement.Underground),"Click this hall and then its terminal"),
                B("Covered link",()=>b.BeginPlacement(BaggagePlacement.Covered),"Click this hall and then a nearby terminal"));
            PropPop("Connection","Settings","baggage-link-details","Conveyor capacity, speed and terminal assignment",()=>DepotTerminalInspector(airport,hall));
            Section("HANDLING TEAM");
            if (station==null) { Note("Add an equipment team to operate this hall."); return; }
            Note("Aircraft: "+(station.automaticHold?.aircraft?.FullName ?? "Not configured"));
            PropStepper("Team",(station.depotBay+1).ToString(),()=>NextHallTeam(hall,-1),()=>NextHallTeam(hall,1),"Choose the team to assign",hall.Teams.Length>1,hall.Teams.Length>1);
            var stand=b.AssignmentStand;
            PropStepper("Serve stand",stand?.Name ?? "No stands",()=>b.standIndex--,()=>b.standIndex++,"Choose an aircraft stand to serve",stand!=null,stand!=null);
            if (stand!=null) Buttons(B(station.standIds.Contains(stand.id) ? "Unassign "+stand.Name : "Assign "+stand.Name,
                ()=>b.ToggleStand(stand),"This equipment team handles baggage at the assigned stand",station.standIds.Contains(stand.id),enabled:b.CanConfigure));
            Note(station.standIds.Count==0 ? "2. Assign a stand to start handling baggage" : station.standIds.Count+" assigned stands · "+(station.Problem ?? (station.automaticHandling ? "Automatic handling on" : "Automatic handling off")));
            Buttons(B("Add equipment team",()=> { var catalog=BaggageBuildCatalog.Load(); if (catalog!=null) { var team=catalog.AddTeam(hall,out var why); if (team!=null) b.station=team; b.SetMessage(why); } },
                "Add a tractor, two carts and belt loader with separate parking",enabled:hall.Teams.Length<hall.Maximum.teams && hall.Trailers+2<=hall.Maximum.trailers));
            Section("STAFF");
            Note($"{hall.hiredWorkers}/{hall.Maximum.workers} workers · {hall.Available} available");
            Buttons(B("Hire worker",b.HireWorker,"Hire another baggage worker",enabled:hall.hiredWorkers<hall.Maximum.workers),
                B("Dismiss worker",b.FireWorker,"Dismiss an available worker",enabled:hall.Available>0));
            PropPop("Processing","Upgrades","baggage-upgrades","Install equipment to improve this hall's processing speed and storage",()=>
            {
                foreach (var upgrade in new[] { BaggageDepotUpgrade.HandlingAssist,BaggageDepotUpgrade.PoweredBelts,BaggageDepotUpgrade.AutomaticSorting,BaggageDepotUpgrade.BufferStorage })
                {
                    bool installed=hall.Has(upgrade);
                    string title=upgrade switch { BaggageDepotUpgrade.HandlingAssist=>"Lifting aids · +30%",BaggageDepotUpgrade.PoweredBelts=>"Powered belts · +50%",
                        BaggageDepotUpgrade.AutomaticSorting=>"Automatic sorting",_=>"Buffer storage · 2×" };
                    string help=upgrade switch { BaggageDepotUpgrade.HandlingAssist=>"Increase depot handover speed by 30%",BaggageDepotUpgrade.PoweredBelts=>"Increase belt speed by 50%",
                        BaggageDepotUpgrade.AutomaticSorting=>"Faster outgoing sorting; requires a Medium or Large hall",_=>"Double arrivals and departures storage capacity" };
                    Buttons(B(title,()=> { hall.SetUpgrade(upgrade,!installed,out var why); b.SetMessage(why); },help,installed,enabled:hall.Working==0 && hall.Supports(upgrade)));
                }
            });
            if (b.Message!=null) Note(b.Message);
            if (airfield.ApronProblemText!=null) Danger(airfield.ApronProblemText);
            Buttons(B("Build another hall",OpenBaggageBuilder,"Open baggage construction"));
        }
        void NextHallTeam(BaggageDepot hall,int step)
        {
            var teams=hall.Teams;
            if (teams.Length==0) return;
            int index=System.Array.IndexOf(teams,BaggageBuilder.Station);
            BaggageBuilder.station=teams[(index+step+teams.Length)%teams.Length];
        }
    }
}
