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
        // Vertex colour alpha says what a surface is (RoadStructureBuilder): 1 plain concrete (also any mesh without
        // colours), about one half a bridge deck's side or a tunnel wall, with uv.x in metres along it, 0 painted
        // steel (rgb), about 0.9 a tunnel lamp glowing in its colour (rgb).
        struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;float2 uv:TEXCOORD0;};
        struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half fog:TEXCOORD2;half4 color:COLOR;float2 uv:TEXCOORD3;};
        Varyings vert(Attributes v)
        {
            Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);
            o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);o.color=v.color;o.uv=v.uv;return o;
        }
        float Hash3(float3 p) {return frac(sin(dot(p,float3(12.9898,78.233,31.517)))*43758.5453);}
        float Noise3(float3 p)
        {
            float3 c=floor(p),f=frac(p);f=f*f*(3-2*f);
            return lerp(lerp(lerp(Hash3(c),Hash3(c+float3(1,0,0)),f.x),lerp(Hash3(c+float3(0,1,0)),Hash3(c+float3(1,1,0)),f.x),f.y),
                lerp(lerp(Hash3(c+float3(0,0,1)),Hash3(c+float3(1,0,1)),f.x),lerp(Hash3(c+float3(0,1,1)),Hash3(c+float3(1,1,1)),f.x),f.y),f.z);
        }
        half4 frag(Varyings i):SV_Target
        {
            float3 p=i.positionWS;half3 normal=normalize(i.normalWS);
            float grain=Hash3(floor(p*34));
            half steel=step(i.color.a,.25),panel=step(.25,i.color.a)*step(i.color.a,.75),wall=saturate(1.5-abs(normal.y)*3);
            // cast concrete: cloudy from pour to pour, fine grain, rain streaks down upright faces
            float cloud=Noise3(p*.4)*.6+Noise3(p*1.7)*.4;
            float streak=smoothstep(.5,.85,Noise3(float3(p.x*2.6,p.y*.16,p.z*2.6)));
            float tone=.93+.07*grain+.2*(cloud-.5)-.14*streak*wall;
            // a deck's side is cast in 5 m lengths: a joint between them, each a slightly different grey
            float along=i.uv.x/5;
            float joint=1-smoothstep(.012,.012+fwidth(i.uv.x)*1.5,abs(frac(along+.5)-.5)*5);
            tone+=panel*(.1*(Hash3(float3(floor(along),3,7))-.5)-.3*joint);
            // anything else upright (columns, abutments, tunnel walls) shows its lifts: a line every 1.2 m of height
            float lift=1-smoothstep(.01,.01+fwidth(p.y)*1.5,abs(frac(p.y/1.2+.5)-.5)*1.2);
            tone-=(1-panel)*wall*.16*lift;
            half3 colour=lerp(_BaseColor.rgb*tone,i.color.rgb*(.95+.05*grain),steel);
            half lamp=step(.8,i.color.a)*step(i.color.a,.95);
            half4 shaded=AirfieldShade(p,i.positionCS,normal,colour,lerp(.12,.5,steel),1,1,i.fog);
            shaded.rgb=lerp(shaded.rgb,i.color.rgb*1.6,lamp);return shaded;
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
