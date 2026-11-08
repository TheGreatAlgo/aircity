namespace AirCity.Vehicles
{
    /// <summary>Game pacing shared by handover timing and animation; airport and aircraft clocks stay independent.</summary>
    public static class BaggagePacing
    {
        public const float WorkRate=18f;
        public const float TravelRate=4f;
        public const float DockCreepDistance=2f;
    }
}
