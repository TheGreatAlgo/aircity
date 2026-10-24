// Vehicle and depot lights (VehicleLights): one camera-facing glow quad per lamp, a few pixels wide however far
// the camera is, and flat pools of light on the ground where head, work and flood lights point (night only).
// Beacons rotate on the GPU: a beam sweeping round, so the glow pulses as it faces the camera. Which groups are on
// (and a per-vehicle phase) comes from the renderer's shader user value, so every vehicle shares one material.
// Brightness follows the global _AirCityLightGlow (0 = day, 1 = night).
//
// Mesh layout: position = the lamp (pools: their centre on the ground), normal = its direction (pools: their long
// axis, flat), color = its colour, uv0 = quad corner (-1..1), uv1 = (type, size, beacon phase, -) for lamps or
// (10 + type, half length, half width, -) for pools. Types: VehicleLightType (Head 0, Tail 1, Beacon 2, Work 3,
// Marker 4, Flood 5).
Shader "AirCity/VehicleLightGlow"
{
    Properties
    {
        _Radius ("Radius (metres)", Float) = 0.45
        _MinPixels ("Min radius (pixels)", Float) = 2.5
        _MaxPixels ("Max radius (pixels)", Float) = 24
        _Intensity ("Intensity", Float) = 3
        _DayGlow ("Glow in daylight", Range(0, 1)) = 0.3
        _PoolIntensity ("Ground pool intensity", Float) = 0.35
        _BeaconSpeed ("Beacon turns per second", Float) = 1.4
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
                float _Radius, _MinPixels, _MaxPixels, _Intensity, _DayGlow, _PoolIntensity, _BeaconSpeed;
            CBUFFER_END
            float _AirCityLightGlow;     // global: 0 = full daylight, 1 = night

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 corner : TEXCOORD0;
                float4 kind : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 corner : TEXCOORD0;      // xy corner, z = 1 for pools
                half4 color : COLOR;
            };

            // VehicleLightType -> user value bit of its group (tail lights go with the headlights)
            static const uint GroupBit[6] = { 1u, 1u, 2u, 4u, 8u, 16u };

            Varyings vert(Attributes v)
            {
                Varyings o;
                uint state = unity_RendererUserValue;
                uint type = (uint)round(v.kind.x);
                bool pool = type >= 10u;
                uint lamp = pool ? type - 10u : type;
                float on = (state & GroupBit[min(lamp, 5u)]) != 0u ? 1.0 : 0.0;
                float night = saturate(_AirCityLightGlow);
                float3 dirWS = normalize(TransformObjectToWorldDir(v.normalOS));

                if (pool)
                {
                    // flat quad on the ground along the lamp's direction; brighter towards the lamp
                    float3 rightOS = normalize(cross(float3(0, 1, 0), v.normalOS));
                    float3 posOS = v.positionOS.xyz + v.normalOS * (v.corner.y * v.kind.y) + rightOS * (v.corner.x * v.kind.z);
                    o.positionCS = TransformObjectToHClip(posOS) * (on * night > 0.001 ? 1.0 : 0.0);
                    o.corner = float3(v.corner, 1.0);
                    o.color = half4(v.color.rgb * on * night * _PoolIntensity, 1);
                    return o;
                }

                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 toCam = normalize(GetCameraPositionWS() - posWS);
                float k;
                if (lamp == 2u)
                {
                    // rotating beacon: a beam sweeping round (phase per vehicle and per lamp), with a dim core
                    float phase = ((state >> 8) & 255u) / 255.0 + v.kind.z;
                    float a = (_Time.y * _BeaconSpeed + phase) * 6.2831853;
                    float3 beamWS = normalize(TransformObjectToWorldDir(float3(cos(a), 0, sin(a))));
                    float2 flat = normalize(toCam.xz + 1e-5);
                    k = 0.25 + 2.6 * pow(saturate(dot(flat, normalize(beamWS.xz + 1e-5))), 10.0);
                    k *= lerp(0.9, 1.0, night);
                }
                else if (lamp == 4u)
                    k = 0.9;
                else
                {
                    // directional lamps fade out when seen from behind
                    k = smoothstep(-0.3, 0.35, dot(dirWS, toCam));
                    k *= lerp(_DayGlow, 1.0, night);
                }
                k *= on;

                posWS += toCam * 0.15;
                float4 cs = TransformWorldToHClip(posWS);
                // abs: the projection is y-flipped when rendering into a texture on some platforms (Metal)
                float pixels = _Radius * v.kind.y * abs(UNITY_MATRIX_P[1][1]) * 0.5 * _ScreenParams.y / cs.w;
                float size = clamp(pixels, _MinPixels * v.kind.y, _MaxPixels) * (k > 0.0 ? 1.0 : 0.0);
                cs.xy += v.corner * size * 2.0 / _ScreenParams.xy * cs.w;
                o.positionCS = cs;
                o.corner = float3(v.corner, 0.0);
                o.color = half4(v.color.rgb * k * _Intensity, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner.xy, i.corner.xy);
                if (i.corner.z > 0.5)
                {
                    // ground pool: soft ellipse, strongest near the lamp end (corner.y = -1)
                    half fall = saturate(1.0 - r2);
                    half nearLamp = lerp(1.0, 0.45, saturate(i.corner.y * 0.5 + 0.5));
                    return half4(i.color.rgb * fall * fall * nearLamp, 0);
                }
                half core = saturate(1.0 - r2 * 9.0);
                half halo = exp(-r2 * 5.0) * saturate(1.0 - r2);
                return half4(i.color.rgb * (core * 1.5 + halo * 0.6), 0);
            }
            ENDHLSL
        }
    }
}
