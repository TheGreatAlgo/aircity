# Terminal baggage connections

Terminal interiors remain abstract. A terminal owns check-in and reclaim records; its connected baggage halls
provide the visible vehicle and worker operations. Bags travel between those endpoints through a timed link.

## Player controls

Select a terminal to see **Baggage** in its selection panel, choose a hall and connect it **underground** or
**covered**. The terminal building designer also offers these connections and check-in/reclaim floor choices.
The expanded **BAGGAGE > TERMINAL CONNECTION** panel manages the selected team's hall, route overlay,
capacity and admission rate. Reassignment waits until handling jobs and queued/in-transit bags finish.
Capacity and rate increases can relieve an active queue; reducing capacity refuses occupied transit space.

- **Underground:** service hatches remain visible. **Show baggage link** displays the abstract ground route.
  Travel includes horizontal distance, the selected terminal floor and the underground descent/ascent.
- **Covered:** a nearby hall connects through an enclosed conveyor bridge with vertical end shafts. The
  bridge is at least 6.6 m high at its centre, with room for service vehicles beneath it. Connections are limited
  to 80 m, approach the hall's machinery wall and cannot cross aircraft stands, taxiways or another terminal.
- Connected halls inherit the terminal's cladding and roof colours. Terminal rooms and internal conveyors are
  not rendered; the exterior portals indicate where baggage enters and leaves the building.

Check-in and reclaim floors are independent of the boarding floor. Effective floor indices clamp when a
terminal loses storeys. Already admitted bags keep their travel time; later admissions use the current geometry.
No terminal interior-view button is exposed. The records and floor references can support an interior mode later.

## Baggage ownership and timing

For a flight served by a connected hall, automatic departure demand enters the terminal's check-in store.
Sealing the manifest queues those exact batches for that hall. Each direction has a separate bounded transit
store and FIFO admission rate. Travel takes route distance / belt speed, plus the time to admit the batch.
Counts, weights, IDs, flight assignments and screening status remain in the existing airport ledger.

The default connection carries 144 bags per direction at 300 bags/min, with a 3 m/s base belt speed using the
existing gameplay travel pacing. Powered belts increase both connection admission rate and travel speed by 50%.
The capacity floor is 36 bags, matching the generated bulk-cart batch size. Automatic manifests also respect
the connection's capacity when choosing batches.

If hall dispatch is full, arriving departure batches remain on their conveyor and eventually backpressure
check-in. A full return conveyor leaves arrivals in the hall until there is capacity. The aircraft team waits
for pending terminal deliveries rather than completing an empty departure manifest. Incoming baggage accepted
by the hall or its return conveyor satisfies aircraft unloading; the aircraft can depart while reclaim delivery
continues. Reclaim records are retained abstractly; passenger collection is not simulated.

Connecting or deleting a busy terminal or reassigning its hall refuses pending work. Capacity/rate changes
apply to later admissions; existing journeys retain their timing. An explicitly disconnected hall waits for a replacement terminal. The old independent station fixtures
remain available for apron tests; newly player-built halls require a terminal connection.

## Source

| File | Role |
|---|---|
| `Terminal.cs` | Stable baggage identity and abstract check-in/reclaim floor settings, preserved by clones and edits |
| `TerminalBaggage.cs` | Terminal stores, link settings, bounded transit and timed delivery |
| `AirportTerminalBaggage.cs` | Connections, manifest supply, arrival destinations and edit/removal guards |
| `TerminalBaggagePresentation.cs` | Exterior service portals, covered bridge and optional route overlay |
| `DevTerminalBaggagePanel.cs` | Terminal/hall connection controls and queue/travel information |

`BaggageDepotSetup.Attach` connects installed Sandbox halls to the nearest terminal. The saved main Sandbox
has an underground connection to its existing terminal. Its incoming 120 and outgoing 180 bags use the full
terminal-to-hall flow. Existing independent delivery and container review scenes retain their separate setups.

## Review

**AirCity > Vehicles > Create Terminal Baggage Review** creates `Assets/AirCity/Scenes/TerminalBaggage.unity`,
with a three-storey terminal beside the Small hall and a covered conveyor bridge. Play runs the complete flow.

`AirCity.EditorTools.Vehicles.TerminalBaggageReview.Run` verifies the saved Sandbox underground link and the
covered review. It checks that departure bags start at terminal check-in, observe travel and queue limits,
reach the aircraft and that every arriving bag reaches the owning terminal's reclaim. Captures are written to
`Captures/TerminalBaggage/`. The scene is saved before simulation so runtime records never become authored demand.

Verified 2026-11-07 in an isolated Unity project: 125 distinct passing checks, including the 124-case
baggage/container regression run and the final connection checks/native Sandbox Play test. The underground
case reclaimed 120 incoming bags and loaded 180 outgoing bags in 315.1 simulated seconds; the covered case
reclaimed 72 and loaded 90 in 219.6 seconds. Both conserved every bag and returned the crews. Exterior captures
were inspected with the terminal roofs and walls intact. Results are in `Logs/terminal-baggage-validation.json`.
