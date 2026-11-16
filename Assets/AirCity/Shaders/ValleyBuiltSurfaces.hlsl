#ifndef AIRCITY_VALLEY_BUILT_SURFACES_INCLUDED
#define AIRCITY_VALLEY_BUILT_SURFACES_INCLUDED

#include "UndergroundRoadView.hlsl"

// Exact committed opaque pavement triangles. Transparent fringes and elevated roads never enter this index.
StructuredBuffer<float4> _AirCitySurfaceTriangles;
StructuredBuffer<uint2> _AirCitySurfaceCells;
StructuredBuffer<uint> _AirCitySurfaceIndices;

float AirCitySurfaceCross(float2 a,float2 b) { return a.x*b.y-a.y*b.x; }
void AirCityClipBuiltSurface(float3 positionWS)
{
    AirCityClipUnderground(positionWS);
    if(_AirCitySurfaceEnabled<.5)return;
    int2 cell=(int2)floor((positionWS.xz-_AirCitySurfaceGrid.xy)*_AirCitySurfaceGrid.z);
    int count=(int)_AirCitySurfaceGrid.w;
    if(any(cell<0) || any(cell>=count))return;
    uint2 span=_AirCitySurfaceCells[cell.y*count+cell.x];
    float covered=0.0;
    [loop]for(uint n=span.x;n<span.x+span.y;n++)
    {
        uint index=_AirCitySurfaceIndices[n]*3;
        float2 a=_AirCitySurfaceTriangles[index].xy,b=_AirCitySurfaceTriangles[index+1].xy,c=_AirCitySurfaceTriangles[index+2].xy;
        if(abs(AirCitySurfaceCross(b-a,c-a))<0.0001)continue;
        float2 p=positionWS.xz;
        float3 sides=float3(AirCitySurfaceCross(b-a,p-a),AirCitySurfaceCross(c-b,p-b),AirCitySurfaceCross(a-c,p-c));
        if(all(sides>=-0.00001) || all(sides<=0.00001)){covered=1.0;break;}
    }
    // Discard once after the exact ownership test.
    clip(0.5-covered);
}
#endif
