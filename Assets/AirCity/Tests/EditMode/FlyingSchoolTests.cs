using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using AirCity.EditorTools.Aircraft;
using AirCity.EditorTools.Airfield;
using AirCity.Game;
using AirCity.Traffic;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Airfield
{
    /// <summary>The flying school: its site and flight line, circuits and touch-and-goes, lessons, money and saves.</summary>
    public sealed class FlyingSchoolTests
    {
        readonly List<Object> spawned = new List<Object>();
        float glow;
        AircraftType Type => AircraftSetup.AllTypes().First(t => t.model == "Cessna172");

        [SetUp] public void Setup() { glow = AircraftLights.Glow; AircraftLights.Glow = 0f; }
        [TearDown] public void Cleanup()
        {
            foreach (var item in spawned) if (item != null) Object.DestroyImmediate(item);
            spawned.Clear(); AircraftLights.Glow = glow;
        }

        /// <summary>A 1200 m runway along z = 0 (09/27, wind from the west), a parallel taxiway at z = -120 joined
        /// to it at both ends and in the middle, and a school south of the taxiway at x = `at`.</summary>
        (Airport airport, FlyingSchool school, Runway runway) AirportWithSchool(float at = 200f, bool build = true, bool materials = true)
        {
            if (materials) AirfieldSetup.CreateMaterials();
            var go = new GameObject("Flying school test"); go.SetActive(false); spawned.Add(go);
            var airport = go.AddComponent<Airport>(); if (materials) AirfieldSetup.AssignMaterials(airport);
            airport.groundSize = 0f; airport.windFrom = 270f; go.SetActive(true);
            var runway = airport.AddRunway(new Vector3(-600f, 0f, 0f), new Vector3(600f, 0f, 0f), PavementSurface.Asphalt, 23f);
            var net = airport.Taxiways;
            int Node(float x, float z, Runway on = null) => net.AddNode(new Vector3(x, 0f, z), on);
            int w = Node(-580f, 0f, runway), a = Node(-580f, -120f), m = Node(0f, -120f), b = Node(580f, -120f), e = Node(580f, 0f, runway);
            foreach (var (x, y) in new[] { (w, a), (a, m), (m, b), (b, e) }) net.Connect(x, y, 10.5f, PavementSurface.Asphalt);
            net.Connect(Node(0f, 0f, runway), m, 10.5f, PavementSurface.Asphalt);
            net.Rebuild(); runway.Rebuild();
            FlyingSchool school = null;
            if (build)
            {
                school = airport.AddFlyingSchool(new Vector2(at, -120f), 180f, 10.5f, 1, PavementSurface.Asphalt);
                Assert.IsNotNull(school, airport.FlyingSchoolProblem(new Vector2(at, -120f), 180f, 10.5f, 1, PavementSurface.Asphalt));
            }
            return (airport, school, runway);
        }

        AirCity.Aircraft.Aircraft NewAircraft(Airport airport, string name = "Test trainer")
        {
            var prefab = AircraftSetup.Prefab(Type); Assert.IsNotNull(prefab);
            var go = Object.Instantiate(prefab, new Vector3(0f, 0f, -600f), Quaternion.identity); spawned.Add(go);
            go.transform.SetParent(airport.transform, true); go.name = name;
            return go.GetComponent<AirCity.Aircraft.Aircraft>();
        }

        static Vector2 XZ(Vector3 value) => new Vector2(value.x, value.z);

        [Test]
        public void School_OwnsATieDownFlightLineThatOnlyItsAircraftUse()
        {
            var (airport, school, _) = AirportWithSchool();
            var net = airport.Taxiways;
            Assert.AreEqual(FlyingSchoolLayout.Spots, net.stands.Count);
            for (int i = 0; i < FlyingSchoolLayout.Spots; i++)
            {
                var stand = school.Spot(i); Assert.IsNotNull(stand);
                Assert.IsTrue(stand.Owned); Assert.AreEqual(school.SiteId, stand.owner); Assert.AreEqual("T" + (i + 1), stand.Label);
                var layout = school.SpotLayout(i);
                Assert.IsNotNull(layout.edge, "the flight line is connected by the taxiway along its front");
                Assert.AreEqual(StandProblem.None, net.CheckStand(stand, Type));
                Assert.IsTrue(school.Contains(layout.Centre), "the tie-downs are picked as part of the school");
            }
            Assert.IsNull(school.AccessProblem);
            // Ordinary traffic is never sent to the school's tie-downs.
            Assert.IsFalse(airport.Traffic.CanArrive(Type, out string why)); StringAssert.Contains("no free stand", why);
            var visitor = NewAircraft(airport, "Visitor");
            Assert.IsNull(airport.Traffic.Arrive(visitor, out _));
            // The school's own aircraft come home, each to its own tie-down.
            var trainer = school.AddTrainer(NewAircraft(airport)); Assert.IsNotNull(trainer);
            Assert.AreSame(school.Spot(trainer.spot), FlyingSchool.HomeOf(trainer.aircraft));
            Assert.IsTrue(school.AtHome(trainer));
            Assert.AreSame(school, FlyingSchool.OfTrainer(trainer.aircraft));
            // A public stand keeps the ordinary numbering.
            var open = net.AddStand(StandKind.Parking, new Vector3(-200f, 0f, -120f), 180f, SizeCode.A, PavementSurface.Asphalt);
            Assert.IsNotNull(open); Assert.AreEqual("1", open.Label); Assert.IsFalse(open.Owned);
            Assert.IsTrue(airport.Traffic.CanArrive(Type, out why), why);
        }

        [Test]
        public void School_SiteKeepsOtherBuildingsOffAndIsRemovedWithItsFlightLine()
        {
            var (airport, school, _) = AirportWithSchool();
            Assert.IsNotNull(airport.FlyingSchoolProblem(new Vector2(215f, -120f), 180f, 10.5f, 1, PavementSurface.Asphalt), "overlapping flight line");
            float block = -120f - FlyingSchoolLayout.BuildingCentre(10.5f);
            StringAssert.Contains("flying school", airport.HangarProblem(new Vector2(200f, block), 0f));
            Assert.IsNull(airport.AddFlyingSchool(new Vector2(200f, -120f), 180f, 10.5f, 1, PavementSurface.Asphalt));
            Assert.AreEqual(1, airport.FlyingSchools.Count());
            // The other side of the taxiway is the runway's: no room for the clubhouse there.
            Assert.IsNull(airport.FlyingSchoolProblem(new Vector2(-250f, -120f), 180f, 10.5f, -1, PavementSurface.Grass), "a second school further along");
            var second = airport.AddFlyingSchool(new Vector2(-250f, -120f), 180f, 10.5f, -1, PavementSurface.Grass);
            Assert.IsNotNull(second); Assert.AreNotEqual(school.SiteId, second.SiteId);
            Assert.AreEqual(PavementSurface.Grass, second.Spot(0).surface);
            Assert.AreEqual(2 * FlyingSchoolLayout.Spots, airport.Taxiways.stands.Count);

            var trainer = school.AddTrainer(NewAircraft(airport));
            Assert.IsFalse(school.CanDemolish); Assert.IsFalse(airport.RemoveFlyingSchool(school), "aircraft are sold first");
            Assert.IsTrue(school.SellTrainer(trainer)); Assert.IsTrue(school.CanDemolish);
            Assert.IsTrue(airport.RemoveFlyingSchool(school));
            Assert.AreEqual(1, airport.FlyingSchools.Count());
            Assert.AreEqual(FlyingSchoolLayout.Spots, airport.Taxiways.stands.Count, "its tie-downs go with it");
            Assert.IsTrue(airport.Taxiways.stands.All(s => s.owner == second.SiteId));
        }

        [TestCase("Cessna172")]
        [TestCase("LowWingPiston")]
        public void Circuits_FlyTheVisiblePatternTouchAndGoThenLandOnTheirOwnTieDown(string model)
        {
            var (airport, school, runway) = AirportWithSchool();
            var type = AircraftSetup.AllTypes().Single(t => t.model == model);
            Assert.IsTrue(school.BuyTrainer(type, out string problem), problem);
            var trainer = school.Trainers.Single();
            Assert.IsNotNull(trainer.aircraft, "The shared catalog supplies the real trainer prefab");
            Assert.IsTrue(airport.Traffic.CanFlyCircuits(type, out problem), problem);
            var flight = airport.Traffic.DepartForCircuits(trainer.aircraft, 2, out problem);
            Assert.IsNotNull(flight, problem);
            Assert.AreEqual(FlightKind.Departure, flight.kind);
            var phases = new List<FlightPhase>();
            var last = trainer.aircraft.transform.position; float highest = 0f, widest = 0f, worstJump = 0f, lowestAirborne = float.MaxValue;
            bool handPush = false;
            for (int step = 0; step < 4 * 2400 && !flight.Finished; step++)
            {
                airport.Simulate(.25f);
                if (phases.Count == 0 || phases[phases.Count - 1] != flight.Phase) phases.Add(flight.Phase);
                handPush |= flight.Phase == FlightPhase.HandPush;
                var at = trainer.aircraft.transform.position;
                worstJump = Mathf.Max(worstJump, Vector3.Distance(at, last)); last = at;
                highest = Mathf.Max(highest, flight.Height);
                if (flight.Phase == FlightPhase.Pattern)
                {
                    widest = Mathf.Max(widest, at.z);
                    lowestAirborne = Mathf.Min(lowestAirborne, flight.Height);
                    Assert.AreEqual(FlightKind.Arrival, flight.kind, "in the circuit the tower treats it as inbound");
                }
            }
            Assert.IsTrue(flight.Finished, flight.Status);
            Assert.AreEqual(FlightPhase.Parked, flight.Phase, string.Join(" > ", phases));
            Assert.IsTrue(handPush, "pushed back off the tie-down by hand");
            Assert.AreEqual(2, flight.TouchAndGoes);
            Assert.AreEqual(2, phases.Count(p => p == FlightPhase.TouchAndGo), string.Join(" > ", phases));
            Assert.AreEqual(3, phases.Count(p => p == FlightPhase.Pattern), "three times round: two touch-and-goes and the landing");
            Assert.AreEqual(0, flight.GoArounds);
            // StepPattern eases the vertical speed to zero over three seconds when the trainer reaches circuit height.
            float levelOffAllowance = Mathf.Max(5f, type.ClimbRate * 1.5f);
            Assert.That(highest, Is.InRange(Flight.PatternHeight - 15f, Flight.PatternHeight + levelOffAllowance),
                "levels off at circuit height");
            Assert.Greater(lowestAirborne, 60f, "never descends below the glide path in the circuit");
            // Wind from the west: it takes off westwards, so a left-hand circuit lies south of the runway (-Z).
            Assert.Less(widest, 1f, "the circuit never crosses to the other side of the runway");
            Assert.Less(worstJump, 16f, "no jumps between the climb, the circuit's legs, final and the roll");
            Assert.IsTrue(school.AtHome(trainer), "back on its own tie-down");
            Assert.AreSame(school.SpotLayout(trainer.spot).stand, flight.Route.stand.stand);
            Assert.IsNull(airport.Traffic.OnRunway(runway), "the runway is given back");
        }

        [Test]
        public void Circuits_RollOutOnTheCentrelineAndKeepToOneSide()
        {
            var (airport, school, runway) = AirportWithSchool();
            var trainer = school.AddTrainer(NewAircraft(airport));
            var flight = airport.Traffic.DepartForCircuits(trainer.aircraft, 1, out string problem);
            Assert.IsNotNull(flight, problem);
            float side = 0f; bool sawFinal = false; var previous = FlightPhase.Ready;
            for (int step = 0; step < 4 * 1500 && flight.TouchAndGoes == 0; step++)
            {
                airport.Simulate(.25f);
                var at = trainer.aircraft.transform.position;
                if (flight.Phase == FlightPhase.Pattern) side = Mathf.Abs(at.z) > Mathf.Abs(side) ? at.z : side;
                if (flight.Phase == FlightPhase.Approach && previous == FlightPhase.Pattern)
                {
                    sawFinal = true;
                    Assert.Less(Mathf.Abs(at.z), 1.5f, "rolls out on the extended centreline");
                    Assert.Greater(at.x, 600f, "final is flown towards the threshold in use (the east end)");
                    float glide = Mathf.Tan(Flight.GlideSlope * Mathf.Deg2Rad) * flight.ToTouchdown;
                    Assert.AreEqual(glide, flight.Height, 3f, "joins the glide path where it rolls out");
                }
                previous = flight.Phase;
            }
            Assert.IsTrue(sawFinal); Assert.AreEqual(1, flight.TouchAndGoes);
            // Heading west, left is south (-Z): the whole circuit lies south of the runway.
            Assert.Less(side, -400f, "a left-hand circuit");
        }

        [Test]
        public void Circuits_NeedARunwayLongEnoughAndFollowAnArrivalInsteadOfCuttingIn()
        {
            var (airport, school, runway) = AirportWithSchool();
            var trainer = school.AddTrainer(NewAircraft(airport));
            var open = airport.Taxiways.AddStand(StandKind.Parking, new Vector3(-200f, 0f, -120f), 180f, SizeCode.A, PavementSurface.Asphalt);
            Assert.IsNotNull(open); airport.Taxiways.Rebuild();
            var flight = airport.Traffic.DepartForCircuits(trainer.aircraft, 1, out string problem);
            Assert.IsNotNull(flight, problem);
            Flight visitor = null; bool extended = false; float visitorDown = -1f, trainerDown = -1f; int owners = 0;
            for (int step = 0; step < 4 * 2400 && !(flight.Finished && (visitor == null || visitor.Finished)); step++)
            {
                airport.Simulate(.25f);
                // An arrival joins final as the trainer flies downwind: they would land together.
                if (visitor == null && flight.Phase == FlightPhase.Pattern && flight.Status.Contains("downwind"))
                {
                    visitor = airport.Traffic.Arrive(NewAircraft(airport, "Visitor"), out problem);
                    Assert.IsNotNull(visitor, problem);
                }
                extended |= flight.Waiting != null && flight.Waiting.Contains("extending downwind");
                if (visitor != null && visitorDown < 0f && visitor.Phase == FlightPhase.Landing) visitorDown = airport.Traffic.Clock;
                if (trainerDown < 0f && flight.Phase == FlightPhase.TouchAndGo) trainerDown = airport.Traffic.Clock;
                var holder = airport.Traffic.OnRunway(runway);
                if (holder != null) owners = Mathf.Max(owners, 1);
                if (visitor != null && visitor.Phase == FlightPhase.Landing && flight.Phase == FlightPhase.TouchAndGo) Assert.Fail("both on the runway at once");
            }
            Assert.IsTrue(flight.Finished, flight.Status); Assert.IsNotNull(visitor); Assert.IsTrue(visitor.Finished, visitor.Status);
            Assert.Greater(visitorDown, 0f); Assert.Greater(trainerDown, 0f);
            Assert.Greater(Mathf.Abs(trainerDown - visitorDown), 40f, "landings are spaced apart");
            Assert.AreEqual(0, visitor.GoArounds, "the arrival on final is not cut off");
            Assert.AreSame(open, visitor.Route.stand.stand, "the visitor takes the public stand, not a tie-down");
            Assert.IsTrue(school.AtHome(trainer));

            // No circuits from a strip too short to take off from again.
            var (small, _, strip) = AirportWithSchool(build: false);
            strip.end = new Vector3(-600f + 450f, 0f, 0f); strip.Rebuild();
            Assert.IsFalse(small.Traffic.CanFlyCircuits(Type, out problem)); Assert.IsNotNull(problem);
            Assert.IsNull(small.Traffic.DepartForCircuits(NewAircraft(small), 1, out problem));
        }

        [Test]
        public void LocalFlight_LeavesComesBackAndTheAircraftIsNeverDestroyed()
        {
            var (airport, school, _) = AirportWithSchool();
            var trainer = school.AddTrainer(NewAircraft(airport));
            var aircraft = trainer.aircraft;
            var flight = airport.Traffic.Depart(aircraft, out string problem); Assert.IsNotNull(flight, problem);
            for (int second = 0; second < 900 && flight.Phase != FlightPhase.Gone; second++) airport.Simulate(1f);
            Assert.AreEqual(FlightPhase.Gone, flight.Phase, flight.Status);
            Assert.IsTrue(aircraft != null, "a school aircraft that flies away is kept");
            Assert.IsFalse(aircraft.gameObject.activeSelf, "out of sight while away");
            Assert.IsTrue(trainer.absent);
            Flight back = null;
            for (int second = 0; second < 1500; second++)
            {
                airport.Simulate(1f);
                back ??= airport.Traffic.FlightOf(aircraft);
                if (back != null && back.Finished) break;
            }
            Assert.IsNotNull(back, trainer.status); Assert.AreEqual(FlightKind.Arrival, back.kind);
            Assert.AreEqual(FlightPhase.Parked, back.Phase, back.Status);
            Assert.IsTrue(aircraft.gameObject.activeSelf); Assert.IsFalse(trainer.absent);
            Assert.IsTrue(school.AtHome(trainer));
        }

        [Test]
        public void Lessons_EnrolTeachEarnFeesAndPayWages()
        {
            var (airport, school, _) = AirportWithSchool();
            var trainer = school.AddTrainer(NewAircraft(airport));
            // No instructor: nobody enrols and nothing flies.
            airport.Simulate(600f);
            Assert.IsEmpty(school.Students); Assert.AreEqual(0, school.LessonsFlown); Assert.Zero(school.TakeNet());
            Assert.IsTrue(school.Hire()); Assert.AreEqual(1, school.Instructors);
            for (int second = 0; second < 5000 && school.LessonsFlown < 2; second++) airport.Simulate(1f);
            Assert.GreaterOrEqual(school.LessonsFlown, 2, trainer.status);
            Assert.IsNotEmpty(school.Students);
            Assert.AreEqual(school.LessonsFlown, school.Students.Sum(s => s.lessons));
            Assert.AreEqual(school.LessonsFlown * (long)FlyingSchoolRules.DualFee, school.Earned, "early lessons are flown with the instructor");
            Assert.AreEqual(school.Earned, school.Unbanked);
            Assert.LessOrEqual(school.InstructorsBusy, school.Instructors);

            var state = new CareerState { funds = 1000 };
            long net = FlyingSchoolAccounts.Advance(airport, state);
            Assert.AreEqual(1000 + net, state.funds); Assert.Zero(school.Unbanked);
            Assert.Greater(school.WagesPaid, 0); Assert.AreEqual(school.Earned - school.WagesPaid, net);
            Assert.Zero(FlyingSchoolAccounts.Advance(airport, state), "nothing is credited twice");
            var sandbox = new CareerState { funds = 500, mode = GameMode.Sandbox };
            school.Simulate(2000f);
            Assert.Zero(FlyingSchoolAccounts.Advance(airport, sandbox)); Assert.AreEqual(500, sandbox.funds, "Sandbox finances are paused");

            // Paused lessons, night without the rating and a disconnected flight line all keep aircraft on the ground.
            school.LessonsPaused = true; StringAssert.Contains("paused", school.FlightProblem(airport)); school.LessonsPaused = false;
            AircraftLights.Glow = 1f; StringAssert.Contains("dark", school.FlightProblem(airport));
            Assert.IsTrue(school.Upgrade(SchoolUpgrade.NightRating)); Assert.IsFalse(school.Upgrade(SchoolUpgrade.NightRating));
            Assert.IsNull(school.FlightProblem(airport)); AircraftLights.Glow = 0f;
            Assert.AreEqual(FlyingSchoolRules.BaseStudents, school.Capacity);
            school.Upgrade(SchoolUpgrade.Classroom); Assert.AreEqual(FlyingSchoolRules.ClassroomStudents, school.Capacity);
            Assert.IsFalse(school.Dismiss() && school.InstructorsBusy > school.Instructors);
        }

        [Test]
        public void Course_SoloStagesNeedNoInstructorAndALicenceEndsIt()
        {
            Assert.IsFalse(FlyingSchoolRules.Solo(0)); Assert.IsTrue(FlyingSchoolRules.Solo(FlyingSchoolRules.FirstSolo));
            Assert.IsTrue(FlyingSchoolRules.Solo(FlyingSchoolRules.LastSolo)); Assert.IsFalse(FlyingSchoolRules.Solo(FlyingSchoolRules.LastSolo + 1));
            Assert.AreEqual("First solo", FlyingSchoolRules.Stage(FlyingSchoolRules.FirstSolo));
            Assert.AreEqual("Checkride", FlyingSchoolRules.Stage(FlyingSchoolRules.CourseLessons - 1));
            Assert.AreEqual(1, FlyingSchoolRules.TouchAndGoes(FlyingSchoolRules.FirstSolo), "a first solo is one circuit and a touch-and-go");
            Assert.IsFalse(FlyingSchoolRules.WantsCircuits(0), "introductory flights leave the circuit");
            Assert.IsTrue(FlyingSchoolRules.WantsCircuits(3)); Assert.IsFalse(FlyingSchoolRules.WantsCircuits(FlyingSchoolRules.CourseLessons - 1));
            Assert.IsNull(FlyingSchoolLayout.TrainerProblem(Type));
            var jet = AircraftSetup.AllTypes().First(t => t.category == AircraftCategory.Airliner);
            Assert.IsNotNull(FlyingSchoolLayout.TrainerProblem(jet));
        }

        [Test]
        public void Save_KeepsTheSchoolItsAircraftAndStudentsAndBringsAnAirborneLessonHome()
        {
            // A bare airport, like the hangar save fixture: only the aircraft type is a catalogued asset.
            var (airport, school, _) = AirportWithSchool(materials: false);
            var type = Type;
            var catalog = ScriptableObject.CreateInstance<GameAssetCatalog>(); spawned.Add(catalog);
            catalog.entries.Add(new GameAssetCatalog.Entry { key = "asset:test-light:1", asset = type });
            var trainer = school.AddTrainer(NewAircraft(airport));
            var parked = school.AddTrainer(NewAircraft(airport, "Second trainer"));
            school.Hire(); school.Upgrade(SchoolUpgrade.Simulator);
            for (int second = 0; second < 3000 && !school.Trainers.Any(t => t.aircraft != null && airport.Traffic.FlightOf(t.aircraft)?.Phase == FlightPhase.Climb); second++)
                airport.Simulate(1f);
            var flying = school.Trainers.First(t => airport.Traffic.FlightOf(t.aircraft) != null);
            Assert.AreNotEqual(LessonKind.None, flying.lesson); int student = flying.student;
            string id = school.SiteId; int students = school.Students.Count; long earned = school.Earned;

            var saved = AirportSaveData.Capture(airport, catalog);
            Assert.IsFalse(flying.absent, "saving leaves the running session as it is");
            Assert.IsNotNull(airport.Traffic.FlightOf(flying.aircraft));
            airport.gameObject.SetActive(false);
            var detached = JsonUtility.FromJson<AirportSaveData>(JsonUtility.ToJson(saved));
            Assert.IsTrue(detached.Validate(out var why), why);
            var restored = detached.Restore(null, catalog); spawned.Add(restored.gameObject);
            foreach (var a in restored.GetComponentsInChildren<AirCity.Aircraft.Aircraft>(true)) spawned.Add(a.gameObject);
            var loaded = restored.FlyingSchools.Single();
            Assert.AreEqual(id, loaded.SiteId); Assert.AreEqual(1, loaded.Instructors); Assert.IsTrue(loaded.Has(SchoolUpgrade.Simulator));
            Assert.AreEqual(students, loaded.Students.Count); Assert.AreEqual(earned, loaded.Earned);
            Assert.AreEqual(2, loaded.Trainers.Count);
            Assert.AreEqual(FlyingSchoolLayout.Spots, restored.Taxiways.stands.Count(s => s.owner == id));
            for (int i = 0; i < FlyingSchoolLayout.Spots; i++) Assert.IsNotNull(loaded.Spot(i));
            Assert.IsNull(loaded.AccessProblem);
            var away = loaded.Trainers.Single(t => t.registration == flying.registration);
            Assert.IsTrue(away.absent, "a flight in the air is saved as away"); Assert.AreEqual(student, away.student);
            Assert.AreSame(type, away.type);
            var home = loaded.Trainers.Single(t => t.registration != flying.registration);
            Assert.IsFalse(home.absent);
            Assert.IsNotNull(home.aircraft, "the parked trainer is rebuilt from the game catalog"); Assert.IsTrue(loaded.AtHome(home));
            loaded.ResumeTrips();
            for (int second = 0; second < 2400 && !(loaded.AtHome(away) && away.lesson == LessonKind.None); second++) restored.Simulate(1f);
            Assert.IsTrue(loaded.AtHome(away), away.status);
            Assert.AreEqual(LessonKind.None, away.lesson, "the interrupted lesson is finished on landing");
        }

        [Test]
        public void Construction_QuotesTheDrivewayAndRefusesWithoutARoadOrFunds()
        {
            var (airport, _, _) = AirportWithSchool(build: false);
            airport.transform.position = Vector3.up * 80f;
            var world = new ValleyWorld { heights = Enumerable.Repeat(80f, ValleyWorld.Resolution * ValleyWorld.Resolution).ToArray() };
            world.sites.Add(new AirportSite { name = "School fixture", centre = Vector2.zero, elevation = 80f });
            var state = new CareerState { world = world, selectedSite = 0, handsOn = true, wholeRegion = true, funds = 1000000,
                layout = new CareerLayout { unlocked = int.MaxValue, elevation = 80f }, publicRoads = new PublicRoadNetwork() };
            var host = new GameObject("School session"); host.SetActive(false); spawned.Add(host);
            var game = host.AddComponent<CareerGame>();
            typeof(CareerGame).GetProperty("State").SetValue(game, state);
            typeof(CareerGame).GetProperty("Airport").SetValue(game, airport);
            var origin = new Vector2(200f, -120f);

            // No public road within reach: the car park cannot be connected.
            Assert.IsFalse(FlyingSchoolConstruction.TryCreate(game, airport, origin, 180f, 10.5f, 1, PavementSurface.Asphalt, out var plan, out var why));
            StringAssert.Contains("public road", why);

            // A two-way road south of the site, running out of the region at both ends.
            float lot = -120f - FlyingSchoolLayout.LotBack(10.5f);
            var graph = state.publicRoads;
            int left = graph.AddNode(new Vector3(-400f, 80f, lot - 40f)); graph.Node(left).outsideConnection = true;
            int right = graph.AddNode(new Vector3(600f, 80f, lot - 40f)); graph.Node(right).outsideConnection = true;
            graph.AddSegment(left, right, PublicRoadStyle.AirportAccess, RoadStructure.Ground);
            string before = JsonUtility.ToJson(graph);
            bool quoted = FlyingSchoolConstruction.TryCreate(game, airport, origin, 180f, 10.5f, 1, PavementSurface.Asphalt, out plan, out why) ||
                          FlyingSchoolConstruction.TryCreate(game, airport, origin, 180f, 10.5f, -1, PavementSurface.Asphalt, out plan, out why);
            Assert.IsTrue(quoted, why);
            Assert.IsNotNull(plan.Road); Assert.Greater(plan.Road.Length, 20f);
            Assert.AreEqual(FlyingSchoolRules.BuildPrice + plan.Road.Price, plan.Price);
            Assert.AreEqual(before, JsonUtility.ToJson(graph), "a quote leaves the saved roads alone");
            Assert.IsEmpty(airport.FlyingSchools); Assert.AreEqual(1000000, state.funds);
            var draft = plan.Road.Draft; Assert.IsTrue(draft.Validate(out why), why);
            var port = draft.nodes.Single(n => n.terminalEntrance == FlyingSchoolLayout.EntranceId(plan.SiteId));
            Assert.IsTrue(draft.HasRoute(port.id, left) && draft.HasRoute(left, port.id), "the driveway is two-way");
            var mouth = FlyingSchoolLayout.Point(origin, 180f, FlyingSchoolLayout.LocalEntrance(10.5f, plan.Side));
            Assert.Less(Vector2.Distance(mouth, new Vector2(port.position.x, port.position.z)), .01f);
            // The driveway runs straight out of the car park far enough for its bend to be an access road's, not a kink.
            var stub = draft.At(port.id).Single();
            Assert.Greater(draft.Path(stub).Length, 25f, "the lead out of the car park leaves room for the bend");

            // The school built from that quote reaches the road; without its driveway it is closed.
            var school = airport.AddFlyingSchool(origin, 180f, plan.Lane, plan.Side, PavementSurface.Asphalt, plan.SiteId);
            Assert.IsNotNull(school); Assert.AreEqual(port.terminalEntrance, school.EntranceId);
            Assert.IsNull(FlyingSchoolConstruction.RoadProblem(draft, school));
            StringAssert.Contains("Street tool", FlyingSchoolConstruction.RoadProblem(graph, school));
            Assert.IsTrue(airport.RemoveFlyingSchool(school));

            state.funds = FlyingSchoolRules.BuildPrice - 1;
            Assert.IsFalse(FlyingSchoolConstruction.TryCreate(game, airport, origin, 180f, 10.5f, plan.Side, PavementSurface.Asphalt, out _, out why));
            StringAssert.Contains("funds", why);
            state.funds = 1000000;
            for (int i = 0; i < world.heights.Length; i++) world.heights[i] = 82f;
            Assert.IsFalse(FlyingSchoolConstruction.TryCreate(game, airport, origin, 180f, 10.5f, plan.Side, PavementSurface.Asphalt, out _, out why));
            Assert.IsEmpty(airport.FlyingSchools);
        }

        [Test]
        public void Model_StaysInsideItsSiteAndBuildsEveryLevelOfDetail()
        {
            foreach (float lane in new[] { 7.5f, 10.5f, 15f })
                foreach (int side in new[] { -1, 1 })
                {
                    var host = new GameObject("School model"); spawned.Add(host);
                    var root = FlyingSchoolBuilder.Build(host.transform, lane, side, null, Type, side > 0);
                    var group = root.GetComponent<LODGroup>(); Assert.AreEqual(3, group.GetLODs().Length);
                    float back = FlyingSchoolLayout.LotBack(lane) + .3f, front = FlyingSchoolLayout.Lane(lane) * .5f;
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        if (filter.GetComponentInParent<FlyingSchoolWindsock>() != null) continue;
                        var mesh = filter.sharedMesh; Assert.IsNotNull(mesh, filter.name); Assert.Greater(mesh.vertexCount, 0, filter.name);
                        var b = mesh.bounds;
                        Assert.GreaterOrEqual(b.min.z, front - .05f, filter.name + " reaches onto the taxilane");
                        Assert.LessOrEqual(b.max.z, back + .05f, filter.name + " behind the car park");
                        float reach = FlyingSchoolLayout.LotHalfWidth + FlyingSchoolLayout.ThroatLength + .05f;
                        Assert.LessOrEqual(Mathf.Max(-b.min.x, b.max.x), Mathf.Max(reach, FlyingSchoolLayout.ApronHalfWidth), filter.name + " outside the site");
                        if (filter.name.Contains("structure")) Assert.LessOrEqual(Mathf.Max(-b.min.x, b.max.x), FlyingSchoolLayout.BlockHalfWidth, "roof inside the protected block");
                    }
                    // Every tie-down ring lies on its stand.
                    Assert.AreEqual(FlyingSchoolLayout.Spots, Enumerable.Range(0, FlyingSchoolLayout.Spots).Select(FlyingSchoolLayout.SpotOffset).Distinct().Count());
                    Object.DestroyImmediate(host);
                }
            Assert.Greater(FlyingSchoolLayout.ParkingBays(10.5f).Count, 12);
            Assert.AreEqual(2, FlyingSchoolLayout.ParkingBays(10.5f).Count(b => b.accessible));
        }
    }
}
