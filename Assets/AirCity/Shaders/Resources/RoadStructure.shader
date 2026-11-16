Shader "AirCity/RoadStructure"
{
    Properties
    {
        _BaseColor ("Concrete",Color)=(0.43,0.45,0.43,1)
        [HideInInspector] _ConstructionZTest ("Depth test",Float)=4
        [HideInInspector] _ConstructionZWrite ("Depth write",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "../AirfieldCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        float _ConstructionZTest,_ConstructionZWrite;
        CBUFFER_END
        struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
        struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half fog:TEXCOORD2;};
        Varyings vert(Attributes v)
        {
            Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);
            o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o;
        }
        half4 frag(Varyings i):SV_Target
        {
            float grain=frac(sin(dot(floor(i.positionWS*34),float3(12.9898,78.233,31.517)))*43758.5453);
            float seam=pow(abs(sin(i.positionWS.y*3.14159265/1.2)),.18);
            half3 colour=_BaseColor.rgb*(.93+.07*grain)*lerp(.95,1,seam);
            return AirfieldShade(i.positionWS,i.positionCS,normalize(i.normalWS),colour,.12,1,1,i.fog);
        }
        half fragDepth(Varyings i):SV_Target {AirCityClipUnderground(i.positionWS);return i.positionCS.z;}
        half4 fragNormals(Varyings i):SV_Target {AirCityClipUnderground(i.positionWS);return AirfieldPackNormal(normalize(i.normalWS));}
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
            ZTest [_ConstructionZTest] ZWrite [_ConstructionZWrite] Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"} ColorMask R ZWrite On Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragDepth
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags {"LightMode"="DepthNormals"} ZWrite On Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment fragNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"} ZWrite On ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertShadow
            #pragma fragment fragDepth
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection,_LightPosition;
            Varyings vertShadow(Attributes v)
            {
                Varyings o=vert(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection=normalize(_LightPosition-o.positionWS);
                #else
                float3 lightDirection=_LightDirection;
                #endif
                o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,lightDirection)));return o;
            }
            ENDHLSL
        }
    }
}
