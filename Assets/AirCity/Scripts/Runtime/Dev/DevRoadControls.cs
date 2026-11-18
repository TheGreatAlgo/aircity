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
            // following the ground over water means a low bridge (LowDeck over the surface), enough to come down onto a
            // low bank without dipping under the water clearance
            if(roadBuildSettings.mode!=RoadBuildMode.Tunnel&&game.State.world.IsWater(p))height=Mathf.Max(height,game.State.world.WaterSurfaceAt(p)+LowDeck);
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
        bool RoadDrawing=>tool==DevTool.Street?streetChainPoint.HasValue:chainNode>=0;
        /// <summary>The level the cursor works at (world): before a chain in Automatic at 0, whatever it is on;
        /// otherwise where the next free point would go.</summary>
        float RoadWorkingLevel(Vector2 point)=>!RoadDrawing&&roadBuildSettings.mode==RoadBuildMode.Automatic&&Mathf.Abs(roadElevation)<.1f?lastGround.y:RoadTargetHeight(point);
        bool SnapRoadLayer(float height,Vector2 point)=>Mathf.Abs(height-RoadWorkingLevel(point))<3f;
        /// <summary>The last cursor ray (world) and how far along it the visible ground is (+∞ in the underground view
        /// or where pavement clips the terrain): set by Ground.</summary>
        Ray roadRay;
        float roadRayGround=float.PositiveInfinity;
        /// <summary>Where the cursor ray crosses a level (world x, z), unless that is behind the ground it meets first.</summary>
        bool RoadRayAt(float height,out Vector2 at)
        {
            at=default;if(Mathf.Abs(roadRay.direction.y)<.0001f)return false;
            float t=(height-roadRay.origin.y)/roadRay.direction.y;
            if(t<0f||t>roadRayGround+.5f)return false;
            at=Geo.XZ(roadRay.GetPoint(t));return true;
        }
        /// <summary>A road at another level is joined only where the player points at it: the cursor ray, at that road's
        /// own level, passes within `within` of it (world x, z), and that point is not hidden behind the ground. In the
        /// underground view the cut ground hides nothing, and the roads it cuts away are outlined (DrawRoadLevelGuide).
        /// So drawing over or under a road crosses it unless the cursor is put on it, and a buried tunnel is never
        /// joined from the surface view.</summary>
        bool SnapAcrossLevels(float height,Vector2 world,float within)=>RoadRayAt(height,out var at)&&Vector2.Distance(at,world)<=within;
        /// <summary>SnapAcrossLevels for a section (path in `space`, null for world): its parameter where the ray at its
        /// level comes nearest, and that point's height (world).</summary>
        bool SnapAcrossSection(Alignment3D path,Transform space,float within,out float t,out float height)
        {
            var offset=space!=null?space.position:Vector3.zero;t=0f;height=0f;
            float y=path.Height(path.ClosestParameter(Geo.XZ(roadRay.origin)-Geo.XZ(offset)))+offset.y;
            for(int i=0;i<3;i++)
            {
                if(!RoadRayAt(y,out var at))return false;
                var local=at-Geo.XZ(offset);t=path.ClosestParameter(local);height=path.Height(t)+offset.y;
                if(Mathf.Abs(height-y)<.25f)return path.DistanceTo(local)<=within;
                y=height;
            }
            return false;
        }
        /// <summary>Ranks a road at another level after every one at the working level: the nearest level first.</summary>
        static float AcrossRank(float height,float level,float distance)=>1e5f+Mathf.Abs(height-level)*100f+distance;
        /// <summary>How a snapped road's level compares with the last point, for the strip and map label.</summary>
        string LevelHint(float height,float from)
        {
            float rise=height-from;
            return Mathf.Abs(rise)<=PublicRoadNetwork.JunctionHeightTolerance?"":$", {Mathf.Abs(rise):0.0} m {(rise>0?"up":"down")}: the road ramps to it";
        }

        /// <summary>In the underground view, the roads its cut hides near the cursor are outlined where they are (drawn
        /// over the cutaway); pointing at an outline joins that road (SnapAcrossLevels), so a tunnel can be brought up
        /// onto a surface road without leaving the underground view.</summary>
        void DrawRoadLevelGuide()
        {
            if(game==null || tool!=DevTool.Street&&tool!=DevTool.Road || !lineSnapShown&&tool==DevTool.Road || !streetSnapShown&&tool==DevTool.Street)return;
            var view=game.GetComponent<UndergroundRoadView>();if(view==null||!view.Visible)return;
            var centre=Geo.XZ(lastGround);const float Radius=300f;int drawn=0;
            var colour=new Color(.62f,.78f,.86f);
            bool Near(Vector3 a,Vector3 b,Vector2 control)
            {
                var min=Vector2.Min(Vector2.Min(Geo.XZ(a),Geo.XZ(b)),control);var max=Vector2.Max(Vector2.Max(Geo.XZ(a),Geo.XZ(b)),control);
                return centre.x>min.x-Radius&&centre.x<max.x+Radius&&centre.y>min.y-Radius&&centre.y<max.y+Radius;
            }
            void Outline(Alignment3D path,float half,Transform space)
            {
                int count=Mathf.Clamp(Mathf.CeilToInt(path.Length/10f),1,60);
                for(int side=-1;side<=1;side+=2)
                {
                    loop.Clear();
                    for(int i=0;i<=count;i++)
                    {var p=path.Offset(path.Length*i/count,side*half);loop.Add(space!=null?space.TransformPoint(p):p);}
                    outlines.Line(cam,loop,colour,1.5f,false,true);
                }
                drawn++;
            }
            if(tool==DevTool.Street)
            {
                var roads=Streets.network;
                foreach(var e in roads.segments)
                {
                    if(drawn>=48)break;var a=roads.Node(e.a).position;var b=roads.Node(e.b).position;
                    if(Mathf.Max(a.y,b.y)<=view.Height||!Near(a,b,e.alignment.curved?e.alignment.control:Geo.XZ(a)))continue;
                    Outline(roads.Path(e),e.Type.FullWidth*.5f,null);
                }
                return;
            }
            var net=airport.Taxiways;var origin=airport.transform.position;
            foreach(var e in net.edges)
            {
                if(drawn>=48)break;if(!e.IsRoad)continue;var a=net.Node(e.a).position+origin;var b=net.Node(e.b).position+origin;
                if(Mathf.Max(a.y,b.y)<=view.Height||!Near(a,b,(e.alignment.curved?e.alignment.control:Geo.XZ(a-origin))+Geo.XZ(origin)))continue;
                Outline(net.Path(e),e.width*.5f,airport.transform);
            }
        }

        /// <summary>A free point over water, where the road follows the ground, is a low deck this far over the surface.</summary>
        const float LowDeck=2.5f;
        float? adoptedElevation;
        float chosenElevation;
        /// <summary>The ground a chain's level is measured from: the terrain, or the water surface for anything but a tunnel.</summary>
        float RoadGround(Vector2 p)
        {
            if(game==null)return 0f;float height=game.valley.DisplayHeight(p);
            if(roadBuildSettings.mode!=RoadBuildMode.Tunnel&&game.State.world.IsWater(p))height=Mathf.Max(height,game.State.world.WaterSurfaceAt(p));
            return height;
        }
        /// <summary>
        /// A chain's level follows the roads it is clicked onto. Its datum is the ground where it starts (or last joined
        /// a road). Starting on, or joining, a bridge, a tunnel or a ramp more than AtGrade off the ground takes that
        /// road's level as the elevation, so the next point continues level with it; joining a ground road returns to 0
        /// (Automatic then follows the ground again) and closes the underground view. Starting on a ground road keeps the
        /// elevation the player chose, so pressing Bridge first climbs away from it. Ground mode always follows the ground. Positions are world.
        /// The chosen elevation comes back when the chain ends (RoadElevationKeys) unless the player changed it since.
        /// </summary>
        void AdoptRoadLevel(Vector2 at,float height,bool joined,bool structure,bool start)
        {
            if(game==null || !joined){if(start)roadDatum=height-roadElevation;return;}
            float ground=RoadGround(at);roadDatum=ground;
            if(roadBuildSettings.mode==RoadBuildMode.Ground)return;
            bool raised=structure||Mathf.Abs(height-ground)>RoadConstructionPlan.AtGrade;
            if(!raised&&start)return;
            if(!adoptedElevation.HasValue)chosenElevation=roadElevation;
            SetRoadElevation(raised?height-ground:0f);adoptedElevation=roadElevation;
            // back on a surface road: the cutaway has nothing left to show
            if(!raised&&roadUnderground)SetRoadUnderground(false);
        }

        void RoadElevationKeys(Keyboard keys)
        {
            if(tool==DevTool.Road&&chainNode<0 || tool==DevTool.Street&&!streetChainPoint.HasValue || tool!=DevTool.Road&&tool!=DevTool.Street)
            {
                roadDatum=null;
                if(adoptedElevation.HasValue){if(roadElevation==adoptedElevation.Value)SetRoadElevation(chosenElevation);adoptedElevation=null;}
            }
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
            Note("Metres above the ground where the road starts · click a road to take its level · Page Up / Down");
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
