using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>One career slot, atomic replacement and a previous good backup. Never regenerate a saved landscape.</summary>
    public sealed class CareerSaveStore
    {
        readonly string path;
        readonly object queueGate = new object(), diskGate = new object();
        Task worker;
        PendingSave pending;
        string saveProblem;
        long nextRevision, committedRevision;
        sealed class PendingSave { public CareerState state; public long revision; }
        public CareerSaveStore(string directory) { path = Path.Combine(directory, "career.json"); }
        public bool Exists => File.Exists(path) || File.Exists(path + ".backup");

        public bool Save(CareerState state, out string problem)
        {
            FlushPending();
            long revision;
            lock (queueGate) revision = ++nextRevision;
            bool success = SaveSnapshot(state, revision, out problem);
            if (success) lock (queueGate) saveProblem = null;
            return success;
        }

        /// <summary>Call on the owning game thread. Only snapshot copying happens here, never JSON or disk IO.</summary>
        public bool QueueSave(CareerState state, out string problem)
        {
            if (state == null) { problem = "There is no career to save."; return false; }
            CareerState snapshot;
            try
            {
                snapshot = CareerSnapshot.Capture(state);
                lock (queueGate)
                {
                    // Keep one in-flight snapshot and only the newest waiting snapshot, even during a long write.
                    pending = new PendingSave { state = snapshot, revision = ++nextRevision };
                    if (worker == null) worker = Task.Run(DrainQueue);
                }
                problem = null; return true;
            }
            catch (Exception e) { problem = "Could not queue the career save: " + e.Message; return false; }
        }

        public bool TryTakeSaveProblem(out string problem)
        {
            lock (queueGate) { problem = saveProblem; saveProblem = null; return problem != null; }
        }

        /// <summary>Waits until queued snapshots are durable. Explicit save/load, pause and shutdown use this barrier.</summary>
        public void FlushPending()
        {
            while (true)
            {
                Task writing;
                lock (queueGate) writing = worker;
                if (writing == null) return;
                writing.GetAwaiter().GetResult();
            }
        }

        void DrainQueue()
        {
            while (true)
            {
                PendingSave item;
                lock (queueGate)
                {
                    item = pending; pending = null;
                    if (item == null) { worker = null; return; }
                }
                if (!SaveSnapshot(item.state, item.revision, out string problem))
                    lock (queueGate) saveProblem = problem;
            }
        }

        bool SaveSnapshot(CareerState state, long revision, out string problem)
        {
            try
            {
                if (state == null) { problem = "There is no career to save."; return false; }
                if (!state.Validate(out problem)) return false;
                string json = JsonUtility.ToJson(state);
                lock (diskGate)
                {
                    // Synchronous saves may overtake an older worker that was still serializing.
                    if (revision <= committedRevision) { problem = null; return true; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string temporary = path + ".pending";
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream))
                    { writer.Write(json); writer.Flush(); stream.Flush(true); }
                    if (File.Exists(path)) File.Replace(temporary, path, path + ".backup");
                    else File.Move(temporary, path);
                    committedRevision = revision;
                }
                problem = null; return true;
            }
            catch (Exception e) { problem = "Could not save the career: " + e.Message; return false; }
        }

        public bool Load(out CareerState state, out string problem)
        {
            FlushPending();
            if (Read(path, out state, out problem)) return true;
            string primaryProblem = problem;
            if (Read(path + ".backup", out state, out _))
            { problem = "Recovered the previous save because the latest save could not be read."; return true; }
            problem = primaryProblem; return false;
        }

        static bool Read(string path, out CareerState state, out string problem)
        {
            state = null; problem = "No saved career was found.";
            try
            {
                if (!File.Exists(path)) return false;
                if (new FileInfo(path).Length > 32000000) { problem = "The career save is too large."; return false; }
                var candidate = JsonUtility.FromJson<CareerState>(File.ReadAllText(path));
                if (candidate == null) { problem = "The career save is empty."; return false; }
                if (!candidate.Validate(out problem)) return false;
                state = candidate; return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { problem = "Could not read the career save: " + e.Message; return false; }
        }
    }
}
