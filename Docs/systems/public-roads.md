# Public roads

Public roads have their own serializable, metre-scale 3D graph in `Runtime/World/PublicRoadNetwork.cs`.
Airport service roads remain in the existing taxiway graph. Public streets, highways and terminal approaches
need independent elevations, lanes, controls and destinations; aircraft routing never sees these segments.

## Network and connections

`PublicRoadNode` stores a stable ID, explicit `Vector3` position, junction control and optional `terminalEntrance`
identifier. `PublicRoadSegment` stores stable ID, endpoints, style and Ground/Elevated structure. Current styles
are Rural, AirportAccess, Urban, Highway and Ramp. Streets are two-way, highways have two lanes each way, and
ramps are one-way from a to b. Width and speed defaults come from style. `LaneConnections` exposes allowed
incoming/outgoing lanes and straight/left/right turns, omitting U-turns and marking left turns as yielding.

`Connect` preflights all crossings before changing segments. Elevations within 0.35 m form one junction;
different levels remain disconnected with at least 6.5 m centreline separation, allowing for the bridge deck.
An automatic street crossing cannot create an at-grade highway junction: merge/ramp nodes must be authored
explicitly. `HasRoute` respects one-way ramps. Save validation rejects missing IDs, invalid coordinates,
unsplit at-grade crossings and elevated crossings without clearance. A topological route alone does not yet
prove vehicle swept-path clearance or a safe merge.

## Meshes and signals

`PublicRoadGeometry` generates road strips with crown, gravel shoulders, lane/edge markings and curved
corner/T/crossroad polygons. Roads flatten toward a junction's saved elevation. Only Ground links contribute
to derived roadbed terrain heights. Decks, parapets and piers belong to Elevated links; piers skip lower roads.
The builder shares low-level `MeshData`/`PaintBuilder` utilities and surface shaders with the airfield, but has
its own topology and junction construction. Generated meshes are rebuilt from the saved graph.

`PublicRoadSignals` groups the most nearly opposing approaches into a through-road phase. Its deterministic
44-second cycle gives that phase 24 seconds green, 3 amber and 1 all-red, followed by 12 seconds green,
3 amber and 1 all-red for the other approaches. Left turns yield to opposing traffic; protected turn arrows
and vehicle-actuated phases are future work. `PublicRoadSignalsView` controls the reusable `TrafficSignal`
prefab through property blocks. Career signals use the saved game clock and pause/speed controls.

## Player flow and reusable assets

The first career connection can be drawn from a player-chosen airport entrance to the region road. `CareerRoadTool` offers clicked
corners, end snapping, terrain-following preview, length-based pricing, undo and cancel. `CareerState.Build`
validates and connects a cloned graph before committing funds. New games require manual tree removal along
the route; building a road never clears its woodland. The entrance uses public node ID `airport-reception`;
future terminals can provide separate arrivals/departures entrance identifiers. The graph is saved with the
career; original implicit-road saves migrate from their saved valley.

Assets live under `Assets/AirCity/World/Roads`. `RuralRoad` and `AirportAccessRoad` profiles hold materials,
widths, shoulder widths and crown. Materials are independent copies of the existing airfield materials and
remain hand-tunable. **AirCity > World > Create Public Road Model Kit** creates missing prefabs for
`RuralStraight`, `UrbanCorner`, `SignalTjunction`, `SignalCrossroads` and `HighwayOverpass`. The latter includes
an 8 m high highway, an unrelated road underneath, and an ascending one-way ramp to a named upper entrance.
`PublicRoadModel.network` keeps the source layout with each prefab. Forced kit regeneration is available
through `PublicRoadKitSetup.Create(true)` and updates mesh subassets in place.

Open `Scenes/WorldAssets.unity`, or create it with **AirCity > World > Create Tree and Road Review Scene**.
`WorldAssetReview.FromCommandLine` renders the kit, the actual airport junction and forest, then completes
a real charter. `CareerRoadPlayReview.Run` captures the actual Game view and drives the drawing tool with
queued mouse clicks, then checks save/load. `PublicRoadTests` covers topology, clearance, one-way ramps,
signal interlocks, custom purchase/save and migration; career tests cover the existing flight loop.

## Next work

This is the graph, asset and first construction foundation. Civilian vehicles, queues, intersection reservations,
highway merge behaviour, passenger transfers, sidewalks/crosswalks, swept turn paths, road maintenance and
general highway/elevation construction UI still need implementation. The current career drawing tool builds
ground access roads only; the elevated kit demonstrates the same graph at multiple heights.
