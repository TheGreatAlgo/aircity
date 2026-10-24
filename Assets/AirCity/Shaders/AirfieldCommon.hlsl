// Shared by the airfield shaders (surface, paint, fixtures): URP lighting for a flat-ish ground surface, the
// weather globals the Airfield sets, and the runway wear patterns that surface and paint must agree on.
#ifndef AIRCITY_AIRFIELD_COMMON_INCLUDED
#define AIRCITY_AIRFIELD_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// set by Airport.ApplyGlobals: how wet surfaces look (0..1), snow on unploughed ground (cm)
float _AirfieldWetness;
float _AirfieldSnow;

// pavement kinds (uv1.x of surface meshes, AirfieldKind in C#)
#define KIND_APRON 0
#define KIND_RUNWAY 1
#define KIND_TAXIWAY 2
#define KIND_SHOULDER 3
#define KIND_GROUND 4
#define KIND_JUNCTION 5
#define KIND_VERGE 6

half4 AirfieldShade(float3 positionWS, float4 positionCS, half3 normalWS, half3 albedo, half smoothness,
                    half occlusion, half alpha, half fogFactor)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.positionCS = positionCS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
    inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), fogFactor);
    inputData.bakedGI = SampleSH(normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    surface.smoothness = smoothness;
    surface.occlusion = occlusion;
    surface.alpha = alpha;
    surface.normalTS = half3(0, 0, 1);

    half4 color = UniversalFragmentPBR(inputData, surface);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = alpha;
    return color;
}

// Rubber laid down by landing tyres: the touchdown zones at both ends (s = metres from the start threshold,
// length = runway length), strongest along the main-gear tracks either side of the centreline (x).
half TouchdownRubber(float s, float x, float length)
{
    if (length < 1.0)
        return 0;
    float a = smoothstep(60.0, 260.0, s) * (1.0 - smoothstep(550.0, 1150.0, s));
    float b = smoothstep(60.0, 260.0, length - s) * (1.0 - smoothstep(550.0, 1150.0, length - s));
    float band = 1.0 - smoothstep(3.0, 9.0, abs(x));
    return saturate(max(a, b)) * band;
}

// Snow cover 0..1 for a depth (cm) at a 0..1 noise value: 1 cm covers about a third, in the low spots first;
// 3 cm covers everything. Surface and paint share it, so markings vanish exactly where the snow lies.
#define SNOW_ALBEDO half3(0.5, 0.52, 0.56)
half SnowCoverage(half noise, float depthCm)
{
    half k = saturate(depthCm / 3.0) * 1.15;
    return smoothstep(noise - 0.12, noise + 0.12, k);
}

// Ploughs and mowers work a runway or taxiway in passes (StripWork in C#), in bands either side of the centreline
// from the middle out, each band as wide as the team: one lane per vehicle, in echelon (vehicle i of a pass is i
// lanes to the left of the lead and i * gap behind it). Ploughs always cast the snow outwards, so every pass on the
// left runs up the strip and every pass on the right back down; mowers swap the side order every pair of passes, so
// neighbouring bands alternate direction (the stripes). work = (pass in progress, or -1 when idle; the lead's
// position along the strip (m); band width (negative: the passes start from the far end, mirroring the pattern);
// lane width (negative: mowing), plus 100 when the team covers the whole width in one pass centred on the
// centreline (StripWork.Centred)). An idle mown strip keeps its band / lane widths for the stripes.
#define ECHELON_GAP_PER_LANE 3.2     // keep in sync with StripWork.EchelonGapPerLane
#define GRASS_CUT_CM 5.0             // keep in sync with RunwayRules.CutGrass

// Has the team been over this spot (s along, x across)? dir = the direction its band is worked in (+1 / -1).
bool StripWorked(float s, float x, float4 work, out half dir)
{
    dir = 1.0;
    float bandW = abs(work.z);
    float laneAbs = abs(work.w);
    bool centred = laneAbs > 50.0;      // StripWork.CentredFlag
    float laneW = max(centred ? laneAbs - 100.0 : laneAbs, 0.1);
    if (bandW < 0.01)
        return false;
    float sgn = work.z < 0.0 ? -1.0 : 1.0;
    x *= sgn;
    if (centred)
        x -= bandW * 0.5;               // the centred band, seen as the first band on the left
    float side = x < 0.0 ? 0.0 : 1.0;
    float band = floor(abs(x) / bandW);
    float k, d;          // the pass working this band ("pass" is reserved) and its direction in the pattern
    if (work.w < 0.0)
    {
        k = 2.0 * band + (fmod(band, 2.0) < 0.5 ? side : 1.0 - side);
        d = fmod(k, 2.0) < 0.5 ? 1.0 : -1.0;
    }
    else
    {
        k = 2.0 * band + side;
        d = side < 0.5 ? 1.0 : -1.0;
    }
    dir = d * sgn;
    if (work.x < -0.5)
        return false;
    if (k < work.x - 0.5)
        return true;
    if (k > work.x + 0.5)
        return false;
    float bandMin = side < 0.5 ? -(band + 1.0) * bandW : band * bandW;
    float lane = d > 0.0 ? floor((bandMin + bandW - x) / laneW) : floor((x - bandMin) / laneW);
    float front = work.y - dir * lane * laneW * ECHELON_GAP_PER_LANE;
    return dir * (front - s) > 0.0;
}

// Packed world-space normal for the DepthNormals pass (SSAO).
half4 AirfieldPackNormal(half3 normalWS)
{
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
    float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
    return half4(PackFloat2To888(remapped), 0.0);
#else
    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
#endif
}

#endif
