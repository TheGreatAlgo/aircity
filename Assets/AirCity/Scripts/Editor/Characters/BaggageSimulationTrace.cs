using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AirCity.Dev;
using AirCity.EditorTools.Vehicles;
using AirCity.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.EditorTools.Characters
{
    /// <summary>
    /// Deterministic headless trace of baggage turnarounds: every state, transfer, crew interlock and ledger change with
    /// its game time, plus vehicle poses once a game second. Used to prove that presentation work leaves the simulation
    /// untouched: run it before and after and diff the files.
    /// <code>Unity -batchmode -projectPath . -executeMethod AirCity.EditorTools.Characters.BaggageSimulationTrace.FromCommandLine
    ///   -traceOut Logs/baggage-trace.txt -logFile Logs/trace.log</code>
    /// </summary>
    public static class BaggageSimulationTrace
    {
        public static void FromCommandLine()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                string output = "Logs/baggage-trace.txt";
                for (int i = 0; i < args.Length-1; i++) if (args[i] == "-traceOut") output = args[i+1];
                var text = new StringBuilder();
                foreach (bool present in new[] { false, true })
                {
                    Turnaround(text,present,1f/60f,false,false);
                    Turnaround(text,present,.25f,false,false);
                    Turnaround(text,present,1f/60f,true,false);
                    Turnaround(text,present,1f/60f,false,true);
                    Sandbox(text,present,1f/60f);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllText(output,text.ToString());
                Debug.Log("Baggage trace written to "+output);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static string F(double v) => v.ToString("R",CultureInfo.InvariantCulture);
        static string F(float v) => v.ToString("R",CultureInfo.InvariantCulture);

        static string Signature(BaggageTurnaround job)
        {
            var fleet = job.Fleet; var crew = fleet.Crew; var s = new StringBuilder();
            s.Append(job.State).Append(" | ").Append(job.Waiting).Append(" | ").Append(job.TransferBatch?.Id).Append(' ')
                .Append(job.TransferFrom?.Name).Append("->").Append(job.TransferTo?.Name).Append(" | cart ").Append(fleet.ActiveCart)
                .Append(" | crew G").Append(crew.GroundSeated ? 's' : '-').Append(crew.GroundReady ? 'r' : '-')
                .Append(" H").Append(crew.HoldSeated ? 's' : '-').Append(crew.HoldReady ? 'r' : '-')
                .Append(" | hold ").Append(job.Hold.Count).Append(" carts");
            foreach (var c in fleet.Carts) s.Append(' ').Append(c.Count);
            s.Append(" dispatch ").Append(fleet.Dispatch.Count).Append(" arrivals ").Append(fleet.Arrivals.Count)
                .Append(" | workers ").Append(fleet.Workers).Append(" doors ").Append(job.Aircraft != null && job.Aircraft.CargoDoors.IsOpen(job.HoldConfiguration.cargoPoint));
            return s.ToString();
        }
        static string Detail(BaggageTurnaround job)
        {
            var fleet = job.Fleet; var crew = fleet.Crew;
            return $"  tractor {F(fleet.Train.Tractor.position.x)},{F(fleet.Train.Tractor.position.y)} dist {F(fleet.Train.Distance)} loader {F(fleet.LoaderPose.position.x)},{F(fleet.LoaderPose.position.y)} h {F(fleet.LoaderHeight)} " +
                $"g {F(crew.GroundProgress)} hp {F(crew.HoldProgress)} ts {F(job.TransferSeconds)} int {F(job.TransferInterval)} trav {F(job.BagTravelSeconds)}";
        }

        static void Run(StringBuilder text, string name, Func<BaggageTurnaround> job, Action<double> step, Action<double,BaggageTurnaround> events, int limit)
        {
            text.Append("=== ").Append(name).Append('\n');
            string last = null; double time = 0; int nextDetail = 0;
            for (int i = 0; i < limit; i++)
            {
                var j = job();
                if (j == null) { step(time); continue; }
                string sig = Signature(j);
                if (sig != last) { text.Append(time.ToString("0.0000",CultureInfo.InvariantCulture)).Append(' ').Append(sig).Append('\n').Append(Detail(j)).Append('\n'); last = sig; }
                if (time >= nextDetail) { text.Append(time.ToString("0.0000",CultureInfo.InvariantCulture)).Append(Detail(j)).Append('\n'); nextDetail++; }
                if (j.Finished) { text.Append("finished at ").Append(time.ToString("0.0000",CultureInfo.InvariantCulture)).Append(" state ").Append(j.State).Append('\n'); return; }
                events?.Invoke(time,j);
                time += Step(step,time);
            }
            text.Append("did not finish\n");
        }
        static double stepSize;
        static double Step(Action<double> step, double time) { step(time); return stepSize; }

        static void Turnaround(StringBuilder text, bool present, float dt, bool blockAndCancel, bool arrive)
        {
            EditorSceneManager.OpenScene(BaggageTurnaroundSetup.ScenePath);
            var demo = Object.FindAnyObjectByType<BaggageTurnaroundDemo>();
            demo.startWithArrival = arrive; demo.autoDeparture = arrive; demo.Initialize();
            demo.airport.timeScale = 1f; stepSize = dt;
            bool blocked = false, unblocked = false, cancelled = false, retried = false; double blockedAt = 0;
            Run(text,$"turnaround present={present} dt={dt} blockCancel={blockAndCancel} arrive={arrive}",() => demo.Job,
                _ => { demo.airport.Simulate(dt); demo.Observe(); if (present) demo.station.Present(dt); },
                blockAndCancel ? (time,job) =>
                {
                    if (!blocked && job.State == BaggageTurnaroundState.UnloadingHold && job.TransferSeconds > .4f)
                    { demo.station.accessBlocked = true; blocked = true; blockedAt = time; text.Append("block\n"); }
                    if (blocked && !unblocked && time > blockedAt+3) { demo.station.accessBlocked = false; unblocked = true; text.Append("unblock\n"); }
                    if (unblocked && !cancelled && job.State == BaggageTurnaroundState.LoadingHold && job.TransferSeconds > 1f)
                    { job.Cancel(); cancelled = true; text.Append("cancel\n"); }
                    if (cancelled && !retried && job.State == BaggageTurnaroundState.Cancelled)
                    { retried = job.Retry(out var why); text.Append("retry ").Append(retried).Append(why).Append('\n'); }
                } : null,
                Mathf.CeilToInt(2400/dt));
            text.Append("ledger ").Append(demo.airport.Baggage.Ledger.Count).Append(" transfers ").Append(demo.airport.Baggage.Ledger.Transfers).Append(" departureReady ").Append(demo.Job.DepartureReady).Append('\n');
        }

        static void Sandbox(StringBuilder text, bool present, float dt)
        {
            EditorSceneManager.OpenScene("Assets/AirCity/Scenes/Sandbox.unity");
            var depot = Object.FindAnyObjectByType<BaggageDepot>();
            var airport = depot.Airport; var station = depot.Teams.Single();
            station.incomingBags = station.outgoingBags = 40;
            airport.timeScale = 1f; stepSize = dt;
            Run(text,$"sandbox present={present} dt={dt}",() => airport.Baggage.Jobs.Count > 0 ? airport.Baggage.Jobs[0] : null,
                _ => { airport.Simulate(dt); if (present) { station.Present(dt); depot.Present(); } }, null, Mathf.CeilToInt(2400/dt));
            text.Append("ledger ").Append(airport.Baggage.Ledger.Count).Append(" transfers ").Append(airport.Baggage.Ledger.Transfers).Append('\n');
        }
    }
}
