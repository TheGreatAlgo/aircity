# AirCity

An airport-building game for PC and Mac (Unity 6, URP).

## Getting started

1. Install **Unity 6000.6.7f1** through Unity Hub (see `ProjectSettings/ProjectVersion.txt`), plus **Git LFS**.
2. `git lfs install && git clone https://github.com/TheGreatAlgo/aircity.git`
3. Open the folder in Unity Hub, then open `Assets/AirCity/Scenes/Sandbox.unity` and press Play.
   - **Toolbar:** the tools are along the bottom (Select, Runway, Taxiway, Road, Depot, Stand, De-ice, Delete), with a strip above
     them for the tool's settings or the selection's actions. The world, maintenance and aircraft toggles are on the
     right. Every button shows its key, and the keys below all still work. **H** hides the HUD.
   - **Right-drag:** orbit · **Middle-drag / WASD:** pan · **Scroll:** zoom (towards the cursor) · **Q / E:** rotate ·
     **F:** re-centre
   - **Click:** select an aircraft, runway, taxiway / road section, depot, stand or apron · **Delete / Backspace:** remove the
     selection · **X:** delete tool (click anything to remove it; **Shift+click** removes a taxiway's whole run) ·
     **Esc:** back to Select · **G:** cycle the selected aircraft's dirt · **T:** taxi it
   - **N:** day / night · **+ / −:** dusk · **1:** beacons · **2:** strobes · **3:** landing + taxi lights ·
     **4:** logo lights · **R:** engines
   - Airfield (panel top left): **B:** next build tool (select / runway / taxiway / road / depot / stand / de-icing
     bay) · runway tool: **drag** a runway (snaps to 10° and 50 m, **Alt** for free) · taxiway and road tools:
     **click** points, **right-click** ends the line · depot tool: **drag** from the door front the way the doors
     face · stand tool: **click** beside a taxiway (it snaps to the stands beside it) · de-ice tool: **click** on a
     taxiway · **V:** surface · **[ / ]:** width (stands: size code A–F) · **Z / Shift+Z:** stand angle / lean ·
     **I:** push-back / taxi-out · **L:** lighting · **C:** weather (dry / rain / snow) · **K / J:** wear / repair ·
     With a runway or stand selected, the panel lists which aircraft can use it and why not.
   - Maintenance (the depot west of the apron): **P:** plough (selected runway / all) · **M:** mow (selected grass
     strip / all) · **Shift+M:** mow the selected runway's verges · **, / .:** fewer / more snow ploughs in the
     selected depot (**Shift:** mowers) · **Y:** time speed 1×–60× · **O:** a 2 cm snowfall at once · **U:** grass
     and verges grow 6 cm at once. Snow and long grass also send the teams out by themselves.
   - Push-back: select one of the jets parked nose-in on the stands south of the parallel taxiway and press
     **Enter** (or **Push back** on the strip; **Shift** to face the other way). A tug comes from the depot,
     picks up the nose wheel, pushes the jet round onto the taxiway and goes home. **Park** puts a selected
     aircraft back on the nearest free stand. The depot's fleet strip sets its small and large tugs.

## Layout

```
Assets/AirCity/
  Aircraft/<Model>/        generated aircraft: Models/*.fbx, Textures/, <Model>.aircraft (AircraftType), <Model>.prefab
  Aircraft/Shared/         registration font shared by all aircraft
  Airfield/                ground textures (generated, CC0 sources) and the airfield materials
  Vehicles/<Model>/        generated snow plough, mower, tugs, depot and control towers: Models/*.fbx, Textures/, <Model>.vehicle
                           (VehicleType), <Model>.prefab; Materials/ (light glow, spray)
  Liveries/                livery assets (airline paint schemes) and Logos/, usable on any aircraft type
  Shaders/                 aircraft outline / ground shadow / light glow; airfield surface / paint / light glow / fixture;
                           vehicle light glow
  Scenes/Sandbox.unity     dev scene: one aircraft per livery
  Scripts/Runtime/         AirCity.Runtime assembly  (namespace AirCity.*)
  Scripts/Editor/          AirCity.Editor assembly   (importers, postprocessors, tools)
  Tests/EditMode/          AirCity.Tests.EditMode
  Settings/                URP assets (PC + Mobile quality tiers from the template)
ArtSource/                 scripts that generate the art (Blender); see ArtSource/README.md
```

## Aircraft

Aircraft are generated from code (`ArtSource/Aircraft`), never hand-edited in Unity. Two types so far:

| Type | Real-world class | Span × length | ICAO code |
|---|---|---|---|
| `Cessna172` | light single (Cessna 172) | 11.1 × 8.3 m | A |
| `Narrowbody` | twinjet (A320 family) | 35.8 × 37.6 m | C |

Each comes with an `AircraftType` asset (imported from `<Model>.aircraft`) holding what gameplay needs, in the
aircraft's local space (origin on the ground under the main gear, +Z = nose, +X = right):
- size: `bounds`, `Wingspan`, `Length`, `SizeCode` (stands / taxiways are rated by it), `Wheelbase`, `MinTurnRadius`
- `wheels` and the steering pivot
- `points`: passenger / cargo doors, fuel, ground power, water, lavatory, tow bar, chocks, tie-downs (position,
  outward normal, opening size)
- `lights`: navigation, strobe, beacon, landing, taxi, logo

The prefab's root carries the components:

| Component | Does |
|---|---|
| `Aircraft` | the type, picking (`Aircraft.TryPick(ray, out a)`), hover / selected outline (`a.Highlight`) |
| `AircraftLiveryApplier` | livery, registration, dirt (`SetDirt(0..1)`) |
| `AircraftLights` | light switches; glows that stay visible at any zoom; real spot lights for landing / taxi lights up close. `AircraftLights.Glow = 1` at night |
| `AircraftGroundMotion` | wheel spin and nose-wheel steering: `Drive(speed, steering)` |
| `AircraftControlSurfaces` | elevator / aileron / rudder / flap deflection |
| `PropellerSpinner` (on props and fans) | `rpm` |

Every aircraft also has a soft contact shadow baked from its shape, so it stays grounded beyond the sun's
shadow distance (150 m on PC).

## Liveries
Every renderer uses at most three materials: `Body` (the livery), `Registration` (LOD0 only) and `Trim`, a small
palette texture that holds every flat colour (glass, metal, tyres, lights). The importer builds `Trim` from the
model's flat-colour materials.

A **livery** (`AircraftLivery` / `AircraftLiveryAsset`) is model-independent:
- a style: Classic, Swoosh, TwoTone, Trainer, or Custom
- a base colour and three zone colours
- a registration colour
- an optional tail logo (any RGBA texture; fitted into each type's fin)

Every aircraft type implements every style, so one airline livery fits its whole fleet.

```csharp
using AirCity.Aircraft;

var applier = aircraft.GetComponent<AircraftLiveryApplier>();
applier.SetLivery(airline.livery, "N208RA");   // body texture shared per (type, livery)
applier.SetRegistration("N55XY");               // cheap: rebuilds only the small patch
applier.SetDirt(0.6f);                          // cheap: a shared material per dirt step
StartCoroutine(AircraftLiveryCompositor.PrewarmRoutine(type, allLiveries));   // behind a loading screen
```

Measured in the tests (Apple M2, editor):

| Measurement | Result |
|---|---|
| New livery | ~50 ms, 2.7 MB GPU (per aircraft type) |
| New aircraft (registration patch) | ~15 ms, 0.3 MB GPU |
| 100 aircraft in 10 liveries | ~4–5 s to paint in total, ~55 MB GPU |

Generated textures get mips and BC1 compression from `AirCity.Rendering.Bc1TextureBuilder` (Burst), which is
~3x faster than `Texture2D.Compress` at the same quality.

### Level of detail

Each aircraft has three LODs, switched by screen size (`LodHeights` in `AircraftAssetPostprocessor`):

| LOD | Cessna 172 | Narrowbody | What changes |
|---|---|---|---|
| 0 | 37k triangles, 17 draws | 46k, 21 draws | full model; animated surfaces, wheels, props / fans, registration lettering |
| 1 | 9.7k, 4 draws | 10k, 2 draws | moving parts merged into one static mesh (the Cessna keeps a low-poly spinning prop); no lettering |
| 2 | 2.7k, 2 draws | 2.9k, 2 draws | everything in one mesh |

LODs cross-fade (dithered), so switching isn't a visible pop.

Most aircraft in a zoomed-out airport sit at LOD2. Textures need no LOD switching: the GPU samples smaller mip
levels on its own as aircraft shrink, and the per-aircraft memory (one registration patch) is small.

New aircraft: **AirCity ▸ Aircraft ▸ Create Missing Prefabs and Example Liveries** creates the prefab and the example
liveries. It never overwrites existing ones. **AirCity ▸ Aircraft ▸ Render Thumbnails** renders every type in every
livery to `Aircraft/<Model>/Thumbnails/` (transparent PNGs for menus).

## Runways and taxiways

An `Airport` holds the runways (child `Runway` objects), the taxiways and service roads (a child
`TaxiwayNetwork`, which also holds the aircraft stands and de-icing bays that open onto them), aprons, the grass under them with the mown verges beside runways and taxiways, and its
maintenance depots (child `MaintenanceDepot`s). Only their layout is saved: meshes, paint and lights are generated
when they're enabled or changed, so runways can be any length and taxiways any shape.

```csharp
using AirCity.Airfield;

var rwy = airport.AddRunway(a, b, PavementSurface.Concrete, 45f);   // designators (09/27, L/C/R) automatic
rwy.SetLighting(RunwayLighting.Basic);        // the lighting upgrade: unlit runways close at night
rwy.Check(type)                               // OK, or why not: TooShort (needs 2,100 m), NeedsPavement, ...
int n0 = net.AddNode(p0), n1 = net.AddNode(p1);
net.Connect(n0, n1, 15f, PavementSurface.Asphalt);   // splits crossings, adds runway crossings
net.FindRoute(from, to, type)                 // node ids along taxiways the type fits
airport.Condition = SurfaceCondition.Snow;    // snow builds up as airport.Simulate(seconds) runs
rwy.StartPloughing();                         // the depot's ploughs come; closed while they work
```

| | Dirt | Grass | Asphalt | Concrete |
|---|---|---|---|---|
| Aircraft | light aircraft (`AircraftType.unpavedRunways`) | light aircraft | all | all |
| Paint, lighting | edge cones only, can't be lit | edge cones only, can't be lit | yes | yes |
| Wear (movements to worn out) | ~600 light aircraft, half when wet | ~850 light aircraft, half when wet | ~4,000 narrowbody | ~12,000 narrowbody |
| Weather | +15% length dry, +30% wet, unusable from 1 cm snow | +20% takeoff / +15% landing dry, +30% / +35% wet, unusable from 1 cm snow | | |
| Upkeep | | grows ~1 cm a day: from 15 cm the takeoff run is 15% longer, at 25 cm it closes until mowed | | |

- **Length:** each aircraft type needs `max(takeoff, landing)` distance from its data (C172 ~500 m, narrowbody
  ~2,100 m). Rain lengthens landings 15%; a layer of snow (over 0.3 cm) lengthens takeoffs 15% and landings 40%.
- **Width:** ICAO by size code (A 18 m, B 23 m, C 30 m, D/E 45 m, F 60 m runways; 7.5 to 25 m taxiways).
- **Snow:** builds up at 3 cm/h while it snows and melts after. At 3 cm (1 cm unpaved) a runway or taxiway closes
  until ploughed. The depot sends its ploughs at 1.5 cm (0.6 cm unpaved), runways first; see below.
- **Wear:** `AddMovement(type)` after each takeoff / landing (taxiing wears taxiways at half that rate); damage
  grows with weight, so light aircraft barely wear pavement. Fully worn closes until `Repair()`.
- **Lighting** (upgrade per runway): Basic = edge, threshold / end, PAPI; Precision adds centreline, touchdown zone
  and approach lights with sequenced flashers. Taxiway edges can be lit (blue). Lights are GPU glows in one draw per
  runway; the sandbox airport has ~1,350.
- **Looks:** photo textures in world space with anti-repeat, slab joints, tyre rubber in touchdown zones, wear,
  wet / puddles, snow, and grass length with mowing stripes; wear, snow, grass and the work in progress update
  vertex data only, never rebuild meshes.
- The ground is flat for now. Flattening terrain under runways comes with the terrain system.

**AirCity ▸ Airfield ▸ Add Sandbox Airport** rebuilds the sandbox airport: a 2,800 m concrete runway with
precision lighting and a parallel taxiway, a crossing 1,600 m asphalt runway, a 600 m dirt strip and a 700 m grass
strip (overdue for mowing), on grass, with a depot of three snow ploughs, a mower and three tugs (two small, one
large) on a small pad and service roads from it to the apron and the parallel taxiway. It also has a taxilane of
jet stands (code C and E, square on and at 60°, one taxi-out) with four jets parked on them, a GA ramp of code A
stands on the grass taxiway, and two de-icing bays at the 09 end.

### Snow ploughs and mowers

An airport can have several `MaintenanceDepot`s, each with its own snow ploughs and mowers
(`SetFleet(ploughs, mowers)`), parked inside behind its roller doors. `airport.Maintenance` holds the jobs and hands
each to the nearest free team. Vehicles of a kind work in teams: ploughs in echelons of up to four, mowers up to
three, so buying more makes the work faster, and more than a team's worth works two jobs at once.

```csharp
using AirCity.Vehicles;

airport.Depot.SetFleet(ploughs: 3, mowers: 1);
airport.Maintenance.Plough(runway);   airport.Maintenance.PloughTaxiways();   airport.Maintenance.Mow(grassStrip);
airport.Maintenance.MowVerges(runway);
airport.Depot.Teams[0].Status   // "ploughing runway 09/27, pass 2 of 4 (40 %)"
```

- The doors roll up and the vehicles drive out one after another onto whatever is in front of the depot (a service
  road, a taxiway or an apron; no forecourt needed), keep right along the roads and taxiways in single file, fan
  out into echelon on the strip and work it in passes. Passes run in bands either side of the centreline from the
  middle out, each as wide as the team (5 m per plough, 4.4 m per mower). Ploughs cast the snow to their left, so
  every pass sends it outwards. Mowers alternate direction band by band, which leaves the stripes. Then the next
  job, or home, where each backs into its bay.
- Taxiways and roads are worked in runs, end to end, turning round only at the ends. There the ploughs turn on the
  pavement, backing into a side taxiway or onto the runway where a U-turn won't fit. A team as wide as the taxiway
  clears it in one pass.
- Every vehicle stops short of aircraft and of other vehicles, and routes round parked aircraft.
- A 2.5 km runway with shoulders (60 m): four ploughs ~13 min working (four passes), one plough ~56 min (twelve). A
  700 m grass strip with one mower: ~25 min. The strip is closed from when the team gets there until it's done;
  verges never close anything.
- `autoDispatch` (on by default) queues jobs from the weather: ploughs before the snow closes a runway, mowers when
  grass or verges reach 15 cm. Ploughing first (runways longest first, then taxiways and roads), then grass
  runways, grass taxiways and verges.
- Everything runs in game seconds (`Airport.timeScale`, 1 by default: vehicles drive at real speed; the dev
  tool's **Y** fast-forwards). Lights: rotating amber beacons whenever a vehicle is out, head and work lights with
  pools of light on the ground at night, blade / wing-tip markers, and the depot's flood lights. Snow sprays off the
  blades and brooms; clippings off the mower decks.
- The vehicles and the depot come from `ArtSource/Vehicles` (see ArtSource/README.md); `VehicleSetup` makes their
  prefabs (**AirCity ▸ Vehicles ▸ Create Missing Prefabs**, also run by the airfield bootstrap).

### Tugs and push-back

Depots also keep towbarless tugs (`depot.SetTugs(small, large)`): `TugSmall` for aircraft of 10–100 t (regional
and narrow-body) and `TugLarge` for 40–600 t (up to an A380). The aircraft's nose wheel goes into a cradle between
the tug's rear legs, which lifts it, so tug and aircraft move as one.

```csharp
var job = airport.Maintenance.PushBack(aircraft, out string problem);   // null + the reason if it can't
job.State   // Waiting, Coming, Lifting, Pushing, Lowering, Done
```

The smallest free tug that can move it drives out, turns in along the side of the stand, backs under the nose,
lifts the wheel and pushes the aircraft straight back and round onto the taxiway centreline, facing the way that
leads to a runway. Then it lowers the wheel and drives home. Light aircraft are pushed by hand (no tug); taxi-out
stands don't need one. Details: Docs/systems/maintenance.md.

## Command line

```sh
U=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity

# tests
$U -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile Logs/tests.log

# create missing prefabs / liveries / sandbox after generating a new aircraft
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.Bootstrap -logFile Logs/bootstrap.log

# airfield materials, vehicle prefabs + sandbox airport (after regenerating ArtSource/Airfield or ArtSource/Vehicles)
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Airfield.AirfieldSetup.Bootstrap -logFile Logs/airfield.log

# thumbnails for menus
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.RenderThumbnails -logFile Logs/thumbs.log

# render a scene to PNG (extra -captureView "camPos:target" args for more angles, -captureLod 2 to force far LODs
# for that capture only, -captureNight for night lighting, -captureSelect "<aircraft name>" to show the outlines)
$U -batchmode -projectPath . -executeMethod AirCity.EditorTools.Dev.SceneCapture.FromCommandLine \
   -captureScene Assets/AirCity/Scenes/Sandbox.unity -captureOut Captures/sandbox.png -logFile Logs/capture.log
```

## Git setup

Binary art (`png`, `fbx`, …) is stored in Git LFS (see `.gitattributes`). To merge scenes and prefabs, add
Unity's YAML merge tool once per clone:

```sh
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
```
