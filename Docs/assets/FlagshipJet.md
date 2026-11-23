# Starling B12

The Starling B12 is a twelve-seat business jet with a low, pointed radome, six-pane windshield and seven wide cabin
windows per side. Its swept wings transition into low, raked tips. Rear-mounted turbofans, a swept T-tail and
twin-wheel landing gear set its stance on the apron. The Graphite Silver livery carries a graphite belly and a
silver pinstripe that sweeps across the fin.

| Aircraft data | Value |
|---|---|
| Internal model | `FlagshipJet` |
| Category | `BusinessJet` |
| Wingspan × length × height | 23.0 × 26.0 × 6.9 m |
| Size code | B |
| Dry paved takeoff and landing distances | 1,590 m and 1,230 m |
| Maximum takeoff mass | 22,100 kg |

In Career or Sandbox, select an empty executive hangar spot, choose **Starling B12**, enter a tenant and
registration, and choose **Start lease**. Use **Taxi to stand** to send it to a paved Code B or larger stand,
then **Taxi to hangar** to return it. A pushback stand requires a tug. [Hangar operation](../systems/hangars.md)

Select the parked aircraft and choose **Deploy airstair** to lower its eight-step passenger door. Use
**Stow airstair** to close it. Taxi and departure wait for it to close. The baggage hatch opens separately on the
starboard side.

The source is in `ArtSource/Aircraft/FlagshipJet/`. Run the shared Blender `build.py` with
`--model FlagshipJet --texture --check`, then `finish.py` with `--model FlagshipJet --previews`.
The output includes the model, three levels of detail, livery maps and the Unity aircraft profile.
[Aircraft pipeline](../systems/aircraft.md)
