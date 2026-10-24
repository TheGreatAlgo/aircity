using System.Collections.Generic;
using AirCity.Airfield;
using AirCity.Airfield.Building;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>
    /// Getting a vehicle from where its path ends to a nearby pose: a plain forward turn (a Dubins curve) where
    /// there's room, else a turn with reversing in it, the way a truck turns round in a narrow street: back into a
    /// side taxiway or onto the runway and drive out the other way (two legs), or drive into one, back out and
    /// drive on (three). The intermediate poses come from the caller (along the taxiways around a junction, in
    /// front of the depot's doors). With a Pavement map the vehicle's footprint has to stay on it: the shortest
    /// manoeuvre that keeps it there wins, a cusp counting as CuspCost metres; if none does, the one that strays
    /// least. Without one (mowers live on the grass) the shortest wins.
    /// </summary>
    public static class VehicleManeuver
    {
        /// <summary>A position and the way the vehicle faces (not necessarily the way it moves).</summary>
        public readonly struct Pose
        {
            public readonly Vector2 p, facing;

            public Pose(Vector2 p, Vector2 facing)
            {
                this.p = p;
                this.facing = facing.sqrMagnitude > 1e-8f ? facing.normalized : Vector2.up;
            }
        }

        public const float CuspCost = 30f;
        const float CheckStep = 2f, Shrink = 0.8f, MaxLength = 500f;

        /// <summary>
        /// Append the best way from the path's end to `goal` (arriving forwards, or backwards if endReverse).
        /// Forward legs run at `speed`, reversing ones at reverseSpeed. Returns the number of cusps (gear
        /// changes) it took, including one at the start if the path ended going the other way.
        /// </summary>
        public static int Append(VehiclePath into, Pose goal, bool endReverse, IReadOnlyList<Pose> via, VehicleType type,
            Pavement pavement, float speed, float reverseSpeed)
        {
            var start = new Pose(into.End, into.EndFacing);
            bool startReverse = into.EndReversing && into.Count > 1;
            float r = type.turnRadius;
            var cands = new List<(float cost, Pose[] poses, int[] gears)>();

            void Try(Pose[] poses, int[] gears)
            {
                float len = 0f;
                for (int i = 0; i < gears.Length; i++)
                {
                    var a = poses[i];
                    var b = poses[i + 1];
                    float l = VehiclePath.DubinsLength(a.p, a.facing * gears[i], b.p, b.facing * gears[i], r);
                    if (float.IsInfinity(l)) return;
                    len += l;
                }
                if (len > MaxLength) return;
                int cusps = gears.Length - 1 + (into.Count > 1 && (gears[0] < 0) != startReverse ? 1 : 0);
                cands.Add((len + cusps * CuspCost, poses, gears));
            }

            int last = endReverse ? -1 : 1;
            Try(new[] { start, goal }, new[] { last });
            foreach (var b in via)
                Try(new[] { start, b, goal }, new[] { -last, last });
            if (!endReverse)
                foreach (var b in via)
                    foreach (var a in via)
                        if (!a.Equals(b))
                            Try(new[] { start, b, a, goal }, new[] { 1, -1, 1 });

            cands.Sort((x, y) => x.cost.CompareTo(y.cost));
            int pick = -1, pickOff = int.MaxValue;
            if (pavement == null) pick = cands.Count > 0 ? 0 : -1;
            else
            {
                var probe = new VehiclePath();
                var poses = new List<(Vector2 p, Vector2 facing)>();
                for (int c = 0; c < cands.Count && c < 400; c++)
                {
                    probe.Clear();
                    probe.Start(start.p, speed);
                    Build(probe, cands[c].poses, cands[c].gears, r, speed, reverseSpeed);
                    poses.Clear();
                    probe.Poses(0f, probe.Length, CheckStep, poses);
                    int off = OffPavement(poses, type, pavement, pickOff);
                    if (off < pickOff)
                    {
                        pickOff = off;
                        pick = c;
                    }
                    if (off == 0) break;
                }
            }
            if (pick < 0)
            {
                into.TurnTo(start.facing, goal.p, goal.facing * last, r, endReverse ? reverseSpeed : speed, endReverse);
                return endReverse != startReverse ? 1 : 0;
            }
            var chosen = cands[pick];
            Build(into, chosen.poses, chosen.gears, r, speed, reverseSpeed);
            return chosen.gears.Length - 1 + (into.Count > 1 && (chosen.gears[0] < 0) != startReverse ? 1 : 0);
        }

        static void Build(VehiclePath into, Pose[] poses, int[] gears, float r, float speed, float reverseSpeed)
        {
            for (int i = 0; i < gears.Length; i++)
            {
                bool rev = gears[i] < 0;
                into.TurnTo(poses[i].facing * gears[i], poses[i + 1].p, poses[i + 1].facing * gears[i], r, rev ? reverseSpeed : speed, rev);
            }
        }

        /// <summary>How many footprint points stray off the pavement over these poses (stops counting at limit).</summary>
        public static int OffPavement(List<(Vector2 p, Vector2 facing)> poses, VehicleType type, Pavement pavement, int limit = int.MaxValue)
        {
            var b = type.bounds;
            float x0 = b.min.x + Shrink, x1 = b.max.x - Shrink, z0 = b.min.z + Shrink, z1 = b.max.z - Shrink, zm = (z0 + z1) * 0.5f;
            int off = 0;
            foreach (var (p, f) in poses)
            {
                var right = Geo.Right(f);
                if (!pavement.Contains(p + right * x0 + f * z0)) off++;
                if (!pavement.Contains(p + right * x1 + f * z0)) off++;
                if (!pavement.Contains(p + right * x0 + f * z1)) off++;
                if (!pavement.Contains(p + right * x1 + f * z1)) off++;
                if (!pavement.Contains(p + right * x0 + f * zm)) off++;
                if (!pavement.Contains(p + right * x1 + f * zm)) off++;
                if (off >= limit) return off;
            }
            return off;
        }

        /// <summary>Poses along the taxiways, roads and runways meeting at the nodes near a point (centred on them,
        /// facing either way), for backing into or driving into to turn round.</summary>
        public static List<Pose> AroundNodes(TaxiwayNetwork net, Vector2 near, float radius, int maxNodes = 3)
        {
            var nodes = new List<TaxiwayNode>(net.nodes);
            nodes.RemoveAll(n => (Geo.XZ(n.position) - near).sqrMagnitude > radius * radius);
            nodes.Sort((a, b) => (Geo.XZ(a.position) - near).sqrMagnitude.CompareTo((Geo.XZ(b.position) - near).sqrMagnitude));
            var poses = new List<Pose>();
            for (int k = 0; k < Mathf.Min(maxNodes, nodes.Count); k++)
            {
                var n = nodes[k];
                var p = Geo.XZ(n.position);
                var dirs = new List<(Vector2 d, float len)>();
                foreach (var e in net.EdgesAt(n.id))
                {
                    var d = net.At(TaxiwayNetwork.Other(e, n.id)) - p;
                    if (d.sqrMagnitude > 1f) dirs.Add((d.normalized, d.magnitude));
                }
                if (n.runway != null)
                {
                    n.runway.Locate(n.position, out float s, out _);
                    dirs.Add((n.runway.Axis2D, n.runway.Length - s));
                    dirs.Add((-n.runway.Axis2D, s));
                }
                foreach (var (d, len) in dirs)
                    foreach (float dist in new[] { 10f, 18f, 28f })
                    {
                        if (dist > len * 0.8f) continue;
                        poses.Add(new Pose(p + d * dist, d));
                        poses.Add(new Pose(p + d * dist, -d));
                    }
            }
            return poses;
        }
    }
}
