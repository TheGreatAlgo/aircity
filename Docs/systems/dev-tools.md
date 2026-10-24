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

It contains one parked aircraft per livery on the apron, the sandbox airport (see
[airfield.md](airfield.md)) and the depot. The camera carries `DevOrbitCamera`, `DevSandboxControls`,
`DevAirfieldTool` and `DevToolbar` (the toolbar adds itself if a scene's camera lacks it).

### HUD (`DevToolbar`, `DevGui`)

An IMGUI HUD, dev only (not game UI):

- **Bottom:** the tool buttons (Select, Runway, Taxiway, Road, Depot, then Delete).
- **Strip above the tools:** what the current tool needs (surface, width, taxiway lights, a depot's fleet). In
  Select it shows the selection's actions instead (surface, width, lighting, wear / repair, plough / mow / verges,
  fleet, taxi / dirt for an aircraft, and Delete).
- **Right sidebar:** weather, time speed, day / night, snowfall, grass growth, plough / mow everything,
  auto-dispatch, and the aircraft lights and engines.
- **Top left:** the airfield panel (`DevAirfieldTool`).

Every button shows its key, and hovering shows a tip. Every key below still works.

How it's built:

- Fonts and sizes scale with the screen height (`DevGui.Scale`: 1 up to 1000 px tall, at most 2.5), so text stays
  sharp on Retina.
- Panels mark their rects. While the pointer is over one (`DevGui.PointerOverHud`), clicks and scrolls don't reach
  the world tools or the camera.

| Keys | What |
|---|---|
| Right-drag / middle-drag or WASD / scroll / Q E / F | orbit / pan / zoom / rotate / re-centre (`DevOrbitCamera`). Each wheel notch zooms 25% (`zoomStep`), eased (`zoomSharpness`), towards the point under the cursor (`zoomToCursor`) |
| Click | Select tool: select an aircraft (`DevSandboxControls`), or a runway, taxiway / road section, depot or apron (`DevAirfieldTool`). The selection is outlined in amber and hover in white |
| Delete / Backspace | remove the selection: the aircraft or the airfield one, whichever was selected last (`DevToolbar`) |
| X, click (Shift+click) | delete tool: click anything to remove it (it's outlined in red first). Shift+click takes a taxiway's or road's whole run. Aircraft too |
| Esc | end the line being laid, else back to Select, else deselect |
| H | hide / show the HUD |
| N, + / − | day / night, dusk |
| 1 2 3 4 | beacons, strobes, landing + taxi lights, logo lights |
| R, T, G | engines, taxi the selected aircraft, dirt on it |
| B | next build tool: select / runway / taxiway / road / depot |
| Runway tool: drag (Alt = free) | lay a runway; snaps to 10° and 50 m |
| Taxiway / road tool: click, right-click / Esc | add points; end the line. The road tool lays 7 m service roads; a click within 20 m of a point 12 m in front of a depot's doors snaps there |
| Depot tool: drag, click | drag from the door front the way the doors face (snaps to 10°) to build a depot; click one to select it |
| V, [ ], L | surface, width, lighting (taxiway tool: all taxiway lights). V / [ ] also change a selected runway |
| C | weather: dry / rain / snowing |
| K / J | wear / repair the selected runway |
| P / M / Shift+M | plough / mow / mow the verges (the selected runway or taxiway / road run; with the taxiway or road tool, all taxiways and roads; with nothing selected, everything) |
| , / . (Shift: mowers) | fewer / more ploughs in the last selected depot (else the first) |
| Y | time speed 1× / 5× / 20× / 60× |
| O / U | a 2 cm snowfall at once / grass strips and verges grow 6 cm at once |

With a runway selected, the panel lists each aircraft type as OK or with the reason it can't use the runway. It
also shows the runway's snow, grass, verges and work progress. A selected taxiway or road shows its size, state
and whether it's closed. The panel always lists every depot and its teams, with what each team is doing or waiting
for.

What deleting does:

- A taxiway or road section is the part between two junctions. `TaxiwayNetwork.RemoveEdges` drops nodes left with
  no sections, then joins up what the removed part had split: two alike sections running straight on through a
  plain node become one again.
- A team whose runway or taxiways are removed gives the job up and drives home (`VehicleTeam.Recall`, checked in
  `AirportMaintenance.Simulate`). If one of its vehicles is still pulling out of the yard, the team waits until all
  of them are out first.
- Everything the tools change happens in Play mode, so it all comes back when you stop playing.

A depot built when the airport has none copies its vehicle types and prefabs from the tool's asset fields (which
the airfield bootstrap fills in); otherwise from the first depot.

`DevAirfieldTool.Active` stops `DevSandboxControls` from taking left clicks while a tool other than Select is on.
`DevAirfieldTool.Deleting` makes them remove aircraft instead.
