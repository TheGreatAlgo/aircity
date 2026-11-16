#ifndef AIRCITY_UNDERGROUND_ROAD_VIEW_INCLUDED
#define AIRCITY_UNDERGROUND_ROAD_VIEW_INCLUDED
float _AirCityUndergroundCut;
float _AirCityUndergroundHeight;
void AirCityClipUnderground(float3 positionWS)
{
    if(_AirCityUndergroundCut>.5)clip(_AirCityUndergroundHeight-positionWS.y);
}
#endif
