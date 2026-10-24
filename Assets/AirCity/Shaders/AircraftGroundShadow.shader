// Soft contact shadow under an aircraft (a quad with a baked occlusion texture), multiplied onto the ground.
// Inside the real shadow distance only a light ambient-occlusion pool is kept (the sun's shadow map draws the
// shape there); beyond it, where URP draws no shadows, the full baked shadow keeps aircraft grounded.
Shader "AirCity/AircraftGroundShadow"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Occlusion (alpha)", 2D) = "black" {}
        _Strength ("Strength beyond shadow distance", Range(0, 1)) = 0.7
        _NearStrength ("Strength inside shadow distance", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" }
        Pass
        {
            Name "GroundShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half _Strength, _NearStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half occ = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                half far = GetMainLightShadowFade(i.positionWS);       // 0 inside the shadow distance, 1 beyond
                half k = occ * lerp(_NearStrength, _Strength, far);
                return half4((1 - k).xxx, 1);
            }
            ENDHLSL
        }
    }
}
