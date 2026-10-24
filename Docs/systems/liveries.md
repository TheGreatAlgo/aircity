# Liveries

A livery is airline paint that doesn't depend on the aircraft type. It holds a `LiveryStyle`, a base colour plus three
zone colours, a registration colour and default registration, a paint smoothness and an optional tail logo. Each
aircraft type ships one RGB zone mask per style, plus baked shading, dirt and logo-placement maps. At runtime
`AircraftLiveryCompositor` combines them into a body texture using Burst jobs and its own BC1 encoder. The reasons:
any airline can go on any type (every type implements every style), a new airline costs no art, and memory stays
small. One body texture is shared per (type, livery), about 2.7 MB. Each aircraft adds only a registration patch of
about 0.3 MB.

## Where it lives

| Path | Role |
|---|---|
| `Assets/AirCity/Scripts/Runtime/Aircraft/Liveries/AircraftLivery.cs` | `LiveryStyle` enum; `AircraftLivery` data class; `CacheKey()` |
| `.../Liveries/AircraftLiveryAsset.cs` | ScriptableObject wrapper (**Create ▸ AirCity ▸ Aircraft Livery**). Its `OnValidate` re-applies every applier in open scenes that uses it |
| `.../Liveries/AircraftLiveryApplier.cs` | Per-aircraft component: finds the `Body` / `Registration` material slots, shares a body material per (type, livery, dirt step), keeps a per-aircraft registration texture and material. Edit mode uses MaterialPropertyBlocks |
| `.../Liveries/AircraftLiveryCompositor.cs` | Static. Builds body and registration textures (Burst jobs), caches them, `Prewarm` / `PrewarmRoutine`, `ReadBack` (GPU readback helper) |
| `.../Liveries/RegistrationFont.cs` | Glyph atlas and metrics (authored pixels, y up) |
| `Scripts/Runtime/Aircraft/AircraftType.cs` (`[Header("Paint")]`) | Per-type inputs: `atlasSize`, `texelsPerMetre`, `detail`, `dirt`, `registrationDirt`, `logoUV`, `logoBox`, `font`, `schemes[]` (style, mask, `registrationBox`), `registrationTextureWidth` / `registrationHalfHeight`, `port` / `starboard` sides, `defaultBody`, `uvTemplate`, `examples[]`; `SchemeFor(livery)` |
| `Scripts/Runtime/Rendering/Bc1TextureBuilder.cs` | Burst mip chain plus BC1 (DXT1) encoder for textures made at runtime |
| `Scripts/Editor/Aircraft/AircraftTypeImporter.cs` | `<Model>.aircraft` JSON → `AircraftType` (ScriptedImporter v3) |
| `Scripts/Editor/Aircraft/RegistrationFontImporter.cs` | `RegistrationFont.regfont` JSON → `RegistrationFont` (v1) |
| `Scripts/Editor/Aircraft/AircraftAssetPostprocessor.cs` | Texture import rules (below); imported `Body` / `Registration` materials get `_Body_Default` / `_Registration_Default` and the dirt detail map. `GetVersion()` = 7 |
| `Scripts/Editor/Aircraft/AircraftSetup.cs` | Creates missing `Liveries/<name>.asset` from `type.examples`, the prefab (applier wired to the first example, ClassicBlue), the sandbox scene (one aircraft per type × livery, dirt `(i % 5) / 4`) and thumbnails (dirt 0.15) |
| `Assets/AirCity/Liveries/*.asset` | 6 example liveries. Created once, then hand-editable and never overwritten |
| `Assets/AirCity/Liveries/Logos/*.png` | Generated logos `SunsetRed`, `RegionalAir` (256² RGBA), rewritten by every `finish.py` run |
| `Assets/AirCity/Aircraft/<Model>/Textures/` | Generated per-type inputs (table below) |
| `Assets/AirCity/Aircraft/Shared/RegistrationFont.{png,regfont}` | Glyph atlas and metrics shared by all types (rewritten by every `finish.py` run) |
| `ArtSource/Aircraft/Common/paint.py` | numpy reference: `Px` texel geometry, AA primitives, `detail_map`, `dirt_map`, `logo_uv_map`, `compose`, `apply_logo`, `layout_text` / `draw_text`, `registration_texture` |
| `ArtSource/Aircraft/Common/airlines.py` | `LIVERIES` (example airlines) and `LOGOS` (procedural logo functions) |
| `ArtSource/Aircraft/Common/font.py` | `render_atlas`: Blender's bundled Inter, emboldened in image space, ink extents measured |
| `ArtSource/Aircraft/Common/pipeline.py` | `stage2` writes every livery input; `make_registration_patch`, `unpatch_registration` (LOD1/2), `write_profile` (`.aircraft` + `.regfont`), `write_logos` |
| `ArtSource/Aircraft/Common/texturing.py` | Body atlas packing (`TEX = 2048`), Cycles bakes (pos / nrm / tag / white / AO), `save_png`, `downsample` |
| `ArtSource/Aircraft/<Model>/model.py` | `SCHEMES`, `REG_BOX`, `REG_FACES_U/V`, `REG_TEX_W`, `LOGO_BOX`, `panel_lines`, `detail_extra`, `dirt_extra` |
| `Assets/AirCity/Tests/EditMode/AircraftLiveryTests.cs` | Per-type livery, LOD, BC1 and budget tests |

### Generated inputs per type (`Aircraft/<Model>/Textures/`)

All of these are written by `finish.py` (stage 2). They are computed from the stage-1 bakes, so a mask change doesn't need `build.py`.

| File | Size: Cessna172 / Narrowbody | Content | Import rule (postprocessor) |
|---|---|---|---|
| `<M>_Mask_<Style>.png` (×4) | 2048² | R / G / B = zones 1 / 2 / 3, antialiased, 0 outside the atlas | linear, CompressedHQ (BC7), no mips, non-readable |
| `<M>_Detail.png` | 2048² | `0.42 + 0.58·AO^0.75`, × `(1 − 0.55·panel lines)`, × `detail_extra`; 1.0 outside the atlas | SingleChannel **Red** (R8), uncompressed, no mips |
| `<M>_Dirt.png` | 1024² (½ res) | URP detail albedo: 0.5 = clean, darker = grime, clipped 0.15–0.7 | linear, Compressed, clamp |
| `<M>_RegistrationDirt.png` | 512×216 / 384×268 | The dirt map resampled into the registration patch layout | same as Dirt |
| `<M>_LogoUV.png` | 512² (¼ res) | `(u·cov, v·cov, cov)`, premultiplied by coverage so bilinear sampling stays exact at edges | linear, **uncompressed** (exact coordinates), no mips |
| `<M>_Body_Default.png` | 2048² | The first `airlines.LIVERIES` entry (ClassicBlue) composed in numpy. Used as the imported Body texture and as the parity-test reference | default (sRGB), clamp, aniso 4 |
| `<M>_Registration_Default.png` | 1024×432 / 768×536 | ClassicBlue registration patch | default, NPOT none |
| `<M>_UV_Template.png` | 2048² | UV wireframe over shading, for hand-painting `customMask` / `customAlbedo` | default |
| `Shared/RegistrationFont.png` | 1024² | White glyphs, alpha = coverage, 8 columns of 128 px cells | SingleChannel **Alpha** (Alpha8), uncompressed |

Every texture under `Assets/AirCity/Aircraft/` gets `maxTextureSize = 2048`. Logos under `Assets/AirCity/Liveries/Logos/`
are imported sRGB, `alphaIsTransparency`, clamp, no mips, CompressedHQ.

## Concepts

### LiveryStyle and zones

`col = base → lerp(c1, mask.r) → lerp(c2, mask.g) → lerp(c3, mask.b) → lerp(logo.rgb, logo.a) → × detail`. Later zones
paint over earlier ones. The tooltips define zone 1 as main stripe / swoosh / belly / nose, zone 2 as pinstripe /
accent / cheatline / tips, and zone 3 as trim / tail. What each mask actually paints:

| Style (int) | Cessna172: R / G / B | Narrowbody: R / G / B |
|---|---|---|
| `Classic` (0) | cheat stripe sweeping up the fin / pinstripe above it / accents (spinner, pants, wing / stab tips, fin cap) | cheatline through the windows into the fin, stopping at the radome joint / pinstripe below / nacelles |
| `Swoosh` (1) | swoosh from the cabin up the fin / thin accent lines on its edges / accents | belly-to-tail swoosh from the wing root / band above it / whole fin + nacelles |
| `TwoTone` (2) | belly under the cheatline + pants + gear legs / cheatline / fin + spinner + wingtips | belly / cheatline / fin + nacelles |
| `Trainer` (3) | cowl + spinner + pants / wing, stab and fin tips / thin window stripe | nose cone + nacelles / sharklets + fin tip / window-line stripe |
| `Custom` (4) | no generated mask: uses `customMask` and/or `customAlbedo` | same |

- Each model's `SCHEMES` maps style **names** to `fn(p) -> (r, g, b)` numpy functions of baked 3D position, normal and
  part tag (`paint.Px`: `s` = station in metres aft of the nose, `z` = height, `nx/ny/nz`, `fus` / `wing` / `ht` /
  `vt` / `group(...)` masks, `inside` / `band` / `line` antialiased over one texel). The output is multiplied by
  `valid` and saved as `<M>_Mask_<Style>.png`.
- `AircraftType.SchemeFor(livery)` returns the scheme with the same style. **If there is none it silently falls back
  to `schemes[0]` (Classic)**, both mask and registration box. `Custom` with no `customMask` therefore paints with
  the Classic mask.

### Detail, dirt and logo maps

- **Detail**: baked AO plus panel lines, multiplied into the body texture. The composite reads only the red channel.
- **Dirt**: not baked into the body texture. It is the URP Lit **detail albedo** in `_DETAIL_SCALED` mode, where
  `albedo *= 1 + amount·(2·d − 1)`. `amount` is the applier's `dirt`. Body materials use `type.dirt`; per-aircraft
  registration materials use `type.registrationDirt`.
- **LogoUV**: where the fin logo goes. Coverage = inside `LOGO_BOX` (station s0..s1, height z0..z1, metres) × fin
  parts (`vtail` / `rudder` / `dorsal`) × `|nx| > 0.5` (fin sides only). The u axis is flipped on the `nx < 0` side,
  so the logo reads left-to-right on both sides (not mirrored). The logo is fitted "contain" into the box's metric
  aspect `(s1 − s0) / (z1 − z0)` through `(sx, sy)`.

### Registration patch and font

- On LOD0, `make_registration_patch` moves the fuselage faces inside `REG_FACES_U` (|station|) × `REG_FACES_V` (arc)
  onto a separate `Registration` material with local UVs: port strip in the **top** half, starboard in the
  **bottom** half. LOD1 and LOD2 put those faces back on `Body` with atlas UVs (`unpatch_registration`), so they
  show no letters.
- Per side, `AircraftType.RegistrationSide` holds `crop` (the main-atlas UV rect the strip shows) and a metric → UV
  map: `u = k·sign·station + tu`, `v = k·height + tv` (`sign` = +1 port, −1 starboard).
- Texture size: `registrationTextureWidth` (`REG_TEX_W`: 1024 Cessna, 768 Narrowbody) × `2·registrationHalfHeight`
  (216 / 268). The half height comes from the crop aspect and is padded to an even number so the full height is a
  multiple of 4 (BC1).
- `REG_BOX[style]` = `(station from, to, height from, to)` in fuselage metres, kept clear of that style's stripes.
  Letters are as tall as the box unless the text is too wide, and are centred in it.
- `RegistrationFont`: `chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-"`, `atlasSize` 1024, `cell` 128,
  `baseline` 26, `capTop` (measured median of HEFTIL), and per-glyph `cellX/cellY/inkLeft/inkRight`.
  `GlyphIndex` upper-cases. Any other character becomes a space.

### AircraftLivery fields

| Field | Default | Notes |
|---|---|---|
| `style` | Classic | serialized as an **int** in `.asset` files (`style: 2`) |
| `baseColor`, `color1..3` | white / navy / gold / navy | sRGB colours; quantised to 8 bit in `CacheKey` |
| `registrationColor` | navy | not in `CacheKey` |
| `defaultRegistration` | "" | used when the applier's `registration` is empty; "" = no letters |
| `paintSmoothness` | 0.55 | set on the materials; not in `CacheKey` |
| `logo` | null | any RGBA Texture2D, transparent background; in `CacheKey` as `GetHashCode()` (instance id) |
| `customMask` | null | RGB zone mask on one model's UV template (**model-specific**) |
| `customAlbedo` | null | flat-colour paint on one model's UV template. Replaces the zone colours; shading still multiplied (**model-specific**) |

`CacheKey()` = style | base | c1 | c2 | c3 | logo | customMask | customAlbedo. Equal keys share one body texture per type.

### Example airlines (`airlines.LIVERIES` → `.aircraft` `examples` → `Liveries/*.asset`)

| Name | Style | Base / c1 / c2 / c3 | Reg colour, default reg | Logo |
|---|---|---|---|---|
| ClassicBlue | Classic | `#F4F4F1` `#1B2A5C` `#C9A13A` `#1B2A5C` | `#1B2A5C` N172CB | none |
| SunsetRed | Classic | `#F5F3EE` `#A3141B` `#3A3A3E` `#A3141B` | `#2B2B2E` N4721R | SunsetRed (sun over bars) |
| SkyhawkSP | Swoosh | `#F6F6F4` `#1D3E72` `#8E98A6` `#1D3E72` | `#1D3E72` N652SP | none |
| FlightSchool | Trainer | `#F7F7F5` `#C62026` `#C62026` `#1F2F57` | `#1F2F57` N35FS | none |
| RegionalAir | TwoTone | `#F7F7F4` `#B8BEC6` `#0F6B4F` `#0F6B4F` | `#0F6B4F` N208RA | RegionalAir (disc with leaf) |
| BushYellow | TwoTone | `#F1B91C` `#1E1E1E` `#F7F7F4` `#1E1E1E` | `#1E1E1E` C-FBSH | none |

## Runtime flow

1. **Trigger.** `AircraftLiveryApplier.Apply()` runs from `OnEnable`, from `OnValidate` (delayCall), from
   `SetLivery(AircraftLivery | AircraftLiveryAsset, reg)`, `SetRegistration`, `SetDirt`, and from
   `AircraftLiveryAsset.OnValidate`. `CurrentLivery` = the runtime livery if set, otherwise `liveryAsset.livery`.
   Apply bails out if the livery, `type` or `type.detail` is null.
2. **Slots.** `FindSlots` scans child renderers for materials whose name, up to the first `" ("`, is `Body` or
   `Registration` (or equals the assigned references) and keeps the original material arrays.
3. **Body texture.** `AircraftLiveryCompositor.GetBodyTexture(type, livery)` looks up the cache
   `(type, CacheKey)`. On a miss, `ComposeBody` runs:
   - source = `customAlbedo` if set, otherwise `customMask` or `SchemeFor(l).mask`. The output size is the
     **source's** size (atlas 2048²).
   - With a logo and `type.logoUV`: the LogoUV map is read once and cached as a region; the logo is read in full.
     `sx` / `sy` are computed from the box and logo aspect ratios.
   - `ReadAll` uses the CPU pixel data if the texture is readable RGBA32. Otherwise `ReadBack` does
     `Graphics.Blit` to a temporary RT (linear for masks / detail / LogoUV, sRGB for logos / custom albedo) then
     `ReadPixels`.
   - Burst `ComposeBodyJob` (same resolution) or `ResampleBodyJob` (bilinear resampling) per texel:
     `Palette.Paint(mask)` (or the custom albedo), then `ApplyLogo`, then `Mul255(·, detail.r)`, written as RGB24
     into a mip-chain buffer. Without a source or detail map the result is a flat base colour.
   - `Bc1TextureBuilder.Build`: Burst `DownsampleJob` (2×2 box filter on sRGB bytes) per mip, then `EncodeJob`
     (stb_dxt-style principal axis plus least-squares refinement; solid blocks use precomputed 5/6-bit match
     tables). The texture is uploaded with `Apply(false, true)`, so it is non-readable and sRGB. It falls back to
     uncompressed RGB24 with mips if `CompressGeneratedTextures` is false **or the width / height isn't a multiple
     of 4**.
   - `Configure`: clamp, aniso 4, `HideFlags.DontSave`.
4. **Body material** (applier static cache, key `(type, CacheKey, dirtStep)`, `DirtSteps = 6`): a `new Material` of
   the imported `Body` material with `mainTexture`, smoothness, `_DetailAlbedoMap = type.dirt`,
   `_DetailAlbedoMapScale = QuantizedDirt` (0, 0.2, …, 1) and `_DETAIL_SCALED`. It is rebuilt only if its
   `mainTexture` no longer matches.
5. **Registration texture** (per aircraft, rebuilt on **every** `Apply`): `ComposeRegistration(type, livery, text,
   reuse)`.
   - `RegistrationBase`, cached per `(type, CacheKey)` as a managed `Color32[]` of W×2H: for each side, the
     `PixelRect(crop)` region of the mask is read back (cached per texture and side) and `CropJob` resamples and
     paints it with the palette. On the first call per type, the detail crop is also resampled into
     `regShadeCache[type]`.
   - The base is cloned, then `DrawText` runs per side: the metric `registrationBox` is converted to atlas UV, then
     to strip pixels; the text is laid out; glyphs are rasterised by `GetGlyph` (3×3 supersampled, cached per
     (font, glyph, scale)) and lerped in with `registrationColor`.
   - The `reuse` texture is destroyed (compressed textures can't be rewritten). `ShadePackJob` multiplies by the
     shade, then `Bc1TextureBuilder.Build(W, 2H)` runs.
6. **Assign.** In play mode, renderer `sharedMaterials` are swapped: shared body material, plus a per-aircraft
   `registrationInstance` (a copy of the imported Registration material) with `registrationDirt` at the
   *unquantised* dirt. In edit mode, MaterialPropertyBlocks set `_BaseMap` / `_BaseColorMap` / `_MainTex`,
   smoothness and `_DetailAlbedoMapScale = dirt`, so nothing is written into scenes or prefabs. `OnDisable` clears
   the blocks in edit mode.

### Caches

| Cache | Key | Holds | Freed by |
|---|---|---|---|
| `bodyCache` | (type, CacheKey) | BC1 body Texture2D | `ClearCaches`; edit mode keeps at most 24 (LRU `EvictOldest`), play mode never evicts |
| `regBaseCache` | (type, CacheKey) | unshaded registration base `Color32[]` (Cessna 1024×432×4 ≈ 1.8 MB, Narrowbody ≈ 1.6 MB managed) | `ClearCaches`, eviction |
| `regShadeCache` | type | registration shade bytes | `ClearCaches` |
| `regionCache` | (texture, region id) | readback copies: port / starboard crops of masks and detail, the full LogoUV (512²) and the full font atlas (1024² ≈ 4 MB managed) | `ClearCaches` |
| `glyphCache` | (font, glyph, round(scale·1000)) | rasterised coverage | `ClearCaches` |
| applier `bodyMaterials` (static) | (type, CacheKey, dirt step) | body Material | `AircraftLiveryApplier.ClearSharedCache()` (which also calls `ClearCaches`) |

### Cost and budgets

| Item | Figure | Source |
|---|---|---|
| New livery (per type) | ~50 ms, 2048² BC1 + mips ≈ 2.7 MB GPU | README (M2, editor) / format maths |
| New aircraft (registration) | ~15 ms; 1024×432 or 768×536 BC1 ≈ 0.3 MB | README / format maths |
| 100 aircraft, 10 liveries | ~4–5 s, ~55 MB GPU; test asserts < 64 MB | README / `Fleet_100Aircraft_10Liveries_StaysWithinBudget` |
| Transient per new body | mask and detail readbacks (2 × 16 MiB RGBA32) + RGB24 chain (≈ 16 MiB) | code |
| Uncompressed body (`CompressGeneratedTextures = false`) | RGB24 + mips ≈ 16.8 MB of data; the code comment says ~22 MB (RGBA on the GPU) | format maths / compositor comment |

All jobs are `.Complete()`d right away on the main thread, so a new livery causes a frame hitch. Call
`AircraftLiveryCompositor.Prewarm(type, livery)` or `StartCoroutine(PrewarmRoutine(type, liveries))` (one per frame)
behind a loading screen. Prewarm builds the body and the registration base.

## Generator vs runtime parity

`paint.py` composes the reference liveries (`Body_Default`, previews in `ArtSource/Aircraft/<Model>/Build/liveries/`,
which is gitignored) and the C# compositor must reproduce it. Both do all colour maths on **sRGB-encoded** values.

| Python (`paint.py`) | C# (`AircraftLiveryCompositor`) |
|---|---|
| `compose(masks, colors, ones)`: sequential lerp base→c1(r)→c2(g)→c3(b) | `Palette.Paint` (byte `Lerp255`, exact rounding) |
| `apply_logo(col, logo_uv, logo, box_aspect)`, contain fit `(sx, sy)` | `ApplyLogo` + `LogoInput` (same `sx` / `sy` formula, un-premultiplies LogoUV) |
| `albedo = unshaded * detail` | `Mul255(c, detail.r)` |
| `bilinear` (pixel centres at +0.5, clamp) | `Bilinear` (8-bit fixed-point weights) |
| `layout_text`: track 0.16·cap, space 0.42·cap, `scale = floor(scale·50)/50`, centred | `DrawText` (same constants and quantisation) |
| `draw_text`: 3×3 supersample, samples restricted to the glyph cell | `GetGlyph` (same; glyph origin rounded to integer pixels for caching) |
| `reg_text_rect` | metric box → UV in `ComposeRegistration` |
| `registration_texture`: crops the composed *unshaded albedo*, port on top, then × detail | `RegistrationBase` crops the *mask* and paints it, port on top; `ShadePackJob` × shade |

**Test:** `AircraftLiveryTests.BodyTexture_MatchesGeneratorReference` (per type) composes `ClassicBlue` in C# and
compares it with `profile.defaultBody`. It requires a mean absolute difference < 2.5/255 and fewer than 1/500 of
texels with a summed RGB difference > 60. It covers only ClassicBlue (Classic style, **no logo**). The logo path,
other styles and registration letters have no pixel parity test; `Logo_IsPaintedOnTheFin` only checks that the
logo covers 0.1–5 % of the atlas.

**Change together:**
- The composition order or maths: `paint.compose` / `apply_logo` and `Palette.Paint` / `ApplyLogo` /
  `ComposeBodyJob` / `ResampleBodyJob`, and the docstrings at the top of both files.
- Text layout constants (track, space, 1/50 quantisation, supersampling): `paint.layout_text` / `draw_text` and
  `DrawText` / `GetGlyph`.
- LogoUV encoding (premultiplied `u·cov, v·cov, cov`; downsample factor): `pipeline.stage2`, `ApplyLogo`, and the
  `_LogoUV` import rule (must stay uncompressed and linear).
- The `.aircraft` / `.regfont` JSON shape: `pipeline.write_profile` with `AircraftTypeImporter` /
  `RegistrationFontImporter` (bump the `ScriptedImporter` version) and the `AircraftType` / `RegistrationFont`
  fields.
- Texture file suffixes (`_Mask_`, `_Detail`, `_Dirt`, `_RegistrationDirt`, `_LogoUV`, `RegistrationFont`): the
  pipeline and `OnPreprocessTexture`. Bump `AircraftAssetPostprocessor.GetVersion()`.

## Common tasks

**Add an airline livery (game content).**
- From code / game data: build an `AircraftLivery` and call
  `aircraft.GetComponent<AircraftLiveryApplier>().SetLivery(livery, "N123AB")`. Nothing else is needed: it works on
  every type.
- As an asset: **Create ▸ AirCity ▸ Aircraft Livery** in `Assets/AirCity/Liveries/`. `AircraftSetup.AllLiveries()`
  only searches that folder, and the sandbox and thumbnails use it. An existing sandbox isn't updated: rebuild it
  with **AirCity ▸ Dev ▸ Rebuild Sandbox Scene**.
- As a shipped example: add an entry to `airlines.LIVERIES`
  `name: (style, base, c1, c2, c3, regcol, default reg, logo name | None)`. Re-run `finish.py` for **every** model
  (each `.aircraft` carries the examples), then **AirCity ▸ Aircraft ▸ Create Missing Prefabs and Example
  Liveries** (or `AircraftSetup.Bootstrap`). Keep `ClassicBlue` first (see gotchas). Editing an existing entry
  does **not** update its `.asset`: edit the asset, or delete it and rerun.

**Add a LiveryStyle.** Every type must implement every style, otherwise `SchemeFor` silently uses Classic.
1. `AircraftLivery.cs`: append the enum value with an explicit number (`= 5`). Never renumber: assets store the int.
2. In **every** `ArtSource/Aircraft/<Model>/model.py`: write `scheme_<x>(p) -> (r, g, b)` and add it to `SCHEMES`
   under the enum name exactly (the importer uses a case-sensitive `Enum.TryParse`; unknown names are dropped with an
   import warning). Add `REG_BOX["<Name>"]` clear of the new stripes and inside `REG_FACES_U/V`.
3. Re-run `finish.py --model <M>` for each model. Use `--previews --liveries <a livery using the style>` to look.
4. Update `AircraftLiveryTests.Profile_IsImportedCompletely`, which asserts `schemes.Length == 4`.
5. Optionally add an example to `airlines.LIVERIES`. Update the style lists in `README.md` and `ArtSource/README.md`.

**Change the registration.**
- Text: `applier.SetRegistration("N55XY")` or the `registration` field. An empty value uses
  `livery.defaultRegistration`; if both are empty there are no letters. Only `A–Z 0–9 -` render; lowercase is
  upper-cased; anything else becomes a space.
- Colour: `livery.registrationColor`. It is not part of the cache key, so it costs no new body texture.
- Position / size per style: `REG_BOX` in `model.py`, then `finish.py`. Letters outside the patch faces
  (`REG_FACES_U/V`) are clipped. For sharper letters raise `REG_TEX_W` (memory scales with it).
- Glyph set: `font.CHARS` / `CELL` / `BASELINE` in `font.py`, then regenerate. The atlas and `.regfont` are shared
  by all types.

**Add a tail logo.**
- Any RGBA `Texture2D` with a transparent background. Put it in `Assets/AirCity/Liveries/Logos/` to get the sRGB /
  alpha / no-mip import, and use a name that isn't in `airlines.LOGOS`: `finish.py` rewrites those files. Logos are
  read through an sRGB readback, so they must be sRGB textures.
- Assign it to `livery.logo`. It is fitted (contain) into each type's `LOGO_BOX` on both fin sides; square-ish logos
  read best. To move or resize the area, edit `LOGO_BOX` and re-run `finish.py`.
- Procedural example logos: add a function to `airlines.LOGOS` and reference its key as the 8th `LIVERIES` field.

**Custom paint.** Set `style = Custom` and paint on `<M>_UV_Template.png`:
- `customMask`: RGB zones. Import it with **sRGB off**, because masks are read back as linear data.
- `customAlbedo`: full colours, read as sRGB.

Both only fit the type whose template they were painted on. Keep their sizes multiples of 4, or the body falls back
to uncompressed RGB24.

**Add an aircraft type (livery side).**
- `model.py` needs a `SCHEMES` entry for all 4 generated styles, plus `REG_BOX`, `REG_FACES_U/V`, `REG_TEX_W` and
  `LOGO_BOX`. The mesh must use materials named exactly `Body` and `Registration`.
- Add `[TestFixture("<Model>")]` to `AircraftLiveryTests`, and add the model to the hard-coded list in
  `AircraftTypeTests.Logo_IsPaintedOnTheFin`.

## Invariants and gotchas

- **ClassicBlue must stay first in `airlines.LIVERIES`.** The pipeline writes `Body_Default` from
  `next(iter(LIVERIES))`, but the parity test composes `"ClassicBlue"` by name. Tests also use `SunsetRed`,
  `SkyhawkSP` and `BushYellow` by name, and `Logo_IsPaintedOnTheFin` needs at least one example with a logo.
- `AircraftSetup` never overwrites `Liveries/*.asset` or prefabs. Delete them to have them recreated.
- `.aircraft` examples with an unknown style silently become `Classic` (`Enum.TryParse` with no warning). Bad hex
  colours become magenta.
- `CacheKey` excludes `paintSmoothness`. In play mode the shared body material keeps the smoothness of whichever
  livery created it first; edit mode sets it per renderer.
- `CacheKey` identifies logos and custom textures by instance (`GetHashCode`). Two Texture2D objects with identical
  pixels build two body textures.
- Every `Apply()` rebuilds the aircraft's registration texture (one Burst crop / text / BC1 pass), including
  `SetDirt` and `SetRegistration`. "Cheap" means no new body texture, not free.
- Body materials share dirt in 6 steps (`QuantizedDirt`). Registration materials and edit-mode blocks use the raw
  value.
- `_DETAIL_SCALED` is enabled on the imported Body / Registration materials by the postprocessor so player builds
  keep the variant the runtime materials need. Don't remove it.
- Generated textures are non-readable and compressed. Inspect them with `AircraftLiveryCompositor.ReadBack(tex,
  false, rect)` and destroy the copy. Readback uses `Graphics.Blit` / `ReadPixels`, so it needs a graphics device.
- Source maps are read back with the colour space their import implies: masks, detail, LogoUV and font as linear;
  logos and custom albedo as sRGB. An import-setting change silently shifts colours.
- In edit mode only 24 body textures are cached. Evicted textures are destroyed even if another aircraft in the
  scene still shows one (re-`Apply` it). Inspector colour tweaks each create a new key.
- Use `AircraftLiveryApplier.ClearSharedCache()` between levels. `AircraftLiveryCompositor.ClearCaches()` alone
  leaves the applier's materials pointing at destroyed textures. Aircraft still in use must `Apply()` again.
- Burst jobs use `CompileSynchronously = true`, so the first livery in an editor session waits for Burst. The
  `Bc1TextureBuilder` jobs run with `DisableSafetyChecks`; the BC1 test is their safety net.
- BC1 needs both dimensions to be multiples of 4. Otherwise `Bc1TextureBuilder` returns RGB24 without warning.
- Registration patch layout: port = top half (rows H..2H−1), starboard = bottom half (Unity row 0 = bottom).
  Python and C# both assume it.
- Aircraft art under `Aircraft/<Model>/Textures`, `Shared/RegistrationFont.*` and `Liveries/Logos` is regenerated.
  Change `ArtSource/Aircraft`, not the PNGs.

## Tests

`Assets/AirCity/Tests/EditMode/AircraftLiveryTests.cs`, run once per type (`[TestFixture("Cessna172")]`,
`[TestFixture("Narrowbody")]`); `TearDown` calls `ClearSharedCache`.

| Test | Checks |
|---|---|
| `Profile_IsImportedCompletely` | 4 schemes with masks, detail, font plus atlas, glyph arrays, both registration crops, dirt, LogoUV, ground shadow, prefab |
| `BodyTexture_MatchesGeneratorReference` | C# ClassicBlue vs numpy `Body_Default` (mean < 2.5/255, outliers < 1/500) |
| `IdenticalLiveries_ShareOneTexture` | same content → same texture; a different `color1` → a new one |
| `Registration_PaintsLetters` | "N172CB" changes 1–30 % of patch texels |
| `Registration_IgnoresUnsupportedCharacters` | `"c-f? bsh!"` doesn't throw |
| `Prefab_IsPaintedInEditMode` | `Body_LOD0`'s property block `_BaseMap` is the applier's body texture; LODGroup exists |
| `Lods_CutTrianglesAndDrawCalls` | 3 LODs, decreasing triangles, ≤ 24 / 4 / 2 draws, no `Registration` material beyond LOD0, every renderer in exactly one LOD, cross-fade |
| `Bc1Encoder_MatchesUnityCompressor` | Burst BC1 error ≤ 1.25 × `Texture2D.Compress(HQ)` + 0.1; same mip count |
| `Fleet_100Aircraft_10Liveries_StaysWithinBudget` | 10 distinct bodies, 100 distinct registrations, all DXT1 and non-readable, GPU bytes < 64 MB |

Also in `AircraftTypeTests.cs`: `Logo_IsPaintedOnTheFin` (the logo changes 0.1–5 % of the atlas on both types) and
`Dirt_SharesMaterialsPerStep` (dirt 0 / 1 quantise exactly and share one body texture).

Run (editor closed): `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile Logs/tests.log`.
Timings and memory are printed with `Debug.Log` (grep the log for `body vs reference`, `fleet:`, `BC1`).
