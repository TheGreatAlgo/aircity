// Afterburner flames: per nozzle, an inner core with shock diamonds, an outer plume and a hot disc in the nozzle
// exit, all additive. The mesh only carries where each nozzle is; the vertex shader shapes the flame, so its length
// follows the throttle and flickers without touching the mesh. How lit the afterburners are (0-255) and a
// per-aircraft flicker phase come from the renderer's shader user value, so every aircraft shares one material.
//
// Mesh layout (built by AircraftAfterburner): position = nozzle exit centre, normal = jet direction,
// tangent.xyz = this vertex's radial direction, uv0 = (t along the flame 0..1, layer 0 core / 1 plume / 2 disc),
// uv1 = (exit radius, nozzle phase).
Shader "AirCity/AircraftAfterburner"
{
    Properties
    {
        _CoreLength ("Core length (exit radii)", Float) = 8
        _PlumeLength ("Plume length (exit radii)", Float) = 19
        _Diamonds ("Shock diamonds in the core", Float) = 5
        _Intensity ("Intensity", Float) = 2.2
        _NightBoost ("Extra brightness at night", Float) = 0.6
        [HDR] _HotColor ("Hot (nozzle, diamonds)", Color) = (1.0, 0.86, 0.62, 1)
        [HDR] _CoreColor ("Core", Color) = (1.0, 0.55, 0.16, 1)
        [HDR] _PlumeColor ("Plume", Color) = (1.0, 0.30, 0.06, 1)
        [HDR] _RootColor ("Plume root (blue)", Color) = (0.35, 0.40, 1.0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+40" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _CoreLength, _PlumeLength, _Diamonds, _Intensity, _NightBoost;
                half4 _HotColor, _CoreColor, _PlumeColor, _RootColor;
            CBUFFER_END
            float _AirCityLightGlow;     // global: 0 = daylight, 1 = night (AircraftLights.Glow)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 tLayer : TEXCOORD0;
                float2 radiusPhase : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float4 data : TEXCOORD2;      // t, layer, level, phase
            };

            // 0..1 noise-ish flicker from a few incommensurate sines
            float Flicker(float t, float phase)
            {
                float x = _Time.y * 31.0 + phase * 13.0;
                return 0.5 + 0.22 * sin(x + t * 9.0) + 0.16 * sin(x * 1.73 - t * 17.0) + 0.12 * sin(x * 2.91 + t * 5.0);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                uint state = unity_RendererUserValue;
                float level = (state & 255u) / 255.0;
                float phase = ((state >> 8) & 255u) / 255.0 * 6.2832 + v.radiusPhase.y;
                float t = v.tLayer.x;
                float layer = v.tLayer.y;
                float R = v.radiusPhase.x;
                float3 dir = v.normalOS;
                float3 radial = v.tangentOS.xyz;

                // the flame grows as the afterburner lights, and breathes a little
                float grow = (0.45 + 0.55 * level) * (0.96 + 0.08 * Flicker(0.0, phase));
                float3 p = v.positionOS.xyz;
                float3 n = radial;
                if (layer < 0.5)
                {
                    // core: leaves the nozzle at 80% of its radius, pinched at every shock diamond, closes to a point
                    float d = frac(t * _Diamonds);
                    float pinch = 1.0 - 0.28 * (1.0 - abs(d * 2.0 - 1.0)) * (1.0 - t);
                    float r = R * 0.8 * pinch * pow(saturate(1.0 - t), 0.7);
                    p += dir * (t * R * _CoreLength * grow) + radial * r;
                }
                else if (layer < 1.5)
                {
                    // plume: swells a little past the exit, then tapers to a ragged point
                    float r = R * (0.95 + 0.35 * t) * pow(saturate(1.0 - t), 0.55);
                    r *= 0.92 + 0.16 * Flicker(t, phase + dot(radial, float3(1.7, 2.3, 0.9)));
                    p += dir * (t * R * _PlumeLength * grow) + radial * r;
                }
                else
                {
                    // disc in the nozzle exit (t is the radius here): the glow seen from behind
                    p += dir * 0.02 + radial * (t * R * 0.97);
                    n = dir;
                }
                float3 posWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(n);
                o.viewWS = GetCameraPositionWS() - posWS;
                o.data = float4(t, layer, level, phase);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = i.data.x, layer = i.data.y, level = i.data.z, phase = i.data.w;
                float facing = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float flick = 0.8 + 0.4 * Flicker(t, phase);
                half3 c;
                if (layer < 0.5)
                {
                    // bright bands at the shock diamonds, fading down the core
                    float d = frac(t * _Diamonds + 0.5) - 0.5;
                    float diamond = exp(-d * d * 90.0) * (1.0 - t * 0.85);
                    half3 col = lerp(_CoreColor.rgb, _HotColor.rgb, saturate(1.2 - t * 1.6));
                    float fall = saturate(1.0 - t) * saturate(t * 12.0 + 0.4);
                    c = col * fall * (0.55 + 1.6 * diamond) * lerp(0.35, 1.0, facing);
                }
                else if (layer < 1.5)
                {
                    // plume: a bluish root, then orange fading to dull red; soft at the silhouette and the tip
                    half3 col = lerp(_RootColor.rgb, _PlumeColor.rgb, saturate(t * 5.0));
                    col = lerp(col, _PlumeColor.rgb * half3(0.8, 0.45, 0.4), saturate(t * 1.3 - 0.3));
                    float fall = pow(saturate(1.0 - t), 1.6);
                    c = col * fall * 0.45 * pow(facing, 1.3);
                }
                else
                {
                    // the disc: white-hot centre to an orange rim, only seen looking up the jet
                    half3 col = lerp(_HotColor.rgb * 1.6, _CoreColor.rgb, t * t);
                    c = col * facing * (1.0 - t * 0.5);
                }
                float k = level * flick * _Intensity * (1.0 + _NightBoost * saturate(_AirCityLightGlow));
                return half4(c * k, 0);
            }
            ENDHLSL
        }
    }
}
