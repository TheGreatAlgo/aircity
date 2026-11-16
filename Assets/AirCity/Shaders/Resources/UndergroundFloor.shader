Shader "AirCity/UndergroundFloor"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes {float4 positionOS:POSITION;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;};
            Varyings vert(Attributes v){Varyings o;o.world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
            half4 frag(Varyings i):SV_Target
            {
                float2 cell=abs(frac(i.world.xz/25.0-.5)-.5)/max(fwidth(i.world.xz/25.0),.0001);
                float gridLine=1-saturate(min(cell.x,cell.y));
                float fade=saturate(1-distance(i.world.xz,_WorldSpaceCameraPos.xz)/3500);
                return half4(lerp(half3(.025,.055,.065),half3(.055,.105,.115),gridLine*fade*.65),1);
            }
            ENDHLSL
        }
    }
}
