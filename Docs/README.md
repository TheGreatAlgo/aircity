# AirCity: start here (for AI agents)

AirCity is an airport-building game for PC and Mac: Unity 6000.6.7f1, URP, minimum spec around an RTX 3060, up to
about 100 aircraft. It's the real game, not a prototype. It's early: there's a sandbox scene with aircraft,
generated airfields and service roads, aircraft stands and de-icing bays, weather, maintenance depots with their vehicles, and air
traffic: aircraft taxi, take off and land on their own, held apart by a tower. There's no gameplay loop yet: no
schedule or economy, no terrain and no real UI.

Read this page, then the system doc for whatever you're touching. The repo's `CLAUDE.md` has conventions and
gotchas, and `README.md` / `ArtSource/README.md` are the human-facing guides.

## Map

| Doc | Read it when you touch |
|---|---|
| [systems/aircraft.md](systems/aircraft.md) | aircraft models, the Blender pipeline, `AircraftType`, prefabs, lights, LODs |
| [systems/liveries.md](systems/liveries.md) | paint schemes, registrations, tail logos, the runtime compositor |
| [systems/airfield.md](systems/airfield.md) | runways, taxiways, service roads, stands and de-icing bays, aprons, verges, runway rules, weather (snow, rain, grass), airfield shaders |
| [systems/maintenance.md](systems/maintenance.md) | depots, snow ploughs, mowers, tugs and push-back, teams, jobs and dispatch, turning round, keeping clear, the ploughing / mowing pass pattern |
| [systems/traffic.md](systems/traffic.md) | flights: taxi route planning, holds and clearances, takeoffs, landings, go-arounds, the wind, the sandbox's traffic controls |
| [systems/dev-tools.md](systems/dev-tools.md) | batch commands, tests, captures, the sandbox controls |
| [assets/Cessna172.md](assets/Cessna172.md), [assets/Narrowbody.md](assets/Narrowbody.md) | one aircraft type |
| [assets/SnowPlough.md](assets/SnowPlough.md), [assets/Mower.md](assets/Mower.md), [assets/Tugs.md](assets/Tugs.md), [assets/Depot.md](assets/Depot.md), [assets/ControlTowers.md](assets/ControlTowers.md) | one vehicle / building |
| [assets/airfield-textures.md](assets/airfield-textures.md) | ground textures and airfield materials |

## Architecture in one screen

```
ArtSource/ (generators: Python + Blender)            Assets/AirCity/ (Unity)
  Aircraft/  build.py + finish.py --model X   ──►  Aircraft/<X>/ fbx, textures, <X>.aircraft ─► AircraftType + prefab
  Vehicles/  build.py --model X               ──►  Vehicles/<X>/ fbx, textures, <X>.vehicle  ─► VehicleType + prefab
  Airfield/  textures.py                      ──►  Airfield/Textures ─► Airfield/Materials

Scripts/Runtime  (assembly AirCity.Runtime)
  Aircraft/   Aircraft (picking, outline), AircraftType (data), AircraftLights, AircraftGroundMotion,
              AircraftControlSurfaces, Liveries/ (applier, compositor: Burst + BC1)
  Airfield/   Airport ─┬─ Runway (child objects)          rules: RunwayRules     state: SurfaceState/SurfaceValues
                       ├─ TaxiwayNetwork (taxiways + service roads, routing, Stands)   Building/*Builder regenerate meshes
                       ├─ MaintenanceDepots (Vehicles/)    StripWork = ploughing / mowing pattern (C# + HLSL twins)
                       ├─ Maintenance (AirportMaintenance: queue, dispatch, avoidance), Pavement (drivable map)
                       └─ Traffic (AirTraffic: flights, the tower; windFrom picks the runways' direction)
  Traffic/    AirTraffic ─► Flight (one aircraft's movement) ; TaxiPlanner ─► TaxiRoute (TaxiPath + holds + sections)
  Vehicles/   AirportMaintenance ─► MaintenanceDepot ─► VehicleTeam (paths + per-vehicle simulation,
              VehicleManeuver turns; a tug's push-back in VehicleTeam.Tow) ─► Vehicle (visual), VehicleLights
              TowJob + PushBackPath (an aircraft's way off its stand)
  Dev/        DevOrbitCamera, DevSandboxControls (aircraft keys), DevTrafficControls (flights, routes drawn), DevAirfieldTool (airfield / depot tools + panel),
              DevToolbar + DevGui (the HUD: tool bar, context strip, sidebar), DevOutlines (hover / selection outlines)
  Rendering/  Bc1TextureBuilder
Scripts/Editor   (AirCity.Editor): importers (.aircraft, .vehicle, .regfont), asset postprocessors, *Setup (prefabs,
                 materials, sandbox), SceneCapture
Shaders/  Aircraft{LightGlow,Outline,GroundShadow}, Airfield{Surface,Paint,LightGlow,Fixture,Common.hlsl}, VehicleLightGlow
Tests/EditMode   AircraftTypeTests, AircraftLiveryTests, AirfieldTests, StandTests, MaintenanceTests, TowerTests, TugTests, TrafficTests
```

## Principles that hold everywhere

- **Generated, not hand-edited.** Models, textures, `*.aircraft` / `*.vehicle` data, airfield textures and the
  airfield meshes are all regenerated. Change the generator, never the output. Prefabs and materials are created
  once and then kept, so hand-tuning survives a regeneration.
- **Data drives gameplay.** Code reads `AircraftType` / `VehicleType` / `RunwayRules`, never mesh positions or
  per-model magic numbers.
- **Unity space:** origin on the ground, +Z forward, +X right, metres. Aircraft origin is under the main gear;
  vehicle origin is under the rear axle. Airfield code works on the ground plane (`Geo`: `Vector2` = (x, z)), with
  bearings clockwise from north = +Z.
- **Save the layout, rebuild the meshes.** Runtime-generated objects use `HideFlags.DontSave`. State that changes
  often goes into vertex channels or shader values, never a rebuild.
- **Scale.** Built for about 100 aircraft, mostly zoomed out: few draw calls per object (far LODs are 1–2 draws),
  lights as GPU glow quads controlled by the renderer's shader user value, and hot per-pixel work in Burst.
- **Game time.** `Airport.timeScale` is game seconds per real second (default 1). Weather, grass growth and
  vehicles all run on it. There's no game clock yet.
- **Input System only.** Never use `UnityEngine.Input`.

## Working with the user

- **Commits:** never commit or push unless asked, and don't add Co-Authored-By lines unless asked. The GitHub repo
  is **public**: don't put secrets or private data in it.
- **Looks:** the user judges looks critically.
  - Check close-up renders (Blender previews, Unity captures) against real-world references before showing art.
  - Fitting shapes to real photos beat guessing curves. The A220-style jet nose was the lesson: the first jet nose
    was called "awful".
- **Design questions:** ask when the design is genuinely open, with a recommended option. The user answers
  quickly, and their answers so far are recorded at the top of each system doc.
- **Environment:** no new environment variables or capacity throttles; hardcode constants instead.
- **Reports:** say what you verified, and how (tests, captures). Link captures in `Captures/*.png` (gitignored).

## Status and next likely work (2026-10-23)

| Area | Status |
|---|---|
| Aircraft | Done: Cessna 172 (code A) and an A320-class twinjet (code C), with liveries, lights, LODs |
| Airfield | Done: four surfaces, runway rules, ICAO markings and lights, taxiway graph and routing, service roads, mown verges, wear, rain, snow, grass |
| Stands | Started 2026-10-24: parking stands (codes A–F, 90/60/45/30°, push-back or taxi-out, grass for A/B) and drive-through de-icing bays, snapped onto taxiways with automatic paint; next: hangars, jet bridges, aircraft parking at stands |
| Maintenance | Done: several depots (vehicles parked inside, roller doors), plough teams in echelon, mowers (strips and verges), taxiway runs turned on the pavement, automatic dispatch to the nearest team, keeping clear of aircraft and each other, night lights |
| Tugs | Started 2026-10-26: towbarless tugs in two sizes (narrow-body, up to A380) that live at the depot and push aircraft back off push-back stands onto the taxiway (`Maintenance.PushBack`); next: towing between stands / to hangars, automatic push-backs once aircraft taxi |
| Not started | aircraft movement / ATC / gameplay loop; economy and costs; terrain and flattening; terminals, hangars and jet bridges; a real build UI; rotary snow blower (not wanted for now) |
