// Runway, taxiway and approach light glows: one camera-facing quad per light, a real size up close and never
// smaller than _MinPixels zoomed out (like AircraftLightGlow). Lights can show a different colour to each side
// of their axis (threshold green / end red, edge white / caution yellow), PAPI units turn red below their
// angle, and approach flashers run in sequence towards the threshold. All on the GPU; one draw per mesh.
// Brightness follows the global _AirCityLightGlow (night), with a faint _DayGlow by day.
//
// Mesh layout (built by AirfieldLightBuilder): position = the light, normal = horizontal axis (0 = the same
// from every side), color = colour seen from the -axis side, uv0 = quad corner (-1..1), uv1 = colour seen
// from the +axis side (a = 0: dark from that side), uv2 = (size multiplier, type, PAPI angle in degrees or
// flash delay in seconds, unused).
Shader "AirCity/AirfieldLightGlow"
{
    Properties
    {
        _Radius ("Radius (metres)", Float) = 0.7
        _MinPixels ("Min radius (pixels)", Float) = 2.2
        _MaxPixels ("Max radius (pixels)", Float) = 18
        _Intensity ("Intensity", Float) = 2.4
        _DayGlow ("Glow in daylight", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+40" "IgnoreProjector" = "True" }
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
            float _AirCityLightGlow;

            // light types (AirfieldLightType)
            #define TYPE_STEADY 0
            #define TYPE_PAPI 1
            #define TYPE_FLASHER 2

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 corner : TEXCOORD0;
                half4 colorB : TEXCOORD1;
                float4 params : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half3 color : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 axis = TransformObjectToWorldDir(v.normalOS, false);
                float3 toCam = GetCameraPositionWS() - posWS;
                float dist = length(toCam);
                toCam /= max(dist, 1e-4);
                int type = (int)round(v.params.y);

                // which side of the light the camera is on: blend the two colours across the axis
                half4 color = v.color;
                if (dot(axis, axis) > 1e-6)
                {
                    float side = dot(normalize(axis.xz), normalize(toCam.xz + 1e-5));
                    color = lerp(v.color, v.colorB, smoothstep(-0.12, 0.12, side));
                }
                half k = color.a;
                if (type == TYPE_PAPI)
                {
                    // white above the unit's angle, red below, with the thin pink transition of a real unit
                    float elevation = degrees(asin(saturate(toCam.y)));
                    color.rgb = lerp(half3(1.0, 0.08, 0.04), half3(1.0, 0.95, 0.88), smoothstep(-0.06, 0.06, elevation - v.params.z));
                }
                else if (type == TYPE_FLASHER)
                {
                    // sequenced flashers: twice a second, running towards the threshold
                    float f = frac(_Time.y * 2.0 - v.params.z);
                    k *= f < 0.05 ? 4.0 : 0.0;
                }

                float4 cs = TransformWorldToHClip(posWS + toCam * 0.15);
                float pixels = _Radius * v.params.x * abs(UNITY_MATRIX_P[1][1]) * 0.5 * _ScreenParams.y / cs.w;
                float size = clamp(pixels, _MinPixels * sqrt(v.params.x), _MaxPixels) * (k > 0.0 ? 1.0 : 0.0);
                cs.xy += v.corner * size * 2.0 / _ScreenParams.xy * cs.w;
                o.positionCS = cs;
                o.corner = v.corner;
                o.color = color.rgb * k * _Intensity * lerp(_DayGlow, 1.0, saturate(_AirCityLightGlow));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                half core = saturate(1.0 - r2 * 9.0);
                half halo = exp(-r2 * 5.0) * saturate(1.0 - r2);
                return half4(i.color * (core * 1.5 + halo * 0.6), 0);
            }
            ENDHLSL
        }
    }
}
