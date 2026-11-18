using System;
using AirCity.Airfield;

namespace AirCity.Game
{
    /// <summary>Live session income. Saved hangar remainders are independent of weather/flight reconstruction.</summary>
    public static class HangarAccounts
    {
        public static int Advance(Airport airport, CareerState state, float seconds)
        {
            if (airport == null || state == null || state.mode == GameMode.Sandbox || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return 0;
            long earned=0;
            foreach (var hangar in airport.Hangars) earned += hangar.AccrueRent(seconds);
            int credited=(int)Math.Min(earned,Math.Max(0,100000000L-state.funds));
            state.funds+=credited; return credited;
        }
    }
}
