// Selection / hover outline: an inverted hull pushed out along the normals by a fixed number of pixels.
// Drawn by AirCity.Aircraft.Aircraft only while an aircraft is highlighted.
Shader "AirCity/AircraftOutline"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.74, 0.1, 1)
        _Width ("Width (pixels)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                // push the hull away from the camera so it never z-fights thin parts (wings are a pixel thick
                // from far away): it then only shows where it sticks out past the silhouette
                float3 view = posWS - GetCameraPositionWS();
                posWS += normalize(view) * (0.15 + length(view) * 0.006);
                float4 cs = TransformWorldToHClip(posWS);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = max(length(nCS), 1e-4);
                // offset in clip space: pixels -> NDC (2 / screen size), times w to undo the perspective divide
                cs.xy += nCS / len * _Width * 2.0 / _ScreenParams.xy * cs.w;
                o.positionCS = cs;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
