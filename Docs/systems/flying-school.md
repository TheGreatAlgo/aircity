# Flying school

## Player experience

Choose **Flying school** in the bottom build toolbar. Career unlocks it with the visitor centre, once the
airport has its public road. Sandbox construction is free. Both modes use the same placement, traffic and
saved data. Prices, fees and the syllabus are initial balance values.

The school is one facility with two sides:

- **Airside:** a flight line of three tie-downs for light aircraft (T1 to T3), opening onto a taxiway.
  Paved tie-downs are painted stands; grass ones are marked by white corner boards and anchor pads.
- **Landside:** the clubhouse, a 16-bay car park and a two-way driveway to a public road.

Point where the clubhouse should stand, beside a straight, level taxiway. With **Snap** on, the flight line
snaps onto that taxiway and the school stands on the cursor's side of it. Away from a taxiway the school is
placed freely on the placement heading (R or Shift+scroll turns it); lay a taxiway along the front of the
flight line afterwards to connect it. The preview shows the building, the tie-downs, the taxilane the flight
line needs and the driveway the purchase includes.

| Option | Effect |
|---|---|
| Tie-downs | **Match taxiway** (paved beside a paved taxiway, grass beside grass), **Paved** or **Grass**. |
| Flip driveway | Leaves the car park on the other side. The tool also switches sides by itself to reach a road. |

Prepare the whole outlined site as cleared, dry, level ground at the airport height, beside a connected
public road within 95 m of the car park. The base price is $68,000 plus the quoted driveway and its
earthwork. Invalid placements show their reason before purchase, and a refused placement changes no funds,
roads or airport data.

The driveway runs 29 m straight out of the car park before it turns towards the road, which leaves room for the
access road's full 25 m bend. Where that lead cannot be built (the road is within 20 m of it, or something is in
its way) the 12 m stub is judged instead (`FlyingSchoolLayout.DrivewayLeads`), and its bend takes nearly the whole
stub. Schools built earlier keep the driveway they have.

Select the school to open its inspector. Clicking a tie-down selects the school, not a separate stand.

| Tab | What it shows |
|---|---|
| Overview | Whether the school is open and why not, students and instructors, lessons flown, licences, income and wages, what each aircraft is doing, and **Pause lessons**. |
| Manage | Hire or let go instructors, buy and sell aircraft, buy upgrades, build another school, demolish. |
| Students | Each student's stage and progress through the course. |

### Running the school

| Item | Price | Notes |
|---|---:|---|
| Instructor | $1,500 to hire, $110 a day | Up to four. Students enrol only while the school has an instructor and an aircraft. |
| Trainer aircraft | $42,000, sells for $21,000 | One for each tie-down. Choose the [Starling A2](../assets/LowWingPiston.md) or [Starling A4](../assets/Cessna172.md). |
| Second classroom | $18,000 | Eight students at once instead of four. |
| Flight simulator | $55,000 | Students train indoors while no aircraft can fly them (night, snow, a busy flight line). |
| Night rating course | $12,000 | Lessons continue after dark on lit runways and taxiways. |

A licence takes 12 lessons:

| Lessons | Stage | Flown as |
|---|---|---|
| 1–2 | Introductory flights | With an instructor: a local flight that leaves the airfield and returns. |
| 3–7 | Circuit training | With an instructor: circuits with two touch-and-goes. |
| 8 | First solo | Alone: one circuit with a touch-and-go, then a full-stop landing. |
| 9–10 | Solo consolidation | Alone: circuits with two touch-and-goes. |
| 11 | Navigation | With an instructor: a local flight. |
| 12 | Checkride | With an instructor: a local flight. The student leaves with a licence. |

The school earns $260 for a lesson with an instructor, $150 for a solo lesson, $90 for a simulator session
and $2,400 for each licence. A first solo and a new licence are announced on the message line. Sandbox
runs the same lessons with finances paused.

Lessons stop, with the reason in the inspector, while lessons are paused, the flight line has no taxiway,
the driveway does not reach the region's roads, it is snowing, or it is dark without the night rating
course. Long grass, snow or wear closing the strip keeps aircraft on the ground through the ordinary
runway rules, so mowing and ploughing have a visible effect on the school.

Sell the school's aircraft before demolishing it. The driveway stays as an ordinary public road.

### What you see

An aircraft is pushed back off its tie-down by hand, taxis to the runway in use and takes off. On a circuit
lesson it turns left at about 110 m, flies crosswind, a downwind leg at 240 m, base and final, touches down,
rolls for a few seconds and takes off again; after its last touch-and-go it lands to a full stop and taxis
back to its own tie-down. The circuit is left-hand and visible throughout. On a local flight the aircraft
climbs away, is out of sight for five to six minutes of game time and comes back onto final.

Circuits share the runway with everyone else. A departure waits at the hold while a circuit is close to
landing; a new arrival is spaced behind a circuit already on its way round; a circuit extends its downwind
leg to follow an aircraft that lands first; and a circuit without landing clearance goes around and flies
the pattern again. At most two of a school's aircraft fly circuits at once.

## Scope

- The flight line takes light general aviation aircraft of size code A. `FlyingSchoolLayout.Code` and
  `Spots`, and the stands' `SizeCode`, are the data a later school for larger aircraft would change.
- Students, instructors and visitors are simulated, not shown. Walk-out animation, cars arriving in the car
  park and parking income are future work.
- Airside security between the car park and the flight line (a staffed gate, fencing, passes) is future
  work for larger airports. Today a low fence with open gates separates the terrace from the tie-downs.
- The circuit is always left-hand. Noise, circuit direction per runway and helicopter training are not
  modelled.
- Flights are not saved. An aircraft in the air or on the runway when the game is saved is restored as away
  and lands again about 90 game seconds after loading; one taxiing keeps its pose and continues.

## Runtime and persistence

| Type | Responsibility |
|---|---|
| `FlyingSchoolLayout` | The site frame and every dimension: tie-down pitch, apron depth, building, car park, bays, driveway mouth, protected footprints and paved polygons. |
| `FlyingSchool` | The saved component: site id, lane, surface, driveway side, stand ids, aircraft, students, instructors, upgrades, accounts, and the lesson simulation. |
| `FlyingSchoolRules` | Prices, fees, the syllabus and timings. |
| `Airport.FlyingSchools` (`AirportFlyingSchools.cs`) | Enumeration, placement validation (`FlyingSchoolProblem`), creation, picking, guarded removal, and stepping schools from `Airport.Simulate`. |
| `Stand.owner` | Marks a stand as part of a facility's flight line. Owned stands are numbered apart (`T1`…), are picked as their school, take only that school's aircraft and call no marshaller. |
| `AirTraffic` / `Flight` | `DepartForCircuits`, `CanFlyCircuits`, `TaxiToStand(aircraft, stand)`, the `Pattern` and `TouchAndGo` phases, and sequencing (see [traffic](traffic.md#circuit-training)). |
| `FlyingSchoolConstruction` | The shared quote and purchase: airport layout, terrain, visitor-centre and road clearance, the driveway plan and its earthwork, funds. Also demolition and `RoadProblem`. |
| `FlyingSchoolAccounts` | Credits fees and debits wages to the shared Career funds. |
| `FlyingSchoolBuilder` | The generated clubhouse, site, tie-down rings and windsock. See the [asset guide](../assets/FlyingSchool.md). |
| `DevFlyingSchool.cs` | The build tool (`DevAirfieldTool`) and the inspector with its three tabs (`DevToolbar`). |

The site frame's origin is on the centreline of the taxilane the flight line opens onto, in the middle of
the line. +Z runs away from that taxilane: flight line, terrace, clubhouse, forecourt, car park. The
driveway leaves the car park's aisle on `DrivewaySide`.

The tie-downs are real stands in `TaxiwayNetwork.stands`, framed with their own front line, so they are
connected by whichever taxiway runs along it and stay put when the graph changes. `AirTraffic.FreeStands`
offers an owned stand only to the aircraft that lives on it (`FlyingSchool.HomeOf`), and offers that
aircraft no other stand. Light aircraft on an owned stand are always pushed back by hand.

Each aircraft is the normal aircraft prefab with a `SchoolTrainerTag`, recreated from portable data (type,
registration, livery, pose). `AirTraffic.Gone` hands a school aircraft to `FlyingSchool.OnTrainerGone`, which
hides it and schedules its return with `AirTraffic.Arrive`; `Finish` calls `OnTrainerParked`, which completes
the lesson once the aircraft is back on its tie-down. An aircraft found off its tie-down with no flight
(after a layout change or a restored save) taxis home with `TaxiToStand`, and is flown round to land again
if no ground route appears within four game minutes.

`Airport.Simulate` steps schools after traffic; decisions are taken once a game second. Schools take no
decisions while `Airport.reconstructing` is set (the session replays an active charter on load). Money waits
in the school (`Unbanked`, fractional wages) until `FlyingSchoolAccounts.Advance` collects it from the live
session, so replay and Sandbox never credit funds.

`AirportSaveData` appends `FlyingSchool` to its component kinds without changing earlier indices.
`FlyingSchool.CaptureState` writes a saved snapshot of each aircraft into separate fields and leaves the
running session untouched, because the game saves while play goes on. The driveway lives only in the saved
`PublicRoadNetwork`; its car-park end is a node whose `terminalEntrance` is `flying-school-<site id>`, which
keeps that end square against the car park and lets the Street tool snap to it and rebuild a removed
driveway out of the aisle.

`ValleyBuiltSurfaces` registers the terrace, forecourt, car park and driveway throat as owned ground; the
tie-downs own theirs as stands. The roof overhang owns none.

Hangars, stands, depots, deicing areas, taxiways, runways, public roads and the visitor centre keep clear of
a school's flight line and block, and a school keeps clear of them and of aircraft aprons. Vehicle routing
treats the clubhouse as an obstacle.

## Verification

Run EditMode filter `FlyingSchoolTests`: the flight line and who may use it, site protection and removal,
a full circuit lesson with two touch-and-goes, the circuit's geometry and roll-out, sequencing behind an
arrival, a local flight that returns, enrolment, fees, wages and Sandbox, the syllabus, a save with a lesson
in the air, the purchase quote with its driveway, and the generated model's bounds.

`AirCity.EditorTools.Airfield.FlyingSchoolReview.Run` builds a standalone airfield, buys a school through
the shared quote, renders day and night views to `Captures/FlyingSchool/` and flies one circuit with a chase
view on each leg (`circuit-track.txt` logs the flight).

`AirCity.EditorTools.Game.FlyingSchoolPlayReview.Run` drives the real game in the Game view: the shared
tool's preview, a mouse purchase at the quoted price, the driveway in the saved road graph, ground
ownership, hiring and buying, a lesson flown and paid, a circuit with a touch-and-go, and a private
save/reload. It needs the editor's Game view (no `-batchmode`); outputs go to `Captures/FlyingSchool/Play/`
and `Logs/FlyingSchoolPlayReview/`. Run it in an isolated copy of the project. It turns off asynchronous
shader compilation while it runs: in the editor a newly generated building is drawn flat cyan until its
shader variant has compiled, which is an editor placeholder and not part of the model.

Verified 2026-11-18:

- All 11 `FlyingSchoolTests` passed, within 205 passing checks across the affected suites (traffic, hangars,
  visitor centre, shared game, saves, player construction, airfield, road and terrain plans, built surfaces,
  maintenance). Report: `Logs/flying-school-final.xml`.
- The full EditMode run passed 1,030 of 1,033 (one skipped). The two failures were the play-mode tests
  `BaggageSandboxTests.SandboxScene_AutomaticHandlingAndToolbarStartInPlayMode` and
  `BaggageTurnaroundTests.ReviewScene_StartsAndStepsWithTheAirportInPlayMode`; both also failed in the earlier
  `Logs/unified-full.xml` and both passed when rerun with the affected suites. Report: `Logs/editmode-school.xml`.
- The batch visual review rendered all views and flew a circuit that ended on its own tie-down.
- The live 1440 × 900 review passed: preview without side effects, a mouse purchase at the exact $68,630
  fixture quote, two-way road access, ground ownership, a $260 lesson credited with wages debited, one
  touch-and-go, and a private save/reload. Result: `Logs/FlyingSchoolPlayReview/review.json`.
