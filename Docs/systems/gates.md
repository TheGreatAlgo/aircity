# Gate layouts and automatic boarding bridges

The player places terminals, fixed corridors, bridge bases and aircraft parking stops. The game chooses passenger
doors and calculates bridge angles, extension, tilt, cab rotation and parking positions. There are no saved bridge
parking poses, moving-bridge waypoints, or player-authored operating sequences.

## Authoring

Play **Sandbox.unity**, then click **Gates** in the bottom toolbar, or click an existing stand/bridge.
**Stand** and **Bridge** in the bottom toolbar open the same workspace in parking or bridge placement mode.
Click an existing bridge's model to select it directly; its owning gate is selected automatically and the same
panel exposes its model, finish, world handles and removal action. Stand inspection no longer has a separate bridge pop-out.
Choose a stand in the panel or click one in the world. The airport pauses by default; the pause toggle and closing
the panel restore its previous speed. Camera mouse controls and the world placement/drag tools remain available.
Text fields capture the keyboard, so typing does not move the camera, delete a selection or trigger aircraft keys.
Selecting a terminal also exposes **Design building** in its inspector; terminal editing works before any stands exist.

The panel has **Build**, **Aircraft**, **Appearance** and **Saved gates** views. The runtime palette uses light, opaque surfaces,
24-unit outer padding, spaced controls, quiet toggles and illustrated facade cards. Numeric settings use steppers; typed values
commit with Enter or when focus leaves the field. Structural positions are edited in the world.
Gate coordinates are relative to the selected stand: x across to its right, s into it from its front line. Corridor
bends display **(x, floor height, s)**. Heights are metres above the apron.

- **Build > Terminal** edits individual storey heights (2–40 m) and the boarding floor. Corridors from a terminal
  always arrive at one of its floors above the ground (`Terminal.BoardingLevel`): the boarding floor, or the floor nearest
  a bridge's or head's own connection height. Changing storey heights or count keeps the boarding floor and moves
  its corridors with it. A one-storey building has no such floor and keeps the height asked for.
- **Build > Parking** creates and edits aircraft stands: pavement, angle and light-aircraft eligibility. Size and
  push-back/taxi-out are chosen before placing and fixed afterwards (shown read-only); remove and re-place the stand to change them. Click beside a taxilane to place; Ctrl/Cmd-drag places and turns a free stand. A successful placement
  switches to editing that stand, with actions to add a bridge or adjust the aircraft stop. Stand deletion is explicit.
- **Build > Jetbridges** chooses a model and glass/enclosed finish, including glass tint, before placement. These
  choices are available before any stand exists. The selected family is checked and rendered during placement;
  the placer does not silently substitute another number of sections or reach. Click a terminal/concourse wall, then the bridge base.
  Select a bridge to see its blue world handles. Base floor height is independent of the target aircraft deck.
  Placing successfully switches to editing that bridge. Delete removes just the current bridge; placing another
  uses an independent draft, so changing the palette does not repaint already placed equipment. Finish controls
  stay available while adjusting world handles, with teal/red feedback during a drag. Place/cancel/remove actions
  stay in a fixed footer on short screens. Escape cancels placement before closing the workspace; Delete on a
  corridor corner removes that corner, while Delete on the bridge selection removes only that bridge.
- Click a **+** handle on a corridor to insert a corner, then drag it. **Shift-drag** a handle to change its height;
  **Ctrl/Cmd** snaps the drag to 0.5 m. Shift-dragging a terminal connection steps between the terminal's floors. Right-click a corner or select it and press Delete to remove it. Escape during
  a drag restores the complete gesture. Every segment contributes to length, slope, collisions and automatic columns.
- **Build > Concourses** creates buildings with walls down to the apron by default. Choose **Elevated** for a head on columns. Heads have editable widths and main/upper floor heights. Additional heads can branch from another head's wall.
  Connections resolve outward from a terminal; a cycle without a terminal stays disconnected.
- The incoming fixed corridor remains elevated on its own supports, allowing a connection over a service road.
  A grounded head's full building footprint must keep clear of roads and taxiways; elevated heads check their columns instead.
- Columns are automatic in the normal workflow. **Hold Alt** to reveal column handles in the world for a precise
  adjustment. Corridor columns slide along their route and keep clear of pavement. There are no bend lists, support
  lists, XYZ fields or hex-colour fields in the runtime palette. Changing a route returns its columns to automatic placement.
- Select an aircraft to edit its nose-wheel stop offset or eligibility. Optionally leave particular bridges unused
  for it. Door assignments stay automatic. Traffic rejects stops outside the stand. Offsets apply to push-back stands.
- The designer opens with a blue-tinted temporary aircraft labelled **PREVIEW AIRCRAFT** at the selected stand's parking stop. The Aircraft page switches
  models, hides the preview or scrubs the docking slider to inspect calculated movement. This unsaved preview does not
  participate in traffic or change bridge simulation state. A live aircraft parked there is hidden visually while
  previewing and restored afterwards. World clicks select gate equipment while the designer is open.
- While placing a bridge, the full bridge model follows the cursor, including its fixed corridor and moving tunnel.
  A compatible aircraft appears at the parking stop for scale. Green/red uses `AirportBridges.PreviewBridge`, the
  same complete reach, parking and movement check as `AddBridge`; it is recalculated at the actual cursor position
  and whenever layout/assets change. A refused click retains the connection and red model so it can be adjusted.
  These models never enter the saved layout, occupancy or traffic and are removed on cancellation/tool changes.
- **Saved gates > Save gate design** saves geometry, family/appearance choices and service configurations as JSON under
  `Application.persistentDataPath/GateLayouts`. These files survive leaving Play mode and restarting the game.
  Apply a saved preset to any stand, optionally mirrored. Invalid physical placement rolls back the operation.
  Equipment is not resized. Other sandbox edits still reset when Play mode ends; save a preset to keep a gate.

The Unity editor also has **AirCity > Airfield > Gate Designer**, using the same layout and docking backend. It edits
the scene outside Play mode, has Scene handles and Undo, and saves `GateLayoutPreset` project assets. Runtime
presets resolve family names, aircraft models and base appearance names against the airport's catalog rather than
storing session-specific Unity object IDs. Runtime finish overrides retain the base appearance's materials and attachments.

## Data

| Type | Responsibility |
|---|---|
| `JetBridgeDefinition` / `BridgeSpecification` | Family prefab, sections, dimensions, lift/rotation/slope limits and movement duration |
| `JetBridge` | Saved base, corridor root/bends/supports, family/appearance and structural heights; legacy door fields are preferences |
| `BridgeHead` | Fixed head and incoming corridor; extra heads live in `Stand.additionalHeads` |
| `HeadFoundation` | Grounded concourse building (default) or elevated head on columns |
| `BuildingRoof` | Flat, wave and scalloped roof profiles with rise, direction and canopy extension |
| `FacadeStyles` | Four coordinated glass facades and four restrained glass tints |
| `BuildingExterior` / `BuildingFacade` | Shared terminal/head facade defaults, overrides by footprint face, wall lean and finishes |
| `StandServiceConfiguration` | Aircraft eligibility, nose-wheel stop offset and bridge IDs to leave unused |
| `BridgeCorridor` | Derived segments, slopes, supports, footprints and collision solids |
| `CorridorMesh` | Draws corridor segments by bending the model's Link: mitred corners, ends buried in the wall |
| `BridgeServicePlan` | Derived joint door assignment and checked moving poses for one aircraft |
| `JetBridgeAppearance` | Cladding/glazing/roof colours, glazing amount, part materials and attached signs/equipment |
| `GateLayoutPreset` | Reusable stand-relative assembly and aircraft configurations |
| `GatePresetStore` | Persistent runtime JSON layouts, catalog resolution and appearance snapshots |
| `DevGateDesigner` / `DevGateDesignerPanel` / `GateDesignerGui` | Runtime controller and padded build/appearance palette |
| `GateLayoutHandles` | Projected world controls, pointer capture, checked corridor/column edits and gesture cancellation |
| `GateAircraftPreview` | Visual-only aircraft copy, independent of simulation |

Existing scenes retain their original long/short dimensions when no family is selected. Height zero means the
family/terminal default. `JetBridgeRules` holds legacy defaults. Imported `.vehicle` bridge dimensions also produce
four family sub-assets (two/three sections, long/short); these use the rig's prefab unless a custom prefab is supplied.

Custom prefabs use the rig part names (`Rotunda`, optional `RotundaHigh`, `Cab`, `Drive`, `DriveLegs`, `Link`, support
parts, and the tunnel names in the definition). `modelLength` is a tunnel mesh's original length; `length` is its
mechanical length. Profile widths/cross-sections must match the art. Create a definition for custom dimensions;
do not mutate the shared legacy specification objects.

## Automatic service

`Airport.Bridges.ServicePlan(stand, aircraftType)` is cached by layout and asset revisions. It searches reachable
passenger doors on both sides and decks, assigns each door at most once, and prefers operating more bridges.
Legacy door labels break equivalent choices; they do not force the same door for every aircraft.

Complete assignments go through `BridgeLayout.Check`: fixed placement, automatic parking, aircraft/wing clearance,
other bridges, heads, neighbours, its own fixed corridor and staged movement. Bridges without valid assignments stay parked. Inspectors,
previews, scene seeding and simulation consume the same checked plans. `Dock()` is the geometric candidate solver,
not permission to operate.

Bridges swing retracted, then telescope out (`BridgePose.Move`), reversing those stages to retract. The stand
synchronizes its bridges to the longest family movement duration, matching the checked motion. Departures still
wait for all attached bridges to retract before requesting a tug.

`StopFor(type)` is shared by arrivals, parking, bridge geometry and stop-bar painting. `ParkPosition(type)` subtracts
the aircraft wheelbase. Exclusion affects traffic stand selection and bridge compatibility. A stand still represents
one occupied aircraft position; simultaneous alternative MARS stand occupation is a separate traffic feature.

## Appearance and art

Terminals and grounded heads share `TerminalBuilder` and the `AirCity/Building` shader. `Terminal.storeyHeights`
contains individual heights; missing entries default to 4 m for the ground storey (`Terminal.GroundStorey`, a jet
bridge's standard floor, so the default first floor boards level) and 5 m above it. Each floor's spandrel band ends
`Terminal.BandBelowFloor` (0.35 m, the corridor's underside, `JetBridgeRules.LinkBottom`) below the floor line, keeping
its style's depth. A corridor arriving at a floor sits on top of the band and the storey above's glass starts level
with the corridor, so it reads as walking straight in. Up to 12 storeys are supported. The
head's building floors follow its structural boarding levels, with a solid envelope from apron level to its roof.
Incoming corridors retain the bridge model. Choosing a facade style also gives elevated heads the shared
procedural facade, with their existing mechanical supports and clearance height.

**Appearance > Terminal / Concourses > Finish** offers four coordinated, predominantly glass styles:

| Style | Framing and roof edge | Architectural columns |
| --- | --- | --- |
| Classic | Silver mullions and a flat roof cap | Regular slim piers |
| Minimal | Fine dark mullions and a thin roof edge | Uninterrupted curtain wall |
| Framed | Strong mullions and a deep fascia | Broad rectangular piers |
| Colonnade | Light framing and a broad roof edge | V-shaped columns |

The only finish choice is **Glass tint**: Clear, Blue, Green or Smoke. Frame, cladding and roof colours, glazing
coverage, mullion pitch and roof trim come from the style; there are no per-wall finish controls or colour mixer.
Moving jetbridges offer **Glass panels / Enclosed panels**, with the same optional glass tints.

**Facade angle** restores a simple inward/outward angle control (±30°). Apply it to all walls or one original wall;
selected walls are outlined in blue in the world. Positive angles lean out, negative angles lean in. **Column gap**
(0–6 m) moves the style’s architectural columns out from the glass; it applies to the whole building. Angled concourse
walls use the Building foundation; elevated mechanical heads retain their original wall envelope.

**Roof shape** offers Flat, Wave and Scalloped forms. A single rise control (0–12 m) and Along / Across direction
shape the whole roof. **Roof extension** (0–8 m) adds a projecting canopy, with a closed underside and deep edge.
The canopy automatically reaches past the architectural columns, including their depth, so they support its underside.
Wave uses broad smooth crests; Scalloped repeats suspended curves between high points.
The glass, mullions, architectural columns and parapet follow the actual roof surface. Floor and boarding elevations
remain horizontal. The profile follows the building's longest edge, with automatic repetition for longer buildings;
it also works on rotated and concave layouts. Flat roofs retain their existing rooftop equipment.

Style changes preserve floor heights, the footprint, roof form and authored wall lean. Curved walls keep that face identity
when tessellated. Corners use local normal-based mitres instead of intersections of nearly parallel lines in airport
coordinates; dense roof samples share the same bounded offsets. Mullions stay on the glass; separated architectural
columns retain their gap along the slanted wall. Roof soffits close the extended canopy from below.
Curved roofs and canopy surfaces use one height field in the final projected coordinates. Slanted walls and columns
intersect that surface at their actual positions, rather than interpolating wall lean through roof triangles.
The mesh is calculated near the building origin for precision; smooth roof normals retain sharp scalloped crests.
Invalid combinations of roof rise and facade angle are rejected before the surface can fold. Degenerate clipped slivers are omitted.
Boarding connections query the facade section at their own elevation. Full projected envelopes, including canopies
and column footprints, are checked against roads, stands and other terminal buildings; collapsed or crossing facades are rejected.

Profiles, roof shapes and curated style choices are stored as layout data and cloned deeply. Existing finishes remain intact until a style is selected. Gate presets include head foundations and facade overrides;
mirroring exchanges the head's left/right facades. Terminal profiles belong to the airport's scene layout, not an
individual gate preset. Existing head layouts without a foundation field become buildings; their optional manual
column positions are retained for switching to elevated mode.

The facade library uses `FacadeStyles` recipes and shared render details: original wall faces remain addressable independently of tessellation
or storey heights. Mesh UV1 records storey, window-bay pitch and source face. The current surface system covers
parameterized curtain walls, opaque cladding and extruded curved roofs. Custom panel assets, openings, interiors
and freely sculpted roof surfaces remain future extensions. Face indices follow footprint edge order; inserting/reordering corners requires remapping overrides.

For developer-authored assets, **AirCity > Jet bridge appearance** still supports cladding, glazing and roof colours.
Glazing amount can replace glazed panels with opaque cladding. Optional part materials override the atlas;
attachment prefabs follow named rig parts for signs, advertisements, lights or equipment. Attachments are decorative
and do not enlarge mechanical collision envelopes.

The Blender generator exports `BridgeZones` vertex colours (red cladding, green glazing, blue roof). The rig
shares an `AirCity/JetBridge` surface material over the original imported atlas. Separate rotunda drums and columns allow height
changes without distorting the drum. Generated files remain owned by `ArtSource/Vehicles/JetBridge/model.py`.

## Validation

`TerminalBridgeTests` covers the original layouts plus automatic reassignment, runtime clearance refusal, aircraft
stops/exclusions, unused bridges, bent/downhill corridors, two-section rigs, branch heads and preset deep copies.
Runtime tests also reload portable presets against fresh asset instances, verify preview isolation, and check
designer pause/close behaviour. `GateHandleTests` covers world edits, physical rejection/rollback, automatic-height
restoration, carrying attached bridges when a concourse moves, and cancelling a provisional corner. Runtime aircraft previews clone an inactive template, disable behaviours/colliders
before activation and share its rendered materials; they never register with `Aircraft.All` or occupy a stand.
`BuildingEnvelopeTests` checks grounded/elevated heads, road clearance, tall/mixed storeys, tilted-wall connections,
sloped mesh normals, overhang protection and face identity/copies. Building changes need a Play-mode/rendered check too.
Run bridge, stand and traffic EditMode tests for changes here; rig and shader changes also need rendered checks.

## Architectural references

- [RSHP: Heathrow Terminal 5](https://rshp.com/projects/transport/terminal-5-heathrow-airport/): the outer shell and roof
  are independent of the multi-level internal arrangement, including double-height spaces. This motivates independent
  storey elevations, facade geometry and boarding connections rather than globally scaling a building mesh.
- [Nordec: Helsinki Airport entrance hall](https://nordec.com/blog/nordec-supplies-glass-facades-and-architectural-roof-windows-of-the-new-entrance-hall-in-helsinki-airport-terminal-2/):
  glass facade and glass pillars over 10 m tall; a fixed 5 m storey assumption cannot describe that frontage.

## Runtime editing interaction

The palette exposes build choices and appearance; structural editing lives in the airport view. `GateLayoutHandles`
projects connection, base, concourse end, corner and insertion controls to the screen. Alt reveals columns. Handles
are visible only for the current target in Build/Select, so placement ghosts and aircraft previews stay uncluttered.
Legacy per-aircraft debug reach lines are hidden while the palette is open.

`DevToolbar` and `DevAirfieldTool` consult the same once-per-frame input handler before their own shortcuts or
picking. A handle owns its gesture, including the release frame; Delete on a selected handle cannot delete a stand.
`DevOrbitCamera` stops reacting during a handle drag. Invalid positions show a red attempted point while preserving
the last valid layout. Escape rolls back the entire gesture, including a new corner or bridges carried by a head.

Corridor edits invalidate airport layout caches and refresh the bridge rig without rebuilding unrelated taxiway
meshes.

## Corridor drawing

`BridgeRig` draws each corridor segment with its own copy of the model's `Link` mesh bent to the segment
(`CorridorMesh`), not the Link stretched as a box. Sections stay upright (a slope shears the mesh rather than pitching
it), and neighbouring segments end on one mitre plane at their corner with the corner's floor, so the joint is
watertight. Corners sharper than about 140° fall back to square ends overlapping half the width. The first segment
reaches back into the wall it leaves (`BridgeCorridor.IntoWall`) until every corner of its end section is inside the
facade at its own height, which hides the end where the corridor meets the wall at an angle, stops within
`ConnectReach` of it, or the wall leans. This is drawing only; placement checks use the corridor as laid out.
`VehicleAssetPostprocessor` keeps the `Link` mesh readable for this; a custom model with an unreadable Link is stretched as before. The shared placement checks enforce physical constraints; the automatic service planner still chooses
aircraft doors and moving poses. The Unity Editor Gate Designer retains its precision fields for asset authoring.

Baggage clearance uses `AirportBridges.BaggageExclusions`: conservative per-part envelopes over the
assigned bridge service plans and their reverse retraction paths, including the current pose and grounded
heads. The baggage planner adds 0.5 m clearance. This permits starboard loading beside a port bridge without
reserving a full rotunda-centred swing square through the aircraft. Layout changes regenerate the envelopes
and cancel incompatible active baggage plans.
