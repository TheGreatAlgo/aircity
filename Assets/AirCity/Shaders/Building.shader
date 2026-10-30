// Generated buildings (terminals): flat vertex colours, lit by URP, casting shadows. Glazing (uv0.x = 1) is a dark,
// glossy tint that reflects the sky at grazing angles and, at night, shows the lit hall behind it (a warm glow,
// broken up per window bay so a facade doesn't light as one sheet).
//
// Mesh layout (built by TerminalBuilder): color = albedo, uv0 = (glass 0..1, smoothness, along the wall in metres,
// height in metres).
Shader "AirCity/Building"
{
    Properties
    {
        _GlassTint ("Glass tint", Color) = (0.10, 0.15, 0.18, 1)
        _NightGlow ("Lit interior at night", Color) = (1.0, 0.82, 0.55, 1)
        _NightStrength ("Night glow strength", Float) = 1.4
        _BayWidth ("Window bay width (m)", Float) = 6
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

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float4 uv0 : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 color : COLOR;
            float4 uv0 : TEXCOORD2;
            half fogFactor : TEXCOORD3;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.color = v.color;
            o.uv0 = v.uv0;
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
            #pragma multi_compile_fog

            half4 frag(Varyings i) : SV_Target
            {
                half glass = i.uv0.x;
                half3 n = normalize(i.normalWS);
                half3 albedo = lerp(i.color.rgb, _GlassTint.rgb, glass);
                half smooth = lerp(i.uv0.y, 0.92, glass);
                half4 c = AirfieldShade(i.positionWS, i.positionCS, n, albedo, smooth, 1.0, 1.0, i.fogFactor);
                // the lit hall behind the glass at night: each bay a little different, a few dark
                float2 bay = float2(floor(i.uv0.z / _BayWidth), floor(i.uv0.w / 5.0));
                half h = Hash(bay);
                half lit = h > 0.12 ? lerp(0.55, 1.0, h) : 0.08;
                c.rgb += glass * _NightGlow.rgb * lit * _NightStrength * saturate(_AirCityLightGlow);
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

            float4 vertShadow(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection)));
            }

            half4 fragShadow() : SV_Target { return 0; }
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

            half fragDepth(Varyings i) : SV_Target { return i.positionCS.z; }
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

            half4 fragNormals(Varyings i) : SV_Target { return AirfieldPackNormal(normalize(i.normalWS)); }
            ENDHLSL
        }
    }
}
