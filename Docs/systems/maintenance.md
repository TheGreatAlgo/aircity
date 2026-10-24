# Maintenance: depots, snow ploughs, mowers

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

## Where it lives

| Path | Role |
|---|---|
| `Scripts/Runtime/Vehicles/AirportMaintenance.cs` | The airport's job queue, auto-dispatch, dispatch to the nearest free team, stepping every team together, keeping vehicles clear of aircraft and each other |
| `MaintenanceDepot.cs` | One depot: fleet (`snowPloughs`, `mowers`, `maxPloughTeam` 4, `maxMowerTeam` 3), teams, bays (inside behind the doors, then a row outside), roller doors, `TryAccess` (where it joins the network), spawning vehicles |
| `VehicleTeam.cs` | One team: plans its paths (each vehicle's way out, the shared path to and along the job, each one's way home), simulates every vehicle along them, reports progress to the job, places its vehicles |
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
| `ArtSource/Vehicles/` | Blender pipeline for the models: see [assets/SnowPlough.md](../assets/SnowPlough.md), [Mower.md](../assets/Mower.md), [Depot.md](../assets/Depot.md) |

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
     - aprons ×1.4, straight across between nodes on the same apron;
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

### Keeping clear (AirportMaintenance.Avoid)

- **Footprints:**
  - Aircraft: `AircraftType.bounds` at their pose, plus 2.5 m.
  - Vehicles: `VehicleType.bounds`, plus 0.3 m. Vehicles standing in their bays don't count.
- **Look-ahead:** each vehicle out looks along its own path as far as it needs to stop, plus about 6 m and a quarter
  of its length. It stops short (2.5 m) of the first footprint it would touch, and `Status` says what it's waiting
  for.
- **Teams:** a team ignores its own vehicles on the shared path, since it keeps their spacing itself. In the yard
  (each on its own way out or in) they count.
- **Right of way:** when two vehicles each stop for the other, the one with right of way goes:
  - working beats driving to a job, which beats going home;
  - otherwise the earlier team goes first;
  - within a team, the lower index goes first.
- **Parked aircraft:**
  - Routes avoid parked aircraft when planned.
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
- **Not yet:**
  - Two wide vehicles meeting head-on on a road too narrow to pass: the one with right of way squeezes past.
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
