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
        AirfieldPart building,paint;
        Transform bagsRoot;
        readonly List<Transform> bags=new List<Transform>();
        readonly List<RampCrewRig> idleWorkers=new List<RampCrewRig>();
        public BaggageDepotPresentation(BaggageDepot depot) { this.depot=depot; }
        public void Build()
        {
            var airport=depot.Airport;
            building ??= new AirfieldPart(depot.transform,"Baggage depot building");
            paint ??= new AirfieldPart(depot.transform,"Baggage depot bays");
            // Geometry is in airport coordinates; the component itself is an organisational child.
            depot.transform.localPosition=Vector3.zero; depot.transform.localRotation=Quaternion.identity;
            float low=-14,high=depot.Maximum.teams*24f-20f;
            var terminal=new Terminal { floors=1, corners=new List<Vector2> {
                depot.Point(11,low),depot.Point(23,low),depot.Point(23,high),depot.Point(11,high) } };
            terminal.SetStoreyHeight(0,4.5f);
            terminal.exterior.facade.glazing=0;
            terminal.exterior.facade.cladding=new Color(.72f,.75f,.76f);
            terminal.exterior.roof=new Color(.32f,.35f,.37f); terminal.exterior.parapet=.35f;
            var shell=new TerminalBuilder(); shell.Build(terminal);
            var mesh=shell.mesh;
            Vector3 At(float x,float y,float z) { var p=depot.Point(x,z); return new Vector3(p.x,y,p.y); }
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
            {
                var normal=Vector3.Cross(b-a,c-a).normalized;
                int first=mesh.Vertex(a,normal,color.linear,new Vector4(0,.3f,0,0));
                mesh.Vertex(b,normal,color.linear,new Vector4(0,.3f,1,0));
                mesh.Vertex(c,normal,color.linear,new Vector4(0,.3f,1,1));
                mesh.Vertex(d,normal,color.linear,new Vector4(0,.3f,0,1)); mesh.Quad(0,first,first+1,first+2,first+3);
            }
            void Box(float x,float y,float z,float width,float height,float length,Color color)
            {
                float x0=x-width*.5f,x1=x+width*.5f,z0=z-length*.5f,z1=z+length*.5f,y0=y-height*.5f,y1=y+height*.5f;
                Quad(At(x0,y0,z0),At(x0,y0,z1),At(x0,y1,z1),At(x0,y1,z0),color);
                Quad(At(x1,y0,z1),At(x1,y0,z0),At(x1,y1,z0),At(x1,y1,z1),color);
                Quad(At(x0,y0,z1),At(x1,y0,z1),At(x1,y1,z1),At(x0,y1,z1),color);
                Quad(At(x1,y0,z0),At(x0,y0,z0),At(x0,y1,z0),At(x1,y1,z0),color);
                Quad(At(x0,y1,z1),At(x1,y1,z1),At(x1,y1,z0),At(x0,y1,z0),color);
            }
            var steel=new Color(.27f,.3f,.32f); var safety=new Color(.93f,.65f,.08f);
            var marking=new PaintBuilder();
            for (int i=0;i<depot.Maximum.teams;i++)
            {
                float z=i*24f-6f;
                // Dark recess, roller shutter and two short conveyors are distinguishable from the road loop.
                Box(10.96f,1.9f,z,.08f,3.5f,5.2f,new Color(.075f,.095f,.1f));
                for (float y=2.2f;y<3.6f;y+=.16f) Box(10.89f,y,z,.08f,.1f,5f,steel);
                Box(9.7f,3.85f,z,3.1f,.18f,7f,steel);
                foreach (float dz in new[] { -3f,3f }) Box(8.4f,1.85f,z+dz,.12f,3.7f,.12f,safety);
                foreach (float dz in new[] { -.9f,.9f })
                {
                    Box(8.6f,.68f,z+dz,4.8f,.18f,.62f,steel);
                    for (float x=6.3f;x<10.8f;x+=.45f) Box(x,.79f,z+dz,.06f,.08f,.64f,new Color(.58f,.61f,.62f));
                    foreach (float x in new[] { 6.8f,10f }) Box(x,.33f,z+dz,.1f,.66f,.45f,steel);
                }
                marking.Text(depot.Point(5,i*24f+1.8f),depot.Facing,"BAY "+(i+1),.35f,PaintBuilder.White,.7f);
                marking.Line(depot.Point(6.3f,i*24f-13f),depot.Point(6.3f,i*24f+3f),.075f,PaintBuilder.White);
                marking.Text(depot.Point(8,i*24f-10.5f),depot.Facing,"IN",.25f,PaintBuilder.White,.5f);
                marking.Text(depot.Point(8,i*24f-1.7f),depot.Facing,"OUT",.25f,PaintBuilder.White,.5f);
            }
            building.Set(mesh.Apply(building.Target,"Baggage depot",false,2),airport.building);
            building.Renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            paint.Set(marking.Apply(paint.Target,"Baggage depot bays"),airport.paint);
            var apron=ApronArea.Rect(depot.Point(13.5f,(low+high)*.5f),new Vector2(37,high-low+10),depot.bearing,PavementSurface.Concrete);
            if (apron.outline.Any(p=>!airport.aprons.Any(a=>a.surface.IsPaved() && a.Contains(p))))
            { airport.aprons.Add(apron); airport.BuildGround(); }
        }

        public void Present()
        {
            var teams=depot.Teams; if (teams.Length==0 || teams[0].bagPrefabs==null || teams[0].bagPrefabs.Length==0) return;
            var fleet=depot.Airport.Baggage.Fleet(teams[0]); if (fleet==null) return;
            if (bagsRoot==null)
            {
                bagsRoot=new GameObject("Depot luggage (runtime)") { hideFlags=HideFlags.DontSave }.transform;
                bagsRoot.SetParent(depot.Airport.transform,false);
            }
            int used=0;
            void Store(BaggageStore store,bool incoming)
            {
                int count=Mathf.Min(36,depot.VisibleCount(store));
                for (int i=0;i<count;i++)
                {
                    if (used==bags.Count) bags.Add(Object.Instantiate(teams[0].bagPrefabs[used%teams[0].bagPrefabs.Length],bagsRoot).transform);
                    var bag=bags[used++]; bag.gameObject.SetActive(true);
                    var at=depot.Point(7.3f+i%3*.65f,incoming ? -12f+i/3%4*.75f : 1f+i/3%4*.75f);
                    var rotation=Quaternion.LookRotation(new Vector3(depot.Facing.x,0,depot.Facing.y))*Quaternion.Euler(90,0,0);
                    var bounds=bag.GetComponent<MeshFilter>().sharedMesh.bounds;
                    bag.localRotation=rotation;
                    bag.localPosition=new Vector3(at.x,.12f+i/12*.32f+ bounds.extents.z,at.y)-rotation*bounds.center;
                }
            }
            Store(fleet.Arrivals,true); Store(fleet.Dispatch,false);
            for (int i=used;i<bags.Count;i++) bags[i].gameObject.SetActive(false);
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
                    int bay=i%depot.Maximum.teams,row=i/depot.Maximum.teams;
                    var at=depot.Point(10.1f,bay*24f+4.5f+row*.8f);
                    worker.Apply(RampCrewPose.Standing(new Vector3(at.x,0,at.y),new Vector3(-depot.Right.x,0,-depot.Right.y)));
                }
            }
        }
        public void Destroy()
        {
            building?.Destroy(); paint?.Destroy();
            if (bagsRoot!=null) AirfieldPart.DestroyAny(bagsRoot.gameObject);
        }
    }
}
