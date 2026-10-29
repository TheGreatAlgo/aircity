using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Traffic
{
    public enum HoldKind
    {
        /// <summary>Before a taxiway junction: the aircraft goes on once it has the junction to itself and there's
        /// room beyond it (it never stops inside one).</summary>
        Junction = 0,
        /// <summary>A runway-holding position: lining up (the takeoff hold) or crossing, once the tower clears it.</summary>
        Runway = 1,
        /// <summary>Off the runway after landing: on once the rest of the way to the stand is free.</summary>
        Route = 2,
    }

    /// <summary>A place along a taxi route where the aircraft stops unless it has been cleared on.</summary>
    public sealed class Hold
    {
        public HoldKind kind;
        /// <summary>Where the nose wheel stops (metres along the way).</summary>
        public float at;
        /// <summary>Where what it was cleared for is given back: its tail out of the junction, off the runway past the
        /// far holding position (float.MaxValue for the takeoff hold: the takeoff gives the runway back).</summary>
        public float release = float.MaxValue;
        /// <summary>The junction's node (Junction holds).</summary>
        public int node = -1;
        public float zoneRadius;
        public Runway runway;
        public bool takeoff;
        public bool cleared, released;
        /// <summary>Why it's still holding (shown while it waits).</summary>
        public string waiting;
    }

    /// <summary>One taxiway section along the route: which way, and from where to where on it (metres from its node
    /// a), with the nose wheel's distance along the way where it is at uFrom, for mapping the two.</summary>
    public struct Traversal
    {
        public TaxiwayEdge edge;
        /// <summary>+1 from node a towards b, -1 the other way.</summary>
        public int dir;
        public float uFrom, uTo;
        /// <summary>The nose wheel's distance along the way at uFrom (on the straight; corners are close enough).</summary>
        public float dStart;

        public float Span => Mathf.Abs(uTo - uFrom);
        public float DEnd => dStart + Span;

        /// <summary>Where on the section a distance along the way is.</summary>
        public float U(float d) => uFrom + dir * Mathf.Clamp(d - dStart, 0f, Span);
    }

    /// <summary>
    /// A planned way on the ground (TaxiPlanner): the nose wheel's path, where it holds and what it needs there, the
    /// taxiway sections it uses (reserved, one way at a time), and where it ends: lined up on a runway for a
    /// departure, parked on a stand for an arrival.
    /// </summary>
    public sealed class TaxiRoute
    {
        public TaxiPath path;
        public readonly List<Hold> holds = new List<Hold>();
        public readonly List<Traversal> traversals = new List<Traversal>();
        /// <summary>The runway taken off from or landed on, and which way (from its start towards its end).</summary>
        public Runway runway;
        public bool fromStart;
        /// <summary>Departures: where the takeoff roll starts, metres from the runway's start along its axis.</summary>
        public float lineUpS;
        /// <summary>Departures: back-tracks along the runway and turns round to take off.</summary>
        public bool backTrack;
        /// <summary>Departures off a push-back stand: the way off it before the path starts (by tug or by hand).</summary>
        public PushBackPath pushBack;
        /// <summary>Arrivals: the stand it parks on.</summary>
        public StandLayout stand;
        /// <summary>Arrivals: the nose wheel's distance along the way at which it has vacated the runway.</summary>
        public float runwayFree = -1f;
        /// <summary>Arrivals: where it waits off the runway when the way on isn't free (null when there's no room to
        /// wait there before the next junction: the whole way must be free before it lands).</summary>
        public Hold vacate;
        /// <summary>Arrivals: where the landing roll goes, from touchdown (metres from the threshold).</summary>
        public float touchdownS;
        /// <summary>Arrivals: from here on (nose wheel, metres) the way turns into the stand, between the parked
        /// aircraft either side, so it doesn't stop for them.</summary>
        public float standFrom = float.MaxValue;
        /// <summary>Departures: until here the way leaves a taxi-out stand.</summary>
        public float standTo = -1f;
        /// <summary>Departures: the stand it leaves (taken for arrivals until it's clear of its front: Flight.ClearOfStand).</summary>
        public StandLayout leaving;
        public float cost;

        /// <summary>"27" or "Stand 4".</summary>
        public string Destination => stand != null ? stand.stand.Name
            : runway != null ? (fromStart ? runway.StartDesignator : runway.EndDesignator).Trim() : "";

        /// <summary>The runway designator for the way it's used ("27").</summary>
        public string RunwayName => runway == null ? "" : (fromStart ? runway.StartDesignator : runway.EndDesignator).Trim();
    }
}
