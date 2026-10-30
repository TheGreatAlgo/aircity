# Taxi to-do

What's left in taxiing and ground traffic, parked on 2026-10-29. How it works now is in
[systems/traffic.md](systems/traffic.md) (flights, the tower) and [systems/maintenance.md](systems/maintenance.md)
(vehicles giving way to aircraft).

## Where it stands

- **The sandbox was rebuilt on 2026-10-29** as a small complete airport (see
  [systems/airfield.md](systems/airfield.md), "Rebuild the sandbox airport"): code F taxiways, and the stand
  taxilane joined to the parallel taxiway at both ends, with no single corner shared by tugs and departures. The
  jams and soak figures below are from the old layout: rerun the soak before trusting them.

- All 14 `TrafficTests` pass: push-back, holds, landings, the no-small-aircraft stand flag, a departure leaving a
  stand an arrival wants, and a departure and an arrival in a dead-end taxilane.
- The soak test `TrafficSoakTests.SandboxScene_AutoTrafficKeepsMoving` runs the saved sandbox scene under auto
  traffic for three game hours per seed. It's `[Explicit]`, so normal runs skip it. Run it with
  `-testFilter TrafficSoakTests` (about 10–15 minutes for all six seeds). On a fail it prints every flight's and
  vehicle's state.
- Last run: **3 of 6 seeds clear the three hours**. The other three jam, as below. Even the clean seeds wait a lot:
  270–430 s on average, up to 700–1400 s, and 13–15 go-arounds per three hours.

## Open jams (from the soak)

1. **Tug and departing jet at the apron's east corner** (seeds 1, and earlier most seeds). Where the stand
   taxilane (z = 100) turns north to the parallel taxiway (x ≈ 600), a departing jet stops "behind a tug". Two tugs
   on their way to stands wait for the jet and for each other, and everything behind them queues. The back-off and
   pull-aside (`VehicleTeam.BackOff`) clear most of these but not all.
   - Ideas: tugs ask the tower for that corner like a junction; tugs wait off the taxiway until departures have
     passed; or give the stand row a service road so tugs don't share the corner. That last one is a layout fix:
     see "Layouts" below.
2. **Hand push-back into a taxiing Cessna** (seed 2, GA ramp near (-150, -166)). A Cessna being pushed back by
   hand rolls into one already taxiing out behind it. They touch (the only touch in six runs), then wait for each
   other for good.
   - The push checks for aircraft before it starts (`AirTraffic.PushAircraft`), but the taxiing one doesn't treat
     a push in progress as taken ground: `FreeAhead` sees the pushed aircraft where it is, not where it's going.
     The hand push doesn't stop for anything either.
   - Fix: taxiing aircraft keep off a push's sweep (as vehicles do, `AirTraffic.Tracks`), and the hand push stops
     for an aircraft in its way.
3. **Tug pushing back waits for another tug** (seed 4, stand row). A tug pushing a jet off Stand 7 stops "waiting
   for a tug". It's the large tug coming to the stand next door, which is already on the push's sweep, so it doesn't
   give way to it.
   - Fix: a tug pushing an aircraft has right of way over vehicles coming into the stand row. Or a tug doesn't
     enter a neighbour's push sweep while the push is under way.

## Smaller things

- **Saved `Sandbox.unity`:** a parked jet (Narrowbody ClassicBlue at (14, 31)) overlaps two parked Cessnas at
  (1, 12) and (27, 12) from the start. The jet stands don't have `noSmallAircraft` on either. "Add Sandbox Airport"
  sets it; the soak test sets it itself. Rebuild or fix the scene.
- **Auto traffic outpaces the layout:** an arrival every ~150 s is more than one runway and one stand lane can take,
  so waits grow over the hours. Auto traffic could pace arrivals to how many stands are free and how busy the way in
  is.
- **Go-arounds on the grass strip:** most go-arounds are Cessnas on 06/24 ("Cessna on the runway" / "coming the
  other way"). Departures back-tracking on the strip block arrivals for long.
- **Aircraft don't route across aprons** (see traffic.md gotchas). The GA ramp reaches only the grass strip.
- **Tuning:** the turn-taking and vehicle rules lean on constants chosen by running the soak, not from real
  procedure:
  - `AirTraffic`: `StartPatience` 90 s, `YieldLimit` 600 s, `TrackClearance` 0.6 m
  - `Flight.GiveBackAfter` 20 s
  - `AirportMaintenance`: `UnderWingAfter` 30 s, `StandoffTime` 4 s, `BackOffDistance` 15 m, `BackedOffLimit` 180 s

## Layouts

Some layouts can't take the traffic (a single lane in and out of a busy stand row). Many real airports have
one-way taxilanes, so they stay allowed, and the traffic takes turns on them.
- **Show the player where it jams** instead of hiding it: hint at sections both directions need, or where traffic
  waits longest (e.g. "stand row east end: one way in and out; aircraft wait here"). That's the prompt to add a
  second way round.
- **Push further back:** where the push-back ends in a lane others need, the push could go further back (past the
  next junction, clear of the lane). That's an option for the edge cases rather than a rule.

## Performance

Each simulated second checks every flight against every other flight and vehicle. Vehicles also rebuild every
taxiing aircraft's track each step and test each look-ahead sample against it (`AirportMaintenance.Avoid`). Fine
for the sandbox; profile it before bigger airports.
