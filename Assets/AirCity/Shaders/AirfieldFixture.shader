// Small airfield hardware: light fittings, approach-light stands, dirt-strip edge markers. Flat vertex colours,
// lit like the ground; lamp glass (uv0.x = 1) glows in its colour at night. Each fitting shrinks to nothing
// towards its fade distance, where it would be smaller than a pixel and only shimmer (the glows take over).
//
// Mesh layout (built by LightBuilder): color = albedo, uv0.x = lamp glass 0..1, uv1 = (fitting origin, fade
// distance in metres).
Shader "AirCity/AirfieldFixture"
{
    Properties
    {
        _Smoothness ("Smoothness", Range(0, 1)) = 0.4
        _GlassGlow ("Lamp glass glow at night", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "AirfieldCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Smoothness, _GlassGlow;
        CBUFFER_END
        float _AirCityLightGlow;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv0 : TEXCOORD0;
            float4 uv1 : TEXCOORD1;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 color : COLOR;
            half glass : TEXCOORD2;
            half fogFactor : TEXCOORD3;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 origin = TransformObjectToWorld(v.uv1.xyz);
            float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
            float fade = saturate((v.uv1.w - distance(origin, GetCameraPositionWS())) / (v.uv1.w * 0.3));
            o.positionWS = origin + (posWS - origin) * fade;
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.color = v.color;
            o.glass = v.uv0.x;
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            return o;
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
            #pragma multi_compile_fog

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = AirfieldShade(i.positionWS, i.positionCS, normalize(i.normalWS), i.color.rgb, _Smoothness, 1.0, 1.0,
                                        i.fogFactor);
                c.rgb += i.color.rgb * i.glass * _GlassGlow * saturate(_AirCityLightGlow);
                return c;
            }
            ENDHLSL
        }
    }
}
