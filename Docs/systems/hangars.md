# Rentable aircraft hangars

## Player experience

Choose **Hangar** in the bottom build toolbar, then select **Small**, **Executive** or **Airline** in the
placement inspector. Career unlocks the tool with aircraft parking. Sandbox construction is free and both
modes use the same placement, traffic and saved data. Prices and rents are initial balance values.

| Size | Footprint | Storage | Career build cost | Rent per occupied bay per 30 days |
|---|---|---|---:|---:|
| Small | 18 × 16 m | One light code A general aviation aircraft | $12,000 | $600 |
| Executive | 84 × 42 m | Three marked parking spots sharing one clear-span floor and an 80 × 9 m lift door | $96,000 | $2,300 |
| Airline | 92 × 90 m | One clear-span bay with an 84 × 27 m opening for commercial aircraft | $420,000 | $10,500 |

Prepare cleared, level ground at the airport height. The cursor marks the doorway, the shell extends behind
it and the blue outline shows the approach in front. With **Snap 15°** on, move the doorway near the
appropriate setback from a straight, level taxiway.
The preview aligns the doors to the section. **Free angle** gives you manual position and heading.
Use **Turn** or Shift+scroll to rotate.
Right-click or Escape leaves the tool.
The placement preview uses the generated building geometry without creating an operational or saved hangar.

The roof overhang participates in overlap checks. Hangars cannot overlap other buildings, the visitor centre
and its car park, aircraft stands, runways, taxiways, service roads or public roads. New road and visitor
centre construction also protect an established paved hangar approach.
You can build a disconnected hangar and connect its taxiway later.

To connect an existing hangar, choose a suitable width and surface in the **Taxiway** tool. Snap a section
across the doors using the blue guide at either end, then extend it to the network. Leave enough straight
taxiway on both sides for turns. Measure setback from the door threshold to the near pavement edge.

| Size | Snap setback | Manual setback | Taxiway |
|---|---:|---:|---|
| Small | 10.5 m | Up to 12 m | Code A or wider |
| Executive | 38 m | 30–50 m | Paved code B or wider |
| Airline | 95 m | 80–115 m | Paved code C or wider. The largest airliners require code F (25 m). |

The actual route must support the aircraft's size, pavement and turning envelope. A connected bay generates its
own yellow lead-in line. The executive hangar uses one concrete apron across the shared door with a lead-in
line to each parking spot. Prepare the full approach as cleared, dry, level land.
Disconnected or blocked leases stop earning until access is restored.

There are two ways to use a bay:

- Select an empty hangar, choose a compatible type, enter tenant and registration, then **Start lease**.
  The resident starts stored nose-in. **Taxi to stand** backs it out, starts its engine and taxis
  it over the network to a free compatible stand. **Taxi into hangar** brings it back.
- Select an existing compatible parked aircraft and choose **Taxi to hangar**. Choose the destination
  when several bays are available. A vacant bay starts a lease for that physical aircraft, which travels
  from its current location.

Movement shares normal aircraft braking, ground spacing, junction and runway-crossing clearances. The
final approach slows to walking pace and stays aligned with the opening. Whole-aircraft bounds are checked
against walls and door cheeks, including the push-back; a taxiway too close to make the turn is refused.
Roads and aprons are never substituted for a disconnected taxiway route. This works without a runway.

Select a parking spot by clicking its part of the floor plan or stepping through **Spot** in the inspector.
The executive lift door serves all three spots. It stays open while a resident is moving
or obstructing the threshold. Return an on-airport resident to its spot before **End lease**, then demolish
the building once all of its bays are vacant. **Build another** places the same size.

The executive and airline hangars light up when AirCity's existing **Night** control is on. Press **N** in
the airport view to switch between day and night.

## Scope

The executive spots accept aircraft tagged as business jets or general aviation, with a maximum 24 m
wingspan, 30 m length and 8.7 m height. The [Starling A2](../assets/LowWingPiston.md), Starling A4, [Starling A6](../assets/LightJet.md),
[Starling B8](../assets/ExecutiveJet.md) and [Starling B12](../assets/FlagshipJet.md) are available to lease in
these bays. The A6 can taxi to a paved Code A stand from the Code B executive hangar approach. The B8 and B12
need a paved Code B or larger stand.
The airline bay accepts airliners up to 83 m wingspan, 88 m length and 26.7 m height. Hangar pushback for larger aircraft is simulated with the
validated ground path and traffic clearances. A stand that requires pushback still uses the airport's tug system.

A moving flight cannot be reassigned through the ground-transfer controls. Land at an ordinary stand first,
then taxi into the hangar. Residents participate in aircraft selection and traffic and can depart manually;
automatic sandbox traffic leaves them alone. A departed resident remains off-airport and its lease can be
ended. Executive aircraft enter and back out along their own marked paths through the common opening.
Automatic resident return flights and aircraft maintenance jobs are future work.

## Runtime and persistence

| Type | Responsibility |
|---|---|
| `HangarSpec` | Building and door sizes, bay count, taxiway setbacks, prices and rent. |
| `SmallHangar` | Door pose, footprint, aircraft fit, lease, resident identity/pose, ground-trip intent and rent. |
| `HangarConnection` | Graph attachment, centred concrete approach, synthetic parking layout and swept wall checks. |
| `Airport.Hangars` | Authored hangar enumeration, placement validation, creation, picking and guarded removal. |
| `HangarBuilder` | Generated steel shell, door poses, interior fittings, three mesh LODs and paved connections. |
| `AirTraffic` / `TaxiPlanner` / `Flight` | Ground transfers through the shared graph, bay reservations, nose-in entry and pushback. |
| `HangarConstruction` | Shared quote and placement transaction, terrain/funds checks and construction conflict helpers. |
| `HangarAccounts` | Live income credited to shared Career funds. |
| `DevHangars` | Shared placement, selection, lease inspector and door controls. |

The door threshold is the building root's local origin. +Z points outside and the shell occupies negative Z.
The executive building saves three individual leases and one shared door pose. Executive hangars placed
before the 42 m variant retain their 32 m footprint and parking positions on load. Aircraft
fit uses `AircraftType` dimensions. Residents use the normal aircraft prefab, motion, selection and registration
components. Their objects are `DontSave`; the lease recreates one aircraft from portable data. Nose-in
parking centres the actual aircraft bounds inside the shell. Floor and throat elevations support the moving
aircraft instead of leaving its wheels below the slab.

`FlightKind.GroundTransfer` starts in `Ready` and finishes in `Parked`, without approach or takeoff phases.
The planner reuses stand-entry geometry through a temporary `StandLayout` with an aircraft-specific stop
at the bay centre. A wider taxiway leaves that stop in place. `TaxiRoute.hangar` retains the destination and
`leavingHangar` retains door/floor ownership during exit. Replanning preserves the requested destination. An occupied bay only takes
its own resident, and an incoming aircraft reserves the lease before movement starts.

`AirportSaveData` appends `SmallHangar` to its allowed component kinds without changing earlier indices.
It saves the transform, building size, resident type reference, tenant, registration, door state, lifetime earnings and
fractional rent, resident livery asset, position/heading, on/off-airport state and ground-trip intent. Capture snapshots
the physical aircraft before serializing. Restore resolves asset references before enabling geometry.
The shared session resumes ground trips after historical charter/weather reconstruction, using the current
graph and normal clearances. Missing or invalid routes leave the aircraft at its saved position for recovery.

Rent accrues fractionally and credits whole currency units. `HangarAccounts.Advance` runs only during live
Career time, including live charter operation. It does not run when replaying historical weather or
reconstructing a saved active flight, so loading does not duplicate income. Paused time, vacant bays,
inaccessible bays and Sandbox do not earn rent. Ending a lease retains already-earned rent and its fraction.

`Airport.HangarApproachProblem` binds the shared world's terrain/vegetation validation to core access checks.
Authored fixtures can omit the callback. See [shared game architecture](new-game.md) for session/save ownership
and [the asset guide](../assets/SmallHangar.md) for geometry, references and visual review commands.

## Verification

The roof and lighting revision passed 63 Unity EditMode checks, including day/night switching and door
rebuilds at night. See `Logs/hangar-lighting-final.xml` and daylight and night captures in `Captures/FlushHangars/`.

The shared-door and materials revision passed 137 Unity EditMode regression checks and 49 focused checks on
the final rendered revision. The ground-transfer test brings a business-jet fixture into and out of the
executive hangar with the other two spots occupied. See `Logs/shared-hangar-tests.xml`,
`Logs/shared-hangar-final-tests.xml` and the daylight captures in `Captures/SharedHangars/`.

Run EditMode filters `SmallHangarTests`, `HangarBuildTests`, `HangarConstructionTests`, `HangarConnectionTests`
and `HangarTrafficTests` for lease/accounting/save, player build flow, physical ground transfers and construction
transactions. Broader affected systems include shared game saves, stands, roads and airside/runway terrain.

`AirCity.EditorTools.Airfield.SmallHangarReview.Run` creates the review scene and writes close-up open/resident,
closed, side and airfield-scale captures to `Captures/SmallHangars/`. It checks generated LOD bounds against
the construction footprint. Review scenes remain development fixtures; no hangars are added automatically
to a player's airport.

Verified 2026-11-16: 165 affected EditMode checks passed, including 17 hangar-specific construction,
lease/accounting/save, build-tool and floor-ownership cases. The live Game view review passed placement,
vacant/leased/closed inspections and tenant typing/Backspace without camera movement or demolition.
Model review checked open/closed visible and shadow LODs against the construction footprint.

Taxi integration verification: 87 affected EditMode checks passed, including all 27 hangar checks and
existing traffic, tower, tug, shared-save, aircraft and deicing-operation cases. Save tests cover incoming
taxi, hand push-back after the gear clears the door, and final approach already inside the bay; the restored
aircraft retains pose, registration, livery and destination. A rendered bay → stand → bay round trip completed
with approach, doorway and stored captures in `Captures/SmallHangars/`.
