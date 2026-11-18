// Generated buildings (terminals): flat vertex colours, lit by URP, casting shadows. Glazing (uv0.x = 1) is a dark,
// glossy tint that reflects the sky at grazing angles and, at night, shows the lit hall behind it (a warm glow,
// broken up per window bay so a facade doesn't light as one sheet).
//
// Mesh layout (built by TerminalBuilder): color = albedo, uv0 = (glass 0..1, smoothness, along the wall in metres,
// height in metres).
// HangarBuilder uses uv0.w for the indoor light mask and uv1 for the finish coordinates and zone.
Shader "AirCity/Building"
{
    Properties
    {
        _GlassTint ("Glass tint", Color) = (0.10, 0.15, 0.18, 1)
        _NightGlow ("Lit interior at night", Color) = (1.0, 0.82, 0.55, 1)
        _NightStrength ("Night glow strength", Float) = 1.4
        _BayWidth ("Window bay width (m)", Float) = 6
        [NoScaleOffset] _HangarSurface ("Hangar surface masks", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "AirfieldCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GlassTint, _NightGlow;
            float _NightStrength, _BayWidth;
        CBUFFER_END
        float _AirCityLightGlow;
        TEXTURE2D(_HangarSurface);
        SAMPLER(sampler_HangarSurface);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float4 uv0 : TEXCOORD0;
            float4 uv1 : TEXCOORD1;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 color : COLOR;
            float4 uv0 : TEXCOORD2;
            half fogFactor : TEXCOORD3;
            float4 facade : TEXCOORD4;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.color = v.color;
            o.uv0 = v.uv0;
            o.facade = v.uv1;
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
        }

        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma shader_feature_local_fragment _ _HANGAR_SURFACE
            #pragma multi_compile_fog

            half4 frag(Varyings i) : SV_Target
            {
                half glass = i.uv0.x;
                half3 n = normalize(i.normalWS);
                half3 albedo = lerp(i.color.rgb, i.color.rgb * _GlassTint.rgb, glass);
                half smooth = lerp(i.uv0.y, 0.92, glass);
                half3 hangarGlow = 0.0;
            #if defined(_HANGAR_SURFACE)
                // One mipmapped sample for cladding, standing-seam roof, concrete or the lift door.
                half zone = round(i.facade.z);
                if (zone > 0.5 && zone < 4.5)
                {
                    half4 surface = SAMPLE_TEXTURE2D(_HangarSurface, sampler_HangarSurface, i.facade.xy);
                    half mask = zone == 1.0 ? surface.r : zone == 2.0 ? surface.g : zone == 3.0 ? surface.b : surface.a;
                    albedo *= lerp(0.53, 1.41, mask);
                    smooth = saturate(smooth + (mask - 0.5) * 0.36);
                    if (zone == 1.0)
                    {
                        half splash = saturate(1.0 - i.facade.w * 0.19);
                        albedo *= lerp(1.0, 0.81 + surface.r * 0.15, splash);
                    }
                }
                half night = smoothstep(0.45, 0.64, _AirCityLightGlow);
                half emission = zone < 4.5 ? 0.0 : zone < 5.5 ? 1.7 : 0.105;
                hangarGlow = night * (emission + i.uv0.w * albedo * 0.25) * half3(1.0, 0.96, 0.9);
            #endif
                half4 c = AirfieldShade(i.positionWS, i.positionCS, n, albedo, smooth, 1.0, 1.0, i.fogFactor);
                // the lit hall behind the glass at night: each bay a little different, a few dark
                float2 bay = float2(floor(i.uv0.z / max(i.facade.y, 0.5)), i.facade.x);
                half h = Hash(bay);
                half lit = h > 0.12 ? lerp(0.55, 1.0, h) : 0.08;
                c.rgb += hangarGlow + glass * _NightGlow.rgb * lit * _NightStrength * saturate(_AirCityLightGlow);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            ShadowVaryings vertShadow(Attributes v)
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                ShadowVaryings output;output.positionWS=positionWS;output.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS,normalWS,lightDirection)));return output;
            }

            half4 fragShadow(ShadowVaryings i) : SV_Target { AirCityClipUnderground(i.positionWS);return 0; }
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

            half fragDepth(Varyings i) : SV_Target { AirCityClipUnderground(i.positionWS);return i.positionCS.z; }
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
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 fragNormals(Varyings i) : SV_Target { AirCityClipUnderground(i.positionWS);return AirfieldPackNormal(normalize(i.normalWS)); }
            ENDHLSL
        }
    }
}
