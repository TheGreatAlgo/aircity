# Roads, bridges and tunnels: handoff

Updated 2026-11-15 of the road upgrade. How the systems work now is in [systems/public-roads.md](systems/public-roads.md)
and [systems/airfield.md](systems/airfield.md) ("Service roads", "Junctions at any angle"). Read the repo's
`CLAUDE.md` first.

## What the user asked for

- High-quality roads whose junctions look right at any angle.
- Road types for growing airports: 2, 4 and 6 lanes, and one-way.
- Real intersections: traffic lights at ordinary town junctions, stop lines, crossings, lane arrows.
- Raise and lower roads while drawing, to make bridges and tunnels. Inspired by Cities: Skylines, never copied from it.
- Bridges and tunnels on **airside service roads too**: tunnel under taxiways, stack roads over each other.
- Landside roads buildable in **both** the career and the sandbox. The user sees the two converging into one game.
- Placing a road must be smooth. There was about a 1 s freeze, which is fixed (see below).

Agreed order: (1) junction solver, done. (2) Road types and real intersections, done. (3) Curves. (4) Elevation,
bridges and tunnels. (5) Airside bridges and tunnels, then multi-level terminal kerbsides, parking and car traffic.

## One game, two modes

Career and Sandbox are rules for the same game in `NewGame.unity`. They must share the complete airport,
public-road graph, building tools, previews, simulation and save format. Do not implement a separate career
version of a feature. Career adds funds, tool unlocks and objectives; Sandbox unlocks tools and makes construction
free. The Game menu switches rules in place without replacing the world or moving the camera.

`CareerGame` retains its historical class name for scene compatibility, but hosts both modes. `DevAirfieldTool`
and `DevToolbar` are the shared build workspace. `CareerBuildTool` supplies the shared terrain/reception tools;
its old starter runway/taxiway/stand input paths remain compatibility entry points and are not the feature path.
`AirportSaveData` stores the full authored airport; `CareerLayout` is a tutorial/progression projection and migration
format, never the source of truth for a saved full airport. `PublicRoadConstruction` handles road transactions in
both modes. Keep all new curves, bridges, tunnels, upgrades and deletion on that shared path.

`Sandbox.unity` remains an authored development fixture, not the player-facing Sandbox mode.

## Two road networks (airside versus landside, shared by both modes)

- **Airside service roads** are `TaxiwayEdge`s with `road = RoadClass.Service` in `TaxiwayNetwork`, drawn by
  `Building/TaxiwayBuilder`.
  - One type, 7 m wide.
  - Shared 3D alignment, saved grades and Ground/Elevated/Tunnel structures. Separated crossings stay disconnected.
  - Depots, tugs, baggage and de-icing use them (about 17 files), so keep them in this graph.
- **Landside public roads** use `World/PublicRoadNetwork`, a separate 3D graph drawn by `World/PublicRoadGeometry`.
  - Nodes have real heights. Crossings within 0.35 m of each other form one junction.
  - Roads at different levels stay separate and need 6.5 m of clearance.
  - Segments are Ground, Elevated or Tunnel, with shared curved alignment and grade data.

## Done

### 1. Junction solver (shared by both systems)

`Runtime/Airfield/Building/JunctionShape.cs` lays out every taxiway, service-road and public-road junction.
- **Cuts:** each section is cut back where its whole rectangle stops overlapping the others (`Needs`).
- **Corners:** kerb fillets go in the corners, and narrow arms flare into wide ones.
- **Outline:** the union of those pieces, traced from the node, so it is always star-shaped and the fan triangulation
  never folds.
- **Sharing a section:** both builders work in two passes (needs first, then shapes), and each end of a section gets
  `Reach(len, needHere, needThere)`.
- **Caching:** `SolveCached` remembers solved junctions.
- **Curves:** public bends between sections of the same type are real curves (`RoadType.curveRadius`), and the arc
  is added to `links` so terrain grading and ground cover follow it.

Guards:
- `TaxiwayNetwork.JoinProblem` (airside) and `PublicRoadNetwork.JoinProblem` (landside) refuse a line that meets a
  road under `TaxiwayNetwork.MinJoinAngle` (15°) or leaves a section too short for its junctions. The landside guard
  also refuses highways crossing at grade.
- In the dev line tool, a click a few metres from a junction snaps onto it.
- `PublicRoadLayout.ShapeProblem` is the guard for the career access road.

### 2. Road types and real intersections

- **Types:** `World/RoadTypes.cs` has one `RoadType` per `PublicRoadStyle`.
  - Country road, airport access, two-lane street, highway, ramp, four-lane avenue, six-lane boulevard, and one-way
    with 1, 2 or 3 lanes.
  - Each type sets lanes, lane width, median (none, raised island, barrier), shoulders or kerb and sidewalk, centre
    line, speed, curve and kerb radii, price per metre, and whether it may cross at grade.
  - **Saves store the style number: append new styles, never reorder.**
- **Junction control:** `PublicRoadNetwork.Control` / `DefaultControl` applies it automatically on `Connect`.
  - Traffic lights where a kerbed (town) road meets others and traffic arrives from two or more of them.
  - Otherwise a priority junction.
  - `SetControl` chooses one by hand (it sets `controlChosen`, so later changes leave it alone).
  - A road that ends on another road's section joins it as a T-junction (`Attach`).
- **Lane rules:** `LaneConnections` numbers lanes from the kerb. The kerb lane turns right, the centre lane turns
  left, every lane may go straight, and a one-way road leaving the junction takes no traffic in.
- **Geometry:**
  - Kerbs (15 cm) with sidewalks, and grass median islands or concrete barriers. They go in the new meshes
    `kerbMesh` and `medianGrass`, both two-sided.
  - Lane lines: dashed, solid for the last 25 m before a junction, and stopping at the stop line.
  - At each approach: a stop line or give-way dashes, a zebra crossing on kerbed roads, and filled lane arrows that
    follow `LaneConnections`. `StopLine(node, edge)` says where the stop line is.
- **Signals:** `PublicRoadSignalsView` puts a kerb pole on each approach. Approaches with two or more lanes also get a
  6.6 m mast-arm head, and divided roads a head on the median. The phases are unchanged: a fixed 44 s two-phase cycle
  (`PublicRoadSignals`).
- **Sandbox Street tool:** `Dev/DevStreetTool.cs`, a toolbar button after Road, with icon `street` from
  `ArtSource/UI/icons.py`.
  - It draws onto `World/PublicRoadsView`, a child of the airport, created on first use.
  - The tray has a card per type with a to-scale cross-section.
  - It snaps to nodes and sections, shows the real mesh while you draw, and refuses bad lines in red.

### Placement lag fix

`Editor/Airfield/RoadPlacementTiming.FromCommandLine` writes `Logs/road-placement-timing.txt`.

| Placing… | Before | Now |
|---|---|---|
| Sandbox airside road | ~300 ms | ~45 ms |
| Sandbox street | — | 13–21 ms |
| Career access road | ~1250 ms | ~80 ms |
| Later career road change | — | ~95 ms |

Causes and fixes:
- **Runway node drift:** `TaxiwayNetwork.Rebuild` re-projected runway nodes every time, moving them by a float ulp, so
  every rebuild looked like a layout change. Nodes already on the centreline are now left alone.
- **Jet bridges:** `AirportBridges` re-planned every bridge on any layout change. It now has `Signature()`, a
  millimetre-quantised fingerprint of its inputs (types, terminals, stands, and pavement within 60 m of the gates),
  and `Version` (`BridgeRig` uses it).
- **Equipment staging:** `StandEquipmentLayout` re-packed every stand. It now caches each stand by a hash of
  everything that can reach that stand.
- **`Box.Overlaps`** allocated on every call; it no longer does.
- **Career valley:** the whole valley was redone on every road. `ValleyView.RefreshRoads` now diffs the road network
  and regrades, repaints and re-scatters only within 64 m of what changed. Ground cover and paint updates drain in
  `LateUpdate` within 3 ms per frame, and `SetGroundCoverExclusions` refreshes only the tiles that changed.
- **Saves:** placing things in the career now saves in the background (`QueueAutosave`).

### Tests and review tools

- **Tests:** `JunctionShapeTests` (awkward and 400 random junctions, guards, builders) and `RoadTypeTests` (types,
  signal defaults, lane rules, guard, every type crossing a street).
- **Full EditMode suite:** 671 passed, 0 failed.
- **Captures:** `Editor/Airfield/RoadJunctionReview.FromCommandLine -reviewOut <folder>` captures awkward junctions and
  each type from above and at an angle. Existing sets: `Captures/RoadJunctions/{before,after,types}`, plus
  `sandbox-streets*.png`.

## Left to do

### Gaps in what's built

1. **Junction control UI:** `SetControl` exists, but nothing lets the player switch a junction between lights and
   give-way. Suggested: click a junction with Select.
2. **Upgrading streets:** section deletion now uses the public graph. Upgrading in place (street to avenue)
   and lane-drop tapers between types aren't built. Different widths
   meet with a flare, but there are no lane-drop markings.
3. **Saving:** both player modes in `NewGame.unity` use the same save slot and full airport layout. Edits made
   directly to the legacy `Sandbox.unity` development fixture still follow normal Unity Play-mode lifetime.
4. **Shared Street tool:** the main game now uses `DevStreetTool` for both modes through the same
   `PublicRoadConstruction` transaction. Career charges `RoadType.pricePerMetre`; Sandbox builds free. The valley
   renderer consumes the same graph, and the outside connection advances the career objective. Do not add another
   career road tool. `CareerRoadTool` remains for old review/compatibility paths only.
5. **Local rendering implemented:** `PublicRoadChunks` now rebuilds changed sections and dependent junctions;
   distant meshes/renderers persist. Further airside chunking remains a separate optimization.
6. **Signals:** fixed timing, no protected left arrows, no vehicle-actuated phases.
7. **Shallow joins:** `MinJoinAngle` is 15°. Shallow joins make long paved wedges, and the user may prefer 25–30°.
8. **Signal prefab in builds:** the sandbox scene's `DevAirfieldTool` gets `trafficSignalPrefab` and
   `roadKerbMaterial` from `AirfieldSetup`, or by asset path in the editor only. A player build of an old scene would
   have no signals.
9. **Airside roads:** service roads are still one 7 m type.

### Steps 3–5: implemented 3D construction

Shared horizontal curves, smooth height profiles, raised/lowered drawing, automatic Ground/Bridge/Tunnel
sections, common structural meshes and an underground cutaway are now implemented for landside and airside.
The exact detached proposal supplies preview and commit. Airside underpasses preserve aircraft pavement and
keep service routing at its own height. See the delivery notes below and the public-road system doc.

Next road-dependent gameplay: multi-level terminal kerbsides, parking/bus/taxi stops and civilian lane traffic.

## Working notes

- **Branch:** all of this is uncommitted on `main`, alongside about 280 other uncommitted files from earlier work.
  Don't commit unless asked, and leave out the Co-Authored-By line.
- **Editor-open workaround:** the user often has the Unity editor open, which locks batchmode. Run tests and captures
  in an APFS clone (`cp -cR aircity <scratch>/aircity`, delete its `Temp/`, `rsync` `Assets/AirCity/Scripts` and
  `Tests` across before each run).
- **Don't edit scripts while a batch test run is going.** Unity recompiles mid-run and reports bogus failures
  (`TargetException`, play-mode sandbox tests).
- **Hashing:** float noise must not count as change. Quantise to millimetres, as `AirportBridges.Signature` and
  `StandEquipmentLayout.GroupKey` do.
- **User preferences:**
  - Picky about looks: check by capture before calling something done.
  - Hardcode constants; no new env vars or capacity caps.
  - Cities: Skylines-inspired, never copied.
  - Update the system doc in `Docs/` with each change.

## Shared-game integration verification (2026-11-14)

- Final focused suite: **74 passed, 0 failed**, covering game modes, legacy career saves/charters, shared road
  transactions, full airport/reference restoration, embedded bridge finishes, terrain and deicing accounts.
  Report: `Logs/unified-focused-final.xml`.
- Native Play review: passed actual mouse-driven Street placement and runway extension, mode switching without
  replacing the airport/camera, free Sandbox construction, full save/reload with runway/taxiway links, and normal
  and narrow HUD captures. `SharedGamePlayReview.Run` is repeatable in an isolated project; it uses its own save
  slot. Report: `Logs/shared-game-play-review.txt`; captures: `Captures/SharedGame/`.
- Broad suite on the initial integration: 671 passed, 6 skipped; nine Play tests initially rejected a setup log.
  Removed the log and limited automatic asset setup to scenes containing the shared game session. Eight native
  Play regressions passed on retry. The remaining baggage-delivery scene test rejects Unity's warning
  `Releasing render texture that is set to be RenderTexture.active!` at scene opening. The same test fails with
  the identical warning against the pre-integration source (`Logs/unified-baggage-baseline.xml`); it is not a
  new gameplay failure. Do not report the whole suite as green.

## Terrain-aware pavement follow-up (2026-11-14)

- Taxiway/service-road placement now uses actual terrain and engineered join heights, quotes its cut/fill,
  grades beneath complete bends/junctions and preserves elevations in full saves. New Game omits taxiway
  grass bands; minor formation corrections retain natural soil. Preview and click share the same refusal.
- Street previews use local forest/prop queries and cache unchanged inputs. Their map label and cut/fill
  overlay show the proposed elevation. The isolated 220,000-tree check measured 5.9 ms moving / 0.004 ms
  stationary tool work; click-side network rebuild optimization remains separate work.
- Validation: broad 476 passed / one external-download case skipped, final focused 58 passed, 480 unique
  passing cases across both runs. Actual mouse placement, exact costs, rendered support and save/load passed.
  Reports: `Logs/airside-{regression,final-targeted}.xml`; captures: `Captures/NewGame/AirsideBuild`.

## Surface ownership refactor (2026-11-15)

`World/BuiltSurfaceMap` now owns terrain coverage, persistent airport formations and whole-model vegetation
exclusion from the actual committed geometry. This fixes terrain/grass burial after later grading and LOD
changes. Road transactions clear vegetation, and visible forest/prop buffers update immediately. Terrain
forward/add/base/depth/normal/shadow passes share the exact footprint; elevated road decks are excluded.
The map is rebuilt from saved airport/road data. See `Docs/systems/built-surfaces.md` for the contracts and
repeatable native graphics/CPU verification.


## 2026-11-15: curve previews and road finish

- Service roads/taxiways and Streets preview their complete detached candidate network, including existing
  approaches re-trimmed into bends. Render-only terrain ownership and temporary renderer hiding avoid old
  square stubs showing through; cancellation restores committed visuals without a gameplay edit.
- Road outlines follow actual opaque boundaries and omit internal seams. Placement labels show only the
  total price, with grading still included; refusals remain visible before clicking.
- Rounded terminal corners, matching edge paint and returning kerbs/shoulders replace square road ends.
  Regional outside connections and elevated ends retain their straight interfaces.
- Road-specific asphalt has broad discoloration and fine grain. Junction arrows use clean analytic heads,
  curved turn stems and lane-limited sizes. Actual pavement-triangle projection fixes paint sinking into
  graded approaches, including its pixel-expanded corners.


## 2026-11-15: placement speed and terrain halos

- Indexed elevation links/junctions and cached protected spans remove repeated scene-wide support searches.
  Burst computes nearest formation targets; nature checks use sparse footprint buckets.
- Preview terrain buffers/material blocks retain capacity and reuse occupied cells. Runway ghosts do not
  invalidate committed layout caches. Local commits refresh their actual changed bounds.
- Automatic construction no longer writes dirt disturbance or repaints the surrounding ground. Terrain
  asphalt weight is always zero; actual road meshes alone supply paving, repairing old blurred end halos
  on load. Legacy disturbance no longer tints the terrain palette.
- The controlled 17-road native-editor benchmark improved moving road CPU work 144.34 → 16.80 ms and runway
  CPU work 16.33 → 2.40 ms. Reports: `Logs/placement-performance-{before,after}.json`.

Public Street cursor work is also fixed: cached unchanged regional roads plus rebuilt local approaches and
junctions reduced the warm fixture from 128.97 ms to 9.58 ms. Partition-vs-full geometry regressions cover
split roads, graded junction markings and a moving endpoint; native mouse review covers placement/cancel and
save/reload. Avoid restoring full-region geometry/outline rebuilds on each cursor movement.


## 2026-11-15: 3D road foundation delivered

- Shared path geometry, exact draft-to-commit construction plans and local public-road chunks.
- Curves, elevation controls, automatic Ground/Bridge/Tunnel spans, supported decks and lined tunnel mouths.
- Airside service-road bridges/underpasses with separate-height graph crossings and preserved aircraft surfaces.
- Temporary underground cutaway/grid, elevation-aware picking and public-road section deletion.
- Baggage, deicing and maintenance paths carry their selected road's elevation. Dynamic actor boxes and route
  blocking distinguish decks; aircraft do not stop for vehicles below a sufficiently deep underpass.
- Legacy straight saves retain their original contract; both modes use the same commands and save data.

Still future: road upgrades, undo/redo UI/history, multi-level terminal destinations and a lane-level civilian
traffic simulation. Those must build on the existing paths/plans rather than add another road implementation.
See [public roads](systems/public-roads.md#shared-3d-construction-2026-11-15) for controls, validation and ownership.
