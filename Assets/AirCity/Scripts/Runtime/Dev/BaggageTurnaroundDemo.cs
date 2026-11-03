using AirCity.Airfield;
using AirCity.Traffic;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public sealed class BaggageTurnaroundDemo : MonoBehaviour
    {
        public Airport airport;
        public BaggageStation station;
        public Aircraft.Aircraft aircraft;
        public int standId;
        public bool startWithArrival = true, autoDeparture = true;
        public BaggageTurnaround Job { get; private set; }
        public Flight Departure { get; private set; }
        public string Problem { get; private set; }
        public bool ObservedDepartureHold { get; private set; }

        void Start() { if (Job == null) Initialize(); }

        public void Initialize()
        {
            var stand = airport.Taxiways.stands.Find(s => s.id == standId);
            Job = airport.Baggage.Request(aircraft, stand, new BulkHoldConfiguration { aircraft = aircraft.type },
                station, "AC101", "AC102", out var reason);
            if (Job == null) { Problem = reason; Debug.LogError(reason); return; }
            var ledger = airport.Baggage.Ledger;
            for (int i = 0; i < 3; i++) ledger.Add("arrival-" + i, "AC101", 10, 150f, true, Job.Hold);
            for (int i = 0; i < 6; i++) ledger.Add("departure-" + i, "AC102", 10, 150f, true, Job.Fleet.Dispatch);
            if (!Job.Seal(out reason)) { Problem = reason; Debug.LogError(reason); return; }
            if (startWithArrival)
            {
                aircraft.transform.position = airport.transform.TransformPoint(new Vector3(0f, 0f, -5000f));
                if (airport.Traffic.Arrive(aircraft, out reason) == null) { Problem = reason; Debug.LogError(reason); }
            }
        }

        void Update() => Observe();

        public void Observe()
        {
            if (Job == null || Problem != null) return;
            if (autoDeparture && Departure == null && Job.Acquired && airport.Traffic.FlightOf(aircraft) == null)
            {
                Departure = airport.Traffic.Depart(aircraft, out var why);
                if (Departure == null) { Problem = why; Debug.LogError(why); }
            }
            if (Departure != null && Departure.Phase == FlightPhase.Ready && Departure.Waiting?.Contains("baggage") == true)
                ObservedDepartureHold = true;
        }

        void OnGUI()
        {
            DevGui.Begin(); if (!DevGui.Visible) return;
            DevGui.Panel(new Rect(18f, 18f, 380f, 272f));
            GUILayout.BeginArea(new Rect(32f, 30f, 352f, 250f));
            GUILayout.Label("NARROWBODY BAGGAGE TURNAROUND", DevGui.Title);
            if (Problem != null) GUILayout.Label(Problem, DevGui.Label);
            else if (Job != null)
            {
                GUILayout.Label("AC101 arrivals → AC102 departure", DevGui.Bold);
                GUILayout.Label(Job.Waiting ?? Job.State.ToString(), DevGui.Label);
                GUILayout.Label($"Arrivals  {Job.Fleet.Arrivals.Count} / 30 bags", DevGui.Small);
                GUILayout.Label($"Dispatch  {Job.Fleet.Dispatch.Count} bags  ·  Cart  {Job.Fleet.Cart.Count}", DevGui.Small);
                GUILayout.Label($"Hold  {Job.Hold.Count} bags  ·  Airport total  {airport.Baggage.Ledger.Count}", DevGui.Small);
                GUILayout.Label(Departure?.Waiting ?? Departure?.Phase.ToString() ?? "Aircraft arriving / parking", DevGui.Small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(airport.timeScale == 0f ? "Resume" : "Pause")) airport.timeScale = airport.timeScale == 0f ? 12f : 0f;
                foreach (float speed in new[] { 1f, 4f, 12f, 30f }) if (GUILayout.Button($"{speed}×")) airport.timeScale = speed;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(station.accessBlocked ? "Clear access" : "Block access")) station.accessBlocked = !station.accessBlocked;
                if (GUILayout.Button("Cancel handling")) Job.Cancel();
                GUILayout.EndHorizontal();
                GUILayout.Label("Logical cargo hold · opening doors follow later", DevGui.Hint);
                GUILayout.Label("Right-drag orbit · scroll zoom · WASD pan", DevGui.Hint);
            }
            GUILayout.EndArea();
        }
    }
}
