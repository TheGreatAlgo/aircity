# Traffic: taxiing, takeoffs, landings and the tower

Aircraft move on their own: a departure leaves its stand, taxis to the runway in use, holds short, lines up and
takes off; an arrival flies a 3° final, lands, takes the first exit it can and taxis to a free stand.
`airport.Traffic` (`AirTraffic`) owns the flights and acts as the tower: it plans each flight's way and keeps
aircraft apart on the runways, through junctions and along taxiways. Flights are simulated in game seconds from
`Airport.Simulate`, like the depots' teams, so tests run them headless.

The user's choices (2026-10-28):

| Topic | Choice |
|---|---|
| Runway direction | Into the wind: `Airport.windFrom` (degrees, wind from); every runway is used the way within 90° of it, so arrivals and departures share a direction |
| Selecting an aircraft | Shows its route: where a departure will taxi to the runway, where an arrival is headed (its final approach, then its stand). A parked one shows the way it would go if it left now |
| Holding | Aircraft hold for each other: at runway-holding positions, before junctions, for traffic coming the other way, behind other aircraft |
| One-way taxilanes | Allowed (many airports have them): the two directions take turns, an arrival isn't cleared to land into a stand lane a departure still has to come out of, and a departure ready to leave gets its turn after 90 s. A layout where traffic can't get by still jams; more ways round are the cure |
| Small aircraft on big stands | Off by default; a stand can turn small (light, under 5.7 t) aircraft away (`Stand.noSmallAircraft`, "No small" on the stand strip) |
| Sandbox traffic | "Depart" on a selected aircraft, "Land" per type, an "Auto traffic" toggle, wind buttons |

## Where it lives

| Path | Role |
|---|---|
| `Scripts/Runtime/Traffic/AirTraffic.cs` | The flights and the tower: `Depart`, `Arrive`, `PreviewDeparture`, `CanArrive`, runways in use (`FromStart`, `InUse`, `Usable`), clearances (`TryClear`, `TryClearLanding`), one-way sections (`RouteBlocker`, `StartBlocker`), spacing on the ground (`FreeAhead`), wear, re-planning |
| `Traffic/Flight.cs` | One flight: phases (`FlightPhase`), stepping along its route, holds, push-back (tug or by hand), takeoff roll and climb, approach / go-around / landing, lights, engines and flaps |
| `Traffic/TaxiPlanner.cs` | The route search: turn-aware Dijkstra over the taxiway graph; runway entries, back-tracks and U-turns for departures; exits, rollout and stand choice for arrivals; laying the way and noting holds |
| `Traffic/TaxiRoute.cs` | A planned route: `TaxiPath`, `Hold`s, `Traversal`s (sections used, which way), runway, stand, `runwayFree` |
| `Traffic/TaxiPath.cs` | The nose wheel's way (straights, arcs, fillets, U-turns) sampled every 0.5 m, the main gear trailing it, the speed limits; `Pose(d)` |
| `Dev/DevTrafficControls.cs` | Sandbox: Space = depart, route drawing, Land, Auto traffic, wind |
| `Tests/EditMode/TrafficTests.cs` | Routes, holds, wind, a departure and an arrival end to end, holding for each other, the sandbox airport |

## Phases

```
Departure:  Ready ─► PushBack (tug) / HandPush (light aircraft) ─► StartingEngines ─► Taxi ─► LinedUp ─► TakeoffRoll ─► Climb ─► Gone
            Ready ─► StartingEngines (taxi-out stand, or already on a taxiway) ─► Taxi ...
Arrival:    Approach ─► Landing (the roll, until slow) ─► Taxi ─► Parked
            Approach ─► GoAround (no landing clearance 12 s out) ─► out of sight for 90 s ─► Approach (behind the others)
```

- **Push-back:** a tug is asked for at once (`AirportMaintenance.PushBack`, with `TowJob.HoldPush`); it comes,
  picks up the nose wheel and waits (`ReadyToPush`). The push itself is cleared only then (`StartBlocker`), so a
  departure waiting minutes for a tug claims no taxiways. After the push it waits for its tug to clear the first
  300 m of its way (or 120 s), since the tug's way home was planned round where the aircraft stands. Aircraft
  under 5.7 t are pushed back by hand when no tug takes them.
- **Taxiing:** the speed is the lowest of the route's limit at the nose wheel (`TaxiPath.SpeedLimit`: top taxi
  speed, turns at 0.8 m/s² sideways, braking to the stop at the end), braking at 1 m/s² to the next hold it isn't
  cleared past, and braking to stop short of whatever is ahead (`FreeAhead`). It asks for each hold in time to roll
  through without stopping (its stopping distance + 40 m before it).
- **Takeoff:** lined up 3 s, then accelerates to `RotateSpeed` over 60% of the takeoff distance (× the runway's
  length factor now), rotates at 3°/s, lifts off and climbs at `ClimbRate`; gone above 450 m. It gives the runway
  back 60 m up (or past the runway's end). Each takeoff and landing wears the runway (`Runway.AddMovement`), each
  section taxied the taxiway (`TaxiwayNetwork.AddMovement`).
- **Landing:** starts `FinalTime` (110 s) out at `ApproachSpeed`, slowing to `TouchdownSpeed` over the last 600 m;
  asks for landing clearance 60 s out; goes around if it has none 12 s out. Touches down a fifth of the landing
  distance in (60–300 m), brakes to stop in 55% of the landing distance on this runway now (1.2–3 m/s²), turns off at
  the exit the route picked at the speed that turn allows (wide rapid-exit curves at up to 20 m/s for turns of 50° or
  less) and gives the runway back past the exit's holding position (`runwayFree`).

Flight figures come from `AircraftType` (`taxiSpeed`, `rotateSpeed`, `approachSpeed`, `climbRate`, `climbSpeed`,
set in each model's `PERFORMANCE` in `ArtSource/Aircraft/<Model>/model.py`; types imported before they existed
fall back to light-aircraft / jet values).

## Route planning (TaxiPlanner)

A Dijkstra search whose states are (node, the section it came in by), so it knows which way the aircraft goes at
each junction:

- only sections the type may use (`TaxiwayNetwork.TaxiOk`: width, surface, the stands' taxilane limits, not worn out
  or closed by snow or long grass; never roads); aircraft don't route across aprons;
- never back along the section it came by, no turn sharper than 135° (150° onto or off a runway);
- costs: length; turns `30 m × (angle / 90°)²`; 60 m per runway crossing; × 2.5 on sections someone has reserved the
  other way (`Conflict`), so it goes round them where it can.

**Departures** end lined up on a runway in use that the type can take off from now (`Usable`: the takeoff distance
× the surface / weather factor). At a runway node it either turns onto the runway towards the far end, if the runway
left (after the turn and 2.5 wheelbases + 5 m to straighten) is long enough, or back-tracks along the runway, makes
a U-turn (`TaxiPath.Builder.UTurn`, radius 0.6 × half the width, at least 1.3 × the gear's tightest) at the threshold
and lines up there. Each metre of runway left behind costs 0.3, back-tracking 3 per metre + 150 for the U-turn, so a
full-length entry is preferred. Anything heavier than a light aircraft pays 20 000 m for a U-turn on a runway (on
the way out or after landing), so a jet turns round on a runway only when there's no other way in or off. Starts: pushed back either way off a push-back stand (the other way costs 30), out
either way from a taxi-out stand, or on from wherever it stands on a taxiway.

**Arrivals** start at touchdown on each runway in use they can land on, rolling to each runway node ahead (1.5 per
metre of rollout, so they take an early exit rather than hold the runway rolling to the exit nearest the stand), or
stopping, turning round and coming back to one behind. An exit is allowed if the aircraft can slow to the speed its
turn allows by the time it gets there. The goal is the nearest free stand that takes it (`FreeStands`: no aircraft
on it, nobody heading for it, and no departure still leaving it: pushed back in front of it, or not yet taxied clear
of its front, `Flight.ClearOfStand`; `TaxiwayNetwork.CheckStand`, which includes `Stand.noSmallAircraft`; 150 m per
size code bigger than it needs); into a push-back stand nose-in to the stop
bar, into a taxi-out stand along its hairpin lead-in.

**Laying the way:** straight along each section's centreline, each node rounded off with the taxiway's turn radius
(`RunwayRules.TaxiwayTurnRadius`, as the painted curves; never within the holding position of a runway, and the
last curve ends before the turn into the stand). Holds are noted on the way:

| Hold | Where | Cleared when |
|---|---|---|
| Junction | before a node with 3+ taxiway arms, the nose wingspan/2 + half the width + 3 m short of it | nobody else holds the junction (and the ones it can't stop between); nobody coming the other way |
| Runway | the runway-holding position (`RunwayRules.HoldingDistance`, measured along the section) | the runway is free and no arrival is due before it would be done with it |
| Route (arrivals) | just off the runway, if there's room before the next junction | nobody coming the other way on the rest of the route |

## Keeping apart

- **Runways:** one flight at a time holds each runway (`runways`): lining up and taking off until 60 m up, landing
  until off past the holding position, crossing until past the far one. A departure or crossing waits while an
  arrival is due in less time than it needs (its taxi to the line-up / across, the ground roll, plus the 12 s go-around
  decision); arrivals are spaced at least 120 s apart on final.
- **Junctions:** one at a time (`junctions`). Junctions too close to stop between are cleared together, so nobody
  stops inside one.
- **One way at a time:** a flight's committed stretch (up to its first uncleared hold, and the section it's on)
  blocks others from entering the same sections the other way (`RouteBlocker`). Departures commit nothing until they
  start moving, and give way to arrivals due within 150 s that will need their sections the other way
  (`StartBlocker`), for 90 s at most (`StartPatience`).
- **Taking turns:** a flight held up by traffic the other way is queued (`Flight.queuedSince`). One that wants a
  stretch the other way gives way to anyone who has waited longer for it, for 600 s at most (`GiveWay`,
  `YieldLimit`), so a stream one way can't shut out the other.
- **Landing clearance** (`TryClearLanding`): the runway free, the junctions and crossings it can't stop between
  free, nobody on their way who still has to come the other way along the stretch it will stop on (its next hold;
  they would meet nose to nose), and nobody waiting on the way off the runway where its tail would still be on it.
  When the way it planned can't be cleared, it tries the other exits (and stands) every 5 s (`OtherWayOff`); its
  status says why it isn't cleared yet, and so does the go-around.
- **In order:** a hold isn't cleared while another aircraft is on the way it's cleared along (to the line-up for a
  takeoff, through the last junction): nobody overtakes, and one pushed out in front of a cleared aircraft isn't
  driven into (`AircraftBefore`). An aircraft stuck 20 s behind one that's holding gives back the clearances it
  hasn't reached yet (`GiveBack`), in case that one waits for them.
- **On the way:** each looks ahead along its route as far as it needs to stop (+10 m) and stops 3 m short of the
  first place its outline (`AirportMaintenance.Outline`) would touch another aircraft's (inflated 1.5 m when moving,
  0.5 m parked). Parked aircraft beside the stand it's leaving or turning into don't count. Vehicles count only ahead
  of its fuselage, 12 m clear (they're low and pass under wings); a vehicle an aircraft is waiting for goes on,
  keeping only clear of the fuselage (`AirportMaintenance.Avoid`, `AirTraffic.WaitsFor`). Vehicles ignore aircraft in
  the air.
- **Vehicles give way:** `AirTraffic.Tracks` gives the maintenance the ground each taxiing aircraft's fuselage
  (+0.6 m) will sweep as far as it's cleared (200 m at most), and the whole sweep of a push-back (+2 m) while it's
  pushed. Vehicles stop short of a track coming the other way or across, but for the 10 m of one they're already on;
  the tug that pushed an aircraft back is exempt from its track. A moving vehicle in the next 150 m of a flight's
  track, coming the other way, goes first: the flight isn't cleared past its hold (or onto its push) until it has
  passed (`OncomingVehicle`; meanwhile it counts as waiting for that vehicle). Vehicles keep right up to 6 m on
  taxiways (3.6 m on roads), enough to pass a narrow-body's fuselage. They go by a flight that has stopped (at a
  hold, behind another, starting engines: `AirTraffic.Holding`) under its wings, clear of the fuselage, and so do
  vehicles held up by aircraft for 30 s. A vehicle stopped beside an aircraft may re-plan a way that leads away
  from it (`RouteBlocked`). Met head to head (in a corner, typically), the vehicle backs off
  ([maintenance.md](maintenance.md)).
- **The layout changes under it:** a flight whose next sections are removed, or close (work, snow, long grass)
  before it gets to them, stops and re-plans from where it is every 5 s (`Replan`); a departure that hasn't left yet
  checks every section, the one it starts on too, and plans its way again from the stand (`ReplanDeparture`). (A
  Cessna that pushed onto a grass taxiway a mower had just closed waited for the mower, and the mower for it.)

## Sandbox (DevTrafficControls)

| Input | Effect |
|---|---|
| Space / "Depart" | The selected aircraft departs (a tug pushes it back where it needs one) |
| Click an aircraft | Its route is drawn on the ground: blue for departures (paler before it leaves, with the push-back off its stand), green for arrivals; holds as bars across the way (red until cleared, green after); the end boxed (the line-up point or the stand's pad); an arrival's final approach as a line down to touchdown |
| "Land jet" / "Land light" (sidebar) | A new aircraft of that type (a copy of one in the scene when it started, in a random livery and registration) comes in on final |
| "Auto traffic" | An arrival every `arrivalEvery` (150 s, ±30%) of a type with a free stand; the aircraft parked longest is sent off after `turnaround` (300 s), two at most getting ready at once |
| "Wind -30°" / "+30°" | Turns the wind (the caption shows it); the runway strip shows the runway's "in use" end |

Aircraft on a flight set their own lights (beacon from push-back, taxi lights taxiing, landing lights and strobes on
the runway and in the air) and engines; the sandbox light / engine toggles apply to the rest.

## Tests (TrafficTests)

| Test | What it checks |
|---|---|
| `Wind_PicksWhichWayEachRunwayIsUsed` | `FromStart`, line-up end and heading for both winds; a preview reserves nothing |
| `Departure_RouteFollowsTheTaxiwaysAndHoldsShortOfTheRunway` | starts at the push-back's end; on the pavement, within the gear's steering, smooth; junction then runway hold, nose short of the 75 m holding position |
| `Departure_PushedBackTaxisHoldsAndTakesOff` | tug, phases in order, runway wear, holds the runway until well up, gone |
| `Arrival_LandsTakesAnExitAndParksOnAFreeStand` | landing into the wind, 3° glide path, a free stand, parked on the stop bar nose-in |
| `Arrival_NeedsAFreeStand` | refused with the reason |
| `Traffic_HoldsForEachOtherAndNeverTouches` | two departures and an arrival: one at a time on the runway, outlines never touch, someone waited |
| `SandboxAirport_EveryParkedJetHasAWayOutAndArrivalsAWayIn` | both winds: every parked jet and GA stand (after mowing the grass taxiway and strip, which close when long) has a driveable route without turning round on the runway; jet and Cessna arrivals land into the wind |
| `Stand_NoSmallAircraft_TurnsLightAircraftAway` | the flag refuses a Cessna (not a jet); an arrival goes to the one stand that takes it, or is refused |
| `Arrival_DoesNotTakeAStandADepartureIsStillLeaving` | a stand being pushed back off is taken until the departure has taxied clear of its front; then the arrival gets it, and both get by |
| `DeadEndTaxilane_DepartureAndArrivalTakeTurns` | a departure out of and an arrival into the same dead-end taxilane (arriving at once, a minute and 150 s after): both done, nobody touches, at most one go-around |
| `SandboxAirport_BusyHourClearsWithoutGridlock` | four departures and two arrivals at once: all done, nobody touches, at most 2 go-arounds |

## Gotchas

- **Speed limit near the end:** `TaxiPath.SpeedLimit` interpolates between samples. Taking the lower of the two
  returned 0 within the last half metre, and aircraft stopped just short of the line-up point or stop bar forever.
- **Aircraft don't route across aprons.** In the sandbox the dirt taxiway and the GA ramp's grass taxiway end on
  the main apron, so aircraft on the GA ramp reach only the grass strip 06/24 (and nothing while its grass is long).
- **Committing early blocks everyone:** a departure that claimed its route when it asked for a tug made arrivals
  for the same apron lane go around for as long as the tug took to come. Claims start when the aircraft moves.
- **Tugs and the aircraft they pushed:** the tug's way home was planned round where the aircraft stands after the
  push-back; if the aircraft taxis off right away, the two meet at the first corner. The aircraft waits for its tug.
- `TaxiwayNetwork.TaxiOk` is `internal` for the planner.
- **A stand inside a runway's holding area:** a push-back (or taxi-out) can end past the runway-holding position,
  in front of an aircraft already cleared onto the runway. Clearances check the way all the way to the runway, not
  just to the hold, for this (`AircraftBefore`). Better: lay stands out of the holding area.
- **Departures waiting forever for arrivals:** giving way to every arrival on final starved departures out of a busy
  stand lane (they waited 15 minutes and more); hence `StartPatience` and the turn-taking.
- **Vehicles between parked aircraft:** a vehicle's way out from where it stands used to start straight across to
  the nearest nodes, between parked jets' wingtips and tails, where it stuck. From a taxiway it now starts along it;
  the parked-aircraft check (`RouteBlocked`) also allows for how far it keeps right.
- **Soak test:** `SandboxScene_AutoTrafficKeepsMoving` runs the sandbox scene with auto traffic for game hours and
  fails if the ground traffic stops for good, with every flight's and tug's state in the message. The traffic rules
  above came out of it.
