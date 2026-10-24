// Airfield ground surfaces: runway and taxiway pavement, shoulders, aprons, dirt strips and grass.
//  - Photo textures (ArtSource/Airfield) are mapped in world space, so strips and junctions meet without seams,
//    and sampled twice (rotated, rescaled) and blended in patches so the repeat doesn't show over kilometres.
//  - Wear blends in a cracked / rutted texture set in growing patches; touchdown zones collect tyre rubber.
//  - Concrete slab and asphalt lane joints are drawn from the pavement's own coordinates (uv0).
//  - Wetness comes from the Airport's global (_AirfieldWetness). Snow: grass shows the Airport's ground depth
//    (_AirfieldSnow), runways and taxiways their own, cleared where the ploughs have been (StripWorked).
//  - Grass strips (_GRASS_LENGTH): long grass is darker with seed heads, cut grass lighter, with mowing stripes
//    that turn with the view and fade as it grows back. Verges (the mown grass beside runways and taxiways) are
//    drawn the same way, under the field's snow.
//
// Mesh layout (built by SurfaceBuilder; state kept current by SurfaceState): uv0 = (metres across from the
// centreline, metres along), uv1 = (kind, runway length), uv4 = ploughs / mowers at work (see StripWorked),
// color.r = wear 0..1, color.g = snow (cm), color.b = grass length (cm), color.a = edge coverage (fringes).
Shader "AirCity/AirfieldSurface"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Albedo", 2D) = "grey" {}
        [NoScaleOffset][Normal] _NormalMap ("Normal", 2D) = "bump" {}
        [NoScaleOffset] _MaskMap ("Mask (R rough, G AO, B height)", 2D) = "grey" {}
        [NoScaleOffset] _WornMap ("Worn albedo", 2D) = "grey" {}
        [NoScaleOffset][Normal] _WornNormalMap ("Worn normal", 2D) = "bump" {}
        [NoScaleOffset] _WornMaskMap ("Worn mask", 2D) = "grey" {}
        [NoScaleOffset] _MacroMap ("Macro noise", 2D) = "grey" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _TileSize ("Texture tile (metres)", Float) = 2.5
        _NormalStrength ("Normal strength", Range(0, 2)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _MacroScale ("Macro tile (metres)", Float) = 240
        _MacroStrength ("Macro tint variation", Range(0, 0.5)) = 0.1
        _JointSpacing ("Joint spacing (across, along; 0 = none)", Vector) = (0, 0, 0, 0)
        _JointWidth ("Joint width (metres)", Float) = 0.03
        _JointDarkness ("Joint darkness", Range(0, 1)) = 0.4
        _SlabVariation ("Slab / lane tint variation", Range(0, 0.3)) = 0.05
        _Porosity ("Darkening when wet", Range(0, 1)) = 0.45
        _Rubber ("Tyre rubber", Range(0, 1)) = 0.8
        _RubberColor ("Tyre rubber colour", Color) = (0.03, 0.03, 0.03, 1)
        _Puddles ("Puddles when wet", Range(0, 1)) = 1
        [Toggle(_FRINGE)] _Fringe ("Ragged edges (dirt, grass strips)", Float) = 0
        [Toggle(_GRASS_LENGTH)] _GrassLength ("Grass length and mowing stripes", Float) = 0
        _StripeStrength ("Mowing stripe strength", Range(0, 0.4)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "AirfieldCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _TileSize, _NormalStrength, _Smoothness, _MacroScale, _MacroStrength;
            float4 _JointSpacing;
            float _JointWidth, _JointDarkness, _SlabVariation, _Porosity, _Rubber;
            half4 _RubberColor;
            float _Puddles, _StripeStrength;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap);
        TEXTURE2D(_MaskMap);
        TEXTURE2D(_WornMap);
        TEXTURE2D(_WornNormalMap);
        TEXTURE2D(_WornMaskMap);
        TEXTURE2D(_MacroMap); SAMPLER(sampler_MacroMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv0 : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
            float4 work : TEXCOORD4;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 color : COLOR;
            float2 local : TEXCOORD2;     // across, along
            float2 kindLength : TEXCOORD3;
            half fogFactor : TEXCOORD4;
            float4 work : TEXCOORD5;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.color = v.color;
            o.local = v.uv0;
            o.kindLength = v.uv1;
            o.work = v.work;
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        half4 Macro(float2 xz, float scale) { return SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, xz / scale); }

        void Fringe(Varyings i)
        {
        #if defined(_FRINGE)
            half grain = Macro(i.positionWS.xz, 9.0).a;
            clip(i.color.a - (grain * 0.9 + 0.05));
        #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _FRINGE
            #pragma shader_feature_local _GRASS_LENGTH
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            // tangent-space normal of a world-planar sample, rotated into the shared (world X, world Z) frame
            half3 SampleNormal(TEXTURE2D_PARAM(tex, smp), float2 uv, half2x2 toWorld)
            {
                half3 n = UnpackNormalScale(SAMPLE_TEXTURE2D(tex, smp, uv), _NormalStrength);
                n.xy = mul(toWorld, n.xy);
                return n;
            }

            // fraction of a pixel covered by a joint line of width w at distance d (screen derivative fw)
            half LineCoverage(float d, float w, float fw)
            {
                fw = max(fw, 1e-5);
                return saturate((0.5 * (w + fw) - d) / fw) * saturate(w / fw);
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                Fringe(i);
                float2 xz = i.positionWS.xz;
                int kind = (int)round(i.kindLength.x);
                float runwayLength = i.kindLength.y;
                bool worldMapped = kind == KIND_APRON || kind == KIND_JUNCTION || kind == KIND_GROUND;
                float2 local = worldMapped ? xz : i.local;

                half4 macro = Macro(xz, _MacroScale);
                half4 fine = Macro(xz + 17.0, _MacroScale * 0.13);

                // two differently rotated / scaled samples, shown in patches, hide the texture repeat
                const half2x2 rotB = half2x2(0.7986, 0.6018, -0.6018, 0.7986);
                float2 uvA = xz / _TileSize;
                float2 uvB = float2(dot(xz, float2(0.7986, -0.6018)), dot(xz, float2(0.6018, 0.7986))) / (_TileSize * 2.31) + 0.37;
                half pick = smoothstep(0.4, 0.6, fine.g);
                half3 albedo = lerp(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvA).rgb,
                                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvB).rgb, pick);
                half4 mask = lerp(SAMPLE_TEXTURE2D(_MaskMap, sampler_BaseMap, uvA),
                                  SAMPLE_TEXTURE2D(_MaskMap, sampler_BaseMap, uvB), pick);
                half3 n = lerp(SampleNormal(TEXTURE2D_ARGS(_NormalMap, sampler_BaseMap), uvA, half2x2(1, 0, 0, 1)),
                               SampleNormal(TEXTURE2D_ARGS(_NormalMap, sampler_BaseMap), uvB, rotB), pick);

                // how much traffic this spot sees: touchdown zones, the centreline band, shoulders less so
                half band = 1.0 - smoothstep(2.0, 8.0, abs(local.x));
                half traffic = kind == KIND_RUNWAY ? max(TouchdownRubber(local.y, local.x, runwayLength) * 1.4, band * 0.8)
                             : kind == KIND_TAXIWAY ? band
                             : kind == KIND_SHOULDER ? 0.6 : 0.5;
                half wear = saturate(i.color.r * (0.55 + 0.7 * traffic));
                half wearNoise = macro.r * 0.7 + fine.b * 0.3;
                half worn = smoothstep(1.0 - wear - 0.12, 1.0 - wear + 0.12, wearNoise) * step(0.001, i.color.r);
                if (worn > 0.001)
                {
                    float2 uvW = uvA * 0.83 + 0.21;
                    albedo = lerp(albedo, SAMPLE_TEXTURE2D(_WornMap, sampler_BaseMap, uvW).rgb, worn);
                    mask = lerp(mask, SAMPLE_TEXTURE2D(_WornMaskMap, sampler_BaseMap, uvW), worn);
                    n = lerp(n, SampleNormal(TEXTURE2D_ARGS(_WornNormalMap, sampler_BaseMap), uvW, half2x2(1, 0, 0, 1)), worn);
                }

                // slab / paving-lane joints and per-slab tint
                float2 spacing = kind == KIND_JUNCTION ? 0 : _JointSpacing.xy;
                half joint = 0;
                float2 slab = 0;
                if (spacing.x > 0)
                {
                    float d = abs(frac(local.x / spacing.x + 0.5) - 0.5) * spacing.x;
                    joint = max(joint, LineCoverage(d, _JointWidth, fwidth(local.x)));
                    slab.x = floor(local.x / spacing.x + 0.5);
                }
                if (spacing.y > 0)
                {
                    float d = abs(frac(local.y / spacing.y + 0.5) - 0.5) * spacing.y;
                    joint = max(joint, LineCoverage(d, _JointWidth, fwidth(local.y)));
                    slab.y = floor(local.y / spacing.y + 0.5);
                }
                albedo *= 1.0 + (Hash(slab + kind * 7.0) - 0.5) * 2.0 * _SlabVariation;
                albedo *= 1.0 - joint * _JointDarkness;

                albedo *= _Tint.rgb * (1.0 + (macro.r - 0.5) * 2.0 * _MacroStrength);
                if (kind == KIND_SHOULDER)
                    albedo *= 1.06;
                if (kind == KIND_GROUND || kind == KIND_VERGE)    // dry, yellower patches in the grass
                    albedo = lerp(albedo, albedo * half3(1.2, 1.08, 0.7), smoothstep(0.6, 0.95, macro.g) * 0.35);

                half stripeDir;
                bool worked = StripWorked(i.local.y, i.local.x, i.work, stripeDir);
            #if defined(_GRASS_LENGTH)
                // grass length: the strip's (or verge's) own where the mowers haven't been; the field around is
                // about 15 cm
                bool mowing = i.work.w < 0.0;
                float height = kind == KIND_GROUND ? 15.0 : (worked && mowing ? GRASS_CUT_CM : i.color.b);
                half shortness = saturate((15.0 - height) / 10.0), longness = saturate((height - 15.0) / 15.0);
                albedo *= lerp(lerp(1.0, half3(1.1, 1.12, 0.94), shortness), half3(0.82, 0.88, 0.8), longness);
                half seeds = smoothstep(0.62, 0.9, Macro(xz * 7.3, 1.0).a * 0.6 + fine.r * 0.4) * longness;
                albedo = lerp(albedo, half3(0.62, 0.58, 0.42), seeds * 0.45);
                if (mowing)
                {
                    // blades laid over by the mower: light seen from behind them, dark from in front; the
                    // strip's along axis in world space comes from the screen derivatives of its coordinates
                    float2 dx = ddx(xz), dy = ddy(xz);
                    float2 gradS = float2(ddx(i.local.y), ddy(i.local.y));
                    float det = dx.x * dy.y - dx.y * dy.x;
                    float2 along = abs(det) > 1e-8 ? normalize(float2(dy.y * gradS.x - dx.y * gradS.y,
                                                                      -dy.x * gradS.x + dx.x * gradS.y) / det) : float2(0, 1);
                    float2 toCam = normalize(GetCameraPositionWS().xz - xz + 1e-4);
                    // mown away from the viewer: the blades lie away and show their light sides
                    half facing = -dot(along, toCam) * stripeDir;
                    half fresh = 1.0 - saturate((height - GRASS_CUT_CM) / 9.0);
                    albedo *= 1.0 + facing * _StripeStrength * fresh;
                }
            #endif

                // tyre rubber: touchdown zones on runways, the centreline band on taxiways
                half rubber = 0;
                if (kind == KIND_RUNWAY || kind == KIND_TAXIWAY)
                {
                    half streak = Macro(float2(local.x / 1.9, local.y / 95.0), 1.0).b;
                    half zone = kind == KIND_RUNWAY ? TouchdownRubber(local.y, local.x, runwayLength) : band * 0.35;
                    rubber = zone * saturate(streak * 1.5 - 0.15) * (0.5 + 0.5 * i.color.r) * _Rubber;
                    albedo = lerp(albedo, _RubberColor.rgb, rubber * 0.8);
                }

                half smoothness = (1.0 - mask.r) * _Smoothness + rubber * 0.12;
                half occlusion = lerp(1.0, mask.g, 0.8);

                // weather: wet darkens and adds puddles in the low spots
                half wet = _AirfieldWetness;
                half puddle = smoothstep(0.66, 0.82, (1.0 - mask.b) * 0.45 + (1.0 - macro.g) * 0.55) * wet * _Puddles;
                albedo *= lerp(1.0, 1.0 - _Porosity, wet);
                smoothness = lerp(smoothness, 0.62, wet * 0.65);
                smoothness = lerp(smoothness, 0.93, puddle);
                n = lerp(n, half3(0, 0, 1), puddle);

                // snow: the ground depth on grass (aprons are kept nearly clear), each runway's / taxiway's own
                // depth on pavement, none behind the ploughs; while the ground around is snowy, pavement keeps
                // white remnants in its joints and low spots
                float depth = kind == KIND_GROUND || kind == KIND_VERGE ? _AirfieldSnow : kind == KIND_APRON ? _AirfieldSnow * 0.15
                            : (worked && i.work.w > 0.0 ? 0.0 : i.color.g);
                // fills the texture's hollows first; the grain keeps the texture's repeat from showing through
                half snowNoise = mask.b * 0.35 + fine.g * 0.4 + Macro(xz, 6.3).a * 0.25;
                half snow = SnowCoverage(snowNoise, depth);
                if (kind != KIND_GROUND && kind != KIND_VERGE)
                    snow = max(snow, saturate(saturate((0.25 - mask.b) * 5.0) * 0.45 + joint * 0.8) * saturate(_AirfieldSnow * 0.5));
                albedo = lerp(albedo, SNOW_ALBEDO * (0.88 + 0.2 * mask.b + 0.1 * macro.g), snow);
                smoothness = lerp(smoothness, 0.35, snow);
                occlusion = lerp(occlusion, lerp(1.0, occlusion, 0.5), snow);
                n = lerp(n, normalize(half3(n.xy * 0.4, 1.0)), snow);

                half3 N = normalize(i.normalWS);
                half3 T = normalize(half3(1, 0, 0) - N * N.x);
                half3 B = cross(T, N);
                half3 normalWS = normalize(T * n.x + B * n.y + N * n.z);
                return AirfieldShade(i.positionWS, i.positionCS, normalWS, saturate(albedo), saturate(smoothness),
                                     occlusion, 1.0, i.fogFactor);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma shader_feature_local _FRINGE

            half fragDepth(Varyings i) : SV_Target
            {
                Fringe(i);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragNormals
            #pragma shader_feature_local _FRINGE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 fragNormals(Varyings i) : SV_Target
            {
                Fringe(i);
                return AirfieldPackNormal(normalize(i.normalWS));
            }
            ENDHLSL
        }
    }
}
