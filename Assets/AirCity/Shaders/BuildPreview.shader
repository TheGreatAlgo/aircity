Shader "AirCity/BuildPreview"
{
    Properties { _BaseColor ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct Input { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Output { float4 positionCS : SV_POSITION; half4 color : COLOR; };
            Output Vertex(Input input)
            {
                Output output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _BaseColor; return output;
            }
            half4 Fragment(Output input) : SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
