# Container baggage equipment

Three generated assets extend the bulk equipment family: `ContainerLD3`, `ContainerDolly` and `ContainerLoader`.
All equipment is **unbranded**. Equipment IDs and capacity markings are baked; the game's name and handler
logos are not. The same change removes the previously baked name from the tractor, cart and belt loader.

The turnaround now opens the actual aircraft hatch, raises the front platform clear of the fuselage,
creeps into sill alignment and moves containers through the opening. The dolly turntable aligns its
container to the loader before side transfers, then returns to its travelling position. Withdrawal reverses clear before
lowering; the hatch closes after equipment returns. `BaggageDockingReview` captures both directions.

## Geometry and interfaces

| Asset | Geometry and operating envelope |
|---|---|
| `ContainerLD3` | AKE-type aluminium container; 1.562 × 1.534 m base, 1.626 m height, 2.007 m contour width. The sloping lower outboard panel extends beyond the base. A separate curtain, curtain roll and internal bag display support open/closed inspection. Proposed game tare: 95 kg; maximum gross: 1,588 kg. |
| `ContainerDolly` | One-container roller bed, nominal 3.50 × 1.77 m equipment class; 0.508 m roller height; 1,600 kg gross payload. Four wheels, front steering turntable, articulated drawbar/eye, matching 0.38 m tractor hitch, rear coupling, turntable deck and retractable restraints. Profile bounds conservatively include the transported container. |
| `ContainerLoader` | Generic 7-tonne lower-deck loader, about 9.2 × 3.6 m, 2.52 m clear platform width. Independent level main and aircraft platforms, fixed-length scissor arms, guide rails, retractable stops, six stabilisers and an operator station. Main range 0.485–3.7 m; front range 1.8–3.7 m. A sliding 2.5 m transverse roller bridge and retractable side gate support transfer from a clear dolly parking position. |

These are game asset specifications, not manufacturer-certified equipment. Loader structure and the side
bridge are a generic design. Cargo points and sill heights come from the generated aircraft data. A full-size
LD3 must never stand in for an AKH/LD3-45 container at an incompatible narrowbody hold.

References checked 2026-11-03:

- [Granger's LD3 contour drawing](https://aircraftcontainer.com/ld3akedimensions.pdf): the 61.5-inch base,
  79-inch maximum span, 60.4-inch depth and 64-inch height establish the asymmetric outline. The website's
  accompanying metric table has inconsistent conversions; the dimension drawing governs this contour.
- [Avius AKE specifications](https://www.aviusuld.com/products/ake/): AKE identity, base dimensions and
  1,588 kg maximum gross. Its particular product height is 1.616 m; this generic asset uses the drawing's 1.626 m envelope.
- [Orientitan LD3 dolly](https://orientitangse.com/1-4-aircraft-container-dolly.html): HD-P011603 size class,
  508–515 mm roller height, turntable steering, coupling, roller deck and 1,600 kg payload.
- [TREPEL CHAMP 70](https://trepel.com/product/champ-70/) and
  [manufacturer brochure](https://trepel.com/wp-content/uploads/2026/08/TREPEL_Brochure_Perfect_to_work_with_09_2024_Web.pdf):
  compact lower-deck loader proportions, separate front/main platform ranges, roller transfer and stabilisers.

## Source and runtime

Sources are `ArtSource/Vehicles/{ContainerLD3,ContainerDolly,ContainerLoader}/model.py`, sharing
`Common/container_gse.py` and the existing baggage finishes/mechanical helpers. Outputs are generated FBX,
albedo/metallic-smoothness atlases and `.vehicle` profiles under `Assets/AirCity/Vehicles/<Model>/`.

The `container` profile block imports into `VehicleType.container`. It supplies ULD dimensions/code/tare,
platform transfer points, payloads, lift lengths/ranges and bridge reach. Trailer hitches remain in the existing
`baggage` block so the tractor/dolly pair uses the tested hitch-constrained `BaggageTrain`.

`ContainerEquipmentRig` poses the loader's platforms, arms, stabilisers, stops, side gate/bridge and the ULD's
curtain/load. The dolly also uses `BaggageEquipmentRig` for steering and its drawbar. Reference transforms are
serialized so posed clones and domain reloads retain the original mechanical geometry. Each moving part has
three LODs following one restored pivot. Container units remain independent prefab instances during transfer.

## Generate and review

From `ArtSource/Vehicles`, run each model through the shared pipeline:

```sh
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model ContainerLD3 --previews --check --bake-device CPU
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model ContainerDolly --previews --check --bake-device CPU
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model ContainerLoader --previews --check --bake-device CPU
```

`--geometry --views front34` provides a quick silhouette review. Additional views show the open ULD, turned
dolly and raised/high loader. Textured previews live in each model's ignored `Build/previews` directory.
**AirCity > Vehicles > Create Missing Prefabs** creates new equipment prefabs while preserving existing ones.

Use **AirCity > Vehicles > Create Container Turnaround Review**, or open
`Assets/AirCity/Scenes/ContainerTurnaround.unity` and enter Play mode. The review exercises incoming containers,
departing containers, both platforms, loose bags at the widebody's bulk point and departure clearance.
Aircraft cargo hatches animate around lined openings; hold contents and terminal dispatch/arrivals endpoints remain logical. See [baggage handling](../systems/baggage.md).
