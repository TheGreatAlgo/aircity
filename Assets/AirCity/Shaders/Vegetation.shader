Shader "AirCity/Vegetation"
{
    Properties
    {
        _BaseMap ("Branch atlas", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        [Toggle(_FOLIAGE)] _Foliage ("Foliage", Float) = 1
        _Cutoff ("Needle coverage", Range(0,1)) = .38
        _Wind ("Wind movement (metres)", Float) = .18
        [HideInInspector] _BakeMode ("Unlit asset bake", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _Cutoff, _Wind, _Foliage, _BakeMode;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half4 color : COLOR;
            half fog : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float3 WindPosition(Attributes input)
        {
            float3 p = TransformObjectToWorld(input.positionOS.xyz);
            float3 origin = TransformObjectToWorld(float3(0,0,0));
            float weight = saturate(input.positionOS.y / 24.0);
            float phase = origin.x * .071 + origin.z * .043;
            p.xz += float2(sin(_Time.y * 1.05 + phase), cos(_Time.y * .81 + phase)) * weight * weight * _Wind;
            return p;
        }
        Varyings Vertex(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output;
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            output.positionWS = WindPosition(input);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv; output.color = input.color;
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        half4 Surface(Varyings input)
        {
        #if defined(_FOLIAGE)
            half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            clip(c.a - _Cutoff);
            return half4(c.rgb * input.color.rgb * _Tint.rgb, c.a);
        #else
            float grain = sin(input.uv.x * 143.0 + sin(input.uv.y * 7.3) * .85 + sin(input.uv.y * 29.1) * .2);
            float fissure = smoothstep(-.85, .2, grain);
            float fleck = frac(sin(dot(floor(input.uv * float2(160,45)), float2(12.9898,78.233))) * 43758.5453);
            return half4(_Tint.rgb * lerp(.52,1.23,fissure) * lerp(.84,1.1,fleck),1);
        #endif
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            AlphaToMask On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma shader_feature_local _FOLIAGE
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 surface = Surface(input);
                if (_BakeMode > .5) return surface;
                half3 normal = normalize(input.normalWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half light = saturate(dot(normal, sun.direction));
                half3 ambient = SampleSH(normal);
                float3 origin = TransformObjectToWorld(float3(0,0,0));
                half variation = lerp(.84,1.12,frac(sin(dot(origin.xz,float2(.047,.071))) * 431.37));
                half3 lit = ambient + sun.color * light * sun.shadowAttenuation;
            #if defined(_FOLIAGE)
                // Soft backlighting and a little unshadowed sky keep individual needle silhouettes readable.
                lit += sun.color * .15 * saturate(dot(-normal,sun.direction)) * sun.shadowAttenuation;
                lit = max(lit, half3(.17,.19,.16));
            #endif
                return half4(MixFog(surface.rgb * lit * variation, input.fog), surface.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma shader_feature_local _FOLIAGE
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVertex(Attributes input)
            {
                Varyings output = Vertex(input);
                float3 direction = _LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction = normalize(_LightPosition-output.positionWS);
            #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, direction));
            #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
            #else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
            #endif
                return output;
            }
            half4 ShadowFragment(Varyings input) : SV_Target { Surface(input); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment DepthFragment
            #pragma shader_feature_local _FOLIAGE
            #pragma multi_compile_instancing
            half4 DepthFragment(Varyings input) : SV_Target { Surface(input); return input.positionCS.z; }
            ENDHLSL
        }
    }
}
