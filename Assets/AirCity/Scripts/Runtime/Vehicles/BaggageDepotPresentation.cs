using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Characters;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Vehicles
{
    /// <summary>Parametric warehouse, drive-through bays, conveyors and bounded luggage presentation.</summary>
    sealed class BaggageDepotPresentation
    {
        readonly BaggageDepot depot;
        AirfieldPart building,roof,equipment,paint;
        AirfieldPart terminalConnection,connectionOverlay;
        int connectionStamp;
        int terminalPalette;
        Transform bagsRoot;
        BaggageArtPool bags;
        readonly BaggageStorageLayout arrivals=new BaggageStorageLayout(36),dispatch=new BaggageStorageLayout(36);
        readonly List<BaggageTurnaround> transfers=new List<BaggageTurnaround>();
        readonly List<RampCrewRig> idleWorkers=new List<RampCrewRig>();
        /// <summary>Each waiting worker's own spot, heading and next small change (a turn, a step, a look round).</summary>
        struct Waiting { public Vector3 at,spot; public float yaw,targetYaw,next; public Vector3 look; public bool looking; }
        readonly List<Waiting> waiting=new List<Waiting>();
        float clock;
        public BaggageDepotPresentation(BaggageDepot depot) { this.depot=depot; }
        public void Build()
        {
            var airport=depot.Airport;
            building ??= new AirfieldPart(depot.transform,"Baggage depot building");
            roof ??= new AirfieldPart(depot.transform,"Baggage depot roof");
            equipment ??= new AirfieldPart(depot.transform,"Baggage depot machinery");
            paint ??= new AirfieldPart(depot.transform,"Baggage depot bays");
            depot.transform.localPosition=Vector3.zero; depot.transform.localRotation=Quaternion.identity;
            var art=new BaggageDepotArt(depot); art.Build();
            building.Set(art.shell.Apply(building.Target,"Depot shell"),airport.building);
            roof.Set(art.roof.Apply(roof.Target,"Depot roof"),airport.building);
            equipment.Set(art.equipment.Apply(equipment.Target,"Depot machinery"),airport.building);
            foreach (var part in new[] { building,roof,equipment }) part.Renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            paint.Set(art.paint.Apply(paint.Target,"Depot yard markings"),airport.paint);
            SetInterior();
            RefreshConnection();
            terminalPalette=PaletteStamp();
            var area=depot.PaintArea;
            var apron=ApronArea.Rect(area.centre,new Vector2(area.width+8,area.length+8),depot.bearing,PavementSurface.Concrete);
            if (apron.outline.Any(p=>!airport.aprons.Any(a=>a.surface.IsPaved() && a.Contains(p))))
            { airport.aprons.Add(apron); airport.BuildGround(); }
        }
        public void SetInterior()
        {
            if (roof?.Renderer!=null) roof.Renderer.enabled=!depot.showInterior;
        }
        public void RefreshConnection()
        {
            terminalConnection ??=new AirfieldPart(depot.transform,"Terminal baggage connection");
            connectionOverlay ??=new AirfieldPart(depot.transform,"Terminal baggage route");
            var art=new TerminalBaggagePresentation(); art.Build(depot.Airport.Baggage.TerminalLink(depot));
            terminalConnection.Set(art.mesh.Apply(terminalConnection.Target,"Terminal baggage connection"),depot.Airport.building);
            terminalConnection.Renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            connectionOverlay.Set(art.overlay.Apply(connectionOverlay.Target,"Terminal baggage route"),depot.Airport.paint);
            connectionOverlay.Renderer.enabled=depot.showConnection;
            connectionStamp=ConnectionStamp();
        }
        int ConnectionStamp()
        {
            var link=depot.Airport.Baggage.TerminalLink(depot);
            return link==null ? 0 : System.HashCode.Combine(link.HallPort,link.TerminalPort(true),link.TerminalPort(false),depot.connection.kind,depot.showConnection);
        }
        int PaletteStamp()
        {
            var terminal=depot.ConnectedTerminal;
            return terminal==null ? 0 : System.HashCode.Combine(terminal.exterior.facade.cladding,terminal.exterior.roof);
        }

        public void Present() => Present(Application.isPlaying ? Time.deltaTime*depot.Airport.timeScale : 0f);
        public void Present(float dt)
        {
            if (terminalPalette!=PaletteStamp()) Build();
            SetInterior();
            if (connectionStamp!=ConnectionStamp()) RefreshConnection();
            var teams=depot.Teams; if (teams.Length==0 || teams[0].bagPrefabs==null || teams[0].bagPrefabs.Length==0) return;
            var fleet=depot.Airport.Baggage.Fleet(teams[0]); if (fleet==null) return;
            if (bagsRoot==null)
            {
                bagsRoot=new GameObject("Depot luggage (runtime)") { hideFlags=HideFlags.DontSave }.transform;
                bagsRoot.SetParent(depot.transform,false);
            }
            bags ??= new BaggageArtPool(bagsRoot,teams[0].bagPrefabs); bags.Begin();
            transfers.Clear();
            foreach (var team in teams)
            {
                var job=depot.Airport.Baggage.Fleet(team)?.Job;
                if (job?.TransferBatch!=null) transfers.Add(job);
            }
            void Store(BaggageStore store,BaggageStorageLayout layout,bool incoming)
            {
                layout.Update(store,transfers);
                foreach (var bag in layout.Bags)
                {
                    int slot=bag.slot;
                    int lane=slot/12%depot.ProcessingBays;
                    float x=depot.Layout.LaneX(lane)+6.2f+slot%3*.65f;
                    var at=depot.Point(x,depot.Layout.WorkZ+(incoming ? -13f : -2f)+slot/3%4*.65f);
                    bags.Show(new Vector3(at.x,.12f+slot/12*.32f,at.y),
                        Quaternion.LookRotation(new Vector3(depot.Facing.x,0,depot.Facing.y)),bag.variant);
                }
            }
            Store(fleet.Arrivals,arrivals,true); Store(fleet.Dispatch,dispatch,false); bags.End();
            if (teams[0].crewPrefab!=null) { Idle(teams[0].crewPrefab,Mathf.Max(0f,dt)); BayHands(teams,teams[0].crewPrefab,Mathf.Max(0f,dt)); }
        }

        readonly List<RampCrewRig> bayRigs=new List<RampCrewRig>();
        /// <summary>One hall handler per working bay, by the conveyor ends. A team's performance drives it while its train
        /// stands in the bay; otherwise it waits there.</summary>
        void BayHands(BaggageStation[] teams,GameObject prefab,float dt)
        {
            int lanes=depot.ProcessingBays;
            while (bayRigs.Count<lanes)
            {
                var rig=Object.Instantiate(prefab,bagsRoot).GetComponent<RampCrewRig>();
                rig.Initialize(5+bayRigs.Count); rig.name="Depot bay handler"; bayRigs.Add(rig);
            }
            for (int lane=0;lane<bayRigs.Count;lane++)
            {
                var rig=bayRigs[lane]; rig.gameObject.SetActive(lane<lanes); if (lane>=lanes) continue;
                var hand=depot.BayHand(lane); hand.rig=rig;
                if (hand.owner!=null)
                {
                    bool live=false;
                    foreach (var team in teams) if (team.Performance!=null && (object)team.Performance==hand.owner) live=true;
                    if (!live) hand.owner=null;
                }
                RampCrewPose pose;
                if (hand.owner!=null) continue; // posed by the owning station
                else
                {
                    var home=hand.Home; if (!hand.placed) { hand.position=home; hand.yaw=hand.HomeYaw; hand.placed=true; }
                    hand.position=Vector3.MoveTowards(hand.position,home,.7f*dt); hand.yaw=Mathf.MoveTowardsAngle(hand.yaw,hand.HomeYaw,60f*dt);
                    pose=RampCrewPose.Standing(hand.position,Quaternion.Euler(0,hand.yaw,0)*Vector3.forward);
                }
                rig.Apply(pose,dt,depot.Airport.timeScale);
            }
        }

        /// <summary>Workers not on a team wait inside the hall like people waiting: a loose group, not a row, some turned
        /// toward each other; now and then one turns, takes a step or looks round (the rig shifts their weight itself).</summary>
        void Idle(GameObject prefab,float dt)
        {
            clock+=dt;
            while (idleWorkers.Count<depot.Available)
            {
                int i=idleWorkers.Count;
                var worker=Object.Instantiate(prefab,bagsRoot).GetComponent<RampCrewRig>();
                worker.Initialize(i); worker.name="Available depot worker"; idleWorkers.Add(worker);
                // A loose group per bay: two or three abreast, jittered, alternate ones half turned to their neighbour.
                int lane=i%depot.ProcessingBays,row=i/depot.ProcessingBays;
                float jx=Hash(i,1)-.5f,jz=Hash(i,2)-.5f;
                var at2=depot.Point(depot.Layout.LaneX(lane)+8.6f+(row%2)*1.2f+jx*.3f,depot.Layout.WallTop-7.2f-(row/2)*1.3f+jz*.3f);
                var spot=new Vector3(at2.x,0,at2.y);
                float face=Mathf.Atan2(-depot.Right.x,-depot.Right.y)*Mathf.Rad2Deg+(row%2==0 ? 35f : -35f)+(Hash(i,3)-.5f)*40f;
                waiting.Add(new Waiting { at=spot,spot=spot,yaw=face,targetYaw=face,next=clock+3f+Hash(i,4)*9f });
            }
            for (int i=0;i<idleWorkers.Count;i++)
            {
                var worker=idleWorkers[i]; worker.gameObject.SetActive(i<depot.Available); if (i>=depot.Available) continue;
                var w=waiting[i];
                if (clock>=w.next)
                {
                    float roll=Hash(i,(int)(w.next*7.3f));
                    if (roll<.35f) w.targetYaw=w.yaw+(Hash(i,(int)w.next+11)-.5f)*90f;                       // turn a little
                    else if (roll<.55f)                                                                         // shift to a nearby spot
                    {
                        float a=Hash(i,(int)w.next+5)*Mathf.PI*2f; var home=Base(i);
                        w.spot=home+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(.12f+Hash(i,(int)w.next+6)*.18f); // never within about .5 m of a neighbour
                    }
                    else                                                                                        // look round at the bays
                    {
                        w.looking=!w.looking;
                        var bay=depot.Point(depot.Layout.LaneX(i%depot.ProcessingBays)+(Hash(i,(int)w.next+3)-.5f)*6f,depot.Layout.WorkZ-4f);
                        w.look=new Vector3(bay.x,1.5f,bay.y);
                    }
                    w.next=clock+4f+Hash(i,(int)w.next+2)*10f;
                }
                w.at=Vector3.MoveTowards(w.at,w.spot,.7f*dt);
                w.yaw=Mathf.MoveTowardsAngle(w.yaw,w.targetYaw,60f*dt);
                waiting[i]=w;
                var pose=RampCrewPose.Standing(w.at,Quaternion.Euler(0,w.yaw,0)*Vector3.forward);
                if (w.looking) pose.lookAt=Quaternion.Inverse(pose.rotation)*(w.look-w.at); // actor frame
                worker.Apply(pose,dt,depot.Airport.timeScale);
            }
        }
        Vector3 Base(int i)
        {
            int lane=i%depot.ProcessingBays,row=i/depot.ProcessingBays;
            var at=depot.Point(depot.Layout.LaneX(lane)+8.6f+(row%2)*1.2f+(Hash(i,1)-.5f)*.3f,depot.Layout.WallTop-7.2f-(row/2)*1.3f+(Hash(i,2)-.5f)*.3f);
            return new Vector3(at.x,0,at.y);
        }
        /// <summary>Deterministic 0..1 from a worker index and a salt.</summary>
        static float Hash(int i,int salt)
        {
            uint h=(uint)(i*73856093)^(uint)(salt*19349663)^0x9E3779B9u; h^=h>>13; h*=0x5bd1e995; h^=h>>15;
            return (h&0xFFFFFF)/(float)0x1000000;
        }
        public void Destroy()
        {
            building?.Destroy(); roof?.Destroy(); equipment?.Destroy(); paint?.Destroy();
            terminalConnection?.Destroy(); connectionOverlay?.Destroy();
            if (bagsRoot!=null) AirfieldPart.DestroyAny(bagsRoot.gameObject);
        }
    }
}
