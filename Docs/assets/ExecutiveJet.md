# Starling B8

The Starling B8 seats eight. Its rounded radome, six-pane cockpit, five oval cabin windows on each side, rear-mounted engines,
T-tail and upturned wingtips distinguish it on the apron. The default Midnight Champagne livery pairs an
ivory fuselage with a midnight-blue belly and a champagne pinstripe that rises across the fin.

| Aircraft data | Value |
|---|---|
| Internal model | `ExecutiveJet` |
| Category | `BusinessJet` |
| Wingspan × length × height | 18.0 × 20.0 × 6.1 m |
| Size code | B |
| Dry paved takeoff and landing distances | 1,320 m and 1,040 m |
| Maximum takeoff mass | 10,600 kg |

In Career or Sandbox, select an empty executive hangar spot, choose **Starling B8**, enter a tenant and
registration, then select **Start lease**. Use **Taxi to stand** to move the B8 to a paved Code B or larger stand
on a compatible taxiway. Use **Taxi to hangar** to return it. The B8 can leave a taxi-out stand under its own
power and needs a tug to leave a pushback stand. [Hangar operation and placement](../systems/hangars.md)

Select the parked B8 and choose **Deploy airstair** to lower the passenger door and expose the seven built-in
treads. Choose **Stow airstair** to close it. A taxi or departure route waits for the door to close before moving.

The source lives in `ArtSource/Aircraft/ExecutiveJet/`. The shared Blender pipeline generates the model,
three LODs, livery maps, contact shadow and profile. The Unity prefab animates the fans, control surfaces,
steering, wheels, baggage hatch and passenger airstair. [Aircraft pipeline](../systems/aircraft.md)
