using System.Collections.Generic;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Pooled bag art follows game-time handovers. The baggage ledger remains the owner of every bag.</summary>
    public sealed class BaggageLoadVisuals
    {
        readonly Transform root;
        readonly GameObject[] prefabs;
        readonly List<Transform>[] pools;
        readonly int[] counts;
        BaggageTurnaround lastJob;
        int used;
        public BaggageLoadVisuals(Transform root, GameObject[] prefabs)
        { this.root = root; this.prefabs = prefabs; pools = new List<Transform>[prefabs?.Length ?? 0]; counts = new int[pools.Length];
            for (int i = 0; i < pools.Length; i++) pools[i] = new List<Transform>(); }

        void Show(Vector3 position, Quaternion rotation, RampCrewRig holder = null, int variant = -1)
        {
            if (prefabs == null || prefabs.Length == 0) return;
            int bucket = variant >= 0 ? variant%pools.Length : used%pools.Length;
            var pool = pools[bucket]; int at = counts[bucket];
            if (at == pool.Count)
            {
                if (prefabs[bucket] == null) return;
                pool.Add(Object.Instantiate(prefabs[bucket],root).transform);
            }
            var bag = pool[at]; counts[bucket]++; used++; bag.gameObject.SetActive(true);
            // Lay a suitcase flat with its lowest point on the conveyor/floor; origins differ between props.
            var mesh = bag.GetComponent<MeshFilter>().sharedMesh.bounds;
            var localRotation = rotation * Quaternion.Euler(90f, 0f, 0f);
            if (holder != null)
            {
                holder.Grip(position,rotation); bag.SetParent(holder.CarrySocket,false);
                bag.localRotation = Quaternion.Euler(90f,0f,0f);
                bag.localPosition = Vector3.up * mesh.extents.z - bag.localRotation * mesh.center;
            }
            else
            {
                bag.SetParent(root,false); bag.localRotation = localRotation;
                bag.localPosition = position + Vector3.up * mesh.extents.z - localRotation * mesh.center;
            }
        }

        static int Variant(BaggageBatch batch,int bag) => BaggageHoldPacking.Variant(batch,bag);

        public void Present(BaggageFleet fleet, RampCrewRig ground = null, RampCrewRig hold = null,
            IReadOnlyList<RampCrewRig> groundWorkers=null,IReadOnlyList<RampCrewRig> holdWorkers=null)
        {
            used = 0; System.Array.Clear(counts,0,counts.Length);
            if (fleet.Job != null) lastJob = fleet.Job;
            var job = fleet.Job ?? lastJob;
            int holdCount = job?.Hold.Count ?? 0, cartCount = fleet.Cart.Count;
            if (job?.TransferBatch != null)
            {
                bool loading = job.TransferTo == job.Hold;
                int started = 0, arrived = 0;
                float travel = job.BagTravelSeconds, interval = job.TransferInterval;
                for (int i = 0; i < job.TransferBatch.Count; i++)
                {
                    float elapsed = job.TransferSeconds - i * interval;
                    if (elapsed < 0f) break;
                    started++;
                    if (elapsed >= travel) { arrived++; continue; }
                    var pose = job.DepotTransfer ? fleet.Station.depot.BagAt(fleet,elapsed,i) : fleet.Crew.BagAt(elapsed,loading,i);
                    var workers=pose.holder==0 ? groundWorkers : holdWorkers;
                    var holder=pose.holder<0 ? null : workers!=null && workers.Count>pose.worker ? workers[pose.worker] : pose.holder==0 ? ground : hold;
                    Show(pose.bottom,pose.rotation,holder,Variant(job.TransferBatch,i));
                }
                if (job.DepotTransfer) cartCount+=fleet.IsCart(job.TransferFrom) ? -started : arrived;
                else if (loading) { cartCount -= started; holdCount += arrived; }
                else { holdCount -= started; cartCount += arrived; }
            }
            var cart = fleet.Equipment.Cart.baggage;
            int columns = Mathf.Max(1, Mathf.FloorToInt((cart.loadMax.x - cart.loadMin.x) / 0.5f));
            int rows = Mathf.Max(1, Mathf.FloorToInt((cart.loadMax.z - cart.loadMin.z) / 0.8f));
            for (int cartIndex = 0; cartIndex < fleet.Carts.Length; cartIndex++)
            {
                var pose = fleet.Train.CartAt(cartIndex); int count = cartIndex == fleet.ActiveCart ? cartCount : fleet.Carts[cartIndex].Count;
                for (int i = 0; i < Mathf.Min(24,count); i++)
                {
                    var local = new Vector3(cart.loadMin.x+.25f+i%columns*.5f,
                        cart.loadMin.y+i/(columns*rows)*.31f,cart.loadMin.z+.4f+i/columns%rows*.8f);
                    var p = pose.Point(local);
                    Show(new Vector3(p.x,local.y,p.y),Quaternion.LookRotation(new Vector3(pose.facing.x,0,pose.facing.y)));
                }
            }
            if (job?.Plan != null && job.Aircraft != null && job.Aircraft.CargoDoors.IsOpen(job.HoldConfiguration.cargoPoint))
            {
                foreach (var allocation in job.Packing.Allocations)
                {
                    var batch = allocation.Key;
                    for (int i = 0; i < allocation.Value.Length; i++)
                    {
                        int slot = allocation.Value[i]; if (slot < 0) continue;
                        bool show = batch.Location == job.Hold;
                        if (batch == job.TransferBatch)
                        {
                            float elapsed = job.TransferSeconds-i*job.TransferInterval;
                            if (job.TransferFrom == job.Hold && elapsed >= 0) show = false;
                            if (job.TransferTo == job.Hold && elapsed >= job.BagTravelSeconds) show = true;
                        }
                        if (show) Show(job.Packing.WorldSlot(slot),job.Packing.BagRotation(batch,i),null,Variant(batch,i));
                    }
                }
            }
            for (int bucket = 0; bucket < pools.Length; bucket++)
                for (int i = counts[bucket]; i < pools[bucket].Count; i++) pools[bucket][i].gameObject.SetActive(false);
        }
    }
}
