# Flying school (generated building and site)

The school's art is generated in C# by `FlyingSchoolBuilder`
(`Assets/AirCity/Scripts/Runtime/Airfield/Building/FlyingSchoolBuilder.cs`) from the dimensions in
`FlyingSchoolLayout`. Nothing is imported and nothing is hand-edited: change the builder. Bump
`FlyingSchoolBuilder.GeometryVersion` when the geometry changes.

See [the system guide](../systems/flying-school.md) for placement, lessons and persistence.

## Design

An original small-airfield clubhouse, 26 × 11 m, in white horizontal boarding on a dark plinth:

- **Briefing hall:** the tall centre, 12 m wide, under one roof pitch that rises to 5.3 m over a fully
  glazed front facing the flight line. Real panes show the briefing table, planning counter, wall chart and
  roof ties inside.
- **Wings:** two lower wings (classrooms and offices) with punched windows and a shallow roof falling away
  from the hall, edged with a signal-orange fascia.
- **Lookout:** a small glazed cab on one wing, with a radio mast: the instructors' view of the circuit.
- **Veranda and terrace:** a timber veranda along the hall's front, picnic tables, benches and a low white
  fence with a gate open at each tie-down.
- **Landside:** an entrance porch, cycle hoops, planters, a forecourt flush with the car park, 16 marked bays
  (two accessible, flanking a hatched path to the door), kerbs, two lamps and a driveway throat that rises to
  meet the public road's deck.
- **Windsock:** on a mast at the terrace's corner. `FlyingSchoolWindsock` turns it downwind of
  `Airport.windFrom`.
- **Tie-down rings:** yellow rings set in each stand where the trainer's `TieDown` service points anchor it,
  from `AircraftType.points`. On grass each ring sits in a small concrete anchor pad and white corner boards
  mark the tie-down, since a grass stand has no pavement or paint of its own.

The lettering "FLIGHT SCHOOL" on both public faces is mesh strokes, independent of any font.

## Structure

| Part | Material | Notes |
|---|---|---|
| Structure LOD0–2 | `AirCity/Building` | Colour in vertices; windows use the shader's glass channel and glow at night. |
| Glazing LOD0–2 | transparent URP Lit | The hall's front and the lookout's panes. |
| Shadow shell | `AirCity/Building`, shadows only | The simplest structure casts for every LOD. |
| Ground | `AirCity/Building` | Terrace, forecourt, car park, throat. Matches `FlyingSchoolLayout.PavedPolygons`. |
| Site furniture | `AirCity/Building` | Markings, kerbs, fence, furniture, lamps, mast, tie-down rings. |
| Windsock | `AirCity/Building` | A separate child so it can turn. |

LOD 1 drops the board shadow gaps and roof seams; LOD 2 also drops the furnishings, mullions and signs.
The visual root reuses `VisitorCentreVisual` to own its generated meshes and to switch its real lights
(hall, lookout, porch, veranda and two car-park lamps) with `AircraftLights.Glow`.

Interior surfaces seen through the real panes are built with a share of the shader's glass channel
(`lit`), brightened to keep their day colour, so the hall and lookout read as lit after dark without
depending on scene lights. They share one window bay so the glow is even.

The flight line's pavement and paint are ordinary stands drawn by `StandBuilder`.

## Visual review

```
Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Airfield.FlyingSchoolReview.Run -logFile Logs/flying-school-review.log
```

Writes to `Captures/FlyingSchool/`: `airside`, `terrace`, `landside`, `entrance`, `flightline`, `site`,
`night-airside`, `night-landside`, the circuit views (`circuit-crosswind-turn`, `circuit-downwind`,
`circuit-from-ground`, `circuit-final-turn`, `circuit-final`, `touch-and-go`) and `returned`.

`FlyingSchoolTests.Model_StaysInsideItsSiteAndBuildsEveryLevelOfDetail` checks that every generated mesh
stays inside the protected site for three taxilane widths and both driveway sides.

The toolbar icon `flying-school` (a straight-winged trainer inside its circuit) is drawn by
`ArtSource/UI/icons.py`.
