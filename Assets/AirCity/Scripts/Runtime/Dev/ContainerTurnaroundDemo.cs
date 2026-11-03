using AirCity.Airfield;
using AirCity.Traffic;
using AirCity.Vehicles;
using UnityEngine;

namespace AirCity.Dev
{
    public sealed class ContainerTurnaroundDemo : MonoBehaviour
    {
        public Airport airport;
        public ContainerStation station;
        public BaggageStation bulkStation;
        public Aircraft.Aircraft aircraft;
        public int standId;
        public bool autoDeparture = true;
        public ContainerTurnaround Job { get; private set; }
        public BaggageTurnaround Bulk { get; private set; }
        public Flight Departure { get; private set; }
        public string Problem { get; private set; }
        bool bulkClosed;
        void Start() { if (Job == null) Initialize(); }
        public void Initialize()
        {
            var stand = airport.Taxiways.stands.Find(s => s.id == standId);
            Job = airport.Baggage.RequestContainers(aircraft, stand, new ContainerHoldConfiguration { aircraft = aircraft.type },
                station, "CT201", "CT202", out var why);
            if (Job == null) { Problem = why; return; }
            var containers = airport.Baggage.Containers; var ledger = airport.Baggage.Ledger;
            var makeUp = ledger.Store("Review make-up", 1000, 15000f);
            void Pack(string id, string flight, int count, ContainerBay bay)
            {
                var unit = containers.Create(id, flight, station.unitType, 100, bay);
                for (int i = 0; i < count; i += 10)
                {
                    int bags = Mathf.Min(10, count - i);
                    var batch = ledger.Add(id + "-" + i, flight, bags, bags * 15f, true, makeUp);
                    if (!containers.Pack(unit, batch, makeUp, out var problem)) { Problem = problem; return; }
                }
                unit.Seal();
            }
            for (int i = 0; i < 2; i++) Pack("AKE-IN-" + i, "CT201", 60, Job.Hold);
            for (int i = 0; i < 3; i++) Pack("AKE-OUT-" + i, "CT202", 70, Job.Fleet.Dispatch);
            if (!Job.Seal(out why)) { Problem = why; return; }
            if (bulkStation != null)
            {
                Bulk = airport.Baggage.Request(aircraft, stand,
                    new BulkHoldConfiguration { aircraft = aircraft.type, cargoPoint = "Cargo_Bulk" }, bulkStation, "CT201", "CT202", out why);
                if (Bulk == null) { Problem = why; return; }
                for (int i = 0; i < 2; i++) ledger.Add("bulk-in-" + i, "CT201", 10, 150f, true, Bulk.Hold);
                for (int i = 0; i < 3; i++) ledger.Add("bulk-out-" + i, "CT202", 10, 150f, true, Bulk.Fleet.Dispatch);
            }
        }
        void Update() => Observe();
        public void Observe()
        {
            if (Job == null || Problem != null) return;
            if (Job.DepartureReady && Bulk != null && !bulkClosed)
            { bulkClosed = Bulk.Seal(out var why); if (!bulkClosed) Problem = why; }
            if (autoDeparture && Job.Acquired && Departure == null)
            { Departure = airport.Traffic.Depart(aircraft, out var why); if (Departure == null) Problem = why; }
        }
        void OnGUI()
        {
            DevGui.Begin(); if (!DevGui.Visible) return;
            DevGui.Panel(new Rect(18, 18, 430, 300)); GUILayout.BeginArea(new Rect(30, 28, 405, 278));
            GUILayout.Label("WIDEBODY CONTAINER TURNAROUND", DevGui.Title);
            if (Problem != null) GUILayout.Label(Problem, DevGui.Label);
            else if (Job != null)
            {
                GUILayout.Label(Job.Waiting ?? Job.State.ToString(), DevGui.Bold);
                GUILayout.Label($"ULDs: {Job.Unloaded}/2 unloaded · {Job.Loaded}/3 loaded", DevGui.Small);
                GUILayout.Label($"Arrivals: {Job.Fleet.ArrivalsBags.Count} bags · Airport total: {airport.Baggage.Ledger.Count}", DevGui.Small);
                GUILayout.Label("Bulk hold: " + (Bulk?.Waiting ?? Bulk?.State.ToString() ?? "none"), DevGui.Small);
                GUILayout.Label(Departure?.Status ?? "Aircraft parked", DevGui.Small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(airport.timeScale == 0 ? "Resume" : "Pause")) airport.timeScale = airport.timeScale == 0 ? 12 : 0;
                foreach (float speed in new[] { 1f, 4f, 12f, 30f }) if (GUILayout.Button(speed + "×")) airport.timeScale = speed;
                GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
                if (GUILayout.Button(station.accessBlocked ? "Clear access" : "Block access")) station.accessBlocked = !station.accessBlocked;
                if (GUILayout.Button("Cancel")) Job.Cancel();
                if (Job.State == ContainerTurnaroundState.Cancelled && GUILayout.Button("Retry")) Job.Retry(out _);
                GUILayout.EndHorizontal();
                GUILayout.Label("Cargo hatches close before departure", DevGui.Hint);
            }
            GUILayout.EndArea();
        }
    }
}
