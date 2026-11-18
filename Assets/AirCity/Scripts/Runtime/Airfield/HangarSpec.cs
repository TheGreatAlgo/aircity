using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Airfield
{
    public enum HangarSize { Small, Executive, Airline }

    public sealed class HangarSpec
    {
        public static HangarSpec For(HangarSize size) => size switch
        {
            HangarSize.Executive => Executive,
            HangarSize.Airline => Airline,
            _ => Small
        };

        static readonly HangarSpec Small = new HangarSpec("Small aircraft hangar", 1, SmallHangar.Width, SmallHangar.Depth,
            SmallHangar.DoorWidth, SmallHangar.DoorHeight, SmallHangar.EaveHeight, SmallHangar.RidgeHeight, 'A',
            SmallHangar.RoofOverhang - .05f, HangarConnection.PreferredApproach, HangarConnection.MaxApproach, 12f,
            SmallHangar.BuildPrice, SmallHangar.MonthlyRent);
        static readonly HangarSpec Executive = new HangarSpec("Executive hangar", 3, 28f, 42f, 80f, 9f, 11.5f, 14.5f,
            'B', 30f, 38f, 50f, 24f, 96000, 2300);
        internal static readonly HangarSpec OriginalExecutive = new HangarSpec("Executive hangar", 3, 28f, 32f, 80f, 9f, 10.5f, 13.5f,
            'B', 30f, 38f, 50f, 24f, 96000, 2300);
        static readonly HangarSpec Airline = new HangarSpec("Airline hangar", 1, 92f, 90f, 84f, 27f, 29f, 35f,
            'C', 80f, 95f, 115f, 72f, 420000, 10500);

        public readonly string Name;
        public readonly int Bays, Price, Rent;
        public readonly float BayWidth, Depth, DoorWidth, DoorHeight, Eave, Ridge;
        public readonly float MinSetback, Setback, MaxSetback, Frontage;
        public readonly char TaxiCode;
        public float Width => BayWidth * Bays;
        public float ApproachWidth => Bays > 1 ? Width : DoorWidth;
        public float ConnectionHalfSpan => Frontage + BayOffset(Bays - 1);
        public float AircraftSpan => Bays > 1 ? 24f : DoorWidth - 1f;
        public float AircraftLength => Bays > 1 ? 30f : Depth - 2f;
        public float BayOffset(int index) => (index - Bays / 2) * BayWidth;

        HangarSpec(string name, int bays, float bayWidth, float depth, float doorWidth, float doorHeight,
            float eave, float ridge, char taxiCode, float minSetback, float setback, float maxSetback, float frontage, int price, int rent)
        {
            Name = name; Bays = bays; BayWidth = bayWidth; Depth = depth;
            DoorWidth = doorWidth; DoorHeight = doorHeight; Eave = eave; Ridge = ridge;
            TaxiCode = taxiCode; MinSetback = minSetback; Setback = setback; MaxSetback = maxSetback; Frontage = frontage;
            Price = price; Rent = rent;
        }

        public Vector2[] Footprint(Vector2 door, float heading, bool wholeBuilding = true)
        {
            float half = (wholeBuilding ? Width : BayWidth) * .5f + SmallHangar.RoofOverhang;
            float overhang = SmallHangar.RoofOverhang;
            var axis = SmallHangar.Facing(heading);
            var right = Geo.Right(axis);
            return new[] { door - right * half - axis * (Depth + overhang), door + right * half - axis * (Depth + overhang),
                door + right * half + axis * overhang, door - right * half + axis * overhang };
        }
    }
}
