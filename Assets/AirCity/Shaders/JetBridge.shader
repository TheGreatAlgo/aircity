// The baked atlas carries detail and AO. Vertex zones independently tint cladding, glazing and roof.
Shader "AirCity/JetBridge"
{
    Properties
    {
        _BaseMap ("Surface", 2D) = "white" {}
        _MetallicGlossMap ("Metallic and smoothness", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _CladdingTint ("Cladding", Color) = (1,1,1,1)
        _GlassTint ("Glazing", Color) = (1,1,1,1)
        _RoofTint ("Roof", Color) = (1,1,1,1)
        _GlassAmount ("Glazing amount", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
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
            #include "AirfieldCommon.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _CladdingTint, _GlassTint, _RoofTint;
                half _GlassAmount;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
                half3 zones : COLOR;
            };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv; o.zones = v.color.rgb;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half smoothness = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv).a;
                half3 tint = 1 + i.zones.r * (_CladdingTint.rgb - 1) + i.zones.g * (_GlassTint.rgb - 1) + i.zones.b * (_RoofTint.rgb - 1);
                albedo *= tint * _BaseColor.rgb;
                // Opaque cladding can replace the glazed panels without altering the mechanical bridge definition.
                albedo = lerp(albedo, _CladdingTint.rgb * 0.65 * _BaseColor.rgb, i.zones.g * (1 - _GlassAmount));
                smoothness = lerp(smoothness, 0.35, i.zones.g * (1 - _GlassAmount));
                return AirfieldShade(i.positionWS, i.positionCS, normalize(i.normalWS), albedo, smoothness, 1, 1, i.fog);
            }
            ENDHLSL
        }
        UsePass "AirCity/Building/ShadowCaster"
        UsePass "AirCity/Building/DepthOnly"
        UsePass "AirCity/Building/DepthNormals"
    }
}
