Shader "AirCity/RegionOcean"
{
    Properties
    {
        _BedMap ("Sea bed height", 2D) = "black" {}
        _DeepColour ("Deep ocean", Color) = (.012,.12,.19,1)
        _ShallowColour ("Shallow lagoon", Color) = (.13,.61,.58,1)
        _SeaLevel ("Sea level", Float) = 40
        _RegionTime ("Simulation clock", Float) = 0
        _Inland ("Mapped inland water", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+20" }
        Pass
        {
            Name "CoastalWater"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BedMap); SAMPLER(sampler_BedMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColour, _ShallowColour;
                float4 _MapOrigin;
                float _SeaLevel, _RegionTime, _Inland;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };
            float Wave(float2 p, float t)
            { return sin(dot(p, float2(.024,.015)) + t * .7) * .075 + sin(dot(p, float2(-.014,.033)) - t * .51) * .045; }
            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 uv = (output.positionWS.xz - _MapOrigin.xy) * _MapOrigin.zw;
                float edge = min(min(uv.x,uv.y), min(1-uv.x,1-uv.y));
                output.positionWS.y += Wave(output.positionWS.xz, _RegionTime) * smoothstep(0,.035,edge) * (1-_Inland);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z); return output;
            }
            half4 Fragment(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz, uv = (p - _MapOrigin.xy) * _MapOrigin.zw;
                float2 bed = SAMPLE_TEXTURE2D(_BedMap, sampler_BedMap, saturate(uv)).rg;
                float inside = step(0,uv.x) * step(uv.x,1) * step(0,uv.y) * step(uv.y,1);
                float outsideMetres = length((uv - saturate(uv)) / _MapOrigin.zw);
                float waterLevel = lerp(_SeaLevel, input.positionWS.y, _Inland);
                // A terrain cut can expose this mesh inland. Keep the shoreline transition and horizon,
                // while discarding water reliably below the terrain at its own position.
                clip(lerp(1.0, waterLevel + .75 - bed.r, inside));
                float depth = max(.02, waterLevel - bed.r);
                float shoreDistance = bed.g + outsideMetres;
                float colourDepth = lerp(max(depth, max(0,shoreDistance-30) * .07),depth,_Inland);
                float shallow = exp(-colourDepth / 14.0);
                float t = _RegionTime;
                float a = dot(p, float2(.54,.22)) + t * 1.1;
                float b = dot(p, float2(-.31,.46)) - t * .8;
                float footprint = max(fwidth(a),fwidth(b));
                float cameraRange = distance(GetCameraPositionWS(),input.positionWS);
                float ripple = exp2(-footprint*footprint*2) * (1-smoothstep(180,650,cameraRange)) * lerp(1,.45,_Inland);
                float2 slope = float2(cos(a) * .055 + cos(b) * -.035, cos(a) * .022 + cos(b) * .05) * ripple;
                float3 normal = normalize(float3(-slope.x,1,-slope.y));
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                sun.shadowAttenuation = lerp(sun.shadowAttenuation, 1, GetMainLightShadowFade(input.positionWS)); // fade out at the end of the shadow range, as the terrain does
                float sparkle = pow(saturate(dot(reflect(-sun.direction, normal), view)), 110) * sun.shadowAttenuation;
                float fresnel = pow(1 - saturate(dot(normal, view)), 4);
                half3 colour = lerp(_DeepColour.rgb, _ShallowColour.rgb, shallow);
                float caustic = pow(saturate(sin(p.x * .29 + sin(p.y * .19 + t * .5)) * sin(p.y * .31 - t * .4)), 5);
                colour *= .985 + .04 * caustic * shallow * ripple;
                colour = lerp(colour, half3(.34,.52,.62), fresnel * .52);
                colour *= .42 + .58 * sun.shadowAttenuation;
                colour += sun.color * sparkle * .22 * ripple;
                // A thin broken wave front progresses across the shallows, rather than a static white outline.
                float surge = shoreDistance * .9 - t * .68 + sin(p.x * .13 + p.y * .17) * .75;
                float wash = pow(saturate(sin(surge)), 8) * (1 - smoothstep(.05,.5,depth)) * inside;
                // Rivers and lakes meet quiet banks; coastal surf would trace the source DEM's corners in white.
                wash *= (1-_Inland) * (1-smoothstep(300,1100,cameraRange));
                colour = lerp(colour, half3(.76,.87,.84), wash * .45);
                // Horizon quads span many kilometres: interpolating a vertex fog value after it has saturated
                // creates a visible rectangle where they meet the detailed region mesh.
                half fog = ComputeFogFactor(TransformWorldToHClip(input.positionWS).z);
                return half4(MixFog(colour, fog),1);
            }
            ENDHLSL
        }
    }
}
