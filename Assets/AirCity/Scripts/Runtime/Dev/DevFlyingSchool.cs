using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.Game;
using UnityEngine;

namespace AirCity.Dev
{
    /// <summary>Placing and selecting flying schools with the shared build tool (both game modes).</summary>
    public partial class DevAirfieldTool
    {
        /// <summary>The tie-down surface: follow the taxiway the flight line snaps to, or always paved / grass.</summary>
        public enum SchoolSurface { MatchTaxiway, Paved, Grass }

        public struct SchoolDraft
        {
            public Vector2 origin;
            public float heading, lane;
            public PavementSurface surface;
            public bool snapped;
        }

        string schoolProblem;
        int schoolSide = 1, schoolBuiltSide = 1, schoolPrice;
        FlyingSchoolConstruction schoolPlan;
        SchoolDraft schoolQuoted;
        int schoolQuotedVersion = -1, schoolQuotedSide;
        ulong schoolQuotedRoads;
        bool schoolQuotedOnce;
        float schoolQuotedAt;

        public string FlyingSchoolProblem => schoolProblem;
        public bool FlyingSchoolSnapped { get; private set; }
        public int FlyingSchoolPrice => schoolPrice;
        public SchoolSurface FlyingSchoolSurface { get; set; }
        public FlyingSchool SelectedFlyingSchool => selection.kind == Kind.FlyingSchool ? selection.school : null;

        /// <summary>Put the driveway on the other side of the car park (it also switches by itself to reach a road).</summary>
        public void FlipFlyingSchoolDriveway() { schoolSide = -schoolSide; schoolQuotedOnce = false; }

        /// <summary>The cursor marks the clubhouse. Near a level taxiway the flight line snaps onto it, the school
        /// standing on the cursor's side; elsewhere the school is laid out freely on the placement heading.</summary>
        public SchoolDraft FlyingSchoolPlacement(Vector3 world)
        {
            var cursor = Geo.XZ(airport.transform.InverseTransformPoint(world));
            PavementSurface Pick(PavementSurface match) => FlyingSchoolSurface == SchoolSurface.Paved ? PavementSurface.Asphalt :
                FlyingSchoolSurface == SchoolSurface.Grass ? PavementSurface.Grass : match;
            float free = FlyingSchoolLayout.Lane(0f);
            var axis0 = FromBearing(objectHeading);
            var result = new SchoolDraft { origin = cursor - axis0 * FlyingSchoolLayout.BuildingCentre(free), heading = objectHeading,
                lane = free, surface = Pick(PavementSurface.Asphalt) };
            if (!placementSnap) return result;
            var net = airport.Taxiways;
            float best = 22f;
            foreach (var edge in net.edges)
            {
                var a = net.Node(edge.a); var b = net.Node(edge.b);
                if (!HangarConnection.IsLevelTaxiway(edge, a, b) || edge.width + .01f < FlyingSchoolLayout.Lane(0f)) continue;
                var start = Geo.XZ(a.position); var line = Geo.XZ(b.position) - start;
                float length = line.magnitude;
                if (length < 20f) continue;
                var along = line / length;
                float at = Vector2.Dot(cursor - start, along);
                if (at < 0f || at > length) continue;
                float offset = Geo.Cross(along, cursor - start);
                // How far the clubhouse would have to move to stand at its place behind this taxiway.
                float miss = Mathf.Abs(Mathf.Abs(offset) - FlyingSchoolLayout.BuildingCentre(edge.width));
                if (miss >= best) continue;
                best = miss;
                float margin = Mathf.Min(FlyingSchoolLayout.ApronHalfWidth + 1f, length * .5f);
                var axis = offset > 0f ? Geo.Left(along) : Geo.Right(along);
                var surfaceHere = edge.surface.IsPaved() ? edge.surface : PavementSurface.Grass;
                result = new SchoolDraft { origin = start + along * Mathf.Clamp(at, margin, length - margin), heading = Mathf.Repeat(Bearing(axis), 360f),
                    lane = edge.width, surface = Pick(surfaceHere), snapped = true };
            }
            return result;
        }

        void FlyingSchoolTool(bool click, bool free, Vector3 world)
        {
            if (!free) { FlyingSchoolSnapped = false; return; }
            var draft = FlyingSchoolPlacement(world);
            FlyingSchoolSnapped = draft.snapped;
            Quote(draft, click);
            if (click) BuildFlyingSchool(draft);
        }

        /// <summary>Quote the draft on the chosen driveway side, then the other: a road is often on one side only.
        /// Repeated for the same pose only when the airport or the roads have changed, and while the cursor moves
        /// at most eight times a second (the driveway plan searches the road graph); a click always quotes afresh.</summary>
        void Quote(SchoolDraft draft, bool now = false)
        {
            if (!now && schoolQuotedOnce && Time.unscaledTime - schoolQuotedAt < .12f) return;
            ulong roads = game != null ? RoadConstructionPlan.Signature(game.State.RoadNetwork()) : 0UL;
            if (schoolQuotedOnce && schoolQuotedVersion == airport.LayoutVersion && schoolQuotedRoads == roads && schoolQuotedSide == schoolSide &&
                Vector2.Distance(schoolQuoted.origin, draft.origin) < .05f && Mathf.Abs(Mathf.DeltaAngle(schoolQuoted.heading, draft.heading)) < .05f &&
                Mathf.Approximately(schoolQuoted.lane, draft.lane) && schoolQuoted.surface == draft.surface) return;
            schoolQuotedAt = Time.unscaledTime;
            schoolQuoted = draft; schoolQuotedVersion = airport.LayoutVersion; schoolQuotedRoads = roads; schoolQuotedSide = schoolSide; schoolQuotedOnce = true;
            schoolBuiltSide = schoolSide;
            if (!FlyingSchoolConstruction.TryCreate(game, airport, draft.origin, draft.heading, draft.lane, schoolSide, draft.surface, out schoolPlan, out schoolProblem))
            {
                string first = schoolProblem; var firstPlan = schoolPlan;
                if (FlyingSchoolConstruction.TryCreate(game, airport, draft.origin, draft.heading, draft.lane, -schoolSide, draft.surface, out var other, out _))
                { schoolPlan = other; schoolProblem = null; schoolBuiltSide = -schoolSide; }
                else { schoolProblem = first; schoolPlan = firstPlan; }
            }
            schoolPrice = schoolPlan != null ? schoolPlan.Price : FlyingSchoolRules.BuildPrice;
        }

        /// <summary>Commit the same flying school draft shown by the construction tool.</summary>
        public bool PlaceFlyingSchool(Vector3 world)
        {
            if (airport == null) return false;
            var draft = FlyingSchoolPlacement(world);
            schoolQuotedOnce = false; Quote(draft, true);
            return BuildFlyingSchool(draft);
        }

        bool BuildFlyingSchool(SchoolDraft draft)
        {
            if (schoolProblem != null) { game?.SetMessage(schoolProblem); return false; }
            if (!FlyingSchoolConstruction.TryBuild(game, airport, draft.origin, draft.heading, draft.lane, schoolBuiltSide, draft.surface, out var made, out schoolProblem))
            { game?.SetMessage(schoolProblem); return false; }
            schoolQuotedOnce = false;
            SelectFlyingSchool(made);
            return true;
        }

        public void SelectFlyingSchool(FlyingSchool school)
        {
            SetTool(DevTool.Select);
            schoolProblem = null;
            Select(school == null ? Pick.None : new Pick { kind = Kind.FlyingSchool, school = school, apron = -1 });
        }

        /// <summary>The site as one outline: the wide flight line, the narrower block behind it.</summary>
        static Vector2[] SchoolOutline(Vector2 origin, float heading, float lane)
        {
            lane = FlyingSchoolLayout.Lane(lane);
            float w = FlyingSchoolLayout.ApronHalfWidth, b = FlyingSchoolLayout.BlockHalfWidth, d = FlyingSchoolLayout.ApronDepth(lane);
            var local = new[]
            {
                new Vector2(-w, lane * .5f), new Vector2(w, lane * .5f), new Vector2(w, d), new Vector2(b, d),
                new Vector2(b, FlyingSchoolLayout.LotBack(lane) + .3f), new Vector2(-b, FlyingSchoolLayout.LotBack(lane) + .3f), new Vector2(-b, d), new Vector2(-w, d),
            };
            return local.Select(p => FlyingSchoolLayout.Point(origin, heading, p)).ToArray();
        }

        void DrawFlyingSchoolPreview(bool pointing, Vector3 world)
        {
            if (!pointing || tool != DevTool.FlyingSchool) return;
            var draft = FlyingSchoolPlacement(world);
            var colour = schoolProblem == null ? new Color(.25f, .85f, .72f) : DevGui.Danger;
            loop.Clear();
            foreach (var p in SchoolOutline(draft.origin, draft.heading, draft.lane)) loop.Add(airport.transform.TransformPoint(Geo.X0Z(p)));
            outlines.Loop(cam, loop, colour, 3);
            loop.Clear();
            foreach (var p in FlyingSchoolLayout.Throat(draft.origin, draft.heading, FlyingSchoolLayout.Lane(draft.lane), schoolBuiltSide))
                loop.Add(airport.transform.TransformPoint(Geo.X0Z(p)));
            outlines.Loop(cam, loop, colour, 2);
            // The taxilane the flight line needs along its front.
            var across = Geo.Right(FromBearing(draft.heading)) * (FlyingSchoolLayout.ApronHalfWidth + 12f);
            loop.Clear();
            loop.Add(airport.transform.TransformPoint(Geo.X0Z(draft.origin - across, .05f)));
            loop.Add(airport.transform.TransformPoint(Geo.X0Z(draft.origin + across, .05f)));
            outlines.Line(cam, loop, new Color(.35f, .7f, .85f), draft.snapped ? 3f : 1.5f);
            // The driveway the purchase includes, along the real graph draft.
            var road = schoolPlan != null && schoolProblem == null ? schoolPlan.Road : null;
            if (road == null) return;
            foreach (int id in road.NewSegments)
            {
                var path = road.Draft.Path(road.Draft.Segment(id));
                int count = Mathf.Max(1, Mathf.CeilToInt(path.Length / 3f));
                loop.Clear();
                for (int i = 0; i <= count; i++) loop.Add(path.Position(i / (float)count) + Vector3.up * .3f);
                outlines.Line(cam, loop, new Color(.95f, .82f, .35f), 3f);
            }
        }
    }

    public partial class DevToolbar
    {
        FlyingSchool schoolSubject;
        int schoolTab, schoolTypeIndex;
        string schoolMessage;
        Vector2 schoolScroll;
        Rect schoolBody, schoolPurchaseAnchor, schoolPurchaseRect;
        SchoolPurchase schoolPurchase;
        static readonly AirfieldWorkspaceGui.Tab[] SchoolTabs =
        {
            new AirfieldWorkspaceGui.Tab("Overview", "overview"),
            new AirfieldWorkspaceGui.Tab("Manage", "manage"),
            new AirfieldWorkspaceGui.Tab("Students", "students")
        };

        sealed class SchoolPurchase
        {
            public readonly object Item;
            public readonly string Title, Confirm;
            public readonly Action Buy;
            public SchoolPurchase(object item, string title, string confirm, Action buy)
            { Item = item; Title = title; Confirm = confirm; Buy = buy; }
        }

        void PayForSchool(int price, Func<bool> apply)
        {
            var game = airfield.game;
            if (game != null && !game.CanSpend(price, out schoolMessage)) return;
            if (!apply()) return;
            game?.Spend(price);
            SchoolChanged();
        }

        string Cost(int price) => airfield.game == null || airfield.game.Sandbox ? "free" : $"${price:N0}";

        /// <summary>The build tray: the tie-downs' surface as swatches of the real ground, and the driveway's side.</summary>
        void FlyingSchoolCards()
        {
            var choice = airfield.FlyingSchoolSurface;
            Group("TIE-DOWNS", 76f,
                new Card(r =>
                {
                    Swatch(new Rect(r.x, r.y, r.width * .5f, r.height), PavementSurface.Asphalt);
                    Swatch(new Rect(r.x + r.width * .5f, r.y, r.width * .5f, r.height), PavementSurface.Grass);
                }, "Match", "taxiway", "Paved tie-downs beside a paved taxiway, grass beside a grass one",
                    choice == DevAirfieldTool.SchoolSurface.MatchTaxiway, true, () => airfield.FlyingSchoolSurface = DevAirfieldTool.SchoolSurface.MatchTaxiway),
                new Card(r => Swatch(r, PavementSurface.Asphalt), "Paved", null, "Asphalt tie-downs for all weather",
                    choice == DevAirfieldTool.SchoolSurface.Paved, true, () => airfield.FlyingSchoolSurface = DevAirfieldTool.SchoolSurface.Paved),
                new Card(r => Swatch(r, PavementSurface.Grass), "Grass", null, "Grass tie-downs for a small airfield; they need mowing",
                    choice == DevAirfieldTool.SchoolSurface.Grass, true, () => airfield.FlyingSchoolSurface = DevAirfieldTool.SchoolSurface.Grass));
            Group("DRIVEWAY", 76f, new Card(null, "Flip side", null, "Leave the car park on the other side; it also switches by itself to reach a road",
                false, true, airfield.FlipFlyingSchoolDriveway));
        }

        void FlyingSchoolBuildInspector()
        {
            Title("Flying school");
            Note($"Clubhouse, car park and driveway behind a flight line of {FlyingSchoolLayout.Spots} tie-downs for light aircraft.");
            PropText("Build", airfield.game == null || airfield.game.Sandbox ? "Free in Sandbox" : $"${airfield.FlyingSchoolPrice:N0}");
            var surface = airfield.FlyingSchoolSurface;
            PropText("Tie-downs", surface == DevAirfieldTool.SchoolSurface.MatchTaxiway ? "Match the taxiway" : surface.ToString());
            Note(airfield.FlyingSchoolSnapped ? "Flight line aligned with the taxiway." :
                "Point where the clubhouse should stand, beside a taxiway: the flight line snaps onto it. A taxiway can also be laid along the front later.");
            Note("Prepare cleared, level ground beside a public road. Then buy aircraft and hire instructors in the school's Manage tab.");
            if (airfield.FlyingSchoolProblem != null) Danger(airfield.FlyingSchoolProblem);
            Buttons(B("Cancel", () => airfield.SetTool(DevTool.Select), hint: "Esc"));
        }

        void DrawFlyingSchoolWorkspace(FlyingSchool school, Rect panel)
        {
            if (schoolSubject != school)
            {
                schoolSubject = school; schoolMessage = null; schoolTypeIndex = 0;
                schoolScroll = Vector2.zero;
                CancelSchoolPurchase();
            }
            var layout = new AirfieldWorkspaceGui.Layout(panel);
            schoolBody = layout.Body;
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && !schoolPurchaseRect.Contains(evt.mousePosition) &&
                !schoolPurchaseAnchor.Contains(evt.mousePosition)) CancelSchoolPurchase();
            bool repaint = evt.type == EventType.Repaint;
            if (!repaint) DrawSchoolPurchase();

            AirfieldWorkspaceGui.Frame(layout, "Flying School", school.name, "flying-school", () =>
            {
                CancelSchoolPurchase();
                airfield.SelectFlyingSchool(null);
            });
            AirfieldWorkspaceGui.Navigation(layout, SchoolTabs, schoolTab, next =>
            {
                schoolTab = next; schoolScroll = Vector2.zero; schoolMessage = null;
                CancelSchoolPurchase();
            });

            var types = schoolTab == 1
                ? airfield.airport.aircraftTypes.Where(t => t != null && FlyingSchoolLayout.TrainerProblem(t) == null).Distinct().ToArray()
                : Array.Empty<AircraftType>();
            if (types.Length > 0) schoolTypeIndex = (schoolTypeIndex % types.Length + types.Length) % types.Length;
            var trainerType = types.ElementAtOrDefault(schoolTypeIndex);
            schoolPurchaseAnchor = default;
            DrawWorkspaceRows(layout.Body, ref schoolScroll, () => FlyingSchoolInspector(school, trainerType, types.Length));
            DrawSchoolFooter(layout.Footer, school, trainerType);

            if (schoolPurchase != null && schoolPurchaseAnchor.width == 0) CancelSchoolPurchase();
            if (repaint) DrawSchoolPurchase();
        }

        void DrawSchoolFooter(Rect rect, FlyingSchool school, AircraftType type)
        {
            var action = new Rect(rect.xMax - U(151), rect.y + U(9), U(137), U(32));
            string status = schoolMessage;
            if (schoolTab == 0)
            {
                bool paused = school.LessonsPaused;
                if (DevGui.Button(action, paused ? "Resume lessons" : "Pause lessons",
                    "Aircraft already in the air finish their lesson.", on: paused))
                { school.LessonsPaused = !paused; SchoolChanged(); }
                status ??= paused ? "Lessons paused" : "Lessons running";
            }
            else if (schoolTab == 1 && type != null && school.FreeSpot() >= 0)
            {
                int price = FlyingSchoolRules.TrainerPrice;
                PurchaseButton(action, type, type.TypeName, "Buy trainer", "Buy for " + Cost(price), () =>
                {
                    schoolMessage = school.AccessProblem;
                    if (schoolMessage == null) PayForSchool(price, () => school.BuyTrainer(type, out schoolMessage));
                }, school.AccessProblem == null, primary: true);
                status ??= $"{type.TypeName}  |  {Cost(price)}";
            }
            else
            {
                action.x = rect.xMax;
                status ??= schoolTab == 2 ? $"{FlyingSchoolRules.CourseLessons} lessons per licence" : "No trainer available";
            }
            GUI.Label(new Rect(rect.x + Pad, rect.y + U(4), action.x - rect.x - Pad - U(8), rect.height - U(8)),
                new GUIContent(status, status), Wrapped);
        }

        void FlyingSchoolInspector(FlyingSchool school, AircraftType trainerType, int trainerTypeCount)
        {
            var airport = airfield.airport;
            bool sandbox = airfield.game == null || airfield.game.Sandbox;
            string access = school.AccessProblem, road = school.RoadProblem;
            if (schoolTab == 0)
            {
                if (access != null) Danger(access);
                if (road != null) Danger(road);
                string flying = access == null && road == null ? school.FlightProblem(airport) : null;
                PropText("Status", access != null || road != null ? "Closed" : flying != null ? "Not flying" :
                    school.Trainers.Count == 0 ? "No aircraft" : school.Instructors == 0 ? "No instructors" : "Open");
                if (flying != null) Note(flying);
                else if (access == null && road == null && school.Trainers.Count == 0) Note("Buy a trainer in the Manage tab to start lessons.");
                else if (access == null && road == null && school.Instructors == 0) Note("Hire an instructor in the Manage tab: students enrol once there is someone to teach them.");
                PropText("Students", $"{school.Students.Count} / {school.Capacity}");
                PropText("Instructors", $"{school.InstructorsBusy} teaching / {school.Instructors}");
                PropText("Lessons", $"{school.LessonsFlown:N0} flown");
                PropText("Licences", $"{school.Graduates:N0} earned");
                PropText("Income", $"${school.Earned:N0}");
                PropText("Wages", $"${school.WagesPaid:N0}");
                if (sandbox) Note("Sandbox · finances are paused.");
                Section("FLIGHT LINE");
                for (int i = 0; i < FlyingSchoolLayout.Spots; i++)
                {
                    var stand = school.Spot(i);
                    var trainer = school.Trainers.FirstOrDefault(t => t.spot == i);
                    string label = stand != null ? stand.Label : "T" + (i + 1);
                    if (trainer == null) { PropText(label, "Empty tie-down"); continue; }
                    PropText(label, $"{trainer.registration} · {trainer.type.TypeName}");
                    var student = school.Students.FirstOrDefault(s => s.id == trainer.student);
                    string doing = trainer.status ?? "On the flight line";
                    if (student != null) doing = $"{student.name}, {(trainer.dual ? "with an instructor" : "solo")}: {doing}";
                    Note(char.ToUpperInvariant(doing[0]) + doing.Substring(1));
                }
                var inSimulator = school.SimulatorStudent;
                if (inSimulator != null) PropText("Simulator", inSimulator.name);
            }
            else if (schoolTab == 1)
            {
                Section("INSTRUCTORS");
                PropStepper("Staff", school.Instructors.ToString(),
                    () => { if (school.Dismiss()) SchoolChanged(); else schoolMessage = "That instructor is teaching. Wait for the lesson to end."; },
                    () => PayForSchool(FlyingSchoolRules.HireFee, school.Hire),
                    $"Hire ({Cost(FlyingSchoolRules.HireFee)}) or let go an instructor",
                    school.Instructors > 0, school.Instructors < FlyingSchoolRules.MaxInstructors);
                Note($"${FlyingSchoolRules.DailyWage:N0} a day each. Students before their first solo, and for navigation and the checkride, fly with an instructor.");
                Section("AIRCRAFT");
                foreach (var trainer in school.Trainers.ToArray())
                {
                    PropText(trainer.registration, $"{trainer.type.TypeName} · {trainer.flights:N0} lessons");
                    Buttons(B($"Sell {trainer.registration} · {Cost(FlyingSchoolRules.TrainerResale)}", () =>
                    {
                        if (!school.SellTrainer(trainer)) { schoolMessage = "Wait until the aircraft is back on its tie-down."; return; }
                        if (airfield.game != null && !airfield.game.Sandbox) airfield.game.Spend(-FlyingSchoolRules.TrainerResale);
                        SchoolChanged();
                    }, "Sell this aircraft once it is back on its tie-down", enabled: school.CanSell(trainer), danger: true));
                }
                if (school.FreeSpot() < 0) Note("Every tie-down has an aircraft.");
                else if (trainerType == null) Note("No light training aircraft are available.");
                else
                {
                    if (trainerTypeCount > 1) PropStepper("Type", trainerType.TypeName, () => schoolTypeIndex--, () => schoolTypeIndex++, "Choose the trainer to buy");
                    if (access != null) Note("Connect the flight line before buying aircraft.");
                }
                Section("UPGRADES");
                SchoolUpgradeRow(school, SchoolUpgrade.Classroom, "classroom");
                SchoolUpgradeRow(school, SchoolUpgrade.Simulator, "flight-simulator");
                SchoolUpgradeRow(school, SchoolUpgrade.NightRating, "lights");
                Section("BUILDING");
                Buttons(B("Build another", () => airfield.SetTool(DevTool.FlyingSchool), "Place another flying school"));
                Buttons(B("Demolish school", () => airfield.DeleteSelection(), "Sell the school's aircraft before demolition", enabled: school.CanDemolish, danger: true));
            }
            else
            {
                if (school.Students.Count == 0)
                    Note(school.Instructors == 0 || school.Trainers.Count == 0 ? "Students enrol once the school has an aircraft and an instructor." : "Waiting for the first students to enrol.");
                foreach (var student in school.Students)
                {
                    var flying = school.Trainers.FirstOrDefault(t => t.student == student.id);
                    bool simulator = school.SimulatorStudent == student;
                    float done = student.lessons / (float)FlyingSchoolRules.CourseLessons;
                    string where = flying != null ? " · flying " + flying.registration : simulator ? " · in the simulator" : "";
                    Add(U(40f), r =>
                    {
                        GUI.Label(new Rect(r.x, r.y, r.width, U(20f)), student.name, DevGui.Label);
                        GUI.Label(new Rect(r.x, r.y + U(18f), r.width, U(16f)), $"{student.Stage} · {student.lessons}/{FlyingSchoolRules.CourseLessons}{where}", DevGui.Small);
                        var bar = new Rect(r.x, r.yMax - U(4f), r.width, U(3f));
                        DevGui.Fill(bar, DevGui.GUI(new Color(.78f, .84f, .80f)), U(1.5f));
                        DevGui.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(done), bar.height), DevGui.GUI(DevGui.Primary), U(1.5f));
                    });
                }
                Space();
                Note($"A licence takes {FlyingSchoolRules.CourseLessons} lessons: introductory flights, circuits with touch-and-goes, a first solo, " +
                     $"solo circuits, navigation and the checkride. ${FlyingSchoolRules.DualFee:N0} a lesson with an instructor, ${FlyingSchoolRules.SoloFee:N0} solo, " +
                     $"${FlyingSchoolRules.GraduationBonus:N0} for each licence.");
            }
        }

        void SchoolUpgradeRow(FlyingSchool school, SchoolUpgrade upgrade, string icon)
        {
            string name = FlyingSchoolRules.Name(upgrade);
            int price = FlyingSchoolRules.Price(upgrade);
            var effect = new GUIContent(FlyingSchoolRules.Effect(upgrade));
            float infoWidth = innerWidth - U(126);
            float descriptionHeight = Wrapped.CalcHeight(effect, infoWidth);
            Add(Mathf.Max(U(40), U(21) + descriptionHeight), r =>
            {
                AirfieldWorkspaceGui.FeatureIcon(new Rect(r.x, r.center.y - U(16), U(32), U(32)), icon);
                GUI.Label(new Rect(r.x + U(40), r.y, infoWidth, U(20)), new GUIContent(name, name), DevGui.Bold);
                GUI.Label(new Rect(r.x + U(40), r.y + U(19), infoWidth, descriptionHeight), effect, Wrapped);
                var button = new Rect(r.xMax - U(80), r.center.y - U(15), U(80), U(30));
                if (school.Has(upgrade)) GUI.Label(button, "Installed", Centred);
                else
                {
                    var anchor = button;
                    anchor.position += schoolBody.position - schoolScroll;
                    PurchaseButton(button, upgrade, name, Cost(price), "Install for " + Cost(price),
                        () => PayForSchool(price, () => school.Upgrade(upgrade)), anchor: anchor);
                }
            });
        }

        void PurchaseButton(Rect rect, object item, string title, string label, string confirm, Action buy,
            bool enabled = true, bool primary = false, Rect? anchor = null)
        {
            bool open = Equals(schoolPurchase?.Item, item);
            bool clicked = primary
                ? DevGui.PrimaryButton(rect, label, title, enabled)
                : DevGui.Button(rect, label, title, on: open, enabled: enabled);
            if (clicked)
            {
                if (open) CancelSchoolPurchase();
                else schoolPurchase = new SchoolPurchase(item, title, confirm, buy);
            }
            if (Equals(schoolPurchase?.Item, item) && enabled && (anchor == null || schoolBody.Contains(anchor.Value.center)))
                schoolPurchaseAnchor = anchor ?? rect;
        }

        void DrawSchoolPurchase()
        {
            if (schoolPurchase == null || schoolPurchaseAnchor.width == 0) return;
            float width = Mathf.Min(U(252), schoolBody.width - U(10)), height = U(82);
            var anchor = schoolPurchaseAnchor;
            float x = Mathf.Clamp(anchor.xMax - width, schoolBody.x + U(5), schoolBody.xMax - width - U(5));
            float y = anchor.y - height - U(5);
            if (y < schoolBody.y) y = Mathf.Min(anchor.yMax + U(5), schoolBody.yMax - height);
            schoolPurchaseRect = new Rect(x, y, width, height);
            DevGui.Panel(schoolPurchaseRect);
            GUI.Label(new Rect(x + U(12), y + U(8), width - U(24), U(22)), schoolPurchase.Title, DevGui.Bold);
            var cancel = new Rect(x + U(12), y + U(39), U(68), U(31));
            if (DevGui.Button(cancel, "Cancel")) CancelSchoolPurchase();
            else if (DevGui.PrimaryButton(new Rect(cancel.xMax + U(6), cancel.y, width - U(98), cancel.height), schoolPurchase.Confirm))
            {
                var buy = schoolPurchase.Buy;
                CancelSchoolPurchase();
                buy();
            }
        }

        internal bool CancelSchoolPurchase()
        {
            bool wasOpen = schoolPurchase != null;
            schoolPurchase = null;
            schoolPurchaseAnchor = schoolPurchaseRect = default;
            return wasOpen;
        }

        void SchoolChanged()
        {
            schoolMessage = null;
            airfield.game?.SharedConstructionChanged();
        }

        /// <summary>A selected aircraft that belongs to a school: whose it is and what it is doing.</summary>
        void AircraftSchool(AirCity.Aircraft.Aircraft aircraft)
        {
            var school = FlyingSchool.OfTrainer(aircraft);
            if (school == null) return;
            var trainer = school.TrainerOf(aircraft);
            Note($"{school.name} trainer{(trainer != null && trainer.lesson != LessonKind.None ? trainer.lesson == LessonKind.Circuits ? " · circuit training" : " · local flight" : "")}");
            Buttons(B("Open school", () => airfield.SelectFlyingSchool(school), "Show the school this aircraft belongs to"));
        }
    }
}
