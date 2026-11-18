Shader "AirCity/TreeShadow"
{
    // Sun shadows of forest trees too far from the camera to cast with their own meshes: ForestRenderer merges a
    // plain low-poly crown per tree into static shadow-only meshes. Depth only, no texture.
    Properties
    {
        [HideInInspector] _Near ("Own meshes cast nearer than this (m)", Float) = 180
        [HideInInspector] _FadeStart ("Trees start dropping out (m)", Float) = 1800
        [HideInInspector] _End ("No tree casts beyond (m)", Float) = 2600
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" } ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Near, _FadeStart, _End;
            CBUFFER_END
            float3 _LightDirection, _LightPosition;
            // seed: one random value per tree, so whole trees (not pixels) drop out with distance.
            struct Attributes { float4 positionOS : POSITION; float seed : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float seed : TEXCOORD1; };
            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz); output.seed = input.seed;
                float3 direction = _LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction = normalize(_LightPosition - output.positionWS);
            #endif
                // The light direction as the normal: depth bias only, the crown is not shrunk.
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, direction, direction));
            #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
            #else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
            #endif
                return output;
            }
            half4 Fragment(Varyings input) : SV_Target
            {
                float range = distance(input.positionWS, GetCameraPositionWS());
                clip(min(range - _Near, input.seed - smoothstep(_FadeStart, _End, range)));
                return 0;
            }
            ENDHLSL
        }
    }
}
