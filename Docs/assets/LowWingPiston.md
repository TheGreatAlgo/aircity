# Starling A2

The Starling A2 is a two-seat piston trainer with mildly tapered low wings, a two-blade propeller and fixed
tricycle gear. It has a split windshield, three cabin windows per side and short wheel fairings. The default
Spruce Gold livery uses a warm ivory fuselage, forest-green trim and a gold pinstripe.

| Aircraft data | Value |
|---|---|
| Internal model | `LowWingPiston` |
| Category | `GeneralAviation` |
| Wingspan × length × height | 10.2 × 8.0 × 2.55 m |
| Size code | A |
| Dry paved takeoff and landing distances | 445 m and 385 m |
| Maximum takeoff mass | 935 kg |

Select **Starling A2** in a flying school's **Manage** tab to buy it as a trainer. It uses the school's Code A
tie-downs and can operate from a suitable grass or paved runway. It also fits small hangars and executive spots.
[Flying school](../systems/flying-school.md) · [Hangars](../systems/hangars.md)

The source is in `ArtSource/Aircraft/LowWingPiston/`. Use `build.py --model LowWingPiston --texture --check`,
then `finish.py --model LowWingPiston --previews` with Blender. The prefab animates the propeller, steering,
wheels, control surfaces and starboard baggage hatch. [Aircraft pipeline](../systems/aircraft.md)
