using System;
using System.Collections.Generic;
using AirCity.Aircraft;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>Where a particular aircraft parks. Door choice and bridge motion remain automatic.</summary>
    [Serializable]
    public sealed class StandServiceConfiguration
    {
        public AircraftType aircraft;
        public bool allowed = true;
        [Tooltip("Nose-wheel stop offset along the stand, metres; positive is further into it.")]
        public float stopOffset;
        [Tooltip("Bridges left unused for this aircraft; the others choose reachable doors automatically.")]
        public List<string> unusedBridges = new List<string>();

        public StandServiceConfiguration Clone() => new StandServiceConfiguration
        {
            aircraft = aircraft, allowed = allowed, stopOffset = stopOffset,
            unusedBridges = new List<string>(unusedBridges),
        };
    }
}
