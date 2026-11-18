# Visitor centre

`VisitorCentreBuilder` replaces the three-box reception in `ValleyView.BuildReception`. The generated
building keeps the saved centre and heading: a 22 × 12 m core, with the public entrance facing local +Z.
The roof and entrance canopy extend to roughly x ±12 m and z −8…10 m. The tallest ridge is 6.285 m.

The central glazed gabled hall is flanked by lower timber-clad wings. Cream walls, a stone plinth, dark
standing-seam roofs, deep timber canopy, framed double doors, window mullions, fascia and downpipes give it
the character of a small airfield visitor centre. Native mesh lettering identifies the entrance. The closest
LOD includes a reception desk and seating. Entry glazing uses a dedicated transparent material. Two small
unshadowed interior lights follow night lighting.

The shell has three LODs and a simple distant shadow proxy. Hall, canopy and parking lights follow night
lighting with their own intensities. `VisitorCentreVisual` owns generated meshes
and materials, including the attached site and temporary driveway preview, and destroys each resource once.
Generated objects are `DontSave`; the layout and public-road graph recreate them. A geometry version refreshes
older live previews and reception visuals after an editor code reload.

`VisitorCentreSiteBuilder` supplies parking pavement, 16 standard and two accessible bays, a shared access
hatch, wheel stops, a pedestrian crossing and flush route to the entrance, curbs with a driveway gap, benches,
bike racks, bollards, planters and lamps. The aisle is 6.5 m wide. The full site spans local x −20…20 m and
z −8…32 m, with a short entrance throat on the right. `VisitorCentreLayout` defines the same geometry for
construction, saved site protection and terrain ownership.

Run `AirCity.EditorTools.Airfield.VisitorCentreReview.Run` in an isolated graphics-enabled editor:

```sh
Unity -batchmode -projectPath . \
  -executeMethod AirCity.EditorTools.Airfield.VisitorCentreReview.Run \
  -logFile Logs/visitor-centre-review.log
```

The helper builds a fresh fixture without a player save and writes front, entrance, rear, whole-site and
night captures to `Captures/VisitorCentre/`. It checks that the driveway joins a real bidirectional route to
the outside road. See [the system guide](../systems/visitor-centre.md) for placement, pricing and persistence.
