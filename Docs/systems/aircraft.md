# Aircraft

Every aircraft type is generated procedurally in Blender by one shared pipeline (`ArtSource/Aircraft/Common`). Each
type adds only a small definition: `model.py` holds the data and `parts.py` holds the geometry. The pipeline writes
an FBX, textures and a `<Model>.aircraft` JSON file into `Assets/AirCity/Aircraft/<Model>/`. Unity imports the JSON
as an `AircraftType` ScriptableObject. Gameplay reads dimensions, wheels, service points, lights and performance
from that asset only, never from mesh positions or per-model constants. One prefab per type carries the runtime
components (picking/outline, livery, lights, ground motion, control surfaces, spinners). The scale target is ~100
aircraft of mixed types, mostly zoomed out, so the far LOD is 2 draw calls, every material is shared, and light
glows run on the GPU. Two types exist: `Cessna172` (see `Docs/assets/Cessna172.md`) and `Narrowbody`
(`Docs/assets/Narrowbody.md`). Liveries are documented in `Docs/systems/liveries.md`; this doc covers only how they
hook in.

## Where it lives

`Common/` means `ArtSource/Aircraft/Common/`, and `Scripts/` means `Assets/AirCity/Scripts/`.

| Path | Role |
|---|---|
| `ArtSource/Aircraft/build.py` | Stage 1 entry point (`pipeline.stage1`) |
| `ArtSource/Aircraft/finish.py` | Stage 2 entry point (`pipeline.stage2`) |
| `ArtSource/Aircraft/Common/pipeline.py` | Both stages: materials (`MATS`), `Mover`, UV rules, previews, registration patch, LODs, collider, ground shadow, `write_profile`, `turn_around`/`flatten`, FBX export |
| `Common/texturing.py` | `TEX = 2048` atlas, UV island packing, Cycles bakes (pos / nrm / tag / coverage / AO), PNG I/O |
| `Common/paint.py` | `Px` (per-texel geometry), detail / dirt / logo-UV maps, `compose`, registration text; mirrored by the C# compositor |
| `Common/geom.py` | `MeshBuilder`, PCHIP, NACA 4-digit airfoils, lofts |
| `Common/shapes.py` | `drape_pane` (windows), `lifting_surface`, `revolve`, `tube`, `wheel` |
| `Common/fuselage.py` | `Fuselage` base (superellipse profiles), `Loft` (tags `fus_port` / `fus_stbd`, metric UVs) |
| `Common/airlines.py` | Example liveries (`LIVERIES`, first = default) and procedural logos (`LOGOS`) |
| `Common/font.py` | Registration glyph atlas (`CHARS` A–Z 0–9 -) |
| `ArtSource/Aircraft/<Model>/model.py`, `parts.py` | Per-type definition and geometry |
| `ArtSource/Aircraft/<Model>/Build/` | Scratch output (gitignored): `work/stage1.blend`, `work/bake_maps.npz`, `debug_*.png`, `previews/`, `liveries/`, `<Model>.blend` |
| `Assets/AirCity/Aircraft/<Model>/Models/<Model>.fbx` | Generated model (LOD0 body + movers, LOD1/2 bodies, `Collider`, `GroundShadow`) |
| `Assets/AirCity/Aircraft/<Model>/Textures/` | Generated maps (see "Stage 2" below) |
| `Assets/AirCity/Aircraft/<Model>/<Model>.aircraft` | Generated type data (JSON, Unity space) → `AircraftType` |
| `Assets/AirCity/Aircraft/<Model>/<Model>.prefab` | Created once by `AircraftSetup`, then hand-editable; never overwritten |
| `Assets/AirCity/Aircraft/<Model>/Thumbnails/` | `AircraftSetup.RenderThumbnails` output, 384×256 PNGs |
| `Assets/AirCity/Aircraft/Shared/RegistrationFont.png` + `.regfont` | Shared font, rewritten by every `finish.py` run |
| `Assets/AirCity/Liveries/*.asset`, `Liveries/Logos/*.png` | Livery assets (created once) and logos (rewritten by every `finish.py` run) |
| `Assets/AirCity/Materials/AircraftOutline.mat`, `AircraftLightGlow.mat` | Shared materials that the prefabs reference |
| `Scripts/Runtime/Aircraft/AircraftType.cs` | The type asset, plus the enums `AircraftCategory`, `ServicePointType`, `AircraftLightType`; derived `Wingspan`, `Length`, `Height`, `SizeCode`, `RunwayLength`, `Wheelbase`, `MinTurnRadius` |
| `Scripts/Runtime/Aircraft/Aircraft.cs` | Root component: `type`, `TryPick`, `Highlight` outline, `CurrentLod(cam)`, `WorldPosition(point)` |
| `Scripts/Runtime/Aircraft/AircraftLights.cs` | Light switches, the per-type glow mesh, spot lights near the camera, global `Glow` |
| `Scripts/Runtime/Aircraft/AircraftGroundMotion.cs` | Wheel spin + nose-gear steering: `Drive(speed, steer)`, `TurnRadius(type)` |
| `Scripts/Runtime/Aircraft/AircraftControlSurfaces.cs` | Elevator / aileron / rudder / flap deflection, found by name prefix |
| `Scripts/Runtime/Aircraft/PropellerSpinner.cs` | Spins `Propeller` / `Fan_*` about local Z (`rpm`, `spoolTime`) |
| `Scripts/Runtime/Aircraft/Liveries/` | `AircraftLiveryApplier`, compositor, livery assets, `RegistrationFont` (see `liveries.md`) |
| `Scripts/Editor/Aircraft/AircraftTypeImporter.cs` | `[ScriptedImporter(3, "aircraft")]`: JSON → `AircraftType` |
| `Scripts/Editor/Aircraft/RegistrationFontImporter.cs` | `[ScriptedImporter(1, "regfont")]` |
| `Scripts/Editor/Aircraft/AircraftAssetPostprocessor.cs` | Texture and model import rules, nesting restore, collider, Trim palette merge, LODGroup (`GetVersion() => 7`) |
| `Scripts/Editor/Aircraft/AircraftSetup.cs` | `Bootstrap`, prefab + example-livery creation, sandbox scene, thumbnails |
| `Scripts/Editor/Dev/SceneCapture.cs` | Batch render of a scene to PNG (`-captureLod`, `-captureNight`, `-captureSelect`) |
| `Scripts/Runtime/Dev/DevSandboxControls.cs` | Sandbox keys: lights, engines (`rpm`), taxi (`Drive`), dirt, day/night (`AircraftLights.Glow`) |
| `Scripts/Runtime/Airfield/RunwayRules.cs`, `Runway.cs`, `TaxiwayNetwork.cs` | Consumers of `takeoffDistance` / `landingDistance` (with surface / weather factors), `SizeCode`, `unpavedRunways`, `maxTakeoffMass` (wear) |
| `Scripts/Runtime/Traffic/*` | Consumers of the flight figures `taxiSpeed`, `rotateSpeed`, `approachSpeed`, `climbRate`, `climbSpeed` (m/s; `PERFORMANCE` keys `taxi_speed` … `climb_speed`), plus `Wheelbase`, `maxSteeringAngle`, `bounds`; see [traffic.md](traffic.md) |
| `Assets/AirCity/Shaders/AircraftLightGlow.shader` | Fixed-pixel-size additive light glows |
| `Assets/AirCity/Shaders/AircraftOutline.shader` | Inverted-hull hover / selection outline |
| `Assets/AirCity/Shaders/AircraftGroundShadow.shader` | Multiplicative contact shadow quad |
| `Assets/AirCity/Tests/EditMode/AircraftTypeTests.cs` | Gameplay data, model/data agreement, prefab components, picking, steering |
| `Assets/AirCity/Tests/EditMode/AircraftLiveryTests.cs` | Per-model fixture: profile import, LOD budget, generator ↔ C# paint match, fleet memory budget |

## How it works

### Coordinate frames

| | Forward (nose) | Up | Port (left) | Starboard (right) | Origin |
|---|---|---|---|---|---|
| Blender (`model.py`, `parts.py`) | −Y | +Z | +X | −X | ground under the main-gear axle |
| Unity (`.aircraft`, runtime) | +Z | +Y | −X | +X | same point |

- `pipeline.to_unity(x, y, z) = (-x, z, -y)`. `write_profile` converts every gameplay position and direction with
  it, so author `GEAR` / `POINTS` / `LIGHTS` in Blender space and never pre-convert them.
- Stations: `s` = metres aft of the nose tip (the Cessna's spinner tip). Blender `Y = s - S_REF`, where `S_REF` is
  the station of the main-gear axle. Each `parts.py` defines `Y(s)` and `P(x, s, z)`.
- **Turn-around:** Unity imports this FBX setup as Blender (x, y, z) → Unity (x, z, y), so a nose at Blender −Y would
  face Unity −Z. `turn_around()` rotates the whole export 180° about Z: mesh data `R·v`, object matrices `R·M·R⁻¹`.
  Pivots move with the aircraft and every part keeps the meaning of its local axes. The pipeline turns it back after
  export for the previews and `Build/<Model>.blend`.
- **Flatten:** Blender's FBX exporter converts axes correctly only for the root's direct children. Deeper nodes come
  out with a stray 270° rotation. `flatten()` re-parents each nested object to the root under the name
  `<Parent>__<Name>`, and `AircraftAssetPostprocessor.RestoreNesting` puts it back under `<Parent>` (keeping its world
  placement) and strips the prefix. Exported this way: `NoseGear__Wheel_Nose*` and `Propeller__Propeller_LOD1`.
- `AircraftTypeTests.Model_MatchesTypeData` guards both tricks. Every wheel transform must be within 5 cm of its
  `.aircraft` position, and `Body_LOD0`'s mesh must reach past the nose gear in +Z.

### Stage 1: `build.py` (geometry, atlas, bakes; 2–4 min)

1. `model.build()` returns `(body MeshBuilder, [Mover])`. `assign_uvs` gives faces without analytic UVs a
   projection: `UV_TOP_BOTTOM` tags are projected from above / below (left and right halves apart), `UV_SIDE` tags
   from the sides, everything else along its dominant axis. `DENSITY` / `BOTTOM_DENSITY` scale the texel density
   per tag. The fuselage `Loft` writes metric UVs (u = ±station, v = arc).
2. Each mover becomes its own object with its origin at `pivot`. A `hinge` direction rotates the object so the
   hinge becomes its local X (`axis="x"`: flaps, ailerons, elevators; in Unity it points to starboard) or local Z
   (`axis="z"`: rudders; Unity local Y). `parent=` nests it (for example wheels under `NoseGear`).
3. `--texture`: `texturing.pack_body_uvs` packs every `Body`-material face of every object into one 0..1 atlas and
   keeps the metric UVs in the `UVraw` layer. `bake_maps` then bakes, in Cycles on the GPU (device hard-coded to
   `METAL`): world position, normal, part tag (`tag_gid`, 64 ids), coverage, valid (incl. margin) and AO
   (`AO_DISTANCE`, 192 samples).
4. Outputs `Build/work/stage1.blend` and `bake_maps.npz`, plus `Build/debug_ao.png` and `debug_tags.png`. It prints
   `TRIANGLES`, coverage % and the fuselage px/m.
5. Flags: `--check` (inverted-winding report per object:tag), `--preview [--engine] [--views a,b] [--prefix geo]
   [--w 1280 --h 760]`, and `--cull` (backface culling in previews, which shows what Unity would drop). Without
   `--texture` nothing in `work/` is written.

### Stage 2: `finish.py` (maps, LODs, FBX, type data; ~20 s)

Opens `work/stage1.blend` and `bake_maps.npz` (so it never re-runs `build()`), then writes to
`Assets/AirCity/Aircraft/<Model>/`:

| Output | Source | Size |
|---|---|---|
| `Textures/<M>_Detail.png` | AO + `panel_lines(p)` + `detail_extra(p)` | 2048² |
| `Textures/<M>_Dirt.png` | `paint.dirt_map` + `dirt_extra(p)`, 0.5 = clean | 1024² |
| `Textures/<M>_LogoUV.png` | `logo_uv_map(p, LOGO_BOX)`, premultiplied (u·cov, v·cov, cov) | 512² |
| `Textures/<M>_Mask_<Style>.png` ×4 | `SCHEMES[style](p)` → RGB zone masks | 2048² |
| `Textures/<M>_UV_Template.png` | UV wireframe over the detail map, for painters | 2048² |
| `Textures/<M>_Registration_Default.png`, `_RegistrationDirt.png` | registration patch (port = top half, starboard = bottom half) | `REG_TEX_W` × 2·half_h (dirt at ½) |
| `Textures/<M>_Body_Default.png` | first entry of `airlines.LIVERIES`, composed in numpy | 2048² |
| `Textures/<M>_GroundShadow.png` | `ground_shadow()` | 256² |
| `<M>.aircraft` | `write_profile` (paint data + `GEAR`, `PERFORMANCE`, `POINTS`, `LIGHTS`, `bounds`, examples) | |
| `Models/<M>.fbx` | LODs + movers + `Collider` + `GroundShadow`, turned around and flattened | |
| `../Shared/RegistrationFont.{png,regfont}`, `../../Liveries/Logos/*.png` | shared by all types | |

The registration patch (`make_registration_patch`) moves the `fus_port` / `fus_stbd` faces whose `UVraw` lies
inside `REG_FACES_U` (|station|) × `REG_FACES_V` (arc) onto the `Registration` material with a local 0..1 UV. It
records the per-side mapping `u = k·sign·station + tu`, `v = k·arc + tv` and the crop in the atlas. `half_h` is
rounded to a multiple of 4 for BC1.

Flags: `--previews [--liveries A,B] [--views a,b]` (default: first livery; views `front34,rear34`), `--lod N` (which
LOD is visible in previews and the saved `.blend`), `--w/--h`.

### Import: FBX + `.aircraft` → prefab

- **`AircraftTypeImporter`** parses the JSON. Enum names (`category`, point / light `type`, scheme `style`) must
  match the C# enums: unknown values log an import warning and fall back to the first value; an unknown style is
  skipped. Texture paths are relative to the `.aircraft` file and loaded with `DependsOnArtifact`, so the type
  reimports when a texture changes.
- **`AircraftAssetPostprocessor`** (only under `Assets/AirCity/Aircraft/`):
  - Model: `globalScale 1`, `bakeAxisConversion`, normals imported, `isReadable = true` during import. Then, in
    order: `RestoreNesting` → `Collider` becomes a `MeshCollider` on layer `Aircraft` (renderer removed) →
    `GroundShadow` renderer gets no shadows or probes → `MergeIntoTrim` → `BuildLodGroup` → `UploadMeshData(true)`
    (drops the CPU copy).
  - Trim: the 12 flat materials (`Glass, Metal, Chrome, DarkMetal, Rubber, Dark, PropBlade, PropTip, LightRed,
    LightGreen, LightWhite, LightLens`) are merged into one `Trim` submesh. It samples 16×16 point-filtered palettes
    (`<M>_TrimAlbedo`, `_TrimMetalGloss`, `_TrimEmission`; a 4×4-texel cell per material, 16 slots) that are stored
    as sub-assets of the FBX. The colours come from `TryGetLook`, **not** from `pipeline.MATS`.
  - `Body` / `Registration` get URP Lit with `<M>_Body_Default` / `_Registration_Default`, plus the dirt map as
    `_DetailAlbedoMap` (scale 0.3, `_DETAIL_SCALED`).
  - `GroundShadow` material: the shader is loaded by path (`AircraftGroundShadow.shader`, with a dependency), and
    `_MainTex` is set to `<M>_GroundShadow.png`.
  - Textures (max 2048): masks BC7 linear with no mips; detail R8 / font Alpha8 uncompressed with no mips; LogoUV
    uncompressed and linear; GroundShadow Alpha8; dirt linear compressed; the rest clamp + aniso 4. Logos are sRGB
    with alpha, HQ-compressed, no mips.
- **`AircraftSetup.CreatePrefab`** (via Bootstrap or the menu, only if the prefab is missing) instantiates the FBX
  and adds: `Aircraft` (outline material), `AircraftLiveryApplier` (`Body` / `Registration` materials by name, first
  example livery), `AircraftControlSurfaces.FindParts()`, `AircraftGroundMotion.FindParts(type)`, `PropellerSpinner`
  on `Propeller` / `Fan_*`, and `AircraftLights` (glow material). It saves the result as a variant of the model, so
  re-exported meshes flow through.

### Runtime components (all on the prefab root except the spinners)

| Component | Driven by | Mechanism |
|---|---|---|
| `Aircraft` | `Highlight = None/Hover/Selected`; `TryPick(ray, out a)` | Raycast on layer `Aircraft` → `GetComponentInParent<Aircraft>` |
| `AircraftLiveryApplier` | `SetLivery`, `SetRegistration`, `SetDirt` | See `liveries.md`; dirt is quantized to `DirtSteps = 6` |
| `AircraftLights` | bools `navigation` (default on), `beacon`, `strobes`, `landing`, `taxi`, `logo`; static `Glow` | `LateUpdate` → `SetShaderUserValue(StateBits)`; spot lights toggled |
| `AircraftGroundMotion` | `Drive(speed m/s, steer −1..1)` | `LateUpdate`: wheels `rest * AngleAxis(spin, right)`, nose gear `rest * AngleAxis(steer·max, up)` |
| `AircraftControlSurfaces` (`[ExecuteAlways]`) | `elevator`, `aileron`, `rudder` (−1..1), `flaps` (0..1) | `rest * AngleAxis(deg, right)` (rudder: `up`); limits 28/23, 20/15, 17, 30° |
| `PropellerSpinner` (on `Propeller`, `Fan_*`) | `rpm` | `MoveTowards` with `spoolTime`, rotate about local −Z (clockwise from behind) |

- Ground motion finds wheels by the exact names in `type.wheels` and the nose gear by the name `NoseGear`. Control
  surfaces are found by name prefix (`Elevator`, `Aileron_L`, `Aileron_R`, `Rudder`, `Flap_L`, `Flap_R`), skipping
  names that contain `_LOD`.
- `TurnRadius(type) = Wheelbase / tan(|steer angle|)`. `Wheelbase = steeringPivot.z`.
- `SizeCode` from wingspan: A < 15 m, B < 24, C < 36, D < 52, E < 65, else F. `RunwayLength = max(takeoff, landing)`.
  The airfield applies surface and weather factors in `RunwayRules`.

### LODs and draw calls

`make_lods` builds:
- LOD0 = `Body_LOD0` + the movers. It has the registration patch.
- LOD1 = `Body_LOD1`: the movers that are not in `LOD1_KEEP`, joined in at rest, then the whole mesh decimated to
  `LOD_RATIOS[1]`. Each `LOD1_KEEP` part also gets a `<Part>_LOD1` copy decimated to 0.3 and parented to the full
  part, so the same spinner turns it.
- LOD2 = `Body_LOD2`: everything joined and decimated to `LOD_RATIOS[2]`.

LOD1 and LOD2 put the registration faces back on `Body` with the atlas UVs. `Collider` is `Body_LOD2` decimated by
`COLLIDER_RATIO`.

In Unity, `BuildLodGroup` assigns `_LODn` renderers to level n and everything else to LOD0, except `GroundShadow`,
which stays outside the group. Heights are `LodHeights = { 0.18, 0.06, 0.002 }`, multiplied by
`QualitySettings.lodBias` (PC 2, Mobile 1). Fade mode is CrossFade (dithered, animated). Unity 6.6 removed
`ModelImporter.lodScreenPercentages`, so the heights are set on the LODGroup here.

Measured in the test log (`Lods_CutTrianglesAndDrawCalls`, budget `maxDraws = {24, 4, 2}`):

| LOD | Cessna172 | Narrowbody |
|---|---|---|
| 0 | 37,283 tris / 17 draws | 46,027 / 21 |
| 1 | 9,729 / 4 | 10,399 / 2 |
| 2 | 2,676 / 2 | 2,836 / 2 |

Each renderer draws at most 3 submeshes: Body + Registration (LOD0 body only) + Trim. A far aircraft costs Body_LOD2
(2 draws) + `GroundShadow` (1) + `LightGlows` (1). Textures are not LOD-switched: mips cover that.

### Picking and outline

- Picking uses the `Collider` child: a coarse `MeshCollider` on layer `Aircraft` (layer 8 in `TagManager.asset`).
  `TryPick` falls back to the default layers if the layer is missing.
- The outline is an inverted hull (`AirCity/AircraftOutline`, `Cull Front`). It is pushed away from the camera by
  `0.15 + 0.006·distance` m and out along the clip-space normal by `_Width` pixels. `Aircraft` draws it from
  `RenderPipelineManager.beginCameraRendering` with `Graphics.RenderMesh`, only for highlighted aircraft and only for
  the renderers of `CurrentLod(cam)`. It skips `Propeller*` / `Fan*` (they would flicker) and Preview / Reflection
  cameras. Colours: hover white 75%, selected amber; width 2.5 px.

### Lights and glow

- `AircraftLights.GlowMesh(type)` builds one camera-facing quad per `type.lights` entry, cached per type and shared by
  every aircraft. Vertex layout: position = light, normal = direction, colour, uv0 = corner, uv1 = (cos of half
  `spread`, or −2 when spread ≥ 359°; light type), uv2 = (flash phase offset, size). Size multipliers: Landing 1.6,
  Strobe 1.3, Logo 0.8, others 1, times `clamp(Wingspan / 11, 1, 1.5)`. Mesh bounds are the type bounds grown by
  4 m. The renderer is a `LightGlows` child with `HideFlags.DontSave`, so it is never saved into scenes or prefabs.
- **User-value bits** (`StateBits`, set per renderer, so all aircraft share `AircraftLightGlow.mat`):

  | Bit | 1 | 2 | 4 | 8 | 16 | 32 | 8..15 |
  |---|---|---|---|---|---|---|---|
  | Group | navigation | beacon | strobes | landing | taxi | logo | per-aircraft flash phase (0–254, from name/position hash) |

  The shader maps light type → bit with `GroupBit = {1, 4, 2, 8, 16, 32}` (enum order Navigation, Strobe, Beacon,
  Landing, Taxi, Logo).
- Shader (`AirCity/AircraftLightGlow`, additive, `Transparent+50`):
  - A quad shows only inside its sector (navigation lights) and only when its group bit is on.
  - Strobe: double flash every 1.1 s (×3). Beacon: 0.18 s flash per second (×1.3).
  - The quad is pulled 0.25 m toward the camera. Radius `_Radius` 0.9 m, clamped to `_MinPixels` 3.5 · size …
    `_MaxPixels` 28 px, using `abs(UNITY_MATRIX_P[1][1])` (Metal render-texture flip).
  - Brightness `_Intensity` 3 × `lerp(_DayGlow 0.35, 1, _AirCityLightGlow)`.
- **`AircraftLights.Glow`** (static) sets the global `_AirCityLightGlow`: 0 = day, 1 = night. `DevSandboxControls`
  and `SceneCapture -captureNight` set it.
- Landing and taxi lights also get real `Spot` lights (`Spot <name>` children, `DontSave`, no shadows, intensity 40,
  range `max(Length·2.5, 35)`). They are enabled only when `Camera.main` is within `spotLightDistance` (180 m), the
  switch is on and `Glow > 0.05`.
- Phase offset within one aircraft (`AircraftLights.PhaseOffset`): all of its strobes flash together; a beacon
  shining downward (`direction.y < -0.5`, the belly beacon) gets +0.5 s, so top and bottom alternate.
  `Beacons_AlternateTopAndBottom` guards it.

### Ground shadow

`ground_shadow()` renders, in Eevee from below with an orthographic camera, the height of the lowest surface into a
256² EXR. It splits the height into bands (`[0, .12, .3, .55, 1] × height`), blurs each band in proportion to its
height and fades the result at the border. Output: `<M>_GroundShadow.png` and a `GroundShadow` quad at z = 0.03 m
(3 cm) covering the footprint plus a margin.

`AirCity/AircraftGroundShadow` multiplies (`Blend DstColor Zero`, `Offset -1,-1`, `Transparent-100`) by
`1 − occ·lerp(_NearStrength 0.3, _Strength 0.7, GetMainLightShadowFade)`. Inside the URP shadow distance (150 m on
PC, 50 m Mobile) only a light AO pool is drawn; beyond it the full baked shadow keeps aircraft grounded. The quad
is outside the LODGroup and draws at every distance.

### Livery hook (summary)

`AircraftType` carries the paint inputs: `schemes` (mask + registration box per `LiveryStyle`), `detail`, `dirt`,
`registrationDirt`, `logoUV`, `logoBox`, `font`, the registration `port` / `starboard` mappings and `examples`.
`AircraftLiveryApplier` on the root composes per (type, livery) via `AircraftLiveryCompositor` (Burst, BC1 via
`Bc1TextureBuilder`). It previews through MaterialPropertyBlocks in edit mode and swaps materials in play mode.
`paint.compose` and the C# compositor must stay identical (`BodyTexture_MatchesGeneratorReference`).

## Common tasks

Shell setup used below:

```sh
B=/Applications/Blender.app/Contents/MacOS/Blender          # Blender 5.2+
U=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
```

**What to rerun after a change:**

| Change | Blender | Unity |
|---|---|---|
| `parts.py` geometry, mover names / pivots / hinges / parents, `UV_*`, `DENSITY`, `AO_DISTANCE`, `S_REF` | stage 1 `--texture`, then stage 2 | refresh; prefab may need `FindParts` (see Invariants) |
| `SCHEMES`, `REG_*`, `LOGO_BOX`, `panel_lines`, `detail_extra`, `dirt_extra` | stage 2 | refresh; tests |
| `GEAR`, `POINTS`, `LIGHTS`, `PERFORMANCE`, `LOD_RATIOS`, `LOD1_KEEP`, `COLLIDER_RATIO`, `DISPLAY_NAME`, `CATEGORY` | stage 2 | refresh; tests |
| `AircraftAssetPostprocessor` rules (`LodHeights`, `TryGetLook`, texture settings) | none | bump `GetVersion()` so the FBX / textures reimport |
| `AircraftTypeImporter` parsing | none | bump `[ScriptedImporter(3, …)]` version |

"Refresh" means focusing the editor, or running any batch command (Unity imports changed assets on startup). The
editor must not be open on the project while batchmode runs. CLAUDE.md suggests wrapping `-executeMethod` runs in
`timeout 300` and never passing method arguments through an unquoted zsh variable.

**Add a new aircraft type**
1. Copy the closest type folder to `ArtSource/Aircraft/<Model>/`. `NAME` must equal the folder name (`load_model`
   asserts it).
2. In `parts.py`, subclass `fuselage.Fuselage` (`S0`, `S1`, `zt`, `zb`, `hw`, `fm`, `nt`, `nb` profiles) and loft it
   with `Loft`. The `fus_port` / `fus_stbd` tags are required: texel density and the registration patch use them.
   Keep the tag prefixes the schemes rely on (`fus*`, `wing*`, `htail` / `elevator`, `vtail` / `rudder` / `dorsal`).
   `Body` is the only painted material; all others must come from `pipeline.MATS`.
3. In `model.py`, provide everything in ArtSource/README.md's list:
   - `CATEGORY` must be an `AircraftCategory` name (add it to the enum if new).
   - `SCHEMES` and `REG_BOX` must cover Classic, Swoosh, TwoTone and Trainer.
   - Movers must use the runtime names (`Propeller` / `Fan_*`, `Rudder*`, `Elevator*`, `Aileron_L*` / `_R*`,
     `Flap_L*` / `_R*`, `NoseGear`, plus wheels named as in `GEAR`). Give swept surfaces a `hinge`.
   - The red nav light goes on Blender +X (port).
4. Run stage 1, check normals and the geometry, then stage 2 with previews:
   ```sh
   cd ArtSource/Aircraft
   $B -b --factory-startup --python build.py -- --model <Model> --preview --cull --views side_ortho,front34   # quick look
   $B -b --factory-startup --python build.py -- --model <Model> --texture --check
   $B -b --factory-startup --python finish.py -- --model <Model> --previews --liveries ClassicBlue,SunsetRed --views front34,rear34
   ```
   Check close-up views against real reference photos before showing the user (see the asset docs).
5. Create the prefab and the example liveries, then run the tests:
   ```sh
   $U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.Bootstrap -logFile Logs/bootstrap.log
   $U -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile Logs/tests.log
   ```
6. Add the model to the tests: `[TestFixture("<Model>")]` in `AircraftLiveryTests` and the `new[] { "Cessna172",
   "Narrowbody" }` lists in `AircraftTypeTests`. Add a dimension check against the real aircraft.
7. Sandbox: `Bootstrap` creates `Sandbox.unity` only if it is missing. To add the new type to an existing sandbox, use
   **AirCity ▸ Dev ▸ Rebuild Sandbox Scene**, which overwrites the scene, then rerun
   `AirCity.EditorTools.Airfield.AirfieldSetup.Bootstrap` to put the airport back. Update the README aircraft and
   LOD tables and add a `Docs/assets/<Model>.md`.

**Change geometry and regenerate:** edit `parts.py` (and `build()` in `model.py` if movers change), then run
`build.py … --texture --check` followed by `finish.py … --previews`. Look at `Build/previews/`, refresh Unity and run
the tests. If mover names, pivots or hinge directions changed, see "Prefab components cache parts" below.

**Add a light:** append `(type, name, position, direction, spread°, colour)` to `LIGHTS`. `type` is an
`AircraftLightType` name. Use Blender space, with spread 360 for all-round. Optionally add a lens to
`build_details()` with `LightRed` / `LightGreen` / `LightWhite` / `LightLens`; that is geometry, so it needs stage 1.
Then run stage 2. A new light **type** needs: the enum, a switch field + `StateBits` bit in `AircraftLights`,
`GroupBit` in the shader (indexed `min(type, 5)`, so a 7th type silently shares Logo's bit), a `Flash` pattern if
needed, and the size multiplier in `GlowMesh`.

**Add a service point:** append `(type, name, position, outward normal, (w, h))` to `POINTS`. `type` is a
`ServicePointType` name. The position is the door sill centre, hatch or attach point, or the ground point for chocks
and tie-downs. `_side(s, z, sign)` puts a point on the fuselage skin. Run stage 2. Gameplay reads it with
`type.TryGetPoint` / `PointsOfType` and `aircraft.WorldPosition(point)`.

**Tweak LODs:**
- Triangle budget: `LOD_RATIOS`, `LOD1_KEEP`, `COLLIDER_RATIO` in `model.py`, then stage 2.
- Switch distances: `LodHeights` in the postprocessor, plus a `GetVersion()` bump.
- Check the `Lods_CutTrianglesAndDrawCalls` log line and update the README LOD table.
- Inspect far LODs up close: `finish.py … --previews --lod 1`, or `SceneCapture … -captureLod 2`.

## Invariants and gotchas

- **Generated files are overwritten** on every regeneration: `Models/`, `Textures/`, `*.aircraft`, `*.regfont`,
  `Liveries/Logos`. Change the generator instead. Prefabs, livery assets and thumbnails are created once and never
  overwritten.
- **Gameplay reads `AircraftType` only.** No mesh-derived positions and no per-model constants in runtime code
  (there are none today outside the tests).
- **Axes:** author data in Blender space. Port is Blender +X and Unity −X. Don't bypass `to_unity`, `turn_around`
  or `flatten`. `Model_MatchesTypeData` catches a mesh/data mismatch.
- **`__` is reserved in object names** (nesting marker). Only one level of nesting is safe: a mover's `parent` must
  itself sit directly under the root, as `NoseGear` does. `RestoreNesting` splits on the first `__`.
- **`_LOD<n>` suffix** decides LOD membership. Never end a mover name with it.
- **Mesh must be readable in `OnPostprocessModel`.** The postprocessor sets `isReadable = true` and then calls
  `UploadMeshData(true)`. Without that, `MergeIntoTrim` would write empty meshes (it logs an error and skips instead).
- **Bump `GetVersion()`** (currently 7) whenever the postprocessor rules change, or existing models keep the old
  import.
- **Trim colours in Unity come from `TryGetLook`**, not `pipeline.MATS`. The two already differ (for example `Glass`
  0.015/0.022/0.03 in Blender vs 0.07/0.09/0.11 in Unity), so Blender previews are not colour-exact. A new flat
  material must be added to `MATS`, `TrimMaterials` and `TryGetLook` (16 palette slots, 12 used). Otherwise it stays
  a separate submesh with an extra draw, which breaks the ≤ 3 materials rule.
- **Prefab components cache parts.** `FindParts` ran once when the prefab was created. It stored transforms and
  rest rotations (`AircraftControlSurfaces`, `AircraftGroundMotion`). After renaming or adding movers, or changing
  a hinge orientation, the cached references go null (silently skipped) or the stale `rest` snaps the part to the
  old orientation. `AircraftControlSurfaces` is `[ExecuteAlways]`, so this also happens in edit mode. Fix: Reset
  `AircraftControlSurfaces` in the prefab, call `AircraftGroundMotion.FindParts(type)` (no menu), or delete the prefab
  and rerun Bootstrap. Recreating the prefab gives it a new GUID, so sandbox instances break; rebuild the sandbox.
- **Shared materials:** prefabs reference `Assets/AirCity/Materials/AircraftOutline.mat` and `AircraftLightGlow.mat`.
  Deleting either breaks the prefabs (delete the prefabs too and rerun Bootstrap). Never make per-aircraft glow
  materials: state goes through the renderer user value.
- **`Shader.Find` can fail in a postprocessor** on a fresh import. The ground-shadow shader is loaded by path with
  `context.DependsOnArtifact`; do the same for any new shader.
- **`QualitySettings.maximumLODLevel` must stay 0** (`QualitySettings_DoNotCapLods`). Batch code that changes
  QualitySettings must restore them, because Unity saves them on exit (`SceneCapture` does this).
- **Pixel-sized shaders need `abs(UNITY_MATRIX_P[1][1])`** (Metal render-texture flip), or glows collapse to their
  minimum in captures and thumbnails.
- **Contact shadow sits at 3 cm.** Airfield layers stack below it (`AirfieldHeights`).
- **Stage 2 depends on stage 1's `work/`.** `build()` is not re-run in stage 2, so geometry or mover changes without
  `build.py --texture` are silently ignored by `finish.py`.
- **Bakes use Cycles with `compute_device_type = "METAL"`** (Mac GPU). Another OS needs `texturing._cycles` changed.
- **`--check` reports inner surfaces** (for example the jet's intake, fan face and fan duct are built with
  `flip=True`). These are expected. An unexpected flipped tag means faces Unity will cull.
- **Registration:** `REG_BOX[style]` must lie inside the `REG_FACES_U` / `REG_FACES_V` window and clear of that
  scheme's stripes. The patch exists on `Body_LOD0` only (the LOD test asserts LOD1/2 have no `Registration`).
- **The default livery is the first key of `airlines.LIVERIES`** (ClassicBlue). `BodyTexture_MatchesGeneratorReference`
  composes "ClassicBlue" in C# and compares it with `<M>_Body_Default.png`, so reordering `LIVERIES` breaks it.
- **Every type must implement all four non-Custom styles.** `Profile_IsImportedCompletely` asserts 4 schemes.
- **Layer `Aircraft` must exist.** Without it the collider import warns, colliders stay on Default, and `TryPick`
  raycasts `Physics.DefaultRaycastLayers` (so it can hit non-aircraft first).
- `Aircraft`, `AircraftLights` and `AircraftControlSurfaces` are `[ExecuteAlways]`. In the editor, `GetComponent`
  can return a fake-null object, so don't use `?.` / `??` on Unity objects.

## Tests and verification

| Test | Guards |
|---|---|
| `AircraftTypeTests.Dimensions_MatchTheRealAircraftClass` | Span / length / height / wheelbase near the real aircraft; size codes A / C; nose ahead of the nose gear; tyres on y = 0 (logs the numbers) |
| `…Model_MatchesTypeData` | Turn-around + flatten: wheel transforms within 5 cm of the data; mesh nose along +Z |
| `…ServicePoints_AreOnTheCorrectSide` | Jet boards from the left (−X) and loads cargo on the right; L1 sill 2.5–3.6 m, front quarter; Cessna fuel on the wing top |
| `…NavigationLights_RedOnTheLeft` | Red nav light at x < −0.4·span, green at > +0.4·span; a beacon exists |
| `…Prefab_HasEveryGameplayComponent` | All components present; every wheel found; nose gear; `MeshCollider` on layer `Aircraft`; ground-shadow shader; elevators / flaps / rudders found |
| `…Picking_HitsTheAircraft` | `TryPick` hits from above the bounds centre and misses beside the aircraft |
| `…GroundMotion_SpinsWheelsAndSteers` | Wheel turns; nose gear reaches `maxSteeringAngle`; jet `MinTurnRadius` 2–6 m |
| `…Logo_IsPaintedOnTheFin` | Logo changes 0.1–5% of the atlas |
| `…QualitySettings_DoNotCapLods`, `…Dirt_SharesMaterialsPerStep` | LOD cap; dirt material sharing |
| `AircraftLiveryTests(<Model>).Profile_IsImportedCompletely` | 4 schemes, masks, detail, font, crops, dirt, logoUV, groundShadow, prefab |
| `…Lods_CutTrianglesAndDrawCalls` | 3 LODs, falling triangle counts, draws ≤ {24, 4, 2}, no CPU mesh copies, no registration at LOD1/2, every renderer in exactly one LOD (except `GroundShadow` / `LightGlows`), CrossFade |
| `…BodyTexture_MatchesGeneratorReference` | C# compositor matches numpy `paint.compose` (mean diff < 2.5/255) |
| `…Fleet_100Aircraft_10Liveries_StaysWithinBudget` | 10 shared bodies, 100 registration textures, all DXT1, < 64 MB |
| `AirfieldTests` | Runway / taxiway rules against both types |

Run the tests (exit code 2 = failures; grep `Logs/tests.log` for `error CS`):

```sh
$U -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile Logs/tests.log
```

**Blender previews** (in `ArtSource/Aircraft/<Model>/Build/previews/`; views are keys of `model.VIEWS`; a negative
lens means an orthographic scale):

```sh
cd ArtSource/Aircraft
$B -b --factory-startup --python build.py -- --model Narrowbody --preview --cull --views side_ortho,front34        # geo_*.png, untextured
$B -b --factory-startup --python finish.py -- --model Narrowbody --previews --liveries ClassicBlue,SunsetRed --views nose_34,photo   # livery_<L>_<view>.png
$B -b --factory-startup --python finish.py -- --model Cessna172 --previews --lod 1 --views front34                  # far LOD look
```

**Unity captures** (the Sandbox names aircraft `"<Model> <Livery>"`):

```sh
$U -batchmode -projectPath . -executeMethod AirCity.EditorTools.Dev.SceneCapture.FromCommandLine \
   -captureScene Assets/AirCity/Scenes/Sandbox.unity -captureOut Captures/sandbox.png -logFile Logs/capture.log \
   [-captureSize 1920x1080] [-captureView "px,py,pz:tx,ty,tz"]... [-captureLod 2] [-captureNight] [-captureSelect "Cessna172 SunsetRed"]
$U -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Aircraft.AircraftSetup.RenderThumbnails -logFile Logs/thumbs.log
```

`SceneCapture` renders a warm-up frame, disables animated cross-fades for stills, and restores
`maximumLODLevel`. Each `-captureView` writes `<out>_<n>.png`.
