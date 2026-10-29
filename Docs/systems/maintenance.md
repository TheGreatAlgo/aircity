# Maintenance: depots, snow ploughs, mowers, tugs

An airport can have several `MaintenanceDepot`s. Each owns snow ploughs and mowers, which park inside its garage, one
behind each roller door. Vehicles of a kind work together as a team in echelon. A starting airport may own a single
plough, which needs many passes; more vehicles make the work faster, and more than a team's worth works two jobs at
once. `AirportMaintenance` (`airport.Maintenance`) holds one job queue for the whole airport and gives each job to the
nearest free team of the right kind, from whichever depot.

Teams drive out of their doors onto whatever is in front of the depot (a service road, a taxiway or an apron) and
along the network. Each team works one of these jobs pass by pass:

- a runway;
- a run of taxiway or road sections, worked end to end;
- the mown verges of a runway or a run.

Then it takes the next job, or drives home and backs into its bays. Teams are simulated in game seconds and need no
visuals, so the tests run them headless. `Vehicle` components only show where the team has them. Every vehicle stops
short of aircraft and of other teams' vehicles.

The user's choices (2026-10-23):

- Vehicles live at a depot and drive on the taxiways (and now service roads).
- Team size is variable.
- A rotary snow blower is not wanted for now.
- Mowers also work in variable teams.
- Grass runways are a surface that grows and must be mowed.
- Lights are needed for night operations.
- **Taxiway turns:** teams work long runs and turn round only at the ends. There they U-turn where there's room
  (a runway, an apron), or back into a side taxiway or the runway to turn.
- **Mowing:** mowers also cut the verges, the graded grass of the runway and taxiway strips; the field beyond stays
  rough.
- **Depots:** an airport can have several. Jobs go to the nearest free team. A depot doesn't need a forecourt: its
  vehicles park inside, the doors roll up, and they come out straight onto a service road, a taxiway or an apron.
- **Service roads:** vehicle-only roads with white markings and give-way lines at taxiways. Aircraft never use
  them; vehicles prefer them.
- **Tugs (2026-10-26):** one vehicle that both pushes back and tows, in two sizes: a small one for small airports
  and narrow-bodies, a full-size one for the big aircraft. The user picked **towbarless** tugs (no towbar
  tractor) and, for this round, a working push-back in the sandbox. Towing between stands and dispatching tugs
  automatically need aircraft that taxi, so they come later. See [Tugs and push-back](#tugs-and-push-back).

## Where it lives

| Path | Role |
|---|---|
| `Scripts/Runtime/Vehicles/AirportMaintenance.cs` | The airport's job queue, auto-dispatch, dispatch to the nearest free team, stepping every team together, keeping vehicles clear of aircraft and each other |
| `MaintenanceDepot.cs` | One depot: fleet (`snowPloughs`, `mowers`, `maxPloughTeam` 4, `maxMowerTeam` 3, `smallTugs`, `largeTugs`), teams, bays (inside behind the doors, then a row outside), roller doors, `TryAccess` (where it joins the network), spawning vehicles |
| `VehicleTeam.cs` | One team: plans its paths (each vehicle's way out, the shared path to and along the job, each one's way home), simulates every vehicle along them, reports progress to the job, places its vehicles |
| `VehicleTeam.Tow.cs` | A tug's push-back (a team of one): the way into the stand and under the nose, lift, push, lower, moving the aircraft |
| `TowJob.cs` | `TowJob` (an aircraft to push back, its tug, `TowState`) and `PushBackPath` (the aircraft's way off the stand, with the steering wound on and off) |
| `MaintenanceJob.cs` | One job: a runway, or a run of taxiway / road sections (a polyline), or their verges; strip frame `At(s, x)`, `Lane`, `Locate`; per-section `StripWork` views for runs |
| `VehicleManeuver.cs` | Turning round: a forward Dubins turn, or turns with reversing through candidate poses, checked against the pavement |
| `VehiclePath.cs` | Sampled ground path with a spread (0 single file .. 1 echelon), a speed limit and a gear (forwards / reversing) per point; `LineTo`, `TurnTo` (Dubins, either gear), `Through` (rounded route, concentric lanes), `Keep`, `SetSpread` |
| `Vehicle.cs` | Visual: `Place(position, forward, speed, steer, dt)` rolls / steers wheels (negative speed rolls them back) and spins the broom; `SetState(outside, working, amount)` drives lights and the spray particles |
| `VehicleLights.cs` | Glow mesh per type, plus ground light pools; state bits through the renderer's shader user value |
| `VehicleType.cs` | Imported data: kind, bounds, wheelbase, wheels, spinners, lights, work figures, `Reach`; buildings: `doors`, `interior` |
| `Airfield/StripWork.cs` | The pass pattern and its shader state (shared with the airfield) |
| `Airfield/Pavement.cs` | Where there's pavement (runways with shoulders and blast pads, taxiways, roads, junctions, aprons), from the generated meshes |
| `Shaders/VehicleLightGlow.shader` | Lamp glows, rotating beacons, ground pools |
| `Scripts/Editor/Vehicles/*` | `.vehicle` importer, model postprocessor (doors in every LOD), `VehicleSetup` (prefabs and materials) |
| `ArtSource/Vehicles/` | Blender pipeline for the models: see [assets/SnowPlough.md](../assets/SnowPlough.md), [Mower.md](../assets/Mower.md), [Tugs.md](../assets/Tugs.md), [Depot.md](../assets/Depot.md) (the control towers share it: [ControlTowers.md](../assets/ControlTowers.md)) |

## API

```csharp
using AirCity.Vehicles;
var work = airport.Maintenance;            // the airport's queue and dispatch
work.Plough(runway);  work.Plough(edge);   // a taxiway / road section queues the whole run it's on
work.PloughTaxiways();  work.PloughAll();
work.Mow(grassRunway);  work.Mow(grassEdge);  work.MowVerges(runway);  work.MowVerges(edge);  work.MowAll();
airport.Depots                             // every enabled MaintenanceDepot; airport.Depot = the first
airport.AddDepot(front, outward, copyAssetsFrom);  airport.RemoveDepot(depot);
depot.SetFleet(ploughs: 3, mowers: 1);     // re-forms teams; jobs in progress go back on the queue
depot.SetTugs(small: 2, large: 1);         // towbarless tugs, each its own team
work.PushBack(aircraft, out string problem);  // see "Tugs and push-back"
depot.Plough(runway) ...                   // the same as airport.Maintenance.Plough
runway.StartPloughing() / StartMowing()    // the same, via the airport's maintenance
depot.Teams[i].Status                      // "ploughing runway 09/27, pass 2 of 4 (40 %)", "(waiting for aircraft)"
runway.Work / VergeWork / IsPloughing / IsMowing / WorkProgress
airport.Simulate(gameSeconds)              // weather, grass and verges, then Maintenance.Simulate
```

## How a job runs

1. **Queue.** `Enqueue` refuses a job that overlaps one queued or assigned (the same runway, or a shared section).
   With `autoDispatch` on any depot, `CheckConditions` runs every 30 game seconds and queues:
   - ploughing where `RunwayRules.NeedsPloughing` (1.5 cm paved, 0.6 cm unpaved);
   - mowing grass runways and taxiways where `NeedsMowing` (15 cm), but not while snow lies or it's snowing;
   - mowing verges at 15 cm, once the ground is clear of snow and it isn't snowing.
2. **Runs.** A taxiway or road job is a run (`TaxiwayNetwork.Run`). A run is the chain of sections through the seed:
   - it carries on round bends (a node with two edges);
   - it goes straight on (within 50°) through junctions;
   - it only takes sections of the same width and kind (road or taxiway) that need the work.
3. **Dispatch.** Jobs are taken in rank order:
   1. ploughing runways (longest first);
   2. ploughing taxiways and roads;
   3. mowing grass runways;
   4. mowing grass taxiways;
   5. runway verges;
   6. taxiway verges.

   Each job goes to the nearest free team of its kind, across all depots. Jobs no longer `Needed` are dropped.
4. **`VehicleTeam.Take(job)` plans everything at once:**
   - **Out:** each vehicle drives straight out of its door (`Threshold`), then turns onto the merge pose. The merge
     pose is on the depot's way onto the network (`TryAccess`: the nearest edge point in front of the doors, roads
     counting as nearer), pushed about 30 m along the route out of the yard.
   - **Route:** `TaxiwayNetwork.VehicleRoute`, the cheapest route over the network:
     - roads ×1, taxiways ×1.25, runway segments ×1.6;
     - aprons ×1.4, straight across between nodes on the same apron where the line stays on it;
     - stretches through a parked aircraft are left out (`AirportMaintenance.RouteBlocked`).

     Each stretch keeps right of its centreline where it's wide enough for an oncoming vehicle to pass.
   - **Runways and their verges:** straight passes with an approach and an exit beyond the ends, over the grass, and
     U-turns between them, as before.
   - **Runs:** each pass follows the run's centreline round its curves, concentric with the pavement. On the outer
     lane of a bend it cuts the corner just enough for the plough's front corner to stay on the taxiway. The
     echelon fans out inside the run (18 m ramps). Ploughs stop with the blade at the far edge of the pavement
     (`Allow`). Between passes, `TurnRound` tries these until the footprint stays on the pavement:
     - ending the pass 0 to 26 m short;
     - starting the next pass 0 to 16 m in;
     - reaching that start forwards or backwards.

     Each turn is planned by `VehicleManeuver`.
   - **Home:** a route to the depot's access, then each vehicle's own way into its bay. It drives past the door,
     then backs in (`Threshold`, then straight back to the bay).
5. **Simulate.** `AirportMaintenance.Simulate` steps every depot's teams together in 0.5 s steps:
   - **Avoid.** Speed caps from what's ahead of each vehicle.
   - **Step.** Every vehicle moves along its timeline: its own way out (negative distances), the shared path, then
     its own way in. A vehicle accelerates at 1.2 m/s² and brakes at 2.2 m/s² under the speed limits, and keeps
     these spacings:
     - **Following:** a gap behind the one ahead, also leaving the depot (their ways out all lead to the merge).
       Each vehicle has its own way home, so no gap is kept there.
     - **Doors:** a vehicle waits in its bay until its door is fully up, and before backing through it.
     - **Reversing turns:** one vehicle at a time. The next waits a vehicle's length short of the turn until the one
       ahead is clear of it.
     - **Yard:** coming home, each waits 40 m out until the one before is in its bay.
     - **Formation:** the lead slows down until a follower that fell behind is back a gap behind it. It slows to
       60 % of transit speed far from the job and to a crawl near it.
     - **In formation**, followers are locked exactly a gap behind, and the lead keeps to every follower's speed
       limit, so the echelon holds through corners.
   - **Doors.** Each door rolls up (5 s) while a vehicle wants it, and down when none does.

   `UpdateWork` turns the lead's position plus `type.Reach` into `job.Advance(pass, front)`. Reach is the blade
   ahead of the rear axle, or the back of the mower decks. A run's sections each show it in their own frame, and
   close as the team reaches them.
6. **Done.** When the last vehicle clears the last pass, `job.Release(true)`:
   - ploughing sets the snow to 0;
   - mowing sets the grass, or the verges, to `CutGrass` and keeps the stripes.

   The team then takes the next job or goes home. `Abandon()` hands the job back unfinished, for example when the
   fleet changes (`Requeue`).

## Tugs and push-back

`TugSmall` (10–100 t: turboprops, regional jets, A320 / 737) and `TugLarge` (40–600 t, up to an A380) are
towbarless tugs, `VehicleKind.Tug`. Both are a U from above: the aircraft's nose wheel goes between the legs, onto a
cradle that grips it and lifts it (`liftHeight`), so tug and aircraft move as one. The cradle's nose-wheel point is
**on the tug's rear axle line** (`VehicleType.cradle` = origin), so a tug's pose *is* the nose gear's pose. Which
aircraft a tug takes is by mass: `VehicleType.CanTow(type)` checks `minTowMass` ≤ `maxTakeoffMass` ≤ `maxTowMass`.
Nothing is a per-model number in code. Light aircraft (the Cessna) are below both tugs: they're pushed by hand.

Tugs live in the depot like the other vehicles (`depot.SetTugs(small, large)`); each is a `VehicleTeam` of one.

```csharp
var job = airport.Maintenance.PushBack(aircraft, out string problem);           // null + problem if it can't
var job = airport.Maintenance.PushBack(aircraft, out problem, otherWay: true);  // face the other way along the taxiway
airport.Maintenance.TowOf(aircraft);   // the push-back waiting or under way, or null
airport.Maintenance.Tows;              // all of them; job.State / job.Status / job.Tug
job.HoldPush = true;                   // the tug picks up the nose wheel and waits (job.ReadyToPush) until cleared
```

A departing flight sets `HoldPush`: the tug comes at once, and the flight clears the push when its way is free
([traffic.md](traffic.md)).

**Refusals** (`problem`): not on a stand; a taxi-out stand; not parked nose-in; no room to turn onto the taxiway;
another aircraft (or where a push-back under way will leave one) in the way; light aircraft; no tug big enough;
no tugs at all.

**The path (`PushBackPath.Plan`).**

1. Straight back along the stand's centreline, then round onto the taxiway's centreline, facing the way the
   aircraft will taxi.
2. **Which way:** the way whose end of the stand's section leads on to a runway, by route length (an
   aircraft route that doesn't go back through the section). Ties and dead ends fall back to the smaller turn.
   `otherWay` takes the second choice.
3. **The turn:** the curvature ramps up over 0.8 of a wheelbase of main-gear travel, holds, and ramps down. So the
   nose-wheel steering (and the tug's heading) never jumps. Radius from 1.4 wheelbases down to what
   `maxSteeringAngle` (capped at 65°) allows, the widest that fits between the stand and the taxiway.
4. It's planned for the **main gear** (the aircraft's origin). The nose gear's way, which the tug drives
   backwards, follows from it. `Sample(d)` gives the aircraft's pose and nose-wheel direction for the tug's
   distance `d` along the nose path.
5. **Clearance** against other aircraft is checked against their **outlines** (`AirportMaintenance.Outline`:
   fuselage, wings, tailplane boxes from the type's size), not bounding rectangles. Rectangles round two
   A320s' wings overlap where the real aircraft pass on a taxilane.

**A push-back (`VehicleTeam.TakeTow`, `TowState`).**

1. **Waiting:** no free tug that can move it. `DispatchTows` picks the smallest that can, then the nearest.
2. **Coming:** from the depot along the network to the stand's section, then into the stand **along its side
   line** (the side clearer of other aircraft' footprints, then the nearer), a quarter turn across the front of
   the stand, on past the nose, and a reversing quarter turn into line behind the nose wheel. Then straight
   back under the nose at a crawl (`HitchSpeed` 0.6 m/s). **By road:** if a service road runs across the stand's
   head (`TaxiwayNetwork.RoadAccess`, side 0), the tug routes to that road instead, drives along it in its lane to
   a turn's radius past the stand's centreline (from whichever way it can, preferring the way it arrives), backs a
   quarter turn round into the stand, facing out, and backs straight on to the nose wheel (`RoadIn`, `BackIn`,
   `BackInFromRoad`). It never uses the taxiway to get there.
3. **Lifting** (8 s): the cradle (`Vehicle.SetLift`) and the aircraft's nose rise together.
4. **Pushing:** the tug backs along the nose path at `workSpeed` (1.3 / 1.1 m/s). Each step it puts the aircraft
   where the nose wheel is (`PlaceAircraft`). It also turns the aircraft's wheels and steers its nose gear the way
   the tug faces, through `AircraftGroundMotion.Drive`.
5. **Lowering** (6 s), then **Done:** the tug drives on out from under the nose and goes home like any vehicle.
   The aircraft is left on the taxiway centreline, ready to taxi (aircraft don't taxi yet).

**Edge cases.**

- An aircraft deleted mid-push: the tug lets go and goes home (`Recall`).
- A fleet change: a push-back that hasn't started waits for another tug. One already moving is left where it is,
  nose down (`DropTow`).
- `RouteBlocked` counts where push-backs under way will leave their aircraft, so other vehicles plan round them.

### Keeping clear (AirportMaintenance.Avoid)

- **Footprints:**
  - Aircraft: `AircraftType.bounds` at their pose, plus 2.5 m.
  - Vehicles: `VehicleType.bounds`, plus 0.3 m. Vehicles standing in their bays don't count.
- **Look-ahead:** each vehicle out looks along its own path as far as it needs to stop, plus about 6 m and a quarter
  of its length. It stops short (2.5 m) of the first footprint it would touch, and `Status` says what it's waiting
  for.
- **Teams:** a team ignores its own vehicles on the shared path, since it keeps their spacing itself. In the yard
  (each on its own way out or in) they count.
- **Turns with reversing claim their ground:** within 40 m of one (a `zone`), a vehicle's footprints through the
  whole manoeuvre (grown 1.5 m) are obstacles for other teams. Without it, a vehicle following another found the
  one ahead backing round into it.
- **Tugs** keep only 0.3 m from aircraft, not 2.5 m, and from the aircraft's outline (fuselage, wings, tailplane:
  `Outline`), not its bounding rectangle: they work under them and pass close by noses and tails. They ignore the aircraft they're coming
  for or holding. Inside the stand they ignore parked aircraft too, since the way round was planned clear of them.
  While pushing, the towed aircraft's footprint also stops at other vehicles.
- **Taxiing aircraft** (flights, [traffic.md](traffic.md)) stop for vehicles 12 m ahead of their fuselage. A
  vehicle an aircraft has stopped for (`AirTraffic.WaitsFor`) goes on, keeping clear of its fuselage only, so the
  two never wait for each other. Aircraft in the air (over 3 m up) are not obstacles.
- **Giving way to aircraft:** vehicles stop short of the ground a taxiing aircraft's fuselage will sweep as far as
  it's cleared, coming the other way or across (`AirTraffic.Tracks`), but for the 10 m of it they're already on, and
  of an aircraft's whole push-back while it's pushed (the pushing tug excepted). A tug pushing an aircraft back has
  the right of way over ground other vehicles have claimed for a turn. A flight that has stopped on its way
  (`AirTraffic.Holding`) is passed under the wings, clear of the fuselage; so is any aircraft once a vehicle has been
  held up by aircraft for 30 s (`UnderWingAfter`). `RouteBlocked` lets a vehicle stopped inside an aircraft's
  clearance re-plan a way that leads away from it.
- **Backing off:** a vehicle that meets a taxiing aircraft head to head (it's in the aircraft's way, the aircraft
  waits for it, and its own way runs into the fuselage) goes 15 m straight back the way it came after 4 s
  (`VehicleTeam.BackOff`: backing up, or driving forward out of a turn it was backing round; a lone vehicle on its
  way to an aircraft or home, not in a stand or on a strip), again if the aircraft still waits for it, and plans its
  way on 3 s after that aircraft has gone by (its tail behind the vehicle), gone 80 m away or stopped taxiing, or
  after 180 s (`EndBackOff`). Typically in a corner, where the aircraft cuts the inside the vehicle keeps right on.
  Going on as soon as the aircraft stopped waiting just met it again a few metres on. Vehicles ignore the track of an aircraft that waits for
  them (they go on, out of its way), not everyone's.
- **Keeping right:** routes are offset right of the centreline by up to 6 m on taxiways and runways
  (`VehicleTeam.MaxKeepRight`), enough to pass a narrow-body's fuselage, and 3.6 m on roads.
- **Right of way:** when two vehicles each stop for the other, the one with right of way goes:
  - one turning round with reversing (it claimed the ground) goes first;
  - working beats driving to a job, which beats going home;
  - otherwise the one in front goes (the other is behind it, along the way it is moving: it may be backing),
    else the earlier team;
  - within a team, the lower index goes first.
- **Parked aircraft:**
  - Routes avoid parked aircraft when planned, allowing for the keep-right offset. From a taxiway a route starts
    along it, never straight across to the nearest nodes (that ran between parked jets' wingtips and tails); vehicles
    go between parked aircraft only into the stand they work on.
  - A team stood behind a parked aircraft for 20 s re-plans, on the way to a job or home, never on the strip.
  - A moving aircraft (its `AircraftGroundMotion.groundSpeed` isn't 0) is just waited for: it doesn't give way.

### Turning round (VehicleManeuver)

`Append(path, goal, endReverse, via, type, pavement, ...)` tries three kinds of manoeuvre:

- a single forward Dubins turn;
- two legs: back into a via pose and drive out, or drive in and back to the goal;
- three legs: drive in, back out, drive on.

Via poses come from `AroundNodes`: points 10, 18 and 28 m along every edge and runway direction at the nearest
nodes, facing either way, plus poses along the road in front of a depot. Candidates are sorted by length, a cusp
counting as 30 m. With a `Pavement`, the first whose footprint stays on it wins; otherwise the one that strays
least. Up to 400 are checked: six footprint points, 0.8 m in from the corners, every 2 m. Mowers pass no pavement,
since they live on the grass.

### The pass pattern (StripWork)

- **Strip frame:** s runs along from the runway's start threshold, or from the run's first node; x is across,
  positive to the right. For verges, x is measured outwards from the pavement edge, as the verge meshes do. The team
  works from `-margin` to `length + margin`:
  - paved runways: the blast pad + 5 m;
  - unpaved runways: 10 m;
  - runs: 4 m;
  - verges: their overrun (60 m paved, 30 m unpaved).
- **Bands:** `BandWidth = laneWidth × vehicles`, and `Passes = 2 × ceil(halfWidth / BandWidth)`. Bands lie either
  side of the centreline, from the middle out.
- **Centred:** a team at least as wide as the strip does it in one pass, its band centred on the centreline: three
  ploughs clear a 15 m taxiway without turning. The shader is told by adding 100 to the lane width.
- **Lanes kept on the strip:** where a band would hang over the edge, the vehicles stay on it (`LaneX`). For a
  centred pass wider than the strip, the echelon fans out less.
- **Ploughs** throw snow to their left. So every pass left of the centreline runs up the strip and every pass right
  of it runs back down: pass 2b + side, where side 0 = left.
- **Mowers** swap the side order every second pair of passes, so neighbouring bands alternate direction. That gives
  the stripes.
- **Echelon:** vehicle i runs one lane to the left of vehicle i−1 and `EchelonGapPerLane` (3.2) lane widths
  behind it: 16 m for 5 m plough lanes, 14 m for mowers.
- **Shader state** is `(pass or -1, lead's s, ±band width, ±lane width (+100 centred))`:
  - a negative band width means reversed;
  - a negative lane width means mowing;
  - an idle mown strip keeps (−1, 0, band, −lane) for its stripes.

  The C# twin is `StripWork.Worked`.

**Timings (tests):**

| Job | Team | Time |
|---|---|---|
| 2.5 km runway, 60 m with shoulders | 4 ploughs (4 passes) | about 13 min working, 23 min including the drive out |
| same runway | 1 plough (12 passes) | about 56 min |
| 700 × 30 m grass strip | 1 mower | about 26 min |
| 3 taxiway runs (one round two bends) and a road | 1 plough | about 25 min |
| 800 m runway's verges (code 2, 40 m) and a taxiway's | 1 mower | about 61 min |

### Depot layout

The depot's transform is the door front, +Z out of the doors, in Airport space. The building prefab is spawned as
a child (DontSave); its `Door_k` children are scaled down in y to roll the doors up.

| Point | Where (depot space: x across, z out of the doors) |
|---|---|
| Bay k < doors | behind door k: x = the door's centre, rear axle at `interior.min.z + 0.8 − bounds.min.z` (each type's tail 0.8 m off the back wall) |
| Bay k ≥ doors | outside in a row along the front, 8.5 m apart past the last door, tail 1.5 m off the wall |
| Threshold k | the rear axle just clear of door k (tail 1 m out); outside bays: the bay |
| Access | `TryAccess`: the nearest point of an edge within 150 m of a point 8 m out (roads count 20 % nearer), else the nearest node within 300 m |

Teams take consecutive bays in fleet order: ploughs first, then mowers. Without door data in the building's type,
the depot falls back to the model's four doors and a 23.5 m hall. In the sandbox the depot is at (−215, 50), facing
east, on a 40 × 56 m pad. Service roads go east to the main apron and north across the dirt taxiway to the parallel
taxiway.

### Visuals

- **Placement:** `MaintenanceDepot.LateUpdate` → `UpdateVehicles(dt)` spawns missing vehicles from the prefabs
  (DontSave), then `team.Place`. Each vehicle stands where its timeline says:
  - facing along the path, or against it while reversing. The path is sampled every 1.5 m, so `Sample` turns the
    facing from one chord's middle to the next instead of using the chord's own direction, which would make it turn
    in 7–12° clicks on a 7–12 m turn. Through a cusp the facing stays smooth; a kink of more than 90° is not smoothed;
  - steered from the path's curvature (taken from those smoothed headings 1 m either side);
  - its wheels roll backwards while it reverses.

  The doors are set from their open amount.
- **Lights** (`VehicleLights.StateBits`):

  | Bit | Group |
  |---|---|
  | 1 | head / tail lamps, at night |
  | 2 | beacon, whenever the vehicle is out of its bay |
  | 4 | work lights, at night while working |
  | 8 | markers |
  | 16 | flood lights (depot) |
  | bits 8+ | the beacon phase, a hash of the vehicle's name |

  Pools are flat ground quads (type 10 + lamp type) at y = 0.045.
- **Spray:** ParticleSystems created at runtime, using the shared `Vehicles/Materials/Spray.mat` (URP Particles
  Unlit, soft dot). Snow comes off the plough blade's left end and the broom; clippings come off the mower decks.
  Particles are dimmed by night.

## Common tasks

**Change the fleet or team size.** Use `SetFleet`, or `maxPloughTeam` / `maxMowerTeam`. The dev keys are `, / .`,
with Shift for mowers, on the selected depot.

**Add a depot.** `airport.AddDepot(front, outward, existingDepot)`, or in the sandbox use build mode **Depot**: drag
from the door front the way the doors face. Connect it with a service road (build mode **Road**; a click in front of
the doors starts the road there), a taxiway or an apron.

**Tune speeds, lane width or turn radius.** Edit the model's `WORK` in ArtSource and regenerate, or edit the
imported `VehicleType` (the importer overwrites it on the next regenerate).

**Add a vehicle kind** (for example a rotary snow blower):

1. Add a `VehicleKind` value.
2. Build a model in `ArtSource/Vehicles/<Name>`.
3. Add `VehicleSetup` prefab creation.
4. Add a depot field for its type and prefab, plus fleet count.
5. Decide how it joins a team. A blower would trail the last plough on the outer side: a team member with its own
   lateral and trailing offset in `VehicleTeam.Where`.

**Change the pass pattern.** Edit `StripWork` (`Side`, `PatternDirection`, `LeadOffset`, `Centred`, `Worked`) and
`StripWorked` in `AirfieldCommon.hlsl` together, then run the `StripWork_*` tests.

## Invariants and gotchas

- **Game time:** everything runs in game seconds (`Airport.timeScale`, default 1 = real speed). Don't move vehicles
  in real time separately, or the cleared area and the snowfall drift apart.
- **One step for everyone:** teams only move inside `AirportMaintenance.Simulate`, which computes the avoidance caps
  first. Don't step a team on its own.
- **Closures:** teams ignore closures when routing (`VehicleRoute`); aircraft routing respects them.
- **Jobs:** a strip has at most one `work` (and one `vergeWork`); `Enqueue` refuses overlapping jobs.
  `MaintenanceJob.Fresh()` re-queues a job without its old `StripWork`.
- **Generated objects:** vehicles, the building and particles are DontSave and NotEditable; disabling a depot
  destroys them. A depot's serialized state is just the fleet counts and asset references.
- **Planning cost:** a turn search builds up to 400 trial paths. Runs and depots plan once per job; it's cheap for
  the sandbox but would want caching for big networks.
- **Tug cradle on the rear axle:** gameplay puts the tug's origin on the aircraft's nose-gear point. A new tug model
  must keep its cradle's wheel position on the rear axle line (`TOW["cradle"]` = origin).
- **Not yet:**
  - Two wide vehicles meeting head-on on a road too narrow to pass: the one with right of way squeezes past.
  - Towing between stands or to a hangar, and dispatching push-backs automatically (aircraft don't taxi yet).
  - Two push-backs whose aircraft end up nose to nose on the same taxilane can box in one tug: aircraft don't
    taxi away yet, so it waits for good.
  - Runway-crossing clearances.
  - Turning room on runway ends: runway passes still U-turn on the grass beyond the end, as before.
- **Bends and junctions:** taxiway verges stop short of junctions and bends so neighbouring bands never overlap.
  The corners there stay unmown.

## Tests

`AirfieldTests` and `MaintenanceTests`:

| Test | What it checks |
|---|---|
| `Snow_BuildsUpClosesAndIsPloughed` | a full plough cycle, then the taxiways, then going home |
| `Ploughing_OnePloughTakesLongerThanAnEchelon` | team size changes the time |
| `StripWork_PassesFromTheCentreOutInEchelon`, `StripWork_OneCentredPass...` | the pattern, mowing alternation, reversal, the centred pass |
| `GrassStrip_GrowsAndIsMowed` | growth, closure and a full mowing cycle |
| `Taxiways_PloughedInRunsTurningRoundOnThePavement` | runs (round bends, through junctions), turns with reversing at a T end, the footprint on the pavement through every pass and turn |
| `Depot_VehiclesLiveInsideAndTheDoorsRollUp` | bays inside, doors up when driving through, shut afterwards, spacing |
| `Depots_JobsGoToTheNearestFreeTeam` | two depots, two runways, each to its own |
| `Avoidance_RoutesRoundParkedAircraftAndWaitsForMovingOnes` | a parked jet routed round, a taxiing one waited for |
| `Verges_GrowAndAreMownInStripes` | verge growth, auto-dispatch, cut with stripes, the runway stays open |
| `Roads_CarryVehiclesNotAircraft` | routing, crossing splits, white markings |
| `Vehicles_ImportWithLightsWheelsAndWork` | types and prefabs (in `AirfieldTests`) |

`TugTests`:

| Test | What it checks |
|---|---|
| `Tugs_TwoSizesWithACradleOnTheRearAxle` | sizes, masses, cradle on the rear axle, lift part, lights, prefabs |
| `PushBack_StraightBackThenRoundOntoTheTaxiwayFacingTheRunway` | 90° and 60° stands: on the centreline facing the runway, smooth steering within the limit, `otherWay` |
| `PushBack_TugComesFromTheDepotPushesAndGoesHome` | the whole cycle: off the neighbours and its own jet's wings, nose wheel held in the cradle, aircraft on the taxiway, tug parked |
| `PushBack_TwoAtOnceGoToTheSmallTugThenTheLargeOne` | dispatch by size, two tugs out at once never touching |
| `PushBack_SaysWhyNot` | light aircraft, taxi-out stand, off a stand, aircraft in the way, no tugs |
| `PushBack_TheAircraftGoingAwaySendsTheTugHome` | deleted mid-push |
