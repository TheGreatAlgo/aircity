# Starling A6

The Starling A6 is a six-seat compact business jet with a tapered nose, four oval cabin windows on each side,
rear-mounted twin engines and a low swept tail. The default Classic Blue livery puts a navy stripe below the
cabin windows and carries a gold pinstripe up the fin. All four AirCity livery styles apply to the A6.

| Aircraft data | Value |
|---|---|
| Internal model | `LightJet` |
| Category | `BusinessJet` |
| Wingspan × length × height | 13.0 × 14.0 × 3.9 m |
| Size code | A |
| Dry paved takeoff and landing distances | 970 m and 815 m |
| Maximum takeoff mass | 4,800 kg |

In Career or Sandbox, select a vacant executive hangar spot and choose **Starling A6**. Enter a tenant and
registration and select **Start lease**. Use **Taxi to stand** to move it to a free paved Code A or larger stand.
Use **Taxi to hangar** on the parked jet to return it. The executive hangar approach requires a paved Code B or
wider taxiway. [Hangar operation and placement](../systems/hangars.md)

`ArtSource/Aircraft/LightJet/parts.py` defines the geometry. `model.py` defines the paint, wheels, lights,
door and baggage points, and fictional performance data. The generated Unity model, textures, prefab and
profile live in `Assets/AirCity/Aircraft/LightJet/`. The main-wheel axle is the origin, +Z is forward in Unity,
and the nose wheel steers. The fans, flaps, ailerons, elevators, rudder and baggage hatch are animated.

Run the shared Blender `build.py` with `--model LightJet --texture --check`, followed by `finish.py` with
`--model LightJet --previews`. In Unity, use **AirCity → Aircraft → Create Missing Prefabs and Example Liveries**
and **AirCity → Game → Update Shared Game Assets** to register a newly generated prefab with the lease and
save systems. The existing **Render Thumbnails** command renders the roster thumbnails. [Aircraft pipeline](../systems/aircraft.md)
