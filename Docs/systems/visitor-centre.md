# Visitor centre, parking and road access

The shared **Visitor centre** construction tool places the reception building and its complete parking lot
as one facility. The building front faces local +Z; the driveway leaves the right side of the lot. Rotate
with the shared angle controls or R. Prepare the full outlined site as cleared, dry, level ground at the
airport height, and place it beside a connected public road. The preview includes the building, parking and
the proposed driveway. Invalid placements show their reason before purchase.

There are 18 marked bays: 16 standard and two accessible, with a shared access hatch, pedestrian crossing,
flush entrance route, wheel stops, curbs and lighting. Parking capacity is currently part of the authored
site. Individual visitor arrivals, parking reservations, walking visitors and parking income are future work.

The driveway is a real two-way `AirportAccess` public-road connection. `VisitorCentreLayout.TryConnect`
prepares a detached graph, preserving existing road positions, alignments and the progression entrance
`airport-reception`. The new parking entrance is `airport-visitor-centre`; it joins a suitable road within
95 m. The finished graph must support travel between the parking entrance and the same regional connection
in both directions. The graph is separate from aircraft taxiways. The base building/site price is $4,500 plus
the quoted driveway construction. Sandbox construction is free.

`VisitorCentreConstruction` checks the complete site, dense terrain samples, inland/ocean water, trees,
existing roads and actual airport structures. The connector is checked along its full width and elevation.
The player clears and levels the building/parking site. The driveway follows a graded road profile and its
quote includes cut/fill, using the same road earthwork rate as public-road construction. Trees are not removed.
Quote and commit use the same package. A failed placement changes no funds, roads or saved facility data.

The building, car park and driveway must clear taxiways, stands, runways and paved hangar approaches.
The Street tool snaps to the saved driveway mouth by name. If the first driveway section has been
removed, start at the mouth and follow the alignment snap out of the car park, or draw from an
outside road and finish at the mouth. That section uses the two-way airport-access width and meets
the parking aisle straight and level. To join an existing driveway, snap to its pavement outside the car park.
Construction and earthwork
protect the rest of the site. Road, airside and runway transactions repeat the site check at commit so an
older quote cannot overwrite a newly built visitor centre.

## State and rendering

The historical reception fields remain in `CareerLayout`. `receptionSiteVersion = 1` identifies the full
building/parking package, and `CareerSnapshot` copies it for background saves. Driveway geometry lives only
in the saved `PublicRoadNetwork`. `CareerGame` and `ValleyView` regenerate the saved building in Career and
Sandbox, including after road demolition disconnects the airport. Public roads refresh after a successful placement.

Older reception saves retain their position and road layout and display the new building. Their site version
is zero, so a larger lot is not imposed on neighbouring construction. The visitor-centre tool can replace
that legacy reception with the full package at a valid prepared site. Existing airport layouts are retained.

`ValleyBuiltSurfaces` registers the opaque building slab, forecourt, lot and entrance throat at the facility
datum. Terrain and vegetation ownership follows these surfaces, with explicit invalidation when the reception
site changes. Roof overhangs do not cut holes in the terrain. Construction previews own no saved surfaces or
road data. `VisitorCentreVisual` releases their temporary meshes and materials.

## Verification

EditMode filters: `VisitorCentre`, `PlayerConstructionTests`, `CareerSaveTests`, `RoadConstructionPlanTests`,
`AirsideTerrainTests`, `RunwayTerrainTests`, `BuiltSurfaceMapTests` and `SharedGameTests`.

The [asset review](../assets/VisitorCentre.md) renders close and airport-scale views, checks the entrance
graph route and captures night lighting. Live placement review should verify the full footprint, rotation,
quote/commit parity, road connection, saved site version and ground ownership.

Verified 2026-11-16: 146 affected EditMode checks passed, including 27 visitor-centre model, site,
transaction and construction-protection cases. The live 1440 × 900 review passed 0°/90° previews, actual
mouse purchase, exact $4,829 fixture quote, bidirectional road connectivity, pavement ownership and private
save/reload. Five standalone views cover the entrance, rear, entire site and night lighting. Review outputs
are in `Captures/VisitorCentre/`, `Logs/visitor-verified.xml` and `Logs/VisitorCentrePlacementReview/review.json`.
