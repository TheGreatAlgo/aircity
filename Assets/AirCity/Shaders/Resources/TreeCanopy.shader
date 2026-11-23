Shader "AirCity/TreeCanopy"
{
    // Forest trees (RegionTreePalette). Vertex colour is the surface colour, with the model's own shading baked in
    // (dark inside and under a crown, light at the tips). Without _CARDS: opaque trunks and crown volumes, the only
    // geometry of the two far levels. With _CARDS: alpha-tested boughs and leaf sprays over those volumes, whose
    // texture only varies the vertex colour, so every level of a tree keeps one colour.
    Properties
    {
        _BaseMap ("Bough or leaf spray atlas", 2D) = "white" {}
        _TexMean ("Mean colour of the atlas", Color) = (1,1,1,1)
        _Cutoff ("Spray coverage", Range(0,1)) = .4
        _Wind ("Branch motion (m)", Float) = .13
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            half4 _TexMean;
            float _Cutoff, _Wind;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2;
            half4 color:TEXCOORD3; // rgb surface colour, a: this tree's own tint
            half fog:TEXCOORD4; float3 positionOS:TEXCOORD5; UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float Hash(float3 p) { p = frac(p * .3183099 + .1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float Noise(float3 x)
        {
            float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x), lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x), lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
        }
        Varyings Vertex(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input); Varyings output; UNITY_TRANSFER_INSTANCE_ID(input, output);
            float3 origin = TransformObjectToWorld(float3(0,0,0));
            float3 p = TransformObjectToWorld(input.positionOS.xyz);
            float weight = saturate(input.positionOS.y / 25.0);
            p.xz += float2(sin(_Time.y + origin.x * .071 + origin.z * .043), cos(_Time.y * .81 + origin.z * .061)) * weight * weight * _Wind;
            output.positionWS = p; output.positionCS = TransformWorldToHClip(p);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS); output.uv = input.uv; output.positionOS = input.positionOS.xyz;
            output.color = half4(input.color.rgb, lerp(.86, 1.12, frac(sin(dot(origin.xz, float2(.047, .071))) * 431.37)));
            output.fog = ComputeFogFactor(output.positionCS.z); return output;
        }
        void Coverage(Varyings input)
        {
        #if defined(_CARDS)
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
        #endif
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_local _ _CARDS
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            half4 Fragment(Varyings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 albedo = input.color.rgb; half3 normal = normalize(input.normalWS);
            #if defined(_CARDS)
                half4 spray = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(spray.a - _Cutoff);
                // Brightness only: the atlas would otherwise pull every tree to its own hue.
                albedo *= min(dot(spray.rgb, half3(.3,.6,.1)) / dot(_TexMean.rgb, half3(.3,.6,.1)), 2.0);
            #else
                // A solid crown seen from underneath is its unlit inside.
                normal *= IS_FRONT_VFACE(face, 1, -1);
                // Leaf-scale mottling, faded out where a pixel covers more than a clump of leaves.
                float3 p = input.positionOS; float footprint = length(fwidth(p));
                float fine = lerp(.5, Noise(p * 6.3), saturate(1.3 - footprint * 5.0));
                albedo *= lerp(.74, 1.2, Noise(p * 2.3) * .55 + fine * .45);
            #endif
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                sun.shadowAttenuation = lerp(sun.shadowAttenuation, 1, GetMainLightShadowFade(input.positionWS)); // fade out at the end of the shadow range, as the terrain does
                // Wrapped: foliage scatters light round the terminator, a hard edge reads as a painted solid.
                half facing = dot(normal, sun.direction);
                half3 light = SampleSH(normal) + sun.color * saturate((facing + .3) / 1.3) * sun.shadowAttenuation;
            #if defined(_CARDS)
                light += sun.color * .10 * saturate(-facing) * sun.shadowAttenuation;
            #endif
                return half4(MixFog(albedo * light * input.color.a, input.fog), 1);
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
            #pragma multi_compile_local _ _CARDS
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
            half4 ShadowFragment(Varyings input):SV_Target { Coverage(input); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" } ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment DepthFragment
            #pragma multi_compile_local _ _CARDS
            #pragma multi_compile_instancing
            half4 DepthFragment(Varyings input):SV_Target { Coverage(input); return input.positionCS.z; }
            ENDHLSL
        }
    }
}
