# Aircraft hangar models

The first hangar family is an 18 × 16 m clear-span metal building for one light aircraft. The doorway is
14 m wide and 4.5 m high; eaves are 5 m high and the ridge reaches 6.5 m. The roof projects 0.3 m around
the footprint. The building's origin is the centre of its doorway, +Z points outside and the shell extends
along negative Z.
The concrete floor sits at `SmallHangar.FloorHeight` (0.075 m above the site datum), with the resident
aircraft presented just above it so the airport's underlying grass cannot show through the interior.

## Appearance and source

`Scripts/Runtime/Airfield/Building/HangarBuilder.cs` generates the complete building from code. Warm grey
ribbed cladding, a slate pitched roof, muted blue doors, darker wainscot, exposed portal frames, rooflights,
gutters and downpipes distinguish it from a terminal or vehicle depot. The right wall has a personnel door;
a bench and storage cabinet sit at the back, clear of the central aircraft envelope.

Eight sliding leaves telescope into the two front cheeks, four per side. The open pose keeps the entire
14 m opening clear without extending beyond the building's footprint. Door tracks and all exterior details
remain within the same projected footprint used for construction checks.

The generator uses the airport's existing `AirCity/Building` material and vertex colours: one visible
renderer at the active detail level, plus a simple shadow-only shell. The inset shadow shell preserves
the building's silhouette without the corrugation and thin roof seams casting noisy subpixel shadows.
Three generated LODs progressively remove corrugation, seams, small fixtures and interior details.
The front has a simple numbered plaque derived from the hangar name. Generated children and meshes are `DontSave`; the `SmallHangar`
layout component recreates them when enabled. Door changes rebuild only the shell; the resident aircraft
is a separate normal aircraft owned by `SmallHangar`. A connected bay also generates a concrete throat
and yellow centreline to the taxiway edge. The throat slopes from the raised floor to the pavement, owns
its ground/vegetation footprint and refreshes when the taxiway layout changes. Residents park nose-in;
the entry and hand push-back are checked with whole-aircraft bounds against the walls and door cheeks.

Visual references reviewed: [Wessex sliding aircraft hangar doors](https://www.wessexindustrialdoors.com/Our-Products/Industrial-Doors/aircraft-hangar-doors),
[Wide Span Sheds aircraft hangars](https://www.sheds.co.nz/industrial-sheds/aircraft-hangars/) and
[Greeley-Weld airport hangars](https://www.flygxy.com/hangars.html). These inform the wide opening, corrugated
cladding, shallow pitched roof and clear-span proportions; the model is original generated geometry.

## Executive and airline hangars

The executive model has a single 80 m lift door and an open floor with three marked aircraft parking spots.
The airline model uses a shallow barrel roof and an 84 m lift door. Both include personnel access, opaque
clerestory glazing and roof framing. Off-white metal siding and pale doors pair with a satin zinc-toned roof.
The skylights sit in the roof mesh with thin flashing and a central mullion. On the airline model they follow
the roof facets. The connected apron is a single mesh with lead-in paint for every spot.

`python3 ArtSource/Airfield/hangar_surfaces.py` generates the tileable 512 × 512 material mask. Its channels
carry profiled steel siding with fasteners, standing-seam roofing, concrete aggregate and slab joints,
and sectional door folds. The Building shader uses one mipmapped sample on these surfaces for colour and
gloss variation, with splash weathering at the base of the cladding. Each building still uses one visible mesh
and a simple shadow caster at the active LOD.

The existing night setting turns on 12 downward interior lights in four rows in each executive or airline hangar.
The lights illuminate parked aircraft and are limited to the interior, with shadow casting disabled. Fixture
lenses, a soft indirect fill on the interior surfaces and a mild glow through the glazing share the building mesh.

## Review

**AirCity > Airfield > Create Small Hangar Review** creates `Scenes/SmallHangars.unity`: three hangars,
a resident Starling A4, a taxiway and a small grass runway. It is a development fixture; Career and Sandbox
use the normal shared construction tools and save system.

To generate the scene and daylight captures from the command line:

```sh
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath . \
  -executeMethod AirCity.EditorTools.Airfield.SmallHangarReview.Run \
  -logFile Logs/small-hangars-review.log
```

The command exits after writing open/resident, closed, side and airport-scale views into
`Captures/SmallHangars/`. It also runs a real bay → stand → bay round trip and writes `taxi-stand.png`,
`taxi-approach.png`, `taxi-doorway.png` and `taxi-stored.png`. It checks generated LOD bounds against the
construction footprint and requires the resident to finish both trips. Use these captures for visual QA
after changing the generator or movement.

`AirCity.EditorTools.Airfield.SmallHangarHudReview.Run` opens this fixture in Play mode with the shared
building tools. Run it in a graphics-enabled editor to capture the placement palette, vacant lease fields,
occupied inspector and closed-door state under `Captures/SmallHangars/Hud/`. It also exercises keyboard
focus in the tenant field: typing and Backspace must edit text without moving the camera or deleting the
selected hangar. The review uses this standalone fixture and does not alter a player save.
