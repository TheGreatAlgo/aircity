using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Pooled bag art follows game-time handovers. The baggage ledger remains the owner of every bag.</summary>
    public sealed class BaggageLoadVisuals
    {
        readonly Transform root;
        readonly GameObject[] prefabs;
        readonly List<Transform> bags = new List<Transform>();
        BaggageTurnaround lastJob;
        int used;
        public BaggageLoadVisuals(Transform root, GameObject[] prefabs) { this.root = root; this.prefabs = prefabs; }

        void Show(Vector3 position, Quaternion rotation)
        {
            if (prefabs == null || prefabs.Length == 0 || used >= 72) return;
            if (used == bags.Count)
            {
                var prefab = prefabs[used % prefabs.Length];
                if (prefab == null) return;
                bags.Add(Object.Instantiate(prefab, root).transform);
            }
            var bag = bags[used++]; bag.gameObject.SetActive(true);
            // Lay a suitcase flat with its lowest point on the conveyor/floor; origins differ between props.
            var mesh = bag.GetComponent<MeshFilter>().sharedMesh.bounds;
            var localRotation = rotation * Quaternion.Euler(90f, 0f, 0f);
            bag.localRotation = localRotation;
            bag.localPosition = position + Vector3.up * mesh.extents.z - localRotation * mesh.center;
        }

        public void Present(BaggageFleet fleet)
        {
            used = 0;
            if (fleet.Job != null) lastJob = fleet.Job;
            var job = fleet.Job ?? lastJob;
            int holdCount = job?.Hold.Count ?? 0, cartCount = fleet.Cart.Count;
            if (job?.TransferBatch != null)
            {
                bool loading = job.TransferTo == job.Hold;
                int started = 0, arrived = 0;
                float travel = job.BagTravelSeconds, interval = fleet.Station.secondsPerBag;
                var facing = Quaternion.LookRotation(job.Plan.inward);
                for (int i = 0; i < job.TransferBatch.Count; i++)
                {
                    float elapsed = job.TransferSeconds - i * interval;
                    if (elapsed < 0f) break;
                    started++;
                    if (elapsed >= travel) { arrived++; continue; }
                    Show(BaggageLoadPath.At(fleet, job.Plan, elapsed / travel, loading), facing);
                }
                if (loading) { cartCount -= started; holdCount += arrived; }
                else { holdCount -= started; cartCount += arrived; }
            }
            var cart = fleet.Equipment.Cart.baggage;
            int columns = Mathf.Max(1, Mathf.FloorToInt((cart.loadMax.x - cart.loadMin.x) / 0.5f));
            int rows = Mathf.Max(1, Mathf.FloorToInt((cart.loadMax.z - cart.loadMin.z) / 0.8f));
            for (int i = 0; i < Mathf.Min(24, cartCount); i++)
            {
                var local = new Vector3(cart.loadMin.x + 0.25f + i % columns * 0.5f,
                    cart.loadMin.y + i / (columns * rows) * 0.31f, cart.loadMin.z + 0.4f + i / columns % rows * 0.8f);
                var p = fleet.Train.Cart.Point(local);
                Show(new Vector3(p.x, local.y, p.y), Quaternion.LookRotation(new Vector3(fleet.Train.Cart.facing.x, 0, fleet.Train.Cart.facing.y)));
            }
            if (job?.Plan != null && job.Aircraft != null && job.Aircraft.CargoDoors.IsOpen(job.HoldConfiguration.cargoPoint))
            {
                job.HoldConfiguration.aircraft.TryGetPoint(job.HoldConfiguration.cargoPoint, out var point);
                int across = Mathf.Max(1, Mathf.FloorToInt(point.size.x / 0.5f));
                float depth = BaggageLoadPath.HoldDepth(job);
                int rowsInHold = Mathf.Max(1, Mathf.FloorToInt((depth - 0.08f) / 0.6f));
                var side = Vector3.Cross(Vector3.up, job.Plan.inward);
                for (int i = 0; i < Mathf.Min(across * rowsInHold * 3, holdCount); i++)
                {
                    var p = job.Plan.door + job.Plan.inward * (0.32f + i / across % rowsInHold * 0.6f) +
                        side * ((i % across - (across - 1) * 0.5f) * 0.5f) + Vector3.up * (i / (across * rowsInHold) * 0.28f);
                    Show(p, Quaternion.LookRotation(job.Plan.inward));
                }
            }
            for (int i = used; i < bags.Count; i++) bags[i].gameObject.SetActive(false);
        }
    }
}
