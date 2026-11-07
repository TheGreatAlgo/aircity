using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Traffic
{
    public enum MarshallingSignal { Identify, Ahead, Left, Right, Slow, Hold, Stop, Clear }
    public enum MarshallerPhase { WalkingIn, Guiding, Stopped, WalkingOut, Complete }

    /// <summary>One marshaller for each arriving stand visit. All timing and arrival limits work headless.</summary>
    public sealed class AirportMarshalling
    {
        readonly Airport airport;
        readonly List<StandMarshaller> crew = new List<StandMarshaller>();
        public IReadOnlyList<StandMarshaller> Crew => crew;
        public AirportMarshalling(Airport airport) { this.airport = airport; }
        public StandMarshaller For(Flight flight) => crew.Find(c => c.Flight == flight && c.Phase != MarshallerPhase.Complete && c.Phase != MarshallerPhase.WalkingOut);
        public bool Clearing(Aircraft.Aircraft aircraft) => aircraft != null && crew.Exists(c => c.Flight.aircraft == aircraft && c.Phase != MarshallerPhase.Complete);
        internal void Step(float dt, IReadOnlyList<Flight> flights)
        {
            foreach (var flight in flights)
                if (flight.kind == FlightKind.Arrival && flight.Phase == FlightPhase.Taxi && flight.Route?.stand != null &&
                    !flight.Route.stand.stand.DeIcing && flight.Route.path.Length-flight.Along < 100f && For(flight) == null)
                    crew.Add(new StandMarshaller(airport,flight));
            foreach (var worker in crew) worker.Step(dt);
            crew.RemoveAll(c => c.Phase == MarshallerPhase.Complete);
        }
    }

    public sealed class StandMarshaller
    {
        readonly Airport airport;
        readonly Stand stand;
        readonly int layoutVersion;
        readonly Vector3 station, waiting, facing;
        float walked, stopped, time, signalSince;
        public Flight Flight { get; }
        public MarshallerPhase Phase { get; private set; }
        public MarshallingSignal Signal { get; private set; }
        public MarshallingSignal PreviousSignal { get; private set; }
        public float SignalBlend => Mathf.SmoothStep(0,1,(time-signalSince)/.5f);
        public float Elapsed => time;
        public float Remaining => Mathf.Max(0,Flight.Route.path.Length-Flight.Along);
        public Vector3 Position => Vector3.Lerp(waiting,station,walked);
        public bool Ready => Phase != MarshallerPhase.WalkingIn;
        public float SpeedLimit => Remaining < 8 ? .65f : Remaining < 35 ? 1.5f : float.MaxValue;

        internal StandMarshaller(Airport airport, Flight flight)
        {
            this.airport = airport; Flight = flight; stand = flight.Route.stand.stand;
            layoutVersion = airport.LayoutVersion;
            var layout = flight.Route.stand; var nose = layout.nose;
            var front = layout.ParkPosition(flight.type)+nose*(flight.type.bounds.max.z+6f);
            var left = new Vector2(-nose.y,nose.x);
            front += left*2.5f;
            station = new Vector3(front.x,0,front.y); waiting = station+new Vector3(left.x,0,left.y)*6f;
            facing = new Vector3(-nose.x,0,-nose.y); Phase = MarshallerPhase.WalkingIn;
        }
        internal void Step(float dt)
        {
            time += dt;
            bool valid = Flight.aircraft != null && Flight.aircraft.isActiveAndEnabled && airport.LayoutVersion == layoutVersion &&
                airport.Taxiways.stands.Contains(stand) && Flight.Route?.stand?.stand == stand;
            if (!valid && Phase != MarshallerPhase.WalkingOut) Phase = MarshallerPhase.WalkingOut;
            if (Phase == MarshallerPhase.WalkingIn)
            {
                walked = Mathf.MoveTowards(walked,1,dt*1.3f/6f);
                if (walked >= 1) Phase = MarshallerPhase.Guiding;
                return;
            }
            if (Phase == MarshallerPhase.WalkingOut)
            { walked = Mathf.MoveTowards(walked,0,dt*1.3f/6f); if (walked <= 0) Phase = MarshallerPhase.Complete; return; }
            if (Flight.Phase == FlightPhase.Parked || Phase == MarshallerPhase.Stopped)
            {
                Phase = MarshallerPhase.Stopped; stopped += dt;
                SetSignal(stopped < 3f ? MarshallingSignal.Stop : MarshallingSignal.Clear);
                if (stopped > 6f) Phase = MarshallerPhase.WalkingOut;
                return;
            }
            if (Flight.Phase != FlightPhase.Taxi) { Phase = MarshallerPhase.WalkingOut; return; }
            if (Remaining < .3f) { SetSignal(MarshallingSignal.Stop); return; }
            if (Flight.Waiting != null) { SetSignal(MarshallingSignal.Hold); return; }
            Flight.Route.path.Pose(Mathf.Min(Flight.Along+5f,Flight.Route.path.Length),out _,out var desired,out _,out _);
            var forward = airport.transform.InverseTransformDirection(Flight.aircraft.transform.forward);
            float angle = Vector2.SignedAngle(new Vector2(forward.x,forward.z),desired);
            SetSignal(Mathf.Abs(angle) > 5f ? angle > 0 ? MarshallingSignal.Left : MarshallingSignal.Right :
                Remaining < 14f ? MarshallingSignal.Slow : MarshallingSignal.Ahead);
        }
        void SetSignal(MarshallingSignal signal)
        { if (Signal == signal) return; PreviousSignal = Signal; Signal = signal; signalSince = time; }
        public RampCrewPose Pose
        {
            get
            {
                var p = RampCrewPose.Standing(Position,facing);
                if (Phase == MarshallerPhase.WalkingIn || Phase == MarshallerPhase.WalkingOut)
                { p.rotation = Quaternion.LookRotation((station-waiting).normalized*(Phase == MarshallerPhase.WalkingIn ? 1 : -1)); p.stride = .8f; p.phase = time*1.5f; return p; }
                p.hands = true;
                Arms(Signal,time,out var left,out var right); Arms(PreviousSignal,time,out var oldLeft,out var oldRight);
                p.leftHand = Vector3.Lerp(oldLeft,left,SignalBlend); p.rightHand = Vector3.Lerp(oldRight,right,SignalBlend);
                return p;
            }
        }
        static void Arms(MarshallingSignal signal,float t,out Vector3 left,out Vector3 right)
        {
            float wave = Mathf.Sin(t*Mathf.PI*1.3f);
            left = new Vector3(-.35f,1.44f+wave*.20f,.20f); right = new Vector3(.35f,left.y,.20f);
            switch (signal)
            {
                case MarshallingSignal.Identify: left = new Vector3(-.17f,1.86f,.02f); right = new Vector3(.17f,1.86f,.02f); break;
                case MarshallingSignal.Left: right = new Vector3(.72f,1.38f,.04f); break;
                case MarshallingSignal.Right: left = new Vector3(-.72f,1.38f,.04f); break;
                case MarshallingSignal.Slow: left = new Vector3(-.48f,1.12f+wave*.12f,.12f); right = new Vector3(.48f,left.y,.12f); break;
                case MarshallingSignal.Hold:
                case MarshallingSignal.Stop: left = new Vector3(.10f,1.80f,.08f); right = new Vector3(-.10f,1.80f,.13f); break;
                case MarshallingSignal.Clear: left = new Vector3(-.24f,.86f,.04f); right = new Vector3(.13f,1.69f,.18f); break;
            }
        }
        public Vector3 WandDirection(bool left)
        {
            if (Signal == MarshallingSignal.Stop || Signal == MarshallingSignal.Hold)
                return Pose.rotation*new Vector3(left ? -.65f : .65f,.76f,0);
            if (Signal == MarshallingSignal.Left && !left || Signal == MarshallingSignal.Right && left)
                return Pose.rotation*(left ? Vector3.left : Vector3.right);
            return Vector3.up;
        }
    }
}
