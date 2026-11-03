# Bulk baggage equipment

First equipment art set: `BaggageTractor`, `BaggageCart`, `BeltLoader` and `BaggageProps`.
`BaggageDelivery.unity` reviews outbound delivery; `BaggageTurnaround.unity` connects the equipment to an airport
stand for arrivals unloading, departing baggage loading and departure clearance. Hold contents remain ledger records; aircraft cargo hatches and openings are modelled.
Aircraft hatches animate with the turnaround; the container fleet is documented in [ContainerEquipment](ContainerEquipment.md). The next slice adds close sill docking, timed individual bags on the conveyor, bounded cart/hold loads and reverse
withdrawal before lowering. See [the baggage system](../systems/baggage.md). Terminal facilities remain deferred.

These are reusable, realistically proportioned, unbranded mesh assets in blue/white. They
share the existing generated vehicle pipeline, metallic/smoothness atlases, restrained grime and separate
mechanical parts. The current finish is an initial art pass, with scope for richer surface detail later.
The game's name is not baked into the equipment; only equipment IDs and functional markings remain.
The [container equipment set](ContainerEquipment.md) adds a separate LD3, dolly and lower-deck loader.

## Equipment

| Model | Geometry and interfaces |
|---|---|
| `BaggageTractor` | Compact open driving station, 1.60 m wheelbase, 0.30 m wheel radius. Seat, pedals, steering wheel, grille, steps, handrails, lamps and front/rear pin couplings. Hitch height 0.38 m. |
| `BaggageCart` | Covered body 3.05 × 1.65 m, roof about 2.05 m high, 2.16 m wheelbase and 0.203 m wheel radius. Rolling side curtains, front steering turntable, hinged drawbar, coupling eye, rear clevis, corner bumpers and reflectors. Separate display load with ten bags. |
| `BeltLoader` | Rigid 7.8 m conveyor with 0.60 m usable belt, 3.60 m wheelbase and 0.33 m wheel radius. Proposed working range: aircraft end 1.2–4.3 m, receiving end 0.7–1.1 m. Independent crossed-arm lifts with sliding bearings, rollers, rubber end bumpers, access strip, handrails and side driving station. |
| `BaggageProps` | Four independent meshes: large and small hard cases, soft suitcase and duffel. Handles, wheels, zipper bands, tags and shell ribs where appropriate. Individual prefabs have their origins at ground contact. |

Reference classes: [TLD JST](https://www.tld-group.com/products/baggage-tractors/jst-series/) for the open
tractor; [Tianyi TY-TD25](https://www.tianyi-gse.com/baggage-tractor/tug-baggage-tractor.html) for compact tractor
dimensions and wheelbase; [FAST covered carts](https://fastgse.com/products/ground-support-equipment/baggage-carts/baggage/)
for the body, fifth-wheel steering and curtains; [TLD NBL](https://www.tld-group.com/products/belt-loaders/nbl/)
for the full-size belt-loader class. This loader's double-scissor front linkage is our own generic design,
not a reproduction of TLD's mechanism. Equipment figures are asset specifications, not manufacturer certifications.

## Source and outputs

- `ArtSource/Vehicles/Common/baggage.py`: shared finishes, wheels, couplings, lettering, driving controls,
  bag geometry and fixed-length lift arms.
- `ArtSource/Vehicles/<Model>/model.py`: dimensions, parts, wheel/light data, interfaces and preview poses.
- Generated outputs: `Assets/AirCity/Vehicles/<Model>/Models/<Model>.fbx`, `Textures/` and `<Model>.vehicle`.
- `VehicleSetup.CreateMissing` creates the equipment prefabs and the four independent bag prefabs under
  `Assets/AirCity/Vehicles/BaggageProps/Props/`. Existing prefabs are preserved.
- `BaggageEquipmentRig` controls cart curtains/load, steering, raised drawbar and conveyor heights. It also
  connects a trailer's actual eye to a leading vehicle's rear coupling for posing a train.

The generated `.vehicle` has a `baggage` block, imported into `VehicleType.baggage`. Interface vectors are
Unity local metres (+Z forward, +X right, +Y up). Cart `frontHitch` is the drawbar eye's lowered, straight-ahead
position; `FrontHitchWorld` follows its articulation. `loadMin` / `loadMax` describe the usable cart volume;
`payload` is the proposed 1,800 kg cart payload. Loader lift `centre` is a forward distance from the rear axle.

`BaggageTractor` is a separate `VehicleKind` from the aircraft push-back tugs. Equipment is not assigned to
maintenance depots or dispatched as plough/mower teams by this change.

## Articulation and LODs

Tractor wheels use the existing `Vehicle` animation. A cart's front wheels and drawbar are parented to
`FrontAxle` by its rig, so turning the complete front assembly also moves the wheel pivots. The drawbar has
its own horizontal hinge. Curtains roll toward their upper pivots, with an expanding roll; the display load
can be shown independently.

The loader's receiving and aircraft-end heights determine the rigid conveyor's pitch. Its rear lift has one
stage and its front lift two. Arms retain their lengths, rotate at their pivots and slide along their bearing
rails as the conveyor changes height. `BeltSurface` and the rollers are separate children of the conveyor
for later belt-motion work. The current rig poses equipment; it does not simulate baggage movement.

The cart and loader use `MOVING_LODS`: each moving part has three decimated meshes under the same pivot.
FBX exports them flat as `<Part>_LOD0..2`; `VehicleAssetPostprocessor` restores the pivots before constructing
the LOD group. This avoids both the FBX nested-axis conversion issue and far LODs containing frozen copies
of the curtains/conveyor. Reference transforms are serialized so a posed scene or clone does not become its
own new rest pose after a domain reload. Only profiles that declare moving-LOD metadata add a model import
dependency; legacy vehicles retain their prior dependency graph. Props currently have one detail level.

## Regenerate and review

From `ArtSource/Vehicles`:

```sh
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model BaggageTractor --previews --check
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model BaggageCart --previews --check
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model BeltLoader --previews --check
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model BaggageProps --previews --check
```

Use `--geometry --views front34` for an initial geometry review. Cart views include `open`, `turn` and `parked`
(75-degree raised drawbar, with its hinge beyond the body front); loader
views include `raised` (3.05 m), `high` (4.3 m) and `side`. Blender previews are in each model's ignored
`Build/previews` directory. If Metal baking stalls, add `--bake-device CPU`; samples and export settings remain
the same. This first set was baked using that fallback.

In Unity, **AirCity > Vehicles > Create Baggage Equipment Showroom** creates
`Assets/AirCity/Scenes/BaggageEquipment.unity`: a coupled tractor/cart train, a loaded open cart, working and
parked loaders and the four bag props. The equipment's Inspector exposes its rig controls. This is a separate
review scene; the airport sandbox and aircraft are not edited.

Batch creation and captures, from the project root with the editor closed:

```sh
rtk proxy /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Vehicles.BaggageEquipmentSetup.FromCommandLine -logFile Logs/baggage-setup.log
```

Captures go to `Captures/BaggageEquipment/`: `overview.png`, `tractor.png`, `cart-open.png`,
`belt-loader.png` and `bags.png`. The showroom command intentionally regenerates that review scene.

For the operational scene, use **AirCity > Vehicles > Create Baggage Delivery Review**, then enter Play mode.
See [the delivery simulation and batch command](../systems/baggage.md#first-working-bulk-delivery).

`BaggageEquipmentTests` checks import scale and wheel pivots, articulated hitch alignment, raised drawbar,
conveyor endpoints across the height range, fixed arm scales, decreasing moving LODs, posed clones and the
individual bag prefabs. `BaggageDeliveryTests`, `BaggageTurnaroundTests` and `BaggageSandboxTests` cover
operational baggage conservation, routing, repeated trips and sandbox integration.

Verified 2026-11-02: all four generators passed normals checks; textured Blender previews and Unity captures
were reviewed, including open/turned/parked cart poses and the loader's maximum height. **94 EditMode checks
passed**: eight baggage equipment checks plus the existing tug, tower and terminal bridge suites. Validation
used a separate project copy because the main project was open in another editor instance. The final assets,
prefabs, scene and GUIDs were copied back together. Results are in `Logs/baggage-tests.xml` and
`Logs/baggage-tests.log`; setup/capture output is in `Logs/baggage-setup.log`.
