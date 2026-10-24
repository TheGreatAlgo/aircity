using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// One piece of work for a team: a runway, or a run of taxiway / road sections worked end to end in one go
    /// (TaxiwayNetwork.Run), or the verges of either. Each is a strip along a centreline (straight for a runway, a
    /// polyline for a run) with s along it from its start and x across to the right; for verges x is measured from
    /// the pavement edge outwards (the pavement taken out, as the verge meshes do), so StripWork's pattern applies
    /// unchanged. Positions are in the Airport's space.
    /// </summary>
    public sealed class MaintenanceJob
    {
        public readonly WorkKind kind;
        public readonly Runway runway;
        /// <summary>A run's sections in order, and whether the run goes along each from its node a to b.</summary>
        public readonly List<TaxiwayEdge> edges;
        readonly List<bool> forward;
        /// <summary>The grass beside the runway / run, not the surface itself (mowing only).</summary>
        public readonly bool verge;
        readonly TaxiwayNetwork net;
        public StripWork work;
        StripWork[] views;

        readonly List<Vector2> line = new List<Vector2>();
        readonly List<float> along = new List<float>();

        public MaintenanceJob(WorkKind kind, Runway runway, bool verge = false)
        {
            this.kind = kind;
            this.runway = runway;
            this.verge = verge;
        }

        public MaintenanceJob(WorkKind kind, TaxiwayNetwork net, List<(TaxiwayEdge edge, bool forward)> run, bool verge = false)
        {
            this.kind = kind;
            this.net = net;
            this.verge = verge;
            edges = new List<TaxiwayEdge>();
            forward = new List<bool>();
            foreach (var (e, f) in run)
            {
                edges.Add(e);
                forward.Add(f);
            }
        }

        public bool IsRunway => runway != null;

        public bool Valid
        {
            get
            {
                if (runway != null) return runway.isActiveAndEnabled;
                if (net == null || edges.Count == 0) return false;
                foreach (var e in edges)
                    if (!net.edges.Contains(e) || net.Node(e.a) == null || net.Node(e.b) == null) return false;
                return true;
            }
        }

        // ------------------------------------------------------------------ frame

        /// <summary>The centreline from the start (runway start threshold; the run's first node).</summary>
        public IReadOnlyList<Vector2> Line
        {
            get
            {
                Refresh();
                return line;
            }
        }

        void Refresh()
        {
            line.Clear();
            along.Clear();
            if (runway != null)
            {
                line.Add(Geo.XZ(runway.start));
                line.Add(Geo.XZ(runway.end));
            }
            else
            {
                line.Add(net.At(forward[0] ? edges[0].a : edges[0].b));
                for (int i = 0; i < edges.Count; i++) line.Add(net.At(forward[i] ? edges[i].b : edges[i].a));
            }
            along.Add(0f);
            for (int i = 1; i < line.Count; i++) along.Add(along[i - 1] + Vector2.Distance(line[i - 1], line[i]));
        }

        public Vector2 Origin => Line[0];
        public float Length
        {
            get
            {
                Refresh();
                return along[along.Count - 1];
            }
        }

        /// <summary>The strip's direction at its start.</summary>
        public Vector2 Axis => Direction(0f);

        float PavedHalfWidth => runway != null ? runway.HalfWidthTotal : edges[0].width * 0.5f;

        /// <summary>Where x = 0 lies from the centreline: the pavement edge for verges, else the centreline.</summary>
        public float Gap => verge ? PavedHalfWidth : 0f;

        /// <summary>Half the width to cover, in strip coordinates.</summary>
        public float HalfWidth => !verge ? PavedHalfWidth
            : (runway != null ? runway.VergeHalfWidth : edges[0].VergeHalfWidth) - PavedHalfWidth;

        /// <summary>How far beyond the ends the team works (over a runway's blast pads; a verge's overrun).</summary>
        public float Margin => runway != null ? (verge ? runway.VergeOverrun : runway.WorkMargin)
            : verge ? 2f : TaxiwayNetwork.WorkMargin;

        /// <summary>Corner radius of the run's centreline (its curves), for the passes.</summary>
        public float CornerRadius => runway != null ? 0f : RunwayRules.TaxiwayTurnRadius(edges[0].width);

        /// <summary>Strip x (across, from the pavement edge for verges) to metres from the centreline.</summary>
        public float Across(float x) => verge ? x + Mathf.Sign(x) * Gap : x;

        /// <summary>The centreline's direction at s (clamped).</summary>
        public Vector2 Direction(float s)
        {
            Refresh();
            int i = SegmentAt(s);
            var d = line[i + 1] - line[i];
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
        }

        int SegmentAt(float s)
        {
            for (int i = 0; i < along.Count - 2; i++)
                if (s < along[i + 1]) return i;
            return along.Count - 2;
        }

        /// <summary>Strip point (s along, x across) in the Airport's ground plane; beyond the ends it carries on
        /// straight.</summary>
        public Vector2 At(float s, float x)
        {
            Refresh();
            int i = SegmentAt(s);
            var d = Direction(s);
            return line[i] + d * (s - along[i]) + Geo.Right(d) * Across(x);
        }

        /// <summary>Distance along the centreline of the point nearest p.</summary>
        public float Locate(Vector2 p)
        {
            Refresh();
            float best = float.MaxValue, bestS = 0f;
            for (int i = 0; i < line.Count - 1; i++)
            {
                var c = Geo.ClosestOnSegment(p, line[i], line[i + 1], out float t);
                float d = (c - p).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestS = Mathf.Lerp(along[i], along[i + 1], t);
                }
            }
            return bestS;
        }

        /// <summary>
        /// The lane x of the strip from s0 to s1 (s1 &lt; s0: driven backwards), as points: the centreline offset by
        /// x with mitred corners (rounded later by the path), carried on straight beyond the ends.
        /// </summary>
        public List<Vector2> Lane(float x, float s0, float s1)
        {
            Refresh();
            float off = Across(x);
            var pts = new List<Vector2> { At(s0, x) };
            bool up = s1 >= s0;
            for (int k = 1; k < line.Count - 1; k++)
            {
                int i = up ? k : line.Count - 1 - k;
                float s = along[i];
                if (up ? s <= s0 || s >= s1 : s >= s0 || s <= s1) continue;
                var n0 = Geo.Right((line[i] - line[i - 1]).normalized);
                var n1 = Geo.Right((line[i + 1] - line[i]).normalized);
                var m = (n0 + n1);
                float dot = m.sqrMagnitude > 1e-6f ? Vector2.Dot(m.normalized, n0) : 1f;
                pts.Add(line[i] + (m.sqrMagnitude > 1e-6f ? m.normalized : n0) * (off / Mathf.Max(dot, 0.35f)));
            }
            pts.Add(At(s1, x));
            return pts;
        }

        /// <summary>The nodes at the run's two ends (start, end); -1 for runways.</summary>
        public (int start, int end) EndNodes => runway != null ? (-1, -1)
            : (forward[0] ? edges[0].a : edges[0].b, forward[edges.Count - 1] ? edges[edges.Count - 1].b : edges[edges.Count - 1].a);

        // ------------------------------------------------------------------ state

        /// <summary>Still worth doing (it may have been done meanwhile, or the weather changed).</summary>
        public bool Needed
        {
            get
            {
                if (kind == WorkKind.Plough) return Snow > 0.05f;
                return Grass > RunwayRules.CutGrass + 0.5f;
            }
        }

        /// <summary>Lying snow (the deepest section of a run).</summary>
        public float Snow
        {
            get
            {
                if (runway != null) return runway.snow;
                float s = 0f;
                foreach (var e in edges) s = Mathf.Max(s, e.snow);
                return s;
            }
        }

        /// <summary>The grass to cut (the longest of a run's sections, or their verges).</summary>
        public float Grass
        {
            get
            {
                if (runway != null) return verge ? runway.vergeGrass : runway.grass;
                float g = 0f;
                foreach (var e in edges) g = Mathf.Max(g, verge ? e.vergeGrass : e.grass);
                return g;
            }
        }

        public PavementSurface Surface => runway != null ? runway.surface : edges[0].surface;

        /// <summary>Is a team already on any of it?</summary>
        public bool Taken
        {
            get
            {
                if (runway != null) return (verge ? runway.VergeWork : runway.Work) != null;
                foreach (var e in edges)
                    if ((verge ? e.vergeWork : e.work) != null) return true;
                return false;
            }
        }

        public void Assign(StripWork w)
        {
            work = w;
            if (runway != null)
            {
                runway.Assign(w, verge);
                return;
            }
            Refresh();
            views = new StripWork[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                float len = along[i + 1] - along[i];
                views[i] = new StripWork(w.kind, len, w.halfWidth, w.margin, w.laneWidth, w.vehicles, w.reversed ^ !forward[i]);
                net.Assign(edges[i], views[i], verge);
            }
        }

        /// <summary>The lead is on pass `pass`, at s = front along the whole strip: each section of a run shows it
        /// in its own frame, and closes once the team reaches it.</summary>
        public void Advance(int pass, float front)
        {
            work.Advance(pass, front);
            if (views == null) return;
            int dir = work.Direction(pass);
            for (int i = 0; i < views.Length; i++)
            {
                float len = along[i + 1] - along[i];
                float local = front - along[i];
                views[i].Show(pass, forward[i] ? local : len - local);
                bool reached = pass > 0 || (dir > 0 ? front >= along[i] - work.margin : front <= along[i + 1] + work.margin);
                if (reached && !views[i].Entered)
                {
                    views[i].Enter();
                    net.RefreshState();
                }
            }
        }

        public void Release(bool done)
        {
            if (work == null) return;
            if (done) work.Finish();
            if (runway != null)
            {
                runway.Release(work, done, verge);
                return;
            }
            if (views == null) return;
            for (int i = 0; i < edges.Count; i++)
            {
                if (done) views[i].Finish();
                net.Release(edges[i], views[i], done, verge);
            }
        }

        /// <summary>Does this job share anything with another of the same kind (the same runway, or a section)?</summary>
        public bool Overlaps(MaintenanceJob other)
        {
            if (other == null || other.kind != kind || other.verge != verge) return false;
            if (runway != null || other.runway != null) return runway == other.runway;
            foreach (var e in edges)
                if (other.edges.Contains(e)) return true;
            return false;
        }

        public bool Contains(TaxiwayEdge e) => edges != null && edges.Contains(e);

        /// <summary>A fresh copy (to queue again after a team gave it up).</summary>
        public MaintenanceJob Fresh()
        {
            if (runway != null) return new MaintenanceJob(kind, runway, verge);
            var run = new List<(TaxiwayEdge, bool)>();
            for (int i = 0; i < edges.Count; i++) run.Add((edges[i], forward[i]));
            return new MaintenanceJob(kind, net, run, verge);
        }

        public override string ToString()
        {
            string what = runway != null ? "runway " + runway.Designation
                : $"{(edges[0].road ? "road" : "taxiway")} {EndNodes.start}-{EndNodes.end}" + (edges.Count > 1 ? $" ({edges.Count} sections)" : "");
            return verge ? what + " verges" : what;
        }
    }
}
