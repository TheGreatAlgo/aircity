// AirCity's terrain sampling extension. Layer lighting and mixing remain in generated URP terrain passes.
#ifndef AIRCITY_VALLEY_TERRAIN_SAMPLING_INCLUDED
#define AIRCITY_VALLEY_TERRAIN_SAMPLING_INCLUDED

float2 ValleyTileOffset(float2 cell)
{
    return frac(sin(float2(dot(cell, float2(127.1, 311.7)), dot(cell, float2(269.5, 183.3)))) * 43758.5453);
}

// Three neighbouring offset samples blend over a triangular lattice. Offsets have no derivatives:
// explicit gradients retain the original texture's mip selection at every lattice boundary.
// The same lookup for albedo, normals and masks keeps each material's surface detail aligned.
// Most of the cost of the terrain is these lookups, so nothing is read that cannot be seen: a layer with no
// weight at this pixel is skipped whole (pass its splat weight as layerWeight), and so is a lattice sample
// carrying under 1%. The gradients are taken before the branches: a derivative needs all four pixels of its quad.
half4 ValleySampleWeighted(TEXTURE2D_PARAM(map, mapSampler), float2 uv, half layerWeight)
{
    float2 gradientX = ddx(uv), gradientY = ddy(uv);
    half4 result = 0.0h;
    UNITY_BRANCH if (layerWeight > .002h)
    {
        float2 lattice = mul(float2x2(1.0, 0.0, -0.57735027, 1.15470054), uv * 1.25);
        float2 cell = floor(lattice), f = frac(lattice);
        float2 a, b, c;
        float3 weight;
        if (f.x + f.y > 1.0)
        {
            a = cell + 1.0; b = cell + float2(1, 0); c = cell + float2(0, 1);
            weight = float3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x);
        }
        else
        {
            a = cell; b = cell + float2(1, 0); c = cell + float2(0, 1);
            weight = float3(1.0 - f.x - f.y, f.x, f.y);
        }
        // A narrow soft transition keeps scanned grit and leaves crisp instead of averaging them away.
        weight = weight * weight * weight;
        weight /= max(dot(weight, float3(1, 1, 1)), .0001);
        weight *= step(.01, weight);
        weight /= max(dot(weight, float3(1, 1, 1)), .0001);
        UNITY_BRANCH if (weight.x > 0.0) result += SAMPLE_TEXTURE2D_GRAD(map, mapSampler, uv + ValleyTileOffset(a), gradientX, gradientY) * weight.x;
        UNITY_BRANCH if (weight.y > 0.0) result += SAMPLE_TEXTURE2D_GRAD(map, mapSampler, uv + ValleyTileOffset(b), gradientX, gradientY) * weight.y;
        UNITY_BRANCH if (weight.z > 0.0) result += SAMPLE_TEXTURE2D_GRAD(map, mapSampler, uv + ValleyTileOffset(c), gradientX, gradientY) * weight.z;
    }
    return result;
}

half4 ValleySample(TEXTURE2D_PARAM(map, mapSampler), float2 uv)
{
    return ValleySampleWeighted(TEXTURE2D_ARGS(map, mapSampler), uv, 1.0h);
}
#endif
