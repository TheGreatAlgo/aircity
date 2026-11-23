using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Dev;
using AirCity.EditorTools.Game;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;
using F = AirCity.EditorTools.Game.RoadConnectionFixture;
using Object = UnityEngine.Object;

namespace AirCity.Tests.Game
{
    /// <summary>
    /// The bridge and tunnel connection flows (a to e) driven through the real Street and Road tools, one pointer
    /// frame at a time (RoadToolDriver), in the order a player would click. Each flow writes what the cursor snapped
    /// to, any refusal and what was built to Logs/road-connection-flows.md, then checks the result: joined to the
    /// roads clicked, height-separated crossings kept apart, structures where the ground says so.
    /// </summary>
    public sealed class RoadConnectionToolTests
    {
        const string Log="Logs/road-connection-flows.md";
        readonly List<GameObject> spawned=new List<GameObject>();
        CareerGame game;
        RoadToolDriver drive;
        int actions;
        [SetUp]public void Create(){game=F.Create(spawned);drive=new RoadToolDriver(game);actions=0;}
        [TearDown]public void Cleanup(){drive?.Dispose();foreach(var go in spawned)if(go!=null)Object.DestroyImmediate(go);spawned.Clear();Shader.SetGlobalFloat("_AirCityUndergroundCut",0);}

        /// <summary>UndergroundRoadView.Show destroys its grid plane's collider with Destroy, which Unity reports as an
        /// error outside Play mode (the first time the view opens in each fixture).</summary>
        static void Write(string line){Directory.CreateDirectory("Logs");File.AppendAllText(Log,line+"\n");}
        void Heading(string flow){Write($"\n### {flow}\n");Write("| # | action | result |");Write("|---|---|---|");}
        void Row(string action,string result=null){actions++;Write($"| {actions} | {action} | {result??drive.Describe()} |");}
        void Mode(RoadBuildMode mode){drive.tool.SetRoadMode(mode);Row($"press {mode}",$"elevation={drive.tool.RoadElevation:+0.0;-0.0;0.0}, underground view={drive.Underground}");}

        PublicRoadNetwork Roads=>drive.tool.Streets.network;
        static float RoadY(PublicRoadNetwork g,Vector2 p)
        {
            var edge=g.segments.OrderBy(e=>g.Path(e).DistanceTo(p)).First();var path=g.Path(edge);return path.Height(path.ClosestParameter(p));
        }
        int NodeAt(PublicRoadNetwork g,Vector2 p)=>g.nodes.Where(n=>Vector2.Distance(PublicRoadNetwork.XZ(n.position),p)<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
        void ClickRoad(string what,Vector2 p){float y=RoadY(Roads,p);drive.HoverRoad(p,y);string hover=drive.Describe();int chain=drive.Chain;drive.ClickRoad(p,y);
            Row($"click {what}",$"hover: {hover}; click {(drive.Chain!=chain?"accepted":"refused or ignored")}");}
        void ClickFree(string what,Vector2 p){drive.HoverFree(p);string hover=drive.Describe();int chain=drive.Chain;drive.ClickFree(p);
            Row($"click {what}",$"hover: {hover}; click {(drive.Chain!=chain?"accepted":"refused or ignored")}");}
        string Built(PublicRoadNetwork before)
        {
            var old=new HashSet<int>(before.segments.Select(e=>e.id));
            var made=Roads.segments.Where(e=>!old.Contains(e.id)).OrderBy(e=>Roads.Node(e.a).position.x+Roads.Node(e.a).position.z).ToList();
            return made.Count==0?"nothing":string.Join(", ",made.Select(e=>$"{e.structure} {Roads.Path(e).Length:0} m ({Roads.Node(e.a).position.y:0.0}→{Roads.Node(e.b).position.y:0.0})"));
        }
        List<PublicRoadSegment> Made(PublicRoadNetwork before)=>Roads.segments.Where(e=>!before.segments.Any(o=>o.id==e.id)).ToList();
        void Result(string text){Write($"\n**Built:** {text}  \n**Actions:** {actions} (tool already chosen)\n");}

        [TestCase(DevTool.Street, false)]
        [TestCase(DevTool.Street, true)]
        [TestCase(DevTool.Road, false)]
        [TestCase(DevTool.Road, true)]
        public void EndpointGesturePlacesThePreviewedStraightOrCurve(DevTool tool, bool curve)
        {
            drive.tool.SetTool(tool);
            var start = new Vector2(260f, 360f);
            var end = new Vector2(460f, 360f);
            drive.ClickFree(start);
            int anchor = drive.Chain;
            var target = Geo.X0Z(end, game.valley.DisplayHeight(end));
            drive.GestureFrame(target, true, centre: true);
            var previewEnd = drive.Endpoint;
            Assert.AreEqual(anchor, drive.Chain);

            var pointer = target + (curve ? Vector3.forward * 30f : Vector3.zero);
            drive.GestureFrame(pointer, true);
            Assert.AreEqual(previewEnd, drive.Endpoint, "Dragging must keep the snapped endpoint.");
            Assert.IsNull(drive.Problem);
            Assert.AreEqual(anchor, drive.Chain, "The draft is committed on release.");
            drive.GestureFrame(pointer, false);
            Assert.AreNotEqual(anchor, drive.Chain);

            Alignment3D path;
            if (tool == DevTool.Street)
                path = Roads.Path(Roads.At(drive.Chain).Single());
            else
            {
                var net = game.Airport.Taxiways;
                path = net.Path(net.EdgesAt(drive.Chain).Single());
            }
            Assert.AreEqual(curve, path.shape.curved);
            if (curve) Assert.Greater(path.Horizontal(.5f).y, (path.start.z + path.end.z) * .5f + 15f);
        }

        [TestCase(DevTool.Street, false)]
        [TestCase(DevTool.Street, true)]
        [TestCase(DevTool.Road, false)]
        public void PointsIntoTheHillKeepTheirWorkingHeightAndBuildATunnel(DevTool tool, bool elevated)
        {
            drive.tool.SetTool(tool);
            var start = new Vector2(-1630f, -900f);
            var hill = new Vector2(-1525f, -895f);
            drive.ClickFree(start);
            float level = drive.Endpoint.y;
            if (elevated) drive.tool.StepRoadElevation(1);
            Assert.AreEqual(elevated ? 5f : 0f, drive.tool.RoadElevation);
            int anchor = drive.Chain;
            drive.HoverFree(hill);
            Assert.IsNull(drive.Problem);
            Assert.AreEqual(level + (elevated ? 5f : 0f), drive.Endpoint.y, .15f);
            drive.ClickFree(hill);
            Assert.AreNotEqual(anchor, drive.Chain);
            bool tunnel = tool == DevTool.Street
                ? Roads.At(drive.Chain).Any(edge => edge.structure == RoadStructure.Tunnel)
                : game.Airport.Taxiways.EdgesAt(drive.Chain).Any(edge => edge.structure == RoadStructure.Tunnel);
            Assert.IsTrue(tunnel);
        }

        [TestCase(DevTool.Street)]
        [TestCase(DevTool.Road)]
        public void ReopenedUndergroundViewContinuesADeepTunnelFromItsEnd(DevTool tool)
        {
            drive.tool.SetTool(tool);
            var net = game.Airport.Taxiways;
            var origin = game.Airport.transform.position;
            var start = new Vector2(300f, 360f);
            var tip = new Vector2(410f, 360f);
            float floor = origin.y - 185f;
            var from = Geo.X0Z(start, floor);
            var end = Geo.X0Z(tip, floor);
            // An upper road projects across the cap with the driver's usual camera angle.
            var above = Geo.X0Z(tip + Vector2.down * (185f * 100f / 220f), origin.y);
            int endNode;
            if (tool == DevTool.Street)
            {
                endNode = Roads.AddNode(end);
                Roads.AddSegment(Roads.AddNode(from), endNode, PublicRoadStyle.Urban, RoadStructure.Tunnel);
                Roads.AddSegment(Roads.AddNode(above), Roads.AddNode(above + Vector3.right * 120f),
                    PublicRoadStyle.Urban, RoadStructure.Ground);
            }
            else
            {
                endNode = net.AddNode(end - origin);
                net.Connect(net.AddNode(from - origin), endNode, RunwayRules.RoadWidth, PavementSurface.Asphalt,
                    road: RoadClass.Service, structure: RoadStructure.Tunnel);
                net.Connect(net.AddNode(above - origin), net.AddNode(above - origin + Vector3.right * 120f),
                    RunwayRules.RoadWidth, PavementSurface.Asphalt, road: RoadClass.Service);
            }

            drive.HoverFree(new Vector2(480f, 460f));
            drive.tool.SetRoadUnderground(true);
            drive.tool.SetRoadUnderground(false);
            drive.tool.SetRoadUnderground(true);
            var view = game.GetComponent<UndergroundRoadView>();
            float cut = view.Height;
            drive.HoverFree(F.HillCentre);
            Assert.AreEqual(cut, view.Height, .01f, "Hovering over higher terrain moved the cutaway.");

            drive.ClickRoad(tip + Vector2.right * 2f, floor);
            Assert.AreEqual(endNode, drive.Chain, "The visible tunnel cap should take priority over the upper road.");
            var extension = tip + Vector2.right * 120f;
            drive.HoverFree(extension);
            Assert.IsNull(drive.Problem);
            Assert.AreEqual(floor, drive.Endpoint.y, .01f, "The existing floor can be deeper than the elevation stepper's range.");
            Assert.AreEqual(floor + UndergroundRoadView.Headroom, view.Height, .01f);
            drive.ClickFree(extension);
            Assert.AreNotEqual(endNode, drive.Chain);
            if (tool == DevTool.Street)
                Assert.That(Roads.At(endNode).Any(e => e.structure == RoadStructure.Tunnel && (e.a == drive.Chain || e.b == drive.Chain)));
            else
                Assert.That(net.EdgesAt(endNode).Any(e => e.structure == RoadStructure.Tunnel && TaxiwayNetwork.Other(e, endNode) == drive.Chain));
        }

        [TestCase(HangarSize.Small, 7.5f, 16f)]
        [TestCase(HangarSize.Executive, 15f, 60f)]
        [TestCase(HangarSize.Airline, 25f, 80f)]
        public void TaxiwayToolSnapsAcrossTheFrontOfAnExistingHangar(HangarSize size, float width, float span)
        {
            var door = new Vector2(100f, -60f);
            var hangar = game.Airport.AddHangar(door, 0f, size);
            Assert.IsNotNull(hangar);
            game.State.world.RemoveGroundProps(GroundCoverScatter.Generate(game.State.world,
                area: new Rect(5f, -170f, 190f, 255f)).Select(prop => prop.id));
            drive.tool.SetTool(DevTool.Taxiway);
            drive.tool.SetWidth(width);
            drive.tool.SetSurface(PavementSurface.Asphalt);
            var centre = door + Vector2.up * (hangar.Spec.Setback + width * .5f);
            var left = centre + new Vector2(-span, 1.5f);
            var right = centre + new Vector2(span, -1.5f);
            drive.HoverFree(left);
            StringAssert.Contains("taxiway alignment", drive.Snap);
            Assert.AreEqual(centre.y, drive.Endpoint.z, .01f);
            drive.ClickFree(left);
            drive.HoverFree(right);
            StringAssert.Contains("taxiway alignment", drive.Snap);
            Assert.AreEqual(centre.y, drive.Endpoint.z, .01f);
            Assert.IsNull(drive.Problem);
            drive.ClickFree(right);

            foreach (var bay in hangar.Bays)
            {
                Assert.IsTrue(HangarConnection.TryGet(bay, null, out var access, out var why), why);
                Assert.AreEqual(bay.Door.x, access.entry.x, .01f);
                Assert.AreEqual(centre.y, access.entry.y, .01f);
                Assert.IsTrue(HangarConnection.Approach(bay, out _));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StreetToolSnapsToTheVisitorMouthAndMatchesItsHeading(bool startAtMouth)
        {
            var layout = game.State.layout;
            layout.receptionBuilt = true;
            layout.receptionSiteVersion = VisitorCentreConstruction.SiteVersion;
            layout.reception = new Vector2(60f, 90f);
            layout.receptionHeading = 30f;
            var mouth = VisitorCentreLayout.Entrance(layout.reception, layout.elevation, layout.receptionHeading);
            var outward = Geo.Right(VisitorCentreLayout.Axis(layout.receptionHeading));
            var roadEnd = Geo.XZ(mouth) + outward * 65f + Geo.Left(outward) * (startAtMouth ? 2f : 9f);
            int port = Roads.AddNode(mouth, VisitorCentreLayout.EntranceId);
            int outside = -1;
            if (!startAtMouth)
            {
                outside = Roads.AddNode(Geo.X0Z(roadEnd, mouth.y));
                int next = Roads.AddNode(Geo.X0Z(roadEnd + outward * 45f, mouth.y));
                Roads.AddSegment(outside, next, PublicRoadStyle.Urban, RoadStructure.Ground);
            }
            drive.tool.SetTool(DevTool.Street);
            drive.tool.SetStreetStyle(PublicRoadStyle.Urban);
            drive.tool.SetRoadMode(RoadBuildMode.Ground);
            var aim = Geo.XZ(mouth) + outward * 1f;
            if (!startAtMouth) drive.ClickRoad(roadEnd, mouth.y);
            drive.HoverFree(aim);
            StringAssert.Contains("visitor centre's driveway", drive.Snap);
            Assert.Less(Vector3.Distance(mouth, drive.Endpoint), .01f);
            Assert.IsNull(drive.Problem);
            drive.ClickFree(aim);
            if (startAtMouth)
            {
                drive.HoverFree(roadEnd);
                StringAssert.Contains("driveway alignment", drive.Snap);
                Assert.IsNull(drive.Problem);
                drive.ClickFree(roadEnd);
                outside = drive.Chain;
            }
            var driveway = Roads.At(port).Single();
            Assert.AreEqual(PublicRoadStyle.AirportAccess, driveway.style);
            Assert.AreEqual(RoadStructure.Ground, driveway.structure);
            var path = Roads.Path(driveway);
            float t = startAtMouth ? 0f : 1f;
            var tangent = Geo.XZ(path.Tangent(t)) * (startAtMouth ? 1f : -1f);
            Assert.Greater(Vector2.Dot(tangent.normalized, outward), .9999f);
            Assert.AreEqual(0f, path.Grade(t), .001f);
            Assert.IsTrue(Roads.HasRoute(port, outside));
            Assert.IsTrue(Roads.HasRoute(outside, port));
        }

        [Test]public void A_GroundRoadUpOverAStreetAndDownOntoAnotherGroundRoad()
        {
            File.WriteAllText(Log,"# Road connection flows (actual Street/Road tools, headless)\n\nUnity "+Application.unityVersion+", "+DateTime.UtcNow.ToString("u")+"\n");
            Heading("a. Bridge: R1 (ground) → over R3 → R2 (ground), Street tool, Bridge pressed first");
            var before=Roads.Clone();float z=-1300;
            drive.tool.SetTool(DevTool.Street);drive.tool.SetStreetStyle(PublicRoadStyle.Urban);
            Mode(RoadBuildMode.Bridge);
            ClickRoad("R1 (middle of the ground street)",new Vector2(-1700,z));
            ClickFree("120 m east",new Vector2(-1580,z));
            ClickFree("past R3",new Vector2(-1420,z));
            ClickRoad("R2 (middle of the ground street)",new Vector2(-1300,z));
            Result(Built(before));
            int r1=NodeAt(Roads,F.R1A),r2=NodeAt(Roads,F.R2A);
            Assert.That(r1>0&&r2>0&&Roads.HasRoute(r1,r2),"R1 and R2 are not connected through the bridge (it must start on R1 and finish on R2).");
            Assert.That(NodeAt(Roads,new Vector2(-1500,z)),Is.EqualTo(-1),"The bridge made a junction with R3 below it.");
            var made=Made(before);Assert.That(made.Any(e=>e.structure==RoadStructure.Elevated),"No bridge section was built.");
            Assert.That(Roads.Validate(out string why),why);
        }

        [Test]public void B_ContinueBranchAndJoinBridgesAtDeckLevel()
        {
            Heading("b. Bridges: continue from B1's end, join B2's middle, branch from B1's middle (Auto, elevation 0)");
            var before=Roads.Clone();
            drive.tool.SetTool(DevTool.Street);drive.tool.SetRoadMode(RoadBuildMode.Automatic);drive.tool.SetRoadElevation(0);
            ClickRoad("B1's east end (deck +10 m)",F.B1B);
            drive.HoverFree(new Vector2(-1400,-1700));float continued=drive.Endpoint.y;
            ClickFree("100 m further east",new Vector2(-1400,-1700));
            ClickRoad("B2's middle (deck +10 m)",new Vector2(-1300,-1700));
            drive.EndChain();
            ClickRoad("B1's middle",new Vector2(-1600,-1700));
            drive.HoverFree(new Vector2(-1600,-1580));float branched=drive.Endpoint.y;
            ClickFree("120 m north",new Vector2(-1600,-1580));
            drive.EndChain();
            Result(Built(before)+$"; continuation endpoint at {continued:0.0} m, branch endpoint at {branched:0.0} m (deck {F.BridgeLevel:0.0} m)");
            Assert.That(continued,Is.EqualTo(F.BridgeLevel).Within(.3f),"Continuing from a bridge end did not stay at deck level.");
            Assert.That(branched,Is.EqualTo(F.BridgeLevel).Within(.3f),"Branching from a bridge did not stay at deck level.");
            int b1=NodeAt(Roads,F.B1A),b2=NodeAt(Roads,F.B2A);
            Assert.That(b1>0&&b2>0&&Roads.HasRoute(b1,b2),"B1 and B2 are not joined.");
            Assert.That(Made(before).All(e=>e.structure==RoadStructure.Elevated),"A deck-level road became a ground road.");
            Assert.That(Roads.Validate(out string why),why);
        }

        [Test]public void C_GroundRoadDownUnderAHillAndUpOntoAnotherGroundRoad()
        {
            Heading("c. Tunnel: R4 (ground) → under the hill → R5 (ground), Street tool, Tunnel pressed after the first click");
            var before=Roads.Clone();float z=-900;
            drive.tool.SetTool(DevTool.Street);drive.tool.SetRoadMode(RoadBuildMode.Automatic);drive.tool.SetRoadElevation(0);
            ClickRoad("R4 (middle of the ground street)",new Vector2(-1750,z));
            Mode(RoadBuildMode.Tunnel);
            ClickFree("130 m east (hill foot)",new Vector2(-1620,z));
            ClickFree("through the hill",new Vector2(-1380,z));
            ClickRoad("R5 (middle of the ground street)",new Vector2(-1250,z));
            Result(Built(before));
            int r4=NodeAt(Roads,F.R4A),r5=NodeAt(Roads,F.R5A);
            Assert.That(r4>0&&r5>0&&Roads.HasRoute(r4,r5),"R4 and R5 are not connected through the tunnel.");
            var made=Made(before);Assert.That(made.Any(e=>e.structure==RoadStructure.Tunnel),"No tunnel section was built.");
            Assert.That(drive.Underground,Is.False,"Coming back up onto a street should close the underground view.");
            Assert.That(made.Where(e=>Roads.Node(e.a).position.x< -1740||Roads.Node(e.b).position.x>-1260).All(e=>e.structure==RoadStructure.Ground),
                "The sections meeting the ground streets should be open ramps, not tunnel or bridge structures.");
            Assert.That(Roads.Validate(out string why),why);
        }

        [Test]public void D_TunnelJoinsATunnelAndLeavesAHillsideAsABridge()
        {
            Heading("d1. Tunnel T1's end → T2's middle, Tunnel pressed first");
            var before=Roads.Clone();
            drive.tool.SetTool(DevTool.Street);
            Mode(RoadBuildMode.Tunnel);
            ClickRoad("T1's east end (−12 m)",F.T1B);
            ClickRoad("T2's middle (−12 m)",new Vector2(-1650,-500));
            drive.EndChain();
            Result(Built(before));
            int t1=NodeAt(Roads,F.T1A),t2=NodeAt(Roads,F.T2A);
            Assert.That(t1>0&&t2>0&&Roads.HasRoute(t1,t2),"T1 and T2 are not joined.");
            Assert.That(Made(before).All(e=>e.structure==RoadStructure.Tunnel),"The underground link is not all tunnel.");

            Heading("d2. Tunnel T3's end inside the ridge → straight out over the valley, Tunnel mode");
            actions=0;before=Roads.Clone();
            ClickRoad("T3's east end inside the ridge",F.T3B);
            ClickFree("across the valley, far rim",new Vector2(-960,-500));
            drive.EndChain();
            Result(Built(before));
            var made=Made(before);
            Assert.That(made.Any(e=>e.structure==RoadStructure.Elevated),"Leaving the hillside over the valley did not become a bridge.");
            Assert.That(made.Any(e=>e.structure==RoadStructure.Tunnel),"The stretch inside the ridge did not stay a tunnel.");
            Assert.That(made.All(e=>Mathf.Abs(Roads.Node(e.a).position.y-F.RidgeTunnelLevel)<.3f&&Mathf.Abs(Roads.Node(e.b).position.y-F.RidgeTunnelLevel)<.3f),"The road left T3's level.");
            Assert.That(Roads.Validate(out string why),why);
        }

        [Test]public void E_ServiceTunnelUnderATaxiwayJoinsServiceRoadsAtBothEnds()
        {
            Heading("e. Airside: S1 (service road) → tunnel under the taxiway → S2, Road tool, Tunnel pressed after the first click");
            var net=game.Airport.Taxiways;var origin=game.Airport.transform.position;var old=new HashSet<int>(net.edges.Select(e=>e.id));
            drive.tool.SetTool(DevTool.Road);drive.tool.SetRoadMode(RoadBuildMode.Automatic);drive.tool.SetRoadElevation(0);
            Vector2 W(Vector2 local)=>local+new Vector2(origin.x,origin.z);
            void Road(string what,Vector2 local){drive.HoverRoad(W(local),origin.y);string hover=drive.Describe();int chain=drive.Chain;drive.ClickRoad(W(local),origin.y);
                Row($"click {what}",$"hover: {hover}; click {(drive.Chain!=chain?"accepted":"refused or ignored")}");}
            void Free(string what,Vector2 local){drive.HoverFree(W(local));string hover=drive.Describe();int chain=drive.Chain;drive.ClickFree(W(local));
                Row($"click {what}",$"hover: {hover}; click {(drive.Chain!=chain?"accepted":"refused or ignored")}");}
            Road("S1 (middle of the service road)",new Vector2(-200,0));
            Mode(RoadBuildMode.Tunnel);
            Free("120 m east",new Vector2(-80,0));
            Free("past the taxiway",new Vector2(80,0));
            Road("S2 (middle of the service road)",new Vector2(200,0));
            var made=net.edges.Where(e=>!old.Contains(e.id)).ToList();
            Result(made.Count==0?"nothing":string.Join(", ",made.OrderBy(e=>Mathf.Min(net.Node(e.a).position.x,net.Node(e.b).position.x)).Select(e=>$"{e.structure} {net.Path(e).Length:0} m ({net.Node(e.a).position.y:0.0}→{net.Node(e.b).position.y:0.0})")));
            int s1=net.nodes.Where(n=>Vector2.Distance(Geo.XZ(n.position),F.S1A)<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
            int s2=net.nodes.Where(n=>Vector2.Distance(Geo.XZ(n.position),F.S2A)<.6f).Select(n=>n.id).DefaultIfEmpty(-1).First();
            Assert.That(s1>=0&&s2>=0&&Connected(net,s1,s2),"S1 and S2 are not connected through the tunnel (it must start on S1 and finish on S2).");
            Assert.That(net.nodes.Any(n=>Vector2.Distance(Geo.XZ(n.position),Vector2.zero)<1f),Is.False,"The tunnel made a junction with the taxiway above it.");
            Assert.That(made.Any(e=>e.structure==RoadStructure.Tunnel),"No tunnel section was built.");
        }
        static bool Connected(TaxiwayNetwork net,int from,int to)
        {
            var seen=new HashSet<int>{from};var queue=new Queue<int>();queue.Enqueue(from);
            while(queue.Count>0){int n=queue.Dequeue();if(n==to)return true;foreach(var e in net.EdgesAt(n)){int o=TaxiwayNetwork.Other(e,n);if(seen.Add(o))queue.Enqueue(o);}}
            return false;
        }

        [Test]public void SnapAcrossLevelsOnlyWhereTheCursorIsOnTheRoad()
        {
            drive.tool.SetTool(DevTool.Street);drive.tool.SetRoadMode(RoadBuildMode.Bridge);float z=-1300;
            float r1=RoadY(Roads,new Vector2(-1700,z));
            drive.HoverFree(new Vector2(-1691,z));
            Assert.IsNull(drive.Snap,"9 m beside a street 8 m below the working level is not on it.");
            drive.HoverRoad(new Vector2(-1700,z),r1);
            Assert.IsTrue(drive.Snap!=null&&drive.Snap.Contains("two-lane street"),"Pointing at the street below does not join it: "+(drive.Snap??"no snap"));
            Assert.That(drive.Endpoint.y,Is.EqualTo(r1).Within(.05f));
            // just past a deck's end the ray meets the ground below, yet the end is what the cursor is on
            drive.EndChain();drive.tool.SetRoadMode(RoadBuildMode.Automatic);drive.tool.SetRoadElevation(0);
            drive.HoverRoad(F.B1B+new Vector2(2.5f,1f),F.BridgeLevel);
            Assert.That(drive.Snap,Is.EqualTo("the junction"),"Pointing just past a bridge's end does not take its end.");
            Assert.That(drive.Endpoint.y,Is.EqualTo(F.BridgeLevel).Within(.01f));
            // drawing on the surface across a buried tunnel: the tunnel cannot be seen, so it is crossed, not joined
            drive.tool.SetRoadMode(RoadBuildMode.Automatic);drive.tool.SetRoadElevation(0);
            drive.ClickFree(new Vector2(-1875,-380));Assert.That(drive.Chain,Is.GreaterThan(0));
            drive.HoverFree(new Vector2(-1875,-500));
            Assert.IsNull(drive.Snap,"A tunnel hidden under the ground was joined from the surface.");
            Assert.That(drive.Endpoint.y,Is.EqualTo(game.valley.DisplayHeight(new Vector2(-1875,-500))).Within(.3f));
            drive.EndChain();
        }

        [Test]public void RoadToolJoinsServiceRoadsButNeverATaxiwayAtAnotherLevel()
        {
            var origin=game.Airport.transform.position;Vector2 W(float x,float z)=>new Vector2(x+origin.x,z+origin.z);
            drive.tool.SetTool(DevTool.Road);drive.tool.SetRoadMode(RoadBuildMode.Automatic);drive.tool.SetRoadElevation(0);
            drive.ClickRoad(W(-200,0),origin.y);Assert.That(drive.Chain,Is.GreaterThanOrEqualTo(0));
            drive.tool.SetRoadMode(RoadBuildMode.Tunnel);
            drive.HoverRoad(W(0,0),origin.y);
            Assert.That(drive.Snap==null||!drive.Snap.Contains("taxiway"),"A tunnel joined the taxiway above it: "+drive.Snap);
            Assert.That(drive.Endpoint.y,Is.EqualTo(origin.y-10).Within(.3f),"Over the taxiway the tunnel keeps its level.");
            drive.HoverRoad(W(200,40),origin.y);
            Assert.IsTrue(drive.Snap!=null&&drive.Snap.Contains("the road"),"Pointing at the service road above (outlined in the underground view) does not join it: "+(drive.Snap??"no snap"));
            Assert.That(drive.Endpoint.y,Is.EqualTo(origin.y).Within(.05f));
            drive.EndChain();
        }

        [Test]public void ClickedRoadLevelIsTakenAndTheChosenElevationReturnsAfterTheChain()
        {
            drive.tool.SetTool(DevTool.Street);drive.tool.SetRoadMode(RoadBuildMode.Automatic);drive.tool.SetRoadElevation(0);
            drive.ClickRoad(F.B1B,F.BridgeLevel);
            Assert.That(drive.tool.RoadElevation,Is.EqualTo(F.BridgeLevel-game.valley.DisplayHeight(F.B1B)).Within(.01f),"Starting on a deck takes its level.");
            drive.HoverFree(new Vector2(-1450,-1700));Assert.That(drive.Endpoint.y,Is.EqualTo(F.BridgeLevel).Within(.05f));
            drive.EndChain();drive.HoverFree(new Vector2(-1450,-1650));
            Assert.That(drive.tool.RoadElevation,Is.EqualTo(0).Within(.001f),"The player's elevation returns when the chain ends.");
            drive.tool.SetRoadMode(RoadBuildMode.Bridge);float chosen=drive.tool.RoadElevation;
            drive.ClickRoad(new Vector2(-1700,-1300),RoadY(Roads,new Vector2(-1700,-1300)));
            Assert.That(drive.tool.RoadElevation,Is.EqualTo(chosen),"Starting on a ground street keeps the chosen elevation (Bridge climbs from it).");
            drive.EndChain();
        }
    }
}
