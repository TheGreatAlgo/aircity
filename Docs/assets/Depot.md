# Depot: maintenance depot building

A steel-clad portal-frame garage where the snow ploughs and mowers live, inside. It has four 6.5 × 6.4 m roller
doors with yellow guard posts, an office end with two rows of windows, a personnel door and canopy, standing-seam
cladding on a concrete plinth, a low-pitch gable roof with gutters and downpipes, and flood lights over the doors.
Behind the doors is a hollow hall:

- a concrete floor with yellow bay lines and a stop line at the back of each lane;
- walls with a dark 1.2 m dado band, and a dark ceiling at 8.3 m;
- steel portal frames between the drive lanes: columns at the front, middle and back, rafters, and a beam down the
  middle.

The openings go down to the ground, and the door curtains are separate parts that roll up. It is built by
the vehicle pipeline (`KIND = "Depot"`, `VehicleKind.Depot`).

## Key numbers

| | |
|---|---|
| Footprint | 46 × 24 m; eaves 8.6 m, ridge 10.2 m (10.5 m with the cap) |
| Origin | on the ground at the middle of the door front. The building extends behind it (−Z); +Z is out of the doors. |
| Doors | centres at x = −18, −9.5, −1, 7.5; office end from x = 12 |
| Hall | x −22.75 .. 11.5 (left wall to the office partition), z −23.75 .. −0.25, floor to 8.3 m (`INTERIOR`) |
| Doors | `Door_0..3`, each pivoted at the top centre of its opening on the curtain's plane (y 6.4, z −0.075) |
| Triangles | about 38k at LOD0: body 14.3k faces, each door 1.1k (mostly cladding and door ribs); LOD ratios 0.35 / 0.1 |
| Textures | baked at 4096 and saved at 2048 (`BAKE_SIZE`), AO strength 0.5 |
| Lights | 5 Flood lights (`Flood_0..3` over the doors, `Flood_Office`), on at night, with ground pools |
| Colours | cladding `#AEB4B8`, trim `#3F4A57`, roof `#4B5157`, doors `#C9CDD0`, guards `#F2B40F` |

## Use in game

- `MaintenanceDepot.buildingPrefab` = `Assets/AirCity/Vehicles/Depot/Depot.prefab`, and `buildingType` =
  `Depot.vehicle`. The prefab has `VehicleLights` only, with headlights and work lights off and flood lights on.
- The depot spawns the building as a DontSave child at its transform.
- **Doors:** `MaintenanceDepot` finds `Door_k` and rolls each up by scaling its local y from 1 to 0.05 (5 s) while
  a vehicle drives through.
- **Parking:** vehicles park inside, one behind each door, facing out, their tail 0.8 m off the back wall. More
  park in a row outside along the front. The bays come from the `.vehicle`'s `doors` and `interior`.
- **Data in `Depot.vehicle`:**
  - `"doors"`: name, `position` (the bottom centre of the opening, Unity space), `width`, `height`.
  - `"interior"`: `min` / `max`, the hall's free space.

  They come from the model's `DOOR_PARTS` and `INTERIOR`, which `vpipe.write_profile` exports for any model that
  has them.

## Regenerate

```sh
cd ArtSource/Vehicles && /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model Depot --previews
```

This takes about 2 minutes because of the 4K bake. The `open` and `inside` previews render with the doors rolled up
(`PREVIEW_OPEN`).

## Notes

- **Bake resolution:** fine ribs aliased badly against a 1K bake, which is why the bake is 4K and the ribs are
  bevelled boxes.
- **Separate movers:** `SEPARATE_MOVERS = True` keeps the doors out of the merged, decimated far LODs (vpipe
  `make_lods`), so the far LODs keep their openings. Unity's postprocessor adds every `Door_*` renderer to all LOD
  levels. Other models don't set it and merge their movers as before.
- **Possible later additions:** a salt store, signage (needs glyph geometry), lights in the hall.
