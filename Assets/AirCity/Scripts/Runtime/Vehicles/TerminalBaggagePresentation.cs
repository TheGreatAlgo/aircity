using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Exterior service portals and a covered conveyor bridge; underground routes use an optional overlay.</summary>
    sealed class TerminalBaggagePresentation
    {
        public readonly MeshData mesh=new MeshData();
        public readonly PaintBuilder overlay=new PaintBuilder();
        readonly Color trim=new Color(.13f,.31f,.43f);
        Color panel=new Color(.65f,.69f,.7f);
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {
            var normal=Vector3.Cross(b-a,c-a).normalized; int first=mesh.Count;
            foreach (var p in new[] { a,b,c,d }) mesh.Vertex(p,normal,color.linear,new Vector4(0,.35f,p.x,p.y));
            mesh.Quad(0,first,first+1,first+2,first+3);
        }
        void Beam(Vector3 a,Vector3 b,float width,float height,Color color)
        {
            var forward=(b-a).normalized;
            if (forward.sqrMagnitude<.1f) return;
            var side=Vector3.Cross(Vector3.up,forward).normalized;
            if (side.sqrMagnitude<.1f) side=Vector3.right;
            var up=Vector3.Cross(forward,side).normalized;
            side*=width*.5f; up*=height*.5f;
            var corners=new[] { side+up,-side+up,-side-up,side-up };
            for (int i=0;i<4;i++) { int j=(i+1)%4; Quad(a+corners[j],b+corners[j],b+corners[i],a+corners[i],color); }
            Quad(a+corners[3],a+corners[2],a+corners[1],a+corners[0],color);
            Quad(b+corners[0],b+corners[1],b+corners[2],b+corners[3],color);
        }
        public void Build(TerminalBaggageLink link)
        {
            if (link==null) return;
            panel=link.Facility.Terminal.exterior.facade.cladding;
            var depot=link.Depot; var start=link.HallPort; var end=link.TerminalPort(true);
            var direction=new Vector3(end.x-start.x,0,end.z-start.z).normalized;
            if (direction.sqrMagnitude<.1f) direction=new Vector3(depot.Right.x,0,depot.Right.y);
            // Opaque hatches imply an internal conveyor without exposing a terminal interior.
            Beam(start-direction*.12f,start+direction*.18f,2.4f,1.55f,trim);
            Beam(end-direction*.18f,end+direction*.12f,2.4f,1.55f,trim);
            if (depot.connection.kind==BaggageConnectionKind.Covered)
            {
                float height=Mathf.Max(6.6f,start.y,end.y);
                var a=new Vector3(start.x,height,start.z); var b=new Vector3(end.x,height,end.z);
                Beam(start,a,2,1.3f,panel); Beam(a,b,2,1.3f,panel); Beam(b,end,2,1.3f,panel);
                Beam(a-Vector3.up*.45f,b-Vector3.up*.45f,2.08f,.12f,trim);
                for (float d=2;d<Vector3.Distance(a,b)-1;d+=4)
                {
                    var at=Vector3.Lerp(a,b,d/Vector3.Distance(a,b));
                    Beam(at-direction*.035f,at+direction*.035f,2.04f,1.34f,trim);
                }
            }
            var p=new Vector2(start.x,start.z); var q=new Vector2(end.x,end.z);
            float length=Vector2.Distance(p,q);
            for (float d=0;d<length;d+=3)
                overlay.Line(Vector2.Lerp(p,q,d/Mathf.Max(.01f,length)),Vector2.Lerp(p,q,Mathf.Min(length,d+1.5f)/Mathf.Max(.01f,length)),.18f,new Color(.15f,.62f,.83f));
            overlay.Text((p+q)*.5f,(q-p).normalized,"BAGGAGE",.55f,PaintBuilder.White,.9f);
        }
    }
}
