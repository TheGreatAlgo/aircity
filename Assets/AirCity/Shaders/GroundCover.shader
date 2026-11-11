Shader "AirCity/GroundCover"
{
    Properties
    {
        _BaseMap ("Rock albedo", 2D) = "white" {}
        [Normal] _NormalMap ("Rock normal", 2D) = "bump" {}
        _UseMap ("Use scanned rock surface", Float) = 0
        _Tint ("Tint", Color) = (1,1,1,1)
        _Rock ("Stone material", Float) = 0
        _Wind ("Wind movement (metres)", Float) = .055
        [HideInInspector] _FadeRange ("Distance fade", Vector) = (0,0,0,0)
        [HideInInspector] _LodRange ("LOD blend", Vector) = (0,0,0,0)
        [HideInInspector] _LodRole ("LOD role", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _UseMap, _Rock, _Wind, _LodRole;
            float4 _FadeRange, _LodRange;
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
            half4 color : TEXCOORD2;
            half fog : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898,78.233,45.164))) * 43758.5453); }
        float Noise(float3 p)
        {
            float3 cell = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash(cell),Hash(cell+float3(1,0,0)),f.x),
                lerp(Hash(cell+float3(0,1,0)),Hash(cell+float3(1,1,0)),f.x),f.y),
                lerp(lerp(Hash(cell+float3(0,0,1)),Hash(cell+float3(1,0,1)),f.x),
                lerp(Hash(cell+float3(0,1,1)),Hash(cell+float3(1,1,1)),f.x),f.y),f.z);
        }
        Varyings Vertex(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output; UNITY_TRANSFER_INSTANCE_ID(input, output);
            float3 p = TransformObjectToWorld(input.positionOS.xyz);
            float3 origin = TransformObjectToWorld(float3(0,0,0));
            float stiffness = saturate(input.positionOS.y);
            p.xz += float2(sin(_Time.y * 1.6 + origin.x * .07 + origin.z * .13),
                cos(_Time.y * 1.25 + origin.z * .07)) * stiffness * stiffness * _Wind;
            output.positionWS = p; output.positionCS = TransformWorldToHClip(p);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.color = input.color; output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        void DistanceClip(float3 p)
        {
            float distanceToCamera = distance(p, _WorldSpaceCameraPos);
            float spatialDither = Hash(floor(p * 80.0));
            if (_FadeRange.y > _FadeRange.x)
            {
                float coverage = 1.0 - saturate((distanceToCamera - _FadeRange.x) / (_FadeRange.y - _FadeRange.x));
                clip(coverage - spatialDither - .0001);
            }
            if (_LodRole > .5 && _LodRange.y > _LodRange.x)
            {
                float transition = saturate((distanceToCamera - _LodRange.x) / (_LodRange.y - _LodRange.x));
                // Complementary thresholds ensure each point belongs to exactly one distance mesh.
                float lodDither = Hash(floor(p * 80.0) + float3(41,17,93));
                clip(_LodRole < 1.5 ? (1.0 - transition) - lodDither - .0001 : lodDither - (1.0 - transition));
            }
        }
        half3 RockNormal(float3 p, half3 normal, half3 blend)
        {
            half3 nx = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, p.zy * .5));
            half3 ny = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, p.xz * .5));
            half3 nz = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, p.xy * .5));
            half3 world = half3(nx.z * sign(normal.x), nx.y, nx.x) * blend.x
                + half3(ny.x, ny.z * sign(normal.y), ny.y) * blend.y
                + half3(nz.x, nz.y, nz.z * sign(normal.z)) * blend.z;
            return normalize(lerp(normal, world, .32));
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            half4 Fragment(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input); DistanceClip(input.positionWS);
                half3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0, -1.0);
                half3 albedo = input.color.rgb * _Tint.rgb;
                if (_Rock > .5)
                {
                    half3 weights = pow(abs(normal), 4.0); weights /= max(.001, weights.x + weights.y + weights.z);
                    if (_UseMap > .5)
                    {
                        half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.zy * .5).rgb * weights.x
                            + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.xz * .5).rgb * weights.y
                            + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.positionWS.xy * .5).rgb * weights.z;
                        albedo *= tex; normal = RockNormal(input.positionWS, normal, weights);
                    }
                    else
                    {
                        float grain = Noise(input.positionWS * 25.0), fracture = Noise(input.positionWS * 3.5);
                        albedo *= half3(.43,.44,.42) * lerp(.75,1.18,grain) * lerp(.88,1.10,fracture);
                    }
                    float lichen = smoothstep(.61,.77,Noise(input.positionWS * 1.8)) * saturate(normal.y + .15) * .23;
                    albedo = lerp(albedo, half3(.31,.33,.22), lichen);
                }
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 ambient = SampleSH(normal);
                half diffuse = saturate(dot(normal, sun.direction));
                half3 light = ambient + sun.color * diffuse * sun.shadowAttenuation;
                if (_Rock < .5) light += sun.color * .12 * saturate(dot(-normal,sun.direction)) * sun.shadowAttenuation;
                float3 origin = TransformObjectToWorld(float3(0,0,0));
                half variation = lerp(.87,1.10,Hash(floor(origin * .2)));
                half3 result = albedo * light * lerp(.63,1.0,input.color.a) * variation;
                return half4(MixFog(result, input.fog), 1);
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
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVertex(Attributes input)
            {
                Varyings output = Vertex(input);
                float3 direction = _LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction = normalize(_LightPosition - output.positionWS);
            #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, direction));
            #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
            #else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
            #endif
                return output;
            }
            half4 ShadowFragment(Varyings input) : SV_Target { UNITY_SETUP_INSTANCE_ID(input); DistanceClip(input.positionWS); return 0; }
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
            #pragma multi_compile_instancing
            half4 DepthFragment(Varyings input) : SV_Target { UNITY_SETUP_INSTANCE_ID(input); DistanceClip(input.positionWS); return input.positionCS.z; }
            ENDHLSL
        }
    }
}
