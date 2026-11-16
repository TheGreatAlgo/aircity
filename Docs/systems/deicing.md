# Aircraft deicing

Deicing is a pad, truck fleet and a separately placed supply work area. The pad itself never stores fluid.
Build a pad, a supply station and truck parking from the snowflake catalogue. Connect the service road to
an explicit outside connection, then use **Manage airport** to assign supply and order one or two trucks
per pad. Place storage on a paved apron and connect roads to its marked FILL and UNLOAD lane ends. Order fluid,
and the empty trucks drive over to fill before returning to their parking. A supply area may serve several pads. Its optional
recycling upgrade automatically collects runoff from all those pads through hidden underground connections.

## Construction and management

The snowflake toolbar button opens one **Deicing** catalogue with **Pads / Buildings / Objects** tabs. The
same catalogue contains the pad, fluid-storage work area, truck parking and outside service access. Recycling
is purchased for an existing supply area rather than placed as an independent source of fluid. Placement uses
the shared model preview and rotation controls; the construction footprint remains visible for clearance.

**Manage deicing** opens an airport-wide workspace, also reached from a selected pad, supply area or parking
bay. It switches the world tool to Select and closes the gate designer. Closing it or pressing Escape returns
to the world. Its area rail filters shared supplies without changing their assignments:

- **Overview** shows pads, trucks, fluid and the recorded operating account.
- **Fleet & pads** shows available truck parking, pad assignments, crew shifts and each truck's status and
  onboard fluid. Supply assignment opens explicit area choices. Ordering a truck reserves a chosen free bay;
  add another parking bay before expanding the fleet. A truck requires an assigned supply area and drives in
  through outside service access.
- **Supply** manages each area's fluid orders, tanker status, tank level and recycling purchase or recovery
  totals. Areas can serve multiple pads. Outside access and a connected service road are required for orders.

Selection inspectors are compact summaries with a fluid or treatment meter, a short status and a clear
management action. Detailed fleet, budget and recycling controls live in the central workspace instead of
nested pop-out panels. They use the shared rounded paper cards, ink text, teal actions and amber selection
styles from `DevGui`, alongside the rest of the construction HUD. Successful orders and validation failures
appear at the top of the management view.

## Storage and deliveries

`Airport.deicingWorkAreas` stores independent `DeicingWorkArea` objects. Each has a position/heading, a
30,000-litre tank, finite fluid stock and one pending delivery. Its 20 × 24 m footprint contains a central
tank, one temporary FILL lane for deicing trucks and one UNLOAD lane for the delivery tanker. Both vehicle
bodies fit inside this visible paved footprint. Each lane has a road connection at either end; road drawing
snaps onto these four ports, and station placement can snap its ports onto an existing service road. Ctrl/Cmd
bypasses placement snapping. `RoadPorts`, `RefillPose`, `UnloadPose` and `EquipmentOutline` share geometry
between placement, route checks and paint. The compact recycler sits across the central island behind the
tank. Roads cannot cross that equipment island.

Supply stations do not create truck parking. FILL and UNLOAD are dashed service positions with directional
arrows; truck parking is a separate player-placed object. Several stations can be placed and assigned independently.

Placement requires a paved apron beneath the whole work area and rejects aircraft pads, taxiways, runways,
other supply areas and terminal buildings. The tank cannot overlap a service road. Players can move or rotate
an idle work area; active refill journeys and outstanding deliveries guard removal. Moving preserves its stock.
An original scene's pad-attached tank and stock migrate once into an independent work area without granting
extra fluid. New construction always uses the standalone placement rules.

Orders require an open service road beside the entrance, a service-road graph route to an explicit outside
connection to an UNLOAD lane end. Clicking Order performs local paving/access/volume/budget checks, rather
than running the full vehicle route search synchronously. Rejected orders spend nothing. A 90-game-second supplier preparation period
precedes actual tanker travel; expiry never adds stock directly. The rigid tanker carries at most 20,000 litres,
so larger orders use successive loads. Its source is a marked outside service-road endpoint; the supplier's off-airport
journey remains abstract.

`AirportDeicingDeliveries` uses the service-road graph and short docking manoeuvres, including reverse
docking from a road along the front of the station. It simulates approach, a two-second hose connection, unloading at 200 litres per game
second, two-second hose retraction and the return to the road gateway. Stock moves only at the berth with a
connected hose. `area.deliveryLitres` always means the remaining reserved volume, including cargo in transit;
it is not added to cargo again when accounting for stock. Closed access, missing pavement, obstructions or a
full tank pause the appropriate phase with cargo retained. Moving/removing an area is blocked while its tanker
is present; removal is also blocked while a paid order remains outstanding.

Tanker transit and new refill journeys yield to each other so vehicles do not meet head-on on a narrow common
service road. Existing refill journeys finish before dispatch/exit, but an empty deicer waiting at the tank
does not block its supplier. Live tanker body boxes participate in aircraft, maintenance, baggage and deicer
avoidance. The hose pays out and retracts from the simulated connection progress; rendering never transfers fluid.

## Trucks and onboard refilling

`Airport.deicingParkingBays` stores player-placed 4.5 × 13 m bays ($1,000 each). Whole-footprint paving,
clear driving lanes and non-overlap are validated in both directions with supply areas. The fleet panel
reserves a chosen free bay when a $180,000 truck is ordered. After 30 game seconds of preparation, that empty
truck enters through marked outside access and physically drives to the bay. Missing access or traffic holds
it in place; paid pending orders retain their bay and remaining preparation through serialization. Pending
trucks incur no crew/upkeep costs and take no share of a treatment already in progress. Pads with pending
truck orders cannot be demolished. Existing owned trucks keep their original home poses without generating
parking capacity. Old automatic legacy bays are removed, preserving stock, motion and recorded spending;
a bay reserved by a paid inbound order is retained so its destination is not lost. The management panel lets
players assign an idle truck to a placed bay; that truck drives to it rather than teleporting.

`Airport.deicingOutsideAccess` explicitly marks service-road endpoints. A nearby isolated road or an apron
shortcut cannot qualify as an outside connection: the service graph must reach a marked open gate. Sandbox
portals represent authored map access. In a career airport the gate must additionally meet a ground public
road at the same elevation, and that separate road graph must reach the valley boundary.

The authored Sandbox now supplies a paved southern entrance at airport-local (100, -700), connected to its
existing service-road graph. `SandboxOutsideAccess` installs the missing spur on Play for older Sandbox
scenes without regenerating or saving the scene, and fresh sandbox setup includes it. It preserves player
gates and closures and refuses occupied corridors. A marked portal apron reads **OUTSIDE / SUPPLIES**.
**Deicing > Objects > Outside connection > Locate outside road** moves the camera to the entrance.

`Airport.deicingFacilities` assigns trucks and a work-area ID to each pad. Every new truck starts empty and
holds at most 1,200 litres onboard. `Airport.Deicing.TrucksFor` exposes its fluid, phase, position, facing,
speed, steering and reason for waiting. `DeicingDrive` moves a truck along finite-radius `VehiclePath` geometry
with acceleration and braking, rather than interpolating its transform sideways or rotating in place.

An empty truck plans a connected paved route to its assigned refill lane. Idle trucks below 20% proactively
top up when their work area has stock; active treatments use their remaining fluid before refilling.
Stock transfers at 120 litres per
game second only after the truck physically arrives. It returns to the aircraft or its pad after filling; if the
tank empties partway, it can return with the available partial load. With no assignment, an unreachable supply
area or an empty tank, it waits with an explicit status. Removing a paved connection never grants fluid.

A supply-area lease admits one complete truck refill round trip at a time, including the return to its pad or
working position. This keeps outbound and inbound trucks from meeting head-on on a narrow common service
road. Pads can share a work area without each inventing an independent reservation. Trucks also check live
vehicle/aircraft/tank footprints, stop for an obstruction and replan; aircraft spacing includes their boxes.

## Aircraft treatment and pacing

`Airport.Deicing` runs headlessly in game seconds alongside traffic; `DeicingRig` only presents its state.
Aircraft stop with their nose wheel on the marked bar facing the bay's direction. Moving, airborne, misaligned
or oversized aircraft are never sprayed. The pair covers opposite wings; a single truck covers both wings.

`AircraftType.deicingWing` exports leading, middle and trailing upper-skin samples along each model's wing.
`DeicingWingCoverage` makes overlapping span bands and full-chord sweeps from those samples. Each target has
a safe truck working pose. Booms and level spray cabins follow the actual target, and reach checks gate fluid
consumption. Nozzle aim moves continuously across a chord rather than jumping between isolated points.
The front pass keeps a consistent inboard-facing truck orientation; a target requiring an opposite heading
is covered in the rear pass instead of sending the truck through a full turn between adjacent chord samples.

Treatment volume is a gameplay estimate from aircraft bounds (80-litre minimum), with a size-dependent
12–22-second active-spraying budget for a stocked pair. Driving into position, traversing the wing and returning
clear take additional game time; they are physical journeys, not 20-second setup/clearing timers. A single truck
has roughly twice the spray work. Full-service regressions cover every aircraft type, including all wing targets,
fluid conservation and safe departure clearance. The narrowbody is bounded to 90 game seconds and the largest
double-deck aircraft to 120; these timings exclude initial fluid delivery and refill travel.
Measured stocked-pair cycles are approximately 34 game seconds for the narrowbody, 40 for the supersonic jet
and 119 for the double-deck aircraft, including approach and departure clearance.

Only actual spraying consumes onboard fluid. If a truck runs dry in a treatment, it automatically drives to
refill and resumes the same target and dose progress. Remote stock, onboard stock and applied fluid are
conserved. Traffic waits until both booms are folded and the truck bodies and their clearance journeys are
outside the aircraft's departure sweep (or behind its tail). Home journeys are planned afterwards and continue
under normal live avoidance while the aircraft leaves. A completed aircraft is not treated again while
it remains on the pad. Pausing simulation freezes trucks, booms, spray and stock use.

## Runoff collection and recycling

**Buy upgrade** on a supply area installs collection drains on its assigned pads and a compact filtration skid
behind the storage tank. The purchase costs $45,000 and creates no fluid. No connecting pipe network is drawn.
New spraying contributes up to 60% of its actual applied volume to that area's 15,000-litre recovery buffer.
The first collected fluid starts a 30-game-second processing delay, after which up to 60 litres per game second
returns automatically to the shared storage tank. The remaining applied volume and any excess beyond buffer
capacity are unavailable for reuse. These are gameplay recovery figures, not a chemistry or concentration model.

Full-wing dose and coverage do not shrink: recycling reduces net fresh-fluid purchases by recovering part of the
used fluid. Recovery waits when storage is full and respects outstanding supplier reservations. New fluid orders
also leave room for already queued recycling. Moving an idle area preserves its queue, delay and lifetime totals;
future spraying follows the pad's current assignment. No earlier treatments are recovered retroactively.

## Staffing and operating costs

Each purchased truck includes one assigned operator. **Crew & costs** starts or ends the pad's crew shift;
ending a shift waits until treatment finishes and trucks return to their parking poses. Off-shift pads do not
move, refill or spray their trucks, and hold new aircraft until the crew returns. Existing authored fleets migrate
once onto shift; an explicitly saved off-shift state stays off shift.

Initial gameplay prices and game-hour rates are centralized in `DeicingOperations`:

| Item | Cost |
| --- | ---: |
| Supply work area with storage | $35,000 |
| Spray truck with assigned operator | $180,000 |
| Collection/recycling upgrade per supply area | $45,000 |
| Purchased fluid | $2 / litre |
| Supplier delivery | $150 / load of up to 20,000 litres |
| Operator on shift, including idle time | $24 / game hour |
| Truck upkeep while staffed | $10 / game hour |
| Staffed pad power | $6 / game hour |
| Driving / spraying | Additional $18 / $60 per truck per game hour |
| Recycling processing | $0.08 / returned litre |

`Airport.deicingAccounts` serializes the balance and separate equipment, fluid/delivery, payroll and running
expense totals. It starts with $500,000. Unlimited sandbox spending is the default; every expense still reduces
the recorded balance. **Use operating budget** also refuses unaffordable purchases atomically. Ongoing bills may
take the balance below zero; new purchases then wait for funding. The sandbox can add an explicit $100,000 capital
injection. Pausing game time pauses payroll, fuel and processing costs. This account is independent of the guided
career's funds: career construction does not yet offer deicing pads or save the airport's deicing layout.

## Traffic and current scope

Routes crossing installed suitable pads gain a nose-wheel `DeicingStop`. Once an airport owns deicing trucks,
snowy departures find a route through an equipped compatible pad, including a detour when needed. Empty
trucks or missing supply hold the aircraft there with their waiting reason. Existing airports with no installed
fleet retain their previous behaviour until a future career winter-operations requirement is introduced.

The turn-aware route search retains width/surface restrictions and runway/junction clearances. A departure
remembers completed treatment across replanning. Moving, resizing, reversing or deleting its bay invalidates
an obsolete planned stop by comparing the current painted bar and direction with the route.

Supply areas, fleet stock, upgrades, recovery queues and cost accounts are serialized in authored airports.
The existing career does not yet include these purchases or a general airport save loop. Type I/IV mixtures,
heating/concentration simulation, holdover expiry and automatic reorder policies remain future gameplay.

## Verification

The compact-browser and Sandbox entrance follow-up passed **76 deicing/movement/access checks** in
`Logs/deicing-compact-sandbox-complete.xml`. Six Sandbox access cases cover connected paving, idempotency,
player-gate preservation (including the chosen attachment endpoint), closures, scene/career exclusion and
occupied corridors. `DeicingBuildUiReview.RunCatalogFromCommandLine` captured the smaller three-tab browser
and verified one four-corner placement outline without an internal guide (`Logs/deicing-compact-ui-verified.log`).
`CaptureSandboxOutsideAccess.RunPlayModeFromCommandLine` verified automatic installation on the actual
Sandbox's Play startup, without manually calling the installer or rebuilding/saving its scene
(`Logs/sandbox-outside-play.log`). The connected road, marked portal and in-game Objects panel are captured
in `Captures/DeicingBuild/sandbox-outside-{road,portal,play}.png`.

Verified after the construction/management update: **89 checks passed** in
`Logs/deicing-build-verified.xml`: 70 deicing/movement/equipment checks and 19 career construction/save checks.
The ten parking cases cover actual arrival, pauses, explicit gate topology, closures, paid-order reload,
legacy stock/pose preservation, whole-bay paving, symmetric placement exclusion, pending demolition guards
and simultaneous truck/tanker delivery with exact stock accounting and non-overlapping bodies.

`DeicingBuildUiReview.RunFromCommandLine` enters Play mode and captures all three catalogue tabs, all three
management pages, the compact supply inspector, model drafts and 37° free rotation. It checks that previews
do not change the airport layout. The final run passed (`Logs/deicing-build-ui-final.log`); its management
refresh includes two supply areas, a free parking bay and a new pad ready to order a truck
(`Logs/deicing-management-ui-verified.log`). Current screenshots are in `Captures/DeicingBuild/`.
These checks ran in an isolated project copy to preserve the user's open Unity editor and scene.

`DeicingTests` covers remote deliveries, empty truck travel/refill/return, disconnected access, shared-area
round-trip reservations, finite stock conservation, automatic mid-treatment refill/resume, pauses and moving
aircraft, paired pacing, placement, bay deletion/edits, route detours and end-to-end stop/spray/clear/takeoff.
Surface loss stops an active truck in place before replanning; failed return routes release the shared supply
lease. Delivery access loss/resumption, legacy stock migration and cancelled-aircraft cleanup have regressions.
`DeicingMotionTests` checks curvature, vehicle motion, wing coverage and nozzle reach. Ordinary traffic tests
retain arrival/departure route regression coverage.

Earlier supply-feature verification, 2026-11-12: all **60 focused deicing checks passed** in `Logs/deicing-supply-tests.xml` (Unity EditMode):
14 motion/coverage, 8 supplier-delivery, 5 equipment/import, 6 recovery/staffing/cost and 27 deicing/traffic
integration cases. These include complete service for all eight aircraft, actual tanker/refill traffic coordination,
multi-load orders, paid-volume restart, monetary rollback on rejection, full-dose runoff recovery, supplier-capacity
reservation, off-shift waiting and game-time payroll. Existing authored-airport and sandbox behavior remains covered
by the deicing fixtures; this result is scoped to those fixtures.

`AirCity.EditorTools.Vehicles.DeicingReview.RunFromCommandLine` creates the separate
`Assets/AirCity/Scenes/DeicingReview.unity` scene, pre-fills its trucks through the real remote refill journey,
then captures root/middle/tip coverage, the enclosed cabin and a later automatic refill. It checks pause,
completion and fluid conservation, writing `Logs/deicing-review-metrics.json`. It leaves Sandbox untouched.
`DeicingAnimationReview.RunFromCommandLine` records the saved scene at quarter-game-second intervals into
`Captures/Deicing/Animation`, together with a per-frame motion/fluid ledger and a paused frame.
The earlier `DeicingHudReview` captured the former equipment popups; use `DeicingBuildUiReview` for the current UI.
The final rendered review completed treatment in 33.9 game seconds and verified the later remote refill/return.
`Captures/Deicing/deicing-cycle-4x.mp4` plays the 145 captured frames at four times game speed. The recorded
truck poses contain no stationary turns above one degree; the 8,000-litre fluid ledger varies by less than
0.013 litre from floating-point accumulation. Root, tip, cabin, refill and UI images are in `Captures/Deicing`.

`DeicingSupplyReview.RunFromCommandLine` builds a separate `DeicingSupplyReview.unity` and captures actual
tanker approach, hose payout, unloading, retraction, departure and the purchased recycler in `Captures/DeicingSupply`.
It checks the combined delivery/application/recovery ledger and that render callbacks cannot advance transfer.
The earlier expanded HUD review captured the former recycling, operating-budget and crew-shift popups.
The supplier render review passed with a 2,500-litre delivery and 258.9 litres recovered from the aircraft's
full treatment (`Logs/deicing-supply-review-final.log`). Its complete 525-frame sequence covers every delivery
phase, with zero recorded cargo-ledger drift, and is encoded as `Captures/DeicingSupply/supplier-delivery-4x.mp4`.
The Play-mode review also ran through a real connected-hose unload and captured the six finished menus
(`Logs/deicing-supply-play-verified.log`); recovery savings, the supplier's remaining volume and crew costs are
visible in `Captures/Deicing/{recycling-inspector,operating-budget,crew-costs}.png`.

## Shared game modes and funds

In the main game, deicing purchases and operating expenses use the shared airport balance. Career gates
purchases against that balance; Sandbox skips charges while retaining expense statistics. The deicing panel
shows the shared funds and removes its fixture-only budget/funding controls. Fractional operating charges carry
in the common save until they reach a dollar. The original local account remains available to standalone review
fixtures. All placed facilities, parking, outside access, trucks and fluid stocks belong to the same full-airport
save in either player mode.


## 3D road integration (2026-11-15)

Road journeys and drive poses retain their chosen 3D deck. Static pavement checks index the actual sampled
road height, so a vehicle cannot use a tunnel's projected footprint as a shortcut at surface level. Revalidation,
dynamic equipment checks and supplier tankers preserve path elevation. Vehicle/aircraft clearance respects
vertical separation; same-level servicing rules are unchanged.
