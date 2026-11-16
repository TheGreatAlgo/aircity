using System.Globalization;
using AirCity.Game;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AirCity.Dev
{
    public partial class DevAirfieldTool
    {
        RoadBuildSettings roadBuildSettings=RoadBuildSettings.Default;
        float roadElevation;
        float? roadDatum;
        bool roadUnderground;
        float roadKeyHoldStart,roadKeyHoldLast;
        int roadKeyDirection;
        public RoadBuildMode RoadMode=>roadBuildSettings.mode;
        public float RoadElevation=>roadElevation;
        public float RoadBend=>roadBuildSettings.bend;
        public bool RoadUnderground=>roadUnderground;
        public void SetRoadMode(RoadBuildMode mode)
        {
            roadBuildSettings.mode=mode;
            if(mode==RoadBuildMode.Bridge && roadElevation<1f)roadElevation=8f;
            if(mode==RoadBuildMode.Tunnel && roadElevation> -1f)roadElevation=-10f;
            if(mode==RoadBuildMode.Ground)roadElevation=0;
            if(mode==RoadBuildMode.Tunnel)SetRoadUnderground(true);else if(mode==RoadBuildMode.Bridge||mode==RoadBuildMode.Ground)SetRoadUnderground(false);
            InvalidateRoadPlan();
        }
        public void SetRoadElevation(float value){value=Mathf.Clamp(value,-150f,150f);if(value==roadElevation)return;roadElevation=value;InvalidateRoadPlan();}
        public void SetRoadBend(float value){roadBuildSettings.bend=Mathf.Clamp(value,-500f,500f);InvalidateRoadPlan();}
        void InvalidateRoadPlan(){streetQuoteKey=default;streetPreviewBuilt=default;airsideQuoteKey=default;}
        float RoadTargetHeight(Vector2 p)
        {
            if(roadDatum.HasValue&&(roadBuildSettings.mode==RoadBuildMode.Bridge||roadBuildSettings.mode==RoadBuildMode.Tunnel||roadBuildSettings.mode==RoadBuildMode.Automatic&&Mathf.Abs(roadElevation)>.01f))return roadDatum.Value+roadElevation;
            if(game==null)return roadElevation;float height=game.valley.DisplayHeight(p);
            if(roadBuildSettings.mode!=RoadBuildMode.Tunnel&&game.State.world.IsWater(p))height=Mathf.Max(height,game.State.world.WaterSurfaceAt(p));
            return height+roadElevation;
        }
        public void SetRoadUnderground(bool shown)
        {
            roadUnderground=shown;UpdateUndergroundView();
        }
        void UpdateUndergroundView()
        {
            if(game==null)return;
            var view=game.GetComponent<UndergroundRoadView>();
            if(view==null && !roadUnderground)return;
            if(view==null)view=game.gameObject.AddComponent<UndergroundRoadView>();
            float height=RoadTargetHeight(Geo.XZ(lastGround))+2.5f;
            view.Show(roadUnderground&&(tool==DevTool.Street||tool==DevTool.Road||tool==DevTool.Select||tool==DevTool.Delete),height);
        }
        bool PickRoadLevel(Ray ray,float maximum,out Vector3 point)
        {
            point=default;float best=maximum;bool found=false;
            if(tool!=DevTool.Street&&tool!=DevTool.Road&&tool!=DevTool.Select&&tool!=DevTool.Delete)return false;
            var roads=Streets.network;
            foreach(var edge in roads.segments)
                if(roads.Path(edge).Raycast(ray,edge.Type.FullWidth*.5f,out float t,.18f)&&t<best)
                {
                    var p=ray.GetPoint(t);if(roadUnderground&&game!=null&&p.y>game.GetComponent<UndergroundRoadView>().Height)continue;
                    best=t;point=p;found=true;
                }
            var transform=airport.transform;var local=new Ray(transform.InverseTransformPoint(ray.origin),transform.InverseTransformDirection(ray.direction));
            foreach(var edge in airport.Taxiways.edges)
                if(airport.Taxiways.Path(edge).Raycast(local,edge.width*.5f,out float t,.01f))
                {
                    var p=transform.TransformPoint(local.GetPoint(t));float distance=Vector3.Distance(ray.origin,p);if(distance>=best)continue;
                    if(roadUnderground&&game!=null&&p.y>game.GetComponent<UndergroundRoadView>().Height)continue;
                    best=distance;point=p;found=true;
                }
            return found;
        }
        bool SnapRoadLayer(float height,Vector2 point)
        {
            bool drawing=tool==DevTool.Street?streetChainPoint.HasValue:chainNode>=0;
            if(!drawing&&roadBuildSettings.mode==RoadBuildMode.Automatic&&Mathf.Abs(roadElevation)<.1f)return Mathf.Abs(height-lastGround.y)<3f;
            return Mathf.Abs(height-RoadTargetHeight(point))<3f;
        }

        void RoadElevationKeys(Keyboard keys)
        {
            if(tool==DevTool.Road&&chainNode<0 || tool==DevTool.Street&&!streetChainPoint.HasValue)roadDatum=null;
            if(tool!=DevTool.Street && tool!=DevTool.Road){UpdateUndergroundView();return;}
            int direction=keys.pageUpKey.isPressed?1:keys.pageDownKey.isPressed?-1:0;
            float now=Time.unscaledTime;
            if(direction==0)roadKeyDirection=0;
            else if(roadKeyDirection!=direction)
            {roadKeyDirection=direction;roadKeyHoldStart=roadKeyHoldLast=now;SetRoadElevation(roadElevation+direction*.5f);}
            else
            {float rate=CareerHud.BrushHeightRate(now-roadKeyHoldStart);SetRoadElevation(roadElevation+direction*rate*Mathf.Min(.1f,now-roadKeyHoldLast));roadKeyHoldLast=now;}
            UpdateUndergroundView();
        }
    }
    public partial class DevToolbar
    {
        string roadHeightText;
        int heldRoadHeight,roadHeightFrame=-1;
        float heldRoadStart,heldRoadLast;
        void RoadBuildControls()
        {
            Section("STRUCTURE");
            Buttons(B("Auto",()=>airfield.SetRoadMode(RoadBuildMode.Automatic),"Choose ground, bridge or tunnel from the path and landscape",airfield.RoadMode==RoadBuildMode.Automatic),
                B("Ground",()=>airfield.SetRoadMode(RoadBuildMode.Ground),"Grade the ground beneath the road",airfield.RoadMode==RoadBuildMode.Ground));
            Buttons(B("Bridge",()=>airfield.SetRoadMode(RoadBuildMode.Bridge),"Build a supported deck above the landscape",airfield.RoadMode==RoadBuildMode.Bridge),
                B("Tunnel",()=>airfield.SetRoadMode(RoadBuildMode.Tunnel),"Build a lined tunnel with open entrances",airfield.RoadMode==RoadBuildMode.Tunnel));
            Prop("Elevation",r=>
            {
                float bw=U(30),gap=U(4);bool down=RoadHeightButton(new Rect(r.x,r.y,bw,r.height),"−");
                var field=new Rect(r.x+bw+gap,r.y,r.width-(bw+gap)*2,r.height);
                const string control="RoadElevation";bool focused=GUI.GetNameOfFocusedControl()==control;
                if(!focused)roadHeightText=airfield.RoadElevation.ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture);
                GUI.SetNextControlName(control);string next=GUI.TextField(field,roadHeightText??"",12,DevGui.Small);
                if(focused)DevGui.KeyboardCaptured=true;
                if(next!=roadHeightText){roadHeightText=next;if(float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out float value)&&!float.IsNaN(value)&&!float.IsInfinity(value))airfield.SetRoadElevation(value);}
                bool up=RoadHeightButton(new Rect(r.xMax-bw,r.y,bw,r.height),"+");RepeatRoadHeight(down?-1:up?1:0);
            });
            Note("Metres relative to road start · Page Up / Down · hold to move faster");
            Buttons(B(airfield.RoadUnderground?"Surface view":"Underground view",()=>airfield.SetRoadUnderground(!airfield.RoadUnderground),"Reveal roads below the landscape",airfield.RoadUnderground));
            Prop("Curve",r=>
            {
                float value=GUI.HorizontalSlider(r,airfield.RoadBend,-150f,150f);
                if(Mathf.Abs(value-airfield.RoadBend)>.1f)airfield.SetRoadBend(Mathf.Abs(value)<2f?0:Mathf.Round(value));
            });
            Buttons(B("Straight",()=>airfield.SetRoadBend(0),"Reset the curve",Mathf.Abs(airfield.RoadBend)<.1f));
        }
        bool RoadHeightButton(Rect r,string label)
        {
            int id=GUIUtility.GetControlID(label.GetHashCode(),FocusType.Passive,r);var e=Event.current;
            if(e.type==EventType.MouseDown&&e.button==0&&r.Contains(e.mousePosition)){GUIUtility.hotControl=id;e.Use();}
            if(e.type==EventType.MouseUp&&e.button==0&&GUIUtility.hotControl==id){GUIUtility.hotControl=0;e.Use();}
            bool held=GUIUtility.hotControl==id&&Mouse.current!=null&&Mouse.current.leftButton.isPressed;
            DevGui.Fill(r,DevGui.GUI(held?DevGui.Accent:DevGui.Surface),U(6));GUI.Label(r,label,Centred);return held;
        }
        void RepeatRoadHeight(int direction)
        {
            if(direction==0){heldRoadHeight=0;return;}float now=Time.unscaledTime;
            if(heldRoadHeight!=direction)
            {heldRoadHeight=direction;heldRoadStart=heldRoadLast=now;roadHeightFrame=Time.frameCount;GUI.FocusControl(null);airfield.SetRoadElevation(airfield.RoadElevation+direction*.5f);}
            else if(roadHeightFrame!=Time.frameCount)
            {airfield.SetRoadElevation(airfield.RoadElevation+direction*CareerHud.BrushHeightRate(now-heldRoadStart)*Mathf.Min(.1f,now-heldRoadLast));heldRoadLast=now;roadHeightFrame=Time.frameCount;}
        }
    }
}
