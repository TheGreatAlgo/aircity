# Baggage depot family

Three generated, metre-scale buildings share an industrial kit and the airport's existing Building material.
These are baggage facilities for the passenger fleet. The maintenance garage remains a separate asset.

Halls can connect to a terminal through a [timed baggage link](../systems/terminal-baggage.md). Connected halls
inherit the terminal's wall and roof colours. Covered links add exterior conveyor bridges; underground links
use service hatches and an optional route overlay. Terminal interiors remain abstract.

| Tier | Shell footprint | Working positions | Handling teams | Layout |
|---|---|---:|---:|---|
| Small | 13 × 26 m, plus a deep canopy | 1 | 2 | Covered pull-up handover outside a sorting shed; independent equipment parking alongside |
| Medium | 28 × 36 m | 2 | 4 | Enclosed drive-through hall, pitched roof, sorting islands and a staff office |
| Large | 52 × 44 m | 4 | 8 | Repeated portal-frame halls with roof monitors, preparation areas, a larger equipment yard and an outside bypass |

The site includes entry/exit headers, approach clearances and home parking beyond the shell dimensions.
`firstBay` anchors the adjoining service-road spine; `bearing` is the forward direction through all working lanes.
`BaggageDepotLayout` supplies the same dimensions to presentation, routing, placement and clearance.

Medium/Large openings are 5 m wide and 4.2 m high. A tractor with three generated covered baggage carts
fits completely inside at the handover stop. Small trains are limited to two carts; Medium/Large trains to three. Floors, wall returns, jambs, raised shutter stacks and portal
frames are geometry. Vehicle clearance uses separate wall/machinery rectangles, leaving the openings traversable.

## Parts and appearance

`BaggageDepotArt` builds separate shell, roof, machinery and ground-marking meshes. Its shared kit contains
ribbed pale metal cladding, concrete upstands, dark standing-seam roofs, blue machinery, glazed office windows,
personnel doors, canopies, safety bollards, belt sections, cages, sorting tables, loading aids and charging cabinets.
Large halls add container preparation roller beds. Operational container stations remain independent; those
roller beds do not introduce a new container-handling simulation or freight economy.

The **BAGGAGE > Show more > View interior** control removes the roof, gables and upper roof structure.
The shell and handling machinery remain visible. Small depots also expose their sorting tables and internal storage.

## Working lanes and parking

Each team retains its own tractor/cart parking and loader parking. Pairs of teams share a working position.
The depot queues returning teams fairly within each pair. A team reserves its position before leaving the
aircraft for the depot and retains it until every vehicle clears the lane. Cancellation/recovery releases the
reservation with the crew. Teams do not park idle equipment in a shared working position.

Arrivals unloading and departure collection use the same stop. The train remains stationary during each
handover. Bag ownership remains in the shared airport ledger; the existing bounded art pools display stored
and moving bags. Arriving trains wait at the aircraft when their working position is occupied.

Building or expanding requires clear paved space for the whole site and sufficient straight service-road
frontage. The saved Sandbox Small lot backs onto a taxilane; a larger depot needs a different clear site.

## Equipment upgrades

Upgrades are installed/removed from the expanded Baggage panel while all hired crews are home. They have
visible machinery and affect specific depot operations; they do not change aircraft handling or vehicle speed.
Purchase prices, research and wages await the game's economy.

| Upgrade | Visual change | Effect |
|---|---|---|
| Lifting aids | Assisted lifting arm beside each working lane | 30% faster depot handover motion |
| Powered belts | Drive motors on conveyor sections | 50% higher depot conveyor travel speed |
| Automatic sorting | Scan portal and branching conveyor positions | Outgoing sorting interval changes from 3.6 to 1.25 / `BaggagePacing.WorkRate`; Medium/Large only |
| Buffer storage | Additional rack levels | Doubles the shared arrivals/departures storage capacities |

The slowest applicable handover/sorting interval governs dispatch. Individual conveyor travel remains separate.
Removing storage or reducing size refuses excess baggage rather than discarding it. Automatic sorting must
be removed before downgrading to Small. Existing 2/4/8 team, 4/12/24 trailer and 8/20/40 worker limits remain.

## Review and regeneration

The buildings are generated in Unity from source and saved depot data; no FBX bake is required.
**AirCity > Vehicles > Create Baggage Depot Family Review** creates `Assets/AirCity/Scenes/BaggageDepotFamily.unity`
with all three sizes, parked equipment and examples of installed machinery.

`AirCity.EditorTools.Vehicles.BaggageDepotFamilyReview.Run` reviews an actual 72-arrival/90-departure turnaround
for every size, captures exterior/interior views, checks conservation and crew/bay release, then creates the
review scene. Larger halls are reviewed on clear sites north of the saved Small lot. Captures are written to
`Captures/BaggageDepotFamily/`. `CaptureShowroom` regenerates just the asset comparison scene and images.

Verified 2026-11-07 in an isolated Unity project: 118 distinct passing checks across the baggage/container
regression suite, focused depot checks and native Sandbox Play mode. Four native turnarounds (all three
tiers plus upgraded Medium) each conserved 72 incoming and 90 outgoing bags and returned every crew and
working-bay lease. Medium depot handovers measured 27 simulated seconds with basic equipment and 14 with
all four upgrades. This measures depot handling, not the whole aircraft turnaround. Results are recorded in
`Logs/depot-family-validation.json` and `Logs/depot-family-*.xml`.
