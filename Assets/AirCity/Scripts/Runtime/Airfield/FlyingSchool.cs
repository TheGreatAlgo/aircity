using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.Airfield.Building;
using AirCity.Game;
using AirCity.Traffic;
using UnityEngine;

namespace AirCity.Airfield
{
    public enum SchoolUpgrade { Classroom = 0, Simulator = 1, NightRating = 2 }
    public enum LessonKind { None = 0, Circuits = 1, LocalFlight = 2 }

    /// <summary>Prices, fees and the syllabus. Initial balance values, shared by both game modes.</summary>
    public static class FlyingSchoolRules
    {
        public const int BuildPrice = 68000, TrainerPrice = 42000, TrainerResale = 21000, HireFee = 1500;
        public const int MaxInstructors = 4;
        /// <summary>An instructor's pay for each calendar day on the payroll.</summary>
        public const int DailyWage = 110;
        public const int DualFee = 260, SoloFee = 150, SimulatorFee = 90, GraduationBonus = 2400;
        /// <summary>Lessons to a licence, and the first and last a student flies alone.</summary>
        public const int CourseLessons = 12, FirstSolo = 7, LastSolo = 9;
        public const int BaseStudents = 4, ClassroomStudents = 8, MaxSimulatorLessons = 3;
        /// <summary>Game seconds: between lessons on one aircraft, a new student enrolling, a simulator session.</summary>
        public const float Turnaround = 150f, EnrolEvery = 420f, SimulatorSession = 320f;
        /// <summary>How long a local flight stays out of sight (game seconds), and when a failed start is tried again.</summary>
        public const float LocalFlight = 330f, Retry = 20f;
        /// <summary>At most this many of one school's aircraft fly circuits at once.</summary>
        public const int CircuitAircraft = 2;

        public static int Price(SchoolUpgrade upgrade) => upgrade switch
        { SchoolUpgrade.Classroom => 18000, SchoolUpgrade.Simulator => 55000, _ => 12000 };
        public static string Name(SchoolUpgrade upgrade) => upgrade switch
        { SchoolUpgrade.Classroom => "Second classroom", SchoolUpgrade.Simulator => "Flight simulator", _ => "Night rating course" };
        public static string Effect(SchoolUpgrade upgrade) => upgrade switch
        {
            SchoolUpgrade.Classroom => $"Enrols {ClassroomStudents} students at once instead of {BaseStudents}.",
            SchoolUpgrade.Simulator => "Students train indoors while no aircraft can fly them: at night, in snow or when the line is busy.",
            _ => "Lessons continue after dark on lit runways and taxiways.",
        };

        public static string Stage(int lessons) => lessons < 2 ? "Introductory flights" : lessons < FirstSolo ? "Circuit training" :
            lessons == FirstSolo ? "First solo" : lessons <= LastSolo ? "Solo consolidation" :
            lessons < CourseLessons - 1 ? "Navigation" : "Checkride";
        public static bool Solo(int lessons) => lessons >= FirstSolo && lessons <= LastSolo;
        /// <summary>Circuits while learning to land and for the solo flights; away from the airfield otherwise.</summary>
        public static bool WantsCircuits(int lessons) => lessons >= 2 && lessons <= LastSolo;
        public static int TouchAndGoes(int lessons) => lessons == FirstSolo ? 1 : 2;
    }

    [Serializable]
    public sealed class SchoolTrainer
    {
        public int spot;
        public AircraftType type;
        public AircraftLiveryAsset livery;
        public string registration = "";
        public LessonKind lesson;
        public int student = -1;
        public bool dual;
        /// <summary>Game seconds until the next lesson may start, and how long the lesson under way has taken.</summary>
        public float readyIn, lessonTime;
        public int flights;
        // The saved snapshot (FlyingSchool.CaptureState). A save is taken while play goes on, so these are written
        // then and read only when the school is restored; the session itself runs on the fields below.
        public bool savedAway, savedAbsent;
        public Vector3 position;
        public float heading, savedReturnIn;
        /// <summary>Saved intent, never a Flight: 1 taxi back to the line, 3 leave on the lesson again.</summary>
        public int trip;
        /// <summary>Away from its tie-down pose (`position` applies), and out of sight off the airport.</summary>
        [NonSerialized] public bool away, absent;
        /// <summary>Game seconds until an absent aircraft comes back onto final.</summary>
        [NonSerialized] public float returnIn;
        [NonSerialized] public AirCity.Aircraft.Aircraft aircraft;
        [NonSerialized] public string status;
        [NonSerialized] public float retry, stuck;
    }

    [Serializable]
    public sealed class SchoolStudent
    {
        public int id;
        public string name = "";
        public int lessons, simulator;
        public float waited;
        public string Stage => FlyingSchoolRules.Stage(lessons);
    }

    /// <summary>Marks a school's aircraft, so traffic finds its home without searching every school.</summary>
    [DisallowMultipleComponent]
    public sealed class SchoolTrainerTag : MonoBehaviour
    {
        [NonSerialized] public FlyingSchool school;
        [NonSerialized] public SchoolTrainer trainer;
    }

    /// <summary>
    /// A flying school: a clubhouse, car park and driveway (FlyingSchoolLayout) behind its own flight line of
    /// tie-down stands. Those are real stands in TaxiwayNetwork.stands marked with this school's id (Stand.owner), so
    /// its aircraft taxi, push back and land through the ordinary traffic; nobody else is sent to them. The school
    /// buys trainers, hires instructors, enrols students and flies lessons: circuits with touch-and-goes
    /// (AirTraffic.DepartForCircuits) or local flights that leave and come back. Simulated in game seconds from
    /// Airport.Simulate (tests run it headless); money it earns waits in `Unbanked` for the session's accounts.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class FlyingSchool : MonoBehaviour
    {
        [SerializeField] string siteId = "";
        [SerializeField] float lane;
        [SerializeField] PavementSurface surface = PavementSurface.Asphalt;
        [SerializeField] int drivewaySide = 1;
        [SerializeField] int[] standIds = Array.Empty<int>();
        [SerializeField] List<SchoolTrainer> trainers = new List<SchoolTrainer>();
        [SerializeField] List<SchoolStudent> students = new List<SchoolStudent>();
        [SerializeField] int instructors, upgrades, graduates, lessonsFlown, nextStudent = 1;
        [SerializeField] bool lessonsPaused;
        [SerializeField] long earned, wagesPaid, unbanked;
        [SerializeField] double wageRemainder;
        [SerializeField] float enrolIn = 60f, simulatorLeft;
        [SerializeField] int simulatorStudent = -1;
        [SerializeField] uint seed = 2463534242u;
        float pending;

        public Airport Airport => GetComponentInParent<Airport>();
        public string SiteId => siteId;
        public float Lane => FlyingSchoolLayout.Lane(lane);
        public PavementSurface Surface => surface;
        public int DrivewaySide => FlyingSchoolLayout.Side(drivewaySide);
        public string EntranceId => FlyingSchoolLayout.EntranceId(siteId);
        public Vector2 Origin => Geo.XZ(transform.localPosition);
        public float Heading => transform.localEulerAngles.y;
        public Vector2 Axis => FlyingSchoolLayout.Axis(Heading);
        public IReadOnlyList<SchoolTrainer> Trainers => trainers;
        public IReadOnlyList<SchoolStudent> Students => students;
        public int Instructors => instructors;
        public int Graduates => graduates;
        public int LessonsFlown => lessonsFlown;
        public long Earned => earned;
        public long WagesPaid => wagesPaid;
        public long Unbanked => unbanked;
        public bool LessonsPaused { get => lessonsPaused; set => lessonsPaused = value; }
        public int Capacity => Has(SchoolUpgrade.Classroom) ? FlyingSchoolRules.ClassroomStudents : FlyingSchoolRules.BaseStudents;
        public bool Has(SchoolUpgrade upgrade) => (upgrades & (1 << (int)upgrade)) != 0;
        public string Notice { get; private set; }
        public int NoticeSerial { get; private set; }
        public SchoolStudent SimulatorStudent => simulatorStudent >= 0 ? students.Find(s => s.id == simulatorStudent) : null;

        public IEnumerable<Vector2[]> Footprints(bool flightLine = true) =>
            FlyingSchoolLayout.Footprints(Origin, Heading, lane, drivewaySide, flightLine);
        public Vector2[] Block => FlyingSchoolLayout.Block(Origin, Heading, lane);
        public Vector2 LocalEntrance => FlyingSchoolLayout.LocalEntrance(lane, drivewaySide);
        /// <summary>The driveway mouth on the car park's edge, Airport space.</summary>
        public Vector2 Entrance => FlyingSchoolLayout.Point(Origin, Heading, LocalEntrance);
        public Vector2 DrivewayOutward => Geo.Right(Axis) * DrivewaySide;
        public AirCity.Vehicles.AirportMaintenance.Box BuildingBox => AirCity.Vehicles.AirportMaintenance.Box.Of(
            Origin + Axis * FlyingSchoolLayout.BuildingCentre(lane), Axis,
            new Bounds(Vector3.zero, new Vector3(FlyingSchoolLayout.BuildingWidth + 1.2f, 6f, FlyingSchoolLayout.BuildingDepth + 1.2f)), 0f);

        public bool Contains(Vector2 point, float margin = 0f)
        {
            var local = FlyingSchoolLayout.Local(Origin, Heading, point);
            float l = Lane;
            if (local.y >= l * .5f - margin && local.y <= FlyingSchoolLayout.ApronDepth(l) &&
                Mathf.Abs(local.x) <= FlyingSchoolLayout.ApronHalfWidth + margin) return true;
            return local.y >= FlyingSchoolLayout.ApronDepth(l) && local.y <= FlyingSchoolLayout.LotBack(l) + margin &&
                   Mathf.Abs(local.x) <= FlyingSchoolLayout.BlockHalfWidth + margin;
        }

        internal void Configure(string id, float laneWidth, PavementSurface pavement, int side, int[] stands)
        {
            siteId = id; lane = FlyingSchoolLayout.Lane(laneWidth); surface = pavement; drivewaySide = FlyingSchoolLayout.Side(side);
            standIds = stands;
            uint hash = 2166136261u;
            foreach (char c in id) hash = (hash ^ c) * 16777619u;
            seed = hash | 1u;
        }

        // ------------------------------------------------------------------ the flight line

        public Stand Spot(int index)
        {
            var net = Airport != null ? Airport.Taxiways : null;
            if (net == null || index < 0 || index >= standIds.Length) return null;
            return net.stands.Find(s => s.id == standIds[index] && s.owner == siteId);
        }

        public StandLayout SpotLayout(int index)
        {
            var stand = Spot(index);
            return stand != null ? Airport.Taxiways.Layout(stand) : null;
        }

        public int SpotOf(Stand stand) => stand != null && stand.owner == siteId ? Array.IndexOf(standIds, stand.id) : -1;

        /// <summary>The stand this aircraft lives on, if it is one of a school's (else null).</summary>
        public static Stand HomeOf(AirCity.Aircraft.Aircraft aircraft)
        {
            var tag = aircraft != null ? aircraft.GetComponent<SchoolTrainerTag>() : null;
            return tag != null && tag.school != null && tag.trainer != null ? tag.school.Spot(tag.trainer.spot) : null;
        }

        public static FlyingSchool OfTrainer(AirCity.Aircraft.Aircraft aircraft)
        {
            var tag = aircraft != null ? aircraft.GetComponent<SchoolTrainerTag>() : null;
            return tag != null ? tag.school : null;
        }

        public SchoolTrainer TrainerOf(AirCity.Aircraft.Aircraft aircraft) =>
            aircraft != null ? trainers.Find(t => t.aircraft == aircraft) : null;

        /// <summary>Why no aircraft can use the flight line, or null: every tie-down needs a taxiway along its front.</summary>
        public string AccessProblem
        {
            get
            {
                if (Airport == null) return "Place the flying school at an airport first.";
                for (int i = 0; i < FlyingSchoolLayout.Spots; i++)
                {
                    var layout = SpotLayout(i);
                    if (layout == null) return "The flight line has been removed. Rebuild the school.";
                    if (layout.edge == null) return "Lay a taxiway along the front of the flight line to connect it.";
                }
                return null;
            }
        }

        /// <summary>Why students cannot reach the school by road, or null (fixtures without a world have no check).</summary>
        public string RoadProblem => Airport != null && Airport.SchoolRoadProblem != null ? Airport.SchoolRoadProblem(this) : null;

        // ------------------------------------------------------------------ management

        public bool CanDemolish => trainers.Count == 0;

        public int FreeSpot()
        {
            for (int i = 0; i < FlyingSchoolLayout.Spots; i++) if (!trainers.Exists(t => t.spot == i)) return i;
            return -1;
        }

        public bool BuyTrainer(AircraftType type, out string problem)
        {
            problem = FlyingSchoolLayout.TrainerProblem(type);
            if (problem != null) return false;
            int spot = FreeSpot();
            if (spot < 0) { problem = "Every tie-down on the flight line has an aircraft."; return false; }
            var layout = SpotLayout(spot);
            if (layout == null) { problem = "The flight line has been removed. Rebuild the school."; return false; }
            var airport = Airport;
            foreach (var other in AirCity.Aircraft.Aircraft.All)
            {
                if (other == null) continue;
                var at = airport.transform.InverseTransformPoint(other.transform.position);
                if (at.y < 3f && layout.Contains(Geo.XZ(at))) { problem = "Another aircraft is standing on that tie-down."; return false; }
            }
            var trainer = new SchoolTrainer { spot = spot, type = type, registration = NextRegistration(), readyIn = 30f };
            trainers.Add(trainer);
            RebuildTrainer(trainer);
            return true;
        }

        public bool CanSell(SchoolTrainer trainer) => trainer != null && trainers.Contains(trainer) && trainer.lesson == LessonKind.None &&
            !trainer.absent && (trainer.aircraft == null || Airport.Traffic.FlightOf(trainer.aircraft) == null);

        public bool SellTrainer(SchoolTrainer trainer)
        {
            if (!CanSell(trainer)) return false;
            ClearTrainer(trainer);
            trainers.Remove(trainer);
            return true;
        }

        public bool Hire()
        {
            if (instructors >= FlyingSchoolRules.MaxInstructors) return false;
            instructors++; return true;
        }

        public bool Dismiss()
        {
            if (instructors <= 0 || instructors <= InstructorsBusy) return false;
            instructors--; return true;
        }

        public bool Upgrade(SchoolUpgrade upgrade)
        {
            if (Has(upgrade)) return false;
            upgrades |= 1 << (int)upgrade; return true;
        }

        public int InstructorsBusy => trainers.Count(t => t.lesson != LessonKind.None && t.dual) + (simulatorStudent >= 0 ? 1 : 0);

        string NextRegistration()
        {
            var airport = Airport;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (airport != null)
            {
                foreach (var school in airport.FlyingSchools) foreach (var t in school.trainers) taken.Add(t.registration);
                foreach (var hangar in airport.Hangars) if (hangar.Occupied) taken.Add(hangar.Registration);
            }
            for (int n = 1; ; n++)
            {
                string tail = "AC-S" + n.ToString("00");
                if (!taken.Contains(tail)) return tail;
            }
        }

        // ------------------------------------------------------------------ the aircraft

        Vector2 HomePosition(SchoolTrainer t, StandLayout layout) => layout.ParkPosition(t.type);

        public bool AtHome(SchoolTrainer t)
        {
            if (t.aircraft == null || t.absent) return false;
            var layout = SpotLayout(t.spot);
            if (layout == null) return false;
            var airport = Airport.transform;
            var at = Geo.XZ(airport.InverseTransformPoint(t.aircraft.transform.position));
            var facing = Geo.XZ(airport.InverseTransformDirection(t.aircraft.transform.forward));
            return Vector2.Distance(at, HomePosition(t, layout)) < .6f && Vector2.Angle(facing, layout.nose) < 6f;
        }

        void RebuildTrainer(SchoolTrainer t)
        {
            if (t.type == null || FlyingSchoolLayout.TrainerProblem(t.type) != null) return;
            var airport = Airport;
            if (airport == null) return;
            if (t.aircraft == null)
                t.aircraft = airport.GetComponentsInChildren<AirCity.Aircraft.Aircraft>(true)
                    .FirstOrDefault(a => a.name == "Trainer " + t.registration && a.type == t.type);
            if (t.aircraft == null)
            {
                var catalog = GameAssetCatalog.Load();
                var prefab = catalog?.entries.Select(e => e.asset).OfType<GameObject>()
                    .FirstOrDefault(go => go.GetComponent<AirCity.Aircraft.Aircraft>()?.type == t.type);
                if (prefab == null) return;
                var staging = new GameObject("Trainer staging"); staging.SetActive(false);
                staging.hideFlags = HideFlags.DontSave; staging.transform.SetParent(transform, false);
                var go = Instantiate(prefab, staging.transform, false); go.SetActive(false);
                foreach (var body in go.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                foreach (var child in go.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.DontSave;
                go.name = "Trainer " + t.registration;
                go.transform.SetParent(airport.transform, false);
                t.aircraft = go.GetComponent<AirCity.Aircraft.Aircraft>();
                var paint = go.GetComponent<AircraftLiveryApplier>();
                if (paint != null)
                {
                    if (t.livery != null) paint.SetLivery(t.livery, t.registration);
                    else { paint.registration = t.registration; t.livery = paint.liveryAsset; }
                }
                AirfieldPart.DestroyAny(staging);
            }
            Adopt(t, t.aircraft);
            var layout = SpotLayout(t.spot);
            if (t.away) t.aircraft.transform.localPosition = t.position;
            else if (layout != null)
            {
                var home = HomePosition(t, layout);
                t.aircraft.transform.localPosition = Geo.X0Z(home, airport.PavementElevationAt(home));
            }
            float bearing = t.away || layout == null ? t.heading : Mathf.Atan2(layout.nose.x, layout.nose.y) * Mathf.Rad2Deg;
            t.aircraft.transform.localRotation = Quaternion.Euler(0f, bearing, 0f);
            t.aircraft.gameObject.SetActive(!t.absent);
            if (!t.absent) Quiet(t.aircraft);
        }

        /// <summary>Take an existing aircraft object as this trainer's (tests and fixtures supply their own).</summary>
        internal void Adopt(SchoolTrainer t, AirCity.Aircraft.Aircraft aircraft)
        {
            t.aircraft = aircraft;
            if (aircraft == null) return;
            var tag = aircraft.GetComponent<SchoolTrainerTag>();
            if (tag == null) tag = aircraft.gameObject.AddComponent<SchoolTrainerTag>();
            tag.school = this; tag.trainer = t;
        }

        /// <summary>Fixtures: a trainer of this type on the next free tie-down, using the aircraft object given.</summary>
        public SchoolTrainer AddTrainer(AirCity.Aircraft.Aircraft aircraft)
        {
            int spot = FreeSpot();
            var layout = spot >= 0 ? SpotLayout(spot) : null;
            if (aircraft == null || layout == null || FlyingSchoolLayout.TrainerProblem(aircraft.type) != null) return null;
            var t = new SchoolTrainer { spot = spot, type = aircraft.type, registration = NextRegistration() };
            trainers.Add(t);
            aircraft.name = "Trainer " + t.registration;
            aircraft.transform.SetParent(Airport.transform, true);
            Adopt(t, aircraft);
            var home = HomePosition(t, layout);
            aircraft.transform.localPosition = Geo.X0Z(home, Airport.PavementElevationAt(home));
            aircraft.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(layout.nose.x, layout.nose.y) * Mathf.Rad2Deg, 0f);
            return t;
        }

        void ClearTrainer(SchoolTrainer t)
        {
            if (t.aircraft == null) return;
            t.aircraft.gameObject.SetActive(false);
            AirfieldPart.DestroyAny(t.aircraft.gameObject);
            t.aircraft = null;
        }

        static void Quiet(AirCity.Aircraft.Aircraft aircraft)
        {
            aircraft.GetComponent<AircraftGroundMotion>()?.Drive(0, 0);
            foreach (var prop in aircraft.GetComponentsInChildren<PropellerSpinner>(true)) prop.rpm = 0;
            foreach (var light in aircraft.GetComponentsInChildren<AircraftLights>(true))
                light.beacon = light.strobes = light.landing = light.taxi = false;
        }

        // ------------------------------------------------------------------ traffic callbacks

        /// <summary>One of this school's aircraft has flown out of sight: it is kept (hidden) and comes back.</summary>
        internal bool OnTrainerGone(AirCity.Aircraft.Aircraft aircraft)
        {
            var t = TrainerOf(aircraft);
            if (t == null) return false;
            t.absent = t.away = true; t.trip = 0;
            t.returnIn = t.lesson == LessonKind.LocalFlight ? FlyingSchoolRules.LocalFlight * (0.85f + 0.3f * Random01()) : 180f;
            aircraft.Highlight = AircraftHighlight.None;
            aircraft.gameObject.SetActive(false);
            return true;
        }

        /// <summary>One of this school's aircraft has parked (traffic's route ended).</summary>
        internal void OnTrainerParked(AirCity.Aircraft.Aircraft aircraft)
        {
            var t = TrainerOf(aircraft);
            if (t == null) return;
            Quiet(aircraft);
            if (AtHome(t)) FinishLesson(t, true);
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>Advance the school (game seconds). Decisions are taken once a game second.</summary>
        public void Simulate(float seconds)
        {
            if (!isActiveAndEnabled || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            var airport = Airport;
            if (airport == null || airport.reconstructing) return;
            wageRemainder += seconds * instructors * (FlyingSchoolRules.DailyWage / (double)CareerClock.SimulationSecondsPerDay);
            pending += seconds;
            int steps = Mathf.Min(Mathf.FloorToInt(pending), 3600);
            pending -= Mathf.Floor(pending);
            for (int i = 0; i < steps; i++) Tick(airport);
        }

        void Tick(Airport airport)
        {
            Enrol();
            string blocked = FlightProblem(airport);
            foreach (var t in trainers.ToArray()) Step(airport, t, blocked);
            StepSimulator(blocked);
            foreach (var s in students) s.waited += 1f;
        }

        void Enrol()
        {
            if (students.Count >= Capacity) { enrolIn = Mathf.Max(enrolIn, 30f); return; }
            if (trainers.Count == 0 || instructors == 0) return;
            enrolIn -= 1f;
            if (enrolIn > 0f) return;
            enrolIn = FlyingSchoolRules.EnrolEvery / (1f + .08f * Mathf.Min(graduates, 15));
            students.Add(new SchoolStudent { id = nextStudent++, name = NewName() });
        }

        /// <summary>Why nobody can fly a lesson now (the same for every aircraft), or null.</summary>
        public string FlightProblem(Airport airport)
        {
            if (lessonsPaused) return "Lessons are paused.";
            string access = AccessProblem;
            if (access != null) return access;
            string road = RoadProblem;
            if (road != null) return road;
            if (airport.Condition == SurfaceCondition.Snow) return "Grounded by snow.";
            if (Airport.IsNight && !Has(SchoolUpgrade.NightRating)) return "No flying after dark without the night rating course.";
            return null;
        }

        void Step(Airport airport, SchoolTrainer t, string blocked)
        {
            var traffic = airport.Traffic;
            if (t.absent)
            {
                t.lessonTime += 1f;
                t.returnIn -= 1f;
                if (t.returnIn > 0f) { t.status = t.lesson == LessonKind.LocalFlight ? "Away on a local flight" : "Away from the airfield"; return; }
                if (t.aircraft == null) RebuildTrainer(t);
                if (t.aircraft == null) { t.returnIn = FlyingSchoolRules.Retry; return; }
                t.aircraft.gameObject.SetActive(true);
                var back = traffic.Arrive(t.aircraft, out string why);
                if (back == null)
                {
                    t.aircraft.gameObject.SetActive(false);
                    t.returnIn = FlyingSchoolRules.Retry;
                    t.status = "Holding away from the airfield: " + why;
                    return;
                }
                t.absent = false; t.away = true; t.status = null;
                return;
            }
            if (t.aircraft == null)
            {
                // Removed by hand: the school gets its aircraft back on its tie-down; the lesson is void.
                t.away = false; RebuildTrainer(t);
                FinishLesson(t, false);
                if (t.aircraft == null) { t.status = "No aircraft model is available for this trainer."; return; }
            }
            var flight = traffic.FlightOf(t.aircraft);
            if (flight != null)
            {
                t.stuck = 0f; t.status = flight.Status;
                if (t.lesson != LessonKind.None) t.lessonTime += 1f;
                return;
            }
            if (airport.Maintenance.TowOf(t.aircraft) != null) { t.status = "Being towed"; return; }
            if (!AtHome(t))
            {
                // Left out on the airfield (a flight interrupted by a layout change or a restored save): taxi home.
                t.retry -= 1f; t.stuck += 1f;
                if (t.retry > 0f) return;
                t.retry = FlyingSchoolRules.Retry;
                var stand = Spot(t.spot);
                string why = "its tie-down has gone";
                if (stand != null && traffic.TaxiToStand(t.aircraft, stand, out why) != null) { t.status = "Taxiing back to the flight line"; return; }
                t.status = "Cannot taxi back to the flight line: " + why;
                if (t.stuck < 240f) return;
                // No way back on the ground: it is ferried round and lands again.
                t.stuck = 0f; t.absent = true; t.away = true; t.returnIn = 120f;
                t.aircraft.gameObject.SetActive(false);
                return;
            }
            t.stuck = 0f; t.away = false;
            if (t.lesson != LessonKind.None) { FinishLesson(t, false); return; }
            if (t.readyIn > 0f) { t.readyIn -= 1f; t.status = "Being turned round for the next lesson"; return; }
            if (blocked != null) { t.status = blocked; return; }
            t.retry -= 1f;
            if (t.retry > 0f) return;
            t.status = StartLesson(airport, t);
            if (t.status != null) t.retry = FlyingSchoolRules.Retry;
        }

        /// <summary>Send this aircraft off with the student who has waited longest and can be taught now. Null when
        /// it has left; else why not.</summary>
        string StartLesson(Airport airport, SchoolTrainer t)
        {
            if (students.Count == 0) return instructors == 0 ? "Hire an instructor to enrol students." : "Waiting for a student to enrol.";
            var traffic = airport.Traffic;
            bool instructorFree = InstructorsBusy < instructors;
            SchoolStudent student = null;
            foreach (var s in students.OrderByDescending(s => s.waited))
            {
                if (s.id == simulatorStudent || trainers.Exists(o => o.student == s.id)) continue;
                if (!FlyingSchoolRules.Solo(s.lessons) && !instructorFree) continue;
                student = s; break;
            }
            if (student == null) return instructorFree ? "Every student is flying." : instructors == 0 ? "Hire an instructor." : "Waiting for a free instructor.";
            bool circuits = FlyingSchoolRules.WantsCircuits(student.lessons) &&
                            trainers.Count(o => o.lesson == LessonKind.Circuits) < FlyingSchoolRules.CircuitAircraft &&
                            traffic.CanFlyCircuits(t.type, out _);
            string why;
            var flight = circuits
                ? traffic.DepartForCircuits(t.aircraft, FlyingSchoolRules.TouchAndGoes(student.lessons), out why)
                : traffic.Depart(t.aircraft, out why);
            if (flight == null) return "Cannot leave: " + why;
            t.lesson = circuits ? LessonKind.Circuits : LessonKind.LocalFlight;
            t.student = student.id; t.dual = !FlyingSchoolRules.Solo(student.lessons);
            t.lessonTime = 0f; t.away = true; student.waited = 0f;
            return null;
        }

        void FinishLesson(SchoolTrainer t, bool flown)
        {
            if (t.lesson == LessonKind.None) { t.student = -1; return; }
            var student = students.Find(s => s.id == t.student);
            if (flown && student != null)
            {
                int fee = t.dual ? FlyingSchoolRules.DualFee : FlyingSchoolRules.SoloFee;
                bool firstSolo = student.lessons == FlyingSchoolRules.FirstSolo;
                student.lessons++; lessonsFlown++; t.flights++;
                Credit(fee);
                if (firstSolo) Announce($"{student.name} has flown a first solo.");
                Graduate(student);
            }
            t.lesson = LessonKind.None; t.student = -1; t.dual = false; t.trip = 0;
            t.readyIn = FlyingSchoolRules.Turnaround; t.retry = 0f;
        }

        void Graduate(SchoolStudent student)
        {
            if (student.lessons < FlyingSchoolRules.CourseLessons) return;
            students.Remove(student); graduates++;
            Credit(FlyingSchoolRules.GraduationBonus);
            Announce($"{student.name} has passed the checkride and earned a pilot's licence.");
        }

        void StepSimulator(string blocked)
        {
            if (!Has(SchoolUpgrade.Simulator)) { simulatorStudent = -1; return; }
            if (simulatorStudent >= 0)
            {
                simulatorLeft -= 1f;
                if (simulatorLeft > 0f) return;
                var done = students.Find(s => s.id == simulatorStudent);
                simulatorStudent = -1;
                if (done == null) return;
                done.lessons++; done.simulator++; done.waited = 0f;
                Credit(FlyingSchoolRules.SimulatorFee);
                Graduate(done);
                return;
            }
            if (lessonsPaused || InstructorsBusy >= instructors) return;
            // The simulator fills in when no aircraft can take a student who is still in dual training.
            bool aircraftFree = blocked == null && trainers.Exists(t => t.lesson == LessonKind.None && !t.absent && t.readyIn <= 0f);
            if (aircraftFree) return;
            var next = students.Where(s => !FlyingSchoolRules.Solo(s.lessons) && s.lessons < FlyingSchoolRules.CourseLessons - 1 &&
                    s.simulator < FlyingSchoolRules.MaxSimulatorLessons && s.waited > 60f && !trainers.Exists(t => t.student == s.id))
                .OrderByDescending(s => s.waited).FirstOrDefault();
            if (next == null) return;
            simulatorStudent = next.id; simulatorLeft = FlyingSchoolRules.SimulatorSession;
        }

        void Credit(int amount) { earned += amount; unbanked += amount; }

        void Announce(string text) { Notice = text; NoticeSerial++; }

        /// <summary>The session's accounts collect what the school has earned and owes since they last asked: whole
        /// currency units, wages already taken off (so it can be negative).</summary>
        public long TakeNet()
        {
            long wages = (long)Math.Floor(wageRemainder);
            wageRemainder -= wages; wagesPaid += wages;
            long net = unbanked - wages; unbanked = 0;
            return net;
        }

        // ------------------------------------------------------------------ saving

        /// <summary>Write the saved snapshot; the running session is left as it is. Flights are never saved: an
        /// aircraft in the air (or on the runway) is saved as away and lands again after loading; one taxiing keeps
        /// its pose and where it was going.</summary>
        public void CaptureState()
        {
            var airport = Airport;
            if (airport == null) return;
            foreach (var t in trainers)
            {
                t.trip = 0; t.savedAbsent = t.absent; t.savedAway = t.away; t.savedReturnIn = t.returnIn;
                if (t.absent || t.aircraft == null) continue;
                t.position = airport.transform.InverseTransformPoint(t.aircraft.transform.position);
                var facing = airport.transform.InverseTransformDirection(t.aircraft.transform.forward);
                t.heading = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                t.savedAway = !AtHome(t);
                var flight = airport.Traffic.FlightOf(t.aircraft);
                if (flight == null) continue;
                bool taxiing = flight.Phase == FlightPhase.Ready || flight.Phase == FlightPhase.HandPush || flight.Phase == FlightPhase.PushBack ||
                               flight.Phase == FlightPhase.StartingEngines || (flight.Phase == FlightPhase.Taxi && !OnRunway(airport, t.position));
                t.savedAway = true;
                if (taxiing) t.trip = flight.kind == FlightKind.Departure ? 3 : 1;
                else { t.savedAbsent = true; t.savedReturnIn = 90f; }
            }
        }

        static bool OnRunway(Airport airport, Vector3 local)
        {
            foreach (var r in airport.Runways)
            {
                r.Locate(local, out float s, out float across);
                if (s > -r.WorkMargin && s < r.Length + r.WorkMargin && Mathf.Abs(across) < r.HalfWidthTotal) return true;
            }
            return false;
        }

        /// <summary>After a save has been restored (and CaptureState, which a save in progress may have followed by
        /// more play): flights saved as away are away; ones saved taxiing go on to where they were going.</summary>
        public void ResumeTrips()
        {
            var airport = Airport;
            if (airport == null) return;
            foreach (var t in trainers)
            {
                int trip = t.trip; t.trip = 0;
                if (t.absent || t.aircraft == null || trip == 0 || airport.Traffic.FlightOf(t.aircraft) != null) continue;
                if (trip == 3 && t.lesson != LessonKind.None)
                {
                    var student = students.Find(s => s.id == t.student);
                    var flight = t.lesson == LessonKind.Circuits && student != null
                        ? airport.Traffic.DepartForCircuits(t.aircraft, FlyingSchoolRules.TouchAndGoes(student.lessons), out _)
                        : airport.Traffic.Depart(t.aircraft, out _);
                    if (flight != null) continue;
                }
                var stand = Spot(t.spot);
                if (stand != null) airport.Traffic.TaxiToStand(t.aircraft, stand, out _);
            }
        }

        // ------------------------------------------------------------------ names

        float Random01()
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            return (seed & 0xFFFFFF) / (float)0x1000000;
        }

        static readonly string[] Given =
        {
            "Mara", "Jonas", "Imre", "Tessa", "Oluwa", "Kirin", "Bram", "Noor", "Sefa", "Linde", "Aiko", "Ravi", "Petra",
            "Dario", "Yuna", "Callum", "Ines", "Tomas", "Wren", "Amara", "Leif", "Sana", "Hugo", "Mirela",
        };
        static readonly string[] Family =
        {
            "Lindqvist", "Okafor", "Varga", "Halloran", "Brandt", "Nakano", "Devries", "Moretti", "Sandoval", "Kowalik",
            "Aubert", "Teixeira", "Rahimi", "Whitlock", "Stenmark", "Obi", "Castell", "Pryce", "Imai", "Volkov",
        };

        string NewName()
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                string name = Given[Mathf.Min((int)(Random01() * Given.Length), Given.Length - 1)] + " " +
                              Family[Mathf.Min((int)(Random01() * Family.Length), Family.Length - 1)];
                if (!students.Exists(s => s.name == name)) return name;
            }
            return "Student " + nextStudent;
        }

        // ------------------------------------------------------------------ lifetime

        public void Rebuild()
        {
            if (!isActiveAndEnabled) return;
            FlyingSchoolBuilder.Rebuild(this);
            foreach (var t in trainers) RebuildTrainer(t);
        }

        void OnEnable()
        {
            FlyingSchoolBuilder.Rebuild(this);
            foreach (var t in trainers)
            {
                t.absent = t.savedAbsent; t.away = t.savedAway; t.returnIn = t.savedReturnIn;
                RebuildTrainer(t);
            }
        }

        void OnDisable()
        {
            CaptureState();
            FlyingSchoolBuilder.Clear(this);
            // Trainers are airport siblings. Unity owns their teardown while that parent is deactivating.
            var airport = Airport;
            if (airport != null && airport.gameObject.activeInHierarchy)
                foreach (var t in trainers) ClearTrainer(t);
        }
    }
}
