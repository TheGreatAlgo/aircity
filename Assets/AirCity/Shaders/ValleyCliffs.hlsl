// World-space cliff surfacing. Included by the generated URP terrain input.
#ifndef AIRCITY_VALLEY_CLIFFS_INCLUDED
#define AIRCITY_VALLEY_CLIFFS_INCLUDED

TEXTURE2D(_CliffAlbedo); SAMPLER(sampler_CliffAlbedo);
TEXTURE2D(_CliffNormal); SAMPLER(sampler_CliffNormal);
TEXTURE2D(_CliffMask); SAMPLER(sampler_CliffMask);

float ValleyCliffNoise(float3 samplePosition)
{
    float3 cell = floor(samplePosition), blend = frac(samplePosition);
    blend = blend * blend * (3.0 - 2.0 * blend);
    float4 base = float4(0, 1, 113, 114) + dot(cell, float3(1, 113, 157));
    float4 low = frac(sin(base) * 43758.5453);
    float4 high = frac(sin(base + 157.0) * 43758.5453);
    float4 layers = lerp(low, high, blend.z);
    return lerp(lerp(layers.x, layers.y, blend.x), lerp(layers.z, layers.w, blend.x), blend.y);
}

// A slow warp bends individual fractures; the stochastic patch sampler below removes tile repetition.
float2 ValleyCliffUV(float2 uv)
{
    return uv + float2(sin(uv.y * .21) + sin(uv.x * .17), sin(uv.x * .19 + uv.y * .11)) * .22;
}

void ValleyCliffPatches(float2 uv, out float2 cellA, out float2 cellB, out float2 cellC, out half3 patchWeights)
{
    float2 lattice = mul(float2x2(1, 0, -.57735027, 1.15470054), uv * .43);
    float2 cell = floor(lattice), part = frac(lattice);
    if (part.x + part.y > 1.0)
    {
        cellA = cell + 1; cellB = cell + float2(1, 0); cellC = cell + float2(0, 1);
        patchWeights = half3(part.x + part.y - 1, 1 - part.y, 1 - part.x);
    }
    else
    {
        cellA = cell; cellB = cell + float2(1, 0); cellC = cell + float2(0, 1);
        patchWeights = half3(1 - part.x - part.y, part.x, part.y);
    }
    // Most pixels use one crisp scan patch, with short seamless joins between neighbouring orientations.
    patchWeights = patchWeights * patchWeights * patchWeights;
    patchWeights /= max(dot(patchWeights, half3(1, 1, 1)), .0001h);
}

float ValleyCliffQuarter(float2 cell)
{
    return floor(frac(sin(dot(cell, float2(41.13, 289.73))) * 43758.5453) * 4.0);
}

float2 ValleyCliffRotate(float2 uv, float quarter)
{
    float2 axes = (quarter < .5 || (quarter > 1.5 && quarter < 2.5)) ? uv : uv.yx;
    return axes * float2(quarter > .5 && quarter < 2.5 ? -1 : 1, quarter > 1.5 ? -1 : 1);
}

half2 ValleyCliffUnrotate(half2 normalXY, float quarter)
{
    half2 axes = (quarter < .5 || (quarter > 1.5 && quarter < 2.5)) ? normalXY : normalXY.yx;
    return axes * half2(quarter > 1.5 ? -1 : 1, quarter > .5 && quarter < 2.5 ? -1 : 1);
}

half4 ValleyCliffPatch(TEXTURE2D_PARAM(map, mapSampler), float2 uv, float2 gradientX, float2 gradientY, float2 cell)
{
    float quarter = ValleyCliffQuarter(cell);
    return SAMPLE_TEXTURE2D_GRAD(map, mapSampler, ValleyCliffRotate(uv, quarter) + ValleyTileOffset(cell),
        ValleyCliffRotate(gradientX, quarter), ValleyCliffRotate(gradientY, quarter));
}

half4 ValleyCliffSample(TEXTURE2D_PARAM(map, mapSampler), float2 uv)
{
    float2 warped = ValleyCliffUV(uv), gradientX = ddx(warped), gradientY = ddy(warped);
    float2 cellA, cellB, cellC; half3 patchWeights;
    ValleyCliffPatches(warped, cellA, cellB, cellC, patchWeights);
    return ValleyCliffPatch(TEXTURE2D_ARGS(map, mapSampler), warped, gradientX, gradientY, cellA) * patchWeights.x
        + ValleyCliffPatch(TEXTURE2D_ARGS(map, mapSampler), warped, gradientX, gradientY, cellB) * patchWeights.y
        + ValleyCliffPatch(TEXTURE2D_ARGS(map, mapSampler), warped, gradientX, gradientY, cellC) * patchWeights.z;
}

half3 ValleyCliffNormalPatch(float2 uv, float2 gradientX, float2 gradientY, float2 cell)
{
    half3 decoded = UnpackNormalScale(ValleyCliffPatch(TEXTURE2D_ARGS(_CliffNormal, sampler_CliffNormal),
        uv, gradientX, gradientY, cell), _CliffNormalStrength);
    decoded.xy = ValleyCliffUnrotate(decoded.xy, ValleyCliffQuarter(cell));
    return decoded;
}

half3 ValleyCliffNormalSample(float2 uv)
{
    float2 warped = ValleyCliffUV(uv), gradientX = ddx(warped), gradientY = ddy(warped);
    float2 cellA, cellB, cellC; half3 patchWeights;
    ValleyCliffPatches(warped, cellA, cellB, cellC, patchWeights);
    return normalize(ValleyCliffNormalPatch(warped, gradientX, gradientY, cellA) * patchWeights.x
        + ValleyCliffNormalPatch(warped, gradientX, gradientY, cellB) * patchWeights.y
        + ValleyCliffNormalPatch(warped, gradientX, gradientY, cellC) * patchWeights.z);
}

half3 ValleyTerrainGeometricNormal(float2 terrainUV, half3 interpolatedNormal)
{
#if defined(ENABLE_TERRAIN_PERPIXEL_NORMAL)
    float2 sampleCoords = (terrainUV / _TerrainHeightmapRecipSize.zw + .5) * _TerrainHeightmapRecipSize.xy;
    return normalize(TransformObjectToWorldNormal(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture,
        sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2.0 - 1.0));
#else
    return normalize(interpolatedNormal);
#endif
}

half ValleyCliffAmount(half3 geometricNormal)
{
    // Feather natural scree into exposed rock; horizontal airport earthwork stays on its painted layer.
    return _CliffEnabled * smoothstep(.22h, .55h, 1.0h - abs(geometricNormal.y));
}

void ValleyCliffCoordinates(float3 positionWS, half3 geometricNormal, out float2 uvX, out float2 uvY,
    out float2 uvZ, out half3 weights, out half2 sides)
{
    float3 p = positionWS / max(_CliffTileSize, .1);
    sides = half2(geometricNormal.x < 0 ? -1 : 1, geometricNormal.z < 0 ? -1 : 1);
    uvX = float2(p.z * sides.x, p.y);
    uvY = p.xz;
    uvZ = float2(p.x * sides.y, p.y);
    weights = pow(abs(geometricNormal), 4.0h);
    weights /= max(dot(weights, half3(1, 1, 1)), .0001h);
}

half3 ValleyCliffNormal(float2 uvX, float2 uvY, float2 uvZ, half3 weights, half2 sides, half3 geometricNormal)
{
    half3 nx = ValleyCliffNormalSample(uvX);
    half3 ny = ValleyCliffNormalSample(uvY);
    half3 nz = ValleyCliffNormalSample(uvZ);
    // Project the perturbation into each plane, then onto the terrain tangent plane. A flat normal map
    // exactly preserves the geometric normal instead of pushing blended slopes towards an axis.
    half3 detail = half3(0, nx.y, nx.x * sides.x) * weights.x
        + half3(ny.x, 0, ny.y) * weights.y + half3(nz.x * sides.y, nz.y, 0) * weights.z;
    detail -= geometricNormal * dot(detail, geometricNormal);
    return normalize(geometricNormal + detail);
}

void ValleyCliffSurface(float3 positionWS, half3 geometricNormal, inout half3 albedo,
    inout half smoothness, inout half occlusion, inout half3 normalWS)
{
    half amount = ValleyCliffAmount(geometricNormal);
    UNITY_BRANCH if (amount > .001h)
    {
        float2 uvX, uvY, uvZ; half3 weights; half2 sides;
        ValleyCliffCoordinates(positionWS, geometricNormal, uvX, uvY, uvZ, weights, sides);
        half3 rock = ValleyCliffSample(TEXTURE2D_ARGS(_CliffAlbedo, sampler_CliffAlbedo), uvX).rgb * weights.x
            + ValleyCliffSample(TEXTURE2D_ARGS(_CliffAlbedo, sampler_CliffAlbedo), uvY).rgb * weights.y
            + ValleyCliffSample(TEXTURE2D_ARGS(_CliffAlbedo, sampler_CliffAlbedo), uvZ).rgb * weights.z;
        half4 mask = ValleyCliffSample(TEXTURE2D_ARGS(_CliffMask, sampler_CliffMask), uvX) * weights.x
            + ValleyCliffSample(TEXTURE2D_ARGS(_CliffMask, sampler_CliffMask), uvY) * weights.y
            + ValleyCliffSample(TEXTURE2D_ARGS(_CliffMask, sampler_CliffMask), uvZ) * weights.z;
        // Large mineral/weathering patches remain visible at airport scale without baking light into the scan.
        half broad = ValleyCliffNoise(positionWS * .012);
        half weather = ValleyCliffNoise(positionWS * .045 + float3(17, 3, 41));
        rock *= _CliffTint.rgb * lerp(.6h, 1.35h, broad) * lerp(.82h, 1.15h, weather);
        albedo = lerp(albedo, rock, amount);
        smoothness = lerp(smoothness, min(mask.a, .14h), amount);
        occlusion = lerp(occlusion, lerp(.65h, 1.0h, mask.g), amount);
        normalWS = normalize(lerp(normalWS, ValleyCliffNormal(uvX, uvY, uvZ, weights, sides, geometricNormal), amount));
    }
}

void ValleyCliffDepthNormal(float3 positionWS, half3 geometricNormal, inout half3 normalWS)
{
    half amount = ValleyCliffAmount(geometricNormal);
    UNITY_BRANCH if (amount > .001h)
    {
        float2 uvX, uvY, uvZ; half3 weights; half2 sides;
        ValleyCliffCoordinates(positionWS, geometricNormal, uvX, uvY, uvZ, weights, sides);
        normalWS = normalize(lerp(normalWS, ValleyCliffNormal(uvX, uvY, uvZ, weights, sides, geometricNormal), amount));
    }
}
#endif
