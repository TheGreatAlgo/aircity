# Ramp crew

Gate arrivals also use this worker as a marshaller. `AirportMarshalling` owns its game-time walk and signals;
`MarshallerPresentation` attaches two shared orange illuminated wands and poses the existing skeleton. The
character generator additionally writes `MarshallingWand.fbx` and its albedo/emission maps. `RampCrewSetup`
creates the editable wand prefab/material once. See [traffic](../systems/traffic.md) for arrival integration.

Shared generated baggage worker, 1.78 m tall, in navy work clothing, fluorescent vest with reflective strips,
coated work gloves, reinforced trouser knees, boots, cap and ear defenders. Four shared atlases vary skin tone while retaining the same
unbranded uniform. The [IATA ground-handling photographs](https://www.iata.org/en/youandiata/ground-handlers/)
provide a reference for clothing and equipment scale.

The shirt and trousers are fused and relaxed before skinning, giving continuous shoulders, hips and knees.
The vest has shoulder panels, armholes, a V neck, curved reflective tape and flush pockets. A continuous
sculpted face replaces the separate head/jaw/nose shapes; gloves, boots and hearing protection use restrained
proportions. Idle hands hang near the thighs while the original palm targets still drive baggage and controls.

`ArtSource/Characters/RampCrew/build.py` writes the skinned FBX, three mesh LODs, 512 px fabric/skin atlases and
`RampCrew.crew` with body dimensions and handling timings. The skeleton has named torso, arm and leg chains.
There is one material/draw per LOD (approximately 29,220 / 5,000 / 899 triangles). The distant LOD removes
individual fingers, eye inserts, stitches and small headset parts before reduction, keeping Unity's imported
mesh below the existing 1,000-vertex budget. A shared normal map and
material-specific smoothness distinguish fabric, skin, rubber and reflective tape. Padded atlas tiles,
trilinear filtering and mipmaps keep the weave stable at gameplay distances. Textures use a fixed random
seed and require no external downloads. Runtime poses are procedural,
including seated driving, standing controls, walking, reach/lift/carry/place and kneeling hold work.

```sh
rtk proxy /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python ArtSource/Characters/RampCrew/build.py
rtk proxy python3 ArtSource/Vehicles/crew_profiles.py
rtk proxy timeout 300 /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -executeMethod AirCity.EditorTools.Characters.RampCrewSetup.Install -logFile Logs/ramp-crew-import.log
```

`RampCrewSetup.CreateAssets` creates the editable prefab once and refreshes generated material textures,
normal maps and smoothness on subsequent runs. The installer assigns crew to existing stations in Sandbox,
BaggageTurnaround and ContainerTurnaround; scene generators and stations added from the
sandbox copy the same references. Vehicle `CREW` declarations author seat/platform, hand and foot positions
in Blender metres. The normal vehicle pipeline exports them; `crew_profiles.py` refreshes just those metadata
fields when vehicle geometry has not changed. The occupied height participates in aircraft clearance.
The belt loader also exports its access treads and walkway offset, keeping the boarding path aligned with
the generated rear stairs and catwalk as the conveyor rises.

Do not start batch Unity while the project is open in the editor. Review the actual simulation and produce
close-ups under `Captures/RampCrew/` with:

```sh
rtk proxy timeout 600 /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Characters.RampCrewReview.FromCommandLine -logFile Logs/ramp-crew-review.log
```

`RampCrewTests` checks the skinned LODs, weights, floor clearance, palm targets, boarding/vehicle interlocks,
one held bag per worker, blocked access, cancellation and retry. Existing turnaround tests cover conservation,
screening, arrivals capacity and combined container/bulk departure readiness. Hall interiors, workforce
management and detailed hold packing are separate work. See [the baggage system](../systems/baggage.md).

Depot staffing adds optional second cart and hold handlers. Bag indices alternate between workers; each
worker receives its own pose and carry socket, with one held bag at a time. Available staff stand at the
depot, and unstaffed parked vehicles hide their driver actors. Driver boarding interlocks still control
vehicle movement. Crew changes apply between batches and leave walking, lifting and conveyor travel
durations intact. See [depot staffing](../systems/baggage.md#baggage-depots-and-hired-crews).
