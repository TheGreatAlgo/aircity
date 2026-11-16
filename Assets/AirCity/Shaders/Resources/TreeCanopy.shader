Shader "AirCity/TreeCanopy"
{
    Properties
    {
        _BaseMap ("Distance silhouettes", 2D) = "white" {}
        _Canopy ("Leaf surface", Float) = 1
        _Impostor ("Distance silhouette", Float) = 0
        _Wind ("Branch motion", Float) = .13
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
            float _Canopy, _Impostor, _Wind;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half4 color:TEXCOORD3; half fog:TEXCOORD4; float3 positionOS:TEXCOORD5; UNITY_VERTEX_INPUT_INSTANCE_ID };
        float Hash(float3 p) { return frac(sin(dot(p,float3(12.9898,78.233,45.164)))*43758.5453); }
        Varyings Vertex(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input); Varyings output; UNITY_TRANSFER_INSTANCE_ID(input,output);
            float3 origin=TransformObjectToWorld(float3(0,0,0));
            float3 p=TransformObjectToWorld(input.positionOS.xyz);
            float weight=saturate(input.positionOS.y/25.0);
            p.xz+=float2(sin(_Time.y+origin.x*.071+origin.z*.043),cos(_Time.y*.81+origin.z*.061))*weight*weight*_Wind;
            output.positionWS=p; output.positionCS=TransformWorldToHClip(p);
            output.normalWS=TransformObjectToWorldNormal(input.normalOS); output.uv=input.uv; output.color=input.color; output.positionOS=input.positionOS.xyz;
            output.fog=ComputeFogFactor(output.positionCS.z); return output;
        }
        half3 Surface(Varyings input)
        {
            if (_Impostor>.5)
            {
                half3 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                // RGB silhouettes use black as a key so the runtime atlas can use shared BC1 compression.
                clip(max(color.r,max(color.g,color.b))-.045); return color;
            }
            if (_Canopy>.5 && input.color.a<.6)
            {
                float2 cell=floor(input.uv*7.0),f=frac(input.uv*7.0)-.5;
                float random=Hash(float3(cell,11.0));
                float angle=random*6.2831853,s=sin(angle),c=cos(angle);
                float2 leaflet=float2(f.x*c-f.y*s,f.x*s+f.y*c)/float2(.53,.39);
                clip(1.0-dot(leaflet,leaflet));
                clip((.45+random*.065)-length(input.uv-.5));
            }
            float leaf=Hash(floor(input.positionOS*float3(5.1,7.3,5.7)));
            float coarse=Hash(floor(input.positionOS*1.7));
            half variation=_Canopy>.5?lerp(.77,1.14,leaf)*lerp(.88,1.06,coarse):lerp(.63,1.15,leaf);
            return input.color.rgb*variation;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForward" }
            AlphaToMask On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            half4 Fragment(Varyings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input); half3 color=Surface(input);
                half3 normal=normalize(input.normalWS)*IS_FRONT_VFACE(face,1,-1);
                if (_Impostor>.5) normal=normalize(input.normalWS);
                if (_Canopy>.5 && _Impostor<.5)
                {
                    float3 leafNormal=float3(Hash(floor(input.positionOS*6)),Hash(floor(input.positionOS.zxy*6)+17),Hash(floor(input.positionOS.yzx*6)+31))-.5;
                    normal=normalize(normal+leafNormal*.32);
                }
                Light sun=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light=SampleSH(normal)+sun.color*saturate(dot(normal,sun.direction))*sun.shadowAttenuation;
                if (_Canopy>.5) light+=sun.color*.12*saturate(dot(-normal,sun.direction))*sun.shadowAttenuation;
                float3 origin=TransformObjectToWorld(float3(0,0,0));
                half tint=lerp(.87,1.10,Hash(origin*.071));
                return half4(MixFog(color*max(light,half3(.13,.16,.12))*tint,input.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" } ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection,_LightPosition;
            Varyings ShadowVertex(Attributes input)
            {
                Varyings output=Vertex(input); float3 direction=_LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction=normalize(_LightPosition-output.positionWS);
            #endif
                output.positionCS=TransformWorldToHClip(ApplyShadowBias(output.positionWS,output.normalWS,direction));
            #if UNITY_REVERSED_Z
                output.positionCS.z=min(output.positionCS.z,UNITY_NEAR_CLIP_VALUE*output.positionCS.w);
            #else
                output.positionCS.z=max(output.positionCS.z,UNITY_NEAR_CLIP_VALUE*output.positionCS.w);
            #endif
                return output;
            }
            half4 ShadowFragment(Varyings input):SV_Target { Surface(input); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half4 DepthFragment(Varyings input):SV_Target { Surface(input); return input.positionCS.z; }
            ENDHLSL
        }
    }
}
