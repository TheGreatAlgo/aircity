# AirCity: notes for AI assistants

Unity 6000.6.7f1, URP, PC + Mac (minimum spec ~RTX 3060). Start with `Docs/README.md`: an overview plus one doc
per system (aircraft, liveries, airfield, maintenance, dev tools) and per asset. Then README.md and
ArtSource/README.md. When you change a system, update its doc in `Docs/` in the same change.

## Conventions
- Game code lives under `Assets/AirCity/Scripts/{Runtime,Editor}`, in assemblies `AirCity.Runtime` / `AirCity.Editor`
  and namespaces `AirCity.<Area>` (editor code: `AirCity.EditorTools.<Area>`). Tests go in `Assets/AirCity/Tests/EditMode`.
- Input uses the Input System package only (`activeInputHandler` = new). Don't use `UnityEngine.Input`.
- Art is generated, not hand-edited: aircraft come from `ArtSource/Aircraft` (one shared pipeline in `Common/`,
  a `model.py` + `parts.py` per type), vehicles and the depot from `ArtSource/Vehicles`. Everything under
  `Assets/AirCity/Aircraft/<Model>/Models|Textures`, `Assets/AirCity/Vehicles/<Model>/Models|Textures`,
  `*.aircraft`, `*.vehicle`, `*.regfont` and the airline logos in `Liveries/Logos` (those named in `airlines.LOGOS`) is
  overwritten on regeneration.
- Gameplay reads aircraft data from `AircraftType` (dimensions, wheels, service points, lights, performance), never
  from mesh positions or hard-coded per-model numbers. Runway / taxiway rules live in `RunwayRules`. Aircraft local space: origin on the ground under the main gear,
  +Z nose, +X right.
- Prefabs and livery assets are created once by `AircraftSetup` and are then hand-editable (never overwritten).
- Airfields (`Assets/AirCity/Scripts/Runtime/Airfield`) save only their layout; `Building/*Builder` regenerate the
  meshes. State that changes often (wear, snow, grass, the ploughs / mowers at work) goes through `SurfaceState` into
  vertex channels, never a rebuild. Ground textures come from `ArtSource/Airfield/textures.py`.
- Maintenance vehicles (`Assets/AirCity/Scripts/Runtime/Vehicles`) are simulated by their team (`VehicleTeam`, game
  seconds, no visuals needed: tests run it headless), and every team is stepped together by `AirportMaintenance`
  (which also keeps them clear of aircraft and each other); `Vehicle` only shows where the team has them. The pass
  pattern lives in `StripWork` and is mirrored in `AirfieldCommon.hlsl` `StripWorked`: change both together.
- Service roads are `TaxiwayEdge`s with `road = true`: aircraft routing must keep leaving them out.
- Hot per-pixel or per-entity work goes in Burst jobs (see `AircraftLiveryCompositor`). Runtime-made textures go
  through `Bc1TextureBuilder`, not `Texture2D.Compress` (which is slow and main-thread only).

## Verifying changes (no GUI needed)
- Compile + tests: `Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode.xml" -logFile Logs/tests.log`
  (exit code 2 = test failures; grep the log for `error CS` for compile errors).
- Look at it: `SceneCapture.FromCommandLine` (see README) renders a scene to `Captures/*.png`.
  A capture taken right after a scene opens needs a warm-up frame; SceneCapture already renders one.
- The editor must not be open on the project while batchmode runs (project lock).
- `-executeMethod` without `-quit` idles forever if Unity doesn't find the method, for example when zsh passes an
  unquoted `$ARGS` variable as one argument. Write the arguments inline and wrap runs in `timeout 300`.

## Gotchas found so far
- Unity 6.6 removed `ModelImporter.lodScreenPercentages`. Set LOD heights on the LODGroup in `OnPostprocessModel`.
- `FindObjectsByType` overloads that take `FindObjectsSortMode` are deprecated.
- `Profiler.GetRuntimeMemorySizeLong` over-reports script-created textures in the editor. Budget from the format/mips.
- In the editor, `GetComponent` can return a fake-null object, so don't use `??` on it.
- `OnPostprocessModel` sees mesh data only if the model imports with Read/Write on. Otherwise `mesh.vertices` etc.
  return empty arrays and a rebuilt mesh comes out empty. Aircraft import readable, and the postprocessor calls
  `UploadMeshData(true)` when it is done.
- Bump `AircraftAssetPostprocessor.GetVersion()` when import rules change, so existing models reimport.
- `QualitySettings` changed from code in batchmode are saved to `ProjectSettings/` when Unity exits. Put them
  back before exiting (as `SceneCapture` does for `-captureLod`). `QualitySettings_DoNotCapLods` guards the LOD cap.
- `UNITY_MATRIX_P[1][1]` is negative when a camera renders into a texture on Metal (flipped projection). Shaders that
  size things in pixels must use `abs()` or they collapse to their minimum size in captures and thumbnails.
- Airfield layers overlap by stacking a few mm apart (`AirfieldHeights`), all under the aircraft contact shadow's 3 cm.
  Don't cut pavements against each other; raise the layer that should win.
- `pass` is a reserved word in HLSL (Metal): a variable named `pass` breaks every shader including the file, and
  the airfield turns magenta.
- Blender's FBX exporter (as the pipeline uses it) maps Blender (x, y, z) to Unity (x, z, y) and mis-converts nodes
  deeper than the root's children. So the pipeline turns aircraft 180° for export and flattens nested parts to
  `<Parent>__<Name>`, which the postprocessor re-nests. `AircraftTypeTests.Model_MatchesTypeData` guards both.
- In an asset postprocessor, `Shader.Find` can fail for a project shader on a fresh import. Load the shader by path
  with `context.DependsOnArtifact`.
- Generated prefabs reference shared materials in `Assets/AirCity/Materials`; deleting one breaks the prefabs
  (delete the prefabs too and rerun Bootstrap).
- The project is in linear colour space, so IMGUI linearizes the colours it's given and sRGB values come out
  washed out (a near-black panel turns slate grey). Pass HUD colours through `DevGui.GUI(c)` / `DevGui.Tag(c)`.
- Unity objects such as `MaterialPropertyBlock` can't be created in a MonoBehaviour's field initializers. That
  includes fields of plain classes held in such fields. EditMode tests don't catch this; it throws only in Play
  mode. Create them on first use.
