# Dev tools, commands and verification

How to build, test and look at things without the GUI, and the sandbox controls. Nothing here is game UI.

## Environment

| | |
|---|---|
| Unity | 6000.6.7f1 (URP) at `/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity` |
| Blender | 5.2 at `/Applications/Blender.app/Contents/MacOS/Blender` (aircraft and vehicle pipelines) |
| Python 3 | with numpy, Pillow and scipy (airfield textures) |
| Repo | `~/Documents/GitHub/aircity`, GitHub `TheGreatAlgo/aircity`, which is **public**, uses Git LFS, and has **no commits yet** |
| Git rules | Never commit or push unless the user asks. No Co-Authored-By lines unless asked. |

## Unity batch commands (from the repo root)

The sandbox's **BAGGAGE** section shows the selected station and its queues; **View station** frames its apron.
Expand **STATION SETUP** to place endpoints/loader parking, set headings and demand, add stations and assign
stands. **Stand staging** parks equipment on the cargo side of the stand; **Stage at Stand** selects its home
when the station serves several stands. Turn staging off to place a manual loader bay. Stand layout edits
recalculate idle equipment and its paint. Setup waits until all bulk/container work on the served stands finishes.
Select an aircraft for handling counts, screening release, cancellation and retry. **Send to reclaim**
frees arrivals bay capacity while retaining baggage records. **AirCity > Vehicles > Enable Sandbox Baggage**
adds the initial service stand to an existing sandbox. See [baggage](baggage.md#sandbox-handling-and-larger-manifests).

The optional container example’s **CONTAINERS > View station** frames its widebody service area. **CONTAINER STATION SETUP**
provides aircraft/hold compatibility, demand, station creation, endpoint placement/headings and stand assignment.
Setup starts folded; long sidebar panels scroll so their lower controls remain accessible.
The panel and aircraft inspector expose screening, access blocking, cancellation/retry and arrivals/reclaim.
**AirCity > Vehicles > Enable Sandbox Containers** installs the example while preserving the existing airport;
`ContainerSandboxSetup.FromCommandLine` validates automatic container/bulk handling through departure and
captures `Captures/ContainerSandbox/loading.png`. See [sandbox containers](baggage.md#main-sandbox-container-handling).

**AirCity > Vehicles > Create Container Turnaround Review** creates the widebody LD3 review scene at
`Assets/AirCity/Scenes/ContainerTurnaround.unity`. Enter Play for container/bulk handling, pause/speed,
blocked-access and cancellation/retry controls. Its batch entry point is
`AirCity.EditorTools.Vehicles.ContainerTurnaroundSetup.FromCommandLine`; it captures both loader platforms
and verifies baggage conservation and departure clearance. See [container handling](baggage.md#widebody-container-turnaround).

```sh
U=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity

# compile + all EditMode tests (exit 2 = failures; grep Logs/tests.log for "error CS" if it doesn't compile)
$U -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile Logs/tests.log

# aircraft prefabs, liveries, sandbox scene
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.Bootstrap -logFile Logs/bootstrap.log

# airfield materials, vehicle prefabs, sandbox airport + depot (rebuilds the Airport in Sandbox.unity)
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Airfield.AirfieldSetup.Bootstrap -logFile Logs/airfield.log

# render a scene to PNG
$U -batchmode -projectPath . -executeMethod AirCity.EditorTools.Dev.SceneCapture.FromCommandLine \
   -captureScene Assets/AirCity/Scenes/Sandbox.unity -captureOut Captures/sandbox.png -logFile Logs/capture.log
#   extra: -captureView "px,py,pz:tx,ty,tz" (camera position : look-at target, repeatable), -captureSize 1600x900,
#          -captureLod 2, -captureNight, -captureSelect "<aircraft name>"
```

**Summarising test results:**

```sh
python3 -c "import xml.etree.ElementTree as E;t=E.parse('Logs/editmode.xml').getroot();print(t.get('total'),t.get('passed'),t.get('failed'));[print('FAIL',c.get('name'),c.find('.//message').text[:300]) for c in t.iter('test-case') if c.get('result')!='Passed']"
```

**Batch-mode rules:**

- The editor must not be open on the project; batch mode needs the project lock.
- Wrap runs in `timeout`.
- `-executeMethod` without `-quit` idles forever if the method isn't found. Write the arguments inline (zsh passes
  an unquoted `$ARGS` as one argument).
- Shader compile errors don't fail batch mode; they show as magenta in captures. Grep the log for `Shader error`.

**Custom captures** (vehicles at work, a particular angle):

1. Write a temporary editor script under `Scripts/Editor/Dev/` that opens the scene, sets up state (for example
   `airport.Simulate(...)`, `depot.Plough(...)`, then `depot.UpdateVehicles(dt)` to place the vehicles), and calls
   `SceneCapture.Capture(cam, "Captures/x.png", 1600, 900)`.
2. Run it with `-executeMethod`.
3. Read the PNGs.
4. Delete the script afterwards.

**The IMGUI HUD** isn't in camera captures. To see it:

1. Write a temporary `[InitializeOnLoad]` editor script. Its `-executeMethod` entry opens the scene and calls
   `EditorApplication.EnterPlaymode()`. An `EditorApplication.update` hook then maximizes the GameView
   (`EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).maximized = true`), sets up state,
   and calls `ScreenCapture.CaptureScreenshot` a few frames apart. Finally it calls `EditorApplication.Exit(0)`.
2. Run the editor without `-batchmode`. This opens a window on screen.

`Captures/` is gitignored.

## Menu items

| Menu | Does |
|---|---|
| AirCity ▸ Aircraft ▸ Create Missing Prefabs and Example Liveries | aircraft prefabs and example liveries |
| AirCity ▸ Aircraft ▸ Render Thumbnails | menu thumbnails |
| AirCity ▸ Dev ▸ Rebuild Sandbox Scene | recreates Sandbox.unity from scratch, with a plain grey apron and no airport: run Add Sandbox Airport afterwards |
| AirCity ▸ Airfield ▸ Create Materials | airfield materials (missing ones only) |
| AirCity ▸ Airfield ▸ Add Sandbox Airport | rebuilds the sandbox airport |
| AirCity ▸ Vehicles ▸ Create Missing Prefabs | vehicle and depot prefabs |

## Sandbox scene (Assets/AirCity/Scenes/Sandbox.unity)

It contains one parked aircraft per livery on the apron (widebodies: one row of three liveries), the sandbox airport (see
[airfield.md](airfield.md)) with four jets parked on its stands, and the depot (with two small tugs and a large
one). The camera carries `DevOrbitCamera`, `DevSandboxControls`,
`DevAirfieldTool` and `DevToolbar` (the toolbar adds itself if a scene's camera lacks it).

### HUD (`DevToolbar`, `DevGui`)

An IMGUI HUD, dev only (not game UI):

- **Bottom (2026-11-02):** the tools as square icon buttons in groups: Select | Runway, Taxiway, Road, Apron | Lights |
  Terminal, Stand, Bridge, Gates | De-icing bay, Depot | Delete. Hover a button for its name and what it does; clicking the tool
  that's on again goes back to Select. The icons come from `ArtSource/UI/icons.py`. On a narrow screen the buttons get smaller so the
  bar fits.
- **Tray, over the tool that's on:** a card for each kind it can build, side by side in captioned groups, showing what
  they look like. Runway: surface (a patch of the real ground texture) and width (a strip of the chosen surface drawn
  to scale, with its size code). Taxiway: the same. Road and apron: surface. Terminal: floors
  (1, 2, 3, 4, 6, 8; `[ ]` gives any up to 12). De-icing bay: size code, surface (paved only) and Turn round. The
  chosen card is amber; the keys still work (V, `[ ]`, Shift+Z) and the tray follows them. Lights: what the lights tool
  puts down (taxiway edge; runway basic, "night": edge, threshold and end with PAPI; runway precision, "fog": plus
  centreline and approach lights, its picture fading into fog) and Remove. Stand, Bridge and Gates
  open the gate designer instead, and Select, Depot and Delete have no tray. The tools' inspector keeps only its hints and problems.
- **Inspector, top right (2026-10-31):** a column of labelled rows for the current tool (its hints and problems, a
  depot's fleet; a stand's code A–F, surface, angle, exit and light aircraft; the tray has the rest) or, in Select, for the
  selection (surface, width, lighting, wear / repair, plough / mow / verges, fleet, a stand's settings and bridges;
  for an aircraft depart / push back / park / taxi / dirt, or its progress), Delete at the bottom, and its hints and
  problems wrapped under it. It replaced a single strip above the tools that ran off screen for stands.
- **Pop-outs:** non-gate settings with many choices (such as Surface) open beside the inspector. Gates use one
  dedicated workspace: Stand/Bridge open placement, and clicking a stand or bridge opens editing. The same panel
  contains model/sections, finishes, parking and removal; there is no separate stand-owned bridge menu.
- **Sidebar, under the inspector** (beside it, to its left, when the two won't fit one above the other): weather,
  time speed, day / night, snowfall, grass growth, plough / mow everything, auto-dispatch, traffic, and the aircraft
  lights and engines. Click a section's caption to fold it.
- **Top left:** the airfield panel (`DevAirfieldTool`).

Buttons show their keys where available, and hovering shows a tip. Keyboard shortcuts are suspended while typing
in a gate designer text field.

How it's built:

- Fonts and sizes scale with the screen height (`DevGui.Scale`: 1 up to 1000 px tall, at most 2.5), so text stays
  sharp on Retina.
- Panels mark their rects. While the pointer is over one (`DevGui.PointerOverHud`), clicks and scrolls don't reach
  the world tools or the camera.

| Keys | What |
|---|---|
| Right-drag / middle-drag or WASD / scroll / Q E / F | orbit / pan / zoom / rotate / re-centre (`DevOrbitCamera`). Each wheel notch zooms 25% (`zoomStep`), eased (`zoomSharpness`), towards the point under the cursor (`zoomToCursor`) |
| Click | Select tool: select an aircraft (`DevSandboxControls`), or a runway, taxiway / road section, depot, stand / de-icing bay or apron (`DevAirfieldTool`). The selection is outlined in amber and hover in white |
| Delete / Backspace | remove the selection: the aircraft or the airfield one, whichever was selected last (`DevToolbar`) |
| X, click (Shift+click) | delete tool: click anything to remove it (it's outlined in red first). Shift+click takes a taxiway's or road's whole run. Aircraft too |
| Esc | end the line being laid, else back to Select, else deselect |
| H | hide / show the HUD |
| N, + / − | day / night, dusk |
| 1 2 3 4 | beacons, strobes, landing + taxi lights, logo lights |
| R, T, G | engines, taxi the selected aircraft, dirt on it |
| Enter (Shift+Enter) | push the selected aircraft back off its push-back stand: a tug comes from the depot (Shift: face the other way along the taxiway). If it can't, the inspector says why. **Park** (inspector only) puts it on the nearest free stand that takes it |
| Space / "Depart" | the selected aircraft departs: pushed back if it needs to be, taxis to the runway in use and takes off (`DevTrafficControls`). A selected aircraft's route is drawn on the ground (a parked one's as it would go now); the inspector shows its flight's status. Sidebar TRAFFIC: Land per type, Auto traffic, wind. See [traffic.md](traffic.md) |
| B | next build tool: select / runway / taxiway / road / apron / terminal / depot / stand / de-icing bay / bridge |
| Placing (all tools) | free by default. A point sticks to something already built only within about 14 pixels (never under 1.5 m), or anywhere on the pavement of a line of its own kind. A ring shows where the next point goes before any click: cyan where it sticks (the inspector says "joins …"), white where it's free, red where it can't go. **Hold Shift for steps:** lines turn in 10° steps counted from what they start on or near (a taxiway, road, runway, apron or stand edge; else the line's last stretch; else the nearest runway; else north), so they run parallel, square or 10°, 20° … off it, and come in whole 5 m |
| Runway tool: click, click (or drag); right-click / Esc | like the line tools: a ring shows where the threshold goes. Click it, then the other threshold, or drag from one to the other. The runway is built as the cursor moves, so it shows as it will look (its surface and width can still be changed in the tray), and the inspector gives its designation and length. Shorter than 100 m, the ring turns red and it won't end there. Shift: 10° from north and 50 m steps. Right-click (not a right-drag, which turns the camera) or Esc drops it |
| Taxiway / road tool: click, right-click / Esc | add points; end the line (a right-drag turns the camera and leaves the line going). The next stretch is built as it will look (surface, paint, verges; `TaxiwayPreview`, never part of the airport), from the last point to the cursor, with a faint outline that turns red where it can't go. It stays while the pointer is over the HUD, so a width or surface picked in the tray shows on it. A taxiway sticks to taxiways and runways and joins a road only when clicked on the road's pavement; a road sticks to roads and joins a taxiway or runway only when clicked on it (so a road laid beside a taxiway stays beside it). The road tool lays 7 m service roads (only paint across paved aprons); it also sticks to a point 12 m in front of a depot's doors and to the head of a parking stand (edge on the pad's edge, running on half a road width past each corner; coming at it end-on, onto the head). A line that would cross a stand turns red and won't build |
| Apron tool: click, drag, right-click / Esc | click corners; they stick to the corners and edges of aprons and stand pads, so a new apron joins an existing one edge to edge. Click the first corner again or right-click to finish; Esc drops it. Dragging from the first press lays a rectangle lined up with what it starts on (an apron or stand edge, a taxiway or road), else the nearest runway. The outline is green, or red with the reason in the inspector (edges crossing, too small, overlapping an apron of a surface it can't overlap). V / the surface chips pick the surface, also for a selected apron |
| Terminal tool: click, drag, right-click / Esc | a terminal's footprint, drawn like an apron (corners stick to aprons' and stands' corners and edges; drag for a rectangle). It may not stand on a runway, taxiway, road or stand pad, nor overlap another terminal (red, with the reason). Click a terminal to select it. [ ] (or the Floors stepper) sets the floors, 1–4, also for the selected one |
| Shift+scroll on a selected terminal (Alt: fine) | the wall nearest the cursor is outlined in cyan; scrolling bows it out or in, 2 m a notch (Alt 0.5 m), up to a half circle. A bulge that would hit something isn't made (the inspector says why) |
| Stand / Bridge toolbar buttons; click a stand or bridge | opens one Gate designer workspace for parking, bridge models/sections, glass finishes, world handles, aircraft stops and removal. Choose bridge settings before placement; a successful placement selects that bridge for editing. Click a bridge model directly without first selecting its stand. Delete in the Jetbridges page removes only the current bridge |
| Select tool: click a rotunda ring | picks that bridge: the inspector shows its door (Left / Right, Main / Upper, and which, counted from the front). Changing it places the bridge again where it works for the new door; a door no type at the stand has, or no place that works, is refused with the reason. **Tunnel Long / Short** gives it 13 m sections (14–36 m) or 8.5 m ones (9.5–22.5 m, as on a head, close to the aircraft); the inspector says what's wrong if it no longer reaches |
| Bridge placement in Gate designer: click, click, right-click / Esc | choose the model and finish, click a terminal/concourse wall, then the bridge base. The full model and blue preview aircraft show the fit. Green/red uses the same complete family-specific check as the click. A refused click keeps the root and red model; Escape/right-click cancels. Successful placement opens editing, with the same model/finish controls and world handles. Build > Concourses places a concourse using wall, near-end and far-end clicks |
| Select tool: drag a rotunda ring | move that bridge (`AirportBridges.MoveBridge`); its link keeps leaving the terminal where it did while that still works, else follows to the nearest terminal wall within 60 m, and it moves onto the stand's head if the head is nearer. A spot where the rotunda or link would stand in the aircraft's way, on pavement, on the next stand or through a terminal is refused (it stays put); other problems are shown in the inspector |
| Select tool: drag a link's square | a square on the terminal wall marks where each link leaves the building (a bridge's, and a head's main link's). Drag it along the walls (it sticks to the nearest one); the rotunda or head stays put (`AirportBridges.MoveRoot`). Refused where the link would be over 60 m, run through a terminal or over where the aircraft parks, or its columns couldn't keep off pavement |
| Selected bridge: Delete | the inspector's **Delete L1** (or Del / Backspace) takes just the clicked bridge off its stand (its head stays). With no bridge picked, **Delete head** takes the stand's head and the bridges on it. Click the stand away from its rings to drop the bridge pick, and Delete takes the stand |
| The thin lines from a bridge | one per aircraft type the stand takes that has the bridge's door: where its cab would have to reach to dock that type. Green: it docks. Red: it can't (the line runs to where it would need to reach, so a red line far past the stand means that door is out of reach); the panel lists the reason per type |
| Select tool: drag a bridge head | a selected stand's head shows a ring at each end. Drag a ring to move that end (the head turns and stretches about the other, 10–60 m), or the head itself to move it whole. Its bridges go with it and its main link keeps leaving the terminal where it did while that works, else follows to the nearest terminal wall (`AirportBridges.MoveHead`). Where the head, its link or a rotunda would stand where the aircraft parks, on pavement, on the next stand or in a terminal it stops where it last stood right; other problems show in the inspector |
| Depot tool: drag, click | drag from the door front the way the doors face (Shift: 10° steps from what it's beside) to build a depot; click one to select it |
| Stand / de-ice tool: click | a ghost pad follows the cursor. Within a stand's depth of a taxiway, or of another stand's front line (the taxilane still to come), it opens onto that line, its centreline under the cursor, pulled against a stand beside it within 40% of its width. Anywhere else it stands free, centred on the cursor. **Hold Ctrl (Mac: or Cmd)** and it stands free wherever the cursor is, never pulled onto a taxiway or against a stand; Ctrl+press and drag turns it to point at the cursor, any angle, and it is built where the button comes up. Green fits, red doesn't (the inspector says why). A stand without a taxiway shows its front line dashed in amber (also while laying taxiways), and the taxiway tool sticks to that line and says "connects it"; a taxiway can run along it but not across the pad. Click builds it; click a stand to select it |
| De-ice tool: click | the same for a drive-through de-icing bay, centred on the taxiway under the cursor |
| [ ], V, Z / Shift+Z, I | stand / bay tools and a selected stand: size code down / up, surface (grass and dirt for A / B only, bays always paved), angle (Z: the next of 90 / 60 / 45 / 30 down) / lean the other way (bays: turn round), push-back / taxi-out ("No small" chip: turn small aircraft away). A change that wouldn't fit is undone. Size code and push-back / taxi-out apply to new stands only: a placed stand or bay keeps the ones it was built with (the inspector shows them, `DevAirfieldTool.StandFixed`); delete and re-place it to change them |
| Shift+scroll (Alt: fine) | the stand being placed, or the selected one: turn it 5° a notch (Alt: 1°). Against a taxiway or front line: its angle to it, 25° to 155°, so past 90° it leans the other way. Free (and unconnected selected ones): all the way round, about the pad's middle. The camera doesn't zoom meanwhile. The inspector's angle stepper does the same; Lean (Shift+Z) mirrors it |
| V, [ ], L | surface, width; L: a selected runway's lighting (none / basic / precision for fog). V / [ ] also change a selected runway |
| Lights tool: click / drag (Shift = whole run) | lights are an upgrade placed by hand on what's already built, so only the parts of the airfield that should work at night need them; new taxiways are laid unlit. Pick a kind in the tray. **Taxiway edge:** click a taxiway section to light it, or hold and drag along sections to light each one passed; Shift lights the section's whole run (roads left out). **Runway basic / precision:** click a runway to give it that lighting (or change its level). Basic is for night; precision adds centreline and approach lights for fog and low cloud (that weather isn't in yet, so for now it works like basic). **Remove:** takes the lights out of sections and runways. The section or runway under the cursor is outlined (blue for taxiway lights, warm white for a runway's, red to remove or where it can't go), and the inspector says what a click does ("light taxiway section, 150 m", "Runway 09/27: precision (fog) → basic") or why not (roads, dirt and grass can't be lit). It also gives how much is lit (km of taxiway, runways) and has **Light all taxiways** / **Clear all** |
| C | weather: dry / rain / snowing |
| K / J | wear / repair the selected runway |
| P / M / Shift+M | plough / mow / mow the verges (the selected runway or taxiway / road run; with the taxiway or road tool, all taxiways and roads; with nothing selected, everything) |
| , / . (Shift: mowers) | fewer / more ploughs in the last selected depot (else the first); the depot's inspector also has small / large tug steppers |
| Y | time speed 1× / 5× / 20× / 60× |
| O / U | a 2 cm snowfall at once / grass strips and verges grow 6 cm at once |

With a runway selected, the panel lists each aircraft type as OK or with the reason it can't use the runway. A
selected stand shows its code limits, pad size, angle, exit, the taxiway it opens onto and which aircraft types it
takes. It
also shows the runway's snow, grass, verges and work progress. A selected taxiway or road shows its size, state
and whether it's closed. The panel always lists every depot and its teams, with what each team is doing or waiting
for.

What deleting does:

- A taxiway or road section is the part between two junctions. `TaxiwayNetwork.RemoveEdges` drops nodes left with
  no sections, then joins up what the removed part had split: two alike sections running straight on through a
  plain node become one again. Stands left with no taxiway under them go too.
- A team whose runway or taxiways are removed gives the job up and drives home (`VehicleTeam.Recall`, checked in
  `AirportMaintenance.Simulate`). If one of its vehicles is still pulling out of the yard, the team waits until all
  of them are out first.
- Everything the tools change happens in Play mode, so it all comes back when you stop playing. Gate designer
  presets saved to disk remain available next session.

A depot built when the airport has none copies its vehicle types and prefabs from the tool's asset fields (which
the airfield bootstrap fills in); otherwise from the first depot.

`DevAirfieldTool.Active` stops `DevSandboxControls` from taking left clicks while a tool other than Select is on.
`DevAirfieldTool.Deleting` makes them remove aircraft instead.

## Gate Designer

In Play mode, click **Gates** in the bottom toolbar, or click an existing stand/bridge. The runtime panel
has Build / Aircraft / Appearance / Saved gates views for models, placement, aircraft service and reusable layouts,
aircraft-specific stops, facade styles, isolated aircraft previews and persistent mirrored gate presets. It pauses the
airport while open by default. Numeric settings use steppers or Enter/focus loss. Corridor shaping uses world handles. Door assignments and movement stay automatic.
An aircraft preview appears automatically at the selected gate's parking stop. The Aircraft page can change or hide
it. The Stand and Bridge toolbar buttons open this same workspace. Click a bridge model directly to select, edit or remove it; the owning stand is resolved automatically. Build > Parking edits or places stands, while Build > Jetbridges exposes model/section and finish choices before placement, even before stands exist. The old stand bridge pop-out has been removed. The Bridge placement tool also shows a blue temporary aircraft labelled PREVIEW AIRCRAFT and a full bridge model, tinted green/red using the
same complete placement decision as the click (including door reach and moving clearance). A refused click keeps
the connection selected; move the cursor to adjust it, or right-click/Escape to cancel.

**Build > Concourses** offers Building / Elevated forms. **Build > Terminal** edits storey heights and boarding
levels. **Appearance** provides four coordinated glass facades (Classic, Minimal, Framed, Colonnade), plus Clear/Blue/Green/Smoke glass tints. Separate **Facade angle** and **Roof shape** pages expose wall lean and Flat/Wave/Scalloped forms with rise, direction and canopy extension. Column gap moves the architectural supports away from the glass; the roof covers them automatically. Moving jetbridges offer glass or enclosed panels.
Blue world handles edit bridge bases, connections, concourse ends and corridor corners: click + to insert a corner,
Shift-drag for height, Ctrl/Cmd for 0.5 m steps, right-click/Delete to remove a corner, Escape to cancel a gesture.
Alt reveals column handles. Supports and bend coordinates are absent from the palette.
Selecting a terminal exposes **Design building** directly; no stand is required for terminal editing.

`DevToolbar` owns `DevGateDesigner`; the existing Sandbox camera needs no additional component. `GateAircraftPreview`
uses inactive models supplied by `DevTrafficControls`, so previews remain available after the original aircraft
departs. The Unity editor's **AirCity > Airfield > Gate Designer** remains available for scene authoring and project
asset presets. See [gates.md](gates.md).

`StandStagingReview.FromCommandLine` renders the real staged fleets and baggage working areas in
`Captures/StandStaging/`, including both airport orientations and a live narrowbody loading view. It leaves
saved scene transforms unchanged. `StandMarkingsReview.InstallFromCommandLine` only upgrades missing saved
station staging fields and review aircraft metadata.

`BaggageDepotSetup.InstallSandbox` migrates the main Sandbox to one baggage example and a Small depot.
`BaggageDepotReview.Run` captures its real collection/unloading cycle and verifies departure and conservation;
results and animation frames are under `Logs/baggage-depot-*` and `Captures/BaggageDepot/`. Run these in an
isolated project copy while the editor is open. The depot tests include a 50-stand queue/workforce test; this
does not claim 50 simultaneous turnarounds or a rendering benchmark.
