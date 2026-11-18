# AirCity: start here (for AI agents)

AirCity is an airport-building game for PC and Mac: Unity 6000.6.7f1, URP, minimum spec around an RTX 3060, up to
about 100 aircraft. It's the real game, not a prototype. It's early: there's a sandbox scene with aircraft,
generated airfields and service roads, aircraft stands and de-icing bays, weather, maintenance depots with their vehicles, and air
traffic: aircraft taxi, take off and land on their own, held apart by a tower. The shared New Game scene provides Career and Sandbox modes with one world, build workspace and full-airport save; Career adds a first task-guided progression: manual tree clearing and terrain brushes, player-placed airport layouts,
progressive bottom tools, road access, paid charters and save/resume. A full schedule and airport economy remain future work.

Read this page, then the system doc for whatever you're touching. The repo's `CLAUDE.md` has conventions and
gotchas, and `README.md` / `ArtSource/README.md` are the human-facing guides.

## Map

| Doc | Read it when you touch |
|---|---|
| [systems/new-game.md](systems/new-game.md) | player-placed airport, manual clearing/terrain, progressive tools, natural terrain relief, scanned ground, instanced rocks/undergrowth, first charter and save/resume |
| [buildings-roadmap.md](buildings-roadmap.md) | playable buildings and their access requirements, plus proposed building families from small airfield to international hub |
| [systems/hangars.md](systems/hangars.md) | rentable hangars: taxiway connections, aircraft entry/exit, leases, doors, Career rent and persistence |
| [assets/SmallHangar.md](assets/SmallHangar.md) | generated steel hangar geometry, sliding doors, LODs and visual review |
| [systems/visitor-centre.md](systems/visitor-centre.md) | visitor centre placement, parking, public-road driveway, site protection and persistence |
| [assets/VisitorCentre.md](assets/VisitorCentre.md) | generated pavilion, glazing, parking furnishings, LODs and visual review |
| [systems/built-surfaces.md](systems/built-surfaces.md) | pavement ownership, persistent terrain foundations, vegetation clearance and terrain shader clipping |
| [systems/public-roads.md](systems/public-roads.md) | separate 3D road graph, terminal entrances, lane turns, signals, road geometry, bridge/ramp kit and career drawing |
| [assets/ForestTrees.md](assets/ForestTrees.md) | spruce/pine assets, foliage source, instancing, distance models and regeneration |
| [systems/aircraft.md](systems/aircraft.md) | aircraft models, the Blender pipeline, `AircraftType`, prefabs, lights, LODs |
| [systems/liveries.md](systems/liveries.md) | paint schemes, registrations, tail logos, the runtime compositor |
| [systems/airfield.md](systems/airfield.md) | runways, taxiways, service roads, stands and de-icing bays, aprons, verges, runway rules, weather (snow, rain, grass), airfield shaders |
| [systems/gates.md](systems/gates.md) | gate/building designer, grounded concourse heads, terminal storeys/facades, bridge families, aircraft stops and automatic door assignment |
| [systems/baggage.md](systems/baggage.md) | sandbox automation and station controls, repeated cart trips, bulk/container turnarounds, departure clearance, manifests and planned terminal expansion |
| [systems/terminal-baggage.md](systems/terminal-baggage.md) | abstract terminal check-in/reclaim, timed underground/covered links, queue limits and connection controls |
| [systems/maintenance.md](systems/maintenance.md) | depots, snow ploughs, mowers, tugs and push-back, teams, jobs and dispatch, turning round, keeping clear, the ploughing / mowing pass pattern |
| [systems/deicing.md](systems/deicing.md) | deicing pads, spray trucks, tank storage, fluid deliveries and aircraft service holds |
| [systems/traffic.md](systems/traffic.md) | flights: taxi route planning, holds and clearances, takeoffs, landings, go-arounds, the wind, the sandbox's traffic controls |
| [taxi-todo.md](taxi-todo.md) | what's left in ground traffic: the jams the soak test still finds, ideas, tuning |
| [roads-todo.md](roads-todo.md) | road construction handoff: shared curves/profiles, bridges, tunnels, underpasses and local rebuilds; future upgrades and traffic |
| [systems/dev-tools.md](systems/dev-tools.md) | batch commands, tests, captures, the sandbox controls |
| [assets/Cessna172.md](assets/Cessna172.md), [assets/Narrowbody.md](assets/Narrowbody.md), [assets/Widebody.md](assets/Widebody.md), [assets/DoubleDeck.md](assets/DoubleDeck.md), [assets/Quadjet.md](assets/Quadjet.md), [assets/Turboprop.md](assets/Turboprop.md), [assets/Trijet.md](assets/Trijet.md), [assets/Supersonic.md](assets/Supersonic.md) | one aircraft type |
| [assets/SnowPlough.md](assets/SnowPlough.md), [assets/Mower.md](assets/Mower.md), [assets/Tugs.md](assets/Tugs.md), [assets/Depot.md](assets/Depot.md), [assets/ControlTowers.md](assets/ControlTowers.md), [assets/JetBridge.md](assets/JetBridge.md) | one vehicle / building |
| [assets/BaggageEquipment.md](assets/BaggageEquipment.md) | baggage tractor, covered cart, belt loader, bag props, mechanical posing and the standalone review scene |
| [assets/BaggageDepot.md](assets/BaggageDepot.md) | Small/Medium/Large depot shells, shared drive-through positions, roof inspection and equipment upgrades |
| [assets/RampCrew.md](assets/RampCrew.md) | shared skinned workers, driving, boarding, bag handovers, hold poses and crew interlocks |
| [assets/ContainerEquipment.md](assets/ContainerEquipment.md) | unbranded LD3, container dolly, dual-platform loader, interfaces, references and generation |
| [assets/DeicingEquipment.md](assets/DeicingEquipment.md) | articulated deicing truck, storage tank, supplier tanker and recycling skid; generated models and review captures |
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
              DevToolbar + DevGui (the HUD: tool bar, context strip, sidebar), DevGateDesigner (runtime gates and saved presets),
              GateAircraftPreview (visual-only aircraft), DevOutlines (hover / selection outlines)
  Rendering/  Bc1TextureBuilder
Scripts/Editor   (AirCity.Editor): importers (.aircraft, .vehicle, .regfont), asset postprocessors, *Setup (prefabs,
                 materials, sandbox), SceneCapture
Shaders/  Aircraft{LightGlow,Outline,GroundShadow}, Airfield{Surface,Paint,LightGlow,Fixture,Common.hlsl}, VehicleLightGlow
Tests/EditMode   AircraftTypeTests, AircraftLiveryTests, AirfieldTests, StandTests, MaintenanceTests, TowerTests, TugTests, TrafficTests,
                 TrafficSoakTests (explicit: game hours of auto traffic)
```

## One game, two modes

All features use the same airport data, tools, simulation and save system in `NewGame.unity`. Career and Sandbox
select rules (funds, unlocks, objectives), not implementations. Use `Game > Switch to Sandbox / Career` to switch
in place. Keep `Sandbox.unity` and the small review scenes as development fixtures. Never duplicate a new feature
into separate career and sandbox tool paths. See [the shared session](systems/new-game.md#shared-game-architecture).

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
  vehicles all run on it. New Game derives its calendar/climate from saved `operatingSeconds`: one displayed
  minute per simulation second, with pause and 1×/5×/20× controls in the top bar.
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
| Aircraft | Done: seven Starling types, one per size code A–F (A4 light single, B50 turboprop, C180 narrowbody, D250 trijet, E350 widebody, F450 quadjet, F550 double-deck), with liveries, lights, LODs |
| Airfield | Done: four surfaces, runway rules, ICAO markings and lights, taxiway graph and routing, service roads, mown verges, wear, rain, snow, grass |
| Stands | Started 2026-10-24: parking stands (codes A–F, 90/60/45/30°, push-back or taxi-out, grass for A/B) and drive-through de-icing bays, snapped onto taxiways with automatic paint; next: hangars, jet bridges, aircraft parking at stands |
| Maintenance | Done: several depots (vehicles parked inside, roller doors), plough teams in echelon, mowers (strips and verges), taxiway runs turned on the pavement, automatic dispatch to the nearest team, keeping clear of aircraft and each other, night lights |
| Tugs | Started 2026-10-26: towbarless tugs in two sizes (narrow-body, up to A380) that live at the depot and push aircraft back off push-back stands onto the taxiway (`Maintenance.PushBack`); next: towing between stands / to hangars, automatic push-backs once aircraft taxi |
| Not started | aircraft movement / ATC / gameplay loop; economy and costs; terrain and flattening; terminals, hangars and jet bridges; a real build UI; rotary snow blower (not wanted for now) |
