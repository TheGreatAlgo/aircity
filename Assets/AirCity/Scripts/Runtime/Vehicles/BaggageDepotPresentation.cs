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

        public void Present()
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
            if (teams[0].crewPrefab!=null)
            {
                while (idleWorkers.Count<depot.Available)
                {
                    var worker=Object.Instantiate(teams[0].crewPrefab,bagsRoot).GetComponent<RampCrewRig>();
                    worker.Initialize(idleWorkers.Count); worker.name="Available depot worker"; idleWorkers.Add(worker);
                }
                for (int i=0;i<idleWorkers.Count;i++)
                {
                    var worker=idleWorkers[i]; worker.gameObject.SetActive(i<depot.Available); if (i>=depot.Available) continue;
                    int lane=i%depot.ProcessingBays,row=i/depot.ProcessingBays;
                    var at=depot.Point(depot.Layout.LaneX(lane)+8.8f,depot.Layout.WallTop-7-row*.75f);
                    worker.Apply(RampCrewPose.Standing(new Vector3(at.x,0,at.y),new Vector3(-depot.Right.x,0,-depot.Right.y)));
                }
            }
        }
        public void Destroy()
        {
            building?.Destroy(); roof?.Destroy(); equipment?.Destroy(); paint?.Destroy();
            terminalConnection?.Destroy(); connectionOverlay?.Destroy();
            if (bagsRoot!=null) AirfieldPart.DestroyAny(bagsRoot.gameObject);
        }
    }
}
