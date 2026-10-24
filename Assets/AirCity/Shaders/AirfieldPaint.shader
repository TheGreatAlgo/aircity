// Runway and taxiway paint: stripes, lines, hold markings and designation numbers, alpha-blended over the
// pavement. Every mark is drawn from its own distance field, so edges stay crisp up close, and the quads grow
// by a pixel on screen so thin lines fade smoothly instead of breaking up when zoomed out (no MSAA needed).
// Paint gets the same touchdown rubber as the pavement, fades with wear and disappears under snow (except
// where the ploughs have been).
//
// Mesh layout (built by PaintBuilder; state kept current by SurfaceState): normal / tangent = which way the
// corner grows (across / along, world, 0 = doesn't), uv0 = (across, along metres from the mark's centre,
// snow cm, unused), uv1 = (half width, half length, along-centre, kind 0 = box / 1 = glyph), uv2 = glyph
// atlas (u0, v0, u per metre, v per metre), uv3 = (strip along, strip across, runway length, wear), uv4 = the
// ploughs at work (AirfieldCommon.hlsl StripWorked), color = paint colour and opacity.
Shader "AirCity/AirfieldPaint"
{
    Properties
    {
        [NoScaleOffset] _GlyphMap ("Runway glyph distance field", 2D) = "black" {}
        [NoScaleOffset] _MacroMap ("Macro noise", 2D) = "grey" {}
        _GlyphSpread ("Glyph distance spread (metres)", Float) = 0.66
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _Grain ("Grain", Range(0, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-150" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Paint"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "AirfieldCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _GlyphSpread, _Smoothness, _Grain;
            CBUFFER_END
            TEXTURE2D(_GlyphMap); SAMPLER(sampler_GlyphMap);
            TEXTURE2D(_MacroMap); SAMPLER(sampler_MacroMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                half4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                float4 work : TEXCOORD4;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 local : TEXCOORD1;
                float4 box : TEXCOORD2;
                float4 atlas : TEXCOORD3;
                float4 runway : TEXCOORD4;
                half4 color : COLOR;
                half fogFactor : TEXCOORD5;
                float snow : TEXCOORD6;
                float4 work : TEXCOORD7;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 growU = TransformObjectToWorldDir(v.normalOS, false);
                float3 growV = TransformObjectToWorldDir(v.tangentOS.xyz, false);
                // one pixel at this distance, in metres
                float4 cs = TransformWorldToHClip(posWS);
                // abs: the projection is y-flipped when rendering into a texture on some platforms (Metal)
                float pixel = 2.0 * cs.w / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                float2 local = v.uv0.xy;
                float hasU = dot(growU, growU) > 1e-6 ? 1.0 : 0.0;
                float hasV = dot(growV, growV) > 1e-6 ? 1.0 : 0.0;
                posWS += (growU + growV) * pixel;
                local.x += sign(local.x) * pixel * hasU;
                local.y += sign(local.y - v.uv1.z) * pixel * hasV;
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.local = local;
                o.box = v.uv1;
                o.atlas = v.uv2;
                o.runway = v.uv3;
                o.color = v.color;
                o.snow = v.uv0.z;
                o.work = v.work;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // share of a pixel (width fw) at x covered by the interval [-h, h]
            half Coverage(float x, float h, float fw)
            {
                fw = max(fw, 1e-5);
                return saturate((min(x + 0.5 * fw, h) - max(x - 0.5 * fw, -h)) / fw);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half alpha;
                if (i.box.w > 0.5)
                {
                    float2 uv = i.atlas.xy + i.local * i.atlas.zw;
                    float sdf = SAMPLE_TEXTURE2D(_GlyphMap, sampler_GlyphMap, uv).r;
                    float d = (0.5 - sdf) * 2.0 * _GlyphSpread;
                    alpha = saturate(0.5 - d / max(fwidth(d), 1e-4));
                }
                else
                {
                    alpha = Coverage(i.local.x, i.box.x, fwidth(i.local.x)) *
                            Coverage(i.local.y - i.box.z, i.box.y, fwidth(i.local.y));
                }
                if (alpha < 0.002)
                    discard;

                float2 xz = i.positionWS.xz;
                half4 macro = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, xz / 31.0);
                half grain = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, xz / 1.7).a;
                half3 albedo = i.color.rgb * (1.0 - _Grain + grain * _Grain * 1.3);

                // wear rubs the paint off in patches; touchdown rubber covers it
                half wear = i.runway.w;
                alpha *= i.color.a * (1.0 - smoothstep(1.0 - wear * 0.8 - 0.1, 1.0 - wear * 0.8 + 0.1, macro.r * 0.6 + grain * 0.4) * 0.85);
                half rubber = TouchdownRubber(i.runway.x, i.runway.y, i.runway.z);
                half streak = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, float2(i.runway.y / 1.9, i.runway.x / 95.0)).b;
                rubber *= saturate(streak * 1.5 - 0.15) * (0.5 + 0.5 * wear);
                albedo = lerp(albedo, half3(0.03, 0.03, 0.03), rubber * 0.8);

                half smoothness = lerp(_Smoothness, 0.65, _AirfieldWetness * 0.7);
                albedo *= lerp(1.0, 0.85, _AirfieldWetness);
                // snow hides paint where it hides the pavement (the surface's noise, its height map taken as
                // average), except where the ploughs have been
                half dir;
                float depth = StripWorked(i.runway.x, i.runway.y, i.work, dir) && i.work.w > 0.0 ? 0.0 : i.snow;
                half snowNoise = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, (xz + 17.0) / 31.2).g * 0.4 +
                                 SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, xz / 6.3).a * 0.25 + 0.175;
                alpha *= 1.0 - SnowCoverage(snowNoise, depth);

                half4 color = AirfieldShade(i.positionWS, i.positionCS, half3(0, 1, 0), saturate(albedo), smoothness, 1.0,
                                            alpha, i.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }
}
