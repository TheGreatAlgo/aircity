using System.Collections.Generic;
using AirCity.Airfield;
using UnityEngine;
using Box = AirCity.Vehicles.AirportMaintenance.Box;

namespace AirCity.Vehicles
{
    /// <summary>Metre-scale shell, working lanes and independent equipment parking for each depot tier.</summary>
    public readonly struct BaggageDepotLayout
    {
        public readonly BaggageDepotSize size;
        public BaggageDepotLayout(BaggageDepotSize size) { this.size=size; }
        public bool Enclosed => size!=BaggageDepotSize.Small;
        public int Lanes => size==BaggageDepotSize.Large ? 4 : Enclosed ? 2 : 1;
        public int CartsPerTrain => Enclosed ? 3 : 2;
        public float HomeZ => Enclosed ? -48 : 0;
        public float EntryZ => Enclosed ? -76 : -26;
        public float HomeExitZ => Enclosed ? -20 : 20;
        public float WorkZ => size==BaggageDepotSize.Large ? 20 : Enclosed ? 16 : 0;
        public float WorkEntryZ => Enclosed ? -20 : -26;
        public float ExitZ => size==BaggageDepotSize.Large ? 48 : Enclosed ? 44 : 20;
        public float WallBottom => size==BaggageDepotSize.Large ? -12 : Enclosed ? -8 : -16;
        public float WallTop => size==BaggageDepotSize.Large ? 32 : Enclosed ? 28 : 10;
        public float Left => Enclosed ? 8 : 11;
        public float Right => size==BaggageDepotSize.Large ? 60 : Enclosed ? 36 : 24;
        public float Eaves => size==BaggageDepotSize.Large ? 7 : Enclosed ? 5.5f : 3.7f;
        public float Ridge => Eaves+(Enclosed ? 1.8f : .8f);
        public float YardRight => Enclosed ? BaggageDepot.Capacity(size).teams*12+10 : 50;
        public float LaneX(int lane) => Enclosed ? 12+lane*12 : 4;
        public float HomeX(int team) => Enclosed ? 12+team*12 : 28+team*12;
        public Vector2 Work(int lane) => new Vector2(LaneX(lane),WorkZ);
        public Vector2 Home(int team) => new Vector2(HomeX(team),HomeZ);
        public Vector2 Loader(int team) => new Vector2(HomeX(team)+5.5f,HomeZ+5);
        public int LaneFor(int team) => Mathf.Clamp(team/2,0,Lanes-1);

        // Local rectangles are also used by route planning: the shell has genuine openings, not black door decals.
        public IEnumerable<Rect> Solids()
        {
            yield return Rect.MinMaxRect(Left,WallBottom,Left+.22f,WallTop);
            yield return Rect.MinMaxRect(Right-.22f,WallBottom,Right,WallTop);
            foreach (float z in new[] { WallBottom,WallTop-.22f })
            {
                float start=Left;
                for (int i=0;i<Lanes && Enclosed;i++)
                {
                    float x=LaneX(i);
                    if (x-2.5f>start) yield return Rect.MinMaxRect(start,z,x-2.5f,z+.22f);
                    start=x+2.5f;
                }
                if (Right>start) yield return Rect.MinMaxRect(start,z,Right,z+.22f);
            }
            if (!Enclosed) yield return Rect.MinMaxRect(Left,WallBottom,Right,WallTop);
            else yield return Rect.MinMaxRect(Right-6.5f,WallTop-7.2f,Right-.5f,WallTop-.2f);
            // Equipment islands leave both the driving envelope and a continuous pedestrian strip clear.
            if (Enclosed)
                for (int i=0;i<Lanes;i++)
                    yield return Rect.MinMaxRect(LaneX(i)+5,WallBottom+3,LaneX(i)+9,WallTop-3);
        }
        public IEnumerable<Box> Obstacles(BaggageDepot depot)
        {
            foreach (var r in Solids())
                yield return Box.Of(depot.Point(r.center.x,r.center.y),depot.Facing,
                    new Bounds(Vector3.zero,new Vector3(r.width,Eaves,r.height)),.12f);
        }
    }
}
