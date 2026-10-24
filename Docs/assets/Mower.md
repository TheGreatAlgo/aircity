# Mower: grass mower

A cab tractor in the airport fleet's yellow, towing an orange batwing rotary mower: a centre deck plus two wings,
about 4.6 m of cut. It mows grass runways, grass taxiways and the verges beside runways and paved taxiways; see
[maintenance.md](../systems/maintenance.md).

## Key numbers

| | |
|---|---|
| Size | 5.24 m wide (mower wings), 9.5 m long including the mower, 3.37 m high |
| Origin | under the tractor's rear axle; the mower trails behind (−Z) |
| Wheels | wheelbase 2.6 m. Rear: radius 0.84 m, track 1.96 m. Front: radius 0.58 m, track 1.84 m, steer. Rims painted yellow. |
| Mower | centre deck 2.3 m (z −2.6 .. −4.9); wings out to ±2.55 m; deck wheels are static |
| Work figures | lane 4.4 m, work 4.2 m/s (15 km/h), transit 8 m/s, turn radius 7 m, echelon gap 14 m |
| Triangles | about 18.7k at LOD0 (body 3.5k faces + 4 wheels) |
| Textures | `Mower_Albedo` (grass / mud grime), `Mower_MetalGloss`: 1024² |
| Materials | one |

## Lights

| Light | Type | Where |
|---|---|---|
| Beacon | Beacon | cab roof, rear |
| Road L/R | Head | front edge of the cab roof |
| Headlight L/R | Head | grille |
| Work FL/FR/RL/RR | Work | roof corners, aimed down at the front and at the decks |
| Tail L/R, MowerTail L/R | Tail | rear fenders, and a lamp bar on the mower |
| Marker L/R | Marker | amber, on the wing tips |

There is also a slow-moving-vehicle triangle (geometry) on the mower.

## Source and outputs

- **Source:** `ArtSource/Vehicles/Mower/model.py` (`tractor`, `cab`, `hitch`, `mower`, `lamps`, `grime`).
- **Outputs:** `Assets/AirCity/Vehicles/Mower/`, laid out the same way as the SnowPlough: no broom, wheels only as
  movers.

## Regenerate

```sh
cd ArtSource/Vehicles && /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model Mower --previews
```

Then run the airfield bootstrap (see [SnowPlough.md](SnowPlough.md)).

## Notes

- Clippings particles leave from `WORK.sprayPoint` at the back of the decks.
- The mowing stripes on the grass come from the shader, not from the vehicle
  ([airfield.md](../systems/airfield.md), `_GRASS_LENGTH`).
- Possible later additions: wings folding up for transit, and a wider mower for the big runway verges.
