// Aircraft light glows: one camera-facing quad per light. Up close a glow has a real size (_Radius metres);
// zoomed out it never shrinks below _MinPixels, so navigation lights, strobes and beacons stay visible as
// points at any zoom without swamping the aircraft. Flash patterns run on the GPU. Which light groups are
// on (and a per-aircraft flash phase) comes from the renderer's shader user value, so every aircraft shares
// one material and batches. Brightness scales with the global _AirCityLightGlow (raise it at night).
//
// Mesh layout (built by AircraftLights): position = the light, normal = its direction, color = its colour,
// uv0 = quad corner (-1..1), uv1 = (cos of half the visibility sector, or -2 for all round; light type),
// uv2 = (flash phase offset, size multiplier).
Shader "AirCity/AircraftLightGlow"
{
    Properties
    {
        _Radius ("Radius (metres)", Float) = 0.9
        _MinPixels ("Min radius (pixels)", Float) = 3.5
        _MaxPixels ("Max radius (pixels)", Float) = 28
        _Intensity ("Intensity", Float) = 3
        _DayGlow ("Glow in daylight", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Radius, _MinPixels, _MaxPixels, _Intensity, _DayGlow;
            CBUFFER_END
            float _AirCityLightGlow;     // global: 0 = full daylight, 1 = night

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 corner : TEXCOORD0;
                float2 sectorType : TEXCOORD1;
                float2 phaseSize : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half4 color : COLOR;
            };

            // light types (AircraftLightType) -> user value bit of their group
            static const uint GroupBit[6] = { 1u, 4u, 2u, 8u, 16u, 32u };

            float Flash(uint type, float t)
            {
                if (type == 1u)   // strobe: double flash every 1.1 s
                {
                    float f = frac(t / 1.1);
                    return (f < 0.035 || (f > 0.11 && f < 0.145)) ? 3.0 : 0.0;
                }
                if (type == 2u)   // beacon: one 0.18 s flash per second
                {
                    float f = frac(t);
                    return smoothstep(0.0, 0.03, f) * (1.0 - smoothstep(0.12, 0.2, f)) * 1.3;
                }
                return 1.0;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                uint state = unity_RendererUserValue;
                uint type = (uint)round(v.sectorType.y);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 dirWS = normalize(TransformObjectToWorldDir(v.normalOS));
                float3 toCam = normalize(GetCameraPositionWS() - posWS);

                // visible only inside the light's sector (navigation lights) and when its group is on
                float sector = v.sectorType.x < -1.5 ? 1.0 : smoothstep(v.sectorType.x - 0.08, v.sectorType.x + 0.02, dot(dirWS, toCam));
                float on = (state & GroupBit[min(type, 5u)]) != 0u ? 1.0 : 0.0;
                float phase = ((state >> 8) & 255u) / 255.0;
                float k = sector * on * Flash(type, _Time.y + phase * 7.0 + v.phaseSize.x);

                // pulled towards the camera so the aircraft's own skin doesn't hide the glow
                posWS += toCam * 0.25;
                float4 cs = TransformWorldToHClip(posWS);
                // abs: the projection is y-flipped when rendering into a texture on some platforms (Metal)
                float pixels = _Radius * v.phaseSize.y * abs(UNITY_MATRIX_P[1][1]) * 0.5 * _ScreenParams.y / cs.w;
                float size = clamp(pixels, _MinPixels * v.phaseSize.y, _MaxPixels) * (k > 0.0 ? 1.0 : 0.0);
                cs.xy += v.corner * size * 2.0 / _ScreenParams.xy * cs.w;
                o.positionCS = cs;
                o.corner = v.corner;
                o.color = half4(v.color.rgb * k * _Intensity * lerp(_DayGlow, 1.0, saturate(_AirCityLightGlow)), 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                half core = saturate(1.0 - r2 * 9.0);                // bright centre
                half halo = exp(-r2 * 5.0) * saturate(1.0 - r2);      // soft falloff to the quad edge
                return half4(i.color.rgb * (core * 1.5 + halo * 0.6), 0);
            }
            ENDHLSL
        }
    }
}
