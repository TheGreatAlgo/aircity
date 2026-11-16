# Deicing equipment

`DeicingTruck`, `DeicingTank`, `DeicingTanker` and `DeicingRecycler` are generated, unbranded equipment for
player-built drive-through deicing pads and their shared supply areas.
The truck has a cab-over road chassis, insulated fluid body, slew bearing, articulated lower/upper boom,
telescopic extension and an enclosed yellow operator cabin at the boom tip. The cabin has side glazing,
a windscreen/wiper, door, access step and forward spray monitor with contact feelers. It stays level as the
shoulder and elbow move. Fill couplings, manholes, vehicle steps and roof rails complete the truck.
The 30,000-litre storage station has a horizontal insulated vessel, separate saddles, open containment bund,
pump cabinet, filter, tanker fill, hose reel, roof walkway, ladder, rails, bollards and floodlight.

The enclosed tip-cabin arrangement references the
[Global G-2200 deicer](https://globalgroundsupport.com/project/g-2200-deicer/) and
[Oshkosh Tempest enclosed-basket boom](https://oshkoshaerotech.com/hubfs/pdf/Tempest-i-De-icer-_QR-C2024.pdf?hsLang=en).
Truck-body proportions use the commercial 2.5 m-wide deicer class. The articulated equipment and mechanical
ranges are game specifications; the asset is not a manufacturer reproduction or certification.

| Asset | Dimensions and interfaces |
|---|---|
| `DeicingTruck` | About 12.0 m long including the tip cabin/nozzle, 2.5 m body width (mirrors project farther), 5.70 m wheelbase, 0.51 m wheel radius. White body, slate equipment band, yellow lower arm, grey upper arm and yellow enclosed tip cabin. |
| `DeicingTank` | 8.6 × 4.9 m bund footprint, 4.35 m top rail height, 30,000 L usable fluid storage. Separate tanker-fill and truck hose interfaces; capped supplier inlets on both sides. |
| `DeicingTanker` | About 11.6 m long, 2.5 m body width (3.13 m with mirrors), 5.70 m front wheelbase and six 0.51 m-radius wheels. A cylindrical 20,000 L delivery vessel, tandem rear axles, top hatch walkway, rear pump cabinet and animated hose reel. |
| `DeicingRecycler` | 4.6 × 2.6 m centre-origin skid, about 2.3 m high, with an enclosed process cabinet, controls, filter bottles, guarded pump and sealed recovery receiver. External connections terminate underneath; no visible pipe network. |

Both models use one baked material per renderer, a 1024² albedo with AO/grime and a metallic/smoothness atlas.
Spray-truck geometry has about 35,000 triangles at LOD0; storage tank about 22,000; delivery tanker about
37,800; recycler about 16,300. LOD1/2 ratios are 0.30/0.09 for both trucks and 0.32/0.10 for the stationary assets.
Moving truck parts retain separate reducing LOD meshes, avoiding
frozen booms at distance. Interface coordinates use Unity local metres (+X right, +Y up, +Z forward), origin
on the ground under the rear axle; the tank's origin is its bund's near centreline.
The dense six-wheel delivery tanker keeps Smart Project's combined atlas (`UV_SMART_ATLAS`) because Blender
5.2's second island-pack operation collapses it. Other models keep their existing unwrap; the shared pipeline
rejects a collapsed zero-area UV atlas before it can overwrite textured assets.

The generated truck is flat until `DeicingRig` reparents its moving pivots while preserving their rest transforms:

| Part | Rest pivot (x, y, z) | Motion |
|---|---|---|
| `BoomYaw` | (0, 2.60, 1.20) | Yaw about local Y; pedestal and shoulder fork. |
| `BoomLift` | (0, 2.80, 1.20) | Shoulder X rotation; lower arm has an 8.5308° upward rest slope. |
| `BoomElbow` | (0, 3.40, 5.20) | Elbow X rotation; horizontal upper arm in its rest pose. |
| `BoomExtend` | (0, 3.40, 7.30) | Upper-arm local +Z extension, up to 3 m. |
| `BoomCabin` | (0, 3.40, 8.60) | Enclosed tip cabin; counter-rotates to remain upright. |
| `SprayNozzle` | (0, 4.25, 9.45) | Independent aim in front of the cabin; physical outlet is 0.47 m forward. |

The lower arm is 4.04475 m long; the upper arm is 3.4 m plus up to 3 m of telescopic travel. Maximum straightened
reach to the cabin pivot is 10.44475 m. The cabin-to-nozzle offset is (0, 0.85, 0.85) m. Shared resource metadata
records these values for the runtime inverse-kinematics solver. Truck movement and spraying read the headless
service simulation; the art does not consume fluid or release aircraft.

The tanker wheels are `Wheel_FL/FR`, `Wheel_ML/MR` at z = 0 and `Wheel_RL/RR` at z = -1.3 m; the front pair
at z = 5.7 m steers. Its `HoseReel` pivot is (-1.0, 1.39, -2.10) m and rotates about local X. The tanker hose
outlet is (-1.25, 1.0, -2.0) m. The storage tank's supplier inlet at (+2.42, 0.86, 1.51) m faces the delivery lane
when the tank turns 180° about its bund centre. Its separate spray-truck hose remains (-2.12, 1.43, 6.02) m.
These anchors are stored in `DeicingAssets`; gameplay drives hose connecting/unloading/disconnecting states.
The recycler's ground origin is its skid centre; it mounts at supply-area `Point(-4.5, -8)` only after purchase.

The recovery skid is a compact game representation of the process-equipment role described by
[Inland Technologies](https://inlandgroup.aero/inland). Its model does not depict a complete industrial
distillation plant. Processing yields, staffing and prices are gameplay data rather than model geometry.

Source lives in `ArtSource/Vehicles/{DeicingTruck,DeicingTank,DeicingTanker,DeicingRecycler}/model.py`. Outputs are in
`Assets/AirCity/Vehicles/<Model>/{Models,Textures}` plus `<Model>.vehicle`.
`DeicingEquipmentSetup.CreateAssets` creates the four prefabs and fills
`Assets/AirCity/Vehicles/Resources/DeicingAssets.asset` for runtime loading. Existing roof-pod truck prefabs
migrate in place to the articulated tip-cabin hierarchy, preserving their GUID, root, components and custom
children. Later runs compare generated part names, mesh references and rest transforms, refreshing the model
hierarchy when its source changes and otherwise preserving the prefab. The setup only touches deicing assets.

Regenerate from `ArtSource/Vehicles`:

```sh
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model DeicingTruck --previews --check --bake-device CPU --views front34,working,cabin
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model DeicingTank --previews --check --bake-device CPU --views front34,pump
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model DeicingTanker --previews --check --bake-device CPU --views front34,hose
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python build.py -- --model DeicingRecycler --previews --check --bake-device CPU --views front34,filters
```

Blender previews are under each asset's ignored `Build/previews`. Geometry and textured previews were reviewed
2026-11-12. The normals checker reports isolated window/marking faces because open decal polygons have no
outward solid; their winding is authored toward the visible side. `DeicingEquipmentTests` checks imported
metre scale, wheel pivots, moving LODs, both arm lengths, cabin/nozzle interfaces, stationary storage and runtime
resource references, including migration of the old truck prefab, the six-wheel tanker/reel and compact recycler.

With Unity closed, `AirCity.EditorTools.Vehicles.DeicingEquipmentSetup.FromCommandLine` creates prefabs;
`CaptureFromCommandLine` also renders an unsaved art-review scene into `Captures/DeicingEquipment/`.
These commands preserve Sandbox and all saved airport layouts.

The build browser uses portraits rendered from those same prefabs. **AirCity > Vehicles > Refresh Deicing
Build Portraits**, or `DeicingBuildThumbnails.FromCommandLine` with Unity closed, refreshes the tank, truck
and recycler PNGs in `Assets/AirCity/UI/Resources/AirCityBuild`. Model placement previews instantiate the
prefabs themselves with operational behaviours disabled; they retain the original textures and never
register vehicles or consume stock.
