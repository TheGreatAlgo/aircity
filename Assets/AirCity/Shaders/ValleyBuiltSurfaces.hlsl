#ifndef AIRCITY_VALLEY_BUILT_SURFACES_INCLUDED
#define AIRCITY_VALLEY_BUILT_SURFACES_INCLUDED

#include "UndergroundRoadView.hlsl"

// Exact pavement footprints and vertical excavation ranges, shared by all terrain passes.
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
        float4 first=_AirCitySurfaceTriangles[index];
        float4 second=_AirCitySurfaceTriangles[index+1];
        float4 third=_AirCitySurfaceTriangles[index+2];
        float3 a=first.xyz,b=second.xyz,c=third.xyz;
        float2 ab=b.xy-a.xy, bc=c.xy-b.xy, ca=a.xy-c.xy;
        float area=AirCitySurfaceCross(ab,-ca);
        if(abs(area)<0.0001)continue;
        float2 p=positionWS.xz;
        float3 sides=float3(AirCitySurfaceCross(ab,p-a.xy),AirCitySurfaceCross(bc,p-b.xy),AirCitySurfaceCross(ca,p-c.xy));
        float3 inward=sides*sign(area);
        if(any(inward<0.0))
        {
            // A fixed tolerance in triangle-area units shrinks as the road sections get longer.
            float3 edgeScale=max(abs(float3(ab.x,bc.x,ca.x)),abs(float3(ab.y,bc.y,ca.y)));
            if(any(inward < -third.w*edgeScale) || any(p < min(a.xy,min(b.xy,c.xy))-third.w) ||
                any(p > max(a.xy,max(b.xy,c.xy))+third.w))continue;
        }
        float surfaceHeight=dot(sides.yzx,float3(a.z,b.z,c.z))/area;
        // Clip from the physical floor upwards. A tunnel also has an upper limit at its roof.
        float height = positionWS.y - surfaceHeight;
        if (height >= -second.w && (first.w <= 0.0 || height <= first.w)) { covered = 1.0; break; }
    }
    // Discard once after the exact ownership test.
    clip(0.5-covered);
}
#endif
