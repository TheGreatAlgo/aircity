Shader "AirCity/RegionGlobe"
{
    Properties
    {
        _EarthMap ("NASA Blue Marble", 2D) = "black" {}
        _ViewLatitude ("Centre latitude", Float) = 0
        _ViewLongitude ("Centre longitude", Float) = 0
        _Zoom ("Zoom", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Fragment
            #include "UnityCG.cginc"
            sampler2D _EarthMap;
            float _ViewLatitude, _ViewLongitude, _Zoom;
            float4 Fragment(v2f_img input) : SV_Target
            {
                const float pi = 3.141592653589793;
                float2 discPosition = (input.uv - .5) * 2 / max(.05,_Zoom);
                float radiusSquared = dot(discPosition,discPosition), edgeWidth = max(.00001,fwidth(radiusSquared));
                float alpha = saturate((1-radiusSquared) / edgeWidth + .5);
                if (alpha <= 0) return 0;
                float3 normal = float3(discPosition,sqrt(max(0,1-radiusSquared)));
                float latitude = _ViewLatitude * pi / 180, longitude = _ViewLongitude * pi / 180;
                float slat = sin(latitude), clat = cos(latitude), slon = sin(longitude), clon = cos(longitude);
                float3 east = float3(clon,0,-slon), north = float3(-slat*slon,clat,-slat*clon), centre = float3(clat*slon,slat,clat*clon);
                float3 world = normalize(east*normal.x + north*normal.y + centre*normal.z);
                float lon = atan2(world.x,world.z), lat = asin(clamp(world.y,-1,1));
                float2 uv = float2(frac(lon / (2*pi) + .5), lat / pi + .5);
                float3 colour = tex2D(_EarthMap,uv).rgb;
                float diffuse = saturate(dot(normal,normalize(float3(-.45,.48,.8))));
                colour *= .34 + .76 * diffuse;
                float2 gridPosition = float2(lon,lat) / (pi / 6);
                float2 gridDistance = abs(frac(gridPosition + .5)-.5), gridWidth = max(fwidth(gridPosition),.00001);
                float2 gridLine = 1-smoothstep(gridWidth*.35,gridWidth*.9,gridDistance);
                float grid = max(gridLine.x * saturate(cos(lat)*3),gridLine.y);
                colour = lerp(colour,float3(.54,.74,.83),grid*.085);
                float atmosphere = pow(saturate(1-normal.z),3.3);
                colour += float3(.06,.27,.44) * atmosphere * .58;
                return float4(colour,alpha);
            }
            ENDCG
        }
    }
}
